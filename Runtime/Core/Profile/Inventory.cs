using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Items;

namespace DungeonGen.Core.Profile
{
    /// <summary>
    /// What a character carries in the dungeon: worn gear plus a weight-only bag of loot and
    /// consumables. No slots. Over capacity you move slower; nothing is refused.
    /// </summary>
    public sealed class Inventory
    {
        public Character Owner { get; }
        public List<Item> Bag { get; } = new List<Item>();
        public Dictionary<string, int> Consumables { get; } = new Dictionary<string, int>();
        public int Keys { get; set; }

        public Inventory(Character owner) { Owner = owner; }

        public float BagWeightKg => Bag.Sum(i => i.WeightKg) + Consumables.Sum(kv => Consumable.All.First(c => c.Name == kv.Key).WeightKg * kv.Value);
        public float TotalWeightKg => Owner.Equipment.WornWeightKg + BagWeightKg;
        public float CapacityKg(CombatBaseline b = null) => Owner.Sheet(b).CarryKg;
        public bool Overloaded(CombatBaseline b = null) => CarryRules.IsOverloaded(TotalWeightKg, CapacityKg(b));
        /// <summary>The sheet's move speed times the load penalty. What the mover actually uses.</summary>
        public float EffectiveMoveSpeed(CombatBaseline b = null)
        {
            var sheet = Owner.Sheet(b);
            return sheet.MoveSpeed * CarryRules.MoveSpeedFactor(TotalWeightKg, sheet.CarryKg);
        }

        public void Take(Item item) => Bag.Add(item);
        public bool Drop(Item item) => Bag.Remove(item);

        /// <summary>Wear a bagged item; what it replaces goes in the bag.</summary>
        public void Equip(Item item)
        {
            if (!Bag.Remove(item)) return;
            var old = Owner.Equipment.Equip(item);
            if (old != null) Bag.Add(old);
        }

        public void Add(Consumable c, int n = 1) => Consumables[c.Name] = (Consumables.TryGetValue(c.Name, out int k) ? k : 0) + n;
        public bool Use(Consumable c)
        {
            if (!Consumables.TryGetValue(c.Name, out int k) || k <= 0) return false;
            Consumables[c.Name] = k - 1;
            return true;
        }

        /// <summary>Salvage Kit in the dungeon: the item's materials, its weight gone.</summary>
        public MaterialBag SalvageInField(Item item)
        {
            if (!Bag.Contains(item)) return null;
            if (!Use(Consumable.All.First(c => c.Name == "Salvage Kit"))) return null;
            Bag.Remove(item);
            return CraftingRules.Salvage(item);
        }

        /// <summary>Everything that drops when this character dies: worn and carried, half of the worn destroyed, keys always dropped.</summary>
        public (List<Item> dropped, List<Item> destroyed, int keys) OnDeath(DungeonRandom rng)
        {
            var (dropped, destroyed) = DurabilityRules.OnDeath(Owner.Equipment, rng);
            dropped.AddRange(Bag);
            int keys = Keys;
            Bag.Clear(); Consumables.Clear(); Keys = 0;
            var e = Owner.Equipment;
            e.Weapon = e.Head = e.Chest = e.Hands = e.Legs = e.Feet = e.Amulet = e.Ring1 = e.Ring2 = null;
            return (dropped, destroyed, keys);
        }

        /// <summary>Walk out: everything carried goes to the stash, consumables to the profile, keys are destroyed, worn gear takes the trip's wear.</summary>
        public void Extract(PlayerProfile profile, float damageTaken, float damageDealt)
        {
            DurabilityRules.WearForRound(Owner.Equipment, damageTaken, damageDealt);
            profile.Stash.AddRange(Bag);
            foreach (var kv in Consumables) profile.AddConsumable(Consumable.All.First(c => c.Name == kv.Key), kv.Value);
            Bag.Clear(); Consumables.Clear();
            Keys = 0;   // destroyed, nothing back
        }
    }
}
