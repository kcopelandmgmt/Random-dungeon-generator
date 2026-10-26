using System;
using System.Linq;
using DungeonGen.Core.Items;
using DungeonGen.Core.Profile;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonGen.Unity.UI
{
    /// <summary>
    /// The Vendor screen of the main menu (the only trade at launch, 2026-10-22 round rules #9): buy the three consumables at
    /// their fixed prices, sell stash items for silver (<see cref="CraftingRules.SellPrice"/>; starter gear is worth nothing) or
    /// salvage them into scrap, prime shards and typed essences (<see cref="CraftingRules.Salvage"/>), all through
    /// <see cref="Vendor"/>. Every change is saved through <see cref="GameFlow.Save"/>. Buttons: Buy-&lt;name&gt;, Sell-&lt;i&gt;,
    /// Salvage-&lt;i&gt; (i = stash index), PrevPage, NextPage, BackButton, StashButton.
    /// </summary>
    public static class VendorScreen
    {
        public const int PerPage = 5;
        public static int Page;
        public static string Note;

        public static RectTransform Build(RectTransform parent, GameFlow flow, Action back, Action rebuild, Action toStash)
        {
            var panel = UiStyle.Panel(parent, "VendorPanel");
            panel.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(1600f, 760f));
            var head = UiStyle.Label(panel, "Header", "VENDOR", 40, UiStyle.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            head.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(1000f, 56f));
            UiStyle.Rule(panel, "HeaderRule", 520f, -88f);
            var p = flow.Profile;
            if (p == null)
            {
                var none = UiStyle.Label(panel, "None", "Create a character first.", 28, UiStyle.TextDim, TextAnchor.MiddleCenter);
                none.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800f, 50f));
                StashScreen.Btn(panel, "BackButton", "Back", back, new Vector2(0f, 0f), new Vector2(40f, 28f), new Vector2(240f, 60f));
                return panel;
            }
            var m = p.Materials;
            var purse = UiStyle.Label(panel, "Purse", $"Silver {UiStyle.Colour(p.Silver.ToString("N0"), UiStyle.Gold)}      Scrap {m.Scrap}      Prime shards {m.PrimeShards}      Essences {m.Essences.Values.Sum()}", 24, UiStyle.TextMain, TextAnchor.MiddleCenter);
            purse.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -104f), new Vector2(1400f, 36f));

            // Buy.
            StashScreen.Section(panel, "BuyTitle", "BUY", new Vector2(40f, -150f));
            float y = -190f;
            foreach (var c in Consumable.All)
            {
                var cc = c;
                var row = UiStyle.Img(panel, "Row-" + c.Name, new Color(1f, 1f, 1f, 0.035f), UiStyle.Rounded);
                row.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, y), new Vector2(620f, 112f));
                var n = UiStyle.Label(row.transform, "Name", $"<b>{c.Name}</b>   {UiStyle.Colour($"you have {p.ConsumableCount(c)}", UiStyle.TextDim)}", 24, UiStyle.TextMain, TextAnchor.UpperLeft);
                n.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -10f), new Vector2(420f, 32f));
                var e = UiStyle.Label(row.transform, "Effect", c.Effect + (c.CooldownSeconds > 0f ? $"  {c.CooldownSeconds:0} s cooldown." : ""), 17, UiStyle.TextDim, TextAnchor.UpperLeft);
                e.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -46f), new Vector2(420f, 60f));
                var b = UiStyle.MakeButton(row.transform, "Buy-" + c.Name, $"Buy · {c.VendorPriceSilver}", () =>
                {
                    bool ok = Vendor.Buy(p, cc);
                    Done(flow, ok ? null : $"{cc.Name} costs {cc.VendorPriceSilver} silver.", ok ? $"Bought a {cc.Name}." : null, rebuild);
                }, 22);
                ((RectTransform)b.transform).Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(150f, 52f));
                b.interactable = p.Silver >= c.VendorPriceSilver;
                y -= 122f;
            }
            var potHint = UiStyle.Label(panel, "PocketHint", "Every Health Potion and Antidote in the bank goes into your pockets (1, 2) when you enter. A death loses them.", 17, UiStyle.TextFaint, TextAnchor.UpperLeft);
            potHint.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, y - 4f), new Vector2(620f, 50f));

            // Sell or salvage.
            StashScreen.Section(panel, "SellTitle", $"SELL OR SALVAGE  {UiStyle.Colour($"{p.Stash.Count} in the stash", UiStyle.TextFaint)}", new Vector2(700f, -150f));
            int pages = Mathf.Max(1, (p.Stash.Count + PerPage - 1) / PerPage);
            Page = Mathf.Clamp(Page, 0, pages - 1);
            y = -190f;
            for (int i = Page * PerPage; i < Math.Min(p.Stash.Count, (Page + 1) * PerPage); i++)
            {
                var it = p.Stash[i];
                float price = CraftingRules.SellPrice(it);
                var yield_ = CraftingRules.Salvage(it);
                var row = UiStyle.Img(panel, "Item-" + i, new Color(1f, 1f, 1f, 0.035f), UiStyle.Rounded);
                row.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(700f, y), new Vector2(860f, 66f));
                StashScreen.Stripe(row.transform, it.Rarity);
                var n = UiStyle.Label(row.transform, "Name", $"<b>{ItemText.Name(it)}</b>   {UiStyle.Colour(ItemText.Line(it), UiStyle.TextDim)}", 19, UiStyle.TextMain, TextAnchor.UpperLeft);
                n.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -6f), new Vector2(560f, 28f));
                n.horizontalOverflow = HorizontalWrapMode.Overflow;
                var v = UiStyle.Label(row.transform, "Value", it.IsStarter ? UiStyle.Colour("Starter gear: worth nothing, salvages to nothing.", UiStyle.TextFaint)
                    : $"sells for {UiStyle.Colour(Mathf.RoundToInt(price).ToString(), UiStyle.Gold)}  ·  salvages to {ItemText.Salvage(yield_)}", 16, UiStyle.TextDim, TextAnchor.UpperLeft);
                v.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -36f), new Vector2(560f, 24f));
                var item = it;
                var sell = UiStyle.MakeButton(row.transform, "Sell-" + i, "Sell", () =>
                {
                    float got = Vendor.Sell(p, item);
                    Done(flow, null, $"Sold {item.Name} for {Mathf.RoundToInt(got)} silver.", rebuild);
                }, 20);
                ((RectTransform)sell.transform).Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-136f, 0f), new Vector2(112f, 46f));
                var salv = UiStyle.MakeButton(row.transform, "Salvage-" + i, "Salvage", () =>
                {
                    var got = Vendor.Salvage(p, item);
                    Done(flow, null, $"Salvaged {item.Name}: {ItemText.Salvage(got)}.", rebuild);
                }, 20);
                ((RectTransform)salv.transform).Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(118f, 46f));
                sell.interactable = salv.interactable = !it.IsStarter && !it.Condition.IsDestroyed;
                y -= 74f;
            }
            if (p.Stash.Count == 0)
            {
                var empty = UiStyle.Label(panel, "Empty", "The stash is empty. What you carry out of the dungeon lands here.", 20, UiStyle.TextDim, TextAnchor.MiddleLeft);
                empty.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(700f, -200f), new Vector2(860f, 30f));
            }
            var pageText = UiStyle.Label(panel, "Page", $"Page {Page + 1} / {pages}", 20, UiStyle.TextDim, TextAnchor.MiddleCenter);
            pageText.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(700f + 430f, -592f), new Vector2(200f, 30f));
            var prev = StashScreen.Btn(panel, "PrevPage", "<", () => { Page--; rebuild(); }, new Vector2(0f, 1f), new Vector2(700f, -572f), new Vector2(80f, 44f), new Vector2(0f, 1f));
            prev.interactable = Page > 0;
            var next = StashScreen.Btn(panel, "NextPage", ">", () => { Page++; rebuild(); }, new Vector2(0f, 1f), new Vector2(700f + 780f, -572f), new Vector2(80f, 44f), new Vector2(0f, 1f));
            next.interactable = Page < pages - 1;

            var note = UiStyle.Label(panel, "Note", Note ?? "Salvaging here is free; a Salvage Kit does it inside the dungeon.", 21, Note == null ? UiStyle.TextFaint : Note.StartsWith("Sold") || Note.StartsWith("Bought") || Note.StartsWith("Salvaged") ? UiStyle.Good : UiStyle.Bad, TextAnchor.MiddleCenter);
            note.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 44f), new Vector2(900f, 30f));
            StashScreen.Btn(panel, "BackButton", "Back", () => { Note = null; back(); }, new Vector2(0f, 0f), new Vector2(40f, 28f), new Vector2(240f, 60f));
            StashScreen.Btn(panel, "StashButton", "Stash & Equipment", () => { Note = null; toStash(); }, new Vector2(1f, 0f), new Vector2(-40f, 28f), new Vector2(320f, 60f), new Vector2(1f, 0f));
            return panel;
        }

        private static void Done(GameFlow flow, string refusal, string success, Action rebuild)
        {
            if (refusal != null) { Note = refusal; UiSound.Play(UiSound.Error); }
            else { flow.Save(); Note = success; UiSound.Play(UiSound.Confirm); }
            rebuild();
        }
    }
}
