using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Items;
using DungeonGen.Core.Profile;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonGen.Unity.UI
{
    /// <summary>
    /// The main menu scene's screens, built from code on this object's canvas: Main (play, characters, settings, quit, the
    /// account card) → Characters (choose or create) → Create (name and an unlocked class) → Prepare (spec, bot teams, the
    /// tier wall, Enter the dungeon) and Settings. Everything it does goes through <see cref="GameFlow"/>. With
    /// <c>-autotest</c> it skips straight to the round scene so the smoke test still runs.
    /// Tests drive it with <c>SendMessage("ShowScreen", "Prepare")</c>.
    /// </summary>
    public sealed class MainMenuUi : MonoBehaviour
    {
        public const string Main = "Main", Characters = "Characters", Create = "Create", Prepare = "Prepare", Settings = "Settings", Loading = "Loading";
        public const string StashScreenName = "Stash", VendorScreenName = "Vendor";
        /// <summary>Online play: host or join, then the lobby.</summary>
        public const string Online = "Online", Lobby = "Lobby";

        public string Current { get; private set; }

        private GameFlow _flow;
        private RectTransform _root, _screen;
        private Text _message, _subtitle;
        private Image _titleRule;
        // Create screen state.
        private string _draftName = "";
        private ClassArchetype _draftClass;
        private string _createError;

        private void Awake()
        {
            if (LaunchFlags.Has(DedicatedServer.Flag)) { enabled = false; return; }   // a dedicated server has no menu
            if (LaunchFlags.AutoTest)
            {
                // The smoke test plays a greybox round with the practice defaults: no menu, no profile.
                enabled = false;
                GameFlow.LoadScene(GameFlow.RoundScenePath);
                return;
            }
            UiStyle.MakeCanvas(gameObject, 0);
            UiStyle.EnsureEventSystem();
            GameSettings.ApplyDisplay();
        }

        private void Start()
        {
            if (LaunchFlags.AutoTest || LaunchFlags.Has(DedicatedServer.Flag)) return;
            _flow = GameFlow.Ensure();
            _flow.Changed += OnFlowChanged;
            BuildBackdrop();
            ShowScreen(_flow.Phase == FlowPhase.Lobby ? Lobby : Main);
        }

        private void OnDestroy() { if (_flow != null) _flow.Changed -= OnFlowChanged; }

        private void OnFlowChanged()
        {
            if (this == null || Current == null) return;
            if (_flow.Phase == FlowPhase.LoadingRound) { ShowScreen(Loading); return; }
            if (_flow.Phase == FlowPhase.Lobby) { ShowScreen(Lobby); return; }
            if (Current == Lobby && _flow.Phase == FlowPhase.Menu) { ShowScreen(Online); return; }   // left, refused, or the connection failed: the message says which
            if (Current != Create && Current != Online) ShowScreen(Current);   // refresh in place; the create and online screens keep their typing
            else if (_message != null) _message.text = _flow.Message ?? "";
        }

        // ---------------- frame ----------------

        private void BuildBackdrop()
        {
            _root = UiStyle.Node(transform, "Menu");
            _root.Stretch();
            var bg = UiStyle.Img(_root, "Backdrop", UiStyle.Backdrop); bg.rectTransform.Stretch();
            // A warm glow behind the title, darkened at the edges.
            var glow = UiStyle.Img(_root, "Glow", new Color(0.62f, 0.36f, 0.12f, 0.30f), UiStyle.Glow);
            glow.rectTransform.Place(new Vector2(0.30f, 0.66f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1900f, 1400f));
            var vig = UiStyle.Img(_root, "Vignette", Color.white, UiStyle.Vignette); vig.rectTransform.Stretch(-200f, -200f, -200f, -200f);

            var title = UiStyle.Label(_root, "Title", "DUNGEON GAME", 96, UiStyle.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            title.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(150f, -110f), new Vector2(1100f, 120f));
            _subtitle = UiStyle.Label(_root, "Subtitle", "Go down. Take what you can carry. Get out before the clock does.", 28, UiStyle.TextDim, TextAnchor.MiddleLeft, FontStyle.Italic);
            _subtitle.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(155f, -230f), new Vector2(1200f, 44f));
            _titleRule = UiStyle.Img(_root, "TitleRule", UiStyle.GoldDim);
            _titleRule.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(155f, -285f), new Vector2(560f, 2f));

            _message = UiStyle.Label(_root, "Message", "", 24, UiStyle.Gold, TextAnchor.MiddleCenter);
            _message.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(1300f, 36f));
            var ver = UiStyle.Label(_root, "Version", (Debug.isDebugBuild ? "Development build · " : "") + (NetSessions.Available ? "Offline or online · v" : "Offline rounds against bot teams · v") + Application.version, 18, UiStyle.TextFaint, TextAnchor.LowerRight);
            ver.rectTransform.Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 14f), new Vector2(700f, 30f));
        }

        /// <summary>Show a screen by name (Main, Characters, Create, Prepare, Settings, Loading). Rebuilds it from the flow.</summary>
        public void ShowScreen(string name)
        {
            if (_root == null) return;
            if (_screen != null) Destroy(_screen.gameObject);
            _screen = UiStyle.Node(_root, "Screen-" + name);
            _screen.Stretch();
            if (name != Create) _createError = null;
            Current = name;
            switch (name)
            {
                case Characters: BuildCharacters(); break;
                case Create: BuildCreate(); break;
                case Prepare: BuildPrepare(); break;
                case Settings: SettingsPanel.Build(_screen, () => ShowScreen(Main)); break;
                case Loading: BuildLoading(); break;
                case StashScreenName: StashScreen.Build(_screen, _flow, () => ShowScreen(Main), () => ShowScreen(StashScreenName), () => ShowScreen(VendorScreenName)); break;
                case VendorScreenName: VendorScreen.Build(_screen, _flow, () => ShowScreen(Main), () => ShowScreen(VendorScreenName), () => ShowScreen(StashScreenName)); break;
                case Online: OnlinePanels.BuildOnline(_screen, _flow, () => ShowScreen(Prepare), m => { _flow.Message = m; if (_message != null) _message.text = m; }); break;
                case Lobby: OnlinePanels.BuildLobby(_screen, _flow); break;
                default: Current = Main; BuildMain(); break;
            }
            _message.text = _flow?.Message ?? "";
            bool main = Current == Main;
            _subtitle.gameObject.SetActive(main); _titleRule.gameObject.SetActive(main);
        }

        private Button Btn(Transform parent, string name, string label, System.Action onClick, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, int font = 28)
        {
            var b = UiStyle.MakeButton(parent, name, label, onClick, font);
            ((RectTransform)b.transform).Place(anchor, pivot, pos, size);
            return b;
        }

        private static Text Header(RectTransform panel, string text)
        {
            var t = UiStyle.Label(panel, "Header", text, 40, UiStyle.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            t.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(1000f, 56f));
            UiStyle.Rule(panel, "HeaderRule", 520f, -96f);
            return t;
        }

        // ---------------- Main ----------------

        private void BuildMain()
        {
            bool has = _flow.HasProfile && _flow.Character != null;
            var col = new Vector2(0f, 1f);
            float y = -400f;
            var play = Btn(_screen, "PlayButton", has ? "Play" : "Create your first character", () => ShowScreen(has ? Prepare : Create), col, new Vector2(0f, 1f), new Vector2(150f, y), new Vector2(460f, 76f), 32);
            play.GetComponent<UiButtonSound>().ClickKey = UiSound.Confirm;
            Btn(_screen, "CharactersButton", "Characters", () => ShowScreen(_flow.HasProfile ? Characters : Create), col, new Vector2(0f, 1f), new Vector2(150f, y - 96f), new Vector2(460f, 68f));
            Btn(_screen, "StashButton", "Stash & Equipment", () => ShowScreen(StashScreenName), col, new Vector2(0f, 1f), new Vector2(150f, y - 180f), new Vector2(460f, 68f)).interactable = has;
            Btn(_screen, "VendorButton", "Vendor", () => ShowScreen(VendorScreenName), col, new Vector2(0f, 1f), new Vector2(150f, y - 264f), new Vector2(460f, 68f)).interactable = has;
            Btn(_screen, "SettingsButton", "Settings", () => ShowScreen(Settings), col, new Vector2(0f, 1f), new Vector2(150f, y - 348f), new Vector2(460f, 68f));
            Btn(_screen, "QuitButton", "Quit", Quit, col, new Vector2(0f, 1f), new Vector2(150f, y - 432f), new Vector2(460f, 68f));
            BuildAccountCard(_screen);
        }

        private void BuildAccountCard(RectTransform parent)
        {
            var card = UiStyle.Panel(parent, "AccountCard");
            card.Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-150f, -40f), new Vector2(680f, 640f));
            var p = _flow.Profile;
            if (p == null)
            {
                var t = UiStyle.Label(card, "Welcome", "WELCOME, DELVER", 36, UiStyle.Gold, TextAnchor.UpperCenter, FontStyle.Bold);
                t.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(600f, 50f));
                var body = UiStyle.Label(card, "Body",
                    "No character yet.\n\nEvery account starts with the Warden, the Marksman and the Mender; the Reaver, the Shade and the Elementalist unlock as your account levels up.\n\n" +
                    "A new account starts with a free starter kit, " + Core.Combat.FightRules.StartingSilver + " silver and two health potions. What you carry out of the dungeon is yours; what you die with is gone.",
                    24, UiStyle.TextMain, TextAnchor.UpperLeft);
                body.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(580f, 460f));
                return;
            }
            int level = p.AccountLevel;
            long from = AccountRules.XpToReach(level), to = AccountRules.XpToReach(level + 1);
            var head = UiStyle.Label(card, "Level", $"ACCOUNT LEVEL {level}", 30, UiStyle.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            head.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -30f), new Vector2(400f, 44f));
            var xp = UiStyle.Label(card, "Xp", $"{p.AccountXp - from} / {to - from} XP", 22, UiStyle.TextDim, TextAnchor.MiddleRight);
            xp.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -30f), new Vector2(240f, 44f));
            var bar = new UiBar(card, "XpBar", new Vector2(600f, 12f), UiStyle.Gold);
            bar.Root.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(600f, 12f));
            bar.Set(to > from ? (float)(p.AccountXp - from) / (to - from) : 1f, null);

            var bank = UiStyle.Label(card, "Bank",
                $"Silver  {UiStyle.Colour(p.Silver.ToString("N0"), UiStyle.Gold)}      Stash  {p.Stash.Count} item{(p.Stash.Count == 1 ? "" : "s")}      " +
                $"Potions  {Count(p, "Health Potion")}      Antidotes  {Count(p, "Antidote")}", 22, UiStyle.TextMain);
            bank.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -112f), new Vector2(600f, 40f));

            var ch = _flow.Character;
            UiStyle.Rule(card, "Rule", 600f, -170f);
            if (ch == null) return;
            var name = UiStyle.Label(card, "CharName", ch.Name, 44, UiStyle.TextMain, TextAnchor.MiddleLeft, FontStyle.Bold);
            name.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -190f), new Vector2(600f, 60f));
            var spec = _flow.CurrentSpec;
            var cls = UiStyle.Label(card, "CharClass", $"{ch.Class.Name}  ·  {spec}  ·  gear score {_flow.GearScore}", 24, UiStyle.Gold);
            cls.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -250f), new Vector2(600f, 36f));
            var worn = ch.Equipment.Items.ToList();
            string gear = worn.Count == 0 ? UiStyle.Colour("Naked. The free starter kit is waiting under Play.", UiStyle.Bad)
                : string.Join("\n", worn.Select(i => $"{SlotName(i)}  {UiStyle.Colour(i.Name, RarityColour(i.Rarity))}{(i.IsStarter ? UiStyle.Colour("  starter", UiStyle.TextFaint) : "")}"));
            var g = UiStyle.Label(card, "Gear", gear, 21, UiStyle.TextDim, TextAnchor.UpperLeft);
            g.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -300f), new Vector2(600f, 320f));
            g.lineSpacing = 1.05f;
        }

        // ---------------- Characters ----------------

        private void BuildCharacters()
        {
            var panel = UiStyle.Panel(_screen, "CharactersPanel");
            panel.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(1100f, 740f));
            Header(panel, "CHOOSE YOUR CHARACTER");
            var p = _flow.Profile;
            float y = -130f;
            if (p != null)
                foreach (var ch in p.Characters)
                {
                    var c = ch;
                    var spec = Loadout.For(p, c).Spec;
                    var b = Btn(panel, "Character-" + c.Name, $"{c.Name}    <color=#{UiStyle.Hex(UiStyle.TextDim)}>{c.Class.Name} · {spec} · gear {GearScore.Of(c.Equipment)}{(c.Equipment.Items.Any() ? "" : " · naked")}</color>",
                        () => { _flow.SelectCharacter(c); }, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(960f, 62f), 26);
                    b.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
                    b.SetSelected(c == _flow.Character);
                    y -= 72f;
                }
            int slots = PlayerProfile.MaxCharacters - (p?.Characters.Count ?? 0);
            var hint = UiStyle.Label(panel, "Slots", $"{slots} of {PlayerProfile.MaxCharacters} character slots free. Silver, the stash and potions are shared by every character.", 20, UiStyle.TextFaint, TextAnchor.MiddleCenter);
            hint.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(960f, 30f));
            Btn(panel, "BackButton", "Back", () => ShowScreen(Main), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 36f), new Vector2(260f, 64f));
            var nb = Btn(panel, "NewCharacterButton", "New character", () => ShowScreen(Create), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 36f), new Vector2(320f, 64f));
            nb.interactable = slots > 0;
            var cont = Btn(panel, "ContinueButton", "Continue", () => ShowScreen(Prepare), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 36f), new Vector2(260f, 64f));
            cont.interactable = _flow.Character != null;
        }

        // ---------------- Create ----------------

        private void BuildCreate()
        {
            var panel = UiStyle.Panel(_screen, "CreatePanel");
            panel.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(1180f, 740f));
            Header(panel, _flow.HasProfile ? "NEW CHARACTER" : "YOUR FIRST CHARACTER");

            var nl = UiStyle.Label(panel, "NameLabel", "Name", 26, UiStyle.TextDim);
            nl.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(60f, -150f), new Vector2(120f, 40f));
            var input = UiStyle.MakeInput(panel, "NameInput", "Type a name");
            ((RectTransform)input.transform).Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(180f, -150f), new Vector2(560f, 58f));
            input.characterLimit = GameFlow.MaxNameLength;
            input.SetTextWithoutNotify(_draftName);
            input.onValueChanged.AddListener(v => _draftName = v);

            var classes = _flow.Classes.ToList();
            if (_draftClass == null) _draftClass = classes.FirstOrDefault(c => c.unlocked).cls;
            Text desc = null;
            var buttons = new List<(Button b, ClassArchetype c)>();
            for (int i = 0; i < classes.Count; i++)
            {
                var (cls, unlocked, level) = classes[i];
                int col = i % 3, row = i / 3;
                string label = unlocked ? $"<b>{cls.Name}</b>\n<size=18><color=#{UiStyle.Hex(UiStyle.TextDim)}>{RoleName(cls.Role)}</color></size>"
                                        : $"<b>{cls.Name}</b>\n<size=18><color=#{UiStyle.Hex(UiStyle.TextFaint)}>Unlocks at account level {level}</color></size>";
                var c = cls;
                var b = Btn(panel, "Class-" + cls.Name, label, null, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(60f + col * 360f, -220f - row * 120f), new Vector2(340f, 104f), 28);
                b.interactable = unlocked;
                buttons.Add((b, c));
            }
            desc = UiStyle.Label(panel, "ClassText", "", 22, UiStyle.TextMain, TextAnchor.UpperLeft);
            desc.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(60f, -462f), new Vector2(1060f, 120f));
            void Pick(ClassArchetype c)
            {
                _draftClass = c;
                foreach (var (bb, cc) in buttons) bb.SetSelected(cc == c);
                desc.text = c == null ? "" : $"{UiStyle.Colour(c.Name, UiStyle.Gold)}  ·  {c.Tagline}\n{UiStyle.Colour("In the dungeon:", UiStyle.TextDim)} {c.DungeonUtility}\n" +
                    UiStyle.Colour("Specs: " + string.Join(", ", WeaponSpec.ForClass(c.Name).Select(s => $"{s.Name} ({s.WeaponBase})")), UiStyle.TextDim);
            }
            foreach (var (b, c) in buttons) { var cc = c; b.onClick.AddListener(() => Pick(cc)); }
            Pick(_draftClass);

            var err = UiStyle.Label(panel, "Error", _createError ?? "", 22, UiStyle.Bad, TextAnchor.MiddleCenter);
            err.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 106f), new Vector2(1000f, 34f));
            Btn(panel, "BackButton", "Back", () => ShowScreen(_flow.HasProfile ? Characters : Main), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 36f), new Vector2(260f, 64f));
            var create = Btn(panel, "CreateButton", "Create", () =>
            {
                var why = _flow.CreateCharacter(_draftName, _draftClass);
                if (why != null) { _createError = why; err.text = why; UiSound.Play(UiSound.Error); return; }
                UiSound.Play(UiSound.Confirm);
                _draftName = ""; _draftClass = null;
                ShowScreen(Prepare);
            }, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 36f), new Vector2(300f, 64f));
            create.GetComponent<UiButtonSound>().ClickKey = null;
        }

        /// <summary>Tests and tools: fill the create screen's fields.</summary>
        public void SetDraft(string name) { _draftName = name ?? ""; if (Current == Create) ShowScreen(Create); }

        // ---------------- Prepare ----------------

        private void BuildPrepare()
        {
            var ch = _flow.Character;
            if (ch == null) { ShowScreen(_flow.HasProfile ? Characters : Create); return; }
            var panel = UiStyle.Panel(_screen, "PreparePanel");
            panel.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(1400f, 750f));
            Header(panel, "PREPARE FOR THE DESCENT");
            var who = UiStyle.Label(panel, "Who", $"{UiStyle.Colour(ch.Name, UiStyle.TextMain)}   {ch.Class.Name}   ·   account level {_flow.Profile.AccountLevel}", 26, UiStyle.TextDim, TextAnchor.MiddleCenter);
            who.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -112f), new Vector2(1200f, 40f));

            var specs = _flow.Specs;
            string current = _flow.CurrentSpec;
            float cardW = 620f, gap = 30f, x0 = -(specs.Count - 1) * (cardW + gap) / 2f;
            for (int i = 0; i < specs.Count; i++)
            {
                var s = specs[i];
                var card = UiStyle.MakeButton(panel, "Spec-" + s.Name, "", () =>
                {
                    var why = _flow.ChooseSpec(s.Name);
                    if (why != null) { _flow.Message = why; UiSound.Play(UiSound.Error); ShowScreen(Prepare); }
                }, 24);
                var rt = (RectTransform)card.transform;
                rt.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x0 + i * (cardW + gap), -165f), new Vector2(cardW, 345f));
                card.SetSelected(s.Name == current);
                var lbl = card.GetComponentInChildren<Text>();
                lbl.alignment = TextAnchor.UpperLeft; lbl.fontSize = 19; lbl.rectTransform.Stretch(26f, 22f, 18f, 14f);
                string ult = s.Ultimate ?? (WeaponSpec.Ultimates.TryGetValue(s.Name, out var u) ? u : null);
                lbl.text = $"<size=34><b><color=#{UiStyle.Hex(UiStyle.Gold)}>{s.Name}</color></b></size>   <color=#{UiStyle.Hex(UiStyle.TextDim)}>{s.WeaponBase}</color>\n" +
                           $"<i>{s.Tagline}</i>\n\n" +
                           Line("RMB", s.Bolt) + Line("Q", s.Zone) + Line("E", s.Mobility) + Line("R", ult) + Line("Passive", s.Passive) +
                           (s.Name == current ? $"\n<color=#{UiStyle.Hex(UiStyle.Good)}>In hand</color>" : $"\n<color=#{UiStyle.Hex(UiStyle.TextFaint)}>Click to take up a {s.WeaponBase}</color>");
            }
            if (specs.Count == 0)
            {
                var only = UiStyle.Label(panel, "OneSpec", $"Spec: {current}", 30, UiStyle.Gold, TextAnchor.MiddleCenter);
                only.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -300f), new Vector2(900f, 50f));
            }

            // Bot teams.
            var bl = UiStyle.Label(panel, "BotsLabel", "Enemy bot teams", 26, UiStyle.TextDim, TextAnchor.MiddleRight);
            bl.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(1f, 0.5f), new Vector2(-150f, 196f), new Vector2(300f, 50f));
            for (int n = 1; n <= 3; n++)
            {
                int k = n;
                var b = Btn(panel, "Bots-" + n, n.ToString(), () => { _flow.BotTeams = k; }, new Vector2(0.5f, 0f), new Vector2(0f, 0.5f), new Vector2(-130f + (n - 1) * 80f, 196f), new Vector2(64f, 56f), 28);
                b.SetSelected(_flow.BotTeams == n);
            }
            var tier = DungeonGen.Core.DungeonTier.For(GameFlow.PracticeTier);
            int gs = _flow.GearScore;
            bool admitted = tier.Admits(gs);
            var tl = UiStyle.Label(panel, "Tier", $"{tier.Name} admits gear score {tier.MinGearScore} to {tier.MaxGearScore}; yours is {UiStyle.Colour(gs.ToString(), admitted ? UiStyle.Good : UiStyle.Bad)}.  " +
                $"Pockets: {Count(_flow.Profile, "Health Potion")} health potion, {Count(_flow.Profile, "Antidote")} antidote.", 21, UiStyle.TextDim, TextAnchor.MiddleCenter);
            tl.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 136f), new Vector2(1300f, 36f));

            Btn(panel, "BackButton", "Back", () => ShowScreen(_flow.Profile.Characters.Count > 1 ? Characters : Main), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 36f), new Vector2(260f, 64f));
            if (_flow.CharacterNaked || ch.Equipment.Items.Count() < 6)
                Btn(panel, "StarterKitButton", "Claim free starter kit", () => { _flow.ClaimStarterKit(); UiSound.Play(UiSound.Confirm); }, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(320f, 36f), new Vector2(330f, 64f), 22);
            // Online: host a round or join one (the offline round stays the big button).
            var online = Btn(panel, "PlayOnlineButton", "Play online…", () => ShowScreen(Online), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-440f, 36f), new Vector2(300f, 64f), 26);
            online.interactable = admitted && NetSessions.Available;
            var enter = Btn(panel, "EnterButton", "Enter the dungeon", () =>
            {
                var why = _flow.Play();
                if (why != null) { _flow.Message = why; _message.text = why; UiSound.Play(UiSound.Error); }
            }, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 36f), new Vector2(380f, 64f), 30);
            enter.GetComponent<UiButtonSound>().ClickKey = UiSound.Confirm;
            enter.interactable = admitted;
        }

        private static string Line(string key, string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int colon = text.IndexOf(':');
            string head = colon > 0 ? text.Substring(0, colon) : text, rest = colon > 0 ? text.Substring(colon + 1).Trim() : "";
            return $"<color=#{UiStyle.Hex(UiStyle.Gold)}>{key}</color>  <b>{head}</b>  <color=#{UiStyle.Hex(UiStyle.TextDim)}>{rest}</color>\n";
        }

        // ---------------- Loading ----------------

        private void BuildLoading()
        {
            var t = UiStyle.Label(_screen, "Loading", "Entering the dungeon…", 40, UiStyle.Gold, TextAnchor.MiddleCenter, FontStyle.Italic);
            t.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(1000f, 60f));
        }

        // ---------------- helpers ----------------

        private static void Quit()
        {
            Debug.Log("Main menu: quit.");
#if UNITY_EDITOR
            if (!Application.isBatchMode) UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static int Count(PlayerProfile p, string consumable)
        {
            var c = Consumable.ByName(consumable);
            return p == null || c == null ? 0 : p.ConsumableCount(c);
        }

        private static string SlotName(Item i) => UiStyle.Colour(i.Base.Slot.ToString().PadRight(7), UiStyle.TextFaint);

        public static Color RarityColour(Rarity r) => r switch
        {
            Rarity.Unique => new Color(0.95f, 0.55f, 0.20f),
            Rarity.Rare => new Color(0.95f, 0.85f, 0.35f),
            Rarity.Magic => new Color(0.45f, 0.62f, 1f),
            _ => UiStyle.TextMain,
        };

        private static string RoleName(ClassRole r) => r switch
        {
            ClassRole.MeleeDps => "Melee damage",
            ClassRole.RangedDps => "Ranged damage",
            _ => r.ToString(),
        };
    }

    /// <summary>A bar: dark track, coloured fill, optional centred label.</summary>
    public sealed class UiBar
    {
        public readonly RectTransform Root;
        public readonly Image Fill;
        public readonly Text Label;

        public UiBar(Transform parent, string name, Vector2 size, Color fill, int fontSize = 0)
        {
            var track = UiStyle.Img(parent, name, new Color(0f, 0f, 0f, 0.65f), UiStyle.Rounded);
            Root = track.rectTransform; Root.sizeDelta = size;
            Fill = UiStyle.Img(Root, "Fill", fill, UiStyle.White);
            Fill.type = Image.Type.Filled; Fill.fillMethod = Image.FillMethod.Horizontal; Fill.fillOrigin = 0;
            Fill.rectTransform.Stretch(2f, 2f, 2f, 2f);
            var frame = UiStyle.Img(Root, "Frame", UiStyle.GoldFaint, UiStyle.Frame); frame.rectTransform.Stretch(-1f, -1f, -1f, -1f);
            if (fontSize > 0)
            {
                Label = UiStyle.Label(Root, "Label", "", fontSize, UiStyle.TextMain, TextAnchor.MiddleCenter);
                Label.rectTransform.Stretch();
            }
        }

        public void Set(float fraction, string text, Color? colour = null)
        {
            Fill.fillAmount = Mathf.Clamp01(fraction);
            if (colour.HasValue) Fill.color = colour.Value;
            if (Label != null && text != null) Label.text = text;
        }
    }
}
