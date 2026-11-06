// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;
using OpenUsd.Rendering.Silk.Metal;
using OpenUsd.Rendering.Silk.Vulkan;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkPtexSlotTransportConformanceTests
{
    [Test]
    [MethodDataSource(nameof(Backends))]
    public async Task VertexIdLookupTransportsIndependentSlotUvsAcrossRepeatedInstancesWithoutSamplingTextures(
        SilkGraphicsBackend backend) => await Run(backend, sampling: false);

    [Test]
    [MethodDataSource(nameof(Backends))]
    public async Task VertexIdLookupSamplesIndependentSlotAtlasesAcrossRepeatedInstances(
        SilkGraphicsBackend backend) => await Run(backend, sampling: true);

    [Test]
    [MethodDataSource(nameof(Backends))]
    public async Task PtexAndOrdinaryUvsSampleSeparatelyWithPerspectiveAndRepeatedInstances(
        SilkGraphicsBackend backend) => await Run(backend, sampling: true, mixed: true);

    [Test]
    [MethodDataSource(nameof(Backends))]
    public async Task VertexIdLookupIgnoresUnindexedPickOnlyVertices(
        SilkGraphicsBackend backend) => await Run(backend, sampling: true, mixed: true, pickOnlyTail: true);

    private static async Task Run(
        SilkGraphicsBackend backend, bool sampling, bool mixed = false, bool pickOnlyTail = false)
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_PTEX_SLOT_PROBE_EXECUTION_ADMITTED") != "1")
        {
            Skip.Test("Ptex slot transport requires a coordinated execution slot and exact compiled probe artifacts.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        SilkShaderBinaryFormat format = backend switch
        {
            SilkGraphicsBackend.D3D12 => SilkShaderBinaryFormat.Dxil,
            SilkGraphicsBackend.Vulkan => SilkShaderBinaryFormat.SpirV,
            SilkGraphicsBackend.Metal => SilkShaderBinaryFormat.MetalLibrary,
            _ => throw new ArgumentOutOfRangeException(nameof(backend)),
        };
        var (vertexCode, fragmentCode) = PtexSlotProbeAssets.Load(format, sampling, mixed);
        if (backend == SilkGraphicsBackend.Vulkan)
        {
            byte[] patched = VulkanSilkGraphicsDevice.PatchSlangDrawIndexLowering(vertexCode.Code.Span);
            await Assert.That(HasDrawParametersCapability(patched)).IsFalse()
                .Because("the zero-base Ptex draws must not require SwiftShader's " +
                    "unsupported DrawParameters capability");
        }
        using ISilkGraphicsDevice device = CreateDevice(backend);
        using ISilkGraphicsShaderModule vertex = device.CreateShaderModule(vertexCode);
        using ISilkGraphicsShaderModule fragment = device.CreateShaderModule(fragmentCode);
        using ISilkGraphicsBindingLayout layout = device.CreateBindingLayout(
            SilkPtexCandidateBinding.WithLookupSlot(SilkBindingLayoutDescriptor.SceneParameters with
            {
                MaterialSlots =
                [
                    new(0, 6, SilkBindingKind.StorageBuffer, 0, SilkShaderStageVisibility.Vertex),
                    .. (sampling ? new SilkBindingSlot[]
                    {
                        new(0, 1, SilkBindingKind.Sampler, 0, SilkShaderStageVisibility.Fragment),
                        new(0, 2, SilkBindingKind.SampledTexture, 0, SilkShaderStageVisibility.Fragment),
                        new(0, 11, SilkBindingKind.Sampler, 0, SilkShaderStageVisibility.Fragment),
                        new(0, 4, SilkBindingKind.SampledTexture, 0, SilkShaderStageVisibility.Fragment),
                    } : []),
                ]
            }));
        using ISilkGraphicsShaderProgram program = device.CreateShaderProgram(
            new SilkShaderProgramDescriptor(vertex, fragment, layout));
        using ISilkGraphicsPipeline pipeline = device.CreateGraphicsPipeline(
            new SilkGraphicsPipelineDescriptor(program, SilkVertexLayoutDescriptor.PositionNormalTexCoord,
                SilkTextureFormat.Rgba8Unorm, SilkTextureFormat.D32Float));
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            64, 64, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(64, 64));
        using ISilkGraphicsTexture? firstAtlas = sampling ? device.CreateTexture2D(
            SilkTextureDescriptor.SampledRgba8(16, 16)) : null;
        using ISilkGraphicsTexture? secondAtlas = sampling ? device.CreateTexture2D(
            SilkTextureDescriptor.SampledRgba8(16, 16)) : null;
        using ISilkGraphicsSampler? sampler = sampling ? device.CreateSampler(
            SilkSceneGpuResources.PtexMaterialSampler) : null;
        if (sampling)
        {
            byte[] firstPixels = new byte[16 * 16 * 4];
            byte[] secondPixels = new byte[firstPixels.Length];
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    int pixel = (y * 16 + x) * 4;
                    firstPixels[pixel] = checked((byte)(x * 17));
                    firstPixels[pixel + 1] = checked((byte)(y * 17));
                    firstPixels[pixel + 2] = 64;
                    firstPixels[pixel + 3] = 255;
                    secondPixels[pixel] = checked((byte)(x * 17));
                    secondPixels[pixel + 1] = mixed ? checked((byte)(y * 17)) : (byte)255;
                    secondPixels[pixel + 2] = 0;
                    secondPixels[pixel + 3] = 255;
                }
            }
            using ISilkGraphicsCommandList upload = device.CreateCommandList();
            upload.UploadTexture(firstAtlas!, firstPixels);
            upload.UploadTexture(secondAtlas!, secondPixels);
            using ISilkGraphicsSubmission submitted = device.Submit(upload);
            submitted.Wait();
        }
        float[] xs = [-0.85f, -0.15f, -0.5f, 0.15f, 0.85f, 0.5f];
        float[] ys = [-0.35f, -0.35f, 0.35f, -0.35f, -0.35f, 0.35f];
        float[] vertices = new float[(pickOnlyTail ? 8 : 6) * 8];
        uint[] words = new uint[44];
        words[0] = 6;
        words[1] = 2;
        words[2] = 2;
        words[3] = 28;
        for (int corner = 0; corner < 6; corner++)
        {
            // The mixed case keeps screen-space triangles unchanged while varying clip W.
            float w = mixed ? 1 / (1 - 0.7f * xs[corner]) : 1;
            float sourceX = xs[corner] * w;
            vertices[corner * 8] = sourceX;
            vertices[corner * 8 + 1] = ys[corner] * w;
            vertices[corner * 8 + 2] = 0.25f * w;
            vertices[corner * 8 + 5] = 1;
            // Ordinary UV values are intentionally unrelated to the lookup.
            vertices[corner * 8 + 6] = mixed ? 0.5f - 0.2f * sourceX : 0.99f;
            vertices[corner * 8 + 7] = mixed ? 0.6f - 0.15f * sourceX : 0.01f;
            int record = 4 + corner * 4;
            words[record] = (uint)(corner / 3);
            words[record + 1] = BitConverter.SingleToUInt32Bits(
                mixed ? 0.5f + 0.15f * sourceX : 0.5f + xs[corner] * 0.5f);
            words[record + 2] = BitConverter.SingleToUInt32Bits(
                mixed ? 0.4f + 0.1f * sourceX : 0.4f + xs[corner] * 0.2f);
        }
        if (mixed)
        {
            // At pixel x=50 the affine alternative must lie outside the readback
            // tolerance for either framebuffer Y orientation.
            float screenX = 50.5f / 32 - 1;
            float perspectiveX = screenX / (1 - 0.7f * screenX);
            foreach (float localY in new[] { -0.034375f, 0.034375f })
            {
                float top = (localY + 0.35f) / 0.7f;
                float right = (screenX - 0.5f * top - 0.15f * (1 - top)) / 0.7f;
                float left = 1 - top - right;
                float affineX = left * vertices[3 * 8] +
                    right * vertices[4 * 8] + top * vertices[5 * 8];
                float sampledSeparation = MathF.Abs(affineX - perspectiveX) * 0.2f * 16 / 15;
                await Assert.That(sampledSeparation).IsGreaterThan(0.02f);
            }
        }
        // slot-major, face-major: offset.xy and scale.xy.
        float[] transforms =
        [
            0.05f, 0.1f, 0.3f, 0.4f,
            0.55f, 0.2f, 0.2f, 0.5f,
            0.2f, 0.6f, 0.6f, 0.1f,
            0.1f, 0.05f, 0.7f, 0.25f,
        ];
        for (int value = 0; value < transforms.Length; value++)
        {
            words[28 + value] = BitConverter.SingleToUInt32Bits(transforms[value]);
        }
        float[] instances = new float[40];
        for (int instance = 0; instance < 2; instance++)
        {
            int offset = instance * 20;
            instances[offset] = instances[offset + 5] = instances[offset + 10] = instances[offset + 15] = 1;
            instances[offset + 7] = instance == 0 ? -0.45f : 0.45f;
            if (mixed)
            {
                // Row-major mul(M, position): translate y by translation * clip W.
                instances[offset + 4] = instances[offset + 7] * 0.7f;
                instances[offset + 12] = 0.7f;
            }
        }
        uint[] indices = [0, 1, 2, 3, 4, 5];
        using ISilkGraphicsBuffer vertexBuffer = Buffer(
            device, MemoryMarshal.AsBytes(vertices.AsSpan()), SilkBufferUsage.Vertex);
        using ISilkGraphicsBuffer indexBuffer = Buffer(
            device, MemoryMarshal.AsBytes(indices.AsSpan()), SilkBufferUsage.Index);
        using ISilkGraphicsBuffer lookup = Buffer(
            device, MemoryMarshal.AsBytes(words.AsSpan()), SilkBufferUsage.Storage);
        byte[] render(bool releaseLookupOwnerBeforeWait = false, uint? declaredLookupBytes = null)
        {
            ISilkGraphicsBuffer boundLookup = releaseLookupOwnerBeforeWait
                ? Buffer(device, MemoryMarshal.AsBytes(words.AsSpan()), SilkBufferUsage.Storage)
                : lookup;
            bool ownsLookup = releaseLookupOwnerBeforeWait;
            try
            {
                uint actualLookupBytes = checked((uint)boundLookup.Size);
                uint lookupBytes = declaredLookupBytes ?? actualLookupBytes;
                if (lookupBytes > actualLookupBytes)
                {
                    throw new InvalidDataException("The Ptex probe cannot advertise bytes beyond the bound lookup.");
                }
                for (int instance = 0; instance < 2; instance++)
                {
                    instances[instance * 20 + 16] = BitConverter.UInt32BitsToSingle(lookupBytes);
                }
                using ISilkGraphicsBuffer instanceBuffer = Buffer(
                    device, MemoryMarshal.AsBytes(instances.AsSpan()),
                    SilkBufferUsage.Storage | SilkBufferUsage.Uniform);
                ISilkGraphicsSubmission submitted;
                using (ISilkGraphicsCommandList commands = device.CreateCommandList())
                {
                    commands.ClearColor(color, new SilkColor(0, 0, 0, 0));
                    commands.ClearDepth(depth, 1);
                    commands.BeginRendering(new SilkRenderingDescriptor(color, depth));
                    commands.SetGraphicsPipeline(pipeline);
                    commands.SetViewport(new SilkViewport(0, 0, 64, 64));
                    commands.SetScissor(new SilkScissor(0, 0, 64, 64));
                    commands.SetVertexBuffer(vertexBuffer);
                    commands.SetIndexBuffer(indexBuffer);
                    commands.SetUniformBuffer(0, 0, instanceBuffer);
                    commands.SetStorageBuffer(0, 6, instanceBuffer);
                    commands.SetStorageBuffer(0, 49, boundLookup);
                    if (sampling)
                    {
                        commands.SetTexture(0, 2, firstAtlas!);
                        commands.SetTexture(0, 4, secondAtlas!);
                        commands.SetSampler(0, 1, sampler!);
                        commands.SetSampler(0, 11, sampler!);
                    }
                    commands.DrawIndexedInstanced(6, 2);
                    commands.EndRendering();
                    submitted = device.Submit(commands);
                }
                using ISilkGraphicsSubmission submission = submitted;
                if (releaseLookupOwnerBeforeWait)
                {
                    ownsLookup = false;
                    boundLookup.Dispose();
                }
                submission.Wait();
                byte[] pixels = new byte[64 * 64 * 4];
                color.ReadbackForTesting(pixels);
                return pixels;
            }
            finally
            {
                if (ownsLookup)
                {
                    boundLookup.Dispose();
                }
            }
        }
        byte[] baseline = render();
        if (pickOnlyTail)
        {
            await Assert.That(vertexBuffer.Size).IsEqualTo((nuint)(8 * 8 * sizeof(float)));
            vertices.AsSpan(6 * 8).Fill(127);
            vertexBuffer.Write(MemoryMarshal.AsBytes(vertices.AsSpan()));
            byte[] changedTail = render();
            await Assert.That(changedTail.AsSpan().SequenceEqual(baseline)).IsTrue();
        }
        foreach (int y in new[] { 16, 48 })
        {
            foreach (int x in new[] { 14, 18, 46, 50 })
            {
                float ndcX = (x + 0.5f) / 32 - 1;
                float u = 0.5f + ndcX * 0.5f;
                float v = 0.4f + ndcX * 0.2f;
                if (mixed)
                {
                    // Interpolating sourceX / clipW and 1 / clipW gives this quotient.
                    // Both vary only with screen X, so readback Y orientation is irrelevant.
                    float sourceX = ndcX / (1 - 0.7f * ndcX);
                    u = 0.5f + 0.15f * sourceX;
                    v = 0.4f + 0.1f * sourceX;
                    int transform = x < 32 ? 0 : 4;
                    float mappedU = transforms[transform] + u * transforms[transform + 2];
                    float mappedV = transforms[transform + 1] + v * transforms[transform + 3];
                    float ordinaryU = 0.5f - 0.2f * sourceX;
                    float ordinaryV = 0.6f - 0.15f * sourceX;
                    float[] expectedMixed =
                    [
                        (mappedU * 16 - 0.5f) / 15,
                        (mappedV * 16 - 0.5f) / 15,
                        (ordinaryU * 16 - 0.5f) / 15,
                        (ordinaryV * 16 - 0.5f) / 15,
                    ];
                    for (int component = 0; component < 4; component++)
                    {
                        float actual = baseline[(y * 64 + x) * 4 + component] / 255f;
                        await Assert.That(MathF.Abs(actual - expectedMixed[component])).IsLessThan(0.008f)
                            .Because($"{backend} mixed perspective sample ({x},{y}) component={component}");
                    }
                    continue;
                }
                int face = x < 32 ? 0 : 1;
                if (sampling)
                {
                    int firstTransform = face * 4;
                    int secondTransform = (2 + face) * 4;
                    float firstU = transforms[firstTransform] + u * transforms[firstTransform + 2];
                    float firstV = transforms[firstTransform + 1] + v * transforms[firstTransform + 3];
                    float secondU = transforms[secondTransform] + u * transforms[secondTransform + 2];
                    float[] expectedSample =
                    [
                        (firstU * 16 - 0.5f) / 15,
                        (firstV * 16 - 0.5f) / 15,
                        64 / 255f,
                        (secondU * 16 - 0.5f) / 15,
                    ];
                    for (int component = 0; component < 4; component++)
                    {
                        float actual = baseline[(y * 64 + x) * 4 + component] / 255f;
                        await Assert.That(MathF.Abs(actual - expectedSample[component])).IsLessThan(0.008f)
                            .Because($"{backend} independent atlas sample ({x},{y}) component={component}");
                    }
                    continue;
                }
                for (int slot = 0; slot < 2; slot++)
                {
                    int transform = (slot * 2 + face) * 4;
                    float[] expected =
                    [
                        transforms[transform] + u * transforms[transform + 2],
                        transforms[transform + 1] + v * transforms[transform + 3],
                    ];
                    for (int component = 0; component < 2; component++)
                    {
                        float actual = baseline[(y * 64 + x) * 4 + slot * 2 + component] / 255f;
                        await Assert.That(MathF.Abs(actual - expected[component])).IsLessThan(0.006f)
                            .Because($"{backend} UV transport only: ({x},{y}) slot={slot} component={component}");
                    }
                }
            }
        }
        byte[] repeated = render();
        await Assert.That(repeated.AsSpan().SequenceEqual(baseline)).IsTrue();
        byte[] submissionRetained = render(releaseLookupOwnerBeforeWait: true);
        await Assert.That(submissionRetained.AsSpan().SequenceEqual(baseline)).IsTrue()
            .Because("Disposing the lookup owner after Submit must preserve this submission's UV transport.");
        await Assert.That(() => render(declaredLookupBytes: checked((uint)lookup.Size + 1)))
            .Throws<InvalidDataException>();
        foreach (uint truncatedBytes in new[] { 15u, checked((uint)lookup.Size - sizeof(uint)) })
        {
            byte[] truncated = render(declaredLookupBytes: truncatedBytes);
            foreach (int y in new[] { 16, 48 })
            {
                foreach (int x in new[] { 14, 18, 46, 50 })
                {
                    int pixel = (y * 64 + x) * 4;
                    await Assert.That(truncated[pixel]).IsEqualTo((byte)255);
                    await Assert.That(truncated[pixel + 1]).IsEqualTo((byte)0);
                    await Assert.That(truncated[pixel + 2]).IsEqualTo((byte)255);
                    await Assert.That(truncated[pixel + 3]).IsEqualTo((byte)255);
                }
            }
        }
        // Change just the second slot, then restore it.
        words[36] = BitConverter.SingleToUInt32Bits(0.35f);
        lookup.Write(MemoryMarshal.AsBytes(words.AsSpan()));
        byte[] replaced = render();
        await Assert.That(replaced.AsSpan().SequenceEqual(baseline)).IsEqualTo(mixed);
        int firstSlotChanges = 0;
        for (int pixel = 0; pixel < 64 * 64; pixel++)
        {
            if (replaced[pixel * 4] != baseline[pixel * 4] ||
                replaced[pixel * 4 + 1] != baseline[pixel * 4 + 1] ||
                (sampling && replaced[pixel * 4 + 2] != baseline[pixel * 4 + 2]))
            {
                firstSlotChanges++;
            }
        }
        await Assert.That(firstSlotChanges).IsEqualTo(0);
        words[36] = BitConverter.SingleToUInt32Bits(transforms[8]);
        lookup.Write(MemoryMarshal.AsBytes(words.AsSpan()));
        byte[] restored = render();
        await Assert.That(restored.AsSpan().SequenceEqual(baseline)).IsTrue();
        if (mixed)
        {
            words[28] = BitConverter.SingleToUInt32Bits(transforms[0] + 0.1f);
            lookup.Write(MemoryMarshal.AsBytes(words.AsSpan()));
            byte[] changedPtex = render();
            await Assert.That(changedPtex.AsSpan().SequenceEqual(baseline)).IsFalse();
            int ordinaryChanges = 0;
            for (int pixel = 0; pixel < 64 * 64; pixel++)
            {
                if (changedPtex[pixel * 4 + 2] != baseline[pixel * 4 + 2] ||
                    changedPtex[pixel * 4 + 3] != baseline[pixel * 4 + 3])
                {
                    ordinaryChanges++;
                }
            }
            await Assert.That(ordinaryChanges).IsEqualTo(0);
            words[28] = BitConverter.SingleToUInt32Bits(transforms[0]);
            lookup.Write(MemoryMarshal.AsBytes(words.AsSpan()));
            byte[] restoredPtex = render();
            await Assert.That(restoredPtex.AsSpan().SequenceEqual(baseline)).IsTrue();
            // Alter only ordinary UVs. Ptex samples must remain byte-identical.
            for (int corner = 0; corner < 6; corner++)
            {
                vertices[corner * 8 + 6] += 0.1f;
            }
            vertexBuffer.Write(MemoryMarshal.AsBytes(vertices.AsSpan()));
            byte[] changedOrdinary = render();
            await Assert.That(changedOrdinary.AsSpan().SequenceEqual(baseline)).IsFalse();
            int ptexChanges = 0;
            for (int pixel = 0; pixel < 64 * 64; pixel++)
            {
                if (changedOrdinary[pixel * 4] != baseline[pixel * 4] ||
                    changedOrdinary[pixel * 4 + 1] != baseline[pixel * 4 + 1])
                {
                    ptexChanges++;
                }
            }
            await Assert.That(ptexChanges).IsEqualTo(0);
            for (int corner = 0; corner < 6; corner++)
            {
                float sourceX = vertices[corner * 8];
                vertices[corner * 8 + 6] = 0.5f - 0.2f * sourceX;
            }
            vertexBuffer.Write(MemoryMarshal.AsBytes(vertices.AsSpan()));
            byte[] restoredOrdinary = render();
            await Assert.That(restoredOrdinary.AsSpan().SequenceEqual(baseline)).IsTrue();
        }
        words[0] = 0;
        lookup.Write(MemoryMarshal.AsBytes(words.AsSpan()));
        byte[] invalidCorners = render();
        foreach (int y in new[] { 16, 48 })
        {
            foreach (int x in new[] { 14, 18, 46, 50 })
            {
                int pixel = (y * 64 + x) * 4;
                await Assert.That(invalidCorners[pixel]).IsEqualTo((byte)255);
                await Assert.That(invalidCorners[pixel + 1]).IsEqualTo((byte)0);
                await Assert.That(invalidCorners[pixel + 2]).IsEqualTo((byte)255);
                await Assert.That(invalidCorners[pixel + 3]).IsEqualTo((byte)255);
            }
        }
        words[0] = 6;
        lookup.Write(MemoryMarshal.AsBytes(words.AsSpan()));
        byte[] repaired = render();
        await Assert.That(repaired.AsSpan().SequenceEqual(baseline)).IsTrue();

    }

    public static IEnumerable<SilkGraphicsBackend> Backends() =>
        OperatingSystem.IsWindows() ? [SilkGraphicsBackend.D3D12, SilkGraphicsBackend.Vulkan] :
        OperatingSystem.IsLinux() ? [SilkGraphicsBackend.Vulkan] :
        OperatingSystem.IsMacOS() ? [SilkGraphicsBackend.Metal] : [];

    private static bool HasDrawParametersCapability(ReadOnlySpan<byte> code)
    {
        if (code.Length < 20 || code.Length % sizeof(uint) != 0)
        {
            throw new InvalidDataException("The Ptex vertex module is truncated.");
        }
        for (int offset = 20; offset < code.Length;)
        {
            uint first = BinaryPrimitives.ReadUInt32LittleEndian(code.Slice(offset, sizeof(uint)));
            int length = checked((int)(first >> 16) * sizeof(uint));
            if (length == 0 || length > code.Length - offset)
            {
                throw new InvalidDataException("The Ptex vertex module contains a truncated instruction.");
            }
            if ((first & 0xffff) == 17 && length == 2 * sizeof(uint) &&
                BinaryPrimitives.ReadUInt32LittleEndian(code.Slice(offset + sizeof(uint), sizeof(uint))) == 4427)
            {
                return true;
            }
            offset += length;
        }
        return false;
    }

    private static ISilkGraphicsBuffer Buffer(
        ISilkGraphicsDevice device, ReadOnlySpan<byte> bytes, SilkBufferUsage usage)
    {
        ISilkGraphicsBuffer buffer = device.CreateBuffer((nuint)bytes.Length, usage | SilkBufferUsage.Upload);
        try
        {
            buffer.Write(bytes);
            return buffer;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    private static ISilkGraphicsDevice CreateDevice(SilkGraphicsBackend backend)
    {
        if (backend == SilkGraphicsBackend.D3D12 && OperatingSystem.IsWindows())
        {
            return D3D12SilkGraphicsDevice.Create(useWarp: true);
        }
        if (backend == SilkGraphicsBackend.Metal && OperatingSystem.IsMacOS())
        {
            return MetalSilkGraphicsDevice.Create();
        }
        if (backend != SilkGraphicsBackend.Vulkan)
        {
            throw new PlatformNotSupportedException($"The {backend} backend is unavailable on this host.");
        }
        VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        if (Environment.GetEnvironmentVariable("OPENUSD_REQUIRE_SWIFTSHADER") == "1" &&
            (!device.Capabilities.IsSoftware ||
                !device.Capabilities.DeviceName.Contains("SwiftShader", StringComparison.OrdinalIgnoreCase)))
        {
            string name = device.Capabilities.DeviceName;
            device.Dispose();
            throw new InvalidOperationException($"Expected SwiftShader; selected {name}.");
        }
        return device;
    }
}
