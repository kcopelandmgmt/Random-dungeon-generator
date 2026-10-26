using System;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Items;
using DungeonGen.Core.Net;
using DungeonGen.Core.Profile;
using Xunit;

namespace DungeonGen.Tests
{
    /// <summary>The four gaps found while building settlement: disconnect deaths, the death trash, consumable effects, the kill-XP cap.</summary>
    public class RuleGapTests
    {
        private static ClassArchetype Cls(string name) => ClassArchetype.ByName(name);
        private static Consumable Potion => Consumable.ByName("Health Potion");
        private static Consumable Antidote => Consumable.ByName("Antidote");

        private static Loadout Kit(PlayerProfile p, Character ch, int potions = 2, int antidotes = 0, params Item[] carried)
        {
            var cons = new Dictionary<string, int>();
            if (potions > 0) cons["Health Potion"] = potions;
            if (antidotes > 0) { p.AddConsumable(Antidote, antidotes); cons["Antidote"] = antidotes; }
            foreach (var c in carried) if (!p.Stash.Contains(c)) p.Stash.Add(c);
            return Loadout.For(p, ch, carried, cons.Count > 0 ? cons : null);
        }

        /// <summary>Player 1 (the loadout) on team 0, a sentinel (player 2) on team 1, the doors open and the clock running.</summary>
        private static MatchServer Open(Loadout lo, int seed = 11, int secret = 5)
        {
            var server = MatchServer.Host(seed, DungeonSettings.ForTeams(2), new MatchRules { TeamSize = 3 }, lootSecret: secret);
            server.StartRound();
            Assert.NotNull(server.AddPlayerWithLoadout(1, 0, lo));
            server.AddPlayer(2, 1, Cls("Warden"), "Sentinel", PlayerStats.For(Cls("Warden"), 1));
            for (int i = 0; i < 200 && !server.State.DoorsOpen; i++) server.Step(0.1f);
            Assert.True(server.State.DoorsOpen);
            return server;
        }

        private static void Run(MatchServer s, float seconds, float dt = 0.1f) { for (float t = 0; t < seconds; t += dt) s.Step(dt); }

        // ---- Gap 1: a disconnect past the window is a death ----------------------------------------------------

        [Fact]
        public void ADisconnectPastTheWindowRaisesTheDeathAndLeavesACorpse()
        {
            var p = PlayerProfile.NewAccount("a", "Bram", Cls("Warden"));
            var s = Open(Kit(p, p.Characters[0]));
            var held = s.State.ItemOwner.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();
            Assert.NotEmpty(held);
            float at = s.Sim.Time;
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.PlayerDisconnected, at, 1)));
            Run(s, 55f);
            Assert.False(s.State.DeadPlayers.Contains(1), "still inside the 60 s window");
            Assert.True(s.Sim.Get(1).Alive);
            Run(s, 8f);
            Assert.Contains(1, s.State.DeadPlayers);
            Assert.Empty(s.State.Disconnected);
            var died = s.State.Log.Single(e => e.Kind == MatchEventKind.PlayerDied && e.A == 1);
            Assert.Equal(-1, died.B);                                                  // nobody killed them: the dungeon did
            Assert.True(died.Time > at + s.Descriptor.Rules.ReconnectWindowSeconds);
            Assert.True(s.Sim.Get(1).Dead);                                            // out of the simulator like any death
            Assert.True(s.State.Corpses.ContainsKey(1));                               // the corpse is there for anyone to loot
            var destroyed = s.State.Log.Where(e => e.Kind == MatchEventKind.ItemDestroyed).Select(e => e.A).ToList();
            Assert.Equal(held.Count, s.State.Corpses[1].Count + destroyed.Count);       // every item is on the corpse or trashed
            Assert.Equal(0, s.RejectedCount);
            Assert.Equal(RoundOutcome.Died, RoundSettlement.OutcomeOf(s.State, 1));
            Assert.Equal("Killed by the dungeon.", DeathRecap.For(s.State, 1, s.Sim.Hits).Headline);
        }

        [Fact]
        public void ReconnectingInTimeCancelsTheDeath()
        {
            var p = PlayerProfile.NewAccount("a", "Bram", Cls("Warden"));
            var s = Open(Kit(p, p.Characters[0]));
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.PlayerDisconnected, s.Sim.Time, 1)));
            Run(s, 40f);
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.PlayerReconnected, s.Sim.Time, 1)));
            Run(s, 40f);
            Assert.DoesNotContain(1, s.State.DeadPlayers);
            Assert.Equal(0, s.RejectedCount);
        }

        // ---- Gap 2: the death trash ------------------------------------------------------------------------------

        private static (MatchServer server, List<int> held) DieWithManyItems(int secret)
        {
            var p = PlayerProfile.NewAccount("a", "Bram", Cls("Warden"));
            var rng = new DungeonRandom(7);
            var carried = Enumerable.Range(0, 14).Select(i => LootRoller.Roll(0, LootSource.Chest, rng)).ToArray();
            var s = Open(Kit(p, p.Characters[0], 2, 0, carried), secret: secret);
            var held = s.State.ItemOwner.Where(kv => kv.Value == 1).Select(kv => kv.Key).OrderBy(i => i).ToList();
            Assert.True(held.Count >= 14);
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.PlayerDied, s.Sim.Time, 1, 2, s.RoomOf(1))));
            return (s, held);
        }

        [Fact]
        public void DeathTrashesRoughlyHalfTheCarriedItemsAndTheCorpseHoldsTheSurvivors()
        {
            var (s, held) = DieWithManyItems(5);
            var destroyed = s.State.Log.Where(e => e.Kind == MatchEventKind.ItemDestroyed).Select(e => e.A).ToList();
            Assert.Equal(destroyed.Count, destroyed.Distinct().Count());
            Assert.All(destroyed, id => Assert.Contains(id, held));
            Assert.True(destroyed.Count > 0 && destroyed.Count < held.Count, $"{destroyed.Count} of {held.Count} trashed");
            Assert.Equal(new HashSet<int>(held.Except(destroyed)), s.State.Corpses[1]);
            Assert.True(new HashSet<int>(destroyed).SetEquals(s.State.DestroyedItems));
            Assert.Equal(0, s.RejectedCount);
            // Worn and carried alike, starter gear included: nothing is exempt.
            Assert.Contains(held, id => s.ItemById(id).IsStarter);
            Assert.False(s.Raise(new MatchEvent(MatchEventKind.ItemTaken, destroyed[0], 2)), "a trashed item cannot be looted");
        }

        [Fact]
        public void TheTrashIsDeterministicPerMatchSecretAndDiffersBetweenSecrets()
        {
            var (a, _) = DieWithManyItems(5);
            var (b, _) = DieWithManyItems(5);
            var (c, _) = DieWithManyItems(6);
            Assert.True(a.State.DestroyedItems.SetEquals(b.State.DestroyedItems));
            Assert.False(a.State.DestroyedItems.SetEquals(c.State.DestroyedItems));
        }

        // ---- Gap 3: consumables ----------------------------------------------------------------------------------

        [Fact]
        public void TheIntentCarriesAConsumableSlotAppendedToTheWireAndOldBytesStillRead()
        {
            var i = new PlayerIntent { PlayerId = 3, Tick = 9, MoveX = 1, AimX = 4, Basic = true, Interact = true, PingKind = 2, ConsumableSlot = 2 };
            var bytes = i.ToBytes();
            var back = PlayerIntent.FromBytes(bytes);
            Assert.Equal(2, back.ConsumableSlot); Assert.Equal(2, back.PingKind); Assert.True(back.Basic && back.Interact); Assert.Equal(3, back.PlayerId);
            Assert.Equal(2, bytes[bytes.Length - 1]);                                  // appended last
            var legacy = PlayerIntent.FromBytes(bytes.Take(bytes.Length - 1).ToArray());   // the format before the field
            Assert.Equal(0, legacy.ConsumableSlot); Assert.Equal(2, legacy.PingKind); Assert.Equal(1f, legacy.MoveX);
        }

        [Fact]
        public void AHealthPotionHealsThirtyPercentOverThreeSecondsThroughTheSimulatorAndTheIntentPath()
        {
            var p = PlayerProfile.NewAccount("a", "Bram", Cls("Warden"));
            var s = Open(Kit(p, p.Characters[0]));
            var c = s.Sim.Get(1);
            c.Health = c.MaxHealth * 0.4f;
            Run(s, 5f);
            Assert.Equal(c.MaxHealth * 0.4f, c.Health, 2);                              // no natural regeneration
            s.SetIntent(1, new PlayerIntent { PlayerId = 1, Tick = s.Tick, ConsumableSlot = 1 });
            s.Step(0.1f);
            Assert.Equal(1, s.PocketsOf(1)["Health Potion"]);
            Run(s, 1.4f);
            Assert.InRange(c.Health, c.MaxHealth * 0.5f, c.MaxHealth * 0.69f);          // mid-pour
            Run(s, 2.6f);
            Assert.Equal(c.MaxHealth * 0.7f, c.Health, 1);
            var heals = s.Sim.Events.Where(e => e.Kind == FightEventKind.Heal && e.Target == 1 && e.Detail == "Health Potion").ToList();
            Assert.True(heals.Count >= 2);
            Assert.Equal(c.MaxHealth * 0.3f, heals.Sum(e => e.Amount), 1);
            Assert.Equal(1, s.ConsumablesUsedBy(1)["Health Potion"]);                  // the held slot did not drink again
            Assert.Equal(0, s.RejectedCount);
        }

        [Fact]
        public void APotionHasASixtySecondCooldownAndStopsWhenItsDrinkerFalls()
        {
            var p = PlayerProfile.NewAccount("a", "Bram", Cls("Warden"));
            var s = Open(Kit(p, p.Characters[0]));
            var c = s.Sim.Get(1);
            c.Health = c.MaxHealth * 0.2f;
            Assert.True(s.UseConsumable(1, Potion));
            Assert.False(s.UseConsumable(1, Potion), "on cooldown");
            Assert.Equal(1, s.PocketsOf(1)["Health Potion"]);                          // a refused use costs nothing
            Run(s, 58f);
            Assert.False(s.UseConsumable(1, Potion));
            Assert.InRange(s.ConsumableCooldownRemaining(1, Potion), 0.5f, 3f);
            Run(s, 3f);
            Assert.True(s.UseConsumable(1, Potion));
            Assert.False(s.PocketsOf(1).ContainsKey("Health Potion"));
            // Downed: the pour stops.
            c.Downed = true; c.Health = 0f;
            Run(s, 1f);
            Assert.Equal(0f, c.Health);
            Assert.Empty(s.Sim.HealsOverTime);
        }

        [Fact]
        public void AnAntidoteCleansesEveryStatusWithItsOwnCooldown()
        {
            var p = PlayerProfile.NewAccount("a", "Bram", Cls("Warden"));
            var s = Open(Kit(p, p.Characters[0], 2, 1));
            var c = s.Sim.Get(1);
            c.Status.Apply(StatusEffectDefinition.All[StatusKind.Burn], c.Resistances);
            c.Status.Apply(StatusEffectDefinition.All[StatusKind.Poisoned], c.Resistances);
            Assert.NotEmpty(c.Status.Active);
            s.SetIntent(1, new PlayerIntent { PlayerId = 1, Tick = s.Tick, ConsumableSlot = 2 });
            s.Step(0.1f);
            Assert.Empty(c.Status.Active);
            Assert.Contains(s.Sim.Events, e => e.Kind == FightEventKind.Cleansed && e.Target == 1 && e.Detail == "Antidote");
            Assert.False(s.PocketsOf(1).ContainsKey("Antidote"));
            Assert.Equal(2, s.PocketsOf(1)["Health Potion"]);                          // the potions are untouched
            Assert.True(s.ConsumableCooldownRemaining(1, Antidote) > 80f);
            Assert.Equal(0f, s.ConsumableCooldownRemaining(1, Potion));
        }

        [Fact]
        public void TheSalvageKitAndBadSlotsAreIgnoredByTheIntentPathAndTheDeadUseNothing()
        {
            var p = PlayerProfile.NewAccount("a", "Bram", Cls("Warden"));
            p.AddConsumable(Consumable.ByName("Salvage Kit"), 1);
            var s = Open(Loadout.For(p, p.Characters[0], null, new Dictionary<string, int> { ["Health Potion"] = 2, ["Salvage Kit"] = 1 }));
            s.SetIntent(1, new PlayerIntent { PlayerId = 1, Tick = s.Tick, ConsumableSlot = 3 });
            s.Step(0.1f);
            s.SetIntent(1, new PlayerIntent { PlayerId = 1, Tick = s.Tick, ConsumableSlot = 99 });
            s.Step(0.1f);
            Assert.Empty(s.ConsumablesUsedBy(1));
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.PlayerDied, s.Sim.Time, 1, -1, s.RoomOf(1))));
            Assert.False(s.UseConsumable(1, Potion));
        }

        // ---- Gap 4: the kill-XP cap at settlement ------------------------------------------------------------------

        private static SettlementReport KillAndExtract(PlayerProfile p, int seed, DateTime when, string victimAccount = "victim-acct", KillXpTracker tracker = null)
        {
            var server = MatchServer.Host(seed, DungeonSettings.ForTeams(2), new MatchRules { TeamSize = 3 }, lootSecret: 5);
            server.StartRound();
            Assert.NotNull(server.AddPlayerWithLoadout(1, 0, Kit(p, p.Characters[0], 0)));
            server.AddPlayer(2, 1, Cls("Warden"), "Sentinel", PlayerStats.For(Cls("Warden"), 1));
            float t = 0f;
            void Ok(MatchEventKind k, int a = 0, int b = 0, int c = 0) => Assert.True(server.Raise(new MatchEvent(k, t, a, b, c)), string.Join(" | ", server.RecentRejections));
            Ok(MatchEventKind.RoundStarted); t = 10f; Ok(MatchEventKind.DoorsOpened); t = 12f;
            Ok(MatchEventKind.PlayerDied, 2, 1, server.Layout.Rooms[0].Id);
            int gate = server.Layout.ExtractRooms.First().Id;
            Ok(MatchEventKind.ExtractsOpened); Ok(MatchEventKind.PlayerEnteredRoom, 1, gate);
            Ok(MatchEventKind.ExtractStarted, 1, gate); t += 8.5f; Ok(MatchEventKind.PlayerExtracted, 1, gate, 0);
            return RoundSettlement.Settle(p, p.Characters[0], server, 1, accountOf: id => id == 2 ? victimAccount : null, whenUtc: when, killXp: tracker);
        }

        [Fact]
        public void KillXpIsPaidOncePerVictimPerDayAtSettlement()
        {
            var p = PlayerProfile.NewAccount("killer", "Bram", Cls("Warden"));
            var day = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
            var first = KillAndExtract(p, 11, day);
            Assert.Equal(AccountRules.XpPerExtraction + AccountRules.XpPerPlayerKill, first.XpDelta);
            var second = KillAndExtract(p, 12, day.AddHours(3));                        // same victim, same day: the kill pays nothing
            Assert.Equal(1, second.Summary.PlayerKills);
            Assert.Equal(AccountRules.XpPerExtraction, second.XpDelta);
            var other = KillAndExtract(p, 13, day.AddHours(4), "someone-else");          // a different victim pays
            Assert.Equal(AccountRules.XpPerExtraction + AccountRules.XpPerPlayerKill, other.XpDelta);
            var nextDay = KillAndExtract(p, 14, day.AddHours(25));                      // 25 hours on: pays again
            Assert.Equal(AccountRules.XpPerExtraction + AccountRules.XpPerPlayerKill, nextDay.XpDelta);
            Assert.Equal(first.XpDelta + second.XpDelta + other.XpDelta + nextDay.XpDelta, p.AccountXp);
        }

        [Fact]
        public void AVictimWithNoKnownAccountIsPaidInFullAndNotRemembered()
        {
            var p = PlayerProfile.NewAccount("killer", "Bram", Cls("Warden"));
            var rep = KillAndExtract(p, 11, new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc), null);
            Assert.Equal(AccountRules.XpPerExtraction + AccountRules.XpPerPlayerKill, rep.XpDelta);
            Assert.Equal(0, p.KillXp.Count);
        }

        [Fact]
        public void AnExplicitTrackerOverridesTheProfilesOwn()
        {
            var p = PlayerProfile.NewAccount("killer", "Bram", Cls("Warden"));
            var day = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
            var tracker = new KillXpTracker();
            tracker.Award("killer", "victim-acct", day.AddHours(-1));
            var rep = KillAndExtract(p, 11, day, "victim-acct", tracker);
            Assert.Equal(AccountRules.XpPerExtraction, rep.XpDelta);
            Assert.Equal(0, p.KillXp.Count);                                            // the profile's own tracker was not touched
        }

        [Fact]
        public void TheTrackerSurvivesTheProfileCodecByteForByteAndVersionOneFilesStillLoad()
        {
            var p = PlayerProfile.NewAccount("killer", "Bram", Cls("Warden"));
            var day = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
            KillAndExtract(p, 11, day);
            Assert.Equal(1, p.KillXp.Count);
            var bytes = ProfileCodec.ToBytes(p);
            var back = ProfileCodec.FromBytes(bytes);
            Assert.Equal(p.KillXp.Entries.ToList(), back.KillXp.Entries.ToList());
            Assert.Equal(bytes, ProfileCodec.ToBytes(back));
            Assert.Equal(0, back.KillXp.Award("killer", "victim-acct", day.AddHours(2)));   // still remembered after a reload
            // A version-1 file: version 1 in the header, no tracker section at the end.
            var fresh = PlayerProfile.NewAccount("old", "Wren", Cls("Warden"));
            var v2 = ProfileCodec.ToBytes(fresh);
            var v1 = v2.Take(v2.Length - 4).ToArray();                                  // the empty tracker is one zero Int32
            v1[4] = 1;
            var old = ProfileCodec.FromBytes(v1);
            Assert.Equal("old", old.AccountId); Assert.Equal(0, old.KillXp.Count);
            Assert.Equal(AccountRules.XpPerPlayerKill, old.KillXp.Award("old", "x", day));
        }
    }
}
