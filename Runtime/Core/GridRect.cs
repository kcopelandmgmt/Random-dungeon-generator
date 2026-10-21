using System;

namespace DungeonGen.Core
{
    /// <summary>Axis-aligned integer rectangle. Max edges are exclusive.</summary>
    public readonly struct GridRect
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Width;
        public readonly int Height;

        public GridRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public int XMax => X + Width;
        public int YMax => Y + Height;
        public int Area => Width * Height;
        public GridPoint Min => new GridPoint(X, Y);
        public GridPoint Center => new GridPoint(X + Width / 2, Y + Height / 2);

        public bool Contains(GridPoint p) => p.X >= X && p.X < XMax && p.Y >= Y && p.Y < YMax;

        public bool Contains(int x, int y) => x >= X && x < XMax && y >= Y && y < YMax;

        /// <summary>True if the rectangles overlap or come within <paramref name="padding"/> cells of each other.</summary>
        public bool Overlaps(GridRect other, int padding = 0)
        {
            return X - padding < other.XMax &&
                   XMax + padding > other.X &&
                   Y - padding < other.YMax &&
                   YMax + padding > other.Y;
        }

        public GridRect Expand(int amount) => new GridRect(X - amount, Y - amount, Width + amount * 2, Height + amount * 2);

        public override string ToString() => $"[{X},{Y} {Width}x{Height}]";
    }
}
