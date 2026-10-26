using System.Collections.Generic;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using UnityEngine;

namespace DungeonGen.Unity
{
    public enum BarKind { Ally, Enemy, Mob, Boss }

    /// <summary>
    /// Health bars over every visible combatant, drawn on one Screen Space Overlay canvas from pooled widgets and placed each LateUpdate
    /// from <c>Camera.WorldToScreenPoint</c>. Ally players green-blue, enemy players red, mobs orange, a boss a wide purple bar with its name;
    /// the local player is not shown (the HUD has it). A downed combatant shows a grey bar and a skull. Players carry a class letter
    /// (W, R, M, E, Me, S); an enemy player never shows a name or gear (DECISIONS, Fight rules 5). Also flashes an avatar white for a moment
    /// when a Hit effect lands on it, through a MaterialPropertyBlock that works for capsules and skinned models alike and is restored exactly.
    /// The assembly has no uGUI reference, so the widgets are CanvasRenderers with hand-built meshes (rectangles, a font-atlas text run, a
    /// pixel-art skull) on the engine's own Canvas; they use the always-included Sprites/Default shader.
    /// </summary>
    public sealed class WorldBars : MonoBehaviour
    {
        public ClientWorldView World;
        public AbilityEffects Effects;
        public DungeonBuilder Builder;
        public Camera Camera;
        public float FlashSeconds = 0.12f;
        public float HeadroomMetres = 0.35f;

        public Color AllyBar = new Color(0.2f, 0.85f, 0.7f), EnemyBar = new Color(0.95f, 0.2f, 0.2f), MobBar = new Color(1f, 0.6f, 0.1f), BossBar = new Color(0.62f, 0.2f, 0.9f), DownedBar = new Color(0.55f, 0.55f, 0.58f);

        /// <summary>What a widget currently shows; read by tests.</summary>
        public struct BarInfo { public int Id; public BarKind Kind; public float Fill; public bool Downed; public string Letter, Name; public Vector2 Screen; }

        private sealed class Widget
        {
            public GameObject Go; public RectTransform Rt;
            public CanvasRenderer Bar, Text, Skull;
            public Mesh BarMesh, TextMesh, SkullMesh;
            public int Id; public BarInfo Info; public bool Shown;
            public int LastFill = -1, LastScale = -1, LastTextKey = -1; public BarKind LastKind; public bool LastDowned;
            public string LastLetter, LastName; public int LastFontVersion, LastSkullKey = -1;
        }

        private sealed class AvatarCache { public GameObject Go; public Renderer[] Rends = new Renderer[0]; public float RefreshAt; }
        private sealed class Flash { public GameObject Go; public Renderer[] Rends; public MaterialPropertyBlock[] Saved; public float Until; }

        private Canvas _canvas;
        private Font _font;
        private Material _flatMat, _textMat, _skullMat;
        private Texture2D _skullTex;
        private int _fontVersion = 1;
        private readonly Stack<Widget> _pool = new Stack<Widget>();
        private readonly Dictionary<int, Widget> _active = new Dictionary<int, Widget>();
        private readonly Dictionary<int, AvatarCache> _cache = new Dictionary<int, AvatarCache>();
        private readonly Dictionary<int, Flash> _flashes = new Dictionary<int, Flash>();
        private readonly List<int> _scratch = new List<int>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private AbilityEffects _subscribed;
        private int _localTeam = -1;
        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Color32> _cols = new List<Color32>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<int> _tris = new List<int>();

        public Canvas Canvas => _canvas;
        public int ActiveCount => _active.Count;
        public bool TryGetBar(int id, out BarInfo info)
        {
            if (_active.TryGetValue(id, out var w) && w.Shown) { info = w.Info; return true; }
            info = default; return false;
        }
        public bool IsFlashing(int id) => _flashes.ContainsKey(id);

        /// <summary>Render the bars through a camera (so a camera render, such as a test screenshot, includes them). Overlay canvases are not part of camera renders.</summary>
        public void UseCameraSpace(Camera cam)
        {
            EnsureCanvas();
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.worldCamera = cam; _canvas.planeDistance = Mathf.Max(0.5f, cam.nearClipPlane + 0.5f);
        }
        public void UseOverlay() { EnsureCanvas(); _canvas.renderMode = RenderMode.ScreenSpaceOverlay; _canvas.worldCamera = null; }

        private void OnDisable()
        {
            if (_subscribed != null) _subscribed.EffectArrived -= OnEffect;
            _subscribed = null;
            foreach (var f in new List<Flash>(_flashes.Values)) Restore(f);
            _flashes.Clear();
            Font.textureRebuilt -= OnFontRebuilt;
        }

        private void OnEnable() { Font.textureRebuilt += OnFontRebuilt; }
        private void OnFontRebuilt(Font f) { if (f == _font) _fontVersion++; }

        private void OnDestroy()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
            if (_flatMat != null) Destroy(_flatMat);
            if (_textMat != null) Destroy(_textMat);
            if (_skullMat != null) Destroy(_skullMat);
            if (_skullTex != null) Destroy(_skullTex);
        }

        // ---- frame ----

        private void Update()
        {
            if (Effects != _subscribed)
            {
                if (_subscribed != null) _subscribed.EffectArrived -= OnEffect;
                _subscribed = Effects;
                if (_subscribed != null) _subscribed.EffectArrived += OnEffect;
            }
        }

        private void LateUpdate()
        {
            UpdateFlashes();
            var cam = Camera != null ? Camera : UnityEngine.Camera.main;
            var snap = World != null ? World.Latest : null;
            if (cam == null || snap == null) { HideAll(); return; }
            EnsureCanvas();
            if (_canvas.renderMode == RenderMode.ScreenSpaceCamera && _canvas.worldCamera != cam) _canvas.worldCamera = cam;

            _localTeam = -1;
            foreach (var a in snap.Avatars) if (a.Id == World.LocalPlayerId) { _localTeam = a.Team; break; }

            float px = Mathf.Clamp(cam.pixelHeight / 900f, 0.75f, 2.5f);
            int scaleKey = Mathf.RoundToInt(px * 4f);
            px = scaleKey / 4f;
            var rect = cam.pixelRect;
            bool overlay = _canvas.renderMode == RenderMode.ScreenSpaceOverlay;
            var origin = overlay ? Vector2.zero : new Vector2(rect.x, rect.y);
            float inv = 1f / Mathf.Max(0.01f, _canvas.scaleFactor);

            _seen.Clear();
            foreach (var a in snap.Avatars)
            {
                if (a.Id == World.LocalPlayerId || a.Dead) continue;
                if (a.Invisible && !(a.IsMob == false && a.Team == _localTeam)) continue;
                if (!World.Avatars.TryGetValue(a.Id, out var go) || go == null) continue;
                var head = HeadPoint(a.Id, go);
                var sp = cam.WorldToScreenPoint(head);
                if (sp.z <= 0f || sp.x < rect.xMin - 80f || sp.x > rect.xMax + 80f || sp.y < rect.yMin - 40f || sp.y > rect.yMax + 80f) continue;

                bool boss = IsBoss(a);
                var kind = boss ? BarKind.Boss : a.IsMob ? BarKind.Mob : a.Team == _localTeam ? BarKind.Ally : BarKind.Enemy;
                if (!_active.TryGetValue(a.Id, out var w)) { w = Take(); w.Id = a.Id; _active[a.Id] = w; }
                _seen.Add(a.Id);
                w.Go.SetActive(true); w.Shown = true;
                float frac = Mathf.Clamp01(a.HealthFraction);
                string letter = a.IsMob ? null : LetterFor(a.ClassIndex);
                string name = boss ? a.Label : null;                 // names only for bosses; an enemy player is a class letter and a colour
                w.Rt.anchoredPosition = new Vector2((sp.x - origin.x) * inv, (sp.y - origin.y) * inv);
                w.Info = new BarInfo { Id = a.Id, Kind = kind, Fill = frac, Downed = a.Downed, Letter = letter, Name = name, Screen = new Vector2(sp.x, sp.y) };
                Rebuild(w, kind, frac, a.Downed, letter, name, scaleKey, px);
            }

            _scratch.Clear();
            foreach (var kv in _active) if (!_seen.Contains(kv.Key)) _scratch.Add(kv.Key);
            foreach (var id in _scratch) { var w = _active[id]; w.Go.SetActive(false); w.Shown = false; _active.Remove(id); _pool.Push(w); }
            // forget renderer caches of avatars that are gone
            if (_cache.Count > World.Avatars.Count + 8)
            {
                _scratch.Clear();
                foreach (var kv in _cache) if (!World.Avatars.TryGetValue(kv.Key, out var g) || g != kv.Value.Go) _scratch.Add(kv.Key);
                foreach (var id in _scratch) _cache.Remove(id);
            }
        }

        private void HideAll()
        {
            _scratch.Clear();
            foreach (var kv in _active) _scratch.Add(kv.Key);
            foreach (var id in _scratch) { var w = _active[id]; w.Go.SetActive(false); w.Shown = false; _active.Remove(id); _pool.Push(w); }
        }

        private static bool IsBoss(AvatarSnapshot a)
        {
            if (!a.IsMob || string.IsNullOrEmpty(a.Label)) return false;
            if (a.Label.StartsWith(BossArchetype.FinalBoss.Name)) return true;
            foreach (var b in BossArchetype.DefaultPool) if (a.Label.StartsWith(b.Name)) return true;
            return false;
        }

        private static string LetterFor(int classIndex)
        {
            var roster = ClassArchetype.DefaultRoster;
            if (classIndex < 0 || classIndex >= roster.Count) return "?";
            string n = roster[classIndex].Name;
            return n == "Mender" ? "Me" : n.Substring(0, 1);
        }

        // ---- head position ----

        private Vector3 HeadPoint(int id, GameObject go)
        {
            if (!_cache.TryGetValue(id, out var c) || c.Go != go) { c = new AvatarCache { Go = go }; _cache[id] = c; c.RefreshAt = -1f; }
            if (Time.time >= c.RefreshAt)
            {
                var all = go.GetComponentsInChildren<Renderer>(false);
                var list = new List<Renderer>(all.Length);
                foreach (var r in all) if (IsBody(r)) list.Add(r);
                c.Rends = list.ToArray(); c.RefreshAt = Time.time + 1f;
            }
            bool any = false; Bounds b = default;
            foreach (var r in c.Rends)
            {
                if (r == null || !r.enabled) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) return go.transform.position + Vector3.up * 2f;
            return new Vector3(b.center.x, b.max.y + HeadroomMetres, b.center.z);
        }

        private static bool IsBody(Renderer r) => (r is MeshRenderer || r is SkinnedMeshRenderer) && r.gameObject.name != "UltimateAura";

        // ---- hit flash ----

        private void OnEffect(EffectSnapshot e)
        {
            if (e.Kind != EffectKind.Hit || World == null) return;
            if (!World.Avatars.TryGetValue(e.Target, out var go) || go == null) return;
            if (_flashes.TryGetValue(e.Target, out var running)) { running.Until = Time.time + FlashSeconds; return; }
            var rends = new List<Renderer>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(false)) if (IsBody(r)) rends.Add(r);
            if (rends.Count == 0) return;
            var f = new Flash { Go = go, Rends = rends.ToArray(), Saved = new MaterialPropertyBlock[rends.Count], Until = Time.time + FlashSeconds };
            var flashBlock = new MaterialPropertyBlock();
            for (int i = 0; i < f.Rends.Length; i++)
            {
                var saved = new MaterialPropertyBlock();
                f.Rends[i].GetPropertyBlock(saved);
                f.Saved[i] = saved;
                flashBlock.Clear();
                f.Rends[i].GetPropertyBlock(flashBlock);
                flashBlock.SetColor("_BaseColor", Color.white);
                flashBlock.SetColor("_Color", Color.white);
                flashBlock.SetColor("_EmissionColor", new Color(1f, 1f, 1f, 1f));
                f.Rends[i].SetPropertyBlock(flashBlock);
            }
            _flashes[e.Target] = f;
        }

        private void UpdateFlashes()
        {
            if (_flashes.Count == 0) return;
            _scratch.Clear();
            foreach (var kv in _flashes) if (Time.time >= kv.Value.Until || kv.Value.Go == null) _scratch.Add(kv.Key);
            foreach (var id in _scratch) { Restore(_flashes[id]); _flashes.Remove(id); }
        }

        private static void Restore(Flash f)
        {
            for (int i = 0; i < f.Rends.Length; i++)
            {
                var r = f.Rends[i]; if (r == null) continue;
                r.SetPropertyBlock(f.Saved[i].isEmpty ? null : f.Saved[i]);
            }
        }

        // ---- canvas and widgets ----

        private void EnsureCanvas()
        {
            if (_canvas != null) return;
            var go = new GameObject("WorldBarsCanvas");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = -10;                  // under menus and the HUD
            _flatMat = new Material(Shader.Find("Sprites/Default")) { name = "WorldBarsFlat" };
            _textMat = new Material(Shader.Find("Sprites/Default")) { name = "WorldBarsText" };
            _skullTex = MakeSkull();
            _skullMat = new Material(Shader.Find("Sprites/Default")) { name = "WorldBarsSkull", mainTexture = _skullTex };
            _flatMat.mainTexture = Texture2D.whiteTexture;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 16);
        }

        private Widget Take()
        {
            if (_pool.Count > 0) return _pool.Pop();
            var w = new Widget();
            w.Go = new GameObject("Bar", typeof(RectTransform));
            w.Rt = (RectTransform)w.Go.transform;
            w.Rt.SetParent(_canvas.transform, false);
            w.Rt.anchorMin = w.Rt.anchorMax = Vector2.zero;
            w.Rt.pivot = new Vector2(0.5f, 0f);
            w.Rt.sizeDelta = Vector2.zero;
            w.Bar = Child(w.Go, "Bar", _flatMat, out w.BarMesh);
            w.Text = Child(w.Go, "Text", _textMat, out w.TextMesh);
            w.Skull = Child(w.Go, "Skull", _skullMat, out w.SkullMesh);
            return w;
        }

        private static CanvasRenderer Child(GameObject parent, string name, Material mat, out Mesh mesh)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent.transform, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero; rt.sizeDelta = Vector2.zero;
            var cr = go.GetComponent<CanvasRenderer>();
            mesh = new Mesh { name = "WorldBarsMesh" }; mesh.MarkDynamic();
            cr.materialCount = 1;
            cr.SetMaterial(mat, 0);
            return cr;
        }

        private void Rebuild(Widget w, BarKind kind, float frac, bool downed, string letter, string name, int scaleKey, float px)
        {
            int fillKey = Mathf.RoundToInt(frac * 200f);
            bool boss = kind == BarKind.Boss;
            float bw = Mathf.Round((boss ? 260f : 64f) * px), bh = Mathf.Round((boss ? 16f : 8f) * px);

            if (w.LastFill != fillKey || w.LastKind != kind || w.LastDowned != downed || w.LastScale != scaleKey)
            {
                Begin();
                float border = Mathf.Max(1f, Mathf.Round(px));
                Quad(-bw / 2f - border, -border, bw + border * 2f, bh + border * 2f, new Color(0f, 0f, 0f, 0.8f));
                Quad(-bw / 2f, 0f, bw, bh, new Color(0.12f, 0.12f, 0.14f, 0.85f));
                var col = downed ? DownedBar : kind == BarKind.Boss ? BossBar : kind == BarKind.Mob ? MobBar : kind == BarKind.Enemy ? EnemyBar : AllyBar;
                float fw = Mathf.Round(bw * (fillKey / 200f));
                if (fw > 0f) Quad(-bw / 2f, 0f, fw, bh, col);
                if (fw > 0f) Quad(-bw / 2f, bh * 0.55f, fw, bh * 0.45f, new Color(1f, 1f, 1f, 0.18f));   // soft highlight so the bar reads as a bar
                Flush(w.BarMesh, w.Bar);
                w.LastFill = fillKey; w.LastKind = kind; w.LastDowned = downed; w.LastScale = scaleKey;
                w.LastTextKey = -1;                                  // text and skull positions depend on the bar size
            }

            bool needText = w.LastLetter != letter || w.LastName != name || w.LastTextKey != scaleKey || w.LastFontVersion != _fontVersion;
            if (needText)
            {
                w.LastFontVersion = _fontVersion;
                Begin();
                int size = Mathf.Max(8, Mathf.RoundToInt(13f * px));
                if (!string.IsNullOrEmpty(letter))
                {
                    var col = kind == BarKind.Enemy ? new Color(1f, 0.55f, 0.55f) : new Color(0.75f, 1f, 0.95f);
                    float width = TextWidth(letter, size);
                    AddText(letter, size, -bw / 2f - 5f * px - width, bh / 2f, col);
                }
                if (!string.IsNullOrEmpty(name))
                {
                    int bigger = Mathf.Max(10, Mathf.RoundToInt(16f * px));
                    float width = TextWidth(name, bigger);
                    AddText(name, bigger, -width / 2f, bh + 4f * px + bigger * 0.5f, new Color(0.95f, 0.8f, 1f));
                }
                Flush(w.TextMesh, w.Text);
                if (_font != null && _font.material != null) _textMat.mainTexture = _font.material.mainTexture;
                w.LastLetter = letter; w.LastName = name; w.LastTextKey = scaleKey;
            }

            // skull for the downed
            int skullKey = (downed ? 1 : 0) | (kind == BarKind.Boss ? 2 : 0) | (scaleKey << 2);
            if (w.LastSkullKey != skullKey)
            {
                Begin();
                if (downed)
                {
                    float s = Mathf.Round(12f * px);
                    float x = bw / 2f + 5f * px;
                    AddUvQuad(x, bh / 2f - s / 2f, s, s, new Color(0.9f, 0.9f, 0.92f));
                }
                Flush(w.SkullMesh, w.Skull);
                w.LastSkullKey = skullKey;
            }
        }

        // ---- mesh helpers ----

        private void Begin() { _verts.Clear(); _cols.Clear(); _uvs.Clear(); _tris.Clear(); }

        private void Quad(float x, float y, float w, float h, Color c)
        {
            int i = _verts.Count;
            _verts.Add(new Vector3(x, y, 0)); _verts.Add(new Vector3(x, y + h, 0)); _verts.Add(new Vector3(x + w, y + h, 0)); _verts.Add(new Vector3(x + w, y, 0));
            for (int k = 0; k < 4; k++) { _cols.Add(c); _uvs.Add(new Vector2(0.5f, 0.5f)); }
            _tris.Add(i); _tris.Add(i + 1); _tris.Add(i + 2); _tris.Add(i); _tris.Add(i + 2); _tris.Add(i + 3);
        }

        private void AddUvQuad(float x, float y, float w, float h, Color c)
        {
            int i = _verts.Count;
            _verts.Add(new Vector3(x, y, 0)); _verts.Add(new Vector3(x, y + h, 0)); _verts.Add(new Vector3(x + w, y + h, 0)); _verts.Add(new Vector3(x + w, y, 0));
            for (int k = 0; k < 4; k++) _cols.Add(c);
            _uvs.Add(new Vector2(0, 0)); _uvs.Add(new Vector2(0, 1)); _uvs.Add(new Vector2(1, 1)); _uvs.Add(new Vector2(1, 0));
            _tris.Add(i); _tris.Add(i + 1); _tris.Add(i + 2); _tris.Add(i); _tris.Add(i + 2); _tris.Add(i + 3);
        }

        private void Flush(Mesh m, CanvasRenderer cr)
        {
            m.Clear();
            if (_verts.Count > 0)
            {
                m.SetVertices(_verts); m.SetColors(_cols); m.SetUVs(0, _uvs); m.SetTriangles(_tris, 0);
                cr.SetMesh(m);
            }
            else cr.Clear();
        }

        private float TextWidth(string s, int size)
        {
            if (_font == null) return 0f;
            _font.RequestCharactersInTexture(s, size, FontStyle.Bold);
            float w = 0f;
            foreach (char ch in s) if (_font.GetCharacterInfo(ch, out var ci, size, FontStyle.Bold)) w += ci.advance;
            return w;
        }

        /// <summary>Append glyph quads for a run of text, starting at x with the glyphs centred vertically on centreY, with a one pixel dark shadow.</summary>
        private void AddText(string s, int size, float x, float centreY, Color c)
        {
            if (_font == null) return;
            _font.RequestCharactersInTexture(s, size, FontStyle.Bold);
            float baseline = centreY - size * 0.35f;
            for (int pass = 0; pass < 2; pass++)
            {
                float pen = x; var col = pass == 0 ? new Color(0f, 0f, 0f, 0.9f) : c;
                float ox = pass == 0 ? 1f : 0f, oy = pass == 0 ? -1f : 0f;
                foreach (char ch in s)
                {
                    if (!_font.GetCharacterInfo(ch, out var ci, size, FontStyle.Bold)) continue;
                    int i = _verts.Count;
                    _verts.Add(new Vector3(pen + ci.minX + ox, baseline + ci.minY + oy, 0)); _verts.Add(new Vector3(pen + ci.minX + ox, baseline + ci.maxY + oy, 0));
                    _verts.Add(new Vector3(pen + ci.maxX + ox, baseline + ci.maxY + oy, 0)); _verts.Add(new Vector3(pen + ci.maxX + ox, baseline + ci.minY + oy, 0));
                    for (int k = 0; k < 4; k++) _cols.Add(col);
                    _uvs.Add(ci.uvBottomLeft); _uvs.Add(ci.uvTopLeft); _uvs.Add(ci.uvTopRight); _uvs.Add(ci.uvBottomRight);
                    _tris.Add(i); _tris.Add(i + 1); _tris.Add(i + 2); _tris.Add(i); _tris.Add(i + 2); _tris.Add(i + 3);
                    pen += ci.advance;
                }
            }
        }

        private static Texture2D MakeSkull()
        {
            string[] rows = { "00111100", "01111110", "11011011", "11011011", "11111111", "01111110", "00111100", "00101000" };
            var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "SkullGlyph" };
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    tex.SetPixel(x, 7 - y, rows[y][x] == '1' ? Color.white : new Color(1f, 1f, 1f, 0f));
            tex.Apply();
            return tex;
        }
    }
}
