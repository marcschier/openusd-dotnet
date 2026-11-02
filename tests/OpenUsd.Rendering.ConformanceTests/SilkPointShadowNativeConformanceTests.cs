// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkPointShadowNativeConformanceTests
{
    private const int Size = 64;
    private const int SampleY = 32;
    private const int CenterX = 32;
    private const int PerspectiveOnlyX = 45;
    private const int ControlX = 51;
    private const int MovedShadowX = 58;
    private const float CasterHalfWidth = 0.25f;
    private const float MovedCasterX = 0.6f;

    private static readonly CameraState Camera = new(Matrix4x4.Identity, Matrix4x4.Identity);
    private static readonly SilkMeshRenderOptions Options = new(
        new SilkColor(0, 32 / 255f, 0, 1), 1)
    {
        OutputTransform = RenderOutputTransform.Identity
    };

    // Unverified until native and backend execution uses regenerated checked shaders.
    [Test]
    [MethodDataSource(nameof(AvailableBackends))]
    public async Task AuthoredBoundedPointShadowMatchesAnalyticPixelsWhenSupported(
        SilkGraphicsBackend backend)
    {
        string plugins = RequirePlugins();
        Sequence sequence = RenderSequence(backend, plugins);
        Frame before = sequence.Before;
        Frame shadowed = sequence.Shadowed;
        Frame moved = sequence.Moved;
        Frame restored = sequence.Restored;
        Frame retired = sequence.Retired;

        // The ray from (0,0,1.4) to a receiver at z=0.2 crosses z=0.8 at x/2.
        await Assert.That(MathF.Abs(CasterIntersectionX(CenterX)))
            .IsLessThan(CasterHalfWidth);
        await Assert.That(MathF.Abs(CasterIntersectionX(PerspectiveOnlyX)))
            .IsLessThan(CasterHalfWidth);
        await Assert.That(PixelWorldX(PerspectiveOnlyX)).IsGreaterThan(CasterHalfWidth);
        await Assert.That(MathF.Abs(CasterIntersectionX(ControlX)))
            .IsGreaterThan(CasterHalfWidth);
        await Assert.That(MathF.Abs(CasterIntersectionX(MovedShadowX)))
            .IsGreaterThan(CasterHalfWidth);
        await Assert.That(MathF.Abs(CasterIntersectionX(MovedShadowX) - MovedCasterX))
            .IsLessThan(CasterHalfWidth);
        await Assert.That(MathF.Abs(CasterIntersectionX(PerspectiveOnlyX) - MovedCasterX))
            .IsGreaterThan(CasterHalfWidth);

        foreach (Frame frame in (Frame[])[before, shadowed, moved, restored, retired])
        {
            await Assert.That(frame.Draws).IsGreaterThan(0);
            await Assert.That(frame.MeshCount).IsEqualTo(2);
            await Assert.That(frame.LightCount).IsEqualTo(1u);
            await Assert.That(frame.Light.Type).IsEqualTo(2u);
            await Assert.That(frame.Light.Radius).IsEqualTo(0f);
            await Assert.That(frame.Light.ShapingConeAngle).IsEqualTo(180f);
            await Assert.That(frame.Light.Transform.M31).IsEqualTo(1f);
            await Assert.That(frame.Light.Transform.M33).IsEqualTo(0f);
            await Assert.That(frame.Light.Transform.M43).IsEqualTo(1.4f);
        }

        await Assert.That(before.Light.ShadowEnabled).IsEqualTo(0u);
        await Assert.That(shadowed.Light.ShadowEnabled).IsEqualTo(1u);
        await Assert.That(moved.Light.ShadowEnabled).IsEqualTo(1u);
        await Assert.That(restored.Light.ShadowEnabled).IsEqualTo(1u);
        await Assert.That(retired.Light.ShadowEnabled).IsEqualTo(0u);
        await Assert.That(before.Descriptors.Length).IsEqualTo(0);
        await Assert.That(shadowed.Descriptors.Length).IsEqualTo(1);
        await Assert.That(moved.Descriptors.Length).IsEqualTo(1);
        await Assert.That(restored.Descriptors.Length).IsEqualTo(1);
        await Assert.That(retired.Descriptors.Length).IsEqualTo(0);
        await Assert.That(shadowed.Unsupported).IsEqualTo(SilkShadowUnsupportedFeatures.None);
        SilkShadowDescriptor descriptor = shadowed.Descriptors[0];
        await Assert.That(descriptor.LightIndex).IsEqualTo(0u);
        await Assert.That(descriptor.MapIndex).IsEqualTo(0u);
        await Assert.That(descriptor.Flags).IsEqualTo(SilkShadowDescriptorOptions.None);
        await Assert.That(descriptor.Projection[11]).IsEqualTo(-1.0);
        await Assert.That(Math.Abs(descriptor.View[2])).IsLessThan(1e-12);
        await Assert.That(Math.Abs(descriptor.View[6])).IsLessThan(1e-12);
        await Assert.That(Math.Abs(descriptor.View[10] - 1.0)).IsLessThan(1e-12);

        foreach (int x in (int[])[CenterX, PerspectiveOnlyX, ControlX, MovedShadowX])
        {
            await AssertLit(before, x, "unshadowed control");
        }

        if (!sequence.SupportsRasterShadows)
        {
            await Assert.That(backend).IsEqualTo(SilkGraphicsBackend.Metal);
            await Assert.That(shadowed.LiveMaps).IsEqualTo(0);
            await Assert.That(shadowed.Renders).IsEqualTo(0ul);
            await Assert.That(shadowed.ShadowDiagnostics.Any(message =>
                message.Contains("Metal", StringComparison.Ordinal) &&
                message.Contains("depth-only pass", StringComparison.Ordinal))).IsTrue();
            await AssertNear(before, shadowed, PerspectiveOnlyX, "unsupported Metal shadow");
            await AssertNear(before, retired, PerspectiveOnlyX, "retired Metal request");
            await Assert.That(retired.ShadowDiagnostics.Length).IsEqualTo(0);
            return;
        }

        await Assert.That(shadowed.LiveMaps).IsEqualTo(1);
        await Assert.That(shadowed.Renders).IsGreaterThan(0ul);
        await Assert.That(shadowed.ShadowDiagnostics.Length).IsEqualTo(0);
        await AssertDarkened(before, shadowed, CenterX, "point shadow center");
        await AssertDarkened(before, shadowed, PerspectiveOnlyX, "perspective-only shadow");
        await AssertNear(before, shadowed, ControlX, "unoccluded receiver");
        await AssertNear(before, shadowed, MovedShadowX, "receiver beyond the initial shadow");

        await Assert.That(moved.LiveMaps).IsEqualTo(1);
        await Assert.That(moved.Renders).IsGreaterThan(shadowed.Renders);
        await AssertNear(before, moved, CenterX, "receiver the moved caster left");
        await AssertNear(before, moved, PerspectiveOnlyX, "perspective extension the caster left");
        await AssertNear(before, moved, ControlX, "unoccluded receiver after movement");
        await AssertDarkened(before, moved, MovedShadowX, "moved point shadow");

        await Assert.That(restored.LiveMaps).IsEqualTo(1);
        await Assert.That(restored.Renders).IsGreaterThan(moved.Renders);
        await AssertDarkened(before, restored, CenterX, "restored point shadow center");
        await AssertDarkened(before, restored, PerspectiveOnlyX, "restored perspective extension");
        await AssertNear(before, restored, MovedShadowX, "receiver beyond the restored shadow");
        await Assert.That(retired.LiveMaps).IsEqualTo(0);
        foreach (int x in (int[])[CenterX, PerspectiveOnlyX, ControlX, MovedShadowX])
        {
            await AssertNear(before, retired, x, "retired shadow");
        }
    }

    public static IEnumerable<SilkGraphicsBackend> AvailableBackends() => SilkConformanceBackends.Available();

    private static Sequence RenderSequence(SilkGraphicsBackend backend, string plugins)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(plugins, FixturePath());
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size, Size, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));

        return new Sequence(
            device.Capabilities.SupportsRasterShadows,
            Capture(session, renderer, color, depth, 0),
            Capture(session, renderer, color, depth, 1),
            Capture(session, renderer, color, depth, 2),
            Capture(session, renderer, color, depth, 3),
            Capture(session, renderer, color, depth, 4));
    }

    private static Frame Capture(
        OpenUsdSilkSession session,
        SilkMeshRenderer renderer,
        ISilkGraphicsTexture color,
        ISilkGraphicsTexture depth,
        double time)
    {
        using OpenUsdSilkPage page = session.Sync(Size, Size, time, Camera);
        SilkMeshRenderResult result = renderer.ApplyAndRender(page, color, depth, Options);
        var pixels = new byte[Size * Size * 4];
        color.ReadbackForTesting(pixels);
        return new Frame(
            pixels,
            result.DrawCount,
            renderer.Scene.Meshes.Count,
            renderer.Scene.Frame.LightCount,
            renderer.Scene.Frame.Lights[0],
            [.. renderer.Scene.Shadows.Descriptors],
            renderer.Scene.Shadows.UnsupportedFeatures,
            renderer.ShadowMapCount,
            renderer.ShadowMapRenderCount,
            [.. renderer.GpuResources.Diagnostics.Entries
                .Where(entry => entry.Code == SilkRenderDiagnosticCodes.ShadowUnsupported)
                .Select(entry => entry.Message)]);
    }

    private static async Task AssertLit(Frame frame, int x, string label)
    {
        int offset = PixelOffset(x);
        await Assert.That(frame.Pixels[offset]).IsGreaterThan((byte)40)
            .Because(Describe(frame, x, label));
        await Assert.That(frame.Pixels[offset + 1]).IsLessThan((byte)8)
            .Because(Describe(frame, x, label));
        await Assert.That(frame.Pixels[offset + 3]).IsGreaterThan((byte)100)
            .Because(Describe(frame, x, label));
    }

    private static async Task AssertDarkened(Frame before, Frame after, int x, string label)
    {
        int offset = PixelOffset(x);
        await Assert.That(after.Pixels[offset] * 4).IsLessThan((int)before.Pixels[offset])
            .Because(Describe(after, x, label));
        await Assert.That(after.Pixels[offset + 1]).IsLessThan((byte)8)
            .Because(Describe(after, x, label));
        await Assert.That(after.Pixels[offset + 3]).IsGreaterThan((byte)100)
            .Because(Describe(after, x, label));
    }

    private static async Task AssertNear(Frame before, Frame after, int x, string label)
    {
        int offset = PixelOffset(x);
        await Assert.That(Math.Abs(after.Pixels[offset] - before.Pixels[offset]))
            .IsLessThanOrEqualTo(2)
            .Because(Describe(after, x, label));
        await Assert.That(after.Pixels[offset + 1]).IsLessThan((byte)8)
            .Because(Describe(after, x, label));
        await Assert.That(after.Pixels[offset + 3]).IsGreaterThan((byte)100)
            .Because(Describe(after, x, label));
    }

    private static string Describe(Frame frame, int x, string label)
    {
        int offset = PixelOffset(x);
        return $"{label} at ({x},{SampleY}) is rgba(" +
            $"{frame.Pixels[offset]},{frame.Pixels[offset + 1]}," +
            $"{frame.Pixels[offset + 2]},{frame.Pixels[offset + 3]}).";
    }

    private static int PixelOffset(int x) => ((SampleY * Size) + x) * 4;

    private static float PixelWorldX(int x) => ((x + 0.5f) * 2f / Size) - 1f;

    private static float CasterIntersectionX(int x) => PixelWorldX(x) * 0.5f;

    private static string RequirePlugins()
    {
        string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
        if (string.IsNullOrWhiteSpace(plugins) || !Directory.Exists(plugins))
        {
            string message = "Set OPENUSD_TEST_PLUGIN_PATH to the matched native runtime plugin directory.";
            string? required = Environment.GetEnvironmentVariable("OPENUSD_DIRECT_LIGHT_EXECUTION_REQUIRED");
            if (string.Equals(required, "1", StringComparison.Ordinal) ||
                string.Equals(required, "true", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(message);
            }
            Skip.Test(message);
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        return plugins;
    }

    private static string FixturePath()
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
            "native-point-shadow-reference.usda");
    }

    private sealed record Sequence(
        bool SupportsRasterShadows,
        Frame Before,
        Frame Shadowed,
        Frame Moved,
        Frame Restored,
        Frame Retired);

    private sealed record Frame(
        byte[] Pixels,
        int Draws,
        int MeshCount,
        uint LightCount,
        SilkFrameLight Light,
        SilkShadowDescriptor[] Descriptors,
        SilkShadowUnsupportedFeatures Unsupported,
        int LiveMaps,
        ulong Renders,
        string[] ShadowDiagnostics);
}
