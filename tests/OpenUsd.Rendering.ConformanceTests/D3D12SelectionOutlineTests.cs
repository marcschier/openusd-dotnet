// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
[SupportedOSPlatform("windows")]
public sealed class D3D12SelectionOutlineTests
{
    [Test]
    public async Task WarpRendersPhysicalVisibleOutlineAndRestoresEmptySelection()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        foreach ((uint width, uint height) in new[] { (32u, 24u), (64u, 48u) })
        {
            using D3D12SilkGraphicsDevice device =
                D3D12SilkGraphicsDevice.Create(useWarp: true);
            using var renderer = new SilkMeshRenderer(device);
            using ISilkGraphicsTexture color = CreateColor(device, width, height);
            using ISilkGraphicsTexture depth = device.CreateTexture2D(
                SilkTextureDescriptor.SampledDepthTarget(width, height));
            ApplyScene(
                renderer,
                width,
                height,
                CreateQuadCommand(1, "/Selected", -0.5f, 0.5f, 0.5f, -0.5f, 0.25f));

            _ = renderer.Render(color, depth);
            byte[] baseline = ReadPixels(color);
            renderer.UpdateSelection(
                new SelectionState(["/Selected"]),
                new SilkSelectionOutlineSettings(
                    enabled: true,
                    new SilkColor(1, 0.55f, 0, 0.9f),
                    width: 1,
                    visibleOnly: true));
            _ = renderer.Render(color, depth);
            byte[] narrow = ReadPixels(color);
            int narrowWidth = MeasureLeftOutlineWidth(
                baseline,
                narrow,
                width,
                height / 2);

            renderer.UpdateSelection(
                new SelectionState(["/Selected"]),
                new SilkSelectionOutlineSettings(
                    enabled: true,
                    new SilkColor(1, 0.55f, 0, 0.9f),
                    width: 4,
                    visibleOnly: true));
            _ = renderer.Render(color, depth);
            byte[] wide = ReadPixels(color);
            int wideWidth = MeasureLeftOutlineWidth(
                baseline,
                wide,
                width,
                height / 2);

            await Assert.That(CountChangedPixels(baseline, narrow))
                .IsGreaterThan(0);
            await Assert.That(narrowWidth).IsBetween(1, 2);
            await Assert.That(wideWidth).IsBetween(3, 5);
            await Assert.That(wideWidth).IsGreaterThan(narrowWidth);
            AssertContainsStraightAlphaOrange(baseline, wide);

            renderer.UpdateSelection(SelectionState.Empty);
            _ = renderer.Render(color, depth);
            byte[] restored = ReadPixels(color);
            await Assert.That(restored.SequenceEqual(baseline)).IsTrue();
        }
    }

    [Test]
    public async Task WarpSuppressesFullyOccludedSelection()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        const uint width = 48;
        const uint height = 36;
        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColor(device, width, height);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(width, height));
        ApplyScene(
            renderer,
            width,
            height,
            CreateQuadCommand(1, "/Near", -0.6f, 0.6f, 0.6f, -0.6f, 0.2f),
            CreateQuadCommand(2, "/Far", -0.6f, 0.6f, 0.6f, -0.6f, 0.8f));

        _ = renderer.Render(color, depth);
        byte[] baseline = ReadPixels(color);
        renderer.UpdateSelection(new SelectionState(["/Far"]));
        _ = renderer.Render(color, depth);
        byte[] selected = ReadPixels(color);

        await Assert.That(selected.SequenceEqual(baseline)).IsTrue();
        await Assert.That(renderer.SelectionOutlineDiagnostics.Status)
            .IsEqualTo(SilkSelectionOutlineStatus.Rendered);
    }

    [Test]
    public async Task WarpRecreatesOnlyInvalidatedOutlineResourcesAndReleasesAll()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        var device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        var renderer = new SilkMeshRenderer(device);
        ISilkGraphicsTexture? firstColor = null;
        ISilkGraphicsTexture? firstDepth = null;
        ISilkGraphicsTexture? resizedColor = null;
        ISilkGraphicsTexture? resizedDepth = null;
        try
        {
            firstColor = CreateColor(device, 32, 24);
            firstDepth = device.CreateTexture2D(
                SilkTextureDescriptor.SampledDepthTarget(32, 24));
            ApplyScene(
                renderer,
                32,
                24,
                CreateQuadCommand(
                    1,
                    "/Selected",
                    -0.5f,
                    0.5f,
                    0.5f,
                    -0.5f,
                    0.25f));
            renderer.UpdateSelection(new SelectionState(["/Selected"]));

            _ = renderer.Render(firstColor, firstDepth);
            D3D12SelectionOutlineNativeStatistics initial =
                device.SelectionOutlineNativeStatisticsForTesting;
            _ = renderer.Render(firstColor, firstDepth);
            D3D12SelectionOutlineNativeStatistics warmed =
                device.SelectionOutlineNativeStatisticsForTesting;
            await Assert.That(warmed).IsEqualTo(initial);

            resizedColor = CreateColor(device, 48, 30);
            resizedDepth = device.CreateTexture2D(
                SilkTextureDescriptor.SampledDepthTarget(48, 30));
            SilkMeshRendererConformance.Apply(
                renderer,
                2,
                SilkMeshRendererConformance.CreateFrameCommand(
                    48,
                    30,
                    SilkMeshRendererConformance.Identity()));
            _ = renderer.Render(resizedColor, resizedDepth);
            D3D12SelectionOutlineNativeStatistics resized =
                device.SelectionOutlineNativeStatisticsForTesting;
            await Assert.That(resized.MaskPipelineCreations)
                .IsEqualTo(initial.MaskPipelineCreations);
            await Assert.That(resized.OutlinePipelineCreations)
                .IsEqualTo(initial.OutlinePipelineCreations);
            await Assert.That(resized.BindingCreations)
                .IsEqualTo(initial.BindingCreations + 1);

            device.InvalidateSelectionOutlineDeviceGenerationForTesting();
            _ = renderer.Render(resizedColor, resizedDepth);
            D3D12SelectionOutlineNativeStatistics invalidated =
                device.SelectionOutlineNativeStatisticsForTesting;
            await Assert.That(invalidated.MaskPipelineCreations)
                .IsEqualTo(initial.MaskPipelineCreations + 1);
            await Assert.That(invalidated.OutlinePipelineCreations)
                .IsEqualTo(initial.OutlinePipelineCreations + 1);
            await Assert.That(invalidated.BindingCreations)
                .IsEqualTo(initial.BindingCreations + 2);
            await Assert.That(invalidated.ActiveMaskPipelines).IsEqualTo(1);
            await Assert.That(invalidated.ActiveOutlinePipelines).IsEqualTo(1);
            await Assert.That(invalidated.ActiveBindings).IsEqualTo(1);
            await Assert.That(renderer.SelectionOutlineDiagnostics.DeviceInvalidations)
                .IsEqualTo(1UL);

            renderer.Dispose();
            D3D12SelectionOutlineNativeStatistics released =
                device.SelectionOutlineNativeStatisticsForTesting;
            await Assert.That(released.ActiveMaskPipelines).IsEqualTo(0);
            await Assert.That(released.ActiveOutlinePipelines).IsEqualTo(0);
            await Assert.That(released.ActiveBindings).IsEqualTo(0);
        }
        finally
        {
            renderer.Dispose();
            resizedDepth?.Dispose();
            resizedColor?.Dispose();
            firstDepth?.Dispose();
            firstColor?.Dispose();
            device.Dispose();
        }
    }

    private static ISilkGraphicsTexture CreateColor(
        D3D12SilkGraphicsDevice device,
        uint width,
        uint height) =>
        device.CreateTexture2D(new SilkTextureDescriptor(
            width,
            height,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));

    private static void ApplyScene(
        SilkMeshRenderer renderer,
        uint width,
        uint height,
        params byte[][] meshes)
    {
        var commands = new byte[meshes.Length + 1][];
        commands[0] = SilkMeshRendererConformance.CreateFrameCommand(
            width,
            height,
            SilkMeshRendererConformance.Identity());
        meshes.CopyTo(commands, 1);
        SilkMeshRendererConformance.Apply(renderer, 1, commands);
    }

    private static byte[] CreateQuadCommand(
        ulong id,
        string pathValue,
        float left,
        float top,
        float right,
        float bottom,
        float depth) =>
        SilkMeshRendererConformance.CreateMeshCommand(
            id,
            pathValue,
            [
                left, top, depth,
                right, top, depth,
                right, bottom, depth,
                left, bottom, depth
            ],
            [0, 1, 2, 0, 2, 3]);

    private static byte[] ReadPixels(ISilkGraphicsTexture texture)
    {
        var pixels = new byte[
            checked((int)(texture.Width * texture.Height * 4))];
        texture.ReadbackForTesting(pixels);
        return pixels;
    }

    private static int MeasureLeftOutlineWidth(
        byte[] baseline,
        byte[] selected,
        uint width,
        uint row)
    {
        int firstInterior = -1;
        for (uint x = 0; x < width; x++)
        {
            int offset = checked((int)(((row * width) + x) * 4));
            if (baseline[offset] != 0 ||
                baseline[offset + 1] != 0 ||
                baseline[offset + 2] != 0)
            {
                firstInterior = checked((int)x);
                break;
            }
        }
        if (firstInterior <= 0)
        {
            throw new InvalidOperationException(
                "The WARP baseline did not contain the expected selected quad.");
        }

        int count = 0;
        for (int x = firstInterior - 1; x >= 0; x--)
        {
            int offset = checked((int)(((row * width) + (uint)x) * 4));
            if (PixelsEqual(baseline, selected, offset))
            {
                break;
            }
            count++;
        }
        return count;
    }

    private static int CountChangedPixels(byte[] baseline, byte[] selected)
    {
        int count = 0;
        for (int offset = 0; offset < baseline.Length; offset += 4)
        {
            if (!PixelsEqual(baseline, selected, offset))
            {
                count++;
            }
        }
        return count;
    }

    private static void AssertContainsStraightAlphaOrange(
        byte[] baseline,
        byte[] selected)
    {
        for (int offset = 0; offset < baseline.Length; offset += 4)
        {
            if (!PixelsEqual(baseline, selected, offset) &&
                selected[offset] >= 220 &&
                selected[offset + 1] is >= 110 and <= 145 &&
                selected[offset + 2] <= 8)
            {
                return;
            }
        }
        throw new InvalidOperationException(
            "The selected WARP frame contained no straight-alpha orange outline pixel.");
    }

    private static bool PixelsEqual(byte[] left, byte[] right, int offset) =>
        left[offset] == right[offset] &&
        left[offset + 1] == right[offset + 1] &&
        left[offset + 2] == right[offset + 2] &&
        left[offset + 3] == right[offset + 3];

}
