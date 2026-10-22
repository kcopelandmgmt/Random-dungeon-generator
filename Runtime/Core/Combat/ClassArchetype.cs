using System.Linq;
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
        /// <summary>Starting attributes: 50 points over five stats, 10 = average.</summary>
        public Attributes BaseAttributes { get; }
        /// <summary>The attribute this class's kit scales damage from.</summary>
        public Attribute Primary { get; }
        /// <summary>Damage coefficient of the kit itself, before attributes: a tank's abilities hit softer than a Reaver's even at equal Strength.</summary>
        public float KitCoefficient { get; }
        /// <summary>Drives armour rating, base resistances, move speed and what drops the class wants.</summary>
        public ArmourWeight Armour { get; }

        /// <summary>Sustained DPS relative to one baseline player, in starting gear. Derived from the kit and attributes.</summary>
        public float DpsMultiplier => KitCoefficient * StatFormulas.Linear(StatFormulas.DamagePerPrimary, BaseAttributes.Get(Primary));
        /// <summary>Effective health relative to one baseline player. Derived from Constitution.</summary>
        public float HealthMultiplier => StatFormulas.Linear(StatFormulas.HealthPerCon, BaseAttributes.Constitution);
        public string Tagline { get; }
        /// <summary>The fixed kit. For the Elementalist this is the shared skeleton; use <see cref="AbilitiesFor"/> for the element in hand.</summary>
        public IReadOnlyList<string> Abilities { get; }
        /// <summary>True for the class whose kit is decided by the element of the weapon it holds.</summary>
        public bool IsElementalist => Name == "Elementalist";

        /// <summary>Abilities with the element resolved: the Elementalist's kit for Fire / Frost / Lightning, everyone else's fixed kit.</summary>
        public IReadOnlyList<string> AbilitiesFor(DamageType element)
        {
            if (!IsElementalist) return Abilities;
            return ElementKit.For(element)?.Abilities ?? Abilities;
        }

        /// <summary>Elements this class can specialise in by weapon choice. Empty for non-casters.</summary>
        public IReadOnlyList<DamageType> Elements => IsElementalist ? ElementKit.All.Select(k => k.Element).ToList() : new List<DamageType>();
        /// <summary>What this class brings to the dungeon layer specifically (doors, keys, bosses, extraction).</summary>
        public string DungeonUtility { get; }

        public ClassArchetype(string name, ClassRole role, Attributes attributes, Attribute primary, float kit, ArmourWeight armour, string tagline, string[] abilities, string dungeonUtility)
        {
            Name = name;
            Role = role;
            BaseAttributes = attributes;
            Primary = primary;
            KitCoefficient = kit;
            Armour = armour;
            Tagline = tagline;
            Abilities = abilities;
            DungeonUtility = dungeonUtility;
        }

        /// <summary>The six launch classes. Two of each weight; every trio composition has a plausible answer to every other.</summary>
        public static readonly IReadOnlyList<ClassArchetype> DefaultRoster = new[]
        {
            new ClassArchetype("Warden", ClassRole.Tank, new Attributes(14, 6, 4, 20, 6), Attribute.Strength, 0.625f, ArmourWeight.Heavy,
                "Holds the doorway so the team decides where the fight happens.",
                new[] { "Shield Wall: block frontal damage for 4s, allies behind you take none", "Bulwark Slam: knockback cone, interrupts casts", "Hold the Line: root yourself, taunt, 40% damage reduction for 6s", "Bastion: passive, cannot be knocked back while standing in a doorway" },
                "Body-blocks corridors and locked-door chokepoints; the class that makes a vault defensible while a teammate loots it."),
            new ClassArchetype("Reaver", ClassRole.MeleeDps, new Attributes(16, 12, 4, 10, 8), Attribute.Strength, 1.10f, ArmourWeight.Medium,
                "Burst and mobility. Opens fights and ends them.",
                new[] { "Lunge: gap-close 8m, next hit bleeds", "Execute: 3x damage below 25% health", "Blood Rush: kill resets Lunge and grants 30% speed for 3s", "Frenzy: passive, +1% damage per 1% missing health" },
                "Fastest boss killer per player; the 2.5-player boss budget assumes one Reaver-grade DPS in the trio."),
            new ClassArchetype("Marksman", ClassRole.RangedDps, new Attributes(8, 17, 8, 7, 10), Attribute.Dexterity, 0.95f, ArmourWeight.Light,
                "Controls sightlines down long corridors.",
                new[] { "Piercing Shot: hits everything in a line, corridor-length range", "Caltrops: slow field on a doorway for 8s", "Vault Back: leap backwards, brief invulnerability", "Deadeye: passive, +30% damage to targets further than 12m" },
                "Punishes teams pushing through spine corridors; traps turn a hub room into a kill box."),
            new ClassArchetype("Elementalist", ClassRole.Caster, new Attributes(4, 8, 18, 6, 14), Attribute.Intelligence, 0.97f, ArmourWeight.Light,
                "One class, three elements. The staff in hand decides which.",
                new[] { "Bolt: single-target hit that applies the element's status", "Zone: a 5-6 m area that applies it to anyone inside", "Mobility: a short teleport or dash", "Passive: what the element's status does when it stacks" },
                "Fire closes a door without a Warden and clears adds. Frost stops a chase or a retreat. Lightning ends a channel. Which one you are is whichever staff you have not lost yet."),
            new ClassArchetype("Mender", ClassRole.Support, new Attributes(6, 8, 10, 12, 14), Attribute.Spirit, 0.58f, ArmourWeight.Medium,
                "Keeps the trio alive long enough for the math to work.",
                new[] { "Mend: heal over time, stacks twice", "Sanctuary: ground circle, allies inside take 25% less damage", "Purge: remove bleeds, burns and slows from an ally", "Last Rites: stabilise a downed ally in 3s (no mid-fight resurrection: full loot means dead is dead)" },
                "The PartySustain number (1.5) assumes a Mender; without one a trio fights a room boss at roughly duo odds."),
            new ClassArchetype("Shade", ClassRole.Skirmisher, new Attributes(8, 16, 8, 5, 13), Attribute.Dexterity, 0.85f, ArmourWeight.Light,
                "Finds the key, picks the moment, leaves with the loot.",
                new[] { "Veil: invisible for 5s while moving slowly, broken by damage", "Keen Sense: reveals keys, locked doors and the boss-gated exit on the map for 10s", "Hamstring: slow and silence for 2s", "Smoke Step: drop a smoke cloud, dash through it" },
                "The extraction specialist: scouts vaults, sees who holds a key, and gets a loaded team out of a contested extract."),
        };
    }
}
