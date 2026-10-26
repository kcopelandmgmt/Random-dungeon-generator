# Wave 5: rules every agent follows

You are one of several agents working at the same time on "Dungeon Game". It is a top-down, team-based extraction
dungeon crawler in Unity 6000.3.0f1 URP, in the style of Albion Online's camera and Dark and Darker's loop. The
server is authoritative and the rules are pure C#.

* Package repo (the code): `D:\unity dungeon game\Random-dungeon-generator`, branch `claude/practical-wright-h5g0so`.
* Unity project (installs the package from disk): `D:\unity dungeon game\dungeongame`.
* Read first: `docs/AUTONOMY.md` (lane commands, verification), `docs/ARCHITECTURE.md` (assemblies, data flow),
  `docs/DECISIONS.md` (locked game rules; never contradict them).

## Hard rules

1. **Never open the Unity editor GUI.** Run Unity only through `bash Tools/Build/unity-batch.sh <cmd>`. That script
   queues on a lock that other agents share, so waits of several minutes are normal. Players it launches run with
   `-mute -minimized`, so the owner hears nothing. Never launch a player any other way.
2. **Touch only the files your brief lists**, plus new files you create in the folders it names. Another agent may
   be editing any other file right now.
3. **If `compile` fails on errors in files you do not own**, another agent is mid-edit. Wait about five minutes and
   retry; give up after about 40 minutes. Never edit their files to fix it.
4. **Do not edit `docs/`.** The orchestrator owns the docs and the project board. Put everything the docs need in
   your final report.
5. **Commits.**
   * Package repo: commit only your own files, by path (never `git add -A`, `git add .` or `commit -a`). Use a clear
     message ending with the line `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Do not push.
   * Unity project: never commit. The orchestrator commits it.
6. **`Runtime/Core` has no engine references.** Run `cd Tests && dotnet test` before and after touching it; all tests
   must pass. Rules belong in the core, with an xUnit test.
7. **Separation of concerns.**
   * Unity-side code reads input only in `Runtime/Unity.Input`.
   * HUD and visuals draw from snapshots and views; the server decides everything.
   * The UI is uGUI built from code with `UiStyle` (see `MainMenuUi`, `StashScreen`). Use legacy `Text`, no TMP.
8. **Scene changes:**
   * Add a scene setup step as a static method with `[GreyboxStep(order)]` in a new Editor file.
   * Rebuild the scene with `bash Tools/Build/unity-batch.sh method DungeonGen.Unity.Editor.GreyboxRebuild.Run`.
   * Any shader you `Shader.Find` at runtime must be in `PlayerBuild.IncludeShaders`. If you need one added, ask in
     your report; don't edit `PlayerBuild`.
9. **API keys** are in `D:\unity dungeon game\.secrets\keys.env`. Only the `Tools/AssetGen` scripts read them. Never
   echo, log or commit them.
10. **Python** runs as `python -I script.py`. Write multi-line scripts to files, not heredocs, because quotes and
    backslashes break heredocs here.

## Verify your own work

1. `bash Tools/Build/unity-batch.sh compile` → zero errors.
2. Write one PlayMode test file for your feature: `dungeongame/Assets/Tests/PlayMode/<Feature>Tests.cs`.
   * Copy the patterns in `GameHarness.cs`, `ShrineTests.cs` and `WallFadeTests.cs`.
   * Run only your class: `bash Tools/Build/unity-batch.sh tests PlayMode <Feature>Tests`.
   * If your test saves screenshots, save them to `dungeongame/Logs/<feature>/` and **look at them with the Read tool**.
     A test that passes on a broken-looking picture is not done.
3. Do not run the full suites or the player builds. The orchestrator does that at the end of the wave.

## Final report (under 350 words)

* Files added and changed.
* What you verified, with the test counts.
* What could not be verified headless.
* Known gaps.
* Anything you need from another owner (a shader include, a doc line).
