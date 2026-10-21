using System;
using System.Collections.Generic;
using DungeonGen.Core.Stages;

namespace DungeonGen.Core
{
    /// <summary>
    /// Entry point. Runs the five-stage pipeline and returns a fully populated layout.
    ///
    ///   seed ──► RoomPlacer ──► GraphBuilder ──► CorridorCarver ──► RoleAssigner ──► Rasterizer ──► DangerMapper ──► DungeonLayout
    ///
    /// Generation is pure and deterministic: the same (settings, seed) pair always yields the
    /// same layout on every platform. In multiplayer the server picks the seed, every client
    /// regenerates locally, and only the seed crosses the wire.
    /// </summary>
    public static class DungeonGenerator
    {
        /// <summary>How many derived seeds to try if a layout fails validation before giving up.</summary>
        public const int MaxRetries = 8;

        public static DungeonLayout Generate(DungeonSettings settings, int seed)
        {
            settings.Validate();

            // Walk a deterministic chain of working seeds. Collect up to CandidateCount playable
            // layouts and keep the fairest; give up after MaxRetries consecutive unplayable ones.
            int attemptSeed = seed;
            DungeonLayout best = null;
            DungeonLayout last = null;
            int playable = 0, failures = 0, attempt = 0;

            while (playable < settings.CandidateCount && failures <= MaxRetries)
            {
                last = GenerateOnce(settings, attemptSeed, seed);
                var problems = new List<string>();
                if (LayoutValidator.IsPlayable(last, settings, problems))
                {
                    playable++;
                    if (best == null || last.Fairness.Score > best.Fairness.Score) best = last;
                }
                else
                {
                    failures++;
                    last.Warnings.AddRange(problems);
                }
                attemptSeed = unchecked(attemptSeed * 1664525 + 1013904223 + attempt++);
            }

            if (best != null) return best;
            last.Warnings.Add($"Layout still failed validation after {MaxRetries} retries; returning best effort.");
            return last;
        }

        private static DungeonLayout GenerateOnce(DungeonSettings s, int workingSeed, int requestedSeed)
        {
            var layout = new DungeonLayout(requestedSeed, s.Width, s.Height);
            var rng = new DungeonRandom(workingSeed);

            var rooms = RoomPlacer.Place(s, rng.Fork(1), layout.Warnings);
            if (rooms.Count < s.TargetRoomCount)
                layout.Warnings.Add($"Placed {rooms.Count}/{s.TargetRoomCount} rooms; canvas is crowded. Enlarge it or shrink rooms.");

            var edges = GraphBuilder.Build(rooms, s, rng.Fork(2));
            var corridors = CorridorCarver.Carve(rooms, edges, s, layout.Warnings);
            RoleAssigner.Assign(rooms, corridors, s, rng.Fork(3), layout.Warnings);

            layout.Rooms.AddRange(rooms);
            layout.Corridors.AddRange(corridors);
            Rasterizer.Paint(layout, rooms, corridors);
            DangerMapper.Assign(layout, s);
            FairnessMapper.Assign(layout);

            return layout;
        }
    }
}
