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
    /// The shrine on the server, as the owner decided it (2026-10-26 answers #1–#3): <see cref="PlayerIntent.ShrineAction"/> / <see cref="PlayerIntent.ShrineArg"/>
    /// on the wire, <see cref="MatchServer.ShrineOptionsFor"/> (what this player can do here and why not), and the two choices, each a
    /// <see cref="ShrineRules.ChannelSeconds"/> prayer at the altar (within <see cref="ShrineRules.ReachCells"/> of it) that walking, a cast or a hit breaks:
    /// arm the ultimate (2 souls, one use, then castable with R), resurrect a dead teammate (2 souls, 1 at the cheap-shrine event, once, naked at the shrine;
    /// the corpse stays where it lay with everything on it, lootable by anyone). No shop: the retired buy byte is refused. Bots walk to the altar and pray.
    /// </summary>
    public class ShrineTests
    {
        private static ClassArchetype Cls(string name) => ClassArchetype.ByName(name);

        /// <summary>Player 1 (a Warden in the free kit with a potion) and a Mender teammate (3) on team 0, an enemy Warden (2) on team 1; doors open.</summary>
        private static MatchServer Open(MatchRules rules = null, int seed = 11) => Open(out _, rules, seed);

        private static MatchServer Open(out PlayerProfile profile, MatchRules rules = null, int seed = 11)
        {
            var p = PlayerProfile.NewAccount("a", "Bram", Cls("Warden"));
            var lo = Loadout.For(p, p.Characters[0], null, new Dictionary<string, int> { ["Health Potion"] = 1 });
            var s = MatchServer.Host(seed, DungeonSettings.ForTeams(2), rules ?? new MatchRules { TeamSize = 3 }, lootSecret: 5);
            s.StartRound();
            Assert.NotNull(s.AddPlayerWithLoadout(1, 0, lo));
            s.AddPlayer(2, 1, Cls("Warden"), "Sentinel", PlayerStats.For(Cls("Warden"), 1));
            s.AddPlayer(3, 0, Cls("Mender"), WeaponSpec.ForClass("Mender").First().Name, PlayerStats.For(Cls("Mender"), 1));
            for (int i = 0; i < 200 && !s.State.DoorsOpen; i++) s.Step(0.1f);
            Assert.True(s.State.DoorsOpen);
            profile = p;
            return s;
        }

        private static Room Shrine(MatchServer s) => s.Layout.Rooms.Where(r => r.HasShrine).OrderBy(r => r.Id).First();

        /// <summary>A walkable cell of the room two or more cells from its altar: in the shrine room, out of reach.</summary>
        private static Vec2 AwayFromAltar(MatchServer s, Room r)
        {
            var a = ShrineRules.AltarCell(r);
            for (int x = r.Bounds.X; x < r.Bounds.XMax; x++)
                for (int y = r.Bounds.Y; y < r.Bounds.YMax; y++)
                {
                    var p = Vec2.Of(new GridPoint(x, y));
                    if (System.Math.Max(System.Math.Abs(x - a.X), System.Math.Abs(y - a.Y)) >= 2 && s.Layout.RoomIdAt[x, y] == r.Id && s.Sim.Walkable(p)) return p;
                }
            throw new System.InvalidOperationException("no cell away from the altar");
        }

        /// <summary>Stand a player beside a shrine's altar and let the server hear of it.</summary>
        private static void AtAltar(MatchServer s, int player, Room r) { s.Sim.Get(player).Position = s.AltarSpot(r.Id); s.Step(0.05f); }

        /// <summary>No pack in these rooms: a bot that sees a mob fights it before anything else, and a mob's hit breaks a prayer.</summary>
        private static void ClearMobs(MatchServer s, params Room[] rooms)
        {
            foreach (var m in s.Sim.Combatants.Values.Where(c => c.IsMob && !c.Dead && s.Layout.InBounds(c.Position.Cell) && rooms.Any(r => s.Layout.RoomIdAt[c.Position.Cell.X, c.Position.Cell.Y] == r.Id))) { m.Health = 0f; m.Dead = true; }
        }

        private static void Send(MatchServer s, PlayerIntent i, int player = 1) { i.PlayerId = player; s.SetIntent(player, i); s.Step(0.05f); }

        private static void Steps(MatchServer s, float seconds) { for (float t = 0f; t < seconds - 0.001f; t += 0.05f) s.Step(0.05f); }

        /// <summary>Make the choice and stand still for the whole prayer (no Space held: the click alone keeps it going).</summary>
        private static void Pray(MatchServer s, int player, ShrineAction action, int arg = 0)
        {
            Send(s, new PlayerIntent { ShrineAction = (byte)action, ShrineArg = arg }, player);
            Steps(s, ShrineRules.ChannelSeconds + 0.15f);
        }

        private static void Kill(MatchServer s, int victim, int killer)
        {
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.PlayerDied, s.Sim.Time, victim, killer, s.RoomOf(victim))), string.Join("; ", s.RecentRejections));
            s.Step(0.05f);
            Assert.True(s.Sim.Get(victim).Dead);
        }

        /// <summary>Hold Space on the spot until <paramref name="done"/> or the time runs out, then let go.</summary>
        private static void HoldSpace(MatchServer s, int player, System.Func<bool> done, int ticks = 400)
        {
            var at = s.Sim.Get(player).Position;
            for (int i = 0; i < ticks && !done(); i++) Send(s, new PlayerIntent { Interact = true, AimX = at.X, AimY = at.Y }, player);
            Send(s, new PlayerIntent(), player);
        }

        /// <summary>Resurrect player 1 by teammate 3 at the first shrine, the way a player does it.</summary>
        private static void ResurrectOne(MatchServer s)
        {
            var shrine = Shrine(s);
            ClearMobs(s, shrine);
            AtAltar(s, 3, shrine);
            s.State.Souls[3] = 2;
            Pray(s, 3, ShrineAction.Resurrect, 1);
            Assert.True(s.State.Log.Any(e => e.Kind == MatchEventKind.PlayerResurrected && e.A == 1), s.ShrineRefusal(3) + " " + string.Join("; ", s.RecentRejections));
        }

        // ---- the wire ----

        [Fact]
        public void ShrineFieldsRoundTripWrittenOnlyWhenSetAndOldPayloadsReadAsNone()
        {
            var i = new PlayerIntent { PlayerId = 4, Tick = 9, MoveX = 1, ConsumableSlot = 2, ShrineAction = (byte)ShrineAction.Resurrect, ShrineArg = 7 };
            var back = PlayerIntent.FromBytes(i.ToBytes());
            Assert.Equal((byte)ShrineAction.Resurrect, back.ShrineAction); Assert.Equal(7, back.ShrineArg);
            Assert.False(back.AnyItemAction); Assert.Equal(2, back.ConsumableSlot); Assert.Equal(1f, back.MoveX);

            // The retired buy byte still reads back as itself (the server refuses it), never as another choice.
            Assert.Equal(3, (byte)ShrineAction.RetiredBuy);
            var both = new PlayerIntent { PlayerId = 4, TakeItemId = 12, SalvageItemId = 100002, ShrineAction = (byte)ShrineAction.RetiredBuy, ShrineArg = 1 };
            var b2 = PlayerIntent.FromBytes(both.ToBytes());
            Assert.Equal(12, b2.TakeItemId); Assert.Equal(100002, b2.SalvageItemId); Assert.Equal((byte)ShrineAction.RetiredBuy, b2.ShrineAction); Assert.Equal(1, b2.ShrineArg);

            // Without a shrine action the bytes are exactly the older formats, and those read as "no shrine action".
            var plain = new PlayerIntent { PlayerId = 4, ConsumableSlot = 1 };
            var withItems = new PlayerIntent { PlayerId = 4, TakeItemId = 3 };
            var withSalvage = new PlayerIntent { PlayerId = 4, SalvageItemId = 100001 };
            Assert.Equal(i.ToBytes().Length - PlayerIntent.ItemBlockLength - 4 - PlayerIntent.ShrineBlockLength, plain.ToBytes().Length);
            Assert.Equal(plain.ToBytes().Length + PlayerIntent.ItemBlockLength, withItems.ToBytes().Length);
            Assert.Equal(plain.ToBytes().Length + PlayerIntent.ItemBlockLength + 4, withSalvage.ToBytes().Length);
            foreach (var old in new[] { plain, withItems, withSalvage })
            {
                var r = PlayerIntent.FromBytes(old.ToBytes());
                Assert.Equal(0, r.ShrineAction); Assert.Equal(0, r.ShrineArg);
            }
            Assert.Equal(100001, PlayerIntent.FromBytes(withSalvage.ToBytes()).SalvageItemId);
        }

        // ---- the altar and the prayer ----

        [Fact]
        public void TheShrineShowsInItsRoomButAChoiceNeedsTheAltarWithinOneCell()
        {
            var s = Open();
            var shrine = Shrine(s);
            ClearMobs(s, shrine);
            s.State.Souls[1] = 2;
            var o = s.ShrineOptionsFor(1);
            Assert.False(o.AtShrine); Assert.False(o.AtAltar);
            Assert.Equal("not at a shrine", o.Arm.Refusal);

            s.Sim.Get(1).Position = AwayFromAltar(s, shrine); s.Step(0.05f);
            o = s.ShrineOptionsFor(1);
            Assert.True(o.AtShrine); Assert.False(o.AtAltar);
            Assert.Equal(ShrineRules.NotAtAltar, o.Refusal);
            Assert.Equal(ShrineRules.NotAtAltar, o.Arm.Refusal);
            Assert.False(s.UseShrine(1, ShrineAction.ArmUltimate, 0));
            Assert.Equal(ShrineRules.NotAtAltar, s.ShrineRefusal(1));
            Assert.Null(s.ChannelOf(1));

            AtAltar(s, 1, shrine);
            Assert.True(ShrineRules.InReach(s.Layout, shrine.Id, s.Sim.Get(1).Position.Cell));
            o = s.ShrineOptionsFor(1);
            Assert.True(o.AtAltar); Assert.Null(o.Refusal); Assert.True(o.Arm.Available);
            // The reach is the shrine's cell and the eight around it, nothing further.
            var a = ShrineRules.AltarCell(shrine);
            Assert.True(ShrineRules.InReach(s.Layout, shrine.Id, a));
            Assert.True(ShrineRules.InReach(s.Layout, shrine.Id, new GridPoint(a.X + 1, a.Y - 1)));
            Assert.False(ShrineRules.InReach(s.Layout, shrine.Id, new GridPoint(a.X + 2, a.Y)));
            Assert.False(ShrineRules.InReach(s.Layout, s.Layout.Rooms.First(r => !r.HasShrine).Id, a));
        }

        [Fact]
        public void ArmingIsATwoSecondPrayerAtTheAltarThenRCastsItOnceAndItIsGone()
        {
            var s = Open();
            var shrine = Shrine(s);
            ClearMobs(s, shrine);
            AtAltar(s, 1, shrine);
            var o = s.ShrineOptionsFor(1);
            Assert.Equal(shrine.Id, o.ShrineRoom);
            Assert.Contains("needs 2 souls", o.Arm.Refusal);
            Send(s, new PlayerIntent { ShrineAction = (byte)ShrineAction.ArmUltimate });
            Assert.Null(s.ChannelOf(1));
            Assert.Contains("needs 2 souls", s.ShrineRefusal(1));

            s.State.Souls[1] = 3;
            Send(s, new PlayerIntent { ShrineAction = (byte)ShrineAction.ArmUltimate });
            Assert.Null(s.ShrineRefusal(1));
            Assert.Equal(MatchServer.ShrineChannel, s.ChannelOf(1));
            Assert.Contains(s.State.Log, e => e.Kind == MatchEventKind.LootStarted && e.A == 1 && e.B == shrine.Id && e.C == MatchState.ShrineChannelKind);
            Assert.False(s.State.UltimateCharges.ContainsKey(1));   // nothing yet: the choice takes when the prayer completes
            Assert.Equal(3, s.State.SoulsOf(1));
            o = s.ShrineOptionsFor(1);
            Assert.Equal(ShrineAction.ArmUltimate, o.Praying.Action);
            Assert.Equal(ShrineRules.Praying, o.Refusal);
            var self = s.SnapshotForTeam(0).Selves.First(m => m.PlayerId == 1);
            Assert.Equal(MatchServer.ShrineChannel, self.ChannelName);   // the HUD's channel bar

            Steps(s, ShrineRules.ChannelSeconds - 0.4f);
            Assert.False(s.State.UltimateCharges.ContainsKey(1));
            Assert.True(s.ShrineOptionsFor(1).PrayingFraction > 0.6f);
            Steps(s, 0.6f);
            Assert.Equal(1, s.State.UltimateCharges[1]);
            Assert.Equal(1, s.State.SoulsOf(1));
            Assert.Null(s.ChannelOf(1));
            Assert.False(s.State.Loots.ContainsKey(1));
            var armedAt = s.State.Log.Single(e => e.Kind == MatchEventKind.UltimateArmed && e.A == 1 && e.B == shrine.Id);
            var startedAt = s.State.Log.Last(e => e.Kind == MatchEventKind.LootStarted && e.A == 1);
            Assert.InRange(armedAt.Time - startedAt.Time, ShrineRules.ChannelSeconds - 0.01f, ShrineRules.ChannelSeconds + 0.1f);
            Assert.Equal("already armed", s.ShrineOptionsFor(1).Arm.Refusal);
            Assert.True(s.SnapshotForTeam(0).Selves.First(m => m.PlayerId == 1).UltimateArmed, "the HUD's ultimate slot reads this");
            // The held intent does not arm again: one press, one use.
            Steps(s, 3f);
            Assert.Equal(1, s.State.Log.Count(e => e.Kind == MatchEventKind.UltimateArmed));

            // R: the ultimate casts through the server and the charge is spent.
            var me = s.Sim.Get(1);
            Send(s, new PlayerIntent { Ultimate = true, AimX = me.Position.X + 1f, AimY = me.Position.Y });
            Assert.Contains(s.State.Log, e => e.Kind == MatchEventKind.UltimateUsed && e.A == 1);
            Assert.Equal(0, s.State.UltimateCharges[1]);
            Assert.Equal(0, s.RejectedCount);
            for (int k = 0; k < 40; k++) Send(s, new PlayerIntent { Ultimate = true, AimX = me.Position.X + 1f, AimY = me.Position.Y });
            Assert.Equal(1, s.State.Log.Count(e => e.Kind == MatchEventKind.UltimateUsed));
            Assert.Contains("needs 2 souls", s.ShrineOptionsFor(1).Arm.Refusal);   // one soul left: re-arming costs two again
        }

        [Fact]
        public void WalkingAHitOrLeavingTheAltarBreaksThePrayerAndNothingIsSpent()
        {
            var s = Open();
            var shrine = Shrine(s);
            ClearMobs(s, shrine);
            AtAltar(s, 1, shrine);
            s.State.Souls[1] = 2;

            // Walking.
            Send(s, new PlayerIntent { ShrineAction = (byte)ShrineAction.ArmUltimate });
            Steps(s, 1f);
            Send(s, new PlayerIntent { MoveX = 1f });
            Assert.Null(s.ChannelOf(1));
            Assert.Contains(s.State.Log, e => e.Kind == MatchEventKind.LootInterrupted && e.A == 1);
            Steps(s, 2f);
            Assert.False(s.State.UltimateCharges.ContainsKey(1));
            Assert.Equal(2, s.State.SoulsOf(1));

            // A hit (like looting: LootInterruptedByDamage).
            AtAltar(s, 1, shrine);
            Send(s, new PlayerIntent { ShrineAction = (byte)ShrineAction.ArmUltimate });
            Assert.Equal(MatchServer.ShrineChannel, s.ChannelOf(1));
            var enemy = s.Sim.Get(2);
            enemy.Position = s.Sim.Get(1).Position + new Vec2(0.6f, 0f);
            var basic = AbilityBook.For(s.SpecOf(2)).First(x => x.Kind == AbilityKind.Basic);
            int interruptsBefore = s.State.Log.Count(e => e.Kind == MatchEventKind.LootInterrupted && e.A == 1);
            for (int k = 0; k < 20 && s.ChannelOf(1) != null; k++) { s.Sim.Cast(2, basic.Id, s.Sim.Get(1).Position); s.Step(0.05f); }
            Assert.Null(s.ChannelOf(1));
            Assert.Equal(interruptsBefore + 1, s.State.Log.Count(e => e.Kind == MatchEventKind.LootInterrupted && e.A == 1));
            enemy.Position = Vec2.Of(s.Layout.Rooms[s.Descriptor.TeamSpawnRooms[1]].Center);
            Steps(s, 2.5f);
            Assert.False(s.State.UltimateCharges.ContainsKey(1));
            Assert.Equal(2, s.State.SoulsOf(1));

            // Knocked (or put) out of reach without walking.
            AtAltar(s, 1, shrine);
            Send(s, new PlayerIntent { ShrineAction = (byte)ShrineAction.ArmUltimate });
            s.Sim.Get(1).Position = AwayFromAltar(s, shrine);
            s.Step(0.05f);
            Assert.Null(s.ChannelOf(1));
            Assert.Equal("moved away from the altar", s.ShrineRefusal(1));
            Assert.Equal(2, s.State.SoulsOf(1));

            // Stand still and it takes; holding Space or pressing the same choice again only continues it.
            AtAltar(s, 1, shrine);
            Send(s, new PlayerIntent { ShrineAction = (byte)ShrineAction.ArmUltimate });
            float started = s.State.Log.Last(e => e.Kind == MatchEventKind.LootStarted && e.A == 1).Time;
            Steps(s, 1f);
            Send(s, new PlayerIntent { Interact = true, ShrineAction = (byte)ShrineAction.ArmUltimate });
            Assert.Equal(started, s.State.Log.Last(e => e.Kind == MatchEventKind.LootStarted && e.A == 1).Time);
            for (int k = 0; k < 30; k++) Send(s, new PlayerIntent { Interact = true });
            Assert.Equal(1, s.State.UltimateCharges[1]);
            Assert.Equal(0, s.State.SoulsOf(1));
            Assert.Equal(0, s.RejectedCount);
        }

        [Fact]
        public void AnArmedUltimateAndEverySoulAreLostOnDeath()
        {
            var s = Open();
            var shrine = Shrine(s);
            ClearMobs(s, shrine);
            AtAltar(s, 1, shrine);
            s.State.Souls[1] = 4;
            Pray(s, 1, ShrineAction.ArmUltimate);
            Assert.Equal(1, s.State.UltimateCharges[1]);
            Kill(s, 1, 2);
            Assert.False(s.State.UltimateCharges.ContainsKey(1));
            Assert.Equal(0, s.State.SoulsOf(1));
            Assert.Equal("not in the round", s.ShrineOptionsFor(1).Refusal);
        }

        // ---- resurrection ----

        [Fact]
        public void AResurrectionCostsTheReviverTwoSoulsAndTheTeammateStandsUpNakedAtTheShrine()
        {
            var s = Open();
            var shrine = Shrine(s);
            ClearMobs(s, shrine);
            Assert.NotEmpty(s.WornBy(1).Items);
            Kill(s, 1, 2);

            AtAltar(s, 3, shrine);
            var o = s.ShrineOptionsFor(3);
            var res = Assert.Single(o.Resurrect);
            Assert.Equal(1, res.Arg);
            Assert.Equal(2, res.Cost);
            Assert.Contains("needs 2 souls", res.Refusal);
            Assert.DoesNotContain(o.Resurrect, r => r.Arg == 2);   // never an enemy

            s.State.Souls[3] = 2;
            Assert.True(s.ShrineOptionsFor(3).Resurrect[0].Available);
            Send(s, new PlayerIntent { ShrineAction = (byte)ShrineAction.Resurrect, ShrineArg = 1 }, player: 3);
            Assert.Null(s.ShrineRefusal(3));
            Assert.Contains(1, s.State.DeadPlayers);   // praying, not yet
            Steps(s, ShrineRules.ChannelSeconds + 0.1f);
            Assert.Contains(s.State.Log, e => e.Kind == MatchEventKind.PlayerResurrected && e.A == 1 && e.B == 3 && e.C == shrine.Id);
            Assert.Equal(0, s.State.SoulsOf(3));
            Assert.DoesNotContain(1, s.State.DeadPlayers);

            var body = s.Sim.Get(1);
            Assert.False(body.Dead); Assert.False(body.Downed);
            Assert.Equal(body.MaxHealth, body.Health, 3);
            Assert.True(ShrineRules.InReach(s.Layout, shrine.Id, body.Position.Cell), "they stand up beside the altar");
            Assert.Equal(shrine.Id, s.RoomOf(1));
            // Naked: nothing worn, nothing carried, empty pockets.
            Assert.Empty(s.WornBy(1).Items);
            Assert.DoesNotContain(s.State.ItemOwner, kv => kv.Value == 1);
            Assert.DoesNotContain(s.PocketsOf(1), kv => kv.Value > 0);

            // They play on: they move and act.
            var before = body.Position;
            for (int k = 0; k < 10; k++) Send(s, new PlayerIntent { MoveX = 1f, AimX = body.Position.X + 3f, AimY = body.Position.Y });
            Assert.NotEqual(before, body.Position);
            Assert.Equal(0, s.RejectedCount);

            // Once per player per round.
            Kill(s, 1, 2);
            s.State.Souls[3] = 2;
            Assert.Equal("already resurrected this round", s.ShrineOptionsFor(3).Resurrect.Single().Refusal);
            Assert.False(s.UseShrine(3, ShrineAction.Resurrect, 1));
        }

        [Fact]
        public void TheCorpseOfAResurrectedPlayerKeepsEverythingWhereItLayAndTheyCanLootItBack()
        {
            var s = Open();
            int deathRoom = s.RoomOf(1);
            var lay = s.Sim.Get(1).Position;
            s.State.PlayerSilver[1] = 25;
            Kill(s, 1, 2);   // an enemy kill: the corpse holds the soul, the purse and what survived the trash
            var left = s.State.Corpses[1].OrderBy(i => i).ToList();
            Assert.NotEmpty(left);
            Assert.Contains(1, s.State.CorpseSouls);

            ResurrectOne(s);
            int remains = MatchState.RemainsOf(1);
            Assert.Equal(1, MatchState.CorpseOwner(remains));
            Assert.False(s.State.Corpses.ContainsKey(1));
            Assert.Equal(left, s.State.Corpses[remains].OrderBy(i => i).ToList());
            Assert.DoesNotContain(left, it => s.State.FloorItems.ContainsKey(it));   // nothing fell on the floor
            Assert.Equal(25, s.State.CorpseSilver[remains]);
            Assert.Contains(remains, s.State.CorpseSouls);
            Assert.DoesNotContain(s.State.ItemOwner, kv => kv.Value == 1);           // still naked
            var bodyLeft = s.Sim.Get(remains);
            Assert.True(bodyLeft.Dead);
            Assert.Equal(lay, bodyLeft.Position);
            // Seen like any corpse: the team's view, its snapshot (the body on the floor), the loot reach.
            Assert.Contains(TeamView.For(s.State, 0, s.ItemById).Corpses, c => c.Player == remains && c.Room == deathRoom);
            Assert.Contains(s.SnapshotForTeam(0).Avatars, av => av.Id == remains && av.Dead);
            Assert.False(s.Sim.Get(1).Dead);

            // They walk back and hold Space over it: open, take everything, the purse. Their own team never takes the kill soul.
            s.Sim.Get(1).Position = lay;
            s.Step(0.05f);
            Assert.Equal("corpse", s.InteractionAvailable(1));
            HoldSpace(s, 1, () => s.State.Corpses[remains].Count == 0 && !s.State.CorpseSilver.ContainsKey(remains));
            Assert.Contains(remains, s.State.OpenedCorpses);
            Assert.All(left, it => Assert.Equal(1, s.State.ItemOwner[it]));
            Assert.Equal(25, s.State.PlayerSilver[1]);
            Assert.Contains(remains, s.State.CorpseSouls);
            Assert.Equal(0, s.RejectedCount);

            // A later death makes a second, separate corpse; the first stays (empty now) where it lay.
            Kill(s, 1, -1);
            Assert.True(s.State.Corpses.ContainsKey(1));
            Assert.True(s.State.Corpses.ContainsKey(remains));
            Assert.Equal(lay, s.Sim.Get(remains).Position);
        }

        [Fact]
        public void AnEnemyCanLootTheRemainsAndTakesTheKillSoul()
        {
            var s = Open();
            var lay = s.Sim.Get(1).Position;
            Kill(s, 1, 2);
            var left = s.State.Corpses[1].ToList();
            ResurrectOne(s);
            int remains = MatchState.RemainsOf(1);
            s.Sim.Get(2).Position = lay;
            s.Step(0.05f);
            HoldSpace(s, 2, () => s.State.Corpses[remains].Count == 0);
            Assert.Contains(remains, s.State.OpenedCorpses);
            Assert.DoesNotContain(remains, s.State.CorpseSouls);
            Assert.Equal(1, s.State.SoulsOf(2));
            Assert.All(left, it => Assert.Equal(2, s.State.ItemOwner[it]));
            Assert.Equal(0, s.RejectedCount);
        }

        /// <summary>Open a chest somewhere for player 1 through the state's own events and take one item no roll holds; returns its id.</summary>
        private static int TakeFromAChest(MatchServer s, int player)
        {
            foreach (var r in s.Layout.Rooms.Where(r => r.Loot.Count > 0 && !r.IsBossRoom && r.Type != RoomType.Vault).OrderBy(r => r.Id))
            {
                if (!s.State.OpenedContainers.Contains(r.Id))
                {
                    Assert.True(s.Raise(new MatchEvent(MatchEventKind.LootStarted, s.Sim.Time, player, r.Id, 0)));
                    Steps(s, FightRules.ChestOpenSeconds + 0.1f);
                    Assert.True(s.Raise(new MatchEvent(MatchEventKind.ContainerOpened, s.Sim.Time, r.Id, player)), string.Join("; ", s.RecentRejections));
                }
                int item = s.State.VisibleItemsIn(r.Id).Where(i => !s.State.OpenRolls.ContainsKey(i)).OrderBy(i => i).FirstOrDefault();
                if (item != 0 && s.Raise(new MatchEvent(MatchEventKind.ItemTaken, s.Sim.Time, item, player))) return item;
            }
            throw new System.InvalidOperationException("no chest item to take");
        }

        private static void Extract(MatchServer s, int player)
        {
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.ExtractsOpened, s.Sim.Time)));
            var gate = s.Layout.ExtractRooms.First();
            ClearMobs(s, gate);
            s.Sim.Get(player).Position = Vec2.Of(gate.Center);
            s.Step(0.05f);
            HoldSpace(s, player, () => s.State.ExtractedPlayers.Contains(player), 400);
            Assert.Contains(player, s.State.ExtractedPlayers);
        }

        [Fact]
        public void SettlementOfAResurrectedPlayerWhoExtractsCountsWhatTheyCarryOutNotTheOldCorpse()
        {
            var s = Open(out var profile);
            var character = profile.Characters[0];
            var lay = s.Sim.Get(1).Position;
            var brought = s.BroughtItemsOf(1).ToList();
            Kill(s, 1, -1);
            var left = s.State.Corpses[1].OrderBy(i => i).ToList();
            Assert.NotEmpty(left);
            ResurrectOne(s);
            int remains = MatchState.RemainsOf(1);

            // Back at the old body: open it, take ONE piece back, leave the rest on it.
            s.Sim.Get(1).Position = lay;
            s.Step(0.05f);
            HoldSpace(s, 1, () => s.State.OpenedCorpses.Contains(remains));
            int back = left[0];
            Send(s, new PlayerIntent { TakeItemId = back });
            Assert.Equal(1, s.State.ItemOwner[back]);
            // And something new.
            int found = TakeFromAChest(s, 1);
            Extract(s, 1);

            var report = RoundSettlement.Settle(profile, character, s, 1);
            Assert.Equal(RoundOutcome.Extracted, report.Outcome);
            Assert.Contains(s.ItemById(back), report.ItemsKept);
            Assert.Contains(s.ItemById(found), report.ItemsGained);
            Assert.Contains(s.ItemById(found), profile.Stash);
            foreach (int id in brought.Where(id => id != back)) Assert.Contains(s.ItemById(id), report.ItemsLost);   // left on the corpse or trashed at the death
            Assert.Equal(brought.Count - 1, report.ItemsLost.Count);
            Assert.Equal(0, s.RejectedCount);
        }

        [Fact]
        public void SettlementOfAResurrectedPlayerWhoDiesAgainLosesEverythingAndBothCorpsesStay()
        {
            var s = Open(out var profile);
            var character = profile.Characters[0];
            var brought = s.BroughtItemsOf(1).ToList();
            Kill(s, 1, -1);
            var left = s.State.Corpses[1].ToList();
            ResurrectOne(s);
            int found = TakeFromAChest(s, 1);
            Kill(s, 1, 2);
            int remains = MatchState.RemainsOf(1);
            Assert.True(s.State.Corpses[remains].SetEquals(left));   // the first body is untouched
            Assert.True(s.State.Corpses[1].Contains(found) || s.State.DestroyedItems.Contains(found));   // the new pickup is on the second body (or trashed)

            var report = RoundSettlement.Settle(profile, character, s, 1);
            Assert.Equal(RoundOutcome.Died, report.Outcome);
            Assert.Empty(report.ItemsKept);
            Assert.Empty(report.ItemsGained);
            Assert.Equal(brought.Count, report.ItemsLost.Count);
            Assert.Contains(s.ItemById(found), report.PickupsLost);
            Assert.DoesNotContain(s.ItemById(found), profile.Stash);
        }

        [Fact]
        public void TheCheapShrineResurrectsForOneSoulAtItsShrineDuringItsMinute()
        {
            var s = Open();
            var shrines = s.Layout.Rooms.Where(r => r.HasShrine).OrderBy(r => r.Id).ToList();
            var cheap = shrines[0];
            ClearMobs(s, shrines.ToArray());
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.EventStarted, s.Sim.Time, (int)DungeonEventKind.CheapShrine, cheap.Id)));
            Kill(s, 1, -1);
            s.State.Souls[3] = 1;
            AtAltar(s, 3, cheap);
            var res = s.ShrineOptionsFor(3).Resurrect.Single();
            Assert.Equal(1, res.Cost);
            Assert.True(res.Available);
            if (shrines.Count > 1)
            {
                AtAltar(s, 3, shrines[1]);
                Assert.Contains("needs 2 souls", s.ShrineOptionsFor(3).Resurrect.Single().Refusal);
                AtAltar(s, 3, cheap);
            }
            Pray(s, 3, ShrineAction.Resurrect, 1);
            Assert.Null(s.ShrineRefusal(3));
            Assert.DoesNotContain(1, s.State.DeadPlayers);
            Assert.Equal(0, s.State.SoulsOf(3));

            // After its minute the price is two again.
            var t = Open();
            ClearMobs(t, cheap);
            Assert.True(t.Raise(new MatchEvent(MatchEventKind.EventStarted, t.Sim.Time, (int)DungeonEventKind.CheapShrine, cheap.Id)));
            Assert.True(t.Raise(new MatchEvent(MatchEventKind.EventEnded, t.Sim.Time)));
            Kill(t, 1, -1);
            t.State.Souls[3] = 1;
            AtAltar(t, 3, cheap);
            Assert.Equal(2, t.ShrineOptionsFor(3).Resurrect.Single().Cost);
            Assert.False(t.UseShrine(3, ShrineAction.Resurrect, 1));
        }

        [Fact]
        public void APrayerForATeammateWhoLeftMeanwhileIsRefusedWhenItCompletesAndCostsNothing()
        {
            var s = Open();
            var shrine = Shrine(s);
            ClearMobs(s, shrine);
            Kill(s, 1, -1);
            AtAltar(s, 3, shrine);
            s.State.Souls[3] = 2;
            Send(s, new PlayerIntent { ShrineAction = (byte)ShrineAction.Resurrect, ShrineArg = 1 }, player: 3);
            Assert.Equal(MatchServer.ShrineChannel, s.ChannelOf(3));
            // Meanwhile the dead player gives up and goes to the hub.
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.PlayerLeft, s.Sim.Time, 1)));
            Steps(s, ShrineRules.ChannelSeconds + 0.1f);
            Assert.DoesNotContain(s.State.Log, e => e.Kind == MatchEventKind.PlayerResurrected);
            Assert.Equal("left for the hub", s.ShrineRefusal(3));
            Assert.False(s.State.Loots.ContainsKey(3));
            Assert.Contains(s.State.Log, e => e.Kind == MatchEventKind.LootInterrupted && e.A == 3);
            Assert.Null(s.ChannelOf(3));
            Assert.Equal(2, s.State.SoulsOf(3));
            Assert.Equal(0, s.RejectedCount);
        }

        [Fact]
        public void APlayerWhoLeftForTheHubCannotBeResurrected()
        {
            var s = Open();
            var shrine = Shrine(s);
            Kill(s, 1, -1);
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.PlayerLeft, s.Sim.Time, 1)));
            AtAltar(s, 3, shrine);
            s.State.Souls[3] = 2;
            Assert.Equal("left for the hub", s.ShrineOptionsFor(3).Resurrect.Single().Refusal);
            Assert.False(s.UseShrine(3, ShrineAction.Resurrect, 1));
            Assert.False(s.UseShrine(3, ShrineAction.Resurrect, 2));   // an enemy is never on the list
            Assert.Equal("not a dead teammate", s.ShrineRefusal(3));
            Assert.Equal(2, s.State.SoulsOf(3));
        }

        // ---- no shop ----

        [Fact]
        public void ThereIsNoShopTheRetiredBuyByteIsRefusedAndOldPurchaseLogsStillReplayAndSettle()
        {
            var s = Open(out var profile);
            var shrine = Shrine(s);
            ClearMobs(s, shrine);
            AtAltar(s, 1, shrine);
            s.State.Souls[1] = 2;
            var o = s.ShrineOptionsFor(1);
            Assert.All(o.All, c => Assert.True(c.Action == ShrineAction.ArmUltimate || c.Action == ShrineAction.Resurrect));
            int antidote = Consumable.All.ToList().FindIndex(c => c.Name == Consumable.AntidoteName);
            Send(s, new PlayerIntent { ShrineAction = (byte)ShrineAction.RetiredBuy, ShrineArg = antidote });
            Assert.Equal(ShrineRules.NoShop, s.ShrineRefusal(1));
            Assert.Null(s.ChannelOf(1));
            Steps(s, 2.5f);
            Assert.Equal(2, s.State.SoulsOf(1));
            Assert.False(s.PocketsOf(1).ContainsKey(Consumable.AntidoteName));
            Assert.DoesNotContain(s.State.Log, e => e.Kind == MatchEventKind.ShrinePurchase);
            Assert.False(s.UseShrine(1, ShrineAction.RetiredBuy, antidote));

            // An old round's log with a purchase in it: the state still applies it, the log round-trips it, and the pockets (and so settlement) count it.
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.ShrinePurchase, s.Sim.Time, 1, antidote, shrine.Id)));
            Assert.Equal(1, s.State.SoulsOf(1));
            Assert.Equal(1, s.PocketsOf(1)[Consumable.AntidoteName]);
            Assert.Contains(MatchState.LogFromBytes(s.State.LogToBytes()), e => e.Kind == MatchEventKind.ShrinePurchase && e.A == 1 && e.B == antidote);
            Extract(s, 1);
            var report = RoundSettlement.Settle(profile, profile.Characters[0], s, 1);
            Assert.Equal(1, report.ConsumableDelta[Consumable.AntidoteName]);
        }

        // ---- the whole shrine refuses ----

        [Fact]
        public void GodlessDownedAndChannellingPlayersGetNothingAndSaySo()
        {
            var dark = Open(new MatchRules { TeamSize = 3, ShrinesUsable = false });
            AtAltar(dark, 1, Shrine(dark));
            dark.State.Souls[1] = 5;
            var o = dark.ShrineOptionsFor(1);
            Assert.Equal("the shrines are dark", o.Refusal);
            Assert.All(o.All, c => Assert.Equal("the shrines are dark", c.Refusal));
            Assert.False(dark.UseShrine(1, ShrineAction.ArmUltimate, 0));
            Assert.Equal(5, dark.State.SoulsOf(1));
            Assert.False(dark.State.Apply(new MatchEvent(MatchEventKind.LootStarted, dark.Sim.Time, 1, Shrine(dark).Id, MatchState.ShrineChannelKind), out string why));
            Assert.Contains("dark", why);

            var s = Open();
            var shrine = Shrine(s);
            ClearMobs(s, shrine);
            AtAltar(s, 1, shrine);
            s.State.Souls[1] = 2;
            Send(s, new PlayerIntent { ShrineAction = (byte)ShrineAction.ArmUltimate });
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.PlayerDowned, s.Sim.Time, 1, 2, shrine.Id)));
            s.Sim.Get(1).Downed = true;
            Assert.Equal("downed", s.ShrineOptionsFor(1).Arm.Refusal);
            Assert.False(s.UseShrine(1, ShrineAction.ArmUltimate, 0));
            Steps(s, 2.5f);
            Assert.Null(s.ChannelOf(1));   // going down ended the prayer
            Assert.False(s.State.UltimateCharges.ContainsKey(1));
            Assert.Equal(2, s.State.SoulsOf(1));
        }

        // ---- bots ----

        [Fact]
        public void ABotWithTwoSoulsAtAShrinePraysAtTheAltarArmsItsUltimateAndItsFightBrainSpendsIt()
        {
            var s = MatchServer.Host(11, DungeonSettings.ForTeams(2), new MatchRules { TeamSize = 3 }, lootSecret: 5);
            s.StartRound();
            var squad = new BotSquad { Team = 0 };
            var bot = new RoundBot(5, squad);
            s.AddBot(5, 0, "Sentinel", 1, BotRules.DefaultConsumables(1), bot);
            for (int i = 0; i < 200 && !s.State.DoorsOpen; i++) s.Step(0.1f);
            var shrine = Shrine(s);
            ClearMobs(s, shrine);
            s.Sim.Get(5).Position = AwayFromAltar(s, shrine);   // in the room, a few steps from the altar: it walks up
            s.Step(0.05f);
            s.State.Souls[5] = 2;
            bool prayed = false;
            for (int i = 0; i < 400 && !s.State.Log.Any(e => e.Kind == MatchEventKind.UltimateArmed); i++) { s.Step(0.05f); prayed |= s.ChannelOf(5) == MatchServer.ShrineChannel; }
            s.Step(0.05f);
            Assert.True(prayed);
            Assert.Contains(s.State.Log, e => e.Kind == MatchEventKind.UltimateArmed && e.A == 5);
            Assert.Contains(s.State.Log, e => e.Kind == MatchEventKind.LootStarted && e.A == 5 && e.C == MatchState.ShrineChannelKind);
            Assert.Equal(1, bot.ShrineArms);
            Assert.True(s.Sim.Get(5).UltimateArmed);

            // The fight brain casts on the simulator; the state hears of it on the bot's next think.
            var body = s.Sim.Get(5);
            var ult = AbilityBook.For(s.SpecOf(5)).First(a => a.Kind == AbilityKind.Ultimate);
            Assert.True(s.Sim.Cast(5, ult.Id, ult.Shape == AbilityShape.Self ? body.Position : body.Position + new Vec2(1f, 0f)).Ok);
            s.Step(0.05f);
            Assert.Contains(s.State.Log, e => e.Kind == MatchEventKind.UltimateUsed && e.A == 5);
            Assert.Equal(0, s.RejectedCount);
        }

        [Fact]
        public void ABotWhosePrayerIsBrokenPraysAgain()
        {
            var s = MatchServer.Host(11, DungeonSettings.ForTeams(2), new MatchRules { TeamSize = 3 }, lootSecret: 5);
            s.StartRound();
            var bot = new RoundBot(5, new BotSquad { Team = 0 });
            s.AddBot(5, 0, "Sentinel", 1, BotRules.DefaultConsumables(1), bot);
            for (int i = 0; i < 200 && !s.State.DoorsOpen; i++) s.Step(0.1f);
            var shrine = Shrine(s);
            ClearMobs(s, shrine);
            s.Sim.Get(5).Position = s.AltarSpot(shrine.Id);
            s.State.Souls[5] = 2;
            for (int i = 0; i < 40 && s.ChannelOf(5) != MatchServer.ShrineChannel; i++) s.Step(0.05f);
            Assert.Equal(MatchServer.ShrineChannel, s.ChannelOf(5));
            s.Sim.Get(5).Position = AwayFromAltar(s, shrine);   // shoved out of reach
            s.Step(0.05f);
            Assert.NotEqual(MatchServer.ShrineChannel, s.ChannelOf(5));
            for (int i = 0; i < 600 && !s.State.Log.Any(e => e.Kind == MatchEventKind.UltimateArmed); i++) s.Step(0.05f);
            Assert.Contains(s.State.Log, e => e.Kind == MatchEventKind.UltimateArmed && e.A == 5);
            Assert.Equal(1, bot.ShrineBroken);
            Assert.Equal(1, bot.ShrineArms);
            Assert.True(s.State.Log.Count(e => e.Kind == MatchEventKind.LootStarted && e.A == 5 && e.C == MatchState.ShrineChannelKind) >= 2);
            Assert.Equal(0, s.RejectedCount);
        }

        [Fact]
        public void ABotWithSoulsWalksToAShrineAndResurrectsItsDeadTeammate()
        {
            var s = MatchServer.Host(11, DungeonSettings.ForTeams(2), new MatchRules { TeamSize = 3 }, lootSecret: 5);
            s.StartRound();
            var squad = new BotSquad { Team = 0 };
            var bot = new RoundBot(5, squad);
            s.AddBot(5, 0, "Sentinel", 1, BotRules.DefaultConsumables(1), bot);
            s.AddBot(6, 0, "Sentinel", 1, BotRules.DefaultConsumables(1), new FightOnlyBot(6));
            for (int i = 0; i < 200 && !s.State.DoorsOpen; i++) s.Step(0.1f);
            Kill(s, 6, -1);
            s.State.Souls[5] = 2;
            var shrine = Shrine(s);
            // Start next to the shrine room (one room away at most), so the walk is short.
            var near = s.Layout.Rooms.First(r => r.Neighbours.Contains(shrine.Id) && !r.IsBossRoom);
            ClearMobs(s, shrine, near);
            s.Sim.Get(5).Position = Vec2.Of(near.Center);
            s.Step(0.05f);
            for (int i = 0; i < 1200 && s.State.DeadPlayers.Contains(6); i++) s.Step(0.05f);
            s.Step(0.05f);
            Assert.DoesNotContain(6, s.State.DeadPlayers);
            Assert.Contains(s.State.Log, e => e.Kind == MatchEventKind.PlayerResurrected && e.A == 6 && e.B == 5);
            Assert.Contains(s.State.Log, e => e.Kind == MatchEventKind.LootStarted && e.A == 5 && e.C == MatchState.ShrineChannelKind);
            Assert.Equal(1, bot.ShrineResurrects);
            Assert.False(s.Sim.Get(6).Dead);
            Assert.Equal(0, s.RejectedCount);
        }
    }
}
