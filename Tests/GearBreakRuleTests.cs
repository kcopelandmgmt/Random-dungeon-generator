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
    /// When gear breaks (owner, 2026-10-26): a downed player a teammate revives keeps every piece, untouched. Only a real death rolls the
    /// break (<see cref="DurabilityRules.DeathTrashChance"/> per piece): being finished by an enemy (a gank) or bleeding out with nobody
    /// coming. A shrine resurrection afterwards does not undo it.
    /// </summary>
    public class GearBreakRuleTests
    {
        private static ClassArchetype Cls(string name) => ClassArchetype.ByName(name);

        private static (MatchServer s, List<int> gear) Seat(int seed)
        {
            var p = PlayerProfile.NewAccount("a", "Bram", Cls("Warden"));
            var carried = new List<Item>();
            foreach (var name in new[] { "Plate Helm", "Plate Greaves", "Gauntlets", "Sabatons", "Longsword", "Amulet", "Ring", "Ring" })
            {
                var it = new Item(ItemBase.ByName(name), 0, Rarity.Normal, name, new List<AffixRoll>(), new List<AffixRoll>());
                p.Stash.Add(it); carried.Add(it);
            }
            var lo = Loadout.For(p, p.Characters[0], carried, null);
            var s = MatchServer.Host(seed, DungeonSettings.ForTeams(2), new MatchRules { TeamSize = 3 }, lootSecret: 5);
            s.StartRound();
            Assert.NotNull(s.AddPlayerWithLoadout(1, 0, lo));
            s.AddPlayer(2, 1, Cls("Warden"), "Sentinel", PlayerStats.For(Cls("Warden"), 1));
            s.AddPlayer(3, 0, Cls("Mender"), WeaponSpec.ForClass("Mender").First().Name, PlayerStats.For(Cls("Mender"), 1));
            for (int i = 0; i < 200 && !s.State.DoorsOpen; i++) s.Step(0.1f);
            Assert.True(s.State.DoorsOpen);
            return (s, s.BroughtItemsOf(1).ToList());
        }

        private static void Down(MatchServer s)
        {
            var me = s.Sim.Get(1);
            s.Sim.ApplyDamage(me, me.MaxHealth * 20f, DamageProfile.Pure(DamageType.Physical), s.Sim.Get(2), "test blow");
            s.Step(0.05f);
            Assert.True(me.Downed);
            Assert.True(s.State.Downed.ContainsKey(1));
        }

        private static int Broken(MatchServer s, List<int> gear) => s.State.Log.Count(e => e.Kind == MatchEventKind.ItemDestroyed && gear.Contains(e.A));

        [Fact]
        public void ARevivedPlayerKeepsEveryPiece()
        {
            var (s, gear) = Seat(11);
            Assert.True(gear.Count >= 8);
            Down(s);
            // A teammate kneels beside them and holds Space through the revive channel.
            var me = s.Sim.Get(1);
            var mate = s.Sim.Get(3);
            mate.Position = new Vec2(me.Position.X + 0.6f, me.Position.Y);
            var hold = new PlayerIntent { PlayerId = 3, Interact = true, AimX = me.Position.X, AimY = me.Position.Y };
            for (float t = 0; t < FightRules.ReviveChannelSeconds + 1.5f && me.Downed; t += 0.05f) { s.SetIntent(3, hold); s.Step(0.05f); }
            Assert.False(me.Downed, "the teammate's revive did not land");
            Assert.Contains(s.State.Log, e => e.Kind == MatchEventKind.PlayerRevived && e.A == 1);
            Assert.Equal(0, Broken(s, gear));
            Assert.All(gear, id => Assert.Equal(1, s.State.ItemOwner[id]));
            Assert.Equal(0, s.RejectedCount);
        }

        [Fact]
        public void BeingFinishedRollsTheBreak()
        {
            var (s, gear) = Seat(11);
            Down(s);
            var me = s.Sim.Get(1);
            s.Sim.ApplyDamage(me, me.MaxHealth * 20f, DamageProfile.Pure(DamageType.Physical), s.Sim.Get(2), "test finish");
            s.Step(0.05f);
            Assert.Contains(s.State.Log, e => e.Kind == MatchEventKind.PlayerFinished && e.A == 1);
            int broken = Broken(s, gear);
            Assert.InRange(broken, 1, gear.Count - 1);   // about half, by the seeded roll; the rest lie on the corpse
            Assert.Equal(0, s.RejectedCount);
        }

        [Fact]
        public void BleedingOutRollsTheBreakToo()
        {
            var (s, gear) = Seat(11);
            Down(s);
            for (float t = 0; t < FightRules.DownedSeconds + 2f; t += 0.1f) s.Step(0.1f);
            Assert.Contains(s.State.Log, e => e.Kind == MatchEventKind.PlayerDied && e.A == 1);
            Assert.InRange(Broken(s, gear), 1, gear.Count - 1);
            Assert.Equal(0, s.RejectedCount);
        }
    }
}
