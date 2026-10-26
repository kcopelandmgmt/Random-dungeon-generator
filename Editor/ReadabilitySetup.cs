using UnityEngine;

namespace DungeonGen.Unity.Editor
{
    /// <summary>Greybox step: floor telegraphs for boss and mob wind-ups, and health bars over every visible combatant, on the Dungeon root.</summary>
    public static class ReadabilitySetup
    {
        [GreyboxStep(40)]
        public static void Step(GreyboxContext ctx)
        {
            var tele = ctx.Root.GetComponent<TelegraphView>();
            if (tele == null) tele = ctx.Root.AddComponent<TelegraphView>();
            tele.Effects = ctx.Effects; tele.World = ctx.World; tele.Builder = ctx.Builder;

            var bars = ctx.Root.GetComponent<WorldBars>();
            if (bars == null) bars = ctx.Root.AddComponent<WorldBars>();
            bars.World = ctx.World; bars.Effects = ctx.Effects; bars.Builder = ctx.Builder;
            bars.Camera = ctx.Camera != null && ctx.Camera.Camera != null ? ctx.Camera.Camera : Camera.main;
        }
    }
}
