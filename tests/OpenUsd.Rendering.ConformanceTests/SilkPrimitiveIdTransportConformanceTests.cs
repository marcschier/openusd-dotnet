// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;
using OpenUsd.Rendering.Silk.Metal;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkPrimitiveIdTransportConformanceTests
{
    private const int Size = 64;
    private const uint Clear = uint.MaxValue;
    private const string Scope =
        "This proves only SV_PrimitiveID transport, not Ptex atlas lookup or per-corner interpolation.";

    [Test]
    [MethodDataSource(nameof(PrimitiveIdCases))]
    public async Task SingleIndexedDrawPreservesPrimitiveIdsWithoutProvingPtex(
        SilkGraphicsBackend backend,
        int boundary)
    {
        SilkShaderBinaryFormat format = backend switch
        {
            SilkGraphicsBackend.D3D12 => SilkShaderBinaryFormat.Dxil,
            SilkGraphicsBackend.Vulkan => SilkShaderBinaryFormat.SpirV,
            SilkGraphicsBackend.Metal => SilkShaderBinaryFormat.MetalLibrary,
            _ => throw new ArgumentOutOfRangeException(nameof(backend))
        };
        SilkShaderModuleDescriptor vertexDescriptor = SilkCheckedShaderAssets.LoadPickVertex(format);
        SilkShaderModuleDescriptor fragmentDescriptor = PrimitiveIdProbeShader.Load(format, vertexDescriptor);

        using ProbeDevice owner = ProbeDevice.Create(backend);
        ISilkGraphicsDevice device = owner.Device;
        using ISilkGraphicsShaderModule vertex = device.CreateShaderModule(vertexDescriptor);
        using ISilkGraphicsShaderModule fragment = device.CreateShaderModule(fragmentDescriptor);
        SilkBindingLayoutDescriptor layoutDescriptor = SilkBindingLayoutDescriptor.SceneParameters with
        {
            MaterialSlots =
            [
                new(0, 6, SilkBindingKind.StorageBuffer, 0, SilkShaderStageVisibility.Vertex)
            ]
        };
        using ISilkGraphicsBindingLayout layout = device.CreateBindingLayout(layoutDescriptor);
        using ISilkGraphicsShaderProgram program = device.CreateShaderProgram(
            new SilkShaderProgramDescriptor(vertex, fragment, layout));
        using ISilkGraphicsPipeline pipeline = device.CreateGraphicsPipeline(
            new SilkGraphicsPipelineDescriptor(
                program,
                SilkVertexLayoutDescriptor.PositionNormal,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureFormat.D32Float));
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            new SilkTextureDescriptor(
                Size,
                Size,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));

        float[] vertexData =
        [
             2.00f, -0.65f, 0.25f, 0, 0, 1,
             3.00f, -0.65f, 0.25f, 0, 0, 1,
             2.50f,  0.65f, 0.25f, 0, 0, 1,
            -0.95f, -0.65f, 0.25f, 0, 0, 1,
            -0.55f, -0.65f, 0.25f, 0, 0, 1,
            -0.75f,  0.65f, 0.25f, 0, 0, 1,
            -0.45f, -0.65f, 0.25f, 0, 0, 1,
            -0.05f, -0.65f, 0.25f, 0, 0, 1,
            -0.25f,  0.65f, 0.25f, 0, 0, 1,
             0.05f, -0.65f, 0.25f, 0, 0, 1,
             0.45f, -0.65f, 0.25f, 0, 0, 1,
             0.25f,  0.65f, 0.25f, 0, 0, 1,
             0.55f, -0.65f, 0.25f, 0, 0, 1,
             0.95f, -0.65f, 0.25f, 0, 0, 1,
             0.75f,  0.65f, 0.25f, 0, 0, 1
        ];
        using ISilkGraphicsBuffer vertices = device.CreateBuffer(
            checked((nuint)(vertexData.Length * sizeof(float))),
            SilkBufferUsage.Vertex | SilkBufferUsage.Upload);
        vertices.Write(MemoryMarshal.AsBytes(vertexData.AsSpan()));

        // Nondegenerate clipped triangles retain their input-assembly primitive positions.
        var indexData = new uint[checked((boundary + 2) * 3)];
        for (int primitive = 0; primitive < boundary + 2; primitive++)
        {
            SetTriangle(indexData, primitive, 0);
        }
        SetTriangle(indexData, 0, 3);
        SetTriangle(indexData, boundary - 1, 6);
        SetTriangle(indexData, boundary, 9);
        SetTriangle(indexData, boundary + 1, 12);
        using ISilkGraphicsBuffer indices = device.CreateBuffer(
            checked((nuint)(indexData.Length * sizeof(uint))),
            SilkBufferUsage.Index | SilkBufferUsage.Upload);
        using ISilkGraphicsBuffer uniforms = device.CreateBuffer(
            80,
            SilkBufferUsage.Uniform | SilkBufferUsage.Storage | SilkBufferUsage.Upload);
        uniforms.Write(MemoryMarshal.AsBytes<float>(
        [
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1,
            1, 1, 1, 1
        ]));

        byte[] baseline = Render(device, pipeline, color, depth, vertices, indices, uniforms, indexData);
        SetTriangle(indexData, boundary - 1, 9);
        SetTriangle(indexData, boundary, 6);
        byte[] swapped = Render(device, pipeline, color, depth, vertices, indices, uniforms, indexData);
        SetTriangle(indexData, boundary - 1, 6);
        SetTriangle(indexData, boundary, 0);
        byte[] omitted = Render(device, pipeline, color, depth, vertices, indices, uniforms, indexData);
        SetTriangle(indexData, boundary, 9);
        byte[] restored = Render(device, pipeline, color, depth, vertices, indices, uniforms, indexData);

        uint id = checked((uint)boundary);
        await AssertImagePixels(baseline, backend, boundary, "baseline", 0, id - 1, id, id + 1);
        await AssertImagePixels(swapped, backend, boundary, "swapped", 0, id, id - 1, id + 1);
        await AssertImagePixels(omitted, backend, boundary, "omitted", 0, id - 1, Clear, id + 1);
        await AssertImagePixels(restored, backend, boundary, "restored", 0, id - 1, id, id + 1);
        await Assert.That(swapped.AsSpan().SequenceEqual(baseline)).IsFalse()
            .Because($"Swapping visible index order must change the primitive-ID image. {Scope}");
        await Assert.That(omitted.AsSpan().SequenceEqual(baseline)).IsFalse()
            .Because($"Omitting the visible target must change the primitive-ID image. {Scope}");
        await Assert.That(restored.AsSpan().SequenceEqual(baseline)).IsTrue()
            .Because($"Restoring the original indexed draw must restore every RGBA8 byte. {Scope}");
    }

    public static IEnumerable<(SilkGraphicsBackend, int)> PrimitiveIdCases()
    {
        SilkGraphicsBackend[] backends = OperatingSystem.IsWindows()
            ? [SilkGraphicsBackend.D3D12, SilkGraphicsBackend.Vulkan]
            : OperatingSystem.IsLinux()
                ? [SilkGraphicsBackend.Vulkan]
                : OperatingSystem.IsMacOS()
                    ? [SilkGraphicsBackend.Metal]
                    : throw new InvalidOperationException(
                        "The primitive-ID conformance probe requires Windows, Linux, or macOS.");
        string? selected = Environment.GetEnvironmentVariable("OPENUSD_PRIMITIVE_PROBE_BACKEND");
        if (selected is not null)
        {
            SilkGraphicsBackend requested = selected switch
            {
                "D3D12" => SilkGraphicsBackend.D3D12,
                "Vulkan" => SilkGraphicsBackend.Vulkan,
                "Metal" => SilkGraphicsBackend.Metal,
                _ => throw new InvalidOperationException(
                    $"Unknown primitive-ID probe backend '{selected}'.")
            };
            if (!backends.Contains(requested))
            {
                throw new InvalidOperationException(
                    $"The primitive-ID probe backend '{selected}' is unavailable on this operating system.");
            }
            backends = [requested];
        }
        foreach (SilkGraphicsBackend backend in backends)
        {
            foreach (int boundary in new[] { 255, 32_767, 65_535 })
            {
                yield return (backend, boundary);
            }
        }
    }

    private static void SetTriangle(uint[] indices, int primitive, uint firstVertex)
    {
        int offset = checked(primitive * 3);
        indices[offset] = firstVertex;
        indices[offset + 1] = firstVertex + 1;
        indices[offset + 2] = firstVertex + 2;
    }

    private static byte[] Render(
        ISilkGraphicsDevice device,
        ISilkGraphicsPipeline pipeline,
        ISilkGraphicsTexture color,
        ISilkGraphicsTexture depth,
        ISilkGraphicsBuffer vertices,
        ISilkGraphicsBuffer indices,
        ISilkGraphicsBuffer uniforms,
        uint[] indexData)
    {
        indices.Write(MemoryMarshal.AsBytes(indexData.AsSpan()));
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.ClearColor(color, new SilkColor(1, 1, 1, 1));
        commands.ClearDepth(depth, 1);
        commands.BeginRendering(new SilkRenderingDescriptor(color, depth));
        commands.SetGraphicsPipeline(pipeline);
        commands.SetViewport(new SilkViewport(0, 0, Size, Size));
        commands.SetScissor(new SilkScissor(0, 0, Size, Size));
        commands.SetVertexBuffer(vertices);
        commands.SetIndexBuffer(indices);
        commands.SetUniformBuffer(0, 0, uniforms);
        commands.SetStorageBuffer(0, 6, uniforms);
        commands.DrawIndexed(checked((uint)indexData.Length));
        commands.EndRendering();
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        submission.Wait();
        var rgba = new byte[Size * Size * 4];
        color.ReadbackForTesting(rgba);
        return rgba;
    }

    private static async Task AssertImagePixels(
        byte[] rgba,
        SilkGraphicsBackend backend,
        int boundary,
        string capture,
        uint first,
        uint before,
        uint target,
        uint after)
    {
        await AssertPixel(rgba, backend, boundary, capture, 8, 32, first);
        await AssertPixel(rgba, backend, boundary, capture, 24, 32, before);
        await AssertPixel(rgba, backend, boundary, capture, 40, 32, target);
        await AssertPixel(rgba, backend, boundary, capture, 56, 32, after);
        await AssertPixel(rgba, backend, boundary, capture, 32, 2, Clear);
    }

    private static async Task AssertPixel(
        byte[] rgba,
        SilkGraphicsBackend backend,
        int boundary,
        string capture,
        int x,
        int y,
        uint expected)
    {
        int offset = checked(((y * Size) + x) * 4);
        uint actual = BinaryPrimitives.ReadUInt32LittleEndian(rgba.AsSpan(offset, 4));
        string raw = Convert.ToHexString(rgba.AsSpan(offset, 4));
        string featureScope = backend == SilkGraphicsBackend.Vulkan
            ? " Vulkan uses a test-owned borrowed device with geometryShader enabled, not the default device."
            : string.Empty;
        await Assert.That(actual).IsEqualTo(expected).Because(
            $"{backend} boundary={boundary} capture={capture} pixel=({x},{y}) " +
            $"RGBA={raw} expected ID=0x{expected:X8}. {Scope}{featureScope}");
    }

    private sealed class ProbeDevice(ISilkGraphicsDevice device, IDisposable owner) : IDisposable
    {
        internal ISilkGraphicsDevice Device { get; } = device;

        internal static ProbeDevice Create(SilkGraphicsBackend backend)
        {
            if (backend == SilkGraphicsBackend.D3D12 && OperatingSystem.IsWindows())
            {
                ISilkGraphicsDevice d3d12 = D3D12SilkGraphicsDevice.Create(useWarp: true);
                return new ProbeDevice(d3d12, d3d12);
            }
            if (backend == SilkGraphicsBackend.Vulkan &&
                (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()))
            {
                PrimitiveIdProbeVulkanDevice vulkan = PrimitiveIdProbeVulkanDevice.Create();
                return new ProbeDevice(vulkan.Device, vulkan);
            }
            if (backend == SilkGraphicsBackend.Metal && OperatingSystem.IsMacOS())
            {
                ISilkGraphicsDevice metal = MetalSilkGraphicsDevice.Create();
                return new ProbeDevice(metal, metal);
            }
            throw new InvalidOperationException(
                $"{backend} is unavailable for the primitive-ID conformance probe on this operating system.");
        }

        public void Dispose() => owner.Dispose();
    }
}
