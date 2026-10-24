using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// The match transport over Fishnet. One scene object with this NetworkBehaviour carries everything the
    /// dungeon layer sends: the descriptor to all, events, views and snapshots to a team (as TargetRpcs to
    /// each connection seated on that team), intents from clients (ServerRpc), pings to a team. Seating a
    /// connection on a team is the server's call (<see cref="Seat"/>); until seated, a connection receives
    /// nothing but the descriptor. Compiled only when the FISHNET define is present (Fishnet adds it).
    /// </summary>
    public sealed class FishnetMatchTransport : NetworkBehaviour, IMatchTransport
    {
        private readonly Dictionary<NetworkConnection, (int player, int team)> _seats = new Dictionary<NetworkConnection, (int, int)>();
        private readonly Dictionary<int, NetworkConnection> _byPlayer = new Dictionary<int, NetworkConnection>();
        private byte[] _descriptor;

        public bool IsServer => base.IsServerInitialized;
        public event Action<byte[]> DescriptorReceived;
        public event Action<byte[]> EventReceived;
        public event Action<byte[]> ViewReceived;
        public event Action<byte[]> SnapshotReceived;
        public event Action<int, byte[]> IntentReceived;
        public event Action<int, int> ClientJoined;
        public event Action<byte[]> TeamMessageReceived;

        /// <summary>Server: seat a connection as a player on a team. Raises ClientJoined so the bootstrap sends the descriptor and the team's view.</summary>
        public void Seat(NetworkConnection conn, int playerId, int team)
        {
            _seats[conn] = (playerId, team); _byPlayer[playerId] = conn;
            if (_descriptor != null) TargetDescriptor(conn, _descriptor);
            ClientJoined?.Invoke(conn.ClientId, team);
        }
        public bool TryPlayerOf(NetworkConnection conn, out int playerId) { if (_seats.TryGetValue(conn, out var s)) { playerId = s.player; return true; } playerId = -1; return false; }

        public override void OnStopServer() { base.OnStopServer(); _seats.Clear(); _byPlayer.Clear(); }

        // ---- server → clients ----
        public void BroadcastDescriptor(byte[] bytes) { _descriptor = bytes; foreach (var conn in _seats.Keys) TargetDescriptor(conn, bytes); }
        public void SendEventToTeam(int team, byte[] bytes) { foreach (var kv in _seats) if (kv.Value.team == team) TargetEvent(kv.Key, bytes); }
        public void SendViewToTeam(int team, byte[] bytes) { foreach (var kv in _seats) if (kv.Value.team == team) TargetView(kv.Key, bytes); }
        public void SendSnapshotToTeam(int team, byte[] bytes) { foreach (var kv in _seats) if (kv.Value.team == team) TargetSnapshot(kv.Key, bytes); }
        public void SendToTeam(int team, byte[] bytes)
        {
            if (IsServer) { foreach (var kv in _seats) if (kv.Value.team == team) TargetTeamMessage(kv.Key, bytes); }
            else ServerRelayTeamMessage(bytes);
        }

        [TargetRpc] private void TargetDescriptor(NetworkConnection conn, byte[] bytes) => DescriptorReceived?.Invoke(bytes);
        [TargetRpc] private void TargetEvent(NetworkConnection conn, byte[] bytes) => EventReceived?.Invoke(bytes);
        [TargetRpc] private void TargetView(NetworkConnection conn, byte[] bytes) => ViewReceived?.Invoke(bytes);
        [TargetRpc(Channel = Channel.Unreliable)] private void TargetSnapshot(NetworkConnection conn, byte[] bytes) => SnapshotReceived?.Invoke(bytes);
        [TargetRpc(Channel = Channel.Unreliable)] private void TargetTeamMessage(NetworkConnection conn, byte[] bytes) => TeamMessageReceived?.Invoke(bytes);

        // ---- clients → server ----
        public void SendIntent(byte[] bytes) => ServerIntent(bytes);

        [ServerRpc(RequireOwnership = false, Channel = Channel.Unreliable)]
        private void ServerIntent(byte[] bytes, NetworkConnection sender = null)
        {
            if (sender == null || !_seats.TryGetValue(sender, out var seat)) return;   // unseated connections have no say
            IntentReceived?.Invoke(seat.player, bytes);
        }

        [ServerRpc(RequireOwnership = false, Channel = Channel.Unreliable)]
        private void ServerRelayTeamMessage(byte[] bytes, NetworkConnection sender = null)
        {
            if (sender == null || !_seats.TryGetValue(sender, out var seat)) return;
            foreach (var kv in _seats) if (kv.Value.team == seat.team) TargetTeamMessage(kv.Key, bytes);   // a ping never crosses teams
        }
    }
}
