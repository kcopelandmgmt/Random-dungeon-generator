using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core.Stages
{
    /// <summary>
    /// Furnishes rooms with pillars and low walls so sightlines break, doorways can be held and boss
    /// arenas have shape. Deterministic from the seed and part of the public layout. Rules: spawns
    /// and extracts stay empty; a one-cell ring inside every wall stays clear so doors are never
    /// blocked and the room stays connected; the centre cell stays clear (shrines, the boss exit);
    /// boss rooms get symmetric pillars; the final boss gets a ring; everything else gets a few
    /// features by area. Any feature that would split the room is dropped.
    /// </summary>
    public static class InteriorPlacer
    {
        public static void Assign(DungeonLayout layout, DungeonSettings s, DungeonRandom rng)
        {
            if (!s.InteriorsEnabled) return;
            foreach (var room in layout.Rooms.OrderBy(r => r.Id))
            {
                var b = room.Bounds;
                if (room.Type == RoomType.Spawn || room.Type == RoomType.Extract || b.Width < 5 || b.Height < 5) continue;
                var inner = new GridRect(b.X + 1, b.Y + 1, b.Width - 2, b.Height - 2);
                var planned = new List<InteriorFeature>();
                switch (room.Type)
                {
                    case RoomType.Boss: Corners(inner, b.Width >= 11 && b.Height >= 11 ? 2 : 1, 1, planned); if (b.Width >= 9 && b.Height >= 9) Corners(inner, 1, 2, planned); break;   // the second set only lands where the first could not
                    case RoomType.FinalBoss: Ring(room.Center, Math.Min(b.Width, b.Height) / 3, b.Width >= 11 ? 8 : 4, planned); break;
                    case RoomType.Hub: Corners(inner, 1, 1, planned); break;
                    default:
                        int count = Math.Min(3, (inner.Width * inner.Height) / 30);
                        for (int i = 0; i < count; i++) planned.Add(RandomFeature(inner, rng));
                        break;
                }
                foreach (var f in planned)
                {
                    if (!Fits(layout, room, inner, f)) continue;
                    Paint(layout, f, f.Tile);
                    if (Connected(layout, room)) room.Interior.Add(f);
                    else Paint(layout, f, TileType.RoomFloor);   // it split the room: undo
                }
            }
        }

        private static void Corners(GridRect inner, int size, int inset, List<InteriorFeature> into)
        {
            into.Add(new InteriorFeature(InteriorKind.Pillar, new GridRect(inner.X + inset, inner.Y + inset, size, size)));
            into.Add(new InteriorFeature(InteriorKind.Pillar, new GridRect(inner.XMax - inset - size, inner.Y + inset, size, size)));
            into.Add(new InteriorFeature(InteriorKind.Pillar, new GridRect(inner.X + inset, inner.YMax - inset - size, size, size)));
            into.Add(new InteriorFeature(InteriorKind.Pillar, new GridRect(inner.XMax - inset - size, inner.YMax - inset - size, size, size)));
        }

        private static void Ring(GridPoint centre, int radius, int count, List<InteriorFeature> into)
        {
            for (int i = 0; i < count; i++)
            {
                double a = Math.PI * 2 * i / count;
                int x = centre.X + (int)Math.Round(Math.Cos(a) * radius), y = centre.Y + (int)Math.Round(Math.Sin(a) * radius);
                into.Add(new InteriorFeature(InteriorKind.Pillar, new GridRect(x, y, 1, 1)));
            }
        }

        private static InteriorFeature RandomFeature(GridRect inner, DungeonRandom rng)
        {
            int roll = rng.Range(0, 3);
            if (roll == 0) { int sz = rng.Chance(0.5f) ? 2 : 1; return new InteriorFeature(InteriorKind.Pillar, new GridRect(rng.Range(inner.X, inner.XMax - sz + 1), rng.Range(inner.Y, inner.YMax - sz + 1), sz, sz)); }
            bool horizontal = roll == 1;
            int len = rng.Range(2, 5);
            return horizontal
                ? new InteriorFeature(InteriorKind.LowWall, new GridRect(rng.Range(inner.X, Math.Max(inner.X + 1, inner.XMax - len + 1)), rng.Range(inner.Y, inner.YMax), Math.Min(len, inner.Width), 1))
                : new InteriorFeature(InteriorKind.LowWall, new GridRect(rng.Range(inner.X, inner.XMax), rng.Range(inner.Y, Math.Max(inner.Y + 1, inner.YMax - len + 1)), 1, Math.Min(len, inner.Height)));
        }

        /// <summary>Inside the clear ring, not on the centre, not overlapping or touching another feature, not within a cell of a door.</summary>
        private static bool Fits(DungeonLayout layout, Room room, GridRect inner, InteriorFeature f)
        {
            var b = f.Bounds;
            if (b.X < inner.X || b.Y < inner.Y || b.XMax > inner.XMax || b.YMax > inner.YMax) return false;
            var grown = b.Expand(1);
            if (grown.Contains(room.Center)) return false;
            foreach (var d in room.Doors) if (grown.Expand(1).Contains(d)) return false;
            for (int x = grown.X; x < grown.XMax; x++)
                for (int y = grown.Y; y < grown.YMax; y++)
                    if (layout.InBounds(new GridPoint(x, y)) && room.Bounds.Contains(new GridPoint(x, y)) && layout.Tiles[x, y] != TileType.RoomFloor) return false;
            return true;
        }

        private static void Paint(DungeonLayout layout, InteriorFeature f, TileType t)
        {
            for (int x = f.Bounds.X; x < f.Bounds.XMax; x++)
                for (int y = f.Bounds.Y; y < f.Bounds.YMax; y++)
                    layout.Tiles[x, y] = t;
        }

        /// <summary>Every floor cell of the room reachable from every door, walking only room floor.</summary>
        public static bool Connected(DungeonLayout layout, Room room)
        {
            var b = room.Bounds;
            GridPoint? start = null;
            int floor = 0;
            for (int x = b.X; x < b.XMax; x++)
                for (int y = b.Y; y < b.YMax; y++)
                    if (layout.Tiles[x, y] == TileType.RoomFloor) { floor++; if (start == null) start = new GridPoint(x, y); }
            if (start == null) return false;
            var seen = new HashSet<GridPoint> { start.Value };
            var stack = new Stack<GridPoint>(); stack.Push(start.Value);
            while (stack.Count > 0)
            {
                var p = stack.Pop();
                foreach (var d in GridPoint.Cardinals)
                {
                    var n = p + d;
                    if (!b.Contains(n) || layout.Tiles[n.X, n.Y] != TileType.RoomFloor || !seen.Add(n)) continue;
                    stack.Push(n);
                }
            }
            if (seen.Count != floor) return false;
            foreach (var d in room.Doors)
                if (!GridPoint.Cardinals.Any(c => seen.Contains(d + c))) return false;   // a door must open onto reachable floor
            return true;
        }
    }

    /// <summary>
    /// Places hazards by depth: the deeper the room, the likelier and nastier. Never in spawns,
    /// extracts, shrine rooms, boss rooms (the boss is the hazard) or corridors; never on the centre,
    /// a door's approach or an interior feature; a pit never splits the room. Public layout.
    /// </summary>
    public static class HazardPlacer
    {
        public static void Assign(DungeonLayout layout, DungeonSettings s, DungeonRandom rng)
        {
            if (!s.HazardsEnabled) return;
            int maxDepth = Math.Max(1, layout.Rooms.Max(r => r.DepthFromSpawn));
            int next = HazardRules.IdBase;
            foreach (var room in layout.Rooms.OrderBy(r => r.Id))
            {
                if (room.Type == RoomType.Spawn || room.Type == RoomType.Extract || room.IsBossRoom || room.HasShrine || room.Type == RoomType.Hub) continue;
                var b = room.Bounds;
                if (b.Width < 5 || b.Height < 5) continue;
                float depth = room.DepthFromSpawn / (float)maxDepth;
                float chance = (0.25f + 0.5f * depth) * s.HazardDensity;
                int wanted = (rng.Chance(chance) ? 1 : 0) + (depth > 0.6f && rng.Chance(chance * 0.5f) ? 1 : 0);
                var inner = new GridRect(b.X + 1, b.Y + 1, b.Width - 2, b.Height - 2);
                for (int i = 0; i < wanted; i++)
                {
                    var kind = (HazardKind)rng.Range(0, 4);
                    GridRect bounds;
                    switch (kind)
                    {
                        case HazardKind.GasVent: bounds = new GridRect(rng.Range(inner.X, inner.XMax - 1), rng.Range(inner.Y, inner.YMax - 1), 2, 2); break;
                        case HazardKind.Pit: bounds = rng.Chance(0.5f) ? new GridRect(rng.Range(inner.X, inner.XMax - 1), rng.Range(inner.Y, inner.YMax - 1), 2, 2) : new GridRect(rng.Range(inner.X, Math.Max(inner.X + 1, inner.XMax - 2)), rng.Range(inner.Y, inner.YMax), Math.Min(3, inner.Width), 1); break;
                        case HazardKind.Lava: bounds = rng.Chance(0.5f) ? new GridRect(rng.Range(inner.X, Math.Max(inner.X + 1, inner.XMax - 3)), rng.Range(inner.Y, inner.YMax), Math.Min(4, inner.Width), 1) : new GridRect(rng.Range(inner.X, inner.XMax), rng.Range(inner.Y, Math.Max(inner.Y + 1, inner.YMax - 3)), 1, Math.Min(4, inner.Height)); break;
                        default: bounds = new GridRect(rng.Range(inner.X, inner.XMax), rng.Range(inner.Y, inner.YMax), 1, 1); break;
                    }
                    if (!Fits(layout, room, inner, bounds)) continue;
                    var h = new Hazard(next, kind, bounds, room.Id);
                    if (kind == HazardKind.Pit)
                    {
                        Paint(layout, bounds, TileType.Pit);
                        if (!InteriorPlacer.Connected(layout, room)) { Paint(layout, bounds, TileType.RoomFloor); continue; }
                    }
                    room.Hazards.Add(h); layout.Hazards.Add(h); next++;
                }
            }
        }

        private static bool Fits(DungeonLayout layout, Room room, GridRect inner, GridRect b)
        {
            if (b.X < inner.X || b.Y < inner.Y || b.XMax > inner.XMax || b.YMax > inner.YMax) return false;
            var grown = b.Expand(1);
            if (grown.Contains(room.Center)) return false;
            foreach (var d in room.Doors) if (grown.Expand(1).Contains(d)) return false;
            foreach (var other in room.Hazards) if (Overlaps(grown, other.Bounds)) return false;
            for (int x = b.X; x < b.XMax; x++)
                for (int y = b.Y; y < b.YMax; y++)
                    if (layout.Tiles[x, y] != TileType.RoomFloor) return false;
            return true;
        }
        private static bool Overlaps(GridRect a, GridRect b) => a.X < b.XMax && b.X < a.XMax && a.Y < b.YMax && b.Y < a.YMax;
        private static void Paint(DungeonLayout layout, GridRect b, TileType t) { for (int x = b.X; x < b.XMax; x++) for (int y = b.Y; y < b.YMax; y++) layout.Tiles[x, y] = t; }
    }
}
