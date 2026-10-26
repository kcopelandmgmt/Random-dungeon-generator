using System;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Items;

namespace DungeonGen.Core.Net
{
    /// <summary>What a bot team knows together: rooms it has finished with, where it is heading, whether it is leaving.</summary>
    public sealed class BotSquad
    {
        public int Team;
        public HashSet<int> Visited = new HashSet<int>();
        public int GoalRoom = -1;
        public bool Leaving;
        /// <summary>Items carried (all three together) at which the squad has had enough and takes them out. Bags nearly full (<see cref="BagsFullFraction"/>) does the same.</summary>
        public int ItemsWanted = 14;
        public float BagsFullFraction = 0.85f;
        /// <summary>A whole squad carrying fewer items than this does not leave just because there is nothing left to walk to; it waits for a chance until the clock runs low.</summary>
        public int SatisfiedItems = 8;
        /// <summary>Out of a fight, a member under this health fraction drinks a potion (in a fight, under half).</summary>
        public float DrinkBelowHealth = 0.85f;
        /// <summary>True while the squad has nothing worth walking to and waits, in its own spawn room, for the gates (or for health to come back).</summary>
        public bool Resting;
        /// <summary>Corpses already worked (or given up on) by this squad, and when it first set off for each.</summary>
        public HashSet<int> CorpsesDone = new HashSet<int>();
        public Dictionary<int, float> CorpseSince = new Dictionary<int, float>();
        /// <summary>For each room, how many rooms the nearest OTHER team's spawn is from it (filled once). A room nearer to us than to every other team is ours to loot first; the rest are contested.</summary>
        public Dictionary<int, int> OthersDistance;
        public float LeaveWithSecondsLeft = 600f;
        public bool TakeOnBosses = true;
        /// <summary>Rooms whose occupants would not come out to fight: avoided until the time given.</summary>
        public Dictionary<int, float> Avoid = new Dictionary<int, float>();
        /// <summary>The boss room the squad has committed to (all three gathered and fit), or -1. A boss the squad has not committed to is walked away from, never fought.</summary>
        public int EngagedBoss = -1;
        /// <summary>Lowest health fraction every member needs before the squad walks into a room boss, and the team average it needs for the final boss.</summary>
        public float BossReadyHealth = 0.9f, FinalReadyHealth = 0.95f;
        /// <summary>The final boss is taken only by a squad that has already beaten a room boss together and walks in with this many potions each (the lab: a bot trio beats it 0–25% of the time).</summary>
        public int FinalBossPotionsEach = 2;
        public int BossesKilled;
        /// <summary>A squad this weak (standing members' average health) heads for the gate once it is open.</summary>
        public float LeaveBelowHealth = 0.45f;
        /// <summary>A squad fights players it sees when its standing health is this many times theirs; closer than this many cells it cannot slip away and fights anyway.</summary>
        /// <summary>With one of the three already down, the squad walks out of a boss fight while the boss is still above this fraction: the boss resets, the rest live.</summary>
        public float CutLossesAboveBossHealth = 0.5f;
        public float EngageRatio = 1.7f, BusyRatio = 1.3f, CorneredCells = 3f;
        /// <summary>Potions each member must still hold to walk into a room boss (one for the fight itself).</summary>
        public int BossPotionsEach = 1;
        /// <summary>Average health (potions counted; nobody under half) the squad needs before it goes for the roaming elite, and the seconds of round that must remain for the clock alone not to send it out while an event is on.</summary>
        public float EliteReadyHealth = 0.85f, EventGraceSeconds = 240f;
        /// <summary>Potions the whole squad must still hold between them before it goes for the roaming elite. One since shrines stopped selling them (2026-10-26 owner #3): with two, squads that once topped up at a shrine never went.</summary>
        public int ElitePotionsTogether = 1;
        /// <summary>A dead teammate is worth two souls at a shrine while at least this much of the round is left (they come back naked: they need time to be of use).</summary>
        public float ResurrectWithSecondsLeft = 180f;
        /// <summary>How far (rooms) a bot walks out of its way to a shrine: to resurrect a teammate, and to arm its ultimate before the boss the squad is heading for (short, so the squad is not left waiting at the door).</summary>
        public int ResurrectDetourRooms = 6, ArmDetourRooms = 2;
        /// <summary>False: the squad never spends souls at a shrine (the lab's A/B switch).</summary>
        public bool UsesShrines = true;
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
        public string Describe(MatchServer s) { var c = s.Sim.Get(PlayerId); return $"{PlayerId} {c?.Spec} T{Squad.Team} room {s.RoomOf(PlayerId)} at {c?.Position} {(c == null ? "" : c.Dead ? "dead" : c.Downed ? "downed" : "up")} goal {Squad.GoalRoom} leaving {Squad.Leaving} visited {Squad.Visited.Count} here {_lastHere ?? "-"} channel {s.ChannelOf(PlayerId) ?? "-"} path {_path?.Count ?? 0} boss {Squad.EngagedBoss} did {_did}"; }
        private string _did = "";

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
            server.SettleUltimateCast(PlayerId);   // the fight brain casts an armed ultimate on the simulator; the state hears of it here
            NoteShrineAnswer(server);
            int range = server.Descriptor.Rules.SightRangeCells;

            // 1. Something standing and hostile in sight: the fight brain takes over this tick.
            int RoomAt(Vec2 p) { var cell = p.Cell; return layout.InBounds(cell) ? layout.RoomIdAt[cell.X, cell.Y] : -1; }
            bool Avoided(Combatant o) => o.IsMob && Squad.Avoid.TryGetValue(RoomAt(o.Position), out float until) && sim.Time < until && Vec2.Distance(o.Position, c.Position) > 3f;
            var mates = Mates(server);
            UpdateBossCommitment(server, c, mates);
            // A boss (or its brood) the squad has not committed to is not a fight: it is a room to leave. Bosses stay in their arena, so walking out is enough.
            var hostileBoss = server.Director.Bosses.FirstOrDefault(b => !b.Boss.Dead && b.RoomId != Squad.EngagedBoss && ((b.InArena(c.Position) && !_passing && (b.Engaged || Vec2.Distance(b.Boss.Position, c.Position) <= 6f)) || Vec2.Distance(b.Boss.Position, c.Position) <= 2.5f));
            if (hostileBoss != null)
            {
                _combatSince = -1f;
                if (Squad.GoalRoom == hostileBoss.RoomId) Squad.GoalRoom = -1;
                _did = "flee"; return Flee(server, c, hostileBoss, dt);
            }
            bool UncommittedBoss(Combatant o) { var b = BossOf(server, o.Id); return b != null && b.RoomId != Squad.EngagedBoss; }
            var inSight = sim.Combatants.Values.Where(o => o.Team != c.Team && !o.Dead && Vec2.Distance(o.Position, c.Position) <= range && sim.CanSee(c.Position, o.Position) && !Avoided(o) && !UncommittedBoss(o) && !(_ignore.TryGetValue(o.Id, out float ig) && sim.Time < ig && !o.Downed)).ToList();
            // A potion when it would matter (pockets come from the loadout; bots seated bare have none).
            byte drink = 0;
            bool underThreat = inSight.Any(o => !o.Downed);
            if (c.Health < c.MaxHealth * (underThreat ? 0.5f : Squad.DrinkBelowHealth))
            {
                var potion = Consumable.ByName(Consumable.HealthPotionName);
                if (server.PocketsOf(PlayerId).TryGetValue(potion.Name, out int n) && n > 0 && server.ConsumableCooldownRemaining(PlayerId, potion) <= 0f && !sim.HealsOverTime.Any(h => h.Target == PlayerId))
                    drink = (byte)(1 + Consumable.All.ToList().IndexOf(potion));
            }
            // A fight that goes nowhere (two Menders out-healing each other, casters kiting at the edge of their leash) is not worth staying in:
            // after 40 s in which nobody went down and the other side lost less than a seventh of its health, walk on and leave them be for a while.
            var standingFoes = inSight.Where(o => !o.Downed).ToList();
            if (standingFoes.Count > 0 && Squad.EngagedBoss < 0)
            {
                int downsNow = sim.Events.Count(e => e.Kind == FightEventKind.Downed || e.Kind == FightEventKind.Died);
                float foeHealth = standingFoes.Sum(o => o.Health);
                if (_pvpSince < 0f) { _pvpSince = sim.Time; _downsAtPvp = downsNow; _foeHealthAtStart = foeHealth; }
                else if (sim.Time - _pvpSince > 40f)
                {
                    if (downsNow == _downsAtPvp && foeHealth > _foeHealthAtStart * 0.85f)
                    {
                        foreach (var o in standingFoes) { if (o.IsMob) Squad.Avoid[RoomAt(o.Position)] = sim.Time + 120f; else _ignore[o.Id] = sim.Time + 40f; }
                        if (Squad.GoalRoom >= 0 && standingFoes.Any(o => o.IsMob && RoomAt(o.Position) == Squad.GoalRoom)) { Squad.Visited.Add(Squad.GoalRoom); Squad.GoalRoom = -1; }
                        inSight.Clear();
                    }
                    _pvpSince = -1f;
                }
            }
            else _pvpSince = -1f;
            if (inSight.Any(o => !o.Downed))
            {
                // Losing a fight against monsters is not a reason to die in it: at a fifth of health, walk back the way we came (mobs leash, a boss resets) unless a teammate is down here.
                // Losing is not a reason to die where we stand: at a fifth of health (a third against players who outnumber us), walk back the way we came (mobs leash, a boss resets, players have to chase) unless a teammate is down right here.
                var foes = inSight.Where(o => !o.Downed).ToList();
                int friends = mates.Count(m => m.Alive && Vec2.Distance(m.Position, c.Position) <= 8f);
                int enemyPlayers = foes.Count(o => !o.IsMob);
                float bail = enemyPlayers > friends ? 0.35f : 0.2f;
                // Not every fight is ours. Players seen at a distance are fought only when the squad is clearly the stronger side (EngageRatio times their
                // standing health), when they are busy with someone else and it is no worse than even, or when they are on top of us and cannot be outrun.
                // Otherwise the squad backs off and leaves that room alone for a while: a gate with another team on it, a brawl, a stronger squad.
                if (Squad.EngagedBoss < 0 && enemyPlayers > 0 && _retreatRoom >= 0 && _retreatRoom != RoomAt(c.Position))
                {
                    var theirPlayers = foes.Where(o => !o.IsMob).ToList();
                    var nearest = theirPlayers.OrderBy(o => Vec2.Distance(o.Position, c.Position)).First();
                    var standingMates = mates.Where(m => m.Alive).ToList();
                    float mine = standingMates.Sum(m => m.Health / m.MaxHealth);
                    // Their strength is what we see of each team plus the rest of its roster, whom we cannot see, taken as nearly whole.
                    int teamSize = server.Descriptor.Rules.TeamSize;
                    float theirs = theirPlayers.GroupBy(o => o.Team).Sum(g => g.Sum(o => o.Health / o.MaxHealth) + Math.Max(0, teamSize - g.Count()) * 0.85f);
                    bool busy = TheyAreBusyElsewhere(sim, foes, c.Team);
                    bool commit = mine >= theirs * Squad.EngageRatio || (busy && mine >= theirs * Squad.BusyRatio);
                    bool cornered = Vec2.Distance(nearest.Position, c.Position) <= Squad.CorneredCells || mates.Any(m => m.Downed && !m.Dead);
                    // Two squads that each wait for the other to leave would stand off until the clock runs out: after 45 s of backing off, the squad fights.
                    if (!commit && !cornered && _avoidFor < MaxAvoidSeconds)
                    {
                        _avoidFor += dt; _avoidIdle = 0f;
                        int theirRoom = RoomAt(nearest.Position);
                        if (theirRoom >= 0) Squad.Avoid[theirRoom] = sim.Time + 45f;
                        if (Squad.GoalRoom >= 0 && Squad.Avoid.TryGetValue(Squad.GoalRoom, out float till) && sim.Time < till) Squad.GoalRoom = -1;
                        _path = null; _did = "avoid";
                        return WalkTo(server, c, layout.Rooms[_retreatRoom].Center, dt);
                    }
                }
                if (Squad.EngagedBoss < 0 && c.Health < c.MaxHealth * bail && drink == 0 && !mates.Any(m => m.Downed && !m.Dead && Vec2.Distance(m.Position, c.Position) <= 6f) && _retreatRoom >= 0 && _retreatRoom != RoomAt(c.Position))
                {
                    _path = null;
                    var back = WalkTo(server, c, layout.Rooms[_retreatRoom].Center, dt);
                    return back;
                }
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
                var bossFoe = Squad.EngagedBoss >= 0 ? server.Director.Bosses.FirstOrDefault(b => b.RoomId == Squad.EngagedBoss && !b.Boss.Dead) : null;
                var squadIds = mates.Select(m => m.Id).OrderBy(i => i).ToList();
                if (bossFoe != null && inSight.Contains(bossFoe.Boss)) { _did = "boss"; PartyAct(sim, c, bossFoe.Boss, Math.Max(0, squadIds.IndexOf(PlayerId)), Math.Max(1, squadIds.Count), dt); }
                else if (server.Elite != null && !server.Elite.Mob.Dead && inSight.Contains(server.Elite.Mob) && mates.Count(m => m.Alive) >= 2) { _did = "elite"; PartyAct(sim, c, server.Elite.Mob, Math.Max(0, squadIds.IndexOf(PlayerId)), Math.Max(1, squadIds.Count), dt); }   // the elite is fought as a party fights a boss: a slot each round it
                else { _did = "fight"; SimpleBrain.Act(sim, c, dt); }
                _path = null;
                return drink == 0 ? (PlayerIntent?)null : new PlayerIntent { PlayerId = PlayerId, ConsumableSlot = drink, AimX = c.Position.X + c.Facing.X, AimY = c.Position.Y + c.Facing.Y };
            }
            _combatSince = -1f;
            _avoidIdle += dt; if (_avoidIdle > 8f) { _avoidFor = 0f; _avoidIdle = 0f; }
            if (drink != 0) return new PlayerIntent { PlayerId = PlayerId, ConsumableSlot = drink, AimX = c.Position.X + c.Facing.X, AimY = c.Position.Y + c.Facing.Y };
            { int here0 = RoomAt(c.Position); if (here0 >= 0 && !layout.Rooms[here0].IsBossRoom && layout.Rooms[here0].Mobs == null || here0 >= 0 && Squad.Visited.Contains(here0)) _retreatRoom = here0; }

            // 2. A downed teammate: get to them and hold Space. Before finishing anyone: a teammate bleeding out is the clock that matters.
            var downed = sim.Combatants.Values.Where(o => o.Team == c.Team && o.Downed && !o.Dead && state.Downed.ContainsKey(o.Id) && Vec2.Distance(o.Position, c.Position) <= range * 2 && !InHostileArena(server, o.Position)).OrderBy(o => Vec2.Distance(o.Position, c.Position)).FirstOrDefault();
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

            // 2c. A Mender tops the squad up between fights: no health comes back any other way.
            var heal = c.Abilities.FirstOrDefault(a => a.Kind == AbilityKind.Bolt && a.Filter == TargetFilter.Allies && a.Heal > 0f);
            if (heal != null && here0Free(server))
            {
                var hurt = mates.Where(m => m.Alive && m.Health < m.MaxHealth * 0.9f && Vec2.Distance(m.Position, c.Position) <= heal.RangeCells).OrderBy(m => m.Health / m.MaxHealth).FirstOrDefault();
                if (hurt != null && sim.Cast(c.Id, heal.Id, hurt.Position).Ok) { _did = "mend"; return Idle(PlayerId); }
            }
            // 3a. The shrine first: resurrect a dead teammate while it is worth it, arm the ultimate before a boss (a 2 s prayer at the altar).
            { var shrine = !Squad.UsesShrines ? null : ShrineErrand(server, c, dt); if (shrine.HasValue) return shrine.Value; }

            // 3. Something to do where we stand.
            string here = server.InteractionAvailable(PlayerId);
            // An interaction the rules refuse is left alone for a while, so a refused take never becomes a loop.
            if (!_hooked) { _hooked = true; server.EventRejected += (e, why) => { if (ActorOf(e) == PlayerId) _ignoreHereUntil = server.Sim.Time + 20f; }; }
            _lastHere = here;
            if (sim.Time < _ignoreHereUntil && server.ChannelOf(PlayerId) == null) here = null;
            // The simulator can down a teammate a moment before the round state hears of it (a cast during this tick's thinking): a revive is only started on someone the state knows is down, or the start is refused.
            if (here == "revive" && server.ChannelOf(PlayerId) == null && !sim.Combatants.Values.Any(o => o.Team == c.Team && o.Id != c.Id && o.Downed && !o.Dead && state.Downed.ContainsKey(o.Id) && Vec2.Distance(o.Position, c.Position) <= 1.5f)) here = null;
            if (here != null && here != "door-close" && (here != "extract" || Squad.Leaving)) return Hold(c, c.Position + c.Facing);

            // 4. Decide where to go.
            int room = server.RoomOf(PlayerId);
            DecideLeaving(server, c);
            // 4a. A corpse worth working (items, silver, an unopened body with its soul) within reach of the squad: go and hold Space over it.
            if (!(Squad.Leaving && server.State.ExtractsOpen) && !BagsFull(server, c))
            {
                var body = NextCorpse(server, c);
                if (body != null)
                {
                    if (!Squad.CorpseSince.ContainsKey(body.Id)) Squad.CorpseSince[body.Id] = sim.Time;
                    if (Vec2.Distance(body.Position, c.Position) <= 1.2f || sim.Time - Squad.CorpseSince[body.Id] > 45f) Squad.CorpsesDone.Add(body.Id);   // nothing left to do here (the interaction above would have held), or it will not be reached
                    else { _did = "corpse"; return WalkTo(server, c, body.Position.Cell, dt); }
                }
            }
            if (Squad.GoalRoom >= 0 && room == Squad.GoalRoom && Vec2.Distance(Vec2.Of(layout.Rooms[room].Center), c.Position) <= 0.8f && here == null)
            {
                // Arrived and nothing left to work here.
                if (!IsExtract(server, room) || !Squad.Leaving) { Squad.Visited.Add(room); Squad.GoalRoom = -1; }
                else return Idle(PlayerId);   // waiting at the gate for the extracts to open
            }
            { int ev = EventTargetRoom(server); if (ev >= 0 && Squad.GoalRoom != ev && !Squad.Leaving && Squad.EngagedBoss < 0) Squad.GoalRoom = -1; }   // the event will not wait: drop the walk to the next chest
            if (Squad.GoalRoom < 0 || !StillWorth(server, Squad.GoalRoom)) { int before = Squad.GoalRoom; Squad.GoalRoom = PickGoal(server, room); if (DebugLog != null && Squad.GoalRoom != before && !Squad.Resting) DebugLog($"   T{Squad.Team} {sim.Time:0}s goal {Squad.GoalRoom} {(Squad.GoalRoom >= 0 ? layout.Rooms[Squad.GoalRoom].Type.ToString() : "-")} (from room {room}, leaving {Squad.Leaving})"); }
            if (Squad.GoalRoom < 0) return Idle(PlayerId);
            if (Squad.Resting && Squad.GoalRoom == RestRoom(server) && room == Squad.GoalRoom) { _did = "rest"; _path = null; return Idle(PlayerId); }
            var goalRoom = layout.Rooms[Squad.GoalRoom];
            if (goalRoom.IsBossRoom && Squad.EngagedBoss != goalRoom.Id && !state.DeadBosses.Contains(goalRoom.Id) && goalRoom.Bounds.Expand(MobRules.LeashCells + 3).Contains(c.Position.Cell))
            {
                // At the boss's door and not committed: wait for the others outside its arena; give it up if they never come.
                _did = "wait";
                if (_waitSince < 0f) _waitSince = sim.Time;
                if (sim.Time - _waitSince > (HasHealer(Mates(server)) ? 45f : Mates(server).Any(m => m.Alive && m.Health < m.MaxHealth * Squad.BossReadyHealth) ? 130f : 25f)) { Squad.Avoid[goalRoom.Id] = sim.Time + 120f; Squad.GoalRoom = -1; _waitSince = -1f; }   // not fit or not gathered: try again later, not never
                var arena = server.Director.Bosses.FirstOrDefault(b => b.RoomId == goalRoom.Id);
                if (arena != null && arena.InArena(c.Position) && _retreatRoom >= 0) return WalkTo(server, c, layout.Rooms[_retreatRoom].Center, dt);
                return Idle(PlayerId);
            }
            _waitSince = -1f;
            return WalkTo(server, c, layout.Rooms[Squad.GoalRoom].Center, dt);
        }

        /// <summary>Did any of these foes land a blow on someone who is not on <paramref name="team"/> in the last few seconds?</summary>
        private static bool TheyAreBusyElsewhere(FightSim sim, List<Combatant> foes, int team)
        {
            var ids = new HashSet<int>(foes.Select(f => f.Id));
            for (int i = sim.Events.Count - 1; i >= 0; i--)
            {
                var e = sim.Events[i];
                if (e.Time < sim.Time - 4f) break;
                if (e.Kind == FightEventKind.Hit && ids.Contains(e.Source) && sim.Get(e.Target) is Combatant t && t.Team != team) return true;
            }
            return false;
        }

        public static Action<string> DebugLog;
        private float _avoidFor; private float _avoidIdle;
        public const float MaxAvoidSeconds = 45f;
        private int _retreatRoom = -1; private float _waitSince = -1f; private bool _passing;
        private float _pvpSince = -1f, _foeHealthAtStart; private int _downsAtPvp; private readonly Dictionary<int, float> _ignore = new Dictionary<int, float>();
        private bool here0Free(MatchServer server) => server.ChannelOf(PlayerId) == null && server.InteractionAvailable(PlayerId) == null;
        private static bool HasHealer(List<Combatant> mates) => mates.Any(m => m.Alive && m.Abilities.Any(a => a.Kind == AbilityKind.Bolt && a.Filter == TargetFilter.Allies && a.Heal > 0f));

        /// <summary>The squad's members still in the round (standing or downed), this bot included.</summary>
        private List<Combatant> Mates(MatchServer server) =>
            server.State.PlayerTeam.Where(kv => kv.Value == Squad.Team && !server.State.DeadPlayers.Contains(kv.Key) && !server.State.ExtractedPlayers.Contains(kv.Key))
                .Select(kv => server.Sim.Get(kv.Key)).Where(m => m != null && !m.Dead).ToList();

        private static BossController BossOf(MatchServer server, int combatantId)
        {
            foreach (var b in server.Director.Bosses) if (b.Boss.Id == combatantId || b.Adds.Contains(combatantId)) return b;
            return null;
        }

        private bool InHostileArena(MatchServer server, Vec2 p) => server.Director.Bosses.Any(b => !b.Boss.Dead && b.RoomId != Squad.EngagedBoss && b.InArena(p));

        /// <summary>Is the squad fit to take this boss on: all three standing, gathered, and healthy enough (the final boss asks more)?</summary>
        private bool ReadyFor(MatchServer server, Room room, List<Combatant> mates, Combatant me, bool gathered, MatchServer counting = null)
        {
            var standing = mates.Where(m => m.Alive).ToList();
            if (standing.Count < 3 || mates.Count != standing.Count) return false;   // the 2.5-player line: a pair does not walk into a boss
            if (room.Type == RoomType.FinalBoss && (Squad.BossesKilled < 1 || standing.Any(m => (server.PocketsOf(m.Id).TryGetValue(Consumable.HealthPotionName, out int pots) ? pots : 0) < Squad.FinalBossPotionsEach))) return false;
            // Planning (counting != null) counts the potions in the pockets as health the squad will have by the door; committing counts only what is there.
            float Hp(Combatant m) => Math.Min(1f, m.Health / m.MaxHealth + (counting != null ? PotionHealth(counting, m) : 0f));
            if (room.Type == RoomType.FinalBoss ? standing.Average(Hp) < Squad.FinalReadyHealth : standing.Min(Hp) < Squad.BossReadyHealth || standing.Any(m => (server.PocketsOf(m.Id).TryGetValue(Consumable.HealthPotionName, out int pk) ? pk : 0) < Squad.BossPotionsEach)) return false;
            return !gathered || standing.All(m => Vec2.Distance(m.Position, me.Position) <= 5f);
        }

        /// <summary>
        /// Commit to a boss when the squad is gathered at its door and fit; give it up when the fight is lost
        /// (two of three down, or the boss well ahead of the squad's health), walking out so it resets.
        /// </summary>
        private void UpdateBossCommitment(MatchServer server, Combatant me, List<Combatant> mates)
        {
            var layout = server.Layout;
            if (Squad.EngagedBoss >= 0)
            {
                var boss = server.Director.Bosses.FirstOrDefault(b => b.RoomId == Squad.EngagedBoss);
                if (boss == null || boss.Boss.Dead) { if (boss != null && boss.Boss.Dead && layout.Rooms[boss.RoomId].Type != RoomType.FinalBoss) Squad.BossesKilled++; if (boss != null) DebugLog?.Invoke($"T{Squad.Team} kills {boss.Instance.DisplayName} at {server.Sim.Time:0}"); Squad.EngagedBoss = -1; return; }
                var standing = mates.Where(m => m.Alive).ToList();
                float squadHealth = standing.Count == 0 ? 0f : standing.Sum(m => m.Health / m.MaxHealth) / 3f;
                bool anyoneThere = mates.Any(m => boss.InArena(m.Position));
                bool lost = standing.Count == 0 || (standing.Count == 2 && boss.HealthFraction > Squad.CutLossesAboveBossHealth) || (standing.Count == 1 && boss.HealthFraction > 0.25f) || (boss.HealthFraction > 0.5f && squadHealth < boss.HealthFraction * 0.35f) || (boss.Enraged && boss.HealthFraction > 0.4f);
                if ((lost || (!anyoneThere && !boss.Engaged && Squad.GoalRoom != Squad.EngagedBoss)) && DebugLog != null) DebugLog($"T{Squad.Team} drops {boss.Instance.DisplayName} at {server.Sim.Time:0}: lost {lost} standing {standing.Count} squadHp {squadHealth:0.00} boss {boss.HealthFraction:0.00} enraged {boss.Enraged} anyoneThere {anyoneThere} engaged {boss.Engaged}");
                if (lost || (!anyoneThere && !boss.Engaged && Squad.GoalRoom != Squad.EngagedBoss))
                {
                    Squad.Avoid[Squad.EngagedBoss] = server.Sim.Time + 240f;
                    if (Squad.GoalRoom == Squad.EngagedBoss) Squad.GoalRoom = -1;
                    Squad.EngagedBoss = -1;
                }
                return;
            }
            if (Squad.GoalRoom < 0 || !layout.Rooms[Squad.GoalRoom].IsBossRoom || server.State.DeadBosses.Contains(Squad.GoalRoom)) return;
            var goal = server.Director.Bosses.FirstOrDefault(b => b.RoomId == Squad.GoalRoom && !b.Boss.Dead);
            if (goal == null) return;
            bool atDoor = layout.Rooms[goal.RoomId].Bounds.Expand(MobRules.LeashCells + 3).Contains(me.Position.Cell);
            if (atDoor && ReadyFor(server, layout.Rooms[goal.RoomId], mates, me, gathered: true)) { Squad.EngagedBoss = goal.RoomId; DebugLog?.Invoke($"T{Squad.Team} commits to {goal.Instance.DisplayName} at {server.Sim.Time:0} hp {string.Join(",", mates.Select(m => (m.Health / m.MaxHealth).ToString("0.00")))}"); }
        }

        /// <summary>Walk out of a boss's arena by the nearest way out: the neighbouring room closest to us.</summary>
        private PlayerIntent Flee(MatchServer server, Combatant c, BossController boss, float dt)
        {
            // The nearest walkable cell outside the arena (and a step beyond it), by breadth-first search from here, away from the boss.
            var layout = server.Layout;
            var start = c.Position.Cell;
            var seen = new HashSet<GridPoint> { start };
            var queue = new Queue<(GridPoint p, int d)>(); queue.Enqueue((start, 0));
            GridPoint? found = null;
            while (queue.Count > 0 && seen.Count < 4000)
            {
                var (p, d) = queue.Dequeue();
                if (!boss.InArena(Vec2.Of(p)) && Vec2.Distance(Vec2.Of(p), boss.Boss.Position) > 3.5f && d >= 2) { found = p; break; }
                foreach (var dir in GridPoint.Cardinals)
                {
                    var n = p + dir;
                    if (!layout.InBounds(n) || !layout.IsWalkable(n) || !seen.Add(n)) continue;
                    queue.Enqueue((n, d + 1));
                }
            }
            if (!found.HasValue) return Idle(PlayerId);
            var path = GridPath.Find(layout, start, found.Value, closedDoors: server.Sim.ClosedDoors);
            if (path == null || path.Count < 2) return WalkTo(server, c, found.Value, dt);   // a shut door on the way out: open it
            _path = null;
            var next = Vec2.Of(path[1]);
            var dirv = (next - c.Position).Normalized;
            return new PlayerIntent { PlayerId = PlayerId, MoveX = dirv.X, MoveY = dirv.Y, AimX = next.X, AimY = next.Y };
        }

        /// <summary>
        /// Fight a boss as one of a party. Friendly fire is full, so a party that crowds one side of a
        /// boss spends its time stepping around its own members (the lab measured a bot trio dealing less
        /// damage than one Berserker alone). Each member takes its own slot around the boss, spread evenly,
        /// melee in reach and ranged at range, and swings from there; telegraphs and ground zones are
        /// still dodged by the fight brain first.
        /// </summary>
        public static void PartyAct(FightSim sim, Combatant c, Combatant boss, int slot, int slots, float dt)
        {
            if (!c.CanAct) return;
            var basic = c.Abilities.FirstOrDefault(a => a.Kind == AbilityKind.Basic);
            bool winding = boss.Channelling && (boss.ChannelName == "crush:" + c.Id || (boss.ChannelName == "groundslam" && Vec2.Distance(boss.Position, c.Position) <= 3.5f));
            bool burning = sim.Zones.Any(z => z.CasterTeam != c.Team && z.Ability.Damage > 0f && Vec2.Distance(z.Centre, c.Position) <= z.Ability.Size + 0.3f);
            if (basic == null || winding || burning) { SimpleBrain.Act(sim, c, dt); return; }
            // A Mender's first job is the party, wherever it stands: the fight brain heals and purges before anything else.
            var mend = c.Abilities.FirstOrDefault(a => a.Kind == AbilityKind.Bolt && a.Filter == TargetFilter.Allies && a.Heal > 0f);
            if (mend != null && sim.Combatants.Values.Any(o => o.Team == c.Team && o.Alive && o.Health < o.MaxHealth * 0.6f)) { SimpleBrain.Act(sim, c, dt); return; }
            bool ranged = basic.Shape == AbilityShape.Projectile || basic.Shape == AbilityShape.Pierce || basic.RangeCells >= 3f;
            float radius = ranged ? Math.Max(2.5f, Math.Min(basic.RangeCells - 0.5f, 4.5f)) : Math.Max(0.8f, basic.RangeCells * 0.8f);
            double phase = (boss.Id % 7) * 0.45;
            Vec2 spot = c.Position; bool found = false;
            for (int k = 0; k < 6 && !found; k++)
            {
                double a = phase + slot * 2 * Math.PI / slots + (k % 2 == 0 ? 1 : -1) * ((k + 1) / 2) * 0.35;
                var p = boss.Position + new Vec2((float)Math.Cos(a), (float)Math.Sin(a)) * radius;
                if (sim.Walkable(p, c) && sim.CanSee(p, boss.Position)) { spot = p; found = true; }
            }
            if (found && Vec2.Distance(spot, c.Position) > (ranged ? 1.2f : 0.6f))
            {
                var before = c.Position;
                SimpleBrain.Approach(sim, c, spot, dt);
                if (Vec2.Distance(before, c.Position) > 0.01f) return;   // on the way; a body that cannot get there fights from here
            }
            SimpleBrain.Act(sim, c, dt);
        }

        private bool IsExtract(MatchServer s, int room) => s.Layout.Rooms[room].Type == RoomType.Extract || (s.Layout.Rooms[room].Type == RoomType.FinalBoss && s.State.BossExitOpen);

        // ---------------- the shrine ----------------

        /// <summary>Shrine prayers this bot completed (arm, resurrect) and ones broken off before they took (a hit, walking off), for the lab's counts.</summary>
        public int ShrineArms, ShrineResurrects, ShrineBroken;
        private bool _shrineAsked, _shrineHooked, _praying; private float _shrineIgnoreUntil = -1f, _shrineWalkSince = -1f;

        /// <summary>
        /// Count prayers as their events land (they take <see cref="ShrineRules.ChannelSeconds"/>), and answer last tick's choice: a refused start leaves
        /// the shrine alone for 20 s. A broken prayer is simply started again on a later tick if it is still worth it.
        /// </summary>
        private void NoteShrineAnswer(MatchServer server)
        {
            if (!_shrineHooked)
            {
                _shrineHooked = true;
                server.EventRaised += e =>
                {
                    if (e.Kind == MatchEventKind.UltimateArmed && e.A == PlayerId) ShrineArms++;
                    else if (e.Kind == MatchEventKind.PlayerResurrected && e.B == PlayerId) ShrineResurrects++;
                    else if (e.Kind == MatchEventKind.LootInterrupted && e.A == PlayerId && _praying) ShrineBroken++;
                };
            }
            _praying = server.ChannelOf(PlayerId) == MatchServer.ShrineChannel;
            if (!_shrineAsked) return;
            _shrineAsked = false;
            if (!_praying && server.ShrineRefusal(PlayerId) != null) _shrineIgnoreUntil = server.Sim.Time + 20f;
        }

        /// <summary>
        /// Souls are spent at a shrine's altar, in this order: a dead teammate (resurrectable, the bot can pay, enough round left, not on the way out), then
        /// the bot's own ultimate (two souls, not armed; walked to only when the squad heads for a boss and a shrine is close, otherwise armed in passing when
        /// the bot is in a shrine room anyway). The bot walks up to the altar (<see cref="MatchServer.AltarSpot"/>), makes the choice, and stands still holding
        /// Space for the <see cref="ShrineRules.ChannelSeconds"/> prayer; a hit or a fight breaks it and the errand starts over once it is quiet again.
        /// Returns the intent for this tick, or null to carry on. A refused choice leaves the shrine alone for 20 s; a walk that takes over 40 s is given up for a minute.
        /// </summary>
        private PlayerIntent? ShrineErrand(MatchServer server, Combatant c, float dt)
        {
            var st = server.State; var sim = server.Sim; var layout = server.Layout;
            // Praying: stand still with Space held until it takes (or a hit breaks it).
            if (server.ChannelOf(PlayerId) == MatchServer.ShrineChannel) { _did = "pray"; _shrineWalkSince = -1f; return Hold(c, c.Position + c.Facing); }
            int souls = st.SoulsOf(PlayerId);
            if (souls <= 0 || !server.Descriptor.Rules.ShrinesUsable || sim.Time < _shrineIgnoreUntil || server.ChannelOf(PlayerId) != null) { _shrineWalkSince = -1f; return null; }
            float left = server.Clock.SecondsRemaining(server.Elapsed);
            bool resWorth = left >= Squad.ResurrectWithSecondsLeft && !(Squad.Leaving && st.ExtractsOpen);
            var dead = st.PlayerTeam.Where(kv => kv.Value == Squad.Team && kv.Key != PlayerId && st.DeadPlayers.Contains(kv.Key) && !st.LeftPlayers.Contains(kv.Key) && !st.ResurrectedOnce.Contains(kv.Key)).Select(kv => kv.Key).OrderBy(p => p).ToList();
            bool wantRes = resWorth && dead.Count > 0 && CheapestShrine(server) >= 0;   // someone to bring back, and a shrine within reach we can pay at
            bool armed = st.UltimateCharges.TryGetValue(PlayerId, out int charges) && charges > 0;
            bool wantArm = !armed && !wantRes && souls >= FightRules.SoulsToArmUltimate;   // souls a teammate's life needs are not spent on the ultimate
            var o = server.ShrineOptionsFor(PlayerId);
            if (o.AtAltar && o.Refusal == null)
            {
                ShrineChoice pick = null;
                if (resWorth && dead.Count > 0) pick = o.Resurrect.FirstOrDefault(r => r.Available);
                if (pick == null && wantArm && o.Arm.Available) pick = o.Arm;
                if (pick != null)
                {
                    _shrineAsked = true; _shrineWalkSince = -1f; _did = "shrine";
                    DebugLog?.Invoke($"   T{Squad.Team} {PlayerId} {sim.Time:0}s shrine {pick}");
                    return new PlayerIntent { PlayerId = PlayerId, ShrineAction = (byte)pick.Action, ShrineArg = pick.Arg, Interact = true, AimX = c.Position.X + c.Facing.X, AimY = c.Position.Y + c.Facing.Y };
                }
            }
            // Worth a walk? To the altar of the cheapest shrine for a teammate; for the ultimate, the altar of the shrine room we are in (in passing) or a close one before a boss.
            int target = wantRes ? CheapestShrine(server) : -1;
            if (target < 0 && wantArm)
            {
                bool bossAhead = Squad.GoalRoom >= 0 && layout.Rooms[Squad.GoalRoom].IsBossRoom && !st.DeadBosses.Contains(Squad.GoalRoom) && Squad.EngagedBoss < 0;
                int here = server.RoomOf(PlayerId);
                if (o.AtShrine && !o.AtAltar && o.Refusal == ShrineRules.NotAtAltar) target = o.ShrineRoom;
                else if (bossAhead)
                {
                    target = layout.Rooms.Where(r => r.HasShrine).Select(r => r.Id).OrderBy(r => RoomsAway(layout, here, r)).ThenBy(r => r).DefaultIfEmpty(-1).First();
                    if (target >= 0 && RoomsAway(layout, here, target) > Squad.ArmDetourRooms) target = -1;
                }
            }
            if (target < 0) { _shrineWalkSince = -1f; return null; }
            if (_shrineWalkSince < 0f) _shrineWalkSince = sim.Time;
            else if (sim.Time - _shrineWalkSince > 40f) { _shrineIgnoreUntil = sim.Time + 60f; _shrineWalkSince = -1f; return null; }
            _did = "to shrine";
            return WalkTo(server, c, server.AltarSpot(target).Cell, dt);
        }

        /// <summary>The shrine room where a resurrection is cheapest (the cheap-shrine event's, for its minute), nearest first, that this bot can pay at and reach within <see cref="BotSquad.ResurrectDetourRooms"/>; -1 if none.</summary>
        private int CheapestShrine(MatchServer server)
        {
            var st = server.State; var layout = server.Layout;
            int here = server.RoomOf(PlayerId), souls = st.SoulsOf(PlayerId);
            return layout.Rooms.Where(r => r.HasShrine)
                .Select(r => (room: r.Id, cost: st.ResurrectionCostAt(r.Id), far: RoomsAway(layout, here, r.Id)))
                .Where(x => x.cost <= souls && x.far <= Squad.ResurrectDetourRooms)
                .OrderBy(x => x.cost).ThenBy(x => x.far).ThenBy(x => x.room).Select(x => x.room).DefaultIfEmpty(-1).First();
        }

        // ---------------- the mid-round event ----------------

        /// <summary>Seconds a bot allows per room of walking when it races the unsealed vault's minute.</summary>
        public const float EventSecondsPerRoom = 5.5f;

        /// <summary>Doors-and-corridors hops between two rooms (breadth-first over the room graph), or int.MaxValue.</summary>
        private static int RoomsAway(DungeonLayout layout, int from, int to)
        {
            if (from < 0 || to < 0) return int.MaxValue;
            var dist = new Dictionary<int, int> { [from] = 0 };
            var q = new Queue<int>(); q.Enqueue(from);
            while (q.Count > 0)
            {
                int r = q.Dequeue();
                if (r == to) return dist[r];
                foreach (int n in layout.Rooms[r].Neighbours) if (!dist.ContainsKey(n)) { dist[n] = dist[r] + 1; q.Enqueue(n); }
            }
            return int.MaxValue;
        }

        /// <summary>The relic of a fallen elite, lying on a floor nobody has taken it from: its room, or -1.</summary>
        private static int RelicRoom(MatchServer server)
        {
            var st = server.State;
            if (!st.EliteDead || st.Layout.EventLoot == null) return -1;
            int id = LayoutIndex.EventLootId;
            return st.FloorItems.TryGetValue(id, out int room) && !st.ItemOwner.ContainsKey(id) && !st.DestroyedItems.Contains(id) ? room : -1;
        }

        /// <summary>Whole, healthy (counting the potions it will drink) and stocked: the squad the roaming elite is a fair fight for.</summary>
        private bool EliteFit(MatchServer server)
        {
            var mates = Mates(server); var standing = mates.Where(m => m.Alive).ToList();
            if (standing.Count < 3 || mates.Count != standing.Count) return false;
            int potions = 0; float hp = 0f;
            foreach (var m in standing)
            {
                server.PocketsOf(m.Id).TryGetValue(Consumable.HealthPotionName, out int pots); potions += pots;
                if (m.Health < m.MaxHealth * 0.5f) return false;
                hp += Math.Min(1f, m.Health / m.MaxHealth + PotionHealth(server, m));
            }
            return potions >= Squad.ElitePotionsTogether && hp / standing.Count >= Squad.EliteReadyHealth;
        }

        /// <summary>The room the mid-round event wants this squad in right now, or -1: a fallen elite's relic, the unsealed vault (lock failed, not yet opened, near enough to reach in its minute), or a live elite a fit squad can take.</summary>
        private int EventTargetRoom(MatchServer server)
        {
            var st = server.State;
            int relic = RelicRoom(server);
            if (relic >= 0) return Squad.Avoid.TryGetValue(relic, out float until) && server.Sim.Time < until ? -1 : relic;
            if (!st.EventOpen) return -1;
            if (st.ActiveEvent == DungeonEventKind.VaultUnsealed)
            {
                int room = st.EventRoom; var mates = Mates(server);
                if (mates.Count == 0 || Squad.Visited.Contains(room) || !server.VaultUnsealedAt(room) || st.OpenVaults.Contains(room)) return -1;
                float left = server.Clock.Plan.EndsAt - server.Sim.Time;
                return left > 5f && RoomsAway(server.Layout, server.RoomOf(mates[0].Id), room) * EventSecondsPerRoom <= left ? room : -1;
            }
            if (st.ActiveEvent == DungeonEventKind.RoamingElite && server.Elite != null && EliteFit(server)) return st.EliteRoom;
            return -1;
        }

        private bool EventRoom(MatchServer server, int room) => room >= 0 && EventTargetRoom(server) == room;


        /// <summary>Should the clock alone send this squad out? Not while an event it is fit for is on and enough of the round is left to walk out afterwards.</summary>
        private bool HoldForEvent(MatchServer server)
        {
            if (server.Clock.SecondsRemaining(server.Elapsed) <= Squad.EventGraceSeconds) return false;
            var plan = server.Clock.Plan; var st = server.State;
            if (RelicRoom(server) >= 0) return Mates(server).Any(m => m.Alive);
            switch (plan.Kind)
            {
                case DungeonEventKind.RoamingElite: return !st.EliteDead && EliteFit(server);
                case DungeonEventKind.VaultUnsealed: return server.Sim.Time < plan.EndsAt && Mates(server).Count(m => m.Alive) >= 3 && RoomsAway(server.Layout, server.RoomOf(Mates(server)[0].Id), plan.RoomId) * EventSecondsPerRoom <= plan.EndsAt - Math.Min(server.Sim.Time, plan.AtSeconds);
                default: return false;
            }
        }


        /// <summary>The squad's load against its capacity: true when the bags are nearly full (<see cref="BotSquad.BagsFullFraction"/>), so the next piece costs speed.</summary>
        private bool BagsFull(MatchServer server, Combatant me)
        {
            var mates = Mates(server);
            float load = 0f, cap = 0f;
            foreach (var m in mates) { if (m.IsMob || m.Stats == null) continue; load += server.LoadKgOf(m.Id); cap += m.Stats.CarryKg; }
            return cap > 0f && load >= cap * Squad.BagsFullFraction;
        }

        /// <summary>Potions a member can still drink, counted at most twice (a potion has a minute's cooldown), as health the squad can count on getting back.</summary>
        private float PotionHealth(MatchServer server, Combatant m)
        {
            var potion = Consumable.ByName(Consumable.HealthPotionName);
            server.PocketsOf(m.Id).TryGetValue(potion.Name, out int n);
            return Math.Min(2, n) * 0.3f;
        }

        private void DecideLeaving(MatchServer server, Combatant c)
        {
            if (Squad.Leaving) return;
            var state = server.State;
            var members = state.PlayerTeam.Where(kv => kv.Value == c.Team).Select(kv => kv.Key).ToList();
            int carried = state.ItemOwner.Count(kv => members.Contains(kv.Value));
            // Enough: the bags are nearly full or the squad holds as many pieces as it set out for. Time spent after that is risk.
            if (carried > 0 && (carried >= Squad.ItemsWanted || BagsFull(server, c))) { Squad.Leaving = true; DebugLog?.Invoke($"T{Squad.Team} leaves at {server.Sim.Time:0}: full, carrying {carried}"); return; }
            if (server.Clock.SecondsRemaining(server.Elapsed) <= Squad.LeaveWithSecondsLeft && !HoldForEvent(server)) { Squad.Leaving = true; DebugLog?.Invoke($"T{Squad.Team} leaves at {server.Sim.Time:0}: time"); return; }
            // A squad that has lost someone, or is worn down beyond what its potions will mend, takes what it has out.
            var mates = Mates(server); var standing = mates.Where(m => m.Alive).ToList();
            bool weak = mates.Count < members.Count(m => !state.ExtractedPlayers.Contains(m)) || standing.Count == 0 || standing.Average(m => Math.Min(1f, m.Health / m.MaxHealth + PotionHealth(server, m))) < Squad.LeaveBelowHealth;
            if (weak && carried > 0) { Squad.Leaving = true; DebugLog?.Invoke($"T{Squad.Team} leaves at {server.Sim.Time:0}: weak, carrying {carried}"); }
        }

        /// <summary>The nearest corpse worth working: items left, silver, or not opened yet; not in a boss arena, not one the squad has finished with.</summary>
        private Combatant NextCorpse(MatchServer server, Combatant me)
        {
            var state = server.State;
            Combatant best = null; float bestD = 0f;
            foreach (var kv in state.Corpses)
            {
                if (Squad.CorpsesDone.Contains(kv.Key)) continue;
                bool worth = !state.OpenedCorpses.Contains(kv.Key) || kv.Value.Count > 0 || state.CorpseSilver.ContainsKey(kv.Key);
                if (!worth) { Squad.CorpsesDone.Add(kv.Key); continue; }
                var body = server.Sim.Get(kv.Key);
                if (body == null || !body.Dead || InHostileArena(server, body.Position)) continue;
                int r = body.Position.Cell.X >= 0 && server.Layout.InBounds(body.Position.Cell) ? server.Layout.RoomIdAt[body.Position.Cell.X, body.Position.Cell.Y] : -1;
                if (r >= 0 && Squad.Avoid.TryGetValue(r, out float until) && server.Sim.Time < until) continue;
                float d = Vec2.Distance(body.Position, me.Position);
                if (d > 45f) continue;
                if (best == null || d < bestD) { best = body; bestD = d; }
            }
            return best;
        }

        /// <summary>Is the chosen room still worth walking to? Loot taken by someone else, or a boss killed by another team, changes the answer.</summary>
        private bool StillWorth(MatchServer server, int room)
        {
            if (Squad.Resting) return false;   // re-decide every tick: a boss may have become fit, the gates may be near
            if (Squad.Leaving) return IsExtract(server, room) || room == Squad.GoalRoom;
            return Worth(server, room);
        }

        private bool Worth(MatchServer server, int room)
        {
            var state = server.State; var layout = server.Layout; var r = layout.Rooms[room];
            if (EventRoom(server, room)) return true;
            if (Squad.Visited.Contains(room)) return false;
            if (r.Type == RoomType.Spawn) return false;
            if (Squad.Avoid.TryGetValue(room, out float until) && server.Sim.Time < until) return false;
            if (r.IsBossRoom && !state.DeadBosses.Contains(room))
            {
                if (!Squad.TakeOnBosses) return false;
                int standing = state.PlayerTeam.Count(kv => kv.Value == Squad.Team && !state.DeadPlayers.Contains(kv.Key) && !state.ExtractedPlayers.Contains(kv.Key));
                if (standing < 3) return false;   // the design's 2.5-player line: a bot pair does not walk into a boss
                var mates = Mates(server);
                if (Squad.EngagedBoss != room && mates.Count > 0 && !ReadyFor(server, r, mates, mates[0], gathered: false, server) && !(HasHealer(mates) && mates.All(m => m.Alive) && mates.Count >= 3)) return false;   // too hurt for this one (a squad with a Mender heals up at the door)
            }
            if (r.Type == RoomType.Vault && !state.OpenVaults.Contains(room) && !layout.Keys.Any(k => k.VaultRoomId == room && state.KeyHolder.TryGetValue(k.KeyId, out int h) && state.PlayerTeam.TryGetValue(h, out int ht) && ht == Squad.Team)) return false;
            bool chest = (r.Loot.Count > 0 || r.Silver > 0) && (!state.OpenedContainers.Contains(room) || state.VisibleItemsIn(room).Any() || (!state.SilverTakenRooms.Contains(room) && r.Silver > 0));
            bool key = layout.Keys.Any(k => k.HolderRoomId == room && !state.KeyHolder.ContainsKey(k.KeyId) && !state.DestroyedKeys.Contains(k.KeyId));
            bool boss = r.IsBossRoom && !state.DeadBosses.Contains(room);
            return chest || key || boss;
        }

        /// <summary>Seconds a bot allows per room of walking when it times its departure for the gates, and the margin it keeps on top.</summary>
        public const float StageSecondsPerRoom = 9f, StageMarginSeconds = 12f;

        /// <summary>Is this room nearer to our spawn than to every other team's?</summary>
        private bool Ours(MatchServer server, int room, int myDistance)
        {
            if (Squad.OthersDistance == null)
            {
                var layout = server.Layout; var homes = server.Descriptor.TeamSpawnRooms;
                var best = new Dictionary<int, int>();
                for (int t = 0; t < homes.Count; t++)
                {
                    if (t == Squad.Team) continue;
                    var d = new Dictionary<int, int> { [homes[t]] = 0 }; var q = new Queue<int>(); q.Enqueue(homes[t]);
                    while (q.Count > 0) { int r = q.Dequeue(); foreach (int n in layout.Rooms[r].Neighbours) if (!d.ContainsKey(n)) { d[n] = d[r] + 1; q.Enqueue(n); } }
                    foreach (var kv in d) if (!best.TryGetValue(kv.Key, out int b) || kv.Value < b) best[kv.Key] = kv.Value;
                }
                Squad.OthersDistance = best;
            }
            // Distances are from where we stand now, which is near enough to home early in the round, when the contested rooms matter.
            return !Squad.OthersDistance.TryGetValue(room, out int other) || myDistance < other;
        }

        /// <summary>Whole, healthy enough and carrying less than <see cref="BotSquad.SatisfiedItems"/>: worth waiting for what the round may still offer.</summary>
        private bool WantsToStay(MatchServer server)
        {
            var state = server.State;
            var members = state.PlayerTeam.Where(kv => kv.Value == Squad.Team).Select(kv => kv.Key).ToList();
            var mates = Mates(server);
            if (mates.Count < members.Count || mates.Any(m => !m.Alive) || members.Any(m => state.ExtractedPlayers.Contains(m))) return false;
            if (state.ItemOwner.Count(kv => members.Contains(kv.Value)) >= Squad.SatisfiedItems) return false;
            return server.Clock.SecondsRemaining(server.Elapsed) > Squad.LeaveWithSecondsLeft;
        }

        private int RestRoom(MatchServer server)
        {
            var homes = server.Descriptor.TeamSpawnRooms;
            return Squad.Team >= 0 && Squad.Team < homes.Count ? homes[Squad.Team] : -1;
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
            var restRoom = RestRoom(server);
            if (Squad.Leaving)
            {
                var exits = layout.Rooms.Where(r => r.Type == RoomType.Extract || (r.Type == RoomType.FinalBoss && server.State.BossExitOpen)).Where(r => dist.ContainsKey(r.Id)).ToList();
                // Another team is on the nearest gate (we saw them and backed off): the next nearest, unless every gate is taken.
                var open = exits.Where(r => !(Squad.Avoid.TryGetValue(r.Id, out float until) && server.Sim.Time < until)).ToList();
                var gates = (open.Count > 0 ? open : exits).OrderBy(r => dist[r.Id]).FirstOrDefault();
                // The gates are not open yet: wait at home and set off with the walk (and a margin) to spare, rather than camping the gate where every team arrives.
                if (gates != null && !server.State.ExtractsOpen && restRoom >= 0 && server.Clock.SecondsUntilExtracts(server.Elapsed) > dist[gates.Id] * StageSecondsPerRoom + StageMarginSeconds) { Squad.Resting = true; return restRoom; }
                Squad.Resting = false;
                return gates?.Id ?? -1;
            }
            var pick = layout.Rooms.Where(r => dist.ContainsKey(r.Id) && Worth(server, r.Id))
                .OrderBy(r => EventRoom(server, r.Id) ? 0 : 1)                                                                 // the mid-round event first: it will not wait
                .OrderBy(r => r.Type == RoomType.FinalBoss ? 2 : r.IsBossRoom && !server.State.DeadBosses.Contains(r.Id) ? 1 : 0)      // ordinary rooms, then bosses, the final boss last
                .ThenBy(r => !Ours(server, r.Id, dist[r.Id]) ? 1 : 0)                                                          // our own side first: the rooms between two spawns are where squads collide
                .ThenBy(r => dist[r.Id]).ThenBy(r => r.Id).FirstOrDefault();
            if (pick == null && !server.State.ExtractsOpen && restRoom >= 0) { Squad.Resting = true; return restRoom; }   // nothing to do until the gates open: wait at home (potions work, a boss may become fit to take)
            Squad.Resting = false;
            // The gates are open and nothing is worth a walk. A whole squad that has little to show for its round waits at home for a chance (a corpse, a boss it is fit for
            // once the potions are back, the clock running low) instead of leaving with empty bags.
            if (pick == null && WantsToStay(server)) { Squad.Resting = true; return restRoom; }
            if (pick == null) { Squad.Leaving = true; DebugLog?.Invoke($"T{Squad.Team} leaves at {server.Sim.Time:0}: nothing left, visited {Squad.Visited.Count} of {layout.Rooms.Count}"); if (DebugLog != null) foreach (var rr in layout.Rooms) DebugLog($"   room {rr.Id} {rr.Type} loot {rr.Loot.Count} silver {rr.Silver} mobs {(rr.Mobs != null ? rr.Mobs.Count : 0)} opened {server.State.OpenedContainers.Contains(rr.Id)} visited {Squad.Visited.Contains(rr.Id)} avoid {Squad.Avoid.ContainsKey(rr.Id)} dist {(dist.TryGetValue(rr.Id, out int dd) ? dd : -1)}"); return PickGoal(server, from); }
            return pick.Id;
        }

        private PlayerIntent Hold(Combatant c, Vec2 aim) => new PlayerIntent { PlayerId = PlayerId, Interact = true, AimX = aim.X, AimY = aim.Y };

        private PlayerIntent WalkTo(MatchServer server, Combatant c, GridPoint target, float dt)
        {
            var sim = server.Sim; var layout = server.Layout;
            int goalKey = target.X * 10007 + target.Y;
            if (_path == null || _pathGoal != goalKey || _path.Count == 0 || Vec2.Distance(Vec2.Of(_path[0]), c.Position) > 2.5f)
            {
                // Route around boss rooms the squad has not committed to: their arena is not a corridor.
                var avoid = new HashSet<GridPoint>(sim.ClosedDoors);
                foreach (var b in server.Director.Bosses)
                {
                    if (b.Boss.Dead || b.RoomId == Squad.EngagedBoss || b.RoomId == Squad.GoalRoom) continue;
                    var bnd = layout.Rooms[b.RoomId].Bounds.Expand(MobRules.LeashCells + 1);
                    for (int x = bnd.X; x < bnd.XMax; x++) for (int y = bnd.Y; y < bnd.YMax; y++) { var p = new GridPoint(x, y); if (p != target && p != c.Position.Cell && layout.InBounds(p) && b.InArena(Vec2.Of(p))) avoid.Add(p); }
                }
                _path = GridPath.Find(layout, c.Position.Cell, target, closedDoors: avoid);
                _passing = _path == null;
                if (_passing && !Squad.Leaving && Squad.GoalRoom >= 0 && target == layout.Rooms[Squad.GoalRoom].Center) { Squad.Visited.Add(Squad.GoalRoom); Squad.GoalRoom = -1; return Idle(PlayerId); }   // only through a boss we are not taking: not worth it
                if (_passing) _path = GridPath.Find(layout, c.Position.Cell, target, closedDoors: sim.ClosedDoors) ?? GridPath.Find(layout, c.Position.Cell, target);   // the way out runs past a boss: run it
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
