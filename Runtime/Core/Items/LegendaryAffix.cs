using DungeonGen.Core.Combat;

namespace DungeonGen.Core.Items
{
    /// <summary>When a legendary effect fires. Numeric kinds (Lifesteal, Thorns, ResistCap) are always on.</summary>
    public enum LegendaryTrigger { Passive, OnHit, OnKill, OnTakeHit, OnHeal, OnLethalHit }

    /// <summary>
    /// Behaviours only a unique can carry. These are not in the affix pool and cannot roll on Magic
    /// or Rare items; a new one is unique-only by construction. The generator stores them as data;
    /// the game executes them.
    /// </summary>
    public enum LegendaryKind
    {
        Lifesteal,      // Magnitude% of damage dealt returned as health
        Thorns,         // Magnitude% of damage taken reflected to the attacker as Type
        ResistCap,      // Raises the 75% mitigation cap by Magnitude points for every type
        ProcDamage,     // Chance on hit: burst of Magnitude% weapon damage as Type in a small radius
        Summon,         // Chance on taking a hit: a friendly mob (Note names it) for Magnitude seconds
        SecondWind,     // Survive a lethal hit at 1 health once per Cooldown seconds
        HealSplash,     // Heals you cast also deal Magnitude% of the heal as Type to enemies near the target
        Phasing,        // Enemies cannot body-block you; Magnitude% move speed while touching one
        KillRush,       // On kill: Magnitude% move speed for Cooldown seconds (Cooldown doubles as duration)
    }

    public sealed class LegendaryAffix
    {
        public string Name { get; }
        public LegendaryKind Kind { get; }
        public LegendaryTrigger Trigger { get; }
        public float Magnitude { get; }
        /// <summary>0..1 for chance-based triggers; 1 for always.</summary>
        public float Chance { get; }
        public float CooldownSeconds { get; }
        public DamageType Type { get; }
        /// <summary>Extra data the game needs (a mob name for Summon, a radius hint for procs).</summary>
        public string Note { get; }
        public string Description { get; }

        public LegendaryAffix(string name, LegendaryKind kind, LegendaryTrigger trigger, float magnitude, string description,
            float chance = 1f, float cooldown = 0f, DamageType type = DamageType.Physical, string note = null)
        {
            Name = name; Kind = kind; Trigger = trigger; Magnitude = magnitude; Description = description;
            Chance = chance; CooldownSeconds = cooldown; Type = type; Note = note;
        }

        public bool IsNumeric => Kind == LegendaryKind.Lifesteal || Kind == LegendaryKind.Thorns || Kind == LegendaryKind.ResistCap;

        public override string ToString() => $"{Name}: {Description}";
    }
}
