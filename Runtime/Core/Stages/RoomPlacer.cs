using System.Collections.Generic;

namespace DungeonGen.Core.Stages
{
    /// <summary>
    /// Stage 1: scatter non-overlapping rectangular rooms across the canvas using
    /// rejection sampling. Simple, fast, and gives organic-looking spacing. Rooms are
    /// placed with at least <see cref="DungeonSettings.RoomSpacing"/> empty cells between
    /// them so corridors always have somewhere to run.
    /// </summary>
    public static class RoomPlacer
    {
        public static List<Room> Place(DungeonSettings s, DungeonRandom rng)
        {
            var rooms = new List<Room>(s.TargetRoomCount);
            int pad = s.BorderPadding + 1; // +1 leaves space for the wall ring
            int attempts = 0;

            while (rooms.Count < s.TargetRoomCount && attempts < s.MaxPlacementAttempts)
            {
                attempts++;

                int w = rng.Range(s.MinRoomSize, s.MaxRoomSize + 1);
                int h = rng.Range(s.MinRoomSize, s.MaxRoomSize + 1);

                // Bias against extreme aspect ratios; long slivers make awkward rooms.
                if (w > h * 2 || h > w * 2) continue;

                int maxX = s.Width - pad - w;
                int maxY = s.Height - pad - h;
                if (maxX <= pad || maxY <= pad) continue;

                var candidate = new GridRect(rng.Range(pad, maxX + 1), rng.Range(pad, maxY + 1), w, h);

                bool blocked = false;
                for (int i = 0; i < rooms.Count; i++)
                {
                    if (rooms[i].Bounds.Overlaps(candidate, s.RoomSpacing))
                    {
                        blocked = true;
                        break;
                    }
                }
                if (blocked) continue;

                rooms.Add(new Room(rooms.Count, candidate));
            }

            return rooms;
        }
    }
}
