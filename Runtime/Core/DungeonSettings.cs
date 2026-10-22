using System;
using DungeonGen.Core.Combat;

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
        /// <summary>One spawn room per team. 1-16 supported; use <see cref="ForTeams"/> for a canvas sized to match.</summary>
        public int SpawnCount = 3;
        /// <summary>Fringe = pick spawns after placing rooms; Ring = place spawn rooms around the edge first (recommended for 4+ teams).</summary>
        public SpawnPlacement SpawnPlacement = SpawnPlacement.Fringe;
        /// <summary>Ring mode: how far each spawn may wander from its ideal angle, as a fraction of the slice between spawns (0 = perfectly even, 1 = may touch its neighbour's slot).</summary>
        public float RingJitter = 0.35f;
        /// <summary>Ring mode: radius of the extract ring as a fraction of the spawn ring (0.3-0.7). Smaller = extracts closer to the center, longer and riskier extractions.</summary>
        public float ExtractRingRadius = 0.6f;
        /// <summary>Ring mode: radius of the boss-room ring as a fraction of the spawn ring. Sits between the extracts and the final boss.</summary>
        public float BossRingRadius = 0.32f;

        // --- Bosses, vaults and keys -------------------------------------------
        /// <summary>Boss arenas tuned for RoomBossPlayerEquivalents players. 0 disables.</summary>
        public int BossCount = 2;
        /// <summary>Locked treasure rooms. Each gets one key hidden elsewhere. Needs dead-end rooms; the generator warns if it cannot place them all.</summary>
        public int VaultCount = 1;
        /// <summary>Probability that a vault key is a boss drop rather than a floor chest.</summary>
        public float KeyFromBossChance = 0.5f;
        /// <summary>Minimum walking distance (cells) between a key and the vault it opens, so the key is always a trip.</summary>
        public int KeyMinWalk = 40;
        // --- Mobs ---------------------------------------------------------------
        public bool MobsEnabled = true;
        /// <summary>Scales every pack's budget. 1 = the defaults below.</summary>
        public float MobDensity = 1f;
        /// <summary>Pack difficulty in baseline player-equivalents for a room next to a spawn (danger 0) and for the deepest room (danger 1). Loot rooms and vaults add MobBudgetLootBonus.</summary>
        public float MobBudgetAtEdge = 0.5f;
        public float MobBudgetAtCenter = 2.0f;
        public float MobBudgetLootBonus = 0.5f;
        // --- Shrines ------------------------------------------------------------
        /// <summary>Resurrection shrines in hub rooms (at least one per dungeon).</summary>
        public bool ShrinesEnabled = true;

        // --- Loot ---------------------------------------------------------------
        public bool LootEnabled = true;
        /// <summary>Scales item counts per room. 1 = 1-2 per ordinary room, 3 in loot rooms, 4 in vaults and the final boss.</summary>
        public float LootDensity = 1f;
        /// <summary>Roll 0-2 affixes per room boss from the seed.</summary>
        public bool BossAffixesEnabled = true;
        public int MaxAffixesPerBoss = 2;
        /// <summary>Numbers every boss is tuned against.</summary>
        public CombatBaseline Combat = new CombatBaseline();
        public int ExtractCount = 2;
        /// <summary>Minimum graph hops between any two spawn rooms.</summary>
        public int MinSpawnSeparation = 3;
        /// <summary>Minimum graph hops from any spawn to an extract.</summary>
        public int MinExtractDepth = 2;
        public int LootTiers = 5;
        /// <summary>Fraction of non-special rooms promoted to Loot rooms (dead ends preferred).</summary>
        public float LootRoomFraction = 0.25f;

        // --- Fairness -----------------------------------------------------------
        /// <summary>Generate this many playable layouts (from seeds derived from the requested one) and keep the fairest. 1 = take the first. Still deterministic.</summary>
        public int CandidateCount = 1;

        public const int MaxTeams = 16;

        /// <summary>A preset scaled for a given number of teams: ring spawns, enough rooms for depth, and candidate selection for fairness.</summary>
        public static DungeonSettings ForTeams(int teams)
        {
            if (teams < 1 || teams > MaxTeams) throw new ArgumentOutOfRangeException(nameof(teams));
            int side = Math.Max(120, 60 + 12 * teams);
            return new DungeonSettings
            {
                Width = side,
                Height = side,
                TargetRoomCount = 10 + 3 * teams,
                SpawnCount = teams,
                // Up to 4 teams every team gets its own extract and boss; beyond that pairs share.
                ExtractCount = teams <= 4 ? Math.Max(2, teams) : Math.Min(4, (teams + 1) / 2),
                SpawnPlacement = SpawnPlacement.Ring,
                BossCount = teams <= 4 ? Math.Max(2, teams) : Math.Min(4, (teams + 1) / 2),
                VaultCount = Math.Max(1, teams / 4),
                MinSpawnSeparation = 3,
                MinExtractDepth = 2,
                NearestNeighbours = 4,
                LoopChance = 0.2f,
                CandidateCount = 4,
            };
        }

        /// <summary>A new player's first dungeon: one team, no rivals, small canvas, one boss, one vault, two extracts, every system present once.</summary>
        public static DungeonSettings ForTutorial()
        {
            return new DungeonSettings
            {
                Width = 96, Height = 96, TargetRoomCount = 14,
                SpawnCount = 1, ExtractCount = 2, BossCount = 1, VaultCount = 1,
                SpawnPlacement = SpawnPlacement.Ring, MinSpawnSeparation = 1, MinExtractDepth = 2,
                LoopChance = 0.15f, CandidateCount = 2, MobDensity = 0.7f,
            };
        }

        public void Validate()
        {
            if (SpawnCount < 1 || SpawnCount > MaxTeams) throw new ArgumentException($"SpawnCount must be 1..{MaxTeams}.");
            if (CandidateCount < 1) throw new ArgumentException("CandidateCount must be >= 1.");
            if (RingJitter < 0f || RingJitter > 1f) throw new ArgumentException("RingJitter must be 0..1.");
            if (ExtractRingRadius < 0.2f || ExtractRingRadius > 0.9f) throw new ArgumentException("ExtractRingRadius must be 0.2..0.9.");
            if (BossRingRadius < 0.15f || BossRingRadius >= ExtractRingRadius) throw new ArgumentException("BossRingRadius must be >= 0.15 and smaller than ExtractRingRadius.");
            if (BossCount < 0 || VaultCount < 0) throw new ArgumentException("BossCount and VaultCount must be >= 0.");
            if (KeyFromBossChance < 0f || KeyFromBossChance > 1f) throw new ArgumentException("KeyFromBossChance must be 0..1.");
            if (LootDensity < 0f || LootDensity > 3f) throw new ArgumentException("LootDensity must be 0..3.");
            if (MobDensity < 0f || MobDensity > 3f) throw new ArgumentException("MobDensity must be 0..3.");
            if (MaxAffixesPerBoss < 0 || MaxAffixesPerBoss > 3) throw new ArgumentException("MaxAffixesPerBoss must be 0..3.");
            if (TargetRoomCount < SpawnCount + ExtractCount + BossCount + VaultCount + 3)
                throw new ArgumentException("Need at least SpawnCount + ExtractCount + BossCount + VaultCount + 3 rooms.");
            Combat.Validate();
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
