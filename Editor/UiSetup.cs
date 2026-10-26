using System;
using UnityEngine;

namespace DungeonGen.Unity.Editor
{
    /// <summary>
    /// Greybox step 30: the uGUI HUD canvas (with <c>GameHud</c>) and an EventSystem driven by the Input System UI module, under the
    /// Dungeon root so a rebuild replaces them. The UI assembly is reached by reflection (this assembly does not reference it),
    /// through <c>DungeonGen.Unity.UI.UiInstaller.InstallHud</c>.
    /// </summary>
    public static class UiSetup
    {
        public const string InstallerType = "DungeonGen.Unity.UI.UiInstaller, DungeonGen.Unity.UI";

        [GreyboxStep(30)]
        public static void Step(GreyboxContext ctx)
        {
            var t = Type.GetType(InstallerType);
            if (t == null) { Debug.LogWarning("Dungeon Gen: the UI assembly (DungeonGen.Unity.UI) is missing; no uGUI HUD added."); return; }
            var m = t.GetMethod("InstallHud");
            var hud = m.Invoke(null, new object[] { ctx.Root != null ? ctx.Root.transform : null, ctx.World, ctx.Bootstrap, ctx.Server }) as Component;
            Debug.Log("Dungeon Gen: uGUI HUD installed" + (hud != null ? " on " + hud.gameObject.name : "") + ".");
        }
    }
}
