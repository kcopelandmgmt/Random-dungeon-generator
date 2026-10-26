using System;
using System.Linq;
using DungeonGen.Core;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using Xunit;

namespace DungeonGen.Tests
{
    /// <summary>
    /// Wave 5.6: the Sovereign Below, the final boss, was never killed (0 kills in 96 bot rounds, 0 of 30 duel-lab fights): its health was tuned for 55% of a party's sheet
    /// damage and its mechanics let a party land about a third, its flood pools never drained, and bot squads almost never went in. These pin the retune: a committed,
    /// decently geared trio wins a good share of fights, a thin or starter-geared party mostly loses, a kill is never quick, and the squads actually walk in.
    /// The numbers behind them are in the wave-5.6 report (`AsciiPreview --sim round`, FB_RUSH=0 for forced attempts).
    /// </summary>
    public class FinalBossTuningTests
    {
        private static readonly string[] Reference = { "Sentinel", "Berserker", "Warder" };   // Warden / Reaver / Mender

        private static (int wins, int fights, float min, float max, float mean) Duels(string[] party, int tier, int seeds)
        {
            int wins = 0; float min = float.MaxValue, max = 0f, sum = 0f;
            for (int s = 1; s <= seeds; s++)
            {
                var r = FightLab.BossFight(BossArchetype.FinalBoss.Name, party, tier, s, 300f);
                if (r.WinnerTeam != 0 || r.TimedOut) continue;
                wins++; min = Math.Min(min, r.Seconds); max = Math.Max(max, r.Seconds); sum += r.Seconds;
            }
            return (wins, seeds, wins == 0 ? 0f : min, max, wins == 0 ? 0f : sum / wins);
        }

        [Fact]
        public void AGearedTrioKillsTheSovereignInTheDuelLab()
        {
            var d = Duels(Reference, 2, 24);
            Assert.InRange(d.wins, 12, 23);                  // measured 19 of 24: it can be killed, and it is not a formality
            Assert.InRange(d.mean, 40f, 100f);              // about the 90 s the tuner aims for (measured 54 s with a lab brain that wastes nothing)
        }

        [Fact]
        public void ANoKillIsQuickEvenForAGearedTrio()
        {
            var d = Duels(Reference, 3, 24);
            Assert.True(d.wins > 0);
            Assert.True(d.min >= 28f, $"the fastest kill took {d.min:0} s: a geared trio must not delete the final boss");   // measured 34 s at tier 3
            Assert.True(d.max <= 160f, $"the slowest kill took {d.max:0} s");
        }

        [Fact]
        public void ItStaysAThreatTheTrioLosesSometimesAndStarterGearMostly()
        {
            var geared = Duels(Reference, 2, 24);
            Assert.True(geared.fights - geared.wins >= 2, "a geared trio should wipe now and then");       // measured 5 wipes in 24
            var starter = Duels(Reference, 0, 24);
            Assert.True(starter.wins <= 12, $"starter gear won {starter.wins} of 24");                     // measured 4 of 24
        }

        [Fact]
        public void ThinPartiesCannotKillIt()
        {
            Assert.Equal(0, Duels(new[] { "Berserker", "Sharpshooter" }, 2, 12).wins);                      // two decently geared players cannot
            Assert.Equal(0, Duels(new[] { "Berserker" }, 4, 12).wins);                                      // nor can one very well geared one
            var trio = Duels(Reference, 2, 12).wins;
            Assert.True(Duels(new[] { "Berserker", "Sentinel" }, 2, 12).wins < trio, "a pair does worse than the full party");
        }

        [Fact]
        public void TheBudgetIsHalfTheTunedHealthAndTheFloodDrains()
        {
            var layout = DungeonGenerator.Generate(DungeonSettings.ForTeams(4), 7);
            var room = layout.FinalBossRoom;
            var tuned = EncounterTuner.FinalBoss(new CombatBaseline());
            Assert.InRange(room.Boss.Budget.Health / tuned.Health, 0.4f, 0.6f);
            Assert.InRange(room.Boss.Budget.Dps / tuned.Dps, 0.55f, 0.95f);
            Assert.True(BossController.SovereignLavaSeconds <= 60f, "flood pools used to last 600 s, a fight's floor became lava with nowhere left to stand");
        }

        [Fact]
        public void BotSquadsGoInWithAPotionEachAndMostOfTheirHealth()
        {
            var squad = new BotSquad();
            Assert.True(squad.FinalBossPotionsEach <= 1);     // it was two: of 62 squads that reached the door whole, one qualified
            Assert.True(squad.FinalReadyHealth <= 0.9f);      // it was 0.95, above the 0.85 at which bots drink
            Assert.True(squad.FinalBossPotionsEach >= 1, "never walk in with nothing to drink");
        }

        /// <summary>A squad set down at the final boss's door with its goal set to it (what the console tool's FB_RUSH does) takes it on and, within a few rounds, kills it.</summary>
        [Fact]
        public void ABotTrioAtTheDoorKillsItWithinAFewRounds()
        {
            int kills = 0, rounds = 0, rejected = 0;
            for (int seed = 1; seed <= 10 && kills == 0; seed++)
            {
                var r = RoundLab.Play(seed, 4, 3, 2, configure: Rush);
                rounds++; rejected += r.Rejected;
                if (r.FinalBossKilled) kills++;
            }
            Assert.True(kills >= 1, $"no kill in {rounds} rounds");
            Assert.Equal(0, rejected);
        }

        private static void Rush(MatchServer sv)
        {
            bool done = false;
            sv.Ticked += srv =>
            {
                if (done || !srv.State.DoorsOpen) return;
                done = true;
                var room = srv.Layout.FinalBossRoom;
                var door = room.Doors.First();
                var outside = GridPoint.Cardinals.Select(c => door + c).First(p => !room.Bounds.Contains(p) && srv.Layout.IsWalkable(p));
                int n = 0;
                foreach (var bb in srv.BotBrains.OfType<RoundBot>().Where(b => b.Squad.Team == 0))
                {
                    var c = srv.Sim.Get(bb.PlayerId);
                    c.Position = Vec2.Of(outside) + new Vec2((n++ % 3) * 0.1f, 0f);
                    bb.Squad.BossesKilled = Math.Max(1, bb.Squad.BossesKilled); bb.Squad.GoalRoom = room.Id; bb.Squad.Resting = false;
                }
            };
        }
    }
}
