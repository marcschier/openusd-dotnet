// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Runtime.InteropServices;

namespace OpenUsd.Physics.Interop;

/// <summary>
/// Owns the memory of one finished build page.
/// </summary>
/// <remarks>
/// The page lives in a pooled <see cref="double"/> array because the payload of a double array is
/// always eight byte aligned, which is exactly what the ABI requires of the page start, while still
/// letting the extractor reuse memory across revisions instead of allocating a new page per frame.
/// The page never exposes a pointer; a caller that needs to hand the page to native code takes a
/// <see cref="PhysxPageLease"/>, which pins the memory for the duration of the call only.
/// </remarks>
internal sealed class PhysxBuildPage : IDisposable
{
    private double[] _storage;
    private readonly int _byteLength;
    private bool _disposed;

    internal PhysxBuildPage(ReadOnlySpan<byte> bytes, PhysxPageValidation validation)
    {
        _storage = ArrayPool<double>.Shared.Rent((bytes.Length + 7) / 8);
        _byteLength = bytes.Length;
        bytes.CopyTo(MemoryMarshal.AsBytes(_storage.AsSpan()));
        Validation = validation;
    }

    /// <summary>Gets the successful validation summary produced while building this page.</summary>
    internal PhysxPageValidation Validation { get; }

    /// <summary>Gets the page size, in bytes.</summary>
    internal int ByteLength => _byteLength;

    /// <summary>Gets the page bytes.</summary>
    internal ReadOnlySpan<byte> Bytes
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return MemoryMarshal.AsBytes(_storage.AsSpan())[.._byteLength];
        }
    }

    /// <summary>Gets the result capacities this page declares.</summary>
    internal PhysxResultCapacities Capacities => Validation.Capacities;

    /// <summary>Creates a reader over this page.</summary>
    internal PhysxPageReader CreateReader() => new(Bytes);

    /// <summary>Pins this page for the duration of one native call.</summary>
    internal PhysxPageLease Lease()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new PhysxPageLease(_storage, _byteLength);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        double[] storage = _storage;
        _storage = [];
        ArrayPool<double>.Shared.Return(storage);
    }
}

/// <summary>
/// Pins a build page for exactly one native call.
/// </summary>
/// <remarks>
/// The lease is a ref struct so the pinned address can never be stored in a field, boxed, or
/// captured; when the lease is disposed the page is unpinned and the address becomes unusable, which
/// is the same lifetime the ABI promises: the page is borrowed for the call and never retained.
/// </remarks>
internal unsafe ref struct PhysxPageLease
{
    private MemoryHandle _handle;

    internal PhysxPageLease(double[] storage, int byteLength)
    {
        _handle = storage.AsMemory().Pin();
        if (((nuint)_handle.Pointer % PhysxAbi.PageAlignment) != 0)
        {
            _handle.Dispose();
            throw new InvalidOperationException("The build page must start on an eight byte boundary.");
        }
        ByteLength = byteLength;
    }

    /// <summary>Gets the page size, in bytes.</summary>
    internal int ByteLength { get; }

    /// <summary>Gets the pinned page address.</summary>
    internal void* Pointer => _handle.Pointer;

    /// <summary>Gets the pinned page address as an unsigned integer, for alignment checks.</summary>
    internal nuint Address => (nuint)_handle.Pointer;

    /// <summary>Unpins the page.</summary>
    public void Dispose() => _handle.Dispose();
}
