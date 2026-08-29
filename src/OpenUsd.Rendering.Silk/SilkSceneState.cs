// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Retains the latest scene resources produced by hdSilk command pages.
/// </summary>
public sealed class SilkSceneState
{
    private readonly Dictionary<ulong, SilkMeshData> _meshes = [];
    private readonly Dictionary<(string Path, int InstanceIndex), SilkMeshData> _meshesByPath =
        [];
    private readonly Dictionary<ulong, string> _pathsByHash = [];
    private readonly Dictionary<string, List<SilkMeshData>> _instancesByPath =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, SilkMaterialData> _materials =
        new(StringComparer.Ordinal);

    // The authored mesh of every mesh whose points are currently simulated. A deformation
    // destructively replaces the retained points, so without this the authored geometry exists
    // nowhere once the first replacement is published: stopping the simulation would leave the last
    // simulated pose on screen until USD happened to dirty the prim. One entry per driven mesh, and
    // it is dropped the moment the mesh is restored, re-authored by a page, or removed, so the
    // table can never outgrow the driven set.
    private readonly Dictionary<ulong, SilkMeshData> _authoredMeshes = [];
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

    /// <summary>
    /// Gets retained meshes keyed by USD prim path and instance ordinal. A
    /// point-instanced prototype contributes one entry per instance under the
    /// same authoritative path.
    /// </summary>
    public IReadOnlyDictionary<(string Path, int InstanceIndex), SilkMeshData> MeshesByPath =>
        _meshesByPath;

    /// <summary>Gets retained future-GPU token ranges and resolved identities.</summary>
    public SilkPickIdentityTable PickIdentities { get; }

    /// <summary>
    /// Gets retained materials keyed by USD material path, which is what a mesh's
    /// <see cref="SilkMeshData.MaterialPath"/> references.
    /// </summary>
    public IReadOnlyDictionary<string, SilkMaterialData> Materials => _materials;

    /// <summary>Applies one dirty page and returns resource-change counts.</summary>
    public SilkSceneDelta Apply(OpenUsdSilkPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return Apply(page.GetEnumerator(), page.Revision);
    }

    /// <summary>
    /// Replaces the points of one retained mesh with externally simulated ones.
    /// </summary>
    /// <param name="path">The absolute authored prim path of the retained mesh.</param>
    /// <param name="instanceIndex">The zero-based instance ordinal.</param>
    /// <param name="points">The simulated points, three components per vertex.</param>
    /// <param name="meshId">
    /// Receives the retained mesh key whenever a retained mesh was found, whether or not its points
    /// changed, so a settled body stays addressable.
    /// </param>
    /// <returns>What the replacement did, distinguishing settled from refused.</returns>
    /// <remarks>
    /// <para>
    /// Only the point positions are replaced. The topology, attributes, material, and transform are
    /// the authored ones, because a deforming body moves its vertices without changing what it is.
    /// A point count that does not match the retained mesh is refused rather than resized: that is
    /// a different mesh, and drawing a solver's vertices against another mesh's indices would
    /// produce geometry neither side described.
    /// </para>
    /// <para>
    /// Authored per-vertex normals are dropped rather than carried over, because they describe the
    /// rest pose: keeping them shades a bent cloth as if it were still flat. An empty normal set is
    /// what makes the geometry builder recompute normals from the deformed topology, which is the
    /// same path an unauthored mesh has always taken. The authored mesh, normals included, is
    /// retained here and returned by <see cref="RestoreAuthoredPoints"/>.
    /// </para>
    /// <para>
    /// The retained mesh is immutable, so a replacement is a new instance published in its place.
    /// Callers must invalidate the retained GPU geometry of the returned mesh, because vertex
    /// buffers are rebuilt from a scene delta and this call emits none on its own.
    /// </para>
    /// </remarks>
    public SilkDeformationResult ReplacePoints(
        string path,
        int instanceIndex,
        ReadOnlySpan<float> points,
        out ulong meshId)
    {
        ArgumentNullException.ThrowIfNull(path);
        meshId = 0;
        if (!_meshesByPath.TryGetValue((path, instanceIndex), out SilkMeshData? mesh))
        {
            return SilkDeformationResult.MeshMissing;
        }

        meshId = mesh.Id;
        ReadOnlySpan<float> retained = mesh.GetPointSpan();
        if (points.Length != retained.Length)
        {
            return SilkDeformationResult.VertexCountMismatch;
        }

        for (int index = 0; index < points.Length; index++)
        {
            if (!float.IsFinite(points[index]))
            {
                return SilkDeformationResult.NonFiniteValue;
            }
        }

        // A settled body republishes the points it already carries. That is a
        // success with no work to do, and it must not be mistaken for a refusal
        // or the body would drop out of the driven set the moment it stops
        // moving.
        if (points.SequenceEqual(retained))
        {
            return SilkDeformationResult.Unchanged;
        }

        SilkMeshData replacement = mesh.WithSimulatedPoints(points);

        // The authored mesh is stashed on the first replacement only. While a mesh stays driven the
        // retained entry is the simulated one, so re-stashing it here would overwrite the rest pose
        // with a simulated one and make restoration a no-op that draws the last frame forever. A
        // page that re-authors the mesh drops the stash instead, so the next replacement captures
        // the newly authored geometry.
        _ = _authoredMeshes.TryAdd(mesh.Id, mesh);
        _meshes[replacement.Id] = replacement;
        _meshesByPath[(path, instanceIndex)] = replacement;
        if (_instancesByPath.TryGetValue(path, out List<SilkMeshData>? instances))
        {
            for (int index = 0; index < instances.Count; index++)
            {
                if (instances[index].InstanceIndex == instanceIndex)
                {
                    instances[index] = replacement;
                    break;
                }
            }
        }

        meshId = replacement.Id;
        return SilkDeformationResult.Applied;
    }

    /// <summary>Puts the authored geometry of one simulated mesh back into the retained scene.</summary>
    /// <param name="meshId">The retained mesh key a deformation was applied to.</param>
    /// <returns>
    /// <see langword="true"/> when a mesh was restored, so its retained GPU geometry must be
    /// invalidated by the caller; <see langword="false"/> when the mesh was not simulated, was
    /// already re-authored by a page, or has been removed from the scene.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the other half of <see cref="ReplacePoints"/>, and stopping a simulation is not
    /// complete without it. The replacement is destructive - the authored points are not retained
    /// anywhere else in the scene - so dropping a deformation restores nothing on its own and the
    /// last simulated pose stays on screen until USD happens to republish the prim, which for a
    /// static stage may be never.
    /// </para>
    /// <para>
    /// Like a replacement, this emits no delta of its own. The caller collects the restored mesh
    /// keys and invalidates exactly those, so restoring costs one upload per mesh that was
    /// simulated and nothing for the rest of the scene.
    /// </para>
    /// </remarks>
    public bool RestoreAuthoredPoints(ulong meshId)
    {
        if (!_authoredMeshes.Remove(meshId, out SilkMeshData? authored))
        {
            return false;
        }

        // A mesh the scene no longer retains has nothing to restore. Its stash is still dropped
        // above, because the mesh it described is gone.
        if (!_meshes.ContainsKey(meshId))
        {
            return false;
        }

        _meshes[authored.Id] = authored;
        _meshesByPath[(authored.Path, authored.InstanceIndex)] = authored;
        if (_instancesByPath.TryGetValue(authored.Path, out List<SilkMeshData>? instances))
        {
            for (int index = 0; index < instances.Count; index++)
            {
                if (instances[index].InstanceIndex == authored.InstanceIndex)
                {
                    instances[index] = authored;
                    break;
                }
            }
        }

        return true;
    }

    /// <summary>Reports whether the retained points of one mesh are currently simulated.</summary>
    /// <param name="meshId">The retained mesh key.</param>
    /// <returns><see langword="true"/> when authored geometry is retained for restoration.</returns>
    public bool HasAuthoredGeometry(ulong meshId) => _authoredMeshes.ContainsKey(meshId);

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
        List<string>? materialChanges = null;
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
                        SilkMeshData mesh = CopyMeshFrom(commands.Current.AsMeshUpsert());
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
                    case SilkCommandType.MaterialUpsert:
                        SilkMaterialData material = SilkMaterialData.CopyFrom(
                            commands.Current.AsMaterialUpsert());
                        VerifyStableHash(material.Path, material.StableHash);
                        _materials[material.Path] = material;
                        (materialChanges ??= []).Add(material.Path);
                        break;
                    case SilkCommandType.MaterialRemove:
                        SilkMaterialRemoveCommand materialRemoval =
                            commands.Current.AsMaterialRemove();
                        VerifyStableHash(
                            materialRemoval.Path,
                            materialRemoval.StableHash);
                        _ = _materials.Remove(materialRemoval.Path);
                        (materialChanges ??= []).Add(materialRemoval.Path);
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
            removals?.ToArray() ?? [],
            materialChanges?.ToArray() ?? []);
    }

    private SilkMeshData CopyMeshFrom(SilkMeshUpsertCommand command)
    {
        if (!command.IsInstanceReference)
        {
            return SilkMeshData.CopyFrom(command);
        }

        if (!_meshesByPath.TryGetValue((command.Path, 0), out SilkMeshData? prototype))
        {
            throw new InvalidDataException(
                $"hdSilk instance '{command.Path}' index {command.InstanceIndex} " +
                "arrived before its prototype geometry.");
        }
        return SilkMeshData.CopyInstanceFrom(command, prototype);
    }

    /// <summary>
    /// Requires the wire hash to match the path it indexes. The hash is an index
    /// only, so a mismatch means the page is inconsistent rather than merely
    /// colliding, and must fail loudly here.
    /// </summary>
    private void VerifyStableHash(string path, ulong stableHash)
    {
        ulong expected = _pathHasher is null
            ? SilkWireFormat.ComputeStableHash(path)
            : _pathHasher(path);
        if (stableHash != expected)
        {
            throw new InvalidDataException(
                $"The hdSilk material hash for '{path}' does not match its path.");
        }
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
                $"hdSilk prim ID {mesh.PrimId} instance {mesh.InstanceIndex} is " +
                $"shared by '{primMesh.Path}' and '{mesh.Path}'.");
        }

        ulong? replacedId = null;
        (string Path, int InstanceIndex) pathKey = (mesh.Path, mesh.InstanceIndex);
        if (_meshesByPath.TryGetValue(pathKey, out SilkMeshData? pathMesh))
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
            _ = _authoredMeshes.Remove(oldId);
        }

        // A page re-authors the geometry, so whatever rest pose was stashed for this mesh describes
        // a version the stage has replaced. Dropping it here is what lets a mesh that is still
        // driven capture the new authored geometry on its next replacement, and what makes a later
        // restore return the newest authored points rather than a stale copy.
        _ = _authoredMeshes.Remove(mesh.Id);
        _meshes[mesh.Id] = mesh;
        _meshesByPath[pathKey] = mesh;
        _pathsByHash[mesh.StableHash] = mesh.Path;

        if (!_instancesByPath.TryGetValue(mesh.Path, out List<SilkMeshData>? instances))
        {
            instances = [];
            _instancesByPath[mesh.Path] = instances;
        }
        int existing = instances.FindIndex(
            candidate => candidate.InstanceIndex == mesh.InstanceIndex);
        if (existing >= 0)
        {
            instances[existing] = mesh;
        }
        else
        {
            instances.Add(mesh);
        }
        return replacedId;
    }

    /// <summary>
    /// Gets every retained instance of one authoritative prim path. A prim with
    /// no instancer yields a single entry.
    /// </summary>
    internal IReadOnlyList<SilkMeshData> GetInstances(string path) =>
        _instancesByPath.TryGetValue(path, out List<SilkMeshData>? instances)
            ? instances
            : [];

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
        (string Path, int InstanceIndex) pathKey =
            (removal.Path, removal.InstanceIndex);
        if (!_meshesByPath.TryGetValue(pathKey, out SilkMeshData? mesh))
        {
            return false;
        }
        if (mesh.StableHash != removal.StableHash)
        {
            throw new InvalidDataException(
                $"hdSilk removal for '{removal.Path}' has a different stable hash.");
        }

        removedId = mesh.Id;
        _meshesByPath.Remove(pathKey);
        _meshes.Remove(mesh.Id);

        // A removed mesh can never be restored, so its stashed rest pose retires with it rather
        // than keeping a copy of every mesh the simulation ever drove.
        _ = _authoredMeshes.Remove(mesh.Id);

        // Pick identity is per instance, so it retires with this record. The path hash index
        // is shared by every instance of a prototype, so it survives until the last one goes.
        PickIdentities.Remove(mesh.Path, mesh.InstanceIndex);
        if (_instancesByPath.TryGetValue(removal.Path, out List<SilkMeshData>? instances))
        {
            int instanceIndex = removal.InstanceIndex;
            instances.RemoveAll(
                candidate => candidate.InstanceIndex == instanceIndex);
            if (instances.Count == 0)
            {
                _instancesByPath.Remove(removal.Path);
                _pathsByHash.Remove(mesh.StableHash);
            }
        }
        return true;
    }
}

/// <summary>
/// Retained camera and viewport state.
/// </summary>
public sealed class SilkFrameState
{
    internal const int MaximumLights = 8;
    private readonly double[] _view = new double[16];
    private readonly double[] _projection = new double[16];
    private readonly double[] _clipPlanes = new double[32];
    private readonly SilkFrameLight[] _lights = new SilkFrameLight[MaximumLights];
    private Vector4 _ambientLight;

    /// <summary>Initializes an identity camera and viewport state.</summary>
    public SilkFrameState()
    {
        for (int i = 0; i < 16; i += 5)
        {
            _view[i] = 1;
            _projection[i] = 1;
        }
    }

    /// <summary>Gets the viewport width.</summary>
    public int Width { get; private set; }

    /// <summary>Gets the viewport height.</summary>
    public int Height { get; private set; }

    /// <summary>Gets the row-major world-to-view matrix.</summary>
    public ReadOnlyMemory<double> View => _view;

    /// <summary>Gets the row-major projection matrix.</summary>
    public ReadOnlyMemory<double> Projection => _projection;

    /// <summary>Gets the number of eye-space clip planes.</summary>
    internal uint ClipPlaneCount { get; private set; }

    /// <summary>Gets the eye-space clip plane table.</summary>
    internal ReadOnlyMemory<double> ClipPlanes => _clipPlanes;

    internal ReadOnlySpan<SilkFrameLight> Lights => _lights;

    internal Vector4 AmbientLight => _ambientLight;

    internal uint LightCount { get; private set; }

    /// <summary>Gets the revision of the retained camera or viewport state.</summary>
    public ulong Revision { get; private set; }

    internal void Update(SilkFrameCommand command)
    {
        bool changed = Width != command.Width ||
            Height != command.Height ||
            ClipPlaneCount != command.ClipPlaneCount ||
            LightCount != command.LightCount;
        Width = command.Width;
        Height = command.Height;
        ClipPlaneCount = command.ClipPlaneCount;
        LightCount = command.LightCount;
        for (int i = 0; i < 16; i++)
        {
            double view = command.GetViewElement(i);
            double projection = command.GetProjectionElement(i);
            changed |= _view[i] != view || _projection[i] != projection;
            _view[i] = view;
            _projection[i] = projection;
        }
        for (int i = 0; i < _clipPlanes.Length; i++)
        {
            double clipPlane = command.GetClipPlaneElement(i / 4, i % 4);
            changed |= _clipPlanes[i] != clipPlane;
            _clipPlanes[i] = clipPlane;
        }
        for (int lightIndex = 0; lightIndex < MaximumLights; lightIndex++)
        {
            SilkFrameLight light = SilkFrameLight.CopyFrom(command, lightIndex);
            changed |= !_lights[lightIndex].Equals(light);
            _lights[lightIndex] = light;
        }
        var ambient = new Vector4(
            command.GetAmbientColor(0),
            command.GetAmbientColor(1),
            command.GetAmbientColor(2),
            command.AmbientIntensity);
        changed |= _ambientLight != ambient;
        _ambientLight = ambient;
        if (changed)
        {
            Revision++;
        }
    }
}

internal readonly record struct SilkFrameLight(
    uint Type,
    uint ShadowEnabled,
    float ShapeX,
    float ShapeY,
    Vector3 Color,
    float Intensity,
    Matrix4x4 Transform,
    float Exposure,
    float Diffuse,
    float Specular,
    float Radius)
{
    internal static SilkFrameLight CopyFrom(SilkFrameCommand command, int light) =>
        new(
            command.GetLightType(light),
            command.GetLightShadowEnabled(light),
            command.GetLightShapeX(light),
            command.GetLightShapeY(light),
            new Vector3(
                command.GetLightColor(light, 0),
                command.GetLightColor(light, 1),
                command.GetLightColor(light, 2)),
            command.GetLightIntensity(light),
            ReadTransform(command, light),
            command.GetLightExposure(light),
            command.GetLightDiffuse(light),
            command.GetLightSpecular(light),
            command.GetLightRadius(light));

    private static Matrix4x4 ReadTransform(SilkFrameCommand command, int light) =>
        new(
            ToSingle(command.GetLightTransformElement(light, 0)),
            ToSingle(command.GetLightTransformElement(light, 1)),
            ToSingle(command.GetLightTransformElement(light, 2)),
            ToSingle(command.GetLightTransformElement(light, 3)),
            ToSingle(command.GetLightTransformElement(light, 4)),
            ToSingle(command.GetLightTransformElement(light, 5)),
            ToSingle(command.GetLightTransformElement(light, 6)),
            ToSingle(command.GetLightTransformElement(light, 7)),
            ToSingle(command.GetLightTransformElement(light, 8)),
            ToSingle(command.GetLightTransformElement(light, 9)),
            ToSingle(command.GetLightTransformElement(light, 10)),
            ToSingle(command.GetLightTransformElement(light, 11)),
            ToSingle(command.GetLightTransformElement(light, 12)),
            ToSingle(command.GetLightTransformElement(light, 13)),
            ToSingle(command.GetLightTransformElement(light, 14)),
            ToSingle(command.GetLightTransformElement(light, 15)));

    private static float ToSingle(double value) =>
        double.IsFinite(value) ? (float)value : 0;
}

internal static class SilkFrameUniformWriter
{
    internal const int ByteSize = 1056;

    internal static void Write(
        SilkFrameState frame,
        Span<byte> destination,
        bool flipClipSpaceY,
        RenderOutputTransform outputTransform,
        float exposure)
    {
        if (destination.Length != ByteSize)
        {
            throw new ArgumentException(
                $"Frame constants must be exactly {ByteSize} bytes.",
                nameof(destination));
        }

        Span<double> projection = stackalloc double[16];
        ConvertOpenGlDepthToZeroToOne(frame.Projection.Span, projection);
        if (flipClipSpaceY)
        {
            MirrorClipSpaceY(projection);
        }

        Matrix4x4 projected = ToMatrix4x4(projection);
        if (!Matrix4x4.Invert(projected, out Matrix4x4 clipToEye))
        {
            throw new InvalidDataException("The frame projection matrix is not invertible.");
        }

        WriteMatrixTranspose(destination, 0, clipToEye);
        WriteSingle(destination, 64, frame.ClipPlaneCount);
        WriteSingle(destination, 68, (uint)outputTransform);
        WriteSingle(destination, 72, exposure);
        WriteSingle(destination, 76, 0u);

        ReadOnlySpan<double> planes = frame.ClipPlanes.Span;
        for (int i = 0; i < 32; i++)
        {
            float value = ToFiniteSingle(planes[i], $"clipPlanes[{i / 4},{i % 4}]");
            WriteSingle(destination, 80 + (i * sizeof(float)), value);
        }

        Matrix4x4 worldToEye = ToMatrix4x4(frame.View.Span);
        if (!Matrix4x4.Invert(worldToEye, out Matrix4x4 eyeToWorld))
        {
            throw new InvalidDataException("The frame view matrix is not invertible.");
        }

        Vector4 ambient = frame.AmbientLight;
        WriteVector4(
            destination,
            208,
            ambient.X,
            ambient.Y,
            ambient.Z,
            (float)Math.Min(frame.LightCount, (uint)SilkFrameState.MaximumLights));
        ReadOnlySpan<SilkFrameLight> lights = frame.Lights;
        for (int i = 0; i < SilkFrameState.MaximumLights; i++)
        {
            WriteLight(destination, i, lights[i]);
        }
        WriteMatrixTranspose(destination, 992, eyeToWorld);
    }

    private static void WriteLight(
        Span<byte> destination,
        int index,
        SilkFrameLight light)
    {
        int positionOffset = 224 + (index * 16);
        int directionOffset = 352 + (index * 16);
        int colorOffset = 480 + (index * 16);
        int controlOffset = 608 + (index * 16);
        int tangentOffset = 736 + (index * 16);
        int bitangentOffset = 864 + (index * 16);
        if (light.Type == 0)
        {
            WriteVector4(destination, positionOffset, 0, 0, 0, 0);
            WriteVector4(destination, directionOffset, 0, 0, 1, 0);
            WriteVector4(destination, colorOffset, 0, 0, 0, 0);
            WriteVector4(destination, controlOffset, 0, 0, 0, 0);
            WriteVector4(destination, tangentOffset, 1, 0, 0, 0);
            WriteVector4(destination, bitangentOffset, 0, 1, 0, 0);
            return;
        }

        Vector3 position = new(light.Transform.M41, light.Transform.M42, light.Transform.M43);
        Vector3 direction = Vector3.TransformNormal(Vector3.UnitZ, light.Transform);
        direction = Normalize(direction, Vector3.UnitZ);
        Vector3 tangent = Vector3.TransformNormal(Vector3.UnitX, light.Transform);
        tangent = Normalize(tangent, Vector3.UnitX);
        Vector3 bitangent = Vector3.TransformNormal(Vector3.UnitY, light.Transform);
        bitangent = Normalize(bitangent, Vector3.UnitY);
        float exposed = light.Intensity * MathF.Pow(2.0f, light.Exposure);
        WriteVector4(destination, positionOffset, position.X, position.Y, position.Z, light.Type);
        WriteVector4(destination, directionOffset, direction.X, direction.Y, direction.Z, light.Radius);
        WriteVector4(
            destination,
            colorOffset,
            light.Color.X,
            light.Color.Y,
            light.Color.Z,
            exposed);
        WriteVector4(
            destination,
            controlOffset,
            light.Diffuse,
            light.Specular,
            light.ShadowEnabled,
            0);
        WriteVector4(destination, tangentOffset, tangent.X, tangent.Y, tangent.Z, light.ShapeX);
        WriteVector4(destination, bitangentOffset, bitangent.X, bitangent.Y, bitangent.Z, light.ShapeY);
    }

    private static Vector3 Normalize(Vector3 value, Vector3 fallback)
    {
        float lengthSquared = value.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared <= float.Epsilon)
        {
            return fallback;
        }
        return value / MathF.Sqrt(lengthSquared);
    }

    private static Matrix4x4 ToMatrix4x4(ReadOnlySpan<double> values) =>
        new(
            ToFiniteSingle(values[0], "projection[0,0]"),
            ToFiniteSingle(values[1], "projection[0,1]"),
            ToFiniteSingle(values[2], "projection[0,2]"),
            ToFiniteSingle(values[3], "projection[0,3]"),
            ToFiniteSingle(values[4], "projection[1,0]"),
            ToFiniteSingle(values[5], "projection[1,1]"),
            ToFiniteSingle(values[6], "projection[1,2]"),
            ToFiniteSingle(values[7], "projection[1,3]"),
            ToFiniteSingle(values[8], "projection[2,0]"),
            ToFiniteSingle(values[9], "projection[2,1]"),
            ToFiniteSingle(values[10], "projection[2,2]"),
            ToFiniteSingle(values[11], "projection[2,3]"),
            ToFiniteSingle(values[12], "projection[3,0]"),
            ToFiniteSingle(values[13], "projection[3,1]"),
            ToFiniteSingle(values[14], "projection[3,2]"),
            ToFiniteSingle(values[15], "projection[3,3]"));

    private static void ConvertOpenGlDepthToZeroToOne(
        ReadOnlySpan<double> source,
        Span<double> destination)
    {
        for (int row = 0; row < 4; row++)
        {
            int offset = row * 4;
            destination[offset] = source[offset];
            destination[offset + 1] = source[offset + 1];
            destination[offset + 2] = (source[offset + 2] + source[offset + 3]) * 0.5;
            destination[offset + 3] = source[offset + 3];
        }
    }

    private static void MirrorClipSpaceY(Span<double> projection)
    {
        for (int row = 0; row < 4; row++)
        {
            projection[(row * 4) + 1] = -projection[(row * 4) + 1];
        }
    }

    private static void WriteMatrixTranspose(
        Span<byte> destination,
        int offset,
        Matrix4x4 matrix)
    {
        Span<float> values =
        [
            matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.M41, matrix.M42, matrix.M43, matrix.M44
        ];
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                WriteSingle(
                    destination,
                    offset + (((row * 4) + column) * sizeof(float)),
                    values[(column * 4) + row]);
            }
        }
    }

    private static float ToFiniteSingle(double value, string name)
    {
        if (!double.IsFinite(value) || value > float.MaxValue || value < -float.MaxValue)
        {
            throw new InvalidDataException($"The frame {name} value is invalid.");
        }
        return (float)value;
    }

    private static void WriteSingle(Span<byte> destination, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(
            destination.Slice(offset, sizeof(float)),
            BitConverter.SingleToInt32Bits(value));

    private static void WriteVector4(
        Span<byte> destination,
        int offset,
        float x,
        float y,
        float z,
        float w)
    {
        WriteSingle(destination, offset, x);
        WriteSingle(destination, offset + 4, y);
        WriteSingle(destination, offset + 8, z);
        WriteSingle(destination, offset + 12, w);
    }

    private static void WriteSingle(Span<byte> destination, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(
            destination.Slice(offset, sizeof(uint)),
            value);
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
    private readonly float[] _authoredNormals = [];
    private readonly SilkVertexAttributeData[] _attributes = [];

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
        double[] transform,
        bool doubleSided = true,
        SilkMeshCullStyle cullStyle = SilkMeshCullStyle.BackUnlessDoubleSided)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(triangleSubprims);
        ArgumentNullException.ThrowIfNull(displayColor);
        ArgumentNullException.ThrowIfNull(transform);
        if (!Enum.IsDefined(cullStyle))
        {
            throw new ArgumentOutOfRangeException(nameof(cullStyle));
        }
        PrimId = primId;
        Path = path;
        StableHash = stableHash;
        InstanceId = instanceId;
        InstanceIndex = instanceIndex;
        TopologyKind = topologyKind;
        TopologyRevision = topologyRevision;
        DoubleSided = doubleSided;
        CullStyle = cullStyle;
        _points = (float[])points.Clone();
        _indices = (uint[])indices.Clone();
        _triangleSubprims = (int[])triangleSubprims.Clone();
        _displayColor = (float[])displayColor.Clone();
        _transform = (double[])transform.Clone();
        TopologyFingerprint = SilkTopologyFingerprint.Compute(
            TopologyKind,
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
        bool doubleSided,
        SilkMeshCullStyle cullStyle,
        ulong topologyFingerprint)
    {
        PrimId = primId;
        Path = path;
        StableHash = stableHash;
        InstanceId = instanceId;
        InstanceIndex = instanceIndex;
        TopologyKind = topologyKind;
        TopologyRevision = topologyRevision;
        DoubleSided = doubleSided;
        CullStyle = cullStyle;
        _points = points;
        _indices = indices;
        _triangleSubprims = triangleSubprims;
        _displayColor = displayColor;
        _transform = transform;
        TopologyFingerprint = topologyFingerprint;
    }

    internal SilkMeshData(
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
        ulong topologyFingerprint,
        bool doubleSided,
        SilkMeshCullStyle cullStyle,
        float[] authoredNormals,
        string materialPath)
        : this(
            primId,
            path,
            stableHash,
            instanceId,
            instanceIndex,
            topologyKind,
            topologyRevision,
            points,
            indices,
            triangleSubprims,
            displayColor,
            transform,
            doubleSided,
            cullStyle,
            topologyFingerprint)
    {
        _authoredNormals = authoredNormals;
        MaterialPath = materialPath;
    }

    internal SilkMeshData(
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
        ulong topologyFingerprint,
        bool doubleSided,
        SilkMeshCullStyle cullStyle,
        float[] authoredNormals,
        string materialPath,
        SilkVertexAttributeData[] attributes)
        : this(
            primId,
            path,
            stableHash,
            instanceId,
            instanceIndex,
            topologyKind,
            topologyRevision,
            points,
            indices,
            triangleSubprims,
            displayColor,
            transform,
            topologyFingerprint,
            doubleSided,
            cullStyle,
            authoredNormals,
            materialPath)
    {
        _attributes = attributes;
    }

    /// <summary>
    /// Gets every authored vertex attribute the delegate could resolve onto the
    /// emitted vertices, in a stable order.
    /// </summary>
    /// <remarks>
    /// Includes normals, which are also exposed pre-expanded through
    /// <see cref="AuthoredNormals"/> for the vertex builder's hot path. An
    /// attribute absent here was either not authored or authored with an
    /// interpolation the delegate could not resolve; in neither case may a
    /// consumer invent one.
    /// </remarks>
    public IReadOnlyList<SilkVertexAttributeData> Attributes => _attributes;

    /// <summary>
    /// Finds a texture coordinate set by authored primvar name, which is how a
    /// <c>UsdUVTexture</c> reader selects among several sets. Returns null when
    /// the mesh carries no such set.
    /// </summary>
    public SilkVertexAttributeData? FindTexCoord(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (SilkVertexAttributeData attribute in _attributes)
        {
            if (attribute.Semantic == SilkAttributeSemantic.TexCoord &&
                string.Equals(attribute.Name, name, StringComparison.Ordinal))
            {
                return attribute;
            }
        }
        return null;
    }

    /// <summary>
    /// Gets the authored per-vertex normals, empty when the mesh authored none
    /// that this delegate could resolve onto emitted vertices. When empty the
    /// renderer computes normals from topology as it always has.
    /// </summary>
    public ReadOnlyMemory<float> AuthoredNormals => _authoredNormals;

    /// <summary>Gets the bound material path, empty when the mesh has none.</summary>
    public string MaterialPath { get; } = string.Empty;

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

    /// <summary>
    /// Gets the retained resource key. A prim with no instancer keeps Hydra's
    /// explicit prim ID so existing identities are unchanged. Point-instanced
    /// records past instance zero pack the prim ID and instance ordinal behind
    /// a high marker bit, because every instance of a prototype shares one prim
    /// ID and would otherwise collide.
    /// </summary>
    public ulong Id => InstanceIndex == 0
        ? checked((ulong)PrimId)
        : (1UL << 63) |
            ((ulong)checked((uint)PrimId) << 32) |
            checked((uint)InstanceIndex);

    /// <summary>Gets the authoritative USD prim path.</summary>
    public string Path { get; }

    /// <summary>Gets the collision-checked FNV-1a path hash index.</summary>
    public ulong StableHash { get; }

    /// <summary>
    /// Gets the stable, diagnostic-only identifier of the owning instancer, or
    /// zero when the prim is not instanced.
    /// </summary>
    public int InstanceId { get; }

    /// <summary>
    /// Gets the zero-based instance ordinal. A prim with no instancer always
    /// reports zero.
    /// </summary>
    public int InstanceIndex { get; }

    /// <summary>Gets the emitted topology kind.</summary>
    public SilkTopologyKind TopologyKind { get; }

    /// <summary>Gets the topology-only mesh revision.</summary>
    public ulong TopologyRevision { get; }

    /// <summary>Gets whether Hydra resolved the mesh as double-sided.</summary>
    public bool DoubleSided { get; }

    /// <summary>Gets Hydra's resolved cull style for this mesh.</summary>
    public SilkMeshCullStyle CullStyle { get; }

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

    /// <summary>Gets the retained point components without copying them.</summary>
    internal ReadOnlySpan<float> GetPointSpan() => _points;

    /// <summary>
    /// Produces the same retained mesh with externally simulated point positions.
    /// </summary>
    /// <param name="points">The simulated point components, three per vertex.</param>
    /// <returns>A new immutable mesh carrying the simulated points and no authored normals.</returns>
    /// <remarks>
    /// <para>
    /// The topology fingerprint is deliberately carried over rather than recomputed: it describes
    /// the element connectivity, which a deformation never changes. Recomputing it would be
    /// harmless but would hide that a deformed mesh is the same mesh, and the geometry cache
    /// already separates deformed geometry through its own point fingerprint.
    /// </para>
    /// <para>
    /// Authored normals are dropped, because they describe the rest pose. Carrying them over shades
    /// a bent cloth with the normals of the flat one it used to be, which is a lighting error no
    /// amount of correct vertex positions can undo. An empty set is exactly what makes
    /// <c>SilkMeshGeometryBuilder</c> recompute normals from the simulated topology, so a deformed
    /// mesh follows the same path a mesh that authored no normals has always taken. The authored
    /// normals are not lost: the scene retains the whole authored mesh for restoration.
    /// </para>
    /// </remarks>
    internal SilkMeshData WithSimulatedPoints(ReadOnlySpan<float> points) =>
        new(
            PrimId,
            Path,
            StableHash,
            InstanceId,
            InstanceIndex,
            TopologyKind,
            TopologyRevision,
            points.ToArray(),
            _indices,
            _triangleSubprims,
            _displayColor,
            _transform,
            TopologyFingerprint,
            DoubleSided,
            CullStyle,
            [],
            MaterialPath,
            _attributes);

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
        if (command.IsInstanceReference)
        {
            throw new InvalidDataException(
                "A lightweight mesh instance requires prototype geometry.");
        }

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
            command.TopologyKind,
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

        float[] authoredNormals = [];
        SilkVertexAttributeData[] attributes = command.AttributeCount == 0
            ? []
            : new SilkVertexAttributeData[command.AttributeCount];
        for (int index = 0; index < command.AttributeCount; index++)
        {
            SilkMeshAttributeEntry attribute = command.GetAttribute(index);
            float[] data = new float[attribute.ElementCount * attribute.ComponentCount];
            for (int element = 0; element < attribute.ElementCount; element++)
            {
                for (int component = 0; component < attribute.ComponentCount; component++)
                {
                    data[(element * attribute.ComponentCount) + component] =
                        attribute.GetComponent(element, component);
                }
            }
            attributes[index] = new SilkVertexAttributeData(
                attribute.Name,
                attribute.Semantic,
                attribute.Interpolation,
                attribute.ComponentCount,
                data);

            if (attribute.Semantic != SilkAttributeSemantic.Normal ||
                attribute.ComponentCount != 3 ||
                authoredNormals.Length != 0)
            {
                continue;
            }
            // A constant normal is expanded here so the vertex builder only ever
            // sees one shape, and so the GPU layout stays identical either way.
            authoredNormals = new float[command.PointCount * 3];
            bool constant = attribute.Interpolation == SilkAttributeInterpolation.Constant;
            for (int point = 0; point < command.PointCount; point++)
            {
                int element = constant ? 0 : point;
                for (int component = 0; component < 3; component++)
                {
                    authoredNormals[(point * 3) + component] =
                        attribute.GetComponent(element, component);
                }
            }
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
            fingerprint.Value,
            command.DoubleSided,
            command.CullStyle,
            authoredNormals,
            command.MaterialPath,
            attributes);
    }

    internal static SilkMeshData CopyInstanceFrom(
        SilkMeshUpsertCommand command,
        SilkMeshData prototype)
    {
        ArgumentNullException.ThrowIfNull(prototype);
        if (!command.IsInstanceReference)
        {
            throw new ArgumentException(
                "The mesh command is not a lightweight instance reference.",
                nameof(command));
        }
        if (!string.Equals(command.Path, prototype.Path, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "A lightweight mesh instance must reference its prototype path.");
        }
        if (command.TopologyKind != prototype.TopologyKind ||
            command.TopologyRevision != prototype.TopologyRevision)
        {
            throw new InvalidDataException(
                "A lightweight mesh instance does not match its prototype topology.");
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
            prototype.TopologyKind,
            prototype.TopologyRevision,
            prototype.Points.ToArray(),
            prototype.Indices.ToArray(),
            prototype.TriangleSubprims.ToArray(),
            color,
            transform,
            prototype.TopologyFingerprint,
            command.DoubleSided,
            command.CullStyle,
            prototype.AuthoredNormals.ToArray(),
            prototype.MaterialPath,
            [.. prototype.Attributes]);
    }
}

internal static class SilkTopologyFingerprint
{
    internal static ulong Compute(
        SilkTopologyKind topologyKind,
        int pointCount,
        ReadOnlySpan<uint> indices,
        ReadOnlySpan<int> triangleSubprims)
    {
        var builder = new SilkTopologyFingerprintBuilder(
            topologyKind,
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
        SilkTopologyKind topologyKind,
        int pointCount,
        int indexCount,
        int triangleCount)
    {
        if (!Enum.IsDefined(topologyKind))
        {
            throw new ArgumentOutOfRangeException(nameof(topologyKind));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(pointCount);
        ArgumentOutOfRangeException.ThrowIfNegative(indexCount);
        ArgumentOutOfRangeException.ThrowIfNegative(triangleCount);
        _value = OffsetBasis;
        AddUInt32(0x53494C4B);
        AddUInt32((uint)topologyKind);
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
    internal SilkSceneDelta(
        ReadOnlyMemory<ulong> upsertedMeshIds,
        ReadOnlyMemory<ulong> removedMeshIds,
        ReadOnlyMemory<string> changedMaterialPaths)
        : this(upsertedMeshIds, removedMeshIds)
    {
        ChangedMaterialPaths = changedMaterialPaths;
    }

    /// <summary>Gets the number of created or updated meshes.</summary>
    public int MeshUpserts => UpsertedMeshIds.Length;

    /// <summary>Gets the number of removed meshes.</summary>
    public int MeshRemovals => RemovedMeshIds.Length;

    internal ReadOnlyMemory<string> ChangedMaterialPaths { get; }

    /// <summary>Gets the number of changed material records.</summary>
    internal int MaterialChanges => ChangedMaterialPaths.Length;
}

/// <summary>
/// Owns backend buffers corresponding to retained hdSilk mesh resources.
/// </summary>
public sealed class SilkSceneGpuResources : IDisposable
{
    private const int DiagnosticCapacity = 128;
    private const int MaximumUdimAtlasCells = 256;

    private readonly ISilkGraphicsDevice _device;
    private readonly Func<string, bool, SilkDecodedImage> _imageDecoder;
    private readonly Func<string, IReadOnlyList<SilkUdimTile>> _udimResolver;
    private readonly Dictionary<ulong, SilkMeshGpuResource> _meshes = [];
    private readonly Dictionary<SilkMeshGpuGeometryKey, List<SilkMeshGpuGeometryResource>> _geometries =
        [];
    private readonly Dictionary<string, SurfaceBuffer> _surfaceBuffers =
        new(StringComparer.Ordinal);
    private readonly Dictionary<TextureCacheKey, TextureCacheEntry> _textures = [];
    private readonly Dictionary<TextureCacheKey, TextureCacheEntry> _failedTextures = [];
    private readonly Dictionary<string, TextureCacheEntry> _volumeTextures =
        new(StringComparer.Ordinal);
    private readonly Dictionary<SilkSamplerDescriptor, ISilkGraphicsSampler> _samplers = [];
    private readonly Dictionary<string, RenderDiagnostic> _diagnostics =
        new(StringComparer.Ordinal);
    private ISilkGraphicsBuffer? _defaultSurfaceBuffer;
    private ISilkGraphicsBuffer? _frameBuffer;
    private readonly byte[] _frameBytes = new byte[SilkFrameUniformWriter.ByteSize];
    private ulong _frameRevision = ulong.MaxValue;
    private RenderOutputTransform _frameOutputTransform = (RenderOutputTransform)(-1);
    private float _frameExposure = float.NaN;
    private bool _disposed;

    private readonly record struct SurfaceBuffer(
        ISilkGraphicsBuffer? Buffer,
        ulong MaterialHash);

    private sealed record TextureCacheEntry(
        ISilkGraphicsTexture Texture,
        byte[] Pixels,
        bool IsUdim = false,
        TextureDependency[]? Dependencies = null)
    {
        internal bool Uploaded { get; set; }
    }

    private readonly record struct VolumeTextureInfo(uint Width, uint Height, uint Depth);

    private readonly record struct TextureDependency(
        string Asset,
        long Length,
        DateTime LastWriteTimeUtc);

    private readonly record struct TextureCacheKey(
        string MaterialPath,
        string Asset,
        SilkColorSpace ColorSpace,
        SilkMaterialParameter Parameter);

    /// <summary>Initializes GPU resource retention for one backend device.</summary>
    public SilkSceneGpuResources(ISilkGraphicsDevice device)
        : this(device, SilkNativeImageDecoder.Decode, SilkNativeImageDecoder.ResolveUdimTiles)
    {
    }

    internal SilkSceneGpuResources(
        ISilkGraphicsDevice device,
        Func<string, bool, SilkDecodedImage> imageDecoder,
        Func<string, IReadOnlyList<SilkUdimTile>>? udimResolver = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(imageDecoder);
        _device = device;
        _imageDecoder = imageDecoder;
        _udimResolver = udimResolver ?? SilkNativeImageDecoder.ResolveUdimTiles;
        SilkManagedDiagnostics.GpuSceneCreated();
    }

    /// <summary>Gets uploaded mesh resources by explicit Hydra prim ID.</summary>
    public IReadOnlyDictionary<ulong, SilkMeshGpuResource> Meshes => _meshes;

    /// <summary>Gets a bounded snapshot of material and texture degradation diagnostics.</summary>
    public RenderDiagnosticsState Diagnostics =>
        _diagnostics.Count == 0
            ? RenderDiagnosticsState.Empty
            : new RenderDiagnosticsState(
                _diagnostics
                    .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                    .Select(static pair => pair.Value));

    /// <summary>
    /// Discards failed texture fallbacks so the next render retries assets that may have changed.
    /// </summary>
    public void RetryFailedTextures()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ClearFailedTextureCache();
        RemoveTextureDiagnostics();
    }

    internal Dictionary<ulong, SilkMeshGpuResource>.ValueCollection MeshValues =>
        _meshes.Values;

    /// <summary>Gets the number of distinct retained geometry payloads.</summary>
    /// <remarks>
    /// A retention diagnostic. Geometry is shared by fingerprint and released by reference count,
    /// so a scene whose points change every frame must return to the same count once the previous
    /// payload is released rather than growing one entry per frame.
    /// </remarks>
    internal int GeometryResourceCount
    {
        get
        {
            int count = 0;
            foreach (List<SilkMeshGpuGeometryResource> matches in _geometries.Values)
            {
                count += matches.Count;
            }

            return count;
        }
    }

    /// <summary>Gets the revision of retained mesh-resource membership or metadata.</summary>
    public ulong Revision { get; private set; }

    /// <summary>Gets cumulative upload and resource-churn diagnostics.</summary>
    public SilkSceneGpuStatistics Statistics => new(
        _meshes.Count,
        _geometryBuilds,
        _vertexUploads,
        _indexUploads,
        _uniformUploads,
        _bufferAllocationBytes,
        _bufferWriteBytes,
        _textureUploadBytes);

    private ulong _geometryBuilds;
    private ulong _vertexUploads;
    private ulong _indexUploads;
    private ulong _uniformUploads;
    private ulong _bufferAllocationBytes;
    private ulong _bufferWriteBytes;
    private ulong _textureUploadBytes;

    /// <summary>Applies only the mesh changes reported by a scene delta.</summary>
    public void Apply(SilkSceneState scene, SilkSceneDelta delta)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        bool changed = delta.MeshRemovals != 0 ||
            delta.MeshUpserts != 0 ||
            delta.MaterialChanges != 0;

        foreach (ulong id in delta.RemovedMeshIds.Span)
        {
            if (_meshes.Remove(id, out SilkMeshGpuResource? removed))
            {
                DisposeMesh(removed);
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

            SilkMeshGpuResource replacement = CreateMesh(scene, mesh);
            if (_meshes.Remove(id, out SilkMeshGpuResource? previous))
            {
                DisposeMesh(previous);
            }
            _meshes.Add(id, replacement);
        }
        foreach (string materialPath in delta.ChangedMaterialPaths.ToArray())
        {
            List<ulong>? affected = null;
            foreach (KeyValuePair<ulong, SilkMeshGpuResource> pair in _meshes)
            {
                if (string.Equals(pair.Value.Mesh.MaterialPath, materialPath, StringComparison.Ordinal))
                {
                    (affected ??= []).Add(pair.Key);
                }
            }
            if (affected is null)
            {
                continue;
            }
            foreach (ulong id in affected)
            {
                SilkMeshData mesh = scene.Meshes[id];
                SilkMeshGpuResource replacement = CreateMesh(scene, mesh);
                SilkMeshGpuResource previous = _meshes[id];
                _meshes[id] = replacement;
                DisposeMesh(previous);
            }
            if (_surfaceBuffers.Remove(materialPath, out SurfaceBuffer surface))
            {
                surface.Buffer?.Dispose();
            }
        }
        if (delta.MaterialChanges != 0)
        {
            ClearTextureCache();
            RemoveMaterialDiagnostics();
            RemoveTextureDiagnostics();
        }
        else if (delta.MeshUpserts != 0 || delta.MeshRemovals != 0)
        {
            RemoveMaterialResolutionDiagnostics();
            PruneInactiveTextureFailures(scene);
        }
        if (changed)
        {
            Revision++;
        }
    }

    /// <summary>Updates only changed per-mesh SceneParameters constants.</summary>
    public int UpdateUniforms(SilkFrameState frame) => UpdateUniforms(frame, overrides: null);

    /// <summary>
    /// Updates only changed per-mesh SceneParameters constants, replacing the authored transform of
    /// every mesh a physics transform override drives.
    /// </summary>
    /// <param name="frame">The frame state the constants are projected with.</param>
    /// <param name="overrides">
    /// The resolved physics transform overrides, or <see langword="null"/> to draw every mesh from
    /// its authored transform.
    /// </param>
    /// <returns>The number of uniform blocks uploaded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    public int UpdateUniforms(SilkFrameState frame, SilkPhysicsTransformOverrides? overrides)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);
        int uploads = 0;
        bool hasOverrides = overrides is not null && overrides.HasOverrides;
        Span<byte> constants = stackalloc byte[SilkSceneUniformWriter.ByteSize];
        foreach (KeyValuePair<ulong, SilkMeshGpuResource> pair in _meshes)
        {
            ReadOnlySpan<double> transform = hasOverrides
                ? overrides!.GetTransform(pair.Key)
                : default;
            if (pair.Value.UpdateUniform(frame, constants, _device.ClipSpaceYPointsDown, transform))
            {
                uploads++;
                _uniformUploads++;
                _bufferWriteBytes += SilkSceneUniformWriter.ByteSize;
            }
        }
        return uploads;
    }

    /// <summary>Returns the per-frame constants the mesh shader reads.</summary>
    internal ISilkGraphicsBuffer RequireFrameBuffer(
        SilkFrameState frame,
        RenderOutputTransform outputTransform,
        float exposure)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);
        bool created = _frameBuffer is null;
        _frameBuffer ??= CreateTrackedBuffer(
            SilkFrameUniformWriter.ByteSize,
            SilkBufferUsage.Storage | SilkBufferUsage.Upload);
        if (_frameRevision != frame.Revision ||
            _frameOutputTransform != outputTransform ||
            _frameExposure != exposure)
        {
            Span<byte> constants = stackalloc byte[SilkFrameUniformWriter.ByteSize];
            SilkFrameUniformWriter.Write(
                frame,
                constants,
                _device.ClipSpaceYPointsDown,
                outputTransform,
                exposure);
            if (created || !constants.SequenceEqual(_frameBytes))
            {
                WriteTracked(_frameBuffer, constants);
                constants.CopyTo(_frameBytes);
            }
            _frameRevision = frame.Revision;
            _frameOutputTransform = outputTransform;
            _frameExposure = exposure;
        }
        return _frameBuffer;
    }

    /// <summary>
    /// Returns the surface constants for one material path, creating and uploading
    /// the block on first use and reusing it afterwards.
    /// </summary>
    /// <remarks>
    /// Keyed by material rather than by mesh because the constants are a property
    /// of the material, and because a per-mesh block would allocate for every prim
    /// in a scene that shares one material. Meshes with no supported material share
    /// a single default block whose shaded flag is zero, so slot 7 is always bound:
    /// leaving it unbound renders correctly on D3D12 and Vulkan and produces
    /// nothing at all on Metal.
    /// </remarks>
    internal ISilkGraphicsBuffer RequireSurfaceBuffer(
        SilkSceneState scene,
        SilkMeshData mesh,
        RenderHeadlight light)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SilkMaterialData? material = null;
        string path = mesh.MaterialPath;
        if (!string.IsNullOrEmpty(path))
        {
            _ = scene.Materials.TryGetValue(path, out material);
        }

        if (material is not { IsSupported: true })
        {
            if (!string.IsNullOrEmpty(path))
            {
                if (material is null)
                {
                    AddDiagnostic(
                        SilkRenderDiagnosticCodes.MaterialUnresolved,
                        path,
                        RenderDiagnosticSeverity.Warning,
                        $"Mesh material '{path}' is not present in retained scene state; default shading was used.");
                }
                else
                {
                    AddDiagnostic(
                        SilkRenderDiagnosticCodes.MaterialUnsupported,
                        path,
                        RenderDiagnosticSeverity.Warning,
                        $"Material '{path}' uses unsupported surface kind " +
                        $"{material.SurfaceKind}; default shading was used.");
                }
            }
            return _defaultSurfaceBuffer ??= CreateSurfaceBuffer(null, light);
        }

        if (_surfaceBuffers.TryGetValue(material.Path, out SurfaceBuffer existing) &&
            existing.Buffer is { } retained)
        {
            if (existing.MaterialHash != material.StableHash)
            {
                // The material changed in place, so refresh the block rather than
                // allocating a second buffer for the same path.
                WriteSurface(retained, material, light);
                _surfaceBuffers[material.Path] =
                    new SurfaceBuffer(retained, material.StableHash);
            }

            return retained;
        }

        ISilkGraphicsBuffer created = CreateSurfaceBuffer(material, light);
        _surfaceBuffers[material.Path] = new SurfaceBuffer(created, material.StableHash);
        return created;
    }

    private ISilkGraphicsBuffer CreateSurfaceBuffer(
        SilkMaterialData? material,
        RenderHeadlight light)
    {
        ISilkGraphicsBuffer? buffer = null;
        try
        {
            buffer = CreateTrackedBuffer(
                SilkSurfaceUniformWriter.ByteSize,
                SilkBufferUsage.Storage | SilkBufferUsage.Upload);
            WriteSurface(buffer, material, light);
            return buffer;
        }
        catch
        {
            buffer?.Dispose();
            throw;
        }
    }

    private void WriteSurface(
        ISilkGraphicsBuffer buffer,
        SilkMaterialData? material,
        RenderHeadlight light)
    {
        Span<byte> constants = stackalloc byte[SilkSurfaceUniformWriter.ByteSize];
        SilkSurfaceUniformWriter.Write(
            material,
            light,
            constants,
            _device is ISilkVolumeTextureGraphicsDevice);
        WriteTracked(buffer, constants);
    }

    internal void BindMaterialTexture(
        ISilkGraphicsCommandList commands,
        SilkMaterialData material,
        SilkMaterialParameter parameter)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(material);
        SilkMaterialTexture texture = material.GetTexture(parameter) ??
            throw new InvalidDataException(
                $"Material '{material.Path}' has no texture for {parameter}.");
        TextureCacheEntry entry = RequireTexture(material.Path, texture);
        if (!entry.Uploaded)
        {
            commands.UploadTexture(entry.Texture, entry.Pixels);
            _textureUploadBytes += checked((ulong)entry.Pixels.Length);
            entry.Uploaded = true;
        }
        (uint samplerBinding, uint textureBinding) =
            SilkBindingLayoutDescriptor.GetMaterialTextureBindings(parameter);
        commands.SetSampler(
            0,
            samplerBinding,
            RequireSampler(texture, entry.Texture.Format, entry.IsUdim));
        commands.SetTexture(0, textureBinding, entry.Texture);
    }

    internal void BindVolumeDensityTexture(
        ISilkGraphicsCommandList commands,
        SilkMaterialData material)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(material);
        SilkMaterialTexture texture = material.GetTexture(SilkMaterialParameter.VolumeDensity) ??
            throw new InvalidDataException(
                $"Material '{material.Path}' has no sampled density volume.");
        if (commands is not ISilkVolumeTextureCommandList volumeCommands ||
            _device is not ISilkVolumeTextureGraphicsDevice)
        {
            return;
        }
        TextureCacheEntry entry = RequireVolumeTexture(texture);
        if (!entry.Uploaded)
        {
            volumeCommands.UploadTexture3D(entry.Texture, entry.Pixels);
            _textureUploadBytes += checked((ulong)entry.Pixels.Length);
            entry.Uploaded = true;
        }
        commands.SetSampler(
            0,
            SilkBindingLayoutDescriptor.VolumeSamplerBinding,
            RequireSampler(texture, entry.Texture.Format));
        commands.SetTexture(
            0,
            SilkBindingLayoutDescriptor.VolumeDensityTextureBinding,
            entry.Texture);
    }

    internal void UploadVolumeDensityTexture(
        ISilkGraphicsCommandList commands,
        SilkMaterialData material)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(material);
        SilkMaterialTexture? texture = material.GetTexture(SilkMaterialParameter.VolumeDensity);
        if (texture is null ||
            commands is not ISilkVolumeTextureCommandList volumeCommands ||
            _device is not ISilkVolumeTextureGraphicsDevice)
        {
            return;
        }
        TextureCacheEntry entry = RequireVolumeTexture(texture);
        if (!entry.Uploaded)
        {
            volumeCommands.UploadTexture3D(entry.Texture, entry.Pixels);
            _textureUploadBytes += checked((ulong)entry.Pixels.Length);
            entry.Uploaded = true;
        }
    }

    internal void UploadMaterialTexture(
        ISilkGraphicsCommandList commands,
        SilkMaterialData material,
        SilkMaterialParameter parameter)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(material);
        SilkMaterialTexture texture = material.GetTexture(parameter) ??
            throw new InvalidDataException(
                $"Material '{material.Path}' has no texture for {parameter}.");
        TextureCacheEntry entry = RequireTexture(material.Path, texture);
        if (!entry.Uploaded)
        {
            commands.UploadTexture(entry.Texture, entry.Pixels);
            _textureUploadBytes += checked((ulong)entry.Pixels.Length);
            entry.Uploaded = true;
        }
    }

    private TextureCacheEntry RequireTexture(
        string materialPath,
        SilkMaterialTexture texture)
    {
        SilkColorSpace effectiveColorSpace = GetEffectiveColorSpace(texture);
        var key = new TextureCacheKey(
            materialPath,
            texture.Asset,
            effectiveColorSpace,
            texture.Parameter);
        if (_textures.TryGetValue(key, out TextureCacheEntry? entry))
        {
            if (!DependenciesChanged(entry.Dependencies))
            {
                return entry;
            }
            _textures.Remove(key);
            entry.Texture.Dispose();
        }
        if (_failedTextures.TryGetValue(key, out entry))
        {
            return entry;
        }

        SilkDecodedImage image;
        string[] dependencies = [texture.Asset];
        try
        {
            image = texture.Asset.Contains("<UDIM>", StringComparison.Ordinal)
                ? CreateUdimAtlas(texture, effectiveColorSpace, out dependencies)
                : DecodeMaterialImage(texture.Asset, texture, effectiveColorSpace);
        }
        catch (FileNotFoundException exception)
        {
            return CreateFailedTexture(
                key,
                materialPath,
                texture,
                SilkRenderDiagnosticCodes.TextureAssetNotFound,
                exception.Message);
        }
        catch (DirectoryNotFoundException exception)
        {
            return CreateFailedTexture(
                key,
                materialPath,
                texture,
                SilkRenderDiagnosticCodes.TextureAssetNotFound,
                exception.Message);
        }
        catch (InvalidDataException exception)
        {
            return CreateFailedTexture(
                key,
                materialPath,
                texture,
                SilkRenderDiagnosticCodes.TextureDecodeFailed,
                exception.Message);
        }
        return CreateTextureEntry(
            key,
            image,
            _textures,
            texture.Asset.Contains("<UDIM>", StringComparison.Ordinal),
            dependencies,
            texture.Parameter == SilkMaterialParameter.Normal);
    }

    private TextureCacheEntry CreateFailedTexture(
        TextureCacheKey key,
        string materialPath,
        SilkMaterialTexture texture,
        string code,
        string detail)
    {
        AddDiagnostic(
            code,
            string.Concat(materialPath, "\0", texture.Asset),
            RenderDiagnosticSeverity.Warning,
            $"Material '{materialPath}' texture '{texture.Asset}' failed: {detail}");
        AddDiagnostic(
            SilkRenderDiagnosticCodes.TextureFallbackUsed,
            string.Concat(materialPath, "\0", texture.Asset, "\0", texture.Parameter),
            RenderDiagnosticSeverity.Warning,
            $"Material '{materialPath}' used the authored fallback for {texture.Parameter} texture '{texture.Asset}'.");
        return CreateTextureEntry(
            key,
            CreateFallbackImage(texture),
            _failedTextures,
            isUdim: false,
            dependencies: null,
            isNormalMap: texture.Parameter == SilkMaterialParameter.Normal);
    }

    private TextureCacheEntry CreateTextureEntry(
        TextureCacheKey key,
        SilkDecodedImage image,
        Dictionary<TextureCacheKey, TextureCacheEntry> cache,
        bool isUdim = false,
        IReadOnlyList<string>? dependencies = null,
        bool isNormalMap = false)
    {
        ISilkGraphicsTexture? gpuTexture = null;
        try
        {
            // UDIM atlases carry sparse per-tile metadata and gutter padding that a naive box
            // filter would corrupt across tile boundaries, so they always stay single-level in
            // this slice. Ordinary material images generate a full CPU mip chain instead.
            byte[] pixels = image.Pixels;
            uint mipLevelCount = 1;
            if (!isUdim)
            {
                pixels = SilkMipGenerator.GenerateChain(
                    image.Pixels,
                    image.Width,
                    image.Height,
                    image.Format,
                    isNormalMap,
                    out mipLevelCount);
            }
            gpuTexture = _device.CreateTexture2D(
                new SilkTextureDescriptor(
                    image.Width,
                    image.Height,
                    image.Format,
                    SilkTextureUsage.Sampled |
                        SilkTextureUsage.CopySource |
                        SilkTextureUsage.CopyDestination,
                    mipLevelCount));
            var entry = new TextureCacheEntry(
                gpuTexture,
                pixels,
                isUdim,
                CaptureDependencies(dependencies));
            cache.Add(key, entry);
            return entry;
        }
        catch
        {
            gpuTexture?.Dispose();
            throw;
        }
    }

    private SilkDecodedImage DecodeMaterialImage(
        string asset,
        SilkMaterialTexture texture,
        SilkColorSpace effectiveColorSpace)
    {
        SilkDecodedImage image = _imageDecoder(
            asset,
            effectiveColorSpace == SilkColorSpace.Srgb);
        ValidateDecodedImage(image);
        FlipRows(image.Pixels, image.Width, image.Height, image.Format);
        ApplyScaleBias(image.Pixels, image.Format, texture);
        return image;
    }

    private SilkDecodedImage CreateUdimAtlas(
        SilkMaterialTexture texture,
        SilkColorSpace effectiveColorSpace,
        out string[] dependencies)
    {
        IReadOnlyList<SilkUdimTile> tiles = _udimResolver(texture.Asset);
        if (tiles.Count == 0)
        {
            throw new FileNotFoundException(
                $"UDIM texture '{texture.Asset}' resolved no tiles.",
                texture.Asset);
        }
        dependencies = tiles.Select(static tile => tile.Asset).ToArray();

        SilkDecodedImage[] images = new SilkDecodedImage[tiles.Count];
        int minU = int.MaxValue;
        int minV = int.MaxValue;
        int maxU = int.MinValue;
        int maxV = int.MinValue;
        for (int index = 0; index < tiles.Count; index++)
        {
            SilkUdimTile tile = tiles[index];
            int offset = checked((int)tile.Number - 1001);
            int u = offset % 10;
            int v = offset / 10;
            minU = Math.Min(minU, u);
            minV = Math.Min(minV, v);
            maxU = Math.Max(maxU, u);
            maxV = Math.Max(maxV, v);
            images[index] = DecodeMaterialImage(
                tile.Asset,
                texture,
                effectiveColorSpace);
            if (index != 0 &&
                (images[index].Width != images[0].Width ||
                 images[index].Height != images[0].Height ||
                 images[index].Format != images[0].Format))
            {
                throw new InvalidDataException(
                    $"UDIM texture '{texture.Asset}' tiles must have identical dimensions and formats.");
            }
        }

        int columns = checked(maxU - minU + 1);
        int rows = checked(maxV - minV + 1);
        if (checked(columns * rows) > MaximumUdimAtlasCells)
        {
            throw new InvalidDataException(
                $"UDIM texture '{texture.Asset}' spans more than {MaximumUdimAtlasCells} atlas cells.");
        }

        SilkDecodedImage first = images[0];
        int bytesPerPixel = checked((int)SilkTextureFormats.GetBytesPerPixel(first.Format));
        int cellWidth = checked((int)first.Width + 2);
        int cellHeight = checked((int)first.Height + 2);
        int atlasWidth = checked(columns * cellWidth);
        int atlasHeight = checked(1 + (rows * cellHeight));
        byte[] fallback = CreateFallbackPixel(texture, first.Format);
        byte[] pixels = new byte[checked(atlasWidth * atlasHeight * bytesPerPixel)];
        for (int offset = 0; offset < pixels.Length; offset += bytesPerPixel)
        {
            fallback.CopyTo(pixels, offset);
        }
        WriteUdimMetadata(pixels, first.Format, minU, minV, columns, rows);

        for (int index = 0; index < tiles.Count; index++)
        {
            int tileOffset = checked((int)tiles[index].Number - 1001);
            int cellX = (tileOffset % 10) - minU;
            int cellY = (tileOffset / 10) - minV;
            CopyUdimTile(
                images[index],
                pixels,
                atlasWidth,
                cellX * cellWidth,
                1 + (cellY * cellHeight));
        }
        return new SilkDecodedImage(
            checked((uint)atlasWidth),
            checked((uint)atlasHeight),
            pixels,
            first.Format);
    }

    private static TextureDependency[] CaptureDependencies(
        IReadOnlyList<string>? dependencies)
    {
        if (dependencies is null || dependencies.Count == 0)
        {
            return [];
        }
        var result = new List<TextureDependency>(dependencies.Count);
        foreach (string asset in dependencies)
        {
            var file = new FileInfo(asset);
            if (file.Exists)
            {
                result.Add(new TextureDependency(
                    asset,
                    file.Length,
                    file.LastWriteTimeUtc));
            }
        }
        return result.ToArray();
    }

    private static bool DependenciesChanged(TextureDependency[]? dependencies)
    {
        if (dependencies is null)
        {
            return false;
        }
        foreach (TextureDependency dependency in dependencies)
        {
            var file = new FileInfo(dependency.Asset);
            if (!file.Exists ||
                file.Length != dependency.Length ||
                file.LastWriteTimeUtc != dependency.LastWriteTimeUtc)
            {
                return true;
            }
        }
        return false;
    }

    private static byte[] CreateFallbackPixel(
        SilkMaterialTexture texture,
        SilkTextureFormat format)
    {
        if (format == SilkTextureFormat.Rgba32Float)
        {
            float[] values = new float[4];
            for (int component = 0; component < values.Length; component++)
            {
                float source = component < texture.Fallback.Count
                    ? texture.Fallback[component]
                    : component == 3 ? 1 : 0;
                values[component] =
                    (source * texture.Scale[component]) + texture.Bias[component];
                if (!float.IsFinite(values[component]))
                {
                    throw new InvalidDataException(
                        "UDIM fallback scale and bias produced a non-finite channel.");
                }
            }
            return MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
        }
        return CreateFallbackImage(texture).Pixels;
    }

    private static void WriteUdimMetadata(
        byte[] pixels,
        SilkTextureFormat format,
        int minU,
        int minV,
        int columns,
        int rows)
    {
        Span<float> values = stackalloc float[4]
        {
            minU / 255f,
            minV / 255f,
            columns / 255f,
            rows / 255f
        };
        if (format == SilkTextureFormat.Rgba32Float)
        {
            MemoryMarshal.AsBytes(values).CopyTo(pixels);
            return;
        }
        for (int component = 0; component < 4; component++)
        {
            pixels[component] = checked((byte)MathF.Round(values[component] * 255));
        }
    }

    private static void CopyUdimTile(
        SilkDecodedImage image,
        byte[] atlas,
        int atlasWidth,
        int cellX,
        int cellY)
    {
        int bytesPerPixel = checked((int)SilkTextureFormats.GetBytesPerPixel(image.Format));
        int tileWidth = checked((int)image.Width);
        int tileHeight = checked((int)image.Height);
        int atlasStride = checked(atlasWidth * bytesPerPixel);
        int tileStride = checked(tileWidth * bytesPerPixel);
        for (int y = 0; y < tileHeight; y++)
        {
            int source = y * tileStride;
            int destination = checked(
                ((cellY + y + 1) * atlasStride) + ((cellX + 1) * bytesPerPixel));
            Buffer.BlockCopy(image.Pixels, source, atlas, destination, tileStride);
            Buffer.BlockCopy(image.Pixels, source, atlas, destination - bytesPerPixel, bytesPerPixel);
            Buffer.BlockCopy(
                image.Pixels,
                source + tileStride - bytesPerPixel,
                atlas,
                destination + tileStride,
                bytesPerPixel);
        }
        int paddedStride = checked((tileWidth + 2) * bytesPerPixel);
        int firstRow = checked(cellY * atlasStride + cellX * bytesPerPixel);
        int lastRow = checked((cellY + tileHeight + 1) * atlasStride + cellX * bytesPerPixel);
        Buffer.BlockCopy(atlas, firstRow + atlasStride, atlas, firstRow, paddedStride);
        Buffer.BlockCopy(atlas, lastRow - atlasStride, atlas, lastRow, paddedStride);
    }

    private TextureCacheEntry RequireVolumeTexture(SilkMaterialTexture texture)
    {
        if (_volumeTextures.TryGetValue(texture.Asset, out TextureCacheEntry? entry))
        {
            return entry;
        }
        if (_device is not ISilkVolumeTextureGraphicsDevice volumeDevice)
        {
            throw new NotSupportedException(
                "The current backend does not support sampled volume textures.");
        }
        VolumeTextureInfo info = ParseVolumeTextureInfo(texture.UvPrimvar);
        byte[] pixels = File.ReadAllBytes(texture.Asset);
        int requiredLength =
            checked((int)(info.Width * info.Height * info.Depth * sizeof(float)));
        if (pixels.Length != requiredLength)
        {
            throw new InvalidDataException(
                $"Volume texture '{texture.Asset}' contains {pixels.Length} bytes, expected {requiredLength}.");
        }
        ISilkGraphicsTexture? gpuTexture = null;
        try
        {
            gpuTexture = volumeDevice.CreateTexture3D(
                info.Width,
                info.Height,
                info.Depth,
                SilkTextureFormat.R32Float);
            entry = new TextureCacheEntry(gpuTexture, pixels);
            _volumeTextures.Add(texture.Asset, entry);
            return entry;
        }
        catch
        {
            gpuTexture?.Dispose();
            throw;
        }
    }

    private static VolumeTextureInfo ParseVolumeTextureInfo(string value)
    {
        string[] parts = value.Split(',');
        if (parts.Length != 3 ||
            !uint.TryParse(parts[0], out uint width) ||
            !uint.TryParse(parts[1], out uint height) ||
            !uint.TryParse(parts[2], out uint depth) ||
            width == 0 ||
            height == 0 ||
            depth == 0)
        {
            throw new InvalidDataException($"Invalid volume texture dimensions '{value}'.");
        }
        return new VolumeTextureInfo(width, height, depth);
    }

    private static SilkDecodedImage CreateFallbackImage(SilkMaterialTexture texture)
    {
        byte[] pixels = new byte[4];
        for (int component = 0; component < 4; component++)
        {
            float value = component < texture.Fallback.Count
                ? texture.Fallback[component]
                : component == 3 ? 1 : 0;
            value = (value * texture.Scale[component]) + texture.Bias[component];
            pixels[component] = (byte)Math.Clamp(MathF.Round(value * 255), 0, 255);
        }
        return new SilkDecodedImage(1, 1, pixels);
    }

    private static void FlipRows(
        byte[] pixels,
        uint width,
        uint height,
        SilkTextureFormat format)
    {
        int stride = checked((int)(width * SilkTextureFormats.GetBytesPerPixel(format)));
        byte[] row = new byte[stride];
        int last = checked((int)height) - 1;
        for (int y = 0; y < height / 2; y++)
        {
            int top = y * stride;
            int bottom = (last - y) * stride;
            Buffer.BlockCopy(pixels, top, row, 0, stride);
            Buffer.BlockCopy(pixels, bottom, pixels, top, stride);
            Buffer.BlockCopy(row, 0, pixels, bottom, stride);
        }
    }

    private static void ApplyScaleBias(
        byte[] pixels,
        SilkTextureFormat format,
        SilkMaterialTexture texture)
    {
        if (format == SilkTextureFormat.Rgba32Float)
        {
            Span<float> values = MemoryMarshal.Cast<byte, float>(pixels.AsSpan());
            for (int offset = 0; offset < values.Length; offset += 4)
            {
                for (int component = 0; component < 4; component++)
                {
                    values[offset + component] =
                        (values[offset + component] * texture.Scale[component]) +
                        texture.Bias[component];
                    if (!float.IsFinite(values[offset + component]))
                    {
                        throw new InvalidDataException(
                            "Decoded texture scale and bias produced a non-finite channel.");
                    }
                }
            }
            return;
        }
        if (format != SilkTextureFormat.Rgba8Unorm)
        {
            throw new InvalidDataException(
                $"Decoded material texture format {format} does not support scale and bias.");
        }
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            for (int component = 0; component < 4; component++)
            {
                float value = pixels[offset + component] / 255f;
                value = (value * texture.Scale[component]) + texture.Bias[component];
                pixels[offset + component] =
                    (byte)Math.Clamp(MathF.Round(value * 255), 0, 255);
            }
        }
    }

    private static void ValidateDecodedImage(SilkDecodedImage image)
    {
        if (image.Width == 0 || image.Height == 0)
        {
            throw new InvalidDataException("Decoded texture dimensions must be non-zero.");
        }
        if (image.Format is not (
            SilkTextureFormat.Rgba8Unorm or
            SilkTextureFormat.Rgba32Float))
        {
            throw new InvalidDataException(
                $"Decoded material texture format {image.Format} is unsupported.");
        }
        int expectedLength = checked(
            (int)(image.Width *
                image.Height *
                SilkTextureFormats.GetBytesPerPixel(image.Format)));
        if (image.Pixels.Length != expectedLength)
        {
            throw new InvalidDataException(
                $"Decoded texture contains {image.Pixels.Length} bytes; expected {expectedLength}.");
        }
    }

    private static SilkColorSpace GetEffectiveColorSpace(SilkMaterialTexture texture) =>
        texture.SourceColorSpace switch
        {
            SilkColorSpace.Raw => SilkColorSpace.Raw,
            SilkColorSpace.Srgb => SilkColorSpace.Srgb,
            SilkColorSpace.Auto => texture.Parameter is SilkMaterialParameter.DiffuseColor or
                SilkMaterialParameter.EmissiveColor
                    ? SilkColorSpace.Srgb
                    : SilkColorSpace.Raw,
            _ => throw new ArgumentOutOfRangeException(nameof(texture))
        };

    private ISilkGraphicsSampler RequireSampler(
        SilkMaterialTexture texture,
        SilkTextureFormat format,
        bool isUdim = false)
    {
        SilkSamplerAddressMode addressU = isUdim
            ? SilkSamplerAddressMode.ClampToEdge
            : GetAddressMode(texture.WrapS);
        SilkSamplerAddressMode addressV = isUdim
            ? SilkSamplerAddressMode.ClampToEdge
            : GetAddressMode(texture.WrapT);
        SilkSamplerFilter filter = format == SilkTextureFormat.Rgba32Float
            ? SilkSamplerFilter.Nearest
            : SilkSamplerFilter.Linear;
        var descriptor = new SilkSamplerDescriptor(
            filter,
            filter,
            addressU,
            addressV,
            SilkSamplerAddressMode.ClampToEdge);
        if (_samplers.TryGetValue(descriptor, out ISilkGraphicsSampler? sampler))
        {
            return sampler;
        }
        sampler = _device.CreateSampler(descriptor);
        _samplers.Add(descriptor, sampler);
        return sampler;
    }

    private static SilkSamplerAddressMode GetAddressMode(SilkTextureWrap wrap) =>
        wrap switch
        {
            SilkTextureWrap.Repeat => SilkSamplerAddressMode.Repeat,
            SilkTextureWrap.Mirror => SilkSamplerAddressMode.MirrorRepeat,
            SilkTextureWrap.Clamp or SilkTextureWrap.Black => SilkSamplerAddressMode.ClampToEdge,
            _ => throw new ArgumentOutOfRangeException(nameof(wrap))
        };

    private void ClearTextureCache()
    {
        foreach (TextureCacheEntry entry in _textures.Values)
        {
            entry.Texture.Dispose();
        }
        _textures.Clear();
        ClearFailedTextureCache();
        foreach (TextureCacheEntry entry in _volumeTextures.Values)
        {
            entry.Texture.Dispose();
        }
        _volumeTextures.Clear();
    }

    private void ClearFailedTextureCache()
    {
        foreach (TextureCacheEntry entry in _failedTextures.Values)
        {
            entry.Texture.Dispose();
        }
        _failedTextures.Clear();
    }

    private void PruneInactiveTextureFailures(SilkSceneState scene)
    {
        if (_failedTextures.Count == 0)
        {
            return;
        }
        if (_diagnostics.Values.Any(static diagnostic =>
                diagnostic.Code == SilkRenderDiagnosticCodes.CapacityExceeded))
        {
            ClearFailedTextureCache();
            RemoveTextureDiagnostics();
            return;
        }

        var activeMaterials = new HashSet<string>(
            scene.Meshes.Values
                .Select(static mesh => mesh.MaterialPath)
                .Where(static path => !string.IsNullOrEmpty(path)),
            StringComparer.Ordinal);
        foreach (TextureCacheKey key in _failedTextures.Keys
            .Where(key => !activeMaterials.Contains(key.MaterialPath))
            .ToArray())
        {
            TextureCacheEntry entry = _failedTextures[key];
            _failedTextures.Remove(key);
            entry.Texture.Dispose();
            RemoveTextureDiagnostics(key.MaterialPath);
        }
    }

    private void AddDiagnostic(
        string code,
        string identity,
        RenderDiagnosticSeverity severity,
        string message)
    {
        string key = string.Concat(code, "\0", identity);
        if (_diagnostics.ContainsKey(key))
        {
            return;
        }
        if (_diagnostics.Count < DiagnosticCapacity - 1)
        {
            _diagnostics.Add(key, new RenderDiagnostic(severity, code, message));
            return;
        }

        const string capacityKey = SilkRenderDiagnosticCodes.CapacityExceeded;
        if (!_diagnostics.ContainsKey(capacityKey))
        {
            _diagnostics.Add(capacityKey, new RenderDiagnostic(
                RenderDiagnosticSeverity.Warning,
                capacityKey,
                "Additional hdSilk material diagnostics were omitted."));
        }
    }

    private void RemoveTextureDiagnostics()
    {
        RemoveDiagnostics(static code =>
            code is SilkRenderDiagnosticCodes.TextureAssetNotFound or
                SilkRenderDiagnosticCodes.TextureDecodeFailed or
                SilkRenderDiagnosticCodes.TextureFallbackUsed or
                SilkRenderDiagnosticCodes.CapacityExceeded);
    }

    private void RemoveMaterialDiagnostics()
    {
        RemoveDiagnostics(static code =>
            code is SilkRenderDiagnosticCodes.MaterialUnresolved or
                SilkRenderDiagnosticCodes.MaterialUnsupported or
                SilkRenderDiagnosticCodes.CapacityExceeded);
    }

    private void RemoveMaterialResolutionDiagnostics()
    {
        RemoveDiagnostics(static code =>
            code is SilkRenderDiagnosticCodes.MaterialUnresolved or
                SilkRenderDiagnosticCodes.MaterialUnsupported);
    }

    private void RemoveTextureDiagnostics(string materialPath)
    {
        foreach (string key in _diagnostics
            .Where(pair =>
                pair.Value.Code is SilkRenderDiagnosticCodes.TextureAssetNotFound or
                    SilkRenderDiagnosticCodes.TextureDecodeFailed or
                    SilkRenderDiagnosticCodes.TextureFallbackUsed &&
                pair.Key.StartsWith(
                    string.Concat(pair.Value.Code, "\0", materialPath, "\0"),
                    StringComparison.Ordinal))
            .Select(static pair => pair.Key)
            .ToArray())
        {
            _diagnostics.Remove(key);
        }
    }

    private void RemoveDiagnostics(Func<string, bool> predicate)
    {
        foreach (string key in _diagnostics
            .Where(pair => predicate(pair.Value.Code))
            .Select(static pair => pair.Key)
            .ToArray())
        {
            _diagnostics.Remove(key);
        }
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
            DisposeMesh(mesh);
        }
        _meshes.Clear();
        _geometries.Clear();
        foreach (SurfaceBuffer surface in _surfaceBuffers.Values)
        {
            surface.Buffer?.Dispose();
        }
        _surfaceBuffers.Clear();
        ClearTextureCache();
        foreach (ISilkGraphicsSampler sampler in _samplers.Values)
        {
            sampler.Dispose();
        }
        _samplers.Clear();
        _defaultSurfaceBuffer?.Dispose();
        _defaultSurfaceBuffer = null;
        _frameBuffer?.Dispose();
        _frameBuffer = null;
        _disposed = true;
        SilkManagedDiagnostics.GpuSceneDestroyed();
    }

    private SilkMeshGpuResource CreateMesh(SilkSceneState scene, SilkMeshData mesh)
    {
        SilkMeshGpuGeometryResource geometryResource = GetOrCreateGeometry(scene, mesh);
        ISilkGraphicsBuffer? uniformBuffer = null;
        try
        {
            // Storage usage as well as Uniform: the mesh vertex shader always
            // reads its transform from the instance table at slot 6, so a
            // non-instanced draw binds this same 80-byte buffer there as a
            // one-element table. D3D12 and Vulkan happened to render correctly
            // with slot 6 left unbound because their reflection-driven binding
            // aliased it onto the uniform buffer; Metal's explicit [[buffer(6)]]
            // read nothing and collapsed every vertex, which is why hosted macOS
            // produced only clear-color pixels.
            uniformBuffer = CreateTrackedBuffer(
                SilkSceneUniformWriter.ByteSize,
                SilkBufferUsage.Uniform | SilkBufferUsage.Storage |
                    SilkBufferUsage.Upload);
            return new SilkMeshGpuResource(
                mesh,
                geometryResource,
                uniformBuffer);
        }
        catch
        {
            uniformBuffer?.Dispose();
            ReleaseGeometry(geometryResource);
            throw;
        }
    }

    private static SilkMaterialData? ResolveMaterial(SilkSceneState scene, SilkMeshData mesh)
    {
        if (string.IsNullOrEmpty(mesh.MaterialPath))
        {
            return null;
        }
        return scene.Materials.TryGetValue(mesh.MaterialPath, out SilkMaterialData? material) &&
            material.IsSupported
            ? material
            : null;
    }

    private SilkMeshGpuGeometryResource GetOrCreateGeometry(SilkSceneState scene, SilkMeshData mesh)
    {
        SilkMaterialData? material = ResolveMaterial(scene, mesh);
        string uvPrimvar = material?.GetPrimaryUvPrimvar() ?? string.Empty;
        bool normalMap = material?.GetTexture(SilkMaterialParameter.Normal) is not null;
        var key = SilkMeshGpuGeometryKey.Create(mesh, uvPrimvar, normalMap);
        if (_geometries.TryGetValue(key, out List<SilkMeshGpuGeometryResource>? matches))
        {
            foreach (SilkMeshGpuGeometryResource candidate in matches)
            {
                if (candidate.HasSameGeometry(mesh))
                {
                    candidate.AddReference();
                    return candidate;
                }
            }
        }

        SilkMeshGeometry geometry = SilkMeshGeometryBuilder.Build(mesh, uvPrimvar, normalMap);
        ReadOnlySpan<byte> vertexBytes = MemoryMarshal.AsBytes(geometry.Vertices.AsSpan());
        ReadOnlySpan<byte> indexBytes = MemoryMarshal.AsBytes(geometry.Indices.AsSpan());
        ISilkGraphicsBuffer? vertexBuffer = null;
        ISilkGraphicsBuffer? indexBuffer = null;
        try
        {
            vertexBuffer = CreateTrackedBuffer(
                GetAllocationSize(vertexBytes.Length),
                SilkBufferUsage.Vertex | SilkBufferUsage.Upload);
            if (!vertexBytes.IsEmpty)
            {
                WriteTracked(vertexBuffer, vertexBytes);
                _vertexUploads++;
            }
            indexBuffer = CreateTrackedBuffer(
                GetAllocationSize(indexBytes.Length),
                SilkBufferUsage.Index | SilkBufferUsage.Upload);
            if (!indexBytes.IsEmpty)
            {
                WriteTracked(indexBuffer, indexBytes);
                _indexUploads++;
            }

            // The instance buffer is allocated on first instanced draw, not here.
            // Most meshes are drawn once, so allocating eagerly cost one storage
            // buffer per unique geometry for nothing.
            var resource = new SilkMeshGpuGeometryResource(
                key,
                mesh,
                geometry.IndexCount,
                geometry.VertexLayout,
                geometry.UvPrimvar,
                geometry.HasTangents,
                vertexBuffer,
                indexBuffer);
            (matches ??= []).Add(resource);
            _geometries[key] = matches;
            _geometryBuilds++;
            return resource;
        }
        catch
        {
            indexBuffer?.Dispose();
            vertexBuffer?.Dispose();
            throw;
        }
    }

    private void DisposeMesh(SilkMeshGpuResource mesh)
    {
        SilkMeshGpuGeometryResource geometry = mesh.Geometry;
        mesh.Dispose();
        ReleaseGeometry(geometry);
    }

    private void ReleaseGeometry(SilkMeshGpuGeometryResource geometry)
    {
        if (geometry.ReleaseReference())
        {
            return;
        }

        if (_geometries.TryGetValue(
                geometry.Key,
                out List<SilkMeshGpuGeometryResource>? matches))
        {
            _ = matches.Remove(geometry);
            if (matches.Count == 0)
            {
                _geometries.Remove(geometry.Key);
            }
        }
        geometry.Dispose();
    }

    private static nuint GetAllocationSize(int dataLength) =>
        checked((nuint)Math.Max(dataLength, sizeof(uint)));

    private ISilkGraphicsBuffer CreateTrackedBuffer(nuint size, SilkBufferUsage usage)
    {
        _bufferAllocationBytes += checked((ulong)size);
        return _device.CreateBuffer(size, usage);
    }

    private void WriteTracked(ISilkGraphicsBuffer buffer, ReadOnlySpan<byte> data, nuint offset = 0)
    {
        buffer.Write(data, offset);
        _bufferWriteBytes += checked((ulong)data.Length);
    }
}

/// <summary>
/// GPU buffers for one retained mesh.
/// </summary>
public sealed class SilkMeshGpuResource : IDisposable
{
    private readonly byte[] _uniformBytes = new byte[SilkSceneUniformWriter.ByteSize];
    private readonly SilkMeshGpuGeometryResource _geometry;
    private SilkMeshData? _uniformMesh;
    private ulong _uniformFrameRevision = ulong.MaxValue;
    private bool _uniformOverridden;
    private bool _disposed;

    internal SilkMeshGpuResource(
        SilkMeshData mesh,
        SilkMeshGpuGeometryResource geometry,
        ISilkGraphicsBuffer uniformBuffer)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        Mesh = mesh;
        _geometry = geometry;
        UniformBuffer = uniformBuffer;
        SilkManagedDiagnostics.GpuMeshCreated();
    }

    /// <summary>Gets the retained CPU mesh metadata.</summary>
    public SilkMeshData Mesh { get; private set; }

    /// <summary>Gets interleaved float3 position and float3 normal data.</summary>
    public ISilkGraphicsBuffer VertexBuffer => _geometry.VertexBuffer;

    /// <summary>Gets packed 16-bit triangle index data.</summary>
    public ISilkGraphicsBuffer IndexBuffer => _geometry.IndexBuffer;

    internal SilkVertexLayoutDescriptor VertexLayout => _geometry.VertexLayout;

    /// <summary>Gets the reusable 80-byte SceneParameters buffer.</summary>
    public ISilkGraphicsBuffer UniformBuffer { get; }

    /// <summary>Gets the indexed triangle-list element count.</summary>
    public uint IndexCount => _geometry.IndexCount;

    internal SilkMeshGpuGeometryResource Geometry => _geometry;

    internal bool HasSameGeometry(SilkMeshData mesh) =>
        Mesh.TopologyKind == mesh.TopologyKind &&
        Mesh.Points.Span.SequenceEqual(mesh.Points.Span) &&
        Mesh.Indices.Span.SequenceEqual(mesh.Indices.Span) &&
        Mesh.AuthoredNormals.Span.SequenceEqual(mesh.AuthoredNormals.Span) &&
        _geometry.HasSameMaterialGeometry(mesh);

    internal void UpdateMesh(SilkMeshData mesh)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Mesh = mesh;
    }

    internal bool UpdateUniform(
        SilkFrameState frame,
        Span<byte> destination,
        bool flipClipSpaceY) =>
        UpdateUniform(frame, destination, flipClipSpaceY, default);

    internal bool UpdateUniform(
        SilkFrameState frame,
        Span<byte> destination,
        bool flipClipSpaceY,
        ReadOnlySpan<double> overrideTransform)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        bool overridden = !overrideTransform.IsEmpty;
        if (!overridden &&
            !_uniformOverridden &&
            ReferenceEquals(_uniformMesh, Mesh) &&
            _uniformFrameRevision == frame.Revision)
        {
            return false;
        }

        SilkSceneUniformWriter.Write(Mesh, frame, destination, flipClipSpaceY, overrideTransform);
        _uniformMesh = Mesh;
        _uniformFrameRevision = frame.Revision;
        _uniformOverridden = overridden;
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
        _disposed = true;
        SilkManagedDiagnostics.GpuMeshDestroyed();
    }
}

internal sealed class SilkMeshGpuGeometryResource : IDisposable
{
    private readonly float[] _points;
    private readonly uint[] _indices;
    private readonly float[] _authoredNormals;
    private readonly string _uvPrimvar;
    private readonly bool _hasTangents;
    private byte[] _instanceBytes;
    private SilkMeshData?[] _instanceMeshes = [];
    private ulong _instanceFrameRevision = ulong.MaxValue;
    // Starts at zero so the first instanced draw always allocates; the buffer no
    // longer exists until then.
    private int _instanceCapacity;
    private int _referenceCount = 1;
    private bool _disposed;

    internal SilkMeshGpuGeometryResource(
        SilkMeshGpuGeometryKey key,
        SilkMeshData mesh,
        uint indexCount,
        SilkVertexLayoutDescriptor vertexLayout,
        string uvPrimvar,
        bool hasTangents,
        ISilkGraphicsBuffer vertexBuffer,
        ISilkGraphicsBuffer indexBuffer)
    {
        Key = key;
        IndexCount = indexCount;
        VertexLayout = vertexLayout;
        VertexBuffer = vertexBuffer;
        IndexBuffer = indexBuffer;
        _instanceBytes = new byte[SilkSceneUniformWriter.ByteSize];
        _points = mesh.Points.ToArray();
        _indices = mesh.Indices.ToArray();
        _authoredNormals = mesh.AuthoredNormals.ToArray();
        _uvPrimvar = uvPrimvar;
        _hasTangents = hasTangents;
    }

    internal SilkMeshGpuGeometryKey Key { get; }

    internal ISilkGraphicsBuffer VertexBuffer { get; }

    internal ISilkGraphicsBuffer IndexBuffer { get; }

    internal SilkVertexLayoutDescriptor VertexLayout { get; }

    /// <summary>
    /// Gets the per-instance transform buffer, null until an instanced draw
    /// first needs it. Most meshes are drawn once and never allocate one.
    /// </summary>
    internal ISilkGraphicsBuffer? InstanceBuffer { get; private set; }

    internal uint IndexCount { get; }

    internal bool HasSameGeometry(SilkMeshData mesh) =>
        Key.TopologyKind == mesh.TopologyKind &&
        _points.AsSpan().SequenceEqual(mesh.Points.Span) &&
        _indices.AsSpan().SequenceEqual(mesh.Indices.Span) &&
        _authoredNormals.AsSpan().SequenceEqual(mesh.AuthoredNormals.Span) &&
        HasSameMaterialGeometry(mesh);

    internal bool HasSameMaterialGeometry(SilkMeshData mesh) =>
        (string.IsNullOrEmpty(_uvPrimvar) || mesh.FindTexCoord(_uvPrimvar) is not null) &&
        _hasTangents == VertexLayout.Equals(SilkVertexLayoutDescriptor.PositionNormalTexCoordTangent);

    /// <summary>
    /// Returns the instance buffer, which an instanced draw must have created.
    /// </summary>
    internal ISilkGraphicsBuffer RequireInstanceBuffer() =>
        InstanceBuffer ?? throw new InvalidOperationException(
            "An instanced draw requires UpdateInstanceBuffer to have run first.");

    internal void UpdateInstanceBuffer(
        ISilkGraphicsDevice device,
        SilkFrameState frame,
        IReadOnlyList<SilkMeshGpuResource> instances,
        bool flipClipSpaceY)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        bool unchanged = _instanceFrameRevision == frame.Revision &&
            _instanceMeshes.Length == instances.Count;
        if (unchanged)
        {
            for (int index = 0; index < instances.Count; index++)
            {
                unchanged &= ReferenceEquals(_instanceMeshes[index], instances[index].Mesh);
            }
            if (unchanged)
            {
                return;
            }
        }

        int required = checked(instances.Count * SilkSceneUniformWriter.ByteSize);
        if (instances.Count > _instanceCapacity)
        {
            InstanceBuffer?.Dispose();
            _instanceCapacity = Math.Max(instances.Count, _instanceCapacity * 2);
            InstanceBuffer = device.CreateBuffer(
                checked((nuint)(_instanceCapacity * SilkSceneUniformWriter.ByteSize)),
                SilkBufferUsage.Storage | SilkBufferUsage.Upload);
            _instanceBytes = new byte[_instanceCapacity * SilkSceneUniformWriter.ByteSize];
        }
        if (_instanceMeshes.Length != instances.Count)
        {
            _instanceMeshes = new SilkMeshData[instances.Count];
        }

        Span<byte> encoded = stackalloc byte[SilkSceneUniformWriter.ByteSize];
        int changedStart = -1;
        int changedLength = 0;
        for (int index = 0; index < instances.Count; index++)
        {
            SilkMeshData mesh = instances[index].Mesh;
            _instanceMeshes[index] = mesh;
            SilkSceneUniformWriter.Write(
                mesh,
                frame,
                encoded,
                flipClipSpaceY);
            int offset = index * SilkSceneUniformWriter.ByteSize;
            Span<byte> retained = _instanceBytes.AsSpan(
                offset,
                SilkSceneUniformWriter.ByteSize);
            if (encoded.SequenceEqual(retained))
            {
                continue;
            }

            encoded.CopyTo(retained);
            if (changedStart < 0)
            {
                changedStart = offset;
                changedLength = SilkSceneUniformWriter.ByteSize;
            }
            else if (changedStart + changedLength == offset)
            {
                changedLength += SilkSceneUniformWriter.ByteSize;
            }
            else
            {
                RequireInstanceBuffer().Write(
                    _instanceBytes.AsSpan(changedStart, changedLength),
                    checked((nuint)changedStart));
                changedStart = offset;
                changedLength = SilkSceneUniformWriter.ByteSize;
            }
        }
        if (changedStart >= 0)
        {
            RequireInstanceBuffer().Write(
                _instanceBytes.AsSpan(changedStart, changedLength),
                checked((nuint)changedStart));
        }
        _instanceFrameRevision = frame.Revision;
    }

    internal void AddReference()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _referenceCount++;
    }

    internal bool ReleaseReference()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _referenceCount--;
        if (_referenceCount < 0)
        {
            throw new InvalidOperationException(
                "The Silk mesh geometry reference count became negative.");
        }
        return _referenceCount != 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        InstanceBuffer?.Dispose();
        IndexBuffer.Dispose();
        VertexBuffer.Dispose();
        _disposed = true;
    }
}

internal readonly record struct SilkMeshGpuGeometryKey(
    string Path,
    SilkTopologyKind TopologyKind,
    ulong TopologyFingerprint,
    ulong PointFingerprint,
    ulong NormalFingerprint,
    string UvPrimvar,
    bool HasTangents)
{
    internal static SilkMeshGpuGeometryKey Create(
        SilkMeshData mesh,
        string uvPrimvar,
        bool hasTangents) =>
        new(
            mesh.Path,
            mesh.TopologyKind,
            mesh.TopologyFingerprint,
            HashFloats(mesh.Points.Span),
            HashFloats(mesh.AuthoredNormals.Span),
            uvPrimvar,
            hasTangents);

    private static ulong HashFloats(ReadOnlySpan<float> values)
    {
        const ulong offsetBasis = 14695981039346656037;
        const ulong prime = 1099511628211;
        ulong hash = offsetBasis;
        foreach (float value in values)
        {
            unchecked
            {
                hash ^= (uint)BitConverter.SingleToInt32Bits(value);
                hash *= prime;
            }
        }
        return hash;
    }
}

/// <summary>Cumulative retained-scene GPU upload diagnostics.</summary>
public readonly record struct SilkSceneGpuStatistics(
    int MeshCount,
    ulong GeometryBuilds,
    ulong VertexUploads,
    ulong IndexUploads,
    ulong UniformUploads,
    ulong BufferAllocationBytes,
    ulong BufferWriteBytes,
    ulong TextureUploadBytes);
