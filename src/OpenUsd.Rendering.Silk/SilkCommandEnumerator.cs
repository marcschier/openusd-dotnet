// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Enumerates command views backed by managed-owned bytes.
/// </summary>
/// <remarks>Contiguous pages allocate nothing; file-backed pages copy individual commands to preserve view
/// lifetimes.</remarks>
public ref struct SilkCommandEnumerator
{
    private ReadOnlySpan<byte> _remaining;
    private uint _remainingCount;
    private SilkCommandStorageReader? _storage;
    private ulong _offset;
    internal SilkCommandEnumerator(ReadOnlySpan<byte> data, uint commandCount)
    {
        _remaining = data;
        _remainingCount = commandCount;
        _storage = null;
        _offset = 0;
        Current = default;
    }

    internal SilkCommandEnumerator(SilkCommandStorage storage, uint commandCount, bool preserveViews)
    {
        _remaining = default;
        _remainingCount = commandCount;
        _offset = 0;
        Current = default;
        _storage = new SilkCommandStorageReader(storage, preserveViews);
    }

    /// <summary>Gets the current command.</summary>
    public SilkCommand Current { get; private set; }

    /// <summary>Advances to the next command.</summary>
    public bool MoveNext()
    {
        if (_storage is { } storage)
        {
            bool next = storage.MoveNext(ref _offset, ref _remainingCount, out SilkCommand command);
            Current = command;
            return next;
        }
        if (_remainingCount == 0)
        {
            if (!_remaining.IsEmpty)
            {
                throw new InvalidDataException(
                    "The command page contains trailing bytes after its declared commands.");
            }
            return false;
        }
        if (_remaining.Length < 8)
        {
            throw new InvalidDataException("The command page ended before its command header.");
        }

        var type = (SilkCommandType)BinaryPrimitives.ReadUInt32LittleEndian(_remaining[..4]);
        uint encodedSize = BinaryPrimitives.ReadUInt32LittleEndian(_remaining[4..8]);
        if (encodedSize > int.MaxValue)
        {
            throw new InvalidDataException("The command size exceeds the managed page limit.");
        }
        int size = (int)encodedSize;
        if (size < 8 || size > _remaining.Length)
        {
            throw new InvalidDataException("The command page contains an invalid command size.");
        }

        Current = new SilkCommand(type, _remaining[..size]);
        _remaining = _remaining[size..];
        _remainingCount--;
        if (_remainingCount == 0 && !_remaining.IsEmpty)
        {
            throw new InvalidDataException(
                "The command page contains trailing bytes after its declared commands.");
        }
        return true;
    }

    /// <summary>Completes enumeration.</summary>
    public void Dispose()
    {
        if (_storage is { PreserveViews: false } storage)
        {
            storage.Dispose();
        }
        _storage = null;
        _offset = 0;
        _remaining = default;
        _remainingCount = 0;
        Current = default;
    }
}
