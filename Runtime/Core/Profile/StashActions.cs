using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Items;

namespace DungeonGen.Core.Profile
{
    /// <summary>
    /// Between rounds: moving gear between the shared stash and a character's paper doll, and what the tier walls would say.
    /// Every call either changes the profile and returns null, or changes nothing and returns why not; the caller saves.
    /// One of each unique when worn (2026-10-22 fight rules #8); starter gear moves like anything else (it is free, not bound).
    /// The vendor's own rules (buy, sell, salvage) stay in <see cref="Vendor"/>.
    /// </summary>
    public static class StashActions
    {
        /// <summary>Wear a stash item; whatever it displaces goes into the stash.</summary>
        public static string Equip(PlayerProfile p, Character ch, Item item)
        {
            if (p == null || ch == null || item == null) return "Nothing to equip.";
            if (!p.Characters.Contains(ch)) return $"{ch.Name} is not on this account.";
            if (!p.Stash.Any(s => ReferenceEquals(s, item))) return $"{item.Name} is not in the stash.";
            var why = EquipPreview.Refusal(ch.Equipment, item);
            if (why != null) return why;
            p.Stash.Remove(item);
            var off = EquipPreview.Wear(ch.Equipment, item);
            if (off != null) p.Stash.Add(off);
            return null;
        }

        /// <summary>Take a worn item off into the stash.</summary>
        public static string Unequip(PlayerProfile p, Character ch, Item item)
        {
            if (p == null || ch == null || item == null) return "Nothing to take off.";
            if (!ch.Equipment.IsWearing(item)) return $"{ch.Name} is not wearing {item.Name}.";
            ch.Equipment.Remove(item);
            p.Stash.Add(item);
            return null;
        }

        /// <summary>The character's gear score (worn; the menu brings nothing else in) if this stash item were put on.</summary>
        public static int GearScoreIfWorn(Character ch, Item item) => GearScore.Of(ch.Equipment) + EquipPreview.GearScoreDelta(ch.Equipment, item);

        /// <summary>The tier walls for a gear score, for a hint line: which tiers admit it, and the bands.</summary>
        public static string TierHint(int gearScore)
        {
            var open = DungeonTier.Open(gearScore).Select(t => t.Name).ToList();
            string bands = string.Join(" · ", DungeonTier.All.Select(t => t.MaxGearScore == int.MaxValue ? $"{t.Name} {t.MinGearScore}+" : $"{t.Name} {t.MinGearScore}–{t.MaxGearScore}"));
            return open.Count == 0 ? $"Gear score {gearScore} fits no tier ({bands})." : $"Gear score {gearScore} admits {string.Join(" and ", open)} ({bands}).";
        }

        /// <summary>The worn items by paper-doll slot, in order, nulls included (rings are two slots).</summary>
        public static IReadOnlyList<(string slot, Item item)> PaperDoll(Character ch)
        {
            var e = ch.Equipment;
            return new List<(string, Item)> { ("Weapon", e.Weapon), ("Head", e.Head), ("Chest", e.Chest), ("Hands", e.Hands), ("Legs", e.Legs), ("Feet", e.Feet), ("Amulet", e.Amulet), ("Ring", e.Ring1), ("Ring", e.Ring2) };
        }
    }
}
