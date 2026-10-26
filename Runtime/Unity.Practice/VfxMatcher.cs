using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DungeonGen.Core.Combat;

namespace DungeonGen.Unity
{
    /// <summary>
    /// Ranks effect prefabs for an ability and a role. Shared by the audition panel (which lists the best candidates
    /// first) and the batch auto-assigner (which takes the best unused one). The score has three parts: the folder
    /// theme of the pack (Fire, Frost, Shock, ... against the ability's element or flavour), the prefab name against
    /// the role (Projectile/Ball/Bolt/Missile for a projectile, Impact/Hit/Explosion for an impact, Area/Floor/Field/
    /// Circle/Aura for a zone, Slash for melee), and the ability's own hint words. Obvious non-combat prefabs
    /// (UI, pixel retro, circus, disco, christmas, furniture, loot beams, wings ...) never score at all.
    /// Pure string work on asset paths: no AssetDatabase, so it is testable and runs anywhere.
    /// </summary>
    public static class VfxMatcher
    {
        /// <summary>What an ability should look like: pack folders in order of preference, name words it wants, name words it does not.</summary>
        public sealed class Profile
        {
            public string[] Themes = new string[0];
            public string[] Words = new string[0];
            public string[] Avoid = new string[0];
        }

        /// <summary>Score at or above which a prefab counts as a fitting candidate.</summary>
        public const int FitThreshold = 8;

        private const string PackRoot = "Assets/Piloto Studio/";
        private const string BundleRoot = "Elemental VFX Mega Bundle/";
        private const int NotAllowed = int.MinValue / 4;

        // ---- exclusion ----

        private static readonly string[] ExcludedWords =
        {
            "Arcade Pixel Retro", "Circus", "Disco", "Christmas", "Furniture", "Cubemap", "MaxLevel", "Floppy", "InfiniteMoney",
            "Baubles", "Fountain", "Musical", "Dance", "Loot", "LevelUp", "Preview", "Example", "Hologram", "Portal", "Rubick",
            "Trophy", "Money", "Halloween", "Gumdrop", "Sugar", "Weather", "Snowy Interest", "Universe", "Cosmic",
        };
        private static readonly Regex UiWord = new Regex(@"(^|[^A-Za-z])UI([^a-z]|$)");
        private static readonly Regex WingName = new Regex(@"(^|[ _/])wing([ _.]|$)", RegexOptions.IgnoreCase);

        private static bool Has(string s, string w) => s.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>True for prefabs that are not combat effects. Bees only count for healing abilities.</summary>
        public static bool IsExcluded(string path, bool healing = false)
        {
            string p = path.Replace('\\', '/');
            foreach (var w in ExcludedWords) if (Has(p, w)) return true;
            if (UiWord.IsMatch(p)) return true;
            if (WingName.IsMatch(Path.GetFileNameWithoutExtension(p))) return true;
            string file = Path.GetFileNameWithoutExtension(p);
            if (Has(file, "Heart") && !Has(file, "HeartOfNature")) return true;
            if (Has(p, "/Bees/") && !healing) return true;
            return false;
        }

        /// <summary>The pack folder a prefab belongs to: "Fire", "Poison And Acid", "Wind" ...</summary>
        public static string Theme(string path)
        {
            string p = path.Replace('\\', '/');
            int i = p.IndexOf(PackRoot, StringComparison.OrdinalIgnoreCase);
            if (i >= 0) p = p.Substring(i + PackRoot.Length);
            if (p.StartsWith(BundleRoot, StringComparison.OrdinalIgnoreCase)) p = p.Substring(BundleRoot.Length);
            int slash = p.IndexOf('/');
            return slash > 0 ? p.Substring(0, slash) : p;
        }

        // ---- ability flavour ----

        private static readonly Dictionary<DamageType, string[]> ElementThemes = new Dictionary<DamageType, string[]>
        {
            { DamageType.Fire, new[] { "Fire" } },
            { DamageType.Frost, new[] { "Frost" } },
            { DamageType.Lightning, new[] { "Shock" } },
            { DamageType.Poison, new[] { "Poison And Acid" } },
            { DamageType.Shadow, new[] { "Dark", "Void", "Crimson" } },
        };

        /// <summary>Folder themes per element, as the audition's old keyword list but by folder.</summary>
        public static string[] ThemesOf(DamageType element) => ElementThemes.TryGetValue(element, out var t) ? t : new string[0];

        private static readonly string[] PhysicalThemes = { "Wind", "Mist", "Tech" };
        private static readonly string[] HealThemes = { "Holy", "Light", "Nature", "Fairy" };

        private static Profile H(string themes, string words, string avoid = null) => new Profile
        {
            Themes = themes == null ? null : themes.Split(','),
            Words = words == null ? new string[0] : words.Split(','),
            Avoid = avoid == null ? new string[0] : avoid.Split(','),
        };

        /// <summary>Per-ability hints for everything the element and shape cannot tell: weapons, shields, smoke, traps, heals.</summary>
        private static readonly Dictionary<string, Profile> Hints = new Dictionary<string, Profile>
        {
            { "Sentinel.basic", H("Wind,Mist,Light", "Slash") },
            { "Sentinel.bolt", H("Light,Mist,Wind", "Impact,Hit,Smite,Smash") },
            { "Sentinel.zone", H("Holy,Light,Tech", "Shield,Guard,Angelical,Hexashield") },
            { "Sentinel.mobility", H("Holy,Light", "Pulse,Ring,Halo,Taunt") },
            { "Sentinel.ult", H("Holy,Light", "Sanctum,Dome,Protection,Guardian,Halo") },
            { "Breaker.basic", H("Mist,Wind,Tech", "Slash,Smash") },
            { "Breaker.bolt", H("Wind,Mist,Tech", "Smash,Clap,Impact,Crack") },
            { "Breaker.zone", H("Wind,Mist,Tech", "ShockWave,Clap,Crack,Eruption,Cone") },
            { "Breaker.mobility", H("Wind,Mist", "Charge,Rush,Lunge,Dash") },
            { "Breaker.ult", H("Tech,Wind", "Rocky,Crack,Quake,Eruption,Ground,Geiser") },
            { "Berserker.basic", H("Crimson,Wind", "Slash,Claw") },
            { "Berserker.bolt", H("Crimson,Wind", "Slash,Wave,Cone,Claw") },
            { "Berserker.zone", H("Crimson,Wind", "Whirlwind,Blade,Spin,Tornado") },
            { "Berserker.mobility", H("Crimson,Wind", "Lunge,Charge,Dash,Woosh") },
            { "Berserker.ult", H("Crimson", "Aura,Buff,Rage,Shield,Blood") },
            { "Duelist.basic", H("Wind,Light,Mist", "Slash") },
            { "Duelist.bolt", H("Wind,Light,Mist", "Impale,Thrust,Impact,Hit") },
            { "Duelist.zone", H("Wind,Light,Mist", "Shield,Guard,Parry,Buff") },
            { "Duelist.mobility", H("Wind,Light,Mist", "Lunge,Charge,Dash,Woosh") },
            { "Duelist.ult", H("Light,Holy,Tech", "Shield,Guard,Barrier,Aura") },
            { "Sharpshooter.basic", H("Wind,Light,Nature", "Arrow,StraightShot,Spear", "Rain") },
            { "Sharpshooter.bolt", H("Wind,Light", "StraightShot,Arrow,Ray,Lance") },
            { "Sharpshooter.zone", H("Nature,Tech,Wind", "Impale,Spike,Trap,Crack,Ground,Rocky,Thorn") },
            { "Sharpshooter.mobility", H("Wind,Mist", "Woosh,Dash,Leap,Burst") },
            { "Sharpshooter.ult", H("Light,Tech", "Target Lock,Scan,Aura,Eye,Guard") },
            { "Volleyer.basic", H("Wind,Light,Tech", "Arrow,StraightShot,Spear", "Rain") },
            { "Volleyer.bolt", H("Wind,Light,Tech", "Arrow,StraightShot,Missile", "Rain") },
            { "Volleyer.zone", H("Wind,Light,Tech", "Arrow Rain,Rain,Skyshot") },
            { "Volleyer.mobility", H("Wind,Mist", "Woosh,Dash,Roll,Burst") },
            { "Volleyer.ult", H("Frost,Wind", "Arrow Rain,Blizzard,Storm,Field") },
            { "Assassin.basic", H("Dark,Void,Crimson,Mist", "Slash,Claw") },
            { "Assassin.bolt", H("Dark,Void,Crimson", "Impale,Hit,Slash,Claw,Puncture") },
            { "Assassin.zone", H("Mist,Dark", "Smoke,Mist,Ghostly,Burst,Dash") },
            { "Assassin.mobility", H("Mist,Dark,Void", "Aura,Ghost,Smoke,Mist") },
            { "Assassin.ult", H("Crimson,Dark", "Mark,Curse,Impale,Hit,Ritual,Puncture") },
            { "Trapper.basic", H("Nature,Wind", "Arrow,StraightShot,Leaf", "Rain") },
            { "Trapper.bolt", H("Nature,Wind", "Projectile,Missile,Leaf,Vine,Grass", "Rain") },
            { "Trapper.zone", H("Nature,Dark", "Impale,Trap,Root,Ground,Bone,Thorn") },
            { "Trapper.mobility", H("Mist,Dark", "Smoke,Mist,Ghostly,Burst,Dash") },
            { "Trapper.ult", H("Nature,Dark", "EnchantedGround,Wall,Trap,Root,Thorn") },
            { "Warder.basic", H("Holy,Light,Fairy", "Projectile,Orb,Ball,Missile") },
            { "Warder.bolt", H("Holy,Light,Fairy,Nature", "Heal,Lifeglow,Rune,Glow,Orb,Life") },
            { "Warder.zone", H("Holy,Light,Fairy", "Sanctum,Ring,Halo,Healing,Pool") },
            { "Warder.mobility", H("Holy,Light,Nature", "Purif,Cure,Rebirth,Glow") },
            { "Warder.ult", H("Holy,Light", "Rebirth,Lifeglow,Angel,Seraphic,Vow") },
            { "Plaguebearer.basic", H(null, "Ball,Spit,Goo,Projectile") },
            { "Plaguebearer.mobility", H("Nature,Holy,Light", "Purif,Cure,Heal,Glow") },
            { "Plaguebearer.ult", H(null, "Explosion,Fumes,Bubble,Geyser,Cloud") },
            { "Pyromancy.zone", H(null, "Wall,Storm,Field,Pool") },
            { "Pyromancy.mobility", H("Fire", "Recall,Teleport,Burst,Dash") },
            { "Pyromancy.ult", H(null, "Storm,Eruption,Pilar,Field,Ground") },
            { "Cryomancy.mobility", H("Frost", "Slide,Trail,Woosh,Dash,Spike") },
            { "Cryomancy.zone", H(null, "FrozenField,Field,Ground,Pool") },
            { "Cryomancy.ult", H(null, "Blizzard,Nova,Burst,Explosion,Crystal") },
            { "Stormcalling.mobility", H("Shock", "Recall,Teleport,Blink,Burst") },
            { "Stormcalling.zone", H(null, "Field,Ground,Pool,Storm") },
            { "Stormcalling.ult", H("Shock", "Storm,Field,Tornado,Pilar,Ground") },
            { "Mob.basic", H("Crimson", "Slash,Claw,Impact,Hit") },
            { "Mob.ranged", H("Dark,Crimson", "Projectile,Missile,Orb,Ball") },
            { "Boss.basic", H("Crimson,Dark", "Impact,Smash,Hit,Slash") },
            { "Boss.slam", H("Dark,Crimson,Fire", "Eruption,Explosion,Clap,ShockWave,Crack") },
        };

        /// <summary>The look an ability wants: themes by element, then hint, then healing, then neutral physical.</summary>
        public static Profile ProfileOf(AbilityDefinition a)
        {
            Hints.TryGetValue(a.Id, out var hint);
            var p = new Profile { Words = hint?.Words ?? new string[0], Avoid = hint?.Avoid ?? new string[0] };
            if (a.Element.HasValue && a.Element.Value != DamageType.Physical && ElementThemes.TryGetValue(a.Element.Value, out var et))
            {
                p.Themes = a.Heal > 0f ? et.Concat(new[] { "Nature" }).ToArray() : et;
                return p;
            }
            if (hint?.Themes != null) p.Themes = hint.Themes;
            else p.Themes = a.Heal > 0f ? HealThemes : PhysicalThemes;
            return p;
        }

        // ---- role keywords ----

        private static readonly (string word, int weight)[] ProjectileWords =
        {
            ("Projectile", 10), ("Missile", 10), ("Bolt", 10), ("Ball", 9), ("StraightShot", 8), ("Spear", 7), ("Lance", 7), ("Dart", 7),
            ("Bullet", 7), ("Comet", 6), ("Spit", 6), ("Needle", 6), ("Arrow", 5), ("Orb", 5),
        };
        private static readonly string[] ProjectileAvoid =
        {
            "Aura", "Pool", "Ground", "Rain", "BlackHole", "Black Hole", "Tornado", "Nado", "Nuclear", "Shield", "Recall", "Buff", "Sky", "Pilar",
            "Eruption", "Ring", "Wall", "Hit", "Muzzle", "Impact", "Explosion", "Crossed", "Spray", "Cone", "Slash", "Cast",
        };
        private static readonly (string word, int weight)[] PierceWords =
        {
            ("Ray", 10), ("Beam", 10), ("Laser", 10), ("StraightShot", 9), ("Lance", 7), ("Projectile", 6), ("Arrow", 5), ("Spear", 5),
        };
        private static readonly (string word, int weight)[] ImpactWords =
        {
            ("Impact", 10), ("Hit", 8), ("Explosion", 8), ("Burst", 7), ("Blast", 7), ("Splash", 4), ("Crack", 4), ("Clap", 4), ("Smite", 6), ("Puncture", 5),
        };
        private static readonly string[] ImpactAvoid =
        {
            "Aura", "Wing", "BlackHole", "Black Hole", "Tornado", "Nado", "Nuclear", "Recall", "Muzzle", "Shield", "Sky", "Ring", "Crossed", "Cast", "Loop", "Pool", "Ground",
        };
        private static readonly (string word, int weight)[] ConeWords =
        {
            ("AOECone", 10), ("ConalSpliff", 8), ("Cone", 8), ("Slash", 9), ("Breath", 7), ("Spray", 7), ("Wave", 6), ("Swing", 7), ("Sweep", 7), ("Claw", 7), ("Fan", 5),
        };
        private static readonly (string word, int weight)[] BlastWords =
        {
            ("AOE_Explosion", 10), ("Explosion", 9), ("Eruption", 9), ("Nova", 9), ("Pulse", 7), ("Blast", 8), ("Burst", 7), ("Impact", 6), ("Clap", 5),
        };
        private static readonly (string word, int weight)[] ZoneWords =
        {
            ("EnchantedGround", 10), ("ElementalPool", 10), ("Floor", 10), ("Field", 10), ("AOE Magic Circle", 10), ("MagicRing", 8), ("Circle", 8), ("Area", 8), ("Pool", 8),
            ("Zone", 8), ("Aura_Ground", 8), ("Ground", 6), ("Totem", 6), ("Rain", 6), ("Cloud", 6), ("Storm", 6), ("Wall", 5), ("Healing", 6), ("Sanctum", 6), ("Aura", 4),
        };
        private static readonly string[] ZoneAvoid =
        {
            "Projectile", "Impact", "Hit", "Slash", "Muzzle", "Missile", "Recall", "Spray", "Cone", "Orb", "Crossed", "Sky", "Cast", "Dissipation", "BuildUp", "Black Hole", "BlackHole", "Nuclear", "Tornado", "Nado",
        };
        private static readonly (string word, int weight)[] SelfCastWords =
        {
            ("Aura", 8), ("Shield", 10), ("Buff", 10), ("Defense", 9), ("Guard", 9), ("Barrier", 9), ("Dome", 9), ("Protection", 9), ("Halo", 8), ("Embrace", 7), ("Empowerment", 8), ("Cast", 3),
        };
        private static readonly (string word, int weight)[] DashCastWords =
        {
            ("Dash", 10), ("Blink", 10), ("Teleport", 10), ("Lunge", 10), ("Charge", 8), ("Rush", 8), ("Recall", 7), ("Recal", 7), ("Trail", 7), ("Woosh", 8), ("Smoke", 7), ("Burst", 5), ("Slash", 4),
        };
        private static readonly (string word, int weight)[] CasterCastWords =
        {
            ("Cast", 10), ("Muzzle", 9), ("Charge", 6), ("Glyph", 6), ("Gathering", 6), ("ChargingUp", 6), ("MagicRing", 6), ("DruidCast", 8), ("Ring", 3),
        };
        private static readonly (string word, int weight)[] MeleeCastWords =
        {
            ("Slash", 10), ("Claw", 8), ("Blade", 7), ("Sweep", 7), ("Wave_", 3), ("Cast", 2),
        };
        private static readonly string[] CastAvoid =
        {
            "Projectile", "Impact", "BlackHole", "Black Hole", "Tornado", "Nado", "Nuclear", "Hit", "Explosion", "Sky", "Crossed", "Rain", "Dissipation", "BuildUp",
        };
        private static readonly string[] DashCastAvoid =
        {
            "Projectile", "Impact", "BlackHole", "Black Hole", "Nuclear", "Hit", "Explosion", "Sky", "Crossed", "Rain", "Dissipation", "BuildUp", "Pool", "Aura",
        };
        private static readonly (string word, int weight)[] SmallImpactWords =
        {
            ("Impact", 10), ("Hit", 9), ("Puncture", 6), ("Splash", 5), ("Burst", 5), ("Smite", 6), ("Explosion", 4), ("Blast", 4), ("Crack", 3),
        };
        private static readonly string[] CasterAvoid =
        {
            "Projectile", "Impact", "BlackHole", "Black Hole", "Tornado", "Nado", "Nuclear", "Hit", "Explosion", "Sky", "Crossed", "Rain", "Dissipation", "BuildUp", "Slash", "Wave",
        };
        private static readonly string[] SpecialAvoid = { "Nuclear", "BlackHole", "Black Hole", "Tornado", "Nado" };

        private static int Match((string word, int weight)[] table, string file)
        {
            int best = 0, bonus = 0;
            foreach (var (w, wt) in table)
            {
                if (!Has(file, w)) continue;
                if (wt > best) { bonus += best / 4; best = wt; } else bonus += wt / 4;
            }
            return best + Math.Min(bonus, 4);
        }

        private static int AvoidPenalty(string[] avoid, string file, int each)
        {
            int n = 0;
            foreach (var w in avoid) if (Has(file, w)) n++;
            return -Math.Min(n, 3) * each;
        }

        private static int NameScore(AbilityDefinition a, VfxRole role, string file)
        {
            switch (role)
            {
                case VfxRole.Projectile:
                    return Match(a.Shape == AbilityShape.Pierce ? PierceWords : ProjectileWords, file) + AvoidPenalty(ProjectileAvoid, file, 6);
                case VfxRole.Impact:
                    if (a.Shape == AbilityShape.Cone) return Match(ConeWords, file) + AvoidPenalty(ImpactAvoid, file, 5);
                    if (a.Shape == AbilityShape.Circle) return Math.Max(Match(BlastWords, file), Match(ImpactWords, file) - 2) + AvoidPenalty(ImpactAvoid, file, 5);
                    if (a.Shape == AbilityShape.Target && a.Kind == AbilityKind.Basic) return Math.Max(Match(ConeWords, file) - 1, Match(ImpactWords, file)) + AvoidPenalty(ImpactAvoid, file, 5);
                    return Match(SmallImpactWords, file) + AvoidPenalty(ImpactAvoid, file, 5);
                case VfxRole.Zone:
                    return Match(ZoneWords, file) + AvoidPenalty(ZoneAvoid, file, 5);
                default:                                                             // Cast
                    switch (a.Shape)
                    {
                        case AbilityShape.Dash: return Match(DashCastWords, file) + AvoidPenalty(DashCastAvoid, file, 5);
                        case AbilityShape.Self: return Match(SelfCastWords, file) + AvoidPenalty(CastAvoid, file, 5);
                        case AbilityShape.Cone: case AbilityShape.Target: return Match(MeleeCastWords, file) + AvoidPenalty(CastAvoid, file, 5);
                        default: return Match(CasterCastWords, file) + AvoidPenalty(CasterAvoid, file, 5);
                    }
            }
        }

        /// <summary>
        /// How well a prefab fits an ability in a role. Very negative means "never"; at or above <see cref="FitThreshold"/> is a fit.
        /// A prefab from a theme the ability does not want is pushed far down; a name that does not suit the role is not a fit.
        /// </summary>
        public static int Score(AbilityDefinition a, VfxRole role, string path, Profile profile = null)
        {
            profile = profile ?? ProfileOf(a);
            if (IsExcluded(path, a.Heal > 0f)) return NotAllowed;
            string file = Path.GetFileNameWithoutExtension(path);
            string theme = Theme(path);

            int themeScore = -25;
            for (int i = 0; i < profile.Themes.Length; i++)
                if (string.Equals(theme, profile.Themes[i], StringComparison.OrdinalIgnoreCase)) { themeScore = Math.Max(4, 12 - i * 3); break; }

            int name = NameScore(a, role, file);
            if (name <= 0) name -= 12;                                    // a prefab whose name says nothing about the role is not a fit, whatever its theme
            int hint = 0;
            foreach (var w in profile.Words) if (Has(file, w)) hint += 6;
            hint = Math.Min(hint, 12);
            hint += AvoidPenalty(profile.Avoid, file, 8);

            int penalty = 0;
            if (a.Kind != AbilityKind.Ultimate) penalty += AvoidPenalty(SpecialAvoid, file, 6);
            else if (Has(file, "Nuclear") || Has(file, "Tornado") || Has(file, "Storm") || Has(file, "Meteor") || Has(file, "Eruption")) hint += 3;

            return themeScore + name + hint + penalty;
        }

        /// <summary>Every fitting prefab for this ability and role, best first. Ties break by path so the list is stable.</summary>
        public static List<string> Rank(AbilityDefinition a, VfxRole role, IEnumerable<string> catalogue)
        {
            var profile = ProfileOf(a);
            return catalogue
                .Select(p => (path: p, score: Score(a, role, p, profile)))
                .Where(s => s.score >= FitThreshold)
                .OrderByDescending(s => s.score)
                .ThenBy(s => s.path, StringComparer.OrdinalIgnoreCase)
                .Select(s => s.path)
                .ToList();
        }

        /// <summary>The audition's candidate list: the best fits for the role.</summary>
        public static List<string> CandidatesFor(AbilityDefinition a, VfxRole role, IEnumerable<string> catalogue) => Rank(a, role, catalogue);

        /// <summary>The roles an ability's effect needs, given how <c>AbilityEffects</c> draws its shape.</summary>
        public static VfxRole[] RolesNeeded(AbilityDefinition a)
        {
            switch (a.Shape)
            {
                case AbilityShape.Projectile: case AbilityShape.Pierce: return new[] { VfxRole.Projectile, VfxRole.Impact };
                case AbilityShape.Circle: return a.IsZone ? new[] { VfxRole.Zone, VfxRole.Cast } : new[] { VfxRole.Impact };
                case AbilityShape.Cone: case AbilityShape.Target: return new[] { VfxRole.Impact };
                case AbilityShape.Dash: return new[] { VfxRole.Cast, VfxRole.Impact };
                default: return new[] { VfxRole.Cast };                               // Self
            }
        }

        /// <summary>Projectile speed in metres per second: arrows 35, bolts 25, slow or heavy things 15.</summary>
        public static float ProjectileSpeedFor(AbilityDefinition a, string prefabPath)
        {
            string name = a.Name + " " + a.Text + " " + (prefabPath != null ? Path.GetFileNameWithoutExtension(prefabPath) : "");
            if (Has(name, "slow") || Has(name, "lobbed") || Has(name, "heavy")) return 15f;
            if (Has(name, "arrow") || Has(name, "Shot") || Has(name, "Spear") || Has(name, "Lance") || a.Shape == AbilityShape.Pierce) return 35f;
            return 25f;
        }
    }
}
