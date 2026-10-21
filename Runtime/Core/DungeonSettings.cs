using System;

namespace DungeonGen.Core
{
    /// <summary>
    /// All knobs for one generation run. Plain class so it can be serialized,
    /// sent over the network, or wrapped in a Unity ScriptableObject.
    /// </summary>
    [Serializable]
    public class DungeonSettings
    {
        // --- Canvas -------------------------------------------------------------
        public int Width = 96;
        public int Height = 96;
        /// <summary>Cells kept empty around the outer edge so walls never touch the border.</summary>
        public int BorderPadding = 2;

        // --- Rooms --------------------------------------------------------------
        public int TargetRoomCount = 18;
        public int MinRoomSize = 5;
        public int MaxRoomSize = 13;
        /// <summary>Minimum empty cells between two rooms (room walls need at least 1).</summary>
        public int RoomSpacing = 3;
        /// <summary>How many random placements to try before giving up on reaching TargetRoomCount.</summary>
        public int MaxPlacementAttempts = 600;

        // --- Graph --------------------------------------------------------------
        /// <summary>Candidate edges per room before the MST prunes them.</summary>
        public int NearestNeighbours = 4;
        /// <summary>Fraction of pruned candidate edges re-added to create loops. 0 = pure tree, 1 = every candidate.</summary>
        public float LoopChance = 0.18f;
        /// <summary>Loops longer than this many cells (straight-line) are never added; keeps corridors from crossing the whole map.</summary>
        public int MaxLoopDistance = 40;

        // --- Corridors ----------------------------------------------------------
        /// <summary>Extra A* cost for digging through fresh rock vs. reusing an existing corridor. Higher = more merged, cave-like networks.</summary>
        public float NewCorridorCost = 4f;
        /// <summary>Set to 2 for wide 2-cell corridors.</summary>
        public int CorridorWidth = 1;

        // --- Roles --------------------------------------------------------------
        public int SpawnCount = 3;
        public int ExtractCount = 2;
        /// <summary>Minimum graph hops between any two spawn rooms.</summary>
        public int MinSpawnSeparation = 3;
        /// <summary>Minimum graph hops from any spawn to an extract.</summary>
        public int MinExtractDepth = 2;
        public int LootTiers = 5;
        /// <summary>Fraction of non-special rooms promoted to Loot rooms (dead ends preferred).</summary>
        public float LootRoomFraction = 0.25f;

        public void Validate()
        {
            if (Width < 24 || Height < 24) throw new ArgumentException("Canvas must be at least 24x24.");
            if (MinRoomSize < 3) throw new ArgumentException("MinRoomSize must be >= 3.");
            if (MaxRoomSize < MinRoomSize) throw new ArgumentException("MaxRoomSize must be >= MinRoomSize.");
            if (TargetRoomCount < SpawnCount + ExtractCount + 1)
                throw new ArgumentException("Need at least SpawnCount + ExtractCount + 1 rooms.");
            if (CorridorWidth < 1 || CorridorWidth > 2) throw new ArgumentException("CorridorWidth must be 1 or 2.");
            if (NearestNeighbours < 1) throw new ArgumentException("NearestNeighbours must be >= 1.");
            if (RoomSpacing < 3) throw new ArgumentException("RoomSpacing must be >= 3 so room walls never touch.");
            if (BorderPadding < 1) throw new ArgumentException("BorderPadding must be >= 1.");
            if (LootTiers < 2) throw new ArgumentException("LootTiers must be >= 2.");
        }
    }
}
