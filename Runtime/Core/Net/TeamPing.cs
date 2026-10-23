using System.IO;

namespace DungeonGen.Core.Net
{
    /// <summary>The only built-in communication: team-scoped pings on the map or world. No voice.</summary>
    public enum PingKind : byte { Look, Enemy, Loot, Danger, GoHere, Boss, Key, Extract, Help }

    /// <summary>Transient and team-only: never enters the match log, never reaches another team.</summary>
    public readonly struct TeamPing
    {
        /// <summary>How long a ping stays on the compass and minimap.</summary>
        public const float LifetimeSeconds = 8f;
        public readonly int FromPlayer;
        public readonly int Team;
        public readonly PingKind Kind;
        public readonly GridPoint Cell;
        public readonly float Time;

        public TeamPing(int fromPlayer, int team, PingKind kind, GridPoint cell, float time) { FromPlayer = fromPlayer; Team = team; Kind = kind; Cell = cell; Time = time; }

        public byte[] ToBytes()
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms)) { w.Write(FromPlayer); w.Write(Team); w.Write((byte)Kind); w.Write(Cell.X); w.Write(Cell.Y); w.Write(Time); }
            return ms.ToArray();
        }

        public static TeamPing FromBytes(byte[] b)
        {
            using var ms = new MemoryStream(b);
            using var r = new BinaryReader(ms);
            return new TeamPing(r.ReadInt32(), r.ReadInt32(), (PingKind)r.ReadByte(), new GridPoint(r.ReadInt32(), r.ReadInt32()), r.ReadSingle());
        }
    }
}
