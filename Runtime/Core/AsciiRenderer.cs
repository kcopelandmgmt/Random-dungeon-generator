using System.Linq;
using System.Text;

namespace DungeonGen.Core
{
    /// <summary>Debug view of a layout. Handy in the console tool and in Unity's log.</summary>
    public static class AsciiRenderer
    {
        public static string Render(DungeonLayout layout, bool includeLegend = true)
        {
            var sb = new StringBuilder(layout.Width * layout.Height + 1024);
            var labels = new char[layout.Width, layout.Height];

            foreach (var room in layout.Rooms)
            {
                char c = room.Type switch
                {
                    RoomType.Spawn => 'S',
                    RoomType.Extract => 'E',
                    RoomType.Vault => 'V',
                    RoomType.Loot => 'L',
                    RoomType.Hub => 'H',
                    RoomType.Boss => 'B',
                    RoomType.FinalBoss => 'F',
                    _ => (char)('0' + room.LootTier),
                };
                labels[room.Center.X, room.Center.Y] = c;
            }
            foreach (var k in layout.Keys)
            {
                var p = k.Cell + GridPoint.Right;
                if (layout.InBounds(p) && layout.Tiles[p.X, p.Y] == TileType.RoomFloor) labels[p.X, p.Y] = 'k';
            }
            if (layout.BossExit != null)
            {
                var p = layout.BossExit.Cell + GridPoint.Down;
                if (layout.InBounds(p) && layout.Tiles[p.X, p.Y] == TileType.RoomFloor) labels[p.X, p.Y] = 'X';
            }

            for (int y = layout.Height - 1; y >= 0; y--)
            {
                for (int x = 0; x < layout.Width; x++)
                {
                    if (labels[x, y] != '\0') { sb.Append(labels[x, y]); continue; }
                    sb.Append(layout.Tiles[x, y] switch
                    {
                        TileType.Void => ' ',
                        TileType.Wall => '#',
                        TileType.RoomFloor => '.',
                        TileType.Corridor => ':',
                        TileType.Door => '+',
                        TileType.LockedDoor => '=',
                        _ => '?',
                    });
                }
                sb.AppendLine();
            }

            if (includeLegend)
            {
                sb.AppendLine();
                sb.AppendLine($"Seed {layout.Seed}  |  {layout.Rooms.Count} rooms, {layout.Corridors.Count} corridors ({layout.Corridors.Count(c => c.IsLoop)} loops)");
                sb.AppendLine("S spawn  E extract  B boss  F final boss (X = gated exit)  V vault (locked)  k key  L loot room  H hub  0-9 standard room (digit = loot tier)");
                sb.AppendLine(". room floor  : corridor  + door  = locked door  # wall");
                foreach (var r in layout.Rooms.OrderBy(r => r.DepthFromSpawn))
                    sb.AppendLine($"  {r.Id,2} {r.Type,-8} depth {r.DepthFromSpawn}  danger {r.Danger:0.00}  tier {r.LootTier}  exits {r.Neighbours.Count}");
                foreach (var r in layout.Rooms.Where(r => r.Encounter != null).OrderBy(r => r.Type))
                    sb.AppendLine($"  room {r.Id,2} {r.Encounter}");
                foreach (var k in layout.Keys)
                    sb.AppendLine($"  key {k.KeyId} opens vault {k.VaultRoomId}: {(k.DroppedByBoss ? "dropped by boss in" : "chest in")} room {k.HolderRoomId}, {k.WalkToVault:0} cells away");
                var f = layout.Fairness;
                if (f.Spawns.Count > 0)
                {
                    sb.AppendLine($"Fairness {f.Score:0.00}  (extract {f.ExtractRatio:0.00}, final boss {f.FinalBossRatio:0.00}, boss {f.BossRatio:0.00}, spacing {f.SpawnSpacingRatio:0.00})  walking cells per team:");
                    foreach (var sp in f.Spawns)
                        sb.AppendLine($"  spawn {sp.RoomId,2}: nearest extract {sp.ToNearestExtract,3}  final boss {sp.ToFinalBoss,3}  nearest boss {sp.ToNearestBoss,3}  nearest rival {sp.ToNearestSpawn,3}");
                }
                foreach (var w in layout.Warnings) sb.AppendLine("  ! " + w);
            }
            return sb.ToString();
        }
    }
}
