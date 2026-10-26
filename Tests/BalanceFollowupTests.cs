using System.Linq;
using DungeonGen.Core;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Items;
using DungeonGen.Core.Net;
using Xunit;

namespace DungeonGen.Tests
{
    /// <summary>Balance follow-ups (2026-10-26): bots with potions, revive starts only on the state's downed, room cleared once at the bridge, per-caster dev casting, the lab brain's targets.</summary>
    public class BalanceFollowupTests
    {
        [Theory]
        [InlineData(1, 2)]
        [InlineData(2, 3)]
        [InlineData(3, 4)]
        [InlineData(5, 4)]
        public void BotsEnterWithPotionsPerTier(int tier, int potions)
        {
            Assert.Equal(potions, BotRules.DefaultConsumables(tier)[Consumable.HealthPotionName]);
            var server = MatchServer.Host(5, DungeonSettings.ForTeams(4), new MatchRules { TeamSize = 3 }, lootSecret: 1);
            server.StartRound();
            var bot = server.AddBot(BotRules.BotIdBase, 0, "Sentinel", tier, BotRules.DefaultConsumables(tier));
            Assert.NotNull(bot);
            Assert.Equal(potions, server.PocketsOf(BotRules.BotIdBase)[Consumable.HealthPotionName]);
            var bare = server.AddBot(BotRules.BotIdBase + 1, 0, "Sentinel", tier);
            Assert.NotNull(bare);
            Assert.Empty(server.PocketsOf(BotRules.BotIdBase + 1));
        }

        [Fact]
        public void ARevivalIsNotStartedOnSomeoneTheStateDoesNotListAsDowned()
        {
            var server = MatchServer.Host(5, DungeonSettings.ForTeams(4), new MatchRules { TeamSize = 3 }, lootSecret: 1);
            server.StartRound();
            var a = server.AddBot(BotRules.BotIdBase, 0, "Sentinel", 1);
            var b = server.AddBot(BotRules.BotIdBase + 1, 0, "Warder", 1);
            for (int i = 0; i < 60; i++) server.Step(0.1f);
            b.Position = a.Position + new Vec2(0.5f, 0f);
            // Down the teammate in the simulator only: the state has not heard of it yet.
            b.Downed = true;
            Assert.DoesNotContain(b.Id, server.State.Downed.Keys);
            Assert.NotEqual("revive", server.InteractionAvailable(a.Id));
        }

        [Fact]
        public void TheBridgeReportsAClearedPackOnceHoweverItIsDrained()
        {
            var layout = DungeonGenerator.Generate(DungeonSettings.ForTeams(4), 7);
            var room = layout.Rooms.First(r => r.Mobs != null && r.Mobs.Count >= 2);
            var sim = new FightSim(layout, 7);
            var director = new EncounterDirector(sim, layout);
            director.StationPack(room);
            foreach (var m in director.Mobs) { m.Mob.Dead = true; sim.Emit(FightEventKind.Died, -1, m.Mob.Id, 0f, "test"); }
            var packOf = new System.Collections.Generic.Dictionary<int, int>();
            int pack = 1; foreach (var m in director.Mobs) packOf[m.Mob.Id] = pack;
            int cleared = 0;
            foreach (var e in sim.Events) foreach (var me in FightBridge.ToMatchEvents(e, sim, _ => room.Id, null, id => packOf.TryGetValue(id, out int p) ? p : (int?)null)) if (me.Kind == MatchEventKind.MobPackCleared) cleared++;
            Assert.Equal(1, cleared);
        }

        [Fact]
        public void DevCastersWaiveCooldownsOnlyForThemselves()
        {
            var sim = new FightSim(FightLab.OpenArena(20, 12), 1);
            var dev = sim.Add(FightLab.Player(1, 0, "Sentinel", 1, new Vec2(5f, 6f)));
            var bot = sim.Add(FightLab.Player(2, 0, "Sentinel", 1, new Vec2(5f, 7f)));
            sim.Add(FightLab.Player(3, 1, "Sentinel", 1, new Vec2(6.5f, 6f)));
            sim.DevCasting = true; sim.DevCasters.Add(1);
            { var rr = sim.Cast(1, "Sentinel.bolt", new Vec2(6.5f, 6f)); Assert.True(rr.Ok, rr.Rejection); }
            Assert.True(sim.Cast(1, "Sentinel.bolt", new Vec2(6.5f, 6f)).Ok);        // no cooldown for the dev
            Assert.True(sim.Cast(2, "Sentinel.bolt", new Vec2(6.5f, 6f)).Ok);
            Assert.False(sim.Cast(2, "Sentinel.bolt", new Vec2(6.5f, 6f)).Ok);       // the bot keeps its cooldown
            Assert.False(sim.Cast(2, "Sentinel.ult", new Vec2(5f, 6.5f)).Ok);         // and its ultimate stays unarmed
            Assert.True(sim.Cast(1, "Sentinel.ult", new Vec2(5f, 6f)).Ok);
            sim.DevCasters.Clear();                                                  // empty set: global, as before
            Assert.True(sim.Cast(2, "Sentinel.bolt", new Vec2(6.5f, 6f)).Ok);
        }

        [Fact]
        public void TheLabBrainDoesNotTargetThroughWalls()
        {
            var layout = DungeonGenerator.Generate(DungeonSettings.ForTeams(4), 3);
            var sim = new FightSim(layout, 3);
            var rooms = layout.Rooms.Where(r => r.Type == RoomType.Spawn).Take(2).ToList();
            var a = sim.Add(FightLab.Player(1, 0, "Sharpshooter", 1, Vec2.Of(rooms[0].Center)));
            var b = sim.Add(FightLab.Player(2, 1, "Sharpshooter", 1, Vec2.Of(rooms[1].Center)));
            Assert.False(sim.CanSee(a.Position, b.Position));
            for (int i = 0; i < 20; i++) SimpleBrain.Act(sim, a, 0.1f);
            Assert.DoesNotContain(sim.Casts, c => c.Caster == a.Id);                  // walks toward them, never fires across the dungeon
        }
    }
}
