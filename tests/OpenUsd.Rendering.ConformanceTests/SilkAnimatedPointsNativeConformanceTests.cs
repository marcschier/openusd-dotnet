// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkAnimatedPointsNativeConformanceTests
{
    private const int Size = 64;
    private const ulong PreparationLimit = 4096;
    private const ulong PointReservation = 2 * 48;
    private const ulong GpuLimit = 4_194_304;

    [Test]
    [MethodDataSource(nameof(AvailableBackends))]
    public async Task TimeSampledPositionsMatchStaticControlsAndPreservePointIdentity(
        SilkGraphicsBackend backend)
    {
        string plugins = RequirePlugins();
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuBufferBudget(GpuLimit);
        budget.ConfigureDevice(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Size, Size));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Size, Size));
        var camera = new CameraState(
            Matrix4x4.CreateLookAt(new Vector3(0, 0, 3), Vector3.Zero, Vector3.UnitY),
            Matrix4x4.CreateOrthographic(2, 2, 0.1f, 10));

        byte[] start = CaptureReference(
            plugins, "hdsilk-animated-points-reference-start.usda", device, color, depth, camera);
        byte[] middle = CaptureReference(
            plugins, "hdsilk-animated-points-reference-middle.usda", device, color, depth, camera);
        byte[] end = CaptureReference(
            plugins, "hdsilk-animated-points-reference-end.usda", device, color, depth, camera);
        await Assert.That(start.SequenceEqual(middle)).IsFalse();
        await Assert.That(middle.SequenceEqual(end)).IsFalse();
        await Assert.That(start.SequenceEqual(end)).IsFalse();
        await Assert.That(RegionHasForeground(start, 16, 16)).IsTrue();
        await Assert.That(RegionHasForeground(start, 48, 48)).IsTrue();
        await Assert.That(RegionHasForeground(start, 48, 16)).IsFalse();
        await Assert.That(RegionHasForeground(middle, 32, 16)).IsTrue();
        await Assert.That(RegionHasForeground(middle, 32, 48)).IsTrue();
        await Assert.That(RegionHasForeground(middle, 16, 16)).IsFalse();
        await Assert.That(RegionHasForeground(end, 48, 16)).IsTrue();
        await Assert.That(RegionHasForeground(end, 16, 48)).IsTrue();
        await Assert.That(RegionHasForeground(end, 16, 16)).IsFalse();

        var frames = new FrameExpectation[]
        {
            new(1, start, [-0.5f, 0.5f, 0, 0.5f, -0.5f, 0], 16, 48),
            new(2, middle, [0, 0.5f, 0, 0, -0.5f, 0], 32, 32),
            new(3, end, [0.5f, 0.5f, 0, -0.5f, -0.5f, 0], 48, 16),
            new(1, start, [-0.5f, 0.5f, 0, 0.5f, -0.5f, 0], 16, 48)
        };
        var limits = new SilkPreparationLimits(PreparationLimit, 100_000);
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(
            plugins, FixturePath("hdsilk-animated-points-conformance.usda"), limits);
        using var renderer = new SilkMeshRenderer(device);
        SelectionItem firstIdentity = default;
        byte[]? firstPixels = null;
        SilkMeshData? firstGeometry = null;
        for (int index = 0; index < frames.Length; index++)
        {
            FrameExpectation expected = frames[index];
            using OpenUsdSilkPage page = session.Sync(
                Size, Size, timeCode: expected.Time, camera: camera);
            SilkMeshPreparationUsage usage = page.MeshPreparationUsage
                ?? throw new InvalidOperationException("The bounded sync did not report preparation usage.");
            await Assert.That(usage.MaximumReservedBytes).IsEqualTo(PreparationLimit);
            await Assert.That(usage.ReservedBytes).IsEqualTo(PointReservation);
            await Assert.That(usage.PeakReservedBytes).IsGreaterThanOrEqualTo(PointReservation);
            await Assert.That(usage.PeakReservedBytes).IsLessThanOrEqualTo(PreparationLimit);

            var binding = new SilkPickFrameBinding(checked((ulong)index + 1), page.Revision);
            _ = renderer.ApplyAndRender(page, color, depth, binding);
            byte[] actual = ReadColor(color);
            await Assert.That(actual.SequenceEqual(expected.Pixels)).IsTrue()
                .Because($"Time {expected.Time} must match its separately authored static scene.");
            await Assert.That(actual.SequenceEqual(expected.Time == 1 ? end : start)).IsFalse()
                .Because("A stale position sample must not match the other static control.");
            SilkMeshData mesh = renderer.Scene.Meshes.Values.Single();
            await Assert.That(mesh.Path).IsEqualTo("/Particles");
            await Assert.That(mesh.TopologyKind).IsEqualTo(SilkTopologyKind.PointList);
            await Assert.That(mesh.Points.Span.SequenceEqual(expected.Points)).IsTrue();
            await Assert.That(mesh.Indices.Span.SequenceEqual([0u, 1u])).IsTrue();
            await Assert.That(mesh.TriangleSubprims.Span.SequenceEqual([0, 1])).IsTrue();
            await Assert.That(mesh.PointOrigins.Span.SequenceEqual([0, 1])).IsTrue();
            await Assert.That(mesh.AuthoredPointCount).IsEqualTo(2);
            await Assert.That(mesh.SubprimIdentity).IsEqualTo(SilkSubprimIdentity.Point);
            await Assert.That(mesh.SubprimUnsupported).IsEqualTo(SilkSubprimUnsupportedReason.TopologyMode);
            await Assert.That(renderer.GpuResources.MeshValues.Count).IsEqualTo(1);
            await Assert.That(budget.Usage.ReservedBytes).IsGreaterThan(0ul);
            await Assert.That(budget.Usage.ReservedBytes).IsLessThanOrEqualTo(GpuLimit);

            RenderPickResult first = await PickNear(renderer, color, depth, binding, expected.FirstX, 16);
            await Assert.That(first.Status).IsEqualTo(RenderPickStatus.Hit);
            await Assert.That(first.PrimPath).IsEqualTo("/Particles");
            await Assert.That(first.ElementIndex).IsEqualTo(0);
            await Assert.That(first.Item).IsNotNull();
            SelectionItem selected = first.Item!.Value;
            await Assert.That(selected.ElementKind).IsEqualTo(SelectionElementKind.Point);
            if (index == 0)
            {
                firstIdentity = selected;
                firstPixels = actual;
                firstGeometry = mesh;
            }
            else
            {
                await Assert.That(selected).IsEqualTo(firstIdentity);
            }

            RenderPickResult second = await PickNear(renderer, color, depth, binding, expected.SecondX, 48);
            await Assert.That(second.Status).IsEqualTo(RenderPickStatus.Hit);
            await Assert.That(second.PrimPath).IsEqualTo("/Particles");
            await Assert.That(second.ElementIndex).IsEqualTo(1);
            await Assert.That(second.Item!.Value.ElementKind).IsEqualTo(SelectionElementKind.Point);
            if (expected.Time == 3)
            {
                RenderPickResult stale = await PickNear(renderer, color, depth, binding, 16, 16);
                await Assert.That(stale.Status).IsEqualTo(RenderPickStatus.Miss);
            }
        }

        renderer.Dispose();
        color.Dispose();
        depth.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
        await Assert.That(budget.Usage.PeakReservedBytes).IsGreaterThan(0ul);
        await Assert.That(budget.Usage.PeakReservedBytes).IsLessThanOrEqualTo(GpuLimit);
        await Assert.That(firstPixels!.SequenceEqual(start)).IsTrue();
        SilkMeshData retainedGeometry = firstGeometry
            ?? throw new InvalidOperationException("The initial particle geometry was not retained.");
        await Assert.That(retainedGeometry.Points.Span.SequenceEqual(frames[0].Points)).IsTrue();
        await Assert.That(retainedGeometry.PointOrigins.Span.SequenceEqual([0, 1])).IsTrue();
    }

    public static IEnumerable<SilkGraphicsBackend> AvailableBackends() =>
        SilkConformanceBackends.Available();

    private static byte[] CaptureReference(
        string plugins,
        string file,
        ISilkGraphicsDevice device,
        ISilkGraphicsTexture color,
        ISilkGraphicsTexture depth,
        CameraState camera)
    {
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(plugins, FixturePath(file));
        using var renderer = new SilkMeshRenderer(device);
        using OpenUsdSilkPage page = session.Sync(Size, Size, camera: camera);
        _ = renderer.ApplyAndRender(page, color, depth);
        return ReadColor(color);
    }

    private static async Task<RenderPickResult> PickNear(
        SilkMeshRenderer renderer,
        ISilkGraphicsTexture color,
        ISilkGraphicsTexture depth,
        SilkPickFrameBinding binding,
        int x,
        int y)
    {
        RenderPickResult result = default;
        for (int row = y - 1; row <= y + 1; row++)
        {
            for (int column = x - 1; column <= x + 1; column++)
            {
                var request = new RenderPickRequest(
                    column, row, new ViewportDimensions(Size, Size),
                    binding.StateRevision, binding.SceneRevision, RenderPickTarget.Point);
                Task<RenderPickResult> pending = renderer.PickAsync(request).AsTask();
                for (int frame = 0; frame < 100 && !pending.IsCompleted; frame++)
                {
                    _ = renderer.Render(color, depth, binding);
                    await Task.Yield();
                }
                if (!pending.IsCompleted)
                {
                    throw new TimeoutException("The point pick did not complete.");
                }
                result = await pending;
                if (result.Status == RenderPickStatus.Hit)
                {
                    return result;
                }
                await Assert.That(result.Status).IsEqualTo(RenderPickStatus.Miss);
            }
        }
        return result;
    }

    private static byte[] ReadColor(ISilkGraphicsTexture color)
    {
        var pixels = new byte[Size * Size * 4];
        color.ReadbackForTesting(pixels);
        return pixels;
    }

    private static bool RegionHasForeground(byte[] pixels, int x, int y)
    {
        for (int row = y - 4; row <= y + 4; row++)
        {
            for (int column = x - 4; column <= x + 4; column++)
            {
                int offset = ((row * Size) + column) * 4;
                if (!pixels.AsSpan(offset, 4).SequenceEqual(pixels.AsSpan(0, 4)))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static string RequirePlugins()
    {
        string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
        if (string.IsNullOrWhiteSpace(plugins) || !Directory.Exists(plugins))
        {
            const string message = "Set OPENUSD_TEST_PLUGIN_PATH to the matched native runtime plugin directory.";
            if (Environment.GetEnvironmentVariable("OPENUSD_MESH_PREPARATION_REQUIRED") == "1")
            {
                throw new InvalidOperationException(message);
            }
            Skip.Test(message);
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        return plugins;
    }

    private static string FixturePath(string file)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
            !File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
        {
            directory = directory.Parent;
        }
        return Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root."),
            "test-assets",
            file);
    }

    private sealed record FrameExpectation(
        double Time, byte[] Pixels, float[] Points, int FirstX, int SecondX);
}
