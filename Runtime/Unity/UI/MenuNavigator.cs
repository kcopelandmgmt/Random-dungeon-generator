using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if DUNGEONGEN_INPUTSYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace DungeonGen.Unity.UI
{
    /// <summary>
    /// Marks a root as one menu screen for <see cref="MenuNavigator"/>. Roots named <c>Screen-*</c> (the main menu's screens)
    /// and the pause, settings, recap and summary panels count without it; add this to any other panel that should get a
    /// first selected button and a Back on B / Escape.
    /// </summary>
    public sealed class MenuScope : MonoBehaviour { }

    /// <summary>
    /// Put on every button and slider by <see cref="UiStyle.MakeButton"/> and <see cref="UiStyle.MakeSlider"/>: draws the
    /// gold selection outline while the control is selected (by gamepad, keyboard, or the mouse hovering it), and lets the
    /// <see cref="MenuNavigator"/> know which menu screen the control belongs to.
    /// </summary>
    public sealed class MenuItem : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler
    {
        internal static readonly List<MenuItem> Live = new List<MenuItem>();
        private static int _sequence;

        internal Selectable Target;
        internal Transform Scope;
        internal Canvas RootCanvas;
        internal int Order;
        private GameObject _focus;

        /// <summary>Add the selection outline and menu registration to a button or slider.</summary>
        public static MenuItem Attach(Selectable s)
        {
            var item = s.gameObject.AddComponent<MenuItem>();
            item.Target = s;
            var rt = UiStyle.Node(s.transform, "Focus");
            rt.Stretch(-5f, -5f, -5f, -5f);
            var inner = UiStyle.Img(rt, "Gold", UiStyle.Gold, UiStyle.Frame);
            inner.rectTransform.Stretch(); inner.pixelsPerUnitMultiplier = 0.5f;
            var outer = UiStyle.Img(rt, "Glow", new Color(UiStyle.Gold.r, UiStyle.Gold.g, UiStyle.Gold.b, 0.30f), UiStyle.Frame);
            outer.rectTransform.Stretch(-5f, -5f, -5f, -5f); outer.pixelsPerUnitMultiplier = 0.5f;
            item._focus = rt.gameObject;
            item._focus.SetActive(false);
            return item;
        }

        private void OnEnable()
        {
            Order = ++_sequence;
            Scope = FindScope(transform);
            var c = GetComponentInParent<Canvas>();
            RootCanvas = c != null ? c.rootCanvas : null;
            Live.Add(this);
            if (_focus != null) _focus.SetActive(EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject);
        }

        private void OnDisable()
        {
            Live.Remove(this);
            if (_focus != null) _focus.SetActive(false);
        }

        public void OnSelect(BaseEventData e) { if (_focus != null) _focus.SetActive(true); }
        public void OnDeselect(BaseEventData e) { if (_focus != null) _focus.SetActive(false); }

        /// <summary>The mouse and the keyboard share one highlight: hovering a control on a menu screen selects it (never in-game HUD buttons, where a stale selection could swallow Enter).</summary>
        public void OnPointerEnter(PointerEventData e)
        {
            if (Scope != null && Target != null && Target.IsInteractable() && EventSystem.current != null && !IsEditingText())
                EventSystem.current.SetSelectedGameObject(gameObject);
        }

        private static bool IsEditingText()
        {
            var sel = EventSystem.current.currentSelectedGameObject;
            var field = sel != null ? sel.GetComponent<InputField>() : null;
            return field != null && field.isFocused;
        }

        internal static bool IsScopeRoot(Transform t) =>
            t.name.StartsWith("Screen-") || t.name == "PausePanel" || t.name == "SettingsPanel" || t.name == "RoundSummaryPanel" || t.name == "DeathRecapPanel" || t.GetComponent<MenuScope>() != null;

        internal static Transform FindScope(Transform t)
        {
            for (; t != null; t = t.parent) if (IsScopeRoot(t)) return t;
            return null;
        }
    }

    /// <summary>
    /// Keeps a sensible control selected on whatever menu screen is up, so a gamepad (or arrow keys and Enter) works
    /// everywhere without each screen doing anything. When the topmost screen changes it selects that screen's first
    /// interactable control (preferring Play, Enter, Continue), or the control that was selected on the same screen before it
    /// was rebuilt. If nothing is selected (the mouse clicked empty space) the next navigation press brings the selection
    /// back. B on a gamepad or Escape presses the screen's Back button where it has one. Lives on the EventSystem object;
    /// <see cref="UiStyle.EnsureEventSystem"/> adds it.
    /// </summary>
    public sealed class MenuNavigator : MonoBehaviour
    {
        /// <summary>Buttons chosen first, in this order, when a screen has one of them; otherwise the first in hierarchy order.</summary>
        public static readonly string[] PreferredFirst = { "PlayButton", "EnterButton", "ContinueButton", "ResumeButton", "ReturnButton" };
        public const string BackName = "BackButton";

        /// <summary>The screen root being driven now (null when no menu screen is up).</summary>
        public Transform Scope { get; private set; }

        private string _scopeName, _lastName;

        private void Update()
        {
            var es = EventSystem.current;
            if (es == null || !es.isActiveAndEnabled) return;
            var top = TopScope();
            if (top == null) { Scope = null; return; }
            if (top != Scope)
            {
                string previousName = _scopeName;
                Scope = top; _scopeName = top.name;
                // The same screen rebuilt in place (a refresh) keeps the control the player was on.
                var again = previousName == top.name && _lastName != null ? Find(top, _lastName) : null;
                Pick(es, again != null ? again : First(top));
                return;
            }
            var sel = es.currentSelectedGameObject;
            bool valid = sel != null && sel.activeInHierarchy && sel.transform.IsChildOf(top);
            if (valid) { _lastName = sel.name; }
            else if (WantsNavigation()) Pick(es, (_lastName != null ? Find(top, _lastName) : null) ?? First(top));
            if (es.sendNavigationEvents && CancelPressed() && !EditingText(sel)) GoBack();
        }

        /// <summary>Press the current screen's Back button, if it has one (what B or Escape does).</summary>
        public bool GoBack()
        {
            if (Scope == null) return false;
            var back = Find(Scope, BackName) as Button;
            if (back == null || !back.IsInteractable()) return false;
            UiSound.Play(UiSound.Click);
            back.onClick.Invoke();
            return true;
        }

        private static void Pick(EventSystem es, Selectable s)
        {
            if (s == null) return;
            es.SetSelectedGameObject(null);
            es.SetSelectedGameObject(s.gameObject);
        }

        /// <summary>The topmost screen: highest canvas, then the one built last.</summary>
        private static Transform TopScope()
        {
            MenuItem best = null;
            foreach (var m in MenuItem.Live)
            {
                if (m == null || m.Scope == null || !m.Scope.gameObject.activeInHierarchy) continue;
                if (best == null) { best = m; continue; }
                int a = m.RootCanvas != null ? m.RootCanvas.sortingOrder : 0, b = best.RootCanvas != null ? best.RootCanvas.sortingOrder : 0;
                if (a > b || (a == b && m.Order > best.Order)) best = m;
            }
            return best != null ? best.Scope : null;
        }

        /// <summary>The control to start on: a preferred one, else the first interactable (text fields last).</summary>
        public static Selectable First(Transform scope)
        {
            foreach (var name in PreferredFirst)
            {
                var s = Find(scope, name);
                if (s != null && s.IsInteractable()) return s;
            }
            Selectable field = null;
            foreach (var s in scope.GetComponentsInChildren<Selectable>(false))
            {
                if (!s.IsInteractable() || !s.gameObject.activeInHierarchy) continue;
                if (s is InputField) { if (field == null) field = s; continue; }
                return s;
            }
            return field;
        }

        private static Selectable Find(Transform scope, string name)
        {
            foreach (var s in scope.GetComponentsInChildren<Selectable>(false))
                if (s.name == name && s.IsInteractable()) return s;
            return null;
        }

        private static bool EditingText(GameObject sel)
        {
            var f = sel != null ? sel.GetComponent<InputField>() : null;
            return f != null && f.isFocused;
        }

        private static bool WantsNavigation()
        {
#if DUNGEONGEN_INPUTSYSTEM
            if (EventSystem.current.currentInputModule is InputSystemUIInputModule m)
            {
                if (m.move != null && m.move.action != null && m.move.action.WasPerformedThisFrame()) return true;
                if (m.submit != null && m.submit.action != null && m.submit.action.WasPressedThisFrame()) return true;
            }
#endif
            return false;
        }

        private static bool CancelPressed()
        {
#if DUNGEONGEN_INPUTSYSTEM
            if (EventSystem.current.currentInputModule is InputSystemUIInputModule m)
                return m.cancel != null && m.cancel.action != null && m.cancel.action.WasPressedThisFrame();
#endif
            return false;
        }
    }
}
