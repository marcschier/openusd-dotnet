// Copyright (c) marcschier. Licensed under the MIT License.

using System.IO.Hashing;
using System.Runtime.InteropServices;

namespace OpenUsd.Rendering.Silk;

// The pool exists only while applying one page; retained immutable meshes own the arrays.
internal sealed class SilkMeshArrayPool
{
    internal ArrayPool<float> Floats { get; } = new();
    internal ArrayPool<int> Integers { get; } = new();
    internal ArrayPool<uint> Indices { get; } = new();

    internal sealed class ArrayPool<T> where T : unmanaged
    {
        private const int MaximumEntries = 65536;
        private const int MaximumCollisions = 8;
        private readonly Dictionary<ulong, List<T[]>> _arrays = [];
        private int _count;

        internal T[] Share(T[] owned)
        {
            if (owned.Length == 0)
            {
                return owned;
            }
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(owned.AsSpan());
            ulong hash = XxHash64.HashToUInt64(bytes);
            if (_arrays.TryGetValue(hash, out List<T[]>? bucket))
            {
                foreach (T[] candidate in bucket)
                {
                    if (bytes.SequenceEqual(MemoryMarshal.AsBytes(candidate.AsSpan())))
                    {
                        return candidate;
                    }
                }
                if (bucket.Count >= MaximumCollisions)
                {
                    return owned;
                }
            }
            if (_count >= MaximumEntries)
            {
                return owned;
            }
            if (bucket is null)
            {
                bucket = [];
                _arrays.Add(hash, bucket);
            }
            bucket.Add(owned);
            _count++;
            return owned;
        }
    }
}
