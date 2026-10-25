using System;
using System.Collections.Generic;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>Where a prefab plays for an ability: at the caster, flying from caster to end, at the end point or each target, or on the ground for a zone's lifetime.</summary>
    public enum VfxRole { Cast, Projectile, Impact, Zone }

    /// <summary>
    /// Which prefab draws which ability. One entry per ability id (`AbilityBook`), four roles each; an ability without
    /// an entry, or a role left empty, falls back to <see cref="Fallback"/>, and with nothing there the effects layer
    /// draws a greybox shape. Filled by hand in the inspector or by the practice mode's VFX audition.
    /// </summary>
    [CreateAssetMenu(menuName = "Dungeon Gen/Ability VFX Library", fileName = "AbilityVfxLibrary")]
    public sealed class AbilityVfxLibrary : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string AbilityId;
            public GameObject Cast, Projectile, Impact, Zone;
            [Tooltip("Metres per second for the projectile prefab.")] public float ProjectileSpeed = 25f;
            [Tooltip("The zone prefab's scale per metre of the zone's radius. 1 if the prefab is authored at one metre radius.")] public float ZoneScalePerMetre = 1f;

            public GameObject Get(VfxRole role) => role == VfxRole.Cast ? Cast : role == VfxRole.Projectile ? Projectile : role == VfxRole.Impact ? Impact : Zone;
            public void Set(VfxRole role, GameObject prefab)
            {
                switch (role)
                {
                    case VfxRole.Cast: Cast = prefab; break;
                    case VfxRole.Projectile: Projectile = prefab; break;
                    case VfxRole.Impact: Impact = prefab; break;
                    default: Zone = prefab; break;
                }
            }
        }

        public List<Entry> Entries = new List<Entry>();
        [Tooltip("Used for any ability or role with nothing assigned.")]
        public Entry Fallback = new Entry { AbilityId = "*" };

        public Entry Find(string abilityId) => Entries.Find(e => e.AbilityId == abilityId);

        public Entry GetOrAdd(string abilityId)
        {
            var e = Find(abilityId);
            if (e == null) { e = new Entry { AbilityId = abilityId }; Entries.Add(e); }
            return e;
        }

        /// <summary>The prefab for this ability and role, the fallback's, or null.</summary>
        public GameObject Prefab(string abilityId, VfxRole role)
        {
            var e = Find(abilityId);
            var p = e != null ? e.Get(role) : null;
            return p != null ? p : Fallback != null ? Fallback.Get(role) : null;
        }

        public float ProjectileSpeed(string abilityId) { var e = Find(abilityId); return e != null && e.ProjectileSpeed > 0f ? e.ProjectileSpeed : Fallback != null && Fallback.ProjectileSpeed > 0f ? Fallback.ProjectileSpeed : 25f; }
        public float ZoneScalePerMetre(string abilityId) { var e = Find(abilityId); return e != null && e.ZoneScalePerMetre > 0f ? e.ZoneScalePerMetre : 1f; }
    }
}
