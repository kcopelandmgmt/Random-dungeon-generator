using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core.Combat
{
    /// <summary>
    /// Six damage types. Physical is mitigated by armour; the other five by their resistance.
    /// Six is enough that no single resistance covers a dungeon and few enough that a player can
    /// read a boss's type from its look.
    /// </summary>
    public enum DamageType { Physical, Fire, Frost, Lightning, Poison, Shadow }

    /// <summary>What fraction of a source's damage comes as each type. Fractions sum to 1.</summary>
    public sealed class DamageProfile
    {
        private readonly Dictionary<DamageType, float> _parts = new Dictionary<DamageType, float>();

        public DamageProfile(params (DamageType type, float fraction)[] parts)
        {
            float total = parts.Sum(p => p.fraction);
            if (total <= 0f) throw new ArgumentException("A damage profile needs at least one positive part.");
            foreach (var (type, fraction) in parts)
                _parts[type] = (_parts.TryGetValue(type, out var f) ? f : 0f) + fraction / total;
        }

        public static DamageProfile Pure(DamageType t) => new DamageProfile((t, 1f));

        public float Fraction(DamageType t) => _parts.TryGetValue(t, out var f) ? f : 0f;
        public IEnumerable<DamageType> Types => _parts.Keys;
        public DamageType Dominant => _parts.OrderByDescending(kv => kv.Value).ThenBy(kv => (int)kv.Key).First().Key;

        public override string ToString() =>
            string.Join(" / ", _parts.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value * 100:0}% {kv.Key}"));
    }

    public enum ArmourWeight { Light, Medium, Heavy }
}
