using System.Collections.Generic;
using DungeonGen.Core;
using UnityEngine;

namespace DungeonGen.Unity.UI
{
    /// <summary>
    /// The fog of war as a texture: <see cref="Scale"/> texels per dungeon cell, fully transparent everywhere the team has not seen. Rooms are
    /// painted when the state marks them explored (with the corridors that join two explored rooms) and corridor cells when the local client has had
    /// them in sight; the walls round a painted cell are drawn with it. Nothing else is ever drawn, so a pixel of the texture is never a leak.
    /// Painting is incremental (only what is new) and uploaded once a frame at most.
    /// </summary>
    internal sealed class MapFog
    {
        public const int Scale = 6;

        public static readonly Color32 Clear = new Color32(0, 0, 0, 0);
        public static readonly Color32 Wall = new Color32(50, 40, 31, 255), WallRim = new Color32(178, 144, 86, 255);
        public static readonly Color32 Floor = new Color32(100, 88, 74, 255), CorridorFloor = new Color32(78, 70, 62, 255), Pit = new Color32(12, 10, 10, 255), Interior = new Color32(136, 118, 90, 255);

        public readonly Texture2D Texture;
        public readonly DungeonLayout Layout;
        public int RevealedCells { get; private set; }

        private readonly Color32[] _px;
        private readonly bool[,] _revealed, _walled;
        private readonly HashSet<int> _rooms = new HashSet<int>();
        private readonly bool[] _corridors;
        private int _trailDone, _exploredSeen = -1;
        private bool _dirty;
        public readonly int WidthPx, HeightPx;

        public MapFog(DungeonLayout layout)
        {
            Layout = layout;
            WidthPx = layout.Width * Scale; HeightPx = layout.Height * Scale;
            Texture = new Texture2D(WidthPx, HeightPx, TextureFormat.RGBA32, false) { name = "map-fog", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _px = new Color32[WidthPx * HeightPx];
            _revealed = new bool[layout.Width, layout.Height]; _walled = new bool[layout.Width, layout.Height];
            _corridors = new bool[layout.Corridors.Count];
            Texture.SetPixels32(_px); Texture.Apply(false);
        }

        public void Destroy() { if (Texture != null) Object.Destroy(Texture); }

        /// <summary>Paint whatever became known since the last call. Cheap when nothing did.</summary>
        public void Sync(IReadOnlyCollection<int> explored, IReadOnlyList<GridPoint> trail)
        {
            if (explored.Count != _exploredSeen)
            {
                _exploredSeen = explored.Count;
                foreach (int id in explored) if (id >= 0 && id < Layout.Rooms.Count && _rooms.Add(id)) PaintRoom(Layout.Rooms[id]);
                for (int i = 0; i < _corridors.Length; i++)
                {
                    if (_corridors[i]) continue;
                    var c = Layout.Corridors[i];
                    if (!_rooms.Contains(c.RoomA) || !_rooms.Contains(c.RoomB)) continue;
                    _corridors[i] = true;
                    foreach (var p in c.Cells) Reveal(p.X, p.Y, null);
                }
            }
            for (; _trailDone < trail.Count; _trailDone++) Reveal(trail[_trailDone].X, trail[_trailDone].Y, null);
            if (_dirty) { Texture.SetPixels32(_px); Texture.Apply(false); _dirty = false; }
        }

        private void PaintRoom(Room r)
        {
            var b = r.Bounds;
            for (int x = b.X; x < b.XMax; x++)
                for (int y = b.Y; y < b.YMax; y++) Reveal(x, y, r);
            foreach (var d in r.Doors) Reveal(d.X, d.Y, r);
        }

        private bool Open(TileType t) => t != TileType.Void && t != TileType.Wall;

        /// <summary>Show one cell (a floor, a door, a pillar) and the walls touching it.</summary>
        private void Reveal(int x, int y, Room room)
        {
            if (x < 0 || y < 0 || x >= Layout.Width || y >= Layout.Height) return;
            var t = Layout.Tiles[x, y];
            if (!Open(t) || _revealed[x, y]) return;
            _revealed[x, y] = true; RevealedCells++;
            Fill(x, y, ColourOf(t, room));
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= Layout.Width || ny >= Layout.Height || Layout.Tiles[nx, ny] != TileType.Wall) continue;
                    if (!_walled[nx, ny]) { _walled[nx, ny] = true; Fill(nx, ny, Wall); }
                    // A lighter edge on the side that faces this open cell (the wall reads as a line, not a slab).
                    if ((dx == 0) != (dy == 0)) Rim(nx, ny, -dx, -dy);
                }
        }

        private Color32 ColourOf(TileType t, Room room)
        {
            switch (t)
            {
                case TileType.Pit: return Pit;
                case TileType.Pillar: case TileType.LowWall: return Interior;
                case TileType.Door: case TileType.LockedDoor: return t == TileType.LockedDoor ? new Color32(190, 120, 40, 255) : new Color32(120, 98, 66, 255);
                case TileType.Corridor: return CorridorFloor;
            }
            if (room == null) return Floor;
            switch (room.Type)
            {
                case RoomType.Extract: return Tint(Floor, new Color32(70, 120, 60, 255), 0.55f);
                case RoomType.Boss: return Tint(Floor, new Color32(140, 50, 44, 255), 0.5f);
                case RoomType.FinalBoss: return Tint(Floor, new Color32(110, 60, 150, 255), 0.55f);
                case RoomType.Vault: return Tint(Floor, new Color32(170, 120, 40, 255), 0.5f);
                case RoomType.Spawn: return Tint(Floor, new Color32(60, 90, 140, 255), 0.5f);
                default: return Floor;
            }
        }

        private static Color32 Tint(Color32 a, Color32 b, float k) => new Color32((byte)(a.r + (b.r - a.r) * k), (byte)(a.g + (b.g - a.g) * k), (byte)(a.b + (b.b - a.b) * k), 255);

        private void Fill(int cx, int cy, Color32 c)
        {
            int x0 = cx * Scale, y0 = cy * Scale;
            for (int y = 0; y < Scale; y++)
            {
                int row = (y0 + y) * WidthPx + x0;
                for (int x = 0; x < Scale; x++) _px[row + x] = c;
            }
            _dirty = true;
        }

        /// <summary>The 2-texel band of a wall cell on its side (ex, ey), the side that faces an open cell.</summary>
        private void Rim(int cx, int cy, int ex, int ey)
        {
            int x0 = cx * Scale, y0 = cy * Scale;
            for (int i = 0; i < Scale; i++)
                for (int k = 0; k < 2; k++)
                {
                    int x = ex == 0 ? i : ex > 0 ? Scale - 1 - k : k;
                    int y = ey == 0 ? i : ey > 0 ? Scale - 1 - k : k;
                    _px[(y0 + y) * WidthPx + x0 + x] = WallRim;
                }
        }
    }
}
