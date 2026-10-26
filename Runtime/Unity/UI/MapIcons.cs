using DungeonGen.Core;
using DungeonGen.Core.Hud;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonGen.Unity.UI
{
    /// <summary>
    /// Shapes and colours the map and the compass share, drawn once at runtime (white sprites, tinted by the Image): an arrowhead for the player,
    /// a diamond, a ring. Colours are the HUD palette's, so a boss reads red and an open extract green on the strip and on the map alike.
    /// </summary>
    internal static class MapIcons
    {
        private static Sprite _arrow, _diamond, _ring, _disc;

        public static Sprite Arrow => _arrow != null ? _arrow : (_arrow = MakePolygon("map-arrow", 64, new[] { new Vector2(0.5f, 0.97f), new Vector2(0.92f, 0.06f), new Vector2(0.5f, 0.30f), new Vector2(0.08f, 0.06f) }));
        public static Sprite Diamond => _diamond != null ? _diamond : (_diamond = MakePolygon("map-diamond", 48, new[] { new Vector2(0.5f, 0.98f), new Vector2(0.98f, 0.5f), new Vector2(0.5f, 0.02f), new Vector2(0.02f, 0.5f) }));
        public static Sprite Disc => _disc != null ? _disc : (_disc = UiStyle.Circle);
        public static Sprite Ring => _ring != null ? _ring : (_ring = MakeRing("map-ring", 64, 0.78f));

        // ---- palette ----
        public static readonly Color Extract = new Color(0.50f, 0.85f, 0.42f), ExtractClosed = new Color(0.36f, 0.50f, 0.34f);
        public static readonly Color Boss = new Color(0.88f, 0.30f, 0.26f), BossDead = new Color(0.46f, 0.30f, 0.28f);
        public static readonly Color FinalBoss = new Color(0.72f, 0.42f, 0.95f), BossExit = new Color(0.60f, 0.95f, 0.50f);
        public static readonly Color Shrine = new Color(0.95f, 0.78f, 0.36f), Vault = new Color(0.95f, 0.58f, 0.18f), VaultOpen = new Color(0.55f, 0.43f, 0.24f);
        public static readonly Color Spawn = new Color(0.45f, 0.62f, 0.85f);
        public static readonly Color Mate = new Color(0.35f, 0.75f, 1f), Ping = new Color(1f, 0.86f, 0.25f);
        public static readonly Color Self = new Color(1f, 0.93f, 0.62f), Locked = new Color(1f, 0.6f, 0.1f), Exit = new Color(0.5f, 0.85f, 0.4f);
        public static readonly Color ClosedDoor = new Color(0.9f, 0.3f, 0.3f), Hazard = new Color(1f, 0.25f, 0.6f), Disarmed = new Color(0.5f, 0.5f, 0.5f);
        public static readonly Color OutlineColour = new Color(0.02f, 0.015f, 0.02f, 0.85f);

        /// <summary>The look of a room's marker: its letter and colour. Null letter: no marker.</summary>
        public static bool RoomMark(MiniMapRoom r, bool extractsOpen, bool bossExitOpen, out string letter, out Color colour)
        {
            switch (r.Type)
            {
                case RoomType.Extract: letter = "E"; colour = extractsOpen ? Extract : ExtractClosed; return true;
                case RoomType.Boss: letter = "B"; colour = r.BossDead ? BossDead : Boss; return true;
                case RoomType.FinalBoss: letter = bossExitOpen ? "X" : "F"; colour = bossExitOpen ? BossExit : FinalBoss; return true;
                case RoomType.Vault: letter = "V"; colour = r.VaultOpen ? VaultOpen : Vault; return true;
                case RoomType.Spawn: if (!r.IsTeamSpawn) break; letter = "S"; colour = Spawn; return true;
            }
            if (r.HasShrine) { letter = "+"; colour = Shrine; return true; }
            letter = null; colour = default; return false;
        }

        public static void CompassMark(CompassMarkerKind kind, bool dim, out string letter, out Color colour, out Sprite shape)
        {
            shape = Disc;
            switch (kind)
            {
                case CompassMarkerKind.Teammate: letter = ""; colour = Mate; break;
                case CompassMarkerKind.Ping: letter = "!"; colour = Ping; break;
                case CompassMarkerKind.Extract: letter = "E"; colour = dim ? ExtractClosed : Extract; break;
                case CompassMarkerKind.BossRoom: letter = "B"; colour = dim ? BossDead : Boss; shape = Diamond; break;
                case CompassMarkerKind.FinalBoss: letter = "F"; colour = FinalBoss; shape = Diamond; break;
                case CompassMarkerKind.BossExit: letter = "X"; colour = BossExit; break;
                case CompassMarkerKind.Shrine: letter = "+"; colour = Shrine; break;
                case CompassMarkerKind.Vault: letter = "V"; colour = dim ? VaultOpen : Vault; break;
                default: letter = "S"; colour = Spawn; break;
            }
        }

        // ---- a pooled marker: outline + body + letter ----

        internal sealed class Mark
        {
            public RectTransform Root; public Image Outline, Body; public Text Letter;
            public static Mark Make(Transform parent, string name, float size)
            {
                var m = new Mark();
                m.Root = UiStyle.Node(parent, name);
                m.Root.sizeDelta = new Vector2(size, size);
                m.Outline = UiStyle.Img(m.Root, "Outline", OutlineColour, Disc);
                m.Outline.rectTransform.Stretch(-1.5f, -1.5f, -1.5f, -1.5f);
                m.Body = UiStyle.Img(m.Root, "Body", Color.white, Disc);
                m.Body.rectTransform.Stretch();
                m.Letter = UiStyle.Label(m.Root, "Letter", "", Mathf.RoundToInt(size * 0.7f), new Color(0.05f, 0.04f, 0.04f), TextAnchor.MiddleCenter, FontStyle.Bold, false);
                m.Letter.rectTransform.Stretch(-4f, -4f, -4f, -4f);
                m.Letter.horizontalOverflow = HorizontalWrapMode.Overflow; m.Letter.verticalOverflow = VerticalWrapMode.Overflow;
                return m;
            }

            public void Set(Vector2 pos, Sprite shape, Color colour, string letter, float alpha = 1f, float scale = 1f)
            {
                Root.gameObject.SetActive(true);
                Root.anchoredPosition = pos; Root.localScale = new Vector3(scale, scale, 1f);
                Body.sprite = shape; Outline.sprite = shape;
                Body.color = new Color(colour.r, colour.g, colour.b, alpha);
                Outline.color = new Color(Outline.color.r, Outline.color.g, Outline.color.b, 0.85f * alpha);
                Letter.text = letter ?? "";
                float lum = 0.3f * colour.r + 0.59f * colour.g + 0.11f * colour.b;
                Letter.color = lum > 0.5f ? new Color(0.05f, 0.04f, 0.04f, alpha) : new Color(0.95f, 0.92f, 0.85f, alpha);   // dark on a bright badge, light on a dim one
            }

            public void Hide() { if (Root.gameObject.activeSelf) Root.gameObject.SetActive(false); }
        }

        /// <summary>A pool of marks under one parent; Begin(), Next() for each in use, End() hides the rest.</summary>
        internal sealed class Pool
        {
            private readonly Transform _parent; private readonly string _name; private readonly float _size;
            private readonly System.Collections.Generic.List<Mark> _marks = new System.Collections.Generic.List<Mark>();
            private int _used;
            public Pool(Transform parent, string name, float size) { _parent = parent; _name = name; _size = size; }
            public void Begin() { _used = 0; }
            public Mark Next()
            {
                if (_used == _marks.Count) _marks.Add(Mark.Make(_parent, _name + "-" + _marks.Count, _size));
                return _marks[_used++];
            }
            public void End() { for (int i = _used; i < _marks.Count; i++) _marks[i].Hide(); }
            public int Used => _used;
        }

        // ---- sprite generation ----

        private static Sprite MakePolygon(string name, int size, Vector2[] pts)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = name };
            var px = new Color32[size * size];
            const int n = 4;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int hit = 0;
                    for (int sy = 0; sy < n; sy++)
                        for (int sx = 0; sx < n; sx++)
                            if (Inside(pts, new Vector2((x + (sx + 0.5f) / n) / size, (y + (sy + 0.5f) / n) / size))) hit++;
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(255 * hit / (n * n)));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static bool Inside(Vector2[] p, Vector2 q)
        {
            bool inside = false;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                if (((p[i].y > q.y) != (p[j].y > q.y)) && (q.x < (p[j].x - p[i].x) * (q.y - p[i].y) / (p[j].y - p[i].y) + p[i].x)) inside = !inside;
            return inside;
        }

        private static Sprite MakeRing(string name, int size, float inner)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = name };
            var px = new Color32[size * size];
            float r = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                    float a = Mathf.Clamp01((1f - d) * r) * Mathf.Clamp01((d - inner) * r);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
