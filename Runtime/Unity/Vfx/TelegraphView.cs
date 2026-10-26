using System.Collections.Generic;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// Draws boss and mob wind-ups on the floor. Each <see cref="EffectKind.Telegraph"/> (keyed by Source + Ability) becomes a flat danger
    /// disc: a dim outline ring at the full radius and an inner fill that grows from nothing to the full radius as the sim clock approaches
    /// <c>Until</c>, pulsing in the last half second. A wind-up aimed at a player (Target at least 0) follows that player's avatar. A
    /// telegraph goes away when the server stops sending it for <see cref="LingerSeconds"/> or its time has passed. Colour by ability:
    /// crush red, groundslam orange, dirge purple with a bright double outline (interrupt it), overdrive yellow.
    /// Meshes are built here (a ring and a fan) and drawn with URP Unlit set to transparent; nothing is decided here.
    /// </summary>
    public sealed class TelegraphView : MonoBehaviour
    {
        public AbilityEffects Effects;
        public ClientWorldView World;
        public DungeonBuilder Builder;
        [Tooltip("Seconds the server may stay silent before a telegraph is dropped.")]
        public float LingerSeconds = 0.3f;
        public float PulseSeconds = 0.5f;

        /// <summary>One live telegraph, for tests and tools.</summary>
        public sealed class Live
        {
            public string Key, Ability; public int Source, Target;
            public GameObject Root; public Transform Ring, Fill;
            public Material RingMat, FillMat;
            public Vector3 Centre; public float Radius, StartSim, Until, LastSeen;
            public float Fraction;          // fill, 0..1, as last drawn
            public Color Colour;
        }

        private readonly Dictionary<string, Live> _live = new Dictionary<string, Live>();
        private readonly List<string> _drop = new List<string>();
        private AbilityEffects _subscribed;
        private ClientWorldView _clockWorld;
        private uint _clockTick = uint.MaxValue;
        private float _clockSim, _clockReal;
        private Transform _holder;
        private static Mesh _ringThin, _ringBold, _fan;

        public IReadOnlyDictionary<string, Live> Active => _live;
        /// <summary>First live telegraph for a source, or null.</summary>
        public Live For(int source) { foreach (var l in _live.Values) if (l.Source == source) return l; return null; }

        /// <summary>The sim time now, estimated from the last snapshot's clock plus real time since it arrived.</summary>
        public float SimNow()
        {
            SyncClock();
            return _clockSim + (Time.time - _clockReal);
        }

        private void SyncClock()
        {
            var snap = World != null ? World.Latest : null;
            if (snap == null) return;
            if (snap.Tick != _clockTick || World != _clockWorld) { _clockTick = snap.Tick; _clockWorld = World; _clockSim = snap.Time; _clockReal = Time.time; }
        }

        private void OnDisable()
        {
            if (_subscribed != null) _subscribed.EffectArrived -= OnEffect;
            _subscribed = null;
            foreach (var l in _live.Values) Kill(l);
            _live.Clear();
        }

        private void Update()
        {
            if (Effects != _subscribed)
            {
                if (_subscribed != null) _subscribed.EffectArrived -= OnEffect;
                _subscribed = Effects;
                if (_subscribed != null) _subscribed.EffectArrived += OnEffect;
            }
            SyncClock();
            float now = _clockSim + (Time.time - _clockReal);
            _drop.Clear();
            foreach (var kv in _live)
            {
                var l = kv.Value;
                if (l.Root == null || Time.time - l.LastSeen > LingerSeconds || now >= l.Until + 0.05f) { _drop.Add(kv.Key); continue; }
                Draw(l, now);
            }
            foreach (var k in _drop) { Kill(_live[k]); _live.Remove(k); }
        }

        private void OnEffect(EffectSnapshot e)
        {
            if (e.Kind != EffectKind.Telegraph || Builder == null) return;
            SyncClock();
            string key = e.Source + "/" + e.Ability;
            var centre = Builder.SimToWorld(new Vec2(e.X2, e.Y2));
            if (!_live.TryGetValue(key, out var l) || l.Root == null)
            {
                l = Create(key, e);
                _live[key] = l;
                l.StartSim = Mathf.Min(_clockSim, e.Until);
            }
            l.Centre = centre;
            l.Radius = Mathf.Max(0.1f, e.Amount * Builder.CellSize);
            l.Until = e.Until; l.Target = e.Target;
            l.LastSeen = Time.time;
        }

        private Live Create(string key, EffectSnapshot e)
        {
            if (_holder == null) { _holder = new GameObject("Telegraphs").transform; _holder.SetParent(transform, false); }
            var (col, bold) = Style(e.Ability);
            var l = new Live { Key = key, Ability = e.Ability, Source = e.Source, Target = e.Target, Colour = col, LastSeen = Time.time };
            l.Root = new GameObject("Telegraph_" + key);
            l.Root.transform.SetParent(_holder, false);
            l.RingMat = MakeMaterial(new Color(col.r, col.g, col.b, bold ? 0.9f : 0.55f));
            l.FillMat = MakeMaterial(new Color(col.r, col.g, col.b, 0.35f));
            EnsureMeshes();
            var ring = new GameObject("Outline", typeof(MeshFilter), typeof(MeshRenderer));
            ring.transform.SetParent(l.Root.transform, false);
            ring.GetComponent<MeshFilter>().sharedMesh = bold ? _ringBold : _ringThin;
            ring.GetComponent<MeshRenderer>().sharedMaterial = l.RingMat;
            var fill = new GameObject("Fill", typeof(MeshFilter), typeof(MeshRenderer));
            fill.transform.SetParent(l.Root.transform, false);
            fill.GetComponent<MeshFilter>().sharedMesh = _fan;
            fill.GetComponent<MeshRenderer>().sharedMaterial = l.FillMat;
            foreach (var r in l.Root.GetComponentsInChildren<MeshRenderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            l.Ring = ring.transform; l.Fill = fill.transform;
            return l;
        }

        private static (Color colour, bool bold) Style(string ability)
        {
            switch (ability)
            {
                case "crush": return (new Color(1f, 0.15f, 0.1f), false);
                case "groundslam": return (new Color(1f, 0.5f, 0.08f), false);
                case "dirge": return (new Color(0.65f, 0.25f, 1f), true);       // interrupt it: thick bright outline
                case "overdrive": return (new Color(1f, 0.9f, 0.15f), false);
                default: return (new Color(1f, 0.25f, 0.25f), false);
            }
        }

        private void Draw(Live l, float now)
        {
            Vector3 c = l.Centre;
            if (l.Target >= 0 && World != null && World.Avatars.TryGetValue(l.Target, out var av) && av != null)
                c = new Vector3(av.transform.position.x, l.Centre.y, av.transform.position.z);     // follow the marked player
            c.y = 0.04f;
            l.Root.transform.position = c;
            float span = Mathf.Max(0.05f, l.Until - l.StartSim);
            float f = Mathf.Clamp01((now - l.StartSim) / span);
            l.Fraction = f;
            float remaining = l.Until - now;
            float pulse = remaining < PulseSeconds ? 1f + 0.06f * Mathf.Sin(Time.time * 36f) : 1f;
            float flash = remaining < PulseSeconds ? 0.5f + 0.5f * Mathf.Sin(Time.time * 36f) : 0f;
            l.Ring.localScale = new Vector3(l.Radius * pulse, 1f, l.Radius * pulse);
            float fr = Mathf.Max(0.001f, l.Radius * f * pulse);
            l.Fill.localScale = new Vector3(fr, 1f, fr);
            l.Fill.localPosition = new Vector3(0f, 0.01f, 0f);
            bool bold = l.Ability == "dirge";
            SetAlpha(l.FillMat, l.Colour, 0.30f + 0.25f * f + 0.25f * flash);
            SetAlpha(l.RingMat, l.Colour, (bold ? 0.85f : 0.5f) + 0.3f * flash);
        }

        private static void SetAlpha(Material m, Color c, float a) { c.a = Mathf.Clamp01(a); m.SetColor("_BaseColor", c); m.color = c; }

        private static void Kill(Live l)
        {
            if (l == null) return;
            if (l.Root != null) Destroy(l.Root);
            if (l.RingMat != null) Destroy(l.RingMat);
            if (l.FillMat != null) Destroy(l.FillMat);
        }

        private static Shader _shader;
        private static Material MakeMaterial(Color c)
        {
            if (_shader == null) _shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            var m = new Material(_shader) { name = "TelegraphMat" };
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_Surface", 1f);                                  // transparent
            m.SetFloat("_Blend", 0f);                                    // alpha
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetColor("_BaseColor", c); m.color = c;
            return m;
        }

        private static void EnsureMeshes()
        {
            if (_ringThin == null) _ringThin = BuildRing(0.90f, 1f, 64);
            if (_ringBold == null) _ringBold = BuildRing(0.78f, 1f, 64);
            if (_fan == null) _fan = BuildFan(64);
        }

        /// <summary>A flat annulus of outer radius 1 in the XZ plane, facing up.</summary>
        public static Mesh BuildRing(float inner, float outer, int segments)
        {
            var v = new Vector3[segments * 2]; var n = new Vector3[segments * 2]; var tri = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v[i * 2] = dir * inner; v[i * 2 + 1] = dir * outer; n[i * 2] = n[i * 2 + 1] = Vector3.up;
                int i0 = i * 2, i1 = i * 2 + 1, j0 = ((i + 1) % segments) * 2, j1 = j0 + 1, t = i * 6;
                tri[t] = i0; tri[t + 1] = j0; tri[t + 2] = i1;
                tri[t + 3] = i1; tri[t + 4] = j0; tri[t + 5] = j1;
            }
            var m = new Mesh { name = "TelegraphRing", vertices = v, normals = n, triangles = tri };
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A flat disc of radius 1 in the XZ plane, facing up.</summary>
        public static Mesh BuildFan(int segments)
        {
            var v = new Vector3[segments + 1]; var n = new Vector3[segments + 1]; var tri = new int[segments * 3];
            v[0] = Vector3.zero; n[0] = Vector3.up;
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                v[i + 1] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)); n[i + 1] = Vector3.up;
                tri[i * 3] = 0; tri[i * 3 + 1] = (i + 1) % segments + 1; tri[i * 3 + 2] = i + 1;
            }
            var m = new Mesh { name = "TelegraphFan", vertices = v, normals = n, triangles = tri };
            m.RecalculateBounds();
            return m;
        }
    }
}
