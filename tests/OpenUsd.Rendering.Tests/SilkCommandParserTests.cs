// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkCommandParserTests
{
    [Test]
    public async Task ParsesFrameAndMeshCommands()
    {
        byte[] frame = CreateFrameCommand();
        byte[] mesh = CreateMeshCommand();
        byte[] page = new byte[frame.Length + mesh.Length];
        frame.CopyTo(page, 0);
        mesh.CopyTo(page, frame.Length);

        int width;
        int height;
        string path;
        int pointCount;
        uint lastIndex;
        ulong stableHash;
        int primId;
        int instanceId;
        int instanceIndex;
        SilkTopologyKind topologyKind;
        ulong topologyRevision;
        int triangleCount;
        int subprim;
        {
            using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(
                page,
                2,
                SilkCommandParser.PageAbiVersion);
            if (!commands.MoveNext())
            {
                throw new InvalidDataException("Missing frame command.");
            }
            SilkFrameCommand frameCommand = commands.Current.AsFrame();
            width = frameCommand.Width;
            height = frameCommand.Height;

            if (!commands.MoveNext())
            {
                throw new InvalidDataException("Missing mesh command.");
            }
            SilkMeshUpsertCommand meshCommand = commands.Current.AsMeshUpsert();
            path = meshCommand.Path;
            pointCount = meshCommand.PointCount;
            lastIndex = meshCommand.GetIndex(2);
            stableHash = meshCommand.StableHash;
            primId = meshCommand.PrimId;
            instanceId = meshCommand.InstanceId;
            instanceIndex = meshCommand.InstanceIndex;
            topologyKind = meshCommand.TopologyKind;
            topologyRevision = meshCommand.TopologyRevision;
            triangleCount = meshCommand.TriangleCount;
            subprim = meshCommand.GetTriangleSubprim(0);
        }

        await Assert.That(width).IsEqualTo(1280);
        await Assert.That(height).IsEqualTo(720);
        await Assert.That(path).IsEqualTo("/Cube");
        await Assert.That(pointCount).IsEqualTo(3);
        await Assert.That(lastIndex).IsEqualTo(2u);
        await Assert.That(stableHash)
            .IsEqualTo(SilkWireFormat.ComputeStableHash("/Cube"));
        await Assert.That(primId).IsEqualTo(42);
        await Assert.That(instanceId).IsEqualTo(0);
        await Assert.That(instanceIndex).IsEqualTo(0);
        await Assert.That(topologyKind).IsEqualTo(SilkTopologyKind.TriangleList);
        await Assert.That(topologyRevision).IsEqualTo(1ul);
        await Assert.That(triangleCount).IsEqualTo(1);
        await Assert.That(subprim).IsEqualTo(17);
    }

    [Test]
    public async Task RejectsPageAbiV1AndAcceptsV2()
    {
        await Assert.That(() => new OpenUsdSilkPage(1, 1, [], 0))
            .Throws<InvalidDataException>();

        bool hasCommand;
        using (var page = new OpenUsdSilkPage(
            SilkCommandParser.PageAbiVersion,
            1,
            [],
            0))
        {
            using SilkCommandEnumerator commands = page.GetEnumerator();
            hasCommand = commands.MoveNext();
        }
        await Assert.That(hasCommand).IsFalse();
    }

    [Test]
    public async Task RejectsMalformedSizesCountsUtf8AndInstanceFields()
    {
        byte[] invalidSize = CreateMeshCommand();
        BinaryPrimitives.WriteUInt32LittleEndian(
            invalidSize.AsSpan(4),
            checked((uint)(invalidSize.Length - 1)));

        byte[] invalidPathCount = CreateMeshCommand();
        BinaryPrimitives.WriteUInt32LittleEndian(
            invalidPathCount.AsSpan(40),
            uint.MaxValue);

        byte[] invalidTriangleCount = CreateMeshCommand();
        BinaryPrimitives.WriteUInt32LittleEndian(
            invalidTriangleCount.AsSpan(52),
            2);

        byte[] invalidUtf8 = CreateMeshCommand();
        invalidUtf8[200] = 0xFF;

        byte[] unsupportedInstance = CreateMeshCommand();
        BinaryPrimitives.WriteInt32LittleEndian(
            unsupportedInstance.AsSpan(20),
            1);

        byte[] invalidHash = CreateMeshCommand(stableHash: 1);

        foreach (byte[] malformed in
            new[]
            {
                invalidSize,
                invalidPathCount,
                invalidTriangleCount,
                invalidUtf8,
                unsupportedInstance,
                invalidHash,
            })
        {
            await Assert.That(
                    () => new SilkSceneState().Apply(malformed, 1, revision: 1))
                .Throws<InvalidDataException>();
        }
        await Assert.That(
                () => new SilkSceneState().Apply(
                    CreateMeshCommand(),
                    commandCount: 0,
                    revision: 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task SeededCommandPageMutationsParseOrThrowInvalidData()
    {
        const int mutationCount = 512;
        const int seed = 0x51C0_2026;
        byte[] frame = CreateFrameCommand();
        byte[] mesh = CreateMeshCommand();
        var baseline = new byte[frame.Length + mesh.Length];
        frame.CopyTo(baseline, 0);
        mesh.CopyTo(baseline, frame.Length);
        var random = new Random(seed);
        int accepted = 0;
        int rejected = 0;

        for (int mutation = 0; mutation < mutationCount; mutation++)
        {
            byte[] page = (byte[])baseline.Clone();
            uint commandCount = 2;
            ApplyCommandPageMutation(random, mutation, ref page, ref commandCount);

            try
            {
                var scene = new SilkSceneState();
                _ = scene.Apply(page, commandCount, revision: 1);
                if (scene.Revision != 1)
                {
                    throw new InvalidOperationException(
                        $"Accepted command-page mutation {mutation} lost its revision.");
                }
                accepted++;
            }
            catch (InvalidDataException)
            {
                rejected++;
            }
        }

        await Assert.That(accepted + rejected).IsEqualTo(mutationCount);
        await Assert.That(accepted).IsGreaterThan(0);
        await Assert.That(rejected).IsGreaterThan(0);
    }

    [Test]
    public async Task CoalescedRecreationReplacesPrimAndInvalidatesOldTokens()
    {
        var scene = new SilkSceneState();
        using var device = new TestGraphicsDevice();
        using var resources = new SilkSceneGpuResources(device);
        SilkSceneDelta firstDelta = scene.Apply(
            CreateMeshCommand(
                primId: 42,
                topologyRevision: 5,
                triangleSubprims: [11]),
            1,
            revision: 1);
        resources.Apply(scene, firstDelta);
        SilkMeshGpuResource firstResource = resources.Meshes[42];
        await Assert.That(scene.PickIdentities.Revision).IsEqualTo(1ul);
        await Assert.That(scene.PickIdentities.TryGetRange(
            "/Cube",
            out SilkPickTokenRange firstRange)).IsTrue();

        SilkSceneDelta recreatedDelta = scene.Apply(
            CreateMeshCommand(
                primId: 43,
                topologyRevision: 1,
                triangleSubprims: [12]),
            1,
            revision: 2);
        resources.Apply(scene, recreatedDelta);
        await Assert.That(scene.PickIdentities.Revision).IsEqualTo(2ul);

        await Assert.That(firstDelta.UpsertedMeshIds.ToArray())
            .IsEquivalentTo([42ul]);
        await Assert.That(recreatedDelta.RemovedMeshIds.ToArray())
            .IsEquivalentTo([42ul]);
        await Assert.That(recreatedDelta.UpsertedMeshIds.ToArray())
            .IsEquivalentTo([43ul]);
        await Assert.That(scene.Meshes.ContainsKey(42)).IsFalse();
        await Assert.That(scene.Meshes[43].TopologyRevision).IsEqualTo(1ul);
        await Assert.That(resources.Meshes.Keys).IsEquivalentTo([43ul]);
        await Assert.That(firstResource.VertexBuffer)
            .IsSameReferenceAs(device.Buffers[0]);
        await Assert.That(device.Buffers[0].IsDisposed).IsTrue();
        await Assert.That(device.Buffers[1].IsDisposed).IsTrue();
        await Assert.That(device.Buffers[2].IsDisposed).IsTrue();
        await Assert.That(scene.PickIdentities.TryResolve(
            firstRange.FirstToken,
            out _)).IsFalse();
        await Assert.That(scene.PickIdentities.TryGetRange(
            "/Cube",
            out SilkPickTokenRange recreatedRange)).IsTrue();
        await Assert.That(recreatedRange.FirstToken)
            .IsGreaterThan(firstRange.LastToken);
        await Assert.That(scene.PickIdentities.TryResolve(
            recreatedRange.FirstToken,
            out SilkPickIdentity identity)).IsTrue();
        await Assert.That(identity.PrimId).IsEqualTo(43);
        await Assert.That(identity.TopologyRevision).IsEqualTo(1ul);
        await Assert.That(identity.SubprimIndex).IsEqualTo(12);
    }

    [Test]
    public async Task DetectsHashCollisionWithoutLosingAuthoritativePath()
    {
        var scene = new SilkSceneState(_ => 99);
        _ = scene.Apply(
            CreateMeshCommand(pathValue: "/First", stableHash: 99, primId: 1),
            1,
            revision: 1);

        InvalidDataException exception = (await Assert.That(
                () => scene.Apply(
                    CreateMeshCommand(pathValue: "/Second", stableHash: 99, primId: 2),
                    1,
                    revision: 2))
            .Throws<InvalidDataException>())!;

        await Assert.That(exception.Message).Contains("collides");
        await Assert.That(scene.MeshesByPath.Keys).IsEquivalentTo(["/First"]);
        await Assert.That(scene.MeshesByPath["/First"].StableHash)
            .IsEqualTo(99ul);
    }

    [Test]
    public async Task CopiesTriangleMappingAndInvalidatesTokensAcrossRemoveReadd()
    {
        byte[] firstPage = CreateMeshCommand(
            topologyRevision: 3,
            triangleSubprims: [11]);
        var scene = new SilkSceneState();
        _ = scene.Apply(firstPage, 1, revision: 1);
        SilkMeshData first = scene.Meshes[42];
        await Assert.That(scene.PickIdentities.TryGetRange(
            "/Cube",
            out SilkPickTokenRange firstRange)).IsTrue();

        int mappingOffset = firstPage.Length - sizeof(uint);
        BinaryPrimitives.WriteUInt32LittleEndian(
            firstPage.AsSpan(mappingOffset),
            999);
        await Assert.That(first.TriangleSubprims.Span[0]).IsEqualTo(11);

        _ = scene.Apply(CreateMeshRemoveCommand(), 1, revision: 2);
        await Assert.That(scene.PickIdentities.TryResolve(
            firstRange.FirstToken,
            out _)).IsFalse();
        await Assert.That(scene.MeshesByPath.Count).IsEqualTo(0);

        _ = scene.Apply(
            CreateMeshCommand(
                primId: 43,
                topologyRevision: 1,
                triangleSubprims: [12]),
            1,
            revision: 3);
        await Assert.That(scene.PickIdentities.TryGetRange(
            "/Cube",
            out SilkPickTokenRange secondRange)).IsTrue();
        await Assert.That(secondRange.FirstToken)
            .IsGreaterThan(firstRange.LastToken);
        await Assert.That(scene.PickIdentities.TryResolve(
            secondRange.FirstToken,
            out SilkPickIdentity identity)).IsTrue();
        await Assert.That(identity.Path).IsEqualTo("/Cube");
        await Assert.That(identity.PrimId).IsEqualTo(43);
        await Assert.That(identity.SubprimIndex).IsEqualTo(12);
    }

    [Test]
    public async Task RetainsMeshesAcrossFrameOnlyPages()
    {
        byte[] frame = CreateFrameCommand();
        byte[] mesh = CreateMeshCommand();
        byte[] firstPage = new byte[frame.Length + mesh.Length];
        frame.CopyTo(firstPage, 0);
        mesh.CopyTo(firstPage, frame.Length);
        var scene = new SilkSceneState();

        SilkSceneDelta firstDelta = scene.Apply(firstPage, 2, revision: 1);
        SilkMeshData retained = scene.Meshes[42];
        ulong identityRevision = scene.PickIdentities.Revision;
        SilkSceneDelta secondDelta = scene.Apply(frame, 1, revision: 2);

        await Assert.That(firstDelta.MeshUpserts).IsEqualTo(1);
        await Assert.That(secondDelta.MeshUpserts).IsEqualTo(0);
        await Assert.That(scene.Meshes[42]).IsSameReferenceAs(retained);
        await Assert.That(scene.Revision).IsEqualTo(2ul);
        await Assert.That(scene.PickIdentities.Revision)
            .IsEqualTo(identityRevision);
    }

    [Test]
    public async Task UploadsOnlyDirtyMeshesAndReleasesRemovals()
    {
        byte[] frame = CreateFrameCommand();
        byte[] mesh = CreateMeshCommand();
        byte[] firstPage = new byte[frame.Length + mesh.Length];
        frame.CopyTo(firstPage, 0);
        mesh.CopyTo(firstPage, frame.Length);
        var scene = new SilkSceneState();
        using var device = new TestGraphicsDevice();
        using var resources = new SilkSceneGpuResources(device);

        SilkSceneDelta firstDelta = scene.Apply(firstPage, 2, revision: 1);
        resources.Apply(scene, firstDelta);
        resources.Apply(scene, scene.Apply(frame, 1, revision: 2));

        await Assert.That(device.Buffers).Count().IsEqualTo(3);
        await Assert.That(resources.Meshes.ContainsKey(42)).IsTrue();
        await Assert.That(
            BinaryPrimitives.ReadSingleLittleEndian(device.Buffers[0].Data))
            .IsEqualTo(0f);
        await Assert.That(
            BinaryPrimitives.ReadSingleLittleEndian(device.Buffers[0].Data.AsSpan(20)))
            .IsEqualTo(1f);
        await Assert.That(
            BinaryPrimitives.ReadUInt16LittleEndian(device.Buffers[1].Data.AsSpan(4)))
            .IsEqualTo((ushort)2);

        byte[] removal = CreateMeshRemoveCommand();
        resources.Apply(scene, scene.Apply(removal, 1, revision: 3));

        await Assert.That(resources.Meshes.Count).IsEqualTo(0);
        await Assert.That(device.Buffers[0].IsDisposed).IsTrue();
        await Assert.That(device.Buffers[1].IsDisposed).IsTrue();
        await Assert.That(device.Buffers[2].IsDisposed).IsTrue();
    }

    private static void ApplyCommandPageMutation(
        Random random,
        int mutation,
        ref byte[] page,
        ref uint commandCount)
    {
        const int meshOffset = 272;
        switch (mutation % 12)
        {
            case 0:
                int byteIndex = random.Next(page.Length);
                page[byteIndex] ^= checked((byte)random.Next(1, 256));
                break;
            case 1:
                int sizeOffset = (random.Next(2) == 0 ? 0 : meshOffset) + 4;
                uint currentSize = BinaryPrimitives.ReadUInt32LittleEndian(
                    page.AsSpan(sizeOffset, sizeof(uint)));
                uint[] sizes = [0, 7, currentSize - 1, currentSize + 1, uint.MaxValue];
                BinaryPrimitives.WriteUInt32LittleEndian(
                    page.AsSpan(sizeOffset, sizeof(uint)),
                    sizes[random.Next(sizes.Length)]);
                break;
            case 2:
                int countOffset = meshOffset + new[] { 40, 44, 48, 52 }[random.Next(4)];
                uint[] counts = [0, 2, 4, (uint)int.MaxValue, uint.MaxValue];
                BinaryPrimitives.WriteUInt32LittleEndian(
                    page.AsSpan(countOffset, sizeof(uint)),
                    counts[random.Next(counts.Length)]);
                break;
            case 3:
                int unknownTypeOffset = random.Next(2) == 0 ? 0 : meshOffset;
                uint unknownType = 0x8000_0000u | checked((uint)random.Next(1, int.MaxValue));
                BinaryPrimitives.WriteUInt32LittleEndian(
                    page.AsSpan(unknownTypeOffset, sizeof(uint)),
                    unknownType);
                break;
            case 4:
                Array.Resize(ref page, random.Next(page.Length));
                break;
            case 5:
                int variableCountOffset = meshOffset + (random.Next(2) == 0 ? 48 : 52);
                uint variableCount = variableCountOffset == meshOffset + 48 ? 4u : 2u;
                BinaryPrimitives.WriteUInt32LittleEndian(
                    page.AsSpan(variableCountOffset, sizeof(uint)),
                    variableCount);
                break;
            case 6:
                page = [.. page, checked((byte)random.Next(0, 256))];
                break;
            case 7:
                page = RemoveAt(
                    page,
                    random.Next(meshOffset + 200, page.Length));
                break;
            case 8:
                page[meshOffset + 200 + random.Next("/Cube".Length)] = 0xff;
                break;
            case 9:
                uint[] commandCounts = [0, 1, 3, uint.MaxValue];
                commandCount = commandCounts[random.Next(commandCounts.Length)];
                break;
            case 10:
                int knownTypeOffset = random.Next(2) == 0 ? 0 : meshOffset;
                uint knownType = knownTypeOffset == 0
                    ? (uint)SilkCommandType.MeshUpsert
                    : (uint)SilkCommandType.Frame;
                BinaryPrimitives.WriteUInt32LittleEndian(
                    page.AsSpan(knownTypeOffset, sizeof(uint)),
                    knownType);
                break;
            default:
                int framePayloadOffset = 16 + random.Next(256);
                page[framePayloadOffset] ^= 0x01;
                break;
        }
    }

    private static byte[] RemoveAt(byte[] values, int index)
    {
        var result = new byte[values.Length - 1];
        values.AsSpan(0, index).CopyTo(result);
        values.AsSpan(index + 1).CopyTo(result.AsSpan(index));
        return result;
    }

    private static byte[] CreateFrameCommand()
    {
        var bytes = new byte[272];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), (uint)bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8, 4), 1280);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12, 4), 720);
        for (int i = 0; i < 16; i++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(16 + (i * 8), 8), i % 5 == 0 ? 1 : 0);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(144 + (i * 8), 8), i % 5 == 0 ? 1 : 0);
        }
        return bytes;
    }

    private static byte[] CreateMeshCommand(
        string pathValue = "/Cube",
        ulong? stableHash = null,
        int primId = 42,
        ulong topologyRevision = 1,
        int[]? triangleSubprims = null)
    {
        byte[] path = Encoding.UTF8.GetBytes(pathValue);
        stableHash ??= SilkWireFormat.ComputeStableHash(pathValue);
        const int pointCount = 3;
        const int indexCount = 3;
        triangleSubprims ??= [17];
        int triangleCount = triangleSubprims.Length;
        int size = 200 +
            path.Length +
            (pointCount * 12) +
            (indexCount * 4) +
            (triangleCount * sizeof(uint));
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8, 8), stableHash.Value);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16, 4), primId);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(28, 4),
            (uint)SilkTopologyKind.TriangleList);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(32, 8),
            topologyRevision);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40, 4), (uint)path.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(44, 4), pointCount);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48, 4), indexCount);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52, 4), (uint)triangleCount);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(56, 4), 0.7f);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(60, 4), 0.7f);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(64, 4), 0.75f);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(68, 4), 1);
        for (int i = 0; i < 16; i++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(72 + (i * 8), 8), i % 5 == 0 ? 1 : 0);
        }
        path.CopyTo(bytes, 200);
        int pointsOffset = 200 + path.Length;
        for (int i = 0; i < pointCount * 3; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(pointsOffset + (i * 4), 4), i);
        }
        int indicesOffset = pointsOffset + (pointCount * 12);
        for (uint i = 0; i < indexCount; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(indicesOffset + ((int)i * 4), 4), i);
        }
        int subprimsOffset = indicesOffset + (indexCount * sizeof(uint));
        for (int i = 0; i < triangleSubprims.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(subprimsOffset + (i * sizeof(uint))),
                checked((uint)triangleSubprims[i]));
        }
        return bytes;
    }

    private static byte[] CreateMeshRemoveCommand()
    {
        byte[] path = Encoding.UTF8.GetBytes("/Cube");
        var bytes = new byte[20 + path.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(0, 4),
            (uint)SilkCommandType.MeshRemove);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8, 8),
            SilkWireFormat.ComputeStableHash("/Cube"));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), (uint)path.Length);
        path.CopyTo(bytes, 20);
        return bytes;
    }

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

        public ISilkGraphicsShaderModule CreateShaderModule(
            SilkShaderModuleDescriptor descriptor) =>
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

        public ISilkGraphicsCommandList CreateCommandList() =>
            throw new NotSupportedException();

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
        }
    }
}
