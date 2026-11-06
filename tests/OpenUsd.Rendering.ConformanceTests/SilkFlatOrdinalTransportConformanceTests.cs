// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;
using OpenUsd.Rendering.Silk.Metal;
using OpenUsd.Rendering.Silk.Vulkan;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkFlatOrdinalTransportConformanceTests
{
    private const int Size = 64;
    private const uint Clear = uint.MaxValue;
    private const uint FlatControl = 5;
    private const string Scope =
        "These sampled rows test flat per-vertex ordinal transport in an indexed draw, not Ptex atlas lookup, " +
        "simultaneous ordinary UV/per-corner coordinate interpolation, or exhaustive ordinal correctness.";

    [Test]
    [MethodDataSource(nameof(FlatOrdinalCases))]
    public async Task SingleIndexedDrawCarriesFlatCompactOrdinalsWithoutProvingPtex(
        SilkGraphicsBackend backend,
        int firstOrdinal,
        bool varyingClipW)
    {
        SilkShaderBinaryFormat format = backend switch
        {
            SilkGraphicsBackend.D3D12 => SilkShaderBinaryFormat.Dxil,
            SilkGraphicsBackend.Vulkan => SilkShaderBinaryFormat.SpirV,
            SilkGraphicsBackend.Metal => SilkShaderBinaryFormat.MetalLibrary,
            _ => throw new ArgumentOutOfRangeException(nameof(backend))
        };
        (SilkShaderModuleDescriptor vertexDescriptor, SilkShaderModuleDescriptor fragmentDescriptor) =
            FlatOrdinalProbeShader.Load(format);

        using ISilkGraphicsDevice device = CreateDevice(backend);
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
                SilkVertexLayoutDescriptor.PositionNormalTexCoord,
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

        uint first = checked((uint)firstOrdinal);
        uint second = first + 1;
        var vertexData = new List<float>();
        AddLeftTriangle(vertexData, first, varyingClipW);
        AddRightTriangle(vertexData, second, varyingClipW);
        AddLeftTriangle(vertexData, second, varyingClipW);
        AddRightTriangle(vertexData, first, varyingClipW);
        AddVertex(vertexData, 0.46f, -0.65f, FlatControl, varyingClipW);
        AddVertex(vertexData, 0.95f, -0.65f, 65_000, varyingClipW);
        AddVertex(vertexData, 0.705f, 0.65f, FlatControl, varyingClipW);
        AddVertex(vertexData, 2.0f, -0.65f, FlatControl, varyingClipW);
        AddVertex(vertexData, 3.0f, -0.65f, FlatControl, varyingClipW);
        AddVertex(vertexData, 2.5f, 0.65f, FlatControl, varyingClipW);
        float[] vertexArray = vertexData.ToArray();
        using ISilkGraphicsBuffer vertices = device.CreateBuffer(
            checked((nuint)(vertexArray.Length * sizeof(float))),
            SilkBufferUsage.Vertex | SilkBufferUsage.Upload);
        vertices.Write(MemoryMarshal.AsBytes(vertexArray.AsSpan()));

        uint[] baselineIndices = [0, 1, 2, 3, 4, 5, 12, 13, 14];
        uint[] swappedIndices = [6, 7, 8, 9, 10, 11, 12, 13, 14];
        uint[] omittedIndices = [0, 1, 2, 15, 16, 17, 12, 13, 14];
        using ISilkGraphicsBuffer indices = device.CreateBuffer(
            checked((nuint)(baselineIndices.Length * sizeof(uint))),
            SilkBufferUsage.Index | SilkBufferUsage.Upload);
        using ISilkGraphicsBuffer uniforms = device.CreateBuffer(
            80,
            SilkBufferUsage.Uniform | SilkBufferUsage.Storage | SilkBufferUsage.Upload);
        float[] matrix = varyingClipW
            ?
            [
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 0, 0.5f,
                0, 0, 0.5f, 0,
                1, 1, 1, 1
            ]
            :
            [
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                0, 0, 0, 1,
                1, 1, 1, 1
            ];
        uniforms.Write(MemoryMarshal.AsBytes(matrix.AsSpan()));

        var baselineDepth = new float[Size * Size];
        byte[] baseline = Render(
            device, pipeline, color, depth, vertices, indices, uniforms, baselineIndices, baselineDepth);
        byte[] swapped = Render(device, pipeline, color, depth, vertices, indices, uniforms, swappedIndices);
        byte[] omitted = Render(device, pipeline, color, depth, vertices, indices, uniforms, omittedIndices);
        byte[] restored = Render(device, pipeline, color, depth, vertices, indices, uniforms, baselineIndices);

        await AssertImage(baseline, backend, firstOrdinal, varyingClipW, "baseline", first, second);
        await AssertImage(swapped, backend, firstOrdinal, varyingClipW, "swapped", second, first);
        await AssertImage(omitted, backend, firstOrdinal, varyingClipW, "omitted", first, Clear);
        await AssertImage(restored, backend, firstOrdinal, varyingClipW, "restored", first, second);
        float leftDepth = baselineDepth[(32 * Size) + 13];
        float rightDepth = baselineDepth[(32 * Size) + 35];
        string depthContext =
            $"{backend} firstOrdinal={firstOrdinal} varyingClipW={varyingClipW} " +
            $"leftDepth={leftDepth} rightDepth={rightDepth}. {Scope}";
        await Assert.That(leftDepth).IsGreaterThan(varyingClipW ? 0.57f : 0.249f).Because(depthContext);
        await Assert.That(leftDepth).IsLessThan(varyingClipW ? 0.63f : 0.251f).Because(depthContext);
        await Assert.That(rightDepth).IsGreaterThan(varyingClipW ? 0.47f : 0.249f).Because(depthContext);
        await Assert.That(rightDepth).IsLessThan(varyingClipW ? 0.52f : 0.251f).Because(depthContext);
        await Assert.That(swapped.AsSpan().SequenceEqual(baseline)).IsFalse()
            .Because($"Swapping the two visible ordinal assignments must change the image. {Scope}");
        await Assert.That(omitted.AsSpan().SequenceEqual(baseline)).IsFalse()
            .Because($"Clipping the second visible triangle must change the image. {Scope}");
        await Assert.That(restored.AsSpan().SequenceEqual(baseline)).IsTrue()
            .Because($"Restoring the original draw must restore every RGBA8 byte. {Scope}");
    }

    public static IEnumerable<(SilkGraphicsBackend, int, bool)> FlatOrdinalCases()
    {
        SilkGraphicsBackend[] backends = OperatingSystem.IsWindows()
            ? [SilkGraphicsBackend.D3D12, SilkGraphicsBackend.Vulkan]
            : OperatingSystem.IsLinux()
                ? [SilkGraphicsBackend.Vulkan]
                : OperatingSystem.IsMacOS()
                    ? [SilkGraphicsBackend.Metal]
                    : throw new InvalidOperationException(
                        "The flat-ordinal conformance probe requires Windows, Linux, or macOS.");
        foreach (SilkGraphicsBackend backend in backends)
        {
            foreach (int first in new[] { 255, 32_767, 65_534 })
            {
                yield return (backend, first, false);
                yield return (backend, first, true);
            }
        }
    }

    private static void AddLeftTriangle(List<float> vertices, uint ordinal, bool varyingClipW)
    {
        AddVertex(vertices, -0.95f, -0.65f, ordinal, varyingClipW);
        AddVertex(vertices, 0.30f, 0.65f, ordinal, varyingClipW);
        AddVertex(vertices, -0.95f, 0.65f, ordinal, varyingClipW);
    }

    private static void AddRightTriangle(List<float> vertices, uint ordinal, bool varyingClipW)
    {
        AddVertex(vertices, -0.95f, -0.65f, ordinal, varyingClipW);
        AddVertex(vertices, 0.30f, -0.65f, ordinal, varyingClipW);
        AddVertex(vertices, 0.30f, 0.65f, ordinal, varyingClipW);
    }

    private static void AddVertex(
        List<float> vertices, float x, float y, uint ordinal, bool varyingClipW)
    {
        float w = varyingClipW ? 1 + (0.25f * x) : 1;
        vertices.Add(x * w);
        vertices.Add(y * w);
        vertices.Add(varyingClipW ? 2 * w : 0.25f);
        vertices.Add(0);
        vertices.Add(0);
        vertices.Add(1);
        vertices.Add(ordinal);
        vertices.Add(0);
    }

    private static byte[] Render(
        ISilkGraphicsDevice device,
        ISilkGraphicsPipeline pipeline,
        ISilkGraphicsTexture color,
        ISilkGraphicsTexture depth,
        ISilkGraphicsBuffer vertices,
        ISilkGraphicsBuffer indices,
        ISilkGraphicsBuffer uniforms,
        uint[] indexData,
        float[]? depthPixels = null)
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
        if (depthPixels is not null)
        {
            depth.ReadbackForTesting(depthPixels);
        }
        return rgba;
    }

    private static async Task AssertImage(
        byte[] rgba,
        SilkGraphicsBackend backend,
        int firstOrdinal,
        bool varyingClipW,
        string capture,
        uint left,
        uint right)
    {
        string context = $"{backend} firstOrdinal={firstOrdinal} varyingClipW={varyingClipW} capture={capture}";
        await AssertPixel(rgba, context, 13, 32, left);
        await AssertPixel(rgba, context, 35, 32, right);
        await AssertPixel(rgba, context, 52, 32, FlatControl);
        await AssertPixel(rgba, context, 56, 32, FlatControl);
        await AssertPixel(rgba, context, 32, 2, Clear);

        int leftCount = 0;
        int rightCount = 0;
        int flatCount = 0;
        int unexpectedCount = 0;
        string firstUnexpected = string.Empty;
        for (int pixel = 0; pixel < Size * Size; pixel++)
        {
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(rgba.AsSpan(pixel * 4, 4));
            if (value == left)
            {
                leftCount++;
            }
            else if (value == right && right != Clear)
            {
                rightCount++;
            }
            else if (value == FlatControl)
            {
                flatCount++;
            }
            else if (value != Clear)
            {
                unexpectedCount++;
                if (firstUnexpected.Length == 0)
                {
                    firstUnexpected = $"pixel=({pixel % Size},{pixel / Size}) RGBA=0x{value:X8}";
                }
            }
        }
        await Assert.That(unexpectedCount).IsEqualTo(0).Because(
            $"{context} has {unexpectedCount} unexpected RGBA8 values; first {firstUnexpected}. {Scope}");
        await Assert.That(leftCount).IsGreaterThan(10).Because($"{context} must cover the left triangle. {Scope}");
        if (right != Clear)
        {
            await Assert.That(rightCount).IsGreaterThan(10)
                .Because($"{context} must cover the right triangle. {Scope}");
        }
        await Assert.That(flatCount).IsGreaterThan(10).Because(
            $"{context} must cover the flat control whose middle vertex has a different ordinal. {Scope}");
    }

    private static async Task AssertPixel(byte[] rgba, string context, int x, int y, uint expected)
    {
        int offset = checked(((y * Size) + x) * 4);
        uint actual = BinaryPrimitives.ReadUInt32LittleEndian(rgba.AsSpan(offset, 4));
        await Assert.That(actual).IsEqualTo(expected).Because(
            $"{context} pixel=({x},{y}) RGBA={Convert.ToHexString(rgba.AsSpan(offset, 4))} " +
            $"expected=0x{expected:X8}. {Scope}");
    }

    private static ISilkGraphicsDevice CreateDevice(SilkGraphicsBackend backend)
    {
        if (backend == SilkGraphicsBackend.D3D12 && OperatingSystem.IsWindows())
        {
            return D3D12SilkGraphicsDevice.Create(useWarp: true);
        }
        if (backend == SilkGraphicsBackend.Vulkan &&
            (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()))
        {
            VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
            if (Environment.GetEnvironmentVariable("OPENUSD_REQUIRE_SWIFTSHADER") == "1" &&
                (!device.Capabilities.IsSoftware ||
                    !device.Capabilities.DeviceName.Contains("SwiftShader", StringComparison.OrdinalIgnoreCase)))
            {
                string deviceName = device.Capabilities.DeviceName;
                device.Dispose();
                throw new InvalidOperationException($"Expected SwiftShader, but Vulkan selected '{deviceName}'.");
            }
            return device;
        }
        if (backend == SilkGraphicsBackend.Metal && OperatingSystem.IsMacOS())
        {
            return MetalSilkGraphicsDevice.Create();
        }
        throw new InvalidOperationException(
            $"{backend} is unavailable for the flat-ordinal conformance probe on this operating system.");
    }
}
