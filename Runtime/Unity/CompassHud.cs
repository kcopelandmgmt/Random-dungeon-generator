using System.Collections.Generic;
using DungeonGen.Core;
using DungeonGen.Core.Hud;
using DungeonGen.Core.Net;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// The compass strip at the top of the screen: cardinal letters and markers from
    /// <see cref="CompassBar"/>. Greybox drawing with OnGUI; swap the draw calls for UI Toolkit or
    /// UGUI without touching the data. Positions come from the builder's grid mapping; the camera's
    /// yaw is the strip's centre, which with the locked camera is always north.
    /// </summary>
    public sealed class CompassHud : MonoBehaviour
    {
        public DungeonMatchBootstrap Match;
        public DungeonBuilder Builder;
        public TopDownCamera Camera;
        public Transform LocalPlayer;
        public int LocalPlayerId = 1;
        [Tooltip("Other players' transforms by id, for teammate markers. Fill from the transport.")]
        public Dictionary<int, Transform> Players = new Dictionary<int, Transform>();
        [Tooltip("Live team pings. Fill from the transport; expired ones are ignored.")]
        public List<TeamPing> Pings = new List<TeamPing>();
        public float StripWidthPx = 640f, StripHeightPx = 28f, TopMarginPx = 8f;

        private readonly Dictionary<int, GridPoint> _cells = new Dictionary<int, GridPoint>();

        private void OnGUI()
        {
            if (Match == null || Match.State == null || Builder == null || LocalPlayer == null) return;
            float yaw = Camera != null ? Camera.YawDegrees : 0f;
            var me = Builder.WorldToGrid(LocalPlayer.position);
            _cells.Clear();
            foreach (var kv in Players) if (kv.Value != null) _cells[kv.Key] = Builder.WorldToGrid(kv.Value.position);

            float left = (Screen.width - StripWidthPx) / 2f;
            var strip = new Rect(left, TopMarginPx, StripWidthPx, StripHeightPx);
            GUI.Box(strip, GUIContent.none);
            // Cardinals.
            for (int i = 0; i < 8; i++)
            {
                var c = (Cardinal)i;
                float rel = Compass.Relative(Compass.BearingOf(c), yaw);
                if (!CompassBar.InWindow(rel)) continue;
                float x = left + CompassBar.Position01(rel) * StripWidthPx;
                var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = i % 2 == 0 ? FontStyle.Bold : FontStyle.Normal };
                GUI.Label(new Rect(x - 12f, TopMarginPx, 24f, StripHeightPx), Compass.Abbrev(c), style);
            }
            // Markers, below the letters.
            foreach (var m in CompassBar.Markers(Match.State, LocalPlayerId, me, _cells, Pings, Match.State.Time))
            {
                float rel = Compass.Relative(m.Bearing, yaw);
                if (!CompassBar.InWindow(rel)) continue;
                float x = left + CompassBar.Position01(rel) * StripWidthPx;
                string glyph = Glyph(m.Kind);
                var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = m.DistanceCells > CompassBar.NearCells ? 9 : 12 };
                GUI.Label(new Rect(x - 10f, TopMarginPx + StripHeightPx, 20f, 18f), glyph, style);
            }
        }

        private static string Glyph(CompassMarkerKind k)
        {
            switch (k)
            {
                case CompassMarkerKind.Teammate: return "●";
                case CompassMarkerKind.Ping: return "!";
                case CompassMarkerKind.Extract: return "⇧";
                case CompassMarkerKind.BossRoom: return "☠";
                case CompassMarkerKind.FinalBoss: return "♛";
                case CompassMarkerKind.BossExit: return "⇈";
                case CompassMarkerKind.Shrine: return "✚";
                case CompassMarkerKind.Vault: return "⚿";
                default: return "⌂";
            }
        }
    }
}
