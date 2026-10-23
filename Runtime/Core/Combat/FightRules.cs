using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core.Combat
{
    /// <summary>What an ability draws from. Physical classes spend Stamina, casters Mana, basic attacks cost nothing.</summary>
    public enum ResourceKind { None, Stamina, Mana }

    /// <summary>
    /// The first-thirty-seconds-of-a-fight rules, as data with tests. Abilities cost a resource
    /// and have a cooldown; zero health is a downed state, not death; looting is a channel; mobs
    /// go for the nearest; hard control has diminishing returns; there is no sprint; one of each unique.
    /// </summary>
    public static class FightRules
    {
        // --- Abilities: cooldowns AND a resource -----------------------------------------
        public static ResourceKind ResourceFor(ClassArchetype c) =>
            c.Role == ClassRole.Caster || c.Role == ClassRole.Support ? ResourceKind.Mana : ResourceKind.Stamina;
        public const bool BasicAttacksCostResource = false;
        /// <summary>Fraction of the pool one ability costs by slot: bolt is cheap and spammable, the zone and the mobility move are the big spends.</summary>
        public const float BoltCostFraction = 0.10f;
        public const float ZoneCostFraction = 0.30f;
        public const float MobilityCostFraction = 0.25f;
        /// <summary>Pool regenerated per second as a fraction of max, scaled by the sheet's RegenMultiplier (Spirit).</summary>
        public const float ResourceRegenPerSecond = 0.05f;
        public static float AbilityCost(float pool, AbilitySlot slot) =>
            pool * (slot == AbilitySlot.Bolt ? BoltCostFraction : slot == AbilitySlot.Zone ? ZoneCostFraction : slot == AbilitySlot.Mobility ? MobilityCostFraction : 0f);

        // --- Health never comes back on its own; resources do ------------------------------
        /// <summary>No natural health regeneration, in or out of combat. Menders, potions and lifesteal are the only healing.</summary>
        public const float HealthRegenPerSecond = 0f;
        public const bool ResourcesRegenerate = true;

        // --- Ultimates: earned, never given --------------------------------------------------
        /// <summary>One ultimate per spec. It is armed at a shrine for souls, lasts one use, and never leaves the dungeon. It competes with resurrection and consumables for the same currency.</summary>
        public const int SoulsToArmUltimate = 2;
        public const int UltimateUsesPerArming = 1;
        /// <summary>Ultimates obey full friendly fire like everything else. No exceptions.</summary>
        public const bool UltimatesObeyFriendlyFire = true;
        /// <summary>An armed ultimate shows as a soft gold aura on the character, visible to everyone in line of sight, gone the instant it is used. Things worth taking glow.</summary>
        public const bool ArmedUltimateAuraVisibleToAll = true;
        public const bool ArmedUltimateAuraThroughWalls = false;

        // --- Silver in the dungeon --------------------------------------------------------
        /// <summary>Silver drops from mob packs and chests, weighs nothing, and all of it drops on death.</summary>
        public const bool SilverDropsInDungeon = true;
        public const float SilverWeightKg = 0f;
        public const float SilverDroppedOnDeathFraction = 1f;
        /// <summary>Base silver per mob pack and per chest, scaled by loot tier (×(1 + tier)).</summary>
        public const int SilverPerMobPackBase = 15;
        public const int SilverPerChestBase = 20;
        public const int SilverPerBossChestBase = 80;
        public const int SilverPerVaultBase = 150;

        // --- Downed, not dead ------------------------------------------------------------
        /// <summary>At zero health a player is downed for this long. If nobody revives or finishes them, they die.</summary>
        public const float DownedSeconds = 60f;
        /// <summary>Any friendly player revives with this channel; damage to the reviver breaks it.</summary>
        public const float ReviveChannelSeconds = 8f;
        /// <summary>Damage dealt to a downed player, as a fraction of their max health, that finishes them. Then only a shrine and souls bring them back.</summary>
        public const float FinishDamageFraction = 0.5f;
        /// <summary>A revived player stands up with this fraction of health.</summary>
        public const float ReviveHealthFraction = 0.3f;

        // --- Looting is a channel --------------------------------------------------------
        public const float ChestOpenSeconds = 2f;
        public const float CorpseLootSeconds = 3f;
        public const float KeyPickupSeconds = 1f;
        public const bool LootInterruptedByDamage = true;

        // --- Mobs ----------------------------------------------------------------------
        /// <summary>Mobs attack the nearest player; a taunt (Sentinel's Hold the Line) overrides for its duration. Bosses keep their own targeting.</summary>
        public const bool MobsTargetNearest = true;
        public const bool TauntOverrides = true;

        // --- Control -------------------------------------------------------------------
        /// <summary>Hard control (Freeze, Stun) on a player: full, then half, then a quarter, then immune, within a 10 s window.</summary>
        public static readonly float[] HardControlDiminishing = { 1f, 0.5f, 0.25f, 0f };
        public const float DiminishingWindowSeconds = 10f;

        // --- Movement ------------------------------------------------------------------
        public const bool Sprint = false;

        // --- Uniques -------------------------------------------------------------------
        public const bool OneOfEachUnique = true;

        // --- The round's first seconds -------------------------------------------------
        /// <summary>Spawn-room doors are sealed for this long; the minimap shows the spawn room; then every door opens at once. No protection after.</summary>
        public const int StartCountdownSeconds = 10;

        // --- A new account -------------------------------------------------------------
        public const long StartingSilver = 200;
        public const int StartingHealthPotions = 2;
        /// <summary>No Tarkov pouch. Nothing you carry survives dying: not gear, not keys, not silver, not souls.</summary>
        public const int SecurePouchSlots = 0;
    }

    public enum AbilitySlot { Bolt, Zone, Mobility, Passive }

    /// <summary>
    /// Diminishing returns on hard control for one target. Apply returns the duration multiplier
    /// to use; the fourth hard control within the window lands for nothing.
    /// </summary>
    public sealed class ControlResistance
    {
        private readonly List<float> _hits = new List<float>();

        public float Apply(float now)
        {
            _hits.RemoveAll(t => now - t > FightRules.DiminishingWindowSeconds);
            int n = _hits.Count;
            _hits.Add(now);
            return n < FightRules.HardControlDiminishing.Length ? FightRules.HardControlDiminishing[n] : 0f;
        }

        public bool IsImmune(float now)
        {
            _hits.RemoveAll(t => now - t > FightRules.DiminishingWindowSeconds);
            return _hits.Count >= FightRules.HardControlDiminishing.Length - 1;
        }
    }
}
