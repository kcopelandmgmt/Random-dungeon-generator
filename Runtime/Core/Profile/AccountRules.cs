using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core.Profile
{
    /// <summary>
    /// Account levels. They mean nothing except "not on day one": the game is free to play, so a
    /// fresh account costs nothing, and the thing a mule would want on day one (receiving gear from
    /// another player) waits for a few sessions of real play. The three earned classes wait longer.
    /// The shared bank is NOT gated: it moves gear between one account's own characters, which is
    /// no use to a mule. Nothing here is power.
    /// XP comes from doing the thing: extracting, killing enemy players, killing bosses. Never time.
    /// </summary>
    public static class AccountRules
    {
        public const int XpPerExtraction = 120;
        public const int XpPerPlayerKill = 40;
        public const int XpPerBossKill = 60;
        public const int XpPerFinalBossKill = 150;

        /// <summary>Total XP needed to reach a level: 150 per level, rising. Level 5 is 2,250 (about eighteen extractions, or fifteen with a few kills: a weekend); level 10 is 8,250; level 20 is 31,500.</summary>
        public static long XpToReach(int level) => level <= 1 ? 0 : 150L * level * (level + 1) / 2;
        public static int LevelFor(long xp) { int l = 1; while (XpToReach(l + 1) <= xp) l++; return l; }

        /// <summary>Hand-offs in the dungeon (giving and receiving) open here.</summary>
        public const int HandOffLevel = 5;

        public static readonly IReadOnlyList<string> StartingClasses = new[] { "Warden", "Marksman", "Mender" };
        public static readonly IReadOnlyDictionary<string, int> ClassUnlockLevel = new Dictionary<string, int>
        {
            ["Reaver"] = 10, ["Elementalist"] = 15, ["Shade"] = 20,
        };

        public static bool ClassUnlocked(string className, int accountLevel) =>
            StartingClasses.Contains(className) || (ClassUnlockLevel.TryGetValue(className, out int need) && accountLevel >= need);
        public static int LevelForClass(string className) => StartingClasses.Contains(className) ? 1 : ClassUnlockLevel.TryGetValue(className, out int n) ? n : int.MaxValue;
        public static bool CanHandOff(int accountLevel) => accountLevel >= HandOffLevel;
    }

    /// <summary>
    /// Kill XP is paid once per victim per day. Two accounts trading kills across rounds earn one
    /// kill's worth each per day, which is not a ladder to level 5. Souls in the dungeon are
    /// unaffected: they are spent inside the round and burn with you.
    /// </summary>
    public sealed class KillXpTracker
    {
        public const int CooldownHours = 24;
        private readonly Dictionary<(string killer, string victim), DateTime> _last = new Dictionary<(string, string), DateTime>();
        /// <summary>XP to award for this kill: the full amount, or 0 when this killer already earned XP for this victim within the cooldown.</summary>
        public int Award(string killer, string victim, DateTime whenUtc)
        {
            if (_last.TryGetValue((killer, victim), out var last) && (whenUtc - last).TotalHours < CooldownHours) return 0;
            _last[(killer, victim)] = whenUtc;
            return AccountRules.XpPerPlayerKill;
        }
    }

    /// <summary>
    /// Every in-dungeon hand-off, kept for review. Real-money trading looks like value flowing one
    /// way between two accounts, repeatedly; the ledger makes that visible, a human makes the call.
    /// </summary>
    public sealed class TransferLedger
    {
        public readonly struct Transfer
        {
            public readonly DateTime WhenUtc; public readonly string From, To; public readonly int ItemValue;
            public Transfer(DateTime when, string from, string to, int value) { WhenUtc = when; From = from; To = to; ItemValue = value; }
        }
        public List<Transfer> Transfers { get; } = new List<Transfer>();
        /// <summary>Net one-way value from one account to another before a pair is flagged.</summary>
        public const int FlagNetValue = 300;
        public const int FlagMinTransfers = 3;

        public void Record(DateTime whenUtc, string from, string to, int value) => Transfers.Add(new Transfer(whenUtc, from, to, value));

        /// <summary>Pairs whose flow is lopsided enough to look at: net value one way at or above the threshold over at least the minimum count.</summary>
        public IEnumerable<(string from, string to, int netValue, int count)> Flagged()
        {
            var pairs = Transfers.GroupBy(t => string.CompareOrdinal(t.From, t.To) < 0 ? (t.From, t.To) : (t.To, t.From));
            foreach (var g in pairs)
            {
                int ab = g.Where(t => t.From == g.Key.Item1).Sum(t => t.ItemValue), ba = g.Where(t => t.From == g.Key.Item2).Sum(t => t.ItemValue);
                int net = ab - ba, count = g.Count();
                if (count >= FlagMinTransfers && Math.Abs(net) >= FlagNetValue)
                    yield return net > 0 ? (g.Key.Item1, g.Key.Item2, net, count) : (g.Key.Item2, g.Key.Item1, -net, count);
            }
        }
    }
}
