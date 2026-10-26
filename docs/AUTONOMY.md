# The autonomous build program

How this game gets built without a human in the loop: who works on what, in what order, how work
is verified, and the rules every agent follows. Read this before touching anything. The design is
settled in `DECISIONS.md`; the order of work in `BUILD_ORDER.md`; the map of the code in
`ARCHITECTURE.md`; what depends on what in `DEPENDENCIES.md`; the running ledger in `TASKS.md`.

## Layout on this machine

| What | Where |
|------|-------|
| This repository (the package: core rules, Unity layer, tools, tests) | `D:\unity dungeon game\Random-dungeon-generator` |
| The Unity 6000.3.0f1 URP project that installs the package from disk | `D:\unity dungeon game\dungeongame` |
| Generated assets (models, audio) and their manifests | `D:\unity dungeon game\dungeongame\Assets\Generated\` |
| API keys (never in git, never echoed) | `D:\unity dungeon game\.secrets\keys.env` |
| Unity editor | `C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe` |
| .NET SDK 8 | `C:\Program Files\dotnet\dotnet.exe` |
| Bought asset packs (.unitypackage) | `D:\Unity Assets\` |

The Unity project is not under git. The package is; the branch is `claude/practical-wright-h5g0so`.

## The one Unity lane

Only one Unity process may use the project at a time (it holds `Temp/UnityLockfile`). Every
agent runs Unity through `Tools/Build/unity-batch.sh`, which queues on a lock and runs the editor
headless on the real project:

```bash
bash Tools/Build/unity-batch.sh compile                      # compile every assembly; prints errors; exit 1 on any
bash Tools/Build/unity-batch.sh method DungeonGen.Unity.Editor.GreyboxSceneSetup.Build
bash Tools/Build/unity-batch.sh import "D:/Unity Assets/Some_Pack.unitypackage"
bash Tools/Build/unity-batch.sh tests EditMode                # Unity Test Framework, results in Logs/tests-EditMode.xml
bash Tools/Build/unity-batch.sh tests PlayMode
```

The core suite needs no Unity: `cd Tests && dotnet test` (about 30 s, 200+ tests). The console
tool prints a dungeon without Unity: `cd Tools/AsciiPreview && dotnet run -- --teams 4 --seed 7`.

## Verification, in order, after every task

1. `cd Tests && dotnet test` if anything under `Runtime/Core` changed. Must pass. Never skip.
2. `bash Tools/Build/unity-batch.sh compile`. Zero errors.
3. `bash Tools/Build/unity-batch.sh tests PlayMode` when the Unity layer or the scene changed.
4. Update `TASKS.md` (what was done, what was verified, what is left), and `ARCHITECTURE.md` /
   `DEPENDENCIES.md` if a file, assembly, asset folder or external dependency was added or removed.
   This is a hard rule: a task is not finished until the documents say so.
5. Commit to the branch with a message that says what changed and why; push.

## Rules for every agent

* Touch only the files your brief names. Two agents never write the same file; if you need a change
  elsewhere, write it down in `TASKS.md` under "Requests" and stop.
* `Runtime/Core` has no engine references and must stay that way. Rules live there with a test.
* Unity-side code reads no input outside `Runtime/Unity.Input`; HUD code draws from snapshots and
  views only; the server decides everything.
* Never commit `Library/`, `Temp/`, build output, or anything under `.secrets`. Keep generated
  assets under `Assets/Generated/<kind>/<name>/` with a `manifest.json` beside them so a rerun
  skips what exists.
* API keys are read from `.secrets/keys.env` by the tools in `Tools/AssetGen`; never paste them
  into code, logs, commit messages or chat.
* Prefer small, verifiable steps. If something cannot be verified headless, say so in `TASKS.md`
  rather than claiming it works.

## Asset generation budget

Meshy: three keys, ~660 credits each (about 1,980). Text-to-3D preview costs 20 (meshy-7.1) or 5
(meshy-6-lite); refine (texture) 10; auto-rig 5; animation 3 per action. Hero assets (the three
slice classes, the two slice bosses) use meshy-7.1 with rigging; props, mobs and tiles use
meshy-6-lite. Hard ceiling: 1,500 credits for the whole program; the tool refuses past it.

ElevenLabs: ~85,000 characters. Sound effects via `/v1/sound-generation`, announcer lines via
`/v1/text-to-speech/{voice_id}` (voice "Daniel - Steady Broadcaster" `onwK4e9ZLuTAKqWW03F9`,
model `eleven_multilingual_v2`). Ceiling: 60,000 characters.

## Milestone plan (from BUILD_ORDER.md, with what is already true)

Done and running in Unity: scene from the layout, movement and the locked camera, server
simulation with per-team snapshots, greybox HUD, practice mode with bots, doors, effects feed,
VFX library and audition, dev casting.

Wave 1 (parallel): asset generation tools and the first model batch · audio tools and the first
sound batch · effect pack import and automatic VFX assignment · Unity PlayMode test harness.
Wave 2 (parallel): generated models bound to avatars, props and tiles · ability audio and announcer
wired to the effects feed · mob and boss telegraphs · round summary and death recap screens · a
real uGUI HUD replacing OnGUI.
Wave 3: Fishnet transport proven between two processes · main menu and round flow · a Windows
build that runs from the desktop.
Each wave ends with the full verification above, a documentation pass, and a build.
