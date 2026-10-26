using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DungeonGen.Core.Items;
using DungeonGen.Core.Net;
using DungeonGen.Core.Profile;
using UnityEngine;

namespace DungeonGen.Unity.UI
{
    /// <summary>
    /// The round-end card: extracted or died, what the round was (<see cref="RoundSummary"/>), and what it did to the profile
    /// (<see cref="SettlementReport"/>: items gained and lost, silver, XP, level). Without a report (a greybox round with no
    /// flow) it shows the round half only.
    /// </summary>
    public static class SummaryPanel
    {
        public const string Name = "RoundSummaryPanel";

        public static RectTransform Build(Transform parent, RoundSummary s, SettlementReport report, MatchState st, Action onReturn)
        {
            // A dim layer over the round, then the card.
            var dim = UiStyle.Img(parent, Name, new Color(0f, 0f, 0f, 0.55f));
            dim.rectTransform.Stretch(); dim.raycastTarget = true;
            var p = UiStyle.Panel(dim.transform, "Card");
            p.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(1180f, 780f));

            bool extracted = report != null ? report.Outcome == RoundOutcome.Extracted : s.Extracted;
            bool clock = !extracted && st != null && st.Ended && !st.Log.Any(e => (e.Kind == MatchEventKind.PlayerDied || e.Kind == MatchEventKind.PlayerFinished) && e.A == s.Player);
            var title = UiStyle.Label(p, "Title", extracted ? "EXTRACTED" : clock ? "LOST TO THE CLOCK" : "YOU DIED", 64, extracted ? UiStyle.Gold : UiStyle.Bad, TextAnchor.MiddleCenter, FontStyle.Bold);
            title.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -26f), new Vector2(1000f, 80f));
            float t = st?.Time ?? 0f;
            string tier = DungeonGen.Core.DungeonTier.All.FirstOrDefault(x => x.Number == s.Tier)?.Name ?? $"Tier {s.Tier}";
            var sub = UiStyle.Label(p, "Sub", extracted ? $"{tier}  ·  out after {UiStyle.Clock(t)}  ·  everything you carried out is yours"
                                                         : $"{tier}  ·  {UiStyle.Clock(t)} into the round  ·  full loot: what you brought and found is gone", 22, UiStyle.TextDim, TextAnchor.MiddleCenter, FontStyle.Italic);
            sub.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -108f), new Vector2(1100f, 34f));
            UiStyle.Rule(p, "Rule", 1060f, -144f);

            // Left: the round.
            var left = new StringBuilder();
            left.Append(UiStyle.Colour("THE ROUND", UiStyle.GoldDim)).Append("\n\n");
            Row(left, "Rooms explored", s.RoomsExplored.ToString());
            Row(left, "Players killed", s.PlayerKills.ToString());
            Row(left, "Bosses killed", s.BossKills + (s.FinalBossKill ? " + the final boss" : ""));
            Row(left, "Souls earned / spent", $"{s.SoulsEarned} / {s.SoulsSpent}");
            Row(left, "Items carried out", s.ItemsExtracted.ToString());
            Row(left, "Value extracted", s.ExtractedValue.ToString());
            Row(left, "Silver extracted", s.ExtractedSilver.ToString());
            var l = UiStyle.Label(p, "Round", left.ToString(), 23, UiStyle.TextMain, TextAnchor.UpperLeft);
            l.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(60f, -176f), new Vector2(500f, 420f));
            l.lineSpacing = 1.15f;

            // Right: the profile.
            var right = new StringBuilder();
            right.Append(UiStyle.Colour("YOUR PROFILE", UiStyle.GoldDim)).Append("\n\n");
            if (report == null) right.Append(UiStyle.Colour("Practice round without a profile: nothing was saved.", UiStyle.TextDim));
            else if (report.AlreadySettled) right.Append(UiStyle.Colour("This round was already settled.", UiStyle.TextDim));
            else
            {
                right.Append($"Silver  {Signed(report.SilverDelta, UiStyle.Gold)}     XP  {Signed(report.XpDelta, UiStyle.Good)}\n");
                right.Append(report.LevelAfter > report.LevelBefore
                    ? UiStyle.Colour($"Account level {report.LevelBefore} → {report.LevelAfter}!", UiStyle.Gold) + "\n\n"
                    : $"Account level {report.LevelAfter}\n\n");
                Items(right, "Gained", report.ItemsGained, UiStyle.Good, 6);
                Items(right, "Kept", report.ItemsKept, UiStyle.TextMain, 4);
                Items(right, "Lost", report.ItemsLost.Concat(report.PickupsLost).ToList(), UiStyle.Bad, 6);
                foreach (var kv in report.ConsumableDelta.OrderBy(k => k.Key, StringComparer.Ordinal))
                    right.Append($"{kv.Key}  {Signed(kv.Value, kv.Value > 0 ? UiStyle.Good : UiStyle.Bad)}\n");
                if (report.CharacterNaked) right.Append('\n').Append(UiStyle.Colour("Your character is naked. A free starter kit waits in the menu.", UiStyle.Gold));
            }
            var r = UiStyle.Label(p, "Profile", right.ToString(), 22, UiStyle.TextMain, TextAnchor.UpperLeft);
            r.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(620f, -176f), new Vector2(500f, 460f));
            r.lineSpacing = 1.1f;
            var divider = UiStyle.Img(p, "Divider", UiStyle.GoldFaint);
            divider.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -180f), new Vector2(2f, 440f));

            // XP bar for the level the account is now at.
            if (report != null)
            {
                long xp = AccountRules.XpToReach(report.LevelAfter);
                long next = AccountRules.XpToReach(report.LevelAfter + 1);
                var profile = GameFlow.Instance?.Profile;
                long have = profile?.AccountXp ?? xp;
                var bar = new UiBar(p, "XpBar", new Vector2(1060f, 12f), UiStyle.Gold);
                bar.Root.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 128f), new Vector2(1060f, 12f));
                bar.Set(next > xp ? (float)(have - xp) / (next - xp) : 1f, null);
                var bl = UiStyle.Label(p, "XpText", $"{have - xp} / {next - xp} XP to level {report.LevelAfter + 1}", 18, UiStyle.TextDim, TextAnchor.MiddleRight);
                bl.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(1060f, 26f));
            }

            var back = UiStyle.MakeButton(p, "ReturnButton", "Return to the menu", onReturn, 28);
            ((RectTransform)back.transform).Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(420f, 70f));
            back.GetComponent<UiButtonSound>().ClickKey = UiSound.Confirm;
            return dim.rectTransform;
        }

        private static void Row(StringBuilder sb, string label, string value) =>
            sb.Append($"{label}<color=#{UiStyle.Hex(UiStyle.TextDim)}>  ·  </color>{UiStyle.Colour(value, UiStyle.Gold)}\n");

        private static string Signed(long v, Color c) => UiStyle.Colour((v > 0 ? "+" : "") + v.ToString("N0"), v == 0 ? UiStyle.TextDim : c);

        private static void Items(StringBuilder sb, string label, IList<Item> items, Color c, int max)
        {
            if (items.Count == 0) return;
            sb.Append(UiStyle.Colour($"{label} ({items.Count})", c)).Append('\n');
            foreach (var it in items.Take(max)) sb.Append("   ").Append(UiStyle.Colour(it.Name, MainMenuUi.RarityColour(it.Rarity))).Append('\n');
            if (items.Count > max) sb.Append(UiStyle.Colour($"   and {items.Count - max} more", UiStyle.TextDim)).Append('\n');
        }
    }
}
