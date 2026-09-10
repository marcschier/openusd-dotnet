// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer;

internal sealed class CompositionCaptureLease(SemaphoreSlim gate, IDisposable? inner = null) : IDisposable
{
    private readonly object _sync = new();
    private SemaphoreSlim? _gate = gate;
    private IDisposable? _inner = inner;

    public void Dispose()
    {
        lock (_sync)
        {
            if (_gate is null)
            {
                return;
            }
            _inner?.Dispose();
            _inner = null;
            _gate.Release();
            _gate = null;
        }
    }
}
