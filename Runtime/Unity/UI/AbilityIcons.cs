using System;
using System.Collections.Generic;
using UnityEngine;

namespace DungeonGen.Unity.UI
{
    /// <summary>
    /// Ability id → icon sprite. The asset lives at <c>Assets/UI/AbilityIcons/Resources/AbilityIcons.asset</c> and is filled by
    /// <c>Editor/AbilityIconBaker.Bake</c> (one 128×128 PNG per ability in <c>AbilityBook.All</c>, rendered from the ability's effect
    /// prefab, framed by element). <see cref="Get"/> loads it once from Resources; a missing asset or id returns null and the HUD
    /// shows the slot without an icon.
    /// </summary>
    [CreateAssetMenu(menuName = "Dungeon Gen/Ability Icons", fileName = "AbilityIcons")]
    public sealed class AbilityIcons : ScriptableObject
    {
        public const string ResourceName = "AbilityIcons";

        [Serializable]
        public sealed class Entry
        {
            public string AbilityId;
            public Sprite Icon;
        }

        public List<Entry> Entries = new List<Entry>();

        private Dictionary<string, Sprite> _map;
        private static AbilityIcons _loaded;
        private static bool _tried;

        public Sprite Find(string abilityId)
        {
            if (string.IsNullOrEmpty(abilityId)) return null;
            if (_map == null)
            {
                _map = new Dictionary<string, Sprite>();
                foreach (var e in Entries) if (e != null && !string.IsNullOrEmpty(e.AbilityId) && e.Icon != null) _map[e.AbilityId] = e.Icon;
            }
            return _map.TryGetValue(abilityId, out var s) ? s : null;
        }

        /// <summary>The icon for an ability id from the shared asset, or null.</summary>
        public static Sprite Get(string abilityId)
        {
            if (!_tried) { _tried = true; _loaded = Resources.Load<AbilityIcons>(ResourceName); }
            return _loaded != null ? _loaded.Find(abilityId) : null;
        }
    }
}
