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

        public Vec2 Move => new Vec2(MoveX, MoveY);
        public Vec2 Aim => new Vec2(AimX, AimY);
        public bool AnyCast => Basic || Bolt || Zone || Mobility || Ultimate;

        public void Write(BinaryWriter w)
        {
            w.Write(PlayerId); w.Write(Tick); w.Write(MoveX); w.Write(MoveY); w.Write(AimX); w.Write(AimY);
            byte flags = 0;
            if (Basic) flags |= 1; if (Bolt) flags |= 2; if (Zone) flags |= 4; if (Mobility) flags |= 8; if (Ultimate) flags |= 16; if (Interact) flags |= 32; if (Ping) flags |= 64;
            w.Write(flags); w.Write(PingKind);
        }
        public static PlayerIntent Read(BinaryReader r)
        {
            var i = new PlayerIntent { PlayerId = r.ReadInt32(), Tick = r.ReadUInt32(), MoveX = r.ReadSingle(), MoveY = r.ReadSingle(), AimX = r.ReadSingle(), AimY = r.ReadSingle() };
            byte f = r.ReadByte(); i.PingKind = r.ReadByte();
            i.Basic = (f & 1) != 0; i.Bolt = (f & 2) != 0; i.Zone = (f & 4) != 0; i.Mobility = (f & 8) != 0; i.Ultimate = (f & 16) != 0; i.Interact = (f & 32) != 0; i.Ping = (f & 64) != 0;
            return i;
        }
        public byte[] ToBytes() { using var ms = new MemoryStream(); using (var w = new BinaryWriter(ms)) Write(w); return ms.ToArray(); }
        public static PlayerIntent FromBytes(byte[] b) { using var ms = new MemoryStream(b); using var r = new BinaryReader(ms); return Read(r); }
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

    /// <summary>The local player's own numbers, sent only to them: resource, cooldowns, channel progress.</summary>
    public struct SelfSnapshot
    {
        public int PlayerId; public float Health, MaxHealth, Resource, MaxResource; public float[] CooldownRemaining; public string ChannelName; public float ChannelFraction; public bool UltimateArmed; public float DownedRemaining;
        public void Write(BinaryWriter w)
        {
            w.Write(PlayerId); w.Write(Health); w.Write(MaxHealth); w.Write(Resource); w.Write(MaxResource);
            w.Write(CooldownRemaining?.Length ?? 0); if (CooldownRemaining != null) foreach (var c in CooldownRemaining) w.Write(c);
            w.Write(ChannelName ?? ""); w.Write(ChannelFraction); w.Write(UltimateArmed); w.Write(DownedRemaining);
        }
        public static SelfSnapshot Read(BinaryReader r)
        {
            var s = new SelfSnapshot { PlayerId = r.ReadInt32(), Health = r.ReadSingle(), MaxHealth = r.ReadSingle(), Resource = r.ReadSingle(), MaxResource = r.ReadSingle() };
            int n = r.ReadInt32(); s.CooldownRemaining = new float[n]; for (int i = 0; i < n; i++) s.CooldownRemaining[i] = r.ReadSingle();
            s.ChannelName = r.ReadString(); s.ChannelFraction = r.ReadSingle(); s.UltimateArmed = r.ReadBoolean(); s.DownedRemaining = r.ReadSingle();
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
