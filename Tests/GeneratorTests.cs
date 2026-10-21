using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core;
using Xunit;

namespace DungeonGen.Tests
{
    public class GeneratorTests
    {
        [Fact]
        public void SameSeedProducesIdenticalLayout()
        {
            var s = new DungeonSettings();
            var a = AsciiRenderer.Render(DungeonGenerator.Generate(s, 777));
            var b = AsciiRenderer.Render(DungeonGenerator.Generate(s, 777));
            Assert.Equal(a, b);
        }

        [Fact]
        public void DifferentSeedsProduceDifferentLayouts()
        {
            var s = new DungeonSettings();
            var a = AsciiRenderer.Render(DungeonGenerator.Generate(s, 1), false);
            var b = AsciiRenderer.Render(DungeonGenerator.Generate(s, 2), false);
            Assert.NotEqual(a, b);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(99)]
        [InlineData(123456)]
        [InlineData(-5)]
        public void EveryRoomIsReachableOnFoot(int seed)
        {
            var s = new DungeonSettings();
            var layout = DungeonGenerator.Generate(s, seed);
            var problems = new List<string>();
            Assert.True(LayoutValidator.IsPlayable(layout, s, problems), string.Join("\n", problems));
        }

        [Fact]
        public void RolesAreAssignedAsConfigured()
        {
            var s = new DungeonSettings { SpawnCount = 4, ExtractCount = 3 };
            var layout = DungeonGenerator.Generate(s, 2024);
            Assert.Equal(4, layout.SpawnRooms.Count());
            Assert.Equal(3, layout.ExtractRooms.Count());
            Assert.Single(layout.Rooms, r => r.Type == RoomType.FinalBoss);
            Assert.All(layout.SpawnRooms, r => Assert.Equal(0, r.DepthFromSpawn));
            Assert.All(layout.SpawnRooms, r => Assert.Equal(0, r.LootTier));
            Assert.Equal(s.LootTiers - 1, layout.FinalBossRoom.LootTier);
        }

        [Fact]
        public void CorridorsNeverCutThroughRoomInteriors()
        {
            var s = new DungeonSettings();
            var layout = DungeonGenerator.Generate(s, 31337);
            foreach (var room in layout.Rooms)
                for (int x = room.Bounds.X; x < room.Bounds.XMax; x++)
                    for (int y = room.Bounds.Y; y < room.Bounds.YMax; y++)
                        Assert.Equal(TileType.RoomFloor, layout.Tiles[x, y]);
        }

        [Fact]
        public void WalkableCellsAreAlwaysEnclosed()
        {
            var s = new DungeonSettings();
            var layout = DungeonGenerator.Generate(s, 5);
            for (int x = 0; x < layout.Width; x++)
                for (int y = 0; y < layout.Height; y++)
                {
                    if (!layout.IsWalkable(new GridPoint(x, y))) continue;
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            var n = new GridPoint(x + dx, y + dy);
                            Assert.True(layout.InBounds(n), "walkable cell on canvas edge");
                            Assert.NotEqual(TileType.Void, layout.Tiles[n.X, n.Y]);
                        }
                }
        }

        [Fact]
        public void ZeroLoopChanceGivesATree()
        {
            var s = new DungeonSettings { LoopChance = 0f };
            var layout = DungeonGenerator.Generate(s, 8);
            // A spanning tree over N rooms has exactly N-1 edges (unless a carve was dropped).
            Assert.Equal(layout.Rooms.Count - 1, layout.Corridors.Count);
            Assert.DoesNotContain(layout.Corridors, c => c.IsLoop);
        }

        [Fact]
        public void HundredSeedsAllPlayable()
        {
            var s = new DungeonSettings();
            for (int seed = 1000; seed < 1100; seed++)
            {
                var layout = DungeonGenerator.Generate(s, seed);
                var problems = new List<string>();
                Assert.True(LayoutValidator.IsPlayable(layout, s, problems), $"seed {seed}: {string.Join("; ", problems)}");
            }
        }
    }
}

namespace DungeonGen.Tests
{
    public class TeamScalingTests
    {
        [Theory]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(8)]
        public void TeamPresetProducesOneSpawnPerTeam(int teams)
        {
            var s = DungeonSettings.ForTeams(teams);
            for (int seed = 1; seed <= 20; seed++)
            {
                var layout = DungeonGenerator.Generate(s, seed);
                Assert.Equal(teams, layout.SpawnRooms.Count());
                Assert.Equal(s.ExtractCount, layout.ExtractRooms.Count());
                Assert.Single(layout.Rooms, r => r.Type == RoomType.FinalBoss);
                var problems = new List<string>();
                Assert.True(LayoutValidator.IsPlayable(layout, s, problems), $"seed {seed}: {string.Join("; ", problems)}");
            }
        }

        [Fact]
        public void EightTeamsStartFair()
        {
            var s = DungeonSettings.ForTeams(8);
            for (int seed = 1; seed <= 30; seed++)
            {
                var layout = DungeonGenerator.Generate(s, seed);
                var f = layout.Fairness;
                Assert.Equal(8, f.Spawns.Count);
                Assert.All(f.Spawns, sp => Assert.True(sp.ToNearestExtract > 0 && sp.ToFinalBoss > 0, "every team can reach an extract and the final boss"));
                // No team's walk to its nearest extract may be more than ~2x another team's.
                Assert.True(f.Score >= 0.5f, $"seed {seed}: fairness {f.Score:0.00} (extract {f.ExtractRatio:0.00}, final boss {f.FinalBossRatio:0.00}, boss {f.BossRatio:0.00}, spacing {f.SpawnSpacingRatio:0.00})");
            }
        }

        [Fact]
        public void RingSpawnsSitOnThePerimeter()
        {
            var s = DungeonSettings.ForTeams(8);
            var layout = DungeonGenerator.Generate(s, 11);
            var center = new GridPoint(s.Width / 2, s.Height / 2);
            foreach (var spawn in layout.SpawnRooms)
            {
                int d = spawn.Center.ManhattanDistance(center);
                Assert.True(d > s.Width / 3, $"spawn {spawn.Id} is only {d} cells from the center");
            }
            Assert.True(layout.FinalBossRoom.Center.ManhattanDistance(center) < s.MaxRoomSize, "final boss sits at the center");
        }

        [Fact]
        public void CandidateSelectionIsDeterministic()
        {
            var s = DungeonSettings.ForTeams(8);
            var a = AsciiRenderer.Render(DungeonGenerator.Generate(s, 4242));
            var b = AsciiRenderer.Render(DungeonGenerator.Generate(s, 4242));
            Assert.Equal(a, b);
        }

        [Fact]
        public void SixteenTeamsStillPlayable()
        {
            var s = DungeonSettings.ForTeams(16);
            var layout = DungeonGenerator.Generate(s, 1);
            Assert.Equal(16, layout.SpawnRooms.Count());
            var problems = new List<string>();
            Assert.True(LayoutValidator.IsPlayable(layout, s, problems), string.Join("; ", problems));
        }
    }
}

namespace DungeonGen.Tests
{
    using DungeonGen.Core.Combat;

    public class BossVaultKeyTests
    {
        [Theory]
        [InlineData(3)]
        [InlineData(8)]
        public void BossesVaultsAndKeysArePlaced(int teams)
        {
            var s = DungeonSettings.ForTeams(teams);
            for (int seed = 1; seed <= 15; seed++)
            {
                var layout = DungeonGenerator.Generate(s, seed);
                Assert.Equal(s.BossCount, layout.BossRooms.Count());
                Assert.Equal(s.VaultCount, layout.VaultRooms.Count());
                Assert.Equal(s.VaultCount, layout.Keys.Count);
                Assert.NotNull(layout.FinalBossRoom);
                Assert.NotNull(layout.BossExit);
                Assert.Equal(layout.FinalBossRoom.Id, layout.BossExit.RoomId);
                Assert.All(layout.BossRooms, b => Assert.NotNull(b.Encounter));
                Assert.NotNull(layout.FinalBossRoom.Encounter);
            }
        }

        [Fact]
        public void FinalBossSitsAtTheCenterAndExitIsInside()
        {
            var s = DungeonSettings.ForTeams(8);
            var layout = DungeonGenerator.Generate(s, 21);
            var center = new GridPoint(s.Width / 2, s.Height / 2);
            Assert.True(layout.FinalBossRoom.Bounds.Contains(center));
            Assert.True(layout.FinalBossRoom.Bounds.Contains(layout.BossExit.Cell));
            Assert.Equal(TileType.RoomFloor, layout.Tiles[layout.BossExit.Cell.X, layout.BossExit.Cell.Y]);
        }

        [Fact]
        public void VaultsAreLockedDeadEndsAndKeysAreElsewhere()
        {
            var s = DungeonSettings.ForTeams(8);
            for (int seed = 1; seed <= 15; seed++)
            {
                var layout = DungeonGenerator.Generate(s, seed);
                foreach (var v in layout.VaultRooms)
                {
                    Assert.Single(v.Neighbours);
                    Assert.All(v.Doors, d => Assert.Equal(TileType.LockedDoor, layout.Tiles[d.X, d.Y]));
                    Assert.True(v.RequiredKeyId >= 0);
                    var key = layout.Keys.Single(k => k.VaultRoomId == v.Id);
                    Assert.NotEqual(v.Id, key.HolderRoomId);
                    Assert.NotEqual(RoomType.Spawn, layout.Rooms[key.HolderRoomId].Type);
                    Assert.NotEqual(RoomType.Vault, layout.Rooms[key.HolderRoomId].Type);
                    Assert.True(key.WalkToVault >= s.KeyMinWalk, $"seed {seed}: key only {key.WalkToVault} cells from its vault");
                    Assert.Equal(key.DroppedByBoss, layout.Rooms[key.HolderRoomId].Type == RoomType.Boss);
                    Assert.Equal(s.LootTiers - 1, v.LootTier);
                }
            }
        }

        [Fact]
        public void LockedDoorsDoNotCutOffAnythingButTheVault()
        {
            var s = DungeonSettings.ForTeams(8);
            var layout = DungeonGenerator.Generate(s, 5);
            var problems = new List<string>();
            Assert.True(LayoutValidator.IsPlayable(layout, s, problems), string.Join("; ", problems));
            Assert.All(layout.VaultRooms, v => Assert.False(layout.IsWalkable(v.Doors[0])));
            Assert.All(layout.VaultRooms, v => Assert.True(layout.IsWalkable(v.Doors[0], ignoreLocks: true)));
        }

        [Fact]
        public void RoomBossIsTunedForTwoAndAHalfPlayers()
        {
            var b = new CombatBaseline();
            var boss = EncounterTuner.RoomBoss(b);
            Assert.False(EncounterTuner.Simulate(boss, b, 1).Wins);
            Assert.False(EncounterTuner.Simulate(boss, b, 2).Wins);
            Assert.True(EncounterTuner.Simulate(boss, b, 3).Wins);
            Assert.True(EncounterTuner.Simulate(boss, b, 2, b.GoodGearMultiplier).Wins, "a well-geared duo should be able to take a room boss");
        }

        [Fact]
        public void FinalBossNeedsAGearedFullParty()
        {
            var b = new CombatBaseline();
            var room = EncounterTuner.RoomBoss(b);
            var final = EncounterTuner.FinalBoss(b);
            Assert.True(final.Health > room.Health && final.Dps > room.Dps, "final boss is strictly harder");
            Assert.False(EncounterTuner.Simulate(final, b, b.PartySize).Wins, "baseline-geared full party wipes");
            Assert.True(EncounterTuner.Simulate(final, b, b.PartySize, b.GoodGearMultiplier).Wins, "geared full party wins");
            Assert.False(EncounterTuner.Simulate(final, b, b.PartySize - 1, b.GoodGearMultiplier).Wins, "one short, even geared, wipes");
        }

        [Fact]
        public void RosterAveragesToOneBaselinePlayer()
        {
            var roster = ClassArchetype.DefaultRoster;
            Assert.Equal(6, roster.Count);
            Assert.InRange(roster.Average(c => c.DpsMultiplier), 0.95f, 1.05f);
            Assert.InRange(roster.Average(c => c.HealthMultiplier), 0.95f, 1.05f);
            Assert.Equal(roster.Count, roster.Select(c => c.Role).Distinct().Count());
        }
    }
}
