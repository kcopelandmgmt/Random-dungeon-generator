using System.Collections.Generic;

namespace DungeonGen.Core.Combat
{
    public enum ClassRole { Tank, MeleeDps, RangedDps, Caster, Support, Skirmisher }

    /// <summary>
    /// A playable class as the tuning model sees it: a role and multipliers on the combat
    /// baseline. Abilities are listed for design reference; implementing them is gameplay code.
    /// The roster average must stay ~1.0 on both multipliers so <see cref="CombatBaseline"/>
    /// keeps meaning "one average player".
    /// </summary>
    public sealed class ClassArchetype
    {
        public string Name { get; }
        public ClassRole Role { get; }
        public float DpsMultiplier { get; }
        public float HealthMultiplier { get; }
        /// <summary>Light / Medium / Heavy. Drives move speed, dodge and what armour drops they want.</summary>
        public string ArmourWeight { get; }
        public string Tagline { get; }
        public IReadOnlyList<string> Abilities { get; }
        /// <summary>What this class brings to the dungeon layer specifically (doors, keys, bosses, extraction).</summary>
        public string DungeonUtility { get; }

        public ClassArchetype(string name, ClassRole role, float dps, float health, string armour, string tagline, string[] abilities, string dungeonUtility)
        {
            Name = name;
            Role = role;
            DpsMultiplier = dps;
            HealthMultiplier = health;
            ArmourWeight = armour;
            Tagline = tagline;
            Abilities = abilities;
            DungeonUtility = dungeonUtility;
        }

        /// <summary>The six launch classes. Two of each weight; every trio composition has a plausible answer to every other.</summary>
        public static readonly IReadOnlyList<ClassArchetype> DefaultRoster = new[]
        {
            new ClassArchetype("Warden", ClassRole.Tank, 0.70f, 1.50f, "Heavy",
                "Holds the doorway so the team decides where the fight happens.",
                new[] { "Shield Wall: block frontal damage for 4s, allies behind you take none", "Bulwark Slam: knockback cone, interrupts casts", "Hold the Line: root yourself, taunt, 40% damage reduction for 6s", "Bastion: passive, cannot be knocked back while standing in a doorway" },
                "Body-blocks corridors and locked-door chokepoints; the class that makes a vault defensible while a teammate loots it."),
            new ClassArchetype("Reaver", ClassRole.MeleeDps, 1.30f, 1.00f, "Medium",
                "Burst and mobility. Opens fights and ends them.",
                new[] { "Lunge: gap-close 8m, next hit bleeds", "Execute: 3x damage below 25% health", "Blood Rush: kill resets Lunge and grants 30% speed for 3s", "Frenzy: passive, +1% damage per 1% missing health" },
                "Fastest boss killer per player; the 2.5-player boss budget assumes one Reaver-grade DPS in the trio."),
            new ClassArchetype("Marksman", ClassRole.RangedDps, 1.15f, 0.85f, "Light",
                "Controls sightlines down long corridors.",
                new[] { "Piercing Shot: hits everything in a line, corridor-length range", "Caltrops: slow field on a doorway for 8s", "Vault Back: leap backwards, brief invulnerability", "Deadeye: passive, +30% damage to targets further than 12m" },
                "Punishes teams pushing through spine corridors; traps turn a hub room into a kill box."),
            new ClassArchetype("Pyromancer", ClassRole.Caster, 1.20f, 0.80f, "Light",
                "Area denial. Burns rooms, not people.",
                new[] { "Firewall: 6m line of fire for 6s, blocks movement to anyone unwilling to burn", "Meteor: delayed AoE, huge damage, telegraphed", "Ignite: damage over time that spreads between adjacent enemies", "Cinder Step: short teleport, leaves fire behind" },
                "Firewall closes a door without a Warden; Meteor is the answer to a team turtling in a vault."),
            new ClassArchetype("Mender", ClassRole.Support, 0.65f, 1.10f, "Medium",
                "Keeps the trio alive long enough for the math to work.",
                new[] { "Mend: heal over time, stacks twice", "Sanctuary: ground circle, allies inside take 25% less damage", "Purge: remove bleeds, burns and slows from an ally", "Last Rites: stabilise a downed ally in 3s (no mid-fight resurrection: full loot means dead is dead)" },
                "The PartySustain number (1.5) assumes a Mender; without one a trio fights a room boss at roughly duo odds."),
            new ClassArchetype("Shade", ClassRole.Skirmisher, 1.00f, 0.75f, "Light",
                "Finds the key, picks the moment, leaves with the loot.",
                new[] { "Veil: invisible for 5s while moving slowly, broken by damage", "Keen Sense: reveals keys, locked doors and the boss-gated exit on the map for 10s", "Hamstring: slow and silence for 2s", "Smoke Step: drop a smoke cloud, dash through it" },
                "The extraction specialist: scouts vaults, sees who holds a key, and gets a loaded team out of a contested extract."),
        };
    }
}
