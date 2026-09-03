// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

/// <summary>
/// Renders a receiver in front of a caster under one authored distant light and
/// requires the published shadow descriptor to darken exactly the covered pixels.
/// </summary>
/// <remarks>
/// <para>
/// The case is analytic rather than a reference image. A white receiver quad
/// fills the frame at the near depth and a smaller white caster quad sits behind
/// it, out of the camera's sight, at the far depth. One white distant light is
/// aimed from the far side and tilted in X, so the caster's shadow lands on the
/// receiver offset by exactly the depth separation times the tilt: every gate
/// below samples a pixel whose shadowed or lit state is computed from that
/// geometry rather than compared against a captured image.
/// </para>
/// <para>
/// Each gate measures something a shadow slice can get wrong without changing
/// coverage. A map that is never rendered leaves the shadowed sample lit; a map
/// sampled with the wrong Y or depth convention moves the shadow somewhere else;
/// a missing bias makes the lit receiver self-shadow into acne, which shows up as
/// the lit sample moving at all; moving the caster must move the shadow, which no
/// stale map can produce; a caster the shadow-link table excludes must stop
/// occluding; and retiring the descriptor table must reproduce the unshadowed
/// image byte for byte.
/// </para>
/// <para>
/// It runs on the D3D12 WARP and Vulkan SwiftShader devices, so the evidence is
/// cross-backend and needs no GPU.
/// </para>
/// </remarks>
internal static class SilkShadowConformance
{
    private const string Receiver = "/World/Receiver";
    private const string Caster = "/World/Caster";
    private const string CutoutMaterial = "/World/Materials/Cutout";
    private const uint Size = 64;

    // The receiver is at depth 0.2 and the caster at 0.8, so the light's X tilt of
    // 0.6 offsets the shadow by 0.6 * 0.6 = 0.36 world units. The caster spans x
    // in [-0.25, 0.25], which shadows the receiver over x in [-0.61, -0.11]; the
    // sample at x = -0.36 is its centre and the sample at x = +0.6 is well clear.
    private const float ReceiverDepth = 0.2f;
    private const float CasterDepth = 0.8f;
    private const float LightTiltX = 0.6f;
    private const int ShadowedX = 20;
    private const int LitX = 51;
    private const int SampleY = 32;

    // The Y-asymmetry gate tilts the light only in Y by the same 0.6, so the
    // shadow lands 0.36 world units toward -Y and occupies rows 36..50 of the
    // 64-row frame. A caster matrix rendered without the device's clip-Y
    // convention spreads it over rows 24..63 on Vulkan instead, which the guard
    // rows below detect and which a Y-symmetric scene cannot.
    private const float YTilt = 0.6f;
    private const int ShadowedYRow = 43;
    private const string ShadowedYRowSpan = "36..50";
    private const int MirroredYRow = 20;

    // The rotated receiver sits deeper so that its 28-degree tilt keeps every
    // corner inside the [0, 1] clip-depth range. Its light is tilted in both axes
    // so the surface meets it well off normal incidence: with the correct
    // world-space normal the incidence is about 48 degrees, and with the
    // object-space one the shader would both offset along the wrong axis and
    // under-estimate the slope.
    private const float TiltedReceiverDepth = 0.35f;
    private const float TiltedLightTiltX = 0.6f;
    private const float TiltedLightTiltY = 0.2f;

    internal static async Task AnAuthoredDistantLightCastsAMeasurableShadow(
        ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        await Assert.That(device.Capabilities.SupportsRasterShadows)
            .IsTrue()
            .Because("This gate exists to measure the depth-only shadow pass.");

        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);

        // Revision 1: shadow-enable is authored but no descriptor has been
        // published, so the scene renders exactly as an unshadowed one.
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 1,
            CreateShadowLightFrame(),
            CreateReceiver(),
            CreateCaster(x: 0));
        _ = renderer.Render(color, depth);
        byte[] unshadowed = ReadPixels(color);
        await Assert.That(renderer.Scene.Shadows.HasShadows)
            .IsFalse()
            .Because("No shadow command was published, so nothing may be retained.");
        await AssertLit(unshadowed, ShadowedX, "unshadowed receiver under the caster");
        await AssertLit(unshadowed, LitX, "unshadowed receiver beside the caster");

        // Revision 2: the descriptor arrives, and the caster must darken the
        // receiver behind it and nothing else.
        SilkMeshRendererConformance.Apply(renderer, revision: 2, CreateShadow());
        _ = renderer.Render(color, depth);
        byte[] shadowed = ReadPixels(color);
        await Assert.That(renderer.Scene.Shadows.HasShadows).IsTrue();
        await Assert.That(renderer.Scene.Shadows.ResolveSlot(0)).IsEqualTo(0);
        await AssertShadowed(shadowed, ShadowedX, "shadowed receiver");
        await AssertLit(shadowed, LitX, "unoccluded receiver");

        // The lit receiver must be bit-identical to the unshadowed render. A
        // missing or too-small depth bias shows up here as acne long before it is
        // visible as a shape, because a self-shadowing receiver moves the channel
        // measured at this pixel.
        int litOffset = Offset(LitX);
        await Assert.That(
                shadowed.AsSpan(litOffset, 4).SequenceEqual(unshadowed.AsSpan(litOffset, 4)))
            .IsTrue()
            .Because("A lit receiver must not self-shadow when a shadow map exists.");

        // Revision 3: moving the caster moves the shadow. The receiver behind the
        // old position must come back lit, which no retained map can produce.
        SilkMeshRendererConformance.Apply(renderer, revision: 3, CreateCaster(x: 0.6));
        _ = renderer.Render(color, depth);
        await AssertLit(ReadPixels(color), ShadowedX, "receiver the caster left");

        // Revision 4: put the caster back and confirm the shadow returns, so the
        // next gate measures a caster restriction rather than a stale map.
        SilkMeshRendererConformance.Apply(renderer, revision: 4, CreateCaster(x: 0));
        _ = renderer.Render(color, depth);
        await AssertShadowed(ReadPixels(color), ShadowedX, "restored shadow");

        // Revision 5: UsdLux collection:shadowLink is a caster restriction, so a
        // caster the light's shadow collection excludes must stop occluding while
        // the light still reaches it.
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 5,
            CreateLightLink(
                lightCount: 1,
                (Caster, SilkLightLinkCommand.AllInstances, 0b1u, 0b0u)));
        _ = renderer.Render(color, depth);
        await AssertLit(ReadPixels(color), ShadowedX, "receiver behind a linked-out caster");

        // Revision 6: retiring the descriptor table must reproduce the unshadowed
        // image exactly, which is what proves the map is released rather than left
        // bound with stale contents.
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 6,
            CreateLightLink(lightCount: 0),
            CreateShadow(descriptorCount: 0));
        _ = renderer.Render(color, depth);
        byte[] retired = ReadPixels(color);
        await Assert.That(renderer.Scene.Shadows.HasShadows).IsFalse();
        await Assert.That(renderer.ShadowMapCount)
            .IsEqualTo(0)
            .Because("A retired shadow table must release every retained map.");
        await Assert.That(retired.AsSpan().SequenceEqual(unshadowed))
            .IsTrue()
            .Because("Retiring the shadow table must reproduce the unshadowed image exactly.");
    }

    /// <summary>
    /// Requires a retained shadow map to be reused while nothing it was produced
    /// from changes, and re-rendered exactly once when its casters deform.
    /// </summary>
    internal static async Task ARetainedShadowMapIsReusedUntilItsCastersMove(
        ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);

        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 1,
            CreateShadowLightFrame(),
            CreateReceiver(),
            CreateCaster(x: 0),
            CreateShadow());
        _ = renderer.Render(color, depth);
        ulong afterFirst = renderer.ShadowMapRenderCount;
        await Assert.That(afterFirst).IsEqualTo(1ul);
        await Assert.That(renderer.ShadowMapCount).IsEqualTo(1);
        byte[] first = ReadPixels(color);
        await AssertShadowed(first, ShadowedX, "shadowed receiver");

        // Re-rendering an unchanged scene must not touch the map at all, and must
        // reproduce the image byte for byte from the retained one.
        _ = renderer.Render(color, depth);
        await Assert.That(renderer.ShadowMapRenderCount)
            .IsEqualTo(afterFirst)
            .Because("An unchanged scene must reuse its retained shadow map.");
        await Assert.That(ReadPixels(color).AsSpan().SequenceEqual(first))
            .IsTrue()
            .Because("A reused shadow map must reproduce the image exactly.");

        // A deformation replaces the caster's points inside the bounds the page
        // published, which the descriptor table cannot see. The map has to be
        // re-rendered from the geometry revision instead. The deformed caster is
        // the same size in a different place, so the depth-only pass is the only
        // thing that can move the shadow.
        SilkDeformationResult deformed = renderer.Scene.ReplacePoints(
            Caster,
            0,
            [
                0.35f, -0.25f, CasterDepth,
                0.85f, -0.25f, CasterDepth,
                0.85f,  0.25f, CasterDepth,
                0.35f,  0.25f, CasterDepth,
            ],
            out ulong meshId);
        await Assert.That(deformed).IsEqualTo(SilkDeformationResult.Applied);
        renderer.GpuResources.Apply(
            renderer.Scene,
            new SilkSceneDelta(new[] { meshId }, ReadOnlyMemory<ulong>.Empty));
        _ = renderer.Render(color, depth);
        await Assert.That(renderer.ShadowMapRenderCount)
            .IsEqualTo(afterFirst + 1)
            .Because("A deformed caster must re-render its shadow map exactly once.");

        // The deformed caster occupies a different part of the map, so the pixel it
        // used to shadow has to come back lit. That is what separates re-rendering
        // the map from merely counting that it was re-rendered.
        await AssertLit(ReadPixels(color), ShadowedX, "receiver a deformed caster left");
    }

    /// <summary>
    /// Requires a caster's shadow to follow its material turning opacity-masked
    /// and opaque again, with no mesh, link, or shadow command in between.
    /// </summary>
    /// <remarks>
    /// Caster selection reads the material, so a material re-authored in place --
    /// same prim, same binding, same geometry -- changes which prims are in the
    /// map. Nothing in the geometry, link, or descriptor revisions moves for that
    /// edit, so a retained map keyed only on those is reused with the opposite
    /// caster set and the diagnostic naming its skipped casters outlives the
    /// condition. This drives the transition in both directions, because only one
    /// of them is caught by a cache that clears on any material change but never
    /// re-populates.
    /// </remarks>
    internal static async Task AMaterialTurningMaskedAndOpaqueAgainReRendersTheMap(
        ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);

        // Revision 1: the caster is bound to a material with no opacity inputs, so
        // it casts normally.
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 1,
            CreateShadowLightFrame(),
            CreateCasterMaterial(masked: false),
            CreateReceiver(),
            CreateCutoutCaster(x: 0),
            CreateShadow());
        _ = renderer.Render(color, depth);
        byte[] opaque = ReadPixels(color);
        ulong afterOpaque = renderer.ShadowMapRenderCount;

        await Assert.That(afterOpaque).IsEqualTo(1ul);
        await AssertShadowed(opaque, ShadowedX, "caster with an opaque material");
        await Assert.That(HasCasterDiagnostic(renderer))
            .IsFalse()
            .Because("An opaque caster must not be named as unsupported.");

        // Revision 2: only the material changes. No mesh, link or shadow command
        // is published, so nothing but the material revision moves.
        ulong geometryBefore = renderer.Scene.GeometryRevision;
        ulong linksBefore = renderer.Scene.LightLinks.Revision;
        ulong shadowsBefore = renderer.Scene.Shadows.Revision;
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 2,
            CreateCasterMaterial(masked: true));

        await Assert.That(renderer.Scene.GeometryRevision).IsEqualTo(geometryBefore);
        await Assert.That(renderer.Scene.LightLinks.Revision).IsEqualTo(linksBefore);
        await Assert.That(renderer.Scene.Shadows.Revision).IsEqualTo(shadowsBefore);
        await Assert.That(renderer.Scene.MaterialRevision)
            .IsGreaterThan(0ul)
            .Because("Re-authoring a material must move the retained material revision.");

        _ = renderer.Render(color, depth);
        byte[] masked = ReadPixels(color);

        await Assert.That(renderer.ShadowMapRenderCount)
            .IsEqualTo(afterOpaque + 1)
            .Because(
                "A caster that turned opacity-masked must re-render the shadow map " +
                "exactly once, not reuse the map it was drawn into.");
        await AssertLit(masked, ShadowedX, "receiver behind a newly masked caster");
        await Assert.That(HasCasterDiagnostic(renderer))
            .IsTrue()
            .Because("A skipped opacity-masked caster must be named.");

        // Revision 3: back to opaque, again with only a material command. The
        // shadow must return and the diagnostic must clear.
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 3,
            CreateCasterMaterial(masked: false));
        _ = renderer.Render(color, depth);
        byte[] reopened = ReadPixels(color);

        await Assert.That(renderer.ShadowMapRenderCount)
            .IsEqualTo(afterOpaque + 2)
            .Because("A caster that turned opaque again must re-render the map once more.");
        await AssertShadowed(reopened, ShadowedX, "caster whose material turned opaque again");
        await Assert.That(HasCasterDiagnostic(renderer))
            .IsFalse()
            .Because("A caster that is no longer masked must stop being named.");
        await Assert.That(reopened.AsSpan().SequenceEqual(opaque))
            .IsTrue()
            .Because("Returning to the opaque material must reproduce its image exactly.");

        // Rendering again with nothing changed must reuse the map, so the
        // invalidation above is a response to the material and not a cache that
        // stopped retaining anything.
        _ = renderer.Render(color, depth);
        await Assert.That(renderer.ShadowMapRenderCount)
            .IsEqualTo(afterOpaque + 2)
            .Because("An unchanged material must still reuse the retained map.");
    }

    private static bool HasCasterDiagnostic(SilkMeshRenderer renderer)
    {
        foreach (RenderDiagnostic entry in renderer.GpuResources.Diagnostics.Entries)
        {
            if (entry.Code == SilkRenderDiagnosticCodes.ShadowCasterUnsupported &&
                entry.Message.Contains(Caster, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static int Offset(int x) => checked(((SampleY * (int)Size) + x) * 4);

    /// <summary>
    /// Requires a caster the light does not illuminate to still cast its shadow.
    /// </summary>
    /// <remarks>
    /// UsdLux resolves <c>collection:lightLink</c> and <c>collection:shadowLink</c>
    /// as separate collections over the same light. Intersecting them -- which the
    /// producer, the wire parser and the probe all used to do -- silently deletes
    /// the shadow of an unlit blocker, which is a scene an author can build on
    /// purpose and cannot debug from the image.
    /// </remarks>
    internal static async Task AnUnlitBlockerStillCastsItsShadow(ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);

        // The caster is linked out of the light entirely and linked into its
        // caster collection: light mask 0, shadow mask 1.
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 1,
            CreateShadowLightFrame(),
            CreateReceiver(),
            CreateCaster(x: 0),
            CreateLightLink(
                lightCount: 1,
                (Caster, SilkLightLinkCommand.AllInstances, 0b0u, 0b1u)),
            CreateShadow());
        _ = renderer.Render(color, depth);

        SilkLightLinkMasks masks = renderer.Scene.LightLinks.Resolve(Caster, 0);
        await Assert.That(masks.LightMask).IsEqualTo(0u);
        await Assert.That(masks.ShadowMask).IsEqualTo(1u);
        await Assert.That(masks.CastsShadow(0)).IsTrue();
        await Assert.That(masks.IsLit(0)).IsFalse();

        byte[] pixels = ReadPixels(color);
        await AssertShadowed(pixels, ShadowedX, "receiver behind an unlit blocker");
        await AssertLit(pixels, LitX, "receiver beside an unlit blocker");
    }

    /// <summary>
    /// Requires a shadow offset in Y to land on the same side of the frame on
    /// every backend.
    /// </summary>
    /// <remarks>
    /// The shadow pass rasterizes with the device's own clip-space convention, and
    /// Vulkan's clip Y points down. A caster matrix that skipped the mirror stored
    /// the map upside down on Vulkan while the colour pass reconstructed the atlas
    /// coordinate with one convention everywhere, which is invisible to a
    /// Y-symmetric scene and puts the shadow on the wrong side of anything else.
    /// The light here is tilted only in Y, so the expected row is computed from the
    /// geometry and the same value is required on both backends.
    /// </remarks>
    internal static async Task AYTiltedShadowLandsOnTheComputedSide(
        ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);

        byte[] frame = CreateShadowLightFrame(0f, YTilt);
        byte[] shadow = CreateShadow(
            descriptorCount: 1,
            tiltX: 0f,
            tiltY: YTilt,
            boundsMinimum: new Vector3(0, 0, ReceiverDepth),
            boundsMaximum: new Vector3(0, 0, CasterDepth),
            normalBiasTexels: 1.5);

        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 1,
            frame,
            CreateReceiver(),
            CreateCaster(x: 0));
        _ = renderer.Render(color, depth);
        byte[] unshadowed = ReadPixels(color);

        SilkMeshRendererConformance.Apply(renderer, revision: 2, shadow);
        _ = renderer.Render(color, depth);
        byte[] shadowed = ReadPixels(color);

        // The caster sits a depth separation of 0.6 behind the receiver and the
        // light is tilted +0.6 in Y, so the shadow occupies exactly rows 36..50 of
        // the 64-row frame: world y in [-0.5625, -0.125], which is the band the
        // geometry computes. The whole span is asserted rather than one sample,
        // because a caster matrix that skips the device's clip-space convention
        // does not remove the shadow, it relocates it -- measured 36..50 with the
        // convention applied on both backends, and 24..63 on Vulkan without it,
        // which still darkens the row a single-sample gate would check.
        await Assert.That(DescribeDarkRows(shadowed))
            .IsEqualTo(ShadowedYRowSpan)
            .Because(
                "The Y-tilted shadow must occupy the computed rows on every " +
                "backend, not merely darken one of them.");
        await AssertShadowedAt(shadowed, ShadowedYRow, "Y-tilted shadow");
        await AssertLitAt(shadowed, MirroredYRow, "the Y-mirror of the shadow");
        await Assert.That(DescribeDarkRows(unshadowed))
            .IsEqualTo("none")
            .Because("The unshadowed frame must have no dark row at all.");

        int mirroredOffset = OffsetAt(Size / 2, MirroredYRow);
        await Assert.That(shadowed.AsSpan(mirroredOffset, 4)
                .SequenceEqual(unshadowed.AsSpan(mirroredOffset, 4)))
            .IsTrue()
            .Because("A Y-mirrored shadow map would darken exactly this row instead.");
    }

    /// <summary>
    /// Requires a rotated, non-uniformly scaled receiver that is its own only
    /// caster to stay bit-identical to the unshadowed render.
    /// </summary>
    /// <remarks>
    /// The shadow bias offsets the receiver along its normal, in world space,
    /// before projecting it into the light. The interpolated vertex normal reaches
    /// the fragment stage in object space, so under a rotation or a non-uniform
    /// scale it points somewhere else entirely and the offset stops compensating
    /// for the map's own quantization: the surface shadows itself. Comparing a
    /// self-casting slanted receiver against the same frame with no shadow map is
    /// a whole-frame acne gate that an axis-aligned scene cannot provide.
    /// </remarks>
    internal static async Task ARotatedNonUniformReceiverDoesNotSelfShadow(
        ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);

        // Rotated 28 degrees about X and scaled non-uniformly, so the world normal
        // and the object normal differ by the rotation and the light meets the
        // surface at a grazing angle.
        byte[] receiver = CreateTransformedReceiver(
            RotateXScaleTranslate(28.0, 1.6f, 1.25f, TiltedReceiverDepth));
        byte[] frame = CreateShadowLightFrame(TiltedLightTiltX, TiltedLightTiltY);

        SilkMeshRendererConformance.Apply(renderer, revision: 1, frame, receiver);
        _ = renderer.Render(color, depth);
        byte[] unshadowed = ReadPixels(color);

        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 2,
            CreateShadow(
                descriptorCount: 1,
                tiltX: TiltedLightTiltX,
                tiltY: TiltedLightTiltY,
                boundsMinimum: new Vector3(-1.6f, -1.2f, TiltedReceiverDepth - 0.6f),
                boundsMaximum: new Vector3(1.6f, 1.2f, TiltedReceiverDepth + 0.6f),
                normalBiasTexels: 2.0));
        _ = renderer.Render(color, depth);
        byte[] shadowed = ReadPixels(color);

        await Assert.That(renderer.Scene.Shadows.HasShadows).IsTrue();
        await Assert.That(renderer.ShadowMapCount).IsEqualTo(1);

        int darkened = 0;
        for (int index = 0; index < unshadowed.Length; index += 4)
        {
            if (shadowed[index] + 2 < unshadowed[index])
            {
                darkened++;
            }
        }

        await Assert.That(darkened)
            .IsEqualTo(0)
            .Because(
                "A rotated, non-uniformly scaled receiver that is its own only " +
                $"caster must not self-shadow; {darkened} pixels darkened.");
    }

    /// <summary>
    /// Requires an opacity-masked caster to be dropped from the shadow map and
    /// named, rather than casting the solid shadow of its geometry.
    /// </summary>
    /// <remarks>
    /// The depth-only caster program binds no material and cannot discard, so an
    /// alpha-tested cutout would shadow as an opaque quad. That is a
    /// plausible-but-wrong image, so the caster is skipped and reported instead.
    /// </remarks>
    internal static async Task AnOpacityMaskedCasterIsSkippedAndNamed(
        ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);

        // Baseline: the same caster with no material bound shadows the receiver.
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 1,
            CreateShadowLightFrame(),
            CreateReceiver(),
            CreateCaster(x: 0),
            CreateShadow());
        _ = renderer.Render(color, depth);
        await AssertShadowed(ReadPixels(color), ShadowedX, "opaque caster");

        // Rebinding the same caster to an alpha-tested material must remove its
        // shadow and name it, rather than keep casting a solid one.
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 2,
            CreateCutoutMaterial(),
            CreateCutoutCaster(x: 0));
        _ = renderer.Render(color, depth);
        byte[] pixels = ReadPixels(color);

        await AssertLit(pixels, ShadowedX, "receiver behind an opacity-masked caster");
        RenderDiagnostic diagnostic = renderer.GpuResources.Diagnostics.Entries.Single(
            entry => entry.Code == SilkRenderDiagnosticCodes.ShadowCasterUnsupported);
        await Assert.That(diagnostic.Message).Contains(Caster);
        await Assert.That(diagnostic.Message).Contains("opacity-masked");
        await Assert.That(diagnostic.Severity).IsEqualTo(RenderDiagnosticSeverity.Warning);
    }

    private static async Task AssertLit(byte[] pixels, int x, string what)
    {
        int offset = Offset(x);
        await Assert.That(pixels[offset])
            .IsGreaterThan((byte)90)
            .Because(Describe(pixels, x, what));
    }

    private static async Task AssertShadowed(byte[] pixels, int x, string what)
    {
        int offset = Offset(x);
        await Assert.That(pixels[offset])
            .IsLessThan((byte)40)
            .Because(Describe(pixels, x, what));
        await Assert.That(pixels[offset + 3])
            .IsGreaterThan((byte)100)
            .Because(Describe(pixels, x, what));
    }

    private static string Describe(byte[] pixels, int x, string what)
    {
        int offset = Offset(x);
        return $"The {what} at ({x},{SampleY}) was rgba({pixels[offset]}," +
            $"{pixels[offset + 1]},{pixels[offset + 2]},{pixels[offset + 3]}).";
    }

    private static int OffsetAt(uint x, int y) => checked(((y * (int)Size) + (int)x) * 4);

    /// <summary>
    /// Describes the contiguous span of image rows whose centre column is dark, so
    /// a mis-placed shadow reports where it actually landed instead of only that
    /// one sampled row was wrong.
    /// </summary>
    private static string DescribeDarkRows(byte[] pixels)
    {
        int first = -1;
        int last = -1;
        for (int row = 0; row < Size; row++)
        {
            if (pixels[OffsetAt(Size / 2, row)] < 40)
            {
                first = first < 0 ? row : first;
                last = row;
            }
        }
        return first < 0 ? "none" : $"{first}..{last}";
    }

    private static async Task AssertLitAt(byte[] pixels, int y, string what)
    {
        int offset = OffsetAt(Size / 2, y);
        await Assert.That(pixels[offset])
            .IsGreaterThan((byte)90)
            .Because(
                $"The {what} at ({Size / 2},{y}) was {pixels[offset]}; " +
                $"dark rows were {DescribeDarkRows(pixels)}.");
    }

    private static async Task AssertShadowedAt(byte[] pixels, int y, string what)
    {
        int offset = OffsetAt(Size / 2, y);
        await Assert.That(pixels[offset])
            .IsLessThan((byte)40)
            .Because(
                $"The {what} at ({Size / 2},{y}) was {pixels[offset]}; " +
                $"dark rows were {DescribeDarkRows(pixels)}.");
        await Assert.That(pixels[offset + 3]).IsGreaterThan((byte)100);
    }

    /// <summary>
    /// Builds the row-major, row-vector object-to-world transform of a receiver
    /// rotated about X, scaled non-uniformly in X and Y, and pushed to a depth.
    /// </summary>
    private static double[] RotateXScaleTranslate(
        double degrees,
        float scaleX,
        float scaleY,
        float depth)
    {
        double radians = degrees * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);

        // Scale first, then rotate, then translate, composed for row vectors so
        // the world position is p * S * R * T.
        return
        [
            scaleX, 0, 0, 0,
            0, scaleY * cos, scaleY * sin, 0,
            0, -sin, cos, 0,
            0, 0, depth, 1,
        ];
    }

    private static byte[] CreateTransformedReceiver(double[] transform)
    {
        byte[] mesh = SilkMeshRendererConformance.CreateMeshCommand(
            1,
            Receiver,
            [
                -1.0f, -1.0f, 0f,
                 1.0f, -1.0f, 0f,
                 1.0f,  1.0f, 0f,
                -1.0f,  1.0f, 0f,
            ],
            [0, 2, 1, 0, 3, 2],
            0,
            0,
            [1, 1, 1, 1]);

        // MESH_UPSERT carries the row-major object-to-world transform at offset 80.
        for (int element = 0; element < 16; element++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                mesh.AsSpan(80 + (element * 8)),
                transform[element]);
        }
        return mesh;
    }

    /// <summary>
    /// Builds the caster's UsdPreviewSurface material, either opaque or
    /// alpha-tested by a non-zero opacity threshold.
    /// </summary>
    /// <remarks>
    /// Both forms carry the same number of scalars, so the two pages differ only
    /// in which input is authored: a transition between them is a material edit
    /// and nothing else.
    /// </remarks>
    private static byte[] CreateCasterMaterial(bool masked)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(CutoutMaterial);
        List<byte> payload =
        [
            .. BitConverter.GetBytes(ComputeStableHash(CutoutMaterial)),
            .. BitConverter.GetBytes((uint)pathBytes.Length),
            .. BitConverter.GetBytes((uint)SilkSurfaceKind.PreviewSurface),
            .. BitConverter.GetBytes(1u),
            .. BitConverter.GetBytes(0u),
            .. pathBytes,
            .. BitConverter.GetBytes((uint)(masked
                ? SilkMaterialParameter.OpacityThreshold
                : SilkMaterialParameter.Roughness)),
            .. BitConverter.GetBytes(1u),
            .. BitConverter.GetBytes(0.5f),

            // The generated MaterialX SPIR-V and MSL payloads are both empty, and
            // the folded texture-coordinate transform is the identity affine.
            .. BitConverter.GetBytes(0u),
            .. BitConverter.GetBytes(0u),
            .. BitConverter.GetBytes(1f),
            .. BitConverter.GetBytes(0f),
            .. BitConverter.GetBytes(0f),
            .. BitConverter.GetBytes(1f),
            .. BitConverter.GetBytes(0f),
            .. BitConverter.GetBytes(0f),
        ];

        var bytes = new byte[8 + payload.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MaterialUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        payload.CopyTo(bytes, 8);
        return bytes;
    }

    private static byte[] CreateCutoutMaterial() => CreateCasterMaterial(masked: true);

    private static byte[] CreateCutoutCaster(double x)
    {
        byte[] mesh = CreateCaster(x);
        byte[] materialBytes = Encoding.UTF8.GetBytes(CutoutMaterial);
        Array.Resize(ref mesh, mesh.Length + materialBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(4), (uint)mesh.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(
            mesh.AsSpan(208),
            ComputeStableHash(CutoutMaterial));
        BinaryPrimitives.WriteUInt32LittleEndian(
            mesh.AsSpan(216),
            (uint)materialBytes.Length);
        materialBytes.CopyTo(mesh.AsSpan(mesh.Length - materialBytes.Length));
        return mesh;
    }

    private static ulong ComputeStableHash(string value)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        foreach (byte b in Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= prime;
        }
        return hash;
    }

    private static byte[] ReadPixels(ISilkGraphicsTexture color)
    {
        var pixels = new byte[checked((int)(color.Width * color.Height * 4))];
        color.ReadbackForTesting(pixels);
        return pixels;
    }

    /// <summary>
    /// Builds the lighting frame with a single white distant light at index 0,
    /// tilted in X, authoring <c>inputs:shadow:enable</c>.
    /// </summary>
    private static byte[] CreateShadowLightFrame() =>
        CreateShadowLightFrame(LightTiltX, 0f);

    private static byte[] CreateShadowLightFrame(float tiltX, float tiltY)
    {
        const int lightingSize = 1976;
        const int lightCountOffset = 536;
        const int lightTableOffset = 552;
        var bytes = new byte[lightingSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), checked((int)Size));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), checked((int)Size));
        double[] identity = SilkMeshRendererConformance.Identity();
        for (int i = 0; i < 16; i++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(16 + (i * 8)), identity[i]);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(144 + (i * 8)), identity[i]);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(lightCountOffset), 1);

        // OPENUSD_SILK_LIGHT_DISTANT. The frame light table carries the raw ABI
        // value; there is no managed enum for it.
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(lightTableOffset), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(lightTableOffset + 4), 1u);
        for (int component = 0; component < 3; component++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(lightTableOffset + 16 + (component * 4)),
                1f);
        }
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(lightTableOffset + 28), 1f);

        // The light-to-world transform's third row is the direction from a shaded
        // point toward the light, which is what the frame table publishes and what
        // the shadow descriptor below is derived from.
        double[] transform = SilkMeshRendererConformance.Identity();
        Vector3 direction = LightDirection(tiltX, tiltY);
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

    private static Vector3 LightDirection(float tiltX, float tiltY) =>
        Vector3.Normalize(new Vector3(tiltX, tiltY, 1));

    private static byte[] CreateReceiver() =>
        SilkMeshRendererConformance.CreateMeshCommand(
            1,
            Receiver,
            [
                -1.0f, -1.0f, ReceiverDepth,
                 1.0f, -1.0f, ReceiverDepth,
                 1.0f,  1.0f, ReceiverDepth,
                -1.0f,  1.0f, ReceiverDepth,
            ],
            [0, 2, 1, 0, 3, 2],
            0,
            0,
            [1, 1, 1, 1]);

    private static byte[] CreateCaster(double x) => CreateCaster(x, 0);

    private static byte[] CreateCaster(double x, double y) =>
        SilkMeshRendererConformance.CreateMeshCommand(
            2,
            Caster,
            [
                -0.25f, -0.25f, CasterDepth,
                 0.25f, -0.25f, CasterDepth,
                 0.25f,  0.25f, CasterDepth,
                -0.25f,  0.25f, CasterDepth,
            ],
            [0, 2, 1, 0, 3, 2],
            x,
            y,
            [1, 1, 1, 1]);

    /// <summary>
    /// Builds the ABI v19 shadow table with one orthographic descriptor derived
    /// from the light direction and the scene's own world bounds, in the same
    /// row-major, row-vector, OpenGL clip-depth conventions the page publishes.
    /// </summary>
    private static byte[] CreateShadow(uint descriptorCount = 1) =>
        CreateShadow(
            descriptorCount,
            LightTiltX,
            0f,
            new Vector3(0, 0, ReceiverDepth),
            new Vector3(0, 0, CasterDepth),
            normalBiasTexels: 1.5);

    private static byte[] CreateShadow(
        uint descriptorCount,
        float tiltX,
        float tiltY,
        Vector3 boundsMinimum,
        Vector3 boundsMaximum,
        double normalBiasTexels)
    {
        const int descriptorSize = 288;
        var bytes = new byte[24 + (descriptorCount * descriptorSize)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Shadow);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), descriptorCount);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(12),
            descriptorCount == 0 ? 0u : 1u);

        // The projection covers the whole scene bound exactly as the producer
        // derives it: the receiver always spans x and y in [-1, 1], and the caller
        // supplies the rest, so the map is fitted rather than guessed.
        var minimum = new Vector3(
            Math.Min(-1f, boundsMinimum.X),
            Math.Min(-1f, boundsMinimum.Y),
            Math.Min(boundsMinimum.Z, boundsMaximum.Z));
        var maximum = new Vector3(
            Math.Max(1f, boundsMaximum.X),
            Math.Max(1f, boundsMaximum.Y),
            Math.Max(boundsMinimum.Z, boundsMaximum.Z));
        Vector3 center = (minimum + maximum) * 0.5f;
        Vector3 half = (maximum - minimum) * 0.5f;
        double radius = Math.Sqrt(
            (half.X * half.X) + (half.Y * half.Y) + (half.Z * half.Z));
        BuildLightSpace(
            LightDirection(tiltX, tiltY),
            center,
            radius,
            out double[] view,
            out double[] projection);
        for (uint index = 0; index < descriptorCount; index++)
        {
            int entry = 24 + ((int)index * descriptorSize);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry), 0u);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 4), index);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 8), 1024u);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 12), 1u);
            for (int element = 0; element < 16; element++)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(
                    bytes.AsSpan(entry + 16 + (element * 8)),
                    view[element]);
                BinaryPrimitives.WriteDoubleLittleEndian(
                    bytes.AsSpan(entry + 144 + (element * 8)),
                    projection[element]);
            }

            // One 1024-texel map covers the whole scene, so a depth texel is about
            // 1/1024 of the normalized range and a texel is about 2 * radius / 1024
            // world units across.
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(entry + 272),
                1.5f / 1024f);
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(entry + 276),
                (float)(normalBiasTexels * 2.0 * radius / 1024.0));
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 280), 1f);
        }
        return bytes;
    }

    /// <summary>
    /// Derives the row-major, row-vector light-space view and orthographic
    /// projection of a directional light covering a bounding sphere.
    /// </summary>
    private static void BuildLightSpace(
        Vector3 direction,
        Vector3 center,
        double radius,
        out double[] view,
        out double[] projection)
    {
        Vector3 zAxis = Vector3.Normalize(direction);
        Vector3 up = Math.Abs(zAxis.Y) > 0.99f
            ? new Vector3(1, 0, 0)
            : new Vector3(0, 1, 0);
        Vector3 xAxis = Vector3.Normalize(Vector3.Cross(up, zAxis));
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

    private static byte[] CreateLightLink(
        uint lightCount,
        params (string Path, int InstanceIndex, uint LightMask, uint ShadowMask)[] entries) =>
        CreateLightLink(lightCount, domeCount: 0, entries);

    private static byte[] CreateLightLink(
        uint lightCount,
        uint domeCount,
        params (string Path, int InstanceIndex, uint LightMask, uint ShadowMask)[] entries)
    {
        uint allDomes = domeCount >= 32 ? uint.MaxValue : (1u << (int)domeCount) - 1;
        List<byte> payload =
        [
            .. BitConverter.GetBytes((uint)entries.Length),
            .. BitConverter.GetBytes(lightCount),
            .. BitConverter.GetBytes((uint)SilkLightLinkUnsupportedFeatures.None),
            .. BitConverter.GetBytes(domeCount),
        ];
        foreach ((string path, int instanceIndex, uint lightMask, uint shadowMask) in entries)
        {
            byte[] pathBytes = Encoding.UTF8.GetBytes(path);
            payload.AddRange(BitConverter.GetBytes(lightMask));
            payload.AddRange(BitConverter.GetBytes(shadowMask));
            payload.AddRange(BitConverter.GetBytes(allDomes));
            payload.AddRange(BitConverter.GetBytes(instanceIndex));
            payload.AddRange(BitConverter.GetBytes((uint)pathBytes.Length));
            payload.AddRange(pathBytes);
        }

        var bytes = new byte[8 + payload.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.LightLink);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        payload.CopyTo(bytes, 8);
        return bytes;
    }
}
