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
    /// <summary>Appended only (Lobby came with online play).</summary>
    public enum FlowPhase { Menu, LoadingRound, InRound, RoundOver, LoadingMenu, Lobby }

    /// <summary>
    /// The game around a round, one per process (DontDestroyOnLoad): the local profile, the chosen character and spec, the
    /// practice round's settings, and the hand-off between the menu scene and the round scene. In a round it watches the
    /// host's state for the local player's end (extracted, dead, or the clock), settles the profile with
    /// <see cref="RoundSettlement.Settle(PlayerProfile, Character, MatchServer, int, string)"/>, saves it, and keeps the report
    /// for the summary screen. It reads the host's <see cref="MatchServer"/> because settlement is a host job; the screens read
    /// only what it exposes. No uGUI here: the screens live in the UI assembly.
    /// Online (2026-10-26): <see cref="HostOnline"/>, <see cref="JoinOnline"/> and <see cref="StartDedicated"/> start a networked session
    /// (<see cref="NetSessions"/>) and wait in the lobby (<see cref="FlowPhase.Lobby"/>); when the server starts the round every process loads
    /// the round scene and <see cref="OnlineMatch"/> hosts or joins it. A host settles like the offline round; a client settles its own
    /// profile from the server's <see cref="RoundResult"/> (<see cref="ClientMirror.Result"/>). Leaving online is a forfeit the server records.
    /// </summary>
    public sealed class GameFlow : MonoBehaviour
    {
        public const string MenuScenePath = "Assets/Scenes/MainMenu.unity";
        public const string RoundScenePath = "Assets/Scenes/SampleScene.unity";
        /// <summary>The player id offline and of a host's own player. A client's id comes from the server: read <see cref="LocalId"/>.</summary>
        public const int LocalPlayerId = 1;
        /// <summary>This process's player in the running round: <see cref="LocalPlayerId"/> offline and when hosting, the server's id on a client.</summary>
        public static int LocalId { get; set; } = LocalPlayerId;
        /// <summary>The practice dungeon's tier (the greybox config is Tier I); the tier wall is checked before Play.</summary>
        public const int PracticeTier = 1;
        public const int MaxNameLength = 20;
        /// <summary>The consumables on the quick slots, in key order (1, 2), brought into every round.</summary>
        public static readonly string[] PocketConsumables = { "Health Potion", "Antidote" };
        /// <summary>PlayerPrefs keys: the selected character index and the bot teams choice.</summary>
        public const string SelectedKey = "dg.character.selected", BotTeamsKey = "dg.practice.botteams";

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

        private void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            if (OnlineLaunch.Current != null) EndSession();
        }

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

        /// <summary>The selected character's loadout for a round: worn gear and every Health Potion and Antidote in the bank (keys 1 and 2). Null with why not.</summary>
        private Loadout RoundLoadout(out string refusal)
        {
            refusal = null;
            if (Profile == null || Character == null) { refusal = "Create a character first."; return null; }
            var pockets = new Dictionary<string, int>();
            foreach (var name in PocketConsumables)
            {
                var c = Consumable.ByName(name);
                int n = c != null ? Profile.ConsumableCount(c) : 0;
                if (n > 0) pockets[name] = n;
            }
            var loadout = Loadout.For(Profile, Character, null, pockets);
            var why = loadout.TierRefusal(PracticeTier);
            if (why != null) { refusal = why + "."; return null; }
            return loadout;
        }

        /// <summary>Start a practice round with the selected character. Returns why not, or null (the round scene is loading).</summary>
        public string Play(int? seed = null)
        {
            if (Phase != FlowPhase.Menu) return "A round is already starting.";
            if (Profile == null || Character == null) return "Create a character first.";
            // Pockets: every Health Potion and Antidote in the bank comes in (keys 1 and 2); a death loses them.
            var loadout = RoundLoadout(out var refusal);
            if (refusal != null) return refusal;
            OnlineLaunch.Current = null; LocalId = LocalPlayerId;
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
            Time.timeScale = 1f;
            StartCoroutine(LoadThen(RoundScenePath, null, HostRound));
            return null;
        }

        /// <summary>The round scene is up: host the offline round in it (the release host; a dev scene's PracticeMode may have hosted already).</summary>
        private IEnumerator HostRound()
        {
            yield return null;   // let the scene's Start methods run first
            var session = NetSessions.Current;
            if (OnlineLaunch.Current != null && session != null)
            {
                var online = OnlineMatch.InScene();
                if (online == null) Debug.LogError("GameFlow: the round scene has no match bootstrap to host on.");
                else if (session.IsServer) online.HostOnline(session);
                else online.JoinOnline(session);
            }
            else
            {
                var match = OfflineMatch.HostInScene();
                if (match == null) Debug.LogError("GameFlow: the round scene has no match bootstrap to host on.");
                else if (!match.Hosting) match.Host();
            }
            Phase = FlowPhase.InRound;
            Changed?.Invoke();
        }

        // ---------------- online ----------------

        /// <summary>The networked session behind the lobby or the round, or null offline.</summary>
        public INetSession Session => OnlineLaunch.Current != null ? NetSessions.Current : null;
        public bool Online => Session != null;
        /// <summary>A line about the session for the lobby screen.</summary>
        public string NetStatus => Session?.Status ?? "";

        /// <summary>Host a round from this machine: listen on <paramref name="port"/> (every interface, or <paramref name="bindAddress"/>) and wait in the lobby. Returns why not, or null.</summary>
        public string HostOnline(ushort port = NetSessions.DefaultPort, string bindAddress = null)
        {
            if (Phase != FlowPhase.Menu) return "A round is already starting.";
            if (!NetSessions.Available) return "Online play is not in this build.";
            var loadout = RoundLoadout(out var refusal);
            if (refusal != null) return refusal;
            return StartSession(new NetSessionOptions { Role = NetRole.Host, Address = bindAddress ?? "", Port = port, Join = JoinRequest.From(loadout, Profile.AccountLevel), BotTeams = BotTeams }, loadout);
        }

        /// <summary>Join a host or a dedicated server at <paramref name="address"/>:<paramref name="port"/> and wait in its lobby. Returns why not, or null.</summary>
        public string JoinOnline(string address, ushort port = NetSessions.DefaultPort)
        {
            if (Phase != FlowPhase.Menu) return "A round is already starting.";
            if (!NetSessions.Available) return "Online play is not in this build.";
            var loadout = RoundLoadout(out var refusal);
            if (refusal != null) return refusal;
            PlayerPrefs.SetString(NetSessions.LastAddressKey, address ?? NetSessions.DefaultAddress); PlayerPrefs.SetInt(NetSessions.LastPortKey, port); PlayerPrefs.Save();
            return StartSession(new NetSessionOptions { Role = NetRole.Client, Address = string.IsNullOrWhiteSpace(address) ? NetSessions.DefaultAddress : address.Trim(), Port = port, Join = JoinRequest.From(loadout, Profile.AccountLevel) }, loadout);
        }

        /// <summary>A dedicated server (<c>-server</c>): no player, no profile; listen and run rounds for whoever joins.</summary>
        public string StartDedicated(ushort port = NetSessions.DefaultPort, string bindAddress = null, int botTeams = 1)
        {
            if (!NetSessions.Available) return "Online play is not in this build.";
            return StartSession(new NetSessionOptions { Role = NetRole.Dedicated, Address = bindAddress ?? "", Port = port, BotTeams = Mathf.Clamp(botTeams, 0, 7) }, null);
        }

        private string StartSession(NetSessionOptions options, Loadout loadout)
        {
            INetSession session;
            try { session = NetSessions.Start(options); }
            catch (Exception e) { return e.Message; }
            OnlineLaunch.Current = new OnlineLaunch { Role = options.Role, Loadout = loadout, AccountLevel = Profile?.AccountLevel ?? 1 };
            PracticeLaunch.Current = null;
            session.RoundStarting += OnSessionRoundStarting;
            session.LobbyChanged += OnSessionChanged;
            session.Closed += OnSessionClosed;
            LastReport = null; LastRecap = null; Message = null;
            Phase = FlowPhase.Lobby;
            Changed?.Invoke();
            return null;
        }

        private void OnSessionChanged() { if (this != null) Changed?.Invoke(); }

        /// <summary>The host's Start button (or a dedicated server's timer): everyone loads the round. Returns why not, or null.</summary>
        public string StartOnlineRound(int? seed = null)
        {
            var s = Session;
            if (s == null || !s.IsServer) return "Only the host starts the round.";
            if (Phase != FlowPhase.Lobby) return "Not in the lobby.";
            return s.StartRound(seed ?? UnityEngine.Random.Range(1, 1000000));
        }

        /// <summary>The lobby's Leave: stop the session and go back to the main menu screen.</summary>
        public void LeaveLobby()
        {
            if (Phase != FlowPhase.Lobby) return;
            EndSession();
            Phase = FlowPhase.Menu;
            Changed?.Invoke();
        }

        private void OnSessionRoundStarting()
        {
            if (this == null) return;
            if (Phase != FlowPhase.Lobby) return;   // a reconnect while the round scene is still up
            LastReport = null; LastRecap = null; Message = null; _settled = false; _sim = null; _forfeitAt = -1f;
            LocalId = Session != null && Session.LocalPlayerId > 0 ? Session.LocalPlayerId : LocalPlayerId;
            Phase = FlowPhase.LoadingRound;
            Changed?.Invoke();
            Time.timeScale = 1f;
            SceneManager.sceneLoaded += SilenceOnRoundLoad;
            StartCoroutine(LoadThen(RoundScenePath, null, HostRound));
        }

        /// <summary>Runs after the round scene's Awake and before its Start: the dev scene's practice wrapper must not host an offline round under an online one.</summary>
        private static void SilenceOnRoundLoad(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= SilenceOnRoundLoad;
            OnlineMatch.SilenceOfflineHosts();
        }

        private void OnSessionClosed(string reason)
        {
            if (this == null) return;
            Debug.Log("GameFlow: the online session closed: " + reason);
            if (Phase == FlowPhase.Lobby) { Message = reason; EndSession(); Phase = FlowPhase.Menu; Changed?.Invoke(); return; }
            if (Phase == FlowPhase.InRound && !_settled && OnlineLaunch.Current?.Role == NetRole.Client)
            {
                // The round goes on without us; the server settles our character as a death after the window. Nothing here can settle it
                // without the server's result, so the profile is left as it was (the gear is still in it) and the menu says so.
                Message = reason + " Your round could not be settled: the server records it as a death if you do not come back within 60 s.";
                ReturnToMenu();
            }
        }

        private void EndSession()
        {
            var s = NetSessions.Current;
            if (s != null) { s.RoundStarting -= OnSessionRoundStarting; s.LobbyChanged -= OnSessionChanged; s.Closed -= OnSessionClosed; }
            NetSessions.StopCurrent();
            OnlineLaunch.Current = null;
            ClientMirror.Current = null;
            LocalId = LocalPlayerId;
        }

        /// <summary>A dedicated server's round is over: back to the lobby (and the menu scene) for the next one, with everyone still connected.</summary>
        public void DedicatedRoundOver()
        {
            var s = Session;
            if (s == null || s.Role != NetRole.Dedicated) return;
            s.EndRound();
            Phase = FlowPhase.LoadingMenu;
            StartCoroutine(LoadThen(MenuScenePath, () => { Phase = FlowPhase.Lobby; Changed?.Invoke(); }));
        }

        private bool IsOnlineClient => OnlineLaunch.Current != null && OnlineLaunch.Current.Role == NetRole.Client;
        private float _forfeitAt = -1f;

        // ---------------- pause ----------------

        /// <summary>Is the running round paused (offline only)?</summary>
        public static bool RoundPaused => OfflineMatch.Current != null && OfflineMatch.Current.Paused;

        /// <summary>
        /// The pause menu opened or closed. Offline (the round hosted here by <see cref="OfflineMatch"/>, alone) the simulation
        /// stops and time freezes; a networked round goes on. Returns whether the round is now paused.
        /// </summary>
        public static bool SetRoundPaused(bool paused)
        {
            var match = OfflineMatch.Current;
            if (match == null || !match.IsOffline) { if (!paused) { Time.timeScale = 1f; if (!LaunchFlags.Has("-mute")) AudioListener.pause = false; } return false; }
            match.SetPaused(paused);
            // Sound stops with the dungeon. A -mute session keeps the listener paused regardless.
            if (!LaunchFlags.Has("-mute")) AudioListener.pause = match.Paused;
            return match.Paused;
        }

        /// <summary>Can the running round pause (it is offline)?</summary>
        public static bool RoundCanPause => OfflineMatch.Current != null && OfflineMatch.Current.IsOffline;

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
            if (IsOnlineClient)
            {
                // A client settles from the server's word on its round.
                if (ClientMirror.Current?.Result != null) SettleNow();
                return;
            }
            var server = Server;
            if (server == null || server.Sim == null || !server.State.PlayerTeam.ContainsKey(LocalPlayerId)) return;
            if (RoundSettlement.OutcomeOf(server.State, LocalPlayerId) == null) return;
            SettleNow();
        }

        /// <summary>A client's settlement: the server's <see cref="RoundResult"/> applied to this profile, the brought ids resolved to the loadout objects it sent.</summary>
        private SettlementReport SettleFromResult()
        {
            var result = ClientMirror.Current?.Result;
            var bootstrap = FindFirstObjectByType<DungeonMatchBootstrap>();
            var launch = OnlineLaunch.Current;
            if (result == null || bootstrap == null || bootstrap.Descriptor == null || bootstrap.State == null || Profile == null || Character == null || launch?.Loadout == null) return null;
            var report = result.Settle(Profile, Character, launch.Loadout.Brought.ToList(), bootstrap.Descriptor, bootstrap.State.Layout);
            if (report.Outcome == RoundOutcome.Died) LastRecap = result.Recap;
            return report;
        }

        /// <summary>Settle the local player's finished round into the profile and save it. Called by the watcher; public for tests.</summary>
        public SettlementReport SettleNow()
        {
            if (_settled) return LastReport;
            var server = Server;
            if (!IsOnlineClient && (server == null || Profile == null || Character == null)) return null;
            try
            {
                SettlementReport report;
                if (IsOnlineClient) { report = SettleFromResult(); if (report == null) return null; }
                else
                {
                    report = RoundSettlement.Settle(Profile, Character, server, LocalPlayerId);
                    if (report.Outcome == RoundOutcome.Died) LastRecap = DeathRecap.For(server.State, LocalPlayerId, server.Sim?.Hits);
                }
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
                if (IsOnlineClient)
                {
                    var st = FindFirstObjectByType<DungeonMatchBootstrap>()?.State;
                    return st != null && ClientMirror.Current?.Result == null && st.PlayerTeam.ContainsKey(LocalId) && RoundSettlement.OutcomeOf(st, LocalId) == null;
                }
                var s = Server;
                return s != null && s.State.PlayersInside.Contains(LocalPlayerId);
            }
        }

        /// <summary>
        /// End the local player's round as a death (the pause menu's Leave, and a downed player giving up). The killer is
        /// whoever downed them, if anyone, so an enemy still earns the soul. Settles at once. On a client the server records it
        /// and sends the result, which settles it a moment later.
        /// </summary>
        public void ForfeitRound()
        {
            if (IsOnlineClient)
            {
                if (_forfeitAt < 0f) { _forfeitAt = Time.unscaledTime; Session?.SendControl(NetControl.Forfeit, Array.Empty<byte>()); }
                return;
            }
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
            if (IsOnlineClient && Phase == FlowPhase.InRound && !_settled) { StartCoroutine(LeaveOnline()); return; }
            if (Phase == FlowPhase.InRound) ForfeitRound();
            var r = LastReport;
            if (r != null && !r.AlreadySettled)
                Message = $"You left the round. It counts as a death: {r.ItemsLost.Count} item{(r.ItemsLost.Count == 1 ? "" : "s")} lost, +{r.XpDelta} XP.";
            ReturnToMenu();
        }

        /// <summary>A client leaving: the server records the forfeit and sends the result; settle from it (up to 10 s), then go.</summary>
        private IEnumerator LeaveOnline()
        {
            ForfeitRound();
            float t = Time.unscaledTime;
            while (!_settled && Time.unscaledTime - t < 10f) yield return null;
            var r = LastReport;
            if (r != null && !r.AlreadySettled)
                Message = $"You left the round. It counts as a death: {r.ItemsLost.Count} item{(r.ItemsLost.Count == 1 ? "" : "s")} lost, +{r.XpDelta} XP.";
            else if (!_settled) Message = "You left the round; the server did not answer, so it will count as a death there.";
            ReturnToMenu();
        }

        /// <summary>Back to the main menu (from the summary, or from a greybox round with no flow behind it). Online, this ends the session (a host's round ends for everyone).</summary>
        public void ReturnToMenu()
        {
            if (Phase == FlowPhase.LoadingMenu) return;
            if (OnlineLaunch.Current != null && OnlineLaunch.Current.Role != NetRole.Dedicated) EndSession();
            PracticeLaunch.Current = null;
            _sim = null;
            SetRoundPaused(false);
            Time.timeScale = 1f;
            Phase = FlowPhase.LoadingMenu;
            Changed?.Invoke();
            StartCoroutine(LoadThen(MenuScenePath, () => { Phase = FlowPhase.Menu; Changed?.Invoke(); }));
        }

        private IEnumerator LoadThen(string path, Action done, Func<IEnumerator> then = null)
        {
            var op = LoadScene(path);
            if (op == null) { Debug.LogError("GameFlow: cannot load " + path); Phase = FlowPhase.Menu; yield break; }
            while (!op.isDone) yield return null;
            done?.Invoke();
            if (then != null) yield return then();
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
