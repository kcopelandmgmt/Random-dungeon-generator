using System;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using DungeonGen.Core.Profile;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// The networked round in the round scene (2026-10-26 online play), the online counterpart of <see cref="OfflineMatch"/>.
    /// On the server (a host, or a dedicated server with no local player): generates the match exactly like the offline round
    /// (<see cref="DungeonMatchBootstrap.HostMatch"/> on the session's seed), seats every lobby player on its team with the loadout its
    /// client sent (the tier wall checked again; a refused loadout is turned away), fills empty teams with roaming bot squads per the host's
    /// choice, and keeps the round honest about connections: a dropped player stands killable for the 60 s window
    /// (<see cref="MatchEventKind.PlayerDisconnected"/>), a returning one is seated on the new connection and gets the descriptor and view
    /// again (<see cref="MatchEventKind.PlayerReconnected"/>), a forfeit is a death, and every remote player whose round is over is sent their
    /// <see cref="RoundResult"/> to settle on their own machine. On a client: attaches the bootstrap, the world view, the HUD and the input to the
    /// session's round transport (through a <see cref="ClientTransport"/>: the prediction's intent tap and the <c>-netlag</c> simulated lag), sets this
    /// process's player id everywhere, keeps the <see cref="ClientMirror"/>, and predicts the local avatar's walking (<see cref="ClientPrediction"/>). Never pauses.
    /// </summary>
    public sealed class OnlineMatch : MonoBehaviour
    {
        public DungeonMatchBootstrap Bootstrap;
        public DungeonBuilder Builder;
        public ServerSimulation Server;
        public ClientWorldView World;
        public PlayerHud Hud;
        public TopDownCamera Camera;
        public MonoBehaviour IntentSource;
        public string[] BotSpecs = { "Berserker", "Warder", "Sharpshooter" };
        public int BotsPerTeam = 2, BotTier = 1;
        [Tooltip("Seconds a dedicated server waits after the round ended (and every result went out) before it goes back to the lobby.")]
        public float DedicatedLingerSeconds = 10f;

        public static OnlineMatch Current { get; private set; }

        public INetSession Session { get; private set; }
        public IMatchTransport Transport { get; private set; }
        public bool Hosting { get; private set; }
        public bool Joined { get; private set; }
        public int LocalPlayerId { get; private set; } = -1;
        public MatchServer MatchServer => Bootstrap != null ? Bootstrap.Server : null;
        /// <summary>Remote client: the session's round transport wrapped with the prediction tap and the simulated lag (<c>-netlag</c>); null on a server.</summary>
        public ClientTransport ClientLink { get; private set; }
        /// <summary>Remote client: the local avatar's movement prediction (null on a server, or with <c>-noprediction</c>).</summary>
        public ClientPrediction Prediction { get; private set; }
        /// <summary>Remote players seated this round, by id, with the team they play on.</summary>
        public IReadOnlyDictionary<int, int> RemoteSeats => _remoteSeats;
        /// <summary>Lobby players the round turned away (a refused loadout), with why.</summary>
        public IReadOnlyDictionary<int, string> Refused => _refused;
        public event Action<OnlineMatch> Hosted;

        private readonly Dictionary<int, int> _remoteSeats = new Dictionary<int, int>();
        private readonly Dictionary<int, string> _refused = new Dictionary<int, string>();
        private readonly HashSet<int> _resultSent = new HashSet<int>();
        private float _endedAt = -1f;
        private bool _returnedToLobby;

        private void Awake() { Current = this; }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            if (ClientMirror.Current != null && ClientMirror.Current.World == World) ClientMirror.Current = null;
            if (Session != null)
            {
                Session.PlayerDropped -= OnDropped; Session.PlayerRejoined -= OnRejoined; Session.ControlReceived -= OnControl; Session.PlayerMessageReceived -= OnPlayerMessage;
            }
        }

        /// <summary>The round scene's online match: the one there, or one added to the bootstrap's object and wired from the scene.</summary>
        public static OnlineMatch InScene()
        {
            var m = Current != null ? Current : FindFirstObjectByType<OnlineMatch>();
            var bootstrap = FindFirstObjectByType<DungeonMatchBootstrap>();
            if (m == null && bootstrap == null) return null;
            if (m == null) m = bootstrap.gameObject.AddComponent<OnlineMatch>();
            m.WireFromScene();
            return m;
        }

        public void WireFromScene()
        {
            if (Bootstrap == null) Bootstrap = FindFirstObjectByType<DungeonMatchBootstrap>();
            if (Builder == null) Builder = Bootstrap != null && Bootstrap.Builder != null ? Bootstrap.Builder : FindFirstObjectByType<DungeonBuilder>();
            if (Server == null) Server = FindFirstObjectByType<ServerSimulation>();
            if (World == null) World = FindFirstObjectByType<ClientWorldView>();
            if (Hud == null) Hud = FindFirstObjectByType<PlayerHud>();
            if (Camera == null) Camera = FindFirstObjectByType<TopDownCamera>();
            if (IntentSource == null)
                foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                    if (mb is IIntentSource) { IntentSource = mb; break; }
        }

        /// <summary>
        /// Before the round scene's Start methods: an online round must not be hosted by the dev scene's practice wrapper (which hosts an offline round on
        /// Start). Disables every practice-assembly component that would, and removes the offline host it made. Called from the scene-loaded callback.
        /// </summary>
        public static void SilenceOfflineHosts()
        {
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (mb == null) continue;
                var n = mb.GetType().Name;
                if (n == "PracticeMode" || n == "VfxAudition" || n == "AutoSmokeTest") mb.enabled = false;
            }
            foreach (var om in FindObjectsByType<OfflineMatch>(FindObjectsSortMode.None)) if (!om.Hosting) Destroy(om);
        }

        private void AttachSession(INetSession session)
        {
            Session = session;
            Transport = session.Transport ?? throw new InvalidOperationException("The session has no round transport: was the round started?");
            Session.PlayerDropped += OnDropped; Session.PlayerRejoined += OnRejoined; Session.ControlReceived += OnControl; Session.PlayerMessageReceived += OnPlayerMessage;
            if (Server != null) Server.GameSpeed = 1f;   // a networked round runs in real time, as a dedicated server does
        }

        private void SetLocalPlayer(int id)
        {
            LocalPlayerId = id;
            GameFlow.LocalId = id;
            World.LocalPlayerId = id;
            foreach (var c in FindObjectsByType<CompassHud>(FindObjectsSortMode.None)) c.LocalPlayerId = id;
            foreach (var m in FindObjectsByType<MiniMapHud>(FindObjectsSortMode.None)) m.LocalPlayerId = id;
            if (IntentSource != null) IntentSource.SendMessage("SetLocalPlayerId", id, SendMessageOptions.DontRequireReceiver);
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)) if (mb is IRoundUi) mb.SendMessage("SetLocalPlayerId", id, SendMessageOptions.DontRequireReceiver);
        }

        private void AttachViews(int localId)
        {
            World.Bootstrap = Bootstrap; World.Builder = Builder; World.Attach(Transport);
            if (localId >= 0) SetLocalPlayer(localId);
            if (Hud != null) { Hud.World = World; Hud.Bootstrap = Bootstrap; Hud.Attach(Transport); }
            if (IntentSource != null) IntentSource.SendMessage("Attach", Transport, SendMessageOptions.DontRequireReceiver);
        }

        // ---------------- server ----------------

        /// <summary>Server (host or dedicated): host the round on the session's seed and seat everyone from the lobby.</summary>
        public void HostOnline(INetSession session)
        {
            if (Hosting) return;
            WireFromScene();
            if (Bootstrap == null || Builder == null || Server == null || World == null) { Debug.LogError("OnlineMatch: the round scene is missing its bootstrap, builder, server simulation or world view."); return; }
            AttachSession(session);   // not "Attach": the scene's components get SendMessage("Attach", transport)
            var transport = Transport;
            Bootstrap.Attach(transport);
            Server.Bootstrap = Bootstrap; Server.Builder = Builder; Server.Attach(transport);
            bool host = session.Role == NetRole.Host;
            AttachViews(host ? session.LocalPlayerId : -1);
            if (Bootstrap.Config == null) Bootstrap.Config = Builder.Config;
            Bootstrap.TeamSize = Math.Max(1, session.Lobby.TeamSize);
            Bootstrap.HostMatch(session.RoundSeed);
            Server.StartRound();
            var server = Server.Server;
            int teams = Bootstrap.Descriptor.TeamSpawnRooms.Count;
            var lobby = session.Lobby;
            lobby.Reseat(teams);   // the lobby counted provisional teams; the dungeon decides how many there are

            foreach (var seat in lobby.Seats.OrderBy(s => s.PlayerId))
            {
                if (!session.Joins.TryGetValue(seat.PlayerId, out var join)) continue;
                if (!seat.Local && !session.IsPlayerConnected(seat.PlayerId)) { _refused[seat.PlayerId] = "left before the round"; continue; }
                string why = join.Refusal(Bootstrap.Descriptor.Settings.Tier);
                if (why == null)
                {
                    try { if (server.AddPlayerWithLoadout(seat.PlayerId, seat.Team, join.ToLoadout(), Math.Max(1, join.AccountLevel)) == null) why = string.Join("; ", server.RecentRejections.Skip(Math.Max(0, server.RecentRejections.Count - 1))); }
                    catch (InvalidOperationException e) { why = e.Message; }
                }
                if (why != null)
                {
                    _refused[seat.PlayerId] = why;
                    Debug.LogWarning($"OnlineMatch: player {seat.PlayerId} ({join.CharacterName}) not seated: {why}");
                    if (!seat.Local) session.SendToPlayer(seat.PlayerId, NetControl.Refused, StringBytes("Your loadout was refused: " + why));
                    continue;
                }
                session.SeatInRound(seat.PlayerId, seat.Team);
                if (!seat.Local) _remoteSeats[seat.PlayerId] = seat.Team;
            }

            // Bots fill the teams nobody joined, as many as the host chose.
            var used = new HashSet<int>(server.State.PlayerTeam.Values);
            int botId = BotRules.BotIdBase, botTeams = 0;
            for (int t = 0; t < teams && botTeams < lobby.BotTeams; t++)
            {
                if (used.Contains(t)) continue;
                var squad = new BotSquad { Team = t };
                for (int b = 0; b < BotsPerTeam; b++) Server.AddBot(botId++, t, BotSpecs[(t * BotsPerTeam + b) % BotSpecs.Length], BotTier, squad);
                botTeams++;
            }
            if (transport is IMatchLocalSeat local) local.JoinLocal();
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)) if (mb is IRoundUi ui) ui.Attach(transport);
            Hosting = true;
            Debug.Log($"OnlineMatch: hosting seed {session.RoundSeed}, {teams} teams, {server.State.PlayerTeam.Count - server.Bots.Count} players ({_remoteSeats.Count} remote), {botTeams} bot teams.");
            Hosted?.Invoke(this);
        }

        private static byte[] StringBytes(string s) { using var ms = new System.IO.MemoryStream(); using (var w = new System.IO.BinaryWriter(ms)) w.Write(s ?? ""); return ms.ToArray(); }

        private void OnDropped(int player)
        {
            var server = MatchServer;
            if (!Hosting || server == null || !server.State.PlayersInside.Contains(player) || server.State.Disconnected.ContainsKey(player)) return;
            server.SetIntent(player, new PlayerIntent { PlayerId = player });   // the character stands where it was
            server.Raise(new MatchEvent(MatchEventKind.PlayerDisconnected, server.State.Time, player));
        }

        private void OnRejoined(int player)
        {
            var server = MatchServer;
            if (!Hosting || server == null || !_remoteSeats.TryGetValue(player, out int team)) return;
            if (server.State.Disconnected.ContainsKey(player) && !server.Raise(new MatchEvent(MatchEventKind.PlayerReconnected, server.State.Time, player)))
                Debug.LogWarning($"OnlineMatch: player {player} came back too late: {server.RecentRejections.LastOrDefault()}");
            Session.SeatInRound(player, team);   // descriptor and view again; snapshots resume
            _resultSent.Remove(player);           // a player whose round ended while away gets the result now
        }

        private void OnControl(int player, NetControl kind, byte[] payload)
        {
            if (kind != NetControl.Forfeit || !Hosting) return;
            Forfeit(MatchServer, player);
        }

        /// <summary>End this player's round as a death (the pause menu's Leave): the killer is whoever downed them, if they are still down.</summary>
        public static void Forfeit(MatchServer s, int player)
        {
            if (s == null || !s.State.PlayersInside.Contains(player)) return;
            int killer = -1;
            foreach (var e in s.State.Log) if (e.Kind == MatchEventKind.PlayerDowned && e.A == player) killer = e.B;
            if (!s.State.Downed.ContainsKey(player)) killer = -1;
            s.Raise(new MatchEvent(MatchEventKind.PlayerDied, s.State.Time, player, killer, Mathf.Max(0, s.RoomOf(player))));
        }

        // ---------------- client ----------------

        /// <summary>Client: attach to the round the server is sending and release the transport's buffered messages.</summary>
        public void JoinOnline(INetSession session)
        {
            if (Joined) return;
            WireFromScene();
            if (Bootstrap == null || Builder == null || World == null) { Debug.LogError("OnlineMatch: the round scene is missing its bootstrap, builder or world view."); return; }
            AttachSession(session);   // not "Attach": the scene's components get SendMessage("Attach", transport)
            // Everything on this client hears the round through the link: the prediction's intent tap, and the simulated lag when asked for.
            ClientLink = new ClientTransport(Transport, ClientTransport.LaunchLagMs());
            Transport = ClientLink;
            if (ClientLink.LagSeconds > 0f) Debug.Log($"OnlineMatch: simulating a {ClientLink.LagSeconds * 1000f:0} ms round trip (-netlag).");
            Bootstrap.Attach(Transport);
            if (Server != null) Server.Bootstrap = Bootstrap;   // never steps: a client has no server
            AttachViews(session.LocalPlayerId);
            ClientMirror.Current = new ClientMirror(session.LocalPlayerId, World, Transport);
            var sheet = OnlineLaunch.Current?.Loadout?.Stats;
            if (ClientPrediction.Enabled && sheet != null)
            {
                Prediction = gameObject.AddComponent<ClientPrediction>();
                if (Server != null) Prediction.ServerTickSeconds = 1f / Mathf.Max(1, Server.TickRate);
                var link = ClientLink;
                Prediction.Begin(link, World, Bootstrap, Builder, sheet, () => (session is INetLatency l ? l.RoundTripSeconds : 0f) + link.LagSeconds);
            }
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)) if (mb is IRoundUi ui) ui.Attach(Transport);
            Joined = true;
            session.RoundSceneReady();
            Debug.Log($"OnlineMatch: joined the round as player {session.LocalPlayerId}.");
        }

        private void OnPlayerMessage(NetControl kind, byte[] payload)
        {
            if (kind != NetControl.RoundResult || ClientMirror.Current == null) return;
            try { ClientMirror.Current.SetResult(RoundResult.FromBytes(payload)); }
            catch (Exception e) { Debug.LogError("OnlineMatch: the round result could not be read: " + e.Message); }
        }

        // ---------------- per frame ----------------

        private void Update() => ClientLink?.Pump();   // a remote client: deliver what the simulated lag held back

        private void LateUpdate()
        {
            if (Transport is IMatchLocalSeat local) local.Pump();
            // The input controller points the camera at the local avatar; without one (a scripted run, a controller turned off) do it here.
            if (Camera != null && Camera.Target == null && World != null && World.LocalAvatar != null) Camera.Target = World.LocalAvatar.transform;
            if (!Hosting) return;
            var server = MatchServer;
            if (server == null) return;
            foreach (var kv in _remoteSeats)
            {
                int p = kv.Key;
                if (_resultSent.Contains(p) || RoundSettlement.OutcomeOf(server.State, p) == null || !Session.IsPlayerConnected(p)) continue;
                try { Session.SendToPlayer(p, NetControl.RoundResult, RoundResult.For(server, p).ToBytes()); _resultSent.Add(p); Debug.Log($"OnlineMatch: sent player {p} their round result."); }
                catch (Exception e) { _resultSent.Add(p); Debug.LogError($"OnlineMatch: no result for player {p}: {e.Message}"); }
            }
            // A dedicated server goes back to the lobby a little after the round ends.
            if (Session.Role == NetRole.Dedicated && !_returnedToLobby)
            {
                bool over = server.State.Ended || !server.State.PlayersInside.Any(p => !BotRules.IsBot(p));
                if (over && _endedAt < 0f) _endedAt = Time.unscaledTime;
                if (over && Time.unscaledTime - _endedAt > DedicatedLingerSeconds) { _returnedToLobby = true; GameFlow.Ensure().DedicatedRoundOver(); }
            }
        }
    }

    /// <summary>A transport with an in-process seat for the host's own player (the Fishnet transport): deliver its queue each frame, and let it join its team's view.</summary>
    public interface IMatchLocalSeat
    {
        void Pump();
        void JoinLocal();
    }
}
