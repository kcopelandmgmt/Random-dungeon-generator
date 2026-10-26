using Attribute = DungeonGen.Core.Combat.Attribute;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Combat;

namespace DungeonGen.Core.Items
{
    public enum ItemSlot { Weapon, Head, Chest, Hands, Legs, Feet, Amulet, Ring }

    public enum Rarity { Normal, Magic, Rare, Unique }

    /// <summary>
    /// What an item is before any affix: a slot, a weight for armour, an attribute a weapon
    /// scales from, and the base numbers at a given item level. Anyone can wear anything;
    /// the class only decides what is worth wearing.
    /// </summary>
    public sealed class ItemBase
    {
        public string Name { get; }
        public ItemSlot Slot { get; }
        /// <summary>Armour pieces only.</summary>
        public ArmourWeight Weight { get; }
        /// <summary>Weapons only: the attribute basic attacks and the class kit scale from.</summary>
        public Attribute? ScalesWith { get; }
        /// <summary>Weapons only: what basic attacks deal.</summary>
        public DamageType AttackType { get; }
        /// <summary>Weapons: damage vs. a 1.0 baseline weapon at the same level; attack speed trades against it.</summary>
        public float WeaponDamage { get; }
        public float WeaponSpeed { get; }

        public bool IsWeapon => Slot == ItemSlot.Weapon;
        public bool IsArmour => Slot == ItemSlot.Head || Slot == ItemSlot.Chest || Slot == ItemSlot.Hands || Slot == ItemSlot.Legs || Slot == ItemSlot.Feet;
        public bool IsJewellery => Slot == ItemSlot.Amulet || Slot == ItemSlot.Ring;

        private ItemBase(string name, ItemSlot slot, ArmourWeight weight, Attribute? scales, DamageType attack, float dmg, float speed)
        {
            Name = name; Slot = slot; Weight = weight; ScalesWith = scales; AttackType = attack; WeaponDamage = dmg; WeaponSpeed = speed;
        }

        public static ItemBase Weapon(string name, Attribute scales, DamageType attack, float dmg = 1f, float speed = 1f) =>
            new ItemBase(name, ItemSlot.Weapon, ArmourWeight.Medium, scales, attack, dmg, speed);
        public static ItemBase Armour(string name, ItemSlot slot, ArmourWeight weight) =>
            new ItemBase(name, slot, weight, null, DamageType.Physical, 0f, 1f);
        public static ItemBase Jewel(string name, ItemSlot slot) =>
            new ItemBase(name, slot, ArmourWeight.Light, null, DamageType.Physical, 0f, 1f);

        /// <summary>Base scaling per item level: tier-4 bases are 40% better than tier-0. Affixes add on top.</summary>
        public static float LevelMultiplier(int itemLevel) => 1f + 0.10f * System.Math.Max(0, itemLevel);

        /// <summary>Share of a full set's armour each piece carries. Sums to 1.</summary>
        public static float SlotShare(ItemSlot slot)
        {
            switch (slot)
            {
                case ItemSlot.Chest: return 0.35f;
                case ItemSlot.Legs: return 0.25f;
                case ItemSlot.Head: return 0.15f;
                case ItemSlot.Hands: return 0.125f;
                case ItemSlot.Feet: return 0.125f;
                default: return 0f;
            }
        }

        /// <summary>Kilograms. Heavy plate is the reason Strength exists on a Warden.</summary>
        public float WeightKg
        {
            get
            {
                if (IsWeapon) return ScalesWith == Attribute.Strength ? 6f : ScalesWith == Attribute.Dexterity ? 2.5f : 2f;
                if (IsJewellery) return 0.2f;
                float perWeight = Weight == ArmourWeight.Heavy ? 30f : Weight == ArmourWeight.Medium ? 15f : 6f;
                return perWeight * SlotShare(Slot) / 0.35f * 0.35f;   // a full set weighs perWeight; pieces by slot share
            }
        }

        public float ArmourAt(int level) => IsArmour ? StatFormulas.BaseArmour(Weight) * SlotShare(Slot) * LevelMultiplier(level) : 0f;
        public float ResistAt(int level) => IsArmour ? StatFormulas.BaseResist(Weight) * SlotShare(Slot) * LevelMultiplier(level) : 0f;
        public float DamageAt(int level) => IsWeapon ? WeaponDamage * LevelMultiplier(level) : 0f;

        public static readonly IReadOnlyList<ItemBase> Weapons = new[]
        {
            Weapon("Longsword", Attribute.Strength, DamageType.Physical),
            Weapon("Greataxe", Attribute.Strength, DamageType.Physical, 1.15f, 0.85f),
            Weapon("Warhammer", Attribute.Strength, DamageType.Physical, 1.1f, 0.9f),
            Weapon("Shortbow", Attribute.Dexterity, DamageType.Physical, 0.9f, 1.15f),
            Weapon("Longbow", Attribute.Dexterity, DamageType.Physical),
            Weapon("Twin Daggers", Attribute.Dexterity, DamageType.Physical, 0.85f, 1.25f),
            Weapon("Fire Staff", Attribute.Intelligence, DamageType.Fire),
            Weapon("Frost Rod", Attribute.Intelligence, DamageType.Frost, 0.95f, 1.05f),
            Weapon("Storm Wand", Attribute.Intelligence, DamageType.Lightning, 0.9f, 1.15f),
            Weapon("Holy Sceptre", Attribute.Spirit, DamageType.Shadow),
            Weapon("Censer", Attribute.Spirit, DamageType.Fire, 0.9f, 1.1f),
        };

        public static readonly IReadOnlyList<ItemBase> Armours = new[]
        {
            Armour("Plate Helm", ItemSlot.Head, ArmourWeight.Heavy), Armour("Plate Cuirass", ItemSlot.Chest, ArmourWeight.Heavy), Armour("Gauntlets", ItemSlot.Hands, ArmourWeight.Heavy), Armour("Plate Greaves", ItemSlot.Legs, ArmourWeight.Heavy), Armour("Sabatons", ItemSlot.Feet, ArmourWeight.Heavy),
            Armour("Leather Hood", ItemSlot.Head, ArmourWeight.Medium), Armour("Brigandine", ItemSlot.Chest, ArmourWeight.Medium), Armour("Leather Gloves", ItemSlot.Hands, ArmourWeight.Medium), Armour("Studded Leggings", ItemSlot.Legs, ArmourWeight.Medium), Armour("Riding Boots", ItemSlot.Feet, ArmourWeight.Medium),
            Armour("Silk Cowl", ItemSlot.Head, ArmourWeight.Light), Armour("Woven Robe", ItemSlot.Chest, ArmourWeight.Light), Armour("Silk Wraps", ItemSlot.Hands, ArmourWeight.Light), Armour("Cloth Trousers", ItemSlot.Legs, ArmourWeight.Light), Armour("Soft Shoes", ItemSlot.Feet, ArmourWeight.Light),
        };

        /// <summary>Every base in the game: weapons, armour, jewellery. Names are unique across all three.</summary>
        public static IEnumerable<ItemBase> All => Weapons.Concat(Armours).Concat(Jewellery);
        /// <summary>The base with this exact name, or null. Saved profiles store items by base name.</summary>
        public static ItemBase ByName(string name) => All.FirstOrDefault(b => b.Name == name);

        public static readonly IReadOnlyList<ItemBase> Jewellery = new[]
        {
            Jewel("Amulet", ItemSlot.Amulet), Jewel("Ring", ItemSlot.Ring),
        };
    }
}
