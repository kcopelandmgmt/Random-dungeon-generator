using System.Collections.Generic;

namespace DungeonGen.Core
{
    /// <summary>Walking distances (in cells) that matter to one team, measured from its spawn room.</summary>
    public readonly struct SpawnStats
    {
        public readonly int RoomId;
        /// <summary>Cells to the closest extract door.</summary>
        public readonly int ToNearestExtract;
        /// <summary>Cells to the vault door.</summary>
        public readonly int ToVault;
        /// <summary>Cells to the closest other team's spawn door.</summary>
        public readonly int ToNearestSpawn;

        public SpawnStats(int roomId, int toNearestExtract, int toVault, int toNearestSpawn)
        {
            RoomId = roomId;
            ToNearestExtract = toNearestExtract;
            ToVault = toVault;
            ToNearestSpawn = toNearestSpawn;
        }
    }

    /// <summary>
    /// How even the start is across teams. Each ratio is min/max across spawns, so 1.0 means every
    /// team has exactly the same walk and 0.5 means the luckiest team's walk is half the unluckiest's.
    /// </summary>
    public sealed class LayoutFairness
    {
        public static readonly LayoutFairness Empty = new LayoutFairness();

        public List<SpawnStats> Spawns { get; } = new List<SpawnStats>();
        public float ExtractRatio { get; set; }
        public float VaultRatio { get; set; }
        public float SpawnSpacingRatio { get; set; }

        /// <summary>Overall score in [0,1]; the weakest of the three ratios, so one unlucky team drags the whole layout down.</summary>
        public float Score { get; set; }
    }
}
