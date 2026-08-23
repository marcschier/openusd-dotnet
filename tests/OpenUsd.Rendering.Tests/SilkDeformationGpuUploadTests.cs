// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Proves simulated points reach the hdSilk vertex buffers, not merely the CPU scene dictionaries.
/// </summary>
/// <remarks>
/// <para>
/// Replacing points in <see cref="SilkSceneState"/> alone is invisible to the GPU: retained
/// geometry is rebuilt from a scene delta, so a deformation that emits none leaves the vertex
/// buffers holding authored geometry and the frame draws the rest pose while
/// <c>Scene.MeshesByPath</c> reports the simulated one. Every assertion here therefore reads the
/// bytes a device actually received.
/// </para>
/// <para>
/// The device is a recording fake rather than a real backend, so the test runs everywhere while
/// still exercising the production upload path: the real <see cref="SilkSceneGpuResources"/>, the
/// real geometry builder, and the real buffer writes.
/// </para>
/// </remarks>
public sealed class SilkDeformationGpuUploadTests
{
    private const string MeshPath = "/Cloth";
    private const int StrideFloats = 6;

    private static readonly float[] AuthoredPoints = [0, 0, 0, 1, 0, 0, 1, 0, 1];

    [Test]
    public async Task ASimulatedDeformationChangesTheUploadedVertexData()
    {
        using var device = new RecordingGraphicsDevice();
        using var resources = new SilkSceneGpuResources(device);
        var scene = new SilkSceneState();
        SilkSceneDelta authored = scene.Apply(CreateMeshCommand(), 1, 1);
        resources.Apply(scene, authored);

        float[] uploaded = ReadPositions(device, resources, scene);
        await Assert.That(uploaded).IsEquivalentTo(AuthoredPoints);
        ulong uploadsBefore = resources.Statistics.VertexUploads;

        (SilkPhysicsDeformations deformations, PhysicsRenderBindingTable bindings) = Drive(scene);
        float[] simulated = [0, 0.5f, 0, 1, 0.5f, 0, 1, 0.5f, 1];
        int driven = deformations.Refresh(scene, bindings, View(simulated));

        await Assert.That(driven).IsEqualTo(1);
        await Assert.That(deformations.HasPendingGeometry).IsTrue();
        await Assert.That(deformations.Delta.MeshUpserts).IsEqualTo(1);

        resources.Apply(scene, deformations.Delta);

        // The decisive assertion: the bytes the device holds are the simulated
        // ones. Asserting Scene.MeshesByPath here would pass even with no upload.
        await Assert.That(ReadPositions(device, resources, scene)).IsEquivalentTo(simulated);
        await Assert.That(resources.Statistics.VertexUploads).IsGreaterThan(uploadsBefore);
    }

    [Test]
    public async Task AnAuthoredPageThatOverwritesTheGeometryIsFollowedByAReapply()
    {
        using var device = new RecordingGraphicsDevice();
        using var resources = new SilkSceneGpuResources(device);
        var scene = new SilkSceneState();
        resources.Apply(scene, scene.Apply(CreateMeshCommand(), 1, 1));

        (SilkPhysicsDeformations deformations, PhysicsRenderBindingTable bindings) = Drive(scene);
        float[] simulated = [0, 0.5f, 0, 1, 0.5f, 0, 1, 0.5f, 1];
        _ = deformations.Refresh(scene, bindings, View(simulated));
        resources.Apply(scene, deformations.Delta);
        await Assert.That(ReadPositions(device, resources, scene)).IsEquivalentTo(simulated);

        // The delegate republishes authored geometry on every page, so a page
        // applied after a deformation puts the rest pose back into the scene.
        SilkSceneDelta republished = scene.Apply(CreateMeshCommand(), 1, 2);
        resources.Apply(scene, republished);
        await Assert.That(ReadPositions(device, resources, scene)).IsEquivalentTo(AuthoredPoints);

        // Re-applying the retained batch is what makes the ordering safe: the
        // simulated points win for the frame that is about to be drawn.
        int driven = deformations.Reapply(scene);
        await Assert.That(driven).IsEqualTo(1);
        await Assert.That(deformations.HasPendingGeometry).IsTrue();
        resources.Apply(scene, deformations.Delta);
        await Assert.That(ReadPositions(device, resources, scene)).IsEquivalentTo(simulated);
    }

    [Test]
    public async Task ASettledBodyUploadsNothingAndStaysDriven()
    {
        using var device = new RecordingGraphicsDevice();
        using var resources = new SilkSceneGpuResources(device);
        var scene = new SilkSceneState();
        resources.Apply(scene, scene.Apply(CreateMeshCommand(), 1, 1));

        (SilkPhysicsDeformations deformations, PhysicsRenderBindingTable bindings) = Drive(scene);
        float[] simulated = [0, 0.5f, 0, 1, 0.5f, 0, 1, 0.5f, 1];
        _ = deformations.Refresh(scene, bindings, View(simulated));
        resources.Apply(scene, deformations.Delta);

        ulong uploadsAfterMotion = resources.Statistics.VertexUploads;
        ulong revisionAfterMotion = deformations.Revision;

        // The body settles: it republishes the points it already carries. That
        // is a success with nothing to upload, and it must stay driven.
        int driven = deformations.Refresh(scene, bindings, View(simulated));

        await Assert.That(driven).IsEqualTo(1);
        await Assert.That(deformations.Count).IsEqualTo(1);
        await Assert.That(deformations.UnchangedRegions).IsEqualTo(1);
        await Assert.That(deformations.MismatchedRegions).IsEqualTo(0);
        await Assert.That(deformations.MissingMeshRegions).IsEqualTo(0);
        await Assert.That(deformations.NonFiniteRegions).IsEqualTo(0);
        await Assert.That(deformations.HasPendingGeometry).IsFalse();
        await Assert.That(deformations.Delta.MeshUpserts).IsEqualTo(0);

        // A settled frame must not churn: no revision bump and no upload.
        await Assert.That(deformations.Revision).IsEqualTo(revisionAfterMotion);
        resources.Apply(scene, deformations.Delta);
        await Assert.That(resources.Statistics.VertexUploads).IsEqualTo(uploadsAfterMotion);
        await Assert.That(ReadPositions(device, resources, scene)).IsEquivalentTo(simulated);
    }

    [Test]
    public async Task RestoringPutsTheAuthoredGeometryBackOnTheDeviceAndInTheScene()
    {
        using var device = new RecordingGraphicsDevice();
        using var resources = new SilkSceneGpuResources(device);
        var scene = new SilkSceneState();
        resources.Apply(scene, scene.Apply(CreateMeshCommand(), 1, 1));

        (SilkPhysicsDeformations deformations, PhysicsRenderBindingTable bindings) = Drive(scene);
        float[] simulated = [0, 0.5f, 0, 1, 0.5f, 0, 1, 0.5f, 1];
        _ = deformations.Refresh(scene, bindings, View(simulated));
        resources.Apply(scene, deformations.Delta);
        await Assert.That(ReadPositions(device, resources, scene)).IsEquivalentTo(simulated);
        await Assert.That(scene.HasAuthoredGeometry(scene.MeshesByPath[(MeshPath, 0)].Id)).IsTrue();

        // Restoring is the only thing that puts the rest pose back on a stage that authors nothing
        // further: the replacement was destructive, so no page is coming to undo it.
        int restored = deformations.Restore(scene);
        resources.Apply(scene, deformations.Delta);

        await Assert.That(restored).IsEqualTo(1);
        await Assert.That(deformations.RestoredMeshes).IsEqualTo(1);
        await Assert.That(deformations.Count).IsEqualTo(0);
        await Assert.That(deformations.HasBatch).IsFalse();
        await Assert.That(ReadPositions(device, resources, scene)).IsEquivalentTo(AuthoredPoints);
        await Assert.That(scene.MeshesByPath[(MeshPath, 0)].Points.ToArray())
            .IsEquivalentTo(AuthoredPoints);
        await Assert.That(scene.HasAuthoredGeometry(scene.MeshesByPath[(MeshPath, 0)].Id)).IsFalse();

        // Restoring twice is a no-op rather than a second upload or a throw.
        await Assert.That(deformations.Restore(scene)).IsEqualTo(0);
    }

    [Test]
    public async Task ARemovedMeshRetiresItsRetainedAuthoredGeometry()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateMeshCommand(), 1, 1);
        (SilkPhysicsDeformations deformations, PhysicsRenderBindingTable bindings) = Drive(scene);
        _ = deformations.Refresh(scene, bindings, View([0, 0.5f, 0, 1, 0.5f, 0, 1, 0.5f, 1]));
        ulong meshId = scene.MeshesByPath[(MeshPath, 0)].Id;
        await Assert.That(scene.HasAuthoredGeometry(meshId)).IsTrue();

        _ = scene.Apply(CreateMeshRemoveCommand(), 1, 2);

        // Nothing retains geometry for a mesh that is gone, and restoring one reports no upload
        // rather than resurrecting it.
        await Assert.That(scene.HasAuthoredGeometry(meshId)).IsFalse();
        await Assert.That(scene.RestoreAuthoredPoints(meshId)).IsFalse();
        await Assert.That(deformations.Restore(scene)).IsEqualTo(0);
        await Assert.That(scene.MeshesByPath.ContainsKey((MeshPath, 0))).IsFalse();
    }

    [Test]
    public async Task EachRefusalIsCountedAsItsOwnReason()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateMeshCommand(), 1, 1);
        (SilkPhysicsDeformations deformations, PhysicsRenderBindingTable bindings) = Drive(scene);

        _ = deformations.Refresh(scene, bindings, View([0, 1, 0, 1, 1, 0]));
        await Assert.That(deformations.MismatchedRegions).IsEqualTo(1);
        await Assert.That(deformations.NonFiniteRegions).IsEqualTo(0);
        await Assert.That(deformations.MissingMeshRegions).IsEqualTo(0);

        _ = deformations.Refresh(scene, bindings, View([0, float.NaN, 0, 1, 1, 0, 1, 1, 1]));
        await Assert.That(deformations.NonFiniteRegions).IsEqualTo(1);
        await Assert.That(deformations.MismatchedRegions).IsEqualTo(1);

        var unbound = new PhysicsRenderBindingTable(4);
        _ = unbound.TryBind(Identity, "/Absent");
        _ = deformations.Refresh(scene, unbound, View([0, 1, 0, 1, 1, 0, 1, 1, 1]));
        await Assert.That(deformations.MissingMeshRegions).IsEqualTo(1);
        await Assert.That(deformations.UnresolvedRegions).IsEqualTo(0);

        var empty = new PhysicsRenderBindingTable(1);
        _ = deformations.Refresh(scene, empty, View([0, 1, 0, 1, 1, 0, 1, 1, 1]));
        await Assert.That(deformations.UnresolvedRegions).IsEqualTo(1);

        // Every reason stayed on its own counter throughout.
        await Assert.That(deformations.MismatchedRegions).IsEqualTo(1);
        await Assert.That(deformations.NonFiniteRegions).IsEqualTo(1);
        await Assert.That(deformations.MissingMeshRegions).IsEqualTo(1);
        await Assert.That(deformations.UnchangedRegions).IsEqualTo(0);
    }

    [Test]
    public async Task ReplacePointsNamesEachOutcome()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateMeshCommand(), 1, 1);

        await Assert.That(scene.ReplacePoints("/Absent", 0, AuthoredPoints, out ulong absent))
            .IsEqualTo(SilkDeformationResult.MeshMissing);
        await Assert.That(absent).IsEqualTo(0UL);

        await Assert.That(scene.ReplacePoints(MeshPath, 0, [0, 1, 0], out _))
            .IsEqualTo(SilkDeformationResult.VertexCountMismatch);
        await Assert.That(scene.ReplacePoints(
                MeshPath, 0, [0, float.PositiveInfinity, 0, 1, 1, 0, 1, 1, 1], out _))
            .IsEqualTo(SilkDeformationResult.NonFiniteValue);

        // A settled body reports the mesh it drives, so it never drops out of
        // the driven set on the frame it stops moving.
        await Assert.That(scene.ReplacePoints(MeshPath, 0, AuthoredPoints, out ulong settled))
            .IsEqualTo(SilkDeformationResult.Unchanged);
        await Assert.That(settled).IsNotEqualTo(0UL);

        await Assert.That(scene.ReplacePoints(
                MeshPath, 0, [0, 2, 0, 1, 2, 0, 1, 2, 1], out ulong moved))
            .IsEqualTo(SilkDeformationResult.Applied);
        await Assert.That(moved).IsEqualTo(settled);
    }

    private static PhysicsRenderObjectId Identity =>
        new(404, PhysicsRenderObjectKind.Deformable);

    private static (SilkPhysicsDeformations Deformations, PhysicsRenderBindingTable Bindings) Drive(
        SilkSceneState scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var bindings = new PhysicsRenderBindingTable(4);
        _ = bindings.TryBind(Identity, MeshPath);
        return (new SilkPhysicsDeformations(), bindings);
    }

    private static PhysicsRenderDeformationView View(float[] vertices) =>
        new(
            new PhysicsRenderDeformableRegion[]
            {
                new(Identity, PhysicsRenderDomain.Cloth, 0, vertices.Length / 3, 7)
            },
            vertices,
            revision: 5);

    /// <summary>Reads the position components out of the vertex buffer the device holds.</summary>
    private static float[] ReadPositions(
        RecordingGraphicsDevice device,
        SilkSceneGpuResources resources,
        SilkSceneState scene)
    {
        SilkMeshData mesh = scene.MeshesByPath[(MeshPath, 0)];
        SilkMeshGpuResource resource = resources.Meshes[mesh.Id];
        RecordingGraphicsBuffer buffer = device.Track(resource.VertexBuffer);
        int pointCount = mesh.Points.Length / 3;
        var positions = new float[pointCount * 3];
        ReadOnlySpan<float> floats = MemoryMarshal.Cast<byte, float>(buffer.Data);
        for (int point = 0; point < pointCount; point++)
        {
            int source = point * StrideFloats;
            positions[point * 3] = floats[source];
            positions[(point * 3) + 1] = floats[source + 1];
            positions[(point * 3) + 2] = floats[source + 2];
        }

        return positions;
    }

    private static byte[] CreateMeshRemoveCommand()
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(MeshPath);
        var bytes = new byte[24 + pathBytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshRemove);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkWireFormat.ComputeStableHash(MeshPath));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), (uint)pathBytes.Length);
        pathBytes.CopyTo(bytes.AsSpan(24));
        return bytes;
    }

    private static byte[] CreateMeshCommand()
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(MeshPath);
        uint[] indices = [0, 1, 2];
        int size = 224 +
            pathBytes.Length +
            (AuthoredPoints.Length * sizeof(float)) +
            (indices.Length * sizeof(uint)) +
            sizeof(uint);
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkWireFormat.ComputeStableHash(MeshPath));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 7);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), 0);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(28),
            (uint)SilkTopologyKind.TriangleList);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(44),
            (uint)SilkMeshCullStyle.BackUnlessDoubleSided);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), (uint)pathBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), 1);
        for (int i = 0; i < 4; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(64 + (i * 4)), 1);
        }
        for (int i = 0; i < 16; i++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(80 + (i * 8)),
                i % 5 == 0 ? 1 : 0);
        }

        int cursor = 224;
        pathBytes.CopyTo(bytes.AsSpan(cursor));
        cursor += pathBytes.Length;
        foreach (float value in AuthoredPoints)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(cursor), value);
            cursor += sizeof(float);
        }
        foreach (uint value in indices)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), value);
            cursor += sizeof(uint);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), 0);
        return bytes;
    }

    /// <summary>A device that keeps every byte written to every buffer it created.</summary>
    private sealed class RecordingGraphicsDevice : ISilkGraphicsDevice
    {
        private readonly List<RecordingGraphicsBuffer> _buffers = [];

        public SilkGraphicsBackend Backend => SilkGraphicsBackend.Vulkan;

        public SilkGraphicsCapabilities Capabilities { get; } =
            new("Test", "1", SupportsCompute: true, IsSoftware: true);

        internal RecordingGraphicsBuffer Track(ISilkGraphicsBuffer buffer)
        {
            foreach (RecordingGraphicsBuffer candidate in _buffers)
            {
                if (ReferenceEquals(candidate, buffer))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException("The buffer was not created by this device.");
        }

        public ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage)
        {
            var buffer = new RecordingGraphicsBuffer(size, usage);
            _buffers.Add(buffer);
            return buffer;
        }

        public ISilkGraphicsTexture CreateTexture2D(
            uint width,
            uint height,
            SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm) =>
            throw new NotSupportedException();

        public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsShaderModule CreateShaderModule(SilkShaderModuleDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsBindingLayout CreateBindingLayout(
            SilkBindingLayoutDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsShaderProgram CreateShaderProgram(
            SilkShaderProgramDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsPipeline CreateGraphicsPipeline(
            SilkGraphicsPipelineDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputeBindingLayout CreateComputeBindingLayout(
            SilkComputeBindingLayoutDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputeShaderProgram CreateComputeShaderProgram(
            SilkComputeShaderProgramDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputePipeline CreateComputePipeline(
            SilkComputePipelineDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsCommandList CreateCommandList() => throw new NotSupportedException();

        public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList) =>
            throw new NotSupportedException();

        public void WaitIdle()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingGraphicsBuffer(nuint size, SilkBufferUsage usage)
        : SilkGraphicsBufferBase(size, usage)
    {
        internal byte[] Data { get; } = new byte[checked((int)size)];

        internal int Writes { get; private set; }

        public override void Write(ReadOnlySpan<byte> data, nuint offset = 0)
        {
            _ = ValidateWrite(data.Length, offset);
            data.CopyTo(Data.AsSpan(checked((int)offset)));
            Writes++;
        }

        public override void ReadbackForTesting(Span<byte> destination)
        {
            _ = ValidateReadback(destination.Length);
            Data.CopyTo(destination);
        }

        protected override void ReleaseNative()
        {
        }
    }
}
