// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkMeshRendererTests
{
    [Test]
    [Arguments(0, false)]
    [Arguments(1, false)]
    [Arguments(2, false)]
    [Arguments(3, false)]
    [Arguments(4, false)]
    [Arguments(5, false)]
    [Arguments(0, true)]
    [Arguments(1, true)]
    [Arguments(2, true)]
    [Arguments(3, true)]
    [Arguments(4, true)]
    [Arguments(5, true)]
    public Task LateBufferRefusalPreservesEveryPreviouslyPublishedMesh(int failure, bool coordinated) =>
        VerifyRefusedPage(failure, coordinated, failWrite: false);

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public Task LateUploadRefusalPreservesTheCompleteCpuAndGpuPage(int failure) =>
        VerifyRefusedPage(failure, coordinated: true, failWrite: true);

    private static async Task VerifyRefusedPage(int failure, bool coordinated, bool failWrite)
    {
        using var device = new TestGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        SilkSceneState scene = renderer.Scene;
        SilkSceneGpuResources resources = renderer.GpuResources;
        byte[] first = CreateMeshCommand([1, 0, 0, 1], pathText: "/First", primId: 7);
        byte[] metadata = CreateMeshCommand([0, 1, 0, 1], pathText: "/Metadata", primId: 8, pointZ: 0.25f);
        byte[] removed = CreateMeshCommand([0, 0, 1, 1], pathText: "/Removed", primId: 9, pointZ: 0.5f);
        byte[] firstFrame = CreateFrameCommand();
        resources.Apply(scene, scene.Apply([.. firstFrame, .. first, .. metadata, .. removed], 4, 1));
        Dictionary<ulong, SilkMeshGpuResource> before = resources.Meshes.ToDictionary();
        Dictionary<ulong, SilkMeshData> beforeMetadata = before.ToDictionary(
            static pair => pair.Key, static pair => pair.Value.Mesh);
        ulong revision = resources.Revision;
        ulong frameRevision = scene.Frame.Revision;
        int live = device.Buffers.Count(static buffer => !buffer.Released);
        byte[] replacement = CreateMeshCommand([1, 1, 0, 1], pathText: "/First", primId: 7, pointZ: 1);
        byte[] tint = CreateMeshCommand([1, 0, 1, 1], pathText: "/Metadata", primId: 8, pointZ: 0.25f);
        byte[] added = CreateMeshCommand([0, 1, 1, 1], pathText: "/Added", primId: 10, pointZ: 2);
        byte[] removal = CreateRemoval("/Removed");
        byte[] movedFrame = CreateFrameCommand();
        BinaryPrimitives.WriteDoubleLittleEndian(movedFrame.AsSpan(16 + 12 * sizeof(double)), 2);
        using var page = new OpenUsdSilkPage(24, 2, [.. movedFrame, .. replacement, .. tint, .. added, .. removal], 5);
        SilkSceneDelta delta = coordinated ? default : scene.Apply(page);
        device.BufferFailureCountdown = failWrite ? null : failure;
        device.WriteFailureCountdown = failWrite ? failure : null;
        Action apply = coordinated ? () => renderer.ApplyPage(page) : () => resources.Apply(scene, delta);

        await Assert.That(apply).Throws<InvalidOperationException>();
        await Assert.That(resources.Revision).IsEqualTo(revision);
        await Assert.That(resources.Meshes.Keys).IsEquivalentTo(before.Keys);
        foreach ((ulong id, SilkMeshGpuResource resource) in before)
        {
            await Assert.That(resources.Meshes[id]).IsSameReferenceAs(resource);
            await Assert.That(resource.Mesh).IsSameReferenceAs(beforeMetadata[id]);
            if (coordinated)
            {
                await Assert.That(scene.Meshes[id]).IsSameReferenceAs(beforeMetadata[id]);
            }
        }
        if (coordinated)
        {
            await Assert.That(scene.Revision).IsEqualTo(1ul);
            await Assert.That(scene.Frame.Revision).IsEqualTo(frameRevision);
            await Assert.That(scene.Meshes.Keys).IsEquivalentTo(before.Keys);
        }
        await Assert.That(device.Buffers.Count(static buffer => !buffer.Released)).IsEqualTo(live);

        device.BufferFailureCountdown = null;
        device.WriteFailureCountdown = null;
        apply();
        await Assert.That(resources.Meshes.Keys).IsEquivalentTo([7ul, 8ul, 10ul]);
        await Assert.That(resources.Meshes[7].Mesh).IsSameReferenceAs(scene.Meshes[7]);
        await Assert.That(resources.Meshes[8].Mesh).IsSameReferenceAs(scene.Meshes[8]);
        await Assert.That(resources.Meshes[10].Mesh).IsSameReferenceAs(scene.Meshes[10]);
        await Assert.That(resources.Revision).IsEqualTo(revision + 1);
        await Assert.That(device.Buffers.Count(static buffer => !buffer.Released)).IsEqualTo(live);
    }

    [Test]
    public async Task PreparedQuietPagesRemainAllocationFreeAndKeepTheSameGpuResources()
    {
        using var device = new TestGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        using var first = new OpenUsdSilkPage(24, 1, [.. CreateFrameCommand(), .. CreateMeshCommand([1, 0, 0, 1])], 2);
        renderer.ApplyPage(first);
        using var quiet = new OpenUsdSilkPage(24, 2, CreateFrameCommand(), 1);
        SilkMeshGpuResource original = renderer.GpuResources.Meshes[7];
        int created = device.Buffers.Count;
        for (int index = 0; index < 1000; index++)
        {
            renderer.ApplyPage(quiet);
        }
        int consecutiveZero = 0;
        for (int pass = 0; pass < 8 && consecutiveZero < 2; pass++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 1000; index++)
            {
                renderer.ApplyPage(quiet);
            }
            consecutiveZero = GC.GetAllocatedBytesForCurrentThread() == before ? consecutiveZero + 1 : 0;
        }
        await Assert.That(consecutiveZero).IsEqualTo(2);
        await Assert.That(device.Buffers.Count).IsEqualTo(created);
        await Assert.That(renderer.GpuResources.Meshes[7]).IsSameReferenceAs(original);
        await Assert.That(renderer.GpuResources.Revision).IsEqualTo(1ul);
    }

    [Test]
    public async Task BuildsInterleavedNormalsAndThirtyTwoBitIndices()
    {
        SilkMeshGeometry geometry = SilkMeshGeometryBuilder.Build(CreateMesh(
            [0, 0, 0, 1, 0, 0, 0, 1, 0],
            [0, 1, 2]));

        await Assert.That(geometry.Vertices.Length).IsEqualTo(18);
        await Assert.That(geometry.Indices).IsEquivalentTo([0u, 1u, 2u]);
        for (int vertex = 0; vertex < 3; vertex++)
        {
            int offset = (vertex * 6) + 3;
            await Assert.That(geometry.Vertices[offset]).IsEqualTo(0f);
            await Assert.That(geometry.Vertices[offset + 1]).IsEqualTo(0f);
            await Assert.That(geometry.Vertices[offset + 2]).IsEqualTo(1f);
        }
    }

    [Test]
    public async Task DegenerateTrianglesUseDeterministicFallbackNormals()
    {
        SilkMeshGeometry geometry = SilkMeshGeometryBuilder.Build(CreateMesh(
            [0, 0, 0, 1, 0, 0, 2, 0, 0],
            [0, 1, 2]));

        await Assert.That(geometry.Vertices[3]).IsEqualTo(0f);
        await Assert.That(geometry.Vertices[4]).IsEqualTo(0f);
        await Assert.That(geometry.Vertices[5]).IsEqualTo(1f);
    }

    [Test]
    [Arguments(new uint[] { 0, 1 }, "topology kind")]
    [Arguments(new uint[] { 0, 1, 3 }, "references vertex 3")]
    public async Task RejectsInvalidTopology(uint[] indices, string message)
    {
        InvalidDataException exception = (await Assert.That(
            () => SilkMeshGeometryBuilder.Build(CreateMesh(
                [0, 0, 0, 1, 0, 0, 0, 1, 0],
                indices)))
            .Throws<InvalidDataException>())!;

        await Assert.That(exception.Message).Contains(message);
        await Assert.That(exception.Message).Contains("/Triangle");
    }

    [Test]
    public async Task RejectsNonFinitePoints()
    {
        InvalidDataException exception = (await Assert.That(
            () => SilkMeshGeometryBuilder.Build(CreateMesh(
                [0, 0, 0, float.NaN, 0, 0, 0, 1, 0],
                [0, 1, 2])))
            .Throws<InvalidDataException>())!;

        await Assert.That(exception.Message).Contains("not finite");
    }

    [Test]
    public async Task WritesTransposedRowVectorTransformAndTint()
    {
        SilkMeshData mesh = CreateMesh(
            [0, 0, 0, 1, 0, 0, 0, 1, 0],
            [0, 1, 2],
            color: [0.25f, 0.5f, 0.75f, 0.125f],
            transform:
            [
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                2, 3, 4, 1,
            ]);
        var frame = new SilkSceneState();
        _ = frame.Apply(CreateFrameCommand(), 1, 1);
        var bytes = new byte[SilkSceneUniformWriter.ByteSize];

        SilkSceneUniformWriter.Write(mesh, frame.Frame, bytes);

        await Assert.That(ReadSingle(bytes, 3)).IsEqualTo(2f);
        await Assert.That(ReadSingle(bytes, 7)).IsEqualTo(3f);
        await Assert.That(ReadSingle(bytes, 10)).IsEqualTo(0.5f);
        await Assert.That(ReadSingle(bytes, 11)).IsEqualTo(2.5f);
        await Assert.That(ReadSingle(bytes, 16)).IsEqualTo(0.25f);
        await Assert.That(ReadSingle(bytes, 19)).IsEqualTo(0.125f);
    }

    [Test]
    public async Task RejectsUnrepresentableTransformValues()
    {
        double[] transform =
        [
            double.MaxValue, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1,
        ];
        SilkMeshData mesh = CreateMesh(
            [0, 0, 0, 1, 0, 0, 0, 1, 0],
            [0, 1, 2],
            transform: transform);
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateFrameCommand(), 1, 1);
        var bytes = new byte[SilkSceneUniformWriter.ByteSize];

        InvalidDataException exception = (await Assert.That(
            () => SilkSceneUniformWriter.Write(mesh, scene.Frame, bytes))
            .Throws<InvalidDataException>())!;

        await Assert.That(exception.Message).Contains("objectToClip");
    }

    [Test]
    public async Task RejectsNonFiniteDisplayColor()
    {
        SilkMeshData mesh = CreateMesh(
            [0, 0, 0, 1, 0, 0, 0, 1, 0],
            [0, 1, 2],
            color: [1, float.NaN, 1, 1]);
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateFrameCommand(), 1, 1);
        var bytes = new byte[SilkSceneUniformWriter.ByteSize];

        InvalidDataException exception = (await Assert.That(
            () => SilkSceneUniformWriter.Write(mesh, scene.Frame, bytes))
            .Throws<InvalidDataException>())!;

        await Assert.That(exception.Message).Contains("green display color");
    }

    [Test]
    public async Task ColorOnlyUpdateReusesGeometryAndSteadyFrameDoesNotUpload()
    {
        var scene = new SilkSceneState();
        using var device = new TestGraphicsDevice();
        using var resources = new SilkSceneGpuResources(device);
        byte[] frame = CreateFrameCommand();
        byte[] firstMesh = CreateMeshCommand([1, 0, 0, 1]);
        Apply(scene, resources, frame, firstMesh, 1);
        SilkMeshGpuResource first = resources.Meshes[7];
        int firstUniformUploads = resources.UpdateUniforms(scene.Frame);

        byte[] secondMesh = CreateMeshCommand([0, 1, 0, 0.5f]);
        Apply(scene, resources, frame, secondMesh, 2);
        SilkMeshGpuResource second = resources.Meshes[7];
        int secondUniformUploads = resources.UpdateUniforms(scene.Frame);
        int steadyUploads = resources.UpdateUniforms(scene.Frame);

        const int steadyFrameIterations = 1000;
        const int maximumMeasuredPasses = 8;
        const int requiredConsecutiveZeroPasses = 2;

        // CI run 31277374697 caught a one-shot 5112-byte net9.0 allocation after
        // the old warm pass. Require consecutive zero measured windows instead
        // of trusting the first window; per-frame allocations still fail every
        // window, while runtime tiering/test-host transients do not define
        // renderer steady state.
        static void runSteadyFrames(
            SilkSceneState scene,
            SilkSceneGpuResources resources,
            byte[] frame,
            ulong firstRevision,
            int iterations)
        {
            for (int i = 0; i < iterations; i++)
            {
                SilkSceneDelta steadyDelta =
                    scene.Apply(frame, 1, checked(firstRevision + (ulong)i));
                resources.Apply(scene, steadyDelta);
                _ = resources.UpdateUniforms(scene.Frame);
            }
        }

        runSteadyFrames(scene, resources, frame, 100, steadyFrameIterations);

        int consecutiveZeroPasses = 0;
        for (int pass = 0; pass < maximumMeasuredPasses; pass++)
        {
            ulong firstRevision = 1_100 + ((ulong)pass * steadyFrameIterations);
            long before = GC.GetAllocatedBytesForCurrentThread();
            runSteadyFrames(scene, resources, frame, firstRevision, steadyFrameIterations);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            consecutiveZeroPasses = allocated == 0 ? consecutiveZeroPasses + 1 : 0;
            if (consecutiveZeroPasses == requiredConsecutiveZeroPasses)
            {
                break;
            }
        }

        await Assert.That(second).IsSameReferenceAs(first);
        // One vertex, one index and one uniform. No instance buffer: it is only
        // allocated by an instanced draw, and this test never renders.
        await Assert.That(device.Buffers).Count().IsEqualTo(3);
        await Assert.That(firstUniformUploads).IsEqualTo(1);
        await Assert.That(secondUniformUploads).IsEqualTo(1);
        await Assert.That(steadyUploads).IsEqualTo(0);
        await Assert.That(consecutiveZeroPasses)
            .IsEqualTo(requiredConsecutiveZeroPasses);
        await Assert.That(resources.Statistics.GeometryBuilds).IsEqualTo(1ul);
        await Assert.That(resources.Statistics.UniformUploads).IsEqualTo(2ul);
        // Resolved from the mesh rather than by buffer index: positional indexing
        // silently pointed at the wrong buffer once the instance buffer stopped
        // being allocated eagerly.
        var uniform = (TestGraphicsBuffer)second.UniformBuffer;
        await Assert.That(ReadSingle(uniform.Data, 17)).IsEqualTo(1f);
        await Assert.That(ReadSingle(uniform.Data, 19)).IsEqualTo(0.5f);
    }

    [Test]
    public async Task PointInstancesShareUploadedPrototypeGeometry()
    {
        var scene = new SilkSceneState();
        using var device = new TestGraphicsDevice();
        using var resources = new SilkSceneGpuResources(device);
        byte[] frame = CreateFrameCommand();
        byte[] firstMesh = CreateMeshCommand([1, 0, 0, 1], instanceIndex: 0);
        byte[] secondMesh = CreateMeshCommand(
            [1, 0, 0, 1],
            instanceIndex: 1,
            transform:
            [
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                2, 0, 0, 1,
            ]);
        byte[] page = new byte[frame.Length + firstMesh.Length + secondMesh.Length];
        frame.CopyTo(page, 0);
        firstMesh.CopyTo(page, frame.Length);
        secondMesh.CopyTo(page, frame.Length + firstMesh.Length);

        resources.Apply(scene, scene.Apply(page, 3, revision: 1));
        SilkMeshGpuResource first = resources.Meshes[7];
        SilkMeshGpuResource second = resources.Meshes[(1UL << 63) | (7UL << 32) | 1UL];

        // One shared vertex and index plus one uniform per instance. The
        // per-instance transform buffer is allocated only by an instanced draw.
        await Assert.That(device.Buffers).Count().IsEqualTo(4);
        await Assert.That(first.VertexBuffer).IsSameReferenceAs(second.VertexBuffer);
        await Assert.That(first.IndexBuffer).IsSameReferenceAs(second.IndexBuffer);
        await Assert.That(first.UniformBuffer).IsNotSameReferenceAs(second.UniformBuffer);
        await Assert.That(resources.Statistics.GeometryBuilds).IsEqualTo(1ul);
        await Assert.That(resources.Statistics.VertexUploads).IsEqualTo(1ul);
        await Assert.That(resources.Statistics.IndexUploads).IsEqualTo(1ul);
        await Assert.That(resources.Statistics.MeshCount).IsEqualTo(2);
    }

    private static SilkMeshData CreateMesh(
        float[] points,
        uint[] indices,
        float[]? color = null,
        double[]? transform = null) =>
        new(
            7,
            "/Triangle",
            points,
            indices,
            color ?? [1, 1, 1, 1],
            transform ??
            [
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                0, 0, 0, 1,
            ]);

    private static void Apply(
        SilkSceneState scene,
        SilkSceneGpuResources resources,
        byte[] frame,
        byte[] mesh,
        ulong revision)
    {
        byte[] page = new byte[frame.Length + mesh.Length];
        frame.CopyTo(page, 0);
        mesh.CopyTo(page, frame.Length);
        resources.Apply(scene, scene.Apply(page, 2, revision));
    }

    private static byte[] CreateFrameCommand()
    {
        var bytes = new byte[272];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 64);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 64);
        for (int i = 0; i < 16; i++)
        {
            double value = i % 5 == 0 ? 1 : 0;
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(16 + (i * 8)), value);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(144 + (i * 8)), value);
        }
        return bytes;
    }

    private static byte[] CreateMeshCommand(
        float[] color,
        int instanceIndex = 0,
        double[]? transform = null,
        string pathText = "/Triangle",
        int primId = 7,
        float pointZ = 0)
    {
        byte[] path = Encoding.UTF8.GetBytes(pathText);
        byte[] instancerPath = instanceIndex == 0
            ? []
            : Encoding.UTF8.GetBytes("/Instancer");
        float[] points = [-0.5f, -0.5f, pointZ, 0, 0.5f, pointZ, 0.5f, -0.5f, pointZ];
        uint[] indices = [0, 1, 2];
        int size = 268 +
            path.Length +
            (points.Length * sizeof(float)) +
            (indices.Length * sizeof(uint)) +
            sizeof(uint) +
            instancerPath.Length +
            (instancerPath.Length == 0 ? 0 : 8 + instancerPath.Length);
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkWireFormat.ComputeStableHash(pathText));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), primId);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(28),
            (uint)SilkTopologyKind.TriangleList);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), instanceIndex == 0 ? 0 : 11);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), instanceIndex);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(44), (uint)SilkMeshCullStyle.BackUnlessDoubleSided);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), (uint)path.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), 1);
        for (int i = 0; i < 4; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(64 + (i * 4)), color[i]);
        }
        for (int i = 0; i < 16; i++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(80 + (i * 8)),
                transform is null ? i % 5 == 0 ? 1 : 0 : transform[i]);
        }
        path.CopyTo(bytes, 268);
        int pointsOffset = 268 + path.Length;
        for (int i = 0; i < points.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(pointsOffset + (i * 4)), points[i]);
        }
        int indicesOffset = pointsOffset + (points.Length * sizeof(float));
        for (int i = 0; i < indices.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(indicesOffset + (i * 4)), indices[i]);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(indicesOffset + (indices.Length * sizeof(uint))),
            0);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(260),
            (uint)instancerPath.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(264),
            instancerPath.Length == 0 ? 0u : 1u);
        int instancerPathOffset =
            indicesOffset + (indices.Length * sizeof(uint)) + sizeof(uint);
        instancerPath.CopyTo(bytes.AsSpan(instancerPathOffset));
        if (instancerPath.Length != 0)
        {
            int contextOffset = instancerPathOffset + instancerPath.Length;
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(contextOffset),
                (uint)instancerPath.Length);
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(contextOffset + 4),
                instanceIndex);
            instancerPath.CopyTo(bytes.AsSpan(contextOffset + 8));
        }
        return bytes;
    }

    private static float ReadSingle(ReadOnlySpan<byte> bytes, int floatIndex) =>
        BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(floatIndex * sizeof(float), sizeof(float)));

    private static byte[] CreateRemoval(string path)
    {
        byte[] text = Encoding.UTF8.GetBytes(path);
        byte[] result = new byte[24 + text.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(result, (uint)SilkCommandType.MeshRemove);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)result.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(8), SilkWireFormat.ComputeStableHash(path));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(20), (uint)text.Length);
        text.CopyTo(result, 24);
        return result;
    }

    private sealed class TestGraphicsDevice : ISilkGraphicsDevice
    {
        internal List<TestGraphicsBuffer> Buffers { get; } = [];
        internal int? BufferFailureCountdown { get; set; }
        internal int? WriteFailureCountdown { get; set; }

        public SilkGraphicsBackend Backend => SilkGraphicsBackend.Vulkan;

        public SilkGraphicsCapabilities Capabilities { get; } =
            new("Test", "1", SupportsCompute: true, IsSoftware: true);

        public ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage)
        {
            if (BufferFailureCountdown is { } count)
            {
                BufferFailureCountdown = count - 1;
                if (count == 0)
                {
                    throw new InvalidOperationException("Injected GPU buffer allocation refusal.");
                }
            }
            var buffer = new TestGraphicsBuffer(size, usage, this);
            Buffers.Add(buffer);
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

        public ISilkGraphicsBindingLayout CreateBindingLayout(SilkBindingLayoutDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsShaderProgram CreateShaderProgram(SilkShaderProgramDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsPipeline CreateGraphicsPipeline(SilkGraphicsPipelineDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputeBindingLayout CreateComputeBindingLayout(
            SilkComputeBindingLayoutDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputeShaderProgram CreateComputeShaderProgram(
            SilkComputeShaderProgramDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputePipeline CreateComputePipeline(SilkComputePipelineDescriptor descriptor) =>
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

    private sealed class TestGraphicsBuffer(nuint size, SilkBufferUsage usage, TestGraphicsDevice owner)
        : SilkGraphicsBufferBase(size, usage)
    {
        internal byte[] Data { get; } = new byte[checked((int)size)];
        internal bool Released { get; private set; }

        public override void Write(ReadOnlySpan<byte> data, nuint offset = 0)
        {
            _ = ValidateWrite(data.Length, offset);
            if (owner.WriteFailureCountdown is { } count)
            {
                owner.WriteFailureCountdown = count - 1;
                if (count == 0)
                {
                    throw new InvalidOperationException("Injected GPU buffer upload refusal.");
                }
            }
            data.CopyTo(Data.AsSpan(checked((int)offset)));
        }

        public override void ReadbackForTesting(Span<byte> destination)
        {
            _ = ValidateReadback(destination.Length);
            Data.CopyTo(destination);
        }

        protected override void ReleaseNative()
        {
            Released = true;
        }
    }
}
