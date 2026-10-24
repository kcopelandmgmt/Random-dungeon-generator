using System;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Net;

namespace DungeonGen.Core.Combat
{
    /// <summary>A temporary state on a combatant: damage reduction, a reflect stance, a root, a mark.</summary>
    public sealed class Buff
    {
        public BuffKind Kind; public float Magnitude; public float Until; public int Source;
        public override string ToString() => $"{Kind} {Magnitude:0.##} until {Until:0.0}";
    }

    /// <summary>
    /// A player, mob or boss as the fight simulator sees it. Players carry a <see cref="PlayerStats"/>
    /// sheet; mobs and bosses carry the budget numbers the encounter tuner gave them. Health never
    /// regenerates; the resource does. Positions are in cells (2 m).
    /// </summary>
    public sealed class Combatant
    {
        public int Id { get; }
        public int Team { get; }
        public bool IsMob { get; }
        public string Name { get; }
        public string Spec { get; }
        public ClassArchetype Class { get; }
        public PlayerStats Stats { get; }
        public Vec2 Position { get; set; }
        public Vec2 Facing { get; set; } = new Vec2(0f, 1f);
        public float MaxHealth { get; }
        public float Health { get; set; }
        public float Dps { get; }
        public float AttackSpeed { get; }
        public float CritChance { get; }
        public float ArmourRating { get; }
        public Resistances Resistances { get; }
        public DamageProfile AttackProfile { get; }
        public StatusEffectDefinition OnHit { get; }
        public float Lifesteal { get; }
        public float Thorns { get; }
        public DamageType ThornsType { get; }
        public float MoveSpeed { get; }
        public float RegenMultiplier { get; }
        public ResourceKind Resource { get; }
        public float ResourceMax { get; }
        public float ResourceNow { get; set; }
        public StatusTracker Status { get; } = new StatusTracker();
        public Dictionary<string, float> CooldownUntil { get; } = new Dictionary<string, float>();
        public List<Buff> Buffs { get; } = new List<Buff>();
        public bool Downed { get; set; }
        public float DownedAt { get; set; }
        public float FinishDamage { get; set; }
        public bool Dead { get; set; }
        public bool UltimateArmed { get; set; }
        public float NextBasicAt { get; set; }
        /// <summary>The raw size of the hit each damage-over-time status ticks from.</summary>
        public Dictionary<StatusKind, float> DotBase { get; } = new Dictionary<StatusKind, float>();
        public int MomentumStacks { get; set; }
        /// <summary>Multiplier on everything this combatant deals: enrage, Overdrive, affixes.</summary>
        public float DamageScale { get; set; } = 1f;
        public float AttackSpeedScale { get; set; } = 1f;
        public float MoveSpeedScale { get; set; } = 1f;
        /// <summary>Floats or burns: traps, vents, pits and lava do nothing to it.</summary>
        public bool HazardImmune { get; set; }
        /// <summary>Passes closed doors and bodies (the Lantern Wraith's Drift).</summary>
        public bool PassesDoors { get; set; }
        /// <summary>A cast with a wind-up. Stagger or Stun cancels it; the brain reads ChannelName when it completes.</summary>
        public string ChannelName { get; set; }
        public float ChannelUntil { get; set; } = -1f;
        public bool Channelling => ChannelName != null && ChannelUntil > 0f;
        /// <summary>attacker id → (damage dealt to me, when). Threat for boss targeting.</summary>
        public Dictionary<int, (float damage, float at)> Threat { get; } = new Dictionary<int, (float, float)>();
        /// <summary>Second lives (the Gravewalker rises once unless burned).</summary>
        public int Lives { get; set; } = 0;
        public bool LastHitWasFire { get; set; }
        /// <summary>Set by Tick when a channel finishes; the brain consumes it.</summary>
        public string CompletedChannel { get; set; }
        public void StartChannel(string name, float seconds, float now) { ChannelName = name; ChannelUntil = now + seconds; }
        /// <summary>Damage over time accumulated since the last whole-second flush, so the hit log and events see one line per second, not one per tick.</summary>
        public float DotPending { get; set; }
        public DamageType DotElement { get; set; }
        public float DotFlushAt { get; set; }
        public float LastSwingAt { get; set; } = -100f;

        private Combatant(int id, int team, bool mob, string name, string spec, ClassArchetype cls, PlayerStats stats, float health, float dps, float attackSpeed, float crit, float armour, Resistances res, DamageProfile profile, StatusEffectDefinition onHit, float lifesteal, float thorns, DamageType thornsType, float move, float regen, ResourceKind resource, float pool, Vec2 pos)
        {
            Id = id; Team = team; IsMob = mob; Name = name; Spec = spec; Class = cls; Stats = stats; MaxHealth = health; Health = health; Dps = dps; AttackSpeed = attackSpeed; CritChance = crit; ArmourRating = armour; Resistances = res;
            AttackProfile = profile; OnHit = onHit; Lifesteal = lifesteal; Thorns = thorns; ThornsType = thornsType; MoveSpeed = move; RegenMultiplier = regen; Resource = resource; ResourceMax = pool; ResourceNow = pool; Position = pos;
            Status.Resistance = mob ? null : new ControlResistance();
        }

        public static Combatant Player(int id, int team, PlayerStats s, Vec2 pos, string specOverride = null)
        {
            string spec = specOverride ?? s.SpecName;
            return new Combatant(id, team, false, $"{s.Class.Name} {id}", spec, s.Class, s, s.Health, s.Dps, s.AttackSpeed, s.CritChance, s.ArmourRating, s.Resistances, s.AttackProfile, s.OnHitEffect, s.Lifesteal, s.Thorns, s.ThornsType, s.MoveSpeed, s.RegenMultiplier, s.Resource, s.ResourcePool, pos);
        }

        public static Combatant Mob(int id, int team, MobArchetype m, float tierMultiplier, CombatBaseline b, Vec2 pos) =>
            new Combatant(id, team, true, m.Name, "Mob", null, null, m.HealthFraction * tierMultiplier * b.PlayerHealth, m.DpsFraction * tierMultiplier * b.PlayerDps, 1f, 0.05f, 30f, m.BuildResistances(), m.Damage, m.OnHit, 0f, 0f, DamageType.Physical, 1f, 1f, ResourceKind.None, 0f, pos);

        public static Combatant Boss(int id, int team, string name, EncounterBudget budget, DamageProfile profile, Resistances res, Vec2 pos, StatusKind? onHit = null) =>
            new Combatant(id, team, true, name, "Boss", null, null, budget.Health, budget.Dps, 1f, 0.05f, 60f, res ?? new Resistances(), profile, onHit.HasValue ? StatusEffectDefinition.All[onHit.Value] : null, 0f, 0f, DamageType.Physical, 0.9f, 1f, ResourceKind.None, 0f, pos);

        public bool Alive => !Dead && !Downed;
        public bool Has(BuffKind k) => Buffs.Any(b => b.Kind == k);
        public float BuffMagnitude(BuffKind k) => Buffs.Where(b => b.Kind == k).Select(b => b.Magnitude).DefaultIfEmpty(0f).Max();
        public bool CanAct => Alive && !Status.IsHardControlled;
        public bool CanMove => CanAct && !Has(BuffKind.Rooted) && !Status.Has(StatusKind.Freeze);
        public bool CanChannel => Alive && !Has(BuffKind.NoChannel);
        /// <summary>The ability ids this combatant can use: four from its spec and the ultimate when armed.</summary>
        public IEnumerable<AbilityDefinition> Abilities => AbilityBook.For(Spec).Where(a => a.Kind != AbilityKind.Ultimate || UltimateArmed);
        public override string ToString() => $"{Name} [{Spec}] T{Team} hp {Health:0}/{MaxHealth:0} at {Position}{(Downed ? " DOWNED" : "")}{(Dead ? " DEAD" : "")}";
    }

    public enum FightEventKind { Hit, Heal, StatusApplied, Downed, Finished, Died, Revived, Rejected, Dashed, KnockedBack, ZoneStarted, Cleansed, HazardFired, Interrupted, Spawned, Phase }

    public readonly struct FightEvent
    {
        public readonly FightEventKind Kind; public readonly float Time; public readonly int Source, Target; public readonly float Amount; public readonly string Detail;
        public FightEvent(FightEventKind kind, float time, int source, int target, float amount, string detail) { Kind = kind; Time = time; Source = source; Target = target; Amount = amount; Detail = detail; }
        public override string ToString() => $"{Time,6:0.0}s {Kind} {Source}→{Target} {Amount:0} {Detail}";
    }

    public sealed class CastResult
    {
        public bool Ok; public string Rejection; public List<int> Targets = new List<int>(); public float TotalDamage; public float TotalHeal;
        public override string ToString() => Ok ? $"hit {Targets.Count} for {TotalDamage:0}" : "rejected: " + Rejection;
    }

    /// <summary>A zone on the ground that ticks once a second until it ends.</summary>
    public sealed class Zone
    {
        public AbilityDefinition Ability; public int Caster; public int CasterTeam; public Vec2 Centre; public float Until; public float NextTick;
        public HashSet<int> Touched = new HashSet<int>();
    }

    /// <summary>
    /// The server's fight resolution: who an ability touches, what it does to them, and what that
    /// means for health, status, downed and dead. Pure and deterministic: the same casts in the
    /// same order with the same seed produce the same fight on any machine. Positions, sight and
    /// solidity come from the dungeon layout (pillars, low walls, closed doors); the rules come from
    /// FightRules, CombatRules and the ability book. Hits go to a <see cref="HitLog"/> for the death
    /// recap. Downed, finished and dead are reported as <see cref="FightEvent"/>s for the server to
    /// turn into match events (<see cref="FightBridge"/>).
    /// </summary>
    public sealed class FightSim
    {
        public DungeonLayout Layout { get; }
        public ISet<GridPoint> ClosedDoors { get; set; } = new HashSet<GridPoint>();
        public Dictionary<int, Combatant> Combatants { get; } = new Dictionary<int, Combatant>();
        public List<Zone> Zones { get; } = new List<Zone>();
        public List<FightEvent> Events { get; } = new List<FightEvent>();
        public HitLog Hits { get; } = new HitLog();
        public float Time { get; private set; }
        private readonly DungeonRandom _rng;
        public const float CellMetres = 2f;
        public const float CritMultiplier = 1.5f;
        public const float BackstabMultiplier = 3f;
        public const float ExecuteBelowFraction = 0.25f;
        public const float ExecuteMultiplier = 3f;
        public const float DeadeyeBeyondMetres = 12f;
        public const float DeadeyeBonus = 0.30f;
        public const float BodyRadiusCells = 0.5f;
        public const float StepCells = 0.25f;

        public FightSim(DungeonLayout layout, int seed) { Layout = layout; _rng = new DungeonRandom(seed); }

        public Combatant Add(Combatant c) { Combatants[c.Id] = c; return c; }
        public Combatant Get(int id) => Combatants.TryGetValue(id, out var c) ? c : null;
        private void Emit(FightEventKind k, int source, int target) => Emit(k, source, target, 0f, "");

        // ---------------------------------------------------------------- geometry

        /// <summary>Does this cell stop a projectile or a body? Walls, pillars, low walls, pits, locked and closed doors do.</summary>
        public bool Solid(GridPoint p)
        {
            if (Layout == null) return false;
            var t = Layout.GetTile(p);
            if (t == TileType.Door) return ClosedDoors.Contains(p);
            return !(t == TileType.RoomFloor || t == TileType.Corridor);
        }
        public bool Walkable(Vec2 p) => !Solid(p.Cell);
        public bool Walkable(Vec2 p, Combatant c)
        {
            if (Layout == null) return true;
            var t = Layout.GetTile(p.Cell);
            if (t == TileType.Door) return !ClosedDoors.Contains(p.Cell) || (c != null && c.PassesDoors);
            if (t == TileType.Pit) return c != null && c.HazardImmune;
            return t == TileType.RoomFloor || t == TileType.Corridor;
        }
        /// <summary>Hazards disarmed this round (mirrors MatchState.DisarmedHazards) and when each trap last fired.</summary>
        public HashSet<int> DisarmedHazards { get; } = new HashSet<int>();
        public Dictionary<int, float> HazardFiredAt { get; } = new Dictionary<int, float>();
        public void Emit(FightEventKind k, int source, int target, float amount, string detail) => Events.Add(new FightEvent(k, Time, source, target, amount, detail));
        /// <summary>Traps fire on the body that steps on them; vents and lava hurt every second you stand in them. Pits are solid to everyone who cannot float.</summary>
        private void CheckHazards(Combatant c)
        {
            if (Layout == null || c.HazardImmune || !c.Alive) return;
            var cell = c.Position.Cell;
            foreach (var h in Layout.Hazards)
            {
                if (!h.Bounds.Contains(cell) || DisarmedHazards.Contains(h.Id)) continue;
                switch (h.Kind)
                {
                    case HazardKind.SpikeTrap:
                        if (HazardFiredAt.TryGetValue(h.Id, out float at) && Time - at < HazardRules.SpikeRearmSeconds) break;
                        HazardFiredAt[h.Id] = Time;
                        Emit(FightEventKind.HazardFired, h.Id, c.Id, 0f, h.Kind.ToString());
                        ApplyDamage(c, c.MaxHealth * HazardRules.SpikeDamageFraction, DamageProfile.Pure(DamageType.Physical), null, "spike trap");
                        break;
                    case HazardKind.GasVent:
                        if (!HazardFiredAt.TryGetValue(h.Id, out float g) || Time - g >= 1f) { HazardFiredAt[h.Id] = Time; Emit(FightEventKind.HazardFired, h.Id, c.Id, 0f, h.Kind.ToString()); }
                        if (!c.Status.Has(StatusKind.Poisoned) || c.Status.Get(StatusKind.Poisoned).RemainingSeconds < 1.5f) { c.Status.Apply(StatusEffectDefinition.All[StatusKind.Poisoned], c.Resistances); c.DotBase[StatusKind.Poisoned] = Math.Max(c.DotBase.TryGetValue(StatusKind.Poisoned, out float pb) ? pb : 0f, c.MaxHealth * HazardRules.GasPoisonPerSecondFraction / StatusEffectDefinition.All[StatusKind.Poisoned].Magnitude); }
                        break;
                    case HazardKind.Lava:
                        if (!HazardFiredAt.TryGetValue(h.Id, out float l) || Time - l >= 1f) { HazardFiredAt[h.Id] = Time; Emit(FightEventKind.HazardFired, h.Id, c.Id, 0f, h.Kind.ToString()); }
                        if (!c.Status.Has(StatusKind.Burn) || c.Status.Get(StatusKind.Burn).RemainingSeconds < 1.5f) { c.Status.Apply(StatusEffectDefinition.All[StatusKind.Burn], c.Resistances); c.DotBase[StatusKind.Burn] = Math.Max(c.DotBase.TryGetValue(StatusKind.Burn, out float lb) ? lb : 0f, c.MaxHealth * HazardRules.LavaBurnPerSecondFraction / StatusEffectDefinition.All[StatusKind.Burn].Magnitude); }
                        break;
                }
            }
        }
        /// <summary>Sight follows LineOfSight: low walls and pits do not block it, pillars and closed doors do.</summary>
        public bool CanSee(Vec2 from, Vec2 to) => Layout == null || LineOfSight.CanSee(Layout, from.Cell, to.Cell, ClosedDoors);
        private bool IsBehind(Combatant attacker, Combatant target) => (attacker.Position - target.Position).Normalized.Dot(target.Facing) < -0.3f;   // the attacker stands where the target is not looking

        private IEnumerable<Combatant> Bodies(int except) => Combatants.Values.Where(c => c.Id != except && !c.Dead);

        private static bool Passes(TargetFilter f, Combatant caster, Combatant target)
        {
            switch (f)
            {
                case TargetFilter.Allies: return target.Team == caster.Team && !target.Downed;
                case TargetFilter.Enemies: return target.Team != caster.Team && !target.Downed;
                case TargetFilter.Downed: return target.Team == caster.Team && target.Downed;
                default: return !target.Downed || true;   // Everyone: friendly fire is full; downed bodies take finishing damage
            }
        }

        /// <summary>Walk a segment from <paramref name="from"/> toward <paramref name="aim"/> up to range; returns the bodies met in order and where the travel stopped.</summary>
        private List<Combatant> Travel(Combatant caster, Vec2 aim, float range, float width, bool stopAtFirstBody, out Vec2 end)
        {
            var dir = (aim - caster.Position).Normalized;
            var hits = new List<Combatant>();
            var pos = caster.Position; end = pos;
            for (float d = StepCells; d <= range; d += StepCells)
            {
                var next = caster.Position + dir * d;
                if (Solid(next.Cell)) break;
                end = next;
                foreach (var b in Bodies(caster.Id).Where(b => !hits.Contains(b) && Vec2.Distance(b.Position, next) <= width / 2f + BodyRadiusCells))
                {
                    hits.Add(b);
                    if (stopAtFirstBody) return hits;
                }
            }
            return hits;
        }

        private List<Combatant> InCone(Combatant caster, Vec2 aim, float range, float degrees)
        {
            var dir = (aim - caster.Position).Normalized;
            float cos = (float)Math.Cos(degrees / 2f * Math.PI / 180f);
            return Bodies(caster.Id).Where(b =>
            {
                var to = b.Position - caster.Position;
                return to.Length <= range + BodyRadiusCells && (to.Length < 0.01f || to.Normalized.Dot(dir) >= cos) && CanSee(caster.Position, b.Position);
            }).ToList();
        }

        private List<Combatant> InCircle(Vec2 centre, float radius, int except) => Bodies(except).Where(b => Vec2.Distance(b.Position, centre) <= radius + BodyRadiusCells).ToList();

        // ---------------------------------------------------------------- casting

        public CastResult Cast(int casterId, string abilityId, Vec2 aim)
        {
            var r = new CastResult();
            var caster = Get(casterId);
            if (caster == null || !AbilityBook.TryGet(abilityId, out var a)) { r.Rejection = "no such caster or ability"; return Reject(casterId, abilityId, r); }
            if (!caster.CanAct) { r.Rejection = caster.Dead ? "dead" : caster.Downed ? "downed" : "controlled"; return Reject(casterId, abilityId, r); }
            if (a.Spec != caster.Spec) { r.Rejection = "not this spec's ability"; return Reject(casterId, abilityId, r); }
            if (a.Kind == AbilityKind.Ultimate && !caster.UltimateArmed) { r.Rejection = "ultimate not armed"; return Reject(casterId, abilityId, r); }
            if (a.Kind != AbilityKind.Basic && caster.Has(BuffKind.Silenced)) { r.Rejection = "silenced"; return Reject(casterId, abilityId, r); }
            if (caster.CooldownUntil.TryGetValue(a.Id, out float until) && Time < until - 1e-4f) { r.Rejection = $"on cooldown for {until - Time:0.0} s"; return Reject(casterId, abilityId, r); }
            if (a.Kind == AbilityKind.Basic && Time < caster.NextBasicAt - 1e-4f) { r.Rejection = "swinging"; return Reject(casterId, abilityId, r); }
            float cost = a.CostSlot.HasValue && caster.Resource != ResourceKind.None ? FightRules.AbilityCost(caster.ResourceMax, a.CostSlot.Value) : 0f;
            if (cost > caster.ResourceNow + 1e-4f) { r.Rejection = $"needs {cost:0} {caster.Resource}"; return Reject(casterId, abilityId, r); }
            var toAim = aim - caster.Position;
            if (toAim.Length > 0.01f) caster.Facing = toAim.Normalized;
            if (a.Shape == AbilityShape.Dash && !caster.CanMove) { r.Rejection = "rooted"; return Reject(casterId, abilityId, r); }

            Vec2 origin = caster.Position;
            var targets = SelectTargets(caster, a, aim, out var centre, out var dashEnd, out string why);
            if (why != null) { r.Rejection = why; return Reject(casterId, abilityId, r); }
            if (a.Shape == AbilityShape.Circle && a.IsZone)
            {
                Zones.Add(new Zone { Ability = a, Caster = caster.Id, CasterTeam = caster.Team, Centre = centre, Until = Time + a.DurationSeconds, NextTick = Time });
                Emit(FightEventKind.ZoneStarted, caster.Id, -1, a.DurationSeconds, a.Name);
            }
            if (a.Shape == AbilityShape.Dash) { caster.Position = dashEnd; Emit(FightEventKind.Dashed, caster.Id, -1, Vec2.Distance(origin, dashEnd), a.Name); }

            // Spend and start the cooldown before effects: a reflected hit must not refund the cast.
            caster.ResourceNow -= cost;
            if (a.CooldownSeconds > 0f) caster.CooldownUntil[a.Id] = Time + a.CooldownSeconds;
            if (a.Kind == AbilityKind.Basic) { caster.NextBasicAt = Time + 1f / Math.Max(0.2f, caster.AttackSpeed * caster.AttackSpeedScale * (1f + caster.BuffMagnitude(BuffKind.AttackSpeed))); caster.LastSwingAt = Time; }
            if (a.Kind == AbilityKind.Ultimate) caster.UltimateArmed = false;
            if (a.Buff.HasValue && !a.BuffOnTargets) AddBuff(caster, a.Buff.Value, a.BuffMagnitude, a.BuffSeconds, caster.Id);
            if (a.Buff == BuffKind.Taunt) AddBuff(caster, BuffKind.DamageReduction, a.BuffMagnitude, a.BuffSeconds, caster.Id);   // Hold the Line: the taunt's reduction is the caster's

            if (!a.IsZone)
                foreach (var t in targets.Where(t => t.Id == caster.Id ? a.Shape == AbilityShape.Self : Passes(a.Filter, caster, t)))
                    Affect(caster, t, a, r, aim);
            r.Ok = true;
            return r;
        }

        /// <summary>Who an ability would touch from here, without casting it. The brain uses it to avoid hitting friends; a HUD could draw it.</summary>
        public List<Combatant> Preview(int casterId, string abilityId, Vec2 aim)
        {
            var caster = Get(casterId);
            if (caster == null || !AbilityBook.TryGet(abilityId, out var a)) return new List<Combatant>();
            var t = SelectTargets(caster, a, aim, out _, out _, out string why);
            return why == null ? t.Where(x => x.Id == caster.Id ? a.Shape == AbilityShape.Self : Passes(a.Filter, caster, x)).ToList() : new List<Combatant>();
        }

        private List<Combatant> SelectTargets(Combatant caster, AbilityDefinition a, Vec2 aim, out Vec2 centre, out Vec2 dashEnd, out string rejection)
        {
            var targets = new List<Combatant>();
            var toAim = aim - caster.Position;
            centre = caster.Position; dashEnd = caster.Position; rejection = null;
            switch (a.Shape)
            {
                case AbilityShape.Self: targets.Add(caster); break;
                case AbilityShape.Target:
                    {
                        if (toAim.Length > a.RangeCells + BodyRadiusCells) { rejection = "out of range"; break; }
                        var t = Bodies(a.Filter == TargetFilter.Allies || a.Filter == TargetFilter.Downed ? -1 : caster.Id).Where(b => Vec2.Distance(b.Position, aim) <= a.Size / 2f + BodyRadiusCells && Passes(a.Filter, caster, b) && CanSee(caster.Position, b.Position)).OrderBy(b => Vec2.Distance(b.Position, aim)).FirstOrDefault();
                        if (t == null) { rejection = "no target there"; break; }
                        targets.Add(t); break;
                    }
                case AbilityShape.Projectile: targets = Travel(caster, aim, a.RangeCells, a.Size, true, out _); break;
                case AbilityShape.Pierce: targets = Travel(caster, aim, a.RangeCells, a.Size, false, out _); break;
                case AbilityShape.Cone: targets = InCone(caster, aim, a.RangeCells, a.Size); break;
                case AbilityShape.Circle:
                    {
                        centre = a.RangeCells <= 0f ? caster.Position : (toAim.Length <= a.RangeCells ? aim : caster.Position + toAim.Normalized * a.RangeCells);
                        if (a.RangeCells > 0f && !CanSee(caster.Position, centre)) { rejection = "no line of sight to the point"; break; }
                        if (!a.IsZone) targets = InCircle(centre, a.Size, caster.Id);
                        else targets = InCircle(centre, a.Size, caster.Id);   // for a preview: who is standing in it now
                        break;
                    }
                case AbilityShape.Dash:
                    {
                        var met = Travel(caster, aim, Math.Min(a.RangeCells, Math.Max(toAim.Length, 0.5f)), Math.Max(a.Size, 0.5f), a.Damage > 0f || a.KnockbackCells > 0f, out dashEnd);
                        if (a.Damage > 0f || a.KnockbackCells > 0f) targets = met;
                        break;
                    }
            }
            return targets;
        }

        private CastResult Reject(int caster, string ability, CastResult r) { Emit(FightEventKind.Rejected, caster, -1, 0f, $"{ability}: {r.Rejection}"); return r; }

        private void Affect(Combatant caster, Combatant t, AbilityDefinition a, CastResult r, Vec2 aim)
        {
            r.Targets.Add(t.Id);
            if (a.Id == "Warder.mobility" || a.Id == "Plaguebearer.mobility") { t.Status.Cleanse(); Emit(FightEventKind.Cleansed, caster.Id, t.Id, 0f, a.Name); }
            if (a.Filter == TargetFilter.Downed && t.Downed && a.Heal > 0f) { Revive(t, 1f); caster.Health = caster.MaxHealth; return; }   // Last Breath
            if (a.Heal > 0f && !t.Downed) r.TotalHeal += Heal(caster, t, a.Heal);
            if (a.Damage > 0f && t.Id != caster.Id) r.TotalDamage += Strike(caster, t, a, aim);
            if (a.Buff.HasValue && a.BuffOnTargets && t.Id != caster.Id && (a.Buff != BuffKind.Rooted || !t.IsMob || true)) AddBuff(t, a.Buff.Value, a.BuffMagnitude, a.BuffSeconds, caster.Id);
            if (a.Status.HasValue && a.Damage <= 0f && t.Id != caster.Id) ApplyStatus(caster, t, StatusEffectDefinition.All[a.Status.Value], 0f, a.Id);
            if (a.KnockbackCells > 0f && t.Id != caster.Id && !t.Has(BuffKind.Immovable) && !(t.Spec == "Sentinel" && Layout != null && Layout.GetTile(t.Position.Cell) == TileType.Door)) Knockback(t, (t.Position - caster.Position).Normalized, a.KnockbackCells, caster.Id);
        }

        private void AddBuff(Combatant c, BuffKind k, float magnitude, float seconds, int source)
        {
            var existing = c.Buffs.FirstOrDefault(b => b.Kind == k && b.Source == source);
            if (existing != null) { existing.Until = Math.Max(existing.Until, Time + seconds); existing.Magnitude = Math.Max(existing.Magnitude, magnitude); return; }
            c.Buffs.Add(new Buff { Kind = k, Magnitude = magnitude, Until = Time + seconds, Source = source });
            if (k == BuffKind.Taunt) AddBuff(c, BuffKind.Rooted, 1f, 0f, source);   // placeholder no-op root for mobs: taunt is read by the AI, not enforced here
        }

        private void Knockback(Combatant t, Vec2 dir, float cells, int source)
        {
            float moved = 0f;
            for (float d = StepCells; d <= cells; d += StepCells)
            {
                var next = t.Position + dir * StepCells;
                if (Solid(next.Cell)) break;
                t.Position = next; moved += StepCells;
            }
            Emit(FightEventKind.KnockedBack, source, t.Id, moved, "");
        }

        // ---------------------------------------------------------------- damage

        /// <summary>One hit of an ability from attacker to defender. Returns the damage that landed.</summary>
        public float Strike(Combatant attacker, Combatant defender, AbilityDefinition a, Vec2? aim = null)
        {
            float raw = attacker.Dps * a.Damage * attacker.DamageScale;
            if (a.Kind == AbilityKind.Basic) raw = attacker.Dps * attacker.DamageScale / Math.Max(0.2f, attacker.AttackSpeed);
            if (attacker.Spec == "Berserker") raw *= 1f + (1f - attacker.Health / attacker.MaxHealth);                     // Frenzy
            if (attacker.Spec == "Sharpshooter" && Vec2.Distance(attacker.Position, defender.Position) * CellMetres > DeadeyeBeyondMetres) raw *= 1f + DeadeyeBonus;   // Deadeye
            if (a.Id == "Assassin.bolt" && IsBehind(attacker, defender)) raw *= BackstabMultiplier;                        // Backstab
            if (attacker.Spec == "Duelist" && defender.Health < defender.MaxHealth * ExecuteBelowFraction && a.Kind != AbilityKind.Zone) raw *= ExecuteMultiplier;   // Execute
            if (attacker.Status.Has(StatusKind.Weaken)) raw *= 1f - StatusEffectDefinition.All[StatusKind.Weaken].Magnitude;
            bool crit = defender.Has(BuffKind.Marked) && defender.Buffs.Any(b => b.Kind == BuffKind.Marked && Get(b.Source)?.Team == attacker.Team) || _rng.Chance(attacker.CritChance);
            if (crit) raw *= CritMultiplier;
            if (defender.Status.Has(StatusKind.Freeze) && a.Element == DamageType.Frost == false && a.Kind != AbilityKind.Basic) { }   // shatter handled below

            // Defender stances.
            if (defender.Has(BuffKind.Invulnerable)) { Emit(FightEventKind.Hit, attacker.Id, defender.Id, 0f, a.Name + " (invulnerable)"); return 0f; }
            if (defender.Has(BuffKind.Reflect))
            {
                float back = ApplyDamage(attacker, raw, Profile(attacker, a), defender, a.Name + " (reflected)");
                ApplyStatus(defender, attacker, StatusEffectDefinition.All[StatusKind.Stagger], raw, a.Id);
                return 0f;
            }
            if (defender.Has(BuffKind.FrontShield) && !IsBehind(attacker, defender)) { Emit(FightEventKind.Hit, attacker.Id, defender.Id, 0f, a.Name + " (shield wall)"); return 0f; }
            if (defender.Has(BuffKind.Invisible)) defender.Buffs.RemoveAll(b => b.Kind == BuffKind.Invisible);

            var profile = Profile(attacker, a);
            float landed = ApplyDamage(defender, raw, profile, attacker, a.Name + (crit ? " (crit)" : ""));
            // Shatter: damage to a frozen target ends the freeze for 30% more.
            if (defender.Status.Has(StatusKind.Freeze) && landed > 0f) { defender.Status.Active.ToList().ForEach(s => { }); landed += ApplyDamage(defender, raw * 0.3f, profile, attacker, "shatter"); defender.Status.Cleanse(); }
            if (landed > 0f)
            {
                var status = a.Status.HasValue ? StatusEffectDefinition.All[a.Status.Value] : (a.Kind == AbilityKind.Basic || a.Kind == AbilityKind.Bolt ? attacker.OnHit : null);
                if (status != null) ApplyStatus(attacker, defender, status, landed, a.Id);   // "a fraction of the hit": the hit as taken, so armour and resistance shrink the burn too
                if (attacker.Lifesteal > 0f) Heal(attacker, attacker, 0f, landed * attacker.Lifesteal);
                if (attacker.Has(BuffKind.CannotBeDowned)) Heal(attacker, attacker, 0f, landed * attacker.BuffMagnitude(BuffKind.CannotBeDowned));   // Bloodbath
                if (defender.Thorns > 0f) ApplyDamage(attacker, landed * defender.Thorns, DamageProfile.Pure(defender.ThornsType), defender, "thorns");
                if (attacker.Spec == "Breaker") { attacker.MomentumStacks = Math.Min(6, attacker.MomentumStacks + 1); }
            }
            return landed;
        }

        private static DamageProfile Profile(Combatant attacker, AbilityDefinition a) => a.Element.HasValue ? DamageProfile.Pure(a.Element.Value) : attacker.AttackProfile;

        /// <summary>Mitigate and subtract. Physical goes through armour, everything else through resistances; damage reduction buffs apply after. Handles downed and dead.</summary>
        public float ApplyDamage(Combatant defender, float raw, DamageProfile profile, Combatant attacker, string detail)
        {
            if (defender.Dead || raw <= 0f) return 0f;
            float total = 0f;
            foreach (var t in profile.Types)
            {
                float part = raw * profile.Fraction(t);
                total += t == DamageType.Physical ? part * (1f - StatFormulas.Mitigation(defender.ArmourRating, StatFormulas.MitigationCap + defender.Resistances.CapBonus / 100f)) : defender.Resistances.DamageTaken(part, t);
            }
            float reduction = Math.Min(0.9f, defender.BuffMagnitude(BuffKind.DamageReduction) + (defender.Spec == "Breaker" && Time - defender.LastSwingAt <= 3f ? Math.Min(0.3f, 0.05f * defender.MomentumStacks) : 0f));
            total *= 1f - reduction;
            total = (float)Math.Round(total, 1);
            Hits.Record(defender.Id, new Hit(Time, attacker?.Id ?? -1, attacker?.Spec, profile.Dominant, total, detail));
            Emit(FightEventKind.Hit, attacker?.Id ?? -1, defender.Id, total, detail);
            if (attacker != null) defender.Threat[attacker.Id] = ((defender.Threat.TryGetValue(attacker.Id, out var th) && Time - th.at < 10f ? th.damage : 0f) + total, Time);
            defender.LastHitWasFire = profile.Dominant == DamageType.Fire;
            if (defender.Downed)
            {
                if (attacker == null || attacker.Team == defender.Team) return 0f;   // only an enemy finishes a downed player; a teammate's stray swing does nothing to a body
                defender.FinishDamage += total;
                if (defender.FinishDamage >= defender.MaxHealth * FightRules.FinishDamageFraction) { defender.Dead = true; Emit(FightEventKind.Finished, attacker?.Id ?? -1, defender.Id, 0f, ""); }
                return total;
            }
            defender.Health -= total;
            if (defender.Health <= 0f)
            {
                if (defender.Has(BuffKind.CannotBeDowned)) { defender.Health = 1f; return total; }   // Bloodbath: you fall when it ends
                defender.Health = 0f;
                if (defender.IsMob)
                {
                    if (defender.Lives > 0 && !defender.LastHitWasFire) { defender.Lives--; defender.Health = defender.MaxHealth * 0.5f; defender.Status.Cleanse(); Emit(FightEventKind.Revived, -1, defender.Id, defender.Health, "rises again"); return total; }
                    defender.Dead = true; Emit(FightEventKind.Died, attacker?.Id ?? -1, defender.Id, 0f, "");
                }
                else { defender.Downed = true; defender.DownedAt = Time; defender.FinishDamage = 0f; defender.Status.Cleanse(); defender.Buffs.Clear(); Emit(FightEventKind.Downed, attacker?.Id ?? -1, defender.Id, 0f, ""); }
            }
            return total;
        }

        private void ApplyStatus(Combatant source, Combatant target, StatusEffectDefinition effect, float rawHit, string ability)
        {
            if (target.Dead || target.Downed) return;
            if (effect.Kind == StatusKind.Freeze && ability.EndsWith(".ult")) { target.Status.Resistance = null; }            // Absolute Zero ignores diminishing returns
            var landed = target.Status.Apply(effect, target.Resistances);
            if (effect.Kind == StatusKind.Freeze && ability.EndsWith(".ult") && !target.IsMob) target.Status.Resistance = new ControlResistance();
            if (landed == null) return;
            if ((landed.Definition.Kind == StatusKind.Stagger || landed.Definition.IsHardControl) && target.Channelling) { Emit(FightEventKind.Interrupted, source.Id, target.Id, 0f, target.ChannelName); target.ChannelName = null; target.ChannelUntil = -1f; }
            if (landed.Definition.IsDamageOverTime && rawHit > 0f) target.DotBase[landed.Definition.Kind] = Math.Max(target.DotBase.TryGetValue(landed.Definition.Kind, out float b) ? b : 0f, rawHit);
            if (effect.Kind == StatusKind.Poisoned && ability.EndsWith(".ult")) { var p = target.Status.Get(StatusKind.Poisoned); if (p != null) p.Stacks = p.Definition.MaxStacks; }   // Outbreak
            Emit(FightEventKind.StatusApplied, source.Id, target.Id, landed.RemainingSeconds, landed.Definition.Kind.ToString());
            // Burn spreads to enemies within 2 m; Stagger chains to two within 4 m.
            if (effect.Spreads && ability.Contains("Pyromancy") || effect.Chains && ability.Contains("Stormcalling"))
            {
                float reach = (effect.Spreads ? 2f : 4f) / CellMetres;
                foreach (var other in InCircle(target.Position, reach, target.Id).Where(o => o.Id != source.Id && o.Team != source.Team && !o.Status.Has(effect.Kind)).Take(2))
                {
                    var l2 = other.Status.Apply(effect, other.Resistances);
                    if (l2 != null) { if (rawHit > 0f && l2.Definition.IsDamageOverTime) other.DotBase[l2.Definition.Kind] = rawHit; Emit(FightEventKind.StatusApplied, source.Id, other.Id, l2.RemainingSeconds, effect.Kind + " (spread)"); }
                }
            }
        }

        private float Heal(Combatant source, Combatant target, float multiplier, float flat = 0f)
        {
            if (target.Dead || target.Downed) return 0f;
            float amount = flat + (multiplier > 0f ? source.Dps * multiplier * (source.Stats?.HealingMultiplier ?? 1f) * (source.Spec == "Plaguebearer" ? 0.75f : 1f) : 0f);
            if (target.Status.Has(StatusKind.Weaken)) amount *= 1f - StatusEffectDefinition.All[StatusKind.Weaken].Magnitude;
            float before = target.Health;
            target.Health = Math.Min(target.MaxHealth, target.Health + amount);
            float healed = target.Health - before;
            if (healed > 0f) Emit(FightEventKind.Heal, source.Id, target.Id, healed, "");
            return healed;
        }

        /// <summary>A revive by channel (30% health) or by Last Breath (full).</summary>
        public void Revive(Combatant t, float healthFraction)
        {
            if (!t.Downed || t.Dead) return;
            t.Downed = false; t.Health = t.MaxHealth * healthFraction; t.FinishDamage = 0f;
            Emit(FightEventKind.Revived, -1, t.Id, t.Health, "");
        }

        // ---------------------------------------------------------------- time

        /// <summary>Advance the fight: cooldowns run, resources regenerate, statuses tick and burn, zones pulse, the downed bleed out, buffs expire.</summary>
        public void Tick(float dt)
        {
            Time += dt;
            foreach (var c in Combatants.Values.Where(c => !c.Dead).ToList())
            {
                if (c.Resource != ResourceKind.None && FightRules.ResourcesRegenerate) c.ResourceNow = Math.Min(c.ResourceMax, c.ResourceNow + c.ResourceMax * FightRules.ResourceRegenPerSecond * c.RegenMultiplier * dt);
                c.Buffs.RemoveAll(b => Time >= b.Until);
                if (c.Spec == "Breaker" && Time - c.LastSwingAt > 3f) c.MomentumStacks = 0;
                if (c.Channelling && Time >= c.ChannelUntil) { Emit(FightEventKind.Phase, c.Id, -1, 0f, "channel:" + c.ChannelName); c.CompletedChannel = c.ChannelName; c.ChannelName = null; c.ChannelUntil = -1f; }
                CheckHazards(c);
                if (c.Downed)
                {
                    if (Time - c.DownedAt >= FightRules.DownedSeconds) { c.Dead = true; Emit(FightEventKind.Died, -1, c.Id, 0f, "bled out"); }
                    continue;
                }
                // Damage over time: each status ticks a fraction of the hit that applied it; applied once per second so the recap reads "Burn 48", not forty lines of 1.2.
                float dot = 0f;
                foreach (var s in c.Status.Active.Where(s => s.Definition.IsDamageOverTime))
                {
                    dot += (c.DotBase.TryGetValue(s.Definition.Kind, out float b) ? b : c.MaxHealth * 0.05f) * s.Stacks * s.Definition.Magnitude * dt;
                    c.DotElement = s.Definition.Element;
                }
                if (dot > 0f) { c.DotPending += dot; if (c.DotFlushAt <= 0f) c.DotFlushAt = Time + 1f; }
                if (c.DotPending > 0f && Time >= c.DotFlushAt) { ApplyDamage(c, c.DotPending, DamageProfile.Pure(c.DotElement), null, c.DotElement + " over time"); c.DotPending = 0f; c.DotFlushAt = Time + 1f; }
                c.Status.Tick(dt);
            }
            foreach (var z in Zones.ToList())
            {
                if (Time >= z.Until) { Zones.Remove(z); continue; }
                if (Time < z.NextTick) continue;
                z.NextTick = Time + 1f;
                var caster = Get(z.Caster);
                if (caster == null) continue;
                foreach (var t in InCircle(z.Centre, z.Ability.Size, -1))
                {
                    if (t.Id == caster.Id && z.Ability.Filter != TargetFilter.Allies) continue;
                    if (!Passes(z.Ability.Filter, caster, t)) continue;
                    if (z.Ability.Id.StartsWith("Trapper.") && !z.Touched.Add(t.Id)) continue;   // a tripwire fires once per body
                    var r = new CastResult();
                    if (z.Ability.Filter == TargetFilter.Allies || z.Ability.Filter == TargetFilter.Everyone && z.Ability.Heal > 0f && t.Team == caster.Team) { if (z.Ability.Heal > 0f) Heal(caster, t, z.Ability.Heal); if (z.Ability.Buff.HasValue && z.Ability.BuffOnTargets) AddBuff(t, z.Ability.Buff.Value, z.Ability.BuffMagnitude, z.Ability.BuffSeconds, caster.Id); continue; }
                    if (z.Ability.Damage > 0f && t.Id != caster.Id) Strike(caster, t, z.Ability);
                    else if (z.Ability.Status.HasValue && t.Id != caster.Id) ApplyStatus(caster, t, StatusEffectDefinition.All[z.Ability.Status.Value], 0f, z.Ability.Id);
                    if (z.Ability.Buff.HasValue && z.Ability.BuffOnTargets && t.Id != caster.Id) AddBuff(t, z.Ability.Buff.Value, z.Ability.BuffMagnitude, z.Ability.BuffSeconds, caster.Id);
                }
            }
        }

        /// <summary>Move a combatant toward a point for dt seconds at its speed, slowed by Chill, stopped by anything solid.</summary>
        public void Move(int id, Vec2 toward, float dt)
        {
            var c = Get(id);
            if (c == null || !c.CanMove) return;
            if (c.Channelling) return;   // a wind-up roots you
            float speed = MovementRules.BaseSpeedMps / CellMetres * c.MoveSpeed * c.MoveSpeedScale * (1f - c.Status.SlowFraction) * (c.Has(BuffKind.Invisible) ? 0.6f : 1f);
            var dir = (toward - c.Position);
            float dist = Math.Min(dir.Length, speed * dt);
            if (dist < 1e-4f) return;
            var step = dir.Normalized * dist;
            var next = c.Position + step;
            if (Walkable(next, c)) { c.Position = next; c.Facing = dir.Normalized; }
            else if (Walkable(new Vec2(next.X, c.Position.Y), c)) c.Position = new Vec2(next.X, c.Position.Y);
            else if (Walkable(new Vec2(c.Position.X, next.Y), c)) c.Position = new Vec2(c.Position.X, next.Y);
            CheckHazards(c);
        }
    }

    /// <summary>
    /// Turns what the fight simulator reports into the match events the round state validates, so
    /// the authoritative MatchState hears about downed, finished and dead players and dead mobs and
    /// bosses through the same door as everything else.
    /// </summary>
    public static class FightBridge
    {
        public static IEnumerable<MatchEvent> ToMatchEvents(FightEvent e, FightSim sim, Func<int, int> roomOf, Func<int, int?> bossRoomOf = null, Func<int, int?> packOf = null)
        {
            var target = sim.Get(e.Target);
            switch (e.Kind)
            {
                case FightEventKind.Downed when target != null && !target.IsMob:
                    yield return new MatchEvent(MatchEventKind.PlayerDowned, e.Time, e.Target, e.Source, roomOf(e.Target)); break;
                case FightEventKind.Finished when target != null && !target.IsMob:
                    yield return new MatchEvent(MatchEventKind.PlayerFinished, e.Time, e.Target, e.Source, roomOf(e.Target)); break;
                case FightEventKind.Died when target != null && !target.IsMob:
                    yield return new MatchEvent(MatchEventKind.PlayerDied, e.Time, e.Target, e.Source, roomOf(e.Target)); break;
                case FightEventKind.Died when target != null && target.IsMob:
                    {
                        int? boss = bossRoomOf?.Invoke(e.Target);
                        if (boss.HasValue) { var killer = sim.Get(e.Source); yield return new MatchEvent(MatchEventKind.BossKilled, e.Time, boss.Value, killer?.Team ?? -1); }
                        else { int? pack = packOf?.Invoke(e.Target); if (pack.HasValue && sim.Combatants.Values.Where(c => c.IsMob && packOf(c.Id) == pack).All(c => c.Dead)) yield return new MatchEvent(MatchEventKind.MobPackCleared, e.Time, pack.Value); }
                        break;
                    }
                case FightEventKind.Revived when target != null && !target.IsMob:
                    yield return new MatchEvent(MatchEventKind.PlayerRevived, e.Time, e.Target, e.Source); break;
                case FightEventKind.HazardFired when target != null && !target.IsMob:
                    yield return new MatchEvent(MatchEventKind.HazardTriggered, e.Time, e.Source, e.Target); break;
            }
        }
    }
}
