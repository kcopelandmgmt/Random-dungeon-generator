using DungeonGen.Core.Profile;

namespace DungeonGen.Unity
{
    /// <summary>
    /// What the menu chose for the next offline round. <see cref="OfflineMatch.Host"/> reads <see cref="Current"/>: when set it
    /// takes the seed, the bot teams, dev casting off and seats the local player with the character's <see cref="Loadout"/>;
    /// when null (greybox Play, <c>-autotest</c>, the PlayMode suite) it keeps its inspector defaults.
    /// </summary>
    public sealed class PracticeLaunch
    {
        public static PracticeLaunch Current;

        public int Seed = 7;
        /// <summary>Enemy bot teams, 1..3.</summary>
        public int BotTeams = 2;
        public int BotsPerTeam = 2;
        /// <summary>Bots seated on the player's own team (they stand at the spawn and fight).</summary>
        public int AllyBots;
        /// <summary>Off for a real round: cooldowns, costs, locked ultimates.</summary>
        public bool DevCasting;
        /// <summary>The character's gear, seated with <c>MatchServer.AddPlayerWithLoadout</c>.</summary>
        public Loadout Loadout;
        public int AccountLevel = 1;
        /// <summary>Hide dev-only panels (the effects audition) until F1.</summary>
        public bool HideDevPanels = true;
        /// <summary>
        /// The tutorial (wave 5.10): <see cref="OfflineMatch.Host"/> builds the tutorial's small dungeon and rules (<c>TutorialScript</c>), seats two
        /// friendly bots (<c>TutorialDirector</c>) instead of enemy teams, and <see cref="GameFlow"/> never settles the round into the profile: the
        /// <see cref="Loadout"/> belongs to a throwaway profile.
        /// </summary>
        public bool Tutorial;
    }

    /// <summary>Round UI that wants the round's transport once the offline host has made it (events for announcements).</summary>
    public interface IRoundUi
    {
        void Attach(IMatchTransport transport);
    }
}
