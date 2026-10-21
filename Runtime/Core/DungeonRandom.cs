using System;

namespace DungeonGen.Core
{
    /// <summary>
    /// Deterministic PRNG (xorshift128+). Unity's UnityEngine.Random and System.Random
    /// are not guaranteed stable across platforms/runtime versions; this one is, which is
    /// what lets a server send clients a 32-bit seed and have every machine build the
    /// identical dungeon.
    /// </summary>
    public sealed class DungeonRandom
    {
        private ulong _s0;
        private ulong _s1;

        public int Seed { get; }

        public DungeonRandom(int seed)
        {
            Seed = seed;
            // SplitMix64 to expand the 32-bit seed into two non-zero 64-bit words.
            ulong x = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 0x1234567UL;
            _s0 = SplitMix(ref x);
            _s1 = SplitMix(ref x);
            if (_s0 == 0 && _s1 == 0) _s1 = 1;
        }

        private static ulong SplitMix(ref ulong state)
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public ulong NextULong()
        {
            ulong s1 = _s0;
            ulong s0 = _s1;
            _s0 = s0;
            s1 ^= s1 << 23;
            _s1 = s1 ^ s0 ^ (s1 >> 18) ^ (s0 >> 5);
            return _s1 + s0;
        }

        /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            ulong span = (ulong)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextULong() % span);
        }

        /// <summary>Uniform float in [0, 1).</summary>
        public float NextFloat() => (float)((NextULong() >> 11) * (1.0 / (1UL << 53)));

        public bool Chance(float probability) => NextFloat() < probability;

        public T Pick<T>(System.Collections.Generic.IReadOnlyList<T> list) => list[Range(0, list.Count)];

        public void Shuffle<T>(System.Collections.Generic.IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>Derive an independent stream for a sub-system (e.g. loot) so changes to one stage don't reshuffle another.</summary>
        public DungeonRandom Fork(int salt) => new DungeonRandom(unchecked(Seed * 31 + salt * 0x5bd1e995));
    }
}
