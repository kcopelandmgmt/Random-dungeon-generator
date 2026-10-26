using System;
using System.IO;
using DungeonGen.Core.Combat;
using System.Collections.Generic;

namespace DungeonGen.Core.Net
{
    /// <summary>
    /// What a player wants this tick, as the client sends it and the server resolves it. Movement
    /// is a direction, aim is a point on the ground in simulator cells, and each button is the
    /// intent to use that slot now. Nothing here is trusted: the server validates every one.
    /// </summary>
    public struct PlayerIntent
    {
        public int PlayerId;
        public uint Tick;
        public float MoveX, MoveY;          // -1..1, screen-relative already turned into world axes by the client
        public float AimX, AimY;            // ground point in simulator cells
        public bool Basic, Bolt, Zone, Mobility, Ultimate;
        public bool Interact;               // held: channels (chest, corpse, key, door, revive, extract, disarm) continue while held
        public bool Ping;                   // tap: a team ping at the aim point
        public byte PingKind;
        /// <summary>Use a consumable now: 0 is none, otherwise 1 + the index into <see cref="DungeonGen.Core.Items.Consumable.All"/> (1 Health Potion, 2 Antidote). Appended last on the wire; one press, one use.</summary>
        public byte ConsumableSlot;
        /// <summary>
        /// Item choices, appended after <see cref="ConsumableSlot"/>; 0 means none for each id (no item or roll has id 0). One press, one use:
        /// the server clears them after applying. <see cref="TakeItemId"/>: take this specific item in reach (chest, corpse or floor) instead of the
        /// first one Space would take. <see cref="DropItemId"/>: put this carried item on the floor of the room you stand in. <see cref="RollItemId"/>
        /// with <see cref="RollChoice"/> (a <see cref="DungeonGen.Core.Items.LootRollChoice"/>: 0 Need, 1 Greed, 2 Pass): answer that group roll.
        /// On the wire the block is written only when one of them is set, so an intent without an item action is byte-for-byte the old format,
        /// and an old payload (with or without the consumable byte) reads as "no item action".
        /// </summary>
        public int TakeItemId, DropItemId, RollItemId;
        public byte RollChoice;
        /// <summary>
        /// Break this carried item down with a Salvage Kit from your pockets (<see cref="MatchServer.SalvageItem"/>): the kit is spent, the item destroyed,
        /// its materials credited if you extract. Appended after the item block, 4 bytes written only when set (the older block before it is then written too,
        /// with zeros), so every older payload reads as "no salvage". One press, one use.
        /// </summary>
        public int SalvageItemId;

        public bool AnyItemAction => TakeItemId != 0 || DropItemId != 0 || RollItemId != 0 || SalvageItemId != 0;
        public Vec2 Move => new Vec2(MoveX, MoveY);
        public Vec2 Aim => new Vec2(AimX, AimY);
        public bool AnyCast => Basic || Bolt || Zone || Mobility || Ultimate;

        public void Write(BinaryWriter w)
        {
            w.Write(PlayerId); w.Write(Tick); w.Write(MoveX); w.Write(MoveY); w.Write(AimX); w.Write(AimY);
            byte flags = 0;
            if (Basic) flags |= 1; if (Bolt) flags |= 2; if (Zone) flags |= 4; if (Mobility) flags |= 8; if (Ultimate) flags |= 16; if (Interact) flags |= 32; if (Ping) flags |= 64;
            w.Write(flags); w.Write(PingKind); w.Write(ConsumableSlot);
            if (AnyItemAction) { w.Write(TakeItemId); w.Write(DropItemId); w.Write(RollItemId); w.Write(RollChoice); if (SalvageItemId != 0) w.Write(SalvageItemId); }
        }
        public static PlayerIntent Read(BinaryReader r)
        {
            var i = new PlayerIntent { PlayerId = r.ReadInt32(), Tick = r.ReadUInt32(), MoveX = r.ReadSingle(), MoveY = r.ReadSingle(), AimX = r.ReadSingle(), AimY = r.ReadSingle() };
            byte f = r.ReadByte(); i.PingKind = r.ReadByte(); i.ConsumableSlot = r.ReadByte();
            i.Basic = (f & 1) != 0; i.Bolt = (f & 2) != 0; i.Zone = (f & 4) != 0; i.Mobility = (f & 8) != 0; i.Ultimate = (f & 16) != 0; i.Interact = (f & 32) != 0; i.Ping = (f & 64) != 0;
            // The item block is optional: present only when the sender set an item action.
            var bs = r.BaseStream;
            if (bs.CanSeek && bs.Length - bs.Position >= ItemBlockLength) { i.TakeItemId = r.ReadInt32(); i.DropItemId = r.ReadInt32(); i.RollItemId = r.ReadInt32(); i.RollChoice = r.ReadByte(); if (bs.Length - bs.Position >= 4) i.SalvageItemId = r.ReadInt32(); }
            return i;
        }
        public byte[] ToBytes() { using var ms = new MemoryStream(); using (var w = new BinaryWriter(ms)) Write(w); return ms.ToArray(); }
        public static PlayerIntent FromBytes(byte[] b)
        {
            // An intent from before the consumable field (one byte shorter) still reads, with no consumable.
            if (b != null && b.Length == LegacyLength) { var longer = new byte[b.Length + 1]; Array.Copy(b, longer, b.Length); b = longer; }
            using var ms = new MemoryStream(b); using var r = new BinaryReader(ms); return Read(r);
        }
        private const int LegacyLength = 4 + 4 + 4 * 4 + 1 + 1;
        /// <summary>Bytes of the optional item block: three ids and the roll choice.</summary>
        public const int ItemBlockLength = 4 + 4 + 4 + 1;
    }

    /// <summary>Anything that produces intents for a player: the local input reader, a bot, a replay.</summary>
    public interface IIntentSource
    {
        int PlayerId { get; }
        bool TryGetIntent(out PlayerIntent intent);
    }

    /// <summary>
    /// One combatant as a client is allowed to see it this tick. Enemies appear only while a member of
    /// the receiving team has line of sight to them; teammates always. Health is a fraction for the bar,
    /// never a number, and nothing about an enemy's gear travels.
    /// </summary>
    public struct AvatarSnapshot
    {
        public int Id; public int Team; public float X, Y, FacingX, FacingY; public float HealthFraction; public byte ClassIndex; public bool Downed, Dead, IsMob, ArmedUltimate, Invisible; public string Label;
        public void Write(BinaryWriter w) { w.Write(Id); w.Write(Team); w.Write(X); w.Write(Y); w.Write(FacingX); w.Write(FacingY); w.Write(HealthFraction); w.Write(ClassIndex); w.Write(Downed); w.Write(Dead); w.Write(IsMob); w.Write(ArmedUltimate); w.Write(Invisible); w.Write(Label ?? ""); }
        public static AvatarSnapshot Read(BinaryReader r) => new AvatarSnapshot { Id = r.ReadInt32(), Team = r.ReadInt32(), X = r.ReadSingle(), Y = r.ReadSingle(), FacingX = r.ReadSingle(), FacingY = r.ReadSingle(), HealthFraction = r.ReadSingle(), ClassIndex = r.ReadByte(), Downed = r.ReadBoolean(), Dead = r.ReadBoolean(), IsMob = r.ReadBoolean(), ArmedUltimate = r.ReadBoolean(), Invisible = r.ReadBoolean(), Label = r.ReadString() };
    }

    /// <summary>
    /// A team member's own numbers, sent to their team (teammates already see each other's bars on the avatars): resource, cooldowns,
    /// channel progress, and, for remote clients (2026-10-26 online play), what the host's screens used to read off the server: the spec
    /// the server seats now (<see cref="Spec"/>), the pockets (<see cref="Pockets"/>) with every consumable's cooldown
    /// (<see cref="ConsumableCooldowns"/>, indexed like <see cref="DungeonGen.Core.Items.Consumable.All"/>), and the item screens' data
    /// (<see cref="Items"/>: in reach, carried, rolling, by id). Item details (base, affixes, weight) travel once per change in
    /// <see cref="TeamView.ItemDetails"/>, not every tick.
    /// </summary>
    public struct SelfSnapshot
    {
        public int PlayerId; public float Health, MaxHealth, Resource, MaxResource; public float[] CooldownRemaining; public string ChannelName; public float ChannelFraction; public bool UltimateArmed; public float DownedRemaining;
        /// <summary>The spec the server seats this player with now (a lost weapon changes it). Empty when unknown.</summary>
        public string Spec;
        /// <summary>What is in the pockets now: consumable name and count.</summary>
        public List<(string name, int count)> Pockets;
        /// <summary>Seconds until each consumable may be used again, indexed like <see cref="DungeonGen.Core.Items.Consumable.All"/>.</summary>
        public float[] ConsumableCooldowns;
        /// <summary>The item screens' data for this player, or null when the server sent none.</summary>
        public SelfItems Items;

        public int PocketCount(string consumable) { if (Pockets != null) foreach (var p in Pockets) if (p.name == consumable) return p.count; return 0; }
        public float ConsumableCooldown(string consumable)
        {
            if (ConsumableCooldowns == null) return 0f;
            var all = DungeonGen.Core.Items.Consumable.All;
            for (int i = 0; i < all.Count && i < ConsumableCooldowns.Length; i++) if (all[i].Name == consumable) return ConsumableCooldowns[i];
            return 0f;
        }

        public void Write(BinaryWriter w)
        {
            w.Write(PlayerId); w.Write(Health); w.Write(MaxHealth); w.Write(Resource); w.Write(MaxResource);
            w.Write(CooldownRemaining?.Length ?? 0); if (CooldownRemaining != null) foreach (var c in CooldownRemaining) w.Write(c);
            w.Write(ChannelName ?? ""); w.Write(ChannelFraction); w.Write(UltimateArmed); w.Write(DownedRemaining);
            w.Write(Spec ?? "");
            w.Write(Pockets?.Count ?? 0); if (Pockets != null) foreach (var p in Pockets) { w.Write(p.name ?? ""); w.Write(p.count); }
            w.Write(ConsumableCooldowns?.Length ?? 0); if (ConsumableCooldowns != null) foreach (var c in ConsumableCooldowns) w.Write(c);
            w.Write(Items != null); Items?.Write(w);
        }
        public static SelfSnapshot Read(BinaryReader r)
        {
            var s = new SelfSnapshot { PlayerId = r.ReadInt32(), Health = r.ReadSingle(), MaxHealth = r.ReadSingle(), Resource = r.ReadSingle(), MaxResource = r.ReadSingle() };
            int n = r.ReadInt32(); s.CooldownRemaining = new float[n]; for (int i = 0; i < n; i++) s.CooldownRemaining[i] = r.ReadSingle();
            s.ChannelName = r.ReadString(); s.ChannelFraction = r.ReadSingle(); s.UltimateArmed = r.ReadBoolean(); s.DownedRemaining = r.ReadSingle();
            s.Spec = r.ReadString();
            n = r.ReadInt32(); s.Pockets = new List<(string, int)>(n); for (int i = 0; i < n; i++) s.Pockets.Add((r.ReadString(), r.ReadInt32()));
            n = r.ReadInt32(); s.ConsumableCooldowns = new float[n]; for (int i = 0; i < n; i++) s.ConsumableCooldowns[i] = r.ReadSingle();
            if (r.ReadBoolean()) s.Items = SelfItems.Read(r);
            return s;
        }
    }

    /// <summary>
    /// <see cref="PlayerItems"/> by id, for the wire: what Space would do, the room and corpse in reach, the load, every item in reach with the
    /// server's refusal, what is carried (worn marked, salvage allowed), and the rolls this player is in. A remote client resolves the ids to
    /// items through the details its team's <see cref="TeamView"/> carries (<see cref="ToPlayerItems"/>).
    /// </summary>
    public sealed class SelfItems
    {
        public struct Loot { public int ItemId; public LootSourceKind Source; public bool InRoll; public int Winner; public string Refusal; }
        public struct Carry { public int ItemId; public bool Worn; public bool CanSalvage; }
        /// <summary>A roll this player is in; <see cref="Answered"/> is the <see cref="DungeonGen.Core.Items.LootRollChoice"/> as a number, -1 before they answer.</summary>
        public struct Ask { public int ItemId; public float SecondsLeft; public sbyte Answered; public int Eligible, Answers; public bool OnClass; }

        public string Interaction; public int Room = -1, Corpse = -1; public float LoadKg, CapacityKg;
        public List<Loot> Here = new List<Loot>();
        public List<Carry> Carried = new List<Carry>();
        public List<Ask> Rolls = new List<Ask>();

        /// <summary>Every item id this message names (in reach, carried, rolling).</summary>
        public IEnumerable<int> ItemIds { get { foreach (var h in Here) yield return h.ItemId; foreach (var c in Carried) yield return c.ItemId; foreach (var a in Rolls) yield return a.ItemId; } }

        /// <summary>The host's view, by id. <paramref name="canSalvage"/> answers whether the Salvage button may show for a carried id.</summary>
        public static SelfItems From(PlayerItems v, Func<int, bool> canSalvage = null)
        {
            var s = new SelfItems { Interaction = v.Interaction, Room = v.Room, Corpse = v.Corpse, LoadKg = v.LoadKg, CapacityKg = v.CapacityKg };
            foreach (var h in v.Here) s.Here.Add(new Loot { ItemId = h.ItemId, Source = h.Source, InRoll = h.InRoll, Winner = h.Winner, Refusal = h.Refusal });
            foreach (var c in v.Carried) s.Carried.Add(new Carry { ItemId = c.ItemId, Worn = c.Worn, CanSalvage = canSalvage != null && canSalvage(c.ItemId) });
            foreach (var r in v.Rolls) s.Rolls.Add(new Ask { ItemId = r.ItemId, SecondsLeft = r.SecondsLeft, Answered = r.Answered.HasValue ? (sbyte)r.Answered.Value : (sbyte)-1, Eligible = r.Eligible, Answers = r.Answers, OnClass = r.OnClass });
            return s;
        }

        /// <summary>Back to the screens' model, items resolved by <paramref name="lookup"/> (null for an id with no details yet).</summary>
        public PlayerItems ToPlayerItems(int playerId, Func<int, DungeonGen.Core.Items.Item> lookup)
        {
            var v = new PlayerItems { PlayerId = playerId, Interaction = Interaction, Room = Room, Corpse = Corpse, LoadKg = LoadKg, CapacityKg = CapacityKg };
            foreach (var h in Here) v.Here.Add(new LootChoice { ItemId = h.ItemId, Item = lookup?.Invoke(h.ItemId), Source = h.Source, InRoll = h.InRoll, Winner = h.Winner, Refusal = h.Refusal });
            foreach (var c in Carried) v.Carried.Add(new CarriedItem { ItemId = c.ItemId, Item = lookup?.Invoke(c.ItemId), Worn = c.Worn });
            foreach (var a in Rolls)
                v.Rolls.Add(new RollPrompt
                {
                    ItemId = a.ItemId, Item = lookup?.Invoke(a.ItemId), SecondsLeft = a.SecondsLeft, Eligible = a.Eligible, Answers = a.Answers, OnClass = a.OnClass,
                    Answered = a.Answered < 0 ? (DungeonGen.Core.Items.LootRollChoice?)null : (DungeonGen.Core.Items.LootRollChoice)a.Answered,
                });
            return v;
        }

        public bool CanSalvage(int itemId) { foreach (var c in Carried) if (c.ItemId == itemId) return c.CanSalvage; return false; }

        public void Write(BinaryWriter w)
        {
            w.Write(Interaction ?? ""); w.Write(Room); w.Write(Corpse); w.Write(LoadKg); w.Write(CapacityKg);
            w.Write(Here.Count);
            foreach (var h in Here) { w.Write(h.ItemId); w.Write((byte)h.Source); w.Write(h.InRoll); w.Write(h.Winner); w.Write(h.Refusal != null); if (h.Refusal != null) w.Write(h.Refusal); }
            w.Write(Carried.Count); foreach (var c in Carried) { w.Write(c.ItemId); w.Write(c.Worn); w.Write(c.CanSalvage); }
            w.Write(Rolls.Count); foreach (var a in Rolls) { w.Write(a.ItemId); w.Write(a.SecondsLeft); w.Write(a.Answered); w.Write(a.Eligible); w.Write(a.Answers); w.Write(a.OnClass); }
        }

        public static SelfItems Read(BinaryReader r)
        {
            var s = new SelfItems();
            var inter = r.ReadString(); s.Interaction = inter.Length == 0 ? null : inter;
            s.Room = r.ReadInt32(); s.Corpse = r.ReadInt32(); s.LoadKg = r.ReadSingle(); s.CapacityKg = r.ReadSingle();
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                var h = new Loot { ItemId = r.ReadInt32(), Source = (LootSourceKind)r.ReadByte(), InRoll = r.ReadBoolean(), Winner = r.ReadInt32() };
                if (r.ReadBoolean()) h.Refusal = r.ReadString();
                s.Here.Add(h);
            }
            n = r.ReadInt32(); for (int i = 0; i < n; i++) s.Carried.Add(new Carry { ItemId = r.ReadInt32(), Worn = r.ReadBoolean(), CanSalvage = r.ReadBoolean() });
            n = r.ReadInt32(); for (int i = 0; i < n; i++) s.Rolls.Add(new Ask { ItemId = r.ReadInt32(), SecondsLeft = r.ReadSingle(), Answered = r.ReadSByte(), Eligible = r.ReadInt32(), Answers = r.ReadInt32(), OnClass = r.ReadBoolean() });
            return s;
        }
    }

    /// <summary>Appended only: the byte values travel on the wire.</summary>
    public enum EffectKind : byte { Cast, Hit, Heal, Zone, Telegraph }

    /// <summary>
    /// Something to draw this tick. Cast: Source cast Ability from (X, Y) to (X2, Y2); Amount is how many it touched. Hit and Heal: Source did Amount
    /// to Target standing at (X, Y); Ability is the hit's label. Zone: Ability's zone centred at (X, Y) with radius Amount, until sim time Until; sent every tick it lives.
    /// Telegraph: Source is winding up Ability (a boss or mob channel such as "crush" or "groundslam"); the danger is a circle of radius Amount cells centred at
    /// (X2, Y2) (the target's position for a targeted wind-up, the caster's for a slam); it lands at sim time Until. Sent every tick while the wind-up lasts.
    /// Casts and hits are sent once, in the next snapshot after they happened; a dropped snapshot drops them, which is fine for a visual.
    /// </summary>
    public struct EffectSnapshot
    {
        public EffectKind Kind; public int Source, Target; public string Ability; public float X, Y, X2, Y2, Amount, Until;
        public void Write(BinaryWriter w) { w.Write((byte)Kind); w.Write(Source); w.Write(Target); w.Write(Ability ?? ""); w.Write(X); w.Write(Y); w.Write(X2); w.Write(Y2); w.Write(Amount); w.Write(Until); }
        public static EffectSnapshot Read(BinaryReader r) => new EffectSnapshot { Kind = (EffectKind)r.ReadByte(), Source = r.ReadInt32(), Target = r.ReadInt32(), Ability = r.ReadString(), X = r.ReadSingle(), Y = r.ReadSingle(), X2 = r.ReadSingle(), Y2 = r.ReadSingle(), Amount = r.ReadSingle(), Until = r.ReadSingle() };
    }

    /// <summary>A world snapshot for one team: the avatars it may see, each member's own numbers, and the effects to draw.</summary>
    public sealed class TeamSnapshot
    {
        public uint Tick; public float Time; public List<AvatarSnapshot> Avatars = new List<AvatarSnapshot>(); public List<SelfSnapshot> Selves = new List<SelfSnapshot>(); public List<EffectSnapshot> Effects = new List<EffectSnapshot>();
        public byte[] ToBytes()
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Tick); w.Write(Time);
                w.Write(Avatars.Count); foreach (var a in Avatars) a.Write(w);
                w.Write(Selves.Count); foreach (var s in Selves) s.Write(w);
                w.Write(Effects.Count); foreach (var e in Effects) e.Write(w);
            }
            return ms.ToArray();
        }
        public static TeamSnapshot FromBytes(byte[] b)
        {
            using var ms = new MemoryStream(b); using var r = new BinaryReader(ms);
            var t = new TeamSnapshot { Tick = r.ReadUInt32(), Time = r.ReadSingle() };
            int n = r.ReadInt32(); for (int i = 0; i < n; i++) t.Avatars.Add(AvatarSnapshot.Read(r));
            n = r.ReadInt32(); for (int i = 0; i < n; i++) t.Selves.Add(SelfSnapshot.Read(r));
            n = r.ReadInt32(); for (int i = 0; i < n; i++) t.Effects.Add(EffectSnapshot.Read(r));
            return t;
        }
    }
}
