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
                    _ => (char)('0' + room.LootTier),
                };
                labels[room.Center.X, room.Center.Y] = c;
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
                        _ => '?',
                    });
                }
                sb.AppendLine();
            }

            if (includeLegend)
            {
                sb.AppendLine();
                sb.AppendLine($"Seed {layout.Seed}  |  {layout.Rooms.Count} rooms, {layout.Corridors.Count} corridors ({layout.Corridors.Count(c => c.IsLoop)} loops)");
                sb.AppendLine("S spawn  E extract  V vault  L loot room  H hub  0-9 standard room (digit = loot tier)");
                sb.AppendLine(". room floor  : corridor  + door  # wall");
                foreach (var r in layout.Rooms.OrderBy(r => r.DepthFromSpawn))
                    sb.AppendLine($"  {r.Id,2} {r.Type,-8} depth {r.DepthFromSpawn}  danger {r.Danger:0.00}  tier {r.LootTier}  exits {r.Neighbours.Count}");
                foreach (var w in layout.Warnings) sb.AppendLine("  ! " + w);
            }
            return sb.ToString();
        }
    }
}
