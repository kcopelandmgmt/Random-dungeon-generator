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

## Wave 3 (started 2026-10-26)

| # | Task | Owner | Status | Verified |
|---|------|-------|--------|----------|
| 3.1 | Release-ready offline mode: round host out of the dev-only practice assembly (`OfflineMatch`), pause pauses offline, menu audio, release and dev builds, `smoke-release` end-to-end run of the release build | agent | done | compile, playmode, build-win (release 448 MB) + smoke-release exit 0 (menu → round 40 s → pause → leave → summary → menu, profile changed), build-win-dev + smoke exit 0 |
| 3.1b | Leftovers: menu footer said "Development build" in release; audio kept playing while paused; release scene logged missing-script warnings for dev-only components; Leave from pause skips the summary screen | 3.1 | done (orchestrator): footer shows the development tag only in debug builds and the version; pause silences the listener; `StripDevComponents` removes practice-assembly components from release scenes; Leave going straight to the menu with a "what you lost" message is kept by design. Compile clean; release build to be re-smoked at wave end. |
| 3.2 | Loot, inventory and vendor: choose items (intent fields), loot/inventory/roll panels in round, stash and equipment and vendor in the menu | agent | done | core 248 (15 new), compile, playmode 24/24 (3 new), 5 screenshots checked |
| 3.2b | Item leftovers: overload slow-down not applied to movement; dropping a worn piece does not change stats; a network client's view lacks item details (base, affixes, weight, carried items); Space cannot pick up floor items; Salvage Kit in the dungeon not wired. Fixed by orchestrator: UI clicks no longer fire LMB/RMB attacks. | 3.2 | todo |
| 3.3 | Atmosphere: dark ambient, black background, fog, URP post-processing, torch/brazier lights, fog of war for unexplored rooms, ability icons, plus 2.1b visual leftovers | agent | doing | |
| 3.6 | Round balance: survivable, fun rounds measured in RoundLab (extraction 35–60%, mixed death causes, bosses stay home, team beats a room boss) with a regression test | agent | doing | |
| 3.4 | Audio leftovers (2.2b): hazard and torch loops, per-element impacts, footsteps for others | agent | done | compile; AudioTests 10/10 (3 new). 4 screenshot brightness tests failing from the darker atmosphere work, handed to 3.3 |
| 3.7 | Item rule leftovers (3.2b core part): overload slow-down, worn-piece loss changes stats, Space takes floor items, Salvage Kit in the dungeon | agent | done | core 261 + 15 new (2 director/fight-lab failures attributed to the balance agent's in-progress Brains.cs, recheck when it lands); round sim 0 rejected; compile clean |
| 3.7b | UI hookups: a Salvage button in the inventory panel (needs `PlayerIntent.SalvageItemId` via `PendingItemActions`); `PlayerItems.Interaction` comment to list `floor-item` | 3.7 | done (orchestrator; compile clean): Salvage button on carried items when `SalvageRefusal` allows; `PendingItemActions.Salvage` → `SalvageItemId` |
| 3.5 | Fishnet multiplayer (2.6) | — | queued (after atmosphere: adding the package and the define recompiles everything) | |

## Wave 2 (queued; starts as wave 1 lands)

| # | Task | Depends on | Status |
|---|------|-----------|--------|
| 2.1 | Bind generated models: character/mob/boss visuals with Animator (idle, walk, attack, cast, hit, death) driven by snapshots; props and tiles as builder prefabs; editor importer that makes prefabs and URP materials | 1.1 | done (playmode 21/21 incl. 3 visual tests; build-win + smoke pass, no magenta; 9 model screenshots checked) |
| 2.1b | Found while binding: every boss had id 99 so only one boss existed per round (fixed by orchestrator, 103b199, `MultiBossTests`); bots then extract 1/72 → balance pass started. Visual leftovers: arch wall slab dominates, thin low wall stretched, spawn/pit markers still greybox, mob death animation rarely plays (avatar removed when the snapshot drops it), walk/run root motion unverified, `WorldBars` counts `TeamRing` in body bounds | 2.1 | balance: doing; visuals → 3.3 |
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
