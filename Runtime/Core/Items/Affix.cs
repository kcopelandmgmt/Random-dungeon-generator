using Attribute = DungeonGen.Core.Combat.Attribute;
using System;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Combat;

namespace DungeonGen.Core.Items
{
    public enum AffixKind
    {
        IncreasedDamage,   // % weapon damage
        AddedDamage,       // % of weapon damage added as a type (changes the attack profile)
        IncreasedArmour,   // % armour on the piece
        Health,            // % max health
        Resist,            // + rating to one type
        AllResist,         // + rating to every non-physical type
        Attribute,         // + points to one attribute
        Crit,              // + crit chance
        AttackSpeed,       // % attack speed
        MoveSpeed,         // % move speed
        Carry,             // + kg
        Healing,           // % healing power
    }

    /// <summary>A family of affix: what it does, where it can roll, and the value ranges per tier (T1 weakest .. T4 strongest).</summary>
    public sealed class AffixDefinition
    {
        public AffixKind Kind { get; }
        public bool IsPrefix { get; }
        /// <summary>Name fragment used in Magic item names: "Sharp Longsword", "Ring of the Bear".</summary>
        public string Word { get; }
        public IReadOnlyList<ItemSlot> Slots { get; }
        /// <summary>Min/max value per tier index 0..3.</summary>
        public IReadOnlyList<(float min, float max)> Tiers { get; }
        /// <summary>Set for Resist / AddedDamage families.</summary>
        public DamageType? Type { get; }
        /// <summary>Set for Attribute families.</summary>
        public Attribute? Attribute { get; }
        public int Weight { get; }

        public AffixDefinition(AffixKind kind, bool prefix, string word, ItemSlot[] slots, (float, float)[] tiers, DamageType? type = null, Attribute? attribute = null, int weight = 10)
        {
            Kind = kind; IsPrefix = prefix; Word = word; Slots = slots; Tiers = tiers; Type = type; Attribute = attribute; Weight = weight;
        }

        /// <summary>Two affixes conflict if they are the same family (same kind and same type/attribute).</summary>
        public bool SameFamily(AffixDefinition other) => Kind == other.Kind && Type == other.Type && Attribute == other.Attribute;

        private static readonly ItemSlot[] WeaponOnly = { ItemSlot.Weapon };
        private static readonly ItemSlot[] ArmourOnly = { ItemSlot.Head, ItemSlot.Chest, ItemSlot.Hands, ItemSlot.Legs, ItemSlot.Feet };
        private static readonly ItemSlot[] ArmourAndJewels = { ItemSlot.Head, ItemSlot.Chest, ItemSlot.Hands, ItemSlot.Legs, ItemSlot.Feet, ItemSlot.Amulet, ItemSlot.Ring };
        private static readonly ItemSlot[] Any = { ItemSlot.Weapon, ItemSlot.Head, ItemSlot.Chest, ItemSlot.Hands, ItemSlot.Legs, ItemSlot.Feet, ItemSlot.Amulet, ItemSlot.Ring };
        private static readonly ItemSlot[] Body = { ItemSlot.Chest, ItemSlot.Legs, ItemSlot.Amulet, ItemSlot.Ring };
        private static readonly ItemSlot[] Quick = { ItemSlot.Feet, ItemSlot.Ring };
        private static readonly ItemSlot[] Hands = { ItemSlot.Weapon, ItemSlot.Hands, ItemSlot.Ring };

        public static readonly IReadOnlyList<AffixDefinition> Pool = Build();

        private static List<AffixDefinition> Build()
        {
            var list = new List<AffixDefinition>
            {
                new AffixDefinition(AffixKind.IncreasedDamage, true, "Sharp", WeaponOnly, new[] { (6f, 9f), (10f, 14f), (15f, 20f), (21f, 25f) }, weight: 14),
                new AffixDefinition(AffixKind.IncreasedArmour, true, "Sturdy", ArmourOnly, new[] { (8f, 12f), (13f, 20f), (21f, 30f), (31f, 40f) }, weight: 14),
                new AffixDefinition(AffixKind.Health, true, "Hale", Body, new[] { (3f, 4f), (5f, 6f), (7f, 8f), (9f, 10f) }, weight: 12),
                new AffixDefinition(AffixKind.AllResist, true, "Warded", ArmourAndJewels, new[] { (4f, 6f), (7f, 10f), (11f, 15f), (16f, 20f) }, weight: 6),
                new AffixDefinition(AffixKind.Crit, false, "of Precision", Hands, new[] { (1f, 1f), (2f, 2f), (3f, 3f), (4f, 5f) }, weight: 8),
                new AffixDefinition(AffixKind.AttackSpeed, false, "of Haste", Hands, new[] { (3f, 4f), (5f, 6f), (7f, 8f), (9f, 10f) }, weight: 8),
                new AffixDefinition(AffixKind.MoveSpeed, false, "of the Fox", Quick, new[] { (2f, 2f), (3f, 3f), (4f, 4f), (5f, 6f) }, weight: 8),
                new AffixDefinition(AffixKind.Carry, false, "of the Mule", ArmourAndJewels, new[] { (4f, 6f), (7f, 9f), (10f, 12f), (13f, 16f) }, weight: 8),
                new AffixDefinition(AffixKind.Healing, false, "of Mending", new[] { ItemSlot.Weapon, ItemSlot.Amulet, ItemSlot.Hands }, new[] { (5f, 7f), (8f, 10f), (11f, 13f), (14f, 16f) }, weight: 6),
            };
            string[] typeWords = { "", "Flaming", "Freezing", "Shocking", "Venomous", "Shadowed" };
            string[] resistWords = { "", "of Embers", "of Rime", "of Grounding", "of Antidote", "of Candles" };
            foreach (DamageType t in Enum.GetValues(typeof(DamageType)))
            {
                if (t == DamageType.Physical) continue;
                list.Add(new AffixDefinition(AffixKind.AddedDamage, true, typeWords[(int)t], WeaponOnly, new[] { (6f, 8f), (9f, 12f), (13f, 16f), (17f, 20f) }, type: t, weight: 5));
                list.Add(new AffixDefinition(AffixKind.Resist, false, resistWords[(int)t], ArmourAndJewels, new[] { (8f, 12f), (13f, 20f), (21f, 30f), (31f, 45f) }, type: t, weight: 6));
            }
            string[] attrWords = { "of the Bear", "of the Hawk", "of the Owl", "of the Oak", "of the Saint" };
            foreach (Attribute a in Enum.GetValues(typeof(Attribute)))
                list.Add(new AffixDefinition(AffixKind.Attribute, false, attrWords[(int)a], Any, new[] { (1f, 1f), (1f, 2f), (2f, 3f), (3f, 4f) }, attribute: a, weight: 8));
            return list;
        }
    }

    /// <summary>One rolled affix on one item.</summary>
    public sealed class AffixRoll
    {
        public AffixDefinition Definition { get; }
        public int Tier { get; }     // 1..4
        public float Value { get; }

        public AffixRoll(AffixDefinition def, int tier, float value) { Definition = def; Tier = tier; Value = value; }

        public override string ToString()
        {
            var d = Definition;
            switch (d.Kind)
            {
                case AffixKind.IncreasedDamage: return $"+{Value:0}% damage";
                case AffixKind.AddedDamage: return $"+{Value:0}% of damage as {d.Type}";
                case AffixKind.IncreasedArmour: return $"+{Value:0}% armour";
                case AffixKind.Health: return $"+{Value:0}% health";
                case AffixKind.Resist: return $"+{Value:0} {d.Type} resistance";
                case AffixKind.AllResist: return $"+{Value:0} to all resistances";
                case AffixKind.Attribute: return $"+{Value:0} {d.Attribute}";
                case AffixKind.Crit: return $"+{Value:0}% crit chance";
                case AffixKind.AttackSpeed: return $"+{Value:0}% attack speed";
                case AffixKind.MoveSpeed: return $"+{Value:0}% move speed";
                case AffixKind.Carry: return $"+{Value:0} kg carry";
                case AffixKind.Healing: return $"+{Value:0}% healing";
                default: return $"{d.Kind} {Value}";
            }
        }
    }
}
