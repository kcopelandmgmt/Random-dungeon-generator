# Dependency map

What depends on what, inside and outside the code. Update it when a reference, package, tool or
external service is added or removed.

## Assembly graph

```
DungeonGen.Core  (no references; netstandard 2.1 under Unity, net8.0 under dotnet)
   ▲        ▲
   │        │
DungeonGen.Unity ──────────────► UnityEngine, UnityEngine.UI? (no), Unity.RenderPipelines.Universal.Runtime (by reflection only, in Editor setup)
   ▲        ▲        ▲
   │        │        └── DungeonGen.Unity.Fishnet  → FishNet.Runtime            (define FISHNET)
   │        └─────────── DungeonGen.Unity.Input    → Unity.InputSystem          (versionDefine DUNGEONGEN_INPUTSYSTEM)
   └──────────────────── DungeonGen.Unity.Practice (UNITY_EDITOR || DEVELOPMENT_BUILD)
                              ▲
                              └── DungeonGen.Unity.Editor (editor only)
```

Rule: arrows only point down toward Core. Core never references anything above it. The Unity
assembly never references Input, Practice, Fishnet or Editor.

## Unity packages (Packages/manifest.json in the Unity project)

| Package | Version | Used by |
|---------|---------|---------|
| com.unity.render-pipelines.universal | 17.3.0 | Rendering; URP Lit/Unlit shaders in the builder's fallbacks and greybox effects; Shader Graph for the effect pack |
| com.unity.inputsystem | 1.16.0 | `DungeonGen.Unity.Input` |
| com.unity.test-framework | 1.6.0 | Unity EditMode/PlayMode tests under `Assets/Tests` |
| com.unity.ugui | 2.0.0 | The real HUD (wave 2) |
| com.unity.ai.navigation, timeline, visualscripting, multiplayer.center, collab-proxy, ide.* | template | Unused by the package |
| com.kcopeland.dungeongen | file:../../Random-dungeon-generator | This package |

Project settings that matter: Active Input Handling = Both (the greybox HUD is IMGUI);
Scripting Define `FISHNET` only once Fishnet is installed.

## Third-party assets in the Unity project

| Asset | Where | Used for |
|-------|-------|----------|
| Piloto Studio Elemental VFX Mega Bundle (1,786 prefabs, Shader Graph) | `Assets/Piloto Studio` | Ability effects via `AbilityVfxLibrary`; chosen in the audition |
| Packs on disk, not yet imported | `D:\Unity Assets\*.unitypackage` | Animations (RPG, Paladin, Archer, Caster, Synty locomotion), weapons, environments, monsters |

## External services (keys in `D:\unity dungeon game\.secrets\keys.env`)

| Service | Endpoint | Used by | Budget |
|---------|----------|---------|--------|
| Meshy text-to-3D | `POST/GET https://api.meshy.ai/openapi/v2/text-to-3d` (preview → refine), `GET /openapi/v1/balance` | `Tools/AssetGen/meshy.py` | 7 keys, 6,383 credits on 2026-10-25; ceiling 4,500 |
| Meshy rigging | `POST/GET https://api.meshy.ai/openapi/v1/rigging` (rigged GLB/FBX, walk and run) | same | 5 per rig |
| Meshy animation | `/openapi/v1/animations` (3 credits per action) | same | |
| ElevenLabs sound effects | `POST https://api.elevenlabs.io/v1/sound-generation` | `Tools/AssetGen/elevenlabs.py` | 84,600 characters on 2026-10-25; ceiling 60,000 |
| ElevenLabs speech | `POST https://api.elevenlabs.io/v1/text-to-speech/{voice_id}` (announcer voice Daniel `onwK4e9ZLuTAKqWW03F9`, boss voice Kallixis `cPoqAvGWCPfCfyPMwe4z`, model `eleven_multilingual_v2`) | same | |
| ElevenLabs music | `POST https://api.elevenlabs.io/v1/music` (three 60 s tracks: exploration, combat, boss) | same | ~3.8k characters used for all 90 files on 2026-10-26 |
| GitHub | `kcopelandmgmt/Random-dungeon-generator`, branch `claude/practical-wright-h5g0so` | git push | |

## Tools on the machine

| Tool | Path | Used for |
|------|------|----------|
| Unity 6000.3.0f1 | `C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe` | Headless compile, tests, imports, builds through `Tools/Build/unity-batch.sh`. The editor GUI is never opened by the program. |
| .NET SDK 8.0.426 | `C:\Program Files\dotnet\dotnet.exe` | `dotnet test`, the console tool |
| Python 3.12 / 3.14 | on PATH (`python`) | `Tools/AssetGen` scripts (standard library only: urllib, json) |
| Git, gh | on PATH | Both repositories |

## Build output

`Directory.Build.props` sends all .NET output to `bin~/` and `obj~/`, which Unity ignores.
Unity player builds go to `D:\unity dungeon game\dungeongame\Builds\Win` (ignored by git).
