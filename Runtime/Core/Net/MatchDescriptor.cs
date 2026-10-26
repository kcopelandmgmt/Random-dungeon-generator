using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DungeonGen.Core.Net
{
    /// <summary>Round rules the server decides and every client must agree on.</summary>
    public sealed class MatchRules
    {
        /// <summary>Hard round limit; at the end anyone still inside dies and drops everything. Locked at 20 minutes.</summary>
        public int RoundSeconds = 20 * 60;
        /// <summary>Regular extracts open this long after the round starts. Locked at 4 minutes.</summary>
        public int ExtractsOpenAfterSeconds = 4 * 60;
        /// <summary>Whether the boss-gated exit stays open for everyone after the killing team uses it.</summary>
        public bool BossExitStaysOpen = true;
        /// <summary>Maximum players per team. Teams of 1 to 3 share one queue; a solo is exactly who a room boss is built to beat.</summary>
        public int TeamSize = 3;
        /// <summary>No spawn protection: the ring spacing is the only defence, by decision.</summary>
        public int SpawnGraceSeconds = 0;
        /// <summary>Seconds a player must stand in a gate, undamaged, to leave. From CombatRules; carried here so clients agree.</summary>
        public float ExtractChannelSeconds = Combat.CombatRules.ExtractChannelSeconds;
        /// <summary>Tutorial: one team, a long clock, extracts open at once.</summary>
        public bool IsTutorial = false;
        /// <summary>A disconnected character stands where it was, killable, for this long; reconnecting replays the log. After it, the server kills it.</summary>
        public int ReconnectWindowSeconds = 60;
        /// <summary>Nobody joins a round after the doors open. Reconnects are not joins.</summary>
        public const bool Backfill = false;

        public static MatchRules Tutorial() => new MatchRules { RoundSeconds = 45 * 60, ExtractsOpenAfterSeconds = 0, TeamSize = BotRules.TeamSize, IsTutorial = true, MidRoundEvent = false, BotTeams = 1 };

        /// <summary>Season mutators and events bend these; see <see cref="Profile.Mutator"/>.</summary>
        public const int DefaultSightRangeCells = 12;
        public int SoulMultiplier = 1;
        public bool ShrinesUsable = true;
        public int ResurrectionCost = MatchState.SoulsPerResurrection;
        public int SightRangeCells = DefaultSightRangeCells;
        public bool MidRoundEvent = true;
        public string MutatorName = "";
        /// <summary>Bot teams seated in this round. Only the tutorial has one.</summary>
        public int BotTeams = 0;

        public void Write(BinaryWriter w)
        {
            w.Write(RoundSeconds); w.Write(ExtractsOpenAfterSeconds); w.Write(BossExitStaysOpen); w.Write(TeamSize); w.Write(SpawnGraceSeconds); w.Write(ExtractChannelSeconds); w.Write(IsTutorial); w.Write(ReconnectWindowSeconds);
            w.Write(SoulMultiplier); w.Write(ShrinesUsable); w.Write(ResurrectionCost); w.Write(SightRangeCells); w.Write(MidRoundEvent); w.Write(MutatorName ?? ""); w.Write(BotTeams);
        }
        public static MatchRules Read(BinaryReader r) => new MatchRules
        {
            RoundSeconds = r.ReadInt32(), ExtractsOpenAfterSeconds = r.ReadInt32(), BossExitStaysOpen = r.ReadBoolean(), TeamSize = r.ReadInt32(), SpawnGraceSeconds = r.ReadInt32(), ExtractChannelSeconds = r.ReadSingle(), IsTutorial = r.ReadBoolean(), ReconnectWindowSeconds = r.ReadInt32(),
            SoulMultiplier = r.ReadInt32(), ShrinesUsable = r.ReadBoolean(), ResurrectionCost = r.ReadInt32(), SightRangeCells = r.ReadInt32(), MidRoundEvent = r.ReadBoolean(), MutatorName = r.ReadString(), BotTeams = r.ReadInt32(),
        };
    }

    /// <summary>
    /// Everything a client needs to build the identical dungeon and know where it starts, in one
    /// message the server sends once: seed, settings, which team gets which spawn room, rules,
    /// and the server's fingerprint of the result so the client can verify before the round starts.
    /// A few hundred bytes; the dungeon itself never crosses the wire.
    /// </summary>
    public sealed class MatchDescriptor
    {
        public const int Version = 2;

        public int Seed { get; }
        public DungeonSettings Settings { get; }
        /// <summary>Index = team number; value = the Spawn room id that team starts in. Shuffled from the seed so slot order is not predictable.</summary>
        public IReadOnlyList<int> TeamSpawnRooms { get; }
        public MatchRules Rules { get; }
        /// <summary>The PUBLIC fingerprint of the server's layout (everything but loot). Clients compare after generating without loot.</summary>
        public ulong ExpectedFingerprint { get; }
        public ulong SettingsHash => SettingsCodec.Hash(Settings);

        public MatchDescriptor(int seed, DungeonSettings settings, IReadOnlyList<int> teamSpawnRooms, MatchRules rules, ulong expectedFingerprint)
        {
            Seed = seed; Settings = settings; TeamSpawnRooms = teamSpawnRooms; Rules = rules; ExpectedFingerprint = expectedFingerprint;
        }

        /// <summary>A new player's first dungeon: generated like any other, one team, no queue.</summary>
        public static MatchDescriptor CreateTutorial(int seed, out DungeonLayout layout) => Create(seed, DungeonSettings.ForTutorial(), MatchRules.Tutorial(), out layout);

        /// <summary>A season's descriptor: the season's mutator applied to a copy of the settings and rules before generating.</summary>
        public static MatchDescriptor CreateForSeason(int seed, DungeonSettings settings, MatchRules rules, Profile.Season season, out DungeonLayout layout, int lootSecret = 0)
        {
            var m = Profile.Mutator.ForSeason(season.Number);
            if (m != null) { settings = SettingsCodec.FromBytes(SettingsCodec.ToBytes(settings)); m.Apply(rules, settings); }
            return Create(seed, settings, rules, out layout, lootSecret);
        }

        /// <summary>The round's mid-round event, deterministic from this descriptor and the layout.</summary>
        public DungeonEventPlan Event(DungeonLayout layout) => DungeonEventPlan.For(this, layout);

        /// <summary>
        /// Server side: generate, assign teams to spawns, fingerprint. The returned layout is the
        /// authoritative one (with loot). <paramref name="lootSecret"/> never leaves the server: it
        /// salts the loot roll so the public seed cannot reproduce the chests. Pass a random value
        /// in production; 0 keeps tools and tests deterministic.
        /// </summary>
        public static MatchDescriptor Create(int seed, DungeonSettings settings, MatchRules rules, out DungeonLayout layout, int lootSecret = 0)
        {
            layout = DungeonGenerator.Generate(settings, seed, lootSecret);
            var spawns = layout.SpawnRooms.OrderBy(r => r.Id).Select(r => r.Id).ToList();
            new DungeonRandom(unchecked(seed * 7919 + 17)).Shuffle(spawns);
            return new MatchDescriptor(seed, settings, spawns, rules ?? new MatchRules(), ClientView.PublicFingerprint(layout));
        }

        /// <summary>
        /// Client side: rebuild the PUBLIC dungeon (no loot) from the descriptor and verify its
        /// fingerprint. Throws if it differs: the client would be playing a different dungeon.
        /// The loot is not here and cannot be derived; it arrives in <see cref="TeamView"/>s.
        /// </summary>
        public DungeonLayout BuildAndVerify()
        {
            var publicSettings = SettingsCodec.FromBytes(SettingsCodec.ToBytes(Settings));
            publicSettings.LootEnabled = false;
            var layout = DungeonGenerator.Generate(publicSettings, Seed);
            ulong actual = ClientView.PublicFingerprint(layout);
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
            if (version != Version) throw new InvalidDataException($"MatchDescriptor version {version}, expected {Version}.");   // rules grew in v2
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

    /// <summary>
    /// What a client sends when it joins a networked round (2026-10-26 online play): who it is (account id, character name, class, account level)
    /// and the character's loadout, worn and carried items in <see cref="Profile.ProfileCodec.WriteItem"/>'s form and the consumables in its pockets.
    /// The server rebuilds a <see cref="Profile.Loadout"/> from it (<see cref="ToLoadout"/>) on a throwaway profile, checks the tier wall
    /// (<see cref="Refusal"/>) and seats it with <see cref="MatchServer.AddPlayerWithLoadout"/>; the client keeps its own loadout objects so its
    /// settlement resolves the brought ids to them in the same order. Nothing here is trusted beyond what the catalogues and the tier wall check:
    /// a modified client can claim gear it does not own until accounts live on a backend.
    /// </summary>
    public sealed class JoinRequest
    {
        public const int Version = 1;
        public string AccountId = "", CharacterName = "", ClassName = "";
        public int AccountLevel = 1;
        /// <summary>Worn items in slot order, then carried items, as the loadout lists them.</summary>
        public List<Items.Item> Worn = new List<Items.Item>(), Carried = new List<Items.Item>();
        public Dictionary<string, int> Consumables = new Dictionary<string, int>();

        public static JoinRequest From(Profile.Loadout loadout, int accountLevel)
        {
            if (loadout == null) throw new ArgumentNullException(nameof(loadout));
            return new JoinRequest
            {
                AccountId = loadout.Profile.AccountId ?? "", CharacterName = loadout.Character.Name ?? "", ClassName = loadout.Character.Class.Name, AccountLevel = accountLevel,
                Worn = loadout.Worn.ToList(), Carried = loadout.Carried.ToList(), Consumables = loadout.Consumables.ToDictionary(kv => kv.Key, kv => kv.Value),
            };
        }

        /// <summary>The loadout this request describes, on a throwaway profile that owns exactly these items and consumables. Throws on a bad request.</summary>
        public Profile.Loadout ToLoadout()
        {
            var cls = Combat.ClassArchetype.ByName(ClassName) ?? throw new InvalidOperationException($"Unknown class '{ClassName}'.");
            var p = new Profile.PlayerProfile(string.IsNullOrEmpty(AccountId) ? "guest" : AccountId);
            var ch = new Profile.Character(string.IsNullOrEmpty(CharacterName) ? "Delver" : CharacterName, cls);
            foreach (var it in Worn)
            {
                var displaced = ch.Equipment.Equip(it);   // throws on a second copy of a unique
                if (displaced != null) throw new InvalidOperationException($"Two worn items in the {it.Base.Slot} slot.");
            }
            p.Characters.Add(ch);
            foreach (var it in Carried) p.Stash.Add(it);
            foreach (var kv in Consumables)
            {
                var c = Items.Consumable.ByName(kv.Key) ?? throw new InvalidOperationException($"Unknown consumable '{kv.Key}'.");
                if (kv.Value < 0 || kv.Value > 99) throw new InvalidOperationException($"Cannot carry {kv.Value} {kv.Key}.");
                if (kv.Value > 0) p.AddConsumable(c, kv.Value);
            }
            return Profile.Loadout.For(p, ch, Carried, Consumables);
        }

        /// <summary>Why the server must not seat this request in a round of <paramref name="tier"/> (a malformed loadout or the tier wall), or null.</summary>
        public string Refusal(int tier)
        {
            try { return ToLoadout().TierRefusal(tier); }
            catch (Exception e) when (e is InvalidOperationException || e is ArgumentException) { return e.Message; }
        }

        public byte[] ToBytes()
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Version); w.Write(AccountId ?? ""); w.Write(CharacterName ?? ""); w.Write(ClassName ?? ""); w.Write(AccountLevel);
                w.Write(Worn.Count); foreach (var it in Worn) Profile.ProfileCodec.WriteItem(w, it);
                w.Write(Carried.Count); foreach (var it in Carried) Profile.ProfileCodec.WriteItem(w, it);
                w.Write(Consumables.Count); foreach (var kv in Consumables.OrderBy(kv => kv.Key, StringComparer.Ordinal)) { w.Write(kv.Key); w.Write(kv.Value); }
            }
            return ms.ToArray();
        }

        /// <summary>Reads a request; throws <see cref="InvalidDataException"/> or <see cref="Profile.ProfileFormatException"/> on a bad one.</summary>
        public static JoinRequest FromBytes(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var r = new BinaryReader(ms);
            int version = r.ReadInt32();
            if (version != Version) throw new InvalidDataException($"JoinRequest version {version}, expected {Version}.");
            var j = new JoinRequest { AccountId = r.ReadString(), CharacterName = r.ReadString(), ClassName = r.ReadString(), AccountLevel = r.ReadInt32() };
            int n = r.ReadInt32(); if (n < 0 || n > 9) throw new InvalidDataException($"{n} worn items.");
            for (int i = 0; i < n; i++) j.Worn.Add(Profile.ProfileCodec.ReadItem(r));
            n = r.ReadInt32(); if (n < 0 || n > 200) throw new InvalidDataException($"{n} carried items.");
            for (int i = 0; i < n; i++) j.Carried.Add(Profile.ProfileCodec.ReadItem(r));
            n = r.ReadInt32(); if (n < 0 || n > 32) throw new InvalidDataException($"{n} consumables.");
            for (int i = 0; i < n; i++) j.Consumables[r.ReadString()] = r.ReadInt32();
            return j;
        }
    }
}
