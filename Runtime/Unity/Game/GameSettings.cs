using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace DungeonGen.Unity
{
    /// <summary>The three graphics presets the settings screen offers. They are values this class applies itself, not Unity quality levels.</summary>
    public enum GraphicsQuality { Low = 0, Medium = 1, High = 2 }

    /// <summary>
    /// The player's non-audio options, kept in PlayerPrefs: the window mode and resolution, the graphics options (preset,
    /// render scale, shadows, vsync, frame cap) and the key bindings (stored as text; the table that interprets them is
    /// <c>GameBindings</c> in the input assembly). Volumes live in <see cref="GameAudioSettings"/> (owned by the audio
    /// layer); the settings screen writes both. Display and graphics are applied to the engine on start and on every change,
    /// except in the editor and in batch runs, where they would rewrite the project's own assets.
    /// </summary>
    public static class GameSettings
    {
        public const string FullscreenKey = "dg.display.fullscreen";
        public const string ResolutionKey = "dg.display.resolution";
        public const string QualityKey = "dg.gfx.quality";
        public const string RenderScaleKey = "dg.gfx.renderscale";
        public const string ShadowsKey = "dg.gfx.shadows";
        public const string VSyncKey = "dg.gfx.vsync";
        public const string FrameCapKey = "dg.gfx.framecap";
        public const string BindingsKey = "dg.input.bindings";

        /// <summary>Frame caps the screen cycles through; 0 is unlimited.</summary>
        public static readonly int[] FrameCaps = { 0, 30, 60, 90, 120, 144, 165, 240 };
        public const float MinRenderScale = 0.5f, MaxRenderScale = 1f;
        public const int DefaultFrameCap = 144;

        /// <summary>Raised after any value here changes.</summary>
        public static event Action Changed;

        private static bool? _fullscreen;

        // ---------------- display ----------------

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

        /// <summary>The window size used when not full screen; (0, 0) means "leave the window as it is".</summary>
        public static Vector2Int WindowSize
        {
            get
            {
                string s = GetString(ResolutionKey, "");
                var parts = s.Split('x');
                return parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h) && w >= 320 && h >= 240 ? new Vector2Int(w, h) : Vector2Int.zero;
            }
            set
            {
                SetString(ResolutionKey, value.x > 0 && value.y > 0 ? value.x + "x" + value.y : "");
                ApplyDisplay();
                Changed?.Invoke();
            }
        }

        /// <summary>The sizes a window can be set to: the monitor's own modes (no smaller than 1280x720), ascending, with the saved size included.</summary>
        public static List<Vector2Int> Resolutions()
        {
            var list = new List<Vector2Int>();
            try
            {
                foreach (var r in Screen.resolutions)
                {
                    var v = new Vector2Int(r.width, r.height);
                    if (v.x >= 1280 && v.y >= 720 && !list.Contains(v)) list.Add(v);
                }
            }
            catch (Exception) { }
            foreach (var extra in new[] { WindowSize, new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(1920, 1080) })
                if (extra.x > 0 && !list.Contains(extra) && (list.Count == 0 || extra == WindowSize || extra.x <= list[list.Count - 1].x)) list.Add(extra);
            list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            return list;
        }

        /// <summary>Put the window in the saved mode and size. Skipped in the editor and in batch runs.</summary>
        public static void ApplyDisplay()
        {
            if (Application.isEditor || Application.isBatchMode) return;
            if (Fullscreen) Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, FullScreenMode.FullScreenWindow);
            else
            {
                var size = WindowSize;
                if (size.x > 0) Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);
                else Screen.fullScreenMode = FullScreenMode.Windowed;
            }
            ApplyGraphics();
        }

        // ---------------- graphics ----------------

        /// <summary>Low, Medium or High. Choosing one also sets the render scale and the shadows switch to its values.</summary>
        public static GraphicsQuality Quality
        {
            get => (GraphicsQuality)Mathf.Clamp(GetInt(QualityKey, (int)GraphicsQuality.High), 0, 2);
            set
            {
                SetInt(QualityKey, (int)value);
                SetFloat(RenderScaleKey, Preset(value).RenderScale);
                SetInt(ShadowsKey, Preset(value).Shadows ? 1 : 0);
                ApplyGraphics();
                Changed?.Invoke();
            }
        }

        /// <summary>Fraction of the screen resolution the world is rendered at (0.5 to 1).</summary>
        public static float RenderScale
        {
            get => Mathf.Clamp(GetFloat(RenderScaleKey, Preset(Quality).RenderScale), MinRenderScale, MaxRenderScale);
            set { SetFloat(RenderScaleKey, Mathf.Clamp(Mathf.Round(value * 20f) / 20f, MinRenderScale, MaxRenderScale)); ApplyGraphics(); Changed?.Invoke(); }
        }

        public static bool Shadows
        {
            get => GetInt(ShadowsKey, Preset(Quality).Shadows ? 1 : 0) != 0;
            set { SetInt(ShadowsKey, value ? 1 : 0); ApplyGraphics(); Changed?.Invoke(); }
        }

        public static bool VSync
        {
            get => GetInt(VSyncKey, 0) != 0;
            set { SetInt(VSyncKey, value ? 1 : 0); ApplyGraphics(); Changed?.Invoke(); }
        }

        /// <summary>Frames per second cap, 0 for none (ignored by the engine while vsync is on).</summary>
        public static int FrameCap
        {
            get => GetInt(FrameCapKey, DefaultFrameCap);
            set { SetInt(FrameCapKey, Mathf.Max(0, value)); ApplyGraphics(); Changed?.Invoke(); }
        }

        /// <summary>Back to the shipped graphics: High, vsync off, 144 fps cap, and the window as it is (not the full-screen switch).</summary>
        public static void ResetGraphics()
        {
            foreach (var k in new[] { QualityKey, RenderScaleKey, ShadowsKey, VSyncKey, FrameCapKey, ResolutionKey }) DeleteKey(k);
            ApplyGraphics();
            ApplyDisplay();
            Changed?.Invoke();
        }

        private struct QualityPreset { public float RenderScale; public bool Shadows; public float ShadowDistance; public int Cascades; }

        private static QualityPreset Preset(GraphicsQuality q)
        {
            switch (q)
            {
                case GraphicsQuality.Low: return new QualityPreset { RenderScale = 0.75f, Shadows = false, ShadowDistance = 20f, Cascades = 1 };
                case GraphicsQuality.Medium: return new QualityPreset { RenderScale = 1f, Shadows = true, ShadowDistance = 30f, Cascades = 2 };
                default: return new QualityPreset { RenderScale = 1f, Shadows = true, ShadowDistance = 0f, Cascades = 0 };   // High: the render pipeline asset's own values
            }
        }

        // What the render pipeline asset held before the first change, so High (and a reset) puts it back.
        private static readonly Dictionary<string, object> OriginalPipeline = new Dictionary<string, object>();
        private static readonly string[] PipelineProps = { "renderScale", "shadowDistance", "shadowCascadeCount", "supportsMainLightShadows" };

        /// <summary>
        /// Apply vsync, the frame cap and the render pipeline options (render scale, shadows on or off, shadow distance and
        /// cascades by preset). The pipeline asset is reached by reflection so this assembly needs no render-pipeline
        /// reference. Skipped in the editor and in batch runs unless <paramref name="force"/>; a forced run in the editor
        /// should be undone with <see cref="RestoreGraphics"/>.
        /// </summary>
        public static void ApplyGraphics(bool force = false)
        {
            if (!force && (Application.isEditor || Application.isBatchMode)) return;
            try
            {
                QualitySettings.vSyncCount = VSync ? 1 : 0;
                Application.targetFrameRate = FrameCap > 0 ? FrameCap : -1;
            }
            catch (Exception) { }
            var asset = GraphicsSettings.currentRenderPipeline;
            if (asset == null) { QualitySettings.shadows = Shadows ? ShadowQuality.All : ShadowQuality.Disable; return; }
            var type = asset.GetType();
            foreach (var n in PipelineProps) if (!OriginalPipeline.ContainsKey(n)) OriginalPipeline[n] = ReadProp(asset, type, n);
            var p = Preset(Quality);
            WriteProp(asset, type, "renderScale", RenderScale);
            WriteProp(asset, type, "supportsMainLightShadows", Shadows);
            if (Quality == GraphicsQuality.High)
            {
                if (OriginalPipeline["shadowDistance"] != null) WriteProp(asset, type, "shadowDistance", OriginalPipeline["shadowDistance"]);
                if (OriginalPipeline["shadowCascadeCount"] != null) WriteProp(asset, type, "shadowCascadeCount", OriginalPipeline["shadowCascadeCount"]);
            }
            else
            {
                WriteProp(asset, type, "shadowDistance", p.ShadowDistance);
                WriteProp(asset, type, "shadowCascadeCount", p.Cascades);
            }
        }

        /// <summary>Put the render pipeline asset back as it was before <see cref="ApplyGraphics"/> first touched it.</summary>
        public static void RestoreGraphics()
        {
            var asset = GraphicsSettings.currentRenderPipeline;
            if (asset == null) return;
            var type = asset.GetType();
            foreach (var kv in OriginalPipeline) if (kv.Value != null) WriteProp(asset, type, kv.Key, kv.Value);
            OriginalPipeline.Clear();
        }

        /// <summary>The render pipeline asset's current render scale, or false when it has none (diagnostics and tests).</summary>
        public static bool TryReadRenderScale(out float scale)
        {
            scale = 0f;
            var asset = GraphicsSettings.currentRenderPipeline;
            if (asset == null) return false;
            var v = ReadProp(asset, asset.GetType(), "renderScale");
            if (v is float f) { scale = f; return true; }
            return false;
        }

        private static object ReadProp(object target, Type type, string name)
        {
            try { return type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target); } catch (Exception) { return null; }
        }

        private static void WriteProp(object target, Type type, string name, object value)
        {
            try
            {
                var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.CanWrite) prop.SetValue(target, Convert.ChangeType(value, prop.PropertyType));
            }
            catch (Exception) { }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ApplyOnStart() => ApplyGraphics();

        // ---------------- key bindings ----------------

        /// <summary>
        /// The player's key bindings as saved text: lines of <c>ActionId=control path</c>, only for keys changed from their
        /// defaults (the table in the input assembly owns the ids and the defaults). Empty means all defaults.
        /// </summary>
        public static string BindingsText
        {
            get => GetString(BindingsKey, "");
            set { if (string.IsNullOrEmpty(value)) DeleteKey(BindingsKey); else SetString(BindingsKey, value); Changed?.Invoke(); }
        }

        // ---------------- PlayerPrefs, never throwing ----------------

        private static int GetInt(string key, int fallback) { try { return PlayerPrefs.GetInt(key, fallback); } catch (Exception) { return fallback; } }
        private static float GetFloat(string key, float fallback) { try { return PlayerPrefs.GetFloat(key, fallback); } catch (Exception) { return fallback; } }
        private static string GetString(string key, string fallback) { try { return PlayerPrefs.GetString(key, fallback); } catch (Exception) { return fallback; } }
        private static void SetInt(string key, int v) { try { PlayerPrefs.SetInt(key, v); PlayerPrefs.Save(); } catch (Exception) { } }
        private static void SetFloat(string key, float v) { try { PlayerPrefs.SetFloat(key, v); PlayerPrefs.Save(); } catch (Exception) { } }
        private static void SetString(string key, string v) { try { PlayerPrefs.SetString(key, v); PlayerPrefs.Save(); } catch (Exception) { } }
        private static void DeleteKey(string key) { try { PlayerPrefs.DeleteKey(key); PlayerPrefs.Save(); } catch (Exception) { } }
    }
}
