using System;
using System.Collections.Generic;

namespace DungeonGen.Core.Stages
{
    /// <summary>
    /// Stage 3: turn abstract graph edges into actual walkable corridors.
    ///
    /// For each edge we pick a door on each room's wall facing the other room, then run A*
    /// between the two doors. Room interiors and room walls are impassable, so corridors never
    /// slice through a room. Existing corridor cells are cheap to traverse while fresh rock is
    /// expensive, which makes later corridors merge into earlier ones and produces a network of
    /// shared tunnels instead of a spaghetti of parallel hallways.
    /// </summary>
    public static class CorridorCarver
    {
        private const byte Free = 0;
        private const byte Floor = 1;
        private const byte WallRing = 2;
        private const byte Tunnel = 3;
        private const byte DoorCell = 4;

        public static List<Corridor> Carve(
            List<Room> rooms,
            List<GraphBuilder.Edge> edges,
            DungeonSettings s,
            List<string> warnings)
        {
            var occ = new byte[s.Width, s.Height];
            foreach (var room in rooms)
            {
                var ring = room.Bounds.Expand(1);
                for (int x = ring.X; x < ring.XMax; x++)
                    for (int y = ring.Y; y < ring.YMax; y++)
                        if (InBounds(s, x, y))
                            occ[x, y] = room.Bounds.Contains(x, y) ? Floor : WallRing;
            }

            var corridors = new List<Corridor>(edges.Count);

            foreach (var edge in edges)
            {
                var a = rooms[edge.A];
                var b = rooms[edge.B];

                var doorA = PickDoor(a, b.Center, occ, s);
                var doorB = PickDoor(b, a.Center, occ, s);

                var path = AStar(doorA, doorB, occ, s, lenient: false)
                           ?? AStar(doorA, doorB, occ, s, lenient: true);

                if (path == null)
                {
                    warnings.Add($"Could not carve corridor between rooms {a.Id} and {b.Id}; edge dropped.");
                    continue;
                }

                var corridor = new Corridor(a.Id, b.Id) { IsLoop = edge.IsLoop };
                occ[doorA.X, doorA.Y] = DoorCell;
                occ[doorB.X, doorB.Y] = DoorCell;

                for (int i = 0; i < path.Count; i++)
                {
                    var c = path[i];
                    corridor.Cells.Add(c);
                    if (occ[c.X, c.Y] != DoorCell) occ[c.X, c.Y] = Tunnel;

                    // Widen perpendicular to travel. Width 2 adds one side, 3 adds both, 4 adds a second on
                    // the right. Only free rock or an existing tunnel is taken, so halls narrow to the single
                    // door cell where they meet a room wall: the door stays the chokepoint, the hall is the arena.
                    if (s.CorridorWidth > 1 && i < path.Count - 1)
                    {
                        var dir = path[i + 1] - c;
                        var right = new GridPoint(dir.Y, -dir.X);
                        foreach (int k in SideOffsets(s.CorridorWidth))
                        {
                            var side = c + new GridPoint(right.X * k, right.Y * k);
                            if (InBounds(s, side.X, side.Y) && (occ[side.X, side.Y] == Free || occ[side.X, side.Y] == Tunnel))
                            {
                                occ[side.X, side.Y] = Tunnel;
                                corridor.Cells.Add(side);
                            }
                        }
                    }
                }

                if (!a.Doors.Contains(doorA)) a.Doors.Add(doorA);
                if (!b.Doors.Contains(doorB)) b.Doors.Add(doorB);
                if (!a.Neighbours.Contains(b.Id)) a.Neighbours.Add(b.Id);
                if (!b.Neighbours.Contains(a.Id)) b.Neighbours.Add(a.Id);

                corridors.Add(corridor);
            }

            return corridors;
        }

        private static bool InBounds(DungeonSettings s, int x, int y) => x >= 0 && y >= 0 && x < s.Width && y < s.Height;

        /// <summary>Perpendicular cell offsets added beside the A* path for a corridor width: 2 → +1; 3 → -1, +1; 4 → -1, +1, +2.</summary>
        public static int[] SideOffsets(int width)
        {
            switch (width)
            {
                case 2: return new[] { 1 };
                case 3: return new[] { -1, 1 };
                case 4: return new[] { -1, 1, 2 };
                default: return System.Array.Empty<int>();
            }
        }

        /// <summary>
        /// Choose the wall-ring cell (never a corner) closest to the target. Prefer cells whose
        /// outward neighbour is open so the corridor can actually leave the door.
        /// </summary>
        private static GridPoint PickDoor(Room room, GridPoint target, byte[,] occ, DungeonSettings s)
        {
            var r = room.Bounds;
            GridPoint best = default;
            int bestScore = int.MaxValue;

            void Consider(GridPoint p, GridPoint outward)
            {
                if (!InBounds(s, p.X, p.Y) || !InBounds(s, outward.X, outward.Y)) return;
                byte o = occ[outward.X, outward.Y];
                int penalty = (o == Floor || o == WallRing) ? 1000 : 0; // blocked exit
                if (occ[p.X, p.Y] == DoorCell) penalty -= 2;           // reuse existing door
                int score = p.ManhattanDistance(target) + penalty;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = p;
                }
            }

            for (int x = r.X; x < r.XMax; x++)
            {
                Consider(new GridPoint(x, r.Y - 1), new GridPoint(x, r.Y - 2));
                Consider(new GridPoint(x, r.YMax), new GridPoint(x, r.YMax + 1));
            }
            for (int y = r.Y; y < r.YMax; y++)
            {
                Consider(new GridPoint(r.X - 1, y), new GridPoint(r.X - 2, y));
                Consider(new GridPoint(r.XMax, y), new GridPoint(r.XMax + 1, y));
            }
            return best;
        }

        private static List<GridPoint> AStar(GridPoint start, GridPoint goal, byte[,] occ, DungeonSettings s, bool lenient)
        {
            int w = s.Width, h = s.Height;
            var gScore = new float[w * h];
            var cameFrom = new int[w * h];
            var closed = new bool[w * h];
            for (int i = 0; i < gScore.Length; i++) { gScore[i] = float.PositiveInfinity; cameFrom[i] = -1; }

            int Idx(GridPoint p) => p.Y * w + p.X;
            GridPoint FromIdx(int i) => new GridPoint(i % w, i / w);

            var open = new MinHeap();
            gScore[Idx(start)] = 0;
            open.Push(Idx(start), start.ManhattanDistance(goal));

            while (open.Count > 0)
            {
                int currentIdx = open.Pop();
                if (closed[currentIdx]) continue;
                closed[currentIdx] = true;
                var current = FromIdx(currentIdx);

                if (current == goal)
                {
                    var path = new List<GridPoint>();
                    int i = currentIdx;
                    while (i != -1) { path.Add(FromIdx(i)); i = cameFrom[i]; }
                    path.Reverse();
                    return path;
                }

                foreach (var d in GridPoint.Cardinals)
                {
                    var next = current + d;
                    // Keep one cell of margin from the canvas edge so walls always fit.
                    if (next.X < 1 || next.Y < 1 || next.X >= w - 1 || next.Y >= h - 1) continue;
                    int nIdx = Idx(next);
                    if (closed[nIdx]) continue;

                    float stepCost = StepCost(occ[next.X, next.Y], next == goal, lenient);
                    if (float.IsPositiveInfinity(stepCost)) continue;

                    float tentative = gScore[currentIdx] + stepCost;
                    if (tentative < gScore[nIdx])
                    {
                        gScore[nIdx] = tentative;
                        cameFrom[nIdx] = currentIdx;
                        open.Push(nIdx, tentative + next.ManhattanDistance(goal));
                    }
                }
            }
            return null;

            float StepCost(byte o, bool isGoal, bool lenientMode)
            {
                if (isGoal) return 1f;
                switch (o)
                {
                    case Free: return s.NewCorridorCost;
                    case Tunnel: return 1f;
                    case DoorCell: return 1f;
                    case WallRing: return lenientMode ? 25f : float.PositiveInfinity;
                    default: return float.PositiveInfinity; // room floor
                }
            }
        }

        /// <summary>Tiny binary heap keyed on float priority; avoids allocating SortedSet nodes per step.</summary>
        private sealed class MinHeap
        {
            private readonly List<(int item, float priority)> _items = new List<(int, float)>();
            public int Count => _items.Count;

            public void Push(int item, float priority)
            {
                _items.Add((item, priority));
                int i = _items.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (_items[parent].priority <= _items[i].priority) break;
                    (_items[parent], _items[i]) = (_items[i], _items[parent]);
                    i = parent;
                }
            }

            public int Pop()
            {
                var top = _items[0].item;
                var last = _items[_items.Count - 1];
                _items.RemoveAt(_items.Count - 1);
                if (_items.Count > 0)
                {
                    _items[0] = last;
                    int i = 0;
                    while (true)
                    {
                        int l = i * 2 + 1, r = l + 1, smallest = i;
                        if (l < _items.Count && _items[l].priority < _items[smallest].priority) smallest = l;
                        if (r < _items.Count && _items[r].priority < _items[smallest].priority) smallest = r;
                        if (smallest == i) break;
                        (_items[smallest], _items[i]) = (_items[i], _items[smallest]);
                        i = smallest;
                    }
                }
                return top;
            }
        }
    }
}
