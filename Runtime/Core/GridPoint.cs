using System;

namespace DungeonGen.Core
{
    /// <summary>
    /// Integer 2D coordinate on the dungeon grid. Engine-agnostic so the core
    /// can run headless (dedicated server, unit tests, CLI preview).
    /// </summary>
    public readonly struct GridPoint : IEquatable<GridPoint>
    {
        public readonly int X;
        public readonly int Y;

        public GridPoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public static readonly GridPoint Zero = new GridPoint(0, 0);
        public static readonly GridPoint Up = new GridPoint(0, 1);
        public static readonly GridPoint Down = new GridPoint(0, -1);
        public static readonly GridPoint Left = new GridPoint(-1, 0);
        public static readonly GridPoint Right = new GridPoint(1, 0);
        public static readonly GridPoint[] Cardinals = { Up, Right, Down, Left };

        public static GridPoint operator +(GridPoint a, GridPoint b) => new GridPoint(a.X + b.X, a.Y + b.Y);
        public static GridPoint operator -(GridPoint a, GridPoint b) => new GridPoint(a.X - b.X, a.Y - b.Y);
        public static bool operator ==(GridPoint a, GridPoint b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(GridPoint a, GridPoint b) => !(a == b);

        public int ManhattanDistance(GridPoint other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

        public bool Equals(GridPoint other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is GridPoint p && Equals(p);
        public override int GetHashCode() => unchecked(X * 73856093 ^ Y * 19349663);
        public override string ToString() => $"({X}, {Y})";
    }
}
