using System;
using System.Diagnostics;
using System.Linq;
using DungeonGen.Core;

namespace DungeonGen.Tools
{
    /// <summary>
    /// Headless preview: dotnet run -- --seed 42 --width 80 --height 60 --rooms 16 --loops 0.2
    /// Add --batch 500 to generate many seeds and report how often validation needed retries.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var settings = new DungeonSettings();
            int seed = Environment.TickCount;
            int batch = 0;
            bool legend = true;

            // --teams applies a whole preset, so it must be read before any individual overrides.
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "--teams") settings = DungeonSettings.ForTeams(int.Parse(args[i + 1]));

            for (int i = 0; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
                switch (args[i])
                {
                    case "--teams": Next(); break;
                    case "--ring": settings.SpawnPlacement = SpawnPlacement.Ring; break;
                    case "--fringe": settings.SpawnPlacement = SpawnPlacement.Fringe; break;
                    case "--candidates": settings.CandidateCount = int.Parse(Next()); break;
                    case "--bosses": settings.BossCount = int.Parse(Next()); break;
                    case "--vaults": settings.VaultCount = int.Parse(Next()); break;
                    case "--fights": return PrintFightTable(settings);
                    case "--stats": return PrintStats(settings);
                    case "--seed": seed = int.Parse(Next()); break;
                    case "--width": settings.Width = int.Parse(Next()); break;
                    case "--height": settings.Height = int.Parse(Next()); break;
                    case "--rooms": settings.TargetRoomCount = int.Parse(Next()); break;
                    case "--loops": settings.LoopChance = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--spawns": settings.SpawnCount = int.Parse(Next()); break;
                    case "--extracts": settings.ExtractCount = int.Parse(Next()); break;
                    case "--corridor-width": settings.CorridorWidth = int.Parse(Next()); break;
                    case "--batch": batch = int.Parse(Next()); break;
                    case "--no-legend": legend = false; break;
                    case "--help":
                        Console.WriteLine("--teams N (preset) --seed N --width N --height N --rooms N --loops 0..1 --spawns N --extracts N --ring|--fringe --candidates N --bosses N --vaults N --corridor-width 1|2 --batch N --no-legend --fights --stats");
                        return 0;
                    default:
                        Console.Error.WriteLine("Unknown argument " + args[i]);
                        return 1;
                }
            }

            if (batch > 0) return RunBatch(settings, seed, batch);

            var sw = Stopwatch.StartNew();
            var layout = DungeonGenerator.Generate(settings, seed);
            sw.Stop();
            Console.Write(AsciiRenderer.Render(layout, legend));
            Console.WriteLine($"Generated in {sw.Elapsed.TotalMilliseconds:0.0} ms");
            return 0;
        }

        private static int PrintStats(DungeonSettings settings)
        {
            var b = settings.Combat;
            Console.WriteLine("CLASSES (tier 0 and tier 3 gear)");
            foreach (var c in DungeonGen.Core.Combat.ClassArchetype.DefaultRoster)
            {
                Console.WriteLine($"  {DungeonGen.Core.Combat.PlayerStats.For(c, 0, b)}");
                Console.WriteLine($"  {DungeonGen.Core.Combat.PlayerStats.For(c, 3, b)}");
            }
            Console.WriteLine();
            Console.WriteLine("BOSS DAMAGE");
            foreach (var a in DungeonGen.Core.Combat.BossArchetype.DefaultPool.Append(DungeonGen.Core.Combat.BossArchetype.FinalBoss))
                Console.WriteLine($"  {a.Name,-20} {a.Damage}   retinue: {a.RetinueMob ?? "-"}");
            Console.WriteLine();
            Console.WriteLine("MOBS");
            foreach (var m in DungeonGen.Core.Combat.MobArchetype.DefaultRoster)
                Console.WriteLine($"  {m.Name,-16} deals {m.Damage,-28} resists {m.Resists,-9} weak to {m.WeakTo,-9} hp {m.HealthFraction:0.00} dps {m.DpsFraction:0.00}");
            Console.WriteLine();
            Console.WriteLine("A 100-damage hit of each type, taken by each class in tier-0 gear:");
            Console.Write("  class        ");
            foreach (DungeonGen.Core.Combat.DamageType t in Enum.GetValues(typeof(DungeonGen.Core.Combat.DamageType))) Console.Write($"{t,10}");
            Console.WriteLine();
            foreach (var c in DungeonGen.Core.Combat.ClassArchetype.DefaultRoster)
            {
                var st = DungeonGen.Core.Combat.PlayerStats.For(c, 0, b);
                Console.Write($"  {c.Name,-12}");
                foreach (DungeonGen.Core.Combat.DamageType t in Enum.GetValues(typeof(DungeonGen.Core.Combat.DamageType))) Console.Write($"{st.TakeHit(100f, t),10:0}");
                Console.WriteLine();
            }
            return 0;
        }

        private static int PrintFightTable(DungeonSettings settings)
        {
            var b = settings.Combat;
            foreach (var e in new[] { DungeonGen.Core.Combat.EncounterTuner.RoomBoss(b), DungeonGen.Core.Combat.EncounterTuner.FinalBoss(b) })
            {
                Console.WriteLine(e);
                Console.WriteLine("  players  gear   fight(s)   taken   capacity  margin  result");
                foreach (float gear in new[] { 1f, b.GoodGearMultiplier })
                    for (int k = 1; k <= b.PartySize + 1; k++)
                    {
                        var o = DungeonGen.Core.Combat.EncounterTuner.Simulate(e, b, k, gear);
                        Console.WriteLine($"  {k,7}  x{gear:0.00}  {o.FightSeconds,8:0}  {o.DamageTaken,6:0}  {o.DamageCapacity,9:0}  {o.Margin,6:0.00}  {(o.Wins ? "win" : "wipe")}");
                    }
            }
            return 0;
        }

        private static int RunBatch(DungeonSettings settings, int startSeed, int count)
        {
            int withWarnings = 0, failed = 0;
            int totalRooms = 0, totalLoops = 0, maxDepthSum = 0;
            double fairnessSum = 0; float fairnessMin = 1f;
            var warningKinds = new System.Collections.Generic.Dictionary<string, int>();
            var archetypes = new System.Collections.Generic.Dictionary<string, int>();
            var affixes = new System.Collections.Generic.Dictionary<string, int>();
            int affixSlots = 0, bossRooms = 0;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < count; i++)
            {
                var layout = DungeonGenerator.Generate(settings, startSeed + i);
                var problems = new System.Collections.Generic.List<string>();
                if (!LayoutValidator.IsPlayable(layout, settings, problems)) failed++;
                if (layout.Warnings.Count > 0) withWarnings++;
                foreach (var w in layout.Warnings)
                {
                    string kind = w.Length > 48 ? w.Substring(0, 48) + "..." : w;
                    warningKinds[kind] = warningKinds.TryGetValue(kind, out int c) ? c + 1 : 1;
                }
                totalRooms += layout.Rooms.Count;
                totalLoops += layout.Corridors.Count(c => c.IsLoop);
                maxDepthSum += layout.Rooms.Max(r => r.DepthFromSpawn);
                fairnessSum += layout.Fairness.Score;
                foreach (var r in layout.BossRooms)
                {
                    bossRooms++;
                    archetypes[r.Boss.Archetype.Name] = archetypes.TryGetValue(r.Boss.Archetype.Name, out int ac) ? ac + 1 : 1;
                    foreach (var a in r.Boss.Affixes)
                    {
                        affixSlots++;
                        affixes[a.Name] = affixes.TryGetValue(a.Name, out int fc) ? fc + 1 : 1;
                    }
                }
                if (layout.Fairness.Score < fairnessMin) fairnessMin = layout.Fairness.Score;
            }
            sw.Stop();
            Console.WriteLine($"Batch of {count} seeds starting at {startSeed}");
            Console.WriteLine($"  unplayable after retries : {failed}");
            Console.WriteLine($"  layouts with warnings    : {withWarnings}");
            Console.WriteLine($"  avg rooms                : {(double)totalRooms / count:0.0}");
            Console.WriteLine($"  avg loop corridors       : {(double)totalLoops / count:0.0}");
            Console.WriteLine($"  avg max depth (hops)     : {(double)maxDepthSum / count:0.0}");
            Console.WriteLine($"  avg fairness (min)       : {fairnessSum / count:0.00} ({fairnessMin:0.00})");
            Console.WriteLine($"  avg time per dungeon     : {sw.Elapsed.TotalMilliseconds / count:0.00} ms");
            foreach (var kv in warningKinds.OrderByDescending(kv => kv.Value))
                Console.WriteLine($"  warning x{kv.Value,-4}: {kv.Key}");
            if (bossRooms > 0)
            {
                Console.WriteLine($"  boss archetypes over {bossRooms} boss rooms: " + string.Join(", ", archetypes.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {100.0 * kv.Value / bossRooms:0}%")));
                Console.WriteLine($"  affixes ({(double)affixSlots / bossRooms:0.00} per boss): " + string.Join(", ", affixes.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")));
            }
            return failed == 0 ? 0 : 2;
        }
    }
}
