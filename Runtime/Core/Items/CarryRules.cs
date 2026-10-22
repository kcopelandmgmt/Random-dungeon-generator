using System;

namespace DungeonGen.Core.Items
{
    /// <summary>
    /// Weight-only inventory: no slot grid. Everything worn and carried counts against the sheet's
    /// carry capacity (30 kg + 2 per Strength + affixes). Over the limit you still move, slower,
    /// so "I cannot carry all this" at a vault is a decision rather than a wall.
    /// </summary>
    public static class CarryRules
    {
        /// <summary>Move-speed multiplier for a load: 1 at or under capacity, down to 0.5 at double capacity.</summary>
        public static float MoveSpeedFactor(float loadKg, float capacityKg)
        {
            if (capacityKg <= 0f) return 0.5f;
            float over = Math.Max(0f, loadKg / capacityKg - 1f);
            return Math.Max(0.5f, 1f - over * 0.5f);
        }

        public static bool IsOverloaded(float loadKg, float capacityKg) => loadKg > capacityKg;
    }
}
