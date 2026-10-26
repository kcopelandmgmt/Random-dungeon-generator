using System.Collections.Generic;

namespace DungeonGen.Core.Combat
{
    /// <summary>
    /// An ordinary enemy type. Each mob deals one dominant damage type and resists it, and is weak
    /// to the type that opposes it, so a team's resistance choices matter room by room and a
    /// Elementalist is not the answer to everything.
    /// </summary>
    public sealed class MobArchetype
    {
        public string Name { get; }
        public DamageProfile Damage { get; }
        public DamageType Resists { get; }
        public DamageType WeakTo { get; }
        /// <summary>Health and DPS as a fraction of one baseline player. A pack of ~4 at 0.25 / 0.2 is about one player-equivalent.</summary>
        public float HealthFraction { get; }
        public float DpsFraction { get; }
        public string Behaviour { get; }
        /// <summary>Status its hits apply: the dominant element's effect, or Bleed for a physical brawler. Null = none.</summary>
        public StatusEffectDefinition OnHit { get; }

        public MobArchetype(string name, DamageProfile damage, DamageType resists, DamageType weakTo, float health, float dps, string behaviour, StatusKind? onHit = null)
        {
            Name = name; Damage = damage; Resists = resists; WeakTo = weakTo; HealthFraction = health; DpsFraction = dps; Behaviour = behaviour;
            OnHit = onHit.HasValue ? StatusEffectDefinition.All[onHit.Value]
                  : StatusEffectDefinition.OnHitFor(damage.Dominant) ?? StatusEffectDefinition.All[StatusKind.Bleed];   // physical brawlers bleed
        }

        public Resistances BuildResistances()
        {
            var r = new Resistances();
            r.Set(Resists, 150f);   // 60% mitigation vs its own element
            r.Set(WeakTo, -100f);   // takes +50% from its opposite
            return r;
        }

        /// <summary>Eight mob types, dominant damage types spread over all six, physical doubled because it is the default.</summary>
        public static readonly IReadOnlyList<MobArchetype> DefaultRoster = new[]
        {
            new MobArchetype("Bandit", DamageProfile.Pure(DamageType.Physical), DamageType.Physical, DamageType.Shadow, 0.30f, 0.20f,
                "Humanoid; uses doors and corridors like a player and loots corpses if left alone.", StatusKind.Bleed),
            new MobArchetype("Hollow Knight", new DamageProfile((DamageType.Physical, 0.8f), (DamageType.Shadow, 0.2f)), DamageType.Physical, DamageType.Fire, 0.45f, 0.25f,
                "Slow, armoured, blocks a doorway as well as a Warden does."),
            new MobArchetype("Skitterer", new DamageProfile((DamageType.Poison, 0.7f), (DamageType.Physical, 0.3f)), DamageType.Poison, DamageType.Fire, 0.12f, 0.12f,
                "Fast and numerous; comes in packs of six or more. The Hive Matron's brood."),
            new MobArchetype("Ember Wisp", DamageProfile.Pure(DamageType.Fire), DamageType.Fire, DamageType.Frost, 0.18f, 0.22f,
                "Floats, ignores floor hazards, lights the room: you cannot hide near one."),
            new MobArchetype("Frostbound", new DamageProfile((DamageType.Frost, 0.7f), (DamageType.Physical, 0.3f)), DamageType.Frost, DamageType.Fire, 0.35f, 0.18f,
                "Slows on hit; three of them stop a retreat."),
            new MobArchetype("Storm Acolyte", DamageProfile.Pure(DamageType.Lightning), DamageType.Lightning, DamageType.Physical, 0.20f, 0.28f,
                "Caster; stands at the back of the room and chains lightning between grouped players."),
            new MobArchetype("Carrion Crawler", new DamageProfile((DamageType.Poison, 0.5f), (DamageType.Physical, 0.5f)), DamageType.Poison, DamageType.Lightning, 0.28f, 0.16f,
                "Eats corpses, including their loot, if nobody claims them in time."),
            new MobArchetype("Gravewalker", new DamageProfile((DamageType.Shadow, 0.7f), (DamageType.Physical, 0.3f)), DamageType.Shadow, DamageType.Fire, 0.32f, 0.20f,
                "Undead; the Bone Choir's retinue. Rises again once unless burned."),
        };
    }

    /// <summary>A group of one mob type stationed in a room.</summary>
    public sealed class MobPack
    {
        public MobArchetype Mob { get; }
        public int Count { get; }
        /// <summary>Tier multiplier on health and DPS from the room's danger.</summary>
        public float TierMultiplier { get; }

        public MobPack(MobArchetype mob, int count, float tierMultiplier) { Mob = mob; Count = count; TierMultiplier = tierMultiplier; }

        public float TotalHealth(CombatBaseline b) => Count * Mob.HealthFraction * TierMultiplier * b.PlayerHealth;
        public float TotalDps(CombatBaseline b) => Count * Mob.DpsFraction * TierMultiplier * b.PlayerDps;
        /// <summary>Rough difficulty in baseline players: the geometric mean of health and DPS fractions. (It was doubled until 2026-10-26; the round lab showed packs dying in 2-13 s for a few percent of a trio's health, so a mob is worth a player per point, not two.)</summary>
        public static float PlayerEquivalentsPerMob(MobArchetype m) => (float)System.Math.Sqrt(m.HealthFraction * m.DpsFraction) * MobWorth;
        /// <summary>Player-equivalents per point of sqrt(health x dps). Lower means more mobs for the same danger budget.</summary>
        public const float MobWorth = 0.5f;
        public float PlayerEquivalents => Count * TierMultiplier * PlayerEquivalentsPerMob(Mob);

        public override string ToString() => $"{Count}x {Mob.Name} (x{TierMultiplier:0.0}, ~{PlayerEquivalents:0.0} PE, {Mob.Damage.Dominant})";
    }
}
