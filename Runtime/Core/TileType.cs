namespace DungeonGen.Core
{
    /// <summary>What occupies a single grid cell in the rasterized layout.</summary>
    public enum TileType : byte
    {
        Void = 0,      // Outside the dungeon entirely (solid rock / nothing rendered)
        Wall = 1,      // Boundary cell adjacent to walkable space
        RoomFloor = 2,
        Corridor = 3,
        Door = 4,      // Transition cell between a room and a corridor
    }
}
