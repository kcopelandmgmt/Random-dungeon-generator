namespace DungeonGen.Core.Combat
{
    /// <summary>
    /// Moment-to-moment rules that are decisions, not tuning. Each is a constant so the game code
    /// reads the rule instead of guessing it, and so a change is one line with a test behind it.
    /// </summary>
    public static class CombatRules
    {
        /// <summary>Full friendly fire: your damage hits your team at full strength and your zones, knockbacks and slows apply to them. Killing a teammate grants no soul (MatchState).</summary>
        public const float AllyDamageMultiplier = 1f;
        public const bool AlliesAffectedByZones = true;
        public const bool AlliesAffectedByKnockback = true;

        /// <summary>Extracting is an 8 s channel standing in the gate; any damage taken resets it. Extracts never close and have no capacity.</summary>
        public const float ExtractChannelSeconds = 8f;
        public const bool ExtractInterruptedByDamage = true;

        /// <summary>Boss chests and player corpses are open to anyone, immediately. Corpses persist until the round ends.</summary>
        public const bool BossChestOpenToAnyone = true;
        public const bool CorpsesOpenToAnyone = true;
    }

    /// <summary>Mobs are obstacles, not a faction: they stay in their room, fight whoever enters (two teams at once if it comes to that), reset when the room empties, and never come back once cleared.</summary>
    public static class MobRules
    {
        public const bool LeashToRoom = true;
        public const bool AttackAnyTeam = true;
        public const bool ResetWhenRoomEmpty = true;
        public const bool Respawn = false;
        /// <summary>Cells beyond the room's doors a mob will follow before turning back.</summary>
        public const int LeashCells = 2;
    }

    /// <summary>
    /// Sound is the second vision system: top-down players learn about what is outside the cone
    /// from what they hear. Radii in metres (1 cell = 2 m); walls attenuate but do not silence.
    /// </summary>
    public static class AudioCues
    {
        public const float FootstepsRadiusM = 12f;
        public const float CombatRadiusM = 20f;
        public const float BossFightRadiusM = 30f;
        public const float VaultDoorRadiusM = 25f;
        public const float ExtractChannelRadiusM = 20f;
        public const float ShrineRadiusM = 15f;
        /// <summary>Multiplier applied per wall between source and listener.</summary>
        public const float PerWallAttenuation = 0.5f;
    }
}
