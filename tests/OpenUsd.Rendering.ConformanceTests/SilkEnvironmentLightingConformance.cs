// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

/// <summary>
/// Renders a lit quad under a textured <c>UsdLuxDomeLight</c> and requires the
/// prefiltered environment to change the image with the sky's direction, the
/// dome's orientation, the material's roughness and the authored contribution
/// scales.
/// </summary>
/// <remarks>
/// <para>
/// The cases are analytic rather than reference images, and every one of them is
/// a difference between two renders that share everything except the property
/// under test. That is what makes them non-vacuous: a mean-radiance ambient
/// term, which is what this renderer produced before, gives *identical* pixels
/// for every one of these pairs, so none of them can pass without a real
/// directional environment response reaching the fragment stage.
/// </para>
/// <para>
/// The strongest case is the equivalence one. Rotating the dome half a turn must
/// produce the same image as leaving the dome alone and moving the bright half of
/// the sky to the other side of the image. Neither render knows what the other
/// did, and they can only agree if the bake applies the authored light-to-world
/// orientation with the same equirectangular convention the fragment samples
/// with -- which is exactly the discrepancy that would otherwise light a scene
/// plausibly with its sun in the wrong place.
/// </para>
/// <para>
/// The scene carries **no** direct light at all. That is the point of the
/// slice: a textured dome is scene lighting, so a dome-only stage must be lit by
/// the dome and by nothing else. Before this, such a stage fell through to the
/// deterministic headlight and was lit from the camera by a light no author
/// placed, which both hid the environment and made a black pixel ambiguous.
/// Every gate below therefore measures the environment alone.
/// </para>
/// <para>
/// It runs on the D3D12 WARP and Vulkan SwiftShader devices, so the evidence is
/// cross-backend and needs no GPU.
/// </para>
/// </remarks>
internal static class SilkEnvironmentLightingConformance
{
    private const string Quad = "/World/Quad";
    private const string DomePath = "/World/Lights/Dome";
    private const string MirrorMaterial = "/World/Materials/Mirror";
    private const string TexturePath = "/assets/env.hdr";
    private const uint Size = 32;
    private const int SampleX = 16;
    private const int SampleY = 16;
    private const uint EnvironmentWidth = 32;
    private const uint EnvironmentHeight = 16;

    /// <summary>
    /// A dome whose sky is bright on one side must light the quad differently
    /// from one whose sky is bright on the other, and rotating the dome must be
    /// indistinguishable from moving the sky.
    /// </summary>
    internal static async Task ADirectionalSkyLightsTheQuadByDirectionAndOrientation(
        ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        byte[] nearHalf = Render(device, NearHalfSky, Upsert(DomePath, TexturePath));
        byte[] farHalf = Render(device, FarHalfSky, Upsert(DomePath, TexturePath));
        byte[] rotated = Render(
            device,
            NearHalfSky,
            Upsert(DomePath, TexturePath, transform: HalfTurnAboutY()));

        int near = Luminance(nearHalf);
        int far = Luminance(farHalf);
        int turned = Luminance(rotated);

        // Directionality: a mean-radiance ambient term gives these two skies the
        // same mean and therefore the same pixel.
        await Assert.That(Math.Max(near, far))
            .IsGreaterThan(Math.Max(1, Math.Min(near, far) * 2))
            .Because(
                $"A hemispherical sky must light the quad by direction " +
                $"(near {near}, far {far}).");

        // Orientation: the authored light-to-world transform has to reach the
        // bake, and rotating the dome has to be the same as rotating the sky.
        await Assert.That(turned)
            .IsNotEqualTo(near)
            .Because("Rotating the dome must change the image.");
        await Assert.That(Math.Abs(turned - far))
            .IsLessThanOrEqualTo(2)
            .Because(
                $"Turning the dome must equal moving the sky " +
                $"(turned {turned}, moved {far}).");
    }

    /// <summary>
    /// The authored <c>inputs:diffuse</c> and <c>inputs:specular</c> must drive
    /// their own halves of the response, and roughness must change the specular
    /// half.
    /// </summary>
    internal static async Task TheContributionScalesAndRoughnessDriveTheResponse(
        ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        // A metallic surface has no diffuse lobe at all, so a diffuse-only dome
        // leaves it black and a specular-only dome does not. That separates the
        // two halves of the response completely rather than by a margin.
        byte[] diffuseOnly = Render(
            device,
            UniformSky,
            Upsert(DomePath, TexturePath, diffuse: 1f, specular: 0f),
            MirrorMaterialPage(roughness: 0.1f),
            QuadMesh(MirrorMaterial));
        byte[] specularOnly = Render(
            device,
            UniformSky,
            Upsert(DomePath, TexturePath, diffuse: 0f, specular: 1f),
            MirrorMaterialPage(roughness: 0.1f),
            QuadMesh(MirrorMaterial));

        await Assert.That(Luminance(diffuseOnly))
            .IsLessThan(8)
            .Because("A metal has no diffuse lobe, so a diffuse-only dome cannot light it.");
        await Assert.That(Luminance(specularOnly))
            .IsGreaterThan(16)
            .Because("A specular-only dome must reflect off a metal.");

        // Roughness has to change what a metal reflects out of a sky that is not
        // uniform. Under a uniform sky the prefilter is energy preserving by
        // construction, so this case uses a narrow band about the axis the
        // reflection points along: a smooth surface reflects the band's peak, and
        // a rough one spreads it over a lobe that is mostly dark sky.
        byte[] smooth = Render(
            device,
            AxisBandZ,
            Upsert(DomePath, TexturePath, diffuse: 0f, specular: 1f),
            MirrorMaterialPage(roughness: 0.05f),
            QuadMesh(MirrorMaterial));
        byte[] rough = Render(
            device,
            AxisBandZ,
            Upsert(DomePath, TexturePath, diffuse: 0f, specular: 1f),
            MirrorMaterialPage(roughness: 1f),
            QuadMesh(MirrorMaterial));

        await Assert.That(Luminance(smooth))
            .IsGreaterThan(Luminance(rough) + 4)
            .Because(
                $"Roughness must select a different prefiltered slice " +
                $"(smooth {Luminance(smooth)}, rough {Luminance(rough)}).");
    }

    /// <summary>
    /// A textured dome is scene lighting: it must suppress the deterministic
    /// headlight a stage with no light at all falls back to.
    /// </summary>
    internal static async Task ATexturedDomeSuppressesTheDeterministicHeadlight(
        ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        // No dome and no light: the fragment falls back to the deterministic
        // headlight, which lights the quad from the camera.
        byte[] headlit = Render(device, UniformSky, dome: null);

        // A dome whose image is black is still a dome. It contributes nothing,
        // but it is scene lighting, so the headlight must be off and the quad
        // must be black -- which is the only unambiguous way to observe that the
        // fallback was suppressed rather than merely outweighed.
        byte[] domeOnly = Render(device, BlackSky, Upsert(DomePath, TexturePath));

        await Assert.That(Luminance(headlit))
            .IsGreaterThan(32)
            .Because("A stage with no lighting at all must still show the headlight.");
        await Assert.That(Luminance(domeOnly))
            .IsLessThan(4)
            .Because(
                $"A dome-only stage is lit by its dome and nothing else " +
                $"(headlit {Luminance(headlit)}, dome-only {Luminance(domeOnly)}).");

        // Non-vacuity: the same dome with a bright image does light the quad, so
        // the black result above is the absence of the headlight rather than the
        // absence of the environment.
        byte[] bright = Render(device, UniformSky, Upsert(DomePath, TexturePath));
        await Assert.That(Luminance(bright)).IsGreaterThan(16);
    }

    /// <summary>
    /// At <c>n.h = 1</c> the specular lobe must return its own peak rather than
    /// the value its guard would decide, on a real device.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the executed half of the precise-arithmetic contract. The two
    /// denominator groupings differ only where <c>n.h</c> is exactly one in single
    /// precision, and no ordinary scene reaches that: the eye vector varies across
    /// a quad, so the half vector is never exactly the normal. The camera is
    /// therefore pushed a million units back, with the projection scaling that
    /// distance into clip range, until the eye vector rounds to exactly
    /// <c>(0, 0, 1)</c> at every fragment. The light points the same way, so the
    /// half vector is the normal and <c>saturate(dot(n, h))</c> is exactly
    /// <c>1.0f</c>.
    /// </para>
    /// <para>
    /// The light's intensity is tiny for the same reason the camera is far: at
    /// <c>n.h = 1</c> and roughness 0.01 the correct lobe peaks at about
    /// <c>3e7</c>, which saturates an eight-bit target on its own and would say
    /// nothing. Scaling it down by <c>1e-9</c> brings the correct answer into the
    /// middle of the range and leaves the reassociated one -- which is <c>1e22</c>,
    /// fifteen orders of magnitude higher, because its denominator cancels to zero
    /// and divides by the guard -- still saturating.
    /// </para>
    /// </remarks>
    internal static async Task TheSpecularLobeReturnsItsPeakAtExactAlignment(
        ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        const float roughness = 0.01f;
        const float scale = 1e-8f;
        byte[] aligned = Frame(
            distantLight: true,
            lightIntensity: scale,
            eyeDistance: 1_000_000d);
        byte[] mesh = QuadMesh(MirrorMaterial, depth: 0f);

        byte[] image = Render(
            device,
            UniformSky,
            dome: null,
            MirrorMaterialPage(roughness),
            mesh,
            aligned);
        int centre = Luminance(image);

        // alphaSquared is 1e-8, so the peak of the normalized lobe is
        // 1 / (pi * 1e-8) = 3.18e7; the Schlick-GGX geometry term and the
        // 1 / (4 n.l n.v) factor are both one at exact alignment, and Fresnel is
        // one for a white metal. Scaled by 1e-8 that lands near 20 of 255.
        //
        // The reassociated grouping computes 1 * (1e-8 - 1) + 1, which is exactly
        // zero in single precision, and then divides 1e-8 by the 1e-30 guard: 1e22,
        // scaled to 1e14, which saturates. Fourteen orders of magnitude separate
        // the two answers, so the band below is wide rather than tight.
        await Assert.That(centre)
            .IsGreaterThan(8)
            .Because(
                "The lobe must return a measurable value at exact alignment; a " +
                $"guard that clamped its peak down would read as black (got {centre}).");
        await Assert.That(centre)
            .IsLessThan(128)
            .Because(
                "The lobe must return its own peak at exact alignment, not the " +
                "value its guard decides. A denominator reassociated back into " +
                "Storm's cancelling form returns fifteen orders of magnitude more " +
                $"and saturates here (got {centre}).");

        // Non-vacuity, two ways. First: the reading tracks the light linearly, so
        // it is the lobe that is being measured rather than a constant. Tripling
        // the intensity must roughly triple it, which a saturated or clamped value
        // cannot do.
        byte[] brighter = Render(
            device,
            UniformSky,
            dome: null,
            MirrorMaterialPage(roughness),
            mesh,
            Frame(
                distantLight: true,
                lightIntensity: scale * 3f,
                eyeDistance: 1_000_000d));
        int brighterCentre = Luminance(brighter);
        await Assert.That(brighterCentre)
            .IsGreaterThan(centre * 2)
            .Because(
                "Tripling the light must roughly triple an unsaturated highlight " +
                $"({centre} at {scale}, {brighterCentre} at {scale * 3f}).");

        // Second: a roughness where no cancellation is possible -- alphaSquared is
        // 6.5e-3, nowhere near the 6e-8 at which subtracting one loses every
        // significant digit -- spreads the same energy over a far broader lobe and
        // reads lower.
        byte[] broad = Render(
            device,
            UniformSky,
            dome: null,
            MirrorMaterialPage(0.3f),
            mesh,
            aligned);
        await Assert.That(Luminance(broad))
            .IsLessThan(centre)
            .Because(
                "A broader lobe must peak lower than a narrow one at the same " +
                $"alignment (roughness {roughness} gave {centre}, roughness 0.3 " +
                $"gave {Luminance(broad)}).");
    }

    /// <summary>
    /// A dome the prefilter refused, and a dome that contributes nothing, are
    /// still authored scene lighting and still suppress the headlight.
    /// </summary>
    /// <remarks>
    /// The headlight used to be suppressed by the environment being <em>enabled</em>
    /// or by a non-zero ambient term. Both are this renderer's verdict on the
    /// dome, not the author's, and both are zero for a dome that is authored
    /// black, authored specular-only, or fell back -- so exactly the domes that
    /// need the suppression most were the ones that lost it, and the stage
    /// acquired a camera light nobody placed.
    /// </remarks>
    internal static async Task AnUnsupportedDomeSuppressesTheDeterministicHeadlight(
        ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        byte[] headlit = Render(device, UniformSky, dome: null);
        await Assert.That(Luminance(headlit))
            .IsGreaterThan(32)
            .Because("A stage with no lighting at all must still show the headlight.");

        // A mirrored-ball dome is refused by name: the environment never becomes
        // live, and the mean-radiance fallback resolves to the dome's untextured
        // emission rather than to its image. Authoring a zero intensity makes even
        // that term zero, so nothing in the frame constants is non-zero except the
        // authored-dome flag itself.
        byte[] unsupported = Render(
            device,
            UniformSky,
            Upsert(
                DomePath,
                TexturePath,
                SilkDomeTextureFormat.MirroredBall,
                intensity: 0f));
        await Assert.That(Luminance(unsupported))
            .IsLessThan(4)
            .Because(
                "A dome this renderer refused is still a dome the author placed " +
                $"(headlit {Luminance(headlit)}, refused {Luminance(unsupported)}).");

        // And a supported dome authored specular-only against a matte surface:
        // the environment is live, but its diffuse contribution is zero and the
        // quad is rough, so nothing measurable reaches the pixel either.
        byte[] specularOnly = Render(
            device,
            UniformSky,
            Upsert(DomePath, TexturePath, diffuse: 0f, specular: 0f));
        await Assert.That(Luminance(specularOnly))
            .IsLessThan(4)
            .Because(
                "A dome that contributes nothing is still scene lighting " +
                $"(headlit {Luminance(headlit)}, contributionless " +
                $"{Luminance(specularOnly)}).");

        // And an *untextured* dome, which never becomes an environment record at
        // all. hdSilk folds it into the frame ambient and records its existence in
        // the ambient intensity; a dome authored black, or with zero diffuse,
        // leaves that colour at zero, so the intensity is the only evidence the
        // dome exists. The managed writer repurposes the ambient slot's w
        // component as the direct-light count, so without carrying that bit into
        // the environment block it was simply discarded.
        byte[] untextured = Render(
            device,
            UniformSky,
            dome: null,
            frame: Frame(ambientIntensity: 1f));
        await Assert.That(Luminance(untextured))
            .IsLessThan(4)
            .Because(
                "A black untextured dome is still a dome the author placed " +
                $"(headlit {Luminance(headlit)}, untextured {Luminance(untextured)}).");

        // Non-vacuity for that case: the same untextured dome with a non-zero
        // colour does light the quad, so the black result is the absence of the
        // headlight rather than the absence of any path from ambient to pixel.
        byte[] litUntextured = Render(
            device,
            UniformSky,
            dome: null,
            frame: Frame(0.5f, 0.5f, 0.5f, 1f));
        await Assert.That(Luminance(litUntextured)).IsGreaterThan(32);
    }
    /// <summary>
    /// A near-mirror surface, with and without a clearcoat, must produce a
    /// bounded, localized, finite highlight rather than an overflow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Driven by a <em>direct</em> light, deliberately. The environment path reads
    /// the prefiltered atlas and the split-sum table and never evaluates
    /// <c>NormalDistribution</c> at all, so a dome-only scene cannot exercise the
    /// direct lobe at any roughness. The quad sits behind the origin so that the
    /// eye vector and the light direction agree in sign, which is what puts a real
    /// highlight on it: with the quad in front, the object-space shading normal
    /// and the eye vector disagree and the half vector degenerates.
    /// </para>
    /// <para>
    /// What this gates is boundedness and locality: a normalized lobe concentrates
    /// the same energy into a narrower cone as it smooths, so its head-on peak
    /// saturates while everything off-peak gets <em>darker</em>. A lobe that has
    /// lost its normalization floods the quad instead, or collapses to black.
    /// </para>
    /// <para>
    /// It does <b>not</b> gate the single-precision denominator grouping. That
    /// difference appears only where <c>n.h</c> is exactly one in float, which no
    /// rasterized fragment of this quad lands on, and everywhere else the two
    /// groupings agree to well inside one 8-bit code value. It is gated instead by
    /// <c>SilkEnvironmentLightingTests.TheDistributionIsStableAtTheSmoothEndOfTheRoughnessAxis</c>,
    /// which evaluates both groupings in <c>float</c> and shows the cancelling one
    /// returning three million times the peak of the lobe.
    /// </para>
    /// </remarks>
    internal static async Task ANearMirrorStaysBoundedAtEveryRoughness(
        ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        byte[] lit = Frame(distantLight: true);
        byte[] mesh = QuadMesh(MirrorMaterial, depth: -0.5f);

        byte[] moderate = Render(
            device,
            UniformSky,
            dome: null,
            MirrorMaterialPage(0.5f),
            mesh,
            lit);
        int moderateCentre = Luminance(moderate);
        int moderateCorner = Luminance(moderate, 2, 2);
        int moderateLit = LitTexelCount(moderate);

        // Non-vacuity: there is a highlight to measure, and it is local.
        await Assert.That(moderateCentre)
            .IsGreaterThan(32)
            .Because("A mirror under a direct light must show a highlight.");
        await Assert.That(moderateCorner)
            .IsLessThan(8)
            .Because(
                "A specular highlight must be local to the reflection direction " +
                $"(centre {moderateCentre}, corner {moderateCorner}).");

        foreach (float roughness in new[] { 0.2f, 0.05f, 0.02f, 0.01f, 0.001f, 0f })
        {
            byte[] smooth = Render(
                device,
                UniformSky,
                dome: null,
                MirrorMaterialPage(roughness),
                mesh,
                lit);
            int corner = Luminance(smooth, 2, 2);

            // A narrower lobe puts *less* energy here, never more. An unbounded
            // lobe raises the whole quad rather than only its peak, and a lobe
            // that has lost its normalization by dividing through its own guard
            // does exactly that.
            await Assert.That(corner)
                .IsLessThanOrEqualTo(moderateCorner)
                .Because(
                    $"A mirror at roughness {roughness} lit the edge of the quad " +
                    $"at least as brightly as a rough one (roughness 0.5 gave " +
                    $"{moderateCorner}, roughness {roughness} gave {corner}).");
            // The lit area must not grow as the lobe narrows. A normalized lobe
            // covers fewer texels as it sharpens; a lobe that divides through its
            // own guard raises the whole quad and covers more.
            int litTexels = LitTexelCount(smooth);
            await Assert.That(litTexels)
                .IsLessThanOrEqualTo(moderateLit)
                .Because(
                    $"A mirror at roughness {roughness} lit {litTexels} texels " +
                    $"where a rough one lit {moderateLit}; a narrower lobe cannot " +
                    "cover more of the quad.");

            // The same surface with a clearcoat, which evaluates the distribution
            // a second time at the coat's own roughness -- a lobe a material
            // authoring only a smooth base coat would never exercise.
            byte[] coated = Render(
                device,
                UniformSky,
                dome: null,
                MirrorMaterialPage(roughness, clearcoat: 1f, clearcoatRoughness: roughness),
                mesh,
                lit);
            int coatedCorner = Luminance(coated, 2, 2);
            await Assert.That(coatedCorner)
                .IsLessThan(64)
                .Because(
                    $"A clearcoat at roughness {roughness} flooded the edge of the " +
                    $"quad (uncoated {corner}, coated {coatedCorner}).");
            await Assert.That(Luminance(coated))
                .IsGreaterThanOrEqualTo(Luminance(smooth))
                .Because("A clearcoat adds a lobe; it cannot remove energy.");
        }

        // And the lobe has not simply vanished at the smooth end, which is the
        // other way a broken denominator fails: the guard can clamp the peak down
        // as easily as up.
        byte[] narrow = Render(
            device,
            UniformSky,
            dome: null,
            MirrorMaterialPage(0.05f),
            mesh,
            lit);
        await Assert.That(Luminance(narrow))
            .IsGreaterThan(4)
            .Because("A roughness-0.05 mirror must still show its highlight.");
    }

    /// <summary>
    /// The environment is sampled with a world-space shading basis: a rotated prim
    /// must reflect the sky its surface faces, and a non-uniformly scaled one must
    /// be transformed by the inverse transpose rather than by the transform.
    /// </summary>
    internal static async Task TheEnvironmentFollowsRotatedAndScaledPrims(
        ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        // Rotation. A screen-facing quad's normal is along Z; rotating it forty
        // degrees about Y swings it toward X. Under a sky lit only in a band
        // about the X axis the rotated quad must therefore be lit and the
        // unrotated one must not -- which is exactly the difference an
        // object-space normal cannot produce, because it is the same value for
        // both.
        byte[] unrotated = Render(
            device,
            AxisBandX,
            Upsert(DomePath, TexturePath),
            mesh: QuadMesh(transform: Identity()));
        byte[] rotated = Render(
            device,
            AxisBandX,
            Upsert(DomePath, TexturePath),
            mesh: QuadMesh(transform: RotationAboutY(40)));

        await Assert.That(Luminance(rotated))
            .IsGreaterThan(Math.Max(4, Luminance(unrotated) * 3))
            .Because(
                $"A rotated prim must reflect the sky it faces " +
                $"(unrotated {Luminance(unrotated)}, rotated {Luminance(rotated)}).");

        // Non-uniform scale. The quad is tilted forty-five degrees inside its own
        // object space, so its normal has equal X and Z components; scaling X by
        // four then leaves the two candidate transforms pointing almost at right
        // angles to each other. The inverse transpose divides the X component,
        // swinging the normal toward Z; the transform itself would multiply it,
        // swinging the normal toward X. The two skies below therefore give
        // opposite answers, and only one of them can be right.
        byte[] scaledUnderZ = Render(
            device,
            AxisBandZ,
            Upsert(DomePath, TexturePath),
            mesh: TiltedQuadMesh(NonUniformScaleX(4)));
        byte[] scaledUnderX = Render(
            device,
            AxisBandX,
            Upsert(DomePath, TexturePath),
            mesh: TiltedQuadMesh(NonUniformScaleX(4)));

        await Assert.That(Luminance(scaledUnderZ))
            .IsGreaterThan(Math.Max(4, Luminance(scaledUnderX) * 2))
            .Because(
                $"A non-uniformly scaled prim's normal follows the inverse " +
                $"transpose (Z band {Luminance(scaledUnderZ)}, X band " +
                $"{Luminance(scaledUnderX)}).");

        // The control: unscaled, the same tilted quad faces both bands equally,
        // so the pair above measures the scale rather than the tilt.
        byte[] tiltedUnderZ = Render(
            device,
            AxisBandZ,
            Upsert(DomePath, TexturePath),
            mesh: TiltedQuadMesh(Identity()));
        byte[] tiltedUnderX = Render(
            device,
            AxisBandX,
            Upsert(DomePath, TexturePath),
            mesh: TiltedQuadMesh(Identity()));
        await Assert.That(Math.Abs(Luminance(tiltedUnderZ) - Luminance(tiltedUnderX)))
            .IsLessThanOrEqualTo(Math.Max(8, Luminance(tiltedUnderZ) / 3))
            .Because(
                $"An unscaled 45-degree quad faces both bands equally " +
                $"(Z {Luminance(tiltedUnderZ)}, X {Luminance(tiltedUnderX)}).");
    }

    /// <summary>
    /// A generated MaterialX surface is <c>ND_surface_unlit</c>, so its
    /// placeholder must be unlit and must receive no environment response.
    /// </summary>
    internal static async Task AGeneratedUnlitMaterialReceivesNoEnvironment(
        ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        // The generated fragment payloads are empty here, which is the pending or
        // failed state: the checked permutation stands in for it. A lit stand-in
        // would light a surface the author declared unlit, including with the
        // sky, so the two skies below must produce the same pixels.
        byte[] brightSky = Render(
            device,
            UniformSky,
            Upsert(DomePath, TexturePath),
            material: UnlitGeneratedMaterialPage(),
            mesh: QuadMesh(MirrorMaterial, Identity()));
        byte[] darkSky = Render(
            device,
            BlackSky,
            Upsert(DomePath, TexturePath),
            material: UnlitGeneratedMaterialPage(),
            mesh: QuadMesh(MirrorMaterial, Identity()));

        await Assert.That(brightSky.AsSpan().SequenceEqual(darkSky))
            .IsTrue()
            .Because("An unlit surface must not respond to the environment at all.");
        await Assert.That(Luminance(brightSky))
            .IsGreaterThan(0)
            .Because(
                "The unlit placeholder shows its surface colour rather than " +
                "collapsing to black, so this comparison is not vacuous.");

        // And it must not be lit by the headlight either: a shaded stand-in under
        // a dome-only stage would differ from the same prim with no dome, because
        // the dome suppresses the headlight.
        byte[] withoutDome = Render(
            device,
            UniformSky,
            dome: null,
            material: UnlitGeneratedMaterialPage(),
            mesh: QuadMesh(MirrorMaterial, Identity()));
        await Assert.That(withoutDome.AsSpan().SequenceEqual(brightSky))
            .IsTrue()
            .Because("An unlit surface is unlit whether or not the stage has lights.");
    }

    /// <summary>
    /// A dome the prefiltered environment cannot carry must fall back to the
    /// mean-radiance ambient term, which is rotation invariant, and retiring the
    /// dome must reproduce the unlit image exactly.
    /// </summary>
    internal static async Task AnUnsupportedDomeFallsBackAndRetiringItReleasesTheMaps(
        ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        // texture:format = angular is diagnosed rather than integrated as if it
        // were equirectangular, so the dome keeps its mean-radiance ambient term
        // -- and a single colour cannot depend on the dome's orientation.
        byte[] upright = Render(
            device,
            NearHalfSky,
            Upsert(DomePath, TexturePath, format: SilkDomeTextureFormat.Angular));
        byte[] rotated = Render(
            device,
            NearHalfSky,
            Upsert(
                DomePath,
                TexturePath,
                format: SilkDomeTextureFormat.Angular,
                transform: HalfTurnAboutY()));

        await Assert.That(upright.AsSpan().SequenceEqual(rotated))
            .IsTrue()
            .Because("The mean-radiance fallback is a single colour and cannot rotate.");
        await Assert.That(Luminance(upright))
            .IsGreaterThan(0)
            .Because("The fallback must light the scene rather than unlighting it.");

        // The supported dome, by contrast, must not be rotation invariant. Stated
        // here as well as in the directional case above because this is the pair
        // that proves the fallback boundary is where it is claimed to be.
        byte[] supported = Render(device, NearHalfSky, Upsert(DomePath, TexturePath));
        byte[] supportedRotated = Render(
            device,
            NearHalfSky,
            Upsert(DomePath, TexturePath, transform: HalfTurnAboutY()));
        await Assert.That(supported.AsSpan().SequenceEqual(supportedRotated)).IsFalse();

        // Retiring the environment must reproduce the image of a scene that never
        // had one, byte for byte, which is what proves the maps are released and
        // the frame block turned off rather than left bound with stale contents.
        using ISilkGraphicsTexture color = CreateColorTarget(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(
            device,
            GetShaderFormat(device),
            NearHalfSky);

        SilkMeshRendererConformance.Apply(renderer, 1, Frame(), QuadMesh());
        _ = renderer.Render(color, depth);
        byte[] withoutDome = ReadPixels(color);

        SilkMeshRendererConformance.Apply(renderer, 2, Upsert(DomePath, TexturePath));
        _ = renderer.Render(color, depth);
        byte[] withDome = ReadPixels(color);

        SilkMeshRendererConformance.Apply(renderer, 3, Remove(DomePath));
        _ = renderer.Render(color, depth);
        byte[] retired = ReadPixels(color);

        await Assert.That(withDome.AsSpan().SequenceEqual(withoutDome))
            .IsFalse()
            .Because("A textured dome must change the image it lights.");
        await Assert.That(retired.AsSpan().SequenceEqual(withoutDome))
            .IsTrue()
            .Because("Retiring the dome must reproduce the undomed image exactly.");
        await Assert.That(renderer.GpuResources.EnvironmentBinding.Enabled).IsFalse();
    }


    private static byte[] Render(
        ISilkGraphicsDevice device,
        Func<string, bool, SilkDecodedImage> decoder,
        byte[]? dome,
        byte[]? material = null,
        byte[]? mesh = null,
        byte[]? frame = null)
    {
        using ISilkGraphicsTexture color = CreateColorTarget(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device, GetShaderFormat(device), decoder);
        var page = new List<byte[]> { frame ?? Frame() };
        if (material is not null)
        {
            page.Add(material);
        }
        page.Add(mesh ?? QuadMesh(material: material is null ? string.Empty : MirrorMaterial));
        if (dome is not null)
        {
            page.Add(dome);
        }
        SilkMeshRendererConformance.Apply(renderer, 1, [.. page]);
        _ = renderer.Render(color, depth);
        return ReadPixels(color);
    }

    private static ISilkGraphicsTexture CreateColorTarget(ISilkGraphicsDevice device) =>
        device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));

    private static SilkShaderBinaryFormat GetShaderFormat(ISilkGraphicsDevice device) =>
        device.Backend switch
        {
            SilkGraphicsBackend.D3D12 => SilkShaderBinaryFormat.Dxil,
            SilkGraphicsBackend.Metal => SilkShaderBinaryFormat.MetalLibrary,
            _ => SilkShaderBinaryFormat.SpirV
        };

    private static int Luminance(byte[] pixels)
    {
        int offset = ((SampleY * (int)Size) + SampleX) * 4;
        return Math.Max(pixels[offset], Math.Max(pixels[offset + 1], pixels[offset + 2]));
    }

    /// <summary>
    /// The number of texels a highlight measurably reaches.
    /// </summary>
    /// <remarks>
    /// This is the shape of the bound rather than its magnitude. A normalized
    /// specular lobe concentrates the same energy into a narrower cone as it
    /// smooths, so it lights fewer and fewer texels; a lobe that has lost its
    /// normalization raises the whole quad instead, which is visible here however
    /// the eight-bit target clamps its peak.
    /// </remarks>
    private static int LitTexelCount(byte[] pixels)
    {
        int count = 0;
        for (int index = 0; index < pixels.Length; index += 4)
        {
            int value = Math.Max(
                pixels[index],
                Math.Max(pixels[index + 1], pixels[index + 2]));
            if (value > 16)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>Reads one named texel rather than the quad's centre.</summary>
    private static int Luminance(byte[] pixels, int x, int y)
    {
        int offset = ((y * (int)Size) + x) * 4;
        return Math.Max(pixels[offset], Math.Max(pixels[offset + 1], pixels[offset + 2]));
    }

    private static byte[] ReadPixels(ISilkGraphicsTexture color)
    {
        var pixels = new byte[checked((int)(color.Width * color.Height * 4))];
        color.ReadbackForTesting(pixels);
        return pixels;
    }

    /// <summary>
    /// Builds a frame with no direct light and no ambient at all.
    /// </summary>
    /// <remarks>
    /// A dome-only stage. Nothing else lights the quad, so every pixel below is
    /// the environment's own contribution -- and the no-dome baseline the
    /// release case compares against is a genuinely unlit image rather than a
    /// headlit one.
    /// </remarks>
    private static byte[] Frame(
        float ambientRed = 0f,
        float ambientGreen = 0f,
        float ambientBlue = 0f,
        float ambientIntensity = 0f,
        bool distantLight = false,
        double lightDirectionZ = 1d,
        float lightIntensity = 1f,
        double eyeDistance = 0d)
    {
        const int lightingSize = 1976;
        const int ambientOffset = 536 + 16 + (8 * 176);
        var bytes = new byte[lightingSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), checked((int)Size));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), checked((int)Size));
        double[] identity = SilkMeshRendererConformance.Identity();
        double[] view = SilkMeshRendererConformance.Identity();
        double[] projection = SilkMeshRendererConformance.Identity();
        if (eyeDistance > 0)
        {
            // Pushes the camera far enough back that the eye vector is exactly
            // (0, 0, 1) in single precision at every fragment of the quad, while
            // the projection scales that distance back into clip range so the quad
            // still covers the viewport. That is what makes n.h reach exactly one:
            // the half vector between a light along +Z and an eye vector along +Z
            // is +Z, and dot(+Z, +Z) saturates to 1.0f rather than to 1 - epsilon.
            view[14] = -eyeDistance;
            projection[10] = 1.0 / eyeDistance;
            projection[14] = 0.5;
        }
        for (int i = 0; i < 16; i++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(16 + (i * 8)), view[i]);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(144 + (i * 8)), projection[i]);
        }

        if (distantLight)
        {
            // One distant light pointing straight down -Z at the quad, which is
            // what makes the *direct* specular lobe -- and therefore
            // NormalDistribution -- run at all. The environment path never
            // evaluates it: it reads the prefiltered atlas and the split-sum table.
            const int lightCountOffset = 536;
            const int lightTableOffset = 536 + 16;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(lightCountOffset), 1u);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(lightTableOffset), 1u);
            for (int component = 0; component < 3; component++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(
                    bytes.AsSpan(lightTableOffset + 16 + (component * 4)),
                    1f);
            }
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(lightTableOffset + 28),
                lightIntensity);
            double[] lightTransform = SilkMeshRendererConformance.Identity();
            lightTransform[8] = 0;
            lightTransform[9] = 0;
            lightTransform[10] = lightDirectionZ;
            for (int element = 0; element < 16; element++)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(
                    bytes.AsSpan(lightTableOffset + 32 + (element * 8)),
                    lightTransform[element]);
            }
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(lightTableOffset + 164),
                1f);
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(lightTableOffset + 168),
                1f);
        }

        // An untextured dome light reaches hdSilk exactly here: its emission is
        // accumulated into the ambient colour and the ambient intensity records
        // that a dome exists. The colour can be black while the intensity is one.
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(ambientOffset), ambientRed);
        BinaryPrimitives.WriteSingleLittleEndian(
            bytes.AsSpan(ambientOffset + 4),
            ambientGreen);
        BinaryPrimitives.WriteSingleLittleEndian(
            bytes.AsSpan(ambientOffset + 8),
            ambientBlue);
        BinaryPrimitives.WriteSingleLittleEndian(
            bytes.AsSpan(ambientOffset + 12),
            ambientIntensity);
        return bytes;
    }

    private static byte[] QuadMesh(
        string material = "",
        double[]? transform = null,
        float depth = 0.5f)
    {
        byte[] mesh = SilkMeshRendererConformance.CreateMeshCommand(
            1,
            Quad,
            [
                -0.5f, -0.5f, depth,
                 0.5f, -0.5f, depth,
                 0.5f,  0.5f, depth,
                -0.5f,  0.5f, depth,
            ],
            [0, 2, 1, 0, 3, 2],
            0,
            0,
            [1, 1, 1, 1]);
        return FinishMesh(mesh, material, transform);
    }

    /// <summary>
    /// A quad tilted forty-five degrees about Y inside its own object space, so
    /// its normal has equal X and Z components and a non-uniform scale on X moves
    /// it.
    /// </summary>
    private static byte[] TiltedQuadMesh(double[] transform)
    {
        const float half = 0.3f;
        const float offset = half * 0.70710678f;
        byte[] mesh = SilkMeshRendererConformance.CreateMeshCommand(
            1,
            Quad,
            [
                -offset, -0.5f, 0.5f + offset,
                 offset, -0.5f, 0.5f - offset,
                 offset,  0.5f, 0.5f - offset,
                -offset,  0.5f, 0.5f + offset,
            ],
            [0, 2, 1, 0, 3, 2],
            0,
            0,
            [1, 1, 1, 1]);
        return FinishMesh(mesh, string.Empty, transform);
    }

    private static byte[] FinishMesh(byte[] mesh, string material, double[]? transform)
    {
        if (transform is not null)
        {
            // MESH_UPSERT carries the row-major object-to-world transform at
            // offset 80.
            for (int element = 0; element < 16; element++)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(
                    mesh.AsSpan(80 + (element * 8)),
                    transform[element]);
            }
        }
        if (material.Length == 0)
        {
            return mesh;
        }

        byte[] materialBytes = Encoding.UTF8.GetBytes(material);
        Array.Resize(ref mesh, mesh.Length + materialBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(4), (uint)mesh.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(mesh.AsSpan(208), ComputeStableHash(material));
        BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(216), (uint)materialBytes.Length);
        materialBytes.CopyTo(mesh.AsSpan(mesh.Length - materialBytes.Length));
        return mesh;
    }

    private static double[] Identity() => SilkMeshRendererConformance.Identity();

    /// <summary>A row-major rotation about the scene's up axis, in degrees.</summary>
    private static double[] RotationAboutY(double degrees)
    {
        double radians = degrees * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        return
        [
            cos, 0, -sin, 0,
            0, 1, 0, 0,
            sin, 0, cos, 0,
            0, 0, 0, 1,
        ];
    }

    /// <summary>A row-major transform that scales the world X axis only.</summary>
    private static double[] NonUniformScaleX(double scale) =>
    [
        scale, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1,
    ];

    /// <summary>
    /// Builds a fully metallic white UsdPreviewSurface at one roughness.
    /// </summary>
    /// <remarks>
    /// Metallic 1 is what makes the two halves of the environment response
    /// separable: a metal has no diffuse lobe at all, and its specular F0 is its
    /// base colour, so a white metal reflects the prefiltered sky almost
    /// unattenuated instead of at a dielectric's four percent.
    /// </remarks>
    private static byte[] MirrorMaterialPage(
        float roughness,
        float clearcoat = 0f,
        float clearcoatRoughness = 0f)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(MirrorMaterial);
        uint scalarCount = clearcoat > 0f ? 5u : 3u;
        List<byte> payload =
        [
            .. BitConverter.GetBytes(ComputeStableHash(MirrorMaterial)),
            .. BitConverter.GetBytes((uint)pathBytes.Length),
            .. BitConverter.GetBytes((uint)SilkSurfaceKind.PreviewSurface),
            .. BitConverter.GetBytes(scalarCount),
            .. BitConverter.GetBytes(0u),
            .. pathBytes,
            .. BitConverter.GetBytes((uint)SilkMaterialParameter.DiffuseColor),
            .. BitConverter.GetBytes(3u),
            .. BitConverter.GetBytes(1f),
            .. BitConverter.GetBytes(1f),
            .. BitConverter.GetBytes(1f),
            .. BitConverter.GetBytes((uint)SilkMaterialParameter.Metallic),
            .. BitConverter.GetBytes(1u),
            .. BitConverter.GetBytes(1f),
            .. BitConverter.GetBytes((uint)SilkMaterialParameter.Roughness),
            .. BitConverter.GetBytes(1u),
            .. BitConverter.GetBytes(roughness),
        ];
        if (clearcoat > 0f)
        {
            payload.AddRange(
                BitConverter.GetBytes((uint)SilkMaterialParameter.Clearcoat));
            payload.AddRange(BitConverter.GetBytes(1u));
            payload.AddRange(BitConverter.GetBytes(clearcoat));
            payload.AddRange(
                BitConverter.GetBytes((uint)SilkMaterialParameter.ClearcoatRoughness));
            payload.AddRange(BitConverter.GetBytes(1u));
            payload.AddRange(BitConverter.GetBytes(clearcoatRoughness));
        }
        payload.AddRange((byte[])[
            .. BitConverter.GetBytes(0u),
            .. BitConverter.GetBytes(0u),
            .. BitConverter.GetBytes(1f),
            .. BitConverter.GetBytes(0f),
            .. BitConverter.GetBytes(0f),
            .. BitConverter.GetBytes(1f),
            .. BitConverter.GetBytes(0f),
            .. BitConverter.GetBytes(0f),
        ]);

        var bytes = new byte[8 + payload.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MaterialUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        payload.CopyTo(bytes, 8);
        return bytes;
    }

    /// <summary>
    /// Builds a generated MaterialX material with empty payloads, which is the
    /// pending-or-failed state the checked permutation stands in for.
    /// </summary>
    /// <remarks>
    /// In this ABI <c>MATERIALX_GENERATED</c> is produced for exactly one
    /// terminal, <c>ND_surface_unlit</c>: standard-surface and OpenPBR graphs are
    /// routed through the projected path instead. So a generated material is an
    /// unlit surface by construction, and the stand-in has to be unlit too.
    /// </remarks>
    private static byte[] UnlitGeneratedMaterialPage()
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(MirrorMaterial);
        List<byte> payload =
        [
            .. BitConverter.GetBytes(ComputeStableHash(MirrorMaterial)),
            .. BitConverter.GetBytes((uint)pathBytes.Length),
            .. BitConverter.GetBytes((uint)SilkSurfaceKind.MaterialXGenerated),
            .. BitConverter.GetBytes(0u),
            .. BitConverter.GetBytes(0u),
            .. pathBytes,
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

    private static byte[] Upsert(
        string path,
        string texture,
        SilkDomeTextureFormat format = SilkDomeTextureFormat.Latlong,
        float diffuse = 1f,
        float specular = 0f,
        double[]? transform = null,
        float intensity = 1f)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        byte[] textureBytes = Encoding.UTF8.GetBytes(texture);
        double[] matrix = transform ?? SilkMeshRendererConformance.Identity();
        List<byte> payload =
        [
            .. BitConverter.GetBytes(ComputeStableHash(path)),
            .. BitConverter.GetBytes((uint)pathBytes.Length),
            .. BitConverter.GetBytes((uint)textureBytes.Length),
            .. BitConverter.GetBytes((uint)format),
            .. BitConverter.GetBytes((uint)SilkColorSpace.Auto),
            .. BitConverter.GetBytes(0u),

            // These scenes publish no frame dome table, so their records claim no
            // dome bit; a record naming an entry the frame does not carry is
            // refused by the page preflight.
            .. BitConverter.GetBytes(SilkEnvironmentUpsertCommand.NoDomeIndex),
            .. BitConverter.GetBytes(1f),
            .. BitConverter.GetBytes(1f),
            .. BitConverter.GetBytes(1f),
            .. BitConverter.GetBytes(intensity),
            .. BitConverter.GetBytes(0f),
            .. BitConverter.GetBytes(diffuse),
            .. BitConverter.GetBytes(specular),
            .. BitConverter.GetBytes(0u),
        ];
        for (int element = 0; element < 16; element++)
        {
            payload.AddRange(BitConverter.GetBytes(matrix[element]));
        }
        payload.AddRange(pathBytes);
        payload.AddRange(textureBytes);

        var bytes = new byte[8 + payload.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes,
            (uint)SilkCommandType.EnvironmentUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        payload.CopyTo(bytes, 8);
        return bytes;
    }

    private static byte[] Remove(string path)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        List<byte> payload =
        [
            .. BitConverter.GetBytes(ComputeStableHash(path)),
            .. BitConverter.GetBytes((uint)pathBytes.Length),
            .. pathBytes,
        ];
        var bytes = new byte[8 + payload.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes,
            (uint)SilkCommandType.EnvironmentRemove);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        payload.CopyTo(bytes, 8);
        return bytes;
    }

    /// <summary>The dome rotated half a turn about its up axis.</summary>
    private static double[] HalfTurnAboutY() =>
    [
        -1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, -1, 0,
        0, 0, 0, 1,
    ];

    /// <summary>
    /// A sky that is bright only over the half of the sphere the quad faces.
    /// </summary>
    /// <remarks>
    /// The equirectangular convention puts <c>+Z</c> at <c>u = 0.75</c> and
    /// <c>-Z</c> at <c>u = 0.25</c>, so the two halves below are separated by the
    /// image's own midpoint rather than by an offset that would have to be kept
    /// in step with the projection.
    /// </remarks>
    /// <summary>
    /// A sky lit only over the hemisphere on one side of the world Z axis.
    /// </summary>
    /// <remarks>
    /// Split by direction rather than by image column. The two are not the same
    /// thing: the equirectangular convention puts <c>-Z</c> at <c>u = 0</c> and
    /// <c>+Z</c> at <c>u = 0.5</c>, so a column split separates the <c>+X</c>
    /// hemisphere from the <c>-X</c> one -- which a screen-facing quad, whose
    /// normal lies along Z, responds to identically. Splitting by the axis the
    /// quad actually faces is what makes the gate measure anything.
    /// </remarks>
    private static SilkDecodedImage NearHalfSky(string asset, bool srgb) =>
        Hemisphere(Vector3.UnitZ);

    private static SilkDecodedImage FarHalfSky(string asset, bool srgb) =>
        Hemisphere(-Vector3.UnitZ);

    private static SilkDecodedImage Hemisphere(Vector3 axis) =>
        CreateFloatImage((column, row) =>
            Vector3.Dot(
                SilkEnvironmentLatLong.Unproject(
                    column,
                    row,
                    EnvironmentWidth,
                    EnvironmentHeight),
                axis) > 0
                ? new Vector3(0.25f)
                : Vector3.Zero);

    private static SilkDecodedImage UniformSky(string asset, bool srgb) =>
        CreateFloatImage((_, _) => new Vector3(0.3f));

    private static SilkDecodedImage BlackSky(string asset, bool srgb) =>
        CreateFloatImage((_, _) => Vector3.Zero);

    /// <summary>
    /// A sky lit only in a narrow band about the world X axis, in both
    /// directions.
    /// </summary>
    /// <remarks>
    /// Symmetric on purpose. Whether a rasterized face's shading normal ends up
    /// pointing along <c>+n</c> or <c>-n</c> depends on winding and on the
    /// front-face flip, and a one-sided band would make these gates depend on
    /// that; a band that is bright on both sides of the axis measures the axis
    /// the normal lies along and nothing else.
    /// </remarks>
    private static SilkDecodedImage AxisBandX(string asset, bool srgb) =>
        AxisBand(Vector3.UnitX);

    private static SilkDecodedImage AxisBandZ(string asset, bool srgb) =>
        AxisBand(Vector3.UnitZ);

    private static SilkDecodedImage AxisBand(Vector3 axis) =>
        CreateFloatImage((column, row) =>
        {
            Vector3 direction = SilkEnvironmentLatLong.Unproject(
                column,
                row,
                EnvironmentWidth,
                EnvironmentHeight);
            // cos(25 degrees), so the band spans fifty degrees about the axis.
            return Math.Abs(Vector3.Dot(direction, axis)) >= 0.9063f
                ? new Vector3(2f)
                : Vector3.Zero;
        });

    private static SilkDecodedImage CreateFloatImage(Func<int, int, Vector3> texel)
    {
        float[] values = new float[EnvironmentWidth * EnvironmentHeight * 4];
        for (int row = 0; row < EnvironmentHeight; row++)
        {
            for (int column = 0; column < EnvironmentWidth; column++)
            {
                Vector3 value = texel(column, row);
                int offset = ((row * (int)EnvironmentWidth) + column) * 4;
                values[offset] = value.X;
                values[offset + 1] = value.Y;
                values[offset + 2] = value.Z;
                values[offset + 3] = 1f;
            }
        }
        return new SilkDecodedImage(
            EnvironmentWidth,
            EnvironmentHeight,
            MemoryMarshal.AsBytes(values.AsSpan()).ToArray(),
            SilkTextureFormat.Rgba32Float);
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
}
