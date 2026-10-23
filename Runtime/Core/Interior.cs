using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core
{
    public enum InteriorKind : byte { Pillar, LowWall }

    public readonly struct InteriorFeature
    {
        public readonly InteriorKind Kind; public readonly GridRect Bounds;
        public InteriorFeature(InteriorKind kind, GridRect bounds) { Kind = kind; Bounds = bounds; }
        public TileType Tile => Kind == InteriorKind.Pillar ? TileType.Pillar : TileType.LowWall;
    }

    public enum HazardKind : byte
    {
        SpikeTrap,   // one cell, passable, hidden; a chunk of health to whoever steps on it, then rearms
        GasVent,     // a small zone, passable, hidden; Poisoned while standing in it
        Pit,         // collapsed floor, impassable, obvious; painted as a tile
        Lava,        // a strip, passable, obvious; Burn while standing in it
    }

    public sealed class Hazard
    {
        public int Id { get; }
        public HazardKind Kind { get; }
        public GridRect Bounds { get; }
        public int RoomId { get; }
        public Hazard(int id, HazardKind kind, GridRect bounds, int roomId) { Id = id; Kind = kind; Bounds = bounds; RoomId = roomId; }
        public bool Hidden => HazardRules.Hidden(Kind);
        public bool Disarmable => HazardRules.Disarmable(Kind);
        public override string ToString() => $"{Kind} #{Id} in room {RoomId} at {Bounds}";
    }

    /// <summary>
    /// What hazards do and who sees them. Traps and vents are hidden until they fire; a Shade sees
    /// every hazard and is the only class that can disarm one (2 s channel, broken by damage).
    /// Pits and lava are obvious to everyone and cannot be disarmed. Damage is a fraction of max
    /// health so a hazard means the same thing to a naked player and a geared one.
    /// </summary>
    public static class HazardRules
    {
        public const int IdBase = 8000;
        public const string DisarmClass = "Shade";
        public const float DisarmSeconds = 2f;
        public const float SpikeDamageFraction = 0.25f;
        public const float SpikeRearmSeconds = 10f;
        public const float GasPoisonPerSecondFraction = 0.04f;
        public const float LavaBurnPerSecondFraction = 0.08f;
        public static bool Hidden(HazardKind k) => k == HazardKind.SpikeTrap || k == HazardKind.GasVent;
        public static bool Disarmable(HazardKind k) => k == HazardKind.SpikeTrap || k == HazardKind.GasVent;
        public static bool Passable(HazardKind k) => k != HazardKind.Pit;
        public static bool CanSee(Combat.ClassArchetype cls, Hazard h) => !h.Hidden || (cls != null && cls.Name == DisarmClass);
        public static bool CanDisarm(Combat.ClassArchetype cls, Hazard h) => h.Disarmable && cls != null && cls.Name == DisarmClass;
    }

    /// <summary>
    /// Doors a player can shut. Closing is instant from either side; opening takes a channel, broken
    /// by damage, from either side. A closed door blocks sight and movement for everyone, mobs
    /// included. Vault doors are locks, not doors, and are not covered here. Nobody breaks a door:
    /// the Breaker makes space with impact, not by smashing the dungeon.
    /// </summary>
    public static class DoorRules
    {
        public const int IdBase = 7000;
        public const float OpenSeconds = 3f;
        public const bool CloseIsInstant = true;
        public const bool Breakable = false;
        /// <summary>A player may work a door from inside its room or from the room on the other side of the corridor it leads to.</summary>
        public const int ReachRooms = 1;
    }
}
