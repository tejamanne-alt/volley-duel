using System;

namespace Duel.Core.Sim
{
    /// <summary>
    /// FNV-1a 64-bit hash. Used for state hashes (exchanged every 30 frames to catch desyncs),
    /// replay checks and version hashes. Never use <c>object.GetHashCode</c> or <c>HashCode</c>
    /// for these: they are randomized per process.
    /// </summary>
    public static class StateHash
    {
        public const ulong OffsetBasis = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;

        public static ulong Fnv1a(ReadOnlySpan<byte> data, ulong hash = OffsetBasis)
        {
            for (int i = 0; i < data.Length; i++)
            {
                hash ^= data[i];
                hash = unchecked(hash * Prime);
            }
            return hash;
        }

        public static ulong Add(ulong hash, ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                hash ^= (byte)(value >> (i * 8));
                hash = unchecked(hash * Prime);
            }
            return hash;
        }

        public static ulong AddString(ulong hash, string text)
        {
            if (text == null) return Add(hash, 0xFFFFFFFFUL);
            hash = Add(hash, (ulong)text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                hash ^= (byte)c;
                hash = unchecked(hash * Prime);
                hash ^= (byte)(c >> 8);
                hash = unchecked(hash * Prime);
            }
            return hash;
        }
    }
}
