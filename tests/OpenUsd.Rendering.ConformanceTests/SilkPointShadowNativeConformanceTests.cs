// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
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
    private const int ControlX = 8;
    private const int MovedShadowX = 58;
    private const float CasterHalfWidth = 0.25f;
    private const float MovedCasterX = 0.5f;

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
        Frame movedAgain = sequence.MovedAgain;
        Frame freshMoved = sequence.FreshMoved;
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
        await Assert.That(shadowed.Descriptors.Length).IsEqualTo(6);
        await Assert.That(moved.Descriptors.Length).IsEqualTo(6);
        await Assert.That(restored.Descriptors.Length).IsEqualTo(6);
        await Assert.That(retired.Descriptors.Length).IsEqualTo(0);
        await Assert.That(shadowed.Unsupported).IsEqualTo(SilkShadowUnsupportedFeatures.None);
        SilkShadowDescriptor descriptor = shadowed.Descriptors[5];
        await Assert.That(descriptor.LightIndex).IsEqualTo(0u);
        await Assert.That(descriptor.MapIndex).IsEqualTo(5u);
        await Assert.That(descriptor.Flags).IsEqualTo(SilkShadowDescriptorOptions.CubeFace);
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

        await Assert.That(shadowed.LiveMaps).IsEqualTo(6);
        await Assert.That(shadowed.Renders).IsGreaterThan(0ul);
        await Assert.That(shadowed.ShadowDiagnostics.Length).IsEqualTo(0);
        await AssertDarkened(before, shadowed, CenterX, "point shadow center");
        await AssertDarkened(before, shadowed, PerspectiveOnlyX, "perspective-only shadow");
        await AssertNear(before, shadowed, ControlX, "unoccluded receiver");
        await AssertNear(before, shadowed, MovedShadowX, "receiver beyond the initial shadow");

        ShadowDepthSample movedProbe = SampleShadowDepth(freshMoved, MovedShadowX);
        await Assert.That(movedProbe.ReceiverDepth).IsLessThan(1.0f)
            .Because($"The moved receiver must stay inside the point-shadow far plane: {movedProbe}.");
        await Assert.That(movedProbe.DirectDepth).IsLessThan(movedProbe.ReceiverDepth)
            .Because($"The moved caster must be in front of its receiver: {movedProbe}.");

        await Assert.That(moved.LiveMaps).IsEqualTo(6);
        await Assert.That(moved.Renders).IsGreaterThan(shadowed.Renders);
        await Assert.That(moved.CasterX).IsEqualTo((double)MovedCasterX).Within(0.000001);
        await Assert.That(movedAgain.CasterX).IsEqualTo((double)MovedCasterX).Within(0.000001);
        await Assert.That(freshMoved.CasterX).IsEqualTo((double)MovedCasterX).Within(0.000001);
        await AssertNear(before, moved, CenterX, "receiver the moved caster left");
        await AssertNear(before, moved, PerspectiveOnlyX, "perspective extension the caster left");
        await AssertNear(before, moved, ControlX, "unoccluded receiver after movement");
        int[] movedDarkPixels = Enumerable.Range(0, Size * Size)
            .Where(pixel => before.Pixels[pixel * 4] > 40 &&
                moved.Pixels[pixel * 4] * 4 < before.Pixels[pixel * 4])
            .ToArray();
        string movedDarkPixelBounds = movedDarkPixels.Length == 0
            ? "none"
            : $"{movedDarkPixels.Min(pixel => pixel % Size)}:{movedDarkPixels.Min(pixel => pixel / Size)}-" +
              $"{movedDarkPixels.Max(pixel => pixel % Size)}:{movedDarkPixels.Max(pixel => pixel / Size)}";
        string movedDarkPixelRows = string.Join(",", movedDarkPixels
            .GroupBy(pixel => pixel / Size)
            .Select(group => $"{group.Key}={group.Count()}"));
        string movedDarkPixelSampleRows = string.Join(";", Enumerable.Range(SampleY - 2, 5)
            .Select(y => $"{y}=[{string.Join(",", movedDarkPixels
                .Where(pixel => pixel / Size == y)
                .Select(pixel => pixel % Size))}]"));
        string movedDarkPixelLocations = string.Join(",", movedDarkPixels
            .Take(64)
            .Select(pixel => $"{pixel % Size}:{pixel / Size}"));
        int movedOffset = PixelOffset(MovedShadowX);
        await AssertDarkened(before, moved, MovedShadowX,
            $"moved point shadow; second red={movedAgain.Pixels[movedOffset]}, " +
            $"fresh red={freshMoved.Pixels[movedOffset]}, dark pixels={movedDarkPixels.Length}, " +
            $"bounds=[{movedDarkPixelBounds}], rows=[{movedDarkPixelRows}], " +
            $"sample rows={movedDarkPixelSampleRows}, " +
            $"first x:y=[{movedDarkPixelLocations}], " +
            $"[DEBUG-point-shadow-depth] {DescribeShadowAtlasDepth(freshMoved)}");
        await AssertDarkened(before, movedAgain, MovedShadowX, "repeated moved point shadow");
        await AssertDarkened(before, freshMoved, MovedShadowX, "fresh moved point shadow");

        await Assert.That(restored.LiveMaps).IsEqualTo(6);
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

    [Test]
    [MethodDataSource(nameof(AvailableBackends))]
    public async Task DistantPointShadowPreservesDepthSeparationWhenSupported(
        SilkGraphicsBackend backend)
    {
        string plugins = RequirePlugins();
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size, Size, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(
            plugins, FixturePath("native-point-shadow-distant-reference.usda"));
        using var renderer = new SilkMeshRenderer(device);

        Frame before = Capture(session, renderer, color, depth, 0);
        Frame shadowed = Capture(session, renderer, color, depth, 1);
        await AssertLit(before, CenterX, "distant point-light control");
        await Assert.That(shadowed.Light.Transform.M43).IsEqualTo(50f);
        await Assert.That(shadowed.Descriptors.Length).IsEqualTo(6);

        if (!device.Capabilities.SupportsRasterShadows)
        {
            await Assert.That(backend).IsEqualTo(SilkGraphicsBackend.Metal);
            await AssertNear(before, shadowed, CenterX, "unsupported distant point shadow");
            return;
        }

        uint shadowAtlasEdge = renderer.ShadowAtlasEdge;
        var shadowAtlasDepth = new float[checked((int)(shadowAtlasEdge * shadowAtlasEdge))];
        renderer.ReadShadowAtlasDepthForTesting(shadowAtlasDepth);
        shadowed = shadowed with
        {
            ShadowAtlasDepth = shadowAtlasDepth,
            ShadowAtlasEdge = shadowAtlasEdge
        };
        ShadowDepthSample probe = SampleShadowDepth(shadowed, CenterX);
        await Assert.That(probe.DirectDepth).IsLessThan(probe.ReceiverDepth)
            .Because($"The distant caster must remain in front of its receiver: {probe}.");
        await AssertDarkened(before, shadowed, CenterX, "distant point shadow");
        await AssertNear(before, shadowed, ControlX, "unoccluded distant receiver");
    }

    [Test]
    [MethodDataSource(nameof(AvailableBackends))]
    public async Task RetainedPointShadowTracksCasterMovingTowardLightWhenSupported(
        SilkGraphicsBackend backend)
    {
        string plugins = RequirePlugins();
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size, Size, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(
            plugins, FixturePath("native-point-shadow-distant-reference.usda"));
        using var renderer = new SilkMeshRenderer(device);

        Frame unshadowed = Capture(session, renderer, color, depth, 0);
        Frame original = Capture(session, renderer, color, depth, 1);
        ulong shadowRevision = renderer.Scene.Shadows.Revision;
        ulong renderCount = renderer.ShadowMapRenderCount;
        SilkDeformationResult result = renderer.Scene.ReplacePoints(
            "/World/Caster",
            0,
            [
                -0.25f, -0.25f, 30f,
                 0.25f, -0.25f, 30f,
                 0.25f,  0.25f, 30f,
                -0.25f,  0.25f, 30f,
            ],
            out ulong meshId);
        await Assert.That(result).IsEqualTo(SilkDeformationResult.Applied);
        renderer.GpuResources.Apply(
            renderer.Scene, new SilkSceneDelta(new[] { meshId }, ReadOnlyMemory<ulong>.Empty));
        Frame moved = CaptureRetained(renderer, color, depth);
        await Assert.That(renderer.Scene.Shadows.Revision).IsEqualTo(shadowRevision);

        await Assert.That(renderer.Scene.RestoreAuthoredPoints(meshId)).IsTrue();
        renderer.GpuResources.Apply(
            renderer.Scene, new SilkSceneDelta(new[] { meshId }, ReadOnlyMemory<ulong>.Empty));
        Frame restored = CaptureRetained(renderer, color, depth);
        await Assert.That(renderer.Scene.Shadows.Revision).IsEqualTo(shadowRevision);
        if (!device.Capabilities.SupportsRasterShadows)
        {
            await Assert.That(backend).IsEqualTo(SilkGraphicsBackend.Metal);
            await AssertNear(unshadowed, moved, PerspectiveOnlyX, "unsupported moved point shadow");
            return;
        }

        await Assert.That(moved.Renders).IsGreaterThan(renderCount);
        await Assert.That(restored.Renders).IsGreaterThan(moved.Renders);
        await AssertNear(unshadowed, original, PerspectiveOnlyX, "original distant receiver");
        await AssertDarkened(unshadowed, moved, PerspectiveOnlyX, "caster moved toward the point light");
        await AssertNear(unshadowed, moved, ControlX, "unoccluded moved point-light receiver");
        await AssertNear(original, restored, PerspectiveOnlyX, "restored point-light receiver");
        await AssertDarkened(unshadowed, restored, CenterX, "restored distant point shadow");
    }

    public static IEnumerable<SilkGraphicsBackend> AvailableBackends() => SilkConformanceBackends.Available();

    private static Sequence RenderSequence(SilkGraphicsBackend backend, string plugins)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size, Size, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));

        Frame before;
        Frame shadowed;
        Frame moved;
        Frame movedAgain;
        Frame restored;
        Frame retired;
        using (OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(plugins, FixturePath()))
        using (var renderer = new SilkMeshRenderer(device))
        {
            before = Capture(session, renderer, color, depth, 0);
            shadowed = Capture(session, renderer, color, depth, 1);
            moved = Capture(session, renderer, color, depth, 2);
            movedAgain = Capture(session, renderer, color, depth, 2);
            restored = Capture(session, renderer, color, depth, 3);
            retired = Capture(session, renderer, color, depth, 4);
        }

        Frame freshMoved;
        using (OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(plugins, FixturePath()))
        using (var renderer = new SilkMeshRenderer(device))
        {
            freshMoved = Capture(session, renderer, color, depth, 2);
            uint shadowAtlasEdge = renderer.ShadowAtlasEdge;
            var shadowAtlasDepth = new float[checked((int)(shadowAtlasEdge * shadowAtlasEdge))];
            renderer.ReadShadowAtlasDepthForTesting(shadowAtlasDepth);
            freshMoved = freshMoved with
            {
                ShadowAtlasDepth = shadowAtlasDepth,
                ShadowAtlasEdge = shadowAtlasEdge
            };
        }

        return new Sequence(
            device.Capabilities.SupportsRasterShadows,
            before,
            shadowed,
            moved,
            movedAgain,
            freshMoved,
            restored,
            retired);
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
        return CaptureFrame(renderer, color, result);
    }

    private static Frame CaptureRetained(
        SilkMeshRenderer renderer,
        ISilkGraphicsTexture color,
        ISilkGraphicsTexture depth) =>
        CaptureFrame(renderer, color, renderer.Render(color, depth, Options));

    private static Frame CaptureFrame(
        SilkMeshRenderer renderer,
        ISilkGraphicsTexture color,
        SilkMeshRenderResult result)
    {
        var pixels = new byte[Size * Size * 4];
        color.ReadbackForTesting(pixels);
        return new Frame(
            pixels,
            result.DrawCount,
            renderer.Scene.Meshes.Count,
            renderer.Scene.Frame.LightCount,
            renderer.Scene.Frame.Lights[0],
            [.. renderer.Scene.Shadows.Descriptors],
            renderer.ShadowBindingForTesting,
            renderer.Scene.Shadows.UnsupportedFeatures,
            renderer.ShadowMapCount,
            renderer.ShadowMapRenderCount,
            renderer.Scene.Meshes.Values.Single(mesh =>
                mesh.Path == "/World/Caster").Transform.Span[12],
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

    private static float PixelWorldY(int y) => ((y + 0.5f) * 2f / Size) - 1f;

    private static float CasterIntersectionX(int x) => PixelWorldX(x) * 0.5f;

    private static string DescribeShadowAtlasDepth(Frame frame)
    {
        float[] depth = frame.ShadowAtlasDepth ??
            throw new InvalidOperationException("The moved frame has no shadow atlas readback.");
        SilkShadowAtlasLayout layout = SilkShadowAtlasLayout.Create(frame.Descriptors) ??
            throw new InvalidOperationException("The moved frame has no shadow atlas layout.");
        var faceDepths = new List<string>();
        for (int face = 0; face < frame.Descriptors.Length; face++)
        {
            SilkShadowTilePlacement tile = layout.Tiles[face];
            float minimum = 1f;
            int minimumX = 0;
            int minimumY = 0;
            int written = 0;
            for (int y = (int)tile.PixelY; y < tile.PixelY + tile.Resolution; y++)
            {
                for (int x = (int)tile.PixelX; x < tile.PixelX + tile.Resolution; x++)
                {
                    float value = depth[(y * (int)frame.ShadowAtlasEdge) + x];
                    if (value < 1f)
                    {
                        written++;
                        if (value < minimum)
                        {
                            minimum = value;
                            minimumX = x;
                            minimumY = y;
                        }
                    }
                }
            }
            faceDepths.Add(
                $"f{face}={written}@{minimumX},{minimumY}:" +
                minimum.ToString("G9", CultureInfo.InvariantCulture));
        }

        var samples = new List<string>();
        for (int x = 54; x <= 63; x++)
        {
            Vector4 worldPosition = new(PixelWorldX(x), PixelWorldY(SampleY), 0.2f, 1);
            ShadowDepthSample column = SampleShadowDepth(
                frame, layout, depth, worldPosition, TransformColumn);
            ShadowDepthSample row = SampleShadowDepth(
                frame, layout, depth, worldPosition, TransformRow);
            samples.Add($"{x}/c{column}/r{row}");
        }
        return $"faces=[{string.Join(";", faceDepths)}] samples=[{string.Join(" ", samples)}]";
    }

    private static ShadowDepthSample SampleShadowDepth(
        Frame frame,
        SilkShadowAtlasLayout layout,
        float[] depth,
        Vector4 worldPosition,
        Func<Matrix4x4, Vector4, Vector4> transform)
    {
        int face = 0;
        float greatestW = -1;
        Vector4 clip = default;
        for (int candidate = 0; candidate < frame.Descriptors.Length; candidate++)
        {
            Vector4 faceClip = transform(
                frame.Binding.GetWorldToLightClip(candidate),
                worldPosition);
            if (faceClip.W > greatestW)
            {
                greatestW = faceClip.W;
                face = candidate;
                clip = faceClip;
            }
        }

        SilkShadowTilePlacement tile = layout.Tiles[face];
        float inverseW = 1f / clip.W;
        float u = ((clip.X * inverseW) * 0.5f) + 0.5f;
        float v = ((clip.Y * inverseW) * -0.5f) + 0.5f;
        int atlasX = Math.Clamp(
            (int)((tile.OffsetU + (u * tile.ScaleU)) * frame.ShadowAtlasEdge),
            0,
            (int)frame.ShadowAtlasEdge - 1);
        int atlasY = Math.Clamp(
            (int)((tile.OffsetV + (v * tile.ScaleV)) * frame.ShadowAtlasEdge),
            0,
            (int)frame.ShadowAtlasEdge - 1);
        int flippedAtlasY = (int)frame.ShadowAtlasEdge - 1 - atlasY;
        float directDepth = depth[(atlasY * (int)frame.ShadowAtlasEdge) + atlasX];
        float flippedDepth = depth[(flippedAtlasY * (int)frame.ShadowAtlasEdge) + atlasX];
        float pointDepthScale = frame.Binding.GetPointDepthScaleForLight(
            (int)frame.Descriptors[face].LightIndex);
        return new ShadowDepthSample(
            face, atlasX, atlasY, directDepth, flippedDepth, clip.W * pointDepthScale, clip.W);
    }

    private static ShadowDepthSample SampleShadowDepth(Frame frame, int x)
    {
        float[] depth = frame.ShadowAtlasDepth ??
            throw new InvalidOperationException("The moved frame has no shadow atlas readback.");
        SilkShadowAtlasLayout layout = SilkShadowAtlasLayout.Create(frame.Descriptors) ??
            throw new InvalidOperationException("The moved frame has no shadow atlas layout.");
        return SampleShadowDepth(
            frame,
            layout,
            depth,
            new Vector4(PixelWorldX(x), PixelWorldY(SampleY), 0.2f, 1),
            TransformRow);
    }

    private static Vector4 TransformColumn(Matrix4x4 matrix, Vector4 vector) => new(
        (matrix.M11 * vector.X) + (matrix.M12 * vector.Y) +
            (matrix.M13 * vector.Z) + (matrix.M14 * vector.W),
        (matrix.M21 * vector.X) + (matrix.M22 * vector.Y) +
            (matrix.M23 * vector.Z) + (matrix.M24 * vector.W),
        (matrix.M31 * vector.X) + (matrix.M32 * vector.Y) +
            (matrix.M33 * vector.Z) + (matrix.M34 * vector.W),
        (matrix.M41 * vector.X) + (matrix.M42 * vector.Y) +
            (matrix.M43 * vector.Z) + (matrix.M44 * vector.W));

    private static Vector4 TransformRow(Matrix4x4 matrix, Vector4 vector) => new(
        (matrix.M11 * vector.X) + (matrix.M21 * vector.Y) +
            (matrix.M31 * vector.Z) + (matrix.M41 * vector.W),
        (matrix.M12 * vector.X) + (matrix.M22 * vector.Y) +
            (matrix.M32 * vector.Z) + (matrix.M42 * vector.W),
        (matrix.M13 * vector.X) + (matrix.M23 * vector.Y) +
            (matrix.M33 * vector.Z) + (matrix.M43 * vector.W),
        (matrix.M14 * vector.X) + (matrix.M24 * vector.Y) +
            (matrix.M34 * vector.Z) + (matrix.M44 * vector.W));

    private readonly record struct ShadowDepthSample(
        int Face,
        int X,
        int Y,
        float DirectDepth,
        float FlippedDepth,
        float ReceiverDepth,
        float ClipW)
    {
        public override string ToString() =>
            $"f{Face}@{X},{Y}:{DirectDepth.ToString("G9", CultureInfo.InvariantCulture)}/" +
            $"{FlippedDepth.ToString("G9", CultureInfo.InvariantCulture)};" +
            $"z={ReceiverDepth.ToString("G9", CultureInfo.InvariantCulture)};" +
            $"w={ClipW.ToString("G9", CultureInfo.InvariantCulture)}";
    }

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

    private static string FixturePath(string fileName = "native-point-shadow-reference.usda")
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
            fileName);
    }

    private sealed record Sequence(
        bool SupportsRasterShadows,
        Frame Before,
        Frame Shadowed,
        Frame Moved,
        Frame MovedAgain,
        Frame FreshMoved,
        Frame Restored,
        Frame Retired);

    private sealed record Frame(
        byte[] Pixels,
        int Draws,
        int MeshCount,
        uint LightCount,
        SilkFrameLight Light,
        SilkShadowDescriptor[] Descriptors,
        SilkShadowFrameBinding Binding,
        SilkShadowUnsupportedFeatures Unsupported,
        int LiveMaps,
        ulong Renders,
        double CasterX,
        string[] ShadowDiagnostics)
    {
        public float[]? ShadowAtlasDepth { get; init; }
        public uint ShadowAtlasEdge { get; init; }
    }
}
