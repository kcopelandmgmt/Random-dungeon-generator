using System;
using System.Diagnostics;
using System.Linq;
using DungeonGen.Core;

namespace DungeonGen.Tools
{
    /// <summary>
    /// Headless preview: dotnet run -- --seed 42 --width 80 --height 60 --rooms 16 --loops 0.2
    /// Add --batch 500 to generate many seeds and report how often validation needed retries.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var settings = new DungeonSettings();
            int seed = Environment.TickCount;
            int batch = 0;
            bool legend = true;
            string command = null; int rollN = 0, rollLevel = 0;

            // --teams applies a whole preset, so it must be read before any individual overrides; --tier stamps the preset.
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "--teams") settings = DungeonSettings.ForTeams(int.Parse(args[i + 1]));
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "--tier") DungeonTier.For(int.Parse(args[i + 1])).Apply(settings);
            int viewTeam = -1; string simMode = "duels"; int simTier = 2, simSeeds = 40, simTeams = 4, simSize = 3;

            for (int i = 0; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
                switch (args[i])
                {
                    case "--teams": Next(); break;
                    case "--tier": Next(); break;
                    case "--view": viewTeam = int.Parse(Next()); command = "--view"; break;
                    case "--sim": simMode = Next(); command = "--sim"; break;
                    case "--sim-tier": simTier = int.Parse(Next()); break;
                    case "--sim-seeds": simSeeds = int.Parse(Next()); break;
                    case "--sim-teams": simTeams = Math.Max(2, Math.Min(8, int.Parse(Next()))); break;
                    case "--sim-size": simSize = Math.Max(1, int.Parse(Next())); break;
                    case "--no-interiors": settings.InteriorsEnabled = false; break;
                    case "--no-hazards": settings.HazardsEnabled = false; break;
                    case "--ring": settings.SpawnPlacement = SpawnPlacement.Ring; break;
                    case "--fringe": settings.SpawnPlacement = SpawnPlacement.Fringe; break;
                    case "--candidates": settings.CandidateCount = int.Parse(Next()); break;
                    case "--bosses": settings.BossCount = int.Parse(Next()); break;
                    case "--vaults": settings.VaultCount = int.Parse(Next()); break;
                    case "--fights": command = "--fights"; break;
                    case "--stats": command = "--stats"; break;
                    case "--roll": rollN = int.Parse(Next()); rollLevel = int.Parse(Next()); command = "--roll"; break;
                    case "--loot": command = "--loot"; break;
                    case "--wear": command = "--wear"; break;
                    case "--lifetime": command = "--lifetime"; break;
                    case "--craft": command = "--craft"; break;
                    case "--net": command = "--net"; break;
                    case "--seed": seed = int.Parse(Next()); break;
                    case "--width": settings.Width = int.Parse(Next()); break;
                    case "--height": settings.Height = int.Parse(Next()); break;
                    case "--rooms": settings.TargetRoomCount = int.Parse(Next()); break;
                    case "--loops": settings.LoopChance = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--spawns": settings.SpawnCount = int.Parse(Next()); break;
                    case "--extracts": settings.ExtractCount = int.Parse(Next()); break;
                    case "--corridor-width": settings.CorridorWidth = int.Parse(Next()); break;
                    case "--batch": batch = int.Parse(Next()); break;
                    case "--no-legend": legend = false; break;
                    case "--help":
                        Console.WriteLine("--teams N (preset) --seed N --width N --height N --rooms N --loops 0..1 --spawns N --extracts N --ring|--fringe --candidates N --bosses N --vaults N --corridor-width 1-4 --batch N --no-legend --fights --stats --roll N LEVEL --loot --wear --lifetime --craft --net --tier 1..3 --view TEAM --no-interiors --no-hazards --sim duels|teams|boss|bosses|packs|trace|sovtrace (final boss damage timeline; PARTY=a,b,c env) |round (round also prints the FINAL BOSS attempt report; env FB_RUSH=0,2 sets those teams down at the final boss door, FB_EAGER=1 counts every squad as having beaten a room boss, FBTRACE=1 prints a ten-second timeline of each fight and every down, FB_DOOR=1 why squads at the door did not go in) [--sim-tier N --sim-seeds N --sim-teams 2..8 --sim-size N (round only)]");
                        return 0;
                    default:
                        Console.Error.WriteLine("Unknown argument " + args[i]);
                        return 1;
                }
            }

            switch (command)
            {
                case "--fights": return PrintFightTable(settings);
                case "--stats": return PrintStats(settings);
                case "--wear": return WearRolledSet(seed);
                case "--lifetime": return PrintLifetimes(seed);
                case "--craft": return CraftDemo(settings, seed);
                case "--net": return NetDemo(settings, seed);
                case "--view": return ViewDemo(settings, seed, viewTeam);
                case "--sim": return SimDemo(simMode, simTier, simSeeds, seed, simTeams, simSize);
                case "--roll": return RollItems(rollN, rollLevel, seed);
                case "--loot":
                    {
                        var l = DungeonGenerator.Generate(settings, seed);
                        foreach (var r in l.Rooms.Where(r => r.Loot.Count > 0).OrderBy(r => r.LootTier))
                        {
                            Console.WriteLine($"room {r.Id} {r.Type} tier {r.LootTier}:");
                            foreach (var it in r.Loot) Console.WriteLine("  " + it);
                        }
                        return 0;
                    }
            }
            if (batch > 0) return RunBatch(settings, seed, batch);

            var sw = Stopwatch.StartNew();
            var layout = DungeonGenerator.Generate(settings, seed);
            sw.Stop();
            Console.Write(AsciiRenderer.Render(layout, legend));
            Console.WriteLine($"Generated in {sw.Elapsed.TotalMilliseconds:0.0} ms");
            return 0;
        }

        /// <summary>How long gear lasts under the design's reference round, and what a death does to a kit.</summary>
        private static int PrintLifetimes(int seed)
        {
            const float taken = 2500f, dealt = 6000f;   // a reference round: two mob packs, one boss, one skirmish
            Console.WriteLine($"Reference round: {taken:0} damage taken, {dealt:0} dealt. Rounds an item survives in constant use (no repairs):");
            Console.WriteLine("  rarity   armour  weapon  jewellery");
            foreach (DungeonGen.Core.Items.Rarity r in Enum.GetValues(typeof(DungeonGen.Core.Items.Rarity)))
            {
                var armour = new DungeonGen.Core.Items.Item(DungeonGen.Core.Items.ItemBase.Armours[1], 3, r, "x", new System.Collections.Generic.List<DungeonGen.Core.Items.AffixRoll>(), new System.Collections.Generic.List<DungeonGen.Core.Items.AffixRoll>());
                var weapon = new DungeonGen.Core.Items.Item(DungeonGen.Core.Items.ItemBase.Weapons[0], 3, r, "x", new System.Collections.Generic.List<DungeonGen.Core.Items.AffixRoll>(), new System.Collections.Generic.List<DungeonGen.Core.Items.AffixRoll>());
                var ring = new DungeonGen.Core.Items.Item(DungeonGen.Core.Items.ItemBase.Jewellery[1], 3, r, "x", new System.Collections.Generic.List<DungeonGen.Core.Items.AffixRoll>(), new System.Collections.Generic.List<DungeonGen.Core.Items.AffixRoll>());
                float max = DungeonGen.Core.Items.DurabilityRules.MaxFor(r);
                float aw = DungeonGen.Core.Items.DurabilityRules.BaseWearPerRound + taken / DungeonGen.Core.Items.DurabilityRules.ArmourDamagePerPoint;
                float ww = DungeonGen.Core.Items.DurabilityRules.BaseWearPerRound + dealt / DungeonGen.Core.Items.DurabilityRules.WeaponDamagePerPoint;
                float jw = DungeonGen.Core.Items.DurabilityRules.BaseWearPerRound + DungeonGen.Core.Items.DurabilityRules.JewelleryWearPerRound;
                Console.WriteLine($"  {r,-8} {max / aw,6:0.0}  {max / ww,6:0.0}  {max / jw,9:0.0}");
            }
            Console.WriteLine();
            var rng = new DungeonRandom(seed);
            var c = DungeonGen.Core.Combat.ClassArchetype.DefaultRoster[1];
            var kit = DungeonGen.Core.Items.Equipment.StandardSet(c.Armour, 2, c.Primary);
            Console.WriteLine("A Reaver's nine-piece kit over rounds (no deaths):");
            for (int round = 1; round <= 5; round++)
            {
                DungeonGen.Core.Items.DurabilityRules.WearForRound(kit, taken, dealt);
                var st = DungeonGen.Core.Combat.PlayerStats.For(c, kit);
                Console.WriteLine($"  after round {round}: " + string.Join(", ", kit.Items.Select(i => $"{i.Base.Slot} {i.Condition}")) + $"  → HP {st.Health:0} DPS {st.Dps:0} armour {st.ArmourRating:0}");
            }
            var (dropped, destroyed) = DungeonGen.Core.Items.DurabilityRules.OnDeath(kit, rng);
            Console.WriteLine($"Then a death: {dropped.Count} items drop for the killer, {destroyed.Count} are trashed.");
            return 0;
        }

        /// <summary>Salvage a whole dungeon's loot, show the bag, then craft a few things from it.</summary>
        private static int CraftDemo(DungeonSettings settings, int seed)
        {
            var layout = DungeonGenerator.Generate(settings, seed);
            var bag = new DungeonGen.Core.Items.MaterialBag();
            float silver = 0f;
            foreach (var it in layout.Items)
            {
                if (it.Rarity == DungeonGen.Core.Items.Rarity.Normal) silver += DungeonGen.Core.Items.CraftingRules.SellPrice(it);   // sell the plain stuff
                else bag.Add(DungeonGen.Core.Items.CraftingRules.Salvage(it));                                                   // salvage the rest
            }
            Console.WriteLine($"Salvaged every Magic+ item in seed {seed} ({layout.Items.Count} items), sold the Normals for {silver:0} silver.");
            Console.WriteLine("Bag: " + bag);
            Console.WriteLine();
            var rng = new DungeonRandom(seed);
            var requests = new[]
            {
                new DungeonGen.Core.Items.CraftRequest(DungeonGen.Core.Items.ItemBase.Weapons[0], 3),
                new DungeonGen.Core.Items.CraftRequest(DungeonGen.Core.Items.ItemBase.Armours[1], 3, new[] { "Health" }),
                new DungeonGen.Core.Items.CraftRequest(DungeonGen.Core.Items.ItemBase.Jewellery[1], 4, new[] { "Resist:Fire", "Attribute:Strength" }, forceRare: true),
                new DungeonGen.Core.Items.CraftRequest(DungeonGen.Core.Items.ItemBase.Weapons[6], 4, new[] { "IncreasedDamage" }, forceRare: true),
            };
            foreach (var r in requests)
            {
                string want = $"{r.Base.Name} L{r.Level}" + (r.GuaranteedFamilies.Count > 0 ? " pin " + string.Join("+", r.GuaranteedFamilies) : "") + (r.ForceRare ? " RARE" : "");
                if (!r.Affordable(bag, out string why)) { Console.WriteLine($"  {want}: cannot, {why}"); continue; }
                var item = DungeonGen.Core.Items.Crafter.Craft(r, bag, rng);
                Console.WriteLine($"  {want} (cost {r.ScrapCost} scrap, {r.ShardCost} shards)");
                Console.WriteLine($"    → {item}");
            }
            Console.WriteLine("Bag after: " + bag);
            return 0;
        }

        /// <summary>Server builds a match, a client rebuilds from the descriptor, fingerprints compared, events replayed.</summary>
        private static int NetDemo(DungeonSettings settings, int seed)
        {
            var sw = Stopwatch.StartNew();
            var d = DungeonGen.Core.Net.MatchDescriptor.Create(seed, settings, new DungeonGen.Core.Net.MatchRules(), out var serverLayout);
            var bytes = d.ToBytes();
            Console.WriteLine($"SERVER  seed {seed}: generated in {sw.Elapsed.TotalMilliseconds:0} ms; descriptor {bytes.Length} bytes; settings hash {d.SettingsHash:x16}; fingerprint {d.ExpectedFingerprint:x16}");
            Console.WriteLine($"        teams → spawn rooms: {string.Join(", ", d.TeamSpawnRooms.Select((r, t) => $"T{t}→room {r}"))}");
            sw.Restart();
            var clientDesc = DungeonGen.Core.Net.MatchDescriptor.FromBytes(bytes);
            var clientLayout = clientDesc.BuildAndVerify();
            Console.WriteLine($"CLIENT  rebuilt and verified in {sw.Elapsed.TotalMilliseconds:0} ms; fingerprint {DungeonGen.Core.Net.LayoutFingerprint.Compute(clientLayout):x16} ✓");
            var view = DungeonGen.Core.Net.ClientView.Redact(serverLayout);
            Console.WriteLine($"        client view: {view.Items.Count} items visible (server has {serverLayout.Items.Count}); public fingerprint {DungeonGen.Core.Net.ClientView.PublicFingerprint(view):x16} == server public {DungeonGen.Core.Net.ClientView.PublicFingerprint(serverLayout):x16}");

            var state = new DungeonGen.Core.Net.MatchState(d, serverLayout);
            var idx = state.Index;
            var vault = serverLayout.VaultRooms.First();
            var key = serverLayout.Keys.First(k => k.VaultRoomId == vault.Id);
            var firstItem = idx.Items.First(kv => idx.ItemRoom[kv.Key] == vault.Id).Key;
            void Try(DungeonGen.Core.Net.MatchEvent e) { bool ok = state.Apply(e, out string why); Console.WriteLine($"  {(ok ? "ok      " : "REJECT  ")}{e}{(ok ? "" : "  ← " + why)}"); }
            Console.WriteLine("EVENTS (server validates, clients mirror):");
            Try(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.RoundStarted, 0));
            Try(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.PlayerSpawned, 0, 1, 0));
            Try(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.PlayerSpawned, 0, 2, 1));
            Try(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.DoorsOpened, 10));
            Try(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.PlayerExtracted, 30, 1, serverLayout.ExtractRooms.First().Id, 0));
            Try(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.VaultOpened, 60, vault.Id, 1));
            Try(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.KeyPickedUp, 90, key.KeyId, 1));
            if (key.DroppedByBoss) Try(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.BossKilled, 95, key.HolderRoomId, 0));
            Try(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.LootStarted, 99, 1, 0, 2));
            Try(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.KeyPickedUp, 100, key.KeyId, 1));
            Try(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.VaultOpened, 160, vault.Id, 1));
            Try(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.ItemTaken, 161, firstItem, 1));
            Try(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.ContainerOpened, 162, vault.Id, 1));
            Try(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.LootStarted, 162, 1, vault.Id, 0));
            Try(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.ContainerOpened, 164, vault.Id, 1));
            Try(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.ItemTaken, 165, firstItem, 1));
            Try(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.ItemTaken, 166, firstItem, 2));
            Try(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.ExtractsOpened, 240));
            Try(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.ExtractStarted, 290, 1, serverLayout.ExtractRooms.First().Id));
            Try(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.PlayerExtracted, 293, 1, serverLayout.ExtractRooms.First().Id, 0));
            Try(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.PlayerExtracted, 300, 1, serverLayout.ExtractRooms.First().Id, 0));
            Try(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.PlayerExtracted, 310, 2, serverLayout.FinalBossRoom.Id, 1));
            Try(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.BossKilled, 400, serverLayout.FinalBossRoom.Id, 1));
            Try(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.BossExitOpened, 401));
            Try(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.LootStarted, 401, 2, serverLayout.FinalBossRoom.Id, 0));
            Try(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.ContainerOpened, 403, serverLayout.FinalBossRoom.Id, 2));
            Console.WriteLine($"  player 2 carries {state.SoulsOf(2)} soul(s): the boss's soul was in its chest, and whoever opens it first takes it");
            Try(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.ExtractStarted, 402, 2, serverLayout.FinalBossRoom.Id));
            Try(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.PlayerExtracted, 410, 2, serverLayout.FinalBossRoom.Id, 1));
            var logBytes = state.LogToBytes();
            var replayed = DungeonGen.Core.Net.MatchState.Replay(d, serverLayout, DungeonGen.Core.Net.MatchState.LogFromBytes(logBytes));
            Console.WriteLine($"SERVER REPLAY {replayed.Log.Count} events from {logBytes.Length} bytes: {replayed.ExtractedPlayers.Count} extracted, vault open: {replayed.OpenVaults.Count > 0}, boss exit open: {replayed.BossExitOpen}. The log never leaves the server.");
            foreach (int team in new[] { 0, 1 })
            {
                var tv = DungeonGen.Core.Net.TeamView.For(state, team);
                var mirror = new DungeonGen.Core.Net.MatchState(clientDesc, clientLayout);
                mirror.LoadView(DungeonGen.Core.Net.TeamView.FromBytes(tv.ToBytes()));
                Console.WriteLine($"TEAM {team} VIEW {tv.ToBytes().Length} bytes: {tv.Members.Count} member(s), {tv.Explored.Count} explored room(s), {tv.Items.Count} item(s) visible, {tv.Corpses.Count} corpse(s); mirror agrees: explored {mirror.Explored[team].Count}, items {mirror.KnownItems.Count}");
            }
            var near = idx.RoomsWithin(serverLayout.SpawnRooms.First().Id, 2);
            Console.WriteLine($"INTEREST: a player in spawn room {serverLayout.SpawnRooms.First().Id} needs updates from {near.Count} of {serverLayout.Rooms.Count} rooms (2 hops).");
            return 0;
        }

        /// <summary>
        /// Lab switch FB_RUSH=0,2: once the doors open, the listed teams are set down in the corridor at the final boss's door with the squad's goal set to it and room bosses counted
        /// as beaten, so the natural sim's few attempts (squads rarely get there) become enough fights to read a kill rate from. Everything else is the normal bot loop.
        /// </summary>
        private static void RushFinalBoss(DungeonGen.Core.Net.MatchServer sv, System.Collections.Generic.List<int> teams)
        {
            bool done = false;
            sv.Ticked += srv =>
            {
                if (done || !srv.State.DoorsOpen) return;
                done = true;
                var room = srv.Layout.FinalBossRoom; if (room == null) return;
                var door = room.Doors.First();
                var outside = DungeonGen.Core.GridPoint.Cardinals.Select(c => door + c).FirstOrDefault(p => !room.Bounds.Contains(p) && srv.Layout.IsWalkable(p));
                int n = 0;
                foreach (var bb in srv.BotBrains)
                {
                    if (!(bb is DungeonGen.Core.Net.RoundBot rb) || !teams.Contains(rb.Squad.Team)) continue;
                    var c = srv.Sim.Get(rb.PlayerId); if (c == null) continue;
                    c.Position = DungeonGen.Core.Combat.Vec2.Of(outside) + new DungeonGen.Core.Combat.Vec2((n % 3) * 0.1f, 0f); n++;
                    rb.Squad.BossesKilled = Math.Max(1, rb.Squad.BossesKilled); rb.Squad.GoalRoom = room.Id; rb.Squad.Resting = false;
                }
            };
        }

        /// <summary>The final boss across a batch of rounds: engagements, serious attempts (a full team of 3 in the arena for 20 s or more), kills, wipes, time to kill.</summary>
        private static void PrintFinalBossReport(System.Collections.Generic.List<DungeonGen.Core.Net.RoundReport> reports, System.Collections.Generic.List<FinalBossWatcher> watchers)
        {
            var all = watchers.SelectMany(w => w.Attempts).ToList();
            var serious = all.Where(a => a.Serious).ToList();
            int kills = serious.Count(a => a.Kill), wipes = serious.Count(a => a.Wipe && !a.Kill), retreats = serious.Count - kills - wipes;
            float Pct(int n, int d) => 100f * n / Math.Max(1, d);
            Console.WriteLine($"FINAL BOSS: rounds with any engagement {watchers.Count(w => w.Attempts.Count > 0)}/{watchers.Count}; team engagements {all.Count}; serious attempts {serious.Count} (full team of 3, >= 20 s) in {serious.Select(a => a.Seed).Distinct().Count()} rounds");
            Console.WriteLine($"  serious: kills {kills} ({Pct(kills, serious.Count):0}%), wipes {wipes} ({Pct(wipes, serious.Count):0}%), retreats {retreats} ({Pct(retreats, serious.Count):0}%); final boss killed in {watchers.Count(w => w.BossKilled)}/{watchers.Count} rounds");
            var ttk = serious.Where(a => a.Kill).Select(a => a.FightLength).ToList();
            if (ttk.Count > 0) Console.WriteLine($"  time to kill (engage to death): mean {ttk.Average():0} s, min {ttk.Min():0}, max {ttk.Max():0}");
            var nk = serious.Where(a => !a.Kill).ToList();
            if (nk.Count > 0) Console.WriteLine($"  non-kill attempts: mean boss health left {nk.Average(a => a.BossHealthAtEnd) * 100f:0}%, mean best {nk.Average(a => a.MinBossHealth) * 100f:0}%, mean length {nk.Average(a => a.FightLength):0} s; of all serious: enraged {serious.Count(a => a.Enraged)}, reached phase 2 {serious.Count(a => a.PhaseReached >= 2)}, phase 3 {serious.Count(a => a.PhaseReached >= 3)}");
            foreach (var a in all.OrderBy(a => a.Seed).ThenBy(a => a.FightStart)) Console.WriteLine($"  seed {a.Seed,5} T{a.Team} {(a.Serious ? "SERIOUS" : "minor  ")} {a.Outcome,-7} start {a.FightStart,5:0}s len {a.FightLength,4:0}s present {a.Present,4:0}s dealt {a.DamageToBoss,6:0} peak {a.PeakInArena} boss left {a.BossHealthAtEnd * 100f,3:0}% (best {a.MinBossHealth * 100f:0}%) phase {a.PhaseReached}{(a.Enraged ? " ENRAGED" : "")}");
        }

        /// <summary>--sim duels|teams|boss|trace: run the fight lab and print win rates, times and, for trace, one fight blow by blow.</summary>
        private static int SimDemo(string mode, int tier, int seeds, int seed, int simTeams = 4, int simSize = 3)
        {
            var specs = DungeonGen.Core.Combat.WeaponSpec.All.Select(w => w.Name).Concat(DungeonGen.Core.Combat.ElementKit.All.Select(k => k.Name)).ToList();
            switch (mode)
            {
                case "round":
                    {
                        Console.WriteLine($"ROUNDS, {simTeams} teams of {simSize} bots, gear tier {tier}, {seeds} seeds. The real server loop, headless; a rejected event is a bug.");
if (Environment.GetEnvironmentVariable("BOTLOG") == "1") DungeonGen.Core.Net.RoundBot.DebugLog = Console.WriteLine;   // squad decisions: commits, drops, leaves
                        var reports = new System.Collections.Generic.List<DungeonGen.Core.Net.RoundReport>();
                        var watchers = new System.Collections.Generic.List<FinalBossWatcher>();
                        for (int sd = seed; sd < seed + seeds; sd++)
                        {
                            var watcher = new FinalBossWatcher(sd); watchers.Add(watcher);
                            reports.Add(DungeonGen.Core.Net.RoundLab.Play(sd, simTeams, simSize, tier, configure: sv =>
                            {
                                watcher.Attach(sv);
                                if (Environment.GetEnvironmentVariable("FB_RUSH") is string rush && rush.Length > 0) RushFinalBoss(sv, rush.Split(',').Select(int.Parse).ToList());
                                if (Environment.GetEnvironmentVariable("FB_EAGER") == "1") foreach (var bb in sv.BotBrains) if (bb is DungeonGen.Core.Net.RoundBot eb) eb.Squad.BossesKilled = Math.Max(1, eb.Squad.BossesKilled);   // lab switch: every squad counts as having beaten a room boss, so many more reach the final boss
                                if (Environment.GetEnvironmentVariable("FBTRACE") == "1") watcher.Trace = Console.WriteLine;
                                if (Environment.GetEnvironmentVariable("BOTDUMP") == null) return;
                                float at = float.Parse(Environment.GetEnvironmentVariable("BOTDUMP"), System.Globalization.CultureInfo.InvariantCulture); bool done = false;
                                sv.Ticked += srv => { if (done || srv.Elapsed < at) return; done = true; foreach (var b in srv.BotBrains) if (b is DungeonGen.Core.Net.RoundBot rb) Console.WriteLine("DUMP " + rb.Describe(srv)); };
                            }));
                        }
                        foreach (var r in reports) { Console.WriteLine(r); foreach (var rej in r.Rejections.Take(5)) Console.WriteLine("    " + rej); }
                        {
                            var withEvent = reports.Where(r => r.EventKind != DungeonGen.Core.Net.DungeonEventKind.None).ToList();
                            string Line(DungeonGen.Core.Net.DungeonEventKind k) { var g = withEvent.Where(r => r.EventKind == k).ToList(); return $"{k} {g.Count} rounds, {g.Count(r => r.EventEngaged)} engaged, {g.Count(r => r.EventDone)} seen through" + (k == DungeonGen.Core.Net.DungeonEventKind.RoamingElite ? $" ({g.Count(r => r.RelicTaken)} relics taken)" : ""); }
                            Console.WriteLine($"EVENTS: {withEvent.Count} of {reports.Count} rounds had one: {Line(DungeonGen.Core.Net.DungeonEventKind.RoamingElite)}; {Line(DungeonGen.Core.Net.DungeonEventKind.VaultUnsealed)}; {Line(DungeonGen.Core.Net.DungeonEventKind.CheapShrine)}");
                        }
                        int players = reports.Sum(r => r.Players);
                        Console.WriteLine($"extracted {reports.Sum(r => r.Extracted)}/{players} ({100f * reports.Sum(r => r.Extracted) / Math.Max(1, players):0}%), died {reports.Sum(r => r.Died)}, still inside at the end {reports.Sum(r => r.StillInside)}, player kills {reports.Sum(r => r.PlayerKills)}, bosses {reports.Sum(r => r.BossKills)}, final bosses {reports.Count(r => r.FinalBossKilled)}, items out {reports.Sum(r => r.ItemsExtracted)}, souls {reports.Sum(r => r.Souls)}, rejected events {reports.Sum(r => r.Rejected)}, mean round {reports.Average(r => r.Seconds):0} s");
                        {
                            var deaths = reports.SelectMany(r => r.DeathCauses).ToList();
                            int Kind(string prefix) => deaths.Count(d => d.Split('|')[1].Trim().StartsWith(prefix));
                            int pvp = Kind("player"), boss = Kind("boss"), mob = Kind("mob"), other = deaths.Count - pvp - boss - mob;
                            float Pct(int n) => 100f * n / Math.Max(1, deaths.Count);
                            Console.WriteLine($"SUMMARY: extraction {100f * reports.Sum(r => r.Extracted) / Math.Max(1, players):0.0}%  deaths {deaths.Count} = pvp {pvp} ({Pct(pvp):0}%) / boss {boss} ({Pct(boss):0}%) / mobs {mob} ({Pct(mob):0}%) / hazard-clock-other {other} ({Pct(other):0}%)  room bosses killed {reports.Sum(r => r.BossKills)} ({reports.Average(r => r.BossKills):0.00} per round)  final boss killed {reports.Count(r => r.FinalBossKilled)}/{reports.Count}  mean round {reports.Average(r => r.Seconds):0} s ({reports.Average(r => r.Seconds) / 60f:0.0} min)  rejected {reports.Sum(r => r.Rejected)}");
                        }
                        PrintFinalBossReport(reports, watchers);
                        {
                            var byMinute = reports.SelectMany(r => r.DeathCauses.Select((d, i) => (d, t: r.DeathTimes[i]))).GroupBy(x => (int)(x.t / 60f)).OrderBy(g => g.Key);
                            Console.WriteLine("DEATHS PER MINUTE OF THE ROUND (pvp / other): " + string.Join("  ", byMinute.Select(g => $"{g.Key}m {g.Count(x => x.d.Split('|')[1].Trim().StartsWith("player"))}/{g.Count(x => !x.d.Split('|')[1].Trim().StartsWith("player"))}")));
                            var inRoom = reports.SelectMany(r => r.DeathCauses).GroupBy(d => d.Split('|')[2].Trim()).OrderByDescending(g => g.Count());
                            Console.WriteLine("DEATHS BY ROOM TYPE: " + string.Join("  ", inRoom.Select(g => $"{g.Key} {g.Count()}")));
                        }
if (Environment.GetEnvironmentVariable("BOTLOG") == "1") foreach (var r in reports) for (int i = 0; i < r.DeathCauses.Count; i++) Console.WriteLine($"DEATH seed {r.Seed} {r.DeathTimes[i]:0}s {r.DeathCauses[i]}");
                        Console.WriteLine("DEATHS BY CAUSE:");
                        foreach (var g in reports.SelectMany(r => r.DeathCauses).GroupBy(d => d).OrderByDescending(g => g.Count()).Take(25)) Console.WriteLine($"  {g.Count(),4}  {g.Key}");
                        return reports.Sum(r => r.Rejected) == 0 ? 0 : 3;
                    }
                case "roundtrace":
                    {
                        Console.WriteLine($"ROUND TRACE seed {seed}, 4 teams of 3 bots, gear tier {tier}: every accepted event, then every rejection and death.");
                        var deaths = new System.Collections.Generic.List<string>();
                        DungeonGen.Core.Net.MatchServer srv = null;
                        var r = DungeonGen.Core.Net.RoundLab.Play(seed, 4, 3, tier, configure: sv =>
                        {
                            srv = sv;
                            bool dumped = false;
                            sv.EventRejected += (e, why) =>
                            {
                                if (dumped) return; dumped = true;
                                Console.WriteLine($"  FIRST REJECTION {e}: {why}");
                                var me = sv.Sim.Get(e.B);
                                Console.WriteLine($"    actor {e.B} at {me?.Position} room {sv.RoomOf(e.B)}");
                                foreach (var kv in sv.State.Corpses) { var body = sv.Sim.Get(kv.Key); Console.WriteLine($"    corpse {kv.Key} at {body?.Position} dist {(body == null || me == null ? -1 : DungeonGen.Core.Combat.Vec2.Distance(body.Position, me.Position)):0.00} opened {sv.State.OpenedCorpses.Contains(kv.Key)} items [{string.Join(",", kv.Value)}] owner-of-items: {string.Join(",", kv.Value.Select(i => sv.State.ItemOwner.TryGetValue(i, out int o) ? o.ToString() : "-"))}"); }
                            };
                            int diagTicks = 0;
                            sv.Ticked += srv2 =>
                            {
                                if (diagTicks >= 12) return;
                                var sim = srv2.Sim;
                                foreach (var c in sim.Combatants.Values.Where(o => !o.IsMob && o.Alive))
                                {
                                    var enemies = sim.Combatants.Values.Where(o => o.Team != c.Team && !o.Dead && !o.IsMob && DungeonGen.Core.Combat.Vec2.Distance(o.Position, c.Position) <= 12f && sim.CanSee(c.Position, o.Position)).ToList();
                                    if (enemies.Count == 0 || enemies.Any(o => !o.Downed)) continue;
                                    if ((srv2.Tick % 10) != 0) continue;
                                    diagTicks++;
                                    var t = enemies.OrderBy(o => DungeonGen.Core.Combat.Vec2.Distance(o.Position, c.Position)).First();
                                    var basic = c.Abilities.First(a => a.Kind == DungeonGen.Core.Combat.AbilityKind.Basic);
                                    var prev = sim.Preview(c.Id, basic.Id, t.Position).ToList();
                                    var zones = sim.Zones.Where(z => z.CasterTeam != c.Team && z.Ability.Damage > 0f && DungeonGen.Core.Combat.Vec2.Distance(z.Centre, c.Position) <= z.Ability.Size + 0.3f).Select(z => z.Ability.Id).ToList();
                                    Console.WriteLine($"  DIAG {sim.Time:0.0} {c.Spec} {c.Id} at {c.Position} hp {c.Health:0} canact {c.CanAct} target {t.Id} dist {DungeonGen.Core.Combat.Vec2.Distance(t.Position, c.Position):0.0} range {basic.RangeCells:0.0} sees {sim.CanSee(c.Position, t.Position)} preview [{string.Join(",", prev.Select(p => p.Id))}] cd {(c.CooldownUntil.TryGetValue(basic.Id, out float cd) ? cd - sim.Time : 0f):0.0} hostileZones [{string.Join(",", zones)}] channel {c.ChannelName} buffs [{string.Join(",", c.Buffs.Select(b => b.Kind))}] status hard {c.Status.IsHardControlled}");
                                }
                            };
                            sv.EventRaised += e =>
                            {
                                if (e.Kind == DungeonGen.Core.Net.MatchEventKind.RoomSeen || e.Kind == DungeonGen.Core.Net.MatchEventKind.PlayerEnteredRoom) return;
                                Console.WriteLine($"  {e}");
                                if (e.Kind == DungeonGen.Core.Net.MatchEventKind.PlayerDied || e.Kind == DungeonGen.Core.Net.MatchEventKind.PlayerFinished || e.Kind == DungeonGen.Core.Net.MatchEventKind.PlayerDowned)
                                {
                                    var killer = sv.Sim.Get(e.B); var victim = sv.Sim.Get(e.A);
                                    deaths.Add($"{e.Time,7:0.0}s {e.Kind} {victim?.Spec} {e.A} (T{victim?.Team}) by {(e.B < 0 ? "bleed-out/hazard" : killer == null ? e.B.ToString() : killer.IsMob ? killer.Name : killer.Spec + " " + e.B)} in room {e.C} {sv.Layout.Rooms[Math.Max(0, e.C)].Type}");
                                }
                            };
                        });
                        Console.WriteLine(r);
                        if (srv != null)
                        {
                            Console.WriteLine("SIM EVENTS: " + string.Join(", ", srv.Sim.Events.GroupBy(e => e.Kind).Select(g => $"{g.Key} {g.Count()}")));
                            var firstDown = srv.Sim.Events.FirstOrDefault(e => e.Kind == DungeonGen.Core.Combat.FightEventKind.Downed && !(srv.Sim.Get(e.Target)?.IsMob ?? true));
                            if (firstDown.Target != 0)
                            {
                                Console.WriteLine($"HITS ON {firstDown.Target} AFTER DOWN AT {firstDown.Time:0.0}:");
                                foreach (var h in srv.Sim.Events.Where(e => e.Target == firstDown.Target && e.Time >= firstDown.Time && e.Kind != DungeonGen.Core.Combat.FightEventKind.StatusApplied).Take(25)) Console.WriteLine($"   {h.Time,7:0.0} {h.Kind} from {h.Source} ({srv.Sim.Get(h.Source)?.Spec ?? srv.Sim.Get(h.Source)?.Name}) {h.Amount:0.0} {h.Detail}");
                                var v = srv.Sim.Get(firstDown.Target);
                                Console.WriteLine($"   victim {v}: finish damage {v.FinishDamage:0} of {v.MaxHealth * DungeonGen.Core.Combat.FightRules.FinishDamageFraction:0}");
                                foreach (var o in srv.Sim.Combatants.Values.Where(o => !o.IsMob && o.Team != v.Team && !o.Dead)) Console.WriteLine($"   enemy standing {o} dist {DungeonGen.Core.Combat.Vec2.Distance(o.Position, v.Position):0.0} sees {srv.Sim.CanSee(o.Position, v.Position)}");
                            }
                        }
                        if (srv != null)
                        {
                            Console.WriteLine("CAST REJECTIONS: " + string.Join(", ", srv.Sim.Events.Where(e => e.Kind == DungeonGen.Core.Combat.FightEventKind.Rejected).GroupBy(e => e.Detail).OrderByDescending(g => g.Count()).Take(8).Select(g => $"{g.Key} {g.Count()}")));
                            var lastDown = srv.Sim.Events.Where(e => e.Kind == DungeonGen.Core.Combat.FightEventKind.Downed && !(srv.Sim.Get(e.Target)?.IsMob ?? true)).Select(e => e.Time).DefaultIfEmpty(0f).Max();
                            Console.WriteLine($"WINNERS' ACTIONS AFTER THE LAST DOWN ({lastDown:0.0}s), first 30 non-status events by standing players:");
                            foreach (var e in srv.Sim.Events.Where(e => e.Time > lastDown && e.Kind != DungeonGen.Core.Combat.FightEventKind.StatusApplied && e.Kind != DungeonGen.Core.Combat.FightEventKind.Phase && (srv.Sim.Get(e.Source)?.IsMob == false)).Take(30))
                                Console.WriteLine($"   {e.Time,7:0.0} {e.Kind} {e.Source}({srv.Sim.Get(e.Source)?.Spec}) -> {e.Target}({srv.Sim.Get(e.Target)?.Spec ?? srv.Sim.Get(e.Target)?.Name}) {e.Amount:0.0} {e.Detail}");
                        }
                        if (srv != null) { Console.WriteLine("BOTS AT THE END:"); foreach (var b in srv.BotBrains.OfType<DungeonGen.Core.Net.RoundBot>()) Console.WriteLine("   " + b.Describe(srv)); }
                        Console.WriteLine("DEATHS:"); foreach (var d in deaths) Console.WriteLine("  " + d);
                        Console.WriteLine("REJECTIONS:"); foreach (var rej in r.Rejections.Take(20)) Console.WriteLine("  " + rej);
                        return 0;
                    }
                case "duels":
                    {
                        Console.WriteLine($"DUELS, gear tier {tier}, {seeds} seeds each. Row beats column; 0.50 is even. Draws at 120 s count for neither.");
                        Console.Write("              "); foreach (var b in specs) Console.Write($"{b.Substring(0, Math.Min(6, b.Length)),7}"); Console.WriteLine();
                        var overall = new System.Collections.Generic.Dictionary<string, float>();
                        foreach (var a in specs)
                        {
                            Console.Write($"{a,-14}"); float sum = 0; int n = 0;
                            foreach (var b in specs)
                            {
                                if (a == b) { Console.Write("      -"); continue; }
                                var (w, _, _) = DungeonGen.Core.Combat.FightLab.DuelSeries(a, b, tier, seeds);
                                Console.Write($"{w,7:0.00}"); sum += w; n++;
                            }
                            overall[a] = sum / n; Console.WriteLine($"   avg {sum / n:0.00}");
                        }
                        Console.WriteLine("Strongest: " + string.Join(", ", overall.OrderByDescending(kv => kv.Value).Take(3).Select(kv => $"{kv.Key} {kv.Value:0.00}")) + "; weakest: " + string.Join(", ", overall.OrderBy(kv => kv.Value).Take(3).Select(kv => $"{kv.Key} {kv.Value:0.00}")));
                        return 0;
                    }
                case "teams":
                    {
                        var trios = new[] { new[] { "Sentinel", "Sharpshooter", "Warder" }, new[] { "Breaker", "Berserker", "Plaguebearer" }, new[] { "Duelist", "Assassin", "Pyromancy" }, new[] { "Sentinel", "Volleyer", "Cryomancy" } };
                        Console.WriteLine($"TRIOS, gear tier {tier}, {seeds} seeds each.");
                        for (int i = 0; i < trios.Length; i++)
                            for (int j = i + 1; j < trios.Length; j++)
                            {
                                int a = 0, draws = 0; float secs = 0;
                                for (int s = 1; s <= seeds; s++) { var r = DungeonGen.Core.Combat.FightLab.TeamFight(trios[i], trios[j], tier, s); if (r.TimedOut) draws++; else if (r.WinnerTeam == 0) a++; secs += r.Seconds; }
                                Console.WriteLine($"  {string.Join("/", trios[i]),-36} vs {string.Join("/", trios[j]),-36} {a / (float)Math.Max(1, seeds - draws),5:0.00}  avg {secs / seeds,5:0.0}s  draws {draws}");
                            }
                        return 0;
                    }
                case "boss":
                    {
                        Console.WriteLine($"PARTY VS ROOM BOSS (tuned for 2.5 players at x1.4 gear), {seeds} seeds. The tuner promises a geared trio wins with margin and a duo is a coin flip.");
                        foreach (var (party, t) in new[] { (new[] { "Sentinel", "Berserker", "Warder" }, 2), (new[] { "Sentinel", "Berserker", "Warder" }, 0), (new[] { "Berserker", "Sharpshooter" }, 2), (new[] { "Berserker" }, 4), (new[] { "Sentinel", "Berserker", "Warder", "Volleyer" }, 1) })
                        {
                            int wins = 0; float secs = 0;
                            for (int s = 1; s <= seeds; s++) { var r = DungeonGen.Core.Combat.FightLab.PartyVsBoss(party, t, s); if (r.WinnerTeam == 0) wins++; secs += r.Seconds; }
                            Console.WriteLine($"  {string.Join("/", party),-40} tier {t}: win {wins / (float)seeds:0.00}, avg {secs / seeds:0.0}s");
                        }
                        return 0;
                    }
                case "bosses":
                    {
                        var party = new[] { "Sentinel", "Berserker", "Warder" };
                        Console.WriteLine($"TRIO {string.Join("/", party)} at gear tier {tier} vs each boss in its own room, with mechanics, {seeds} seeds.");
                        foreach (var arch in DungeonGen.Core.Combat.BossArchetype.DefaultPool.Select(a => a.Name).Concat(new[] { DungeonGen.Core.Combat.BossArchetype.FinalBoss.Name }))
                        {
                            int wins = 0, downs = 0; float secs = 0;
                            for (int s = 1; s <= seeds; s++) { var r = DungeonGen.Core.Combat.FightLab.BossFight(arch, party, tier, s); if (r.WinnerTeam == 0) wins++; secs += r.Seconds; downs += r.Downs; }
                            Console.WriteLine($"  {arch,-20} win {wins / (float)seeds:0.00}  avg {secs / seeds,5:0.0}s  downs/fight {downs / (float)seeds:0.0}");
                        }
                        return 0;
                    }
                case "sovtrace":
                    {
                        // The final boss alone with a trio in its real room: where the health goes, bucket by bucket (--seed picks the fight, --sim-tier the gear).
                        var party = (Environment.GetEnvironmentVariable("PARTY") ?? "Sentinel,Berserker,Warder").Split(',');
                        Console.WriteLine($"SOVEREIGN TRACE: {string.Join("/", party)} tier {tier} seed {seed}");
                        var rep = DungeonGen.Core.Combat.FightLab.BossFight(DungeonGen.Core.Combat.BossArchetype.FinalBoss.Name, party, tier, seed, 300f, (sim, dir) =>
                        {
                            var boss = dir.Boss.Boss;
                            Console.WriteLine($"  boss {boss.MaxHealth:0} hp, dps {boss.Dps:0.0}, enrage at {dir.Boss.Enraged}");
                            foreach (var c in sim.Combatants.Values.Where(c => !c.IsMob)) Console.WriteLine($"  {c}  dps {c.Dps:0}");
                            var ev = sim.Events;
                            for (int t0 = 0; t0 < (int)sim.Time + 1; t0 += 10)
                            {
                                float toBoss = ev.Where(e => e.Kind == DungeonGen.Core.Combat.FightEventKind.Hit && e.Target == boss.Id && e.Time >= t0 && e.Time < t0 + 10).Sum(e => e.Amount);
                                var onPlayers = ev.Where(e => e.Kind == DungeonGen.Core.Combat.FightEventKind.Hit && e.Time >= t0 && e.Time < t0 + 10 && sim.Get(e.Target) is DungeonGen.Core.Combat.Combatant tc && !tc.IsMob).ToList();
                                string by(Func<DungeonGen.Core.Combat.FightEvent, bool> f) => onPlayers.Where(f).Sum(e => e.Amount).ToString("0");
                                float heal = ev.Where(e => e.Kind == DungeonGen.Core.Combat.FightEventKind.Heal && e.Time >= t0 && e.Time < t0 + 10 && e.Target != boss.Id).Sum(e => e.Amount);
                                Console.WriteLine($"  {t0,3}-{t0 + 10,3}s  to boss {toBoss,6:0}  to players: boss {by(e => e.Source == boss.Id)} adds {by(e => e.Source > 9000)} other {by(e => e.Source != boss.Id && e.Source <= 9000)}  healed {heal:0}  downs {ev.Count(e => e.Kind == DungeonGen.Core.Combat.FightEventKind.Downed && e.Time >= t0 && e.Time < t0 + 10)}");
                            }
                            foreach (var e in ev.Where(e => e.Kind == DungeonGen.Core.Combat.FightEventKind.Phase || e.Kind == DungeonGen.Core.Combat.FightEventKind.Downed || e.Kind == DungeonGen.Core.Combat.FightEventKind.Died && !(sim.Get(e.Target)?.IsMob ?? false))) Console.WriteLine("  " + e);
                            foreach (var g in ev.Where(e => e.Kind == DungeonGen.Core.Combat.FightEventKind.Hit && sim.Get(e.Target) is DungeonGen.Core.Combat.Combatant tc && !tc.IsMob).GroupBy(e => (e.Source == boss.Id ? "boss" : e.Source > 9000 ? "add" : e.Source < 0 ? "env" : "ally:" + (sim.Get(e.Source)?.Spec ?? "?")) + " " + e.Detail).OrderByDescending(g => g.Sum(e => e.Amount)).Take(10)) Console.WriteLine($"    damage on players: {g.Key,-32} {g.Sum(e => e.Amount),7:0} in {g.Count()} hits");
                            Console.WriteLine($"  boss left {boss.Health:0}/{boss.MaxHealth:0}  phase {dir.Boss.Phase}  enraged {dir.Boss.Enraged}  adds spawned {dir.Boss.Adds.Count}");
                        });
                        Console.WriteLine("  " + rep);
                        return 0;
                    }
                case "packs":
                    {
                        var party = new[] { "Sentinel", "Berserker", "Warder" };
                        Console.WriteLine($"TRIO at gear tier {tier} vs a room's pack of each mob, {seeds} seeds.");
                        foreach (var mob in DungeonGen.Core.Combat.MobArchetype.DefaultRoster.Select(m => m.Name))
                        {
                            int wins = 0, downs = 0; float secs = 0;
                            for (int s = 1; s <= seeds; s++) { var r = DungeonGen.Core.Combat.FightLab.PackFight(mob, party, tier, s); if (r.WinnerTeam == 0) wins++; secs += r.Seconds; downs += r.Downs; }
                            Console.WriteLine($"  {mob,-16} win {wins / (float)seeds:0.00}  avg {secs / seeds,5:0.0}s  downs/fight {downs / (float)seeds:0.0}");
                        }
                        return 0;
                    }
                case "roomtrace":
                    {
                        var arch = Environment.GetEnvironmentVariable("ARCH") ?? "Hive Matron";
                        var report = DungeonGen.Core.Combat.FightLab.BossFight(arch, new[] { "Sentinel", "Berserker", "Warder" }, tier, seed, 300f, (sim, dir) =>
                        {
                            Console.WriteLine($"BOSS {dir.Boss.Boss} budget hp {dir.Boss.Boss.MaxHealth:0} dps {dir.Boss.Boss.Dps:0} scale {dir.Boss.Boss.DamageScale} affixes {string.Join(",", dir.Boss.Instance.Affixes.Select(a => a.Name))}");
                            foreach (var c in sim.Combatants.Values.Where(c => !c.IsMob)) Console.WriteLine($"  {c}");
                            foreach (var e in sim.Events.Where(e => e.Kind != DungeonGen.Core.Combat.FightEventKind.Rejected && e.Kind != DungeonGen.Core.Combat.FightEventKind.Hit || (e.Kind == DungeonGen.Core.Combat.FightEventKind.Hit && e.Amount >= 150)).Take(40)) Console.WriteLine("  " + e);
                            var first = sim.Events.FirstOrDefault(e => e.Kind == DungeonGen.Core.Combat.FightEventKind.Downed);
                            if (first.Kind == DungeonGen.Core.Combat.FightEventKind.Downed) foreach (var h in sim.Hits.LastHits(first.Target)) Console.WriteLine($"  last hit on {first.Target}: {h.Time:0.0}s {h.Amount:0} {h.Type} {h.Ability} by {h.Attacker}");
                            var rej = sim.Events.Where(e => e.Kind == DungeonGen.Core.Combat.FightEventKind.Rejected).GroupBy(e => e.Detail.Split(':')[0] + ":" + e.Detail.Split(':').Last()).OrderByDescending(g => g.Count()).Take(5);
                            foreach (var g in rej) Console.WriteLine($"  rejected x{g.Count()}: {g.Key}");
                        });
                        Console.WriteLine("  " + report);
                        return 0;
                    }
                case "bosstrace":
                    {
                        var b = new DungeonGen.Core.Combat.CombatBaseline();
                        var budget = DungeonGen.Core.Combat.EncounterTuner.RoomBoss(b);
                        Console.WriteLine("BUDGET " + budget);
                        var sim = new DungeonGen.Core.Combat.FightSim(DungeonGen.Core.Combat.FightLab.OpenArena(16, 16), seed);
                        var party = new[] { "Sentinel", "Berserker", "Warder" };
                        for (int i = 0; i < party.Length; i++) { var c = sim.Add(DungeonGen.Core.Combat.FightLab.Player(1 + i, 0, party[i], tier, new DungeonGen.Core.Combat.Vec2(3f + i * 2f, 3f), b)); Console.WriteLine($"  {c}  dps {c.Dps:0} armour {c.ArmourRating:0} atkspd {c.AttackSpeed:0.00}"); }
                        var arch = DungeonGen.Core.Combat.BossArchetype.DefaultPool[0];
                        var boss = sim.Add(DungeonGen.Core.Combat.Combatant.Boss(99, 1, arch.Name, budget, arch.Damage, null, new DungeonGen.Core.Combat.Vec2(9f, 12f), DungeonGen.Core.Combat.StatusKind.Bleed));
                        Console.WriteLine($"  {boss} dps {boss.Dps:0} profile {arch.Damage}");
                        var report = DungeonGen.Core.Combat.FightLab.Run(sim, 240f);
                        foreach (var kv in report.DamageDealt) Console.WriteLine($"  dealt by {kv.Key}: {kv.Value:0}");
                        foreach (var e in sim.Events.Where(e => e.Kind == DungeonGen.Core.Combat.FightEventKind.Downed || e.Kind == DungeonGen.Core.Combat.FightEventKind.Died || e.Kind == DungeonGen.Core.Combat.FightEventKind.Finished)) Console.WriteLine("  " + e);
                        Console.WriteLine($"  boss hp left {boss.Health:0}/{boss.MaxHealth:0}; " + report);
                        var hits = sim.Events.Where(e => e.Kind == DungeonGen.Core.Combat.FightEventKind.Hit && e.Source == 99).Take(6);
                        foreach (var h in hits) Console.WriteLine("  " + h);
                        return 0;
                    }
                default:
                    {
                        var sim = new DungeonGen.Core.Combat.FightSim(DungeonGen.Core.Combat.FightLab.OpenArena(20, 12), seed);
                        sim.Add(DungeonGen.Core.Combat.FightLab.Player(1, 0, "Duelist", tier, new DungeonGen.Core.Combat.Vec2(4f, 6f)));
                        sim.Add(DungeonGen.Core.Combat.FightLab.Player(2, 1, "Pyromancy", tier, new DungeonGen.Core.Combat.Vec2(17f, 6f)));
                        var report = DungeonGen.Core.Combat.FightLab.Run(sim, 120f);
                        foreach (var e in sim.Events.Where(e => e.Kind != DungeonGen.Core.Combat.FightEventKind.Rejected).Take(60)) Console.WriteLine("  " + e);
                        Console.WriteLine("  ... " + report);
                        return 0;
                    }
            }
        }

        /// <summary>--view TEAM: what one team knows after a short scripted round, next to what the server knows.</summary>
        private static int ViewDemo(DungeonSettings settings, int seed, int team)
        {
            var d = DungeonGen.Core.Net.MatchDescriptor.Create(seed, settings, new DungeonGen.Core.Net.MatchRules(), out var layout, lootSecret: 424242);
            var st = new DungeonGen.Core.Net.MatchState(d, layout);
            st.Apply(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.RoundStarted, 0), out _);
            int teams = d.TeamSpawnRooms.Count;
            for (int t = 0; t < teams; t++) for (int p = 0; p < 2; p++) st.Apply(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.PlayerSpawned, 0, t * 10 + p + 1, t, p), out _);
            st.Apply(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.DoorsOpened, 10), out _);
            // Every team explores its spawn's neighbours; team 0 also walks to the nearest chest room and opens it.
            for (int t = 0; t < teams; t++) foreach (int n in layout.Rooms[d.TeamSpawnRooms[t]].Neighbours) st.Apply(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.RoomSeen, 20, t, n), out _);
            var chest = layout.Rooms.Where(r => r.Loot.Count > 0 && !r.IsBossRoom && r.Type != DungeonGen.Core.RoomType.Vault).OrderBy(r => System.Math.Abs(r.DepthFromSpawn - 2)).First();
            st.Apply(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.RoomSeen, 30, 0, chest.Id), out _);
            st.Apply(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.PlayerEnteredRoom, 30, 1, chest.Id), out _);
            st.Apply(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.PlayerEnteredRoom, 30, 2, chest.Id), out _);
            st.Apply(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.LootStarted, 31, 1, chest.Id, 0), out _);
            st.Apply(new DungeonGen.Core.Net.MatchEvent(DungeonGen.Core.Net.MatchEventKind.ContainerOpened, 33, chest.Id, 1), out _);
            if (team < 0 || team >= teams) { Console.Error.WriteLine($"--view needs a team 0..{teams - 1}"); return 1; }
            var v = DungeonGen.Core.Net.TeamView.For(st, team);
            Console.WriteLine($"SERVER knows: {layout.Rooms.Count} rooms, {layout.Items.Count} items, {layout.Hazards.Count} hazards, {st.PlayerTeam.Count} players, {st.OpenRolls.Count} open roll(s), event loot: {layout.EventLoot?.Name}");
            Console.WriteLine($"TEAM {team} VIEW ({v.ToBytes().Length} bytes):");
            Console.WriteLine($"  clock {v.Time:0}s, doors {(v.DoorsOpen ? "open" : "sealed")}, extracts {(v.ExtractsOpen ? "open" : "closed")}, exit {(v.BossExitOpen ? "open" : "shut")}, final boss {(v.FinalBossDead ? "dead" : "alive")}");
            foreach (var m in v.Members) Console.WriteLine($"  member {m.Player}: {DungeonGen.Core.Combat.ClassArchetype.DefaultRoster[m.Class].Name}, room {m.Room}, souls {m.Souls}, silver {m.Silver}{(m.Dead ? ", dead" : "")}");
            Console.WriteLine($"  explored rooms: {string.Join(", ", v.Explored)}  (of {layout.Rooms.Count})");
            Console.WriteLine($"  open containers seen: {string.Join(", ", v.OpenedContainers)}");
            Console.WriteLine($"  items visible: {v.Items.Count}" + (v.Items.Count > 0 ? " → " + string.Join("; ", v.Items.Select(i => $"#{i.Id} {i.Name} ({(DungeonGen.Core.Items.Rarity)i.Rarity}, ilvl {i.Level})")) : ""));
            Console.WriteLine($"  rolls open for this team: {v.Rolls.Count}; known hazards: {v.KnownHazards.Count}; closed doors: {v.ClosedDoors.Count}");
            Console.WriteLine($"  NOT in the view: {st.PlayerTeam.Count - v.Members.Count} enemy players, their rooms and souls; {layout.Items.Count - v.Items.Count} items; {layout.Rooms.Count - v.Explored.Count} rooms; the event relic.");
            return 0;
        }

        private static int RollItems(int n, int level, int seed)
        {
            var rng = new DungeonRandom(seed);
            var counts = new System.Collections.Generic.Dictionary<DungeonGen.Core.Items.Rarity, int>();
            for (int i = 0; i < n; i++)
            {
                var it = DungeonGen.Core.Items.LootRoller.Roll(level, DungeonGen.Core.Items.LootSource.Chest, rng);
                counts[it.Rarity] = counts.TryGetValue(it.Rarity, out int c) ? c + 1 : 1;
                if (n <= 40) Console.WriteLine(it);
            }
            Console.WriteLine(string.Join(", ", counts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {100.0 * kv.Value / n:0.0}%")));
            return 0;
        }

        /// <summary>Dress each class in its starting set, then in a rolled tier-3 Rare set, and show both sheets.</summary>
        private static int WearRolledSet(int seed)
        {
            var b = new DungeonGen.Core.Combat.CombatBaseline();
            var rng = new DungeonRandom(seed);
            foreach (var c in DungeonGen.Core.Combat.ClassArchetype.DefaultRoster)
            {
                var start = DungeonGen.Core.Items.Equipment.StandardSet(c.Armour, 0, c.Primary);
                Console.WriteLine(DungeonGen.Core.Combat.PlayerStats.For(c, start, b));
                var geared = DungeonGen.Core.Items.Equipment.StandardSet(c.Armour, 3, c.Primary);
                int tries = 0;
                while (geared.Items.Count(i => i.Rarity == DungeonGen.Core.Items.Rarity.Rare) < 9 && tries++ < 400)
                {
                    var it = DungeonGen.Core.Items.LootRoller.Roll(3, DungeonGen.Core.Items.LootSource.Vault, rng);
                    if (it.Rarity != DungeonGen.Core.Items.Rarity.Rare) continue;
                    if (it.Base.IsArmour && it.Base.Weight != c.Armour) continue;
                    if (it.Base.IsWeapon && it.Base.ScalesWith != c.Primary) continue;
                    geared.Equip(it);
                }
                Console.WriteLine(DungeonGen.Core.Combat.PlayerStats.For(c, geared, b));
                Console.WriteLine(geared);
                Console.WriteLine();
            }
            return 0;
        }

        private static int PrintStats(DungeonSettings settings)
        {
            var b = settings.Combat;
            Console.WriteLine("CLASSES (tier 0 and tier 3 gear)");
            foreach (var c in DungeonGen.Core.Combat.ClassArchetype.DefaultRoster)
            {
                Console.WriteLine($"  {DungeonGen.Core.Combat.PlayerStats.For(c, 0, b)}");
                Console.WriteLine($"  {DungeonGen.Core.Combat.PlayerStats.For(c, 3, b)}");
            }
            Console.WriteLine();
            Console.WriteLine("ELEMENTALIST KITS (element = weapon in hand)");
            foreach (var k in DungeonGen.Core.Combat.ElementKit.All)
            {
                Console.WriteLine($"  {k.Name} ({k.Element}) - {k.Tagline}  on hit: {k.OnHit.Kind}");
                foreach (var a in k.Abilities) Console.WriteLine($"      {a}");
            }
            Console.WriteLine();
            Console.WriteLine("WEAPON SPECS (the weapon in hand is the spec)");
            foreach (var sp in DungeonGen.Core.Combat.WeaponSpec.All)
            {
                Console.WriteLine($"  {sp.ClassName,-9} {sp.WeaponBase,-13} {sp.Name,-13} {sp.Tagline}");
                foreach (var a in sp.Abilities) Console.WriteLine($"      {a}");
            }
            Console.WriteLine();
            Console.WriteLine("STATUS EFFECTS");
            foreach (var d in DungeonGen.Core.Combat.StatusEffectDefinition.All.Values)
                Console.WriteLine($"  {d.Kind,-9} {d.Element,-9} {d.Description}");
            Console.WriteLine();
            Console.WriteLine("BOSS DAMAGE");
            foreach (var a in DungeonGen.Core.Combat.BossArchetype.DefaultPool.Append(DungeonGen.Core.Combat.BossArchetype.FinalBoss))
                Console.WriteLine($"  {a.Name,-20} {a.Damage,-40} on hit: {a.OnHit?.Kind.ToString() ?? "-",-9} retinue: {a.RetinueMob ?? "-"}");
            Console.WriteLine();
            Console.WriteLine("MOBS");
            foreach (var m in DungeonGen.Core.Combat.MobArchetype.DefaultRoster)
                Console.WriteLine($"  {m.Name,-16} deals {m.Damage,-28} on hit {m.OnHit?.Kind.ToString() ?? "-",-9} resists {m.Resists,-9} weak to {m.WeakTo,-9} hp {m.HealthFraction:0.00} dps {m.DpsFraction:0.00}");
            Console.WriteLine();
            Console.WriteLine("A 100-damage hit of each type, taken by each class in tier-0 gear:");
            Console.Write("  class        ");
            foreach (DungeonGen.Core.Combat.DamageType t in Enum.GetValues(typeof(DungeonGen.Core.Combat.DamageType))) Console.Write($"{t,10}");
            Console.WriteLine();
            foreach (var c in DungeonGen.Core.Combat.ClassArchetype.DefaultRoster)
            {
                var st = DungeonGen.Core.Combat.PlayerStats.For(c, 0, b);
                Console.Write($"  {c.Name,-12}");
                foreach (DungeonGen.Core.Combat.DamageType t in Enum.GetValues(typeof(DungeonGen.Core.Combat.DamageType))) Console.Write($"{st.TakeHit(100f, t),10:0}");
                Console.WriteLine();
            }
            return 0;
        }

        private static int PrintFightTable(DungeonSettings settings)
        {
            var b = settings.Combat;
            foreach (var e in new[] { DungeonGen.Core.Combat.EncounterTuner.RoomBoss(b), DungeonGen.Core.Combat.EncounterTuner.FinalBoss(b) })
            {
                Console.WriteLine(e);
                Console.WriteLine("  players  gear   fight(s)   taken   capacity  margin  result");
                foreach (float gear in new[] { 1f, b.GoodGearMultiplier })
                    for (int k = 1; k <= b.PartySize + 1; k++)
                    {
                        var o = DungeonGen.Core.Combat.EncounterTuner.Simulate(e, b, k, gear);
                        Console.WriteLine($"  {k,7}  x{gear:0.00}  {o.FightSeconds,8:0}  {o.DamageTaken,6:0}  {o.DamageCapacity,9:0}  {o.Margin,6:0.00}  {(o.Wins ? "win" : "wipe")}");
                    }
            }
            return 0;
        }

        private static int RunBatch(DungeonSettings settings, int startSeed, int count)
        {
            int withWarnings = 0, failed = 0;
            int totalRooms = 0, totalLoops = 0, maxDepthSum = 0;
            double fairnessSum = 0; float fairnessMin = 1f;
            var warningKinds = new System.Collections.Generic.Dictionary<string, int>();
            var archetypes = new System.Collections.Generic.Dictionary<string, int>();
            var affixes = new System.Collections.Generic.Dictionary<string, int>();
            int affixSlots = 0, bossRooms = 0;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < count; i++)
            {
                var layout = DungeonGenerator.Generate(settings, startSeed + i);
                var problems = new System.Collections.Generic.List<string>();
                if (!LayoutValidator.IsPlayable(layout, settings, problems)) failed++;
                if (layout.Warnings.Count > 0) withWarnings++;
                foreach (var w in layout.Warnings)
                {
                    string kind = w.Length > 48 ? w.Substring(0, 48) + "..." : w;
                    warningKinds[kind] = warningKinds.TryGetValue(kind, out int c) ? c + 1 : 1;
                }
                totalRooms += layout.Rooms.Count;
                totalLoops += layout.Corridors.Count(c => c.IsLoop);
                maxDepthSum += layout.Rooms.Max(r => r.DepthFromSpawn);
                fairnessSum += layout.Fairness.Score;
                foreach (var r in layout.BossRooms)
                {
                    bossRooms++;
                    archetypes[r.Boss.Archetype.Name] = archetypes.TryGetValue(r.Boss.Archetype.Name, out int ac) ? ac + 1 : 1;
                    foreach (var a in r.Boss.Affixes)
                    {
                        affixSlots++;
                        affixes[a.Name] = affixes.TryGetValue(a.Name, out int fc) ? fc + 1 : 1;
                    }
                }
                if (layout.Fairness.Score < fairnessMin) fairnessMin = layout.Fairness.Score;
            }
            sw.Stop();
            Console.WriteLine($"Batch of {count} seeds starting at {startSeed}");
            Console.WriteLine($"  unplayable after retries : {failed}");
            Console.WriteLine($"  layouts with warnings    : {withWarnings}");
            Console.WriteLine($"  avg rooms                : {(double)totalRooms / count:0.0}");
            Console.WriteLine($"  avg loop corridors       : {(double)totalLoops / count:0.0}");
            Console.WriteLine($"  avg max depth (hops)     : {(double)maxDepthSum / count:0.0}");
            Console.WriteLine($"  avg fairness (min)       : {fairnessSum / count:0.00} ({fairnessMin:0.00})");
            Console.WriteLine($"  avg time per dungeon     : {sw.Elapsed.TotalMilliseconds / count:0.00} ms");
            foreach (var kv in warningKinds.OrderByDescending(kv => kv.Value))
                Console.WriteLine($"  warning x{kv.Value,-4}: {kv.Key}");
            if (bossRooms > 0)
            {
                Console.WriteLine($"  boss archetypes over {bossRooms} boss rooms: " + string.Join(", ", archetypes.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {100.0 * kv.Value / bossRooms:0}%")));
                Console.WriteLine($"  affixes ({(double)affixSlots / bossRooms:0.00} per boss): " + string.Join(", ", affixes.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")));
            }
            return failed == 0 ? 0 : 2;
        }
    }
}
