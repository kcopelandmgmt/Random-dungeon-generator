using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DungeonGen.Core;
using DungeonGen.Core.Items;
using DungeonGen.Core.Profile;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonGen.Unity.UI
{
    /// <summary>
    /// The Crafting Bench of the main menu (2026-10-22 forge rules): the account's materials from salvaging, a recipe builder
    /// (slot, base, item level, up to two pinned affix families, optionally forced Rare) with the price and a preview of what comes
    /// out, and the craft button. Crafting only ever makes a new item (<see cref="Crafter"/>): it lands in the stash, to be worn
    /// from Stash &amp; Equipment, and anything unwanted goes back through the Vendor's salvage. Rules and prices come from
    /// <see cref="CraftingActions"/> and <see cref="CraftingRules"/>; every craft is saved through <see cref="GameFlow.Save"/>.
    /// Registered with the menu from <see cref="Register"/>; buttons are named CraftSlot-&lt;slot&gt;, CraftBase-&lt;base&gt;,
    /// CraftLevel-&lt;n&gt;, CraftForceRare, CraftPin-&lt;family&gt;, CraftButton, VendorButton, StashButton, BackButton.
    /// </summary>
    public static class CraftingScreen
    {
        public const string ScreenName = "Crafting";

        /// <summary>What the bench is set to; survives the screen being rebuilt after every click.</summary>
        public static ItemSlot Slot = ItemSlot.Weapon;
        public static string BaseName;
        public static int Level = 1;
        public static readonly List<string> Pins = new List<string>();
        public static bool ForceRare;
        public static string Note;
        /// <summary>The newest crafted item, shown until the bench is left.</summary>
        public static Item Result;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            MainMenuUi.Register(new MainMenuUi.ExtraScreen { Name = ScreenName, Label = "Crafting Bench", Order = 10, NeedsCharacter = true, Build = Build });
        }

        /// <summary>Tests and tools: back to the defaults.</summary>
        public static void Reset()
        {
            Slot = ItemSlot.Weapon; BaseName = null; Level = 1; Pins.Clear(); ForceRare = false; Note = null; Result = null;
        }

        public static void Build(RectTransform screen, GameFlow flow, Action back, Action<string> show)
        {
            Action rebuild = () => show(ScreenName);
            var panel = UiStyle.Panel(screen, "CraftingPanel");
            panel.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(1760f, 760f));
            var head = UiStyle.Label(panel, "Header", "CRAFTING BENCH", 40, UiStyle.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            head.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(1000f, 56f));
            UiStyle.Rule(panel, "HeaderRule", 520f, -88f);
            var p = flow.Profile;
            if (p == null)
            {
                var none = UiStyle.Label(panel, "None", "Create a character first.", 28, UiStyle.TextDim, TextAnchor.MiddleCenter);
                none.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800f, 50f));
                StashScreen.Btn(panel, "BackButton", "Back", back, new Vector2(0f, 0f), new Vector2(40f, 28f), new Vector2(240f, 60f));
                return;
            }

            // Keep the settings legal for what the account holds now (a craft can use up the last of an essence).
            var bases = CraftingActions.Bases(Slot);
            var baseItem = bases.FirstOrDefault(b => b.Name == BaseName) ?? bases[0];
            BaseName = baseItem.Name;
            Level = Mathf.Clamp(Level, CraftingActions.MinLevel, CraftingActions.MaxLevel);
            var pinnable = CraftingActions.PinnableFamilies(p, baseItem);
            Pins.RemoveAll(f => !pinnable.Contains(f));
            while (Pins.Count > CraftingRules.MaxGuaranteedFamilies) Pins.RemoveAt(Pins.Count - 1);
            if (ForceRare && p.Materials.PrimeShards < CraftingRules.ShardsForRare) ForceRare = false;
            var req = new CraftRequest(baseItem, Level, Pins, ForceRare);

            var purse = UiStyle.Label(panel, "Purse", $"Silver {UiStyle.Colour(p.Silver.ToString("N0"), UiStyle.Gold)}      Scrap {p.Materials.Scrap:N0}      Prime shards {p.Materials.PrimeShards}      Essences {p.Materials.Essences.Values.Sum()}", 24, UiStyle.TextMain, TextAnchor.MiddleCenter);
            purse.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -104f), new Vector2(1400f, 36f));

            BuildMaterials(panel, p);
            BuildLastCrafted(panel);

            // ---------- the recipe ----------
            const float x0 = 420f, width = 760f;
            Section(panel, "SlotTitle", "WHAT TO FORGE", new Vector2(x0, -140f), width);
            for (int i = 0; i < CraftingActions.Slots.Count; i++)
            {
                var slot = CraftingActions.Slots[i];
                var b = StashScreen.Btn(panel, "CraftSlot-" + slot, slot.ToString(), () => { Slot = slot; BaseName = null; Note = null; rebuild(); },
                    new Vector2(0f, 1f), new Vector2(x0 + i * 96.4f, -172f), new Vector2(90f, 44f), new Vector2(0f, 1f));
                var lbl = b.GetComponentInChildren<Text>(); lbl.fontSize = 18; lbl.rectTransform.Stretch(2f, 2f, 2f, 2f); lbl.horizontalOverflow = HorizontalWrapMode.Overflow;
                b.SetSelected(slot == Slot);
            }
            const int perRow = 6;
            float y = -226f;
            for (int i = 0; i < bases.Count; i++)
            {
                var bs = bases[i];
                float bw = (width - (perRow - 1) * 8f) / perRow;
                string sub = bs.IsWeapon ? bs.ScalesWith.ToString() : bs.IsArmour ? bs.Weight.ToString() : "no base stats";
                var b = UiStyle.MakeButton(panel, "CraftBase-" + bs.Name, $"{bs.Name}\n<size=15>{UiStyle.Colour(sub, UiStyle.TextDim)}</size>", () => { BaseName = bs.Name; Note = null; rebuild(); }, 18);
                ((RectTransform)b.transform).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x0 + (i % perRow) * (bw + 8f), y - (i / perRow) * 58f), new Vector2(bw, 52f));
                b.SetSelected(bs.Name == BaseName);
            }
            y -= ((bases.Count + perRow - 1) / perRow) * 58f + 10f;

            Section(panel, "LevelTitle", "ITEM LEVEL  " + UiStyle.Colour("<size=16>higher costs more scrap and silver and rolls stronger affixes</size>", UiStyle.TextFaint), new Vector2(x0, y), width);
            y -= 34f;
            var recipes = CraftingActions.Recipes(p, baseItem);
            for (int i = 0; i < recipes.Count; i++)
            {
                var r = recipes[i];
                float bw = (width - 4 * 6f) / 5f;
                var cost = UiStyle.Colour($"{r.Scrap} scrap\n{r.SilverFee} silver", r.Affordable ? UiStyle.Good : UiStyle.Bad);
                var b = UiStyle.MakeButton(panel, "CraftLevel-" + r.Level, $"<b>Level {r.Level}</b>\n<size=16>{cost}</size>", () => { Level = r.Level; Note = null; rebuild(); }, 23);
                ((RectTransform)b.transform).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x0 + i * (bw + 6f), y), new Vector2(bw, 88f));
                b.SetSelected(r.Level == Level);
            }
            y -= 88f + 14f;

            Section(panel, "PinTitle", "GUARANTEES  " + UiStyle.Colour($"<size=16>up to {CraftingRules.MaxGuaranteedFamilies} affix families ({CraftingRules.EssencesToGuarantee} essences each), or force Rare ({CraftingRules.ShardsForRare} prime shards)</size>", UiStyle.TextFaint), new Vector2(x0, y), width);
            y -= 34f;
            float cw = (width - 3 * 8f) / 4f;
            float pinY = y;
            Vector2 CellPos(int cell) => new Vector2(x0 + (cell % 4) * (cw + 8f), pinY - (cell / 4) * 52f);
            var rare = UiStyle.MakeButton(panel, "CraftForceRare", $"Force Rare\n<size=14>{UiStyle.Colour($"{CraftingRules.ShardsForRare} shards · have {p.Materials.PrimeShards}", UiStyle.TextDim)}</size>", () => { ForceRare = !ForceRare; Note = null; rebuild(); }, 18);
            ((RectTransform)rare.transform).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), CellPos(0), new Vector2(cw, 46f));
            rare.SetSelected(ForceRare);
            rare.interactable = ForceRare || p.Materials.PrimeShards >= CraftingRules.ShardsForRare;
            const int maxPins = 11;
            for (int i = 0; i < Math.Min(maxPins, pinnable.Count); i++)
            {
                string fam = pinnable[i];
                var b = UiStyle.MakeButton(panel, "CraftPin-" + fam, $"{FamilyName(fam)}\n<size=14>{UiStyle.Colour($"have {p.Materials.Essence(fam)}", UiStyle.TextDim)}</size>", () =>
                {
                    if (Pins.Contains(fam)) Pins.Remove(fam);
                    else if (Pins.Count >= CraftingRules.MaxGuaranteedFamilies) { Note = $"At most {CraftingRules.MaxGuaranteedFamilies} families can be guaranteed."; UiSound.Play(UiSound.Error); rebuild(); return; }
                    else Pins.Add(fam);
                    Note = null; rebuild();
                }, 18);
                ((RectTransform)b.transform).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), CellPos(i + 1), new Vector2(cw, 46f));
                b.SetSelected(Pins.Contains(fam));
            }
            if (pinnable.Count == 0)
            {
                var hint = UiStyle.Label(panel, "PinHint", $"No essence to pin yet. Salvage gear at the Vendor: every affix on it yields essence of its family, and {CraftingRules.EssencesToGuarantee} of one family guarantees it on a {baseItem.Slot}.", 17, UiStyle.TextFaint, TextAnchor.UpperLeft);
                hint.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(CellPos(1).x, pinY - 2f), new Vector2(width - cw - 8f, 80f));
            }

            BuildPreview(panel, flow, p, req, baseItem, rebuild);

            var note = UiStyle.Label(panel, "Note", Note ?? "", 21, Note != null && Note.StartsWith("Crafted") ? UiStyle.Good : UiStyle.Bad, TextAnchor.MiddleCenter);
            note.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 44f), new Vector2(900f, 30f));
            StashScreen.Btn(panel, "BackButton", "Back", () => { Note = null; Result = null; back(); }, new Vector2(0f, 0f), new Vector2(40f, 28f), new Vector2(240f, 60f));
            StashScreen.Btn(panel, "VendorButton", "Vendor", () => { Note = null; Result = null; show(MainMenuUi.VendorScreenName); }, new Vector2(1f, 0f), new Vector2(-360f, 28f), new Vector2(240f, 60f), new Vector2(1f, 0f));
            StashScreen.Btn(panel, "StashButton", "Stash", () => { Note = null; Result = null; show(MainMenuUi.StashScreenName); }, new Vector2(1f, 0f), new Vector2(-40f, 28f), new Vector2(300f, 60f), new Vector2(1f, 0f));
        }

        // ---------------- pieces ----------------

        private static void Section(RectTransform panel, string name, string text, Vector2 pos, float width)
        {
            var t = UiStyle.Label(panel, name, text, 21, UiStyle.GoldDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            t.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), pos, new Vector2(width, 30f));
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        private static void BuildMaterials(RectTransform panel, PlayerProfile p)
        {
            StashScreen.Section(panel, "MaterialsTitle", "MATERIALS  " + UiStyle.Colour("from salvaging", UiStyle.TextFaint), new Vector2(40f, -140f));
            var card = UiStyle.Panel(panel, "Materials", UiStyle.PanelBgLight);
            card.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -172f), new Vector2(360f, 254f));
            var m = p.Materials;
            var top = UiStyle.Label(card, "Totals", $"Scrap  {UiStyle.Colour(m.Scrap.ToString("N0"), UiStyle.Gold)}\nPrime shards  {UiStyle.Colour(m.PrimeShards.ToString(), UiStyle.Gold)}\n{UiStyle.Colour("ESSENCES", UiStyle.GoldDim)}  {UiStyle.Colour($"<size=15>{CraftingRules.EssencesToGuarantee} pins a family</size>", UiStyle.TextFaint)}", 20, UiStyle.TextMain, TextAnchor.UpperLeft);
            top.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -10f), new Vector2(330f, 92f));
            top.lineSpacing = 1.1f;
            var held = m.Essences.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).ToList();
            if (held.Count == 0)
            {
                var none = UiStyle.Label(card, "NoEssence", "None yet. Salvaging an item with affixes gives essence of each family on it.", 16, UiStyle.TextFaint, TextAnchor.UpperLeft);
                none.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -100f), new Vector2(330f, 80f));
                return;
            }
            const int rows = 6;
            for (int col = 0; col < 2; col++)
            {
                var sb = new StringBuilder();
                foreach (var kv in held.Skip(col * rows).Take(rows))
                {
                    var c = kv.Value >= CraftingRules.EssencesToGuarantee ? UiStyle.Good : UiStyle.TextDim;
                    sb.Append(UiStyle.Colour($"<b>{kv.Value}</b>  {FamilyName(kv.Key)}", c)).Append('\n');
                }
                var t = UiStyle.Label(card, "Essences-" + col, sb.ToString(), 17, UiStyle.TextDim, TextAnchor.UpperLeft);
                t.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f + col * 172f, -100f), new Vector2(168f, 150f));
                t.lineSpacing = 1.1f;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
            if (held.Count > rows * 2)
            {
                var more = UiStyle.Label(card, "EssenceMore", $"+{held.Count - rows * 2} more families", 15, UiStyle.TextFaint, TextAnchor.LowerRight);
                more.rectTransform.Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-14f, 8f), new Vector2(200f, 22f));
            }
        }

        private static void BuildLastCrafted(RectTransform panel)
        {
            StashScreen.Section(panel, "LastTitle", "JUST CRAFTED", new Vector2(40f, -436f));
            var card = UiStyle.Panel(panel, "LastCrafted", UiStyle.PanelBgLight);
            card.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -468f), new Vector2(360f, 174f));
            string text;
            if (Result == null) text = UiStyle.Colour("Nothing yet. What you forge goes into the shared stash.", UiStyle.TextFaint);
            else
            {
                var sb = new StringBuilder();
                sb.Append($"<size=21><b>{ItemText.Name(Result)}</b></size>\n");
                sb.Append(UiStyle.Colour(ItemText.Line(Result), UiStyle.TextDim)).Append('\n');
                foreach (var a in Result.Affixes) sb.Append(UiStyle.Colour(a.ToString(), new Color(0.62f, 0.75f, 1f))).Append(UiStyle.Colour($"  T{a.Tier}", UiStyle.TextFaint)).Append('\n');
                sb.Append(UiStyle.Colour("In the stash. Wear it from Stash.", UiStyle.TextFaint));
                text = sb.ToString();
            }
            var t = UiStyle.Label(card, "Text", text, 16, UiStyle.TextMain, TextAnchor.UpperLeft);
            t.rectTransform.Stretch(18f, 12f, 8f, 6f);
            t.lineSpacing = 1.08f;
            if (Result != null) StashScreen.Stripe(card, Result.Rarity);
        }

        private static void BuildPreview(RectTransform panel, GameFlow flow, PlayerProfile p, CraftRequest req, ItemBase b, Action rebuild)
        {
            StashScreen.Section(panel, "PreviewTitle", "PREVIEW", new Vector2(1200f, -140f));
            var card = UiStyle.Panel(panel, "Preview", UiStyle.PanelBgLight);
            card.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(1200f, -172f), new Vector2(520f, 468f));
            var sb = new StringBuilder();
            var odds = CraftingActions.RarityOdds(req);
            sb.Append($"<size=26><b>{UiStyle.Colour(b.Name, UiStyle.TextMain)}</b></size>   {UiStyle.Colour($"item level {req.Level}", UiStyle.Gold)}\n");
            sb.Append(UiStyle.Colour(BaseStats(b, req.Level), UiStyle.TextDim)).Append("\n\n");
            string rarity = odds.rare >= 0.999f ? UiStyle.Colour("Rare", ItemText.Colour(Rarity.Rare)) + UiStyle.Colour(req.ForceRare ? "  (forced)" : "  (two affixes of one kind need room)", UiStyle.TextFaint)
                : $"{UiStyle.Colour("Magic", ItemText.Colour(Rarity.Magic))} {Mathf.RoundToInt(odds.magic * 100f)}%   {UiStyle.Colour("Rare", ItemText.Colour(Rarity.Rare))} {Mathf.RoundToInt(odds.rare * 100f)}%";
            sb.Append($"{UiStyle.Colour("RARITY", UiStyle.GoldDim)}   {rarity}\n");
            sb.Append($"{UiStyle.Colour("AFFIXES", UiStyle.GoldDim)}   ");
            if (req.GuaranteedFamilies.Count == 0) sb.Append(UiStyle.Colour("all rolled like a drop: 1–2 if Magic, 3–4 if Rare", UiStyle.TextDim)).Append('\n');
            else
            {
                sb.Append(UiStyle.Colour("guaranteed, plus the rest rolled", UiStyle.TextDim)).Append('\n');
                foreach (var f in req.GuaranteedFamilies) sb.Append("   ").Append(UiStyle.Colour(CraftingActions.AffixRange(f, req.Level), new Color(0.62f, 0.75f, 1f))).Append('\n');
            }
            sb.Append(UiStyle.Colour("Never Normal or Unique. Comes fresh, at full durability.", UiStyle.TextFaint)).Append("\n\n");
            sb.Append(UiStyle.Colour("COST", UiStyle.GoldDim)).Append('\n');
            foreach (var c in CraftingActions.Costs(p, req))
                sb.Append(UiStyle.Colour($"{c.Need:N0} {PrettyCost(c.Name)}", c.Ok ? UiStyle.Good : UiStyle.Bad)).Append(UiStyle.Colour($"   you have {c.Have:N0}", UiStyle.TextFaint)).Append('\n');
            var t = UiStyle.Label(card, "Text", sb.ToString(), 19, UiStyle.TextMain, TextAnchor.UpperLeft);
            t.rectTransform.Stretch(24f, 24f, 18f, 100f);
            t.lineSpacing = 1.08f;

            var why = CraftingActions.Refusal(p, req);
            var craft = StashScreen.Btn(card, "CraftButton", "Craft", () =>
            {
                var rng = new DungeonRandom(unchecked(Environment.TickCount * 31 + p.Stash.Count * 7919 + (int)(p.Silver & 0xFFFF) + (int)(Time.realtimeSinceStartup * 1000f)));
                var refusal = CraftingActions.Craft(p, req, rng, out var made);
                if (refusal != null) { Note = refusal; UiSound.Play(UiSound.Error); }
                else
                {
                    flow.Save();
                    Result = made;
                    Note = $"Crafted {made.Name}: it is in the stash.";
                    UiSound.Play(UiSound.Confirm);
                }
                rebuild();
            }, new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(300f, 60f), new Vector2(0.5f, 0f));
            craft.interactable = why == null;
            if (why != null)
            {
                var w = UiStyle.Label(card, "Why", why, 16, UiStyle.Bad, TextAnchor.MiddleCenter);
                w.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 84f), new Vector2(480f, 24f));
            }
        }

        // ---------------- text ----------------

        private static string BaseStats(ItemBase b, int level)
        {
            if (b.IsWeapon) return $"Damage ×{b.DamageAt(level):0.00}  ·  speed ×{b.WeaponSpeed:0.00}  ·  {b.AttackType}  ·  scales with {b.ScalesWith}";
            if (b.IsArmour) return $"Armour {b.ArmourAt(level):0}  ·  resist {b.ResistAt(level):0}  ·  {b.Weight} {b.Slot}  ·  {b.WeightKg:0.#} kg";
            return $"{b.Slot}  ·  no base stats, the affixes are the point  ·  {b.WeightKg:0.#} kg";
        }

        private static string PrettyCost(string name) => name.EndsWith(" essence") ? FamilyName(name.Substring(0, name.Length - " essence".Length)) + " essence" : name;

        /// <summary>Short bench names: "Resist:Fire" is "Fire resist", "Attribute:Strength" is "Strength", "AddedDamage:Fire" is "Added Fire", "IncreasedDamage" is "Increased Damage".</summary>
        public static string FamilyName(string key)
        {
            int colon = key.IndexOf(':');
            if (colon > 0)
            {
                string kind = key.Substring(0, colon), arg = key.Substring(colon + 1);
                if (kind == "Resist") return arg + " resist";
                if (kind == "Attribute") return arg;
                if (kind == "AddedDamage") return "Added " + arg;
            }
            return Regex.Replace(key.Replace(':', ' '), "(?<=[a-z])(?=[A-Z])", " ");
        }
    }
}
