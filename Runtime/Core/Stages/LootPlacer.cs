using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Items;

namespace DungeonGen.Core.Stages
{
    /// <summary>
    /// Stage 8: roll the actual items in every room from the seed. The server owns the result;
    /// clients only ever see what they open. Item level = the room's loot tier, so depth is
    /// rarity, and bosses, vaults and the final boss have their own floors and unique chances.
    /// </summary>
    public static class LootPlacer
    {
        public static void Assign(DungeonLayout layout, DungeonSettings s, DungeonRandom rng)
        {
            if (!s.LootEnabled) return;
            foreach (var room in layout.Rooms.OrderBy(r => r.Id))
            {
                int count;
                LootSource source;
                string theme = null;
                switch (room.Type)
                {
                    case RoomType.Spawn:
                    case RoomType.Extract: continue;
                    case RoomType.Loot: count = 3; source = LootSource.LootRoom; break;
                    case RoomType.Vault: count = 4; source = LootSource.Vault; break;
                    case RoomType.Boss: count = 2 + (room.Boss?.BonusLootRolls ?? 0); source = LootSource.Boss; theme = room.Boss?.Archetype.Name; break;
                    case RoomType.FinalBoss: count = 4; source = LootSource.FinalBoss; theme = room.Boss?.Archetype.Name; break;
                    default: count = rng.Chance(0.5f) ? 2 : 1; source = LootSource.Chest; break;
                }
                count = (int)System.Math.Round(count * s.LootDensity);
                for (int i = 0; i < count; i++)
                {
                    var item = LootRoller.Roll(room.LootTier, source, rng, theme);
                    room.Loot.Add(item);
                    layout.Items.Add(item);
                }
            }
        }
    }
}
