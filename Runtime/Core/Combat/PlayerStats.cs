using System;

namespace DungeonGen.Core.Combat
{
    /// <summary>
    /// A character sheet: attributes + class kit + gear → every derived stat. Pure data; the
    /// game applies it. Build one with <see cref="For"/> and read the properties.
    /// </summary>
    public sealed class PlayerStats
    {
        public ClassArchetype Class { get; }
        public Attributes Attributes { get; }
        public ArmourWeight Armour { get; }
        public int GearTier { get; }

        public float Health { get; }
        public float Stamina { get; }
        public float Mana { get; }
        /// <summary>Sustained DPS with the class kit: CombatBaseline.PlayerDps × this.</summary>
        public float DamageMultiplier { get; }
        public float Dps { get; }
        public float HealingMultiplier { get; }
        public float CritChance { get; }
        public float AttackSpeed { get; }
        public float MoveSpeed { get; }
        public float RegenMultiplier { get; }
        public float CarryKg { get; }
        public float ArmourRating { get; }
        public Resistances Resistances { get; }

        public float PhysicalMitigation => StatFormulas.Mitigation(ArmourRating);

        private PlayerStats(ClassArchetype cls, Attributes attrs, ArmourWeight armour, int tier, CombatBaseline baseline)
        {
            Class = cls; Attributes = attrs; Armour = armour; GearTier = tier;
            float gear = StatFormulas.GearMultiplier(tier);

            Health = StatFormulas.BaseHealth * StatFormulas.Linear(StatFormulas.HealthPerCon, attrs.Constitution);
            Stamina = StatFormulas.BaseStamina * StatFormulas.Linear(StatFormulas.StaminaPerDex, attrs.Dexterity);
            Mana = StatFormulas.BaseMana * StatFormulas.Linear(StatFormulas.ManaPerInt, attrs.Intelligence);
            DamageMultiplier = cls.KitCoefficient * StatFormulas.Linear(StatFormulas.DamagePerPrimary, attrs.Get(cls.Primary)) * gear;
            Dps = baseline.PlayerDps * DamageMultiplier;
            HealingMultiplier = StatFormulas.Linear(StatFormulas.HealingPerSpirit, attrs.Spirit) * gear;
            CritChance = StatFormulas.BaseCrit + StatFormulas.CritPerDex * (attrs.Dexterity - StatFormulas.AverageAttribute);
            AttackSpeed = StatFormulas.Linear(StatFormulas.AttackSpeedPerDex, attrs.Dexterity);
            MoveSpeed = StatFormulas.Linear(StatFormulas.MoveSpeedPerDex, attrs.Dexterity) * StatFormulas.MoveSpeedFor(armour);
            RegenMultiplier = StatFormulas.Linear(StatFormulas.RegenPerSpirit, attrs.Spirit);
            CarryKg = StatFormulas.BaseCarryKg + StatFormulas.CarryPerStr * attrs.Strength;
            ArmourRating = StatFormulas.BaseArmour(armour) * gear;

            Resistances = new Resistances();
            Resistances.AddAllMagic(StatFormulas.BaseResist(armour) * gear);
            Resistances.AddAllMagic(StatFormulas.MagicResistPerSpirit * (attrs.Spirit - StatFormulas.AverageAttribute));
        }

        /// <summary>Sheet for a class at a gear tier (0 = starting gear, 4 = vault / final boss drops). Gear adds attribute points to the class primary and Constitution.</summary>
        public static PlayerStats For(ClassArchetype cls, int gearTier = 0, CombatBaseline baseline = null)
        {
            baseline ??= new CombatBaseline();
            var attrs = cls.BaseAttributes;
            int bonus = StatFormulas.GearAttributePoints(gearTier);
            attrs = attrs + Bonus(cls.Primary, bonus / 2 + bonus % 2) + Bonus(Attribute.Constitution, bonus / 2);
            return new PlayerStats(cls, attrs, cls.Armour, gearTier, baseline);
        }

        private static Attributes Bonus(Attribute a, int points)
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

        /// <summary>Damage actually taken from a hit of the given raw amount and type.</summary>
        public float TakeHit(float raw, DamageType type) =>
            type == DamageType.Physical ? raw * (1f - PhysicalMitigation) : Resistances.DamageTaken(raw, type);

        public float TakeHit(float raw, DamageProfile profile)
        {
            float total = 0f;
            foreach (var t in profile.Types) total += TakeHit(raw * profile.Fraction(t), t);
            return total;
        }

        public override string ToString() =>
            $"{Class.Name} T{GearTier} [{Attributes}] HP {Health:0} DPS {Dps:0} heal x{HealingMultiplier:0.00} crit {CritChance * 100:0}% armour {ArmourRating:0} ({PhysicalMitigation * 100:0}%) carry {CarryKg:0}kg | {Resistances}";
    }
}
