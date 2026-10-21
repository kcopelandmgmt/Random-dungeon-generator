using System.Collections.Generic;

namespace DungeonGen.Core.Combat
{
    /// <summary>
    /// A modifier rolled onto a boss per dungeon. Affixes multiply the budget instead of adding
    /// mechanics, so eight bosses become hundreds of fights without new art and without the
    /// tuning drifting away from the 2.5-player line.
    /// </summary>
    public sealed class BossAffix
    {
        public string Name { get; }
        public string Description { get; }
        public float HealthMultiplier { get; }
        public float DpsMultiplier { get; }
        /// <summary>Multiplier on enrage time: below 1 the boss enrages sooner.</summary>
        public float EnrageMultiplier { get; }
        /// <summary>Extra loot rolls from the boss's chest.</summary>
        public int BonusLootRolls { get; }
        /// <summary>Relative roll weight. 0 = never rolled randomly (assigned by rule instead).</summary>
        public int Weight { get; }

        public BossAffix(string name, string description, float health = 1f, float dps = 1f, float enrage = 1f, int bonusLoot = 0, int weight = 10)
        {
            Name = name;
            Description = description;
            HealthMultiplier = health;
            DpsMultiplier = dps;
            EnrageMultiplier = enrage;
            BonusLootRolls = bonusLoot;
            Weight = weight;
        }

        /// <summary>Assigned, never rolled: every boss that holds a vault key gets it, so the tell is visible from the door.</summary>
        public static readonly BossAffix Keybearer = new BossAffix("Keybearer",
            "Carries a vault key on a chain around its neck. Everyone in the room can see it.", bonusLoot: 0, weight: 0);

        public static readonly IReadOnlyList<BossAffix> DefaultPool = new[]
        {
            new BossAffix("Armoured", "30% more health. A longer fight and more time for the neighbours to notice.", health: 1.3f, bonusLoot: 1),
            new BossAffix("Frenzied", "Enrages at 1.5x the target time instead of 2x and hits 10% harder.", dps: 1.1f, enrage: 0.75f, bonusLoot: 1),
            new BossAffix("Swift", "Moves and attacks faster. Chasers catch, space-denial bosses close the gap.", dps: 1.1f),
            new BossAffix("Venomous", "Every hit also poisons. Mender Purge becomes mandatory.", dps: 1.15f, bonusLoot: 1),
            new BossAffix("Thorned", "Reflects 15% of melee damage. Reavers bleed for it.", health: 1.1f),
            new BossAffix("Shrouded", "The room is dark; only the boss and the doorway are lit.", weight: 6),
            new BossAffix("Hoarder", "No stronger, but two extra loot rolls. The one everyone wants to third-party.", bonusLoot: 2, weight: 8),
            new BossAffix("Ancient", "20% more health and damage, enrages later, one extra loot roll.", health: 1.2f, dps: 1.2f, enrage: 1.25f, bonusLoot: 1, weight: 4),
        };
    }
}
