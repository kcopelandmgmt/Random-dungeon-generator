using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DungeonGen.Core.Net
{
    public enum MatchEventKind : byte
    {
        RoundStarted,      // t
        PlayerSpawned,     // playerId, team
        ExtractsOpened,    // t
        ContainerOpened,   // roomId, byTeam: the server reveals the room's items to that team
        ItemTaken,         // itemId, byPlayer
        KeyPickedUp,       // keyId, byPlayer
        VaultOpened,       // roomId, byPlayer (consumes nothing; the key stays an item)
        MobPackCleared,    // packId
        BossKilled,        // roomId, byTeam
        BossExitOpened,    // -
        PlayerDied,        // playerId, killerPlayerId (or -1), roomId
        PlayerExtracted,   // playerId, roomId
        PlayerResurrected, // playerId, byPlayerId, shrineRoomId  (costs the team SoulsPerResurrection; the player returns naked)
        ItemRegistered,    // itemId (>= BroughtItemIdBase, server-assigned), ownerPlayer, value: gear a player brought in from their stash
        ItemDestroyed,     // itemId: trashed on death (the server rolls which half)
        CorpseOpened,      // playerId (the dead one), byPlayer: anyone may loot a corpse
        ExtractStarted,    // playerId, roomId: the channel begins
        ExtractInterrupted,// playerId: took damage, channel resets
        RoomSeen,          // team, roomId: the team's minimap gains the room (server derives from LineOfSight)
        ShrinePurchase,    // playerId, consumableIndex, shrineRoomId: one soul from the team for a consumable
        PlayerDowned,      // playerId, byPlayerId (-1 mob), roomId: zero health; 60 s to be revived or finished, else dies
        ReviveStarted,     // reviverId, downedId: 8 s channel, broken by damage to the reviver
        ReviveInterrupted, // reviverId
        PlayerRevived,     // downedId, reviverId: channel complete, stands up at 30% health
        PlayerFinished,    // downedId, byPlayerId: took 50% of max health while downed → dead (then PlayerDied semantics apply)
        LootStarted,       // playerId, roomId or corpse playerId (B), kind (C: 0 chest, 1 corpse, 2 key): the channel begins
        LootInterrupted,   // playerId
        DoorsOpened,       // t: the 10 s countdown ended; spawn doors unseal for everyone
        SilverPicked,      // playerId, roomId: takes the room's silver, once (needs an open container channel for chests; mob piles are free)
        SilverLooted,      // playerId, corpseOf: takes everything on the corpse's purse (corpse must be opened)
        UltimateArmed,     // playerId, shrineRoomId: two of the team's souls arm the spec's ultimate for one use
        UltimateUsed,      // playerId
        LastBreath,        // reviverId, downedId: the Warder's ultimate; instant revive, consumes the charge, never on a finished player
        PlayerDisconnected,// playerId: the character stands still, killable; the reconnect window starts
        PlayerReconnected, // playerId: within the window; the client replays the log
        RoundEnded,        // t: anyone still inside dies and drops everything
    }

    /// <summary>
    /// One thing that happened, as the server decided it. Clients apply the same events in the same
    /// order and arrive at the same <see cref="MatchState"/>; a late joiner replays the log.
    /// </summary>
    public readonly struct MatchEvent
    {
        public readonly MatchEventKind Kind;
        public readonly int A, B, C;
        public readonly float Time;

        public MatchEvent(MatchEventKind kind, float time, int a = 0, int b = 0, int c = 0) { Kind = kind; Time = time; A = a; B = b; C = c; }

        public void Write(BinaryWriter w) { w.Write((byte)Kind); w.Write(Time); w.Write(A); w.Write(B); w.Write(C); }
        public static MatchEvent Read(BinaryReader r) { var k = (MatchEventKind)r.ReadByte(); float t = r.ReadSingle(); return new MatchEvent(k, t, r.ReadInt32(), r.ReadInt32(), r.ReadInt32()); }
        public override string ToString() => $"{Time,7:0.0}s {Kind} {A} {B} {C}";
    }

    /// <summary>
    /// Everything about a round that changes after the dungeon is built. The server owns it and
    /// mutates it only through <see cref="Apply"/>; clients mirror it by applying the same events.
    /// Invalid events (taking an item twice, opening a vault without its key) are rejected, which
    /// is where cheating attempts die on the server.
    /// </summary>
    public sealed class MatchState
    {
        public MatchDescriptor Descriptor { get; }
        public DungeonLayout Layout { get; }
        public LayoutIndex Index { get; }

        public float Time { get; private set; }
        public bool Started { get; private set; }
        public bool Ended { get; private set; }
        public bool ExtractsOpen { get; private set; }
        public bool BossExitOpen { get; private set; }
        public HashSet<int> OpenedContainers { get; } = new HashSet<int>();     // room ids
        public Dictionary<int, int> ItemOwner { get; } = new Dictionary<int, int>();   // itemId → playerId
        public Dictionary<int, int> KeyHolder { get; } = new Dictionary<int, int>();   // keyId → playerId
        public HashSet<int> OpenVaults { get; } = new HashSet<int>();
        public HashSet<int> ClearedPacks { get; } = new HashSet<int>();
        public HashSet<int> DeadBosses { get; } = new HashSet<int>();
        public HashSet<int> DeadPlayers { get; } = new HashSet<int>();
        public HashSet<int> ExtractedPlayers { get; } = new HashSet<int>();
        /// <summary>playerId → team, from PlayerSpawned.</summary>
        public Dictionary<int, int> PlayerTeam { get; } = new Dictionary<int, int>();
        /// <summary>Souls per team: one per enemy player killed. Spent at shrines.</summary>
        public Dictionary<int, int> Souls { get; } = new Dictionary<int, int>();
        public HashSet<int> ResurrectedOnce { get; } = new HashSet<int>();
        /// <summary>Keys destroyed because their holder extracted with them unused.</summary>
        public HashSet<int> DestroyedKeys { get; } = new HashSet<int>();

        public const int SoulsPerResurrection = 2;
        public const int SoulsPerShrinePurchase = 1;
        public const int BroughtItemIdBase = 100000;

        /// <summary>Value (Item.Value) of every item, generated or brought in, for extraction scoring.</summary>
        public Dictionary<int, int> ItemValue { get; } = new Dictionary<int, int>();
        /// <summary>playerId → items lying on that player's corpse.</summary>
        public Dictionary<int, HashSet<int>> Corpses { get; } = new Dictionary<int, HashSet<int>>();
        public HashSet<int> OpenedCorpses { get; } = new HashSet<int>();
        public HashSet<int> DestroyedItems { get; } = new HashSet<int>();
        /// <summary>playerId → (room, time) of a running extraction channel.</summary>
        public Dictionary<int, (int room, float started)> Channels { get; } = new Dictionary<int, (int, float)>();
        /// <summary>team → rooms on its minimap.</summary>
        public Dictionary<int, HashSet<int>> Explored { get; } = new Dictionary<int, HashSet<int>>();
        /// <summary>playerId → time they went down. Alive-but-helpless until revived, finished, or the timer runs out.</summary>
        public Dictionary<int, float> Downed { get; } = new Dictionary<int, float>();
        /// <summary>reviverId → (downedId, started).</summary>
        public Dictionary<int, (int downed, float started)> Revives { get; } = new Dictionary<int, (int, float)>();
        /// <summary>playerId → (target, kind, started) for a loot channel in progress.</summary>
        public Dictionary<int, (int target, int kind, float started)> Loots { get; } = new Dictionary<int, (int, int, float)>();
        public bool DoorsOpen { get; private set; }
        /// <summary>playerId → silver carried. Weightless; all of it drops on death.</summary>
        public Dictionary<int, int> PlayerSilver { get; } = new Dictionary<int, int>();
        public HashSet<int> SilverTakenRooms { get; } = new HashSet<int>();
        public Dictionary<int, int> CorpseSilver { get; } = new Dictionary<int, int>();
        public Dictionary<int, int> ExtractedSilver { get; } = new Dictionary<int, int>();
        /// <summary>playerId → uses of the ultimate remaining this round.</summary>
        public Dictionary<int, int> UltimateCharges { get; } = new Dictionary<int, int>();
        /// <summary>playerId → time the connection dropped. The server kills the character when the window passes.</summary>
        public Dictionary<int, float> Disconnected { get; } = new Dictionary<int, float>();
        /// <summary>playerId → total value walked out with.</summary>
        public Dictionary<int, long> ExtractedValue { get; } = new Dictionary<int, long>();
        public int? FinalBossKillerTeam { get; private set; }
        public List<MatchEvent> Log { get; } = new List<MatchEvent>();

        public MatchState(MatchDescriptor descriptor, DungeonLayout layout)
        {
            Descriptor = descriptor; Layout = layout; Index = new LayoutIndex(layout);
        }

        /// <summary>Validate and apply. Returns false (and changes nothing) if the event is not legal in the current state.</summary>
        public bool Apply(MatchEvent e, out string rejection)
        {
            rejection = null;
            if (Ended && e.Kind != MatchEventKind.RoundEnded) { rejection = "round over"; return false; }
            switch (e.Kind)
            {
                case MatchEventKind.RoundStarted:
                    if (Started) { rejection = "already started"; return false; }
                    Started = true; break;
                case MatchEventKind.PlayerSpawned:
                    if (PlayerTeam.ContainsKey(e.A)) { rejection = "already spawned"; return false; }
                    if (e.B < 0 || e.B >= Descriptor.TeamSpawnRooms.Count) { rejection = "no such team"; return false; }
                    if (PlayerTeam.Count(kv => kv.Value == e.B) >= Descriptor.Rules.TeamSize) { rejection = "team full"; return false; }
                    PlayerTeam[e.A] = e.B;
                    if (!Explored.ContainsKey(e.B)) Explored[e.B] = new HashSet<int>();
                    Explored[e.B].Add(Descriptor.TeamSpawnRooms[e.B]);
                    break;
                case MatchEventKind.ItemRegistered:
                    if (e.A < BroughtItemIdBase) { rejection = "brought-in item ids start at 100000"; return false; }
                    if (ItemValue.ContainsKey(e.A)) { rejection = "item id in use"; return false; }
                    if (!PlayerTeam.ContainsKey(e.B)) { rejection = "unknown player"; return false; }
                    ItemValue[e.A] = e.C; ItemOwner[e.A] = e.B; break;
                case MatchEventKind.ItemDestroyed:
                    if (!ItemOwner.ContainsKey(e.A) && !Corpses.Values.Any(c => c.Contains(e.A))) { rejection = "no such item"; return false; }
                    ItemOwner.Remove(e.A);
                    foreach (var c in Corpses.Values) c.Remove(e.A);
                    DestroyedItems.Add(e.A); break;
                case MatchEventKind.CorpseOpened:
                    {
                        if (!Corpses.ContainsKey(e.A)) { rejection = "no corpse"; return false; }
                        if (DeadPlayers.Contains(e.B)) { rejection = "player is dead"; return false; }
                        if (!OpenedCorpses.Contains(e.A))
                        {
                            if (!Loots.TryGetValue(e.B, out var lc) || lc.kind != 1 || lc.target != e.A) { rejection = "no corpse channel"; return false; }
                            if (e.Time - lc.started < Combat.FightRules.CorpseLootSeconds - 0.01f) { rejection = $"corpse takes {Combat.FightRules.CorpseLootSeconds:0} s"; return false; }
                            Loots.Remove(e.B);
                        }
                        OpenedCorpses.Add(e.A); break;
                    }
                case MatchEventKind.ExtractStarted:
                    {
                        if (DeadPlayers.Contains(e.A) || ExtractedPlayers.Contains(e.A) || Downed.ContainsKey(e.A)) { rejection = "player cannot extract"; return false; }
                        var room = Layout.Rooms[e.B];
                        bool viaBoss = Layout.BossExit != null && room.Id == Layout.BossExit.RoomId;
                        if (!viaBoss && room.Type != RoomType.Extract) { rejection = "not an extract"; return false; }
                        if (!viaBoss && !ExtractsOpen) { rejection = "extracts not open yet"; return false; }
                        if (viaBoss && !BossExitOpen) { rejection = "boss exit closed"; return false; }
                        Channels[e.A] = (e.B, e.Time); break;
                    }
                case MatchEventKind.ExtractInterrupted:
                    if (!Channels.Remove(e.A)) { rejection = "not channelling"; return false; }
                    break;
                case MatchEventKind.RoomSeen:
                    if (e.B < 0 || e.B >= Layout.Rooms.Count) { rejection = "no such room"; return false; }
                    if (!Explored.ContainsKey(e.A)) Explored[e.A] = new HashSet<int>();
                    Explored[e.A].Add(e.B); break;
                case MatchEventKind.ShrinePurchase:
                    {
                        if (DeadPlayers.Contains(e.A) || !PlayerTeam.TryGetValue(e.A, out int team)) { rejection = "player not alive"; return false; }
                        if (e.B < 0 || e.B >= Items.Consumable.All.Count) { rejection = "no such consumable"; return false; }
                        if (e.C < 0 || e.C >= Layout.Rooms.Count || !Layout.Rooms[e.C].HasShrine) { rejection = "no shrine there"; return false; }
                        if (!Souls.TryGetValue(team, out int souls) || souls < SoulsPerShrinePurchase) { rejection = "team has no souls"; return false; }
                        Souls[team] = souls - SoulsPerShrinePurchase; break;
                    }
                case MatchEventKind.ExtractsOpened:
                    if (!Started) { rejection = "not started"; return false; }
                    ExtractsOpen = true; break;
                case MatchEventKind.ContainerOpened:
                    {
                        if (e.A < 0 || e.A >= Layout.Rooms.Count) { rejection = "no such room"; return false; }
                        if (!OpenedContainers.Contains(e.A))
                        {
                            if (!Loots.TryGetValue(e.B, out var lc) || lc.kind != 0 || lc.target != e.A) { rejection = "no chest channel"; return false; }
                            if (e.Time - lc.started < Combat.FightRules.ChestOpenSeconds - 0.01f) { rejection = $"chest takes {Combat.FightRules.ChestOpenSeconds:0} s"; return false; }
                            Loots.Remove(e.B);
                        }
                        OpenedContainers.Add(e.A); break;
                    }
                case MatchEventKind.ItemTaken:
                    {
                        if (DeadPlayers.Contains(e.B)) { rejection = "player is dead"; return false; }
                        if (DestroyedItems.Contains(e.A)) { rejection = "item destroyed"; return false; }
                        var corpse = Corpses.FirstOrDefault(kv => kv.Value.Contains(e.A));
                        if (corpse.Value != null)
                        {
                            if (!OpenedCorpses.Contains(corpse.Key)) { rejection = "corpse not opened"; return false; }
                            corpse.Value.Remove(e.A);
                            ItemOwner[e.A] = e.B; break;
                        }
                        if (!Index.Items.ContainsKey(e.A)) { rejection = "no such item"; return false; }
                        if (ItemOwner.ContainsKey(e.A)) { rejection = "already taken"; return false; }
                        if (!OpenedContainers.Contains(Index.ItemRoom[e.A])) { rejection = "container not opened"; return false; }
                        ItemOwner[e.A] = e.B;
                        if (!ItemValue.ContainsKey(e.A)) ItemValue[e.A] = Index.Items[e.A].Value;
                        break;
                    }
                case MatchEventKind.KeyPickedUp:
                    {
                        var key = Layout.Keys.FirstOrDefault(k => k.KeyId == e.A);
                        if (key == null) { rejection = "no such key"; return false; }
                        if (DestroyedKeys.Contains(e.A)) { rejection = "key was destroyed"; return false; }
                        if (!Loots.TryGetValue(e.B, out var kc) || kc.kind != 2) { rejection = "no key pickup channel"; return false; }
                        if (e.Time - kc.started < Combat.FightRules.KeyPickupSeconds - 0.01f) { rejection = $"key takes {Combat.FightRules.KeyPickupSeconds:0} s"; return false; }
                        Loots.Remove(e.B);
                        if (key.DroppedByBoss && !DeadBosses.Contains(key.HolderRoomId)) { rejection = "boss still alive"; return false; }
                        KeyHolder[e.A] = e.B; break;
                    }
                case MatchEventKind.VaultOpened:
                    {
                        var vault = Layout.Rooms[e.A];
                        if (vault.Type != RoomType.Vault) { rejection = "not a vault"; return false; }
                        if (!KeyHolder.TryGetValue(vault.RequiredKeyId, out int holder) || holder != e.B) { rejection = "player does not hold the key"; return false; }
                        OpenVaults.Add(e.A); break;
                    }
                case MatchEventKind.MobPackCleared:
                    if (!Index.MobPacks.ContainsKey(e.A)) { rejection = "no such pack"; return false; }
                    ClearedPacks.Add(e.A); break;
                case MatchEventKind.BossKilled:
                    {
                        var room = Layout.Rooms[e.A];
                        if (!room.IsBossRoom) { rejection = "not a boss room"; return false; }
                        if (DeadBosses.Contains(e.A)) { rejection = "already dead"; return false; }
                        DeadBosses.Add(e.A);
                        if (room.Type == RoomType.FinalBoss) FinalBossKillerTeam = e.B;
                        break;
                    }
                case MatchEventKind.BossExitOpened:
                    if (Layout.FinalBossRoom == null || !DeadBosses.Contains(Layout.FinalBossRoom.Id)) { rejection = "final boss alive"; return false; }
                    BossExitOpen = true; break;
                case MatchEventKind.PlayerDied:
                    if (DeadPlayers.Contains(e.A) || ExtractedPlayers.Contains(e.A)) { rejection = "player already out"; return false; }
                    DeadPlayers.Add(e.A);
                    Channels.Remove(e.A); Loots.Remove(e.A); Downed.Remove(e.A); Revives.Remove(e.A);
                    Disconnected.Remove(e.A);
                    UltimateCharges.Remove(e.A);                                   // an armed ultimate dies with you
                    if (PlayerSilver.TryGetValue(e.A, out int purse) && purse > 0) { CorpseSilver[e.A] = purse; PlayerSilver.Remove(e.A); }   // all of it
                    // Everything they held goes onto their corpse, open to anyone; the server then rolls which half of the worn gear is destroyed.
                    {
                        var held = ItemOwner.Where(kv => kv.Value == e.A).Select(kv => kv.Key).ToList();
                        foreach (int it in held) ItemOwner.Remove(it);
                        Corpses[e.A] = new HashSet<int>(held);
                    }
                    // A soul for the killer's team, only for player kills, never for teammates.
                    if (e.B >= 0 && PlayerTeam.TryGetValue(e.B, out int killerTeam) && (!PlayerTeam.TryGetValue(e.A, out int victimTeam) || victimTeam != killerTeam))
                        Souls[killerTeam] = (Souls.TryGetValue(killerTeam, out int sv) ? sv : 0) + 1;
                    // Their keys fall where they stand; the server re-issues KeyPickedUp when someone grabs them.
                    foreach (var k in KeyHolder.Where(kv => kv.Value == e.A).Select(kv => kv.Key).ToList()) KeyHolder.Remove(k);
                    break;
                case MatchEventKind.PlayerExtracted:
                    {
                        if (DeadPlayers.Contains(e.A) || ExtractedPlayers.Contains(e.A)) { rejection = "player already out"; return false; }
                        var room = Layout.Rooms[e.B];
                        bool viaBossExit = Layout.BossExit != null && room.Id == Layout.BossExit.RoomId;
                        if (viaBossExit)
                        {
                            if (!BossExitOpen) { rejection = "boss exit closed"; return false; }
                            if (!Descriptor.Rules.BossExitStaysOpen && FinalBossKillerTeam != e.C) { rejection = "boss exit belongs to the killing team"; return false; }
                        }
                        else
                        {
                            if (room.Type != RoomType.Extract) { rejection = "not an extract"; return false; }
                            if (!ExtractsOpen) { rejection = "extracts not open yet"; return false; }
                        }
                        // The channel: started here, undamaged since, and long enough ago.
                        if (!Channels.TryGetValue(e.A, out var ch) || ch.room != e.B) { rejection = "no extraction channel at this gate"; return false; }
                        if (e.Time - ch.started < Descriptor.Rules.ExtractChannelSeconds - 0.01f) { rejection = $"channel needs {Descriptor.Rules.ExtractChannelSeconds:0} s"; return false; }
                        Channels.Remove(e.A);
                        ExtractedPlayers.Add(e.A);
                        ExtractedValue[e.A] = ItemOwner.Where(kv => kv.Value == e.A).Sum(kv => (long)(ItemValue.TryGetValue(kv.Key, out int v) ? v : 0));
                        if (PlayerSilver.TryGetValue(e.A, out int carried)) { ExtractedSilver[e.A] = carried; PlayerSilver.Remove(e.A); }
                        UltimateCharges.Remove(e.A);                                   // never leaves the dungeon
                        // A vault key is worthless outside this dungeon: extracting with one destroys it.
                        foreach (var k in KeyHolder.Where(kv => kv.Value == e.A).Select(kv => kv.Key).ToList()) { KeyHolder.Remove(k); DestroyedKeys.Add(k); }
                        break;
                    }
                case MatchEventKind.PlayerResurrected:
                    {
                        if (!DeadPlayers.Contains(e.A)) { rejection = "player is not dead"; return false; }
                        if (ResurrectedOnce.Contains(e.A)) { rejection = "already resurrected this round"; return false; }
                        if (!PlayerTeam.TryGetValue(e.A, out int team) || !PlayerTeam.TryGetValue(e.B, out int byTeam) || team != byTeam) { rejection = "only a teammate can resurrect"; return false; }
                        if (DeadPlayers.Contains(e.B) || ExtractedPlayers.Contains(e.B)) { rejection = "resurrector is out"; return false; }
                        if (e.C < 0 || e.C >= Layout.Rooms.Count || !Layout.Rooms[e.C].HasShrine) { rejection = "no shrine there"; return false; }
                        if (!Souls.TryGetValue(team, out int souls) || souls < SoulsPerResurrection) { rejection = $"team needs {SoulsPerResurrection} souls"; return false; }
                        Souls[team] = souls - SoulsPerResurrection;
                        DeadPlayers.Remove(e.A);
                        ResurrectedOnce.Add(e.A);
                        break;
                    }
                case MatchEventKind.DoorsOpened:
                    if (!Started) { rejection = "not started"; return false; }
                    if (e.Time < Combat.FightRules.StartCountdownSeconds - 0.01f) { rejection = $"countdown is {Combat.FightRules.StartCountdownSeconds} s"; return false; }
                    DoorsOpen = true; break;
                case MatchEventKind.PlayerDowned:
                    if (!PlayerTeam.ContainsKey(e.A) || DeadPlayers.Contains(e.A) || ExtractedPlayers.Contains(e.A)) { rejection = "player not in play"; return false; }
                    if (Downed.ContainsKey(e.A)) { rejection = "already downed"; return false; }
                    Downed[e.A] = e.Time; Channels.Remove(e.A); Loots.Remove(e.A);
                    foreach (var r in Revives.Where(kv => kv.Key == e.A).Select(kv => kv.Key).ToList()) Revives.Remove(r);   // a downed reviver stops reviving
                    break;
                case MatchEventKind.ReviveStarted:
                    {
                        if (!Downed.ContainsKey(e.B)) { rejection = "target is not downed"; return false; }
                        if (!PlayerTeam.TryGetValue(e.A, out int rt) || !PlayerTeam.TryGetValue(e.B, out int dt) || rt != dt) { rejection = "only a teammate can revive"; return false; }
                        if (DeadPlayers.Contains(e.A) || Downed.ContainsKey(e.A) || ExtractedPlayers.Contains(e.A)) { rejection = "reviver is not standing"; return false; }
                        Revives[e.A] = (e.B, e.Time); break;
                    }
                case MatchEventKind.ReviveInterrupted:
                    if (!Revives.Remove(e.A)) { rejection = "not reviving"; return false; }
                    break;
                case MatchEventKind.PlayerRevived:
                    {
                        if (!Downed.ContainsKey(e.A)) { rejection = "not downed"; return false; }
                        if (!Revives.TryGetValue(e.B, out var rv) || rv.downed != e.A) { rejection = "no revive channel"; return false; }
                        if (e.Time - rv.started < Combat.FightRules.ReviveChannelSeconds - 0.01f) { rejection = $"revive needs {Combat.FightRules.ReviveChannelSeconds:0} s"; return false; }
                        Revives.Remove(e.B); Downed.Remove(e.A); break;
                    }
                case MatchEventKind.PlayerFinished:
                    if (!Downed.ContainsKey(e.A)) { rejection = "not downed"; return false; }
                    Downed.Remove(e.A);
                    goto case MatchEventKind.PlayerDied;
                case MatchEventKind.LootStarted:
                    {
                        if (DeadPlayers.Contains(e.A) || Downed.ContainsKey(e.A) || ExtractedPlayers.Contains(e.A)) { rejection = "player cannot loot"; return false; }
                        if (e.C == 1 && !Corpses.ContainsKey(e.B)) { rejection = "no corpse"; return false; }
                        if (e.C != 1 && (e.B < 0 || e.B >= Layout.Rooms.Count)) { rejection = "no such room"; return false; }
                        Loots[e.A] = (e.B, e.C, e.Time); break;
                    }
                case MatchEventKind.LootInterrupted:
                    if (!Loots.Remove(e.A)) { rejection = "not looting"; return false; }
                    break;
                case MatchEventKind.SilverPicked:
                    {
                        if (DeadPlayers.Contains(e.A) || Downed.ContainsKey(e.A) || ExtractedPlayers.Contains(e.A)) { rejection = "player cannot loot"; return false; }
                        if (e.B < 0 || e.B >= Layout.Rooms.Count) { rejection = "no such room"; return false; }
                        if (SilverTakenRooms.Contains(e.B)) { rejection = "silver already taken"; return false; }
                        var room = Layout.Rooms[e.B];
                        if (room.Silver <= 0) { rejection = "no silver here"; return false; }
                        if (room.Loot.Count > 0 && !OpenedContainers.Contains(e.B)) { rejection = "the purse is in the chest"; return false; }
                        SilverTakenRooms.Add(e.B);
                        PlayerSilver[e.A] = (PlayerSilver.TryGetValue(e.A, out int ps) ? ps : 0) + room.Silver;
                        break;
                    }
                case MatchEventKind.SilverLooted:
                    {
                        if (DeadPlayers.Contains(e.A) || Downed.ContainsKey(e.A)) { rejection = "player cannot loot"; return false; }
                        if (!OpenedCorpses.Contains(e.B)) { rejection = "corpse not opened"; return false; }
                        if (!CorpseSilver.TryGetValue(e.B, out int cs) || cs <= 0) { rejection = "no silver on the corpse"; return false; }
                        CorpseSilver.Remove(e.B);
                        PlayerSilver[e.A] = (PlayerSilver.TryGetValue(e.A, out int ps2) ? ps2 : 0) + cs;
                        break;
                    }
                case MatchEventKind.UltimateArmed:
                    {
                        if (DeadPlayers.Contains(e.A) || Downed.ContainsKey(e.A) || !PlayerTeam.TryGetValue(e.A, out int team)) { rejection = "player not standing"; return false; }
                        if (e.B < 0 || e.B >= Layout.Rooms.Count || !Layout.Rooms[e.B].HasShrine) { rejection = "no shrine there"; return false; }
                        if (UltimateCharges.TryGetValue(e.A, out int have) && have > 0) { rejection = "already armed"; return false; }
                        if (!Souls.TryGetValue(team, out int souls) || souls < Combat.FightRules.SoulsToArmUltimate) { rejection = $"team needs {Combat.FightRules.SoulsToArmUltimate} souls"; return false; }
                        Souls[team] = souls - Combat.FightRules.SoulsToArmUltimate;
                        UltimateCharges[e.A] = Combat.FightRules.UltimateUsesPerArming;
                        break;
                    }
                case MatchEventKind.UltimateUsed:
                    if (DeadPlayers.Contains(e.A) || Downed.ContainsKey(e.A)) { rejection = "player not standing"; return false; }
                    if (!UltimateCharges.TryGetValue(e.A, out int charges) || charges <= 0) { rejection = "ultimate not armed"; return false; }
                    UltimateCharges[e.A] = charges - 1; break;
                case MatchEventKind.LastBreath:
                    {
                        if (DeadPlayers.Contains(e.A) || Downed.ContainsKey(e.A)) { rejection = "reviver not standing"; return false; }
                        if (!UltimateCharges.TryGetValue(e.A, out int lb) || lb <= 0) { rejection = "ultimate not armed"; return false; }
                        if (!Downed.ContainsKey(e.B)) { rejection = DeadPlayers.Contains(e.B) ? "finished players need a shrine" : "target is not downed"; return false; }
                        if (!PlayerTeam.TryGetValue(e.A, out int rt) || !PlayerTeam.TryGetValue(e.B, out int dt) || rt != dt) { rejection = "only a teammate"; return false; }
                        UltimateCharges[e.A] = lb - 1;
                        Downed.Remove(e.B);
                        foreach (var r in Revives.Where(kv => kv.Value.downed == e.B).Select(kv => kv.Key).ToList()) Revives.Remove(r);
                        break;
                    }
                case MatchEventKind.PlayerDisconnected:
                    if (!PlayerTeam.ContainsKey(e.A) || DeadPlayers.Contains(e.A) || ExtractedPlayers.Contains(e.A)) { rejection = "player not in play"; return false; }
                    Disconnected[e.A] = e.Time; Channels.Remove(e.A); break;
                case MatchEventKind.PlayerReconnected:
                    if (!Disconnected.TryGetValue(e.A, out float dropped)) { rejection = "was not disconnected"; return false; }
                    if (e.Time - dropped > Descriptor.Rules.ReconnectWindowSeconds) { rejection = "reconnect window passed"; return false; }
                    Disconnected.Remove(e.A); break;
                case MatchEventKind.RoundEnded:
                    // The clock is a weapon: anyone still inside dies and drops everything.
                    foreach (int p in PlayerTeam.Keys.Where(p => !DeadPlayers.Contains(p) && !ExtractedPlayers.Contains(p)).ToList())
                    {
                        DeadPlayers.Add(p);
                        Channels.Remove(p);
                        foreach (var k in KeyHolder.Where(kv => kv.Value == p).Select(kv => kv.Key).ToList()) KeyHolder.Remove(k);
                        var held = ItemOwner.Where(kv => kv.Value == p).Select(kv => kv.Key).ToList();
                        foreach (int it in held) { ItemOwner.Remove(it); DestroyedItems.Add(it); }   // nothing left behind survives the round
                        PlayerSilver.Remove(p); UltimateCharges.Remove(p);
                    }
                    Ended = true; break;
            }
            Time = Math.Max(Time, e.Time);
            Log.Add(e);
            return true;
        }

        /// <summary>Downed players whose 60 s ran out: the server should kill them (PlayerDied with killer -1).</summary>
        public IEnumerable<int> DownedExpired(float now) =>
            Downed.Where(kv => now - kv.Value > Combat.FightRules.DownedSeconds).Select(kv => kv.Key).ToList();

        /// <summary>Disconnected players whose window has passed: the server should kill them (PlayerDied with killer -1).</summary>
        public IEnumerable<int> ReconnectExpired(float now) =>
            Disconnected.Where(kv => now - kv.Value > Descriptor.Rules.ReconnectWindowSeconds).Select(kv => kv.Key).ToList();

        /// <summary>Players still alive and inside.</summary>
        public IEnumerable<int> PlayersInside => PlayerTeam.Keys.Where(p => !DeadPlayers.Contains(p) && !ExtractedPlayers.Contains(p));

        /// <summary>Rebuild a state from the descriptor and a log: what a late joiner or a reconnect does.</summary>
        public static MatchState Replay(MatchDescriptor d, DungeonLayout layout, IEnumerable<MatchEvent> log)
        {
            var s = new MatchState(d, layout);
            foreach (var e in log) if (!s.Apply(e, out string why)) throw new InvalidOperationException($"Replay rejected {e}: {why}");
            return s;
        }

        public byte[] LogToBytes()
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms)) { w.Write(Log.Count); foreach (var e in Log) e.Write(w); }
            return ms.ToArray();
        }

        public static List<MatchEvent> LogFromBytes(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var r = new BinaryReader(ms);
            int n = r.ReadInt32();
            var list = new List<MatchEvent>(n);
            for (int i = 0; i < n; i++) list.Add(MatchEvent.Read(r));
            return list;
        }

        /// <summary>Items a player may be shown: everything in containers they or their team opened that nobody has taken. The server filters per recipient with this.</summary>
        public IEnumerable<int> VisibleItemsIn(int roomId) =>
            OpenedContainers.Contains(roomId) ? Index.Items.Where(kv => Index.ItemRoom[kv.Key] == roomId && !ItemOwner.ContainsKey(kv.Key)).Select(kv => kv.Key) : Enumerable.Empty<int>();
    }
}
