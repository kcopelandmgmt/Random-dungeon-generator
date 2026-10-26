using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DungeonGen.Unity.Editor
{
    /// <summary>
    /// Headless scene refresh: open SampleScene, delete the old Dungeon root and any top-down rig, rebuild through
    /// <see cref="GreyboxSceneSetup.Build"/> (which runs every <see cref="GreyboxStepAttribute"/> step), save. Run with
    /// <c>bash Tools/Build/unity-batch.sh method DungeonGen.Unity.Editor.GreyboxRebuild.Run</c> after any step changes.
    /// </summary>
    public static class GreyboxRebuild
    {
        public const string ScenePath = "Assets/Scenes/SampleScene.unity";

        [MenuItem("Dungeon Gen/Rebuild Greybox In SampleScene")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == "Dungeon") Object.DestroyImmediate(go);
            var main = Camera.main;
            if (main != null) { var rig = main.GetComponent<TopDownCamera>(); if (rig != null) Object.DestroyImmediate(rig); }
            GreyboxSceneSetup.Build();
            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene);
            Debug.Log($"Dungeon Gen: SampleScene rebuilt and {(saved ? "saved" : "NOT saved")}.");
            if (Application.isBatchMode && !saved) EditorApplication.Exit(1);
        }
    }
}
