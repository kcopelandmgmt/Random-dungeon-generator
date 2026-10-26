using System;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Items;
using DungeonGen.Core.Net;

namespace DungeonGen.Core.Profile
{
    public enum RoundOutcome { Extracted, Died }

    /// <summary>What settling one round did to one profile. Every list holds the actual item objects.</summary>
    public sealed class SettlementReport
    {
        public string RoundKey;
        public int PlayerId;
        /// <summary>True when this round was already settled into the profile: nothing changed this time.</summary>
        public bool AlreadySettled;
        public RoundOutcome Outcome;
        /// <summary>Found, looted or handed over in the round and walked out with: now in the stash.</summary>
        public List<Item> ItemsGained = new List<Item>();
        /// <summary>Brought in and brought back: worn gear stays worn (with the trip's wear), carried gear stays in the stash.</summary>
        public List<Item> ItemsKept = new List<Item>();
        /// <summary>Brought in and not brought back: taken off the character or out of the stash.</summary>
        public List<Item> ItemsLost = new List<Item>();
        /// <summary>Picked up in the round but not walked out with (died, or given / dropped). Never were the profile's; listed for the screen.</summary>
        public List<Item> PickupsLost = new List<Item>();
        public long SilverDelta;
        public long XpDelta;
        /// <summary>Consumable name → change to the bank (negative: used or lost; positive: bought at a shrine and walked out).</summary>
        public Dictionary<string, int> ConsumableDelta = new Dictionary<string, int>();
        /// <summary>Materials broken down from carried items with a Salvage Kit in the round and walked out with: added to the profile's materials. Empty on a death.</summary>
        public MaterialBag MaterialsGained = new MaterialBag();
        public int LevelBefore, LevelAfter;
        public RoundSummary Summary;
        /// <summary>True when the character has nothing on: the screen should offer the free starter kit (<see cref="Vendor.ClaimStarterKit"/>).</summary>
        public bool CharacterNaked;
    }

    /// <summary>What settlement needs that the match state does not hold. All optional.</summary>
    public sealed class SettlementOptions
    {
        /// <summary>The backend's match id. Defaults to <see cref="RoundSettlement.DefaultRoundKey"/>.</summary>
        public string RoundKey;
        /// <summary>Raw damage the player took and dealt: what durability wear reads.</summary>
        public float DamageTaken, DamageDealt;
        /// <summary>Consumables carried in (name → count), as the loadout had them.</summary>
        public IReadOnlyDictionary<string, int> ConsumablesCarried;
        /// <summary>Every consumable used, with the round time it was used at.</summary>
        public IReadOnlyList<(float time, string name)> ConsumableUses;
        /// <summary>Materials the player salvaged in the round and still held (<see cref="MatchServer.MaterialsSalvagedBy"/>). Credited on extraction only.</summary>
        public MaterialBag SalvagedMaterials;
        /// <summary>Kill-XP memory to apply and update. Defaults to the profile's own (<see cref="PlayerProfile.KillXp"/>, saved with it).</summary>
        public KillXpTracker KillXp;
        /// <summary>Which account stood behind a player id, so a victim can be paid for once a day (2026-10-26 audit #8). Null or a null answer: that kill is paid in full and not remembered.</summary>
        public Func<int, string> AccountOf;
        /// <summary>The settlement time in UTC; defaults to now. The cap is per day, so this decides whether a kill is the same day's.</summary>
        public DateTime? WhenUtc;
    }

    /// <summary>
    /// Settles a finished round into the profile, by the locked rules:
    /// <list type="bullet">
    /// <item>Extracted (2026-10-22 "Moment-to-moment…" #4; 2026-10-22 "Round, progression…" #2): every item the
    /// player owns at extraction comes home. Brought worn gear stays worn, brought stash gear stays in the stash,
    /// everything else goes to the stash. Extracted silver is added (2026-10-23 "Economy-in-the-dungeon" #1).</item>
    /// <item>Died, finished, bled out, or the clock ran out (2026-10-22 "Round, progression…" #2): full loot. Everything
    /// brought and picked up is lost; nothing survives dying, no secure pouch (2026-10-23 "Tiers, hand-offs…" #13,
    /// <see cref="FightRules.SecurePouchSlots"/> = 0); the starter kit is not kept but is free again any time
    /// (2026-10-22 "Moment-to-moment…" #1). Silver carried is lost: it dropped on the corpse (2026-10-23 "Economy-in-the-dungeon" #1).</item>
    /// <item>A disconnect whose window passed is a death (2026-10-22 "Technical and production" #10).</item>
    /// <item>Account XP from the round summary either way: extraction, enemy player kills, boss kills, the final boss;
    /// never time (2026-10-23 "Tiers, hand-offs…" #6; <see cref="RoundSummary.AccountXp"/>).</item>
    /// <item>Wear: kept worn gear takes the trip's wear, no repairs (2026-10-22 "No repairs"; <see cref="DurabilityRules.WearForRound"/>).</item>
    /// <item>Souls and keys never reach the profile (2026-10-23 "Souls are personal"; 2026-10-22 "Round, progression…" #4).</item>
    /// <item>Consumables used are removed; a death loses what was in the pockets. (Shrines sold consumables until 2026-10-26 owner #3; an old log's
    /// <see cref="MatchEventKind.ShrinePurchase"/> still counts, walked out with, so old rounds settle as they did.)</item>
    /// <item>A resurrected player (2026-10-26 owner #1) settles on how they finally left: they came back naked and their first corpse kept everything, so
    /// what counts is what they own at extraction (new pickups, and anything taken back off that corpse, brought gear included, comes home; what
    /// was left on it is lost), or nothing at a second death. Their pockets were emptied by the first death.</item>
    /// </list>
    /// The profile is untouched between the loadout and this call. Settling the same round twice changes nothing
    /// the second time (<see cref="PlayerProfile.SettledRounds"/>, saved with the profile).
    /// </summary>
    public static class RoundSettlement
    {
        /// <summary>The round's identity when the backend gives none: seed, public fingerprint, player.</summary>
        public static string DefaultRoundKey(MatchState state, int playerId) =>
            $"{state.Descriptor.Seed}/{state.Descriptor.ExpectedFingerprint:x16}/p{playerId}";

        /// <summary>
        /// Has this player's round finished, and how: extracted, dead, or disconnected past the window.
        /// Null while they are still inside (or never were in it).
        /// </summary>
        public static RoundOutcome? OutcomeOf(MatchState state, int playerId)
        {
            if (state.ExtractedPlayers.Contains(playerId)) return RoundOutcome.Extracted;
            if (state.DeadPlayers.Contains(playerId)) return RoundOutcome.Died;
            if (state.Disconnected.TryGetValue(playerId, out float dropped) && (state.Ended || state.Time - dropped > state.Descriptor.Rules.ReconnectWindowSeconds)) return RoundOutcome.Died;
            return null;
        }

        /// <summary>Settle on the host, reading brought items, damage and consumables from the server.</summary>
        public static SettlementReport Settle(PlayerProfile profile, Character character, MatchServer server, int playerId, string roundKey = null, Func<int, string> accountOf = null, DateTime? whenUtc = null, KillXpTracker killXp = null)
        {
            var (taken, dealt) = server.DamageTotals(playerId);
            return Settle(profile, character, server.State, playerId, server.ItemById, new SettlementOptions
            {
                RoundKey = roundKey, DamageTaken = taken, DamageDealt = dealt,
                ConsumablesCarried = server.ConsumablesCarriedBy(playerId), ConsumableUses = server.ConsumableUsesBy(playerId), SalvagedMaterials = server.MaterialsSalvagedBy(playerId),
                AccountOf = accountOf, WhenUtc = whenUtc, KillXp = killXp,
            });
        }

        public static SettlementReport Settle(PlayerProfile profile, Character character, MatchState state, int playerId, Func<int, Item> itemById, SettlementOptions options = null)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (character == null) throw new ArgumentNullException(nameof(character));
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (itemById == null) throw new ArgumentNullException(nameof(itemById));
            options = options ?? new SettlementOptions();
            if (!profile.Characters.Contains(character)) throw new InvalidOperationException($"'{character.Name}' is not a character on account {profile.AccountId}.");
            if (!state.PlayerTeam.ContainsKey(playerId)) throw new InvalidOperationException($"Player {playerId} was never in this round.");
            if (state.PlayerClass.TryGetValue(playerId, out int ci) && ci >= 0 && ci < ClassArchetype.DefaultRoster.Count && ClassArchetype.DefaultRoster[ci] != character.Class)
                throw new InvalidOperationException($"Player {playerId} played a {ClassArchetype.DefaultRoster[ci].Name}; '{character.Name}' is a {character.Class.Name}.");

            string key = options.RoundKey ?? DefaultRoundKey(state, playerId);
            var report = new SettlementReport { RoundKey = key, PlayerId = playerId, LevelBefore = profile.AccountLevel, LevelAfter = profile.AccountLevel };
            if (profile.IsSettled(key)) { report.AlreadySettled = true; report.Outcome = OutcomeOf(state, playerId) ?? RoundOutcome.Died; report.CharacterNaked = !character.Equipment.Items.Any(); return report; }
            var outcome = OutcomeOf(state, playerId) ?? throw new InvalidOperationException($"Player {playerId} is still inside: settle after they extract or die.");
            report.Outcome = outcome;
            report.Summary = RoundSummary.For(state, playerId);

            // What they brought, from the state's own record, resolved to the profile's objects.
            var brought = new List<(int id, Item item, bool worn)>();
            foreach (var e in state.Log.Where(e => e.Kind == MatchEventKind.ItemRegistered && e.B == playerId))
            {
                var it = itemById(e.A) ?? throw new InvalidOperationException($"Brought item {e.A} of player {playerId} cannot be resolved.");
                bool worn = character.Equipment.IsWearing(it);
                if (!worn && !profile.Stash.Any(s => ReferenceEquals(s, it))) throw new InvalidOperationException($"Brought item {e.A} ('{it.Name}') is neither worn by '{character.Name}' nor in the stash: wrong profile or already settled elsewhere.");
                brought.Add((e.A, it, worn));
            }
            var broughtIds = new HashSet<int>(brought.Select(b => b.id));
            var owned = outcome == RoundOutcome.Extracted ? new HashSet<int>(state.ItemOwner.Where(kv => kv.Value == playerId).Select(kv => kv.Key)) : new HashSet<int>();

            // Brought gear: home if still theirs at extraction, otherwise lost (full loot).
            foreach (var (id, item, worn) in brought)
            {
                if (owned.Contains(id)) { report.ItemsKept.Add(item); continue; }
                if (worn) character.Equipment.Remove(item); else profile.Stash.Remove(item);
                report.ItemsLost.Add(item);
            }
            // Everything else they own walked out with them: into the stash.
            foreach (int id in owned.Where(id => !broughtIds.Contains(id)).OrderBy(id => id))
            {
                var it = itemById(id) ?? throw new InvalidOperationException($"Item {id} owned by player {playerId} cannot be resolved.");
                if (character.Equipment.IsWearing(it) || profile.Stash.Any(s => ReferenceEquals(s, it))) continue;
                profile.Stash.Add(it);
                report.ItemsGained.Add(it);
            }
            // Pickups that did not come home, for the screen.
            foreach (int id in state.Log.Where(e => e.Kind == MatchEventKind.ItemTaken && e.B == playerId).Select(e => e.A).Distinct())
            {
                if (broughtIds.Contains(id) || owned.Contains(id)) continue;
                var it = itemById(id);
                if (it != null && !report.PickupsLost.Contains(it)) report.PickupsLost.Add(it);
            }
            // The trip's wear on what is still worn. No repairs.
            if (outcome == RoundOutcome.Extracted) DurabilityRules.WearForRound(character.Equipment, options.DamageTaken, options.DamageDealt);

            // Silver: only what was walked out with. What was carried at death lies on the corpse.
            if (outcome == RoundOutcome.Extracted && state.ExtractedSilver.TryGetValue(playerId, out int silver) && silver > 0)
            {
                profile.Silver += silver; report.SilverDelta = silver;
            }

            // Materials from Salvage Kits: only what was walked out with (a death loses them like the pockets).
            if (outcome == RoundOutcome.Extracted && options.SalvagedMaterials != null) { report.MaterialsGained.Add(options.SalvagedMaterials); profile.Materials.Add(options.SalvagedMaterials); }

            // Consumables.
            var carried = options.ConsumablesCarried ?? new Dictionary<string, int>();
            var pockets = PocketsAt(state, playerId, carried, options.ConsumableUses, float.MaxValue);
            foreach (var name in carried.Keys.Concat(pockets.Keys).Distinct().OrderBy(n => n, StringComparer.Ordinal))
            {
                var c = Consumable.ByName(name) ?? throw new InvalidOperationException($"No consumable '{name}'.");
                int had = carried.TryGetValue(name, out int h) ? h : 0;
                int home = outcome == RoundOutcome.Extracted && pockets.TryGetValue(name, out int p) ? p : 0;
                int delta = home - had;
                if (delta > 0) profile.AddConsumable(c, delta);
                else if (delta < 0) delta = -profile.RemoveConsumable(c, -delta);
                if (delta != 0) report.ConsumableDelta[name] = delta;
            }

            // Account XP: earned either way.
            long xp = report.Summary.AccountXp - report.Summary.PlayerKills * AccountRules.XpPerPlayerKill + KillXpFor(profile, state, playerId, options);
            profile.AddAccountXp(xp);
            report.XpDelta = Math.Max(0, xp);
            report.LevelAfter = profile.AccountLevel;

            report.CharacterNaked = !character.Equipment.Items.Any();
            profile.MarkSettled(key);
            return report;
        }

        /// <summary>
        /// Kill XP for this round, with the once-per-victim-per-day cap applied (<see cref="KillXpTracker"/>, 2026-10-26 audit #8):
        /// each enemy player this one finished is paid <see cref="AccountRules.XpPerPlayerKill"/> unless that victim's account was already paid
        /// to this account within 24 hours. A victim with no known account is paid in full and not remembered.
        /// </summary>
        private static long KillXpFor(PlayerProfile profile, MatchState state, int playerId, SettlementOptions options)
        {
            var tracker = options.KillXp ?? profile.KillXp;
            var when = options.WhenUtc ?? DateTime.UtcNow;
            tracker.Prune(when);
            int team = state.PlayerTeam[playerId];
            long xp = 0;
            foreach (var e in state.Log)
            {
                if ((e.Kind != MatchEventKind.PlayerDied && e.Kind != MatchEventKind.PlayerFinished) || e.B != playerId || e.A == playerId) continue;
                if (!state.PlayerTeam.TryGetValue(e.A, out int vt) || vt == team) continue;   // the same rule as RoundSummary.PlayerKills
                string victim = options.AccountOf?.Invoke(e.A);
                xp += victim == null ? AccountRules.XpPerPlayerKill : tracker.Award(profile.AccountId, victim, when);
            }
            return xp;
        }

        /// <summary>
        /// What is in a player's pockets at a moment: carried in, plus shrine purchases in an old log (<see cref="MatchEventKind.ShrinePurchase"/>, retired; new rounds have none),
        /// minus uses, emptied by every death (a resurrected player returns naked, 2026-10-22 "Round, progression…" #3).
        /// </summary>
        public static Dictionary<string, int> PocketsAt(MatchState state, int playerId, IReadOnlyDictionary<string, int> carried, IReadOnlyList<(float time, string name)> uses, float until)
        {
            var pockets = new Dictionary<string, int>();
            foreach (var kv in carried ?? new Dictionary<string, int>()) if (kv.Value > 0) pockets[kv.Key] = kv.Value;
            // Merge the log's purchases and deaths with the uses, in time order; at equal times the log comes first.
            var timeline = new List<(float time, int order, int kind, string name)>();
            int n = 0;
            foreach (var e in state.Log)
            {
                if (e.Time > until) break;
                if (e.Kind == MatchEventKind.ShrinePurchase && e.A == playerId && e.B >= 0 && e.B < Consumable.All.Count) timeline.Add((e.Time, n++, 0, Consumable.All[e.B].Name));
                else if ((e.Kind == MatchEventKind.PlayerDied || e.Kind == MatchEventKind.PlayerFinished) && e.A == playerId) timeline.Add((e.Time, n++, 1, null));
                else if (e.Kind == MatchEventKind.RoundEnded && !state.ExtractedPlayers.Contains(playerId)) timeline.Add((e.Time, n++, 1, null));
            }
            foreach (var u in uses ?? Array.Empty<(float, string)>()) if (u.time <= until) timeline.Add((u.time, n++, 2, u.name));
            foreach (var t in timeline.OrderBy(t => t.time).ThenBy(t => t.order))
            {
                int have = t.name != null && pockets.TryGetValue(t.name, out int k) ? k : 0;
                switch (t.kind)
                {
                    case 0: pockets[t.name] = have + 1; break;
                    case 1: pockets.Clear(); break;
                    case 2: if (have > 0) pockets[t.name] = have - 1; break;
                }
            }
            foreach (var k in pockets.Where(kv => kv.Value <= 0).Select(kv => kv.Key).ToList()) pockets.Remove(k);
            return pockets;
        }
    }
}
