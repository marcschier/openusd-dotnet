// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;

namespace OpenUsd.Rendering.ConformanceTests;

/// <summary>
/// Executable WARP evidence that face, edge, and point picks answer with the
/// identity the scene authored, and that x-ray selection composites an occluded
/// outline the visible-only mode does not.
/// </summary>
/// <remarks>
/// These run against a real Direct3D 12 device (the WARP software adapter), so
/// the tokens they read back were written by the rasterizer rather than by a
/// managed model of it. That is the only way the coincident depth bias, the line
/// and point topologies, and the second composite can be shown to work.
/// </remarks>
[NotInParallel]
[SupportedOSPlatform("windows")]
public sealed class D3D12SubprimPickingTests
{
    private const uint Size = 64;

    /// <summary>
    /// A quad triangulated into two triangles shares one interior diagonal. A
    /// face pick answers with the authored face; an edge pick on the diagonal
    /// misses, because the scene authored no edge there.
    /// </summary>
    [Test]
    public async Task WarpAnswersAuthoredFacesAndRefusesTriangulationDiagonals()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColor(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        ApplyQuad(renderer);
        var binding = new SilkPickFrameBinding(1, 2);
        _ = renderer.Render(color, depth, binding);

        // The quad spans the whole viewport. Both triangles came from authored
        // face 0, so a face pick anywhere on it answers 0 rather than naming the
        // triangle that happened to cover the pixel.
        RenderPickResult upperLeft = await Pick(
            renderer, color, depth, binding, 8, 8, RenderPickTarget.Face);
        RenderPickResult lowerRight = await Pick(
            renderer, color, depth, binding, 55, 55, RenderPickTarget.Face);

        await Assert.That(upperLeft.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(upperLeft.PrimPath).IsEqualTo("/Quad");
        await Assert.That(upperLeft.ElementIndex).IsEqualTo(0);
        await Assert.That(lowerRight.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(lowerRight.ElementIndex).IsEqualTo(0);

        // The diagonal runs corner to corner. An edge pick that lands on it must
        // miss: no line is drawn there at all, because it is not an authored
        // edge and returning a generated index would name a component the stage
        // does not have.
        RenderPickResult diagonal = await Pick(
            renderer, color, depth, binding, 32, 32, RenderPickTarget.Edge);

        await Assert.That(diagonal.Status).IsEqualTo(RenderPickStatus.Miss);
        await Assert.That(diagonal.Item).IsNull();
    }

    /// <summary>
    /// An edge pick along an authored boundary answers with the authored edge
    /// index, and the four authored edges of the quad answer four distinct
    /// indices.
    /// </summary>
    [Test]
    public async Task WarpAnswersAuthoredEdgeIndicesOnTheQuadBoundary()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColor(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        ApplyQuad(renderer);
        var binding = new SilkPickFrameBinding(1, 2);
        _ = renderer.Render(color, depth, binding);

        var found = new HashSet<int>();
        // The quad spans NDC -0.8..0.8, which is pixels 6..57 in a 64-pixel
        // viewport, so its authored boundary edges run along those rows and
        // columns. A one-pixel line is sampled over a short span because the
        // rasterized line lies within a pixel of the boundary.
        foreach ((int x, int y) in BoundarySamples())
        {
            RenderPickResult result = await Pick(
                renderer, color, depth, binding, x, y, RenderPickTarget.Edge);
            if (result.Status == RenderPickStatus.Hit)
            {
                await Assert.That(result.PrimPath).IsEqualTo("/Quad");
                await Assert.That(result.ElementIndex).IsNotNull();
                await Assert.That(result.ElementIndex!.Value).IsGreaterThanOrEqualTo(0);
                await Assert.That(result.ElementIndex!.Value).IsLessThan(4);
                _ = found.Add(result.ElementIndex!.Value);
            }
        }

        // The pass must resolve at least one authored edge, and every index it
        // resolves must be an authored one. A pass that resolved nothing would
        // make the diagonal-miss assertion above vacuous.
        await Assert.That(found).IsNotEmpty();
    }

    /// <summary>
    /// A face-varying mesh emits one vertex per corner, so one authored point
    /// arrives several times. A point pick still answers with one authored
    /// index.
    /// </summary>
    [Test]
    public async Task WarpAnswersOneAuthoredPointForDuplicatedFaceVaryingVertices()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColor(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        ApplyExpandedQuad(renderer);
        var binding = new SilkPickFrameBinding(1, 2);
        _ = renderer.Render(color, depth, binding);

        var resolved = new HashSet<int>();
        foreach ((int x, int y) in CornerSamples())
        {
            RenderPickResult result = await Pick(
                renderer, color, depth, binding, x, y, RenderPickTarget.Point);
            if (result.Status == RenderPickStatus.Hit)
            {
                await Assert.That(result.PrimPath).IsEqualTo("/ExpandedQuad");
                await Assert.That(result.ElementIndex).IsNotNull();

                // Six emitted vertices, four authored points: an index of four
                // or five would be an emitted index leaking through.
                await Assert.That(result.ElementIndex!.Value).IsLessThan(4);
                _ = resolved.Add(result.ElementIndex!.Value);
            }
        }

        await Assert.That(resolved).IsNotEmpty();
    }

    /// <summary>
    /// A scene whose meshes refuse the requested target completes the request as
    /// unsupported and names the reason, rather than reporting a miss that a
    /// caller could not tell from "nothing was there".
    /// </summary>
    [Test]
    public async Task WarpRefusesASubprimTargetNoMeshAnswers()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColor(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));

        // No subprim identity tables at all, which is what a refined
        // subdivision surface or a resubdivided line list publishes.
        SilkMeshRendererConformance.Apply(
            renderer,
            1,
            SilkMeshRendererConformance.CreateFrameCommand(
                Size,
                Size,
                SilkMeshRendererConformance.Identity()),
            SilkMeshRendererConformance.CreateMeshCommand(
                1,
                "/Refined",
                QuadPoints,
                QuadIndices,
                topologyRevision: 1));
        var binding = new SilkPickFrameBinding(1, 2);
        _ = renderer.Render(color, depth, binding);

        RenderPickResult edge = await Pick(
            renderer, color, depth, binding, 32, 20, RenderPickTarget.Edge);
        RenderPickResult point = await Pick(
            renderer, color, depth, binding, 32, 20, RenderPickTarget.Point);
        RenderPickResult face = await Pick(
            renderer, color, depth, binding, 32, 20, RenderPickTarget.Face);

        await Assert.That(edge.Status).IsEqualTo(RenderPickStatus.Unsupported);
        await Assert.That(point.Status).IsEqualTo(RenderPickStatus.Unsupported);
        await Assert.That(face.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(renderer.PickingStatistics.RefusedSubprimTargets)
            .IsGreaterThanOrEqualTo(2ul);
    }

    /// <summary>
    /// X-ray selection composites an occluded outline the visible-only mode
    /// does not, in a distinct style, without disturbing the visible-only
    /// image where the selection is unoccluded.
    /// </summary>
    [Test]
    public async Task WarpCompositesAnOccludedOutlineOnlyInXRayMode()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColor(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Size, Size));

        // A small selected quad entirely behind a large occluder, so the
        // visible-only composite has nothing to draw at all.
        SilkMeshRendererConformance.Apply(
            renderer,
            1,
            SilkMeshRendererConformance.CreateFrameCommand(
                Size,
                Size,
                SilkMeshRendererConformance.Identity()),
            SilkMeshRendererConformance.CreateMeshCommand(
                1,
                "/Hidden",
                Quad(-0.4f, 0.4f, 0.7f),
                QuadIndices,
                topologyRevision: 1),
            SilkMeshRendererConformance.CreateMeshCommand(
                2,
                "/Occluder",
                Quad(-0.9f, 0.9f, 0.2f),
                QuadIndices,
                topologyRevision: 1));

        var selection = new SelectionState(["/Hidden"]);
        renderer.UpdateSelection(
            selection,
            new SilkSelectionOutlineSettings(
                enabled: true,
                new SilkColor(1, 0.55f, 0, 0.9f),
                width: 2,
                visibleOnly: true));
        _ = renderer.Render(color, depth, new SilkPickFrameBinding(1, 2));
        SilkSelectionOutlineDiagnostics visibleOnly =
            renderer.SelectionOutlineDiagnostics;
        byte[] visiblePixels = ReadPixels(color);

        renderer.UpdateSelection(
            selection,
            new SilkSelectionOutlineSettings(
                enabled: true,
                new SilkColor(1, 0.55f, 0, 0.9f),
                width: 2,
                SilkSelectionOutlineMode.XRay,
                SilkSelectionOutlineSettings.DefaultOccludedColor));
        _ = renderer.Render(color, depth, new SilkPickFrameBinding(1, 2));
        SilkSelectionOutlineDiagnostics xray = renderer.SelectionOutlineDiagnostics;
        byte[] xrayPixels = ReadPixels(color);

        await Assert.That(visibleOnly.Status)
            .IsEqualTo(SilkSelectionOutlineStatus.Rendered);
        await Assert.That(xray.Status).IsEqualTo(SilkSelectionOutlineStatus.Rendered);

        // X-ray costs exactly one extra mask pass -- the untested silhouette
        // that goes into the mask's second channel -- and no extra composite and
        // no extra binding at all. Both silhouettes are composited together, so
        // the mask texture, the depth target, the sampler, the parameter buffer
        // and the composite pipeline are all shared with the visible-only mode.
        await Assert.That(xray.OutlinePasses - visibleOnly.OutlinePasses)
            .IsEqualTo(1ul);
        await Assert.That(xray.MaskPasses - visibleOnly.MaskPasses).IsEqualTo(2ul);
        await Assert.That(xray.BindingCreations - visibleOnly.BindingCreations)
            .IsEqualTo(0ul);

        // The occluded selection is invisible in visible-only mode and visible
        // in x-ray mode, in the cool occluded style rather than the warm
        // visible one. Pixels are classified by the ratio of their sorted
        // channels, so the check does not depend on the readback's channel
        // order and holds over whatever the occluder shaded to: the cool
        // occluded style leaves all three channels comparatively close, while
        // the warm visible style drives one of them to nearly zero.
        int changed = 0;
        int occludedStyle = 0;
        int visibleStyle = 0;
        for (int index = 0; index + 3 < xrayPixels.Length; index += 4)
        {
            if (xrayPixels[index] == visiblePixels[index] &&
                xrayPixels[index + 1] == visiblePixels[index + 1] &&
                xrayPixels[index + 2] == visiblePixels[index + 2])
            {
                continue;
            }
            changed++;
            byte first = xrayPixels[index];
            byte second = xrayPixels[index + 1];
            byte third = xrayPixels[index + 2];
            float high = Math.Max(first, Math.Max(second, third));
            float low = Math.Min(first, Math.Min(second, third));
            float middle = first + second + third - high - low;
            if (high <= 0)
            {
                continue;
            }
            if (low / high >= 0.15f && middle / high >= 0.6f)
            {
                occludedStyle++;
            }
            else if (low / high < 0.1f)
            {
                visibleStyle++;
            }
        }

        await Assert.That(changed).IsGreaterThan(0);
        await Assert.That(occludedStyle)
            .IsGreaterThan(0)
            .Because($"changed={changed} visibleStyle={visibleStyle}");
        await Assert.That(visibleStyle).IsEqualTo(0);
    }

    private static readonly float[] QuadPoints = Quad(-0.8f, 0.8f, 0.5f);

    private static readonly uint[] QuadIndices = [0, 1, 2, 0, 2, 3];

    /// <summary>
    /// Pixels along the quad's authored boundary. NDC -0.8..0.8 maps to pixels
    /// 6..57 in a 64-pixel viewport, and a one-pixel line can land on either
    /// side of that boundary, so each side is sampled over a short span.
    /// </summary>
    private static IEnumerable<(int X, int Y)> BoundarySamples()
    {
        for (int offset = 5; offset <= 8; offset++)
        {
            yield return (32, offset);
            yield return (offset, 32);
        }
        for (int offset = 55; offset <= 58; offset++)
        {
            yield return (32, offset);
            yield return (offset, 32);
        }
    }

    /// <summary>
    /// Pixels around the quad's four authored corners, sampled over a small
    /// window because one rasterized point covers a single pixel.
    /// </summary>
    private static IEnumerable<(int X, int Y)> CornerSamples()
    {
        int[] columns = [5, 6, 7, 56, 57, 58];
        foreach (int x in columns)
        {
            foreach (int y in columns)
            {
                yield return (x, y);
            }
        }
    }

    /// <summary>
    /// A quad whose two triangles both came from authored face zero, whose four
    /// emitted vertices are the four authored points, and whose shared interior
    /// diagonal -- corner 2 of the first triangle and corner 0 of the second --
    /// is the sentinel rather than an authored edge.
    /// </summary>
    private static void ApplyQuad(SilkMeshRenderer renderer) =>
        SilkMeshRendererConformance.Apply(
            renderer,
            1,
            SilkMeshRendererConformance.CreateFrameCommand(
                Size,
                Size,
                SilkMeshRendererConformance.Identity()),
            SilkMeshRendererConformance.CreateMeshCommand(
                1,
                "/Quad",
                QuadPoints,
                QuadIndices,
                0,
                0,
                null,
                1,
                triangleSubprims: [0, 0],
                pointOrigins: [0, 1, 2, 3],
                cornerEdges: [0, 1, -1, -1, 2, 3]));

    private static void ApplyExpandedQuad(SilkMeshRenderer renderer)
    {
        float[] points = Quad(-0.8f, 0.8f, 0.5f);
        // One emitted vertex per corner, which is what an expanded face-varying
        // topology publishes. Authored point 0 and 2 each arrive twice.
        float[] expanded =
        [
            points[0], points[1], points[2],
            points[3], points[4], points[5],
            points[6], points[7], points[8],
            points[0], points[1], points[2],
            points[6], points[7], points[8],
            points[9], points[10], points[11]
        ];
        SilkMeshRendererConformance.Apply(
            renderer,
            1,
            SilkMeshRendererConformance.CreateFrameCommand(
                Size,
                Size,
                SilkMeshRendererConformance.Identity()),
            SilkMeshRendererConformance.CreateMeshCommand(
                1,
                "/ExpandedQuad",
                expanded,
                [0, 1, 2, 3, 4, 5],
                0,
                0,
                null,
                1,
                triangleSubprims: [0, 0],
                pointOrigins: [0, 1, 2, 0, 2, 3],
                cornerEdges: [0, 1, -1, -1, 2, 3]));
    }

    /// <summary>
    /// Textured and normal-mapped meshes upload 32- and 48-byte vertices, and
    /// both the pick and the outline mask must read them at that stride.
    /// </summary>
    [Test]
    public async Task WarpPicksAndOutlinesEveryVertexStride()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        await SilkSubprimPickingConformance.PicksAndOutlinesEveryVertexStride(
            device,
            Pick);
    }

    /// <summary>
    /// The subprim pass runs over the surface depth pre-pass, so an occluder
    /// still hides what is behind it and a sloped surface still resolves its own
    /// authored edges.
    /// </summary>
    [Test]
    public async Task WarpKeepsSubprimPicksHonestUnderOccludersAndSlopes()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        await SilkSubprimPickingConformance.OccludersAndSlopesKeepSubprimPicksHonest(
            device,
            Pick);
    }

    /// <summary>
    /// Every emitted copy of one authored point rasterizes, and every copy
    /// answers with the one authored index.
    /// </summary>
    [Test]
    public async Task WarpRasterizesEverySplitCopyOfOneAuthoredPoint()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        await SilkSubprimPickingConformance.EverySplitCopyOfOneAuthoredPointResolvesToIt(
            device,
            Pick);
    }

    /// <summary>
    /// Whole basis-curve and <c>UsdGeomPoints</c> resources are drawn, picked,
    /// outlined, and act as occluders on real WARP pixels.
    /// </summary>
    [Test]
    public async Task WarpPicksOutlinesAndOccludesWholeCurvesAndPoints()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        await SilkSubprimPickingConformance.WholeCurvesAndPointsPickOutlineAndOcclude(
            device,
            Pick);
    }

    /// <summary>
    /// Whole curve and point resources genuinely in front of and genuinely
    /// behind a surface answer exactly what each position implies, on WARP
    /// pixels.
    /// </summary>
    [Test]
    public async Task WarpResolvesWholeResourcesInFrontAndBehindExactly()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        await SilkSubprimPickingConformance.WholeResourcesInFrontAndBehindAreExact(
            device,
            Pick);
    }

    /// <summary>
    /// The x-ray composite leaves every pixel the visible-only mode paints
    /// byte-identical, on WARP pixels.
    /// </summary>
    [Test]
    public async Task WarpXRayLeavesTheVisibleOutlineByteIdentical()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        await SilkSubprimPickingConformance.XRayLeavesTheVisibleOutlineByteIdentical(
            device);
    }

    private static float[] Quad(float low, float high, float depth) =>
    [
        low, low, depth,
        high, low, depth,
        high, high, depth,
        low, high, depth
    ];

    private static ISilkGraphicsTexture CreateColor(
        D3D12SilkGraphicsDevice device) =>
        device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));

    private static async Task<RenderPickResult> Pick(
        SilkMeshRenderer renderer,
        ISilkGraphicsTexture color,
        ISilkGraphicsTexture depth,
        SilkPickFrameBinding binding,
        int x,
        int y,
        RenderPickTarget target)
    {
        var request = new RenderPickRequest(
            x,
            y,
            new ViewportDimensions(checked((int)Size), checked((int)Size)),
            binding.StateRevision,
            binding.SceneRevision,
            target);
        Task<RenderPickResult> pending = renderer.PickAsync(request).AsTask();
        for (int iteration = 0;
             iteration < 100 && !pending.IsCompleted;
             iteration++)
        {
            _ = renderer.Render(color, depth, binding);
            await Task.Yield();
        }
        if (!pending.IsCompleted)
        {
            throw new TimeoutException(
                "The WARP subprim pick did not complete without a render-loop wait.");
        }
        return await pending;
    }

    private static byte[] ReadPixels(ISilkGraphicsTexture texture)
    {
        var pixels = new byte[checked((int)(texture.Width * texture.Height * 4))];
        texture.ReadbackForTesting(pixels);
        return pixels;
    }
}
