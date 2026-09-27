// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

internal sealed class SilkByteReservationPool(
    ulong maximumBytes,
    Func<ulong, ulong, ulong, Exception> exceeded)
{
    private readonly object _gate = new();
    private ulong _reserved;
    private ulong _peak;
    private ulong _count;

    internal (ulong Reserved, ulong Peak, ulong Count) Snapshot
    {
        get
        {
            lock (_gate)
            {
                return (_reserved, _peak, _count);
            }
        }
    }

    internal IDisposable Reserve(ulong bytes)
    {
        ArgumentOutOfRangeException.ThrowIfZero(bytes);
        var reservation = new Reservation(this, bytes);
        lock (_gate)
        {
            if (bytes > maximumBytes - _reserved)
            {
                throw exceeded(bytes, _reserved, maximumBytes);
            }
            _reserved = checked(_reserved + bytes);
            _peak = Math.Max(_peak, _reserved);
            _count++;
        }
        return reservation;
    }

    private void Release(ulong bytes)
    {
        lock (_gate)
        {
            _reserved -= bytes;
            _count--;
        }
    }

    private sealed class Reservation(SilkByteReservationPool owner, ulong bytes) : IDisposable
    {
        private SilkByteReservationPool? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(bytes);
    }
}
