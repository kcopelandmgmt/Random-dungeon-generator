using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Items;

namespace DungeonGen.Core.Net
{
    /// <summary>
    /// Where the server's messages go. The Unity layer forwards these to the netcode; tests and the
    /// headless round lab leave it null and read the state directly. Every byte a client ever receives
    /// passes through one of these four calls, which is what makes the fog of war auditable.
    /// </summary>
    public interface IServerWire
    {
        /// <summary>Reliable, ordered: an event this team may know happened.</summary>
        void SendEventToTeam(int team, byte[] eventBytes);
        /// <summary>Reliable, ordered: the team's refreshed <see cref="TeamView"/>.</summary>
        void SendViewToTeam(int team, byte[] viewBytes);
        /// <summary>Unreliable, every tick: the <see cref="TeamSnapshot"/> of what the team can see.</summary>
        void SendSnapshotToTeam(int team, byte[] snapshotBytes);
        /// <summary>Unreliable: a ping from a teammate.</summary>
        void SendPingToTeam(int team, byte[] pingBytes);
    }

    /// <summary>
    /// A bot seated on the server. Each tick it either returns an intent, which goes through the same
    /// validation as a player's, or acts on the simulator directly and returns null (the lab brain does
    /// this in a fight). Bots exist for the tutorial and the dev practice mode; they never queue.
    /// </summary>
    public interface IServerBot
    {
        int PlayerId { get; }
        PlayerIntent? Think(MatchServer server, float dt);
    }

    /// <summary>
    /// The server's game loop for one round, with no engine in it. Owns the authoritative
    /// <see cref="MatchState"/>, the <see cref="FightSim"/>, the encounter brains and the clock. Each
    /// tick it applies every intent (movement, casts, channels), steps the mobs and bosses, turns the
    /// simulator's deaths and the players' actions into match events the state validates, records
    /// which rooms each team now sees, and hands every team a snapshot of what it may see. Nothing
    /// here decides a rule: it connects input to the core and the core to the wire. The Unity
    /// component and the headless round lab both drive this same class, so what the lab proves holds
    /// on the dedicated server.
    /// </summary>
    public sealed class MatchServer
    {
        public MatchDescriptor Descriptor { get; }
        public MatchState State { get; }
        public DungeonLayout Layout => State.Layout;
        public FightSim Sim { get; private set; }
        public EncounterDirector Director { get; private set; }
        public RoundClock Clock { get; private set; }
        public uint Tick { get; private set; }
        public float Elapsed { get; private set; }
        public bool Running { get; private set; }
        /// <summary>The roaming elite while the mid-round event has one on the floor (null before it appears and after it falls). Server only.</summary>
        public EliteController Elite { get; private set; }
        public bool Ended => State.Ended;
        public IServerWire Wire { get; set; }
        /// <summary>Every accepted event, after the state applied it. The Unity layer opens the exit gate from here.</summary>
        public event Action<MatchEvent> EventRaised;
        /// <summary>Every rejected event with the state's reason. A rejection is a bug in whoever raised it, never in the state.</summary>
        public event Action<MatchEvent, string> EventRejected;
        /// <summary>After every tick, once the snapshots went out.</summary>
        public event Action<MatchServer> Ticked;
        public int RejectedCount { get; private set; }
        public List<string> RecentRejections { get; } = new List<string>();
        public IReadOnlyList<int> Bots => _botIds;
        public IReadOnlyList<IServerBot> BotBrains => _bots;

        private readonly Dictionary<int, PlayerIntent> _intents = new Dictionary<int, PlayerIntent>();
        private readonly Dictionary<int, int> _lastRoom = new Dictionary<int, int>();
        private readonly Dictionary<int, Channel> _channels = new Dictionary<int, Channel>();
        /// <summary>Per team: how far into the simulator's casts and events its last snapshot reached, so each effect is sent once.</summary>
        private readonly Dictionary<int, int> _castCursor = new Dictionary<int, int>(), _hitCursor = new Dictionary<int, int>();
        /// <summary>Player → the door their still-held Interact just opened. Closing is instant, so without this a held key would shut the door the tick after the channel opened it.</summary>
        private readonly Dictionary<int, int> _heldSinceDoorOpened = new Dictionary<int, int>();
        private readonly Dictionary<int, string> _spec = new Dictionary<int, string>();
        private readonly List<IServerBot> _bots = new List<IServerBot>();
        private readonly List<int> _botIds = new List<int>();
        private int _eventCursor;
        private float _sightTimer;

        private sealed class Channel
        {
            public string Kind; public int Target; public float Started; public float Seconds;
            /// <summary>A "shrine" channel's choice, applied when it completes.</summary>
            public ShrineAction Action; public int Arg;
        }

        /// <summary>Server-only: seeds the death-trash rolls (<see cref="DurabilityRules.DeathTrashChance"/>) like the loot. Never sent to a client.</summary>
        private readonly int _lootSecret;

        public MatchServer(MatchDescriptor descriptor, DungeonLayout layout, IServerWire wire = null, int? lootSecret = null)
        {
            Descriptor = descriptor; State = new MatchState(descriptor, layout); Wire = wire;
            _lootSecret = lootSecret ?? unchecked(descriptor.Seed * 1103515245 + 12345);   // no secret given (tests, tools): still deterministic per match
        }

        /// <summary>Generate the dungeon with a loot secret that never leaves this process and stand the server up on it.</summary>
        public static MatchServer Host(int seed, DungeonSettings settings, MatchRules rules, IServerWire wire = null, int? lootSecret = null)
        {
            int secret = lootSecret ?? Guid.NewGuid().GetHashCode();
            var descriptor = MatchDescriptor.Create(seed, settings, rules, out var layout, lootSecret: secret);
            return new MatchServer(descriptor, layout, wire, secret);
        }

        public IEnumerable<int> Teams => Enumerable.Range(0, Descriptor.TeamSpawnRooms.Count);
        public int TeamOf(int playerId) => State.PlayerTeam.TryGetValue(playerId, out int t) ? t : -1;
        public string SpecOf(int playerId) => _spec.TryGetValue(playerId, out var s) ? s : null;
        public int RoomOf(int playerId) => _lastRoom.TryGetValue(playerId, out int r) ? r : -1;
        public string ChannelOf(int playerId) => _channels.TryGetValue(playerId, out var c) ? c.Kind : null;

        /// <summary>
        /// Record something that happened, tell the teams allowed to hear about it, and send every
        /// team its refreshed view. Returns false if the rules reject it; the rejection is kept.
        /// </summary>
        public bool Raise(MatchEvent e)
        {
            // Before the state applies it: whose worn piece is about to leave them (a drop, a hand-off, a destroyed item).
            int wornLoser = -1;
            if (e.Kind == MatchEventKind.ItemDropped || e.Kind == MatchEventKind.ItemGiven) wornLoser = _worn.Contains(e.A) ? e.B : -1;
            else if (e.Kind == MatchEventKind.ItemDestroyed) wornLoser = _worn.Contains(e.A) && State.ItemOwner.TryGetValue(e.A, out int holder) ? holder : -1;
            if (!State.Apply(e, out string why))
            {
                RejectedCount++;
                if (RecentRejections.Count < 50) RecentRejections.Add($"{e}: {why}");
                EventRejected?.Invoke(e, why);
                return false;
            }
            if (Wire != null)
            {
                byte[] bytes;
                using (var ms = new MemoryStream()) { using (var w = new BinaryWriter(ms)) e.Write(w); bytes = ms.ToArray(); }
                var audience = EventVisibility.Audience(e, State);
                foreach (int team in Teams) if (audience == null || audience.Contains(team)) Wire.SendEventToTeam(team, bytes);
                foreach (int team in Teams) Wire.SendViewToTeam(team, TeamView.For(State, team, ItemById).ToBytes());
            }
            // A worn piece that changes hands is carried from then on (the paper doll is the profile's; in the round only the bag moves).
            if (e.Kind == MatchEventKind.ItemDropped || e.Kind == MatchEventKind.ItemGiven || e.Kind == MatchEventKind.ItemTaken || e.Kind == MatchEventKind.ItemDestroyed) _worn.Remove(e.A);
            // A dead player's salvaged materials are lost like their pockets.
            if (e.Kind == MatchEventKind.PlayerDied || e.Kind == MatchEventKind.PlayerFinished) _salvaged.Remove(e.A);
            // Worn gear that left a player changes their sheet; whatever moved changes their load and so their speed.
            if (wornLoser >= 0) RefreshSheet(wornLoser);
            switch (e.Kind)
            {
                case MatchEventKind.ItemTaken: case MatchEventKind.ItemDropped: RefreshCarry(e.B); break;
                case MatchEventKind.ItemGiven: RefreshCarry(e.B); RefreshCarry(e.C); break;
                case MatchEventKind.ItemDestroyed: if (wornLoser >= 0) RefreshCarry(wornLoser); else RefreshAllCarry(); break;
                case MatchEventKind.ItemRegistered: RefreshCarry(e.B); break;
                case MatchEventKind.ShrinePurchase: case MatchEventKind.PlayerDied: case MatchEventKind.PlayerFinished: case MatchEventKind.PlayerExtracted: RefreshCarry(e.A); break;
                case MatchEventKind.PlayerResurrected: RefreshSheet(e.A); RefreshCarry(e.A); break;
            }
            EventRaised?.Invoke(e);
            if (e.Kind == MatchEventKind.PlayerDied || e.Kind == MatchEventKind.PlayerFinished) TrashOnDeath(e);
            return true;
        }

        public void RaiseAll(IEnumerable<MatchEvent> events) { foreach (var e in events) Raise(e); }

        /// <summary>Start the round's simulation: station every pack and boss, start the clock.</summary>
        public void StartRound()
        {
            Sim = new FightSim(Layout, Descriptor.Seed) { ClosedDoors = State.ClosedDoors };
            Director = new EncounterDirector(Sim, Layout, Descriptor.Settings.Combat);
            foreach (var room in Layout.Rooms) { Director.StationPack(room); if (room.Boss != null) Director.StationBoss(room); }
            Clock = new RoundClock(Descriptor.Rules, Descriptor.Event(Layout));
            Running = true;
        }

        /// <summary>Seat a player: PlayerSpawned with the class, AccountLevelReported, a combatant at the team's spawn.</summary>
        public Combatant AddPlayer(int playerId, int team, ClassArchetype cls, string spec, PlayerStats stats, int accountLevel = 1)
        {
            if (Sim == null) throw new InvalidOperationException("StartRound before AddPlayer.");
            int classIndex = ClassArchetype.DefaultRoster.ToList().IndexOf(cls);
            if (!Raise(new MatchEvent(MatchEventKind.PlayerSpawned, Sim.Time, playerId, team, classIndex))) return null;
            Raise(new MatchEvent(MatchEventKind.AccountLevelReported, Sim.Time, playerId, accountLevel));
            var room = Layout.Rooms[Descriptor.TeamSpawnRooms[team]];
            int slot = State.PlayerTeam.Count(kv => kv.Value == team) - 1;
            var pos = Vec2.Of(room.Center) + new Vec2(slot - 1f, 0.5f);
            var c = Sim.Add(Combatant.Player(playerId, team, stats, Sim.Walkable(pos) ? pos : Vec2.Of(room.Center), spec));
            _spec[playerId] = spec;
            _lastRoom[playerId] = room.Id;
            return c;
        }

        /// <summary>Brought-in items by their server-assigned id (>= <see cref="MatchState.BroughtItemIdBase"/>). Generated loot lives in the layout index.</summary>
        private readonly Dictionary<int, Item> _brought = new Dictionary<int, Item>();
        private readonly Dictionary<int, List<int>> _broughtBy = new Dictionary<int, List<int>>();
        private readonly Dictionary<int, Dictionary<string, int>> _consumablesCarried = new Dictionary<int, Dictionary<string, int>>();
        private readonly Dictionary<int, List<(float time, string name)>> _consumableUses = new Dictionary<int, List<(float, string)>>();
        private int _nextBroughtId = MatchState.BroughtItemIdBase;
        /// <summary>Brought-in ids that were worn at spawn and have not changed hands since.</summary>
        private readonly HashSet<int> _worn = new HashSet<int>();

        /// <summary>
        /// Seat a player with what their character brings: the sheet from its gear, and every worn and
        /// carried item registered with the state (<see cref="MatchEventKind.ItemRegistered"/>) from the
        /// first tick, so full loot, hand-offs, extraction value and settlement all see them. Refuses a
        /// loadout outside the round's tier band: gear score is worn AND carried (2026-10-23 "Tiers,
        /// hand-offs…" #1–2). Returns null when the state refuses the spawn, as <see cref="AddPlayer"/> does.
        /// </summary>
        public Combatant AddPlayerWithLoadout(int playerId, int team, Profile.Loadout loadout, int accountLevel = 1)
        {
            if (loadout == null) throw new ArgumentNullException(nameof(loadout));
            string refusal = loadout.TierRefusal(Descriptor.Settings.Tier);
            if (refusal != null) throw new InvalidOperationException(refusal);
            var c = AddPlayer(playerId, team, loadout.Character.Class, loadout.Spec, loadout.Stats, accountLevel);
            if (c == null) return null;
            var ids = new List<int>();
            int wornCount = loadout.Worn.Count;
            // Past anything already registered by hand, so a server-assigned id never collides.
            _nextBroughtId = Math.Max(_nextBroughtId, State.ItemValue.Keys.Where(k => k >= MatchState.BroughtItemIdBase).DefaultIfEmpty(MatchState.BroughtItemIdBase - 1).Max() + 1);
            foreach (var (id, item, registration) in loadout.Registrations(playerId, _nextBroughtId, Sim.Time))
            {
                if (!Raise(registration)) throw new InvalidOperationException($"The state refused brought item {id} ({item.Name}): {RecentRejections.LastOrDefault()}");
                _brought[id] = item; ids.Add(id);
                if (ids.Count <= wornCount) _worn.Add(id);
                _nextBroughtId = id + 1;
            }
            _broughtBy[playerId] = ids;
            _consumablesCarried[playerId] = loadout.Consumables.ToDictionary(kv => kv.Key, kv => kv.Value);
            RefreshCarry(playerId);
            return c;
        }

        /// <summary>
        /// The item behind a network id, on the host: rolled loot from the layout index, the roaming
        /// elite's relic, or gear a player brought in. Null for an id the server never issued.
        /// </summary>
        public Item ItemById(int itemId)
        {
            if (_brought.TryGetValue(itemId, out var b)) return b;
            if (State.Index.Items.TryGetValue(itemId, out var it)) return it;
            if (itemId == LayoutIndex.EventLootId) return Layout.EventLoot;
            return null;
        }

        /// <summary>Kilograms this player holds now: every item they own plus their pockets, consumables included (what <see cref="CarryRules"/> weighs).</summary>
        public float LoadKgOf(int playerId)
        {
            float load = 0f;
            foreach (var kv in State.ItemOwner.Where(kv => kv.Value == playerId)) load += ItemById(kv.Key)?.WeightKg ?? 0f;
            foreach (var kv in PocketsOf(playerId)) load += (Consumable.ByName(kv.Key)?.WeightKg ?? 0f) * kv.Value;
            return load;
        }

        /// <summary>Overload slow-down (<see cref="CarryRules.MoveSpeedFactor"/>) set on the combatant's own carry factor, so Chill, buffs and Invisible still multiply on top.</summary>
        private void RefreshCarry(int playerId)
        {
            var body = Sim?.Get(playerId);
            if (body == null || body.IsMob || body.Stats == null) return;
            body.CarrySpeedScale = CarryRules.MoveSpeedFactor(LoadKgOf(playerId), body.Stats.CarryKg);
        }

        private void RefreshAllCarry() { if (Sim != null) foreach (var c in Sim.Combatants.Values.Where(c => !c.IsMob).ToList()) RefreshCarry(c.Id); }

        /// <summary>
        /// A worn piece left this player (dropped, handed off, destroyed, or lost with a death): rebuild the combat sheet from what they still wear, the way the
        /// loadout built it (PlayerStats.For with the worn set, the spec the weapon in hand gives) and apply it to the combatant, which keeps its health
        /// fraction and clamps its resource. Only for players seated with a loadout.
        /// </summary>
        private void RefreshSheet(int playerId)
        {
            var body = Sim?.Get(playerId);
            if (body == null || body.IsMob || body.Class == null || !_broughtBy.ContainsKey(playerId)) return;
            var stats = PlayerStats.For(body.Class, WornBy(playerId), Descriptor.Settings.Combat);
            string spec = stats.Spec?.Name ?? stats.ElementKit?.Name ?? WeaponSpec.ForClass(body.Class.Name).FirstOrDefault()?.Name ?? stats.SpecName;
            _spec[playerId] = spec;
            body.ApplyStats(stats, spec);
        }

        /// <summary>The ids this player's loadout registered, in loadout order (worn, then carried).</summary>
        public IReadOnlyList<int> BroughtItemsOf(int playerId) => _broughtBy.TryGetValue(playerId, out var l) ? l : (IReadOnlyList<int>)Array.Empty<int>();

        /// <summary>Consumables carried in (name → count). Empty for a player seated without a loadout.</summary>
        public IReadOnlyDictionary<string, int> ConsumablesCarriedBy(int playerId) => _consumablesCarried.TryGetValue(playerId, out var d) ? d : new Dictionary<string, int>();
        /// <summary>Consumables used so far (name → count).</summary>
        public IReadOnlyDictionary<string, int> ConsumablesUsedBy(int playerId) =>
            ConsumableUsesBy(playerId).GroupBy(u => u.name).ToDictionary(g => g.Key, g => g.Count());
        /// <summary>Every consumable this player used, with when. What settlement reads.</summary>
        public IReadOnlyList<(float time, string name)> ConsumableUsesBy(int playerId) =>
            _consumableUses.TryGetValue(playerId, out var l) ? l : (IReadOnlyList<(float, string)>)Array.Empty<(float, string)>();
        /// <summary>What this player has in their pockets now: carried in (plus shrine purchases in an old log), minus uses; a death empties them.</summary>
        public Dictionary<string, int> PocketsOf(int playerId) =>
            Profile.RoundSettlement.PocketsAt(State, playerId, ConsumablesCarriedBy(playerId), ConsumableUsesBy(playerId), float.MaxValue);

        /// <summary>
        /// The death trash (2026-10-22 "Round, progression…" full loot; <see cref="DurabilityRules.DeathTrashChance"/>): every item the dead player held
        /// (worn, carried, picked up, unique, starter: 2026-10-26 settlement #2 and #3 give none a pass) is destroyed with that chance, rolled from the
        /// server's loot secret so the same match always trashes the same items. The corpse keeps the survivors. Keys are not items here and always drop.
        /// </summary>
        private void TrashOnDeath(MatchEvent death)
        {
            int victim = death.A;
            if (!State.Corpses.TryGetValue(victim, out var held) || held.Count == 0) return;
            var rng = new DungeonRandom(unchecked(_lootSecret * 486187739 + victim * 7919 + 101));
            foreach (int item in held.OrderBy(i => i).ToList())
                if (rng.Chance(DurabilityRules.DeathTrashChance)) Raise(new MatchEvent(MatchEventKind.ItemDestroyed, death.Time, item));
        }

        private readonly Dictionary<(int player, string name), float> _consumableReadyAt = new Dictionary<(int, string), float>();

        /// <summary>Seconds until this player may drink/use this consumable again; 0 when ready.</summary>
        public float ConsumableCooldownRemaining(int playerId, Consumable c)
        {
            if (c == null || Sim == null) return 0f;
            return _consumableReadyAt.TryGetValue((playerId, c.Name), out float at) ? Math.Max(0f, at - Sim.Time) : 0f;
        }

        /// <summary>
        /// Use one consumable from a standing player's pockets: carried in (or, in an old log, bought at a shrine)
        /// (<see cref="MatchEventKind.ShrinePurchase"/>). False if they have none left, cannot act, or it is on cooldown
        /// (2026-10-22 "Open questions closed" #8: Health Potion 30% over 3 s, 60 s cooldown; Antidote a bottled Purge, 90 s). The effect runs in the
        /// simulator, so heal events, snapshots and the death recap see it. The Salvage Kit is only spent here: which item to break down is the profile's call
        /// (<see cref="Profile.Inventory.SalvageInField"/>), and the intent path never uses it.
        /// </summary>
        public bool UseConsumable(int playerId, Consumable c)
        {
            if (c == null || !State.PlayersInside.Contains(playerId) || State.Downed.ContainsKey(playerId)) return false;
            var body = Sim?.Get(playerId);
            if (Sim != null && body != null && (body.Dead || body.Downed)) return false;
            if (!PocketsOf(playerId).TryGetValue(c.Name, out int have) || have <= 0) return false;
            if (ConsumableCooldownRemaining(playerId, c) > 0f) return false;
            if (body != null)
            {
                bool ok = c.Name == Consumable.HealthPotionName ? Sim.StartHealOverTime(playerId, Consumable.HealthPotionFraction, Consumable.HealthPotionSeconds, c.Name)
                        : c.Name == Consumable.AntidoteName ? Sim.CleanseStatuses(playerId, c.Name) >= 0
                        : true;
                if (!ok) return false;
                if (c.CooldownSeconds > 0f) _consumableReadyAt[(playerId, c.Name)] = Sim.Time + c.CooldownSeconds;
            }
            if (!_consumableUses.TryGetValue(playerId, out var uses)) _consumableUses[playerId] = uses = new List<(float, string)>();
            uses.Add((Sim?.Time ?? State.Time, c.Name));
            RefreshCarry(playerId);   // the bottle is gone
            return true;
        }

        /// <summary>Damage this player took and dealt this round (raw hit amounts from the simulator): what durability wear reads.</summary>
        public (float taken, float dealt) DamageTotals(int playerId)
        {
            float taken = 0f, dealt = 0f;
            if (Sim == null) return (0f, 0f);
            foreach (var e in Sim.Events)
            {
                if (e.Kind != FightEventKind.Hit || e.Amount <= 0f) continue;
                if (e.Target == playerId) taken += e.Amount;
                if (e.Source == playerId && e.Target != playerId) dealt += e.Amount;
            }
            return (taken, dealt);
        }

        /// <summary>Seat a bot with the given brain (the plain fight brain when none is given). Never seated by matchmaking.</summary>
        public Combatant AddBot(int botId, int team, string spec, int tier, IServerBot brain = null) => AddBot(botId, team, spec, tier, (IReadOnlyDictionary<string, int>)null, brain);

        /// <summary>Seat a bot with pockets (consumable name → count, e.g. <see cref="BotRules.DefaultConsumables"/>); null seats it bare. The bot's own logic drinks them.</summary>
        public Combatant AddBot(int botId, int team, string spec, int tier, IReadOnlyDictionary<string, int> consumables, IServerBot brain = null)
        {            var cls = FightLab.ClassOf(spec);
            var c = AddPlayer(botId, team, cls, spec, PlayerStats.For(cls, tier, Descriptor.Settings.Combat));
            if (c == null) return null;
            _bots.Add(brain ?? new FightOnlyBot(botId));
            _botIds.Add(botId);
            if (consumables != null) { _consumablesCarried[botId] = consumables.ToDictionary(kv => kv.Key, kv => kv.Value); RefreshCarry(botId); }
            return c;
        }

        /// <summary>A client's intent for this tick. The transport has already matched the connection to the player id.</summary>
        public void SetIntent(int playerId, PlayerIntent intent)
        {
            if (intent.PlayerId != playerId) return;
            // A one-press item choice not applied yet survives a newer intent that carries none (the client sends faster than some tick rates).
            if (!intent.AnyItemAction && _intents.TryGetValue(playerId, out var pending) && pending.AnyItemAction)
            { intent.TakeItemId = pending.TakeItemId; intent.DropItemId = pending.DropItemId; intent.RollItemId = pending.RollItemId; intent.RollChoice = pending.RollChoice; intent.SalvageItemId = pending.SalvageItemId; }
            // The same for a potion press and a shrine choice: Apply clears both once used, so carrying them never repeats them.
            if (_intents.TryGetValue(playerId, out var earlier))
            {
                if (intent.ConsumableSlot == 0 && earlier.ConsumableSlot != 0) intent.ConsumableSlot = earlier.ConsumableSlot;
                if (intent.ShrineAction == 0 && earlier.ShrineAction != 0) { intent.ShrineAction = earlier.ShrineAction; intent.ShrineArg = earlier.ShrineArg; }
            }
            _intents[playerId] = intent;
        }

        /// <summary>One server tick.</summary>
        public void Step(float dt)
        {
            if (!Running) return;
            Tick++;
            Elapsed += dt;
            // Disarmed hazards and shut doors are the state's; the simulator reads them.
            Sim.DisarmedHazards.Clear(); foreach (int h in State.DisarmedHazards) Sim.DisarmedHazards.Add(h);

            foreach (var bot in _bots)
            {
                var c = Sim.Get(bot.PlayerId);
                if (c == null || c.Dead) continue;
                var want = bot.Think(this, dt);
                if (want.HasValue) _intents[bot.PlayerId] = want.Value; else _intents.Remove(bot.PlayerId);
            }
            foreach (var kv in _intents.ToList()) Apply(kv.Key, kv.Value, dt);
            Elite?.Step(Sim, dt);
            Director.Step(dt);

            // The simulator's deaths become match events first, stamped when they happened, so the log never runs backwards.
            var produced = Director.Drain(_eventCursor, id => _lastRoom.TryGetValue(id, out int r) ? r : 0).ToList();
            RaiseAll(produced);
            // A hit on a channelling player breaks the channel.
            for (int i = _eventCursor; i < Sim.Events.Count; i++)
            {
                var e = Sim.Events[i];
                if (e.Kind == FightEventKind.Hit && e.Amount > 0f && _channels.TryGetValue(e.Target, out var ch) && FightRules.LootInterruptedByDamage) Interrupt(e.Target, ch);
            }
            _eventCursor = Sim.Events.Count;
            TickShrineChannels();
            // The clock's events, stamped no earlier than the simulator's time.
            foreach (var e in Clock.Tick(Elapsed)) Raise(new MatchEvent(e.Kind, Math.Max(e.Time, Sim.Time), e.A, e.B, e.C));
            MidRoundEvent();
            // A disconnect past its window is a death like any other (2026-10-22 "Technical and production" #10; 2026-10-26 settlement #6): the corpse, souls and loot follow.
            if (!State.Ended)
                foreach (int gone in State.ReconnectExpired(Sim.Time))
                    if (!State.DeadPlayers.Contains(gone) && !State.ExtractedPlayers.Contains(gone))
                        Raise(new MatchEvent(MatchEventKind.PlayerDied, Sim.Time, gone, -1, _lastRoom.TryGetValue(gone, out int dr) ? dr : 0));

            // Rooms entered, rooms seen.
            foreach (var c in Sim.Combatants.Values.Where(c => !c.IsMob && c.Alive))
            {
                var cell = c.Position.Cell;
                int room = Layout.InBounds(cell) ? Layout.RoomIdAt[cell.X, cell.Y] : -1;
                if (room >= 0 && (!_lastRoom.TryGetValue(c.Id, out int last) || last != room)) { _lastRoom[c.Id] = room; Raise(new MatchEvent(MatchEventKind.PlayerEnteredRoom, Sim.Time, c.Id, room)); }
            }
            _sightTimer += dt;
            if (_sightTimer >= 0.5f)
            {
                _sightTimer = 0f;
                int range = Descriptor.Rules.SightRangeCells;
                foreach (var team in State.PlayerTeam.Values.Distinct().ToList())
                {
                    var seen = State.Explored.TryGetValue(team, out var ex) ? ex : new HashSet<int>();
                    foreach (var c in Sim.Combatants.Values.Where(c => !c.IsMob && c.Alive && c.Team == team))
                        foreach (int room in LineOfSight.VisibleRooms(Layout, c.Position.Cell, range, Sim.ClosedDoors))
                            if (!seen.Contains(room)) Raise(new MatchEvent(MatchEventKind.RoomSeen, Sim.Time, team, room));
                }
            }
            ResolveRolls();
            // Players the round state says are dead or extracted leave the simulator.
            foreach (var c in Sim.Combatants.Values.Where(c => !c.IsMob && !c.Dead).ToList())
                if (State.DeadPlayers.Contains(c.Id) || State.ExtractedPlayers.Contains(c.Id)) { c.Dead = true; c.Downed = false; _channels.Remove(c.Id); _intents.Remove(c.Id); }
            if (Wire != null) foreach (var team in State.PlayerTeam.Values.Distinct().ToList()) Wire.SendSnapshotToTeam(team, SnapshotForTeam(team).ToBytes());
            if (State.Ended) Running = false;
            Ticked?.Invoke(this);
        }

        private void Apply(int playerId, PlayerIntent intent, float dt)
        {
            var c = Sim.Get(playerId);
            if (c == null || c.Dead) return;
            if (!State.DoorsOpen && !Descriptor.Rules.IsTutorial) return;   // sealed in the spawn until the countdown ends
            if (c.Downed) return;
            if (intent.ConsumableSlot > 0)
            {
                int index = intent.ConsumableSlot - 1;   // 1-based on the wire; 0 is "none"
                var wanted = index < Consumable.All.Count ? Consumable.All[index] : null;
                if (wanted != null && wanted.Name != Consumable.SalvageKitName) UseConsumable(playerId, wanted);
                intent.ConsumableSlot = 0; _intents[playerId] = intent;   // one press, one use: a held intent must not drink again
            }
            if (intent.AnyItemAction)
            {
                // The player's choices go through the same validated events as Space does (ItemTaken, ItemDropped, LootRolled).
                if (intent.RollItemId != 0) AnswerRoll(playerId, intent.RollItemId, (LootRollChoice)intent.RollChoice);
                if (intent.TakeItemId != 0) TakeItem(playerId, intent.TakeItemId);
                if (intent.DropItemId != 0) DropItem(playerId, intent.DropItemId);
                if (intent.SalvageItemId != 0) SalvageItem(playerId, intent.SalvageItemId);
                intent.TakeItemId = intent.DropItemId = intent.RollItemId = intent.SalvageItemId = 0; intent.RollChoice = 0;
                _intents[playerId] = intent;   // one press, one action: a held intent must not repeat it
            }
            if (intent.ShrineAction != 0)
            {
                // A shrine choice (arm, resurrect) starts praying at the altar within reach; the same choice again just continues it.
                UseShrine(playerId, (ShrineAction)intent.ShrineAction, intent.ShrineArg);
                intent.ShrineAction = 0; intent.ShrineArg = 0;
                _intents[playerId] = intent;   // one press, one use
            }
            // Walking away from the altar breaks a prayer (2026-10-26 owner #2); any other channel holds the player still instead.
            if (intent.Move.Length > 0.05f && _channels.TryGetValue(playerId, out var praying) && praying.Kind == ShrineChannel) Interrupt(playerId, praying);
            var aim = intent.Aim;
            if (intent.Move.Length > 0.05f && !_channels.ContainsKey(playerId)) Sim.Move(playerId, c.Position + intent.Move.Normalized * 10f, dt);
            else if ((aim - c.Position).Length > 0.1f) c.Facing = (aim - c.Position).Normalized;
            // Casts, one per tick, by slot.
            if (intent.AnyCast && c.CanAct)
            {
                var book = AbilityBook.For(_spec[playerId]).ToList();
                AbilityDefinition want = intent.Ultimate ? book.FirstOrDefault(a => a.Kind == AbilityKind.Ultimate) : intent.Zone ? book.First(a => a.Kind == AbilityKind.Zone) : intent.Mobility ? book.First(a => a.Kind == AbilityKind.Mobility) : intent.Bolt ? book.First(a => a.Kind == AbilityKind.Bolt) : book.First(a => a.Kind == AbilityKind.Basic);
                if (want != null)
                {
                    int charges = State.UltimateCharges.TryGetValue(playerId, out int ch) ? ch : 0;
                    if (want.Kind == AbilityKind.Ultimate) c.UltimateArmed = Sim.IsDevCaster(playerId) || charges > 0;
                    int eventsBefore = Sim.Events.Count;
                    var r = Sim.Cast(playerId, want.Id, want.Shape == AbilityShape.Self ? c.Position : aim);
                    // Last Breath spends its charge through its own LastBreath event (raised from the simulator's Revived); spending it here as well would be refused.
                    bool lastBreath = false;
                    for (int i = eventsBefore; i < Sim.Events.Count && !lastBreath; i++) lastBreath = Sim.Events[i].Kind == FightEventKind.Revived && Sim.Events[i].Source == playerId && Sim.Events[i].Detail == "lastbreath";
                    if (r.Ok && want.Kind == AbilityKind.Ultimate && charges > 0 && !lastBreath) Raise(new MatchEvent(MatchEventKind.UltimateUsed, Sim.Time, playerId));   // a dev's free ultimate spends nothing
                    if (r.Ok && _channels.TryGetValue(playerId, out var open)) Interrupt(playerId, open);
                }
            }
            if (intent.Ping && Wire != null && State.PlayerTeam.TryGetValue(playerId, out int team)) Wire.SendPingToTeam(team, new TeamPing(playerId, team, (PingKind)intent.PingKind, aim.Cell, Sim.Time).ToBytes());
            // Channels: hold to continue, release to stop. Releasing also re-arms the instant door close.
            if (intent.Interact) Interact(playerId, c);
            else
            {
                _heldSinceDoorOpened.Remove(playerId);
                // A prayer is started by a click on the shrine panel, not a held key: letting go of Space does not end it (moving, casting or a hit does).
                if (_channels.TryGetValue(playerId, out var held) && held.Kind != ShrineChannel) Interrupt(playerId, held);
            }
        }

        private void Interrupt(int playerId, Channel ch)
        {
            _channels.Remove(playerId);
            // The state drops a channel itself when the player is downed or dies; then there is nothing left to interrupt.
            switch (ch.Kind)
            {
                case "extract": if (State.Channels.ContainsKey(playerId)) Raise(new MatchEvent(MatchEventKind.ExtractInterrupted, Sim.Time, playerId)); break;
                case "revive": if (State.Revives.ContainsKey(playerId)) Raise(new MatchEvent(MatchEventKind.ReviveInterrupted, Sim.Time, playerId)); break;
                default: if (State.Loots.ContainsKey(playerId)) Raise(new MatchEvent(MatchEventKind.LootInterrupted, Sim.Time, playerId)); break;
            }
        }

        /// <summary>What a bot or a client can expect Space to do where this player stands, without doing it. Null when nothing is in reach.</summary>
        public string InteractionAvailable(int playerId)
        {
            var c = Sim.Get(playerId);
            return c == null ? null : Resolve(playerId, c, false);
        }

        /// <summary>What Space means where you stand: the nearest thing you can work, by priority. Starts the channel on first press, completes it when held long enough.</summary>
        private void Interact(int playerId, Combatant c)
        {
            if (_channels.TryGetValue(playerId, out var ch))
            {
                if (ch.Kind == ShrineChannel) return;   // Space keeps a prayer going; TickShrineChannels completes it
                if (Sim.Time - ch.Started < ch.Seconds) return;
                _channels.Remove(playerId);
                // Two players can hold the same thing; the second to finish finds it done and simply lets go.
                switch (ch.Kind)
                {
                    case "chest": if (!State.OpenedContainers.Contains(ch.Target)) Raise(new MatchEvent(MatchEventKind.ContainerOpened, Sim.Time, ch.Target, playerId)); break;
                    case "corpse":
                        // The owner may have been resurrected mid-loot: the body stays where it lay, re-keyed RemainsOf(owner).
                        int body = State.Corpses.ContainsKey(ch.Target) ? ch.Target : MatchState.RemainsOf(ch.Target);
                        if (State.Corpses.ContainsKey(body) && !State.OpenedCorpses.Contains(body)) Raise(new MatchEvent(MatchEventKind.CorpseOpened, Sim.Time, body, playerId));
                        break;
                    case "key": if (!State.KeyHolder.ContainsKey(ch.Target)) Raise(new MatchEvent(MatchEventKind.KeyPickedUp, Sim.Time, ch.Target, playerId)); break;
                    case "door":
                        if (State.Index.Doors.TryGetValue(ch.Target, out var d) && State.ClosedDoors.Contains(d.cell) && Raise(new MatchEvent(MatchEventKind.DoorOpened, Sim.Time, ch.Target, playerId)))
                            _heldSinceDoorOpened[playerId] = ch.Target;   // the key is still down; it must not shut what it just opened
                        break;
                    case "hazard": if (!State.DisarmedHazards.Contains(ch.Target)) Raise(new MatchEvent(MatchEventKind.HazardDisarmed, Sim.Time, ch.Target, playerId)); break;
                    case "revive": if (State.Downed.ContainsKey(ch.Target) && Raise(new MatchEvent(MatchEventKind.PlayerRevived, Sim.Time, ch.Target, playerId))) Sim.Revive(Sim.Get(ch.Target), FightRules.ReviveHealthFraction); break;
                    case "extract": Raise(new MatchEvent(MatchEventKind.PlayerExtracted, Sim.Time, playerId, ch.Target, State.PlayerTeam[playerId])); break;
                }
                return;
            }
            Resolve(playerId, c, true);
        }

        /// <summary>True while <paramref name="room"/> is the vault the mid-round event unsealed and its minute is open: anyone in it may open the lock without the key.</summary>
        public bool VaultUnsealedAt(int room) => State.EventOpen && State.ActiveEvent == DungeonEventKind.VaultUnsealed && State.EventRoom == room && Sim != null && Sim.Time < Clock.Plan.EndsAt;

        /// <summary>
        /// The server's half of the mid-round event (<see cref="DungeonEventPlan"/>): the clock raises EventStarted and EventEnded, this puts the roaming elite
        /// on the floor when its event starts, tells the state which room it walks into (<see cref="MatchEventKind.EliteMoved"/>), and raises
        /// <see cref="MatchEventKind.EliteKilled"/> when it falls: the state drops the relic on that room's floor. The unsealed vault and the cheap shrine need
        /// nothing here: their windows live in the state and Space reads them.
        /// </summary>
        private void MidRoundEvent()
        {
            if (State.ActiveEvent != DungeonEventKind.RoamingElite || State.EventOver) return;
            if (Elite == null && !_eliteSpawned)
            {
                _eliteSpawned = true;
                var room = Layout.Rooms[State.EventRoom];
                var pos = Vec2.Of(room.Center);
                var mob = Sim.Add(Combatant.Mob(EliteRules.CombatantId, EncounterDirector.MobTeam, EliteRules.Archetype, EliteRules.PowerMultiplier, Director.Baseline, pos));
                Elite = new EliteController(mob, room.Id, Layout, new DungeonRandom(Descriptor.Seed).Fork(11));
                return;
            }
            if (Elite == null) return;
            if (Elite.RoomId != State.EliteRoom) Raise(new MatchEvent(MatchEventKind.EliteMoved, Sim.Time, Elite.RoomId));
            // The elite shoulders open the shut door in its way (the state lets this one mob do it; the door stays open behind it).
            if (Elite.PendingDoor.HasValue && !Elite.Mob.Dead)
            {
                var cell = Elite.PendingDoor.Value;
                var door = State.Index.Doors.Where(kv => kv.Value.cell == cell).Select(kv => kv.Key).FirstOrDefault();
                if (door != 0 && Vec2.Distance(Vec2.Of(cell), Elite.Mob.Position) <= 1.6f) Raise(new MatchEvent(MatchEventKind.DoorOpened, Sim.Time, door, EliteRules.CombatantId));
            }
            if (Elite.Mob.Dead)
            {
                int killer = -1, killerTeam = -1;
                for (int i = Sim.Events.Count - 1; i >= 0; i--)
                {
                    var fe = Sim.Events[i];
                    if (fe.Kind == FightEventKind.Died && fe.Target == EliteRules.CombatantId) { killer = fe.Source; break; }
                }
                var by = killer >= 0 ? Sim.Get(killer) : null;
                if (by != null && !by.IsMob) killerTeam = by.Team;
                Raise(new MatchEvent(MatchEventKind.EliteKilled, Sim.Time, Elite.RoomId, killerTeam));
                Elite = null;
                return;
            }
        }
        private bool _eliteSpawned;

        /// <summary>The interaction priority list. With act=false only names what would happen.</summary>
        private string Resolve(int playerId, Combatant c, bool act)
        {
            var layout = Layout;
            var cell = c.Position.Cell;
            int room = layout.InBounds(cell) ? layout.RoomIdAt[cell.X, cell.Y] : -1;
            bool Start(string kind, int target, float seconds, MatchEvent? begin)
            {
                if (!act) return true;
                if (begin.HasValue && !Raise(begin.Value)) return false;
                _channels[playerId] = new Channel { Kind = kind, Target = target, Started = Sim.Time, Seconds = seconds };
                return true;
            }
            // 1. A door within reach: shut it now, or start opening it.
            var door = State.Index.Doors.Where(kv => Vec2.Distance(Vec2.Of(kv.Value.cell), c.Position) <= 1.3f && State.PlayerRoom.TryGetValue(playerId, out int standingIn) && State.Index.RoomsWithin(kv.Value.room, DoorRules.ReachRooms).Contains(standingIn)).OrderBy(kv => Vec2.Distance(Vec2.Of(kv.Value.cell), c.Position)).FirstOrDefault();
            bool justOpenedAndStillHeld = door.Key != 0 && _heldSinceDoorOpened.TryGetValue(playerId, out int opened) && opened == door.Key && !State.ClosedDoors.Contains(door.Value.cell);
            if (door.Key != 0 && !justOpenedAndStillHeld)
            {
                if (State.ClosedDoors.Contains(door.Value.cell)) { Start("door", door.Key, DoorRules.OpenSeconds, new MatchEvent(MatchEventKind.LootStarted, Sim.Time, playerId, door.Key, 4)); return "door-open"; }
                if (!act || Raise(new MatchEvent(MatchEventKind.DoorClosed, Sim.Time, door.Key, playerId))) return "door-close";
            }
            // 2. A downed teammate.
            var downed = Sim.Combatants.Values.FirstOrDefault(o => o.Downed && !o.Dead && o.Team == c.Team && o.Id != c.Id && State.Downed.ContainsKey(o.Id) && Vec2.Distance(o.Position, c.Position) <= 1.5f);   // the state must list them too: the simulator can down someone a moment before the bridge tells the state
            if (downed != null) { Start("revive", downed.Id, FightRules.ReviveChannelSeconds, new MatchEvent(MatchEventKind.ReviveStarted, Sim.Time, playerId, downed.Id)); return "revive"; }
            // 3. A corpse.
            var corpse = Sim.Combatants.Values.FirstOrDefault(o => o.Dead && !o.IsMob && State.Corpses.ContainsKey(o.Id) && Vec2.Distance(o.Position, c.Position) <= 1.5f);
            if (corpse != null)
            {
                if (!State.OpenedCorpses.Contains(corpse.Id)) { Start("corpse", corpse.Id, FightRules.CorpseLootSeconds, new MatchEvent(MatchEventKind.LootStarted, Sim.Time, playerId, corpse.Id, 1)); return "corpse"; }
                var item = State.Corpses[corpse.Id].FirstOrDefault();
                if (item != 0) { if (act) Raise(new MatchEvent(MatchEventKind.ItemTaken, Sim.Time, item, playerId)); return "corpse-item"; }
                if (State.CorpseSilver.ContainsKey(corpse.Id)) { if (act) Raise(new MatchEvent(MatchEventKind.SilverLooted, Sim.Time, playerId, corpse.Id)); return "corpse-silver"; }
            }
            if (room < 0) return null;
            var r = layout.Rooms[room];
            // 4. A hazard a Shade can disarm.
            var hazard = r.Hazards.FirstOrDefault(h => h.Disarmable && !State.DisarmedHazards.Contains(h.Id) && Vec2.Distance(Vec2.Of(h.Bounds.Center), c.Position) <= 1.5f);
            if (hazard != null && HazardRules.CanDisarm(State.ClassOf(playerId), hazard)) { Start("hazard", hazard.Id, HazardRules.DisarmSeconds, new MatchEvent(MatchEventKind.LootStarted, Sim.Time, playerId, hazard.Id, 3)); return "hazard"; }
            // 5. A key lying here (a boss's key only once the boss is down).
            var key = layout.Keys.FirstOrDefault(k => k.HolderRoomId == room && !State.KeyHolder.ContainsKey(k.KeyId) && !State.DestroyedKeys.Contains(k.KeyId) && (!k.DroppedByBoss || State.DeadBosses.Contains(room)));
            if (key != null) { Start("key", key.KeyId, FightRules.KeyPickupSeconds, new MatchEvent(MatchEventKind.LootStarted, Sim.Time, playerId, room, 2)); return "key"; }
            // 6. The vault door, if you hold its key, or while the mid-round event has its lock failed.
            if (r.Type == RoomType.Vault && !State.OpenVaults.Contains(room) && (VaultUnsealedAt(room) || layout.Keys.Any(k => k.VaultRoomId == room && State.KeyHolder.TryGetValue(k.KeyId, out int holder) && holder == playerId)))
            { if (!act || Raise(new MatchEvent(MatchEventKind.VaultOpened, Sim.Time, room, playerId))) return "vault"; }
            // 7. The chest: open it, then take or roll. A boss room's chest waits for the boss.
            if ((r.Loot.Count > 0 || r.Silver > 0) && (!r.IsBossRoom || State.DeadBosses.Contains(room)) && (r.Type != RoomType.Vault || State.OpenVaults.Contains(room)))
            {
                if (!State.OpenedContainers.Contains(room)) { Start("chest", room, FightRules.ChestOpenSeconds, new MatchEvent(MatchEventKind.LootStarted, Sim.Time, playerId, room, 0)); return "chest"; }
                var rolling = State.OpenRolls.FirstOrDefault(kv => kv.Value.Eligible.Contains(playerId) && !kv.Value.Choices.ContainsKey(playerId));
                if (rolling.Value != null) { if (act) Raise(new MatchEvent(MatchEventKind.LootRolled, Sim.Time, rolling.Key, playerId, (int)LootRollChoice.Need)); return "roll"; }
                var item = State.VisibleItemsIn(room).FirstOrDefault(i => !State.OpenRolls.ContainsKey(i) && (!State.LootWinner.TryGetValue(i, out int w) || w < 0 || w == playerId));
                if (item != 0) { if (act) Raise(new MatchEvent(MatchEventKind.ItemTaken, Sim.Time, item, playerId)); return "item"; }
                if (!State.SilverTakenRooms.Contains(room) && r.Silver > 0) { if (act) Raise(new MatchEvent(MatchEventKind.SilverPicked, Sim.Time, playerId, room)); return "silver"; }
            }
            // 7b. An item lying on the floor of this room (dropped, a relic): the same reach as TakeItemId, after the chest's items.
            var floor = State.FloorItems.Where(kv => kv.Value == room && !State.DestroyedItems.Contains(kv.Key) && !State.ItemOwner.ContainsKey(kv.Key)).Select(kv => kv.Key).OrderBy(i => i).FirstOrDefault();
            if (floor != 0) { if (act) Raise(new MatchEvent(MatchEventKind.ItemTaken, Sim.Time, floor, playerId)); return "floor-item"; }
            // 8. Leaving: an extract room with the gates open, or the centre when the exit is open.
            bool gate = r.Type == RoomType.Extract && State.ExtractsOpen, exit = r.Type == RoomType.FinalBoss && State.BossExitOpen;
            if (gate || exit) { Start("extract", room, Descriptor.Rules.ExtractChannelSeconds, new MatchEvent(MatchEventKind.ExtractStarted, Sim.Time, playerId, room)); return "extract"; }
            return null;
        }

        // ---------------- shrines (PlayerIntent.ShrineAction / ShrineArg) ----------------

        /// <summary>The channel kind of a prayer at a shrine's altar (<see cref="ChannelOf"/>, the HUD's channel bar).</summary>
        public const string ShrineChannel = "shrine";

        private readonly Dictionary<int, string> _shrineRefusals = new Dictionary<int, string>();

        /// <summary>Why this player's last shrine choice was refused or broken off, or null if it went through (or none was made).</summary>
        public string ShrineRefusal(int playerId) => _shrineRefusals.TryGetValue(playerId, out var why) ? why : null;

        /// <summary>
        /// What the shrine this player stands at offers them now, and why not for each choice (<see cref="ShrineRules.OptionsAt"/>): arm the ultimate,
        /// resurrect each dead teammate. Read from the cell the player is on (not the last room the state heard of): anywhere in the shrine room shows the
        /// shrine, only within <see cref="ShrineRules.ReachCells"/> of its altar can a choice be prayed for; not while downed or channelling something else.
        /// While a prayer runs, <see cref="ShrineOptions.Praying"/> names it with its progress.
        /// </summary>
        public ShrineOptions ShrineOptionsFor(int playerId)
        {
            var c = Sim?.Get(playerId);
            _channels.TryGetValue(playerId, out var ch);
            bool praying = ch != null && ch.Kind == ShrineChannel;
            string blocked = c == null || c.Dead || !State.PlayersInside.Contains(playerId) ? "not in the round" : c.Downed ? "downed" : ch != null ? (praying ? ShrineRules.Praying : "busy channelling") : null;
            var o = c == null ? ShrineRules.OptionsFor(State, playerId, -1, blocked, false) : ShrineRules.OptionsAt(State, playerId, c.Position.Cell, blocked);
            if (praying)
            {
                var what = ShrineRules.OptionsAt(State, playerId, c.Position.Cell).Find(ch.Action, ch.Arg);
                o.Praying = new ShrineChoice { Action = ch.Action, Arg = ch.Arg, Label = what?.Label ?? ch.Action.ToString(), Cost = what?.Cost ?? 0 };
                o.PrayingFraction = Math.Max(0f, Math.Min(1f, (Sim.Time - ch.Started) / ch.Seconds));
            }
            return o;
        }

        /// <summary>
        /// A shrine choice (what <see cref="PlayerIntent.ShrineAction"/> does): checked against <see cref="ShrineOptionsFor"/>, then the prayer starts at the
        /// altar (<see cref="MatchEventKind.LootStarted"/> kind <see cref="MatchState.ShrineChannelKind"/>, <see cref="ShrineRules.ChannelSeconds"/>). It is held
        /// like looting and broken like it: walking, casting or a hit ends it (<see cref="MatchEventKind.LootInterrupted"/>), as does leaving the altar's reach.
        /// When it completes the validated event is raised (<see cref="MatchEventKind.UltimateArmed"/>, <see cref="MatchEventKind.PlayerResurrected"/>).
        /// The same choice again while it runs continues it; a different one starts over. The retired buy byte is refused ("no shop at shrines").
        /// True when the prayer started or continues; false with <see cref="ShrineRefusal"/> set when refused.
        /// </summary>
        public bool UseShrine(int playerId, ShrineAction action, int arg)
        {
            if (Sim == null) return false;
            if (action == ShrineAction.RetiredBuy) { _shrineRefusals[playerId] = ShrineRules.NoShop; return false; }
            if (_channels.TryGetValue(playerId, out var running) && running.Kind == ShrineChannel)
            {
                var body = Sim.Get(playerId);
                bool still = body != null && body.Alive && !State.Downed.ContainsKey(playerId) && State.Loots.TryGetValue(playerId, out var loot) && loot.kind == MatchState.ShrineChannelKind;
                if (still && running.Action == action && running.Arg == arg) return true;   // the same choice again: keep praying
                Interrupt(playerId, running);                                                // a different one (or a prayer that can no longer go on) starts over
            }
            var o = ShrineOptionsFor(playerId);
            var choice = o.Find(action, arg);
            string why = choice == null ? (action == ShrineAction.Resurrect ? "not a dead teammate" : "the shrine offers no such thing") : choice.Refusal;
            if (why == null && !Raise(new MatchEvent(MatchEventKind.LootStarted, Sim.Time, playerId, o.ShrineRoom, MatchState.ShrineChannelKind))) why = LastRejection();
            if (why != null) { _shrineRefusals[playerId] = why; return false; }
            _shrineRefusals.Remove(playerId);
            _channels[playerId] = new Channel { Kind = ShrineChannel, Target = o.ShrineRoom, Started = Sim.Time, Seconds = ShrineRules.ChannelSeconds, Action = action, Arg = arg };
            return true;
        }

        private string LastRejection() => RecentRejections.Count > 0 ? RecentRejections[RecentRejections.Count - 1] : "refused";

        /// <summary>Every tick, after damage broke what it broke: drop prayers whose player went down or out, break those that left the altar's reach, complete the rest that are due.</summary>
        private void TickShrineChannels()
        {
            foreach (var kv in _channels.Where(kv => kv.Value.Kind == ShrineChannel).ToList())
            {
                int p = kv.Key; var ch = kv.Value;
                var body = Sim.Get(p);
                if (body == null || body.Dead || body.Downed || !State.Loots.TryGetValue(p, out var loot) || loot.kind != MatchState.ShrineChannelKind) { Interrupt(p, ch); continue; }
                if (!ShrineRules.InReach(Layout, ch.Target, body.Position.Cell)) { Interrupt(p, ch); _shrineRefusals[p] = "moved away from the altar"; continue; }
                if (Sim.Time - ch.Started < ch.Seconds - 0.0001f) continue;
                CompleteShrine(p, ch);
            }
        }

        /// <summary>The prayer is done: check the choice again (a teammate may have been brought back meanwhile, souls spent) and raise its event.</summary>
        private void CompleteShrine(int playerId, Channel ch)
        {
            _channels.Remove(playerId);
            var o = ShrineOptionsFor(playerId);
            var choice = o.Find(ch.Action, ch.Arg);
            string why = choice == null ? (ch.Action == ShrineAction.Resurrect ? "not a dead teammate" : "the shrine offers no such thing") : choice.Refusal;
            Vec2? lay = ch.Action == ShrineAction.Resurrect ? Sim.Get(ch.Arg)?.Position : null;
            var ev = ShrineRules.EventFor(ch.Action, ch.Arg, playerId, ch.Target, Sim.Time);
            if (why == null && (ev == null || !Raise(ev.Value))) why = ev == null ? "the shrine offers no such thing" : LastRejection();
            if (why != null)
            {
                _shrineRefusals[playerId] = why;
                if (State.Loots.TryGetValue(playerId, out var loot) && loot.kind == MatchState.ShrineChannelKind) Raise(new MatchEvent(MatchEventKind.LootInterrupted, Sim.Time, playerId));
                return;
            }
            _shrineRefusals.Remove(playerId);
            if (ch.Action == ShrineAction.ArmUltimate) { var body = Sim.Get(playerId); if (body != null) body.UltimateArmed = true; }   // a bot's fight brain casts from the body
            else { LeaveRemains(ch.Arg, lay); StandUpAtShrine(ch.Arg, ch.Target); }
        }

        /// <summary>
        /// A bot's fight brain casts on the simulator directly, so its armed ultimate is spent there, not through an intent: when the body has used
        /// it (the simulator clears the flag) and the state still counts a charge, raise <see cref="MatchEventKind.UltimateUsed"/>. Returns true if it did.
        /// </summary>
        public bool SettleUltimateCast(int playerId)
        {
            var body = Sim?.Get(playerId);
            if (body == null || body.Dead || body.Downed || body.UltimateArmed) return false;
            if (!State.UltimateCharges.TryGetValue(playerId, out int charges) || charges <= 0) return false;
            return Raise(new MatchEvent(MatchEventKind.UltimateUsed, Sim.Time, playerId));
        }

        /// <summary>
        /// The resurrected player's old body stays where it lay (2026-10-26 owner #1): a dead combatant keyed <see cref="MatchState.RemainsOf"/> at that spot,
        /// so Space, the loot panel, bots and team snapshots find the corpse (which the state re-keyed the same way) like any other.
        /// </summary>
        private void LeaveRemains(int playerId, Vec2? at)
        {
            int id = MatchState.RemainsOf(playerId);
            var body = Sim.Get(playerId);
            if (!State.Corpses.ContainsKey(id) || body == null || body.Stats == null || Sim.Get(id) != null) return;
            var remains = Combatant.Player(id, body.Team, body.Stats, at ?? body.Position, body.Spec);
            remains.Dead = true; remains.Health = 0f;
            Sim.Add(remains);
        }

        /// <summary>The resurrected body: up at the shrine, full health and resource, nothing on it (its corpse, with everything that was on it, stays where it lay).</summary>
        private void StandUpAtShrine(int playerId, int room)
        {
            var body = Sim.Get(playerId);
            if (body == null) return;
            body.Dead = false; body.Downed = false; body.FinishDamage = 0f;
            body.Position = AltarSpot(room);
            // Naked: a loadout player's sheet was rebuilt from what they wear (nothing) when the event was raised; anyone else gets the bare sheet, keeping their spec.
            if (body.Class != null && !_broughtBy.ContainsKey(playerId) && _spec.TryGetValue(playerId, out var spec))
                body.ApplyStats(PlayerStats.For(body.Class, WornBy(playerId), Descriptor.Settings.Combat), spec);
            body.Health = body.MaxHealth; body.ResourceNow = body.ResourceMax;
            body.Status.Cleanse(); body.Buffs.Clear(); body.DotBase.Clear(); body.DotPending = 0f; body.Threat.Clear(); body.CooldownUntil.Clear();
            body.UltimateArmed = false; body.ChannelName = null; body.ChannelUntil = -1f;
            _channels.Remove(playerId); _intents.Remove(playerId); _lastRoom[playerId] = room;
            RefreshCarry(playerId);
        }

        /// <summary>
        /// Where to stand at a shrine room's altar: the walkable cell beside the altar (within <see cref="ShrineRules.ReachCells"/>) nearest the room's centre,
        /// else the nearest walkable cell of the room. A resurrected player stands up here; bots walk here to pray.
        /// </summary>
        public Vec2 AltarSpot(int room)
        {
            var r = Layout.Rooms[room];
            var centre = Vec2.Of(ShrineRules.AltarCell(r));
            for (int ring = 1; ring <= 4; ring++)
                for (int dx = -ring; dx <= ring; dx++)
                    for (int dy = -ring; dy <= ring; dy++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring) continue;
                        var p = centre + new Vec2(dx, dy);
                        if (Layout.InBounds(p.Cell) && Layout.RoomIdAt[p.Cell.X, p.Cell.Y] == room && Sim.Walkable(p)) return p;
                    }
            return centre;
        }

        // ---------------- item choices (PlayerIntent.TakeItemId / DropItemId / RollItemId) ----------------

        /// <summary>How close a corpse must be to work it, as Space uses.</summary>
        public const float CorpseReach = 1.5f;

        private int CellRoom(Combatant c) { var cell = c.Position.Cell; return Layout.InBounds(cell) ? Layout.RoomIdAt[cell.X, cell.Y] : -1; }

        private bool CanHandle(int playerId, out Combatant c, out string why)
        {
            c = Sim?.Get(playerId); why = null;
            if (c == null || c.Dead || !State.PlayersInside.Contains(playerId)) { why = "not in the round"; return false; }
            if (c.Downed || State.Downed.ContainsKey(playerId)) { why = "downed"; return false; }
            if (_channels.ContainsKey(playerId)) { why = "busy channelling"; return false; }
            return true;
        }

        /// <summary>
        /// Why this player cannot take this item where they stand right now, or null. The server's half of the check (reach: the
        /// chest's room, the corpse within <see cref="CorpseReach"/>, the floor's room; not while channelling or downed); the state still
        /// decides on the event itself (opened, untaken, the group roll, a teammate's win).
        /// </summary>
        public string TakeRefusal(int playerId, int itemId)
        {
            if (!CanHandle(playerId, out var c, out string why)) return why;
            if (State.DestroyedItems.Contains(itemId)) return "destroyed";
            if (State.ItemOwner.TryGetValue(itemId, out int owner)) return owner == playerId ? "already yours" : "someone holds it";
            foreach (var kv in State.Corpses)
            {
                if (!kv.Value.Contains(itemId)) continue;
                if (!State.OpenedCorpses.Contains(kv.Key)) return "the corpse is not opened";
                var body = Sim.Get(kv.Key);
                if (body == null || Vec2.Distance(body.Position, c.Position) > CorpseReach) return "not at that corpse";
                return null;
            }
            int here = CellRoom(c);
            if (State.FloorItems.TryGetValue(itemId, out int floorRoom)) return here == floorRoom ? null : "not in that room";
            if (!State.Index.ItemRoom.TryGetValue(itemId, out int room)) return "no such item";
            if (here != room) return "not in that room";
            if (!State.VisibleItemsIn(room).Contains(itemId)) return State.OpenedContainers.Contains(room) ? "already taken" : "the chest is not opened";
            if (State.OpenRolls.TryGetValue(itemId, out var roll) && roll.Eligible.Contains(playerId)) return "your group is rolling for it";
            if (State.LootWinner.TryGetValue(itemId, out int won) && won >= 0 && won != playerId && State.PlayersInside.Contains(won) && TeamOf(won) == TeamOf(playerId)) return "a teammate won it";
            return null;
        }

        /// <summary>Take this item (what <see cref="PlayerIntent.TakeItemId"/> does): an <see cref="MatchEventKind.ItemTaken"/> if the server's reach check passes; the state validates the rest.</summary>
        public bool TakeItem(int playerId, int itemId) => TakeRefusal(playerId, itemId) == null && Raise(new MatchEvent(MatchEventKind.ItemTaken, Sim.Time, itemId, playerId));

        /// <summary>Why this player cannot drop this item now, or null.</summary>
        public string DropRefusal(int playerId, int itemId)
        {
            if (!CanHandle(playerId, out _, out string why)) return why;
            if (!State.ItemOwner.TryGetValue(itemId, out int owner) || owner != playerId) return "not yours";
            return null;
        }

        /// <summary>Drop a carried item on the floor of the room the player stands in (<see cref="PlayerIntent.DropItemId"/>): an <see cref="MatchEventKind.ItemDropped"/>.</summary>
        public bool DropItem(int playerId, int itemId) => DropRefusal(playerId, itemId) == null && Raise(new MatchEvent(MatchEventKind.ItemDropped, Sim.Time, itemId, playerId));

        private readonly Dictionary<int, MaterialBag> _salvaged = new Dictionary<int, MaterialBag>();

        /// <summary>Materials this player broke items down into with a Salvage Kit this round and still holds: credited on extraction (<see cref="Profile.RoundSettlement"/>), gone with a death like the pockets.</summary>
        public MaterialBag MaterialsSalvagedBy(int playerId) => _salvaged.TryGetValue(playerId, out var b) ? b : new MaterialBag();

        /// <summary>Why this player cannot break this item down now, or null: they must hold it and a Salvage Kit, and it must yield something (starter gear salvages to nothing).</summary>
        public string SalvageRefusal(int playerId, int itemId)
        {
            if (!CanHandle(playerId, out _, out string why)) return why;
            if (!State.ItemOwner.TryGetValue(itemId, out int owner) || owner != playerId) return "not yours";
            var item = ItemById(itemId);
            if (item == null) return "no such item";
            if (item.IsStarter || item.Condition.IsDestroyed) return "it salvages to nothing";
            if (!PocketsOf(playerId).TryGetValue(Consumable.SalvageKitName, out int kits) || kits <= 0) return "no Salvage Kit";
            return null;
        }

        /// <summary>
        /// Break a carried item down inside the dungeon (<see cref="PlayerIntent.SalvageItemId"/>): one Salvage Kit is spent, the item is destroyed through a
        /// validated <see cref="MatchEventKind.ItemDestroyed"/>, and <see cref="CraftingRules.Salvage"/>'s materials are recorded for settlement
        /// (<see cref="Profile.Inventory.SalvageInField"/> is the same rule on the profile's side).
        /// </summary>
        public bool SalvageItem(int playerId, int itemId)
        {
            if (SalvageRefusal(playerId, itemId) != null) return false;
            var bag = CraftingRules.Salvage(ItemById(itemId));
            if (!Raise(new MatchEvent(MatchEventKind.ItemDestroyed, Sim.Time, itemId))) return false;
            UseConsumable(playerId, Consumable.ByName(Consumable.SalvageKitName));
            if (!_salvaged.TryGetValue(playerId, out var mine)) _salvaged[playerId] = mine = new MaterialBag();
            mine.Add(bag);
            return true;
        }

        /// <summary>Why this player cannot answer this roll, or null.</summary>
        public string RollRefusal(int playerId, int itemId, LootRollChoice choice)
        {
            if (!State.OpenRolls.TryGetValue(itemId, out var roll)) return "no roll open";
            if (!roll.Eligible.Contains(playerId) || !State.PlayersInside.Contains(playerId)) return "not in this roll";
            if (roll.Choices.ContainsKey(playerId)) return "already answered";
            if ((byte)choice > (byte)LootRollChoice.Pass) return "no such choice";
            return null;
        }

        /// <summary>Answer a group roll (<see cref="PlayerIntent.RollItemId"/> + <see cref="PlayerIntent.RollChoice"/>): a <see cref="MatchEventKind.LootRolled"/>; eligibility was fixed when the chest opened.</summary>
        public bool AnswerRoll(int playerId, int itemId, LootRollChoice choice) => RollRefusal(playerId, itemId, choice) == null && Raise(new MatchEvent(MatchEventKind.LootRolled, Sim.Time, itemId, playerId, (int)choice));

        /// <summary>
        /// Everything one player's item panels may show (<see cref="PlayerItems"/>): items in reach with the reason any cannot be taken,
        /// what they hold (worn marked) and its weight against their carry capacity, and the rolls they are in with the seconds left.
        /// </summary>
        public PlayerItems ItemsFor(int playerId)
        {
            var v = new PlayerItems { PlayerId = playerId };
            var c = Sim?.Get(playerId);
            if (c == null) return v;
            v.Interaction = c.Dead ? null : InteractionAvailable(playerId);
            v.Room = CellRoom(c);
            void Add(int id, LootSourceKind src)
            {
                var item = ItemById(id);
                if (item == null) return;
                v.Here.Add(new LootChoice
                {
                    ItemId = id, Item = item, Source = src, InRoll = State.OpenRolls.ContainsKey(id),
                    Winner = State.LootWinner.TryGetValue(id, out int w) ? w : -1, Refusal = TakeRefusal(playerId, id),
                });
            }
            if (!c.Dead)
            {
                var corpse = Sim.Combatants.Values.Where(o => o.Dead && !o.IsMob && State.OpenedCorpses.Contains(o.Id) && State.Corpses.ContainsKey(o.Id) && Vec2.Distance(o.Position, c.Position) <= CorpseReach)
                    .OrderBy(o => Vec2.Distance(o.Position, c.Position)).FirstOrDefault();
                if (corpse != null) { v.Corpse = corpse.Id; foreach (int id in State.Corpses[corpse.Id].OrderBy(i => i)) Add(id, LootSourceKind.Corpse); }
                if (v.Room >= 0)
                {
                    foreach (int id in State.VisibleItemsIn(v.Room).OrderBy(i => i)) Add(id, LootSourceKind.Chest);
                    foreach (var kv in State.FloorItems.Where(kv => kv.Value == v.Room).OrderBy(kv => kv.Key)) Add(kv.Key, LootSourceKind.Floor);
                }
            }
            foreach (var kv in State.ItemOwner.Where(kv => kv.Value == playerId).OrderBy(kv => _worn.Contains(kv.Key) ? 0 : 1).ThenBy(kv => kv.Key))
                v.Carried.Add(new CarriedItem { ItemId = kv.Key, Item = ItemById(kv.Key), Worn = _worn.Contains(kv.Key) });
            v.LoadKg = LoadKgOf(playerId); v.CapacityKg = c.Stats?.CarryKg ?? 0f;
            var cls = State.ClassOf(playerId);
            foreach (var kv in State.OpenRolls.Where(kv => kv.Value.Eligible.Contains(playerId)).OrderBy(kv => kv.Key))
            {
                var item = ItemById(kv.Key);
                v.Rolls.Add(new RollPrompt
                {
                    ItemId = kv.Key, Item = item, SecondsLeft = Math.Max(0f, LootRollRules.RollSeconds - (Sim.Time - kv.Value.Opened)),
                    Answered = kv.Value.Choices.TryGetValue(playerId, out var ch) ? ch : (LootRollChoice?)null,
                    Eligible = kv.Value.Eligible.Count, Answers = kv.Value.Choices.Count, OnClass = item != null && LootRollRules.FitsClass(cls, item.Base),
                });
            }
            return v;
        }

        /// <summary>The worn ids (brought in worn, still held, never handed off) as an <see cref="Equipment"/> for previews: what a tooltip compares against.</summary>
        public Equipment WornBy(int playerId) =>
            EquipPreview.FromWorn(State.ItemOwner.Where(kv => kv.Value == playerId && _worn.Contains(kv.Key)).OrderBy(kv => kv.Key).Select(kv => ItemById(kv.Key)));

        /// <summary>True if this id was brought in worn by its holder and has not changed hands since.</summary>
        public bool IsWorn(int itemId) => _worn.Contains(itemId);

        /// <summary>Resolve any group roll that has all its answers or ran out of time.</summary>
        private void ResolveRolls()
        {
            if (State.OpenRolls.Count == 0) return;
            foreach (var kv in State.OpenRolls.Where(kv => State.RollCanResolve(kv.Key, Sim.Time)).ToList())
            {
                var outcome = LootRollRules.Resolve(State.RollEntries(kv.Key), new DungeonRandom(unchecked(Descriptor.Seed * 31 + kv.Key + (int)Sim.Time)));
                Raise(new MatchEvent(MatchEventKind.LootRollResolved, Sim.Time, kv.Key, outcome.Winner, outcome.WinningRoll));
            }
        }

        /// <summary>What one team may see: its own members always, everyone else only inside a member's sight (closed doors and pillars block), and never an invisible enemy.</summary>
        public TeamSnapshot SnapshotForTeam(int team)
        {
            var snap = new TeamSnapshot { Tick = Tick, Time = Sim.Time };
            var members = Sim.Combatants.Values.Where(c => !c.IsMob && c.Team == team && !c.Dead).ToList();
            var visible = new HashSet<GridPoint>();
            foreach (var m in members) visible.UnionWith(LineOfSight.VisibleCells(Layout, m.Position.Cell, Descriptor.Rules.SightRangeCells, Sim.ClosedDoors));
            foreach (var c in Sim.Combatants.Values)
            {
                bool mine = c.Team == team;
                if (!mine && (c.Dead && !State.Corpses.ContainsKey(c.Id))) continue;
                if (!mine && !visible.Contains(c.Position.Cell)) continue;
                if (!mine && c.Has(BuffKind.Invisible) && !c.Buffs.Any(b => b.Kind == BuffKind.Marked && Sim.Get(b.Source)?.Team == team)) continue;
                snap.Avatars.Add(new AvatarSnapshot
                {
                    Id = c.Id, Team = c.Team, X = c.Position.X, Y = c.Position.Y, FacingX = c.Facing.X, FacingY = c.Facing.Y, HealthFraction = c.MaxHealth > 0f ? c.Health / c.MaxHealth : 0f,
                    ClassIndex = (byte)Math.Max(0, ClassArchetype.DefaultRoster.ToList().IndexOf(c.Class)), Downed = c.Downed, Dead = c.Dead, IsMob = c.IsMob,
                    ArmedUltimate = !c.IsMob && State.UltimateCharges.TryGetValue(c.Id, out int u) && u > 0, Invisible = c.Has(BuffKind.Invisible), Label = c.IsMob ? c.Name : (mine ? c.Name : (c.Class?.Name ?? "")),
                });
            }
            foreach (var m in members)
            {
                var book = AbilityBook.For(m.Spec).OrderBy(a => a.Kind).ToList();
                var cds = book.Select(a => m.CooldownUntil.TryGetValue(a.Id, out float until) ? Math.Max(0f, until - Sim.Time) : 0f).ToArray();
                _channels.TryGetValue(m.Id, out var ch);
                snap.Selves.Add(new SelfSnapshot
                {
                    PlayerId = m.Id, Health = m.Health, MaxHealth = m.MaxHealth, Resource = m.ResourceNow, MaxResource = m.ResourceMax, CooldownRemaining = cds,
                    ChannelName = ch?.Kind, ChannelFraction = ch == null ? 0f : Math.Max(0f, Math.Min(1f, (Sim.Time - ch.Started) / ch.Seconds)),
                    UltimateArmed = State.UltimateCharges.TryGetValue(m.Id, out int uu) && uu > 0, DownedRemaining = m.Downed ? Math.Max(0f, FightRules.DownedSeconds - (Sim.Time - m.DownedAt)) : 0f,
                    // For a remote client's screens (2026-10-26 online play): the spec, the pockets with cooldowns, and the item panels' data by id.
                    Spec = SpecOf(m.Id) ?? m.Spec ?? "",
                    Pockets = PocketsOf(m.Id).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value)).ToList(),
                    ConsumableCooldowns = Consumable.All.Select(cc => ConsumableCooldownRemaining(m.Id, cc)).ToArray(),
                    Items = _botIds.Contains(m.Id) ? null : SelfItems.From(ItemsFor(m.Id), id => SalvageRefusal(m.Id, id) == null),
                });
            }
            // Effects since this team's last snapshot: the casts and hits it may see, and the zones in its sight (those come every tick they live).
            var shown = new HashSet<int>(snap.Avatars.Select(a => a.Id));
            bool Sees(Vec2 p) => visible.Contains(p.Cell);
            for (int i = _castCursor.TryGetValue(team, out int c0) ? c0 : 0; i < Sim.Casts.Count; i++)
            {
                var k = Sim.Casts[i];
                if (k.CasterTeam == team || shown.Contains(k.Caster) || Sees(k.From) || Sees(k.End))
                    snap.Effects.Add(new EffectSnapshot { Kind = EffectKind.Cast, Source = k.Caster, Target = -1, Ability = k.AbilityId, X = k.From.X, Y = k.From.Y, X2 = k.End.X, Y2 = k.End.Y, Amount = k.Targets.Length });
            }
            _castCursor[team] = Sim.Casts.Count;
            for (int i = _hitCursor.TryGetValue(team, out int h0) ? h0 : 0; i < Sim.Events.Count; i++)
            {
                var e = Sim.Events[i];
                if (e.Kind != FightEventKind.Hit && e.Kind != FightEventKind.Heal) continue;
                var t = Sim.Get(e.Target); if (t == null) continue;
                if (t.Team == team || shown.Contains(e.Target) || Sees(t.Position))
                    snap.Effects.Add(new EffectSnapshot { Kind = e.Kind == FightEventKind.Heal ? EffectKind.Heal : EffectKind.Hit, Source = e.Source, Target = e.Target, Ability = e.Detail ?? "", X = t.Position.X, Y = t.Position.Y, X2 = t.Position.X, Y2 = t.Position.Y, Amount = e.Amount });
            }
            _hitCursor[team] = Sim.Events.Count;
            // Wind-ups of anything this team can see: the danger circle and when it lands.
            foreach (var c in Sim.Combatants.Values)
            {
                if (!c.Channelling || !shown.Contains(c.Id)) continue;
                var (name, targetId, radius, onTarget) = TelegraphShapes.For(c.ChannelName);
                var at = c.Position;
                if (onTarget) { var t = targetId >= 0 ? Sim.Get(targetId) : null; if (t != null) at = t.Position; }
                snap.Effects.Add(new EffectSnapshot { Kind = EffectKind.Telegraph, Source = c.Id, Target = targetId, Ability = name, X = c.Position.X, Y = c.Position.Y, X2 = at.X, Y2 = at.Y, Amount = radius, Until = c.ChannelUntil });
            }
            foreach (var z in Sim.Zones)
                if (z.CasterTeam == team || Sees(z.Centre))
                    snap.Effects.Add(new EffectSnapshot { Kind = EffectKind.Zone, Source = z.Caster, Target = -1, Ability = z.Ability.Id, X = z.Centre.X, Y = z.Centre.Y, X2 = z.Centre.X, Y2 = z.Centre.Y, Amount = z.Ability.Size, Until = z.Until });
            return snap;
        }

        public RoundSummary SummaryFor(int playerId) => RoundSummary.For(State, playerId);
    }

    /// <summary>The dev practice mode's bot: stands still until something hostile is near, then fights with the lab brain.</summary>
    public sealed class FightOnlyBot : IServerBot
    {
        public int PlayerId { get; }
        public FightOnlyBot(int playerId) { PlayerId = playerId; }
        public PlayerIntent? Think(MatchServer server, float dt)
        {
            var c = server.Sim.Get(PlayerId);
            if (c == null || !c.Alive || !server.State.DoorsOpen) return null;
            SimpleBrain.Act(server.Sim, c, dt);
            return null;
        }
    }
}
