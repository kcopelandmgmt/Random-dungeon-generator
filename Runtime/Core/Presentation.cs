using System.Collections.Generic;
using DungeonGen.Core.Combat;

namespace DungeonGen.Core
{
    /// <summary>The single launch theme, and the slot the later ones go in. A theme is a tileset id, ambient light and a mob bias; at launch there is one and the bias is empty.</summary>
    public sealed class DungeonTheme
    {
        public string Name { get; }
        public string TilesetId { get; }
        public (float r, float g, float b) AmbientLight { get; }
        public IReadOnlyDictionary<string, float> MobWeightBias { get; }

        public DungeonTheme(string name, string tileset, (float, float, float) ambient, IReadOnlyDictionary<string, float> bias)
        {
            Name = name; TilesetId = tileset; AmbientLight = ambient; MobWeightBias = bias;
        }

        public static readonly DungeonTheme Default = new DungeonTheme("Undercroft", "tiles/undercroft", (0.18f, 0.16f, 0.20f), new Dictionary<string, float>());
        public static readonly IReadOnlyList<DungeonTheme> All = new[] { Default };
    }

    /// <summary>
    /// Damage-type colours. Colour is the only differentiator by decision, so the colour-blind
    /// palette is not an afterthought: it is the second entry here and every effect reads its
    /// colour from whichever palette the player picked. Values are linear RGB 0..1.
    /// </summary>
    public static class ElementPalette
    {
        public static readonly IReadOnlyDictionary<DamageType, (float r, float g, float b)> Standard = new Dictionary<DamageType, (float, float, float)>
        {
            [DamageType.Physical] = (0.80f, 0.78f, 0.72f),
            [DamageType.Fire] = (1.00f, 0.42f, 0.10f),
            [DamageType.Frost] = (0.40f, 0.80f, 1.00f),
            [DamageType.Lightning] = (0.95f, 0.90f, 0.25f),
            [DamageType.Poison] = (0.35f, 0.85f, 0.25f),
            [DamageType.Shadow] = (0.60f, 0.25f, 0.85f),
        };

        /// <summary>Tuned for deuteranopia and protanopia (the common ~8%): separates on blue-yellow and luminance, keeps fire and poison far apart.</summary>
        public static readonly IReadOnlyDictionary<DamageType, (float r, float g, float b)> ColourBlind = new Dictionary<DamageType, (float, float, float)>
        {
            [DamageType.Physical] = (0.85f, 0.85f, 0.85f),
            [DamageType.Fire] = (0.90f, 0.45f, 0.00f),   // orange
            [DamageType.Frost] = (0.35f, 0.70f, 0.90f),  // sky blue
            [DamageType.Lightning] = (0.95f, 0.90f, 0.25f), // yellow
            [DamageType.Poison] = (0.00f, 0.45f, 0.70f),  // deep blue (never green)
            [DamageType.Shadow] = (0.80f, 0.60f, 0.70f),  // pink
        };

        public static IReadOnlyDictionary<DamageType, (float r, float g, float b)> Get(bool colourBlind) => colourBlind ? ColourBlind : Standard;
    }
}
