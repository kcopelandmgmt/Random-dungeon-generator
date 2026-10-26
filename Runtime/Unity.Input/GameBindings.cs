using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace DungeonGen.Unity
{
    /// <summary>Every gameplay key the player can rebind. Gamepad bindings are fixed and not part of this table.</summary>
    public enum BindingAction
    {
        MoveUp, MoveDown, MoveLeft, MoveRight,
        Basic, Bolt, Zone, Mobility, Ultimate,
        Interact, Ping, Potion, Antidote,
        Map, Inventory, Pause,
    }

    /// <summary>One row of the binding table.</summary>
    public sealed class BindingInfo
    {
        public BindingAction Action;
        /// <summary>Stable id used in saved settings.</summary>
        public string Id;
        /// <summary>What the settings screen calls it.</summary>
        public string Label;
        /// <summary>Settings screen grouping: Movement, Abilities, or Actions.</summary>
        public string Group;
        /// <summary>An Input System control path on the keyboard or mouse.</summary>
        public string DefaultPath;
    }

    /// <summary>The outcome of a rebind: what changed, and which other action had to give its key up.</summary>
    public readonly struct RebindResult
    {
        public readonly bool Changed;
        /// <summary>Set when the new key was already in use: that action took this action's old key (a swap).</summary>
        public readonly BindingAction? SwappedWith;
        public readonly string Error;
        public RebindResult(bool changed, BindingAction? swappedWith, string error) { Changed = changed; SwappedWith = swappedWith; Error = error; }
        public bool Ok => Error == null;
    }

    /// <summary>
    /// The single table of gameplay keys. <see cref="LocalPlayerController"/> builds its actions from it and re-binds them
    /// whenever it changes; HUD scripts that need a key (pause, inventory) ask <see cref="WasPressedThisFrame"/> here
    /// instead of naming a key. Changes persist in <see cref="GameSettings"/> (PlayerPrefs) as text and apply at once.
    /// Giving an action a key another action holds swaps the two, so no key does two jobs.
    /// </summary>
    public static class GameBindings
    {
        public static readonly IReadOnlyList<BindingInfo> All = new List<BindingInfo>
        {
            Row(BindingAction.MoveUp, "MoveUp", "Move up", "Movement", "<Keyboard>/w"),
            Row(BindingAction.MoveDown, "MoveDown", "Move down", "Movement", "<Keyboard>/s"),
            Row(BindingAction.MoveLeft, "MoveLeft", "Move left", "Movement", "<Keyboard>/a"),
            Row(BindingAction.MoveRight, "MoveRight", "Move right", "Movement", "<Keyboard>/d"),
            Row(BindingAction.Basic, "Basic", "Basic attack", "Abilities", "<Mouse>/leftButton"),
            Row(BindingAction.Bolt, "Bolt", "Bolt", "Abilities", "<Mouse>/rightButton"),
            Row(BindingAction.Zone, "Zone", "Zone", "Abilities", "<Keyboard>/q"),
            Row(BindingAction.Mobility, "Mobility", "Mobility", "Abilities", "<Keyboard>/e"),
            Row(BindingAction.Ultimate, "Ultimate", "Ultimate", "Abilities", "<Keyboard>/r"),
            Row(BindingAction.Interact, "Interact", "Interact (hold)", "Actions", "<Keyboard>/space"),
            Row(BindingAction.Ping, "Ping", "Ping", "Actions", "<Keyboard>/f"),
            Row(BindingAction.Potion, "Potion", "Health potion", "Actions", "<Keyboard>/1"),
            Row(BindingAction.Antidote, "Antidote", "Antidote", "Actions", "<Keyboard>/2"),
            Row(BindingAction.Map, "Map", "Map (toggle)", "Actions", "<Keyboard>/m"),
            Row(BindingAction.Inventory, "Inventory", "Inventory", "Actions", "<Keyboard>/i"),
            Row(BindingAction.Pause, "Pause", "Pause menu", "Actions", "<Keyboard>/escape"),
        };

        private static BindingInfo Row(BindingAction a, string id, string label, string group, string path) =>
            new BindingInfo { Action = a, Id = id, Label = label, Group = group, DefaultPath = path };

        /// <summary>Raised after any binding changes (a rebind, a swap, a reset).</summary>
        public static event Action Changed;
        /// <summary>Counts changes, so a cache can tell it is stale.</summary>
        public static int Version { get; private set; }

        private static Dictionary<BindingAction, string> _paths;
        private static readonly Dictionary<BindingAction, ButtonControl> Controls = new Dictionary<BindingAction, ButtonControl>();

        public static BindingInfo Info(BindingAction a) => All[(int)a];

        private static Dictionary<BindingAction, string> Paths
        {
            get
            {
                if (_paths == null)
                {
                    _paths = new Dictionary<BindingAction, string>();
                    foreach (var b in All) _paths[b.Action] = b.DefaultPath;
                    foreach (var line in GameSettings.BindingsText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string id = line.Substring(0, eq).Trim(), path = line.Substring(eq + 1).Trim();
                        var info = FindById(id);
                        if (info != null && IsBindable(path)) _paths[info.Action] = path;
                    }
                    // A saved file that gave one key to two actions (hand edited): the first keeps it, the rest go back to default.
                    var seen = new HashSet<string>();
                    foreach (var b in All)
                        if (!seen.Add(_paths[b.Action])) _paths[b.Action] = b.DefaultPath;
                }
                return _paths;
            }
        }

        private static BindingInfo FindById(string id)
        {
            foreach (var b in All) if (b.Id == id) return b;
            return null;
        }

        /// <summary>The control path an action is bound to now, such as <c>&lt;Keyboard&gt;/w</c>.</summary>
        public static string Path(BindingAction a) => Paths[a];

        /// <summary>What to show on a button: "W", "Space", "Left Mouse".</summary>
        public static string DisplayName(BindingAction a) => DisplayName(Path(a));

        public static string DisplayName(string path)
        {
            switch (path)
            {
                case "<Mouse>/leftButton": return "Left Mouse";
                case "<Mouse>/rightButton": return "Right Mouse";
                case "<Mouse>/middleButton": return "Middle Mouse";
                case "<Mouse>/backButton": return "Mouse 4";
                case "<Mouse>/forwardButton": return "Mouse 5";
            }
            string s = InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
            return string.IsNullOrEmpty(s) ? path : s;
        }

        public static bool IsDefault(BindingAction a) => Path(a) == Info(a).DefaultPath;

        /// <summary>Only keyboard keys and mouse buttons can be bound.</summary>
        public static bool IsBindable(string path) =>
            !string.IsNullOrEmpty(path) && (path.StartsWith("<Keyboard>/", StringComparison.Ordinal) || (path.StartsWith("<Mouse>/", StringComparison.Ordinal) && path.EndsWith("Button", StringComparison.Ordinal)));

        /// <summary>The action holding this control now, if any.</summary>
        public static BindingAction? HolderOf(string path)
        {
            foreach (var b in All) if (Paths[b.Action] == path) return b.Action;
            return null;
        }

        /// <summary>
        /// Give <paramref name="a"/> the control <paramref name="path"/>. If another action had it, that action takes
        /// <paramref name="a"/>'s old control (a swap) and the result says which.
        /// </summary>
        public static RebindResult Rebind(BindingAction a, string path)
        {
            if (!IsBindable(path)) return new RebindResult(false, null, "Only keyboard keys and mouse buttons can be bound.");
            var paths = Paths;
            if (paths[a] == path) return new RebindResult(false, null, null);
            BindingAction? swapped = HolderOf(path);
            string old = paths[a];
            paths[a] = path;
            if (swapped.HasValue) paths[swapped.Value] = old;
            Commit();
            return new RebindResult(true, swapped, null);
        }

        /// <summary>One action back to its default key (swapping with whoever holds that key now).</summary>
        public static RebindResult Reset(BindingAction a) => Rebind(a, Info(a).DefaultPath);

        /// <summary>Every key back to its default.</summary>
        public static void ResetAll()
        {
            _paths = null;
            GameSettings.BindingsText = "";
            _paths = null;
            Controls.Clear();
            Version++;
            Changed?.Invoke();
        }

        private static void Commit()
        {
            var sb = new StringBuilder();
            foreach (var b in All)
                if (_paths[b.Action] != b.DefaultPath) sb.Append(b.Id).Append('=').Append(_paths[b.Action]).Append('\n');
            GameSettings.BindingsText = sb.ToString();
            Controls.Clear();
            Version++;
            Changed?.Invoke();
        }

        /// <summary>Forget the cached table so the next read comes from PlayerPrefs again (tests that edit PlayerPrefs directly).</summary>
        public static void Reload()
        {
            _paths = null; Controls.Clear(); Version++;
            Changed?.Invoke();
        }

        // ---------------- reading keys (for scripts that have no InputAction of their own) ----------------

        private static ButtonControl Control(BindingAction a)
        {
            if (!Controls.TryGetValue(a, out var c) || c == null || c.device == null || !c.device.added)
            {
                c = InputSystem.FindControl(ToInputPath(Path(a))) as ButtonControl;
                Controls[a] = c;
            }
            return c;
        }

        // "<Keyboard>/w" and "/Keyboard/w" are both valid for FindControl only in the second form.
        private static string ToInputPath(string path)
        {
            int close = path.IndexOf('>');
            return path.StartsWith("<", StringComparison.Ordinal) && close > 0 ? "/" + path.Substring(1, close - 1) + path.Substring(close + 1) : path;
        }

        /// <summary>True on the frame the action's key went down.</summary>
        public static bool WasPressedThisFrame(BindingAction a) { var c = Control(a); return c != null && c.wasPressedThisFrame; }

        /// <summary>True while the action's key is held.</summary>
        public static bool IsPressed(BindingAction a) { var c = Control(a); return c != null && c.isPressed; }

        // ---------------- listening for the next key ----------------

        /// <summary>True while the game waits for a key to bind.</summary>
        public static bool Capturing => _listener != null;

        private static RebindListener _listener;

        /// <summary>
        /// Wait for the next keyboard key or mouse button and report its control path. Escape cancels. UI navigation is
        /// switched off while listening, so arrow keys can be bound. Returns false if a capture is already running.
        /// </summary>
        public static bool BeginCapture(Action<string> onPath, Action onCancel)
        {
            if (_listener != null) return false;
            var go = new GameObject("RebindListener") { hideFlags = HideFlags.HideAndDontSave };
            _listener = go.AddComponent<RebindListener>();
            _listener.Begin(onPath, onCancel);
            return true;
        }

        public static void CancelCapture()
        {
            if (_listener != null) _listener.Finish(null, true);
        }

        private static void Clear(RebindListener l) { if (_listener == l) _listener = null; }

        private sealed class RebindListener : MonoBehaviour
        {
            private Action<string> _onPath;
            private Action _onCancel;
            private int _startFrame;
            private bool _navWas = true, _finished;
            private int _restoreAtFrame;

            public void Begin(Action<string> onPath, Action onCancel)
            {
                _onPath = onPath; _onCancel = onCancel; _startFrame = Time.frameCount;
                var es = UnityEngine.EventSystems.EventSystem.current;
                if (es != null) { _navWas = es.sendNavigationEvents; es.sendNavigationEvents = false; }
            }

            private void Update()
            {
                if (_finished)
                {
                    // Navigation comes back a frame after the capture ends, so the Escape that cancelled does not also go "back".
                    if (Time.frameCount >= _restoreAtFrame) { Restore(); Destroy(gameObject); }
                    return;
                }
                if (Time.frameCount <= _startFrame) return;   // the press that started the capture is not the answer
                var kb = Keyboard.current;
                if (kb != null && kb.anyKey.wasPressedThisFrame)
                {
                    if (kb.escapeKey.wasPressedThisFrame) { Finish(null, true); return; }
                    foreach (var k in kb.allKeys)
                    {
                        if (!k.wasPressedThisFrame || k.keyCode == Key.None || k.keyCode == Key.IMESelected) continue;
                        Finish("<Keyboard>/" + k.name, false);
                        return;
                    }
                }
                var m = Mouse.current;
                if (m == null) return;
                if (m.leftButton.wasPressedThisFrame) Finish("<Mouse>/leftButton", false);
                else if (m.rightButton.wasPressedThisFrame) Finish("<Mouse>/rightButton", false);
                else if (m.middleButton.wasPressedThisFrame) Finish("<Mouse>/middleButton", false);
                else if (m.backButton.wasPressedThisFrame) Finish("<Mouse>/backButton", false);
                else if (m.forwardButton.wasPressedThisFrame) Finish("<Mouse>/forwardButton", false);
            }

            public void Finish(string path, bool cancelled)
            {
                if (_finished) return;
                _finished = true;
                _restoreAtFrame = Time.frameCount + 2;
                Clear(this);
                if (cancelled) _onCancel?.Invoke(); else _onPath?.Invoke(path);
            }

            private void Restore()
            {
                var es = UnityEngine.EventSystems.EventSystem.current;
                if (es != null) es.sendNavigationEvents = _navWas;
            }

            private void OnDestroy()
            {
                if (!_finished) { _finished = true; Restore(); Clear(this); }
            }
        }
    }
}
