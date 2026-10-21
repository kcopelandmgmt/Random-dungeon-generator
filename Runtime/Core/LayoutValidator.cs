using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core
{
    /// <summary>
    /// Post-generation sanity checks. A layout that fails these would make a round unplayable
    /// (e.g. an extract nobody can reach), so the generator uses them to decide whether to
    /// retry with a derived seed.
    /// </summary>
    public static class LayoutValidator
    {
        public static bool IsPlayable(DungeonLayout layout, DungeonSettings s, List<string> problems)
        {
            bool ok = true;

            int spawns = layout.SpawnRooms.Count();
            int extracts = layout.ExtractRooms.Count();
            if (spawns < s.SpawnCount) { problems.Add($"Only {spawns}/{s.SpawnCount} spawn rooms."); ok = false; }
            if (extracts < s.ExtractCount) { problems.Add($"Only {extracts}/{s.ExtractCount} extract rooms."); ok = false; }
            if (layout.FinalBossRoom == null) { problems.Add("No final boss room."); ok = false; }
            int bosses = layout.BossRooms.Count();
            if (bosses < s.BossCount) { problems.Add($"Only {bosses}/{s.BossCount} boss rooms."); ok = false; }
            foreach (var v in layout.VaultRooms)
            {
                if (v.RequiredKeyId < 0) { problems.Add($"Vault {v.Id} has no key."); ok = false; }
                if (v.Doors.Any(d => layout.Tiles[d.X, d.Y] != TileType.LockedDoor)) { problems.Add($"Vault {v.Id} has an unlocked door."); ok = false; }
            }
            foreach (var k in layout.Keys)
                if (layout.Rooms[k.HolderRoomId].Type == RoomType.Vault) { problems.Add($"Key {k.KeyId} is inside a vault."); ok = false; }

            // Flood fill the walkable tiles from the first spawn (locked doors closed) and make
            // sure every non-vault room is touched; then open the locks and require the vaults too.
            var origin = layout.SpawnRooms.FirstOrDefault();
            if (origin == null) return false;

            var reached = new HashSet<int>();
            var seen = new bool[layout.Width, layout.Height];
            var stack = new Stack<GridPoint>();
            stack.Push(origin.Center);
            seen[origin.Center.X, origin.Center.Y] = true;

            while (stack.Count > 0)
            {
                var p = stack.Pop();
                int roomId = layout.RoomIdAt[p.X, p.Y];
                if (roomId >= 0) reached.Add(roomId);

                foreach (var d in GridPoint.Cardinals)
                {
                    var n = p + d;
                    if (!layout.InBounds(n) || seen[n.X, n.Y] || !layout.IsWalkable(n)) continue;
                    seen[n.X, n.Y] = true;
                    stack.Push(n);
                }
            }

            var unreachable = layout.Rooms.Where(r => r.Type != RoomType.Vault && !reached.Contains(r.Id)).Select(r => r.Id).ToList();
            if (unreachable.Count > 0)
            {
                problems.Add($"Rooms unreachable without keys from spawn {origin.Id}: {string.Join(", ", unreachable)}");
                ok = false;
            }

            foreach (var v in layout.VaultRooms)
            {
                // Each locked door must open straight onto reached territory.
                bool adjacent = v.Doors.Any(d => GridPoint.Cardinals.Any(c => layout.InBounds(d + c) && seen[(d + c).X, (d + c).Y]));
                if (!adjacent) { problems.Add($"Vault {v.Id} is not adjacent to reachable space."); ok = false; }
            }

            return ok;
        }
    }
}
