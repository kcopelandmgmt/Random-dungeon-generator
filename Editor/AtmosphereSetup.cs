using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DungeonGen.Unity.Editor
{
    /// <summary>
    /// Greybox step 50: the dungeon's look. Dark and lit instead of a flat greybox under a sky.
    /// <list type="bullet">
    /// <item>Scene: no skybox, the camera clears to near-black, very low cool ambient, a dim cold directional "moon" without shadows,
    /// linear distance fog (scaled to the zoom at runtime by <see cref="AtmosphereRig"/>).</item>
    /// <item>A global URP volume (<c>Assets/Atmosphere/AtmosphereProfile.asset</c>): ACES tonemapping, bloom for fire and effects,
    /// vignette, a grade toward warm torchlight on cold stone. The renderer's SSAO feature (already on PC_Renderer) stays.</item>
    /// <item>Generated assets under <c>Assets/Atmosphere/</c>: a procedural stone texture with its normal map, a solid wall block with
    /// a near-black top (walls read as dark masses from above), a pit that drops into the dark, a glowing spawn sigil, a cold corridor
    /// crystal light, and a brazier variant with a flickering warm light. They are put into the builder's slots.</item>
    /// <item>Runtime components on the Dungeon root: <see cref="AtmosphereRig"/> (player fill light, zoom-scaled fog) and
    /// <see cref="FogOfWar"/>.</item>
    /// </list>
    /// Everything is regenerated on each run (asset GUIDs are kept), so a rebuild is idempotent.
    /// </summary>
    public static class AtmosphereSetup
    {
        public const string Root = "Assets/Atmosphere";
        public const string ProfilePath = Root + "/AtmosphereProfile.asset";
        /// <summary>Lower than the 1.8 m arch model: at the 50° camera a tall wall on the camera side hid the player in a corridor.</summary>
        public const float WallHeight = 1.5f;

        public static readonly Color Ambient = new Color(0.3f, 0.31f, 0.4f);
        public static readonly Color ClearColour = new Color(0.006f, 0.007f, 0.012f);
        public static readonly Color MoonColour = new Color(0.55f, 0.64f, 0.9f);
        public const float MoonIntensity = 0.45f;
        public static readonly Color FireColour = new Color(1f, 0.56f, 0.24f);
        public const float FireIntensity = 18f, FireRange = 13f;
        public static readonly Color CorridorColour = new Color(0.5f, 0.68f, 1f);

        [GreyboxStep(50)]
        public static void Step(GreyboxContext ctx)
        {
            foreach (var d in new[] { Root, Root + "/Textures", Root + "/Materials", Root + "/Meshes", Root + "/Prefabs" }) EnsureFolder(d);

            var (stone, stoneNormal) = StoneTextures();
            var pitGradient = Texture(Root + "/Textures/pit_gradient.png", 4, 64, (x, y) => { float v = y / 63f; float k = Mathf.Pow(Mathf.Clamp01((v - 0.25f) / 0.75f), 2.2f); return new Color(k, k, k * 1.05f, 1f); }, false);
            var sigil = Texture(Root + "/Textures/spawn_sigil.png", 256, 256, SigilPixel, false, true);

            var wallSide = Lit("WallSide", stone, new Color(0.85f, 0.84f, 0.88f), 0.12f, stoneNormal);
            var wallTop = Lit("WallTop", stone, new Color(0.2f, 0.2f, 0.23f), 0.05f, stoneNormal);
            var pitWall = Lit("PitWall", pitGradient, new Color(0.5f, 0.5f, 0.55f), 0f);
            var pitFloor = Lit("PitFloor", null, Color.black, 0f);
            var sigilMat = Lit("SpawnSigil", sigil, new Color(0.08f, 0.25f, 0.3f), 0.2f, null, new Color(0.25f, 0.85f, 1f) * 2.6f, sigil, true);
            var crystalMat = Lit("CorridorCrystal", null, new Color(0.2f, 0.3f, 0.45f), 0.8f, null, CorridorColour * 3.2f);

            var wallMesh = SaveMesh(WallMesh(2f, WallHeight), "WallBlock");
            var pitMesh = SaveMesh(PitMesh(2f, 1.6f), "Pit");
            var quadMesh = SaveMesh(FloorQuad(), "FloorQuad");
            var crystalMesh = SaveMesh(Octahedron(0.16f, 0.3f), "Crystal");

            var wall = SavePrefab("WallBlock", go =>
            {
                AddMesh(go, wallMesh, new[] { wallSide, wallTop }, ShadowCastingMode.On);
            });
            var pit = SavePrefab("Pit", go => AddMesh(go, pitMesh, new[] { pitWall, pitFloor }, ShadowCastingMode.Off));
            var spawn = SavePrefab("SpawnSigil", go =>
            {
                var disc = new GameObject("Sigil");
                disc.transform.SetParent(go.transform, false);
                disc.transform.localPosition = Vector3.up * 0.04f;
                disc.transform.localScale = new Vector3(4.6f, 1f, 4.6f);
                AddMesh(disc, quadMesh, new[] { sigilMat }, ShadowCastingMode.Off);
                var l = AddLight(go, 1.6f, new Color(0.35f, 0.85f, 1f), 7f, 6f);
                var f = l.gameObject.AddComponent<TorchFlicker>(); f.Amount = 0.1f; f.Speed = 0.6f;
            });
            var corridor = SavePrefab("CorridorLight", go =>
            {
                var c = new GameObject("Crystal");
                c.transform.SetParent(go.transform, false);
                c.transform.localPosition = Vector3.up * 1.5f;
                AddMesh(c, crystalMesh, new[] { crystalMat }, ShadowCastingMode.Off);
                var l = AddLight(go, 1.6f, CorridorColour, 9f, 9f);
                var f = l.gameObject.AddComponent<TorchFlicker>(); f.Amount = 0.06f; f.Speed = 0.8f;
            });
            var brazier = BrazierVariant();

            var b = ctx.Builder;
            if (b != null)
            {
                b.WallPrefab = wall;
                b.PitPrefab = pit;
                b.SpawnMarkerPrefab = spawn;
                b.CorridorLightPrefab = corridor;
                if (brazier != null) b.RoomLightPrefab = brazier;
                EditorUtility.SetDirty(b);
            }

            SceneLook(ctx);

            if (ctx.Root != null)
            {
                var rig = ctx.Root.GetComponent<AtmosphereRig>();
                if (rig == null) rig = ctx.Root.AddComponent<AtmosphereRig>();   // not ??: Unity's fake-null components defeat it in the editor
                rig.World = ctx.World;
                var fog = ctx.Root.GetComponent<FogOfWar>();
                if (fog == null) fog = ctx.Root.AddComponent<FogOfWar>();
                fog.Builder = ctx.Builder; fog.World = ctx.World; fog.Bootstrap = ctx.Bootstrap;
                EditorUtility.SetDirty(ctx.Root);
            }
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------ scene

        private static void SceneLook(GreyboxContext ctx)
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Ambient;
            var sh = new SphericalHarmonicsL2(); sh.AddAmbientLight(Ambient); RenderSettings.ambientProbe = sh;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null;
            RenderSettings.reflectionIntensity = 0.25f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.006f, 0.007f, 0.012f);
            RenderSettings.fogStartDistance = 20f; RenderSettings.fogEndDistance = 32f;

            foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (l.type != LightType.Directional) continue;
                l.color = MoonColour; l.intensity = MoonIntensity; l.shadows = LightShadows.None;
                l.transform.rotation = Quaternion.Euler(62f, -25f, 0f);
                EditorUtility.SetDirty(l);
            }

            var cam = ctx.Camera != null && ctx.Camera.Camera != null ? ctx.Camera.Camera : Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = ClearColour;
                cam.allowHDR = true;
                var data = cam.GetComponent(Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime"));
                if (data != null)
                {
                    var p = data.GetType().GetProperty("renderPostProcessing"); if (p != null) p.SetValue(data, true);
                    EditorUtility.SetDirty(data);
                }
                EditorUtility.SetDirty(cam);
            }

            var profile = BuildProfile();
            if (profile != null && ctx.Root != null)
            {
                var volType = Type.GetType("UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core.Runtime");
                var t = ctx.Root.transform.Find("Atmosphere Volume");
                var go = t != null ? t.gameObject : new GameObject("Atmosphere Volume");
                go.transform.SetParent(ctx.Root.transform, false);
                var vol = go.GetComponent(volType);
                if (vol == null) vol = go.AddComponent(volType);
                volType.GetField("isGlobal")?.SetValue(vol, true);
                volType.GetProperty("isGlobal")?.SetValue(vol, true);
                volType.GetProperty("priority")?.SetValue(vol, 50f);
                volType.GetField("priority")?.SetValue(vol, 50f);
                volType.GetProperty("sharedProfile")?.SetValue(vol, profile);
                volType.GetField("sharedProfile")?.SetValue(vol, profile);
                EditorUtility.SetDirty(go);
            }
        }

        /// <summary>The volume profile, rebuilt from scratch each run (URP types by reflection: the editor assembly does not reference URP).</summary>
        private static ScriptableObject BuildProfile()
        {
            var profileType = Type.GetType("UnityEngine.Rendering.VolumeProfile, Unity.RenderPipelines.Core.Runtime");
            if (profileType == null) { Debug.LogWarning("Dungeon Gen atmosphere: no URP VolumeProfile type; no post-processing."); return null; }
            var profile = AssetDatabase.LoadAssetAtPath(ProfilePath, profileType) as ScriptableObject;
            if (profile == null) { profile = ScriptableObject.CreateInstance(profileType); AssetDatabase.CreateAsset(profile, ProfilePath); }
            // Drop the old overrides (sub-assets) and add fresh ones.
            var components = (System.Collections.IList)profileType.GetField("components").GetValue(profile);
            foreach (var c in components) if (c is UnityEngine.Object o) UnityEngine.Object.DestroyImmediate(o, true);
            components.Clear();

            var add = profileType.GetMethod("Add", new[] { typeof(Type), typeof(bool) });
            object Comp(string name)
            {
                var t = Type.GetType($"UnityEngine.Rendering.Universal.{name}, Unity.RenderPipelines.Universal.Runtime");
                if (t == null) { Debug.LogWarning("Dungeon Gen atmosphere: no URP override " + name); return null; }
                var c = add.Invoke(profile, new object[] { t, false });
                ((UnityEngine.Object)c).name = name;
                AssetDatabase.AddObjectToAsset((UnityEngine.Object)c, profile);
                return c;
            }
            var tone = Comp("Tonemapping"); Set(tone, "mode", 2);   // ACES
            var bloom = Comp("Bloom"); Set(bloom, "threshold", 0.95f); Set(bloom, "intensity", 0.75f); Set(bloom, "scatter", 0.68f); Set(bloom, "tint", new Color(1f, 0.92f, 0.82f));
            var vig = Comp("Vignette"); Set(vig, "intensity", 0.26f); Set(vig, "smoothness", 0.5f); Set(vig, "color", Color.black);
            var adj = Comp("ColorAdjustments"); Set(adj, "postExposure", 1.15f); Set(adj, "contrast", 6f); Set(adj, "saturation", -4f); Set(adj, "colorFilter", new Color(1f, 0.97f, 0.94f));
            var split = Comp("SplitToning"); Set(split, "shadows", new Color(0.46f, 0.49f, 0.57f)); Set(split, "highlights", new Color(0.57f, 0.51f, 0.44f)); Set(split, "balance", 0f);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static void Set(object component, string field, object value)
        {
            if (component == null) return;
            var f = component.GetType().GetField(field);
            var param = f?.GetValue(component);
            if (param == null) { Debug.LogWarning($"Dungeon Gen atmosphere: no parameter {field} on {component.GetType().Name}"); return; }
            var valueProp = param.GetType().GetProperty("value");
            var target = valueProp.PropertyType;
            object v = target.IsEnum ? Enum.ToObject(target, value) : Convert.ChangeType(value, target);
            valueProp.SetValue(param, v);
            param.GetType().GetProperty("overrideState").SetValue(param, true);
        }

        // ------------------------------------------------------------ prefabs

        private static GameObject SavePrefab(string name, Action<GameObject> build)
        {
            var go = new GameObject(name);
            try
            {
                build(go);
                return PrefabUtility.SaveAsPrefabAsset(go, $"{Root}/Prefabs/{name}.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        private static void AddMesh(GameObject go, Mesh mesh, Material[] mats, ShadowCastingMode shadows)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats; r.shadowCastingMode = shadows; r.receiveShadows = true;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private static Light AddLight(GameObject root, float height, Color colour, float range, float intensity)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = Vector3.up * height;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = colour; l.range = range; l.intensity = intensity; l.shadows = LightShadows.None;
            return l;
        }

        /// <summary>The generated brazier with a warmer, stronger, flickering light, saved as a variant so a model re-import carries over.</summary>
        private static GameObject BrazierVariant()
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(GeneratedModelImporter.PrefabPath("brazier"));
            GameObject inst = src != null ? (GameObject)PrefabUtility.InstantiatePrefab(src) : new GameObject("brazier");
            try
            {
                var light = inst.GetComponentInChildren<Light>(true);
                if (light == null) light = AddLight(inst, 1.35f, FireColour, FireRange, FireIntensity);
                light.color = FireColour; light.intensity = FireIntensity; light.range = FireRange; light.shadows = LightShadows.None;
                if (light.GetComponent<TorchFlicker>() == null) light.gameObject.AddComponent<TorchFlicker>();
                return PrefabUtility.SaveAsPrefabAsset(inst, Root + "/Prefabs/Brazier.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(inst); }
        }

        // ------------------------------------------------------------ meshes

        private static Mesh SaveMesh(Mesh m, string name)
        {
            var path = $"{Root}/Meshes/{name}.asset";
            m.name = name;
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(m, path); return m; }
            EditorUtility.CopySerialized(m, existing);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        /// <summary>A solid block, feet at 0, centred on the cell: four sides (sub-mesh 0, stone tiled one texture per 2 m) and the top (sub-mesh 1). No bottom.</summary>
        private static Mesh WallMesh(float size, float height)
        {
            float h = size * 0.5f;
            var mb = new MeshBuild();
            // sides: +X, -X, +Z, -Z
            mb.Quad(0, new Vector3(h, 0, -h), new Vector3(h, 0, h), new Vector3(h, height, h), new Vector3(h, height, -h), Vector3.right, size, height);
            mb.Quad(0, new Vector3(-h, 0, h), new Vector3(-h, 0, -h), new Vector3(-h, height, -h), new Vector3(-h, height, h), Vector3.left, size, height);
            mb.Quad(0, new Vector3(h, 0, h), new Vector3(-h, 0, h), new Vector3(-h, height, h), new Vector3(h, height, h), Vector3.forward, size, height);
            mb.Quad(0, new Vector3(-h, 0, -h), new Vector3(h, 0, -h), new Vector3(h, height, -h), new Vector3(-h, height, -h), Vector3.back, size, height);
            mb.Quad(1, new Vector3(-h, height, -h), new Vector3(h, height, -h), new Vector3(h, height, h), new Vector3(-h, height, h), Vector3.up, size, size);
            return mb.Build(2);
        }

        /// <summary>A square hole one cell wide: four inner walls going down (sub-mesh 0, gradient to black) and a black bottom (sub-mesh 1).</summary>
        private static Mesh PitMesh(float size, float depth)
        {
            float h = size * 0.5f;
            var mb = new MeshBuild();
            // inner walls face the centre; v runs 0 at the bottom to 1 at the rim
            mb.Quad(0, new Vector3(h, -depth, h), new Vector3(h, -depth, -h), new Vector3(h, 0, -h), new Vector3(h, 0, h), Vector3.left, 1f, 1f, true);
            mb.Quad(0, new Vector3(-h, -depth, -h), new Vector3(-h, -depth, h), new Vector3(-h, 0, h), new Vector3(-h, 0, -h), Vector3.right, 1f, 1f, true);
            mb.Quad(0, new Vector3(-h, -depth, h), new Vector3(h, -depth, h), new Vector3(h, 0, h), new Vector3(-h, 0, h), Vector3.back, 1f, 1f, true);
            mb.Quad(0, new Vector3(h, -depth, -h), new Vector3(-h, -depth, -h), new Vector3(-h, 0, -h), new Vector3(h, 0, -h), Vector3.forward, 1f, 1f, true);
            mb.Quad(1, new Vector3(-h, -depth, -h), new Vector3(-h, -depth, h), new Vector3(h, -depth, h), new Vector3(h, -depth, -h), Vector3.up, 1f, 1f, true);
            return mb.Build(2);
        }

        /// <summary>A 1×1 quad in XZ facing up, UV 0..1.</summary>
        private static Mesh FloorQuad()
        {
            var mb = new MeshBuild();
            mb.Quad(0, new Vector3(-0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(0.5f, 0, -0.5f), Vector3.up, 1f, 1f, true);
            return mb.Build(1);
        }

        private static Mesh Octahedron(float r, float h)
        {
            var top = new Vector3(0, h, 0); var bot = new Vector3(0, -h, 0);
            var ring = new[] { new Vector3(r, 0, 0), new Vector3(0, 0, r), new Vector3(-r, 0, 0), new Vector3(0, 0, -r) };
            var v = new System.Collections.Generic.List<Vector3>(); var t = new System.Collections.Generic.List<int>();
            for (int i = 0; i < 4; i++)
            {
                var a = ring[i]; var b = ring[(i + 1) % 4];
                int k = v.Count; v.Add(top); v.Add(b); v.Add(a); t.Add(k); t.Add(k + 1); t.Add(k + 2);
                k = v.Count; v.Add(bot); v.Add(a); v.Add(b); t.Add(k); t.Add(k + 1); t.Add(k + 2);
            }
            var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        private sealed class MeshBuild
        {
            private readonly System.Collections.Generic.List<Vector3> _v = new System.Collections.Generic.List<Vector3>();
            private readonly System.Collections.Generic.List<Vector3> _n = new System.Collections.Generic.List<Vector3>();
            private readonly System.Collections.Generic.List<Vector2> _uv = new System.Collections.Generic.List<Vector2>();
            private readonly System.Collections.Generic.List<int>[] _t = { new System.Collections.Generic.List<int>(), new System.Collections.Generic.List<int>() };

            /// <summary>a,b,c,d go round the quad; the winding is chosen so the face looks along <paramref name="normal"/>.
            /// UVs: width/height in metres, one texture per 2 m, unless <paramref name="unit"/> (0..1).</summary>
            public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, float w, float hgt, bool unit = false)
            {
                int k = _v.Count;
                _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
                for (int i = 0; i < 4; i++) _n.Add(normal);
                float u = unit ? 1f : w / 2f, v = unit ? 1f : hgt / 2f;
                _uv.Add(new Vector2(0, 0)); _uv.Add(new Vector2(u, 0)); _uv.Add(new Vector2(u, v)); _uv.Add(new Vector2(0, v));
                // Pick the winding whose geometric normal agrees with the requested one.
                var geo = Vector3.Cross(b - a, c - a);
                // Unity's front face has normal Cross(b - a, c - a) (what RecalculateNormals computes).
                if (Vector3.Dot(geo, normal) > 0f) { _t[sub].AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3 }); }
                else { _t[sub].AddRange(new[] { k, k + 2, k + 1, k, k + 3, k + 2 }); }
            }

            public Mesh Build(int subs)
            {
                var m = new Mesh();
                m.SetVertices(_v); m.SetNormals(_n); m.SetUVs(0, _uv);
                m.subMeshCount = subs;
                for (int i = 0; i < subs; i++) m.SetTriangles(_t[i], i);
                m.RecalculateTangents(); m.RecalculateBounds();
                return m;
            }
        }

        // ------------------------------------------------------------ materials and textures

        private static Material Lit(string name, Texture2D albedo, Color tint, float smooth, Texture2D normal = null, Color? emission = null, Texture2D emissionMap = null, bool alphaClip = false)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(shader) { name = name };
            if (albedo != null) m.SetTexture("_BaseMap", albedo);
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", 0f);
            if (normal != null) { m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                if (emissionMap != null) m.SetTexture("_EmissionMap", emissionMap);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            if (alphaClip) { m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.3f); m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = (int)RenderQueue.AlphaTest; }
            var path = $"{Root}/Materials/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null) { AssetDatabase.CreateAsset(m, path); return m; }
            existing.shader = shader;
            existing.CopyPropertiesFromMaterial(m);
            existing.shaderKeywords = m.shaderKeywords;
            existing.renderQueue = m.renderQueue;
            existing.globalIlluminationFlags = m.globalIlluminationFlags;
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(m);
            return existing;
        }

        /// <summary>Tileable irregular stone (wrapped Voronoi cells with dark mortar) and the normal map from its height.</summary>
        private static (Texture2D albedo, Texture2D normal) StoneTextures()
        {
            const int N = 256, Cells = 26;
            var rng = new System.Random(4242);
            var pts = new Vector2[Cells]; var tone = new float[Cells];
            for (int i = 0; i < Cells; i++) { pts[i] = new Vector2((float)rng.NextDouble() * N, (float)rng.NextDouble() * N); tone[i] = 0.62f + 0.38f * (float)rng.NextDouble(); }
            var height = new float[N, N]; var col = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d1 = float.MaxValue, d2 = float.MaxValue; int c1 = 0;
                    for (int i = 0; i < Cells; i++)
                    {
                        float dx = Mathf.Abs(x - pts[i].x); dx = Mathf.Min(dx, N - dx);
                        float dy = Mathf.Abs(y - pts[i].y); dy = Mathf.Min(dy, N - dy);
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (d < d1) { d2 = d1; d1 = d; c1 = i; } else if (d < d2) d2 = d;
                    }
                    float edge = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d2 - d1) / 5f));
                    float grain = Mathf.PerlinNoise(x * 0.11f, y * 0.11f) * 0.5f + Mathf.PerlinNoise(x * 0.37f + 9f, y * 0.37f) * 0.5f;
                    float dome = 1f - Mathf.Clamp01(d1 / 40f) * 0.25f;
                    height[x, y] = edge * (0.75f + 0.25f * dome) + grain * 0.08f;
                    float v = tone[c1] * (0.86f + 0.14f * grain) * Mathf.Lerp(0.22f, 1f, edge);
                    col[y * N + x] = new Color(0.5f * v, 0.51f * v, 0.56f * v, 1f);
                }
            var albedo = Write(Root + "/Textures/stone_albedo.png", N, N, col, false);
            var nrm = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float l = height[(x + N - 1) % N, y], r = height[(x + 1) % N, y], dn = height[x, (y + N - 1) % N], up = height[x, (y + 1) % N];
                    var n = new Vector3((l - r) * 3f, (dn - up) * 3f, 1f).normalized;
                    nrm[y * N + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            var normal = Write(Root + "/Textures/stone_normal.png", N, N, nrm, true);
            return (albedo, normal);
        }

        /// <summary>A rune circle: two rings, twelve ticks between them, a six-pointed star inside. White on transparent.</summary>
        private static Color SigilPixel(int x, int y)
        {
            var p = new Vector2((x + 0.5f) / 128f - 1f, (y + 0.5f) / 128f - 1f);
            float r = p.magnitude, a = Mathf.Atan2(p.y, p.x);
            float k = 0f;
            k = Mathf.Max(k, Band(r, 0.93f, 0.025f));
            k = Mathf.Max(k, Band(r, 0.66f, 0.018f));
            k = Mathf.Max(k, Band(r, 0.2f, 0.015f));
            if (r > 0.71f && r < 0.88f)
            {
                float seg = Mathf.Repeat(a / (Mathf.PI * 2f) * 12f, 1f);
                float tick = Mathf.Abs(seg - 0.5f) < 0.06f ? 1f : 0f;
                float rune = (Mathf.Abs(seg - 0.25f) < 0.025f || Mathf.Abs(seg - 0.75f) < 0.025f) && r > 0.76f && r < 0.83f ? 0.7f : 0f;
                k = Mathf.Max(k, Mathf.Max(tick, rune));
            }
            for (int t = 0; t < 2; t++)
                for (int i = 0; i < 3; i++)
                {
                    float a0 = Mathf.PI / 2f + t * Mathf.PI / 3f + i * Mathf.PI * 2f / 3f, a1 = a0 + Mathf.PI * 2f / 3f;
                    var s0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * 0.64f; var s1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * 0.64f;
                    k = Mathf.Max(k, 1f - Mathf.Clamp01((SegDist(p, s0, s1) - 0.008f) / 0.012f));
                }
            float glow = Mathf.Clamp01(1f - r) * 0.18f;
            k = Mathf.Clamp01(Mathf.Max(k, glow));
            if (r > 1f) k = 0f;
            return new Color(1f, 1f, 1f, k);
        }

        private static float Band(float r, float at, float half) => 1f - Mathf.Clamp01((Mathf.Abs(r - at) - half) / 0.012f);

        private static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude;
        }

        private static Texture2D Texture(string path, int w, int h, Func<int, int, Color> px, bool normalMap, bool alpha = false)
        {
            var col = new Color[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) col[y * w + x] = px(x, y);
            return Write(path, w, h, col, normalMap, alpha);
        }

        private static Texture2D Write(string path, int w, int h, Color[] pixels, bool normalMap, bool alpha = false)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(pixels); tex.Apply();
            File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, path), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null)
            {
                imp.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
                imp.sRGBTexture = !normalMap;
                imp.wrapMode = path.Contains("sigil") || path.Contains("gradient") ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                imp.alphaIsTransparency = alpha;
                imp.mipmapEnabled = true;
                imp.anisoLevel = 4;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
