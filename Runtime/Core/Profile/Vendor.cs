using System;
using System.Collections.Generic;
using DungeonGen.Core.Items;

namespace DungeonGen.Core.Profile
{
    /// <summary>
    /// The only trade at launch. Sells the three consumables and plain Normal starter pieces at a
    /// fixed price; buys any item for silver. No player-to-player trading, no market.
    /// </summary>
    public static class Vendor
    {
        public const int StarterPieceSilver = 20;

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

        public static Item Craft(PlayerProfile p, CraftRequest req, DungeonRandom rng)
        {
            var item = Crafter.Craft(req, p.Materials, rng);
            p.Stash.Add(item);
            return item;
        }
    }
}
