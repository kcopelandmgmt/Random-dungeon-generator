using System.Linq;
using DungeonGen.Core;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using Xunit;

namespace DungeonGen.Tests
{
    /// <summary>Every boss room has its own boss in a real round, and killing any of them is a BossKilled for its room.</summary>
    public class MultiBossTests
    {
        [Fact]
        public void EveryBossRoomGetsItsOwnBossWithADistinctId()
        {
            var server = MatchServer.Host(11, DungeonSettings.ForTeams(4), new MatchRules { TeamSize = 3 }, lootSecret: 5);
            server.StartRound();
            var bossRooms = server.Layout.Rooms.Where(r => r.Boss != null).ToList();
            Assert.True(bossRooms.Count >= 2, "a four-team dungeon has room bosses and the final boss");
            Assert.Equal(bossRooms.Count, server.Director.Bosses.Count);
            var ids = server.Director.Bosses.Select(b => b.Boss.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
            foreach (var b in server.Director.Bosses) Assert.Same(b.Boss, server.Sim.Get(b.Boss.Id));     // none overwritten in the simulator
            Assert.Equal(EncounterDirector.FirstBossId, ids[0]);
        }

        [Fact]
        public void KillingAnyBossRaisesBossKilledForItsOwnRoom()
        {
            var server = MatchServer.Host(11, DungeonSettings.ForTeams(4), new MatchRules { TeamSize = 3 }, lootSecret: 5);
            server.StartRound();
            var warden = ClassArchetype.DefaultRoster.First(c => c.Name == "Warden");
            server.AddPlayer(1, 0, warden, "Sentinel", PlayerStats.For(warden, 1));
            foreach (var boss in server.Director.Bosses)
            {
                var b = boss.Boss;
                b.Health = 1f;
                server.Sim.Strike(server.Sim.Get(1), b, AbilityBook.Get("Sentinel.basic"));
                server.Step(0.05f);
                Assert.True(b.Dead, $"{b.Name} should be dead");
                Assert.Contains(server.State.Log, e => e.Kind == MatchEventKind.BossKilled && e.A == boss.RoomId);
                Assert.Contains(boss.RoomId, server.State.DeadBosses);
            }
            var final = server.Layout.Rooms.First(r => r.Type == RoomType.FinalBoss);
            Assert.Contains(final.Id, server.State.DeadBosses);
        }
    }
}
