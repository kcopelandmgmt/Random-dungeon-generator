# Procedural Dungeon Layout System — Design

## Goal

Every round of the game takes place in a dungeon nobody has seen before. Squads enter through
separate gates, fight through rooms of increasing danger and reward, and must reach an
extraction gate to keep what they found. The layout generator has to deliver:

1. **Infinite variety** from a single integer seed.
2. **Determinism** so a server can send clients a seed and all of them build the identical map.
3. **Guaranteed playability**: every room reachable on foot, every extract reachable from every spawn.
4. **Extraction-shooter pacing**: spawns far apart, extracts deep, loot that scales with risk,
   and loops so there is always more than one way to get somewhere.
5. **Speed**: a few milliseconds, so it can run at match start without a loading screen.

## Pipeline

```
seed ─► 1 RoomPlacer ─► 2 GraphBuilder ─► 3 CorridorCarver ─► 4 RoleAssigner ─► 5 Rasterizer ─► 6 DangerMapper ─► 7 FairnessMapper ─► DungeonLayout
```

Each stage is a static class in `Runtime/Core/Stages/` that takes the previous stage's output.
Each stage draws its randomness from a *forked* RNG stream, so tuning one stage never reshuffles another.

### 1. Room placement (`RoomPlacer`)
Rejection sampling: pick a random rectangle (size between `MinRoomSize` and `MaxRoomSize`,
aspect ratio capped at 2:1) at a random position; keep it if it is at least `RoomSpacing` cells
from every room already placed. Repeat until `TargetRoomCount` rooms exist or
`MaxPlacementAttempts` is exhausted. Spacing of 3+ guarantees each room's one-cell wall ring
never touches another room's ring, so corridors always have somewhere to go.

### 2. Connectivity graph (`GraphBuilder`)
Rooms are nodes. Three steps:

* **Candidate edges** — each room links to its `NearestNeighbours` closest rooms (Manhattan distance between centers).
  This keeps corridors short and local; no room ever tunnels across the whole map.
* **Minimum spanning tree** — Kruskal's algorithm over the candidates yields a connected tree with no
  loops. If the k-nearest graph itself came out in disconnected clusters, the cheapest cross-cluster edge
  is added until one component remains. This is the connectivity guarantee.
* **Loops** — each candidate edge the MST rejected is re-added with probability `LoopChance`
  (if shorter than `MaxLoopDistance`). `LoopChance = 0` gives a pure tree (linear, roguelike);
  `0.15–0.3` gives tactical layouts with flanking routes; higher turns the map into a grid.

### 3. Corridor carving (`CorridorCarver`)
For every edge, pick a **door** on each room: the wall-ring cell (never a corner) nearest the
other room whose outward neighbour is open. Then run **A\*** on the cell grid from door to door.

* Room interiors and wall rings are impassable, so a corridor never slices through a room.
* An existing corridor cell costs 1 to step on; untouched rock costs `NewCorridorCost` (4 by default).
  Later corridors therefore prefer to merge into earlier ones, producing a shared tunnel network
  rather than parallel hallways. Lower the cost for a more cave-like, interwoven map.
* If strict A\* fails (a room boxed in by others), a lenient pass may cut through a wall ring at
  high cost. If that also fails the edge is dropped and a warning is recorded; the final validator
  catches any resulting disconnection.
* `CorridorWidth = 2` widens every step perpendicular to travel for 2-cell halls.

### 4. Role assignment (`RoleAssigner`)
Roles come from **walking distance along corridors** (corridor length plus room crossings, all-pairs
via Floyd–Warshall), never raw coordinates and never bare hop counts: two rooms can be one hop apart
through a 90-cell tunnel or a 6-cell one, and the player feels the difference.

| Role | Rule |
|------|------|
| **Spawn** × N | Ring mode: already placed on the outer ring (see *Scaling to 8 teams*). Fringe mode: the most peripheral room first, then farthest-point sampling so each new spawn maximizes its walk to the existing ones. |
| **Vault** × 1 | Ring mode: already placed at the center. Fringe mode: the room maximizing `minWalk × (minWalk / maxWalk)` across spawns, i.e. far from every team *and* about equally far. |
| **Extract** × M | Ring mode: already placed on the inner ring. Fringe mode: greedy, each pick maximizing the evenness (min/max) of every team's walk to its nearest extract, at least `MinExtractDepth` hops from any spawn. |
| **Hub** | Highest-degree remaining rooms (3+ exits). Where routes cross; the natural PvP flashpoints. |
| **Loot** | `LootRoomFraction` of remaining rooms, dead ends first, deeper first. Side objectives worth a detour. |
| **Standard** | Everything else. |

### 5. Rasterization (`Rasterizer`)
Paints a `TileType[,]` grid: `RoomFloor` for room interiors, `Corridor` for carved cells,
`Door` where a corridor meets a room, then `Wall` on every empty cell that touches a walkable one
(8-neighbourhood so corners close). Also fills `RoomIdAt[x,y]` so gameplay can ask "which room
am I in?" in O(1).

### 6. Danger mapping (`DangerMapper`)
A multi-source BFS from all spawn room cells over walkable tiles gives the **walking distance** of
every cell from safety (`DistanceFromSpawn[x,y]`). Each room's `Danger` is its doors' average
distance, normalized so the deepest room is 1.0. Loot rooms get +0.15, hubs +0.10, spawns are
pinned to 0 and the vault to 1. `LootTier` buckets `Danger` into `LootTiers - 1` grades; the vault
alone owns the top tier. This is the hook for the Path-of-Exile-style loot system: tier → rarity
weights, affix pools, item level. The same field drives enemy density and ambience.

### 7. Fairness (`FairnessMapper`)
One BFS per spawn over walkable tiles measures each team's walk to its nearest extract, to the vault,
and to the closest rival spawn. Each is summarized as a min/max ratio across teams (1.0 = identical
walks for everyone) and the score is the weakest of the three. The generator uses the score to choose
between candidate layouts; designers read it in the console / Inspector to judge a seed.

### Validation and retries (`LayoutValidator`, `DungeonGenerator`)
After generation a flood fill from a spawn room confirms every room is reachable and that spawn /
extract / vault counts were met. If not, the generator derives a new working seed from the requested
one and tries again (up to 8 times), always reporting the *requested* seed on the result so clients
stay in sync. In 1,300+ test seeds across four configurations no layout needed a retry.

## Scaling to 8 teams

A round can host 1 to 16 teams (`SpawnCount`), but past 3 or 4 teams the fringe-spawn approach
produces flat, unfair maps: spawns land anywhere, the map is 2–3 hops deep everywhere, and one team
can start 70 cells from the vault while another starts 388. Three changes make 8 teams work:

1. **Ring placement** (`SpawnPlacement.Ring`). Before any random room exists, the placer lays out an
   arena: the vault at the center, one compact spawn room per team on the outer ring at evenly spaced
   angles (random phase + jitter per seed), and the extract rooms on an inner ring
   (`ExtractRingRadius`, default 0.5) rotated half a slice so each sits *between* two teams.
   Random rooms then fill the gaps. Every team starts the same distance from the center.
2. **Radial spine edges.** Ring geometry alone did not help (fairness stayed ≈0.38) because the
   spanning tree could still route one team the long way round. `GraphBuilder` now forces
   *spawn → nearest extract → vault* edges into the tree before Kruskal runs, so every team's route
   inward is direct and similar in length. Loops and the k-NN edges still grow around the spine.
3. **Candidate selection** (`CandidateCount`). The generator walks a deterministic chain of derived
   seeds, generates up to N playable layouts, and keeps the one with the best fairness score. The
   requested seed is still what clients receive, so sync is unaffected.

`DungeonSettings.ForTeams(n)` bundles these with a canvas and room count scaled to the team count.

| Teams | Canvas | Rooms | Extracts | Avg fairness (worst of 200 seeds) | Time |
|------:|-------:|------:|---------:|----------------------------------:|-----:|
| 4 | 100² | 22 | 2 | 0.76 (0.56) | 11 ms |
| 8 | 152² | 34 | 4 | 0.75 (0.60) | 28 ms |
| 16 | 256² | 58 | 4 | 0.39 (0.24) | 130 ms |

For comparison, 8 teams with fringe spawns and no candidates scores 0.21 (0.07). Sixteen teams
generate and validate fine but are not tuned (four extracts for sixteen teams); raise `ExtractCount`
and `CandidateCount` if you go there.

## Output: `DungeonLayout`

| Member | Use |
|--------|-----|
| `Tiles[x,y]` | Render: Tilemap, prefabs, or mesh. Bake NavMesh from floors. |
| `Rooms` | Role, bounds, doors, neighbours, depth, danger, loot tier. |
| `Corridors` | Cells, endpoints, `IsLoop` flag (for door/gate placement or debug). |
| `RoomIdAt[x,y]` | Room lookup for triggers, loot tables, music. |
| `DistanceFromSpawn[x,y]` | Enemy density, fog, "far from safety" UI. |
| `Fairness` | Per-team walks to nearest extract / vault / rival, and min/max ratios. |
| `Warnings` | Non-fatal generation notes; log them in the editor. |

## Determinism and multiplayer

`DungeonRandom` is a self-contained xorshift128+ generator seeded via SplitMix64, so results are
bit-identical across platforms and .NET/Mono/IL2CPP versions (unlike `System.Random`, whose
algorithm has changed between runtimes). The server rolls a seed, generates the layout for
authoritative loot/AI, and sends only the `int` to clients, which rebuild the identical map locally.

## Tuning cheat sheet

| Want… | Change |
|-------|--------|
| More linear, roguelike | `LoopChance` ↓ (0–0.1) |
| More tactical, flanking | `LoopChance` ↑ (0.25–0.4), `NearestNeighbours` 5 |
| Cave-like merged tunnels | `NewCorridorCost` ↓ (1.5–2) |
| Separate clean hallways | `NewCorridorCost` ↑ (6–8) |
| Bigger, longer rounds | `Width/Height` ↑, `TargetRoomCount` ↑ |
| Squads meet sooner | `SpawnCount` ↑ or `MinSpawnSeparation` ↓ |
| More than 3–4 teams | `DungeonSettings.ForTeams(n)`, or `SpawnPlacement = Ring` + `CandidateCount` 4 |
| Longer, riskier extractions | `ExtractRingRadius` ↓ (extracts closer to the vault) |
| Fairer starts at a CPU cost | `CandidateCount` ↑ |
| Extracts feel earned | `MinExtractDepth` ↑ |

## Not in v0.1 (next steps)

* Room interior templates (pillars, pits, cover) chosen by room role and size.
* Locked gates / keys on loop corridors and vault doors (the graph already knows which edges are loops).
* Timed extract activation (Tarkov-style) using `Danger` as the schedule.
* Multi-floor dungeons: stack layouts, connect via stair rooms.
* Mesh / Tilemap renderer and NavMesh baking in the Unity layer.
