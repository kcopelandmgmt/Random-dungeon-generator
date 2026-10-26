using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Items;
using DungeonGen.Core.Net;
using DungeonGen.Core.Profile;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DungeonGen.Unity
{
    public enum FlowPhase { Menu, LoadingRound, InRound, RoundOver, LoadingMenu }

    /// <summary>
    /// The game around a round, one per process (DontDestroyOnLoad): the local profile, the chosen character and spec, the
    /// practice round's settings, and the hand-off between the menu scene and the round scene. In a round it watches the
    /// host's state for the local player's end (extracted, dead, or the clock), settles the profile with
    /// <see cref="RoundSettlement.Settle(PlayerProfile, Character, MatchServer, int, string)"/>, saves it, and keeps the report
    /// for the summary screen. It reads the host's <see cref="MatchServer"/> because settlement is a host job; the screens read
    /// only what it exposes. No uGUI here: the screens live in the UI assembly.
    /// </summary>
    public sealed class GameFlow : MonoBehaviour
    {
        public const string MenuScenePath = "Assets/Scenes/MainMenu.unity";
        public const string RoundScenePath = "Assets/Scenes/SampleScene.unity";
        public const int LocalPlayerId = 1;
        /// <summary>The practice dungeon's tier (the greybox config is Tier I); the tier wall is checked before Play.</summary>
        public const int PracticeTier = 1;
        public const int MaxNameLength = 20;
        /// <summary>The consumables on the quick slots, in key order (1, 2), brought into every round.</summary>
        public static readonly string[] PocketConsumables = { "Health Potion", "Antidote" };
        private const string SelectedKey = "dg.character.selected", BotTeamsKey = "dg.practice.botteams";

        public static GameFlow Instance { get; private set; }

        /// <summary>The running flow, made on first use.</summary>
        public static GameFlow Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("GameFlow");
            return go.AddComponent<GameFlow>();
        }

        public PlayerProfile Profile { get; private set; }
        public Character Character { get; private set; }
        public FlowPhase Phase { get; private set; } = FlowPhase.Menu;
        /// <summary>The last settled round (null until one settles).</summary>
        public SettlementReport LastReport { get; private set; }
        /// <summary>How the local player died in the last round, when they did.</summary>
        public DeathRecap LastRecap { get; private set; }
        /// <summary>A line for the menu: why Play was refused, what leaving cost.</summary>
        public string Message { get; set; }
        /// <summary>Rounds played this session.</summary>
        public int RoundsPlayed { get; private set; }
        /// <summary>Raised whenever anything a screen shows changes.</summary>
        public event Action Changed;

        private int _botTeams = 2;
        public int BotTeams
        {
            get => _botTeams;
            set { _botTeams = Mathf.Clamp(value, 1, 3); PlayerPrefs.SetInt(BotTeamsKey, _botTeams); Changed?.Invoke(); }
        }

        private ServerSimulation _sim;
        private bool _settled;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            _botTeams = Mathf.Clamp(PlayerPrefs.GetInt(BotTeamsKey, 2), 1, 3);
            LoadProfile();
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        // ---------------- profile and characters ----------------

        public bool HasProfile => Profile != null;

        /// <summary>(Re)load the profile from <see cref="ProfileStore"/>; selects the last used character.</summary>
        public void LoadProfile()
        {
            Profile = ProfileStore.Load();
            Character = null;
            if (Profile != null && Profile.Characters.Count > 0)
            {
                int i = Mathf.Clamp(PlayerPrefs.GetInt(SelectedKey, 0), 0, Profile.Characters.Count - 1);
                Character = Profile.Characters[i];
            }
            Changed?.Invoke();
        }

        public void Save()
        {
            if (Profile == null) return;
            ProfileStore.Save(Profile);
        }

        /// <summary>Classes the account may create now, and the rest with the level that unlocks them.</summary>
        public IEnumerable<(ClassArchetype cls, bool unlocked, int level)> Classes =>
            ClassArchetype.DefaultRoster.Select(c => (c, AccountRules.ClassUnlocked(c.Name, Profile?.AccountLevel ?? 1), AccountRules.LevelForClass(c.Name)));

        /// <summary>Make a character in the free kit (the first one makes the account). Returns why not, or null.</summary>
        public string CreateCharacter(string name, ClassArchetype cls)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return "Give your character a name.";
            if (name.Length > MaxNameLength) return $"Names are at most {MaxNameLength} letters.";
            if (cls == null) return "Choose a class.";
            if (!AccountRules.ClassUnlocked(cls.Name, Profile?.AccountLevel ?? 1)) return $"{cls.Name} unlocks at account level {AccountRules.LevelForClass(cls.Name)}.";
            if (Profile == null)
            {
                Profile = ProfileStore.CreateNew(name, cls);
                SelectCharacter(Profile.Characters[0]);
                return null;
            }
            if (Profile.Characters.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))) return $"You already have a character called {name}.";
            try
            {
                var ch = Profile.CreateCharacter(name, cls);
                Vendor.ClaimStarterKit(ch);
                Save();
                SelectCharacter(ch);
                return null;
            }
            catch (InvalidOperationException e) { return e.Message; }
        }

        public void SelectCharacter(Character c)
        {
            if (Profile == null || c == null || !Profile.Characters.Contains(c)) return;
            Character = c;
            PlayerPrefs.SetInt(SelectedKey, Profile.Characters.IndexOf(c));
            Changed?.Invoke();
        }

        /// <summary>The specs the selected character's class has (one per weapon base).</summary>
        public IReadOnlyList<WeaponSpec> Specs => Character == null ? Array.Empty<WeaponSpec>() : WeaponSpec.ForClass(Character.Class.Name).ToList();

        /// <summary>The spec the weapon in hand gives (what the server will seat).</summary>
        public string CurrentSpec => Character == null || Profile == null ? null : Loadout.For(Profile, Character).Spec;

        public int GearScore => Character == null ? 0 : DungeonGen.Core.Items.GearScore.Of(Character.Equipment);

        public bool CharacterNaked => Character != null && !Character.Equipment.Items.Any();

        /// <summary>
        /// Take up a spec: the spec is the weapon in hand, so this puts that weapon base in the character's hand: the best
        /// one of that base in the stash, or a free Normal starter weapon (the starter kit is always free). A non-starter
        /// weapon taken off goes to the stash. Returns why not, or null.
        /// </summary>
        public string ChooseSpec(string specName)
        {
            if (Character == null) return "Choose a character first.";
            var spec = WeaponSpec.ForClass(Character.Class.Name).FirstOrDefault(s => s.Name == specName);
            if (spec == null) return $"{Character.Class.Name} has no spec called {specName}.";
            if (Character.Spec?.Name == spec.Name) return null;
            var b = ItemBase.ByName(spec.WeaponBase);
            if (b == null) return $"No weapon base {spec.WeaponBase}.";
            var fromStash = Profile.Stash.Where(i => i.Base == b).OrderByDescending(i => DungeonGen.Core.Items.GearScore.Of(i)).FirstOrDefault();
            var weapon = fromStash ?? new Item(b, 0, Rarity.Normal, b.Name, new List<AffixRoll>(), new List<AffixRoll>()).MarkStarter();
            if (fromStash != null) Profile.Stash.Remove(fromStash);
            var old = Character.Equipment.Equip(weapon);
            if (old != null && !old.IsStarter) Profile.Stash.Add(old);
            Save();
            Changed?.Invoke();
            return null;
        }

        /// <summary>Re-dress a naked (or partly naked) character in the free starter kit.</summary>
        public void ClaimStarterKit()
        {
            if (Character == null) return;
            Vendor.ClaimStarterKit(Character);
            Save();
            Changed?.Invoke();
        }

        // ---------------- the round ----------------

        /// <summary>Start a practice round with the selected character. Returns why not, or null (the round scene is loading).</summary>
        public string Play(int? seed = null)
        {
            if (Phase != FlowPhase.Menu) return "A round is already starting.";
            if (Profile == null || Character == null) return "Create a character first.";
            // Pockets: every Health Potion and Antidote in the bank comes in (keys 1 and 2); a death loses them.
            var pockets = new Dictionary<string, int>();
            foreach (var name in PocketConsumables)
            {
                var c = Consumable.ByName(name);
                int n = c != null ? Profile.ConsumableCount(c) : 0;
                if (n > 0) pockets[name] = n;
            }
            var loadout = Loadout.For(Profile, Character, null, pockets);
            var refusal = loadout.TierRefusal(PracticeTier);
            if (refusal != null) return refusal + ".";
            PracticeLaunch.Current = new PracticeLaunch
            {
                Seed = seed ?? UnityEngine.Random.Range(1, 1000000),
                BotTeams = BotTeams,
                Loadout = loadout,
                AccountLevel = Profile.AccountLevel,
            };
            LastReport = null; LastRecap = null; Message = null; _settled = false; _sim = null;
            Phase = FlowPhase.LoadingRound;
            Changed?.Invoke();
            StartCoroutine(LoadThen(RoundScenePath, () => { Phase = FlowPhase.InRound; Changed?.Invoke(); }));
            return null;
        }

        /// <summary>The host's server for the running round, or null.</summary>
        public MatchServer Server
        {
            get
            {
                if (_sim == null) _sim = FindFirstObjectByType<ServerSimulation>();
                return _sim != null ? _sim.Server : null;
            }
        }

        private void Update()
        {
            if (Phase != FlowPhase.InRound || _settled) return;
            var server = Server;
            if (server == null || server.Sim == null || !server.State.PlayerTeam.ContainsKey(LocalPlayerId)) return;
            if (RoundSettlement.OutcomeOf(server.State, LocalPlayerId) == null) return;
            SettleNow();
        }

        /// <summary>Settle the local player's finished round into the profile and save it. Called by the watcher; public for tests.</summary>
        public SettlementReport SettleNow()
        {
            if (_settled) return LastReport;
            var server = Server;
            if (server == null || Profile == null || Character == null) return null;
            try
            {
                var report = RoundSettlement.Settle(Profile, Character, server, LocalPlayerId);
                if (report.Outcome == RoundOutcome.Died) LastRecap = DeathRecap.For(server.State, LocalPlayerId, server.Sim?.Hits);
                LastReport = report;
                _settled = true;
                RoundsPlayed++;
                Save();
                Phase = FlowPhase.RoundOver;
                Debug.Log($"GameFlow: round settled: {report.Outcome}, +{report.XpDelta} XP, +{report.SilverDelta} silver, {report.ItemsGained.Count} gained, {report.ItemsLost.Count} lost, level {report.LevelBefore}->{report.LevelAfter}.");
                Changed?.Invoke();
                return report;
            }
            catch (Exception e)
            {
                _settled = true;   // never retry in a loop
                Debug.LogError("GameFlow: settlement failed: " + e);
                Phase = FlowPhase.RoundOver;
                Changed?.Invoke();
                return null;
            }
        }

        /// <summary>Is the local player still inside the running round?</summary>
        public bool LocalPlayerInside
        {
            get
            {
                var s = Server;
                return s != null && s.State.PlayersInside.Contains(LocalPlayerId);
            }
        }

        /// <summary>
        /// End the local player's round as a death (the pause menu's Leave, and a downed player giving up). The killer is
        /// whoever downed them, if anyone, so an enemy still earns the soul. Settles at once.
        /// </summary>
        public void ForfeitRound()
        {
            var s = Server;
            if (s == null || !s.State.PlayersInside.Contains(LocalPlayerId)) return;
            int killer = -1;
            foreach (var e in s.State.Log) if (e.Kind == MatchEventKind.PlayerDowned && e.A == LocalPlayerId) killer = e.B;
            if (!s.State.Downed.ContainsKey(LocalPlayerId)) killer = -1;
            s.Raise(new MatchEvent(MatchEventKind.PlayerDied, s.State.Time, LocalPlayerId, killer, Mathf.Max(0, s.RoomOf(LocalPlayerId))));
            if (Phase == FlowPhase.InRound) SettleNow();
        }

        /// <summary>Pause menu: leave the round. It counts as a death: settle, save, back to the menu.</summary>
        public void LeaveRound()
        {
            if (Phase == FlowPhase.InRound) ForfeitRound();
            var r = LastReport;
            if (r != null && !r.AlreadySettled)
                Message = $"You left the round. It counts as a death: {r.ItemsLost.Count} item{(r.ItemsLost.Count == 1 ? "" : "s")} lost, +{r.XpDelta} XP.";
            ReturnToMenu();
        }

        /// <summary>Back to the main menu (from the summary, or from a greybox round with no flow behind it).</summary>
        public void ReturnToMenu()
        {
            if (Phase == FlowPhase.LoadingMenu) return;
            PracticeLaunch.Current = null;
            _sim = null;
            Phase = FlowPhase.LoadingMenu;
            Changed?.Invoke();
            StartCoroutine(LoadThen(MenuScenePath, () => { Phase = FlowPhase.Menu; Changed?.Invoke(); }));
        }

        private IEnumerator LoadThen(string path, Action done)
        {
            var op = LoadScene(path);
            if (op == null) { Debug.LogError("GameFlow: cannot load " + path); Phase = FlowPhase.Menu; yield break; }
            while (!op.isDone) yield return null;
            done?.Invoke();
        }

        /// <summary>Load a scene by asset path: from the editor's AssetDatabase in play mode (no build list needed), else from the build.</summary>
        public static AsyncOperation LoadScene(string path)
        {
#if UNITY_EDITOR
            return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            return SceneManager.LoadSceneAsync(path, LoadSceneMode.Single);
#endif
        }
    }
}
