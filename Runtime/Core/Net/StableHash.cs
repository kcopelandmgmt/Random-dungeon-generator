using System;
using System.Text;

namespace DungeonGen.Core.Net
{
    /// <summary>
    /// FNV-1a 64-bit. Deliberately not <c>GetHashCode</c>: that is randomized per process in .NET
    /// Core and differs between Mono and IL2CPP. This gives the same value on every machine, which
    /// is what lets a server and its clients compare what they built.
    /// </summary>
    public struct StableHash
    {
        private ulong _h;
        public static StableHash Start() => new StableHash { _h = 14695981039346656037UL };
        public ulong Value => _h;

        public StableHash Add(byte b) { _h ^= b; _h *= 1099511628211UL; return this; }
        public StableHash Add(int v) { for (int i = 0; i < 4; i++) Add((byte)(v >> (8 * i))); return this; }
        public StableHash Add(float v) => Add(BitConverter.SingleToInt32Bits(v));
        public StableHash Add(bool v) => Add((byte)(v ? 1 : 0));
        public StableHash Add(string s) { if (s == null) return Add(-1); Add(s.Length); foreach (var b in Encoding.UTF8.GetBytes(s)) Add(b); return this; }

        public override string ToString() => _h.ToString("x16");
    }
}
