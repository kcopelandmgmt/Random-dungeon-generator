using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Net;

namespace DungeonGen.Core.Profile
{
    /// <summary>
    /// One rule twist per season, from season two on. Each is a single constant changed in the match
    /// rules or the generator settings, so a season feels new without new content. Season one ships
    /// with none: the base rules have to be learned before they can be bent.
    /// </summary>
    public sealed class Mutator
    {
        public string Name { get; }
        public string Tagline { get; }
        private readonly System.Action<MatchRules, DungeonSettings> _apply;
        private Mutator(string name, string tagline, System.Action<MatchRules, DungeonSettings> apply) { Name = name; Tagline = tagline; _apply = apply; }

        public void Apply(MatchRules rules, DungeonSettings settings) { rules.MutatorName = Name; _apply(rules, settings); }

        public static readonly IReadOnlyList<Mutator> All = new[]
        {
            new Mutator("Soulfeast", "Every corpse and every boss chest holds two souls.", (r, s) => r.SoulMultiplier = 2),
            new Mutator("Godless", "The shrines are dark. No resurrection, no ultimates, no shrine goods.", (r, s) => r.ShrinesUsable = false),
            new Mutator("Keybearers", "Every boss carries a vault key.", (r, s) => s.KeyFromBossChance = 1f),
            new Mutator("Dusk", "You see half as far.", (r, s) => r.SightRangeCells = MatchRules.DefaultSightRangeCells / 2),
            new Mutator("Twin Vaults", "Two vaults, two keys, twice the reason to fight.", (r, s) => s.VaultCount = 2),
        };

        /// <summary>Season one has none; after that they rotate in order, so every season's twist is known in advance.</summary>
        public static Mutator ForSeason(int seasonNumber) => seasonNumber <= 1 ? null : All[(seasonNumber - 2) % All.Count];
        public static Mutator ByName(string name) => All.FirstOrDefault(m => m.Name == name);
    }
}
