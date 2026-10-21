using System;
using System.Linq;
using DungeonGen.Core.Items;

namespace DungeonGen.Core.Combat
{
    /// <summary>
    /// A character sheet: attributes + class kit + gear → every derived stat. Pure data; the
    /// game applies it. Build one with <see cref="For(ClassArchetype, Equipment, CombatBaseline)"/>
    /// from real items, or <see cref="For(ClassArchetype, int, CombatBaseline)"/> for the abstract
    /// tier the boss tuning reasons about.
    /// </summary>
    public sealed class PlayerStats
    {
        public ClassArchetype Class { get; }
        public Attributes Attributes { get; }
        public GearSummary Gear { get; }
        public int GearTier { get; }

        public float Health { get; }
        public float Stamina { get; }
        public float Mana { get; }
        /// <summary>Sustained DPS with the class kit: CombatBaseline.PlayerDps × this.</summary>
        public float DamageMultiplier { get; }
        public float Dps { get; }
        /// <summary>What basic attacks deal, by type. Added-damage affixes widen it.</summary>
        public DamageProfile AttackProfile => Gear.AttackProfile;
        public float HealingMultiplier { get; }
        public float CritChance { get; }
        public float AttackSpeed { get; }
        public float MoveSpeed { get; }
        public float RegenMultiplier { get; }
        public float CarryKg { get; }
        public float ArmourRating { get; }
        public Resistances Resistances { get; }
        /// <summary>Fraction of damage dealt returned as health (unique-only).</summary>
        public float Lifesteal { get; }
        /// <summary>Fraction of damage taken reflected, and as what (unique-only).</summary>
        public float Thorns { get; }
        public DamageType ThornsType { get; }
        /// <summary>Triggered legendary effects for the game to run: procs, summons, second wind, phasing.</summary>
        public System.Collections.Generic.IReadOnlyList<LegendaryAffix> Legendaries => Gear.Legendaries;

        public float PhysicalMitigation => StatFormulas.Mitigation(ArmourRating, StatFormulas.MitigationCap + Gear.ResistCapBonus / 100f);

        private PlayerStats(ClassArchetype cls, GearSummary gear, int tier, CombatBaseline baseline)
        {
            Class = cls; Gear = gear; GearTier = tier;
            var attrs = cls.BaseAttributes + gear.AttributeBonus;
            Attributes = attrs;

            // The kit scales from the class primary unless the weapon scales from something else (a Reaver with a bow is a worse Reaver).
            var scaling = gear.WeaponScalesWith ?? cls.Primary;
            float offStat = scaling == cls.Primary ? 1f : 0.75f;

            Health = StatFormulas.BaseHealth * StatFormulas.Linear(StatFormulas.HealthPerCon, attrs.Constitution) * (1f + gear.HealthPct / 100f);
            Stamina = StatFormulas.BaseStamina * StatFormulas.Linear(StatFormulas.StaminaPerDex, attrs.Dexterity);
            Mana = StatFormulas.BaseMana * StatFormulas.Linear(StatFormulas.ManaPerInt, attrs.Intelligence);
            AttackSpeed = StatFormulas.Linear(StatFormulas.AttackSpeedPerDex, attrs.Dexterity) * gear.WeaponSpeed * (1f + gear.AttackSpeedPct / 100f);
            DamageMultiplier = cls.KitCoefficient * StatFormulas.Linear(StatFormulas.DamagePerPrimary, attrs.Get(scaling)) * offStat * gear.WeaponDamage * AttackSpeed / StatFormulas.Linear(StatFormulas.AttackSpeedPerDex, cls.BaseAttributes.Dexterity);
            Dps = baseline.PlayerDps * DamageMultiplier;
            HealingMultiplier = StatFormulas.Linear(StatFormulas.HealingPerSpirit, attrs.Spirit) * (1f + gear.HealingPct / 100f) * StatFormulas.GearMultiplier(tier > 0 && gear.HealingPct == 0f ? tier : 0);
            CritChance = StatFormulas.BaseCrit + StatFormulas.CritPerDex * (attrs.Dexterity - StatFormulas.AverageAttribute) + gear.CritPct / 100f;
            MoveSpeed = StatFormulas.Linear(StatFormulas.MoveSpeedPerDex, attrs.Dexterity) * gear.MoveSpeedFactor * (1f + gear.MoveSpeedPct / 100f);
            RegenMultiplier = StatFormulas.Linear(StatFormulas.RegenPerSpirit, attrs.Spirit);
            CarryKg = StatFormulas.BaseCarryKg + StatFormulas.CarryPerStr * attrs.Strength + gear.CarryKg;
            ArmourRating = gear.ArmourRating;

            Resistances = new Resistances { CapBonus = gear.ResistCapBonus };
            foreach (DamageType t in Enum.GetValues(typeof(DamageType))) Resistances.Set(t, gear.Resistances.Rating(t));
            Resistances.AddAllMagic(StatFormulas.MagicResistPerSpirit * (attrs.Spirit - StatFormulas.AverageAttribute));
            Lifesteal = gear.LifestealPct / 100f;
            Thorns = gear.ThornsPct / 100f;
            ThornsType = gear.ThornsType;
        }

        /// <summary>Sheet for a class at an abstract gear tier (0 = starting gear, 4 = vault / final boss drops). What the boss tuning assumes.</summary>
        public static PlayerStats For(ClassArchetype cls, int gearTier = 0, CombatBaseline baseline = null) =>
            new PlayerStats(cls, GearSummary.ForTier(cls.Armour, gearTier, cls.Primary), gearTier, baseline ?? new CombatBaseline());

        /// <summary>Sheet for a class wearing real items.</summary>
        public static PlayerStats For(ClassArchetype cls, Equipment equipment, CombatBaseline baseline = null) =>
            new PlayerStats(cls, equipment.Summarise(), 0, baseline ?? new CombatBaseline());

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
            $"{Class.Name} [{Attributes}] HP {Health:0} DPS {Dps:0} ({AttackProfile}) heal x{HealingMultiplier:0.00} crit {CritChance * 100:0}% spd x{MoveSpeed:0.00} armour {ArmourRating:0} ({PhysicalMitigation * 100:0}%) carry {CarryKg:0}kg | {Resistances}{(Lifesteal > 0 ? $" | lifesteal {Lifesteal * 100:0}%" : "")}{(Thorns > 0 ? $" | thorns {Thorns * 100:0}% {ThornsType}" : "")}{(Legendaries.Count > 0 ? " | ★ " + string.Join("; ", Legendaries.Select(l => l.Name)) : "")}";
    }
}
