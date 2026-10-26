using System;
using System.Linq;
using DungeonGen.Core;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Items;
using DungeonGen.Core.Profile;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonGen.Unity.UI
{
    /// <summary>
    /// The Stash &amp; Equipment screen of the main menu, laid out like an ARPG inventory over the whole screen. Left: the selected
    /// character's paper doll, a live render of its class model (<see cref="PaperDollPreview"/>) with the nine worn slots around it
    /// as icon tiles (Head on top; Chest, Hands, Legs, Feet down the left; Amulet, Weapon and the two Rings down the right), and the
    /// stats block under it (gear score and the tiers it admits, armour, damage, health, carry weight against the limit,
    /// resistances; all from the core: <see cref="GearScore"/>, <see cref="DungeonTier"/>, <see cref="Character.Sheet"/>,
    /// <see cref="CarryRules"/>). Middle: the shared stash as a paged grid of icon tiles with rarity borders
    /// (<see cref="ItemIconTile"/>). Right: the details card for the chosen item (hovering another tile previews it) with
    /// Equip (or Swap) / Unequip. Rules from <see cref="StashActions"/> and <see cref="EquipPreview"/> (one of each unique worn).
    /// Every change is saved through <see cref="GameFlow.Save"/> (<see cref="ProfileStore"/>). Built into the menu's screen node by
    /// <see cref="MainMenuUi"/>; buttons are named Slot-&lt;i&gt; (i = <see cref="StashActions.PaperDoll"/> order), StashItem-&lt;i&gt;
    /// (i = stash index), EquipButton, UnequipButton, PrevPage, NextPage, BackButton, VendorButton. The stats block is "Who" and
    /// reads "gear score N"; the card is "Details".
    /// </summary>
    public static class StashScreen
    {
        public const int Columns = 5, Rows = 6, PerPage = Columns * Rows;
        /// <summary>Icon tile size in reference pixels (1920x1080).</summary>
        public const float Tile = 96f, Gap = 15f;

        /// <summary>Survives the screen being rebuilt after every change.</summary>
        public static Item Selected;
        public static int Page;
        public static string Note;

        // Layout, in panel pixels from the top-left corner.
        private const float PanelW = 1840f, PanelH = 1000f;
        private const float DollX = 40f, DollW = 660f, StashX = 740f, CardX = 1310f, CardW = 490f, TopY = 100f, BodyBottom = 900f;

        /// <summary>Where each <see cref="StashActions.PaperDoll"/> slot sits around the body (top-left corner of its tile).</summary>
        private static Vector2 SlotPos(int i)
        {
            float left = DollX, right = DollX + DollW - Tile, row0 = 252f, step = Tile + 16f;
            switch (i)
            {
                case 1: return new Vector2(DollX + (DollW - Tile) / 2f, 140f);   // Head, over the model
                case 2: return new Vector2(left, row0);                         // Chest
                case 3: return new Vector2(left, row0 + step);                  // Hands
                case 4: return new Vector2(left, row0 + 2 * step);              // Legs
                case 5: return new Vector2(left, row0 + 3 * step);              // Feet
                case 6: return new Vector2(right, row0);                        // Amulet, at the neck
                case 0: return new Vector2(right, row0 + step);                 // Weapon
                case 7: return new Vector2(right, row0 + 2 * step);             // Ring
                default: return new Vector2(right, row0 + 3 * step);            // Ring
            }
        }

        public static RectTransform Build(RectTransform parent, GameFlow flow, Action back, Action rebuild, Action toVendor)
        {
            // A full-screen inventory: cover the menu title with the menu's own backdrop, glow and vignette.
            var shade = UiStyle.Img(parent, "StashBackdrop", UiStyle.Backdrop); shade.rectTransform.Stretch(); shade.raycastTarget = true;
            var warm = UiStyle.Img(parent, "StashGlow", new Color(0.62f, 0.36f, 0.12f, 0.16f), UiStyle.Glow);
            warm.rectTransform.Place(new Vector2(0.22f, 0.55f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500f, 1300f));
            var vig = UiStyle.Img(parent, "StashVignette", Color.white, UiStyle.Vignette); vig.rectTransform.Stretch(-200f, -200f, -200f, -200f);

            var panel = UiStyle.Panel(parent, "StashPanel");
            panel.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PanelW, PanelH));
            var head = UiStyle.Label(panel, "Header", "STASH & EQUIPMENT", 40, UiStyle.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            head.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(1000f, 56f));
            UiStyle.Rule(panel, "HeaderRule", 520f, -84f);
            var p = flow.Profile; var ch = flow.Character;
            if (p == null || ch == null)
            {
                var none = UiStyle.Label(panel, "None", "Create a character first.", 28, UiStyle.TextDim, TextAnchor.MiddleCenter);
                none.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800f, 50f));
                Btn(panel, "BackButton", "Back", back, new Vector2(0f, 0f), new Vector2(40f, 28f), new Vector2(240f, 60f));
                return panel;
            }
            if (Selected != null && !p.Stash.Contains(Selected) && !ch.Equipment.IsWearing(Selected)) Selected = null;
            int pages = Mathf.Max(1, (p.Stash.Count + PerPage - 1) / PerPage);
            Page = Mathf.Clamp(Page, 0, pages - 1);

            var card = new DetailsCard();
            BuildDoll(panel, ch, card, rebuild);
            BuildStats(panel, ch);
            BuildStash(panel, p, pages, card, rebuild);
            card.Build(panel, flow, p, ch, rebuild);
            // One highlight: the gamepad / keyboard focus starts on the chosen tile, not on the first button of the screen.
            var chosen = Selected == null ? null : FindTile(panel, Selected, p, ch);
            if (chosen != null) panel.gameObject.AddComponent<PaperDollFocus>().Target = chosen.gameObject;

            var note = UiStyle.Label(panel, "Note", Note ?? "", 21, Note != null && Note.StartsWith("Saved") ? UiStyle.Good : UiStyle.Bad, TextAnchor.MiddleCenter);
            note.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 42f), new Vector2(1100f, 32f));

            Btn(panel, "BackButton", "Back", () => { Selected = null; Note = null; back(); }, new Vector2(0f, 0f), new Vector2(40f, 28f), new Vector2(240f, 60f));
            Btn(panel, "VendorButton", "Vendor", () => { Selected = null; Note = null; toVendor(); }, new Vector2(1f, 0f), new Vector2(-40f, 28f), new Vector2(240f, 60f), new Vector2(1f, 0f));
            return panel;
        }

        // ---------------- paper doll ----------------

        private static void BuildDoll(RectTransform panel, Character ch, DetailsCard card, Action rebuild)
        {
            var name = UiStyle.Label(panel, "CharacterName", $"<size=30><b>{ch.Name}</b></size>   {UiStyle.Colour($"{ch.Class.Name}  ·  {ch.Class.Armour} armour", UiStyle.TextDim)}", 20, UiStyle.TextMain, TextAnchor.MiddleLeft);
            At(name.rectTransform, DollX, TopY - 4f, DollW, 40f);
            name.horizontalOverflow = HorizontalWrapMode.Overflow;

            // The stage: a dark well, warm light pooled at the feet, the live model, a vignette.
            const float sx = DollX + Tile + 14f, sw = DollW - 2f * (Tile + 14f), sy = 150f, sh = 556f;
            var well = UiStyle.Img(panel, "Stage", new Color(0.02f, 0.018f, 0.022f, 0.85f), UiStyle.Rounded);
            At(well.rectTransform, sx, sy, sw, sh);
            var halo = UiStyle.Img(well.transform, "Halo", new Color(0.55f, 0.36f, 0.16f, 0.22f), UiStyle.Glow);
            halo.rectTransform.Place(new Vector2(0.5f, 0.42f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(sw * 1.05f, sh * 0.95f));
            var floor = UiStyle.Img(well.transform, "Floor", new Color(0.95f, 0.62f, 0.26f, 0.30f), UiStyle.Glow);
            floor.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 34f), new Vector2(sw * 0.8f, 70f));
            var raw = UiStyle.Node(well.transform, "Model").gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            raw.rectTransform.Stretch(0f, 0f, 96f, 0f);   // below the head tile
            if (!PaperDollPreview.Show(raw, ch.Class.Name, new Vector2(sw, sh - 96f)))
            {
                var letter = UiStyle.Label(well.transform, "ClassLetter", ch.Class.Name.Substring(0, 1), 240, new Color(UiStyle.GoldDim.r, UiStyle.GoldDim.g, UiStyle.GoldDim.b, 0.35f), TextAnchor.MiddleCenter, FontStyle.Bold, false);
                letter.rectTransform.Stretch(0f, 0f, 96f, 0f);
            }
            var vig = UiStyle.Img(well.transform, "StageVignette", new Color(1f, 1f, 1f, 0.85f), UiStyle.Vignette);
            vig.rectTransform.Stretch();
            var rim = UiStyle.Img(well.transform, "StageFrame", new Color(UiStyle.GoldDim.r, UiStyle.GoldDim.g, UiStyle.GoldDim.b, 0.35f), UiStyle.Frame);
            rim.rectTransform.Stretch();

            var doll = StashActions.PaperDoll(ch);
            for (int i = 0; i < doll.Count; i++)
            {
                var (slot, item) = doll[i];
                var it = item;
                var b = ItemIconTile.Tile(panel, "Slot-" + i, it, Tile, () => { Selected = it; Note = null; rebuild(); }, slot);
                At((RectTransform)b.transform, SlotPos(i).x, SlotPos(i).y, Tile, Tile);
                b.interactable = it != null;
                if (it != null) { Mark(b, ReferenceEquals(it, Selected)); card.Hover(b, it); }
            }
        }

        // ---------------- stats ----------------

        private static void BuildStats(RectTransform panel, Character ch)
        {
            const float top = 716f;
            var who = UiStyle.Panel(panel, "Who", UiStyle.PanelBgLight);
            At(who, DollX, top, DollW, BodyBottom - top);
            var sheet = ch.Sheet();
            int gs = GearScore.Of(ch.Equipment);

            // Row 1: the gear score, big, and the tiers it admits.
            var gsl = UiStyle.Label(who, "GearScore", $"{UiStyle.Colour("gear score", UiStyle.TextDim)} <size=54><b>{UiStyle.Colour(gs.ToString(), UiStyle.Gold)}</b></size>", 22, UiStyle.TextMain, TextAnchor.LowerLeft);
            gsl.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, -2f), new Vector2(300f, 68f));
            gsl.horizontalOverflow = HorizontalWrapMode.Overflow;
            var open = DungeonTier.Open(gs).Select(t => t.Name).ToList();
            var admits = UiStyle.Label(who, "TierHint", open.Count == 0 ? UiStyle.Colour("Fits no tier", UiStyle.Bad) : $"{UiStyle.Colour("Admits", UiStyle.TextDim)}  <b>{UiStyle.Colour(string.Join(" and ", open), UiStyle.Good)}</b>", 22, UiStyle.TextMain, TextAnchor.MiddleRight);
            admits.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-22f, -12f), new Vector2(330f, 28f));
            string bands = string.Join("  ·  ", DungeonTier.All.Select(t => t.MaxGearScore == int.MaxValue ? $"{RomanOnly(t.Name)} {t.MinGearScore}+" : $"{RomanOnly(t.Name)} {t.MinGearScore}–{t.MaxGearScore}"));
            var bl = UiStyle.Label(who, "TierBands", UiStyle.Colour("Tiers  " + bands, UiStyle.TextFaint), 18, UiStyle.TextMain, TextAnchor.MiddleRight);
            bl.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-22f, -42f), new Vector2(360f, 26f));
            var rule = UiStyle.Img(who, "Rule", new Color(UiStyle.GoldDim.r, UiStyle.GoldDim.g, UiStyle.GoldDim.b, 0.5f));
            rule.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -76f), new Vector2(DollW - 44f, 1.5f));

            // Row 2: the sheet, four columns.
            float load = ch.Equipment.WornWeightKg, cap = sheet.CarryKg;
            bool over = CarryRules.IsOverloaded(load, cap);
            var cols = new (string k, string v)[]
            {
                ("Armour", $"{sheet.ArmourRating:0}"),
                ("Damage", $"×{sheet.DamageMultiplier:0.00}"),
                ("Health", $"{sheet.Health:0}"),
                ("Carry", UiStyle.Colour($"{load:0.#}", over ? UiStyle.Bad : UiStyle.TextMain) + UiStyle.Colour($" / {cap:0} kg", UiStyle.TextDim)),
            };
            float cw = (DollW - 44f) / cols.Length;
            for (int i = 0; i < cols.Length; i++)
            {
                var l = UiStyle.Label(who, "Stat-" + cols[i].k, $"{UiStyle.Colour(cols[i].k.ToUpperInvariant(), UiStyle.GoldDim)}\n<size=24><b>{cols[i].v}</b></size>", 18, UiStyle.TextMain, TextAnchor.UpperLeft);
                l.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f + i * cw, -86f), new Vector2(cw + (i == cols.Length - 1 ? 30f : 0f), 56f));
                l.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
            var bar = UiStyle.Img(who, "CarryBar", new Color(0f, 0f, 0f, 0.6f), UiStyle.Rounded);
            bar.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f + 3 * cw, -144f), new Vector2(cw - 6f, 6f));
            float frac = Mathf.Clamp01(load / Mathf.Max(1f, cap));
            var fill = UiStyle.Img(bar.transform, "Fill", over ? UiStyle.Bad : frac > 0.85f ? UiStyle.Channel : UiStyle.Good, UiStyle.Rounded);
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(frac, 1f);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;

            // Row 3: resistances by element.
            var res = new[] { DamageType.Fire, DamageType.Frost, DamageType.Lightning, DamageType.Poison, DamageType.Shadow };
            var rl = UiStyle.Label(who, "Resistances", UiStyle.Colour("RESIST", UiStyle.GoldDim) + "   " + string.Join("   ", res.Select(t => UiStyle.Colour(t.ToString(), DamageColour(t)) + " " + $"<b>{sheet.Resistances.Rating(t):0}</b>")), 18, UiStyle.TextMain, TextAnchor.MiddleLeft);
            rl.rectTransform.Place(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(22f, 8f), new Vector2(DollW - 44f, 28f));
            rl.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        /// <summary>"Tier III" → "III".</summary>
        private static string RomanOnly(string tierName) => tierName.StartsWith("Tier ") ? tierName.Substring(5) : tierName;

        private static Color DamageColour(DamageType t) => t switch
        {
            DamageType.Fire => new Color(1f, 0.55f, 0.25f),
            DamageType.Frost => new Color(0.55f, 0.85f, 1f),
            DamageType.Lightning => new Color(0.78f, 0.68f, 1f),
            DamageType.Poison => new Color(0.55f, 0.92f, 0.4f),
            DamageType.Shadow => new Color(0.75f, 0.5f, 1f),
            _ => UiStyle.TextDim,
        };

        // ---------------- stash ----------------

        private static void BuildStash(RectTransform panel, PlayerProfile p, int pages, DetailsCard card, Action rebuild)
        {
            float gridW = Columns * Tile + (Columns - 1) * Gap;
            var title = UiStyle.Label(panel, "StashTitle", $"STASH  {UiStyle.Colour($"{p.Stash.Count} item{(p.Stash.Count == 1 ? "" : "s")}, shared by every character", UiStyle.TextFaint)}", 21, UiStyle.GoldDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            At(title.rectTransform, StashX, TopY, gridW, 36f);
            float gy = 150f;
            var onPage = p.Stash.Skip(Page * PerPage).Take(PerPage).ToList();
            for (int i = 0; i < PerPage; i++)
            {
                int col = i % Columns, row = i / Columns;
                float x = StashX + col * (Tile + Gap), y = gy + row * (Tile + Gap);
                if (i >= onPage.Count)
                {
                    var empty = UiStyle.Img(panel, "Empty-" + i, ItemIconTile.WellEmpty, UiStyle.Rounded);
                    At(empty.rectTransform, x, y, Tile, Tile);
                    var eg = UiStyle.Img(empty.transform, "Glow", new Color(0f, 0f, 0f, 0.6f), ItemIconTile.InnerGlow); eg.type = Image.Type.Sliced; eg.rectTransform.Stretch();
                    var ef = UiStyle.Img(empty.transform, "Frame", new Color(UiStyle.GoldDim.r, UiStyle.GoldDim.g, UiStyle.GoldDim.b, 0.22f), UiStyle.Frame); ef.rectTransform.Stretch();
                    continue;
                }
                var it = onPage[i];
                var b = ItemIconTile.Tile(panel, "StashItem-" + (Page * PerPage + i), it, Tile, () => { Selected = it; Note = null; rebuild(); });
                At((RectTransform)b.transform, x, y, Tile, Tile);
                Mark(b, ReferenceEquals(it, Selected));
                card.Hover(b, it);
            }
            float py = gy + Rows * (Tile + Gap) + 2f;
            var pageText = UiStyle.Label(panel, "Page", $"Page {Page + 1} / {pages}", 20, UiStyle.TextDim, TextAnchor.MiddleCenter);
            pageText.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(StashX + gridW / 2f, -py), new Vector2(240f, 44f));
            var prev = Btn(panel, "PrevPage", "<", () => { Page--; rebuild(); }, new Vector2(0f, 1f), new Vector2(StashX, -py), new Vector2(80f, 44f), new Vector2(0f, 1f));
            prev.interactable = Page > 0;
            var next = Btn(panel, "NextPage", ">", () => { Page++; rebuild(); }, new Vector2(0f, 1f), new Vector2(StashX + gridW - 80f, -py), new Vector2(80f, 44f), new Vector2(0f, 1f));
            next.interactable = Page < pages - 1;
        }

        /// <summary>The chosen tile: a gold ring and a lifted well.</summary>
        private static void Mark(Button b, bool selected)
        {
            if (!selected) return;
            var ring = UiStyle.Img(b.transform, "SelectedRing", UiStyle.Gold, ItemIconTile.Border);
            ring.type = Image.Type.Sliced; ring.rectTransform.Stretch(-4f, -4f, -4f, -4f);
            var glow = UiStyle.Img(b.transform, "SelectedGlow", new Color(UiStyle.Gold.r, UiStyle.Gold.g, UiStyle.Gold.b, 0.30f), ItemIconTile.InnerGlow);
            glow.type = Image.Type.Sliced; glow.rectTransform.Stretch();
            var focus = b.transform.Find("Focus"); if (focus != null) focus.SetAsLastSibling();
        }

        // ---------------- details ----------------

        /// <summary>The card on the right: the chosen item with its action, or (while the pointer is over another tile) a preview of that one.</summary>
        private sealed class DetailsCard
        {
            private RectTransform _card, _content;
            private GameObject _action;
            private Character _ch;
            private bool _previewing;

            public void Build(RectTransform panel, GameFlow flow, PlayerProfile p, Character ch, Action rebuild)
            {
                _ch = ch;
                _card = UiStyle.Panel(panel, "Details", UiStyle.PanelBgLight);
                At(_card, CardX, TopY, CardW, BodyBottom - TopY);
                _content = UiStyle.Node(_card, "Content"); _content.Stretch();
                if (Selected != null)
                {
                    var sel = Selected;
                    if (ch.Equipment.IsWearing(sel))
                        _action = Btn(_card, "UnequipButton", "Unequip", () => Act(flow, StashActions.Unequip(p, ch, sel), rebuild), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(280f, 58f), new Vector2(0.5f, 0f)).gameObject;
                    else
                    {
                        var off = EquipPreview.Displaced(ch.Equipment, sel);
                        var eq = Btn(_card, "EquipButton", off == null ? "Equip" : "Swap", () => Act(flow, StashActions.Equip(p, ch, sel), rebuild), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(280f, 58f), new Vector2(0.5f, 0f));
                        eq.interactable = EquipPreview.Refusal(ch.Equipment, sel) == null;
                        _action = eq.gameObject;
                    }
                }
                Show(Selected);
            }

            public void Hover(Button b, Item it)
            {
                var h = b.gameObject.AddComponent<ItemHover>();
                h.Enter = () => { if (!ReferenceEquals(it, Selected)) { _previewing = true; Show(it); } };
                h.Exit = () => { if (_previewing) { _previewing = false; Show(Selected); } };
            }

            private void Show(Item item)
            {
                if (_content == null) return;   // the screen is going away
                for (int i = _content.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_content.GetChild(i).gameObject);
                bool preview = item != null && !ReferenceEquals(item, Selected);
                if (_action != null) _action.SetActive(!preview);
                if (item == null)
                {
                    var t = UiStyle.Label(_content, "Text", UiStyle.Colour("Choose an item in the stash to see it and wear it, or a worn piece to take it off.\n\nThe tier walls count everything you wear and carry in.\n\nSell or salvage what you do not need at the Vendor.", UiStyle.TextDim), 21, UiStyle.TextMain, TextAnchor.UpperLeft);
                    t.rectTransform.Stretch(28f, 28f, 28f, 100f);
                    Fit(LayoutUtility.GetPreferredHeight(t.rectTransform) + 56f);
                    return;
                }
                var icon = ItemIconTile.Icon(_content, "Icon", item, 124f, true);
                icon.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(124f, 124f));
                var head = UiStyle.Label(_content, "Head", $"<size=27><b>{ItemText.Name(item)}</b></size>\n{UiStyle.Colour(ItemText.Line(item), UiStyle.TextDim)}", 20, UiStyle.TextMain, TextAnchor.MiddleLeft);
                head.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(166f, -20f), new Vector2(CardW - 190f, 132f));
                head.lineSpacing = 1.05f;
                var rule = UiStyle.Img(_content, "Rule", new Color(UiStyle.GoldDim.r, UiStyle.GoldDim.g, UiStyle.GoldDim.b, 0.6f));
                rule.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -166f), new Vector2(CardW - 48f, 2f));
                // The tooltip without its first two lines (name and base line, shown in the header).
                var lines = ItemText.Tooltip(item, _ch.Equipment).Split('\n');
                string body = lines.Length > 2 && lines[0].Contains(item.Name) ? string.Join("\n", lines.Skip(2)) : string.Join("\n", lines);
                bool wearing = _ch.Equipment.IsWearing(item);
                if (!wearing) body += $"\n{UiStyle.Colour($"Gear score after: {StashActions.GearScoreIfWorn(_ch, item)}", UiStyle.TextDim)}";
                if (preview) body += "\n\n" + UiStyle.Colour("Click to choose it.", UiStyle.TextFaint);
                var text = UiStyle.Label(_content, "Text", body, 20, UiStyle.TextMain, TextAnchor.UpperLeft);
                text.rectTransform.Stretch(28f, 26f, 184f, 96f);
                text.lineSpacing = 1.06f;
                // The card ends under its text: the action button follows the last line, no dead space above it.
                Fit(184f + LayoutUtility.GetPreferredHeight(text.rectTransform) + 30f + (preview || _action == null ? 24f : 104f));   // 30: a line of slack for wrapping at other scales
            }

            private void Fit(float height)
            {
                if (_card == null) return;
                _card.sizeDelta = new Vector2(CardW, Mathf.Clamp(height, 300f, BodyBottom - TopY));
            }
        }

        /// <summary>The tile showing <paramref name="item"/>: its paper-doll slot or its stash cell on this page.</summary>
        private static Button FindTile(RectTransform panel, Item item, PlayerProfile p, Character ch)
        {
            string name = null;
            var doll = StashActions.PaperDoll(ch);
            for (int i = 0; i < doll.Count; i++) if (ReferenceEquals(doll[i].item, item)) name = "Slot-" + i;
            if (name == null) { int k = p.Stash.IndexOf(item); if (k >= 0) name = "StashItem-" + k; }
            var t = name == null ? null : panel.Find(name);
            return t != null ? t.GetComponent<Button>() : null;
        }

        private static void Act(GameFlow flow, string refusal, Action rebuild)
        {
            if (refusal != null) { Note = refusal; UiSound.Play(UiSound.Error); }
            else { flow.Save(); Note = "Saved."; UiSound.Play(UiSound.Confirm); }
            rebuild();
        }

        private static void At(RectTransform rt, float x, float y, float w, float h) =>
            rt.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(w, h));

        // ---------------- shared helpers (the vendor and crafting screens use these) ----------------

        internal static void Section(RectTransform panel, string name, string text, Vector2 pos)
        {
            var t = UiStyle.Label(panel, name, text, 21, UiStyle.GoldDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            t.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), pos, new Vector2(560f, 30f));
        }

        internal static Button Cell(RectTransform parent, string name, string label, Vector2 pos, Vector2 size, Action onClick)
        {
            var b = UiStyle.MakeButton(parent, name, label, onClick, 18);
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
