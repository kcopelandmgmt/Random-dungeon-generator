using System;
using System.IO;
using DungeonGen.Core.Net;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DungeonGen.Unity.UI
{
    /// <summary>The key names the tutorial's texts show, read from the player's own bindings (<see cref="GameBindings"/>), so a rebind shows at once.</summary>
    public static class TutorialKeys
    {
        /// <summary>What the key bound to this action (a <see cref="TutorialScript.Tokens"/> name) is called: "Space", "Left Mouse", "W".</summary>
        public static string Name(string token)
        {
#if DUNGEONGEN_INPUTSYSTEM
            if (Enum.TryParse(token, out BindingAction a))
            {
                try { return GameBindings.DisplayName(a); } catch (Exception) { }
            }
#endif
            switch (token)
            {
                case "MoveUp": return "W"; case "MoveDown": return "S"; case "MoveLeft": return "A"; case "MoveRight": return "D";
                case "Basic": return "Left Mouse"; case "Bolt": return "Right Mouse"; case "Zone": return "Q"; case "Mobility": return "E"; case "Ultimate": return "R";
                case "Interact": return "Space"; case "Ping": return "F"; case "Potion": return "1"; case "Antidote": return "2"; case "Map": return "M"; case "Inventory": return "I"; case "Pause": return "Esc";
                default: return token;
            }
        }

        /// <summary>The text with every key token filled in and drawn as a gold key name.</summary>
        public static string Fill(string text) => TutorialScript.Format(text, k => $"<b>{UiStyle.Colour(Name(k), UiStyle.Gold)}</b>");

        /// <summary>The text with plain key names (for tests and logs).</summary>
        public static string Plain(string text) => TutorialScript.Format(text, Name);
    }

    /// <summary>
    /// The tutorial's objective card (wave 5.10): top left, on its own canvas (sorting order 11, over the HUD), never blocking clicks. It shows the
    /// lesson the player is on (step n of 7, a row of pips, the title, what to press with the player's own key names, live progress such as the packs
    /// cleared, the weight carried or the extract timer, and a hint), and briefly marks a finished lesson. It advances only on what really happened:
    /// the round's events and snapshots and the mirrored state, through the core <see cref="TutorialTracker"/>. Installed when a tutorial round's
    /// scene loads (<see cref="PracticeLaunch.Tutorial"/>); hidden while paused, on the summary and after the end. Reads nothing from input.
    /// </summary>
    public sealed class TutorialOverlay : MonoBehaviour, IRoundUi
    {
        public const string CanvasName = "Tutorial Canvas";
        public const int SortingOrder = 11;
        private const float Width = 560f, Pad = 26f;

        public static TutorialOverlay Current { get; private set; }
        public TutorialTracker Tracker { get; private set; }
        /// <summary>The lesson on the card (Move before the round is up).</summary>
        public TutorialStep Step => Tracker?.Step ?? TutorialStep.Move;
        /// <summary>The card's title and text as shown, with plain key names: what tests read.</summary>
        public string CardText { get; private set; } = "";
        public bool CardVisible => _card != null && _card.gameObject.activeSelf;

        private IMatchTransport _transport;
        private ClientWorldView _world;
        private DungeonMatchBootstrap _bootstrap;
        private RectTransform _card, _pipRow, _toast;
        private Image _accent, _frame;
        private Image[] _pips;
        private Text _stepLabel, _title, _body, _progress, _hint, _toastText;
        private TutorialStep _shown = (TutorialStep)255;
        private string _shownProgress, _shownKeys;
        private float _changedAt = -10f, _toastAt = -10f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Hook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        /// <summary>A tutorial round's scene: put the card up (before the round hosts, so the host's IRoundUi pass attaches it).</summary>
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (PracticeLaunch.Current == null || !PracticeLaunch.Current.Tutorial) return;
            if (FindFirstObjectByType<DungeonMatchBootstrap>() == null) return;
            Install();
        }

        /// <summary>Make (or return) the card's canvas in the current scene.</summary>
        public static TutorialOverlay Install()
        {
            if (Current != null) return Current;
            var go = new GameObject(CanvasName);
            UiStyle.MakeCanvas(go, SortingOrder);
            return go.AddComponent<TutorialOverlay>();
        }

        private void Awake()
        {
            Current = this;
            Build();
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            if (_world != null) _world.SnapshotApplied -= OnSnapshot;
            if (_transport != null) _transport.EventReceived -= OnEventBytes;
        }

        /// <summary>The round's transport (the offline host calls this on every <see cref="IRoundUi"/>): events this team hears.</summary>
        public void Attach(IMatchTransport transport)
        {
            if (transport == null || transport == _transport) return;
            if (_transport != null) _transport.EventReceived -= OnEventBytes;
            _transport = transport;
            _transport.EventReceived += OnEventBytes;
        }

        private void OnEventBytes(byte[] bytes)
        {
            if (this == null) return;
            EnsureTracker();
            if (Tracker == null) return;
            using var ms = new MemoryStream(bytes);
            using var r = new BinaryReader(ms);
            Tracker.Observe(MatchEvent.Read(r));
        }

        private void OnSnapshot(TeamSnapshot snap) { if (this != null) { EnsureTracker(); Tracker?.Observe(snap); } }

        private void EnsureTracker()
        {
            if (_bootstrap == null) _bootstrap = OfflineMatch.Current != null && OfflineMatch.Current.Bootstrap != null ? OfflineMatch.Current.Bootstrap : FindFirstObjectByType<DungeonMatchBootstrap>();
            if (Tracker == null && _bootstrap != null && _bootstrap.State != null) Tracker = new TutorialTracker(GameFlow.LocalId, _bootstrap.State.Layout);
        }

        private void Update()
        {
            if (_transport == null && OfflineMatch.Current != null && OfflineMatch.Current.Transport != null) Attach(OfflineMatch.Current.Transport);
            if (_world == null)
            {
                _world = OfflineMatch.Current != null && OfflineMatch.Current.World != null ? OfflineMatch.Current.World : FindFirstObjectByType<ClientWorldView>();
                if (_world != null) _world.SnapshotApplied += OnSnapshot;
            }
            EnsureTracker();
            if (Tracker != null && _bootstrap != null) Tracker.Observe(_bootstrap.State);

            var flow = GameFlow.Instance;
            bool inRound = flow == null || flow.Phase == FlowPhase.InRound;
            bool show = Tracker != null && inRound && !GameFlow.RoundPaused && !Tracker.Finished;
            if (_card.gameObject.activeSelf != show) _card.gameObject.SetActive(show);
            if (_toast.gameObject.activeSelf != (show && Time.unscaledTime - _toastAt < 3.2f)) _toast.gameObject.SetActive(show && Time.unscaledTime - _toastAt < 3.2f);
            if (!show) return;
            Refresh();
            Animate();
        }

        // ---------------------------------------------------------------- drawing ----------------------------------------------------------------

        private void Build()
        {
            var root = UiStyle.Node(transform, "Tutorial");
            root.Stretch();
            _card = UiStyle.Panel(root, "TutorialCard", new Color(0.045f, 0.040f, 0.048f, 0.90f), true, false);
            _card.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -24f), new Vector2(Width, 240f));
            _frame = _card.Find("Frame")?.GetComponent<Image>();
            _accent = UiStyle.Img(_card, "Accent", UiStyle.Gold);
            _accent.rectTransform.anchorMin = new Vector2(0f, 0f); _accent.rectTransform.anchorMax = new Vector2(0f, 1f); _accent.rectTransform.pivot = new Vector2(0f, 0.5f);
            _accent.rectTransform.offsetMin = new Vector2(3f, 10f); _accent.rectTransform.offsetMax = new Vector2(8f, -10f);

            var head = UiStyle.Label(_card, "TutorialHeading", "TUTORIAL", UiStyle.Size.Small, UiStyle.GoldDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            head.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Pad, -14f), new Vector2(240f, 26f));
            _stepLabel = UiStyle.Label(_card, "TutorialStepLabel", "", UiStyle.Size.Small, UiStyle.TextDim, TextAnchor.MiddleRight, FontStyle.Bold);
            _stepLabel.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-Pad + 4f, -14f), new Vector2(240f, 26f));

            _pipRow = UiStyle.Node(_card, "TutorialPips");
            _pipRow.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Pad, -46f), new Vector2(Width - 2f * Pad, 6f));
            int n = TutorialScript.Lessons.Length;
            float gap = 6f, w = (Width - 2f * Pad - gap * (n - 1)) / n;
            _pips = new Image[n];
            for (int i = 0; i < n; i++)
            {
                _pips[i] = UiStyle.Img(_pipRow, "Pip-" + i, UiStyle.BarTrack, UiStyle.Rounded);
                _pips[i].rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(i * (w + gap), 0f), new Vector2(w, 6f));
            }

            _title = UiStyle.Label(_card, "TutorialTitle", "", UiStyle.Size.Heading, UiStyle.TextMain, TextAnchor.UpperLeft, FontStyle.Bold);
            _body = UiStyle.Label(_card, "TutorialText", "", UiStyle.Size.Body, UiStyle.TextSoft, TextAnchor.UpperLeft);
            _body.lineSpacing = 1.1f;
            _progress = UiStyle.Label(_card, "TutorialProgress", "", UiStyle.Size.Body, UiStyle.Gold, TextAnchor.UpperLeft, FontStyle.Bold);
            _hint = UiStyle.Label(_card, "TutorialHint", "", UiStyle.Size.Small, UiStyle.TextDim, TextAnchor.UpperLeft, FontStyle.Italic);
            _hint.lineSpacing = 1.05f;

            _toast = UiStyle.Panel(root, "TutorialToast", new Color(0.10f, 0.16f, 0.08f, 0.88f), false, false);
            _toast.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -280f), new Vector2(Width, 40f));
            var tick = UiStyle.Img(_toast, "ToastBar", UiStyle.Good);
            tick.rectTransform.anchorMin = new Vector2(0f, 0f); tick.rectTransform.anchorMax = new Vector2(0f, 1f); tick.rectTransform.pivot = new Vector2(0f, 0.5f);
            tick.rectTransform.offsetMin = new Vector2(3f, 6f); tick.rectTransform.offsetMax = new Vector2(8f, -6f);
            _toastText = UiStyle.Label(_toast, "ToastText", "", UiStyle.Size.Body, UiStyle.Good, TextAnchor.MiddleLeft, FontStyle.Bold);
            _toastText.rectTransform.Stretch(Pad, 12f, 0f, 0f);
            _toast.gameObject.SetActive(false);
        }

        /// <summary>Redraw the card's words when the lesson, its progress or a key binding changed; size the card to them.</summary>
        private void Refresh()
        {
            var step = Tracker.Step;
            string progress = Tracker.Progress;
            string keys = TutorialKeys.Name("Interact") + TutorialKeys.Name("Ultimate") + TutorialKeys.Name("MoveUp") + TutorialKeys.Name("Basic");
            if (step == _shown && progress == _shownProgress && keys == _shownKeys) return;
            if (step != _shown && _shown != (TutorialStep)255)
            {
                _changedAt = Time.unscaledTime;
                var previous = TutorialScript.Objective(_shown);
                if (Tracker.IsDone(_shown)) { _toastText.text = "Done: " + previous.Title; _toastAt = Time.unscaledTime; }
            }
            _shown = step; _shownProgress = progress; _shownKeys = keys;

            var o = TutorialScript.Objective(step);
            int index = Array.IndexOf(TutorialScript.Lessons, step);
            _stepLabel.text = index >= 0 ? $"STEP {index + 1} OF {TutorialScript.Lessons.Length}" : "";
            for (int i = 0; i < _pips.Length; i++)
                _pips[i].color = Tracker.IsDone(TutorialScript.Lessons[i]) ? UiStyle.Gold : i == index ? UiStyle.TextMain : UiStyle.BarTrack;
            _title.text = o.Title;
            _body.text = TutorialKeys.Fill(o.Text);
            _progress.text = progress ?? "";
            _hint.text = TutorialKeys.Fill(o.Hint);
            CardText = o.Title + "\n" + TutorialKeys.Plain(o.Text) + "\n" + (progress ?? "") + "\n" + TutorialKeys.Plain(o.Hint);

            // Lay the lines out top to bottom at their wrapped heights.
            float inner = Width - 2f * Pad, y = 62f;
            y = Line(_title, y, inner, 4f);
            y = Line(_body, y, inner, 10f);
            if (!string.IsNullOrEmpty(_progress.text)) { _progress.gameObject.SetActive(true); y = Line(_progress, y, inner, 8f); } else _progress.gameObject.SetActive(false);
            if (!string.IsNullOrEmpty(_hint.text)) { _hint.gameObject.SetActive(true); y = Line(_hint, y, inner, 0f); } else _hint.gameObject.SetActive(false);
            _card.sizeDelta = new Vector2(Width, y + 20f);
            _toast.anchoredPosition = new Vector2(28f, -24f - (y + 20f) - 10f);
        }

        private static readonly TextGenerator Measure = new TextGenerator();

        /// <summary>
        /// Place a line at <paramref name=top/> and size it to its wrapped height, measured at the 1920x1080 reference scale (scale factor 1) and a little
        /// narrower than drawn, so a window of another size (whose glyphs round differently) never wraps a word more than the card made room for.
        /// </summary>
        private static float Line(Text t, float top, float width, float after)
        {
            t.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Pad, -top), new Vector2(width, 10f));
            var settings = t.GetGenerationSettings(new Vector2(width - 10f, 0f));
            settings.scaleFactor = 1f; settings.generateOutOfBounds = true;
            float h = Mathf.Ceil(Measure.GetPreferredHeight(t.text, settings)) + 4f;
            t.rectTransform.sizeDelta = new Vector2(width, h);
            return top + h + after;
        }

        /// <summary>A new lesson: the accent and the frame glow for a moment, the current pip breathes.</summary>
        private void Animate()
        {
            float since = Time.unscaledTime - _changedAt;
            float flash = since < 0.9f ? 1f - since / 0.9f : 0f;
            if (_frame != null) _frame.color = Color.Lerp(UiStyle.GoldFaint, UiStyle.Gold, flash);
            _accent.color = Color.Lerp(UiStyle.GoldDim, Color.white, flash * 0.6f);
            int index = Array.IndexOf(TutorialScript.Lessons, Tracker.Step);
            if (index >= 0 && index < _pips.Length)
            {
                float breathe = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 3.2f);
                _pips[index].color = Color.Lerp(UiStyle.GoldDim, UiStyle.TextMain, breathe);
            }
        }
    }
}
