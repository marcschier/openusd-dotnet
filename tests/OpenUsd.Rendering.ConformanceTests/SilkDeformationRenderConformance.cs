// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

/// <summary>
/// Renders a deformed prim through the retained renderer and requires the
/// pixels the GPU deformation pass produces to equal the pixels the
/// authoritative CPU geometry produces.
/// </summary>
/// <remarks>
/// <para>
/// The gate is deliberately instrumented so it can only pass if the kernel ran.
/// The GPU record publishes its <em>bind-pose</em> points in the record's point
/// array while carrying the rig that moves them; the CPU reference record
/// publishes the resolved points and no rig. A GPU-deformed geometry never
/// uploads a record's points -- its vertex buffer lives on the device heap and
/// is written only by the kernel -- so the two images can agree only when the
/// kernel produced the deformed surface. If the pass silently fell back, the
/// GPU image would be the bind pose, which the third image in each case shows
/// is a different picture.
/// </para>
/// <para>
/// Everything the renderer must get right around the dispatch is measured from
/// the same scenes: that a repeated frame dispatches nothing, that a scrubbed
/// pose dispatches exactly once, that a shadow map rendered before the colour
/// pass sees the deformed surface, that an ineligible rig still draws the
/// authoritative CPU geometry, and that a device generation reset re-dispatches
/// once and reproduces the same pixels.
/// </para>
/// </remarks>
public static class SilkDeformationRenderConformance
{
    private const int Size = 64;
    private const int MeshFixedSize = 268;
    private const float QuadDepth = 0.5f;
    private const float ReceiverDepth = 0.2f;
    private const float CasterDepth = 0.8f;
    private const float LightTiltX = 0.6f;

    /// <summary>
    /// Renders three poses and requires the GPU image to equal the CPU image at
    /// each, and to differ from the bind pose.
    /// </summary>
    internal static async Task GpuDeformedImageMatchesTheCpuResolvedImage(
        Func<ISilkGraphicsDevice> createDevice,
        SilkShaderBinaryFormat shaderFormat)
    {
        ArgumentNullException.ThrowIfNull(createDevice);
        foreach (float pose in (float[])[0.35f, 0.75f, 1.25f])
        {
            SilkMeshDeformationData rig = TranslationRig(pose);
            float[] deformed = EvaluatePoints(rig);
            float[] normals = EvaluateNormals(rig);

            byte[] gpu = Render(
                createDevice,
                shaderFormat,
                CreateQuad("/Quad", BindPoints(), BindNormals(), rig),
                out int gpuDispatches);
            byte[] cpu = Render(
                createDevice,
                shaderFormat,
                CreateQuad("/Quad", deformed, normals, deformation: null),
                out int cpuDispatches);
            byte[] bind = Render(
                createDevice,
                shaderFormat,
                CreateQuad("/Quad", BindPoints(), BindNormals(), deformation: null),
                out _);

            await Assert.That(gpuDispatches)
                .IsEqualTo(1)
                .Because($"pose {pose} must dispatch the deformation kernel once");
            await Assert.That(cpuDispatches)
                .IsEqualTo(0)
                .Because("a record without a rig must dispatch nothing");
            // The scene has to actually draw something, or every comparison
            // below would be a comparison of two cleared images.
            await Assert.That(CountLit(cpu))
                .IsGreaterThan(100)
                .Because($"the CPU-resolved quad at pose {pose} must cover pixels");
            await Assert.That(CountLit(gpu))
                .IsGreaterThan(100)
                .Because($"the GPU-deformed quad at pose {pose} must cover pixels");
            await Assert.That(gpu.AsSpan().SequenceEqual(cpu))
                .IsTrue()
                .Because(
                    $"the GPU-deformed image at pose {pose} must equal the " +
                    "CPU-resolved image");
            // Non-vacuity: a silent fallback would draw this instead, so the
            // agreement above is only meaningful because the bind pose differs.
            await Assert.That(gpu.AsSpan().SequenceEqual(bind))
                .IsFalse()
                .Because(
                    $"the deformed image at pose {pose} must differ from the bind pose");
        }
    }

    /// <summary>
    /// Requires a repeated frame to dispatch nothing and a changed pose to
    /// dispatch exactly once, with identical pixels either way.
    /// </summary>
    internal static async Task RepeatedFramesReuseAndChangedPosesDispatchOnce(
        Func<ISilkGraphicsDevice> createDevice,
        SilkShaderBinaryFormat shaderFormat)
    {
        ArgumentNullException.ThrowIfNull(createDevice);
        using ISilkGraphicsDevice device = createDevice();
        using ISilkGraphicsTexture color = CreateColor(device);
        using ISilkGraphicsTexture depth = CreateDepth(device);
        using var renderer = new SilkMeshRenderer(device, shaderFormat);

        SilkMeshDeformationData first = TranslationRig(0.5f);
        Apply(renderer, 1, CreateQuad("/Quad", BindPoints(), BindNormals(), first));
        byte[] firstImage = RenderInto(renderer, color, depth);
        ulong afterFirst = renderer.GpuResources.DeformationDispatches;
        await Assert.That(CountLit(firstImage))
            .IsGreaterThan(100)
            .Because("the deformed quad must cover pixels for the comparisons below");

        // A frame that changed nothing must not touch the vertex buffer again.
        byte[] repeated = RenderInto(renderer, color, depth);
        await Assert.That(renderer.GpuResources.DeformationDispatches).IsEqualTo(afterFirst);
        await Assert.That(repeated.AsSpan().SequenceEqual(firstImage)).IsTrue();

        // Republishing the identical rig is what a material or transform edit
        // does to a skinned prim, and it must not dispatch either.
        Apply(renderer, 2, CreateQuad("/Quad", BindPoints(), BindNormals(), TranslationRig(0.5f)));
        byte[] republished = RenderInto(renderer, color, depth);
        await Assert.That(renderer.GpuResources.DeformationDispatches).IsEqualTo(afterFirst);
        await Assert.That(republished.AsSpan().SequenceEqual(firstImage)).IsTrue();

        // A scrubbed pose dispatches exactly once and changes the picture.
        Apply(renderer, 3, CreateQuad("/Quad", BindPoints(), BindNormals(), TranslationRig(1.1f)));
        byte[] moved = RenderInto(renderer, color, depth);
        await Assert.That(renderer.GpuResources.DeformationDispatches)
            .IsEqualTo(afterFirst + 1);
        await Assert.That(moved.AsSpan().SequenceEqual(firstImage)).IsFalse();

        // And the resource is reused rather than rebuilt: one geometry build
        // covers every pose of one rig.
        await Assert.That(renderer.GpuResources.Statistics.GeometryBuilds).IsEqualTo(1UL);
    }

    /// <summary>
    /// Requires a device generation reset to re-dispatch once and reproduce the
    /// same pixels.
    /// </summary>
    internal static async Task ADeviceGenerationResetRedispatchesOnce(
        Func<ISilkGraphicsDevice> createDevice,
        SilkShaderBinaryFormat shaderFormat,
        Action<ISilkGraphicsDevice> invalidateGeneration)
    {
        ArgumentNullException.ThrowIfNull(createDevice);
        ArgumentNullException.ThrowIfNull(invalidateGeneration);
        using ISilkGraphicsDevice device = createDevice();
        using ISilkGraphicsTexture color = CreateColor(device);
        using ISilkGraphicsTexture depth = CreateDepth(device);
        using var renderer = new SilkMeshRenderer(device, shaderFormat);

        Apply(renderer, 1, CreateQuad("/Quad", BindPoints(), BindNormals(), TranslationRig(0.8f)));
        byte[] before = RenderInto(renderer, color, depth);
        ulong dispatches = renderer.GpuResources.DeformationDispatches;
        await Assert.That(CountLit(before))
            .IsGreaterThan(100)
            .Because("the deformed quad must cover pixels for the comparisons below");
        await Assert.That(dispatches).IsEqualTo(1UL);

        // Nothing the host uploaded is invalidated, but what the device wrote
        // can no longer be assumed, so the next frame dispatches once more.
        invalidateGeneration(device);
        byte[] after = RenderInto(renderer, color, depth);
        await Assert.That(renderer.GpuResources.DeformationDispatches)
            .IsEqualTo(dispatches + 1);
        await Assert.That(after.AsSpan().SequenceEqual(before)).IsTrue();

        // And it settles: the frame after the reset dispatches nothing again.
        byte[] settled = RenderInto(renderer, color, depth);
        await Assert.That(renderer.GpuResources.DeformationDispatches)
            .IsEqualTo(dispatches + 1);
        await Assert.That(settled.AsSpan().SequenceEqual(before)).IsTrue();
    }

    /// <summary>
    /// Requires an eligible rig whose GPU setup fails to draw the authoritative
    /// CPU geometry instead of throwing or drawing a bind pose.
    /// </summary>
    /// <remarks>
    /// The record is shaped exactly as hdSilk publishes one: the CPU-resolved
    /// points *and* the rig that produced them. The kernel and the fallback are
    /// therefore both supposed to produce the same picture, which is what makes
    /// "did the fallback draw the right thing" answerable at all -- and the bind
    /// pose is a third, different picture, so a fallback that drew it would be
    /// caught rather than mistaken for success.
    /// </remarks>
    internal static async Task AFailedDeformationSetupDrawsTheCpuGeometry(
        Func<ISilkGraphicsDevice> createDevice,
        SilkShaderBinaryFormat shaderFormat)
    {
        ArgumentNullException.ThrowIfNull(createDevice);
        SilkMeshDeformationData rig = TranslationRig(0.9f);
        float[] deformed = EvaluatePoints(rig);
        float[] normals = EvaluateNormals(rig);

        using ISilkGraphicsDevice device = createDevice();
        using ISilkGraphicsTexture color = CreateColor(device);
        using ISilkGraphicsTexture depth = CreateDepth(device);
        using var renderer = new SilkMeshRenderer(device, shaderFormat);

        // The rig is fully eligible: nothing about it is refused by policy, so
        // the only reason it can end up on the CPU is the injected failure.
        renderer.GpuResources.InjectDeformationFailuresForTesting(
            () => new InvalidOperationException("Injected deformation setup failure."),
            onDispatch: null);
        Apply(renderer, 1, CreateQuad("/Quad", deformed, normals, rig));
        byte[] image = RenderInto(renderer, color, depth);

        await Assert.That(renderer.GpuResources.DeformationFallbacks)
            .IsEqualTo(1UL)
            .Because("the refused setup must be recorded as a fallback");
        await Assert.That(renderer.GpuResources.DeformationDispatches)
            .IsEqualTo(0UL)
            .Because("a geometry that fell back has no kernel to dispatch");
        await AssertMatchesCpuGeometry(createDevice, shaderFormat, deformed, normals, image);

        // A second prim published after the failure keeps drawing the CPU
        // geometry rather than rediscovering the same failure per prim.
        Apply(renderer, 2, CreateQuad("/Quad", deformed, normals, TranslationRig(1.2f)));
        _ = RenderInto(renderer, color, depth);
        await Assert.That(renderer.GpuResources.DeformationDispatches).IsEqualTo(0UL);
        await Assert.That(renderer.GpuResources.DeformationFallbacks).IsEqualTo(1UL);
    }

    /// <summary>
    /// Requires an eligible rig whose dispatch fails to fall back onto the
    /// authoritative CPU geometry within the same frame.
    /// </summary>
    internal static async Task AFailedDeformationDispatchDrawsTheCpuGeometry(
        Func<ISilkGraphicsDevice> createDevice,
        SilkShaderBinaryFormat shaderFormat)
    {
        ArgumentNullException.ThrowIfNull(createDevice);
        SilkMeshDeformationData rig = TranslationRig(0.9f);
        float[] deformed = EvaluatePoints(rig);
        float[] normals = EvaluateNormals(rig);

        using ISilkGraphicsDevice device = createDevice();
        using ISilkGraphicsTexture color = CreateColor(device);
        using ISilkGraphicsTexture depth = CreateDepth(device);
        using var renderer = new SilkMeshRenderer(device, shaderFormat);

        // The setup succeeds, so the vertex buffer really is the device-heap one
        // the kernel owns and has never been written by the host. Failing the
        // dispatch is therefore the case where a frame would otherwise draw
        // whatever that buffer happens to hold.
        Apply(renderer, 1, CreateQuad("/Quad", deformed, normals, rig));
        renderer.GpuResources.InjectDeformationFailuresForTesting(
            onSetup: null,
            () => new InvalidOperationException("Injected deformation dispatch failure."));
        byte[] image = RenderInto(renderer, color, depth);

        await Assert.That(renderer.GpuResources.DeformationFallbacks)
            .IsEqualTo(1UL)
            .Because("the refused dispatch must be recorded as a fallback");
        await Assert.That(renderer.GpuResources.DeformationDispatches)
            .IsEqualTo(0UL)
            .Because("a dispatch that never reached the device is not a dispatch");
        await AssertMatchesCpuGeometry(createDevice, shaderFormat, deformed, normals, image);

        // The frame after it is steady: nothing pending, nothing dispatched, and
        // the same picture.
        byte[] settled = RenderInto(renderer, color, depth);
        await Assert.That(renderer.GpuResources.DeformationDispatches).IsEqualTo(0UL);
        await Assert.That(settled.AsSpan().SequenceEqual(image)).IsTrue();
    }

    /// <summary>
    /// Requires a device loss detected by the production submission path to
    /// propagate rather than be absorbed as a fallback.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two failures are indistinguishable by exception type -- both backends
    /// report a lost device as the same exception they report a refused
    /// allocation with -- so what separates them is the generation the backend
    /// advances when it notices the loss. Absorbing one would leave the renderer
    /// drawing through resources the reset path was never told to rebuild.
    /// </para>
    /// <para>
    /// The loss is armed on the device's ordinary queue submission rather than
    /// signalled by hand, because that is the path the deformation dispatch
    /// actually uses and the one the defect lived on: the picking and
    /// selection-outline generations do not advance for a loss detected there,
    /// so a classifier reading either of them would have called a real device
    /// loss a recoverable allocation failure. Only the driver's result is
    /// substituted; the backend still runs its own detection, recording and
    /// reporting.
    /// </para>
    /// </remarks>
    internal static async Task ADeviceLossDuringDispatchPropagates(
        Func<ISilkGraphicsDevice> createDevice,
        SilkShaderBinaryFormat shaderFormat,
        Action<ISilkGraphicsDevice> armSubmissionDeviceLoss,
        bool selectionGenerationTracksSubmissionLoss)
    {
        ArgumentNullException.ThrowIfNull(createDevice);
        ArgumentNullException.ThrowIfNull(armSubmissionDeviceLoss);
        SilkMeshDeformationData rig = TranslationRig(0.9f);
        float[] deformed = EvaluatePoints(rig);
        float[] normals = EvaluateNormals(rig);

        using ISilkGraphicsDevice device = createDevice();
        using ISilkGraphicsTexture color = CreateColor(device);
        using ISilkGraphicsTexture depth = CreateDepth(device);
        using var renderer = new SilkMeshRenderer(device, shaderFormat);

        Apply(renderer, 1, CreateQuad("/Quad", deformed, normals, rig));

        var lossDevice = (ISilkDeviceLossGraphicsDevice)device;
        var outlineDevice = (ISilkSelectionOutlineGraphicsDevice)device;
        ulong before = lossDevice.DeviceLossGeneration;
        ulong outlineBefore = outlineDevice.SelectionOutlineDeviceGeneration;
        armSubmissionDeviceLoss(device);

        await Assert.That(() => RenderInto(renderer, color, depth))
            .Throws<Exception>()
            .Because("a device loss must reach the reset path rather than fall back");
        await Assert.That(renderer.GpuResources.DeformationFallbacks)
            .IsEqualTo(0UL)
            .Because("a device loss is not a recoverable allocation failure");
        // The loss has to be the one the deformation pass itself provoked, or the
        // case would prove nothing about how that pass classifies a failure.
        await Assert.That(renderer.GpuResources.DeformationDispatches)
            .IsEqualTo(0UL)
            .Because("the armed loss must be the deformation pass's own submission");

        // The backend recorded the loss on its own general signal, which is what
        // makes it classifiable at all, on every backend.
        await Assert.That(lossDevice.DeviceLossGeneration)
            .IsNotEqualTo(before)
            .Because("the production submission path must record the loss it saw");

        // Whether a subsystem generation also moved is a backend's own business,
        // and the two supported backends genuinely differ: Direct3D 12 derives
        // the selection-outline generation from the picking one, which its
        // removal observation advances, while Vulkan keeps them separate and
        // advances neither for a loss on an ordinary submission. That difference
        // is the whole defect -- a classifier reading the selection generation
        // was right on one backend and silently wrong on the other -- so it is
        // pinned here rather than left to chance.
        if (selectionGenerationTracksSubmissionLoss)
        {
            await Assert.That(outlineDevice.SelectionOutlineDeviceGeneration)
                .IsNotEqualTo(outlineBefore);
        }
        else
        {
            await Assert.That(outlineDevice.SelectionOutlineDeviceGeneration)
                .IsEqualTo(outlineBefore)
                .Because(
                    "this backend does not invalidate selection outlines for a " +
                    "loss detected on an ordinary submission");
        }
    }

    /// <summary>
    /// Requires an image to equal the one the CPU-resolved record draws, and to
    /// differ from the bind pose.
    /// </summary>
    private static async Task AssertMatchesCpuGeometry(
        Func<ISilkGraphicsDevice> createDevice,
        SilkShaderBinaryFormat shaderFormat,
        float[] deformed,
        float[] normals,
        byte[] image)
    {
        byte[] reference = Render(
            createDevice,
            shaderFormat,
            CreateQuad("/Quad", deformed, normals, deformation: null),
            out _);
        byte[] bind = Render(
            createDevice,
            shaderFormat,
            CreateQuad("/Quad", BindPoints(), BindNormals(), deformation: null),
            out _);
        await Assert.That(CountLit(image))
            .IsGreaterThan(100)
            .Because("the recovered geometry must cover pixels");
        await Assert.That(image.AsSpan().SequenceEqual(reference))
            .IsTrue()
            .Because("a recovered geometry must draw the CPU-resolved surface");
        await Assert.That(image.AsSpan().SequenceEqual(bind))
            .IsFalse()
            .Because("a recovered geometry must not fall back to the bind pose");
    }

    /// <summary>
    /// Requires an ineligible rig to draw the authoritative CPU geometry rather
    /// than a bind pose, and to dispatch nothing.
    /// </summary>
    internal static async Task AnIneligibleRigDrawsTheCpuGeometry(
        Func<ISilkGraphicsDevice> createDevice,
        SilkShaderBinaryFormat shaderFormat)
    {
        ArgumentNullException.ThrowIfNull(createDevice);
        SilkMeshDeformationData rig = TranslationRig(0.9f);
        float[] deformed = EvaluatePoints(rig);
        float[] normals = EvaluateNormals(rig);

        // A rig with no authored bind normals cannot be renormalized on the GPU
        // from what the page carries, so it is refused before allocation.
        // hdSilk publishes the resolved points regardless, so the image must
        // still be the deformed one rather than the bind pose.
        byte[] ineligible = Render(
            createDevice,
            shaderFormat,
            CreateQuad("/Quad", deformed, normals, IneligibleRig(0.9f)),
            out int dispatches);
        byte[] reference = Render(
            createDevice,
            shaderFormat,
            CreateQuad("/Quad", deformed, normals, deformation: null),
            out _);
        byte[] bind = Render(
            createDevice,
            shaderFormat,
            CreateQuad("/Quad", BindPoints(), BindNormals(), deformation: null),
            out _);

        await Assert.That(dispatches)
            .IsEqualTo(0)
            .Because("an ineligible rig must not reach the kernel");
        await Assert.That(ineligible.AsSpan().SequenceEqual(reference))
            .IsTrue()
            .Because("an ineligible rig must draw the CPU-resolved geometry");
        await Assert.That(ineligible.AsSpan().SequenceEqual(bind))
            .IsFalse()
            .Because("an ineligible rig must not fall back to the bind pose");
    }

    /// <summary>
    /// Requires a shadow map rendered before the colour pass to see the
    /// deformed surface rather than the bind pose.
    /// </summary>
    /// <remarks>
    /// The shadow cache submits its own command list, which this renderer does
    /// not compose, so the deformation dispatch has to be ordered by submission
    /// rather than by an intra-list barrier alone. This measures that ordering:
    /// the caster is deformed, and the receiver behind it is shaded through the
    /// atlas the shadow pass produced.
    /// </remarks>
    internal static async Task ShadowsFollowTheDeformedSurface(
        Func<ISilkGraphicsDevice> createDevice,
        SilkShaderBinaryFormat shaderFormat)
    {
        ArgumentNullException.ThrowIfNull(createDevice);
        SilkMeshDeformationData near = CasterRig(0.0f);
        SilkMeshDeformationData far = CasterRig(1.0f);

        byte[] gpuNear = RenderShadowScene(createDevice, shaderFormat, near, out int nearDispatches);
        byte[] gpuFar = RenderShadowScene(createDevice, shaderFormat, far, out int farDispatches);
        byte[] cpuFar = RenderShadowScene(
            createDevice,
            shaderFormat,
            far,
            out int cpuDispatches,
            useCpuGeometry: true);

        await Assert.That(nearDispatches).IsEqualTo(1);
        await Assert.That(farDispatches).IsEqualTo(1);
        await Assert.That(cpuDispatches).IsEqualTo(0);
        await Assert.That(gpuFar.AsSpan().SequenceEqual(cpuFar))
            .IsTrue()
            .Because("a shadowed GPU-deformed scene must match the CPU-resolved one");
        await Assert.That(gpuFar.AsSpan().SequenceEqual(gpuNear))
            .IsFalse()
            .Because("moving the caster must move what the shadow pass rendered");
    }

    private static byte[] RenderShadowScene(
        Func<ISilkGraphicsDevice> createDevice,
        SilkShaderBinaryFormat shaderFormat,
        SilkMeshDeformationData rig,
        out int dispatches,
        bool useCpuGeometry = false)
    {
        using ISilkGraphicsDevice device = createDevice();
        using ISilkGraphicsTexture color = CreateColor(device);
        using ISilkGraphicsTexture depth = CreateDepth(device);
        using var renderer = new SilkMeshRenderer(device, shaderFormat);
        byte[] caster = useCpuGeometry
            ? CreateQuad("/Caster", EvaluatePoints(rig), EvaluateNormals(rig), deformation: null)
            : CreateQuad("/Caster", CasterBindPoints(), BindNormals(), rig);
        ApplyScene(
            renderer,
            1,
            CreateShadowLightFrame(),
            caster,
            CreateReceiver("/Floor"),
            CreateShadow());
        ulong before = renderer.GpuResources.DeformationDispatches;
        byte[] pixels = RenderInto(renderer, color, depth);
        dispatches = checked((int)(renderer.GpuResources.DeformationDispatches - before));
        return pixels;
    }

    private static byte[] Render(
        Func<ISilkGraphicsDevice> createDevice,
        SilkShaderBinaryFormat shaderFormat,
        byte[] mesh,
        out int dispatches)
    {
        using ISilkGraphicsDevice device = createDevice();
        using ISilkGraphicsTexture color = CreateColor(device);
        using ISilkGraphicsTexture depth = CreateDepth(device);
        using var renderer = new SilkMeshRenderer(device, shaderFormat);
        Apply(renderer, 1, mesh);
        ulong before = renderer.GpuResources.DeformationDispatches;
        byte[] pixels = RenderInto(renderer, color, depth);
        dispatches = checked((int)(renderer.GpuResources.DeformationDispatches - before));
        return pixels;
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

    private static void Apply(
        SilkMeshRenderer renderer,
        ulong revision,
        params byte[][] commands) =>
        ApplyScene(renderer, revision, CreateFrame(), commands);

    private static void ApplyScene(
        SilkMeshRenderer renderer,
        ulong revision,
        byte[] frame,
        params byte[][] commands)
    {
        byte[][] all = [frame, .. commands];
        int length = 0;
        foreach (byte[] command in all)
        {
            length += command.Length;
        }
        byte[] page = new byte[length];
        int offset = 0;
        foreach (byte[] command in all)
        {
            command.CopyTo(page, offset);
            offset += command.Length;
        }
        SilkSceneDelta delta = renderer.Scene.Apply(
            page,
            checked((uint)all.Length),
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

    private static float[] BindPoints() =>
    [
        -0.6f, -0.6f, QuadDepth,
        0.1f, -0.6f, QuadDepth,
        0.1f, 0.1f, QuadDepth,
        -0.6f, 0.1f, QuadDepth
    ];

    /// <summary>
    /// The view is the identity, so the camera looks along positive z and a
    /// surface facing it -- and facing the light -- points along negative z.
    /// </summary>
    private static float[] BindNormals() =>
    [
        0, 0, -1,
        0, 0, -1,
        0, 0, -1,
        0, 0, -1
    ];

    /// <summary>
    /// A single-joint rig that translates the whole quad along x and y, so a
    /// pose change is a large, unambiguous pixel change rather than a subtle
    /// shading difference.
    /// </summary>
    private static SilkMeshDeformationData TranslationRig(float pose) =>
        BuildRig(BindPoints(), pose * 0.6f, pose * 0.4f);

    /// <summary>
    /// The shadow scene's caster: a small quad in front of the receiver that the
    /// rig slides along x, so the only thing that can move its shadow is the
    /// deformed position the shadow pass sampled.
    /// </summary>
    private static SilkMeshDeformationData CasterRig(float pose) =>
        BuildRig(CasterBindPoints(), pose * 0.3f, 0f);

    /// <summary>
    /// The same pose carried by a rig with no authored bind normals, which the
    /// GPU path refuses because it cannot rebuild the published normals.
    /// </summary>
    private static SilkMeshDeformationData IneligibleRig(float pose) =>
        BuildRig(BindPoints(), pose * 0.6f, pose * 0.4f, includeBindNormals: false);

    private static float[] CasterBindPoints() =>
    [
        -0.3f, -0.3f, CasterDepth,
        0.3f, -0.3f, CasterDepth,
        0.3f, 0.3f, CasterDepth,
        -0.3f, 0.3f, CasterDepth
    ];

    private static SilkMeshDeformationData BuildRig(
        float[] bindPoints,
        float translateX,
        float translateY,
        bool includeBindNormals = true)
    {
        float[] joint =
        [
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            translateX, translateY, 0, 1
        ];
        return SilkDeformationRigBuilder.Build(
            bindPoints: bindPoints,
            bindNormals: BindNormals(),
            influencesPerPoint: 1,
            jointIndices: [0, 0, 0, 0],
            jointWeights: [1, 1, 1, 1],
            jointMatrices: joint,
            geomBindTransform:
            [
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                0, 0, 0, 1
            ],
            includeBindNormals: includeBindNormals);
    }

    /// <summary>
    /// The scenes are lit rather than flat shaded: a deformed surface publishes
    /// vertex normals, so an unlit frame would render every image black and
    /// every comparison below would be vacuous.
    /// </summary>
    private static byte[] CreateFrame() => CreateShadowLightFrame();

    /// <summary>
    /// Builds the 1976-byte lighting frame carrying one shadow-casting distant
    /// light, in the same encoding <see cref="SilkShadowConformance"/> uses.
    /// </summary>
    private static byte[] CreateShadowLightFrame()
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

        // OPENUSD_SILK_LIGHT_DISTANT with shadows enabled.
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

    /// <summary>
    /// Builds the ABI v19 shadow table with one orthographic descriptor fitted
    /// to the caster and receiver planes.
    /// </summary>
    private static byte[] CreateShadow()
    {
        const int descriptorSize = 288;
        byte[] bytes = new byte[24 + descriptorSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Shadow);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 1u);

        var center = new Vector3(0, 0, (ReceiverDepth + CasterDepth) * 0.5f);
        var half = new Vector3(1, 1, (CasterDepth - ReceiverDepth) * 0.5f);
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

    private static byte[] CreateReceiver(string path) =>
        CreateQuad(
            path,
            [
                -1f, -1f, ReceiverDepth,
                1f, -1f, ReceiverDepth,
                1f, 1f, ReceiverDepth,
                -1f, 1f, ReceiverDepth
            ],
            BindNormals(),
            deformation: null,
            primId: 2);

    /// <summary>
    /// Encodes one MESH_UPSERT for a quad, optionally carrying a deformation
    /// block. The encoder is hand-written on purpose: the point of an end-to-end
    /// gate is that the bytes are produced independently of the parser and the
    /// renderer under test.
    /// </summary>
    private static byte[] CreateQuad(
        string pathValue,
        float[] points,
        float[] normals,
        SilkMeshDeformationData? deformation,
        int primId = 1)
    {
        byte[] path = Encoding.UTF8.GetBytes(pathValue);
        float[] emittedPoints = points;
        float[] emittedNormals = normals;
        uint[] indices = [0, 2, 1, 0, 3, 2];
        int pointCount = emittedPoints.Length / 3;
        byte[] block = deformation is null ? [] : EncodeBlock(deformation);
        byte[] name = Encoding.UTF8.GetBytes("normals");
        int attributeBytes = 20 + name.Length + (emittedNormals.Length * sizeof(float));
        int size = MeshFixedSize +
            path.Length +
            (emittedPoints.Length * sizeof(float)) +
            (indices.Length * sizeof(uint)) +
            (2 * sizeof(uint)) +
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
        ReadOnlySpan<double> identity =
        [
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1
        ];
        for (int element = 0; element < 16; element++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(80 + (element * sizeof(double))),
                identity[element]);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(224),
            deformation is null ? 0u : (uint)deformation.Options);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(228), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(232), (uint)block.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(220), 1);

        path.CopyTo(bytes, MeshFixedSize);
        int cursor = MeshFixedSize + path.Length;
        foreach (float value in emittedPoints)
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

        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(cursor),
            (uint)SilkAttributeSemantic.Normal);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor + 4), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(cursor + 8),
            (uint)SilkAttributeInterpolation.Vertex);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor + 12), (uint)name.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor + 16), (uint)pointCount);
        name.CopyTo(bytes, cursor + 20);
        cursor += 20 + name.Length;
        foreach (float value in emittedNormals)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(cursor), value);
            cursor += sizeof(float);
        }
        block.CopyTo(bytes, cursor);
        return bytes;
    }

    /// <summary>Re-encodes a decoded rig as the block bytes a page carries.</summary>
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
