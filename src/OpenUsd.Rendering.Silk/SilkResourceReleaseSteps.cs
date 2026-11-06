// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

internal struct SilkResourceReleaseSteps
{
    private int _count;
    private bool _releasing;
    internal int Completed { get; private set; }
    internal bool IsReleasing => _releasing;

    internal void Release(int count, Action<int> release)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (count is <= 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
        if (_count != 0 && _count != count)
        {
            throw new InvalidOperationException("Native retirement cannot change its admitted release steps.");
        }
        if (_releasing)
        {
            throw new InvalidOperationException("Native retirement cannot reenter an active release.");
        }
        _count = count;
        _releasing = true;
        try
        {
            while (Completed < count)
            {
                release(Completed);
                Completed++;
            }
        }
        finally
        {
            _releasing = false;
        }
    }
}
