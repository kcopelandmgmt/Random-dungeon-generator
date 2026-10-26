using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if DUNGEONGEN_INPUTSYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace DungeonGen.Unity.UI
{
    /// <summary>
    /// One look for every screen: dark translucent panels with a thin warm-gold frame, parchment text, gold accents, sized
    /// for a 1920x1080 reference canvas. Everything is built from code with uGUI and the legacy Text component (the project
    /// has no TextMesh Pro resources imported); sprites are generated once at runtime, so there are no UI assets to lose.
    /// </summary>
    public static class UiStyle
    {
        public const float RefWidth = 1920f, RefHeight = 1080f;
        public const int UiLayer = 5;

        // ---- palette ----
        public static readonly Color Backdrop = new Color(0.030f, 0.026f, 0.034f, 1f);
        public static readonly Color PanelBg = new Color(0.060f, 0.052f, 0.060f, 0.94f);
        public static readonly Color PanelBgLight = new Color(0.105f, 0.090f, 0.095f, 0.92f);
        public static readonly Color Gold = new Color(0.878f, 0.690f, 0.341f, 1f);
        public static readonly Color GoldDim = new Color(0.560f, 0.435f, 0.220f, 1f);
        public static readonly Color GoldFaint = new Color(0.560f, 0.435f, 0.220f, 0.45f);
        public static readonly Color TextMain = new Color(0.930f, 0.885f, 0.790f, 1f);
        public static readonly Color TextDim = new Color(0.650f, 0.610f, 0.540f, 1f);
        public static readonly Color TextFaint = new Color(0.450f, 0.420f, 0.380f, 1f);
        public static readonly Color Health = new Color(0.690f, 0.120f, 0.110f, 1f);
        public static readonly Color HealthLow = new Color(0.900f, 0.200f, 0.150f, 1f);
        public static readonly Color Resource = new Color(0.220f, 0.470f, 0.860f, 1f);
        public static readonly Color Channel = new Color(0.900f, 0.760f, 0.360f, 1f);
        public static readonly Color Good = new Color(0.500f, 0.780f, 0.380f, 1f);
        public static readonly Color Bad = new Color(0.880f, 0.330f, 0.270f, 1f);
        public static readonly Color ButtonBg = new Color(0.120f, 0.100f, 0.100f, 0.95f);
        public static readonly Color ButtonHover = new Color(0.250f, 0.195f, 0.120f, 1f);
        public static readonly Color ButtonPressed = new Color(0.360f, 0.280f, 0.150f, 1f);
        /// <summary>The inset of a bar: lighter than the panels around it so an empty bar still reads as a bar.</summary>
        public static readonly Color BarTrack = new Color(0.150f, 0.130f, 0.125f, 1f);
        public static readonly Color Selected = new Color(0.300f, 0.230f, 0.120f, 0.95f);
        /// <summary>Secondary text on a brown or dark card: lighter than <see cref="TextDim"/> so it keeps contrast on <see cref="Selected"/>.</summary>
        public static readonly Color TextSoft = new Color(0.840f, 0.795f, 0.710f, 1f);
        // A disabled button: dimmed text, no gold border, a darker face (see UiButtonState).
        public static readonly Color TextDisabled = new Color(0.400f, 0.375f, 0.345f, 1f);
        public static readonly Color FrameDisabled = new Color(0.300f, 0.270f, 0.240f, 0.45f);
        public static readonly Color ButtonDisabled = new Color(0.075f, 0.068f, 0.068f, 0.80f);

        /// <summary>
        /// The type scale at the 1920x1080 reference. Every screen draws from these so no two screens pick their own size:
        /// nothing is under <see cref="Small"/> except the HUD's slot captions (<see cref="Caption"/>).
        /// </summary>
        public static class Size
        {
            public const int Caption = 16;   // HUD slot captions only
            public const int Small = 18;     // notes, hints, secondary lines
            public const int Body = 20;      // running text, lists
            public const int Label = 24;     // row labels, buttons
            public const int Heading = 30;   // panel headings, big buttons
            public const int Title = 40;     // screen titles, the clock
            public const int Hero = 56;      // banners (DOWNED, YOU DIED)
            public const int Display = 96;   // the game's name on the main menu
        }

        private static Font _font;
        public static Font Font => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        private static Sprite _white, _rounded, _frame, _vignette, _circle, _glow, _barGradient;
        /// <summary>A plain sprite (filled images need one to fill).</summary>
        public static Sprite White => _white != null ? _white : (_white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f));
        /// <summary>Rounded rectangle, 9-sliced.</summary>
        public static Sprite Rounded => _rounded != null ? _rounded : (_rounded = MakeRounded(32, 7, false));
        /// <summary>Just the rounded outline (2 px), 9-sliced, for frames.</summary>
        public static Sprite Frame => _frame != null ? _frame : (_frame = MakeRounded(32, 7, true));
        /// <summary>A soft circle.</summary>
        public static Sprite Circle => _circle != null ? _circle : (_circle = MakeCircle(64));
        /// <summary>A radial darkening for full-screen backdrops.</summary>
        /// <summary>A soft radial glow (white centre fading to nothing).</summary>
        public static Sprite Glow => _glow != null ? _glow : (_glow = MakeGlow(128));
        /// <summary>A vertical shading, bright at the top and darker at the bottom (white tinted by the image colour): the body of a bar.</summary>
        public static Sprite BarGradient => _barGradient != null ? _barGradient : (_barGradient = MakeBarGradient(32));
        public static Sprite Vignette => _vignette != null ? _vignette : (_vignette = MakeVignette(128));

        // ---------------- building blocks ----------------

        public static RectTransform Node(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = UiLayer };
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>Anchor at one point of the parent (0..1 each), with a pivot, an offset and a size in reference pixels.</summary>
        public static RectTransform Place(this RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot; rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(this RectTransform rt, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom); rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static Image Img(Transform parent, string name, Color color, Sprite sprite = null)
        {
            var rt = Node(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite ?? White; img.color = color;
            if (sprite == Rounded || sprite == Frame) img.type = UnityEngine.UI.Image.Type.Sliced;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>A dark panel with a gold frame. Children go under the returned transform.</summary>
        public static RectTransform Panel(Transform parent, string name, Color? background = null, bool frame = true, bool blocksClicks = true)
        {
            var bg = Img(parent, name, background ?? PanelBg, Rounded);
            bg.raycastTarget = blocksClicks;
            if (frame)
            {
                var f = Img(bg.transform, "Frame", GoldFaint, Frame);
                f.rectTransform.Stretch();
            }
            return bg.rectTransform;
        }

        public static Text Label(Transform parent, string name, string text, int size = Size.Label, Color? color = null, TextAnchor align = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal, bool shadow = true)
        {
            var rt = Node(parent, name);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font; t.fontSize = size; t.color = color ?? TextMain; t.alignment = align; t.fontStyle = style; t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false; t.supportRichText = true;
            if (shadow) { var s = rt.gameObject.AddComponent<Shadow>(); s.effectColor = new Color(0f, 0f, 0f, 0.85f); s.effectDistance = new Vector2(1.5f, -1.5f); }
            return t;
        }

        /// <summary>A gold horizontal rule.</summary>
        public static Image Rule(Transform parent, string name, float width, float y, Vector2? anchor = null)
        {
            var img = Img(parent, name, GoldDim);
            img.rectTransform.Place(anchor ?? new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, y), new Vector2(width, 2f));
            return img;
        }

        public static Button MakeButton(Transform parent, string name, string label, Action onClick, int fontSize = Size.Label)
        {
            var bg = Img(parent, name, Color.white, Rounded);
            bg.raycastTarget = true;
            var b = bg.gameObject.AddComponent<UnityEngine.UI.Button>();
            b.targetGraphic = bg;
            var cb = b.colors;
            cb.normalColor = ButtonBg; cb.highlightedColor = ButtonHover; cb.selectedColor = ButtonHover; cb.pressedColor = ButtonPressed; cb.disabledColor = ButtonDisabled;
            cb.colorMultiplier = 1f; cb.fadeDuration = 0f;   // instant: a fade from white shows as a pale button for a few frames
            b.colors = cb;
            var frame = Img(bg.transform, "Frame", GoldDim, Frame);
            frame.rectTransform.Stretch();
            var t = Label(bg.transform, "Label", label, fontSize, TextMain, TextAnchor.MiddleCenter);
            t.rectTransform.Stretch(12f, 12f, 4f, 4f);
            bg.gameObject.AddComponent<UiButtonSound>();
            bg.gameObject.AddComponent<UiButtonState>();   // dimmed text and no gold border while the button is not interactable
            MenuItem.Attach(b);   // gold selection outline for gamepad / keyboard navigation, and registration with the MenuNavigator
            if (onClick != null) b.onClick.AddListener(() => onClick());
            return b;
        }

        public static void SetLabel(this UnityEngine.UI.Button b, string text)
        {
            var t = b.GetComponentInChildren<Text>(true);
            if (t != null) t.text = text;
        }

        /// <summary>Mark a button as the chosen one of a group (gold fill) or not.</summary>
        public static void SetSelected(this UnityEngine.UI.Button b, bool selected)
        {
            var cb = b.colors;
            cb.normalColor = selected ? Selected : ButtonBg;
            b.colors = cb;
            var frame = b.transform.Find("Frame")?.GetComponent<Image>();
            if (frame != null) frame.color = !b.interactable ? FrameDisabled : selected ? Gold : GoldDim;
        }

        public static Slider MakeSlider(Transform parent, string name, float value, Action<float> onChanged)
        {
            var root = Node(parent, name);
            var s = root.gameObject.AddComponent<Slider>();
            var track = Img(root, "Track", new Color(0f, 0f, 0f, 0.6f), Rounded);
            track.rectTransform.anchorMin = new Vector2(0f, 0.5f); track.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            track.rectTransform.sizeDelta = new Vector2(0f, 10f); track.rectTransform.anchoredPosition = Vector2.zero;
            var fillArea = Node(root, "Fill Area"); fillArea.Stretch(0f, 0f, 0f, 0f);
            fillArea.anchorMin = new Vector2(0f, 0.5f); fillArea.anchorMax = new Vector2(1f, 0.5f); fillArea.sizeDelta = new Vector2(0f, 10f);
            var fill = Img(fillArea, "Fill", GoldDim, Rounded);
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(0f, 1f); fill.rectTransform.sizeDelta = Vector2.zero;
            var handleArea = Node(root, "Handle Slide Area"); handleArea.Stretch(10f, 10f, 0f, 0f);
            var handle = Img(handleArea, "Handle", Gold, Circle);
            handle.rectTransform.sizeDelta = new Vector2(26f, 26f);
            handle.raycastTarget = true;
            s.fillRect = fill.rectTransform; s.handleRect = handle.rectTransform; s.targetGraphic = handle;
            s.direction = UnityEngine.UI.Slider.Direction.LeftToRight; s.minValue = 0f; s.maxValue = 1f;
            s.SetValueWithoutNotify(value);
            var hit = root.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0); hit.raycastTarget = true;   // the whole row is clickable
            if (onChanged != null) s.onValueChanged.AddListener(v => onChanged(v));
            MenuItem.Attach(s);
            return s;
        }

        public static InputField MakeInput(Transform parent, string name, string placeholder, int fontSize = Size.Heading)
        {
            var bg = Img(parent, name, new Color(0f, 0f, 0f, 0.55f), Rounded);
            bg.raycastTarget = true;
            var frame = Img(bg.transform, "Frame", GoldDim, Frame); frame.rectTransform.Stretch();
            var f = bg.gameObject.AddComponent<UnityEngine.UI.InputField>();
            var text = Label(bg.transform, "Text", "", fontSize, TextMain, TextAnchor.MiddleLeft, FontStyle.Normal, false);
            text.rectTransform.Stretch(16f, 16f, 4f, 4f); text.supportRichText = false; text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var ph = Label(bg.transform, "Placeholder", placeholder, fontSize, TextFaint, TextAnchor.MiddleLeft, FontStyle.Italic, false);
            ph.rectTransform.Stretch(16f, 16f, 4f, 4f);
            f.textComponent = text; f.placeholder = ph; f.targetGraphic = bg; f.caretColor = Gold; f.selectionColor = new Color(0.88f, 0.69f, 0.34f, 0.35f);
            f.lineType = UnityEngine.UI.InputField.LineType.SingleLine;
            return f;
        }

        // ---------------- canvas and events ----------------

        /// <summary>A canvas on this object: overlay, scaled from 1920x1080 (half width, half height match), with a raycaster.</summary>
        public static Canvas MakeCanvas(GameObject go, int sortingOrder)
        {
            go.layer = UiLayer;
            var c = go.GetComponent<Canvas>(); if (c == null) c = go.AddComponent<Canvas>();   // not ??: Unity fake-null
            c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = sortingOrder; c.pixelPerfect = false;
            var s = go.GetComponent<CanvasScaler>(); if (s == null) s = go.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; s.referenceResolution = new Vector2(RefWidth, RefHeight);
            s.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; s.matchWidthOrHeight = 0.5f;
            if (go.GetComponent<GraphicRaycaster>() == null) go.AddComponent<GraphicRaycaster>();
            return c;
        }

        /// <summary>The scene's EventSystem, made if missing, driven by the Input System's UI module when the package is there.</summary>
        public static EventSystem EnsureEventSystem()
        {
            var es = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            if (es == null)
            {
                var go = new GameObject("EventSystem");
                es = go.AddComponent<EventSystem>();
            }
#if DUNGEONGEN_INPUTSYSTEM
            var module = es.GetComponent<InputSystemUIInputModule>();
            if (module == null)
            {
                var legacy = es.GetComponent<StandaloneInputModule>();
                if (legacy != null) UnityEngine.Object.DestroyImmediate(legacy);
                module = es.gameObject.AddComponent<InputSystemUIInputModule>();
            }
            if (Application.isPlaying && module.actionsAsset == null) module.AssignDefaultActions();   // the defaults already bind arrows, WASD, Enter, Escape and the gamepad (stick, d-pad, A, B)
#else
            if (es.GetComponent<BaseInputModule>() == null) es.gameObject.AddComponent<StandaloneInputModule>();
#endif
            if (Application.isPlaying && es.GetComponent<MenuNavigator>() == null) es.gameObject.AddComponent<MenuNavigator>();   // picks the first button on each screen, B / Escape goes back
            return es;
        }

        // ---------------- formatting ----------------

        public static string Clock(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);
            int s = Mathf.FloorToInt(seconds);
            return $"{s / 60}:{s % 60:00}";
        }

        public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        public static string Colour(string text, Color c) => $"<color=#{Hex(c)}>{text}</color>";

        // ---------------- generated sprites ----------------

        private static Sprite MakeRounded(int size, int radius, bool outlineOnly)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = outlineOnly ? "ui-frame" : "ui-rounded" };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Distance outside the rounded rectangle (negative inside).
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius), cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)) - radius;
                    float a = Mathf.Clamp01(0.5f - d);
                    if (outlineOnly) a *= Mathf.Clamp01(d + 2.5f);   // keep a 2 px band inside the edge
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            float b = radius + 2;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        private static Sprite MakeCircle(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "ui-circle" };
            var px = new Color32[size * size];
            float r = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d) * 255f));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite MakeGlow(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "ui-glow" };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Clamp01(Mathf.Sqrt(u * u + v * v));
                    float a = 1f - Mathf.SmoothStep(0f, 1f, d);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite MakeBarGradient(int height)
        {
            var tex = new Texture2D(4, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "ui-bar-gradient" };
            var px = new Color32[4 * height];
            for (int y = 0; y < height; y++)
            {
                float t = y / (float)(height - 1);                       // 0 bottom, 1 top
                float shade = Mathf.Lerp(0.56f, 1.12f, t);               // a little over 1 near the top: a soft highlight once tinted
                byte v = (byte)(Mathf.Clamp01(shade) * 255f);
                for (int x = 0; x < 4; x++) px[y * 4 + x] = new Color32(v, v, v, 255);
            }
            tex.SetPixels32(px); tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, 4, height), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite MakeVignette(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "ui-vignette" };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(u * u + v * v) / 1.4142f;
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - 0.25f) / 0.75f));
                    px[y * size + x] = new Color32(0, 0, 0, (byte)(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }

    /// <summary>
    /// On every <see cref="UiStyle.MakeButton"/> button: while it is not interactable its label is dimmed, its border loses the gold and its
    /// face goes dark, so a disabled button never looks like an enabled one. Reads <c>interactable</c> each frame (callers just assign it),
    /// and gives the caller's label colour and border back when the button is enabled again.
    /// </summary>
    public sealed class UiButtonState : MonoBehaviour
    {
        private Button _button;
        private Text _label;
        private Image _frame;
        private bool? _shownEnabled;
        private Color _labelColour = UiStyle.TextMain;

        private void Awake() { _button = GetComponent<Button>(); }
        private void OnEnable() { _shownEnabled = null; Apply(); }
        private void Update() => Apply();

        private void Apply()
        {
            if (_button == null) return;
            bool on = _button.IsInteractable();
            if (_shownEnabled.HasValue && _shownEnabled.Value == on) return;
            if (_label == null) _label = GetComponentInChildren<Text>(true);
            if (_frame == null) { var f = transform.Find("Frame"); if (f != null) _frame = f.GetComponent<Image>(); }
            if (_label != null)
            {
                if (!on) { if (_label.color != UiStyle.TextDisabled) _labelColour = _label.color; _label.color = UiStyle.TextDisabled; }   // remember what the caller chose
                else if (_label.color == UiStyle.TextDisabled) _label.color = _labelColour;
            }
            if (_frame != null) _frame.color = on ? (_button.colors.normalColor == UiStyle.Selected ? UiStyle.Gold : UiStyle.GoldDim) : UiStyle.FrameDisabled;
            _shownEnabled = on;
        }
    }
}
