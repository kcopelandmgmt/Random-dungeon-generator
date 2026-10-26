using System.Collections.Generic;
using DungeonGen.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace DungeonGen.Unity
{
    /// <summary>
    /// Full-height walls that never hide a character (the owner's call of 2026-10-26). Every frame, after the camera has moved, the
    /// camera-to-target segments for each avatar the client shows (the local player, allies, enemies, mobs, bosses) and for the
    /// local player's aim point are walked through the layout grid (an exact 2D DDA over the cells with the ray's height checked
    /// against <see cref="WallHeight"/>; no physics, no colliders). Each avatar is sampled with a small cone of five rays (feet,
    /// waist, head, and the waist a little to either side on screen), so a wall that hides any part of the body counts.
    /// <para>A blocking wall or pillar cell fades to <see cref="FadedAlpha"/> over <see cref="FadeSeconds"/> and back when it stops
    /// blocking. A fading renderer swaps to a transparent twin of the material it shows (the twins are assets made by
    /// <c>Editor/AtmosphereSetup</c>, so their shader variants are in the player; anything else gets a runtime copy) and gets a
    /// <see cref="MaterialPropertyBlock"/> with the alpha in <c>_BaseColor</c>, per sub-mesh. The colour comes from the material
    /// the fog of war chose (normal or dimmed), and <see cref="FogOfWar.CellApplied"/> makes the fade re-apply on top whenever the
    /// fog swaps a cell's materials, so dimming keeps working through a fade. Only fading renderers leave the SRP batcher.</para>
    /// </summary>
    [DefaultExecutionOrder(200)]   // after TopDownCamera's LateUpdate has placed the camera
    public sealed class WallFade : MonoBehaviour
    {
        public DungeonBuilder Builder;
        public ClientWorldView World;
        public TopDownCamera Rig;
        public FogOfWar Fog;

        [Tooltip("Height of the wall blocks in metres (Editor/AtmosphereSetup.WallHeight). A ray above this over a wall cell passes.")]
        public float WallHeight = 3f;
        [Tooltip("Alpha a blocking wall fades to.")]
        [Range(0f, 1f)] public float FadedAlpha = 0.2f;
        [Tooltip("Seconds for a full fade out or back in.")]
        public float FadeSeconds = 0.15f;
        [Tooltip("Heights sampled on each avatar (feet, waist, head), metres above the floor.")]
        public float FeetHeight = 0.25f, WaistHeight = 1f, HeadHeight = 1.8f;
        [Tooltip("Sideways reach of the two extra waist rays, metres along the camera's right.")]
        public float BodyHalfWidth = 0.45f;
        [Tooltip("Off: walls stay solid (debug).")] public bool Enabled = true;

        [Tooltip("Opaque wall materials and their transparent twins, pairwise (set by AtmosphereSetup). Other materials get a runtime copy.")]
        public Material[] FadeFrom = new Material[0], FadeTo = new Material[0];

        /// <summary>Cells blocking a target this frame.</summary>
        public IReadOnlyCollection<GridPoint> Blocking => _blocking;
        /// <summary>Rays cast and renderers fading in the last frame, and its cost in milliseconds (for tests and the report).</summary>
        public int RaysLastFrame { get; private set; }
        public int FadingCount => _active.Count;
        public float LastMilliseconds { get; private set; }

        private sealed class Faded
        {
            public Renderer Renderer; public GridPoint Cell;
            public float Alpha = 1f; public bool Want;
            public Material[] Base;     // what the fog of war (or the build) put on the renderer
            public Material[] Fade;     // their transparent twins
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private DungeonLayout _layout;
        private readonly Dictionary<GridPoint, Renderer[]> _cellRenderers = new Dictionary<GridPoint, Renderer[]>();
        private readonly Dictionary<Renderer, Faded> _active = new Dictionary<Renderer, Faded>();
        private readonly HashSet<GridPoint> _blocking = new HashSet<GridPoint>();
        private readonly Dictionary<Material, Material> _twins = new Dictionary<Material, Material>();
        private readonly HashSet<Material> _fadeMats = new HashSet<Material>();
        private readonly List<Material> _runtimeTwins = new List<Material>();
        private readonly List<Renderer> _done = new List<Renderer>();
        private readonly List<Material> _matsScratch = new List<Material>();
        private MaterialPropertyBlock _mpb;
        private readonly System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();

        private void Awake()
        {
            if (Builder == null) Builder = GetComponent<DungeonBuilder>() ?? FindFirstObjectByType<DungeonBuilder>();
            if (World == null) World = GetComponent<ClientWorldView>() ?? FindFirstObjectByType<ClientWorldView>();
            if (Rig == null) Rig = FindFirstObjectByType<TopDownCamera>();
            if (Fog == null) Fog = GetComponent<FogOfWar>() ?? FindFirstObjectByType<FogOfWar>();
            _mpb = new MaterialPropertyBlock();
            RegisterTwins();
        }

        private void OnEnable() { if (Fog != null) Fog.CellApplied += OnFogApplied; }

        private void OnDisable()
        {
            if (Fog != null) Fog.CellApplied -= OnFogApplied;
            RestoreAll();
        }

        private void OnDestroy()
        {
            foreach (var m in _runtimeTwins) if (m != null) Destroy(m);
            _runtimeTwins.Clear();
        }

        private void RegisterTwins()
        {
            int n = Mathf.Min(FadeFrom?.Length ?? 0, FadeTo?.Length ?? 0);
            for (int i = 0; i < n; i++)
                if (FadeFrom[i] != null && FadeTo[i] != null) { _twins[FadeFrom[i]] = FadeTo[i]; _fadeMats.Add(FadeTo[i]); }
        }

        /// <summary>The alpha a renderer is drawn at now (1 when it is not fading).</summary>
        public float AlphaOf(Renderer r) => r != null && _active.TryGetValue(r, out var f) ? f.Alpha : 1f;

        /// <summary>Wall and pillar renderers at a cell.</summary>
        public Renderer[] RenderersAt(GridPoint cell) => Renderers(cell);

        // ------------------------------------------------------------ the frame

        private void LateUpdate()
        {
            _sw.Restart();
            var layout = Builder != null ? Builder.Layout : null;
            if (!ReferenceEquals(layout, _layout)) { RestoreAll(); _cellRenderers.Clear(); _layout = layout; }
            _blocking.Clear();
            RaysLastFrame = 0;
            var cam = Rig != null ? Rig.Camera : null;
            if (Enabled && _layout != null && cam != null && World != null)
            {
                var eye = cam.transform.position;
                var right = cam.transform.right; right.y = 0f; right = right.sqrMagnitude > 1e-6f ? right.normalized : Vector3.right;
                foreach (var kv in World.Avatars)
                {
                    var go = kv.Value;
                    if (go == null || !go.activeInHierarchy) continue;
                    var p = go.transform.position; p.y = 0f;   // floors are at y 0; a capsule is lifted by its half height
                    Trace(eye, p + Vector3.up * FeetHeight);
                    Trace(eye, p + Vector3.up * WaistHeight);
                    Trace(eye, p + Vector3.up * HeadHeight);
                    Trace(eye, p + Vector3.up * WaistHeight + right * BodyHalfWidth);
                    Trace(eye, p + Vector3.up * WaistHeight - right * BodyHalfWidth);
                }
                if (World.LocalAvatar != null && Rig.ScreenToGround(Rig.AimScreenPosition, out var aim)) Trace(eye, aim + Vector3.up * FeetHeight);
            }

            foreach (var cell in _blocking)
                foreach (var r in Renderers(cell))
                {
                    if (r == null) continue;
                    if (!_active.TryGetValue(r, out var f)) _active[r] = f = new Faded { Renderer = r, Cell = cell };
                    f.Want = true;
                }

            float step = FadeSeconds <= 0f ? 1f : Time.unscaledDeltaTime * (1f - FadedAlpha) / FadeSeconds;
            _done.Clear();
            foreach (var f in _active.Values)
            {
                if (f.Renderer == null) { _done.Add(f.Renderer); continue; }
                f.Alpha = Mathf.MoveTowards(f.Alpha, f.Want ? FadedAlpha : 1f, step);
                f.Want = false;
                if (f.Alpha >= 0.999f) { Restore(f); _done.Add(f.Renderer); continue; }
                Show(f);
            }
            foreach (var r in _done) _active.Remove(r);
            _sw.Stop();
            LastMilliseconds = (float)_sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>Walk the segment eye → target over the grid (cell centres at GridToWorld, CellSize wide) and mark every wall or pillar
        /// cell where the segment passes below the wall top. The target's own cell never counts.</summary>
        private void Trace(Vector3 eye, Vector3 target)
        {
            RaysLastFrame++;
            float cs = Builder.CellSize;
            // grid space: u = world / cellSize + 0.5, so floor(u) is the cell index
            float ux = eye.x / cs + 0.5f, uz = eye.z / cs + 0.5f;
            float vx = target.x / cs + 0.5f, vz = target.z / cs + 0.5f;
            float dx = vx - ux, dz = vz - uz;
            int cx = Mathf.FloorToInt(ux), cz = Mathf.FloorToInt(uz);
            int tx = Mathf.FloorToInt(vx), tz = Mathf.FloorToInt(vz);
            int sx = dx > 0 ? 1 : -1, sz = dz > 0 ? 1 : -1;
            float tdx = Mathf.Abs(dx) < 1e-6f ? float.PositiveInfinity : Mathf.Abs(1f / dx);
            float tdz = Mathf.Abs(dz) < 1e-6f ? float.PositiveInfinity : Mathf.Abs(1f / dz);
            float tmx = float.IsPositiveInfinity(tdx) ? float.PositiveInfinity : (dx > 0 ? (cx + 1 - ux) : (ux - cx)) * tdx;
            float tmz = float.IsPositiveInfinity(tdz) ? float.PositiveInfinity : (dz > 0 ? (cz + 1 - uz) : (uz - cz)) * tdz;
            float tEnter = 0f;
            int guard = 0;
            while (guard++ < 512)
            {
                float tExit = Mathf.Min(1f, Mathf.Min(tmx, tmz));
                if (cx == tx && cz == tz) return;
                // The segment is lowest where it leaves the cell when the eye is above the target (always, for this camera).
                float yLow = Mathf.Min(Mathf.Lerp(eye.y, target.y, tEnter), Mathf.Lerp(eye.y, target.y, tExit));
                if (yLow < WallHeight && cx >= 0 && cz >= 0 && cx < _layout.Width && cz < _layout.Height)
                {
                    var t = _layout.Tiles[cx, cz];
                    if (t == TileType.Wall || t == TileType.Pillar) _blocking.Add(new GridPoint(cx, cz));
                }
                if (tExit >= 1f) return;
                tEnter = tExit;
                if (tmx < tmz) { cx += sx; tmx += tdx; } else { cz += sz; tmz += tdz; }
            }
        }

        private Renderer[] Renderers(GridPoint cell)
        {
            if (_cellRenderers.TryGetValue(cell, out var rs)) return rs;
            var list = new List<Renderer>();
            if (_layout != null && _layout.InBounds(cell) && (_layout.Tiles[cell.X, cell.Y] == TileType.Wall || _layout.Tiles[cell.X, cell.Y] == TileType.Pillar)
                && Builder.CellObjects.TryGetValue(cell, out var gos))
                foreach (var go in gos)
                    if (go != null) list.AddRange(go.GetComponentsInChildren<Renderer>(true));
            rs = list.ToArray();
            _cellRenderers[cell] = rs;
            return rs;
        }

        // ------------------------------------------------------------ materials

        private void Show(Faded f)
        {
            var r = f.Renderer;
            r.GetSharedMaterials(_matsScratch);
            bool ours = _matsScratch.Count > 0 && _matsScratch[0] != null && _fadeMats.Contains(_matsScratch[0]);
            if (!ours || f.Fade == null) Swap(f);
            for (int i = 0; i < f.Base.Length; i++)
            {
                var src = f.Base[i];
                if (src == null) continue;
                var c = src.HasProperty(BaseColorId) ? src.GetColor(BaseColorId) : src.HasProperty(ColorId) ? src.GetColor(ColorId) : Color.white;
                c.a *= f.Alpha;
                _mpb.Clear();
                _mpb.SetColor(BaseColorId, c);
                _mpb.SetColor(ColorId, c);
                r.SetPropertyBlock(_mpb, i);
            }
        }

        /// <summary>Take what the renderer shows now as the base and put the transparent twins on.</summary>
        private void Swap(Faded f)
        {
            var r = f.Renderer;
            f.Base = r.sharedMaterials;
            f.Fade = new Material[f.Base.Length];
            for (int i = 0; i < f.Base.Length; i++) f.Fade[i] = TwinOf(f.Base[i]);
            r.sharedMaterials = f.Fade;
        }

        private void Restore(Faded f)
        {
            var r = f.Renderer;
            if (r == null) return;
            if (f.Base != null)
            {
                r.GetSharedMaterials(_matsScratch);
                if (_matsScratch.Count > 0 && _matsScratch[0] != null && _fadeMats.Contains(_matsScratch[0])) r.sharedMaterials = f.Base;
                for (int i = 0; i < f.Base.Length; i++) r.SetPropertyBlock(null, i);
            }
            f.Base = null; f.Fade = null;
        }

        private void RestoreAll()
        {
            foreach (var f in _active.Values) { f.Alpha = 1f; Restore(f); }
            _active.Clear();
        }

        /// <summary>The fog of war just set this cell's materials: take its choice as the new base and fade on top of it.</summary>
        private void OnFogApplied(GridPoint cell)
        {
            if (!_cellRenderers.TryGetValue(cell, out var rs)) return;
            foreach (var r in rs)
                if (r != null && _active.TryGetValue(r, out var f) && f.Base != null) { Swap(f); Show(f); }
        }

        private Material TwinOf(Material m)
        {
            if (m == null) return null;
            if (_fadeMats.Contains(m)) return m;
            if (_twins.TryGetValue(m, out var t)) return t;
            // A fog-dimmed copy shares its source's keywords: use the source's twin (the colour comes from the property block).
            var src = Fog != null ? Fog.NormalOf(m) : m;
            if (src != m && _twins.TryGetValue(src, out t)) { _twins[m] = t; return t; }
            t = MakeTransparent(new Material(src) { name = src.name + " (fade)" });
            _runtimeTwins.Add(t);
            _twins[m] = t; _fadeMats.Add(t);
            return t;
        }

        /// <summary>URP Lit (or Simple Lit) set to Transparent / Alpha blending, no depth write. Used by AtmosphereSetup for the twin assets too.</summary>
        public static Material MakeTransparent(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_AlphaClip", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }
    }
}
