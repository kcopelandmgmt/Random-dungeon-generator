using Attribute = DungeonGen.Core.Combat.Attribute;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Combat;

namespace DungeonGen.Core.Items
{
    /// <summary>A concrete item: base + level + rarity + affixes. Immutable once rolled; the server rolls, clients display.</summary>
    public sealed class Item
    {
        public ItemBase Base { get; }
        public int ItemLevel { get; }
        public Rarity Rarity { get; }
        public string Name { get; }
        public IReadOnlyList<AffixRoll> Prefixes { get; }
        public IReadOnlyList<AffixRoll> Suffixes { get; }
        /// <summary>Set when this is a unique item.</summary>
        public UniqueItem Unique { get; }
        /// <summary>Wear state. The one mutable thing about an item.</summary>
        public ItemCondition Condition { get; }
        /// <summary>Free starter gear. Worth nothing to the vendor and yields nothing when salvaged, or eight character slots would be a materials farm.</summary>
        public bool IsStarter { get; private set; }
        public Item MarkStarter() { IsStarter = true; return this; }
        public IEnumerable<AffixRoll> Affixes => Prefixes.Concat(Suffixes);

        public Item(ItemBase b, int level, Rarity rarity, string name, List<AffixRoll> prefixes, List<AffixRoll> suffixes, UniqueItem unique = null)
        {
            Base = b; ItemLevel = level; Rarity = rarity; Name = name; Prefixes = prefixes; Suffixes = suffixes; Unique = unique;
            float max = DurabilityRules.MaxFor(rarity);
            Condition = new ItemCondition(max, max);
        }

        public float WeightKg => Base.WeightKg;
        public float Armour => Base.ArmourAt(ItemLevel) * (1f + Sum(AffixKind.IncreasedArmour) / 100f);
        public float BaseResist => Base.ResistAt(ItemLevel);
        public float WeaponDamage => Base.DamageAt(ItemLevel) * (1f + Sum(AffixKind.IncreasedDamage) / 100f);

        public float Sum(AffixKind kind) => Affixes.Where(a => a.Definition.Kind == kind).Sum(a => a.Value);
        public float Sum(AffixKind kind, DamageType t) => Affixes.Where(a => a.Definition.Kind == kind && a.Definition.Type == t).Sum(a => a.Value);
        public float Sum(AffixKind kind, Attribute at) => Affixes.Where(a => a.Definition.Kind == kind && a.Definition.Attribute == at).Sum(a => a.Value);

        /// <summary>Rough trade value in "tier points": what the loot-weight maths and the HUD colour use.</summary>
        public int Value => (ItemLevel + 1) * (Rarity == Rarity.Unique ? 12 : Rarity == Rarity.Rare ? 5 : Rarity == Rarity.Magic ? 2 : 1);

        public override string ToString()
        {
            var parts = Affixes.Select(a => a.ToString()).ToList();
            if (Unique?.Legendary != null) parts.Insert(0, "★ " + Unique.Legendary);
            string stats = Base.IsWeapon ? $"dmg x{WeaponDamage:0.00} spd x{Base.WeaponSpeed:0.00} {Base.AttackType}" : Base.IsArmour ? $"armour {Armour:0}" + (BaseResist > 0 ? $" res {BaseResist:0}" : "") : "";
            return $"[{Rarity}] {Name} (L{ItemLevel} {Base.Slot}{(Base.IsArmour ? " " + Base.Weight : "")}) {stats} dur {Condition}{(parts.Count > 0 ? " | " + string.Join(", ", parts) : "")}";
        }
    }

    /// <summary>A named item with fixed affixes. Dropped by bosses, vaults and the final boss only.</summary>
    public sealed class UniqueItem
    {
        public string Name { get; }
        public ItemBase Base { get; }
        public string Flavour { get; }
        /// <summary>Which boss or source it is themed on; the loot tables use it for a themed drop chance.</summary>
        public string Source { get; }
        public IReadOnlyList<(AffixKind kind, float value, DamageType? type, Attribute? attribute)> Fixed { get; }
        /// <summary>The headline: a behaviour no Magic or Rare item can roll.</summary>
        public LegendaryAffix Legendary { get; }

        public UniqueItem(string name, ItemBase b, string flavour, string source, LegendaryAffix legendary, params (AffixKind, float, DamageType?, Attribute?)[] fixedAffixes)
        {
            Name = name; Base = b; Flavour = flavour; Source = source; Legendary = legendary; Fixed = fixedAffixes;
        }

        /// <summary>The unique with this exact name, or null. Saved profiles store uniques by name.</summary>
        public static UniqueItem ByName(string name) => All.FirstOrDefault(u => u.Name == name);

        public static readonly IReadOnlyList<UniqueItem> All = new[]
        {
            new UniqueItem("Sovereign's Crown", ItemBase.Armours.First(a => a.Name == "Plate Helm"),
                "It is heavier than it looks. So was the throne.", "The Sovereign Below",
                new LegendaryAffix("Unyielding", LegendaryKind.ResistCap, LegendaryTrigger.Passive, 10f, "Your resistance cap is 85% instead of 75%, for every damage type."),
                (AffixKind.Attribute, 3f, null, Attribute.Strength), (AffixKind.Attribute, 3f, null, Attribute.Constitution), (AffixKind.Attribute, 3f, null, Attribute.Spirit), (AffixKind.AllResist, 25f, null, null)),
            new UniqueItem("Ashen Heart", ItemBase.Jewellery.First(j => j.Slot == ItemSlot.Amulet),
                "Still warm. Still beating, if you listen.", "Ashen Colossus",
                new LegendaryAffix("Cinder Pulse", LegendaryKind.ProcDamage, LegendaryTrigger.OnHit, 50f, "15% chance on hit: a burst of 50% weapon damage as Fire to everything within 3 m.", chance: 0.15f, type: DamageType.Fire, note: "radius 3"),
                (AffixKind.Resist, 60f, DamageType.Fire, null), (AffixKind.AddedDamage, 30f, DamageType.Fire, null), (AffixKind.Resist, -20f, DamageType.Frost, null)),
            new UniqueItem("Matron's Carapace", ItemBase.Armours.First(a => a.Name == "Brigandine"),
                "The brood recognises it. They do not attack. They watch.", "Hive Matron",
                new LegendaryAffix("Brood Mother", LegendaryKind.Summon, LegendaryTrigger.OnTakeHit, 10f, "10% chance when hit: a Skitterer fights for you for 10 s.", chance: 0.10f, note: "Skitterer"),
                (AffixKind.IncreasedArmour, 35f, null, null), (AffixKind.Resist, 45f, DamageType.Poison, null), (AffixKind.Health, 8f, null, null)),
            new UniqueItem("Hound's Fang", ItemBase.Weapons.First(w => w.Name == "Twin Daggers"),
                "It wants to run. Let it.", "Hound of Vesh",
                new LegendaryAffix("Bloodhound", LegendaryKind.Lifesteal, LegendaryTrigger.Passive, 8f, "8% of damage you deal is returned as health."),
                (AffixKind.IncreasedDamage, 25f, null, null), (AffixKind.MoveSpeed, 10f, null, null), (AffixKind.Attribute, 4f, null, Attribute.Dexterity)),
            new UniqueItem("Glasswright's Prism", ItemBase.Jewellery.First(j => j.Slot == ItemSlot.Ring),
                "Every face shows a different room.", "Glasswright",
                new LegendaryAffix("Refraction", LegendaryKind.Thorns, LegendaryTrigger.OnTakeHit, 20f, "20% of damage you take is reflected to the attacker as Lightning.", type: DamageType.Lightning),
                (AffixKind.AllResist, 15f, null, null), (AffixKind.AddedDamage, 20f, DamageType.Lightning, null), (AffixKind.Crit, 3f, null, null)),
            new UniqueItem("Choirmaster's Mantle", ItemBase.Armours.First(a => a.Name == "Woven Robe"),
                "The dead sing quieter when you wear it.", "Bone Choir",
                new LegendaryAffix("Last Verse", LegendaryKind.SecondWind, LegendaryTrigger.OnLethalHit, 1f, "A killing blow leaves you at 1 health instead. Once every 120 s.", cooldown: 120f),
                (AffixKind.Health, 15f, null, null), (AffixKind.Resist, 50f, DamageType.Shadow, null), (AffixKind.Healing, 15f, null, null)),
            new UniqueItem("Herald's Censer", ItemBase.Weapons.First(w => w.Name == "Censer"),
                "The smoke heals your friends and poisons everyone else.", "Plague Herald",
                new LegendaryAffix("Miasma", LegendaryKind.HealSplash, LegendaryTrigger.OnHeal, 30f, "Heals you cast also deal 30% of the amount as Poison to enemies within 3 m of the target.", type: DamageType.Poison, note: "radius 3"),
                (AffixKind.Healing, 25f, null, null), (AffixKind.AddedDamage, 30f, DamageType.Poison, null), (AffixKind.Attribute, 3f, null, Attribute.Spirit)),
            new UniqueItem("Wraithlight Boots", ItemBase.Armours.First(a => a.Name == "Soft Shoes"),
                "Your footsteps arrive a moment after you do.", "Lantern Wraith",
                new LegendaryAffix("Phase Step", LegendaryKind.Phasing, LegendaryTrigger.Passive, 15f, "Enemies cannot body-block you, and you move 15% faster while passing through one."),
                (AffixKind.MoveSpeed, 12f, null, null), (AffixKind.Resist, 35f, DamageType.Shadow, null), (AffixKind.Resist, 35f, DamageType.Frost, null)),
        };
    }
}
