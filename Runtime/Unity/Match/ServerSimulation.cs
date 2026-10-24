using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// The frame clock for the engine-free <see cref="MatchServer"/> the bootstrap hosts, and the
    /// place intents arrive from the transport. Nothing in here decides anything: the server loop,
    /// the interaction list and the snapshots live in the core, where the headless round lab runs
    /// the same code. Server authority, no prediction: clients send intents, the core moves them.
    /// </summary>
    public sealed class ServerSimulation : MonoBehaviour
    {
        public DungeonMatchBootstrap Bootstrap;
        public DungeonBuilder Builder;
        [Tooltip("Server ticks per second. Twenty is plenty for a 5 m/s game with 2 m cells.")]
        public int TickRate = 20;

        public MatchServer Server => Bootstrap != null ? Bootstrap.Server : null;
        public FightSim Sim => Server?.Sim;
        public EncounterDirector Director => Server?.Director;
        public RoundClock Clock => Server?.Clock;
        public uint Tick => Server?.Tick ?? 0;
        public bool Running => Server != null && Server.Running;

        private float _accum;

        public void Attach(IMatchTransport transport)
        {
            transport.IntentReceived += (player, bytes) => Server?.SetIntent(player, PlayerIntent.FromBytes(bytes));
        }

        /// <summary>Server: start the round's simulation on the layout the bootstrap generated.</summary>
        public void StartRound() => Server.StartRound();

        /// <summary>Server: seat a player at the team's spawn.</summary>
        public Combatant AddPlayer(int playerId, int team, ClassArchetype cls, string spec, PlayerStats stats, int accountLevel = 1) => Server.AddPlayer(playerId, team, cls, spec, stats, accountLevel);

        /// <summary>Dev practice mode and the tutorial: a bot. Roaming bots play the round (explore, loot, fight, extract); the others stand and fight only.</summary>
        public Combatant AddBot(int botId, int team, string spec, int tier, BotSquad squad = null) => Server.AddBot(botId, team, spec, tier, squad != null ? new RoundBot(botId, squad) : null);

        private void Update()
        {
            if (!Running) return;
            float dt = 1f / Mathf.Max(1, TickRate);
            _accum += Time.deltaTime;
            int steps = 0;
            while (_accum >= dt && steps++ < 5) { _accum -= dt; Server.Step(dt); }
        }

        /// <summary>One server tick, for tests and tools that drive it without a frame.</summary>
        public void Step(float dt) => Server.Step(dt);

        public TeamSnapshot SnapshotForTeam(int team) => Server.SnapshotForTeam(team);
    }

    /// <summary>Forwards the core server's four outgoing messages to the netcode transport.</summary>
    public sealed class TransportWire : IServerWire
    {
        private readonly IMatchTransport _t;
        public TransportWire(IMatchTransport transport) { _t = transport; }
        public void SendEventToTeam(int team, byte[] b) => _t.SendEventToTeam(team, b);
        public void SendViewToTeam(int team, byte[] b) => _t.SendViewToTeam(team, b);
        public void SendSnapshotToTeam(int team, byte[] b) => _t.SendSnapshotToTeam(team, b);
        public void SendPingToTeam(int team, byte[] b) => _t.SendToTeam(team, b);
    }
}
