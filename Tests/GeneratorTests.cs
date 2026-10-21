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
            Assert.Single(layout.Rooms, r => r.Type == RoomType.Vault);
            Assert.All(layout.SpawnRooms, r => Assert.Equal(0, r.DepthFromSpawn));
            Assert.All(layout.SpawnRooms, r => Assert.Equal(0, r.LootTier));
            Assert.Equal(s.LootTiers - 1, layout.Rooms.First(r => r.Type == RoomType.Vault).LootTier);
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
