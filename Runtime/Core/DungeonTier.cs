using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core
{
    /// <summary>
    /// Three dungeons, walled by gear score in both directions. You need a minimum score to go up
    /// and you are refused below a maximum coming down, so a geared player only ever meets geared
    /// players and a fresh one is never farmed by them. Higher tiers raise the item level of every
    /// drop, multiply unique chances, and assume stronger players when tuning mobs and bosses.
    /// Gear score counts what you carry, not just what you wear: see <see cref="Items.GearScore"/>.
    /// The bands overlap a little so a player at a wall can choose which side to fight on.
    /// </summary>
    public sealed class DungeonTier
    {
        public int Number { get; }
        public string Name { get; }
        /// <summary>Inclusive gear-score band a character must fall in to queue for this tier.</summary>
        public int MinGearScore { get; }
        public int MaxGearScore { get; }
        /// <summary>Added to every room's loot tier when rolling items. Tier 1 is the base dungeon.</summary>
        public int ItemLevelOffset { get; }
        public float UniqueChanceMultiplier { get; }
        /// <summary>How much stronger than baseline the tuner assumes players are, for mob and boss budgets.</summary>
        public float AssumedPlayerPower { get; }

        private DungeonTier(int n, string name, int min, int max, int ilvl, float uniq, float power)
        { Number = n; Name = name; MinGearScore = min; MaxGearScore = max; ItemLevelOffset = ilvl; UniqueChanceMultiplier = uniq; AssumedPlayerPower = power; }

        public static readonly IReadOnlyList<DungeonTier> All = new[]
        {
            new DungeonTier(1, "Tier I",   0,   60,            0, 1.0f, 1.0f),   // starter kit scores 9; a full Magic set about 36
            new DungeonTier(2, "Tier II",  40,  220,           2, 1.5f, 1.4f),   // a full Rare set at item level 2 scores about 135
            new DungeonTier(3, "Tier III", 160, int.MaxValue,  4, 2.0f, 1.9f),   // Rare at item level 4 and uniques
        };

        public static DungeonTier For(int number) => All.FirstOrDefault(t => t.Number == number) ?? throw new ArgumentOutOfRangeException(nameof(number));

        public bool Admits(int gearScore) => gearScore >= MinGearScore && gearScore <= MaxGearScore;
        /// <summary>Why a score is refused, or null when admitted.</summary>
        public string Refusal(int gearScore) =>
            gearScore < MinGearScore ? $"{Name} needs gear score {MinGearScore}; yours is {gearScore}" :
            gearScore > MaxGearScore ? $"{Name} is closed above gear score {MaxGearScore}; yours is {gearScore}" : null;

        /// <summary>Every tier a score may queue for. Never empty: the bands cover 0 to infinity.</summary>
        public static IEnumerable<DungeonTier> Open(int gearScore) => All.Where(t => t.Admits(gearScore));

        /// <summary>Stamp this tier onto a settings object (loot level, unique chance, tuning). Returns the same object.</summary>
        public DungeonSettings Apply(DungeonSettings s)
        {
            s.Tier = Number;
            s.ItemLevelOffset = ItemLevelOffset;
            s.UniqueChanceMultiplier = UniqueChanceMultiplier;
            s.Combat.PlayerDps *= AssumedPlayerPower;
            s.Combat.PlayerHealth *= AssumedPlayerPower;
            return s;
        }
    }
}
