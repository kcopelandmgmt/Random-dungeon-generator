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
    /// The shrine on the server: <see cref="PlayerIntent.ShrineAction"/> / <see cref="PlayerIntent.ShrineArg"/> on the wire, <see cref="MatchServer.ShrineOptionsFor"/>
    /// (what this player can do here and why not), and the three choices through their validated events: arm the ultimate (2 souls, one use, then castable
    /// with R), resurrect a dead teammate (2 souls, 1 at the cheap-shrine event, once, naked at the shrine, the corpse's leftovers on its floor), buy a
    /// consumable (1 soul). Bots arm and resurrect at shrines.
    /// </summary>
    public class ShrineTests
    {
        private static ClassArchetype Cls(string name) => ClassArchetype.ByName(name);

        /// <summary>Player 1 (a Warden in the free kit with a potion) and a Mender teammate (3) on team 0, an enemy Warden (2) on team 1; doors open.</summary>
        private static MatchServer Open(MatchRules rules = null, int seed = 11)
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
            return s;
        }

        private static Room Shrine(MatchServer s) => s.Layout.Rooms.Where(r => r.HasShrine).OrderBy(r => r.Id).First();

        private static Vec2 Inside(MatchServer s, Room r)
        {
            var c = Vec2.Of(r.Center);
            for (int ring = 1; ring <= 3; ring++)
                for (int dx = -ring; dx <= ring; dx++) for (int dy = -ring; dy <= ring; dy++)
                    { var p = c + new Vec2(dx, dy); if (s.Layout.InBounds(p.Cell) && s.Sim.Walkable(p) && s.Layout.RoomIdAt[p.Cell.X, p.Cell.Y] == r.Id) return p; }
            return c;
        }

        /// <summary>Stand a player in a room and let the server hear of it.</summary>
        private static void StandIn(MatchServer s, int player, Room r) { s.Sim.Get(player).Position = Inside(s, r); s.Step(0.05f); }

        /// <summary>No pack in these rooms: a bot that sees a mob fights it before anything else.</summary>
        private static void ClearMobs(MatchServer s, params Room[] rooms)
        {
            foreach (var m in s.Sim.Combatants.Values.Where(c => c.IsMob && !c.Dead && s.Layout.InBounds(c.Position.Cell) && rooms.Any(r => s.Layout.RoomIdAt[c.Position.Cell.X, c.Position.Cell.Y] == r.Id))) { m.Health = 0f; m.Dead = true; }
        }

        private static void Send(MatchServer s, PlayerIntent i, int player = 1) { i.PlayerId = player; s.SetIntent(player, i); s.Step(0.05f); }

        private static void Kill(MatchServer s, int victim, int killer)
        {
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.PlayerDied, s.Sim.Time, victim, killer, s.RoomOf(victim))), string.Join("; ", s.RecentRejections));
            s.Step(0.05f);
            Assert.True(s.Sim.Get(victim).Dead);
        }

        // ---- the wire ----

        [Fact]
        public void ShrineFieldsRoundTripWrittenOnlyWhenSetAndOldPayloadsReadAsNone()
        {
            var i = new PlayerIntent { PlayerId = 4, Tick = 9, MoveX = 1, ConsumableSlot = 2, ShrineAction = (byte)ShrineAction.Resurrect, ShrineArg = 7 };
            var back = PlayerIntent.FromBytes(i.ToBytes());
            Assert.Equal((byte)ShrineAction.Resurrect, back.ShrineAction); Assert.Equal(7, back.ShrineArg);
            Assert.False(back.AnyItemAction); Assert.Equal(2, back.ConsumableSlot); Assert.Equal(1f, back.MoveX);

            var both = new PlayerIntent { PlayerId = 4, TakeItemId = 12, SalvageItemId = 100002, ShrineAction = (byte)ShrineAction.Buy, ShrineArg = 1 };
            var b2 = PlayerIntent.FromBytes(both.ToBytes());
            Assert.Equal(12, b2.TakeItemId); Assert.Equal(100002, b2.SalvageItemId); Assert.Equal((byte)ShrineAction.Buy, b2.ShrineAction); Assert.Equal(1, b2.ShrineArg);

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

        // ---- arming the ultimate ----

        [Fact]
        public void ArmingNeedsAShrineAndTwoSoulsThenRCastsItOnceAndItIsGone()
        {
            var s = Open();
            var o = s.ShrineOptionsFor(1);
            Assert.False(o.AtShrine);
            Assert.Equal("not at a shrine", o.Arm.Refusal);

            var shrine = Shrine(s);
            StandIn(s, 1, shrine);
            o = s.ShrineOptionsFor(1);
            Assert.Equal(shrine.Id, o.ShrineRoom);
            Assert.Null(o.Refusal);
            Assert.Contains("needs 2 souls", o.Arm.Refusal);
            Send(s, new PlayerIntent { ShrineAction = (byte)ShrineAction.ArmUltimate });
            Assert.False(s.State.UltimateCharges.ContainsKey(1));
            Assert.Contains("needs 2 souls", s.ShrineRefusal(1));

            s.State.Souls[1] = 3;
            Assert.True(s.ShrineOptionsFor(1).Arm.Available);
            Send(s, new PlayerIntent { ShrineAction = (byte)ShrineAction.ArmUltimate });
            Assert.Equal(1, s.State.UltimateCharges[1]);
            Assert.Equal(1, s.State.SoulsOf(1));
            Assert.Null(s.ShrineRefusal(1));
            Assert.Contains(s.State.Log, e => e.Kind == MatchEventKind.UltimateArmed && e.A == 1 && e.B == shrine.Id);
            Assert.Equal("already armed", s.ShrineOptionsFor(1).Arm.Refusal);
            Assert.True(s.SnapshotForTeam(0).Selves.First(m => m.PlayerId == 1).UltimateArmed, "the HUD's ultimate slot reads this");
            // The held intent does not arm again: one press, one use.
            s.Step(0.05f);
            Assert.Equal(1, s.State.Log.Count(e => e.Kind == MatchEventKind.UltimateArmed));

            // R: the ultimate casts through the server and the charge is spent.
            var me = s.Sim.Get(1);
            Send(s, new PlayerIntent { Ultimate = true, AimX = me.Position.X + 1f, AimY = me.Position.Y });
            Assert.Contains(s.State.Log, e => e.Kind == MatchEventKind.UltimateUsed && e.A == 1);
            Assert.Equal(0, s.State.UltimateCharges[1]);
            Assert.Equal(0, s.RejectedCount);
            // Not armed: R does nothing.

            for (int k = 0; k < 40; k++) Send(s, new PlayerIntent { Ultimate = true, AimX = me.Position.X + 1f, AimY = me.Position.Y });
            Assert.Equal(1, s.State.Log.Count(e => e.Kind == MatchEventKind.UltimateUsed));
            Assert.Contains("needs 2 souls", s.ShrineOptionsFor(1).Arm.Refusal);   // one soul left: re-arming costs two again
        }

        [Fact]
        public void AnArmedUltimateAndEverySoulAreLostOnDeath()
        {
            var s = Open();
            StandIn(s, 1, Shrine(s));
            s.State.Souls[1] = 4;
            Assert.True(s.UseShrine(1, ShrineAction.ArmUltimate, 0));
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
            int deathRoom = s.RoomOf(1);
            Assert.NotEmpty(s.WornBy(1).Items);
            Kill(s, 1, 2);   // an enemy kill: the corpse holds the soul and what survived the trash
            var leftovers = s.State.Corpses[1].ToList();
            Assert.Contains(1, s.State.CorpseSouls);

            StandIn(s, 3, shrine);
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
            Assert.Contains(s.State.Log, e => e.Kind == MatchEventKind.PlayerResurrected && e.A == 1 && e.B == 3 && e.C == shrine.Id);
            Assert.Equal(0, s.State.SoulsOf(3));
            Assert.DoesNotContain(1, s.State.DeadPlayers);

            var body = s.Sim.Get(1);
            Assert.False(body.Dead); Assert.False(body.Downed);
            Assert.Equal(body.MaxHealth, body.Health, 3);
            Assert.Equal(shrine.Id, s.Layout.RoomIdAt[body.Position.Cell.X, body.Position.Cell.Y]);
            Assert.Equal(shrine.Id, s.RoomOf(1));
            // Naked: nothing worn, nothing carried, empty pockets.
            Assert.Empty(s.WornBy(1).Items);
            Assert.DoesNotContain(s.State.ItemOwner, kv => kv.Value == 1);
            Assert.DoesNotContain(s.PocketsOf(1), kv => kv.Value > 0);
            // The body stayed behind: what the corpse still held lies on the floor where it fell, its soul is gone.
            Assert.False(s.State.Corpses.ContainsKey(1));
            Assert.DoesNotContain(1, s.State.CorpseSouls);
            foreach (int it in leftovers) Assert.Equal(deathRoom, s.State.FloorItems[it]);

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
        public void TheCheapShrineResurrectsForOneSoulAtItsShrineDuringItsMinute()
        {
            var s = Open();
            var shrines = s.Layout.Rooms.Where(r => r.HasShrine).OrderBy(r => r.Id).ToList();
            var cheap = shrines[0];
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.EventStarted, s.Sim.Time, (int)DungeonEventKind.CheapShrine, cheap.Id)));
            Kill(s, 1, -1);
            s.State.Souls[3] = 1;
            StandIn(s, 3, cheap);
            var res = s.ShrineOptionsFor(3).Resurrect.Single();
            Assert.Equal(1, res.Cost);
            Assert.True(res.Available);
            if (shrines.Count > 1)
            {
                StandIn(s, 3, shrines[1]);
                Assert.Contains("needs 2 souls", s.ShrineOptionsFor(3).Resurrect.Single().Refusal);
                StandIn(s, 3, cheap);
            }
            Assert.True(s.UseShrine(3, ShrineAction.Resurrect, 1), s.ShrineRefusal(3));
            Assert.Equal(0, s.State.SoulsOf(3));

            // After its minute the price is two again.
            var t = Open();
            Assert.True(t.Raise(new MatchEvent(MatchEventKind.EventStarted, t.Sim.Time, (int)DungeonEventKind.CheapShrine, cheap.Id)));
            Assert.True(t.Raise(new MatchEvent(MatchEventKind.EventEnded, t.Sim.Time)));
            Kill(t, 1, -1);
            t.State.Souls[3] = 1;
            StandIn(t, 3, cheap);
            Assert.Equal(2, t.ShrineOptionsFor(3).Resurrect.Single().Cost);
            Assert.False(t.UseShrine(3, ShrineAction.Resurrect, 1));
        }

        [Fact]
        public void APlayerWhoLeftForTheHubCannotBeResurrected()
        {
            var s = Open();
            Kill(s, 1, -1);
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.PlayerLeft, s.Sim.Time, 1)));
            StandIn(s, 3, Shrine(s));
            s.State.Souls[3] = 2;
            Assert.Equal("left for the hub", s.ShrineOptionsFor(3).Resurrect.Single().Refusal);
            Assert.False(s.UseShrine(3, ShrineAction.Resurrect, 1));
            Assert.False(s.UseShrine(3, ShrineAction.Resurrect, 2));   // an enemy is never on the list
            Assert.Equal("not a dead teammate", s.ShrineRefusal(3));
            Assert.Equal(2, s.State.SoulsOf(3));
        }

        // ---- purchases ----

        [Fact]
        public void EveryConsumableSellsForOneSoulIntoThePockets()
        {
            var s = Open();
            StandIn(s, 1, Shrine(s));
            var o = s.ShrineOptionsFor(1);
            Assert.Equal(Consumable.All.Select(c => c.Name), o.Buy.Select(b => b.Label));
            Assert.All(o.Buy, b => Assert.Equal(1, b.Cost));
            Assert.All(o.Buy, b => Assert.Contains("needs 1 soul", b.Refusal));
            s.State.Souls[1] = 2;
            int antidote = Consumable.All.ToList().FindIndex(c => c.Name == Consumable.AntidoteName);
            int potion = Consumable.All.ToList().FindIndex(c => c.Name == Consumable.HealthPotionName);
            Send(s, new PlayerIntent { ShrineAction = (byte)ShrineAction.Buy, ShrineArg = antidote });
            Send(s, new PlayerIntent { ShrineAction = (byte)ShrineAction.Buy, ShrineArg = potion });
            Assert.Equal(0, s.State.SoulsOf(1));
            Assert.Equal(1, s.PocketsOf(1)[Consumable.AntidoteName]);
            Assert.Equal(2, s.PocketsOf(1)[Consumable.HealthPotionName]);
            Assert.Equal(2, s.State.Log.Count(e => e.Kind == MatchEventKind.ShrinePurchase && e.A == 1 && e.C == Shrine(s).Id));
            Assert.False(s.UseShrine(1, ShrineAction.Buy, Consumable.All.Count));   // no such thing
            Assert.Equal(0, s.RejectedCount);
        }

        // ---- the whole shrine refuses ----

        [Fact]
        public void GodlessDownedAndChannellingPlayersGetNothingAndSaySo()
        {
            var dark = Open(new MatchRules { TeamSize = 3, ShrinesUsable = false });
            StandIn(dark, 1, Shrine(dark));
            dark.State.Souls[1] = 5;
            var o = dark.ShrineOptionsFor(1);
            Assert.Equal("the shrines are dark", o.Refusal);
            Assert.All(o.All, c => Assert.Equal("the shrines are dark", c.Refusal));
            Assert.False(dark.UseShrine(1, ShrineAction.ArmUltimate, 0));
            Assert.Equal(5, dark.State.SoulsOf(1));

            var s = Open();
            StandIn(s, 1, Shrine(s));
            s.State.Souls[1] = 2;
            Assert.True(s.Raise(new MatchEvent(MatchEventKind.PlayerDowned, s.Sim.Time, 1, 2, Shrine(s).Id)));
            Assert.Equal("downed", s.ShrineOptionsFor(1).Arm.Refusal);
            Assert.False(s.UseShrine(1, ShrineAction.ArmUltimate, 0));
            Assert.Equal(2, s.State.SoulsOf(1));
        }

        // ---- bots ----

        [Fact]
        public void ABotWithTwoSoulsAtAShrineArmsItsUltimateAndItsFightBrainSpendsIt()
        {
            var s = MatchServer.Host(11, DungeonSettings.ForTeams(2), new MatchRules { TeamSize = 3 }, lootSecret: 5);
            s.StartRound();
            var squad = new BotSquad { Team = 0 };
            var bot = new RoundBot(5, squad);
            s.AddBot(5, 0, "Sentinel", 1, BotRules.DefaultConsumables(1), bot);
            for (int i = 0; i < 200 && !s.State.DoorsOpen; i++) s.Step(0.1f);
            ClearMobs(s, Shrine(s));
            StandIn(s, 5, Shrine(s));
            s.State.Souls[5] = 2;
            for (int i = 0; i < 200 && !s.State.Log.Any(e => e.Kind == MatchEventKind.UltimateArmed); i++) s.Step(0.05f);   // a chest channel already started finishes first
            s.Step(0.05f);
            Assert.Contains(s.State.Log, e => e.Kind == MatchEventKind.UltimateArmed && e.A == 5);
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
            StandIn(s, 5, near);
            for (int i = 0; i < 1200 && s.State.DeadPlayers.Contains(6); i++) s.Step(0.05f);
            s.Step(0.05f);
            Assert.DoesNotContain(6, s.State.DeadPlayers);
            Assert.Contains(s.State.Log, e => e.Kind == MatchEventKind.PlayerResurrected && e.A == 6 && e.B == 5);
            Assert.Equal(1, bot.ShrineResurrects);
            Assert.False(s.Sim.Get(6).Dead);
            Assert.Equal(0, s.RejectedCount);
        }
    }
}
