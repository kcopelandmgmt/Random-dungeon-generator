using UnityEditor;
using UnityEngine;

namespace DungeonGen.Unity.Editor
{
    /// <summary>
    /// Dungeon Gen ▸ Build Greybox Scene: puts every component the greybox needs into the open scene,
    /// wired together, with a config asset created if none exists. Press Play, click "Host practice
    /// round", walk. No prefabs are required: the builder makes cubes for anything unassigned.
    /// The local player controller is added by reflection so this menu works whether or not the
    /// Input System package (and with it the Input assembly) is installed.
    /// </summary>
    public static class GreyboxSceneSetup
    {
        [MenuItem("Dungeon Gen/Build Greybox Scene")]
        public static void Build()
        {
            var config = AssetDatabase.LoadAssetAtPath<DungeonConfig>("Assets/DungeonConfig.asset");
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<DungeonConfig>();
                AssetDatabase.CreateAsset(config, "Assets/DungeonConfig.asset");
                AssetDatabase.SaveAssets();
            }
            var root = new GameObject("Dungeon");
            var builder = root.AddComponent<DungeonBuilder>();
            builder.Config = config; builder.BuildOnStart = false; builder.RandomizeSeed = false;
            var bootstrap = root.AddComponent<DungeonMatchBootstrap>();
            bootstrap.Config = config; bootstrap.Builder = builder;
            var server = root.AddComponent<ServerSimulation>();
            server.Bootstrap = bootstrap; server.Builder = builder;
            var world = root.AddComponent<ClientWorldView>();
            world.Bootstrap = bootstrap; world.Builder = builder;
            var hud = root.AddComponent<PlayerHud>();
            hud.World = world; hud.Bootstrap = bootstrap;
            var compass = root.AddComponent<CompassHud>();
            compass.Match = bootstrap; compass.Builder = builder; compass.World = world;
            var minimap = root.AddComponent<MiniMapHud>();
            minimap.Match = bootstrap; minimap.Builder = builder; minimap.World = world;

            // Reuse the scene's main camera (the URP template's, with its render-pipeline data) rather than adding a
            // second base camera: a bare camera created here rendered on top of IMGUI until it was toggled off and on.
            var cam = Camera.main;
            GameObject camGo;
            if (cam != null) camGo = cam.gameObject;
            else
            {
                camGo = new GameObject("TopDownCamera");
                cam = camGo.AddComponent<Camera>();
                var urpData = System.Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
                if (urpData != null) camGo.AddComponent(urpData);
            }
            var rig = camGo.GetComponent<TopDownCamera>() ?? camGo.AddComponent<TopDownCamera>();
            rig.Camera = cam;
            if (Object.FindFirstObjectByType<Light>() == null) { var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(55f, 30f, 0f); }

            var practice = root.AddComponent<PracticeMode>();
            practice.Bootstrap = bootstrap; practice.Builder = builder; practice.Server = server; practice.World = world; practice.Hud = hud; practice.Camera = rig;
            compass.Camera = rig;

            var controllerType = System.Type.GetType("DungeonGen.Unity.LocalPlayerController, DungeonGen.Unity.Input");
            if (controllerType != null)
            {
                var controller = root.AddComponent(controllerType);
                controllerType.GetField("Builder")?.SetValue(controller, builder);
                controllerType.GetField("Camera")?.SetValue(controller, rig);
                controllerType.GetField("World")?.SetValue(controller, world);
                controllerType.GetField("MiniMap")?.SetValue(controller, minimap);
                practice.IntentSource = controller as MonoBehaviour;
                compass.LocalPlayer = null; minimap.LocalPlayer = null;
            }
            else Debug.LogWarning("Dungeon Gen: the Input System package is not installed, so no LocalPlayerController was added. Install com.unity.inputsystem and run this menu again.");

            Selection.activeGameObject = root;
            Debug.Log("Dungeon Gen: greybox scene ready. Press Play and click 'Host practice round'.");
        }
    }
}
