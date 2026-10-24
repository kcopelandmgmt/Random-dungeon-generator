using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core.Combat
{
    /// <summary>A position in cell units (one cell is 2 m). Floats, so a player can stand between cells.</summary>
    public readonly struct Vec2
    {
        public readonly float X, Y;
        public Vec2(float x, float y) { X = x; Y = y; }
        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);
        public float Length => (float)Math.Sqrt(X * X + Y * Y);
        public Vec2 Normalized => Length < 1e-6f ? new Vec2(0f, 1f) : this * (1f / Length);
        public float Dot(Vec2 o) => X * o.X + Y * o.Y;
        public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;
        public GridPoint Cell => new GridPoint((int)Math.Floor(X), (int)Math.Floor(Y));
        public static Vec2 Of(GridPoint p) => new Vec2(p.X + 0.5f, p.Y + 0.5f);
        public override string ToString() => $"({X:0.0}, {Y:0.0})";
    }

    public enum AbilityKind { Basic, Bolt, Zone, Mobility, Ultimate }

    /// <summary>How an ability finds what it touches.</summary>
    public enum AbilityShape
    {
        Self,        // the caster
        Target,      // one combatant near the aim point, in range and sight
        Projectile,  // travels from the caster toward the aim point; stops at the first body or blocking tile
        Pierce,      // travels the full range through bodies; stops only at blocking tiles
        Cone,        // everyone within range inside an arc centred on the aim direction, in sight
        Circle,      // everyone within a radius of the aim point (the point must be in sight); with a duration it is a zone that ticks
        Dash,        // the caster moves toward the aim point; may strike the first body met
    }

    public enum TargetFilter { Everyone, Allies, Enemies, Downed }

    /// <summary>Temporary states the fight simulator tracks beyond status effects.</summary>
    public enum BuffKind { DamageReduction, Invulnerable, Reflect, CannotBeDowned, Taunt, Rooted, Silenced, Invisible, Revealed, Marked, HealOnHit, NoChannel, FrontShield, Immovable, SlowField, AttackSpeed }

    /// <summary>
    /// One ability as numbers. Damage is a multiple of the caster's DPS per hit (a basic attack is
    /// 1.0 per attack interval). Everything that damages obeys full friendly fire: Everyone means
    /// everyone but the caster. Status is what a hit applies on top of the weapon's own element.
    /// A duration on a Circle makes a zone that ticks once a second. A Buff on Self or on the
    /// targets lasts BuffSeconds. Knockback pushes along the hit direction, stopping at anything solid.
    /// </summary>
    public sealed class AbilityDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public string Spec { get; }
        public AbilityKind Kind { get; }
        public AbilityShape Shape { get; }
        public TargetFilter Filter { get; }
        public float RangeCells { get; }
        /// <summary>Projectile and Pierce: hit width. Cone: full angle in degrees. Circle: radius. Target: pick radius around the aim point.</summary>
        public float Size { get; }
        public float Damage { get; }
        public float Heal { get; }
        public DamageType? Element { get; }
        public StatusKind? Status { get; }
        public float CooldownSeconds { get; }
        public float DurationSeconds { get; }
        public float KnockbackCells { get; }
        public BuffKind? Buff { get; }
        public float BuffMagnitude { get; }
        public float BuffSeconds { get; }
        /// <summary>The buff goes on the caster (Self and Dash) or on the targets.</summary>
        public bool BuffOnTargets { get; }
        public string Text { get; }

        public AbilityDefinition(string id, string name, string spec, AbilityKind kind, AbilityShape shape, TargetFilter filter, float range, float size, float damage, float cooldown, string text,
            float heal = 0f, DamageType? element = null, StatusKind? status = null, float duration = 0f, float knockback = 0f, BuffKind? buff = null, float buffMagnitude = 0f, float buffSeconds = 0f, bool buffOnTargets = false)
        {
            Id = id; Name = name; Spec = spec; Kind = kind; Shape = shape; Filter = filter; RangeCells = range; Size = size; Damage = damage; CooldownSeconds = cooldown; Text = text;
            Heal = heal; Element = element; Status = status; DurationSeconds = duration; KnockbackCells = knockback; Buff = buff; BuffMagnitude = buffMagnitude; BuffSeconds = buffSeconds; BuffOnTargets = buffOnTargets;
        }

        public AbilitySlot? CostSlot => Kind == AbilityKind.Bolt ? AbilitySlot.Bolt : Kind == AbilityKind.Zone ? AbilitySlot.Zone : Kind == AbilityKind.Mobility ? AbilitySlot.Mobility : (AbilitySlot?)null;
        public bool IsZone => Shape == AbilityShape.Circle && DurationSeconds > 0f;
        public override string ToString() => $"{Name} [{Spec} {Kind}] {Shape} r{RangeCells} s{Size} dmg x{Damage} cd {CooldownSeconds}s";
    }

    /// <summary>
    /// Every ability in the game as data: four per spec (basic, bolt, zone, mobility) for the twelve
    /// weapon specs and three element kits, and the thirteen ultimates. Passives are not here; the
    /// simulator implements the handful that change numbers (Backstab, Deadeye, Execute, Frenzy,
    /// Momentum) by name. Numbers are first tuning, meant to be moved by the fight lab.
    /// </summary>
    public static class AbilityBook
    {
        private const float M = 0.5f;   // one metre in cells
        private static AbilityDefinition Basic(string spec, string name, AbilityShape shape, float rangeM, float size, string text, DamageType? element = null, StatusKind? status = null) =>
            new AbilityDefinition($"{spec}.basic", name, spec, AbilityKind.Basic, shape, TargetFilter.Everyone, rangeM * M, size, 1.0f, 0f, text, element: element, status: status);

        public static readonly IReadOnlyList<AbilityDefinition> All = new List<AbilityDefinition>
        {
            // ---------------- Warden ----------------
            Basic("Sentinel", "Sword Strike", AbilityShape.Cone, 3f, 70f, "A short arc in front of you."),
            new AbilityDefinition("Sentinel.bolt", "Shield Bash", "Sentinel", AbilityKind.Bolt, AbilityShape.Target, TargetFilter.Everyone, 3f * M, 1f, 1.2f, 6f, "Single target, Staggers, short range.", status: StatusKind.Stagger),
            new AbilityDefinition("Sentinel.zone", "Shield Wall", "Sentinel", AbilityKind.Zone, AbilityShape.Self, TargetFilter.Allies, 0f, 0f, 0f, 14f, "Block frontal damage for 4 s; allies behind you take none.", buff: BuffKind.FrontShield, buffMagnitude: 1f, buffSeconds: 4f),
            new AbilityDefinition("Sentinel.mobility", "Hold the Line", "Sentinel", AbilityKind.Mobility, AbilityShape.Circle, TargetFilter.Enemies, 0f, 6f * M, 0f, 18f, "Root yourself, taunt everything within 6 m, 40% damage reduction for 6 s.", buff: BuffKind.Taunt, buffMagnitude: 0.4f, buffSeconds: 6f, buffOnTargets: true),
            Basic("Breaker", "Hammer Swing", AbilityShape.Cone, 3f, 90f, "A wide, slow arc."),
            new AbilityDefinition("Breaker.bolt", "Crush", "Breaker", AbilityKind.Bolt, AbilityShape.Target, TargetFilter.Everyone, 3f * M, 1f, 1.8f, 7f, "Heavy single hit, Staggers.", status: StatusKind.Stagger),
            new AbilityDefinition("Breaker.zone", "Shockwave", "Breaker", AbilityKind.Zone, AbilityShape.Cone, TargetFilter.Everyone, 5f * M, 90f, 0.8f, 20f, "Ground slam: knocks everything in a cone back 4 m and Staggers it.", status: StatusKind.Stagger, knockback: 4f * M),
            new AbilityDefinition("Breaker.mobility", "Charge", "Breaker", AbilityKind.Mobility, AbilityShape.Dash, TargetFilter.Everyone, 8f * M, 1f, 0.8f, 12f, "Short rush; knocks the first target back and interrupts.", status: StatusKind.Stagger, knockback: 2f * M),
            // ---------------- Reaver ----------------
            Basic("Berserker", "Axe Swing", AbilityShape.Cone, 3f, 110f, "A sweeping arc."),
            new AbilityDefinition("Berserker.bolt", "Cleave", "Berserker", AbilityKind.Bolt, AbilityShape.Cone, TargetFilter.Everyone, 4f * M, 120f, 1.3f, 6f, "Cone hit; Bleeds everything it touches.", status: StatusKind.Bleed),
            new AbilityDefinition("Berserker.zone", "Whirlwind", "Berserker", AbilityKind.Zone, AbilityShape.Circle, TargetFilter.Everyone, 0f, 3f * M, 0.7f, 16f, "Spin for 3 s, hitting all around you each second.", duration: 3f),
            new AbilityDefinition("Berserker.mobility", "Lunge", "Berserker", AbilityKind.Mobility, AbilityShape.Dash, TargetFilter.Everyone, 8f * M, 1f, 0.6f, 10f, "8 m gap-close; the hit Bleeds.", status: StatusKind.Bleed),
            Basic("Duelist", "Sword Strike", AbilityShape.Cone, 3f, 60f, "A tight arc."),
            new AbilityDefinition("Duelist.bolt", "Thrust", "Duelist", AbilityKind.Bolt, AbilityShape.Target, TargetFilter.Everyone, 3.5f * M, 1f, 2.0f, 6f, "Single target, high damage, Bleeds.", status: StatusKind.Bleed),
            new AbilityDefinition("Duelist.zone", "Riposte", "Duelist", AbilityKind.Zone, AbilityShape.Self, TargetFilter.Everyone, 0f, 0f, 0f, 12f, "Parry stance for 1 s; a blocked hit is reflected and interrupts the attacker.", buff: BuffKind.Reflect, buffMagnitude: 1f, buffSeconds: 1f),
            new AbilityDefinition("Duelist.mobility", "Lunge", "Duelist", AbilityKind.Mobility, AbilityShape.Dash, TargetFilter.Everyone, 8f * M, 1f, 0.5f, 10f, "8 m gap-close."),
            // ---------------- Marksman ----------------
            Basic("Sharpshooter", "Arrow", AbilityShape.Projectile, 16f, 1f, "A single arrow, long range."),
            new AbilityDefinition("Sharpshooter.bolt", "Piercing Shot", "Sharpshooter", AbilityKind.Bolt, AbilityShape.Pierce, TargetFilter.Everyone, 24f * M, 1f, 1.4f, 7f, "Hits everything in a line, corridor-length range."),
            new AbilityDefinition("Sharpshooter.zone", "Caltrops", "Sharpshooter", AbilityKind.Zone, AbilityShape.Circle, TargetFilter.Everyone, 10f * M, 2f * M, 0.1f, 16f, "Slow field on a doorway for 8 s.", status: StatusKind.Chill, duration: 8f),
            new AbilityDefinition("Sharpshooter.mobility", "Vault Back", "Sharpshooter", AbilityKind.Mobility, AbilityShape.Dash, TargetFilter.Everyone, 5f * M, 0f, 0f, 10f, "Leap backwards; brief invulnerability.", buff: BuffKind.Invulnerable, buffMagnitude: 1f, buffSeconds: 0.5f),
            Basic("Volleyer", "Quick Arrow", AbilityShape.Projectile, 12f, 1f, "Fast arrows."),
            new AbilityDefinition("Volleyer.bolt", "Rapid Shot", "Volleyer", AbilityKind.Bolt, AbilityShape.Projectile, TargetFilter.Everyone, 12f * M, 1f, 1.6f, 4f, "Fast single-target shots."),
            new AbilityDefinition("Volleyer.zone", "Rain of Arrows", "Volleyer", AbilityKind.Zone, AbilityShape.Circle, TargetFilter.Everyone, 12f * M, 5f * M, 0.6f, 14f, "5 m circle for 4 s, hits everything inside each second.", duration: 4f),
            new AbilityDefinition("Volleyer.mobility", "Tumble", "Volleyer", AbilityKind.Mobility, AbilityShape.Dash, TargetFilter.Everyone, 4f * M, 0f, 0f, 6f, "Short dodge roll; attack speed +20% for 4 s after.", buff: BuffKind.AttackSpeed, buffMagnitude: 0.2f, buffSeconds: 4f),
            // ---------------- Shade ----------------
            Basic("Assassin", "Dagger Strike", AbilityShape.Cone, 2.5f, 60f, "Quick, close."),
            new AbilityDefinition("Assassin.bolt", "Backstab", "Assassin", AbilityKind.Bolt, AbilityShape.Target, TargetFilter.Everyone, 2.5f * M, 1f, 1.2f, 6f, "Triple damage from behind."),
            new AbilityDefinition("Assassin.zone", "Smoke Step", "Assassin", AbilityKind.Zone, AbilityShape.Dash, TargetFilter.Everyone, 6f * M, 0f, 0f, 14f, "Drop a smoke cloud, dash through it.", buff: BuffKind.Invisible, buffMagnitude: 1f, buffSeconds: 1.5f),
            new AbilityDefinition("Assassin.mobility", "Veil", "Assassin", AbilityKind.Mobility, AbilityShape.Self, TargetFilter.Everyone, 0f, 0f, 0f, 20f, "Invisible for 5 s while moving slowly; broken by damage.", buff: BuffKind.Invisible, buffMagnitude: 1f, buffSeconds: 5f),
            Basic("Trapper", "Quick Arrow", AbilityShape.Projectile, 12f, 1f, "Fast arrows."),
            new AbilityDefinition("Trapper.bolt", "Snare Shot", "Trapper", AbilityKind.Bolt, AbilityShape.Projectile, TargetFilter.Everyone, 12f * M, 1f, 0.8f, 8f, "Roots for 1.5 s.", buff: BuffKind.Rooted, buffMagnitude: 1f, buffSeconds: 1.5f, buffOnTargets: true),
            new AbilityDefinition("Trapper.zone", "Tripwire", "Trapper", AbilityKind.Zone, AbilityShape.Circle, TargetFilter.Enemies, 8f * M, 1f * M, 0.3f, 18f, "Invisible line across a doorway; the first to cross it is rooted and revealed.", duration: 30f, buff: BuffKind.Rooted, buffMagnitude: 1f, buffSeconds: 1.5f, buffOnTargets: true),
            new AbilityDefinition("Trapper.mobility", "Smoke Step", "Trapper", AbilityKind.Mobility, AbilityShape.Dash, TargetFilter.Everyone, 6f * M, 0f, 0f, 14f, "Drop a smoke cloud, dash through it.", buff: BuffKind.Invisible, buffMagnitude: 1f, buffSeconds: 1.5f),
            // ---------------- Mender ----------------
            Basic("Warder", "Sceptre Bolt", AbilityShape.Projectile, 10f, 1f, "A slow bolt of light."),
            new AbilityDefinition("Warder.bolt", "Mend", "Warder", AbilityKind.Bolt, AbilityShape.Target, TargetFilter.Allies, 10f * M, 1.5f, 0f, 3f, "Heal over time, stacks twice.", heal: 1.0f),
            new AbilityDefinition("Warder.zone", "Sanctuary", "Warder", AbilityKind.Zone, AbilityShape.Circle, TargetFilter.Allies, 8f * M, 3f * M, 0f, 18f, "Ground circle: allies inside take 25% less damage.", duration: 6f, buff: BuffKind.DamageReduction, buffMagnitude: 0.25f, buffSeconds: 1.2f, buffOnTargets: true),
            new AbilityDefinition("Warder.mobility", "Purge", "Warder", AbilityKind.Mobility, AbilityShape.Target, TargetFilter.Allies, 10f * M, 1.5f, 0f, 10f, "Remove every status from an ally."),
            Basic("Plaguebearer", "Censer Swing", AbilityShape.Projectile, 8f, 1f, "A lobbed ember.", element: DamageType.Poison),
            new AbilityDefinition("Plaguebearer.bolt", "Smouldering Bolt", "Plaguebearer", AbilityKind.Bolt, AbilityShape.Projectile, TargetFilter.Everyone, 10f * M, 1f, 1.2f, 4f, "Poisons on hit.", element: DamageType.Poison, status: StatusKind.Poisoned),
            new AbilityDefinition("Plaguebearer.zone", "Miasma", "Plaguebearer", AbilityKind.Zone, AbilityShape.Circle, TargetFilter.Everyone, 8f * M, 3f * M, 0.3f, 16f, "Heal-over-time cloud for allies that Poisons enemies inside.", heal: 0.3f, element: DamageType.Poison, status: StatusKind.Poisoned, duration: 5f),
            new AbilityDefinition("Plaguebearer.mobility", "Purge", "Plaguebearer", AbilityKind.Mobility, AbilityShape.Target, TargetFilter.Allies, 10f * M, 1.5f, 0f, 10f, "Remove every status from an ally."),
            // ---------------- Elementalist ----------------
            Basic("Pyromancy", "Ember", AbilityShape.Projectile, 10f, 1f, "A spark that Burns.", element: DamageType.Fire),
            new AbilityDefinition("Pyromancy.bolt", "Firebolt", "Pyromancy", AbilityKind.Bolt, AbilityShape.Projectile, TargetFilter.Everyone, 12f * M, 1f, 1.5f, 4f, "Single target, applies Burn.", element: DamageType.Fire, status: StatusKind.Burn),
            new AbilityDefinition("Pyromancy.zone", "Firewall", "Pyromancy", AbilityKind.Zone, AbilityShape.Circle, TargetFilter.Everyone, 8f * M, 3f * M, 0.5f, 16f, "6 m line of fire for 6 s; burns anyone who crosses.", element: DamageType.Fire, status: StatusKind.Burn, duration: 6f),
            new AbilityDefinition("Pyromancy.mobility", "Cinder Step", "Pyromancy", AbilityKind.Mobility, AbilityShape.Dash, TargetFilter.Everyone, 6f * M, 0f, 0f, 10f, "Short teleport, leaves fire behind."),
            Basic("Cryomancy", "Frost Mote", AbilityShape.Projectile, 10f, 1f, "A mote that Chills.", element: DamageType.Frost),
            new AbilityDefinition("Cryomancy.bolt", "Frost Shard", "Cryomancy", AbilityKind.Bolt, AbilityShape.Projectile, TargetFilter.Everyone, 12f * M, 1f, 1.4f, 4f, "Single target, applies Chill.", element: DamageType.Frost, status: StatusKind.Chill),
            new AbilityDefinition("Cryomancy.zone", "Glacial Field", "Cryomancy", AbilityKind.Zone, AbilityShape.Circle, TargetFilter.Everyone, 10f * M, 5f * M, 0.3f, 16f, "5 m circle for 6 s, Chills every second.", element: DamageType.Frost, status: StatusKind.Chill, duration: 6f),
            new AbilityDefinition("Cryomancy.mobility", "Ice Slide", "Cryomancy", AbilityKind.Mobility, AbilityShape.Dash, TargetFilter.Everyone, 6f * M, 0f, 0f, 10f, "Dash that leaves a trail of ice."),
            Basic("Stormcalling", "Spark", AbilityShape.Projectile, 10f, 1f, "A crackle that Staggers.", element: DamageType.Lightning),
            new AbilityDefinition("Stormcalling.bolt", "Arc", "Stormcalling", AbilityKind.Bolt, AbilityShape.Projectile, TargetFilter.Everyone, 12f * M, 1f, 1.3f, 4f, "Single target, applies Stagger, chains to two enemies within 4 m.", element: DamageType.Lightning, status: StatusKind.Stagger),
            new AbilityDefinition("Stormcalling.zone", "Static Field", "Stormcalling", AbilityKind.Zone, AbilityShape.Circle, TargetFilter.Everyone, 10f * M, 5f * M, 0.3f, 16f, "5 m circle for 5 s, Staggers anyone inside.", element: DamageType.Lightning, status: StatusKind.Stagger, duration: 5f),
            new AbilityDefinition("Stormcalling.mobility", "Blink", "Stormcalling", AbilityKind.Mobility, AbilityShape.Dash, TargetFilter.Everyone, 6f * M, 0f, 0f, 8f, "Short teleport, no trail."),
            // ---------------- Ultimates: one per spec, earned with two souls ----------------
            new AbilityDefinition("Breaker.ult", "Cave-In", "Breaker", AbilityKind.Ultimate, AbilityShape.Circle, TargetFilter.Everyone, 0f, 1f * M, 1.5f, 0f, "Collapse the doorway you stand in; rubble blocks everything for 15 s.", duration: 15f, buff: BuffKind.SlowField, buffMagnitude: 1f, buffSeconds: 1f, buffOnTargets: true),
            new AbilityDefinition("Trapper.ult", "Lockdown", "Trapper", AbilityKind.Ultimate, AbilityShape.Circle, TargetFilter.Enemies, 0f, 8f * M, 0.2f, 0f, "Every doorway of the room is tripwired for 20 s.", duration: 20f, buff: BuffKind.Rooted, buffMagnitude: 1f, buffSeconds: 1.5f, buffOnTargets: true),
            new AbilityDefinition("Pyromancy.ult", "Inferno", "Pyromancy", AbilityKind.Ultimate, AbilityShape.Circle, TargetFilter.Everyone, 0f, 8f * M, 0.6f, 0f, "The room burns for 6 s, Burning everyone inside every second. Everyone.", element: DamageType.Fire, status: StatusKind.Burn, duration: 6f),
            new AbilityDefinition("Volleyer.ult", "Suppression", "Volleyer", AbilityKind.Ultimate, AbilityShape.Circle, TargetFilter.Everyone, 12f * M, 10f * M, 0.2f, 0f, "A 10 m zone for 8 s; anyone inside is Chilled and cannot channel.", status: StatusKind.Chill, duration: 8f, buff: BuffKind.NoChannel, buffMagnitude: 1f, buffSeconds: 1.2f, buffOnTargets: true),
            new AbilityDefinition("Stormcalling.ult", "Blackout", "Stormcalling", AbilityKind.Ultimate, AbilityShape.Circle, TargetFilter.Everyone, 0f, 10f * M, 0f, 0f, "For 8 s nobody within 10 m can channel anything.", duration: 8f, buff: BuffKind.NoChannel, buffMagnitude: 1f, buffSeconds: 1.2f, buffOnTargets: true),
            new AbilityDefinition("Sentinel.ult", "Aegis", "Sentinel", AbilityKind.Ultimate, AbilityShape.Circle, TargetFilter.Allies, 0f, 5f * M, 0f, 0f, "For 5 s you cannot be moved and allies within 5 m take half damage.", duration: 5f, buff: BuffKind.DamageReduction, buffMagnitude: 0.5f, buffSeconds: 1.2f, buffOnTargets: true),
            new AbilityDefinition("Duelist.ult", "Perfect Guard", "Duelist", AbilityKind.Ultimate, AbilityShape.Self, TargetFilter.Everyone, 0f, 0f, 0f, 0f, "For 3 s every hit on you is parried and reflected.", buff: BuffKind.Reflect, buffMagnitude: 1f, buffSeconds: 3f),
            new AbilityDefinition("Berserker.ult", "Bloodbath", "Berserker", AbilityKind.Ultimate, AbilityShape.Self, TargetFilter.Everyone, 0f, 0f, 0f, 0f, "For 8 s every hit heals 20% of its damage and you cannot be downed.", buff: BuffKind.CannotBeDowned, buffMagnitude: 0.2f, buffSeconds: 8f),
            new AbilityDefinition("Cryomancy.ult", "Absolute Zero", "Cryomancy", AbilityKind.Ultimate, AbilityShape.Circle, TargetFilter.Everyone, 0f, 8f * M, 0.5f, 0f, "Freeze everyone within 8 m for 2 s, ignoring diminishing returns.", element: DamageType.Frost, status: StatusKind.Freeze),
            new AbilityDefinition("Warder.ult", "Last Breath", "Warder", AbilityKind.Ultimate, AbilityShape.Target, TargetFilter.Downed, 10f * M, 1.5f, 0f, 0f, "Revive a downed ally instantly, both of you at full health. Never on a finished player.", heal: 100f),
            new AbilityDefinition("Plaguebearer.ult", "Outbreak", "Plaguebearer", AbilityKind.Ultimate, AbilityShape.Circle, TargetFilter.Enemies, 0f, 10f * M, 0.5f, 0f, "Every enemy within 10 m goes to maximum Poison.", element: DamageType.Poison, status: StatusKind.Poisoned),
            new AbilityDefinition("Assassin.ult", "Death Mark", "Assassin", AbilityKind.Ultimate, AbilityShape.Target, TargetFilter.Enemies, 15f * M, 2f, 0f, 0f, "Mark a player for 10 s; your hits on them are critical and your team sees them through walls.", buff: BuffKind.Marked, buffMagnitude: 1f, buffSeconds: 10f, buffOnTargets: true),
            new AbilityDefinition("Sharpshooter.ult", "Overwatch", "Sharpshooter", AbilityKind.Ultimate, AbilityShape.Self, TargetFilter.Everyone, 0f, 0f, 0f, 0f, "For 10 s every enemy who enters your line of sight is shot once automatically.", buff: BuffKind.Revealed, buffMagnitude: 1f, buffSeconds: 10f),
            // ---------------- Mobs and bosses: the simulator's stand-ins for their behaviour scripts ----------------
            Basic("Mob", "Claw", AbilityShape.Target, 2.5f, 2f, "A melee mob's swing: one target, the tuner's DPS is single-target."),
            new AbilityDefinition("Mob.ranged", "Bolt", "Mob", AbilityKind.Bolt, AbilityShape.Projectile, TargetFilter.Everyone, 10f * M, 1f, 1.0f, 1.5f, "A caster mob's shot."),
            Basic("Boss", "Smash", AbilityShape.Target, 4f, 2f, "A boss's swing: one target. The slam is the area attack."),
            new AbilityDefinition("Boss.slam", "Slam", "Boss", AbilityKind.Bolt, AbilityShape.Circle, TargetFilter.Everyone, 0f, 4f * M, 2.0f, 8f, "A boss's area slam around itself.", knockback: 2f * M),
        };

        private static readonly Dictionary<string, AbilityDefinition> _byId = All.ToDictionary(a => a.Id);
        public static AbilityDefinition Get(string id) => _byId[id];
        public static bool TryGet(string id, out AbilityDefinition a) => _byId.TryGetValue(id, out a);
        public static IEnumerable<AbilityDefinition> For(string spec) => All.Where(a => a.Spec == spec);
        public static AbilityDefinition Ultimate(string spec) => All.FirstOrDefault(a => a.Spec == spec && a.Kind == AbilityKind.Ultimate);
        public static IEnumerable<string> Specs => All.Select(a => a.Spec).Distinct();
    }
}
