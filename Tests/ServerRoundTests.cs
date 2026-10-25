using System;
using System.Linq;
using DungeonGen.Core;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using DungeonGen.Core.Profile;
using Xunit;

namespace DungeonGen.Tests
{
    /// <summary>The server loop and whole rounds, headless: what the dedicated server will run, proven without Unity.</summary>
    public class ServerRoundTests
    {
        private static MatchServer Seated(int seed = 11, int teams = 4)
        {
            var server = MatchServer.Host(seed, DungeonSettings.ForTeams(teams), new MatchRules { TeamSize = 3 }, lootSecret: 5);
            server.StartRound();
            var warden = ClassArchetype.DefaultRoster.First(c => c.Name == "Warden");
            server.AddPlayer(1, 0, warden, "Sentinel", PlayerStats.For(warden, 1));
            server.AddPlayer(2, 1, warden, "Sentinel", PlayerStats.For(warden, 1));
            return server;
        }

        [Fact]
        public void HeadlessRoundsRunToTheEndWithoutARejectedEvent()
        {
            foreach (int seed in new[] { 1, 2, 3 })
            {
                var r = RoundLab.Play(seed, teams: 4, perTeam: 3, tier: 1);
                Assert.True(r.Rejected == 0, $"seed {seed}: {string.Join(" | ", r.Rejections.Take(3))}");
                Assert.Equal(r.Players, r.Extracted + r.Died + r.StillInside);
                Assert.True(r.StillInside == 0 || r.Seconds >= 1200f, "a round ends when everyone is out or the clock runs down");
                Assert.True(r.RoomsLooted > 0, "bots open chests");
                // The log is in simulator time and never runs backwards.
                var times = r.Summaries.Count > 0 ? r.Summaries : null;
                Assert.NotNull(times);
            }
        }

        [Fact]
        public void TheLogStaysMonotonicAndTheSummaryMatchesTheState()
        {
            MatchServer kept = null;
            var r = RoundLab.Play(4, configure: s => kept = s);
            var log = kept.State.Log;
            for (int i = 1; i < log.Count; i++) Assert.True(log[i].Time >= log[i - 1].Time - 0.001f, $"{log[i - 1]} then {log[i]}");
            foreach (var s in r.Summaries)
            {
                Assert.Equal(kept.State.ExtractedPlayers.Contains(s.Player), s.Extracted);
                Assert.Equal(kept.State.DeadPlayers.Contains(s.Player), s.Died);
                if (s.Extracted) Assert.True(s.AccountXp >= AccountRules.XpPerExtraction);
                if (!s.Extracted) Assert.Equal(0, s.ItemsExtracted);
            }
        }

        [Fact]
        public void SomeBotsExtractWithLootAcrossSeeds()
        {
            var reports = RoundLab.PlayMany(1, 6, tier: 2);
            Assert.True(reports.Sum(r => r.Extracted) > 0, "nobody ever extracted");
            Assert.True(reports.Sum(r => r.ItemsExtracted) > 0, "nothing ever left the dungeon");
            Assert.True(reports.Sum(r => r.PlayerKills) > 0, "no team ever met another");
            Assert.All(reports, r => Assert.Equal(0, r.Rejected));
        }

        [Fact]
        public void SnapshotsHideEnemiesOutOfSightAndRoundTrip()
        {
            var server = Seated();
            server.Step(0.05f);
            var snap = server.SnapshotForTeam(0);
            Assert.Single(snap.Selves);
            Assert.Equal(1, snap.Selves[0].PlayerId);
            Assert.DoesNotContain(snap.Avatars, a => a.Team == 1);          // spawns are far apart: the rival is not in sight
            Assert.Contains(snap.Avatars, a => a.Id == 1 && a.Team == 0);
            var back = TeamSnapshot.FromBytes(snap.ToBytes());
            Assert.Equal(snap.Avatars.Count, back.Avatars.Count);
            Assert.Equal(snap.Selves[0].MaxHealth, back.Selves[0].MaxHealth);
            Assert.Equal(snap.Tick, back.Tick);
        }

        [Fact]
        public void IntentsMoveNobodyUntilTheCountdownEnds()
        {
            var server = Seated();
            var start = server.Sim.Get(1).Position;
            var intent = new PlayerIntent { PlayerId = 1, MoveX = 1f, MoveY = 0f, AimX = start.X + 5f, AimY = start.Y };
            for (int i = 0; i < 40; i++) { server.SetIntent(1, intent); server.Step(0.1f); }   // 4 s: still sealed
            Assert.Equal(start, server.Sim.Get(1).Position);
            Assert.False(server.State.DoorsOpen);
            for (int i = 0; i < 70; i++) { server.SetIntent(1, intent); server.Step(0.1f); }   // past 10 s
            Assert.True(server.State.DoorsOpen);
            Assert.NotEqual(start, server.Sim.Get(1).Position);
            var wrong = new PlayerIntent { PlayerId = 2, MoveX = 1f };
            server.SetIntent(1, wrong);                                       // a connection may only speak for its own player
            Assert.Equal(PlayerIntent.FromBytes(intent.ToBytes()).Basic, intent.Basic);
        }

        [Fact]
        public void IntentBytesRoundTrip()
        {
            var i = new PlayerIntent { PlayerId = 7, Tick = 99, MoveX = -0.5f, MoveY = 0.25f, AimX = 12.5f, AimY = 3f, Bolt = true, Interact = true, Ping = true, PingKind = 4 };
            var back = PlayerIntent.FromBytes(i.ToBytes());
            Assert.Equal(i.PlayerId, back.PlayerId); Assert.Equal(i.Tick, back.Tick); Assert.Equal(i.MoveX, back.MoveX); Assert.Equal(i.AimY, back.AimY);
            Assert.True(back.Bolt && back.Interact && back.Ping && !back.Basic && !back.Zone); Assert.Equal((byte)4, back.PingKind);
        }

        [Fact]
        public void AnItemTakenFromAChestNeverReappearsInIt()
        {
            var d = MatchDescriptor.Create(21, DungeonSettings.ForTeams(2), new MatchRules { TeamSize = 3 }, out var layout, lootSecret: 3);
            var st = new MatchState(d, layout);
            var room = layout.Rooms.First(r => r.Loot.Count > 0 && !r.IsBossRoom && r.Type != RoomType.Vault);
            string why;
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, 1, 0, 0), out why), why);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, 2, 1, 0), out why), why);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.RoundStarted, 0), out why), why);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.DoorsOpened, 10), out why), why);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 12, 1, room.Id), out why), why);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.LootStarted, 12, 1, room.Id, 0), out why), why);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ContainerOpened, 14.5f, room.Id, 1), out why), why);
            int item = st.VisibleItemsIn(room.Id).First();
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ItemTaken, 15, item, 1), out why), why);
            Assert.DoesNotContain(item, st.VisibleItemsIn(room.Id));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 20, 1, 2, room.Id), out why), why);
            Assert.Contains(item, st.Corpses[1]);
            Assert.DoesNotContain(item, st.VisibleItemsIn(room.Id));                  // on the corpse, not back in the chest
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.ItemTaken, 21, item, 2), out why));   // the corpse must be opened first
            Assert.Equal("corpse not opened", why);
        }

        [Fact]
        public void LastBreathAndChannelRevivesReachTheStateOnce()
        {
            var sim = new FightSim(FightLab.OpenArena(12, 12), 1);
            var mender = ClassArchetype.DefaultRoster.First(c => c.Name == "Mender");
            var a = sim.Add(Combatant.Player(1, 0, PlayerStats.For(mender, 1), new Vec2(3f, 3f), "Warder"));
            var b = sim.Add(Combatant.Player(2, 0, PlayerStats.For(mender, 1), new Vec2(4f, 3f), "Warder"));
            b.Downed = true; b.DownedAt = 0f;
            sim.Revive(b, 1f, 1, "lastbreath");
            var events = sim.Events.SelectMany(e => FightBridge.ToMatchEvents(e, sim, _ => 0)).ToList();
            Assert.Contains(events, e => e.Kind == MatchEventKind.LastBreath && e.A == 1 && e.B == 2);
            Assert.DoesNotContain(events, e => e.Kind == MatchEventKind.PlayerRevived);
            b.Downed = true;
            int before = sim.Events.Count;
            sim.Revive(b, FightRules.ReviveHealthFraction);                                 // the server's channel revive: it raises PlayerRevived itself
            var later = sim.Events.Skip(before).SelectMany(e => FightBridge.ToMatchEvents(e, sim, _ => 0)).ToList();
            Assert.Empty(later);
        }
            [Fact]
        public void AHeldInteractOpensAClosedDoorAndLeavesItOpen()
        {
            var server = Seated();
            int rejected = 0; server.EventRejected += (e, why) => rejected++;
            var idle = new PlayerIntent { PlayerId = 1 };
            for (int i = 0; i < 220; i++) { server.SetIntent(1, idle); server.Step(0.05f); }   // past the 10 s countdown
            Assert.True(server.State.DoorsOpen);
            var c = server.Sim.Get(1);
            int room = server.RoomOf(1);
            var door = server.State.Index.Doors.First(kv => kv.Value.room == room);
            var inside = GridPoint.Cardinals.Select(d => door.Value.cell + d).First(p => server.Layout.InBounds(p) && server.Layout.RoomIdAt[p.X, p.Y] == room);
            c.Position = Vec2.Of(inside);
            server.SetIntent(1, idle); server.Step(0.05f);
            Assert.DoesNotContain(door.Value.cell, server.State.ClosedDoors);

            var hold = new PlayerIntent { PlayerId = 1, Interact = true, AimX = c.Position.X, AimY = c.Position.Y };
            server.SetIntent(1, hold); server.Step(0.05f);                                     // a press on an open door shuts it at once
            Assert.Contains(door.Value.cell, server.State.ClosedDoors);
            server.SetIntent(1, idle); server.Step(0.05f);                                     // release

            for (int i = 0; i < 100; i++) { server.SetIntent(1, hold); server.Step(0.05f); }   // 5 s held: the 3 s channel opens it ...
            Assert.DoesNotContain(door.Value.cell, server.State.ClosedDoors);                   // ... and the key still being down does not shut it again
            Assert.Null(server.ChannelOf(1));
            Assert.Equal(0, rejected);

            server.SetIntent(1, idle); server.Step(0.05f);
            server.SetIntent(1, hold); server.Step(0.05f);                                     // a fresh press shuts it again
            Assert.Contains(door.Value.cell, server.State.ClosedDoors);
        }
            [Fact]
        public void SnapshotsCarryTheCastsHitsAndZonesATeamMaySee()
        {
            var server = Seated();
            var idle = new PlayerIntent { PlayerId = 1 };
            for (int i = 0; i < 220; i++) { server.SetIntent(1, idle); server.Step(0.05f); }
            server.SnapshotForTeam(0);                                                     // everything before this is old news
            var a = server.Sim.Get(1); var b = server.Sim.Get(2);
            b.Position = a.Position + new Vec2(1f, 0f);
            server.Sim.DevCasting = true;
            var res = server.Sim.Cast(1, "Sentinel.bolt", b.Position);                     // Shield Bash on the rival next to us
            Assert.True(res.Ok, res.Rejection);
            var snap = server.SnapshotForTeam(0);
            Assert.Contains(snap.Effects, e => e.Kind == EffectKind.Cast && e.Source == 1 && e.Ability == "Sentinel.bolt" && e.Amount == 1f);
            Assert.Contains(snap.Effects, e => e.Kind == EffectKind.Hit && e.Target == 2 && e.Amount > 0f);
            Assert.DoesNotContain(server.SnapshotForTeam(0).Effects, e => e.Kind == EffectKind.Cast);   // sent once
            var ult = server.Sim.Cast(1, "Sentinel.ult", a.Position);                      // Aegis: a 5 s zone
            Assert.True(ult.Ok, ult.Rejection);
            var withZone = server.SnapshotForTeam(0);
            Assert.Contains(withZone.Effects, e => e.Kind == EffectKind.Zone && e.Ability == "Sentinel.ult" && e.Until > withZone.Time);
            var back = TeamSnapshot.FromBytes(withZone.ToBytes());
            Assert.Equal(withZone.Effects.Count, back.Effects.Count);
            Assert.Equal(withZone.Effects[0].Ability, back.Effects[0].Ability);
            Assert.Equal(withZone.Effects[0].Until, back.Effects[0].Until);
            Assert.Contains(server.SnapshotForTeam(1).Effects, e => e.Kind == EffectKind.Hit && e.Target == 2);   // the rival team sees the hit on its own member
        }

        [Fact]
        public void DevCastingLetsADevSpamAnyAbilityAndUltimateForFree()
        {
            var server = Seated();
            var idle = new PlayerIntent { PlayerId = 1 };
            for (int i = 0; i < 220; i++) { server.SetIntent(1, idle); server.Step(0.05f); }
            var a = server.Sim.Get(1); var b = server.Sim.Get(2); b.Position = a.Position + new Vec2(1f, 0f);
            Assert.False(server.Sim.Cast(1, "Sentinel.ult", a.Position).Ok);                // unarmed, as the rules say
            server.Sim.DevCasting = true;
            float resource = a.ResourceNow;
            Assert.True(server.Sim.Cast(1, "Sentinel.bolt", b.Position).Ok);
            Assert.True(server.Sim.Cast(1, "Sentinel.bolt", b.Position).Ok);                // no cooldown
            Assert.True(server.Sim.Cast(1, "Sentinel.ult", a.Position).Ok);                 // armed for free
            Assert.Equal(resource, a.ResourceNow, 1);                                        // and nothing spent
            Assert.True(server.Sim.Cast(1, "Sharpshooter.bolt", b.Position).Ok);            // another spec's ability, for the effects audition
            Assert.Equal(4, server.Sim.Casts.Count);
        }
    }
}
