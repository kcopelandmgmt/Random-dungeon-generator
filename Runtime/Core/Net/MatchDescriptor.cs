using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DungeonGen.Core.Net
{
    /// <summary>Round rules the server decides and every client must agree on.</summary>
    public sealed class MatchRules
    {
        /// <summary>Hard round limit; anyone still inside is dead to the world.</summary>
        public int RoundSeconds = 20 * 60;
        /// <summary>Regular extracts open this long after the round starts (Tarkov-style: nobody leaves in the first minutes).</summary>
        public int ExtractsOpenAfterSeconds = 4 * 60;
        /// <summary>Whether the boss-gated exit stays open for everyone after the killing team uses it.</summary>
        public bool BossExitStaysOpen = true;
        /// <summary>Players per team the server will admit.</summary>
        public int TeamSize = 3;

        public void Write(BinaryWriter w) { w.Write(RoundSeconds); w.Write(ExtractsOpenAfterSeconds); w.Write(BossExitStaysOpen); w.Write(TeamSize); }
        public static MatchRules Read(BinaryReader r) => new MatchRules { RoundSeconds = r.ReadInt32(), ExtractsOpenAfterSeconds = r.ReadInt32(), BossExitStaysOpen = r.ReadBoolean(), TeamSize = r.ReadInt32() };
    }

    /// <summary>
    /// Everything a client needs to build the identical dungeon and know where it starts, in one
    /// message the server sends once: seed, settings, which team gets which spawn room, rules,
    /// and the server's fingerprint of the result so the client can verify before the round starts.
    /// A few hundred bytes; the dungeon itself never crosses the wire.
    /// </summary>
    public sealed class MatchDescriptor
    {
        public const int Version = 1;

        public int Seed { get; }
        public DungeonSettings Settings { get; }
        /// <summary>Index = team number; value = the Spawn room id that team starts in. Shuffled from the seed so slot order is not predictable.</summary>
        public IReadOnlyList<int> TeamSpawnRooms { get; }
        public MatchRules Rules { get; }
        /// <summary>LayoutFingerprint.Compute on the server's layout. Clients compare after generating.</summary>
        public ulong ExpectedFingerprint { get; }
        public ulong SettingsHash => SettingsCodec.Hash(Settings);

        public MatchDescriptor(int seed, DungeonSettings settings, IReadOnlyList<int> teamSpawnRooms, MatchRules rules, ulong expectedFingerprint)
        {
            Seed = seed; Settings = settings; TeamSpawnRooms = teamSpawnRooms; Rules = rules; ExpectedFingerprint = expectedFingerprint;
        }

        /// <summary>Server side: generate, assign teams to spawns, fingerprint. The returned layout is the authoritative one (with loot).</summary>
        public static MatchDescriptor Create(int seed, DungeonSettings settings, MatchRules rules, out DungeonLayout layout)
        {
            layout = DungeonGenerator.Generate(settings, seed);
            var spawns = layout.SpawnRooms.OrderBy(r => r.Id).Select(r => r.Id).ToList();
            new DungeonRandom(unchecked(seed * 7919 + 17)).Shuffle(spawns);
            return new MatchDescriptor(seed, settings, spawns, rules ?? new MatchRules(), LayoutFingerprint.Compute(layout));
        }

        /// <summary>Client side: rebuild from the descriptor and verify. Throws if the fingerprint differs: the client would be playing a different dungeon.</summary>
        public DungeonLayout BuildAndVerify()
        {
            var layout = DungeonGenerator.Generate(Settings, Seed);
            ulong actual = LayoutFingerprint.Compute(layout);
            if (actual != ExpectedFingerprint)
                throw new InvalidOperationException($"Layout fingerprint mismatch: server {ExpectedFingerprint:x16}, client {actual:x16}. Settings hash {SettingsHash:x16}. Client and server are not running the same generator.");
            return layout;
        }

        public byte[] ToBytes()
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Version);
                w.Write(Seed);
                SettingsCodec.Write(w, Settings);
                w.Write(TeamSpawnRooms.Count);
                foreach (int id in TeamSpawnRooms) w.Write(id);
                Rules.Write(w);
                w.Write(ExpectedFingerprint);
            }
            return ms.ToArray();
        }

        public static MatchDescriptor FromBytes(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var r = new BinaryReader(ms);
            int version = r.ReadInt32();
            if (version != Version) throw new InvalidDataException($"MatchDescriptor version {version}, expected {Version}.");
            int seed = r.ReadInt32();
            var settings = SettingsCodec.Read(r);
            int n = r.ReadInt32();
            var spawns = new List<int>(n);
            for (int i = 0; i < n; i++) spawns.Add(r.ReadInt32());
            var rules = MatchRules.Read(r);
            ulong fp = r.ReadUInt64();
            return new MatchDescriptor(seed, settings, spawns, rules, fp);
        }
    }
}
