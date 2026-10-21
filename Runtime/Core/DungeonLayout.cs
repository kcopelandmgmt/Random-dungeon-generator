using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core
{
    /// <summary>
    /// The finished, engine-agnostic result of a generation run. Everything a renderer,
    /// navmesh baker, loot spawner or server needs lives here.
    /// </summary>
    public sealed class DungeonLayout
    {
        public int Seed { get; }
        public int Width { get; }
        public int Height { get; }
        public TileType[,] Tiles { get; }
        public List<Room> Rooms { get; } = new List<Room>();
        public List<Corridor> Corridors { get; } = new List<Corridor>();

        /// <summary>Per-team start fairness (walking distances from each spawn to the extracts and vault). Filled by FairnessMapper.</summary>
        public LayoutFairness Fairness { get; set; } = LayoutFairness.Empty;

        /// <summary>Non-fatal issues found during generation (crowded canvas, dropped edges, relaxed constraints).</summary>
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>Which room (by id) owns each cell, or -1 for corridors/void. Lets gameplay ask "which room am I in?" in O(1).</summary>
        public int[,] RoomIdAt { get; }

        /// <summary>Walking distance (cells) from the nearest spawn room, or -1 for non-walkable / unreachable cells. Filled by DangerMapper.</summary>
        public int[,] DistanceFromSpawn { get; }

        public DungeonLayout(int seed, int width, int height)
        {
            Seed = seed;
            Width = width;
            Height = height;
            Tiles = new TileType[width, height];
            RoomIdAt = new int[width, height];
            DistanceFromSpawn = new int[width, height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    RoomIdAt[x, y] = -1;
        }

        public bool InBounds(GridPoint p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;

        public TileType GetTile(GridPoint p) => InBounds(p) ? Tiles[p.X, p.Y] : TileType.Void;

        public bool IsWalkable(GridPoint p)
        {
            var t = GetTile(p);
            return t == TileType.RoomFloor || t == TileType.Corridor || t == TileType.Door;
        }

        public IEnumerable<Room> RoomsOfType(RoomType type) => Rooms.Where(r => r.Type == type);
        public IEnumerable<Room> SpawnRooms => RoomsOfType(RoomType.Spawn);
        public IEnumerable<Room> ExtractRooms => RoomsOfType(RoomType.Extract);

        public Room GetRoom(int id) => Rooms[id];
    }
}
