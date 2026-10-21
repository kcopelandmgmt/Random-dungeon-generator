using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core.Stages
{
    /// <summary>
    /// Stage 7: measure the round from each team's point of view. One BFS per spawn over the
    /// walkable tiles gives that team's walk to the nearest extract, to the vault, and to the
    /// closest rival spawn. The generator uses the resulting score to pick between candidate
    /// layouts; designers can read it in the Inspector to judge a seed at a glance.
    /// </summary>
    public static class FairnessMapper
    {
        public static void Assign(DungeonLayout layout)
        {
            var fairness = new LayoutFairness();
            layout.Fairness = fairness;

            var spawns = layout.SpawnRooms.ToList();
            var extractDoors = layout.ExtractRooms.SelectMany(r => r.Doors).ToList();
            var finalDoors = layout.RoomsOfType(RoomType.FinalBoss).SelectMany(r => r.Doors).ToList();
            var bossDoors = layout.BossRooms.SelectMany(r => r.Doors).ToList();
            if (spawns.Count == 0) return;

            var dist = new int[layout.Width, layout.Height];
            var queue = new Queue<GridPoint>();

            foreach (var spawn in spawns)
            {
                for (int x = 0; x < layout.Width; x++)
                    for (int y = 0; y < layout.Height; y++)
                        dist[x, y] = -1;

                queue.Clear();
                var b = spawn.Bounds;
                for (int x = b.X; x < b.XMax; x++)
                    for (int y = b.Y; y < b.YMax; y++)
                    {
                        dist[x, y] = 0;
                        queue.Enqueue(new GridPoint(x, y));
                    }

                while (queue.Count > 0)
                {
                    var p = queue.Dequeue();
                    foreach (var d in GridPoint.Cardinals)
                    {
                        var n = p + d;
                        if (!layout.InBounds(n) || dist[n.X, n.Y] != -1 || !layout.IsWalkable(n, ignoreLocks: true)) continue;
                        dist[n.X, n.Y] = dist[p.X, p.Y] + 1;
                        queue.Enqueue(n);
                    }
                }

                int Nearest(IEnumerable<GridPoint> targets)
                {
                    int best = int.MaxValue;
                    foreach (var t in targets)
                    {
                        int v = dist[t.X, t.Y];
                        if (v >= 0 && v < best) best = v;
                    }
                    return best == int.MaxValue ? -1 : best;
                }

                var rivalDoors = spawns.Where(o => o.Id != spawn.Id).SelectMany(o => o.Doors);
                fairness.Spawns.Add(new SpawnStats(spawn.Id, Nearest(extractDoors), Nearest(finalDoors), Nearest(rivalDoors), bossDoors.Count > 0 ? Nearest(bossDoors) : -1));
            }

            fairness.ExtractRatio = Ratio(fairness.Spawns.Select(s => s.ToNearestExtract));
            fairness.FinalBossRatio = finalDoors.Count > 0 ? Ratio(fairness.Spawns.Select(s => s.ToFinalBoss)) : 1f;
            fairness.SpawnSpacingRatio = spawns.Count > 1 ? Ratio(fairness.Spawns.Select(s => s.ToNearestSpawn)) : 1f;
            fairness.BossRatio = bossDoors.Count > 0 ? Ratio(fairness.Spawns.Select(s => s.ToNearestBoss)) : 1f;
            fairness.Score = Math.Min(Math.Min(fairness.ExtractRatio, fairness.FinalBossRatio), Math.Min(fairness.SpawnSpacingRatio, fairness.BossRatio));
        }

        private static float Ratio(IEnumerable<int> values)
        {
            int min = int.MaxValue, max = 0;
            foreach (int v in values)
            {
                if (v < 0) return 0f; // unreachable: worst possible
                if (v < min) min = v;
                if (v > max) max = v;
            }
            if (max == 0) return 1f;
            return (float)min / max;
        }
    }
}
