using System;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Items;

namespace DungeonGen.Core.Profile
{
    /// <summary>
    /// The only trade at launch. Sells the three consumables and plain Normal starter pieces at a
    /// fixed price; buys any item for silver. No player-to-player trading, no market.
    /// </summary>
    public static class Vendor
    {
        /// <summary>Single Normal pieces still cost silver, but a whole starter kit is always free: see ClaimStarterKit.</summary>
        public const int StarterPieceSilver = 20;

        /// <summary>
        /// The economic floor. A Normal tier-0 set of the character's armour weight and a weapon of
        /// its primary, free, any time, as many times as needed. Worth almost nothing to a killer, so
        /// it is not worth farming; worth everything to a naked player, so nobody is locked out.
        /// </summary>
        public static Equipment ClaimStarterKit(Character character)
        {
            var kit = Equipment.StandardSet(character.Class.Armour, 0, character.Class.Primary);
            foreach (var item in kit.Items.ToList())
            {
                var old = character.Equipment.Equip(item);
                // Anything the starter kit displaces is still the player's: it goes nowhere, they keep wearing the better piece.
                if (old != null && old.Rarity != Rarity.Normal) character.Equipment.Equip(old);
            }
            return kit;
        }

        public static bool Buy(PlayerProfile p, Consumable c, int n = 1)
        {
            long cost = (long)c.VendorPriceSilver * n;
            if (p.Silver < cost) return false;
            p.Silver -= cost;
            p.AddConsumable(c, n);
            return true;
        }

        /// <summary>A Normal, level-0 item of a base: how a naked character gets back on its feet.</summary>
        public static Item BuyStarter(PlayerProfile p, ItemBase b)
        {
            if (p.Silver < StarterPieceSilver) return null;
            p.Silver -= StarterPieceSilver;
            var item = new Item(b, 0, Rarity.Normal, b.Name, new List<AffixRoll>(), new List<AffixRoll>());
            p.Stash.Add(item);
            return item;
        }

        public static float Sell(PlayerProfile p, Item item)
        {
            if (!p.Stash.Remove(item)) return 0f;
            float price = CraftingRules.SellPrice(item);
            p.Silver += (long)Math.Round(price);
            return price;
        }

        public static MaterialBag Salvage(PlayerProfile p, Item item)
        {
            if (!p.Stash.Remove(item)) return null;
            var bag = CraftingRules.Salvage(item);
            p.Materials.Add(bag);
            return bag;
        }

        /// <summary>Crafting costs materials and a silver fee: the silver sink that keeps the vendor's buying from inflating forever.</summary>
        public static Item Craft(PlayerProfile p, CraftRequest req, DungeonRandom rng)
        {
            long fee = CraftingRules.SilverFee(req.Level);
            if (p.Silver < fee) throw new InvalidOperationException($"Cannot craft: needs {fee} silver fee, have {p.Silver}");
            var item = Crafter.Craft(req, p.Materials, rng);
            p.Silver -= fee;
            p.Stash.Add(item);
            return item;
        }
    }
}
