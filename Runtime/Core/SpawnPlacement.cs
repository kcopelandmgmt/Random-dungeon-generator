namespace DungeonGen.Core
{
    /// <summary>How spawn (team entry) rooms are chosen.</summary>
    public enum SpawnPlacement
    {
        /// <summary>Place all rooms randomly, then promote the most peripheral, mutually distant rooms to spawns. Good up to ~4 teams.</summary>
        Fringe,
        /// <summary>Place one spawn room per team around the canvas edge at evenly spaced angles before any other room. Keeps every team an equal distance from the center; use for 4-8+ teams.</summary>
        Ring,
    }
}
