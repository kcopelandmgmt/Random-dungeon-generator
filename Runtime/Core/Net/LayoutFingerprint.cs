using System.Linq;

namespace DungeonGen.Core.Net
{
    /// <summary>
    /// A stable hash of everything gameplay-relevant in a layout: tiles, room roles and bounds,
    /// corridors, keys, bosses, mobs and items. Server and clients compute it independently; if
    /// they differ, someone generated a different dungeon and the round must not start.
    /// </summary>
    public static class LayoutFingerprint
    {
        public static ulong Compute(DungeonLayout l, bool includeServerOnly = true)
        {
            var h = StableHash.Start();
            h.Add(l.Seed).Add(l.Width).Add(l.Height);
            for (int y = 0; y < l.Height; y++)
                for (int x = 0; x < l.Width; x++)
                    h.Add((byte)l.Tiles[x, y]);

            foreach (var r in l.Rooms.OrderBy(r => r.Id))
            {
                h.Add(r.Id).Add((int)r.Type).Add(r.Bounds.X).Add(r.Bounds.Y).Add(r.Bounds.Width).Add(r.Bounds.Height);
                h.Add(r.LootTier).Add(r.Danger).Add(r.RequiredKeyId);
                foreach (var d in r.Doors) h.Add(d.X).Add(d.Y);
                foreach (int n in r.Neighbours.OrderBy(n => n)) h.Add(n);
                if (r.Boss != null)
                {
                    h.Add(r.Boss.Archetype.Name);
                    foreach (var a in r.Boss.Affixes) h.Add(a.Name);
                    h.Add(r.Boss.Budget.Health).Add(r.Boss.Budget.Dps);
                }
                if (r.Mobs != null) h.Add(r.Mobs.Mob.Name).Add(r.Mobs.Count).Add(r.Mobs.TierMultiplier);
                h.Add(r.HasShrine);
                foreach (var f in r.Interior) h.Add((byte)f.Kind).Add(f.Bounds.X).Add(f.Bounds.Y).Add(f.Bounds.Width).Add(f.Bounds.Height);
                foreach (var hz in r.Hazards) h.Add(hz.Id).Add((byte)hz.Kind).Add(hz.Bounds.X).Add(hz.Bounds.Y).Add(hz.Bounds.Width).Add(hz.Bounds.Height);
                if (includeServerOnly) h.Add(r.Silver);
            }
            foreach (var c in l.Corridors) { h.Add(c.RoomA).Add(c.RoomB).Add(c.Length).Add(c.IsLoop); }
            if (l.BossExit != null) h.Add(l.BossExit.RoomId).Add(l.BossExit.Cell.X).Add(l.BossExit.Cell.Y);

            if (includeServerOnly)
            {
                foreach (var k in l.Keys) h.Add(k.KeyId).Add(k.VaultRoomId).Add(k.HolderRoomId).Add(k.DroppedByBoss);
                foreach (var r in l.Rooms) foreach (var c in r.Consumables) h.Add(c.Name);
                foreach (var it in l.Items)
                {
                    h.Add(it.Name).Add(it.ItemLevel).Add((int)it.Rarity).Add(it.Base.Name).Add(it.Condition.Current);
                    foreach (var a in it.Affixes) h.Add(a.Definition.FamilyKey).Add(a.Tier).Add(a.Value);
                }
            }
            return h.Value;
        }
    }
}
