using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DungeonGen.Core.Items;
using DungeonGen.Core.Net;
using DungeonGen.Core.Profile;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonGen.Unity.UI
{
    /// <summary>
    /// The round-end card: extracted or died, what the round was (<see cref="RoundSummary"/>) as a grid of figures on the left, and what it did to
    /// the profile (<see cref="SettlementReport"/>) on the right: silver and XP earned with the new totals, then the items gained, kept, salvaged
    /// and lost, and the pockets. An item broken down with a Salvage Kit shows as Salvaged, not Lost, when the log says so. Without a report (a
    /// greybox round with no flow) it shows the round half only. On a remote client both halves come from its own settlement of the server's <see cref="RoundResult"/>.
    /// </summary>
    public static class SummaryPanel
    {
        public const string Name = "RoundSummaryPanel";

        private const float CardW = 1180f, CardH = 860f;
        private const float LeftX = 60f, RightX = 620f, ColW = 496f, ContentTop = 166f;

        public static RectTransform Build(Transform parent, RoundSummary s, SettlementReport report, MatchState st, Action onReturn)
        {
            // A dim layer over the round, then the card.
            var dim = UiStyle.Img(parent, Name, new Color(0f, 0f, 0f, 0.62f));
            dim.rectTransform.Stretch(); dim.raycastTarget = true;
            var p = UiStyle.Panel(dim.transform, "Card");
            p.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(CardW, CardH));

            bool extracted = report != null ? report.Outcome == RoundOutcome.Extracted : s.Extracted;
            // A remote client's mirror has no log: the server's result says whether the clock did it.
            var result = ClientMirror.Current?.Result;
            bool clock = !extracted && (result != null && result.PlayerId == s.Player ? result.DiedToClock
                : st != null && st.Ended && !st.Log.Any(e => (e.Kind == MatchEventKind.PlayerDied || e.Kind == MatchEventKind.PlayerFinished) && e.A == s.Player));
            var title = UiStyle.Label(p, "Title", extracted ? "EXTRACTED" : clock ? "LOST TO THE CLOCK" : "YOU DIED", UiStyle.Size.Hero, extracted ? UiStyle.Gold : UiStyle.Bad, TextAnchor.MiddleCenter, FontStyle.Bold);
            title.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(1000f, 72f));
            float t = st?.Time ?? 0f;
            string tier = DungeonGen.Core.DungeonTier.All.FirstOrDefault(x => x.Number == s.Tier)?.Name ?? $"Tier {s.Tier}";
            var sub = UiStyle.Label(p, "Sub", extracted ? $"{tier}  ·  out after {UiStyle.Clock(t)}  ·  everything you carried out is yours"
                                                         : $"{tier}  ·  {UiStyle.Clock(t)} into the round  ·  full loot: what you brought and found is gone", UiStyle.Size.Label, UiStyle.TextSoft, TextAnchor.MiddleCenter, FontStyle.Italic);
            sub.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -102f), new Vector2(1100f, 36f));
            UiStyle.Rule(p, "Rule", 1060f, -146f);

            // Left: the round, as a grid of figures so the column is as full as the right one.
            Heading(p, "RoundHeading", "THE ROUND", LeftX, ContentTop);
            var tiles = new List<(string value, string label)>
            {
                (s.RoomsExplored.ToString(), "Rooms explored"),
                (UiStyle.Clock(t), "In the dungeon"),
                (s.PlayerKills.ToString(), "Players killed"),
                (s.BossKills + (s.FinalBossKill ? "<size=24> + final</size>" : ""), "Bosses killed"),
                ($"{s.SoulsEarned} / {s.SoulsSpent}", "Souls earned / spent"),
                (s.ItemsExtracted.ToString(), "Items carried out"),
                (s.ExtractedValue.ToString("N0"), "Value extracted"),
                (s.ExtractedSilver.ToString("N0"), "Silver extracted"),
            };
            for (int i = 0; i < tiles.Count; i++)
            {
                int col = i % 2, row = i / 2;
                Tile(p, "Tile-" + i, new Vector2(LeftX + col * (ColW / 2f + 8f), -(ContentTop + 40f + row * 100f)), new Vector2(ColW / 2f - 8f, 88f), tiles[i].value, tiles[i].label, UiStyle.Gold, null);
            }

            // Right: the profile.
            Heading(p, "ProfileHeading", "YOUR PROFILE", RightX, ContentTop);
            var profile = GameFlow.Instance?.Profile;
            float textTop = ContentTop + 40f;
            if (report == null || report.AlreadySettled)
            {
                var note = UiStyle.Label(p, "Profile", UiStyle.Colour(report == null ? "Practice round without a profile: nothing was saved." : "This round was already settled.", UiStyle.TextDim), UiStyle.Size.Body, UiStyle.TextMain, TextAnchor.UpperLeft);
                note.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(RightX, -textTop), new Vector2(ColW, 120f));
            }
            else
            {
                // What the round earned, and what the account holds now: "+0" never reads as the total.
                Tile(p, "Tile-Silver", new Vector2(RightX, -textTop), new Vector2(ColW / 2f - 8f, 104f), Signed(report.SilverDelta, UiStyle.Gold), "Silver earned", UiStyle.Gold, profile != null ? $"{profile.Silver:N0} in total" : null);
                Tile(p, "Tile-Xp", new Vector2(RightX + ColW / 2f + 8f, -textTop), new Vector2(ColW / 2f - 8f, 104f), Signed(report.XpDelta, UiStyle.Good), "XP earned", UiStyle.Good, profile != null ? $"{profile.AccountXp:N0} in total" : null);
                textTop += 104f + 14f;

                var salvaged = SalvagedOf(report, GameFlow.Instance?.Server?.State ?? st, s.Player);   // the host's own log has every salvage; a client's slice has none
                var lost = report.ItemsLost.Concat(report.PickupsLost).ToList();
                var lostForReal = lost.Where(i => !salvaged.Contains(i)).ToList();
                var salvagedItems = lost.Where(salvaged.Contains).ToList();
                bool unknownSalvage = extracted && salvagedItems.Count == 0 && !MaterialsEmpty(report.MaterialsGained);   // a client's log does not say which; the materials do say it happened

                var right = new StringBuilder();
                if (report.LevelAfter > report.LevelBefore) right.Append(UiStyle.Colour($"Account level {report.LevelBefore} → {report.LevelAfter}!", UiStyle.Gold)).Append('\n');
                int budget = report.LevelAfter > report.LevelBefore ? 12 : 13;
                var sections = new List<Section>
                {
                    new Section { Label = "Gained", Items = report.ItemsGained, Colour = UiStyle.Good, Limit = 4 },
                    new Section { Label = "Kept", Items = report.ItemsKept, Colour = UiStyle.TextMain, Limit = 3 },
                    new Section { Label = "Salvaged", Items = salvagedItems, Colour = UiStyle.Gold, Limit = 3, Foot = salvagedItems.Count > 0 ? "into " + Materials(report.MaterialsGained) : null },
                    new Section { Label = extracted ? (unknownSalvage ? "Used up or left behind" : "Left behind") : "Lost", Items = lostForReal, Colour = extracted ? UiStyle.TextDim : UiStyle.Bad, Limit = 4 },
                };
                var pockets = report.ConsumableDelta.OrderBy(k => k.Key, StringComparer.Ordinal).ToList();
                int fixedLines = pockets.Count + (report.CharacterNaked ? 2 : 0) + (unknownSalvage ? 1 : 0);
                while (sections.Sum(x => x.Lines) + fixedLines > budget && sections.Any(x => x.Limit > 1))
                    sections.OrderByDescending(x => x.Limit).First().Limit--;
                foreach (var sec in sections) sec.Write(right);
                if (unknownSalvage) right.Append(UiStyle.Colour("Salvaged on the way out: " + Materials(report.MaterialsGained), UiStyle.Gold)).Append('\n');
                foreach (var kv in pockets)
                    right.Append($"{kv.Key}  {Signed(kv.Value, kv.Value > 0 ? UiStyle.Good : UiStyle.Bad)}\n");
                if (report.CharacterNaked) right.Append('\n').Append(UiStyle.Colour("Your character is naked. A free starter kit waits in the menu.", UiStyle.Gold));
                var r = UiStyle.Label(p, "Profile", right.ToString(), UiStyle.Size.Body, UiStyle.TextMain, TextAnchor.UpperLeft);
                r.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(RightX, -textTop), new Vector2(ColW, CardH - textTop - 200f));
                r.lineSpacing = 1.12f;
            }
            var divider = UiStyle.Img(p, "Divider", UiStyle.GoldFaint);
            divider.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(RightX - 30f, -(ContentTop + 4f)), new Vector2(2f, CardH - ContentTop - 214f));

            // XP bar for the level the account is now at: a visible track, the level on the left, the progress on the right.
            if (report != null)
            {
                long xp = AccountRules.XpToReach(report.LevelAfter);
                long next = AccountRules.XpToReach(report.LevelAfter + 1);
                long have = profile?.AccountXp ?? xp;
                var bar = new UiBar(p, "XpBar", new Vector2(1060f, 20f), UiStyle.Gold);
                bar.Root.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 128f), new Vector2(1060f, 20f));
                bar.Set(next > xp ? (float)(have - xp) / (next - xp) : 1f, null);
                var level = UiStyle.Label(p, "XpLevel", $"ACCOUNT LEVEL {report.LevelAfter}", UiStyle.Size.Label, UiStyle.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
                level.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(-530f, 152f), new Vector2(500f, 34f));
                var bl = UiStyle.Label(p, "XpText", $"{have - xp} / {next - xp} XP to level {report.LevelAfter + 1}", UiStyle.Size.Body, UiStyle.TextSoft, TextAnchor.MiddleRight);
                bl.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(530f, 152f), new Vector2(500f, 34f));
            }

            var back = UiStyle.MakeButton(p, "ReturnButton", "Return to the menu", onReturn, UiStyle.Size.Heading);
            ((RectTransform)back.transform).Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(420f, 70f));
            back.GetComponent<UiButtonSound>().ClickKey = UiSound.Confirm;
            return dim.rectTransform;
        }

        private static void Heading(RectTransform p, string name, string text, float x, float top)
        {
            var h = UiStyle.Label(p, name, text, UiStyle.Size.Body, UiStyle.GoldDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            h.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -top), new Vector2(ColW, 30f));
        }

        /// <summary>A figure with its caption under it (and a note under that, when there is one).</summary>
        private static void Tile(RectTransform p, string name, Vector2 pos, Vector2 size, string value, string label, Color valueColour, string foot)
        {
            var bg = UiStyle.Img(p, name, new Color(0.115f, 0.100f, 0.100f, 0.96f), UiStyle.Rounded);
            bg.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), pos, size);
            var frame = UiStyle.Img(bg.transform, "Frame", UiStyle.GoldFaint, UiStyle.Frame); frame.rectTransform.Stretch();
            var v = UiStyle.Label(bg.transform, "Value", value, UiStyle.Size.Title, valueColour, TextAnchor.MiddleLeft, FontStyle.Bold);
            v.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -4f), new Vector2(size.x - 28f, 48f));
            v.horizontalOverflow = HorizontalWrapMode.Overflow;
            var l = UiStyle.Label(bg.transform, "Label", label, UiStyle.Size.Small, UiStyle.TextSoft, TextAnchor.MiddleLeft);
            l.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -52f), new Vector2(size.x - 28f, 26f));
            if (foot != null)
            {
                var f = UiStyle.Label(bg.transform, "Total", foot, UiStyle.Size.Small, UiStyle.TextDim, TextAnchor.MiddleLeft);
                f.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -76f), new Vector2(size.x - 28f, 24f));
            }
        }

        private sealed class Section
        {
            public string Label, Foot; public IList<Item> Items; public Color Colour; public int Limit;
            public int Lines => Items.Count == 0 ? 0 : 1 + Math.Min(Items.Count, Limit) + (Items.Count > Limit ? 1 : 0) + (Foot != null ? 1 : 0);
            public void Write(StringBuilder sb)
            {
                if (Items.Count == 0) return;
                sb.Append(UiStyle.Colour($"{Label} ({Items.Count})", Colour)).Append('\n');
                foreach (var it in Items.Take(Limit)) sb.Append("   ").Append(UiStyle.Colour(it.Name, MainMenuUi.RarityColour(it.Rarity))).Append('\n');
                if (Items.Count > Limit) sb.Append(UiStyle.Colour($"   and {Items.Count - Limit} more", UiStyle.TextDim)).Append('\n');
                if (Foot != null) sb.Append(UiStyle.Colour("   " + Foot, UiStyle.TextDim)).Append('\n');
            }
        }

        /// <summary>
        /// The items this player brought that a Salvage Kit broke down in the round. After an extraction an <see cref="MatchEventKind.ItemDestroyed"/> is only ever
        /// a salvage (the death trash happens to the dead), so a lost item whose id was destroyed was salvaged. Items brought and lost come in registration order, and pickups
        /// that did not come home in the order of their first pickup, which is how the ids line up with the report. Empty when the log cannot say (a remote client's slice has no such events).
        /// </summary>
        private static HashSet<Item> SalvagedOf(SettlementReport report, MatchState st, int player)
        {
            var set = new HashSet<Item>();
            if (report == null || st == null || report.Outcome != RoundOutcome.Extracted) return set;
            var destroyed = new HashSet<int>(st.Log.Where(e => e.Kind == MatchEventKind.ItemDestroyed).Select(e => e.A));
            if (destroyed.Count == 0) return set;
            bool Owned(int id) => st.ItemOwner.TryGetValue(id, out int owner) && owner == player;
            var brought = st.Log.Where(e => e.Kind == MatchEventKind.ItemRegistered && e.B == player).Select(e => e.A).ToList();
            var broughtSet = new HashSet<int>(brought);
            // Brought and gone, in the order settlement lists them; then picked up in the round and gone, in the order of the first pickup.
            var lostIds = brought.Where(id => !Owned(id)).ToList();
            var pickedIds = st.Log.Where(e => e.Kind == MatchEventKind.ItemTaken && e.B == player).Select(e => e.A).Distinct().Where(id => !broughtSet.Contains(id) && !Owned(id)).ToList();
            if (lostIds.Count == report.ItemsLost.Count) for (int i = 0; i < lostIds.Count; i++) if (destroyed.Contains(lostIds[i])) set.Add(report.ItemsLost[i]);
            if (pickedIds.Count == report.PickupsLost.Count) for (int i = 0; i < pickedIds.Count; i++) if (destroyed.Contains(pickedIds[i])) set.Add(report.PickupsLost[i]);
            return set;
        }

        private static bool MaterialsEmpty(MaterialBag m) => m == null || (m.Scrap == 0 && m.PrimeShards == 0 && m.Essences.Count == 0);

        private static string Materials(MaterialBag m)
        {
            if (MaterialsEmpty(m)) return "nothing";
            var parts = new List<string>();
            if (m.Scrap > 0) parts.Add($"{m.Scrap} scrap");
            if (m.PrimeShards > 0) parts.Add($"{m.PrimeShards} shard{(m.PrimeShards == 1 ? "" : "s")}");
            int essences = m.Essences.Values.Sum();
            if (essences > 0) parts.Add($"{essences} essence{(essences == 1 ? "" : "s")}");
            return string.Join(", ", parts);
        }

        private static string Signed(long v, Color c) => UiStyle.Colour((v >= 0 ? "+" : "") + v.ToString("N0"), v == 0 ? UiStyle.TextDim : c);
    }
}
