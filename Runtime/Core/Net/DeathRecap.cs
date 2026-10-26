using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Items;

namespace DungeonGen.Core.Net
{
    /// <summary>
    /// One hit, as the server saw it. Hits are not match events (that would be thousands of log
    /// lines a round); the server keeps the last few per victim in a <see cref="HitLog"/> for the
    /// death recap and throws them away with the round.
    /// </summary>
    public readonly struct Hit
    {
        public readonly float Time; public readonly int Attacker; public readonly string AttackerSpec; public readonly DamageType Type; public readonly float Amount; public readonly string Ability;
        public Hit(float time, int attacker, string attackerSpec, DamageType type, float amount, string ability) { Time = time; Attacker = attacker; AttackerSpec = attackerSpec; Type = type; Amount = amount; Ability = ability; }
    }

    public sealed class HitLog
    {
        public const int Kept = 5;
        private readonly Dictionary<int, List<Hit>> _byVictim = new Dictionary<int, List<Hit>>();
        public void Record(int victim, Hit hit)
        {
            if (!_byVictim.TryGetValue(victim, out var list)) _byVictim[victim] = list = new List<Hit>();
            list.Add(hit);
            if (list.Count > Kept) list.RemoveAt(0);
        }
        public IReadOnlyList<Hit> LastHits(int victim) => _byVictim.TryGetValue(victim, out var l) ? l : (IReadOnlyList<Hit>)Array.Empty<Hit>();
        public void Clear(int victim) => _byVictim.Remove(victim);
    }

    /// <summary>
    /// What the death screen says. The killer by class and weapon spec, never by name (the same
    /// rule as what you see on a living enemy), the last hits, and how long you lay downed.
    /// </summary>
    public sealed class DeathRecap
    {
        public int Victim { get; private set; }
        /// <summary>-1 when a mob or the clock killed you.</summary>
        public int Killer { get; private set; } = -1;
        public string KillerClass { get; private set; }
        public string KillerSpec { get; private set; }
        public bool KilledByTeammate { get; private set; }
        public bool Finished { get; private set; }
        public float DownedSeconds { get; private set; }
        public IReadOnlyList<Hit> LastHits { get; private set; } = Array.Empty<Hit>();
        public string Headline =>
            Killer < 0 ? (Finished ? "Finished by the dungeon." : "Killed by the dungeon.") :
            $"{(Finished ? "Finished" : "Killed")} by {(KilledByTeammate ? "your own " : "a ")}{KillerClass ?? "player"}{(KillerSpec != null ? " (" + KillerSpec + ")" : "")}.";

        public static DeathRecap For(MatchState st, int victim, HitLog hits)
        {
            var r = new DeathRecap { Victim = victim };
            MatchEvent? death = null, downed = null;
            foreach (var e in st.Log)
            {
                if ((e.Kind == MatchEventKind.PlayerDied || e.Kind == MatchEventKind.PlayerFinished) && e.A == victim) death = e;
                if (e.Kind == MatchEventKind.PlayerDowned && e.A == victim && death == null) downed = e;
            }
            if (death == null) return r;
            var d = death.Value;
            r.Finished = d.Kind == MatchEventKind.PlayerFinished;
            r.Killer = d.B;
            if (downed != null && downed.Value.Time <= d.Time) r.DownedSeconds = d.Time - downed.Value.Time;
            if (d.B >= 0 && st.PlayerTeam.ContainsKey(d.B))
            {
                r.KillerClass = st.ClassOf(d.B)?.Name;
                r.KilledByTeammate = st.PlayerTeam.TryGetValue(victim, out int vt) && st.PlayerTeam[d.B] == vt;
            }
            var last = hits?.LastHits(victim) ?? Array.Empty<Hit>();
            r.LastHits = last;
            r.KillerSpec = last.LastOrDefault(h => h.Attacker == d.B).AttackerSpec;
            return r;
        }

        /// <summary>The recap on the wire (a remote client's death screen; the host builds it from its own hit log).</summary>
        public void Write(BinaryWriter w)
        {
            w.Write(Victim); w.Write(Killer); w.Write(KillerClass ?? ""); w.Write(KillerSpec ?? ""); w.Write(KilledByTeammate); w.Write(Finished); w.Write(DownedSeconds);
            w.Write(LastHits.Count);
            foreach (var h in LastHits) { w.Write(h.Time); w.Write(h.Attacker); w.Write(h.AttackerSpec ?? ""); w.Write((byte)h.Type); w.Write(h.Amount); w.Write(h.Ability ?? ""); }
        }

        public static DeathRecap Read(BinaryReader r)
        {
            string Opt(string s) => s.Length == 0 ? null : s;
            var d = new DeathRecap { Victim = r.ReadInt32(), Killer = r.ReadInt32(), KillerClass = Opt(r.ReadString()), KillerSpec = Opt(r.ReadString()), KilledByTeammate = r.ReadBoolean(), Finished = r.ReadBoolean(), DownedSeconds = r.ReadSingle() };
            int n = r.ReadInt32();
            var hits = new List<Hit>(n);
            for (int i = 0; i < n; i++) hits.Add(new Hit(r.ReadSingle(), r.ReadInt32(), Opt(r.ReadString()), (DamageType)r.ReadByte(), r.ReadSingle(), Opt(r.ReadString())));
            d.LastHits = hits;
            return d;
        }

        public byte[] ToBytes() { using var ms = new MemoryStream(); using (var w = new BinaryWriter(ms)) Write(w); return ms.ToArray(); }
        public static DeathRecap FromBytes(byte[] b) { using var ms = new MemoryStream(b); using var r = new BinaryReader(ms); return Read(r); }
    }

    /// <summary>
    /// One player's end of the round, as the server sends it to that player's client (2026-10-26 online play): everything
    /// <see cref="Profile.RoundSettlement"/> and <see cref="RoundSummary"/> read, so the client settles its own profile locally. It holds
    /// the slice of the log that concerns this player (their brought items, pickups, kills and deaths, corpses and chests they opened with
    /// those openings' history, bosses their team killed, souls they spent, the round's end), the team and class of every player that
    /// slice names, what they own at the end, their extracted silver and value, the rooms their team explored, the full items behind
    /// every id that is not one they brought (brought ids resolve to the client's own loadout objects, in registration order), damage
    /// totals for wear, consumables carried and used, salvaged materials, and the death recap with each last hit's attacker named.
    /// Nothing about another team's rooms or positions: only events this player took part in.
    /// </summary>
    public sealed class RoundResult
    {
        public const int Version = 1;
        public int PlayerId, Team, ClassIndex;
        public Profile.RoundOutcome Outcome;
        public string RoundKey;
        /// <summary>The round ended with them still inside (no death of their own in the log).</summary>
        public bool DiedToClock;
        public List<MatchEvent> Events = new List<MatchEvent>();
        public List<(int player, int team, int cls)> Players = new List<(int, int, int)>();
        public List<int> Owned = new List<int>();
        public List<int> Explored = new List<int>();
        public int ExtractedSilver; public long ExtractedValue;
        public List<(int id, byte[] item)> Items = new List<(int, byte[])>();
        public float DamageTaken, DamageDealt;
        public List<(string name, int count)> ConsumablesCarried = new List<(string, int)>();
        public List<(float time, string name)> ConsumableUses = new List<(float, string)>();
        public int SalvageScrap, SalvageShards;
        public List<(string family, int count)> SalvageEssences = new List<(string, int)>();
        /// <summary>The death screen, when they died (null when they extracted).</summary>
        public DeathRecap Recap;
        /// <summary>Who dealt each of <see cref="DeathRecap.LastHits"/>, in order: a mob's name or a player's class ("something unseen" when gone).</summary>
        public List<string> HitAttackers = new List<string>();

        /// <summary>The server's result for a player whose round is over (<see cref="Profile.RoundSettlement.OutcomeOf"/> is not null).</summary>
        public static RoundResult For(MatchServer server, int playerId, string roundKey = null)
        {
            var st = server.State;
            var outcome = Profile.RoundSettlement.OutcomeOf(st, playerId) ?? throw new InvalidOperationException($"Player {playerId} is still inside.");
            int team = st.PlayerTeam[playerId];
            var res = new RoundResult
            {
                PlayerId = playerId, Team = team, ClassIndex = st.PlayerClass.TryGetValue(playerId, out int ci) ? ci : -1, Outcome = outcome,
                RoundKey = roundKey ?? Profile.RoundSettlement.DefaultRoundKey(st, playerId),
                ExtractedSilver = st.ExtractedSilver.TryGetValue(playerId, out int sv) ? sv : 0, ExtractedValue = st.ExtractedValue.TryGetValue(playerId, out long ev) ? ev : 0,
            };
            res.DiedToClock = outcome == Profile.RoundOutcome.Died && st.Ended && !st.Log.Any(e => (e.Kind == MatchEventKind.PlayerDied || e.Kind == MatchEventKind.PlayerFinished) && e.A == playerId);
            // The log slice: what this player did and what was done to them, and the history those events are judged against.
            var corpsesOpened = new HashSet<int>(st.Log.Where(e => e.Kind == MatchEventKind.CorpseOpened && e.B == playerId).Select(e => e.A));
            var chestsOpened = new HashSet<int>(st.Log.Where(e => e.Kind == MatchEventKind.ContainerOpened && e.B == playerId).Select(e => e.A));
            foreach (var e in st.Log)
            {
                bool keep;
                switch (e.Kind)
                {
                    case MatchEventKind.ItemRegistered: case MatchEventKind.ItemTaken: keep = e.B == playerId; break;
                    case MatchEventKind.PlayerDied: case MatchEventKind.PlayerFinished: keep = e.A == playerId || e.B == playerId || corpsesOpened.Contains(e.A); break;
                    case MatchEventKind.PlayerDowned: keep = e.A == playerId; break;
                    case MatchEventKind.CorpseOpened: keep = corpsesOpened.Contains(e.A); break;
                    case MatchEventKind.ContainerOpened: keep = chestsOpened.Contains(e.A); break;
                    case MatchEventKind.BossKilled: keep = e.B == team; break;
                    case MatchEventKind.PlayerResurrected: keep = e.B == playerId; break;
                    case MatchEventKind.ShrinePurchase: case MatchEventKind.UltimateArmed: keep = e.A == playerId; break;
                    case MatchEventKind.RoundEnded: keep = true; break;
                    default: keep = false; break;
                }
                if (keep) res.Events.Add(e);
            }
            var named = new SortedSet<int> { playerId };
            foreach (var e in res.Events)
            {
                if (e.Kind == MatchEventKind.PlayerDied || e.Kind == MatchEventKind.PlayerFinished || e.Kind == MatchEventKind.PlayerDowned || e.Kind == MatchEventKind.CorpseOpened || e.Kind == MatchEventKind.PlayerResurrected)
                { named.Add(e.A); named.Add(e.B); }
                else if (e.Kind == MatchEventKind.ContainerOpened || e.Kind == MatchEventKind.ItemTaken || e.Kind == MatchEventKind.ItemRegistered) named.Add(e.B);
            }
            foreach (int p in named)
                if (st.PlayerTeam.TryGetValue(p, out int t)) res.Players.Add((p, t, st.PlayerClass.TryGetValue(p, out int c) ? c : -1));
            if (outcome == Profile.RoundOutcome.Extracted) res.Owned = st.ItemOwner.Where(kv => kv.Value == playerId).Select(kv => kv.Key).OrderBy(i => i).ToList();
            if (st.Explored.TryGetValue(team, out var ex)) res.Explored = ex.OrderBy(i => i).ToList();
            // Full items for every id settlement may resolve that the client did not bring.
            var brought = new HashSet<int>(server.BroughtItemsOf(playerId));
            var ids = new SortedSet<int>(res.Owned);
            foreach (var e in res.Events) if (e.Kind == MatchEventKind.ItemTaken) ids.Add(e.A);
            foreach (int id in ids)
            {
                if (brought.Contains(id)) continue;
                var it = server.ItemById(id);
                if (it != null) res.Items.Add((id, TeamView.WriteItem(it)));
            }
            (res.DamageTaken, res.DamageDealt) = server.DamageTotals(playerId);
            foreach (var kv in server.ConsumablesCarriedBy(playerId).OrderBy(kv => kv.Key, StringComparer.Ordinal)) res.ConsumablesCarried.Add((kv.Key, kv.Value));
            foreach (var u in server.ConsumableUsesBy(playerId)) res.ConsumableUses.Add(u);
            var bag = server.MaterialsSalvagedBy(playerId);
            res.SalvageScrap = bag.Scrap; res.SalvageShards = bag.PrimeShards;
            foreach (var kv in bag.Essences.OrderBy(kv => kv.Key, StringComparer.Ordinal)) res.SalvageEssences.Add((kv.Key, kv.Value));
            if (outcome == Profile.RoundOutcome.Died)
            {
                res.Recap = DeathRecap.For(st, playerId, server.Sim?.Hits);
                foreach (var h in res.Recap.LastHits)
                {
                    var c = server.Sim?.Get(h.Attacker);
                    res.HitAttackers.Add(c == null ? "something unseen" : c.IsMob ? c.Name : c.Class?.Name ?? "a player");
                }
            }
            return res;
        }

        /// <summary>
        /// The state settlement and the summary read, rebuilt on the client from this result: the player's team and class, every named player's,
        /// the log slice, the outcome, what they own, silver and value walked out with, and the rooms explored.
        /// </summary>
        public MatchState ToState(MatchDescriptor descriptor, DungeonLayout layout)
        {
            var st = new MatchState(descriptor, layout);
            foreach (var (p, t, c) in Players) { st.PlayerTeam[p] = t; st.PlayerClass[p] = c; }
            st.PlayerTeam[PlayerId] = Team; st.PlayerClass[PlayerId] = ClassIndex;
            st.Log.AddRange(Events);
            if (Outcome == Profile.RoundOutcome.Extracted) st.ExtractedPlayers.Add(PlayerId); else st.DeadPlayers.Add(PlayerId);
            foreach (int id in Owned) st.ItemOwner[id] = PlayerId;
            if (ExtractedSilver > 0) st.ExtractedSilver[PlayerId] = ExtractedSilver;
            if (ExtractedValue > 0) st.ExtractedValue[PlayerId] = ExtractedValue;
            st.Explored[Team] = new HashSet<int>(Explored);
            return st;
        }

        /// <summary>
        /// Settle the round into the client's own profile with <see cref="Profile.RoundSettlement"/>. <paramref name="brought"/> is the loadout the client
        /// sent (<see cref="Profile.Loadout.Brought"/>: worn, then carried), matched to the server's registrations in order; every other id resolves to the
        /// items this result carries. Idempotent per <see cref="RoundKey"/> like any settlement.
        /// </summary>
        public Profile.SettlementReport Settle(Profile.PlayerProfile profile, Profile.Character character, IReadOnlyList<Items.Item> brought, MatchDescriptor descriptor, DungeonLayout layout)
        {
            var st = ToState(descriptor, layout);
            var byId = new Dictionary<int, Items.Item>();
            int k = 0;
            foreach (var e in Events.Where(e => e.Kind == MatchEventKind.ItemRegistered && e.B == PlayerId))
            {
                if (brought == null || k >= brought.Count) throw new InvalidOperationException($"The server registered more items ({k + 1}+) than this client brought ({brought?.Count ?? 0}).");
                byId[e.A] = brought[k++];
            }
            foreach (var (id, bytes) in Items) if (!byId.ContainsKey(id)) byId[id] = TeamView.ReadItem(bytes);
            var bag = new MaterialBag { Scrap = SalvageScrap, PrimeShards = SalvageShards };
            foreach (var (f, n) in SalvageEssences) bag.Essences[f] = n;
            return Profile.RoundSettlement.Settle(profile, character, st, PlayerId, id => byId.TryGetValue(id, out var it) ? it : null, new Profile.SettlementOptions
            {
                RoundKey = RoundKey, DamageTaken = DamageTaken, DamageDealt = DamageDealt,
                ConsumablesCarried = ConsumablesCarried.ToDictionary(c => c.name, c => c.count),
                ConsumableUses = ConsumableUses, SalvagedMaterials = bag,
            });
        }

        public byte[] ToBytes()
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Version); w.Write(PlayerId); w.Write(Team); w.Write(ClassIndex); w.Write((byte)Outcome); w.Write(RoundKey ?? ""); w.Write(DiedToClock);
                w.Write(Events.Count); foreach (var e in Events) e.Write(w);
                w.Write(Players.Count); foreach (var p in Players) { w.Write(p.player); w.Write(p.team); w.Write(p.cls); }
                w.Write(Owned.Count); foreach (int i in Owned) w.Write(i);
                w.Write(Explored.Count); foreach (int i in Explored) w.Write(i);
                w.Write(ExtractedSilver); w.Write(ExtractedValue);
                w.Write(Items.Count); foreach (var (id, item) in Items) { w.Write(id); w.Write(item.Length); w.Write(item); }
                w.Write(DamageTaken); w.Write(DamageDealt);
                w.Write(ConsumablesCarried.Count); foreach (var c in ConsumablesCarried) { w.Write(c.name); w.Write(c.count); }
                w.Write(ConsumableUses.Count); foreach (var u in ConsumableUses) { w.Write(u.time); w.Write(u.name); }
                w.Write(SalvageScrap); w.Write(SalvageShards);
                w.Write(SalvageEssences.Count); foreach (var e in SalvageEssences) { w.Write(e.family); w.Write(e.count); }
                w.Write(Recap != null); Recap?.Write(w);
                w.Write(HitAttackers.Count); foreach (var a in HitAttackers) w.Write(a ?? "");
            }
            return ms.ToArray();
        }

        public static RoundResult FromBytes(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var r = new BinaryReader(ms);
            int version = r.ReadInt32();
            if (version != Version) throw new InvalidDataException($"RoundResult version {version}, expected {Version}.");
            var res = new RoundResult { PlayerId = r.ReadInt32(), Team = r.ReadInt32(), ClassIndex = r.ReadInt32(), Outcome = (Profile.RoundOutcome)r.ReadByte(), RoundKey = r.ReadString(), DiedToClock = r.ReadBoolean() };
            if (res.RoundKey.Length == 0) res.RoundKey = null;
            int n = r.ReadInt32(); for (int i = 0; i < n; i++) res.Events.Add(MatchEvent.Read(r));
            n = r.ReadInt32(); for (int i = 0; i < n; i++) res.Players.Add((r.ReadInt32(), r.ReadInt32(), r.ReadInt32()));
            n = r.ReadInt32(); for (int i = 0; i < n; i++) res.Owned.Add(r.ReadInt32());
            n = r.ReadInt32(); for (int i = 0; i < n; i++) res.Explored.Add(r.ReadInt32());
            res.ExtractedSilver = r.ReadInt32(); res.ExtractedValue = r.ReadInt64();
            n = r.ReadInt32(); for (int i = 0; i < n; i++) { int id = r.ReadInt32(); int len = r.ReadInt32(); res.Items.Add((id, r.ReadBytes(len))); }
            res.DamageTaken = r.ReadSingle(); res.DamageDealt = r.ReadSingle();
            n = r.ReadInt32(); for (int i = 0; i < n; i++) res.ConsumablesCarried.Add((r.ReadString(), r.ReadInt32()));
            n = r.ReadInt32(); for (int i = 0; i < n; i++) res.ConsumableUses.Add((r.ReadSingle(), r.ReadString()));
            res.SalvageScrap = r.ReadInt32(); res.SalvageShards = r.ReadInt32();
            n = r.ReadInt32(); for (int i = 0; i < n; i++) res.SalvageEssences.Add((r.ReadString(), r.ReadInt32()));
            if (r.ReadBoolean()) res.Recap = DeathRecap.Read(r);
            n = r.ReadInt32(); for (int i = 0; i < n; i++) res.HitAttackers.Add(r.ReadString());
            return res;
        }
    }

    /// <summary>
    /// The round-end screen for one player, built from the match log so it is exact: what they
    /// walked out with, what they killed, what their souls did, and the account XP it was worth.
    /// </summary>
    public sealed class RoundSummary
    {
        public int Player { get; private set; }
        public bool Extracted { get; private set; }
        public bool Died { get; private set; }
        public long ExtractedValue { get; private set; }
        public int ExtractedSilver { get; private set; }
        public int ItemsExtracted { get; private set; }
        public int PlayerKills { get; private set; }
        public int BossKills { get; private set; }
        public bool FinalBossKill { get; private set; }
        public int SoulsEarned { get; private set; }
        public int SoulsSpent { get; private set; }
        public int RoomsExplored { get; private set; }
        public int Tier { get; private set; }
        public long AccountXp { get; private set; }

        /// <summary>Was this the first opening of a corpse that carried a soul? The death that made it must have been an enemy player's kill, and no enemy opened it before.</summary>
        private static bool CorpseHadSoul(MatchState st, MatchEvent open)
        {
            bool armed = false;
            foreach (var e in st.Log)
            {
                if (e.Equals(open)) return armed;
                if ((e.Kind == MatchEventKind.PlayerDied || e.Kind == MatchEventKind.PlayerFinished) && e.A == open.A)
                    armed = e.B >= 0 && st.PlayerTeam.TryGetValue(e.B, out int kt) && st.PlayerTeam.TryGetValue(e.A, out int vt2) && kt != vt2;
                else if (e.Kind == MatchEventKind.CorpseOpened && e.A == open.A && armed && st.PlayerTeam.TryGetValue(e.B, out int ot) && st.PlayerTeam.TryGetValue(e.A, out int vt3) && ot != vt3)
                    armed = false;   // an enemy already took it
            }
            return false;
        }

        public static RoundSummary For(MatchState st, int player)
        {
            var s = new RoundSummary { Player = player, Tier = st.Descriptor.Settings.Tier };
            if (!st.PlayerTeam.TryGetValue(player, out int team)) return s;
            s.Extracted = st.ExtractedPlayers.Contains(player);
            s.Died = st.DeadPlayers.Contains(player);
            s.ExtractedValue = st.ExtractedValue.TryGetValue(player, out long v) ? v : 0;
            s.ExtractedSilver = st.ExtractedSilver.TryGetValue(player, out int sv) ? sv : 0;
            s.ItemsExtracted = s.Extracted ? st.ItemOwner.Count(kv => kv.Value == player) : 0;
            s.RoomsExplored = st.Explored.TryGetValue(team, out var ex) ? ex.Count : 0;
            foreach (var e in st.Log)
            {
                switch (e.Kind)
                {
                    case MatchEventKind.PlayerDied:
                    case MatchEventKind.PlayerFinished:
                        if (e.B == player && e.A != player && st.PlayerTeam.TryGetValue(e.A, out int vt) && vt != team) s.PlayerKills++;   // the soul is on the corpse, not in the blow
                        break;
                    case MatchEventKind.CorpseOpened:
                        if (e.B == player && st.PlayerTeam.TryGetValue(e.A, out int ct) && ct != team && CorpseHadSoul(st, e)) s.SoulsEarned++;
                        break;
                    case MatchEventKind.BossKilled:
                        if (e.B == team) { if (st.Layout.Rooms[e.A].Type == RoomType.FinalBoss) s.FinalBossKill = true; else s.BossKills++; }
                        break;
                    case MatchEventKind.ContainerOpened:
                        if (e.B == player && st.Layout.Rooms[e.A].IsBossRoom && st.Log.TakeWhile(x => !x.Equals(e)).All(x => !(x.Kind == MatchEventKind.ContainerOpened && x.A == e.A))) s.SoulsEarned += MatchState.SoulsPerBossChest;
                        break;
                    case MatchEventKind.PlayerResurrected: if (e.B == player) s.SoulsSpent += MatchState.SoulsPerResurrection; break;
                    case MatchEventKind.ShrinePurchase: if (e.A == player) s.SoulsSpent += MatchState.SoulsPerShrinePurchase; break;
                    case MatchEventKind.UltimateArmed: if (e.A == player) s.SoulsSpent += FightRules.SoulsToArmUltimate; break;
                }
            }
            s.AccountXp = (s.Extracted ? Profile.AccountRules.XpPerExtraction : 0) + s.PlayerKills * Profile.AccountRules.XpPerPlayerKill + s.BossKills * Profile.AccountRules.XpPerBossKill + (s.FinalBossKill ? Profile.AccountRules.XpPerFinalBossKill : 0);
            return s;
        }
    }
}
