using System;
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

namespace DungeonGen.Tests
{
    using DungeonGen.Core.Combat;

    public class BossTypeTests
    {
        [Theory]
        [InlineData(3)]
        [InlineData(8)]
        public void NoArchetypeRepeatsWithinADungeon(int teams)
        {
            var s = DungeonSettings.ForTeams(teams);
            for (int seed = 1; seed <= 40; seed++)
            {
                var layout = DungeonGenerator.Generate(s, seed);
                var names = layout.BossRooms.Select(r => r.Boss.Archetype.Name).ToList();
                Assert.Equal(s.BossCount, names.Count);
                Assert.Equal(names.Count, names.Distinct().Count());
                Assert.All(layout.BossRooms, r => Assert.NotNull(r.Boss.Budget));
                Assert.All(layout.BossRooms, r => Assert.Same(r.Boss.Budget, r.Encounter));
            }
        }

        [Fact]
        public void FourBossesCoverAllFourFamilies()
        {
            var s = DungeonSettings.ForTeams(8);
            for (int seed = 1; seed <= 40; seed++)
            {
                var layout = DungeonGenerator.Generate(s, seed);
                var families = layout.BossRooms.Select(r => r.Boss.Archetype.Family).Distinct().Count();
                Assert.Equal(4, families);
            }
        }

        [Fact]
        public void EveryArchetypeShowsUpAcrossSeeds()
        {
            var s = DungeonSettings.ForTeams(8);
            var seen = new HashSet<string>();
            for (int seed = 1; seed <= 60; seed++)
                foreach (var r in DungeonGenerator.Generate(s, seed).BossRooms) seen.Add(r.Boss.Archetype.Name);
            Assert.Equal(BossArchetype.DefaultPool.Count, seen.Count);
        }

        [Fact]
        public void KeybearerExactlyWhenTheBossDropsAKey()
        {
            var s = DungeonSettings.ForTeams(8);
            for (int seed = 1; seed <= 30; seed++)
            {
                var layout = DungeonGenerator.Generate(s, seed);
                foreach (var r in layout.BossRooms)
                    Assert.Equal(r.HoldsKeyId >= 0, r.Boss.HasAffix(BossAffix.Keybearer));
                Assert.All(layout.BossRooms, r => Assert.True(r.Boss.Affixes.Count(a => a != BossAffix.Keybearer) <= s.MaxAffixesPerBoss));
            }
        }

        [Fact]
        public void AffixesScaleTheBudgetAndKeepTheLine()
        {
            var b = new CombatBaseline();
            var plain = EncounterTuner.RoomBoss(b);
            var s = DungeonSettings.ForTeams(8);
            for (int seed = 1; seed <= 30; seed++)
                foreach (var r in DungeonGenerator.Generate(s, seed).BossRooms)
                {
                    float hp = r.Boss.Archetype.HealthMultiplier, dps = r.Boss.Archetype.DpsMultiplier;
                    foreach (var a in r.Boss.Affixes) { hp *= a.HealthMultiplier; dps *= a.DpsMultiplier; }
                    Assert.Equal(plain.Health * hp, r.Boss.Budget.Health, 1);
                    Assert.Equal(plain.Dps * dps, r.Boss.Budget.Dps, 1);
                    // Even the nastiest roll stays beatable by a geared trio; a solo never wins.
                    Assert.True(EncounterTuner.Simulate(r.Boss.Budget, b, 3, b.GoodGearMultiplier).Wins, r.Boss.DisplayName);
                    Assert.False(EncounterTuner.Simulate(r.Boss.Budget, b, 1).Wins, r.Boss.DisplayName);
                }
        }

        [Fact]
        public void FinalBossHasItsOwnArchetypeOutsideThePool()
        {
            var layout = DungeonGenerator.Generate(DungeonSettings.ForTeams(8), 9);
            Assert.Same(BossArchetype.FinalBoss, layout.FinalBossRoom.Boss.Archetype);
            Assert.DoesNotContain(BossArchetype.FinalBoss, BossArchetype.DefaultPool);
            Assert.Equal(8, BossArchetype.DefaultPool.Count);
            Assert.All(System.Enum.GetValues(typeof(BossFamily)).Cast<BossFamily>(),
                f => Assert.Equal(2, BossArchetype.DefaultPool.Count(a => a.Family == f)));
        }

        [Fact]
        public void RingArenasAreSizedForTheirBoss()
        {
            var s = DungeonSettings.ForTeams(8);
            for (int seed = 1; seed <= 20; seed++)
                foreach (var r in DungeonGenerator.Generate(s, seed).BossRooms)
                {
                    int side = System.Math.Max(r.Bounds.Width, r.Bounds.Height);
                    Assert.True(side >= System.Math.Min(r.Boss.Archetype.ArenaMin, s.MaxRoomSize) - 1, $"{r.Boss.Archetype.Name} in a {side}-cell room");
                }
        }
    }
}

namespace DungeonGen.Tests
{
    using DungeonGen.Core.Combat;

    public class StatsAndResistanceTests
    {
        [Fact]
        public void AllTensInMediumGearIsTheBaselinePlayer()
        {
            var b = new CombatBaseline();
            var avg = new ClassArchetype("Average", ClassRole.MeleeDps, Attributes.Baseline, Attribute.Strength, 1f, ArmourWeight.Medium, "", new string[0], "");
            var st = PlayerStats.For(avg, 0, b);
            Assert.Equal(b.PlayerHealth, st.Health, 1);
            Assert.Equal(b.PlayerDps, st.Dps, 1);
            Assert.Equal(0.05f, st.CritChance, 3);
            Assert.Equal(50f, st.CarryKg, 1);
        }

        [Fact]
        public void ClassMultipliersComeFromAttributesAndStillAverageToOne()
        {
            var roster = ClassArchetype.DefaultRoster;
            Assert.All(roster, c => Assert.Equal(50, c.BaseAttributes.Total));
            Assert.InRange(roster.Average(c => c.DpsMultiplier), 0.97f, 1.03f);
            Assert.InRange(roster.Average(c => c.HealthMultiplier), 0.97f, 1.03f);
            var warden = roster.First(c => c.Name == "Warden");
            Assert.Equal(0.70f, warden.DpsMultiplier, 2);
            Assert.Equal(1.50f, warden.HealthMultiplier, 2);
            var reaver = roster.First(c => c.Name == "Reaver");
            Assert.Equal(1.30f, reaver.DpsMultiplier, 2);
        }

        [Fact]
        public void GearTierThreeIsAboutGoodGear()
        {
            var b = new CombatBaseline();
            foreach (var c in ClassArchetype.DefaultRoster)
            {
                var t0 = PlayerStats.For(c, 0, b);
                var t3 = PlayerStats.For(c, 3, b);
                Assert.InRange(t3.Dps / t0.Dps, 1.35f, 1.6f);
                Assert.InRange(t3.Health / t0.Health, 1.1f, 1.4f);
            }
        }

        [Fact]
        public void MitigationHasDiminishingReturnsAndACap()
        {
            Assert.Equal(0f, StatFormulas.Mitigation(0f), 3);
            Assert.Equal(0.5f, StatFormulas.Mitigation(100f), 3);
            Assert.Equal(0.75f, StatFormulas.Mitigation(10000f), 3);
            Assert.True(StatFormulas.Mitigation(-100f) < 0f);
            var r = new Resistances();
            r.Set(DamageType.Fire, 100f);
            Assert.Equal(50f, r.DamageTaken(100f, DamageType.Fire), 1);
            Assert.Equal(100f, r.DamageTaken(100f, DamageType.Frost), 1);
        }

        [Fact]
        public void ArmourWeightTradesPhysicalForMagic()
        {
            var b = new CombatBaseline();
            var warden = PlayerStats.For(ClassArchetype.DefaultRoster.First(c => c.Name == "Warden"), 0, b);
            var pyro = PlayerStats.For(ClassArchetype.DefaultRoster.First(c => c.Name == "Pyromancer"), 0, b);
            Assert.True(warden.TakeHit(100f, DamageType.Physical) < pyro.TakeHit(100f, DamageType.Physical));
            Assert.True(warden.TakeHit(100f, DamageType.Fire) > pyro.TakeHit(100f, DamageType.Fire));
            Assert.True(warden.MoveSpeed < pyro.MoveSpeed);
        }

        [Fact]
        public void DamageProfilesSumToOne()
        {
            foreach (var a in BossArchetype.DefaultPool.Append(BossArchetype.FinalBoss).Select(a => a.Damage).Concat(MobArchetype.DefaultRoster.Select(m => m.Damage)))
                Assert.Equal(1f, a.Types.Sum(t => a.Fraction(t)), 3);
        }

        [Fact]
        public void BossesAndMobsCoverEveryDamageType()
        {
            var all = System.Enum.GetValues(typeof(DamageType)).Cast<DamageType>().ToList();
            var bossTypes = BossArchetype.DefaultPool.Select(a => a.Damage.Dominant).Distinct().ToList();
            var mobTypes = MobArchetype.DefaultRoster.Select(m => m.Damage.Dominant).Distinct().ToList();
            Assert.All(all, t => Assert.Contains(t, mobTypes));
            Assert.True(bossTypes.Count >= 5, "bosses should spread over at least five of six damage types");
            Assert.All(MobArchetype.DefaultRoster, m => Assert.NotEqual(m.Resists, m.WeakTo));
            Assert.All(BossArchetype.DefaultPool, a => Assert.Contains(MobArchetype.DefaultRoster, m => m.Name == a.RetinueMob));
        }

        [Fact]
        public void EveryOrdinaryRoomHasMobsAndTypesAreSpread()
        {
            var s = DungeonSettings.ForTeams(8);
            for (int seed = 1; seed <= 20; seed++)
            {
                var layout = DungeonGenerator.Generate(s, seed);
                foreach (var r in layout.Rooms)
                {
                    bool shouldHave = r.Type != RoomType.Spawn && r.Type != RoomType.Extract && !r.IsBossRoom;
                    Assert.Equal(shouldHave, r.Mobs != null);
                }
                var types = layout.MobPacks.Select(m => m.Mob.Damage.Dominant).Distinct().Count();
                Assert.True(types >= 5, $"seed {seed}: only {types} damage types among mobs");
                // Deeper rooms get bigger, tougher packs: compare the most and least dangerous third.
                var ordered = layout.Rooms.Where(r => r.Mobs != null).OrderBy(r => r.Danger).ToList();
                int third = Math.Max(1, ordered.Count / 3);
                var shallow = ordered.Take(third).Average(r => r.Mobs.PlayerEquivalents);
                var deep = ordered.Skip(ordered.Count - third).Average(r => r.Mobs.PlayerEquivalents);
                Assert.True(deep > shallow, $"seed {seed}: deep {deep:0.00} vs shallow {shallow:0.00}");
                Assert.All(ordered, r => Assert.InRange(r.Mobs.PlayerEquivalents, 0.2f, 3.2f));
            }
        }

        [Fact]
        public void BossRetinueGuardsTheApproach()
        {
            var s = DungeonSettings.ForTeams(8);
            int matched = 0, total = 0;
            for (int seed = 1; seed <= 20; seed++)
            {
                var layout = DungeonGenerator.Generate(s, seed);
                foreach (var boss in layout.BossRooms)
                    foreach (var n in boss.Neighbours.Select(id => layout.Rooms[id]).Where(n => n.Mobs != null))
                    {
                        total++;
                        if (n.Mobs.Mob.Name == boss.Boss.Archetype.RetinueMob) matched++;
                    }
            }
            Assert.True(total > 0 && (float)matched / total > 0.5f, $"{matched}/{total} boss-adjacent rooms hold the retinue");
        }
    }
}
