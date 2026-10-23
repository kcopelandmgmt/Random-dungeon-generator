using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core.Profile
{
    /// <summary>
    /// Account levels. They mean nothing except "not on day one": the game is free to play, so a
    /// fresh account costs nothing, and the things a mule or a cheat would want on day one (handing
    /// gear to another player, the shared bank between characters) wait for a few sessions of real
    /// play. The three earned classes wait longer. Nothing here is power.
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

        /// <summary>Hand-offs in the dungeon and the shared bank open here.</summary>
        public const int HandOffLevel = 5;
        public const int BankLevel = 5;

        public static readonly IReadOnlyList<string> StartingClasses = new[] { "Warden", "Marksman", "Mender" };
        public static readonly IReadOnlyDictionary<string, int> ClassUnlockLevel = new Dictionary<string, int>
        {
            ["Reaver"] = 10, ["Elementalist"] = 15, ["Shade"] = 20,
        };

        public static bool ClassUnlocked(string className, int accountLevel) =>
            StartingClasses.Contains(className) || (ClassUnlockLevel.TryGetValue(className, out int need) && accountLevel >= need);
        public static int LevelForClass(string className) => StartingClasses.Contains(className) ? 1 : ClassUnlockLevel.TryGetValue(className, out int n) ? n : int.MaxValue;
        public static bool CanHandOff(int accountLevel) => accountLevel >= HandOffLevel;
        public static bool BankUnlocked(int accountLevel) => accountLevel >= BankLevel;
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
