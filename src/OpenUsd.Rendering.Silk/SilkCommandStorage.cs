// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;

namespace OpenUsd.Rendering.Silk;

internal abstract class SilkCommandStorage(ulong length, int maximumCommandBytes, int transferBytes) : IDisposable
{
    private int _references = 1;

    internal ulong Length { get; } = length;
    internal int MaximumCommandBytes { get; } = maximumCommandBytes;
    internal int TransferBytes { get; } = transferBytes;

    internal SilkCommandStorage Acquire()
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
        throw new ObjectDisposedException(nameof(SilkCommandStorage));
    }

    internal void Read(ulong offset, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _references) == 0, this);
        if (offset > Length || (ulong)destination.Length > Length - offset ||
            destination.Length > TransferBytes)
        {
            throw new InvalidDataException("The command-storage read exceeds its immutable extent or transfer limit.");
        }
        ReadCore(offset, destination);
    }

    protected abstract void ReadCore(ulong offset, Span<byte> destination);
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
            throw new InvalidOperationException("A command-storage reference was released twice.");
        }
    }
}

internal sealed class SilkCommandStorageReader : IDisposable
{
    private SilkCommandStorage? _storage;
    private byte[] _transfer;
    private byte[] _command = [];
    private ulong _windowOffset;
    private int _windowLength;

    internal SilkCommandStorageReader(SilkCommandStorage storage, bool preserveViews)
    {
        _transfer = GC.AllocateUninitializedArray<byte>(storage.TransferBytes);
        PreserveViews = preserveViews;
        _storage = storage.Acquire();
    }

    // Public enumerator copies share the reader but own independent cursors.
    // Its final reference keeps the native file alive even after page disposal.
    ~SilkCommandStorageReader() => Release();

    internal bool PreserveViews { get; }

    internal bool MoveNext(scoped ref ulong offset, scoped ref uint remaining, out SilkCommand command)
    {
        SilkCommandStorage storage = _storage ?? throw new ObjectDisposedException(nameof(SilkCommandStorageReader));
        command = default;
        if (remaining == 0)
        {
            if (offset != storage.Length)
            {
                throw new InvalidDataException("The command update contains trailing bytes.");
            }
            return false;
        }
        if (offset > storage.Length || storage.Length - offset < 8)
        {
            throw new InvalidDataException("The command update ended before its command header.");
        }
        Span<byte> header = stackalloc byte[8];
        Read(offset, header);
        uint encodedSize = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        if (encodedSize < 8 || encodedSize > (uint)storage.MaximumCommandBytes ||
            encodedSize > storage.Length - offset)
        {
            throw new InvalidDataException("The command size exceeds its extent or admitted buffer limit.");
        }
        if (remaining == 1 && offset + encodedSize != storage.Length)
        {
            throw new InvalidDataException("The command update contains trailing bytes.");
        }
        int size = checked((int)encodedSize);
        byte[] bytes;
        if (PreserveViews)
        {
            bytes = GC.AllocateUninitializedArray<byte>(size);
        }
        else
        {
            if (_command.Length < size)
            {
                _command = GC.AllocateUninitializedArray<byte>(size);
            }
            bytes = _command;
        }
        header.CopyTo(bytes);
        Read(offset + 8, bytes.AsSpan(8, size - 8));
        command = new SilkCommand((SilkCommandType)BinaryPrimitives.ReadUInt32LittleEndian(header),
            bytes.AsSpan(0, size));
        offset += encodedSize;
        remaining--;
        return true;
    }

    private void Read(ulong offset, Span<byte> destination)
    {
        SilkCommandStorage storage = _storage ?? throw new ObjectDisposedException(nameof(SilkCommandStorageReader));
        while (!destination.IsEmpty)
        {
            if (offset < _windowOffset || offset - _windowOffset >= (ulong)_windowLength)
            {
                _windowLength = 0;
                int length = (int)Math.Min((ulong)_transfer.Length, storage.Length - offset);
                storage.Read(offset, _transfer.AsSpan(0, length));
                _windowOffset = offset;
                _windowLength = length;
            }
            int start = checked((int)(offset - _windowOffset));
            int count = Math.Min(destination.Length, _windowLength - start);
            _transfer.AsSpan(start, count).CopyTo(destination);
            offset += (uint)count;
            destination = destination[count..];
        }
    }

    public void Dispose()
    {
        Release();
        GC.SuppressFinalize(this);
    }

    private void Release()
    {
        Interlocked.Exchange(ref _storage, null)?.Dispose();
        _command = [];
        _transfer = [];
        _windowLength = 0;
    }
}
