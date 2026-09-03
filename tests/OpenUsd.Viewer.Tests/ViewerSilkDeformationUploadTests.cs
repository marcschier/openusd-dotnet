// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Drives simulated geometry through the exact sequence the Silk presentation path runs, and reads
/// the bytes the device received.
/// </summary>
/// <remarks>
/// <para>
/// The defect this pins was invisible to every existing gate. Production ran
/// <c>ViewerSilkPhysicsOverrideApplier.ApplyDeformations</c> - which applied the points to the
/// retained scene there and then - and only afterwards called
/// <c>SilkMeshRenderer.ApplyAndRender</c>, whose own apply found every mesh already carrying the
/// simulated points. That is the settled case,
/// so it reported unchanged, produced an empty geometry delta, and uploaded nothing. The CPU scene
/// held the simulation and the vertex buffers held the authored rest pose, with no diagnostic
/// anywhere. The GPU upload tests in the rendering suite could not see it because they apply the
/// delta by hand immediately after the apply, which is not what the viewer does.
/// </para>
/// <para>
/// Every assertion here therefore reads the vertex buffer of a recording device after a real
/// <c>ApplyAndRender</c>, using the production applier and the production ordering.
/// </para>
/// </remarks>
public sealed class ViewerSilkDeformationUploadTests
{
    private const string MeshPath = "/World/Cloth";
    private const string SecondMeshPath = "/World/Cloth2";
    private const int StrideFloats = 6;

    private static readonly float[] AuthoredPoints = [0, 0, 0, 1, 0, 0, 1, 0, 1];
    private static readonly float[] SimulatedPoints = [0, 0.5f, 0, 1, 0.5f, 0, 1, 0.5f, 1];
    private static readonly float[] MovedAuthoredPoints = [0, 2, 0, 1, 2, 0, 1, 2, 1];

    // A flat mesh in the XZ plane with authored +Y normals, and a deformation that lifts one corner
    // out of that plane. No area-weighted normal of the bent triangle is +Y, so a buffer that still
    // shades with the authored value cannot be mistaken for one that recomputed.
    private static readonly float[] AuthoredNormals = [0, 1, 0, 0, 1, 0, 0, 1, 0];
    private static readonly float[] BentPoints = [0, 0, 0, 1, 0, 0, 1, 1.5f, 1];

    [Test]
    public async Task TheProductionOrderingUploadsSimulatedPointsWithoutAnAuthoredMeshUpsert()
    {
        using var device = new RecordingSilkDevice();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(64, 64));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(64, 64));

        using (OpenUsdSilkPage authored = CreateMeshPage(revision: 1))
        {
            _ = renderer.ApplyAndRender(authored, color, depth);
        }

        await Assert.That(ReadUploadedPositions(device, renderer)).IsEquivalentTo(AuthoredPoints);

        var stage = new ViewerPhysicsOverrideStage();
        var deformations = new SilkPhysicsDeformations();
        StageDeformation(stage, SimulatedPoints);

        // Exactly the production sequence: the applier hands the batch over, then the renderer
        // applies the authored page and draws.
        _ = ViewerSilkPhysicsOverrideApplier.ApplyDeformations(stage, deformations, renderer);
        using (OpenUsdSilkPage empty = CreateFramePage(revision: 2))
        {
            _ = renderer.ApplyAndRender(empty, color, depth);
        }

        // The decisive assertion: the bytes the device holds are the simulated ones. Asserting on
        // Scene.MeshesByPath here passes even when nothing was ever uploaded.
        await Assert.That(ReadUploadedPositions(device, renderer)).IsEquivalentTo(SimulatedPoints);
        await Assert.That(deformations.Count).IsEqualTo(1);
        await Assert.That(deformations.MissingMeshRegions).IsEqualTo(0);
        await Assert.That(deformations.MismatchedRegions).IsEqualTo(0);
    }

    [Test]
    public async Task AnAuthoredMeshUpsertInTheSameFrameCannotOverwriteTheDeformation()
    {
        using var device = new RecordingSilkDevice();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(64, 64));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(64, 64));

        using (OpenUsdSilkPage authored = CreateMeshPage(revision: 1))
        {
            _ = renderer.ApplyAndRender(authored, color, depth);
        }

        var stage = new ViewerPhysicsOverrideStage();
        var deformations = new SilkPhysicsDeformations();
        StageDeformation(stage, SimulatedPoints);
        _ = ViewerSilkPhysicsOverrideApplier.ApplyDeformations(stage, deformations, renderer);

        // The delegate republishes authored geometry on every page, so this frame carries a mesh
        // upsert that puts the rest pose back into the scene before the simulation is applied.
        using (OpenUsdSilkPage republished = CreateMeshPage(revision: 2))
        {
            _ = renderer.ApplyAndRender(republished, color, depth);
        }

        await Assert.That(ReadUploadedPositions(device, renderer)).IsEquivalentTo(SimulatedPoints);
    }

    [Test]
    public async Task ASettledBodyUploadsNothingAndKeepsTheSimulatedGeometryOnScreen()
    {
        using var device = new RecordingSilkDevice();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(64, 64));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(64, 64));

        using (OpenUsdSilkPage authored = CreateMeshPage(revision: 1))
        {
            _ = renderer.ApplyAndRender(authored, color, depth);
        }

        var stage = new ViewerPhysicsOverrideStage();
        var deformations = new SilkPhysicsDeformations();
        StageDeformation(stage, SimulatedPoints);
        _ = ViewerSilkPhysicsOverrideApplier.ApplyDeformations(stage, deformations, renderer);
        using (OpenUsdSilkPage empty = CreateFramePage(revision: 2))
        {
            _ = renderer.ApplyAndRender(empty, color, depth);
        }

        ulong uploadsAfterMotion = renderer.GpuResources.Statistics.VertexUploads;
        ulong revisionAfterMotion = deformations.Revision;

        // The body settles: it republishes the points it already carries, over a frame that
        // authors nothing. That is a success with nothing to upload.
        StageDeformation(stage, SimulatedPoints);
        _ = ViewerSilkPhysicsOverrideApplier.ApplyDeformations(stage, deformations, renderer);
        using (OpenUsdSilkPage empty = CreateFramePage(revision: 3))
        {
            _ = renderer.ApplyAndRender(empty, color, depth);
        }

        await Assert.That(renderer.GpuResources.Statistics.VertexUploads)
            .IsEqualTo(uploadsAfterMotion);
        await Assert.That(deformations.Revision).IsEqualTo(revisionAfterMotion);
        await Assert.That(deformations.UnchangedRegions).IsEqualTo(1);
        await Assert.That(deformations.Count).IsEqualTo(1);
        await Assert.That(ReadUploadedPositions(device, renderer)).IsEquivalentTo(SimulatedPoints);
    }

    [Test]
    public async Task AnEmptyBatchRestoresTheAuthoredGeometryOnAPageThatAuthorsNothing()
    {
        using var device = new RecordingSilkDevice();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(64, 64));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(64, 64));
        using (OpenUsdSilkPage authored = CreateMeshPage(revision: 1))
        {
            _ = renderer.ApplyAndRender(authored, color, depth);
        }

        var stage = new ViewerPhysicsOverrideStage();
        var deformations = new SilkPhysicsDeformations();
        StageDeformation(stage, SimulatedPoints);
        _ = ViewerSilkPhysicsOverrideApplier.ApplyDeformations(stage, deformations, renderer);
        using (OpenUsdSilkPage empty = CreateFramePage(revision: 2))
        {
            _ = renderer.ApplyAndRender(empty, color, depth);
        }

        await Assert.That(ReadUploadedPositions(device, renderer)).IsEquivalentTo(SimulatedPoints);

        // Stopping the simulation stages an empty batch, exactly as ClearPhysicsOverrides does.
        stage.ClearDeformations();
        _ = ViewerSilkPhysicsOverrideApplier.ApplyDeformations(stage, deformations, renderer);
        using (OpenUsdSilkPage empty = CreateFramePage(revision: 3))
        {
            _ = renderer.ApplyAndRender(empty, color, depth);
        }

        // The page authored no geometry at all, so nothing but restoration can put the rest pose
        // back - in the vertex buffer as well as in the retained scene.
        await Assert.That(ReadUploadedPositions(device, renderer)).IsEquivalentTo(AuthoredPoints);
        await Assert.That(ReadScenePoints(renderer)).IsEquivalentTo(AuthoredPoints);
        await Assert.That(deformations.Count).IsEqualTo(0);
        await Assert.That(deformations.RestoredMeshes).IsEqualTo(1);
        await Assert.That(renderer.Scene.HasAuthoredGeometry(MeshId(renderer))).IsFalse();
    }

    [Test]
    public async Task AnAuthoredUpsertWhileDrivenIsWhatRestorationPutsBack()
    {
        using var device = new RecordingSilkDevice();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(64, 64));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(64, 64));
        using (OpenUsdSilkPage authored = CreateMeshPage(revision: 1))
        {
            _ = renderer.ApplyAndRender(authored, color, depth);
        }

        var stage = new ViewerPhysicsOverrideStage();
        var deformations = new SilkPhysicsDeformations();
        StageDeformation(stage, SimulatedPoints);
        _ = ViewerSilkPhysicsOverrideApplier.ApplyDeformations(stage, deformations, renderer);
        using (OpenUsdSilkPage empty = CreateFramePage(revision: 2))
        {
            _ = renderer.ApplyAndRender(empty, color, depth);
        }

        // The stage moves the cloth while it is being simulated: a page re-authors the same mesh
        // with different rest points, and the simulation keeps drawing over it.
        StageDeformation(stage, SimulatedPoints);
        _ = ViewerSilkPhysicsOverrideApplier.ApplyDeformations(stage, deformations, renderer);
        using (OpenUsdSilkPage reauthored = CreateMeshPage(revision: 3, MovedAuthoredPoints))
        {
            _ = renderer.ApplyAndRender(reauthored, color, depth);
        }

        await Assert.That(ReadUploadedPositions(device, renderer)).IsEquivalentTo(SimulatedPoints);

        stage.ClearDeformations();
        _ = ViewerSilkPhysicsOverrideApplier.ApplyDeformations(stage, deformations, renderer);
        using (OpenUsdSilkPage empty = CreateFramePage(revision: 4))
        {
            _ = renderer.ApplyAndRender(empty, color, depth);
        }

        // The newest authored points come back, not the ones the mesh had when it started being
        // simulated. A stale baseline here would silently rewind the stage.
        await Assert.That(ReadUploadedPositions(device, renderer))
            .IsEquivalentTo(MovedAuthoredPoints);
        await Assert.That(ReadScenePoints(renderer)).IsEquivalentTo(MovedAuthoredPoints);
    }

    [Test]
    public async Task DroppingOneRegionRestoresOnlyThatMesh()
    {
        using var device = new RecordingSilkDevice();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(64, 64));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(64, 64));
        using (OpenUsdSilkPage authored = CreateTwoMeshPage(revision: 1))
        {
            _ = renderer.ApplyAndRender(authored, color, depth);
        }

        var stage = new ViewerPhysicsOverrideStage();
        var deformations = new SilkPhysicsDeformations();
        StageTwoDeformations(stage);
        _ = ViewerSilkPhysicsOverrideApplier.ApplyDeformations(stage, deformations, renderer);
        using (OpenUsdSilkPage empty = CreateFramePage(revision: 2))
        {
            _ = renderer.ApplyAndRender(empty, color, depth);
        }

        await Assert.That(ReadUploadedPositions(device, renderer, MeshPath))
            .IsEquivalentTo(SimulatedPoints);
        await Assert.That(ReadUploadedPositions(device, renderer, SecondMeshPath))
            .IsEquivalentTo(SimulatedPoints);

        // The second body stops publishing geometry while the first keeps deforming.
        StageDeformation(stage, SimulatedPoints);
        _ = ViewerSilkPhysicsOverrideApplier.ApplyDeformations(stage, deformations, renderer);
        using (OpenUsdSilkPage empty = CreateFramePage(revision: 3))
        {
            _ = renderer.ApplyAndRender(empty, color, depth);
        }

        await Assert.That(ReadUploadedPositions(device, renderer, MeshPath))
            .IsEquivalentTo(SimulatedPoints);
        await Assert.That(ReadUploadedPositions(device, renderer, SecondMeshPath))
            .IsEquivalentTo(AuthoredPoints);
        await Assert.That(deformations.Count).IsEqualTo(1);
        await Assert.That(deformations.RestoredMeshes).IsEqualTo(1);
    }

    [Test]
    public async Task ADeformedMeshIsShadedWithRecomputedNormalsAndRestoresTheAuthoredOnes()
    {
        using var device = new RecordingSilkDevice();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(64, 64));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(64, 64));

        // The authored mesh is flat and authors +Y normals for it. The deformation bends it out of
        // that plane, so an authored normal is no longer the normal of any face it belongs to.
        using (OpenUsdSilkPage authored = CreateMeshPage(revision: 1, AuthoredPoints, AuthoredNormals))
        {
            _ = renderer.ApplyAndRender(authored, color, depth);
        }

        await Assert.That(ReadUploadedNormals(device, renderer)).IsEquivalentTo(AuthoredNormals);

        var stage = new ViewerPhysicsOverrideStage();
        var deformations = new SilkPhysicsDeformations();
        StageDeformation(stage, BentPoints);
        _ = ViewerSilkPhysicsOverrideApplier.ApplyDeformations(stage, deformations, renderer);
        using (OpenUsdSilkPage empty = CreateFramePage(revision: 2))
        {
            _ = renderer.ApplyAndRender(empty, color, depth);
        }

        float[] deformedNormals = ReadUploadedNormals(device, renderer);
        await Assert.That(ReadUploadedPositions(device, renderer)).IsEquivalentTo(BentPoints);

        // Every vertex normal must be the one the bent topology produces, not the authored rest
        // normal that would keep shading the cloth as if it were still flat.
        float[] expected = ComputeFaceNormal(BentPoints);
        for (int vertex = 0; vertex < 3; vertex++)
        {
            for (int component = 0; component < 3; component++)
            {
                await Assert.That((double)deformedNormals[(vertex * 3) + component])
                    .IsEqualTo(expected[component])
                    .Within(1e-4);
            }
        }

        stage.ClearDeformations();
        _ = ViewerSilkPhysicsOverrideApplier.ApplyDeformations(stage, deformations, renderer);
        using (OpenUsdSilkPage empty = CreateFramePage(revision: 3))
        {
            _ = renderer.ApplyAndRender(empty, color, depth);
        }

        await Assert.That(ReadUploadedPositions(device, renderer)).IsEquivalentTo(AuthoredPoints);
        await Assert.That(ReadUploadedNormals(device, renderer)).IsEquivalentTo(AuthoredNormals);
    }

    private static PhysicsRenderObjectId Identity =>
        new(0xC10741, PhysicsRenderObjectKind.Deformable);

    private static PhysicsRenderObjectId SecondIdentity =>
        new(0xC10742, PhysicsRenderObjectKind.Deformable);

    private static PhysicsRenderBindingTable CreateBindings()
    {
        var bindings = new PhysicsRenderBindingTable(4);
        _ = bindings.TryBind(Identity, MeshPath);
        _ = bindings.TryBind(SecondIdentity, SecondMeshPath);
        return bindings;
    }

    private static void StageDeformation(ViewerPhysicsOverrideStage stage, float[] vertices)
    {
        PhysicsRenderBindingTable bindings = CreateBindings();

        // The transform half publishes the bindings the deformable half resolves against, exactly
        // as the render bridge does for a frame that carries both.
        _ = stage.Stage(PhysicsRenderOverrideView.Empty, bindings);
        _ = stage.StageDeformations(
            new PhysicsRenderDeformationView(
                new PhysicsRenderDeformableRegion[]
                {
                    new(Identity, PhysicsRenderDomain.Cloth, 0, vertices.Length / 3, 11)
                },
                vertices,
                revision: 9));
    }

    private static void StageTwoDeformations(ViewerPhysicsOverrideStage stage, float lift = 0f)
    {
        PhysicsRenderBindingTable bindings = CreateBindings();
        _ = stage.Stage(PhysicsRenderOverrideView.Empty, bindings);
        int pointCount = SimulatedPoints.Length / 3;
        var vertices = new float[SimulatedPoints.Length * 2];
        SimulatedPoints.CopyTo(vertices, 0);
        SimulatedPoints.CopyTo(vertices, SimulatedPoints.Length);
        if (lift != 0f)
        {
            // One component per mesh moves, so every frame is geometry no earlier frame produced.
            for (int index = 1; index < vertices.Length; index += 3)
            {
                vertices[index] += lift;
            }
        }
        _ = stage.StageDeformations(
            new PhysicsRenderDeformationView(
                new PhysicsRenderDeformableRegion[]
                {
                    new(Identity, PhysicsRenderDomain.Cloth, 0, pointCount, 11),
                    new(SecondIdentity, PhysicsRenderDomain.Cloth, pointCount, pointCount, 12)
                },
                vertices,
                revision: 9));
    }

    private static ulong MeshId(SilkMeshRenderer renderer) =>
        renderer.Scene.MeshesByPath[(MeshPath, 0)].Id;

    private static float[] ReadScenePoints(SilkMeshRenderer renderer) =>
        renderer.Scene.MeshesByPath[(MeshPath, 0)].Points.ToArray();

    /// <summary>Reads the position components out of the vertex buffer the device holds.</summary>
    private static float[] ReadUploadedPositions(
        RecordingSilkDevice device,
        SilkMeshRenderer renderer,
        string path = MeshPath) =>
        ReadVertexComponents(device, renderer, path, componentOffset: 0);

    /// <summary>Reads the normal components out of the vertex buffer the device holds.</summary>
    private static float[] ReadUploadedNormals(
        RecordingSilkDevice device,
        SilkMeshRenderer renderer,
        string path = MeshPath) =>
        ReadVertexComponents(device, renderer, path, componentOffset: 3);

    private static float[] ReadVertexComponents(
        RecordingSilkDevice device,
        SilkMeshRenderer renderer,
        string path,
        int componentOffset)
    {
        SilkMeshData mesh = renderer.Scene.MeshesByPath[(path, 0)];
        SilkMeshGpuResource resource = renderer.GpuResources.Meshes[mesh.Id];
        RecordingSilkBuffer buffer = device.Track(resource.VertexBuffer);
        int pointCount = mesh.Points.Length / 3;
        var components = new float[pointCount * 3];
        ReadOnlySpan<float> floats = MemoryMarshal.Cast<byte, float>(buffer.Data);
        for (int point = 0; point < pointCount; point++)
        {
            int source = (point * StrideFloats) + componentOffset;
            components[point * 3] = floats[source];
            components[(point * 3) + 1] = floats[source + 1];
            components[(point * 3) + 2] = floats[source + 2];
        }

        return components;
    }

    /// <summary>The unit face normal of one triangle, which is what the builder recomputes.</summary>
    private static float[] ComputeFaceNormal(float[] points)
    {
        double ax = points[3] - points[0];
        double ay = points[4] - points[1];
        double az = points[5] - points[2];
        double bx = points[6] - points[0];
        double by = points[7] - points[1];
        double bz = points[8] - points[2];
        double nx = (ay * bz) - (az * by);
        double ny = (az * bx) - (ax * bz);
        double nz = (ax * by) - (ay * bx);
        double length = Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
        return [(float)(nx / length), (float)(ny / length), (float)(nz / length)];
    }

    private static OpenUsdSilkPage CreateMeshPage(
        ulong revision,
        float[]? points = null,
        float[]? normals = null) =>
        CreatePage(
            revision,
            Concat(
                CreateFrameCommand(),
                CreateMeshCommand(MeshPath, 7, points ?? AuthoredPoints, normals)),
            commandCount: 2);

    private static OpenUsdSilkPage CreateTwoMeshPage(ulong revision) =>
        CreatePage(
            revision,
            Concat(
                CreateFrameCommand(),
                CreateMeshCommand(MeshPath, 7, AuthoredPoints, normals: null),
                CreateMeshCommand(SecondMeshPath, 8, AuthoredPoints, normals: null)),
            commandCount: 3);

    private static OpenUsdSilkPage CreateFramePage(ulong revision) =>
        CreatePage(revision, CreateFrameCommand(), commandCount: 1);

    private static OpenUsdSilkPage CreatePage(ulong revision, byte[] data, uint commandCount)
    {
        ConstructorInfo constructor = typeof(OpenUsdSilkPage).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(uint), typeof(ulong), typeof(byte[]), typeof(uint)],
            modifiers: null) ??
            throw new InvalidOperationException("The managed Silk page constructor is unavailable.");
        return (OpenUsdSilkPage)constructor.Invoke(
            [SilkCommandParser.PageAbiVersion, revision, data, commandCount]);
    }

    private static byte[] Concat(params byte[][] commands)
    {
        var bytes = new byte[commands.Sum(command => command.Length)];
        int cursor = 0;
        foreach (byte[] command in commands)
        {
            command.CopyTo(bytes, cursor);
            cursor += command.Length;
        }

        return bytes;
    }

    private static byte[] CreateFrameCommand()
    {
        var bytes = new byte[272];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 64);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 64);
        for (int index = 0; index < 16; index++)
        {
            double value = index % 5 == 0 ? 1 : 0;
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(16 + (index * sizeof(double))),
                value);
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(144 + (index * sizeof(double))),
                value);
        }

        return bytes;
    }

    private static byte[] CreateMeshCommand(
        string meshPath,
        int primId,
        float[] points,
        float[]? normals)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(meshPath);
        uint[] indices = [0, 1, 2];
        int attributeBytes = normals is null ? 0 : 20 + (normals.Length * sizeof(float));
        int size = 268 +
            pathBytes.Length +
            (points.Length * sizeof(float)) +
            (indices.Length * sizeof(uint)) +
            sizeof(uint) +
            attributeBytes;
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), ComputeStableHash(pathBytes));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), primId);
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
        for (int index = 0; index < 4; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(64 + (index * 4)), 1);
        }
        for (int index = 0; index < 16; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(80 + (index * 8)),
                index % 5 == 0 ? 1 : 0);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(220), normals is null ? 0u : 1u);

        int cursor = 268;
        pathBytes.CopyTo(bytes.AsSpan(cursor));
        cursor += pathBytes.Length;
        foreach (float value in points)
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
        cursor += sizeof(uint);
        if (normals is null)
        {
            return bytes;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(cursor),
            (uint)SilkAttributeSemantic.Normal);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor + 4), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(cursor + 8),
            (uint)SilkAttributeInterpolation.Vertex);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor + 12), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(cursor + 16),
            (uint)(normals.Length / 3));
        for (int index = 0; index < normals.Length; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(cursor + 20 + (index * sizeof(float))),
                normals[index]);
        }

        return bytes;
    }
    /// <summary>The wire-format path hash the parser keys retained meshes by.</summary>
    private static ulong ComputeStableHash(ReadOnlySpan<byte> path)
    {
        ulong hash = 14695981039346656037;
        foreach (byte value in path)
        {
            hash ^= value;
            hash *= 1099511628211;
        }

        return hash;
    }

    /// <summary>A device that keeps every byte written to every buffer it created.</summary>
    private sealed class RecordingSilkDevice : ISilkGraphicsDevice
    {
        private readonly List<RecordingSilkBuffer> _buffers = [];

        public SilkGraphicsBackend Backend => SilkGraphicsBackend.Vulkan;

        public SilkGraphicsCapabilities Capabilities { get; } =
            new("Viewer test", "1", SupportsCompute: true, IsSoftware: true);

        internal RecordingSilkBuffer Track(ISilkGraphicsBuffer buffer)
        {
            foreach (RecordingSilkBuffer candidate in _buffers)
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
            var buffer = new RecordingSilkBuffer(size, usage);
            _buffers.Add(buffer);
            return buffer;
        }

        public ISilkGraphicsTexture CreateTexture2D(
            uint width,
            uint height,
            SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm) =>
            new RecordingSilkTexture(
                new SilkTextureDescriptor(
                    width,
                    height,
                    format,
                    SilkTextureDescriptor.GetDefaultUsage(format)));

        public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor) =>
            new RecordingSilkTexture(descriptor);

        public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor) =>
            new RecordingSilkSampler(descriptor);

        public ISilkGraphicsShaderModule CreateShaderModule(SilkShaderModuleDescriptor descriptor) =>
            new RecordingSilkShaderModule(descriptor);

        public ISilkGraphicsBindingLayout CreateBindingLayout(
            SilkBindingLayoutDescriptor descriptor) =>
            new RecordingSilkBindingLayout(descriptor);

        public ISilkGraphicsShaderProgram CreateShaderProgram(
            SilkShaderProgramDescriptor descriptor) =>
            new RecordingSilkShaderProgram(descriptor.BindingLayout);

        public ISilkGraphicsPipeline CreateGraphicsPipeline(
            SilkGraphicsPipelineDescriptor descriptor) =>
            new RecordingSilkPipeline(descriptor);

        public ISilkComputeBindingLayout CreateComputeBindingLayout(
            SilkComputeBindingLayoutDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputeShaderProgram CreateComputeShaderProgram(
            SilkComputeShaderProgramDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputePipeline CreateComputePipeline(
            SilkComputePipelineDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsCommandList CreateCommandList() => new RecordingSilkCommandList();

        public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList) =>
            new RecordingSilkSubmission();

        public void WaitIdle()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingSilkBuffer(nuint size, SilkBufferUsage usage)
        : SilkGraphicsBufferBase(size, usage)
    {
        internal byte[] Data { get; } = new byte[checked((int)size)];

        public override void Write(ReadOnlySpan<byte> data, nuint offset = 0)
        {
            _ = ValidateWrite(data.Length, offset);
            data.CopyTo(Data.AsSpan(checked((int)offset)));
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

    private sealed class RecordingSilkTexture(SilkTextureDescriptor descriptor)
        : SilkGraphicsTextureBase(descriptor)
    {
        public override void ReadbackForTesting(Span<byte> destination) => destination.Clear();

        public override void ReadbackForTesting(Span<float> destination) => destination.Clear();

        protected override void ReleaseNative()
        {
        }
    }

    private sealed class RecordingSilkSampler(SilkSamplerDescriptor descriptor)
        : ISilkGraphicsSampler
    {
        public SilkSamplerDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingSilkShaderModule(SilkShaderModuleDescriptor descriptor)
        : ISilkGraphicsShaderModule
    {
        public SilkShaderModuleDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingSilkBindingLayout(SilkBindingLayoutDescriptor descriptor)
        : ISilkGraphicsBindingLayout
    {
        public SilkBindingLayoutDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingSilkShaderProgram(ISilkGraphicsBindingLayout bindingLayout)
        : ISilkGraphicsShaderProgram
    {
        public ISilkGraphicsBindingLayout BindingLayout { get; } = bindingLayout;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingSilkPipeline(SilkGraphicsPipelineDescriptor descriptor)
        : ISilkGraphicsPipeline
    {
        public SilkGraphicsPipelineDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingSilkCommandList : ISilkGraphicsCommandList
    {
        public void UploadTexture(ISilkGraphicsTexture texture, ReadOnlySpan<byte> source)
        {
        }

        public void ClearColor(ISilkGraphicsTexture texture, SilkColor color)
        {
        }

        public void ClearDepth(ISilkGraphicsTexture texture, float depth)
        {
        }

        public void BeginRendering(SilkRenderingDescriptor descriptor)
        {
        }

        public void SetGraphicsPipeline(ISilkGraphicsPipeline pipeline)
        {
        }

        public void SetViewport(SilkViewport viewport)
        {
        }

        public void SetScissor(SilkScissor scissor)
        {
        }

        public void SetVertexBuffer(ISilkGraphicsBuffer buffer)
        {
        }

        public void SetIndexBuffer(ISilkGraphicsBuffer buffer)
        {
        }

        public void SetUniformBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer)
        {
        }

        public void SetTexture(uint setIndex, uint binding, ISilkGraphicsTexture texture)
        {
        }

        public void SetSampler(uint setIndex, uint binding, ISilkGraphicsSampler sampler)
        {
        }

        public void DrawIndexed(uint indexCount)
        {
        }

        public void DrawIndexedInstanced(uint indexCount, uint instanceCount)
        {
        }

        public void EndRendering()
        {
        }

        public void SetComputePipeline(ISilkComputePipeline pipeline)
        {
        }

        public void SetStorageBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer)
        {
        }

        public void SetComputeUniformBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer)
        {
        }

        public void Dispatch(uint elementCount)
        {
        }

        public void BufferBarrier(ISilkGraphicsBuffer buffer)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingSilkSubmission : ISilkGraphicsSubmission
    {
        public bool IsCompleted => true;

        public void Wait()
        {
        }

        public void Dispose()
        {
        }
    }
}
