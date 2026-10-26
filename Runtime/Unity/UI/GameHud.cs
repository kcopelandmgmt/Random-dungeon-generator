using System.Collections.Generic;
using System.IO;
using System.Linq;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Items;
using DungeonGen.Core.Net;
using DungeonGen.Core.Profile;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonGen.Unity.UI
{
    /// <summary>
    /// The in-round HUD, uGUI on its own canvas: health and resource bars, the five ability slots (LMB RMB Q E R) with a
    /// cooldown sweep and the locked ultimate, two consumable slots (1, 2), the channel bar, the downed timer, the round
    /// clock and extracts timer, souls and silver, the spawn countdown, the announcement banner, the death recap, the round
    /// summary, and the pause menu on Esc. It draws from the local player's snapshot (<see cref="ClientWorldView.Self"/>) and
    /// the mirrored state; the pieces the host reads off its server (last hits, pockets, the settlement report) come from the host's server and
    /// <see cref="GameFlow"/>, and on a remote client from its own snapshot and the <see cref="ClientMirror"/> (pockets and cooldowns in the
    /// <see cref="SelfSnapshot"/>, the recap in the server's <see cref="RoundResult"/>). The greybox OnGUI <see cref="PlayerHud"/> stands down while this exists; the compass and
    /// minimap (OnGUI) stay.
    /// Tests drive it with SendMessage: TogglePause, ContinueToSummary, ReturnToMenu, ShowSummaryNow.
    /// </summary>
    public sealed class GameHud : MonoBehaviour, IRoundUi
    {
        public ClientWorldView World;
        public DungeonMatchBootstrap Bootstrap;
        public ServerSimulation Server;
        public int LocalPlayerId = GameFlow.LocalPlayerId;
        public float AnnouncementSeconds = 5f;
        [Tooltip("Seconds after an extraction before the summary opens.")]
        public float SummaryDelay = 1.5f;

        public bool Paused { get; private set; }
        public bool RecapVisible => _recap != null && _recap.gameObject.activeSelf;
        public bool SummaryVisible => _summary != null;

        private static readonly string[] Keys = { "LMB", "RMB", "Q", "E", "R" };

        private RectTransform _root;
        private UiBar _health, _resource, _channel;
        private Text _healthText;
        private readonly Slot[] _slots = new Slot[5];
        private readonly Slot[] _pockets = new Slot[2];
        private Text _clock, _extracts, _purse, _countdown, _banner, _downed, _downedSub;
        private RectTransform _bannerPanel, _channelRoot, _downedRoot, _recap, _summary, _pause, _pauseSettings;
        private Text _recapTitle, _recapBody;
        private Button _recapButton;
        private float _announcedAt = -100f;
        private string _spec; private List<AbilityDefinition> _book;
        private readonly float[] _cdMax = new float[5];
        private int _lastCountdown = -1;
        private float _outAt = -1f; private bool _recapDismissed;
        /// <summary>The round is over for the local player, or a summary or the pause menu is up: the in-round overlays (downed banner, channel bar, announcements) stand down.</summary>
        private bool _hideRoundOverlays;
        private readonly List<MonoBehaviour> _silenced = new List<MonoBehaviour>();
        private bool _modal;

        private sealed class Slot
        {
            public RectTransform Root; public Image Icon, Sweep, Lock, Frame; public Text Key, Seconds, Name, Count; public string IconFor;
        }

        // ---------------- build ----------------

        private void Awake()
        {
            UiStyle.MakeCanvas(gameObject, 10);
            UiStyle.EnsureEventSystem();
            foreach (var old in FindObjectsByType<PlayerHud>(FindObjectsSortMode.None)) old.enabled = false;   // the greybox HUD stands down
            if (World == null) World = FindFirstObjectByType<ClientWorldView>();
            if (Bootstrap == null) Bootstrap = FindFirstObjectByType<DungeonMatchBootstrap>();
            if (Server == null) Server = FindFirstObjectByType<ServerSimulation>();
            Build();
        }

        private void OnDestroy()
        {
            if (Paused) GameFlow.SetRoundPaused(false);   // never leave the game frozen behind a closed HUD
        }

        private IMatchTransport _transport;

        /// <summary>Online: this process's player id (the server gave it). Sent by <see cref="OnlineMatch"/>.</summary>
        public void SetLocalPlayerId(int id) => LocalPlayerId = id;

        /// <summary>A remote client's mirror of what the host reads off its server (null offline and when hosting).</summary>
        private static ClientMirror Mirror => ClientMirror.Current;

        public void Attach(IMatchTransport transport)
        {
            if (transport == null || ReferenceEquals(transport, _transport)) return;   // attached twice (SendMessage reaches every component on the object): once is enough
            _transport = transport;
            transport.EventReceived += bytes =>
            {
                if (this == null) return;
                using var ms = new MemoryStream(bytes); using var r = new BinaryReader(ms);
                var e = MatchEvent.Read(r);
                if (Bootstrap?.State == null) return;
                var a = Announcer.For(e, Bootstrap.State.Layout);
                if (a.HasValue) Announce(a.Value.Text);
            };
        }

        public void Announce(string text)
        {
            if (_banner == null || string.IsNullOrEmpty(text)) return;
            _banner.text = text; _announcedAt = Time.time;
        }

        private void Build()
        {
            _root = UiStyle.Node(transform, "HUD"); _root.Stretch();

            // Vitals and abilities, bottom centre: bars on top, then the five abilities and the two pockets, each with its caption under it.
            var bottom = UiStyle.Node(_root, "Bottom");
            bottom.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(860f, 244f));
            var plate = UiStyle.Panel(bottom, "Plate", new Color(0.05f, 0.045f, 0.05f, 0.80f), true, false);
            plate.Stretch();
            _health = new UiBar(bottom, "Health", new Vector2(800f, 34f), UiStyle.Health) { TrailEnabled = true };
            _health.Root.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(800f, 34f));
            _healthText = UiStyle.Label(_health.Root, "Label", "", UiStyle.Size.Label, UiStyle.TextMain, TextAnchor.MiddleCenter, FontStyle.Bold);
            _healthText.rectTransform.Stretch();
            _resource = new UiBar(bottom, "Resource", new Vector2(800f, 14f), UiStyle.Resource) { TrailEnabled = true };
            _resource.Root.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(800f, 14f));
            for (int i = 0; i < 5; i++)
            {
                _slots[i] = MakeSlot(bottom, "Ability-" + Keys[i], Keys[i], 96f);
                _slots[i].Root.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-315f + i * 108f, 56f), new Vector2(96f, 96f));
            }
            for (int i = 0; i < 2; i++)
            {
                _pockets[i] = MakeSlot(bottom, "Pocket-" + (i + 1), (i + 1).ToString(), 68f);
                _pockets[i].Root.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(249f + i * 80f, 56f), new Vector2(68f, 68f));
                _pockets[i].Name.text = i == 0 ? "Potion" : "Antidote";
            }

            // Round clock, top right: as wide as its longest line needs and no more.
            var clock = UiStyle.Panel(_root, "RoundPanel", null, true, false);
            clock.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(300f, 118f));
            _clock = UiStyle.Label(clock, "Clock", "20:00", UiStyle.Size.Title, UiStyle.TextMain, TextAnchor.MiddleRight, FontStyle.Bold);
            _clock.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -6f), new Vector2(264f, 50f));
            _extracts = UiStyle.Label(clock, "Extracts", "", UiStyle.Size.Body, UiStyle.TextSoft, TextAnchor.MiddleRight);
            _extracts.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -56f), new Vector2(276f, 26f));
            _extracts.horizontalOverflow = HorizontalWrapMode.Overflow;
            _purse = UiStyle.Label(clock, "Purse", "", UiStyle.Size.Body, UiStyle.TextMain, TextAnchor.MiddleRight);
            _purse.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -84f), new Vector2(276f, 26f));
            _purse.horizontalOverflow = HorizontalWrapMode.Overflow;

            // Announcement banner, under the compass.
            _bannerPanel = UiStyle.Panel(_root, "Banner", new Color(0.05f, 0.04f, 0.04f, 0.78f), true, false);
            _bannerPanel.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -64f), new Vector2(900f, 58f));
            _banner = UiStyle.Label(_bannerPanel, "Text", "", UiStyle.Size.Heading, UiStyle.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            _banner.rectTransform.Stretch(16f, 16f, 0f, 0f);

            // Spawn countdown, centre.
            _countdown = UiStyle.Label(_root, "Countdown", "", UiStyle.Size.Title, UiStyle.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            _countdown.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 250f), new Vector2(900f, 60f));

            // Channel bar, under the character.
            _channelRoot = UiStyle.Node(_root, "Channel");
            _channelRoot.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -150f), new Vector2(420f, 60f));
            var chName = UiStyle.Label(_channelRoot, "Name", "", UiStyle.Size.Label, UiStyle.TextMain, TextAnchor.MiddleCenter);
            chName.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(420f, 32f));
            _channel = new UiBar(_channelRoot, "Bar", new Vector2(420f, 20f), UiStyle.Channel);
            _channel.Root.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(420f, 20f));

            // Downed: short enough to sit clear of the recap card on the left.
            _downedRoot = UiStyle.Node(_root, "Downed");
            _downedRoot.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 230f), new Vector2(520f, 112f));
            _downed = UiStyle.Label(_downedRoot, "Title", "DOWNED", UiStyle.Size.Hero, UiStyle.Bad, TextAnchor.MiddleCenter, FontStyle.Bold);
            _downed.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(520f, 66f));
            _downedSub = UiStyle.Label(_downedRoot, "Sub", "", UiStyle.Size.Heading, UiStyle.TextMain, TextAnchor.MiddleCenter);
            _downedSub.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(520f, 40f));

            // Death recap (built hidden).
            _recap = UiStyle.Panel(_root, "DeathRecapPanel");
            _recap.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(60f, 20f), new Vector2(620f, 470f));
            _recapTitle = UiStyle.Label(_recap, "Title", "YOU DIED", UiStyle.Size.Title, UiStyle.Bad, TextAnchor.MiddleLeft, FontStyle.Bold);
            _recapTitle.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, -22f), new Vector2(560f, 56f));
            UiStyle.Rule(_recap, "Rule", 552f, -88f);
            _recapBody = UiStyle.Label(_recap, "Body", "", UiStyle.Size.Body, UiStyle.TextMain, TextAnchor.UpperLeft);
            _recapBody.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, -104f), new Vector2(556f, 270f));
            _recapBody.lineSpacing = 1.1f;
            _recapButton = UiStyle.MakeButton(_recap, "ContinueButton", "Continue", OnRecapButton);
            ((RectTransform)_recapButton.transform).Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-30f, 28f), new Vector2(260f, 60f));
            _recap.gameObject.SetActive(false);

            _bannerPanel.gameObject.SetActive(false);
            _channelRoot.gameObject.SetActive(false);
            _downedRoot.gameObject.SetActive(false);
        }

        private Slot MakeSlot(Transform parent, string name, string key, float size)
        {
            var s = new Slot();
            var bg = UiStyle.Img(parent, name, new Color(0.10f, 0.085f, 0.08f, 0.95f), UiStyle.Rounded);
            s.Root = bg.rectTransform;
            s.Icon = UiStyle.Img(s.Root, "Icon", Color.white, null);   // the ability's baked icon (AbilityIcons); under the sweep, lock and frame
            s.Icon.rectTransform.Stretch(4f, 4f, 4f, 4f); s.Icon.preserveAspect = true; s.Icon.enabled = false;
            s.Sweep = UiStyle.Img(s.Root, "Sweep", new Color(0f, 0f, 0f, 0.7f), UiStyle.White);
            s.Sweep.rectTransform.Stretch(3f, 3f, 3f, 3f);
            s.Sweep.type = Image.Type.Filled; s.Sweep.fillMethod = Image.FillMethod.Radial360; s.Sweep.fillOrigin = (int)Image.Origin360.Top; s.Sweep.fillClockwise = false; s.Sweep.fillAmount = 0f;
            s.Lock = UiStyle.Img(s.Root, "Lock", new Color(0.02f, 0.02f, 0.03f, 0.82f), UiStyle.Rounded);
            s.Lock.rectTransform.Stretch(); s.Lock.enabled = false;
            s.Frame = UiStyle.Img(s.Root, "Frame", UiStyle.GoldDim, UiStyle.Frame);
            s.Frame.rectTransform.Stretch();
            s.Key = UiStyle.Label(s.Root, "Key", key, size > 80f ? UiStyle.Size.Small : UiStyle.Size.Caption, UiStyle.Gold, TextAnchor.UpperLeft, FontStyle.Bold);
            s.Key.rectTransform.Stretch(7f, 4f, 3f, 4f);
            var keyShadow = s.Key.gameObject.AddComponent<Shadow>(); keyShadow.effectColor = new Color(0f, 0f, 0f, 0.9f); keyShadow.effectDistance = new Vector2(1.5f, -1.5f);   // readable over a bright icon
            s.Seconds = UiStyle.Label(s.Root, "Seconds", "", size > 80f ? UiStyle.Size.Heading : UiStyle.Size.Label, UiStyle.TextMain, TextAnchor.MiddleCenter, FontStyle.Bold);
            s.Seconds.rectTransform.Stretch(2f, 2f, 10f, 2f);
            s.Count = UiStyle.Label(s.Root, "Count", "", UiStyle.Size.Body, UiStyle.TextMain, TextAnchor.LowerRight, FontStyle.Bold);
            s.Count.rectTransform.Stretch(4f, 7f, 4f, 4f);
            s.Name = UiStyle.Label(s.Root, "Name", "", UiStyle.Size.Caption, UiStyle.TextSoft, TextAnchor.UpperCenter);
            s.Name.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(size + 24f, 24f));
            s.Name.horizontalOverflow = HorizontalWrapMode.Overflow;
            return s;
        }

        // ---------------- per frame ----------------

        private MatchServer Host => Server != null ? Server.Server : null;

        private void Update()
        {
            if (PausePressed()) TogglePause();
            if (Mirror != null && Host == null) LocalPlayerId = Mirror.PlayerId;
            var self = World != null ? World.Self : null;
            var st = Bootstrap != null ? Bootstrap.State : null;
            bool haveSelf = self.HasValue;
            // Once the player's round is over (dead, extracted), or a summary or the pause menu is up, the in-round overlays stand down.
            bool outOfRound = st != null && st.PlayerTeam.ContainsKey(LocalPlayerId) && RoundSettlement.OutcomeOf(st, LocalPlayerId) != null;
            _hideRoundOverlays = outOfRound || SummaryVisible || Paused;
            _root.Find("Bottom").gameObject.SetActive(haveSelf && !SummaryVisible);
            if (haveSelf) DrawVitals(self.Value);
            DrawRound(st);
            DrawEnd(st, self);
            bool panelOpen = Paused || SummaryVisible || RecapVisible;
            _countdown.gameObject.SetActive(!panelOpen);
            _bannerPanel.gameObject.SetActive(Time.time - _announcedAt < AnnouncementSeconds && !_hideRoundOverlays && !RecapVisible);
            UpdateModal();
        }

        private void DrawVitals(SelfSnapshot s)
        {
            float hf = s.MaxHealth > 0f ? s.Health / s.MaxHealth : 0f;
            _health.Set(hf, null, hf < 0.3f ? UiStyle.HealthLow : UiStyle.Health);
            _healthText.text = $"{Mathf.CeilToInt(s.Health)} / {Mathf.CeilToInt(s.MaxHealth)}";
            _resource.Set(s.MaxResource > 0f ? s.Resource / s.MaxResource : 0f, null);

            var spec = Host?.SpecOf(LocalPlayerId) ?? (!string.IsNullOrEmpty(s.Spec) ? s.Spec : null) ?? PracticeLaunch.Current?.Loadout?.Spec ?? OnlineLaunch.Current?.Loadout?.Spec;
            if (spec != _spec) { _spec = spec; _book = spec == null ? new List<AbilityDefinition>() : AbilityBook.For(spec).OrderBy(a => a.Kind).ToList(); }
            for (int i = 0; i < 5; i++)
            {
                var slot = _slots[i];
                float cd = s.CooldownRemaining != null && i < s.CooldownRemaining.Length ? s.CooldownRemaining[i] : 0f;
                if (cd > _cdMax[i] || cd <= 0f) _cdMax[i] = cd;
                slot.Name.text = _book != null && i < _book.Count ? _book[i].Name : "";
                var iconId = _book != null && i < _book.Count ? _book[i].Id : null;
                if (iconId != slot.IconFor) { slot.IconFor = iconId; slot.Icon.sprite = AbilityIcons.Get(iconId); slot.Icon.enabled = slot.Icon.sprite != null; }
                bool locked = i == 4 && !s.UltimateArmed && !(Host?.Sim?.DevCasting ?? false);
                slot.Lock.enabled = locked;
                slot.Sweep.fillAmount = !locked && cd > 0f && _cdMax[i] > 0f ? cd / _cdMax[i] : 0f;
                slot.Seconds.text = locked ? $"<size=16>{FightRules.SoulsToArmUltimate} souls\nat a shrine</size>" : cd > 0f ? (cd >= 10f ? cd.ToString("0") : cd.ToString("0.0")) : "";
                slot.Frame.color = i == 4 && !locked ? UiStyle.Gold : locked ? UiStyle.GoldFaint : UiStyle.GoldDim;
                slot.Key.color = locked ? UiStyle.TextFaint : UiStyle.Gold;
            }
            var host = Host;
            for (int i = 0; i < 2; i++)
            {
                var slot = _pockets[i];
                var c = Consumable.ByName(GameFlow.PocketConsumables[i]);
                int n = 0; float cd = 0f;
                if (host != null && c != null)
                {
                    n = host.PocketsOf(LocalPlayerId).TryGetValue(c.Name, out int k) ? k : 0;
                    cd = host.ConsumableCooldownRemaining(LocalPlayerId, c);
                }
                else if (c != null && s.Pockets != null)
                {
                    // A remote client: the pockets and cooldowns come in its own snapshot.
                    n = s.PocketCount(c.Name);
                    cd = s.ConsumableCooldown(c.Name);
                }
                if (c != null && slot.IconFor != c.Name)
                {   // the consumable's painted icon (ItemIcons); the lock overlay dims it when the pocket is empty
                    slot.IconFor = c.Name;
                    slot.Icon.sprite = ItemIcons.For(c.Name);
                    slot.Icon.enabled = slot.Icon.sprite != null;
                }
                slot.Count.text = n.ToString();
                slot.Count.color = n > 0 ? UiStyle.TextMain : UiStyle.Bad;
                slot.Lock.enabled = n == 0;
                slot.Sweep.fillAmount = c != null && cd > 0f && c.CooldownSeconds > 0f ? Mathf.Clamp01(cd / c.CooldownSeconds) : 0f;
                slot.Seconds.text = cd > 0f ? cd.ToString("0") : "";
            }

            bool channel = !string.IsNullOrEmpty(s.ChannelName) && !_hideRoundOverlays && !RecapVisible;   // never behind a recap or a summary card
            _channelRoot.gameObject.SetActive(channel);
            if (channel) { _channel.Set(s.ChannelFraction, null); _channelRoot.Find("Name").GetComponent<Text>().text = ChannelLabel(s.ChannelName); }

            // The bleed-out timer shows while you are down and nothing says it is over: not after death, not under a summary or the pause menu.
            bool downed = s.DownedRemaining > 0f && !_hideRoundOverlays;
            _downedRoot.gameObject.SetActive(downed);
            if (downed) _downedSub.text = $"Bleeding out in {Mathf.CeilToInt(s.DownedRemaining)} s";
        }

        private static string ChannelLabel(string kind)
        {
            switch (kind)
            {
                case "extract": return "Extracting…";
                case "revive": return "Reviving…";
                case "loot": return "Looting…";
                default: return char.ToUpperInvariant(kind[0]) + kind.Substring(1) + "…";
            }
        }

        private void DrawRound(MatchState st)
        {
            if (st == null) { _clock.text = ""; _extracts.text = ""; _purse.text = ""; _countdown.text = ""; return; }
            var rules = st.Descriptor.Rules;
            float now = Host == null && World != null && World.Latest != null ? Mathf.Max(st.Time, World.Latest.Time) : st.Time;   // a client's view only moves on events; its snapshots tick
            float remaining = Mathf.Max(0f, rules.RoundSeconds - now);
            _clock.text = UiStyle.Clock(remaining);
            _clock.color = remaining < 60f ? UiStyle.Bad : UiStyle.TextMain;
            _extracts.text = st.BossExitOpen ? UiStyle.Colour("Extracts open  ·  boss exit open", UiStyle.Good)
                : st.ExtractsOpen ? UiStyle.Colour("Extracts open", UiStyle.Good)
                : "Extracts open in " + UiStyle.Clock(rules.ExtractsOpenAfterSeconds - now);
            int souls = st.SoulsOf(LocalPlayerId);
            int silver = st.PlayerSilver.TryGetValue(LocalPlayerId, out int sv) ? sv : 0;
            _purse.text = $"Souls {UiStyle.Colour(souls.ToString(), new Color(0.62f, 0.78f, 1f))}     Silver {UiStyle.Colour(silver.ToString(), UiStyle.Gold)}";

            // Spawn countdown: the doors open at 10 s; a tick each second.
            if (!st.DoorsOpen && st.Started)
            {
                int left = Mathf.CeilToInt(FightRules.StartCountdownSeconds - now);
                _countdown.text = left > 0 ? $"The doors open in {left}" : "";
                if (left != _lastCountdown && left > 0 && left <= 5) UiSound.Play(UiSound.CountdownTick);
                _lastCountdown = left;
            }
            else _countdown.text = "";
        }

        // ---------------- end of the round ----------------

        private void DrawEnd(MatchState st, SelfSnapshot? self)
        {
            if (st == null || !st.PlayerTeam.ContainsKey(LocalPlayerId)) return;
            var outcome = RoundSettlement.OutcomeOf(st, LocalPlayerId);
            bool downed = self.HasValue && self.Value.DownedRemaining > 0f;
            if (outcome == null)
            {
                if (downed && !Paused) ShowRecap(false);
                else if (_recap.gameObject.activeSelf) _recap.gameObject.SetActive(false);
                return;
            }
            if (_outAt < 0f) _outAt = Time.time;
            if (SummaryVisible) return;
            var flow = GameFlow.Instance;
            bool flowRound = flow != null && (flow.Phase == FlowPhase.InRound || flow.Phase == FlowPhase.RoundOver);
            if (flowRound && flow.LastReport == null && flow.Phase == FlowPhase.InRound) return;   // settlement lands this frame or next
            if (outcome == RoundOutcome.Died && !_recapDismissed) { ShowRecap(true); return; }
            if (outcome == RoundOutcome.Extracted && Time.time - _outAt < SummaryDelay) return;
            ShowSummaryNow();
        }

        private void ShowRecap(bool dead)
        {
            if (!_recap.gameObject.activeSelf) _recap.gameObject.SetActive(true);
            var host = Host;
            var st = Bootstrap.State;
            DeathRecap recap = null;
            var result = host == null ? Mirror?.Result : null;
            if (dead) recap = (host != null ? DeathRecap.For(st, LocalPlayerId, host.Sim?.Hits, MonsterName) : null) ?? GameFlow.Instance?.LastRecap ?? result?.Recap;   // built here so a monster killer is named
            var hits = recap?.LastHits ?? (host?.Sim != null ? host.Sim.Hits.LastHits(LocalPlayerId) : (IReadOnlyList<Hit>)System.Array.Empty<Hit>());
            bool clock = dead && (result != null ? result.DiedToClock : st.Ended && !st.Log.Any(e => (e.Kind == MatchEventKind.PlayerDied || e.Kind == MatchEventKind.PlayerFinished) && e.A == LocalPlayerId));
            _recapTitle.text = !dead ? "DOWNED" : clock ? "THE CLOCK RAN OUT" : "YOU DIED";
            _recapTitle.color = UiStyle.Bad;
            var sb = new System.Text.StringBuilder();
            if (!dead) sb.Append("You are down. A teammate can revive you before you bleed out.\n\n");
            else if (clock) sb.Append("The round ended with you still inside. Everything you carried is gone.\n\n");
            else if (recap != null)
            {
                sb.Append(UiStyle.Colour(recap.Headline, UiStyle.TextMain)).Append('\n');
                if (recap.DownedSeconds > 0f) sb.Append(UiStyle.Colour($"Downed for {recap.DownedSeconds:0} s before the end.", UiStyle.TextDim)).Append('\n');
                sb.Append('\n');
            }
            if (hits.Count > 0)
            {
                sb.Append(UiStyle.Colour("LAST HITS", UiStyle.GoldDim)).Append('\n');
                float now = host == null && World != null && World.Latest != null ? Mathf.Max(st.Time, World.Latest.Time) : st.Time;
                var list = hits.ToList();
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var h = list[i];
                    string who = HitSource(h, i, host == null && result != null && i < result.HitAttackers.Count ? result.HitAttackers[i] : null);
                    sb.Append($"{UiStyle.Colour(h.Amount.ToString("0"), UiStyle.Bad)} {h.Type}  ·  {Pretty(h.Ability)}  ·  {who}  {UiStyle.Colour($"{Mathf.Max(0f, now - h.Time):0.0} s ago", UiStyle.TextDim)}\n");
                }
            }
            else if (dead && !clock) sb.Append(UiStyle.Colour("No hits recorded.", UiStyle.TextDim));
            _recapBody.text = sb.ToString();
            _recapButton.SetLabel(dead ? "Continue" : "Give up");
        }

        private string AttackerName(int id)
        {
            var c = Host?.Sim?.Get(id);
            if (c == null) return "something unseen";
            return c.IsMob ? c.Name : c.Class?.Name ?? "a player";
        }

        /// <summary>The monster's or boss's name for a combatant id (the host reads it off its sim); null for a player or an id the sim no longer has.</summary>
        private string MonsterName(int id)
        {
            var c = Host?.Sim?.Get(id);
            return c != null && c.IsMob ? c.Name : null;
        }

        /// <summary>Who dealt a hit, for the recap line: yourself, a monster or boss by its name (the sim tags them "Mob" and "Boss"), a player by weapon spec.</summary>
        private string HitSource(Hit h, int index, string fromResult)
        {
            if (h.Attacker == LocalPlayerId) return "yourself";
            if (h.AttackerSpec == "Mob" || h.AttackerSpec == "Boss") return MonsterName(h.Attacker) ?? fromResult ?? "a monster";
            return h.AttackerSpec ?? fromResult ?? AttackerName(h.Attacker);
        }

        private static string Pretty(string ability)
        {
            if (string.IsNullOrEmpty(ability)) return "a hit";
            if (AbilityBook.TryGet(ability, out var a)) return a.Name;
            return char.ToUpperInvariant(ability[0]) + ability.Substring(1);   // a monster's own move ("crush"): not in the player's book
        }

        private void OnRecapButton()
        {
            var st = Bootstrap?.State;
            bool dead = st != null && RoundSettlement.OutcomeOf(st, LocalPlayerId) != null;
            if (dead) ContinueToSummary();
            else
            {
                // Give up: bleed out now. Counts as a death (settles through the flow when there is one).
                if (GameFlow.Instance != null && GameFlow.Instance.Phase == FlowPhase.InRound) GameFlow.Instance.ForfeitRound();
                else if (Host != null && st != null && st.PlayersInside.Contains(LocalPlayerId))
                    Host.Raise(new MatchEvent(MatchEventKind.PlayerDied, st.Time, LocalPlayerId, -1, Mathf.Max(0, Host.RoomOf(LocalPlayerId))));
            }
        }

        public void ContinueToSummary()
        {
            _recapDismissed = true;
            _recap.gameObject.SetActive(false);
            ShowSummaryNow();
        }

        /// <summary>Open the round summary for the local player (from the flow's settlement when there is one).</summary>
        public void ShowSummaryNow()
        {
            var st = Bootstrap?.State;
            if (SummaryVisible || st == null || !st.PlayerTeam.ContainsKey(LocalPlayerId)) return;
            if (Paused) TogglePause();
            _recap.gameObject.SetActive(false);
            var report = GameFlow.Instance?.LastReport;
            var summary = report?.Summary ?? RoundSummary.For(st, LocalPlayerId);
            _summary = SummaryPanel.Build(_root, summary, report, st, ReturnToMenu);
            UiSound.Play(summary.Extracted ? UiSound.Confirm : UiSound.Click);
        }

        public void ReturnToMenu()
        {
            GameFlow.Ensure().ReturnToMenu();
        }

        // ---------------- pause ----------------

        public void TogglePause()
        {
            if (SummaryVisible) return;
            Paused = !Paused;
            bool frozen = GameFlow.SetRoundPaused(Paused);   // offline the round stops; online it goes on
            if (Paused)
            {
                _pause = UiStyle.Panel(_root, "PausePanel");
                _pause.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 520f));
                var t = UiStyle.Label(_pause, "Title", "PAUSED", UiStyle.Size.Title, UiStyle.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
                t.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(500f, 60f));
                var sub = UiStyle.Label(_pause, "Sub", frozen ? "The dungeon waits for you." : "The dungeon does not wait: the round goes on.", UiStyle.Size.Body, UiStyle.TextDim, TextAnchor.MiddleCenter, FontStyle.Italic);
                sub.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -94f), new Vector2(500f, 30f));
                UiStyle.Rule(_pause, "Rule", 420f, -126f);
                PauseButton("ResumeButton", "Resume", -170f, TogglePause);
                PauseButton("SettingsButton", "Settings", -256f, OpenPauseSettings);
                PauseButton("LeaveButton", "Leave round", -342f, LeaveRound);
                var warn = UiStyle.Label(_pause, "Warn", "Leaving counts as a death: what you carry is lost.", UiStyle.Size.Body, UiStyle.Bad, TextAnchor.MiddleCenter);
                warn.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(500f, 30f));
            }
            else
            {
                if (_pause != null) Destroy(_pause.gameObject);
                if (_pauseSettings != null) Destroy(_pauseSettings.gameObject);
                _pause = null; _pauseSettings = null;
            }
        }

        private void PauseButton(string name, string label, float y, System.Action a)
        {
            var b = UiStyle.MakeButton(_pause, name, label, a, UiStyle.Size.Heading);
            ((RectTransform)b.transform).Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(380f, 68f));
        }

        private void OpenPauseSettings()
        {
            if (_pause != null) _pause.gameObject.SetActive(false);
            _pauseSettings = SettingsPanel.Build(_root, () =>
            {
                if (_pauseSettings != null) Destroy(_pauseSettings.gameObject);
                _pauseSettings = null;
                if (_pause != null) _pause.gameObject.SetActive(true);
            });
        }

        public void LeaveRound()
        {
            if (Paused) TogglePause();
            var flow = GameFlow.Instance;
            if (flow != null && flow.Phase == FlowPhase.InRound) { StartCoroutine(LeaveThenSummary(flow)); return; }
            GameFlow.Ensure().ReturnToMenu();   // a greybox round with no flow behind it: just go to the menu
        }

        /// <summary>Leaving counts as a death: forfeit, wait for the settlement (online, the server's result), then show the death summary; its button returns to the menu (2026-10-26 owner #7).</summary>
        private System.Collections.IEnumerator LeaveThenSummary(GameFlow flow)
        {
            flow.ForfeitRound();
            float t = Time.unscaledTime;
            while (flow.Phase == FlowPhase.InRound && Time.unscaledTime - t < 10f) yield return null;
            ShowSummaryNow();
            if (!SummaryVisible) flow.LeaveRound();   // nothing to show (no state): the old path, straight to the menu with a note
        }

        // ---------------- input while a panel is open ----------------

        /// <summary>While a panel is open the character stands still and clicks go to the panel, not to the Basic attack.</summary>
        private void UpdateModal()
        {
            bool modal = Paused || SummaryVisible || (_recap.gameObject.activeSelf && Bootstrap?.State != null && RoundSettlement.OutcomeOf(Bootstrap.State, LocalPlayerId) != null);
            if (modal == _modal) return;
            _modal = modal;
            if (modal)
            {
                foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                    if (mb is IIntentSource && mb.enabled) { mb.enabled = false; _silenced.Add(mb); }
                var host = Host;
                if (host != null && host.State.PlayersInside.Contains(LocalPlayerId)) host.SetIntent(LocalPlayerId, new PlayerIntent { PlayerId = LocalPlayerId });
                else if (host == null && _transport != null && !_transport.IsServer) _transport.SendIntent(new PlayerIntent { PlayerId = LocalPlayerId }.ToBytes());   // a client stops its character too
            }
            else
            {
                foreach (var mb in _silenced) if (mb != null) mb.enabled = true;
                _silenced.Clear();
            }
        }

        /// <summary>The Pause binding (Escape unless the player rebound it), read through <see cref="GameBindings"/>. Not while a key is being rebound, and not while the pause menu's own Settings card is open (there Back closes the card).</summary>
        private bool PausePressed()
        {
#if DUNGEONGEN_INPUTSYSTEM
            return !GameBindings.Capturing && _pauseSettings == null && GameBindings.WasPressedThisFrame(BindingAction.Pause);
#else
            return _pauseSettings == null && Input.GetKeyDown(KeyCode.Escape);
#endif
        }
    }
}
