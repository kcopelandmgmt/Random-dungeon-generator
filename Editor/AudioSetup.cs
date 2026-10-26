using UnityEditor;
using UnityEngine;

namespace DungeonGen.Unity.Editor
{
    /// <summary>Greybox step: puts <see cref="GameAudio"/> on the Dungeon root, wired to the round and to the built library.</summary>
    public static class AudioSetup
    {
        [GreyboxStep(20)]
        public static void Step(GreyboxContext ctx)
        {
            var lib = AssetDatabase.LoadAssetAtPath<AudioLibrary>(AudioLibraryBuilder.LibraryPath);
            if (lib == null) Debug.LogWarning("Dungeon Gen audio: no AudioLibrary at " + AudioLibraryBuilder.LibraryPath + "; run AudioLibraryBuilder.Build first. GameAudio is added without one.");
            var audio = ctx.Root.GetComponent<GameAudio>();
            if (audio == null) audio = ctx.Root.AddComponent<GameAudio>();
            audio.Library = lib; audio.World = ctx.World; audio.Builder = ctx.Builder; audio.Bootstrap = ctx.Bootstrap; audio.Effects = ctx.Effects;

            var cam = ctx.Camera != null && ctx.Camera.Camera != null ? ctx.Camera.Camera : Camera.main;
            if (cam != null)
            {
                if (cam.GetComponent<AudioListener>() == null && Object.FindFirstObjectByType<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
            }
            else Debug.LogWarning("Dungeon Gen audio: no camera to hold an AudioListener.");
        }
    }
}
