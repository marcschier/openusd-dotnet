// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Runtime.Versioning;
using System.Text;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.Metal;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class MetalSelectionOutlineConformanceTests
{
    [Test]
    [SupportedOSPlatform("macos")]
    public async Task BaselineOutlineAndClearSelectionRenderOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }
        RequirePinnedMetalLibrary();

        const uint size = 64;
        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();
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
    [SupportedOSPlatform("macos")]
    public async Task OcclusionAndPhysicalWidthAreVisibleOnlyOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }
        RequirePinnedMetalLibrary();

        const uint size = 80;
        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();
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
    [SupportedOSPlatform("macos")]
    public async Task ResizeAndFailureRecreateTheRequiredResourcesOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }
        RequirePinnedMetalLibrary();

        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();
        using var renderer = new SilkMeshRenderer(device);
        Apply(
            renderer,
            48,
            48,
            CreateQuad(1, "/Selected", 0.2f, -0.5f, 0.5f, 0.5f, -0.5f));
        renderer.UpdateSelection(new SelectionState(["/Selected"]));
        using (ISilkGraphicsTexture color = CreateColorTarget(device, 48, 48))
        using (ISilkGraphicsTexture depth = CreateDepthTarget(device, 48, 48))
        {
            _ = renderer.Render(color, depth);
        }

        using ISilkGraphicsTexture resizedColor = CreateColorTarget(device, 72, 40);
        using ISilkGraphicsTexture resizedDepth = CreateDepthTarget(device, 72, 40);
        _ = renderer.Render(resizedColor, resizedDepth);
        await Assert.That(device.SelectionMaskPipelineCreationCount).IsEqualTo(1L);
        await Assert.That(device.SelectionOutlinePipelineCreationCount).IsEqualTo(1L);
        await Assert.That(device.SelectionOutlineBindingCreationCount).IsEqualTo(2L);

        ulong generation = device.SelectionOutlineDeviceGeneration;
        device.NotifySelectionOutlineCommandBufferFailure();
        _ = renderer.Render(resizedColor, resizedDepth);

        await Assert.That(device.SelectionOutlineDeviceGeneration)
            .IsEqualTo(generation + 1);
        await Assert.That(device.SelectionMaskPipelineCreationCount).IsEqualTo(2L);
        await Assert.That(device.SelectionOutlinePipelineCreationCount).IsEqualTo(2L);
        await Assert.That(device.SelectionOutlineBindingCreationCount).IsEqualTo(3L);
        await Assert.That(renderer.SelectionOutlineDiagnostics.DeviceInvalidations)
            .IsEqualTo(1UL);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task RetainedBindingsAndFailureCleanupReleaseOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }
        RequirePinnedMetalLibrary();

        MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();
        ISilkSelectionMaskGraphicsPipeline? maskPipeline = null;
        ISilkSelectionOutlineGraphicsPipeline? outlinePipeline = null;
        ISilkGraphicsTexture? mask = null;
        ISilkGraphicsTexture? depth = null;
        ISilkGraphicsSampler? sampler = null;
        ISilkGraphicsBuffer? parameters = null;
        ISilkSelectionOutlineBinding? binding = null;
        try
        {
            maskPipeline = device.CreateSelectionMaskGraphicsPipeline(
                SilkSelectionMaskPipelineDescriptor.CreateChecked(
                    SilkShaderBinaryFormat.MetalLibrary));
            outlinePipeline = device.CreateSelectionOutlineGraphicsPipeline(
                SilkSelectionOutlinePipelineDescriptor.CreateChecked(
                    SilkShaderBinaryFormat.MetalLibrary));
            mask = device.CreateTexture2D(
                SilkTextureDescriptor.SelectionMask(8, 8));
            depth = CreateDepthTarget(device, 8, 8);
            sampler = device.CreateSampler(SilkSamplerDescriptor.NearestClamp);
            parameters = device.CreateBuffer(
                SilkSelectionOutlineUniformWriter.ByteSize,
                SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
            binding = device.CreateSelectionOutlineBinding(new(
                mask,
                depth,
                sampler,
                parameters));

            mask.Dispose();
            depth.Dispose();
            sampler.Dispose();
            parameters.Dispose();
            await Assert.That(() => device.Dispose())
                .Throws<InvalidOperationException>();
        }
        finally
        {
            binding?.Dispose();
            outlinePipeline?.Dispose();
            maskPipeline?.Dispose();
            parameters?.Dispose();
            sampler?.Dispose();
            depth?.Dispose();
            mask?.Dispose();
            device.Dispose();
        }
    }

    [SupportedOSPlatform("macos")]
    private static ISilkGraphicsTexture CreateColorTarget(
        MetalSilkGraphicsDevice device,
        uint width,
        uint height) =>
        device.CreateTexture2D(new SilkTextureDescriptor(
            width,
            height,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));

    [SupportedOSPlatform("macos")]
    private static ISilkGraphicsTexture CreateDepthTarget(
        MetalSilkGraphicsDevice device,
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
        int size = 216 +
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
        path.CopyTo(bytes, 216);
        int pointsOffset = 216 + path.Length;
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

    private static void RequirePinnedMetalLibrary()
    {
        string library = Path.Combine(AppContext.BaseDirectory, "mesh.metallib");
        string manifest = Path.Combine(
            AppContext.BaseDirectory,
            "mesh.metallib.manifest.json");
        if (!File.Exists(library) || !File.Exists(manifest))
        {
            throw new InvalidOperationException(
                "Real Metal selection-outline conformance requires the hosted " +
                "Xcode 16.4 ten-entry mesh.metallib and validation sidecar.");
        }
        SilkCheckedShaderAssets.ValidatePinnedMetalLibrary();
    }
}
