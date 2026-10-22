using System.Linq;

namespace DungeonGen.Core.Stages
{
    /// <summary>
    /// Stage 6c: resurrection shrines. One per hub room (hubs are where routes cross, so a res is
    /// always a contested walk), and at least one per dungeon: if no room became a hub, the
    /// standard room with the most exits gets it.
    /// </summary>
    public static class ShrinePlacer
    {
        public static void Assign(DungeonLayout layout, DungeonSettings s)
        {
            if (!s.ShrinesEnabled) return;
            var hubs = layout.RoomsOfType(RoomType.Hub).ToList();
            if (hubs.Count == 0)
            {
                var fallback = layout.Rooms.Where(r => r.Type == RoomType.Standard).OrderByDescending(r => r.Neighbours.Count).ThenBy(r => r.Id).FirstOrDefault();
                if (fallback != null) hubs.Add(fallback);
            }
            foreach (var r in hubs) r.HasShrine = true;
        }
    }
}
