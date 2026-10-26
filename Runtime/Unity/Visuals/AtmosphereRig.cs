using UnityEngine;
using UnityEngine.Rendering;

namespace DungeonGen.Unity
{
    /// <summary>
    /// The runtime half of the dungeon's look (the scene half, ambient, moon, camera clear, volume, is set by
    /// <c>Editor/AtmosphereSetup</c>). Three jobs:
    /// <list type="bullet">
    /// <item>A soft warm fill light that follows the local avatar, so the player always reads against the dark.</item>
    /// <item>Distance fog scaled to the top-down rig's zoom: the ground under the player is clear and the far edge of the view
    /// falls into darkness at any zoom. A camera without an enabled <see cref="TopDownCamera"/> (an overview or a line-up render)
    /// draws without fog.</item>
    /// <item>The flat ambient probe rebuilt from the scene's ambient colour on load, so a scene saved without baked lighting
    /// still gets its (very low) ambient.</item>
    /// </list>
    /// </summary>
    public sealed class AtmosphereRig : MonoBehaviour
    {
        public ClientWorldView World;

        [Header("Player fill light")]
        public bool FillLight = true;
        public Color FillColour = new Color(1f, 0.88f, 0.74f);
        public float FillIntensity = 9f, FillRange = 10f, FillHeight = 2.4f;

        [Header("Fog (multiples of the rig's camera distance)")]
        public bool Fog = true;
        [Tooltip("Fog starts this many camera distances away: 1 keeps the player's ground clear.")] public float FogStart = 1.35f;
        [Tooltip("Fog is total at this many camera distances: the top edge of the view is about 1.5.")] public float FogEnd = 2.3f;
        public Color FogColour = new Color(0.006f, 0.007f, 0.012f);

        public Light Fill { get; private set; }

        private void Awake()
        {
            if (World == null) World = FindFirstObjectByType<ClientWorldView>();
            if (RenderSettings.ambientMode == AmbientMode.Flat)
            {
                var sh = new SphericalHarmonicsL2();
                sh.AddAmbientLight(RenderSettings.ambientLight);
                RenderSettings.ambientProbe = sh;
            }
            if (FillLight)
            {
                var go = new GameObject("PlayerFillLight");
                go.transform.SetParent(transform, false);
                Fill = go.AddComponent<Light>();
                Fill.type = LightType.Point; Fill.color = FillColour; Fill.intensity = FillIntensity; Fill.range = FillRange;
                Fill.shadows = LightShadows.None; Fill.renderMode = LightRenderMode.ForcePixel;
                go.SetActive(false);
            }
        }

        private void OnEnable() { RenderPipelineManager.beginCameraRendering += OnBeginCamera; }
        private void OnDisable() { RenderPipelineManager.beginCameraRendering -= OnBeginCamera; }

        private void LateUpdate()
        {
            if (Fill == null) return;
            var avatar = World != null ? World.LocalAvatar : null;
            Fill.gameObject.SetActive(avatar != null);
            if (avatar != null) Fill.transform.position = avatar.transform.position + Vector3.up * FillHeight;
        }

        private void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == null || cam.cameraType != CameraType.Game) return;
            var rig = cam.GetComponent<TopDownCamera>();
            bool on = Fog && rig != null && rig.isActiveAndEnabled;
            RenderSettings.fog = on;
            if (!on) return;
            float d = Mathf.Max(4f, rig.CurrentDistance > 0f ? rig.CurrentDistance : rig.Distance);
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = FogColour;
            RenderSettings.fogStartDistance = d * FogStart;
            RenderSettings.fogEndDistance = d * FogEnd;
        }
    }
}
