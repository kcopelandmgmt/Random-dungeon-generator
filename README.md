# Procedural Dungeon Generator

Seeded, graph-based dungeon layouts for an extraction-style dungeon game
(Albion Online hellgates × Path of Exile loot × Escape from Tarkov rounds).
Every round gets a fresh dungeon; the same seed always rebuilds the same one.

```
seed ─► RoomPlacer ─► GraphBuilder ─► CorridorCarver ─► RoleAssigner ─► Rasterizer ─► DangerMapper ─► FairnessMapper ─► DungeonLayout
```

See `docs/DESIGN.md` for the full explanation of how each stage works and why.

## Layout of this repository

| Path | What it is |
|------|------------|
| `Runtime/Core/` | Pure C# generator. No `UnityEngine` references, so it runs on a dedicated server, in unit tests and in the CLI. |
| `Runtime/Unity/` | Unity glue: `DungeonConfig` (ScriptableObject), `DungeonBuilder` (spawns prefabs), `DungeonGizmos` (scene-view debug). |
| `Tools/AsciiPreview/` | Console app that prints a dungeon as ASCII and batch-validates thousands of seeds. |
| `Tests/` | xUnit tests: determinism, connectivity, role counts, wall enclosure, 8-team fairness. |
| `package.json` | Unity Package Manager manifest so the repo can be installed straight into a project. |

## Installing into Unity

1. In Unity (2021.3 or newer) open **Window ▸ Package Manager ▸ + ▸ Add package from git URL** and paste this repository's URL.
   Or clone it and use **Add package from disk**, pointing at `package.json`.
2. Create a config: **Assets ▸ Create ▸ Dungeon Gen ▸ Dungeon Config**. Tweak sizes, room counts, loop chance, spawn/extract counts.
3. Add an empty GameObject, attach `DungeonBuilder` (and `DungeonGizmos` for the overlay), assign the config and some
   floor/wall/door prefabs (cubes are fine to start), then press Play or right-click the component ▸ **Build**.

`DungeonBuilder.SpawnPoints` and `ExtractPoints` give you world positions for squads and exit gates.
`DungeonBuilder.RoomAt(worldPos)` tells you which room a player is in, with its `LootTier` and `Danger`.

**Bosses, vaults, keys, classes.** Boss rooms carry an `Encounter` budget tuned for 2.5 players
(`CombatBaseline`); the final boss at the center is tuned for a geared full party and opens a
boss-gated exit (`Layout.BossExit`, `DungeonBuilder.OpenBossGatedExit()`). Vaults are locked
dead-end rooms (`TileType.LockedDoor`) with a key hidden elsewhere (`Layout.Keys`). Each boss room rolls one of eight boss
archetypes (never repeated in a dungeon) plus 0-2 affixes from the seed (`Room.Boss`). Six classes
are in `docs/CLASSES.md` and `ClassArchetype.DefaultRoster`. Design details in `docs/DESIGN.md`.

**Teams.** One spawn room per team, 1–16 supported. For more than 3–4 teams use the preset:
`DungeonSettings.ForTeams(8)` in code, or set *Preset Teams* on the config asset and right-click ▸ **Apply Team Preset**.
It switches to ring placement (spawns around the edge, extracts on an inner ring, vault in the center)
and keeps the fairest of four candidate layouts per seed. `Layout.Fairness` reports every team's walk
to its nearest extract and the vault.

For multiplayer: the server calls `DungeonGenerator.Generate(settings, seed)`, sends only the `int` seed to clients,
and every client calls `DungeonBuilder.Build(seed)` to reproduce the identical map.

## Running the preview tool (no Unity needed)

```bash
cd Tools/AsciiPreview
dotnet run -- --seed 42 --width 80 --height 50 --rooms 14
dotnet run -- --teams 8 --seed 7     # 8-team arena preset
dotnet run -- --batch 500            # generate 500 seeds, report failures / warnings / fairness / timings
dotnet run -- --help
```

## Running the tests

```bash
cd Tests
dotnet test
```

## Status

v0.1 — layout generation only. Not yet here: room interior decoration, loot table rolling,
enemy placement, locked/keyed gates, multi-floor dungeons. The `DungeonLayout` result
already carries the data those systems need (room roles, loot tiers, danger field, door positions).
