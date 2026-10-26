using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using DungeonGen.Core.Items;

namespace DungeonGen.Core.Profile
{
    /// <summary>One line of a craft's price: what, how much it takes, how much the account has.</summary>
    public readonly struct CostLine
    {
        public string Name { get; }
        public long Need { get; }
        public long Have { get; }
        public bool Ok => Have >= Need;
        public CostLine(string name, long need, long have) { Name = name; Need = need; Have = have; }
    }

    /// <summary>The cheapest craft of one base at one item level (no pins, no forced Rare): the bench's recipe list.</summary>
    public readonly struct Recipe
    {
        public int Level { get; }
        public int Scrap { get; }
        public long SilverFee { get; }
        public bool Affordable { get; }
        public Recipe(int level, int scrap, long fee, bool affordable) { Level = level; Scrap = scrap; SilverFee = fee; Affordable = affordable; }
    }

    /// <summary>
    /// The crafting bench between rounds: what a <see cref="CraftRequest"/> costs this account, why it is refused, and the craft itself,
    /// which spends scrap, shards, pinned essences and the silver fee from the profile and puts the new item in the stash. The rules
    /// (prices, what may be pinned, how the item rolls) stay in <see cref="CraftingRules"/>, <see cref="CraftRequest"/> and <see cref="Crafter"/>;
    /// the fee in <see cref="Vendor.Craft"/>. Like <see cref="StashActions"/>, every call either changes the profile and returns null, or
    /// changes nothing and returns why not; the caller saves. Crafting only ever makes a new item (there is no reroll or upgrade of an
    /// existing one: salvage it for materials instead).
    /// </summary>
    public static class CraftingActions
    {
        public const int MinLevel = 0, MaxLevel = 4;

        /// <summary>Slots in the order the bench lists them.</summary>
        public static readonly IReadOnlyList<ItemSlot> Slots = new[]
        {
            ItemSlot.Weapon, ItemSlot.Head, ItemSlot.Chest, ItemSlot.Hands, ItemSlot.Legs, ItemSlot.Feet, ItemSlot.Amulet, ItemSlot.Ring,
        };

        /// <summary>Every base that fills a slot, weapons first as they are listed in <see cref="ItemBase"/>.</summary>
        public static IReadOnlyList<ItemBase> Bases(ItemSlot slot) => ItemBase.All.Where(b => b.Slot == slot).ToList();

        /// <summary>The affix families that may roll on a base: the only ones that can be pinned.</summary>
        public static IReadOnlyList<AffixDefinition> LegalFamilies(ItemBase b) =>
            b == null ? new List<AffixDefinition>() : AffixDefinition.Pool.Where(d => d.Slots.Contains(b.Slot)).ToList();

        /// <summary>Legal families the account holds enough essence of to pin (<see cref="CraftingRules.EssencesToGuarantee"/>), strongest stock first.</summary>
        public static IReadOnlyList<string> PinnableFamilies(PlayerProfile p, ItemBase b) =>
            LegalFamilies(b).Select(d => d.FamilyKey).Where(f => p.Materials.Essence(f) >= CraftingRules.EssencesToGuarantee)
                .OrderByDescending(f => p.Materials.Essence(f)).ThenBy(f => f, StringComparer.Ordinal).ToList();

        /// <summary>The recipe at each item level for a base, with whether scrap and silver cover it today.</summary>
        public static IReadOnlyList<Recipe> Recipes(PlayerProfile p, ItemBase b)
        {
            var list = new List<Recipe>();
            for (int level = MinLevel; level <= MaxLevel; level++)
            {
                var req = new CraftRequest(b, level);
                list.Add(new Recipe(level, req.ScrapCost, CraftingRules.SilverFee(level), Refusal(p, req) == null));
            }
            return list;
        }

        /// <summary>Everything the craft would spend, line by line, with what the account holds.</summary>
        public static IReadOnlyList<CostLine> Costs(PlayerProfile p, CraftRequest req)
        {
            var lines = new List<CostLine> { new CostLine("scrap", req.ScrapCost, p.Materials.Scrap), new CostLine("silver", CraftingRules.SilverFee(req.Level), p.Silver) };
            if (req.ForceRare) lines.Add(new CostLine("prime shards", req.ShardCost, p.Materials.PrimeShards));
            foreach (var f in req.GuaranteedFamilies) lines.Add(new CostLine(f + " essence", req.EssenceCost, p.Materials.Essence(f)));
            return lines;
        }

        /// <summary>Why this craft cannot be made now (materials, silver, a family that cannot roll on the base), or null.</summary>
        public static string Refusal(PlayerProfile p, CraftRequest req)
        {
            if (p == null || req == null) return "Nothing to craft.";
            if (!req.Affordable(p.Materials, out string why)) return "Cannot craft: " + why + ".";
            long fee = CraftingRules.SilverFee(req.Level);
            if (p.Silver < fee) return $"Cannot craft: needs {fee} silver fee, have {p.Silver}.";
            return null;
        }

        /// <summary>
        /// Make it: spends the materials and the silver fee and puts the new item in the stash. Returns null on success (the item in
        /// <paramref name="crafted"/>), or why not, leaving the profile untouched. Callers pass a fresh random each time: a repeat of the
        /// seed is a repeat of the roll.
        /// </summary>
        public static string Craft(PlayerProfile p, CraftRequest req, DungeonRandom rng, out Item crafted)
        {
            crafted = null;
            var why = Refusal(p, req);
            if (why != null) return why;
            if (rng == null) return "Nothing to roll with.";
            crafted = Vendor.Craft(p, req, rng);
            return null;
        }

        /// <summary>
        /// The odds that a craft ends up Magic or Rare (never Normal or Unique), for the preview. A forced-Rare craft, or two pinned
        /// families of the same kind (a Magic item has room for one of each), is always Rare; otherwise the loot table at that level.
        /// </summary>
        public static (float magic, float rare) RarityOdds(CraftRequest req)
        {
            if (req.ForceRare) return (0f, 1f);
            if (req.GuaranteedFamilies.Count == 2 && req.GuaranteedFamilies.Select(f => AffixDefinition.ByFamily(f)?.IsPrefix == true).Distinct().Count() == 1) return (0f, 1f);
            // The table is private to LootRoller: ask it, with a fixed seed so the number never flickers.
            var rng = new DungeonRandom(1000 + req.Level);
            const int n = 4000;
            int rare = 0;
            for (int i = 0; i < n; i++)
            {
                var r = LootRoller.RollRarity(req.Level, LootSource.LootRoom, rng);
                if (r == Rarity.Rare || r == Rarity.Unique) rare++;
            }
            float rareFraction = (float)Math.Round(rare / (float)n, 2);
            return (1f - rareFraction, rareFraction);
        }

        /// <summary>
        /// What a pinned family rolls at an item level: the affix text with its number replaced by the range (tier rolls one lower 30% of the
        /// time above tier 1), e.g. "+13–20% armour".
        /// </summary>
        public static string AffixRange(string family, int level)
        {
            var def = AffixDefinition.ByFamily(family);
            if (def == null) return family;
            level = Math.Max(MinLevel, Math.Min(MaxLevel, level));
            int top = level == 0 ? 1 : Math.Min(4, level);
            int low = top > 1 ? top - 1 : top;
            float min = def.Tiers[low - 1].min, max = def.Tiers[top - 1].max;
            string text = new AffixRoll(def, top, min).ToString();
            var inv = CultureInfo.InvariantCulture;
            string range = Math.Abs(min - max) < 0.001f ? min.ToString("0", inv) : min.ToString("0", inv) + "–" + max.ToString("0", inv);
            return new Regex(@"\d+(\.\d+)?").Replace(text, range, 1);
        }
    }
}
