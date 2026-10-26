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
| 1.1 | Meshy generation tool `Tools/AssetGen/meshy.py` and the first model batch (3 classes, 2 bosses rigged; 3 mobs, chest, vault door, shrine, key, pillar, floor and wall tiles) into `Assets/Generated/models/` | agent | doing | | |
| 1.2 | ElevenLabs tool `Tools/AssetGen/elevenlabs.py` and the first sound batch (ability casts/impacts per element, hits, doors, chest, shrine, extract, UI, announcer lines) into `Assets/Generated/audio/` | agent | done | 90 files, 0 failures | abilities 26, combat 19, world 14, ui 6, ambience 5 (incl. 3 music tracks of 60 s from `/v1/music`), announcer 11, boss taunts 9. About 3,773 characters spent (music is billed outside the per-clip figures). Not listened to. |
| 1.3 | Automatic VFX assignment: editor method that fills `AbilityVfxLibrary` for all 55 abilities from the Piloto pack by shape and element, respecting audition yes-votes | agent | doing | | |
| 1.4 | Unity test harness: `Assets/Tests/PlayMode` with tests for scene build, hosting a practice round, movement, casting, door open; runnable through `unity-batch.sh tests PlayMode` | agent | doing | | |
| 1.6 | Core extraction loop: `ProfileCodec` (save/load), loadouts carried into a round, `RoundSettlement` (extract keeps, death loses, silver, XP), item lookup on the host | agent | done | core (220) | Gaps found, queued as 2.7: the server never raises `PlayerDied` on `ReconnectExpired`; no `ItemDestroyed` for the 50% trash on death; consumables have no effect in the sim; the kill-XP daily cap is not applied at settlement. `Tools/UnityStubs` no longer builds (new code uses types the stub lacks); the real headless Unity compile replaces it. |
| 1.7 | Telegraphs: boss/mob channels reach the snapshot as `EffectKind.Telegraph` (danger circle, landing time) | orchestrator | done | core (205) | `TelegraphShapes` mirrors the brains' reach for drawing only |
| 1.8 | Greybox setup steps: `[GreyboxStep]` hook so features wire themselves into the scene from their own files; `GreyboxRebuild.Run` rebuilds SampleScene headless | orchestrator | done | compile | Fixed `??` on a Unity component lookup in the setup |
| 1.5 | Player build: `Editor/PlayerBuild.cs` (Windows, development), `-mute` and `-minimized` flags honoured at startup, `unity-batch.sh build-win` | agent | done | build, smoke (exit 0: 30 s, 24 casts ok, 0 exceptions) | Build 382 MB with URP shaders always included (stripping crashed the first player). Follow-ups: avatar capsules rendered magenta (fixed by orchestrator: explicit URP material); audition panel showed in players (sent to 1.3); one 2.7 s frame in the smoke run, cause unknown; `smoke` moved out of the Unity lock (orchestrator). |

## Wave 2 (queued; starts as wave 1 lands)

| # | Task | Depends on | Status |
|---|------|-----------|--------|
| 2.1 | Bind generated models: character/mob/boss visuals with Animator (idle, walk, attack, cast, hit, death) driven by snapshots; props and tiles as builder prefabs; editor importer that makes prefabs and URP materials | 1.1 | todo |
| 2.2 | Game audio: audio library asset, ability/hit/world/UI sounds from effects and events, announcer, music/ambience, honours mute; `[GreyboxStep]` wiring | 1.2 | todo |
| 2.3 | Combat readability: telegraph circles that fill to the landing time, world-space health bars with ally/enemy colour and class icon, hit flash | 1.3, 1.7 | todo |
| 2.4 | Real UI in uGUI built from code: main menu (play, class and spec, settings with volume, quit), in-round HUD (health, resource, ability bar, channel, clock, souls, silver, announcements), pause menu, death recap and round summary screens, return to menu | 1.5, 1.6 | todo |
| 2.5 | Loot and inventory: chest contents panel, take/roll prompts, carried items and weight, stash and equip in the menu, settlement after a round saved to disk | 1.6, 2.4 | todo |
| 2.6 | Fishnet: install from GitHub, prove host and client in two muted processes on one machine | 1.5 | todo |
| 2.7 | Core gaps from 1.6: death on reconnect expiry, 50% item trash on death, consumable effects (health potion etc.), kill-XP cap at settlement | 1.6 | todo |

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
