using System;
using System.Linq;
using DungeonGen.Core.Items;
using DungeonGen.Core.Profile;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonGen.Unity.UI
{
    /// <summary>
    /// The Stash &amp; Equipment screen of the main menu: the selected character's paper doll by slot, the shared stash in pages,
    /// and a details card for the chosen item with Equip (or Swap) / Unequip. Rules from <see cref="StashActions"/> (one of each
    /// unique worn); gear score and the tier walls shown at the top. Every change is saved through <see cref="GameFlow.Save"/>
    /// (<see cref="ProfileStore"/>). Built into the menu's screen node by <see cref="MainMenuUi"/>; buttons are named
    /// Slot-&lt;i&gt;, StashItem-&lt;i&gt;, EquipButton, UnequipButton, PrevPage, NextPage, BackButton, VendorButton.
    /// </summary>
    public static class StashScreen
    {
        public const int Columns = 4, Rows = 5, PerPage = Columns * Rows;

        /// <summary>Survives the screen being rebuilt after every change.</summary>
        public static Item Selected;
        public static int Page;
        public static string Note;

        public static RectTransform Build(RectTransform parent, GameFlow flow, Action back, Action rebuild, Action toVendor)
        {
            var panel = UiStyle.Panel(parent, "StashPanel");
            panel.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(1760f, 760f));
            var head = UiStyle.Label(panel, "Header", "STASH & EQUIPMENT", 40, UiStyle.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            head.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(1000f, 56f));
            UiStyle.Rule(panel, "HeaderRule", 520f, -88f);
            var p = flow.Profile; var ch = flow.Character;
            if (p == null || ch == null)
            {
                var none = UiStyle.Label(panel, "None", "Create a character first.", 28, UiStyle.TextDim, TextAnchor.MiddleCenter);
                none.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800f, 50f));
                Btn(panel, "BackButton", "Back", back, new Vector2(0f, 0f), new Vector2(40f, 32f), new Vector2(240f, 62f));
                return panel;
            }
            if (Selected != null && !p.Stash.Contains(Selected) && !ch.Equipment.IsWearing(Selected)) Selected = null;
            int pages = Mathf.Max(1, (p.Stash.Count + PerPage - 1) / PerPage);
            Page = Mathf.Clamp(Page, 0, pages - 1);

            int gs = GearScore.Of(ch.Equipment);
            var who = UiStyle.Label(panel, "Who", $"{UiStyle.Colour(ch.Name, UiStyle.TextMain)}  ·  {ch.Class.Name} ({ch.Class.Armour} armour)  ·  gear score {UiStyle.Colour(gs.ToString(), UiStyle.Gold)}", 24, UiStyle.TextDim, TextAnchor.MiddleLeft);
            who.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -100f), new Vector2(900f, 36f));
            var tier = UiStyle.Label(panel, "TierHint", StashActions.TierHint(gs), 19, UiStyle.TextDim, TextAnchor.MiddleRight);
            tier.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -100f), new Vector2(800f, 36f));

            // Paper doll.
            Section(panel, "DollTitle", "WORN", new Vector2(40f, -146f));
            var doll = StashActions.PaperDoll(ch);
            for (int i = 0; i < doll.Count; i++)
            {
                var (slot, item) = doll[i];
                int col = i % 3, row = i / 3;
                var it = item;
                string label = $"<size=15>{UiStyle.Colour(slot.ToUpperInvariant(), UiStyle.TextFaint)}</size>\n" + (it == null ? UiStyle.Colour("empty", UiStyle.TextFaint) : $"<size=18>{ItemText.Name(it)}</size>\n<size=14>{UiStyle.Colour($"iL {it.ItemLevel} · gs {GearScore.Of(it)}", UiStyle.TextDim)}</size>");
                var b = Cell(panel, "Slot-" + i, label, new Vector2(40f + col * 186f, -180f - row * 132f), new Vector2(176f, 122f), () => { Selected = it; Note = null; rebuild(); });
                b.interactable = it != null;
                b.SetSelected(it != null && ReferenceEquals(it, Selected));
                if (it != null) Stripe(b.transform, it.Rarity);
            }

            // Stash grid.
            Section(panel, "StashTitle", $"STASH  {UiStyle.Colour($"{p.Stash.Count} item{(p.Stash.Count == 1 ? "" : "s")}, shared by every character", UiStyle.TextFaint)}", new Vector2(620f, -146f));
            var onPage = p.Stash.Skip(Page * PerPage).Take(PerPage).ToList();
            for (int i = 0; i < PerPage; i++)
            {
                int col = i % Columns, row = i / Columns;
                var pos = new Vector2(620f + col * 152f, -180f - row * 92f);
                if (i >= onPage.Count)
                {
                    var empty = UiStyle.Img(panel, "Empty-" + i, new Color(0f, 0f, 0f, 0.25f), UiStyle.Rounded);
                    empty.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), pos, new Vector2(146f, 84f));
                    continue;
                }
                var it = onPage[i];
                string label = $"<size=16>{ItemText.Name(it)}</size>\n<size=13>{UiStyle.Colour($"{it.Base.Slot} · iL {it.ItemLevel}", UiStyle.TextDim)}</size>";
                var b = Cell(panel, "StashItem-" + (Page * PerPage + i), label, pos, new Vector2(146f, 84f), () => { Selected = it; Note = null; rebuild(); });
                b.SetSelected(ReferenceEquals(it, Selected));
                Stripe(b.transform, it.Rarity);
            }
            var pageText = UiStyle.Label(panel, "Page", $"Page {Page + 1} / {pages}", 20, UiStyle.TextDim, TextAnchor.MiddleCenter);
            pageText.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(620f + 301f, -662f), new Vector2(200f, 30f));
            var prev = Btn(panel, "PrevPage", "<", () => { Page--; rebuild(); }, new Vector2(0f, 1f), new Vector2(620f, -642f), new Vector2(80f, 44f), new Vector2(0f, 1f));
            prev.interactable = Page > 0;
            var next = Btn(panel, "NextPage", ">", () => { Page++; rebuild(); }, new Vector2(0f, 1f), new Vector2(620f + 522f, -642f), new Vector2(80f, 44f), new Vector2(0f, 1f));
            next.interactable = Page < pages - 1;

            // Details.
            var card = UiStyle.Panel(panel, "Details", UiStyle.PanelBgLight);
            card.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -146f), new Vector2(480f, 490f));
            var text = UiStyle.Label(card, "Text", "", 19, UiStyle.TextMain, TextAnchor.UpperLeft);
            text.rectTransform.Stretch(22f, 22f, 18f, 80f);
            if (Selected == null) text.text = UiStyle.Colour("Choose an item in the stash to see it and wear it, or a worn piece to take it off.\n\nThe tier walls count everything you wear and carry in.", UiStyle.TextDim);
            else
            {
                bool wearing = ch.Equipment.IsWearing(Selected);
                string after = wearing ? "" : $"\n{UiStyle.Colour($"Gear score after: {StashActions.GearScoreIfWorn(ch, Selected)}", UiStyle.TextDim)}";
                text.text = ItemText.Tooltip(Selected, ch.Equipment) + after;
                var sel = Selected;
                if (wearing)
                    Btn(card, "UnequipButton", "Unequip", () => Act(flow, StashActions.Unequip(p, ch, sel), rebuild), new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(260f, 56f), new Vector2(0.5f, 0f));
                else
                {
                    var off = EquipPreview.Displaced(ch.Equipment, sel);
                    var eq = Btn(card, "EquipButton", off == null ? "Equip" : "Swap", () => Act(flow, StashActions.Equip(p, ch, sel), rebuild), new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(260f, 56f), new Vector2(0.5f, 0f));
                    eq.interactable = EquipPreview.Refusal(ch.Equipment, sel) == null;
                }
            }
            var note = UiStyle.Label(panel, "Note", Note ?? "", 21, Note != null && Note.StartsWith("Saved") ? UiStyle.Good : UiStyle.Bad, TextAnchor.MiddleCenter);
            note.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -650f), new Vector2(480f, 30f));

            Btn(panel, "BackButton", "Back", () => { Selected = null; Note = null; back(); }, new Vector2(0f, 0f), new Vector2(40f, 28f), new Vector2(240f, 60f));
            Btn(panel, "VendorButton", "Vendor", () => { Selected = null; Note = null; toVendor(); }, new Vector2(1f, 0f), new Vector2(-40f, 28f), new Vector2(240f, 60f), new Vector2(1f, 0f));
            return panel;
        }

        private static void Act(GameFlow flow, string refusal, Action rebuild)
        {
            if (refusal != null) { Note = refusal; UiSound.Play(UiSound.Error); }
            else { flow.Save(); Note = "Saved."; UiSound.Play(UiSound.Confirm); }
            rebuild();
        }

        internal static void Section(RectTransform panel, string name, string text, Vector2 pos)
        {
            var t = UiStyle.Label(panel, name, text, 21, UiStyle.GoldDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            t.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), pos, new Vector2(560f, 30f));
        }

        internal static Button Cell(RectTransform parent, string name, string label, Vector2 pos, Vector2 size, Action onClick)
        {
            var b = UiStyle.MakeButton(parent, name, label, onClick, 16);
            ((RectTransform)b.transform).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), pos, size);
            var t = b.GetComponentInChildren<Text>();
            t.alignment = TextAnchor.MiddleLeft; t.rectTransform.Stretch(14f, 8f, 4f, 4f);
            return b;
        }

        internal static void Stripe(Transform button, Rarity r)
        {
            var s = UiStyle.Img(button, "Rarity", ItemText.Colour(r));
            s.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(4f, 0f));
            s.rectTransform.anchorMin = new Vector2(0f, 0.12f); s.rectTransform.anchorMax = new Vector2(0f, 0.88f); s.rectTransform.sizeDelta = new Vector2(4f, 0f);
        }

        internal static Button Btn(RectTransform parent, string name, string label, Action onClick, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            var b = UiStyle.MakeButton(parent, name, label, onClick, 24);
            ((RectTransform)b.transform).Place(anchor, pivot ?? anchor, pos, size);
            return b;
        }
    }
}
