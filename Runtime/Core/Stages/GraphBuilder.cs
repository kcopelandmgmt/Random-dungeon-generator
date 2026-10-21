using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core.Stages
{
    /// <summary>
    /// Stage 2: decide which rooms connect to which.
    ///
    /// 1. Build a sparse candidate graph: each room links to its K nearest neighbours.
    /// 2. Run Kruskal's algorithm to get a minimum spanning tree — guaranteed connected, no loops.
    /// 3. Re-add a fraction of the discarded short candidate edges to create loops.
    ///
    /// Trees alone produce "hallway dungeons" with one way in and out of everything, which is
    /// bad for an extraction game: you want flanking routes and multiple ways to reach an extract.
    /// The loop fraction is the main knob for how tactical versus linear a dungeon feels.
    /// </summary>
    public static class GraphBuilder
    {
        public readonly struct Edge
        {
            public readonly int A;
            public readonly int B;
            public readonly int Distance;
            public readonly bool IsLoop;

            public Edge(int a, int b, int distance, bool isLoop = false)
            {
                A = Math.Min(a, b);
                B = Math.Max(a, b);
                Distance = distance;
                IsLoop = isLoop;
            }

            public Edge AsLoop() => new Edge(A, B, Distance, true);
            public long Key => ((long)A << 32) | (uint)B;
        }

        public static List<Edge> Build(List<Room> rooms, DungeonSettings s, DungeonRandom rng)
        {
            int n = rooms.Count;
            if (n < 2) return new List<Edge>();

            // --- 1. K-nearest-neighbour candidate edges (deduplicated) -------------
            var candidates = new Dictionary<long, Edge>();
            for (int i = 0; i < n; i++)
            {
                var ordered = Enumerable.Range(0, n)
                    .Where(j => j != i)
                    .OrderBy(j => rooms[i].Center.ManhattanDistance(rooms[j].Center))
                    .ThenBy(j => j)
                    .Take(s.NearestNeighbours);

                foreach (int j in ordered)
                {
                    var e = new Edge(i, j, rooms[i].Center.ManhattanDistance(rooms[j].Center));
                    candidates[e.Key] = e;
                }
            }

            var sorted = candidates.Values.OrderBy(e => e.Distance).ThenBy(e => e.Key).ToList();

            // --- 2. Kruskal MST ---------------------------------------------------
            var uf = new UnionFind(n);
            var tree = new List<Edge>();
            var rejected = new List<Edge>();

            // Ring mode pre-typed spawns, extracts and the vault. Force a radial spine
            // (each spawn -> its nearest extract -> vault) into the tree first, so every
            // team's route inward is direct and about the same length. Without this the
            // MST can send one team the long way round the arena.
            foreach (var e in SpineEdges(rooms))
            {
                candidates.Remove(e.Key);
                if (uf.Union(e.A, e.B)) tree.Add(e);
            }
            sorted = candidates.Values.OrderBy(e => e.Distance).ThenBy(e => e.Key).ToList();

            foreach (var e in sorted)
            {
                if (uf.Union(e.A, e.B)) tree.Add(e);
                else rejected.Add(e);
            }

            // K-NN graphs can be disconnected (two clusters that only look at themselves).
            // Stitch components together with the cheapest cross-component edge until one remains.
            while (uf.Components > 1)
            {
                Edge? best = null;
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                    {
                        if (uf.Find(i) == uf.Find(j)) continue;
                        int d = rooms[i].Center.ManhattanDistance(rooms[j].Center);
                        if (best == null || d < best.Value.Distance) best = new Edge(i, j, d);
                    }
                if (best == null) break;
                uf.Union(best.Value.A, best.Value.B);
                tree.Add(best.Value);
            }

            // --- 3. Add loops back -----------------------------------------------
            var result = new List<Edge>(tree);
            foreach (var e in rejected)
            {
                if (e.Distance > s.MaxLoopDistance) continue;
                if (rng.Chance(s.LoopChance)) result.Add(e.AsLoop());
            }

            return result;
        }

        private static IEnumerable<Edge> SpineEdges(List<Room> rooms)
        {
            var spawns = rooms.Where(r => r.Type == RoomType.Spawn).ToList();
            var extracts = rooms.Where(r => r.Type == RoomType.Extract).ToList();
            var vault = rooms.FirstOrDefault(r => r.Type == RoomType.Vault);
            if (spawns.Count == 0 || extracts.Count == 0) yield break;

            foreach (var sp in spawns)
            {
                var nearest = extracts.OrderBy(e => sp.Center.ManhattanDistance(e.Center)).ThenBy(e => e.Id).First();
                yield return new Edge(sp.Id, nearest.Id, sp.Center.ManhattanDistance(nearest.Center));
            }
            if (vault != null)
                foreach (var e in extracts)
                    yield return new Edge(e.Id, vault.Id, e.Center.ManhattanDistance(vault.Center));
        }

        private sealed class UnionFind
        {
            private readonly int[] _parent;
            public int Components { get; private set; }

            public UnionFind(int n)
            {
                _parent = new int[n];
                for (int i = 0; i < n; i++) _parent[i] = i;
                Components = n;
            }

            public int Find(int x)
            {
                while (_parent[x] != x)
                {
                    _parent[x] = _parent[_parent[x]];
                    x = _parent[x];
                }
                return x;
            }

            public bool Union(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra == rb) return false;
                _parent[ra] = rb;
                Components--;
                return true;
            }
        }
    }
}
