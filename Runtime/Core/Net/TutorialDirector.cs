using System;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Items;

namespace DungeonGen.Core.Net
{
    /// <summary>
    /// The server's half of the tutorial (<see cref="TutorialScript"/>), engine-free so the offline host, the xUnit run and any tool drive the same thing:
    /// seats the two friendly bots (<see cref="TutorialFollower"/>), shuts the spawn room's doors so the door lesson has one to open, and raises
    /// <see cref="MatchEventKind.ExtractsOpened"/> a minute after the room boss falls. Every change is a validated event on the server.
    /// </summary>
    public sealed class TutorialDirector
    {
        public MatchServer Server { get; }
        public int LocalPlayer { get; }
        public TutorialRoute Route { get; }
        public IReadOnlyList<int> BotIds => _bots;
        private readonly List<int> _bots = new List<int>();
        private bool _attached;

        public TutorialDirector(MatchServer server, int localPlayer)
        {
            Server = server ?? throw new ArgumentNullException(nameof(server));
            LocalPlayer = localPlayer;
            Route = TutorialScript.Plan(server.Layout, out string why) ?? throw new InvalidOperationException("This layout cannot host the tutorial: " + why + ". Pick the seed with TutorialScript.PickSeed.");
        }

        /// <summary>After the round started and the player is seated: the bots, the shut doors, and the extract timer on every tick.</summary>
        public void Begin(int firstBotId = BotRules.BotIdBase)
        {
            SeatBots(firstBotId);
            ShutSpawnDoors();
            if (!_attached) { _attached = true; Server.Ticked += _ => Tick(); }
        }

        /// <summary>The two friendly bots on the player's team, following the player.</summary>
        public void SeatBots(int firstBotId = BotRules.BotIdBase)
        {
            var avoid = TutorialScript.CellsOf(Server.Layout, new[] { Route.FinalBossRoom });
            var arena = TutorialScript.CellsOf(Server.Layout, new[] { Route.BossRoom });
            int team = Server.TeamOf(LocalPlayer);
            if (team < 0) team = 0;
            int id = firstBotId;
            foreach (var spec in TutorialScript.BotSpecs)
            {
                while (Server.State.PlayerTeam.ContainsKey(id)) id++;
                if (Server.AddBot(id, team, spec, TutorialScript.BotTier, new TutorialFollower(id, LocalPlayer, avoid, arena)) != null) _bots.Add(id);
                id++;
            }
        }

        /// <summary>Shut every door of the spawn room (the player shuts them: they stand in the room, as the state's reach rule wants).</summary>
        public void ShutSpawnDoors()
        {
            foreach (int door in Route.SpawnDoors)
                if (Server.State.Index.Doors.TryGetValue(door, out var d) && !Server.State.ClosedDoors.Contains(d.cell))
                    Server.Raise(new MatchEvent(MatchEventKind.DoorClosed, Server.Sim?.Time ?? 0f, door, LocalPlayer));
        }

        /// <summary>Every tick: open the extract once it is due.</summary>
        public void Tick()
        {
            float now = Server.Sim?.Time ?? Server.State.Time;
            if (TutorialScript.ExtractsDue(Server.State, now)) Server.Raise(new MatchEvent(MatchEventKind.ExtractsOpened, Math.Max(now, Server.State.Time)));
        }
    }

    /// <summary>
    /// A tutorial companion: keeps a few cells behind the player (on the grid, round walls, never into the final boss's room), fights with the lab brain
    /// (<see cref="SimpleBrain"/>) when a monster near both of them is in sight, passes on every group roll so the player's loot is the player's, and never
    /// opens, loots or prays. Engine-free; the server runs it like any <see cref="IServerBot"/>.
    /// </summary>
    public sealed class TutorialFollower : IServerBot
    {
        public int PlayerId { get; }
        public int Leader { get; }
        /// <summary>Stay this close to the player (cells).</summary>
        public const float FollowCells = 3f;
        /// <summary>Fight a monster this close to the bot that is also this close to the player.</summary>
        public const float EngageCells = 9f, LeaderEngageCells = 12f;

        private readonly ISet<GridPoint> _avoid, _arena;
        private List<GridPoint> _path;
        private GridPoint _pathTo;
        private float _repath;

        /// <param name="avoid">Cells never walked into or fought in (the final boss's room).</param>
        /// <param name="arena">The room boss's room: a monster in it is fought only once the player is in it too (the boss lesson is the player's to start).</param>
        public TutorialFollower(int playerId, int leader, ISet<GridPoint> avoid = null, ISet<GridPoint> arena = null)
        {
            PlayerId = playerId; Leader = leader; _avoid = avoid ?? new HashSet<GridPoint>(); _arena = arena ?? new HashSet<GridPoint>();
        }

        public PlayerIntent? Think(MatchServer server, float dt)
        {
            var sim = server.Sim;
            var c = sim.Get(PlayerId);
            if (c == null || !c.Alive) return null;
            // Pass on the player's rolls at once: the loot is theirs.
            foreach (var kv in server.State.OpenRolls)
                if (kv.Value.Eligible.Contains(PlayerId) && !kv.Value.Choices.ContainsKey(PlayerId))
                    return new PlayerIntent { PlayerId = PlayerId, AimX = c.Position.X, AimY = c.Position.Y, RollItemId = kv.Key, RollChoice = (byte)LootRollChoice.Pass };
            // A downed teammate (the player first): the nearest standing bot goes to them and holds Interact (the 8 s revive); the other keeps fighting.
            // With a monster on top of the body the revive would only be broken, so it fights first, unless the bleed-out clock is nearly done.
            var downed = sim.Combatants.Values.Where(o => !o.IsMob && o.Team == c.Team && o.Id != c.Id && o.Downed && !o.Dead && server.State.Downed.ContainsKey(o.Id))
                .OrderBy(o => o.Id == Leader ? 0 : 1).ThenBy(o => Vec2.Distance(o.Position, c.Position)).FirstOrDefault();
            bool contested = downed != null && FightRules.DownedSeconds - (sim.Time - downed.DownedAt) > 20f
                             && sim.Combatants.Values.Any(o => o.IsMob && !o.Dead && !_avoid.Contains(o.Position.Cell) && Vec2.Distance(o.Position, downed.Position) <= 3.5f);
            if (downed != null && contested) { SimpleBrain.Act(sim, c, dt); return null; }
            if (downed != null)
            {
                var reviver = server.Bots.Select(sim.Get).Where(b => b != null && b.Alive && b.Team == c.Team)
                    .OrderBy(b => Vec2.Distance(b.Position, downed.Position)).ThenBy(b => b.Id).FirstOrDefault();
                if (reviver != null && reviver.Id == PlayerId)
                {
                    // Kneel where Space means the body, not a door: a door within reach outranks a downed teammate.
                    var doors = server.State.Index.Doors.Values.Select(d => Vec2.Of(d.cell)).ToList();
                    bool ClearOfDoors(Vec2 p) => doors.All(d => Vec2.Distance(d, p) > 1.35f);
                    if (server.InteractionAvailable(PlayerId) == "revive") { _path = null; return new PlayerIntent { PlayerId = PlayerId, Interact = true, AimX = downed.Position.X, AimY = downed.Position.Y }; }
                    Vec2? spot = null;
                    foreach (float r in new[] { 0.8f, 1.1f, 1.35f })
                        for (int k = 0; k < 16 && !spot.HasValue; k++)
                        {
                            double a = Math.PI * 2 * k / 16;
                            var p = downed.Position + new Vec2((float)Math.Cos(a), (float)Math.Sin(a)) * r;
                            if (sim.Walkable(p) && ClearOfDoors(p)) spot = p;
                        }
                    var to = spot ?? downed.Position;
                    return Walk(server, c, to.Cell, dt, downed.Position, to);
                }
            }
            var leader = sim.Get(Leader);
            bool leaderInArena = leader != null && _arena.Contains(leader.Position.Cell);
            bool threat = sim.Combatants.Values.Any(o => o.IsMob && !o.Dead && Vec2.Distance(o.Position, c.Position) <= EngageCells
                                                         && !_avoid.Contains(o.Position.Cell)
                                                         && (leaderInArena || !_arena.Contains(o.Position.Cell) || (leader != null && !leader.Dead && Vec2.Distance(o.Position, leader.Position) <= 4f))
                                                         && (leader == null || leader.Dead || Vec2.Distance(o.Position, leader.Position) <= LeaderEngageCells) && sim.CanSee(c.Position, o.Position));
            if (threat) { SimpleBrain.Act(sim, c, dt); return null; }
            if (leader == null || leader.Dead) return Idle(c);
            if (Vec2.Distance(leader.Position, c.Position) <= FollowCells) { _path = null; return Idle(c, leader.Position); }
            return Walk(server, c, leader.Position.Cell, dt, leader.Position);
        }

        private PlayerIntent Walk(MatchServer server, Combatant c, GridPoint goal, float dt, Vec2 look, Vec2? final = null)
        {
            var sim = server.Sim;
            _repath -= dt;
            if (_path == null || _path.Count == 0 || _repath <= 0f || goal.ManhattanDistance(_pathTo) > 2)
            {
                var blocked = new HashSet<GridPoint>(sim.ClosedDoors);
                foreach (var p in _avoid) if (p != c.Position.Cell && p != goal) blocked.Add(p);
                _path = GridPath.Find(server.Layout, c.Position.Cell, goal, closedDoors: blocked);
                _pathTo = goal; _repath = 0.6f;
            }
            if (_path == null || _path.Count == 0) return Idle(c, look);
            while (_path.Count > 1 && (Vec2.Distance(Vec2.Of(_path[0]), c.Position) < 0.45f || _path[0] == c.Position.Cell)) _path.RemoveAt(0);
            var next = _path.Count <= 1 && final.HasValue ? final.Value : Vec2.Of(_path[0]);
            if (Vec2.Distance(next, c.Position) < 0.05f) return Idle(c, look);
            var dir = (next - c.Position).Normalized;
            return new PlayerIntent { PlayerId = PlayerId, MoveX = dir.X, MoveY = dir.Y, AimX = next.X, AimY = next.Y };
        }

        private PlayerIntent Idle(Combatant c, Vec2? look = null)
        {
            var aim = look ?? c.Position + c.Facing;
            return new PlayerIntent { PlayerId = PlayerId, AimX = aim.X, AimY = aim.Y };
        }
    }
}
