using System.Collections.Generic;
using DungeonGen.Core;
using DungeonGen.Core.Hud;
using DungeonGen.Core.Net;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// The minimap in the corner and the full map on hold-Tab, both drawn from <see cref="MiniMap"/>.
    /// Explored rooms only: the model never contains a room the team has not seen. Greybox drawing
    /// with OnGUI; the data is what matters.
    /// </summary>
    public sealed class MiniMapHud : MonoBehaviour
    {
        public DungeonMatchBootstrap Match;
        public DungeonBuilder Builder;
        public Transform LocalPlayer;
        public int LocalPlayerId = 1;
        public Dictionary<int, Transform> Players = new Dictionary<int, Transform>();
        public List<TeamPing> Pings = new List<TeamPing>();
        public KeyCode FullMapKey = KeyCode.Tab;
        public float CornerSizePx = 220f, MarginPx = 12f;
        public Color RoomColour = new Color(0.75f, 0.75f, 0.75f, 0.9f), HereColour = new Color(0.95f, 0.95f, 0.95f, 1f), CorridorColour = new Color(0.55f, 0.55f, 0.55f, 0.9f);
        public Color SelfColour = Color.white, TeammateColour = new Color(0.3f, 0.8f, 1f), PingColour = Color.yellow, LockedColour = new Color(1f, 0.6f, 0.1f), ExitColour = Color.green;
        public Color ClosedDoorColour = new Color(0.9f, 0.3f, 0.3f), HazardColour = new Color(1f, 0.2f, 0.6f), DisarmedColour = new Color(0.5f, 0.5f, 0.5f);

        private readonly Dictionary<int, GridPoint> _cells = new Dictionary<int, GridPoint>();
        private static Texture2D _white;

        private void OnGUI()
        {
            if (Match == null || Match.State == null || Builder == null || LocalPlayer == null) return;
            if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); }
            var me = Builder.WorldToGrid(LocalPlayer.position);
            _cells.Clear();
            foreach (var kv in Players) if (kv.Value != null) _cells[kv.Key] = Builder.WorldToGrid(kv.Value.position);
            var model = MiniMap.Build(Match.State, LocalPlayerId, me, _cells, Pings, Match.State.Time);
            bool full = Input.GetKey(FullMapKey);

            Rect panel; float scale; Vector2 origin;   // origin: grid cell → pixel
            if (full)
            {
                float size = Mathf.Min(Screen.width, Screen.height) * 0.8f;
                panel = new Rect((Screen.width - size) / 2f, (Screen.height - size) / 2f, size, size);
                scale = size / Mathf.Max(Match.State.Layout.Width, Match.State.Layout.Height);
                origin = new Vector2(panel.x, panel.yMax);
            }
            else
            {
                panel = new Rect(Screen.width - CornerSizePx - MarginPx, Screen.height - CornerSizePx - MarginPx, CornerSizePx, CornerSizePx);
                scale = CornerSizePx / (MiniMapModel.ViewRadiusCells * 2f + 1f);
                origin = new Vector2(panel.center.x - (me.X + 0.5f) * scale, panel.center.y + (me.Y + 0.5f) * scale);
            }
            GUI.Box(panel, GUIContent.none);
            GUI.BeginGroup(panel);
            origin -= panel.position;
            foreach (var r in model.Rooms)
            {
                if (!full && !model.Shows(r.Bounds)) continue;
                GUI.color = r.IsHere ? HereColour : RoomColour;
                GUI.DrawTexture(new Rect(origin.x + r.Bounds.X * scale, origin.y - r.Bounds.YMax * scale, r.Bounds.Width * scale, r.Bounds.Height * scale), _white);
                GUI.color = Color.black;
                string tag = r.Type == RoomType.Extract ? "E" : r.Type == RoomType.Boss ? (r.BossDead ? "b" : "B") : r.Type == RoomType.FinalBoss ? "F" : r.Type == RoomType.Vault ? (r.VaultOpen ? "v" : "V") : r.HasShrine ? "+" : r.IsTeamSpawn ? "S" : "";
                if (tag.Length > 0) GUI.Label(new Rect(origin.x + r.Bounds.Center.X * scale - 6f, origin.y - r.Bounds.Center.Y * scale - 8f, 14f, 16f), tag);
            }
            GUI.color = CorridorColour;
            foreach (var (a, b) in model.Corridors)
            {
                var ca = Match.State.Layout.Rooms[a].Center; var cb = Match.State.Layout.Rooms[b].Center;
                var pa = new Vector2(origin.x + (ca.X + 0.5f) * scale, origin.y - (ca.Y + 0.5f) * scale);
                var pb = new Vector2(origin.x + (cb.X + 0.5f) * scale, origin.y - (cb.Y + 0.5f) * scale);
                GUI.DrawTexture(new Rect(Mathf.Min(pa.x, pb.x), pa.y - 1f, Mathf.Abs(pb.x - pa.x) + 2f, 2f), _white);
                GUI.DrawTexture(new Rect(pb.x - 1f, Mathf.Min(pa.y, pb.y), 2f, Mathf.Abs(pb.y - pa.y) + 2f), _white);
            }
            foreach (var m in model.Markers)
            {
                GUI.color = m.Kind == MiniMapMarkerKind.Self ? SelfColour : m.Kind == MiniMapMarkerKind.Teammate ? TeammateColour : m.Kind == MiniMapMarkerKind.Ping ? PingColour : m.Kind == MiniMapMarkerKind.LockedDoor ? LockedColour
                          : m.Kind == MiniMapMarkerKind.ClosedDoor ? ClosedDoorColour : m.Kind == MiniMapMarkerKind.Hazard ? HazardColour : m.Kind == MiniMapMarkerKind.DisarmedHazard ? DisarmedColour : ExitColour;
                float s = m.Kind == MiniMapMarkerKind.Self || m.Kind == MiniMapMarkerKind.Teammate ? 6f : 5f;
                GUI.DrawTexture(new Rect(origin.x + (m.Cell.X + 0.5f) * scale - s / 2f, origin.y - (m.Cell.Y + 0.5f) * scale - s / 2f, s, s), _white);
            }
            GUI.color = Color.white;
            GUI.EndGroup();
        }
    }
}
