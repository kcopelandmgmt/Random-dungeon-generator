using System.Collections.Generic;

namespace DungeonGen.Core
{
    public sealed class Room
    {
        public int Id { get; }
        public GridRect Bounds { get; }
        public RoomType Type { get; set; } = RoomType.Standard;

        /// <summary>Shortest hop count from the nearest Spawn room along the room graph. -1 until assigned.</summary>
        public int DepthFromSpawn { get; set; } = -1;

        /// <summary>0 = safe, 1 = deepest. Drives loot rarity weights and enemy density.</summary>
        public float Danger { get; set; }

        /// <summary>Loot tier 0..N; higher is rarer. Mapped from Danger in RoleAssigner.</summary>
        public int LootTier { get; set; }

        /// <summary>Ids of rooms reached directly by a corridor.</summary>
        public List<int> Neighbours { get; } = new List<int>();

        /// <summary>Door cells on this room's perimeter, one per corridor.</summary>
        public List<GridPoint> Doors { get; } = new List<GridPoint>();

        public GridPoint Center => Bounds.Center;

        public Room(int id, GridRect bounds)
        {
            Id = id;
            Bounds = bounds;
        }

        public override string ToString() => $"Room {Id} {Type} {Bounds} depth={DepthFromSpawn} tier={LootTier}";
    }
}
