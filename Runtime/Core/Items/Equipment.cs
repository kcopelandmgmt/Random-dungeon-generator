using Attribute = DungeonGen.Core.Combat.Attribute;
using System;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Combat;

namespace DungeonGen.Core.Items
{
    /// <summary>Everything the character sheet needs from gear, whatever the gear is. Built from real items or from an abstract tier.</summary>
    public sealed class GearSummary
    {
        public float WeaponDamage = 1f;          // multiplier on baseline DPS
        public float WeaponSpeed = 1f;
        public Attribute? WeaponScalesWith;      // null = use the class primary
        public ItemBase WeaponBase;              // what is in hand; decides the spec
        public DamageProfile AttackProfile = DamageProfile.Pure(DamageType.Physical);
        public float ArmourRating;
        public Resistances Resistances = new Resistances();
        public Attributes AttributeBonus;
        public float HealthPct, CritPct, AttackSpeedPct, MoveSpeedPct, HealingPct, CarryKg;
        /// <summary>0 light .. 2 heavy, weighted by slot share; sets the move-speed penalty.</summary>
        public float AverageWeight = 1f;

        // Legendary (unique-only) effects. Numeric ones fold into the sheet; the rest are handed to the game as triggers.
        public float LifestealPct, ThornsPct, ResistCapBonus;
        public DamageType ThornsType = DamageType.Physical;
        public List<LegendaryAffix> Legendaries = new List<LegendaryAffix>();

        public float MoveSpeedFactor => AverageWeight <= 0.5f ? 1f : AverageWeight < 1.5f ? 0.95f : 0.90f;

        /// <summary>The abstract "a tier-t set" used by boss tuning: tier-t Normal bases plus the attribute points gear is expected to carry at that tier.</summary>
        public static GearSummary ForTier(ArmourWeight weight, int tier, Attribute primary)
        {
            float g = StatFormulas.GearMultiplier(tier);
            var s = new GearSummary
            {
                WeaponDamage = g,
                ArmourRating = StatFormulas.BaseArmour(weight) * g,
                AverageWeight = (int)weight,
            };
            s.Resistances.AddAllMagic(StatFormulas.BaseResist(weight) * g);
            int bonus = StatFormulas.GearAttributePoints(tier);
            s.AttributeBonus = Plus(primary, bonus / 2 + bonus % 2) + Plus(Attribute.Constitution, bonus / 2);
            return s;
        }

        internal static Attributes Plus(Attribute a, int points)
        {
            switch (a)
            {
                case Attribute.Strength: return new Attributes(points, 0, 0, 0, 0);
                case Attribute.Dexterity: return new Attributes(0, points, 0, 0, 0);
                case Attribute.Intelligence: return new Attributes(0, 0, points, 0, 0);
                case Attribute.Constitution: return new Attributes(0, 0, 0, points, 0);
                default: return new Attributes(0, 0, 0, 0, points);
            }
        }
    }

    /// <summary>What a character is wearing. Nine slots; two rings.</summary>
    public sealed class Equipment
    {
        public Item Weapon, Head, Chest, Hands, Legs, Feet, Amulet, Ring1, Ring2;

        public IEnumerable<Item> Items => new[] { Weapon, Head, Chest, Hands, Legs, Feet, Amulet, Ring1, Ring2 }.Where(i => i != null);

        /// <summary>Kilograms worn. Carried loot is on top of this against the sheet's CarryKg.</summary>
        public float WornWeightKg => Items.Sum(i => i.WeightKg);

        /// <summary>True if wearing this would break the one-of-each-unique rule.</summary>
        public bool WouldDuplicateUnique(Item item) =>
            item.Unique != null && Items.Any(i => i.Unique == item.Unique && !ReferenceEquals(i, item));

        /// <summary>Put an item in its slot (second ring slot if the first is taken, else replaces the first). Returns what was displaced. Throws on a second copy of a unique.</summary>
        public Item Equip(Item item)
        {
            if (WouldDuplicateUnique(item)) throw new InvalidOperationException($"Already wearing {item.Unique.Name}: one of each unique.");
            Item old;
            switch (item.Base.Slot)
            {
                case ItemSlot.Weapon: old = Weapon; Weapon = item; break;
                case ItemSlot.Head: old = Head; Head = item; break;
                case ItemSlot.Chest: old = Chest; Chest = item; break;
                case ItemSlot.Hands: old = Hands; Hands = item; break;
                case ItemSlot.Legs: old = Legs; Legs = item; break;
                case ItemSlot.Feet: old = Feet; Feet = item; break;
                case ItemSlot.Amulet: old = Amulet; Amulet = item; break;
                default:
                    if (Ring1 == null) { old = null; Ring1 = item; }
                    else if (Ring2 == null) { old = null; Ring2 = item; }
                    else { old = Ring1; Ring1 = item; }
                    break;
            }
            return old;
        }

        /// <summary>Take this exact item off, whichever slot holds it. False if it is not worn.</summary>
        public bool Remove(Item item)
        {
            if (item == null) return false;
            if (ReferenceEquals(Weapon, item)) { Weapon = null; return true; }
            if (ReferenceEquals(Head, item)) { Head = null; return true; }
            if (ReferenceEquals(Chest, item)) { Chest = null; return true; }
            if (ReferenceEquals(Hands, item)) { Hands = null; return true; }
            if (ReferenceEquals(Legs, item)) { Legs = null; return true; }
            if (ReferenceEquals(Feet, item)) { Feet = null; return true; }
            if (ReferenceEquals(Amulet, item)) { Amulet = null; return true; }
            if (ReferenceEquals(Ring1, item)) { Ring1 = null; return true; }
            if (ReferenceEquals(Ring2, item)) { Ring2 = null; return true; }
            return false;
        }

        /// <summary>True if this exact item is worn.</summary>
        public bool IsWearing(Item item) => item != null && Items.Any(i => ReferenceEquals(i, item));

        /// <summary>A full set of Normal items of one weight at one level: the "store-bought" kit a class starts with.</summary>
        public static Equipment StandardSet(ArmourWeight weight, int level, Attribute primary)
        {
            var e = new Equipment();
            foreach (var a in ItemBase.Armours.Where(a => a.Weight == weight))
                e.Equip(new Item(a, level, Rarity.Normal, a.Name, new List<AffixRoll>(), new List<AffixRoll>()));
            var w = ItemBase.Weapons.First(x => x.ScalesWith == primary && Math.Abs(x.WeaponDamage - 1f) < 0.01f);
            e.Equip(new Item(w, level, Rarity.Normal, w.Name, new List<AffixRoll>(), new List<AffixRoll>()));
            return e;
        }

        public GearSummary Summarise()
        {
            var s = new GearSummary();
            float weightSum = 0f, shareSum = 0f;
            foreach (var it in Items)
            {
                if (it.Condition.IsBroken || it.Condition.IsDestroyed)
                {
                    if (it.Base.IsWeapon) s.WeaponDamage = 0.5f;   // fighting with the broken half
                    continue;
                }
                if (it.Base.IsWeapon)
                {
                    s.WeaponDamage = it.WeaponDamage;
                    s.WeaponSpeed = it.Base.WeaponSpeed;
                    s.WeaponScalesWith = it.Base.ScalesWith;
                    s.WeaponBase = it.Base;
                    var parts = new List<(DamageType, float)> { (it.Base.AttackType, 1f) };
                    foreach (var a in it.Affixes.Where(a => a.Definition.Kind == AffixKind.AddedDamage))
                        parts.Add((a.Definition.Type.Value, a.Value / 100f));
                    s.AttackProfile = new DamageProfile(parts.ToArray());
                    s.WeaponDamage *= parts.Sum(p => p.Item2);   // added damage is extra, not converted
                }
                if (it.Base.IsArmour)
                {
                    s.ArmourRating += it.Armour;
                    s.Resistances.AddAllMagic(it.BaseResist);
                    float share = ItemBase.SlotShare(it.Base.Slot);
                    weightSum += (int)it.Base.Weight * share;
                    shareSum += share;
                }
                var leg = it.Unique?.Legendary;
                if (leg != null)
                {
                    s.Legendaries.Add(leg);
                    switch (leg.Kind)
                    {
                        case LegendaryKind.Lifesteal: s.LifestealPct += leg.Magnitude; break;
                        case LegendaryKind.Thorns: s.ThornsPct += leg.Magnitude; s.ThornsType = leg.Type; break;
                        case LegendaryKind.ResistCap: s.ResistCapBonus += leg.Magnitude; break;
                    }
                }
                foreach (var a in it.Affixes)
                {
                    var d = a.Definition;
                    switch (d.Kind)
                    {
                        case AffixKind.Health: s.HealthPct += a.Value; break;
                        case AffixKind.Resist: s.Resistances.Add(d.Type.Value, a.Value); break;
                        case AffixKind.AllResist: s.Resistances.AddAllMagic(a.Value); break;
                        case AffixKind.Attribute: s.AttributeBonus = s.AttributeBonus + GearSummary.Plus(d.Attribute.Value, (int)a.Value); break;
                        case AffixKind.Crit: s.CritPct += a.Value; break;
                        case AffixKind.AttackSpeed: s.AttackSpeedPct += a.Value; break;
                        case AffixKind.MoveSpeed: s.MoveSpeedPct += a.Value; break;
                        case AffixKind.Carry: s.CarryKg += a.Value; break;
                        case AffixKind.Healing: s.HealingPct += a.Value; break;
                    }
                }
            }
            s.AverageWeight = shareSum > 0f ? weightSum / shareSum : 1f;
            return s;
        }

        public override string ToString() => string.Join("\n", Items.Select(i => "  " + i));
    }
}
