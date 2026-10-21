using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core.Stages
{
    /// <summary>
    /// Stage 4: give every room a gameplay role based on the *graph* (hop distance), not raw
    /// coordinates. Two rooms can be physically adjacent yet far apart on foot; the graph is
    /// what the player actually experiences.
    ///
    /// - Spawns: far apart from each other, on the fringe, low connectivity (feel like gates).
    /// - Vault: the single deepest room from every spawn. Best loot, worst odds.
    /// - Extracts: deep rooms spread out from each other, so every squad has a run to make.
    /// - Hubs: high-degree crossroads where routes converge — the natural PvP flashpoints.
    /// - Loot rooms: dead ends off the beaten path; risk/reward side objectives.
    /// Danger and loot tiers are assigned afterwards by <see cref="DangerMapper"/> from walking distance.
    /// </summary>
    public static class RoleAssigner
    {
        public static void Assign(List<Room> rooms, DungeonSettings s, DungeonRandom rng, List<string> warnings)
        {
            int n = rooms.Count;
            if (n == 0) return;

            int[,] hops = AllPairsHops(rooms);

            // ---- Spawns: farthest-point sampling seeded by the most peripheral room ---------
            var spawns = new List<int>();
            int first = Enumerable.Range(0, n)
                .OrderByDescending(i => Eccentricity(hops, i, n))
                .ThenBy(i => rooms[i].Neighbours.Count)
                .ThenBy(i => rng.Range(0, 1000))
                .First();
            spawns.Add(first);

            while (spawns.Count < s.SpawnCount)
            {
                int pick = Enumerable.Range(0, n)
                    .Where(i => !spawns.Contains(i))
                    .OrderByDescending(i => spawns.Min(sp => hops[i, sp]))
                    .ThenBy(i => rooms[i].Neighbours.Count)
                    .ThenBy(i => rng.Range(0, 1000))
                    .First();
                int sep = spawns.Min(sp => hops[pick, sp]);
                if (sep < s.MinSpawnSeparation)
                    warnings.Add($"Spawn separation {sep} is below MinSpawnSeparation {s.MinSpawnSeparation} (not enough rooms / too dense).");
                spawns.Add(pick);
            }
            foreach (int id in spawns) rooms[id].Type = RoomType.Spawn;

            // ---- Depth from nearest spawn --------------------------------------------------
            int maxDepth = 0;
            foreach (var r in rooms)
            {
                r.DepthFromSpawn = spawns.Min(sp => hops[r.Id, sp]);
                if (r.DepthFromSpawn > maxDepth) maxDepth = r.DepthFromSpawn;
            }
            if (maxDepth == 0) maxDepth = 1;

            // ---- Vault: deepest room -------------------------------------------------------
            int vault = Enumerable.Range(0, n)
                .Where(i => rooms[i].Type == RoomType.Standard)
                .OrderByDescending(i => rooms[i].DepthFromSpawn)
                .ThenBy(i => rooms[i].Neighbours.Count)
                .ThenByDescending(i => rooms[i].Bounds.Area)
                .First();
            rooms[vault].Type = RoomType.Vault;

            // ---- Extracts: deep and spread out ---------------------------------------------
            var extracts = new List<int>();
            var extractPool = Enumerable.Range(0, n)
                .Where(i => rooms[i].Type == RoomType.Standard && rooms[i].DepthFromSpawn >= s.MinExtractDepth)
                .ToList();
            if (extractPool.Count < s.ExtractCount)
            {
                warnings.Add("Not enough deep rooms for extracts; relaxing MinExtractDepth.");
                extractPool = Enumerable.Range(0, n).Where(i => rooms[i].Type == RoomType.Standard).ToList();
            }
            while (extracts.Count < s.ExtractCount && extractPool.Count > 0)
            {
                int pick = extractPool
                    .OrderByDescending(i => extracts.Count == 0
                        ? rooms[i].DepthFromSpawn * 10 + spawns.Sum(sp => hops[i, sp])
                        : extracts.Min(e => hops[i, e]) * 10 + rooms[i].DepthFromSpawn)
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

        private static int Eccentricity(int[,] hops, int i, int n)
        {
            int max = 0;
            for (int j = 0; j < n; j++) if (hops[i, j] < int.MaxValue / 2) max = Math.Max(max, hops[i, j]);
            return max;
        }

        /// <summary>BFS from every room over the Neighbours lists. Unreachable pairs get a large sentinel.</summary>
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
    }
}
