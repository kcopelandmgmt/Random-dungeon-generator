using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core.Net
{
    /// <summary>A queued party: 1 to 3 players who want to play together.</summary>
    public sealed class QueuedGroup
    {
        public int GroupId { get; }
        public IReadOnlyList<int> PlayerIds { get; }
        public float QueuedAt { get; }
        /// <summary>Which dungeon tier the group queued for. The gate (gear score in band for every member) is checked before enqueueing; groups only ever match within a tier.</summary>
        public int Tier { get; }
        /// <summary>The region the group queued in: auto-picked by latency (<see cref="Region.Pick"/>) or chosen by the player. A match never mixes regions.</summary>
        public string Region { get; }
        public QueuedGroup(int id, IReadOnlyList<int> players, float queuedAt, int tier = 1, string region = "auto")
        {
            if (players.Count < 1 || players.Count > 3) throw new ArgumentException("A group is 1 to 3 players.");
            if (players.Any(BotRules.IsBot)) throw new ArgumentException("Bots never queue. They exist in the tutorial and nowhere else.");
            DungeonTier.For(tier);
            GroupId = id; PlayerIds = players; QueuedAt = queuedAt; Tier = tier; Region = region ?? "auto";
        }
    }

    /// <summary>A match ready to start: a seed, and which groups became which teams.</summary>
    public sealed class FormedMatch
    {
        public int Seed { get; }
        public IReadOnlyList<QueuedGroup> Teams { get; }
        public int Tier { get; }
        public string Region { get; }
        public FormedMatch(int seed, IReadOnlyList<QueuedGroup> teams) { Seed = seed; Teams = teams; Tier = teams.Count > 0 ? teams[0].Tier : 1; Region = teams.Count > 0 ? teams[0].Region : "auto"; }

        /// <summary>The dungeon is sized to the teams actually present (four teams get the 4-team preset, eight the 8-team one) and stamped with the tier they queued for.</summary>
        public DungeonSettings Settings => DungeonTier.For(Tier).Apply(DungeonSettings.ForTeams(Teams.Count));
    }

    /// <summary>Region choice: the client measures latency to each region and queues in the best one unless the player overrides.</summary>
    public static class Region
    {
        public static readonly IReadOnlyList<string> Launch = new[] { "na-east", "na-west", "eu", "sa", "oce", "asia" };
        public const bool NeverMixed = true;
        /// <summary>The lowest-latency region, or the override when the player set one.</summary>
        public static string Pick(IReadOnlyDictionary<string, float> latencyMs, string playerOverride = null)
        {
            if (!string.IsNullOrEmpty(playerOverride) && Launch.Contains(playerOverride)) return playerOverride;
            if (latencyMs == null || latencyMs.Count == 0) return Launch[0];
            return latencyMs.Where(kv => Launch.Contains(kv.Key)).OrderBy(kv => kv.Value).ThenBy(kv => kv.Key).Select(kv => kv.Key).DefaultIfEmpty(Launch[0]).First();
        }
    }

    /// <summary>
    /// One queue per tier and region, no gear brackets inside it, groups of 1 to 3 stay together and each becomes a team. A
    /// match forms when enough groups are waiting, or when the oldest group has waited long enough
    /// to go with fewer teams. Seeds come from a server counter, never from a client.
    /// </summary>
    public sealed class Matchmaker
    {
        public int TeamsPerMatch { get; }
        public int MinTeamsAfterWait { get; }
        public float MaxWaitSeconds { get; }
        /// <summary>Tiers above I are thin at launch: after this long a Tier II or III match may start with <see cref="MinTeamsAfterLongWait"/> teams in a dungeon sized for them. Tier I keeps the normal floor.</summary>
        public float LongWaitSeconds { get; }
        public int MinTeamsAfterLongWait { get; }
        private readonly List<QueuedGroup> _queue = new List<QueuedGroup>();
        private int _seedCounter;

        public IReadOnlyList<QueuedGroup> Queue => _queue;

        public Matchmaker(int teamsPerMatch = 8, int minTeamsAfterWait = 4, float maxWaitSeconds = 90f, int seedStart = 1, float longWaitSeconds = 180f, int minTeamsAfterLongWait = 2)
        {
            TeamsPerMatch = teamsPerMatch; MinTeamsAfterWait = minTeamsAfterWait; MaxWaitSeconds = maxWaitSeconds; _seedCounter = seedStart;
            LongWaitSeconds = longWaitSeconds; MinTeamsAfterLongWait = minTeamsAfterLongWait;
        }

        public void Enqueue(QueuedGroup g) => _queue.Add(g);
        public bool Dequeue(int groupId) => _queue.RemoveAll(g => g.GroupId == groupId) > 0;

        /// <summary>Call every few seconds. Returns a match when one can form, oldest groups first.</summary>
        public FormedMatch TryForm(float now)
        {
            if (_queue.Count == 0) return null;
            // Tiers and regions are separate queues: a match is only ever formed from groups that queued for the same tier in the same region.
            foreach (var key in _queue.Select(g => (g.Tier, g.Region)).Distinct().OrderBy(k => k.Tier).ThenBy(k => k.Region))
            {
                var ordered = _queue.Where(g => g.Tier == key.Tier && g.Region == key.Region).OrderBy(g => g.QueuedAt).ThenBy(g => g.GroupId).ToList();
                bool waited = now - ordered[0].QueuedAt >= MaxWaitSeconds;
                bool longWaited = key.Tier > 1 && now - ordered[0].QueuedAt >= LongWaitSeconds;
                int need = ordered.Count >= TeamsPerMatch ? TeamsPerMatch : waited && ordered.Count >= MinTeamsAfterWait ? ordered.Count : longWaited && ordered.Count >= MinTeamsAfterLongWait ? ordered.Count : 0;
                if (need == 0) continue;
                var teams = ordered.Take(need).ToList();
                foreach (var t in teams) _queue.Remove(t);
                return new FormedMatch(unchecked(_seedCounter++ * 2654435761u.GetHashCode() ^ (int)now), teams);
            }
            return null;
        }
    }
}
