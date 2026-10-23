using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core.Items
{
    /// <summary>
    /// What the tier walls measure. Item level times a rarity weight, summed over everything a
    /// character brings into the dungeon: worn AND carried, so nobody walks a high-tier weapon into
    /// a low-tier dungeon in their bag. Consumables and silver do not count. One item's score is its
    /// <see cref="Item.Value"/>, so the number players see at the vendor is the number at the gate.
    /// </summary>
    public static class GearScore
    {
        public static int Of(Item item) => item == null ? 0 : item.Value;
        public static int Of(IEnumerable<Item> items) => items?.Sum(Of) ?? 0;
        public static int Of(Equipment worn, IEnumerable<Item> carried = null) => Of(worn?.Items ?? Enumerable.Empty<Item>()) + Of(carried ?? Enumerable.Empty<Item>());
    }
}
