using System;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Combat;

namespace DungeonGen.Core.Net
{
    /// <summary>
    /// A scripted player for the tutorial: each tick it reads the tracker's current lesson and returns the intent a player at the keyboard would send
    /// (walk to the door and hold Interact, fight the packs, hold Interact at the chest, pray at the altar, cast the ultimate on the boss, hold Interact
    /// in the extract). It goes through <see cref="MatchServer.SetIntent"/> like the input layer does, so everything it achieves is the real rules
    /// working. For tests and tools (the xUnit round and the PlayMode tutorial test); a player never sees it.
    /// </summary>
    public sealed class TutorialPilot
    {
        public MatchServer Server { get; }
        public int Player { get; }
        public TutorialRoute Route { get; }
        public TutorialTracker Tracker { get; }
        /// <summary>What it is doing now, for logs.</summary>
        public string Doing { get; private set; } = "";

        private List<GridPoint> _path;
        private GridPoint _pathTo;
        private float _prayRetry, _stuck;
        private Vec2 _lastPos;

        public TutorialPilot(MatchServer server, int player, TutorialRoute route, TutorialTracker tracker)
        {
            Server = server; Player = player; Route = route; Tracker = tracker;
        }

        private FightSim Sim => Server.Sim;
        private DungeonLayout Layout => Server.Layout;

        /// <summary>The intent for this tick.</summary>
        public PlayerIntent Next(float dt)
        {
            var me = Sim.Get(Player);
            var idle = new PlayerIntent { PlayerId = Player };
            if (me == null || me.Dead || me.Downed) { Doing = "down"; return idle; }
            idle.AimX = me.Position.X + me.Facing.X; idle.AimY = me.Position.Y + me.Facing.Y;
            if (me.Health < me.MaxHealth * 0.45f && Server.PocketsOf(Player).TryGetValue(Items.Consumable.HealthPotionName, out int pots) && pots > 0
                && Server.ConsumableCooldownRemaining(Player, Items.Consumable.ByName(Items.Consumable.HealthPotionName)) <= 0f)
                idle.ConsumableSlot = (byte)(1 + Items.Consumable.All.ToList().FindIndex(c => c.Name == Items.Consumable.HealthPotionName));

            switch (Tracker.Step)
            {
                case TutorialStep.Move:
                case TutorialStep.OpenDoor: return OpenDoor(me, idle);
                case TutorialStep.KillMobs:
                    {
                        var room = Route.PackRooms.Where(r => !Server.State.ClearedPacks.Contains(LayoutIndex.MobPackIdBase + r)).OrderBy(r => Vec2.Distance(Vec2.Of(Layout.Rooms[r].Center), me.Position)).Cast<int?>().FirstOrDefault();
                        var fight = Fight(me, idle, false);
                        if (fight.HasValue) return fight.Value;
                        if (room.HasValue && RoomOf(me) != room.Value) { Doing = "to pack room " + room; return Walk(me, TutorialScript.Inside(Layout, room.Value), idle, BeforeBoss); }
                        // In the room and nothing in sight: hunt the straggler (a caster that backed into a corridor).
                        var straggler = Sim.Combatants.Values.Where(o => o.IsMob && !o.Dead && !Server.Director.Bosses.Any(b => b.Boss.Id == o.Id) && o.Id != EliteRules.CombatantId
                                                                       && !InRoom(o.Position, Route.BossRoom) && !InRoom(o.Position, Route.FinalBossRoom))
                            .OrderBy(o => Vec2.Distance(o.Position, me.Position)).FirstOrDefault();
                        if (straggler != null) { Doing = "hunting " + straggler.Name; return Walk(me, straggler.Position.Cell, idle, BeforeBoss); }
                        if (room.HasValue) { Doing = "to pack room " + room; return Walk(me, TutorialScript.Inside(Layout, room.Value), idle, BeforeBoss); }
                        return idle;
                    }
                case TutorialStep.LootChest:
                    {
                        var fight = Fight(me, idle, false);
                        if (fight.HasValue) return fight.Value;
                        if (RoomOf(me) != Route.ChestRoom || Vec2.Distance(Vec2.Of(TutorialScript.Inside(Layout, Route.ChestRoom)), me.Position) > 1.2f) { Doing = "to chest room " + Route.ChestRoom; return Walk(me, TutorialScript.Inside(Layout, Route.ChestRoom), idle, BeforeBoss); }
                        Doing = "looting"; idle.Interact = true; return idle;
                    }
                case TutorialStep.ArmUltimate:
                    {
                        var fight = Fight(me, idle, false);
                        if (fight.HasValue) return fight.Value;
                        var spot = Server.AltarSpot(Route.ShrineRoom);
                        if (!ShrineRules.InReach(Layout, Route.ShrineRoom, me.Position.Cell) || Vec2.Distance(spot, me.Position) > 0.6f) { Doing = "to the altar"; return Walk(me, spot.Cell, idle, BeforeBoss); }
                        _prayRetry -= dt;
                        if (Server.ChannelOf(Player) == MatchServer.ShrineChannel) { Doing = "praying"; return idle; }
                        if (_prayRetry <= 0f) { _prayRetry = 0.5f; idle.ShrineAction = (byte)ShrineAction.ArmUltimate; Doing = "pray"; }
                        return idle;
                    }
                case TutorialStep.KillBoss:
                    {
                        var fight = Fight(me, idle, true);
                        if (fight.HasValue) return fight.Value;
                        Doing = "to the boss"; return Walk(me, TutorialScript.Inside(Layout, Route.BossRoom), idle, FinalOnly);
                    }
                case TutorialStep.Extract:
                    {
                        var fight = Fight(me, idle, false);
                        if (fight.HasValue) return fight.Value;
                        if (RoomOf(me) != Route.ExtractRoom || Vec2.Distance(Vec2.Of(TutorialScript.Inside(Layout, Route.ExtractRoom)), me.Position) > 1.5f) { Doing = "to the extract"; return Walk(me, TutorialScript.Inside(Layout, Route.ExtractRoom), idle, FinalOnly); }
                        if (!Server.State.ExtractsOpen) { Doing = "waiting for the extract"; return idle; }
                        Doing = "extracting"; idle.Interact = true; return idle;
                    }
                default: Doing = Tracker.Step.ToString(); return idle;
            }
        }

        private IEnumerable<int> BeforeBoss => new[] { Route.BossRoom, Route.FinalBossRoom };
        private IEnumerable<int> FinalOnly => new[] { Route.FinalBossRoom };

        private int RoomOf(Combatant c) => Layout.InBounds(c.Position.Cell) ? Layout.RoomIdAt[c.Position.Cell.X, c.Position.Cell.Y] : -1;

        /// <summary>Walk to the inside cell beside the nearest shut spawn door and hold Interact there.</summary>
        private PlayerIntent OpenDoor(Combatant me, PlayerIntent idle)
        {
            var shut = Route.SpawnDoors.Where(d => Server.State.Index.Doors.TryGetValue(d, out var dd) && Server.State.ClosedDoors.Contains(dd.cell)).ToList();
            if (shut.Count == 0) { Doing = "no shut door"; return Walk(me, TutorialScript.Inside(Layout, Route.PackRooms[0]), idle, BeforeBoss); }
            var door = Server.State.Index.Doors[shut.OrderBy(d => Vec2.Distance(Vec2.Of(Server.State.Index.Doors[d].cell), me.Position)).First()].cell;
            var stand = GridPoint.Cardinals.Select(d => door + d).Where(p => Layout.InBounds(p) && Layout.RoomIdAt[p.X, p.Y] == Route.SpawnRoom && Sim.Walkable(Vec2.Of(p)))
                .OrderBy(p => p.ManhattanDistance(me.Position.Cell)).Cast<GridPoint?>().FirstOrDefault() ?? door;
            if (Vec2.Distance(Vec2.Of(stand), me.Position) > 0.12f) { Doing = "to the door"; return Walk(me, stand, idle, BeforeBoss); }
            Doing = "opening the door"; idle.Interact = true; idle.AimX = door.X; idle.AimY = door.Y; return idle;
        }

        /// <summary>Fight the nearest monster in sight within reach of the fight, or null when there is none. The boss lesson opens with the ultimate.</summary>
        private PlayerIntent? Fight(Combatant me, PlayerIntent idle, bool bossLesson)
        {
            var target = Sim.Combatants.Values.Where(o => o.IsMob && !o.Dead && Vec2.Distance(o.Position, me.Position) <= (bossLesson ? 14f : 9f) && Sim.CanSee(me.Position, o.Position))
                .Where(o => !InRoom(o.Position, Route.FinalBossRoom) && (bossLesson || !InRoom(o.Position, Route.BossRoom)))
                .OrderBy(o => Vec2.Distance(o.Position, me.Position)).FirstOrDefault();
            if (target == null) return null;
            var book = AbilityBook.For(Server.SpecOf(Player) ?? me.Spec).ToList();
            var basic = book.FirstOrDefault(a => a.Kind == AbilityKind.Basic);
            var bolt = book.FirstOrDefault(a => a.Kind == AbilityKind.Bolt);
            float dist = Vec2.Distance(target.Position, me.Position);
            float reach = Math.Max(1f, (basic?.RangeCells ?? 1.5f) * 0.85f);
            var i = idle;
            i.AimX = target.Position.X; i.AimY = target.Position.Y;
            // Read the floor, as the card teaches: out of a crush aimed at you, a slam around you, or an enemy zone under you.
            var winding = Sim.Combatants.Values.FirstOrDefault(o => o.IsMob && o.Channelling && (o.ChannelName == "crush:" + me.Id || (o.ChannelName == "groundslam" && Vec2.Distance(o.Position, me.Position) <= 3.5f)));
            var burning = Sim.Zones.FirstOrDefault(z => z.CasterTeam != me.Team && z.Ability.Damage > 0f && Vec2.Distance(z.Centre, me.Position) <= z.Ability.Size + 0.3f);
            var danger = winding != null ? winding.Position : burning?.Centre;
            if (danger.HasValue)
            {
                var away = me.Position - danger.Value;
                if (away.Length < 0.1f) away = new Vec2(1f, 0f);
                foreach (var dir in new[] { away.Normalized, new Vec2(-away.Y, away.X).Normalized, new Vec2(away.Y, -away.X).Normalized })
                    if (Sim.Walkable(me.Position + dir * 0.8f)) { i.MoveX = dir.X; i.MoveY = dir.Y; Doing = "dodging"; return i; }
            }
            bool armed = Server.State.UltimateCharges.TryGetValue(Player, out int ch) && ch > 0;
            if (bossLesson && armed && !Tracker.UltimateUsed && dist <= 6f) { i.Ultimate = true; Doing = "ultimate"; return i; }
            if (dist > reach)
            {
                Doing = "closing in";
                var to = (target.Position - me.Position).Normalized;
                if (Sim.CanSee(me.Position, target.Position) && Sim.Walkable(me.Position + to * 0.6f)) { i.MoveX = to.X; i.MoveY = to.Y; }
                else return Walk(me, target.Position.Cell, idle, bossLesson ? FinalOnly : BeforeBoss);
                if (bolt != null && bolt.Filter != TargetFilter.Allies && dist <= bolt.RangeCells) i.Bolt = true;
                return i;
            }
            Doing = "fighting " + target.Name;
            if (bolt != null && bolt.Filter != TargetFilter.Allies && (!me.CooldownUntil.TryGetValue(bolt.Id, out float until) || until <= Sim.Time)) i.Bolt = true;
            else i.Basic = true;
            return i;
        }

        private bool InRoom(Vec2 p, int room) => room >= 0 && Layout.InBounds(p.Cell) && Layout.RoomIdAt[p.Cell.X, p.Cell.Y] == room;

        /// <summary>Follow the grid to <paramref name="target"/>, never through the rooms in <paramref name="avoid"/> (unless the target is in one).</summary>
        private PlayerIntent Walk(Combatant me, GridPoint target, PlayerIntent idle, IEnumerable<int> avoid)
        {
            if (_path == null || _path.Count == 0 || _pathTo != target || Vec2.Distance(Vec2.Of(_path[0]), me.Position) > 2.5f)
            {
                int targetRoom = Layout.InBounds(target) ? Layout.RoomIdAt[target.X, target.Y] : -1;
                var blocked = TutorialScript.CellsOf(Layout, avoid.Where(r => r != targetRoom && r != RoomOf(me)));
                foreach (var d in Sim.ClosedDoors) blocked.Add(d);
                _path = GridPath.Find(Layout, me.Position.Cell, target, maxCost: 100000, closedDoors: blocked) ?? GridPath.Find(Layout, me.Position.Cell, target, maxCost: 100000, closedDoors: Sim.ClosedDoors);
                _pathTo = target;
            }
            if (_path == null || _path.Count == 0) return idle;
            while (_path.Count > 1 && (Vec2.Distance(Vec2.Of(_path[0]), me.Position) < 0.45f || _path[0] == me.Position.Cell)) _path.RemoveAt(0);
            var next = Vec2.Of(_path[0]);
            if (_path.Count == 1 && Vec2.Distance(next, me.Position) < 0.08f) return idle;
            if (Vec2.Distance(_lastPos, me.Position) < 0.01f) { _stuck += 0.05f; if (_stuck > 1.5f) { _stuck = 0f; _path = null; } } else _stuck = 0f;
            _lastPos = me.Position;
            var dir = (next - me.Position).Normalized;
            var i = idle;
            i.MoveX = dir.X; i.MoveY = dir.Y; i.AimX = next.X; i.AimY = next.Y;
            return i;
        }
    }
}
