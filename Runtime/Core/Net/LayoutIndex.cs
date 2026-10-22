using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Items;

namespace DungeonGen.Core.Net
{
    /// <summary>
    /// Stable network ids for everything in a layout, assigned in a deterministic order so server
    /// and clients agree on them without exchanging a table. Also answers "which rooms are near
    /// this one" for interest management: a client only needs updates from rooms within a few hops.
    /// </summary>
    public sealed class LayoutIndex
    {
        public DungeonLayout Layout { get; }

        /// <summary>Item network id → item. Ids are 1-based in generation order (room id, then slot in the room's loot).</summary>
        public IReadOnlyDictionary<int, Item> Items => _items;
        public IReadOnlyDictionary<Item, int> ItemIds => _itemIds;
        /// <summary>Mob pack network id → pack. Ids = 1000 + room id.</summary>
        public IReadOnlyDictionary<int, MobPack> MobPacks => _packs;
        public IReadOnlyDictionary<int, int> ItemRoom => _itemRoom;

        private readonly Dictionary<int, Item> _items = new Dictionary<int, Item>();
        private readonly Dictionary<Item, int> _itemIds = new Dictionary<Item, int>();
        private readonly Dictionary<int, int> _itemRoom = new Dictionary<int, int>();
        private readonly Dictionary<int, MobPack> _packs = new Dictionary<int, MobPack>();

        public const int MobPackIdBase = 1000;
        public const int KeyIdBase = 5000;

        public LayoutIndex(DungeonLayout layout)
        {
            Layout = layout;
            int next = 1;
            foreach (var room in layout.Rooms.OrderBy(r => r.Id))
            {
                foreach (var it in room.Loot)
                {
                    _items[next] = it; _itemIds[it] = next; _itemRoom[next] = room.Id; next++;
                }
                if (room.Mobs != null) _packs[MobPackIdBase + room.Id] = room.Mobs;
            }
        }

        public int IdOf(Item it) => _itemIds[it];
        public static int IdOfKey(KeyPlacement k) => KeyIdBase + k.KeyId;
        public static int IdOfMobPack(Room r) => MobPackIdBase + r.Id;

        /// <summary>Rooms within <paramref name="hops"/> corridor hops of a room (inclusive). The relevancy set for a player standing there.</summary>
        public IReadOnlyCollection<int> RoomsWithin(int roomId, int hops)
        {
            var seen = new HashSet<int> { roomId };
            var frontier = new List<int> { roomId };
            for (int h = 0; h < hops; h++)
            {
                var next = new List<int>();
                foreach (int id in frontier)
                    foreach (int n in Layout.Rooms[id].Neighbours)
                        if (seen.Add(n)) next.Add(n);
                frontier = next;
            }
            return seen;
        }

        /// <summary>Which room a grid cell belongs to, or -1 in a corridor.</summary>
        public int RoomAt(GridPoint p) => Layout.InBounds(p) ? Layout.RoomIdAt[p.X, p.Y] : -1;
    }

    /// <summary>
    /// What a client is allowed to know before it opens a chest: the dungeon, the roles, the
    /// bosses and mobs, but not what is inside any container or where any key lies. The server
    /// reveals items one at a time through events. This is what stops a modified client from
    /// reading the vault's contents off the map.
    /// </summary>
    public static class ClientView
    {
        /// <summary>
        /// Regenerate the layout with loot disabled. Loot is rolled from its own RNG fork after
        /// everything else is fixed, so tiles, rooms, bosses, mobs and even key *holders* come out
        /// bit-identical; only Room.Loot / Items are empty. Key holder rooms are public knowledge
        /// (the Shade's Keen Sense reveals them), the items are not.
        /// </summary>
        public static DungeonLayout Redact(DungeonLayout server)
        {
            var s = SettingsCodec.FromBytes(SettingsCodec.ToBytes(server.Settings));
            s.LootEnabled = false;
            return DungeonGenerator.Generate(s, server.Seed);
        }

        /// <summary>Fingerprint of the public part only; what a client can verify without knowing the loot.</summary>
        public static ulong PublicFingerprint(DungeonLayout layout) => LayoutFingerprint.Compute(layout, includeServerOnly: false);
    }
}
