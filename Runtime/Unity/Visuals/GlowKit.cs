using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// Shared pieces for the cheap light effects (loot beams, chest glows, extraction beacons and channel rings): one soft-edged
    /// alpha material on the always-included Sprites/Default shader (so a player build keeps it), three small generated textures
    /// (a beam that fades toward its tip, a round glow, a thin ring) and two meshes (two crossed vertical quads, a flat quad).
    /// Colour and pulse go through a MaterialPropertyBlock, so every effect shares the one material. Nothing here is lit and nothing
    /// casts or receives a shadow.
    /// </summary>
    internal static class GlowKit
    {
        private static Material _beamMat, _discMat, _ringMat;
        private static Texture2D _beamTex, _discTex, _ringTex;
        private static Mesh _crossed, _flat;
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public static Material Beam => _beamMat != null ? _beamMat : (_beamMat = Make("GlowBeam", BeamTex));
        public static Material Disc => _discMat != null ? _discMat : (_discMat = Make("GlowDisc", DiscTex));
        public static Material Ring => _ringMat != null ? _ringMat : (_ringMat = Make("GlowRing", RingTex));

        private static Material Make(string name, Texture2D tex)
        {
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var m = new Material(shader) { name = name, mainTexture = tex, hideFlags = HideFlags.HideAndDontSave };
            return m;
        }

        /// <summary>Soft horizontal edges, bright at the foot, fading to nothing at the tip. 32 wide, 128 tall.</summary>
        private static Texture2D BeamTex => _beamTex != null ? _beamTex : (_beamTex = Bake("GlowBeamTex", 32, 128, (u, v) =>
        {
            float x = (u - 0.5f) * 2f;
            float edge = Mathf.Exp(-x * x * 3.2f);
            float core = Mathf.Exp(-x * x * 22f);
            float tip = Mathf.Pow(Mathf.Clamp01(1f - v), 1.25f);
            float foot = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(v / 0.05f));
            float a = (edge * 0.55f + core * 0.6f) * tip * foot;
            return new Color(1f, 1f, 1f, Mathf.Clamp01(a));
        }));

        /// <summary>A round, soft pool of light: 64x64.</summary>
        private static Texture2D DiscTex => _discTex != null ? _discTex : (_discTex = Bake("GlowDiscTex", 64, 64, (u, v) =>
        {
            float dx = (u - 0.5f) * 2f, dy = (v - 0.5f) * 2f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float a = Mathf.Pow(Mathf.Clamp01(1f - r), 1.6f);
            a += 0.35f * Mathf.Exp(-r * r * 14f);
            return new Color(1f, 1f, 1f, Mathf.Clamp01(a));
        }));

        /// <summary>A thin glowing ring at 80% of the radius: 64x64.</summary>
        private static Texture2D RingTex => _ringTex != null ? _ringTex : (_ringTex = Bake("GlowRingTex", 64, 64, (u, v) =>
        {
            float dx = (u - 0.5f) * 2f, dy = (v - 0.5f) * 2f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float d = (r - 0.8f) / 0.11f;
            float a = Mathf.Exp(-d * d) * Mathf.Clamp01((1f - r) * 12f + 0.15f);
            return new Color(1f, 1f, 1f, Mathf.Clamp01(a));
        }));

        private static Texture2D Bake(string name, int w, int h, System.Func<float, float, Color> pixel)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = pixel((x + 0.5f) / w, (y + 0.5f) / h);
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>Two vertical quads crossed at right angles, 1 m wide and 1 m tall, standing on y = 0 (scale it to the beam's size).</summary>
        public static Mesh Crossed
        {
            get
            {
                if (_crossed != null) return _crossed;
                var v = new[]
                {
                    new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(-0.5f, 1f, 0f), new Vector3(0.5f, 1f, 0f),
                    new Vector3(0f, 0f, -0.5f), new Vector3(0f, 0f, 0.5f), new Vector3(0f, 1f, -0.5f), new Vector3(0f, 1f, 0.5f),
                };
                var uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
                var t = new[] { 0, 2, 1, 1, 2, 3, 4, 6, 5, 5, 6, 7 };
                _crossed = new Mesh { name = "GlowCrossed", vertices = v, uv = uv, triangles = t, hideFlags = HideFlags.HideAndDontSave };
                _crossed.RecalculateBounds();
                return _crossed;
            }
        }

        /// <summary>A 1 m square lying flat on y = 0, facing up, centred.</summary>
        public static Mesh Flat
        {
            get
            {
                if (_flat != null) return _flat;
                var v = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) };
                var uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
                _flat = new Mesh { name = "GlowFlat", vertices = v, uv = uv, triangles = new[] { 0, 1, 2, 0, 2, 3 }, hideFlags = HideFlags.HideAndDontSave };
                _flat.RecalculateBounds();
                return _flat;
            }
        }

        /// <summary>A mesh child on <paramref name="parent"/> with a glow material, no shadows, no probes.</summary>
        public static MeshRenderer Visual(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return r;
        }

        /// <summary>Tint a glow renderer (alpha is the strength). Reuses <paramref name="block"/>.</summary>
        public static void Tint(Renderer r, MaterialPropertyBlock block, Color c)
        {
            block.SetColor(ColorId, c);
            r.SetPropertyBlock(block);
        }
    }
}
