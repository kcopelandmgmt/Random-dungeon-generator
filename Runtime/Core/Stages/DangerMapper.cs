using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core.Stages
{
    /// <summary>
    /// Stage 6: measure real walking distance from the nearest spawn to every walkable cell
    /// (multi-source BFS over the rasterized tiles) and turn that into a per-room Danger value
    /// in [0,1] and a discrete LootTier.
    ///
    /// Walking distance beats hop count here: a room three short corridors away is much safer
    /// than one three *long* corridors away, and the loot table should know the difference.
    /// The per-cell distance field is kept on the layout so gameplay can also use it for enemy
    /// density, ambient audio, fog, or "you are far from safety" UI.
    /// </summary>
    public static class DangerMapper
    {
        public static void Assign(DungeonLayout layout, DungeonSettings s)
        {
            int w = layout.Width, h = layout.Height;
            var dist = layout.DistanceFromSpawn;
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    dist[x, y] = -1;

            var queue = new Queue<GridPoint>();
            foreach (var spawn in layout.SpawnRooms)
            {
                var b = spawn.Bounds;
                for (int x = b.X; x < b.XMax; x++)
                    for (int y = b.Y; y < b.YMax; y++)
                    {
                        dist[x, y] = 0;
                        queue.Enqueue(new GridPoint(x, y));
                    }
            }

            int maxDist = 0;
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                int d = dist[p.X, p.Y];
                foreach (var dir in GridPoint.Cardinals)
                {
                    var n = p + dir;
                    if (!layout.InBounds(n) || dist[n.X, n.Y] != -1 || !layout.IsWalkable(n, ignoreLocks: true)) continue;
                    dist[n.X, n.Y] = d + 1;
                    if (d + 1 > maxDist) maxDist = d + 1;
                    queue.Enqueue(n);
                }
            }
            if (maxDist == 0) maxDist = 1;

            // Per-room danger = normalized distance to the room's doors (its "front line"),
            // averaged so a room with one close door and one far door sits in between.
            float maxRoomDanger = 0f;
            foreach (var r in layout.Rooms)
            {
                float avg = r.Doors.Count > 0
                    ? (float)r.Doors.Where(d => dist[d.X, d.Y] >= 0).DefaultIfEmpty(r.Center).Average(d => dist[d.X, d.Y])
                    : dist[r.Center.X, r.Center.Y];
                r.Danger = Math.Max(0f, avg) / maxDist;
                if (r.Danger > maxRoomDanger) maxRoomDanger = r.Danger;
            }
            // Re-normalize so the deepest room is exactly 1.0 regardless of corridor geometry.
            if (maxRoomDanger > 0f)
                foreach (var r in layout.Rooms) r.Danger /= maxRoomDanger;

            foreach (var r in layout.Rooms)
            {
                switch (r.Type)
                {
                    case RoomType.Spawn: r.Danger = 0f; break;
                    case RoomType.FinalBoss: r.Danger = 1f; break;
                    case RoomType.Boss: r.Danger = Math.Max(0.6f, r.Danger); break;
                    case RoomType.Vault: r.Danger = Math.Max(0.7f, r.Danger); break;
                    case RoomType.Loot: r.Danger = Math.Min(1f, r.Danger + 0.15f); break;
                    case RoomType.Hub: r.Danger = Math.Min(1f, r.Danger + 0.10f); break;
                }

                // The final boss and the vaults own the top tier; bosses sit one below;
                // everything else is graded into the tiers beneath.
                int gradedTiers = s.LootTiers - 1;
                int tier = (int)Math.Floor(r.Danger * gradedTiers);
                tier = Math.Min(gradedTiers - 1, tier);
                if (r.Type == RoomType.Loot) tier = Math.Min(gradedTiers - 1, tier + 1);
                if (r.Type == RoomType.Boss) tier = Math.Max(tier, s.LootTiers - 2);
                if (r.Type == RoomType.Vault || r.Type == RoomType.FinalBoss) tier = s.LootTiers - 1;
                if (r.Type == RoomType.Spawn) tier = 0;
                r.LootTier = Math.Max(0, tier);
            }
        }
    }
}
