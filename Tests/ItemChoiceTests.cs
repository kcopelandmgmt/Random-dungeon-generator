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
    /// Letting a player choose: the intent's TakeItemId / DropItemId / RollItemId + RollChoice go through the same validated
    /// events as Space (ItemTaken, ItemDropped, LootRolled), the host's <see cref="PlayerItems"/> view of what is in reach,
    /// carried and rolling, and the between-round stash and paper-doll rules.
    /// </summary>
    public class ItemChoiceTests
    {
        private static ClassArchetype Cls(string name) => ClassArchetype.ByName(name);

        /// <summary>Player 1 (a Warden in the free kit with two potions) on team 0; optionally a teammate (3) on team 0 and an enemy (2) on team 1; doors open.</summary>
        private static MatchServer Open(bool teammate, bool enemy = true, int seed = 11)
        {
            var p = PlayerProfile.NewAccount("a", "Bram", Cls("Warden"));
            var lo = Loadout.For(p, p.Characters[0], null, new Dictionary<string, int> { ["Health Potion"] = 2 });
            var s = MatchServer.Host(seed, DungeonSettings.ForTeams(2), new MatchRules { TeamSize = 3 }, lootSecret: 5);
            s.StartRound();
            Assert.NotNull(s.AddPlayerWithLoadout(1, 0, lo));
            if (enemy) s.AddPlayer(2, 1, Cls("Warden"), "Sentinel", PlayerStats.For(Cls("Warden"), 1));
            if (teammate) s.AddPlayer(3, 0, Cls("Mender"), WeaponSpec.ForClass("Mender").First().Name, PlayerStats.For(Cls("Mender"), 1));
            for (int i = 0; i < 200 && !s.State.DoorsOpen; i++) s.Step(0.1f);
            Assert.True(s.State.DoorsOpen);
            return s;
        }

        private static void Run(MatchServer s, float seconds, float dt = 0.1f) { for (float t = 0; t < seconds; t += dt) s.Step(dt); }

        /// <summary>A plain chest room (not a boss room, not a vault) holding at least <paramref name="min"/> items, preferring one with a Magic+ piece when asked.</summary>
        private static Room ChestRoom(MatchServer s, int min, bool wantRoll = false) =>
            s.Layout.Rooms.Where(r => !r.IsBossRoom && r.Type != RoomType.Vault && r.Loot.Count >= min && (!wantRoll || r.Loot.Any(LootRollRules.GoesToRoll)))
                .OrderBy(r => r.Mobs == null ? 0 : 1).ThenBy(r => r.Id).First();

        /// <summary>A walkable cell in the room, away from its doors.</summary>
        private static Vec2 Inside(MatchServer s, Room r)
        {
            var c = Vec2.Of(r.Center);
            if (s.Sim.Walkable(c)) return c;
            for (int dx = -2; dx <= 2; dx++) for (int dy = -2; dy <= 2; dy++) { var p = c + new Vec2(dx, dy); if (s.Sim.Walkable(p) && s.Layout.RoomIdAt[p.Cell.X, p.Cell.Y] == r.Id) return p; }
            return c;
        }

        /// <summary>Open the room's chest for <paramref name="opener"/> through the validated channel events, then stand them in the room.</summary>
        private static void OpenChest(MatchServer s, Room r, int opener)
        {
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.LootStarted, s.Sim.Time, opener, r.Id, 0)), string.Join("; ", s.RecentRejections));
            Run(s, FightRules.ChestOpenSeconds + 0.1f);
            s.Sim.Get(opener).Position = Inside(s, r);
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.ContainerOpened, s.Sim.Time, r.Id, opener)), string.Join("; ", s.RecentRejections));
        }

        private static void Send(MatchServer s, PlayerIntent i, int player = 1) { i.PlayerId = player; s.SetIntent(player, i); s.Step(0.05f); }

        // ---- the wire ----

        [Fact]
        public void ItemFieldsRoundTripAndAnIntentWithoutThemIsTheOldFormat()
        {
            var i = new PlayerIntent { PlayerId = 4, Tick = 2, MoveX = 1, Basic = true, ConsumableSlot = 1, TakeItemId = 17, DropItemId = 100003, RollItemId = 9, RollChoice = (byte)LootRollChoice.Greed };
            var back = PlayerIntent.FromBytes(i.ToBytes());
            Assert.Equal(17, back.TakeItemId); Assert.Equal(100003, back.DropItemId); Assert.Equal(9, back.RollItemId); Assert.Equal((byte)LootRollChoice.Greed, back.RollChoice);
            Assert.Equal(1, back.ConsumableSlot); Assert.True(back.Basic); Assert.Equal(1f, back.MoveX);

            var plain = new PlayerIntent { PlayerId = 4, Tick = 2, MoveX = 1, ConsumableSlot = 2 };
            var bytes = plain.ToBytes();
            Assert.Equal(i.ToBytes().Length - PlayerIntent.ItemBlockLength, bytes.Length);   // no item block unless an item action is set
            Assert.Equal(2, bytes[bytes.Length - 1]);                                        // the consumable byte is still last
            var old = PlayerIntent.FromBytes(bytes);
            Assert.False(old.AnyItemAction);
            var older = PlayerIntent.FromBytes(bytes.Take(bytes.Length - 1).ToArray());     // before the consumable byte
            Assert.False(older.AnyItemAction); Assert.Equal(0, older.ConsumableSlot);
        }

        // ---- taking a chosen item ----

        [Fact]
        public void TakeItemIdTakesTheChosenItemNotTheFirstAndOnlyOnce()
        {
            var s = Open(teammate: false);
            var room = ChestRoom(s, 2);
            OpenChest(s, room, 1);
            var visible = s.State.VisibleItemsIn(room.Id).OrderBy(i => i).ToList();
            Assert.True(visible.Count >= 2);
            int chosen = visible.Last();
            var view = s.ItemsFor(1);
            Assert.True(view.LootInReach);
            Assert.Contains(view.Here, h => h.ItemId == chosen && h.Source == LootSourceKind.Chest && h.CanTake && h.Item == s.ItemById(chosen));
            Assert.Equal("item", view.Interaction);

            int before = s.State.Log.Count(e => e.Kind == MatchEventKind.ItemTaken);
            Send(s, new PlayerIntent { TakeItemId = chosen });
            Assert.Equal(1, s.State.ItemOwner[chosen]);
            Assert.False(s.State.ItemOwner.ContainsKey(visible.First()), "the first item must stay in the chest");
            s.Step(0.05f); s.Step(0.05f);                                                  // the stored intent must not repeat the take
            Assert.Equal(before + 1, s.State.Log.Count(e => e.Kind == MatchEventKind.ItemTaken));
            Assert.Equal(0, s.RejectedCount);
            var carried = s.ItemsFor(1).Carried.Single(c => c.ItemId == chosen);
            Assert.False(carried.Worn);
            Assert.Equal("already yours", s.TakeRefusal(1, chosen));

            // Space still takes the first remaining item.
            Send(s, new PlayerIntent { Interact = true });
            Assert.Equal(1, s.State.ItemOwner[visible.First()]);
        }

        [Fact]
        public void AnUnappliedChoiceSurvivesANewerIntentWithoutOne()
        {
            var s = Open(teammate: false);
            var room = ChestRoom(s, 2);
            OpenChest(s, room, 1);
            int chosen = s.State.VisibleItemsIn(room.Id).Max();
            s.SetIntent(1, new PlayerIntent { PlayerId = 1, TakeItemId = chosen });
            s.SetIntent(1, new PlayerIntent { PlayerId = 1, MoveX = 0f });              // the next send, before the tick
            s.Step(0.05f);
            Assert.Equal(1, s.State.ItemOwner[chosen]);
        }

        [Fact]
        public void TakingIsRefusedOutOfReachAndWhileChannelling()
        {
            var s = Open(teammate: false);
            var room = ChestRoom(s, 2);
            OpenChest(s, room, 1);
            int item = s.State.VisibleItemsIn(room.Id).First();
            // Back to the spawn room: out of reach, nothing happens.
            s.Sim.Get(1).Position = Vec2.Of(s.Layout.Rooms[s.Descriptor.TeamSpawnRooms[0]].Center);
            Assert.Equal("not in that room", s.TakeRefusal(1, item));
            Send(s, new PlayerIntent { TakeItemId = item });
            Assert.False(s.State.ItemOwner.ContainsKey(item));
            Assert.Empty(s.ItemsFor(1).Here.Where(h => h.ItemId == item));
            // A closed chest elsewhere: not visible, not takeable.
            var other = s.Layout.Rooms.First(r => r.Id != room.Id && !r.IsBossRoom && r.Type != RoomType.Vault && r.Loot.Count > 0);
            int hidden = s.State.Index.ItemIds[other.Loot[0]];
            s.Sim.Get(1).Position = Inside(s, other);
            Assert.Equal("the chest is not opened", s.TakeRefusal(1, hidden));
            // Holding Space starts the chest channel; while it runs no item can be taken or dropped.
            Send(s, new PlayerIntent { Interact = true });
            Assert.Equal("chest", s.ChannelOf(1));
            Assert.Equal("busy channelling", s.TakeRefusal(1, hidden));
            int worn = s.BroughtItemsOf(1).First();
            Assert.Equal("busy channelling", s.DropRefusal(1, worn));
        }

        // ---- dropping ----

        [Fact]
        public void DropItemIdPutsItOnTheFloorWhereAnyoneInTheRoomMayTakeItAndTheViewShowsIt()
        {
            var s = Open(teammate: false);
            var room = ChestRoom(s, 1);
            OpenChest(s, room, 1);
            int worn = s.BroughtItemsOf(1).First();
            var before = s.ItemsFor(1);
            Assert.True(before.Carried.Single(c => c.ItemId == worn).Worn);
            Send(s, new PlayerIntent { DropItemId = worn });
            Assert.False(s.State.ItemOwner.ContainsKey(worn));
            Assert.Equal(room.Id, s.State.FloorItems[worn]);
            var after = s.ItemsFor(1);
            Assert.DoesNotContain(after.Carried, c => c.ItemId == worn);
            Assert.Contains(after.Here, h => h.ItemId == worn && h.Source == LootSourceKind.Floor && h.CanTake);
            Assert.True(after.LoadKg < before.LoadKg - s.ItemById(worn).WeightKg + 0.01f);
            // The team's view carries the brought-in piece on the floor (it is not generated loot), once the room is seen.
            Run(s, 0.6f);
            Assert.Contains(room.Id, s.State.Explored[0]);
            var view = TeamView.FromBytes(TeamView.For(s.State, 0, s.ItemById).ToBytes());
            Assert.Contains(worn, view.FloorItems);
            Assert.Equal(s.ItemById(worn).Name, view.Items.Single(i => i.Id == worn).Name);
            // Taken back: carried now, no longer worn.
            Send(s, new PlayerIntent { TakeItemId = worn });
            Assert.Equal(1, s.State.ItemOwner[worn]);
            Assert.False(s.ItemsFor(1).Carried.Single(c => c.ItemId == worn).Worn);
            Assert.False(s.IsWorn(worn));
            Assert.Equal(0, s.RejectedCount);
        }

        [Fact]
        public void YouCanOnlyDropYourOwnItems()
        {
            var s = Open(teammate: false);
            int theirs = s.BroughtItemsOf(1).First();
            Assert.Equal("not yours", s.DropRefusal(2, theirs));
            Send(s, new PlayerIntent { DropItemId = theirs }, player: 2);
            Assert.Equal(1, s.State.ItemOwner[theirs]);
        }

        // ---- weight ----

        [Fact]
        public void TheCarryViewAddsWornCarriedAndPocketsAgainstTheSheetCapacity()
        {
            var s = Open(teammate: false);
            var v = s.ItemsFor(1);
            float worn = s.BroughtItemsOf(1).Sum(id => s.ItemById(id).WeightKg);
            Assert.Equal(worn + 2 * Consumable.ByName("Health Potion").WeightKg, v.LoadKg, 3);
            Assert.Equal(s.Sim.Get(1).Stats.CarryKg, v.CapacityKg, 3);
            Assert.Equal(CarryRules.MoveSpeedFactor(v.LoadKg, v.CapacityKg), v.SpeedFactor);
            Assert.All(v.Carried, c => Assert.True(c.Worn));
            Assert.Equal(GearScore.Of(v.Carried.Select(c => c.Item)), GearScore.Of(s.WornBy(1)));
        }

        // ---- group rolls ----

        [Fact]
        public void RollPromptsListTheRollsAPlayerIsInAndRollChoiceReachesTheState()
        {
            var s = Open(teammate: true);
            var room = ChestRoom(s, 1, wantRoll: true);
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.PlayerEnteredRoom, s.Sim.Time, 3, room.Id)));   // the teammate stands near
            OpenChest(s, room, 1);
            Assert.NotEmpty(s.State.OpenRolls);
            int item = s.State.OpenRolls.Keys.Min();
            var mine = s.ItemsFor(1).Rolls.Single(r => r.ItemId == item);
            Assert.Null(mine.Answered); Assert.Equal(2, mine.Eligible); Assert.Equal(0, mine.Answers);
            Assert.InRange(mine.SecondsLeft, LootRollRules.RollSeconds - 1f, LootRollRules.RollSeconds);
            Assert.Equal(LootRollRules.FitsClass(Cls("Warden"), s.ItemById(item).Base), mine.OnClass);
            // While the group rolls, the eligible may not grab it.
            Assert.Equal("your group is rolling for it", s.TakeRefusal(1, item));
            Assert.Contains(s.ItemsFor(1).Here, h => h.ItemId == item && h.InRoll && !h.CanTake);
            // The enemy is in no roll.
            Assert.Empty(s.ItemsFor(2).Rolls);
            Assert.Equal("not in this roll", s.RollRefusal(2, item, LootRollChoice.Need));

            Send(s, new PlayerIntent { RollItemId = item, RollChoice = (byte)LootRollChoice.Need });
            Assert.Equal(LootRollChoice.Need, s.State.OpenRolls[item].Choices[1]);
            Assert.Equal(LootRollChoice.Need, s.ItemsFor(1).Rolls.Single(r => r.ItemId == item).Answered);
            Assert.Equal("already answered", s.RollRefusal(1, item, LootRollChoice.Greed));
            Assert.Contains(s.State.Log, e => e.Kind == MatchEventKind.LootRolled && e.A == item && e.B == 1 && e.C == (int)LootRollChoice.Need);
            // The teammate passes: the roll resolves to player 1, who may now take it.
            Assert.True(s.AnswerRoll(3, item, LootRollChoice.Pass));
            s.Step(0.05f);
            Assert.False(s.State.OpenRolls.ContainsKey(item));
            Assert.Equal(1, s.State.LootWinner[item]);
            Assert.Null(s.TakeRefusal(1, item));
            s.Sim.Get(3).Position = Inside(s, room);
            Assert.Equal("a teammate won it", s.TakeRefusal(3, item));
            Assert.Equal(0, s.RejectedCount);
        }

        // ---- corpses ----

        [Fact]
        public void TakeItemIdPicksAChosenPieceOffAnOpenedCorpseWithinReach()
        {
            var s = Open(teammate: false);
            var victim = s.Sim.Get(1);
            var held = s.BroughtItemsOf(1).ToList();
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.PlayerDied, s.Sim.Time, 1, 2, s.RoomOf(1))));
            var left = s.State.Corpses[1].OrderBy(i => i).ToList();
            Assert.NotEmpty(left);
            var looter = s.Sim.Get(2);
            looter.Position = victim.Position + new Vec2(3f, 0f);
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.LootStarted, s.Sim.Time, 2, 1, 1)));
            Run(s, FightRules.CorpseLootSeconds + 0.1f);
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.CorpseOpened, s.Sim.Time, 1, 2)), string.Join("; ", s.RecentRejections));
            left = s.State.Corpses[1].OrderBy(i => i).ToList();
            int chosen = left.Last();
            looter.Position = victim.Position + new Vec2(3f, 0f);
            Assert.Equal("not at that corpse", s.TakeRefusal(2, chosen));
            looter.Position = victim.Position + new Vec2(0.5f, 0f);
            var v = s.ItemsFor(2);
            Assert.Equal(1, v.Corpse);
            Assert.Contains(v.Here, h => h.ItemId == chosen && h.Source == LootSourceKind.Corpse && h.CanTake);
            Send(s, new PlayerIntent { TakeItemId = chosen }, player: 2);
            Assert.Equal(2, s.State.ItemOwner[chosen]);
            if (left.Count > 1) Assert.Contains(left.First(), s.State.Corpses[1]);
        }

        // ---- between rounds: stash and paper doll ----

        private static Item Make(string baseName, int level, Rarity r, UniqueItem u = null) =>
            new Item(ItemBase.ByName(baseName), level, r, u?.Name ?? baseName, new List<AffixRoll>(), new List<AffixRoll>(), u);

        [Fact]
        public void EquippingFromTheStashSwapsTheOldPieceIntoTheStashAndMovesTheGearScore()
        {
            var p = PlayerProfile.NewAccount("a", "Bram", Cls("Warden"));
            var ch = p.Characters[0];
            var helm = Make("Plate Helm", 3, Rarity.Rare);
            p.Stash.Add(helm);
            int gs = GearScore.Of(ch.Equipment);
            var old = ch.Equipment.Head;
            Assert.Equal(old, EquipPreview.Displaced(ch.Equipment, helm));
            Assert.Equal(GearScore.Of(helm) - GearScore.Of(old), EquipPreview.GearScoreDelta(ch.Equipment, helm));
            Assert.Equal(gs + GearScore.Of(helm) - GearScore.Of(old), StashActions.GearScoreIfWorn(ch, helm));
            Assert.Null(StashActions.Equip(p, ch, helm));
            Assert.Same(helm, ch.Equipment.Head);
            Assert.Contains(old, p.Stash);
            Assert.DoesNotContain(helm, p.Stash);
            Assert.Equal(gs + GearScore.Of(helm) - GearScore.Of(old), GearScore.Of(ch.Equipment));
            // Off again into the stash.
            Assert.Null(StashActions.Unequip(p, ch, helm));
            Assert.Null(ch.Equipment.Head);
            Assert.Contains(helm, p.Stash);
            Assert.NotNull(StashActions.Unequip(p, ch, helm));                            // not worn any more
            Assert.NotNull(StashActions.Equip(p, ch, Make("Ring", 1, Rarity.Magic)));     // not in the stash
        }

        [Fact]
        public void OneOfEachUniqueIsWornButACopyMaySwapForTheCopyInItsSlot()
        {
            var p = PlayerProfile.NewAccount("a", "Bram", Cls("Warden"));
            var ch = p.Characters[0];
            var prism = UniqueItem.All.First(u => u.Name == "Glasswright's Prism");
            var a = Make("Ring", 2, Rarity.Unique, prism);
            var b = Make("Ring", 4, Rarity.Unique, prism);
            var plain = Make("Ring", 1, Rarity.Magic);
            p.Stash.AddRange(new[] { a, b, plain });
            Assert.Null(StashActions.Equip(p, ch, a));                                     // Ring1
            Assert.Null(StashActions.Equip(p, ch, plain));                                 // Ring2
            // Both rings full: the next ring replaces Ring1, which holds the copy: a copy for a copy is fine.
            Assert.Same(a, EquipPreview.Displaced(ch.Equipment, b));
            Assert.Null(StashActions.Equip(p, ch, b));
            Assert.Same(b, ch.Equipment.Ring1); Assert.Contains(a, p.Stash);
            // Take the plain ring off; now the copy would go in the free slot beside the worn one: refused.
            Assert.Null(StashActions.Unequip(p, ch, plain));
            var why = StashActions.Equip(p, ch, a);
            Assert.Contains("one of each unique", why);
            Assert.Contains(a, p.Stash);
        }

        [Fact]
        public void TheTierHintNamesTheTiersAGearScoreAdmits()
        {
            Assert.Contains("admits Tier I (", StashActions.TierHint(6));
            Assert.Contains("Tier I and Tier II", StashActions.TierHint(40));
            Assert.Contains("Tier III 130+", StashActions.TierHint(200));
            var doll = StashActions.PaperDoll(PlayerProfile.NewAccount("a", "Bram", Cls("Warden")).Characters[0]);
            Assert.Equal(9, doll.Count);
            Assert.Equal(6, doll.Count(d => d.item != null));                               // weapon and five armour pieces
        }

        [Fact]
        public void TheVendorLoopBuysSellsAndSalvagesThroughTheProfile()
        {
            var p = PlayerProfile.NewAccount("a", "Bram", Cls("Warden"));
            var potion = Consumable.ByName("Health Potion");
            long silver = p.Silver; int have = p.ConsumableCount(potion);
            Assert.True(Vendor.Buy(p, potion));
            Assert.Equal(silver - potion.VendorPriceSilver, p.Silver); Assert.Equal(have + 1, p.ConsumableCount(potion));
            var rare = Make("Longsword", 2, Rarity.Rare);
            p.Stash.Add(rare);
            float price = CraftingRules.SellPrice(rare);
            Assert.Equal(price, Vendor.Sell(p, rare));
            Assert.DoesNotContain(rare, p.Stash);
            var magic = Make("Ring", 1, Rarity.Magic);
            p.Stash.Add(magic);
            int scrap = p.Materials.Scrap;
            var bag = Vendor.Salvage(p, magic);
            Assert.Equal(scrap + bag.Scrap, p.Materials.Scrap);
            Assert.True(bag.Scrap > 0);
            // Round-trips through the save.
            var back = ProfileCodec.FromBytes(ProfileCodec.ToBytes(p));
            Assert.Equal(p.Silver, back.Silver); Assert.Equal(p.ConsumableCount(potion), back.ConsumableCount(potion)); Assert.Equal(p.Materials.Scrap, back.Materials.Scrap);
        }
    }
}
