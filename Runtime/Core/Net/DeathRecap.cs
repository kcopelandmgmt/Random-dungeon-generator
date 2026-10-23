using System;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Combat;

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
