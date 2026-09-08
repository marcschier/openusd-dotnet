// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd;

// Owns an unaliased array without exposing ICollection.SyncRoot or mutable collection interfaces.
internal sealed class OwnedReadOnlyList<T>(T[] items) : IReadOnlyList<T>
{
    public int Count => items.Length;
    public T this[int index] => items[index];
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)items).GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
