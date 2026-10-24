using System;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// The server's game loop for one round, on top of the engine-free core. Each tick it applies every
    /// player's intent to the fight simulator (movement, casts, channels), steps the mob and boss brains,
    /// turns the simulator's deaths and the players' actions into match events the round state validates,
    /// tells the state which rooms each team now sees, and sends every team a snapshot of what it may see.
    /// Nothing here decides a rule; it only connects input to the core and the core to the wire.
    /// Server authority, no prediction: clients send intents, this moves them.
    /// </summary>
    public sealed class ServerSimulation : MonoBehaviour
    {
        public DungeonMatchBootstrap Bootstrap;
        public DungeonBuilder Builder;
        [Tooltip("Server ticks per second. Twenty is plenty for a 5 m/s game with 2 m cells.")]
        public int TickRate = 20;

        public FightSim Sim { get; private set; }
        public EncounterDirector Director { get; private set; }
        public RoundClock Clock { get; private set; }
        public uint Tick { get; private set; }
        public bool Running { get; private set; }

        private IMatchTransport _transport;
        private readonly Dictionary<int, PlayerIntent> _intents = new Dictionary<int, PlayerIntent>();
        private readonly Dictionary<int, PlayerIntent> _lastIntents = new Dictionary<int, PlayerIntent>();
        private readonly Dictionary<int, int> _lastRoom = new Dictionary<int, int>();
        private readonly Dictionary<int, Channel> _channels = new Dictionary<int, Channel>();
        private readonly Dictionary<int, string> _spec = new Dictionary<int, string>();
        private readonly List<int> _bots = new List<int>();
        private int _eventCursor;
        private float _accum, _sightTimer, _elapsed;

        private sealed class Channel { public string Kind; public int Target; public float Started; public float Seconds; public int HitsAtStart; }

        public void Attach(IMatchTransport transport)
        {
            _transport = transport;
            transport.IntentReceived += (player, bytes) => { var i = PlayerIntent.FromBytes(bytes); if (i.PlayerId == player) _intents[player] = i; };
        }

        /// <summary>Server: start the round's simulation on the layout the bootstrap generated. Stations every pack and boss.</summary>
        public void StartRound()
        {
            var state = Bootstrap.State;
            Sim = new FightSim(state.Layout, state.Descriptor.Seed) { ClosedDoors = state.ClosedDoors };
            Director = new EncounterDirector(Sim, state.Layout, state.Descriptor.Settings.Combat);
            foreach (var room in state.Layout.Rooms) { Director.StationPack(room); if (room.Boss != null) Director.StationBoss(room); }
            Clock = new RoundClock(state.Descriptor.Rules, state.Descriptor.Event(state.Layout));
            Running = true;
        }

        /// <summary>Server: seat a player. Raises PlayerSpawned (with the class) and AccountLevelReported, and puts a combatant at the team's spawn.</summary>
        public Combatant AddPlayer(int playerId, int team, ClassArchetype cls, string spec, PlayerStats stats, int accountLevel = 1)
        {
            int classIndex = ClassArchetype.DefaultRoster.ToList().IndexOf(cls);
            if (!Bootstrap.Raise(new MatchEvent(MatchEventKind.PlayerSpawned, Sim.Time, playerId, team, classIndex))) return null;
            Bootstrap.Raise(new MatchEvent(MatchEventKind.AccountLevelReported, Sim.Time, playerId, accountLevel));
            var room = Bootstrap.State.Layout.Rooms[Bootstrap.Descriptor.TeamSpawnRooms[team]];
            int slot = Bootstrap.State.PlayerTeam.Count(kv => kv.Value == team) - 1;
            var pos = Vec2.Of(room.Center) + new Vec2(slot - 1f, 0.5f);
            var c = Sim.Add(Combatant.Player(playerId, team, stats, Sim.Walkable(pos) ? pos : Vec2.Of(room.Center), spec));
            _spec[playerId] = spec;
            _lastRoom[playerId] = room.Id;
            return c;
        }

        /// <summary>Dev practice mode: a bot that fights with the plain lab brain. Never seated by matchmaking.</summary>
        public Combatant AddBot(int botId, int team, string spec, int tier)
        {
            var cls = FightLab.ClassOf(spec);
            var c = AddPlayer(botId, team, cls, spec, PlayerStats.For(cls, tier, Bootstrap.Descriptor.Settings.Combat));
            if (c != null) _bots.Add(botId);
            return c;
        }

        private void Update()
        {
            if (!Running) return;
            float dt = 1f / Mathf.Max(1, TickRate);
            _accum += Time.deltaTime;
            int steps = 0;
            while (_accum >= dt && steps++ < 5) { _accum -= dt; Step(dt); }
        }

        /// <summary>One server tick. Public so tests and the practice mode can drive it without a frame.</summary>
        public void Step(float dt)
        {
            var state = Bootstrap.State;
            Tick++;
            _elapsed += dt;
            foreach (var e in Clock.Tick(_elapsed)) Bootstrap.Raise(e);
            // Disarmed hazards and shut doors are the state's; the simulator reads them.
            Sim.DisarmedHazards.Clear(); foreach (int h in state.DisarmedHazards) Sim.DisarmedHazards.Add(h);

            foreach (var kv in _intents.ToList()) Apply(kv.Key, kv.Value, dt);
            foreach (int bot in _bots) { var c = Sim.Get(bot); if (c != null && c.Alive && state.DoorsOpen) SimpleBrain.Act(Sim, c, dt); }
            Director.Step(dt);

            // Rooms entered, rooms seen.
            foreach (var c in Sim.Combatants.Values.Where(c => !c.IsMob && c.Alive))
            {
                var cell = c.Position.Cell;
                int room = state.Layout.InBounds(cell) ? state.Layout.RoomIdAt[cell.X, cell.Y] : -1;
                if (room >= 0 && (!_lastRoom.TryGetValue(c.Id, out int last) || last != room)) { _lastRoom[c.Id] = room; Bootstrap.Raise(new MatchEvent(MatchEventKind.PlayerEnteredRoom, Sim.Time, c.Id, room)); }
            }
            _sightTimer += dt;
            if (_sightTimer >= 0.5f)
            {
                _sightTimer = 0f;
                int range = state.Descriptor.Rules.SightRangeCells;
                foreach (var team in state.PlayerTeam.Values.Distinct().ToList())
                {
                    var seen = state.Explored.TryGetValue(team, out var ex) ? ex : new HashSet<int>();
                    foreach (var c in Sim.Combatants.Values.Where(c => !c.IsMob && c.Alive && c.Team == team))
                        foreach (int room in LineOfSight.VisibleRooms(state.Layout, c.Position.Cell, range, Sim.ClosedDoors))
                            if (!seen.Contains(room)) Bootstrap.Raise(new MatchEvent(MatchEventKind.RoomSeen, Sim.Time, team, room));
                }
            }
            // The simulator's deaths become match events; a hit on a channelling player breaks the channel.
            var produced = Director.Drain(_eventCursor, id => _lastRoom.TryGetValue(id, out int r) ? r : 0).ToList();
            for (int i = _eventCursor; i < Sim.Events.Count; i++)
            {
                var e = Sim.Events[i];
                if (e.Kind == FightEventKind.Hit && e.Amount > 0f && _channels.TryGetValue(e.Target, out var ch) && FightRules.LootInterruptedByDamage) Interrupt(e.Target, ch);
            }
            _eventCursor = Sim.Events.Count;
            Bootstrap.RaiseAll(produced);
            // Players the round state says are dead or extracted leave the simulator.
            foreach (var c in Sim.Combatants.Values.Where(c => !c.IsMob && !c.Dead).ToList())
                if (state.DeadPlayers.Contains(c.Id) || state.ExtractedPlayers.Contains(c.Id)) { c.Dead = true; c.Downed = false; }
            // Snapshots to every team.
            if (_transport != null) foreach (var team in state.PlayerTeam.Values.Distinct().ToList()) _transport.SendSnapshotToTeam(team, SnapshotForTeam(team).ToBytes());
            _lastIntents.Clear(); foreach (var kv in _intents) _lastIntents[kv.Key] = kv.Value;
        }

        private void Apply(int playerId, PlayerIntent intent, float dt)
        {
            var state = Bootstrap.State;
            var c = Sim.Get(playerId);
            if (c == null || c.Dead) return;
            if (!state.DoorsOpen && !state.Descriptor.Rules.IsTutorial) return;   // sealed in the spawn until the countdown ends
            if (c.Downed) { if (!intent.Interact) return; return; }
            var aim = intent.Aim;
            if (intent.Move.Length > 0.05f && !_channels.ContainsKey(playerId)) Sim.Move(playerId, c.Position + intent.Move.Normalized * 10f, dt);
            else if ((aim - c.Position).Length > 0.1f) c.Facing = (aim - c.Position).Normalized;
            // Casts, one per tick, by slot.
            var book = AbilityBook.For(_spec[playerId]).ToList();
            AbilityDefinition want = intent.Ultimate ? book.FirstOrDefault(a => a.Kind == AbilityKind.Ultimate) : intent.Zone ? book.First(a => a.Kind == AbilityKind.Zone) : intent.Mobility ? book.First(a => a.Kind == AbilityKind.Mobility) : intent.Bolt ? book.First(a => a.Kind == AbilityKind.Bolt) : intent.Basic ? book.First(a => a.Kind == AbilityKind.Basic) : null;
            if (want != null)
            {
                if (want.Kind == AbilityKind.Ultimate) c.UltimateArmed = state.UltimateCharges.TryGetValue(playerId, out int ch) && ch > 0;
                var r = Sim.Cast(playerId, want.Id, want.Shape == AbilityShape.Self ? c.Position : aim);
                if (r.Ok && want.Kind == AbilityKind.Ultimate) Bootstrap.Raise(new MatchEvent(MatchEventKind.UltimateUsed, Sim.Time, playerId));
                if (r.Ok && _channels.ContainsKey(playerId)) Interrupt(playerId, _channels[playerId]);
            }
            // Pings.
            if (intent.Ping && _transport != null && state.PlayerTeam.TryGetValue(playerId, out int team)) _transport.SendToTeam(team, new TeamPing(playerId, team, (PingKind)intent.PingKind, aim.Cell, Sim.Time).ToBytes());
            // Channels: hold to continue, release to stop.
            if (intent.Interact) Interact(playerId, c, aim);
            else if (_channels.TryGetValue(playerId, out var open)) Interrupt(playerId, open);
        }

        private void Interrupt(int playerId, Channel ch)
        {
            _channels.Remove(playerId);
            switch (ch.Kind)
            {
                case "extract": Bootstrap.Raise(new MatchEvent(MatchEventKind.ExtractInterrupted, Sim.Time, playerId)); break;
                case "revive": Bootstrap.Raise(new MatchEvent(MatchEventKind.ReviveInterrupted, Sim.Time, playerId)); break;
                default: Bootstrap.Raise(new MatchEvent(MatchEventKind.LootInterrupted, Sim.Time, playerId)); break;
            }
        }

        /// <summary>What Space means where you stand: the nearest thing you can work, by priority. Starts the channel on first press, completes it when held long enough.</summary>
        private void Interact(int playerId, Combatant c, Vec2 aim)
        {
            var state = Bootstrap.State; var layout = state.Layout;
            var cell = c.Position.Cell;
            int room = layout.InBounds(cell) ? layout.RoomIdAt[cell.X, cell.Y] : -1;
            if (_channels.TryGetValue(playerId, out var ch))
            {
                if (Sim.Time - ch.Started < ch.Seconds) return;
                _channels.Remove(playerId);
                switch (ch.Kind)
                {
                    case "chest": Bootstrap.Raise(new MatchEvent(MatchEventKind.ContainerOpened, Sim.Time, ch.Target, playerId)); break;
                    case "corpse": Bootstrap.Raise(new MatchEvent(MatchEventKind.CorpseOpened, Sim.Time, ch.Target, playerId)); break;
                    case "key": Bootstrap.Raise(new MatchEvent(MatchEventKind.KeyPickedUp, Sim.Time, ch.Target, playerId)); break;
                    case "door": Bootstrap.Raise(new MatchEvent(MatchEventKind.DoorOpened, Sim.Time, ch.Target, playerId)); break;
                    case "hazard": Bootstrap.Raise(new MatchEvent(MatchEventKind.HazardDisarmed, Sim.Time, ch.Target, playerId)); break;
                    case "revive": if (Bootstrap.Raise(new MatchEvent(MatchEventKind.PlayerRevived, Sim.Time, ch.Target, playerId))) Sim.Revive(Sim.Get(ch.Target), FightRules.ReviveHealthFraction); break;
                    case "extract": Bootstrap.Raise(new MatchEvent(MatchEventKind.PlayerExtracted, Sim.Time, playerId, ch.Target, state.PlayerTeam[playerId])); break;
                }
                return;
            }
            void Start(string kind, int target, float seconds, MatchEvent? begin)
            {
                if (begin.HasValue && !Bootstrap.Raise(begin.Value)) return;
                _channels[playerId] = new Channel { Kind = kind, Target = target, Started = Sim.Time, Seconds = seconds };
            }
            // 1. A door within reach: shut it now, or start opening it.
            var door = state.Index.Doors.Where(kv => Vec2.Distance(Vec2.Of(kv.Value.cell), c.Position) <= 1.3f).OrderBy(kv => Vec2.Distance(Vec2.Of(kv.Value.cell), c.Position)).FirstOrDefault();
            if (door.Key != 0)
            {
                if (state.ClosedDoors.Contains(door.Value.cell)) { Start("door", door.Key, DoorRules.OpenSeconds, new MatchEvent(MatchEventKind.LootStarted, Sim.Time, playerId, door.Key, 4)); return; }
                if (Bootstrap.Raise(new MatchEvent(MatchEventKind.DoorClosed, Sim.Time, door.Key, playerId))) return;
            }
            // 2. A downed teammate.
            var downed = Sim.Combatants.Values.FirstOrDefault(o => o.Downed && !o.Dead && o.Team == c.Team && o.Id != c.Id && Vec2.Distance(o.Position, c.Position) <= 1.5f);
            if (downed != null) { Start("revive", downed.Id, FightRules.ReviveChannelSeconds, new MatchEvent(MatchEventKind.ReviveStarted, Sim.Time, playerId, downed.Id)); return; }
            // 3. A corpse.
            var corpse = Sim.Combatants.Values.FirstOrDefault(o => o.Dead && !o.IsMob && state.Corpses.ContainsKey(o.Id) && Vec2.Distance(o.Position, c.Position) <= 1.5f);
            if (corpse != null)
            {
                if (!state.OpenedCorpses.Contains(corpse.Id)) { Start("corpse", corpse.Id, FightRules.CorpseLootSeconds, new MatchEvent(MatchEventKind.LootStarted, Sim.Time, playerId, corpse.Id, 1)); return; }
                var item = state.Corpses[corpse.Id].FirstOrDefault();
                if (item != 0) { Bootstrap.Raise(new MatchEvent(MatchEventKind.ItemTaken, Sim.Time, item, playerId)); return; }
                if (state.CorpseSilver.ContainsKey(corpse.Id)) { Bootstrap.Raise(new MatchEvent(MatchEventKind.SilverLooted, Sim.Time, playerId, corpse.Id)); return; }
            }
            if (room < 0) return;
            var r = layout.Rooms[room];
            // 4. A hazard a Shade can disarm.
            var hazard = r.Hazards.FirstOrDefault(h => h.Disarmable && !state.DisarmedHazards.Contains(h.Id) && Vec2.Distance(Vec2.Of(h.Bounds.Center), c.Position) <= 1.5f);
            if (hazard != null && HazardRules.CanDisarm(state.ClassOf(playerId), hazard)) { Start("hazard", hazard.Id, HazardRules.DisarmSeconds, new MatchEvent(MatchEventKind.LootStarted, Sim.Time, playerId, hazard.Id, 3)); return; }
            // 5. A key lying here.
            var key = layout.Keys.FirstOrDefault(k => k.HolderRoomId == room && !state.KeyHolder.ContainsKey(k.KeyId) && !state.DestroyedKeys.Contains(k.KeyId));
            if (key != null) { Start("key", key.KeyId, FightRules.KeyPickupSeconds, new MatchEvent(MatchEventKind.LootStarted, Sim.Time, playerId, room, 2)); return; }
            // 6. The vault door, if you hold its key.
            if (r.Type == RoomType.Vault && !state.OpenVaults.Contains(room) && Bootstrap.Raise(new MatchEvent(MatchEventKind.VaultOpened, Sim.Time, room, playerId))) return;
            // 7. The chest: open it, then take or roll.
            if (r.Loot.Count > 0 || r.Silver > 0)
            {
                if (!state.OpenedContainers.Contains(room)) { Start("chest", room, FightRules.ChestOpenSeconds, new MatchEvent(MatchEventKind.LootStarted, Sim.Time, playerId, room, 0)); return; }
                var rolling = state.OpenRolls.FirstOrDefault(kv => kv.Value.Eligible.Contains(playerId) && !kv.Value.Choices.ContainsKey(playerId));
                if (rolling.Value != null) { Bootstrap.Raise(new MatchEvent(MatchEventKind.LootRolled, Sim.Time, rolling.Key, playerId, (int)DungeonGen.Core.Items.LootRollChoice.Need)); ResolveRolls(); return; }
                var item = state.VisibleItemsIn(room).FirstOrDefault(i => !state.OpenRolls.ContainsKey(i) && (!state.LootWinner.TryGetValue(i, out int w) || w < 0 || w == playerId));
                if (item != 0) { Bootstrap.Raise(new MatchEvent(MatchEventKind.ItemTaken, Sim.Time, item, playerId)); return; }
                if (!state.SilverTakenRooms.Contains(room) && r.Silver > 0) { Bootstrap.Raise(new MatchEvent(MatchEventKind.SilverPicked, Sim.Time, playerId, room)); return; }
            }
            // 8. Leaving: an extract room with the gates open, or the centre when the exit is open.
            bool gate = r.Type == RoomType.Extract && state.ExtractsOpen, exit = r.Type == RoomType.FinalBoss && state.BossExitOpen;
            if (gate || exit) { Start("extract", room, state.Descriptor.Rules.ExtractChannelSeconds, new MatchEvent(MatchEventKind.ExtractStarted, Sim.Time, playerId, room)); return; }
        }

        /// <summary>Server side of the group rolls: resolve any roll that has all its answers or ran out of time.</summary>
        private void ResolveRolls()
        {
            var state = Bootstrap.State;
            foreach (var kv in state.OpenRolls.Where(kv => state.RollCanResolve(kv.Key, Sim.Time)).ToList())
            {
                var outcome = DungeonGen.Core.Items.LootRollRules.Resolve(state.RollEntries(kv.Key), new DungeonRandom(unchecked(state.Descriptor.Seed * 31 + kv.Key + (int)Sim.Time)));
                Bootstrap.Raise(new MatchEvent(MatchEventKind.LootRollResolved, Sim.Time, kv.Key, outcome.Winner, outcome.WinningRoll));
            }
        }

        /// <summary>What one team may see: its own members always, everyone else only inside a member's sight (closed doors and pillars block), and never an invisible enemy.</summary>
        public TeamSnapshot SnapshotForTeam(int team)
        {
            var state = Bootstrap.State;
            var snap = new TeamSnapshot { Tick = Tick, Time = Sim.Time };
            var members = Sim.Combatants.Values.Where(c => !c.IsMob && c.Team == team && !c.Dead).ToList();
            var visible = new HashSet<GridPoint>();
            foreach (var m in members) visible.UnionWith(LineOfSight.VisibleCells(state.Layout, m.Position.Cell, state.Descriptor.Rules.SightRangeCells, Sim.ClosedDoors));
            foreach (var c in Sim.Combatants.Values)
            {
                bool mine = c.Team == team;
                if (!mine && (c.Dead && !state.Corpses.ContainsKey(c.Id))) continue;
                if (!mine && !visible.Contains(c.Position.Cell)) continue;
                if (!mine && c.Has(BuffKind.Invisible) && !c.Buffs.Any(b => b.Kind == BuffKind.Marked && Sim.Get(b.Source)?.Team == team)) continue;
                snap.Avatars.Add(new AvatarSnapshot
                {
                    Id = c.Id, Team = c.Team, X = c.Position.X, Y = c.Position.Y, FacingX = c.Facing.X, FacingY = c.Facing.Y, HealthFraction = c.MaxHealth > 0f ? c.Health / c.MaxHealth : 0f,
                    ClassIndex = (byte)Mathf.Max(0, ClassArchetype.DefaultRoster.ToList().IndexOf(c.Class)), Downed = c.Downed, Dead = c.Dead, IsMob = c.IsMob,
                    ArmedUltimate = !c.IsMob && state.UltimateCharges.TryGetValue(c.Id, out int u) && u > 0, Invisible = c.Has(BuffKind.Invisible), Label = c.IsMob ? c.Name : (mine ? c.Name : (c.Class?.Name ?? "")),
                });
            }
            foreach (var m in members)
            {
                var book = AbilityBook.For(m.Spec).OrderBy(a => a.Kind).ToList();
                var cds = book.Select(a => m.CooldownUntil.TryGetValue(a.Id, out float until) ? Mathf.Max(0f, until - Sim.Time) : 0f).ToArray();
                _channels.TryGetValue(m.Id, out var ch);
                snap.Selves.Add(new SelfSnapshot
                {
                    PlayerId = m.Id, Health = m.Health, MaxHealth = m.MaxHealth, Resource = m.ResourceNow, MaxResource = m.ResourceMax, CooldownRemaining = cds,
                    ChannelName = ch?.Kind, ChannelFraction = ch == null ? 0f : Mathf.Clamp01((Sim.Time - ch.Started) / ch.Seconds),
                    UltimateArmed = state.UltimateCharges.TryGetValue(m.Id, out int uu) && uu > 0, DownedRemaining = m.Downed ? Mathf.Max(0f, FightRules.DownedSeconds - (Sim.Time - m.DownedAt)) : 0f,
                });
            }
            return snap;
        }
    }
}
