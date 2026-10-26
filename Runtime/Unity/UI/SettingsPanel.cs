using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DungeonGen.Unity.UI
{
    /// <summary>
    /// The settings card, shared by the main menu and the pause menu. Three tabs: Audio (three volumes), Graphics (quality
    /// preset, window mode, resolution, vsync, frame cap, render scale, shadows) and Controls (every gameplay key, each with
    /// a Rebind button that waits for the next key; a key already in use is swapped, never doubled). "Reset to defaults"
    /// resets the tab you are on. Everything is stored through <see cref="GameAudioSettings"/>, <see cref="GameSettings"/>
    /// and <see cref="GameBindings"/> and takes effect at once. Controls are plain buttons and sliders, so a gamepad or
    /// arrow keys and Enter drive the whole card (see <see cref="MenuNavigator"/>); B / Escape presses Back.
    /// </summary>
    public static class SettingsPanel
    {
        public const string AudioTab = "Audio", GraphicsTab = "Graphics", ControlsTab = "Controls";

        private const float PanelW = 1280f, PanelH = 960f;
        private const float RowX = 110f, ControlX = 590f, ControlW = 574f;

        public static RectTransform Build(Transform parent, Action onBack) => Build(parent, onBack, AudioTab);

        /// <summary>Build the card on the given tab (Audio, Graphics or Controls).</summary>
        public static RectTransform Build(Transform parent, Action onBack, string tab)
        {
            var p = UiStyle.Panel(parent, "SettingsPanel", new Color(0.060f, 0.052f, 0.060f, 1f));   // opaque: the menu title must not show through
            p.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PanelW, PanelH));
            var ui = new Card { Panel = p };

            var title = UiStyle.Label(p, "Title", "SETTINGS", 52, UiStyle.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            title.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(700f, 70f));
            UiStyle.Rule(p, "Rule", 1080f, -112f);

            string[] tabs = { AudioTab, GraphicsTab, ControlsTab };
            for (int i = 0; i < tabs.Length; i++)
            {
                string name = tabs[i];
                var b = UiStyle.MakeButton(p, name + "Tab", name, () => ui.ShowTab(name), 28);
                ((RectTransform)b.transform).Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2((i - 1) * 300f, -128f), new Vector2(280f, 58f));
                ui.TabButtons[name] = b;
            }

            ui.Content = UiStyle.Node(p, "Content");
            ui.Content.Stretch(0f, 0f, 205f, 160f);

            ui.Message = UiStyle.Label(p, "Message", "", 22, UiStyle.Gold, TextAnchor.MiddleCenter);
            ui.Message.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 112f), new Vector2(1100f, 34f));

            var back = UiStyle.MakeButton(p, "BackButton", "Back", onBack);
            ((RectTransform)back.transform).Place(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(60f, 36f), new Vector2(300f, 64f));
            var reset = UiStyle.MakeButton(p, "ResetButton", "Reset to defaults", ui.ResetCurrent, 26);
            ((RectTransform)reset.transform).Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-60f, 36f), new Vector2(360f, 64f));
            var hint = UiStyle.Label(p, "Hint", "B / Esc: back", 18, UiStyle.TextFaint, TextAnchor.MiddleCenter);
            hint.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 56f), new Vector2(300f, 28f));

            ui.ShowTab(tab);
            ui.Built = true;   // from here on a tab change also moves the keyboard / gamepad focus onto the tab
            return p;
        }

        // ------------------------------------------------------------------------------------------------

        private sealed class Card
        {
            public RectTransform Panel, Content;
            public Text Message;
            public readonly Dictionary<string, Button> TabButtons = new Dictionary<string, Button>();
            public string Tab;
            public bool Built;
            private Image _tabMark;
            private readonly List<Action> _refresh = new List<Action>();
            private Action _resetTab;

            public void ShowTab(string tab)
            {
                Tab = tab;
                for (int i = Content.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(Content.GetChild(i).gameObject);
                _refresh.Clear(); _resetTab = null;
                Message.text = "";
                foreach (var kv in TabButtons)
                {
                    bool active = kv.Key == tab;
                    kv.Value.SetSelected(active);
                    var label = kv.Value.GetComponentInChildren<Text>(true);
                    if (label != null) label.color = active ? UiStyle.Gold : UiStyle.TextDim;
                    if (active)
                    {
                        // The active tab leads the hierarchy, so the menu navigator's "first control" is the tab you are on, not always Audio.
                        kv.Value.transform.SetAsFirstSibling();
                        if (_tabMark == null) _tabMark = UiStyle.Img(Panel, "TabMark", UiStyle.Gold, UiStyle.White);
                        var rt = _tabMark.rectTransform; var tabRt = (RectTransform)kv.Value.transform;
                        rt.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(tabRt.anchoredPosition.x, tabRt.anchoredPosition.y - tabRt.sizeDelta.y - 4f), new Vector2(tabRt.sizeDelta.x, 5f));
                    }
                }
                if (Built && EventSystem.current != null && TabButtons.TryGetValue(tab, out var activeButton)) EventSystem.current.SetSelectedGameObject(activeButton.gameObject);
                // Build into a fresh child: the old content is destroyed at the end of the frame.
                var holder = UiStyle.Node(Content, "Tab-" + tab);
                holder.Stretch();
                switch (tab)
                {
                    case GraphicsTab: BuildGraphics(holder); break;
                    case ControlsTab: BuildControls(holder); break;
                    default: BuildAudio(holder); break;
                }
            }

            private void Refresh() { foreach (var r in _refresh) r(); }
            public void ResetCurrent() { _resetTab?.Invoke(); Refresh(); }
            private void Say(string text, bool bad = false) { if (Message == null) return; Message.text = text; Message.color = bad ? UiStyle.Bad : UiStyle.Gold; }

            // ---------------- audio ----------------

            private void BuildAudio(RectTransform c)
            {
                var master = SliderRow(c, "Master", "Master volume", 30f, GameAudioSettings.Master, v => GameAudioSettings.Master = v);
                var music = SliderRow(c, "Music", "Music", 130f, GameAudioSettings.Music, v => GameAudioSettings.Music = v);
                var sfx = SliderRow(c, "Sfx", "Effects and voice", 230f, GameAudioSettings.Sfx, v => GameAudioSettings.Sfx = v);
                Note(c, "Volumes apply at once and are remembered.", 360f);
                _resetTab = () =>
                {
                    GameAudioSettings.Master = 1f; GameAudioSettings.Music = 1f; GameAudioSettings.Sfx = 1f;
                    master(1f); music(1f); sfx(1f);
                    Say("Audio reset to defaults.");
                };
            }

            // ---------------- graphics ----------------

            private void BuildGraphics(RectTransform c)
            {
                const float pitch = 78f;
                float y = 26f;
                Chooser(c, "Quality", "Quality preset", y, () => GameSettings.Quality.ToString(), step =>
                {
                    GameSettings.Quality = (GraphicsQuality)(((int)GameSettings.Quality + step + 3) % 3);
                    Refresh();
                });
                y += pitch;
                Toggle(c, "Display", "Display mode", y, () => GameSettings.Fullscreen ? "Full screen" : "Windowed", () => { GameSettings.Fullscreen = !GameSettings.Fullscreen; Refresh(); });
                y += pitch;
                Button resLeft = null, resRight = null;
                var resText = Chooser(c, "Resolution", "Resolution", y, ResolutionText, step =>
                {
                    var list = GameSettings.Resolutions();
                    if (list.Count == 0) return;
                    int i = Mathf.Max(0, list.IndexOf(CurrentWindowSize()));
                    GameSettings.WindowSize = list[(i + step + list.Count) % list.Count];
                    Refresh();
                }, out resLeft, out resRight);
                _refresh.Add(() => { bool windowed = !GameSettings.Fullscreen; resLeft.interactable = windowed; resRight.interactable = windowed; resText.color = windowed ? UiStyle.TextMain : UiStyle.TextFaint; });
                y += pitch;
                Toggle(c, "VSync", "Vertical sync", y, () => GameSettings.VSync ? "On" : "Off", () => { GameSettings.VSync = !GameSettings.VSync; Refresh(); });
                y += pitch;
                Chooser(c, "FrameCap", "Frame rate cap", y, () =>
                {
                    string s = GameSettings.FrameCap <= 0 ? "Unlimited" : GameSettings.FrameCap + " fps";
                    return GameSettings.VSync ? s + "  (vsync decides)" : s;
                }, step =>
                {
                    int i = Array.IndexOf(GameSettings.FrameCaps, GameSettings.FrameCap);
                    if (i < 0) i = Array.IndexOf(GameSettings.FrameCaps, GameSettings.DefaultFrameCap);
                    GameSettings.FrameCap = GameSettings.FrameCaps[(i + step + GameSettings.FrameCaps.Length) % GameSettings.FrameCaps.Length];
                    Refresh();
                });
                y += pitch;
                var setScale = SliderRow(c, "RenderScale", "Render scale", y, ScaleToSlider(GameSettings.RenderScale), v => GameSettings.RenderScale = SliderToScale(v), v => Mathf.RoundToInt(SliderToScale(v) * 100f) + "%");
                _refresh.Add(() => setScale(ScaleToSlider(GameSettings.RenderScale)));
                y += pitch;
                Toggle(c, "Shadows", "Shadows", y, () => GameSettings.Shadows ? "On" : "Off", () => { GameSettings.Shadows = !GameSettings.Shadows; Refresh(); });
                Note(c, "Picking a preset sets the render scale and shadows; change them afterwards if you like. Resolution applies to windowed mode.", y + 74f);
                _resetTab = () => { GameSettings.ResetGraphics(); Say("Graphics reset to defaults."); };
                Refresh();
            }

            private static Vector2Int CurrentWindowSize()
            {
                var w = GameSettings.WindowSize;
                return w.x > 0 ? w : new Vector2Int(Screen.width, Screen.height);
            }

            private static string ResolutionText() =>
                GameSettings.Fullscreen ? "Desktop (borderless)" : CurrentWindowSize().x + " x " + CurrentWindowSize().y;

            private static float ScaleToSlider(float scale) => Mathf.InverseLerp(GameSettings.MinRenderScale, GameSettings.MaxRenderScale, scale);
            private static float SliderToScale(float v) => Mathf.Lerp(GameSettings.MinRenderScale, GameSettings.MaxRenderScale, v);

            // ---------------- controls ----------------

            private readonly Dictionary<BindingAction, Button> _keyButtons = new Dictionary<BindingAction, Button>();

            private void BuildControls(RectTransform c)
            {
                _keyButtons.Clear();
                float leftY = 0f, rightY = 0f;
                string last = null;
                foreach (var info in GameBindings.All)
                {
                    bool left = info.Group != "Actions";
                    float x = left ? 70f : 660f;
                    ref float y = ref (left ? ref leftY : ref rightY);
                    if (info.Group != last)
                    {
                        if (y > 0f) y += 14f;
                        var h = UiStyle.Label(c, info.Group + "Heading", info.Group.ToUpperInvariant(), 20, UiStyle.GoldDim, TextAnchor.MiddleLeft, FontStyle.Bold);
                        h.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(x, -(y + 18f)), new Vector2(500f, 30f));
                        y += 38f;
                        last = info.Group;
                    }
                    var action = info.Action;
                    var l = UiStyle.Label(c, info.Id + "Label", info.Label, 25, UiStyle.TextMain);
                    l.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(x, -(y + 23f)), new Vector2(260f, 44f));
                    var b = UiStyle.MakeButton(c, "Key-" + info.Id, GameBindings.DisplayName(action), () => BeginRebind(action), 24);
                    ((RectTransform)b.transform).Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(x + 270f, -(y + 23f)), new Vector2(240f, 46f));
                    _keyButtons[action] = b;
                    y += 52f;
                }
                // Controller summary under the right column.
                var pad = UiStyle.Label(c, "GamepadNote",
                    "<color=#" + UiStyle.Hex(UiStyle.GoldDim) + "><b>GAMEPAD</b></color>\n" +
                    "Left stick moves, right trigger attacks, left trigger Bolt\n" +
                    "Bumpers: Zone and Mobility.  Y: Ultimate.  A: Interact\n" +
                    "X: Ping.  D-pad: potions, camera zoom.  Select: map\n" +
                    "Gamepad buttons are fixed.", 20, UiStyle.TextDim, TextAnchor.UpperLeft);
                pad.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(660f, -(rightY + 24f)), new Vector2(520f, 150f));
                _refresh.Add(RefreshKeys);
                _resetTab = () => { GameBindings.ResetAll(); Say("Controls reset to defaults."); };
                RefreshKeys();
            }

            private void RefreshKeys()
            {
                foreach (var kv in _keyButtons)
                {
                    if (kv.Value == null) continue;
                    kv.Value.SetLabel(GameBindings.DisplayName(kv.Key));
                    var t = kv.Value.GetComponentInChildren<Text>(true);
                    if (t != null) t.color = GameBindings.IsDefault(kv.Key) ? UiStyle.TextMain : UiStyle.Gold;
                    kv.Value.SetSelected(!GameBindings.IsDefault(kv.Key));
                }
            }

            private void BeginRebind(BindingAction action)
            {
                if (GameBindings.Capturing) return;
                var info = GameBindings.Info(action);
                var overlay = UiStyle.Img(Panel, "RebindOverlay", new Color(0.030f, 0.026f, 0.034f, 1f), UiStyle.Rounded);
                overlay.raycastTarget = true;   // swallows clicks while listening
                overlay.rectTransform.Stretch();
                var t1 = UiStyle.Label(overlay.transform, "Prompt", "Press the new key for", 30, UiStyle.TextDim, TextAnchor.MiddleCenter);
                t1.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 70f), new Vector2(900f, 44f));
                var t2 = UiStyle.Label(overlay.transform, "Action", info.Label.ToUpperInvariant(), 64, UiStyle.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
                t2.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(900f, 90f));
                var t3 = UiStyle.Label(overlay.transform, "Cancel", "Esc cancels.  A key already in use swaps with its owner.", 24, UiStyle.TextDim, TextAnchor.MiddleCenter);
                t3.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -70f), new Vector2(1000f, 40f));
                var ov = overlay.gameObject.AddComponent<RebindOverlay>();
                bool started = GameBindings.BeginCapture(path =>
                {
                    var r = GameBindings.Rebind(action, path);
                    RefreshKeys();
                    if (!r.Ok) Say(r.Error, true);
                    else if (r.SwappedWith.HasValue)
                        Say(GameBindings.DisplayName(path) + " was " + GameBindings.Info(r.SwappedWith.Value).Label + ": the two swapped. " + GameBindings.Info(r.SwappedWith.Value).Label + " is now " + GameBindings.DisplayName(r.SwappedWith.Value) + ".");
                    else if (r.Changed) Say(info.Label + " is now " + GameBindings.DisplayName(path) + ".");
                    ov.CloseSoon();
                }, () => { Say("Rebind cancelled."); ov.CloseSoon(); });
                if (!started) UnityEngine.Object.Destroy(overlay.gameObject);
            }

            // ---------------- row helpers ----------------

            private static Text RowLabel(RectTransform c, string name, string label, float y)
            {
                var l = UiStyle.Label(c, name + "Label", label, 28, UiStyle.TextMain);
                l.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(RowX, -y), new Vector2(440f, 48f));
                return l;
            }

            private static void Note(RectTransform c, string text, float y)
            {
                var n = UiStyle.Label(c, "Note", text, 20, UiStyle.TextFaint, TextAnchor.UpperLeft, FontStyle.Italic);
                n.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(RowX, -y), new Vector2(PanelW - 2f * RowX, 60f));
            }

            /// <summary>A slider row. Returns a setter that moves the slider without firing its callback.</summary>
            private static Action<float> SliderRow(RectTransform c, string name, string label, float y, float value, Action<float> set, Func<float, string> format = null)
            {
                format = format ?? (v => Mathf.RoundToInt(v * 100f) + "%");
                RowLabel(c, name, label, y);
                var pct = UiStyle.Label(c, name + "Value", format(value), 26, UiStyle.TextDim, TextAnchor.MiddleRight);
                pct.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(ControlX + ControlW - 110f, -y), new Vector2(110f, 44f));
                var s = UiStyle.MakeSlider(c, name + "Slider", value, v => { set(v); pct.text = format(v); });
                ((RectTransform)s.transform).Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(ControlX, -y), new Vector2(ControlW - 130f, 40f));
                return v => { s.SetValueWithoutNotify(v); pct.text = format(v); };
            }

            /// <summary>A "&lt; value &gt;" row. <paramref name="step"/> gets -1 or +1. Returns the value text so the caller can dim it.</summary>
            private Text Chooser(RectTransform c, string name, string label, float y, Func<string> text, Action<int> step) => Chooser(c, name, label, y, text, step, out _, out _);

            private Text Chooser(RectTransform c, string name, string label, float y, Func<string> text, Action<int> step, out Button left, out Button right)
            {
                RowLabel(c, name, label, y);
                var value = UiStyle.Label(c, name + "Value", text(), 28, UiStyle.TextMain, TextAnchor.MiddleCenter);
                value.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(ControlX + 76f, -y), new Vector2(ControlW - 152f, 48f));
                left = UiStyle.MakeButton(c, name + "Prev", "<", () => step(-1), 30);
                ((RectTransform)left.transform).Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(ControlX, -y), new Vector2(66f, 54f));
                right = UiStyle.MakeButton(c, name + "Next", ">", () => step(1), 30);
                ((RectTransform)right.transform).Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(ControlX + ControlW - 66f, -y), new Vector2(66f, 54f));
                _refresh.Add(() => value.text = text());
                return value;
            }

            /// <summary>A row with one wide button that flips a value; the button shows the value.</summary>
            private void Toggle(RectTransform c, string name, string label, float y, Func<string> text, Action flip)
            {
                RowLabel(c, name, label, y);
                var b = UiStyle.MakeButton(c, name + "Toggle", text(), flip, 28);
                ((RectTransform)b.transform).Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(ControlX, -y), new Vector2(ControlW, 54f));
                _refresh.Add(() => b.SetLabel(text()));
            }
        }

        /// <summary>On the "press a key" cover: closes shortly after the answer (so the click that gave it does not reach the card) and cancels the listener if the card goes away.</summary>
        private sealed class RebindOverlay : MonoBehaviour
        {
            private float _closeAt = -1f;
            public void CloseSoon() { _closeAt = Time.unscaledTime + 0.3f; }
            private void Update() { if (_closeAt >= 0f && Time.unscaledTime >= _closeAt) Destroy(gameObject); }
            private void OnDestroy() { GameBindings.CancelCapture(); }
        }
    }
}
