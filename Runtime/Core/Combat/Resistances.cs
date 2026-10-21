using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core.Combat
{
    /// <summary>
    /// One rating per damage type. A rating becomes a damage reduction through
    /// rating / (rating + <see cref="StatFormulas.MitigationConstant"/>), capped at
    /// <see cref="StatFormulas.MitigationCap"/>, so stacking has diminishing returns and nothing
    /// is ever immune except by design (negative ratings mean extra damage taken).
    /// </summary>
    public sealed class Resistances
    {
        private readonly Dictionary<DamageType, float> _rating = new Dictionary<DamageType, float>();

        public Resistances() { foreach (DamageType t in Enum.GetValues(typeof(DamageType))) _rating[t] = 0f; }

        public float Rating(DamageType t) => _rating[t];
        public void Set(DamageType t, float rating) => _rating[t] = rating;
        public void Add(DamageType t, float delta) => _rating[t] += delta;
        public void AddAllMagic(float delta) { foreach (DamageType t in Enum.GetValues(typeof(DamageType))) if (t != DamageType.Physical) _rating[t] += delta; }

        /// <summary>Points added to the mitigation cap (only a unique can grant this).</summary>
        public float CapBonus { get; set; }

        /// <summary>Fraction of incoming damage of this type that is removed. Negative when the rating is negative.</summary>
        public float Mitigation(DamageType t) => StatFormulas.Mitigation(_rating[t], StatFormulas.MitigationCap + CapBonus / 100f);

        public float DamageTaken(float amount, DamageType t) => amount * (1f - Mitigation(t));

        public float DamageTaken(float amount, DamageProfile profile) =>
            profile.Types.Sum(t => DamageTaken(amount * profile.Fraction(t), t));

        public override string ToString() =>
            string.Join("  ", _rating.Select(kv => $"{kv.Key} {Mitigation(kv.Key) * 100:+0;-0}%"));
    }
}
