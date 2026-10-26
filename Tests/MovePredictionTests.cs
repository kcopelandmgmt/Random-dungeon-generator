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
    /// Client-side prediction (2026-10-26 online play beyond the LAN): <see cref="MovePredictor"/> walks the local player with the server's own movement code,
    /// so for the same intents it lands where the server does, walls and shut doors included; reconciling against a lagged server is silent while the
    /// speeds agree, snaps a big error, smooths a small one, and holds when the server keeps the player still.
    /// </summary>
    public class MovePredictionTests
    {
        private static (MatchServer server, Loadout loadout) Round(int seed = 11)
        {
            var warden = ClassArchetype.DefaultRoster.First(c => c.Name == "Warden");
            var p = PlayerProfile.NewAccount("a", "Bram", warden);
            var lo = Loadout.For(p, p.Characters[0]);
            var server = MatchServer.Host(seed, DungeonSettings.ForTeams(2), new MatchRules { TeamSize = 3 }, lootSecret: 5);
            server.StartRound();
            Assert.NotNull(server.AddPlayerWithLoadout(1, 0, lo));
            for (int i = 0; i < 400 && !server.State.DoorsOpen; i++) server.Step(0.1f);
            Assert.True(server.State.DoorsOpen);
            return (server, lo);
        }

        private static MoveGate GateOf(MatchServer server, int id = 1) => new MoveGate
        {
            DoorsOpen = server.State.DoorsOpen, DownOrDead = false, Channel = server.ChannelOf(id),
            LoadKg = server.LoadKgOf(id), CapacityKg = server.Sim.Get(id).Stats.CarryKg, ClosedDoors = server.State.ClosedDoors,
        };

        [Fact]
        public void ThePredictionWalksWhereTheServerWalksIntoAndAlongTheWalls()
        {
            var (server, lo) = Round();
            var me = server.Sim.Get(1);
            var predictor = new MovePredictor(server.Layout, lo.Stats, 1);
            predictor.Reset(me.Position, 0f);
            const float dt = 0.05f;
            float t = 0f, maxGap = 0f;
            var start = me.Position;
            // Walk diagonally (into a wall and then along it), then straight the other way.
            for (int i = 0; i < 240; i++)
            {
                var move = i < 160 ? new Vec2(0.8f, 0.6f) : new Vec2(-1f, 0f);
                server.SetIntent(1, new PlayerIntent { PlayerId = 1, Tick = (uint)i, MoveX = move.X, MoveY = move.Y, AimX = me.Position.X + 2f, AimY = me.Position.Y });
                var gate = GateOf(server);
                server.Step(dt);
                t += dt;
                predictor.Step(t, dt, move, gate);
                maxGap = Math.Max(maxGap, Vec2.Distance(predictor.Position, me.Position));
            }
            Assert.True(Vec2.Distance(start, me.Position) > 3f, "the server never walked the player");
            Assert.True(maxGap < 1e-3f, $"the prediction drifted {maxGap:0.0000} cells from the server");
        }

        [Fact]
        public void AnOverloadedPlayerIsPredictedAtTheCarrySpeed()
        {
            var (server, lo) = Round();
            var me = server.Sim.Get(1);
            var light = new MovePredictor(server.Layout, lo.Stats, 1); light.Reset(me.Position, 0f);
            var heavy = new MovePredictor(server.Layout, lo.Stats, 1); heavy.Reset(me.Position, 0f);
            var gate = GateOf(server);
            var loaded = gate; loaded.LoadKg = gate.CapacityKg * 2f;   // double the capacity: half speed
            for (int i = 1; i <= 4; i++) { light.Step(i * 0.05f, 0.05f, new Vec2(1f, 0f), gate); heavy.Step(i * 0.05f, 0.05f, new Vec2(1f, 0f), loaded); }
            float l = Vec2.Distance(light.Position, me.Position), h = Vec2.Distance(heavy.Position, me.Position);
            Assert.True(l > 0.3f);
            Assert.Equal(CarryRules.MoveSpeedFactor(loaded.LoadKg, loaded.CapacityKg) * l, h, 3);
        }

        [Fact]
        public void AShutDoorStopsThePredictionAndAnOpenOneDoesNot()
        {
            var (server, lo) = Round();
            var layout = server.Layout;
            foreach (var (room, door) in server.State.Index.Doors.Values)
            {
                // A floor cell beside the door, and the direction through it.
                foreach (var d in new[] { new GridPoint(1, 0), new GridPoint(-1, 0), new GridPoint(0, 1), new GridPoint(0, -1) })
                {
                    var from = new GridPoint(door.X - d.X, door.Y - d.Y);
                    var beyond = new GridPoint(door.X + d.X, door.Y + d.Y);
                    if (!layout.IsWalkable(from) || !layout.IsWalkable(beyond) || layout.GetTile(from) == TileType.Door || layout.GetTile(beyond) == TileType.Door) continue;
                    var dir = new Vec2(d.X, d.Y);
                    var shut = new MovePredictor(layout, lo.Stats, 1); shut.Reset(Vec2.Of(from), 0f);
                    var open = new MovePredictor(layout, lo.Stats, 1); open.Reset(Vec2.Of(from), 0f);
                    var gate = GateOf(server);
                    var closed = gate; closed.ClosedDoors = new HashSet<GridPoint> { door };
                    var none = gate; none.ClosedDoors = new HashSet<GridPoint>();
                    for (int i = 1; i <= 30; i++) { shut.Step(i * 0.05f, 0.05f, dir, closed); open.Step(i * 0.05f, 0.05f, dir, none); }
                    Assert.NotEqual(door, shut.Position.Cell);
                    Assert.True(Vec2.Distance(shut.Position, Vec2.Of(from)) < 1f, "the prediction walked through a shut door");
                    Assert.True(Vec2.Distance(open.Position, Vec2.Of(from)) > 2f, "the prediction did not walk through an open door");
                    return;
                }
            }
            Assert.Fail("no door with floor on both sides");
        }

        [Fact]
        public void ThePredictionGateFollowsTheServersRules()
        {
            Assert.False(new MoveGate { DoorsOpen = false }.Walks);
            Assert.False(new MoveGate { DoorsOpen = true, DownOrDead = true }.Walks);
            Assert.False(new MoveGate { DoorsOpen = true, Channel = "loot" }.Walks);
            Assert.True(new MoveGate { DoorsOpen = true, Channel = MatchServer.ShrineChannel }.Walks);   // walking away breaks a prayer
            Assert.True(new MoveGate { DoorsOpen = true, Channel = "" }.Walks);
        }

        /// <summary>
        /// A server that applies the client's inputs <paramref name="lag"/> seconds late (the round trip): it is the same walk, delayed. Under that lag the
        /// prediction runs ahead of every snapshot while the input is held, needs no correction, and meets the server once the input stops.
        /// </summary>
        [Fact]
        public void UnderLagThePredictionLeadsTheSnapshotsAndConvergesWhenTheInputStops()
        {
            var (server, lo) = Round();
            var me = server.Sim.Get(1);
            var start = me.Position;
            var client = new MovePredictor(server.Layout, lo.Stats, 1); client.Reset(start, 0f);
            var authority = new MovePredictor(server.Layout, lo.Stats, 1); authority.Reset(start, 0f);
            var gate = GateOf(server);
            const float frame = 1f / 60f, lag = 0.24f, snapshotEvery = 0.05f;
            var inputs = new List<(float t, Vec2 move)>();
            var snapshots = new Queue<(float arrive, Vec2 pos)>();
            float t = 0f, nextSnap = 0f, maxLead = 0f, serverT = 0f;
            for (int f = 0; f < 150; f++)
            {
                t += frame;
                var move = t < 1.2f ? new Vec2(0f, 1f) : new Vec2(0f, 0f);   // hold "up" for 1.2 s, then let go
                inputs.Add((t, move));
                client.Step(t, frame, move, gate);
                // The server lives lag seconds behind: it walks with the input the client had then.
                while (serverT + frame <= t - lag + 1e-6f)
                {
                    serverT += frame;
                    var then = inputs.LastOrDefault(i => i.t <= serverT + 1e-6f).move;
                    authority.Step(serverT, frame, then, gate);
                }
                if (t >= nextSnap) { nextSnap += snapshotEvery; snapshots.Enqueue((t, authority.Position)); }
                while (snapshots.Count > 0 && snapshots.Peek().arrive <= t)
                {
                    var s = snapshots.Dequeue();
                    client.Reconcile(t, s.pos, lag);
                    if (t < 1.2f) maxLead = Math.Max(maxLead, Vec2.Distance(client.Shown(t), s.pos));
                }
            }
            Assert.True(maxLead > 0.4f, $"the prediction did not lead the snapshots (lead {maxLead:0.00} cells)");
            Assert.True(client.Snaps == 0, "a lagged but agreeing server must never snap");
            Assert.True(Vec2.Distance(client.Shown(t), authority.Position) < 0.15f, $"no convergence: shown {client.Shown(t)}, server {authority.Position}");
        }

        [Fact]
        public void ABigErrorSnapsAndASmallOneIsSmoothedOverATenthOfASecond()
        {
            var (server, lo) = Round();
            var at = server.Sim.Get(1).Position;
            var p = new MovePredictor(server.Layout, lo.Stats, 1);
            Assert.Equal(PredictionCorrection.Started, p.Reconcile(0f, at, 0.1f));

            // Small: half a cell off. The logical position jumps, the drawn one starts where it was and arrives within SmoothSeconds.
            var near = FloorNear(server.Layout, at, 0.5f);
            Assert.Equal(PredictionCorrection.Smooth, p.Reconcile(1f, near, 0.1f));
            Assert.True(Vec2.Distance(p.Position, near) < 1e-4f);
            Assert.True(Vec2.Distance(p.Shown(1f), at) < 1e-4f, "the correction was not smoothed");
            Assert.True(Vec2.Distance(p.Shown(1f + MovePredictor.SmoothSeconds * 0.5f), near) > 0.1f);
            Assert.True(Vec2.Distance(p.Shown(1f + MovePredictor.SmoothSeconds), near) < 1e-4f, "the smoothing took longer than SmoothSeconds");

            // Big: three cells (a dash or a resurrection). Drawn there at once.
            var far = FloorNear(server.Layout, near, 3f);
            Assert.Equal(PredictionCorrection.Snap, p.Reconcile(2f, far, 0.1f));
            Assert.True(Vec2.Distance(p.Shown(2f), far) < 1e-4f);
            Assert.Equal(1, p.Snaps);
        }

        [Fact]
        public void WhenTheServerHoldsThePlayerStillThePredictionStopsRunningAhead()
        {
            var (server, lo) = Round();
            var at = server.Sim.Get(1).Position;
            var p = new MovePredictor(server.Layout, lo.Stats, 1); p.Reset(at, 0f);
            var gate = GateOf(server);
            const float frame = 1f / 60f, lag = 0.1f;
            float t = 0f, worst = 0f;
            for (int f = 0; f < 120; f++)   // two seconds of "right" against a stunned body that never moves
            {
                t += frame;
                p.Step(t, frame, new Vec2(1f, 0f), gate);
                if (f % 3 == 0) p.Reconcile(t, at, lag);
                if (t > 1f) worst = Math.Max(worst, Vec2.Distance(p.Shown(t), at));
            }
            Assert.True(p.Holds > 0, "the prediction never held");
            Assert.True(worst < 0.5f, $"the prediction kept running ahead of a held server ({worst:0.00} cells)");
        }

        private static Vec2 FloorNear(DungeonLayout layout, Vec2 from, float distance)
        {
            foreach (var d in new[] { new Vec2(1f, 0f), new Vec2(-1f, 0f), new Vec2(0f, 1f), new Vec2(0f, -1f) })
            {
                var p = from + d * distance;
                bool clear = true;
                for (float s = 0f; s <= distance; s += 0.25f) if (!layout.IsWalkable((from + d * s).Cell) || layout.GetTile((from + d * s).Cell) == TileType.Door) clear = false;
                if (clear) return p;
            }
            throw new InvalidOperationException("no floor around " + from);
        }
    }
}
