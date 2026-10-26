using System;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// The player's non-audio options, kept in PlayerPrefs: for now the window mode. Volumes live in
    /// <see cref="GameAudioSettings"/> (owned by the audio layer); the settings screen writes both.
    /// </summary>
    public static class GameSettings
    {
        public const string FullscreenKey = "dg.display.fullscreen";

        /// <summary>Raised after any value here changes.</summary>
        public static event Action Changed;

        private static bool? _fullscreen;

        /// <summary>Full screen (borderless at the desktop resolution) or a window. Applied to <see cref="Screen"/> when set.</summary>
        public static bool Fullscreen
        {
            get
            {
                if (!_fullscreen.HasValue)
                {
                    bool v = false;
                    try { v = PlayerPrefs.GetInt(FullscreenKey, Screen.fullScreenMode != FullScreenMode.Windowed ? 1 : 0) != 0; } catch (Exception) { }
                    _fullscreen = v;
                }
                return _fullscreen.Value;
            }
            set
            {
                if (Fullscreen == value) return;
                _fullscreen = value;
                try { PlayerPrefs.SetInt(FullscreenKey, value ? 1 : 0); PlayerPrefs.Save(); } catch (Exception) { }
                ApplyDisplay();
                Changed?.Invoke();
            }
        }

        /// <summary>Put the window in the saved mode. Skipped in the editor and in batch runs.</summary>
        public static void ApplyDisplay()
        {
            if (Application.isEditor || Application.isBatchMode) return;
            if (Fullscreen) Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, FullScreenMode.FullScreenWindow);
            else Screen.fullScreenMode = FullScreenMode.Windowed;
        }
    }
}
