using System;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Net;

namespace DungeonGen.Core.Combat
{
    /// <summary>What a mob archetype does differently, read off its name. The behaviour text in the roster is the design; this is the code.</summary>
    public sealed class MobTraits
    {
        public bool Ranged, HoldsDoorways, Swarm, IgnoresHazards, LootsCorpses, EatsCorpses, RisesOnce, Caster;
        public static MobTraits For(MobArchetype m)
        {
            switch (m.Name)
            {
                case "Bandit": return new MobTraits { LootsCorpses = true };
                case "Hollow Knight": return new MobTraits { HoldsDoorways = true };
                case "Skitterer": return new MobTraits { Swarm = true };
                case "Ember Wisp": return new MobTraits { IgnoresHazards = true };
                case "Storm Acolyte": return new MobTraits { Ranged = true, Caster = true };
                case "Carrion Crawler": return new MobTraits { EatsCorpses = true };
                case "Gravewalker": return new MobTraits { RisesOnce = true };
                default: return new MobTraits();
            }
        }
    }

    public enum MobState { Idle, Chase, Attack, HoldDoor, Return, Dead }

    /// <summary>One pack's shared mind: aggro is shared, the leash is the room, the pack resets together.</summary>
    public sealed class PackState
    {
        public int PackId; public int RoomId; public int? Target; public float LastSeenEnemyAt = -100f;
        public override string ToString() => $"pack {PackId} room {RoomId} target {Target}";
    }

    /// <summary>
    /// The mob state machine, by decision: leash to the room, attack any team, nearest target unless
    /// taunted, reset when the room empties, never respawn. A pack shares its target. Door holders
    /// stand in the doorway the players came through; casters keep their distance; swarms are fast;
    /// wisps float over hazards; Gravewalkers rise once unless the hit that killed them burned.
    /// Every action goes through the simulator's Cast and Move, so mobs obey the same physics as players.
    /// </summary>
    public sealed class MobController
    {
        public Combatant Mob { get; }
        public MobTraits Traits { get; }
        public PackState Pack { get; }
        public Vec2 Post { get; }
        public MobState State { get; private set; } = MobState.Idle;
        private readonly DungeonLayout _layout;
        private float _nextThink;

        public MobController(Combatant mob, MobArchetype arch, PackState pack, DungeonLayout layout)
        {
            Mob = mob; Traits = MobTraits.For(arch); Pack = pack; Post = mob.Position; _layout = layout;
            mob.HazardImmune = Traits.IgnoresHazards;
            if (Traits.Swarm) mob.MoveSpeedScale = 1.3f;
            if (Traits.RisesOnce) mob.Lives = 1;
        }

        private int RoomAt(Vec2 p) => _layout != null && _layout.InBounds(p.Cell) ? _layout.RoomIdAt[p.Cell.X, p.Cell.Y] : -1;
        private bool InLeash(Vec2 p) => _layout == null || GridPath.WithinLeash(_layout, Pack.RoomId, p.Cell, MobRules.LeashCells);

        public void Step(FightSim sim, float dt)
        {
            if (Mob.Dead) { State = MobState.Dead; return; }
            if (!Mob.CanAct) return;
            // Acquire: taunt first, then the pack's target, then the nearest living enemy inside the leash.
            var taunt = Mob.Buffs.FirstOrDefault(b => b.Kind == BuffKind.Taunt);
            Combatant target = taunt != null ? sim.Get(taunt.Source) : null;
            if (target != null && !target.Alive) target = null;
            if (target == null && Pack.Target.HasValue) { target = sim.Get(Pack.Target.Value); if (target == null || !target.Alive || !InLeash(target.Position)) { target = null; Pack.Target = null; } }
            if (target == null)
            {
                target = sim.Combatants.Values.Where(o => o.Team != Mob.Team && o.Alive && !o.Has(BuffKind.Invisible) && InLeash(o.Position) && sim.CanSee(Mob.Position, o.Position)).OrderBy(o => Vec2.Distance(o.Position, Mob.Position)).FirstOrDefault();
                if (target != null) { Pack.Target = target.Id; Pack.LastSeenEnemyAt = sim.Time; }
            }
            if (target == null)
            {
                // Nobody here: walk back to the post and, once there, heal to full (the room reset).
                if (Vec2.Distance(Mob.Position, Post) > 0.6f) { State = MobState.Return; sim.Move(Mob.Id, Post, dt); }
                else
                {
                    State = MobState.Idle;
                    if (MobRules.ResetWhenRoomEmpty && sim.Time - Pack.LastSeenEnemyAt > 3f && Mob.Health < Mob.MaxHealth) { Mob.Health = Mob.MaxHealth; Mob.Status.Cleanse(); sim.Emit(FightEventKind.Phase, Mob.Id, -1, 0f, "reset"); }
                }
                return;
            }
            Pack.LastSeenEnemyAt = sim.Time;
            float dist = Vec2.Distance(Mob.Position, target.Position);
            var basic = AbilityBook.Get(Traits.Ranged ? "Mob.ranged" : "Mob.basic");
            if (Traits.HoldsDoorways && _layout != null)
            {
                // Stand in the door between the enemy and the room; swing at whoever is in reach.
                var room = _layout.Rooms[Pack.RoomId];
                var door = room.Doors.OrderBy(d => Vec2.Distance(Vec2.Of(d), target.Position)).FirstOrDefault();
                if (room.Doors.Count > 0 && RoomAt(target.Position) != Pack.RoomId)
                {
                    State = MobState.HoldDoor;
                    var doorPos = Vec2.Of(door);
                    if (Vec2.Distance(Mob.Position, doorPos) > 0.4f) sim.Move(Mob.Id, doorPos, dt);
                    if (dist <= basic.RangeCells + 0.5f && sim.CanSee(Mob.Position, target.Position)) sim.Cast(Mob.Id, basic.Id, target.Position);
                    return;
                }
            }
            if (Traits.Ranged)
            {
                State = MobState.Attack;
                if (dist < 3f) sim.Move(Mob.Id, Mob.Position + (Mob.Position - target.Position).Normalized * 2f, dt);          // keep the back of the room
                else if (dist > basic.RangeCells || !sim.CanSee(Mob.Position, target.Position)) sim.Move(Mob.Id, target.Position, dt);
                if (sim.CanSee(Mob.Position, target.Position) && dist <= basic.RangeCells) sim.Cast(Mob.Id, basic.Id, target.Position);
                return;
            }
            if (dist <= basic.RangeCells + 0.3f && sim.CanSee(Mob.Position, target.Position)) { State = MobState.Attack; sim.Cast(Mob.Id, basic.Id, target.Position); return; }
            State = MobState.Chase;
            var next = target.Position;
            if (!InLeash(Mob.Position + (next - Mob.Position).Normalized * 0.5f)) { State = MobState.Return; sim.Move(Mob.Id, Post, dt); return; }   // the leash holds
            sim.Move(Mob.Id, next, dt);
        }
    }

    /// <summary>
    /// The boss brains. Target selection by archetype (taunt first, always), the slam when crowded, the
    /// archetype's mechanics on timers and health thresholds, enrage after the budget's time, and the
    /// affixes that change numbers. Each mechanic is a few lines on top of the simulator so the
    /// design sheet's words are the code's behaviour, in a form the fight lab can run.
    /// </summary>
    public sealed class BossController
    {
        public Combatant Boss { get; }
        public BossArchetype Archetype { get; }
        public BossInstance Instance { get; }
        public int RoomId { get; }
        public int Phase { get; private set; } = 1;
        public bool Enraged { get; private set; }
        private readonly DungeonLayout _layout;
        private readonly Vec2 _home;
        private float _nextSlam = 6f, _nextMechanic = 8f, _nextSecondary = 12f, _chaseUntil = -1f, _started = -1f, _enrageAt;
        private readonly List<int> _adds = new List<int>();
        private readonly List<GridPoint> _tempWalls = new List<GridPoint>();
        private float _wallsUntil = -1f;
        private int _nextAddId;
        public Func<int, int> InventoryValueOf { get; set; } = _ => 0;
        public IReadOnlyList<int> Adds => _adds;

        public BossController(FightSim sim, BossInstance instance, int roomId, Vec2 pos, DungeonLayout layout, int team = 1, int id = 99)
        {
            Instance = instance; Archetype = instance.Archetype; RoomId = roomId; _layout = layout; _home = pos;
            bool scaled = instance.Budget != null;   // the generator's BossRoller already folded archetype and affix multipliers into the budget
            var budget = instance.Budget ?? EncounterTuner.RoomBoss(new CombatBaseline());
            float health = budget.Health * (scaled ? 1f : Archetype.HealthMultiplier), dps = budget.Dps * (scaled ? 1f : Archetype.DpsMultiplier);
            float enrage = budget.EnrageSeconds;
            var res = new Resistances();   // bosses have no resistances of their own: the budget is the whole of their toughness, as the tuner assumes
            Boss = sim.Add(Combatant.Boss(id, team, instance.DisplayName, new EncounterBudget(budget.Kind, budget.PlayerEquivalents, budget.GearMultiplier, health, dps, budget.TargetSeconds, enrage), Archetype.Damage, res, pos, Archetype.OnHit.Kind));
            foreach (var a in instance.Affixes)
            {
                switch (a.Name)   // numbers (health, dps, enrage) are already in the budget; only behaviours are added here
                {
                    case "Swift": Boss.MoveSpeedScale *= 1.25f; Boss.AttackSpeedScale *= 1.2f; break;
                    case "Venomous": _venomous = true; break;
                    case "Thorned": _thorned = true; break;
                }
            }
            _enrageAt = enrage;
            if (Archetype.Name == "Lantern Wraith") Boss.PassesDoors = true;
            if (Archetype.Name == "Ashen Colossus") Boss.HazardImmune = true;
            _nextAddId = id * 100 + 1;
        }
        private bool _venomous, _thorned;

        public float HealthFraction => Boss.Health / Boss.MaxHealth;

        private Combatant PickTarget(FightSim sim)
        {
            var enemies = sim.Combatants.Values.Where(o => o.Team != Boss.Team && !o.Dead && !o.IsMob).ToList();
            var alive = enemies.Where(o => o.Alive && !o.Has(BuffKind.Invisible)).ToList();
            if (alive.Count == 0) return enemies.FirstOrDefault(o => o.Downed);
            var taunt = Boss.Buffs.FirstOrDefault(b => b.Kind == BuffKind.Taunt);
            if (taunt != null && sim.Get(taunt.Source) is Combatant t && t.Alive) return t;
            switch (Archetype.Name)
            {
                case "Hound of Vesh": return alive.OrderBy(o => o.Health / o.MaxHealth).First();                                     // Scent
                case "Lantern Wraith": return alive.OrderByDescending(o => InventoryValueOf(o.Id)).ThenBy(o => Vec2.Distance(o.Position, Boss.Position)).First();   // Covet
                default:
                    var threat = Boss.Threat.Where(kv => sim.Time - kv.Value.at < 10f && alive.Any(o => o.Id == kv.Key)).OrderByDescending(kv => kv.Value.damage).FirstOrDefault();
                    return threat.Key != 0 && sim.Get(threat.Key) is Combatant top && top.Alive ? top : alive.OrderBy(o => Vec2.Distance(o.Position, Boss.Position)).First();
            }
        }

        private Combatant SpawnAdd(FightSim sim, string mobName, Vec2 at, CombatBaseline b)
        {
            var arch = MobArchetype.DefaultRoster.First(m => m.Name == mobName);
            var add = sim.Add(Combatant.Mob(_nextAddId++, Boss.Team, arch, 1f, b, at));
            _adds.Add(add.Id);
            sim.Emit(FightEventKind.Spawned, Boss.Id, add.Id, 0f, mobName);
            return add;
        }

        private void Lava(FightSim sim, Vec2 centre, float radius, float seconds)
        {
            sim.Zones.Add(new Zone { Ability = AbilityBook.Get("Pyromancy.zone"), Caster = Boss.Id, CasterTeam = Boss.Team, Centre = centre, Until = sim.Time + seconds, NextTick = sim.Time });
        }

        public void Step(FightSim sim, float dt, CombatBaseline baseline = null)
        {
            baseline = baseline ?? new CombatBaseline();
            if (Boss.Dead) return;
            var target = PickTarget(sim);
            if (target == null) { if (Vec2.Distance(Boss.Position, _home) > 0.6f) sim.Move(Boss.Id, _home, dt); return; }
            if (_started < 0f) _started = sim.Time;
            if (!Enraged && sim.Time - _started >= _enrageAt) { Enraged = true; Boss.DamageScale *= 2f; sim.Emit(FightEventKind.Phase, Boss.Id, -1, 0f, "enrage"); }
            int phase = HealthFraction > 0.7f ? 1 : HealthFraction > 0.35f ? 2 : 3;
            if (phase != Phase) { Phase = phase; sim.Emit(FightEventKind.Phase, Boss.Id, -1, phase, "phase"); OnPhase(sim, phase, baseline); }
            if (_wallsUntil > 0f && sim.Time >= _wallsUntil) { foreach (var w in _tempWalls) _layout.Tiles[w.X, w.Y] = TileType.RoomFloor; _tempWalls.Clear(); _wallsUntil = -1f; }
            // A channel that just finished lands now.
            if (Boss.CompletedChannel != null) { Resolve(sim, Boss.CompletedChannel, target, baseline); Boss.CompletedChannel = null; }
            if (Boss.Channelling || !Boss.CanAct) return;
            float dist = Vec2.Distance(Boss.Position, target.Position);
            // Mechanics on timers.
            if (sim.Time >= _nextMechanic) { _nextMechanic = sim.Time + MechanicPeriod(); Mechanic(sim, target, baseline); if (Boss.Channelling) return; }
            if (sim.Time >= _nextSecondary) { _nextSecondary = sim.Time + 10f; Secondary(sim, target, baseline); }
            // The slam when two or more stand close, else the swing.
            int crowd = sim.Combatants.Values.Count(o => o.Team != Boss.Team && o.Alive && Vec2.Distance(o.Position, Boss.Position) <= 2f);
            if (sim.Time >= _nextSlam && crowd >= 2 && sim.Cast(Boss.Id, "Boss.slam", Boss.Position).Ok) { _nextSlam = sim.Time + 8f; if (Archetype.Name == "Ashen Colossus") Lava(sim, Boss.Position, 2f, 600f); return; }
            if (dist <= AbilityBook.Get("Boss.basic").RangeCells + 0.3f && sim.CanSee(Boss.Position, target.Position))
            {
                var r = sim.Cast(Boss.Id, "Boss.basic", target.Position);
                if (r.Ok && _venomous) foreach (int tid in r.Targets) sim.Get(tid)?.Status.Apply(StatusEffectDefinition.All[StatusKind.Poisoned], sim.Get(tid).Resistances);
                return;
            }
            // Chasers leave the room; everyone else leashes to it.
            bool chaser = Archetype.Family == BossFamily.Chaser;
            bool inLeash = _layout == null || GridPath.WithinLeash(_layout, RoomId, Boss.Position.Cell, chaser ? 40 : MobRules.LeashCells);
            if (chaser && !GridPath.WithinLeash(_layout, RoomId, Boss.Position.Cell, MobRules.LeashCells))
            {
                if (_chaseUntil < 0f) _chaseUntil = sim.Time + 8f;
                if (sim.Time > _chaseUntil) { sim.Move(Boss.Id, _home, dt); if (Vec2.Distance(Boss.Position, _home) < 1f) _chaseUntil = -1f; return; }
            }
            if (inLeash) sim.Move(Boss.Id, target.Position, dt); else sim.Move(Boss.Id, _home, dt);
        }

        private float MechanicPeriod()
        {
            switch (Archetype.Name)
            {
                case "Hive Matron": return Phase >= 2 ? 6f : 8f;
                case "Iron Juggernaut": return 6f;
                case "Bone Choir": return 15f;
                default: return 12f;
            }
        }

        private void Mechanic(FightSim sim, Combatant target, CombatBaseline b)
        {
            switch (Archetype.Name)
            {
                case "Ashen Colossus":   // Ground Slam: telegraphed cone that leaves lava
                    Boss.StartChannel("groundslam", 1.5f, sim.Time); break;
                case "Glasswright":      // Partition: a crystal wall across the room for 10 s
                    if (_layout != null && _tempWalls.Count == 0)
                    {
                        var room = _layout.Rooms[RoomId];
                        int x = room.Center.X;
                        for (int y = room.Bounds.Y + 1; y < room.Bounds.YMax - 1; y++) if (_layout.Tiles[x, y] == TileType.RoomFloor && !sim.Combatants.Values.Any(c => c.Position.Cell.Equals(new GridPoint(x, y)))) { _layout.Tiles[x, y] = TileType.Pillar; _tempWalls.Add(new GridPoint(x, y)); }
                        _wallsUntil = sim.Time + 10f;
                        sim.Emit(FightEventKind.Phase, Boss.Id, -1, _tempWalls.Count, "partition");
                    }
                    break;
                case "Hive Matron":      // Brood: two skitterers
                    for (int i = 0; i < 2; i++) SpawnAdd(sim, "Skitterer", Boss.Position + new Vec2(i == 0 ? 1f : -1f, 0.5f), b);
                    break;
                case "Bone Choir":       // Choir: every corpse in the room rises as a Gravewalker
                    foreach (var corpse in sim.Combatants.Values.Where(c => c.Dead && !_adds.Contains(c.Id) && (_layout == null || _layout.RoomIdAt[c.Position.Cell.X, c.Position.Cell.Y] == RoomId) && !c.LastHitWasFire).ToList())
                    { SpawnAdd(sim, "Gravewalker", corpse.Position, b); corpse.LastHitWasFire = true; }   // marked so it is not raised twice
                    break;
                case "Hound of Vesh":    // Howl: slows everything near it
                    foreach (var o in sim.Combatants.Values.Where(o => o.Team != Boss.Team && o.Alive && Vec2.Distance(o.Position, Boss.Position) <= 3f)) o.Status.Apply(StatusEffectDefinition.All[StatusKind.Chill], o.Resistances);
                    break;
                case "Lantern Wraith":   // Snuff: a shadow pulse that blinds (silences) for 2 s
                    foreach (var o in sim.Combatants.Values.Where(o => o.Team != Boss.Team && o.Alive && Vec2.Distance(o.Position, Boss.Position) <= 4f)) o.Buffs.Add(new Buff { Kind = BuffKind.Silenced, Magnitude = 1f, Until = sim.Time + 2f, Source = Boss.Id });
                    break;
                case "Iron Juggernaut":  // Crush: 3 s telegraph, then 60% of a baseline player's health to one target
                    Boss.StartChannel("crush:" + target.Id, 3f, sim.Time); break;
                case "Plague Herald":    // Miasma: a poison cloud under the target
                    sim.Zones.Add(new Zone { Ability = AbilityBook.Get("Plaguebearer.zone"), Caster = Boss.Id, CasterTeam = Boss.Team, Centre = target.Position, Until = sim.Time + 6f, NextTick = sim.Time });
                    break;
                case "The Sovereign Below":
                    if (Phase == 1) Boss.StartChannel("crush:" + target.Id, 3f, sim.Time);
                    else if (Phase == 2) { SpawnAdd(sim, "Gravewalker", Boss.Position + new Vec2(1.5f, 0f), b); if (_layout != null) { var room = _layout.Rooms[RoomId]; Lava(sim, Vec2.Of(new GridPoint(room.Bounds.X + 1, room.Bounds.Y + 1)), 1.5f, 600f); Lava(sim, Vec2.Of(new GridPoint(room.Bounds.XMax - 2, room.Bounds.YMax - 2)), 1.5f, 600f); } }
                    else sim.Zones.Add(new Zone { Ability = AbilityBook.Get("Plaguebearer.zone"), Caster = Boss.Id, CasterTeam = Boss.Team, Centre = target.Position, Until = sim.Time + 6f, NextTick = sim.Time });
                    break;
            }
        }

        private void Secondary(FightSim sim, Combatant target, CombatBaseline b)
        {
            switch (Archetype.Name)
            {
                case "Glasswright":      // Shardstorm: everything in sight is hit
                    foreach (var o in sim.Combatants.Values.Where(o => o.Team != Boss.Team && o.Alive && sim.CanSee(Boss.Position, o.Position)).ToList()) sim.Strike(Boss, o, AbilityBook.Get("Boss.slam"));
                    break;
                case "Hive Matron":      // Pheromone: mark a player; the brood follows the mark
                    target.Buffs.Add(new Buff { Kind = BuffKind.Marked, Magnitude = 1f, Until = sim.Time + 8f, Source = Boss.Id }); break;
                case "Bone Choir":       // Dirge: a channel that heals the boss per add alive; interruptible
                    Boss.StartChannel("dirge", 4f, sim.Time); break;
                case "Iron Juggernaut":  // Trample: charge the furthest player; a wall stops and stuns it
                    {
                        var far = sim.Combatants.Values.Where(o => o.Team != Boss.Team && o.Alive).OrderByDescending(o => Vec2.Distance(o.Position, Boss.Position)).FirstOrDefault();
                        if (far == null) break;
                        var before = Boss.Position;
                        var r = sim.Cast(Boss.Id, "Boss.slam", Boss.Position);   // consumes nothing: the trample is a dash
                        var dir = (far.Position - Boss.Position).Normalized;
                        float moved = 0f;
                        for (float d = 0.25f; d <= 10f; d += 0.25f) { var n = Boss.Position + dir * 0.25f; if (!sim.Walkable(n, Boss)) { Boss.Status.Apply(StatusEffectDefinition.All[StatusKind.Stun]); sim.Emit(FightEventKind.Phase, Boss.Id, -1, 0f, "trample:wall"); break; } Boss.Position = n; moved += 0.25f; if (Vec2.Distance(Boss.Position, far.Position) < 1f) break; }
                        sim.Emit(FightEventKind.Dashed, Boss.Id, far.Id, moved, "Trample");
                        if (HealthFraction <= 0.5f && !_overdriven) { _overdriven = true; Boss.StartChannel("overdrive", 3f, sim.Time); }
                        break;
                    }
            }
        }
        private bool _overdriven;

        private void Resolve(FightSim sim, string channel, Combatant target, CombatBaseline b)
        {
            if (channel.StartsWith("crush:"))
            {
                var t = sim.Get(int.Parse(channel.Substring(6)));
                if (t != null && t.Alive && Vec2.Distance(t.Position, Boss.Position) <= 3f)
                {
                    float wanted = 0.6f * b.PlayerHealth;
                    var crush = new AbilityDefinition("Boss.crush", "Crush", "Boss", AbilityKind.Bolt, AbilityShape.Target, TargetFilter.Everyone, 3f, 2f, wanted / Math.Max(1f, Boss.Dps * Boss.DamageScale), 0f, "Crush");
                    sim.Strike(Boss, t, crush);
                }
            }
            else if (channel == "groundslam")
            {
                foreach (var o in sim.Combatants.Values.Where(o => o.Team != Boss.Team && o.Alive && Vec2.Distance(o.Position, Boss.Position) <= 3f && sim.CanSee(Boss.Position, o.Position)).ToList()) sim.Strike(Boss, o, AbilityBook.Get("Boss.slam"));
                Lava(sim, Boss.Position + Boss.Facing * 1.5f, 1.5f, 600f);
            }
            else if (channel == "dirge")
            {
                int alive = _adds.Count(id => sim.Get(id) is Combatant a && !a.Dead);
                Boss.Health = Math.Min(Boss.MaxHealth, Boss.Health + Boss.MaxHealth * 0.05f * alive);
                sim.Emit(FightEventKind.Heal, Boss.Id, Boss.Id, Boss.MaxHealth * 0.05f * alive, "Dirge");
            }
            else if (channel == "overdrive") { Boss.AttackSpeedScale *= 2f; _overdriveUntil = sim.Time + 10f; }
        }
        private float _overdriveUntil = -1f;

        private void OnPhase(FightSim sim, int phase, CombatBaseline b)
        {
            switch (Archetype.Name)
            {
                case "Ashen Colossus" when phase == 3 && _layout != null:   // Collapse: the outer ring becomes lava
                    { var room = _layout.Rooms[RoomId]; var bnd = room.Bounds; for (int x = bnd.X; x < bnd.XMax; x += 3) { Lava(sim, Vec2.Of(new GridPoint(x, bnd.Y)), 1.2f, 600f); Lava(sim, Vec2.Of(new GridPoint(x, bnd.YMax - 1)), 1.2f, 600f); } for (int y = bnd.Y; y < bnd.YMax; y += 3) { Lava(sim, Vec2.Of(new GridPoint(bnd.X, y)), 1.2f, 600f); Lava(sim, Vec2.Of(new GridPoint(bnd.XMax - 1, y)), 1.2f, 600f); } break; }
                case "Hive Matron" when phase == 2:                          // Molt: sheds the brood, gains speed
                    foreach (int id in _adds) if (sim.Get(id) is Combatant a && !a.Dead) { a.Dead = true; sim.Emit(FightEventKind.Died, Boss.Id, a.Id, 0f, "molt"); }
                    Boss.MoveSpeedScale *= 1.4f; break;
                case "Bone Choir" when phase == 3:                           // Requiem: everything rises at once
                    Mechanic(sim, null, b); break;
                case "Plague Herald" when phase == 2:                        // Outbreak: every stack spreads to the nearest ally
                    foreach (var o in sim.Combatants.Values.Where(o => o.Team != Boss.Team && o.Alive && o.Status.Has(StatusKind.Poisoned)).ToList())
                    { var ally = sim.Combatants.Values.Where(x => x.Team == o.Team && x.Alive && x.Id != o.Id).OrderBy(x => Vec2.Distance(x.Position, o.Position)).FirstOrDefault(); if (ally != null) { var p = ally.Status.Apply(StatusEffectDefinition.All[StatusKind.Poisoned], ally.Resistances); if (p != null) p.Stacks = Math.Max(p.Stacks, o.Status.Get(StatusKind.Poisoned).Stacks); } }
                    break;
            }
        }

        /// <summary>Ember Shell: the Colossus takes half damage standing in its own lava; Thorned reflects melee. Called by the director each tick.</summary>
        public void Passives(FightSim sim)
        {
            if (Archetype.Name == "Ashen Colossus")
            {
                bool inLava = sim.Zones.Any(z => z.Caster == Boss.Id && Vec2.Distance(z.Centre, Boss.Position) <= z.Ability.Size);
                Boss.Buffs.RemoveAll(bf => bf.Kind == BuffKind.DamageReduction && bf.Source == Boss.Id);
                if (inLava) Boss.Buffs.Add(new Buff { Kind = BuffKind.DamageReduction, Magnitude = 0.5f, Until = sim.Time + 0.2f, Source = Boss.Id });
            }
            if (_overdriveUntil > 0f && sim.Time >= _overdriveUntil) { Boss.AttackSpeedScale /= 2f; _overdriveUntil = -1f; }
        }
        public bool Thorned => _thorned;
    }

    /// <summary>
    /// Runs a room's encounter: stations the pack and the boss from the layout, steps every brain each
    /// tick, keeps brood adds following the Matron's mark, and hands the simulator's deaths to the match
    /// state through the bridge as MobPackCleared and BossKilled.
    /// </summary>
    public sealed class EncounterDirector
    {
        public FightSim Sim { get; }
        public DungeonLayout Layout { get; }
        public List<MobController> Mobs { get; } = new List<MobController>();
        /// <summary>Every stationed boss, in stationing order. A dungeon has several boss rooms and the final boss.</summary>
        public List<BossController> Bosses { get; } = new List<BossController>();
        /// <summary>The most recently stationed boss (the single-boss API the fight lab and older tests use).</summary>
        public BossController Boss => Bosses.Count == 0 ? null : Bosses[Bosses.Count - 1];
        private readonly Dictionary<int, int> _bossRoomOf = new Dictionary<int, int>();
        /// <summary>Boss combatant ids count down from 99 (91..99), so each boss's adds (id × 100 + n) get their own block above 9100 and never meet mob (500+) or bot (900+) ids.</summary>
        public const int FirstBossId = 99, MaxBosses = 9;
        public CombatBaseline Baseline { get; }
        private readonly Dictionary<int, int> _packOf = new Dictionary<int, int>();
        private int _nextId = 500;
        public const int MobTeam = 100;

        public EncounterDirector(FightSim sim, DungeonLayout layout, CombatBaseline baseline = null) { Sim = sim; Layout = layout; Baseline = baseline ?? new CombatBaseline(); }

        /// <summary>Station a room's pack around its centre, in a ring, on walkable floor.</summary>
        public PackState StationPack(Room room)
        {
            if (room.Mobs == null) return null;
            var pack = new PackState { PackId = LayoutIndex.IdOfMobPack(room), RoomId = room.Id };
            int n = room.Mobs.Count;
            for (int i = 0; i < n; i++)
            {
                double a = Math.PI * 2 * i / n;
                var p = Vec2.Of(room.Center) + new Vec2((float)Math.Cos(a), (float)Math.Sin(a)) * Math.Min(2.5f, room.Bounds.Width / 3f);
                if (!Sim.Walkable(p)) p = Vec2.Of(room.Center);
                var mob = Sim.Add(Combatant.Mob(_nextId++, MobTeam, room.Mobs.Mob, room.Mobs.TierMultiplier, Baseline, p));
                Mobs.Add(new MobController(mob, room.Mobs.Mob, pack, Layout));
                _packOf[mob.Id] = pack.PackId;
            }
            return pack;
        }

        public BossController StationBoss(Room room)
        {
            if (room.Boss == null) return null;
            if (Bosses.Count >= MaxBosses) throw new InvalidOperationException($"At most {MaxBosses} bosses per encounter director.");
            int id = FirstBossId - Bosses.Count;
            var boss = new BossController(Sim, room.Boss, room.Id, Vec2.Of(room.Center), Layout, MobTeam, id);
            Bosses.Add(boss);
            _bossRoomOf[boss.Boss.Id] = room.Id;
            return boss;
        }

        public void Step(float dt)
        {
            foreach (var m in Mobs) m.Step(Sim, dt);
            foreach (var boss in Bosses)
            {
                boss.Passives(Sim);
                boss.Step(Sim, dt, Baseline);
                // Brood and risen adds fight for their boss: they chase its mark if there is one, else the nearest.
                foreach (int id in boss.Adds.ToList())
                {
                    var add = Sim.Get(id);
                    if (add == null || add.Dead || !add.CanAct) continue;
                    var marked = Sim.Combatants.Values.FirstOrDefault(o => o.Team != add.Team && o.Alive && o.Buffs.Any(b => b.Kind == BuffKind.Marked && b.Source == boss.Boss.Id));
                    var target = marked ?? Sim.Combatants.Values.Where(o => o.Team != add.Team && o.Alive && !o.IsMob).OrderBy(o => Vec2.Distance(o.Position, add.Position)).FirstOrDefault();
                    if (target == null) continue;
                    var basic = AbilityBook.Get("Mob.basic");
                    if (Vec2.Distance(add.Position, target.Position) <= basic.RangeCells + 0.3f) Sim.Cast(add.Id, basic.Id, target.Position); else Sim.Move(add.Id, target.Position, dt);
                }
            }
            Sim.Tick(dt);
        }

        /// <summary>The match events this tick's deaths imply, through the bridge.</summary>
        public IEnumerable<MatchEvent> Drain(int from, Func<int, int> roomOfPlayer)
        {
            for (int i = from; i < Sim.Events.Count; i++)
                foreach (var e in FightBridge.ToMatchEvents(Sim.Events[i], Sim, roomOfPlayer, id => _bossRoomOf.TryGetValue(id, out int r) ? r : (int?)null, id => _packOf.TryGetValue(id, out int p) ? p : (int?)null))
                    yield return e;
        }
    }
}
