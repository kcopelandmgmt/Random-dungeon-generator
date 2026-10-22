using System.Collections.Generic;

namespace DungeonGen.Core.Combat
{
    /// <summary>The pressure a boss puts on the room. Every dungeon draws at most one boss per family when it can.</summary>
    public enum BossFamily
    {
        SpaceDenial, // Shrinks the usable floor; doorways become the only safe ground
        Summoner,    // Adds keep coming until the boss dies; punishes splitting up
        Chaser,      // Leaves its room to pursue; can be dragged into another team
        Check,       // Tests one thing hard: a tank, a healer, burst damage or coordination
    }

    /// <summary>
    /// A boss design as the generator sees it: a family, a budget tweak, an arena size it wants,
    /// and the design text that explains the fight. Implementing the mechanics is gameplay code.
    /// </summary>
    public sealed class BossArchetype
    {
        public string Name { get; }
        public BossFamily Family { get; }
        public string Tagline { get; }
        public IReadOnlyList<string> Mechanics { get; }
        /// <summary>Which classes or compositions answer this boss, for the design sheet and for matchmaking telemetry later.</summary>
        public string Counters { get; }
        /// <summary>How the fight interacts with other teams arriving (full-loot PvP is the point).</summary>
        public string ThirdPartyNote { get; }
        /// <summary>Preferred arena side length (cells). Ring placement sizes the room to this when the canvas allows.</summary>
        public int ArenaMin { get; }
        public int ArenaMax { get; }
        /// <summary>Budget multipliers vs. the plain 2.5-player room boss, because some mechanics are worth more than raw numbers.</summary>
        public float HealthMultiplier { get; }
        public float DpsMultiplier { get; }
        /// <summary>What the boss hits with. The resistance to bring.</summary>
        public DamageProfile Damage { get; }
        /// <summary>Mob type stationed in the rooms around this boss, or null.</summary>
        public string RetinueMob { get; }
        /// <summary>Status the boss's hits apply, from its dominant element (Hound and Juggernaut: Bleed).</summary>
        public StatusEffectDefinition OnHit => Damage.Dominant == DamageType.Physical ? StatusEffectDefinition.All[StatusKind.Bleed] : StatusEffectDefinition.OnHitFor(Damage.Dominant);

        public BossArchetype(string name, BossFamily family, string tagline, string[] mechanics, string counters, string thirdParty,
            int arenaMin, int arenaMax, DamageProfile damage, string retinue = null, float healthMult = 1f, float dpsMult = 1f)
        {
            Damage = damage;
            RetinueMob = retinue;
            Name = name;
            Family = family;
            Tagline = tagline;
            Mechanics = mechanics;
            Counters = counters;
            ThirdPartyNote = thirdParty;
            ArenaMin = arenaMin;
            ArenaMax = arenaMax;
            HealthMultiplier = healthMult;
            DpsMultiplier = dpsMult;
        }

        /// <summary>The eight room bosses: two per family. A dungeon never repeats one.</summary>
        public static readonly IReadOnlyList<BossArchetype> DefaultPool = new[]
        {
            new BossArchetype("Ashen Colossus", BossFamily.SpaceDenial,
                "Every slam leaves lava. The room gets smaller until somebody dies.",
                new[] { "Ground Slam: telegraphed cone, leaves a lava pool that lasts the whole fight", "Ember Shell: takes 50% less damage while standing in its own lava", "Collapse: at 30% health the outer ring of floor becomes lava" },
                "Ranged teams kite along the walls; a Warden can tank on the last island. Melee-heavy trios run out of floor.",
                "Arriving late is an advantage: the room is already small and the first team is pinned on the remaining floor.",
                11, 13, new DamageProfile((DamageType.Fire, 0.7f), (DamageType.Physical, 0.3f)), "Ember Wisp", 1.0f, 0.9f),
            new BossArchetype("Glasswright", BossFamily.SpaceDenial,
                "Raises crystal walls that cut the room into pieces and the team into pieces.",
                new[] { "Partition: raises a crystal wall across the room for 10 s, splitting whoever is on either side", "Shardstorm: hits everything with line of sight; the walls are cover", "Refract: beam that bounces off its own walls" },
                "A Reaver and Mender on the same side survive; a Mender alone does not. Firewall through a crystal gap is a free kill.",
                "Walls block sightlines for arriving teams too; a third party has to commit to a side blind.",
                11, 13, new DamageProfile((DamageType.Lightning, 0.5f), (DamageType.Physical, 0.5f)), "Storm Acolyte", 1.0f, 1.0f),
            new BossArchetype("Hive Matron", BossFamily.Summoner,
                "Skitterers keep coming. The swarm is the boss; she is just the clock.",
                new[] { "Brood: spawns 2 skitterers every 8 s for as long as she lives", "Pheromone: marks a player; skitterers chase the mark", "Molt: at 50% she sheds adds and gains speed" },
                "Burn and Piercing Shot clear adds; a Warden holds the doorway so adds come one at a time. A trio of single-target damage drowns.",
                "Adds pour into the corridor. A second team walking in meets the swarm before the fight.",
                9, 12, new DamageProfile((DamageType.Poison, 0.6f), (DamageType.Physical, 0.4f)), "Skitterer", 0.85f, 0.8f),
            new BossArchetype("Bone Choir", BossFamily.Summoner,
                "Raises the dead. Including yours.",
                new[] { "Choir: raises every corpse in the room as an add, players included, with their gear's stats", "Dirge: channel that heals the boss for each add alive; interruptible", "Requiem: at 25% raises everything at once" },
                "Burn corpses before they rise (fire), interrupt Dirge (Warden Slam, Shade Hamstring, Stormcaller Arc). Executes on adds keep Reaver fed.",
                "A dead player's corpse is loot for the winners and an add for the boss: whoever killed them has to decide which matters first.",
                10, 13, new DamageProfile((DamageType.Shadow, 0.7f), (DamageType.Physical, 0.3f)), "Gravewalker", 1.0f, 0.9f),
            new BossArchetype("Hound of Vesh", BossFamily.Chaser,
                "Does not stay in its room. Picks the weakest and runs them down.",
                new[] { "Scent: targets the lowest-health player and pursues into corridors for 8 s, then leashes back", "Savage: bleeds on hit, stacks", "Howl: slows everything in the corridor it is in" },
                "Mender Purge strips bleeds; Marksman Vault Back buys the leash timer; a Warden taunt ends the chase instantly; a Cryomancer's Chill makes it a joke.",
                "Pull it toward the neighbours. A Hound dragged into another team's extract fight is the oldest play in the book.",
                9, 11, DamageProfile.Pure(DamageType.Physical), "Bandit", 0.9f, 1.1f),
            new BossArchetype("Lantern Wraith", BossFamily.Chaser,
                "Hunts whoever carries the most. Full loot works both ways.",
                new[] { "Covet: targets the player with the highest-value inventory, anywhere within 2 rooms", "Snuff: shadow pulse that blinds for 2 s", "Drift: passes through doors, cannot be body-blocked" },
                "Spread loot across the team before engaging. Shade Veil breaks Covet; fire light shrinks its blind.",
                "It will leave your fight to chase a loaded team passing by. The richest team in the quarter is its target, not you.",
                9, 11, new DamageProfile((DamageType.Shadow, 0.5f), (DamageType.Frost, 0.5f)), "Frostbound", 0.9f, 1.0f),
            new BossArchetype("Iron Juggernaut", BossFamily.Check,
                "Tank check. Hits one thing, very hard, on a timer you can read.",
                new[] { "Crush: 3 s telegraphed single-target hit for 60% of a baseline player's health", "Trample: charges the furthest player; a wall stops it and stuns it", "Overdrive: interruptible cast that doubles attack speed for 10 s" },
                "A Warden with Shield Wall eats Crush; without one the team rotates Crush between players and heals through. Someone must hold the interrupt.",
                "Predictable timers mean a watching team knows exactly when the fighting team is weakest.",
                10, 13, new DamageProfile((DamageType.Physical, 0.8f), (DamageType.Lightning, 0.2f)), "Hollow Knight", 1.2f, 1.0f),
            new BossArchetype("Plague Herald", BossFamily.Check,
                "Healer check. Nothing it does is fast; all of it stacks.",
                new[] { "Blight: stacking damage-over-time applied on every hit, never expires on its own", "Miasma: cloud that doubles Blight ticks inside it", "Outbreak: at 40% spreads every stack to the nearest ally" },
                "Mender Purge is the whole fight; without a Mender the team must burst it before stacks win. Burn is irrelevant; Reaver Execute is the race.",
                "Blight follows you out of the room. A team that wins and leaves is still ticking down when the next fight starts.",
                9, 12, DamageProfile.Pure(DamageType.Poison), "Carrion Crawler", 0.9f, 0.9f),
        };

        /// <summary>The center boss. Not part of the pool; appears once in every dungeon.</summary>
        public static readonly BossArchetype FinalBoss = new BossArchetype("The Sovereign Below", BossFamily.Check,
            "Three phases, one for each thing a full party has to prove.",
            new[] { "Phase 1 (100-70%): Juggernaut-pattern single hits and a trample; tank check", "Phase 2 (70-35%): the floor floods from the edges, adds rise from the water; space and add control", "Phase 3 (35-0%): stacking Blight plus a 90 s enrage; healer and damage check", "On death: the gated exit opens at the center of the arena" },
            "Needs all three roles. A geared Warden / Reaver / Mender is the reference kill; two geared players cannot.",
            "Every team can reach it on equal terms and everyone knows the exit opens when it dies. The last fight of the round is always here.",
            12, 14, new DamageProfile((DamageType.Physical, 0.4f), (DamageType.Frost, 0.3f), (DamageType.Poison, 0.3f)));
    }
}
