namespace DungeonGen.Core.Net
{
    /// <summary>
    /// What the whole dungeon is told. The final boss falling, the centre exit opening, and the
    /// round's one mid-round event beginning. Everything else about other teams is earned by seeing
    /// or hearing it.
    /// </summary>
    public static class Announcer
    {
        public readonly struct Announcement
        {
            public readonly string Text, Sound;
            public Announcement(string text, string sound) { Text = text; Sound = sound; }
        }

        /// <summary>The global announcement an event carries, or null for the vast majority that carry none.</summary>
        public static Announcement? For(MatchEvent e, DungeonLayout layout)
        {
            switch (e.Kind)
            {
                case MatchEventKind.BossKilled when e.A >= 0 && e.A < layout.Rooms.Count && layout.Rooms[e.A].Type == RoomType.FinalBoss:
                    return new Announcement(Strings.Get("announce.final_boss_dead"), "announce.final_boss_dead");
                case MatchEventKind.BossExitOpened:
                    return new Announcement(Strings.Get("announce.exit_open"), "announce.exit_open");
                case MatchEventKind.EventStarted:
                    {
                        var kind = (DungeonEventKind)e.A;
                        string key = kind == DungeonEventKind.RoamingElite ? "event.elite" : kind == DungeonEventKind.VaultUnsealed ? "event.vault" : "event.shrine";
                        return new Announcement(Strings.Get(key), "announce." + key);
                    }
                default:
                    return null;
            }
        }
    }
}
