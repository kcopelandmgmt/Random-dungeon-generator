using System.Collections.Generic;
using DungeonGen.Core;
using DungeonGen.Core.Hud;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonGen.Unity.UI
{
    /// <summary>
    /// The uGUI minimap: a framed square in the bottom-right corner (clear of the round HUD's panels) showing the fog-of-war texture around the player,
    /// turned so the screen's up is the map's up (the camera is fixed at a 45 degree yaw), with the player's arrowhead, team mates, pings, the rooms'
    /// extract, boss, shrine, vault and spawn marks, hazards and shut doors drawn over it. M opens the same map as a large overlay, fitted to what
    /// the team has explored. Everything is read from <see cref="MiniMapHud"/>, which reads the client view; nothing unexplored is ever painted.
    /// </summary>
    internal sealed class MapMiniMapView : MonoBehaviour
    {
        /// <summary>The corner map's frame and its distance from the screen corner, in reference pixels (the UI scales from 1920x1080). Clear of the round HUD: its clock is top right, its abilities bottom centre, its loot panel on the right edge above this.</summary>
        public const float CornerSize = 270f, Margin = 24f;
        private static readonly Color MapBg = new Color(0.035f, 0.030f, 0.036f, 1f);

        private MiniMapHud _hud;
        private RectTransform _root, _corner, _overlay;
        private Surface _mini, _full;
        private Text _overlayTitle;
        private MapFog _fog;
        private GameHud _gameHud; private float _gameHudAt = -10f;
        private int _boundsRevision = -1; private Vector2 _bMin, _bMax;
        private bool _fullWas; private Vector2 _fullCentre; private float _fullPpc;

        public static GameObject Create(MiniMapHud hud, RectTransform canvas)
        {
            var rt = UiStyle.Node(canvas, "MiniMap");
            rt.Stretch();
            var view = rt.gameObject.AddComponent<MapMiniMapView>();
            view.Build(hud, rt);
            return rt.gameObject;
        }

        // ---------------- build ----------------

        private void Build(MiniMapHud hud, RectTransform root)
        {
            _hud = hud; _root = root;
            const float size = CornerSize, pad = 8f;

            // The corner map: panel, window, key hint.
            _corner = UiStyle.Panel(root, "Corner", UiStyle.PanelBg, true, false);
            _corner.Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-Margin, Margin), new Vector2(size, size));
            _mini = Surface.Make(_corner, "Mini", new Vector2(size - pad * 2f, size - pad * 2f), 16f, 24f, hud);
            _mini.Window.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, _mini.Size);
            var key = UiStyle.Panel(_corner, "KeyHint", UiStyle.PanelBg, true, false);
            key.Place(new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(2f, -2f), new Vector2(26f, 26f));
            var keyText = UiStyle.Label(key, "Key", "M", 17, UiStyle.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            keyText.rectTransform.Stretch();

            // The overlay: a dimmed screen, a big panel with a title, the map and a legend.
            _overlay = UiStyle.Node(root, "Overlay");
            _overlay.Stretch();
            var dim = UiStyle.Img(_overlay, "Dim", new Color(0f, 0f, 0f, 0.5f));
            dim.rectTransform.Stretch();
            var panel = UiStyle.Panel(_overlay, "Panel", UiStyle.PanelBg, true, false);
            const float pw = 1000f, ph = 720f;   // clear of the compass above and the ability bar below
            panel.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 95f), new Vector2(pw, ph));
            _overlayTitle = UiStyle.Label(panel, "Title", "MAP", 34, UiStyle.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            _overlayTitle.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -14f), new Vector2(500f, 46f));
            var close = UiStyle.Label(panel, "Close", "M  close", 20, UiStyle.TextDim, TextAnchor.MiddleRight);
            close.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-30f, -20f), new Vector2(300f, 34f));
            UiStyle.Rule(panel, "Rule", pw - 60f, -66f);
            _full = Surface.Make(panel, "Full", new Vector2(pw - 60f, ph - 66f - 88f), 24f, 36f, hud);
            _full.Window.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -78f), _full.Size);
            BuildLegend(panel, pw);
            _overlay.gameObject.SetActive(false);
        }

        private static void BuildLegend(RectTransform panel, float pw)
        {
            var row = UiStyle.Node(panel, "Legend");
            row.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(pw - 60f, 34f));
            var items = new (string letter, Color colour, string text)[]
            {
                ("E", MapIcons.Extract, "Extract"), ("B", MapIcons.Boss, "Boss"), ("F", MapIcons.FinalBoss, "Final boss"), ("+", MapIcons.Shrine, "Shrine"),
                ("V", MapIcons.Vault, "Vault"), ("S", MapIcons.Spawn, "Spawn"), ("", MapIcons.Mate, "Team"), ("", MapIcons.Self, "You"),
            };
            float x = 0f;
            foreach (var it in items)
            {
                var m = MapIcons.Mark.Make(row, "Legend-" + it.text, 22f);
                m.Set(Vector2.zero, it.text == "You" ? MapIcons.Arrow : MapIcons.Disc, it.colour, it.letter);
                m.Root.Place(new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x + 11f, 0f), new Vector2(22f, 22f));
                var t = UiStyle.Label(row, "Text-" + it.text, it.text, 19, UiStyle.TextDim, TextAnchor.MiddleLeft);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x + 30f, 0f), new Vector2(110f, 30f));
                x += 40f + it.text.Length * 10.5f + 14f;
            }
        }

        // ---------------- per frame ----------------

        private void LateUpdate()
        {
            if (_hud == null) return;
            _hud.Tick();
            if (_gameHud == null && Time.unscaledTime - _gameHudAt > 1f) { _gameHudAt = Time.unscaledTime; _gameHud = FindFirstObjectByType<GameHud>(); }
            bool covered = _gameHud != null && _gameHud.SummaryVisible;
            bool live = _hud.HasSelf && _hud.Team >= 0 && _hud.Model != null && _hud.Layout != null && !covered;
            // The view object stays active so it keeps ticking; its two screens show or hide.
            bool wantFull = live && _hud.FullMapOpen && !(_gameHud != null && _gameHud.Paused);
            Show(_corner, live && !wantFull); Show(_overlay, wantFull);
            if (!live) return;

            if (_fog == null || !ReferenceEquals(_fog.Layout, _hud.Layout)) MakeFog();
            _fog.Sync(_hud.ExploredRooms, _hud.Trail);
            if (wantFull) DrawFull(); else DrawMini();
            _fullWas = wantFull;
        }

        private static void Show(RectTransform rt, bool on) { if (rt.gameObject.activeSelf != on) rt.gameObject.SetActive(on); }

        private void MakeFog()
        {
            if (_fog != null) _fog.Destroy();
            _fog = new MapFog(_hud.Layout);
            _hud.FogTexture = _fog.Texture;
            _mini.SetTexture(_fog);
            _full.SetTexture(_fog);
        }

        private void OnDestroy() { if (_fog != null) _fog.Destroy(); }

        private void DrawMini()
        {
            var self = _hud.Self;
            _mini.Draw(_hud, self.Cell, _mini.Size.x / (MiniMapModel.ViewRadiusCells * 2f + 1f), pinEdges: true);
        }

        private void DrawFull()
        {
            if (_boundsRevision != _hud.Revision) { _boundsRevision = _hud.Revision; RecomputeBounds(); }
            float yaw = _hud.YawDegrees * Mathf.Deg2Rad, c = Mathf.Cos(yaw), s = Mathf.Sin(yaw);
            // Fit the explored box, as turned by the camera's yaw, into the window.
            Vector2 lo = new Vector2(float.MaxValue, float.MaxValue), hi = new Vector2(float.MinValue, float.MinValue);
            foreach (var corner in new[] { _bMin, new Vector2(_bMax.x, _bMin.y), _bMax, new Vector2(_bMin.x, _bMax.y) })
            {
                var r = new Vector2(corner.x * c - corner.y * s, corner.x * s + corner.y * c);
                lo = Vector2.Min(lo, r); hi = Vector2.Max(hi, r);
            }
            var size = hi - lo;
            float ppc = Mathf.Clamp(Mathf.Min(_full.Size.x / size.x, _full.Size.y / size.y), 3f, 18f);
            var rc = (lo + hi) * 0.5f;
            var centre = new Vector2(rc.x * c + rc.y * s, -rc.x * s + rc.y * c);   // back out of the turn
            if (!_fullWas) { _fullCentre = centre; _fullPpc = ppc; }
            float k = 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime);
            _fullCentre = Vector2.Lerp(_fullCentre, centre, k); _fullPpc = Mathf.Lerp(_fullPpc, ppc, k);
            _full.Draw(_hud, _fullCentre, _fullPpc, pinEdges: false);
            _overlayTitle.text = _hud.ExploredRooms.Count == 1 ? "MAP  <size=22><color=#A69C8A>1 room explored</color></size>" : $"MAP  <size=22><color=#A69C8A>{_hud.ExploredRooms.Count} rooms explored</color></size>";
        }

        /// <summary>The box (in cell coordinates, edges included) round everything the team has explored, never smaller than a screenful of cells.</summary>
        private void RecomputeBounds()
        {
            Vector2 lo = new Vector2(float.MaxValue, float.MaxValue), hi = new Vector2(float.MinValue, float.MinValue);
            void Add(float x0, float y0, float x1, float y1) { lo = Vector2.Min(lo, new Vector2(x0, y0)); hi = Vector2.Max(hi, new Vector2(x1, y1)); }
            if (_hud.Model != null) foreach (var r in _hud.Model.Rooms) Add(r.Bounds.X - 0.5f, r.Bounds.Y - 0.5f, r.Bounds.XMax - 0.5f, r.Bounds.YMax - 0.5f);
            foreach (var t in _hud.Trail) Add(t.X - 0.5f, t.Y - 0.5f, t.X + 0.5f, t.Y + 0.5f);
            if (lo.x > hi.x) { var p = _hud.Self.Cell; Add(p.x - 1f, p.y - 1f, p.x + 1f, p.y + 1f); }
            lo -= Vector2.one * 3f; hi += Vector2.one * 3f;
            var mid = (lo + hi) * 0.5f; var half = Vector2.Max((hi - lo) * 0.5f, Vector2.one * 14f);
            _bMin = mid - half; _bMax = mid + half;
        }

        // ---------------- one drawing surface: the corner map or the overlay ----------------

        private sealed class Surface
        {
            public RectTransform Window, Content, Markers, SelfRoot; public RawImage Map; public Vector2 Size;
            public MapIcons.Pool Static, Pings, Mates;
            public Text North; public Image SelfOutline, SelfBody;
            private float _ppc, _yaw; private Vector2 _centre; private float _markSize;

            public static Surface Make(Transform parent, string name, Vector2 size, float markSize, float selfSize, MiniMapHud hud)
            {
                var s = new Surface { Size = size, _markSize = markSize };
                s.Window = UiStyle.Node(parent, name + "Window");
                s.Window.sizeDelta = size;
                s.Window.gameObject.AddComponent<RectMask2D>();
                var bg = UiStyle.Img(s.Window, "Bg", MapBg);
                bg.rectTransform.Stretch();
                s.Content = UiStyle.Node(s.Window, "Map");
                s.Map = s.Content.gameObject.AddComponent<RawImage>();
                s.Map.raycastTarget = false; s.Map.color = Color.white;
                s.Content.anchorMin = s.Content.anchorMax = new Vector2(0.5f, 0.5f);
                var vig = UiStyle.Img(s.Window, "Vignette", new Color(1f, 1f, 1f, 0.7f), UiStyle.Vignette);
                vig.rectTransform.Stretch();
                s.Markers = UiStyle.Node(s.Window, "Markers");
                s.Markers.anchorMin = s.Markers.anchorMax = new Vector2(0.5f, 0.5f); s.Markers.sizeDelta = Vector2.zero;
                s.Static = new MapIcons.Pool(s.Markers, name + "-Static", markSize);
                s.Pings = new MapIcons.Pool(s.Markers, name + "-Ping", markSize);
                s.Mates = new MapIcons.Pool(s.Markers, name + "-Mate", markSize * 0.72f);
                s.SelfRoot = UiStyle.Node(s.Markers, "Map-Self");
                s.SelfRoot.sizeDelta = new Vector2(selfSize, selfSize);
                s.SelfOutline = UiStyle.Img(s.SelfRoot, "Outline", MapIcons.OutlineColour, MapIcons.Arrow);
                s.SelfOutline.rectTransform.Stretch(-3f, -3f, -3f, -3f);
                s.SelfBody = UiStyle.Img(s.SelfRoot, "Body", MapIcons.Self, MapIcons.Arrow);
                s.SelfBody.rectTransform.Stretch();
                s.North = UiStyle.Label(s.Window, "North", "N", Mathf.RoundToInt(markSize * 1.15f), UiStyle.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
                s.North.rectTransform.sizeDelta = new Vector2(30f, 30f);
                s.North.rectTransform.anchorMin = s.North.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                return s;
            }

            public void SetTexture(MapFog fog)
            {
                Map.texture = fog.Texture;
                Content.sizeDelta = new Vector2(fog.WidthPx, fog.HeightPx);
            }

            /// <summary>Screen offset from the window's centre of a point in cell coordinates: scaled, then turned by the camera's yaw.</summary>
            public Vector2 ToScreen(Vector2 cell)
            {
                var d = (cell - _centre) * _ppc;
                float r = _yaw * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
                return new Vector2(d.x * c - d.y * s, d.x * s + d.y * c);
            }

            public void Draw(MiniMapHud hud, Vector2 centre, float ppc, bool pinEdges)
            {
                _centre = centre; _ppc = ppc; _yaw = hud.YawDegrees;
                var model = hud.Model; var st = hud.State;
                float w = Map.texture != null ? Map.texture.width : 1f, h = Map.texture != null ? Map.texture.height : 1f;
                Content.pivot = new Vector2((centre.x + 0.5f) * MapFog.Scale / w, (centre.y + 0.5f) * MapFog.Scale / h);
                Content.anchoredPosition = Vector2.zero;
                Content.localScale = Vector3.one * (ppc / MapFog.Scale);
                Content.localRotation = Quaternion.Euler(0f, 0f, _yaw);
                float scale = _markSize / 16f;

                // Rooms' marks and the map's own points, under the moving things.
                Static.Begin();
                bool extractsOpen = st.ExtractsOpen, exitOpen = st.BossExitOpen;
                foreach (var r in model.Rooms)
                {
                    if (!MapIcons.RoomMark(r, extractsOpen, exitOpen, out string letter, out var colour)) continue;
                    var centreCell = new Vector2(r.Bounds.X + (r.Bounds.Width - 1) * 0.5f, r.Bounds.Y + (r.Bounds.Height - 1) * 0.5f);
                    Static.Next().Set(ToScreen(centreCell), MapIcons.Disc, colour, letter);
                }
                foreach (var m in model.Markers)
                {
                    Color col; float k = 0.42f;
                    switch (m.Kind)
                    {
                        case MiniMapMarkerKind.LockedDoor: col = MapIcons.Locked; break;
                        case MiniMapMarkerKind.ClosedDoor: col = MapIcons.ClosedDoor; break;
                        case MiniMapMarkerKind.Hazard: col = MapIcons.Hazard; break;
                        case MiniMapMarkerKind.DisarmedHazard: col = MapIcons.Disarmed; break;
                        case MiniMapMarkerKind.BossExit: col = MapIcons.Exit; k = 0.75f; break;
                        default: continue;   // the player, team mates and pings are drawn live below
                    }
                    Static.Next().Set(ToScreen(new Vector2(m.Cell.X, m.Cell.Y)), MapIcons.Diamond, col, "", 1f, k * scale);
                }
                Static.End();

                float half = Mathf.Min(Size.x, Size.y) * 0.5f - 11f;
                Pings.Begin();
                float pulse = 1f + 0.2f * Mathf.Sin(Time.unscaledTime * 7f);
                foreach (var p in hud.LivePings)
                {
                    var pos = ToScreen(new Vector2(p.Cell.X, p.Cell.Y)); bool pinned = false;
                    if (pinEdges) pos = Pin(pos, half, out pinned);
                    Pings.Next().Set(pos, MapIcons.Disc, MapIcons.Ping, "!", pinned ? 0.7f : 1f, pulse);
                }
                Pings.End();
                Mates.Begin();
                foreach (var m in hud.Teammates)
                {
                    var pos = ToScreen(m.Cell); bool pinned = false;
                    if (pinEdges) pos = Pin(pos, half, out pinned);
                    Mates.Next().Set(pos, MapIcons.Disc, MapIcons.Mate, "", (pinned || m.Downed ? 0.55f : 1f), pinned ? 0.8f : 1f);
                }
                Mates.End();

                var self = hud.Self;
                SelfRoot.anchoredPosition = ToScreen(self.Cell);
                float bearing = Mathf.Atan2(self.Facing.x, self.Facing.y) * Mathf.Rad2Deg;
                SelfRoot.localRotation = Quaternion.Euler(0f, 0f, _yaw - bearing);
                SelfBody.color = MapIcons.Self;

                // North sits on the window's rim, in the direction north has been turned to.
                var n = ToScreenDir(new Vector2(0f, 1f));
                var edge = n * (Mathf.Min(Size.x, Size.y) * 0.5f - 15f);
                North.rectTransform.anchoredPosition = edge;
            }

            private Vector2 ToScreenDir(Vector2 d)
            {
                float r = _yaw * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
                return new Vector2(d.x * c - d.y * s, d.x * s + d.y * c);
            }

            /// <summary>Keep a point inside the window: past the rim it slides to the rim along the line from the centre.</summary>
            private static Vector2 Pin(Vector2 pos, float half, out bool pinned)
            {
                float m = Mathf.Max(Mathf.Abs(pos.x), Mathf.Abs(pos.y));
                pinned = m > half;
                return pinned ? pos * (half / m) : pos;
            }
        }
    }
}
