using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Combat;

namespace DungeonGen.Core.Stages
{
    /// <summary>
    /// Stage 4b: decide *which* boss each boss room holds. Ring placement may already have chosen
    /// archetypes (it needs them to size arenas); this stage fills the rest, never repeating an
    /// archetype within one dungeon, rolls affixes, pins Keybearer on every boss that drops a key,
    /// and folds archetype and affix multipliers into the encounter budget.
    /// </summary>
    public static class BossRoller
    {
        public static void Assign(List<Room> rooms, DungeonSettings s, DungeonRandom rng, List<string> warnings)
        {
            var bossRooms = rooms.Where(r => r.Type == RoomType.Boss).OrderBy(r => r.Id).ToList();

            // --- Archetypes: one family at a time first, then anything left, never a repeat. ---
            var used = new HashSet<BossArchetype>(bossRooms.Where(r => r.Boss != null).Select(r => r.Boss.Archetype));
            foreach (var room in bossRooms.Where(r => r.Boss == null))
            {
                var pick = PickArchetype(used, room, rng, s.AllowedBosses);
                if (pick == null)
                {
                    warnings.Add($"More boss rooms ({bossRooms.Count}) than allowed archetypes; repeating one.");
                    pick = rng.Pick(s.AllowedBosses == null ? BossArchetype.DefaultPool : BossArchetype.DefaultPool.Where(a => s.AllowedBosses.Contains(a.Name)).ToList());
                }
                used.Add(pick);
                room.Boss = new BossInstance(pick);
            }

            // --- Affixes and budgets -------------------------------------------------------
            var baseBudget = EncounterTuner.RoomBoss(s.Combat);
            foreach (var room in bossRooms)
            {
                var boss = room.Boss;
                if (s.BossAffixesEnabled) RollAffixes(boss, rng, s.MaxAffixesPerBoss);
                if (room.HoldsKeyId >= 0) boss.Affixes.Insert(0, BossAffix.Keybearer);
                boss.Budget = Scale(baseBudget, boss);
                room.Encounter = boss.Budget;
            }

            foreach (var room in rooms.Where(r => r.Type == RoomType.FinalBoss))
            {
                room.Boss = new BossInstance(BossArchetype.FinalBoss);
                room.Boss.Budget = Scale(EncounterTuner.FinalBoss(s.Combat), room.Boss);
                room.Encounter = room.Boss.Budget;
            }
        }

        /// <summary>Draw an archetype the dungeon has not used, preferring a family not yet present, then one whose arena fits the room.</summary>
        public static BossArchetype PickArchetype(HashSet<BossArchetype> used, Room room, DungeonRandom rng, IReadOnlyList<string> allowed = null)
        {
            var pool0 = allowed == null ? BossArchetype.DefaultPool : BossArchetype.DefaultPool.Where(a => allowed.Contains(a.Name)).ToList();
            var unused = pool0.Where(a => !used.Contains(a)).ToList();
            if (unused.Count == 0) return null;
            var usedFamilies = new HashSet<BossFamily>(used.Select(a => a.Family));
            var freshFamily = unused.Where(a => !usedFamilies.Contains(a.Family)).ToList();
            var pool = freshFamily.Count > 0 ? freshFamily : unused;

            if (room != null)
            {
                int side = System.Math.Max(room.Bounds.Width, room.Bounds.Height);
                var fits = pool.Where(a => side >= a.ArenaMin - 1).ToList();
                if (fits.Count > 0) pool = fits;
            }
            return rng.Pick(pool);
        }

        private static void RollAffixes(BossInstance boss, DungeonRandom rng, int max)
        {
            // 40% none, 40% one, 20% two (capped by max).
            float roll = rng.NextFloat();
            int count = roll < 0.4f ? 0 : roll < 0.8f ? 1 : 2;
            count = System.Math.Min(count, max);

            var available = BossAffix.DefaultPool.Where(a => a.Weight > 0).ToList();
            for (int i = 0; i < count && available.Count > 0; i++)
            {
                int total = available.Sum(a => a.Weight);
                int r = rng.Range(0, total);
                BossAffix chosen = available[0];
                foreach (var a in available)
                {
                    r -= a.Weight;
                    if (r < 0) { chosen = a; break; }
                }
                boss.Affixes.Add(chosen);
                available.Remove(chosen);
            }
        }

        private static EncounterBudget Scale(EncounterBudget b, BossInstance boss)
        {
            float hp = boss.Archetype.HealthMultiplier, dps = boss.Archetype.DpsMultiplier, enrage = 1f;
            foreach (var a in boss.Affixes) { hp *= a.HealthMultiplier; dps *= a.DpsMultiplier; enrage *= a.EnrageMultiplier; }
            return new EncounterBudget(b.Kind, b.PlayerEquivalents, b.GearMultiplier, b.Health * hp, b.Dps * dps, b.TargetSeconds, b.EnrageSeconds * enrage);
        }
    }
}
