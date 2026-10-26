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
| 1.1 | Meshy generation tool `Tools/AssetGen/meshy.py` and the first model batch (3 classes, 2 bosses rigged; 3 mobs, chest, vault door, shrine, key, pillar, floor and wall tiles) into `Assets/Generated/models/` | agent | todo | | |
| 1.2 | ElevenLabs tool `Tools/AssetGen/elevenlabs.py` and the first sound batch (ability casts/impacts per element, hits, doors, chest, shrine, extract, UI, announcer lines) into `Assets/Generated/audio/` | agent | todo | | |
| 1.3 | Automatic VFX assignment: editor method that fills `AbilityVfxLibrary` for all 55 abilities from the Piloto pack by shape and element, respecting audition yes-votes | agent | todo | | |
| 1.4 | Unity test harness: `Assets/Tests/PlayMode` with tests for scene build, hosting a practice round, movement, casting, door open; runnable through `unity-batch.sh tests PlayMode` | agent | todo | | |
| 1.5 | Player build: `Editor/PlayerBuild.cs` (Windows, development), `-mute` and `-minimized` flags honoured at startup, `unity-batch.sh build-win` | agent | todo | | |

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
