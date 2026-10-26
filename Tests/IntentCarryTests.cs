using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using DungeonGen.Core.Profile;
using Xunit;

namespace DungeonGen.Tests
{
    /// <summary>One-press choices survive a newer intent that arrives before the server ticks, and still happen only once.</summary>
    public class IntentCarryTests
    {
        [Fact]
        public void APotionPressSurvivesANewerIntentAndIsUsedOnce()
        {
            var warden = ClassArchetype.DefaultRoster.First(c => c.Name == "Warden");
            var p = PlayerProfile.NewAccount("a", "Bram", warden);
            var lo = Loadout.For(p, p.Characters[0], null, new Dictionary<string, int> { ["Health Potion"] = 2 });
            var server = MatchServer.Host(11, DungeonSettings.ForTeams(2), new MatchRules { TeamSize = 3 }, lootSecret: 5);
            server.StartRound();
            Assert.NotNull(server.AddPlayerWithLoadout(1, 0, lo));
            for (int i = 0; i < 200 && !server.State.DoorsOpen; i++) server.Step(0.1f);
            var me = server.Sim.Get(1);
            me.Health = me.MaxHealth * 0.5f;

            server.SetIntent(1, new PlayerIntent { PlayerId = 1, Tick = server.Tick, ConsumableSlot = 1 });   // press 1
            server.SetIntent(1, new PlayerIntent { PlayerId = 1, Tick = server.Tick });                       // a newer intent without it, before the tick
            server.Step(0.1f);
            Assert.Equal(1, server.ConsumablesUsedBy(1)["Health Potion"]);

            for (int i = 0; i < 10; i++) { server.SetIntent(1, new PlayerIntent { PlayerId = 1, Tick = server.Tick }); server.Step(0.1f); }
            Assert.Equal(1, server.ConsumablesUsedBy(1)["Health Potion"]);                                  // not drunk twice
            Assert.Equal(0, server.RejectedCount);
        }
    }
}
