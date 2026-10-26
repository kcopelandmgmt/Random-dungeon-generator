using System;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonGen.Unity.UI
{
    /// <summary>The settings card, shared by the main menu and the pause menu: three volumes, the window mode, back.</summary>
    public static class SettingsPanel
    {
        public static RectTransform Build(Transform parent, Action onBack)
        {
            var p = UiStyle.Panel(parent, "SettingsPanel");
            p.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 600f));
            var title = UiStyle.Label(p, "Title", "SETTINGS", 44, UiStyle.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            title.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(700f, 60f));
            UiStyle.Rule(p, "Rule", 640f, -110f);

            Row(p, "Master", "Master volume", -170f, GameAudioSettings.Master, v => GameAudioSettings.Master = v);
            Row(p, "Music", "Music", -250f, GameAudioSettings.Music, v => GameAudioSettings.Music = v);
            Row(p, "Sfx", "Effects and voice", -330f, GameAudioSettings.Sfx, v => GameAudioSettings.Sfx = v);

            Button fs = null;
            fs = UiStyle.MakeButton(p, "FullscreenButton", FsLabel(), () => { GameSettings.Fullscreen = !GameSettings.Fullscreen; fs.SetLabel(FsLabel()); }, 24);
            ((RectTransform)fs.transform).Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -400f), new Vector2(420f, 58f));

            var back = UiStyle.MakeButton(p, "BackButton", "Back", onBack);
            ((RectTransform)back.transform).Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(320f, 64f));
            return p;
        }

        private static string FsLabel() => "Display: " + (GameSettings.Fullscreen ? "Full screen" : "Windowed");

        private static void Row(RectTransform p, string name, string label, float y, float value, Action<float> set)
        {
            var l = UiStyle.Label(p, name + "Label", label, 26, UiStyle.TextMain);
            l.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(70f, y), new Vector2(260f, 40f));
            var pct = UiStyle.Label(p, name + "Value", Pct(value), 24, UiStyle.TextDim, TextAnchor.MiddleRight);
            pct.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-70f, y), new Vector2(90f, 40f));
            var s = UiStyle.MakeSlider(p, name + "Slider", value, v => { set(v); pct.text = Pct(v); });
            ((RectTransform)s.transform).Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(340f, y), new Vector2(300f, 40f));
        }

        private static string Pct(float v) => Mathf.RoundToInt(v * 100f) + "%";
    }
}
