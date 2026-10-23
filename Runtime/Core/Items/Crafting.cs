using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core.Items
{
    /// <summary>What salvaging produces and crafting consumes. Three kinds, all stackable.</summary>
    public sealed class MaterialBag
    {
        /// <summary>Untyped. Every item yields some; every craft costs some. The main currency of crafting.</summary>
        public int Scrap { get; set; }
        /// <summary>From Rares and Uniques. Spent to force a crafted item to be Rare.</summary>
        public int PrimeShards { get; set; }
        /// <summary>Typed by affix family ("Resist:Fire", "Attribute:Strength", "Health"). Spent to guarantee that family on a craft.</summary>
        public Dictionary<string, int> Essences { get; } = new Dictionary<string, int>();

        public int Essence(string family) => Essences.TryGetValue(family, out int n) ? n : 0;
        public void AddEssence(string family, int n) => Essences[family] = Essence(family) + n;

        public void Add(MaterialBag other)
        {
            Scrap += other.Scrap;
            PrimeShards += other.PrimeShards;
            foreach (var kv in other.Essences) AddEssence(kv.Key, kv.Value);
        }

        public override string ToString() =>
            $"{Scrap} scrap, {PrimeShards} shards" + (Essences.Count > 0 ? ", " + string.Join(", ", Essences.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value} {kv.Key}")) : "");
    }

    /// <summary>The two ways an unwanted item leaves the game, and every crafting number, in one place.</summary>
    public static class CraftingRules
    {
        // --- Salvage ---------------------------------------------------------------------
        public const int ScrapPerLevel = 4;              // × (level + 1) × rarity multiplier
        public static float ScrapRarityMultiplier(Rarity r) => r == Rarity.Unique ? 4f : r == Rarity.Rare ? 2.5f : r == Rarity.Magic ? 1.5f : 1f;
        public static int ShardsFor(Rarity r) => r == Rarity.Unique ? 3 : r == Rarity.Rare ? 1 : 0;
        /// <summary>Essences per affix on the salvaged item: one, plus one more for a T3+ roll.</summary>
        public static int EssencesFor(AffixRoll a) => a.Tier >= 3 ? 2 : 1;

        // --- Vendor ----------------------------------------------------------------------
        public const float SilverPerValue = 8f;
        /// <summary>Broken gear still sells, at this fraction.</summary>
        public const float BrokenSellFraction = 0.25f;

        // --- Crafting --------------------------------------------------------------------
        /// <summary>Scrap to craft at an item level: 10, 40, 90, 160, 250.</summary>
        public static int ScrapCost(int level) => 10 * (level + 1) * (level + 1);
        public const int EssencesToGuarantee = 3;
        /// <summary>Silver fee per craft on top of the materials: 10, 20, 40, 80, 160 by level. The economy's steady silver sink.</summary>
        public static long SilverFee(int level) => 10L << Math.Max(0, Math.Min(4, level));
        public const int ShardsForRare = 3;
        public const int MaxGuaranteedFamilies = 2;

        /// <summary>Break an item down. Broken items salvage like intact ones; destroyed items yield nothing.</summary>
        public static MaterialBag Salvage(Item item)
        {
            var bag = new MaterialBag();
            if (item.Condition.IsDestroyed || item.IsStarter) return bag;
            bag.Scrap = (int)Math.Round(ScrapPerLevel * (item.ItemLevel + 1) * ScrapRarityMultiplier(item.Rarity));
            bag.PrimeShards = ShardsFor(item.Rarity);
            foreach (var a in item.Affixes) bag.AddEssence(a.Definition.FamilyKey, EssencesFor(a));
            return bag;
        }

        /// <summary>Silver a vendor pays. Uniques and Rares are worth more salvaged than sold; Normals the reverse.</summary>
        public static float SellPrice(Item item)
        {
            if (item.Condition.IsDestroyed || item.IsStarter) return 0f;
            float price = item.Value * SilverPerValue;
            return item.Condition.IsBroken ? price * BrokenSellFraction : price * (0.5f + 0.5f * item.Condition.Fraction);
        }
    }

    /// <summary>What a player asks the forge for. Everything not specified here is rolled.</summary>
    public sealed class CraftRequest
    {
        public ItemBase Base { get; }
        public int Level { get; }
        /// <summary>Affix families to guarantee (max 2), each costing EssencesToGuarantee essences. Must be legal on the base's slot.</summary>
        public IReadOnlyList<string> GuaranteedFamilies { get; }
        /// <summary>Spend ShardsForRare shards to force Rare rarity.</summary>
        public bool ForceRare { get; }

        public CraftRequest(ItemBase b, int level, IEnumerable<string> guaranteed = null, bool forceRare = false)
        {
            Base = b; Level = Math.Max(0, Math.Min(4, level));
            GuaranteedFamilies = (guaranteed ?? Enumerable.Empty<string>()).Distinct().Take(CraftingRules.MaxGuaranteedFamilies).ToList();
            ForceRare = forceRare;
        }

        public int ScrapCost => CraftingRules.ScrapCost(Level);
        public int ShardCost => ForceRare ? CraftingRules.ShardsForRare : 0;
        public int EssenceCost => CraftingRules.EssencesToGuarantee;

        public bool Affordable(MaterialBag bag, out string why)
        {
            if (bag.Scrap < ScrapCost) { why = $"needs {ScrapCost} scrap, have {bag.Scrap}"; return false; }
            if (bag.PrimeShards < ShardCost) { why = $"needs {ShardCost} shards, have {bag.PrimeShards}"; return false; }
            foreach (var f in GuaranteedFamilies)
            {
                var def = AffixDefinition.ByFamily(f);
                if (def == null) { why = $"unknown affix family {f}"; return false; }
                if (!def.Slots.Contains(Base.Slot)) { why = $"{f} cannot roll on a {Base.Slot}"; return false; }
                if (bag.Essence(f) < EssenceCost) { why = $"needs {EssenceCost} {f} essence, have {bag.Essence(f)}"; return false; }
            }
            if (GuaranteedFamilies.Count(f => AffixDefinition.ByFamily(f)?.IsPrefix == true) > 2 || GuaranteedFamilies.Count(f => AffixDefinition.ByFamily(f)?.IsPrefix == false) > 2)
            { why = "too many guaranteed affixes of one kind"; return false; }
            why = null;
            return true;
        }
    }

    /// <summary>
    /// The forge. You choose the base and the level, and can pin one or two affix families; the
    /// rarity (unless you pay for Rare), the other affixes and every value still roll exactly as a
    /// drop would. Crafted items are never Normal, never Unique, and always fresh.
    /// </summary>
    public static class Crafter
    {
        public static Item Craft(CraftRequest req, MaterialBag bag, DungeonRandom rng)
        {
            if (!req.Affordable(bag, out string why)) throw new InvalidOperationException("Cannot craft: " + why);

            bag.Scrap -= req.ScrapCost;
            bag.PrimeShards -= req.ShardCost;
            foreach (var f in req.GuaranteedFamilies) bag.AddEssence(f, -req.EssenceCost);

            // Rarity: forced Rare, else the drop table at this level with Normal removed.
            Rarity rarity = req.ForceRare ? Rarity.Rare : LootRoller.RollRarity(req.Level, LootSource.LootRoom, rng);
            if (rarity == Rarity.Unique) rarity = Rarity.Rare;
            if (rarity == Rarity.Normal) rarity = Rarity.Magic;
            // Two guaranteed families need room: a Magic item has only one of each kind.
            if (req.GuaranteedFamilies.Count == 2 && rarity == Rarity.Magic)
            {
                var kinds = req.GuaranteedFamilies.Select(f => AffixDefinition.ByFamily(f).IsPrefix).Distinct().Count();
                if (kinds == 1) rarity = Rarity.Rare;
            }

            int total = rarity == Rarity.Magic ? rng.Range(1, 3) : rng.Range(3, 5);
            int cap = rarity == Rarity.Rare ? 2 : 1;

            var pre = new List<AffixRoll>();
            var suf = new List<AffixRoll>();
            foreach (var f in req.GuaranteedFamilies)
            {
                var def = AffixDefinition.ByFamily(f);
                (def.IsPrefix ? pre : suf).Add(LootRoller.RollOne(def, req.Level, rng));
            }
            total = Math.Max(total, pre.Count + suf.Count);

            // Fill the rest randomly, honouring the per-kind cap and family exclusion.
            int wantPre = Math.Min(cap, rarity == Rarity.Magic ? (total == 2 ? 1 : (pre.Count > 0 ? 1 : (rng.Chance(0.5f) ? 1 : 0))) : rng.Range(1, 3));
            if (wantPre > pre.Count) pre.AddRange(LootRoller.RollAffixes(req.Base, req.Level, wantPre - pre.Count, true, rng, pre.Concat(suf).ToList()));
            int wantSuf = Math.Min(cap, total - pre.Count);
            if (wantSuf > suf.Count) suf.AddRange(LootRoller.RollAffixes(req.Base, req.Level, wantSuf - suf.Count, false, rng, pre.Concat(suf).ToList()));
            if (pre.Count + suf.Count < total && pre.Count < cap)
                pre.AddRange(LootRoller.RollAffixes(req.Base, req.Level, Math.Min(cap - pre.Count, total - pre.Count - suf.Count), true, rng, pre.Concat(suf).ToList()));

            string name = LootRoller.NameFor(req.Base, rarity, pre, suf, rng);
            return new Item(req.Base, req.Level, rarity, name, pre, suf);
        }
    }
}
