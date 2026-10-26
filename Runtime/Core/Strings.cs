using System.Collections.Generic;

namespace DungeonGen.Core
{
    /// <summary>
    /// Every player-facing string goes through here, so a second language is a file, not a
    /// refactor. English only at launch. A missing key returns the key itself, so an untranslated
    /// line is visible in play rather than a crash.
    /// </summary>
    public static class Strings
    {
        public static readonly IReadOnlyList<string> LaunchLanguages = new[] { "en" };
        public static string Language { get; private set; } = "en";

        private static readonly Dictionary<string, string> English = new Dictionary<string, string>
        {
            ["announce.final_boss_dead"] = "The Sovereign Below has fallen.",
            ["announce.exit_open"] = "The way out has opened at the heart of the dungeon.",
            ["hud.extract"] = "Extract", ["hud.extract_open"] = "Extract (open)",
            ["hud.boss"] = "Boss", ["hud.boss_dead"] = "Boss (dead)",
            ["hud.final_boss"] = "Final boss", ["hud.exit_open"] = "Exit (open)",
            ["hud.vault_locked"] = "Vault (locked)", ["hud.vault_open"] = "Vault (open)",
            ["hud.shrine"] = "Shrine", ["hud.spawn"] = "Spawn",
            ["death.by_dungeon"] = "Killed by the dungeon.", ["death.finished_by_dungeon"] = "Finished by the dungeon.",
            ["death.by_monster"] = "Killed by {0}.", ["death.finished_by_monster"] = "Finished by {0}.",
            ["roll.need"] = "Need", ["roll.greed"] = "Greed", ["roll.pass"] = "Pass",
            ["event.elite"] = "Something old walks the halls, and it carries a relic.",
            ["event.vault"] = "A vault's lock has failed. It will not stay open long.",
            ["event.shrine"] = "A shrine burns bright: one soul brings back the dead, for a little while.",
            ["report.cheating"] = "Cheating", ["report.rmt"] = "Real-money trading", ["report.abuse"] = "Abuse",
        };
        private static Dictionary<string, string> _active = new Dictionary<string, string>(English);

        public static IEnumerable<string> Keys => English.Keys;

        /// <summary>Install a language table. Keys missing from it fall back to English, then to the key.</summary>
        public static void Load(string language, IReadOnlyDictionary<string, string> table)
        {
            Language = language;
            _active = new Dictionary<string, string>(English);
            foreach (var kv in table) _active[kv.Key] = kv.Value;
        }
        public static void ResetToEnglish() { Language = "en"; _active = new Dictionary<string, string>(English); }

        public static string Get(string key) => _active.TryGetValue(key, out var s) ? s : key;
    }
}
