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

            for (int i = 0; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
                switch (args[i])
                {
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
                        Console.WriteLine("--seed N --width N --height N --rooms N --loops 0..1 --spawns N --extracts N --corridor-width 1|2 --batch N --no-legend");
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

        private static int RunBatch(DungeonSettings settings, int startSeed, int count)
        {
            int withWarnings = 0, failed = 0;
            int totalRooms = 0, totalLoops = 0, maxDepthSum = 0;
            var warningKinds = new System.Collections.Generic.Dictionary<string, int>();
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
            }
            sw.Stop();
            Console.WriteLine($"Batch of {count} seeds starting at {startSeed}");
            Console.WriteLine($"  unplayable after retries : {failed}");
            Console.WriteLine($"  layouts with warnings    : {withWarnings}");
            Console.WriteLine($"  avg rooms                : {(double)totalRooms / count:0.0}");
            Console.WriteLine($"  avg loop corridors       : {(double)totalLoops / count:0.0}");
            Console.WriteLine($"  avg max depth (hops)     : {(double)maxDepthSum / count:0.0}");
            Console.WriteLine($"  avg time per dungeon     : {sw.Elapsed.TotalMilliseconds / count:0.00} ms");
            foreach (var kv in warningKinds.OrderByDescending(kv => kv.Value))
                Console.WriteLine($"  warning x{kv.Value,-4}: {kv.Key}");
            return failed == 0 ? 0 : 2;
        }
    }
}
