using System.Collections.Generic;

namespace DungeonGen.Core
{
    /// <summary>
    /// Production and technical decisions, as data, so the code base states them in one place and
    /// a test can hold the vertical slice to its cut. See docs/DECISIONS.md for the reasoning.
    /// </summary>
    public static class TechStack
    {
        public const string Engine = "Unity 6";
        public const string RenderPipeline = "URP (Forward+)";
        public const string Netcode = "Fishnet";
        public const string Hosting = "Managed dedicated servers (Edgegap or Hathora)";
        public const string Backend = "PlayFab";
        public const string Pathfinding = "Grid A* on the server (no NavMesh)";
        public const string ArtDirection = "Stylized low-poly, flat-shaded or hand-painted";
        public const string Platform = "PC (Steam) first, controller supported, console later";
        public const string Hub = "Menus only";
        public const int TeamSizeOfDevelopers = 1;
    }

    /// <summary>
    /// The first playable build: every system once, half the content. Anything outside this list
    /// is still in the code base but is not in the slice.
    /// </summary>
    public static class VerticalSlice
    {
        public const int Teams = 4;
        public static readonly IReadOnlyList<string> Classes = new[] { "Warden", "Reaver", "Elementalist" };
        public static readonly IReadOnlyList<string> Bosses = new[] { "Iron Juggernaut", "Hive Matron" };
        /// <summary>The one element kit the slice ships for the Elementalist; the other two staves exist but their kits are not in the slice.</summary>
        public static readonly IReadOnlyList<string> ElementKits = new[] { "Pyromancy" };
        /// <summary>Specs in the slice: one per class, the second spec of each comes after.</summary>
        public static readonly IReadOnlyList<string> Specs = new[] { "Sentinel", "Berserker", "Pyromancy" };

        /// <summary>The slice's dungeon: the 4-team preset, two boss rooms drawn only from the slice's bosses.</summary>
        public static DungeonSettings Settings()
        {
            var s = DungeonSettings.ForTeams(Teams);
            s.BossCount = 2;
            s.AllowedBosses = Bosses;
            return s;
        }
    }
}
