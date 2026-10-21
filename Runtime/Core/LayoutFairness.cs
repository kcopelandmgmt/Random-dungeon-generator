using System.Collections.Generic;

namespace DungeonGen.Core
{
    /// <summary>Walking distances (in cells) that matter to one team, measured from its spawn room.</summary>
    public readonly struct SpawnStats
    {
        public readonly int RoomId;
        /// <summary>Cells to the closest extract door.</summary>
        public readonly int ToNearestExtract;
        /// <summary>Cells to the final boss room's door.</summary>
        public readonly int ToFinalBoss;
        /// <summary>Cells to the closest other team's spawn door.</summary>
        public readonly int ToNearestSpawn;
        /// <summary>Cells to the closest boss room door (-1 if the layout has no boss rooms).</summary>
        public readonly int ToNearestBoss;

        public SpawnStats(int roomId, int toNearestExtract, int toFinalBoss, int toNearestSpawn, int toNearestBoss)
        {
            RoomId = roomId;
            ToNearestExtract = toNearestExtract;
            ToFinalBoss = toFinalBoss;
            ToNearestSpawn = toNearestSpawn;
            ToNearestBoss = toNearestBoss;
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
        public float FinalBossRatio { get; set; }
        public float SpawnSpacingRatio { get; set; }
        /// <summary>1 when the layout has no boss rooms.</summary>
        public float BossRatio { get; set; } = 1f;

        /// <summary>Overall score in [0,1]; the weakest of the ratios, so one unlucky team drags the whole layout down.</summary>
        public float Score { get; set; }
    }
}
