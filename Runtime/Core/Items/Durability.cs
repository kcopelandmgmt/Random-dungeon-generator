using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core.Items
{
    /// <summary>
    /// The state of one item's wear. The only mutable part of an item. Current durability falls
    /// with use; a repair refills it but lowers the maximum, so every item is finite.
    /// </summary>
    public sealed class ItemCondition
    {
        public float Max { get; private set; }
        public float Current { get; private set; }
        public int Repairs { get; private set; }

        public ItemCondition(float max, float current) { Max = max; Current = Math.Min(current, max); }

        public bool IsBroken => Current <= 0f && Max > 0f;
        /// <summary>No maximum left: the item is gone for good.</summary>
        public bool IsDestroyed => Max <= 0f;
        public float Fraction => Max <= 0f ? 0f : Current / Max;

        public void Wear(float points) => Current = Math.Max(0f, Current - Math.Max(0f, points));

        /// <summary>Refill to the (reduced) maximum. Returns false if the item cannot take another repair.</summary>
        public bool Repair()
        {
            if (IsDestroyed) return false;
            Max = Math.Max(0f, Max - DurabilityRules.RepairMaxLoss);
            Current = Max;
            Repairs++;
            return !IsDestroyed;
        }

        public void Destroy() { Max = 0f; Current = 0f; }

        public override string ToString() => IsDestroyed ? "destroyed" : IsBroken ? $"BROKEN (max {Max:0})" : $"{Current:0}/{Max:0}";
    }

    /// <summary>
    /// Every durability number in one place. Tuned so that an item in constant use lasts a handful
    /// of rounds before its first break, a dozen or so with repairs, and never forever.
    /// </summary>
    public static class DurabilityRules
    {
        /// <summary>Points of maximum durability by rarity. Uniques are the prize; they last longest.</summary>
        public static float MaxFor(Rarity r) => r == Rarity.Unique ? 150f : r == Rarity.Rare ? 120f : 100f;

        /// <summary>Each repair takes this many points off the maximum. 100 max → six repairs at most.</summary>
        public const float RepairMaxLoss = 15f;

        /// <summary>Armour: 1 point per this much raw damage taken, on every worn piece.</summary>
        public const float ArmourDamagePerPoint = 100f;
        /// <summary>Weapon: 1 point per this much damage dealt.</summary>
        public const float WeaponDamagePerPoint = 200f;
        /// <summary>Jewellery wears with time rather than hits: flat loss per extraction.</summary>
        public const float JewelleryWearPerRound = 15f;
        /// <summary>Everything also loses this on every extraction (the trip itself).</summary>
        public const float BaseWearPerRound = 5f;

        /// <summary>On death each item is destroyed with this chance; the rest drop for the killer. Keys always drop.</summary>
        public const float DeathTrashChance = 0.5f;

        /// <summary>Items found in ordinary chests spawn between these fractions of max: found gear is used gear.</summary>
        public const float FoundMinFraction = 0.6f;
        public const float FoundMaxFraction = 1.0f;

        /// <summary>Repair price in silver: proportional to the item's value and how much is missing.</summary>
        public static float RepairCost(Item item) => item.Value * 10f * (1f - item.Condition.Fraction) + item.Value * 2f;

        /// <summary>Apply one round's wear: damage the wearer took and dealt, plus the extraction itself.</summary>
        public static void WearForRound(Equipment e, float damageTaken, float damageDealt)
        {
            foreach (var it in e.Items)
            {
                if (it.Condition.IsDestroyed) continue;
                float w = BaseWearPerRound;
                if (it.Base.IsArmour) w += damageTaken / ArmourDamagePerPoint;
                else if (it.Base.IsWeapon) w += damageDealt / WeaponDamagePerPoint;
                else w += JewelleryWearPerRound;
                it.Condition.Wear(w);
            }
        }

        /// <summary>What happens to the gear of a player who died: (dropped for the killer, destroyed).</summary>
        public static (List<Item> dropped, List<Item> destroyed) OnDeath(Equipment e, DungeonRandom rng)
        {
            var dropped = new List<Item>();
            var destroyed = new List<Item>();
            foreach (var it in e.Items)
            {
                if (rng.Chance(DeathTrashChance)) { it.Condition.Destroy(); destroyed.Add(it); }
                else dropped.Add(it);
            }
            return (dropped, destroyed);
        }

        /// <summary>
        /// How many rounds an item survives in constant use, repairing whenever it breaks, if its
        /// owner never dies. The design number for "how long does gear last".
        /// </summary>
        public static int ExpectedLifetimeRounds(Item item, float damageTakenPerRound, float damageDealtPerRound)
        {
            var probe = new ItemCondition(DurabilityRules.MaxFor(item.Rarity), DurabilityRules.MaxFor(item.Rarity));
            float perRound = BaseWearPerRound + (item.Base.IsArmour ? damageTakenPerRound / ArmourDamagePerPoint : item.Base.IsWeapon ? damageDealtPerRound / WeaponDamagePerPoint : JewelleryWearPerRound);
            int rounds = 0;
            while (!probe.IsDestroyed && rounds < 1000)
            {
                probe.Wear(perRound);
                rounds++;
                if (probe.IsBroken) probe.Repair();
            }
            return rounds;
        }
    }
}
