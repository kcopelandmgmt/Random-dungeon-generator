using System;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Combat;

namespace DungeonGen.Core.Stages
{
    /// <summary>
    /// Stage 6b: station a mob pack in every ordinary room, sized by the room's danger, with the
    /// damage types spread so that no single resistance carries a dungeon. Rooms next to a boss
    /// draw from that boss's retinue so the approach telegraphs the fight.
    /// </summary>
    public static class MobPlacer
    {
        public static void Assign(DungeonLayout layout, DungeonSettings s, DungeonRandom rng)
        {
            if (!s.MobsEnabled) return;
            var roster = MobArchetype.DefaultRoster;
            var typeCounts = new Dictionary<DamageType, int>();
            foreach (DamageType t in Enum.GetValues(typeof(DamageType))) typeCounts[t] = 0;

            foreach (var room in layout.Rooms.OrderBy(r => r.Id))
            {
                if (room.Type == RoomType.Spawn || room.Type == RoomType.Extract || room.Type == RoomType.Boss || room.Type == RoomType.FinalBoss) continue;

                MobArchetype pick = null;

                // Boss retinue: the rooms around a boss spawn its kind.
                var bossNeighbour = room.Neighbours.Select(id => layout.Rooms[id]).FirstOrDefault(n => n.Type == RoomType.Boss && n.Boss?.Archetype.RetinueMob != null);
                if (bossNeighbour != null && rng.Chance(0.75f))
                    pick = roster.FirstOrDefault(m => m.Name == bossNeighbour.Boss.Archetype.RetinueMob);

                // Otherwise weight toward damage types this dungeon has used least so far.
                if (pick == null)
                {
                    int maxUsed = typeCounts.Values.Max();
                    var weighted = roster.Select(m => (mob: m, w: 1 + (maxUsed - typeCounts[m.Damage.Dominant]) * 2)).ToList();
                    int total = weighted.Sum(x => x.w);
                    int r = rng.Range(0, total);
                    foreach (var (mob, w) in weighted) { r -= w; if (r < 0) { pick = mob; break; } }
                    pick ??= roster[0];
                }

                typeCounts[pick.Damage.Dominant]++;

                // Pack budget in player-equivalents from danger, spent on as many of this mob as it buys.
                // A room boss is 2.5 PE, so the deepest ordinary room (2.0) is a real fight for a trio
                // and the first room out of spawn (0.5) is a warm-up.
                float budget = (s.MobBudgetAtEdge + room.Danger * (s.MobBudgetAtCenter - s.MobBudgetAtEdge)) * s.MobDensity;
                if (room.Type == RoomType.Loot || room.Type == RoomType.Vault) budget += s.MobBudgetLootBonus * s.MobDensity;
                float tier = 0.8f + room.Danger * 0.6f;                      // 0.8 at the spawn edge, 1.4 at the center
                float perMob = MobPack.PlayerEquivalentsPerMob(pick) * tier;
                int count = Math.Max(1, Math.Min(12, (int)Math.Round(budget / perMob)));
                room.Mobs = new MobPack(pick, count, tier);
                layout.MobPacks.Add(room.Mobs);
            }
        }
    }
}
