namespace DungeonGen.Core.Combat
{
    /// <summary>
    /// Locked: WASD movement, screen-relative under the fixed-yaw camera, with mouse aim. One
    /// control scheme for everyone; a controller maps to it (left stick move, right stick aim).
    /// No click-to-move. Mobility moves belong to specs, there is no universal dodge.
    /// </summary>
    public static class MovementRules
    {
        /// <summary>Metres per second at a sheet move speed of 1.0. One cell is 2 m, so 2.5 cells/s; the 8-team dungeon is walked edge to edge in about a minute.</summary>
        public const float BaseSpeedMps = 5f;
        /// <summary>W is always up the screen, which is always grid +Y: the fixed camera makes movement and the map agree.</summary>
        public const bool ScreenRelative = true;
        public const bool ClickToMove = false;
        public const bool UniversalDodge = false;
        /// <summary>Turning is free: the character faces the mouse, movement direction is independent of facing (strafing).</summary>
        public const bool FacingFollowsAim = true;
        /// <summary>Movement while channelling an extraction cancels it; abilities with a cast time root the caster for the cast.</summary>
        public const bool MovingCancelsExtractChannel = true;

        /// <summary>World speed for a character: sheet move speed (attributes, armour weight, affixes) times load.</summary>
        public static float SpeedMps(PlayerStats sheet, float loadKg) =>
            BaseSpeedMps * sheet.MoveSpeed * Items.CarryRules.MoveSpeedFactor(loadKg, sheet.CarryKg);

        /// <summary>Seconds to walk a straight line of cells at a given speed.</summary>
        public static float SecondsForCells(int cells, float speedMps) => cells * 2f / speedMps;
    }
}
