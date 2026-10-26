using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using Xunit;

namespace DungeonGen.Tests
{
    /// <summary>
    /// The mid-round event on the headless server (task 3.9): the roaming elite appears at the halfway mark as a real combatant, roams, and drops its relic where it
    /// falls; the unsealed vault opens without a key for its minute; the cheap shrine's minute is priced by the state. Deterministic per seed.
    /// </summary>
    public class MidRoundEventTests
    {
        private static readonly MatchRules Rules = new MatchRules { TeamSize = 3 };

        private static DungeonEventPlan PlanOf(int seed)
        {
            var d = MatchDescriptor.Create(seed, DungeonSettings.ForTeams(4), Rules, out var layout, lootSecret: seed * 7919);
            return d.Event(layout);
        }

        private static int SeedFor(DungeonEventKind kind) { for (int s = 1; s < 400; s++) if (PlanOf(s).Kind == kind) return s; throw new System.InvalidOperationException("no seed with " + kind); }

        /// <summary>A server on the seed with one idle player in a spawn (so the round is not empty), started and run to <paramref name="until"/> seconds.</summary>
        private static MatchServer Serve(int seed, float until, int playerId = 901, int team = 1, bool withFighter = false)
        {
            var server = MatchServer.Host(seed, DungeonSettings.ForTeams(4), Rules, lootSecret: seed * 7919);
            server.StartRound();
            var cls = FightLab.ClassOf("Sentinel");
            server.AddPlayer(playerId, team, cls, "Sentinel", PlayerStats.For(cls, 1, server.Descriptor.Settings.Combat));
            if (withFighter) server.AddBot(BotRules.BotIdBase, 0, "Berserker", 1, BotRules.DefaultConsumables(1));
            while (server.Elapsed < until) server.Step(0.25f);
            return server;
        }

        private static void Teleport(MatchServer server, int playerId, Vec2 to) { server.Sim.Get(playerId).Position = to; server.Step(0.05f); server.Step(0.05f); }

        [Fact]
        public void TheEventStartsAtTheHalfwayMarkAndOnlyThen()
        {
            int seed = SeedFor(DungeonEventKind.RoamingElite);
            var plan = PlanOf(seed);
            Assert.Equal(Rules.RoundSeconds / 2f, plan.AtSeconds);
            var server = Serve(seed, plan.AtSeconds - 5f);
            Assert.Equal(DungeonEventKind.None, server.State.ActiveEvent);
            Assert.Null(server.Elite);
            Assert.Null(server.Sim.Get(EliteRules.CombatantId));
            while (server.Elapsed < plan.AtSeconds + 1f) server.Step(0.25f);
            Assert.Equal(DungeonEventKind.RoamingElite, server.State.ActiveEvent);
            Assert.Equal(plan.RoomId, server.State.EventRoom);
            Assert.NotNull(server.Elite);
            var elite = server.Sim.Get(EliteRules.CombatantId);
            Assert.True(elite.IsMob && elite.Alive);
            Assert.True(elite.MaxHealth > 2000f, "an elite is several players of health");
            Assert.Equal(1, server.State.Log.Count(e => e.Kind == MatchEventKind.EventStarted));
            var started = server.State.Log.First(e => e.Kind == MatchEventKind.EventStarted);
            Assert.NotNull(Announcer.For(started, server.Layout));
            Assert.Equal(0, server.RejectedCount);
        }

        [Fact]
        public void TheEliteRoamsBetweenRoomsAndKeepsOutOfSpawnAndBossRooms()
        {
            int seed = SeedFor(DungeonEventKind.RoamingElite);
            var server = Serve(seed, 601f);
            var visited = new HashSet<int> { server.Elite.RoomId };
            while (server.Elapsed < 700f) { server.Step(0.25f); if (server.Elite != null) visited.Add(server.Elite.RoomId); }
            Assert.True(visited.Count >= 3, "it walks on from the room it appeared in: " + string.Join(",", visited));
            Assert.DoesNotContain(visited, r => server.Layout.Rooms[r].Type == RoomType.Spawn);
            Assert.DoesNotContain(visited, r => server.Layout.Rooms[r].IsBossRoom);
            Assert.Equal(server.Elite.RoomId, server.State.EliteRoom);   // the state follows it
            Assert.Equal(0, server.RejectedCount);
        }

        [Fact]
        public void TheEliteFallsAndItsRelicLiesOnTheFloorForWhoeverStandsThere()
        {
            int seed = SeedFor(DungeonEventKind.RoamingElite);
            var server = Serve(seed, 601f, withFighter: true);
            int killer = BotRules.BotIdBase;
            // A fighter on the elite's doorstep, the elite already hurt: the real combat path does the rest.
            var elite = server.Elite.Mob;
            elite.Health = 120f;
            var spot = elite.Position + new Vec2(1f, 0f);
            server.Sim.Get(killer).Position = server.Sim.Walkable(spot) ? spot : elite.Position - new Vec2(1f, 0f);
            for (int i = 0; i < 400 && !server.State.EliteDead; i++) server.Step(0.1f);
            Assert.True(server.State.EliteDead, "the elite fell");
            Assert.True(server.State.EventOver);
            Assert.Null(server.Elite);
            var dead = server.State.Log.Single(e => e.Kind == MatchEventKind.EliteKilled);
            Assert.Equal(0, dead.B);   // the killer's team
            Assert.Equal(dead.A, server.State.FloorItems[LayoutIndex.EventLootId]);
            Assert.NotNull(server.ItemById(LayoutIndex.EventLootId));
            // Another team's player walks in and takes it through the ordinary item path.
            var room = server.Layout.Rooms[dead.A];
            Teleport(server, 901, Vec2.Of(room.Center));
            Assert.Null(server.TakeRefusal(901, LayoutIndex.EventLootId));
            server.SetIntent(901, new PlayerIntent { PlayerId = 901, TakeItemId = LayoutIndex.EventLootId });
            server.Step(0.1f);
            Assert.Equal(901, server.State.ItemOwner[LayoutIndex.EventLootId]);
            Assert.Equal(0, server.RejectedCount);
        }

        [Fact]
        public void TheStateOnlyAcceptsTheEliteKilledWhereItWalksNow()
        {
            int seed = SeedFor(DungeonEventKind.RoamingElite);
            var server = Serve(seed, 601f);
            var st = server.State;
            int appeared = st.EventRoom;
            int elsewhere = server.Layout.Rooms.First(r => r.Id != st.EliteRoom && r.Id != appeared).Id;
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.EliteKilled, 610, elsewhere, 1), out _), "wrong room");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.EliteMoved, 611, elsewhere), out _));
            Assert.Equal(elsewhere, st.EliteRoom);
            Assert.Equal(appeared, st.EventRoom);   // what clients are told is the room it appeared in
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.EliteKilled, 612, elsewhere, 1), out _));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.EliteMoved, 613, elsewhere), out _), "a dead elite does not move");
        }

        [Fact]
        public void TheUnsealedVaultOpensWithoutAKeyInsideItsMinuteAndNotBeforeOrAfter()
        {
            int seed = SeedFor(DungeonEventKind.VaultUnsealed);
            var plan = PlanOf(seed);
            Assert.True(plan.Timed);
            // Before the event: the lock holds.
            var early = Serve(seed, plan.AtSeconds - 30f);
            var vault = early.Layout.Rooms[plan.RoomId];
            Teleport(early, 901, Vec2.Of(vault.Center));
            early.SetIntent(901, new PlayerIntent { PlayerId = 901, Interact = true });
            for (int i = 0; i < 6; i++) early.Step(0.1f);
            Assert.DoesNotContain(plan.RoomId, early.State.OpenVaults);
            // Inside the minute: Space opens it, no key held.
            var open = Serve(seed, plan.AtSeconds + 5f);
            Assert.Empty(open.State.KeyHolder.Where(kv => kv.Value == 901));
            Teleport(open, 901, Vec2.Of(vault.Center));
            Assert.True(open.VaultUnsealedAt(plan.RoomId));
            Assert.Equal("vault", open.InteractionAvailable(901));
            open.SetIntent(901, new PlayerIntent { PlayerId = 901, Interact = true });
            for (int i = 0; i < 4; i++) open.Step(0.1f);
            Assert.Contains(plan.RoomId, open.State.OpenVaults);
            Assert.Equal(0, open.RejectedCount);
            // After it: sealed again.
            var late = Serve(seed, plan.EndsAt + 5f);
            Assert.True(late.State.EventOver);
            Assert.False(late.VaultUnsealedAt(plan.RoomId));
            Teleport(late, 901, Vec2.Of(vault.Center));
            Assert.NotEqual("vault", late.InteractionAvailable(901));
            late.SetIntent(901, new PlayerIntent { PlayerId = 901, Interact = true });
            for (int i = 0; i < 6; i++) late.Step(0.1f);
            Assert.DoesNotContain(plan.RoomId, late.State.OpenVaults);
        }

        [Fact]
        public void TheCheapShrineIsCheapOnlyInsideItsMinute()
        {
            int seed = SeedFor(DungeonEventKind.CheapShrine);
            var plan = PlanOf(seed);
            var inside = Serve(seed, plan.AtSeconds + 5f);
            Assert.Equal(1, inside.State.ResurrectionCostAt(plan.RoomId));
            Assert.True(Rules.ResurrectionCost > 1);
            var after = Serve(seed, plan.EndsAt + 5f);
            Assert.Equal(Rules.ResurrectionCost, after.State.ResurrectionCostAt(plan.RoomId));
            Assert.Null(after.Elite);
        }

        [Fact]
        public void TheSameSeedPlaysTheSameEvent()
        {
            int seed = SeedFor(DungeonEventKind.RoamingElite);
            string Trace()
            {
                var server = Serve(seed, 601f);
                var rooms = new List<int>();
                while (server.Elapsed < 760f) { server.Step(0.25f); if (server.Elite != null && (rooms.Count == 0 || rooms.Last() != server.Elite.RoomId)) rooms.Add(server.Elite.RoomId); }
                return string.Join(",", rooms) + "|" + string.Join(";", server.State.Log.Where(e => e.Kind >= MatchEventKind.EventStarted).Select(e => e.ToString()));
            }
            Assert.Equal(Trace(), Trace());
        }

        [Fact]
        public void RoundsWithAnEventStillRunCleanAndSomeSquadsGoForIt()
        {
            int engaged = 0, killed = 0, withEvent = 0;
            for (int seed = 100; seed < 109; seed++)
            {
                var r = RoundLab.Play(seed, 4, 3);
                Assert.Equal(0, r.Rejected);
                if (r.EventKind == DungeonEventKind.None) continue;
                withEvent++;
                if (r.EventEngaged) engaged++;
                if (r.EventKind == DungeonEventKind.RoamingElite && r.EventDone) killed++;
            }
            Assert.True(withEvent >= 4);
            Assert.True(engaged >= 1, "a bot squad took on the event in nine rounds");
            Assert.True(killed >= 1);
        }
    }
}
