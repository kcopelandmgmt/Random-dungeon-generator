using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DungeonGen.Core.Net;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// The online session over Fishnet (UDP through Fishnet's Tugboat transport), made from code: a NetworkManager on a DontDestroyOnLoad
    /// object of its own, no NetworkObjects, every message a <see cref="DgMessage"/> broadcast. Registered as the
    /// <see cref="NetSessions.Factory"/> at load.
    /// Server (host or dedicated): listens, keeps the lobby (each join is a <see cref="JoinRequest"/>; teams fill to the team size, then the next;
    /// the host's own player is seat 1), starts the round for everyone (<see cref="StartRound"/>), maps connections to players for the round's
    /// <see cref="FishnetMatchTransport"/>, and treats a join with a seated player's account and character during the round as a reconnect.
    /// Client: connects, sends its join once Fishnet authenticated it, follows the lobby, and on <see cref="NetControl.RoundStarting"/> makes the
    /// round's transport (which buffers until the scene is ready). A connection lost mid-round is retried every two seconds for the 60 s reconnect window.
    /// </summary>
    public sealed class FishnetSession : MonoBehaviour, INetSession
    {
        public const int HostPlayerId = 1, FirstRemotePlayerId = 2;
        public const float ReconnectSeconds = 60f, RetryEverySeconds = 2f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register() => NetSessions.Factory = Create;

        private static INetSession Create(NetSessionOptions options)
        {
            var go = new GameObject("FishnetSession-" + options.Role);
            go.SetActive(false);
            DontDestroyOnLoad(go);
            // In the editor, AddComponent runs the NetworkManager's Reset, which complains that no prefab collection is set yet (it is, on the next line).
            NetworkManager nm;
            bool logging = Debug.unityLogger.logEnabled;
            Debug.unityLogger.logEnabled = false;
            try { nm = go.AddComponent<NetworkManager>(); }
            finally { Debug.unityLogger.logEnabled = logging; }
            nm.SpawnablePrefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();   // no networked prefabs: broadcasts only
            SetPrivate(nm, "_persistence", NetworkManager.PersistenceType.AllowMultiple);   // tests run a server and a client in one process
            var tug = go.AddComponent<Tugboat>();
            var session = go.AddComponent<FishnetSession>();
            session._options = options; session._nm = nm; session._tugboat = tug;
            go.SetActive(true);
            session.Begin();
            return session;
        }

        private static void SetPrivate(object target, string field, object value)
        {
            var f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f != null) f.SetValue(target, value);
            else Debug.LogWarning($"FishnetSession: no field {field} on {target.GetType().Name} (Fishnet changed); continuing.");
        }

        private NetSessionOptions _options;
        private NetworkManager _nm;
        private Tugboat _tugboat;
        private FishnetMatchTransport _transport;
        private bool _stopped, _welcomed, _joinSent, _reconnecting;
        private float _lostAt = -1f, _nextRetry;
        private string _status = "";

        public NetRole Role => _options.Role;
        public bool IsServer => Role == NetRole.Host || Role == NetRole.Dedicated;
        public bool Connected => IsServer ? _nm != null && _nm.IsServerStarted : _welcomed && _nm != null && _nm.ClientManager.Started;
        public string Status => _status;
        public int LocalPlayerId { get; private set; } = -1;
        public LobbyState Lobby { get; private set; } = new LobbyState();
        public IMatchTransport Transport => _transport;
        public FishnetMatchTransport RoundTransport => _transport;
        public int RoundSeed { get; private set; }
        public bool InRound { get; private set; }

        public event Action LobbyChanged;
        public event Action RoundStarting;
        public event Action<string> Closed;
        public event Action<NetControl, byte[]> PlayerMessageReceived;
        public event Action<int> PlayerDropped;
        public event Action<int> PlayerRejoined;
        public event Action<int, NetControl, byte[]> ControlReceived;

        // Server bookkeeping.
        private readonly Dictionary<int, JoinRequest> _joins = new Dictionary<int, JoinRequest>();
        private readonly Dictionary<int, NetworkConnection> _connOf = new Dictionary<int, NetworkConnection>();
        private readonly Dictionary<NetworkConnection, int> _playerOf = new Dictionary<NetworkConnection, int>();
        private int _nextPlayerId = FirstRemotePlayerId;
        public IReadOnlyDictionary<int, JoinRequest> Joins => _joins;

        private void Begin()
        {
            Lobby = new LobbyState { BotTeams = _options.BotTeams, TeamSize = Math.Max(1, _options.TeamSize) };
            _tugboat.SetPort(_options.Port);
            if (IsServer)
            {
                _nm.ServerManager.SetStartOnHeadless(false);
                string bind = (_options.Address ?? "").Trim();
                if (bind.Length > 0 && bind != "0.0.0.0")
                {
                    _tugboat.SetServerBindAddress(bind, IPAddressType.IPv4);
                    if (bind == "127.0.0.1") _tugboat.SetServerBindAddress("::1", IPAddressType.IPv6);   // loopback only: no firewall prompt
                }
                _tugboat.SetMaximumClients(Math.Max(1, _options.MaxPlayers));
                _nm.ServerManager.OnRemoteConnectionState += OnRemoteState;
                _nm.ServerManager.RegisterBroadcast<DgMessage>(OnServerBroadcast);
                if (Role == NetRole.Host && _options.Join != null)
                {
                    LocalPlayerId = HostPlayerId;
                    _joins[HostPlayerId] = _options.Join;
                    Lobby.Seats.Add(new LobbySeat { PlayerId = HostPlayerId, Team = 0, Name = _options.Join.CharacterName, ClassName = _options.Join.ClassName, Connected = true, Local = true });
                }
                bool ok = _nm.ServerManager.StartConnection(_options.Port);
                _status = ok ? $"Listening on {(bind.Length > 0 ? bind : "every interface")}:{_options.Port}" : $"Could not listen on port {_options.Port}";
                Debug.Log("FishnetSession: " + _status);
                if (!ok) { Close(_status); return; }
            }
            else
            {
                _tugboat.SetClientAddress(_options.Address);
                _nm.ClientManager.OnAuthenticated += OnAuthenticated;
                _nm.ClientManager.OnClientConnectionState += OnClientState;
                _nm.ClientManager.RegisterBroadcast<DgMessage>(OnClientBroadcast);
                Connect();
            }
        }

        private void Connect()
        {
            _joinSent = false;
            bool ok = _nm.ClientManager.StartConnection(_options.Address, _options.Port);
            _status = ok ? $"Connecting to {_options.Address}:{_options.Port}…" : $"Could not start connecting to {_options.Address}:{_options.Port}";
            Debug.Log("FishnetSession: " + _status);
        }

        private void Update()
        {
            if (_stopped || IsServer) return;
            if (_reconnecting)
            {
                float lost = Time.realtimeSinceStartup - _lostAt;
                if (lost > ReconnectSeconds) { _reconnecting = false; Close("The connection to the server was lost and did not come back within 60 s."); return; }
                _status = $"Connection lost: reconnecting ({Mathf.CeilToInt(ReconnectSeconds - lost)} s)…";
                if (Time.realtimeSinceStartup >= _nextRetry && !_nm.ClientManager.Started)
                {
                    _nextRetry = Time.realtimeSinceStartup + RetryEverySeconds;
                    _nm.ClientManager.StartConnection(_options.Address, _options.Port);
                }
            }
        }

        // ---------------- sending ----------------

        public void SendTo(NetworkConnection conn, byte kind, byte[] data, Channel channel)
        {
            if (conn == null || !conn.IsActive || _nm == null || !_nm.IsServerStarted) return;
            _nm.ServerManager.Broadcast(conn, new DgMessage { Kind = kind, Data = data ?? Array.Empty<byte>() }, true, channel);
        }

        public void SendToServer(byte kind, byte[] data, Channel channel)
        {
            if (_nm == null || !_nm.ClientManager.Started || !_welcomed && kind != FishnetWire.ControlBase + (byte)NetControl.Join) return;
            _nm.ClientManager.Broadcast(new DgMessage { Kind = kind, Data = data ?? Array.Empty<byte>() }, channel);
        }

        private void SendControlTo(NetworkConnection conn, NetControl kind, byte[] payload) => SendTo(conn, (byte)(FishnetWire.ControlBase + (byte)kind), payload, Channel.Reliable);

        public void SendControl(NetControl kind, byte[] payload)
        {
            if (IsServer) return;
            SendToServer((byte)(FishnetWire.ControlBase + (byte)kind), payload, Channel.Reliable);
        }

        public void SendToPlayer(int playerId, NetControl kind, byte[] payload)
        {
            if (_connOf.TryGetValue(playerId, out var conn)) SendControlTo(conn, kind, payload);
        }

        private void BroadcastLobby()
        {
            var bytes = Lobby.ToBytes();
            foreach (var conn in _connOf.Values) SendControlTo(conn, NetControl.Lobby, bytes);
            LobbyChanged?.Invoke();
        }

        private static byte[] Ints(params int[] values) { using var ms = new MemoryStream(); using (var w = new BinaryWriter(ms)) foreach (int v in values) w.Write(v); return ms.ToArray(); }

        // ---------------- server ----------------

        public bool IsPlayerConnected(int playerId) => playerId == LocalPlayerId && Role == NetRole.Host || _connOf.TryGetValue(playerId, out var c) && c.IsActive;

        private void OnRemoteState(NetworkConnection conn, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped) return;
            if (!_playerOf.TryGetValue(conn, out int player)) return;
            _playerOf.Remove(conn);
            if (_connOf.TryGetValue(player, out var current) && current == conn) _connOf.Remove(player);
            Debug.Log($"FishnetSession: player {player} disconnected{(InRound ? " during the round" : "")}.");
            if (InRound)
            {
                _transport?.Unseat(player);
                for (int i = 0; i < Lobby.Seats.Count; i++) if (Lobby.Seats[i].PlayerId == player) { var s = Lobby.Seats[i]; s.Connected = false; Lobby.Seats[i] = s; }
                PlayerDropped?.Invoke(player);
                BroadcastLobby();
            }
            else
            {
                Lobby.Seats.RemoveAll(s => s.PlayerId == player);
                _joins.Remove(player);
                BroadcastLobby();
            }
        }

        private void OnServerBroadcast(NetworkConnection conn, DgMessage msg, Channel channel)
        {
            if (msg.Kind >= FishnetWire.ControlBase)
            {
                var kind = (NetControl)(msg.Kind - FishnetWire.ControlBase);
                if (kind == NetControl.Join) { OnJoin(conn, msg.Data); return; }
                if (_playerOf.TryGetValue(conn, out int p)) ControlReceived?.Invoke(p, kind, msg.Data);
                return;
            }
            _transport?.OnClientMessage(conn, msg.Kind, msg.Data);
        }

        private static string Identity(JoinRequest j) => (j.AccountId ?? "") + "/" + (j.CharacterName ?? "");

        private void OnJoin(NetworkConnection conn, byte[] data)
        {
            JoinRequest join;
            try { join = JoinRequest.FromBytes(data); }
            catch (Exception e) { Refuse(conn, "Your join request could not be read: " + e.Message); return; }
            string id = Identity(join);
            int existing = _joins.Where(kv => kv.Key != LocalPlayerId && Identity(kv.Value) == id).Select(kv => kv.Key).DefaultIfEmpty(-1).First();
            if (InRound)
            {
                if (existing < 0) { Refuse(conn, "A round is already running here. Wait for the next one."); return; }
                if (_connOf.TryGetValue(existing, out var old) && old.IsActive && old != conn) { Refuse(conn, "That character is already connected."); return; }
                Bind(existing, conn);
                for (int i = 0; i < Lobby.Seats.Count; i++) if (Lobby.Seats[i].PlayerId == existing) { var s = Lobby.Seats[i]; s.Connected = true; Lobby.Seats[i] = s; }
                SendControlTo(conn, NetControl.Welcome, Ints(existing, 1));
                SendControlTo(conn, NetControl.Lobby, Lobby.ToBytes());
                SendControlTo(conn, NetControl.RoundStarting, Ints(RoundSeed));
                Debug.Log($"FishnetSession: player {existing} reconnected.");
                PlayerRejoined?.Invoke(existing);
                return;
            }
            string why = join.Refusal(GameFlow.PracticeTier);
            if (why != null) { Refuse(conn, why); return; }
            int player = existing >= 0 ? existing : _nextPlayerId++;
            if (existing < 0)
            {
                int team = LobbyState.TeamFor(Lobby.Seats, Lobby.TeamSize, Lobby.Teams);
                if (team < 0) { Refuse(conn, "Every team is full."); _nextPlayerId--; return; }
                Lobby.Seats.Add(new LobbySeat { PlayerId = player, Team = team, Name = join.CharacterName, ClassName = join.ClassName, Connected = true });
            }
            _joins[player] = join;
            Bind(player, conn);
            SendControlTo(conn, NetControl.Welcome, Ints(player, 0));
            Debug.Log($"FishnetSession: {join.CharacterName} ({join.ClassName}) joined as player {player} on team {Lobby.TeamOf(player)}.");
            BroadcastLobby();
        }

        private void Bind(int player, NetworkConnection conn)
        {
            if (_connOf.TryGetValue(player, out var old) && old != conn) _playerOf.Remove(old);
            _connOf[player] = conn; _playerOf[conn] = player;
        }

        private void Refuse(NetworkConnection conn, string reason)
        {
            Debug.Log("FishnetSession: refused a join: " + reason);
            using var ms = new MemoryStream(); using (var w = new BinaryWriter(ms)) w.Write(reason ?? "");
            SendControlTo(conn, NetControl.Refused, ms.ToArray());
            // Give the message a moment to leave before the connection closes.
            StartCoroutine(KickLater(conn));
        }

        private System.Collections.IEnumerator KickLater(NetworkConnection conn)
        {
            float t = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t < 0.5f) yield return null;
            if (conn != null && conn.IsActive) conn.Disconnect(false);
        }

        public string StartRound(int seed)
        {
            if (!IsServer) return "Only the host starts the round.";
            if (InRound) return "The round is already running.";
            if (Lobby.Seats.Count == 0) return "Nobody has joined yet.";
            InRound = true; RoundSeed = seed; Lobby.Phase = 1;
            _transport = new FishnetMatchTransport(this, true);
            var bytes = Ints(seed);
            foreach (var conn in _connOf.Values) SendControlTo(conn, NetControl.RoundStarting, bytes);
            BroadcastLobby();
            RoundStarting?.Invoke();
            return null;
        }

        public void SeatInRound(int playerId, int team)
        {
            if (!IsServer || _transport == null) return;
            if (playerId == LocalPlayerId && Role == NetRole.Host) { _transport.SeatLocal(playerId, team); return; }
            if (_connOf.TryGetValue(playerId, out var conn) && conn.IsActive) _transport.Seat(playerId, conn, team);
        }

        public void EndRound()
        {
            if (!IsServer) return;
            InRound = false; Lobby.Phase = 0; _transport = null;
            foreach (var p in Lobby.Seats.Where(s => !s.Connected && !s.Local).Select(s => s.PlayerId).ToList()) { _joins.Remove(p); _connOf.Remove(p); }
            Lobby.Seats.RemoveAll(s => !s.Connected && !s.Local);
            BroadcastLobby();
        }

        // ---------------- client ----------------

        private void OnAuthenticated()
        {
            if (_joinSent || _options.Join == null) return;
            _joinSent = true;
            SendToServer((byte)(FishnetWire.ControlBase + (byte)NetControl.Join), _options.Join.ToBytes(), Channel.Reliable);
            _status = $"Connected to {_options.Address}:{_options.Port}; joining…";
        }

        private void OnClientState(ClientConnectionStateArgs args)
        {
            if (_stopped) return;
            if (args.ConnectionState == LocalConnectionState.Started) { if (_reconnecting) Debug.Log("FishnetSession: reconnected; joining again."); return; }
            if (args.ConnectionState != LocalConnectionState.Stopped) return;
            if (InRound && _welcomed)
            {
                if (!_reconnecting) { _reconnecting = true; _lostAt = Time.realtimeSinceStartup; _nextRetry = _lostAt + RetryEverySeconds; Debug.Log("FishnetSession: connection lost during the round; retrying for 60 s."); }
                _joinSent = false;
                return;
            }
            if (!_reconnecting) Close(_welcomed ? "The server closed the connection." : $"Could not reach {_options.Address}:{_options.Port}.");
        }

        private void OnClientBroadcast(DgMessage msg, Channel channel)
        {
            if (msg.Kind < FishnetWire.ControlBase) { _transport?.OnServerMessage(msg.Kind, msg.Data); return; }
            var kind = (NetControl)(msg.Kind - FishnetWire.ControlBase);
            using var ms = new MemoryStream(msg.Data ?? Array.Empty<byte>());
            using var r = new BinaryReader(ms);
            switch (kind)
            {
                case NetControl.Welcome:
                    LocalPlayerId = r.ReadInt32(); _welcomed = true; _reconnecting = false;
                    _status = $"Joined {_options.Address}:{_options.Port} as player {LocalPlayerId}.";
                    Debug.Log("FishnetSession: " + _status);
                    LobbyChanged?.Invoke();
                    break;
                case NetControl.Lobby:
                    Lobby = LobbyState.FromBytes(msg.Data);
                    LobbyChanged?.Invoke();
                    break;
                case NetControl.RoundStarting:
                    RoundSeed = r.ReadInt32();
                    if (InRound && _transport != null) break;   // a reconnect: the round scene and its transport are still here
                    InRound = true;
                    _transport = new FishnetMatchTransport(this, false);
                    Debug.Log($"FishnetSession: the round is starting (seed {RoundSeed}).");
                    RoundStarting?.Invoke();
                    break;
                case NetControl.Refused:
                    string why = r.ReadString();
                    Close("Refused: " + why);
                    break;
                default:
                    PlayerMessageReceived?.Invoke(kind, msg.Data);
                    break;
            }
        }

        public void RoundSceneReady() => _transport?.SetReady();

        // ---------------- shutdown ----------------

        private void Close(string reason)
        {
            if (_stopped) return;
            _status = reason;
            Debug.Log("FishnetSession: closed: " + reason);
            Closed?.Invoke(reason);
            Stop();
        }

        public void Stop()
        {
            if (_stopped) return;
            _stopped = true;
            try
            {
                if (_nm != null)
                {
                    if (_nm.IsServerStarted) _nm.ServerManager.StopConnection(true);
                    if (_nm.ClientManager.Started) _nm.ClientManager.StopConnection();
                }
            }
            catch (Exception e) { Debug.LogWarning("FishnetSession: stopping threw " + e.Message); }
            if (NetSessions.Current == (INetSession)this) NetSessions.StopCurrent();
            if (this != null) Destroy(gameObject, 0.25f);
        }

        private void OnDestroy() { _stopped = true; }
        private void OnApplicationQuit() => Stop();
    }
}
