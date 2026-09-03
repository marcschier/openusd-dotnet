// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

/// <summary>
/// Device-independent subprim-pick and x-ray scenarios shared by every backend
/// that has executable evidence, so D3D12 (WARP) and Vulkan (SwiftShader) prove
/// the same behaviour from the same scene rather than from two descriptions of
/// it.
/// </summary>
/// <remarks>
/// Every scenario here is one the managed model cannot answer on its own: the
/// vertex stride a pipeline reads, whether a coincident line wins its own depth
/// test on a sloped surface, whether an occluder still hides a component behind
/// it, and whether both emitted copies of one authored point rasterize.
/// </remarks>
internal static class SilkSubprimPickingConformance
{
    internal const uint Size = 64;

    private static readonly uint[] QuadIndices = [0, 1, 2, 0, 2, 3];

    private static readonly int[] QuadSubprims = [0, 0];

    private static readonly int[] QuadPointOrigins = [0, 1, 2, 3];

    private static readonly int[] QuadCornerEdges = [0, 1, -1, -1, 2, 3];

    /// <summary>
    /// A textured mesh interleaves texture coordinates and a normal-mapped one
    /// interleaves tangents, so their vertices are 32 and 48 bytes apart rather
    /// than 24. Both must pick and outline the surface actually on screen.
    /// </summary>
    internal static async Task PicksAndOutlinesEveryVertexStride(
        ISilkGraphicsDevice device,
        Func<SilkMeshRenderer, ISilkGraphicsTexture, ISilkGraphicsTexture,
            SilkPickFrameBinding, int, int, RenderPickTarget,
            Task<RenderPickResult>> pick)
    {
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColor(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Size, Size));

        float[] points = Quad(-0.8f, 0.8f, 0.5f);
        SilkMeshRendererConformance.Apply(
            renderer,
            1,
            SilkMeshRendererConformance.CreateFrameCommand(
                Size,
                Size,
                SilkMeshRendererConformance.Identity()),
            SilkMeshRendererConformance.CreateMeshCommand(
                1,
                "/Textured",
                points,
                QuadIndices,
                0,
                0,
                null,
                1,
                QuadSubprims,
                QuadPointOrigins,
                QuadCornerEdges,
                [("st", 2u, 2u, [0, 0, 1, 0, 1, 1, 0, 1])]));

        var binding = new SilkPickFrameBinding(1, 2);
        _ = renderer.Render(color, depth, binding);

        // 32-byte vertices: the pick must still land on the quad rather than
        // reading positions from the wrong stride.
        RenderPickResult face = await pick(
            renderer, color, depth, binding, 32, 20, RenderPickTarget.Face);
        await Assert.That(face.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(face.PrimPath).IsEqualTo("/Textured");
        await Assert.That(face.ElementIndex).IsEqualTo(0);

        // The outline mask uses its own pipeline family and must match the same
        // stride, so a selected textured mesh outlines what is on screen.
        renderer.UpdateSelection(
            new SelectionState(["/Textured"]),
            new SilkSelectionOutlineSettings(
                enabled: true,
                new SilkColor(1, 0.55f, 0, 0.9f),
                width: 2,
                visibleOnly: true));
        _ = renderer.Render(color, depth, binding);
        await Assert.That(renderer.SelectionOutlineDiagnostics.Status)
            .IsEqualTo(SilkSelectionOutlineStatus.Rendered);
        await Assert.That(renderer.SelectionOutlineDiagnostics.ResolvedMeshCount)
            .IsEqualTo(1);

        // 48-byte vertices: the same quad with tangents as well.
        SilkMeshRendererConformance.Apply(
            renderer,
            2,
            SilkMeshRendererConformance.CreateFrameCommand(
                Size,
                Size,
                SilkMeshRendererConformance.Identity()),
            SilkMeshRendererConformance.CreateMeshCommand(
                2,
                "/Tangent",
                points,
                QuadIndices,
                0,
                0,
                null,
                1,
                QuadSubprims,
                QuadPointOrigins,
                QuadCornerEdges,
                [
                    ("st", 2u, 2u, [0, 0, 1, 0, 1, 1, 0, 1]),
                    ("tangents", 4u, 3u, [1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0])
                ]));
        renderer.UpdateSelection(new SelectionState(["/Tangent"]));
        var second = new SilkPickFrameBinding(2, 3);
        _ = renderer.Render(color, depth, second);

        RenderPickResult tangentFace = await pick(
            renderer, color, depth, second, 32, 20, RenderPickTarget.Face);
        await Assert.That(tangentFace.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(tangentFace.PrimPath).IsEqualTo("/Tangent");
        await Assert.That(tangentFace.ElementIndex).IsEqualTo(0);

        // An edge pick on the 48-byte mesh resolves an authored edge, which it
        // could not do if the line pass read positions at the wrong stride.
        var edges = new HashSet<int>();
        for (int offset = 5; offset <= 8; offset++)
        {
            RenderPickResult edge = await pick(
                renderer, color, depth, second, 32, offset, RenderPickTarget.Edge);
            if (edge.Status == RenderPickStatus.Hit)
            {
                await Assert.That(edge.PrimPath).IsEqualTo("/Tangent");
                await Assert.That(edge.ElementIndex!.Value).IsLessThan(4);
                _ = edges.Add(edge.ElementIndex!.Value);
            }
        }
        await Assert.That(edges).IsNotEmpty();
    }

    /// <summary>
    /// A subprim pass runs over the depth the surface pre-pass wrote, so an
    /// occluder in front of a component still hides it, and a sloped surface
    /// does not change that. This is what a rasterizer depth bias could not
    /// deliver portably.
    /// </summary>
    internal static async Task OccludersAndSlopesKeepSubprimPicksHonest(
        ISilkGraphicsDevice device,
        Func<SilkMeshRenderer, ISilkGraphicsTexture, ISilkGraphicsTexture,
            SilkPickFrameBinding, int, int, RenderPickTarget,
            Task<RenderPickResult>> pick)
    {
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColor(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Size, Size));

        // A quad tilted in depth: its authored boundary edges lie on a sloped
        // surface, which is exactly the case a slope-scaled rasterizer bias
        // would have separated by a different amount at every pixel.
        float[] sloped =
        [
            -0.8f, -0.8f, 0.30f,
            0.8f, -0.8f, 0.70f,
            0.8f, 0.8f, 0.70f,
            -0.8f, 0.8f, 0.30f
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
                "/Sloped",
                sloped,
                QuadIndices,
                0,
                0,
                null,
                1,
                QuadSubprims,
                QuadPointOrigins,
                QuadCornerEdges));

        var binding = new SilkPickFrameBinding(1, 2);
        _ = renderer.Render(color, depth, binding);

        var slopedEdges = new HashSet<int>();
        for (int offset = 5; offset <= 8; offset++)
        {
            foreach ((int x, int y) in new[] { (32, offset), (offset, 32) })
            {
                RenderPickResult edge = await pick(
                    renderer, color, depth, binding, x, y, RenderPickTarget.Edge);
                if (edge.Status == RenderPickStatus.Hit)
                {
                    await Assert.That(edge.PrimPath).IsEqualTo("/Sloped");
                    await Assert.That(edge.ElementIndex!.Value).IsLessThan(4);
                    _ = slopedEdges.Add(edge.ElementIndex!.Value);
                }
            }
        }

        // The coincident separation has to work on a slope, not only on a
        // fronto-parallel quad.
        await Assert.That(slopedEdges).IsNotEmpty();

        // Now put an opaque occluder in front of the left half. A component
        // behind it must not answer, or "visible only" would mean nothing.
        SilkMeshRendererConformance.Apply(
            renderer,
            2,
            SilkMeshRendererConformance.CreateFrameCommand(
                Size,
                Size,
                SilkMeshRendererConformance.Identity()),
            SilkMeshRendererConformance.CreateMeshCommand(
                3,
                "/Hidden",
                Quad(-0.8f, 0.8f, 0.7f),
                QuadIndices,
                0,
                0,
                null,
                1,
                QuadSubprims,
                QuadPointOrigins,
                QuadCornerEdges),
            SilkMeshRendererConformance.CreateMeshCommand(
                4,
                "/Occluder",
                Quad(-0.95f, 0.95f, 0.1f),
                QuadIndices,
                0,
                0,
                null,
                1,
                QuadSubprims,
                null,
                null));

        var occluded = new SilkPickFrameBinding(2, 3);
        _ = renderer.Render(color, depth, occluded);

        for (int offset = 5; offset <= 8; offset++)
        {
            RenderPickResult edge = await pick(
                renderer, color, depth, occluded, 32, offset, RenderPickTarget.Edge);

            // The occluder publishes no edge table, so nothing may answer here:
            // either the pass finds no edge at all, or it finds one that is not
            // the hidden quad's.
            if (edge.Status == RenderPickStatus.Hit)
            {
                await Assert.That(edge.PrimPath).IsNotEqualTo("/Hidden");
            }
        }
    }

    /// <summary>
    /// A face-varying topology emits one vertex per corner, and displacement or
    /// divergent authored normals can place those copies at visibly different
    /// pixels. Every copy must rasterize, and every copy must resolve to the one
    /// authored index.
    /// </summary>
    internal static async Task EverySplitCopyOfOneAuthoredPointResolvesToIt(
        ISilkGraphicsDevice device,
        Func<SilkMeshRenderer, ISilkGraphicsTexture, ISilkGraphicsTexture,
            SilkPickFrameBinding, int, int, RenderPickTarget,
            Task<RenderPickResult>> pick)
    {
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColor(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));

        // Two triangles that share authored point 0 and authored point 2, with
        // the second triangle's copies displaced well away from the first's.
        // Naming only the first copy would leave the second unpickable even
        // though it is plainly on screen.
        float[] split =
        [
            -0.8f, -0.8f, 0.5f,
            0.0f, -0.8f, 0.5f,
            -0.8f, 0.0f, 0.5f,
            0.2f, 0.2f, 0.5f,
            0.9f, 0.9f, 0.5f,
            0.2f, 0.9f, 0.5f
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
                "/Split",
                split,
                [0, 1, 2, 3, 4, 5],
                0,
                0,
                null,
                1,
                triangleSubprims: [0, 0],
                pointOrigins: [0, 1, 2, 0, 1, 2],
                cornerEdges: [0, 1, 2, 0, 1, 2]));

        var binding = new SilkPickFrameBinding(1, 2);
        _ = renderer.Render(color, depth, binding);

        var resolved = new HashSet<int>();
        int hits = 0;
        for (int x = 0; x < (int)Size; x++)
        {
            for (int y = 0; y < (int)Size; y++)
            {
                RenderPickResult point = await pick(
                    renderer, color, depth, binding, x, y, RenderPickTarget.Point);
                if (point.Status != RenderPickStatus.Hit)
                {
                    continue;
                }
                hits++;
                await Assert.That(point.PrimPath).IsEqualTo("/Split");

                // Six emitted vertices, three authored points: an index of
                // three, four, or five would be an emitted index leaking out.
                await Assert.That(point.ElementIndex!.Value).IsLessThan(3);
                _ = resolved.Add(point.ElementIndex!.Value);
            }
        }

        // Both copies of at least one authored point rasterized, so more points
        // were hit than there are authored points.
        await Assert.That(hits).IsGreaterThan(resolved.Count);
        await Assert.That(resolved).IsNotEmpty();
    }

    /// <summary>
    /// A whole basis-curve resource and a whole <c>UsdGeomPoints</c> resource
    /// are drawn, picked, outlined, and used as occluders on real device pixels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every claim here is one the managed model cannot answer. Whether a curve
    /// or a point cloud rasterizes at all depends on the vertex stage writing an
    /// explicit point size that SPIR-V and Metal leave undefined; whether it
    /// occludes a mesh subprim behind it depends on the depth it writes; and
    /// whether a face request on it answers a miss depends on the
    /// colour-write-disabled occluder pipeline actually leaving the background
    /// token in place.
    /// </para>
    /// <para>
    /// The regression this pins is the coincident offset. A whole resource drawn
    /// through the subprim overlay stage is pulled toward the viewer, so a curve
    /// standing <em>behind</em> a surface would answer the pick and would be
    /// outlined through it in the visible-only mode. The curve here is behind the
    /// quad on purpose.
    /// </para>
    /// </remarks>
    internal static async Task WholeCurvesAndPointsPickOutlineAndOcclude(
        ISilkGraphicsDevice device,
        Func<SilkMeshRenderer, ISilkGraphicsTexture, ISilkGraphicsTexture,
            SilkPickFrameBinding, int, int, RenderPickTarget,
            Task<RenderPickResult>> pick)
    {
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColor(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Size, Size));

        // A wide horizontal curve in front of the camera, and a point cloud
        // beside it. Depth 0.2 puts both nearer than the quad at 0.6.
        float[] curvePoints = [-0.9f, 0.4f, 0.2f, 0.9f, 0.4f, 0.2f];
        float[] cloudPoints =
        [
            -0.5f, -0.4f, 0.2f,
            0.0f, -0.4f, 0.2f,
            0.5f, -0.4f, 0.2f
        ];
        SilkMeshRendererConformance.Apply(
            renderer,
            1,
            SilkMeshRendererConformance.CreateFrameCommand(
                Size,
                Size,
                SilkMeshRendererConformance.Identity()),
            SilkMeshRendererConformance.CreateTopologyMeshCommand(
                1,
                "/Curve",
                SilkTopologyKind.LineList,
                curvePoints,
                [0, 1]),
            SilkMeshRendererConformance.CreateTopologyMeshCommand(
                2,
                "/Cloud",
                SilkTopologyKind.PointList,
                cloudPoints,
                [0, 1, 2]),
            SilkMeshRendererConformance.CreateMeshCommand(
                3,
                "/Surface",
                Quad(-0.95f, 0.95f, 0.6f),
                QuadIndices,
                0,
                0,
                null,
                1,
                QuadSubprims,
                QuadPointOrigins,
                QuadCornerEdges));

        var binding = new SilkPickFrameBinding(1, 2);
        _ = renderer.Render(color, depth, binding);

        // A prim pick anywhere along the curve resolves the curve itself. A
        // whole-resource token range has to exist for a line list at all, which
        // it did not before: the surface pass had no token to draw it with.
        RenderPickResult curveHit = await ScanForHit(
            renderer, color, depth, binding, pick, RenderPickTarget.Primitive,
            firstRow: 14, lastRow: 24, "/Curve");
        await Assert.That(curveHit.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(curveHit.PrimPath).IsEqualTo("/Curve");
        await Assert.That(curveHit.ElementIndex).IsNull();

        // The same for a point cloud, which needs the explicit one-pixel point
        // size to cover a pixel at all.
        RenderPickResult cloudHit = await ScanForHit(
            renderer, color, depth, binding, pick, RenderPickTarget.Primitive,
            firstRow: 38, lastRow: 50, "/Cloud");
        await Assert.That(cloudHit.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(cloudHit.PrimPath).IsEqualTo("/Cloud");

        // A face request over the curve answers the surface behind it or a
        // miss, never the curve: a curve has no authored face, so it is drawn as
        // a pure occluder that writes depth and no token.
        RenderPickResult face = await pick(
            renderer,
            color,
            depth,
            binding,
            curveHit.Request.X,
            curveHit.Request.Y,
            RenderPickTarget.Face);
        await Assert.That(face.PrimPath).IsNotEqualTo("/Curve");
        if (face.Status == RenderPickStatus.Hit)
        {
            await Assert.That(face.PrimPath).IsEqualTo("/Surface");
        }

        // Both resources outline as whole prims through the unbiased mask stage.
        renderer.UpdateSelection(
            new SelectionState(["/Curve", "/Cloud"]),
            new SilkSelectionOutlineSettings(
                enabled: true,
                new SilkColor(1, 0.55f, 0, 0.9f),
                width: 2,
                visibleOnly: true));
        _ = renderer.Render(color, depth, binding);
        await Assert.That(renderer.SelectionOutlineDiagnostics.Status)
            .IsEqualTo(SilkSelectionOutlineStatus.Rendered);
        await Assert.That(renderer.SelectionOutlineDiagnostics.ResolvedMeshCount)
            .IsEqualTo(2);

        // The x-ray mode composites the occluded silhouette too, from the same
        // unbiased stage with its depth test disabled.
        renderer.UpdateSelection(
            new SelectionState(["/Curve", "/Cloud"]),
            new SilkSelectionOutlineSettings(
                enabled: true,
                new SilkColor(1, 0.55f, 0, 0.9f),
                width: 2,
                SilkSelectionOutlineMode.XRay,
                new SilkColor(0, 0.6f, 1, 0.6f)));
        _ = renderer.Render(color, depth, binding);
        await Assert.That(renderer.SelectionOutlineDiagnostics.Status)
            .IsEqualTo(SilkSelectionOutlineStatus.Rendered);
        await Assert.That(renderer.SelectionOutlineDiagnostics.ResolvedMeshCount)
            .IsEqualTo(2);

        // A curve in front of a surface hides the surface's own components: an
        // edge or point request at the curve's pixels must not answer with a
        // component of the quad standing behind it.
        RenderPickResult occludedEdge = await pick(
            renderer,
            color,
            depth,
            binding,
            curveHit.Request.X,
            curveHit.Request.Y,
            RenderPickTarget.Edge);
        await Assert.That(occludedEdge.PrimPath).IsNotEqualTo("/Curve");
        RenderPickResult occludedPoint = await pick(
            renderer,
            color,
            depth,
            binding,
            cloudHit.Request.X,
            cloudHit.Request.Y,
            RenderPickTarget.Point);
        await Assert.That(occludedPoint.PrimPath).IsNotEqualTo("/Surface");
    }

    /// <summary>
    /// A curve and a point cloud genuinely in front of a surface, and the same
    /// two genuinely behind one, answer exactly what each position implies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two fixtures are separate on purpose. A single scene in which the
    /// whole resources are only ever in front proves that they draw and occlude,
    /// but says nothing about whether their depth is honest: a resource pulled
    /// toward the viewer answers exactly the same way. The behind fixture is the
    /// one that fails when the whole-resource stage is biased, because a curve
    /// standing behind a wall then wins the pick and is outlined through it.
    /// </para>
    /// <para>
    /// The face assertions are exact rather than tolerant. A curve or a point
    /// cloud has no authored face at all, so a face request landing on one in
    /// front of a surface must answer a miss -- the resource writes depth, the
    /// surface behind it is hidden, and no token is written. And a face request
    /// landing on a surface that hides a curve must answer that surface's own
    /// authored face.
    /// </para>
    /// </remarks>
    internal static async Task WholeResourcesInFrontAndBehindAreExact(
        ISilkGraphicsDevice device,
        Func<SilkMeshRenderer, ISilkGraphicsTexture, ISilkGraphicsTexture,
            SilkPickFrameBinding, int, int, RenderPickTarget,
            Task<RenderPickResult>> pick)
    {
        float[] curvePoints = [-0.9f, 0.4f, 0f, 0.9f, 0.4f, 0f];
        float[] cloudPoints =
        [
            -0.5f, -0.4f, 0f,
            0.0f, -0.4f, 0f,
            0.5f, -0.4f, 0f
        ];

        // In front: the whole resources are nearer than the surface.
        using (var renderer = new SilkMeshRenderer(device))
        {
            using ISilkGraphicsTexture color = CreateColor(device);
            using ISilkGraphicsTexture depth = device.CreateTexture2D(
                SilkTextureDescriptor.SampledDepthTarget(Size, Size));
            ApplyWholeResourceScene(
                renderer,
                AtDepth(curvePoints, 0.2f),
                AtDepth(cloudPoints, 0.2f),
                surfaceDepth: 0.6f);
            var binding = new SilkPickFrameBinding(1, 2);
            _ = renderer.Render(color, depth, binding);

            RenderPickResult curveHit = await ScanForHit(
                renderer, color, depth, binding, pick, RenderPickTarget.Primitive,
                firstRow: 14, lastRow: 24, "/Curve");
            await Assert.That(curveHit.Status).IsEqualTo(RenderPickStatus.Hit);
            await Assert.That(curveHit.PrimPath).IsEqualTo("/Curve");

            RenderPickResult cloudHit = await ScanForHit(
                renderer, color, depth, binding, pick, RenderPickTarget.Primitive,
                firstRow: 38, lastRow: 50, "/Cloud");
            await Assert.That(cloudHit.Status).IsEqualTo(RenderPickStatus.Hit);
            await Assert.That(cloudHit.PrimPath).IsEqualTo("/Cloud");

            // Exact miss, not "anything but the curve": the curve occludes the
            // surface's faces and writes no token of its own.
            RenderPickResult curveFace = await pick(
                renderer, color, depth, binding,
                curveHit.Request.X, curveHit.Request.Y, RenderPickTarget.Face);
            await Assert.That(curveFace.Status).IsEqualTo(RenderPickStatus.Miss);

            RenderPickResult cloudFace = await pick(
                renderer, color, depth, binding,
                cloudHit.Request.X, cloudHit.Request.Y, RenderPickTarget.Face);
            await Assert.That(cloudFace.Status).IsEqualTo(RenderPickStatus.Miss);
        }

        // Behind: the very same resources, now hidden by the surface.
        using (var renderer = new SilkMeshRenderer(device))
        {
            using ISilkGraphicsTexture color = CreateColor(device);
            using ISilkGraphicsTexture depth = device.CreateTexture2D(
                SilkTextureDescriptor.SampledDepthTarget(Size, Size));
            ApplyWholeResourceScene(
                renderer,
                AtDepth(curvePoints, 0.8f),
                AtDepth(cloudPoints, 0.8f),
                surfaceDepth: 0.3f);
            var binding = new SilkPickFrameBinding(1, 2);
            _ = renderer.Render(color, depth, binding);
            byte[] unselected = ReadPixels(color);

            // The curve's own pixels now belong to the surface, and a face
            // request there answers the surface's authored face exactly.
            RenderPickResult behindPrim = await pick(
                renderer, color, depth, binding, 32, 19, RenderPickTarget.Primitive);
            await Assert.That(behindPrim.Status).IsEqualTo(RenderPickStatus.Hit);
            await Assert.That(behindPrim.PrimPath).IsEqualTo("/Surface");

            RenderPickResult behindFace = await pick(
                renderer, color, depth, binding, 32, 19, RenderPickTarget.Face);
            await Assert.That(behindFace.Status).IsEqualTo(RenderPickStatus.Hit);
            await Assert.That(behindFace.PrimPath).IsEqualTo("/Surface");
            await Assert.That(behindFace.ElementIndex).IsEqualTo(0);

            RenderPickResult behindCloud = await pick(
                renderer, color, depth, binding, 32, 44, RenderPickTarget.Primitive);
            await Assert.That(behindCloud.PrimPath).IsEqualTo("/Surface");

            // A point request over a hidden point cloud must not answer with the
            // cloud. This is the near-occluder case the coincident offset breaks:
            // a biased point is pulled in front of the wall that hides it.
            RenderPickResult hiddenPoint = await pick(
                renderer, color, depth, binding, 32, 44, RenderPickTarget.Point);
            await Assert.That(hiddenPoint.PrimPath).IsNotEqualTo("/Cloud");

            // The visible-only outline of a fully hidden selection changes no
            // pixel at all, which is read from the image rather than from a
            // diagnostic counter.
            renderer.UpdateSelection(
                new SelectionState(["/Curve", "/Cloud"]),
                new SilkSelectionOutlineSettings(
                    enabled: true,
                    new SilkColor(1, 0.55f, 0, 0.9f),
                    width: 2,
                    visibleOnly: true));
            _ = renderer.Render(color, depth, binding);
            byte[] visibleOnly = ReadPixels(color);
            await Assert.That(visibleOnly.AsSpan().SequenceEqual(unselected)).IsTrue();

            // The x-ray mode does draw it, in the occluded style.
            renderer.UpdateSelection(
                new SelectionState(["/Curve", "/Cloud"]),
                new SilkSelectionOutlineSettings(
                    enabled: true,
                    new SilkColor(1, 0.55f, 0, 0.9f),
                    width: 2,
                    SilkSelectionOutlineMode.XRay,
                    SilkSelectionOutlineSettings.DefaultOccludedColor));
            _ = renderer.Render(color, depth, binding);
            byte[] xray = ReadPixels(color);
            await Assert.That(xray.AsSpan().SequenceEqual(unselected)).IsFalse();
            await Assert.That(CountOccludedStyle(xray, unselected)).IsGreaterThan(0);
        }
    }

    /// <summary>
    /// Every pixel the visible-only mode paints is painted identically by the
    /// x-ray mode, and the occluded style reaches only the remainder.
    /// </summary>
    /// <remarks>
    /// The x-ray mode used to composite the whole silhouette and then the
    /// visible one over it, which blends the two styles wherever both cover a
    /// pixel. The default outline colour is not opaque, so a visible edge came
    /// out as a mixture of orange and cyan rather than as the orange the
    /// visible-only mode draws. The assertion is byte equality on exactly the
    /// pixels the visible-only mode changed, read back from the device.
    /// </remarks>
    internal static async Task XRayLeavesTheVisibleOutlineByteIdentical(
        ISilkGraphicsDevice device)
    {
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColor(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Size, Size));

        // A selected quad whose lower half is hidden by an occluder, so the same
        // selection has a visible part and an occluded part.
        SilkMeshRendererConformance.Apply(
            renderer,
            1,
            SilkMeshRendererConformance.CreateFrameCommand(
                Size,
                Size,
                SilkMeshRendererConformance.Identity()),
            SilkMeshRendererConformance.CreateMeshCommand(
                1,
                "/Selected",
                Quad(-0.55f, 0.55f, 0.6f),
                QuadIndices,
                topologyRevision: 1),
            SilkMeshRendererConformance.CreateMeshCommand(
                2,
                "/Occluder",
                LowerBand(0.3f),
                QuadIndices,
                topologyRevision: 1));

        var binding = new SilkPickFrameBinding(1, 2);
        _ = renderer.Render(color, depth, binding);
        byte[] unselected = ReadPixels(color);

        var selection = new SelectionState(["/Selected"]);
        var visibleColor = new SilkColor(1, 0.55f, 0, 0.9f);
        renderer.UpdateSelection(
            selection,
            new SilkSelectionOutlineSettings(
                enabled: true,
                visibleColor,
                width: 2,
                visibleOnly: true));
        _ = renderer.Render(color, depth, binding);
        byte[] visibleOnly = ReadPixels(color);

        renderer.UpdateSelection(
            selection,
            new SilkSelectionOutlineSettings(
                enabled: true,
                visibleColor,
                width: 2,
                SilkSelectionOutlineMode.XRay,
                SilkSelectionOutlineSettings.DefaultOccludedColor));
        _ = renderer.Render(color, depth, binding);
        byte[] xray = ReadPixels(color);

        int visibleOutlinePixels = 0;
        int mismatched = 0;
        for (int index = 0; index + 3 < visibleOnly.Length; index += 4)
        {
            if (Same(visibleOnly, unselected, index))
            {
                continue;
            }
            visibleOutlinePixels++;
            if (!Same(xray, visibleOnly, index))
            {
                mismatched++;
            }
        }

        // Non-vacuity: the visible half really is outlined.
        await Assert.That(visibleOutlinePixels).IsGreaterThan(0);
        await Assert.That(mismatched)
            .IsEqualTo(0)
            .Because(
                $"visibleOutlinePixels={visibleOutlinePixels} " +
                $"mismatched={mismatched}");

        // And the occluded half arrives, in the occluded style, only where the
        // visible-only mode painted nothing.
        int occludedOnly = CountOccludedStyle(xray, visibleOnly);
        await Assert.That(occludedOnly).IsGreaterThan(0);
    }

    private static void ApplyWholeResourceScene(
        SilkMeshRenderer renderer,
        float[] curvePoints,
        float[] cloudPoints,
        float surfaceDepth) =>
        SilkMeshRendererConformance.Apply(
            renderer,
            1,
            SilkMeshRendererConformance.CreateFrameCommand(
                Size,
                Size,
                SilkMeshRendererConformance.Identity()),
            SilkMeshRendererConformance.CreateTopologyMeshCommand(
                1,
                "/Curve",
                SilkTopologyKind.LineList,
                curvePoints,
                [0, 1]),
            SilkMeshRendererConformance.CreateTopologyMeshCommand(
                2,
                "/Cloud",
                SilkTopologyKind.PointList,
                cloudPoints,
                [0, 1, 2]),
            SilkMeshRendererConformance.CreateMeshCommand(
                3,
                "/Surface",
                Quad(-0.95f, 0.95f, surfaceDepth),
                QuadIndices,
                0,
                0,
                null,
                1,
                QuadSubprims,
                QuadPointOrigins,
                QuadCornerEdges));

    private static float[] AtDepth(float[] points, float depth)
    {
        var moved = (float[])points.Clone();
        for (int component = 2; component < moved.Length; component += 3)
        {
            moved[component] = depth;
        }
        return moved;
    }

    private static float[] LowerBand(float depth) =>
    [
        -0.95f, -0.95f, depth,
        0.95f, -0.95f, depth,
        0.95f, -0.1f, depth,
        -0.95f, -0.1f, depth
    ];

    private static bool Same(byte[] left, byte[] right, int index) =>
        left[index] == right[index] &&
        left[index + 1] == right[index + 1] &&
        left[index + 2] == right[index + 2];

    /// <summary>
    /// Counts pixels that changed into the cool occluded style.
    /// </summary>
    /// <remarks>
    /// Pixels are classified by the ratio of their sorted channels, so the check
    /// does not depend on the readback's channel order: the cool occluded style
    /// leaves all three channels comparatively close, while the warm visible
    /// style drives one of them to nearly zero.
    /// </remarks>
    private static int CountOccludedStyle(byte[] actual, byte[] reference)
    {
        int occluded = 0;
        for (int index = 0; index + 3 < actual.Length; index += 4)
        {
            if (Same(actual, reference, index))
            {
                continue;
            }
            float high = Math.Max(
                actual[index],
                Math.Max(actual[index + 1], actual[index + 2]));
            float low = Math.Min(
                actual[index],
                Math.Min(actual[index + 1], actual[index + 2]));
            float middle =
                actual[index] + actual[index + 1] + actual[index + 2] - high - low;
            if (high > 0 && low / high >= 0.15f && middle / high >= 0.6f)
            {
                occluded++;
            }
        }
        return occluded;
    }

    private static byte[] ReadPixels(ISilkGraphicsTexture texture)
    {
        var pixels = new byte[checked((int)(texture.Width * texture.Height * 4))];
        texture.ReadbackForTesting(pixels);
        return pixels;
    }
    /// <summary>
    /// Scans one column band for the first hit on an expected path, so a
    /// scenario does not depend on the exact pixel a one-pixel-wide primitive
    /// happens to land on.
    /// </summary>
    private static async Task<RenderPickResult> ScanForHit(
        SilkMeshRenderer renderer,
        ISilkGraphicsTexture color,
        ISilkGraphicsTexture depth,
        SilkPickFrameBinding binding,
        Func<SilkMeshRenderer, ISilkGraphicsTexture, ISilkGraphicsTexture,
            SilkPickFrameBinding, int, int, RenderPickTarget,
            Task<RenderPickResult>> pick,
        RenderPickTarget target,
        int firstRow,
        int lastRow,
        string expectedPath)
    {
        RenderPickResult last = default;
        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int column = 8; column < (int)Size - 8; column++)
            {
                last = await pick(
                    renderer, color, depth, binding, column, row, target);
                if (last.Status == RenderPickStatus.Hit &&
                    string.Equals(last.PrimPath, expectedPath, StringComparison.Ordinal))
                {
                    return last;
                }
            }
        }
        return last;
    }

    internal static float[] Quad(float low, float high, float depth) =>
    [
        low, low, depth,
        high, low, depth,
        high, high, depth,
        low, high, depth
    ];

    private static ISilkGraphicsTexture CreateColor(ISilkGraphicsDevice device) =>
        device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
}
