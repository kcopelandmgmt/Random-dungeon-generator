using System;
using System.Collections.Generic;

namespace DungeonGen.Unity
{
    /// <summary>
    /// An in-process transport: the server and one client in the same process, for the practice mode
    /// and for tests of the Unity layer without a network. Messages are delivered at the end of the
    /// frame they were sent in, so ordering matches a real transport. One local client, one team.
    /// </summary>
    public sealed class LoopbackTransport : IMatchTransport
    {
        public bool IsServer { get; set; } = true;
        public int LocalTeam { get; set; } = 0;
        public int LocalPlayerId { get; set; } = 1;
        private readonly Queue<Action> _pending = new Queue<Action>();

        public void BroadcastDescriptor(byte[] b) => _pending.Enqueue(() => DescriptorReceived?.Invoke(b));
        public void SendEventToTeam(int team, byte[] b) { if (team == LocalTeam) _pending.Enqueue(() => EventReceived?.Invoke(b)); }
        public void SendViewToTeam(int team, byte[] b) { if (team == LocalTeam) _pending.Enqueue(() => ViewReceived?.Invoke(b)); }
        public void SendToTeam(int team, byte[] b) { if (team == LocalTeam) _pending.Enqueue(() => TeamMessageReceived?.Invoke(b)); }
        public void SendSnapshotToTeam(int team, byte[] b) { if (team == LocalTeam) _pending.Enqueue(() => SnapshotReceived?.Invoke(b)); }
        public void SendIntent(byte[] b) => _pending.Enqueue(() => IntentReceived?.Invoke(LocalPlayerId, b));

        public event Action<byte[]> DescriptorReceived;
        public event Action<byte[]> EventReceived;
        public event Action<byte[]> ViewReceived;
        public event Action<byte[]> SnapshotReceived;
        public event Action<int, byte[]> IntentReceived;
        public event Action<int, int> ClientJoined;
        public event Action<byte[]> TeamMessageReceived;

        /// <summary>Deliver everything queued. Call once per frame after the server ticked.</summary>
        public void Pump() { int n = _pending.Count; for (int i = 0; i < n; i++) _pending.Dequeue()(); }
        public void Join() => ClientJoined?.Invoke(0, LocalTeam);
    }
}
