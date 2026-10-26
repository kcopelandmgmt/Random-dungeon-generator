using System;
using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Transporting;

namespace DungeonGen.Unity
{
    /// <summary>
    /// Everything this game sends over Fishnet is one broadcast: a kind byte and the bytes the core already wrote (descriptor, event,
    /// view, snapshot, ping, intent, or a <see cref="NetControl"/> message offset by <see cref="FishnetWire.ControlBase"/>). No NetworkObjects,
    /// no scene synchronisation: each side loads its own scenes and the dungeon is rebuilt from the descriptor.
    /// </summary>
    public struct DgMessage : IBroadcast
    {
        public byte Kind;
        public byte[] Data;
    }

    /// <summary>The kinds of <see cref="DgMessage"/>.</summary>
    public static class FishnetWire
    {
        public const byte Descriptor = 1, Event = 2, View = 3, Snapshot = 4, TeamPing = 5, Intent = 6;
        /// <summary><see cref="NetControl"/> messages travel as ControlBase + the control byte.</summary>
        public const byte ControlBase = 100;
    }

    /// <summary>
    /// The match transport over Fishnet, one per round. On the server it maps each seated connection to its player and team
    /// (<see cref="Seat"/>), so events, views and snapshots reach a team's connections only and an intent counts only for the player its
    /// connection was seated as; the host's own player is a local seat whose messages are delivered in-process at the end of the frame
    /// (<see cref="Pump"/>), like the loopback transport. Each connection gets the descriptor once (a reconnect gets it again). On a client it
    /// buffers the round's messages until the round scene is ready (<see cref="SetReady"/>), drops snapshots until the descriptor was delivered,
    /// and ignores a repeated identical descriptor so a reconnect never rebuilds the dungeon.
    /// </summary>
    public sealed class FishnetMatchTransport : IMatchTransport, IMatchLocalSeat
    {
        private readonly FishnetSession _session;
        public bool IsServer { get; }

        // Server.
        private readonly Dictionary<int, (NetworkConnection conn, int team)> _remote = new Dictionary<int, (NetworkConnection, int)>();
        private readonly HashSet<NetworkConnection> _descriptorSent = new HashSet<NetworkConnection>();
        private byte[] _descriptor;
        private int _localPlayer = -1, _localTeam = -1;
        private readonly Queue<Action> _local = new Queue<Action>();

        // Client.
        private bool _ready, _descriptorDelivered;
        private byte[] _lastDescriptor;
        private readonly Queue<(byte kind, byte[] data)> _buffer = new Queue<(byte, byte[])>();

        public event Action<byte[]> DescriptorReceived;
        public event Action<byte[]> EventReceived;
        public event Action<byte[]> ViewReceived;
        public event Action<byte[]> SnapshotReceived;
        public event Action<int, byte[]> IntentReceived;
        public event Action<int, int> ClientJoined;
        public event Action<byte[]> TeamMessageReceived;

        public FishnetMatchTransport(FishnetSession session, bool isServer) { _session = session; IsServer = isServer; }

        /// <summary>Players seated on a connection this round.</summary>
        public IEnumerable<int> SeatedPlayers => _remote.Keys;
        public bool IsSeated(int playerId) => _remote.ContainsKey(playerId);
        public int LocalPlayer => _localPlayer;

        // ---------------- server: seats ----------------

        /// <summary>Server: this connection now speaks for this player on this team. Sends the descriptor if it has not had it and raises ClientJoined (the bootstrap sends the team's view).</summary>
        public void Seat(int playerId, NetworkConnection conn, int team)
        {
            if (!IsServer || conn == null) return;
            _remote[playerId] = (conn, team);
            if (_descriptor != null && _descriptorSent.Add(conn)) _session.SendTo(conn, FishnetWire.Descriptor, _descriptor, Channel.Reliable);
            ClientJoined?.Invoke(conn.ClientId, team);
        }

        /// <summary>Server: the player's connection is gone; nothing more is sent to it.</summary>
        public void Unseat(int playerId)
        {
            if (_remote.TryGetValue(playerId, out var s)) { _descriptorSent.Remove(s.conn); _remote.Remove(playerId); }
        }

        /// <summary>Server (host): the host's own player, delivered in-process.</summary>
        public void SeatLocal(int playerId, int team) { _localPlayer = playerId; _localTeam = team; }

        /// <summary>Server (host): the local player is ready for its team's view.</summary>
        public void JoinLocal() { if (_localTeam >= 0) ClientJoined?.Invoke(0, _localTeam); }

        /// <summary>Deliver the host's local messages queued this frame.</summary>
        public void Pump() { int n = _local.Count; for (int i = 0; i < n; i++) _local.Dequeue()(); }

        public bool TryPlayerOf(NetworkConnection conn, out int playerId)
        {
            foreach (var kv in _remote) if (kv.Value.conn == conn) { playerId = kv.Key; return true; }
            playerId = -1; return false;
        }

        private void ToTeam(int team, byte kind, byte[] bytes, Channel channel, Action<byte[]> local)
        {
            foreach (var kv in _remote) if (kv.Value.team == team) _session.SendTo(kv.Value.conn, kind, bytes, channel);
            if (local != null && _localTeam == team && _localPlayer >= 0) _local.Enqueue(() => local(bytes));
        }

        // ---------------- IMatchTransport: server → clients ----------------

        public void BroadcastDescriptor(byte[] bytes)
        {
            if (!IsServer) return;
            _descriptor = bytes;
            foreach (var kv in _remote) if (_descriptorSent.Add(kv.Value.conn)) _session.SendTo(kv.Value.conn, FishnetWire.Descriptor, bytes, Channel.Reliable);
        }

        public void SendEventToTeam(int team, byte[] bytes) => ToTeam(team, FishnetWire.Event, bytes, Channel.Reliable, b => EventReceived?.Invoke(b));
        public void SendViewToTeam(int team, byte[] bytes) => ToTeam(team, FishnetWire.View, bytes, Channel.Reliable, b => ViewReceived?.Invoke(b));
        public void SendSnapshotToTeam(int team, byte[] bytes) => ToTeam(team, FishnetWire.Snapshot, bytes, Channel.Unreliable, b => SnapshotReceived?.Invoke(b));

        public void SendToTeam(int team, byte[] bytes)
        {
            if (IsServer) ToTeam(team, FishnetWire.TeamPing, bytes, Channel.Unreliable, b => TeamMessageReceived?.Invoke(b));
            else _session.SendToServer(FishnetWire.TeamPing, bytes, Channel.Unreliable);   // the server relays it to our team only
        }

        // ---------------- IMatchTransport: client → server ----------------

        public void SendIntent(byte[] bytes)
        {
            if (IsServer) { if (_localPlayer >= 0) { int p = _localPlayer; _local.Enqueue(() => IntentReceived?.Invoke(p, bytes)); } }
            else _session.SendToServer(FishnetWire.Intent, bytes, Channel.Unreliable);
        }

        // ---------------- server: what arrived from a connection ----------------

        /// <summary>Server: a message from a connection. Only a seated connection's intents count, and only for its own player.</summary>
        public void OnClientMessage(NetworkConnection conn, byte kind, byte[] data)
        {
            if (!IsServer || !TryPlayerOf(conn, out int player)) return;
            switch (kind)
            {
                case FishnetWire.Intent: IntentReceived?.Invoke(player, data); break;
                case FishnetWire.TeamPing: SendToTeam(_remote[player].team, data); break;   // a ping never crosses teams
            }
        }

        // ---------------- client: what arrived from the server ----------------

        public void OnServerMessage(byte kind, byte[] data)
        {
            if (IsServer) return;
            if (!_ready) { if (kind != FishnetWire.Snapshot || _buffer.Count < 400) _buffer.Enqueue((kind, data)); return; }
            Deliver(kind, data);
        }

        /// <summary>Client: the round scene is attached; deliver everything buffered, in arrival order.</summary>
        public void SetReady()
        {
            if (_ready) return;
            _ready = true;
            while (_buffer.Count > 0) { var (k, d) = _buffer.Dequeue(); Deliver(k, d); }
        }

        private void Deliver(byte kind, byte[] data)
        {
            switch (kind)
            {
                case FishnetWire.Descriptor:
                    if (_lastDescriptor != null && Same(_lastDescriptor, data)) return;   // a reconnect: the dungeon is already built
                    _lastDescriptor = data; _descriptorDelivered = true;
                    DescriptorReceived?.Invoke(data); break;
                case FishnetWire.Event: if (_descriptorDelivered) EventReceived?.Invoke(data); break;
                case FishnetWire.View: if (_descriptorDelivered) ViewReceived?.Invoke(data); break;
                case FishnetWire.Snapshot: if (_descriptorDelivered) SnapshotReceived?.Invoke(data); break;
                case FishnetWire.TeamPing: if (_descriptorDelivered) TeamMessageReceived?.Invoke(data); break;
            }
        }

        private static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
