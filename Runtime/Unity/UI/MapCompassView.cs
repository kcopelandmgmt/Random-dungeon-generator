using System.Collections.Generic;
using DungeonGen.Core.Hud;
using UnityEngine;
using UnityEngine.UI;
using HudCompass = DungeonGen.Core.Hud.Compass;   // UnityEngine.Compass is the phone sensor

namespace DungeonGen.Unity.UI
{
    /// <summary>
    /// The uGUI compass: a dark strip across the top of the screen with a tick every 15 degrees, the eight directions as letters, and below them a badge
    /// for each thing the team knows of (the nearest extract, boss rooms, shrines, vaults, team mates, pings) at its bearing from the player, with the
    /// distance in metres. The strip shows 180 degrees about the camera's up; team mates, pings and the extract that are behind you pin to its ends.
    /// Everything comes from <see cref="CompassHud"/>, which reads the client view.
    /// </summary>
    internal sealed class MapCompassView : MonoBehaviour
    {
        /// <summary>The strip's size and its distance from the top edge in reference pixels. The banner sits just under it (GameHud, 64 px down), the round clock is top right.</summary>
        public const float StripWidth = 640f, StripHeight = 58f, TopMargin = 6f;
        private const float LetterY = 13f, TickTop = 26f, MarkY = 43f, Pad = 14f;

        private CompassHud _hud;
        private RectTransform _strip, _clip;
        private readonly Image[] _ticks = new Image[24];
        private readonly Text[] _letters = new Text[8];
        private MapIcons.Pool _marks;
        private readonly List<Text> _dist = new List<Text>();
        private GameHud _gameHud; private float _gameHudAt = -10f;

        public static GameObject Create(CompassHud hud, RectTransform canvas)
        {
            var strip = UiStyle.Panel(canvas, "Compass", new Color(0.05f, 0.045f, 0.05f, 0.95f), true, false);
            strip.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -TopMargin), new Vector2(StripWidth, StripHeight));
            var view = strip.gameObject.AddComponent<MapCompassView>();
            view.Build(hud, strip);
            return strip.gameObject;
        }

        private void Build(CompassHud hud, RectTransform strip)
        {
            _hud = hud; _strip = strip;
            _clip = UiStyle.Node(strip, "Clip");
            _clip.Stretch(3f, 3f, 3f, 3f);
            _clip.gameObject.AddComponent<RectMask2D>();
            for (int i = 0; i < _ticks.Length; i++)
            {
                _ticks[i] = UiStyle.Img(_clip, "Tick-" + i * 15, UiStyle.GoldDim);
                _ticks[i].rectTransform.sizeDelta = new Vector2(1.5f, i % 3 == 0 ? 9f : 5f);
            }
            for (int i = 0; i < _letters.Length; i++)
            {
                bool main = i % 2 == 0;
                var c = (Cardinal)i;
                _letters[i] = UiStyle.Label(_clip, "Letter-" + HudCompass.Abbrev(c), HudCompass.Abbrev(c), main ? 25 : 16, main ? UiStyle.Gold : UiStyle.TextDim, TextAnchor.MiddleCenter, main ? FontStyle.Bold : FontStyle.Normal);
                _letters[i].rectTransform.sizeDelta = new Vector2(44f, 30f);
                _letters[i].horizontalOverflow = HorizontalWrapMode.Overflow;
            }
            var centre = UiStyle.Img(_clip, "Centre", UiStyle.Gold);   // where the screen's up points
            centre.rectTransform.sizeDelta = new Vector2(2f, 12f);
            centre.rectTransform.anchoredPosition = new Vector2(0f, Y(TickTop + 6f));
            _marks = new MapIcons.Pool(_clip, "Compass-Mark", 20f);
        }

        /// <summary>Strip y (down from the top edge) to anchored y inside the clip.</summary>
        private float Y(float fromTop) => StripHeight * 0.5f - 3f - fromTop;

        private void LateUpdate()
        {
            if (_hud == null) return;
            if (_gameHud == null && Time.unscaledTime - _gameHudAt > 1f) { _gameHudAt = Time.unscaledTime; _gameHud = FindFirstObjectByType<GameHud>(); }
            bool show = _hud.HasData && !(_gameHud != null && _gameHud.SummaryVisible);
            foreach (Transform c in _strip) if (c.gameObject.activeSelf != show) c.gameObject.SetActive(show);
            var img = _strip.GetComponent<Image>(); if (img != null && img.enabled != show) img.enabled = show;
            if (!show) return;

            float yaw = _hud.YawDegrees, half = StripWidth * 0.5f - Pad;
            for (int i = 0; i < _ticks.Length; i++)
            {
                float rel = HudCompass.Relative(i * 15f, yaw);
                bool inside = CompassBar.InWindow(rel);
                _ticks[i].enabled = inside;
                if (!inside) continue;
                _ticks[i].rectTransform.anchoredPosition = new Vector2(Edge(rel) * half, Y(TickTop) - _ticks[i].rectTransform.sizeDelta.y * 0.5f);
                var col = UiStyle.GoldDim; col.a = Fade(rel);
                _ticks[i].color = col;
            }
            for (int i = 0; i < _letters.Length; i++)
            {
                float rel = HudCompass.Relative(HudCompass.BearingOf((Cardinal)i), yaw);
                bool inside = CompassBar.InWindow(rel);
                _letters[i].enabled = inside;
                if (!inside) continue;
                _letters[i].rectTransform.anchoredPosition = new Vector2(Edge(rel) * half, Y(LetterY));
                var col = i % 2 == 0 ? UiStyle.Gold : UiStyle.TextDim; col.a = Fade(rel);
                _letters[i].color = col;
            }

            var st = _hud.Match != null ? _hud.Match.State : null;
            float metres = _hud.Builder != null ? _hud.Builder.CellSize : 2f;
            _marks.Begin();
            foreach (var m in _hud.Markers)
            {
                float rel = HudCompass.Relative(m.Bearing, yaw);
                bool inside = CompassBar.InWindow(rel), pinned = false;
                if (!inside)
                {
                    if (m.Kind != CompassMarkerKind.Teammate && m.Kind != CompassMarkerKind.Ping && m.Kind != CompassMarkerKind.Extract) continue;
                    rel = Mathf.Sign(rel) * (CompassBar.WindowDegrees * 0.5f); pinned = true;
                }
                bool dim = m.Kind == CompassMarkerKind.Extract ? !_hud.ExtractsOpen
                         : m.Kind == CompassMarkerKind.BossRoom ? st != null && st.DeadBosses.Contains(m.Ref)
                         : m.Kind == CompassMarkerKind.Vault && st != null && st.OpenVaults.Contains(m.Ref);
                MapIcons.CompassMark(m.Kind, dim, out string letter, out var colour, out var shape);
                float far = m.DistanceCells > CompassBar.NearCells ? 0.82f : 1f;
                float x = Edge(rel) * half;
                var mark = _marks.Next();
                mark.Set(new Vector2(x, Y(MarkY)), shape, colour, letter, (pinned ? 0.6f : 1f) * (far < 1f ? 0.85f : 1f), far);
                int idx = _marks.Used - 1;
                while (_dist.Count <= idx) { var t = UiStyle.Label(_clip, "Dist-" + _dist.Count, "", 13, UiStyle.TextDim, TextAnchor.MiddleLeft); t.rectTransform.sizeDelta = new Vector2(46f, 18f); t.horizontalOverflow = HorizontalWrapMode.Overflow; _dist.Add(t); }
                var d = _dist[idx];
                d.gameObject.SetActive(true);
                d.text = Mathf.RoundToInt(m.DistanceCells * metres) + "m";
                d.alignment = x > half - 40f ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
                d.rectTransform.anchoredPosition = new Vector2(x + (x > half - 40f ? -32f : 32f), Y(MarkY));
                d.color = new Color(UiStyle.TextDim.r, UiStyle.TextDim.g, UiStyle.TextDim.b, pinned ? 0.55f : 0.9f);
            }
            _marks.End();
            for (int i = _marks.Used; i < _dist.Count; i++) _dist[i].gameObject.SetActive(false);
        }

        /// <summary>-1..1 across the window for a relative bearing.</summary>
        private static float Edge(float rel) => Mathf.Clamp(rel / (CompassBar.WindowDegrees * 0.5f), -1f, 1f);

        /// <summary>Ticks and letters thin out toward the ends of the strip.</summary>
        private static float Fade(float rel) => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1f, Mathf.Abs(rel) / (CompassBar.WindowDegrees * 0.5f))) * 0.85f;
    }
}
