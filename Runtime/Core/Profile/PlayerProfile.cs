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
        /// <summary>Account XP and the level it buys. Levels gate the three earned classes and nothing else: see <see cref="AccountRules"/>.</summary>
        public long AccountXp { get; private set; }
        public int AccountLevel => AccountRules.LevelFor(AccountXp);
        public void AddAccountXp(long xp) { if (xp > 0) AccountXp += xp; }
        public bool ClassUnlocked(ClassArchetype cls) => AccountRules.ClassUnlocked(cls.Name, AccountLevel);
        public IEnumerable<ClassArchetype> PlayableClasses => ClassArchetype.DefaultRoster.Where(ClassUnlocked);

        /// <summary>Eight character slots per account; the stash, silver and materials are one shared bank between them.</summary>
        public const int MaxCharacters = 8;

        public PlayerProfile(string accountId) { AccountId = accountId; }

        /// <summary>A brand-new account: one character of the chosen class in the free kit, 200 silver, two health potions.</summary>
        public static PlayerProfile NewAccount(string accountId, string characterName, ClassArchetype cls)
        {
            var p = new PlayerProfile(accountId) { Silver = FightRules.StartingSilver };
            var ch = p.CreateCharacter(characterName, cls);
            Vendor.ClaimStarterKit(ch);
            p.AddConsumable(Consumable.All.First(c => c.Name == "Health Potion"), FightRules.StartingHealthPotions);
            return p;
        }

        public Character CreateCharacter(string name, ClassArchetype cls)
        {
            if (Characters.Count >= MaxCharacters) throw new System.InvalidOperationException($"All {MaxCharacters} character slots are used.");
            if (!ClassUnlocked(cls)) throw new System.InvalidOperationException($"{cls.Name} unlocks at account level {AccountRules.LevelForClass(cls.Name)}.");
            var ch = new Character(name, cls);
            Characters.Add(ch);
            return ch;
        }

        /// <summary>
        /// Keys of the rounds already settled into this profile, oldest first, so a retried or
        /// replayed settlement changes nothing (<see cref="RoundSettlement"/>). Saved with the profile
        /// (<see cref="ProfileCodec"/>); only the newest <see cref="SettledRoundsKept"/> are kept.
        /// </summary>
        public IReadOnlyList<string> SettledRounds => _settledRounds;
        private readonly List<string> _settledRounds = new List<string>();
        public const int SettledRoundsKept = 128;
        /// <summary>Which victims this account has already been paid kill XP for today (2026-10-26 audit #8). Saved with the profile (<see cref="ProfileCodec"/> version 2).</summary>
        public KillXpTracker KillXp { get; } = new KillXpTracker();
        public bool IsSettled(string roundKey) => _settledRounds.Contains(roundKey);
        public void MarkSettled(string roundKey)
        {
            if (string.IsNullOrEmpty(roundKey) || _settledRounds.Contains(roundKey)) return;
            _settledRounds.Add(roundKey);
            while (_settledRounds.Count > SettledRoundsKept) _settledRounds.RemoveAt(0);
        }

        public int ConsumableCount(Consumable c) => Consumables.TryGetValue(c.Name, out int n) ? n : 0;
        public void AddConsumable(Consumable c, int n) => Consumables[c.Name] = ConsumableCount(c) + n;
        /// <summary>Take up to <paramref name="n"/> away; returns how many were actually removed. A count of zero drops the entry.</summary>
        public int RemoveConsumable(Consumable c, int n)
        {
            int have = ConsumableCount(c), take = System.Math.Max(0, System.Math.Min(have, n));
            if (have - take <= 0) Consumables.Remove(c.Name); else Consumables[c.Name] = have - take;
            return take;
        }
    }
}
