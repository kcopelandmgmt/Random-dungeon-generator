using UnityEngine;

namespace DungeonGen.Unity.UI
{
    /// <summary>
    /// Hooks the uGUI map and compass into <see cref="CompassHud"/> and <see cref="MiniMapHud"/>, which live in the base assembly and cannot see
    /// the UI package: this registers the factories they call in Awake, before any scene object wakes. Both views share one overlay canvas under the
    /// hud's object, ordered under the round HUD's own canvas so the pause menu and the summary cover them.
    /// </summary>
    internal static class MapHudInstaller
    {
        public const string CanvasName = "Map HUD Canvas";
        public const int SortingOrder = 9;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            MapHudViews.Compass = hud => MapCompassView.Create(hud, Canvas(hud.transform));
            MapHudViews.MiniMap = hud => MapMiniMapView.Create(hud, Canvas(hud.transform));
        }

        /// <summary>The overlay canvas the map and the compass draw on (made on first use).</summary>
        public static RectTransform Canvas(Transform host)
        {
            var existing = host.Find(CanvasName);
            if (existing != null) return (RectTransform)existing;
            var go = new GameObject(CanvasName);
            go.transform.SetParent(host, false);
            UiStyle.MakeCanvas(go, SortingOrder);
            var raycaster = go.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            if (raycaster != null) Object.Destroy(raycaster);   // nothing on the map takes clicks
            return (RectTransform)go.transform;
        }
    }
}
