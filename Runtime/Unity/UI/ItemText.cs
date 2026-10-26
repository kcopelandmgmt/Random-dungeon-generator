using System;
using System.Linq;
using System.Text;
using DungeonGen.Core.Items;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DungeonGen.Unity.UI
{
    /// <summary>How an item reads everywhere (loot rows, inventory, rolls, tooltips, the stash and the vendor): rarity colour, base, item level, affixes, weight.</summary>
    public static class ItemText
    {
        public static Color Colour(Rarity r) => MainMenuUi.RarityColour(r);

        public static string Name(Item i) => i == null ? "?" : UiStyle.Colour(i.Name, Colour(i.Rarity));

        /// <summary>"Rare Plate Helm · iL 3 · 4.3 kg"</summary>
        public static string Line(Item i) =>
            i == null ? "" : $"{(i.Rarity == Rarity.Normal ? "" : i.Rarity + " ")}{i.Base.Name} · iL {i.ItemLevel} · {i.WeightKg:0.#} kg{(i.IsStarter ? " · starter" : "")}{(i.Condition.IsBroken ? " · " + UiStyle.Colour("broken", UiStyle.Bad) : "")}";

        /// <summary>The legendary first, then the first few affixes: what a row has room for.</summary>
        public static string KeyAffixes(Item i, int n = 2)
        {
            if (i == null) return "";
            var parts = i.Affixes.Take(n).Select(a => a.ToString()).ToList();
            if (i.Unique?.Legendary != null) parts.Insert(0, "★ " + i.Unique.Legendary.Name);
            int more = i.Affixes.Count() - Math.Min(n, i.Affixes.Count());
            return string.Join("  ·  ", parts) + (more > 0 ? $"  · +{more} more" : "");
        }

        public static string Stats(Item i)
        {
            if (i == null) return "";
            if (i.Base.IsWeapon) return $"Damage ×{i.WeaponDamage:0.00}  ·  speed ×{i.Base.WeaponSpeed:0.00}  ·  {i.Base.AttackType}{(i.Base.ScalesWith.HasValue ? "  ·  " + i.Base.ScalesWith : "")}";
            if (i.Base.IsArmour) return $"Armour {i.Armour:0}{(i.BaseResist > 0f ? $"  ·  resist {i.BaseResist:0}" : "")}  ·  {i.Base.Weight} {i.Base.Slot}";
            return i.Base.Slot.ToString();
        }

        /// <summary>The full card: name, line, stats, every affix, the legendary, durability, weight, gear score, and the change if worn.</summary>
        public static string Tooltip(Item i, Equipment worn = null, string footer = null)
        {
            if (i == null) return "";
            var sb = new StringBuilder();
            sb.Append($"<size=26><b>{Name(i)}</b></size>\n");
            sb.Append(UiStyle.Colour(Line(i), UiStyle.TextDim)).Append('\n');
            sb.Append(Stats(i)).Append('\n');
            if (i.Unique?.Legendary != null)
            {
                var l = i.Unique.Legendary;
                sb.Append('\n').Append(UiStyle.Colour($"★ {l.Name}", Colour(Rarity.Unique))).Append('\n').Append(UiStyle.Colour(l.Description, UiStyle.TextMain)).Append('\n');
            }
            var affixes = i.Affixes.ToList();
            if (affixes.Count > 0)
            {
                sb.Append('\n');
                foreach (var a in affixes) sb.Append(UiStyle.Colour(a.ToString(), new Color(0.62f, 0.75f, 1f))).Append(UiStyle.Colour($"  T{a.Tier}", UiStyle.TextFaint)).Append('\n');
            }
            if (i.Unique != null && !string.IsNullOrEmpty(i.Unique.Flavour)) sb.Append('\n').Append(UiStyle.Colour("<i>" + i.Unique.Flavour + "</i>", UiStyle.TextFaint)).Append('\n');
            sb.Append('\n').Append(UiStyle.Colour($"Durability {i.Condition}   ·   gear score {GearScore.Of(i)}   ·   sells for {CraftingRules.SellPrice(i):0}", UiStyle.TextDim));
            if (worn != null)
            {
                if (worn.IsWearing(i)) sb.Append('\n').Append(UiStyle.Colour("Worn", UiStyle.Gold));
                else
                {
                    var why = EquipPreview.Refusal(worn, i);
                    var off = EquipPreview.Displaced(worn, i);
                    int d = EquipPreview.GearScoreDelta(worn, i);
                    string delta = UiStyle.Colour((d >= 0 ? "+" : "") + d, d > 0 ? UiStyle.Good : d < 0 ? UiStyle.Bad : UiStyle.TextDim);
                    sb.Append('\n').Append(why != null ? UiStyle.Colour(why, UiStyle.Bad)
                        : $"If worn: gear score {delta}" + (off != null ? UiStyle.Colour($"  (replaces {off.Name})", UiStyle.TextDim) : UiStyle.Colour("  (empty slot)", UiStyle.TextDim)));
                }
            }
            if (!string.IsNullOrEmpty(footer)) sb.Append('\n').Append(footer);
            return sb.ToString();
        }

        /// <summary>"12 scrap, 1 shard, 2 essences" for a salvage preview.</summary>
        public static string Salvage(MaterialBag b)
        {
            if (b == null || (b.Scrap == 0 && b.PrimeShards == 0 && b.Essences.Count == 0)) return "nothing";
            int ess = b.Essences.Values.Sum();
            return $"{b.Scrap} scrap" + (b.PrimeShards > 0 ? $", {b.PrimeShards} shard{(b.PrimeShards == 1 ? "" : "s")}" : "") + (ess > 0 ? $", {ess} essence{(ess == 1 ? "" : "s")}" : "");
        }
    }

    /// <summary>Calls back when the pointer enters or leaves this element (or any child): the item tooltips.</summary>
    public sealed class ItemHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Action Enter, Exit;
        public void OnPointerEnter(PointerEventData e) => Enter?.Invoke();
        public void OnPointerExit(PointerEventData e) => Exit?.Invoke();
        private void OnDisable() => Exit?.Invoke();
    }
}
