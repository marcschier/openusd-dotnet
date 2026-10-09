// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

internal sealed class SilkSharedReservation
{
    private readonly object _gate = new();
    private IDisposable? _reservation;
    private int _references;

    internal SilkSharedReservation(IDisposable reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        _reservation = reservation;
    }

    internal IDisposable Acquire()
    {
        var lease = new Lease(this);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_reservation is null, this);
            _references = checked(_references + 1);
        }
        return lease;
    }

    private void Release()
    {
        IDisposable? released = null;
        lock (_gate)
        {
            if (--_references == 0)
            {
                released = _reservation;
                _reservation = null;
            }
        }
        released?.Dispose();
    }

    private sealed class Lease(SilkSharedReservation owner) : IDisposable
    {
        private SilkSharedReservation? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}
