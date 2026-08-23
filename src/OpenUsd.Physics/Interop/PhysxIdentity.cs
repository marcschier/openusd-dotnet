// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace OpenUsd.Physics.Interop;

/// <summary>
/// Computes the stable 64-bit identity of a simulated object.
/// </summary>
/// <remarks>
/// The algorithm mirrors <c>openusd_physx_support::ComputeIdentity</c> exactly: an FNV-1a hash over
/// the UTF-8 prim path bytes followed by the little-endian bytes of the instance domain and the
/// instance index. The reserved zero identity is never produced. Identity therefore depends only on
/// canonical prim addressing and never on traversal order, so it survives rebuilds and reorderings.
/// </remarks>
internal static class PhysxIdentity
{
    private const ulong OffsetBasis = 1469598103934665603UL;
    private const ulong Prime = 1099511628211UL;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>Computes the identity of an already encoded UTF-8 prim path.</summary>
    /// <exception cref="ArgumentException">The path or the instance domain is not addressable.</exception>
    internal static ulong Compute(ReadOnlySpan<byte> utf8Path, PhysxInstanceDomain domain, uint instanceIndex)
    {
        if (!TryCompute(utf8Path, domain, instanceIndex, out ulong id, out string? error))
        {
            throw new ArgumentException(error, nameof(utf8Path));
        }
        return id;
    }

    /// <summary>Computes the identity of a prim path.</summary>
    /// <exception cref="ArgumentException">The path or the instance domain is not addressable.</exception>
    internal static ulong Compute(string path, PhysxInstanceDomain domain, uint instanceIndex)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Compute(Encode(path), domain, instanceIndex);
    }

    /// <summary>Computes the identity of an already encoded UTF-8 prim path without throwing.</summary>
    internal static bool TryCompute(
        ReadOnlySpan<byte> utf8Path,
        PhysxInstanceDomain domain,
        uint instanceIndex,
        out ulong id,
        [NotNullWhen(false)] out string? error)
    {
        id = PhysxAbi.InvalidId;
        if (utf8Path.IsEmpty)
        {
            error = "An identity requires a non-empty absolute prim path.";
            return false;
        }
        if (utf8Path[0] != (byte)'/')
        {
            error = "An identity requires an absolute prim path that starts with '/'.";
            return false;
        }
        if (domain >= PhysxInstanceDomain.Count)
        {
            error = "An identity requires a known instance domain.";
            return false;
        }
        if (!PhysxUtf8.IsValid(utf8Path))
        {
            error = "A prim path must be UTF-8 without embedded null bytes.";
            return false;
        }

        ulong hash = OffsetBasis;
        for (int index = 0; index < utf8Path.Length; index++)
        {
            hash ^= utf8Path[index];
            hash *= Prime;
        }
        hash = Mix(hash, (uint)domain);
        hash = Mix(hash, instanceIndex);
        id = hash == PhysxAbi.InvalidId ? Prime : hash;
        error = null;
        return true;
    }

    /// <summary>Encodes a prim path as strict UTF-8.</summary>
    /// <exception cref="ArgumentException">The path is not encodable as UTF-8.</exception>
    internal static byte[] Encode(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            return StrictUtf8.GetBytes(path);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(
                "A prim path must be encodable as UTF-8 without unpaired surrogates.",
                nameof(path),
                exception);
        }
    }

    private static ulong Mix(ulong hash, uint value)
    {
        for (int shift = 0; shift < 32; shift += 8)
        {
            hash ^= (value >> shift) & 0xFFU;
            hash *= Prime;
        }
        return hash;
    }
}

/// <summary>
/// Describes one row of the stable identity table.
/// </summary>
internal sealed record PhysxIdentityEntry(
    ulong Id,
    string Path,
    PhysxInstanceDomain Domain,
    uint InstanceIndex,
    uint PathOffset,
    uint PathLength)
{
    /// <inheritdoc/>
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"0x{Id:x16} {Path} [{Domain}:{InstanceIndex}]");
}

/// <summary>
/// Accumulates the string section and the identity section of a build page.
/// </summary>
/// <remarks>
/// The table preserves complete prim and instance addressing for every identity, so a caller can map
/// a result or an event back to the exact authored prim, native instance, or <c>PointInstancer</c>
/// element without expanding identity into unstable traversal order. Re-adding the same address
/// returns the existing identity; two different addresses that hash to the same identity are
/// reported as a collision instead of silently aliasing.
/// </remarks>
internal sealed class PhysxIdentityTable : IDisposable
{
    private readonly List<PhysxIdentityEntry> _entries = [];
    private readonly Dictionary<ulong, int> _byId = [];
    private readonly Dictionary<(string Path, PhysxInstanceDomain Domain, uint Index), int> _byAddress = [];
    private readonly PhysxPooledBuffer _strings = new();
    private bool _disposed;

    /// <summary>Gets the identity rows in insertion order.</summary>
    internal IReadOnlyList<PhysxIdentityEntry> Entries => _entries;

    /// <summary>Gets the accumulated UTF-8 string section bytes.</summary>
    internal ReadOnlySpan<byte> StringBytes => _strings.Written;

    /// <summary>Gets the number of identities.</summary>
    internal int Count => _entries.Count;

    /// <summary>Adds an address to the table and returns its stable identity.</summary>
    /// <exception cref="InvalidOperationException">Two distinct addresses collide.</exception>
    internal ulong Add(string path, PhysxInstanceDomain domain = PhysxInstanceDomain.Prim, uint instanceIndex = 0)
    {
        if (!TryAdd(path, domain, instanceIndex, out ulong id, out string? error))
        {
            throw new InvalidOperationException(error);
        }
        return id;
    }

    /// <summary>Adds an address to the table without throwing on a collision.</summary>
    internal bool TryAdd(
        string path,
        PhysxInstanceDomain domain,
        uint instanceIndex,
        out ulong id,
        [NotNullWhen(false)] out string? error)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(path);

        id = PhysxAbi.InvalidId;
        if (_byAddress.TryGetValue((path, domain, instanceIndex), out int existing))
        {
            id = _entries[existing].Id;
            error = null;
            return true;
        }

        byte[] utf8;
        try
        {
            utf8 = PhysxIdentity.Encode(path);
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }

        if (!PhysxIdentity.TryCompute(utf8, domain, instanceIndex, out ulong computed, out error))
        {
            return false;
        }

        if (_byId.TryGetValue(computed, out int collision))
        {
            PhysxIdentityEntry other = _entries[collision];
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Identity 0x{computed:x16} for '{path}' collides with '{other.Path}'.");
            return false;
        }

        if (_strings.Length + utf8.Length > int.MaxValue)
        {
            error = "The identity string section exceeded its addressable size.";
            return false;
        }

        var entry = new PhysxIdentityEntry(
            computed,
            path,
            domain,
            instanceIndex,
            (uint)_strings.Length,
            (uint)utf8.Length);
        _strings.Write(utf8);
        _byId[computed] = _entries.Count;
        _byAddress[(path, domain, instanceIndex)] = _entries.Count;
        _entries.Add(entry);
        id = computed;
        error = null;
        return true;
    }

    /// <summary>Looks up the full address of an identity.</summary>
    internal bool TryGet(ulong id, [NotNullWhen(true)] out PhysxIdentityEntry? entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_byId.TryGetValue(id, out int index))
        {
            entry = _entries[index];
            return true;
        }

        entry = null;
        return false;
    }

    /// <summary>Copies the identity records in insertion order.</summary>
    internal PhysxIdentityRecord[] ToRecords()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var records = new PhysxIdentityRecord[_entries.Count];
        for (int index = 0; index < _entries.Count; index++)
        {
            PhysxIdentityEntry entry = _entries[index];
            records[index] = new PhysxIdentityRecord
            {
                Id = entry.Id,
                PathOffset = entry.PathOffset,
                PathLength = entry.PathLength,
                InstanceDomain = (uint)entry.Domain,
                InstanceIndex = entry.InstanceIndex
            };
        }
        return records;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _strings.Dispose();
    }
}
