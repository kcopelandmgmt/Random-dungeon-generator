using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Items;

namespace DungeonGen.Core.Profile
{
    /// <summary>
    /// Everything that persists between rounds, and nothing else. Flat characters by decision: a
    /// character is a class and a name, its attributes are the class's and never change, and the
    /// only vertical power is what it is wearing. There is no level, no experience, no talent tree.
    /// </summary>
    public sealed class Character
    {
        public string Name { get; }
        public ClassArchetype Class { get; }
        /// <summary>Always the class's base attributes. Exists so nothing can quietly grow it.</summary>
        public Attributes Attributes => Class.BaseAttributes;
        public Equipment Equipment { get; } = new Equipment();

        public Character(string name, ClassArchetype cls) { Name = name; Class = cls; }

        public PlayerStats Sheet(CombatBaseline baseline = null) => PlayerStats.For(Class, Equipment, baseline);
        /// <summary>The spec the character is currently playing: its weapon's spec, or null for fists / a weapon with no spec for this class.</summary>
        public WeaponSpec Spec => WeaponSpec.For(Class.Name, Equipment.Weapon?.Base);
    }

    public sealed class PlayerProfile
    {
        public string AccountId { get; }
        public long Silver { get; set; }
        public MaterialBag Materials { get; } = new MaterialBag();
        /// <summary>The stash. Gear here is not being worn and so does not wear; it also cannot be used until equipped, and it breaks like anything else once it is.</summary>
        public List<Item> Stash { get; } = new List<Item>();
        public Dictionary<string, int> Consumables { get; } = new Dictionary<string, int>();
        public List<Character> Characters { get; } = new List<Character>();
        /// <summary>Account-level unlocks: cosmetics and recipes only, never power.</summary>
        public HashSet<string> Unlocks { get; } = new HashSet<string>();

        public PlayerProfile(string accountId) { AccountId = accountId; }

        public int ConsumableCount(Consumable c) => Consumables.TryGetValue(c.Name, out int n) ? n : 0;
        public void AddConsumable(Consumable c, int n) => Consumables[c.Name] = ConsumableCount(c) + n;
    }
}
