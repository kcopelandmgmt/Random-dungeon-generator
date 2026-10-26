using UnityEditor;
using UnityEngine;

namespace DungeonGen.Unity.Editor
{
    /// <summary>
    /// Greybox step 60: game feel. Puts <see cref="HitFeel"/> (camera shake, hit-stop), <see cref="LootBeams"/> (rarity beams, chest and corpse glows) and
    /// <see cref="ExtractBeacon"/> (beacon pillars, the extraction rings, the exit-opened cue) on the Dungeon root and wires them to the pieces the earlier steps
    /// made. The floating damage numbers need no step: <see cref="WorldBars"/> (step 40) draws them.
    /// </summary>
    public static class FeelSetup
    {
        [GreyboxStep(60)]
        public static void Step(GreyboxContext ctx)
        {
            var root = ctx.Root;
            var fog = root.GetComponent<FogOfWar>();
            var rig = ctx.Camera;

            var feel = root.GetComponent<HitFeel>();
            if (feel == null) feel = root.AddComponent<HitFeel>();   // not ??: Unity's fake-null components defeat it in the editor
            feel.World = ctx.World; feel.Rig = rig;

            var beams = root.GetComponent<LootBeams>();
            if (beams == null) beams = root.AddComponent<LootBeams>();
            beams.Builder = ctx.Builder; beams.World = ctx.World; beams.Bootstrap = ctx.Bootstrap; beams.Fog = fog;

            var beacon = root.GetComponent<ExtractBeacon>();
            if (beacon == null) beacon = root.AddComponent<ExtractBeacon>();
            beacon.Builder = ctx.Builder; beacon.World = ctx.World; beacon.Bootstrap = ctx.Bootstrap; beacon.Fog = fog;
            beacon.Audio = root.GetComponent<GameAudio>();
            beacon.Bars = root.GetComponent<WorldBars>();

            EditorUtility.SetDirty(feel); EditorUtility.SetDirty(beams); EditorUtility.SetDirty(beacon);
            EditorUtility.SetDirty(root);
        }
    }
}
