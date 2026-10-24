using System;
using System.IO;
using DungeonGen.Core;
using DungeonGen.Core.Net;
using UnityEngine;
using UnityEngine.Events;

namespace DungeonGen.Unity
{
    /// <summary>
    /// Runs the dungeon layer of a networked match. On the server: generate, build the descriptor,
    /// broadcast it, own the <see cref="MatchState"/> and push events. On a client: receive the
    /// descriptor, rebuild and verify the identical dungeon, mirror the state from events.
    /// The only things that cross the wire are the descriptor and the event log; the dungeon,
    /// its loot and its mobs are regenerated locally from the seed.
    /// </summary>
    public sealed class DungeonMatchBootstrap : MonoBehaviour
    {
        public DungeonConfig Config;
        public DungeonBuilder Builder;
        [Tooltip("Round rules the server sends with the descriptor.")]
        public int RoundSeconds = 1200;
        public int ExtractsOpenAfterSeconds = 240;
        public bool BossExitStaysOpen = true;
        public int TeamSize = 3;

        public UnityEvent<MatchState> OnMatchReady;
        public UnityEvent<string> OnDesync;

        /// <summary>Authoritative on the server, mirrored on clients.</summary>
        public MatchState State { get; private set; }
        /// <summary>The engine-free server loop, on the server only.</summary>
        public MatchServer Server { get; private set; }
        public MatchDescriptor Descriptor { get; private set; }
        public bool IsServer => _transport != null && _transport.IsServer;

        private IMatchTransport _transport;

        public void Attach(IMatchTransport transport)
        {
            _transport = transport;
            transport.DescriptorReceived += OnDescriptorBytes;
            transport.EventReceived += OnEventBytes;
            transport.ViewReceived += OnViewBytes;
            transport.ClientJoined += OnClientJoined;
        }

        /// <summary>Server: create the match. Clients receive the descriptor and build on their own.</summary>
        public void HostMatch(int seed)
        {
            if (!IsServer) throw new InvalidOperationException("HostMatch on a client.");
            var rules = new MatchRules { RoundSeconds = RoundSeconds, ExtractsOpenAfterSeconds = ExtractsOpenAfterSeconds, BossExitStaysOpen = BossExitStaysOpen, TeamSize = TeamSize };
            Server = MatchServer.Host(seed, Config.Settings, rules, new TransportWire(_transport));   // the loot secret never leaves this process
            Descriptor = Server.Descriptor;
            State = Server.State;
            Server.EventRaised += e => { if (e.Kind == MatchEventKind.BossExitOpened) Builder?.OpenBossGatedExit(); };
            Server.EventRejected += (e, why) => Debug.LogWarning($"Rejected {e}: {why}");
            BuildScene(State.Layout);
            _transport.BroadcastDescriptor(Descriptor.ToBytes());
            OnMatchReady?.Invoke(State);
        }

        /// <summary>Client: the last notification the server sent this team. Hook sounds and UI lines here.</summary>
        public UnityEvent<MatchEvent> OnEventNotified;

        /// <summary>
        /// Server: record something that happened, tell the teams allowed to hear about it, and send
        /// every team its refreshed view. Returns false if the server's own rules reject it.
        /// </summary>
        public bool Raise(MatchEvent e)
        {
            if (!IsServer || Server == null) throw new InvalidOperationException("Only the server raises events.");
            return Server.Raise(e);
        }

        private void OnDescriptorBytes(byte[] bytes)
        {
            if (IsServer) return;
            try
            {
                Descriptor = MatchDescriptor.FromBytes(bytes);
                var layout = Descriptor.BuildAndVerify();     // throws on fingerprint mismatch
                State = new MatchState(Descriptor, layout);
                BuildScene(layout);
                OnMatchReady?.Invoke(State);
            }
            catch (Exception ex)
            {
                Debug.LogError("DungeonMatchBootstrap: " + ex.Message, this);
                OnDesync?.Invoke(ex.Message);
            }
        }

        private void OnEventBytes(byte[] bytes)
        {
            if (IsServer || State == null) return;
            using var ms = new MemoryStream(bytes);
            using var r = new BinaryReader(ms);
            var e = MatchEvent.Read(r);                       // a notification only; the facts arrive in the view
            OnEventNotified?.Invoke(e);
            if (e.Kind == MatchEventKind.BossExitOpened) Builder?.OpenBossGatedExit();
        }

        private void OnViewBytes(byte[] bytes)
        {
            if (IsServer || State == null) return;
            State.LoadView(TeamView.FromBytes(bytes));
            if (State.BossExitOpen) Builder?.OpenBossGatedExit();
        }

        private void OnClientJoined(int clientId, int team)
        {
            if (!IsServer || State == null) return;
            _transport.BroadcastDescriptor(Descriptor.ToBytes());
            _transport.SendViewToTeam(team, TeamView.For(State, team).ToBytes());
        }

        private void BuildScene(DungeonLayout layout)
        {
            if (Builder == null) return;
            Builder.RandomizeSeed = false;
            Builder.Build(layout);   // the descriptor's dungeon, never a regeneration from the local config
        }

        /// <summary>Server: raise a batch of events (from the simulation bridge) in order; rejected ones are logged and dropped.</summary>
        public void RaiseAll(System.Collections.Generic.IEnumerable<MatchEvent> events) => Server.RaiseAll(events);

        /// <summary>The team a player id belongs to, from the state, or -1.</summary>
        public int TeamOf(int playerId) => State != null && State.PlayerTeam.TryGetValue(playerId, out int t) ? t : -1;

        /// <summary>World position where a team starts, from the descriptor's team → spawn-room assignment.</summary>
        public Vector3 SpawnPositionForTeam(int team)
        {
            int roomId = Descriptor.TeamSpawnRooms[team];
            return Builder.GridToWorld(State.Layout.Rooms[roomId].Center);
        }
    }
}
