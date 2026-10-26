using System.Collections.Generic;

namespace DungeonGen.Core.Items
{
    /// <summary>
    /// The small consumable set. Bought from the vendor for silver (the silver sink) or found in
    /// ordinary chests. Deliberately few: a trio without a Mender should survive a room boss at
    /// duo odds, not become a Mender.
    /// </summary>
    public sealed class Consumable
    {
        public string Name { get; }
        public string Effect { get; }
        public float CooldownSeconds { get; }
        public int VendorPriceSilver { get; }
        /// <summary>Chance an ordinary chest also holds one.</summary>
        public float ChestChance { get; }
        public float WeightKg { get; }

        public Consumable(string name, string effect, float cooldown, int price, float chestChance, float weight)
        {
            Name = name; Effect = effect; CooldownSeconds = cooldown; VendorPriceSilver = price; ChestChance = chestChance; WeightKg = weight;
        }

        public const string HealthPotionName = "Health Potion", AntidoteName = "Antidote", SalvageKitName = "Salvage Kit";
        /// <summary>The Health Potion heals this fraction of max health, evenly over <see cref="HealthPotionSeconds"/> (2026-10-22 "Open questions closed" #8).</summary>
        public const float HealthPotionFraction = 0.30f, HealthPotionSeconds = 3f;

        public static readonly IReadOnlyList<Consumable> All = new[]
        {
            new Consumable("Health Potion", "Heal 30% of max health over 3 s.", 60f, 40, 0.20f, 0.5f),
            new Consumable("Antidote", "Cleanse every status effect (a bottled Purge).", 90f, 60, 0.10f, 0.3f),
            new Consumable("Salvage Kit", "Break an item down in the dungeon: its materials go in the bag, its weight does not.", 0f, 25, 0.10f, 1f),
        };

        /// <summary>The consumable with this exact name, or null. Profiles and inventories key consumables by name.</summary>
        public static Consumable ByName(string name) { foreach (var c in All) if (c.Name == name) return c; return null; }

        public override string ToString() => $"{Name} ({VendorPriceSilver} silver, {WeightKg} kg): {Effect}";
    }
}
