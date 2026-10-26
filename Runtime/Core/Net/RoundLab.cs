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
        public int EventCount;
        public override string ToString() =>
            $"seed {Seed,5}: {Seconds,6:0}s  out {Extracted}/{Players}  dead {Died}  inside {StillInside}  pk {PlayerKills}  bosses {BossKills}{(FinalBossKilled ? "+final" : "")}  items {ItemsExtracted}  value {ValueExtracted}  looted {RoomsLooted}  souls {Souls}  events {EventCount}  rejected {Rejected}";
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
                var squad = new BotSquad { Team = t };
                for (int b = 0; b < perTeam; b++) { server.AddBot(id, t, specs[(t * perTeam + b) % specs.Length], tier, new RoundBot(id, squad)); id++; }
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
            foreach (var e in st.Log)
            {
                if (e.Kind != MatchEventKind.PlayerDied && e.Kind != MatchEventKind.PlayerFinished) continue;
                var downed = st.Log.LastOrDefault(d => d.Kind == MatchEventKind.PlayerDowned && d.A == e.A && d.Time <= e.Time);
                int by = e.B >= 0 ? e.B : downed.Kind == MatchEventKind.PlayerDowned ? downed.B : -1;
                var killer = server.Sim.Get(by);
                string who = by < 0 ? "hazard or clock" : killer == null ? by.ToString() : killer.IsMob ? (IsBossId(by) ? "boss " + killer.Name : by >= (EncounterDirector.FirstBossId - EncounterDirector.MaxBosses + 1) * 100 ? "boss add " + killer.Name : "mob " + killer.Name) : "player " + killer.Spec;
                string how = e.Kind == MatchEventKind.PlayerFinished ? "finished" : e.B >= 0 ? "killed" : downed.Kind == MatchEventKind.PlayerDowned ? "bled out" : e.Time >= rules.RoundSeconds ? "clock" : "died";
                string room = e.C >= 0 && e.C < st.Layout.Rooms.Count ? st.Layout.Rooms[e.C].Type.ToString() : "?";
                report.DeathCauses.Add($"{how} | {who} | {room}");
            }
            foreach (int p in st.PlayerTeam.Keys.OrderBy(p => p))
            {
                var s = server.SummaryFor(p);
                report.Summaries.Add(s);
                report.PlayerKills += s.PlayerKills; report.ItemsExtracted += s.ItemsExtracted; report.ValueExtracted += s.ExtractedValue; report.Souls += s.SoulsEarned;
            }
            return report;
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
