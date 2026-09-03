// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Covers the prefiltered image-based environment a textured
/// <c>UsdLuxDomeLight</c> resolves to, and the mean-radiance ambient fallback
/// every dome that cannot reach it keeps.
/// </summary>
/// <remarks>
/// <para>
/// Every case here is analytic. The environments are constructed so their
/// convolutions have closed forms or unambiguous inequalities -- a constant sky
/// whose cosine convolution is exactly <c>pi</c> times its radiance, a
/// hemisphere that must light one pole and not the other, a single bright
/// column that must be reflected in its own direction and nowhere else -- so a
/// failure names the property that broke rather than the pixels that moved.
/// </para>
/// <para>
/// The two properties worth stating up front are the ones the previous
/// mean-radiance-only implementation could not have: the response depends on the
/// shading direction, and it depends on the dome's authored orientation. Both
/// are asserted directly, and the orientation case is the one that would catch a
/// bake and a sample that disagreed by a rotation -- a scene that looks
/// plausibly lit with its sun in the wrong place.
/// </para>
/// </remarks>
public sealed class SilkEnvironmentLightingTests
{
    private const string DomePath = "/World/Lights/Dome";
    private const string TexturePath = "/assets/studio.hdr";

    /// <summary>
    /// The ambient a unit white dome resolves to, matching Storm and the
    /// untextured dome term hdSilk already publishes.
    /// </summary>
    private const float UnitDomeAmbient = 0.96f;

    [Test]
    public async Task AConstantEnvironmentConvolvesToExactlyPiTimesItsRadiance()
    {
        // The identity the shader's Lambert normalization depends on. The diffuse
        // lobe already carries its 1/pi, so a uniform sky must arrive here as
        // pi * L for the uniform-sky result to come out exactly right rather than
        // approximately right.
        SilkEnvironmentMaps maps = Build(Source(Constant(16, 8, 1f)));

        float expected = MathF.PI * UnitDomeAmbient;
        foreach (Vector3 direction in Directions())
        {
            await Assert.That(maps.SampleIrradiance(direction).X)
                .IsEqualTo(expected)
                .Within(expected * 0.01f)
                .Because($"The irradiance in {direction} must be pi times the sky radiance.");
        }
    }

    [Test]
    public async Task AHemisphereLightsTheNormalFacingItAndNotTheOneAwayFromIt()
    {
        // The whole point of the slice: a sky that is bright above and dark below
        // must light an up-facing normal and leave a down-facing one nearly dark.
        // A mean-radiance ambient term gives both the same value, so this case
        // cannot pass without a real directional convolution.
        SilkEnvironmentMaps maps = Build(Source(Hemisphere(32, 16, 4f)));

        Vector3 up = maps.SampleIrradiance(Vector3.UnitY);
        Vector3 down = maps.SampleIrradiance(-Vector3.UnitY);
        Vector3 horizon = maps.SampleIrradiance(Vector3.UnitX);

        await Assert.That(up.X).IsGreaterThan(down.X * 10f);
        await Assert.That(horizon.X).IsGreaterThan(down.X);
        await Assert.That(horizon.X).IsLessThan(up.X);

        // A closed form to hold the magnitude to: the irradiance at the pole of a
        // uniform upper hemisphere of radiance L is exactly pi * L.
        await Assert.That(up.X)
            .IsEqualTo(MathF.PI * 4f * UnitDomeAmbient)
            .Within(MathF.PI * 4f * UnitDomeAmbient * 0.03f);

        // And the irradiance on the horizon is exactly half of it, because the
        // cosine-weighted hemisphere it integrates is half lit.
        await Assert.That(horizon.X).IsEqualTo(up.X * 0.5f).Within(up.X * 0.05f);
    }

    [Test]
    public async Task RotatingTheDomeRotatesTheEnvironmentItLightsWith()
    {
        // The authored light-to-world orientation is applied while the image is
        // resampled into world space, so a dome rotated a quarter turn about Y
        // must move its bright column a quarter turn with it. If this ever starts
        // failing, the bake and the sample have drifted into two conventions and
        // the scene is lit from the wrong direction.
        SilkDecodedImage image = Column(32, 16, azimuthColumn: 0, value: 8f);
        SilkEnvironmentMaps upright = Build(Source(image, specular: 1f));
        SilkEnvironmentMaps rotated = Build(
            Source(image, QuarterTurnAboutY(), specular: 1f));

        // The direction column 0 of a 32-wide latlong actually covers, taken from
        // the mapping rather than restated. Pinning the convention is a separate
        // case; hard-coding an axis here would make this one fail for the wrong
        // reason if the convention ever moved.
        Vector3 authored = SilkEnvironmentLatLong.Unproject(0.5 / 32, 0.5);
        Vector3 turned = QuarterTurn(authored);

        await Assert.That(upright.SampleSpecularSlice(authored, 0).X)
            .IsGreaterThan(upright.SampleSpecularSlice(turned, 0).X * 5f);
        await Assert.That(rotated.SampleSpecularSlice(turned, 0).X)
            .IsGreaterThan(rotated.SampleSpecularSlice(authored, 0).X * 5f);

        // The rotated dome's response in the turned direction must match the
        // upright dome's response in the authored one: a rotation moves the sky,
        // it does not change how much of it there is.
        await Assert.That(rotated.SampleSpecularSlice(turned, 0).X)
            .IsEqualTo(upright.SampleSpecularSlice(authored, 0).X)
            .Within(upright.SampleSpecularSlice(authored, 0).X * 0.2f);
    }

    [Test]
    public async Task TheDiffuseResponseFollowsTheRotatedDomeToo()
    {
        // The specular case above proves the radiance base is oriented; this one
        // proves the cosine convolution built from it inherits that orientation
        // rather than being rebuilt from an unrotated copy.
        SilkDecodedImage image = Column(32, 16, azimuthColumn: 0, value: 8f);
        SilkEnvironmentMaps upright = Build(Source(image));
        SilkEnvironmentMaps rotated = Build(Source(image, QuarterTurnAboutY()));
        Vector3 authored = SilkEnvironmentLatLong.Unproject(0.5 / 32, 0.5);
        Vector3 turned = QuarterTurn(authored);

        await Assert.That(upright.SampleIrradiance(authored).X)
            .IsGreaterThan(upright.SampleIrradiance(turned).X);
        await Assert.That(rotated.SampleIrradiance(turned).X)
            .IsGreaterThan(rotated.SampleIrradiance(authored).X);
        await Assert.That(rotated.SampleIrradiance(turned).X)
            .IsEqualTo(upright.SampleIrradiance(authored).X)
            .Within(upright.SampleIrradiance(authored).X * 0.1f);
    }

    [Test]
    public async Task TheSharpestSpecularLevelReflectsTheMirrorDirection()
    {
        // Level 0 is roughness 0, so it must be the radiance base itself: a
        // bright column reflects in its own direction and nowhere else.
        SilkEnvironmentMaps maps = Build(
            Source(Column(32, 16, azimuthColumn: 8, value: 16f), specular: 1f));

        Vector3 bright = SilkEnvironmentLatLong.Unproject((8 + 0.5) / 32, 0.5);
        Vector3 away = -bright;

        await Assert.That(maps.SampleSpecularSlice(bright, 0).X)
            .IsGreaterThan(maps.SampleSpecularSlice(away, 0).X * 20f);
    }

    [Test]
    public async Task IncreasingRoughnessBroadensTheSpecularLobeMonotonically()
    {
        // The physical property a roughness-indexed chain exists to express: a
        // rougher surface gathers from a wider cone, so the peak falls and the
        // direction facing away from the light rises. A chain that was merely a
        // box-filtered mip pyramid would show the same trend, which is why the
        // constant-environment case below pins the normalization too.
        SilkEnvironmentMaps maps = Build(
            Source(Column(32, 16, azimuthColumn: 8, value: 16f), specular: 1f));
        Vector3 bright = SilkEnvironmentLatLong.Unproject((8 + 0.5) / 32, 0.5);
        Vector3 away = -bright;

        float previousPeak = float.MaxValue;
        float previousAway = -1f;
        // Every slice is compared, not a prefix of them. Every slice shares one
        // angular resolution, so the roughest is a genuine hemisphere-wide
        // integration rather than a two-texel image whose reconstruction says
        // more about where the sample landed than about the kernel -- which is
        // exactly the property a collapsing mip chain does not have and this
        // representation exists to provide.
        for (uint slice = 0; slice < maps.SpecularSliceCount; slice++)
        {
            float peak = maps.SampleSpecularSlice(bright, slice).X;
            float behind = maps.SampleSpecularSlice(away, slice).X;
            await Assert.That(peak)
                .IsLessThanOrEqualTo(previousPeak * 1.001f)
                .Because($"Slice {slice} must not sharpen the lobe.");
            await Assert.That(behind)
                .IsGreaterThanOrEqualTo(previousAway)
                .Because($"Slice {slice} must not narrow the lobe behind the light.");
            previousPeak = peak;
            previousAway = behind;
        }

        await Assert.That(maps.SpecularSliceCount).IsGreaterThan(3u);
        await Assert.That(previousAway).IsGreaterThan(0f);
        await Assert.That(previousPeak).IsLessThan(
            maps.SampleSpecularSlice(bright, 0).X);

        // The roughest slice integrates a lobe wide enough to cover a whole
        // hemisphere, so the peak has to have fallen by most of its value. It is
        // deliberately *not* asserted to be isotropic: a GGX lobe at roughness 1
        // is the cosine-weighted hemisphere, not the sphere, so a direction facing
        // away from a single bright column still sees almost none of it. The
        // identity below is the precise statement of what it is instead.
        uint roughest = maps.SpecularSliceCount - 1;
        await Assert.That(maps.GetSliceRoughness(roughest)).IsEqualTo(1f);
        await Assert.That(maps.SampleSpecularSlice(bright, roughest).X).IsLessThan(
            maps.SampleSpecularSlice(bright, 0).X * 0.25f);

        // And the roughest slice must agree with the cosine convolution: a GGX
        // lobe at roughness 1 is the cosine-weighted hemisphere, so the
        // prefiltered radiance in a direction is exactly the irradiance in that
        // direction divided by pi. That identity is what "a hemispherical
        // integration" means, and a two-texel trailing mip could not satisfy it at
        // all -- which is the whole reason the roughness axis is a slice stack.
        //
        // The irradiance map is built at the slice resolution here so the two are
        // reconstructed from the same lattice: at the shipped 32x16 the identity
        // still holds, but to a few percent that is reconstruction error rather
        // than integration error, and a tolerance wide enough to absorb it would
        // be wide enough to hide a real one.
        var matched = new SilkEnvironmentPrefilterOptions
        {
            IrradianceWidth = SilkEnvironmentPrefilterOptions.DefaultRadianceWidth,
        };
        SilkEnvironmentMaps balanced = SilkEnvironmentPrefilter.Build(
            [Source(Column(32, 16, azimuthColumn: 8, value: 16f), diffuse: 1f, specular: 1f)],
            matched);
        uint roughestSlice = balanced.SpecularSliceCount - 1;
        foreach (Vector3 direction in Directions())
        {
            float prefiltered = balanced.SampleSpecularSlice(direction, roughestSlice).X;
            float fromIrradiance = balanced.SampleIrradiance(direction).X / MathF.PI;
            await Assert.That(prefiltered)
                .IsEqualTo(fromIrradiance)
                .Within(Math.Max(fromIrradiance * 0.02f, 0.005f))
                .Because(
                    $"Roughness 1 in {direction} must be the cosine-weighted " +
                    "hemisphere the irradiance map already integrates.");
        }
    }

    [Test]
    public async Task AConstantEnvironmentPrefiltersToItselfAtEveryRoughness()
    {
        // The prefilter weights are normalized by their own sum, so a uniform sky
        // must survive every level unchanged. Without that normalization the
        // roughest levels lose most of their energy and a rough metal goes black
        // under a sky that is obviously not.
        SilkEnvironmentMaps maps = Build(Source(Constant(16, 8, 2f), specular: 1f));

        float expected = 2f * UnitDomeAmbient;
        for (uint level = 0; level < maps.SpecularSliceCount; level++)
        {
            foreach (Vector3 direction in Directions())
            {
                await Assert.That(maps.SampleSpecularSlice(direction, level).X)
                    .IsEqualTo(expected)
                    .Within(expected * 0.02f)
                    .Because($"Level {level} in {direction} must preserve a uniform sky.");
            }
        }
    }

    [Test]
    public async Task TheAuthoredEmissionControlsScaleTheTwoMapsIndependently()
    {
        // colour, intensity and exposure scale both maps; diffuse scales only the
        // irradiance and specular only the prefiltered chain. A dome that authors
        // one and not the other must therefore light without reflecting, or
        // reflect without lighting.
        SilkEnvironmentMaps diffuseOnly = Build(
            Source(Constant(16, 8, 1f), diffuse: 1f, specular: 0f));
        SilkEnvironmentMaps specularOnly = Build(
            Source(Constant(16, 8, 1f), diffuse: 0f, specular: 1f));
        SilkEnvironmentMaps scaled = Build(Source(
            Constant(16, 8, 1f),
            color: new Vector3(1f, 0.5f, 0.25f),
            intensity: 3f,
            exposure: 1f,
            diffuse: 0.5f,
            specular: 0.5f));

        await Assert.That(diffuseOnly.SampleIrradiance(Vector3.UnitY).X).IsGreaterThan(1f);
        await Assert.That(diffuseOnly.SampleSpecularSlice(Vector3.UnitY, 0).X).IsEqualTo(0f);
        await Assert.That(specularOnly.SampleIrradiance(Vector3.UnitY).X).IsEqualTo(0f);
        await Assert.That(specularOnly.SampleSpecularSlice(Vector3.UnitY, 0).X)
            .IsGreaterThan(0.9f);

        // color * intensity * 2^exposure * diffuse * the unit-dome normalization,
        // times pi for the cosine convolution.
        float scale = UnitDomeAmbient * 3f * 2f * 0.5f;
        Vector3 irradiance = scaled.SampleIrradiance(Vector3.UnitY);
        await Assert.That(irradiance.X)
            .IsEqualTo(MathF.PI * scale)
            .Within(MathF.PI * scale * 0.02f);
        await Assert.That(irradiance.Y).IsEqualTo(irradiance.X * 0.5f).Within(0.02f);
        await Assert.That(irradiance.Z).IsEqualTo(irradiance.X * 0.25f).Within(0.02f);

        Vector3 radiance = scaled.SampleSpecularSlice(Vector3.UnitY, 0);
        await Assert.That(radiance.X).IsEqualTo(scale).Within(scale * 0.02f);
    }

    [Test]
    public async Task SeveralDomesComposeIntoOneEnvironmentByAddition()
    {
        // The bake is a sum in world space, so composing domes is exact rather
        // than an approximation of a per-dome loop nothing runs.
        SilkEnvironmentMaps single = Build(Source(Constant(16, 8, 1f)));
        SilkEnvironmentMaps both = Build(
            Source(Constant(16, 8, 1f)),
            Source(Constant(16, 8, 3f)));

        await Assert.That(both.DomeCount).IsEqualTo(2);
        await Assert.That(both.SampleIrradiance(Vector3.UnitY).X)
            .IsEqualTo(single.SampleIrradiance(Vector3.UnitY).X * 4f)
            .Within(single.SampleIrradiance(Vector3.UnitY).X * 0.02f);
    }

    [Test]
    public async Task TwoDomesFacingOppositeWaysLightOppositeHemispheres()
    {
        // Composition has to honour each dome's own orientation, not one shared
        // one. Two identical hemispheres, one turned upside down, must produce a
        // sky that is bright everywhere rather than bright on one side.
        SilkDecodedImage hemisphere = Hemisphere(32, 16, 4f);
        SilkEnvironmentMaps maps = Build(
            Source(hemisphere),
            Source(hemisphere, HalfTurnAboutX()));

        Vector3 up = maps.SampleIrradiance(Vector3.UnitY);
        Vector3 down = maps.SampleIrradiance(-Vector3.UnitY);
        await Assert.That(up.X).IsEqualTo(down.X).Within(up.X * 0.05f);
        await Assert.That(up.X).IsGreaterThan(1f);
    }

    [Test]
    public async Task AnEightBitEnvironmentIsLinearizedAndAFloatEnvironmentIsNot()
    {
        // The same resolution the mean-radiance path performs, restated here
        // because the prefilter reads the texels itself rather than through it.
        SilkEnvironmentMaps srgb = Build(new SilkEnvironmentSource(
            ConstantBytes(8, 4, 188),
            SilkColorSpace.Srgb,
            Matrix4x4.Identity,
            new Vector3(UnitDomeAmbient),
            Vector3.Zero));
        SilkEnvironmentMaps raw = Build(new SilkEnvironmentSource(
            ConstantBytes(8, 4, 188),
            SilkColorSpace.Raw,
            Matrix4x4.Identity,
            new Vector3(UnitDomeAmbient),
            Vector3.Zero));

        await Assert.That(srgb.SampleIrradiance(Vector3.UnitY).X)
            .IsEqualTo(raw.SampleIrradiance(Vector3.UnitY).X * SrgbToLinearRatio(188))
            .Within(0.02f);

        // Non-vacuity: the two must actually differ, and by the amount the sRGB
        // transfer function says rather than by any amount at all.
        await Assert.That(SrgbToLinearRatio(188)).IsLessThan(0.7f);
        await Assert.That(SrgbToLinearRatio(188)).IsGreaterThan(0.6f);
    }

    private static float SrgbToLinearRatio(byte encoded)
    {
        float value = encoded / 255f;
        float linear = value <= 0.04045f
            ? value / 12.92f
            : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
        return linear / value;
    }

    [Test]
    public async Task TheSpecularAtlasIsFixedResolutionSlicesAndCarriesOnlyFiniteTexels()
    {
        // Every slice is the full angular resolution, stacked vertically. That is
        // the shape the shader's slice inset and blend assume, and it is what
        // makes the roughest reflection an integration rather than a texel.
        // Half precision is also where a very bright sun can become an infinity
        // that poisons every filtered neighbourhood, so every texel is checked.
        SilkEnvironmentMaps maps = Build(
            Source(Column(32, 16, azimuthColumn: 4, value: 100000f), specular: 1f));

        await Assert.That(maps.SpecularSliceCount)
            .IsEqualTo(SilkEnvironmentPrefilterOptions.Default.SpecularSliceCount);
        await Assert.That(maps.SpecularSliceHeight)
            .IsEqualTo(SilkEnvironmentPrefilterOptions.Default.RadianceHeight);
        await Assert.That(maps.SpecularAtlasHeight)
            .IsEqualTo(maps.SpecularSliceHeight * maps.SpecularSliceCount);
        await Assert.That(maps.SpecularPixels.Length).IsEqualTo(
            checked((int)(maps.SpecularWidth * maps.SpecularAtlasHeight * 8)));
        await Assert.That(maps.IrradiancePixels.Length)
            .IsEqualTo(checked((int)(maps.IrradianceWidth * maps.IrradianceHeight * 8)));

        // Non-vacuity for the whole representation choice: a mip chain of the
        // same base would end at a single texel, and the slice stack does not.
        await Assert.That(SilkMipChainLayout.GetMaxMipLevelCount(
                maps.SpecularWidth,
                maps.SpecularSliceHeight))
            .IsNotEqualTo(maps.SpecularSliceCount);

        await Assert.That(AllFinite(maps.IrradiancePixels)).IsTrue();
        await Assert.That(AllFinite(maps.SpecularPixels)).IsTrue();

        // The saturating clamp is what keeps a 100000-radiance sun finite in a
        // half, and it must saturate rather than overflow.
        Vector3 bright = SilkEnvironmentLatLong.Unproject((4 + 0.5) / 32, 0.5);
        await Assert.That(maps.SampleSpecularSlice(bright, 0).X)
            .IsEqualTo(65504f)
            .Within(1f);
    }

    [Test]
    public async Task ASourceCoarserThanTheLatticeStillCoversEverySphereBin()
    {
        // The resample scatters source texels into world bins, so a source that
        // is coarser than the lattice leaves bins no texel landed in. Those are
        // gather-filled; without that fill the sky would carry black holes that
        // are invisible at every ordinary resolution and obvious at low ones.
        SilkEnvironmentMaps maps = Build(Source(Constant(4, 2, 5f), specular: 1f));

        float expected = 5f * UnitDomeAmbient;
        for (uint level = 0; level < maps.SpecularSliceCount; level++)
        {
            foreach (Vector3 direction in Directions())
            {
                await Assert.That(maps.SampleSpecularSlice(direction, level).X)
                    .IsGreaterThan(0f)
                    .Because($"Bin {direction} at level {level} was never filled.");
            }
        }

        await Assert.That(maps.SampleIrradiance(Vector3.UnitY).X)
            .IsEqualTo(MathF.PI * expected)
            .Within(MathF.PI * expected * 0.05f);
    }

    [Test]
    public async Task TheLatLongMappingIsUsdsOwnAndNotMerelySelfConsistent()
    {
        // The external reference points, restated from
        // pxr/imaging/hdSt/shaders/domeLight.glslfx:
        //
        //     u = (atan(z, x) + 0.5 * pi) / (2 * pi)
        //     v = acos(y) / pi
        //
        // A round trip through the projection and its own inverse cannot see a
        // convention error at all -- a mapping rotated by any constant round-trips
        // perfectly -- so the gate is against these fixed directions, in both
        // directions, and the round trip is only a supporting check.
        (Vector3 Direction, float U, float V)[] reference =
        [
            (-Vector3.UnitZ, 0.00f, 0.5f),
            (Vector3.UnitX, 0.25f, 0.5f),
            (Vector3.UnitZ, 0.50f, 0.5f),
            (-Vector3.UnitX, 0.75f, 0.5f),
            (Vector3.UnitY, 0.25f, 0.0f),
            (-Vector3.UnitY, 0.25f, 1.0f),
        ];

        foreach ((Vector3 direction, float u, float v) in reference)
        {
            Vector2 projected = SilkEnvironmentLatLong.Project(direction);
            await Assert.That(projected.Y)
                .IsEqualTo(v)
                .Within(1e-4f)
                .Because($"v for {direction} must match USD's acos(y)/pi.");
            if (v is > 0.001f and < 0.999f)
            {
                // Longitude is undefined at the poles, so u is only compared away
                // from them.
                await Assert.That(projected.X)
                    .IsEqualTo(u)
                    .Within(1e-4f)
                    .Because($"u for {direction} must match USD's (atan2(z,x)+pi/2)/2pi.");
                Vector3 unprojected = SilkEnvironmentLatLong.Unproject(u, v);
                await Assert.That(Vector3.Distance(unprojected, direction))
                    .IsLessThan(1e-4f)
                    .Because($"({u}, {v}) must unproject to {direction}.");
            }
        }

        foreach (Vector3 direction in Directions())
        {
            Vector2 uv = SilkEnvironmentLatLong.Project(direction);
            Vector3 recovered = SilkEnvironmentLatLong.Unproject(uv.X, uv.Y);
            await Assert.That(Vector3.Distance(direction, recovered)).IsLessThan(1e-4f);
        }
    }

    [Test]
    public async Task TheEnvironmentBrdfTableConvergesAndStaysInsideItsEnergyBounds()
    {
        // The table replaced an analytic fit, so what has to be gated is that it
        // is a *converged numerical integral* rather than another approximation:
        // recomputing an entry at four times the sample count must not move it.
        byte[] table = SilkEnvironmentBrdf.Pixels;
        await Assert.That((uint)table.Length)
            .IsEqualTo(SilkEnvironmentBrdf.Size * SilkEnvironmentBrdf.Size * 8);
        await Assert.That(SilkEnvironmentBrdf.ByteSize).IsEqualTo((ulong)table.Length);

        foreach ((double cosine, double roughness) in BrdfProbes())
        {
            Vector2 shipped = SilkEnvironmentBrdf.Integrate(
                cosine,
                roughness,
                SilkEnvironmentBrdf.SampleCount);
            Vector2 refined = SilkEnvironmentBrdf.Integrate(
                cosine,
                roughness,
                SilkEnvironmentBrdf.SampleCount * 4);
            await Assert.That(Math.Abs(shipped.X - refined.X))
                .IsLessThan(0.01f)
                .Because($"A ({cosine}, {roughness}) must be converged.");
            await Assert.That(Math.Abs(shipped.Y - refined.Y))
                .IsLessThan(0.01f)
                .Because($"B ({cosine}, {roughness}) must be converged.");
        }

        // Non-negativity and energy conservation over the *whole* domain, read
        // out of the packed table the way the shader's clamped sampler reads it.
        for (int row = 0; row <= 32; row++)
        {
            float roughness = row / 32f;
            for (int column = 0; column <= 32; column++)
            {
                float cosine = column / 32f;
                Vector2 term = SilkEnvironmentBrdf.Sample(
                    table,
                    SilkEnvironmentBrdf.Size,
                    cosine,
                    roughness);
                await Assert.That(term.X)
                    .IsGreaterThanOrEqualTo(0f)
                    .Because($"A must be non-negative at ({cosine}, {roughness}).");
                await Assert.That(term.Y)
                    .IsGreaterThanOrEqualTo(0f)
                    .Because($"B must be non-negative at ({cosine}, {roughness}).");
                await Assert.That(term.X + term.Y)
                    .IsLessThanOrEqualTo(1.02f)
                    .Because(
                        $"A + B is a directional albedo and cannot exceed one at " +
                        $"({cosine}, {roughness}).");
            }
        }
    }

    [Test]
    public async Task TheEnvironmentBrdfIsTheSameLobeTheDirectLightingEvaluates()
    {
        // The table is integrated from the shader's own GGX distribution and
        // Smith geometry term, so those are restated here against their closed
        // forms. If either drifts from the checked fragment, the environment and
        // the direct lobe stop being the same material.
        await Assert.That(SilkEnvironmentBrdf.Geometry(0.5, 1.0, 1.0))
            .IsEqualTo(1.0)
            .Within(1e-9);
        await Assert.That(SilkEnvironmentBrdf.Geometry(1.0, 1.0, 1.0))
            .IsEqualTo(1.0)
            .Within(1e-9);
        // k = alpha/2 = roughness^2/2; at n.l = n.v = 0.5 and roughness 1 the
        // Schlick-GGX term is (0.5 / (0.5*0.5 + 0.5))^2.
        double expected = Math.Pow(0.5 / ((0.5 * 0.5) + 0.5), 2);
        await Assert.That(SilkEnvironmentBrdf.Geometry(1.0, 0.5, 0.5))
            .IsEqualTo(expected)
            .Within(1e-9);
        // The distribution is the normalized GGX, so at n.h = 1 it is exactly
        // 1 / pi at roughness 1: alphaSquared over pi * (alphaSquared)^2 with
        // alphaSquared = 1. Storm's additive numerator epsilon is deliberately
        // gone -- 0.001 is comparable to alphaSquared for anything smooth, and it
        // left the lobe unnormalized, which is exactly what stopped the table's
        // importance-sampled estimator from cancelling the distribution exactly.
        await Assert.That(SilkEnvironmentBrdf.Distribution(1.0, 1.0))
            .IsEqualTo(1.0 / Math.PI)
            .Within(1e-9);

        // Normalization is the property that matters, and it is checked as one:
        // the GGX distribution integrates to 1 over the projected hemisphere at
        // every roughness. An additive numerator epsilon fails this at every
        // roughness, and fails it by 60% at roughness 0.2.
        foreach (double roughness in new[] { 0.02, 0.05, 0.1, 0.2, 0.5, 1.0 })
        {
            await Assert.That(IntegrateDistribution(roughness))
                .IsEqualTo(1.0)
                .Within(2e-3);
        }

        // A smooth surface at normal incidence reflects almost all of F0 and
        // almost none of F90, which is the qualitative shape a fit gets right in
        // the middle and wrong at the edges.
        Vector2 mirror = SilkEnvironmentBrdf.Sample(
            SilkEnvironmentBrdf.Pixels,
            SilkEnvironmentBrdf.Size,
            1f,
            0f);
        await Assert.That(mirror.X).IsGreaterThan(0.9f);
        await Assert.That(mirror.Y).IsLessThan(0.1f);

        // A rough surface reflects materially less of F0 than a smooth one, which
        // is the energy the geometry term removes.
        Vector2 rough = SilkEnvironmentBrdf.Sample(
            SilkEnvironmentBrdf.Pixels,
            SilkEnvironmentBrdf.Size,
            1f,
            1f);
        await Assert.That(rough.X).IsLessThan(mirror.X * 0.9f);
    }

    [Test]
    public async Task TheBrdfTableIntegratesTheSameLobeByAnIndependentQuadrature()
    {
        // The table is produced by GGX importance sampling, which cancels the
        // distribution against the sampling density analytically. That
        // cancellation is only exact if the distribution is normalized, so a
        // wrong distribution would still produce a smooth, plausible-looking
        // table -- it would simply be a table of the wrong BRDF.
        //
        // This integrates the same split-sum quantity by brute-force spherical
        // quadrature over the hemisphere, evaluating the fragment's specular
        // lobe directly rather than the estimator, and requires the two to agree.
        // The two share only SpecularLobe, so a distribution that drifted from
        // the shader would move both and be caught by the normalization gate
        // above, while an estimator that drifted from the distribution moves only
        // one and is caught here.
        foreach ((double cosine, double roughness) in new[]
        {
            (0.2, 0.3),
            (0.5, 0.35),
            (0.85, 0.6),
            (1.0, 0.9),
            (0.5, 0.5),
        })
        {
            (double scale, double bias) = QuadratureBrdf(cosine, roughness);
            Vector2 sampled = SilkEnvironmentBrdf.Integrate(cosine, roughness, 8192);
            await Assert.That((double)sampled.X).IsEqualTo(scale).Within(0.02);
            await Assert.That((double)sampled.Y).IsEqualTo(bias).Within(0.02);
        }
    }

    /// <summary>
    /// Integrates the split-sum environment BRDF by uniform hemisphere
    /// quadrature over the fragment's own specular lobe.
    /// </summary>
    private static (double Scale, double Bias) QuadratureBrdf(
        double normalDotEye,
        double roughness)
    {
        const int thetaSteps = 512;
        const int phiSteps = 512;
        double viewX = Math.Sqrt(Math.Max(0.0, 1.0 - (normalDotEye * normalDotEye)));
        double viewZ = normalDotEye;
        double scale = 0;
        double bias = 0;
        double dTheta = (Math.PI / 2.0) / thetaSteps;
        double dPhi = (2.0 * Math.PI) / phiSteps;
        for (int t = 0; t < thetaSteps; t++)
        {
            double theta = (t + 0.5) * dTheta;
            double sinTheta = Math.Sin(theta);
            double cosTheta = Math.Cos(theta);
            for (int p = 0; p < phiSteps; p++)
            {
                double phi = (p + 0.5) * dPhi;
                double lightX = sinTheta * Math.Cos(phi);
                double lightY = sinTheta * Math.Sin(phi);
                double lightZ = cosTheta;

                double halfX = lightX + viewX;
                double halfZ = lightZ + viewZ;
                double halfLength = Math.Sqrt(
                    (halfX * halfX) + (lightY * lightY) + (halfZ * halfZ));
                if (halfLength <= 0)
                {
                    continue;
                }
                double normalDotHalf = halfZ / halfLength;
                double eyeDotHalf = ((viewX * halfX) + (viewZ * halfZ)) / halfLength;
                if (normalDotHalf <= 0 || eyeDotHalf <= 0)
                {
                    continue;
                }

                double lobe = SilkEnvironmentBrdf.SpecularLobe(
                    roughness,
                    lightZ,
                    normalDotEye,
                    normalDotHalf);
                double weight = lobe * lightZ * sinTheta * dTheta * dPhi;
                double fresnel = Math.Pow(1.0 - eyeDotHalf, 5.0);
                scale += (1.0 - fresnel) * weight;
                bias += fresnel * weight;
            }
        }
        return (scale, bias);
    }

    /// <summary>
    /// Integrates the GGX distribution over the projected hemisphere, which is
    /// exactly one for a normalized lobe.
    /// </summary>
    private static double IntegrateDistribution(double roughness, int steps = 200000)
    {
        double total = 0;
        double dTheta = (Math.PI / 2.0) / steps;
        for (int index = 0; index < steps; index++)
        {
            double theta = (index + 0.5) * dTheta;
            total += SilkEnvironmentBrdf.Distribution(roughness, Math.Cos(theta)) *
                Math.Cos(theta) * Math.Sin(theta) * dTheta;
        }
        return total * 2.0 * Math.PI;
    }

    [Test]
    public async Task TheDistributionIsStableAtTheSmoothEndOfTheRoughnessAxis()
    {
        // Storm writes the GGX denominator as n2 * (alphaSquared - 1) + 1. It is
        // algebraically n2 * alphaSquared + (1 - n2), and numerically nothing like
        // it: at roughness 0.01, alphaSquared is 1e-8, so alphaSquared - 1 rounds
        // to exactly -1 in single precision and the whole expression cancels to
        // zero at n.h = 1. The lobe then divides by its own guard and returns
        // something on the order of 1e30 -- one texel of a mirror highlight turning
        // into an overflow that survives tone mapping as pure white.
        //
        // Checked in float, deliberately. In double the cancellation is harmless
        // and the bug is invisible, which is exactly why it survived.
        foreach (double roughness in new[] { 0.001, 0.005, 0.01, 0.02, 0.05 })
        {
            double reference = SilkEnvironmentBrdf.Distribution(roughness, 1.0);
            // Below this the float subtraction still keeps some significant
            // digits; above it, none. 0.02 is the last probe where the cancelling
            // grouping is merely inaccurate rather than destroyed.
            bool catastrophic = roughness <= 0.02;
            double alpha = Math.Max(roughness, SilkEnvironmentBrdf.MinimumRoughness);
            alpha *= alpha;
            double peak = 1.0 / (Math.PI * alpha * alpha);

            // The exact peak of a normalized GGX lobe at n.h = 1 is 1 / (pi a^4).
            await Assert.That(reference / peak).IsEqualTo(1.0).Within(1e-9);

            // And the single-precision evaluation of the same grouping agrees,
            // which the cancelling grouping does not.
            await Assert.That((double)StableDistributionSingle((float)roughness, 1f))
                .IsEqualTo(reference)
                .Within(reference * 1e-3);

            // The cancelling grouping is shown to be the problem rather than
            // merely asserted to be. Its failure mode depends on how far the
            // cancellation goes: at roughness 0.05 the subtraction merely loses
            // most of its significant digits, and by roughness 0.01 it loses all
            // of them, the denominator reaches exactly zero, and the guard clamps
            // the peak of a mirror lobe down to a fraction of where it belongs.
            double cancelling = CancellingDistributionSingle((float)roughness, 1f);
            await Assert.That(Math.Abs((cancelling / reference) - 1.0))
                .IsGreaterThan(catastrophic ? 0.15 : 0.0)
                .Because(
                    $"Storm's grouping at roughness {roughness} returned " +
                    $"{cancelling} where the lobe peaks at {reference}.");
        }

        // And at the mirror end the cancellation is total: the denominator is
        // exactly zero in single precision, so the guard decides the answer, and
        // the answer it decides is three million times the peak of the lobe. One
        // texel of a mirror highlight becomes a value that survives tone mapping
        // as pure white and bleeds through every filtered neighbourhood.
        await Assert.That(
                (double)CancellingDistributionSingle(0.001f, 1f) /
                SilkEnvironmentBrdf.Distribution(0.001, 1.0))
            .IsGreaterThan(1e3);

        // Normalization still holds across the smooth end, which is the property
        // the cancellation destroyed.
        foreach (double roughness in new[] { 0.005, 0.01, 0.05 })
        {
            // A lobe this narrow needs a correspondingly fine grid: at roughness
            // 0.005 its half-width is about 1.6e-5 radians, so a grid that
            // resolved the moderate roughnesses samples it four times.
            await Assert.That(IntegrateDistribution(roughness, 8_000_000))
                .IsEqualTo(1.0)
                .Within(5e-3);
        }
    }

    [Test]
    public async Task TheSpecularLobeStaysFiniteAndBoundedAtEveryRoughness()
    {
        // The energy a normalized lobe delivers is bounded even where the lobe
        // itself is enormous, because the peak is narrow in exactly the proportion
        // that it is tall. That is the invariant the cancelling denominator broke:
        // it made the peak unbounded without making it narrower.
        foreach (double roughness in new[] { 0.001, 0.01, 0.1, 0.5, 1.0 })
        {
            double lobe = SilkEnvironmentBrdf.SpecularLobe(roughness, 1.0, 1.0, 1.0);
            await Assert.That(double.IsFinite(lobe)).IsTrue();
            await Assert.That(lobe).IsGreaterThan(0.0);

            // The integrated energy, through the importance-sampled estimator,
            // which resolves a lobe however narrow it is. A mirror's peak is
            // enormous and its energy is not, because the peak is narrow in
            // exactly the proportion that it is tall -- and that is the invariant
            // the cancelling denominator broke: it made the peak unbounded
            // without making it narrower.
            foreach (double cosine in new[] { 0.05, 0.5, 1.0 })
            {
                Vector2 energy = SilkEnvironmentBrdf.Integrate(cosine, roughness, 4096);
                await Assert.That(float.IsFinite(energy.X)).IsTrue();
                await Assert.That(float.IsFinite(energy.Y)).IsTrue();
                await Assert.That(energy.X).IsGreaterThanOrEqualTo(0f);
                await Assert.That(energy.Y).IsGreaterThanOrEqualTo(0f);
                await Assert.That(energy.X + energy.Y).IsLessThanOrEqualTo(1.001f);
            }
        }

        // And by uniform quadrature over the hemisphere, where the lobe is broad
        // enough for a uniform grid to resolve it. Schlick-GGX with k = alpha/2
        // approximates the Smith masking term and is not exactly energy
        // preserving, so the bound is stated rather than derived; what matters is
        // that it is a bound at all.
        foreach (double roughness in new[] { 0.1, 0.3, 0.6, 1.0 })
        {
            double energy = IntegrateSpecularEnergy(roughness);
            await Assert.That(double.IsFinite(energy)).IsTrue();
            await Assert.That(energy).IsGreaterThan(0.05);
            await Assert.That(energy).IsLessThan(1.25);
        }
    }

    /// <summary>
    /// Evaluates the fragment's GGX denominator grouping in single precision.
    /// </summary>
    private static float StableDistributionSingle(float roughness, float normalDotHalf)
    {
        float clamped = MathF.Max(roughness, (float)SilkEnvironmentBrdf.MinimumRoughness);
        float alpha = clamped * clamped;
        float alphaSquared = alpha * alpha;
        float n2 = Math.Clamp(normalDotHalf * normalDotHalf, 0f, 1f);
        float denominator = (n2 * alphaSquared) + (1f - n2);
        denominator = MathF.PI * denominator * denominator;
        return alphaSquared /
            MathF.Max(denominator, (float)SilkEnvironmentBrdf.DenominatorEpsilon);
    }

    /// <summary>
    /// Evaluates Storm's grouping in single precision, for contrast only.
    /// </summary>
    private static float CancellingDistributionSingle(float roughness, float normalDotHalf)
    {
        float clamped = MathF.Max(roughness, (float)SilkEnvironmentBrdf.MinimumRoughness);
        float alpha = clamped * clamped;
        float alphaSquared = alpha * alpha;
        float denominator = (normalDotHalf * normalDotHalf * (alphaSquared - 1f)) + 1f;
        denominator = MathF.PI * denominator * denominator;
        return alphaSquared /
            MathF.Max(denominator, (float)SilkEnvironmentBrdf.DenominatorEpsilon);
    }

    /// <summary>
    /// Integrates the specular lobe against the cosine over the hemisphere, which
    /// a physical BRDF keeps at or below one.
    /// </summary>
    private static double IntegrateSpecularEnergy(double roughness)
    {
        const int thetaSteps = 4096;
        const int phiSteps = 64;
        double total = 0;
        double dTheta = (Math.PI / 2.0) / thetaSteps;
        double dPhi = (2.0 * Math.PI) / phiSteps;
        for (int t = 0; t < thetaSteps; t++)
        {
            double theta = (t + 0.5) * dTheta;
            double cosTheta = Math.Cos(theta);
            double sinTheta = Math.Sin(theta);

            // View along the normal, so the half vector between it and the light
            // has cos(theta_h) = cos(theta / 2).
            double normalDotHalf = Math.Cos(theta * 0.5);
            double lobe = SilkEnvironmentBrdf.SpecularLobe(
                roughness,
                cosTheta,
                1.0,
                normalDotHalf);
            total += lobe * cosTheta * sinTheta * dTheta * dPhi * phiSteps;
        }
        return total;
    }

    private static IEnumerable<(double Cosine, double Roughness)> BrdfProbes() =>
    [
        (0.05, 0.0),
        (0.05, 0.5),
        (0.05, 1.0),
        (0.5, 0.0),
        (0.5, 0.25),
        (0.5, 0.75),
        (1.0, 0.0),
        (1.0, 0.5),
        (1.0, 1.0),
    ];

    [Test]
    public async Task AnOutputShapeOverTheBudgetIsRefusedBeforeAnythingIsAllocated()
    {
        var options = new SilkEnvironmentPrefilterOptions
        {
            RadianceWidth = 256,
            MaximumPrefilteredBytes = 1024,
        };

        await Assert.That(options.Validate)
            .Throws<SilkEnvironmentBudgetExceededException>();
        await Assert.That(() => SilkEnvironmentPrefilter.Build(
                [Source(Constant(8, 4, 1f))],
                options))
            .Throws<SilkEnvironmentBudgetExceededException>();
    }

    [Test]
    public async Task AnUnrepresentableOutputShapeIsRejected()
    {
        // The specular chain is the full mip chain, which only exists for a power
        // of two, and both maps are equirectangular, which fixes the aspect.
        await Assert.That(() => new SilkEnvironmentPrefilterOptions
        {
            RadianceWidth = 48,
        }.Validate()).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new SilkEnvironmentPrefilterOptions
        {
            IrradianceWidth = 4,
        }.Validate()).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new SilkEnvironmentPrefilterOptions
        {
            MaximumDomeLights = 0,
        }.Validate()).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task ComposingMoreDomesThanTheBoundAdmitsIsRefused()
    {
        var options = new SilkEnvironmentPrefilterOptions { MaximumDomeLights = 1 };

        await Assert.That(() => SilkEnvironmentPrefilter.Build(
                [Source(Constant(8, 4, 1f)), Source(Constant(8, 4, 1f))],
                options))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => SilkEnvironmentPrefilter.Build(
                [],
                SilkEnvironmentPrefilterOptions.Default))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task TheDefaultOutputShapeStaysWellInsideItsOwnBudget()
    {
        // Non-vacuity for the budget cases above: the shipped configuration has
        // to be one the budget admits by a wide margin, or the budget is really a
        // limit the product runs against.
        SilkEnvironmentPrefilterOptions options = SilkEnvironmentPrefilterOptions.Default;
        options.Validate();

        await Assert.That(options.PrefilteredByteSize).IsLessThan(1024UL * 1024);
        await Assert.That(options.PrefilteredByteSize).IsGreaterThan(1024UL);
        await Assert.That(options.SpecularSliceCount).IsGreaterThan(3u);
    }

    [Test]
    public async Task TheCacheBuildsOneEnvironmentOncePerIdentity()
    {
        var cache = new SilkEnvironmentLightingCache();
        int builds = 0;
        SilkEnvironmentMaps build0()
        {
            builds++;
            return Build(Source(Constant(8, 4, 1f)));
        }

        SilkEnvironmentMaps first = cache.GetOrBuild("a", build0);
        SilkEnvironmentMaps second = cache.GetOrBuild("a", build0);
        _ = cache.GetOrBuild("b", build0);

        await Assert.That(builds).IsEqualTo(2);
        await Assert.That(ReferenceEquals(first, second)).IsTrue();
        await Assert.That(cache.Count).IsEqualTo(2);
        await Assert.That(cache.Bytes).IsEqualTo(first.ByteSize * 2);
    }

    [Test]
    public async Task TheCacheEvictsTheLeastRecentlyUsedEnvironmentAtItsCapacity()
    {
        var cache = new SilkEnvironmentLightingCache(capacity: 2);
        int builds = 0;
        SilkEnvironmentMaps build0()
        {
            builds++;
            return Build(Source(Constant(8, 4, 1f)));
        }

        _ = cache.GetOrBuild("a", build0);
        _ = cache.GetOrBuild("b", build0);
        _ = cache.GetOrBuild("a", build0);
        _ = cache.GetOrBuild("c", build0);
        _ = cache.GetOrBuild("a", build0);

        await Assert.That(cache.Count).IsEqualTo(2);
        await Assert.That(cache.EvictionCount).IsGreaterThan(0);
        // "a" was touched most recently before each eviction, so it survived and
        // "b" did not.
        await Assert.That(builds).IsEqualTo(3);
    }

    [Test]
    public async Task AnEnvironmentOverTheCacheByteBudgetIsRefusedRatherThanRetained()
    {
        var cache = new SilkEnvironmentLightingCache(byteBudget: 16);

        await Assert.That(() => cache.GetOrBuild("a", () => Build(Source(Constant(8, 4, 1f)))))
            .Throws<SilkEnvironmentBudgetExceededException>();
        await Assert.That(cache.Count).IsEqualTo(0);
        await Assert.That(cache.Bytes).IsEqualTo(0UL);
    }

    [Test]
    public async Task ARewrittenTextureFileChangesTheCacheIdentity()
    {
        // The path is unchanged, so nothing else in the identity moves. Without
        // the file stamp an edited HDR would serve the previous sky forever.
        SilkEnvironmentData dome = ReadEnvironment(CreateEnvironmentUpsert(DomePath, TexturePath));
        string before = SilkEnvironmentIdentity.Compose(
            "context",
            [dome],
            [new SilkEnvironmentAssetStamp(1024, 100)],
            SilkEnvironmentPrefilterOptions.Default);
        string after = SilkEnvironmentIdentity.Compose(
            "context",
            [dome],
            [new SilkEnvironmentAssetStamp(1024, 200)],
            SilkEnvironmentPrefilterOptions.Default);
        string resized = SilkEnvironmentIdentity.Compose(
            "context",
            [dome],
            [new SilkEnvironmentAssetStamp(2048, 100)],
            SilkEnvironmentPrefilterOptions.Default);

        await Assert.That(after).IsNotEqualTo(before);
        await Assert.That(resized).IsNotEqualTo(before);
    }

    [Test]
    public async Task TheCacheIdentityNamesTheAssetContextControlsAndOrientation()
    {
        SilkEnvironmentData dome = ReadEnvironment(CreateEnvironmentUpsert(DomePath, TexturePath));
        SilkEnvironmentAssetStamp[] stamp = [new SilkEnvironmentAssetStamp(1, 2)];
        string baseline = Compose("context", dome, stamp);

        await Assert.That(Compose("other", dome, stamp)).IsNotEqualTo(baseline);
        await Assert.That(Compose(
                "context",
                ReadEnvironment(CreateEnvironmentUpsert(DomePath, "/assets/other.hdr")),
                stamp))
            .IsNotEqualTo(baseline);
        await Assert.That(Compose(
                "context",
                ReadEnvironment(CreateEnvironmentUpsert(DomePath, TexturePath, intensity: 2f)),
                stamp))
            .IsNotEqualTo(baseline);
        await Assert.That(Compose(
                "context",
                ReadEnvironment(CreateEnvironmentUpsert(DomePath, TexturePath, specular: 1f)),
                stamp))
            .IsNotEqualTo(baseline);
        await Assert.That(Compose(
                "context",
                ReadEnvironment(CreateEnvironmentUpsert(DomePath, TexturePath, translation: 3d)),
                stamp))
            .IsNotEqualTo(baseline);
        await Assert.That(Compose(
                "context",
                ReadEnvironment(CreateEnvironmentUpsert(
                    DomePath,
                    TexturePath,
                    colorSpace: SilkColorSpace.Raw)),
                stamp))
            .IsNotEqualTo(baseline);

        // Republishing an identical record must be the same identity, or nothing
        // would ever hit.
        await Assert.That(Compose(
                "context",
                ReadEnvironment(CreateEnvironmentUpsert(DomePath, TexturePath)),
                stamp))
            .IsEqualTo(baseline);

        await Assert.That(() => SilkEnvironmentIdentity.Compose(
                "context",
                [dome],
                [],
                SilkEnvironmentPrefilterOptions.Default))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ANonUniformDomeScaleDoesNotSkewTheSkyItOrients()
    {
        // A dome light is infinitely distant, so a scale on its transform cannot
        // resize the sky. Left unnormalized it would rotate the image by a
        // different amount at different latitudes, which is a warp no authored
        // scene asks for.
        SilkDecodedImage image = Hemisphere(32, 16, 4f);
        Matrix4x4 scaled = new(
            3, 0, 0, 0,
            0, 0.25f, 0, 0,
            0, 0, 7, 0,
            11, 13, 17, 1);
        SilkEnvironmentMaps identity = Build(Source(image));
        SilkEnvironmentMaps skewed = Build(Source(image, scaled));

        foreach (Vector3 direction in Directions())
        {
            float expected = identity.SampleIrradiance(direction).X;
            await Assert.That(skewed.SampleIrradiance(direction).X)
                .IsEqualTo(expected)
                .Within(Math.Max(expected * 0.02f, 1e-3f));
        }
    }

    [Test]
    public async Task ADegenerateDomeTransformFallsBackToTheIdentityOrientation()
    {
        SilkDecodedImage image = Hemisphere(16, 8, 2f);
        SilkEnvironmentMaps identity = Build(Source(image));
        SilkEnvironmentMaps degenerate = Build(Source(image, default));

        await Assert.That(degenerate.SampleIrradiance(Vector3.UnitY).X)
            .IsEqualTo(identity.SampleIrradiance(Vector3.UnitY).X)
            .Within(1e-3f);
    }

    [Test]
    public async Task ANonFiniteEnvironmentTexelIsRejectedRatherThanPrefiltered()
    {
        SilkDecodedImage image = CreateFloatImage(
            4,
            2,
            (column, _) => column == 0 ? new Vector3(float.NaN) : Vector3.One);

        await Assert.That(() => Build(Source(image))).Throws<InvalidDataException>();
    }

    [Test]
    public async Task AnEnvironmentImageWhoseBufferDoesNotMatchItsExtentIsRejected()
    {
        var image = new SilkDecodedImage(
            8,
            4,
            new byte[8 * 4 * 4],
            SilkTextureFormat.Rgba32Float);

        await Assert.That(() => Build(Source(image))).Throws<InvalidDataException>();
    }

    private static string Compose(
        string context,
        SilkEnvironmentData dome,
        SilkEnvironmentAssetStamp[] stamps) =>
        SilkEnvironmentIdentity.Compose(
            context,
            [dome],
            stamps,
            SilkEnvironmentPrefilterOptions.Default);

    private static SilkEnvironmentMaps Build(params SilkEnvironmentSource[] sources) =>
        SilkEnvironmentPrefilter.Build(sources, SilkEnvironmentPrefilterOptions.Default);

    private static SilkEnvironmentMaps BuildGrouped(params SilkEnvironmentSource[] sources) =>
        SilkEnvironmentPrefilter.Build(
            sources,
            SilkEnvironmentPrefilterOptions.Default,
            perDomeGroups: true);

    private static SilkEnvironmentSource Source(
        SilkDecodedImage image,
        Matrix4x4? transform = null,
        Vector3? color = null,
        float intensity = 1f,
        float exposure = 0f,
        float diffuse = 1f,
        float specular = 0f)
    {
        Vector3 tint = color ?? Vector3.One;
        float scale = UnitDomeAmbient * intensity * MathF.Pow(2f, exposure);
        return new SilkEnvironmentSource(
            image,
            SilkColorSpace.Raw,
            transform ?? Matrix4x4.Identity,
            tint * (scale * diffuse),
            tint * (scale * specular));
    }

    private static Matrix4x4 QuarterTurnAboutY() =>
        Matrix4x4.CreateRotationY(MathF.PI / 2f);

    private static Matrix4x4 HalfTurnAboutX() =>
        Matrix4x4.CreateRotationX(MathF.PI);

    private static Vector3 QuarterTurn(Vector3 direction) =>
        Vector3.TransformNormal(direction, QuarterTurnAboutY());

    private static IEnumerable<Vector3> Directions() =>
    [
        Vector3.UnitX,
        -Vector3.UnitX,
        Vector3.UnitY,
        -Vector3.UnitY,
        Vector3.UnitZ,
        -Vector3.UnitZ,
        Vector3.Normalize(new Vector3(1f, 1f, 1f)),
        Vector3.Normalize(new Vector3(-1f, 0.3f, 0.7f)),
    ];

    private static bool AllFinite(byte[] pixels)
    {
        ReadOnlySpan<Half> halves = MemoryMarshal.Cast<byte, Half>(pixels);
        foreach (Half value in halves)
        {
            if (!Half.IsFinite(value))
            {
                return false;
            }
        }
        return true;
    }

    private static SilkDecodedImage Constant(uint width, uint height, float value) =>
        CreateFloatImage(width, height, (_, _) => new Vector3(value));

    private static SilkDecodedImage Hemisphere(uint width, uint height, float value) =>
        CreateFloatImage(
            width,
            height,
            (_, row) => row < height / 2 ? new Vector3(value) : Vector3.Zero);

    private static SilkDecodedImage Column(
        uint width,
        uint height,
        int azimuthColumn,
        float value) =>
        CreateFloatImage(
            width,
            height,
            (column, _) => column == azimuthColumn ? new Vector3(value) : Vector3.Zero);

    private static SilkDecodedImage CreateFloatImage(
        uint width,
        uint height,
        Func<int, int, Vector3> texel)
    {
        float[] values = new float[width * height * 4];
        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                Vector3 value = texel(column, row);
                int offset = ((row * (int)width) + column) * 4;
                values[offset] = value.X;
                values[offset + 1] = value.Y;
                values[offset + 2] = value.Z;
                values[offset + 3] = 1f;
            }
        }
        return new SilkDecodedImage(
            width,
            height,
            MemoryMarshal.AsBytes(values.AsSpan()).ToArray(),
            SilkTextureFormat.Rgba32Float);
    }

    private static SilkDecodedImage ConstantBytes(uint width, uint height, byte value)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = value;
            pixels[index + 1] = value;
            pixels[index + 2] = value;
            pixels[index + 3] = 255;
        }
        return new SilkDecodedImage(width, height, pixels, SilkTextureFormat.Rgba8Unorm);
    }

    private static SilkEnvironmentData ReadEnvironment(byte[] page)
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(page, 1, 1);
        return scene.Environments.Values.Single();
    }

    internal static byte[] CreateEnvironmentUpsert(
        string path,
        string texture,
        SilkDomeTextureFormat format = SilkDomeTextureFormat.Latlong,
        SilkColorSpace colorSpace = SilkColorSpace.Auto,
        SilkEnvironmentUnsupportedFeatures unsupported =
            SilkEnvironmentUnsupportedFeatures.None,
        float[]? color = null,
        float intensity = 1f,
        float exposure = 0f,
        float diffuse = 1f,
        float specular = 0f,
        double translation = 0d,
        double[]? transform = null,
        uint domeIndex = SilkEnvironmentUpsertCommand.NoDomeIndex)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        byte[] textureBytes = Encoding.UTF8.GetBytes(texture);
        color ??= [1f, 1f, 1f];
        List<byte> payload = [];
        payload.AddRange(BitConverter.GetBytes(ComputeStableHash(path)));
        payload.AddRange(BitConverter.GetBytes((uint)pathBytes.Length));
        payload.AddRange(BitConverter.GetBytes((uint)textureBytes.Length));
        payload.AddRange(BitConverter.GetBytes((uint)format));
        payload.AddRange(BitConverter.GetBytes((uint)colorSpace));
        payload.AddRange(BitConverter.GetBytes((uint)unsupported));
        payload.AddRange(BitConverter.GetBytes(domeIndex));
        payload.AddRange(BitConverter.GetBytes(color[0]));
        payload.AddRange(BitConverter.GetBytes(color[1]));
        payload.AddRange(BitConverter.GetBytes(color[2]));
        payload.AddRange(BitConverter.GetBytes(intensity));
        payload.AddRange(BitConverter.GetBytes(exposure));
        payload.AddRange(BitConverter.GetBytes(diffuse));
        payload.AddRange(BitConverter.GetBytes(specular));
        payload.AddRange(BitConverter.GetBytes(0u));
        for (int index = 0; index < 16; index++)
        {
            double value = transform is not null
                ? transform[index]
                : index switch
                {
                    12 => translation,
                    _ => index % 5 == 0 ? 1d : 0d
                };
            payload.AddRange(BitConverter.GetBytes(value));
        }
        payload.AddRange(pathBytes);
        payload.AddRange(textureBytes);
        List<byte> command =
        [
            .. BitConverter.GetBytes((uint)SilkCommandType.EnvironmentUpsert),
            .. BitConverter.GetBytes((uint)(payload.Count + 8)),
            .. payload,
        ];
        return [.. command];
    }

    internal static ulong ComputeStableHash(string path)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte value in Encoding.UTF8.GetBytes(path))
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }
        return hash;
    }

    internal static byte[] ReadFrameConstants(ISilkGraphicsBuffer buffer)
    {
        byte[] constants = new byte[1584];
        buffer.ReadbackForTesting(constants);
        return constants;
    }

    internal static Vector3 ReadAmbient(ISilkGraphicsBuffer buffer)
    {
        byte[] constants = ReadFrameConstants(buffer);
        return new Vector3(
            BinaryPrimitives.ReadSingleLittleEndian(constants.AsSpan(208, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(constants.AsSpan(212, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(constants.AsSpan(216, 4)));
    }

    internal static (float Enabled, float SliceCount, float SliceHeight)
        ReadEnvironmentControls(ISilkGraphicsBuffer buffer)
    {
        byte[] constants = ReadFrameConstants(buffer);
        return (
            BinaryPrimitives.ReadSingleLittleEndian(constants.AsSpan(1568, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(constants.AsSpan(1572, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(constants.AsSpan(1576, 4)));
    }

    /// <summary>
    /// Reads the authored-scene-lighting flag, which is what suppresses the
    /// deterministic headlight independently of whether an environment resolved.
    /// </summary>
    internal static float ReadAuthoredSceneLighting(ISilkGraphicsBuffer buffer) =>
        BinaryPrimitives.ReadSingleLittleEndian(
            ReadFrameConstants(buffer).AsSpan(1580, 4));

    /// <summary>Reads the packed frame constants verbatim.</summary>
    internal static byte[] ReadFrameBytes(ISilkGraphicsBuffer buffer) =>
        ReadFrameConstants(buffer);

    /// <summary>
    /// Builds a lighting FRAME command carrying only an ambient term.
    /// </summary>
    /// <remarks>
    /// hdSilk publishes an untextured dome light this way and nowhere else: the
    /// dome's emission is accumulated into the ambient colour, and the ambient
    /// intensity is set to one to record that a dome exists at all. The colour may
    /// be black -- an authored black dome, or one with zero diffuse -- which is
    /// why the intensity is the only evidence of the dome and has to reach the
    /// shader on its own.
    /// </remarks>
    internal static byte[] CreateFrameWithAmbient(
        float red,
        float green,
        float blue,
        float intensity)
    {
        const int lightingSize = 1976;
        const int ambientOffset = 536 + 16 + (8 * 176);
        byte[] bytes = new byte[lightingSize];
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(0, 4),
            (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), (uint)bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8, 4), 160);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12, 4), 128);
        for (int element = 0; element < 16; element++)
        {
            double identity = element % 5 == 0 ? 1 : 0;
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(16 + (element * 8), 8),
                identity);
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(144 + (element * 8), 8),
                identity);
        }
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(ambientOffset, 4), red);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(ambientOffset + 4, 4), green);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(ambientOffset + 8, 4), blue);
        BinaryPrimitives.WriteSingleLittleEndian(
            bytes.AsSpan(ambientOffset + 12, 4),
            intensity);
        return bytes;
    }
}
