namespace DungeonGen.Core
{
    /// <summary>
    /// Gameplay role of a room. Assigned after the spatial layout exists, using walking
    /// distance from the spawn rooms so risk and reward scale with depth.
    /// </summary>
    public enum RoomType
    {
        Standard,   // Generic combat / filler space
        Spawn,      // Team entry gate (one team per spawn room)
        Extract,    // Exit gate
        Loot,       // Elevated loot table, usually a dead end off the main routes
        Vault,      // Locked treasure room: every door is a LockedDoor tile, opened by a key found elsewhere
        Hub,        // High-connectivity crossroads; where teams tend to collide
        Boss,       // Boss arena tuned for ~2.5 players; guards loot and sometimes a vault key
        FinalBoss,  // Center of the dungeon. Tuned for a full, well-geared party. Killing it opens the gated exit.
    }
}
