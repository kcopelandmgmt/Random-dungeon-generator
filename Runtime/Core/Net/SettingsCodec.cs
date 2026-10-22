using System;
using System.IO;
using DungeonGen.Core.Combat;

namespace DungeonGen.Core.Net
{
    /// <summary>
    /// Binary (de)serialization of <see cref="DungeonSettings"/> in a fixed field order, plus a
    /// stable hash of the same bytes. Clients generate from the server's settings, never their
    /// own asset, and the hash catches a stale build before it desyncs.
    /// </summary>
    public static class SettingsCodec
    {
        public const int Version = 1;

        public static void Write(BinaryWriter w, DungeonSettings s)
        {
            w.Write(Version);
            w.Write(s.Width); w.Write(s.Height); w.Write(s.BorderPadding);
            w.Write(s.TargetRoomCount); w.Write(s.MinRoomSize); w.Write(s.MaxRoomSize); w.Write(s.RoomSpacing); w.Write(s.MaxPlacementAttempts);
            w.Write(s.NearestNeighbours); w.Write(s.LoopChance); w.Write(s.MaxLoopDistance);
            w.Write(s.NewCorridorCost); w.Write(s.CorridorWidth);
            w.Write(s.SpawnCount); w.Write((int)s.SpawnPlacement); w.Write(s.RingJitter); w.Write(s.ExtractRingRadius); w.Write(s.BossRingRadius);
            w.Write(s.BossCount); w.Write(s.VaultCount); w.Write(s.KeyFromBossChance); w.Write(s.KeyMinWalk);
            w.Write(s.MobsEnabled); w.Write(s.MobDensity); w.Write(s.MobBudgetAtEdge); w.Write(s.MobBudgetAtCenter); w.Write(s.MobBudgetLootBonus);
            w.Write(s.LootEnabled); w.Write(s.LootDensity);
            w.Write(s.BossAffixesEnabled); w.Write(s.MaxAffixesPerBoss);
            w.Write(s.ExtractCount); w.Write(s.MinSpawnSeparation); w.Write(s.MinExtractDepth); w.Write(s.LootTiers); w.Write(s.LootRoomFraction);
            w.Write(s.CandidateCount);
            var c = s.Combat;
            w.Write(c.PlayerDps); w.Write(c.PlayerHealth); w.Write(c.PartySustain); w.Write(c.GoodGearMultiplier); w.Write(c.PartySize); w.Write(c.TuningMargin);
            w.Write(c.RoomBossPlayerEquivalents); w.Write(c.RoomBossFightSeconds); w.Write(c.FinalBossFightSeconds); w.Write(c.EnrageAfterMultiple);
        }

        public static DungeonSettings Read(BinaryReader r)
        {
            int version = r.ReadInt32();
            if (version != Version) throw new InvalidDataException($"Settings version {version}, expected {Version}. Client and server builds differ.");
            var s = new DungeonSettings
            {
                Width = r.ReadInt32(), Height = r.ReadInt32(), BorderPadding = r.ReadInt32(),
                TargetRoomCount = r.ReadInt32(), MinRoomSize = r.ReadInt32(), MaxRoomSize = r.ReadInt32(), RoomSpacing = r.ReadInt32(), MaxPlacementAttempts = r.ReadInt32(),
                NearestNeighbours = r.ReadInt32(), LoopChance = r.ReadSingle(), MaxLoopDistance = r.ReadInt32(),
                NewCorridorCost = r.ReadSingle(), CorridorWidth = r.ReadInt32(),
                SpawnCount = r.ReadInt32(), SpawnPlacement = (SpawnPlacement)r.ReadInt32(), RingJitter = r.ReadSingle(), ExtractRingRadius = r.ReadSingle(), BossRingRadius = r.ReadSingle(),
                BossCount = r.ReadInt32(), VaultCount = r.ReadInt32(), KeyFromBossChance = r.ReadSingle(), KeyMinWalk = r.ReadInt32(),
                MobsEnabled = r.ReadBoolean(), MobDensity = r.ReadSingle(), MobBudgetAtEdge = r.ReadSingle(), MobBudgetAtCenter = r.ReadSingle(), MobBudgetLootBonus = r.ReadSingle(),
                LootEnabled = r.ReadBoolean(), LootDensity = r.ReadSingle(),
                BossAffixesEnabled = r.ReadBoolean(), MaxAffixesPerBoss = r.ReadInt32(),
                ExtractCount = r.ReadInt32(), MinSpawnSeparation = r.ReadInt32(), MinExtractDepth = r.ReadInt32(), LootTiers = r.ReadInt32(), LootRoomFraction = r.ReadSingle(),
                CandidateCount = r.ReadInt32(),
            };
            var c = s.Combat;
            c.PlayerDps = r.ReadSingle(); c.PlayerHealth = r.ReadSingle(); c.PartySustain = r.ReadSingle(); c.GoodGearMultiplier = r.ReadSingle(); c.PartySize = r.ReadInt32(); c.TuningMargin = r.ReadSingle();
            c.RoomBossPlayerEquivalents = r.ReadSingle(); c.RoomBossFightSeconds = r.ReadSingle(); c.FinalBossFightSeconds = r.ReadSingle(); c.EnrageAfterMultiple = r.ReadSingle();
            return s;
        }

        public static byte[] ToBytes(DungeonSettings s)
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms)) Write(w, s);
            return ms.ToArray();
        }

        public static DungeonSettings FromBytes(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var r = new BinaryReader(ms);
            return Read(r);
        }

        /// <summary>Stable across machines. Two clients with the same hash will generate the same dungeon from the same seed.</summary>
        public static ulong Hash(DungeonSettings s)
        {
            var h = StableHash.Start();
            foreach (var b in ToBytes(s)) h.Add(b);
            return h.Value;
        }
    }
}
