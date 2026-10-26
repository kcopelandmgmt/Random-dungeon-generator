using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DungeonGen.Unity.UI
{
    /// <summary>
    /// The live character on the Stash &amp; Equipment screen: the selected character's class model in its idle loop, lit by a
    /// warm key, a cool fill and a gold rim, rendered by its own camera into a <see cref="RenderTexture"/> that a
    /// <see cref="RawImage"/> shows. The model comes from the same <see cref="AvatarVisualLibrary"/> entries the round uses
    /// (<see cref="AvatarVisualLibrary.Find"/> by class name): <c>Editor/ItemIconSetup</c> copies the hero entries into
    /// <c>Resources/PaperDollModels</c> (an <see cref="AvatarVisualLibrary"/>), because the menu scene holds no round library.
    /// The stage stands far below the world on layer <see cref="Layer"/>, seen only by its camera. It survives the screen being
    /// rebuilt (the next preview of the same class takes it over, so the idle does not restart) and destroys itself, with its
    /// texture, once no preview owns it: when the screen closes or the scene unloads.
    /// </summary>
    public sealed class PaperDollPreview : MonoBehaviour
    {
        public const int Layer = 30;
        public const string LibraryResource = "PaperDollModels";
        public static readonly Vector3 StageOrigin = new Vector3(0f, -6000f, 6000f);

        private static AvatarVisualLibrary _library;
        private static bool _triedLibrary;

        /// <summary>The live stage, if any (tests read it).</summary>
        public static PaperDollStage Current { get; internal set; }

        /// <summary>The hero models the menu may show, or null when the setup step never ran.</summary>
        public static AvatarVisualLibrary Library
        {
            get { if (!_triedLibrary) { _triedLibrary = true; _library = Resources.Load<AvatarVisualLibrary>(LibraryResource); } return _library; }
        }

        /// <summary>Show <paramref name="className"/> in <paramref name="target"/> (sized in reference pixels; the texture is rendered at twice that). False if there is no model.</summary>
        public static bool Show(RawImage target, string className, Vector2 size)
        {
            var entry = Library != null ? Library.Find(className) : null;
            if (entry == null || entry.Prefab == null) { target.enabled = false; return false; }
            int w = Mathf.Max(64, Mathf.RoundToInt(size.x * 2f)), h = Mathf.Max(64, Mathf.RoundToInt(size.y * 2f));
            var stage = Current;
            if (stage == null || stage.ClassName != className || stage.Texture == null || stage.Texture.width != w || stage.Texture.height != h)
            {
                if (stage != null) stage.Dispose();
                stage = PaperDollStage.Create(className, entry, w, h);
                Current = stage;
            }
            var owner = target.gameObject.AddComponent<PaperDollPreview>();
            stage.Owner = owner;
            target.texture = stage.Texture;
            target.color = Color.white;
            target.enabled = true;
            return true;
        }
    }

    /// <summary>The far-away stage of a <see cref="PaperDollPreview"/>: model, lights, camera, texture.</summary>
    public sealed class PaperDollStage : MonoBehaviour
    {
        public string ClassName { get; private set; }
        public RenderTexture Texture { get; private set; }
        public Camera Camera { get; private set; }
        public GameObject Model { get; private set; }
        internal PaperDollPreview Owner;
        private int _orphanFrames;

        internal static PaperDollStage Create(string className, AvatarVisualLibrary.Entry entry, int w, int h)
        {
            var root = new GameObject("PaperDollStage");
            root.transform.position = PaperDollPreview.StageOrigin;
            var stage = root.AddComponent<PaperDollStage>();
            stage.ClassName = className;
            stage.Texture = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "PaperDoll", antiAliasing = 4 };
            stage.Texture.Create();

            // The model, idle, three-quarter turned toward the camera.
            var model = Instantiate(entry.Prefab, root.transform, false);
            model.name = "Model-" + className;
            if (entry.Scale > 0f && !Mathf.Approximately(entry.Scale, 1f)) model.transform.localScale *= entry.Scale;
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, -28f, 0f);
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var mb in model.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;   // no round behaviours
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = PaperDollPreview.Layer;
            foreach (var a in model.GetComponentsInChildren<Animator>(true)) { a.applyRootMotion = false; a.updateMode = AnimatorUpdateMode.UnscaledTime; a.cullingMode = AnimatorCullingMode.AlwaysAnimate; }
            foreach (var r in model.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            stage.Model = model;

            // Frame it: the whole body with a little air, a touch above centre.
            var bounds = new Bounds(root.transform.position + Vector3.up, Vector3.one);
            bool any = false;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true)) { if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds); }
            float height = Mathf.Clamp(bounds.size.y, 1f, 6f);
            var focus = new Vector3(root.transform.position.x, root.transform.position.y + height * 0.35f, root.transform.position.z);   // skinned bounds run loose above the head: measured from the renders
            const float fov = 24f;
            float aspect = w / (float)h;
            float halfV = height * 0.44f, halfH = Mathf.Max(bounds.size.x, bounds.size.z) * 0.62f;
            float dist = Mathf.Max(halfV / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad), halfH / (Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * aspect));

            var camGo = new GameObject("PaperDollCamera");
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.position = focus + new Vector3(0f, height * 0.06f, dist);
            camGo.transform.LookAt(focus + Vector3.up * height * 0.02f);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.cullingMask = 1 << PaperDollPreview.Layer;
            cam.fieldOfView = fov; cam.nearClipPlane = 0.1f; cam.farClipPlane = dist + 10f;
            cam.depth = -50f; cam.allowHDR = false; cam.allowMSAA = true;
            cam.targetTexture = stage.Texture;
            stage.Camera = cam;

            // Lights: warm key from the front left, cool fill from the right, a strong gold rim from behind, a faint floor bounce.
            Light(root.transform, "Key", focus + new Vector3(-2.2f, height * 0.9f, 2.6f), new Color(1f, 0.86f, 0.68f), 22f, 10f);
            Light(root.transform, "Fill", focus + new Vector3(2.6f, height * 0.2f, 2.2f), new Color(0.55f, 0.65f, 0.9f), 7f, 10f);
            Light(root.transform, "Rim", focus + new Vector3(0.9f, height * 0.75f, -2.4f), new Color(1f, 0.72f, 0.35f), 20f, 7f);
            Light(root.transform, "RimLeft", focus + new Vector3(-1.6f, height * 0.35f, -2f), new Color(0.95f, 0.6f, 0.3f), 4f, 6f);
            return stage;
        }

        private static void Light(Transform parent, string name, Vector3 at, Color c, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            go.layer = PaperDollPreview.Layer;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
            l.cullingMask = 1 << PaperDollPreview.Layer;
        }

        private void LateUpdate()
        {
            // Gone with the screen: wait one frame (a rebuild hands the stage to the new preview in the same frame), then clean up.
            if (Owner != null) { _orphanFrames = 0; return; }
            if (++_orphanFrames >= 2) Dispose();
        }

        internal void Dispose()
        {
            if (PaperDollPreview.Current == this) PaperDollPreview.Current = null;
            if (Camera != null) Camera.targetTexture = null;
            if (Texture != null) { Texture.Release(); Destroy(Texture); Texture = null; }
            if (gameObject != null) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (Texture != null) { Texture.Release(); Destroy(Texture); Texture = null; }
        }
    }

    /// <summary>
    /// Puts the event system's selection (the gold focus outline the menu navigator draws) on the chosen item's tile for the
    /// first frames after the Stash &amp; Equipment screen is built, after the navigator has picked the screen's first button,
    /// so the chosen item and the focus are one tile. Then it stops and leaves navigation to the player.
    /// </summary>
    public sealed class PaperDollFocus : MonoBehaviour
    {
        public GameObject Target;
        private int _frames;

        private void LateUpdate()
        {
            if (Target == null || ++_frames > 3) { enabled = false; return; }
            var es = EventSystem.current;
            if (es != null && Target.activeInHierarchy && es.currentSelectedGameObject != Target) es.SetSelectedGameObject(Target);
        }
    }
}
