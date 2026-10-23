using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Items;

namespace DungeonGen.Core.Combat
{
    /// <summary>
    /// "The weapon in hand is the spec" for every class. Each spec is four abilities on the shared
    /// skeleton (bolt, zone, mobility, passive); the kit coefficient and attributes stay with the
    /// class, so a spec changes how you fight and never how much. A class holding a weapon it has
    /// no spec for falls back to basic attacks at the off-stat penalty.
    /// </summary>
    public sealed class WeaponSpec
    {
        public string ClassName { get; }
        public string WeaponBase { get; }
        public string Name { get; }
        public string Tagline { get; }
        public string Bolt { get; }
        public string Zone { get; }
        public string Mobility { get; }
        public string Passive { get; }
        public string DungeonJob { get; }
        /// <summary>Earned, never given: armed at a shrine for two souls, one use, lost on death, never leaves the dungeon.</summary>
        public string Ultimate { get; }
        public IReadOnlyList<string> Abilities => new[] { Bolt, Zone, Mobility, Passive };

        public WeaponSpec(string cls, string weapon, string name, string tagline, string bolt, string zone, string mobility, string passive, string job, string ultimate = null)
        {
            ClassName = cls; WeaponBase = weapon; Name = name; Tagline = tagline; Bolt = bolt; Zone = zone; Mobility = mobility; Passive = passive; DungeonJob = job;
            Ultimate = ultimate ?? (Ultimates.TryGetValue(name, out var u) ? u : null);
        }

        /// <summary>
        /// Every ultimate has one of three jobs: deny space (change the layout for a while), swing a
        /// fight, or find or mark (beat the vision rule). All of them obey full friendly fire: Inferno
        /// burns your team, Absolute Zero freezes them, Blackout stops their channels. Position first.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> Ultimates = new Dictionary<string, string>
        {
            // deny space
            ["Breaker"] = "Cave-In: collapse the doorway you stand in; rubble blocks everything, bodies and abilities, for 15 s.",
            ["Trapper"] = "Lockdown: every doorway of the room you stand in is tripwired for 20 s; the first to cross each is rooted and revealed.",
            ["Pyromancy"] = "Inferno: the room you stand in burns for 6 s, applying Burn to everyone inside every second. Everyone.",
            ["Volleyer"] = "Suppression: a 10 m zone for 8 s; anyone inside is Chilled and cannot start a channel: no extraction, no revive, no loot.",
            ["Stormcalling"] = "Blackout: for 8 s nobody within 10 m can channel anything: no extraction, no revive, no loot, no boss channel.",
            // swing a fight
            ["Sentinel"] = "Aegis: for 5 s you cannot be moved and allies within 5 m take half damage.",
            ["Duelist"] = "Perfect Guard: for 3 s every hit on you is parried and reflected; the first attacker is Staggered.",
            ["Berserker"] = "Bloodbath: for 8 s every hit heals 20% of its damage and you cannot be downed; if you are at zero when it ends, you fall.",
            ["Cryomancy"] = "Absolute Zero: Freeze everyone within 8 m for 2 s, ignoring diminishing returns.",
            ["Warder"] = "Last Breath: revive a downed ally instantly, no channel, both of you at full health. Never works on a finished player.",
            ["Plaguebearer"] = "Outbreak: every enemy within 10 m goes to maximum Poison and spreads it to anything they touch for 10 s.",
            // find or mark
            ["Assassin"] = "Death Mark: mark a player for 10 s; your hits on them are critical and your team sees them through walls.",
            ["Sharpshooter"] = "Overwatch: for 10 s, every enemy who enters your line of sight is shot once automatically.",
        };

        /// <summary>Which job an ultimate does. Used by the HUD and by tests that keep every spec to exactly one.</summary>
        public enum UltimateJob { DenySpace, SwingFight, FindOrMark }

        public static readonly IReadOnlyDictionary<string, UltimateJob> UltimateJobs = new Dictionary<string, UltimateJob>
        {
            ["Breaker"] = UltimateJob.DenySpace, ["Trapper"] = UltimateJob.DenySpace, ["Pyromancy"] = UltimateJob.DenySpace, ["Volleyer"] = UltimateJob.DenySpace, ["Stormcalling"] = UltimateJob.DenySpace,
            ["Sentinel"] = UltimateJob.SwingFight, ["Duelist"] = UltimateJob.SwingFight, ["Berserker"] = UltimateJob.SwingFight, ["Cryomancy"] = UltimateJob.SwingFight, ["Warder"] = UltimateJob.SwingFight, ["Plaguebearer"] = UltimateJob.SwingFight,
            ["Assassin"] = UltimateJob.FindOrMark, ["Sharpshooter"] = UltimateJob.FindOrMark,
        };

        public static WeaponSpec For(string className, ItemBase weapon)
        {
            if (weapon == null) return null;
            var spec = All.FirstOrDefault(s => s.ClassName == className && s.WeaponBase == weapon.Name);
            if (spec != null) return spec;
            // The Elementalist's specs are its element kits: any staff of an element it has a kit for.
            if (className == "Elementalist" && weapon.ScalesWith == Attribute.Intelligence)
            {
                var kit = ElementKit.For(weapon.AttackType);
                if (kit != null) return new WeaponSpec(className, weapon.Name, kit.Name, kit.Tagline, kit.Bolt, kit.Zone, kit.Mobility, kit.Passive, "See the element kit.");
            }
            return null;
        }

        public static IEnumerable<WeaponSpec> ForClass(string className) => All.Where(s => s.ClassName == className);

        public static readonly IReadOnlyList<WeaponSpec> All = new[]
        {
            // ---- Warden: holds or pushes. Neither touches a lock. ----
            new WeaponSpec("Warden", "Longsword", "Sentinel", "Holds the doorway so the team decides where the fight happens.",
                "Shield Bash: single target, Staggers, short range", "Shield Wall: block frontal damage for 4 s, allies behind take none", "Hold the Line: root yourself, taunt everything within 6 m, 40% damage reduction for 6 s", "Bastion: cannot be knocked back while standing in a doorway",
                "Body-blocks corridors and vault doors; makes a vault defensible while a teammate loots it."),
            new WeaponSpec("Warden", "Warhammer", "Breaker", "The bruiser tank. Makes space with impact instead of holding it with a shield.",
                "Crush: heavy single hit, Staggers", "Shockwave: ground slam, knocks everything in a cone back 4 m and Staggers it (long cooldown)", "Charge: short rush, knocks the first target back, interrupts", "Momentum: each hit builds a stack of 5% damage reduction, up to 30%, lost 3 s after you stop swinging",
                "Clears a doorway from the other side, shoves a team off an extract. Tankiest while fighting, not while standing. Never breaks a lock."),
            // ---- Reaver ----
            new WeaponSpec("Reaver", "Greataxe", "Berserker", "Cleaves. The boss and add killer.",
                "Cleave: cone hit, Bleeds everything it touches", "Whirlwind: spin for 3 s, hitting all around you", "Lunge: 8 m gap-close, next hit Bleeds", "Frenzy: +1% damage per 1% missing health; kills refresh Lunge",
                "Fastest boss killer per player; eats summoner adds. The 2.5-player budget assumes one Berserker-grade DPS."),
            new WeaponSpec("Reaver", "Longsword", "Duelist", "Single target. The PvP killer.",
                "Thrust: single target, high damage, Bleeds", "Riposte: parry stance for 1 s; a blocked hit is reflected and interrupts the attacker", "Lunge: 8 m gap-close", "Execute: triple damage below 25% health",
                "Punishes the player who committed first. Riposte answers a Breaker's Charge and a Juggernaut's Crush."),
            // ---- Marksman ----
            new WeaponSpec("Marksman", "Longbow", "Sharpshooter", "Controls sightlines down long corridors.",
                "Piercing Shot: hits everything in a line, corridor-length range", "Caltrops: slow field on a doorway for 8 s", "Vault Back: leap backwards, brief invulnerability", "Deadeye: +30% damage to targets further than 12 m",
                "Punishes teams pushing through spine corridors; a hub becomes a kill box."),
            new WeaponSpec("Marksman", "Shortbow", "Volleyer", "Speed and area. Anti-swarm, anti-rush.",
                "Rapid Shot: fast single-target shots", "Rain of Arrows: 5 m circle for 4 s, hits everything inside each second", "Tumble: short dodge roll", "Quickdraw: attack speed +20% for 4 s after a dodge",
                "Clears skitterers and shreds a Choir's corpses; keeps a chaser off a teammate."),
            // ---- Shade ----
            new WeaponSpec("Shade", "Twin Daggers", "Assassin", "Kills the key holder.",
                "Backstab: triple damage from behind", "Smoke Step: drop a smoke cloud, dash through it", "Veil: invisible for 5 s while moving slowly, broken by damage", "Hamstring: hits slow and silence for 2 s",
                "Opens on the player carrying the key or the most loot, then leaves."),
            new WeaponSpec("Shade", "Shortbow", "Trapper", "Controls the extract instead of the fight.",
                "Snare Shot: roots for 1.5 s", "Tripwire: invisible line across a doorway; the first to cross it is rooted and revealed", "Smoke Step: drop a smoke cloud, dash through it", "Keen Sense: reveals keys, locked doors, the gated exit, and players within 15 m through walls for 3 s",
                "Holds an extract for a loaded team; sees who holds the key before anyone else."),
            // ---- Mender ----
            new WeaponSpec("Mender", "Holy Sceptre", "Warder", "Pure sustain. The reference healer.",
                "Mend: heal over time, stacks twice", "Sanctuary: ground circle, allies inside take 25% less damage", "Purge: remove every status from an ally", "Last Rites: stabilise a downed ally in 3 s; no resurrection outside a shrine",
                "The PartySustain number assumes a Warder; without one a trio fights a room boss at duo odds."),
            new WeaponSpec("Mender", "Censer", "Plaguebearer", "The healer that fights.",
                "Smouldering Bolt: Poisons on hit", "Miasma: heal-over-time cloud for allies that Poisons enemies inside", "Purge: remove every status from an ally", "Contagion: your heals also apply one Poison stack to enemies within 3 m of the target; your raw healing is 25% lower",
                "Trades sustain for pressure; the Herald's Censer unique is this spec's dream item."),
        };
    }
}
