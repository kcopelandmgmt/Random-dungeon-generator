using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DungeonGen.Unity.Editor
{
    /// <summary>
    /// Generates <c>Assets/Scenes/MainMenu.unity</c>: a dark camera, the menu canvas (<c>MainMenuUi</c>, which builds its screens
    /// at runtime) and an EventSystem; then puts MainMenu first and SampleScene second in the editor's build list.
    /// Headless: <c>bash Tools/Build/unity-batch.sh method DungeonGen.Unity.Editor.MenuSceneBuilder.Build</c>.
    /// </summary>
    public static class MenuSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/MainMenu.unity";

        [MenuItem("Dungeon Gen/Build Main Menu Scene")]
        public static void Build()
        {
            try
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var camGo = new GameObject("MenuCamera") { tag = "MainCamera" };
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.03f, 0.026f, 0.034f, 1f);
                cam.orthographic = false; cam.nearClipPlane = 0.1f; cam.farClipPlane = 100f;
                var urpData = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
                if (urpData != null) camGo.AddComponent(urpData);
                camGo.AddComponent<AudioListener>();

                var t = Type.GetType(UiSetup.InstallerType);
                if (t == null) throw new InvalidOperationException("the UI assembly (DungeonGen.Unity.UI) is missing");
                t.GetMethod("InstallMenu").Invoke(null, null);

                bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.SaveAssets();
                SetBuildList();
                Debug.Log($"Dungeon Gen: main menu scene {(saved ? "saved" : "NOT saved")} at {ScenePath}.");
                if (Application.isBatchMode && !saved) EditorApplication.Exit(1);
            }
            catch (Exception e)
            {
                Debug.LogError("Dungeon Gen: main menu scene build failed: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        /// <summary>MainMenu first (the player boots into it), then SampleScene (the round); anything else already listed keeps its place after.</summary>
        public static void SetBuildList()
        {
            var wanted = PlayerBuild.Scenes;
            var rest = EditorBuildSettings.scenes.Where(s => !wanted.Contains(s.path));
            EditorBuildSettings.scenes = wanted.Select(p => new EditorBuildSettingsScene(p, true)).Concat(rest).ToArray();
        }
    }
}
