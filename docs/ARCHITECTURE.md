# Architecture

The map of the code: what each assembly is for, how data moves, and where a change belongs.
Keep this current: when you add, move or remove a file, assembly or asset folder, update it in
the same task (see `AUTONOMY.md`).

## The one idea

Everything that decides anything is pure C# in `Runtime/Core` with a test behind it. Unity draws
what the core decided. The server is authoritative: clients send intents, the core moves them,
each team receives only what it may know.

```
input ──intent──► MatchServer (core) ──snapshot/view/event──► ClientWorldView, HUDs, AbilityEffects
                        │
          FightSim · MatchState · EncounterDirector · RoundClock · bots
```

## Assemblies

| Assembly | Folder | References | Purpose |
|----------|--------|------------|---------|
| `DungeonGen.Core` | `Runtime/Core` | none (no engine) | Generator, combat, items, round state, server loop, views, HUD models, profile and meta rules. Runs on a dedicated server, in xUnit, in the console tool. |
| `DungeonGen.Unity` | `Runtime/Unity` | Core | Scene building, camera, match bootstrap, server frame clock, client world view, greybox HUDs, effects, the transport seam. No input reading. |
| `DungeonGen.Unity.Input` | `Runtime/Unity.Input` | Core, Unity, Unity.InputSystem | The only reader of input: WASD, aim, ability keys, zoom, map. Compiles only when the Input System package is present. |
| `DungeonGen.Unity.Practice` | `Runtime/Unity.Practice` | Core, Unity | Dev-only: hosts a round in-process over the loopback transport with bots; the effects audition. Editor and development builds only. |
| `DungeonGen.Unity.Fishnet` | `Runtime/Unity.Fishnet` | Core, Unity, FishNet.Runtime | The network transport over Fishnet. Compiles only with the `FISHNET` define. |
| `DungeonGen.Unity.Editor` | `Editor` | Core, Unity, Practice | Menu items: Build Greybox Scene; player builds. |

Tests and tools (not Unity assemblies): `Tests` (xUnit over Core), `Tools/AsciiPreview` (console
tool over Core), `Tools/UnityStubs` (retired 2026-10-26: the real headless Unity compile replaced it and the stub no longer covers the APIs in use),
`Tools/AssetGen` (Meshy and ElevenLabs generation), `Tools/Build` (the Unity lane script).

## Runtime/Core, by folder

| Folder | What lives there |
|--------|------------------|
| `Stages/` | The generator pipeline, one class per stage: `RoomPlacer`, `GraphBuilder`, `CorridorCarver`, `RoleAssigner`, `BossRoller`, `Rasterizer`, `InteriorPlacer`, `DangerMapper`, `MobPlacer`, `ShrinePlacer`, `LootPlacer`, `FairnessMapper`. `DungeonGenerator` runs them in order from a seed and `DungeonSettings`. |
| root | `DungeonLayout` (rooms, corridors, tiles, hazards, keys), `Room`, `Corridor`, `GridPoint`/`GridRect`/`GridPath`, `LineOfSight`, `LayoutValidator`, `LayoutFairness`, `Interior` (pillars, low walls, hazards, `DoorRules`), `Hud` (compass and minimap models), `AsciiRenderer`, `Strings`, `Presentation`, `Production` (tech stack and vertical slice as code). |
| `Combat/` | `FightSim` (resolution: targets, damage, statuses, downed, zones, casts, dev casting), `Abilities` (`AbilityBook`, `AbilityDefinition`), `Brains` (mob and boss controllers, `EncounterDirector`), `BossArchetype`/`BossInstance`/`BossAffix`, `MobArchetype`, `ClassArchetype`, `WeaponSpec`, `ElementKit`, `PlayerStats`/`StatFormulas`/`Attributes`/`Resistances`/`StatusEffect`/`DamageType`, `FightRules`, `MovementRules`, `CombatBaseline`, `EncounterTuner`, `FightLab` (headless fight harness). |
| `Items/` | `ItemBase`, `Item`, `Affix`, `LegendaryAffix`, `LootRoller`/`LootRoll`, `Equipment`, `Durability`, `Crafting`, `Consumable`, `CarryRules`, `GearScore`. |
| `Net/` | `MatchServer` (the authoritative tick: intents in, events, views and snapshots out), `MatchState` (every validated event and its rules), `MatchDescriptor` (what crosses the wire once), `TeamView` (what a team may know), `PlayerIntent` (intent, `AvatarSnapshot`, `SelfSnapshot`, `EffectSnapshot`, `TeamSnapshot`), `RoundClock`, `RoundBot`/`RoundLab` (bots and headless rounds), `Matchmaker`, `Announcer`, `DeathRecap`, `DungeonEvent` (mid-round events), `LayoutIndex`, `LayoutFingerprint`, `SettingsCodec`, `StableHash`, `TeamPing`. |
| `Profile/` | `PlayerProfile`, `AccountRules`, `Inventory`, `Vendor`, `Season`, `Mutator`, `Report`; the meta loop: `ProfileCodec` (versioned binary save, items by stable keys, `ProfileFormatException` on unknown keys), `Loadout` (sheet, brought items and consumables for a character, no profile change), `RoundSettlement` (after extract or death: extraction keeps, full loot loses, silver, XP, wear, consumables; idempotent per round key saved in the profile). `MatchServer.AddPlayerWithLoadout` checks the tier wall and registers brought items (ids from 100000); `MatchServer.ItemById` maps any item id to its `Item` on the host. |

## Runtime/Unity, by file

| File | Role |
|------|------|
| `DungeonConfig` | ScriptableObject wrapping `DungeonSettings` for the inspector. |
| `DungeonBuilder` | Instantiates tiles, interiors, hazards and markers from a layout; fallback cube prefabs live under a hidden inactive holder. Grid ↔ world ↔ simulator mapping (`CellSize` 2 m). |
| `DungeonMatchBootstrap` | Hosts (server) or mirrors (client) a match: descriptor, state, events, views. |
| `Match/ServerSimulation` | Frame clock around `MatchServer`; `GameSpeed`; `TransportWire`. |
| `Match/LoopbackTransport` | In-process transport for practice and tests. |
| `IMatchTransport` | The seam the netcode implements. |
| `Player/ClientWorldView` | Avatars from snapshots (ground-space interpolation), doors and hazards from the view; raises `SnapshotApplied`. |
| `TopDownCamera` | Fixed 45° yaw, 50° pitch, scroll zoom 12–28 m, fixed vertical FOV with a 21:9 cap; aim fed by the input layer. |
| `Hud/PlayerHud`, `CompassHud`, `MiniMapHud` | Greybox OnGUI HUDs drawn from snapshots and HUD models. |
| `Vfx/AbilityVfxLibrary`, `Vfx/AbilityEffects` | Prefab per ability and role; the effects layer that draws casts, projectiles, impacts, zones and floating numbers from `EffectSnapshot`s. Lifetimes are capped (`MaxLifetimeSeconds`) and physics on effect prefabs is neutralised. The library asset is filled by `Editor/VfxAutoAssign` (all abilities, audition yes-votes win, prefab reuse penalised, zone scale measured), which writes `Logs/vfx-assignment.txt`; ranking lives in `Runtime/Unity.Practice/VfxMatcher`. |
| `PlayerClassDefinition`, `DungeonGizmos` | Designer asset and scene-view overlay. |
| `Boot/LaunchFlags` | Command-line flags read before the first scene: `-mute` (silences audio all session), `-minimized` (minimises the Windows player), `-autotest`, `-shots <dir>`. `LaunchFlags.Has/Value` for any code. |

`Runtime/Unity.Input/LocalPlayerController` turns input into intents and feeds the camera's aim
and zoom and the minimap's full-map hold. `Runtime/Unity.Practice/PracticeMode` hosts a practice
round on Play (dev casting on, one ally bot, bot teams); `VfxAudition` is the effect chooser.
`AutoSmokeTest` (practice, dev builds) runs a 30 s scripted round when `-autotest` is given: walks, casts every ability of several specs, holds Interact, writes `smoke-result.json` and screenshots, and exits 0 or 1.
`Editor/GreyboxSceneSetup` assembles the scene on the template's Main Camera and then runs every `[GreyboxStep]` method (`Editor/GreyboxStepAttribute.cs`); `GreyboxRebuild.Run` rebuilds and saves SampleScene headless. `Editor/PlayerBuild.Windows` builds the development player with its settings and always-included shaders set from code.

## Data flow in a round

1. `DungeonMatchBootstrap.HostMatch` → `MatchServer.Host(seed, settings, rules, wire)` generates the
   layout, builds the descriptor, and the builder spawns the scene.
2. Each frame `ServerSimulation` steps the server at `TickRate` × `GameSpeed`. Intents arrive through
   the transport (`IntentReceived`), bots think, the sim moves and casts, the director drives mobs,
   the clock raises round events, the state validates every event.
3. After each tick the server sends every team a `TeamSnapshot` (avatars it may see, its members'
   numbers, effects it may see) and, when its knowledge changed, a `TeamView`; events it may hear are
   sent as notifications.
4. `ClientWorldView` places avatars; `AbilityEffects` draws effects; HUDs read the snapshot and the
   mirrored state; `DungeonBuilder.SetDoorClosed` shows doors as the view says.

## Where things go

* A new rule → `Runtime/Core` with a test in `Tests`, and a line in `DECISIONS.md`.
* Something a player sees → `Runtime/Unity`, drawn from a snapshot, view or event, never from the server object directly.
* Anything that reads a key or a mouse → `Runtime/Unity.Input`.
* Dev conveniences → `Runtime/Unity.Practice`.
* Generated assets → `Assets/Generated/<models|audio>/<name>/` in the Unity project with a `manifest.json`.
  Audio: `Assets/Generated/audio/<abilities|combat|world|ui|ambience|voice/announcer|voice/boss>/<slug>.mp3`, one
  `manifest.json` (slug, file, category, prompt, duration, loop). Made by `Tools/AssetGen/elevenlabs.py` (resumable).
  Models: `Assets/Generated/models/<slug>/` with `<slug>.fbx`, `_basecolor.png`, `_thumb.png` (untextured preview), and for
  humanoids `_rigged.fbx` plus `_anim_<walk|run|idle|attack|cast|hit|death>.fbx` (mobs lack cast and hit); `manifest.json` per
  folder and `models/ledger.json` for credits. Made by `Tools/AssetGen/meshy.py` (`run|status|balance|library`, resumable, 4,500-credit ceiling).
