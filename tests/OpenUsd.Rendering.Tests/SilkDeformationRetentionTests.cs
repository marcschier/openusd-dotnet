// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Drives a long deformation run through a real renderer and requires everything it retains to stay
/// bounded.
/// </summary>
/// <remarks>
/// <para>
/// Deformable geometry is the one input that produces a new GPU geometry payload on almost every
/// frame, and the renderer's draw-batch table is keyed by that payload's reference identity. The
/// table used to clear its lists per frame but never its keys, so a deformed scene added one
/// permanent key per frame, kept the disposed geometry it named alive with it, and lengthened the
/// per-frame sweep that clears the table. Nothing failed; the process simply grew for as long as the
/// simulation ran.
/// </para>
/// <para>
/// The run is long on purpose. One frame proves nothing about a leak, and a handful of frames stays
/// inside the noise of pooled storage, so this drives well over a thousand distinct deformation
/// frames and compares what the renderer holds at the end against what it held at the start.
/// </para>
/// </remarks>
public sealed class SilkDeformationRetentionTests
{
    private const string FirstPath = "/World/Cloth";
    private const string SecondPath = "/World/Cloth2";
    private const int StrideFloats = 6;

    private static readonly float[] AuthoredPoints = [0, 0, 0, 1, 0, 0, 1, 0, 1];

    [Test]
    public async Task ALongDeformationRunKeepsBatchesGeometryAndRetainedPointsBounded()
    {
        using var device = new RetentionGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(64, 64));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(64, 64));

        // Two meshes, because a one-mesh scene takes the renderer's ungrouped fast path and never
        // touches the batch table this guards.
        using (OpenUsdSilkPage authored = CreateTwoMeshPage(revision: 1))
        {
            _ = renderer.ApplyAndRender(authored, color, depth);
        }

        var bindings = new PhysicsRenderBindingTable(4);
        _ = bindings.TryBind(FirstIdentity, FirstPath);
        _ = bindings.TryBind(SecondIdentity, SecondPath);
        var deformations = new SilkPhysicsDeformations();

        const int frames = 1500;
        int steadyBatchKeys = 0;
        int steadyGeometry = 0;
        int steadyPointBytes = 0;
        int steadyLiveBuffers = 0;
        WeakReference? earlyGeometry = null;

        for (int frame = 0; frame < frames; frame++)
        {
            // The staged batch is handed over exactly as the viewer's applier hands it over: retain
            // it, let the renderer apply it after the page, and let the renderer upload the delta.
            _ = deformations.Stage(bindings, View(0.001f * (frame + 1)));
            renderer.PhysicsDeformations = deformations;
            using OpenUsdSilkPage empty = CreateFramePage((ulong)frame + 2);
            _ = renderer.ApplyAndRender(empty, color, depth);

            if (frame == 2)
            {
                steadyBatchKeys = renderer.BatchKeyCount;
                steadyGeometry = renderer.GpuResources.GeometryResourceCount;
                steadyPointBytes = RetainedPointBytes(renderer);
                steadyLiveBuffers = device.LiveBufferCount;
                earlyGeometry = SampleGeometry(renderer, FirstPath);
            }
        }

        // Nothing accumulates: two meshes deformed for 1500 distinct frames hold the same number of
        // batch keys, geometry payloads, and retained CPU points as the third frame did.
        await Assert.That(steadyBatchKeys).IsGreaterThan(0);
        await Assert.That(renderer.BatchKeyCount).IsEqualTo(steadyBatchKeys);
        await Assert.That(renderer.PooledBatchCount).IsEqualTo(0);
        await Assert.That(renderer.GpuResources.GeometryResourceCount).IsEqualTo(steadyGeometry);
        await Assert.That(RetainedPointBytes(renderer)).IsEqualTo(steadyPointBytes);
        await Assert.That(renderer.Scene.MeshesByPath.Count).IsEqualTo(2);
        await Assert.That(deformations.Count).IsEqualTo(2);

        // Buffer churn is expected - new points mean a new payload - but the number of buffers
        // that are still alive must not follow it. A leak keeps every one of them.
        await Assert.That(device.LiveBufferCount).IsLessThanOrEqualTo(steadyLiveBuffers);

        // The geometry of an early frame is released, disposed, and unreachable. A batch key that
        // still named it would keep it alive for the life of the renderer.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        await Assert.That(earlyGeometry!.IsAlive).IsFalse();

        // Stopping after a long run still restores the authored geometry, and still leaves nothing
        // growing behind it.
        _ = deformations.Stage(bindings, PhysicsRenderDeformationView.Empty);
        using (OpenUsdSilkPage empty = CreateFramePage(frames + 2))
        {
            _ = renderer.ApplyAndRender(empty, color, depth);
        }

        await Assert.That(ReadPositions(device, renderer, FirstPath)).IsEquivalentTo(AuthoredPoints);
        await Assert.That(ReadPositions(device, renderer, SecondPath))
            .IsEquivalentTo(AuthoredPoints);
        await Assert.That(deformations.Count).IsEqualTo(0);
        await Assert.That(deformations.RestoredMeshes).IsEqualTo(2);
        await Assert.That(renderer.BatchKeyCount).IsLessThanOrEqualTo(steadyBatchKeys);
        await Assert.That(renderer.GpuResources.GeometryResourceCount)
            .IsLessThanOrEqualTo(steadyGeometry);
        await Assert.That(RetainedPointBytes(renderer)).IsEqualTo(steadyPointBytes);
    }

    [Test]
    public async Task AFrameThatShrinksToTheFastPathReleasesTheBatchTable()
    {
        using var device = new RetentionGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(64, 64));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(64, 64));

        // A wide frame first, so the table holds a key - and a geometry resource - per distinct
        // batch. Each mesh authors its own points, so none of them share a payload.
        using (OpenUsdSilkPage authored = CreateWidePage(revision: 1, meshes: 8))
        {
            _ = renderer.ApplyAndRender(authored, color, depth);
        }

        int wideBatchKeys = renderer.BatchKeyCount;
        await Assert.That(wideBatchKeys).IsGreaterThanOrEqualTo(8);
        WeakReference wideGeometry = SampleGeometry(renderer, WidePath(3));
        int liveBuffersWhileWide = device.LiveBufferCount;

        // The scene shrinks to one mesh, which takes the renderer's ungrouped fast path. That path
        // never groups anything, so unless the table is released before the branch it keeps every
        // key - and every geometry resource those keys name - for the life of the renderer.
        using (OpenUsdSilkPage narrowed = CreateRemovalPage(revision: 2, keep: 1, previous: 8))
        {
            _ = renderer.ApplyAndRender(narrowed, color, depth);
        }

        await Assert.That(renderer.Scene.MeshesByPath.Count).IsEqualTo(1);
        await Assert.That(renderer.BatchKeyCount).IsEqualTo(0);
        await Assert.That(renderer.PooledBatchCount).IsLessThanOrEqualTo(wideBatchKeys);
        await Assert.That(renderer.GpuResources.GeometryResourceCount).IsEqualTo(1);
        await Assert.That(device.LiveBufferCount).IsLessThan(liveBuffersWhileWide);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        await Assert.That(wideGeometry.IsAlive).IsFalse();

        // And an empty scene, which draws through neither path, still leaves nothing retained.
        using (OpenUsdSilkPage emptied = CreateRemovalPage(revision: 3, keep: 0, previous: 1))
        {
            _ = renderer.ApplyAndRender(emptied, color, depth);
        }

        await Assert.That(renderer.Scene.MeshesByPath.Count).IsEqualTo(0);
        await Assert.That(renderer.BatchKeyCount).IsEqualTo(0);
        await Assert.That(renderer.PooledBatchCount).IsLessThanOrEqualTo(wideBatchKeys);
        await Assert.That(renderer.GpuResources.GeometryResourceCount).IsEqualTo(0);
    }

    [Test]
    public async Task DisposingTheRendererDropsEveryRetainedBatchKey()
    {
        using var device = new RetentionGraphicsDevice();
        var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(64, 64));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(64, 64));
        using (OpenUsdSilkPage authored = CreateTwoMeshPage(revision: 1))
        {
            _ = renderer.ApplyAndRender(authored, color, depth);
        }

        await Assert.That(renderer.BatchKeyCount).IsGreaterThan(0);

        renderer.Dispose();

        // The GPU scene disposed its geometry, so a table still naming it would be holding disposed
        // resources for the lifetime of the object graph.
        await Assert.That(renderer.BatchKeyCount).IsEqualTo(0);
        await Assert.That(renderer.PooledBatchCount).IsEqualTo(0);
    }

    [Test]
    public async Task ADeformedMeshRepublishesItsNormalsAndKeepsRetentionBounded()
    {
        using var device = new RetentionGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(64, 64));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(64, 64));

        // The bind pose. The authored normal points along +Z while the triangle lies in the XZ
        // plane, so a topology-derived normal would point along Y: nothing here can pass by
        // accident from recomputed normals.
        float[] bindNormals = RepeatNormal(0, 0, 1);
        using (OpenUsdSilkPage bindPose = CreateDeformedPage(revision: 1, AuthoredPoints, bindNormals))
        {
            _ = renderer.ApplyAndRender(bindPose, color, depth);
        }

        await Assert.That(ReadNormals(device, renderer, FirstPath)).IsEquivalentTo(bindNormals);

        const int frames = 600;
        int steadyBatchKeys = 0;
        int steadyGeometry = 0;
        int steadyLiveBuffers = 0;
        WeakReference? earlyGeometry = null;
        float[] lastNormals = bindNormals;

        // Every frame republishes both arrays, exactly as a CPU-resolved UsdSkel mesh does: the
        // points and the normals of one time code always travel together.
        for (int frame = 0; frame < frames; frame++)
        {
            double angle = (frame + 1) * 0.001;
            float[] points = LiftPoints((float)angle);
            lastNormals = RepeatNormal(0, (float)Math.Sin(angle), (float)Math.Cos(angle));
            using (OpenUsdSilkPage deformed =
                CreateDeformedPage((ulong)frame + 2, points, lastNormals))
            {
                _ = renderer.ApplyAndRender(deformed, color, depth);
            }

            if (frame == 2)
            {
                steadyBatchKeys = renderer.BatchKeyCount;
                steadyGeometry = renderer.GpuResources.GeometryResourceCount;
                steadyLiveBuffers = device.LiveBufferCount;
                earlyGeometry = SampleGeometry(renderer, FirstPath);
            }
        }

        // The last published normal is the one on the GPU. A renderer that kept the bind pose, or
        // that recomputed normals from the deformed points, fails here rather than only looking
        // wrong.
        await Assert.That(ReadNormals(device, renderer, FirstPath)).IsEquivalentTo(lastNormals);
        await Assert.That(renderer.GpuResources.GeometryResourceCount).IsEqualTo(steadyGeometry);
        await Assert.That(renderer.BatchKeyCount).IsEqualTo(steadyBatchKeys);
        await Assert.That(device.LiveBufferCount).IsLessThanOrEqualTo(steadyLiveBuffers);
        await Assert.That(renderer.Scene.MeshesByPath.Count).IsEqualTo(2);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        await Assert.That(earlyGeometry!.IsAlive).IsFalse();

        // Scrubbing back to the bind pose restores the bind-pose normals: invalidation has to run
        // in both directions, or a scrub backwards leaves the deformed shading behind.
        using (OpenUsdSilkPage restored =
            CreateDeformedPage(frames + 2, AuthoredPoints, bindNormals))
        {
            _ = renderer.ApplyAndRender(restored, color, depth);
        }

        await Assert.That(ReadNormals(device, renderer, FirstPath)).IsEquivalentTo(bindNormals);
        await Assert.That(ReadPositions(device, renderer, FirstPath)).IsEquivalentTo(AuthoredPoints);
        await Assert.That(renderer.GpuResources.GeometryResourceCount)
            .IsLessThanOrEqualTo(steadyGeometry);
    }

    [Test]
    public async Task ADeformedMeshWithoutPublishedNormalsStillShadesFromItsDeformedPoints()
    {
        using var device = new RetentionGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(64, 64));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(64, 64));

        // The delegate omits normals it cannot deform rather than publishing the bind pose, so the
        // consumer must derive them from the points it did receive. The authored triangle lies in
        // the XZ plane and the deformed one is tilted, so the two derived normals differ.
        using (OpenUsdSilkPage bindPose = CreateDeformedPage(revision: 1, AuthoredPoints, []))
        {
            _ = renderer.ApplyAndRender(bindPose, color, depth);
        }

        float[] bindDerived = ReadNormals(device, renderer, FirstPath);
        await Assert.That(Math.Abs(bindDerived[1])).IsGreaterThan(0.99f);

        float[] tilted = [0, 0, 0, 1, 0, 0, 1, 1, 1];
        using (OpenUsdSilkPage deformed = CreateDeformedPage(revision: 2, tilted, []))
        {
            _ = renderer.ApplyAndRender(deformed, color, depth);
        }

        float[] deformedDerived = ReadNormals(device, renderer, FirstPath);
        await Assert.That(Math.Abs(deformedDerived[1])).IsLessThan(0.99f);
    }

    private static PhysicsRenderObjectId FirstIdentity =>
        new(0xB0A701, PhysicsRenderObjectKind.Deformable);

    private static PhysicsRenderObjectId SecondIdentity =>
        new(0xB0A702, PhysicsRenderObjectKind.Deformable);

    /// <summary>One batch that lifts both meshes by an amount no other frame used.</summary>
    private static PhysicsRenderDeformationView View(float lift)
    {
        int pointCount = AuthoredPoints.Length / 3;
        var vertices = new float[AuthoredPoints.Length * 2];
        AuthoredPoints.CopyTo(vertices, 0);
        AuthoredPoints.CopyTo(vertices, AuthoredPoints.Length);
        for (int index = 1; index < vertices.Length; index += 3)
        {
            vertices[index] += lift;
        }

        return new PhysicsRenderDeformationView(
            new PhysicsRenderDeformableRegion[]
            {
                new(FirstIdentity, PhysicsRenderDomain.Cloth, 0, pointCount, 11),
                new(SecondIdentity, PhysicsRenderDomain.Cloth, pointCount, pointCount, 12)
            },
            vertices,
            revision: 9);
    }

    /// <summary>
    /// Samples one frame's geometry payload in a frame of its own.
    /// </summary>
    /// <remarks>
    /// The temporary that reads the resource must not stay in the calling method's stack frame,
    /// because a stack slot the JIT has not reused is a root and would keep the object alive no
    /// matter how correct the renderer is.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference SampleGeometry(SilkMeshRenderer renderer, string path) =>
        new(renderer.GpuResources.Meshes[MeshId(renderer, path)].Geometry);

    private static ulong MeshId(SilkMeshRenderer renderer, string path) =>
        renderer.Scene.MeshesByPath[(path, 0)].Id;

    private static int RetainedPointBytes(SilkMeshRenderer renderer)
    {
        int bytes = 0;
        foreach (SilkMeshData mesh in renderer.Scene.MeshesByPath.Values)
        {
            bytes += mesh.Points.Length * sizeof(float);
        }

        return bytes;
    }

    private static float[] ReadPositions(
        RetentionGraphicsDevice device,
        SilkMeshRenderer renderer,
        string path)
    {
        SilkMeshData mesh = renderer.Scene.MeshesByPath[(path, 0)];
        SilkMeshGpuResource resource = renderer.GpuResources.Meshes[mesh.Id];
        RetentionGraphicsBuffer buffer = device.Track(resource.VertexBuffer);
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

    /// <summary>Reads the interleaved normals the renderer actually uploaded.</summary>
    private static float[] ReadNormals(
        RetentionGraphicsDevice device,
        SilkMeshRenderer renderer,
        string path)
    {
        SilkMeshData mesh = renderer.Scene.MeshesByPath[(path, 0)];
        SilkMeshGpuResource resource = renderer.GpuResources.Meshes[mesh.Id];
        RetentionGraphicsBuffer buffer = device.Track(resource.VertexBuffer);
        int pointCount = mesh.Points.Length / 3;
        var normals = new float[pointCount * 3];
        ReadOnlySpan<float> floats = MemoryMarshal.Cast<byte, float>(buffer.Data);
        for (int point = 0; point < pointCount; point++)
        {
            int source = (point * StrideFloats) + 3;
            normals[point * 3] = floats[source];
            normals[(point * 3) + 1] = floats[source + 1];
            normals[(point * 3) + 2] = floats[source + 2];
        }

        return normals;
    }

    /// <summary>One per-point normal repeated across the authored triangle.</summary>
    private static float[] RepeatNormal(float x, float y, float z)
    {
        int pointCount = AuthoredPoints.Length / 3;
        var normals = new float[pointCount * 3];
        for (int point = 0; point < pointCount; point++)
        {
            normals[point * 3] = x;
            normals[(point * 3) + 1] = y;
            normals[(point * 3) + 2] = z;
        }

        return normals;
    }

    private static float[] LiftPoints(float lift)
    {
        float[] points = (float[])AuthoredPoints.Clone();
        for (int index = 1; index < points.Length; index += 3)
        {
            points[index] += lift;
        }

        return points;
    }

    /// <summary>
    /// One deformed frame: a mesh that republishes points and, when the delegate resolved them,
    /// per-point normals, plus a second mesh so the renderer keeps using its batch table.
    /// </summary>
    private static OpenUsdSilkPage CreateDeformedPage(
        ulong revision,
        float[] points,
        float[] normals) =>
        CreatePage(
            revision,
            Concat(
                CreateFrameCommand(),
                CreateMeshCommand(FirstPath, 7, points, normals),
                CreateMeshCommand(SecondPath, 8, AuthoredPoints, [])),
            commandCount: 3);

    /// <summary>The path of one mesh in the wide scene.</summary>
    private static string WidePath(int index) => $"/World/Wide{index}";

    /// <summary>A frame plus several meshes, each with points of its own.</summary>
    private static OpenUsdSilkPage CreateWidePage(ulong revision, int meshes)
    {
        var commands = new byte[meshes + 1][];
        commands[0] = CreateFrameCommand();
        for (int index = 0; index < meshes; index++)
        {
            float[] points =
            [
                0, index, 0,
                1, index, 0,
                1, index, 1,
            ];
            commands[index + 1] = CreateMeshCommand(WidePath(index), 100 + index, points);
        }

        return CreatePage(revision, Concat(commands), (uint)commands.Length);
    }

    /// <summary>A frame that removes every wide mesh above the kept count.</summary>
    private static OpenUsdSilkPage CreateRemovalPage(ulong revision, int keep, int previous)
    {
        var commands = new byte[previous - keep + 1][];
        commands[0] = CreateFrameCommand();
        for (int index = keep; index < previous; index++)
        {
            commands[index - keep + 1] = CreateMeshRemoveCommand(WidePath(index));
        }

        return CreatePage(revision, Concat(commands), (uint)commands.Length);
    }

    private static byte[] CreateMeshRemoveCommand(string meshPath)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(meshPath);
        var bytes = new byte[24 + pathBytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshRemove);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkWireFormat.ComputeStableHash(meshPath));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), (uint)pathBytes.Length);
        pathBytes.CopyTo(bytes.AsSpan(24));
        return bytes;
    }

    private static OpenUsdSilkPage CreateTwoMeshPage(ulong revision) =>
        CreatePage(
            revision,
            Concat(
                CreateFrameCommand(),
                CreateMeshCommand(FirstPath, 7),
                CreateMeshCommand(SecondPath, 8)),
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

    private static byte[] CreateMeshCommand(string meshPath, int primId) =>
        CreateMeshCommand(meshPath, primId, AuthoredPoints);

    private static byte[] CreateMeshCommand(string meshPath, int primId, float[] points) =>
        CreateMeshCommand(meshPath, primId, points, []);

    private static byte[] CreateMeshCommand(
        string meshPath,
        int primId,
        float[] points,
        float[] normals)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(meshPath);
        uint[] indices = [0, 1, 2];
        byte[] normalName = Encoding.UTF8.GetBytes("normals");
        int attributeCount = normals.Length == 0 ? 0 : 1;
        int attributeBytes = attributeCount == 0
            ? 0
            : (5 * sizeof(uint)) + normalName.Length + (normals.Length * sizeof(float));
        int size = 268 +
            pathBytes.Length +
            (points.Length * sizeof(float)) +
            (indices.Length * sizeof(uint)) +
            sizeof(uint) +
            attributeBytes;
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkWireFormat.ComputeStableHash(meshPath));
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

        int cursor = 268;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(220), (uint)attributeCount);
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
        if (attributeCount != 0)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(cursor),
                (uint)SilkAttributeSemantic.Normal);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor + 4), 3);
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(cursor + 8),
                (uint)SilkAttributeInterpolation.Vertex);
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(cursor + 12),
                (uint)normalName.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(cursor + 16),
                (uint)(normals.Length / 3));
            cursor += 5 * sizeof(uint);
            normalName.CopyTo(bytes.AsSpan(cursor));
            cursor += normalName.Length;
            foreach (float value in normals)
            {
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(cursor), value);
                cursor += sizeof(float);
            }
        }

        return bytes;
    }

    /// <summary>A device that keeps written bytes and counts the buffers it ever created.</summary>
    private sealed class RetentionGraphicsDevice : ISilkGraphicsDevice
    {
        private readonly List<RetentionGraphicsBuffer> _buffers = [];

        internal int CreatedBufferCount { get; private set; }

        internal int DisposedBufferCount { get; private set; }

        internal int LiveBufferCount => CreatedBufferCount - DisposedBufferCount;

        public SilkGraphicsBackend Backend => SilkGraphicsBackend.Vulkan;

        public SilkGraphicsCapabilities Capabilities { get; } =
            new("Retention test", "1", SupportsCompute: true, IsSoftware: true);

        internal RetentionGraphicsBuffer Track(ISilkGraphicsBuffer buffer)
        {
            foreach (RetentionGraphicsBuffer candidate in _buffers)
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
            var buffer = new RetentionGraphicsBuffer(size, usage, () => DisposedBufferCount++);
            CreatedBufferCount++;

            // Only live buffers are tracked, so the tracking list cannot itself become the leak
            // this test is looking for.
            _buffers.RemoveAll(candidate => candidate.IsDisposed);
            _buffers.Add(buffer);
            return buffer;
        }

        public ISilkGraphicsTexture CreateTexture2D(
            uint width,
            uint height,
            SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm) =>
            new RetentionGraphicsTexture(
                new SilkTextureDescriptor(
                    width,
                    height,
                    format,
                    SilkTextureDescriptor.GetDefaultUsage(format)));

        public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor) =>
            new RetentionGraphicsTexture(descriptor);

        public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor) =>
            new RetentionGraphicsSampler(descriptor);

        public ISilkGraphicsShaderModule CreateShaderModule(SilkShaderModuleDescriptor descriptor) =>
            new RetentionGraphicsShaderModule(descriptor);

        public ISilkGraphicsBindingLayout CreateBindingLayout(
            SilkBindingLayoutDescriptor descriptor) =>
            new RetentionGraphicsBindingLayout(descriptor);

        public ISilkGraphicsShaderProgram CreateShaderProgram(
            SilkShaderProgramDescriptor descriptor) =>
            new RetentionGraphicsShaderProgram(descriptor.BindingLayout);

        public ISilkGraphicsPipeline CreateGraphicsPipeline(
            SilkGraphicsPipelineDescriptor descriptor) =>
            new RetentionGraphicsPipeline(descriptor);

        public ISilkComputeBindingLayout CreateComputeBindingLayout(
            SilkComputeBindingLayoutDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputeShaderProgram CreateComputeShaderProgram(
            SilkComputeShaderProgramDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputePipeline CreateComputePipeline(
            SilkComputePipelineDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsCommandList CreateCommandList() => new RetentionGraphicsCommandList();

        public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList) =>
            new RetentionGraphicsSubmission();

        public void WaitIdle()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class RetentionGraphicsBuffer(
        nuint size,
        SilkBufferUsage usage,
        Action disposed)
        : SilkGraphicsBufferBase(size, usage)
    {
        private readonly Action _disposed = disposed;

        internal byte[] Data { get; } = new byte[checked((int)size)];

        internal bool IsDisposed { get; private set; }

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
            IsDisposed = true;
            _disposed();
        }
    }

    private sealed class RetentionGraphicsTexture(SilkTextureDescriptor descriptor)
        : SilkGraphicsTextureBase(descriptor)
    {
        public override void ReadbackForTesting(Span<byte> destination) => destination.Clear();

        public override void ReadbackForTesting(Span<float> destination) => destination.Clear();

        protected override void ReleaseNative()
        {
        }
    }

    private sealed class RetentionGraphicsSampler(SilkSamplerDescriptor descriptor)
        : ISilkGraphicsSampler
    {
        public SilkSamplerDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class RetentionGraphicsShaderModule(SilkShaderModuleDescriptor descriptor)
        : ISilkGraphicsShaderModule
    {
        public SilkShaderModuleDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class RetentionGraphicsBindingLayout(SilkBindingLayoutDescriptor descriptor)
        : ISilkGraphicsBindingLayout
    {
        public SilkBindingLayoutDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class RetentionGraphicsShaderProgram(ISilkGraphicsBindingLayout bindingLayout)
        : ISilkGraphicsShaderProgram
    {
        public ISilkGraphicsBindingLayout BindingLayout { get; } = bindingLayout;

        public void Dispose()
        {
        }
    }

    private sealed class RetentionGraphicsPipeline(SilkGraphicsPipelineDescriptor descriptor)
        : ISilkGraphicsPipeline
    {
        public SilkGraphicsPipelineDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class RetentionGraphicsCommandList : ISilkGraphicsCommandList
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

    private sealed class RetentionGraphicsSubmission : ISilkGraphicsSubmission
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
