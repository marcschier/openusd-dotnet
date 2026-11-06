// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

internal abstract class SilkVolumeAssets : IDisposable
{
    private int _references = 1;

    internal SilkVolumeAssets Acquire()
    {
        int current = Volatile.Read(ref _references);
        while (current > 0)
        {
            int next = checked(current + 1);
            int observed = Interlocked.CompareExchange(ref _references, next, current);
            if (observed == current)
            {
                return this;
            }
            current = observed;
        }
        throw new ObjectDisposedException(nameof(SilkVolumeAssets));
    }

    internal void Copy(string materialPath, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _references) == 0, this);
        CopyCore(materialPath, destination);
    }

    protected abstract void CopyCore(string materialPath, Span<byte> destination);

    protected abstract void DisposeCore();

    public void Dispose()
    {
        int remaining = Interlocked.Decrement(ref _references);
        if (remaining == 0)
        {
            DisposeCore();
        }
        else if (remaining < 0)
        {
            throw new InvalidOperationException("A volume-asset reference was released twice.");
        }
    }
}
