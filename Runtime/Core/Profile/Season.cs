using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core.Profile
{
    public enum LeaderboardKind { ExtractedValue, PlayerKills, BossKills, FinalBossKills }

    /// <summary>
    /// The long game. A season is a window with leaderboards and cosmetic rewards at thresholds.
    /// The main board is value extracted; player kills, boss kills and final boss kills are side
    /// boards. Every board is kept per dungeon tier and in total. Nothing in a season is power:
    /// stash and silver carry over, only the boards reset. Silver's steady sink is the crafting fee.
    /// </summary>
    public sealed class Season
    {
        public int Number { get; }
        public DateTime StartUtc { get; }
        public DateTime EndUtc { get; }
        /// <summary>Account → total item value extracted this season, all tiers. The main board.</summary>
        public Dictionary<string, long> ValueExtracted { get; } = new Dictionary<string, long>();
        public Dictionary<string, int> Extractions { get; } = new Dictionary<string, int>();
        public Dictionary<string, int> PlayerKills { get; } = new Dictionary<string, int>();
        public Dictionary<string, int> BossKills { get; } = new Dictionary<string, int>();
        public Dictionary<string, int> FinalBossKills { get; } = new Dictionary<string, int>();
        private readonly Dictionary<(LeaderboardKind kind, int tier), Dictionary<string, long>> _perTier = new Dictionary<(LeaderboardKind, int), Dictionary<string, long>>();

        /// <summary>Cosmetic unlock ids by value threshold. Cosmetics only, by decision.</summary>
        public static readonly IReadOnlyList<(long value, string unlock)> Rewards = new[]
        {
            (500L, "cosmetic.cloak.season"), (2500L, "cosmetic.weapon_glow.season"), (10000L, "cosmetic.title.season"), (50000L, "cosmetic.mount_banner.season"),
        };

        public Season(int number, DateTime startUtc, DateTime endUtc) { Number = number; StartUtc = startUtc; EndUtc = endUtc; }

        public bool IsActive(DateTime nowUtc) => nowUtc >= StartUtc && nowUtc < EndUtc;

        private void Bump(LeaderboardKind kind, int tier, string account, long by)
        {
            if (!_perTier.TryGetValue((kind, tier), out var d)) _perTier[(kind, tier)] = d = new Dictionary<string, long>();
            d[account] = (d.TryGetValue(account, out long v) ? v : 0) + by;
        }
        private static void Bump(Dictionary<string, int> d, string account) => d[account] = (d.TryGetValue(account, out int n) ? n : 0) + 1;

        public void RecordExtraction(string account, long value, int tier = 1)
        {
            ValueExtracted[account] = (ValueExtracted.TryGetValue(account, out long v) ? v : 0) + value;
            Bump(Extractions, account);
            Bump(LeaderboardKind.ExtractedValue, tier, account, value);
        }
        public void RecordKill(string account, int tier = 1) { Bump(PlayerKills, account); Bump(LeaderboardKind.PlayerKills, tier, account, 1); }
        public void RecordBossKill(string account, bool finalBoss, int tier = 1)
        {
            Bump(finalBoss ? FinalBossKills : BossKills, account);
            Bump(finalBoss ? LeaderboardKind.FinalBossKills : LeaderboardKind.BossKills, tier, account, 1);
        }

        /// <summary>The main board, all tiers.</summary>
        public IEnumerable<(string account, long value)> Leaderboard(int top = 100) => Leaderboard(LeaderboardKind.ExtractedValue, top);

        /// <summary>Any board across all tiers.</summary>
        public IEnumerable<(string account, long value)> Leaderboard(LeaderboardKind kind, int top = 100)
        {
            Dictionary<string, long> src;
            switch (kind)
            {
                case LeaderboardKind.ExtractedValue: src = ValueExtracted; break;
                case LeaderboardKind.PlayerKills: src = PlayerKills.ToDictionary(kv => kv.Key, kv => (long)kv.Value); break;
                case LeaderboardKind.BossKills: src = BossKills.ToDictionary(kv => kv.Key, kv => (long)kv.Value); break;
                default: src = FinalBossKills.ToDictionary(kv => kv.Key, kv => (long)kv.Value); break;
            }
            return src.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Take(top).Select(kv => (kv.Key, kv.Value));
        }

        /// <summary>Any board for one dungeon tier.</summary>
        public IEnumerable<(string account, long value)> Leaderboard(LeaderboardKind kind, int tier, int top) =>
            _perTier.TryGetValue((kind, tier), out var d) ? d.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Take(top).Select(kv => (kv.Key, kv.Value)) : Enumerable.Empty<(string, long)>();

        /// <summary>Unlocks an account has earned this season. Grant them into PlayerProfile.Unlocks.</summary>
        public IEnumerable<string> EarnedUnlocks(string account)
        {
            long v = ValueExtracted.TryGetValue(account, out long x) ? x : 0;
            return Rewards.Where(r => v >= r.value).Select(r => r.unlock);
        }
    }
}
