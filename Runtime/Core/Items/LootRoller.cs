using Attribute = DungeonGen.Core.Combat.Attribute;
using System;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Combat;

namespace DungeonGen.Core.Items
{
    /// <summary>Where a drop comes from. Sets the rarity floor and the unique chance.</summary>
    public enum LootSource { Chest, LootRoom, Boss, Vault, FinalBoss }

    /// <summary>
    /// Rolls items deterministically from a <see cref="DungeonRandom"/>: base, rarity by item
    /// level and source, affixes by rarity with prefix/suffix limits and no repeated family,
    /// affix tiers by item level, and PoE-style names.
    /// </summary>
    public static class LootRoller
    {
        // Normal / Magic / Rare weights per item level 0..4. Uniques never come from this table:
        // only a boss, a vault or the final boss can drop one, each with its own chance.
        private static readonly int[][] RarityWeights =
        {
            new[] { 70, 28, 2 },
            new[] { 55, 38, 7 },
            new[] { 40, 42, 18 },
            new[] { 25, 45, 30 },
            new[] { 10, 40, 50 },
        };

        public static Rarity RollRarity(int level, LootSource source, DungeonRandom rng)
        {
            level = Math.Max(0, Math.Min(4, level));
            float uniqueChance = source == LootSource.FinalBoss ? 0.35f : source == LootSource.Vault ? 0.15f : source == LootSource.Boss ? 0.08f : 0f;
            if (uniqueChance > 0f && rng.Chance(uniqueChance)) return Rarity.Unique;

            var w = RarityWeights[level];
            Rarity floor = source == LootSource.Vault || source == LootSource.FinalBoss ? Rarity.Rare : source == LootSource.Boss || source == LootSource.LootRoom ? Rarity.Magic : Rarity.Normal;
            int total = 0;
            for (int i = (int)floor; i < w.Length; i++) total += w[i];
            int r = rng.Range(0, Math.Max(1, total));
            for (int i = (int)floor; i < w.Length; i++) { r -= w[i]; if (r < 0) return (Rarity)i; }
            return floor;
        }

        public static Item Roll(int level, LootSource source, DungeonRandom rng, string themedSource = null)
        {
            var rarity = RollRarity(level, source, rng);
            if (rarity == Rarity.Unique) return RollUnique(level, rng, themedSource);

            var b = RollBase(rng);
            // Magic: 1-2 affixes, at most one of each kind. Rare: 3-4, at most two of each kind.
            int total = rarity == Rarity.Magic ? rng.Range(1, 3) : rarity == Rarity.Rare ? rng.Range(3, 5) : 0;
            int cap = rarity == Rarity.Rare ? 2 : 1;
            int wantPrefixes = total == 0 ? 0 : rarity == Rarity.Magic ? (total == 2 ? 1 : (rng.Chance(0.5f) ? 1 : 0)) : rng.Range(1, 3);

            var pre = RollAffixes(b, level, Math.Min(cap, wantPrefixes), true, rng, new List<AffixRoll>());
            var suf = RollAffixes(b, level, Math.Min(cap, total - pre.Count), false, rng, pre);
            if (pre.Count + suf.Count < total)   // a slot with a thin prefix pool: top up from the other side
                pre.AddRange(RollAffixes(b, level, Math.Min(cap, total - suf.Count) - pre.Count, true, rng, pre.Concat(suf).ToList()));
            string name = Name(b, rarity, pre, suf, rng);
            var item = new Item(b, level, rarity, name, pre, suf);
            // Found gear is used gear: ordinary chests hold items at 60-100%; bosses, vaults and the final boss hand out fresh ones.
            if (source == LootSource.Chest || source == LootSource.LootRoom)
                item.Condition.Wear(item.Condition.Max * (1f - (DurabilityRules.FoundMinFraction + rng.NextFloat() * (DurabilityRules.FoundMaxFraction - DurabilityRules.FoundMinFraction))));
            return item;
        }

        public static Item RollUnique(int level, DungeonRandom rng, string themedSource = null)
        {
            var pool = UniqueItem.All;
            var themed = themedSource == null ? new List<UniqueItem>() : pool.Where(u => u.Source == themedSource).ToList();
            var u = themed.Count > 0 && rng.Chance(0.6f) ? themed[0] : rng.Pick(pool);
            var pre = new List<AffixRoll>();
            var suf = new List<AffixRoll>();
            foreach (var (kind, value, type, attr) in u.Fixed)
            {
                var def = AffixDefinition.Pool.First(d => d.Kind == kind && d.Type == type && d.Attribute == attr);
                (def.IsPrefix ? pre : suf).Add(new AffixRoll(def, 4, value));
            }
            return new Item(u.Base, Math.Max(level, 3), Rarity.Unique, u.Name, pre, suf, u);
        }

        private static ItemBase RollBase(DungeonRandom rng)
        {
            // Weapons 25%, armour 50% (five slots), jewellery 25%.
            float r = rng.NextFloat();
            if (r < 0.25f) return rng.Pick(ItemBase.Weapons);
            if (r < 0.75f) return rng.Pick(ItemBase.Armours);
            return rng.Pick(ItemBase.Jewellery);
        }

        private static List<AffixRoll> RollAffixes(ItemBase b, int level, int count, bool prefix, DungeonRandom rng, List<AffixRoll> taken)
        {
            var result = new List<AffixRoll>();
            var pool = AffixDefinition.Pool.Where(d => d.IsPrefix == prefix && d.Slots.Contains(b.Slot)).ToList();
            for (int i = 0; i < count && pool.Count > 0; i++)
            {
                var available = pool.Where(d => !taken.Concat(result).Any(t => t.Definition.SameFamily(d))).ToList();
                if (available.Count == 0) break;
                int total = available.Sum(d => d.Weight);
                int r = rng.Range(0, total);
                AffixDefinition chosen = available[0];
                foreach (var d in available) { r -= d.Weight; if (r < 0) { chosen = d; break; } }

                // Tier follows item level, with a 30% chance of one tier lower so high-level drops still vary.
                int tier = Math.Min(4, Math.Max(1, level)) ;
                if (level == 0) tier = 1;
                if (tier > 1 && rng.Chance(0.3f)) tier--;
                var (min, max) = chosen.Tiers[tier - 1];
                float value = (float)Math.Round(min + rng.NextFloat() * (max - min));
                result.Add(new AffixRoll(chosen, tier, value));
            }
            return result;
        }

        private static readonly string[] RareFirst = { "Dread", "Storm", "Grim", "Ember", "Hollow", "Vile", "Iron", "Pale", "Blood", "Gloom", "Sun", "Wolf", "Raven", "Shard", "Brine", "Thorn" };
        private static readonly string[] RareSecond = { "Grip", "Bane", "Ward", "Song", "Mark", "Veil", "Fang", "Crown", "Hide", "Bite", "Step", "Knell", "Edge", "Shell", "Vow", "Howl" };

        private static string Name(ItemBase b, Rarity rarity, List<AffixRoll> pre, List<AffixRoll> suf, DungeonRandom rng)
        {
            switch (rarity)
            {
                case Rarity.Magic:
                    {
                        string p = pre.Count > 0 ? pre[0].Definition.Word + " " : "";
                        string s = suf.Count > 0 ? " " + suf[0].Definition.Word : "";
                        return (p + b.Name + s).Trim();
                    }
                case Rarity.Rare:
                    return $"{rng.Pick(RareFirst)} {rng.Pick(RareSecond)}, {b.Name}";
                default:
                    return b.Name;
            }
        }
    }
}
