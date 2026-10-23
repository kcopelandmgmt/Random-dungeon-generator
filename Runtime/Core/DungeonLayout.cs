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
        /// <summary>The settings this layout was generated with (needed to regenerate a redacted client view).</summary>
        public DungeonSettings Settings { get; internal set; }
        public int Width { get; }
        public int Height { get; }
        public TileType[,] Tiles { get; }
        public List<Room> Rooms { get; } = new List<Room>();
        public List<Corridor> Corridors { get; } = new List<Corridor>();

        /// <summary>One entry per vault: where its key is and what it opens.</summary>
        public List<KeyPlacement> Keys { get; } = new List<KeyPlacement>();

        /// <summary>Every mob pack in the dungeon (also reachable via Room.Mobs).</summary>
        public List<Combat.MobPack> MobPacks { get; } = new List<Combat.MobPack>();

        /// <summary>Every item in the dungeon (also reachable via Room.Loot).</summary>
        public List<Items.Item> Items { get; } = new List<Items.Item>();

        /// <summary>Exit that opens when the final boss dies. Null if the layout has no final boss.</summary>
        public BossGatedExit BossExit { get; set; }
        /// <summary>The roaming elite's relic: a unique rolled with the loot secret. Server-only. Used only when the round's event is the elite.</summary>
        public Items.Item EventLoot { get; set; }
        /// <summary>Every hazard in the dungeon, in id order. Public layout.</summary>
        public List<Hazard> Hazards { get; } = new List<Hazard>();

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

        /// <summary>Can a player without keys stand here? Pass ignoreLocks=true to treat locked doors as open (distance / danger maths).</summary>
        public bool IsWalkable(GridPoint p, bool ignoreLocks = false)
        {
            var t = GetTile(p);
            return t == TileType.RoomFloor || t == TileType.Corridor || t == TileType.Door || (ignoreLocks && t == TileType.LockedDoor);
        }

        public IEnumerable<Room> RoomsOfType(RoomType type) => Rooms.Where(r => r.Type == type);
        public IEnumerable<Room> SpawnRooms => RoomsOfType(RoomType.Spawn);
        public IEnumerable<Room> ExtractRooms => RoomsOfType(RoomType.Extract);
        public IEnumerable<Room> BossRooms => RoomsOfType(RoomType.Boss);
        public IEnumerable<Room> VaultRooms => RoomsOfType(RoomType.Vault);
        public Room FinalBossRoom => Rooms.FirstOrDefault(r => r.Type == RoomType.FinalBoss);

        public Room GetRoom(int id) => Rooms[id];
    }
}
