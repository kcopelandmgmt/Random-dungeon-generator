using System;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Combat;

namespace DungeonGen.Core.Net
{
    /// <summary>What a bot team knows together: rooms it has finished with, where it is heading, whether it is leaving.</summary>
    public sealed class BotSquad
    {
        public int Team;
        public HashSet<int> Visited = new HashSet<int>();
        public int GoalRoom = -1;
        public bool Leaving;
        public int ItemsWanted = 8;
        public float LeaveWithSecondsLeft = 300f;
        public bool TakeOnBosses = true;
        /// <summary>Rooms whose occupants would not come out to fight: avoided until the time given.</summary>
        public Dictionary<int, float> Avoid = new Dictionary<int, float>();
    }

    /// <summary>
    /// A bot that plays a whole round through the same intents a player sends: it explores room by
    /// room, holds Space over chests, keys, vault doors and corpses, revives a downed teammate, fights
    /// with the lab brain when something hostile is in sight, and heads for the nearest extract when
    /// it has enough or time runs short. It is not meant to be clever. It is meant to exercise every
    /// path a real round takes so the server, the state and the summary are proven before anyone
    /// walks a corridor in Unity, and to give the tutorial and the practice mode a team that moves.
    /// </summary>
    public sealed class RoundBot : IServerBot
    {
        public int PlayerId { get; }
        public BotSquad Squad { get; }
        private List<GridPoint> _path;
        private int _pathGoal = -1;
        private Vec2 _lastPos; private float _stuck;
        private string _lastHere; private float _ignoreHereUntil; private bool _hooked;
        private float _combatSince = -1f; private int _hitsAtCombatStart;
        public string Describe(MatchServer s) { var c = s.Sim.Get(PlayerId); return $"{PlayerId} {c?.Spec} T{Squad.Team} room {s.RoomOf(PlayerId)} at {c?.Position} {(c == null ? "" : c.Dead ? "dead" : c.Downed ? "downed" : "up")} goal {Squad.GoalRoom} leaving {Squad.Leaving} visited {Squad.Visited.Count} here {_lastHere ?? "-"} channel {s.ChannelOf(PlayerId) ?? "-"} path {_path?.Count ?? 0}"; }

        public RoundBot(int playerId, BotSquad squad) { PlayerId = playerId; Squad = squad; }

        private static PlayerIntent Idle(int id) => new PlayerIntent { PlayerId = id };
        /// <summary>Which player raised this event, by the kinds a player's interaction raises.</summary>
        private static int ActorOf(MatchEvent e)
        {
            switch (e.Kind)
            {
                case MatchEventKind.ContainerOpened: case MatchEventKind.CorpseOpened: case MatchEventKind.KeyPickedUp: case MatchEventKind.DoorOpened: case MatchEventKind.DoorClosed:
                case MatchEventKind.HazardDisarmed: case MatchEventKind.VaultOpened: case MatchEventKind.ItemTaken: case MatchEventKind.LootRolled: return e.B;
                case MatchEventKind.LootStarted: case MatchEventKind.SilverPicked: case MatchEventKind.SilverLooted: case MatchEventKind.ExtractStarted: case MatchEventKind.ReviveStarted: case MatchEventKind.PlayerExtracted: return e.A;
                default: return -1;
            }
        }

        public PlayerIntent? Think(MatchServer server, float dt)
        {
            var sim = server.Sim; var state = server.State; var layout = server.Layout;
            var c = sim.Get(PlayerId);
            if (c == null || c.Dead || !state.DoorsOpen) return Idle(PlayerId);
            if (c.Downed) return Idle(PlayerId);
            int range = server.Descriptor.Rules.SightRangeCells;

            // 1. Something standing and hostile in sight: the fight brain takes over this tick.
            int RoomAt(Vec2 p) { var cell = p.Cell; return layout.InBounds(cell) ? layout.RoomIdAt[cell.X, cell.Y] : -1; }
            bool Avoided(Combatant o) => o.IsMob && Squad.Avoid.TryGetValue(RoomAt(o.Position), out float until) && sim.Time < until && Vec2.Distance(o.Position, c.Position) > 3f;
            var inSight = sim.Combatants.Values.Where(o => o.Team != c.Team && !o.Dead && Vec2.Distance(o.Position, c.Position) <= range && sim.CanSee(c.Position, o.Position) && !Avoided(o)).ToList();
            if (inSight.Any(o => !o.Downed))
            {
                // A stalemate (a boss that will not leave its room, a pack across a pit) is not a fight: after 20 s with no blow landed either way, go elsewhere.
                int hits = sim.Events.Count(e => e.Kind == FightEventKind.Hit && (e.Source == PlayerId || e.Target == PlayerId));
                if (_combatSince < 0f) { _combatSince = sim.Time; _hitsAtCombatStart = hits; }
                else if (hits != _hitsAtCombatStart) { _combatSince = sim.Time; _hitsAtCombatStart = hits; }
                else if (sim.Time - _combatSince > 20f)
                {
                    foreach (var o in inSight.Where(o => !o.Downed && o.IsMob)) Squad.Avoid[RoomAt(o.Position)] = sim.Time + 120f;
                    _combatSince = -1f;
                    if (Squad.GoalRoom >= 0) { Squad.Visited.Add(Squad.GoalRoom); Squad.GoalRoom = -1; }
                    return Idle(PlayerId);
                }
                SimpleBrain.Act(sim, c, dt); _path = null; return null;
            }
            _combatSince = -1f;

            // 2. A downed teammate: get to them and hold Space. Before finishing anyone: a teammate bleeding out is the clock that matters.
            var downed = sim.Combatants.Values.Where(o => o.Team == c.Team && o.Downed && !o.Dead && Vec2.Distance(o.Position, c.Position) <= range * 2).OrderBy(o => Vec2.Distance(o.Position, c.Position)).FirstOrDefault();
            if (downed != null)
            {
                if (Vec2.Distance(downed.Position, c.Position) <= 1.2f) return Hold(c, downed.Position);
                return WalkTo(server, c, downed.Position.Cell, dt);
            }
            // 2b. Downed enemies and nobody of ours to pick up: finish them, nearest first, so the soul is on the corpse before their friends come back.
            if (inSight.Count > 0)
            {
                var body = inSight.OrderBy(o => Vec2.Distance(o.Position, c.Position)).First();
                if (Vec2.Distance(body.Position, c.Position) > 1.2f && !sim.CanSee(c.Position, body.Position)) return WalkTo(server, c, body.Position.Cell, dt);
                SimpleBrain.Act(sim, c, dt); _path = null; return null;
            }

            // 3. Something to do where we stand.
            string here = server.InteractionAvailable(PlayerId);
            // An interaction the rules refuse is left alone for a while, so a refused take never becomes a loop.
            if (!_hooked) { _hooked = true; server.EventRejected += (e, why) => { if (ActorOf(e) == PlayerId) _ignoreHereUntil = server.Sim.Time + 20f; }; }
            _lastHere = here;
            if (sim.Time < _ignoreHereUntil && server.ChannelOf(PlayerId) == null) here = null;
            if (here != null && here != "door-close" && (here != "extract" || Squad.Leaving)) return Hold(c, c.Position + c.Facing);

            // 4. Decide where to go.
            int room = server.RoomOf(PlayerId);
            DecideLeaving(server, c);
            if (Squad.GoalRoom >= 0 && room == Squad.GoalRoom && Vec2.Distance(Vec2.Of(layout.Rooms[room].Center), c.Position) <= 0.8f && here == null)
            {
                // Arrived and nothing left to work here.
                if (!IsExtract(server, room) || !Squad.Leaving) { Squad.Visited.Add(room); Squad.GoalRoom = -1; }
                else return Idle(PlayerId);   // waiting at the gate for the extracts to open
            }
            if (Squad.GoalRoom < 0 || !StillWorth(server, Squad.GoalRoom)) Squad.GoalRoom = PickGoal(server, room);
            if (Squad.GoalRoom < 0) return Idle(PlayerId);
            return WalkTo(server, c, layout.Rooms[Squad.GoalRoom].Center, dt);
        }

        private bool IsExtract(MatchServer s, int room) => s.Layout.Rooms[room].Type == RoomType.Extract || (s.Layout.Rooms[room].Type == RoomType.FinalBoss && s.State.BossExitOpen);

        private void DecideLeaving(MatchServer server, Combatant c)
        {
            if (Squad.Leaving) return;
            var state = server.State;
            var members = state.PlayerTeam.Where(kv => kv.Value == c.Team).Select(kv => kv.Key).ToList();
            int carried = state.ItemOwner.Count(kv => members.Contains(kv.Value));
            if (carried >= Squad.ItemsWanted && server.Clock.SecondsUntilExtracts(server.Elapsed) <= 30f) Squad.Leaving = true;   // enough in the bags, and the gates are about to open
            if (server.Clock.SecondsRemaining(server.Elapsed) <= Squad.LeaveWithSecondsLeft) Squad.Leaving = true;
        }

        /// <summary>Is the chosen room still worth walking to? Loot taken by someone else, or a boss killed by another team, changes the answer.</summary>
        private bool StillWorth(MatchServer server, int room)
        {
            if (Squad.Leaving) return IsExtract(server, room) || room == Squad.GoalRoom;
            return Worth(server, room);
        }

        private bool Worth(MatchServer server, int room)
        {
            var state = server.State; var layout = server.Layout; var r = layout.Rooms[room];
            if (Squad.Visited.Contains(room)) return false;
            if (r.Type == RoomType.Spawn) return false;
            if (Squad.Avoid.TryGetValue(room, out float until) && server.Sim.Time < until) return false;
            if (r.IsBossRoom && !state.DeadBosses.Contains(room))
            {
                if (!Squad.TakeOnBosses) return false;
                int standing = state.PlayerTeam.Count(kv => kv.Value == Squad.Team && !state.DeadPlayers.Contains(kv.Key) && !state.ExtractedPlayers.Contains(kv.Key));
                if (standing < 3) return false;   // the design's 2.5-player line: a bot pair does not walk into a boss
            }
            if (r.Type == RoomType.Vault && !state.OpenVaults.Contains(room) && !layout.Keys.Any(k => k.VaultRoomId == room && state.KeyHolder.TryGetValue(k.KeyId, out int h) && state.PlayerTeam.TryGetValue(h, out int ht) && ht == Squad.Team)) return false;
            bool chest = (r.Loot.Count > 0 || r.Silver > 0) && (!state.OpenedContainers.Contains(room) || state.VisibleItemsIn(room).Any() || (!state.SilverTakenRooms.Contains(room) && r.Silver > 0));
            bool key = layout.Keys.Any(k => k.HolderRoomId == room && !state.KeyHolder.ContainsKey(k.KeyId) && !state.DestroyedKeys.Contains(k.KeyId));
            bool boss = r.IsBossRoom && !state.DeadBosses.Contains(room);
            return chest || key || boss;
        }

        private int PickGoal(MatchServer server, int from)
        {
            var layout = server.Layout;
            if (from < 0) return -1;
            // Breadth-first over the room graph from here; nearest room first.
            var dist = new Dictionary<int, int> { [from] = 0 };
            var queue = new Queue<int>(); queue.Enqueue(from);
            while (queue.Count > 0)
            {
                int r = queue.Dequeue();
                foreach (int n in layout.Rooms[r].Neighbours) if (!dist.ContainsKey(n)) { dist[n] = dist[r] + 1; queue.Enqueue(n); }
            }
            if (Squad.Leaving)
            {
                var gates = layout.Rooms.Where(r => r.Type == RoomType.Extract || (r.Type == RoomType.FinalBoss && server.State.BossExitOpen)).Where(r => dist.ContainsKey(r.Id)).OrderBy(r => dist[r.Id]).FirstOrDefault();
                return gates?.Id ?? -1;
            }
            var pick = layout.Rooms.Where(r => dist.ContainsKey(r.Id) && Worth(server, r.Id))
                .OrderBy(r => r.Type == RoomType.FinalBoss ? 2 : r.IsBossRoom && !server.State.DeadBosses.Contains(r.Id) ? 1 : 0)      // ordinary rooms, then bosses, the final boss last
                .ThenBy(r => dist[r.Id]).ThenBy(r => r.Id).FirstOrDefault();
            if (pick == null) { Squad.Leaving = true; return PickGoal(server, from); }
            return pick.Id;
        }

        private PlayerIntent Hold(Combatant c, Vec2 aim) => new PlayerIntent { PlayerId = PlayerId, Interact = true, AimX = aim.X, AimY = aim.Y };

        private PlayerIntent WalkTo(MatchServer server, Combatant c, GridPoint target, float dt)
        {
            var sim = server.Sim; var layout = server.Layout;
            int goalKey = target.X * 10007 + target.Y;
            if (_path == null || _pathGoal != goalKey || _path.Count == 0 || Vec2.Distance(Vec2.Of(_path[0]), c.Position) > 2.5f)
            {
                _path = GridPath.Find(layout, c.Position.Cell, target, closedDoors: sim.ClosedDoors) ?? GridPath.Find(layout, c.Position.Cell, target);
                _pathGoal = goalKey;
            }
            if (_path == null || _path.Count == 0) { Squad.Visited.Add(Squad.GoalRoom); Squad.GoalRoom = -1; return Idle(PlayerId); }
            while (_path.Count > 1 && Vec2.Distance(Vec2.Of(_path[0]), c.Position) < 0.45f) _path.RemoveAt(0);
            var next = Vec2.Of(_path[0]);
            if (_path.Count > 1 && Vec2.Distance(next, c.Position) < 0.45f) next = Vec2.Of(_path[1]);
            // A closed door on the way: stand next to it and hold Space (the server opens it).
            var closed = _path.Take(3).FirstOrDefault(p => sim.ClosedDoors.Contains(p));
            if (sim.ClosedDoors.Contains(_path[0]) || (closed.X != 0 || closed.Y != 0) && Vec2.Distance(Vec2.Of(closed), c.Position) <= 1.2f) return Hold(c, Vec2.Of(closed));
            // Stuck? Give the room up.
            if (Vec2.Distance(_lastPos, c.Position) < 0.02f) { _stuck += dt; if (_stuck > 4f) { _stuck = 0f; _path = null; if (Squad.GoalRoom >= 0) Squad.Visited.Add(Squad.GoalRoom); Squad.GoalRoom = -1; return Idle(PlayerId); } }
            else _stuck = 0f;
            _lastPos = c.Position;
            var dir = (next - c.Position).Normalized;
            return new PlayerIntent { PlayerId = PlayerId, MoveX = dir.X, MoveY = dir.Y, AimX = next.X, AimY = next.Y };
        }
    }
}
