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
    /// <summary>
    /// What a remote client needs on the wire (2026-10-26 online play): its pockets, consumable cooldowns, spec and item screens in the
    /// <see cref="SelfSnapshot"/>, item details in the <see cref="TeamView"/>, the death recap, the <see cref="JoinRequest"/> that carries its
    /// loadout to the server, and the <see cref="RoundResult"/> it settles its own profile from. The test of record: a client settling from the
    /// result ends with the byte-identical profile the host's own settlement produces.
    /// </summary>
    public class NetworkWireTests
    {
        private static ClassArchetype Cls(string name) => ClassArchetype.ByName(name);
        private static Consumable Potion => Consumable.ByName("Health Potion");

        private static PlayerProfile Clone(PlayerProfile p) => ProfileCodec.FromBytes(ProfileCodec.ToBytes(p));

        private static Loadout Kit(PlayerProfile p, int potions = 2, params Item[] carried) =>
            Loadout.For(p, p.Characters[0], carried, potions > 0 ? new Dictionary<string, int> { ["Health Potion"] = potions } : null);

        private sealed class Round
        {
            public MatchServer Server; public Room Chest; public int Gate; public float T;
            public void Ok(MatchEventKind k, int a = 0, int b = 0, int c = 0) => Assert.True(Server.Raise(new MatchEvent(k, T, a, b, c)), string.Join(" | ", Server.RecentRejections));
            public int OpenChestAndTake(int player)
            {
                Ok(MatchEventKind.PlayerEnteredRoom, player, Chest.Id);
                Ok(MatchEventKind.LootStarted, player, Chest.Id, 0); T += 2.5f;
                Ok(MatchEventKind.ContainerOpened, Chest.Id, player);
                int item = Server.State.VisibleItemsIn(Chest.Id).First();
                Ok(MatchEventKind.ItemTaken, item, player);
                if (Chest.Silver > 0) Ok(MatchEventKind.SilverPicked, player, Chest.Id);
                T += 1f;
                return item;
            }
            public void Extract(int player)
            {
                if (!Server.State.ExtractsOpen) Ok(MatchEventKind.ExtractsOpened);
                Ok(MatchEventKind.PlayerEnteredRoom, player, Gate);
                Ok(MatchEventKind.ExtractStarted, player, Gate); T += 8.5f;
                Ok(MatchEventKind.PlayerExtracted, player, Gate, Server.TeamOf(player));
            }
        }

        private static Round Start(Loadout one, Loadout two)
        {
            var server = MatchServer.Host(11, DungeonSettings.ForTeams(2), new MatchRules { TeamSize = 3 }, lootSecret: 5);
            server.StartRound();
            Assert.NotNull(server.AddPlayerWithLoadout(1, 0, one));
            Assert.NotNull(server.AddPlayerWithLoadout(2, 1, two));
            var r = new Round { Server = server };
            r.Ok(MatchEventKind.RoundStarted); r.T = 10f; r.Ok(MatchEventKind.DoorsOpened); r.T = 12f;
            r.Chest = server.Layout.Rooms.First(x => x.Loot.Count > 0 && x.Silver > 0 && !x.IsBossRoom && x.Type != RoomType.Vault);
            r.Gate = server.Layout.ExtractRooms.First().Id;
            return r;
        }

        [Fact]
        public void TheSelfSnapshotCarriesSpecPocketsCooldownsAndTheItemScreens()
        {
            var p = PlayerProfile.NewAccount("a", "Bram", Cls("Warden"));
            var server = MatchServer.Host(11, DungeonSettings.ForTeams(2), new MatchRules { TeamSize = 3 }, lootSecret: 5);
            server.StartRound();
            var lo = Kit(p, 2);
            Assert.NotNull(server.AddPlayerWithLoadout(1, 0, lo));
            server.AddBot(BotRules.BotIdBase, 0, "Warder", 1);
            for (int i = 0; i < 5; i++) server.Step(0.05f);
            Assert.True(server.UseConsumable(1, Potion));

            var back = TeamSnapshot.FromBytes(server.SnapshotForTeam(0).ToBytes());
            var me = back.Selves.Single(s => s.PlayerId == 1);
            Assert.Equal(lo.Spec, me.Spec);
            Assert.Equal(1, me.PocketCount("Health Potion"));
            Assert.Equal(Consumable.All.Count, me.ConsumableCooldowns.Length);
            Assert.True(me.ConsumableCooldown("Health Potion") > 0f);
            Assert.Equal(server.ConsumableCooldownRemaining(1, Potion), me.ConsumableCooldown("Health Potion"), 3);
            Assert.Equal(0f, me.ConsumableCooldown("Antidote"));

            // The item screens by id match the host's view.
            var host = server.ItemsFor(1);
            Assert.NotNull(me.Items);
            Assert.Equal(host.Carried.Select(c => (c.ItemId, c.Worn)), me.Items.Carried.Select(c => (c.ItemId, c.Worn)));
            Assert.Equal(host.LoadKg, me.Items.LoadKg, 3); Assert.Equal(host.CapacityKg, me.Items.CapacityKg, 3);
            Assert.Equal(host.Interaction, me.Items.Interaction);
            Assert.Equal(lo.Worn.Count, me.Items.Carried.Count(c => c.Worn));
            // A bot's member entry carries no item screens (nobody reads them).
            Assert.Null(back.Selves.Single(s => s.PlayerId == BotRules.BotIdBase).Items);
        }

        [Fact]
        public void TheTeamViewCarriesItemDetailsAndAnOlderViewStillReads()
        {
            var a = PlayerProfile.NewAccount("a", "Bram", Cls("Warden"));
            var b = PlayerProfile.NewAccount("b", "Vex", Cls("Warden"));
            var ring = new Item(ItemBase.ByName("Ring"), 3, Rarity.Magic, "Ring of Tests", new List<AffixRoll>(), new List<AffixRoll>());
            a.Stash.Add(ring);
            var r = Start(Kit(a, 1, ring), Kit(b, 0));
            int picked = r.OpenChestAndTake(1);
            var s = r.Server;

            var view = TeamView.FromBytes(TeamView.For(s.State, 0, s.ItemById).ToBytes());
            var mine = s.State.ItemOwner.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();
            Assert.Contains(picked, mine);
            foreach (int id in mine)
            {
                var detail = view.DetailOf(id);
                Assert.NotNull(detail);
                Assert.Equal(TeamView.WriteItem(s.ItemById(id)), TeamView.WriteItem(detail));   // base, level, rarity, affixes, wear: all of it
                Assert.Equal(s.ItemById(id).WeightKg, detail.WeightKg);
            }
            foreach (var info in view.Items) Assert.NotNull(view.DetailOf(info.Id));             // what is in the opened chest too
            // The other team learns nothing about our pockets.
            var theirs = TeamView.For(s.State, 1, s.ItemById);
            Assert.DoesNotContain(theirs.ItemDetails, d => mine.Contains(d.id));
            // Without the host's lookup (tests, tools) there are none, and a view written before the field existed still reads.
            var bare = TeamView.For(s.State, 0);
            Assert.Empty(bare.ItemDetails);
            var bytes = bare.ToBytes();
            var old = new byte[bytes.Length - 4];
            Array.Copy(bytes, old, old.Length);
            var oldView = TeamView.FromBytes(old);
            Assert.Empty(oldView.ItemDetails);
            Assert.Equal(bare.Explored, oldView.Explored);
        }

        [Fact]
        public void TheClientRebuildsTheItemScreensFromTheSnapshotAndTheView()
        {
            var a = PlayerProfile.NewAccount("a", "Bram", Cls("Warden"));
            var b = PlayerProfile.NewAccount("b", "Vex", Cls("Warden"));
            var r = Start(Kit(a, 2), Kit(b, 0));
            r.OpenChestAndTake(1);
            var s = r.Server;
            for (int i = 0; i < 3; i++) s.Step(0.05f);
            var self = TeamSnapshot.FromBytes(s.SnapshotForTeam(0).ToBytes()).Selves.Single(x => x.PlayerId == 1);
            var view = TeamView.FromBytes(TeamView.For(s.State, 0, s.ItemById).ToBytes());
            var cache = view.ItemDetails.ToDictionary(d => d.id, d => TeamView.ReadItem(d.item));
            var client = self.Items.ToPlayerItems(1, id => cache.TryGetValue(id, out var it) ? it : null);
            var host = s.ItemsFor(1);
            Assert.Equal(host.Carried.Count, client.Carried.Count);
            for (int i = 0; i < host.Carried.Count; i++)
            {
                Assert.Equal(host.Carried[i].ItemId, client.Carried[i].ItemId);
                Assert.Equal(TeamView.WriteItem(host.Carried[i].Item), TeamView.WriteItem(client.Carried[i].Item));
            }
            Assert.Equal(host.Overloaded, client.Overloaded);
            Assert.Equal(host.SpeedFactor, client.SpeedFactor, 4);
        }

        [Fact]
        public void TheDeathRecapRoundTrips()
        {
            var a = PlayerProfile.NewAccount("a", "Bram", Cls("Warden"));
            var b = PlayerProfile.NewAccount("b", "Vex", Cls("Marksman"));
            var r = Start(Kit(a, 0), Kit(b, 0));
            r.Ok(MatchEventKind.PlayerDowned, 1, 2, r.Chest.Id); r.T += 4f;
            r.Ok(MatchEventKind.PlayerFinished, 1, 2, r.Chest.Id);
            var hits = new HitLog();
            hits.Record(1, new Hit(13f, 2, "Volleyer", DamageType.Physical, 42.5f, "volley"));
            hits.Record(1, new Hit(15f, 2, null, DamageType.Fire, 7f, null));
            var recap = DeathRecap.For(r.Server.State, 1, hits);
            var back = DeathRecap.FromBytes(recap.ToBytes());
            Assert.Equal(recap.Headline, back.Headline);
            Assert.Equal(recap.Killer, back.Killer); Assert.Equal(recap.KillerClass, back.KillerClass); Assert.Equal(recap.KillerSpec, back.KillerSpec);
            Assert.True(back.Finished); Assert.Equal(recap.DownedSeconds, back.DownedSeconds);
            Assert.Equal(2, back.LastHits.Count);
            Assert.Equal(recap.LastHits.Select(h => (h.Time, h.Attacker, h.AttackerSpec, h.Type, h.Amount, h.Ability)), back.LastHits.Select(h => (h.Time, h.Attacker, h.AttackerSpec, h.Type, h.Amount, h.Ability)));
        }

        [Fact]
        public void AJoinRequestCarriesTheLoadoutAndTheServerChecksTheTierWall()
        {
            var p = PlayerProfile.NewAccount("acct-7", "Bram", Cls("Warden"));
            var ring = new Item(ItemBase.ByName("Ring"), 2, Rarity.Magic, "Ring", new List<AffixRoll>(), new List<AffixRoll>());
            p.Stash.Add(ring);
            var lo = Kit(p, 2, ring);
            var join = JoinRequest.FromBytes(JoinRequest.From(lo, 4).ToBytes());
            Assert.Equal("acct-7", join.AccountId); Assert.Equal("Bram", join.CharacterName); Assert.Equal("Warden", join.ClassName); Assert.Equal(4, join.AccountLevel);
            Assert.Null(join.Refusal(1));
            var rebuilt = join.ToLoadout();
            Assert.Equal(lo.Spec, rebuilt.Spec);
            Assert.Equal(lo.GearScore, rebuilt.GearScore);
            Assert.Equal(lo.Stats.Health, rebuilt.Stats.Health, 3);
            Assert.Equal(lo.Brought.Select(TeamView.WriteItem), rebuilt.Brought.Select(TeamView.WriteItem));   // same items, same order
            Assert.Equal(2, rebuilt.Consumables["Health Potion"]);

            // The server seats it like a local loadout.
            var server = MatchServer.Host(11, DungeonSettings.ForTeams(2), new MatchRules(), lootSecret: 5);
            server.StartRound();
            Assert.NotNull(server.AddPlayerWithLoadout(2, 0, rebuilt));
            Assert.Equal(lo.Brought.Count(), server.BroughtItemsOf(2).Count);

            // Refusals: over the tier wall, an unknown class, two weapons worn.
            var rich = PlayerProfile.NewAccount("r", "Rich", Cls("Warden"));
            for (int i = 0; i < 3; i++) rich.Stash.Add(new Item(UniqueItem.All[i].Base, 8, Rarity.Unique, UniqueItem.All[i].Name, new List<AffixRoll>(), new List<AffixRoll>(), UniqueItem.All[i]));
            Assert.Contains("Tier I", JoinRequest.From(Loadout.For(rich, rich.Characters[0], rich.Stash), 1).Refusal(1));
            var bad = JoinRequest.From(lo, 1); bad.ClassName = "Bard";
            Assert.NotNull(JoinRequest.FromBytes(bad.ToBytes()).Refusal(1));
            var twoWeapons = JoinRequest.From(lo, 1); twoWeapons.Worn.Add(lo.Worn.First(i => i.Base.Slot == ItemSlot.Weapon));
            Assert.NotNull(twoWeapons.Refusal(1));
        }

        /// <summary>
        /// Both ends of a fight settle on clients: the victim (died, carried a stash ring and two potions, picked up loot) and the killer (opened the
        /// corpse, took what survived the death trash, extracted). Each client profile is a copy of the host's profile before the round; settled from
        /// the result alone, it must come out byte-identical to the host's settlement of the original.
        /// </summary>
        [Fact]
        public void AClientSettlingFromTheRoundResultMatchesTheHostSettlement()
        {
            var victim = PlayerProfile.NewAccount("victim", "Bram", Cls("Warden"));
            var ring = new Item(ItemBase.ByName("Ring"), 2, Rarity.Magic, "Ring", new List<AffixRoll>(), new List<AffixRoll>());
            victim.Stash.Add(ring);
            var killer = PlayerProfile.NewAccount("killer", "Vex", Cls("Warden"));
            var victimClient = Clone(victim); var killerClient = Clone(killer);
            var victimClientLoadout = Kit(victimClient, 2, victimClient.Stash.Single());
            var killerClientLoadout = Kit(killerClient, 0);

            var r = Start(Kit(victim, 2, ring), Kit(killer, 0));
            var s = r.Server;
            r.OpenChestAndTake(1);
            Assert.True(s.UseConsumable(1, Potion));
            r.Ok(MatchEventKind.PlayerDied, 1, 2, r.Chest.Id);
            r.Ok(MatchEventKind.PlayerEnteredRoom, 2, r.Chest.Id);
            r.Ok(MatchEventKind.LootStarted, 2, 1, 1); r.T += 3.5f;
            r.Ok(MatchEventKind.CorpseOpened, 1, 2);
            foreach (int id in s.State.Corpses[1].ToList()) r.Ok(MatchEventKind.ItemTaken, id, 2);
            r.Ok(MatchEventKind.SilverLooted, 2, 1);
            r.Extract(2);

            var clientLayout = s.Descriptor.BuildAndVerify();
            foreach (var (player, profile, clientProfile, clientLoadout) in new[] { (1, victim, victimClient, victimClientLoadout), (2, killer, killerClient, killerClientLoadout) })
            {
                var result = RoundResult.FromBytes(RoundResult.For(s, player).ToBytes());
                var hostReport = RoundSettlement.Settle(profile, profile.Characters[0], s, player);
                var clientReport = result.Settle(clientProfile, clientProfile.Characters[0], clientLoadout.Brought.ToList(), s.Descriptor, clientLayout);
                Assert.Equal(hostReport.Outcome, clientReport.Outcome);
                Assert.Equal(hostReport.XpDelta, clientReport.XpDelta);
                Assert.Equal(hostReport.SilverDelta, clientReport.SilverDelta);
                Assert.Equal(hostReport.ItemsGained.Count, clientReport.ItemsGained.Count);
                Assert.Equal(hostReport.ItemsLost.Count, clientReport.ItemsLost.Count);
                Assert.Equal(hostReport.PickupsLost.Count, clientReport.PickupsLost.Count);
                Assert.Equal(hostReport.ConsumableDelta.OrderBy(kv => kv.Key), clientReport.ConsumableDelta.OrderBy(kv => kv.Key));
                Assert.Equal(hostReport.Summary.AccountXp, clientReport.Summary.AccountXp);
                Assert.Equal(hostReport.Summary.PlayerKills, clientReport.Summary.PlayerKills);
                Assert.Equal(hostReport.Summary.SoulsEarned, clientReport.Summary.SoulsEarned);
                Assert.Equal(hostReport.Summary.ItemsExtracted, clientReport.Summary.ItemsExtracted);
                Assert.Equal(hostReport.Summary.RoomsExplored, clientReport.Summary.RoomsExplored);
                Assert.Equal(ProfileCodec.ToBytes(profile), ProfileCodec.ToBytes(clientProfile));
                // Settling the same result again changes nothing.
                Assert.True(result.Settle(clientProfile, clientProfile.Characters[0], clientLoadout.Brought.ToList(), s.Descriptor, clientLayout).AlreadySettled);
            }
            var victimResult = RoundResult.For(s, 1);
            Assert.Equal(RoundOutcome.Died, victimResult.Outcome);
            Assert.NotNull(victimResult.Recap);
            Assert.Equal(victimResult.Recap.LastHits.Count, victimResult.HitAttackers.Count);
            Assert.Equal("Killed by a Warden.", RoundResult.FromBytes(victimResult.ToBytes()).Recap.Headline.Replace(" (Sentinel)", ""));
            Assert.Equal(AccountRules.XpPerExtraction + AccountRules.XpPerPlayerKill, killer.AccountXp);
        }

        [Fact]
        public void TheRoundResultCarriesOnlyThisPlayersSliceOfTheLog()
        {
            var a = PlayerProfile.NewAccount("a", "Bram", Cls("Warden"));
            var b = PlayerProfile.NewAccount("b", "Vex", Cls("Warden"));
            var r = Start(Kit(a, 0), Kit(b, 0));
            int other = r.Server.Layout.Rooms.First(x => x.Id != r.Chest.Id && x.Type == RoomType.Standard).Id;
            r.Ok(MatchEventKind.PlayerEnteredRoom, 2, other);     // the enemy walks somewhere: never in our result
            r.OpenChestAndTake(1);
            r.Extract(1);
            var res = r.Server == null ? null : RoundResult.For(r.Server, 1);
            Assert.Equal(RoundOutcome.Extracted, res.Outcome);
            Assert.DoesNotContain(res.Events, e => e.Kind == MatchEventKind.PlayerEnteredRoom);
            Assert.DoesNotContain(res.Players, p => p.player == 2);
            Assert.All(res.Events, e => Assert.True(e.A == 1 || e.B == 1 || e.Kind == MatchEventKind.RoundEnded || e.Kind == MatchEventKind.ContainerOpened || e.Kind == MatchEventKind.BossKilled));
            Assert.Equal(r.Server.State.ItemOwner.Where(kv => kv.Value == 1).Select(kv => kv.Key).OrderBy(i => i), res.Owned);
            Assert.Throws<InvalidOperationException>(() => RoundResult.For(r.Server, 2));   // still inside
        }
    }
}
