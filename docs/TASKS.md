# Task ledger

The running record of the autonomous build. Every task gets a row when it starts and its row is
finished when the task is: what changed, how it was verified, what is left. Newest wave first.
Agents also leave notes for each other under **Requests**.

Status: `todo` · `doing` · `done` · `blocked`. Verification: `core` = `dotnet test` passed,
`compile` = Unity headless compile clean, `playmode` = Unity PlayMode tests passed, `manual` =
could not be verified headless (say why).

## Wave 1 (started 2026-10-25)

| # | Task | Owner | Status | Verified | Notes |
|---|------|-------|--------|----------|-------|
| 1.1 | Meshy generation tool `Tools/AssetGen/meshy.py` and the model batch into `Assets/Generated/models/` | agent | done | 38/38 downloaded, textures checked by eye | 6 heroes and 5 humanoid bosses rigged with walk, run, idle, attack, cast, hit, death; 5 humanoid mobs rigged with walk, run, idle, attack, death; 4 non-humanoid bosses, 3 mobs, 15 props/tiles as textured meshes. 1,085 credits (keys 4–7); keys 1–3 untouched. |
| 1.2 | ElevenLabs tool `Tools/AssetGen/elevenlabs.py` and the first sound batch (ability casts/impacts per element, hits, doors, chest, shrine, extract, UI, announcer lines) into `Assets/Generated/audio/` | agent | done | 90 files, 0 failures | abilities 26, combat 19, world 14, ui 6, ambience 5 (incl. 3 music tracks of 60 s from `/v1/music`), announcer 11, boss taunts 9. About 3,773 characters spent (music is billed outside the per-clip figures). Not listened to. |
| 1.3 | Automatic VFX assignment: editor method that fills `AbilityVfxLibrary` for all 69 abilities from the Piloto pack by shape and element, respecting audition yes-votes | agent | done | compile clean; rerun 2026-10-26 01:00: 69 abilities, 108 distinct prefabs, 0 weak picks, 1 yes-vote honoured | Fixed in `AbilityEffects`: infinite particle lifetimes never retired (cap `MaxLifetimeSeconds` 8), physics on effect prefabs neutralised. Audition panel editor-only. Weak picks to revisit: arrow impacts (Volleyer, Trapper), WindRay as a projectile, Caltrops/Tripwire look. Berserker cleave shows a hard cut (depth/soft-particle artifact in the asset). |
| 1.4 | Unity test harness: `Assets/Tests/PlayMode` with tests for scene build, hosting a practice round, movement, casting, door open; runnable through `unity-batch.sh tests PlayMode` | agent | done | playmode 7/7 three runs in a row, editmode 1/1 | Screenshots every run in `dungeongame/Logs/shots/` (spawn, after-move, six casts, zoomed-out, overview). No game bugs found. Stale `Temp/UnityLockfile` after a crashed run: the lane now clears it when no Unity process has the project open. |
| 1.6 | Core extraction loop: `ProfileCodec` (save/load), loadouts carried into a round, `RoundSettlement` (extract keeps, death loses, silver, XP), item lookup on the host | agent | done | core (220) | Gaps found, queued as 2.7: the server never raises `PlayerDied` on `ReconnectExpired`; no `ItemDestroyed` for the 50% trash on death; consumables have no effect in the sim; the kill-XP daily cap is not applied at settlement. `Tools/UnityStubs` no longer builds (new code uses types the stub lacks); the real headless Unity compile replaces it. |
| 1.7 | Telegraphs: boss/mob channels reach the snapshot as `EffectKind.Telegraph` (danger circle, landing time) | orchestrator | done | core (205) | `TelegraphShapes` mirrors the brains' reach for drawing only |
| 1.8 | Greybox setup steps: `[GreyboxStep]` hook so features wire themselves into the scene from their own files; `GreyboxRebuild.Run` rebuilds SampleScene headless | orchestrator | done | compile | Fixed `??` on a Unity component lookup in the setup |
| 1.5 | Player build: `Editor/PlayerBuild.cs` (Windows, development), `-mute` and `-minimized` flags honoured at startup, `unity-batch.sh build-win` | agent | done | build, smoke (exit 0: 30 s, 24 casts ok, 0 exceptions) | Build 382 MB with URP shaders always included (stripping crashed the first player). Follow-ups: avatar capsules rendered magenta (fixed by orchestrator: explicit URP material); audition panel showed in players (sent to 1.3); one 2.7 s frame in the smoke run, cause unknown; `smoke` moved out of the Unity lock (orchestrator). |

## Wave 5: every recommendation (started 2026-10-26)

The owner asked for all of the orchestrator's suggestions. Shared agent rules: `docs/agents/WAVE5_COMMON.md`.
Two limits stay: no third-party relay or matchmaking (needs an account), and the final-boss check is done in sims, not by hand.

| # | Task | Owner | Status | Verified |
|---|------|-------|--------|----------|
| 5.0 | Plug-in hooks: `MainMenuUi.Register(ExtraScreen)` adds a screen and a Main button without editing the menu; `unity-batch.sh tests <platform> <filter>` runs one test class | orchestrator | done | compile clean |
| 5.1 | Hit feel (camera shake, visual hit-stop, clearer damage numbers), rarity light beams on floor loot and glowing chests, extract beacons with a channel effect and an "exit opened nearby" cue | agent (sonnet) | doing | |
| 5.2 | uGUI minimap (fog-respecting, M for a large map) and compass strip replacing the OnGUI ones | agent (sonnet) | doing | |
| 5.3 | Settings: key rebinding through one binding table, graphics options, controller and keyboard navigation in every menu | agent (sonnet) | doing | |
| 5.4 | Online beyond the LAN: UPnP port mapping on host, client-side movement prediction with reconciliation, simulated latency for smoke-net | agent (opus) | doing | |
| 5.5 | Regenerate the four off-theme models (Ember Wisp, Frostbound, Ashen Colossus, Glasswright), budget 320 credits | agent (sonnet) | doing | |
| 5.6 | Final boss tuning from sims: attempts, kills and wipes measured; cause fixed | agent (sonnet) | doing | |
| 5.7 | Crafting Bench screen over the core `Crafting` rules | agent (sonnet) | doing | |
| 5.8 | Animations from the bought packs retargeted to the characters | agent | next | |
| 5.9 | Smaller download: strip unused VFX and assets from the player | agent | next | |
| 5.10 | Tutorial: a guided solo dungeon teaching souls, shrines and extraction | agent | next | |
| 5.11 | Gear showcase: four drops per tier and the paper doll, rendered for the owner | agent | doing | |

## Wave 4: the owner's answers (started 2026-10-26)

| # | Task | Owner | Status | Verified |
|---|------|-------|--------|----------|
| 4.1 | Shrines and leaving: corpse keeps items after a resurrection; shrine use is a ~2 s channel at the altar broken by damage; no shrine shop (rules, bots, panel); Leave from pause shows the summary first | agent, finished by orchestrator | done | core 309; sim 8 seeds extraction 46%, 11.1 min, rejected 0; compile clean. The agent stalled after the core work and left the UI referencing the retired buy action, which blocked every Unity run for two hours; the orchestrator stopped it and finished the panel, the leave-then-summary flow and the PlayMode test. |
| 4.2 | Level look: corridors 4 cells wide; tall walls that fade when they hide a character | agent | done | core 300; in a sandbox copy: playmode 37/37 (3 new `WallFadeTests`), build-win + smoke-release; then confirmed on the real project: playmode 37/37, build-win + smoke-release exit 0. Sims (fixed seeds): extraction 49.6% → 53.1%, round 9.9 → 11.0 min, PvP share 63% → 69%, rejected 0. |
| 4.3 | Painted ability icons generated with Meshy text-to-image for all abilities | agent | done | 69 icons reviewed by eye; model pilot (nano-banana, nano-banana-2-lite, gpt-image-2): gpt-image-2 chosen (only one that obeyed no frame/no text), 9 credits each, 621 total + 27 pilot + 9 regenerated; ledger 1,760 / 4,500. `AbilityIconBaker.FromImages` frames them for the HUD; `Bake` stays the fallback. |

**Wave 4 verification (2026-10-26, real project):** icons imported (`FromImages`), SampleScene rebuilt, compile clean, PlayMode 37/37, EditMode 1/1, build-win + smoke-release, build-win-dev + smoke, smoke-net host and client: all exit 0. Screenshots of the painted HUD icons, the shrine prayer panel and the 8 m corridor with a faded wall checked by eye.

## Wave 3 (started 2026-10-26)

| # | Task | Owner | Status | Verified |
|---|------|-------|--------|----------|
| 3.1 | Release-ready offline mode: round host out of the dev-only practice assembly (`OfflineMatch`), pause pauses offline, menu audio, release and dev builds, `smoke-release` end-to-end run of the release build | agent | done | compile, playmode, build-win (release 448 MB) + smoke-release exit 0 (menu → round 40 s → pause → leave → summary → menu, profile changed), build-win-dev + smoke exit 0 |
| 3.1b | Leftovers: menu footer said "Development build" in release; audio kept playing while paused; release scene logged missing-script warnings for dev-only components; Leave from pause skips the summary screen | 3.1 | done (orchestrator): footer shows the development tag only in debug builds and the version; pause silences the listener; `StripDevComponents` removes practice-assembly components from release scenes; Leave going straight to the menu with a "what you lost" message is kept by design. Compile clean; release build to be re-smoked at wave end. |
| 3.2 | Loot, inventory and vendor: choose items (intent fields), loot/inventory/roll panels in round, stash and equipment and vendor in the menu | agent | done | core 248 (15 new), compile, playmode 24/24 (3 new), 5 screenshots checked |
| 3.2b | Item leftovers: overload slow-down not applied to movement; dropping a worn piece does not change stats; a network client's view lacks item details (base, affixes, weight, carried items); Space cannot pick up floor items; Salvage Kit in the dungeon not wired. Fixed by orchestrator: UI clicks no longer fire LMB/RMB attacks. | 3.2 | todo |
| 3.3 | Atmosphere: dark ambient, black background, fog, URP post-processing, torch/brazier lights, fog of war for unexplored rooms, ability icons, plus 2.1b visual leftovers | agent | done | playmode 30/30 (3 new); build-win + smoke-release pass (~1,050 fps avg, 7–9 lights on at once); 5 look screenshots checked. Brightness checks retuned for the darker look (0.012; overview 0.006; black < 0.004). Found: StripDevComponents read the editor's development flag (fixed by orchestrator). |
| 3.6 | Round balance: survivable, fun rounds measured in RoundLab (extraction 35–60%, mixed death causes, bosses stay home, team beats a room boss) with a regression test | agent | done | core 266 (3 new `BalanceTests`, ~9 s); 4 teams × 24 seeds: extraction 2% → 43–45%, boss downs outside the arena 49–53 → 0, rejected 0, items out 7 → ~200. Missed: rounds 5–6.5 min (target 8–15), PvP 52–53% of deaths (82% at 8 teams), in-round trios win ~45–50% of committed boss fights; final boss never killed (lab 0–25%). |
| 3.6b | Balance follow-ups: seat bots with consumables (`AddBot` with a loadout); revive start must check the state's downed list; de-duplicate room-cleared in the fight bridge; `FightLab` brain targets map-wide and flanks 60° apart (allies hit each other); `AsciiPreview --sim round` hard-wired to 4 teams; round length (bots run out of rooms by ~3 min) and PvP share at 8 teams | 3.6 | done (core 274, 8 new; rejected 0): 4×3 extraction 47–50%, round 9–9.5 min, PvP 49–54%; 8×3 extraction 44%, round 10–12.5 min, PvP 91%. Per-caster `DevCasters`, wired by orchestrator in `OfflineMatch` for the local player. |
| 3.9 | Mid-round events on the server: the 10:00 roaming elite, unsealed vault and shrine event are designed (`DungeonEvent`) but the headless server never spawns them (nothing raises `EliteKilled`; `Interact` has no unsealed-vault path) | 3.6b | done (core 289, 8 new; rejected 0; 24 seeds 4×3: extraction 59%, round 11.3 min, elite engaged 4/8, killed 2) |
| 3.10 | Shrines on the server and in the UI: the server has no shrine interaction (resurrect a teammate for 2 souls, arm the ultimate for 2 souls, shrine purchases, the cheap-shrine event price), so ultimates can never be armed in real play | 3.9 | done (core 299, 10 new; playmode 34/34; `ui-shrine.png`; 72 bot rounds: 19 arms, 4 resurrections, 20 buys, 0 rejected). Orchestrator: potion and shrine presses now survive a newer intent before the tick (`IntentCarryTests`, core 300). Known: a remote client's panel may offer a second resurrection the server refuses (the mirror does not know who was already resurrected). |
| 3.12 | Elementalist playable from Prepare: one card per element kit (Pyromancy/Fire Staff, Cryomancy/Frost Rod, Stormcalling/Storm Wand) via `GameFlow.SpecsFor` | agent | done | compile; `ElementalistTests` pass; `ui-prepare-elementalist.png` checked |
| 3.11 | The elite "Revenant Champion" (combatant 490) has no model mapping in the avatar library | 3.9 | done (orchestrator): `GeneratedModelImporter.Aliases` maps it to the Hollow Knight model at 1.6× with a boss ring; library rebuilt headless |
| 3.4 | Audio leftovers (2.2b): hazard and torch loops, per-element impacts, footsteps for others | agent | done | compile; AudioTests 10/10 (3 new). 4 screenshot brightness tests failing from the darker atmosphere work, handed to 3.3 |
| 3.7 | Item rule leftovers (3.2b core part): overload slow-down, worn-piece loss changes stats, Space takes floor items, Salvage Kit in the dungeon | agent | done | core 261 + 15 new (2 director/fight-lab failures attributed to the balance agent's in-progress Brains.cs, recheck when it lands); round sim 0 rejected; compile clean |
| 3.7b | UI hookups: a Salvage button in the inventory panel (needs `PlayerIntent.SalvageItemId` via `PendingItemActions`); `PlayerItems.Interaction` comment to list `floor-item` | 3.7 | done (orchestrator; compile clean): Salvage button on carried items when `SalvageRefusal` allows; `PendingItemActions.Salvage` → `SalvageItemId` |
| 3.8 | Dev player exits 139 at shutdown after a passing smoke (release exits 0): bisect and fix | agent | done | smoke 3/3 exit 0, smoke-release 0, playmode 30/30. Cause: access violation in `D3D12Core.dll` tearing down after thousands of particle systems; fix: `PlayerBuild` pins Windows to Direct3D 11. The flood came from dev casting applying to bots (sent to 3.6b: per-caster `DevCasters`); `AbilityEffects.MaxLiveEffects` 64 added as a defence (orchestrator). |
| 3.5 | Online multiplayer with FishNet: install, online round host/join/dedicated, everything a remote client needs on the wire, lobby, `smoke-net` two-process proof | agent | done | core 289 (7 new wire tests), playmode 32/32 (2 network tests), smoke-net host+client exit 0 twice, build-win + smoke-release, build-win-dev + smoke |
| 3.5b | Online gaps: no NAT traversal or relay (hosting over the internet needs UDP port forwarding); no matchmaking service (join by IP); the client's join loadout is trusted (needs a backend for profiles); a client that never returns stays unsettled locally; no client prediction; item details re-sent with every view change; Unity Dedicated Server module not installed (use the player with `-batchmode -nographics -server`) | 3.5 | known |

## Wave 2 (queued; starts as wave 1 lands)

| # | Task | Depends on | Status |
|---|------|-----------|--------|
| 2.1 | Bind generated models: character/mob/boss visuals with Animator (idle, walk, attack, cast, hit, death) driven by snapshots; props and tiles as builder prefabs; editor importer that makes prefabs and URP materials | 1.1 | done (playmode 21/21 incl. 3 visual tests; build-win + smoke pass, no magenta; 9 model screenshots checked) |
| 2.1b (visuals closed in 3.3) | Found while binding: every boss had id 99 so only one boss existed per round (fixed by orchestrator, 103b199, `MultiBossTests`); bots then extract 1/72 → balance pass started. Visual leftovers: arch wall slab dominates, thin low wall stretched, spawn/pit markers still greybox, mob death animation rarely plays (avatar removed when the snapshot drops it), walk/run root motion unverified, `WorldBars` counts `TeamRing` in body bounds | 2.1 | balance: doing; visuals → 3.3 |
| 2.2 | Game audio: audio library asset, ability/hit/world/UI sounds from effects and events, announcer, music/ambience, honours mute; `[GreyboxStep]` wiring | 1.2 | done (compile, playmode 13/13 incl. 6 audio tests; silent) |
| 2.2b | Audio left unmapped: hazard and torch loops need positions in the snapshot/view; per-element impact sounds need the element on Hit effects; footsteps for other avatars | 2.2 | done in 3.4 |
| 2.3 | Combat readability: telegraph circles that fill to the landing time, world-space health bars with ally/enemy colour and class icon, hit flash | 1.3, 1.7 | done (compile; playmode 21/21 incl. 2 readability tests; `telegraph.png`, `bars.png` checked by eye) |
| 2.4 | Real UI in uGUI built from code: main menu (play, class and spec, settings with volume, quit), in-round HUD (health, resource, ability bar, channel, clock, souls, silver, announcements), pause menu, death recap and round summary screens, return to menu; profile saved to disk and settled after each round | 1.5, 1.6 | done (GameFlowTests 3/3 incl. a three-round flow checked against the profile on disk; build-win and smoke pass; 9 UI screenshots checked by eye) |
| 2.4b | UI gaps: menu-launched rounds live in the dev-only practice assembly (no release build yet); pause does not pause; MainMenu has no GameAudio (silent menu sounds, no menu music); ability slots have no icons; Elementalist specs not wired; no stash/vendor screen; legacy Text (no TMP); death recap and pockets read the host server (a network client needs them on the wire) | 2.4 | todo |
| 2.5 | Loot and inventory: chest contents panel, take/roll prompts, carried items and weight, stash and equip in the menu, settlement after a round saved to disk | 1.6, 2.4 | todo |
| 2.6 | Fishnet: install from GitHub, prove host and client in two muted processes on one machine | 1.5 | todo |
| 2.7 | Core gaps from 1.6: death on reconnect expiry, 50% item trash on death, consumable effects (health potion etc.), kill-XP cap at settlement | 1.6 | done (core 233; round sim 0 rejected) | Also fixed: bots tried doors they could not work (30 rejections on one seed). The host must pass `AccountOf` to `Settle` for the kill-XP cap to apply to known accounts. |

## Done before the program (2026-10-24)

| Task | Verified |
|------|----------|
| Package compiles in Unity 6000.3.0f1; .meta files committed | compile |
| Greybox renders (fallback prefabs active), practice hosts on Play, HUD draws (one camera) | manual in editor |
| Legacy Input removed; Active Input Handling Both | compile |
| Albion camera (45° yaw, zoom, 21:9 cap), corridors 3 cells wide, held Interact never re-closes a door, avatars no longer bob, GameSpeed 1.1 | core, compile, manual |
| Effects feed (casts, hits, zones in the snapshot), dev casting, VFX library, effects layer, audition | core (204 tests), compile, manual |
| .NET output moved to `bin~/obj~` so Unity ignores it | compile |

## Requests

(none)
