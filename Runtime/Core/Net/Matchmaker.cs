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
        public QueuedGroup(int id, IReadOnlyList<int> players, float queuedAt, int tier = 1)
        {
            if (players.Count < 1 || players.Count > 3) throw new ArgumentException("A group is 1 to 3 players.");
            DungeonTier.For(tier);
            GroupId = id; PlayerIds = players; QueuedAt = queuedAt; Tier = tier;
        }
    }

    /// <summary>A match ready to start: a seed, and which groups became which teams.</summary>
    public sealed class FormedMatch
    {
        public int Seed { get; }
        public IReadOnlyList<QueuedGroup> Teams { get; }
        public int Tier { get; }
        public FormedMatch(int seed, IReadOnlyList<QueuedGroup> teams) { Seed = seed; Teams = teams; Tier = teams.Count > 0 ? teams[0].Tier : 1; }

        /// <summary>The dungeon is sized to the teams actually present (four teams get the 4-team preset, eight the 8-team one) and stamped with the tier they queued for.</summary>
        public DungeonSettings Settings => DungeonTier.For(Tier).Apply(DungeonSettings.ForTeams(Teams.Count));
    }

    /// <summary>
    /// One queue, no gear brackets, groups of 1 to 3 stay together and each becomes a team. A
    /// match forms when enough groups are waiting, or when the oldest group has waited long enough
    /// to go with fewer teams. Seeds come from a server counter, never from a client.
    /// </summary>
    public sealed class Matchmaker
    {
        public int TeamsPerMatch { get; }
        public int MinTeamsAfterWait { get; }
        public float MaxWaitSeconds { get; }
        private readonly List<QueuedGroup> _queue = new List<QueuedGroup>();
        private int _seedCounter;

        public IReadOnlyList<QueuedGroup> Queue => _queue;

        public Matchmaker(int teamsPerMatch = 8, int minTeamsAfterWait = 4, float maxWaitSeconds = 90f, int seedStart = 1)
        {
            TeamsPerMatch = teamsPerMatch; MinTeamsAfterWait = minTeamsAfterWait; MaxWaitSeconds = maxWaitSeconds; _seedCounter = seedStart;
        }

        public void Enqueue(QueuedGroup g) => _queue.Add(g);
        public bool Dequeue(int groupId) => _queue.RemoveAll(g => g.GroupId == groupId) > 0;

        /// <summary>Call every few seconds. Returns a match when one can form, oldest groups first.</summary>
        public FormedMatch TryForm(float now)
        {
            if (_queue.Count == 0) return null;
            // Tiers are separate queues: a match is only ever formed from groups that queued for the same tier.
            foreach (int tier in _queue.Select(g => g.Tier).Distinct().OrderBy(t => t))
            {
                var ordered = _queue.Where(g => g.Tier == tier).OrderBy(g => g.QueuedAt).ThenBy(g => g.GroupId).ToList();
                bool waited = now - ordered[0].QueuedAt >= MaxWaitSeconds;
                int need = ordered.Count >= TeamsPerMatch ? TeamsPerMatch : waited && ordered.Count >= MinTeamsAfterWait ? ordered.Count : 0;
                if (need == 0) continue;
                var teams = ordered.Take(need).ToList();
                foreach (var t in teams) _queue.Remove(t);
                return new FormedMatch(unchecked(_seedCounter++ * 2654435761u.GetHashCode() ^ (int)now), teams);
            }
            return null;
        }
    }
}
