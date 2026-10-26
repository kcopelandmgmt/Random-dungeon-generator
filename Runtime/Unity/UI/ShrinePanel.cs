using System.Linq;
using DungeonGen.Core.Net;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonGen.Unity.UI
{
    /// <summary>
    /// The shrine panel, on the HUD canvas: while the local player stands in a shrine room it lists what the shrine offers them now, with the
    /// price in souls and a button for each: Arm ultimate (two souls, once armed it says so), Resurrect for every dead teammate (two souls, one at
    /// the cheap-shrine event's shrine), and the consumables for one soul each. A choice that cannot be had is greyed with the reason under it
    /// (the shrines are dark, not enough souls, already armed, already resurrected once, left for the hub, downed, busy). Clicks queue in
    /// <see cref="PendingShrineActions"/>, which the input controller copies into the next intent; the server decides.
    /// Reads the host's <see cref="MatchServer.ShrineOptionsFor"/>; on a remote client the same rules (<see cref="ShrineRules.OptionsFor"/>) over the
    /// team's mirrored state. Hidden while paused, at the summary or recap, and while the inventory is open (they share the left side).
    /// Tests drive it with SendMessage("RefreshNow") and find the buttons by name: ShrineArm, ShrineRes-{player}, ShrineBuy-{index}.
    /// </summary>
    public sealed class ShrinePanel : MonoBehaviour
    {
        public ClientWorldView World;
        public DungeonMatchBootstrap Bootstrap;
        public ServerSimulation Server;
        public int LocalPlayerId = GameFlow.LocalPlayerId;
        [Tooltip("Seconds between refreshes.")]
        public float RefreshSeconds = 0.1f;

        public const string PanelName = "ShrinePanel";
        private const float Width = 560f, Inner = 508f;

        /// <summary>Online: this process's player id (the server gave it).</summary>
        public void SetLocalPlayerId(int id) => LocalPlayerId = id;

        public bool Visible => _panel != null && _panel.gameObject.activeSelf;
        /// <summary>What the panel last drew.</summary>
        public ShrineOptions Options { get; private set; }

        private RectTransform _panel;
        private GameHud _hud;
        private ItemPanels _items;
        private float _nextRefresh;
        private string _sig;

        /// <summary>Put the panel on the HUD canvas (<see cref="UiInstaller.InstallHud"/> calls this).</summary>
        public static ShrinePanel Install(GameObject hudCanvas, ClientWorldView world, DungeonMatchBootstrap bootstrap, ServerSimulation server)
        {
            var p = hudCanvas.GetComponent<ShrinePanel>();
            if (p == null) p = hudCanvas.AddComponent<ShrinePanel>();
            p.World = world; p.Bootstrap = bootstrap; p.Server = server;
            return p;
        }

        private MatchServer Host => Server != null ? Server.Server : null;
        private ClientMirror Mirror => Host == null ? ClientMirror.Current : null;

        private void Awake()
        {
            if (World == null) World = FindFirstObjectByType<ClientWorldView>();
            if (Bootstrap == null) Bootstrap = FindFirstObjectByType<DungeonMatchBootstrap>();
            if (Server == null) Server = FindFirstObjectByType<ServerSimulation>();
            PendingShrineActions.Clear();
            _panel = UiStyle.Panel(transform, PanelName);
            _panel.Place(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(36f, 250f), new Vector2(Width, 200f));
            _panel.gameObject.SetActive(false);
        }

        private void OnDestroy() => PendingShrineActions.Clear();

        private void Update()
        {
            if (_hud == null) _hud = GetComponent<GameHud>();
            if (_items == null) _items = GetComponent<ItemPanels>();
            bool blocked = (_hud != null && (_hud.Paused || _hud.SummaryVisible || _hud.RecapVisible)) || (_items != null && _items.InventoryOpen);
            if (blocked) { Hide(); return; }
            if (Time.unscaledTime >= _nextRefresh) RefreshNow();
        }

        private void Hide() { if (_panel != null && _panel.gameObject.activeSelf) { _panel.gameObject.SetActive(false); _sig = null; } }

        /// <summary>What the shrine offers the local player now: the host asks its server; a remote client applies the same rules to its mirror.</summary>
        private ShrineOptions Read()
        {
            var host = Host;
            if (host != null) return host.Sim != null && host.State.PlayerTeam.ContainsKey(LocalPlayerId) ? host.ShrineOptionsFor(LocalPlayerId) : null;
            var mirror = Mirror;
            var st = Bootstrap != null ? Bootstrap.State : null;
            if (mirror == null || st == null) return null;
            LocalPlayerId = mirror.PlayerId;
            var self = mirror.Self;
            if (!self.HasValue) return null;
            string busy = self.Value.DownedRemaining > 0f ? "downed" : !string.IsNullOrEmpty(self.Value.ChannelName) ? "busy channelling" : null;
            int room = st.PlayerRoom.TryGetValue(LocalPlayerId, out int r) ? r : -1;
            return ShrineRules.OptionsFor(st, LocalPlayerId, room, busy);
        }

        /// <summary>Read the options now and redraw if anything changed.</summary>
        public void RefreshNow()
        {
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            var o = Read();
            Options = o;
            if (o == null || !o.AtShrine || o.Refusal == "not in the round") { Hide(); return; }
            _panel.gameObject.SetActive(true);
            string sig = $"{o.Souls}#{o.Refusal}#" + string.Join("|", o.All.Select(c => $"{c.Action}:{c.Arg}:{c.Cost}:{c.Refusal}"));
            if (sig == _sig) return;
            _sig = sig;
            Draw(o);
        }

        private void Draw(ShrineOptions o)
        {
            for (int i = _panel.childCount - 1; i >= 0; i--)
            {
                var c = _panel.GetChild(i);
                if (c.name == "Frame") continue;
                c.SetParent(null, false);
                Destroy(c.gameObject);
            }
            var t = UiStyle.Label(_panel, "Title", "SHRINE", 30, UiStyle.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            t.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(26f, -14f), new Vector2(240f, 40f));
            var souls = UiStyle.Label(_panel, "ShrineSouls", $"Souls  <b>{o.Souls}</b>", 22, o.Souls > 0 ? UiStyle.Gold : UiStyle.TextDim, TextAnchor.MiddleRight);
            souls.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-26f, -16f), new Vector2(240f, 36f));
            UiStyle.Rule(_panel, "Rule", Inner, -62f);
            float y = -72f;
            if (o.Refusal != null)
            {
                var why = UiStyle.Label(_panel, "ShrineRefusal", Capital(o.Refusal) + ".", 20, UiStyle.Bad, TextAnchor.MiddleLeft);
                why.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(26f, y), new Vector2(Inner, 30f));
                y -= 36f;
            }

            Row("ShrineArm", "Arm ultimate", o.Arm.Refusal == "already armed" ? UiStyle.Colour("armed: R to cast, one use", UiStyle.Good) : $"{o.Arm.Cost} souls · one use, lost if you die", o.Arm,
                "Arm", () => PendingShrineActions.Arm(), ref y);

            Section("RESURRECT", ref y);
            if (o.Resurrect.Count == 0) { Note("ShrineNoDead", "No fallen teammate.", ref y); }
            foreach (var c in o.Resurrect)
            {
                int who = c.Arg;
                Row("ShrineRes-" + who, c.Label.Replace("Resurrect ", ""), $"{c.Cost} soul{(c.Cost == 1 ? "" : "s")} · returns naked, once a round", c, "Resurrect", () => PendingShrineActions.Resurrect(who), ref y);
            }

            Section("OFFERINGS", ref y);
            foreach (var c in o.Buy)
            {
                int index = c.Arg;
                Row("ShrineBuy-" + index, c.Label, $"{c.Cost} soul", c, "Buy", () => PendingShrineActions.Buy(index), ref y);
            }
            _panel.sizeDelta = new Vector2(Width, -y + 14f);
        }

        private void Section(string title, ref float y)
        {
            y -= 6f;
            var s = UiStyle.Label(_panel, "Section-" + title, title, 17, UiStyle.GoldDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            s.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(26f, y), new Vector2(Inner, 24f));
            y -= 28f;
        }

        private void Note(string name, string text, ref float y)
        {
            var n = UiStyle.Label(_panel, name, text, 18, UiStyle.TextFaint, TextAnchor.MiddleLeft);
            n.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(26f, y), new Vector2(Inner, 26f));
            y -= 30f;
        }

        /// <summary>One choice: its name, the price (or why not, in red), and a button that queues it when it can be had.</summary>
        private void Row(string name, string title, string price, ShrineChoice choice, string verb, System.Action queue, ref float y)
        {
            var row = UiStyle.Node(_panel, name + "-Row");
            row.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(26f, y), new Vector2(Inner, 52f));
            var head = UiStyle.Label(row, name + "-Name", title, 21, choice.Available ? UiStyle.TextMain : UiStyle.TextDim, TextAnchor.MiddleLeft);
            head.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(Inner - 150f, 28f));
            bool armed = choice.Action == ShrineAction.ArmUltimate && choice.Refusal == "already armed";
            string sub = choice.Available || armed ? price : UiStyle.Colour(Capital(choice.Refusal), UiStyle.Bad) + UiStyle.Colour("  ·  " + price, UiStyle.TextFaint);
            var note = UiStyle.Label(row, name + "-Note", sub, 16, UiStyle.TextDim, TextAnchor.MiddleLeft);
            note.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -27f), new Vector2(Inner - 150f, 24f));
            var b = UiStyle.MakeButton(row, name, armed ? "Armed" : verb, () => { if (choice.Available) { queue(); UiSound.Play(UiSound.Confirm); } }, 19);
            ((RectTransform)b.transform).Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(136f, 42f));
            b.interactable = choice.Available;
            if (armed) b.SetSelected(true);
            y -= 58f;
        }

        private static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
