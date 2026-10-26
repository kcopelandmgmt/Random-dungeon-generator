using System;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Items;
using DungeonGen.Core.Net;
using DungeonGen.Core.Profile;
using Xunit;
using Xunit.Abstractions;

namespace DungeonGen.Tests
{
    /// <summary>Wave 5.10, the tutorial: its dungeon, its round rules, the objective tracker, and a whole headless round walked by the scripted pilot.</summary>
    public class TutorialScriptTests
    {
        private readonly ITestOutputHelper _out;
        public TutorialScriptTests(ITestOutputHelper o) { _out = o; }

        [Fact]
        public void TheDungeonIsTheSmallestTheGeneratorAllows()
        {
            var s = TutorialScript.Settings();
            s.Validate();
            Assert.Equal(1, s.SpawnCount); Assert.Equal(1, s.BossCount); Assert.Equal(1, s.ExtractCount); Assert.Equal(0, s.VaultCount);
            Assert.Equal(s.SpawnCount + s.ExtractCount + s.BossCount + s.VaultCount + 3, s.TargetRoomCount);
            s.TargetRoomCount--;
            Assert.Throws<ArgumentException>(() => s.Validate());
            Assert.False(TutorialScript.Settings().HazardsEnabled);
            Assert.False(TutorialScript.Settings().BossAffixesEnabled);
            Assert.True(TutorialScript.Settings().Combat.RoomBossPlayerEquivalents < new CombatBaseline().RoomBossPlayerEquivalents);
        }

        [Fact]
        public void PickedSeedsTeachTheWholeLoop()
        {
            var seen = new HashSet<int>();
            foreach (int start in new[] { 1, 7, 100, 4242, 99991, 123456 })
            {
                int seed = TutorialScript.PickSeed(start);
                Assert.True(seed >= start);
                seen.Add(seed);
                var d = MatchDescriptor.Create(seed, TutorialScript.Settings(), TutorialScript.Rules(), out var layout);
                var route = TutorialScript.Plan(layout, out string why);
                Assert.True(route != null, why);
                Assert.Equal(6, layout.Rooms.Count);
                Assert.Single(d.TeamSpawnRooms);
                Assert.Equal(2, route.PackRooms.Count);
                Assert.All(route.PackRooms, r => Assert.InRange(layout.Rooms[r].Mobs.Count, 1, TutorialScript.MaxPackSize));
                Assert.True(layout.Rooms[route.ShrineRoom].HasShrine);
                Assert.NotEmpty(layout.Rooms[route.ChestRoom].Loot);
                Assert.NotEmpty(route.SpawnDoors);
                Assert.Equal(RoomType.Boss, layout.Rooms[route.BossRoom].Type);
                Assert.Equal(RoomType.Extract, layout.Rooms[route.ExtractRoom].Type);
                // Nothing on the way runs through the final boss's room, and nothing before the boss lesson through the boss's.
                foreach (int r in route.PackRooms.Append(route.ShrineRoom).Append(route.ChestRoom))
                    Assert.True(TutorialScript.Reachable(layout, route.SpawnRoom, r, new[] { route.BossRoom, route.FinalBossRoom }));
                Assert.True(TutorialScript.Reachable(layout, route.SpawnRoom, route.ExtractRoom, new[] { route.FinalBossRoom }));
                Assert.Equal(DungeonEventKind.None, d.Event(layout).Kind);
                MatchDescriptor.FromBytes(d.ToBytes()).BuildAndVerify();   // a client could build the same dungeon
            }
            Assert.Equal(TutorialScript.PickSeed(7), TutorialScript.PickSeed(7));   // deterministic
        }

        [Fact]
        public void RulesAreATutorialsAndTheExtractFollowsTheBoss()
        {
            var r = TutorialScript.Rules();
            Assert.True(r.IsTutorial); Assert.False(r.MidRoundEvent); Assert.Equal(3, r.TeamSize); Assert.Equal(0, r.BotTeams);
            Assert.True(r.ExtractsOpenAfterSeconds < r.RoundSeconds);
            var other = new MatchRules();
            TutorialScript.ApplyRules(other);
            Assert.True(other.IsTutorial); Assert.Equal(r.ExtractsOpenAfterSeconds, other.ExtractsOpenAfterSeconds); Assert.False(other.MidRoundEvent);

            int seed = TutorialScript.PickSeed(11);
            var d = MatchDescriptor.Create(seed, TutorialScript.Settings(), r, out var layout);
            var st = new MatchState(d, layout);
            var route = TutorialScript.Plan(layout, out _);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.RoundStarted, 0f), out _));
            Assert.False(TutorialScript.ExtractsDue(st, 300f));
            Assert.Equal(TutorialScript.ExtractsFallbackSeconds, TutorialScript.ExtractOpensAt(st));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.BossKilled, 200f, route.BossRoom, 0), out string why), why);
            Assert.Equal(200f, TutorialScript.BossKilledAt(st));
            Assert.False(TutorialScript.ExtractsDue(st, 259f));
            Assert.True(TutorialScript.ExtractsDue(st, 260f));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ExtractsOpened, 260f), out why), why);
            Assert.False(TutorialScript.ExtractsDue(st, 300f));
            Assert.True(TutorialScript.ExtractsDue(NewState(seed, out _), TutorialScript.ExtractsFallbackSeconds), "the fallback opens it with the boss alive");
        }

        private static MatchState NewState(int seed, out TutorialRoute route, MatchRules rules = null)
        {
            var d = MatchDescriptor.Create(seed, TutorialScript.Settings(), rules ?? TutorialScript.Rules(), out var layout);
            route = TutorialScript.Plan(layout, out _);
            var st = new MatchState(d, layout);
            st.Apply(new MatchEvent(MatchEventKind.RoundStarted, 0f), out _);
            return st;
        }

        [Fact]
        public void AClearedPackPaysASoulOnlyInATutorial()
        {
            int seed = TutorialScript.PickSeed(21);
            foreach (bool tutorial in new[] { true, false })
            {
                var rules = tutorial ? TutorialScript.Rules() : new MatchRules();
                var st = NewState(seed, out var route, rules);
                string why;
                Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0f, 1, 0, 0), out why), why);
                Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0f, 2, 0, 0), out why), why);   // a teammate left at the spawn
                int pack = route.PackRooms[0];
                Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 1f, 1, pack), out why), why);
                Assert.True(st.Apply(new MatchEvent(MatchEventKind.MobPackCleared, 2f, LayoutIndex.MobPackIdBase + pack), out why), why);
                Assert.Equal(tutorial ? TutorialScript.SoulsPerPack : 0, st.SoulsOf(1));
                bool spawnNextDoor = st.Index.RoomsWithin(pack, 1).Contains(route.SpawnRoom);
                Assert.Equal(tutorial && spawnNextDoor ? TutorialScript.SoulsPerPack : 0, st.SoulsOf(2));   // only players at the fight (its room or next door)
            }
        }

        [Fact]
        public void TheTrackerAdvancesOnRealEventsAndLatchesOutOfOrder()
        {
            int seed = TutorialScript.PickSeed(31);
            var st = NewState(seed, out var route);
            var t = new TutorialTracker(1, st.Layout);
            Assert.Equal(2, t.PackCount);
            Assert.Equal(TutorialStep.Move, t.Step);
            TeamSnapshot Snap(float x, float y, float time, float load = 0f) => new TeamSnapshot
            {
                Time = time,
                Avatars = { new AvatarSnapshot { Id = 1, X = x, Y = y, HealthFraction = 1f } },
                Selves = { new SelfSnapshot { PlayerId = 1, Items = new SelfItems { LoadKg = load, CapacityKg = 40f } } },
            };
            Assert.False(t.Observe(Snap(10f, 10f, 0f)));
            Assert.False(t.Observe(Snap(10.5f, 10.5f, 1f)));
            Assert.True(t.Observe(Snap(12.5f, 10f, 2f)));
            Assert.Equal(TutorialStep.OpenDoor, t.Step);
            Assert.False(t.Observe(new MatchEvent(MatchEventKind.DoorOpened, 3f, route.SpawnDoors[0], 900)), "a bot opening the door is not the lesson");
            Assert.True(t.Observe(new MatchEvent(MatchEventKind.DoorOpened, 4f, route.SpawnDoors[0], 1)));
            Assert.Equal(TutorialStep.KillMobs, t.Step);
            Assert.Contains("0 / 2", t.Progress);
            // Out of order: the chest before the second pack is fine; it is already ticked off when the packs are done.
            Assert.False(t.Observe(new MatchEvent(MatchEventKind.MobPackCleared, 30f, LayoutIndex.MobPackIdBase + route.PackRooms[0])));
            Assert.Contains("1 / 2", t.Progress);
            Assert.False(t.Observe(new MatchEvent(MatchEventKind.ItemTaken, 40f, 3, 1)));
            Assert.False(t.Observe(new MatchEvent(MatchEventKind.ItemTaken, 41f, MatchState.BroughtItemIdBase + 2, 1)), "brought gear is not loot");
            Assert.True(t.Observe(new MatchEvent(MatchEventKind.MobPackCleared, 50f, LayoutIndex.MobPackIdBase + route.PackRooms[1])));
            Assert.Equal(TutorialStep.ArmUltimate, t.Step);
            Assert.True(t.IsDone(TutorialStep.LootChest));
            t.Observe(Snap(30f, 30f, 51f, 52f));
            Assert.Equal(52f, t.LoadKg); Assert.Equal(40f, t.CapacityKg);
            Assert.False(t.Observe(new MatchEvent(MatchEventKind.UltimateArmed, 60f, 900, route.ShrineRoom)), "a bot's prayer is not the player's");
            Assert.True(t.Observe(new MatchEvent(MatchEventKind.UltimateArmed, 61f, 1, route.ShrineRoom)));
            Assert.Equal(TutorialStep.KillBoss, t.Step);
            Assert.Equal("Ultimate armed: ready", t.Progress);
            t.Observe(new MatchEvent(MatchEventKind.UltimateUsed, 70f, 1));
            Assert.Equal("Ultimate unleashed", t.Progress);
            if (route.FinalBossRoom >= 0) Assert.False(t.Observe(new MatchEvent(MatchEventKind.BossKilled, 75f, route.FinalBossRoom, 0)), "the final boss is not the lesson");
            Assert.True(t.Observe(new MatchEvent(MatchEventKind.BossKilled, 80f, route.BossRoom, 0)));
            Assert.Equal(TutorialStep.Extract, t.Step);
            t.Observe(Snap(30f, 30f, 100f));
            Assert.Equal(40f, t.ExtractOpensIn.Value, 3);
            Assert.Equal("The extract opens in 0:40", t.Progress);
            t.Observe(new MatchEvent(MatchEventKind.ExtractsOpened, 140f));
            Assert.Equal("The extract is open", t.Progress);
            Assert.Equal(6, t.LessonsDone);
            Assert.True(t.Observe(new MatchEvent(MatchEventKind.PlayerExtracted, 150f, 1, route.ExtractRoom, 0)));
            Assert.Equal(TutorialStep.Done, t.Step);
            Assert.Equal(7, t.LessonsDone);
            Assert.True(t.Finished);

            // A death ends it.
            var dead = new TutorialTracker(1, st.Layout);
            Assert.True(dead.Observe(new MatchEvent(MatchEventKind.PlayerDied, 5f, 1, -1, 0)));
            Assert.Equal(TutorialStep.Failed, dead.Step);
            Assert.False(dead.Observe(new MatchEvent(MatchEventKind.PlayerDied, 5f, 2, -1, 0)));

            // The state alone (a missed event) also moves it: souls enough to arm count as the mob lesson.
            var fromState = new TutorialTracker(1, st.Layout);
            st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0f, 1, 0, 0), out _);
            st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 1f, 1, route.PackRooms[0]), out _);
            st.Apply(new MatchEvent(MatchEventKind.MobPackCleared, 2f, LayoutIndex.MobPackIdBase + route.PackRooms[0]), out _);
            fromState.Observe(st);
            Assert.Equal(1, fromState.Souls);
            Assert.Equal(1, fromState.PacksCleared);
        }

        [Fact]
        public void CardTextShowsTheBoundKeys()
        {
            var keys = new Dictionary<string, string> { ["Interact"] = "Space", ["Ultimate"] = "R", ["MoveUp"] = "W", ["MoveLeft"] = "A", ["MoveDown"] = "S", ["MoveRight"] = "D" };
            Assert.Equal("Hold Space here", TutorialScript.Format("Hold {Interact} here", k => keys.TryGetValue(k, out var v) ? v : "?"));
            Assert.Equal("Hold G here", TutorialScript.Format("Hold {Interact} here", k => k == "Interact" ? "G" : "?"));   // a rebind shows
            Assert.Equal("{Nope} stays", TutorialScript.Format("{Nope} stays", k => "X"));
            Assert.Equal("open { brace", TutorialScript.Format("open { brace", k => "X"));
            foreach (var o in TutorialScript.Objectives)
                foreach (var text in new[] { o.Text, o.Hint })
                {
                    string filled = TutorialScript.Format(text, k => "KEY");
                    Assert.DoesNotContain("{", filled);
                }
            Assert.Equal(7, TutorialScript.Lessons.Length);
            Assert.Contains("Space", TutorialScript.Format(TutorialScript.Objective(TutorialStep.OpenDoor).Text, k => keys.TryGetValue(k, out var v) ? v : "?"));
        }

        /// <summary>The whole round, headless: the player (a starter-kit Warden from a throwaway profile, as the game seats it), two followers, the director, the pilot.</summary>
        [Fact]
        public void APilotWalksTheWholeTutorialAndExtracts()
        {
            foreach (int start in new[] { 3, 50, 777, 2024, 31337, 9001, 424242, 65000 })
            {
                var (server, tracker, director, order, seconds) = RunTutorial(start);
                _out.WriteLine($"seed {server.Descriptor.Seed}: done in {seconds:0} s, order {string.Join(" > ", order)}, rejected {server.RejectedCount}: {string.Join(" | ", server.RecentRejections)}");
                if (tracker.Step != TutorialStep.Done) foreach (var e in server.State.Log.Where(e => e.Kind != MatchEventKind.RoomSeen && e.Kind != MatchEventKind.PlayerEnteredRoom).Reverse().Take(25).Reverse()) _out.WriteLine("  " + e);
                Assert.Equal(TutorialStep.Done, tracker.Step);
                Assert.Equal(TutorialScript.Lessons.Append(TutorialStep.Done), order);
                Assert.Contains(1, server.State.ExtractedPlayers);
                Assert.True(tracker.UltimateUsed, "the ultimate was cast on the boss");
                Assert.DoesNotContain(director.Route.FinalBossRoom, server.State.DeadBosses);   // nobody wandered into the final boss
                Assert.Equal(2, director.BotIds.Count);
                Assert.All(director.BotIds, b => Assert.Equal(0, server.TeamOf(b)));
                Assert.DoesNotContain(server.State.PlayerTeam, kv => kv.Value != 0);   // no enemy team
                Assert.Equal(0, server.RejectedCount);
                Assert.InRange(seconds, 60f, 8 * 60f);
            }
        }

        internal static (MatchServer server, TutorialTracker tracker, TutorialDirector director, List<TutorialStep> order, float seconds) RunTutorial(int start)
        {
            int seed = TutorialScript.PickSeed(start);
            var server = MatchServer.Host(seed, TutorialScript.Settings(), TutorialScript.Rules(), lootSecret: 5);
            server.StartRound();
            var temp = PlayerProfile.NewAccount("tutorial", "Tutor", ClassArchetype.ByName("Warden"));
            var loadout = Loadout.For(temp, temp.Characters[0], null, new Dictionary<string, int> { ["Health Potion"] = 2 });
            Assert.NotNull(server.AddPlayerWithLoadout(1, 0, loadout));
            var director = new TutorialDirector(server, 1);
            director.Begin();
            Assert.All(director.Route.SpawnDoors, d => Assert.Contains(server.State.Index.Doors[d].cell, server.State.ClosedDoors));
            var tracker = new TutorialTracker(1, server.Layout);
            server.EventRaised += e => tracker.Observe(e);
            var pilot = new TutorialPilot(server, 1, director.Route, tracker);
            var order = new List<TutorialStep> { tracker.Step };
            const float dt = 0.05f;
            float t = 0f;
            while (t < 15 * 60f && !tracker.Finished)
            {
                server.SetIntent(1, pilot.Next(dt));
                server.Step(dt);
                t += dt;
                tracker.Observe(server.SnapshotForTeam(0));
                tracker.Observe(server.State);
                if (tracker.Step != order[order.Count - 1]) order.Add(tracker.Step);
            }
            return (server, tracker, director, order, t);
        }
    }
}
