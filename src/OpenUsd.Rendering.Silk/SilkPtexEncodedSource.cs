// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Rendering.Silk;

internal sealed class SilkPtexEncodedSource : IDisposable
{
    private readonly object _gate = new();
    private byte[]? _bytes;
    private IDisposable? _reservation;
    private int _leases;
    private bool _disposed;

    private SilkPtexEncodedSource(
        string asset,
        byte[] bytes,
        SilkCpuTextureBudget? budget,
        IDisposable? reservation)
    {
        Asset = asset;
        Budget = budget;
        _bytes = bytes;
        _reservation = reservation;
    }

    internal string Asset { get; }
    internal SilkCpuTextureBudget? Budget { get; }

    internal static SilkPtexEncodedSource Open(
        string asset,
        ulong maximumEncodedBytes,
        SilkCpuTextureBudget? budget = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(asset);
        ArgumentOutOfRangeException.ThrowIfZero(maximumEncodedBytes);

        IDisposable? reservation = null;
        try
        {
            byte[] bytes;
            using (var stream = new FileStream(asset, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                long length = stream.Length;
                if (length <= 0 || (ulong)length > maximumEncodedBytes || length > Array.MaxLength)
                {
                    throw new InvalidDataException(
                        $"The Ptex encoded source length {length} is empty or exceeds its configured limit.");
                }

                reservation = budget?.Reserve((ulong)length);
                bytes = new byte[(int)length];
                stream.ReadExactly(bytes);
                if (stream.ReadByte() != -1 || stream.Length != length)
                {
                    throw new InvalidDataException("The Ptex encoded source changed length while being captured.");
                }
            }

            var source = new SilkPtexEncodedSource(asset, bytes, budget, reservation);
            reservation = null;
            return source;
        }
        catch (InvalidDataException exception)
        {
            throw new OpenUsdSilkException(
                OpenUsdNativeStatus.InvalidArgument,
                $"Could not capture Ptex source '{asset}': {exception.Message}",
                exception);
        }
        catch (IOException exception)
        {
            throw new OpenUsdSilkException(
                OpenUsdNativeStatus.NativeError,
                $"Could not read Ptex source '{asset}': {exception.Message}",
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new OpenUsdSilkException(
                OpenUsdNativeStatus.NativeError,
                $"Could not read Ptex source '{asset}': {exception.Message}",
                exception);
        }
        finally
        {
            reservation?.Dispose();
        }
    }

    internal Lease Acquire()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var lease = new Lease(this, _bytes!);
            _leases = checked(_leases + 1);
            return lease;
        }
    }

    public void Dispose()
    {
        IDisposable? reservation = null;
        lock (_gate)
        {
            if (!_disposed)
            {
                _disposed = true;
                _bytes = null;
                if (_leases == 0)
                {
                    reservation = _reservation;
                    _reservation = null;
                }
            }
        }
        reservation?.Dispose();
    }

    private void Release()
    {
        IDisposable? reservation = null;
        lock (_gate)
        {
            _leases--;
            if (_disposed && _leases == 0)
            {
                reservation = _reservation;
                _reservation = null;
            }
        }
        reservation?.Dispose();
    }

    internal sealed class Lease(SilkPtexEncodedSource owner, byte[] bytes) : IDisposable
    {
        private SilkPtexEncodedSource? _owner = owner;
        private byte[]? _bytes = bytes;

        internal ReadOnlySpan<byte> Bytes
        {
            get
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _owner) is null, this);
                return Volatile.Read(ref _bytes) ?? throw new ObjectDisposedException(nameof(Lease));
            }
        }

        public void Dispose()
        {
            SilkPtexEncodedSource? released = Interlocked.Exchange(ref _owner, null);
            if (released is not null)
            {
                _bytes = null;
                released.Release();
            }
        }
    }
}
