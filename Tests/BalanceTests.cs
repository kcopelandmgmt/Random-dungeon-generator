using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using Xunit;

namespace DungeonGen.Tests
{
    /// <summary>
    /// Balance guard rails (2026-10-26). Whole rounds of bots on the real server loop must keep the
    /// safety properties the balance pass established: a boss fights in its own arena (a chaser a little
    /// further, never into a spawn room), nothing is ever rejected, and a fair share of players get out.
    /// The numbers are floors, not targets: the round lab (`--sim round`) is where the targets are read.
    /// </summary>
    public class BalanceTests
    {
        private sealed class Watch
        {
            public readonly List<string> SpawnDowns = new List<string>();
            public readonly List<string> FarDowns = new List<string>();
            public int BossDowns;
        }

        private static bool IsBossOrAdd(int id) => RoundLab.IsBossId(id) || id >= (EncounterDirector.FirstBossId - EncounterDirector.MaxBosses + 1) * 100;

        private static RoundReport PlayWatched(int seed, int tier, Watch w) => RoundLab.Play(seed, 4, 3, tier, configure: server =>
        {
            server.EventRaised += e =>
            {
                if (e.Kind != MatchEventKind.PlayerDowned || !IsBossOrAdd(e.B)) return;
                int bossId = RoundLab.IsBossId(e.B) ? e.B : e.B / 100;
                var boss = server.Director.Bosses.FirstOrDefault(b => b.Boss.Id == bossId);
                var victim = server.Sim.Get(e.A);
                if (boss == null || victim == null) return;
                w.BossDowns++;
                var layout = server.Layout;
                var cell = victim.Position.Cell;
                int room = layout.InBounds(cell) ? layout.RoomIdAt[cell.X, cell.Y] : -1;
                string where = $"seed {seed}: {boss.Instance.DisplayName} (room {boss.RoomId}) downed {e.A} in room {room} at {victim.Position}";
                if (room >= 0 && layout.Rooms[room].Type == RoomType.Spawn) w.SpawnDowns.Add(where);
                // Its own room, or the corridor at its door, plus a swing's reach; a chaser may run its quarry a few rooms further.
                bool chaser = boss.Archetype.Family == BossFamily.Chaser;
                int reach = MobRules.LeashCells + 3 + (chaser ? BossController.ChaseCells : 0);
                bool near = layout.Rooms[boss.RoomId].Bounds.Expand(reach).Contains(cell) && (chaser || room < 0 || room == boss.RoomId);
                if (!near) w.FarDowns.Add(where);
            };
        });

        [Fact]
        public void BossesFightInTheirArenaNeverInASpawnAndRoundsStayClean()
        {
            var w = new Watch();
            var reports = new[] { 1, 2, 3, 4 }.Select(seed => PlayWatched(seed, 2, w)).ToList();
            Assert.All(reports, r => Assert.True(r.Rejected == 0, $"seed {r.Seed}: {string.Join(" / ", r.Rejections)}"));
            Assert.Empty(w.SpawnDowns);
            Assert.True(w.FarDowns.Count == 0, string.Join("\n", w.FarDowns));
            int players = reports.Sum(r => r.Players), extracted = reports.Sum(r => r.Extracted);
            Assert.True(extracted >= players / 4, $"extraction fell to {extracted}/{players} (the balance pass measured ~45% at tier 2)");
            Assert.True(reports.Sum(r => r.BossKills) >= 1, "nobody killed a room boss in four rounds");
            Assert.True(reports.Average(r => r.Seconds) >= 180f, "rounds end before the extracts open");
        }

        [Fact]
        public void ABossIgnoresPlayersOutsideItsArenaAndResetsWhenTheRoomEmpties()
        {
            var s = DungeonSettings.ForTeams(4); s.AllowedBosses = new[] { "Plague Herald" }; s.BossCount = 1;
            var layout = DungeonGenerator.Generate(s, 3);
            var room = layout.BossRooms.First();
            var sim = new FightSim(layout, 3);
            var director = new EncounterDirector(sim, layout);
            var boss = director.StationBoss(room);
            var spawn = layout.SpawnRooms.First();
            var p = sim.Add(FightLab.Player(1, 0, "Sentinel", 2, Vec2.Of(spawn.Center)));
            // A player across the dungeon: no target, no enrage clock, no Miasma under their feet.
            for (int i = 0; i < 300; i++) director.Step(0.1f);
            Assert.False(boss.Engaged);
            Assert.Equal(p.MaxHealth, p.Health);
            Assert.DoesNotContain(sim.Zones, z => z.Caster == boss.Boss.Id);
            // Walk in: the fight starts, and the enrage clock with it.
            p.Position = Vec2.Of(room.Center) + new Vec2(1.5f, 0f);
            for (int i = 0; i < 100; i++) { p.Health = p.MaxHealth; director.Step(0.1f); }
            Assert.True(boss.Engaged);
            Assert.False(boss.Enraged, "enrage counts from the start of the fight, not of the round");
            boss.Boss.Health = boss.Boss.MaxHealth * 0.5f;
            // Leave: after the reset delay the boss is whole again and its clouds are gone.
            p.Position = Vec2.Of(spawn.Center);
            for (int i = 0; i < (int)((BossController.ResetAfterSeconds + 2f) / 0.1f); i++) director.Step(0.1f);
            Assert.False(boss.Engaged);
            Assert.Equal(boss.Boss.MaxHealth, boss.Boss.Health);
            Assert.DoesNotContain(sim.Zones, z => z.Caster == boss.Boss.Id);
        }

        [Fact]
        public void TwoOfAPackDyingOnOneTickClearTheRoomOnce()
        {
            var layout = DungeonGenerator.Generate(DungeonSettings.ForTeams(4), 7);
            var room = layout.Rooms.First(r => r.Mobs != null && r.Mobs.Count >= 2);
            var sim = new FightSim(layout, 7);
            var director = new EncounterDirector(sim, layout);
            director.StationPack(room);
            foreach (var m in director.Mobs) { m.Mob.Dead = true; sim.Emit(FightEventKind.Died, -1, m.Mob.Id, 0f, "test"); }
            var cleared = director.Drain(0, _ => room.Id).Where(e => e.Kind == MatchEventKind.MobPackCleared).ToList();
            Assert.Single(cleared);
        }
    }
}
