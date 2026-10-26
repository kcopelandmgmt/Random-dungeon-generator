using System;
using System.Collections.Generic;
using DungeonGen.Core.Net;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// A remote client's side of the round transport (2026-10-26 online play beyond the LAN), wrapped around the session's: it tells the prediction about
    /// every intent the moment it is sent (<see cref="IntentSent"/>), and it can add a simulated round trip (<c>-netlag &lt;ms&gt;</c>, or the
    /// <c>DG_NETLAG</c> environment variable for the lane script's players), half on what the client sends and half on what it receives, in order,
    /// so latency can be tried on one machine. With no lag everything passes straight through. <see cref="Pump"/> once a frame delivers what is due.
    /// </summary>
    public sealed class ClientTransport : IMatchTransport
    {
        public const string LagFlag = "-netlag", LagEnvironment = "DG_NETLAG";
        public const int MaxLagMs = 2000;

        /// <summary>The simulated round trip asked for at launch, in milliseconds (0 when none).</summary>
        public static int LaunchLagMs()
        {
            string v = LaunchFlags.Value(LagFlag);
            if (string.IsNullOrEmpty(v)) { try { v = Environment.GetEnvironmentVariable(LagEnvironment); } catch (Exception) { v = null; } }
            return int.TryParse(v, out int ms) && ms > 0 ? Math.Min(ms, MaxLagMs) : 0;
        }

        public IMatchTransport Inner { get; }
        /// <summary>The simulated round trip added (seconds).</summary>
        public float LagSeconds { get; }
        /// <summary>The clock the delays run on (real time by default; tests may drive it).</summary>
        public Func<float> Clock = () => Time.realtimeSinceStartup;
        /// <summary>Every intent this client sends, decoded, before any simulated delay: what the prediction walks with.</summary>
        public event Action<PlayerIntent> IntentSent;

        private readonly Queue<(float due, Action deliver)> _down = new Queue<(float, Action)>();
        private readonly Queue<(float due, byte[] bytes)> _up = new Queue<(float, byte[])>();

        public ClientTransport(IMatchTransport inner, int lagMs = 0)
        {
            Inner = inner ?? throw new ArgumentNullException(nameof(inner));
            LagSeconds = Mathf.Clamp(lagMs, 0, MaxLagMs) / 1000f;
            inner.DescriptorReceived += b => Down(() => DescriptorReceived?.Invoke(b));
            inner.EventReceived += b => Down(() => EventReceived?.Invoke(b));
            inner.ViewReceived += b => Down(() => ViewReceived?.Invoke(b));
            inner.SnapshotReceived += b => Down(() => SnapshotReceived?.Invoke(b));
            inner.TeamMessageReceived += b => Down(() => TeamMessageReceived?.Invoke(b));
            inner.IntentReceived += (p, b) => IntentReceived?.Invoke(p, b);
            inner.ClientJoined += (c, t) => ClientJoined?.Invoke(c, t);
        }

        private float Half => LagSeconds * 0.5f;

        private void Down(Action deliver)
        {
            if (LagSeconds <= 0f && _down.Count == 0) { deliver(); return; }
            _down.Enqueue((Clock() + Half, deliver));
        }

        /// <summary>Deliver everything whose delay is over, both ways, in the order it was sent.</summary>
        public void Pump()
        {
            float now = Clock();
            // Intents due together leave as one: the server keeps only the newest intent per tick, so a press (cast, ping) in an
            // older one would be lost to the delay queue's bunching, which a real link spreads out.
            byte[] last = null; PlayerIntent merged = default; int due = 0;
            while (_up.Count > 0 && _up.Peek().due <= now)
            {
                last = _up.Dequeue().bytes;
                if (++due == 1) { if (_up.Count == 0 || _up.Peek().due > now) break; merged = PlayerIntent.FromBytes(last); continue; }
                var next = PlayerIntent.FromBytes(last);
                next.Basic |= merged.Basic; next.Bolt |= merged.Bolt; next.Zone |= merged.Zone; next.Mobility |= merged.Mobility; next.Ultimate |= merged.Ultimate;
                if (merged.Ping && !next.Ping) { next.Ping = true; next.PingKind = merged.PingKind; }
                if (next.ConsumableSlot == 0) next.ConsumableSlot = merged.ConsumableSlot;
                if (!next.AnyItemAction) { next.TakeItemId = merged.TakeItemId; next.DropItemId = merged.DropItemId; next.RollItemId = merged.RollItemId; next.RollChoice = merged.RollChoice; next.SalvageItemId = merged.SalvageItemId; }
                if (next.ShrineAction == 0) { next.ShrineAction = merged.ShrineAction; next.ShrineArg = merged.ShrineArg; }
                merged = next;
            }
            if (due == 1) Inner.SendIntent(last);
            else if (due > 1) Inner.SendIntent(merged.ToBytes());
            while (_down.Count > 0 && _down.Peek().due <= now) _down.Dequeue().deliver();
        }

        /// <summary>Messages still held back by the simulated lag (for tests).</summary>
        public int Pending => _down.Count + _up.Count;

        public bool IsServer => Inner.IsServer;
        public void BroadcastDescriptor(byte[] b) => Inner.BroadcastDescriptor(b);
        public void SendEventToTeam(int team, byte[] b) => Inner.SendEventToTeam(team, b);
        public void SendViewToTeam(int team, byte[] b) => Inner.SendViewToTeam(team, b);
        public void SendToTeam(int team, byte[] b) => Inner.SendToTeam(team, b);
        public void SendSnapshotToTeam(int team, byte[] b) => Inner.SendSnapshotToTeam(team, b);

        public void SendIntent(byte[] bytes)
        {
            if (IntentSent != null)
            {
                try { IntentSent(PlayerIntent.FromBytes(bytes)); }
                catch (Exception e) { Debug.LogWarning("ClientTransport: an intent could not be read back: " + e.Message); }
            }
            if (LagSeconds <= 0f && _up.Count == 0) Inner.SendIntent(bytes);
            else _up.Enqueue((Clock() + Half, bytes));
        }

        public event Action<byte[]> DescriptorReceived;
        public event Action<byte[]> EventReceived;
        public event Action<byte[]> ViewReceived;
        public event Action<byte[]> SnapshotReceived;
        public event Action<int, byte[]> IntentReceived;
        public event Action<int, int> ClientJoined;
        public event Action<byte[]> TeamMessageReceived;
    }
}
