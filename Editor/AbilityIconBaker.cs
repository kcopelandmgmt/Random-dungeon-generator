using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DungeonGen.Core.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DungeonGen.Unity.Editor
{
    /// <summary>
    /// Bakes one 128×128 icon per ability in <see cref="AbilityBook.All"/> into <c>Assets/UI/AbilityIcons/&lt;abilityId&gt;.png</c> and fills the
    /// <c>AbilityIcons</c> lookup at <c>Assets/UI/AbilityIcons/Resources/AbilityIcons.asset</c> that the HUD reads.
    ///
    /// Each icon is the ability's own effect from <c>Assets/AbilityVfxLibrary.asset</c> (projectile for a bolt, impact for a basic attack
    /// or an ultimate, cast for a mobility move, falling back through the other roles), a short time into its simulation: the prefab is
    /// instantiated in a preview scene, its particle systems are simulated with a fixed seed to the moment with the most visible
    /// particles, and every live particle is drawn additively (its texture, sheet frame, colour and size, seen from above at 60°) onto a
    /// dark background, then tone-mapped with a soft glow and framed by the ability's element colour.
    /// The drawing is done on the CPU, because the Unity lane runs the editor with <c>-nographics</c> and there is no GPU to render with.
    /// Run: <c>bash Tools/Build/unity-batch.sh method DungeonGen.Unity.Editor.AbilityIconBaker.Bake</c>. Report: <c>Logs/ability-icons.txt</c>.
    /// </summary>
    public static class AbilityIconBaker
    {
        public const string Folder = "Assets/UI/AbilityIcons";
        public const string LookupPath = Folder + "/Resources/AbilityIcons.asset";
        public const string LibraryPath = "Assets/AbilityVfxLibrary.asset";
        public const int Size = 128;

        [MenuItem("Dungeon Gen/Bake Ability Icons")]
        public static void Bake()
        {
            var report = new StringBuilder();
            report.AppendLine($"Ability icons {DateTime.Now:yyyy-MM-dd HH:mm}  ({AbilityBook.All.Count} abilities)");
            report.AppendLine("id | role | prefab | particles | sim time | note");
            int made = 0, empty = 0;
            try
            {
                EnsureFolder(Folder); EnsureFolder(Folder + "/Resources");
                var lib = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>(LibraryPath);
                if (lib == null) Debug.LogWarning("Dungeon Gen icons: no AbilityVfxLibrary at " + LibraryPath + "; every icon is a plain element glyph.");
                var preview = EditorSceneManager.NewPreviewScene();
                var paths = new List<(string id, string path)>();
                try
                {
                    foreach (var a in AbilityBook.All)
                    {
                        var (role, prefab) = Choose(lib, a);
                        var buf = new float[Size * Size * 3];
                        int count = 0; float t = 0f; string note = "";
                        if (prefab != null)
                        {
                            try { (count, t) = Splat(prefab, preview, buf); }
                            catch (Exception ex) { note = "simulation failed: " + ex.Message; count = 0; }
                        }
                        else note = "no prefab";
                        if (count == 0) { Glyph(buf, ElementColour(a.Element)); empty++; if (note == "") note = "no particles; glyph"; }
                        var png = Compose(buf, ElementColour(a.Element), a.Kind == AbilityKind.Ultimate);
                        var path = $"{Folder}/{a.Id}.png";
                        File.WriteAllBytes(Abs(path), png);
                        paths.Add((a.Id, path));
                        report.AppendLine($"{a.Id} | {role} | {(prefab != null ? prefab.name : "-")} | {count} | {t:0.00} | {note}");
                        made++;
                    }
                }
                finally { EditorSceneManager.ClosePreviewScene(preview); }

                AssetDatabase.Refresh();
                foreach (var (_, path) in paths) ConfigureSprite(path);
                WriteLookup(paths);
                AssetDatabase.SaveAssets();
                Debug.Log($"Dungeon Gen: ability icons baked: {made} ({empty} without particles) -> {Folder}");
            }
            catch (Exception ex)
            {
                Debug.LogError("Dungeon Gen: ability icon bake failed: " + ex);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
            finally
            {
                var logs = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs");
                Directory.CreateDirectory(logs);
                File.WriteAllText(Path.Combine(logs, "ability-icons.txt"), report.ToString());
            }
        }

        // ------------------------------------------------------------ choosing the effect

        private static (VfxRole role, GameObject prefab) Choose(AbilityVfxLibrary lib, AbilityDefinition a)
        {
            if (lib == null) return (VfxRole.Cast, null);
            VfxRole[] order;
            switch (a.Kind)
            {
                case AbilityKind.Bolt: order = new[] { VfxRole.Projectile, VfxRole.Impact, VfxRole.Cast, VfxRole.Zone }; break;
                case AbilityKind.Mobility: order = new[] { VfxRole.Cast, VfxRole.Impact, VfxRole.Projectile, VfxRole.Zone }; break;
                case AbilityKind.Zone: order = new[] { VfxRole.Impact, VfxRole.Cast, VfxRole.Zone, VfxRole.Projectile }; break;
                default: order = new[] { VfxRole.Impact, VfxRole.Cast, VfxRole.Projectile, VfxRole.Zone }; break;
            }
            var e = lib.Find(a.Id);
            if (e != null) foreach (var r in order) if (e.Get(r) != null) return (r, e.Get(r));
            foreach (var r in order) { var p = lib.Prefab(a.Id, r); if (p != null) return (r, p); }
            return (order[0], null);
        }

        // ------------------------------------------------------------ simulation and splatting

        private static readonly Vector3 Right = Vector3.right;
        private static readonly Vector3 Up = new Vector3(0f, Mathf.Cos(60f * Mathf.Deg2Rad), Mathf.Sin(60f * Mathf.Deg2Rad));   // seen from above at 60°

        private struct Dot { public Vector2 P; public float R; public Color C; public Sprite2 S; }

        private sealed class Sprite2 { public int W, H; public Color[] Px; }

        private static (int count, float time) Splat(GameObject prefab, Scene preview, float[] buf)
        {
            var go = UnityEngine.Object.Instantiate(prefab);
            SceneManager.MoveGameObjectToScene(go, preview);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            try
            {
                var systems = go.GetComponentsInChildren<ParticleSystem>(true);
                if (systems.Length == 0) return (0, 0f);
                foreach (var ps in systems)
                {
                    ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.useAutoRandomSeed = false; ps.randomSeed = 1234;
                }
                var roots = systems.Where(ps => ps.transform.parent == null || ps.transform.parent.GetComponentInParent<ParticleSystem>() == null).ToArray();
                // The moment with the most visible particle area.
                float bestT = 0.3f, bestScore = -1f;
                foreach (float t in new[] { 0.12f, 0.25f, 0.4f, 0.6f, 0.85f, 1.2f })
                {
                    foreach (var ps in roots) ps.Simulate(t, true, true, true);
                    float score = 0f;
                    foreach (var dot in Collect(systems)) score += dot.R * dot.R * dot.C.a * (dot.C.r + dot.C.g + dot.C.b);
                    if (score > bestScore * 1.15f) { bestScore = score; bestT = t; }
                }
                foreach (var ps in roots) ps.Simulate(bestT, true, true, true);
                var dots = Collect(systems);
                if (dots.Count == 0) return (0, bestT);
                Draw(dots, buf);
                return (dots.Count, bestT);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        private static readonly ParticleSystem.Particle[] _particles = new ParticleSystem.Particle[4096];

        private static List<Dot> Collect(ParticleSystem[] systems)
        {
            var dots = new List<Dot>();
            foreach (var ps in systems)
            {
                var pr = ps.GetComponent<ParticleSystemRenderer>();
                if (pr == null || !pr.enabled || pr.sharedMaterial == null) continue;
                int n = ps.GetParticles(_particles);
                if (n == 0) continue;
                var mat = pr.sharedMaterial;
                var tint = MaterialTint(mat);
                var tex = MainTexture(mat);
                var tsa = ps.textureSheetAnimation;
                bool local = ps.main.simulationSpace == ParticleSystemSimulationSpace.Local;
                for (int i = 0; i < n; i++)
                {
                    var p = _particles[i];
                    var world = local ? ps.transform.TransformPoint(p.position) : p.position;
                    var size3 = p.GetCurrentSize3D(ps);
                    float size = Mathf.Max(size3.x, size3.y) * (local ? ps.transform.lossyScale.x : 1f);
                    if (pr.renderMode == ParticleSystemRenderMode.Stretch) size *= 1.3f;
                    if (size <= 0.001f) continue;
                    Color c = p.GetCurrentColor(ps);
                    c = new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, c.a * Mathf.Clamp01(tint.a));
                    if (c.a < 0.01f) continue;
                    var sprite = tex;
                    if (tex != null && tsa.enabled && tsa.mode == ParticleSystemAnimationMode.Grid && (tsa.numTilesX > 1 || tsa.numTilesY > 1))
                    {
                        float life = p.startLifetime > 0f ? 1f - p.remainingLifetime / p.startLifetime : 0f;
                        int frames = tsa.numTilesX * tsa.numTilesY;
                        int frame = Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(life * Mathf.Max(1f, tsa.cycleCount), 1f) * frames), 0, frames - 1);
                        sprite = Tile(tex, tsa.numTilesX, tsa.numTilesY, frame);
                    }
                    dots.Add(new Dot { P = new Vector2(Vector3.Dot(world, Right), Vector3.Dot(world, Up)), R = size * 0.5f, C = c, S = sprite });
                }
            }
            return dots;
        }

        private static void Draw(List<Dot> dots, float[] buf)
        {
            // Fit the bulk of the particles (5th..95th percentile, plus their radii) into the icon inside the frame.
            var xs = dots.Select(d => d.P.x).OrderBy(v => v).ToList(); var ys = dots.Select(d => d.P.y).OrderBy(v => v).ToList();
            float Pct(List<float> l, float q) => l[Mathf.Clamp(Mathf.RoundToInt(q * (l.Count - 1)), 0, l.Count - 1)];
            float medR = dots.Select(d => d.R).OrderBy(v => v).ElementAt(dots.Count / 2);
            float x0 = Pct(xs, 0.05f) - medR, x1 = Pct(xs, 0.95f) + medR, y0 = Pct(ys, 0.05f) - medR, y1 = Pct(ys, 0.95f) + medR;
            float span = Mathf.Max(0.2f, Mathf.Max(x1 - x0, y1 - y0));
            float inner = Size - 28f;
            float scale = inner / span;
            var centre = new Vector2((x0 + x1) * 0.5f, (y0 + y1) * 0.5f);
            foreach (var d in dots)
            {
                float cx = Size * 0.5f + (d.P.x - centre.x) * scale, cy = Size * 0.5f + (d.P.y - centre.y) * scale;
                float r = Mathf.Clamp(d.R * scale, 0.8f, Size * 0.6f);
                int xa = Mathf.Max(0, Mathf.FloorToInt(cx - r)), xb = Mathf.Min(Size - 1, Mathf.CeilToInt(cx + r));
                int ya = Mathf.Max(0, Mathf.FloorToInt(cy - r)), yb = Mathf.Min(Size - 1, Mathf.CeilToInt(cy + r));
                for (int y = ya; y <= yb; y++)
                    for (int x = xa; x <= xb; x++)
                    {
                        float u = (x + 0.5f - (cx - r)) / (2f * r), v = (y + 0.5f - (cy - r)) / (2f * r);
                        if (u < 0f || u > 1f || v < 0f || v > 1f) continue;
                        Color s = Sample(d.S, u, v);
                        // A soft round falloff over every particle quad: noise and flow textures are opaque squares, a real renderer
                        // softens them with depth fade and blending this splat does not have.
                        float du = u - 0.5f, dv = v - 0.5f; float rr = Mathf.Sqrt(du * du + dv * dv) * 2f;
                        float soft = Mathf.Clamp01((1f - rr) * 1.6f);
                        float w = s.a * d.C.a * soft * soft;
                        if (w <= 0.002f) continue;
                        int i = (y * Size + x) * 3;
                        buf[i] += s.r * d.C.r * w; buf[i + 1] += s.g * d.C.g * w; buf[i + 2] += s.b * d.C.b * w;
                    }
            }
        }

        private static Color Sample(Sprite2 s, float u, float v)
        {
            if (s == null)
            {
                float dx = u - 0.5f, dy = v - 0.5f; float k = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) * 2f);
                return new Color(1f, 1f, 1f, k * k);
            }
            int x = Mathf.Clamp((int)(u * s.W), 0, s.W - 1), y = Mathf.Clamp((int)(v * s.H), 0, s.H - 1);
            return s.Px[y * s.W + x];
        }

        // ------------------------------------------------------------ textures (read from the files: textures are not CPU-readable)

        private static readonly Dictionary<Texture, Sprite2> _textures = new Dictionary<Texture, Sprite2>();
        private static readonly Dictionary<(Sprite2, int, int, int), Sprite2> _tiles = new Dictionary<(Sprite2, int, int, int), Sprite2>();

        private static Sprite2 MainTexture(Material m)
        {
            Texture tex = null;
            foreach (var name in new[] { "_MainTex", "_BaseMap", "_BaseTex", "_MainTexture", "_Texture", "_Albedo" })
                if (m.HasProperty(name) && m.GetTexture(name) != null) { tex = m.GetTexture(name); break; }
            if (tex == null)
                foreach (var name in m.GetTexturePropertyNames()) { var t = m.GetTexture(name); if (t is Texture2D) { tex = t; break; } }
            if (tex == null) return null;
            if (_textures.TryGetValue(tex, out var cached)) return cached;
            Sprite2 s = null;
            var path = AssetDatabase.GetAssetPath(tex);
            if (!string.IsNullOrEmpty(path) && (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)))
            {
                var t2 = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (t2.LoadImage(File.ReadAllBytes(Abs(path))))
                {
                    // Down-sample to at most 128 px a side; an icon particle is rarely bigger.
                    int w = Mathf.Min(128, t2.width), h = Mathf.Min(128, t2.height);
                    var src = t2.GetPixels(); var px = new Color[w * h];
                    bool greyAlpha = true;
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            var c = src[(y * t2.height / h) * t2.width + x * t2.width / w];
                            px[y * w + x] = c;
                            if (c.a < 0.99f) greyAlpha = false;
                        }
                    // An opaque texture used as a mask (black background) carries its shape in brightness.
                    if (greyAlpha) for (int i = 0; i < px.Length; i++) { var c = px[i]; float l = Mathf.Max(c.r, Mathf.Max(c.g, c.b)); px[i] = new Color(c.r, c.g, c.b, l); }
                    s = new Sprite2 { W = w, H = h, Px = px };
                }
                UnityEngine.Object.DestroyImmediate(t2);
            }
            _textures[tex] = s;
            return s;
        }

        private static Sprite2 Tile(Sprite2 s, int nx, int ny, int frame)
        {
            if (s == null) return null;
            var key = (s, nx, ny, frame);
            if (_tiles.TryGetValue(key, out var t)) return t;
            int tw = Mathf.Max(1, s.W / nx), th = Mathf.Max(1, s.H / ny);
            int fx = frame % nx, fy = ny - 1 - frame / nx;   // sheets run left to right, top to bottom
            var px = new Color[tw * th];
            for (int y = 0; y < th; y++) for (int x = 0; x < tw; x++) px[y * tw + x] = s.Px[Mathf.Min(s.H - 1, fy * th + y) * s.W + Mathf.Min(s.W - 1, fx * tw + x)];
            t = new Sprite2 { W = tw, H = th, Px = px };
            _tiles[key] = t;
            return t;
        }

        private static Color MaterialTint(Material m)
        {
            foreach (var name in new[] { "_TintColor", "_Color", "_BaseColor", "_MainColor", "_EmissionColor" })
                if (m.HasProperty(name))
                {
                    var c = m.GetColor(name);
                    float mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                    if (mx > 1f) c = new Color(c.r / mx, c.g / mx, c.b / mx, c.a);   // HDR: keep the hue, the glow comes from the tone curve
                    if (mx < 0.05f) continue;
                    return new Color(c.r, c.g, c.b, c.a <= 0.01f ? 1f : c.a);
                }
            return Color.white;
        }

        // ------------------------------------------------------------ composing

        private static void Glyph(float[] buf, Color element)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float dx = (x + 0.5f) / Size - 0.5f, dy = (y + 0.5f) / Size - 0.5f; float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Atan2(dy, dx);
                    float ray = Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 4f)), 8f) * Mathf.Clamp01(1f - r * 2.4f);
                    float k = Mathf.Clamp01(1f - r * 4.5f) * 1.6f + ray * 0.9f;
                    int i = (y * Size + x) * 3;
                    buf[i] += element.r * k; buf[i + 1] += element.g * k; buf[i + 2] += element.b * k;
                }
        }

        private static byte[] Compose(float[] buf, Color element, bool ultimate)
        {
            int n = Size * Size;
            // Exposure from the bright end of the effect, so a faint effect and a blinding one both fill the icon.
            var lum = new float[n];
            for (int i = 0; i < n; i++) lum[i] = buf[i * 3] * 0.3f + buf[i * 3 + 1] * 0.59f + buf[i * 3 + 2] * 0.11f;
            var sorted = lum.Where(l => l > 0.001f).OrderBy(l => l).ToList();
            float hi = sorted.Count > 0 ? sorted[Mathf.Clamp((int)(sorted.Count * 0.97f), 0, sorted.Count - 1)] : 1f;
            float exposure = 2.2f / Mathf.Max(0.05f, hi);
            // A flat field (a big glow covering the icon) would saturate to a white or single-colour square: expose it by its median instead.
            float med = sorted.Count > 0 ? sorted[sorted.Count / 2] : 1f;
            if (sorted.Count > n * 0.6f) exposure = Mathf.Min(exposure, 0.9f / Mathf.Max(0.05f, med));
            var glow = Blur(Blur(buf, 5), 5);
            var px = new Color32[n];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int i = y * Size + x;
                    float dx = (x + 0.5f) / Size - 0.5f, dy = (y + 0.5f) / Size - 0.5f; float r = Mathf.Sqrt(dx * dx + dy * dy);
                    var bg = Color.Lerp(new Color(0.075f, 0.068f, 0.082f), new Color(0.018f, 0.016f, 0.022f), Mathf.Clamp01(r * 1.7f));
                    bg += element * 0.05f * Mathf.Clamp01(1f - r * 2f);
                    var c = new Color(Tone(buf[i * 3] * exposure + glow[i * 3] * exposure * 0.45f),
                                      Tone(buf[i * 3 + 1] * exposure + glow[i * 3 + 1] * exposure * 0.45f),
                                      Tone(buf[i * 3 + 2] * exposure + glow[i * 3 + 2] * exposure * 0.45f));
                    c = new Color(1f - (1f - bg.r) * (1f - c.r), 1f - (1f - bg.g) * (1f - c.g), 1f - (1f - bg.b) * (1f - c.b));   // screen over the background
                    // Frame: a dark outer line, the element colour, a thin inner highlight; ultimates get a double line.
                    int edge = Mathf.Min(Mathf.Min(x, Size - 1 - x), Mathf.Min(y, Size - 1 - y));
                    if (edge == 0) c = new Color(0.01f, 0.01f, 0.01f);
                    else if (edge <= 3) c = Color.Lerp(element * 0.55f, element, (edge - 1) / 2f);
                    else if (edge == 4) c = Color.Lerp(c, Color.white, 0.25f) * 0.9f;
                    else if (ultimate && (edge == 7 || edge == 8)) c = Color.Lerp(c, new Color(1f, 0.85f, 0.4f), 0.75f);
                    else if (edge < 12) c *= 0.75f + 0.025f * edge;   // inner shade
                    px[i] = new Color32((byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f), (byte)(Mathf.Clamp01(c.b) * 255f), 255);
                }
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.SetPixels32(px); tex.Apply();
            var png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);
            return png;
        }

        private static float Tone(float v) => 1f - Mathf.Exp(-Mathf.Max(0f, v));

        private static float[] Blur(float[] src, int r)
        {
            var tmp = new float[src.Length]; var dst = new float[src.Length];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    for (int ch = 0; ch < 3; ch++)
                    {
                        float s = 0f; int c = 0;
                        for (int k = -r; k <= r; k++) { int xx = x + k; if (xx < 0 || xx >= Size) continue; s += src[(y * Size + xx) * 3 + ch]; c++; }
                        tmp[(y * Size + x) * 3 + ch] = s / c;
                    }
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    for (int ch = 0; ch < 3; ch++)
                    {
                        float s = 0f; int c = 0;
                        for (int k = -r; k <= r; k++) { int yy = y + k; if (yy < 0 || yy >= Size) continue; s += tmp[(yy * Size + x) * 3 + ch]; c++; }
                        dst[(y * Size + x) * 3 + ch] = s / c;
                    }
            return dst;
        }

        /// <summary>The frame colour of an element: fire orange, frost ice blue, lightning pale yellow, poison green, shadow violet, physical steel gold.</summary>
        public static Color ElementColour(DamageType? e)
        {
            switch (e)
            {
                case DamageType.Fire: return new Color(1f, 0.45f, 0.12f);
                case DamageType.Frost: return new Color(0.45f, 0.8f, 1f);
                case DamageType.Lightning: return new Color(1f, 0.93f, 0.45f);
                case DamageType.Poison: return new Color(0.45f, 0.95f, 0.3f);
                case DamageType.Shadow: return new Color(0.68f, 0.4f, 1f);
                default: return new Color(0.78f, 0.68f, 0.48f);
            }
        }

        // ------------------------------------------------------------ assets

        private static void ConfigureSprite(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.mipmapEnabled = false;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.alphaIsTransparency = false;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
        }

        /// <summary>The HUD's lookup asset. Its type lives in the UI assembly, which this editor assembly does not reference: made by name, filled through serialization.</summary>
        private static void WriteLookup(List<(string id, string path)> icons)
        {
            var type = Type.GetType("DungeonGen.Unity.UI.AbilityIcons, DungeonGen.Unity.UI");
            if (type == null) { Debug.LogError("Dungeon Gen icons: no DungeonGen.Unity.UI.AbilityIcons type; the HUD gets no icons."); return; }
            var asset = AssetDatabase.LoadAssetAtPath(LookupPath, type) as ScriptableObject;
            if (asset == null) { asset = ScriptableObject.CreateInstance(type); AssetDatabase.CreateAsset(asset, LookupPath); }
            var so = new SerializedObject(asset);
            var list = so.FindProperty("Entries");
            list.arraySize = icons.Count;
            for (int i = 0; i < icons.Count; i++)
            {
                var el = list.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("AbilityId").stringValue = icons[i].id;
                el.FindPropertyRelative("Icon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(icons[i].path);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static string Abs(string assetPath) => Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
