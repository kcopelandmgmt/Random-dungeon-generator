using System;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Items;
using DungeonGen.Core.Net;

namespace DungeonGen.Core.Profile
{
    /// <summary>
    /// What one character takes into one round: the sheet its gear gives it, every item it brings
    /// (worn, plus anything carried out of the shared stash), and the consumables in its pockets.
    /// Built from the profile without changing it; the profile changes only when the round is
    /// settled (<see cref="RoundSettlement"/>), so the host must not start the same account in a
    /// second round before that.
    /// Gear score counts worn AND carried (2026-10-23 "Tiers, hand-offs…" #2), so carried stash
    /// items are part of the loadout and of <see cref="GearScore"/>; consumables and silver never are.
    /// No silver goes in: silver is found inside and extracted (2026-10-23 "Economy-in-the-dungeon" #1).
    /// </summary>
    public sealed class Loadout
    {
        public PlayerProfile Profile { get; }
        public Character Character { get; }
        /// <summary>The sheet from the character's equipment (<see cref="Character.Sheet"/>). Flat characters: gear is the only thing that moves it.</summary>
        public PlayerStats Stats { get; }
        /// <summary>The spec the weapon in hand gives, or the class's first spec for an empty hand. What the server seats.</summary>
        public string Spec { get; }
        /// <summary>Worn items, in slot order.</summary>
        public IReadOnlyList<Item> Worn { get; }
        /// <summary>Items carried in from the stash (they stay in the stash list until settlement decides their fate).</summary>
        public IReadOnlyList<Item> Carried { get; }
        /// <summary>Consumable name → count carried in. Taken from the profile's bank at settlement if the character dies.</summary>
        public IReadOnlyDictionary<string, int> Consumables { get; }

        /// <summary>Everything to register with the server: worn first, then carried.</summary>
        public IEnumerable<Item> Brought => Worn.Concat(Carried);
        /// <summary>The tier-wall number: worn and carried.</summary>
        public int GearScore => Items.GearScore.Of(Brought);

        private Loadout(PlayerProfile p, Character c, PlayerStats stats, string spec, List<Item> worn, List<Item> carried, Dictionary<string, int> consumables)
        {
            Profile = p; Character = c; Stats = stats; Spec = spec; Worn = worn; Carried = carried; Consumables = consumables;
        }

        /// <summary>
        /// The loadout of <paramref name="character"/> on <paramref name="profile"/>. Carried items must be in
        /// the stash and not repeated; consumables must not exceed the bank. Null carried / consumables mean none.
        /// </summary>
        public static Loadout For(PlayerProfile profile, Character character, IEnumerable<Item> carriedFromStash = null, IReadOnlyDictionary<string, int> consumables = null, CombatBaseline baseline = null)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (character == null) throw new ArgumentNullException(nameof(character));
            if (!profile.Characters.Contains(character)) throw new InvalidOperationException($"'{character.Name}' is not a character on account {profile.AccountId}.");
            var worn = character.Equipment.Items.ToList();
            var carried = new List<Item>();
            foreach (var it in carriedFromStash ?? Enumerable.Empty<Item>())
            {
                if (!profile.Stash.Any(s => ReferenceEquals(s, it))) throw new InvalidOperationException($"'{it.Name}' is not in the stash.");
                if (carried.Any(c => ReferenceEquals(c, it))) throw new InvalidOperationException($"'{it.Name}' is carried twice.");
                carried.Add(it);
            }
            var cons = new Dictionary<string, int>();
            foreach (var kv in consumables ?? new Dictionary<string, int>())
            {
                var c = Consumable.ByName(kv.Key) ?? throw new InvalidOperationException($"No consumable '{kv.Key}'.");
                if (kv.Value < 0) throw new InvalidOperationException($"Cannot carry {kv.Value} {kv.Key}.");
                if (kv.Value > profile.ConsumableCount(c)) throw new InvalidOperationException($"Carrying {kv.Value} {kv.Key}, the bank holds {profile.ConsumableCount(c)}.");
                if (kv.Value > 0) cons[kv.Key] = kv.Value;
            }
            var stats = character.Sheet(baseline);
            string spec = stats.Spec?.Name ?? stats.ElementKit?.Name ?? WeaponSpec.ForClass(character.Class.Name).FirstOrDefault()?.Name ?? stats.SpecName;
            return new Loadout(profile, character, stats, spec, worn, carried, cons);
        }

        /// <summary>
        /// The <see cref="MatchEventKind.ItemRegistered"/> events for this loadout: one per brought item, ids
        /// from <paramref name="firstId"/> (at least <see cref="MatchState.BroughtItemIdBase"/>) upward, value = <see cref="Item.Value"/>.
        /// </summary>
        public List<(int id, Item item, MatchEvent registration)> Registrations(int playerId, int firstId, float time)
        {
            if (firstId < MatchState.BroughtItemIdBase) throw new ArgumentOutOfRangeException(nameof(firstId), $"Brought-in item ids start at {MatchState.BroughtItemIdBase}.");
            var list = new List<(int, Item, MatchEvent)>();
            int id = firstId;
            foreach (var it in Brought) { list.Add((id, it, new MatchEvent(MatchEventKind.ItemRegistered, time, id, playerId, it.Value))); id++; }
            return list;
        }

        /// <summary>The tier wall for the round's tier (2026-10-23 "Tiers, hand-offs…" #1): null when admitted, else why not.</summary>
        public string TierRefusal(int tierNumber)
        {
            var tier = DungeonTier.All.FirstOrDefault(t => t.Number == tierNumber);
            return tier?.Refusal(GearScore);
        }
    }
}
