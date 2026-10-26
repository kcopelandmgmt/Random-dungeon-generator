using System;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>Player volume settings, persisted in PlayerPrefs. Menus set these; <see cref="GameAudio"/> reads them every frame.</summary>
    public static class GameAudioSettings
    {
        private const string MasterKey = "audio.master", MusicKey = "audio.music", SfxKey = "audio.sfx";
        private static float? _master, _music, _sfx;

        /// <summary>Raised after any volume changes.</summary>
        public static event Action Changed;

        public static float Master { get => Get(ref _master, MasterKey); set => Set(ref _master, MasterKey, value); }
        public static float Music { get => Get(ref _music, MusicKey); set => Set(ref _music, MusicKey, value); }
        /// <summary>Sound effects, ambience, interface and announcer.</summary>
        public static float Sfx { get => Get(ref _sfx, SfxKey); set => Set(ref _sfx, SfxKey, value); }

        private static float Get(ref float? cache, string key)
        {
            if (!cache.HasValue)
            {
                float v = 1f;
                try { v = PlayerPrefs.GetFloat(key, 1f); } catch (Exception) { }
                cache = Mathf.Clamp01(v);
            }
            return cache.Value;
        }

        private static void Set(ref float? cache, string key, float value)
        {
            cache = Mathf.Clamp01(value);
            try { PlayerPrefs.SetFloat(key, cache.Value); PlayerPrefs.Save(); } catch (Exception) { }
            Changed?.Invoke();
        }

        /// <summary>Forget cached values so the next read comes from PlayerPrefs (tests).</summary>
        public static void ResetCache() { _master = _music = _sfx = null; }
    }
}
