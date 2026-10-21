using System.Collections.Generic;

namespace DungeonGen.Core
{
    /// <summary>A carved path between two rooms. Cells are ordered from room A's door to room B's door.</summary>
    public sealed class Corridor
    {
        public int RoomA { get; }
        public int RoomB { get; }
        public List<GridPoint> Cells { get; } = new List<GridPoint>();

        /// <summary>True if this edge was added back after the MST to create a loop (flanking route).</summary>
        public bool IsLoop { get; set; }

        public Corridor(int roomA, int roomB)
        {
            RoomA = roomA;
            RoomB = roomB;
        }

        public int Length => Cells.Count;
    }
}
