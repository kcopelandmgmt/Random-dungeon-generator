using System;
using System.Collections.Generic;

namespace DungeonGen.Core
{
    /// <summary>
    /// Tile-based visibility: what a character standing on a cell can see. Walls, locked doors and
    /// closed doors block; floors, corridors and open doors do not. Pure C# so the same rule runs on
    /// the server (interest filtering, "did they actually see that player") and on the client
    /// (the fog the camera draws). This is what makes a doorway scary from above.
    /// </summary>
    public static class LineOfSight
    {
        /// <summary>Does a cell stop sight? Locked doors do; ordinary doors do not (a door is an opening in the layout, the game may close it).</summary>
        public static bool Blocks(DungeonLayout layout, GridPoint p) => Blocks(layout, p, null);
        /// <summary>As above, with the doors players have shut this round (<see cref="Net.MatchState.ClosedDoors"/>). Pillars block; low walls and pits do not.</summary>
        public static bool Blocks(DungeonLayout layout, GridPoint p, ISet<GridPoint> closedDoors)
        {
            var t = layout.GetTile(p);
            return t == TileType.Wall || t == TileType.Void || t == TileType.LockedDoor || t == TileType.Pillar || (closedDoors != null && t == TileType.Door && closedDoors.Contains(p));
        }

        /// <summary>Is there an unobstructed line from <paramref name="from"/> to <paramref name="to"/>? Blocking cells are visible but not seen through.</summary>
        public static bool CanSee(DungeonLayout layout, GridPoint from, GridPoint to, int maxRange = int.MaxValue) => CanSee(layout, from, to, null, maxRange);
        public static bool CanSee(DungeonLayout layout, GridPoint from, GridPoint to, ISet<GridPoint> closedDoors, int maxRange = int.MaxValue)
        {
            int dx = Math.Abs(to.X - from.X), dy = Math.Abs(to.Y - from.Y);
            if (maxRange != int.MaxValue && (long)dx * dx + (long)dy * dy > (long)maxRange * maxRange) return false;
            int x = from.X, y = from.Y;
            int sx = from.X < to.X ? 1 : -1, sy = from.Y < to.Y ? 1 : -1;
            int err = dx - dy;
            while (true)
            {
                if (x == to.X && y == to.Y) return true;
                var here = new GridPoint(x, y);
                if (here != from && Blocks(layout, here, closedDoors)) return false;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x += sx; }
                if (e2 < dx) { err += dx; y += sy; }
            }
        }

        /// <summary>
        /// Every cell visible from <paramref name="origin"/> within <paramref name="range"/>, walls
        /// included (you see the wall, not past it). Simple ray casting to the perimeter of the
        /// range square; cheap enough for one call per player per tick at the ranges we use (8-14 cells).
        /// </summary>
        public static HashSet<GridPoint> VisibleCells(DungeonLayout layout, GridPoint origin, int range) => VisibleCells(layout, origin, range, null);
        public static HashSet<GridPoint> VisibleCells(DungeonLayout layout, GridPoint origin, int range, ISet<GridPoint> closedDoors)
        {
            var seen = new HashSet<GridPoint> { origin };
            int r = range;
            for (int i = -r; i <= r; i++)
            {
                CastRay(layout, origin, new GridPoint(origin.X + i, origin.Y - r), r, seen, closedDoors);
                CastRay(layout, origin, new GridPoint(origin.X + i, origin.Y + r), r, seen, closedDoors);
                CastRay(layout, origin, new GridPoint(origin.X - r, origin.Y + i), r, seen, closedDoors);
                CastRay(layout, origin, new GridPoint(origin.X + r, origin.Y + i), r, seen, closedDoors);
            }
            return seen;
        }

        private static void CastRay(DungeonLayout layout, GridPoint from, GridPoint to, int range, HashSet<GridPoint> seen, ISet<GridPoint> closedDoors)
        {
            int dx = Math.Abs(to.X - from.X), dy = Math.Abs(to.Y - from.Y);
            int x = from.X, y = from.Y;
            int sx = from.X < to.X ? 1 : -1, sy = from.Y < to.Y ? 1 : -1;
            int err = dx - dy;
            int r2 = range * range;
            while (true)
            {
                var here = new GridPoint(x, y);
                int ox = x - from.X, oy = y - from.Y;
                if (ox * ox + oy * oy > r2) return;
                if (layout.InBounds(here)) seen.Add(here);
                if (here != from && Blocks(layout, here, closedDoors)) return;
                if (x == to.X && y == to.Y) return;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x += sx; }
                if (e2 < dx) { err += dx; y += sy; }
            }
        }

        /// <summary>Rooms with at least one visible floor cell from here: the rooms a player can currently see into.</summary>
        public static HashSet<int> VisibleRooms(DungeonLayout layout, GridPoint origin, int range) => VisibleRooms(layout, origin, range, null);
        public static HashSet<int> VisibleRooms(DungeonLayout layout, GridPoint origin, int range, ISet<GridPoint> closedDoors)
        {
            var rooms = new HashSet<int>();
            foreach (var c in VisibleCells(layout, origin, range, closedDoors))
            {
                int id = layout.RoomIdAt[c.X, c.Y];
                if (id >= 0 && layout.IsWalkable(c) && !(closedDoors != null && closedDoors.Contains(c))) rooms.Add(id);   // a locked or closed door is seen, not seen through
            }
            return rooms;
        }
    }
}
