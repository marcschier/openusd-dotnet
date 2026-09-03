// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.Vulkan;

namespace OpenUsd.Rendering.ConformanceTests;

/// <summary>
/// Executable SwiftShader evidence that the ABI v22 authored subprim identity
/// and the x-ray selection composite behave identically on Vulkan and on the
/// Direct3D 12 WARP adapter.
/// </summary>
/// <remarks>
/// The same scenes and the same assertions run on both backends deliberately.
/// The edge and point passes depend on a line and point topology, a coincident
/// depth bias, and a depth convention that a managed model cannot verify, and a
/// pick that answered differently on two backends would make the identity the
/// contract promises backend-specific.
/// </remarks>
[NotInParallel]
public sealed class VulkanSubprimPickingTests
{
    private const uint Size = 64;

    /// <summary>
    /// A face pick answers with the authored face, and an edge pick that lands
    /// on the triangulation diagonal misses.
    /// </summary>
    [Test]
    public async Task SwiftShaderAnswersAuthoredFacesAndRefusesDiagonals()
    {
        if (!IsSupportedPlatform())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColor(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        ApplyQuad(renderer);
        var binding = new SilkPickFrameBinding(1, 2);
        _ = renderer.Render(color, depth, binding);

        RenderPickResult face = await Pick(
            renderer, color, depth, binding, 20, 20, RenderPickTarget.Face);
        RenderPickResult diagonal = await Pick(
            renderer, color, depth, binding, 32, 32, RenderPickTarget.Edge);

        await Assert.That(face.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(face.PrimPath).IsEqualTo("/Quad");
        await Assert.That(face.ElementIndex).IsEqualTo(0);
        await Assert.That(diagonal.Status).IsEqualTo(RenderPickStatus.Miss);
    }

    /// <summary>
    /// Edge and point picks answer with authored indices rather than emitted
    /// ones, including on a mesh whose face-varying topology duplicated its
    /// authored points across corners.
    /// </summary>
    [Test]
    public async Task SwiftShaderAnswersAuthoredEdgesAndDeduplicatedPoints()
    {
        if (!IsSupportedPlatform())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColor(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        ApplyExpandedQuad(renderer);
        var binding = new SilkPickFrameBinding(1, 2);
        _ = renderer.Render(color, depth, binding);

        var edges = new HashSet<int>();
        var points = new HashSet<int>();
        for (int offset = 4; offset <= 9; offset++)
        {
            foreach ((int x, int y) in new[]
            {
                (32, offset),
                (offset, 32),
                (32, 63 - offset),
                (63 - offset, 32)
            })
            {
                RenderPickResult edge = await Pick(
                    renderer, color, depth, binding, x, y, RenderPickTarget.Edge);
                if (edge.Status == RenderPickStatus.Hit)
                {
                    await Assert.That(edge.ElementIndex!.Value).IsLessThan(4);
                    _ = edges.Add(edge.ElementIndex!.Value);
                }
            }
            for (int column = 4; column <= 9; column++)
            {
                foreach ((int x, int y) in new[]
                {
                    (column, offset),
                    (63 - column, offset),
                    (column, 63 - offset),
                    (63 - column, 63 - offset)
                })
                {
                    RenderPickResult point = await Pick(
                        renderer,
                        color,
                        depth,
                        binding,
                        x,
                        y,
                        RenderPickTarget.Point);
                    if (point.Status == RenderPickStatus.Hit)
                    {
                        // Six emitted vertices, four authored points.
                        await Assert.That(point.ElementIndex!.Value).IsLessThan(4);
                        _ = points.Add(point.ElementIndex!.Value);
                    }
                }
            }
        }

        await Assert.That(edges).IsNotEmpty();
        await Assert.That(points).IsNotEmpty();
    }

    /// <summary>
    /// X-ray composites an occluded outline the visible-only mode does not, and
    /// costs exactly one extra mask pass -- no extra composite and no extra
    /// binding, because both silhouettes are composited together.
    /// </summary>
    [Test]
    public async Task SwiftShaderCompositesAnOccludedOutlineOnlyInXRayMode()
    {
        if (!IsSupportedPlatform())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColor(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Size, Size));
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
        await Assert.That(xray.OutlinePasses - visibleOnly.OutlinePasses)
            .IsEqualTo(1ul);
        await Assert.That(xray.MaskPasses - visibleOnly.MaskPasses).IsEqualTo(2ul);
        await Assert.That(xray.BindingCreations - visibleOnly.BindingCreations)
            .IsEqualTo(0ul);

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
            float high = Math.Max(
                xrayPixels[index],
                Math.Max(xrayPixels[index + 1], xrayPixels[index + 2]));
            float low = Math.Min(
                xrayPixels[index],
                Math.Min(xrayPixels[index + 1], xrayPixels[index + 2]));
            float middle =
                xrayPixels[index] + xrayPixels[index + 1] + xrayPixels[index + 2] -
                high - low;
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
        await Assert.That(occludedStyle).IsGreaterThan(0);
        await Assert.That(visibleStyle).IsEqualTo(0);
    }

    private static readonly uint[] QuadIndices = [0, 1, 2, 0, 2, 3];

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
                Quad(-0.8f, 0.8f, 0.5f),
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
    public async Task SwiftShaderPicksAndOutlinesEveryVertexStride()
    {
        if (!IsSupportedPlatform())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
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
    public async Task SwiftShaderKeepsSubprimPicksHonestUnderOccludersAndSlopes()
    {
        if (!IsSupportedPlatform())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkSubprimPickingConformance.OccludersAndSlopesKeepSubprimPicksHonest(
            device,
            Pick);
    }

    /// <summary>
    /// Every emitted copy of one authored point rasterizes, and every copy
    /// answers with the one authored index.
    /// </summary>
    [Test]
    public async Task SwiftShaderRasterizesEverySplitCopyOfOneAuthoredPoint()
    {
        if (!IsSupportedPlatform())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkSubprimPickingConformance.EverySplitCopyOfOneAuthoredPointResolvesToIt(
            device,
            Pick);
    }

    /// <summary>
    /// Whole basis-curve and <c>UsdGeomPoints</c> resources are drawn, picked,
    /// outlined, and act as occluders on real SwiftShader pixels.
    /// </summary>
    [Test]
    public async Task SwiftShaderPicksOutlinesAndOccludesWholeCurvesAndPoints()
    {
        if (!IsSupportedPlatform())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkSubprimPickingConformance.WholeCurvesAndPointsPickOutlineAndOcclude(
            device,
            Pick);
    }

    /// <summary>
    /// Whole curve and point resources genuinely in front of and genuinely
    /// behind a surface answer exactly what each position implies, on
    /// SwiftShader pixels.
    /// </summary>
    [Test]
    public async Task SwiftShaderResolvesWholeResourcesInFrontAndBehindExactly()
    {
        if (!IsSupportedPlatform())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkSubprimPickingConformance.WholeResourcesInFrontAndBehindAreExact(
            device,
            Pick);
    }

    /// <summary>
    /// The x-ray composite leaves every pixel the visible-only mode paints
    /// byte-identical, on SwiftShader pixels.
    /// </summary>
    [Test]
    public async Task SwiftShaderXRayLeavesTheVisibleOutlineByteIdentical()
    {
        if (!IsSupportedPlatform())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
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

    private static bool IsSupportedPlatform() =>
        OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    private static ISilkGraphicsTexture CreateColor(
        VulkanSilkGraphicsDevice device) =>
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
                "The SwiftShader subprim pick did not complete without a wait.");
        }
        return await pending;
    }

    private static byte[] ReadPixels(ISilkGraphicsTexture texture)
    {
        var pixels = new byte[
            checked((int)(texture.Width * texture.Height * 4))];
        texture.ReadbackForTesting(pixels);
        return pixels;
    }
}
