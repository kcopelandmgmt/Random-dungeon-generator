using System;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Combat;

namespace DungeonGen.Core.Items
{
    /// <summary>What a teammate says about a piece that just dropped for the group.</summary>
    public enum LootRollChoice : byte { Need = 0, Greed = 1, Pass = 2 }

    /// <summary>
    /// Need-over-greed for groups, so a player who queued with strangers still has a fair shot.
    /// When a chest opens, every item at or above <see cref="MinRarity"/> is put to a roll among the
    /// opener's living teammates. Anyone may Need; any Need beats any Greed; among Needs, a player
    /// whose CURRENT class can wear the piece rolls with a bonus, so the Warden usually wins the
    /// plate but the Shade still has a chance at it. No answer inside the window is a Pass. If
    /// everyone passes, the item is free for anyone. Only teammates within <see cref="RollRangeRooms"/>
    /// rooms of the chest are in the roll; solo players and lone wanderers never roll.
    ///
    /// Only the opener's team rolls. The dungeon is full loot: an enemy who reaches the open chest
    /// can grab a piece mid-roll, which cancels that roll, and can take the winner's piece off their
    /// corpse. Rolling is a courtesy between teammates, not a lock on the chest.
    /// </summary>
    public static class LootRollRules
    {
        /// <summary>Seconds a group has to answer before the server closes the roll.</summary>
        public const float RollSeconds = 20f;
        /// <summary>Items below this just get picked up. Magic and above go to a roll.</summary>
        public const Rarity MinRarity = Rarity.Magic;
        /// <summary>Everyone rolls 1..RollMax.</summary>
        public const int RollMax = 100;
        /// <summary>
        /// A teammate must be this close, in corridor hops, to the chest's room to get a roll. The team
        /// that stays together shares its loot; the teammate off doing their own thing does not.
        /// </summary>
        public const int RollRangeRooms = 2;
        /// <summary>Added to a Need roll when the item fits the player's current class. 50 on a d100 means the on-class player wins about seven times in eight against one off-class Need, and about four in five against two.</summary>
        public const int OnClassNeedBonus = 50;

        public static bool GoesToRoll(Item item) => item.Rarity >= MinRarity;

        /// <summary>
        /// Does this piece fit the class the player is playing right now? Weapons: one of the class's
        /// two spec weapons. Armour: the class's armour weight. Jewellery fits everyone.
        /// </summary>
        public static bool FitsClass(ClassArchetype cls, ItemBase b)
        {
            if (cls == null || b == null) return false;
            if (b.IsJewellery) return true;
            if (b.IsWeapon) return WeaponSpec.ForClass(cls.Name).Any(s => s.WeaponBase == b.Name);
            return b.Weight == cls.Armour;
        }

        public readonly struct Entry
        {
            public readonly int Player; public readonly LootRollChoice Choice; public readonly bool OnClass;
            public Entry(int player, LootRollChoice choice, bool onClass) { Player = player; Choice = choice; OnClass = onClass; }
        }

        public readonly struct Outcome
        {
            /// <summary>-1 when everyone passed: the item is free for anyone.</summary>
            public readonly int Winner;
            public readonly int WinningRoll;
            public readonly IReadOnlyDictionary<int, int> Rolls;
            public Outcome(int winner, int roll, IReadOnlyDictionary<int, int> rolls) { Winner = winner; WinningRoll = roll; Rolls = rolls; }
        }

        /// <summary>
        /// The server's decision. Needs beat Greeds outright; within the winning tier everyone rolls
        /// 1..RollMax, on-class Needs add the bonus, highest wins, ties broken by a re-roll. The
        /// server runs this with its own RNG and publishes the result as an event, so clients never
        /// see a number before they choose.
        /// </summary>
        public static Outcome Resolve(IEnumerable<Entry> entries, DungeonRandom rng)
        {
            var list = entries.Where(e => e.Choice != LootRollChoice.Pass).ToList();
            var rolls = new Dictionary<int, int>();
            if (list.Count == 0) return new Outcome(-1, 0, rolls);
            var tier = list.Any(e => e.Choice == LootRollChoice.Need) ? LootRollChoice.Need : LootRollChoice.Greed;
            var contenders = list.Where(e => e.Choice == tier).ToList();
            while (true)
            {
                int best = -1, bestRoll = int.MinValue; bool tie = false;
                foreach (var e in contenders)
                {
                    int r = rng.Range(1, RollMax + 1) + (tier == LootRollChoice.Need && e.OnClass ? OnClassNeedBonus : 0);
                    rolls[e.Player] = r;
                    if (r > bestRoll) { best = e.Player; bestRoll = r; tie = false; }
                    else if (r == bestRoll) tie = true;
                }
                if (!tie) return new Outcome(best, bestRoll, rolls);
                contenders = contenders.Where(e => rolls[e.Player] == bestRoll).ToList();   // re-roll only the tied
            }
        }

        /// <summary>Chance the on-class Need wins against n off-class Needs, by brute force over the d100s. Used by tests and the design doc.</summary>
        public static double OnClassWinChance(int offClassNeeders)
        {
            if (offClassNeeders <= 0) return 1.0;
            double wins = 0, total = 0;
            for (int mine = 1; mine <= RollMax; mine++)
            {
                int eff = mine + OnClassNeedBonus;
                // P(all off-class rolls < eff) + ties split evenly by re-roll (approximated as half)
                double below = Math.Min(RollMax, eff - 1) / (double)RollMax;
                double tieOne = eff <= RollMax ? 1.0 / RollMax : 0.0;
                wins += Math.Pow(below, offClassNeeders) + (offClassNeeders == 1 ? tieOne * 0.5 : 0.0);
                total += 1;
            }
            return wins / total;
        }
    }
}
