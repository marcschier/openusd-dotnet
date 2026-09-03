// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

/// <summary>
/// Renders an authored UsdPreviewSurface <c>displacement</c> through the retained
/// renderer and requires the pixels it produces to equal the pixels of the same
/// surface authored already displaced.
/// </summary>
/// <remarks>
/// <para>
/// The gate is shaped so it can only pass if the geometry moved. Each case renders
/// three images: the displaced scene, a control scene whose points the case itself
/// moved by the same amounts along the same normals and whose material displaces
/// nothing, and the undisplaced scene. The first two must be identical and the
/// third must differ. A renderer that ignored the input would produce the third
/// image, and a renderer that shifted a colour rather than a position could not
/// reproduce the control's silhouette.
/// </para>
/// <para>
/// The normals are deliberately tilted out of the view axis. A quad facing the
/// camera displaced along its own normal under this frame's orthographic identity
/// projection would move only in depth, and every comparison would be vacuous;
/// tilted normals make displacement a silhouette change the rasterizer has to
/// resolve.
/// </para>
/// <para>
/// The same scenes measure what has to hold around the move: that the raster
/// shadow depth pass draws the displaced surface, that an input hdSilk cannot
/// represent exactly renders the undisplaced surface and says so, that a repeated
/// frame rebuilds nothing while a changed amount rebuilds once, and that a
/// displaced skinned prim draws the deformed surface with the amount applied on
/// top of it rather than a displaced bind pose.
/// </para>
/// </remarks>
internal static class SilkDisplacementRenderConformance
{
    private const int Size = 64;
    private const int MeshFixedSize = 268;
    private const float QuadDepth = 0.5f;
    private const float ReceiverDepth = 0.2f;
    private const float CasterDepth = 0.8f;
    private const float LightTiltX = 0.6f;
    private const float Amount = 0.45f;
    private const string MaterialPath = "/World/Materials/Displaced";
    private const string HeightAsset = "displacement-height.png";

    /// <summary>
    /// An authored constant moves the drawn surface by exactly that amount along
    /// each point's shading normal.
    /// </summary>
    internal static async Task AConstantDisplacementRendersTheDisplacedSurface(
        Func<ISilkGraphicsDevice> createDevice,
        SilkShaderBinaryFormat shaderFormat)
    {
        ArgumentNullException.ThrowIfNull(createDevice);
        byte[] displaced = Render(
            createDevice,
            shaderFormat,
            Material(constant: Amount),
            Quad("/Quad", QuadPoints(), QuadNormals()));
        byte[] control = Render(
            createDevice,
            shaderFormat,
            Material(constant: null),
            Quad("/Quad", Displace(QuadPoints(), QuadNormals(), Amount), QuadNormals()));
        byte[] undisplaced = Render(
            createDevice,
            shaderFormat,
            Material(constant: null),
            Quad("/Quad", QuadPoints(), QuadNormals()));

        await Assert.That(CountLit(control))
            .IsGreaterThan(100)
            .Because("the control surface must cover pixels for the comparisons below");
        await Assert.That(displaced.AsSpan().SequenceEqual(control))
            .IsTrue()
            .Because("a displaced surface must equal the same surface authored displaced");
        // Non-vacuity: a renderer that dropped the input draws this instead.
        await Assert.That(displaced.AsSpan().SequenceEqual(undisplaced))
            .IsFalse()
            .Because("the displaced image must differ from the undisplaced image");
    }

    /// <summary>
    /// An authored height field moves each point by its own sampled amount, which a
    /// single constant could not reproduce.
    /// </summary>
    /// <remarks>
    /// The control material carries the same image with a zero <c>scale</c>, so the
    /// decoded field is flat and nothing moves while the material still publishes
    /// the same texture coordinate set. Both scenes therefore emit the same vertex
    /// layout and select the same pipeline, and the only difference between the two
    /// images is where the geometry is.
    /// </remarks>
    internal static async Task ATextureDisplacementRendersThePerVertexDisplacedSurface(
        Func<ISilkGraphicsDevice> createDevice,
        SilkShaderBinaryFormat shaderFormat)
    {
        ArgumentNullException.ThrowIfNull(createDevice);
        float[] amounts = HeightAmounts();
        byte[] displaced = Render(
            createDevice,
            shaderFormat,
            Material(textureAsset: HeightAsset),
            Quad("/Quad", QuadPoints(), QuadNormals()));
        byte[] control = Render(
            createDevice,
            shaderFormat,
            Material(textureAsset: HeightAsset, textureScale: 0f),
            Quad("/Quad", Displace(QuadPoints(), QuadNormals(), amounts), QuadNormals()));
        byte[] undisplaced = Render(
            createDevice,
            shaderFormat,
            Material(textureAsset: HeightAsset, textureScale: 0f),
            Quad("/Quad", QuadPoints(), QuadNormals()));
        byte[] uniform = Render(
            createDevice,
            shaderFormat,
            Material(textureAsset: HeightAsset, textureScale: 0f),
            Quad(
                "/Quad",
                Displace(QuadPoints(), QuadNormals(), amounts[0]),
                QuadNormals()));

        await Assert.That(CountLit(control))
            .IsGreaterThan(100)
            .Because("the control surface must cover pixels for the comparisons below");
        await Assert.That(displaced.AsSpan().SequenceEqual(control))
            .IsTrue()
            .Because("a height field must move each point by its own sampled amount");
        // Non-vacuity: a renderer that dropped the image draws the undisplaced
        // surface, and one that folded the image to a single value draws the
        // uniform one. Neither is the picture the height field asks for.
        await Assert.That(displaced.AsSpan().SequenceEqual(undisplaced)).IsFalse();
        await Assert.That(displaced.AsSpan().SequenceEqual(uniform)).IsFalse();
    }

    /// <summary>
    /// The raster shadow depth pass draws the displaced caster, so the occluder in
    /// the map is the surface the colour pass drew.
    /// </summary>
    internal static async Task ShadowsFollowTheDisplacedSurface(
        Func<ISilkGraphicsDevice> createDevice,
        SilkShaderBinaryFormat shaderFormat)
    {
        ArgumentNullException.ThrowIfNull(createDevice);
        byte[] displaced = RenderShadowScene(
            createDevice,
            shaderFormat,
            Material(constant: Amount),
            CasterPoints());
        byte[] control = RenderShadowScene(
            createDevice,
            shaderFormat,
            Material(constant: null),
            Displace(CasterPoints(), QuadNormals(), Amount));
        byte[] undisplaced = RenderShadowScene(
            createDevice,
            shaderFormat,
            Material(constant: null),
            CasterPoints());

        await Assert.That(CountLit(control)).IsGreaterThan(100);
        await Assert.That(displaced.AsSpan().SequenceEqual(control))
            .IsTrue()
            .Because("the shadow pass must draw the same displaced vertices the colour pass drew");
        await Assert.That(displaced.AsSpan().SequenceEqual(undisplaced))
            .IsFalse()
            .Because("displacing the caster must change the shadowed image");
    }

    /// <summary>
    /// A displacement hdSilk cannot represent exactly renders the undisplaced
    /// surface and names the reason, rather than rendering a plausible-but-wrong
    /// picture in silence.
    /// </summary>
    internal static async Task AnUnsupportedDisplacementRendersTheUndisplacedSurface(
        Func<ISilkGraphicsDevice> createDevice,
        SilkShaderBinaryFormat shaderFormat)
    {
        ArgumentNullException.ThrowIfNull(createDevice);
        using ISilkGraphicsDevice device = createDevice();
        using ISilkGraphicsTexture color = CreateColor(device);
        using ISilkGraphicsTexture depth = CreateDepth(device);
        using var renderer = new SilkMeshRenderer(device, shaderFormat, HeightDecoder);
        ApplyScene(
            renderer,
            1,
            Frame(),
            Material(textureAsset: "displacement-height.<UDIM>.png"),
            Quad("/Quad", QuadPoints(), QuadNormals()));
        byte[] reported = RenderInto(renderer, color, depth);

        byte[] undisplaced = Render(
            createDevice,
            shaderFormat,
            Material(constant: null),
            Quad("/Quad", QuadPoints(), QuadNormals()));
        byte[] displaced = Render(
            createDevice,
            shaderFormat,
            Material(constant: Amount),
            Quad("/Quad", QuadPoints(), QuadNormals()));

        await Assert.That(reported.AsSpan().SequenceEqual(undisplaced))
            .IsTrue()
            .Because("an unrepresentable displacement must leave the surface where it was");
        // Non-vacuity: the same scene with a representable displacement does move,
        // so the equality above is the refusal and not a renderer that never moves.
        await Assert.That(displaced.AsSpan().SequenceEqual(undisplaced)).IsFalse();
        await Assert
            .That(renderer.GpuResources.Diagnostics.Entries.Any(
                diagnostic => diagnostic.Code ==
                    SilkRenderDiagnosticCodes.DisplacementUnsupported))
            .IsTrue()
            .Because("the refusal must be named rather than silent");
    }

    /// <summary>
    /// A repeated frame reuses the displaced geometry, and a changed amount rebuilds
    /// it exactly once and changes the picture.
    /// </summary>
    internal static async Task RepeatedFramesReuseTheDisplacedGeometry(
        Func<ISilkGraphicsDevice> createDevice,
        SilkShaderBinaryFormat shaderFormat)
    {
        ArgumentNullException.ThrowIfNull(createDevice);
        using ISilkGraphicsDevice device = createDevice();
        using ISilkGraphicsTexture color = CreateColor(device);
        using ISilkGraphicsTexture depth = CreateDepth(device);
        using var renderer = new SilkMeshRenderer(device, shaderFormat);

        ApplyScene(
            renderer,
            1,
            Frame(),
            Material(constant: Amount),
            Quad("/Quad", QuadPoints(), QuadNormals()));
        byte[] first = RenderInto(renderer, color, depth);
        ulong builds = renderer.GpuResources.Statistics.GeometryBuilds;
        await Assert.That(CountLit(first)).IsGreaterThan(100);

        // Republishing the identical material must not rebuild the displaced
        // vertices, which is what keeps a scrub through a displaced scene from
        // re-resolving the height field on every frame.
        ApplyScene(renderer, 2, Frame(), Material(constant: Amount));
        byte[] repeated = RenderInto(renderer, color, depth);
        await Assert.That(renderer.GpuResources.Statistics.GeometryBuilds).IsEqualTo(builds);
        await Assert.That(repeated.AsSpan().SequenceEqual(first)).IsTrue();

        // A changed amount is different geometry, so it rebuilds once and the
        // picture changes.
        ApplyScene(renderer, 3, Frame(), Material(constant: Amount * 0.5f));
        byte[] changed = RenderInto(renderer, color, depth);
        await Assert.That(renderer.GpuResources.Statistics.GeometryBuilds)
            .IsEqualTo(builds + 1);
        await Assert.That(changed.AsSpan().SequenceEqual(first)).IsFalse();
    }

    /// <summary>
    /// A displaced skinned prim draws the deformed surface with the amount applied
    /// on top of it, and never a displaced bind pose.
    /// </summary>
    /// <remarks>
    /// The record is shaped exactly as hdSilk publishes one: the CPU-resolved points
    /// and the rig that produced them. A displaced rig is refused by the ABI v20
    /// kernel with a named reason, because that kernel writes a skinned position and
    /// nothing else, so the authoritative CPU points are what the amounts move. The
    /// gate pins the ordering by comparing against a record carrying the deformed
    /// points already displaced, and against the bind pose displaced by the same
    /// amount, which is the picture a renderer that displaced before deforming would
    /// draw.
    /// </remarks>
    internal static async Task ADisplacedRigDrawsTheDeformedSurfaceDisplaced(
        Func<ISilkGraphicsDevice> createDevice,
        SilkShaderBinaryFormat shaderFormat)
    {
        ArgumentNullException.ThrowIfNull(createDevice);
        SilkMeshDeformationData rig = TranslationRig(0.9f);
        float[] deformed = EvaluatePoints(rig);
        float[] normals = EvaluateNormals(rig);

        byte[] displacedRig = Render(
            createDevice,
            shaderFormat,
            Material(constant: Amount),
            Quad("/Quad", deformed, normals, rig),
            out int rigDispatches,
            out ulong rigDisplacementFallbacks);
        byte[] control = Render(
            createDevice,
            shaderFormat,
            Material(constant: null),
            Quad("/Quad", Displace(deformed, normals, Amount), normals),
            out int controlDispatches,
            out _);
        byte[] displacedBindPose = Render(
            createDevice,
            shaderFormat,
            Material(constant: null),
            Quad("/Quad", Displace(QuadPoints(), QuadNormals(), Amount), QuadNormals()),
            out _,
            out _);
        byte[] deformedOnly = Render(
            createDevice,
            shaderFormat,
            Material(constant: null),
            Quad("/Quad", deformed, normals, rig),
            out int deformedDispatches,
            out ulong deformedDisplacementFallbacks);

        await Assert.That(rigDispatches)
            .IsEqualTo(0)
            .Because("a displaced rig is refused by the kernel and drawn from CPU points");
        // The refusal is a named one, and this is where that name is measured:
        // the kernel is declined *because* the material moves the surface, not
        // because the rig was ineligible for some other reason. The count is a
        // count of decisions rather than of builds -- one page that publishes a
        // mesh and its material makes the decision for both the mesh upsert and
        // the material change -- so the claim is that it was reached at all.
        await Assert.That(rigDisplacementFallbacks)
            .IsGreaterThanOrEqualTo(1UL)
            .Because("a moving displacement must reach the MaterialDisplacement fallback");
        await Assert.That(deformedDisplacementFallbacks)
            .IsEqualTo(0UL)
            .Because("an undisplaced rig must not take the displacement fallback");
        await Assert.That(controlDispatches).IsEqualTo(0);
        await Assert.That(CountLit(control)).IsGreaterThan(100);
        await Assert.That(displacedRig.AsSpan().SequenceEqual(control))
            .IsTrue()
            .Because("displacement must be applied to the deformed surface");
        // Non-vacuity, twice over: displacing before deforming draws a different
        // picture, and so does deforming without displacing.
        await Assert.That(displacedRig.AsSpan().SequenceEqual(displacedBindPose)).IsFalse();
        await Assert.That(displacedRig.AsSpan().SequenceEqual(deformedOnly)).IsFalse();
        // The undisplaced rig still reaches the kernel, so the refusal above is the
        // displacement and not a renderer that lost its GPU deformation path.
        await Assert.That(deformedDispatches)
            .IsEqualTo(1)
            .Because("an undisplaced rig must still be deformed by the checked kernel");
    }

    /// <summary>
    /// Repairing an unreadable height field while the displaced caster is both
    /// selected and shadowing reaches the next frame, and nothing the repair
    /// retired is still reachable from the resolved selection or the shadow atlas.
    /// </summary>
    /// <remarks>
    /// This is the case the scene-scoped retry's ordering exists for. The renderer
    /// holds a resolved selection keyed by the GPU resource revision and a shadow
    /// atlas keyed by the scene's geometry revision, and both name the retained
    /// mesh resources the retry replaces. Publishing the replacements and advancing
    /// both revisions before anything is disposed is what keeps a stale key from
    /// validating against a released resource; getting it wrong is a use after
    /// free, so the gate renders the frame rather than only inspecting counters.
    /// </remarks>
    internal static async Task RepairingAHeightFieldReachesSelectionAndShadows(
        Func<ISilkGraphicsDevice> createDevice,
        SilkShaderBinaryFormat shaderFormat)
    {
        ArgumentNullException.ThrowIfNull(createDevice);
        bool repaired = false;
        using ISilkGraphicsDevice device = createDevice();
        using ISilkGraphicsTexture color = CreateColor(device);
        using ISilkGraphicsTexture depth = CreateDepth(device);
        using var renderer = new SilkMeshRenderer(
            device,
            shaderFormat,
            (asset, srgb) => repaired
                ? HeightDecoder(asset, srgb)
                : throw new FileNotFoundException($"Texture '{asset}' is absent.", asset));
        ApplyScene(
            renderer,
            1,
            Frame(),
            Material(textureAsset: HeightAsset),
            Quad("/Caster", CasterPoints(), QuadNormals()),
            Receiver("/Floor"),
            Shadow());
        renderer.UpdateSelection(new SelectionState(["/Caster"]));
        byte[] broken = RenderInto(renderer, color, depth);

        // The file is repaired outside the scene: no page is republished, so only
        // the retry can pick it up, and it has to rebuild the retained geometry
        // both the selection and the shadow atlas already name.
        repaired = true;
        renderer.GpuResources.RetryFailedTextures(renderer.Scene);
        byte[] fixedUp = RenderInto(renderer, color, depth);

        // The outline is what makes the frames above exercise the resolved
        // selection, so the pixel comparison against the control is made after it
        // is cleared -- the control scene has no selection to draw.
        renderer.UpdateSelection(SelectionState.Empty);
        byte[] unselected = RenderInto(renderer, color, depth);
        byte[] control = RenderShadowScene(
            createDevice,
            shaderFormat,
            Material(constant: null),
            Displace(CasterPoints(), QuadNormals(), HeightAmounts()));

        await Assert.That(CountLit(fixedUp)).IsGreaterThan(100);
        await Assert.That(fixedUp.AsSpan().SequenceEqual(broken))
            .IsFalse()
            .Because("a repaired height field must reach the next frame");
        await Assert.That(unselected.AsSpan().SequenceEqual(control))
            .IsTrue()
            .Because("the repaired frame must draw the surface the height field asks for");
    }

    private static byte[] Render(
        Func<ISilkGraphicsDevice> createDevice,
        SilkShaderBinaryFormat shaderFormat,
        byte[] material,
        byte[] mesh) =>
        Render(createDevice, shaderFormat, material, mesh, out _, out _);

    private static byte[] Render(
        Func<ISilkGraphicsDevice> createDevice,
        SilkShaderBinaryFormat shaderFormat,
        byte[] material,
        byte[] mesh,
        out int dispatches,
        out ulong displacementFallbacks)
    {
        using ISilkGraphicsDevice device = createDevice();
        using ISilkGraphicsTexture color = CreateColor(device);
        using ISilkGraphicsTexture depth = CreateDepth(device);
        using var renderer = new SilkMeshRenderer(device, shaderFormat, HeightDecoder);
        ApplyScene(renderer, 1, Frame(), material, mesh);
        ulong before = renderer.GpuResources.DeformationDispatches;
        byte[] pixels = RenderInto(renderer, color, depth);
        dispatches = checked((int)(renderer.GpuResources.DeformationDispatches - before));
        displacementFallbacks = renderer.GpuResources.DeformationDisplacementFallbacks;
        return pixels;
    }

    private static byte[] RenderShadowScene(
        Func<ISilkGraphicsDevice> createDevice,
        SilkShaderBinaryFormat shaderFormat,
        byte[] material,
        float[] casterPoints)
    {
        using ISilkGraphicsDevice device = createDevice();
        using ISilkGraphicsTexture color = CreateColor(device);
        using ISilkGraphicsTexture depth = CreateDepth(device);
        using var renderer = new SilkMeshRenderer(device, shaderFormat, HeightDecoder);
        ApplyScene(
            renderer,
            1,
            Frame(),
            material,
            Quad("/Caster", casterPoints, QuadNormals()),
            Receiver("/Floor"),
            Shadow());
        return RenderInto(renderer, color, depth);
    }

    private static byte[] RenderInto(
        SilkMeshRenderer renderer,
        ISilkGraphicsTexture color,
        ISilkGraphicsTexture depth)
    {
        _ = renderer.Render(
            color,
            depth,
            new SilkMeshRenderOptions(new SilkColor(0, 0, 0, 1), 1));
        byte[] pixels = new byte[Size * Size * 4];
        color.ReadbackForTesting(pixels);
        return pixels;
    }

    private static void ApplyScene(
        SilkMeshRenderer renderer,
        ulong revision,
        params byte[][] commands)
    {
        int length = 0;
        foreach (byte[] command in commands)
        {
            length += command.Length;
        }
        byte[] page = new byte[length];
        int offset = 0;
        foreach (byte[] command in commands)
        {
            command.CopyTo(page, offset);
            offset += command.Length;
        }
        SilkSceneDelta delta = renderer.Scene.Apply(
            page,
            checked((uint)commands.Length),
            revision);
        renderer.GpuResources.Apply(renderer.Scene, delta);
    }

    private static int CountLit(byte[] pixels)
    {
        int lit = 0;
        for (int pixel = 0; pixel < pixels.Length; pixel += 4)
        {
            if (pixels[pixel] != 0 || pixels[pixel + 1] != 0 || pixels[pixel + 2] != 0)
            {
                lit++;
            }
        }
        return lit;
    }

    private static ISilkGraphicsTexture CreateColor(ISilkGraphicsDevice device) =>
        device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));

    private static ISilkGraphicsTexture CreateDepth(ISilkGraphicsDevice device) =>
        device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Size, Size));

    /// <summary>Moves every point by one shared amount along its own normal.</summary>
    private static float[] Displace(float[] points, float[] normals, float amount)
    {
        float[] amounts = new float[points.Length / 3];
        Array.Fill(amounts, amount);
        return Displace(points, normals, amounts);
    }

    /// <summary>Moves every point by its own amount along its own normal.</summary>
    private static float[] Displace(float[] points, float[] normals, float[] amounts)
    {
        float[] moved = new float[points.Length];
        for (int point = 0; point < amounts.Length; point++)
        {
            int offset = point * 3;
            double x = normals[offset];
            double y = normals[offset + 1];
            double z = normals[offset + 2];
            double inverseLength = 1.0 / Math.Sqrt((x * x) + (y * y) + (z * z));
            moved[offset] = points[offset] + (amounts[point] * (float)(x * inverseLength));
            moved[offset + 1] =
                points[offset + 1] + (amounts[point] * (float)(y * inverseLength));
            moved[offset + 2] =
                points[offset + 2] + (amounts[point] * (float)(z * inverseLength));
        }
        return moved;
    }

    private static float[] QuadPoints() =>
    [
        -0.3f, -0.3f, QuadDepth,
        0.3f, -0.3f, QuadDepth,
        0.3f, 0.3f, QuadDepth,
        -0.3f, 0.3f, QuadDepth
    ];

    private static float[] CasterPoints() =>
    [
        -0.25f, -0.25f, CasterDepth,
        0.25f, -0.25f, CasterDepth,
        0.25f, 0.25f, CasterDepth,
        -0.25f, 0.25f, CasterDepth
    ];

    /// <summary>
    /// Outward-tilted normals. The view is the identity, so a normal along negative
    /// z faces the camera; the lateral components are what turn a displacement into
    /// a silhouette change instead of a depth-only one.
    /// </summary>
    private static float[] QuadNormals() =>
    [
        -0.5f, -0.5f, -0.7071068f,
        0.5f, -0.5f, -0.7071068f,
        0.5f, 0.5f, -0.7071068f,
        -0.5f, 0.5f, -0.7071068f
    ];

    private static float[] FlatNormals() =>
    [
        0, 0, -1,
        0, 0, -1,
        0, 0, -1,
        0, 0, -1
    ];

    /// <summary>
    /// The four amounts <see cref="HeightDecoder"/>'s field resolves at the quad's
    /// authored coordinates, which are the four texel centres.
    /// </summary>
    /// <remarks>
    /// The authored <c>scale</c> is applied in float, after the eight-bit texel is
    /// converted to a unit value, and is never requantized: a height is not a
    /// colour, and the renderer's own affine is exact. The oracle is therefore the
    /// plain product, computed here independently of the renderer.
    /// </remarks>
    private static float[] HeightAmounts() =>
    [
        (128f / 255f) * Amount,
        (255f / 255f) * Amount,
        (64f / 255f) * Amount,
        0f
    ];

    /// <summary>
    /// A two-by-two height field whose four texels differ, so a per-vertex sample
    /// and a single folded constant cannot produce the same geometry.
    /// </summary>
    /// <remarks>
    /// The rows are emitted in decode order; the shared decode path flips them, so
    /// the field the renderer samples has 128 and 255 in its first row and 0 and 64
    /// in its second. The quad's authored coordinates address the four texel centres
    /// in the corner order the points are emitted in.
    /// </remarks>
    private static SilkDecodedImage HeightDecoder(string asset, bool srgb)
    {
        if (!string.Equals(asset, HeightAsset, StringComparison.Ordinal))
        {
            throw new FileNotFoundException($"Texture '{asset}' is absent.", asset);
        }
        byte[] rows = [0, 64, 128, 255];
        byte[] pixels = new byte[16];
        for (int texel = 0; texel < 4; texel++)
        {
            pixels[texel * 4] = rows[texel];
            pixels[(texel * 4) + 1] = rows[texel];
            pixels[(texel * 4) + 2] = rows[texel];
            pixels[(texel * 4) + 3] = 255;
        }
        return new SilkDecodedImage(2, 2, pixels);
    }

    /// <summary>
    /// The four texel centres of the two-by-two field, in the corner order the quad
    /// emits its points, so each corner resolves a different amount.
    /// </summary>
    private static float[] TexCoords() =>
    [
        0.25f, 0.25f,
        0.75f, 0.25f,
        0.75f, 0.75f,
        0.25f, 0.75f
    ];

    private static float[] EvaluatePoints(SilkMeshDeformationData rig)
    {
        float[] points = new float[rig.BindPointCount * 3];
        SilkDeformationEvaluator.EvaluatePoints(rig, points);
        return points;
    }

    private static float[] EvaluateNormals(SilkMeshDeformationData rig)
    {
        float[] normals = new float[rig.BindPointCount * 3];
        _ = SilkDeformationEvaluator.TryEvaluateNormals(rig, normals);
        return normals;
    }

    /// <summary>
    /// A single-joint rig that slides the quad along x and y, so the deformed
    /// surface is unmistakably a different place from the bind pose.
    /// </summary>
    private static SilkMeshDeformationData TranslationRig(float pose) =>
        SilkDeformationRigBuilder.Build(
            bindPoints: QuadPoints(),
            bindNormals: QuadNormals(),
            influencesPerPoint: 1,
            jointIndices: [0, 0, 0, 0],
            jointWeights: [1, 1, 1, 1],
            jointMatrices:
            [
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                pose * 0.4f, pose * 0.3f, 0, 1
            ],
            geomBindTransform:
            [
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                0, 0, 0, 1
            ]);

    private static byte[] Receiver(string path) =>
        Quad(
            path,
            [
                -1f, -1f, ReceiverDepth,
                1f, -1f, ReceiverDepth,
                1f, 1f, ReceiverDepth,
                -1f, 1f, ReceiverDepth
            ],
            FlatNormals(),
            deformation: null,
            primId: 2,
            materialPath: string.Empty);

    private static byte[] Frame()
    {
        const int lightingSize = 1976;
        const int lightCountOffset = 536;
        const int lightTableOffset = 552;
        byte[] bytes = new byte[lightingSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), Size);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), Size);
        double[] identity = SilkMeshRendererConformance.Identity();
        for (int element = 0; element < 16; element++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(16 + (element * 8)),
                identity[element]);
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(144 + (element * 8)),
                identity[element]);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(lightCountOffset), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(lightTableOffset), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(lightTableOffset + 4), 1u);
        for (int component = 0; component < 3; component++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(lightTableOffset + 16 + (component * 4)),
                1f);
        }
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(lightTableOffset + 28), 1f);
        double[] transform = SilkMeshRendererConformance.Identity();
        Vector3 direction = LightDirection();
        transform[8] = direction.X;
        transform[9] = direction.Y;
        transform[10] = direction.Z;
        for (int element = 0; element < 16; element++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(lightTableOffset + 32 + (element * 8)),
                transform[element]);
        }
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(lightTableOffset + 164), 1f);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(lightTableOffset + 168), 1f);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(lightTableOffset + 172), 0.5f);
        return bytes;
    }

    private static Vector3 LightDirection() =>
        Vector3.Normalize(new Vector3(LightTiltX, 0, 1));

    private static byte[] Shadow()
    {
        const int descriptorSize = 288;
        byte[] bytes = new byte[24 + descriptorSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Shadow);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 1u);

        var center = new Vector3(0, 0, (ReceiverDepth + CasterDepth) * 0.5f);
        var half = new Vector3(1.5f, 1.5f, (CasterDepth - ReceiverDepth) * 0.5f);
        double radius = Math.Sqrt(
            (half.X * half.X) + (half.Y * half.Y) + (half.Z * half.Z));
        BuildLightSpace(center, radius, out double[] view, out double[] projection);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32), 1024u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(36), 1u);
        for (int element = 0; element < 16; element++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(40 + (element * 8)),
                view[element]);
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(168 + (element * 8)),
                projection[element]);
        }
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(296), 1.5f / 1024f);
        BinaryPrimitives.WriteSingleLittleEndian(
            bytes.AsSpan(300),
            (float)(1.5 * 2.0 * radius / 1024.0));
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(304), 1f);
        return bytes;
    }

    private static void BuildLightSpace(
        Vector3 center,
        double radius,
        out double[] view,
        out double[] projection)
    {
        Vector3 zAxis = LightDirection();
        Vector3 xAxis = Vector3.Normalize(Vector3.Cross(new Vector3(0, 1, 0), zAxis));
        Vector3 yAxis = Vector3.Normalize(Vector3.Cross(zAxis, xAxis));
        double eyeDistance = (2.0 * radius) + 1.0;
        Vector3 eye = center + (zAxis * (float)eyeDistance);
        view =
        [
            xAxis.X, yAxis.X, zAxis.X, 0,
            xAxis.Y, yAxis.Y, zAxis.Y, 0,
            xAxis.Z, yAxis.Z, zAxis.Z, 0,
            -Vector3.Dot(eye, xAxis), -Vector3.Dot(eye, yAxis), -Vector3.Dot(eye, zAxis), 1,
        ];
        double near = eyeDistance - radius;
        double far = eyeDistance + radius;
        projection = new double[16];
        projection[0] = 1.0 / radius;
        projection[5] = 1.0 / radius;
        projection[10] = -2.0 / (far - near);
        projection[14] = -(far + near) / (far - near);
        projection[15] = 1.0;
    }

    /// <summary>
    /// Encodes one MATERIAL_UPSERT carrying an authored displacement, as a constant,
    /// as a connected height field, or as neither.
    /// </summary>
    private static byte[] Material(
        float? constant = null,
        string? textureAsset = null,
        float textureScale = 1f)
    {
        byte[] path = Encoding.UTF8.GetBytes(MaterialPath);
        List<byte> payload = [];
        int scalars = constant is null ? 0 : 1;
        int textures = textureAsset is null ? 0 : 1;
        payload.AddRange(BitConverter.GetBytes(SilkWireFormat.ComputeStableHash(MaterialPath)));
        payload.AddRange(BitConverter.GetBytes((uint)path.Length));
        payload.AddRange(BitConverter.GetBytes((uint)SilkSurfaceKind.PreviewSurface));
        payload.AddRange(BitConverter.GetBytes((uint)scalars));
        payload.AddRange(BitConverter.GetBytes((uint)textures));
        payload.AddRange(path);
        if (constant is { } amount)
        {
            payload.AddRange(BitConverter.GetBytes((uint)SilkMaterialParameter.Displacement));
            payload.AddRange(BitConverter.GetBytes(1u));
            payload.AddRange(BitConverter.GetBytes(amount));
        }
        if (textureAsset is not null)
        {
            byte[] asset = Encoding.UTF8.GetBytes(textureAsset);
            byte[] uv = Encoding.UTF8.GetBytes("st");
            payload.AddRange(BitConverter.GetBytes((uint)SilkMaterialParameter.Displacement));
            payload.AddRange(BitConverter.GetBytes((uint)SilkTextureWrap.Clamp));
            payload.AddRange(BitConverter.GetBytes((uint)SilkTextureWrap.Clamp));
            payload.AddRange(BitConverter.GetBytes((uint)SilkColorSpace.Raw));
            payload.AddRange(BitConverter.GetBytes((uint)asset.Length));
            payload.AddRange(BitConverter.GetBytes((uint)uv.Length));
            payload.AddRange(BitConverter.GetBytes(1u));
            for (int component = 0; component < 4; component++)
            {
                payload.AddRange(BitConverter.GetBytes(textureScale * Amount));
            }
            for (int component = 0; component < 4; component++)
            {
                payload.AddRange(BitConverter.GetBytes(0f));
            }
            for (int component = 0; component < 4; component++)
            {
                payload.AddRange(BitConverter.GetBytes(component == 3 ? 1f : 0f));
            }
            payload.AddRange(BitConverter.GetBytes((uint)SilkTextureChannel.R));
            payload.AddRange(BitConverter.GetBytes((uint)SilkCompositeOperator.None));
            payload.AddRange(BitConverter.GetBytes(0f));
            payload.AddRange(asset);
            payload.AddRange(uv);
        }
        payload.AddRange(BitConverter.GetBytes(0u));
        payload.AddRange(BitConverter.GetBytes(0u));
        foreach (float element in (float[])[1, 0, 0, 1, 0, 0])
        {
            payload.AddRange(BitConverter.GetBytes(element));
        }
        byte[] bytes = new byte[8 + payload.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MaterialUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        payload.CopyTo(bytes, 8);
        return bytes;
    }

    private static byte[] Quad(
        string pathValue,
        float[] points,
        float[] normals,
        SilkMeshDeformationData? deformation = null,
        int primId = 1,
        string materialPath = MaterialPath)
    {
        byte[] path = Encoding.UTF8.GetBytes(pathValue);
        byte[] material = Encoding.UTF8.GetBytes(materialPath);
        uint[] indices = [0, 2, 1, 0, 3, 2];
        int pointCount = points.Length / 3;
        byte[] block = deformation is null ? [] : EncodeBlock(deformation);
        byte[] normalName = Encoding.UTF8.GetBytes("normals");
        byte[] uvName = Encoding.UTF8.GetBytes("st");
        float[] texCoords = TexCoords();
        int attributeBytes =
            20 + normalName.Length + (normals.Length * sizeof(float)) +
            20 + uvName.Length + (texCoords.Length * sizeof(float));
        int size = MeshFixedSize +
            path.Length +
            (points.Length * sizeof(float)) +
            (indices.Length * sizeof(uint)) +
            (2 * sizeof(uint)) +
            material.Length +
            attributeBytes +
            block.Length;
        byte[] bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkWireFormat.ComputeStableHash(pathValue));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), primId);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(28),
            (uint)SilkTopologyKind.TriangleList);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(44),
            (uint)SilkMeshCullStyle.Nothing);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), (uint)path.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52), (uint)pointCount);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), (uint)indices.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), 2);
        for (int component = 0; component < 4; component++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(64 + (component * sizeof(float))),
                component == 3 ? 1.0f : 0.8f);
        }
        double[] identity = SilkMeshRendererConformance.Identity();
        for (int element = 0; element < 16; element++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(80 + (element * sizeof(double))),
                identity[element]);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(216), (uint)material.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(220), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(224),
            deformation is null ? 0u : (uint)deformation.Options);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(228), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(232), (uint)block.Length);

        path.CopyTo(bytes, MeshFixedSize);
        int cursor = MeshFixedSize + path.Length;
        foreach (float value in points)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(cursor), value);
            cursor += sizeof(float);
        }
        foreach (uint index in indices)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), index);
            cursor += sizeof(uint);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor + 4), 0);
        cursor += 2 * sizeof(uint);
        material.CopyTo(bytes, cursor);
        cursor += material.Length;
        cursor = WriteAttribute(
            bytes,
            cursor,
            SilkAttributeSemantic.Normal,
            3,
            normalName,
            pointCount,
            normals);
        cursor = WriteAttribute(
            bytes,
            cursor,
            SilkAttributeSemantic.TexCoord,
            2,
            uvName,
            pointCount,
            texCoords);
        block.CopyTo(bytes, cursor);
        return bytes;
    }

    private static int WriteAttribute(
        byte[] bytes,
        int cursor,
        SilkAttributeSemantic semantic,
        int components,
        byte[] name,
        int pointCount,
        float[] data)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), (uint)semantic);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor + 4), (uint)components);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(cursor + 8),
            (uint)SilkAttributeInterpolation.Vertex);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor + 12), (uint)name.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor + 16), (uint)pointCount);
        name.CopyTo(bytes, cursor + 20);
        cursor += 20 + name.Length;
        foreach (float value in data)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(cursor), value);
            cursor += sizeof(float);
        }
        return cursor;
    }

    private static byte[] EncodeBlock(SilkMeshDeformationData deformation)
    {
        int points = deformation.BindPointCount;
        int influences = deformation.InfluencesPerPoint;
        int joints = deformation.JointCount;
        bool bindNormals =
            (deformation.Options & SilkDeformationOptions.BindNormals) != 0;
        int size = 96 +
            (points * 3 * sizeof(float)) +
            (bindNormals ? points * 3 * sizeof(float) : 0) +
            (points * influences * sizeof(uint)) +
            (points * influences * sizeof(float)) +
            (joints * 16 * sizeof(float));
        byte[] block = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(block, (uint)joints);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(4), (uint)influences);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(8), (uint)points);
        int cursor = 32;
        cursor = WriteFloats(block, cursor, deformation.GeomBindTransform.Span);
        cursor = WriteFloats(block, cursor, deformation.BindPoints.Span);
        if (bindNormals)
        {
            cursor = WriteFloats(block, cursor, deformation.BindNormals.Span);
        }
        foreach (uint joint in deformation.JointIndices.Span)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(cursor), joint);
            cursor += sizeof(uint);
        }
        cursor = WriteFloats(block, cursor, deformation.JointWeights.Span);
        cursor = WriteFloats(block, cursor, deformation.JointMatrices.Span);
        ulong identity = 14695981039346656037UL;
        for (int offset = 32; offset < cursor; offset++)
        {
            identity ^= block[offset];
            identity *= 1099511628211UL;
        }
        BinaryPrimitives.WriteUInt64LittleEndian(block.AsSpan(24), identity);
        return block;
    }

    private static int WriteFloats(byte[] block, int cursor, ReadOnlySpan<float> values)
    {
        foreach (float value in values)
        {
            BinaryPrimitives.WriteSingleLittleEndian(block.AsSpan(cursor), value);
            cursor += sizeof(float);
        }
        return cursor;
    }
}
