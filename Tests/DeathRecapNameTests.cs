using System.Linq;
using DungeonGen.Core;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using Xunit;

namespace DungeonGen.Tests
{
    /// <summary>The death recap names the monster or boss that killed you ("Killed by the Skitterer."), not "a player (Mob)".</summary>
    public class DeathRecapNameTests
    {
        private const int Victim = 4, MobId = 900;

        private static (MatchState st, int room) Round(bool finishedWhileDowned, int killerId)
        {
            var d = MatchDescriptor.Create(7, DungeonSettings.ForTeams(8), new MatchRules(), out var layout);
            var st = new MatchState(d, layout);
            st.Apply(new MatchEvent(MatchEventKind.RoundStarted, 0), out _);
            for (int p = 1; p <= 6; p++) st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, p, (p - 1) / 3), out _);
            int room = layout.Rooms.First(r => r.Type == RoomType.Standard).Id;
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDowned, 5f, Victim, killerId, room), out string why), why);
            Assert.True(st.Apply(new MatchEvent(finishedWhileDowned ? MatchEventKind.PlayerFinished : MatchEventKind.PlayerDied, 9f, Victim, killerId, room), out why), why);
            return (st, room);
        }

        private static HitLog MobHits()
        {
            var hits = new HitLog();
            hits.Record(Victim, new Hit(4f, MobId, "Mob", DamageType.Poison, 31f, "Mob"));
            hits.Record(Victim, new Hit(8f, MobId, "Mob", DamageType.Poison, 12f, "Mob"));
            return hits;
        }

        [Theory]
        [InlineData("Skitterer", "Finished by the Skitterer.")]
        [InlineData("Hollow Knight", "Finished by the Hollow Knight.")]
        [InlineData("Iron Juggernaut", "Finished by the Iron Juggernaut.")]
        [InlineData("The Sovereign Below", "Finished by The Sovereign Below.")]
        public void AMonsterKillerIsNamedNotCalledAPlayer(string name, string headline)
        {
            var (st, _) = Round(true, MobId);
            var recap = DeathRecap.For(st, Victim, MobHits(), id => id == MobId ? name : null);
            Assert.Equal(name, recap.KillerName);
            Assert.False(recap.KillerIsPlayer);
            Assert.Null(recap.KillerSpec);                       // "Mob" is a tag inside the sim, not something to show
            Assert.Equal(headline, recap.Headline);
            Assert.DoesNotContain("player", recap.Headline);
            Assert.DoesNotContain("Mob", recap.Headline);
        }

        [Fact]
        public void AMonsterThatKilledWithoutAnyHitOnFileIsStillNamed()
        {
            var (st, _) = Round(false, MobId);
            Assert.Equal("Killed by the Bandit.", DeathRecap.For(st, Victim, new HitLog(), id => "Bandit").Headline);
        }

        [Fact]
        public void WithoutANameTheHeadlineSaysAMonsterNeverAPlayer()
        {
            var (st, _) = Round(true, MobId);
            var recap = DeathRecap.For(st, Victim, MobHits());
            Assert.Null(recap.KillerName);
            Assert.Equal("Finished by a monster.", recap.Headline);
        }

        [Fact]
        public void APlayerKillerStillReadsAsAClassAndSpec()
        {
            var (st, _) = Round(true, 1);
            var hits = new HitLog();
            hits.Record(Victim, new Hit(8f, 1, "Duelist", DamageType.Physical, 20f, "Duelist.basic"));
            var recap = DeathRecap.For(st, Victim, hits, id => "Skitterer");   // the resolver is for monsters only
            Assert.True(recap.KillerIsPlayer);
            Assert.Null(recap.KillerName);
            Assert.StartsWith("Finished by a ", recap.Headline);
            Assert.Contains("(Duelist)", recap.Headline);
        }

        [Fact]
        public void TheDungeonAndTheClockStillHaveNoKiller()
        {
            var (st, _) = Round(false, -1);
            Assert.Equal("Killed by the dungeon.", DeathRecap.For(st, Victim, new HitLog(), id => "Skitterer").Headline);
        }

        [Fact]
        public void TheMonsterNameSurvivesTheWire()
        {
            var (st, _) = Round(true, MobId);
            var recap = DeathRecap.For(st, Victim, MobHits(), id => "Skitterer");
            var back = DeathRecap.FromBytes(recap.ToBytes());
            Assert.Equal("Skitterer", back.KillerName);
            Assert.False(back.KillerIsPlayer);
            Assert.Equal(recap.Headline, back.Headline);
            Assert.Equal("Finished by the Skitterer.", back.Headline);
        }
    }
}
