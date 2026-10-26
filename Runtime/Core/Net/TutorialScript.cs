using System;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Combat;

namespace DungeonGen.Core.Net
{
    /// <summary>The tutorial's objectives, in the order the card teaches them. <see cref="Done"/> after the extract; <see cref="Failed"/> on a death.</summary>
    public enum TutorialStep : byte { Move, OpenDoor, KillMobs, LootChest, ArmUltimate, KillBoss, Extract, Done, Failed }

    /// <summary>One objective as the card shows it. Text carries key tokens (<c>{Interact}</c>, <c>{Ultimate}</c>…) that the UI fills from the player's own bindings.</summary>
    public sealed class TutorialObjective
    {
        public TutorialStep Step;
        public string Title;
        public string Text;
        /// <summary>A second line under the text: what the player should notice (the weight, the price, the timer).</summary>
        public string Hint;
    }

    /// <summary>
    /// Which rooms the tutorial's lesson runs through, read from a generated layout: the spawn, the two mob rooms, the room whose chest the card points at,
    /// the shrine, the room boss, the extract, and the final boss's room (kept off the route). Null from <see cref="TutorialScript.Plan"/> when a layout cannot teach the loop.
    /// </summary>
    public sealed class TutorialRoute
    {
        public int SpawnRoom, ChestRoom, ShrineRoom, BossRoom, ExtractRoom, FinalBossRoom = -1;
        public List<int> PackRooms = new List<int>();
        /// <summary>The spawn room's doors (door ids as <see cref="LayoutIndex.Doors"/> numbers them): shut at the start so the second lesson has a door to open.</summary>
        public List<int> SpawnDoors = new List<int>();
    }

    /// <summary>
    /// The tutorial round's rules, in one place (wave 5.10). A new player learns the loop in one short solo round with two friendly bots and no enemy team:
    /// the smallest dungeon the generator allows (one spawn, one room boss, one extract, two mob rooms; the generator always adds the final boss's room, which the
    /// route avoids), the spawn doors shut so there is a door to open, a soul for every mob pack cleared (in a normal round souls come only from boss chests and
    /// player kills; here two packs pay for the ultimate), and the extract opening <see cref="ExtractDelayAfterBoss"/> seconds after the boss falls (or at
    /// <see cref="ExtractsFallbackSeconds"/> regardless). The round never settles into the player's profile: the gear is a throwaway kit and the only lasting
    /// thing is a one-time silver reward behind a profile flag (<see cref="RewardFlag"/>).
    /// </summary>
    public static class TutorialScript
    {
        /// <summary>The extract opens this long after the room boss dies.</summary>
        public const float ExtractDelayAfterBoss = 60f;
        /// <summary>A slow learner is never shut in: the extract opens at this round time whatever happened.</summary>
        public const int ExtractsFallbackSeconds = 12 * 60;
        /// <summary>The tutorial's clock. Long enough for anyone; the lesson takes five to eight minutes.</summary>
        public const int RoundSeconds = 20 * 60;
        /// <summary>Souls one cleared mob pack gives each standing player near it (tutorial rounds only).</summary>
        public const int SoulsPerPack = 1;
        /// <summary>The most mobs in one tutorial pack: "a couple of weak packs", not a swarm.</summary>
        public const int MaxPackSize = 6;
        /// <summary>Mobs that hit harder than their budget says (the Gravewalker's shadow drain wiped headless tutorial parties): never a tutorial pack.</summary>
        public static readonly string[] UnfitPackMobs = { "Gravewalker" };
        /// <summary>Silver paid once, the first time a profile finishes (extracts from) the tutorial.</summary>
        public const int RewardSilver = 100;
        /// <summary>Profile flags, kept in <see cref="Profile.PlayerProfile.Unlocks"/> (saved by <see cref="Profile.ProfileCodec"/> with no version change).</summary>
        public const string RewardFlag = "tutorial.rewarded", OfferedFlag = "tutorial.offered";
        /// <summary>The friendly bots' specs: a healer and a melee bruiser, so nobody shoots through the player.</summary>
        public static readonly string[] BotSpecs = { "Warder", "Berserker" };
        /// <summary>The bots' gear tier.</summary>
        public const int BotTier = 1;
        /// <summary>The tutorial's room boss: the Iron Juggernaut's crush and trample are telegraphed and readable.</summary>
        public const string TutorialBoss = "Iron Juggernaut";
        /// <summary>Class seated when the account has no character (never saved).</summary>
        public const string FallbackClass = "Warden";

        /// <summary>The tutorial dungeon: the fewest rooms <see cref="DungeonSettings.Validate"/> allows, a tree (no loops), no vault, no hazards, weak packs, a softer Iron Juggernaut with no affixes.</summary>
        public static DungeonSettings Settings()
        {
            var s = new DungeonSettings
            {
                Width = 64, Height = 64, TargetRoomCount = 6, MinRoomSize = 7, MaxRoomSize = 13,
                SpawnCount = 1, ExtractCount = 1, BossCount = 1, VaultCount = 0,
                SpawnPlacement = SpawnPlacement.Fringe, MinSpawnSeparation = 1, MinExtractDepth = 2,
                LoopChance = 0f, CandidateCount = 1,
                MobDensity = 1f, MobBudgetAtEdge = 0.35f, MobBudgetAtCenter = 0.6f, MobBudgetLootBonus = 0f,
                HazardsEnabled = false, BossAffixesEnabled = false, MaxAffixesPerBoss = 0,
                // The boss that teaches reading the floor: one target, telegraphed hits, nothing that stacks or swarms.
                AllowedBosses = new[] { TutorialBoss },
            };
            s.Combat.RoomBossPlayerEquivalents = 0.85f;   // a boss built for less than one player (two and a half in a real round): the bots help, the ultimate decides
            s.Combat.RoomBossFightSeconds = 35f;
            return s;
        }

        /// <summary>The tutorial's round rules: <see cref="MatchRules.IsTutorial"/> (no countdown lock, no mid-round event), a team of three, the long fallback for the extract.</summary>
        public static MatchRules Rules() => new MatchRules
        {
            RoundSeconds = RoundSeconds, ExtractsOpenAfterSeconds = ExtractsFallbackSeconds, TeamSize = 3,
            IsTutorial = true, MidRoundEvent = false, BotTeams = 0,
        };

        /// <summary>Bend rules made elsewhere (a scene's bootstrap) into the tutorial's, in place, before the round starts.</summary>
        public static void ApplyRules(MatchRules r)
        {
            var t = Rules();
            r.RoundSeconds = t.RoundSeconds; r.ExtractsOpenAfterSeconds = t.ExtractsOpenAfterSeconds; r.TeamSize = t.TeamSize;
            r.IsTutorial = true; r.MidRoundEvent = false; r.BotTeams = 0;
        }

        // ---------------------------------------------------------------- the route ----------------------------------------------------------------

        /// <summary>A walkable cell of the room, nearest its centre.</summary>
        public static GridPoint Inside(DungeonLayout layout, int room)
        {
            var r = layout.Rooms[room];
            GridPoint best = r.Center; int bestD = int.MaxValue;
            for (int x = r.Bounds.X; x < r.Bounds.XMax; x++)
                for (int y = r.Bounds.Y; y < r.Bounds.YMax; y++)
                {
                    var p = new GridPoint(x, y);
                    if (!layout.InBounds(p) || layout.RoomIdAt[x, y] != room || !layout.IsWalkable(p)) continue;
                    int d = p.ManhattanDistance(r.Center);
                    if (d < bestD) { bestD = d; best = p; }
                }
            return best;
        }

        /// <summary>Every cell of these rooms, as a blocked set for <see cref="GridPath.Find"/>.</summary>
        public static HashSet<GridPoint> CellsOf(DungeonLayout layout, IEnumerable<int> rooms)
        {
            var set = new HashSet<GridPoint>();
            foreach (int id in rooms)
            {
                if (id < 0) continue;
                var b = layout.Rooms[id].Bounds;
                for (int x = b.X; x < b.XMax; x++) for (int y = b.Y; y < b.YMax; y++) if (layout.InBounds(new GridPoint(x, y)) && layout.RoomIdAt[x, y] == id) set.Add(new GridPoint(x, y));
            }
            return set;
        }

        /// <summary>Can one walk from room <paramref name="from"/> to room <paramref name="to"/> on the tiles without setting foot in any of <paramref name="avoid"/>?</summary>
        public static bool Reachable(DungeonLayout layout, int from, int to, IEnumerable<int> avoid)
        {
            var blocked = CellsOf(layout, avoid.Where(a => a != from && a != to));
            return GridPath.Find(layout, Inside(layout, from), Inside(layout, to), maxCost: 100000, closedDoors: blocked) != null;
        }

        /// <summary>
        /// Read the lesson's route off a layout, or null with <paramref name="why"/> when this layout cannot teach it: one spawn, exactly two mob rooms, a shrine,
        /// one room boss, one extract; the mob rooms, the shrine and a chest reachable without walking through the boss's room or the final boss's; the extract
        /// reachable without the final boss's room; and a door on the spawn room.
        /// </summary>
        public static TutorialRoute Plan(DungeonLayout layout, out string why)
        {
            why = null;
            var spawns = layout.SpawnRooms.ToList();
            var bosses = layout.BossRooms.ToList();
            var extracts = layout.ExtractRooms.ToList();
            if (spawns.Count != 1 || bosses.Count != 1 || extracts.Count != 1) { why = $"{spawns.Count} spawns, {bosses.Count} bosses, {extracts.Count} extracts"; return null; }
            var route = new TutorialRoute { SpawnRoom = spawns[0].Id, BossRoom = bosses[0].Id, ExtractRoom = extracts[0].Id, FinalBossRoom = layout.FinalBossRoom?.Id ?? -1 };
            route.PackRooms = layout.Rooms.Where(r => r.Mobs != null && r.Mobs.Count > 0).Select(r => r.Id).ToList();
            if (route.PackRooms.Count != 2) { why = $"{route.PackRooms.Count} mob rooms"; return null; }
            if (route.PackRooms.Any(id => layout.Rooms[id].Mobs.Count > MaxPackSize)) { why = "a pack is too big for a first fight"; return null; }
            if (route.PackRooms.Any(id => UnfitPackMobs.Contains(layout.Rooms[id].Mobs.Mob.Name))) { why = "a pack is too nasty for a first fight"; return null; }
            var shrine = layout.Rooms.FirstOrDefault(r => r.HasShrine && r.Type != RoomType.Boss && r.Type != RoomType.FinalBoss);
            if (shrine == null) { why = "no shrine"; return null; }
            route.ShrineRoom = shrine.Id;
            var beforeBoss = new[] { route.BossRoom, route.FinalBossRoom };
            foreach (int target in route.PackRooms.Append(route.ShrineRoom).Append(route.BossRoom))
                if (!Reachable(layout, route.SpawnRoom, target, target == route.BossRoom ? new[] { route.FinalBossRoom } : beforeBoss)) { why = $"room {target} is only reached through a boss"; return null; }
            if (!Reachable(layout, route.SpawnRoom, route.ExtractRoom, new[] { route.FinalBossRoom })) { why = "the extract is behind the final boss"; return null; }
            // The chest the card points at: a mob room's first, else any other ordinary room with loot on the safe side.
            var chest = route.PackRooms.Concat(new[] { route.ShrineRoom })
                .Concat(layout.Rooms.Where(r => r.Type != RoomType.Boss && r.Type != RoomType.FinalBoss && r.Type != RoomType.Spawn).Select(r => r.Id))
                .Cast<int?>().FirstOrDefault(id => layout.Rooms[id.Value].Loot.Count > 0 && Reachable(layout, route.SpawnRoom, id.Value, beforeBoss));
            if (!chest.HasValue) { why = "no chest on the safe side"; return null; }
            route.ChestRoom = chest.Value;
            var index = new LayoutIndex(layout);
            route.SpawnDoors = index.Doors.Where(kv => kv.Value.room == route.SpawnRoom).Select(kv => kv.Key).OrderBy(i => i).ToList();
            if (route.SpawnDoors.Count == 0) { why = "the spawn room has no door"; return null; }
            return route;
        }

        /// <summary>
        /// The first seed at or after <paramref name="seed"/> (stepping by one) whose tutorial dungeon teaches the whole loop (<see cref="Plan"/>). Deterministic.
        /// Throws if none of <paramref name="tries"/> seeds does, which the tests rule out.
        /// </summary>
        public static int PickSeed(int seed, int tries = 400)
        {
            var settings = Settings();
            for (int i = 0; i < tries; i++)
            {
                int s = seed + i;
                var layout = DungeonGenerator.Generate(settings, s);
                if (Plan(layout, out _) != null) return s;
            }
            throw new InvalidOperationException($"No tutorial dungeon among seeds {seed}..{seed + tries - 1}.");
        }

        // ---------------------------------------------------------------- round rules ----------------------------------------------------------------

        /// <summary>
        /// Who a cleared pack pays (<see cref="MatchState"/> applies it on <see cref="MatchEventKind.MobPackCleared"/> in a tutorial round): every standing player
        /// whose last room is the pack's room or one next to it. The fight is over by then, so the players near it are the ones who fought it.
        /// </summary>
        public static IEnumerable<int> PackSoulRecipients(MatchState st, int packId)
        {
            int room = packId - LayoutIndex.MobPackIdBase;
            if (room < 0 || room >= st.Layout.Rooms.Count) yield break;
            var near = st.Index.RoomsWithin(room, 1);
            foreach (var kv in st.PlayerTeam.OrderBy(kv => kv.Key))
            {
                int p = kv.Key;
                if (st.DeadPlayers.Contains(p) || st.ExtractedPlayers.Contains(p) || st.Downed.ContainsKey(p)) continue;
                if (st.PlayerRoom.TryGetValue(p, out int at) && near.Contains(at)) yield return p;
            }
        }

        /// <summary>When the room boss died, from the log, or null while it lives.</summary>
        public static float? BossKilledAt(MatchState st)
        {
            foreach (var e in st.Log)
                if (e.Kind == MatchEventKind.BossKilled && e.A >= 0 && e.A < st.Layout.Rooms.Count && st.Layout.Rooms[e.A].Type == RoomType.Boss) return e.Time;
            return null;
        }

        /// <summary>The round time the extract opens at: <see cref="ExtractDelayAfterBoss"/> after the boss, or the fallback, whichever is sooner.</summary>
        public static float ExtractOpensAt(MatchState st)
        {
            var boss = BossKilledAt(st);
            return boss.HasValue ? Math.Min(boss.Value + ExtractDelayAfterBoss, ExtractsFallbackSeconds) : ExtractsFallbackSeconds;
        }

        /// <summary>Should the server raise <see cref="MatchEventKind.ExtractsOpened"/> now?</summary>
        public static bool ExtractsDue(MatchState st, float now) => st.Started && !st.ExtractsOpen && now >= ExtractOpensAt(st);

        // ---------------------------------------------------------------- the card ----------------------------------------------------------------

        /// <summary>The seven lessons and the two endings. Tokens in braces are binding names (<see cref="Tokens"/>).</summary>
        public static readonly IReadOnlyList<TutorialObjective> Objectives = new List<TutorialObjective>
        {
            new TutorialObjective { Step = TutorialStep.Move, Title = "Find your feet", Text = "Move with {MoveUp} {MoveLeft} {MoveDown} {MoveRight}. Aim with the mouse.", Hint = "Your two companions follow you and fight beside you." },
            new TutorialObjective { Step = TutorialStep.OpenDoor, Title = "Open the door", Text = "Stand at the shut door and hold {Interact} until it opens.", Hint = "Doors take 3 s. Tap {Interact} at an open door to shut it behind you." },
            new TutorialObjective { Step = TutorialStep.KillMobs, Title = "Clear the mob rooms", Text = "Attack with {Basic}, {Bolt}, {Zone} and {Mobility}. Each cleared pack gives you a soul.", Hint = "Souls buy power at shrines. Drink a potion with {Potion}." },
            new TutorialObjective { Step = TutorialStep.LootChest, Title = "Loot a chest", Text = "Hold {Interact} in a room with a chest, then take an item from the list.", Hint = "Watch the weight: carrying more than you can slows you down. {Inventory} opens your bag." },
            new TutorialObjective { Step = TutorialStep.ArmUltimate, Title = "Pray at the shrine", Text = "Stand at the shrine's altar, choose Arm, and hold still for 2 s.", Hint = "Two souls arm your ultimate for one use. A shrine can also bring back a fallen teammate." },
            new TutorialObjective { Step = TutorialStep.KillBoss, Title = "Slay the room boss", Text = "Unleash your ultimate with {Ultimate} and bring the boss down.", Hint = "Read the floor: step out of the red circles before they land." },
            new TutorialObjective { Step = TutorialStep.Extract, Title = "Get out alive", Text = "Go to the extract and hold {Interact} until the channel completes.", Hint = "Damage breaks the channel. Die in a real round and you lose everything you carried." },
            new TutorialObjective { Step = TutorialStep.Done, Title = "Tutorial complete", Text = "You made it out. In a real round, everything you carry out is yours.", Hint = "" },
            new TutorialObjective { Step = TutorialStep.Failed, Title = "You died", Text = "In a real round, dying loses everything you brought and found.", Hint = "Nothing was lost this time: tutorial gear is not real loot." },
        };

        public static TutorialObjective Objective(TutorialStep s) => Objectives.First(o => o.Step == s);

        /// <summary>The lessons a player works through, in order (not the endings).</summary>
        public static readonly TutorialStep[] Lessons = { TutorialStep.Move, TutorialStep.OpenDoor, TutorialStep.KillMobs, TutorialStep.LootChest, TutorialStep.ArmUltimate, TutorialStep.KillBoss, TutorialStep.Extract };

        /// <summary>The binding names the texts may use (the Unity input layer's <c>BindingAction</c> names).</summary>
        public static readonly string[] Tokens = { "MoveUp", "MoveDown", "MoveLeft", "MoveRight", "Basic", "Bolt", "Zone", "Mobility", "Ultimate", "Interact", "Ping", "Potion", "Antidote", "Map", "Inventory", "Pause" };

        /// <summary>Fill every <c>{Token}</c> with what <paramref name="keyName"/> says the key is (unknown tokens stay as written).</summary>
        public static string Format(string text, Func<string, string> keyName)
        {
            if (string.IsNullOrEmpty(text) || keyName == null) return text ?? "";
            var sb = new System.Text.StringBuilder(text.Length + 16);
            int i = 0;
            while (i < text.Length)
            {
                int open = text.IndexOf('{', i);
                if (open < 0) { sb.Append(text, i, text.Length - i); break; }
                int close = text.IndexOf('}', open + 1);
                if (close < 0) { sb.Append(text, i, text.Length - i); break; }
                sb.Append(text, i, open - i);
                string token = text.Substring(open + 1, close - open - 1);
                string name = Tokens.Contains(token) ? keyName(token) : null;
                sb.Append(name ?? text.Substring(open, close - open + 1));
                i = close + 1;
            }
            return sb.ToString();
        }
    }
}
