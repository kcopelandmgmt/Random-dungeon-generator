using System;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Combat;

namespace DungeonGen.Core.Net
{
    /// <summary>What one headless round came to.</summary>
    public sealed class RoundReport
    {
        public int Seed; public int Teams; public int Players; public float Seconds; public int Ticks;
        public int Extracted, Died, StillInside, PlayerKills, BossKills, ItemsExtracted, RoomsLooted, Souls;
        public bool FinalBossKilled; public long ValueExtracted; public int Rejected; public List<string> Rejections = new List<string>();
        public List<RoundSummary> Summaries = new List<RoundSummary>();
        /// <summary>One line per death: how it happened, by whom, in what kind of room.</summary>
        public List<string> DeathCauses = new List<string>();
        /// <summary>When each death in <see cref="DeathCauses"/> happened (round seconds), same order.</summary>
        public List<float> DeathTimes = new List<float>();
        public int EventCount;
        /// <summary>The round's mid-round event (none when the dungeon has none), whether any bot engaged it (hit the elite, opened the unsealed vault, resurrected at the cheap shrine), and whether it was seen through (elite killed, vault opened in its minute, relic taken).</summary>
        public DungeonEventKind EventKind; public bool EventEngaged, EventDone, RelicTaken; public float EliteKilledAt = -1f; public int EliteKilledByTeam = -1;
        public override string ToString() =>
            $"seed {Seed,5}: {Seconds,6:0}s  out {Extracted}/{Players}  dead {Died}  inside {StillInside}  pk {PlayerKills}  bosses {BossKills}{(FinalBossKilled ? "+final" : "")}  items {ItemsExtracted}  value {ValueExtracted}  looted {RoomsLooted}  souls {Souls}  events {EventCount}  rejected {Rejected}{EventText()}";
        public string EventText() => EventKind == DungeonEventKind.None ? "" : "  event " + EventKind + " engaged " + EventEngaged + " done " + EventDone + (EliteKilledAt >= 0 ? " killed " + EliteKilledAt.ToString("0") + "s by T" + EliteKilledByTeam + " relic " + (RelicTaken ? "taken" : "left") : "");
    }

    /// <summary>
    /// Plays whole rounds with bot teams on the real server loop, no engine, no wire. The round state,
    /// the simulator, the brains, the clock, the intents and the summary all run as they will on the
    /// dedicated server; what this proves there is nothing to re-prove in Unity. Use it to find
    /// round-flow bugs, to see whether rounds end the way the design expects, and for soak runs.
    /// </summary>
    public static class RoundLab
    {
        public static readonly string[] DefaultSpecs = { "Sentinel", "Sharpshooter", "Warder", "Berserker", "Breaker", "Volleyer", "Plaguebearer", "Duelist" };

        public static RoundReport Play(int seed, int teams = 4, int perTeam = 3, int tier = 1, float dt = 0.1f, DungeonSettings settings = null, MatchRules rules = null, string[] specs = null, Action<MatchServer> configure = null)
        {
            settings = settings ?? DungeonSettings.ForTeams(teams);
            rules = rules ?? new MatchRules { TeamSize = perTeam };
            specs = specs ?? DefaultSpecs;
            var server = MatchServer.Host(seed, settings, rules, lootSecret: seed * 7919);
            server.StartRound();
            int id = BotRules.BotIdBase;
            int teamCount = server.Descriptor.TeamSpawnRooms.Count;
            for (int t = 0; t < teamCount; t++)
            {
                var squad = new BotSquad { Team = t, LeaveWithSecondsLeft = new BotSquad().LeaveWithSecondsLeft - 10 * ((t * 3) % Math.Max(1, teamCount)) };   // staggered, so the squads that wait it out do not all reach the gates in one tick
                for (int b = 0; b < perTeam; b++) { server.AddBot(id, t, specs[(t * perTeam + b) % specs.Length], tier, BotRules.DefaultConsumables(tier), new RoundBot(id, squad)); id++; }
            }
            configure?.Invoke(server);
            var report = new RoundReport { Seed = seed, Teams = teamCount, Players = server.State.PlayerTeam.Count };
            float limit = rules.RoundSeconds + 5f;
            while (!server.Ended && server.Elapsed < limit)
            {
                server.Step(dt);
                if (!server.State.PlayersInside.Any()) break;
            }
            report.Seconds = server.Elapsed; report.Ticks = (int)server.Tick;
            var st = server.State;
            report.Extracted = st.ExtractedPlayers.Count; report.Died = st.DeadPlayers.Count; report.StillInside = st.PlayersInside.Count();
            report.FinalBossKilled = st.Layout.FinalBossRoom != null && st.DeadBosses.Contains(st.Layout.FinalBossRoom.Id);
            report.BossKills = st.DeadBosses.Count(r => st.Layout.Rooms[r].Type == RoomType.Boss);
            report.RoomsLooted = st.OpenedContainers.Count;
            report.Rejected = server.RejectedCount; report.Rejections = server.RecentRejections.ToList();
            report.EventCount = st.Log.Count;
            ReportEvent(server, report);
            foreach (var e in st.Log)
            {
                if (e.Kind != MatchEventKind.PlayerDied && e.Kind != MatchEventKind.PlayerFinished) continue;
                var downed = st.Log.LastOrDefault(d => d.Kind == MatchEventKind.PlayerDowned && d.A == e.A && d.Time <= e.Time);
                int by = e.B >= 0 ? e.B : downed.Kind == MatchEventKind.PlayerDowned ? downed.B : -1;
                var killer = server.Sim.Get(by);
                string who = by < 0 ? "hazard or clock" : killer == null ? by.ToString() : killer.IsMob ? (IsBossId(by) ? "boss " + killer.Name : by >= (EncounterDirector.FirstBossId - EncounterDirector.MaxBosses + 1) * 100 ? "boss add " + killer.Name : "mob " + killer.Name) : "player " + killer.Spec;
                string how = e.Kind == MatchEventKind.PlayerFinished ? "finished" : e.B >= 0 ? "killed" : downed.Kind == MatchEventKind.PlayerDowned ? "bled out" : e.Time >= rules.RoundSeconds ? "clock" : "died";
                string room = e.C >= 0 && e.C < st.Layout.Rooms.Count ? st.Layout.Rooms[e.C].Type.ToString() : "?";
                report.DeathCauses.Add($"{how} | {who} | {room}"); report.DeathTimes.Add(e.Time);
            }
            foreach (int p in st.PlayerTeam.Keys.OrderBy(p => p))
            {
                var s = server.SummaryFor(p);
                report.Summaries.Add(s);
                report.PlayerKills += s.PlayerKills; report.ItemsExtracted += s.ItemsExtracted; report.ValueExtracted += s.ExtractedValue; report.Souls += s.SoulsEarned;
            }
            return report;
        }

        private static void ReportEvent(MatchServer server, RoundReport report)
        {
            var st = server.State; var plan = server.Clock.Plan;
            report.EventKind = st.ActiveEvent;
            if (st.ActiveEvent == DungeonEventKind.None) return;
            switch (st.ActiveEvent)
            {
                case DungeonEventKind.RoamingElite:
                    report.EventEngaged = server.Sim.Events.Any(e => e.Kind == FightEventKind.Hit && e.Target == EliteRules.CombatantId && e.Source >= 0 && server.Sim.Get(e.Source) is Combatant c && !c.IsMob);
                    var killed = st.Log.FirstOrDefault(e => e.Kind == MatchEventKind.EliteKilled);
                    if (killed.Kind == MatchEventKind.EliteKilled) { report.EventDone = true; report.EliteKilledAt = killed.Time; report.EliteKilledByTeam = killed.B; }
                    report.RelicTaken = st.Log.Any(e => e.Kind == MatchEventKind.ItemTaken && e.A == LayoutIndex.EventLootId);
                    break;
                case DungeonEventKind.VaultUnsealed:
                    report.EventDone = report.EventEngaged = st.Log.Any(e => e.Kind == MatchEventKind.VaultOpened && e.A == st.EventRoom && e.Time <= plan.EndsAt + 0.5f);
                    break;
                case DungeonEventKind.CheapShrine:
                    report.EventDone = report.EventEngaged = st.Log.Any(e => e.Kind == MatchEventKind.PlayerResurrected && e.Time >= plan.AtSeconds && e.Time <= plan.EndsAt + 0.5f);
                    break;
            }
        }

        /// <summary>Boss combatants count down from <see cref="EncounterDirector.FirstBossId"/>; their adds are numbered id × 100 + n.</summary>
        public static bool IsBossId(int id) => id <= EncounterDirector.FirstBossId && id > EncounterDirector.FirstBossId - EncounterDirector.MaxBosses;

        public static List<RoundReport> PlayMany(int firstSeed, int count, int teams = 4, int perTeam = 3, int tier = 1, float dt = 0.1f)
        {
            var list = new List<RoundReport>();
            for (int s = firstSeed; s < firstSeed + count; s++) list.Add(Play(s, teams, perTeam, tier, dt));
            return list;
        }
    }
}
