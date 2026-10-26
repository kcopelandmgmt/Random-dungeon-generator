using System.Collections.Generic;
using DungeonGen.Core;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using UnityEngine;
using UnityEngine.Rendering;

namespace DungeonGen.Unity
{
    /// <summary>What the local team knows of a cell.</summary>
    public enum FogState : byte { Hidden = 0, Explored = 1, Visible = 2 }

    /// <summary>
    /// The map's fog of war, drawn per cell on the objects <see cref="DungeonBuilder.CellObjects"/> lists (tiles, interiors,
    /// hazards, markers, lights). A cell is
    /// <list type="bullet">
    /// <item><b>Visible</b> when a standing member of the local team has line of sight to it (<see cref="LineOfSight.VisibleCells"/>
    /// with the descriptor's sight range and the doors shut this round): drawn as built.</item>
    /// <item><b>Explored</b> when the team has explored its room (the mirrored <see cref="MatchState.Explored"/>; the room's ring of
    /// walls counts) or someone on the team has seen it this round: drawn with dimmed, desaturated, cooler copies of its materials.</item>
    /// <item><b>Hidden</b> otherwise: renderers and lights off.</item>
    /// </list>
    /// Recomputed <see cref="UpdatesPerSecond"/> times a second; only cells whose state changed are touched, and dimming swaps
    /// shared materials (one dim copy per material), so the SRP batcher keeps batching. Enemies are filtered by the server; this
    /// is the map's fog. A camera rendering without an enabled <see cref="TopDownCamera"/> (an overview shot) sees the whole map.
    /// </summary>
    public sealed class FogOfWar : MonoBehaviour
    {
        public DungeonBuilder Builder;
        public ClientWorldView World;
        public DungeonMatchBootstrap Bootstrap;
        [Tooltip("Line-of-sight recomputations per second.")] public float UpdatesPerSecond = 6f;
        [Tooltip("Explored-but-unseen cells: brightness kept and saturation kept (0 grey, 1 full colour).")]
        [Range(0f, 1f)] public float DimBrightness = 0.5f, DimSaturation = 0.3f;
        public Color DimTint = new Color(0.82f, 0.9f, 1.1f);
        [Tooltip("Off: every cell is drawn as built (debug).")] public bool Enabled = true;

        /// <summary>Recomputations so far (for tests).</summary>
        public int Updates { get; private set; }
        public int VisibleCount { get; private set; }
        public int ExploredCount { get; private set; }

        /// <summary>Raised after the fog has set a cell's renderers (materials, enabled). <see cref="WallFade"/> re-applies its fade on top.</summary>
        public event System.Action<GridPoint> CellApplied;

        /// <summary>The material a dimmed copy was made from; the material itself when it is not one of the fog's copies.</summary>
        public Material NormalOf(Material m) => m != null && _undim.TryGetValue(m, out var n) ? n : m;

        private sealed class CellEntry
        {
            public Renderer[] Renderers; public Material[][] Normal; public Light[] Lights;
        }

        private DungeonLayout _layout;
        private FogState[,] _state;
        private bool[,] _seen;
        private Dictionary<GridPoint, CellEntry> _entries = new Dictionary<GridPoint, CellEntry>();
        private HashSet<GridPoint> _visible = new HashSet<GridPoint>(), _prevVisible = new HashSet<GridPoint>();
        private readonly HashSet<int> _roomsMarked = new HashSet<int>();
        private readonly List<GridPoint> _dirty = new List<GridPoint>();
        private readonly Dictionary<Material, Material> _dim = new Dictionary<Material, Material>();
        private readonly Dictionary<Material, Material> _undim = new Dictionary<Material, Material>();
        private float _next;
        private bool _revealed, _showingAll;

        private void Awake()
        {
            if (Builder == null) Builder = GetComponent<DungeonBuilder>() ?? FindFirstObjectByType<DungeonBuilder>();
            if (World == null) World = GetComponent<ClientWorldView>() ?? FindFirstObjectByType<ClientWorldView>();
            if (Bootstrap == null) Bootstrap = GetComponent<DungeonMatchBootstrap>() ?? FindFirstObjectByType<DungeonMatchBootstrap>();
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            if (_revealed) Restore();
        }

        private void OnDestroy()
        {
            foreach (var m in _dim.Values) if (m != null) Destroy(m);
            _dim.Clear(); _undim.Clear();
        }

        /// <summary>The state a cell is drawn in. Hidden when no layout has been seen yet.</summary>
        public FogState StateOf(GridPoint cell) => _state != null && _layout != null && _layout.InBounds(cell) ? _state[cell.X, cell.Y] : FogState.Hidden;

        /// <summary>Recompute now instead of at the next tick.</summary>
        public void Refresh() { _next = 0f; Tick(); }

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f / Mathf.Max(0.5f, UpdatesPerSecond);
            Tick();
        }

        private void Tick()
        {
            if (Builder == null || Builder.Layout == null) return;
            if (!ReferenceEquals(Builder.Layout, _layout)) Init(Builder.Layout);
            var state = Bootstrap != null ? Bootstrap.State : null;
            if (!Enabled || state == null) { ShowAll(); _showingAll = true; return; }

            int team = state.PlayerTeam.TryGetValue(World != null ? World.LocalPlayerId : 1, out int t) ? t : -1;
            int range = state.Descriptor != null ? state.Descriptor.Rules.SightRangeCells : MatchRules.DefaultSightRangeCells;

            (_prevVisible, _visible) = (_visible, _prevVisible);
            _visible.Clear();
            var snap = World != null ? World.Latest : null;
            if (snap != null)
                foreach (var a in snap.Avatars)
                {
                    if (a.IsMob || a.Dead) continue;
                    if (a.Id != World.LocalPlayerId && a.Team != team) continue;
                    var cell = new Vec2(a.X, a.Y).Cell;
                    if (!_layout.InBounds(cell)) continue;
                    _visible.UnionWith(LineOfSight.VisibleCells(_layout, cell, range, state.ClosedDoors));
                }

            _dirty.Clear();
            if (team >= 0 && state.Explored.TryGetValue(team, out var rooms))
                foreach (int id in rooms)
                {
                    if (!_roomsMarked.Add(id) || id < 0 || id >= _layout.Rooms.Count) continue;
                    var b = _layout.Rooms[id].Bounds;
                    for (int x = b.X - 1; x <= b.XMax; x++)
                        for (int y = b.Y - 1; y <= b.YMax; y++)
                        {
                            var c = new GridPoint(x, y);
                            if (!_layout.InBounds(c) || _layout.Tiles[x, y] == TileType.Void) continue;
                            // The ring around the room: its walls and doors, not the corridor beyond them.
                            bool inside = x >= b.X && x < b.XMax && y >= b.Y && y < b.YMax;
                            var tile = _layout.Tiles[x, y];
                            if (!inside && tile == TileType.Corridor) continue;
                            if (!_seen[x, y]) { _seen[x, y] = true; ExploredCount++; _dirty.Add(c); }
                        }
                }
            foreach (var c in _visible) { if (!_seen[c.X, c.Y]) { _seen[c.X, c.Y] = true; ExploredCount++; } _dirty.Add(c); }
            if (_showingAll) { _showingAll = false; _dirty.AddRange(_entries.Keys); }
            foreach (var c in _prevVisible) _dirty.Add(c);

            foreach (var c in _dirty)
            {
                var s = _visible.Contains(c) ? FogState.Visible : _seen[c.X, c.Y] ? FogState.Explored : FogState.Hidden;
                if (_state[c.X, c.Y] == s) continue;
                _state[c.X, c.Y] = s;
                if (!_revealed) Apply(c, s);
            }
            VisibleCount = _visible.Count;
            Updates++;
        }

        private void Init(DungeonLayout layout)
        {
            _layout = layout;
            _state = new FogState[layout.Width, layout.Height];
            _seen = new bool[layout.Width, layout.Height];
            _entries.Clear(); _visible.Clear(); _prevVisible.Clear(); _roomsMarked.Clear(); ExploredCount = 0; _showingAll = false;
            var rends = new List<Renderer>(); var lights = new List<Light>();
            foreach (var kv in Builder.CellObjects)
            {
                rends.Clear(); lights.Clear();
                foreach (var go in kv.Value)
                {
                    if (go == null) continue;
                    rends.AddRange(go.GetComponentsInChildren<Renderer>(true));
                    lights.AddRange(go.GetComponentsInChildren<Light>(true));
                }
                var e = new CellEntry { Renderers = rends.ToArray(), Lights = lights.ToArray() };
                e.Normal = new Material[e.Renderers.Length][];
                for (int i = 0; i < e.Renderers.Length; i++) e.Normal[i] = e.Renderers[i].sharedMaterials;
                _entries[kv.Key] = e;
            }
            for (int x = 0; x < layout.Width; x++)
                for (int y = 0; y < layout.Height; y++) _state[x, y] = FogState.Hidden;
            foreach (var kv in _entries) Apply(kv.Key, FogState.Hidden);
        }

        private void ShowAll()
        {
            if (_state == null) return;
            foreach (var kv in _entries)
                if (_state[kv.Key.X, kv.Key.Y] != FogState.Visible) { _state[kv.Key.X, kv.Key.Y] = FogState.Visible; Apply(kv.Key, FogState.Visible); }
        }

        private void Apply(GridPoint cell, FogState s)
        {
            if (!_entries.TryGetValue(cell, out var e)) return;
            for (int i = 0; i < e.Renderers.Length; i++)
            {
                var r = e.Renderers[i];
                if (r == null) continue;
                r.enabled = s != FogState.Hidden;
                if (s == FogState.Hidden) continue;
                r.sharedMaterials = s == FogState.Visible ? e.Normal[i] : DimOf(e.Normal[i]);
            }
            foreach (var l in e.Lights) if (l != null) l.enabled = s != FogState.Hidden;
            CellApplied?.Invoke(cell);
        }

        private Material[] DimOf(Material[] normal)
        {
            var dim = new Material[normal.Length];
            for (int i = 0; i < normal.Length; i++) dim[i] = DimOf(normal[i]);
            return dim;
        }

        private Material DimOf(Material m)
        {
            if (m == null) return null;
            if (_dim.TryGetValue(m, out var d)) return d;
            d = new Material(m) { name = m.name + " (fog)" };
            foreach (var prop in new[] { "_BaseColor", "_Color", "_TintColor" })
            {
                if (!d.HasProperty(prop)) continue;
                var c = d.GetColor(prop);
                float grey = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
                var k = Color.Lerp(new Color(grey, grey, grey, c.a), c, DimSaturation) * DimBrightness;
                d.SetColor(prop, new Color(k.r * DimTint.r, k.g * DimTint.g, k.b * DimTint.b, c.a));
            }
            if (d.HasProperty("_EmissionColor")) d.SetColor("_EmissionColor", d.GetColor("_EmissionColor") * 0.15f);
            _dim[m] = d; _undim[d] = m;
            return d;
        }

        // ---- a camera without the game rig (an overview render) sees everything ----

        private void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (_state == null || cam == null || cam.cameraType != CameraType.Game) return;
            var rig = cam.GetComponent<TopDownCamera>();
            if (rig == null || rig.isActiveAndEnabled) return;
            _revealed = true;
            foreach (var kv in _entries) Apply(kv.Key, FogState.Visible);
        }

        private void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (_revealed) Restore();
        }

        private void Restore()
        {
            _revealed = false;
            if (_state == null) return;
            foreach (var kv in _entries) Apply(kv.Key, _state[kv.Key.X, kv.Key.Y]);
        }
    }
}
