using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Items;

namespace DungeonGen.Core.Profile
{
    /// <summary>A saved profile that cannot be read back: wrong header, unknown version, or a key no catalogue knows.</summary>
    public sealed class ProfileFormatException : Exception
    {
        public ProfileFormatException(string message) : base(message) { }
        public ProfileFormatException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// The lossless binary form of a <see cref="PlayerProfile"/>: what the backend stores and the
    /// client caches. Everything that persists between rounds goes in (account id, silver, materials,
    /// the stash, consumables, the eight character slots with what each wears per slot, unlocks,
    /// account XP, the rounds already settled). Items are written by stable keys, never by object
    /// identity: base name, item level, rarity, name, each affix's family key + tier + value, the
    /// unique's name, the wear state and the starter flag. Reading resolves every key through the
    /// existing catalogues (<see cref="ItemBase.ByName"/>, <see cref="AffixDefinition.ByFamily"/>,
    /// <see cref="UniqueItem.ByName"/>, <see cref="Consumable.ByName"/>, <see cref="ClassArchetype.ByName"/>)
    /// and fails loudly with a <see cref="ProfileFormatException"/> on any key it does not know:
    /// a profile is never silently loaded with something missing.
    /// Unordered collections are written in ordinal key order, so save → load → save is byte-identical.
    /// </summary>
    public static class ProfileCodec
    {
        /// <summary>"DGPF" little-endian.</summary>
        public const int Magic = 0x46504744;
        public const int Version = 1;

        public static byte[] ToBytes(PlayerProfile p)
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms)) Write(w, p);
            return ms.ToArray();
        }

        public static PlayerProfile FromBytes(byte[] bytes)
        {
            if (bytes == null) throw new ProfileFormatException("No profile bytes.");
            using var ms = new MemoryStream(bytes);
            using var r = new BinaryReader(ms);
            PlayerProfile p;
            try { p = Read(r); }
            catch (EndOfStreamException e) { throw new ProfileFormatException("Profile is truncated.", e); }
            if (ms.Position != ms.Length) throw new ProfileFormatException($"Profile has {ms.Length - ms.Position} unread trailing bytes.");
            return p;
        }

        public static void Write(BinaryWriter w, PlayerProfile p)
        {
            w.Write(Magic); w.Write(Version);
            w.Write(p.AccountId ?? "");
            w.Write(p.Silver);
            w.Write(p.AccountXp);
            // Materials.
            w.Write(p.Materials.Scrap); w.Write(p.Materials.PrimeShards);
            var essences = p.Materials.Essences.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
            w.Write(essences.Count);
            foreach (var kv in essences) { w.Write(kv.Key); w.Write(kv.Value); }
            // The stash, in order.
            w.Write(p.Stash.Count);
            foreach (var it in p.Stash) WriteItem(w, it);
            // Consumables.
            var cons = p.Consumables.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
            w.Write(cons.Count);
            foreach (var kv in cons) { w.Write(kv.Key); w.Write(kv.Value); }
            // Characters: name, class, the nine slots in a fixed order.
            w.Write(p.Characters.Count);
            foreach (var ch in p.Characters)
            {
                w.Write(ch.Name ?? ""); w.Write(ch.Class.Name);
                foreach (var it in Slots(ch.Equipment)) { w.Write(it != null); if (it != null) WriteItem(w, it); }
            }
            // Unlocks.
            var unlocks = p.Unlocks.OrderBy(u => u, StringComparer.Ordinal).ToList();
            w.Write(unlocks.Count);
            foreach (var u in unlocks) w.Write(u);
            // Settled rounds, oldest first.
            w.Write(p.SettledRounds.Count);
            foreach (var k in p.SettledRounds) w.Write(k);
        }

        public static PlayerProfile Read(BinaryReader r)
        {
            int magic = r.ReadInt32();
            if (magic != Magic) throw new ProfileFormatException($"Not a profile (header 0x{magic:x8}, expected 0x{Magic:x8}).");
            int version = r.ReadInt32();
            if (version != Version) throw new ProfileFormatException($"Profile version {version} is not supported (this build reads {Version}).");
            var p = new PlayerProfile(r.ReadString()) { Silver = r.ReadInt64() };
            p.AddAccountXp(r.ReadInt64());
            p.Materials.Scrap = r.ReadInt32(); p.Materials.PrimeShards = r.ReadInt32();
            int essences = Count(r, "essences");
            for (int i = 0; i < essences; i++)
            {
                string family = r.ReadString(); int n = r.ReadInt32();
                if (AffixDefinition.ByFamily(family) == null) throw new ProfileFormatException($"Unknown essence family '{family}'.");
                p.Materials.Essences[family] = n;
            }
            int stash = Count(r, "stash items");
            for (int i = 0; i < stash; i++) p.Stash.Add(ReadItem(r));
            int cons = Count(r, "consumables");
            for (int i = 0; i < cons; i++)
            {
                string name = r.ReadString(); int n = r.ReadInt32();
                if (Consumable.ByName(name) == null) throw new ProfileFormatException($"Unknown consumable '{name}'.");
                p.Consumables[name] = n;
            }
            int chars = Count(r, "characters");
            if (chars > PlayerProfile.MaxCharacters) throw new ProfileFormatException($"Profile has {chars} characters; an account has {PlayerProfile.MaxCharacters} slots.");
            for (int i = 0; i < chars; i++)
            {
                string name = r.ReadString(), className = r.ReadString();
                var cls = ClassArchetype.ByName(className) ?? throw new ProfileFormatException($"Unknown class '{className}' on character '{name}'.");
                // Added directly, not through CreateCharacter: loading restores what was saved, it does not re-run the unlock gate.
                var ch = new Character(name, cls);
                var e = ch.Equipment;
                e.Weapon = ReadSlot(r, ItemSlot.Weapon, name); e.Head = ReadSlot(r, ItemSlot.Head, name); e.Chest = ReadSlot(r, ItemSlot.Chest, name);
                e.Hands = ReadSlot(r, ItemSlot.Hands, name); e.Legs = ReadSlot(r, ItemSlot.Legs, name); e.Feet = ReadSlot(r, ItemSlot.Feet, name);
                e.Amulet = ReadSlot(r, ItemSlot.Amulet, name); e.Ring1 = ReadSlot(r, ItemSlot.Ring, name); e.Ring2 = ReadSlot(r, ItemSlot.Ring, name);
                p.Characters.Add(ch);
            }
            int unlocks = Count(r, "unlocks");
            for (int i = 0; i < unlocks; i++) p.Unlocks.Add(r.ReadString());
            int settled = Count(r, "settled rounds");
            for (int i = 0; i < settled; i++) p.MarkSettled(r.ReadString());
            return p;
        }

        /// <summary>One item by its stable keys. Public so other saves (a mail-back, a trade log) can reuse the exact form.</summary>
        public static void WriteItem(BinaryWriter w, Item it)
        {
            w.Write(it.Base.Name);
            w.Write(it.ItemLevel);
            w.Write((byte)it.Rarity);
            w.Write(it.Name ?? "");
            w.Write(it.Unique?.Name ?? "");
            WriteAffixes(w, it.Prefixes); WriteAffixes(w, it.Suffixes);
            w.Write(it.Condition.Max); w.Write(it.Condition.Current);
            w.Write(it.IsStarter);
        }

        public static Item ReadItem(BinaryReader r)
        {
            string baseName = r.ReadString();
            var b = ItemBase.ByName(baseName) ?? throw new ProfileFormatException($"Unknown item base '{baseName}'.");
            int level = r.ReadInt32();
            byte rarityByte = r.ReadByte();
            if (!Enum.IsDefined(typeof(Rarity), (int)rarityByte)) throw new ProfileFormatException($"Unknown rarity {rarityByte} on a {baseName}.");
            var rarity = (Rarity)rarityByte;
            string name = r.ReadString();
            string uniqueName = r.ReadString();
            UniqueItem unique = null;
            if (uniqueName.Length > 0)
            {
                unique = UniqueItem.ByName(uniqueName) ?? throw new ProfileFormatException($"Unknown unique '{uniqueName}'.");
                if (unique.Base != b) throw new ProfileFormatException($"Unique '{uniqueName}' is a {unique.Base.Name}, saved as a {baseName}.");
                if (rarity != Rarity.Unique) throw new ProfileFormatException($"Unique '{uniqueName}' saved with rarity {rarity}.");
            }
            else if (rarity == Rarity.Unique) throw new ProfileFormatException($"A Unique {baseName} ('{name}') saved without its unique name.");
            var pre = ReadAffixes(r, name); var suf = ReadAffixes(r, name);
            float max = r.ReadSingle(), current = r.ReadSingle();
            bool starter = r.ReadBoolean();
            var item = new Item(b, level, rarity, name, pre, suf, unique);
            item.Condition.Restore(max, current);
            if (starter) item.MarkStarter();
            return item;
        }

        private static void WriteAffixes(BinaryWriter w, IReadOnlyList<AffixRoll> list)
        {
            w.Write(list.Count);
            foreach (var a in list) { w.Write(a.Definition.FamilyKey); w.Write(a.Tier); w.Write(a.Value); }
        }

        private static List<AffixRoll> ReadAffixes(BinaryReader r, string itemName)
        {
            int n = Count(r, "affixes");
            var list = new List<AffixRoll>(n);
            for (int i = 0; i < n; i++)
            {
                string key = r.ReadString(); int tier = r.ReadInt32(); float value = r.ReadSingle();
                var def = AffixDefinition.ByFamily(key) ?? throw new ProfileFormatException($"Unknown affix family '{key}' on '{itemName}'.");
                list.Add(new AffixRoll(def, tier, value));
            }
            return list;
        }

        private static Item ReadSlot(BinaryReader r, ItemSlot slot, string character)
        {
            if (!r.ReadBoolean()) return null;
            var it = ReadItem(r);
            if (it.Base.Slot != slot) throw new ProfileFormatException($"'{it.Name}' ({it.Base.Slot}) saved in the {slot} slot of '{character}'.");
            return it;
        }

        private static int Count(BinaryReader r, string what)
        {
            int n = r.ReadInt32();
            if (n < 0 || n > 1_000_000) throw new ProfileFormatException($"Impossible count {n} of {what}.");
            return n;
        }

        private static IEnumerable<Item> Slots(Equipment e) => new[] { e.Weapon, e.Head, e.Chest, e.Hands, e.Legs, e.Feet, e.Amulet, e.Ring1, e.Ring2 };
    }
}
