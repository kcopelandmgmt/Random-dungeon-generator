using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DungeonGen.Unity.Editor
{
    /// <summary>
    /// Headless Windows player build for the orchestrator: <c>Tools/Build/unity-batch.sh build-win</c> calls
    /// <see cref="Windows"/>. A development build, because the practice assembly only exists in the editor and in
    /// development builds. Player settings the smoke test needs are set here, in code, never by hand.
    /// </summary>
    public static class PlayerBuild
    {
        public const string ScenePath = "Assets/Scenes/SampleScene.unity";
        public const string OutputRelative = "Builds/Win/DungeonGame.exe";

        public static void Windows()
        {
            try
            {
                var projectRoot = Directory.GetParent(Application.dataPath).FullName;
                var output = Path.Combine(projectRoot, OutputRelative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(output));

                ApplySettings();

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = output,
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.Development,
                };
                var report = BuildPipeline.BuildPlayer(options);
                var summary = report.summary;
                Debug.Log($"Dungeon Gen: player build {summary.result}, {summary.totalSize / (1024.0 * 1024.0):0.0} MB, {summary.totalTime.TotalSeconds:0.0} s, {summary.totalErrors} errors, {summary.totalWarnings} warnings -> {summary.outputPath}");
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
