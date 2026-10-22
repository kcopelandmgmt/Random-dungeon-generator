using System;
using System.Collections.Generic;

namespace DungeonGen.Core
{
    /// <summary>
    /// Server-side pathfinding for mobs and bosses on the layout's own tiles. No NavMesh: the
    /// server already has the grid, A* on it is deterministic and cheap, and mobs leash to rooms
    /// so paths are short. Locked doors block mobs as they block players.
    /// </summary>
    public static class GridPath
    {
        /// <summary>Shortest 4-connected walkable path from <paramref name="from"/> to <paramref name="to"/>, inclusive, or null. Optionally confined to one room plus its leash margin.</summary>
        public static List<GridPoint> Find(DungeonLayout layout, GridPoint from, GridPoint to, int? leashRoom = null, int leashCells = 0, int maxCost = 4000)
        {
            if (!layout.IsWalkable(from) || !layout.IsWalkable(to)) return null;
            int w = layout.Width, h = layout.Height;
            var g = new int[w * h];
            var came = new int[w * h];
            var closed = new bool[w * h];
            for (int i = 0; i < g.Length; i++) { g[i] = int.MaxValue; came[i] = -1; }
            int Idx(GridPoint p) => p.Y * w + p.X;
            GridPoint At(int i) => new GridPoint(i % w, i / w);
            var open = new SortedSet<(int f, int idx)>();
            g[Idx(from)] = 0;
            open.Add((from.ManhattanDistance(to), Idx(from)));
            GridRect? leash = leashRoom.HasValue ? layout.Rooms[leashRoom.Value].Bounds.Expand(leashCells + 1) : (GridRect?)null;

            while (open.Count > 0)
            {
                var cur = open.Min; open.Remove(cur);
                if (closed[cur.idx]) continue;
                closed[cur.idx] = true;
                var p = At(cur.idx);
                if (p == to)
                {
                    var path = new List<GridPoint>();
                    for (int i = cur.idx; i != -1; i = came[i]) path.Add(At(i));
                    path.Reverse();
                    return path;
                }
                if (g[cur.idx] > maxCost) return null;
                foreach (var d in GridPoint.Cardinals)
                {
                    var n = p + d;
                    if (!layout.IsWalkable(n)) continue;
                    if (leash.HasValue && !leash.Value.Contains(n)) continue;
                    int ni = Idx(n);
                    if (closed[ni]) continue;
                    int ng = g[cur.idx] + 1;
                    if (ng < g[ni]) { g[ni] = ng; came[ni] = cur.idx; open.Add((ng + n.ManhattanDistance(to), ni)); }
                }
            }
            return null;
        }

        /// <summary>Is a mob at <paramref name="at"/> still within its leash of its room?</summary>
        public static bool WithinLeash(DungeonLayout layout, int roomId, GridPoint at, int leashCells) =>
            layout.Rooms[roomId].Bounds.Expand(leashCells + 1).Contains(at);
    }
}
