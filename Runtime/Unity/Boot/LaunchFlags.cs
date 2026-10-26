using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// Command-line flags for the player, read before the first scene loads.
    /// -mute: AudioListener silent and paused for the whole session. -minimized: minimise the window once it exists (Windows player).
    /// -autotest: the practice assembly's AutoSmokeTest runs a scripted scenario and quits. -shots &lt;dir&gt;: screenshot directory.
    /// </summary>
    public static class LaunchFlags
    {
        private static string[] _args;
        private static string[] Args => _args ??= Environment.GetCommandLineArgs();

        public static bool Has(string flag)
        {
            foreach (var a in Args) if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>The argument after <paramref name="flag"/>, or null.</summary>
        public static string Value(string flag)
        {
            var args = Args;
            for (int i = 0; i < args.Length - 1; i++) if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        public static bool Mute => Has("-mute");
        public static bool Minimized => Has("-minimized");
        public static bool AutoTest => Has("-autotest");
        public static string ShotsDirectory => Value("-shots");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            if (!Mute && !Minimized) return;
            var go = new GameObject("LaunchFlags") { hideFlags = HideFlags.HideInHierarchy };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<LaunchFlagsRunner>();
        }

        internal static void ApplyMute()
        {
            AudioListener.volume = 0f;
            AudioListener.pause = true;
        }
    }

    internal sealed class LaunchFlagsRunner : MonoBehaviour
    {
#pragma warning disable 0414, 0649
        private int _minimizeTries;
        private bool _minimized;
#pragma warning restore 0414, 0649

        private void Awake()
        {
            if (LaunchFlags.Mute)
            {
                LaunchFlags.ApplyMute();
                UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => LaunchFlags.ApplyMute();
            }
        }

        private void Update()
        {
            if (LaunchFlags.Mute) LaunchFlags.ApplyMute();   // cheap, and survives anything that resets the listener
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (LaunchFlags.Minimized && !_minimized && _minimizeTries < 10 && Time.frameCount >= 3)
            {
                _minimizeTries++;
                var hwnd = GetActiveWindow();
                if (hwnd == IntPtr.Zero) hwnd = FindOwnWindow();
                if (hwnd != IntPtr.Zero) { ShowWindow(hwnd, 7); _minimized = true; }   // SW_SHOWMINNOACTIVE: minimised without activating anything
            }
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint threadId, EnumProc proc, IntPtr lParam);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

        private static IntPtr FindOwnWindow()
        {
            IntPtr found = IntPtr.Zero;
            EnumThreadWindows(GetCurrentThreadId(), (h, l) => { found = h; return false; }, IntPtr.Zero);
            return found;
        }
#endif
    }
}
