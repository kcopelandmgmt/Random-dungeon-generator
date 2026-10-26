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
| `DungeonGen.Unity.UI` | `Runtime/Unity/UI` | Core, Unity, UnityEngine.UI, Unity.InputSystem | The real UI, built from code: `UiStyle` (dark/gold look, code-made sprites, 1920×1080 scaler), `MainMenuUi` (characters, create, prepare with spec cards and bot teams, settings, quit), `GameHud` (bars, ability and consumable slots with cooldowns, countdown, clock, souls, silver, announcements, pause on Esc), `SummaryPanel` (death recap, round summary, settlement), `SettingsPanel`, `UiSound`, `UiInstaller`. The editor and tests reach it by reflection. |
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
| `Combat/` | `EncounterDirector.Bosses`: one boss per boss room, ids 99 down to 91, adds in id×100 blocks; `FightSim` (resolution: targets, damage, statuses, downed, zones, casts, dev casting), `Abilities` (`AbilityBook`, `AbilityDefinition`), `Brains` (mob and boss controllers, `EncounterDirector`), `BossArchetype`/`BossInstance`/`BossAffix`, `MobArchetype`, `ClassArchetype`, `WeaponSpec`, `ElementKit`, `PlayerStats`/`StatFormulas`/`Attributes`/`Resistances`/`StatusEffect`/`DamageType`, `FightRules`, `MovementRules`, `CombatBaseline`, `EncounterTuner`, `FightLab` (headless fight harness). |
| `Items/` | `ItemBase`, `Item`, `Affix`, `LegendaryAffix`, `LootRoller`/`LootRoll`, `Equipment`, `Durability`, `Crafting`, `Consumable`, `CarryRules`, `GearScore`. |
| `Net/` | `MatchServer` (the authoritative tick: intents in, events, views and snapshots out), `MatchState` (every validated event and its rules), `MatchDescriptor` (what crosses the wire once), `TeamView` (what a team may know), `PlayerIntent` (intent, `AvatarSnapshot`, `SelfSnapshot`, `EffectSnapshot`, `TeamSnapshot`), `RoundClock`, `RoundBot`/`RoundLab` (bots and headless rounds), `Matchmaker`, `Announcer`, `DeathRecap`, `DungeonEvent` (mid-round events), `LayoutIndex`, `LayoutFingerprint`, `SettingsCodec`, `StableHash`, `TeamPing`. |
| `Profile/` | `PlayerProfile`, `AccountRules`, `Inventory`, `Vendor`, `Season`, `Mutator`, `Report`; the meta loop: `ProfileCodec` (versioned binary save, items by stable keys, `ProfileFormatException` on unknown keys), `Loadout` (sheet, brought items and consumables for a character, no profile change), `RoundSettlement` (after extract or death: extraction keeps, full loot loses, silver, XP, wear, consumables; idempotent per round key saved in the profile). `MatchServer.AddPlayerWithLoadout` checks the tier wall and registers brought items (ids from 100000); `MatchServer.ItemById` maps any item id to its `Item` on the host. `ProfileCodec` is version 2 (adds `PlayerProfile.KillXp`) and reads version 1; `Settle` options `KillXp`, `AccountOf`, `WhenUtc`. `PlayerIntent.ConsumableSlot` (0 none, else 1 + index into `Consumable.All`) drives `MatchServer.UseConsumable` (cooldowns, `FightSim.StartHealOverTime`, `CleanseStatuses`). |

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
| `Audio/AudioLibrary`, `Audio/GameAudio`, `Audio/GameAudioSettings` | Sound keys (manifest slugs, variants grouped) → clips with per-category volume, built by `Editor/AudioLibraryBuilder.Build` into `Assets/Audio/AudioLibrary.asset` (also sets import settings). `GameAudio` is the one player: 24 pooled 3D sources (30 m min distance for the high camera), 2D UI via `Play2D`, announcer and boss voice, music crossfade exploration → combat (6 s after a local hit/telegraph) → boss, ambience. It listens to `AbilityEffects.EffectArrived`, `ClientWorldView.SnapshotApplied` and match events (`Server.EventRaised` filtered by `EventVisibility` on the host; `OnMatchEvent` for a client). Honours `-mute`; `RecentKeys` for tests. `GameAudioSettings`: Master/Music/Sfx in PlayerPrefs. Wired by `Editor/AudioSetup` (`[GreyboxStep(20)]`). |
| `Vfx/TelegraphView` | Floor danger discs for `EffectKind.Telegraph`, keyed by source + ability: outline ring plus a fill that grows to the landing time on the snapshot clock, pulsing in the last 0.5 s; a targeted wind-up follows its target. Colours: crush red, groundslam orange, overdrive yellow, dirge purple with a thick outline (interrupt it). |
| `Hud/WorldBars` | Over-head bars on one overlay canvas built from `CanvasRenderer` meshes (no uGUI dependency): ally green-blue, enemy red, mob orange, boss wide purple with name; class letter for players, never names or gear; downed grey with a skull; hit flash through a property block. Wired with TelegraphView by `Editor/ReadabilitySetup` (`[GreyboxStep(40)]`). |
| `Visuals/AvatarVisualLibrary`, `Visuals/AvatarVisual`, `Visuals/DoorVisual`, `Visuals/IdleBob` | Class, mob or boss name → generated prefab. `AvatarVisual` drives the Animator (Speed from motion; Attack/Cast/Hit triggers from the effects feed; Dead/Downed), turns smoothly to the facing, draws the team ring, ghosts while invisible. `ClientWorldView.Library` picks the model and falls back to the capsule; `UltimateAura` stays on both. `DoorVisual` sinks the door when open. Prefabs, materials and animator controllers are made by `Editor/GeneratedModelImporter.Build` (Generic rigs, feet at y 0, sizes normalised; report in `Logs/model-import.txt`); `Editor/ModelSetup` (`[GreyboxStep(10)]`) assigns tile, marker and room-light prefabs to the builder (one brazier with a point light per room). |
| `Boot/LaunchFlags` | Command-line flags read before the first scene: `-mute` (silences audio all session), `-minimized` (minimises the Windows player), `-autotest`, `-shots <dir>`. `LaunchFlags.Has/Value` for any code. |

`Runtime/Unity/Game`: `ProfileStore` (`persistentDataPath/profile.dgpf` via `ProfileCodec`, atomic save, `.bak` fallback), `GameFlow` (DontDestroyOnLoad: characters, spec by weapon, Play, watches `RoundSettlement.OutcomeOf`, settles and saves, shows recap and summary, returns to the menu), `PracticeLaunch` (the menu's round settings PracticeMode reads), `GameSettings` (window mode; volumes live in `GameAudioSettings`). Scenes: `MainMenu.unity` (made by `Editor/MenuSceneBuilder.Build`) first, then `SampleScene`; `-autotest` skips the menu. `Editor/UiSetup` is `[GreyboxStep(30)]` (HUD canvas, EventSystem with the Input System UI module). The OnGUI `PlayerHud` stands down when `GameHud` exists.
`Runtime/Unity/Game/OfflineMatch` is the release round host (loopback transport, bootstrap host, loadout seating, roaming `BotSquad` bot teams, offline pause); `GameFlow` hosts it one frame after the round scene loads and owns `SetRoundPaused` (`ServerSimulation.Paused` + `Time.timeScale` 0, UI on unscaled time; never when networked). `PracticeMode` is a dev wrapper over it (host on Play, dev casting, ally bot, audition). `Audio/MenuAudio` plays menu music; MainMenu also has a `GameAudio` for UI sounds. `Game/ReleaseSmoke` (`-releasesmoke`) is the release player's scripted run; `ProfileStore` honours `-profile <path>`. `PlayerBuild`: `WindowsRelease` → `Builds/Win`, `WindowsDev` → `Builds/WinDev`, `Windows` = dev alias.
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
