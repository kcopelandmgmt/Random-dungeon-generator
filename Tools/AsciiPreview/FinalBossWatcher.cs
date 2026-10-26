using System;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using DungeonGen.Core.Items;

namespace DungeonGen.Tools
{
    /// <summary>One team's try at the final boss inside one fight (the stretch from the boss engaging to it dying or resetting).</summary>
    public sealed class BossAttempt
    {
        public int Seed, Team;
        public float FightStart, FightEnd;
        /// <summary>Seconds this team had a member standing in the arena.</summary>
        public float Present;
        public int PeakInArena;
        public bool Full;          // three standing members in the arena at once
        public bool Kill, Wipe, Enraged;
        /// <summary>Damage this team's players dealt to the boss in the fight (the rest of a long stay in the arena is other teams, or nobody, being fought).</summary>
        public float DamageToBoss, BossMaxHealth;
        public int PhaseReached = 1;
        public float BossHealthAtEnd = 1f, MinBossHealth = 1f;
        public float FightLength => FightEnd - FightStart;
        /// <summary>A serious attempt: the full team of 3 was in the arena together for 20 s or more and hit the boss (at least 1% of its health).</summary>
        public bool Serious => Full && Present >= 20f && DamageToBoss >= 0.01f * BossMaxHealth;
        public string Outcome => Kill ? "KILL" : Wipe ? "wipe" : "retreat";
    }

    /// <summary>
    /// Watches the final boss during a headless round (a MatchServer.Ticked hook) and records every
    /// team's attempt on it: who engaged, for how long, how it ended, how far the boss went down.
    /// </summary>
    public sealed class FinalBossWatcher
    {
        public readonly int Seed;
        public readonly List<BossAttempt> Attempts = new List<BossAttempt>();
        public bool BossKilled; public float KilledAt = -1f; public int KillerTeam = -1;
        private readonly Dictionary<int, BossAttempt> _live = new Dictionary<int, BossAttempt>();
        private bool _inFight; private float _fightStart, _last = -1f;
        private bool _closedByDeath;
        /// <summary>Set to print a ten-second timeline of every fight on the final boss (damage to the boss, damage and heals on the players, who is standing).</summary>
        public Action<string> Trace;
        private int _evFrom, _dmgFrom; private float _nextTrace;
        private readonly Dictionary<int, float> _dmg = new Dictionary<int, float>();

        public FinalBossWatcher(int seed) { Seed = seed; }

        public void Attach(MatchServer server) { server.Ticked += OnTick; if (Environment.GetEnvironmentVariable("FB_DOOR") == "1") server.Ticked += DoorDiag; }

        /// <summary>FB_DOOR=1: every 10 s, each squad with someone at the final boss's door that has not committed: why not (standing, gathered, potions, health).</summary>
        private float _nextDoor;
        private void DoorDiag(MatchServer server)
        {
            if (server.Sim.Time < _nextDoor) return; _nextDoor = server.Sim.Time + 10f;
            var room = server.Layout.FinalBossRoom; if (room == null) return;
            var zone = room.Bounds.Expand(MobRules.LeashCells + 3);
            foreach (var grp in server.State.PlayerTeam.GroupBy(kv => kv.Value))
            {
                var bots = server.BotBrains.OfType<RoundBot>().Where(b => b.Squad.Team == grp.Key).ToList();
                if (bots.Count == 0 || bots[0].Squad.EngagedBoss >= 0) continue;
                var members = grp.Select(kv => server.Sim.Get(kv.Key)).Where(m => m != null && !m.Dead && !server.State.ExtractedPlayers.Contains(m.Id)).ToList();
                var near = members.Where(m => zone.Contains(m.Position.Cell)).ToList();
                if (near.Count == 0) continue;
                var pots = members.Select(m => server.PocketsOf(m.Id).TryGetValue(Consumable.HealthPotionName, out int p) ? p : 0).ToList();
                Trace?.Invoke($"  seed {Seed} {server.Sim.Time,4:0}s DOOR T{grp.Key} members {members.Count} alive {members.Count(m => m.Alive)} at door {near.Count} potions [{string.Join(",", pots)}] hp [{string.Join(" ", members.Select(m => (100f * m.Health / m.MaxHealth).ToString("0")))}] killedBosses {bots[0].Squad.BossesKilled}");
            }
        }

        private void OnTick(MatchServer server)
        {
            var ctrl = server.Director?.Bosses.FirstOrDefault(b => server.Layout.Rooms[b.RoomId].Type == RoomType.FinalBoss);
            if (ctrl == null) return;
            float now = server.Sim.Time, dt = _last < 0f ? 0f : now - _last; _last = now;
            bool fighting = ctrl.Engaged && !ctrl.Boss.Dead;
            if (fighting && !_inFight) { _inFight = true; _fightStart = now; _nextTrace = now; _dmg.Clear(); _evFrom = server.Sim.Events.Count; Trace?.Invoke($"  seed {Seed} FIGHT START at {now:0}s"); _live.Clear(); _closedByDeath = false; }
            if (_inFight)
            {
                var ev0 = server.Sim.Events;
                for (int i = _dmgFrom; i < ev0.Count; i++)
                    if (ev0[i].Kind == FightEventKind.Hit && ev0[i].Target == ctrl.Boss.Id && server.Sim.Get(ev0[i].Source) is Combatant src && !src.IsMob) _dmg[src.Team] = (_dmg.TryGetValue(src.Team, out float d0) ? d0 : 0f) + ev0[i].Amount;
            }
            if (_inFight && Trace != null)
                for (int i = _dmgFrom; i < server.Sim.Events.Count; i++)
                {
                    var e = server.Sim.Events[i];
                    if (e.Kind != FightEventKind.Downed || !(server.Sim.Get(e.Target) is Combatant dn) || dn.IsMob) continue;
                    var hits = server.Sim.Hits.LastHits(e.Target);
                    var recent = hits.Skip(Math.Max(0, hits.Count - 6)).Select(h => h.Ability + " " + h.Amount.ToString("0") + (h.Attacker == ctrl.Boss.Id ? "" : h.Attacker > 9000 ? "(add)" : h.Attacker < 0 ? "(env)" : "(ally/foe " + h.Attacker + ")"));
                    Trace($"  seed {Seed} {now - _fightStart,4:0}s DOWN {dn.Spec} T{dn.Team} at boss {100f * ctrl.HealthFraction:0}% last: {string.Join(", ", recent)}");
                }
            _dmgFrom = server.Sim.Events.Count;
            if (_inFight && Trace != null && now >= _nextTrace)
            {
                _nextTrace = now + 10f;
                var ev = server.Sim.Events; float toBoss = 0, bossOnP = 0, addsOnP = 0, otherOnP = 0, heal = 0;
                for (int i = _evFrom; i < ev.Count; i++)
                {
                    var e = ev[i];
                    if (e.Kind == FightEventKind.Hit)
                    {
                        if (e.Target == ctrl.Boss.Id) toBoss += e.Amount;
                        else if (server.Sim.Get(e.Target) is Combatant t && !t.IsMob) { if (e.Source == ctrl.Boss.Id) bossOnP += e.Amount; else if (e.Source > 9000) addsOnP += e.Amount; else otherOnP += e.Amount; }
                    }
                    else if (e.Kind == FightEventKind.Heal && e.Target != ctrl.Boss.Id) heal += e.Amount;
                }
                _evFrom = ev.Count;
                var teams = server.State.PlayerTeam.GroupBy(kv => kv.Value).Select(g => TeamLine(server, ctrl, g.Key, g.Select(kv => kv.Key))).Where(x => x != null);
                string enr = ctrl.Enraged ? " ENR" : "", who = string.Join(" ", teams);
                Trace($"  seed {Seed} {now - _fightStart,4:0}s boss {100f * ctrl.HealthFraction,3:0}% ph{ctrl.Phase}{enr}  to boss {toBoss:0}  boss->P {bossOnP:0} adds->P {addsOnP:0} other->P {otherOnP:0} healed {heal:0}  {who}");
            }
            if (_inFight)
            {
                var st = server.State;
                foreach (var grp in st.PlayerTeam.GroupBy(kv => kv.Value))
                {
                    var members = grp.Select(kv => server.Sim.Get(kv.Key)).Where(m => m != null).ToList();
                    int inArena = members.Count(m => m.Alive && ctrl.InArena(m.Position) && !st.ExtractedPlayers.Contains(m.Id));
                    if (inArena == 0 && !_live.ContainsKey(grp.Key)) continue;
                    if (!_live.TryGetValue(grp.Key, out var a)) _live[grp.Key] = a = new BossAttempt { Seed = Seed, Team = grp.Key, FightStart = _fightStart };
                    if (inArena > 0) a.Present += dt;
                    a.DamageToBoss = _dmg.TryGetValue(grp.Key, out float dealt) ? dealt : 0f;
                    a.PeakInArena = Math.Max(a.PeakInArena, inArena);
                    if (inArena >= 3) a.Full = true;
                    if (a.Full && members.Count(m => m.Alive) == 0) a.Wipe = true;     // a full team with nobody left standing
                    a.BossMaxHealth = ctrl.Boss.MaxHealth; a.PhaseReached = Math.Max(a.PhaseReached, ctrl.Phase);
                    a.Enraged |= ctrl.Enraged;
                    a.BossHealthAtEnd = ctrl.HealthFraction; a.MinBossHealth = Math.Min(a.MinBossHealth, ctrl.HealthFraction);
                }
            }
            if (ctrl.Boss.Dead && _inFight && !_closedByDeath)
            {
                _closedByDeath = true; BossKilled = true; KilledAt = now; KillerTeam = server.State.FinalBossKillerTeam ?? -1;
                foreach (var a in _live.Values) { a.FightEnd = now; a.BossHealthAtEnd = 0f; a.MinBossHealth = 0f; a.PhaseReached = Math.Max(a.PhaseReached, 3); if (a.Team == KillerTeam) a.Kill = true; Attempts.Add(a); }
                _live.Clear(); _inFight = false;
            }
            else if (_inFight && !fighting && !ctrl.Boss.Dead)
            {
                foreach (var a in _live.Values) { a.FightEnd = now; Attempts.Add(a); }
                _live.Clear(); _inFight = false;
            }
        }

        private static string TeamLine(MatchServer server, BossController ctrl, int team, IEnumerable<int> ids)
        {
            var parts = new List<string>();
            foreach (int id in ids)
            {
                var m = server.Sim.Get(id);
                if (m == null || !ctrl.InArena(m.Position)) continue;
                string hp = m.Dead ? "dead" : m.Downed ? "down" : (100f * m.Health / m.MaxHealth).ToString("0");
                parts.Add(hp + (m.Spec.Length > 3 ? m.Spec.Substring(0, 3) : m.Spec));
            }
            return parts.Count == 0 ? null : "T" + team + "[" + string.Join(" ", parts) + "]";
        }
    }
}
