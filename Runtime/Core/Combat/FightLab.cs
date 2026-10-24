using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core.Combat
{
    /// <summary>
    /// A plain brain for the fight lab: nearest enemy, bolt when ready, zone when it pays, dash to
    /// close, basic attack in range, Menders heal the lowest ally, bosses slam when crowded. Not the
    /// game's AI; the thing that lets thousands of fights run so the numbers can be read.
    /// </summary>
    public static class SimpleBrain
    {
        /// <summary>A point at melee range around the target, spread by caster id so a party surrounds instead of stacking.</summary>
        public static Vec2 Flank(FightSim sim, Combatant c, Combatant target, float range)
        {
            int slot = c.Id % 6;
            double angle = slot * Math.PI / 3.0;
            float r = Math.Max(0.8f, range * 0.85f);
            var p = target.Position + new Vec2((float)Math.Cos(angle), (float)Math.Sin(angle)) * r;
            return sim.Walkable(p) ? p : target.Position;
        }

        public static void Act(FightSim sim, Combatant c, float dt)
        {
            if (!c.CanAct) return;
            var enemies = sim.Combatants.Values.Where(o => o.Team != c.Team && !o.Dead).ToList();
            var alive = enemies.Where(o => !o.Downed).ToList();
            var target = (alive.Count > 0 ? alive : enemies).OrderBy(o => Vec2.Distance(o.Position, c.Position)).FirstOrDefault();
            // Taunt overrides: a mob or boss under a Warden's Hold the Line turns on the Warden.
            var taunt = c.Buffs.FirstOrDefault(b => b.Kind == BuffKind.Taunt);
            if (c.IsMob && taunt != null && sim.Get(taunt.Source) is Combatant taunter && taunter.Alive) target = taunter;
            var abilities = c.Abilities.ToList();
            var basic = abilities.FirstOrDefault(a => a.Kind == AbilityKind.Basic);
            var bolt = abilities.FirstOrDefault(a => a.Kind == AbilityKind.Bolt);
            var zone = abilities.FirstOrDefault(a => a.Kind == AbilityKind.Zone);
            var mobility = abilities.FirstOrDefault(a => a.Kind == AbilityKind.Mobility);
            var ult = abilities.FirstOrDefault(a => a.Kind == AbilityKind.Ultimate);

            // Healers first: the lowest ally under 60%.
            if (bolt != null && bolt.Filter == TargetFilter.Allies && bolt.Heal > 0f)
            {
                var hurt = sim.Combatants.Values.Where(o => o.Team == c.Team && o.Alive && o.Health < o.MaxHealth * 0.6f).OrderBy(o => o.Health / o.MaxHealth).FirstOrDefault();
                if (hurt != null && Vec2.Distance(hurt.Position, c.Position) <= bolt.RangeCells && sim.Cast(c.Id, bolt.Id, hurt.Position).Ok) return;
            }
            if (zone != null && zone.Filter == TargetFilter.Allies)
            {
                var low = sim.Combatants.Values.Where(o => o.Team == c.Team && o.Alive && o.Health < o.MaxHealth * 0.5f).OrderBy(o => o.Health / o.MaxHealth).FirstOrDefault();
                if (low != null && Vec2.Distance(low.Position, c.Position) <= zone.RangeCells && sim.Cast(c.Id, zone.Id, low.Position).Ok) return;
            }
            if (target == null) return;
            float dist = Vec2.Distance(target.Position, c.Position);
            bool sees = sim.CanSee(c.Position, target.Position);
            // Full friendly fire: never swing through a teammate. Check who an ability would touch first.
            bool Clear(AbilityDefinition a, Vec2 aim) => a.Filter == TargetFilter.Allies || !sim.Preview(c.Id, a.Id, aim).Any(t => t.Team == c.Team && t.Id != c.Id);
            if (ult != null && c.UltimateArmed && (ult.Shape == AbilityShape.Self || dist <= Math.Max(ult.RangeCells, ult.Size)))
            {
                var aim = ult.Shape == AbilityShape.Self ? c.Position : target.Position;
                if ((ult.Damage <= 0f || Clear(ult, aim)) && sim.Cast(c.Id, ult.Id, aim).Ok) return;
            }
            // Tanks: taunt when the enemy is on top of the party, raise the shield wall when it is in front.
            if (mobility != null && mobility.Buff == BuffKind.Taunt && enemies.Any(o => !o.Downed && Vec2.Distance(o.Position, c.Position) <= mobility.Size) && sim.Cast(c.Id, mobility.Id, c.Position).Ok) return;
            if (zone != null && zone.Buff == BuffKind.FrontShield && dist <= 2f && c.Health < c.MaxHealth * 0.7f && sim.Cast(c.Id, zone.Id, c.Position).Ok) return;
            if (zone != null && zone.Damage > 0f && sees)
            {
                int crowd = enemies.Count(o => Vec2.Distance(o.Position, zone.RangeCells <= 0f ? c.Position : target.Position) <= Math.Max(zone.Size, 1.5f));
                if ((crowd >= 2 || (zone.Shape == AbilityShape.Cone && dist <= zone.RangeCells)) && Clear(zone, target.Position) && sim.Cast(c.Id, zone.Id, target.Position).Ok) return;
            }
            if (bolt != null && bolt.Filter != TargetFilter.Allies && sees && dist <= bolt.RangeCells + 0.5f && Clear(bolt, target.Position) && sim.Cast(c.Id, bolt.Id, target.Position).Ok) return;
            if (basic != null && sees && dist <= basic.RangeCells + 0.5f)
            {
                if (Clear(basic, target.Position)) { if (sim.Cast(c.Id, basic.Id, target.Position).Ok) return; }
                else { sim.Move(c.Id, Flank(sim, c, target, basic.RangeCells), dt); return; }   // a teammate is in the way: step around
            }
            if (mobility != null && mobility.Shape == AbilityShape.Dash && mobility.Damage > 0f && dist > (basic?.RangeCells ?? 1f) && dist <= mobility.RangeCells + 1f && Clear(mobility, target.Position) && sim.Cast(c.Id, mobility.Id, target.Position).Ok) return;
            if (basic == null || dist > basic.RangeCells || !sees) sim.Move(c.Id, basic != null ? Flank(sim, c, target, basic.RangeCells) : target.Position, dt);
        }
    }

    public sealed class FightReport
    {
        public int WinnerTeam = -1; public float Seconds; public Dictionary<int, float> DamageDealt = new Dictionary<int, float>(); public int Downs; public bool TimedOut;
        public override string ToString() => $"{(TimedOut ? "draw" : "team " + WinnerTeam)} in {Seconds:0.0}s, {Downs} downs";
    }

    /// <summary>
    /// Runs fights so the class numbers can be checked before anything is built in Unity: duels,
    /// team fights and a party against a boss, on an open arena or a real room, deterministic per seed.
    /// </summary>
    public static class FightLab
    {
        /// <summary>A bare rectangular room with a wall around it, for fights without a dungeon.</summary>
        public static DungeonLayout OpenArena(int width, int height, int seed = 1)
        {
            var l = new DungeonLayout(seed, width + 2, height + 2) { Settings = new DungeonSettings() };
            for (int x = 0; x < width + 2; x++) for (int y = 0; y < height + 2; y++) l.Tiles[x, y] = x == 0 || y == 0 || x == width + 1 || y == height + 1 ? TileType.Wall : TileType.RoomFloor;
            return l;
        }

        public static ClassArchetype ClassOf(string spec) => ClassArchetype.DefaultRoster.First(c => WeaponSpec.All.Any(w => w.ClassName == c.Name && w.Name == spec) || (c.IsElementalist && ElementKit.All.Any(k => k.Name == spec)));

        public static Combatant Player(int id, int team, string spec, int tier, Vec2 pos, CombatBaseline b = null)
        {
            var cls = ClassOf(spec);
            var stats = PlayerStats.For(cls, tier, b);
            return Combatant.Player(id, team, stats, pos, spec);
        }

        public static FightReport Run(FightSim sim, float maxSeconds, float dt = 0.1f)
        {
            var report = new FightReport();
            int downs = 0;
            while (sim.Time < maxSeconds)
            {
                foreach (var c in sim.Combatants.Values.OrderBy(c => c.Id).ToList()) SimpleBrain.Act(sim, c, dt);
                sim.Tick(dt);
                var standing = sim.Combatants.Values.Where(c => c.Alive).Select(c => c.Team).Distinct().ToList();
                if (standing.Count <= 1) { report.WinnerTeam = standing.Count == 1 ? standing[0] : -1; break; }
            }
            report.Seconds = sim.Time;
            report.TimedOut = sim.Combatants.Values.Where(c => c.Alive).Select(c => c.Team).Distinct().Count() > 1;
            report.Downs = sim.Events.Count(e => e.Kind == FightEventKind.Downed);
            foreach (var e in sim.Events.Where(e => e.Kind == FightEventKind.Hit && e.Source >= 0)) report.DamageDealt[e.Source] = (report.DamageDealt.TryGetValue(e.Source, out float d) ? d : 0f) + e.Amount;
            return report;
        }

        public static FightReport Duel(string specA, string specB, int tier, int seed, float maxSeconds = 120f)
        {
            var sim = new FightSim(OpenArena(20, 12), seed);
            sim.Add(Player(1, 0, specA, tier, new Vec2(4f, 6f)));
            sim.Add(Player(2, 1, specB, tier, new Vec2(17f, 6f)));
            return Run(sim, maxSeconds);
        }

        public static FightReport TeamFight(IReadOnlyList<string> teamA, IReadOnlyList<string> teamB, int tier, int seed, float maxSeconds = 180f)
        {
            var sim = new FightSim(OpenArena(24, 14), seed);
            for (int i = 0; i < teamA.Count; i++) sim.Add(Player(1 + i, 0, teamA[i], tier, new Vec2(4f, 4f + i * 3f)));
            for (int i = 0; i < teamB.Count; i++) sim.Add(Player(11 + i, 1, teamB[i], tier, new Vec2(21f, 4f + i * 3f)));
            return Run(sim, maxSeconds);
        }

        /// <summary>A party against a room boss built from the tuner's budget; the margin the tuner promised is what this checks.</summary>
        public static FightReport PartyVsBoss(IReadOnlyList<string> party, int tier, int seed, bool finalBoss = false, CombatBaseline b = null, float maxSeconds = 240f)
        {
            b = b ?? new CombatBaseline();
            var sim = new FightSim(OpenArena(16, 16), seed);
            for (int i = 0; i < party.Count; i++) sim.Add(Player(1 + i, 0, party[i], tier, new Vec2(3f + i * 2f, 3f), b));
            var budget = finalBoss ? EncounterTuner.FinalBoss(b) : EncounterTuner.RoomBoss(b);
            var arch = BossArchetype.DefaultPool[0];
            sim.Add(Combatant.Boss(99, 1, arch.Name, budget, arch.Damage, null, new Vec2(9f, 12f), StatusKind.Bleed));
            return Run(sim, maxSeconds);
        }

        /// <summary>Win rate of A over many seeds. 0.5 is balanced.</summary>
        public static (float winRateA, float avgSeconds, int draws) DuelSeries(string specA, string specB, int tier, int seeds)
        {
            int a = 0, draws = 0; float secs = 0f;
            for (int s = 1; s <= seeds; s++) { var r = Duel(specA, specB, tier, s); if (r.TimedOut) draws++; else if (r.WinnerTeam == 0) a++; secs += r.Seconds; }
            return (a / (float)Math.Max(1, seeds - draws), secs / seeds, draws);
        }
    }
}
