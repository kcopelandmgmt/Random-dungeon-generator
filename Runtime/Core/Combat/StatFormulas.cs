using System;

namespace DungeonGen.Core.Combat
{
    /// <summary>
    /// Every number that turns attributes and gear into stats, in one place. Linear in the
    /// attribute, anchored so that all-10 attributes in tier-1 medium gear is exactly the
    /// <see cref="CombatBaseline"/> player (1000 health, 100 DPS with a 1.0 kit).
    /// </summary>
    public static class StatFormulas
    {
        public const int AverageAttribute = 10;

        // --- Per point above/below 10 --------------------------------------------------
        public const float HealthPerCon = 0.05f;        // +5% health per point
        public const float DamagePerPrimary = 0.03f;    // +3% damage per point of the kit's primary attribute
        public const float HealingPerSpirit = 0.05f;
        public const float CritPerDex = 0.005f;         // +0.5% crit chance per point
        public const float AttackSpeedPerDex = 0.02f;
        public const float MoveSpeedPerDex = 0.01f;
        public const float StaminaPerDex = 0.03f;
        public const float ManaPerInt = 0.05f;
        public const float MagicResistPerSpirit = 2f;   // rating added to every non-physical resistance
        public const float RegenPerSpirit = 0.04f;
        public const float CarryPerStr = 2f;            // kg

        // --- Bases -----------------------------------------------------------------------
        public const float BaseHealth = 1000f;
        public const float BaseStamina = 100f;
        public const float BaseMana = 100f;
        public const float BaseCrit = 0.05f;
        public const float BaseCarryKg = 30f;

        // --- Mitigation ------------------------------------------------------------------
        public const float MitigationConstant = 100f;
        public const float MitigationCap = 0.75f;

        public static float Mitigation(float rating) => Mitigation(rating, MitigationCap);

        public static float Mitigation(float rating, float cap)
        {
            if (rating >= 0f) return Math.Min(cap, rating / (rating + MitigationConstant));
            float r = -rating;                                   // negative rating: take extra damage, uncapped symmetric curve
            return -(r / (r + MitigationConstant));
        }

        // --- Gear ----------------------------------------------------------------------
        /// <summary>Armour rating of a full set by weight at tier 1. Heavy = 50% physical mitigation, Light = 23%.</summary>
        public static float BaseArmour(ArmourWeight w) => w == ArmourWeight.Heavy ? 100f : w == ArmourWeight.Medium ? 60f : 30f;
        /// <summary>Resistance rating a set gives to every non-physical type at tier 1. Light armour is enchanted cloth; heavy is iron.</summary>
        public static float BaseResist(ArmourWeight w) => w == ArmourWeight.Heavy ? 0f : w == ArmourWeight.Medium ? 10f : 25f;
        /// <summary>Move speed multiplier by weight.</summary>
        public static float MoveSpeedFor(ArmourWeight w) => w == ArmourWeight.Heavy ? 0.90f : w == ArmourWeight.Medium ? 0.95f : 1f;
        /// <summary>Gear tier 0..4 scales armour, resistances, weapon damage and adds attribute points. Tier 3 ≈ the "good gear" 1.4 in CombatBaseline.</summary>
        public static float GearMultiplier(int tier) => 1f + 0.13f * Math.Max(0, tier);
        public static int GearAttributePoints(int tier) => 2 * Math.Max(0, tier);

        public static float Linear(float perPoint, int value) => 1f + perPoint * (value - AverageAttribute);
    }
}
