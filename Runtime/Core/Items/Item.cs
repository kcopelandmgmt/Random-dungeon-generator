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
        public IEnumerable<AffixRoll> Affixes => Prefixes.Concat(Suffixes);

        public Item(ItemBase b, int level, Rarity rarity, string name, List<AffixRoll> prefixes, List<AffixRoll> suffixes, UniqueItem unique = null)
        {
            Base = b; ItemLevel = level; Rarity = rarity; Name = name; Prefixes = prefixes; Suffixes = suffixes; Unique = unique;
        }

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
            string stats = Base.IsWeapon ? $"dmg x{WeaponDamage:0.00} spd x{Base.WeaponSpeed:0.00} {Base.AttackType}" : Base.IsArmour ? $"armour {Armour:0}" + (BaseResist > 0 ? $" res {BaseResist:0}" : "") : "";
            return $"[{Rarity}] {Name} (L{ItemLevel} {Base.Slot}{(Base.IsArmour ? " " + Base.Weight : "")}) {stats}{(parts.Count > 0 ? " | " + string.Join(", ", parts) : "")}";
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

        public UniqueItem(string name, ItemBase b, string flavour, string source, params (AffixKind, float, DamageType?, Attribute?)[] fixedAffixes)
        {
            Name = name; Base = b; Flavour = flavour; Source = source; Fixed = fixedAffixes;
        }

        public static readonly IReadOnlyList<UniqueItem> All = new[]
        {
            new UniqueItem("Sovereign's Crown", ItemBase.Armours.First(a => a.Name == "Plate Helm"),
                "It is heavier than it looks. So was the throne.", "The Sovereign Below",
                (AffixKind.Attribute, 3f, null, Attribute.Strength), (AffixKind.Attribute, 3f, null, Attribute.Constitution), (AffixKind.Attribute, 3f, null, Attribute.Spirit), (AffixKind.AllResist, 25f, null, null)),
            new UniqueItem("Ashen Heart", ItemBase.Jewellery.First(j => j.Slot == ItemSlot.Amulet),
                "Still warm. Still beating, if you listen.", "Ashen Colossus",
                (AffixKind.Resist, 60f, DamageType.Fire, null), (AffixKind.AddedDamage, 30f, DamageType.Fire, null), (AffixKind.Resist, -20f, DamageType.Frost, null)),
            new UniqueItem("Matron's Carapace", ItemBase.Armours.First(a => a.Name == "Brigandine"),
                "The brood recognises it. They do not attack. They watch.", "Hive Matron",
                (AffixKind.IncreasedArmour, 35f, null, null), (AffixKind.Resist, 45f, DamageType.Poison, null), (AffixKind.Health, 8f, null, null)),
            new UniqueItem("Hound's Fang", ItemBase.Weapons.First(w => w.Name == "Twin Daggers"),
                "It wants to run. Let it.", "Hound of Vesh",
                (AffixKind.IncreasedDamage, 25f, null, null), (AffixKind.MoveSpeed, 10f, null, null), (AffixKind.Attribute, 4f, null, Attribute.Dexterity)),
            new UniqueItem("Glasswright's Prism", ItemBase.Jewellery.First(j => j.Slot == ItemSlot.Ring),
                "Every face shows a different room.", "Glasswright",
                (AffixKind.AllResist, 15f, null, null), (AffixKind.AddedDamage, 20f, DamageType.Lightning, null), (AffixKind.Crit, 3f, null, null)),
            new UniqueItem("Choirmaster's Mantle", ItemBase.Armours.First(a => a.Name == "Woven Robe"),
                "The dead sing quieter when you wear it.", "Bone Choir",
                (AffixKind.Health, 15f, null, null), (AffixKind.Resist, 50f, DamageType.Shadow, null), (AffixKind.Healing, 15f, null, null)),
            new UniqueItem("Herald's Censer", ItemBase.Weapons.First(w => w.Name == "Censer"),
                "The smoke heals your friends and poisons everyone else.", "Plague Herald",
                (AffixKind.Healing, 25f, null, null), (AffixKind.AddedDamage, 30f, DamageType.Poison, null), (AffixKind.Attribute, 3f, null, Attribute.Spirit)),
            new UniqueItem("Wraithlight Boots", ItemBase.Armours.First(a => a.Name == "Soft Shoes"),
                "Your footsteps arrive a moment after you do.", "Lantern Wraith",
                (AffixKind.MoveSpeed, 12f, null, null), (AffixKind.Resist, 35f, DamageType.Shadow, null), (AffixKind.Resist, 35f, DamageType.Frost, null)),
        };
    }
}
