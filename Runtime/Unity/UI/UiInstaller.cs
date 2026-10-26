using UnityEngine;
using UnityEngine.EventSystems;

namespace DungeonGen.Unity.UI
{
    /// <summary>
    /// Entry points for the editor (called by reflection from <c>Editor/UiSetup.cs</c> and <c>Editor/MenuSceneBuilder.cs</c>,
    /// whose assembly does not reference this one): put the HUD canvas and an EventSystem into a scene, or the main menu.
    /// The widgets themselves are built at runtime from code, so the scene holds only the canvas and its component.
    /// </summary>
    public static class UiInstaller
    {
        public const string HudName = "HUD Canvas", MenuName = "Menu Canvas";

        public static GameHud InstallHud(Transform parent, ClientWorldView world, DungeonMatchBootstrap bootstrap, ServerSimulation server)
        {
            foreach (var old in Object.FindObjectsByType<GameHud>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.DestroyImmediate(old.gameObject);
            var go = new GameObject(HudName);
            if (parent != null) go.transform.SetParent(parent, false);
            UiStyle.MakeCanvas(go, 10);
            var hud = go.AddComponent<GameHud>();
            hud.World = world; hud.Bootstrap = bootstrap; hud.Server = server;
            ItemPanels.Install(go, world, bootstrap, server);   // loot, inventory (I), group rolls, item tooltips
            ShrinePanel.Install(go, world, bootstrap, server);   // arm the ultimate, resurrect, buy, while standing at a shrine
            var es = UiStyle.EnsureEventSystem();
            if (parent != null && es.transform.parent == null && es.gameObject.scene == go.scene) es.transform.SetParent(parent, false);
            return hud;
        }

        public static MainMenuUi InstallMenu()
        {
            foreach (var old in Object.FindObjectsByType<MainMenuUi>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.DestroyImmediate(old.gameObject);
            var go = new GameObject(MenuName);
            UiStyle.MakeCanvas(go, 0);
            var menu = go.AddComponent<MainMenuUi>();
            UiStyle.EnsureEventSystem();
            return menu;
        }

        public static EventSystem EnsureEventSystem() => UiStyle.EnsureEventSystem();
    }
}
