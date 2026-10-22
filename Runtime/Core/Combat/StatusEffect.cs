using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core.Combat
{
    /// <summary>Every status effect in the game. Players, mobs and bosses all apply from this list.</summary>
    public enum StatusKind
    {
        Burn,       // Fire: damage over time, spreads to adjacent enemies
        Chill,      // Frost: slow; three stacks become Freeze
        Freeze,     // Frost: rooted and unable to act
        Stagger,    // Lightning: brief interrupt; on a Chilled target becomes Stun
        Stun,       // Lightning (via Chill) or heavy physical: cannot act
        Poisoned,   // Poison: stacking damage over time that does not expire on its own until cleansed
        Weaken,     // Shadow: reduced healing taken and damage dealt
        Bleed,      // Physical: damage over time, stacks
    }

    /// <summary>The design of one status effect: what it does, how long, how it stacks and what it turns into.</summary>
    public sealed class StatusEffectDefinition
    {
        public StatusKind Kind { get; }
        public DamageType Element { get; }
        public float DurationSeconds { get; }
        /// <summary>Burn / Poisoned / Bleed: damage per second as a fraction of the applying hit. Chill: slow fraction. Weaken: healing-and-damage reduction. Stagger / Freeze / Stun: unused.</summary>
        public float Magnitude { get; }
        public int MaxStacks { get; }
        /// <summary>Reaching MaxStacks replaces the effect with this one (Chill → Freeze). Null = just caps.</summary>
        public StatusKind? OnMaxStacks { get; }
        /// <summary>Burn spreads to adjacent enemies; Stagger chains to grouped ones.</summary>
        public bool Spreads { get; }
        public bool Chains { get; }
        /// <summary>If the target already has this, the applied effect is upgraded (Stagger on Chill → Stun).</summary>
        public StatusKind? ComboWith { get; }
        public StatusKind? ComboResult { get; }
        public string Description { get; }

        public StatusEffectDefinition(StatusKind kind, DamageType element, float duration, float magnitude, string description,
            int maxStacks = 1, StatusKind? onMax = null, bool spreads = false, bool chains = false, StatusKind? comboWith = null, StatusKind? comboResult = null)
        {
            Kind = kind; Element = element; DurationSeconds = duration; Magnitude = magnitude; Description = description;
            MaxStacks = maxStacks; OnMaxStacks = onMax; Spreads = spreads; Chains = chains; ComboWith = comboWith; ComboResult = comboResult;
        }

        public bool IsDamageOverTime => Kind == StatusKind.Burn || Kind == StatusKind.Poisoned || Kind == StatusKind.Bleed;
        public bool IsHardControl => Kind == StatusKind.Freeze || Kind == StatusKind.Stun;

        public static readonly IReadOnlyDictionary<StatusKind, StatusEffectDefinition> All = new[]
        {
            new StatusEffectDefinition(StatusKind.Burn, DamageType.Fire, 4f, 0.15f, "Takes 15% of the hit per second for 4 s. Spreads to enemies within 2 m.", maxStacks: 3, spreads: true),
            new StatusEffectDefinition(StatusKind.Chill, DamageType.Frost, 3f, 0.15f, "15% slower per stack for 3 s. Three stacks freeze.", maxStacks: 3, onMax: StatusKind.Freeze),
            new StatusEffectDefinition(StatusKind.Freeze, DamageType.Frost, 1f, 0f, "Rooted and unable to act for 1 s. Shattering damage ends it early."),
            new StatusEffectDefinition(StatusKind.Stagger, DamageType.Lightning, 0.5f, 0f, "Interrupted for 0.5 s; casts and channels cancel. Chains to up to two enemies within 4 m. Stuns a chilled target.", chains: true, comboWith: StatusKind.Chill, comboResult: StatusKind.Stun),
            new StatusEffectDefinition(StatusKind.Stun, DamageType.Lightning, 1.5f, 0f, "Cannot act for 1.5 s."),
            new StatusEffectDefinition(StatusKind.Poisoned, DamageType.Poison, 8f, 0.05f, "5% of the hit per second per stack for 8 s, refreshed by every hit. Mender Purge removes all stacks.", maxStacks: 5),
            new StatusEffectDefinition(StatusKind.Weaken, DamageType.Shadow, 5f, 0.25f, "Heals received and damage dealt reduced 25% for 5 s. Marked for your team."),
            new StatusEffectDefinition(StatusKind.Bleed, DamageType.Physical, 5f, 0.08f, "8% of the hit per second per stack for 5 s.", maxStacks: 3),
        }.ToDictionary(d => d.Kind);

        /// <summary>The effect a basic hit of each element applies. Physical hits apply nothing by default; abilities add Bleed explicitly.</summary>
        public static StatusEffectDefinition OnHitFor(DamageType element)
        {
            switch (element)
            {
                case DamageType.Fire: return All[StatusKind.Burn];
                case DamageType.Frost: return All[StatusKind.Chill];
                case DamageType.Lightning: return All[StatusKind.Stagger];
                case DamageType.Poison: return All[StatusKind.Poisoned];
                case DamageType.Shadow: return All[StatusKind.Weaken];
                default: return null;
            }
        }
    }

    /// <summary>One effect currently on a target.</summary>
    public sealed class StatusInstance
    {
        public StatusEffectDefinition Definition { get; }
        public int Stacks { get; set; }
        public float RemainingSeconds { get; set; }
        public StatusInstance(StatusEffectDefinition d, float duration) { Definition = d; Stacks = 1; RemainingSeconds = duration; }
        public override string ToString() => $"{Definition.Kind}{(Stacks > 1 ? " x" + Stacks : "")} ({RemainingSeconds:0.0}s)";
    }

    /// <summary>
    /// The statuses on one target and the rules for adding more: stacking, caps, Chill → Freeze,
    /// Stagger on Chill → Stun, and resistance shortening everything of its element.
    /// </summary>
    public sealed class StatusTracker
    {
        private readonly List<StatusInstance> _active = new List<StatusInstance>();
        public IReadOnlyList<StatusInstance> Active => _active;

        public bool Has(StatusKind k) => _active.Any(s => s.Definition.Kind == k);
        public StatusInstance Get(StatusKind k) => _active.FirstOrDefault(s => s.Definition.Kind == k);

        /// <summary>Apply an effect to a target with the given resistances. Returns what actually landed (may differ: combos and max-stack transforms).</summary>
        public StatusInstance Apply(StatusEffectDefinition effect, Resistances targetResists = null)
        {
            // Resistance shortens the effect: a 50% fire resist means a 2 s burn instead of 4.
            float mitigation = targetResists?.Mitigation(effect.Element) ?? 0f;
            float duration = effect.DurationSeconds * Math.Max(0f, 1f - mitigation);
            if (duration <= 0.05f) return null;

            // Combo: Stagger on a chilled target becomes Stun (and consumes the chill).
            if (effect.ComboWith.HasValue && effect.ComboResult.HasValue && Has(effect.ComboWith.Value))
            {
                _active.RemoveAll(s => s.Definition.Kind == effect.ComboWith.Value);
                return Add(StatusEffectDefinition.All[effect.ComboResult.Value], targetResists);
            }

            var existing = Get(effect.Kind);
            if (existing == null) return Add(effect, duration);

            existing.RemainingSeconds = Math.Max(existing.RemainingSeconds, duration);
            if (existing.Stacks < effect.MaxStacks) existing.Stacks++;
            if (existing.Stacks >= effect.MaxStacks && effect.OnMaxStacks.HasValue)
            {
                _active.Remove(existing);
                return Add(StatusEffectDefinition.All[effect.OnMaxStacks.Value], targetResists);
            }
            return existing;
        }

        private StatusInstance Add(StatusEffectDefinition d, Resistances r)
        {
            float mitigation = r?.Mitigation(d.Element) ?? 0f;
            return Add(d, d.DurationSeconds * Math.Max(0f, 1f - mitigation));
        }

        private StatusInstance Add(StatusEffectDefinition d, float duration)
        {
            var inst = new StatusInstance(d, duration);
            _active.Add(inst);
            return inst;
        }

        public void Tick(float seconds)
        {
            foreach (var s in _active) s.RemainingSeconds -= seconds;
            _active.RemoveAll(s => s.RemainingSeconds <= 0f);
        }

        /// <summary>Remove every effect (Mender Purge).</summary>
        public void Cleanse() => _active.Clear();

        /// <summary>Current slow fraction from Chill stacks (0 = none).</summary>
        public float SlowFraction => Get(StatusKind.Chill) is StatusInstance c ? Math.Min(0.9f, c.Stacks * c.Definition.Magnitude) : 0f;
        public bool IsHardControlled => _active.Any(s => s.Definition.IsHardControl);

        /// <summary>Damage per second currently ticking, as a fraction of the hits that applied it.</summary>
        public float DotFractionPerSecond => _active.Where(s => s.Definition.IsDamageOverTime).Sum(s => s.Stacks * s.Definition.Magnitude);
    }
}
