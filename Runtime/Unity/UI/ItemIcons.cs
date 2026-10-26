using System;
using System.Collections.Generic;
using DungeonGen.Core.Items;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonGen.Unity.UI
{
    /// <summary>
    /// Item base name → painted icon. One icon per <see cref="ItemBase.All"/> base, per <see cref="Consumable.All"/> consumable, and
    /// for the purse (Silver, Scrap, Prime Shards, Essence). Sources are Meshy text-to-image paintings in
    /// <c>Assets/Generated/item-icons/&lt;slug&gt;.png</c> (<c>Tools/AssetGen/meshy_images.py run --set items</c>); the lookup asset
    /// <c>Assets/Generated/item-icons/Resources/ItemIcons.asset</c> is written by <c>Editor/ItemIconSetup</c> (also a greybox step),
    /// so player builds load it from Resources with no AssetDatabase. Uniques use their base's icon; rarity is the frame's job
    /// (<see cref="FrameColour"/>). A missing asset or name returns null and the tiles show a text fallback.
    /// </summary>
    [CreateAssetMenu(menuName = "Dungeon Gen/Item Icons", fileName = "ItemIcons")]
    public sealed class ItemIcons : ScriptableObject
    {
        public const string ResourceName = "ItemIcons";
        /// <summary>Names of the purse icons (not items).</summary>
        public const string Silver = "Silver", Scrap = "Scrap", PrimeShards = "Prime Shards", Essence = "Essence";

        [Serializable]
        public sealed class Entry
        {
            [Tooltip("File slug: the name lower-cased with spaces as underscores (plate_helm, health_potion).")] public string Slug;
            public Sprite Icon;
        }

        public List<Entry> Entries = new List<Entry>();

        private Dictionary<string, Sprite> _map;
        private static ItemIcons _loaded;
        private static bool _tried;

        /// <summary>"Plate Helm" → "plate_helm", as the generator names its files.</summary>
        public static string Slug(string name) => string.IsNullOrEmpty(name) ? "" : name.Trim().ToLowerInvariant().Replace(' ', '_').Replace("'", "");

        public Sprite Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_map == null)
            {
                _map = new Dictionary<string, Sprite>();
                foreach (var e in Entries) if (e != null && !string.IsNullOrEmpty(e.Slug) && e.Icon != null) _map[e.Slug] = e.Icon;
            }
            return _map.TryGetValue(Slug(name), out var s) ? s : null;
        }

        /// <summary>The shared lookup, loaded once from Resources (null when the setup step never ran).</summary>
        public static ItemIcons Loaded
        {
            get { if (!_tried) { _tried = true; _loaded = Resources.Load<ItemIcons>(ResourceName); } return _loaded; }
        }

        /// <summary>The icon of an item: its base's painting (uniques included), or null.</summary>
        public static Sprite For(Item item) => item == null ? null : For(item.Base?.Name);

        /// <summary>The icon for a base, consumable or purse name ("Longsword", "Health Potion", "Silver"), or null.</summary>
        public static Sprite For(string name) => Loaded != null ? Loaded.Find(name) : null;

        /// <summary>The rarity's frame colour: Normal grey, Magic blue, Rare yellow, Unique orange (the hues of <see cref="ItemText.Colour"/>).</summary>
        public static Color FrameColour(Rarity r) => r == Rarity.Normal ? NormalGrey : ItemText.Colour(r);
        public static readonly Color NormalGrey = new Color(0.56f, 0.55f, 0.53f, 1f);
    }

    /// <summary>
    /// The icon tile every item screen shares: a dark well, the painted icon, a rarity border with a soft inner glow, and an
    /// item-level badge. <see cref="Tile"/> makes a real <see cref="Button"/> (so the menu navigator and the gold focus outline
    /// work); <see cref="Icon"/> makes a plain picture for rows.
    /// </summary>
    public static class ItemIconTile
    {
        public static readonly Color Well = new Color(0.045f, 0.040f, 0.045f, 1f);
        public static readonly Color WellEmpty = new Color(0.035f, 0.031f, 0.036f, 0.92f);

        private static Sprite _innerGlow, _border, _badge;
        /// <summary>A rounded square that is opaque at the edge and fades to nothing inward: the rarity glow. 9-sliced.</summary>
        public static Sprite InnerGlow => _innerGlow != null ? _innerGlow : (_innerGlow = MakeEdge(64, 9, 20f, 1.6f, "item-inner-glow"));
        /// <summary>A rounded outline, 3 px at 1x. 9-sliced.</summary>
        public static Sprite Border => _border != null ? _border : (_border = MakeEdge(32, 7, 3f, 0f, "item-border"));
        /// <summary>A filled rounded rectangle with a small radius for badges.</summary>
        public static Sprite Badge => _badge != null ? _badge : (_badge = MakeEdge(16, 5, 99f, 0f, "item-badge"));

        /// <summary>
        /// A square item tile button. <paramref name="item"/> null draws an empty slot with <paramref name="emptyLabel"/> faint in the middle.
        /// The label child is named "Label" (for tests that read text) and holds the item name invisibly when filled, so
        /// <c>GetComponentInChildren&lt;Text&gt;</c> still finds it.
        /// </summary>
        public static Button Tile(RectTransform parent, string name, Item item, float size, Action onClick, string emptyLabel = null, bool showLevel = true)
        {
            var b = UiStyle.MakeButton(parent, name, "", onClick, 18);
            var rt = (RectTransform)b.transform;
            rt.sizeDelta = new Vector2(size, size);
            var bg = b.targetGraphic as Image;
            var cb = b.colors;
            cb.normalColor = item == null ? WellEmpty : Well; cb.highlightedColor = new Color(0.16f, 0.13f, 0.10f, 1f); cb.selectedColor = cb.highlightedColor;
            cb.pressedColor = UiStyle.ButtonPressed; cb.disabledColor = WellEmpty;
            b.colors = cb;
            var frame = rt.Find("Frame");
            var label = rt.Find("Label")?.GetComponent<Text>();
            Fill(rt, item, size, showLevel, emptyLabel, label);
            if (frame != null) { frame.SetAsLastSibling(); frame.GetComponent<Image>().color = item == null ? new Color(UiStyle.GoldDim.r, UiStyle.GoldDim.g, UiStyle.GoldDim.b, 0.55f) : new Color(0f, 0f, 0f, 0f); }
            var focus = rt.Find("Focus"); if (focus != null) focus.SetAsLastSibling();
            return b;
        }

        /// <summary>A non-interactive icon (for list rows): well, picture, rarity border and glow.</summary>
        public static RectTransform Icon(Transform parent, string name, Item item, float size, bool showLevel = false)
        {
            var well = UiStyle.Img(parent, name, Well, UiStyle.Rounded);
            well.rectTransform.sizeDelta = new Vector2(size, size);
            Fill(well.rectTransform, item, size, showLevel, null, null);
            return well.rectTransform;
        }

        /// <summary>A plain icon for a name (consumable, purse) with a neutral gold frame.</summary>
        public static RectTransform Named(Transform parent, string name, string iconName, float size, Color? frame = null)
        {
            var well = UiStyle.Img(parent, name, Well, UiStyle.Rounded);
            well.rectTransform.sizeDelta = new Vector2(size, size);
            var sprite = ItemIcons.For(iconName);
            if (sprite != null)
            {
                var pic = UiStyle.Img(well.transform, "Icon", Color.white, sprite);
                pic.rectTransform.Stretch(size * 0.05f, size * 0.05f, size * 0.05f, size * 0.05f);
                pic.preserveAspect = true;
            }
            else
            {
                var t = UiStyle.Label(well.transform, "Initial", iconName.Substring(0, 1), Mathf.Max(18, Mathf.RoundToInt(size * 0.45f)), UiStyle.GoldDim, TextAnchor.MiddleCenter, FontStyle.Bold);
                t.rectTransform.Stretch();
            }
            var c = frame ?? UiStyle.GoldDim;
            var b = UiStyle.Img(well.transform, "Border", c, Border); b.rectTransform.Stretch(); b.type = Image.Type.Sliced; b.pixelsPerUnitMultiplier = size < 60f ? 1.4f : 1f;
            return well.rectTransform;
        }

        private static void Fill(RectTransform tile, Item item, float size, bool showLevel, string emptyLabel, Text label)
        {
            float inset = Mathf.Round(size * 0.06f);
            if (item == null)
            {
                if (label != null)
                {
                    label.text = emptyLabel ?? "";
                    label.fontSize = 18; label.color = new Color(UiStyle.TextFaint.r, UiStyle.TextFaint.g, UiStyle.TextFaint.b, 0.9f);
                    label.fontStyle = FontStyle.Normal;
                    label.rectTransform.Stretch(4f, 4f, 4f, 4f);
                }
                var inner = UiStyle.Img(tile, "EmptyGlow", new Color(0f, 0f, 0f, 0.55f), InnerGlow);
                inner.type = Image.Type.Sliced; inner.rectTransform.Stretch();
                return;
            }
            var rc = ItemIcons.FrameColour(item.Rarity);
            // Rarity light pooled in the well, behind the picture.
            var pool = UiStyle.Img(tile, "RarityPool", new Color(rc.r, rc.g, rc.b, item.Rarity == Rarity.Normal ? 0.06f : 0.16f), UiStyle.Glow);
            pool.rectTransform.Stretch(-size * 0.1f, -size * 0.1f, -size * 0.1f, -size * 0.1f);
            var sprite = ItemIcons.For(item);
            if (sprite != null)
            {
                var pic = UiStyle.Img(tile, "Icon", Color.white, sprite);
                pic.rectTransform.Stretch(inset, inset, inset, inset);
                pic.preserveAspect = true;
            }
            else
            {
                var t = UiStyle.Label(tile, "Fallback", item.Base.Name, 18, rc, TextAnchor.MiddleCenter, FontStyle.Bold);
                t.rectTransform.Stretch(6f, 6f, 6f, 6f);
            }
            var glow = UiStyle.Img(tile, "RarityGlow", new Color(rc.r, rc.g, rc.b, item.Rarity == Rarity.Normal ? 0.18f : 0.50f), InnerGlow);
            glow.type = Image.Type.Sliced; glow.rectTransform.Stretch(); glow.pixelsPerUnitMultiplier = Mathf.Max(1f, 190f / size);   // the glow band stays about an eighth of the tile
            var border = UiStyle.Img(tile, "RarityBorder", rc, Border);
            border.type = Image.Type.Sliced; border.rectTransform.Stretch(); border.pixelsPerUnitMultiplier = size < 60f ? 1.4f : 1f;
            if (showLevel)
            {
                var badge = UiStyle.Img(tile, "LevelBadge", new Color(0.02f, 0.018f, 0.02f, 0.92f), Badge);
                badge.type = Image.Type.Sliced;
                badge.rectTransform.Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-4f, 4f), new Vector2(item.ItemLevel >= 10 ? 34f : 26f, 24f));
                var bb = UiStyle.Img(badge.transform, "Edge", new Color(rc.r, rc.g, rc.b, 0.7f), Border); bb.type = Image.Type.Sliced; bb.rectTransform.Stretch(); bb.pixelsPerUnitMultiplier = 2f;
                var lv = UiStyle.Label(badge.transform, "Level", item.ItemLevel.ToString(), 18, UiStyle.TextMain, TextAnchor.MiddleCenter, FontStyle.Bold, false);
                lv.rectTransform.Stretch(0f, 0f, 0f, 1f);
            }
            if (label != null)
            {
                // Keep the name readable to tests and screen readers without drawing it over the picture.
                label.text = item.Name; label.color = new Color(0f, 0f, 0f, 0f);
                var sh = label.GetComponent<Shadow>(); if (sh != null) sh.enabled = false;
            }
        }

        /// <summary>A rounded square band: alpha 1 at the edge, fading to 0 at <paramref name="band"/> px inside (power <paramref name="falloff"/>; 0 = hard band).</summary>
        private static Sprite MakeEdge(int size, int radius, float band, float falloff, string name)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = name };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Signed distance to a rounded square (negative inside), so the depth is the true distance from the edge.
                    float half = size / 2f, inner = half - radius;
                    float qx = Mathf.Abs(x + 0.5f - half) - inner, qy = Mathf.Abs(y + 0.5f - half) - inner;
                    float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                    float outside = Mathf.Clamp01(0.5f - d);
                    float depth = -d;   // px inside the edge
                    float a = falloff <= 0f ? Mathf.Clamp01(band + 0.5f - depth) : Mathf.Pow(Mathf.Clamp01(1f - depth / band), falloff);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a * outside) * 255f));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            float b = Mathf.Min(size / 2f - 1f, Mathf.Max(radius + 2, falloff > 0f ? band + 2 : radius + band + 1));
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }
    }
}
