using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DungeonGen.Unity.Editor
{
    /// <summary>
    /// Release builds only: removes components from the dev-only practice assembly (PracticeMode, VfxAudition, …) from every
    /// scene as it is built. The assembly is excluded from non-development players, so without this the scene would carry
    /// "missing script" references and log a warning for each one. Development builds keep them.
    /// </summary>
    public sealed class StripDevComponents : IProcessSceneWithReport
    {
        public const string DevAssembly = "DungeonGen.Unity.Practice";
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (!BuildPipeline.isBuildingPlayer || EditorUserBuildSettings.development) return;
            int removed = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var c in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (c == null) continue;
                    if (c.GetType().Assembly.GetName().Name != DevAssembly) continue;
                    Object.DestroyImmediate(c, true);
                    removed++;
                }
            if (removed > 0) Debug.Log($"Dungeon Gen: stripped {removed} dev-only component(s) from {scene.name} for the release build.");
        }
    }
}
