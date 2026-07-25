// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkMeshRendererTests
{
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
    [Arguments(new uint[] { 0, 1 }, "divisible by three")]
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
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            SilkSceneDelta steadyDelta = scene.Apply(frame, 1, checked((ulong)(100 + i)));
            resources.Apply(scene, steadyDelta);
            _ = resources.UpdateUniforms(scene.Frame);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(second).IsSameReferenceAs(first);
        await Assert.That(device.Buffers).Count().IsEqualTo(3);
        await Assert.That(firstUniformUploads).IsEqualTo(1);
        await Assert.That(secondUniformUploads).IsEqualTo(1);
        await Assert.That(steadyUploads).IsEqualTo(0);
        await Assert.That(allocated).IsEqualTo(0);
        await Assert.That(resources.Statistics.GeometryBuilds).IsEqualTo(1ul);
        await Assert.That(resources.Statistics.UniformUploads).IsEqualTo(2ul);
        await Assert.That(ReadSingle(device.Buffers[2].Data, 17)).IsEqualTo(1f);
        await Assert.That(ReadSingle(device.Buffers[2].Data, 19)).IsEqualTo(0.5f);
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

    private static byte[] CreateMeshCommand(float[] color)
    {
        byte[] path = Encoding.UTF8.GetBytes("/Triangle");
        float[] points = [-0.5f, -0.5f, 0, 0, 0.5f, 0, 0.5f, -0.5f, 0];
        uint[] indices = [0, 1, 2];
        int size = 200 +
            path.Length +
            (points.Length * sizeof(float)) +
            (indices.Length * sizeof(uint)) +
            sizeof(uint);
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkWireFormat.ComputeStableHash("/Triangle"));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 7);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(28),
            (uint)SilkTopologyKind.TriangleList);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), (uint)path.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(44), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52), 1);
        for (int i = 0; i < 4; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(56 + (i * 4)), color[i]);
        }
        for (int i = 0; i < 16; i++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(72 + (i * 8)),
                i % 5 == 0 ? 1 : 0);
        }
        path.CopyTo(bytes, 200);
        int pointsOffset = 200 + path.Length;
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
        return bytes;
    }

    private static float ReadSingle(ReadOnlySpan<byte> bytes, int floatIndex) =>
        BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(floatIndex * sizeof(float), sizeof(float)));

    private sealed class TestGraphicsDevice : ISilkGraphicsDevice
    {
        internal List<TestGraphicsBuffer> Buffers { get; } = [];

        public SilkGraphicsBackend Backend => SilkGraphicsBackend.Vulkan;

        public SilkGraphicsCapabilities Capabilities { get; } =
            new("Test", "1", SupportsCompute: true, IsSoftware: true);

        public ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage)
        {
            var buffer = new TestGraphicsBuffer(size, usage);
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

    private sealed class TestGraphicsBuffer(nuint size, SilkBufferUsage usage)
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
}
