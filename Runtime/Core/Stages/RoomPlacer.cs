using System;
using System.Collections.Generic;

namespace DungeonGen.Core.Stages
{
    /// <summary>
    /// Stage 1: scatter non-overlapping rectangular rooms across the canvas using
    /// rejection sampling. Simple, fast, and gives organic-looking spacing. Rooms are
    /// placed with at least <see cref="DungeonSettings.RoomSpacing"/> empty cells between
    /// them so corridors always have somewhere to run.
    /// </summary>
    public static class RoomPlacer
    {
        public static List<Room> Place(DungeonSettings s, DungeonRandom rng, List<string> warnings)
        {
            var rooms = new List<Room>(s.TargetRoomCount);
            int pad = s.BorderPadding + 1; // +1 leaves space for the wall ring

            if (s.SpawnPlacement == SpawnPlacement.Ring)
                PlaceSpawnRing(rooms, s, rng, pad, warnings);
            int preplaced = rooms.Count;

            int attempts = 0;

            // Random rooms fill up to the target minus the vault satellites added afterwards.
            while (rooms.Count < s.TargetRoomCount - s.VaultCount && attempts < s.MaxPlacementAttempts)
            {
                attempts++;

                int w = rng.Range(s.MinRoomSize, s.MaxRoomSize + 1);
                int h = rng.Range(s.MinRoomSize, s.MaxRoomSize + 1);

                // Bias against extreme aspect ratios; long slivers make awkward rooms.
                if (w > h * 2 || h > w * 2) continue;

                int maxX = s.Width - pad - w;
                int maxY = s.Height - pad - h;
                if (maxX <= pad || maxY <= pad) continue;

                var candidate = new GridRect(rng.Range(pad, maxX + 1), rng.Range(pad, maxY + 1), w, h);

                bool blocked = false;
                for (int i = 0; i < rooms.Count; i++)
                {
                    if (rooms[i].Bounds.Overlaps(candidate, s.RoomSpacing))
                    {
                        blocked = true;
                        break;
                    }
                }
                if (blocked) continue;

                rooms.Add(new Room(rooms.Count, candidate));
            }

            PlaceVaultSatellites(rooms, s, rng, pad, preplaced, warnings);
            return rooms;
        }

        /// <summary>
        /// Vaults are locked treasure rooms and must be dead ends, otherwise a locked door would
        /// cut the dungeon in half for anyone without the key. Rather than hoping the random graph
        /// leaves dead ends, each vault is built as a small satellite room a short gap away from a
        /// random ordinary host room. GraphBuilder then links it to that host and nothing else.
        /// </summary>
        private static void PlaceVaultSatellites(List<Room> rooms, DungeonSettings s, DungeonRandom rng, int pad, int preplaced, List<string> warnings)
        {
            if (s.VaultCount == 0) return;
            var hosts = new List<Room>();
            for (int i = preplaced; i < rooms.Count; i++) hosts.Add(rooms[i]);      // random (untyped) rooms only
            if (hosts.Count == 0) { warnings.Add("No ordinary rooms to attach vaults to."); return; }

            int placed = 0;
            for (int v = 0; v < s.VaultCount; v++)
            {
                bool done = false;
                for (int attempt = 0; attempt < 40 && !done; attempt++)
                {
                    var host = rng.Pick(hosts);
                    int w = rng.Range(s.MinRoomSize, s.MinRoomSize + 3);
                    int h = rng.Range(s.MinRoomSize, s.MinRoomSize + 3);
                    int gap = rng.Range(s.RoomSpacing, s.RoomSpacing + 4);       // wall + short corridor + wall
                    int side = rng.Range(0, 4);
                    var hb = host.Bounds;
                    int x, y;
                    switch (side)
                    {
                        case 0: x = rng.Range(hb.X, hb.XMax - w + 1); y = hb.YMax + gap; break;       // above
                        case 1: x = rng.Range(hb.X, hb.XMax - w + 1); y = hb.Y - gap - h; break;      // below
                        case 2: x = hb.XMax + gap; y = rng.Range(hb.Y, hb.YMax - h + 1); break;       // right
                        default: x = hb.X - gap - w; y = rng.Range(hb.Y, hb.YMax - h + 1); break;     // left
                    }
                    if (x < pad || y < pad || x + w > s.Width - pad || y + h > s.Height - pad) continue;
                    var candidate = new GridRect(x, y, w, h);

                    bool blocked = false;
                    foreach (var r in rooms)
                        if (r.Bounds.Overlaps(candidate, s.RoomSpacing)) { blocked = true; break; }
                    if (blocked) continue;

                    rooms.Add(new Room(rooms.Count, candidate) { Type = RoomType.Vault });
                    hosts.Remove(host);                                           // one vault per host
                    placed++;
                    done = true;
                }
            }
            if (placed < s.VaultCount)
                warnings.Add($"Only {placed}/{s.VaultCount} vaults placed: no room for satellites. Enlarge the canvas.");
        }

        /// <summary>
        /// Ring mode lays the dungeon out like an arena before any random room exists:
        ///   outer ring  – one compact spawn room per team at evenly spaced angles,
        ///   inner ring  – the extract rooms, rotated half a slice so each sits *between* spawns,
        ///   center      – the vault.
        /// A random phase and a little jitter keep the compass points different per seed, while
        /// the geometry guarantees every team starts the same distance from the center and has
        /// an extract roughly the same distance away. Random rooms then fill the gaps.
        /// </summary>
        private static void PlaceSpawnRing(List<Room> rooms, DungeonSettings s, DungeonRandom rng, int pad, List<string> warnings)
        {
            int n = s.SpawnCount;
            double phase = rng.NextFloat() * Math.PI * 2;
            double spawnSlice = Math.PI * 2 / n;
            int smallMax = Math.Min(s.MaxRoomSize, s.MinRoomSize + 3);

            // Center: the final boss arena (large).
            {
                int w = rng.Range(Math.Max(s.MinRoomSize + 2, s.MaxRoomSize - 2), s.MaxRoomSize + 1);
                int h = rng.Range(Math.Max(s.MinRoomSize + 2, s.MaxRoomSize - 2), s.MaxRoomSize + 1);
                var arena = new GridRect(s.Width / 2 - w / 2, s.Height / 2 - h / 2, w, h);
                rooms.Add(new Room(rooms.Count, arena) { Type = RoomType.FinalBoss });
            }

            // Outer ring: spawns.
            for (int i = 0; i < n; i++)
            {
                double angle = phase + spawnSlice * i + (rng.NextFloat() - 0.5) * spawnSlice * s.RingJitter;
                if (!TryPlaceOnRing(rooms, s, rng, pad, angle, 1.0, s.MinRoomSize, smallMax, RoomType.Spawn))
                    warnings.Add($"Ring placement could not fit spawn room {i + 1}/{n}; canvas too small for this many teams. Falling back to fringe selection for it.");
            }

            // Inner ring: extracts, offset half a spawn slice so they sit between teams.
            int e = s.ExtractCount;
            double extractSlice = Math.PI * 2 / e;
            for (int j = 0; j < e; j++)
            {
                double angle = phase + spawnSlice * 0.5 + extractSlice * j + (rng.NextFloat() - 0.5) * extractSlice * s.RingJitter * 0.5;
                if (!TryPlaceOnRing(rooms, s, rng, pad, angle, s.ExtractRingRadius, s.MinRoomSize, smallMax, RoomType.Extract))
                    warnings.Add($"Ring placement could not fit extract room {j + 1}/{e}; falling back to graph selection for it.");
            }

            // Boss ring: between the extracts and the center, on the extract angles so each
            // pair of teams shares an extract *and* a boss. Bosses need arena-sized rooms.
            int bCount = s.BossCount;
            if (bCount > 0)
            {
                double bossSlice = Math.PI * 2 / bCount;
                int bossMin = Math.Min(s.MaxRoomSize, s.MinRoomSize + 2);
                int bossMax = Math.Max(bossMin, s.MaxRoomSize - 2);
                for (int j = 0; j < bCount; j++)
                {
                    double angle = phase + spawnSlice * 0.5 + bossSlice * j + (rng.NextFloat() - 0.5) * bossSlice * s.RingJitter * 0.5;
                    if (!TryPlaceOnRing(rooms, s, rng, pad, angle, s.BossRingRadius, bossMin, bossMax, RoomType.Boss))
                        warnings.Add($"Ring placement could not fit boss room {j + 1}/{bCount}; falling back to graph selection for it.");
                }
            }
        }

        private static bool TryPlaceOnRing(List<Room> rooms, DungeonSettings s, DungeonRandom rng, int pad,
            double angle, double radiusFraction, int minSize, int maxSize, RoomType type)
        {
            double cx = s.Width / 2.0, cy = s.Height / 2.0;
            int w = rng.Range(minSize, maxSize + 1);
            int h = rng.Range(minSize, maxSize + 1);
            double rx = (cx - pad - w / 2.0) * radiusFraction;
            double ry = (cy - pad - h / 2.0) * radiusFraction;

            // If the slot is taken, slide outward and inward alternately, two cells at a time.
            for (int step = 0; step < 12; step++)
            {
                double offset = (step % 2 == 1 ? -1 : 1) * ((step + 1) / 2) * 2.0;
                int x = (int)Math.Round(cx + (rx + offset) * Math.Cos(angle) - w / 2.0);
                int y = (int)Math.Round(cy + (ry + offset) * Math.Sin(angle) - h / 2.0);
                x = Math.Max(pad, Math.Min(s.Width - pad - w, x));
                y = Math.Max(pad, Math.Min(s.Height - pad - h, y));
                var candidate = new GridRect(x, y, w, h);

                bool blocked = false;
                foreach (var r in rooms)
                    if (r.Bounds.Overlaps(candidate, s.RoomSpacing)) { blocked = true; break; }
                if (blocked) continue;

                rooms.Add(new Room(rooms.Count, candidate) { Type = type });
                return true;
            }
            return false;
        }
    }
}
