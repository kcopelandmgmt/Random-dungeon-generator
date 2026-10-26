using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DungeonGen.Core.Net
{
    /// <summary>
    /// What one team is allowed to know, built by the server from the authoritative
    /// <see cref="MatchState"/> and sent to that team's clients. Clients never receive the full
    /// event log: the log carries every team's rooms, rolls, souls and loot, and a client with the
    /// log is a wallhack. A client holds a <see cref="MatchState"/> built from the redacted layout
    /// and loads views into it (<see cref="MatchState.LoadView"/>); reconnecting is just the next view.
    /// Public facts: the clock, the doors, the extracts, the centre exit, the final boss. Team facts:
    /// members, rooms, souls, silver, charges, rolls, keys. Explored facts: rooms the team has seen,
    /// with their open containers, their items, their corpses, their dead bosses and open vaults.
    /// Nothing about any room the team has not seen, and nothing about an enemy's position ever.
    /// </summary>
    public sealed class TeamView
    {
        public int Team;
        public float Time;
        public bool Started, Ended, DoorsOpen, ExtractsOpen, BossExitOpen, FinalBossDead;
        /// <summary>The mid-round event is public: everyone was told.</summary>
        public byte EventKind; public int EventRoom = -1; public float EventStartedAt = -1f; public bool EventOver, EliteDead;

        public struct Member
        {
            public int Player, Class, Room, Souls, Silver, UltimateCharges;
            public bool Dead, Downed, Extracted, Left, Disconnected;
        }
        public struct ItemInfo { public int Id, Room, Level, Value; public byte Rarity; public string Name; }
        public struct Roll { public int Item; public float Opened; public List<int> Eligible; public List<(int player, byte choice)> Choices; }
        public struct Corpse { public int Player, Room, Silver; public bool Opened, HasSoul; public List<int> Items; }

        public List<Member> Members = new List<Member>();
        public List<int> Explored = new List<int>();
        public List<int> OpenedContainers = new List<int>();   // in explored rooms
        public List<int> DeadBosses = new List<int>();         // in explored rooms
        public List<int> OpenVaults = new List<int>();         // in explored rooms
        public List<int> SilverTaken = new List<int>();        // in explored rooms
        public List<ItemInfo> Items = new List<ItemInfo>();    // untaken items in opened containers of explored rooms, and floor items there
        public List<int> FloorItems = new List<int>();
        public List<Roll> Rolls = new List<Roll>();
        public List<(int item, int winner)> Winners = new List<(int, int)>();
        public List<(int key, int holder)> Keys = new List<(int, int)>();
        public List<Corpse> Corpses = new List<Corpse>();
        /// <summary>Closed door cells in or next to explored rooms.</summary>
        public List<GridPoint> ClosedDoors = new List<GridPoint>();
        public List<int> KnownHazards = new List<int>();
        public List<int> DisarmedHazards = new List<int>();
        /// <summary>
        /// The full item behind every id the team's screens can name (2026-10-26 online play): the items listed in <see cref="Items"/>, everything a
        /// member holds, the items in opened corpses the team sees, and the items in its rolls. Each is <see cref="Profile.ProfileCodec.WriteItem"/>'s
        /// form (base, item level, rarity, name, affixes, wear), so a remote client shows base, affixes and weight like the host does. Filled only when
        /// the view is built with the host's item lookup; appended last on the wire, so an older view reads with none.
        /// </summary>
        public List<(int id, byte[] item)> ItemDetails = new List<(int, byte[])>();

        /// <summary>The item behind an id from <see cref="ItemDetails"/>, or null.</summary>
        public Items.Item DetailOf(int id)
        {
            foreach (var d in ItemDetails) if (d.id == id) return ReadItem(d.item);
            return null;
        }

        public static byte[] WriteItem(Items.Item item)
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms)) Profile.ProfileCodec.WriteItem(w, item);
            return ms.ToArray();
        }

        public static Items.Item ReadItem(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var r = new BinaryReader(ms);
            return Profile.ProfileCodec.ReadItem(r);
        }

        /// <summary>
        /// The view for one team. <paramref name="itemLookup"/> (the host's <see cref="MatchServer.ItemById"/>) lets brought-in gear that was
        /// dropped on a seen room's floor appear in <see cref="Items"/> like generated loot; without it only generated loot and the relic can.
        /// </summary>
        public static TeamView For(MatchState st, int team, Func<int, Items.Item> itemLookup = null)
        {
            var v = new TeamView { Team = team, Time = st.Time, Started = st.Started, Ended = st.Ended, DoorsOpen = st.DoorsOpen, ExtractsOpen = st.ExtractsOpen, BossExitOpen = st.BossExitOpen };
            var final = st.Layout.FinalBossRoom;
            v.FinalBossDead = final != null && st.DeadBosses.Contains(final.Id);
            v.EventKind = (byte)st.ActiveEvent; v.EventRoom = st.EventRoom; v.EventStartedAt = st.EventStartedAt; v.EventOver = st.EventOver; v.EliteDead = st.EliteDead;
            var members = st.PlayerTeam.Where(kv => kv.Value == team).Select(kv => kv.Key).OrderBy(p => p).ToList();
            foreach (int p in members)
                v.Members.Add(new Member
                {
                    Player = p, Class = st.PlayerClass.TryGetValue(p, out int c) ? c : 0, Room = st.PlayerRoom.TryGetValue(p, out int r) ? r : -1,
                    Souls = st.SoulsOf(p), Silver = st.PlayerSilver.TryGetValue(p, out int s) ? s : 0, UltimateCharges = st.UltimateCharges.TryGetValue(p, out int u) ? u : 0,
                    Dead = st.DeadPlayers.Contains(p), Downed = st.Downed.ContainsKey(p), Extracted = st.ExtractedPlayers.Contains(p), Left = st.LeftPlayers.Contains(p), Disconnected = st.Disconnected.ContainsKey(p),
                });
            var seen = st.Explored.TryGetValue(team, out var ex) ? ex : new HashSet<int>();
            v.Explored = seen.OrderBy(i => i).ToList();
            v.OpenedContainers = st.OpenedContainers.Where(seen.Contains).OrderBy(i => i).ToList();
            v.DeadBosses = st.DeadBosses.Where(seen.Contains).OrderBy(i => i).ToList();
            v.OpenVaults = st.OpenVaults.Where(seen.Contains).OrderBy(i => i).ToList();
            v.SilverTaken = st.SilverTakenRooms.Where(seen.Contains).OrderBy(i => i).ToList();
            foreach (var kv in st.Index.Items.OrderBy(kv => kv.Key))
            {
                int room = st.Index.ItemRoom[kv.Key];
                bool inOpenChest = seen.Contains(room) && st.OpenedContainers.Contains(room) && !st.ItemOwner.ContainsKey(kv.Key) && !st.FloorItems.ContainsKey(kv.Key) && !st.DestroyedItems.Contains(kv.Key) && !st.Corpses.Values.Any(cs => cs.Contains(kv.Key));
                bool onFloor = st.FloorItems.TryGetValue(kv.Key, out int fr) && seen.Contains(fr);
                if (!inOpenChest && !onFloor) continue;
                v.Items.Add(new ItemInfo { Id = kv.Key, Room = onFloor ? fr : room, Level = kv.Value.ItemLevel, Value = kv.Value.Value, Rarity = (byte)kv.Value.Rarity, Name = kv.Value.Name });
                if (onFloor) v.FloorItems.Add(kv.Key);
            }
            if (st.Layout.EventLoot != null && st.FloorItems.TryGetValue(LayoutIndex.EventLootId, out int er) && seen.Contains(er))
            {
                var relic = st.Layout.EventLoot;
                v.Items.Add(new ItemInfo { Id = LayoutIndex.EventLootId, Room = er, Level = relic.ItemLevel, Value = relic.Value, Rarity = (byte)relic.Rarity, Name = relic.Name });
                v.FloorItems.Add(LayoutIndex.EventLootId);
            }
            if (itemLookup != null)
                foreach (var kv in st.FloorItems.Where(kv => !st.Index.Items.ContainsKey(kv.Key) && kv.Key != LayoutIndex.EventLootId && seen.Contains(kv.Value)).OrderBy(kv => kv.Key))
                {
                    var it = itemLookup(kv.Key);
                    if (it == null) continue;
                    v.Items.Add(new ItemInfo { Id = kv.Key, Room = kv.Value, Level = it.ItemLevel, Value = it.Value, Rarity = (byte)it.Rarity, Name = it.Name });
                    v.FloorItems.Add(kv.Key);
                }
            foreach (var kv in st.OpenRolls.Where(kv => kv.Value.Eligible.Any(members.Contains)).OrderBy(kv => kv.Key))
                v.Rolls.Add(new Roll { Item = kv.Key, Opened = kv.Value.Opened, Eligible = kv.Value.Eligible.OrderBy(p => p).ToList(), Choices = kv.Value.Choices.OrderBy(c => c.Key).Select(c => (c.Key, (byte)c.Value)).ToList() });
            foreach (var kv in st.LootWinner.Where(kv => kv.Value >= 0 ? members.Contains(kv.Value) : st.Index.ItemRoom.TryGetValue(kv.Key, out int wr) && seen.Contains(wr)).OrderBy(kv => kv.Key))
                v.Winners.Add((kv.Key, kv.Value));
            foreach (var kv in st.KeyHolder.Where(kv => members.Contains(kv.Value)).OrderBy(kv => kv.Key)) v.Keys.Add((kv.Key, kv.Value));
            foreach (var kv in st.Corpses.OrderBy(kv => kv.Key))
            {
                int room = st.PlayerRoom.TryGetValue(kv.Key, out int cr) ? cr : -1;
                bool ours = members.Contains(kv.Key);
                if (!ours && !seen.Contains(room)) continue;
                bool opened = st.OpenedCorpses.Contains(kv.Key);
                v.Corpses.Add(new Corpse { Player = kv.Key, Room = room, Silver = st.CorpseSilver.TryGetValue(kv.Key, out int cs) ? cs : 0, Opened = opened, HasSoul = st.CorpseSouls.Contains(kv.Key), Items = opened ? kv.Value.OrderBy(i => i).ToList() : new List<int>() });
            }
            foreach (var cell in st.ClosedDoors.OrderBy(c => c.X).ThenBy(c => c.Y))
            {
                int room = st.Layout.RoomIdAt[cell.X, cell.Y];
                if (seen.Contains(room) || st.Layout.Rooms[room].Neighbours.Any(seen.Contains)) v.ClosedDoors.Add(cell);
            }
            v.KnownHazards = st.HazardsVisibleTo(team).Select(h => h.Id).OrderBy(i => i).ToList();
            v.DisarmedHazards = st.DisarmedHazards.Where(id => seen.Contains(st.Index.Hazards[id].RoomId)).OrderBy(i => i).ToList();
            if (itemLookup != null)
            {
                var ids = new SortedSet<int>(v.Items.Select(i => i.Id));
                foreach (var kv in st.ItemOwner) if (members.Contains(kv.Value)) ids.Add(kv.Key);
                foreach (var c in v.Corpses) foreach (int i in c.Items) ids.Add(i);
                foreach (var r in v.Rolls) ids.Add(r.Item);
                foreach (int id in ids)
                {
                    var it = itemLookup(id);
                    if (it != null) v.ItemDetails.Add((id, WriteItem(it)));
                }
            }
            return v;
        }

        public byte[] ToBytes()
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Team); w.Write(Time); w.Write(Started); w.Write(Ended); w.Write(DoorsOpen); w.Write(ExtractsOpen); w.Write(BossExitOpen); w.Write(FinalBossDead);
                w.Write(EventKind); w.Write(EventRoom); w.Write(EventStartedAt); w.Write(EventOver); w.Write(EliteDead);
                w.Write(Members.Count);
                foreach (var m in Members) { w.Write(m.Player); w.Write(m.Class); w.Write(m.Room); w.Write(m.Souls); w.Write(m.Silver); w.Write(m.UltimateCharges); w.Write(m.Dead); w.Write(m.Downed); w.Write(m.Extracted); w.Write(m.Left); w.Write(m.Disconnected); }
                WriteInts(w, Explored); WriteInts(w, OpenedContainers); WriteInts(w, DeadBosses); WriteInts(w, OpenVaults); WriteInts(w, SilverTaken); WriteInts(w, FloorItems);
                w.Write(Items.Count);
                foreach (var i in Items) { w.Write(i.Id); w.Write(i.Room); w.Write(i.Level); w.Write(i.Value); w.Write(i.Rarity); w.Write(i.Name ?? ""); }
                w.Write(Rolls.Count);
                foreach (var r in Rolls) { w.Write(r.Item); w.Write(r.Opened); WriteInts(w, r.Eligible); w.Write(r.Choices.Count); foreach (var c in r.Choices) { w.Write(c.player); w.Write(c.choice); } }
                w.Write(Winners.Count); foreach (var x in Winners) { w.Write(x.item); w.Write(x.winner); }
                w.Write(Keys.Count); foreach (var k in Keys) { w.Write(k.key); w.Write(k.holder); }
                w.Write(Corpses.Count);
                foreach (var c in Corpses) { w.Write(c.Player); w.Write(c.Room); w.Write(c.Silver); w.Write(c.Opened); w.Write(c.HasSoul); WriteInts(w, c.Items); }
                w.Write(ClosedDoors.Count); foreach (var d in ClosedDoors) { w.Write(d.X); w.Write(d.Y); }
                WriteInts(w, KnownHazards); WriteInts(w, DisarmedHazards);
                w.Write(ItemDetails.Count); foreach (var d in ItemDetails) { w.Write(d.id); w.Write(d.item.Length); w.Write(d.item); }
            }
            return ms.ToArray();
        }

        public static TeamView FromBytes(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var r = new BinaryReader(ms);
            var v = new TeamView { Team = r.ReadInt32(), Time = r.ReadSingle(), Started = r.ReadBoolean(), Ended = r.ReadBoolean(), DoorsOpen = r.ReadBoolean(), ExtractsOpen = r.ReadBoolean(), BossExitOpen = r.ReadBoolean(), FinalBossDead = r.ReadBoolean() };
            v.EventKind = r.ReadByte(); v.EventRoom = r.ReadInt32(); v.EventStartedAt = r.ReadSingle(); v.EventOver = r.ReadBoolean(); v.EliteDead = r.ReadBoolean();
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++) v.Members.Add(new Member { Player = r.ReadInt32(), Class = r.ReadInt32(), Room = r.ReadInt32(), Souls = r.ReadInt32(), Silver = r.ReadInt32(), UltimateCharges = r.ReadInt32(), Dead = r.ReadBoolean(), Downed = r.ReadBoolean(), Extracted = r.ReadBoolean(), Left = r.ReadBoolean(), Disconnected = r.ReadBoolean() });
            v.Explored = ReadInts(r); v.OpenedContainers = ReadInts(r); v.DeadBosses = ReadInts(r); v.OpenVaults = ReadInts(r); v.SilverTaken = ReadInts(r); v.FloorItems = ReadInts(r);
            n = r.ReadInt32();
            for (int i = 0; i < n; i++) v.Items.Add(new ItemInfo { Id = r.ReadInt32(), Room = r.ReadInt32(), Level = r.ReadInt32(), Value = r.ReadInt32(), Rarity = r.ReadByte(), Name = r.ReadString() });
            n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                var roll = new Roll { Item = r.ReadInt32(), Opened = r.ReadSingle(), Eligible = ReadInts(r), Choices = new List<(int, byte)>() };
                int cn = r.ReadInt32(); for (int j = 0; j < cn; j++) roll.Choices.Add((r.ReadInt32(), r.ReadByte()));
                v.Rolls.Add(roll);
            }
            n = r.ReadInt32(); for (int i = 0; i < n; i++) v.Winners.Add((r.ReadInt32(), r.ReadInt32()));
            n = r.ReadInt32(); for (int i = 0; i < n; i++) v.Keys.Add((r.ReadInt32(), r.ReadInt32()));
            n = r.ReadInt32();
            for (int i = 0; i < n; i++) v.Corpses.Add(new Corpse { Player = r.ReadInt32(), Room = r.ReadInt32(), Silver = r.ReadInt32(), Opened = r.ReadBoolean(), HasSoul = r.ReadBoolean(), Items = ReadInts(r) });
            n = r.ReadInt32(); for (int i = 0; i < n; i++) v.ClosedDoors.Add(new GridPoint(r.ReadInt32(), r.ReadInt32()));
            v.KnownHazards = ReadInts(r); v.DisarmedHazards = ReadInts(r);
            if (ms.Position < ms.Length)   // item details: appended 2026-10-26, absent from older views
            {
                n = r.ReadInt32();
                for (int i = 0; i < n; i++) { int id = r.ReadInt32(); int len = r.ReadInt32(); v.ItemDetails.Add((id, r.ReadBytes(len))); }
            }
            return v;
        }

        private static void WriteInts(BinaryWriter w, List<int> l) { w.Write(l.Count); foreach (int i in l) w.Write(i); }
        private static List<int> ReadInts(BinaryReader r) { int n = r.ReadInt32(); var l = new List<int>(n); for (int i = 0; i < n; i++) l.Add(r.ReadInt32()); return l; }
    }

    /// <summary>
    /// Which teams are told that an event happened. The event itself is a notification (a sound, a
    /// line of UI); the facts travel in the <see cref="TeamView"/>. Public events reach everyone;
    /// most reach only the teams involved; a few reach only one player's team. Null means everyone.
    /// </summary>
    public static class EventVisibility
    {
        public static HashSet<int> Audience(MatchEvent e, MatchState st)
        {
            HashSet<int> Teams(params int[] players)
            {
                var set = new HashSet<int>();
                foreach (int p in players) if (p >= 0 && st.PlayerTeam.TryGetValue(p, out int t)) set.Add(t);
                return set;
            }
            switch (e.Kind)
            {
                case MatchEventKind.RoundStarted: case MatchEventKind.DoorsOpened: case MatchEventKind.ExtractsOpened: case MatchEventKind.BossExitOpened: case MatchEventKind.RoundEnded:
                case MatchEventKind.EventStarted: case MatchEventKind.EventEnded:
                    return null;
                case MatchEventKind.EliteKilled:
                    return new HashSet<int> { e.B };
                case MatchEventKind.BossKilled:
                    return e.A >= 0 && e.A < st.Layout.Rooms.Count && st.Layout.Rooms[e.A].Type == RoomType.FinalBoss ? null : new HashSet<int> { e.B };
                case MatchEventKind.PlayerSpawned: case MatchEventKind.PlayerDisconnected: case MatchEventKind.PlayerReconnected: case MatchEventKind.PlayerEnteredRoom:
                case MatchEventKind.PlayerLeft: case MatchEventKind.AccountLevelReported: case MatchEventKind.ExtractStarted: case MatchEventKind.ExtractInterrupted: case MatchEventKind.PlayerExtracted:
                case MatchEventKind.LootStarted: case MatchEventKind.LootInterrupted: case MatchEventKind.SilverPicked: case MatchEventKind.UltimateArmed: case MatchEventKind.UltimateUsed:
                case MatchEventKind.ShrinePurchase: case MatchEventKind.ItemDropped:
                    return Teams(e.A);
                case MatchEventKind.PlayerDied: case MatchEventKind.PlayerDowned: case MatchEventKind.PlayerFinished:
                    return Teams(e.A, e.B);
                case MatchEventKind.PlayerResurrected: case MatchEventKind.ReviveStarted: case MatchEventKind.ReviveInterrupted: case MatchEventKind.PlayerRevived: case MatchEventKind.LastBreath:
                    return Teams(e.A, e.B);
                case MatchEventKind.ItemGiven: case MatchEventKind.KeyGiven:
                    return Teams(e.B, e.C);
                case MatchEventKind.DoorClosed: case MatchEventKind.DoorOpened:
                    {   // everyone who has explored the door's room or a neighbour hears the door
                        if (!st.Index.Doors.TryGetValue(e.A, out var d)) return new HashSet<int>();
                        var near = st.Index.RoomsWithin(d.room, 1);
                        return new HashSet<int>(st.Explored.Where(kv => kv.Value.Overlaps(near)).Select(kv => kv.Key));
                    }
                case MatchEventKind.HazardTriggered: case MatchEventKind.HazardDisarmed:
                    return Teams(e.B);
                case MatchEventKind.ContainerOpened: case MatchEventKind.VaultOpened: case MatchEventKind.ItemTaken: case MatchEventKind.KeyPickedUp: case MatchEventKind.CorpseOpened:
                case MatchEventKind.SilverLooted: case MatchEventKind.LootRolled:
                    return Teams(e.B);
                case MatchEventKind.LootRollResolved:
                    return st.LootWinner.TryGetValue(e.A, out int w) && w >= 0 ? Teams(w) : (st.Index.ItemRoom.TryGetValue(e.A, out int room) ? new HashSet<int>(st.Explored.Where(kv => kv.Value.Contains(room)).Select(kv => kv.Key)) : new HashSet<int>());
                case MatchEventKind.RoomSeen:
                    return new HashSet<int> { e.A };
                case MatchEventKind.ItemRegistered: case MatchEventKind.ItemDestroyed:
                    return st.ItemOwner.TryGetValue(e.A, out int owner) ? Teams(owner) : Teams(e.B);
                case MatchEventKind.MobPackCleared:
                    return new HashSet<int>();                        // nobody is told; the room tells you
                default:
                    return new HashSet<int>();
            }
        }
    }
}
