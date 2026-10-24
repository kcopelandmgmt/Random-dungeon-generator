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
| **FinalBoss** × 1 | Ring mode: placed at the exact center. Fringe mode: the room nearest the canvas center that is not next door to a spawn. The boss-gated exit spawns at its center cell. |
| **Boss** × B | Ring mode: on a third ring between the extracts and the center, on the extract angles. Fringe mode: deep rooms scored by `minWalk × (minWalk / maxWalk)` plus distance from bosses already chosen. |
| **Vault** × V | Built as satellite rooms (see *Vaults, keys and locked doors*), never selected. |
| **Extract** × M | Ring mode: already placed on the inner ring. Fringe mode: greedy, each pick maximizing the evenness (min/max) of every team's walk to its nearest extract, at least `MinExtractDepth` hops from any spawn. |
| **Hub** | Highest-degree remaining rooms (3+ exits). Where routes cross; the natural PvP flashpoints. |
| **Loot** | `LootRoomFraction` of remaining rooms, dead ends first, deeper first. Side objectives worth a detour. |

Keys are assigned in the same stage because it already has the walking-distance matrix; see below.
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
One BFS per spawn over walkable tiles measures each team's walk to its nearest extract, to the final
boss, to its nearest boss room, and to the closest rival spawn. Each is summarized as a min/max ratio across teams (1.0 = identical
walks for everyone) and the score is the weakest of the three. The generator uses the score to choose
between candidate layouts; designers read it in the console / Inspector to judge a seed.

## Bosses and encounter tuning

Boss rooms are a room role; what makes them *bosses* is the `EncounterBudget` attached to each
(`Room.Encounter`), computed by `EncounterTuner` from a `CombatBaseline`:

| Baseline | Default | Meaning |
|----------|--------:|---------|
| `PlayerDps` | 100 | Sustained DPS of one average player in tier-1 gear |
| `PlayerHealth` | 1000 | Effective HP of that player |
| `PartySustain` | 1.5 | Extra damage a coordinated party absorbs (healing, blocks, kiting) |
| `GoodGearMultiplier` | 1.4 | DPS *and* HP multiplier of tier-3/4 gear |
| `PartySize` | 3 | A full party |
| `TuningMargin` | 1.15 | The target group wins with 15% to spare |
| `RoomBossPlayerEquivalents` | 2.5 | Room bosses are balanced against this many baseline players |
| `RoomBossFightSeconds` / `FinalBossFightSeconds` | 60 / 90 | Target fight length for the target group |

**Model.** A group of *k* players with per-player DPS *d* and effective HP *h* fights a boss with
health *H* and damage *D*. The fight lasts *T = H / (k·d)*; the group survives if *D·T < k·h·μ*
(μ = party sustain). Choose *H* so the target group takes exactly the target time, then *D* so that
group wins by the margin:

```
H = k* · d · T*                D = k* · h · μ / (T* · margin)
```

Fight time scales as 1/k while damage capacity scales as k, so a group one player short takes
roughly (k*/k)² as much relative damage. That is what makes 2.5 a sharp line: a duo wipes, a trio
wins, and a well-geared duo (×1.4 on both stats, so ×1.96 relative) can just take it.

| Encounter | Tuned for | Health | DPS | Enrage |
|-----------|-----------|-------:|----:|-------:|
| Room boss | 2.5 players, baseline gear, 60 s | 15,000 | 54.3 | 120 s |
| Final boss | 3 players, good gear (×1.4), 90 s | 37,800 | 60.9 | 180 s |

`EncounterTuner.Simulate(budget, baseline, players, gear)` predicts the outcome; the tests pin
down the intended ones: solo and duo lose to a room boss, a trio wins; a baseline-geared trio
loses to the final boss, a geared trio wins, a geared duo does not. Enrage (boss DPS doubles after
2× the target time) stops a lone tank from out-sustaining a fight that should have needed a team.

**Full-loot PvP consequences.** Boss rooms are mid-ring, on the extract angles, so each pair of
teams shares a boss and an extract: the boss is a *third-party magnet* by design. A team that
commits 60 seconds to a boss is loud, stationary and low on cooldowns when the neighbours arrive.
Boss tier loot (`LootTiers - 2`) and a 50% chance of holding a vault key make it worth the risk.

## Boss types

Eight room bosses, two per mechanic family, and a dungeon never repeats one. With four bosses
drawn per 8-team dungeon that is 70 possible sets, and a given boss comes back every other round
on average; with the family-first draw every 8-team dungeon holds one of each family.

| Family | Boss | One-line fight | Answer |
|--------|------|----------------|--------|
| Space denial | **Ashen Colossus** | Slams leave permanent lava; the floor shrinks to islands | Ranged kiting, Warden on the last island |
| Space denial | **Glasswright** | Crystal walls partition the room and the team | Keep Mender with Reaver; Firewall through gaps |
| Summoner | **Hive Matron** | Skitterers every 8 s until she dies | AoE (Ignite, Piercing Shot), Warden holds the door |
| Summoner | **Bone Choir** | Raises every corpse in the room, players included | Burn corpses, interrupt Dirge |
| Chaser | **Hound of Vesh** | Pursues the lowest-health player into corridors, leashes after 8 s | Purge bleeds, taunt ends the chase |
| Chaser | **Lantern Wraith** | Hunts the richest inventory within two rooms, passes through doors | Spread loot before engaging; Veil breaks the target |
| Check | **Iron Juggernaut** | Tank check: telegraphed 60% hits, an interruptible overdrive | Shield Wall or rotate the hit; hold the interrupt |
| Check | **Plague Herald** | Healer check: stacking blight that never expires | Mender Purge, or out-race it |

Each archetype carries a small budget multiplier (0.85–1.2 on health or damage) because a boss
whose adds do half the work needs fewer raw numbers, and an arena size range that ring placement
honours when sizing its room (summoners want space for adds, chasers want less). The full design
text, including how each fight interacts with a third team arriving, is in
`BossArchetype.DefaultPool`. The center boss, **The Sovereign Below**, is a three-phase fight
outside the pool: a tank check, then a flooding arena with adds, then stacking blight under a
90-second enrage; one phase per role a full party has to prove.

### Affixes

Each room boss rolls 0, 1 or 2 affixes (40 / 40 / 20%) from a weighted pool, deterministically
from the seed. Affixes multiply the budget and change the tell, never the base mechanics, so the
2.5-player line holds: the tests check that every rolled boss is still beaten by a geared trio
and never by a solo.

| Affix | Effect | Loot |
|-------|--------|-----:|
| Armoured | +30% health | +1 roll |
| Frenzied | Enrage at 1.5× instead of 2×, +10% damage | +1 |
| Swift | +10% damage, faster movement (chasers catch) | |
| Venomous | +15% damage, every hit poisons | +1 |
| Thorned | +10% health, reflects 15% of melee | |
| Shrouded | Room is dark except boss and doorway | |
| Hoarder | No stronger, +2 loot rolls | +2 |
| Ancient (rare) | +20% health and damage, enrages later | +1 |
| **Keybearer** | Assigned, not rolled: every boss holding a vault key wears it visibly | key |

`BossRoller` runs after keys are placed: it fills any boss room without an archetype (fringe
mode), rolls affixes, pins Keybearer, and folds all multipliers into `Room.Encounter`.
`Room.Boss.DisplayName` gives the HUD string, e.g. *Hound of Vesh (Keybearer, Armoured)*.

## Final boss and the gated exit

The final boss room is the center of the dungeon (ring mode places it there; fringe mode picks the
room nearest the center that is not adjacent to a spawn). Every team is the same walk from it and
every spine route ends there. Killing it opens the **boss-gated exit** (`DungeonLayout.BossExit`):
a guaranteed extract at the room's center cell, as close to the center of the map as it is possible
to be. In Unity, `DungeonBuilder` instantiates `BossGatedExitPrefab` there disabled and
`OpenBossGatedExit()` reveals it; hook that to the boss's death.

Because the room is the one place all eight teams can reach equally, the final boss is also where
the round's endgame concentrates: the team that kills it gets the top loot tier and an exit nobody
else can use until they too kill... nothing, because the boss is dead. Whether the exit stays open
for everyone or closes behind the killing team is a gameplay decision (not a layout one); both are
one line of code at the prefab.

## Vaults, keys and locked doors

A vault is a locked treasure room: all of its doors are `TileType.LockedDoor`, its loot is the top
tier, and the only way in is the matching key.

* **Placement.** Vaults are never picked from the random graph, because a locked room with two
  doors would cut the dungeon in half for anyone without the key. Instead `RoomPlacer` builds each
  vault as a small **satellite room** a short gap away from a random ordinary host room, and
  `GraphBuilder` links it to that host and nothing else. Every vault is a guaranteed dead end.
* **Keys.** `RoleAssigner` places one key per vault. With `KeyFromBossChance` (default 0.5) the key
  is a boss drop; otherwise it is a floor chest in an ordinary / loot / hub room. The holder must be
  at least `KeyMinWalk` cells (default 40) from the vault, and among qualifying rooms the *closest*
  is chosen: a key three rooms over is a trip, a key across the map is a chore. Never a spawn,
  never inside a vault, never the same holder twice.
* **Output.** `DungeonLayout.Keys` lists `KeyPlacement { KeyId, VaultRoomId, HolderRoomId,
  DroppedByBoss, Cell, WalkToVault }`; `Room.RequiredKeyId` and `Room.HoldsKeyId` give the same
  from the room side. `DungeonBuilder` spawns `KeyMarkerPrefab` at each key cell and
  `LockedDoorPrefab` on each locked door.
* **Validation.** The validator flood-fills with locks closed and requires every non-vault room to
  be reachable, then checks each vault's locked door opens straight onto reached space, each vault
  has a key, and no key is inside a vault.

Keys are inventory items. In full-loot PvP that means a key is also a bounty: whoever holds it is a
target, and killing the holder is the other way into the vault.

## Player stats

Five primary attributes, everything else derived. Ten is an average adventurer; a class starts
with 50 points over the five, gear tiers add a few more, and `StatFormulas` holds every number
in one place so a rebalance is one file.

| Attribute | Drives |
|-----------|--------|
| **Strength** | Melee / Strength-kit damage, carry capacity (30 kg + 2 kg per point; full loot makes this a real stat) |
| **Dexterity** | Ranged / finesse damage, crit chance (+0.5%/pt), attack speed (+2%/pt), move speed (+1%/pt), stamina |
| **Intelligence** | Spell damage, mana (+5%/pt) |
| **Constitution** | Health (+5%/pt: 1000 at 10) |
| **Spirit** | Healing power (+5%/pt), resource regen, +2 magic resistance rating per point to every non-physical type |

Derived: health, stamina, mana, DPS (`kit coefficient × primary-attribute scaling × gear`),
healing, crit, attack speed, move speed (armour weight: light 100%, medium 95%, heavy 90%),
carry weight, armour rating and five resistances. `PlayerStats.For(class, gearTier)` builds the
whole sheet; `TakeHit(amount, type)` applies mitigation.

**Anchor.** All-10 attributes in tier-1 medium gear with a 1.0 kit is exactly the
`CombatBaseline` player: 1000 health, 100 DPS. The class multipliers used by boss tuning are no
longer stored; they are derived from each class's attributes and kit coefficient, and the roster
still averages to 1.0 on both (test-enforced). Gear tier 3 multiplies DPS by about 1.45 and health
by about 1.2, which is where the "good gear ×1.4" in the baseline comes from.

**Mitigation.** Armour and each resistance are ratings; damage reduction is
`rating / (rating + 100)`, capped at 75%. 100 rating = 50%, 300 = 75%. Negative ratings mean
extra damage on the same curve, so a weakness is never an instant kill. Armour weight trades
physical for magic: heavy armour is 100 armour and 0 resistance (50% physical, 0% magic), light
is 30 armour and 25 resistance (23% physical, 20% magic), medium sits between.

## Damage types and resistances

Six types: **Physical, Fire, Frost, Lightning, Poison, Shadow.** Physical is mitigated by armour,
the other five by their resistance. Every damage source carries a `DamageProfile` (fractions per
type that sum to 1), so a hit from the Ashen Colossus is 70% fire and 30% physical and each part
is mitigated separately.

| Boss | Deals | Retinue mob |
|------|-------|-------------|
| Ashen Colossus | 70% Fire / 30% Physical | Ember Wisp |
| Glasswright | 50% Lightning / 50% Physical | Storm Acolyte |
| Hive Matron | 60% Poison / 40% Physical | Skitterer |
| Bone Choir | 70% Shadow / 30% Physical | Gravewalker |
| Hound of Vesh | 100% Physical | Bandit |
| Lantern Wraith | 50% Shadow / 50% Frost | Frostbound |
| Iron Juggernaut | 80% Physical / 20% Lightning | Hollow Knight |
| Plague Herald | 100% Poison | Carrion Crawler |
| The Sovereign Below | 40% Physical / 30% Frost / 30% Poison (one per phase) | – |

Because a dungeon draws one boss per family, every 8-team dungeon has at least four dominant boss
damage types in it, and the resistance a team stacks for one boss is the wrong one for the next.

## Mobs

Eight ordinary enemy types, each dealing one dominant damage type (all six covered, physical
twice), resisting its own type (60%) and weak to its opposite (+50%). `MobPlacer` stations one
pack in every ordinary, loot, hub and vault room:

* **Budget.** Each pack is sized in player-equivalents from the room's danger: 0.5 PE next to a
  spawn, 2.0 at the deepest room, +0.5 for loot rooms and vaults. A room boss is 2.5 PE, so the
  deepest ordinary room is a real fight for a trio and the first room out is a warm-up. Mob health
  and DPS also scale 0.8× to 1.4× with danger.
* **Spread.** Each pick is weighted toward the damage types the dungeon has used least, so every
  layout contains at least five of the six types among its mobs (test-enforced).
* **Retinue.** Rooms adjacent to a boss draw that boss's retinue 75% of the time, so the approach
  telegraphs the fight: skitterers mean the Matron is next door.

| Mob | Deals | Resists | Weak to | Note |
|-----|-------|---------|---------|------|
| Bandit | Physical | Physical | Shadow | Uses doors like a player; loots corpses if left alone |
| Hollow Knight | Physical / Shadow | Physical | Fire | Armoured, blocks a doorway |
| Skitterer | Poison / Physical | Poison | Fire | Fast, packs of six or more |
| Ember Wisp | Fire | Fire | Frost | Floats over hazards, lights the room |
| Frostbound | Frost / Physical | Frost | Fire | Slows on hit |
| Storm Acolyte | Lightning | Lightning | Physical | Caster, chains between grouped players |
| Carrion Crawler | Poison / Physical | Poison | Lightning | Eats corpses and their loot |
| Gravewalker | Shadow / Physical | Shadow | Fire | Rises again once unless burned |

`Room.Mobs` and `DungeonLayout.MobPacks` carry the result; `MobPack.TotalHealth / TotalDps`
give absolute numbers against the baseline.

## Gear

The Path of Exile layer: random items with rolled affixes, rarity that scales with depth, and
boss-themed uniques. Everything an item does is expressed through the stat system above, so a
new affix is one line in a table and never a new formula.

**Slots.** Nine: weapon, head, chest, hands, legs, feet, amulet, two rings. Armour pieces carry a
weight (light / medium / heavy) and a share of the set's armour (chest 35%, legs 25%, head 15%,
hands and feet 12.5% each). Anyone can wear anything; mixing weights averages the move-speed
penalty by share. Weapons scale from one attribute (swords and axes from Strength, bows and
daggers from Dexterity, staves from Intelligence, sceptres from Spirit) and deal one base damage
type; a class whose primary does not match its weapon scales at 75%.

**Item level.** The room's loot tier (0–4). Base armour and weapon damage scale ×(1 + 0.1 per
level); affix *tiers* follow the level (T1 at level 0–1 up to T4 at level 4, with a 30% chance
of one tier lower so high drops still vary).

**Rarity.**

| Item level | Normal | Magic | Rare |
|-----------:|-------:|------:|-----:|
| 0 | 70% | 28% | 2% |
| 1 | 55% | 38% | 7% |
| 2 | 40% | 42% | 18% |
| 3 | 25% | 45% | 30% |
| 4 | 10% | 40% | 50% |

Sources set a floor and a unique chance: ordinary chests use the table as-is; loot rooms and
bosses never drop Normal; vaults and the final boss never drop below Rare; bosses have an 8%
unique chance, vaults 15%, the final boss 35%. Uniques never come from the table itself.

| Rarity | Affixes | Name |
|--------|---------|------|
| Normal | none | base name |
| Magic | 1–2, at most one prefix and one suffix | *Sharp Longsword of the Bear* |
| Rare | 3–4, at most two prefixes and two suffixes | two-word name, *Grim Bane, Plate Greaves* |
| Unique | fixed set, item level ≥ 3 | its own name and flavour |

No item carries two affixes of the same family (two fire resists, two Strength rolls).

**Affixes.** Prefixes are the item's core; suffixes are the extras.

| Prefix | On | T1 … T4 |
|--------|----|---------|
| +% damage | weapon | 6–9 … 21–25 |
| +% of damage as Fire / Frost / Lightning / Poison / Shadow | weapon | 6–8 … 17–20 (adds to the attack profile) |
| +% armour | armour | 8–12 … 31–40 |
| +% health | chest, legs, amulet, ring | 3–4 … 9–10 |
| +all resistances | armour, jewellery | 4–6 … 16–20 |

| Suffix | On | T1 … T4 |
|--------|----|---------|
| +resistance (one type) | armour, jewellery | 8–12 … 31–45 |
| +attribute (one of five) | any | 1 … 3–4 |
| +% crit | weapon, hands, ring | 1 … 4–5 |
| +% attack speed | weapon, hands, ring | 3–4 … 9–10 |
| +% move speed | feet, ring | 2 … 5–6 |
| +kg carry | armour, jewellery | 4–6 … 13–16 |
| +% healing | weapon, amulet, hands | 5–7 … 14–16 |

**Uniques.** Eight, one per boss plus the Sovereign's, each themed on its source and 60% likely
to drop from that boss when a unique rolls there. Every unique carries two or three ordinary
fixed affixes *and one legendary affix*: a behaviour that no Magic or Rare item can roll.

Legendary affixes (`LegendaryAffix`) are a separate type from the affix pool, not a low-weight
entry in it, so they are unique-only by construction. Each has a kind, a trigger (passive, on hit,
on kill, on taking a hit, on heal, on lethal hit), a chance, a magnitude and a cooldown. The three
numeric kinds (lifesteal, thorns, resistance cap) fold straight into the character sheet; the
rest are handed to the game as a list of triggers on `PlayerStats.Legendaries`.

| Unique | Slot | Legendary | Fixed affixes |
|--------|------|-----------|---------------|
| Sovereign's Crown | heavy head | **Unyielding**: resistance cap 85% instead of 75%, every type | +3 STR, +3 CON, +3 SPI, +25 all resist |
| Ashen Heart | amulet | **Cinder Pulse**: 15% on hit, burst of 50% weapon damage as Fire in 3 m | +60 fire resist, +30% as fire, −20 frost resist |
| Matron's Carapace | medium chest | **Brood Mother**: 10% when hit, a Skitterer fights for you for 10 s | +35% armour, +45 poison resist, +8% health |
| Hound's Fang | twin daggers | **Bloodhound**: 8% lifesteal | +25% damage, +10% move speed, +4 DEX |
| Glasswright's Prism | ring | **Refraction**: 20% of damage taken reflected as Lightning | +15 all resist, +20% as lightning, +3% crit |
| Choirmaster's Mantle | light chest | **Last Verse**: survive a killing blow at 1 health, once per 120 s | +15% health, +50 shadow resist, +15% healing |
| Herald's Censer | censer | **Miasma**: heals you cast also deal 30% as Poison to enemies near the target | +25% healing, +30% as poison, +3 SPI |
| Wraithlight Boots | light feet | **Phase Step**: enemies cannot body-block you; +15% speed while passing through one | +12% move speed, +35 shadow and frost resist |

In full-loot PvP every one of these is a target call: a Reaver with Bloodhound is the one to
focus, a Choirmaster's Mantle means the first kill shot does not count, a Sovereign's Crown on
the ground is why teams fight over the center.

**Loot per room** (`LootPlacer`, rolled from the seed, server-authoritative): ordinary rooms 1–2
chest items; loot rooms 3 (Magic floor); boss rooms 2 plus the boss's affix bonus rolls; vaults 4
(Rare floor); the final boss 4 (Rare floor, 35% unique each). `Room.Loot` and
`DungeonLayout.Items` carry the result. On the 8-team sample seed that is 48 items: 8 Normal,
17 Magic, 19 Rare, 4 Unique.

**Group rolls** (`LootRollRules`, `MatchState.OpenRolls`): when a chest is opened by a player
with living teammates, every Magic+ piece in it goes to a need-over-greed roll among that team.
Need, Greed or Pass within 20 s, silence is a Pass; any Need beats any Greed; among Needs a d100
with +50 if the piece fits the player's current class (spec weapon, armour weight; jewellery
fits all). Only teammates within `RollRangeRooms` (2) corridor hops of the chest when it opens are
in the roll; `PlayerEnteredRoom` keeps `MatchState.PlayerRoom` current and a resurrection places
you at the shrine. The server resolves with its own RNG and publishes `LootRollResolved`; the winner
alone may take the piece, unless everyone passed. Enemies are never bound: taking a piece
mid-roll ends the roll, and a corpse is a corpse. Solo players never roll.

**Equipment and the sheet.** `Equipment` holds nine slots; `Summarise()` folds every item into a
`GearSummary` (weapon damage and profile, armour, resistances, attribute bonus, percentages),
and `PlayerStats.For(class, equipment)` builds the sheet from it. The anchor still holds: a full
Normal tier-0 set of the class's weight equals the abstract tier-0 sheet exactly (test). A full
nine-slot Rare tier-3 set lands 10–40% above the abstract tier 3 the final boss is tuned for;
that is the top end of luck and the margin a fully-geared trio earns. `--wear` in the preview
tool dresses each class in a rolled set and prints both sheets.

## Durability: the gear sink

Full loot alone does not shrink the amount of gear in the world; it only moves items from the
loser to the winner. Without a sink, every player's bank fills with spare sets and the loot stops
mattering. Two sinks make every item finite, and **there are no repairs** (`DurabilityRules`):

1. **Wear from use.** Every item has a maximum durability (Normal and Magic 100, Rare 120,
   Unique 150) and loses points with use: armour pieces 1 per 100 raw damage taken, weapons 1 per
   200 damage dealt, jewellery a flat 15 per round, and everything 5 per extraction for the trip.
   At 0 the item is *broken* for good: an armour piece gives nothing, a broken weapon is fists
   (half damage). A broken item can still be sold or salvaged; it just cannot be worn again.
   Found gear is used gear: ordinary chests hold items at 60–100% durability; bosses, vaults and
   the final boss hand out fresh ones.
2. **Death trashes half.** When a player dies, each worn item is destroyed with 50% probability
   and the rest drop for the killer. Keys always drop.

Under the reference round (2,500 damage taken, 6,000 dealt: two mob packs, a boss, a skirmish),
an item in constant use whose owner never dies lasts:

| Rarity | Armour piece | Weapon | Ring / amulet |
|--------|-------------:|-------:|--------------:|
| Normal / Magic | 3 rounds | 3 | 5 |
| Rare | 4 | 3 | 6 |
| Unique | 5 | 4 | 8 |

With deaths in the mix the real numbers are lower still. Every item is a few rounds of use and
then a decision: sell it or break it down.

## Vendor, salvage and crafting

Two ways an unwanted or worn-out item leaves the game, and one way materials come back as gear
(`CraftingRules`, `Crafter`):

**Sell.** A vendor pays silver proportional to the item's value (rarity × level) and condition;
broken gear sells at a quarter. Plain Normal items are worth more sold than salvaged.

**Salvage.** Any item that is not destroyed, broken or not, breaks down into three materials:

| Material | From | Used for |
|----------|------|----------|
| **Scrap** (untyped) | every item: 4 × (level + 1) × rarity multiplier (Normal 1, Magic 1.5, Rare 2.5, Unique 4) | the price of every craft |
| **Essence** (typed by affix family: `Resist:Fire`, `Attribute:Strength`, `Health` …) | one per affix on the item, two for a T3+ roll | guaranteeing that family on a craft |
| **Prime Shard** | 1 per Rare, 3 per Unique | forcing a crafted item to be Rare |

**Craft.** The player chooses a *base* (slot, weight, weapon type) and an *item level* and pays
scrap: 10 / 40 / 90 / 160 / 250 for levels 0–4. Optionally they pin up to two affix *families* at
3 essences each, which must be legal on that slot, and spend 3 shards to force Rare. Then the
forge rolls exactly as a drop would:

* rarity from the drop table at that level with Normal removed (or Rare if forced),
* the pinned families present at a random tier and value,
* every other affix slot filled randomly with the usual family exclusion and prefix/suffix caps,
* a fresh item at full durability, never Normal, never Unique.

That is the "somewhat random" line: a crafter can say *fire resistance on a ring, level 4, Rare*
and will get it, but not which tier, not what else is on it, and not the second and third
affix. Uniques never come from the forge; a salvaged unique is three shards and its essences.
`--craft` in the preview tool salvages a whole dungeon's loot and crafts from the bag.

## Status effects and elements

Every element has an on-hit status, and players, mobs and bosses all apply from the same list
(`StatusEffectDefinition`, `StatusTracker`). A status is data: kind, element, duration,
magnitude, stack cap, what it becomes at the cap, whether it spreads or chains, and what it
combos into.

| Status | Element | Effect | Stacks |
|--------|---------|--------|--------|
| **Burn** | Fire | 15% of the hit per second for 4 s; spreads to enemies within 2 m | 3 |
| **Chill** | Frost | 15% slower per stack for 3 s | 3 → **Freeze** (rooted, 1 s) |
| **Stagger** | Lightning | 0.5 s interrupt; chains to two enemies within 4 m | on a Chilled target → **Stun** (1.5 s) |
| **Poisoned** | Poison | 5% of the hit per second per stack for 8 s, refreshed by hits; Purge clears | 5 |
| **Weaken** | Shadow | heals received and damage dealt −25% for 5 s; marked | 1 |
| **Bleed** | Physical (abilities and brawlers only) | 8% of the hit per second per stack for 5 s | 3 |

Rules the tracker enforces: resistance shortens a status of its element by the same fraction
it mitigates damage (50% fire resist is a 2 s burn; the Sovereign's Crown's 85% cap is 0.6 s);
Stagger consumes the Chill it stuns through; the only hard controls are Freeze (three Chills)
and Stun (Stagger on Chill), and both need two steps, so nobody is stun-locked from range by
one button. Mobs apply their dominant element's status (Frostbound Chill, Skitterers Poison,
Bandits Bleed); bosses likewise (the Colossus Burns, the Hound Bleeds, the Herald Poisons).

**The Elementalist** is the class built on this: its element is the weapon in hand, and each of
Fire, Frost and Lightning has a kit on the same skeleton (bolt, zone, mobility, passive). See
`docs/CLASSES.md`. Because mobs resist their own element, a specialised caster has rooms that
punish them, and because the element is a staff, full loot can take it away.

## Classes

See `docs/CLASSES.md`. The six-class roster (`ClassArchetype.DefaultRoster`) averages to exactly one
baseline player, so the boss numbers above assume an average trio. `PlayerClassDefinition` is the
Unity asset form.

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

| Teams | Canvas | Rooms | Extracts | Bosses | Vaults | Avg fairness (worst of 150 seeds) | Time |
|------:|-------:|------:|---------:|-------:|-------:|----------------------------------:|-----:|
| 3 | 120² | 19 | 3 | 3 | 1 | 0.71 (0.54) | 13 ms |
| 4 | 120² | 22 | 4 | 4 | 1 | 0.69 (0.48) | 14 ms |
| 8 | 156² | 34 | 4 | 4 | 2 | 0.72 (0.62) | 26 ms |
| 16 | 252² | 58 | 4 | 4 | 4 | 0.42 (0.30) | 124 ms |

Up to four teams every team gets its own extract and boss; beyond that pairs of teams share. The
fairness score now also includes each team's walk to its nearest boss room.

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
| `Fairness` | Per-team walks to nearest extract / final boss / nearest boss / rival, and min/max ratios. |
| `Keys` | Where each vault key is and what it opens. |
| `BossExit` | Room and cell of the exit that opens when the final boss dies. |
| `Room.Encounter` | Boss health, DPS, target time and enrage for Boss / FinalBoss rooms. |
| `Room.Boss` | Archetype, rolled affixes, display name, damage profile. |
| `Room.Mobs` | Mob pack: type, count, tier multiplier, player-equivalents. |
| `Room.Loot` | Items rolled for the room's chests or boss drop. |
| `Warnings` | Non-fatal generation notes; log them in the editor. |
| `Settings` | The settings the layout was generated with (for `ClientView.Redact`). |

## Round rules, souls and shrines (locked)

`docs/DECISIONS.md` has the full list. The ones with code behind them:

* **20-minute round, extracts open at 4** (`MatchRules`). `RoundEnded` kills everyone still
  inside and drops their keys: the clock is a weapon.
* **Resurrection.** `PlayerSpawned` gives the state the roster; `PlayerDied` with a player killer
  grants the killer one soul (never for teammates) and burns every soul the victim carried; opening
  a boss chest first grants its opener one. Souls are per player. `PlayerResurrected` costs the
  reviver two of their own souls, needs a living teammate at a shrine room, works once per player
  per round, and brings the player back **naked**. Shrines are placed by `ShrinePlacer` in every hub (at least one per
  dungeon) and shown as † in the ASCII view.
* **Keys die on extraction.** `PlayerExtracted` destroys every key the player held; a destroyed
  key cannot be picked up again.
* **Weight-only inventory.** `ItemBase.WeightKg`, `Equipment.WornWeightKg`, `CarryRules`.
* **Consumables** (`Consumable.All`): Health Potion, Antidote, Salvage Kit; sold by the vendor,
  found in ordinary chests (`Room.Consumables`).
* **Teams of 1–3**, one queue; **no spawn protection**; **no brackets**; **vendor only**.

## Persistence, inventory, vendor, matchmaking (locked, built)

* **Flat characters** (`Profile/Character`): a name, a class, the class's attributes (a read-only
  property so nothing can grow it), and equipment. No level, no XP. A test asserts the properties
  do not exist. **`PlayerProfile`** holds what persists: silver, materials, the stash, consumables,
  characters, cosmetic/recipe unlocks.
* **Weight-only inventory** (`Profile/Inventory`): worn kilograms plus a bag of loot and
  consumables against `CarryKg`; overload slows (`CarryRules`), never refuses. Death drops the bag
  and half the worn gear (the other half destroyed), keys always; extraction stashes the bag, wears
  the worn gear for the trip, and destroys keys.
* **Vendor** (`Profile/Vendor`): the only trade. Sells the three consumables and 20-silver Normal
  starter pieces (how a naked character re-equips); buys any item; salvages and crafts into the
  profile's materials and stash.
* **Matchmaker** (`Net/Matchmaker`): one queue, groups of 1–3 become teams, eight teams per match,
  or at least four after the oldest group has waited 90 s. No gear brackets. Seeds from a server counter.
* **Round clock** (`Net/RoundClock`): fires `RoundStarted`, `ExtractsOpened` (4:00) and
  `RoundEnded` (20:00) once each from the rules.
* **Weapon specs** (`Combat/WeaponSpec`): two per class by weapon base, the Elementalist's three
  by element; `PlayerStats.Spec` / `SpecName` / `Abilities` resolve from the weapon in hand. The
  Breaker is a bruiser and never breaks a lock. Table in `docs/CLASSES.md`.
* **Theme and palettes** (`Presentation.cs`): `DungeonTheme.Default` is the one launch look;
  `ElementPalette.Standard` and `.ColourBlind` give every effect its colour.

## Moment-to-moment rules (locked, built)

`docs/DECISIONS.md` has the list; the code: `CombatRules` (full friendly fire; 8 s extraction
channel reset by damage; chests and corpses open to anyone), `MobRules` (leash, attack anyone,
reset, no respawn), `AudioCues` (sound radii: the second vision system), `TeamPing` (the only
comms), `Vendor.ClaimStarterKit` (the free floor), `CraftingRules.SilverFee` (the silver sink),
`Season` (leaderboard of value extracted, cosmetic rewards), `DungeonSettings.ForTutorial` +
`MatchRules.Tutorial` (the first dungeon). In `MatchState`: brought-in gear is registered with
server ids; death moves everything held onto a corpse anyone can open after the server rolls
which worn half is destroyed; extraction needs a channel started at that gate, undamaged, long
enough ago; the round end destroys whatever was left inside; a team's minimap is the rooms the
server has told it it saw; a shrine sells a consumable for one of your own souls.

## Silver, regen and ultimates (locked, built)

Silver lies in every room with a mob pack or a chest (`Room.Silver`, rolled by `LootPlacer`,
weightless), is picked up once per room, drops in full on death and is destroyed if left inside.
Health never regenerates on its own; Stamina and Mana do. Each spec has one **earned ultimate**:
two of your own souls at a shrine arm it for one use, it dies with you and never leaves the
dungeon (`WeaponSpec.Ultimates`, `UltimateArmed` / `UltimateUsed`). The matchmaker's match carries
settings sized to the teams it formed.

## Fight rules (locked, built)

`FightRules` holds them; `docs/DECISIONS.md` has the list. Abilities cost Stamina (physical
classes) or Mana (casters) and have cooldowns, basic attacks are free. Zero health is a 60-second
downed state: an 8-second friendly revive channel, or 50% of max health from an enemy to finish.
Chests, corpses and keys are 2 / 3 / 1-second channels broken by damage. Mobs hit the nearest,
taunts override. Hard control on players diminishes full / half / quarter / immune within 10 s.
No sprint. One of each unique. A new account is the free kit, 200 silver and two potions. The
round opens with a 10-second sealed countdown. `MatchState` validates all of it; `RoundClock`
raises `DoorsOpened`.

## Death recap, round summary, reports, regions, strings (locked, built)

`HitLog` keeps the last five hits per victim outside the event log; `DeathRecap.For(state,
victim, hits)` gives killer class and spec (never a name), finished or killed, downed seconds and
the hits. `RoundSummary.For(state, player)` walks the log for kills, boss kills, souls earned and
spent, extraction value and the account XP earned. `ReportQueue.Submit` dedupes per reporter,
accused and match; `ReadingOrder` is for a human. `Region.Pick(latencies, override)` chooses the
queue region; `QueuedGroup.Region` and `Tier` key the matchmaker. `MatchRules.Backfill` is false
and `PlayerSpawned` is refused once the doors are open. `Season.Starting` makes 91-day seasons.
`Strings.Get(key)` serves every player-facing string with English fallback.

## Mob and boss behaviour (built, first tuning)

`MobController.Step(sim, dt)` is the mob state machine (`MobState`): acquire (taunt, pack target,
nearest in sight inside `GridPath.WithinLeash`), hold a doorway or keep range by `MobTraits`, cast
`Mob.basic` or `Mob.ranged`, chase, return and reset. `BossController.Step` picks a target by
archetype, runs the mechanic timer (`Mechanic`, `Secondary`, `OnPhase`, channel results in
`Resolve`), slams when crowded, swings, chases within the leash (Chasers for eight seconds beyond
it), and enrages. `EncounterDirector` owns a room's controllers and `Drain`s match events through
`FightBridge` with the pack and boss room lookups. `FightLab.BossFight` and `PackFight` run them in
generated rooms; the console tool exposes `--sim bosses`, `--sim packs` and `--sim roomtrace`.

## The ability and damage pipeline (built, first tuning)

`AbilityDefinition` is the unit; `AbilityBook.All` holds every ability, `AbilityBook.For(spec)`
a spec's five. `FightSim` holds `Combatant`s (players from `PlayerStats`, mobs and bosses from
the tuner's budgets) on a layout: `Cast(caster, abilityId, aim)` validates (alive, not hard
controlled, right spec, armed ultimate, cooldown, resource, range, sight), selects targets by
shape (`SelectTargets`, also exposed as `Preview` for brains and HUDs), spends and starts the
cooldown, then `Affect`s each target: `Strike` (passives, crit, stances, mitigation in
`ApplyDamage`, statuses in `ApplyStatus` with spread and chain, lifesteal, thorns), `Heal`,
buffs, knockback. `Tick(dt)` regenerates resources, flushes damage over time once a second,
pulses zones, bleeds out the downed and expires buffs. `Move` respects Chill, roots and solids.
`FightBridge.ToMatchEvents` maps the simulator's downed, finished, died and revived to
`MatchEvent`s. `FightLab` builds arenas and runs duels, team fights and party-versus-boss with
`SimpleBrain`; the console tool exposes it as `--sim`.

## Interiors, doors and hazards (locked, built)

`InteriorPlacer.Assign` runs after the rasterizer: it paints `TileType.Pillar` and
`TileType.LowWall` inside rooms (never spawns or extracts), keeps a clear ring inside the walls,
the centre and every door approach, and undoes any feature that fails `Connected`. `HazardPlacer`
then places `Hazard`s by `DepthFromSpawn`, painting pits as `TileType.Pit`; `HazardRules` holds
damage fractions, the Shade-only disarm and visibility. Both are hashed into the public
fingerprint and reach clients with the layout. `LineOfSight` treats pillars and closed doors as
opaque and low walls and pits as clear; `GridPath` treats all three tiles and closed doors as
impassable. `LayoutIndex` numbers doors from `DoorRules.IdBase` and hazards from
`HazardRules.IdBase`. `MatchState` validates `DoorClosed` / `DoorOpened` (reach, channel) and
`HazardTriggered` / `HazardDisarmed` (armed, Shade, room, channel); `TeamView` carries closed doors
near explored rooms and the hazards the team may see; the minimap marks all three.

## Mutators, events and bots (locked, built)

`Mutator.ForSeason(n)` is null for season one and rotates through five from season two;
`MatchDescriptor.CreateForSeason` applies it to copies of the rules and settings, and the rules
travel in the descriptor (`SoulMultiplier`, `ShrinesUsable`, `ResurrectionCost`, `SightRangeCells`,
`MutatorName`). `DungeonEventPlan.For(descriptor, layout)` picks the round's event from a fork of
the public seed; `RoundClock` raises `EventStarted` at the halfway mark and `EventEnded` a minute
later for timed kinds; `MatchState` enforces the elite's room, the unsealed vault's door, the cheap
shrine's price (`ResurrectionCostAt`), and drops the relic (`LayoutIndex.EventLootId`, rolled with
the loot secret) on the floor on `EliteKilled`. `BotDirector.Step(now)` yields the tutorial bots'
events: spawn, room hops along the shortest room path, chest channel and opening, Need rolls, the
server-side roll resolution and one item each.

## Compass and minimap (locked, built)

`Compass.Bearing` measures from +Y clockwise (0 north, 90 east). `CompassBar.Markers(state,
player, cell, teammateCells, pings, now)` returns bearings and distances for teammates, live pings
(`TeamPing.LifetimeSeconds` = 8) and every explored extract, boss, shrine, vault, final boss and
open exit, with state-aware labels; `CompassBar.Callout` turns one into a line of speech.
`MiniMap.Build` returns explored rooms, corridors between explored pairs and markers; the corner
view is `MiniMapModel.ViewRadiusCells` (18) around the player, the full map ignores it. Both
Unity HUDs (`CompassHud`, `MiniMapHud`) draw with OnGUI for the greybox and read only these models.

## Dungeon tiers, gear score and account levels (locked, built)

Three tiers (`DungeonTier.All`), walled by gear score both ways: Tier I 0–60, Tier II 40–220,
Tier III 160+. `GearScore.Of(worn, carried)` sums `Item.Value` (item level × rarity weight) over
everything a character brings in. `DungeonTier.Apply(settings)` stamps `Tier`, `ItemLevelOffset`
(+0/+2/+4 on every drop), `UniqueChanceMultiplier` (×1/×1.5/×2) and scales the combat baseline so
mobs and bosses are tuned for geared players. `QueuedGroup.Tier` keeps queues apart; `FormedMatch`
applies the tier. The settings codec is version 2 and round-trips the three fields.

Account levels (`AccountRules`): XP for extractions, kills and bosses, never time. Level 5 opens
hand-offs (`ItemGiven`, same room, both parties past the gate; `AccountLevelReported` carries the
level into the match) and the shared bank; Reaver, Elementalist and Shade open at 10/15/20.
`MatchState.Transfers` records every hand-off for `TransferLedger.Flagged()`. `ItemDropped` puts
an item on the room floor (`FloorItems`) for anyone standing there; the floor is destroyed at round
end. A chest's silver splits across `TeammatesNear` on `SilverPicked`. Dead players may
`PlayerLeft`, forfeiting resurrection. `Announcer.For` yields text for exactly two events.
`Season` keeps four `LeaderboardKind` boards per tier and in total. `Monetisation`: free to play,
cosmetics only. `Roadmap.SeasonTwo`: weekly contracts, a possible fourth tier.

## Technical stack and the vertical slice (locked)

`TechStack` states them: Unity 6 + URP, Fishnet over `IMatchTransport`, managed dedicated
servers, PlayFab, grid A\* on the server (`GridPath`), stylized low-poly, PC first, menus for a
hub, one developer. `VerticalSlice` is the first build: 4 teams, Warden / Reaver / Elementalist
(Pyromancy), Iron Juggernaut and Hive Matron, the 4-team preset with `AllowedBosses` restricting
the draw. Disconnected characters stand in the world for 60 s and reconnect by replaying the log.
Eight character slots share one bank.

## Presentation: top-down, 3D, line-of-sight (locked)

See `docs/DECISIONS.md` for the reasoning. The short form: a 3D scene viewed from a fixed-yaw
camera pitched 55–60° (`TopDownCamera`), one cell = 2 m (`DungeonBuilder.CellSize`), **WASD plus
mouse aim** as the only control scheme (`MovementRules`: 5 m/s base, screen-relative, facing
follows the mouse, no click-to-move, no universal dodge) with floor telegraphs, and **line-of-sight fog**: walls and locked doors
occlude, you see what your character could see. The vision rule is in the core (`LineOfSight`:
`CanSee`, `VisibleCells`, `VisibleRooms`) so the server can enforce it, use it to decide what to
replicate to whom, and settle "did they see that" disputes, and the client draws the same answer.
From a doorway you see the room you are leaving and the one ahead, at most three; a locked door is
seen, never seen through.

## Multiplayer architecture

The generator was built deterministic so that the dungeon never has to cross the wire. The
`Runtime/Core/Net` layer turns that into a working model (`Net/` is still pure C#; the Unity
adapter is `IMatchTransport` + `DungeonMatchBootstrap`):

```
SERVER                                           CLIENT (one team)
MatchDescriptor.Create(seed, settings,    ──►    MatchDescriptor.FromBytes
  lootSecret) = seed + settings + team→spawn       BuildAndVerify(): regenerate WITHOUT loot,
  + rules + PUBLIC fingerprint (~250 bytes)        compare public fingerprint (mismatch = refuse)
MatchState (authoritative, full layout)          MatchState (mirror, redacted layout)
  Apply(event) validates and logs                  LoadView(TeamView): what this team may know
  EventVisibility.Audience(event) ──────────►      notification (sound, UI line)
  TeamView.For(state, team) per team ───────►      reconnect = the next view
```

* **One message builds the world.** `MatchDescriptor` carries the seed, the full settings
  (binary, fixed field order, versioned by `SettingsCodec`), the shuffled team → spawn-room
  table, the round rules, and the server's `LayoutFingerprint`. Clients never use their own
  settings asset; a stale client build fails the settings version or the fingerprint before the
  round starts instead of desyncing ten minutes in.
* **Fingerprints are stable across machines.** `StableHash` is FNV-1a over bytes; `GetHashCode`
  is randomized per process and differs between Mono and IL2CPP, so it is never used for this.
* **Stable ids without a table.** `LayoutIndex` numbers items in generation order (room id, then
  slot), mob packs as 1000 + room id, keys as 5000 + key id. Server and clients compute the same
  ids independently.
* **Clients do not know what is in the chests, and cannot compute it.** Loot is rolled from a
  server-only `lootSecret` salted into the loot RNG, so the public seed reproduces everything
  about the dungeon except its items. `BuildAndVerify` generates with loot disabled and checks the
  public fingerprint (`ClientView.PublicFingerprint`); tiles, rooms, bosses, mobs and key holders
  are bit-identical. Items reach a client only inside its team's `TeamView`.
* **State changes only through events, on the server.** `MatchState.Apply` validates every
  `MatchEvent` and rejects the illegal ones; the log stays on the server for replay, review and
  the death recap. Clients never receive the log (it holds every team's rooms, rolls and souls).
  Each team gets `TeamView.For(state, team)`: public round state, its own members, souls, silver,
  charges, rolls and keys, and the containers, items, corpses, dead bosses and open vaults in rooms
  it has explored. `EventVisibility.Audience` decides which teams are told an event happened at
  all (positions, rolls and account levels stay inside the team; the final boss and the exit are
  public). The client's `MatchState` is loaded from views (`LoadView`, `IsView`, `KnownItems`), so
  the HUD models work unchanged on both sides, and a reconnect is just the next view.
* **Interest management.** `LayoutIndex.RoomsWithin(room, hops)` is the relevancy set: a player
  needs updates from the rooms within two hops (7 of 34 rooms on the sample seed), not the whole dungeon.
* **Netcode-agnostic.** `IMatchTransport` is one broadcast (the descriptor) and three team-scoped
  sends (event notification, view, ping). Implement it over Fishnet; `DungeonMatchBootstrap` does
  the rest and raises `OnMatchReady` / `OnEventNotified` / `OnDesync`.

What the dungeon layer deliberately does *not* do: player movement, combat resolution and
hit validation are the netcode's job. The layer gives them the authoritative world, ids for
everything in it, and the rules for what may change.

## Determinism and multiplayer

`DungeonRandom` is a self-contained xorshift128+ generator seeded via SplitMix64, so results are
bit-identical across platforms and .NET/Mono/IL2CPP versions (unlike `System.Random`, whose
algorithm has changed between runtimes). The server rolls a seed, generates the layout for
authoritative AI, and sends only the `int` to clients, which rebuild the identical map locally,
minus the loot, which is salted with a secret the clients never see.

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
* Locked gates on loop corridors (vault doors and keys are done; the graph already knows which edges are loops).
* Timed extract activation (Tarkov-style) using `Danger` as the schedule.
* Consumables (potions, in-dungeon salvage kits) and a crafting UI; the forge rules are done.
* Multi-floor dungeons: stack layouts, connect via stair rooms.
* Mesh / Tilemap renderer and NavMesh baking in the Unity layer.
