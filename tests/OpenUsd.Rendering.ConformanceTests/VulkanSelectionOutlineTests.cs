// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.Vulkan;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class VulkanSelectionOutlineTests
{
    [Test]
    public async Task SwiftShaderManualMaskAndOutlineWritePixels()
    {
        if (!IsSupportedPlatform())
        {
            return;
        }

        const uint size = 32;
        using VulkanSilkGraphicsDevice device = CreateSwiftShader();
        if (!IsSwiftShader(device))
        {
            return;
        }
        using ISilkSelectionMaskGraphicsPipeline maskPipeline =
            device.CreateSelectionMaskGraphicsPipeline(
                SilkSelectionMaskPipelineDescriptor.CreateChecked(
                    SilkShaderBinaryFormat.SpirV));
        using ISilkSelectionOutlineGraphicsPipeline outlinePipeline =
            device.CreateSelectionOutlineGraphicsPipeline(
                SilkSelectionOutlinePipelineDescriptor.CreateChecked(
                    SilkShaderBinaryFormat.SpirV));
        using ISilkGraphicsTexture color = CreateColorTarget(device, size, size);
        using ISilkGraphicsTexture depth = CreateDepthTarget(device, size, size);
        using ISilkGraphicsTexture mask = device.CreateTexture2D(
            SilkTextureDescriptor.SelectionMask(size, size));
        using ISilkGraphicsSampler sampler =
            device.CreateSampler(SilkSamplerDescriptor.NearestClamp);
        using ISilkGraphicsBuffer parameters = device.CreateBuffer(
            SilkSelectionOutlineUniformWriter.ByteSize,
            SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
        Span<byte> parameterBytes =
            stackalloc byte[SilkSelectionOutlineUniformWriter.ByteSize];
        SilkSelectionOutlineUniformWriter.Write(
            SilkSelectionOutlineSettings.Default,
            size,
            size,
            parameterBytes);
        parameters.Write(parameterBytes);
        using ISilkSelectionOutlineBinding binding =
            device.CreateSelectionOutlineBinding(new(
                mask,
                depth,
                sampler,
                parameters));
        using ISilkGraphicsBuffer vertices = CreateTriangleVertices(device);
        using ISilkGraphicsBuffer indices = CreateTriangleIndices(device);
        using ISilkGraphicsBuffer scene = CreateIdentitySceneParameters(device);
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        var selection = (ISilkSelectionOutlineGraphicsCommandList)commands;
        commands.ClearColor(color, new SilkColor(0, 0, 0, 1));
        commands.ClearDepth(depth, 1);
        commands.ClearColor(mask, new SilkColor(0, 0, 0, 0));
        selection.BeginSelectionMaskRendering(new(mask, depth));
        selection.SetSelectionMaskGraphicsPipeline(maskPipeline);
        commands.SetViewport(new SilkViewport(0, 0, size, size));
        commands.SetScissor(new SilkScissor(0, 0, size, size));
        commands.SetVertexBuffer(vertices);
        commands.SetIndexBuffer(indices);
        commands.SetUniformBuffer(0, 0, scene);
        commands.DrawIndexed(3);
        commands.EndRendering();
        selection.BeginSelectionOutlineRendering(new(color));
        selection.SetSelectionOutlineGraphicsPipeline(outlinePipeline);
        selection.SetSelectionOutlineBinding(binding);
        commands.SetViewport(new SilkViewport(0, 0, size, size));
        commands.SetScissor(new SilkScissor(0, 0, size, size));
        selection.DrawSelectionOutlineFullscreenTriangle();
        commands.EndRendering();
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        submission.Wait();

        byte[] maskPixels = ReadPixels(mask);
        byte[] outlinePixels = ReadPixels(color);
        await Assert.That(CountWhitePixels(maskPixels)).IsGreaterThan(0);
        await Assert.That(CountOutlinePixels(outlinePixels)).IsGreaterThan(0);
    }

    [Test]
    public async Task SwiftShaderRendersOutlineAndRestoresExactBaseline()
    {
        if (!IsSupportedPlatform())
        {
            return;
        }

        const uint size = 64;
        using VulkanSilkGraphicsDevice device = CreateSwiftShader();
        if (!IsSwiftShader(device))
        {
            return;
        }
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColorTarget(device, size, size);
        using ISilkGraphicsTexture depth = CreateDepthTarget(device, size, size);
        Apply(
            renderer,
            size,
            size,
            CreateQuad(1, "/Selected", 0.25f, -0.45f, 0.45f, 0.45f, -0.45f));

        _ = renderer.Render(color, depth);
        byte[] baseline = ReadPixels(color);
        renderer.UpdateSelection(new SelectionState(["/Selected"]));
        _ = renderer.Render(color, depth);
        byte[] outlined = ReadPixels(color);
        renderer.UpdateSelection(SelectionState.Empty);
        _ = renderer.Render(color, depth);
        byte[] cleared = ReadPixels(color);

        await Assert.That(outlined.AsSpan().SequenceEqual(baseline)).IsFalse();
        await Assert.That(CountOutlinePixels(outlined))
            .IsGreaterThan(CountOutlinePixels(baseline));
        await Assert.That(cleared.AsSpan().SequenceEqual(baseline)).IsTrue();
        await Assert.That(renderer.SelectionOutlineDiagnostics.Status)
            .IsEqualTo(SilkSelectionOutlineStatus.EmptySelection);
    }

    [Test]
    public async Task SwiftShaderSuppressesOcclusionAndUsesPhysicalWidth()
    {
        if (!IsSupportedPlatform())
        {
            return;
        }

        const uint size = 80;
        using VulkanSilkGraphicsDevice device = CreateSwiftShader();
        if (!IsSwiftShader(device))
        {
            return;
        }
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColorTarget(device, size, size);
        using ISilkGraphicsTexture depth = CreateDepthTarget(device, size, size);
        Apply(
            renderer,
            size,
            size,
            CreateQuad(1, "/Far", 0.7f, -0.45f, 0.45f, 0.45f, -0.45f),
            CreateQuad(2, "/Near", 0.2f, -0.55f, 0.55f, 0.55f, -0.55f));
        renderer.UpdateSelection(new SelectionState(["/Far"]));
        _ = renderer.Render(color, depth);
        int occludedOutlinePixels = CountOutlinePixels(ReadPixels(color));

        var narrow = new SilkSelectionOutlineSettings(
            true,
            SilkSelectionOutlineSettings.Default.Color,
            1,
            visibleOnly: true);
        renderer.UpdateSelection(new SelectionState(["/Near"]), narrow);
        _ = renderer.Render(color, depth);
        int narrowPixels = CountOutlinePixels(ReadPixels(color));

        var wide = new SilkSelectionOutlineSettings(
            true,
            SilkSelectionOutlineSettings.Default.Color,
            5,
            visibleOnly: true);
        renderer.UpdateSelection(renderer.Selection, wide);
        _ = renderer.Render(color, depth);
        int widePixels = CountOutlinePixels(ReadPixels(color));

        await Assert.That(occludedOutlinePixels).IsEqualTo(0);
        await Assert.That(narrowPixels).IsGreaterThan(0);
        await Assert.That(widePixels).IsGreaterThan(narrowPixels);
    }

    [Test]
    public async Task SwiftShaderRecreatesResizeDepthAndGenerationResources()
    {
        if (!IsSupportedPlatform())
        {
            return;
        }

        using VulkanSilkGraphicsDevice device = CreateSwiftShader();
        if (!IsSwiftShader(device))
        {
            return;
        }
        using var renderer = new SilkMeshRenderer(device);
        Apply(
            renderer,
            48,
            48,
            CreateQuad(1, "/Selected", 0.2f, -0.5f, 0.5f, 0.5f, -0.5f));
        renderer.UpdateSelection(new SelectionState(["/Selected"]));
        using ISilkGraphicsTexture color = CreateColorTarget(device, 48, 48);
        using ISilkGraphicsTexture depth = CreateDepthTarget(device, 48, 48);
        _ = renderer.Render(color, depth);
        VulkanSilkSelectionOutlineDiagnostics first =
            device.SelectionOutlineDiagnostics;

        using ISilkGraphicsTexture replacementDepth =
            CreateDepthTarget(device, 48, 48);
        _ = renderer.Render(color, replacementDepth);
        VulkanSilkSelectionOutlineDiagnostics depthReplaced =
            device.SelectionOutlineDiagnostics;
        await Assert.That(depthReplaced.BindingCreations)
            .IsEqualTo(first.BindingCreations + 1);
        await Assert.That(depthReplaced.MaskPipelineCreations)
            .IsEqualTo(first.MaskPipelineCreations);

        using ISilkGraphicsTexture resizedColor = CreateColorTarget(device, 72, 40);
        using ISilkGraphicsTexture resizedDepth = CreateDepthTarget(device, 72, 40);
        _ = renderer.Render(resizedColor, resizedDepth);
        VulkanSilkSelectionOutlineDiagnostics resized =
            device.SelectionOutlineDiagnostics;
        await Assert.That(resized.BindingCreations)
            .IsEqualTo(depthReplaced.BindingCreations + 1);
        await Assert.That(resized.MaskPipelineCreations).IsEqualTo(1L);
        await Assert.That(resized.OutlinePipelineCreations).IsEqualTo(1L);

        _ = renderer.Render(resizedColor, resizedDepth);
        VulkanSilkSelectionOutlineDiagnostics warm =
            device.SelectionOutlineDiagnostics;
        await Assert.That(warm.BindingCreations).IsEqualTo(resized.BindingCreations);
        await Assert.That(warm.FramebufferCreations)
            .IsEqualTo(resized.FramebufferCreations);
        await Assert.That(warm.DescriptorSetCreations)
            .IsEqualTo(resized.DescriptorSetCreations);

        device.AdvanceSelectionOutlineDeviceGenerationForTesting();
        _ = renderer.Render(resizedColor, resizedDepth);
        VulkanSilkSelectionOutlineDiagnostics invalidated =
            device.SelectionOutlineDiagnostics;
        await Assert.That(invalidated.MaskPipelineCreations).IsEqualTo(2L);
        await Assert.That(invalidated.OutlinePipelineCreations).IsEqualTo(2L);
        await Assert.That(renderer.SelectionOutlineDiagnostics.DeviceInvalidations)
            .IsEqualTo(1UL);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SwiftShaderSubmissionAndFenceFailuresInvalidateAndClean(
        bool fenceFailure,
        bool deviceLost)
    {
        if (!IsSupportedPlatform())
        {
            return;
        }

        VulkanSilkGraphicsDevice device = CreateSwiftShader();
        if (!IsSwiftShader(device))
        {
            device.Dispose();
            return;
        }
        var renderer = new SilkMeshRenderer(device);
        ISilkGraphicsTexture color = CreateColorTarget(device, 32, 32);
        ISilkGraphicsTexture depth = CreateDepthTarget(device, 32, 32);
        try
        {
            Apply(
                renderer,
                32,
                32,
                CreateQuad(1, "/Selected", 0.2f, -0.5f, 0.5f, 0.5f, -0.5f));
            renderer.UpdateSelection(new SelectionState(["/Selected"]));
            _ = renderer.Render(color, depth);
            ulong generation = device.SelectionOutlineDeviceGeneration;

            if (fenceFailure)
            {
                device.FailNextSelectionOutlineFenceForTesting(deviceLost);
            }
            else
            {
                device.FailNextSelectionOutlineSubmissionForTesting(
                    deviceLost);
            }
            await Assert.That(() => renderer.Render(color, depth))
                .Throws<InvalidOperationException>();
            await Assert.That(device.SelectionOutlineDeviceGeneration)
                .IsEqualTo(deviceLost ? generation + 1 : generation);

            _ = renderer.Render(color, depth);
            await Assert.That(device.SelectionOutlineDiagnostics.DeviceLosses)
                .IsEqualTo(deviceLost ? 1L : 0L);
        }
        finally
        {
            renderer.Dispose();
            color.Dispose();
            depth.Dispose();
            await Assert.That(device.SelectionOutlineDiagnostics.LiveDependentObjects)
                .IsEqualTo(0L);
            device.Dispose();
        }
    }

    private static bool IsSupportedPlatform() =>
        OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    private static VulkanSilkGraphicsDevice CreateSwiftShader()
        => VulkanSilkGraphicsDevice.Create();

    private static bool IsSwiftShader(VulkanSilkGraphicsDevice device) =>
        device.Capabilities.IsSoftware &&
        device.Capabilities.DeviceName.Contains(
            "SwiftShader",
            StringComparison.OrdinalIgnoreCase);

    private static ISilkGraphicsTexture CreateColorTarget(
        VulkanSilkGraphicsDevice device,
        uint width,
        uint height) =>
        device.CreateTexture2D(new SilkTextureDescriptor(
            width,
            height,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));

    private static ISilkGraphicsTexture CreateDepthTarget(
        VulkanSilkGraphicsDevice device,
        uint width,
        uint height) =>
        device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(width, height));

    private static byte[] ReadPixels(ISilkGraphicsTexture texture)
    {
        var pixels = new byte[checked((int)(texture.Width * texture.Height * 4))];
        texture.ReadbackForTesting(pixels);
        return pixels;
    }

    private static int CountOutlinePixels(ReadOnlySpan<byte> pixels)
    {
        int count = 0;
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset] > 150 &&
                pixels[offset] > pixels[offset + 1] + 25 &&
                pixels[offset + 1] > pixels[offset + 2])
            {
                count++;
            }
        }
        return count;
    }

    private static int CountWhitePixels(ReadOnlySpan<byte> pixels)
    {
        int count = 0;
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset] > 200 &&
                pixels[offset + 1] > 200 &&
                pixels[offset + 2] > 200)
            {
                count++;
            }
        }
        return count;
    }

    private static ISilkGraphicsBuffer CreateTriangleVertices(
        VulkanSilkGraphicsDevice device)
    {
        ISilkGraphicsBuffer buffer = device.CreateBuffer(
            72,
            SilkBufferUsage.Vertex | SilkBufferUsage.Upload);
        buffer.Write(MemoryMarshal.AsBytes<float>(
        [
            -0.6f, -0.6f, 0.25f, 0, 0, 1,
             0.0f,  0.6f, 0.25f, 0, 0, 1,
             0.6f, -0.6f, 0.25f, 0, 0, 1
        ]));
        return buffer;
    }

    private static ISilkGraphicsBuffer CreateTriangleIndices(
        VulkanSilkGraphicsDevice device)
    {
        ISilkGraphicsBuffer buffer = device.CreateBuffer(
            12,
            SilkBufferUsage.Index | SilkBufferUsage.Upload);
        buffer.Write(MemoryMarshal.AsBytes<uint>([0, 1, 2]));
        return buffer;
    }

    private static ISilkGraphicsBuffer CreateIdentitySceneParameters(
        VulkanSilkGraphicsDevice device)
    {
        ISilkGraphicsBuffer buffer = device.CreateBuffer(
            80,
            SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
        buffer.Write(MemoryMarshal.AsBytes<float>(
        [
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1,
            1, 1, 1, 1
        ]));
        return buffer;
    }

    private static void Apply(
        SilkMeshRenderer renderer,
        uint width,
        uint height,
        params byte[][] meshes)
    {
        byte[] frame = CreateFrame(width, height);
        int length = frame.Length + meshes.Sum(mesh => mesh.Length);
        var page = new byte[length];
        frame.CopyTo(page, 0);
        int offset = frame.Length;
        foreach (byte[] mesh in meshes)
        {
            mesh.CopyTo(page, offset);
            offset += mesh.Length;
        }
        SilkSceneDelta delta = renderer.Scene.Apply(
            page,
            checked((uint)(meshes.Length + 1)),
            revision: 1);
        renderer.GpuResources.Apply(renderer.Scene, delta);
    }

    private static byte[] CreateFrame(uint width, uint height)
    {
        var bytes = new byte[272];
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes,
            (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(4),
            checked((uint)bytes.Length));
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(8),
            checked((int)width));
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(12),
            checked((int)height));
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

    private static byte[] CreateQuad(
        ulong id,
        string pathValue,
        float z,
        float left,
        float top,
        float right,
        float bottom)
    {
        byte[] path = Encoding.UTF8.GetBytes(pathValue);
        float[] points =
        [
            left, top, z,
            right, top, z,
            right, bottom, z,
            left, bottom, z
        ];
        uint[] indices = [0, 1, 2, 0, 2, 3];
        int size = 200 +
            path.Length +
            (points.Length * sizeof(float)) +
            (indices.Length * sizeof(uint)) +
            (2 * sizeof(uint));
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes,
            (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(4),
            checked((uint)size));
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            ComputeStableHash(pathValue));
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(16),
            checked((int)id));
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(28),
            (uint)SilkTopologyKind.TriangleList);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(40),
            checked((uint)path.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(44), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), 6);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52), 2);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(56), 0.1f);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(60), 0.7f);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(64), 0.2f);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(68), 1);
        for (int index = 0; index < 16; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(72 + (index * sizeof(double))),
                index % 5 == 0 ? 1 : 0);
        }
        path.CopyTo(bytes, 200);
        int pointsOffset = 200 + path.Length;
        for (int index = 0; index < points.Length; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(pointsOffset + (index * sizeof(float))),
                points[index]);
        }
        int indicesOffset = pointsOffset + (points.Length * sizeof(float));
        for (int index = 0; index < indices.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(indicesOffset + (index * sizeof(uint))),
                indices[index]);
        }
        int subprimsOffset = indicesOffset + (indices.Length * sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(subprimsOffset), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(subprimsOffset + sizeof(uint)),
            1);
        return bytes;
    }

    private static ulong ComputeStableHash(string path)
    {
        ulong hash = 14695981039346656037;
        foreach (byte value in Encoding.UTF8.GetBytes(path))
        {
            hash ^= value;
            hash *= 1099511628211;
        }
        return hash;
    }
}
