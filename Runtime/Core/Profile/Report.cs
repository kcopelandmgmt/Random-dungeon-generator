using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core.Profile
{
    public enum ReportReason : byte { Cheating, RealMoneyTrading, Abuse }

    /// <summary>
    /// One click from the death screen, the scoreboard or the spectator view. A report carries the
    /// match id so a human can read that match's log and the transfer ledger. No automated bans:
    /// reports are read, never counted into an action, because counting is what a coordinated
    /// group would weaponise.
    /// </summary>
    public readonly struct PlayerReport
    {
        public readonly DateTime WhenUtc; public readonly string Reporter, Accused, MatchId; public readonly ReportReason Reason; public readonly string Note;
        public PlayerReport(DateTime when, string reporter, string accused, string matchId, ReportReason reason, string note = null)
        { WhenUtc = when; Reporter = reporter; Accused = accused; MatchId = matchId; Reason = reason; Note = note; }
    }

    public sealed class ReportQueue
    {
        public const bool AutomatedAction = false;
        public List<PlayerReport> Reports { get; } = new List<PlayerReport>();

        /// <summary>One report per reporter, accused and match; a second on the same match is ignored, so spamming does nothing.</summary>
        public bool Submit(PlayerReport r)
        {
            if (string.IsNullOrEmpty(r.MatchId) || r.Reporter == r.Accused) return false;
            if (Reports.Any(x => x.Reporter == r.Reporter && x.Accused == r.Accused && x.MatchId == r.MatchId)) return false;
            Reports.Add(r); return true;
        }

        /// <summary>Accounts with the most distinct reporters first: the reading order for a human, nothing more.</summary>
        public IEnumerable<(string accused, int reporters, int reports)> ReadingOrder() =>
            Reports.GroupBy(r => r.Accused).Select(g => (g.Key, g.Select(r => r.Reporter).Distinct().Count(), g.Count())).OrderByDescending(t => t.Item2).ThenByDescending(t => t.Item3);
    }
}
