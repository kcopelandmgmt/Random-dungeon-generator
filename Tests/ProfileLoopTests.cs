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
    /// <summary>The meta loop: a profile saved and loaded, gear carried into a round, the round settled back into the profile.</summary>
    public class ProfileLoopTests
    {
        private static ClassArchetype Cls(string name) => ClassArchetype.ByName(name);
        private static Consumable Potion => Consumable.ByName("Health Potion");

        private static Item MakeUnique(UniqueItem u, int level = 4)
        {
            var pre = new List<AffixRoll>(); var suf = new List<AffixRoll>();
            foreach (var (kind, value, type, attr) in u.Fixed)
            {
                var def = AffixDefinition.Pool.First(d => d.Kind == kind && d.Type == type && d.Attribute == attr);
                (def.IsPrefix ? pre : suf).Add(new AffixRoll(def, 4, value));
            }
            return new Item(u.Base, level, Rarity.Unique, u.Name, pre, suf, u);
        }

        /// <summary>Every rarity, every unique, worn and broken and destroyed gear, materials, consumables, three characters.</summary>
        private static PlayerProfile RichProfile()
        {
            var p = PlayerProfile.NewAccount("acct-rich", "Bram", Cls("Warden"));
            p.Silver = 12345; p.AddAccountXp(9000);   // level 10: the Reaver is open
            p.Materials.Scrap = 77; p.Materials.PrimeShards = 5; p.Materials.AddEssence("Resist:Fire", 3); p.Materials.AddEssence("Health", 2);
            var rng = new DungeonRandom(42);
            var rolled = new List<Item>();
            for (int i = 0; i < 400 && (rolled.Count(x => x.Rarity == Rarity.Magic) < 3 || rolled.Count(x => x.Rarity == Rarity.Rare) < 3); i++)
            {
                var it = LootRoller.Roll(i % 5, LootSource.Chest, rng);
                if (it.Rarity != Rarity.Normal || rolled.Count(x => x.Rarity == Rarity.Normal) < 2) rolled.Add(it);
            }
            p.Stash.AddRange(rolled);
            foreach (var u in UniqueItem.All) p.Stash.Add(MakeUnique(u));
            var broken = LootRoller.Roll(2, LootSource.Boss, rng); broken.Condition.Wear(10000f); p.Stash.Add(broken);
            var destroyed = LootRoller.Roll(1, LootSource.Boss, rng); destroyed.Condition.Destroy(); p.Stash.Add(destroyed);
            p.AddConsumable(Consumable.ByName("Antidote"), 3); p.AddConsumable(Consumable.ByName("Salvage Kit"), 1);
            var marks = p.CreateCharacter("Wren", Cls("Marksman"));
            Vendor.ClaimStarterKit(marks);
            marks.Equipment.Ring2 = MakeUnique(UniqueItem.ByName("Glasswright's Prism"));   // second ring slot only: slots are kept exactly
            marks.Equipment.Feet.Condition.Wear(33.5f);
            var reaver = p.CreateCharacter("Kesh", Cls("Reaver"));
            reaver.Equipment.Weapon = new Item(ItemBase.ByName("Greataxe"), 3, Rarity.Rare, "Grim Bite, Greataxe",
                new List<AffixRoll> { new AffixRoll(AffixDefinition.ByFamily("IncreasedDamage"), 3, 17f), new AffixRoll(AffixDefinition.ByFamily("AddedDamage:Fire"), 3, 14f) },
                new List<AffixRoll> { new AffixRoll(AffixDefinition.ByFamily("Crit"), 3, 3f), new AffixRoll(AffixDefinition.ByFamily("Attribute:Strength"), 2, 2f) });
            p.Unlocks.Add("cosmetic:ember-cloak"); p.Unlocks.Add("recipe:warded");
            p.MarkSettled("old-round/p1");
            return p;
        }

        private static string Describe(Item it) => it == null ? "-" : $"{it}|{it.IsStarter}|{it.Condition.Max}|{it.Condition.Current}|{it.Unique?.Name}|{string.Join(",", it.Prefixes.Select(a => a.Definition.FamilyKey + a.Tier + ":" + a.Value))}/{string.Join(",", it.Suffixes.Select(a => a.Definition.FamilyKey + a.Tier + ":" + a.Value))}";
        private static IEnumerable<Item> Slots(Equipment e) => new[] { e.Weapon, e.Head, e.Chest, e.Hands, e.Legs, e.Feet, e.Amulet, e.Ring1, e.Ring2 };

        private static void AssertSameProfile(PlayerProfile a, PlayerProfile b)
        {
            Assert.Equal(a.AccountId, b.AccountId); Assert.Equal(a.Silver, b.Silver); Assert.Equal(a.AccountXp, b.AccountXp);
            Assert.Equal(a.Materials.ToString(), b.Materials.ToString());
            Assert.Equal(a.Stash.Select(Describe), b.Stash.Select(Describe));
            Assert.Equal(a.Consumables.OrderBy(kv => kv.Key), b.Consumables.OrderBy(kv => kv.Key));
            Assert.Equal(a.Characters.Count, b.Characters.Count);
            for (int i = 0; i < a.Characters.Count; i++)
            {
                Assert.Equal(a.Characters[i].Name, b.Characters[i].Name);
                Assert.Same(a.Characters[i].Class, b.Characters[i].Class);
                Assert.Equal(Slots(a.Characters[i].Equipment).Select(Describe), Slots(b.Characters[i].Equipment).Select(Describe));
            }
            Assert.Equal(a.Unlocks.OrderBy(x => x), b.Unlocks.OrderBy(x => x));
            Assert.Equal(a.SettledRounds, b.SettledRounds);
        }

        [Fact]
        public void ARichProfileRoundTripsLosslessly()
        {
            var p = RichProfile();
            Assert.Contains(p.Stash, i => i.Rarity == Rarity.Normal); Assert.Contains(p.Stash, i => i.Rarity == Rarity.Magic);
            Assert.Contains(p.Stash, i => i.Rarity == Rarity.Rare); Assert.Equal(UniqueItem.All.Count, p.Stash.Count(i => i.Rarity == Rarity.Unique));
            var bytes = ProfileCodec.ToBytes(p);
            var back = ProfileCodec.FromBytes(bytes);
            AssertSameProfile(p, back);
            Assert.Equal(bytes, ProfileCodec.ToBytes(back));   // save → load → save is byte-identical
            // Uniques resolve to the catalogue's own objects, so one-of-each and the legendary still work.
            foreach (var u in back.Stash.Where(i => i.Unique != null)) Assert.Same(UniqueItem.ByName(u.Name), u.Unique);
            Assert.True(back.Stash.Single(i => i.Condition.IsDestroyed).Condition.IsDestroyed);
            Assert.Contains(back.Stash, i => i.Condition.IsBroken);
            Assert.True(back.Characters[0].Equipment.Items.All(i => i.IsStarter));
            Assert.Null(back.Characters[1].Equipment.Ring1);
            Assert.NotNull(back.Characters[1].Equipment.Ring2);
            Assert.Equal(p.AccountLevel, back.AccountLevel);
            Assert.Equal(p.Characters[1].Sheet().ToString(), back.Characters[1].Sheet().ToString());
        }

        [Fact]
        public void UnknownKeysAndBadHeadersFailLoudly()
        {
            var p = PlayerProfile.NewAccount("acct", "Bram", Cls("Warden"));
            p.Stash.Add(new Item(ItemBase.ByName("Longsword"), 1, Rarity.Normal, "Longsword", new List<AffixRoll>(), new List<AffixRoll>()));
            var bytes = ProfileCodec.ToBytes(p);
            byte[] Swap(byte[] src, string from, string to, int nth = 0)
            {
                var s = System.Text.Encoding.UTF8.GetBytes(from); var t = System.Text.Encoding.UTF8.GetBytes(to);
                var copy = (byte[])src.Clone();
                for (int i = 0; i + s.Length <= copy.Length; i++)
                    if (copy.Skip(i).Take(s.Length).SequenceEqual(s) && nth-- == 0) { Array.Copy(t, 0, copy, i, t.Length); return copy; }
                throw new Exception("not found: " + from);
            }
            var e1 = Assert.Throws<ProfileFormatException>(() => ProfileCodec.FromBytes(Swap(bytes, "Longsword", "Longswore")));
            Assert.Contains("Longswore", e1.Message);
            var e2 = Assert.Throws<ProfileFormatException>(() => ProfileCodec.FromBytes(Swap(bytes, "Warden", "Wardem")));
            Assert.Contains("Wardem", e2.Message);
            var e3 = Assert.Throws<ProfileFormatException>(() => ProfileCodec.FromBytes(Swap(bytes, "Health Potion", "Health Lotion")));
            Assert.Contains("Health Lotion", e3.Message);
            var badVersion = (byte[])bytes.Clone(); badVersion[4] = 99;
            Assert.Contains("version", Assert.Throws<ProfileFormatException>(() => ProfileCodec.FromBytes(badVersion)).Message);
            var badMagic = (byte[])bytes.Clone(); badMagic[0] ^= 0xFF;
            Assert.Throws<ProfileFormatException>(() => ProfileCodec.FromBytes(badMagic));
            Assert.Throws<ProfileFormatException>(() => ProfileCodec.FromBytes(bytes.Take(bytes.Length - 3).ToArray()));
            // An affix family nobody defines.
            var magic = new Item(ItemBase.ByName("Ring"), 1, Rarity.Magic, "Ring of the Bear", new List<AffixRoll>(), new List<AffixRoll> { new AffixRoll(AffixDefinition.ByFamily("Attribute:Strength"), 1, 1f) });
            var q = new PlayerProfile("a"); q.Stash.Add(magic);
            var e4 = Assert.Throws<ProfileFormatException>(() => ProfileCodec.FromBytes(Swap(ProfileCodec.ToBytes(q), "Attribute:Strength", "Attribute:Strengxh")));
            Assert.Contains("Attribute:Strengxh", e4.Message);
            // A unique nobody defines.
            var r = new PlayerProfile("a"); r.Stash.Add(MakeUnique(UniqueItem.ByName("Ashen Heart")));
            Assert.Contains("Ashen Heary", Assert.Throws<ProfileFormatException>(() => ProfileCodec.FromBytes(Swap(ProfileCodec.ToBytes(r), "Ashen Heart", "Ashen Heary", nth: 1))).Message);
            // A mangled base name.
            Assert.Throws<ProfileFormatException>(() => ProfileCodec.FromBytes(Swap(ProfileCodec.ToBytes(r), "Amulet", "Ring\u0000\u0000")));
        }

        [Fact]
        public void CatalogueLookupsByName()
        {
            foreach (var b in ItemBase.All) Assert.Same(b, ItemBase.ByName(b.Name));
            Assert.Equal(ItemBase.All.Count(), ItemBase.All.Select(b => b.Name).Distinct().Count());
            foreach (var u in UniqueItem.All) Assert.Same(u, UniqueItem.ByName(u.Name));
            foreach (var c in Consumable.All) Assert.Same(c, Consumable.ByName(c.Name));
            foreach (var c in ClassArchetype.DefaultRoster) Assert.Same(c, ClassArchetype.ByName(c.Name));
            foreach (var d in AffixDefinition.Pool) Assert.Same(d, AffixDefinition.ByFamily(d.FamilyKey));
            Assert.Null(ItemBase.ByName("Spork")); Assert.Null(UniqueItem.ByName("x")); Assert.Null(Consumable.ByName("x")); Assert.Null(ClassArchetype.ByName("Bard"));
        }

        // ---- The round ----------------------------------------------------------------------

        private sealed class Round
        {
            public MatchServer Server; public Room Chest; public int Gate; public float T = 20f;
            public bool Raise(MatchEventKind k, int a = 0, int b = 0, int c = 0) => Server.Raise(new MatchEvent(k, T, a, b, c));
            public void Ok(MatchEventKind k, int a = 0, int b = 0, int c = 0) => Assert.True(Raise(k, a, b, c), string.Join(" | ", Server.RecentRejections));
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

        /// <summary>A two-team dungeon with player 1 seated from <paramref name="loadout"/> on team 0 and optionally player 2 on team 1.</summary>
        private static Round StartRound(Loadout loadout, Loadout enemy = null, int seed = 11, Action<MatchServer> seat = null)
        {
            var server = MatchServer.Host(seed, DungeonSettings.ForTeams(2), new MatchRules { TeamSize = 3 }, lootSecret: 5);
            server.StartRound();
            Assert.NotNull(server.AddPlayerWithLoadout(1, 0, loadout));
            if (enemy != null) Assert.NotNull(server.AddPlayerWithLoadout(2, 1, enemy));
            else server.AddPlayer(2, 1, Cls("Warden"), "Sentinel", PlayerStats.For(Cls("Warden"), 1));
            seat?.Invoke(server);
            var r = new Round { Server = server, T = 0f };
            r.Ok(MatchEventKind.RoundStarted); r.T = 10f; r.Ok(MatchEventKind.DoorsOpened); r.T = 12f;
            r.Chest = server.Layout.Rooms.First(x => x.Loot.Count > 0 && x.Silver > 0 && !x.IsBossRoom && x.Type != RoomType.Vault);
            r.Gate = server.Layout.ExtractRooms.First().Id;
            return r;
        }

        private static Loadout Kit(PlayerProfile p, Character ch, int potions = 2, params Item[] carried) =>
            Loadout.For(p, ch, carried, potions > 0 ? new Dictionary<string, int> { ["Health Potion"] = potions } : null);

        [Fact]
        public void ALoadoutIsTheCharactersSheetAndRegistersEveryBroughtItem()
        {
            var p = PlayerProfile.NewAccount("acct", "Bram", Cls("Warden"));
            var extra = new Item(ItemBase.ByName("Ring"), 1, Rarity.Magic, "Ring", new List<AffixRoll>(), new List<AffixRoll>());
            p.Stash.Add(extra);
            var ch = p.Characters[0];
            var lo = Kit(p, ch, 2, extra);
            Assert.Equal(ch.Sheet().ToString(), lo.Stats.ToString());
            Assert.Equal("Sentinel", lo.Spec);
            Assert.Equal(6, lo.Worn.Count); Assert.Single(lo.Carried);
            Assert.Equal(6 + GearScore.Of(extra), lo.GearScore);                     // worn AND carried count
            var regs = lo.Registrations(1, MatchState.BroughtItemIdBase, 0f);
            Assert.Equal(7, regs.Count);
            Assert.All(regs, r => { Assert.Equal(MatchEventKind.ItemRegistered, r.registration.Kind); Assert.Equal(1, r.registration.B); Assert.Equal(r.item.Value, r.registration.C); });
            Assert.Throws<ArgumentOutOfRangeException>(() => lo.Registrations(1, 5, 0f));
            // Refusals: more potions than the bank, an item not in the stash, another account's character.
            Assert.Throws<InvalidOperationException>(() => Kit(p, ch, 3));
            Assert.Throws<InvalidOperationException>(() => Kit(p, ch, 0, MakeUnique(UniqueItem.All[0])));
            Assert.Throws<InvalidOperationException>(() => Loadout.For(PlayerProfile.NewAccount("b", "x", Cls("Mender")), ch));

            var round = StartRound(lo);
            var s = round.Server;
            var ids = s.BroughtItemsOf(1);
            Assert.Equal(7, ids.Count);
            Assert.All(ids, id => { Assert.True(id >= MatchState.BroughtItemIdBase); Assert.Equal(1, s.State.ItemOwner[id]); });
            Assert.Same(extra, s.ItemById(ids.Last()));
            Assert.Same(ch.Equipment.Weapon, s.ItemById(ids.First()));
            Assert.Same(s.State.Index.Items[1], s.ItemById(1));                     // rolled loot resolves too
            Assert.Null(s.ItemById(424242));
            Assert.Equal(lo.Stats.Health, s.Sim.Get(1).MaxHealth, 3);
            Assert.Equal(2, s.ConsumablesCarriedBy(1)["Health Potion"]);
        }

        [Fact]
        public void TheTierWallRefusesALoadoutOutsideTheBand()
        {
            var p = PlayerProfile.NewAccount("acct", "Bram", Cls("Warden"));
            for (int i = 0; i < 3; i++) p.Stash.Add(MakeUnique(UniqueItem.All[i], 8));   // 3 × 36 = 108, plus the kit's 6: over Tier I's 50
            var lo = Loadout.For(p, p.Characters[0], p.Stash);
            Assert.NotNull(lo.TierRefusal(1));
            var server = MatchServer.Host(11, DungeonSettings.ForTeams(2), new MatchRules(), lootSecret: 5);
            server.StartRound();
            var e = Assert.Throws<InvalidOperationException>(() => server.AddPlayerWithLoadout(1, 0, lo));
            Assert.Contains("Tier I", e.Message);
            Assert.False(server.State.PlayerTeam.ContainsKey(1));
        }

        [Fact]
        public void ExtractingPutsPickupsInTheStashWithSilverAndXp()
        {
            var p = PlayerProfile.NewAccount("acct", "Bram", Cls("Warden"));
            var ch = p.Characters[0];
            var worn = ch.Equipment.Items.ToList();
            var round = StartRound(Kit(p, ch));
            int picked = round.OpenChestAndTake(1);
            var pickedItem = round.Server.ItemById(picked);
            Assert.True(round.Server.UseConsumable(1, Potion));
            int silver = round.Server.State.PlayerSilver[1];
            Assert.True(silver > 0);
            round.Extract(1);

            long silverBefore = p.Silver;
            var rep = RoundSettlement.Settle(p, ch, round.Server, 1);
            Assert.Equal(RoundOutcome.Extracted, rep.Outcome);
            Assert.False(rep.AlreadySettled);
            Assert.Contains(pickedItem, p.Stash);
            Assert.Equal(new[] { pickedItem }, rep.ItemsGained);
            Assert.Empty(rep.ItemsLost);
            Assert.Equal(worn.Count, rep.ItemsKept.Count);
            Assert.Equal(worn, ch.Equipment.Items.ToList());                         // worn gear stays worn
            Assert.Equal(silver, rep.SilverDelta); Assert.Equal(silverBefore + silver, p.Silver);
            Assert.Equal(AccountRules.XpPerExtraction, rep.XpDelta); Assert.Equal(AccountRules.XpPerExtraction, p.AccountXp);
            Assert.Equal(1, p.ConsumableCount(Potion));                             // one drunk, one walked home
            Assert.Equal(-1, rep.ConsumableDelta["Health Potion"]);
            // The trip's wear on what is still worn: no repairs.
            Assert.All(ch.Equipment.Items, i => Assert.True(i.Condition.Current <= i.Condition.Max - DurabilityRules.BaseWearPerRound + 0.001f));
            Assert.False(rep.CharacterNaked);
        }

        [Fact]
        public void DyingLosesBroughtGearPickupsSilverAndPocketsButKeepsXp()
        {
            var p = PlayerProfile.NewAccount("acct", "Bram", Cls("Warden"));
            var carried = new Item(ItemBase.ByName("Amulet"), 1, Rarity.Magic, "Amulet", new List<AffixRoll>(), new List<AffixRoll>());
            p.Stash.Add(carried);
            var ch = p.Characters[0];
            var killerProfile = PlayerProfile.NewAccount("killer", "Vex", Cls("Warden"));
            var round = StartRound(Kit(p, ch, 2, carried), Kit(killerProfile, killerProfile.Characters[0], 0));
            int picked = round.OpenChestAndTake(1);
            var pickedItem = round.Server.ItemById(picked);
            var brought = round.Server.BroughtItemsOf(1).Select(round.Server.ItemById).ToList();
            round.Ok(MatchEventKind.PlayerDied, 1, 2, round.Chest.Id);
            Assert.False(round.Server.UseConsumable(1, Potion), "the dead drink nothing");
            // The killer loots the corpse: full loot.
            round.Ok(MatchEventKind.PlayerEnteredRoom, 2, round.Chest.Id);
            round.Ok(MatchEventKind.LootStarted, 2, 1, 1); round.T += 3.5f;
            round.Ok(MatchEventKind.CorpseOpened, 1, 2);
            foreach (int id in round.Server.State.Corpses[1].ToList()) round.Ok(MatchEventKind.ItemTaken, id, 2);
            round.Ok(MatchEventKind.SilverLooted, 2, 1);

            long silverBefore = p.Silver;
            var rep = RoundSettlement.Settle(p, ch, round.Server, 1);
            Assert.Equal(RoundOutcome.Died, rep.Outcome);
            Assert.Empty(ch.Equipment.Items);                                        // the starter kit too: nothing survives dying
            Assert.True(rep.CharacterNaked);
            Assert.DoesNotContain(carried, p.Stash);
            Assert.Equal(brought.Count, rep.ItemsLost.Count);
            Assert.Contains(pickedItem, rep.PickupsLost);
            Assert.DoesNotContain(pickedItem, p.Stash);
            Assert.Equal(0, rep.SilverDelta); Assert.Equal(silverBefore, p.Silver);
            Assert.Equal(0, p.ConsumableCount(Potion));                             // both potions were in the pockets
            Assert.Equal(0, rep.XpDelta);
            // The starter kit is free again any time.
            Vendor.ClaimStarterKit(ch);
            Assert.Equal(6, ch.Equipment.Items.Count());

            // The killer walks out with the victim's gear, the pickup and the silver.
            round.Extract(2);
            var krep = RoundSettlement.Settle(killerProfile, killerProfile.Characters[0], round.Server, 2);
            Assert.Equal(RoundOutcome.Extracted, krep.Outcome);
            // Half the victim's items were trashed on death (DeathTrashChance); the killer walks out with exactly the survivors.
            var trashed = round.Server.State.DestroyedItems;
            Assert.All(round.Server.BroughtItemsOf(1).Append(picked), id => Assert.Equal(!trashed.Contains(id), killerProfile.Stash.Contains(round.Server.ItemById(id))));
            Assert.True(krep.SilverDelta > 0);
            Assert.Equal(AccountRules.XpPerExtraction + AccountRules.XpPerPlayerKill, krep.XpDelta);
        }

        [Fact]
        public void GearHandedAwayOrDroppedIsLostEvenWhenExtracting()
        {
            var p = PlayerProfile.NewAccount("acct", "Bram", Cls("Warden"));
            var ch = p.Characters[0];
            var helm = ch.Equipment.Head;
            var round = StartRound(Kit(p, ch, 0));
            var s = round.Server;
            int helmId = s.BroughtItemsOf(1).Single(id => ReferenceEquals(s.ItemById(id), helm));
            round.Ok(MatchEventKind.PlayerEnteredRoom, 1, round.Chest.Id);
            round.Ok(MatchEventKind.ItemDropped, helmId, 1);
            round.Extract(1);
            var rep = RoundSettlement.Settle(p, ch, s, 1);
            Assert.Equal(new[] { helm }, rep.ItemsLost);
            Assert.Null(ch.Equipment.Head);
            Assert.Equal(5, ch.Equipment.Items.Count());
        }

        [Fact]
        public void AnExpiredDisconnectSettlesAsADeathAndAPlayerStillInsideCannotSettle()
        {
            var p = PlayerProfile.NewAccount("acct", "Bram", Cls("Warden"));
            var ch = p.Characters[0];
            var round = StartRound(Kit(p, ch));
            Assert.Throws<InvalidOperationException>(() => RoundSettlement.Settle(p, ch, round.Server, 1));
            round.Ok(MatchEventKind.PlayerDisconnected, 1);
            Assert.Null(RoundSettlement.OutcomeOf(round.Server.State, 1));             // inside the window: still in the round
            round.T += round.Server.Descriptor.Rules.ReconnectWindowSeconds + 1f;
            round.Ok(MatchEventKind.PlayerEnteredRoom, 2, round.Chest.Id);           // time moves on
            Assert.Equal(RoundOutcome.Died, RoundSettlement.OutcomeOf(round.Server.State, 1));
            var rep = RoundSettlement.Settle(p, ch, round.Server, 1);
            Assert.Equal(RoundOutcome.Died, rep.Outcome);
            Assert.Empty(ch.Equipment.Items);
            Assert.Equal(0, p.ConsumableCount(Potion));
        }

        [Fact]
        public void TheClockKillsWhoeverIsInsideAndSettlementAgrees()
        {
            var p = PlayerProfile.NewAccount("acct", "Bram", Cls("Warden"));
            var ch = p.Characters[0];
            var round = StartRound(Kit(p, ch));
            round.OpenChestAndTake(1);
            round.T = 1200f; round.Ok(MatchEventKind.RoundEnded);
            var rep = RoundSettlement.Settle(p, ch, round.Server, 1);
            Assert.Equal(RoundOutcome.Died, rep.Outcome);
            Assert.Empty(rep.ItemsGained); Assert.Empty(ch.Equipment.Items);
        }

        [Fact]
        public void ShrinePurchasesWalkOutAndADeathEmptiesThePocketsEvenAfterAResurrection()
        {
            var p = PlayerProfile.NewAccount("acct", "Bram", Cls("Warden"));
            var ch = p.Characters[0];
            var round = StartRound(Kit(p, ch, 1));
            var st = round.Server.State;
            var shrine = round.Server.Layout.Rooms.First(r => r.HasShrine);
            st.Souls[1] = 1;   // as if from a boss chest
            round.Ok(MatchEventKind.ShrinePurchase, 1, Consumable.All.ToList().IndexOf(Consumable.ByName("Antidote")), shrine.Id);
            Assert.Equal(1, round.Server.PocketsOf(1)["Antidote"]);
            round.Extract(1);
            var rep = RoundSettlement.Settle(p, ch, round.Server, 1);
            Assert.Equal(1, p.ConsumableCount(Consumable.ByName("Antidote")));
            Assert.Equal(2, p.ConsumableCount(Potion));                             // carried one, used none, the other never left the bank
            Assert.Equal(1, rep.ConsumableDelta["Antidote"]);
            Assert.False(rep.ConsumableDelta.ContainsKey("Health Potion"));

            // Died, resurrected, extracted: the pockets emptied at the death.
            var q = PlayerProfile.NewAccount("acct2", "Ana", Cls("Warden"));
            var round2 = StartRound(Kit(q, q.Characters[0], 2), seat: s => Assert.NotNull(s.AddPlayer(3, 0, Cls("Mender"), "Warder", PlayerStats.For(Cls("Mender"), 1))));
            round2.Ok(MatchEventKind.PlayerDied, 1, -1, round2.Chest.Id);
            round2.Server.State.Souls[3] = 2;
            var shrine2 = round2.Server.Layout.Rooms.First(r => r.HasShrine);
            round2.Ok(MatchEventKind.PlayerResurrected, 1, 3, shrine2.Id);
            Assert.Empty(round2.Server.PocketsOf(1));
            round2.Extract(1);
            var rep2 = RoundSettlement.Settle(q, q.Characters[0], round2.Server, 1);
            Assert.Equal(RoundOutcome.Extracted, rep2.Outcome);
            Assert.Equal(0, q.ConsumableCount(Potion));
            Assert.Empty(q.Characters[0].Equipment.Items);                           // returned naked; the corpse kept the gear
        }

        [Fact]
        public void SettlingTwiceChangesNothingTheSecondTimeEvenAfterASaveAndLoad()
        {
            var p = PlayerProfile.NewAccount("acct", "Bram", Cls("Warden"));
            var ch = p.Characters[0];
            var round = StartRound(Kit(p, ch));
            round.OpenChestAndTake(1);
            round.Extract(1);
            var first = RoundSettlement.Settle(p, ch, round.Server, 1);
            var bytes = ProfileCodec.ToBytes(p);
            var again = RoundSettlement.Settle(p, ch, round.Server, 1);
            Assert.True(again.AlreadySettled);
            Assert.Empty(again.ItemsGained); Assert.Equal(0, again.SilverDelta); Assert.Equal(0, again.XpDelta);
            Assert.Equal(bytes, ProfileCodec.ToBytes(p));
            var loaded = ProfileCodec.FromBytes(bytes);
            Assert.Contains(first.RoundKey, loaded.SettledRounds);
            Assert.True(RoundSettlement.Settle(loaded, loaded.Characters[0], round.Server, 1).AlreadySettled);
            Assert.Equal(bytes, ProfileCodec.ToBytes(loaded));
        }

        [Fact]
        public void SaveLoadPlaySettleSaveStaysConsistent()
        {
            // Save and load before the round: the round is played with the LOADED profile's objects.
            var original = RichProfile();
            var p = ProfileCodec.FromBytes(ProfileCodec.ToBytes(original));
            var ch = p.Characters[1];   // the Marksman with a unique ring
            var carried = p.Stash.First(i => i.Rarity == Rarity.Magic);
            int stashBefore = p.Stash.Count;
            var lo = Loadout.For(p, ch, new[] { carried }, new Dictionary<string, int> { ["Antidote"] = 1 });
            var round = StartRound(lo);
            int picked = round.OpenChestAndTake(1);
            round.Extract(1);
            var rep = RoundSettlement.Settle(p, ch, round.Server, 1, roundKey: "match-0001");
            Assert.Equal("match-0001", rep.RoundKey);
            Assert.Equal(stashBefore + 1, p.Stash.Count);
            Assert.Contains(carried, p.Stash);
            Assert.Contains(round.Server.ItemById(picked), p.Stash);
            Assert.Equal(original.AccountXp + AccountRules.XpPerExtraction, p.AccountXp);
            // Save, load, save again: identical, and the settled round and its wear survive.
            var saved = ProfileCodec.ToBytes(p);
            var reloaded = ProfileCodec.FromBytes(saved);
            AssertSameProfile(p, reloaded);
            Assert.Equal(saved, ProfileCodec.ToBytes(reloaded));
            Assert.Contains("match-0001", reloaded.SettledRounds);
            Assert.Contains("old-round/p1", reloaded.SettledRounds);
            Assert.Equal(Slots(ch.Equipment).Select(Describe), Slots(reloaded.Characters[1].Equipment).Select(Describe));
            Assert.True(reloaded.Characters[1].Equipment.Ring2.Condition.Current < reloaded.Characters[1].Equipment.Ring2.Condition.Max);
            // A settlement against the wrong character is refused.
            Assert.Throws<InvalidOperationException>(() => RoundSettlement.Settle(reloaded, reloaded.Characters[0], round.Server, 1, roundKey: "match-0002"));
        }

        [Fact]
        public void ALoadoutSeatedPlayerRunsOnTheRealServerLoop()
        {
            var p = PlayerProfile.NewAccount("acct", "Bram", Cls("Marksman"));
            var server = MatchServer.Host(3, DungeonSettings.ForTeams(2), new MatchRules { TeamSize = 3 }, lootSecret: 9);
            server.StartRound();
            Assert.NotNull(server.AddPlayerWithLoadout(1, 0, Loadout.For(p, p.Characters[0])));
            server.AddBot(BotRules.BotIdBase, 1, "Sentinel", 1, new RoundBot(BotRules.BotIdBase, new BotSquad { Team = 1 }));
            for (int i = 0; i < 90; i++) server.Step(0.1f);   // the countdown: the clock starts the round, nobody moves yet
            Assert.Equal(0, server.RejectedCount);
            Assert.Equal(6, server.State.ItemOwner.Count(kv => kv.Value == 1 && kv.Key >= MatchState.BroughtItemIdBase));
            var (taken, dealt) = server.DamageTotals(1);
            Assert.True(taken >= 0f && dealt >= 0f);
            // Server-assigned ids start at the base.
            Assert.Equal(MatchState.BroughtItemIdBase, server.BroughtItemsOf(1).First());
        }

        [Fact]
        public void TheSettledRoundListIsBounded()
        {
            var p = new PlayerProfile("a");
            for (int i = 0; i < PlayerProfile.SettledRoundsKept + 10; i++) p.MarkSettled("r" + i);
            Assert.Equal(PlayerProfile.SettledRoundsKept, p.SettledRounds.Count);
            Assert.False(p.IsSettled("r0")); Assert.True(p.IsSettled("r" + (PlayerProfile.SettledRoundsKept + 9)));
        }
    }
}
