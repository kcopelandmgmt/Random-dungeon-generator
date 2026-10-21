using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core.Stages
{
    /// <summary>
    /// Stage 4: give every room a gameplay role.
    ///
    /// Decisions use *walking distance along corridors* (corridor length plus room crossings),
    /// never raw coordinates and never bare hop counts: two rooms can be one hop apart through a
    /// 90-cell tunnel or a 6-cell one, and the player feels the difference.
    ///
    /// - Spawns: pre-placed on the ring (Ring mode) or farthest-point sampled from the fringe.
    /// - Vault: the room whose *nearest* spawn is farthest away, with an even walk for all teams.
    /// - Extracts: chosen greedily so every team's walk to its nearest extract is as even as possible.
    /// - Hubs: high-degree crossroads where routes converge: the natural fight locations.
    /// - Loot rooms: dead ends off the beaten path: risk/reward side objectives.
    /// Danger and loot tiers are assigned afterwards by <see cref="DangerMapper"/> from the tile grid.
    /// </summary>
    public static class RoleAssigner
    {
        private const float Unreachable = 1e9f;

        public static void Assign(List<Room> rooms, List<Corridor> corridors, DungeonSettings s, DungeonRandom rng, List<string> warnings)
        {
            int n = rooms.Count;
            if (n == 0) return;

            int[,] hops = AllPairsHops(rooms);
            float[,] walk = AllPairsWalk(rooms, corridors);

            // ---- Spawns ---------------------------------------------------------------------
            // Ring mode pre-typed spawn rooms in RoomPlacer; keep those. Otherwise (or if the ring
            // came up short) use farthest-point sampling seeded by the most peripheral room.
            var spawns = rooms.Where(r => r.Type == RoomType.Spawn).Select(r => r.Id).ToList();
            if (spawns.Count == 0)
            {
                int first = Enumerable.Range(0, n)
                    .OrderByDescending(i => Eccentricity(walk, i, n))
                    .ThenBy(i => rooms[i].Neighbours.Count)
                    .ThenBy(i => rng.Range(0, 1000))
                    .First();
                spawns.Add(first);
            }
            while (spawns.Count < s.SpawnCount)
            {
                int pick = Enumerable.Range(0, n)
                    .Where(i => !spawns.Contains(i))
                    .OrderByDescending(i => spawns.Min(sp => walk[i, sp]))
                    .ThenBy(i => rooms[i].Neighbours.Count)
                    .ThenBy(i => rng.Range(0, 1000))
                    .First();
                int sep = spawns.Min(sp => hops[pick, sp]);
                if (sep < s.MinSpawnSeparation)
                    warnings.Add($"Spawn separation {sep} is below MinSpawnSeparation {s.MinSpawnSeparation} (not enough rooms / too dense).");
                spawns.Add(pick);
            }
            foreach (int id in spawns) rooms[id].Type = RoomType.Spawn;

            // ---- Depth (hops) from nearest spawn, kept for display and MinExtractDepth ------
            foreach (var r in rooms) r.DepthFromSpawn = spawns.Min(sp => hops[r.Id, sp]);

            // ---- Vault: far from everyone, and about equally far ----------------------------
            // Score = nearest-spawn distance scaled by how even the walk is across teams, so a
            // room 300 cells from one team and 70 from another loses to one that is ~150 from all.
            if (!rooms.Any(r => r.Type == RoomType.Vault))
            {
                int vault = Enumerable.Range(0, n)
                    .Where(i => rooms[i].Type == RoomType.Standard)
                    .OrderByDescending(i => FairDepth(walk, i, spawns))
                    .ThenBy(i => rooms[i].Neighbours.Count)
                    .ThenByDescending(i => rooms[i].Bounds.Area)
                    .First();
                rooms[vault].Type = RoomType.Vault;
            }

            // ---- Extracts: greedy, maximising the evenness of each team's nearest-extract walk --
            var extracts = rooms.Where(r => r.Type == RoomType.Extract).Select(r => r.Id).ToList(); // pre-placed by Ring mode
            var extractPool = Enumerable.Range(0, n)
                .Where(i => rooms[i].Type == RoomType.Standard && rooms[i].DepthFromSpawn >= s.MinExtractDepth)
                .ToList();
            if (extractPool.Count < s.ExtractCount - extracts.Count)
            {
                warnings.Add("Not enough deep rooms for extracts; relaxing MinExtractDepth.");
                extractPool = Enumerable.Range(0, n).Where(i => rooms[i].Type == RoomType.Standard).ToList();
            }

            while (extracts.Count < s.ExtractCount && extractPool.Count > 0)
            {
                int pick = extractPool
                    .OrderByDescending(i => ExtractEvenness(walk, extracts, i, spawns))
                    .ThenByDescending(i => spawns.Min(sp => NearestExtractWalk(walk, extracts, i, sp)))
                    .ThenBy(i => rng.Range(0, 1000))
                    .First();
                extracts.Add(pick);
                extractPool.Remove(pick);
                rooms[pick].Type = RoomType.Extract;
            }

            // ---- Hubs: top-connectivity crossroads -----------------------------------------
            foreach (var r in rooms.Where(r => r.Type == RoomType.Standard && r.Neighbours.Count >= 3)
                                   .OrderByDescending(r => r.Neighbours.Count)
                                   .Take(Math.Max(1, n / 8)))
                r.Type = RoomType.Hub;

            // ---- Loot rooms: dead ends first, then random fill -----------------------------
            var standard = rooms.Where(r => r.Type == RoomType.Standard).ToList();
            int lootTarget = (int)Math.Round(standard.Count * s.LootRoomFraction);
            var lootOrder = standard
                .OrderBy(r => r.Neighbours.Count)             // dead ends (degree 1) first
                .ThenByDescending(r => r.DepthFromSpawn)      // deeper preferred
                .ThenBy(r => rng.Range(0, 1000))
                .Take(lootTarget);
            foreach (var r in lootOrder) r.Type = RoomType.Loot;
        }

        /// <summary>min distance × (min / max): rewards rooms that are far from all spawns *and* evenly so.</summary>
        private static float FairDepth(float[,] walk, int room, List<int> spawns)
        {
            float min = float.MaxValue, max = 0f;
            foreach (int sp in spawns)
            {
                float d = walk[room, sp];
                if (d < min) min = d;
                if (d > max) max = d;
            }
            if (max <= 0f || min >= Unreachable) return 0f;
            return min * (min / max);
        }

        private static float NearestExtractWalk(float[,] walk, List<int> chosen, int candidate, int spawn)
        {
            float best = walk[candidate, spawn];
            foreach (int e in chosen) best = Math.Min(best, walk[e, spawn]);
            return best;
        }

        /// <summary>min/max across teams of the walk to the nearest extract if <paramref name="candidate"/> were added.</summary>
        private static float ExtractEvenness(float[,] walk, List<int> chosen, int candidate, List<int> spawns)
        {
            float min = float.MaxValue, max = 0f;
            foreach (int sp in spawns)
            {
                float d = NearestExtractWalk(walk, chosen, candidate, sp);
                if (d < min) min = d;
                if (d > max) max = d;
            }
            if (max <= 0f || min >= Unreachable) return 0f;
            return min / max;
        }

        private static float Eccentricity(float[,] walk, int i, int n)
        {
            float max = 0f;
            for (int j = 0; j < n; j++) if (walk[i, j] < Unreachable) max = Math.Max(max, walk[i, j]);
            return max;
        }

        /// <summary>BFS hop counts over the Neighbours lists. Unreachable pairs get a large sentinel.</summary>
        private static int[,] AllPairsHops(List<Room> rooms)
        {
            int n = rooms.Count;
            var hops = new int[n, n];
            var queue = new Queue<int>();
            for (int src = 0; src < n; src++)
            {
                for (int j = 0; j < n; j++) hops[src, j] = int.MaxValue / 4;
                hops[src, src] = 0;
                queue.Clear();
                queue.Enqueue(src);
                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    foreach (int nb in rooms[cur].Neighbours)
                    {
                        if (hops[src, nb] != int.MaxValue / 4) continue;
                        hops[src, nb] = hops[src, cur] + 1;
                        queue.Enqueue(nb);
                    }
                }
            }
            return hops;
        }

        /// <summary>
        /// Floyd–Warshall over corridor lengths. Edge weight = corridor cells + half of each room's
        /// span (the walk across the room to reach the next door). Fine for the ≤ 60 rooms we use.
        /// </summary>
        private static float[,] AllPairsWalk(List<Room> rooms, List<Corridor> corridors)
        {
            int n = rooms.Count;
            var d = new float[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    d[i, j] = i == j ? 0f : Unreachable;

            foreach (var c in corridors)
            {
                float w = c.Length
                        + (rooms[c.RoomA].Bounds.Width + rooms[c.RoomA].Bounds.Height) * 0.25f
                        + (rooms[c.RoomB].Bounds.Width + rooms[c.RoomB].Bounds.Height) * 0.25f;
                if (w < d[c.RoomA, c.RoomB]) { d[c.RoomA, c.RoomB] = w; d[c.RoomB, c.RoomA] = w; }
            }

            for (int k = 0; k < n; k++)
                for (int i = 0; i < n; i++)
                {
                    if (d[i, k] >= Unreachable) continue;
                    for (int j = 0; j < n; j++)
                    {
                        float via = d[i, k] + d[k, j];
                        if (via < d[i, j]) d[i, j] = via;
                    }
                }
            return d;
        }
    }
}
