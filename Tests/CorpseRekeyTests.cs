using System.Linq;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using DungeonGen.Core;
using Xunit;

namespace DungeonGen.Tests
{
    public class CorpseRekeyTests
    {
        [Fact]
        public void LootingABodyWhoseOwnerIsResurrectedFinishesOnTheRemains()
        {
            var d = MatchDescriptor.Create(7, DungeonSettings.ForTeams(8), new MatchRules(), out var layout);
            var st = new MatchState(d, layout);
            string why;
            st.Apply(new MatchEvent(MatchEventKind.RoundStarted, 0), out _);
            for (int p = 1; p <= 3; p++) st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, p, 0), out _);
            int room = layout.Rooms[0].Id;
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 20, 2, -1, room), out why), why);
            Assert.True(st.Corpses.ContainsKey(2));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.LootStarted, 21, 3, 2, 1), out why), why);   // player 3 starts looting 2's body

            st.Souls[1] = 2;
            int shrine = layout.Rooms.First(r => r.HasShrine).Id;
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerResurrected, 22, 2, 1, shrine), out why), why);
            int remains = MatchState.RemainsOf(2);
            Assert.Equal((remains, 1), (st.Loots[3].target, st.Loots[3].kind));   // the channel follows the body to its new key

            Assert.True(st.Apply(new MatchEvent(MatchEventKind.CorpseOpened, 21 + FightRules.CorpseLootSeconds, remains, 3), out why), why);
            Assert.Contains(remains, st.OpenedCorpses);
        }
    }
}
