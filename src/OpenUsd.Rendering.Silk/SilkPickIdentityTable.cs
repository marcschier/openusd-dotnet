// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Retains nonzero 32-bit token ranges for a future per-triangle GPU ID pass.
/// </summary>
/// <remarks>
/// Token zero remains the background/miss value. Allocated ranges are never
/// reused during the table lifetime, so tokens invalidated by removal,
/// topology change, or implicit recreation cannot resolve to another identity.
/// Deactivated ranges are removed from the active search structure entirely.
/// </remarks>
public sealed class SilkPickIdentityTable
{
    private readonly uint _maximumToken;
    private readonly Func<string, ulong>? _pathHasher;

    // Page ABI 3 publishes one record per resolved instance of a prototype, so identity is
    // (path, instance index). Keying by path alone made the second instance of a prototype
    // look like the same mesh changing identity without recreation evidence.
    private readonly Dictionary<(string Path, int InstanceIndex), MeshEntry> _entries = [];

    // The prim ID and path hash are shared by every instance of a prototype, so they are
    // retired only once the last instance is gone.
    private readonly Dictionary<string, int> _instanceCountsByPath =
        new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _pathsByPrimId = [];
    private readonly Dictionary<ulong, string> _pathsByHash = [];
    private readonly List<TokenRangeEntry> _ranges = [];
    private uint _nextToken = 1;
    private ulong _allocatedRangeCount;
    private ulong _revision;
    private ulong _topologyFingerprintComparisonCount;

    /// <summary>Initializes a table using tokens 1 through <paramref name="maximumToken"/>.</summary>
    public SilkPickIdentityTable(uint maximumToken = uint.MaxValue)
        : this(maximumToken, pathHasher: null)
    {
    }

    internal SilkPickIdentityTable(
        uint maximumToken,
        Func<string, ulong>? pathHasher)
    {
        ArgumentOutOfRangeException.ThrowIfZero(maximumToken);
        _maximumToken = maximumToken;
        _pathHasher = pathHasher;
    }

    /// <summary>Gets the number of active, non-empty token ranges.</summary>
    public int ActiveRangeCount => _ranges.Count;

    /// <summary>Gets the monotonic number of non-empty ranges ever allocated.</summary>
    public ulong AllocatedRangeCount => _allocatedRangeCount;

    /// <summary>
    /// Gets the monotonic active identity/topology revision.
    /// </summary>
    /// <remarks>
    /// New identities, topology changes, implicit recreation, and removal
    /// advance this value. Property-only mesh updates leave it unchanged.
    /// </remarks>
    public ulong Revision => _revision;

    internal ulong TopologyFingerprintComparisonCount =>
        _topologyFingerprintComparisonCount;

    /// <summary>
    /// Gets the active token range for the non-instanced record of an authoritative path.
    /// </summary>
    public bool TryGetRange(string path, out SilkPickTokenRange range) =>
        TryGetRange(path, 0, out range);

    /// <summary>
    /// Gets the active token range for one resolved instance of an authoritative path.
    /// A prim with no instancer publishes instance index zero.
    /// </summary>
    public bool TryGetRange(string path, int instanceIndex, out SilkPickTokenRange range)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentOutOfRangeException.ThrowIfNegative(instanceIndex);
        if (_entries.TryGetValue((path, instanceIndex), out MeshEntry? entry))
        {
            range = entry.Range;
            return true;
        }
        range = default;
        return false;
    }

    /// <summary>Resolves one active future-GPU token without allocating.</summary>
    public bool TryResolve(uint token, out SilkPickIdentity identity)
    {
        if (token == 0 || _ranges.Count == 0)
        {
            identity = default;
            return false;
        }

        int low = 0;
        int high = _ranges.Count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            TokenRangeEntry candidate = _ranges[middle];
            if (token < candidate.FirstToken)
            {
                high = middle - 1;
                continue;
            }
            if ((ulong)token >=
                (ulong)candidate.FirstToken + candidate.TokenCount)
            {
                low = middle + 1;
                continue;
            }
            int triangleIndex = checked((int)(token - candidate.FirstToken));
            identity = candidate.Identity.Resolve(triangleIndex);
            return true;
        }

        identity = default;
        return false;
    }

    internal SilkPickTokenRange Upsert(SilkMeshData mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ValidateMesh(mesh);

        ulong expectedHash = _pathHasher is null
            ? SilkWireFormat.ComputeStableHash(mesh.Path)
            : _pathHasher(mesh.Path);
        if (mesh.StableHash != expectedHash)
        {
            throw new InvalidDataException(
                $"Mesh '{mesh.Path}' has stable hash 0x{mesh.StableHash:X16}, " +
                $"but its path-derived hash is 0x{expectedHash:X16}.");
        }
        if (_pathsByHash.TryGetValue(mesh.StableHash, out string? hashPath) &&
            !string.Equals(hashPath, mesh.Path, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Silk pick hash 0x{mesh.StableHash:X16} collides for " +
                $"'{hashPath}' and '{mesh.Path}'.");
        }
        if (_pathsByPrimId.TryGetValue(mesh.PrimId, out string? primPath) &&
            !string.Equals(primPath, mesh.Path, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Silk pick prim ID {mesh.PrimId} collides for " +
                $"'{primPath}' and '{mesh.Path}'.");
        }

        if (_entries.TryGetValue(
            (mesh.Path, mesh.InstanceIndex),
            out MeshEntry? existing))
        {
            CompactPickIdentity previous = existing.Identity;
            if (previous.StableHash != mesh.StableHash)
            {
                throw new InvalidDataException(
                    $"Silk pick hash changed for '{mesh.Path}' without removal.");
            }

            bool implicitRecreation =
                previous.PrimId != mesh.PrimId ||
                mesh.TopologyRevision < previous.TopologyRevision;
            if (!implicitRecreation)
            {
                EnsureStableIdentity(previous, mesh);
                if (mesh.TopologyRevision == previous.TopologyRevision)
                {
                    _topologyFingerprintComparisonCount++;
                    if (!previous.HasSameTopology(mesh))
                    {
                        throw new InvalidDataException(
                            $"Silk pick topology changed for '{mesh.Path}' " +
                            "without a new topology revision.");
                    }
                    return existing.Range;
                }
            }

            EnsureRevisionAvailable();
            CompactPickIdentity replacement = CompactPickIdentity.CopyFrom(mesh);
            (SilkPickTokenRange range, TokenRangeEntry? tokenRange) =
                AllocateRange(replacement);
            Deactivate(existing.TokenRange);
            if (previous.PrimId != mesh.PrimId)
            {
                _pathsByPrimId.Remove(previous.PrimId);
                _pathsByPrimId[mesh.PrimId] = mesh.Path;
            }
            existing.Identity = replacement;
            existing.Range = range;
            existing.TokenRange = tokenRange;
            _revision++;
            return range;
        }

        EnsureRevisionAvailable();
        CompactPickIdentity identity = CompactPickIdentity.CopyFrom(mesh);
        (SilkPickTokenRange newRange, TokenRangeEntry? newTokenRange) =
            AllocateRange(identity);
        _entries.Add(
            (mesh.Path, mesh.InstanceIndex),
            new MeshEntry(identity, newRange, newTokenRange));
        _instanceCountsByPath.TryGetValue(mesh.Path, out int instanceCount);
        _instanceCountsByPath[mesh.Path] = instanceCount + 1;
        _pathsByPrimId[mesh.PrimId] = mesh.Path;
        _pathsByHash[mesh.StableHash] = mesh.Path;
        _revision++;
        return newRange;
    }

    internal bool Remove(string path, int instanceIndex)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentOutOfRangeException.ThrowIfNegative(instanceIndex);
        if (!_entries.TryGetValue((path, instanceIndex), out MeshEntry? entry))
        {
            return false;
        }
        EnsureRevisionAvailable();
        _entries.Remove((path, instanceIndex));
        if (_instanceCountsByPath.TryGetValue(path, out int instanceCount) &&
            instanceCount <= 1)
        {
            _instanceCountsByPath.Remove(path);
            _pathsByPrimId.Remove(entry.Identity.PrimId);
            _pathsByHash.Remove(entry.Identity.StableHash);
        }
        else
        {
            _instanceCountsByPath[path] = instanceCount - 1;
        }
        Deactivate(entry.TokenRange);
        _revision++;
        return true;
    }

    private (SilkPickTokenRange Range, TokenRangeEntry? Entry) AllocateRange(
        CompactPickIdentity identity)
    {
        uint count = checked((uint)identity.TriangleCount);
        if (count == 0)
        {
            return (
                new SilkPickTokenRange(0, 0, identity.TopologyRevision),
                null);
        }
        if (_nextToken == 0)
        {
            throw new InvalidOperationException(
                "The Silk 32-bit pick token space is exhausted.");
        }

        uint first = _nextToken;
        ulong last = (ulong)first + count - 1;
        if (last > _maximumToken)
        {
            throw new InvalidOperationException(
                "The Silk 32-bit pick token space is exhausted.");
        }
        if (_ranges.Count != 0)
        {
            TokenRangeEntry previous = _ranges[^1];
            ulong previousEnd =
                (ulong)previous.FirstToken + previous.TokenCount;
            if (first < previousEnd)
            {
                throw new InvalidOperationException(
                    "The Silk pick token allocator produced overlapping ranges.");
            }
        }

        var entry = new TokenRangeEntry(first, count, identity);
        _ranges.Add(entry);
        _allocatedRangeCount++;
        _nextToken = last == _maximumToken ? 0 : checked((uint)last + 1);
        return (
            new SilkPickTokenRange(first, count, identity.TopologyRevision),
            entry);
    }

    private void Deactivate(TokenRangeEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        int low = 0;
        int high = _ranges.Count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            TokenRangeEntry candidate = _ranges[middle];
            if (entry.FirstToken < candidate.FirstToken)
            {
                high = middle - 1;
            }
            else if (entry.FirstToken > candidate.FirstToken)
            {
                low = middle + 1;
            }
            else
            {
                if (!ReferenceEquals(entry, candidate))
                {
                    throw new InvalidOperationException(
                        "The Silk pick range index contains a token collision.");
                }
                _ranges.RemoveAt(middle);
                return;
            }
        }

        throw new InvalidOperationException(
            "The Silk pick range being deactivated is not active.");
    }

    private void EnsureRevisionAvailable()
    {
        if (_revision == ulong.MaxValue)
        {
            throw new InvalidOperationException(
                "The Silk pick identity revision is exhausted.");
        }
    }

    private static void ValidateMesh(SilkMeshData mesh)
    {
        if (mesh.PrimId < 0 ||
            mesh.Path.Length == 0 ||
            mesh.Path[0] != '/' ||
            mesh.InstanceId < 0 ||
            mesh.InstanceIndex < 0 ||
            mesh.TopologyKind != SilkTopologyKind.TriangleList ||
            mesh.TopologyRevision == 0 ||
            mesh.Points.Length % 3 != 0 ||
            mesh.Indices.Length % 3 != 0 ||
            mesh.TriangleSubprims.Length != mesh.Indices.Length / 3)
        {
            throw new InvalidDataException(
                $"Mesh '{mesh.Path}' has invalid Silk pick identity or topology.");
        }
    }

    private static void EnsureStableIdentity(
        CompactPickIdentity previous,
        SilkMeshData current)
    {
        if (previous.PrimId != current.PrimId ||
            previous.StableHash != current.StableHash ||
            previous.InstanceId != current.InstanceId ||
            previous.InstanceIndex != current.InstanceIndex)
        {
            throw new InvalidDataException(
                $"Silk pick identity changed for '{current.Path}' " +
                "without recreation evidence.");
        }
    }

    private sealed class MeshEntry(
        CompactPickIdentity identity,
        SilkPickTokenRange range,
        TokenRangeEntry? tokenRange)
    {
        internal CompactPickIdentity Identity { get; set; } = identity;

        internal SilkPickTokenRange Range { get; set; } = range;

        internal TokenRangeEntry? TokenRange { get; set; } = tokenRange;
    }

    private sealed class TokenRangeEntry(
        uint firstToken,
        uint tokenCount,
        CompactPickIdentity identity)
    {
        internal uint FirstToken { get; } = firstToken;

        internal uint TokenCount { get; } = tokenCount;

        internal CompactPickIdentity Identity { get; } = identity;
    }

    private sealed class CompactPickIdentity
    {
        private readonly int[] _triangleSubprims;

        private CompactPickIdentity(
            string path,
            int primId,
            ulong stableHash,
            int instanceId,
            int instanceIndex,
            ulong topologyRevision,
            int[] triangleSubprims,
            ulong topologyFingerprint)
        {
            Path = path;
            PrimId = primId;
            StableHash = stableHash;
            InstanceId = instanceId;
            InstanceIndex = instanceIndex;
            TopologyRevision = topologyRevision;
            _triangleSubprims = triangleSubprims;
            TopologyFingerprint = topologyFingerprint;
        }

        internal string Path { get; }

        internal int PrimId { get; }

        internal ulong StableHash { get; }

        internal int InstanceId { get; }

        internal int InstanceIndex { get; }

        internal ulong TopologyRevision { get; }

        internal ulong TopologyFingerprint { get; }

        internal int TriangleCount => _triangleSubprims.Length;

        internal static CompactPickIdentity CopyFrom(SilkMeshData mesh)
        {
            return new CompactPickIdentity(
                mesh.Path,
                mesh.PrimId,
                mesh.StableHash,
                mesh.InstanceId,
                mesh.InstanceIndex,
                mesh.TopologyRevision,
                mesh.TriangleSubprims.ToArray(),
                mesh.TopologyFingerprint);
        }

        internal bool HasSameTopology(SilkMeshData mesh) =>
            TriangleCount == mesh.TriangleCount &&
            TopologyFingerprint == mesh.TopologyFingerprint;

        internal SilkPickIdentity Resolve(int triangleIndex) =>
            new(
                Path,
                PrimId,
                StableHash,
                InstanceId,
                InstanceIndex,
                TopologyRevision,
                _triangleSubprims[triangleIndex]);

    }
}

/// <summary>Describes one retained contiguous per-triangle GPU-token range.</summary>
public readonly record struct SilkPickTokenRange(
    uint FirstToken,
    uint TokenCount,
    ulong TopologyRevision)
{
    /// <summary>Gets the inclusive final token, or zero for an empty range.</summary>
    public uint LastToken => TokenCount == 0
        ? 0
        : checked(FirstToken + TokenCount - 1);
}

/// <summary>Resolves one future GPU token to authoritative immutable identity.</summary>
public readonly record struct SilkPickIdentity(
    string Path,
    int PrimId,
    ulong StableHash,
    int InstanceId,
    int InstanceIndex,
    ulong TopologyRevision,
    int SubprimIndex);
