using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DungeonGen.Core.Net
{
    public enum MatchEventKind : byte
    {
        RoundStarted,      // t
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
        RoundEnded,        // t
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
                case MatchEventKind.ExtractsOpened:
                    if (!Started) { rejection = "not started"; return false; }
                    ExtractsOpen = true; break;
                case MatchEventKind.ContainerOpened:
                    if (e.A < 0 || e.A >= Layout.Rooms.Count) { rejection = "no such room"; return false; }
                    OpenedContainers.Add(e.A); break;
                case MatchEventKind.ItemTaken:
                    if (!Index.Items.ContainsKey(e.A)) { rejection = "no such item"; return false; }
                    if (ItemOwner.ContainsKey(e.A)) { rejection = "already taken"; return false; }
                    if (!OpenedContainers.Contains(Index.ItemRoom[e.A])) { rejection = "container not opened"; return false; }
                    if (DeadPlayers.Contains(e.B)) { rejection = "player is dead"; return false; }
                    ItemOwner[e.A] = e.B; break;
                case MatchEventKind.KeyPickedUp:
                    {
                        var key = Layout.Keys.FirstOrDefault(k => k.KeyId == e.A);
                        if (key == null) { rejection = "no such key"; return false; }
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
                        ExtractedPlayers.Add(e.A); break;
                    }
                case MatchEventKind.RoundEnded:
                    Ended = true; break;
            }
            Time = Math.Max(Time, e.Time);
            Log.Add(e);
            return true;
        }

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
