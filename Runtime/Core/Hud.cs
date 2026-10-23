using System;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Net;

namespace DungeonGen.Core.Hud
{
    public enum Cardinal { N, NE, E, SE, S, SW, W, NW }

    /// <summary>
    /// Bearings on the dungeon grid. North is +Y (up the screen with the locked camera), east is +X,
    /// clockwise, so a bearing of 90 is due east. The camera never rotates, so north is always up:
    /// the compass is not for finding north, it is a shared vocabulary ("boss is north-east") and a
    /// strip of markers for things you cannot see from where you stand.
    /// </summary>
    public static class Compass
    {
        /// <summary>Bearing from one cell to another in degrees, 0 = north (+Y), 90 = east (+X), [0, 360).</summary>
        public static float Bearing(GridPoint from, GridPoint to)
        {
            int dx = to.X - from.X, dy = to.Y - from.Y;
            if (dx == 0 && dy == 0) return 0f;
            float deg = (float)(Math.Atan2(dx, dy) * 180.0 / Math.PI);
            return Normalize(deg);
        }

        public static float Normalize(float degrees) { degrees %= 360f; if (degrees < 0f) degrees += 360f; return degrees; }

        /// <summary>Signed difference from the camera's up direction, in (-180, 180].</summary>
        public static float Relative(float bearing, float cameraYaw)
        {
            float d = Normalize(bearing - cameraYaw);
            return d > 180f ? d - 360f : d;
        }

        public static Cardinal CardinalOf(float bearing) => (Cardinal)(((int)Math.Round(Normalize(bearing) / 45f)) % 8);
        public static float BearingOf(Cardinal c) => (int)c * 45f;
        public static string Abbrev(Cardinal c) => c.ToString();
        public static string Name(Cardinal c)
        {
            switch (c)
            {
                case Cardinal.N: return "north"; case Cardinal.NE: return "north-east"; case Cardinal.E: return "east"; case Cardinal.SE: return "south-east";
                case Cardinal.S: return "south"; case Cardinal.SW: return "south-west"; case Cardinal.W: return "west"; default: return "north-west";
            }
        }

        public static float DistanceCells(GridPoint a, GridPoint b) { int dx = a.X - b.X, dy = a.Y - b.Y; return (float)Math.Sqrt(dx * dx + dy * dy); }
    }

    public enum CompassMarkerKind { Teammate, Ping, Extract, BossRoom, FinalBoss, BossExit, Shrine, Vault, Spawn }

    public readonly struct CompassMarker
    {
        public readonly CompassMarkerKind Kind;
        /// <summary>Absolute bearing, 0 = north.</summary>
        public readonly float Bearing;
        public readonly float DistanceCells;
        public readonly string Label;
        /// <summary>Player id for a teammate, room id for a room, ping kind for a ping.</summary>
        public readonly int Ref;
        public CompassMarker(CompassMarkerKind kind, float bearing, float distance, string label, int reference) { Kind = kind; Bearing = bearing; DistanceCells = distance; Label = label; Ref = reference; }
    }

    /// <summary>
    /// The strip across the top of the screen. Cardinal letters at their bearings and a marker for
    /// every teammate, live team ping, and every extract, boss room, shrine, vault and the centre
    /// exit the TEAM has seen. Nothing you have not explored is on it: the compass tells you where
    /// things you know about are, never where things you have not found are.
    /// </summary>
    public static class CompassBar
    {
        /// <summary>The strip shows this many degrees, centred on the camera's up. 180 means everything in the top half of the world is on the bar.</summary>
        public const float WindowDegrees = 180f;
        /// <summary>Room markers beyond this many cells fade to an edge chevron rather than a glyph.</summary>
        public const float NearCells = 30f;

        public static bool InWindow(float relativeBearing) => Math.Abs(relativeBearing) <= WindowDegrees / 2f;
        /// <summary>0..1 position along the strip for an in-window relative bearing; -180 (or the window's left edge) is 0, +window edge is 1.</summary>
        public static float Position01(float relativeBearing) => Math.Max(0f, Math.Min(1f, (relativeBearing + WindowDegrees / 2f) / WindowDegrees));

        public static List<CompassMarker> Markers(MatchState st, int player, GridPoint playerCell, IReadOnlyDictionary<int, GridPoint> playerCells, IEnumerable<TeamPing> pings, float now)
        {
            var list = new List<CompassMarker>();
            if (!st.PlayerTeam.TryGetValue(player, out int team)) return list;
            foreach (var kv in st.PlayerTeam.Where(kv => kv.Value == team && kv.Key != player && !st.DeadPlayers.Contains(kv.Key) && !st.ExtractedPlayers.Contains(kv.Key)))
                if (playerCells != null && playerCells.TryGetValue(kv.Key, out var cell))
                    list.Add(new CompassMarker(CompassMarkerKind.Teammate, Compass.Bearing(playerCell, cell), Compass.DistanceCells(playerCell, cell), $"Player {kv.Key}", kv.Key));
            if (pings != null)
                foreach (var p in pings.Where(p => p.Team == team && now - p.Time <= TeamPing.LifetimeSeconds))
                    list.Add(new CompassMarker(CompassMarkerKind.Ping, Compass.Bearing(playerCell, p.Cell), Compass.DistanceCells(playerCell, p.Cell), p.Kind.ToString(), (int)p.Kind));
            if (!st.Explored.TryGetValue(team, out var seen)) return list;
            foreach (int id in seen)
            {
                var room = st.Layout.Rooms[id];
                CompassMarkerKind kind; string label;
                switch (room.Type)
                {
                    case RoomType.Extract: kind = CompassMarkerKind.Extract; label = Strings.Get(st.ExtractsOpen ? "hud.extract_open" : "hud.extract"); break;
                    case RoomType.Boss: kind = CompassMarkerKind.BossRoom; label = st.DeadBosses.Contains(id) ? Strings.Get("hud.boss_dead") : room.Boss?.Archetype.Name ?? Strings.Get("hud.boss"); break;
                    case RoomType.FinalBoss: kind = st.BossExitOpen ? CompassMarkerKind.BossExit : CompassMarkerKind.FinalBoss; label = Strings.Get(st.BossExitOpen ? "hud.exit_open" : "hud.final_boss"); break;
                    case RoomType.Vault: kind = CompassMarkerKind.Vault; label = Strings.Get(st.OpenVaults.Contains(id) ? "hud.vault_open" : "hud.vault_locked"); break;
                    case RoomType.Spawn: if (st.Descriptor.TeamSpawnRooms[team] != id) continue; kind = CompassMarkerKind.Spawn; label = Strings.Get("hud.spawn"); break;
                    default: if (!room.HasShrine) continue; kind = CompassMarkerKind.Shrine; label = Strings.Get("hud.shrine"); break;
                }
                list.Add(new CompassMarker(kind, Compass.Bearing(playerCell, room.Center), Compass.DistanceCells(playerCell, room.Center), label, id));
            }
            return list;
        }

        /// <summary>A callout line for a marker: "Shrine, north-east, 24 m".</summary>
        public static string Callout(CompassMarker m, float cellMetres = 2f) => $"{m.Label}, {Compass.Name(Compass.CardinalOf(m.Bearing))}, {Math.Round(m.DistanceCells * cellMetres)} m";
    }

    public readonly struct MiniMapRoom
    {
        public readonly int Id; public readonly GridRect Bounds; public readonly RoomType Type;
        public readonly bool HasShrine, BossDead, VaultOpen, IsTeamSpawn, IsHere;
        public MiniMapRoom(int id, GridRect bounds, RoomType type, bool shrine, bool bossDead, bool vaultOpen, bool teamSpawn, bool here)
        { Id = id; Bounds = bounds; Type = type; HasShrine = shrine; BossDead = bossDead; VaultOpen = vaultOpen; IsTeamSpawn = teamSpawn; IsHere = here; }
    }

    public enum MiniMapMarkerKind { Self, Teammate, Ping, LockedDoor, BossExit, ClosedDoor, Hazard, DisarmedHazard }

    public readonly struct MiniMapMarker
    {
        public readonly MiniMapMarkerKind Kind; public readonly GridPoint Cell; public readonly int Ref;
        public MiniMapMarker(MiniMapMarkerKind kind, GridPoint cell, int reference) { Kind = kind; Cell = cell; Ref = reference; }
    }

    /// <summary>What the minimap draws: explored rooms, the corridors between two explored rooms, and markers.</summary>
    public sealed class MiniMapModel
    {
        public List<MiniMapRoom> Rooms { get; } = new List<MiniMapRoom>();
        /// <summary>Pairs of explored room ids that are joined by a corridor.</summary>
        public List<(int a, int b)> Corridors { get; } = new List<(int, int)>();
        public List<MiniMapMarker> Markers { get; } = new List<MiniMapMarker>();
        public GridPoint Centre { get; set; }
        /// <summary>Cells the small corner map shows in each direction from the player. The full map (hold Tab) ignores it.</summary>
        public const int ViewRadiusCells = 18;
        public bool Shows(GridRect r) => r.XMax >= Centre.X - ViewRadiusCells && r.X <= Centre.X + ViewRadiusCells && r.YMax >= Centre.Y - ViewRadiusCells && r.Y <= Centre.Y + ViewRadiusCells;
    }

    /// <summary>
    /// Explored rooms only, by decision. The server adds rooms to the team's Explored set as line of
    /// sight reveals them; the minimap never shows a room nobody on the team has seen, and never
    /// shows an enemy. Teammates and live pings are the only moving things on it.
    /// </summary>
    public static class MiniMap
    {
        public static MiniMapModel Build(MatchState st, int player, GridPoint playerCell, IReadOnlyDictionary<int, GridPoint> playerCells, IEnumerable<TeamPing> pings, float now)
        {
            var m = new MiniMapModel { Centre = playerCell };
            if (!st.PlayerTeam.TryGetValue(player, out int team)) return m;
            var seen = st.Explored.TryGetValue(team, out var s) ? s : new HashSet<int>();
            int here = st.PlayerRoom.TryGetValue(player, out int hr) ? hr : -1;
            foreach (int id in seen.OrderBy(i => i))
            {
                var r = st.Layout.Rooms[id];
                m.Rooms.Add(new MiniMapRoom(id, r.Bounds, r.Type, r.HasShrine, st.DeadBosses.Contains(id), st.OpenVaults.Contains(id), st.Descriptor.TeamSpawnRooms[team] == id, id == here));
                foreach (int n in r.Neighbours) if (n > id && seen.Contains(n)) m.Corridors.Add((id, n));
                if (r.Type == RoomType.Vault && !st.OpenVaults.Contains(id)) foreach (var d in r.Doors) m.Markers.Add(new MiniMapMarker(MiniMapMarkerKind.LockedDoor, d, id));
                if (r.Type == RoomType.FinalBoss && st.BossExitOpen && st.Layout.BossExit != null) m.Markers.Add(new MiniMapMarker(MiniMapMarkerKind.BossExit, st.Layout.BossExit.Cell, id));
            }
            foreach (var d in st.ClosedDoors.Where(c => seen.Contains(st.Layout.RoomIdAt[c.X, c.Y]) || st.Layout.Rooms[st.Layout.RoomIdAt[c.X, c.Y]].Neighbours.Any(seen.Contains)))
                m.Markers.Add(new MiniMapMarker(MiniMapMarkerKind.ClosedDoor, d, st.Index.DoorIdAt.TryGetValue(d, out int did) ? did : -1));
            foreach (var h in st.HazardsVisibleTo(team))
                m.Markers.Add(new MiniMapMarker(st.DisarmedHazards.Contains(h.Id) ? MiniMapMarkerKind.DisarmedHazard : MiniMapMarkerKind.Hazard, h.Bounds.Center, h.Id));
            m.Markers.Add(new MiniMapMarker(MiniMapMarkerKind.Self, playerCell, player));
            foreach (var kv in st.PlayerTeam.Where(kv => kv.Value == team && kv.Key != player && !st.DeadPlayers.Contains(kv.Key) && !st.ExtractedPlayers.Contains(kv.Key)))
                if (playerCells != null && playerCells.TryGetValue(kv.Key, out var c)) m.Markers.Add(new MiniMapMarker(MiniMapMarkerKind.Teammate, c, kv.Key));
            if (pings != null)
                foreach (var p in pings.Where(p => p.Team == team && now - p.Time <= TeamPing.LifetimeSeconds))
                    m.Markers.Add(new MiniMapMarker(MiniMapMarkerKind.Ping, p.Cell, (int)p.Kind));
            return m;
        }
    }
}
