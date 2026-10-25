using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Items;

namespace DungeonGen.Core.Net
{
    /// <summary>
    /// Where the server's messages go. The Unity layer forwards these to the netcode; tests and the
    /// headless round lab leave it null and read the state directly. Every byte a client ever receives
    /// passes through one of these four calls, which is what makes the fog of war auditable.
    /// </summary>
    public interface IServerWire
    {
        /// <summary>Reliable, ordered: an event this team may know happened.</summary>
        void SendEventToTeam(int team, byte[] eventBytes);
        /// <summary>Reliable, ordered: the team's refreshed <see cref="TeamView"/>.</summary>
        void SendViewToTeam(int team, byte[] viewBytes);
        /// <summary>Unreliable, every tick: the <see cref="TeamSnapshot"/> of what the team can see.</summary>
        void SendSnapshotToTeam(int team, byte[] snapshotBytes);
        /// <summary>Unreliable: a ping from a teammate.</summary>
        void SendPingToTeam(int team, byte[] pingBytes);
    }

    /// <summary>
    /// A bot seated on the server. Each tick it either returns an intent, which goes through the same
    /// validation as a player's, or acts on the simulator directly and returns null (the lab brain does
    /// this in a fight). Bots exist for the tutorial and the dev practice mode; they never queue.
    /// </summary>
    public interface IServerBot
    {
        int PlayerId { get; }
        PlayerIntent? Think(MatchServer server, float dt);
    }

    /// <summary>
    /// The server's game loop for one round, with no engine in it. Owns the authoritative
    /// <see cref="MatchState"/>, the <see cref="FightSim"/>, the encounter brains and the clock. Each
    /// tick it applies every intent (movement, casts, channels), steps the mobs and bosses, turns the
    /// simulator's deaths and the players' actions into match events the state validates, records
    /// which rooms each team now sees, and hands every team a snapshot of what it may see. Nothing
    /// here decides a rule: it connects input to the core and the core to the wire. The Unity
    /// component and the headless round lab both drive this same class, so what the lab proves holds
    /// on the dedicated server.
    /// </summary>
    public sealed class MatchServer
    {
        public MatchDescriptor Descriptor { get; }
        public MatchState State { get; }
        public DungeonLayout Layout => State.Layout;
        public FightSim Sim { get; private set; }
        public EncounterDirector Director { get; private set; }
        public RoundClock Clock { get; private set; }
        public uint Tick { get; private set; }
        public float Elapsed { get; private set; }
        public bool Running { get; private set; }
        public bool Ended => State.Ended;
        public IServerWire Wire { get; set; }
        /// <summary>Every accepted event, after the state applied it. The Unity layer opens the exit gate from here.</summary>
        public event Action<MatchEvent> EventRaised;
        /// <summary>Every rejected event with the state's reason. A rejection is a bug in whoever raised it, never in the state.</summary>
        public event Action<MatchEvent, string> EventRejected;
        /// <summary>After every tick, once the snapshots went out.</summary>
        public event Action<MatchServer> Ticked;
        public int RejectedCount { get; private set; }
        public List<string> RecentRejections { get; } = new List<string>();
        public IReadOnlyList<int> Bots => _botIds;
        public IReadOnlyList<IServerBot> BotBrains => _bots;

        private readonly Dictionary<int, PlayerIntent> _intents = new Dictionary<int, PlayerIntent>();
        private readonly Dictionary<int, int> _lastRoom = new Dictionary<int, int>();
        private readonly Dictionary<int, Channel> _channels = new Dictionary<int, Channel>();
        /// <summary>Per team: how far into the simulator's casts and events its last snapshot reached, so each effect is sent once.</summary>
        private readonly Dictionary<int, int> _castCursor = new Dictionary<int, int>(), _hitCursor = new Dictionary<int, int>();
        /// <summary>Player → the door their still-held Interact just opened. Closing is instant, so without this a held key would shut the door the tick after the channel opened it.</summary>
        private readonly Dictionary<int, int> _heldSinceDoorOpened = new Dictionary<int, int>();
        private readonly Dictionary<int, string> _spec = new Dictionary<int, string>();
        private readonly List<IServerBot> _bots = new List<IServerBot>();
        private readonly List<int> _botIds = new List<int>();
        private int _eventCursor;
        private float _sightTimer;

        private sealed class Channel { public string Kind; public int Target; public float Started; public float Seconds; }

        public MatchServer(MatchDescriptor descriptor, DungeonLayout layout, IServerWire wire = null)
        {
            Descriptor = descriptor; State = new MatchState(descriptor, layout); Wire = wire;
        }

        /// <summary>Generate the dungeon with a loot secret that never leaves this process and stand the server up on it.</summary>
        public static MatchServer Host(int seed, DungeonSettings settings, MatchRules rules, IServerWire wire = null, int? lootSecret = null)
        {
            var descriptor = MatchDescriptor.Create(seed, settings, rules, out var layout, lootSecret: lootSecret ?? Guid.NewGuid().GetHashCode());
            return new MatchServer(descriptor, layout, wire);
        }

        public IEnumerable<int> Teams => Enumerable.Range(0, Descriptor.TeamSpawnRooms.Count);
        public int TeamOf(int playerId) => State.PlayerTeam.TryGetValue(playerId, out int t) ? t : -1;
        public string SpecOf(int playerId) => _spec.TryGetValue(playerId, out var s) ? s : null;
        public int RoomOf(int playerId) => _lastRoom.TryGetValue(playerId, out int r) ? r : -1;
        public string ChannelOf(int playerId) => _channels.TryGetValue(playerId, out var c) ? c.Kind : null;

        /// <summary>
        /// Record something that happened, tell the teams allowed to hear about it, and send every
        /// team its refreshed view. Returns false if the rules reject it; the rejection is kept.
        /// </summary>
        public bool Raise(MatchEvent e)
        {
            if (!State.Apply(e, out string why))
            {
                RejectedCount++;
                if (RecentRejections.Count < 50) RecentRejections.Add($"{e}: {why}");
                EventRejected?.Invoke(e, why);
                return false;
            }
            if (Wire != null)
            {
                byte[] bytes;
                using (var ms = new MemoryStream()) { using (var w = new BinaryWriter(ms)) e.Write(w); bytes = ms.ToArray(); }
                var audience = EventVisibility.Audience(e, State);
                foreach (int team in Teams) if (audience == null || audience.Contains(team)) Wire.SendEventToTeam(team, bytes);
                foreach (int team in Teams) Wire.SendViewToTeam(team, TeamView.For(State, team).ToBytes());
            }
            EventRaised?.Invoke(e);
            return true;
        }

        public void RaiseAll(IEnumerable<MatchEvent> events) { foreach (var e in events) Raise(e); }

        /// <summary>Start the round's simulation: station every pack and boss, start the clock.</summary>
        public void StartRound()
        {
            Sim = new FightSim(Layout, Descriptor.Seed) { ClosedDoors = State.ClosedDoors };
            Director = new EncounterDirector(Sim, Layout, Descriptor.Settings.Combat);
            foreach (var room in Layout.Rooms) { Director.StationPack(room); if (room.Boss != null) Director.StationBoss(room); }
            Clock = new RoundClock(Descriptor.Rules, Descriptor.Event(Layout));
            Running = true;
        }

        /// <summary>Seat a player: PlayerSpawned with the class, AccountLevelReported, a combatant at the team's spawn.</summary>
        public Combatant AddPlayer(int playerId, int team, ClassArchetype cls, string spec, PlayerStats stats, int accountLevel = 1)
        {
            if (Sim == null) throw new InvalidOperationException("StartRound before AddPlayer.");
            int classIndex = ClassArchetype.DefaultRoster.ToList().IndexOf(cls);
            if (!Raise(new MatchEvent(MatchEventKind.PlayerSpawned, Sim.Time, playerId, team, classIndex))) return null;
            Raise(new MatchEvent(MatchEventKind.AccountLevelReported, Sim.Time, playerId, accountLevel));
            var room = Layout.Rooms[Descriptor.TeamSpawnRooms[team]];
            int slot = State.PlayerTeam.Count(kv => kv.Value == team) - 1;
            var pos = Vec2.Of(room.Center) + new Vec2(slot - 1f, 0.5f);
            var c = Sim.Add(Combatant.Player(playerId, team, stats, Sim.Walkable(pos) ? pos : Vec2.Of(room.Center), spec));
            _spec[playerId] = spec;
            _lastRoom[playerId] = room.Id;
            return c;
        }

        /// <summary>Seat a bot with the given brain (the plain fight brain when none is given). Never seated by matchmaking.</summary>
        public Combatant AddBot(int botId, int team, string spec, int tier, IServerBot brain = null)
        {
            var cls = FightLab.ClassOf(spec);
            var c = AddPlayer(botId, team, cls, spec, PlayerStats.For(cls, tier, Descriptor.Settings.Combat));
            if (c == null) return null;
            _bots.Add(brain ?? new FightOnlyBot(botId));
            _botIds.Add(botId);
            return c;
        }

        /// <summary>A client's intent for this tick. The transport has already matched the connection to the player id.</summary>
        public void SetIntent(int playerId, PlayerIntent intent) { if (intent.PlayerId == playerId) _intents[playerId] = intent; }

        /// <summary>One server tick.</summary>
        public void Step(float dt)
        {
            if (!Running) return;
            Tick++;
            Elapsed += dt;
            // Disarmed hazards and shut doors are the state's; the simulator reads them.
            Sim.DisarmedHazards.Clear(); foreach (int h in State.DisarmedHazards) Sim.DisarmedHazards.Add(h);

            foreach (var bot in _bots)
            {
                var c = Sim.Get(bot.PlayerId);
                if (c == null || c.Dead) continue;
                var want = bot.Think(this, dt);
                if (want.HasValue) _intents[bot.PlayerId] = want.Value; else _intents.Remove(bot.PlayerId);
            }
            foreach (var kv in _intents.ToList()) Apply(kv.Key, kv.Value, dt);
            Director.Step(dt);

            // The simulator's deaths become match events first, stamped when they happened, so the log never runs backwards.
            var produced = Director.Drain(_eventCursor, id => _lastRoom.TryGetValue(id, out int r) ? r : 0).ToList();
            RaiseAll(produced);
            // A hit on a channelling player breaks the channel.
            for (int i = _eventCursor; i < Sim.Events.Count; i++)
            {
                var e = Sim.Events[i];
                if (e.Kind == FightEventKind.Hit && e.Amount > 0f && _channels.TryGetValue(e.Target, out var ch) && FightRules.LootInterruptedByDamage) Interrupt(e.Target, ch);
            }
            _eventCursor = Sim.Events.Count;
            // The clock's events, stamped no earlier than the simulator's time.
            foreach (var e in Clock.Tick(Elapsed)) Raise(new MatchEvent(e.Kind, Math.Max(e.Time, Sim.Time), e.A, e.B, e.C));

            // Rooms entered, rooms seen.
            foreach (var c in Sim.Combatants.Values.Where(c => !c.IsMob && c.Alive))
            {
                var cell = c.Position.Cell;
                int room = Layout.InBounds(cell) ? Layout.RoomIdAt[cell.X, cell.Y] : -1;
                if (room >= 0 && (!_lastRoom.TryGetValue(c.Id, out int last) || last != room)) { _lastRoom[c.Id] = room; Raise(new MatchEvent(MatchEventKind.PlayerEnteredRoom, Sim.Time, c.Id, room)); }
            }
            _sightTimer += dt;
            if (_sightTimer >= 0.5f)
            {
                _sightTimer = 0f;
                int range = Descriptor.Rules.SightRangeCells;
                foreach (var team in State.PlayerTeam.Values.Distinct().ToList())
                {
                    var seen = State.Explored.TryGetValue(team, out var ex) ? ex : new HashSet<int>();
                    foreach (var c in Sim.Combatants.Values.Where(c => !c.IsMob && c.Alive && c.Team == team))
                        foreach (int room in LineOfSight.VisibleRooms(Layout, c.Position.Cell, range, Sim.ClosedDoors))
                            if (!seen.Contains(room)) Raise(new MatchEvent(MatchEventKind.RoomSeen, Sim.Time, team, room));
                }
            }
            ResolveRolls();
            // Players the round state says are dead or extracted leave the simulator.
            foreach (var c in Sim.Combatants.Values.Where(c => !c.IsMob && !c.Dead).ToList())
                if (State.DeadPlayers.Contains(c.Id) || State.ExtractedPlayers.Contains(c.Id)) { c.Dead = true; c.Downed = false; _channels.Remove(c.Id); _intents.Remove(c.Id); }
            if (Wire != null) foreach (var team in State.PlayerTeam.Values.Distinct().ToList()) Wire.SendSnapshotToTeam(team, SnapshotForTeam(team).ToBytes());
            if (State.Ended) Running = false;
            Ticked?.Invoke(this);
        }

        private void Apply(int playerId, PlayerIntent intent, float dt)
        {
            var c = Sim.Get(playerId);
            if (c == null || c.Dead) return;
            if (!State.DoorsOpen && !Descriptor.Rules.IsTutorial) return;   // sealed in the spawn until the countdown ends
            if (c.Downed) return;
            var aim = intent.Aim;
            if (intent.Move.Length > 0.05f && !_channels.ContainsKey(playerId)) Sim.Move(playerId, c.Position + intent.Move.Normalized * 10f, dt);
            else if ((aim - c.Position).Length > 0.1f) c.Facing = (aim - c.Position).Normalized;
            // Casts, one per tick, by slot.
            if (intent.AnyCast && c.CanAct)
            {
                var book = AbilityBook.For(_spec[playerId]).ToList();
                AbilityDefinition want = intent.Ultimate ? book.FirstOrDefault(a => a.Kind == AbilityKind.Ultimate) : intent.Zone ? book.First(a => a.Kind == AbilityKind.Zone) : intent.Mobility ? book.First(a => a.Kind == AbilityKind.Mobility) : intent.Bolt ? book.First(a => a.Kind == AbilityKind.Bolt) : book.First(a => a.Kind == AbilityKind.Basic);
                if (want != null)
                {
                    int charges = State.UltimateCharges.TryGetValue(playerId, out int ch) ? ch : 0;
                    if (want.Kind == AbilityKind.Ultimate) c.UltimateArmed = Sim.DevCasting || charges > 0;
                    var r = Sim.Cast(playerId, want.Id, want.Shape == AbilityShape.Self ? c.Position : aim);
                    if (r.Ok && want.Kind == AbilityKind.Ultimate && charges > 0) Raise(new MatchEvent(MatchEventKind.UltimateUsed, Sim.Time, playerId));   // a dev's free ultimate spends nothing
                    if (r.Ok && _channels.TryGetValue(playerId, out var open)) Interrupt(playerId, open);
                }
            }
            if (intent.Ping && Wire != null && State.PlayerTeam.TryGetValue(playerId, out int team)) Wire.SendPingToTeam(team, new TeamPing(playerId, team, (PingKind)intent.PingKind, aim.Cell, Sim.Time).ToBytes());
            // Channels: hold to continue, release to stop. Releasing also re-arms the instant door close.
            if (intent.Interact) Interact(playerId, c);
            else
            {
                _heldSinceDoorOpened.Remove(playerId);
                if (_channels.TryGetValue(playerId, out var held)) Interrupt(playerId, held);
            }
        }

        private void Interrupt(int playerId, Channel ch)
        {
            _channels.Remove(playerId);
            // The state drops a channel itself when the player is downed or dies; then there is nothing left to interrupt.
            switch (ch.Kind)
            {
                case "extract": if (State.Channels.ContainsKey(playerId)) Raise(new MatchEvent(MatchEventKind.ExtractInterrupted, Sim.Time, playerId)); break;
                case "revive": if (State.Revives.ContainsKey(playerId)) Raise(new MatchEvent(MatchEventKind.ReviveInterrupted, Sim.Time, playerId)); break;
                default: if (State.Loots.ContainsKey(playerId)) Raise(new MatchEvent(MatchEventKind.LootInterrupted, Sim.Time, playerId)); break;
            }
        }

        /// <summary>What a bot or a client can expect Space to do where this player stands, without doing it. Null when nothing is in reach.</summary>
        public string InteractionAvailable(int playerId)
        {
            var c = Sim.Get(playerId);
            return c == null ? null : Resolve(playerId, c, false);
        }

        /// <summary>What Space means where you stand: the nearest thing you can work, by priority. Starts the channel on first press, completes it when held long enough.</summary>
        private void Interact(int playerId, Combatant c)
        {
            if (_channels.TryGetValue(playerId, out var ch))
            {
                if (Sim.Time - ch.Started < ch.Seconds) return;
                _channels.Remove(playerId);
                // Two players can hold the same thing; the second to finish finds it done and simply lets go.
                switch (ch.Kind)
                {
                    case "chest": if (!State.OpenedContainers.Contains(ch.Target)) Raise(new MatchEvent(MatchEventKind.ContainerOpened, Sim.Time, ch.Target, playerId)); break;
                    case "corpse": if (!State.OpenedCorpses.Contains(ch.Target)) Raise(new MatchEvent(MatchEventKind.CorpseOpened, Sim.Time, ch.Target, playerId)); break;
                    case "key": if (!State.KeyHolder.ContainsKey(ch.Target)) Raise(new MatchEvent(MatchEventKind.KeyPickedUp, Sim.Time, ch.Target, playerId)); break;
                    case "door":
                        if (State.Index.Doors.TryGetValue(ch.Target, out var d) && State.ClosedDoors.Contains(d.cell) && Raise(new MatchEvent(MatchEventKind.DoorOpened, Sim.Time, ch.Target, playerId)))
                            _heldSinceDoorOpened[playerId] = ch.Target;   // the key is still down; it must not shut what it just opened
                        break;
                    case "hazard": if (!State.DisarmedHazards.Contains(ch.Target)) Raise(new MatchEvent(MatchEventKind.HazardDisarmed, Sim.Time, ch.Target, playerId)); break;
                    case "revive": if (State.Downed.ContainsKey(ch.Target) && Raise(new MatchEvent(MatchEventKind.PlayerRevived, Sim.Time, ch.Target, playerId))) Sim.Revive(Sim.Get(ch.Target), FightRules.ReviveHealthFraction); break;
                    case "extract": Raise(new MatchEvent(MatchEventKind.PlayerExtracted, Sim.Time, playerId, ch.Target, State.PlayerTeam[playerId])); break;
                }
                return;
            }
            Resolve(playerId, c, true);
        }

        /// <summary>The interaction priority list. With act=false only names what would happen.</summary>
        private string Resolve(int playerId, Combatant c, bool act)
        {
            var layout = Layout;
            var cell = c.Position.Cell;
            int room = layout.InBounds(cell) ? layout.RoomIdAt[cell.X, cell.Y] : -1;
            bool Start(string kind, int target, float seconds, MatchEvent? begin)
            {
                if (!act) return true;
                if (begin.HasValue && !Raise(begin.Value)) return false;
                _channels[playerId] = new Channel { Kind = kind, Target = target, Started = Sim.Time, Seconds = seconds };
                return true;
            }
            // 1. A door within reach: shut it now, or start opening it.
            var door = State.Index.Doors.Where(kv => Vec2.Distance(Vec2.Of(kv.Value.cell), c.Position) <= 1.3f).OrderBy(kv => Vec2.Distance(Vec2.Of(kv.Value.cell), c.Position)).FirstOrDefault();
            bool justOpenedAndStillHeld = door.Key != 0 && _heldSinceDoorOpened.TryGetValue(playerId, out int opened) && opened == door.Key && !State.ClosedDoors.Contains(door.Value.cell);
            if (door.Key != 0 && !justOpenedAndStillHeld)
            {
                if (State.ClosedDoors.Contains(door.Value.cell)) { Start("door", door.Key, DoorRules.OpenSeconds, new MatchEvent(MatchEventKind.LootStarted, Sim.Time, playerId, door.Key, 4)); return "door-open"; }
                if (!act || Raise(new MatchEvent(MatchEventKind.DoorClosed, Sim.Time, door.Key, playerId))) return "door-close";
            }
            // 2. A downed teammate.
            var downed = Sim.Combatants.Values.FirstOrDefault(o => o.Downed && !o.Dead && o.Team == c.Team && o.Id != c.Id && Vec2.Distance(o.Position, c.Position) <= 1.5f);
            if (downed != null) { Start("revive", downed.Id, FightRules.ReviveChannelSeconds, new MatchEvent(MatchEventKind.ReviveStarted, Sim.Time, playerId, downed.Id)); return "revive"; }
            // 3. A corpse.
            var corpse = Sim.Combatants.Values.FirstOrDefault(o => o.Dead && !o.IsMob && State.Corpses.ContainsKey(o.Id) && Vec2.Distance(o.Position, c.Position) <= 1.5f);
            if (corpse != null)
            {
                if (!State.OpenedCorpses.Contains(corpse.Id)) { Start("corpse", corpse.Id, FightRules.CorpseLootSeconds, new MatchEvent(MatchEventKind.LootStarted, Sim.Time, playerId, corpse.Id, 1)); return "corpse"; }
                var item = State.Corpses[corpse.Id].FirstOrDefault();
                if (item != 0) { if (act) Raise(new MatchEvent(MatchEventKind.ItemTaken, Sim.Time, item, playerId)); return "corpse-item"; }
                if (State.CorpseSilver.ContainsKey(corpse.Id)) { if (act) Raise(new MatchEvent(MatchEventKind.SilverLooted, Sim.Time, playerId, corpse.Id)); return "corpse-silver"; }
            }
            if (room < 0) return null;
            var r = layout.Rooms[room];
            // 4. A hazard a Shade can disarm.
            var hazard = r.Hazards.FirstOrDefault(h => h.Disarmable && !State.DisarmedHazards.Contains(h.Id) && Vec2.Distance(Vec2.Of(h.Bounds.Center), c.Position) <= 1.5f);
            if (hazard != null && HazardRules.CanDisarm(State.ClassOf(playerId), hazard)) { Start("hazard", hazard.Id, HazardRules.DisarmSeconds, new MatchEvent(MatchEventKind.LootStarted, Sim.Time, playerId, hazard.Id, 3)); return "hazard"; }
            // 5. A key lying here (a boss's key only once the boss is down).
            var key = layout.Keys.FirstOrDefault(k => k.HolderRoomId == room && !State.KeyHolder.ContainsKey(k.KeyId) && !State.DestroyedKeys.Contains(k.KeyId) && (!k.DroppedByBoss || State.DeadBosses.Contains(room)));
            if (key != null) { Start("key", key.KeyId, FightRules.KeyPickupSeconds, new MatchEvent(MatchEventKind.LootStarted, Sim.Time, playerId, room, 2)); return "key"; }
            // 6. The vault door, if you hold its key.
            if (r.Type == RoomType.Vault && !State.OpenVaults.Contains(room) && layout.Keys.Any(k => k.VaultRoomId == room && State.KeyHolder.TryGetValue(k.KeyId, out int holder) && holder == playerId))
            { if (!act || Raise(new MatchEvent(MatchEventKind.VaultOpened, Sim.Time, room, playerId))) return "vault"; }
            // 7. The chest: open it, then take or roll. A boss room's chest waits for the boss.
            if ((r.Loot.Count > 0 || r.Silver > 0) && (!r.IsBossRoom || State.DeadBosses.Contains(room)) && (r.Type != RoomType.Vault || State.OpenVaults.Contains(room)))
            {
                if (!State.OpenedContainers.Contains(room)) { Start("chest", room, FightRules.ChestOpenSeconds, new MatchEvent(MatchEventKind.LootStarted, Sim.Time, playerId, room, 0)); return "chest"; }
                var rolling = State.OpenRolls.FirstOrDefault(kv => kv.Value.Eligible.Contains(playerId) && !kv.Value.Choices.ContainsKey(playerId));
                if (rolling.Value != null) { if (act) Raise(new MatchEvent(MatchEventKind.LootRolled, Sim.Time, rolling.Key, playerId, (int)LootRollChoice.Need)); return "roll"; }
                var item = State.VisibleItemsIn(room).FirstOrDefault(i => !State.OpenRolls.ContainsKey(i) && (!State.LootWinner.TryGetValue(i, out int w) || w < 0 || w == playerId));
                if (item != 0) { if (act) Raise(new MatchEvent(MatchEventKind.ItemTaken, Sim.Time, item, playerId)); return "item"; }
                if (!State.SilverTakenRooms.Contains(room) && r.Silver > 0) { if (act) Raise(new MatchEvent(MatchEventKind.SilverPicked, Sim.Time, playerId, room)); return "silver"; }
            }
            // 8. Leaving: an extract room with the gates open, or the centre when the exit is open.
            bool gate = r.Type == RoomType.Extract && State.ExtractsOpen, exit = r.Type == RoomType.FinalBoss && State.BossExitOpen;
            if (gate || exit) { Start("extract", room, Descriptor.Rules.ExtractChannelSeconds, new MatchEvent(MatchEventKind.ExtractStarted, Sim.Time, playerId, room)); return "extract"; }
            return null;
        }

        /// <summary>Resolve any group roll that has all its answers or ran out of time.</summary>
        private void ResolveRolls()
        {
            if (State.OpenRolls.Count == 0) return;
            foreach (var kv in State.OpenRolls.Where(kv => State.RollCanResolve(kv.Key, Sim.Time)).ToList())
            {
                var outcome = LootRollRules.Resolve(State.RollEntries(kv.Key), new DungeonRandom(unchecked(Descriptor.Seed * 31 + kv.Key + (int)Sim.Time)));
                Raise(new MatchEvent(MatchEventKind.LootRollResolved, Sim.Time, kv.Key, outcome.Winner, outcome.WinningRoll));
            }
        }

        /// <summary>What one team may see: its own members always, everyone else only inside a member's sight (closed doors and pillars block), and never an invisible enemy.</summary>
        public TeamSnapshot SnapshotForTeam(int team)
        {
            var snap = new TeamSnapshot { Tick = Tick, Time = Sim.Time };
            var members = Sim.Combatants.Values.Where(c => !c.IsMob && c.Team == team && !c.Dead).ToList();
            var visible = new HashSet<GridPoint>();
            foreach (var m in members) visible.UnionWith(LineOfSight.VisibleCells(Layout, m.Position.Cell, Descriptor.Rules.SightRangeCells, Sim.ClosedDoors));
            foreach (var c in Sim.Combatants.Values)
            {
                bool mine = c.Team == team;
                if (!mine && (c.Dead && !State.Corpses.ContainsKey(c.Id))) continue;
                if (!mine && !visible.Contains(c.Position.Cell)) continue;
                if (!mine && c.Has(BuffKind.Invisible) && !c.Buffs.Any(b => b.Kind == BuffKind.Marked && Sim.Get(b.Source)?.Team == team)) continue;
                snap.Avatars.Add(new AvatarSnapshot
                {
                    Id = c.Id, Team = c.Team, X = c.Position.X, Y = c.Position.Y, FacingX = c.Facing.X, FacingY = c.Facing.Y, HealthFraction = c.MaxHealth > 0f ? c.Health / c.MaxHealth : 0f,
                    ClassIndex = (byte)Math.Max(0, ClassArchetype.DefaultRoster.ToList().IndexOf(c.Class)), Downed = c.Downed, Dead = c.Dead, IsMob = c.IsMob,
                    ArmedUltimate = !c.IsMob && State.UltimateCharges.TryGetValue(c.Id, out int u) && u > 0, Invisible = c.Has(BuffKind.Invisible), Label = c.IsMob ? c.Name : (mine ? c.Name : (c.Class?.Name ?? "")),
                });
            }
            foreach (var m in members)
            {
                var book = AbilityBook.For(m.Spec).OrderBy(a => a.Kind).ToList();
                var cds = book.Select(a => m.CooldownUntil.TryGetValue(a.Id, out float until) ? Math.Max(0f, until - Sim.Time) : 0f).ToArray();
                _channels.TryGetValue(m.Id, out var ch);
                snap.Selves.Add(new SelfSnapshot
                {
                    PlayerId = m.Id, Health = m.Health, MaxHealth = m.MaxHealth, Resource = m.ResourceNow, MaxResource = m.ResourceMax, CooldownRemaining = cds,
                    ChannelName = ch?.Kind, ChannelFraction = ch == null ? 0f : Math.Max(0f, Math.Min(1f, (Sim.Time - ch.Started) / ch.Seconds)),
                    UltimateArmed = State.UltimateCharges.TryGetValue(m.Id, out int uu) && uu > 0, DownedRemaining = m.Downed ? Math.Max(0f, FightRules.DownedSeconds - (Sim.Time - m.DownedAt)) : 0f,
                });
            }
            // Effects since this team's last snapshot: the casts and hits it may see, and the zones in its sight (those come every tick they live).
            var shown = new HashSet<int>(snap.Avatars.Select(a => a.Id));
            bool Sees(Vec2 p) => visible.Contains(p.Cell);
            for (int i = _castCursor.TryGetValue(team, out int c0) ? c0 : 0; i < Sim.Casts.Count; i++)
            {
                var k = Sim.Casts[i];
                if (k.CasterTeam == team || shown.Contains(k.Caster) || Sees(k.From) || Sees(k.End))
                    snap.Effects.Add(new EffectSnapshot { Kind = EffectKind.Cast, Source = k.Caster, Target = -1, Ability = k.AbilityId, X = k.From.X, Y = k.From.Y, X2 = k.End.X, Y2 = k.End.Y, Amount = k.Targets.Length });
            }
            _castCursor[team] = Sim.Casts.Count;
            for (int i = _hitCursor.TryGetValue(team, out int h0) ? h0 : 0; i < Sim.Events.Count; i++)
            {
                var e = Sim.Events[i];
                if (e.Kind != FightEventKind.Hit && e.Kind != FightEventKind.Heal) continue;
                var t = Sim.Get(e.Target); if (t == null) continue;
                if (t.Team == team || shown.Contains(e.Target) || Sees(t.Position))
                    snap.Effects.Add(new EffectSnapshot { Kind = e.Kind == FightEventKind.Heal ? EffectKind.Heal : EffectKind.Hit, Source = e.Source, Target = e.Target, Ability = e.Detail ?? "", X = t.Position.X, Y = t.Position.Y, X2 = t.Position.X, Y2 = t.Position.Y, Amount = e.Amount });
            }
            _hitCursor[team] = Sim.Events.Count;
            foreach (var z in Sim.Zones)
                if (z.CasterTeam == team || Sees(z.Centre))
                    snap.Effects.Add(new EffectSnapshot { Kind = EffectKind.Zone, Source = z.Caster, Target = -1, Ability = z.Ability.Id, X = z.Centre.X, Y = z.Centre.Y, X2 = z.Centre.X, Y2 = z.Centre.Y, Amount = z.Ability.Size, Until = z.Until });
            return snap;
        }

        public RoundSummary SummaryFor(int playerId) => RoundSummary.For(State, playerId);
    }

    /// <summary>The dev practice mode's bot: stands still until something hostile is near, then fights with the lab brain.</summary>
    public sealed class FightOnlyBot : IServerBot
    {
        public int PlayerId { get; }
        public FightOnlyBot(int playerId) { PlayerId = playerId; }
        public PlayerIntent? Think(MatchServer server, float dt)
        {
            var c = server.Sim.Get(PlayerId);
            if (c == null || !c.Alive || !server.State.DoorsOpen) return null;
            SimpleBrain.Act(server.Sim, c, dt);
            return null;
        }
    }
}
