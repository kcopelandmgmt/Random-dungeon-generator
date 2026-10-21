namespace DungeonGen.Core
{
    /// <summary>
    /// Gameplay role of a room. Assigned after the spatial layout exists, using graph
    /// distance from the spawn rooms so risk and reward scale with depth.
    /// </summary>
    public enum RoomType
    {
        Standard,   // Generic combat / filler space
        Spawn,      // Player entry gate (one squad per spawn room)
        Extract,    // Exit gate; may be locked until a timer/condition is met
        Loot,       // Elevated loot table, usually 1-2 corridors off the main routes
        Vault,      // Deepest, most dangerous room: best loot, boss or heavy guards
        Hub,        // High-connectivity crossroads; where squads tend to collide
    }
}
