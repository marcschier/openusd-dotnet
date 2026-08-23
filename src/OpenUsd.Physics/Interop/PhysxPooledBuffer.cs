// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Runtime.InteropServices;

namespace OpenUsd.Physics.Interop;

/// <summary>
/// A growable append-only byte buffer backed by pooled managed memory.
/// </summary>
/// <remarks>
/// The page builder and the identity table accumulate section bytes here so that building a page
/// reuses pooled arrays instead of allocating a fresh buffer per extraction. The buffer never hands
/// out a pointer and never survives its owner.
/// </remarks>
internal sealed class PhysxPooledBuffer : IDisposable
{
    private byte[] _array;
    private int _length;
    private bool _disposed;

    /// <summary>Rents an initial buffer of at least <paramref name="initialCapacity"/> bytes.</summary>
    internal PhysxPooledBuffer(int initialCapacity = 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);
        _array = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, 64));
    }

    /// <summary>Gets the number of written bytes.</summary>
    internal int Length => _length;

    /// <summary>Gets the written bytes.</summary>
    internal ReadOnlySpan<byte> Written
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _array.AsSpan(0, _length);
        }
    }

    /// <summary>Appends raw bytes.</summary>
    internal void Write(ReadOnlySpan<byte> value)
    {
        Reserve(value.Length);
        value.CopyTo(_array.AsSpan(_length));
        _length += value.Length;
    }

    /// <summary>Appends one blittable record.</summary>
    internal void Write<T>(in T value)
        where T : unmanaged
    {
        var source = new ReadOnlySpan<T>(in value);
        Write(MemoryMarshal.AsBytes(source));
    }

    /// <summary>Appends a contiguous run of blittable records.</summary>
    internal void WriteRange<T>(ReadOnlySpan<T> values)
        where T : unmanaged =>
        Write(MemoryMarshal.AsBytes(values));

    /// <summary>Appends zero bytes until the length is a multiple of <paramref name="alignment"/>.</summary>
    internal void PadTo(int alignment)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(alignment);
        int remainder = _length % alignment;
        if (remainder == 0)
        {
            return;
        }

        int padding = alignment - remainder;
        Reserve(padding);
        _array.AsSpan(_length, padding).Clear();
        _length += padding;
    }

    /// <summary>Overwrites bytes already written at <paramref name="offset"/>.</summary>
    internal void Overwrite(int offset, ReadOnlySpan<byte> value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (offset + value.Length > _length)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "The overwrite range is outside the buffer.");
        }
        value.CopyTo(_array.AsSpan(offset));
    }

    /// <summary>Discards every written byte and keeps the rented capacity.</summary>
    internal void Reset() => _length = 0;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        byte[] array = _array;
        _array = [];
        _length = 0;
        ArrayPool<byte>.Shared.Return(array);
    }

    private void Reserve(int additional)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_length + additional <= _array.Length)
        {
            return;
        }

        int required = checked(_length + additional);
        byte[] replacement = ArrayPool<byte>.Shared.Rent(Math.Max(required, _array.Length * 2));
        _array.AsSpan(0, _length).CopyTo(replacement);
        ArrayPool<byte>.Shared.Return(_array);
        _array = replacement;
    }
}
