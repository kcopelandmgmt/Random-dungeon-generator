using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core.Profile
{
    /// <summary>
    /// The long game. A season is a window with a leaderboard of value extracted and cosmetic
    /// rewards at thresholds. Nothing in a season is power: stash and silver carry over, only the
    /// leaderboard resets. Silver's steady sink is the crafting fee (CraftingRules.SilverFee).
    /// </summary>
    public sealed class Season
    {
        public int Number { get; }
        public DateTime StartUtc { get; }
        public DateTime EndUtc { get; }
        /// <summary>Account → total item value extracted this season.</summary>
        public Dictionary<string, long> ValueExtracted { get; } = new Dictionary<string, long>();
        public Dictionary<string, int> Extractions { get; } = new Dictionary<string, int>();
        public Dictionary<string, int> PlayerKills { get; } = new Dictionary<string, int>();

        /// <summary>Cosmetic unlock ids by value threshold. Cosmetics only, by decision.</summary>
        public static readonly IReadOnlyList<(long value, string unlock)> Rewards = new[]
        {
            (500L, "cosmetic.cloak.season"), (2500L, "cosmetic.weapon_glow.season"), (10000L, "cosmetic.title.season"), (50000L, "cosmetic.mount_banner.season"),
        };

        public Season(int number, DateTime startUtc, DateTime endUtc) { Number = number; StartUtc = startUtc; EndUtc = endUtc; }

        public bool IsActive(DateTime nowUtc) => nowUtc >= StartUtc && nowUtc < EndUtc;

        public void RecordExtraction(string account, long value)
        {
            ValueExtracted[account] = (ValueExtracted.TryGetValue(account, out long v) ? v : 0) + value;
            Extractions[account] = (Extractions.TryGetValue(account, out int n) ? n : 0) + 1;
        }

        public void RecordKill(string account) => PlayerKills[account] = (PlayerKills.TryGetValue(account, out int n) ? n : 0) + 1;

        public IEnumerable<(string account, long value)> Leaderboard(int top = 100) =>
            ValueExtracted.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Take(top).Select(kv => (kv.Key, kv.Value));

        /// <summary>Unlocks an account has earned this season. Grant them into PlayerProfile.Unlocks.</summary>
        public IEnumerable<string> EarnedUnlocks(string account)
        {
            long v = ValueExtracted.TryGetValue(account, out long x) ? x : 0;
            return Rewards.Where(r => v >= r.value).Select(r => r.unlock);
        }
    }
}
