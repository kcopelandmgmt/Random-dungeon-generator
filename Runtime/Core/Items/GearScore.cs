using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core.Items
{
    /// <summary>
    /// What the tier walls measure. Item level times a rarity weight, summed over everything a
    /// character brings into the dungeon: worn AND carried, so nobody walks a high-tier weapon into
    /// a low-tier dungeon in their bag. Consumables and silver do not count. The rarity weights are
    /// gentler than the vendor's (1/2/3/4, not 1/2/5/12): the vendor prices a unique at twelve
    /// Normals, but one unique must not throw a starter character out of Tier I on its own.
    /// A starter kit (weapon and five armour pieces) scores 6; nine Rares at item level 4 score 135; a unique at level 4 scores 20.
    /// </summary>
    public static class GearScore
    {
        public static int Weight(Rarity r) => r == Rarity.Unique ? 4 : r == Rarity.Rare ? 3 : r == Rarity.Magic ? 2 : 1;
        public static int Of(Item item) => item == null ? 0 : (item.ItemLevel + 1) * Weight(item.Rarity);
        public static int Of(IEnumerable<Item> items) => items?.Sum(Of) ?? 0;
        public static int Of(Equipment worn, IEnumerable<Item> carried = null) => Of(worn?.Items ?? Enumerable.Empty<Item>()) + Of(carried ?? Enumerable.Empty<Item>());
    }
}
