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

    // The undo journal the table is mutated under while a page is being applied.
    // The scene state applies a page as a transaction, and identity is retained
    // here as well as there: a page rejected after its second mesh was accepted
    // would otherwise leave this table holding an identity, a token range and a
    // revision for a prim the scene does not retain, and the next page would then
    // be validated against evidence no producer ever published.
    private readonly List<PickUndo> _undo = [];
    private bool _journaling;
    private int _undoRecordCount;
    private int _undoFailureOrdinal = -1;
    private uint _undoNextToken;
    private ulong _undoAllocatedRangeCount;
    private ulong _undoRevision;
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

    /// <summary>
    /// Gets the active token range one resolved instance uses for one subprim
    /// target.
    /// </summary>
    /// <remarks>
    /// An empty range means the record refuses that target;
    /// <see cref="TryGetSubprimSupport"/> names why. The three ranges are
    /// allocated from one monotonic token space and never overlap, so one GPU
    /// pass per target resolves without a second table.
    /// </remarks>
    public bool TryGetRange(
        string path,
        int instanceIndex,
        SilkPickSubprimKind kind,
        out SilkPickTokenRange range)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentOutOfRangeException.ThrowIfNegative(instanceIndex);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        if (_entries.TryGetValue((path, instanceIndex), out MeshEntry? entry))
        {
            range = entry.RangeFor(kind);
            return true;
        }
        range = default;
        return false;
    }

    /// <summary>
    /// Gets which exact subprim targets one resolved instance answers, and the
    /// named reason it refuses the rest.
    /// </summary>
    public bool TryGetSubprimSupport(
        string path,
        int instanceIndex,
        out SilkPickSubprimSupport support)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentOutOfRangeException.ThrowIfNegative(instanceIndex);
        if (_entries.TryGetValue((path, instanceIndex), out MeshEntry? entry))
        {
            support = new SilkPickSubprimSupport(
                entry.Identity.SubprimIdentity,
                entry.Identity.SubprimUnsupported);
            return true;
        }
        support = default;
        return false;
    }

    /// <summary>
    /// Determines whether one resolved instance answers a face pick with an
    /// authored face index.
    /// </summary>
    /// <remarks>
    /// A basis-curve resource, a UsdGeomPoints resource, and a wireframe line
    /// list do not: their emitted primitives are curve segments and points, and
    /// no authored mesh face exists behind them. They are still drawn into the
    /// face pass, because a curve in front of a wall has to keep the wall's
    /// faces hidden, but they are drawn as pure occluders that write depth and
    /// no token.
    /// </remarks>
    public bool AnswersFacePicks(string path, int instanceIndex)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentOutOfRangeException.ThrowIfNegative(instanceIndex);
        return _entries.TryGetValue((path, instanceIndex), out MeshEntry? entry) &&
            entry.Identity.HasAuthoredFaces;
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
            identity = candidate.Identity.Resolve(candidate.Kind, triangleIndex);
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

            // An instancer that is retargeted, or a level that appears or
            // disappears, moves the record to a different place in the
            // instancing hierarchy. hdSilk derives the record's instance ID from
            // its instancer path, so that move changes the instance ID too --
            // and the two changes arrive together in one republished record.
            //
            // Read on its own, a changed instance ID looks like the impossible
            // case the stable-identity check exists to catch. Read together with
            // the changed instancer path or chain it is the opposite: coherent
            // evidence that this is a different scene instance reusing the same
            // path and index, which is an identity replacement. Rotating the
            // tokens is the correct answer, and refusing the page would have
            // rejected a scene edit the renderer is required to follow.
            bool instancerMoved = !previous.HasSameInstancerIdentity(mesh);
            if (!implicitRecreation)
            {
                if (!instancerMoved)
                {
                    // With the instancing position unchanged there is no
                    // evidence for a replacement, so a changed instance ID is
                    // exactly the corruption it looks like and is still refused.
                    EnsureStableIdentity(previous, mesh);
                }
                if (mesh.TopologyRevision == previous.TopologyRevision)
                {
                    _topologyFingerprintComparisonCount++;
                    if (!previous.HasSameTopology(mesh))
                    {
                        throw new InvalidDataException(
                            $"Silk pick topology changed for '{mesh.Path}' " +
                            "without a new topology revision.");
                    }

                    // A record can keep its composite instance ordinal and its
                    // topology revision while its instancing position changes:
                    // an instancer is retargeted, or an outer level appears. The
                    // retained identity is then stale even though nothing the
                    // topology check looks at moved, so the identity is replaced
                    // and fresh tokens are allocated below. Reinterpreting the
                    // old tokens would answer a pick with an instancing chain the
                    // scene no longer contains, and a readback already in flight
                    // must be recognised as stale rather than re-resolved.
                    if (!instancerMoved)
                    {
                        return existing.Range;
                    }
                }
            }

            EnsureRevisionAvailable();
            CompactPickIdentity replacement = CompactPickIdentity.CopyFrom(mesh);
            (SilkPickTokenRange range, TokenRangeEntry? tokenRange) =
                AllocateRange(replacement);
            (SilkPickTokenRange edgeRange, TokenRangeEntry? edgeTokenRange) =
                AllocateRange(replacement, SilkPickSubprimKind.Edge);
            (SilkPickTokenRange pointRange, TokenRangeEntry? pointTokenRange) =
                AllocateRange(replacement, SilkPickSubprimKind.Point);
            Deactivate(existing.TokenRange);
            Deactivate(existing.EdgeTokenRange);
            Deactivate(existing.PointTokenRange);
            if (previous.PrimId != mesh.PrimId)
            {
                RemovePrimIdPath(previous.PrimId);
                SetPrimIdPath(mesh.PrimId, mesh.Path);
            }
            JournalEntryMutation(mesh.Path, mesh.InstanceIndex, existing);
            existing.Identity = replacement;
            existing.Range = range;
            existing.TokenRange = tokenRange;
            existing.EdgeRange = edgeRange;
            existing.EdgeTokenRange = edgeTokenRange;
            existing.PointRange = pointRange;
            existing.PointTokenRange = pointTokenRange;
            _revision++;
            return range;
        }

        EnsureRevisionAvailable();
        CompactPickIdentity identity = CompactPickIdentity.CopyFrom(mesh);
        (SilkPickTokenRange newRange, TokenRangeEntry? newTokenRange) =
            AllocateRange(identity);
        (SilkPickTokenRange newEdgeRange, TokenRangeEntry? newEdgeTokenRange) =
            AllocateRange(identity, SilkPickSubprimKind.Edge);
        (SilkPickTokenRange newPointRange, TokenRangeEntry? newPointTokenRange) =
            AllocateRange(identity, SilkPickSubprimKind.Point);
        AddEntry(
            mesh.Path,
            mesh.InstanceIndex,
            new MeshEntry(identity, newRange, newTokenRange)
            {
                EdgeRange = newEdgeRange,
                EdgeTokenRange = newEdgeTokenRange,
                PointRange = newPointRange,
                PointTokenRange = newPointTokenRange
            });
        _ = _instanceCountsByPath.TryGetValue(mesh.Path, out int instanceCount);
        SetInstanceCount(mesh.Path, instanceCount + 1);
        SetPrimIdPath(mesh.PrimId, mesh.Path);
        SetHashPath(mesh.StableHash, mesh.Path);
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
        RemoveEntry(path, instanceIndex, entry);
        if (_instanceCountsByPath.TryGetValue(path, out int instanceCount) &&
            instanceCount <= 1)
        {
            RemoveInstanceCount(path);
            RemovePrimIdPath(entry.Identity.PrimId);
            RemoveHashPath(entry.Identity.StableHash);
        }
        else
        {
            SetInstanceCount(path, instanceCount - 1);
        }
        Deactivate(entry.TokenRange);
        Deactivate(entry.EdgeTokenRange);
        Deactivate(entry.PointTokenRange);
        _revision++;
        return true;
    }

    /// <summary>Opens the undo journal one page's identity writes are made under.</summary>
    internal void BeginTransaction()
    {
        _undo.Clear();
        _undoNextToken = _nextToken;
        _undoAllocatedRangeCount = _allocatedRangeCount;
        _undoRevision = _revision;
        _undoRecordCount = 0;
        _journaling = true;
    }

    /// <summary>
    /// Makes the <paramref name="ordinal"/>th journal record of the next page
    /// throw, so a test can prove that a page whose journal itself fails leaves
    /// the table exactly as it found it.
    /// </summary>
    /// <remarks>
    /// A journal entry is recorded <em>before</em> the write it undoes, so a
    /// record that fails must find nothing to undo. This exists because that
    /// ordering is invisible from the outside: a table that published a token
    /// range and then failed to record its undo is indistinguishable from one
    /// that never allocated, right up until the rollback leaks the range.
    /// </remarks>
    internal void FailUndoRecordForTesting(int ordinal) =>
        _undoFailureOrdinal = ordinal;

    /// <summary>Records one journal entry, before the write it undoes happens.</summary>
    private void RecordUndo(in PickUndo entry)
    {
        if (_undoRecordCount++ == _undoFailureOrdinal)
        {
            _undoFailureOrdinal = -1;
            throw new InvalidOperationException(
                "The injected Silk pick journal record failed.");
        }
        _undo.Add(entry);
    }

    /// <summary>Accepts every identity write the page made.</summary>
    internal void CommitTransaction()
    {
        _journaling = false;
        _undoFailureOrdinal = -1;
        _undo.Clear();
    }

    /// <summary>Puts every identity write the rejected page made back, newest first.</summary>
    internal void RollbackTransaction()
    {
        _journaling = false;
        _undoFailureOrdinal = -1;
        for (int index = _undo.Count - 1; index >= 0; index--)
        {
            PickUndo entry = _undo[index];
            switch (entry.Kind)
            {
                case PickUndoKind.Entry:
                    if (entry.Existed)
                    {
                        _entries[(entry.Path!, entry.Index)] = (MeshEntry)entry.Value!;
                    }
                    else
                    {
                        _ = _entries.Remove((entry.Path!, entry.Index));
                    }
                    break;
                case PickUndoKind.EntryFields:
                    var target = (MeshEntry)entry.Target!;
                    var snapshot = (MeshEntry)entry.Value!;
                    target.RestoreFrom(snapshot);
                    break;
                case PickUndoKind.InstanceCount:
                    if (entry.Existed)
                    {
                        _instanceCountsByPath[entry.Path!] = entry.Index;
                    }
                    else
                    {
                        _ = _instanceCountsByPath.Remove(entry.Path!);
                    }
                    break;
                case PickUndoKind.PrimIdPath:
                    if (entry.Existed)
                    {
                        _pathsByPrimId[entry.Index] = (string)entry.Value!;
                    }
                    else
                    {
                        _ = _pathsByPrimId.Remove(entry.Index);
                    }
                    break;
                case PickUndoKind.HashPath:
                    if (entry.Existed)
                    {
                        _pathsByHash[entry.Key] = (string)entry.Value!;
                    }
                    else
                    {
                        _ = _pathsByHash.Remove(entry.Key);
                    }
                    break;
                case PickUndoKind.RangeAdded:
                    _ranges.RemoveAt(entry.Index);
                    break;
                case PickUndoKind.RangeRemoved:
                    _ranges.Insert(entry.Index, (TokenRangeEntry)entry.Value!);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"The Silk pick undo journal has an unknown entry {entry.Kind}.");
            }
        }

        _undo.Clear();
        _nextToken = _undoNextToken;
        _allocatedRangeCount = _undoAllocatedRangeCount;
        _revision = _undoRevision;
    }

    private void AddEntry(string path, int instanceIndex, MeshEntry entry)
    {
        if (_journaling)
        {
            RecordUndo(new PickUndo(
                PickUndoKind.Entry, path, instanceIndex, 0, null, null, false));
        }
        _entries.Add((path, instanceIndex), entry);
    }

    private void RemoveEntry(string path, int instanceIndex, MeshEntry entry)
    {
        if (_journaling)
        {
            RecordUndo(new PickUndo(
                PickUndoKind.Entry, path, instanceIndex, 0, null, entry, true));
        }
        _ = _entries.Remove((path, instanceIndex));
    }

    private void JournalEntryMutation(string path, int instanceIndex, MeshEntry entry)
    {
        if (!_journaling)
        {
            return;
        }
        RecordUndo(new PickUndo(
            PickUndoKind.EntryFields,
            path,
            instanceIndex,
            0,
            entry,
            entry.Snapshot(),
            true));
    }

    private void SetInstanceCount(string path, int count)
    {
        if (_journaling)
        {
            bool existed = _instanceCountsByPath.TryGetValue(path, out int previous);
            RecordUndo(new PickUndo(
                PickUndoKind.InstanceCount, path, previous, 0, null, null, existed));
        }
        _instanceCountsByPath[path] = count;
    }

    private void RemoveInstanceCount(string path)
    {
        if (_journaling)
        {
            bool existed = _instanceCountsByPath.TryGetValue(path, out int previous);
            RecordUndo(new PickUndo(
                PickUndoKind.InstanceCount, path, previous, 0, null, null, existed));
        }
        _ = _instanceCountsByPath.Remove(path);
    }

    private void SetPrimIdPath(int primId, string path)
    {
        if (_journaling)
        {
            bool existed = _pathsByPrimId.TryGetValue(primId, out string? previous);
            RecordUndo(new PickUndo(
                PickUndoKind.PrimIdPath, null, primId, 0, null, previous, existed));
        }
        _pathsByPrimId[primId] = path;
    }

    private void RemovePrimIdPath(int primId)
    {
        if (_journaling)
        {
            bool existed = _pathsByPrimId.TryGetValue(primId, out string? previous);
            RecordUndo(new PickUndo(
                PickUndoKind.PrimIdPath, null, primId, 0, null, previous, existed));
        }
        _ = _pathsByPrimId.Remove(primId);
    }

    private void SetHashPath(ulong hash, string path)
    {
        if (_journaling)
        {
            bool existed = _pathsByHash.TryGetValue(hash, out string? previous);
            RecordUndo(new PickUndo(
                PickUndoKind.HashPath, null, 0, hash, null, previous, existed));
        }
        _pathsByHash[hash] = path;
    }

    private void RemoveHashPath(ulong hash)
    {
        if (_journaling)
        {
            bool existed = _pathsByHash.TryGetValue(hash, out string? previous);
            RecordUndo(new PickUndo(
                PickUndoKind.HashPath, null, 0, hash, null, previous, existed));
        }
        _ = _pathsByHash.Remove(hash);
    }

    private enum PickUndoKind
    {
        Entry,
        EntryFields,
        InstanceCount,
        PrimIdPath,
        HashPath,
        RangeAdded,
        RangeRemoved,
    }

    private readonly record struct PickUndo(
        PickUndoKind Kind,
        string? Path,
        int Index,
        ulong Key,
        object? Target,
        object? Value,
        bool Existed);

    /// <summary>
    /// Allocates the whole-resource token range one rendered resource is drawn
    /// into the surface pass with.
    /// </summary>
    /// <remarks>
    /// It is allocated for every rendered topology. The tokens of a triangulated
    /// mesh resolve to the authored face each triangle came from; the tokens of
    /// a curve or point resource resolve to the whole resource. Refusing a
    /// non-triangle resource a range, as this allocator used to, left the
    /// surface pass with no token to draw a curve or a point cloud with at all,
    /// so a curve in front of a wall could neither be picked nor occlude the
    /// wall behind it.
    /// </remarks>
    private (SilkPickTokenRange Range, TokenRangeEntry? Entry) AllocateRange(
        CompactPickIdentity identity) =>
        AllocateRange(
            identity,
            SilkPickSubprimKind.Primitive,
            identity.PrimitiveKind);

    private (SilkPickTokenRange Range, TokenRangeEntry? Entry) AllocateRange(
        CompactPickIdentity identity,
        SilkPickSubprimKind kind) =>
        AllocateRange(identity, kind, kind);

    private (SilkPickTokenRange Range, TokenRangeEntry? Entry) AllocateRange(
        CompactPickIdentity identity,
        SilkPickSubprimKind countKind,
        SilkPickSubprimKind resolveKind)
    {
        uint count = checked((uint)identity.CountFor(countKind));
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

        var entry = new TokenRangeEntry(first, count, identity, resolveKind);

        // The undo is recorded before the range is published, not after: a record
        // that failed after the range was already active would leave a token
        // range no rollback can retire, and the table would hand out a token that
        // resolves to an identity the scene does not retain. If publishing itself
        // fails the record is popped again, because it would otherwise name an
        // index the list never grew to.
        if (_journaling)
        {
            RecordUndo(new PickUndo(
                PickUndoKind.RangeAdded, null, _ranges.Count, 0, null, null, true));
            try
            {
                _ranges.Add(entry);
            }
            catch
            {
                _undo.RemoveAt(_undo.Count - 1);
                throw;
            }
        }
        else
        {
            _ranges.Add(entry);
        }
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
                if (_journaling)
                {
                    RecordUndo(new PickUndo(
                        PickUndoKind.RangeRemoved, null, middle, 0, null, candidate, true));
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
            mesh.TopologyKind is not SilkTopologyKind.TriangleList and
                not SilkTopologyKind.LineList and
                not SilkTopologyKind.PointList ||
            mesh.TopologyRevision == 0 ||
            mesh.Points.Length % 3 != 0 ||
            mesh.Indices.Length % IndicesPerPrimitive(mesh.TopologyKind) != 0 ||
            mesh.TriangleSubprims.Length !=
                mesh.Indices.Length / IndicesPerPrimitive(mesh.TopologyKind))
        {
            throw new InvalidDataException(
                $"Mesh '{mesh.Path}' has invalid Silk pick identity or topology.");
        }

        ValidateSubprimIdentity(mesh);
    }

    /// <summary>
    /// Refuses a retained mesh whose subprim-identity claim does not match the
    /// tables it published.
    /// </summary>
    /// <remarks>
    /// The wire decoder already rejects a malformed page. This is the second,
    /// independent check the retained table needs anyway: a mesh built directly
    /// by a host or a test never crossed the wire, and a claim without a table
    /// behind it would allocate an authored token range no draw could fill.
    /// </remarks>
    private static void ValidateSubprimIdentity(SilkMeshData mesh)
    {
        bool claimsEdges = mesh.SubprimIdentity.HasFlag(SilkSubprimIdentity.Edge);
        bool claimsPoints = mesh.SubprimIdentity.HasFlag(SilkSubprimIdentity.Point);
        if (claimsEdges != (mesh.CornerEdges.Length != 0) ||
            claimsPoints != (mesh.PointOrigins.Length != 0))
        {
            throw new InvalidDataException(
                $"Mesh '{mesh.Path}' claims a subprim target with no table behind it.");
        }
        if (claimsPoints && mesh.PointOrigins.Length != mesh.Points.Length / 3)
        {
            throw new InvalidDataException(
                $"Mesh '{mesh.Path}' has an invalid Silk authored point table.");
        }
        if (claimsEdges &&
            mesh.CornerEdges.Length !=
                mesh.TriangleSubprims.Length * CornerEdgesPerPrimitive(mesh.TopologyKind))
        {
            throw new InvalidDataException(
                $"Mesh '{mesh.Path}' has an invalid Silk authored edge table.");
        }

        // The authored counts are validated as "one past the largest index the
        // table names" rather than merely as an upper bound. Nothing downstream
        // is ever sized by them -- the drawn tables are sized by the published
        // entries -- but a record whose declared authored space is larger than
        // its own table would hand a consumer an authored index no entry names.
        ValidateAuthoredCount(
            mesh.Path,
            mesh.PointOrigins.Span,
            mesh.AuthoredPointCount,
            "point");
        ValidateAuthoredCount(
            mesh.Path,
            mesh.CornerEdges.Span,
            mesh.AuthoredEdgeCount,
            "edge");
    }

    private static void ValidateAuthoredCount(
        string path,
        ReadOnlySpan<int> table,
        int authoredCount,
        string name)
    {
        long largest = -1;
        for (int index = 0; index < table.Length; index++)
        {
            int entry = table[index];
            if (entry < -1 || entry >= authoredCount)
            {
                throw new InvalidDataException(
                    $"Mesh '{path}' has a Silk {name} entry outside its authored " +
                    $"{name} count.");
            }
            largest = Math.Max(largest, entry);
        }
        if (authoredCount != largest + 1)
        {
            throw new InvalidDataException(
                $"Mesh '{path}' declares an authored {name} count that is not one " +
                $"past the largest authored index its table names.");
        }
    }

    private static int CornerEdgesPerPrimitive(SilkTopologyKind topologyKind) =>
        topologyKind switch
        {
            SilkTopologyKind.TriangleList => 3,
            SilkTopologyKind.LineList => 1,
            _ => 0
        };

    private static int IndicesPerPrimitive(SilkTopologyKind topologyKind) =>
        topologyKind switch
        {
            SilkTopologyKind.TriangleList => 3,
            SilkTopologyKind.LineList => 2,
            SilkTopologyKind.PointList => 1,
            _ => 0
        };

    /// <summary>
    /// Refuses a republished record whose identity changed with nothing to
    /// explain the change.
    /// </summary>
    /// <remarks>
    /// This is only reached when the record's instancing position is unchanged.
    /// An instance ID is derived from the instancer path, so with that path and
    /// the whole ordered chain identical the ID cannot legitimately move; a
    /// record that claims otherwise is describing two different instances under
    /// one identity and is refused. The caller handles the coherent case -- a
    /// changed ID alongside a changed path or chain -- as an identity
    /// replacement instead.
    /// </remarks>
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

        /// <summary>The authored-edge token range, empty when edges are refused.</summary>
        internal SilkPickTokenRange EdgeRange { get; set; }

        internal TokenRangeEntry? EdgeTokenRange { get; set; }

        /// <summary>The authored-point token range, empty when points are refused.</summary>
        internal SilkPickTokenRange PointRange { get; set; }

        internal TokenRangeEntry? PointTokenRange { get; set; }

        internal SilkPickTokenRange RangeFor(SilkPickSubprimKind kind) => kind switch
        {
            // The whole-resource range is the same range a triangulated mesh
            // answers a face pick from, because every emitted triangle names the
            // authored face it came from. A curve or point resource has no
            // authored face at all, so it answers the whole-resource target and
            // refuses the face one: a face request that lands on a curve is
            // answered as a miss rather than with an index the scene never
            // authored.
            SilkPickSubprimKind.Face =>
                Identity.HasAuthoredFaces ? Range : default,
            SilkPickSubprimKind.Primitive => Range,
            SilkPickSubprimKind.Edge => EdgeRange,
            SilkPickSubprimKind.Point => PointRange,
            _ => default
        };

        internal MeshEntry Snapshot() => new(Identity, Range, TokenRange)
        {
            EdgeRange = EdgeRange,
            EdgeTokenRange = EdgeTokenRange,
            PointRange = PointRange,
            PointTokenRange = PointTokenRange
        };

        internal void RestoreFrom(MeshEntry snapshot)
        {
            Identity = snapshot.Identity;
            Range = snapshot.Range;
            TokenRange = snapshot.TokenRange;
            EdgeRange = snapshot.EdgeRange;
            EdgeTokenRange = snapshot.EdgeTokenRange;
            PointRange = snapshot.PointRange;
            PointTokenRange = snapshot.PointTokenRange;
        }
    }

    private sealed class TokenRangeEntry(
        uint firstToken,
        uint tokenCount,
        CompactPickIdentity identity,
        SilkPickSubprimKind kind)
    {
        internal uint FirstToken { get; } = firstToken;

        internal uint TokenCount { get; } = tokenCount;

        internal CompactPickIdentity Identity { get; } = identity;

        /// <summary>The subprim target the tokens of this range answer.</summary>
        internal SilkPickSubprimKind Kind { get; } = kind;
    }

    private sealed class CompactPickIdentity
    {
        // The authored component every emitted primitive of the whole resource
        // came from: the authored face of a triangle, the authored curve segment
        // of a line, or the authored point of a point. It is retained for every
        // topology, not only the triangulated ones, because the whole-resource
        // token range is what lets a curve or a point cloud be drawn into the
        // surface pass and occlude what is behind it.
        private readonly int[] _primitiveSubprims;
        private readonly int[] _edgeSubprims;
        private readonly int[] _pointSubprims;
        private readonly SilkInstancerContextEntry[] _instancerContext;
        private readonly bool _hasAuthoredFaces;

        private CompactPickIdentity(
            string path,
            string instancerPath,
            SilkInstancerContextEntry[] instancerContext,
            int primId,
            ulong stableHash,
            int instanceId,
            int instanceIndex,
            ulong topologyRevision,
            int[] primitiveSubprims,
            int[] edgeSubprims,
            int[] pointSubprims,
            bool hasAuthoredFaces,
            ulong topologyFingerprint,
            SilkSubprimIdentity subprimIdentity,
            SilkSubprimUnsupportedReason subprimUnsupported)
        {
            Path = path;
            InstancerPath = instancerPath;
            _instancerContext = instancerContext;
            PrimId = primId;
            StableHash = stableHash;
            InstanceId = instanceId;
            InstanceIndex = instanceIndex;
            TopologyRevision = topologyRevision;
            _primitiveSubprims = primitiveSubprims;
            _edgeSubprims = edgeSubprims;
            _pointSubprims = pointSubprims;
            _hasAuthoredFaces = hasAuthoredFaces;
            TopologyFingerprint = topologyFingerprint;
            SubprimIdentity = subprimIdentity;
            SubprimUnsupported = subprimUnsupported;
        }

        internal string Path { get; }

        /// <summary>The authoritative owning instancer path, empty when none.</summary>
        internal string InstancerPath { get; }

        /// <summary>
        /// The complete ordered instancing chain, outermost level first. Empty
        /// when the prim has no instancer.
        /// </summary>
        internal ReadOnlySpan<SilkInstancerContextEntry> InstancerContext =>
            _instancerContext;

        internal int PrimId { get; }

        internal ulong StableHash { get; }

        internal int InstanceId { get; }

        internal int InstanceIndex { get; }

        internal ulong TopologyRevision { get; }

        internal ulong TopologyFingerprint { get; }

        internal SilkSubprimIdentity SubprimIdentity { get; }

        internal SilkSubprimUnsupportedReason SubprimUnsupported { get; }

        internal int AuthoredEdgeCount => _edgeSubprims.Length;

        internal int AuthoredPointCount => _pointSubprims.Length;

        /// <summary>The number of primitives the whole resource emits.</summary>
        internal int PrimitiveCount => _primitiveSubprims.Length;

        /// <summary>Whether the emitted primitives name authored mesh faces.</summary>
        internal bool HasAuthoredFaces => _hasAuthoredFaces;

        /// <summary>
        /// The kind the whole-resource token range resolves with.
        /// </summary>
        /// <remarks>
        /// A triangulated mesh resolves its whole-resource tokens to the
        /// authored face each triangle came from, which is what a face pick
        /// needs and what a prim pick can simply ignore. A curve or point
        /// resource has no authored face, so its tokens resolve to the whole
        /// resource and a face request that lands on one is answered as a miss
        /// rather than with a fabricated face index.
        /// </remarks>
        internal SilkPickSubprimKind PrimitiveKind => _hasAuthoredFaces
            ? SilkPickSubprimKind.Face
            : SilkPickSubprimKind.Primitive;

        internal int CountFor(SilkPickSubprimKind kind) => kind switch
        {
            SilkPickSubprimKind.Face => _hasAuthoredFaces ? PrimitiveCount : 0,
            SilkPickSubprimKind.Edge => _edgeSubprims.Length,
            SilkPickSubprimKind.Point => _pointSubprims.Length,
            SilkPickSubprimKind.Primitive => PrimitiveCount,
            _ => 0
        };

        internal static CompactPickIdentity CopyFrom(SilkMeshData mesh)
        {
            // Every array here is the retained record's own immutable array, and
            // a lightweight instance record shares its prototype's. Copying them
            // per record made a prototype's subprim tables cost one full copy per
            // instance -- O(points x instances) -- for tables every instance
            // already describes identically.
            SilkSubprimTables tables = mesh.SubprimTables;
            return new CompactPickIdentity(
                mesh.Path,
                mesh.InstancerPath,
                mesh.InstancerContextArray ?? [],
                mesh.PrimId,
                mesh.StableHash,
                mesh.InstanceId,
                mesh.InstanceIndex,
                mesh.TopologyRevision,
                mesh.TriangleSubprimArray,
                tables.AuthoredEdges,
                tables.AuthoredPoints,
                mesh.TopologyKind == SilkTopologyKind.TriangleList,
                mesh.TopologyFingerprint,
                mesh.SubprimIdentity,
                mesh.SubprimUnsupported);
        }

        /// <summary>
        /// Determines whether one record still names the same instancing
        /// position this identity was allocated for.
        /// </summary>
        /// <remarks>
        /// The instancer path and the ordered chain are the only description
        /// that decodes back to a scene instance, and neither is covered by the
        /// topology fingerprint or by the stable identity check: a record can
        /// keep its prim ID, its hash, its composite instance ordinal and its
        /// topology revision while moving to a different instancer or gaining an
        /// outer instancing level. A token that kept resolving through the old
        /// chain would then report an instance the scene no longer contains.
        /// </remarks>
        internal bool HasSameInstancerIdentity(SilkMeshData mesh)
        {
            if (!string.Equals(InstancerPath, mesh.InstancerPath, StringComparison.Ordinal))
            {
                return false;
            }
            IReadOnlyList<SilkInstancerContextEntry> chain = mesh.InstancerContext;
            if (_instancerContext.Length != chain.Count)
            {
                return false;
            }
            for (int level = 0; level < _instancerContext.Length; level++)
            {
                if (_instancerContext[level] != chain[level])
                {
                    return false;
                }
            }
            return true;
        }

        internal bool HasSameTopology(SilkMeshData mesh)
        {
            if (PrimitiveCount != mesh.TriangleSubprims.Length ||
                TopologyFingerprint != mesh.TopologyFingerprint ||
                SubprimIdentity != mesh.SubprimIdentity)
            {
                return false;
            }
            SilkSubprimTables tables = mesh.SubprimTables;
            return _edgeSubprims.AsSpan().SequenceEqual(tables.AuthoredEdges) &&
                _pointSubprims.AsSpan().SequenceEqual(tables.AuthoredPoints);
        }

        internal SilkPickIdentity Resolve(int triangleIndex) =>
            Resolve(SilkPickSubprimKind.Face, triangleIndex);

        /// <summary>
        /// Resolves one offset inside a token range to authoritative identity.
        /// </summary>
        /// <remarks>
        /// Every kind resolves through its own authored table rather than
        /// returning the offset itself. A face offset is a triangle index the
        /// authored-face table maps onto the authored face the triangle was
        /// triangulated from; an edge or point offset is a draw-order index the
        /// authored table maps onto the authored component, which is not the
        /// same number whenever the mesh authors a component no emitted
        /// primitive covers. A whole-resource offset maps onto the authored
        /// curve segment or authored point the emitted primitive came from,
        /// which is real authored data even though a prim pick reports only the
        /// prim and the instance.
        /// </remarks>
        internal SilkPickIdentity Resolve(SilkPickSubprimKind kind, int offset) =>
            new(
                Path,
                PrimId,
                StableHash,
                InstanceId,
                InstanceIndex,
                TopologyRevision,
                kind switch
                {
                    SilkPickSubprimKind.Edge => _edgeSubprims[offset],
                    SilkPickSubprimKind.Point => _pointSubprims[offset],
                    _ => _primitiveSubprims[offset]
                },
                kind,
                InstancerPath.Length == 0 ? null : InstancerPath,
                _instancerContext.Length == 0 ? null : _instancerContext);
    }
}

/// <summary>The subprim target one retained Silk pick token range answers.</summary>
public enum SilkPickSubprimKind
{
    /// <summary>Tokens resolve to the authored face a triangle came from.</summary>
    Face,

    /// <summary>Tokens resolve to an authored mesh edge index.</summary>
    Edge,

    /// <summary>Tokens resolve to an authored point index.</summary>
    Point,

    /// <summary>
    /// Tokens resolve to the whole rendered resource rather than to an authored
    /// component of it.
    /// </summary>
    /// <remarks>
    /// This is the kind a basis-curve resource, a UsdGeomPoints resource, and a
    /// wireframe line list answer with. Their emitted primitives are curve
    /// segments and points, not authored mesh faces, so a token from one of them
    /// names the prim (and the instance) and nothing finer. The range exists for
    /// every rendered topology, not only the ones with authored faces, because
    /// the surface pass has to draw every depth-writing resource: a curve in
    /// front of a wall must occlude the wall's faces, edges and points, and a
    /// resource with no token range at all could not be drawn into that pass.
    /// </remarks>
    Primitive
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
public readonly record struct SilkPickIdentity
{
    private readonly SilkInstancerContextEntry[]? _instancerContext;

    /// <summary>Initializes one resolved identity.</summary>
    /// <param name="path">The authoritative absolute prim path.</param>
    /// <param name="primId">Hydra's explicit Rprim identifier.</param>
    /// <param name="stableHash">The FNV-1a path hash, an index only.</param>
    /// <param name="instanceId">The diagnostic-only owning instancer hash.</param>
    /// <param name="instanceIndex">The retained record's instance ordinal.</param>
    /// <param name="topologyRevision">The topology revision the token was allocated under.</param>
    /// <param name="subprimIndex">The authored component the token names.</param>
    /// <param name="subprimKind">What <paramref name="subprimIndex"/> names.</param>
    /// <param name="instancerPath">The innermost instancer path, when instanced.</param>
    /// <param name="instancerContext">
    /// The complete ordered instancing chain, outermost level first.
    /// </param>
    public SilkPickIdentity(
        string path,
        int primId,
        ulong stableHash,
        int instanceId,
        int instanceIndex,
        ulong topologyRevision,
        int subprimIndex,
        SilkPickSubprimKind subprimKind = SilkPickSubprimKind.Face,
        string? instancerPath = null,
        ReadOnlySpan<SilkInstancerContextEntry> instancerContext = default)
    {
        Path = path;
        PrimId = primId;
        StableHash = stableHash;
        InstanceId = instanceId;
        InstanceIndex = instanceIndex;
        TopologyRevision = topologyRevision;
        SubprimIndex = subprimIndex;
        SubprimKind = subprimKind;
        InstancerPath = instancerPath;
        _instancerContext = instancerContext.IsEmpty
            ? null
            : instancerContext.ToArray();
    }

    /// <summary>
    /// Initializes one resolved identity that shares an already immutable chain.
    /// </summary>
    /// <remarks>
    /// The retained table owns one chain per record and hands the same array to
    /// every token it resolves, so copying it per resolve would allocate once
    /// per pick for data that never changes. The array is never handed out: only
    /// a read-only span over it is.
    /// </remarks>
    internal SilkPickIdentity(
        string path,
        int primId,
        ulong stableHash,
        int instanceId,
        int instanceIndex,
        ulong topologyRevision,
        int subprimIndex,
        SilkPickSubprimKind subprimKind,
        string? instancerPath,
        SilkInstancerContextEntry[]? instancerContext)
    {
        Path = path;
        PrimId = primId;
        StableHash = stableHash;
        InstanceId = instanceId;
        InstanceIndex = instanceIndex;
        TopologyRevision = topologyRevision;
        SubprimIndex = subprimIndex;
        SubprimKind = subprimKind;
        InstancerPath = instancerPath;
        _instancerContext =
            instancerContext is { Length: > 0 } ? instancerContext : null;
    }

    /// <summary>Gets the authoritative absolute prim path.</summary>
    public string Path { get; init; }

    /// <summary>Gets Hydra's explicit Rprim identifier.</summary>
    public int PrimId { get; init; }

    /// <summary>Gets the FNV-1a path hash, which is an identity index only.</summary>
    public ulong StableHash { get; init; }

    /// <summary>Gets the diagnostic-only owning instancer hash.</summary>
    public int InstanceId { get; init; }

    /// <summary>
    /// Gets the retained record's instance ordinal.
    /// </summary>
    /// <remarks>
    /// For a single-level instancer this is the instance's own index inside
    /// <see cref="InstancerPath"/>. For a nested one it is the hdSilk composite
    /// ordinal that keys the retained tables, which is not any level's own
    /// index; <see cref="InstancerContext"/> is then the only description that
    /// names a scene instance.
    /// </remarks>
    public int InstanceIndex { get; init; }

    /// <summary>Gets the topology revision the token was allocated under.</summary>
    public ulong TopologyRevision { get; init; }

    /// <summary>Gets the authored component the token names.</summary>
    public int SubprimIndex { get; init; }

    /// <summary>Gets what <see cref="SubprimIndex"/> names.</summary>
    public SilkPickSubprimKind SubprimKind { get; init; }

    /// <summary>Gets the innermost instancer path, when the hit is an instance.</summary>
    public string? InstancerPath { get; init; }

    /// <summary>
    /// Gets the complete ordered instancing chain, outermost level first and
    /// innermost last. Empty when the prim has no instancer.
    /// </summary>
    public ReadOnlySpan<SilkInstancerContextEntry> InstancerContext =>
        _instancerContext;

    /// <summary>Compares complete identity, including every instancing level.</summary>
    /// <remarks>
    /// The chain is compared by content rather than by array reference. The
    /// compiler-generated comparison of a record struct compares its fields with
    /// the default equality comparer, and for an array that is reference
    /// equality: two identities resolved from two token readbacks of the same
    /// record would then be unequal, and using them as dictionary keys -- which
    /// is exactly how a caller de-duplicates repeated picks of one instance --
    /// would grow a new entry per pick.
    /// </remarks>
    public bool Equals(SilkPickIdentity other)
    {
        if (!string.Equals(Path, other.Path, StringComparison.Ordinal) ||
            PrimId != other.PrimId ||
            StableHash != other.StableHash ||
            InstanceId != other.InstanceId ||
            InstanceIndex != other.InstanceIndex ||
            TopologyRevision != other.TopologyRevision ||
            SubprimIndex != other.SubprimIndex ||
            SubprimKind != other.SubprimKind ||
            !string.Equals(InstancerPath, other.InstancerPath, StringComparison.Ordinal))
        {
            return false;
        }
        return InstancerContext.SequenceEqual(other.InstancerContext);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(Path, StringComparer.Ordinal);
        hash.Add(PrimId);
        hash.Add(StableHash);
        hash.Add(InstanceId);
        hash.Add(InstanceIndex);
        hash.Add(TopologyRevision);
        hash.Add(SubprimIndex);
        hash.Add(SubprimKind);
        hash.Add(InstancerPath, StringComparer.Ordinal);
        ReadOnlySpan<SilkInstancerContextEntry> chain = InstancerContext;
        hash.Add(chain.Length);
        for (int index = 0; index < chain.Length; index++)
        {
            hash.Add(chain[index]);
        }
        return hash.ToHashCode();
    }
}

/// <summary>
/// Reports whether one retained mesh answers an exact subprim pick target, and
/// why it does not.
/// </summary>
public readonly record struct SilkPickSubprimSupport(
    SilkSubprimIdentity Identity,
    SilkSubprimUnsupportedReason Unsupported)
{
    /// <summary>Determines whether one target is answered with authored identity.</summary>
    public bool Supports(SilkPickSubprimKind kind) => kind switch
    {
        SilkPickSubprimKind.Face => Identity.HasFlag(SilkSubprimIdentity.Face),
        SilkPickSubprimKind.Edge => Identity.HasFlag(SilkSubprimIdentity.Edge),
        SilkPickSubprimKind.Point => Identity.HasFlag(SilkSubprimIdentity.Point),

        // Every rendered resource answers the whole-resource target: the prim
        // path and the instance index are always authored identity, whatever the
        // record could or could not say about its components.
        SilkPickSubprimKind.Primitive => true,
        _ => false
    };
}
