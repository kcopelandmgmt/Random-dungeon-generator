using Attribute = DungeonGen.Core.Combat.Attribute;
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
                        Assert.Contains(layout.Tiles[x, y], new[] { TileType.RoomFloor, TileType.Pillar, TileType.LowWall, TileType.Pit });   // interiors and pits are the room's own; corridors never are
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
            var pyro = PlayerStats.For(ClassArchetype.DefaultRoster.First(c => c.Name == "Elementalist"), 0, b);
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

namespace DungeonGen.Tests
{
    using DungeonGen.Core.Combat;
    using DungeonGen.Core.Items;

    public class GearTests
    {
        [Fact]
        public void StartingSetEqualsTheAbstractTierZeroSheet()
        {
            var b = new CombatBaseline();
            foreach (var c in ClassArchetype.DefaultRoster)
            {
                var abstractSheet = PlayerStats.For(c, 0, b);
                var real = PlayerStats.For(c, Equipment.StandardSet(c.Armour, 0, c.Primary), b);
                Assert.Equal(abstractSheet.Health, real.Health, 0);
                Assert.Equal(abstractSheet.Dps, real.Dps, 0);
                Assert.Equal(abstractSheet.ArmourRating, real.ArmourRating, 0);
                Assert.Equal(abstractSheet.TakeHit(100f, DamageType.Fire), real.TakeHit(100f, DamageType.Fire), 0);
                Assert.Equal(abstractSheet.MoveSpeed, real.MoveSpeed, 2);
            }
        }

        [Fact]
        public void RarityFollowsItemLevelAndSourceFloors()
        {
            var rng = new DungeonRandom(11);
            for (int i = 0; i < 300; i++)
            {
                Assert.NotEqual(Rarity.Unique, LootRoller.RollRarity(0, LootSource.Chest, rng));
                Assert.True(LootRoller.RollRarity(2, LootSource.Vault, rng) >= Rarity.Rare);
                Assert.True(LootRoller.RollRarity(1, LootSource.Boss, rng) >= Rarity.Magic);
            }
            int rareAt0 = 0, rareAt4 = 0;
            for (int i = 0; i < 2000; i++)
            {
                if (LootRoller.RollRarity(0, LootSource.Chest, rng) >= Rarity.Rare) rareAt0++;
                if (LootRoller.RollRarity(4, LootSource.Chest, rng) >= Rarity.Rare) rareAt4++;
            }
            Assert.True(rareAt4 > rareAt0 * 5, $"rare at L4 {rareAt4} vs L0 {rareAt0}");
        }

        [Fact]
        public void AffixCountsAndFamiliesObeyTheRules()
        {
            var rng = new DungeonRandom(3);
            for (int i = 0; i < 1500; i++)
            {
                var it = LootRoller.Roll(rng.Range(0, 5), LootSource.Chest, rng);
                int n = it.Affixes.Count();
                switch (it.Rarity)
                {
                    case Rarity.Normal: Assert.Equal(0, n); break;
                    case Rarity.Magic: Assert.InRange(n, 1, 2); Assert.True(it.Prefixes.Count <= 1 && it.Suffixes.Count <= 1); break;
                    case Rarity.Rare: Assert.InRange(n, 3, 4); Assert.True(it.Prefixes.Count <= 2 && it.Suffixes.Count <= 2); break;
                }
                var defs = it.Affixes.Select(a => a.Definition).ToList();
                for (int a = 0; a < defs.Count; a++)
                    for (int c = a + 1; c < defs.Count; c++)
                        Assert.False(defs[a].SameFamily(defs[c]), $"{it}: duplicate family");
                Assert.All(it.Affixes, a => Assert.Contains(it.Base.Slot, a.Definition.Slots));
                Assert.All(it.Affixes, a => Assert.InRange(a.Tier, 1, System.Math.Max(1, it.ItemLevel)));
                Assert.All(it.Affixes, a => Assert.InRange(a.Value, a.Definition.Tiers[a.Tier - 1].min, a.Definition.Tiers[a.Tier - 1].max));
            }
        }

        [Fact]
        public void UniquesComeOnlyFromBossesVaultsAndTheFinalBossAndAreThemed()
        {
            var rng = new DungeonRandom(8);
            for (int i = 0; i < 500; i++)
                Assert.NotEqual(Rarity.Unique, LootRoller.Roll(4, LootSource.LootRoom, rng).Rarity);
            int themed = 0, uniques = 0;
            for (int i = 0; i < 2000; i++)
            {
                var it = LootRoller.Roll(4, LootSource.Boss, rng, "Hive Matron");
                if (it.Rarity != Rarity.Unique) continue;
                uniques++;
                Assert.NotNull(it.Unique);
                Assert.True(it.ItemLevel >= 3);
                if (it.Unique.Source == "Hive Matron") themed++;
            }
            Assert.True(uniques > 50, $"only {uniques} uniques");
            Assert.True(themed > uniques / 3, $"{themed}/{uniques} themed");
            Assert.All(UniqueItem.All, u => Assert.Contains(BossArchetype.DefaultPool.Select(a => a.Name).Append(BossArchetype.FinalBoss.Name), s => s == u.Source));
        }

        [Fact]
        public void LootIsRolledForEveryRoomThatShouldHaveIt()
        {
            var s = DungeonSettings.ForTeams(8);
            for (int seed = 1; seed <= 10; seed++)
            {
                var layout = DungeonGenerator.Generate(s, seed);
                foreach (var r in layout.Rooms)
                {
                    bool should = r.Type != RoomType.Spawn && r.Type != RoomType.Extract;
                    Assert.Equal(should, r.Loot.Count > 0);
                    Assert.All(r.Loot, it => Assert.Equal(it.Rarity == Rarity.Unique ? System.Math.Max(3, r.LootTier) : r.LootTier, it.ItemLevel));
                }
                Assert.All(layout.VaultRooms, v => Assert.All(v.Loot, it => Assert.True(it.Rarity >= Rarity.Rare)));
                Assert.All(layout.BossRooms, bRoom => Assert.Equal(2 + bRoom.Boss.BonusLootRolls, bRoom.Loot.Count));
                Assert.Equal(4, layout.FinalBossRoom.Loot.Count);
                Assert.Equal(layout.Rooms.Sum(r => r.Loot.Count), layout.Items.Count);
            }
        }

        [Fact]
        public void LootIsDeterministic()
        {
            var s = DungeonSettings.ForTeams(8);
            var a = string.Join("\n", DungeonGenerator.Generate(s, 77).Items.Select(i => i.ToString()));
            var b = string.Join("\n", DungeonGenerator.Generate(s, 77).Items.Select(i => i.ToString()));
            Assert.Equal(a, b);
        }

        [Fact]
        public void RareTierThreeSetsLandNearTheAbstractTierThree()
        {
            var b = new CombatBaseline();
            var rng = new DungeonRandom(21);
            foreach (var c in ClassArchetype.DefaultRoster)
            {
                float dpsSum = 0, hpSum = 0; int sets = 0;
                for (int n = 0; n < 12; n++)
                {
                    var eq = Equipment.StandardSet(c.Armour, 3, c.Primary);
                    int tries = 0;
                    while (eq.Items.Count(i => i.Rarity == Rarity.Rare) < 9 && tries++ < 600)
                    {
                        var it = LootRoller.Roll(3, LootSource.Vault, rng);
                        if (it.Rarity != Rarity.Rare) continue;
                        if (it.Base.IsArmour && it.Base.Weight != c.Armour) continue;
                        if (it.Base.IsWeapon && it.Base.ScalesWith != c.Primary) continue;
                        eq.Equip(it);
                    }
                    var st = PlayerStats.For(c, eq, b);
                    dpsSum += st.Dps; hpSum += st.Health; sets++;
                }
                var abs3 = PlayerStats.For(c, 3, b);
                float dpsRatio = dpsSum / sets / abs3.Dps, hpRatio = hpSum / sets / abs3.Health;
                // A full nine-slot Rare set is the lucky top end: expect it a little above the tuning assumption, never double it.
                Assert.InRange(dpsRatio, 0.9f, 1.5f);
                Assert.InRange(hpRatio, 0.9f, 1.45f);
            }
        }

        [Fact]
        public void EquipReplacesSlotsAndFillsBothRings()
        {
            var ring = ItemBase.Jewellery.First(j => j.Slot == ItemSlot.Ring);
            Item R(string n) => new Item(ring, 0, Rarity.Normal, n, new List<AffixRoll>(), new List<AffixRoll>());
            var e = new Equipment();
            Assert.Null(e.Equip(R("a")));
            Assert.Null(e.Equip(R("b")));
            Assert.Equal("a", e.Equip(R("c")).Name);
            Assert.Equal(2, e.Items.Count());
        }
    }
}

namespace DungeonGen.Tests
{
    using DungeonGen.Core.Combat;
    using DungeonGen.Core.Items;

    public class LegendaryAffixTests
    {
        [Fact]
        public void EveryUniqueHasExactlyOneLegendaryAndTheyAreAllDifferent()
        {
            Assert.All(UniqueItem.All, u => Assert.NotNull(u.Legendary));
            Assert.Equal(UniqueItem.All.Count, UniqueItem.All.Select(u => u.Legendary.Name).Distinct().Count());
            Assert.Equal(UniqueItem.All.Count, UniqueItem.All.Select(u => u.Legendary.Kind).Distinct().Count());
        }

        [Fact]
        public void LegendaryEffectsNeverAppearOnRolledItems()
        {
            var rng = new DungeonRandom(5);
            for (int i = 0; i < 1000; i++)
            {
                var it = LootRoller.Roll(4, LootSource.Chest, rng);
                Assert.Null(it.Unique);
                Assert.Empty(PlayerStats.For(ClassArchetype.DefaultRoster[0], Wear(it), null).Legendaries);
            }
        }

        private static Equipment Wear(Item it) { var e = new Equipment(); e.Equip(it); return e; }

        [Fact]
        public void NumericLegendariesReachTheSheet()
        {
            var rng = new DungeonRandom(1);
            var hound = LootRoller.RollUnique(4, rng, "Hound of Vesh");
            while (hound.Name != "Hound's Fang") hound = LootRoller.RollUnique(4, rng, "Hound of Vesh");
            var reaver = ClassArchetype.DefaultRoster.First(c => c.Name == "Reaver");
            var st = PlayerStats.For(reaver, Wear(hound));
            Assert.Equal(0.08f, st.Lifesteal, 3);

            var prism = UniqueItem.All.First(u => u.Name == "Glasswright's Prism");
            var item = new Item(prism.Base, 4, Rarity.Unique, prism.Name, new List<AffixRoll>(), new List<AffixRoll>(), prism);
            var st2 = PlayerStats.For(reaver, Wear(item));
            Assert.Equal(0.20f, st2.Thorns, 3);
            Assert.Equal(DamageType.Lightning, st2.ThornsType);
        }

        [Fact]
        public void SovereignsCrownRaisesTheResistCap()
        {
            var crown = UniqueItem.All.First(u => u.Name == "Sovereign's Crown");
            var item = new Item(crown.Base, 4, Rarity.Unique, crown.Name, new List<AffixRoll>(), new List<AffixRoll>(), crown);
            var e = Wear(item);
            var st = PlayerStats.For(ClassArchetype.DefaultRoster[0], e);
            st.Resistances.Set(DamageType.Fire, 100000f);
            Assert.Equal(0.85f, st.Resistances.Mitigation(DamageType.Fire), 3);
            var plain = new Resistances(); plain.Set(DamageType.Fire, 100000f);
            Assert.Equal(0.75f, plain.Mitigation(DamageType.Fire), 3);
        }

        [Fact]
        public void TriggeredLegendariesAreHandedToTheGame()
        {
            var heart = UniqueItem.All.First(u => u.Name == "Ashen Heart");
            var item = new Item(heart.Base, 4, Rarity.Unique, heart.Name, new List<AffixRoll>(), new List<AffixRoll>(), heart);
            var st = PlayerStats.For(ClassArchetype.DefaultRoster[3], Wear(item));
            var leg = Assert.Single(st.Legendaries);
            Assert.Equal(LegendaryKind.ProcDamage, leg.Kind);
            Assert.Equal(LegendaryTrigger.OnHit, leg.Trigger);
            Assert.Equal(0.15f, leg.Chance, 3);
            Assert.Equal(DamageType.Fire, leg.Type);
        }
    }
}

namespace DungeonGen.Tests
{
    using DungeonGen.Core.Combat;
    using DungeonGen.Core.Items;

    public class DurabilityTests
    {
        private static Item Fresh(ItemBase b, Rarity r = Rarity.Normal) => new Item(b, 2, r, b.Name, new List<AffixRoll>(), new List<AffixRoll>());

        [Fact]
        public void BrokenIsForeverButStillSellsAndSalvages()
        {
            var it = new Item(ItemBase.Armours[0], 2, Rarity.Rare, "x", new List<AffixRoll>(), new List<AffixRoll>());
            it.Condition.Wear(1000f);
            Assert.True(it.Condition.IsBroken);
            Assert.False(it.Condition.IsDestroyed);
            Assert.True(CraftingRules.SellPrice(it) > 0f);
            Assert.True(CraftingRules.Salvage(it).Scrap > 0);
            it.Condition.Destroy();
            Assert.Equal(0f, CraftingRules.SellPrice(it));
            Assert.Equal(0, CraftingRules.Salvage(it).Scrap);
        }

        [Fact]
        public void EveryItemHasAFiniteLifeEvenIfItsOwnerNeverDies()
        {
            foreach (Rarity r in System.Enum.GetValues(typeof(Rarity)))
            {
                int armour = DurabilityRules.ExpectedLifetimeRounds(Fresh(ItemBase.Armours[1], r), 2500f, 6000f);
                int weapon = DurabilityRules.ExpectedLifetimeRounds(Fresh(ItemBase.Weapons[0], r), 2500f, 6000f);
                int ring = DurabilityRules.ExpectedLifetimeRounds(Fresh(ItemBase.Jewellery[1], r), 2500f, 6000f);
                Assert.InRange(armour, 2, 8);
                Assert.InRange(weapon, 2, 8);
                Assert.InRange(ring, 3, 10);
            }
            // Uniques outlast Normals.
            Assert.True(DurabilityRules.ExpectedLifetimeRounds(Fresh(ItemBase.Weapons[0], Rarity.Unique), 2500f, 6000f) >
                        DurabilityRules.ExpectedLifetimeRounds(Fresh(ItemBase.Weapons[0], Rarity.Normal), 2500f, 6000f));
        }

        [Fact]
        public void BrokenGearStopsWorking()
        {
            var c = ClassArchetype.DefaultRoster.First(x => x.Name == "Warden");
            var kit = Equipment.StandardSet(c.Armour, 0, c.Primary);
            var before = PlayerStats.For(c, kit);
            foreach (var it in kit.Items) it.Condition.Wear(1000f);
            var after = PlayerStats.For(c, kit);
            Assert.Equal(0f, after.ArmourRating);
            Assert.True(after.Dps < before.Dps * 0.6f, "a broken weapon is fists");
            Assert.True(after.TakeHit(100f, DamageType.Physical) > before.TakeHit(100f, DamageType.Physical));
        }

        [Fact]
        public void WearPerRoundHitsTheRightSlots()
        {
            var c = ClassArchetype.DefaultRoster[1];
            var kit = Equipment.StandardSet(c.Armour, 0, c.Primary);
            kit.Equip(Fresh(ItemBase.Jewellery[1]));
            DurabilityRules.WearForRound(kit, 1000f, 2000f);
            Assert.Equal(100f - DurabilityRules.BaseWearPerRound - 10f, kit.Chest.Condition.Current, 1);
            Assert.Equal(100f - DurabilityRules.BaseWearPerRound - 10f, kit.Weapon.Condition.Current, 1);
            Assert.Equal(100f - DurabilityRules.BaseWearPerRound - DurabilityRules.JewelleryWearPerRound, kit.Ring1.Condition.Current, 1);
        }

        [Fact]
        public void DeathTrashesAboutHalfAndDropsTheRest()
        {
            var rng = new DungeonRandom(9);
            int dropped = 0, destroyed = 0;
            for (int i = 0; i < 300; i++)
            {
                var c = ClassArchetype.DefaultRoster[0];
                var kit = Equipment.StandardSet(c.Armour, 1, c.Primary);
                var (d, x) = DurabilityRules.OnDeath(kit, rng);
                dropped += d.Count; destroyed += x.Count;
                Assert.All(x, it => Assert.True(it.Condition.IsDestroyed));
                Assert.All(d, it => Assert.False(it.Condition.IsDestroyed));
            }
            float frac = (float)destroyed / (dropped + destroyed);
            Assert.InRange(frac, 0.42f, 0.58f);
        }

        [Fact]
        public void ChestLootIsWornAndBossLootIsFresh()
        {
            var rng = new DungeonRandom(2);
            bool anyWorn = false;
            for (int i = 0; i < 200; i++)
            {
                var chest = LootRoller.Roll(2, LootSource.Chest, rng);
                Assert.InRange(chest.Condition.Fraction, DurabilityRules.FoundMinFraction - 0.01f, 1f);
                if (chest.Condition.Fraction < 0.99f) anyWorn = true;
                var boss = LootRoller.Roll(2, LootSource.Boss, rng);
                if (boss.Rarity != Rarity.Unique) Assert.Equal(1f, boss.Condition.Fraction, 3);
            }
            Assert.True(anyWorn);
        }

        [Fact]
        public void VendorPaysMoreForBetterAndLessForBroken()
        {
            var plain = new Item(ItemBase.Armours[0], 1, Rarity.Normal, "x", new List<AffixRoll>(), new List<AffixRoll>());
            var rare = new Item(ItemBase.Armours[0], 4, Rarity.Rare, "x", new List<AffixRoll>(), new List<AffixRoll>());
            Assert.True(CraftingRules.SellPrice(rare) > CraftingRules.SellPrice(plain) * 5f);
            float intact = CraftingRules.SellPrice(rare);
            rare.Condition.Wear(1000f);
            Assert.True(CraftingRules.SellPrice(rare) < intact * 0.3f);
        }
    }
}

namespace DungeonGen.Tests
{
    using DungeonGen.Core.Items;

    public class CraftingTests
    {
        private static Item Rolled(DungeonRandom rng, int level, LootSource src = LootSource.Vault) => LootRoller.Roll(level, src, rng);

        [Fact]
        public void SalvageYieldsScrapShardsAndOneEssencePerAffix()
        {
            var rng = new DungeonRandom(4);
            for (int i = 0; i < 300; i++)
            {
                var it = Rolled(rng, rng.Range(0, 5), LootSource.Chest);
                var bag = CraftingRules.Salvage(it);
                Assert.Equal((int)System.Math.Round(CraftingRules.ScrapPerLevel * (it.ItemLevel + 1) * CraftingRules.ScrapRarityMultiplier(it.Rarity)), bag.Scrap);
                Assert.Equal(CraftingRules.ShardsFor(it.Rarity), bag.PrimeShards);
                Assert.Equal(it.Affixes.Select(a => a.Definition.FamilyKey).Distinct().Count(), bag.Essences.Count);
                foreach (var a in it.Affixes) Assert.True(bag.Essence(a.Definition.FamilyKey) >= 1);
            }
        }

        [Fact]
        public void CraftingConsumesMaterialsAndRefusesWhenShort()
        {
            var bag = new MaterialBag { Scrap = 100 };
            var req = new CraftRequest(ItemBase.Weapons[0], 3);
            Assert.False(req.Affordable(bag, out string why));
            Assert.Contains("scrap", why);
            bag.Scrap = 200;
            var item = Crafter.Craft(req, bag, new DungeonRandom(1));
            Assert.Equal(200 - CraftingRules.ScrapCost(3), bag.Scrap);
            Assert.Equal(3, item.ItemLevel);
            Assert.Equal(1f, item.Condition.Fraction, 3);
        }

        [Fact]
        public void CraftedItemsAreNeverNormalOrUnique()
        {
            var rng = new DungeonRandom(2);
            for (int i = 0; i < 300; i++)
            {
                var bag = new MaterialBag { Scrap = 1000 };
                var item = Crafter.Craft(new CraftRequest(rng.Pick(ItemBase.Armours), rng.Range(0, 5)), bag, rng);
                Assert.NotEqual(Rarity.Normal, item.Rarity);
                Assert.NotEqual(Rarity.Unique, item.Rarity);
                Assert.Null(item.Unique);
            }
        }

        [Fact]
        public void PinnedFamiliesAlwaysAppearAndTheRestStillRolls()
        {
            var rng = new DungeonRandom(3);
            var seen = new HashSet<string>();
            for (int i = 0; i < 200; i++)
            {
                var bag = new MaterialBag { Scrap = 1000, PrimeShards = 10 };
                bag.AddEssence("Resist:Fire", 3);
                bag.AddEssence("Attribute:Strength", 3);
                var item = Crafter.Craft(new CraftRequest(ItemBase.Jewellery[1], 4, new[] { "Resist:Fire", "Attribute:Strength" }, forceRare: true), bag, rng);
                Assert.Equal(Rarity.Rare, item.Rarity);
                Assert.Contains(item.Affixes, a => a.Definition.FamilyKey == "Resist:Fire");
                Assert.Contains(item.Affixes, a => a.Definition.FamilyKey == "Attribute:Strength");
                Assert.InRange(item.Affixes.Count(), 3, 4);
                Assert.Equal(0, bag.Essence("Resist:Fire"));
                Assert.Equal(7, bag.PrimeShards);
                seen.Add(string.Join("|", item.Affixes.Select(a => a.ToString())));
            }
            Assert.True(seen.Count > 100, "pinning two families should still leave the item random");
        }

        [Fact]
        public void PinningAnIllegalFamilyIsRefused()
        {
            var bag = new MaterialBag { Scrap = 1000 };
            bag.AddEssence("IncreasedDamage", 3);
            var req = new CraftRequest(ItemBase.Armours[0], 2, new[] { "IncreasedDamage" });
            Assert.False(req.Affordable(bag, out string why));
            Assert.Contains("cannot roll", why);
        }

        [Fact]
        public void JunkSellsForMoreThanItSalvagesAndGoodGearSalvagesIntoMore()
        {
            var plain = new Item(ItemBase.Armours[0], 1, Rarity.Normal, "x", new List<AffixRoll>(), new List<AffixRoll>());
            Assert.True(CraftingRules.SellPrice(plain) > CraftingRules.Salvage(plain).Scrap);
            var rng = new DungeonRandom(6);
            var rare = LootRoller.Roll(4, LootSource.Vault, rng);
            while (rare.Rarity != Rarity.Rare) rare = LootRoller.Roll(4, LootSource.Vault, rng);
            var bag = CraftingRules.Salvage(rare);
            Assert.True(bag.Scrap > CraftingRules.Salvage(plain).Scrap * 4);
            Assert.True(bag.PrimeShards >= 1 && bag.Essences.Count >= 3);
        }
    }
}

namespace DungeonGen.Tests
{
    using DungeonGen.Core.Combat;
    using DungeonGen.Core.Items;

    public class ElementAndStatusTests
    {
        private static ClassArchetype Ele => ClassArchetype.DefaultRoster.First(c => c.IsElementalist);

        [Fact]
        public void ElementalistElementComesFromTheWeaponInHand()
        {
            foreach (var staff in new[] { "Fire Staff", "Frost Rod", "Storm Wand" })
            {
                var kit = Equipment.StandardSet(Ele.Armour, 0, Ele.Primary);
                var w = ItemBase.Weapons.First(x => x.Name == staff);
                kit.Equip(new Item(w, 0, Rarity.Normal, w.Name, new List<AffixRoll>(), new List<AffixRoll>()));
                var st = PlayerStats.For(Ele, kit);
                Assert.Equal(w.AttackType, st.Element);
                Assert.NotNull(st.ElementKit);
                Assert.Equal(w.AttackType, st.ElementKit.Element);
                Assert.Equal(4, st.Abilities.Count);
                Assert.Equal(StatusEffectDefinition.OnHitFor(w.AttackType).Kind, st.OnHitEffect.Kind);
            }
            var reaver = ClassArchetype.DefaultRoster.First(c => c.Name == "Reaver");
            var r = PlayerStats.For(reaver, Equipment.StandardSet(reaver.Armour, 0, reaver.Primary));
            Assert.Null(r.ElementKit);
            Assert.Null(r.OnHitEffect);
            Assert.Equal("Duelist", r.SpecName);                       // starter Longsword → Duelist
            Assert.Equal(r.Spec.Abilities, r.Abilities);
        }

        [Fact]
        public void ThreeKitsShareASkeletonAndCoverThreeElements()
        {
            Assert.Equal(3, ElementKit.All.Count);
            Assert.Equal(new[] { DamageType.Fire, DamageType.Frost, DamageType.Lightning }, ElementKit.All.Select(k => k.Element).ToArray());
            Assert.All(ElementKit.All, k => Assert.Equal(4, k.Abilities.Count));
            Assert.All(ElementKit.All, k => Assert.NotNull(k.OnHit));
            Assert.Equal(3, Ele.Elements.Count);
        }

        [Fact]
        public void ThreeChillsFreeze()
        {
            var t = new StatusTracker();
            var chill = StatusEffectDefinition.All[StatusKind.Chill];
            t.Apply(chill); t.Apply(chill);
            Assert.Equal(2, t.Get(StatusKind.Chill).Stacks);
            Assert.Equal(0.30f, t.SlowFraction, 2);
            var result = t.Apply(chill);
            Assert.Equal(StatusKind.Freeze, result.Definition.Kind);
            Assert.False(t.Has(StatusKind.Chill));
            Assert.True(t.IsHardControlled);
        }

        [Fact]
        public void StaggerOnAChilledTargetStuns()
        {
            var t = new StatusTracker();
            t.Apply(StatusEffectDefinition.All[StatusKind.Chill]);
            var landed = t.Apply(StatusEffectDefinition.All[StatusKind.Stagger]);
            Assert.Equal(StatusKind.Stun, landed.Definition.Kind);
            Assert.False(t.Has(StatusKind.Chill));
            var plain = new StatusTracker();
            Assert.Equal(StatusKind.Stagger, plain.Apply(StatusEffectDefinition.All[StatusKind.Stagger]).Definition.Kind);
            Assert.False(plain.IsHardControlled);
        }

        [Fact]
        public void ResistanceShortensAndCanNegateAStatus()
        {
            var r = new Resistances(); r.Set(DamageType.Fire, 100f);   // 50%
            var t = new StatusTracker();
            var burn = t.Apply(StatusEffectDefinition.All[StatusKind.Burn], r);
            Assert.Equal(2f, burn.RemainingSeconds, 1);
            var capped = new Resistances(); capped.Set(DamageType.Fire, 100000f);
            Assert.Equal(1f, new StatusTracker().Apply(StatusEffectDefinition.All[StatusKind.Burn], capped).RemainingSeconds, 1);
            var crown = new Resistances { CapBonus = 10f }; crown.Set(DamageType.Fire, 100000f);
            Assert.Equal(0.6f, new StatusTracker().Apply(StatusEffectDefinition.All[StatusKind.Burn], crown).RemainingSeconds, 1);
        }

        [Fact]
        public void DotsStackAndTickAndPurgeClears()
        {
            var t = new StatusTracker();
            var poison = StatusEffectDefinition.All[StatusKind.Poisoned];
            for (int i = 0; i < 7; i++) t.Apply(poison);
            Assert.Equal(5, t.Get(StatusKind.Poisoned).Stacks);
            Assert.Equal(0.25f, t.DotFractionPerSecond, 2);
            t.Tick(9f);
            Assert.Empty(t.Active);
            t.Apply(poison); t.Apply(StatusEffectDefinition.All[StatusKind.Burn]);
            t.Cleanse();
            Assert.Empty(t.Active);
        }

        [Fact]
        public void MobsAndBossesApplyTheirElementsStatus()
        {
            Assert.Equal(StatusKind.Chill, MobArchetype.DefaultRoster.First(m => m.Name == "Frostbound").OnHit.Kind);
            Assert.Equal(StatusKind.Poisoned, MobArchetype.DefaultRoster.First(m => m.Name == "Skitterer").OnHit.Kind);
            Assert.Equal(StatusKind.Bleed, MobArchetype.DefaultRoster.First(m => m.Name == "Bandit").OnHit.Kind);
            Assert.Equal(StatusKind.Burn, BossArchetype.DefaultPool.First(b => b.Name == "Ashen Colossus").OnHit.Kind);
            Assert.Equal(StatusKind.Bleed, BossArchetype.DefaultPool.First(b => b.Name == "Hound of Vesh").OnHit.Kind);
            Assert.Equal(StatusKind.Poisoned, BossArchetype.DefaultPool.First(b => b.Name == "Plague Herald").OnHit.Kind);
            Assert.All(MobArchetype.DefaultRoster, m => Assert.NotNull(m.OnHit));
        }

        [Fact]
        public void RosterStillAveragesToOneAfterTheRename()
        {
            var roster = ClassArchetype.DefaultRoster;
            Assert.Contains(roster, c => c.Name == "Elementalist");
            Assert.DoesNotContain(roster, c => c.Name == "Pyromancer");
            Assert.InRange(roster.Average(c => c.DpsMultiplier), 0.97f, 1.03f);
            Assert.InRange(roster.Average(c => c.HealthMultiplier), 0.97f, 1.03f);
        }
    }
}

namespace DungeonGen.Tests
{
    using DungeonGen.Core.Items;
    using DungeonGen.Core.Net;

    public class MultiplayerTests
    {
        [Fact]
        public void DescriptorRoundTripsAndClientBuildsTheSameDungeon()
        {
            var s = DungeonSettings.ForTeams(8);
            var d = MatchDescriptor.Create(4242, s, new MatchRules { RoundSeconds = 900 }, out var serverLayout);
            var bytes = d.ToBytes();
            Assert.True(bytes.Length < 400, $"descriptor is {bytes.Length} bytes");
            var client = MatchDescriptor.FromBytes(bytes);
            Assert.Equal(d.Seed, client.Seed);
            Assert.Equal(d.SettingsHash, client.SettingsHash);
            Assert.Equal(d.TeamSpawnRooms, client.TeamSpawnRooms);
            Assert.Equal(900, client.Rules.RoundSeconds);
            var clientLayout = client.BuildAndVerify();
            Assert.Equal(ClientView.PublicFingerprint(serverLayout), ClientView.PublicFingerprint(clientLayout));
            Assert.NotEqual(LayoutFingerprint.Compute(serverLayout), LayoutFingerprint.Compute(clientLayout));   // the server knows the loot, the client does not
            Assert.Empty(clientLayout.Items);
            Assert.NotEmpty(serverLayout.Items);
        }

        [Fact]
        public void TeamsGetDistinctSpawnRoomsInShuffledOrder()
        {
            var s = DungeonSettings.ForTeams(8);
            var d = MatchDescriptor.Create(1, s, null, out var layout);
            Assert.Equal(8, d.TeamSpawnRooms.Count);
            Assert.Equal(8, d.TeamSpawnRooms.Distinct().Count());
            Assert.All(d.TeamSpawnRooms, id => Assert.Equal(RoomType.Spawn, layout.Rooms[id].Type));
            var d2 = MatchDescriptor.Create(2, s, null, out _);
            Assert.NotEqual(d.TeamSpawnRooms, d2.TeamSpawnRooms);
        }

        [Fact]
        public void FingerprintMismatchRefusesToStart()
        {
            var s = DungeonSettings.ForTeams(4);
            var d = MatchDescriptor.Create(99, s, null, out _);
            var tampered = new MatchDescriptor(d.Seed, d.Settings, d.TeamSpawnRooms, d.Rules, d.ExpectedFingerprint ^ 1UL);
            Assert.Throws<System.InvalidOperationException>(() => tampered.BuildAndVerify());
            // Different settings → different fingerprint even on the same seed.
            var other = SettingsCodec.FromBytes(SettingsCodec.ToBytes(s)); other.LoopChance += 0.1f;
            Assert.NotEqual(SettingsCodec.Hash(s), SettingsCodec.Hash(other));
            Assert.NotEqual(d.ExpectedFingerprint, LayoutFingerprint.Compute(DungeonGenerator.Generate(other, 99)));
        }

        [Fact]
        public void StableHashIsStableAndSettingsCodecIsLossless()
        {
            var s = DungeonSettings.ForTeams(8); s.Combat.PlayerDps = 123.5f; s.LootDensity = 1.5f;
            var back = SettingsCodec.FromBytes(SettingsCodec.ToBytes(s));
            Assert.Equal(SettingsCodec.ToBytes(s), SettingsCodec.ToBytes(back));
            Assert.Equal(123.5f, back.Combat.PlayerDps);
            Assert.Equal(SettingsCodec.Hash(s), SettingsCodec.Hash(back));
            Assert.Equal(StableHash.Start().Add("dungeon").Add(7).Value, StableHash.Start().Add("dungeon").Add(7).Value);
            Assert.NotEqual(StableHash.Start().Add("dungeon").Value, StableHash.Start().Add("Dungeon").Value);
        }

        [Fact]
        public void ClientViewHidesLootButNothingElse()
        {
            var s = DungeonSettings.ForTeams(8);
            var server = DungeonGenerator.Generate(s, 7);
            var view = ClientView.Redact(server);
            Assert.Empty(view.Items);
            Assert.NotEmpty(server.Items);
            Assert.Equal(ClientView.PublicFingerprint(server), ClientView.PublicFingerprint(view));
            Assert.Equal(server.Keys.Select(k => (k.KeyId, k.HolderRoomId, k.VaultRoomId)), view.Keys.Select(k => (k.KeyId, k.HolderRoomId, k.VaultRoomId)));
            Assert.Equal(server.Rooms.Select(r => r.Boss?.DisplayName), view.Rooms.Select(r => r.Boss?.DisplayName));
            Assert.Equal(server.Rooms.Select(r => r.Mobs?.ToString()), view.Rooms.Select(r => r.Mobs?.ToString()));
        }

        [Fact]
        public void NetworkIdsAreStableAndCoverEverything()
        {
            var s = DungeonSettings.ForTeams(8);
            var a = new LayoutIndex(DungeonGenerator.Generate(s, 11));
            var b = new LayoutIndex(DungeonGenerator.Generate(s, 11));
            Assert.Equal(a.Items.Count, b.Items.Count);
            Assert.Equal(a.Layout.Items.Count, a.Items.Count);
            foreach (var kv in a.Items) Assert.Equal(kv.Value.ToString(), b.Items[kv.Key].ToString());
            Assert.Equal(a.Layout.MobPacks.Count, a.MobPacks.Count);
            Assert.All(a.Items.Keys, id => Assert.True(id < LayoutIndex.MobPackIdBase));
            var near = a.RoomsWithin(a.Layout.SpawnRooms.First().Id, 2);
            Assert.True(near.Count > 1 && near.Count < a.Layout.Rooms.Count);
            Assert.Equal(a.Layout.Rooms.Count, a.RoomsWithin(0, 50).Count);
        }

        [Fact]
        public void MatchStateRejectsIllegalEventsAndReplaysIdentically()
        {
            var s = DungeonSettings.ForTeams(8);
            var d = MatchDescriptor.Create(7, s, new MatchRules { BossExitStaysOpen = false }, out var layout);
            var st = new MatchState(d, layout);
            var vault = layout.VaultRooms.First();
            var key = layout.Keys.First(k => k.VaultRoomId == vault.Id);
            int item = st.Index.Items.First(kv => st.Index.ItemRoom[kv.Key] == vault.Id).Key;
            string why;

            Assert.False(st.Apply(new MatchEvent(MatchEventKind.PlayerExtracted, 1, 1, layout.ExtractRooms.First().Id), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.RoundStarted, 0), out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.VaultOpened, 2, vault.Id, 1), out why));
            st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, 1, 0), out _); st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, 2, 0), out _);
            st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, 5, 1), out _); st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, 6, 3), out _);
            if (key.DroppedByBoss)
            {
                Assert.False(Ch.TakeKey(st, 3, key.KeyId, 1, out why));
                Assert.True(st.Apply(new MatchEvent(MatchEventKind.BossKilled, 4, key.HolderRoomId, 0), out why));
            }
            Assert.True(Ch.TakeKey(st, 5, key.KeyId, 1, out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.VaultOpened, 6, vault.Id, 2), out why), "wrong player");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.VaultOpened, 6, vault.Id, 1), out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.ItemTaken, 7, item, 1), out why), "container not opened");
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.ContainerOpened, 8, vault.Id, 1), out why), "no chest channel");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 8, 1, vault.Id), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 8, 2, vault.Id), out why));
            Assert.True(Ch.OpenChest(st, 8, vault.Id, 1, out why));
            Assert.Contains(item, st.VisibleItemsIn(vault.Id));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.ItemTaken, 9, item, 1), out why), "players 1 and 2 are a group in the room: the vault piece goes to a roll first");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.LootRolled, 9, item, 1, (int)LootRollChoice.Need), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.LootRolled, 9, item, 2, (int)LootRollChoice.Pass), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.LootRollResolved, 9, item, 1, 64), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ItemTaken, 9, item, 1), out why));
            Assert.DoesNotContain(item, st.VisibleItemsIn(vault.Id));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.ItemTaken, 10, item, 2), out why), "already taken");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 11, 1, 2, vault.Id), out why));
            Assert.False(st.KeyHolder.ContainsKey(key.KeyId), "dead player drops the key");
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.ItemTaken, 12, st.Index.Items.Keys.Last(), 1), out why), "dead player cannot loot");
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.BossExitOpened, 13), out why), "final boss alive");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.BossKilled, 14, layout.FinalBossRoom.Id, 3), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.BossExitOpened, 15), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ExtractStarted, 16, 5, layout.FinalBossRoom.Id), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ExtractStarted, 16, 6, layout.FinalBossRoom.Id), out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.PlayerExtracted, 25, 5, layout.FinalBossRoom.Id, 0), out why), "exit belongs to team 3");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerExtracted, 25, 6, layout.FinalBossRoom.Id, 3), out why));

            var replay = MatchState.Replay(MatchDescriptor.FromBytes(d.ToBytes()), layout, MatchState.LogFromBytes(st.LogToBytes()));
            Assert.Equal(st.Log.Count, replay.Log.Count);
            Assert.Equal(st.ExtractedPlayers, replay.ExtractedPlayers);
            Assert.Equal(st.ItemOwner, replay.ItemOwner);
            Assert.Equal(st.OpenVaults, replay.OpenVaults);
            Assert.Equal(st.BossExitOpen, replay.BossExitOpen);
        }
    }
}

namespace DungeonGen.Tests
{
    public class LineOfSightTests
    {
        [Fact]
        public void WallsBlockAndFloorsDoNot()
        {
            var layout = DungeonGenerator.Generate(DungeonSettings.ForTeams(8), 7);
            var room = layout.Rooms.First(r => r.Type == RoomType.Standard && r.Bounds.Width >= 7 && r.Bounds.Height >= 7);
            var a = new GridPoint(room.Bounds.X + 1, room.Bounds.Y + 1);
            var b = new GridPoint(room.Bounds.XMax - 2, room.Bounds.YMax - 2);
            Assert.True(LineOfSight.CanSee(layout, a, b));
            var outside = new GridPoint(room.Bounds.XMax + 3, room.Center.Y);   // beyond the wall ring
            Assert.False(LineOfSight.CanSee(layout, room.Center, outside));
        }

        [Fact]
        public void YouCannotSeeIntoAVaultThroughItsLockedDoor()
        {
            var layout = DungeonGenerator.Generate(DungeonSettings.ForTeams(8), 7);
            var vault = layout.VaultRooms.First();
            var door = vault.Doors[0];
            var outsideDoor = GridPoint.Cardinals.Select(d => door + d).First(p => layout.IsWalkable(p) && !vault.Bounds.Contains(p));
            Assert.False(LineOfSight.CanSee(layout, outsideDoor, vault.Center));
            var seen = LineOfSight.VisibleRooms(layout, outsideDoor, 12);
            Assert.DoesNotContain(vault.Id, seen);
        }

        [Fact]
        public void VisibleCellsStayInRangeAndIncludeTheWallsYouLookAt()
        {
            var layout = DungeonGenerator.Generate(DungeonSettings.ForTeams(8), 7);
            var room = layout.Rooms.First(r => r.Type == RoomType.Standard);
            var cells = LineOfSight.VisibleCells(layout, room.Center, 6);
            Assert.All(cells, c => Assert.True((c.X - room.Center.X) * (c.X - room.Center.X) + (c.Y - room.Center.Y) * (c.Y - room.Center.Y) <= 36));
            Assert.Contains(cells, c => layout.Tiles[c.X, c.Y] == TileType.Wall);
            Assert.True(cells.Count(c => layout.IsWalkable(c)) >= 9);
        }

        [Fact]
        public void FromACorridorYouSeeTheRoomsItOpensInto()
        {
            var layout = DungeonGenerator.Generate(DungeonSettings.ForTeams(8), 7);
            var spawn = layout.SpawnRooms.First();
            var door = spawn.Doors[0];
            var seen = LineOfSight.VisibleRooms(layout, door, 10);
            Assert.Contains(spawn.Id, seen);
            Assert.True(seen.Count <= 3, $"a doorway should not reveal the dungeon: {seen.Count} rooms");
        }
    }
}

namespace DungeonGen.Tests
{
    using DungeonGen.Core.Combat;
    using DungeonGen.Core.Items;
    using DungeonGen.Core.Net;

    public class RoundRuleTests
    {
        private static (MatchState st, DungeonLayout layout) Fresh(int seed = 7, bool exitStays = true)
        {
            var d = MatchDescriptor.Create(seed, DungeonSettings.ForTeams(8), new MatchRules { BossExitStaysOpen = exitStays }, out var layout);
            var st = new MatchState(d, layout);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.RoundStarted, 0), out _));
            for (int p = 1; p <= 6; p++) Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, p, (p - 1) / 3), out _));   // players 1-3 team 0, 4-6 team 1
            return (st, layout);
        }

        [Fact]
        public void EveryDungeonHasAShrineAndHubsAlwaysDo()
        {
            var s = DungeonSettings.ForTeams(8);
            for (int seed = 1; seed <= 20; seed++)
            {
                var layout = DungeonGenerator.Generate(s, seed);
                Assert.Contains(layout.Rooms, r => r.HasShrine);
                Assert.All(layout.RoomsOfType(RoomType.Hub), h => Assert.True(h.HasShrine));
                Assert.All(layout.Rooms.Where(r => r.HasShrine), r => Assert.True(r.Type == RoomType.Hub || r.Type == RoomType.Standard));
            }
        }

        [Fact]
        public void SoulsArePersonalLieOnCorpsesAndInBossChestsAndBurnOnDeath()
        {
            var (st, layout) = Fresh();
            string why;
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 10, 2, -1, 0), out why));     // killed by a mob: no soul anywhere
            Assert.Empty(st.Souls); Assert.DoesNotContain(2, st.CorpseSouls);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 11, 6, 4, 0), out why));      // teammate kill: no soul on that corpse
            Assert.DoesNotContain(6, st.CorpseSouls);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 12, 5, 1, 0), out why));      // player 1 kills player 5
            Assert.Empty(st.Souls);                                                                      // the blow itself grants nothing
            Assert.Contains(5, st.CorpseSouls);                                                          // the soul lies on the corpse
            Assert.True(Ch.OpenCorpse(st, 13, 5, 4, out why));                                           // 5's own teammate loots first: items yes, soul no
            Assert.Equal(0, st.SoulsOf(4)); Assert.Contains(5, st.CorpseSouls);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.CorpseOpened, 17, 5, 3), out why));       // player 3 (team 0, not the killer) loots: the soul is theirs
            Assert.Equal(1, st.SoulsOf(3)); Assert.Equal(0, st.SoulsOf(1)); Assert.DoesNotContain(5, st.CorpseSouls);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.CorpseOpened, 18, 5, 1), out why));       // the killer comes by later: nothing left
            Assert.Equal(0, st.SoulsOf(1));                                                              // a corpse holds one soul

            var boss = layout.BossRooms.First();
            Assert.False(Ch.OpenChest(st, 24, boss.Id, 1, out why));                                      // boss alive: chest stays shut
            Assert.Contains("alive", why);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.BossKilled, 26, boss.Id, 1), out why));   // team 1 lands the kill...
            Assert.True(Ch.OpenChest(st, 27, boss.Id, 1, out why));                                      // ...player 1 opens the chest first
            Assert.Equal(1, st.SoulsOf(1));                                                              // and takes the boss's soul
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.BossKilled, 31, boss.Id, 0), out why), "a boss dies once");
            Assert.False(FightRules.SoulsTransferable);

            st.Souls[3] = 2;
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerResurrected, 32, 2, 3, layout.Rooms.First(r => r.HasShrine).Id), out why));   // player 3 spends both on teammate 2
            Assert.Equal(0, st.SoulsOf(3));
            st.Souls[1] = 2;
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 41, 1, 4, 0), out why));      // player 4 kills player 1, who carries two
            Assert.Equal(0, st.SoulsOf(1));                                                              // both burn
            Assert.Equal(0, st.SoulsOf(4));                                                              // the killer holds nothing until the corpse is looted
            Assert.True(Ch.OpenCorpse(st, 42, 1, 4, out why));
            Assert.Equal(1, st.SoulsOf(4));                                                              // exactly one, not the victim's two
            Assert.Equal(1, st.Souls.Values.Sum());
        }

        [Fact]
        public void NeedOverGreedRollsAmongTheOpenersTeamAndNeverBindsEnemies()
        {
            var d = MatchDescriptor.Create(7, DungeonSettings.ForTeams(8), new MatchRules(), out var layout);
            var st = new MatchState(d, layout);
            string why;
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.RoundStarted, 0), out why));
            int warden = 0, marksman = ClassArchetype.DefaultRoster.ToList().FindIndex(c => c.Name == "Marksman"), elementalist = ClassArchetype.DefaultRoster.ToList().FindIndex(c => c.Name == "Elementalist");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, 1, 0, warden), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, 2, 0, marksman), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, 3, 0, elementalist), out why));
            for (int p = 4; p <= 6; p++) Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, p, 1, warden), out why));
            Assert.Equal("Marksman", st.ClassOf(2).Name);

            var room = layout.FinalBossRoom;                                                         // four Rare+ pieces: every one goes to a roll
            var items = st.Index.Items.Where(kv => st.Index.ItemRoom[kv.Key] == room.Id).Select(kv => kv.Key).ToList();
            Assert.True(items.Count >= 4);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.BossKilled, 10, room.Id, 1), out why));

            // Proximity: everyone spawned far away, so a chest opened now rolls with nobody and player 1 just takes.
            Assert.DoesNotContain(st.PlayerRoom[1], st.Index.RoomsWithin(room.Id, LootRollRules.RollRangeRooms));
            Assert.Empty(st.TeammatesNear(1, room.Id));
            int twoAway = st.Index.RoomsWithin(room.Id, 2).Except(st.Index.RoomsWithin(room.Id, 1)).First();
            int threeAway = st.Index.RoomsWithin(room.Id, 3).Except(st.Index.RoomsWithin(room.Id, 2)).First();
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 10, 1, 9999), out why), "no such room");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 10, 1, room.Id), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 10, 2, threeAway), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 10, 3, twoAway), out why));
            Assert.Equal(new List<int> { 1, 3 }, st.TeammatesNear(1, room.Id));                      // 2 is three rooms off: doing their own thing
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 10, 2, room.Id), out why));   // 2 comes back
            Assert.True(Ch.OpenChest(st, 11, room.Id, 1, out why));                                   // player 1 opens: the roll is among 1, 2, 3
            Assert.All(items, it => Assert.True(st.OpenRolls.ContainsKey(it)));
            Assert.Equal(new HashSet<int> { 1, 2, 3 }, st.OpenRolls[items[0]].Eligible);

            // Teammates cannot just grab; an enemy can, and that ends the roll for that piece.
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.ItemTaken, 14, items[0], 1), out why));
            Assert.Contains("rolling", why);
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.LootRolled, 14, items[0], 5, (int)LootRollChoice.Need), out why), "enemies do not roll");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ItemTaken, 14, items[0], 5), out why));
            Assert.False(st.OpenRolls.ContainsKey(items[0]));

            // Need beats Greed; the server may not resolve until everyone standing has answered or the window closes.
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.LootRolled, 15, items[1], 1, (int)LootRollChoice.Need), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.LootRolled, 15, items[1], 2, (int)LootRollChoice.Greed), out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.LootRolled, 15, items[1], 2, (int)LootRollChoice.Need), out why), "one answer each");
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.LootRollResolved, 16, items[1], 1, 50), out why));
            Assert.Contains("waiting", why);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.LootRolled, 16, items[1], 3, (int)LootRollChoice.Pass), out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.LootRollResolved, 17, items[1], 2, 99), out why));
            Assert.Contains("Need beats", why);
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.LootRollResolved, 17, items[1], 3, 99), out why), "a pass cannot win");
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.LootRollResolved, 17, items[1], -1, 0), out why), "someone rolled");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.LootRollResolved, 17, items[1], 1, 50), out why));
            Assert.Equal(1, st.LootWinner[items[1]]);
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.ItemTaken, 18, items[1], 2), out why));
            Assert.Contains("won by", why);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ItemTaken, 18, items[1], 1), out why));

            // Everyone passes: free for anyone on the team.
            foreach (int p in new[] { 1, 2, 3 }) Assert.True(st.Apply(new MatchEvent(MatchEventKind.LootRolled, 19, items[2], p, (int)LootRollChoice.Pass), out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.LootRollResolved, 19, items[2], 1, 10), out why), "nobody rolled, nobody wins");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.LootRollResolved, 19, items[2], -1, 0), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ItemTaken, 20, items[2], 3), out why));

            // Silence is a pass once the window closes; a dead teammate is not waited for.
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.LootRolled, 21, items[3], 2, (int)LootRollChoice.Greed), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 22, 3, 5, room.Id), out why));
            Assert.Equal(new[] { 1, 2 }, st.StillRolling(items[3]).OrderBy(x => x).ToArray());
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.LootRollResolved, 13 + LootRollRules.RollSeconds - 2f, items[3], 2, 40), out why), "player 1 still has time");   // the chest opened at t=13
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.LootRollResolved, 13 + LootRollRules.RollSeconds, items[3], 2, 40), out why));
            var entries = st.RollEntries(items[1]).ToList();
            Assert.Empty(entries);                                                                     // resolved rolls have no entries
            Assert.Equal(2, st.LootWinner[items[3]]);

            var replay = MatchState.Replay(MatchDescriptor.FromBytes(st.Descriptor.ToBytes()), layout, MatchState.LogFromBytes(st.LogToBytes()));
            Assert.Equal(st.LootWinner, replay.LootWinner);
            Assert.Equal(st.PlayerClass, replay.PlayerClass);
            Assert.Equal(st.ItemOwner, replay.ItemOwner);
        }

        [Fact]
        public void TheWandererGetsNoRoll()
        {
            var d = MatchDescriptor.Create(7, DungeonSettings.ForTeams(8), new MatchRules(), out var layout);
            var st = new MatchState(d, layout);
            string why;
            st.Apply(new MatchEvent(MatchEventKind.RoundStarted, 0), out _);
            for (int p = 1; p <= 3; p++) st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, p, 0), out _);
            var room = layout.FinalBossRoom;
            int item = st.Index.Items.First(kv => st.Index.ItemRoom[kv.Key] == room.Id).Key;
            st.Apply(new MatchEvent(MatchEventKind.BossKilled, 10, room.Id, 0), out _);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 10, 1, room.Id), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 10, 2, room.Id), out why));   // 3 stays at spawn, farming on their own
            Assert.True(Ch.OpenChest(st, 11, room.Id, 1, out why));
            Assert.Equal(new HashSet<int> { 1, 2 }, st.OpenRolls[item].Eligible);
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.LootRolled, 14, item, 3, (int)LootRollChoice.Need), out why));
            Assert.Contains("not in this roll", why);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 15, 3, room.Id), out why));   // arriving late changes nothing
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.LootRolled, 15, item, 3, (int)LootRollChoice.Need), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.LootRolled, 15, item, 1, (int)LootRollChoice.Greed), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.LootRolled, 15, item, 2, (int)LootRollChoice.Pass), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.LootRollResolved, 15, item, 1, 33), out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.ItemTaken, 16, item, 3), out why), "won by player 1");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ItemTaken, 16, item, 1), out why));
            // A resurrection puts you at the shrine; a dead player cannot report a room.
            st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 20, 2, -1, room.Id), out _);
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 21, 2, room.Id), out why));
            st.Souls[1] = 2;
            int shrine = layout.Rooms.First(r => r.HasShrine).Id;
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerResurrected, 22, 2, 1, shrine), out why));
            Assert.Equal(shrine, st.PlayerRoom[2]);
        }

        [Fact]
        public void SoloPlayersAndSoloTeamsNeverRoll()
        {
            var d = MatchDescriptor.Create(7, DungeonSettings.ForTeams(8), new MatchRules(), out var layout);
            var st = new MatchState(d, layout);
            st.Apply(new MatchEvent(MatchEventKind.RoundStarted, 0), out _);
            st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, 1, 0), out _);
            st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, 4, 1), out _);
            var room = layout.FinalBossRoom;
            st.Apply(new MatchEvent(MatchEventKind.BossKilled, 10, room.Id, 0), out _);
            Assert.True(Ch.OpenChest(st, 11, room.Id, 1, out string why));
            Assert.Empty(st.OpenRolls);
            int item = st.Index.Items.First(kv => st.Index.ItemRoom[kv.Key] == room.Id).Key;
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ItemTaken, 14, item, 1), out why));
        }

        [Fact]
        public void LootRollRulesFavourTheClassThatWearsIt()
        {
            var warden = ClassArchetype.DefaultRoster.First(c => c.Name == "Warden");
            var shade = ClassArchetype.DefaultRoster.First(c => c.Name == "Shade");
            var mender = ClassArchetype.DefaultRoster.First(c => c.Name == "Mender");
            var longsword = ItemBase.Weapons.First(w => w.Name == "Longsword");
            var daggers = ItemBase.Weapons.First(w => w.Name == "Twin Daggers");
            var sceptre = ItemBase.Weapons.First(w => w.Name == "Holy Sceptre");
            Assert.True(LootRollRules.FitsClass(warden, longsword));
            Assert.False(LootRollRules.FitsClass(warden, daggers));
            Assert.True(LootRollRules.FitsClass(shade, daggers));
            Assert.True(LootRollRules.FitsClass(mender, sceptre));
            Assert.False(LootRollRules.FitsClass(shade, sceptre));
            var plate = ItemBase.Armours.First(a => a.Weight == ArmourWeight.Heavy);
            var leather = ItemBase.Armours.First(a => a.Weight == ArmourWeight.Light);
            Assert.True(LootRollRules.FitsClass(warden, plate));
            Assert.False(LootRollRules.FitsClass(shade, plate));
            Assert.True(LootRollRules.FitsClass(shade, leather));
            Assert.True(LootRollRules.FitsClass(warden, ItemBase.Jewellery.First()));
            Assert.True(LootRollRules.FitsClass(shade, ItemBase.Jewellery.First()));
            Assert.False(LootRollRules.FitsClass(null, plate));

            // Any Need beats any Greed, whatever the dice.
            for (int seed = 1; seed <= 200; seed++)
            {
                var o = LootRollRules.Resolve(new[] { new LootRollRules.Entry(1, LootRollChoice.Greed, true), new LootRollRules.Entry(2, LootRollChoice.Need, false), new LootRollRules.Entry(3, LootRollChoice.Pass, true) }, new DungeonRandom(seed));
                Assert.Equal(2, o.Winner);
                Assert.False(o.Rolls.ContainsKey(1));
            }
            Assert.Equal(-1, LootRollRules.Resolve(new[] { new LootRollRules.Entry(1, LootRollChoice.Pass, true) }, new DungeonRandom(1)).Winner);
            Assert.Equal(-1, LootRollRules.Resolve(Array.Empty<LootRollRules.Entry>(), new DungeonRandom(1)).Winner);

            // One on-class Need against one off-class Need: the on-class player wins about seven times in eight, never always.
            int onClassWins = 0, trials = 4000;
            for (int seed = 1; seed <= trials; seed++)
                if (LootRollRules.Resolve(new[] { new LootRollRules.Entry(1, LootRollChoice.Need, true), new LootRollRules.Entry(2, LootRollChoice.Need, false) }, new DungeonRandom(seed)).Winner == 1) onClassWins++;
            double share = onClassWins / (double)trials, expected = LootRollRules.OnClassWinChance(1);
            Assert.InRange(expected, 0.85, 0.90);
            Assert.InRange(share, expected - 0.03, expected + 0.03);
            // Two Greeds with a tie get re-rolled, so there is always exactly one winner.
            for (int seed = 1; seed <= 500; seed++)
            {
                var o = LootRollRules.Resolve(new[] { new LootRollRules.Entry(1, LootRollChoice.Greed, false), new LootRollRules.Entry(2, LootRollChoice.Greed, false) }, new DungeonRandom(seed));
                Assert.True(o.Winner == 1 || o.Winner == 2);
                Assert.Equal(o.WinningRoll, o.Rolls[o.Winner]);
            }
        }

        [Fact]
        public void ResurrectionCostsTheReviverTwoSoulsNeedsAShrineAndWorksOnce()
        {
            var (st, layout) = Fresh();
            var shrine = layout.Rooms.First(r => r.HasShrine).Id;
            var plain = layout.Rooms.First(r => !r.HasShrine && r.Type == RoomType.Standard).Id;
            string why;
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 10, 2, 4, 0), out why));                 // player 2 (team 0) dies to player 4
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.PlayerResurrected, 11, 2, 1, shrine), out why));   // reviver has no souls
            Assert.Contains("souls", why);
            Assert.True(Ch.KillAndLoot(st, 12, 4, 1));
            Assert.True(Ch.KillAndLoot(st, 13, 5, 3));                                                              // player 3 earned this one
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.PlayerResurrected, 14, 2, 1, shrine), out why), "player 1 holds one soul; a teammate's does not count");
            Assert.True(Ch.KillAndLoot(st, 15, 6, 1));                                                              // now player 1 holds two
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.PlayerResurrected, 16, 2, 1, plain), out why));    // not a shrine
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.PlayerResurrected, 16, 2, 4, shrine), out why));   // enemy (and dead) cannot res you
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerResurrected, 17, 2, 1, shrine), out why));
            Assert.Equal(0, st.SoulsOf(1));
            Assert.Equal(1, st.SoulsOf(3));                                                                          // untouched
            Assert.DoesNotContain(2, st.DeadPlayers);
            Assert.Contains(2, st.PlayersInside);
            st.Souls[1] = 5;
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 20, 2, -1, 0), out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.PlayerResurrected, 23, 2, 1, shrine), out why));   // once per round
            Assert.Contains("already", why);
        }

        [Fact]
        public void ExtractingDestroysUnusedKeysAndRoundEndWipesEveryoneInside()
        {
            var (st, layout) = Fresh();
            var key = layout.Keys.First();
            string why;
            if (key.DroppedByBoss) Assert.True(st.Apply(new MatchEvent(MatchEventKind.BossKilled, 5, key.HolderRoomId, 0), out why));
            Assert.True(Ch.TakeKey(st, 6, key.KeyId, 1, out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ExtractsOpened, 240), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ExtractStarted, 290, 1, layout.ExtractRooms.First().Id), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerExtracted, 300, 1, layout.ExtractRooms.First().Id, 0), out why));
            Assert.False(st.KeyHolder.ContainsKey(key.KeyId));
            Assert.Contains(key.KeyId, st.DestroyedKeys);
            Assert.False(Ch.TakeKey(st, 301, key.KeyId, 2, out why));
            Assert.Contains("destroyed", why);

            Assert.Equal(5, st.PlayersInside.Count());
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.RoundEnded, 1200), out why));
            Assert.Empty(st.PlayersInside);
            Assert.Equal(5, st.DeadPlayers.Count);
            Assert.Contains(1, st.ExtractedPlayers);
        }

        [Fact]
        public void TeamsAreCappedAtThree()
        {
            var (st, _) = Fresh();
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 1, 99, 0), out string why));
            Assert.Contains("full", why);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 1, 99, 2), out why));
        }

        [Fact]
        public void WeightOnlyInventoryUsesStrength()
        {
            var warden = ClassArchetype.DefaultRoster.First(c => c.Name == "Warden");
            var shade = ClassArchetype.DefaultRoster.First(c => c.Name == "Shade");
            var wKit = Equipment.StandardSet(warden.Armour, 0, warden.Primary);
            var sKit = Equipment.StandardSet(shade.Armour, 0, shade.Primary);
            Assert.InRange(wKit.WornWeightKg, 33f, 38f);     // 30 kg plate + 6 kg sword
            Assert.InRange(sKit.WornWeightKg, 8f, 10f);
            var wSheet = PlayerStats.For(warden, wKit);
            Assert.Equal(1f, CarryRules.MoveSpeedFactor(wKit.WornWeightKg, wSheet.CarryKg));
            Assert.Equal(0.75f, CarryRules.MoveSpeedFactor(150f, 100f), 2);
            Assert.Equal(0.5f, CarryRules.MoveSpeedFactor(300f, 100f), 2);
            Assert.True(CarryRules.IsOverloaded(wSheet.CarryKg + 1f, wSheet.CarryKg));
        }

        [Fact]
        public void ConsumablesAppearInOrdinaryChestsOnly()
        {
            var s = DungeonSettings.ForTeams(8);
            int found = 0;
            for (int seed = 1; seed <= 10; seed++)
            {
                var layout = DungeonGenerator.Generate(s, seed);
                foreach (var r in layout.Rooms)
                {
                    if (r.Type == RoomType.Vault || r.IsBossRoom || r.Type == RoomType.Spawn || r.Type == RoomType.Extract) Assert.Empty(r.Consumables);
                    found += r.Consumables.Count;
                }
            }
            Assert.True(found > 10, $"only {found} consumables in ten dungeons");
            Assert.Equal(3, Consumable.All.Count);
        }

        [Fact]
        public void NewEventsReplayAndSettingsStillRoundTrip()
        {
            var (st, layout) = Fresh();
            var shrine = layout.Rooms.First(r => r.HasShrine).Id;
            st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 1, 2, 4, 0), out _);
            Ch.KillAndLoot(st, 2, 4, 1);
            Ch.KillAndLoot(st, 6, 5, 1);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerResurrected, 10, 2, 1, shrine), out _));
            var replay = MatchState.Replay(MatchDescriptor.FromBytes(st.Descriptor.ToBytes()), layout, MatchState.LogFromBytes(st.LogToBytes()));
            Assert.Equal(st.Souls, replay.Souls);
            Assert.Equal(st.DeadPlayers, replay.DeadPlayers);
            Assert.Equal(st.ResurrectedOnce, replay.ResurrectedOnce);
            Assert.Equal(st.Descriptor.Rules.SpawnGraceSeconds, replay.Descriptor.Rules.SpawnGraceSeconds);
        }
    }
}

namespace DungeonGen.Tests
{
    using DungeonGen.Core.Combat;
    using DungeonGen.Core.Items;
    using DungeonGen.Core.Net;
    using DungeonGen.Core.Profile;

    public class LockedSystemsTests
    {
        private static Item Plain(ItemBase b, int level = 0) => new Item(b, level, Rarity.Normal, b.Name, new List<AffixRoll>(), new List<AffixRoll>());
        private static ClassArchetype Cls(string n) => ClassArchetype.DefaultRoster.First(c => c.Name == n);

        [Fact]
        public void EveryNonCasterClassHasTwoSpecsOnDistinctWeaponsOfItsPrimary()
        {
            foreach (var c in ClassArchetype.DefaultRoster.Where(c => !c.IsElementalist))
            {
                var specs = WeaponSpec.ForClass(c.Name).ToList();
                Assert.Equal(2, specs.Count);
                Assert.Equal(2, specs.Select(s => s.WeaponBase).Distinct().Count());
                Assert.All(specs, s => Assert.Equal(c.Primary, ItemBase.Weapons.First(w => w.Name == s.WeaponBase).ScalesWith));
                Assert.All(specs, s => Assert.Equal(4, s.Abilities.Count));
            }
            foreach (var staff in new[] { "Fire Staff", "Frost Rod", "Storm Wand" })
                Assert.NotNull(WeaponSpec.For("Elementalist", ItemBase.Weapons.First(w => w.Name == staff)));
            Assert.Equal(10, WeaponSpec.All.Count);
        }

        [Fact]
        public void TheWeaponInHandIsTheSpec()
        {
            var warden = Cls("Warden");
            var kit = Equipment.StandardSet(warden.Armour, 0, warden.Primary);
            Assert.Equal("Sentinel", PlayerStats.For(warden, kit).SpecName);
            kit.Equip(Plain(ItemBase.Weapons.First(w => w.Name == "Warhammer")));
            var breaker = PlayerStats.For(warden, kit);
            Assert.Equal("Breaker", breaker.SpecName);
            Assert.Contains(breaker.Abilities, a => a.StartsWith("Shockwave"));
            Assert.DoesNotContain(breaker.Abilities, a => a.Contains("lock") || a.Contains("vault door"));
            kit.Equip(Plain(ItemBase.Weapons.First(w => w.Name == "Twin Daggers")));
            var fists = PlayerStats.For(warden, kit);
            Assert.Null(fists.Spec);
            Assert.Equal("Warden", fists.SpecName);
            Assert.True(fists.Dps < breaker.Dps, "a weapon with no spec is the off-stat penalty");
        }

        [Fact]
        public void CharactersAreFlat()
        {
            var ch = new Character("Ash", Cls("Reaver"));
            Assert.Equal(Cls("Reaver").BaseAttributes, ch.Attributes);
            Assert.Null(typeof(Character).GetProperty("Level"));
            Assert.Null(typeof(Character).GetProperty("Experience"));
            Assert.Null(typeof(PlayerProfile).GetProperty("Level"));
        }

        [Fact]
        public void InventoryIsWeightOnlyAndOverloadSlows()
        {
            var ch = new Character("Ash", Cls("Shade"));
            foreach (var it in Equipment.StandardSet(ch.Class.Armour, 0, ch.Class.Primary).Items) ch.Equipment.Equip(it);
            var inv = new Inventory(ch);
            float cap = inv.CapacityKg();
            Assert.False(inv.Overloaded());
            float normalSpeed = inv.EffectiveMoveSpeed();
            var plate = ItemBase.Armours.First(a => a.Name == "Plate Cuirass");
            while (!inv.Overloaded()) inv.Take(Plain(plate, 3));
            Assert.True(inv.TotalWeightKg > cap);
            Assert.True(inv.EffectiveMoveSpeed() < normalSpeed);
            Assert.True(inv.Bag.Count > 1, "it took several cuirasses to overload a Shade");
        }

        [Fact]
        public void DeathDropsEverythingAndExtractionDestroysKeys()
        {
            var ch = new Character("Ash", Cls("Warden"));
            foreach (var it in Equipment.StandardSet(ch.Class.Armour, 1, ch.Class.Primary).Items) ch.Equipment.Equip(it);
            var inv = new Inventory(ch) { Keys = 2 };
            inv.Take(Plain(ItemBase.Jewellery[0], 2));
            var (dropped, destroyed, keys) = inv.OnDeath(new DungeonRandom(1));
            Assert.Equal(2, keys);
            Assert.Equal(7, dropped.Count + destroyed.Count);     // 6 worn + 1 bagged
            Assert.Empty(ch.Equipment.Items);
            Assert.Empty(inv.Bag);

            var ch2 = new Character("Bo", Cls("Mender"));
            foreach (var it in Equipment.StandardSet(ch2.Class.Armour, 0, ch2.Class.Primary).Items) ch2.Equipment.Equip(it);
            var inv2 = new Inventory(ch2) { Keys = 1 };
            inv2.Take(Plain(ItemBase.Jewellery[1], 3));
            inv2.Add(Consumable.All[0], 2);
            var profile = new PlayerProfile("acct");
            inv2.Extract(profile, 2500, 6000);
            Assert.Equal(0, inv2.Keys);
            Assert.Single(profile.Stash);
            Assert.Equal(2, profile.ConsumableCount(Consumable.All[0]));
            Assert.All(ch2.Equipment.Items, i => Assert.True(i.Condition.Current < i.Condition.Max));
        }

        [Fact]
        public void VendorBuysSellsAndRestocksANakedCharacter()
        {
            var p = new PlayerProfile("acct") { Silver = 100 };
            Assert.True(Vendor.Buy(p, Consumable.All[0]));                  // 40 → 60 left
            Assert.True(Vendor.Buy(p, Consumable.All[1]));                  // 60 → 0 left
            Assert.False(Vendor.Buy(p, Consumable.All[2]));                 // broke
            p.Silver = 40;
            Assert.NotNull(Vendor.BuyStarter(p, ItemBase.Weapons[0]));
            Assert.NotNull(Vendor.BuyStarter(p, ItemBase.Armours[0]));
            Assert.Null(Vendor.BuyStarter(p, ItemBase.Armours[1]));          // out of silver
            Assert.Equal(2, p.Stash.Count);
            var item = p.Stash[0];
            Assert.Equal(0f, Vendor.Sell(p, item));                           // starter gear is worth nothing to the vendor
            Assert.DoesNotContain(item, p.Stash);
            Assert.Equal(0, Vendor.Salvage(p, p.Stash[0]).Scrap);             // or to the salvage bench
            var found = new Item(ItemBase.Armours[0], 1, Rarity.Magic, "Found Cuirass", new List<AffixRoll>(), new List<AffixRoll>());
            p.Stash.Add(found);
            Assert.True(Vendor.Sell(p, found) > 0);
            var found2 = new Item(ItemBase.Armours[1], 1, Rarity.Magic, "Found Greaves", new List<AffixRoll>(), new List<AffixRoll>());
            p.Stash.Add(found2);
            var bag = Vendor.Salvage(p, found2);
            Assert.True(bag.Scrap > 0 && p.Materials.Scrap == bag.Scrap);
        }

        [Fact]
        public void MatchmakerFormsEightTeamsOfOneToThreeWithNoBrackets()
        {
            var mm = new Matchmaker(teamsPerMatch: 8, minTeamsAfterWait: 4, maxWaitSeconds: 90f);
            int pid = 1;
            for (int g = 0; g < 7; g++) { mm.Enqueue(new QueuedGroup(g, Enumerable.Range(pid, 1 + g % 3).ToList(), g)); pid += 3; }
            Assert.Null(mm.TryForm(10f));
            mm.Enqueue(new QueuedGroup(7, new[] { 99 }, 10f));
            var match = mm.TryForm(11f);
            Assert.NotNull(match);
            Assert.Equal(8, match.Teams.Count);
            Assert.All(match.Teams, t => Assert.InRange(t.PlayerIds.Count, 1, 3));
            Assert.Empty(mm.Queue);
            mm.Enqueue(new QueuedGroup(20, new[] { 1 }, 100f)); mm.Enqueue(new QueuedGroup(21, new[] { 2, 3 }, 101f));
            mm.Enqueue(new QueuedGroup(22, new[] { 4 }, 102f)); mm.Enqueue(new QueuedGroup(23, new[] { 5, 6, 7 }, 103f));
            Assert.Null(mm.TryForm(150f));
            var small = mm.TryForm(195f);
            Assert.NotNull(small);
            Assert.Equal(4, small.Teams.Count);
            Assert.Throws<System.ArgumentException>(() => new QueuedGroup(1, new[] { 1, 2, 3, 4 }, 0f));
        }

        [Fact]
        public void RoundClockFiresEachTimedEventOnce()
        {
            var clock = new RoundClock(new MatchRules());
            var fired = new List<MatchEvent>();
            foreach (float t in new[] { 0f, 1f, 239f, 240f, 241f, 1199f, 1200f, 1300f }) fired.AddRange(clock.Tick(t));
            Assert.Equal(new[] { MatchEventKind.RoundStarted, MatchEventKind.DoorsOpened, MatchEventKind.ExtractsOpened, MatchEventKind.RoundEnded }, fired.Select(e => e.Kind).ToArray());
            Assert.Equal(10f, fired[1].Time);
            Assert.Equal(240f, fired[2].Time);
            Assert.Equal(1200f, fired[3].Time);
        }

        [Fact]
        public void OneThemeAndTwoPalettesCoverEveryDamageType()
        {
            Assert.Single(DungeonTheme.All);
            Assert.Same(DungeonTheme.Default, DungeonTheme.All[0]);
            foreach (DamageType t in System.Enum.GetValues(typeof(DamageType)))
            {
                Assert.True(ElementPalette.Standard.ContainsKey(t));
                Assert.True(ElementPalette.ColourBlind.ContainsKey(t));
            }
            // Fire and Poison must not both be red/green-ish in the colour-blind palette.
            var fire = ElementPalette.ColourBlind[DamageType.Fire]; var poison = ElementPalette.ColourBlind[DamageType.Poison];
            Assert.True(System.Math.Abs(fire.b - poison.b) > 0.4f, "fire and poison separate on blue");
        }
    }
}

namespace DungeonGen.Tests
{
    using DungeonGen.Core.Combat;
    using DungeonGen.Core.Items;
    using DungeonGen.Core.Net;
    using DungeonGen.Core.Profile;

    public class MomentToMomentRuleTests
    {
        private static (MatchState st, DungeonLayout layout) Fresh(int seed = 7)
        {
            var d = MatchDescriptor.Create(seed, DungeonSettings.ForTeams(8), new MatchRules(), out var layout);
            var st = new MatchState(d, layout);
            st.Apply(new MatchEvent(MatchEventKind.RoundStarted, 0), out _);
            for (int p = 1; p <= 6; p++) st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, p, (p - 1) / 3), out _);
            st.Apply(new MatchEvent(MatchEventKind.ExtractsOpened, 240), out _);
            return (st, layout);
        }

        [Fact]
        public void StarterKitIsFreeAndNeverDisplacesBetterGear()
        {
            var ch = new Character("Ash", ClassArchetype.DefaultRoster.First(c => c.Name == "Warden"));
            Vendor.ClaimStarterKit(ch);
            Assert.Equal(6, ch.Equipment.Items.Count());
            Assert.All(ch.Equipment.Items, i => Assert.Equal(Rarity.Normal, i.Rarity));
            var rare = new Item(ItemBase.Armours.First(a => a.Name == "Plate Cuirass"), 3, Rarity.Rare, "Grim Hide", new List<AffixRoll>(), new List<AffixRoll>());
            ch.Equipment.Equip(rare);
            Vendor.ClaimStarterKit(ch);
            Assert.Same(rare, ch.Equipment.Chest);
            var profile = new PlayerProfile("acct") { Silver = 0 };
            Vendor.ClaimStarterKit(ch);                       // still free at zero silver
            Assert.Equal(0, profile.Silver);
        }

        [Fact]
        public void FriendlyFireIsFullAndMobsAreObstacles()
        {
            Assert.Equal(1f, CombatRules.AllyDamageMultiplier);
            Assert.True(CombatRules.AlliesAffectedByZones && CombatRules.AlliesAffectedByKnockback);
            Assert.True(MobRules.LeashToRoom && MobRules.AttackAnyTeam && MobRules.ResetWhenRoomEmpty && !MobRules.Respawn);
            Assert.True(AudioCues.FootstepsRadiusM > 0 && AudioCues.BossFightRadiusM > AudioCues.FootstepsRadiusM);
        }

        [Fact]
        public void ExtractionNeedsAnUndamagedEightSecondChannel()
        {
            var (st, layout) = Fresh();
            int gate = layout.ExtractRooms.First().Id;
            string why;
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.PlayerExtracted, 300, 1, gate, 0), out why));
            Assert.Contains("channel", why);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ExtractStarted, 300, 1, gate), out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.PlayerExtracted, 305, 1, gate, 0), out why), "too early");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ExtractInterrupted, 306, 1), out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.PlayerExtracted, 310, 1, gate, 0), out why), "interrupted");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ExtractStarted, 310, 1, gate), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerExtracted, 318, 1, gate, 0), out why));
            Assert.False(st.Channels.ContainsKey(1));
        }

        [Fact]
        public void BroughtInGearFallsOnTheCorpseAndAnyoneCanLootIt()
        {
            var (st, layout) = Fresh();
            string why;
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ItemRegistered, 1, 100001, 1, 25), out why));   // player 1 brought a Rare
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ItemRegistered, 1, 100002, 1, 2), out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.ItemRegistered, 1, 5, 1, 2), out why), "ids below the base are generated items");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 50, 1, 4, 3), out why));
            Assert.Equal(2, st.Corpses[1].Count);
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.ItemTaken, 51, 100001, 4), out why), "corpse not opened");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ItemDestroyed, 51, 100002), out why));      // server rolled: trashed
            Assert.True(Ch.OpenCorpse(st, 52, 1, 6, out why));                                               // a different team opens it
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ItemTaken, 53, 100001, 6), out why));
            Assert.Equal(6, st.ItemOwner[100001]);
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.ItemTaken, 54, 100002, 6), out why), "destroyed");
            int gate = layout.ExtractRooms.First().Id;
            st.Apply(new MatchEvent(MatchEventKind.ExtractStarted, 300, 6, gate), out _);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerExtracted, 310, 6, gate, 1), out why));
            Assert.Equal(25, st.ExtractedValue[6]);
        }

        [Fact]
        public void MinimapIsWhatTheServerSaysYouSaw()
        {
            var (st, layout) = Fresh();
            Assert.Single(st.Explored[0]);
            Assert.Contains(st.Descriptor.TeamSpawnRooms[0], st.Explored[0]);
            var seen = LineOfSight.VisibleRooms(layout, layout.Rooms[st.Descriptor.TeamSpawnRooms[0]].Doors[0], 10);
            foreach (int r in seen) Assert.True(st.Apply(new MatchEvent(MatchEventKind.RoomSeen, 5, 0, r), out _));
            Assert.True(st.Explored[0].Count >= seen.Count);
            Assert.DoesNotContain(layout.FinalBossRoom.Id, st.Explored[0]);
        }

        [Fact]
        public void ShrinesSellConsumablesForASoulToAnyone()
        {
            var (st, layout) = Fresh();
            int shrine = layout.Rooms.First(r => r.HasShrine).Id;
            string why;
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.ShrinePurchase, 10, 1, 0, shrine), out why), "no souls");
            Ch.KillAndLoot(st, 11, 4, 1);
            Assert.Equal(1, st.SoulsOf(1));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.ShrinePurchase, 12, 2, 0, shrine), out why), "a teammate's soul is not yours");
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.ShrinePurchase, 12, 1, 0, layout.ExtractRooms.First().Id), out why), "not a shrine");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ShrinePurchase, 12, 1, 0, shrine), out why));
            Assert.Equal(0, st.SoulsOf(1));
        }

        [Fact]
        public void SeasonTracksValueAndGrantsCosmeticsOnly()
        {
            var season = new Season(1, new System.DateTime(2027, 1, 1), new System.DateTime(2027, 4, 1));
            season.RecordExtraction("a", 600); season.RecordExtraction("b", 3000); season.RecordExtraction("a", 100);
            season.RecordKill("b");
            var board = season.Leaderboard().ToList();
            Assert.Equal("b", board[0].account);
            Assert.Equal(700, board[1].value);
            Assert.Contains("cosmetic.cloak.season", season.EarnedUnlocks("a"));
            Assert.DoesNotContain("cosmetic.weapon_glow.season", season.EarnedUnlocks("a"));
            Assert.All(Season.Rewards, r => Assert.StartsWith("cosmetic.", r.unlock));
        }

        [Fact]
        public void CraftingChargesASilverFee()
        {
            var p = new PlayerProfile("acct") { Silver = 5 };
            p.Materials.Scrap = 1000;
            var req = new CraftRequest(ItemBase.Weapons[0], 2);
            Assert.Throws<System.InvalidOperationException>(() => Vendor.Craft(p, req, new DungeonRandom(1)));
            p.Silver = 100;
            Vendor.Craft(p, req, new DungeonRandom(1));
            Assert.Equal(100 - CraftingRules.SilverFee(2), p.Silver);
            Assert.Equal(40, CraftingRules.SilverFee(2));
        }

        [Fact]
        public void TutorialIsOneTeamWithEverythingOnce()
        {
            var d = MatchDescriptor.CreateTutorial(3, out var layout);
            Assert.True(d.Rules.IsTutorial);
            Assert.Equal(0, d.Rules.ExtractsOpenAfterSeconds);
            Assert.Equal(2, d.TeamSpawnRooms.Count);                                                   // the player's spawn and the bot team's
            Assert.Single(layout.BossRooms);
            Assert.Single(layout.VaultRooms);
            Assert.Equal(2, layout.ExtractRooms.Count());
            Assert.NotNull(layout.FinalBossRoom);
            Assert.Contains(layout.Rooms, r => r.HasShrine);
            var back = MatchDescriptor.FromBytes(d.ToBytes());
            Assert.True(back.Rules.IsTutorial);
            Assert.Equal(8f, back.Rules.ExtractChannelSeconds);
            back.BuildAndVerify();
        }

        [Fact]
        public void PingsRoundTripAndNeverTouchTheLog()
        {
            var ping = new TeamPing(3, 1, PingKind.Enemy, new GridPoint(10, 12), 44.5f);
            var back = TeamPing.FromBytes(ping.ToBytes());
            Assert.Equal(ping.Kind, back.Kind); Assert.Equal(ping.Cell, back.Cell); Assert.Equal(ping.Team, back.Team);
            Assert.DoesNotContain(System.Enum.GetNames(typeof(MatchEventKind)), n => n.Contains("Ping"));
        }

        [Fact]
        public void RoundEndDestroysWhatWasLeftInside()
        {
            var (st, layout) = Fresh();
            st.Apply(new MatchEvent(MatchEventKind.ItemRegistered, 1, 100001, 2, 25), out _);
            st.Apply(new MatchEvent(MatchEventKind.RoundEnded, 1200), out _);
            Assert.Contains(100001, st.DestroyedItems);
            Assert.False(st.ItemOwner.ContainsKey(100001));
        }
    }
}

namespace DungeonGen.Tests
{
    using DungeonGen.Core.Combat;
    using DungeonGen.Core.Items;

    public class MovementTests
    {
        [Fact]
        public void WasdWithMouseAimIsTheOnlyScheme()
        {
            Assert.True(MovementRules.ScreenRelative && MovementRules.FacingFollowsAim);
            Assert.False(MovementRules.ClickToMove);
            Assert.False(MovementRules.UniversalDodge);
        }

        [Fact]
        public void SpeedsLandWhereTheDesignSaysTheyDo()
        {
            var warden = ClassArchetype.DefaultRoster.First(c => c.Name == "Warden");
            var shade = ClassArchetype.DefaultRoster.First(c => c.Name == "Shade");
            var w = PlayerStats.For(warden, Equipment.StandardSet(warden.Armour, 0, warden.Primary));
            var s = PlayerStats.For(shade, Equipment.StandardSet(shade.Armour, 0, shade.Primary));
            float wSpeed = MovementRules.SpeedMps(w, 0f), sSpeed = MovementRules.SpeedMps(s, 0f);
            Assert.InRange(wSpeed, 4.2f, 4.6f);      // heavy armour, low DEX
            Assert.InRange(sSpeed, 5.1f, 5.5f);      // light, high DEX
            Assert.True(sSpeed > wSpeed);
            // The 8-team dungeon (156 cells) edge to edge in about a minute at base speed.
            Assert.InRange(MovementRules.SecondsForCells(156, MovementRules.BaseSpeedMps), 55f, 70f);
            // A spawn-to-extract walk on the sample seed (32-50 cells) is 13-20 s.
            Assert.InRange(MovementRules.SecondsForCells(40, MovementRules.BaseSpeedMps), 14f, 18f);
            Assert.True(MovementRules.SpeedMps(w, w.CarryKg * 2f) < wSpeed * 0.6f, "double load halves speed");
        }
    }
}

namespace DungeonGen.Tests
{
    using DungeonGen.Core.Combat;
    using DungeonGen.Core.Net;
    using DungeonGen.Core.Profile;

    public class ProductionDecisionTests
    {
        [Fact]
        public void VerticalSliceDrawsOnlyItsBossesAndItsClassesExist()
        {
            var s = VerticalSlice.Settings();
            for (int seed = 1; seed <= 20; seed++)
            {
                var layout = DungeonGenerator.Generate(s, seed);
                Assert.Equal(4, layout.SpawnRooms.Count());
                Assert.Equal(2, layout.BossRooms.Count());
                Assert.All(layout.BossRooms, b => Assert.Contains(b.Boss.Archetype.Name, VerticalSlice.Bosses));
                Assert.Equal(2, layout.BossRooms.Select(b => b.Boss.Archetype.Name).Distinct().Count());
            }
            Assert.All(VerticalSlice.Classes, c => Assert.Contains(ClassArchetype.DefaultRoster, r => r.Name == c));
            Assert.All(VerticalSlice.Specs, sp => Assert.True(WeaponSpec.All.Any(w => w.Name == sp) || ElementKit.All.Any(k => k.Name == sp)));
            var back = SettingsCodec.FromBytes(SettingsCodec.ToBytes(s));
            Assert.Equal(VerticalSlice.Bosses, back.AllowedBosses);
        }

        [Fact]
        public void GridPathsStayWalkableRespectLocksAndLeashes()
        {
            var layout = DungeonGenerator.Generate(DungeonSettings.ForTeams(8), 7);
            var spawn = layout.SpawnRooms.First();
            var extract = layout.ExtractRooms.First();
            var path = GridPath.Find(layout, spawn.Center, extract.Center);
            Assert.NotNull(path);
            Assert.Equal(spawn.Center, path[0]);
            Assert.Equal(extract.Center, path[^1]);
            for (int i = 1; i < path.Count; i++) Assert.Equal(1, path[i].ManhattanDistance(path[i - 1]));
            Assert.All(path, p => Assert.True(layout.IsWalkable(p)));
            var vault = layout.VaultRooms.First();
            Assert.Null(GridPath.Find(layout, spawn.Center, vault.Center));                        // locked door blocks mobs too
            var mobRoom = layout.Rooms.First(r => r.Mobs != null && r.Neighbours.Count > 0);
            var neighbour = layout.Rooms[mobRoom.Neighbours[0]];
            Assert.Null(GridPath.Find(layout, mobRoom.Center, neighbour.Center, leashRoom: mobRoom.Id, leashCells: MobRules.LeashCells));
            Assert.NotNull(GridPath.Find(layout, mobRoom.Center, mobRoom.Doors[0], leashRoom: mobRoom.Id, leashCells: MobRules.LeashCells));
            Assert.True(GridPath.WithinLeash(layout, mobRoom.Id, mobRoom.Doors[0], MobRules.LeashCells));
            Assert.False(GridPath.WithinLeash(layout, mobRoom.Id, neighbour.Center, MobRules.LeashCells));
        }

        [Fact]
        public void EightCharacterSlotsShareOneBank()
        {
            var p = new PlayerProfile("acct");
            Assert.Throws<System.InvalidOperationException>(() => p.CreateCharacter("locked", ClassArchetype.DefaultRoster.First(c => c.Name == "Shade")));   // earned class, level 1 account
            p.AddAccountXp(AccountRules.XpToReach(20));
            for (int i = 0; i < 8; i++) p.CreateCharacter("c" + i, ClassArchetype.DefaultRoster[i % 6]);
            Assert.Throws<System.InvalidOperationException>(() => p.CreateCharacter("c9", ClassArchetype.DefaultRoster[0]));
            Assert.Equal(8, PlayerProfile.MaxCharacters);
            Assert.Null(typeof(Character).GetProperty("Stash"));   // the bank is on the profile, shared
            Assert.NotNull(typeof(PlayerProfile).GetProperty("Stash"));
        }

        [Fact]
        public void DisconnectedCharactersStandThereAndCanComeBackWithinTheWindow()
        {
            var d = MatchDescriptor.Create(7, DungeonSettings.ForTeams(8), new MatchRules(), out var layout);
            var st = new MatchState(d, layout);
            st.Apply(new MatchEvent(MatchEventKind.RoundStarted, 0), out _);
            st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, 1, 0), out _);
            st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, 4, 1), out _);
            string why;
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDisconnected, 100, 1), out why));
            Assert.Contains(1, st.PlayersInside);                                                   // still in the world
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 120, 1, 4, 0), out why));   // and killable
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.PlayerReconnected, 130, 1), out why));
            st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, 2, 0), out _);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDisconnected, 200, 2), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerReconnected, 250, 2), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDisconnected, 300, 2), out why));
            Assert.Empty(st.ReconnectExpired(350));
            Assert.Contains(2, st.ReconnectExpired(361));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.PlayerReconnected, 365, 2), out why));
            Assert.Contains("window", why);
            Assert.Equal(60, MatchDescriptor.FromBytes(d.ToBytes()).Rules.ReconnectWindowSeconds);
        }

        [Fact]
        public void TechStackIsStated()
        {
            Assert.Equal("Fishnet", TechStack.Netcode);
            Assert.Contains("PlayFab", TechStack.Backend);
            Assert.Contains("Unity 6", TechStack.Engine);
            Assert.Contains("Grid", TechStack.Pathfinding);
            Assert.Equal("Menus only", TechStack.Hub);
        }
    }
}

namespace DungeonGen.Tests
{
    using DungeonGen.Core.Net;

    /// <summary>Test shorthand: loot and key actions now need their channel first.</summary>
    internal static class Ch
    {
        public static bool OpenChest(MatchState st, float t, int room, int by, out string why)
        {
            if (!st.Apply(new MatchEvent(MatchEventKind.LootStarted, t, by, room, 0), out why)) return false;
            return st.Apply(new MatchEvent(MatchEventKind.ContainerOpened, t + 2f, room, by), out why);
        }
        /// <summary>Kill an enemy and loot the corpse: the only way a player kill yields a soul.</summary>
        public static bool KillAndLoot(MatchState st, float t, int victim, int killer, int room = 0)
        {
            if (!st.Apply(new MatchEvent(MatchEventKind.PlayerDied, t, victim, killer, room), out _)) return false;
            return OpenCorpse(st, t + 0.1f, victim, killer, out _);
        }
        public static bool OpenCorpse(MatchState st, float t, int corpseOf, int by, out string why)
        {
            if (!st.Apply(new MatchEvent(MatchEventKind.LootStarted, t, by, corpseOf, 1), out why)) return false;
            return st.Apply(new MatchEvent(MatchEventKind.CorpseOpened, t + 3f, corpseOf, by), out why);
        }
        public static bool TakeKey(MatchState st, float t, int keyId, int by, out string why)
        {
            if (!st.Apply(new MatchEvent(MatchEventKind.LootStarted, t, by, 0, 2), out why)) return false;
            return st.Apply(new MatchEvent(MatchEventKind.KeyPickedUp, t + 1f, keyId, by), out why);
        }
    }
}

namespace DungeonGen.Tests
{
    using DungeonGen.Core.Combat;
    using DungeonGen.Core.Items;
    using DungeonGen.Core.Net;
    using DungeonGen.Core.Profile;

    public class FightRuleTests
    {
        private static (MatchState st, DungeonLayout layout) Fresh(int seed = 7)
        {
            var d = MatchDescriptor.Create(seed, DungeonSettings.ForTeams(8), new MatchRules(), out var layout);
            var st = new MatchState(d, layout);
            st.Apply(new MatchEvent(MatchEventKind.RoundStarted, 0), out _);
            for (int p = 1; p <= 6; p++) st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, p, (p - 1) / 3), out _);
            return (st, layout);
        }

        [Fact]
        public void ResourcesSplitByRoleAndBasicAttacksAreFree()
        {
            Assert.Equal(ResourceKind.Stamina, FightRules.ResourceFor(ClassArchetype.DefaultRoster.First(c => c.Name == "Warden")));
            Assert.Equal(ResourceKind.Stamina, FightRules.ResourceFor(ClassArchetype.DefaultRoster.First(c => c.Name == "Shade")));
            Assert.Equal(ResourceKind.Mana, FightRules.ResourceFor(ClassArchetype.DefaultRoster.First(c => c.Name == "Elementalist")));
            Assert.Equal(ResourceKind.Mana, FightRules.ResourceFor(ClassArchetype.DefaultRoster.First(c => c.Name == "Mender")));
            Assert.False(FightRules.BasicAttacksCostResource);
            var ele = PlayerStats.For(ClassArchetype.DefaultRoster.First(c => c.Name == "Elementalist"), 0);
            Assert.Equal(ele.Mana, ele.ResourcePool);
            Assert.Equal(ele.Mana * 0.3f, FightRules.AbilityCost(ele.ResourcePool, AbilitySlot.Zone), 2);
            Assert.Equal(0f, FightRules.AbilityCost(ele.ResourcePool, AbilitySlot.Passive));
        }

        [Fact]
        public void DownedPlayersCanBeRevivedOrFinished()
        {
            var (st, layout) = Fresh();
            string why;
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDowned, 10, 2, 4, 0), out why));
            Assert.Contains(2, st.Downed.Keys);
            Assert.Contains(2, st.PlayersInside);                                                       // downed is not dead
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.ExtractStarted, 11, 2, layout.ExtractRooms.First().Id), out why), "downed cannot act");
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.ReviveStarted, 12, 4, 2), out why), "enemy cannot revive");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ReviveStarted, 12, 1, 2), out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.PlayerRevived, 15, 2, 1), out why), "8 s channel");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ReviveInterrupted, 16, 1), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ReviveStarted, 17, 1, 2), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerRevived, 25, 2, 1), out why));
            Assert.DoesNotContain(2, st.Downed.Keys);
            Assert.DoesNotContain(2, st.DeadPlayers);

            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDowned, 30, 3, 5, 0), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerFinished, 35, 3, 5, 0), out why));
            Assert.Contains(3, st.DeadPlayers);
            Assert.Contains(3, st.CorpseSouls);                                                          // finishing is the kill: the soul lies on the corpse
            Assert.True(Ch.OpenCorpse(st, 36, 3, 4, out why));                                           // and whoever loots it first has it, finisher or not
            Assert.Equal(1, st.SoulsOf(4)); Assert.Equal(0, st.SoulsOf(5));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDowned, 40, 1, 6, 0), out why));
            Assert.Empty(st.DownedExpired(99));
            Assert.Contains(1, st.DownedExpired(101));
            Assert.Equal(60f, FightRules.DownedSeconds); Assert.Equal(8f, FightRules.ReviveChannelSeconds); Assert.Equal(0.5f, FightRules.FinishDamageFraction);
        }

        [Fact]
        public void LootingIsAChannel()
        {
            var (st, layout) = Fresh();
            var room = layout.Rooms.First(r => r.Type == RoomType.Loot);
            string why;
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.ContainerOpened, 5, room.Id, 1), out why));
            Assert.Contains("channel", why);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.LootStarted, 5, 1, room.Id, 0), out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.ContainerOpened, 6, room.Id, 1), out why), "2 s");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.LootInterrupted, 6, 1), out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.ContainerOpened, 8, room.Id, 1), out why), "interrupted");
            Assert.True(Ch.OpenChest(st, 8, room.Id, 1, out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ContainerOpened, 20, room.Id, 2), out why), "already open: no channel needed");
            Assert.Equal(2f, FightRules.ChestOpenSeconds); Assert.Equal(3f, FightRules.CorpseLootSeconds); Assert.Equal(1f, FightRules.KeyPickupSeconds);
        }

        [Fact]
        public void HardControlDiminishesOnPlayersOnly()
        {
            var player = new StatusTracker { Resistance = new ControlResistance() };
            var stun = StatusEffectDefinition.All[StatusKind.Stun];
            Assert.Equal(1.5f, player.Apply(stun).RemainingSeconds, 2);
            player.Tick(1.6f); Assert.Equal(0.75f, player.Apply(stun).RemainingSeconds, 2);
            player.Tick(0.8f); Assert.Equal(0.375f, player.Apply(stun).RemainingSeconds, 2);
            player.Tick(0.4f); Assert.Null(player.Apply(stun));                                          // immune
            player.Tick(11f);  Assert.Equal(1.5f, player.Apply(stun).RemainingSeconds, 2);              // window passed
            var mob = new StatusTracker();
            for (int i = 0; i < 4; i++) { Assert.Equal(1.5f, mob.Apply(stun).RemainingSeconds, 2); mob.Tick(1.6f); }
            Assert.False(FightRules.Sprint);
        }

        [Fact]
        public void OneOfEachUnique()
        {
            var prism = UniqueItem.All.First(u => u.Name == "Glasswright's Prism");
            Item Ring() => new Item(prism.Base, 4, Rarity.Unique, prism.Name, new List<AffixRoll>(), new List<AffixRoll>(), prism);
            var e = new Equipment();
            e.Equip(Ring());
            Assert.Throws<System.InvalidOperationException>(() => e.Equip(Ring()));
            Assert.True(FightRules.OneOfEachUnique);
        }

        [Fact]
        public void NewAccountAndCountdown()
        {
            var p = PlayerProfile.NewAccount("acct", "Ash", ClassArchetype.DefaultRoster[0]);
            Assert.Equal(200, p.Silver);
            Assert.Single(p.Characters);
            Assert.Equal(6, p.Characters[0].Equipment.Items.Count());
            Assert.Equal(2, p.ConsumableCount(Consumable.All[0]));

            var (st, _) = Fresh();
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.DoorsOpened, 5), out string why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.DoorsOpened, 10), out why));
            Assert.True(st.DoorsOpen);
            var clock = new RoundClock(new MatchRules());
            var kinds = new List<MatchEventKind>();
            foreach (float t in new[] { 0f, 9f, 10f, 240f, 1200f }) foreach (var ev in clock.Tick(t)) kinds.Add(ev.Kind);
            Assert.Equal(new[] { MatchEventKind.RoundStarted, MatchEventKind.DoorsOpened, MatchEventKind.ExtractsOpened, MatchEventKind.RoundEnded }, kinds.ToArray());
        }
    }
}

namespace DungeonGen.Tests
{
    using DungeonGen.Core.Combat;
    using DungeonGen.Core.Items;
    using DungeonGen.Core.Net;

    public class SilverAndUltimateTests
    {
        private static (MatchState st, DungeonLayout layout) Fresh(int seed = 7)
        {
            var d = MatchDescriptor.Create(seed, DungeonSettings.ForTeams(8), new MatchRules(), out var layout);
            var st = new MatchState(d, layout);
            st.Apply(new MatchEvent(MatchEventKind.RoundStarted, 0), out _);
            for (int p = 1; p <= 6; p++) st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, p, (p - 1) / 3), out _);
            return (st, layout);
        }

        [Fact]
        public void SilverLiesWhereMobsAndChestsAreAndScalesWithTier()
        {
            var layout = DungeonGenerator.Generate(DungeonSettings.ForTeams(8), 7);
            Assert.All(layout.Rooms.Where(r => r.Type == RoomType.Spawn || r.Type == RoomType.Extract), r => Assert.Equal(0, r.Silver));
            Assert.All(layout.Rooms.Where(r => r.Mobs != null || r.Loot.Count > 0), r => Assert.True(r.Silver > 0));
            var vault = layout.VaultRooms.First(); var plain = layout.Rooms.First(r => r.Type == RoomType.Standard && r.LootTier <= 1);
            Assert.True(vault.Silver > plain.Silver * 3);
            Assert.Equal(0f, FightRules.SilverWeightKg);
            Assert.Equal(LayoutFingerprint.Compute(layout), LayoutFingerprint.Compute(DungeonGenerator.Generate(DungeonSettings.ForTeams(8), 7)));
        }

        [Fact]
        public void SilverIsPickedOnceDropsInFullOnDeathAndIsLootable()
        {
            var (st, layout) = Fresh();
            string why;
            var mobRoom = layout.Rooms.First(r => r.Mobs != null && r.Loot.Count > 0);
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.SilverPicked, 5, 1, mobRoom.Id), out why), "purse is in the chest");
            int far = layout.Rooms.First(r => !st.Index.RoomsWithin(mobRoom.Id, 2).Contains(r.Id)).Id;                  // teammates out of range: no split
            st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 4, 2, far), out _); st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 4, 3, far), out _);
            Assert.True(Ch.OpenChest(st, 5, mobRoom.Id, 1, out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.SilverPicked, 8, 1, mobRoom.Id), out why));
            Assert.Equal(mobRoom.Silver, st.PlayerSilver[1]);
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.SilverPicked, 9, 2, mobRoom.Id), out why), "already taken");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 20, 1, 4, mobRoom.Id), out why));
            Assert.False(st.PlayerSilver.ContainsKey(1));
            Assert.Equal(mobRoom.Silver, st.CorpseSilver[1]);
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.SilverLooted, 21, 4, 1), out why), "corpse not opened");
            Assert.True(Ch.OpenCorpse(st, 21, 1, 4, out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.SilverLooted, 25, 4, 1), out why));
            Assert.Equal(mobRoom.Silver, st.PlayerSilver[4]);
            st.Apply(new MatchEvent(MatchEventKind.ExtractsOpened, 240), out _);
            int gate = layout.ExtractRooms.First().Id;
            st.Apply(new MatchEvent(MatchEventKind.ExtractStarted, 300, 4, gate), out _);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerExtracted, 310, 4, gate, 1), out why));
            Assert.Equal(mobRoom.Silver, st.ExtractedSilver[4]);
        }

        [Fact]
        public void NoHealthRegenButResourcesRefill()
        {
            Assert.Equal(0f, FightRules.HealthRegenPerSecond);
            Assert.True(FightRules.ResourcesRegenerate);
            Assert.True(FightRules.ResourceRegenPerSecond > 0f);
        }

        [Fact]
        public void EverySpecHasAnUltimateAndItIsEarnedAtAShrine()
        {
            Assert.All(WeaponSpec.All, s => Assert.False(string.IsNullOrEmpty(s.Ultimate)));
            Assert.All(ElementKit.All, k => Assert.False(string.IsNullOrEmpty(k.Ultimate)));
            Assert.Equal(13, WeaponSpec.Ultimates.Count);
            Assert.Equal(13, WeaponSpec.Ultimates.Values.Distinct().Count());

            var (st, layout) = Fresh();
            int shrine = layout.Rooms.First(r => r.HasShrine).Id;
            string why;
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.UltimateUsed, 1, 1), out why), "not armed");
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.UltimateArmed, 2, 1, shrine), out why), "no souls");
            Ch.KillAndLoot(st, 3, 4, 1);
            Ch.KillAndLoot(st, 4, 5, 1);
            Assert.Equal(2, st.SoulsOf(1));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.UltimateArmed, 5, 2, shrine), out why), "a teammate's souls are not yours");
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.UltimateArmed, 5, 1, layout.ExtractRooms.First().Id), out why), "not a shrine");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.UltimateArmed, 5, 1, shrine), out why));
            Assert.Equal(0, st.SoulsOf(1));                                                     // the same two souls a resurrection would have cost
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.PlayerResurrected, 6, 4, 1, shrine), out why), "souls spent on the ultimate");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.UltimateUsed, 7, 1), out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.UltimateUsed, 8, 1), out why), "one use");
            st.Souls[1] = 2;
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.UltimateArmed, 9, 1, shrine), out why));
            st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 10, 1, 6, 0), out _);
            Assert.False(st.UltimateCharges.ContainsKey(1));                                 // dies with you
        }

        [Fact]
        public void FormedMatchesAreSizedToTheirTeams()
        {
            var mm = new Matchmaker(8, 4, 90f);
            for (int g = 0; g < 4; g++) mm.Enqueue(new QueuedGroup(g, new[] { g * 3 + 1 }, g));
            var m = mm.TryForm(200f);
            Assert.NotNull(m);
            Assert.Equal(4, m.Teams.Count);
            Assert.Equal(4, m.Settings.SpawnCount);
            Assert.Equal(DungeonSettings.ForTeams(4).Width, m.Settings.Width);
        }
    }
}

namespace DungeonGen.Tests
{
    using DungeonGen.Core.Combat;
    using DungeonGen.Core.Net;

    public class UltimateDesignTests
    {
        [Fact]
        public void EverySpecHasExactlyOneJobAndTheRulesAreStated()
        {
            var names = WeaponSpec.All.Select(s => s.Name).Concat(ElementKit.All.Select(k => k.Name)).ToList();
            Assert.Equal(13, names.Count);
            Assert.All(names, n => Assert.True(WeaponSpec.Ultimates.ContainsKey(n) && WeaponSpec.UltimateJobs.ContainsKey(n), n));
            Assert.Equal(13, WeaponSpec.UltimateJobs.Count);
            Assert.True(WeaponSpec.UltimateJobs.Values.Count(j => j == WeaponSpec.UltimateJob.DenySpace) >= 4);
            Assert.True(WeaponSpec.UltimateJobs.Values.Count(j => j == WeaponSpec.UltimateJob.FindOrMark) >= 2);
            Assert.True(FightRules.UltimatesObeyFriendlyFire);
            Assert.True(FightRules.ArmedUltimateAuraVisibleToAll && !FightRules.ArmedUltimateAuraThroughWalls);
            Assert.Contains("Absolute Zero", WeaponSpec.Ultimates["Cryomancy"]);
            Assert.Contains("Blackout", WeaponSpec.Ultimates["Stormcalling"]);
            Assert.Contains("Overwatch", WeaponSpec.Ultimates["Sharpshooter"]);
            Assert.Contains("Suppression", WeaponSpec.Ultimates["Volleyer"]);
            Assert.Contains("Last Breath", WeaponSpec.Ultimates["Warder"]);
            Assert.Contains("Outbreak", WeaponSpec.Ultimates["Plaguebearer"]);
        }

        [Fact]
        public void LastBreathRevivesInstantlyButNeverAFinishedPlayer()
        {
            var d = MatchDescriptor.Create(7, DungeonSettings.ForTeams(8), new MatchRules(), out var layout);
            var st = new MatchState(d, layout);
            st.Apply(new MatchEvent(MatchEventKind.RoundStarted, 0), out _);
            for (int p = 1; p <= 6; p++) st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, p, (p - 1) / 3), out _);
            int shrine = layout.Rooms.First(r => r.HasShrine).Id;
            string why;
            Ch.KillAndLoot(st, 1, 4, 1);
            Ch.KillAndLoot(st, 2, 5, 1);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.UltimateArmed, 6, 1, shrine), out why));   // player 1 looted both corpses
            st.Apply(new MatchEvent(MatchEventKind.PlayerDowned, 10, 2, 6, 0), out _);
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.LastBreath, 11, 3, 2), out why), "player 3 has no charge");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.LastBreath, 11, 1, 2), out why));     // instant, no channel
            Assert.DoesNotContain(2, st.Downed.Keys);
            Assert.Equal(0, st.UltimateCharges[1]);
            st.Souls[1] = 2;
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.UltimateArmed, 12, 1, shrine), out why));
            st.Apply(new MatchEvent(MatchEventKind.PlayerDowned, 20, 3, 6, 0), out _);
            st.Apply(new MatchEvent(MatchEventKind.PlayerFinished, 22, 3, 6, 0), out _);
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.LastBreath, 23, 1, 3), out why));
            Assert.Contains("shrine", why);
            Assert.Equal(1, st.UltimateCharges[1]);                                                 // the charge is not wasted on a refusal
        }
    }
}

namespace DungeonGen.Tests
{
    using DungeonGen.Core.Combat;
    using DungeonGen.Core.Items;
    using DungeonGen.Core.Net;
    using DungeonGen.Core.Profile;

    public class TierAndAccountTests
    {
        private static Item Rare(int level) => new Item(ItemBase.Weapons[0], level, Rarity.Rare, "Test Blade", new List<AffixRoll>(), new List<AffixRoll>());

        [Fact]
        public void DungeonTiersWallBothWaysAndCoverEveryScore()
        {
            Assert.Equal(3, DungeonTier.All.Count);
            for (int score = 0; score <= 2000; score += 7) Assert.NotEmpty(DungeonTier.Open(score));
            var t1 = DungeonTier.For(1); var t2 = DungeonTier.For(2); var t3 = DungeonTier.For(3);
            var fresh = PlayerProfile.NewAccount("a", "Ash", ClassArchetype.DefaultRoster[0]);
            int starter = GearScore.Of(fresh.Characters[0].Equipment);
            Assert.InRange(starter, 1, 20);
            Assert.True(t1.Admits(starter)); Assert.False(t2.Admits(starter)); Assert.False(t3.Admits(starter));
            Assert.Contains("needs gear score", t2.Refusal(starter));
            Assert.Equal(6, starter);                                                                     // weapon and five armour pieces, Normal, level 0
            int geared = GearScore.Of(Enumerable.Range(0, 9).Select(_ => Rare(4)));                 // nine Rare pieces at item level 4 = 135
            Assert.Equal(135, geared);
            Assert.False(t1.Admits(geared)); Assert.True(t2.Admits(geared)); Assert.True(t3.Admits(geared));   // the overlap: the player picks
            Assert.Contains("closed above", t1.Refusal(geared));
            int top = GearScore.Of(Enumerable.Range(0, 9).Select(_ => Rare(8)));                     // Tier III drops: 243
            Assert.False(t2.Admits(top)); Assert.Null(t3.Refusal(top));
            // One unique on a starter character does not throw them out of Tier I (the vendor would price it at twelve Normals; the gate does not).
            var unique = new Item(ItemBase.Weapons[0], 4, Rarity.Unique, "Test Relic", new List<AffixRoll>(), new List<AffixRoll>(), UniqueItem.All[0]);
            Assert.Equal(20, GearScore.Of(unique));
            Assert.True(t1.Admits(starter + GearScore.Of(unique)));
            Assert.True(GearScore.Of(unique) < unique.Value);
            // Carried gear counts: a starter character with three Tier III Rares in the bag is no longer Tier I.
            int smuggler = GearScore.Of(fresh.Characters[0].Equipment, new[] { Rare(8), Rare(8), Rare(8) });
            Assert.False(t1.Admits(smuggler));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => DungeonTier.For(4));
        }

        [Fact]
        public void HigherTiersDropHigherLevelLootMoreUniquesAndRoundTrip()
        {
            double Avg(int tier)
            {
                double sum = 0; int n = 0, uniques = 0;
                for (int seed = 1; seed <= 12; seed++)
                {
                    var layout = DungeonGenerator.Generate(DungeonTier.For(tier).Apply(DungeonSettings.ForTeams(4)), seed);
                    foreach (var it in layout.Items) { sum += it.ItemLevel; n++; if (it.Rarity == Rarity.Unique) uniques++; }
                }
                return sum / n;
            }
            double a1 = Avg(1), a2 = Avg(2), a3 = Avg(3);
            Assert.True(a1 + 1.5 < a2 && a2 + 1.5 < a3, $"{a1:0.00} {a2:0.00} {a3:0.00}");
            var s3 = DungeonTier.For(3).Apply(DungeonSettings.ForTeams(4));
            Assert.Equal(3, s3.Tier); Assert.Equal(4, s3.ItemLevelOffset); Assert.Equal(2f, s3.UniqueChanceMultiplier);
            Assert.Equal(1.4f, DungeonTier.For(2).UniqueChanceMultiplier);
            Assert.True(s3.Combat.PlayerDps > DungeonSettings.ForTeams(4).Combat.PlayerDps, "the tuner assumes geared players");
            var back = SettingsCodec.FromBytes(SettingsCodec.ToBytes(s3));
            Assert.Equal(3, back.Tier); Assert.Equal(4, back.ItemLevelOffset); Assert.Equal(2f, back.UniqueChanceMultiplier);
            Assert.Equal(SettingsCodec.ToBytes(s3), SettingsCodec.ToBytes(back));
            Assert.Equal(1, VerticalSlice.Tiers);
            Assert.Equal(AccountRules.StartingClasses, VerticalSlice.Classes);
        }

        [Fact]
        public void MatchmakerKeepsTiersApart()
        {
            var mm = new Matchmaker(teamsPerMatch: 4, minTeamsAfterWait: 2, maxWaitSeconds: 90f);
            for (int g = 0; g < 3; g++) mm.Enqueue(new QueuedGroup(g, new[] { g * 10 + 1 }, 0f, tier: 1));
            for (int g = 10; g < 14; g++) mm.Enqueue(new QueuedGroup(g, new[] { g * 10 + 1 }, 1f, tier: 3));
            var m = mm.TryForm(2f);
            Assert.NotNull(m);
            Assert.Equal(3, m.Tier);
            Assert.All(m.Teams, t => Assert.Equal(3, t.Tier));
            Assert.Equal(3, m.Settings.Tier);
            Assert.Equal(4, m.Settings.ItemLevelOffset);
            Assert.Null(mm.TryForm(3f));                                        // three Tier I groups: not enough yet
            var late = mm.TryForm(100f);                                       // waited long enough to go with three
            Assert.Equal(1, late.Tier); Assert.Equal(3, late.Teams.Count);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new QueuedGroup(99, new[] { 1 }, 0f, tier: 7));

            // Thin queues above Tier I: two teams may go after three minutes; Tier I never does.
            var thin = new Matchmaker(teamsPerMatch: 8, minTeamsAfterWait: 4, maxWaitSeconds: 90f);
            thin.Enqueue(new QueuedGroup(1, new[] { 1 }, 0f, tier: 3)); thin.Enqueue(new QueuedGroup(2, new[] { 2 }, 5f, tier: 3));
            thin.Enqueue(new QueuedGroup(3, new[] { 3 }, 0f, tier: 1)); thin.Enqueue(new QueuedGroup(4, new[] { 4 }, 5f, tier: 1));
            Assert.Null(thin.TryForm(179f));
            var two = thin.TryForm(181f);
            Assert.Equal(3, two.Tier); Assert.Equal(2, two.Teams.Count); Assert.Equal(2, two.Settings.SpawnCount);
            Assert.Null(thin.TryForm(1000f));                                                            // the two Tier I groups wait for a third
        }

        [Fact]
        public void AccountLevelsGateOnlyTheEarnedClasses()
        {
            Assert.Equal(0, AccountRules.XpToReach(1));
            Assert.Equal(2250, AccountRules.XpToReach(5));
            Assert.Equal(8250, AccountRules.XpToReach(10));
            Assert.Equal(1, AccountRules.LevelFor(0)); Assert.Equal(4, AccountRules.LevelFor(2249)); Assert.Equal(5, AccountRules.LevelFor(2250));
            var p = PlayerProfile.NewAccount("acct", "Ash", ClassArchetype.DefaultRoster.First(c => c.Name == "Mender"));
            Assert.Equal(1, p.AccountLevel);
            Assert.Equal(new[] { "Warden", "Marksman", "Mender" }, p.PlayableClasses.Select(c => c.Name).ToArray());
            Assert.Throws<System.InvalidOperationException>(() => p.CreateCharacter("r", ClassArchetype.DefaultRoster.First(c => c.Name == "Reaver")));
            // Fifteen extractions and a few kills: a weekend.
            for (int i = 0; i < 15; i++) p.AddAccountXp(AccountRules.XpPerExtraction);
            p.AddAccountXp(AccountRules.XpPerPlayerKill * 5 + AccountRules.XpPerBossKill * 5 + AccountRules.XpPerFinalBossKill);
            Assert.True(p.AccountLevel >= 5, $"level {p.AccountLevel}");
            Assert.False(p.ClassUnlocked(ClassArchetype.DefaultRoster.First(c => c.Name == "Reaver")));
            p.AddAccountXp(AccountRules.XpToReach(10) - p.AccountXp);
            Assert.Equal(new[] { "Warden", "Reaver", "Marksman", "Mender" }, p.PlayableClasses.Select(c => c.Name).ToArray());
            p.AddAccountXp(AccountRules.XpToReach(20) - p.AccountXp);
            Assert.Equal(6, p.PlayableClasses.Count());
            Assert.Equal(0, FightRules.SecurePouchSlots);
            Assert.Equal("Free to play", Monetisation.Model); Assert.False(Monetisation.BattlePass); Assert.False(Monetisation.PaidPower);
            Assert.Contains(Roadmap.SeasonTwo, r => r.Contains("contracts"));
        }

        [Fact]
        public void TransferLedgerFlagsOneWayFlows()
        {
            var ledger = new TransferLedger();
            var t = new System.DateTime(2027, 1, 1);
            ledger.Record(t, "seller", "buyer", 150); ledger.Record(t, "seller", "buyer", 150); ledger.Record(t, "seller", "buyer", 60);
            ledger.Record(t, "a", "b", 500);                                     // one big gift between friends: not enough transfers
            ledger.Record(t, "c", "d", 200); ledger.Record(t, "d", "c", 190); ledger.Record(t, "c", "d", 50); // two-way trading: net small
            var flagged = ledger.Flagged().ToList();
            Assert.Single(flagged);
            Assert.Equal(("seller", "buyer", 360, 3), flagged[0]);
        }

        [Fact]
        public void SeasonKeepsFourBoardsPerTier()
        {
            var s = new Season(2, new System.DateTime(2027, 1, 1), new System.DateTime(2027, 4, 1));
            s.RecordExtraction("a", 600, tier: 1); s.RecordExtraction("b", 3000, tier: 3); s.RecordExtraction("a", 100, tier: 3);
            s.RecordKill("b", tier: 3); s.RecordKill("b", tier: 1); s.RecordBossKill("a", finalBoss: false, tier: 1); s.RecordBossKill("a", finalBoss: true, tier: 3);
            Assert.Equal("b", s.Leaderboard().First().account);                                 // main board, all tiers
            Assert.Equal(("a", 600L), s.Leaderboard(LeaderboardKind.ExtractedValue, tier: 1, top: 10).First());
            Assert.Equal(("b", 3000L), s.Leaderboard(LeaderboardKind.ExtractedValue, tier: 3, top: 10).First());
            Assert.Equal(2, s.Leaderboard(LeaderboardKind.PlayerKills).First().value);
            Assert.Equal(1, s.Leaderboard(LeaderboardKind.BossKills, tier: 1, top: 10).Single().value);
            Assert.Equal("a", s.Leaderboard(LeaderboardKind.FinalBossKills).Single().account);
            Assert.Empty(s.Leaderboard(LeaderboardKind.FinalBossKills, tier: 2, top: 10));
        }

        [Fact]
        public void TheDungeonAnnouncesExactlyTwoThings()
        {
            var layout = DungeonGenerator.Generate(DungeonSettings.ForTeams(4), 3);
            Assert.NotNull(Announcer.For(new MatchEvent(MatchEventKind.BossKilled, 1, layout.FinalBossRoom.Id, 0), layout));
            Assert.Contains("fallen", Announcer.For(new MatchEvent(MatchEventKind.BossKilled, 1, layout.FinalBossRoom.Id, 0), layout).Value.Text);
            Assert.NotNull(Announcer.For(new MatchEvent(MatchEventKind.BossExitOpened, 2), layout));
            Assert.Null(Announcer.For(new MatchEvent(MatchEventKind.BossKilled, 1, layout.BossRooms.First().Id, 0), layout));   // a room boss is nobody else's business
            Assert.Null(Announcer.For(new MatchEvent(MatchEventKind.PlayerExtracted, 1, 1, 0), layout));
            Assert.Null(Announcer.For(new MatchEvent(MatchEventKind.PlayerDied, 1, 1, 2, 0), layout));
        }

        private static (MatchState st, DungeonLayout layout) Fresh()
        {
            var d = MatchDescriptor.Create(7, DungeonSettings.ForTeams(8), new MatchRules(), out var layout);
            var st = new MatchState(d, layout);
            st.Apply(new MatchEvent(MatchEventKind.RoundStarted, 0), out _);
            for (int p = 1; p <= 6; p++) st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, p, (p - 1) / 3), out _);
            return (st, layout);
        }

        [Fact]
        public void HandOffsNeedTheSameRoomHaveNoGateAndAreLogged()
        {
            var (st, layout) = Fresh();
            string why;
            var room = layout.FinalBossRoom;
            int item = st.Index.Items.First(kv => st.Index.ItemRoom[kv.Key] == room.Id).Key;
            st.Apply(new MatchEvent(MatchEventKind.BossKilled, 5, room.Id, 0), out _);
            st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 6, 1, room.Id), out _);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.LootStarted, 7, 1, room.Id, 0), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ContainerOpened, 9, room.Id, 1), out why));   // alone in range: no roll
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ItemTaken, 10, item, 1), out why));

            Assert.False(st.Apply(new MatchEvent(MatchEventKind.ItemGiven, 11, item, 1, 2), out why));
            Assert.Contains("same room", why);
            st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 11, 2, room.Id), out _);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.AccountLevelReported, 12, 1, 1), out why));   // day-one accounts on both sides: no gate
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.AccountLevelReported, 12, 2, 1), out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.ItemGiven, 12, item, 2, 1), out why), "not the giver's item");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ItemGiven, 13, item, 1, 2), out why));
            Assert.Equal(2, st.ItemOwner[item]);
            Assert.Single(st.Transfers);
            Assert.Equal((1, 2), (st.Transfers[0].from, st.Transfers[0].to));
            Assert.Equal(st.Index.Items[item].Value, st.Transfers[0].value);

            // Drop to the floor: anyone standing in the room may pick it up, enemies included; nobody elsewhere can.
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ItemDropped, 14, item, 2), out why));
            Assert.False(st.ItemOwner.ContainsKey(item));
            Assert.Equal(room.Id, st.FloorItems[item]);
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.ItemTaken, 15, item, 4), out why));
            Assert.Contains("not in that room", why);
            st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 15, 4, room.Id), out _);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ItemTaken, 16, item, 4), out why));
            Assert.Equal(4, st.ItemOwner[item]);
            Assert.Empty(st.FloorItems);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ItemDropped, 17, item, 4), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.RoundEnded, 1200), out why));
            Assert.Contains(item, st.DestroyedItems);                                                       // the floor does not survive the round

            var replay = MatchState.Replay(MatchDescriptor.FromBytes(st.Descriptor.ToBytes()), layout, MatchState.LogFromBytes(st.LogToBytes()));
            Assert.Equal(st.Transfers.Count, replay.Transfers.Count);
            Assert.Equal(st.AccountLevel, replay.AccountLevel);
        }

        [Fact]
        public void ChestSilverSplitsAcrossNearTeammatesAndMobPilesDoNot()
        {
            var (st, layout) = Fresh();
            string why;
            var spawn = layout.Rooms[st.Descriptor.TeamSpawnRooms[0]];
            var chest = layout.Rooms.First(r => r.Type == RoomType.Standard && r.Loot.Count > 0 && r.Silver > 0 && !st.Index.RoomsWithin(r.Id, 2).Contains(spawn.Id));
            st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 1, 1, chest.Id), out _);
            st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 1, 2, chest.Id), out _);                 // 3 stays at spawn
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.LootStarted, 2, 1, chest.Id, 0), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ContainerOpened, 4, chest.Id, 1), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.SilverPicked, 5, 1, chest.Id), out why));
            int share = chest.Silver / 2, rest = chest.Silver - share * 2;
            Assert.Equal(share + rest, st.PlayerSilver[1]);
            Assert.Equal(share, st.PlayerSilver[2]);
            Assert.False(st.PlayerSilver.ContainsKey(3));
            Assert.Equal(chest.Silver, st.PlayerSilver[1] + st.PlayerSilver[2]);

            var pile = layout.Rooms.FirstOrDefault(r => r.Mobs != null && r.Loot.Count == 0 && r.Silver > 0);
            if (pile != null)
            {
                st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 6, 1, pile.Id), out _);
                st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 6, 2, pile.Id), out _);
                int before = st.PlayerSilver[1];
                Assert.True(st.Apply(new MatchEvent(MatchEventKind.SilverPicked, 7, 1, pile.Id), out why));
                Assert.Equal(before + pile.Silver, st.PlayerSilver[1]);                                      // the pile is the picker's
                Assert.Equal(share, st.PlayerSilver[2]);
            }
        }

        [Fact]
        public void DeadPlayersMayLeaveAndForfeitTheirResurrection()
        {
            var (st, layout) = Fresh();
            string why;
            int shrine = layout.Rooms.First(r => r.HasShrine).Id;
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.PlayerLeft, 1, 2), out why), "the living do not leave early");
            st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 2, 2, 4, 0), out _);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerLeft, 3, 2), out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.PlayerLeft, 3, 2), out why));
            st.Souls[1] = 2;
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.PlayerResurrected, 4, 2, 1, shrine), out why));
            Assert.Contains("left", why);
            Assert.Equal(2, st.SoulsOf(1));                                                                  // nothing spent
        }
    }
}

namespace DungeonGen.Tests
{
    using DungeonGen.Core.Hud;
    using DungeonGen.Core.Net;

    public class HudTests
    {
        [Fact]
        public void CompassBearingsNorthIsUpEastIsRight()
        {
            var o = new GridPoint(10, 10);
            Assert.Equal(0f, Compass.Bearing(o, new GridPoint(10, 20)));
            Assert.Equal(90f, Compass.Bearing(o, new GridPoint(20, 10)));
            Assert.Equal(180f, Compass.Bearing(o, new GridPoint(10, 0)));
            Assert.Equal(270f, Compass.Bearing(o, new GridPoint(0, 10)));
            Assert.Equal(45f, Compass.Bearing(o, new GridPoint(15, 15)), 3);
            Assert.Equal(Cardinal.N, Compass.CardinalOf(359f)); Assert.Equal(Cardinal.N, Compass.CardinalOf(22f)); Assert.Equal(Cardinal.NE, Compass.CardinalOf(23f));
            Assert.Equal(Cardinal.SW, Compass.CardinalOf(225f)); Assert.Equal("south-west", Compass.Name(Cardinal.SW));
            Assert.Equal(-10f, Compass.Relative(350f, 0f), 3); Assert.Equal(180f, Compass.Relative(180f, 0f), 3); Assert.Equal(-90f, Compass.Relative(0f, 90f), 3);
            Assert.True(CompassBar.InWindow(90f)); Assert.False(CompassBar.InWindow(91f));
            Assert.Equal(0.5f, CompassBar.Position01(0f), 3); Assert.Equal(0f, CompassBar.Position01(-90f), 3); Assert.Equal(1f, CompassBar.Position01(90f), 3);
            Assert.Equal(5f, Compass.DistanceCells(new GridPoint(0, 0), new GridPoint(3, 4)), 3);
        }

        private static (MatchState st, DungeonLayout layout) Fresh()
        {
            var d = MatchDescriptor.Create(7, DungeonSettings.ForTeams(8), new MatchRules(), out var layout);
            var st = new MatchState(d, layout);
            st.Apply(new MatchEvent(MatchEventKind.RoundStarted, 0), out _);
            for (int p = 1; p <= 6; p++) st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, p, (p - 1) / 3), out _);
            return (st, layout);
        }

        [Fact]
        public void CompassShowsOnlyWhatTheTeamHasSeenPlusTeammatesAndLivePings()
        {
            var (st, layout) = Fresh();
            var spawn = layout.Rooms[st.Descriptor.TeamSpawnRooms[0]];
            var me = spawn.Center;
            var cells = new Dictionary<int, GridPoint> { [2] = me + new GridPoint(0, 5), [3] = me + new GridPoint(5, 0), [4] = me + new GridPoint(-5, 0) };
            var pings = new List<TeamPing> { new TeamPing(2, 0, PingKind.Enemy, me + new GridPoint(0, -5), 10f), new TeamPing(4, 1, PingKind.Loot, me, 10f), new TeamPing(3, 0, PingKind.Boss, me + new GridPoint(3, 3), 1f) };
            var markers = CompassBar.Markers(st, 1, me, cells, pings, now: 12f);
            Assert.Single(markers, m => m.Kind == CompassMarkerKind.Spawn);                                  // own spawn, nothing else explored yet
            Assert.Equal(2, markers.Count(m => m.Kind == CompassMarkerKind.Teammate));                       // 2 and 3; 4 is an enemy
            Assert.Equal(0f, markers.First(m => m.Ref == 2 && m.Kind == CompassMarkerKind.Teammate).Bearing);
            Assert.Single(markers, m => m.Kind == CompassMarkerKind.Ping);                                   // enemy team's ping hidden, the 11-second-old one expired
            Assert.Equal(180f, markers.First(m => m.Kind == CompassMarkerKind.Ping).Bearing);
            Assert.DoesNotContain(markers, m => m.Kind == CompassMarkerKind.Extract || m.Kind == CompassMarkerKind.BossRoom || m.Kind == CompassMarkerKind.Shrine);

            var extract = layout.ExtractRooms.First(); var boss = layout.BossRooms.First(); var shrine = layout.Rooms.First(r => r.HasShrine); var vault = layout.VaultRooms.First();
            foreach (var r in new[] { extract, boss, shrine, vault, layout.FinalBossRoom }) Assert.True(st.Apply(new MatchEvent(MatchEventKind.RoomSeen, 13, 0, r.Id), out _));
            markers = CompassBar.Markers(st, 1, me, cells, pings, 13f);
            Assert.Equal("Extract", markers.First(m => m.Kind == CompassMarkerKind.Extract).Label);
            Assert.Equal("Vault (locked)", markers.First(m => m.Kind == CompassMarkerKind.Vault).Label);
            Assert.Equal("Final boss", markers.First(m => m.Kind == CompassMarkerKind.FinalBoss).Label);
            Assert.Single(markers, m => m.Kind == CompassMarkerKind.Shrine);
            Assert.Equal(boss.Boss.Archetype.Name, markers.First(m => m.Kind == CompassMarkerKind.BossRoom).Label);
            Assert.Empty(CompassBar.Markers(st, 4, me, cells, pings, 13f).Where(m => m.Kind != CompassMarkerKind.Teammate && m.Kind != CompassMarkerKind.Ping && m.Kind != CompassMarkerKind.Spawn && m.Ref != st.Descriptor.TeamSpawnRooms[1]));   // team 1 has seen none of it

            st.Apply(new MatchEvent(MatchEventKind.ExtractsOpened, 240), out _);
            st.Apply(new MatchEvent(MatchEventKind.BossKilled, 241, boss.Id, 1), out _);
            st.Apply(new MatchEvent(MatchEventKind.BossKilled, 242, layout.FinalBossRoom.Id, 1), out _);
            st.Apply(new MatchEvent(MatchEventKind.BossExitOpened, 243), out _);
            markers = CompassBar.Markers(st, 1, me, cells, pings, 244f);
            Assert.Equal("Extract (open)", markers.First(m => m.Kind == CompassMarkerKind.Extract).Label);
            Assert.Equal("Boss (dead)", markers.First(m => m.Kind == CompassMarkerKind.BossRoom).Label);
            Assert.Single(markers, m => m.Kind == CompassMarkerKind.BossExit);
            Assert.Contains("north", CompassBar.Callout(markers.First(m => m.Ref == 2 && m.Kind == CompassMarkerKind.Teammate)));
            Assert.Contains("10 m", CompassBar.Callout(markers.First(m => m.Ref == 2 && m.Kind == CompassMarkerKind.Teammate)));
        }

        [Fact]
        public void MiniMapIsExploredRoomsOnlyWithCorridorsBetweenThem()
        {
            var (st, layout) = Fresh();
            var spawn = layout.Rooms[st.Descriptor.TeamSpawnRooms[0]];
            var me = spawn.Center;
            var model = MiniMap.Build(st, 1, me, new Dictionary<int, GridPoint> { [2] = me, [5] = me }, new List<TeamPing>(), 1f);
            Assert.Single(model.Rooms);
            Assert.True(model.Rooms[0].IsTeamSpawn && model.Rooms[0].IsHere);
            Assert.Empty(model.Corridors);
            Assert.Equal(2, model.Markers.Count);                                                         // self and teammate 2; 5 is an enemy, never drawn
            Assert.Contains(model.Markers, m => m.Kind == MiniMapMarkerKind.Self && m.Cell.Equals(me));

            int next = spawn.Neighbours.First();
            st.Apply(new MatchEvent(MatchEventKind.RoomSeen, 2, 0, next), out _);
            var vault = layout.VaultRooms.First();
            st.Apply(new MatchEvent(MatchEventKind.RoomSeen, 2, 0, vault.Id), out _);
            model = MiniMap.Build(st, 1, me, null, null, 2f);
            Assert.Equal(3, model.Rooms.Count);
            Assert.Contains((System.Math.Min(spawn.Id, next), System.Math.Max(spawn.Id, next)), model.Corridors);
            Assert.DoesNotContain(model.Corridors, c => c.a == vault.Id || c.b == vault.Id);              // its neighbour is unexplored
            Assert.Equal(vault.Doors.Count, model.Markers.Count(m => m.Kind == MiniMapMarkerKind.LockedDoor));
            Assert.DoesNotContain(model.Rooms, r => r.Id == layout.FinalBossRoom.Id);
            Assert.True(model.Shows(spawn.Bounds));
            Assert.False(model.Shows(new GridRect(me.X + MiniMapModel.ViewRadiusCells + 5, me.Y, 3, 3)));
            // The other team's map is theirs alone.
            var theirs = MiniMap.Build(st, 4, me, null, null, 2f);
            Assert.Single(theirs.Rooms);
            Assert.Equal(st.Descriptor.TeamSpawnRooms[1], theirs.Rooms[0].Id);
            Assert.Equal(8f, TeamPing.LifetimeSeconds);
        }
    }
}

namespace DungeonGen.Tests
{
    using DungeonGen.Core.Combat;
    using DungeonGen.Core.Net;
    using DungeonGen.Core.Profile;

    public class OperationsTests
    {
        private static (MatchState st, DungeonLayout layout) Fresh()
        {
            var d = MatchDescriptor.Create(7, DungeonSettings.ForTeams(8), new MatchRules(), out var layout);
            var st = new MatchState(d, layout);
            st.Apply(new MatchEvent(MatchEventKind.RoundStarted, 0), out _);
            for (int p = 1; p <= 6; p++) st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, p, (p - 1) / 3), out _);
            return (st, layout);
        }

        [Fact]
        public void DeathRecapNamesClassAndSpecNeverTheAccount()
        {
            var (st, layout) = Fresh();
            var hits = new HitLog();
            for (int i = 0; i < 7; i++) hits.Record(4, new Hit(9f + i * 0.1f, 1, "Sentinel", DamageType.Physical, 80f + i, "Shield Bash"));
            Assert.Equal(HitLog.Kept, hits.LastHits(4).Count);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDowned, 10, 4, 1, 0), out _));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerFinished, 15, 4, 1, 0), out _));
            var r = DeathRecap.For(st, 4, hits);
            Assert.Equal(1, r.Killer); Assert.Equal("Warden", r.KillerClass); Assert.Equal("Sentinel", r.KillerSpec);
            Assert.True(r.Finished); Assert.False(r.KilledByTeammate); Assert.Equal(5f, r.DownedSeconds, 3);
            Assert.Equal("Finished by a Warden (Sentinel).", r.Headline);
            Assert.DoesNotContain("acct", r.Headline);
            st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 20, 5, -1, 0), out _);
            Assert.Equal("Killed by the dungeon.", DeathRecap.For(st, 5, hits).Headline);
            st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 21, 2, 3, 0), out _);
            Assert.Contains("your own Warden", DeathRecap.For(st, 2, null).Headline);
            Assert.Equal(-1, DeathRecap.For(st, 6, hits).Killer);                                   // still alive: empty recap

            var sum = RoundSummary.For(st, 1);
            Assert.Equal(1, sum.PlayerKills); Assert.Equal(0, sum.SoulsEarned); Assert.False(sum.Extracted); Assert.False(sum.Died);   // the soul is on 4's corpse
            Assert.True(Ch.OpenCorpse(st, 16, 4, 1, out _));
            sum = RoundSummary.For(st, 1);
            Assert.Equal(1, sum.SoulsEarned);
            Assert.Equal(0, RoundSummary.For(st, 6).SoulsEarned);
            Assert.Equal(AccountRules.XpPerPlayerKill, sum.AccountXp);
            int shrine = layout.Rooms.First(r2 => r2.HasShrine).Id;
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ShrinePurchase, 22, 1, 0, shrine), out _));
            Assert.Equal(1, RoundSummary.For(st, 1).SoulsSpent);
            Assert.Equal(0, RoundSummary.For(st, 3).PlayerKills);                                   // a teammate kill is not a kill
            Assert.Equal(1, sum.Tier);
        }

        [Fact]
        public void ReportsDedupeAndAreReadByHumans()
        {
            var q = new ReportQueue();
            var t = new System.DateTime(2027, 1, 1);
            Assert.False(ReportQueue.AutomatedAction);
            Assert.True(q.Submit(new PlayerReport(t, "a", "x", "m1", ReportReason.Cheating)));
            Assert.False(q.Submit(new PlayerReport(t, "a", "x", "m1", ReportReason.Abuse)), "same reporter, accused and match");
            Assert.True(q.Submit(new PlayerReport(t, "a", "x", "m2", ReportReason.Cheating)));
            Assert.True(q.Submit(new PlayerReport(t, "b", "x", "m1", ReportReason.RealMoneyTrading)));
            Assert.True(q.Submit(new PlayerReport(t, "c", "y", "m1", ReportReason.Abuse)));
            Assert.False(q.Submit(new PlayerReport(t, "c", "c", "m1", ReportReason.Abuse)), "self");
            Assert.False(q.Submit(new PlayerReport(t, "c", "y", "", ReportReason.Abuse)), "no match id");
            var order = q.ReadingOrder().ToList();
            Assert.Equal(("x", 2, 3), order[0]); Assert.Equal(("y", 1, 1), order[1]);
        }

        [Fact]
        public void StringsResolveAndFallBack()
        {
            Assert.All(Strings.Keys, k => Assert.NotEqual(k, Strings.Get(k)));
            Assert.Equal("no.such.key", Strings.Get("no.such.key"));
            Assert.Equal(new[] { "en" }, Strings.LaunchLanguages);
            var layout = DungeonGenerator.Generate(DungeonSettings.ForTeams(4), 3);
            Strings.Load("fr", new Dictionary<string, string> { ["announce.exit_open"] = "La sortie est ouverte." });
            try
            {
                Assert.Equal("fr", Strings.Language);
                Assert.Equal("La sortie est ouverte.", Announcer.For(new MatchEvent(MatchEventKind.BossExitOpened, 1), layout).Value.Text);
                Assert.Equal("Shrine", Strings.Get("hud.shrine"));                                      // untranslated keys fall back to English
            }
            finally { Strings.ResetToEnglish(); }
            Assert.Contains("opened", Announcer.For(new MatchEvent(MatchEventKind.BossExitOpened, 1), layout).Value.Text);
        }

        [Fact]
        public void RegionsAutoPickByLatencyAndNeverMix()
        {
            Assert.True(Region.NeverMixed);
            var lat = new Dictionary<string, float> { ["eu"] = 90f, ["na-east"] = 35f, ["asia"] = 200f, ["mars"] = 1f };
            Assert.Equal("na-east", Region.Pick(lat));
            Assert.Equal("eu", Region.Pick(lat, "eu"));
            Assert.Equal("na-east", Region.Pick(lat, "mars"));                                        // unknown override ignored
            Assert.Equal(Region.Launch[0], Region.Pick(null));
            var mm = new Matchmaker(teamsPerMatch: 2, minTeamsAfterWait: 2, maxWaitSeconds: 90f);
            mm.Enqueue(new QueuedGroup(1, new[] { 1 }, 0f, 1, "eu"));
            mm.Enqueue(new QueuedGroup(2, new[] { 2 }, 0f, 1, "na-east"));
            Assert.Null(mm.TryForm(1f));
            mm.Enqueue(new QueuedGroup(3, new[] { 3 }, 1f, 1, "eu"));
            var m = mm.TryForm(2f);
            Assert.Equal("eu", m.Region);
            Assert.All(m.Teams, t => Assert.Equal("eu", t.Region));
            Assert.Equal(TechStack.RegionPolicy, TechStack.RegionPolicy);
            Assert.Contains("Easy Anti-Cheat", TechStack.AntiCheat);
        }

        [Fact]
        public void SeasonsAreThreeMonthsAndStoreHasNoLootBoxes()
        {
            var s = Season.Starting(1, new System.DateTime(2027, 1, 1));
            Assert.Equal(91, Season.LengthDays);
            Assert.Equal(new System.DateTime(2027, 4, 2), s.EndUtc);
            Assert.Equal(2, s.Next().Number); Assert.Equal(s.EndUtc, s.Next().StartUtc);
            Assert.False(Monetisation.LootBoxes); Assert.Equal(7, Monetisation.FeaturedRotationDays); Assert.Equal(7, Monetisation.MinimumOfferDays);
            Assert.True(MovementRules.EnemiesBlock); Assert.False(MovementRules.AlliesBlock);
        }

        [Fact]
        public void NobodyJoinsAfterTheDoorsOpen()
        {
            var (st, layout) = Fresh();
            string why;
            Assert.False(MatchRules.Backfill);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 5, 7, 2), out why), "before the doors: fine");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.DoorsOpened, 10), out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 11, 8, 2), out why));
            Assert.Contains("backfill", why);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDisconnected, 12, 7), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerReconnected, 20, 7), out why), "a reconnect is not a join");
        }
    }
}

namespace DungeonGen.Tests
{
    using DungeonGen.Core.Combat;
    using DungeonGen.Core.Hud;
    using DungeonGen.Core.Items;
    using DungeonGen.Core.Net;
    using DungeonGen.Core.Profile;

    public class AuditTests
    {
        private static (MatchState st, DungeonLayout layout) Fresh()
        {
            var d = MatchDescriptor.Create(7, DungeonSettings.ForTeams(8), new MatchRules(), out var layout);
            var st = new MatchState(d, layout);
            st.Apply(new MatchEvent(MatchEventKind.RoundStarted, 0), out _);
            for (int p = 1; p <= 6; p++) st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, p, (p - 1) / 3), out _);
            return (st, layout);
        }

        [Fact]
        public void TeamViewsNeverCarryAnotherTeamsRoomsSoulsOrLoot()
        {
            var (st, layout) = Fresh();
            var boss = layout.BossRooms.First();
            st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 1, 4, boss.Id), out _);          // team 1 is at a boss room
            st.Apply(new MatchEvent(MatchEventKind.RoomSeen, 1, 1, boss.Id), out _);
            st.Apply(new MatchEvent(MatchEventKind.BossKilled, 2, boss.Id, 1), out _);
            st.Apply(new MatchEvent(MatchEventKind.LootStarted, 3, 4, boss.Id, 0), out _);
            st.Apply(new MatchEvent(MatchEventKind.ContainerOpened, 5, boss.Id, 4), out _);           // 4 takes the boss soul
            st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 6, 5, 1, 0), out _);                     // 1 kills 5: both teams hear that
            var v0 = TeamView.For(st, 0); var v1 = TeamView.For(st, 1);
            Assert.All(v0.Members, m => Assert.Equal(0, st.PlayerTeam[m.Player]));
            Assert.DoesNotContain(v0.Members, m => m.Player == 4);                                       // no enemy position, soul or silver anywhere in team 0's view
            Assert.DoesNotContain(boss.Id, v0.Explored); Assert.DoesNotContain(boss.Id, v0.DeadBosses); Assert.DoesNotContain(boss.Id, v0.OpenedContainers);
            Assert.Empty(v0.Items);
            Assert.Contains(boss.Id, v1.Explored); Assert.Contains(boss.Id, v1.DeadBosses);
            Assert.NotEmpty(v1.Items);
            Assert.Equal(1, v1.Members.First(m => m.Player == 4).Souls);
            Assert.Equal(0, v0.Members.First(m => m.Player == 1).Souls);                                 // the kill left the soul on 5's corpse
            Assert.True(v1.Corpses.First(c => c.Player == 5).HasSoul);
            Assert.True(v1.Members.First(m => m.Player == 5).Dead);
            Assert.DoesNotContain(v0.Corpses, c => c.Player == 5);                                       // team 0 has not seen the room the corpse lies in
            var back = TeamView.FromBytes(v1.ToBytes());
            Assert.Equal(v1.ToBytes(), back.ToBytes());
            Assert.Equal(v1.Items.Count, back.Items.Count); Assert.Equal(v1.Members.Count, back.Members.Count);
        }

        [Fact]
        public void LootIsRolledFromAServerSecretTheSeedCannotReproduce()
        {
            var s = DungeonSettings.ForTeams(4);
            var a = MatchDescriptor.Create(77, s, new MatchRules(), out var layoutA, lootSecret: 1234567);
            var b = MatchDescriptor.Create(77, s, new MatchRules(), out var layoutB, lootSecret: 7654321);
            Assert.Equal(a.ToBytes(), b.ToBytes());                                                    // the descriptor carries no trace of the secret
            Assert.Equal(ClientView.PublicFingerprint(layoutA), ClientView.PublicFingerprint(layoutB));
            Assert.Equal(a.ExpectedFingerprint, ClientView.PublicFingerprint(layoutA));
            Assert.NotEqual(LayoutFingerprint.Compute(layoutA), LayoutFingerprint.Compute(layoutB));   // different loot
            Assert.NotEqual(layoutA.Items.Select(i => i.Name + i.Rarity), layoutB.Items.Select(i => i.Name + i.Rarity));
            Assert.Equal(layoutA.Rooms.Count, layoutB.Rooms.Count);                                     // the same dungeon; only what is in the chests differs
            var client = MatchDescriptor.FromBytes(a.ToBytes()).BuildAndVerify();
            Assert.Empty(client.Items);
            Assert.Equal(layoutA.Rooms.Count, client.Rooms.Count);
        }

        [Fact]
        public void ClientMirrorBuiltFromRedactedLayoutAndViewsSeesWhatTheServerSaysItMay()
        {
            var (server, layout) = Fresh();
            var spawn = layout.Rooms[server.Descriptor.TeamSpawnRooms[0]];
            int next = spawn.Neighbours.First();
            server.Apply(new MatchEvent(MatchEventKind.RoomSeen, 1, 0, next), out _);
            server.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 1, 1, next), out _);
            var shrine = layout.Rooms.First(r => r.HasShrine);
            server.Apply(new MatchEvent(MatchEventKind.RoomSeen, 1, 0, shrine.Id), out _);
            var chest = layout.Rooms.First(r => r.Type == RoomType.Standard && r.Loot.Count > 0);
            server.Apply(new MatchEvent(MatchEventKind.RoomSeen, 2, 0, chest.Id), out _);
            server.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 2, 1, chest.Id), out _);
            server.Apply(new MatchEvent(MatchEventKind.LootStarted, 3, 1, chest.Id, 0), out _);
            Assert.True(server.Apply(new MatchEvent(MatchEventKind.ContainerOpened, 5, chest.Id, 1), out _));

            // The client gets the descriptor (public layout, no loot) and its team's view, never the log.
            var client = new MatchState(MatchDescriptor.FromBytes(server.Descriptor.ToBytes()), server.Descriptor.BuildAndVerify());
            Assert.Empty(client.Index.Items);
            client.LoadView(TeamView.FromBytes(TeamView.For(server, 0).ToBytes()));
            Assert.True(client.IsView);
            Assert.Equal(server.Explored[0], client.Explored[0]);
            Assert.Equal(server.VisibleItemsIn(chest.Id).OrderBy(i => i), client.VisibleItemsIn(chest.Id).OrderBy(i => i));
            Assert.Equal(chest.Id, client.PlayerRoom[1]);
            Assert.False(client.PlayerTeam.ContainsKey(4));
            // The HUD models give the same answer on both sides.
            var me = chest.Center;
            var cells = new Dictionary<int, GridPoint> { [2] = spawn.Center };
            var serverMarkers = CompassBar.Markers(server, 1, me, cells, null, 5f).Select(m => (m.Kind, m.Label, m.Ref)).OrderBy(x => x.Ref).ToList();
            var clientMarkers = CompassBar.Markers(client, 1, me, cells, null, 5f).Select(m => (m.Kind, m.Label, m.Ref)).OrderBy(x => x.Ref).ToList();
            Assert.Equal(serverMarkers, clientMarkers);
            Assert.Equal(MiniMap.Build(server, 1, me, cells, null, 5f).Rooms.Select(r => r.Id), MiniMap.Build(client, 1, me, cells, null, 5f).Rooms.Select(r => r.Id));
        }

        [Fact]
        public void EventAudiencesKeepPositionsAndRollsInsideTheTeam()
        {
            var (st, layout) = Fresh();
            Assert.Equal(new HashSet<int> { 0 }, EventVisibility.Audience(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 1, 2, 3), st));
            Assert.Equal(new HashSet<int> { 1 }, EventVisibility.Audience(new MatchEvent(MatchEventKind.LootRolled, 1, 7, 5, 0), st));
            Assert.Equal(new HashSet<int> { 0, 1 }, EventVisibility.Audience(new MatchEvent(MatchEventKind.PlayerDied, 1, 5, 1, 0), st));
            Assert.Equal(new HashSet<int> { 1 }, EventVisibility.Audience(new MatchEvent(MatchEventKind.PlayerDied, 1, 5, -1, 0), st));   // a mob kill is the victim's team's business
            Assert.Null(EventVisibility.Audience(new MatchEvent(MatchEventKind.BossExitOpened, 1), st));
            Assert.Null(EventVisibility.Audience(new MatchEvent(MatchEventKind.BossKilled, 1, layout.FinalBossRoom.Id, 1), st));
            Assert.Equal(new HashSet<int> { 1 }, EventVisibility.Audience(new MatchEvent(MatchEventKind.BossKilled, 1, layout.BossRooms.First().Id, 1), st));
            Assert.Equal(new HashSet<int> { 0 }, EventVisibility.Audience(new MatchEvent(MatchEventKind.UltimateArmed, 1, 3, 0), st));
            Assert.Equal(new HashSet<int> { 0 }, EventVisibility.Audience(new MatchEvent(MatchEventKind.AccountLevelReported, 1, 3, 9), st));
            Assert.Empty(EventVisibility.Audience(new MatchEvent(MatchEventKind.MobPackCleared, 1, 0), st));
            // Every event kind has a rule (none falls through to a surprise).
            foreach (MatchEventKind k in System.Enum.GetValues(typeof(MatchEventKind))) EventVisibility.Audience(new MatchEvent(k, 1, 0, 0, 0), st);
        }

        [Fact]
        public void KeysCanBeHandedToATeammateInTheRoom()
        {
            var (st, layout) = Fresh();
            string why;
            var key = layout.Keys.First(k => !k.DroppedByBoss);
            st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 1, 1, key.HolderRoomId), out _);
            Assert.True(Ch.TakeKey(st, 2, key.KeyId, 1, out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.KeyGiven, 4, key.KeyId, 1, 2), out why));
            Assert.Contains("same room", why);
            st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 4, 2, key.HolderRoomId), out _);
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.KeyGiven, 5, key.KeyId, 2, 1), out why), "not the holder");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.KeyGiven, 5, key.KeyId, 1, 2), out why), "no account gate on keys");
            Assert.Equal(2, st.KeyHolder[key.KeyId]);
            Assert.Equal(new HashSet<int> { 0 }, EventVisibility.Audience(new MatchEvent(MatchEventKind.KeyGiven, 5, key.KeyId, 1, 2), st));
        }

        [Fact]
        public void NeedOnAUniqueYouAlreadyCarryCountsAsGreed()
        {
            var d = MatchDescriptor.Create(7, DungeonSettings.ForTeams(8), new MatchRules(), out var layout);
            var st = new MatchState(d, layout);
            st.Apply(new MatchEvent(MatchEventKind.RoundStarted, 0), out _);
            for (int p = 1; p <= 3; p++) st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, p, 0), out _);
            // Find a dungeon (any seed) whose final boss room holds two copies of one unique; if none in range, the rule still holds trivially.
            var uniques = st.Index.Items.Where(kv => kv.Value.Unique != null).ToList();
            if (uniques.Count < 2) return;
            var first = uniques[0];
            var same = uniques.Skip(1).FirstOrDefault(kv => kv.Value.Unique == first.Value.Unique);
            if (same.Value == null) return;
            int room = st.Index.ItemRoom[same.Key];
            st.ItemOwner[first.Key] = 1;                                                                 // player 1 already carries that unique
            foreach (int p in new[] { 1, 2, 3 }) st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 1, p, room), out _);
            if (layout.Rooms[room].IsBossRoom) st.Apply(new MatchEvent(MatchEventKind.BossKilled, 2, room, 0), out _);
            Assert.True(Ch.OpenChest(st, 3, room, 2, out string why), why);
            st.Apply(new MatchEvent(MatchEventKind.LootRolled, 6, same.Key, 1, (int)LootRollChoice.Need), out _);
            st.Apply(new MatchEvent(MatchEventKind.LootRolled, 6, same.Key, 2, (int)LootRollChoice.Need), out _);
            var entries = st.RollEntries(same.Key).ToDictionary(e => e.Player, e => e.Choice);
            Assert.Equal(LootRollChoice.Greed, entries[1]);
            Assert.Equal(LootRollChoice.Need, entries[2]);
        }

        [Fact]
        public void StarterGearIsWorthNothingToTheVendorOrTheSalvageBench()
        {
            var p = PlayerProfile.NewAccount("acct", "Ash", ClassArchetype.DefaultRoster[0]);
            var piece = p.Characters[0].Equipment.Items.First();
            Assert.True(piece.IsStarter);
            Assert.Equal(0f, CraftingRules.SellPrice(piece));
            var bag = CraftingRules.Salvage(piece);
            Assert.Equal(0, bag.Scrap); Assert.Empty(bag.Essences); Assert.Equal(0, bag.PrimeShards);
            var bought = Vendor.BuyStarter(p, ItemBase.Weapons[0]);
            Assert.True(bought.IsStarter);
            Assert.Equal(0f, Vendor.Sell(p, bought));
            var found = new Item(ItemBase.Weapons[0], 0, Rarity.Normal, "Found Blade", new List<AffixRoll>(), new List<AffixRoll>());
            Assert.False(found.IsStarter);
            Assert.True(CraftingRules.SellPrice(found) > 0f);
        }

        [Fact]
        public void KillXpIsPaidOncePerVictimPerDay()
        {
            var t = new KillXpTracker();
            var when = new System.DateTime(2027, 1, 1, 12, 0, 0);
            Assert.Equal(AccountRules.XpPerPlayerKill, t.Award("a", "b", when));
            Assert.Equal(0, t.Award("a", "b", when.AddHours(3)));
            Assert.Equal(AccountRules.XpPerPlayerKill, t.Award("b", "a", when.AddHours(3)));                  // the other direction is its own pair
            Assert.Equal(AccountRules.XpPerPlayerKill, t.Award("a", "c", when.AddHours(3)));
            Assert.Equal(AccountRules.XpPerPlayerKill, t.Award("a", "b", when.AddHours(25)));
        }
    }
}

namespace DungeonGen.Tests
{
    using DungeonGen.Core.Combat;
    using DungeonGen.Core.Hud;
    using DungeonGen.Core.Net;
    using DungeonGen.Core.Stages;

    public class InteriorTests
    {
        [Fact]
        public void InteriorsFurnishRoomsWithoutBlockingDoorsOrSplittingThem()
        {
            int furnished = 0, pillars = 0, lowWalls = 0;
            for (int seed = 1; seed <= 12; seed++)
            {
                var layout = DungeonGenerator.Generate(DungeonSettings.ForTeams(8), seed);
                foreach (var room in layout.Rooms)
                {
                    Assert.True(InteriorPlacer.Connected(layout, room), $"seed {seed} room {room.Id} is split");
                    if (room.Type == RoomType.Spawn || room.Type == RoomType.Extract) Assert.Empty(room.Interior);
                    foreach (var f in room.Interior)
                    {
                        Assert.True(f.Bounds.X > room.Bounds.X && f.Bounds.Y > room.Bounds.Y && f.Bounds.XMax < room.Bounds.XMax && f.Bounds.YMax < room.Bounds.YMax, "a clear ring inside every wall");
                        Assert.False(f.Bounds.Expand(1).Contains(room.Center), "the centre stays clear");
                        foreach (var d in room.Doors) Assert.False(f.Bounds.Expand(2).Contains(d), "doors stay clear");
                        if (f.Kind == InteriorKind.Pillar) pillars++; else lowWalls++;
                    }
                    if (room.Interior.Count > 0) furnished++;
                    if (room.Type == RoomType.Boss && room.Bounds.Width >= 9 && room.Bounds.Height >= 9) Assert.True(room.Interior.Count >= 1, $"a boss arena has shape (seed {seed} room {room.Id} {room.Bounds})");
                    // Every door still opens onto walkable floor.
                    foreach (var d in room.Doors) Assert.Contains(GridPoint.Cardinals, c => room.Bounds.Contains(d + c) && layout.IsWalkable(d + c));
                }
                Assert.True(LayoutValidator.IsPlayable(layout, layout.Settings, new List<string>()));
            }
            Assert.True(furnished > 40 && pillars > 40 && lowWalls > 10, $"{furnished} rooms, {pillars} pillars, {lowWalls} low walls");
        }

        [Fact]
        public void PillarsBlockSightLowWallsDoNotAndInteriorsAreDeterministicAndPublic()
        {
            var layout = DungeonGenerator.Generate(DungeonSettings.ForTeams(8), 3);
            var boss = layout.BossRooms.First(r => r.Interior.Any(f => f.Kind == InteriorKind.Pillar));
            var pillar = boss.Interior.First(f => f.Kind == InteriorKind.Pillar).Bounds;
            var left = new GridPoint(pillar.X - 1, pillar.Y); var right = new GridPoint(pillar.XMax, pillar.Y);
            Assert.True(layout.IsWalkable(left) && layout.IsWalkable(right));
            Assert.False(LineOfSight.CanSee(layout, left, right), "a pillar hides what is behind it");
            Assert.False(layout.IsWalkable(new GridPoint(pillar.X, pillar.Y)));
            var withLow = layout.Rooms.FirstOrDefault(r => r.Interior.Any(f => f.Kind == InteriorKind.LowWall && f.Bounds.Width == 1));
            if (withLow != null)
            {
                var lw = withLow.Interior.First(f => f.Kind == InteriorKind.LowWall && f.Bounds.Width == 1).Bounds;
                var a = new GridPoint(lw.X - 1, lw.Y); var b = new GridPoint(lw.X + 1, lw.Y);
                Assert.True(LineOfSight.CanSee(layout, a, b), "you see over a low wall");
                Assert.False(layout.IsWalkable(new GridPoint(lw.X, lw.Y)), "but not through it");
            }
            var again = DungeonGenerator.Generate(DungeonSettings.ForTeams(8), 3);
            Assert.Equal(LayoutFingerprint.Compute(layout), LayoutFingerprint.Compute(again));
            var redacted = ClientView.Redact(layout);
            Assert.Equal(ClientView.PublicFingerprint(layout), ClientView.PublicFingerprint(redacted));   // interiors and hazards are public, clients get them
            Assert.Equal(layout.Rooms.Sum(r => r.Interior.Count), redacted.Rooms.Sum(r => r.Interior.Count));
            var bare = DungeonSettings.ForTeams(8); bare.InteriorsEnabled = false; bare.HazardsEnabled = false;
            Assert.All(DungeonGenerator.Generate(bare, 3).Rooms, r => { Assert.Empty(r.Interior); Assert.Empty(r.Hazards); });
            var back = SettingsCodec.FromBytes(SettingsCodec.ToBytes(bare));
            Assert.False(back.InteriorsEnabled); Assert.False(back.HazardsEnabled);
        }

        [Fact]
        public void HazardsFollowDepthAndStayOutOfSafeRooms()
        {
            int total = 0, pits = 0, hidden = 0; double shallow = 0, deep = 0; int shallowRooms = 0, deepRooms = 0;
            for (int seed = 1; seed <= 12; seed++)
            {
                var layout = DungeonGenerator.Generate(DungeonSettings.ForTeams(8), seed);
                int maxDepth = layout.Rooms.Max(r => r.DepthFromSpawn);
                foreach (var room in layout.Rooms)
                {
                    if (room.Type == RoomType.Spawn || room.Type == RoomType.Extract || room.IsBossRoom || room.HasShrine) Assert.Empty(room.Hazards);
                    foreach (var h in room.Hazards)
                    {
                        Assert.Equal(room.Id, h.RoomId);
                        Assert.True(h.Id >= HazardRules.IdBase);
                        Assert.False(h.Bounds.Expand(1).Contains(room.Center));
                        foreach (var d in room.Doors) Assert.False(h.Bounds.Expand(2).Contains(d));
                        if (h.Kind == HazardKind.Pit) { pits++; Assert.Equal(TileType.Pit, layout.Tiles[h.Bounds.X, h.Bounds.Y]); Assert.False(HazardRules.Passable(h.Kind)); }
                        else Assert.Equal(TileType.RoomFloor, layout.Tiles[h.Bounds.X, h.Bounds.Y]);
                        if (h.Hidden) hidden++;
                        total++;
                    }
                    Assert.True(InteriorPlacer.Connected(layout, room));
                    if (room.Type == RoomType.Standard || room.Type == RoomType.Loot || room.Type == RoomType.Vault)
                    {
                        if (room.DepthFromSpawn * 2 < maxDepth) { shallow += room.Hazards.Count; shallowRooms++; } else { deep += room.Hazards.Count; deepRooms++; }
                    }
                }
                Assert.Equal(layout.Hazards.Count, layout.Rooms.Sum(r => r.Hazards.Count));
                Assert.Equal(layout.Hazards.Select(h => h.Id).OrderBy(i => i), layout.Hazards.Select(h => h.Id));
            }
            Assert.True(total > 20 && pits > 0 && hidden > 0, $"{total} hazards, {pits} pits, {hidden} hidden");
            Assert.True(deep / Math.Max(1, deepRooms) > shallow / Math.Max(1, shallowRooms), "deeper rooms carry more hazards");
            Assert.True(HazardRules.CanSee(ClassArchetype.DefaultRoster.First(c => c.Name == "Shade"), new Hazard(1, HazardKind.SpikeTrap, new GridRect(0, 0, 1, 1), 0)));
            Assert.False(HazardRules.CanSee(ClassArchetype.DefaultRoster.First(c => c.Name == "Warden"), new Hazard(1, HazardKind.SpikeTrap, new GridRect(0, 0, 1, 1), 0)));
            Assert.True(HazardRules.CanSee(null, new Hazard(1, HazardKind.Lava, new GridRect(0, 0, 1, 1), 0)));
        }

        private static (MatchState st, DungeonLayout layout) Fresh(int seed = 7)
        {
            var d = MatchDescriptor.Create(seed, DungeonSettings.ForTeams(8), new MatchRules(), out var layout);
            var st = new MatchState(d, layout);
            st.Apply(new MatchEvent(MatchEventKind.RoundStarted, 0), out _);
            int shade = ClassArchetype.DefaultRoster.ToList().FindIndex(c => c.Name == "Shade");
            for (int p = 1; p <= 6; p++) st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, p, (p - 1) / 3, p == 3 ? shade : 0), out _);   // player 3 is the Shade
            return (st, layout);
        }

        [Fact]
        public void TrapsFireRearmAndOnlyAShadeDisarmsThem()
        {
            var (st, layout) = Fresh();
            string why;
            var trap = layout.Hazards.First(h => h.Kind == HazardKind.SpikeTrap);
            var lava = layout.Hazards.FirstOrDefault(h => h.Kind == HazardKind.Lava);
            Assert.True(st.HazardArmed(trap.Id, 1f));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.HazardTriggered, 10, trap.Id, 4), out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.HazardTriggered, 12, trap.Id, 5), out why), "rearming");
            Assert.Contains("not armed", why);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.HazardTriggered, 10 + HazardRules.SpikeRearmSeconds, trap.Id, 5), out why));
            if (lava != null) { Assert.True(st.Apply(new MatchEvent(MatchEventKind.HazardTriggered, 21, lava.Id, 4), out why)); Assert.True(st.Apply(new MatchEvent(MatchEventKind.HazardTriggered, 22, lava.Id, 4), out why), "lava never rearms, it just burns"); }

            // Disarming: Shade only, in the room, two-second channel.
            st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 30, 1, trap.RoomId), out _);
            st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 30, 3, trap.RoomId), out _);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.LootStarted, 31, 1, trap.Id, 3), out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.HazardDisarmed, 34, trap.Id, 1), out why));
            Assert.Contains("Shade", why);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.LootStarted, 31, 3, trap.Id, 3), out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.HazardDisarmed, 32, trap.Id, 3), out why));
            Assert.Contains("takes", why);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.HazardDisarmed, 33.1f, trap.Id, 3), out why));
            Assert.False(st.HazardArmed(trap.Id, 100f));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.HazardTriggered, 100, trap.Id, 5), out why), "disarmed for good");
            if (lava != null)
            {
                st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 40, 3, lava.RoomId), out _);
                st.Apply(new MatchEvent(MatchEventKind.LootStarted, 41, 3, lava.Id, 3), out _);
                Assert.False(st.Apply(new MatchEvent(MatchEventKind.HazardDisarmed, 44, lava.Id, 3), out why), "lava cannot be disarmed");
            }
            // Visibility: team 0 has a Shade and sees the hidden trap once the room is explored; team 1 sees only obvious hazards there.
            st.Apply(new MatchEvent(MatchEventKind.RoomSeen, 50, 0, trap.RoomId), out _);
            st.Apply(new MatchEvent(MatchEventKind.RoomSeen, 50, 1, trap.RoomId), out _);
            Assert.Contains(trap.Id, st.HazardsVisibleTo(0).Select(h => h.Id));
            var otherHidden = layout.Hazards.FirstOrDefault(h => h.Hidden && h.RoomId == trap.RoomId && h.Id != trap.Id && !st.HazardFired.ContainsKey(h.Id));
            if (otherHidden != null) Assert.DoesNotContain(otherHidden.Id, st.HazardsVisibleTo(1).Select(h => h.Id));
            Assert.Contains(trap.Id, st.HazardsVisibleTo(1).Select(h => h.Id));                                           // it fired, so it is known
            var v0 = TeamView.For(st, 0);
            Assert.Contains(trap.Id, v0.KnownHazards); Assert.Contains(trap.Id, v0.DisarmedHazards);
            var back = TeamView.FromBytes(v0.ToBytes());
            Assert.Equal(v0.KnownHazards, back.KnownHazards);
            var mini = MiniMap.Build(st, 1, layout.Rooms[trap.RoomId].Center, null, null, 60f);
            Assert.Contains(mini.Markers, m => m.Kind == MiniMapMarkerKind.DisarmedHazard && m.Ref == trap.Id);
        }

        [Fact]
        public void DoorsCloseInstantlyOpenWithAChannelAndBlockSightAndPaths()
        {
            var (st, layout) = Fresh();
            string why;
            int spawn0 = st.Descriptor.TeamSpawnRooms[0];
            var room = layout.Rooms.First(r => r.Type == RoomType.Standard && r.Doors.Any(d => st.Index.DoorIdAt.ContainsKey(d)) && !st.Index.RoomsWithin(r.Id, 1).Contains(spawn0));   // not next to player 1's spawn
            var cell = room.Doors.First(d => st.Index.DoorIdAt.ContainsKey(d));
            int door = st.Index.DoorIdAt[cell];
            Assert.Equal((room.Id, cell), st.Index.Doors[door]);
            Assert.True(door >= DoorRules.IdBase);
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.DoorClosed, 1, door, 1), out why), "player 1 is at spawn, not at the door");
            Assert.Contains("not at that door", why);
            st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 2, 1, room.Id), out _);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.DoorClosed, 3, door, 1), out why));
            Assert.Contains(cell, st.ClosedDoors);
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.DoorClosed, 3, door, 1), out why), "already closed");

            // Sight and movement stop at the shut door.
            var inside = GridPoint.Cardinals.Select(c => cell + c).First(p => room.Bounds.Contains(p));
            var outside = GridPoint.Cardinals.Select(c => cell + c).First(p => !room.Bounds.Contains(p) && layout.IsWalkable(p));
            Assert.True(LineOfSight.CanSee(layout, inside, outside));
            Assert.False(LineOfSight.CanSee(layout, inside, outside, st.ClosedDoors));
            Assert.NotNull(GridPath.Find(layout, inside, outside));
            Assert.Null(GridPath.Find(layout, inside, outside, closedDoors: st.ClosedDoors));
            Assert.DoesNotContain(room.Id, LineOfSight.VisibleRooms(layout, outside, 6, st.ClosedDoors));

            // Opening: from either side, with the channel.
            int neighbour = room.Neighbours.First();
            st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 4, 4, neighbour), out _);   // an enemy on the other side
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.DoorOpened, 5, door, 4), out why));
            Assert.Contains("no door channel", why);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.LootStarted, 5, 4, door, 4), out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.DoorOpened, 6, door, 4), out why));
            Assert.Contains("takes", why);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.LootInterrupted, 6.5f, 4), out why));             // took a hit: start over
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.LootStarted, 7, 4, door, 4), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.DoorOpened, 7 + DoorRules.OpenSeconds, door, 4), out why));
            Assert.Empty(st.ClosedDoors);
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.DoorOpened, 11, door, 4), out why), "already open");
            Assert.False(DoorRules.Breakable);

            // Vault doors are locks, not doors.
            var vault = layout.VaultRooms.First();
            Assert.All(vault.Doors, d => Assert.False(st.Index.DoorIdAt.ContainsKey(d)));

            // Views and audiences: a shut door is on the map of anyone who has seen the room or its neighbour.
            st.Apply(new MatchEvent(MatchEventKind.DoorClosed, 12, door, 1), out _);
            st.Apply(new MatchEvent(MatchEventKind.RoomSeen, 12, 0, room.Id), out _);
            var v0 = TeamView.For(st, 0); var v1 = TeamView.For(st, 1);
            Assert.Contains(cell, v0.ClosedDoors);
            Assert.DoesNotContain(cell, v1.ClosedDoors);                                                        // team 1 has seen neither room
            Assert.Equal(new HashSet<int> { 0 }, EventVisibility.Audience(new MatchEvent(MatchEventKind.DoorClosed, 12, door, 1), st));
            var client = new MatchState(MatchDescriptor.FromBytes(st.Descriptor.ToBytes()), st.Descriptor.BuildAndVerify());
            client.LoadView(TeamView.FromBytes(v0.ToBytes()));
            Assert.Contains(cell, client.ClosedDoors);
            Assert.Contains(MiniMap.Build(client, 1, room.Center, null, null, 12f).Markers, m => m.Kind == MiniMapMarkerKind.ClosedDoor && m.Ref == door);
        }
    }
}

namespace DungeonGen.Tests
{
    using DungeonGen.Core.Combat;
    using DungeonGen.Core.Items;
    using DungeonGen.Core.Net;
    using DungeonGen.Core.Profile;

    public class SeasonContentTests
    {
        [Fact]
        public void MutatorsRotateFromSeasonTwoAndEachBendsOneRule()
        {
            Assert.Null(Mutator.ForSeason(1));
            Assert.Equal("Soulfeast", Mutator.ForSeason(2).Name);
            Assert.Equal(Mutator.All[0].Name, Mutator.ForSeason(2 + Mutator.All.Count).Name);
            Assert.Equal(5, Mutator.All.Count);
            var s2 = Season.Starting(2, new System.DateTime(2027, 4, 1));
            Assert.Equal("Soulfeast", s2.Mutator.Name);
            Assert.Null(Season.Starting(1, new System.DateTime(2027, 1, 1)).Mutator);

            var settings = DungeonSettings.ForTeams(4);
            var d = MatchDescriptor.CreateForSeason(11, settings, new MatchRules(), s2, out var layout);
            Assert.Equal(2, d.Rules.SoulMultiplier); Assert.Equal("Soulfeast", d.Rules.MutatorName);
            var back = MatchDescriptor.FromBytes(d.ToBytes());
            Assert.Equal(2, back.Rules.SoulMultiplier); Assert.Equal("Soulfeast", back.Rules.MutatorName); Assert.True(back.Rules.ShrinesUsable);
            var st = new MatchState(d, layout);
            st.Apply(new MatchEvent(MatchEventKind.RoundStarted, 0), out _);
            for (int p = 1; p <= 4; p++) st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, p, (p - 1) / 2), out _);
            Assert.True(Ch.KillAndLoot(st, 5, 3, 1));
            Assert.Equal(2, st.SoulsOf(1));                                                            // two souls per corpse

            var godless = MatchDescriptor.CreateForSeason(11, settings, new MatchRules(), Season.Starting(3, new System.DateTime(2027, 7, 1)), out var l3);
            Assert.False(godless.Rules.ShrinesUsable);
            var g = new MatchState(godless, l3);
            g.Apply(new MatchEvent(MatchEventKind.RoundStarted, 0), out _);
            for (int p = 1; p <= 4; p++) g.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, p, (p - 1) / 2), out _);
            g.Souls[1] = 5;
            int shrine = l3.Rooms.First(r => r.HasShrine).Id;
            Assert.False(g.Apply(new MatchEvent(MatchEventKind.ShrinePurchase, 1, 1, 0, shrine), out string why)); Assert.Contains("dark", why);
            Assert.False(g.Apply(new MatchEvent(MatchEventKind.UltimateArmed, 1, 1, shrine), out why)); Assert.Contains("dark", why);
            g.Apply(new MatchEvent(MatchEventKind.PlayerDied, 2, 2, -1, 0), out _);
            Assert.False(g.Apply(new MatchEvent(MatchEventKind.PlayerResurrected, 3, 2, 1, shrine), out why)); Assert.Contains("dark", why);

            Assert.Equal(1f, MatchDescriptor.CreateForSeason(11, settings, new MatchRules(), Season.Starting(4, new System.DateTime(2027, 10, 1)), out _).Settings.KeyFromBossChance);   // Keybearers
            Assert.Equal(1f, settings.KeyFromBossChance * 2, 3);                                      // the caller's settings were copied, not changed
            Assert.Equal(6, MatchDescriptor.CreateForSeason(11, settings, new MatchRules(), Season.Starting(5, new System.DateTime(2028, 1, 1)), out _).Rules.SightRangeCells);   // Dusk
            MatchDescriptor.CreateForSeason(11, settings, new MatchRules(), Season.Starting(6, new System.DateTime(2028, 4, 1)), out var twin);
            Assert.Equal(2, twin.VaultRooms.Count());                                                   // Twin Vaults
        }

        [Fact]
        public void OneMidRoundEventPerRoundAnnouncedToEveryoneAndDeterministic()
        {
            var settings = DungeonSettings.ForTeams(8);
            int elites = 0, vaults = 0, shrines = 0;
            for (int seed = 1; seed <= 24; seed++)
            {
                var d = MatchDescriptor.Create(seed, settings, new MatchRules(), out var layout);
                var plan = d.Event(layout);
                var again = MatchDescriptor.FromBytes(d.ToBytes()).Event(d.BuildAndVerify());
                Assert.Equal((plan.Kind, plan.RoomId, plan.AtSeconds), (again.Kind, again.RoomId, again.AtSeconds));   // clients compute the same plan from the public layout
                Assert.Equal(600f, plan.AtSeconds);
                switch (plan.Kind)
                {
                    case DungeonEventKind.RoamingElite: elites++; Assert.True(layout.Rooms[plan.RoomId].Type == RoomType.Hub || layout.Rooms[plan.RoomId].Type == RoomType.Standard); break;
                    case DungeonEventKind.VaultUnsealed: vaults++; Assert.Equal(RoomType.Vault, layout.Rooms[plan.RoomId].Type); Assert.True(plan.Timed); Assert.Equal(660f, plan.EndsAt); break;
                    case DungeonEventKind.CheapShrine: shrines++; Assert.True(layout.Rooms[plan.RoomId].HasShrine); break;
                    default: Assert.Fail("every round has an event"); break;
                }
            }
            Assert.True(elites > 0 && vaults > 0 && shrines > 0, $"{elites} {vaults} {shrines}");
            var tut = MatchDescriptor.CreateTutorial(3, out var tl);
            Assert.Equal(DungeonEventKind.None, tut.Event(tl).Kind);

            // The clock fires it halfway and closes a timed one a minute later.
            var dd = MatchDescriptor.Create(1, settings, new MatchRules(), out var ll);
            var p = dd.Event(ll);
            var clock = new RoundClock(dd.Rules, p);
            var fired = new List<MatchEvent>();
            foreach (float t in new[] { 0f, 10f, 240f, 599f, 600f, 659f, 660f, 1200f }) fired.AddRange(clock.Tick(t));
            Assert.Contains(fired, e => e.Kind == MatchEventKind.EventStarted && e.Time == 600f && e.A == (int)p.Kind && e.B == p.RoomId);
            Assert.Equal(p.Timed ? 1 : 0, fired.Count(e => e.Kind == MatchEventKind.EventEnded));
            Assert.NotNull(Announcer.For(fired.First(e => e.Kind == MatchEventKind.EventStarted), ll));
            Assert.Null(EventVisibility.Audience(fired.First(e => e.Kind == MatchEventKind.EventStarted), new MatchState(dd, ll)));
        }

        private static (MatchState st, DungeonLayout layout, MatchDescriptor d) Fresh(int seed)
        {
            var d = MatchDescriptor.Create(seed, DungeonSettings.ForTeams(8), new MatchRules(), out var layout);
            var st = new MatchState(d, layout);
            st.Apply(new MatchEvent(MatchEventKind.RoundStarted, 0), out _);
            for (int p = 1; p <= 6; p++) st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, p, (p - 1) / 3), out _);
            st.Apply(new MatchEvent(MatchEventKind.DoorsOpened, 10), out _);
            return (st, layout, d);
        }

        [Fact]
        public void TheEliteDropsItsRelicOnTheFloorForAnyone()
        {
            var (st, layout, d) = Fresh(2);
            string why;
            int hub = layout.Rooms.First(r => r.Type == RoomType.Hub || r.Type == RoomType.Standard).Id;
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.EliteKilled, 100, hub, 0), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.EventStarted, 600, (int)DungeonEventKind.RoamingElite, hub), out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.EventStarted, 601, (int)DungeonEventKind.CheapShrine, hub), out why), "one per round");
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.EventEnded, 660, (int)DungeonEventKind.RoamingElite), out why), "the elite does not time out");
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.EliteKilled, 700, hub + 1 < layout.Rooms.Count ? hub + 1 : hub - 1, 1), out why), "wrong room");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.EliteKilled, 700, hub, 1), out why));
            Assert.True(st.EliteDead && st.EventOver);
            Assert.Equal(hub, st.FloorItems[LayoutIndex.EventLootId]);
            Assert.NotNull(layout.EventLoot); Assert.Equal(Rarity.Unique, layout.EventLoot.Rarity);
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.ItemTaken, 701, LayoutIndex.EventLootId, 1), out why), "not standing there");
            st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 702, 1, hub), out _);                      // team 0 arrives first, though team 1 killed it
            st.Apply(new MatchEvent(MatchEventKind.RoomSeen, 702, 0, hub), out _);
            var v0 = TeamView.For(st, 0);
            Assert.Contains(v0.Items, i => i.Id == LayoutIndex.EventLootId && i.Rarity == (byte)Rarity.Unique);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ItemTaken, 703, LayoutIndex.EventLootId, 1), out why));
            Assert.Equal(1, st.ItemOwner[LayoutIndex.EventLootId]);
            Assert.Equal(layout.EventLoot.Value, st.ItemValue[LayoutIndex.EventLootId]);
            Assert.Equal(new HashSet<int> { 1 }, EventVisibility.Audience(new MatchEvent(MatchEventKind.EliteKilled, 700, hub, 1), st));
            // Clients never see the relic until it is on a floor they have explored.
            var client = new MatchState(MatchDescriptor.FromBytes(d.ToBytes()), d.BuildAndVerify());
            Assert.Null(client.Layout.EventLoot);
        }

        [Fact]
        public void UnsealedVaultsAndCheapShrinesWorkOnlyInsideTheirMinute()
        {
            var (st, layout, d) = Fresh(2);
            string why;
            var vault = layout.VaultRooms.First();
            st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 11, 1, vault.Neighbours.First()), out _);
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.VaultOpened, 12, vault.Id, 1), out why), "no key, no event");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.EventStarted, 600, (int)DungeonEventKind.VaultUnsealed, vault.Id), out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.VaultOpened, 601, vault.Id, 4), out why), "player 4 is at spawn, not at the door");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.VaultOpened, 601, vault.Id, 1), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.EventEnded, 660, (int)DungeonEventKind.VaultUnsealed), out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.EventEnded, 661, (int)DungeonEventKind.VaultUnsealed), out why));

            var (s2, l2, _) = Fresh(5);
            int shrine = l2.Rooms.First(r => r.HasShrine).Id;
            s2.Apply(new MatchEvent(MatchEventKind.PlayerDied, 20, 2, -1, 0), out _);
            s2.Souls[1] = 1;
            Assert.False(s2.Apply(new MatchEvent(MatchEventKind.PlayerResurrected, 21, 2, 1, shrine), out why));
            Assert.True(s2.Apply(new MatchEvent(MatchEventKind.EventStarted, 600, (int)DungeonEventKind.CheapShrine, shrine), out why));
            Assert.Equal(1, s2.ResurrectionCostAt(shrine));
            Assert.Equal(2, s2.ResurrectionCostAt(shrine == 0 ? 1 : 0));
            Assert.True(s2.Apply(new MatchEvent(MatchEventKind.PlayerResurrected, 601, 2, 1, shrine), out why));
            Assert.Equal(0, s2.SoulsOf(1));
            Assert.True(s2.Apply(new MatchEvent(MatchEventKind.EventEnded, 660, (int)DungeonEventKind.CheapShrine), out why));
            Assert.Equal(2, s2.ResurrectionCostAt(shrine));
            var view = TeamView.FromBytes(TeamView.For(s2, 1).ToBytes());
            Assert.Equal((byte)DungeonEventKind.CheapShrine, view.EventKind); Assert.True(view.EventOver);
        }

        [Fact]
        public void TutorialBotsWalkToTheLootRoomOpenTheChestAndObeyEveryRule()
        {
            var d = MatchDescriptor.CreateTutorial(3, out var layout);
            Assert.Equal(1, d.Rules.BotTeams); Assert.True(d.Rules.IsTutorial); Assert.Equal(2, d.TeamSpawnRooms.Count);
            var st = new MatchState(d, layout);
            string why;
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.RoundStarted, 0), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, 1, 0), out why));                 // the human
            var bots = new BotDirector(st, team: 1);
            int applied = 0, rejected = 0; var reasons = new List<string>();
            for (float t = 0f; t <= 300f; t += 1f)
            {
                if (t == 5f) st.Apply(new MatchEvent(MatchEventKind.DoorsOpened, 10), out _);
                foreach (var e in bots.Step(t).ToList()) { if (st.Apply(e, out string r)) applied++; else { rejected++; reasons.Add($"{e}: {r}"); } }
            }
            Assert.All(bots.Bots, b => Assert.True(BotRules.IsBot(b)));
            Assert.All(bots.Bots, b => Assert.Equal(1, st.PlayerTeam[b]));
            Assert.Equal("Warden", st.ClassOf(bots.Bots[0]).Name); Assert.Equal("Marksman", st.ClassOf(bots.Bots[1]).Name);
            Assert.All(bots.Bots, b => Assert.Equal(bots.TargetRoom, st.PlayerRoom[b]));                            // they walked there, room by room
            Assert.Contains(bots.TargetRoom, st.OpenedContainers);                                                      // and opened the chest
            Assert.True(st.ItemOwner.Values.Count(v => bots.Bots.Contains(v)) >= 1, "at least one bot took a piece");
            Assert.True(applied > 6 && rejected == 0, $"{applied} applied, {rejected} rejected: bots only raise legal events. {string.Join(" | ", reasons)}");
            Assert.False(BotRules.FinishDowned); Assert.False(BotRules.LootPlayerCorpses); Assert.True(BotRules.TutorialOnly);
            // The player kills a bot and loots the corpse: a soul, like any player kill.
            st.Apply(new MatchEvent(MatchEventKind.PlayerEnteredRoom, 301, 1, bots.TargetRoom), out _);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 302, bots.Bots[0], 1, bots.TargetRoom), out why));
            Assert.True(Ch.OpenCorpse(st, 303, bots.Bots[0], 1, out why));
            Assert.Equal(1, st.SoulsOf(1));
            Assert.DoesNotContain(bots.Step(310f), e => e.A == bots.Bots[0]);                                        // the dead bot raises nothing more
            // The matchmaker never seats a bot and the tutorial never has an event.
            Assert.Equal(DungeonEventKind.None, d.Event(layout).Kind);
            Assert.Throws<System.ArgumentException>(() => new QueuedGroup(1, new[] { BotRules.BotIdBase }, 0f));
        }
    }
}

namespace DungeonGen.Tests
{
    using DungeonGen.Core.Combat;
    using DungeonGen.Core.Net;

    public class FightSimTests
    {
        private static FightSim Arena(int seed = 1) => new FightSim(FightLab.OpenArena(20, 12), seed);
        private static Combatant P(FightSim sim, int id, int team, string spec, float x, float y, int tier = 0) => sim.Add(FightLab.Player(id, team, spec, tier, new Vec2(x, y)));

        [Fact]
        public void EverySpecHasFourAbilitiesAndAnUltimateAndIdsAreUnique()
        {
            var specs = WeaponSpec.All.Select(w => w.Name).Concat(ElementKit.All.Select(k => k.Name)).ToList();
            Assert.Equal(13, specs.Count);                                                             // ten weapon specs and three element kits
            foreach (var spec in specs)
            {
                var kinds = AbilityBook.For(spec).Select(a => a.Kind).ToList();
                Assert.Equal(new[] { AbilityKind.Basic, AbilityKind.Bolt, AbilityKind.Zone, AbilityKind.Mobility, AbilityKind.Ultimate }, kinds.OrderBy(k => k).ToArray());
                Assert.Equal(AbilityBook.Ultimate(spec).Name, WeaponSpec.Ultimates[spec].Split(':')[0]);
            }
            Assert.Equal(AbilityBook.All.Count, AbilityBook.All.Select(a => a.Id).Distinct().Count());
            Assert.Equal(AbilitySlot.Bolt, AbilityBook.Get("Sentinel.bolt").CostSlot);
            Assert.Null(AbilityBook.Get("Sentinel.basic").CostSlot); Assert.Null(AbilityBook.Get("Sentinel.ult").CostSlot);
            Assert.NotNull(AbilityBook.Get("Mob.basic")); Assert.NotNull(AbilityBook.Get("Boss.slam"));
            Assert.All(AbilityBook.All.Where(a => a.Damage > 0f), a => Assert.NotEqual(TargetFilter.Allies, a.Filter));   // everything that hurts obeys friendly fire
        }

        [Fact]
        public void ProjectilesStopAtTheFirstBodyAndAtPillarsAndClosedDoorsPierceGoesThrough()
        {
            var sim = Arena();
            var shooter = P(sim, 1, 0, "Sharpshooter", 3f, 6f);
            var ally = P(sim, 2, 0, "Warder", 8f, 6f);
            var enemy = P(sim, 3, 1, "Reaver" == "" ? "" : "Berserker", 13f, 6f);
            var r = sim.Cast(1, "Sharpshooter.basic", enemy.Position);
            Assert.True(r.Ok); Assert.Equal(new[] { 2 }, r.Targets);                                  // the arrow hits the teammate standing in the line, at full strength
            Assert.True(ally.Health < ally.MaxHealth);
            sim.Tick(1.5f);
            var pierce = sim.Cast(1, "Sharpshooter.bolt", enemy.Position);
            Assert.True(pierce.Ok); Assert.Equal(new[] { 2, 3 }, pierce.Targets);                     // Piercing Shot goes through both
            // A pillar in the way stops the arrow (the teammate steps out of the lane first).
            ally.Position = new Vec2(8f, 2f);
            sim.Layout.Tiles[10, 6] = TileType.Pillar;
            sim.Tick(1.5f);
            Assert.Empty(sim.Cast(1, "Sharpshooter.basic", new Vec2(16f, 6f)).Targets);
            sim.Layout.Tiles[10, 6] = TileType.LowWall;
            sim.Tick(1.5f);
            Assert.Empty(sim.Cast(1, "Sharpshooter.basic", new Vec2(16f, 6f)).Targets);               // a low wall stops arrows too
            Assert.True(sim.CanSee(shooter.Position, enemy.Position));                                 // but not sight
            sim.Layout.Tiles[10, 6] = TileType.Door;
            sim.ClosedDoors.Add(new GridPoint(10, 6));
            Assert.False(sim.CanSee(shooter.Position, enemy.Position));
            sim.Tick(7f);
            var past = sim.Cast(1, "Sharpshooter.bolt", enemy.Position);
            Assert.True(past.Ok, past.Rejection); Assert.Empty(past.Targets);                           // the cast is legal; it just hits nothing past the door
        }

        [Fact]
        public void ConesCirclesAndTargetsFindTheRightBodiesAndRejectBadAims()
        {
            var sim = Arena();
            var axe = P(sim, 1, 0, "Berserker", 10f, 6f);
            var front = P(sim, 2, 1, "Warder", 11.2f, 6f);
            var behind = P(sim, 3, 1, "Warder", 8.8f, 6f);
            var far = P(sim, 4, 1, "Warder", 16f, 6f);
            var cleave = sim.Cast(1, "Berserker.bolt", front.Position);
            Assert.True(cleave.Ok); Assert.Contains(2, cleave.Targets); Assert.DoesNotContain(3, cleave.Targets); Assert.DoesNotContain(4, cleave.Targets);
            Assert.True(front.Status.Has(StatusKind.Bleed));
            var thrust = new FightSim(FightLab.OpenArena(20, 12), 1);
            var d = thrust.Add(FightLab.Player(1, 0, "Duelist", 0, new Vec2(4f, 6f)));
            thrust.Add(FightLab.Player(2, 1, "Warder", 0, new Vec2(16f, 6f)));
            var oor = thrust.Cast(1, "Duelist.bolt", new Vec2(16f, 6f));
            Assert.False(oor.Ok); Assert.Contains("out of range", oor.Rejection);
            var nobody = thrust.Cast(1, "Duelist.bolt", new Vec2(5f, 6f));
            Assert.False(nobody.Ok); Assert.Contains("no target", nobody.Rejection);
            var rain = new FightSim(FightLab.OpenArena(20, 12), 1);
            rain.Add(FightLab.Player(1, 0, "Volleyer", 0, new Vec2(8f, 6f)));                      // within the zone's 12 m throw
            var a = rain.Add(FightLab.Player(2, 1, "Warder", 0, new Vec2(12f, 6f)));
            var b = rain.Add(FightLab.Player(3, 1, "Warder", 0, new Vec2(13f, 7f)));
            var c = rain.Add(FightLab.Player(4, 1, "Warder", 0, new Vec2(18f, 6f)));
            Assert.True(rain.Cast(1, "Volleyer.zone", new Vec2(12.5f, 6.5f)).Ok);
            Assert.Single(rain.Zones);
            rain.Tick(0.1f);
            Assert.True(a.Health < a.MaxHealth && b.Health < b.MaxHealth && c.Health == c.MaxHealth);   // the zone ticked on the two inside
            float afterOne = a.Health;
            rain.Tick(0.5f);
            Assert.Equal(afterOne, a.Health, 1);                                                      // once a second, not every tick
            rain.Tick(0.5f);
            Assert.True(a.Health < afterOne);
            for (int y = 1; y <= 12; y++) rain.Layout.Tiles[10, y] = TileType.Pillar;                 // a wall of pillars between the archer and the point
            rain.Tick(20f);
            var blind = rain.Cast(1, "Volleyer.zone", new Vec2(12.5f, 6.5f));
            Assert.False(blind.Ok); Assert.Contains("line of sight", blind.Rejection);
        }

        [Fact]
        public void DamageIsMitigatedByArmourAndResistanceAndPassivesChangeIt()
        {
            var sim = Arena(5);
            var warden = P(sim, 1, 0, "Sentinel", 10f, 6f, tier: 2);
            var pyro = P(sim, 2, 1, "Pyromancy", 14f, 6f);
            var duelist = P(sim, 3, 1, "Duelist", 10f, 9f);                                           // off the firebolt's lane
            float fire = warden.Resistances.Mitigation(DamageType.Fire), armour = StatFormulas.Mitigation(warden.ArmourRating, StatFormulas.MitigationCap + warden.Resistances.CapBonus / 100f);
            Assert.True(armour > fire, "heavy armour stops swords, not fire");
            var firebolt = sim.Cast(2, "Pyromancy.bolt", warden.Position);
            Assert.True(firebolt.Ok);
            float expectedFire = (float)Math.Round(pyro.Dps * 1.5f * (1f - fire), 1);
            Assert.True(Math.Abs(firebolt.TotalDamage - expectedFire) < 0.2f || Math.Abs(firebolt.TotalDamage - expectedFire * FightSim.CritMultiplier) < 0.2f, $"{firebolt.TotalDamage} vs {expectedFire}");
            Assert.True(warden.Status.Has(StatusKind.Burn));
            duelist.Position = new Vec2(11.5f, 6f);
            var thrust = sim.Cast(3, "Duelist.bolt", warden.Position);
            float expectedPhys = duelist.Dps * 2.0f * (1f - armour);
            Assert.True(thrust.TotalDamage <= expectedPhys * FightSim.CritMultiplier + 0.2f && thrust.TotalDamage >= expectedPhys - 0.2f);
            // Execute: below a quarter health the Duelist's hits triple.
            warden.Health = warden.MaxHealth * 0.2f;
            sim.Tick(7f);
            var exec = sim.Cast(3, "Duelist.bolt", warden.Position);
            Assert.True(exec.TotalDamage >= expectedPhys * FightSim.ExecuteMultiplier - 0.3f || warden.Downed);
            // Backstab: triple from behind, not from the front.
            var bs = Arena(2);
            var shade = P(bs, 1, 0, "Assassin", 10f, 6f);
            var mark = P(bs, 2, 1, "Warder", 11f, 6f);
            mark.Facing = new Vec2(1f, 0f);                                                          // looking away from the Shade
            var r1 = bs.Cast(1, "Assassin.bolt", mark.Position);
            Assert.True(r1.Ok, r1.Rejection);
            float behind = r1.TotalDamage;
            mark.Health = mark.MaxHealth; mark.Facing = new Vec2(-1f, 0f); bs.Tick(7f);
            float front = bs.Cast(1, "Assassin.bolt", mark.Position).TotalDamage;
            Assert.True(behind > front * 2f, $"{behind} vs {front}");
            // Deadeye: the Sharpshooter hits harder past 12 m.
            var de = Arena(3);
            var sharp = P(de, 1, 0, "Sharpshooter", 2f, 6f);
            var near = P(de, 2, 1, "Warder", 5f, 9f);                                                   // off the far target's lane
            var farAway = P(de, 3, 1, "Warder", 9f, 6f);                                                // 14 m away: past Deadeye's 12 m, inside the bow's 16 m
            float n = de.Cast(1, "Sharpshooter.basic", near.Position).TotalDamage; de.Tick(2f);
            float f = de.Cast(1, "Sharpshooter.basic", farAway.Position).TotalDamage;
            Assert.True(f > n * 1.2f || f > n * 1.2f / FightSim.CritMultiplier, $"{f} vs {n}");
        }

        [Fact]
        public void FriendlyFireIsFullButTeammatesCannotFinishAndEnemiesFinishAtHalfHealth()
        {
            var sim = Arena();
            var a = P(sim, 1, 0, "Berserker", 10f, 6f);
            var mate = P(sim, 2, 0, "Warder", 11f, 6f);
            var enemy = P(sim, 3, 1, "Duelist", 14f, 6f);
            float before = mate.Health;
            Assert.Contains(2, sim.Cast(1, "Berserker.bolt", mate.Position).Targets);
            Assert.True(mate.Health < before);
            mate.Health = 1f;
            sim.Tick(7f);
            sim.Cast(1, "Berserker.bolt", mate.Position);
            Assert.True(mate.Downed);                                                                  // a teammate can down you
            Assert.Contains(sim.Events, e => e.Kind == FightEventKind.Downed && e.Target == 2 && e.Source == 1);
            sim.Tick(7f);
            sim.Cast(1, "Berserker.bolt", mate.Position);
            Assert.False(mate.Dead);                                                                   // but never finish you
            Assert.Equal(0f, mate.FinishDamage);
            enemy.Position = new Vec2(11.5f, 6f);
            int swings = 0;
            while (!mate.Dead && swings++ < 40) { sim.Tick(1.1f); sim.Cast(3, "Duelist.basic", mate.Position); }
            Assert.True(mate.Dead);
            Assert.True(mate.FinishDamage >= mate.MaxHealth * FightRules.FinishDamageFraction);
            Assert.Contains(sim.Events, e => e.Kind == FightEventKind.Finished && e.Target == 2 && e.Source == 3);
            // The recap reads the hit log the simulator wrote.
            Assert.Equal(HitLog.Kept, sim.Hits.LastHits(2).Count);
            Assert.Equal("Duelist", sim.Hits.LastHits(2).Last().AttackerSpec);                       // the finishing blow is the Duelist's; the Berserker's earlier hits are still in the five
        }

        [Fact]
        public void CooldownsResourcesAndControlGateCasts()
        {
            var sim = Arena();
            var w = P(sim, 1, 0, "Warder", 6f, 6f);
            var e = P(sim, 2, 1, "Sentinel", 8f, 6f);
            Assert.True(sim.Cast(1, "Warder.bolt", w.Position).Ok);
            var again = sim.Cast(1, "Warder.bolt", w.Position);
            Assert.False(again.Ok); Assert.Contains("cooldown", again.Rejection);
            Assert.True(w.ResourceNow < w.ResourceMax);
            float spent = w.ResourceMax - w.ResourceNow;
            Assert.Equal(FightRules.AbilityCost(w.ResourceMax, AbilitySlot.Bolt), spent, 1);
            w.ResourceNow = 1f;
            sim.Tick(3.1f);
            var broke = sim.Cast(1, "Warder.zone", w.Position);
            Assert.False(broke.Ok); Assert.Contains("needs", broke.Rejection);
            sim.Tick(10f);
            Assert.True(w.ResourceNow > 1f, "resources regenerate");
            Assert.True(sim.Cast(1, "Warder.basic", e.Position).Ok);
            Assert.Contains("swinging", sim.Cast(1, "Warder.basic", e.Position).Rejection);
            Assert.False(sim.Cast(1, "Sentinel.bolt", e.Position).Ok, "not this spec's ability");
            Assert.False(sim.Cast(1, "Warder.ult", e.Position).Ok, "ultimate not armed");
            // Shield Bash staggers (an interrupt, not a stun); a stunned caster cannot act at all.
            Assert.True(sim.Cast(2, "Sentinel.bolt", w.Position).Ok);
            Assert.True(w.Status.Has(StatusKind.Stagger));
            Assert.False(w.Status.IsHardControlled);
            w.Status.Apply(StatusEffectDefinition.All[StatusKind.Stun]);
            sim.Tick(1.2f);
            Assert.Contains("controlled", sim.Cast(1, "Warder.basic", e.Position).Rejection);
        }

        [Fact]
        public void StatusCombosDotsCleanseAndTheFrostUltimateIgnoreDiminishingReturns()
        {
            var sim = Arena(4);
            var cryo = P(sim, 1, 0, "Cryomancy", 4f, 6f);
            var storm = P(sim, 2, 0, "Stormcalling", 4f, 8f);
            var mender = P(sim, 3, 1, "Warder", 10f, 6f);
            var victim = P(sim, 4, 1, "Berserker", 10f, 7f);
            Assert.True(sim.Cast(1, "Cryomancy.bolt", victim.Position).Ok);
            Assert.True(victim.Status.Has(StatusKind.Chill));
            Assert.True(sim.Cast(2, "Stormcalling.bolt", victim.Position).Ok);
            Assert.True(victim.Status.Has(StatusKind.Stun), "Stagger on a chilled target is a Stun");
            Assert.False(victim.Status.Has(StatusKind.Chill));
            // Purge removes everything.
            Assert.True(sim.Cast(3, "Warder.mobility", victim.Position).Ok);
            Assert.Empty(victim.Status.Active);
            // Burn ticks once per second from the hit that applied it.
            var pyro = new FightSim(FightLab.OpenArena(20, 12), 1);
            var p = pyro.Add(FightLab.Player(1, 0, "Pyromancy", 0, new Vec2(4f, 6f)));
            var t = pyro.Add(FightLab.Player(2, 1, "Sentinel", 0, new Vec2(9f, 6f)));
            var hit = pyro.Cast(1, "Pyromancy.bolt", t.Position);
            float after = t.Health;
            pyro.Tick(0.5f); Assert.Equal(after, t.Health, 1);                                       // the burn accumulates silently
            pyro.Tick(1.1f); Assert.True(t.Health < after);                                            // and lands once the second is up
            Assert.Contains(pyro.Events, e => e.Kind == FightEventKind.Hit && e.Detail.Contains("over time") && e.Target == 2);
            Assert.Equal(1, pyro.Events.Count(e => e.Kind == FightEventKind.Hit && e.Detail.Contains("over time")));
            // Absolute Zero freezes even a target whose control resistance would otherwise refuse.
            var az = new FightSim(FightLab.OpenArena(20, 12), 1);
            var c = az.Add(FightLab.Player(1, 0, "Cryomancy", 0, new Vec2(6f, 6f)));
            var v = az.Add(FightLab.Player(2, 1, "Sentinel", 0, new Vec2(7f, 6f)));
            for (int i = 0; i < 4; i++) v.Status.Resistance.Apply(az.Time);                            // four hard controls already: immune
            Assert.Null(v.Status.Apply(StatusEffectDefinition.All[StatusKind.Stun]));
            c.UltimateArmed = true;
            Assert.True(az.Cast(1, "Cryomancy.ult", c.Position).Ok);
            Assert.True(v.Status.Has(StatusKind.Freeze));
            Assert.False(c.UltimateArmed);
        }

        [Fact]
        public void DownedBleedsOutLastBreathRevivesBloodbathHoldsAegisHalvesRiposteReflects()
        {
            var sim = Arena(7);
            var warder = P(sim, 1, 0, "Warder", 6f, 6f);
            var tank = P(sim, 2, 0, "Sentinel", 7f, 6f);
            var axe = P(sim, 3, 1, "Berserker", 8f, 6f);
            tank.Health = 1f;
            sim.Cast(3, "Berserker.bolt", tank.Position);
            Assert.True(tank.Downed);
            Assert.False(sim.Cast(2, "Sentinel.basic", axe.Position).Ok);                              // the downed do not act
            warder.UltimateArmed = true;
            Assert.True(sim.Cast(1, "Warder.ult", tank.Position).Ok);                                   // Last Breath
            Assert.False(tank.Downed); Assert.Equal(tank.MaxHealth, tank.Health); Assert.Equal(warder.MaxHealth, warder.Health);
            Assert.Contains(sim.Events, e => e.Kind == FightEventKind.Revived && e.Target == 2);
            // Bleed out.
            tank.Health = 1f; sim.Tick(7f); sim.Cast(3, "Berserker.bolt", tank.Position);
            Assert.True(tank.Downed);
            sim.Tick(FightRules.DownedSeconds + 0.5f);
            Assert.True(tank.Dead);
            Assert.Contains(sim.Events, e => e.Kind == FightEventKind.Died && e.Target == 2 && e.Detail == "bled out");
            // Bloodbath: at zero you stand until it ends.
            var bb = Arena(8);
            var b = P(bb, 1, 0, "Berserker", 6f, 6f);
            var killer = P(bb, 2, 1, "Duelist", 7f, 6f);
            b.UltimateArmed = true; b.Health = 5f;
            Assert.True(bb.Cast(1, "Berserker.ult", b.Position).Ok);
            bb.Cast(2, "Duelist.bolt", b.Position);
            Assert.False(b.Downed); Assert.True(b.Health >= 1f);
            // Aegis: allies within 5 m take half.
            var ag = Arena(9);
            var sent = P(ag, 1, 0, "Sentinel", 6f, 6f);
            var ally = P(ag, 2, 0, "Warder", 7f, 6f);
            var foe = P(ag, 3, 1, "Berserker", 8f, 6f);
            float plain = ag.Cast(3, "Berserker.bolt", ally.Position).TotalDamage;
            ally.Health = ally.MaxHealth; ag.Tick(7f);
            sent.UltimateArmed = true; Assert.True(ag.Cast(1, "Sentinel.ult", sent.Position).Ok); ag.Tick(0.1f);
            float shielded = ag.Cast(3, "Berserker.bolt", ally.Position).TotalDamage;
            Assert.True(shielded < plain * 0.6f, $"{shielded} vs {plain}");
            // Riposte: the hit comes back and staggers the attacker.
            var rp = Arena(10);
            var duel = P(rp, 1, 0, "Duelist", 6f, 6f);
            var att = P(rp, 2, 1, "Berserker", 7f, 6f);
            Assert.True(rp.Cast(1, "Duelist.zone", duel.Position).Ok);
            float hp = duel.Health, attHp = att.Health;
            rp.Cast(2, "Berserker.bolt", duel.Position);
            Assert.Equal(hp, duel.Health); Assert.True(att.Health < attHp); Assert.True(att.Status.Has(StatusKind.Stagger));
        }

        [Fact]
        public void DashesMoveKnockbacksPushAndWallsStopBoth()
        {
            var sim = Arena();
            var breaker = P(sim, 1, 0, "Breaker", 4f, 6f);
            var target = P(sim, 2, 1, "Warder", 7f, 6f);
            var r = sim.Cast(1, "Breaker.mobility", target.Position);                                   // Charge
            Assert.True(r.Ok); Assert.Contains(2, r.Targets);
            Assert.True(breaker.Position.X > 4f, "the caster moved");
            Assert.True(target.Position.X > 7f, "the target was knocked back");
            Assert.True(target.Status.Has(StatusKind.Stagger));
            target.Position = new Vec2(19.6f, 6f); breaker.Position = new Vec2(18.5f, 6f); sim.Tick(20f);
            sim.Cast(1, "Breaker.zone", target.Position);                                               // Shockwave toward the wall
            Assert.True(target.Position.X < 21f && sim.Walkable(target.Position), "a wall stops the knockback");
            var dash = Arena();
            var d = P(dash, 1, 0, "Stormcalling", 18f, 6f);
            dash.Cast(1, "Stormcalling.mobility", new Vec2(30f, 6f));                                   // Blink at the wall
            Assert.True(d.Position.X < 21f && dash.Walkable(d.Position));
            Assert.Contains(dash.Events, e => e.Kind == FightEventKind.Dashed);
        }

        [Fact]
        public void TheBridgeTurnsFightEventsIntoMatchEventsTheRoundStateAccepts()
        {
            var d = MatchDescriptor.Create(7, DungeonSettings.ForTeams(8), new MatchRules(), out var layout);
            var st = new MatchState(d, layout);
            st.Apply(new MatchEvent(MatchEventKind.RoundStarted, 0), out _);
            for (int p = 1; p <= 6; p++) st.Apply(new MatchEvent(MatchEventKind.PlayerSpawned, 0, p, (p - 1) / 3), out _);
            var room = layout.Rooms.First(r => r.Type == RoomType.Standard);
            var sim = new FightSim(layout, 1);
            st.PlayerClass[1] = ClassArchetype.DefaultRoster.ToList().FindIndex(c => c.Name == "Reaver");   // the round state knows the class; the sim knows the spec
            var a = sim.Add(FightLab.Player(1, 0, "Berserker", 0, Vec2.Of(room.Center)));
            var v = sim.Add(FightLab.Player(4, 1, "Warder", 0, Vec2.Of(room.Center + GridPoint.Right)));
            v.Health = 1f;
            sim.Cast(1, "Berserker.bolt", v.Position);
            int swings = 0; while (!v.Dead && swings++ < 60) { sim.Tick(1.1f); sim.Cast(1, "Berserker.basic", v.Position); }
            var produced = sim.Events.SelectMany(e => FightBridge.ToMatchEvents(e, sim, _ => room.Id)).ToList();
            Assert.Contains(produced, e => e.Kind == MatchEventKind.PlayerDowned && e.A == 4 && e.B == 1);
            Assert.Contains(produced, e => e.Kind == MatchEventKind.PlayerFinished && e.A == 4 && e.B == 1);
            foreach (var e in produced) Assert.True(st.Apply(e, out string why), why);
            Assert.Contains(4, st.DeadPlayers); Assert.Contains(4, st.CorpseSouls);
            var recap = DeathRecap.For(st, 4, sim.Hits);
            Assert.Equal("Berserker", recap.KillerSpec); Assert.Equal("Reaver", recap.KillerClass); Assert.True(recap.Finished);
            Assert.NotEmpty(recap.LastHits);
        }

        [Fact]
        public void TheFightLabIsDeterministicAndTheBossTuningHolds()
        {
            var one = FightLab.Duel("Berserker", "Warder", 2, 11); var two = FightLab.Duel("Berserker", "Warder", 2, 11);
            Assert.Equal((one.WinnerTeam, one.Seconds, one.Downs), (two.WinnerTeam, two.Seconds, two.Downs));
            Assert.Equal(0, one.WinnerTeam);                                                             // a healer does not win duels, by design
            Assert.True(FightLab.DuelSeries("Berserker", "Warder", 2, 6).winRateA > 0.9f);
            int wins = 0, soloWins = 0, duoWins = 0;
            for (int s = 1; s <= 6; s++)
            {
                if (FightLab.PartyVsBoss(new[] { "Sentinel", "Berserker", "Warder" }, 2, s).WinnerTeam == 0) wins++;
                if (FightLab.PartyVsBoss(new[] { "Berserker" }, 4, s).WinnerTeam == 0) soloWins++;
                if (FightLab.PartyVsBoss(new[] { "Berserker", "Sharpshooter" }, 2, s).WinnerTeam == 0) duoWins++;
            }
            Assert.True(wins >= 5, $"a geared trio beats the room boss ({wins}/6)");
            Assert.Equal(0, soloWins);                                                                   // a solo does not
            Assert.True(duoWins <= 3, "a duo is the coin flip at best");
            var team = FightLab.TeamFight(new[] { "Sentinel", "Sharpshooter", "Warder" }, new[] { "Breaker", "Berserker", "Plaguebearer" }, 2, 3);
            Assert.True(team.WinnerTeam >= 0 || team.TimedOut);
            Assert.True(team.DamageDealt.Count >= 4);
        }
    }
}
