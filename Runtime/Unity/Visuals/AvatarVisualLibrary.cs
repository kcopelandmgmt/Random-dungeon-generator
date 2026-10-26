using System;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// Which model draws which avatar. Heroes are keyed by class name (<see cref="ClassArchetype.DefaultRoster"/>), mobs by
    /// mob name, bosses by archetype name (a boss's label is its display name with affixes, so keys match by prefix).
    /// Built by <c>GeneratedModelImporter.Build</c>; anything with no entry, or an entry with no prefab, falls back to
    /// <see cref="Fallback"/> and then to the greybox capsule in <see cref="ClientWorldView"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Dungeon Gen/Avatar Visual Library", fileName = "AvatarVisualLibrary")]
    public sealed class AvatarVisualLibrary : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("Class name, mob name or boss archetype name.")] public string Key;
            public GameObject Prefab;
            [Tooltip("Extra uniform scale on top of the prefab's normalised size.")] public float Scale = 1f;
            [Tooltip("Radius of the team ring under the feet, metres (before Scale).")] public float RingRadius = 0.6f;
            public bool IsBoss;
        }

        public List<Entry> Entries = new List<Entry>();
        [Tooltip("Used when an avatar has no entry. Leave empty to fall back to the greybox capsule.")]
        public Entry Fallback = new Entry { Key = "*" };
        [Tooltip("Unlit material for the team ring decal; its colour is set per avatar.")] public Material RingMaterial;
        [Tooltip("Transparent URP Lit material used while an avatar is invisible (its base map is copied in).")] public Material GhostMaterial;

        public Entry Find(string key) => string.IsNullOrEmpty(key) ? null : Entries.Find(e => e.Key == key);

        /// <summary>The entry that draws this avatar, or null when nothing (not even the fallback) has a prefab.</summary>
        public Entry Resolve(AvatarSnapshot a)
        {
            Entry e = null;
            if (!a.IsMob)
            {
                var roster = ClassArchetype.DefaultRoster;
                if (a.ClassIndex < roster.Count) e = Find(roster[a.ClassIndex].Name);
            }
            else if (!string.IsNullOrEmpty(a.Label))
            {
                e = Find(a.Label) ?? Entries.Where(x => !string.IsNullOrEmpty(x.Key) && a.Label.StartsWith(x.Key, StringComparison.Ordinal)).OrderByDescending(x => x.Key.Length).FirstOrDefault();
            }
            if (e != null && e.Prefab != null) return e;
            return Fallback != null && Fallback.Prefab != null ? Fallback : null;
        }
    }
}
