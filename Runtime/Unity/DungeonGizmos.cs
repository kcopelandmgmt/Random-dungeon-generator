using DungeonGen.Core;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// Scene-view overlay: room boxes coloured by role, graph edges, and depth labels.
    /// Lets you judge a layout's pacing without placing a single prefab.
    /// </summary>
    [RequireComponent(typeof(DungeonBuilder))]
    public sealed class DungeonGizmos : MonoBehaviour
    {
        public bool DrawRooms = true;
        public bool DrawGraph = true;
        public bool DrawLabels = true;

        private void OnDrawGizmos()
        {
            var builder = GetComponent<DungeonBuilder>();
            var layout = builder?.Layout;
            if (layout == null) return;

            if (DrawGraph)
            {
                foreach (var c in layout.Corridors)
                {
                    Gizmos.color = c.IsLoop ? new Color(1f, 0.6f, 0.1f) : Color.white;
                    var a = builder.GridToWorld(layout.GetRoom(c.RoomA).Center);
                    var b = builder.GridToWorld(layout.GetRoom(c.RoomB).Center);
                    Gizmos.DrawLine(a, b);
                }
            }

            if (DrawRooms)
            {
                foreach (var room in layout.Rooms)
                {
                    Gizmos.color = ColorFor(room.Type);
                    var min = builder.GridToWorld(room.Bounds.Min);
                    var max = builder.GridToWorld(new GridPoint(room.Bounds.XMax, room.Bounds.YMax));
                    var center = (min + max) * 0.5f;
                    var size = max - min;
                    if (builder.UseXZPlane) size.y = 0.1f; else size.z = 0.1f;
                    Gizmos.DrawWireCube(center, size);

#if UNITY_EDITOR
                    if (DrawLabels)
                        UnityEditor.Handles.Label(center, $"{room.Type} d{room.DepthFromSpawn} t{room.LootTier}");
#endif
                }
            }
        }

        private static Color ColorFor(RoomType type)
        {
            switch (type)
            {
                case RoomType.Spawn: return Color.green;
                case RoomType.Extract: return Color.cyan;
                case RoomType.Vault: return Color.magenta;
                case RoomType.Loot: return Color.yellow;
                case RoomType.Hub: return new Color(1f, 0.5f, 0f);
                default: return Color.gray;
            }
        }
    }
}
