using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core.Net
{
    public enum DungeonEventKind : byte
    {
        None = 0,
        RoamingElite,   // an elite appears in a hub carrying a guaranteed unique; kill it and the relic is on the floor for anyone
        VaultUnsealed,  // one vault's lock fails for a minute: anyone at the door may open it, no key
        CheapShrine,    // one shrine resurrects for a single soul for a minute
    }

    /// <summary>
    /// One event per round, halfway through, announced to everyone, chosen from the public seed so
    /// server and clients agree on what and where. Its job is to pull teams toward one place late
    /// in the round. The elite's relic is loot and is rolled with the server's loot secret.
    /// </summary>
    public sealed class DungeonEventPlan
    {
        public DungeonEventKind Kind { get; }
        public int RoomId { get; }
        public float AtSeconds { get; }
        /// <summary>How long a timed event stays open. The elite stays until killed or the round ends.</summary>
        public const float WindowSeconds = 60f;
        public bool Timed => Kind == DungeonEventKind.VaultUnsealed || Kind == DungeonEventKind.CheapShrine;
        public float EndsAt => Timed ? AtSeconds + WindowSeconds : float.MaxValue;

        private DungeonEventPlan(DungeonEventKind kind, int room, float at) { Kind = kind; RoomId = room; AtSeconds = at; }

        public static DungeonEventPlan None => new DungeonEventPlan(DungeonEventKind.None, -1, float.MaxValue);

        /// <summary>Deterministic from the descriptor: the kind by a fork of the seed, the room by what the layout has.</summary>
        public static DungeonEventPlan For(MatchDescriptor d, DungeonLayout layout)
        {
            if (!d.Rules.MidRoundEvent || d.Rules.IsTutorial) return None;
            var rng = new DungeonRandom(d.Seed).Fork(9);
            float at = d.Rules.RoundSeconds / 2f;
            var kinds = new List<DungeonEventKind> { DungeonEventKind.RoamingElite };
            if (layout.VaultRooms.Any()) kinds.Add(DungeonEventKind.VaultUnsealed);
            if (layout.Rooms.Any(r => r.HasShrine)) kinds.Add(DungeonEventKind.CheapShrine);
            var kind = kinds[rng.Range(0, kinds.Count)];
            int room;
            switch (kind)
            {
                case DungeonEventKind.VaultUnsealed: room = rng.Pick(layout.VaultRooms.OrderBy(r => r.Id).Select(r => r.Id).ToList()); break;
                case DungeonEventKind.CheapShrine: room = rng.Pick(layout.Rooms.Where(r => r.HasShrine).OrderBy(r => r.Id).Select(r => r.Id).ToList()); break;
                default:
                    var hubs = layout.Rooms.Where(r => r.Type == RoomType.Hub).OrderBy(r => r.Id).Select(r => r.Id).ToList();
                    if (hubs.Count == 0) hubs = layout.Rooms.Where(r => r.Type == RoomType.Standard).OrderByDescending(r => r.Danger).Take(3).Select(r => r.Id).ToList();
                    room = rng.Pick(hubs); break;
            }
            return new DungeonEventPlan(kind, room, at);
        }

        public string Announcement()
        {
            switch (Kind)
            {
                case DungeonEventKind.RoamingElite: return Strings.Get("event.elite");
                case DungeonEventKind.VaultUnsealed: return Strings.Get("event.vault");
                case DungeonEventKind.CheapShrine: return Strings.Get("event.shrine");
                default: return null;
            }
        }
    }

    /// <summary>
    /// The tutorial's opponents. Two bots on their own team, day-one classes, scripted to walk to a
    /// loot room, open its chest, fight back at half strength, never finish a downed player and
    /// never loot the player's corpse. They die so the player learns full loot, corpse souls and
    /// the shrine. Bots exist in the tutorial and nowhere else: the matchmaker never seats one.
    /// </summary>
    public static class BotRules
    {
        public const bool TutorialOnly = true;
        public const int BotIdBase = 900;
        public const int TeamSize = 2;
        public static readonly IReadOnlyList<string> Classes = new[] { "Warden", "Marksman" };
        public const float DamageMultiplier = 0.5f;
        public const bool FinishDowned = false;
        public const bool LootPlayerCorpses = false;
        /// <summary>Seconds a bot takes to walk one room.</summary>
        public const float SecondsPerRoom = 6f;
        public static bool IsBot(int playerId) => playerId >= BotIdBase && playerId < BotIdBase + 100;
    }

    /// <summary>
    /// Drives the tutorial bots through the real event pipeline: they spawn as a team, walk room by
    /// room toward the dungeon's loot room, open its chest and take a piece, then hold there and
    /// fight whoever comes. Every move is a validated MatchEvent, so the bots obey the same rules as
    /// a player and the tutorial's state is replayable like any round.
    /// </summary>
    public sealed class BotDirector
    {
        private readonly MatchState _st;
        private readonly int _team;
        private readonly int _target;
        private readonly Dictionary<int, float> _nextMove = new Dictionary<int, float>();
        private bool _spawned;

        public int Team => _team;
        public int TargetRoom => _target;
        public IReadOnlyList<int> Bots { get; }

        public BotDirector(MatchState st, int team)
        {
            _st = st; _team = team;
            var loot = st.Layout.Rooms.Where(r => r.Type == RoomType.Loot).OrderBy(r => r.Id).FirstOrDefault() ?? st.Layout.Rooms.Where(r => r.Type == RoomType.Standard && r.Loot.Count > 0).OrderBy(r => r.Id).First();
            _target = loot.Id;
            Bots = Enumerable.Range(0, BotRules.TeamSize).Select(i => BotRules.BotIdBase + i).ToList();
        }

        /// <summary>Events the bots want to raise now. The server applies them like any other; rejected ones are simply dropped.</summary>
        public IEnumerable<MatchEvent> Step(float now)
        {
            if (!_spawned)
            {
                _spawned = true;
                for (int i = 0; i < Bots.Count; i++)
                    yield return new MatchEvent(MatchEventKind.PlayerSpawned, now, Bots[i], _team, Combat.ClassArchetype.DefaultRoster.ToList().FindIndex(c => c.Name == BotRules.Classes[i % BotRules.Classes.Count]));
                yield break;
            }
            if (!_st.DoorsOpen) yield break;
            foreach (int bot in Bots)
            {
                if (_st.DeadPlayers.Contains(bot) || !_st.PlayerRoom.TryGetValue(bot, out int at)) continue;
                if (_nextMove.TryGetValue(bot, out float t) && now < t) continue;
                if (at != _target)
                {
                    int next = NextHop(at, _target);
                    if (next >= 0) { _nextMove[bot] = now + BotRules.SecondsPerRoom; yield return new MatchEvent(MatchEventKind.PlayerEnteredRoom, now, bot, next); }
                    continue;
                }
                // At the loot room: one bot opens the chest; the group rolls Need like players would; the server part of the director resolves; winners take.
                if (!_st.OpenedContainers.Contains(_target))
                {
                    if (bot != Bots[0]) continue;
                    if (!_st.Loots.ContainsKey(bot)) { yield return new MatchEvent(MatchEventKind.LootStarted, now, bot, _target, 0); _nextMove[bot] = now + Combat.FightRules.ChestOpenSeconds; }
                    else yield return new MatchEvent(MatchEventKind.ContainerOpened, now, _target, bot);
                    continue;
                }
                foreach (var kv in _st.OpenRolls.Where(kv => kv.Value.Eligible.Contains(bot) && !kv.Value.Choices.ContainsKey(bot)).OrderBy(kv => kv.Key).ToList())
                    yield return new MatchEvent(MatchEventKind.LootRolled, now, kv.Key, bot, (int)Items.LootRollChoice.Need);
                if (_st.ItemOwner.Values.Contains(bot)) continue;   // one piece each
                int item = _st.VisibleItemsIn(_target).Where(i => !_st.OpenRolls.ContainsKey(i) && (!_st.LootWinner.TryGetValue(i, out int w) || w < 0 || w == bot)).OrderBy(i => i).FirstOrDefault();
                if (item > 0) yield return new MatchEvent(MatchEventKind.ItemTaken, now, item, bot);
            }
            // The server side of the director: resolve the bots' rolls once everyone has answered.
            foreach (var kv in _st.OpenRolls.Where(kv => kv.Value.Eligible.All(Bots.Contains) && _st.RollCanResolve(kv.Key, now)).OrderBy(kv => kv.Key).ToList())
            {
                var outcome = Items.LootRollRules.Resolve(_st.RollEntries(kv.Key), new DungeonRandom(unchecked(_st.Descriptor.Seed * 31 + kv.Key)));
                yield return new MatchEvent(MatchEventKind.LootRollResolved, now, kv.Key, outcome.Winner, outcome.WinningRoll);
            }
        }

        private int NextHop(int from, int to)
        {
            var prev = new Dictionary<int, int> { [from] = -1 };
            var q = new Queue<int>(); q.Enqueue(from);
            while (q.Count > 0)
            {
                int r = q.Dequeue();
                if (r == to) break;
                foreach (int n in _st.Layout.Rooms[r].Neighbours.OrderBy(n => n)) if (!prev.ContainsKey(n)) { prev[n] = r; q.Enqueue(n); }
            }
            if (!prev.ContainsKey(to)) return -1;
            int cur = to;
            while (prev[cur] != from) cur = prev[cur];
            return cur;
        }
    }
}
