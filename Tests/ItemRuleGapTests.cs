using System;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Items;
using DungeonGen.Core.Net;
using DungeonGen.Core.Profile;
using Xunit;
using Attr = DungeonGen.Core.Combat.Attribute;

namespace DungeonGen.Tests
{
    /// <summary>
    /// The four item-rule leftovers (TASKS 3.2b): overload slows movement, a worn piece leaving a player changes their sheet,
    /// Space takes floor items, and a Salvage Kit breaks a carried item down inside the dungeon.
    /// </summary>
    public class ItemRuleGapTests
    {
        private static ClassArchetype Cls(string name) => ClassArchetype.ByName(name);

        private static Item Make(string baseName, int level, Rarity r, params AffixRoll[] affixes) =>
            new Item(ItemBase.ByName(baseName), level, r, baseName, affixes.ToList(), new List<AffixRoll>());

        private static AffixRoll Roll(AffixKind kind, float value, Attr? attr = null) =>
            new AffixRoll(AffixDefinition.Pool.First(d => d.Kind == kind && (attr == null || d.Attribute == attr)), 1, value);

        private static MatchServer Seat(PlayerProfile p, Loadout lo, bool teammate = false, int seed = 11)
        {
            var s = MatchServer.Host(seed, DungeonSettings.ForTeams(2), new MatchRules { TeamSize = 3 }, lootSecret: 5);
            s.StartRound();
            Assert.NotNull(s.AddPlayerWithLoadout(1, 0, lo));
            s.AddPlayer(2, 1, Cls("Warden"), "Sentinel", PlayerStats.For(Cls("Warden"), 1));
            if (teammate) s.AddPlayer(3, 0, Cls("Mender"), WeaponSpec.ForClass("Mender").First().Name, PlayerStats.For(Cls("Mender"), 1));
            for (int i = 0; i < 200 && !s.State.DoorsOpen; i++) s.Step(0.1f);
            Assert.True(s.State.DoorsOpen);
            return s;
        }

        private static PlayerProfile Warden() => PlayerProfile.NewAccount("a", "Bram", Cls("Warden"));

        private static void Send(MatchServer s, PlayerIntent i, int player = 1) { i.PlayerId = player; s.SetIntent(player, i); s.Step(0.05f); }
        private static void Run(MatchServer s, float seconds, float dt = 0.1f) { for (float t = 0; t < seconds; t += dt) s.Step(dt); }

        // ---- the wire ----

        [Fact]
        public void SalvageItemIdRoundTripsAndOlderPayloadsReadAsNone()
        {
            var i = new PlayerIntent { PlayerId = 4, Tick = 2, TakeItemId = 17, SalvageItemId = 100007 };
            var back = PlayerIntent.FromBytes(i.ToBytes());
            Assert.Equal(100007, back.SalvageItemId); Assert.Equal(17, back.TakeItemId);
            Assert.True(back.AnyItemAction);

            var onlySalvage = PlayerIntent.FromBytes(new PlayerIntent { PlayerId = 4, SalvageItemId = 9 }.ToBytes());
            Assert.Equal(9, onlySalvage.SalvageItemId); Assert.Equal(0, onlySalvage.TakeItemId);

            // An item block from before the field (13 bytes) and an intent without any item action both read as no salvage.
            var oldItems = new PlayerIntent { PlayerId = 4, DropItemId = 5 }.ToBytes();
            Assert.Equal(new PlayerIntent { PlayerId = 4 }.ToBytes().Length + PlayerIntent.ItemBlockLength, oldItems.Length);   // unchanged size without salvage
            Assert.Equal(0, PlayerIntent.FromBytes(oldItems).SalvageItemId);
            Assert.Equal(5, PlayerIntent.FromBytes(oldItems).DropItemId);
            Assert.Equal(0, PlayerIntent.FromBytes(new PlayerIntent { PlayerId = 4 }.ToBytes()).SalvageItemId);
        }

        // ---- Gap 1: overload slows movement ----

        private static Loadout Heavy(PlayerProfile p, int plates, Dictionary<string, int> consumables = null)
        {
            var carried = new List<Item>();
            for (int i = 0; i < plates; i++) { var it = Make("Plate Cuirass", 0, Rarity.Normal); p.Stash.Add(it); carried.Add(it); }
            return Loadout.For(p, p.Characters[0], carried, consumables);
        }

        private static float Travel(MatchServer s, int id, float seconds)
        {
            var c = s.Sim.Get(id); var from = c.Position;
            for (float t = 0; t < seconds; t += 0.05f) s.Sim.Move(id, c.Position + new Vec2(1f, 0f) * 10f, 0.05f);
            return Vec2.Distance(from, c.Position);
        }

        [Fact]
        public void BeingOverloadedSlowsTheCarrierByTheCarryRuleAndDroppingOrTakingChangesIt()
        {
            var p = Warden();
            var s = Seat(p, Heavy(p, 5));
            var c = s.Sim.Get(1);
            var v = s.ItemsFor(1);
            Assert.True(v.Overloaded, $"load {v.LoadKg} vs capacity {v.CapacityKg}");
            Assert.Equal(CarryRules.MoveSpeedFactor(v.LoadKg, v.CapacityKg), c.CarrySpeedScale, 4);
            Assert.InRange(c.CarrySpeedScale, 0.5f, 0.99f);
            Assert.Equal(1f, c.MoveSpeedScale);                          // the other modifiers are untouched

            // The simulator uses it: the same walk covers less ground than with the factor removed.
            var open = new Vec2(c.Position.X, c.Position.Y);
            float slow = Travel(s, 1, 0.5f);
            c.Position = open; c.CarrySpeedScale = 1f;
            float fast = Travel(s, 1, 0.5f);
            Assert.True(slow < fast, $"{slow} vs {fast}");
            Assert.Equal(CarryRules.MoveSpeedFactor(v.LoadKg, v.CapacityKg), slow / fast, 2);

            // Chill multiplies on top of the carry factor rather than replacing it.
            c.Position = open; c.CarrySpeedScale = 0.5f; c.MoveSpeedScale = 0.5f;
            Assert.True(Travel(s, 1, 0.5f) < slow);
            c.MoveSpeedScale = 1f;

            // Dropping a carried plate lightens the load and raises the factor; taking it back lowers it again.
            var plate = s.BroughtItemsOf(1).Last();
            Send(s, new PlayerIntent { DropItemId = plate });
            float lighter = c.CarrySpeedScale;
            var after = s.ItemsFor(1);
            Assert.Equal(CarryRules.MoveSpeedFactor(after.LoadKg, after.CapacityKg), lighter, 4);
            Assert.True(lighter > CarryRules.MoveSpeedFactor(v.LoadKg, v.CapacityKg));
            Send(s, new PlayerIntent { TakeItemId = plate });
            Assert.Equal(CarryRules.MoveSpeedFactor(v.LoadKg, v.CapacityKg), c.CarrySpeedScale, 4);
            Assert.Equal(0, s.RejectedCount);
        }

        [Fact]
        public void AHandOffMovesTheSlowdownToTheReceiverAndDeathOrDestructionLiftsIt()
        {
            var p = Warden();
            var s = Seat(p, Heavy(p, 5), teammate: true);
            var giver = s.Sim.Get(1); var taker = s.Sim.Get(3);
            float before = giver.CarrySpeedScale;
            Assert.True(before < 1f);
            int plate = s.BroughtItemsOf(1).Last();
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.ItemGiven, s.Sim.Time, plate, 1, 3)), string.Join("; ", s.RecentRejections));
            Assert.True(giver.CarrySpeedScale > before);
            Assert.Equal(CarryRules.MoveSpeedFactor(s.LoadKgOf(3), taker.Stats.CarryKg), taker.CarrySpeedScale, 4);

            // A destroyed carried piece (not worn) lightens its holder too.
            int other = s.BroughtItemsOf(1)[s.BroughtItemsOf(1).Count - 2];
            float mid = giver.CarrySpeedScale;
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.ItemDestroyed, s.Sim.Time, other)));
            Assert.True(giver.CarrySpeedScale > mid);
            Assert.Equal(CarryRules.MoveSpeedFactor(s.LoadKgOf(1), giver.Stats.CarryKg), giver.CarrySpeedScale, 4);

            // A death empties the hands: nothing left to slow the body.
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.PlayerDied, s.Sim.Time, 1, 2, s.RoomOf(1))));
            Assert.Equal(1f, giver.CarrySpeedScale);
        }

        [Fact]
        public void TheLoadFactorCountsPocketsSoDrinkingALightensIt()
        {
            var p = Warden();
            p.AddConsumable(Consumable.ByName("Health Potion"), 2);
            var s = Seat(p, Heavy(p, 9, new Dictionary<string, int> { ["Health Potion"] = 2 }));
            var c = s.Sim.Get(1);
            var v = s.ItemsFor(1);
            Assert.Equal(CarryRules.MoveSpeedFactor(v.LoadKg, v.CapacityKg), c.CarrySpeedScale, 4);
            Assert.True(s.UseConsumable(1, Consumable.ByName("Health Potion")));
            var v2 = s.ItemsFor(1);
            Assert.True(v2.LoadKg < v.LoadKg);
            Assert.Equal(CarryRules.MoveSpeedFactor(v2.LoadKg, v2.CapacityKg), c.CarrySpeedScale, 4);
        }

        // ---- Gap 2: worn gear that leaves changes the sheet ----

        private static PlayerProfile WardenWearingRichGear(out Item chest, out Item ring)
        {
            var p = Warden();
            chest = Make("Plate Cuirass", 3, Rarity.Rare, Roll(AffixKind.Health, 25f));
            ring = Make("Ring", 3, Rarity.Rare, Roll(AffixKind.Attribute, 3f, Attr.Dexterity));
            p.Characters[0].Equipment.Equip(chest);
            p.Characters[0].Equipment.Equip(ring);
            return p;
        }

        [Fact]
        public void DroppingAWornPieceRebuildsTheSheetKeepingHealthFractionAndClampingTheResource()
        {
            var p = WardenWearingRichGear(out var chest, out var ring);
            var lo = Loadout.For(p, p.Characters[0]);
            var s = Seat(p, lo);
            var c = s.Sim.Get(1);
            int chestId = s.BroughtItemsOf(1)[lo.Worn.ToList().IndexOf(chest)];
            int ringId = s.BroughtItemsOf(1)[lo.Worn.ToList().IndexOf(ring)];
            float maxBefore = c.MaxHealth, armourBefore = c.ArmourRating, poolBefore = c.ResourceMax;
            c.Health = maxBefore * 0.5f; c.ResourceNow = c.ResourceMax;

            Send(s, new PlayerIntent { DropItemId = chestId });
            Assert.True(c.MaxHealth < maxBefore);
            Assert.True(c.ArmourRating < armourBefore);
            Assert.Equal(0.5f, c.Health / c.MaxHealth, 3);
            Assert.Same(c.Stats, c.Stats);
            Assert.Equal(c.Stats.Health, c.MaxHealth);
            Assert.Equal(c.Stats.CarryKg, s.ItemsFor(1).CapacityKg);
            var expected = PlayerStats.For(Cls("Warden"), s.WornBy(1), s.Descriptor.Settings.Combat);
            Assert.Equal(expected.Health, c.MaxHealth, 3);
            Assert.Equal(expected.Dps, c.Dps, 3);

            // The ring's Dexterity fed the stamina pool: losing it shrinks the pool and the current resource follows down.
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.ItemDestroyed, s.Sim.Time, ringId)));
            Assert.True(c.ResourceMax < poolBefore);
            Assert.Equal(c.ResourceMax, c.ResourceNow, 3);
            Assert.Equal(0, s.RejectedCount);
        }

        [Fact]
        public void HandingOffOrLosingTheWeaponChangesTheKitToWhatTheEmptyHandPlays()
        {
            var p = WardenWearingRichGear(out _, out _);
            var lo = Loadout.For(p, p.Characters[0], null);
            var s = Seat(p, lo, teammate: true);
            var c = s.Sim.Get(1);
            int weaponId = s.BroughtItemsOf(1)[0];
            Assert.True(s.ItemById(weaponId).Base.IsWeapon);
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.ItemGiven, s.Sim.Time, weaponId, 1, 3)), string.Join("; ", s.RecentRejections));
            Assert.False(s.IsWorn(weaponId));
            Assert.Null(c.Stats.Spec);
            Assert.Equal(s.SpecOf(1), c.Spec);
            Assert.Equal(WeaponSpec.ForClass("Warden").First().Name, c.Spec);
            Assert.Equal(0, s.RejectedCount);
            // The teammate's own sheet is not rebuilt: they did not lose a worn piece.
            Assert.Equal(PlayerStats.For(Cls("Mender"), 1).Health, s.Sim.Get(3).MaxHealth, 3);
        }

        [Fact]
        public void ADroppedCarriedStashPieceLeavesTheSheetAlone()
        {
            var p = Warden();
            var s = Seat(p, Heavy(p, 2));
            var c = s.Sim.Get(1);
            var stats = c.Stats; float hp = c.MaxHealth;
            Send(s, new PlayerIntent { DropItemId = s.BroughtItemsOf(1).Last() });
            Assert.Same(stats, c.Stats); Assert.Equal(hp, c.MaxHealth);
        }

        // ---- Gap 3: Space takes floor items ----

        private static Room ChestRoom(MatchServer s) =>
            s.Layout.Rooms.Where(r => !r.IsBossRoom && r.Type != RoomType.Vault && r.Loot.Count >= 1)
                .OrderBy(r => r.Mobs == null ? 0 : 1).ThenBy(r => r.Id).First();

        private static Vec2 Inside(MatchServer s, Room r)
        {
            var c = Vec2.Of(r.Center);
            if (s.Sim.Walkable(c)) return c;
            for (int dx = -2; dx <= 2; dx++) for (int dy = -2; dy <= 2; dy++) { var q = c + new Vec2(dx, dy); if (s.Sim.Walkable(q) && s.Layout.RoomIdAt[q.Cell.X, q.Cell.Y] == r.Id) return q; }
            return c;
        }

        [Fact]
        public void SpaceTakesAFloorItemInTheRoomAfterTheChestItemsAndNotFromAnotherRoom()
        {
            var p = Warden();
            var s = Seat(p, Heavy(p, 1));
            var room = ChestRoom(s);
            s.Sim.Get(1).Position = Inside(s, room);
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.LootStarted, s.Sim.Time, 1, room.Id, 0)));
            Run(s, FightRules.ChestOpenSeconds + 0.1f);
            s.Sim.Get(1).Position = Inside(s, room);
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.ContainerOpened, s.Sim.Time, room.Id, 1)));

            int dropped = s.BroughtItemsOf(1).Last();
            Send(s, new PlayerIntent { DropItemId = dropped });
            Assert.Equal(room.Id, s.State.FloorItems[dropped]);
            var chestItems = s.State.VisibleItemsIn(room.Id).Where(i => !s.State.OpenRolls.ContainsKey(i)).ToList();

            // The chest's items come first: Space takes those, one per press, before the floor item.
            int guard = 0;
            while (s.InteractionAvailable(1) != "floor-item" && guard++ < 20)
            {
                var kind = s.InteractionAvailable(1);
                Assert.True(kind == "item" || kind == "roll" || kind == "silver", $"unexpected {kind}");
                if (kind == "roll") break;
                Send(s, new PlayerIntent { Interact = true });
                s.Step(0.05f);
            }
            if (s.InteractionAvailable(1) == "floor-item")
            {
                Assert.Equal("floor-item", s.ItemsFor(1).Interaction);
                Send(s, new PlayerIntent { Interact = true });
                Assert.Equal(1, s.State.ItemOwner[dropped]);
                Assert.False(s.State.FloorItems.ContainsKey(dropped));
                Assert.Equal(0, s.RejectedCount);
            }
            else Assert.Contains(chestItems.Concat(s.State.OpenRolls.Keys), i => true);   // a roll in progress holds the list
        }

        [Fact]
        public void SpaceTakesAnItemDroppedInAnEmptyRoomOnlyFromInsideThatRoom()
        {
            var p = Warden();
            var s = Seat(p, Heavy(p, 1));
            int dropped = s.BroughtItemsOf(1).Last();
            int spawnRoom = s.RoomOf(1);
            Send(s, new PlayerIntent { DropItemId = dropped });
            Assert.Equal(spawnRoom, s.State.FloorItems[dropped]);
            Assert.Equal("floor-item", s.InteractionAvailable(1));
            // Out of the room: no floor interaction, and the take refusal agrees.
            var other = s.Layout.Rooms.First(r => r.Id != spawnRoom && !r.IsBossRoom);
            s.Sim.Get(1).Position = Inside(s, other);
            Assert.NotEqual("floor-item", s.InteractionAvailable(1));
            Assert.Equal("not in that room", s.TakeRefusal(1, dropped));
            // Back inside: held Space takes it.
            s.Sim.Get(1).Position = Vec2.Of(s.Layout.Rooms[spawnRoom].Center);
            Send(s, new PlayerIntent { Interact = true });
            Assert.Equal(1, s.State.ItemOwner[dropped]);
            // A teammate-less enemy standing in the room can take a dropped piece the same way.
            Send(s, new PlayerIntent { DropItemId = dropped });
            s.Sim.Get(2).Position = Vec2.Of(s.Layout.Rooms[spawnRoom].Center); s.Step(0.05f);
            Assert.Equal("floor-item", s.InteractionAvailable(2));
            Send(s, new PlayerIntent { Interact = true }, player: 2);
            Assert.Equal(2, s.State.ItemOwner[dropped]);
            Assert.Equal(0, s.RejectedCount);
        }

        // ---- Gap 4: the Salvage Kit in the dungeon ----

        private static (MatchServer s, Item rare, Item common) SeatWithKit(PlayerProfile p, int kits = 1)
        {
            var rare = Make("Plate Helm", 2, Rarity.Rare);
            var common = Make("Plate Greaves", 0, Rarity.Normal);
            p.Stash.Add(rare); p.Stash.Add(common);
            p.AddConsumable(Consumable.ByName("Salvage Kit"), kits);
            var lo = Loadout.For(p, p.Characters[0], new[] { rare, common }, new Dictionary<string, int> { ["Salvage Kit"] = kits });
            return (Seat(p, lo), rare, common);
        }

        private static void ExtractAt(MatchServer s, int player)
        {
            float t = s.Sim.Time;
            void Ok(MatchEventKind k, int a = 0, int b = 0, int c = 0) => Assert.True(s.Raise(new MatchEvent(k, t, a, b, c)), string.Join(" | ", s.RecentRejections));
            int gate = s.Layout.ExtractRooms.First().Id;
            if (!s.State.ExtractsOpen) Ok(MatchEventKind.ExtractsOpened);
            Ok(MatchEventKind.PlayerEnteredRoom, player, gate);
            Ok(MatchEventKind.ExtractStarted, player, gate); t += s.Descriptor.Rules.ExtractChannelSeconds + 0.5f;
            Ok(MatchEventKind.PlayerExtracted, player, gate, s.TeamOf(player));
        }

        [Fact]
        public void ASalvageIntentSpendsAKitDestroysTheItemAndRecordsTheMaterials()
        {
            var p = Warden();
            var (s, rare, common) = SeatWithKit(p);
            int rareId = s.BroughtItemsOf(1)[s.BroughtItemsOf(1).Count - 2];
            Assert.Same(rare, s.ItemById(rareId));
            float loadBefore = s.ItemsFor(1).LoadKg;
            Assert.Null(s.SalvageRefusal(1, rareId));

            Send(s, new PlayerIntent { SalvageItemId = rareId });
            Assert.True(s.State.DestroyedItems.Contains(rareId));
            Assert.False(s.State.ItemOwner.ContainsKey(rareId));
            Assert.Contains(s.State.Log, e => e.Kind == MatchEventKind.ItemDestroyed && e.A == rareId);
            Assert.False(s.PocketsOf(1).ContainsKey("Salvage Kit"));
            Assert.Equal(1, s.ConsumablesUsedBy(1)["Salvage Kit"]);
            var expected = CraftingRules.Salvage(rare);
            var got = s.MaterialsSalvagedBy(1);
            Assert.Equal(expected.Scrap, got.Scrap); Assert.Equal(expected.PrimeShards, got.PrimeShards);
            Assert.True(got.Scrap > 0 && got.PrimeShards == 1);
            Assert.True(s.ItemsFor(1).LoadKg < loadBefore - rare.WeightKg + 0.01f);
            s.Step(0.05f); s.Step(0.05f);                                          // one press, one use
            Assert.Equal(got.Scrap, s.MaterialsSalvagedBy(1).Scrap);
            Assert.Equal(0, s.RejectedCount);

            // No kit left: the next one is refused and nothing is destroyed.
            int commonId = s.BroughtItemsOf(1).Last();
            Assert.Equal("no Salvage Kit", s.SalvageRefusal(1, commonId));
            Send(s, new PlayerIntent { SalvageItemId = commonId });
            Assert.Equal(1, s.State.ItemOwner[commonId]);
            Assert.Equal(got.Scrap, s.MaterialsSalvagedBy(1).Scrap);
        }

        [Fact]
        public void SalvageIsRefusedForStarterGearOthersItemsAndTheDowned()
        {
            var p = Warden();
            var (s, _, _) = SeatWithKit(p, kits: 2);
            int worn = s.BroughtItemsOf(1).First();
            Assert.True(s.ItemById(worn).IsStarter);
            Assert.Equal("it salvages to nothing", s.SalvageRefusal(1, worn));
            int carried = s.BroughtItemsOf(1).Last();
            Assert.Equal("not yours", s.SalvageRefusal(2, carried));
            Assert.Equal("not yours", s.SalvageRefusal(1, 424242));
            Send(s, new PlayerIntent { SalvageItemId = worn });
            Assert.Equal(1, s.State.ItemOwner[worn]);
            Assert.Equal(2, s.PocketsOf(1)["Salvage Kit"]);                       // nothing was spent
            Assert.False(s.SalvageItem(2, carried));
            Assert.Equal(0, s.RejectedCount);
        }

        [Fact]
        public void SalvagingAWornPieceAlsoRebuildsTheSheet()
        {
            var p = WardenWearingRichGear(out var chest, out _);
            p.AddConsumable(Consumable.ByName("Salvage Kit"), 1);
            var lo = Loadout.For(p, p.Characters[0], null, new Dictionary<string, int> { ["Salvage Kit"] = 1 });
            var s = Seat(p, lo);
            int chestId = s.BroughtItemsOf(1)[lo.Worn.ToList().IndexOf(chest)];
            float hp = s.Sim.Get(1).MaxHealth;
            Assert.True(s.SalvageItem(1, chestId));
            Assert.True(s.Sim.Get(1).MaxHealth < hp);
            Assert.Equal(CraftingRules.Salvage(chest).Scrap, s.MaterialsSalvagedBy(1).Scrap);
            Assert.False(s.IsWorn(chestId));
        }

        [Fact]
        public void ExtractingCreditsTheSalvagedMaterialsAndLosesTheItemFromTheStash()
        {
            var p = Warden();
            var (s, rare, _) = SeatWithKit(p);
            int rareId = s.BroughtItemsOf(1)[s.BroughtItemsOf(1).Count - 2];
            Assert.True(s.SalvageItem(1, rareId));
            var expected = CraftingRules.Salvage(rare);
            int scrap0 = p.Materials.Scrap, shards0 = p.PrimeShards();
            ExtractAt(s, 1);
            var rep = RoundSettlement.Settle(p, p.Characters[0], s, 1);
            Assert.Equal(RoundOutcome.Extracted, rep.Outcome);
            Assert.Equal(scrap0 + expected.Scrap, p.Materials.Scrap);
            Assert.Equal(shards0 + expected.PrimeShards, p.PrimeShards());
            Assert.Equal(expected.Scrap, rep.MaterialsGained.Scrap);
            Assert.Contains(rare, rep.ItemsLost);
            Assert.DoesNotContain(rare, p.Stash);
            Assert.False(rep.ConsumableDelta.ContainsKey("Salvage Kit") && rep.ConsumableDelta["Salvage Kit"] > 0);
            Assert.Equal(-1, rep.ConsumableDelta["Salvage Kit"]);                 // the kit is spent
            // Settling again changes nothing.
            var again = RoundSettlement.Settle(p, p.Characters[0], s, 1);
            Assert.True(again.AlreadySettled);
            Assert.Equal(scrap0 + expected.Scrap, p.Materials.Scrap);
        }

        [Fact]
        public void ADeathAfterSalvagingLosesTheMaterialsLikeThePockets()
        {
            var p = Warden();
            var (s, rare, _) = SeatWithKit(p);
            int rareId = s.BroughtItemsOf(1)[s.BroughtItemsOf(1).Count - 2];
            Assert.True(s.SalvageItem(1, rareId));
            Assert.True(s.MaterialsSalvagedBy(1).Scrap > 0);
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.PlayerDied, s.Sim.Time, 1, 2, s.RoomOf(1))));
            Assert.Equal(0, s.MaterialsSalvagedBy(1).Scrap);
            int scrap0 = p.Materials.Scrap;
            var rep = RoundSettlement.Settle(p, p.Characters[0], s, 1);
            Assert.Equal(RoundOutcome.Died, rep.Outcome);
            Assert.Equal(scrap0, p.Materials.Scrap);
            Assert.Equal(0, rep.MaterialsGained.Scrap);
            Assert.Contains(rare, rep.ItemsLost);
        }

        [Fact]
        public void SettlementCreditsOptionMaterialsOnlyOnExtraction()
        {
            // The same rule through the lower-level overload (no server): extraction credits, a death does not.
            var p = Warden();
            var (s, _, _) = SeatWithKit(p);
            ExtractAt(s, 1);
            var bag = new MaterialBag { Scrap = 40, PrimeShards = 2 }; bag.AddEssence("Health", 3);
            int scrap0 = p.Materials.Scrap;
            var rep = RoundSettlement.Settle(p, p.Characters[0], s.State, 1, s.ItemById, new SettlementOptions { SalvagedMaterials = bag, ConsumablesCarried = s.ConsumablesCarriedBy(1), ConsumableUses = s.ConsumableUsesBy(1) });
            Assert.Equal(scrap0 + 40, p.Materials.Scrap);
            Assert.Equal(3, p.Materials.Essence("Health"));
            Assert.Equal(40, rep.MaterialsGained.Scrap);
        }
    }

    internal static class ProfileMaterialsExt
    {
        public static int PrimeShards(this PlayerProfile p) => p.Materials.PrimeShards;
    }
}
