// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Retains the latest scene resources produced by hdSilk command pages.
/// </summary>
public sealed class SilkSceneState
{
    private readonly Dictionary<ulong, SilkMeshData> _meshes = [];
    private readonly Dictionary<string, SilkMeshData> _meshesByPath =
        new(StringComparer.Ordinal);
    private readonly Dictionary<ulong, string> _pathsByHash = [];
    private readonly Func<string, ulong>? _pathHasher;

    /// <summary>Initializes an empty retained scene and pick identity table.</summary>
    public SilkSceneState()
        : this(pathHasher: null)
    {
    }

    internal SilkSceneState(Func<string, ulong>? pathHasher)
    {
        _pathHasher = pathHasher;
        PickIdentities = new SilkPickIdentityTable(uint.MaxValue, pathHasher);
    }

    /// <summary>Gets the latest page revision.</summary>
    public ulong Revision { get; private set; }

    /// <summary>Gets the retained frame state.</summary>
    public SilkFrameState Frame { get; } = new();

    /// <summary>Gets retained meshes by explicit Hydra prim ID.</summary>
    public IReadOnlyDictionary<ulong, SilkMeshData> Meshes => _meshes;

    /// <summary>Gets retained meshes by authoritative USD prim path.</summary>
    public IReadOnlyDictionary<string, SilkMeshData> MeshesByPath => _meshesByPath;

    /// <summary>Gets retained future-GPU token ranges and resolved identities.</summary>
    public SilkPickIdentityTable PickIdentities { get; }

    /// <summary>Applies one dirty page and returns resource-change counts.</summary>
    public SilkSceneDelta Apply(OpenUsdSilkPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return Apply(page.GetEnumerator(), page.Revision);
    }

    /// <summary>Applies command bytes from a test or recorded page.</summary>
    public SilkSceneDelta Apply(
        ReadOnlySpan<byte> data,
        uint commandCount,
        ulong revision)
    {
        return Apply(SilkCommandParser.Enumerate(data, commandCount), revision);
    }

    private SilkSceneDelta Apply(SilkCommandEnumerator commands, ulong revision)
    {
        List<ulong>? upserts = null;
        List<ulong>? removals = null;
        using (commands)
        {
            while (commands.MoveNext())
            {
                switch (commands.Current.Type)
                {
                    case SilkCommandType.Frame:
                        Frame.Update(commands.Current.AsFrame());
                        break;
                    case SilkCommandType.MeshUpsert:
                        SilkMeshData mesh = SilkMeshData.CopyFrom(
                            commands.Current.AsMeshUpsert());
                        if (UpsertMesh(mesh) is { } replacedId)
                        {
                            (removals ??= []).Add(replacedId);
                        }
                        (upserts ??= []).Add(mesh.Id);
                        break;
                    case SilkCommandType.MeshRemove:
                        SilkMeshRemoveCommand removal =
                            commands.Current.AsMeshRemove();
                        if (RemoveMesh(removal, out ulong removedId))
                        {
                            (removals ??= []).Add(removedId);
                        }
                        break;
                    default:
                        throw new InvalidDataException(
                            $"Unsupported hdSilk command {commands.Current.Type}.");
                }
            }
        }

        Revision = revision;
        return new SilkSceneDelta(
            upserts?.ToArray() ?? [],
            removals?.ToArray() ?? []);
    }

    private ulong? UpsertMesh(SilkMeshData mesh)
    {
        ulong expectedHash = _pathHasher is null
            ? SilkWireFormat.ComputeStableHash(mesh.Path)
            : _pathHasher(mesh.Path);
        if (mesh.StableHash != expectedHash)
        {
            throw new InvalidDataException(
                $"hdSilk path '{mesh.Path}' has stable hash " +
                $"0x{mesh.StableHash:X16}, expected 0x{expectedHash:X16}.");
        }
        if (_pathsByHash.TryGetValue(mesh.StableHash, out string? hashPath) &&
            !string.Equals(hashPath, mesh.Path, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"hdSilk path hash 0x{mesh.StableHash:X16} collides for " +
                $"'{hashPath}' and '{mesh.Path}'.");
        }

        if (_meshes.TryGetValue(mesh.Id, out SilkMeshData? primMesh) &&
            !string.Equals(primMesh.Path, mesh.Path, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"hdSilk prim ID {mesh.PrimId} is shared by " +
                $"'{primMesh.Path}' and '{mesh.Path}'.");
        }

        ulong? replacedId = null;
        if (_meshesByPath.TryGetValue(mesh.Path, out SilkMeshData? pathMesh))
        {
            if (pathMesh.StableHash != mesh.StableHash)
            {
                throw new InvalidDataException(
                    $"hdSilk changed the path-derived hash for '{mesh.Path}'.");
            }

            bool implicitRecreation =
                pathMesh.PrimId != mesh.PrimId ||
                mesh.TopologyRevision < pathMesh.TopologyRevision;
            if (implicitRecreation && pathMesh.Id != mesh.Id)
            {
                replacedId = pathMesh.Id;
            }
        }

        PickIdentities.Upsert(mesh);
        if (replacedId is { } oldId)
        {
            _meshes.Remove(oldId);
        }
        _meshes[mesh.Id] = mesh;
        _meshesByPath[mesh.Path] = mesh;
        _pathsByHash[mesh.StableHash] = mesh.Path;
        return replacedId;
    }

    private bool RemoveMesh(
        SilkMeshRemoveCommand removal,
        out ulong removedId)
    {
        removedId = 0;
        ulong expectedHash = _pathHasher is null
            ? SilkWireFormat.ComputeStableHash(removal.Path)
            : _pathHasher(removal.Path);
        if (removal.StableHash != expectedHash)
        {
            throw new InvalidDataException(
                $"hdSilk removal path '{removal.Path}' has stable hash " +
                $"0x{removal.StableHash:X16}, expected 0x{expectedHash:X16}.");
        }
        if (_pathsByHash.TryGetValue(removal.StableHash, out string? hashPath) &&
            !string.Equals(hashPath, removal.Path, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"hdSilk removal hash 0x{removal.StableHash:X16} names " +
                $"'{hashPath}', not '{removal.Path}'.");
        }
        if (!_meshesByPath.TryGetValue(removal.Path, out SilkMeshData? mesh))
        {
            return false;
        }
        if (mesh.StableHash != removal.StableHash)
        {
            throw new InvalidDataException(
                $"hdSilk removal for '{removal.Path}' has a different stable hash.");
        }

        removedId = mesh.Id;
        _meshesByPath.Remove(mesh.Path);
        _meshes.Remove(mesh.Id);
        _pathsByHash.Remove(mesh.StableHash);
        PickIdentities.Remove(mesh.Path);
        return true;
    }
}

/// <summary>
/// Retained camera and viewport state.
/// </summary>
public sealed class SilkFrameState
{
    private readonly double[] _view = new double[16];
    private readonly double[] _projection = new double[16];

    /// <summary>Gets the viewport width.</summary>
    public int Width { get; private set; }

    /// <summary>Gets the viewport height.</summary>
    public int Height { get; private set; }

    /// <summary>Gets the row-major world-to-view matrix.</summary>
    public ReadOnlyMemory<double> View => _view;

    /// <summary>Gets the row-major projection matrix.</summary>
    public ReadOnlyMemory<double> Projection => _projection;

    /// <summary>Gets the revision of the retained camera or viewport state.</summary>
    public ulong Revision { get; private set; }

    internal void Update(SilkFrameCommand command)
    {
        bool changed = Width != command.Width || Height != command.Height;
        Width = command.Width;
        Height = command.Height;
        for (int i = 0; i < 16; i++)
        {
            double view = command.GetViewElement(i);
            double projection = command.GetProjectionElement(i);
            changed |= _view[i] != view || _projection[i] != projection;
            _view[i] = view;
            _projection[i] = projection;
        }
        if (changed)
        {
            Revision++;
        }
    }
}

/// <summary>
/// Retained triangulated mesh data.
/// </summary>
public sealed class SilkMeshData
{
    private readonly float[] _points;
    private readonly uint[] _indices;
    private readonly int[] _triangleSubprims;
    private readonly float[] _displayColor;
    private readonly double[] _transform;

    /// <summary>Initializes immutable retained mesh data.</summary>
    public SilkMeshData(
        int primId,
        string path,
        ulong stableHash,
        int instanceId,
        int instanceIndex,
        SilkTopologyKind topologyKind,
        ulong topologyRevision,
        float[] points,
        uint[] indices,
        int[] triangleSubprims,
        float[] displayColor,
        double[] transform)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(triangleSubprims);
        ArgumentNullException.ThrowIfNull(displayColor);
        ArgumentNullException.ThrowIfNull(transform);
        PrimId = primId;
        Path = path;
        StableHash = stableHash;
        InstanceId = instanceId;
        InstanceIndex = instanceIndex;
        TopologyKind = topologyKind;
        TopologyRevision = topologyRevision;
        _points = (float[])points.Clone();
        _indices = (uint[])indices.Clone();
        _triangleSubprims = (int[])triangleSubprims.Clone();
        _displayColor = (float[])displayColor.Clone();
        _transform = (double[])transform.Clone();
        TopologyFingerprint = SilkTopologyFingerprint.Compute(
            _points.Length / 3,
            _indices,
            _triangleSubprims);
    }

    private SilkMeshData(
        int primId,
        string path,
        ulong stableHash,
        int instanceId,
        int instanceIndex,
        SilkTopologyKind topologyKind,
        ulong topologyRevision,
        float[] points,
        uint[] indices,
        int[] triangleSubprims,
        float[] displayColor,
        double[] transform,
        ulong topologyFingerprint)
    {
        PrimId = primId;
        Path = path;
        StableHash = stableHash;
        InstanceId = instanceId;
        InstanceIndex = instanceIndex;
        TopologyKind = topologyKind;
        TopologyRevision = topologyRevision;
        _points = points;
        _indices = indices;
        _triangleSubprims = triangleSubprims;
        _displayColor = displayColor;
        _transform = transform;
        TopologyFingerprint = topologyFingerprint;
    }

    /// <summary>
    /// Initializes non-instanced triangle data for callers that do not retain wire identity.
    /// </summary>
    public SilkMeshData(
        ulong primId,
        string path,
        float[] points,
        uint[] indices,
        float[] displayColor,
        double[] transform)
        : this(
            checked((int)primId),
            path,
            SilkWireFormat.ComputeStableHash(path),
            0,
            0,
            SilkTopologyKind.TriangleList,
            1,
            points,
            indices,
            new int[indices.Length / 3],
            displayColor,
            transform)
    {
    }

    /// <summary>Gets Hydra's explicit Rprim identifier.</summary>
    public int PrimId { get; }

    /// <summary>Gets the explicit prim ID as the retained resource key.</summary>
    public ulong Id => checked((ulong)PrimId);

    /// <summary>Gets the authoritative USD prim path.</summary>
    public string Path { get; }

    /// <summary>Gets the collision-checked FNV-1a path hash index.</summary>
    public ulong StableHash { get; }

    /// <summary>Gets the reserved instance ID, which is zero in page ABI v2.</summary>
    public int InstanceId { get; }

    /// <summary>Gets the reserved instance index, which is zero in page ABI v2.</summary>
    public int InstanceIndex { get; }

    /// <summary>Gets the emitted topology kind.</summary>
    public SilkTopologyKind TopologyKind { get; }

    /// <summary>Gets the topology-only mesh revision.</summary>
    public ulong TopologyRevision { get; }

    /// <summary>Gets a deterministic defensive 64-bit topology fingerprint.</summary>
    /// <remarks>
    /// The topology revision is authoritative. This non-cryptographic value
    /// detects accidental same-revision conflicts in constant time, but a
    /// deliberate or extremely unlikely 64-bit collision can evade that
    /// defensive check.
    /// </remarks>
    public ulong TopologyFingerprint { get; }

    /// <summary>Gets a defensive read-only view of point components.</summary>
    public ReadOnlyMemory<float> Points => _points;

    /// <summary>Gets a defensive read-only view of triangle indices.</summary>
    public ReadOnlyMemory<uint> Indices => _indices;

    /// <summary>Gets one authored USD face/subprim per emitted triangle.</summary>
    public ReadOnlyMemory<int> TriangleSubprims => _triangleSubprims;

    /// <summary>Gets the canonical display color.</summary>
    public ReadOnlyMemory<float> DisplayColor => _displayColor;

    /// <summary>Gets the row-major local-to-world transform.</summary>
    public ReadOnlyMemory<double> Transform => _transform;

    /// <summary>Gets the emitted triangle count.</summary>
    public int TriangleCount => _triangleSubprims.Length;

    internal static SilkMeshData CopyFrom(SilkMeshUpsertCommand command)
    {
        var points = new float[command.PointCount * 3];
        for (int point = 0; point < command.PointCount; point++)
        {
            for (int component = 0; component < 3; component++)
            {
                points[(point * 3) + component] = command.GetPointComponent(point, component);
            }
        }

        var indices = new uint[command.IndexCount];
        var fingerprint = new SilkTopologyFingerprintBuilder(
            command.PointCount,
            command.IndexCount,
            command.TriangleCount);
        for (int i = 0; i < indices.Length; i++)
        {
            uint index = command.GetIndex(i);
            indices[i] = index;
            fingerprint.AddIndex(index);
        }

        var triangleSubprims = new int[command.TriangleCount];
        for (int triangle = 0; triangle < triangleSubprims.Length; triangle++)
        {
            int subprim = command.GetTriangleSubprim(triangle);
            triangleSubprims[triangle] = subprim;
            fingerprint.AddSubprim(subprim);
        }

        var color = new float[4];
        for (int i = 0; i < color.Length; i++)
        {
            color[i] = command.GetDisplayColor(i);
        }

        var transform = new double[16];
        for (int i = 0; i < transform.Length; i++)
        {
            transform[i] = command.GetTransformElement(i);
        }

        return new SilkMeshData(
            command.PrimId,
            command.Path,
            command.StableHash,
            command.InstanceId,
            command.InstanceIndex,
            command.TopologyKind,
            command.TopologyRevision,
            points,
            indices,
            triangleSubprims,
            color,
            transform,
            fingerprint.Value);
    }
}

internal static class SilkTopologyFingerprint
{
    internal static ulong Compute(
        int pointCount,
        ReadOnlySpan<uint> indices,
        ReadOnlySpan<int> triangleSubprims)
    {
        var builder = new SilkTopologyFingerprintBuilder(
            pointCount,
            indices.Length,
            triangleSubprims.Length);
        foreach (uint index in indices)
        {
            builder.AddIndex(index);
        }
        foreach (int subprim in triangleSubprims)
        {
            builder.AddSubprim(subprim);
        }
        return builder.Value;
    }
}

internal struct SilkTopologyFingerprintBuilder
{
    private const ulong OffsetBasis = 14695981039346656037;
    private const ulong Prime = 1099511628211;
    private ulong _value;

    internal SilkTopologyFingerprintBuilder(
        int pointCount,
        int indexCount,
        int triangleCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pointCount);
        ArgumentOutOfRangeException.ThrowIfNegative(indexCount);
        ArgumentOutOfRangeException.ThrowIfNegative(triangleCount);
        _value = OffsetBasis;
        AddUInt32(0x53494C4B);
        AddUInt32(checked((uint)pointCount));
        AddUInt32(checked((uint)indexCount));
        AddUInt32(checked((uint)triangleCount));
    }

    internal readonly ulong Value => _value;

    internal void AddIndex(uint index) => AddUInt32(index);

    internal void AddSubprim(int subprim)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(subprim);
        AddUInt32(checked((uint)subprim));
    }

    private void AddUInt32(uint value)
    {
        unchecked
        {
            _value = (_value ^ (byte)value) * Prime;
            _value = (_value ^ (byte)(value >> 8)) * Prime;
            _value = (_value ^ (byte)(value >> 16)) * Prime;
            _value = (_value ^ (byte)(value >> 24)) * Prime;
        }
    }
}

/// <summary>
/// Counts resource changes applied from one command page.
/// </summary>
public readonly record struct SilkSceneDelta(
    ReadOnlyMemory<ulong> UpsertedMeshIds,
    ReadOnlyMemory<ulong> RemovedMeshIds)
{
    /// <summary>Gets the number of created or updated meshes.</summary>
    public int MeshUpserts => UpsertedMeshIds.Length;

    /// <summary>Gets the number of removed meshes.</summary>
    public int MeshRemovals => RemovedMeshIds.Length;
}

/// <summary>
/// Owns backend buffers corresponding to retained hdSilk mesh resources.
/// </summary>
public sealed class SilkSceneGpuResources : IDisposable
{
    private readonly ISilkGraphicsDevice _device;
    private readonly Dictionary<ulong, SilkMeshGpuResource> _meshes = [];
    private bool _disposed;

    /// <summary>Initializes GPU resource retention for one backend device.</summary>
    public SilkSceneGpuResources(ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        SilkManagedDiagnostics.GpuSceneCreated();
    }

    /// <summary>Gets uploaded mesh resources by explicit Hydra prim ID.</summary>
    public IReadOnlyDictionary<ulong, SilkMeshGpuResource> Meshes => _meshes;

    internal Dictionary<ulong, SilkMeshGpuResource>.ValueCollection MeshValues =>
        _meshes.Values;

    /// <summary>Gets the revision of retained mesh-resource membership or metadata.</summary>
    public ulong Revision { get; private set; }

    /// <summary>Gets cumulative upload and resource-churn diagnostics.</summary>
    public SilkSceneGpuStatistics Statistics => new(
        _meshes.Count,
        _geometryBuilds,
        _vertexUploads,
        _indexUploads,
        _uniformUploads);

    private ulong _geometryBuilds;
    private ulong _vertexUploads;
    private ulong _indexUploads;
    private ulong _uniformUploads;

    /// <summary>Applies only the mesh changes reported by a scene delta.</summary>
    public void Apply(SilkSceneState scene, SilkSceneDelta delta)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        bool changed = delta.MeshRemovals != 0 || delta.MeshUpserts != 0;

        foreach (ulong id in delta.RemovedMeshIds.Span)
        {
            if (_meshes.Remove(id, out SilkMeshGpuResource? removed))
            {
                removed.Dispose();
            }
        }

        foreach (ulong id in delta.UpsertedMeshIds.Span)
        {
            if (!scene.Meshes.TryGetValue(id, out SilkMeshData? mesh))
            {
                throw new InvalidDataException(
                    $"Scene delta references missing mesh {id}.");
            }

            if (_meshes.TryGetValue(id, out SilkMeshGpuResource? existing) &&
                existing.HasSameGeometry(mesh))
            {
                existing.UpdateMesh(mesh);
                continue;
            }

            SilkMeshGpuResource replacement = CreateMesh(mesh);
            if (_meshes.Remove(id, out SilkMeshGpuResource? previous))
            {
                previous.Dispose();
            }
            _meshes.Add(id, replacement);
        }
        if (changed)
        {
            Revision++;
        }
    }

    /// <summary>Updates only changed per-mesh SceneParameters constants.</summary>
    public int UpdateUniforms(SilkFrameState frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);
        int uploads = 0;
        Span<byte> constants = stackalloc byte[SilkSceneUniformWriter.ByteSize];
        foreach (SilkMeshGpuResource mesh in _meshes.Values)
        {
            if (mesh.UpdateUniform(frame, constants))
            {
                uploads++;
                _uniformUploads++;
            }
        }
        return uploads;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        foreach (SilkMeshGpuResource mesh in _meshes.Values)
        {
            mesh.Dispose();
        }
        _meshes.Clear();
        _disposed = true;
        SilkManagedDiagnostics.GpuSceneDestroyed();
    }

    private SilkMeshGpuResource CreateMesh(SilkMeshData mesh)
    {
        SilkMeshGeometry geometry = SilkMeshGeometryBuilder.Build(mesh);
        ReadOnlySpan<byte> vertexBytes = MemoryMarshal.AsBytes(geometry.Vertices.AsSpan());
        ReadOnlySpan<byte> indexBytes = MemoryMarshal.AsBytes(geometry.Indices.AsSpan());
        ISilkGraphicsBuffer? vertexBuffer = null;
        ISilkGraphicsBuffer? indexBuffer = null;
        ISilkGraphicsBuffer? uniformBuffer = null;
        try
        {
            vertexBuffer = _device.CreateBuffer(
                GetAllocationSize(vertexBytes.Length),
                SilkBufferUsage.Vertex | SilkBufferUsage.Upload);
            if (!vertexBytes.IsEmpty)
            {
                vertexBuffer.Write(vertexBytes);
                _vertexUploads++;
            }
            indexBuffer = _device.CreateBuffer(
                GetAllocationSize(indexBytes.Length),
                SilkBufferUsage.Index | SilkBufferUsage.Upload);
            if (!indexBytes.IsEmpty)
            {
                indexBuffer.Write(indexBytes);
                _indexUploads++;
            }
            uniformBuffer = _device.CreateBuffer(
                SilkSceneUniformWriter.ByteSize,
                SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
            _geometryBuilds++;
            return new SilkMeshGpuResource(
                mesh,
                geometry.IndexCount,
                vertexBuffer,
                indexBuffer,
                uniformBuffer);
        }
        catch
        {
            uniformBuffer?.Dispose();
            indexBuffer?.Dispose();
            vertexBuffer?.Dispose();
            throw;
        }
    }

    private static nuint GetAllocationSize(int dataLength) =>
        checked((nuint)Math.Max(dataLength, sizeof(uint)));
}

/// <summary>
/// GPU buffers for one retained mesh.
/// </summary>
public sealed class SilkMeshGpuResource : IDisposable
{
    private readonly byte[] _uniformBytes = new byte[SilkSceneUniformWriter.ByteSize];
    private SilkMeshData? _uniformMesh;
    private ulong _uniformFrameRevision = ulong.MaxValue;
    private bool _disposed;

    internal SilkMeshGpuResource(
        SilkMeshData mesh,
        uint indexCount,
        ISilkGraphicsBuffer vertexBuffer,
        ISilkGraphicsBuffer indexBuffer,
        ISilkGraphicsBuffer uniformBuffer)
    {
        Mesh = mesh;
        IndexCount = indexCount;
        VertexBuffer = vertexBuffer;
        IndexBuffer = indexBuffer;
        UniformBuffer = uniformBuffer;
        SilkManagedDiagnostics.GpuMeshCreated();
    }

    /// <summary>Gets the retained CPU mesh metadata.</summary>
    public SilkMeshData Mesh { get; private set; }

    /// <summary>Gets interleaved float3 position and float3 normal data.</summary>
    public ISilkGraphicsBuffer VertexBuffer { get; }

    /// <summary>Gets packed 16-bit triangle index data.</summary>
    public ISilkGraphicsBuffer IndexBuffer { get; }

    /// <summary>Gets the reusable 80-byte SceneParameters buffer.</summary>
    public ISilkGraphicsBuffer UniformBuffer { get; }

    /// <summary>Gets the indexed triangle-list element count.</summary>
    public uint IndexCount { get; }

    internal bool HasSameGeometry(SilkMeshData mesh) =>
        Mesh.Points.Span.SequenceEqual(mesh.Points.Span) &&
        Mesh.Indices.Span.SequenceEqual(mesh.Indices.Span);

    internal void UpdateMesh(SilkMeshData mesh)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Mesh = mesh;
    }

    internal bool UpdateUniform(SilkFrameState frame, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ReferenceEquals(_uniformMesh, Mesh) &&
            _uniformFrameRevision == frame.Revision)
        {
            return false;
        }

        SilkSceneUniformWriter.Write(Mesh, frame, destination);
        _uniformMesh = Mesh;
        _uniformFrameRevision = frame.Revision;
        if (destination.SequenceEqual(_uniformBytes))
        {
            return false;
        }
        UniformBuffer.Write(destination);
        destination.CopyTo(_uniformBytes);
        return true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        UniformBuffer.Dispose();
        IndexBuffer.Dispose();
        VertexBuffer.Dispose();
        _disposed = true;
        SilkManagedDiagnostics.GpuMeshDestroyed();
    }
}

/// <summary>Cumulative retained-scene GPU upload diagnostics.</summary>
public readonly record struct SilkSceneGpuStatistics(
    int MeshCount,
    ulong GeometryBuilds,
    ulong VertexUploads,
    ulong IndexUploads,
    ulong UniformUploads);
