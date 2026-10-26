using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core.Items
{
    /// <summary>
    /// What wearing an item would change, for a tooltip or the stash screen: the piece it would take off and the gear-score
    /// difference. Pure reads; nothing is equipped. One of each unique (2026-10-22 fight rules #8): a second copy is refused
    /// unless it replaces the copy in the same slot.
    /// </summary>
    public static class EquipPreview
    {
        /// <summary>Build the worn set from a list of items (in-round: the ids the player brought in as worn), slot by slot as <see cref="Equipment.Equip"/> fills it.</summary>
        public static Equipment FromWorn(IEnumerable<Item> worn)
        {
            var e = new Equipment();
            foreach (var it in worn ?? Enumerable.Empty<Item>()) if (it != null && !e.WouldDuplicateUnique(it)) e.Equip(it);
            return e;
        }

        /// <summary>The piece wearing <paramref name="candidate"/> takes off, or null for a free slot (or when it is already worn).</summary>
        public static Item Displaced(Equipment worn, Item candidate)
        {
            if (worn == null || candidate == null || worn.IsWearing(candidate)) return null;
            return worn.WouldDisplace(candidate);
        }

        /// <summary>Why <paramref name="candidate"/> cannot be worn now, or null. Only the unique rule refuses: anyone can wear anything.</summary>
        public static string Refusal(Equipment worn, Item candidate)
        {
            if (candidate == null) return "nothing to wear";
            if (worn == null || worn.IsWearing(candidate)) return null;
            if (!worn.WouldDuplicateUnique(candidate)) return null;
            var out_ = worn.WouldDisplace(candidate);
            bool replacesTheCopy = out_ != null && out_.Unique == candidate.Unique && worn.Items.Count(i => i.Unique == candidate.Unique) == 1;
            return replacesTheCopy ? null : $"Already wearing {candidate.Unique.Name}: one of each unique.";
        }

        /// <summary>Gear score of the worn set after putting <paramref name="candidate"/> on, minus before. The tier walls count worn and carried, so in the bag it already counts; this is the paper-doll change.</summary>
        public static int GearScoreDelta(Equipment worn, Item candidate)
        {
            if (candidate == null || worn == null || worn.IsWearing(candidate)) return 0;
            return GearScore.Of(candidate) - GearScore.Of(Displaced(worn, candidate));
        }

        /// <summary>Put it on (the stash screen's swap): honours the unique rule, swapping a copy for a copy in the same slot. Returns what came off, or throws like <see cref="Equipment.Equip"/>.</summary>
        public static Item Wear(Equipment worn, Item candidate)
        {
            var why = Refusal(worn, candidate);
            if (why != null) throw new System.InvalidOperationException(why);
            var off = worn.WouldDisplace(candidate);
            if (off != null && off.Unique != null && off.Unique == candidate.Unique) { worn.Remove(off); worn.Equip(candidate); return off; }
            return worn.Equip(candidate);
        }
    }
}
