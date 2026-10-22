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
        public MatchDescriptor Descriptor { get; private set; }
        public bool IsServer => _transport != null && _transport.IsServer;

        private IMatchTransport _transport;

        public void Attach(IMatchTransport transport)
        {
            _transport = transport;
            transport.DescriptorReceived += OnDescriptorBytes;
            transport.EventReceived += OnEventBytes;
            transport.LogReceived += OnLogBytes;
            transport.ClientJoined += OnClientJoined;
        }

        /// <summary>Server: create the match. Clients receive the descriptor and build on their own.</summary>
        public void HostMatch(int seed)
        {
            if (!IsServer) throw new InvalidOperationException("HostMatch on a client.");
            var rules = new MatchRules { RoundSeconds = RoundSeconds, ExtractsOpenAfterSeconds = ExtractsOpenAfterSeconds, BossExitStaysOpen = BossExitStaysOpen, TeamSize = TeamSize };
            Descriptor = MatchDescriptor.Create(seed, Config.Settings, rules, out var layout);
            State = new MatchState(Descriptor, layout);
            BuildScene(layout);
            _transport.BroadcastDescriptor(Descriptor.ToBytes());
            OnMatchReady?.Invoke(State);
        }

        /// <summary>Server: record something that happened and tell everyone. Returns false if the server's own rules reject it.</summary>
        public bool Raise(MatchEvent e)
        {
            if (!IsServer) throw new InvalidOperationException("Only the server raises events.");
            if (!State.Apply(e, out string why)) { Debug.LogWarning($"Rejected {e}: {why}"); return false; }
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms)) e.Write(w);
            _transport.BroadcastEvent(ms.ToArray());
            if (e.Kind == MatchEventKind.BossExitOpened) Builder?.OpenBossGatedExit();
            return true;
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
            var e = MatchEvent.Read(r);
            if (!State.Apply(e, out string why)) { OnDesync?.Invoke($"Server event rejected locally: {e} ({why})"); return; }
            if (e.Kind == MatchEventKind.BossExitOpened) Builder?.OpenBossGatedExit();
        }

        private void OnLogBytes(byte[] bytes)
        {
            if (IsServer || Descriptor == null) return;
            var layout = Descriptor.BuildAndVerify();
            State = MatchState.Replay(Descriptor, layout, MatchState.LogFromBytes(bytes));
            if (State.BossExitOpen) Builder?.OpenBossGatedExit();
        }

        private void OnClientJoined(int clientId)
        {
            if (!IsServer || State == null) return;
            _transport.BroadcastDescriptor(Descriptor.ToBytes());
            _transport.SendLog(clientId, State.LogToBytes());
        }

        private void BuildScene(DungeonLayout layout)
        {
            if (Builder == null) return;
            Builder.RandomizeSeed = false;
            Builder.Build(layout.Seed);
        }

        /// <summary>World position where a team starts, from the descriptor's team → spawn-room assignment.</summary>
        public Vector3 SpawnPositionForTeam(int team)
        {
            int roomId = Descriptor.TeamSpawnRooms[team];
            return Builder.GridToWorld(State.Layout.Rooms[roomId].Center);
        }
    }
}
