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
            Assert.Equal(LayoutFingerprint.Compute(serverLayout), LayoutFingerprint.Compute(clientLayout));
            Assert.Equal(AsciiRenderer.Render(serverLayout), AsciiRenderer.Render(clientLayout));
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
            if (key.DroppedByBoss)
            {
                Assert.False(st.Apply(new MatchEvent(MatchEventKind.KeyPickedUp, 3, key.KeyId, 1), out why));
                Assert.True(st.Apply(new MatchEvent(MatchEventKind.BossKilled, 4, key.HolderRoomId, 0), out why));
            }
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.KeyPickedUp, 5, key.KeyId, 1), out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.VaultOpened, 6, vault.Id, 2), out why), "wrong player");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.VaultOpened, 6, vault.Id, 1), out why));
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.ItemTaken, 7, item, 1), out why), "container not opened");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ContainerOpened, 8, vault.Id, 0), out why));
            Assert.Contains(item, st.VisibleItemsIn(vault.Id));
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

            var replay = MatchState.Replay(MatchDescriptor.FromBytes(d.ToBytes()), d.BuildAndVerify(), MatchState.LogFromBytes(st.LogToBytes()));
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
        public void SoulsComeOnlyFromEnemyPlayerKills()
        {
            var (st, layout) = Fresh();
            string why;
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 10, 4, -1, 0), out why));     // killed by a mob
            Assert.Empty(st.Souls);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 11, 2, 1, 0), out why));      // teammate kill
            Assert.Empty(st.Souls);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 12, 5, 1, 0), out why));      // team 0 kills team 1
            Assert.Equal(1, st.Souls[0]);
        }

        [Fact]
        public void ResurrectionCostsTwoSoulsNeedsAShrineAndWorksOnce()
        {
            var (st, layout) = Fresh();
            var shrine = layout.Rooms.First(r => r.HasShrine).Id;
            var plain = layout.Rooms.First(r => !r.HasShrine && r.Type == RoomType.Standard).Id;
            string why;
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 10, 2, 4, 0), out why));                 // player 2 (team 0) dies to player 4
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.PlayerResurrected, 11, 2, 1, shrine), out why));   // no souls yet
            Assert.Contains("souls", why);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 12, 4, 1, 0), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 13, 5, 1, 0), out why));                 // team 0 now has 2 souls
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.PlayerResurrected, 14, 2, 1, plain), out why));    // not a shrine
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.PlayerResurrected, 14, 2, 6, shrine), out why));   // enemy cannot res you
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.PlayerResurrected, 14, 2, 4, shrine), out why));   // dead teammate? 4 is enemy and dead anyway
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerResurrected, 15, 2, 1, shrine), out why));
            Assert.Equal(0, st.Souls[0]);
            Assert.DoesNotContain(2, st.DeadPlayers);
            Assert.Contains(2, st.PlayersInside);
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 20, 2, 6, 0), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 21, 6, 1, 0), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 22, 6, 1, 0), out why) == false);        // already dead
            st.Souls[0] = 5;
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
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.KeyPickedUp, 6, key.KeyId, 1), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ExtractsOpened, 240), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ExtractStarted, 290, 1, layout.ExtractRooms.First().Id), out why));
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.PlayerExtracted, 300, 1, layout.ExtractRooms.First().Id, 0), out why));
            Assert.False(st.KeyHolder.ContainsKey(key.KeyId));
            Assert.Contains(key.KeyId, st.DestroyedKeys);
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.KeyPickedUp, 301, key.KeyId, 2), out why));
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
            st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 2, 4, 1, 0), out _);
            st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 3, 5, 1, 0), out _);
            st.Apply(new MatchEvent(MatchEventKind.PlayerResurrected, 4, 2, 1, shrine), out _);
            var replay = MatchState.Replay(MatchDescriptor.FromBytes(st.Descriptor.ToBytes()), st.Descriptor.BuildAndVerify(), MatchState.LogFromBytes(st.LogToBytes()));
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
            float price = Vendor.Sell(p, item);
            Assert.True(price > 0 && !p.Stash.Contains(item));
            var bag = Vendor.Salvage(p, p.Stash[0]);
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
            Assert.Equal(new[] { MatchEventKind.RoundStarted, MatchEventKind.ExtractsOpened, MatchEventKind.RoundEnded }, fired.Select(e => e.Kind).ToArray());
            Assert.Equal(240f, fired[1].Time);
            Assert.Equal(1200f, fired[2].Time);
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
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.CorpseOpened, 52, 1, 6), out why));          // a different team opens it
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
            st.Apply(new MatchEvent(MatchEventKind.PlayerDied, 11, 4, 1, 0), out _);
            Assert.Equal(1, st.Souls[0]);
            Assert.False(st.Apply(new MatchEvent(MatchEventKind.ShrinePurchase, 12, 1, 0, layout.ExtractRooms.First().Id), out why), "not a shrine");
            Assert.True(st.Apply(new MatchEvent(MatchEventKind.ShrinePurchase, 12, 1, 0, shrine), out why));
            Assert.Equal(0, st.Souls[0]);
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
            Assert.Single(d.TeamSpawnRooms);
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
