using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DungeonGen.Unity.Editor
{
    /// <summary>
    /// Headless Windows player builds for the orchestrator. <c>Tools/Build/unity-batch.sh build-win</c> calls
    /// <see cref="WindowsRelease"/> (non-development, into <c>Builds/Win</c>: the game a person plays, without the practice
    /// assembly); <c>build-win-dev</c> calls <see cref="WindowsDev"/> (development, into <c>Builds/WinDev</c>: with the practice
    /// assembly, so <c>-autotest</c> works). <see cref="Windows"/> is the old name for the dev build. Player settings the smoke
    /// tests need are set here, in code, never by hand.
    /// </summary>
    public static class PlayerBuild
    {
        public const string ScenePath = "Assets/Scenes/SampleScene.unity";
        public const string MenuScenePath = "Assets/Scenes/MainMenu.unity";
        /// <summary>The build list: the main menu boots first, the round scene second (-autotest skips the menu).</summary>
        public static readonly string[] Scenes = { MenuScenePath, ScenePath };
        /// <summary>The release player.</summary>
        public const string OutputRelative = "Builds/Win/DungeonGame.exe";
        /// <summary>The development player (practice assembly, -autotest).</summary>
        public const string DevOutputRelative = "Builds/WinDev/DungeonGame.exe";

        /// <summary>Alias for <see cref="WindowsDev"/>, kept so older calls still build the development player.</summary>
        public static void Windows() => WindowsDev();

        public static void WindowsDev() => Build(DevOutputRelative, BuildOptions.Development);

        public static void WindowsRelease() => Build(OutputRelative, BuildOptions.None);

        private static void Build(string outputRelative, BuildOptions buildOptions)
        {
            try
            {
                var projectRoot = Directory.GetParent(Application.dataPath).FullName;
                var output = Path.Combine(projectRoot, outputRelative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(output));

                ApplySettings();

                var options = new BuildPlayerOptions
                {
                    scenes = Scenes,
                    locationPathName = output,
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = buildOptions,
                };
                var report = BuildPipeline.BuildPlayer(options);
                var summary = report.summary;
                Debug.Log($"Dungeon Gen: {(buildOptions.HasFlag(BuildOptions.Development) ? "development" : "release")} player build {summary.result}, {summary.totalSize / (1024.0 * 1024.0):0.0} MB, {summary.totalTime.TotalSeconds:0.0} s, {summary.totalErrors} errors, {summary.totalWarnings} warnings -> {summary.outputPath}");
                if (summary.result != BuildResult.Succeeded)
                {
                    foreach (var step in report.steps)
                        foreach (var m in step.messages.Where(m => m.type == LogType.Error || m.type == LogType.Exception))
                            Debug.LogError("Dungeon Gen: build error: " + m.content);
                    EditorApplication.Exit(1);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("Dungeon Gen: player build threw: " + e);
                EditorApplication.Exit(1);
            }
        }

        private static void ApplySettings()
        {
            PlayerSettings.companyName = "kcopelandmgmt";
            PlayerSettings.productName = "Dungeon Game";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            // Direct3D 11 only on Windows. The Unity 6 default (Direct3D 12 first) makes the development player die with an access
            // violation inside D3D12Core.dll while it tears the device down (exit code 139) once many particle systems have been
            // drawn; the same build under Direct3D 11 exits 0 every time.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
            // Active Input Handling is deliberately left alone (it must stay Both).
            IncludeShaders("Universal Render Pipeline/Lit", "Universal Render Pipeline/Unlit", "Universal Render Pipeline/Simple Lit", "Unlit/Color", "Standard", "Sprites/Default");
        }

        /// <summary>
        /// The greybox builds its fallback materials with Shader.Find at runtime, and a shader nothing references is stripped
        /// from a player. Listing them under Always Included Shaders keeps them.
        /// </summary>
        private static void IncludeShaders(params string[] names)
        {
            var settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("ProjectSettings/GraphicsSettings.asset");
            if (settings == null) return;
            var so = new SerializedObject(settings);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            if (list == null) return;
            bool changed = false;
            foreach (var n in names)
            {
                var shader = Shader.Find(n);
                if (shader == null) continue;
                bool present = false;
                for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) { present = true; break; }
                if (present) continue;
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
                changed = true;
            }
            if (changed) { so.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets(); }
        }
    }
}
