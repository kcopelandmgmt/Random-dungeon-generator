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
        public const string AntiCheat = "Easy Anti-Cheat (Epic Online Services) on the client; server rejections remain the line";
        public const string RegionPolicy = "Auto by latency, manual override, a match never mixes regions";
        public static readonly IReadOnlyList<string> LaunchLanguages = Strings.LaunchLanguages;
    }

    /// <summary>How the game is sold. Free to play; the only thing for sale is how you look. Nothing for sale is power, ever.</summary>
    public static class Monetisation
    {
        public const string Model = "Free to play";
        public const string Sells = "Cosmetics only: seasonal cosmetics, nothing with a stat";
        public const bool BattlePass = false;
        public const bool PaidPower = false;
        public const string Store = "Direct purchase at a visible price, plus a small featured shelf that rotates weekly";
        public const bool LootBoxes = false;
        public const int FeaturedRotationDays = 7;
        /// <summary>No countdown timer on anything for sale shorter than this. No FOMO under a week.</summary>
        public const int MinimumOfferDays = 7;
        /// <summary>Free accounts are cheap for mules and cheats, so the account level gates in AccountRules exist.</summary>
        public const string Consequence = "Account levels gate the three earned classes; the transfer ledger, the tier walls and durability box in real-money trading";
    }

    /// <summary>What is decided but deliberately not in the launch build.</summary>
    public static class Roadmap
    {
        public static readonly IReadOnlyList<string> SeasonTwo = new[]
        {
            "Weekly contracts: three a week, paying silver and account XP (extract with a unique, open two vaults, kill a boss in Tier II)",
            "A fourth dungeon tier if Tier III queues are healthy",
        };
    }

    /// <summary>
    /// The first playable build: every system once, half the content. Anything outside this list
    /// is still in the code base but is not in the slice.
    /// </summary>
    public static class VerticalSlice
    {
        public const int Teams = 4;
        public static readonly IReadOnlyList<string> Classes = new[] { "Warden", "Marksman", "Mender" };   // the three day-one classes (AccountRules.StartingClasses)
        public static readonly IReadOnlyList<string> Bosses = new[] { "Iron Juggernaut", "Hive Matron" };
        /// <summary>No Elementalist in the slice, so no element kit: the Elementalist is an earned class and lands with the second milestone.</summary>
        public static readonly IReadOnlyList<string> ElementKits = new string[0];
        /// <summary>Specs in the slice: one per class, the second spec of each comes after.</summary>
        public static readonly IReadOnlyList<string> Specs = new[] { "Sentinel", "Sharpshooter", "Warder" };
        /// <summary>The slice is Tier I only.</summary>
        public const int Tiers = 1;

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
