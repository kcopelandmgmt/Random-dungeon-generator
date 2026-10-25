# Random Dungeon Generator

The rules engine for an extraction dungeon game: Albion Online hellgates for the full-loot PvP
and the shared arena, Path of Exile for the random gear, Escape from Tarkov for the round. Up to
eight teams of one to three drop into a dungeon nobody has seen, fight mobs, bosses and each
other for twenty minutes, and walk out with what they can carry or lose all of it.

Everything decided about the game is code here, with a test behind it, before any of it is a
scene in Unity. The core is pure C# with no engine references, so it runs on a dedicated server,
in the test suite and in a console tool. `docs/DECISIONS.md` is the log of every decision and why.

```
seed ─► rooms ─► graph ─► corridors ─► roles ─► bosses ─► tiles ─► interiors ─► hazards ─► danger ─► mobs ─► shrines ─► loot ─► fairness
```

## What is here

| Area | Where | What it decides |
|------|-------|-----------------|
| Layout | `Runtime/Core/Stages`, `DungeonGenerator` | Ring placement for up to 8 teams, a final boss at the centre, boss rooms, vaults behind keys, shrines in hubs, room interiors (pillars, low walls), hazards by depth, fairness-ranked candidates. |
| Combat | `Runtime/Core/Combat` | Five attributes, six damage types, resistances, status effects with diminishing returns, mob and boss archetypes tuned by a baseline, six classes with two weapon specs each and an earned ultimate, movement, fight rules (downed state, channels, souls). |
| Items | `Runtime/Core/Items` | Bases, 24 affix families, rarity by source and tier, uniques with legendary affixes, durability with no repairs, salvage and crafting, consumables, need-over-greed group rolls, gear score. |
| Round | `Runtime/Core/Net/MatchState` | The server's authoritative round: every change is a validated event. Souls on corpses and in boss chests, resurrection at shrines, closable doors, traps, hand-offs, extraction, the boss-gated exit, the mid-round event, round end. |
| Views | `Runtime/Core/Net/TeamView`, `EventVisibility` | What each team may know. Clients never receive the log; loot is rolled from a server secret the seed cannot reproduce. |
| Meta | `Runtime/Core/Profile` | Accounts and earned classes, the vendor, crafting, seasons with per-tier leaderboards and mutators, kill-XP and transfer ledgers, reports. |
| Tiers | `DungeonTier`, `Items/GearScore` | Three dungeons walled by gear score both ways; separate queues per tier and region. |
| HUD | `Runtime/Core/Hud` | Compass strip and explored-only minimap models; death recap and round summary from the log. |
| Server loop | `Runtime/Core/Net/MatchServer.cs`, `RoundBot.cs`, `RoundLab.cs` | The authoritative tick with no engine in it: intents in, state and snapshots out. Round bots and the headless round lab that plays whole rounds on it. |
| Unity | `Runtime/Unity` | Config, builder, gizmos, camera, greybox HUDs, the match bootstrap, the transport seam, the server simulation and the client world view. |
| Unity, gated | `Runtime/Unity.Input`, `Runtime/Unity.Fishnet`, `Runtime/Unity.Practice`, `Editor` | Input System controller, Fishnet transport, dev-only practice mode with bots, the greybox scene menu. Each compiles only when its dependency is present. |
| Stub compile | `Tools/UnityStubs` | Compiles the whole Unity layer against a minimal stand-in `UnityEngine` so typos are caught without Unity. |
| Tool | `Tools/AsciiPreview` | Prints dungeons, batch-validates seeds, rolls loot, simulates fights, crafting, wear, a networked round and a team's view. |
| Tests | `Tests` | xUnit, 178 tests, 3,000+ seeds, every rule in the decision log. |

## Running the console tool (no Unity needed)

```bash
cd Tools/AsciiPreview
dotnet run -- --teams 8 --seed 7            # an 8-team dungeon as ASCII, with interiors and hazards
dotnet run -- --teams 4 --seed 3 --tier 3   # the same preset stamped as Tier III: higher item levels, more uniques
dotnet run -- --teams 4 --seed 3 --view 0   # what team 0 knows after a short round, next to what the server knows
dotnet run -- --teams 2 --seed 3 --net      # a scripted server round: validated events, rejections, replay, views
dotnet run -- --batch 500                   # 500 seeds: failures, warnings, fairness, timings
dotnet run -- --fights | --stats | --loot | --roll 40 2 | --wear | --lifetime | --craft
dotnet run -- --help
```

`--sim round --sim-seeds 10 --sim-tier 2` plays ten whole rounds with four bot teams on the real server loop and prints extractions, deaths by cause and rejected events (always zero). `--sim roundtrace --seed N` prints one round's event log.

## Running the tests

```bash
cd Tests
dotnet test
```

## Installing into Unity

1. Make a fresh **Unity 6** project with the **URP** template and install **Input System** from the Package Manager (restart when asked). *Project Settings ▸ Player ▸ Active Input Handling* can be **Input System Package (New)**: nothing in the package uses the legacy `UnityEngine.Input` API, so "Both" is not needed. Install **Fishnet** only when you want networking; add `FISHNET` to *Project Settings ▸ Player ▸ Scripting Define Symbols* when you do, and the Fishnet assembly compiles.
2. **Window ▸ Package Manager ▸ + ▸ Add package from git URL** with this repository, or clone and **Add package from disk** pointing at `package.json`.
3. **Dungeon Gen ▸ Build Greybox Scene**. The menu builds a scene with the builder, camera, server simulation, client view, HUD, input controller and practice mode wired together, using cube prefabs for every tile.
4. Press Play. `PracticeMode` hosts a round in-process over the loopback transport, seats you and one bot team, and runs the fight on the server simulation. Keybinds: LMB basic, RMB bolt, Q zone, E mobility, R ultimate, Space interact, Tab map, F ping.
5. Paste any console errors back. The Unity layer is compiled here against a stub engine (`Tools/UnityStubs`), so what reaches you has had its typos caught but has never run in Unity.

Assemblies: `DungeonGen.Core` (no engine references), `DungeonGen.Unity`, `DungeonGen.Unity.Input` (needs Input System), `DungeonGen.Unity.Fishnet` (needs the `FISHNET` define), `DungeonGen.Unity.Practice` (editor and development builds only), `DungeonGen.Unity.Editor`.

## Where things are explained

* `docs/DECISIONS.md`: every design decision, dated, with the reasoning and the code that enforces it.
* `docs/DESIGN.md`: how each stage and system works.
* `docs/CLASSES.md`: the six classes, their specs and ultimates.
* `docs/BUILD_ORDER.md`: what gets built next, in what order, and what done means for each piece.

## Status

The design is complete and enforced by tests. The ability pipeline and the mob and boss brains run in the
core and a greybox Unity layer (input, server simulation, per-team snapshots, HUD, practice mode,
a first Fishnet transport) is written but has not yet run inside Unity. What does not exist yet:
room art, visuals for abilities and telegraphs, PlayFab, Easy Anti-Cheat, the store and the menus. See `docs/BUILD_ORDER.md`.
