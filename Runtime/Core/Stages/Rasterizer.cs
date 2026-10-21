using System.Collections.Generic;

namespace DungeonGen.Core.Stages
{
    /// <summary>
    /// Stage 5: paint rooms, corridors and doors into the tile grid, then wrap every walkable
    /// cell in walls. The output is what a Tilemap, mesh builder or prefab spawner consumes.
    /// </summary>
    public static class Rasterizer
    {
        public static void Paint(DungeonLayout layout, List<Room> rooms, List<Corridor> corridors)
        {
            foreach (var room in rooms)
            {
                var b = room.Bounds;
                for (int x = b.X; x < b.XMax; x++)
                    for (int y = b.Y; y < b.YMax; y++)
                    {
                        layout.Tiles[x, y] = TileType.RoomFloor;
                        layout.RoomIdAt[x, y] = room.Id;
                    }
            }

            foreach (var corridor in corridors)
                foreach (var c in corridor.Cells)
                    if (layout.InBounds(c) && layout.Tiles[c.X, c.Y] == TileType.Void)
                        layout.Tiles[c.X, c.Y] = TileType.Corridor;

            foreach (var room in rooms)
                foreach (var d in room.Doors)
                    if (layout.InBounds(d))
                    {
                        layout.Tiles[d.X, d.Y] = room.Type == RoomType.Vault ? TileType.LockedDoor : TileType.Door;
                        layout.RoomIdAt[d.X, d.Y] = room.Id;
                    }

            // Walls: any Void cell touching a walkable cell (8-neighbourhood so corners close).
            for (int x = 0; x < layout.Width; x++)
                for (int y = 0; y < layout.Height; y++)
                {
                    if (layout.Tiles[x, y] != TileType.Void) continue;
                    if (TouchesWalkable(layout, x, y)) layout.Tiles[x, y] = TileType.Wall;
                }
        }

        private static bool TouchesWalkable(DungeonLayout layout, int x, int y)
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    if (layout.IsWalkable(new GridPoint(x + dx, y + dy), ignoreLocks: true)) return true;
                }
            return false;
        }
    }
}
