using System.Collections.Generic;
using DungeonGen.Core.Combat;

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

        /// <summary>Boss tuning for Boss / FinalBoss rooms; null otherwise. Same object as <see cref="Boss"/>.Budget.</summary>
        public EncounterBudget Encounter { get; set; }

        /// <summary>Which boss, with which affixes. Null unless this is a Boss / FinalBoss room.</summary>
        public BossInstance Boss { get; set; }

        /// <summary>Vault rooms: id of the key that opens their locked doors. -1 for everything else.</summary>
        public int RequiredKeyId { get; set; } = -1;

        /// <summary>Id of the key that spawns in this room (chest or boss drop). -1 if none.</summary>
        public int HoldsKeyId { get; set; } = -1;

        /// <summary>Items rolled for this room's chests / boss drop. Empty for spawns and extracts.</summary>
        public List<Items.Item> Loot { get; } = new List<Items.Item>();

        /// <summary>A resurrection shrine stands here (hub rooms). Two player-kill souls bring a dead teammate back, naked.</summary>
        public bool HasShrine { get; set; }

        /// <summary>Silver lying in this room: the mob pack's drop plus the chest's purse. Weightless; picked up once; all of it drops on death.</summary>
        public int Silver { get; set; }

        /// <summary>Consumables found in this room's chests, beside the gear.</summary>
        public List<Items.Consumable> Consumables { get; } = new List<Items.Consumable>();

        /// <summary>Ordinary enemies stationed here. Null for spawns, extracts and boss rooms.</summary>
        public MobPack Mobs { get; set; }

        public bool IsBossRoom => Type == RoomType.Boss || Type == RoomType.FinalBoss;

        /// <summary>Ids of rooms reached directly by a corridor.</summary>
        public List<int> Neighbours { get; } = new List<int>();

        /// <summary>Door cells on this room's perimeter, one per corridor.</summary>
        public List<GridPoint> Doors { get; } = new List<GridPoint>();

        /// <summary>Pillars and low walls inside the room (<see cref="Stages.InteriorPlacer"/>). Painted into the tiles; kept here for the fingerprint and the builder.</summary>
        public List<InteriorFeature> Interior { get; } = new List<InteriorFeature>();
        /// <summary>Traps, vents, pits and lava (<see cref="Stages.HazardPlacer"/>).</summary>
        public List<Hazard> Hazards { get; } = new List<Hazard>();

        public GridPoint Center => Bounds.Center;

        public Room(int id, GridRect bounds)
        {
            Id = id;
            Bounds = bounds;
        }

        public override string ToString() => $"Room {Id} {Type} {Bounds} depth={DepthFromSpawn} tier={LootTier}";
    }
}
