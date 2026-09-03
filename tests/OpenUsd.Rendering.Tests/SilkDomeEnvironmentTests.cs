// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Covers the ABI 16 dome-light environment record and the mean-radiance ambient
/// fallback resolved from it.
/// </summary>
/// <remarks>
/// <para>
/// Before ABI 16 a <c>UsdLuxDomeLight</c> reached the renderer as a single
/// ambient colour derived from its authored <c>color</c>, <c>intensity</c>,
/// <c>exposure</c> and <c>diffuse</c> inputs, and its <c>texture:file</c> was
/// discarded outright. Every HDR environment in the accepted corpus therefore
/// lit the scene with whatever flat colour happened to be authored beside the
/// image, most often plain white.
/// </para>
/// <para>
/// These tests pin the wire record that carries the texture identity and the
/// authored emission controls, and the single ambient value the decoded image is
/// reduced to when the prefiltered environment cannot carry the dome. The
/// directional response that carries it when it can is gated by
/// <see cref="SilkEnvironmentLightingTests"/> and
/// <see cref="SilkEnvironmentRetentionTests"/>; nothing in *this* file runs the
/// environment step, so every case here exercises the fallback, and the fallback
/// is still one colour for the whole sky.
/// </para>
/// <para>
/// The parity case is the important one: an environment whose image is constant
/// 1.0 must produce exactly the ambient an untextured unit dome already produced,
/// because that is what proves the new path replaced the old approximation
/// rather than stacking a second light on top of it.
/// </para>
/// </remarks>
public sealed class SilkDomeEnvironmentTests
{
    private const string DomePath = "/World/Lights/Dome";
    private const string TexturePath = "/assets/studio.hdr";

    /// <summary>
    /// The ambient a unit white dome resolves to, matching Storm and the
    /// untextured dome term hdSilk already publishes.
    /// </summary>
    private const float UnitDomeAmbient = 0.96f;

    /// <summary>
    /// The stamp these cases resolve with. A fixed value keeps them about the
    /// keying they are testing; the stamp's own role -- invalidating a file
    /// rewritten in place -- is gated by
    /// <see cref="SilkEnvironmentRetentionTests"/>.
    /// </summary>
    private static readonly SilkEnvironmentAssetStamp Stamp = new(1024, 100);

    [Test]
    public async Task EnvironmentUpsertRoundTripsEveryFieldAtItsOwnOffset()
    {
        // Every field is given a value distinct from its neighbours so that an
        // offset error cannot pass by reading an adjacent field and finding the
        // same number there.
        byte[] page = CreateEnvironmentUpsert(
            DomePath,
            TexturePath,
            SilkDomeTextureFormat.Latlong,
            SilkColorSpace.Raw,
            SilkEnvironmentUnsupportedFeatures.PoleAxis,
            color: [0.25f, 0.5f, 0.75f],
            intensity: 3.5f,
            exposure: 1.5f,
            diffuse: 0.875f,
            specular: 0.625f,
            translation: 12.5);

        string path;
        string texture;
        SilkDomeTextureFormat format;
        SilkColorSpace colorSpace;
        SilkEnvironmentUnsupportedFeatures unsupported;
        float red;
        float green;
        float blue;
        float intensity;
        float exposure;
        float diffuse;
        float specular;
        double translationX;
        double diagonal;
        ulong hash;
        {
            using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(
                page,
                1,
                SilkCommandParser.PageAbiVersion);
            _ = commands.MoveNext();
            SilkEnvironmentUpsertCommand command = commands.Current.AsEnvironmentUpsert();
            path = command.Path;
            texture = command.TexturePath;
            format = command.TextureFormat;
            colorSpace = command.SourceColorSpace;
            unsupported = command.UnsupportedFeatures;
            red = command.GetColor(0);
            green = command.GetColor(1);
            blue = command.GetColor(2);
            intensity = command.Intensity;
            exposure = command.Exposure;
            diffuse = command.Diffuse;
            specular = command.Specular;
            translationX = command.GetTransformElement(12);
            diagonal = command.GetTransformElement(0);
            hash = command.StableHash;
        }

        await Assert.That(path).IsEqualTo(DomePath);
        await Assert.That(texture).IsEqualTo(TexturePath);
        await Assert.That(format).IsEqualTo(SilkDomeTextureFormat.Latlong);
        await Assert.That(colorSpace).IsEqualTo(SilkColorSpace.Raw);
        await Assert.That(unsupported)
            .IsEqualTo(SilkEnvironmentUnsupportedFeatures.PoleAxis);
        await Assert.That(red).IsEqualTo(0.25f);
        await Assert.That(green).IsEqualTo(0.5f);
        await Assert.That(blue).IsEqualTo(0.75f);
        await Assert.That(intensity).IsEqualTo(3.5f);
        await Assert.That(exposure).IsEqualTo(1.5f);
        await Assert.That(diffuse).IsEqualTo(0.875f);
        await Assert.That(specular).IsEqualTo(0.625f);
        await Assert.That(translationX).IsEqualTo(12.5d);
        await Assert.That(diagonal).IsEqualTo(1d);
        await Assert.That(hash).IsEqualTo(ComputeStableHash(DomePath));
    }

    [Test]
    public async Task EnvironmentUpsertRejectsAnEmptyTexturePath()
    {
        // An environment record exists only because a dome carries an image. An
        // empty texture describes nothing the frame ambient term does not already
        // carry, so it must be refused rather than retained as an environment
        // that can never resolve.
        byte[] page = CreateEnvironmentUpsert(DomePath, texture: string.Empty);

        await Assert.That(() => ParseEnvironment(page)).Throws<InvalidDataException>();
    }

    [Test]
    public async Task EnvironmentUpsertRejectsASizeThatDisagreesWithItsPathLengths()
    {
        byte[] page = CreateEnvironmentUpsert(DomePath, TexturePath);
        byte[] truncated = page[..^1];
        BinaryPrimitives.WriteUInt32LittleEndian(
            truncated.AsSpan(4, 4),
            (uint)truncated.Length);

        await Assert.That(() => ParseEnvironment(truncated)).Throws<InvalidDataException>();
    }

    [Test]
    public async Task EnvironmentUpsertRejectsAnUnknownTextureFormat()
    {
        byte[] page = CreateEnvironmentUpsert(DomePath, TexturePath);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(8 + 16, 4), 5u);

        await Assert.That(() => ParseEnvironment(page)).Throws<InvalidDataException>();
    }

    [Test]
    public async Task EnvironmentUpsertRejectsAnUnknownSourceColorSpace()
    {
        byte[] page = CreateEnvironmentUpsert(DomePath, TexturePath);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(8 + 20, 4), 3u);

        await Assert.That(() => ParseEnvironment(page)).Throws<InvalidDataException>();
    }

    [Test]
    public async Task RetainedEnvironmentsAreKeyedByPrimPathAndMoveTheEnvironmentRevision()
    {
        var scene = new SilkSceneState();
        ulong initial = scene.EnvironmentRevision;
        _ = scene.Apply(CreateEnvironmentUpsert(DomePath, TexturePath), 1, 1);
        ulong afterUpsert = scene.EnvironmentRevision;
        SilkEnvironmentData retained = scene.Environments[DomePath];

        _ = scene.Apply(CreateEnvironmentRemove(DomePath), 1, 2);
        ulong afterRemoval = scene.EnvironmentRevision;

        await Assert.That(afterUpsert).IsGreaterThan(initial);
        await Assert.That(retained.TexturePath).IsEqualTo(TexturePath);
        await Assert.That(afterRemoval).IsGreaterThan(afterUpsert);
        await Assert.That(scene.Environments).IsEmpty();
    }

    [Test]
    public async Task RetainedEnvironmentRejectsAHashThatDoesNotNameItsPath()
    {
        byte[] page = CreateEnvironmentUpsert(DomePath, TexturePath);
        BinaryPrimitives.WriteUInt64LittleEndian(page.AsSpan(8, 8), 1234ul);
        var scene = new SilkSceneState();

        await Assert.That(() => scene.Apply(page, 1, 1)).Throws<InvalidDataException>();
    }

    [Test]
    public async Task RemovingAnEnvironmentThatWasNeverPublishedLeavesTheRevisionAlone()
    {
        // The revision drives the frame constants re-pack. A removal for a dome
        // this consumer never retained changes nothing, so moving the revision
        // for it would re-pack and re-upload the constants for no reason.
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateEnvironmentRemove(DomePath), 1, 1);

        await Assert.That(scene.EnvironmentRevision).IsEqualTo(0ul);
    }

    [Test]
    public async Task ConstantWhiteEnvironmentResolvesToUnitMeanRadiance()
    {
        // The normalization the whole parity argument rests on: a constant 1.0
        // environment is a unit-white dome, so its mean radiance must be exactly
        // one whatever the solid-angle weights are.
        Vector3 mean = SilkEnvironmentMeanRadiance.ComputeMeanRadiance(
            CreateFloatImage(8, 4, (_, _) => new Vector3(1f, 1f, 1f)),
            SilkColorSpace.Raw);

        await Assert.That(mean.X).IsEqualTo(1f).Within(1e-5f);
        await Assert.That(mean.Y).IsEqualTo(1f).Within(1e-5f);
        await Assert.That(mean.Z).IsEqualTo(1f).Within(1e-5f);
    }

    [Test]
    public async Task MeanRadianceWeightsEachRowByItsSolidAngle()
    {
        // Four rows of an equirectangular image cover very different solid
        // angles: the polar rows are sin(22.5 degrees) wide and the equatorial
        // rows sin(67.5 degrees). An unweighted average would report 0.25 for
        // both cases below, which is exactly the bug this guards against: a
        // bright polar cap would then light the scene as strongly as a bright
        // horizon that covers nearly two and a half times more of the sphere.
        double polar = Math.Sin(Math.PI * 0.5 / 4);
        double equator = Math.Sin(Math.PI * 1.5 / 4);
        double total = 2 * (polar + equator);

        Vector3 polarLit = SilkEnvironmentMeanRadiance.ComputeMeanRadiance(
            CreateFloatImage(6, 4, (_, row) => row == 0 ? Vector3.One : Vector3.Zero),
            SilkColorSpace.Raw);
        Vector3 equatorLit = SilkEnvironmentMeanRadiance.ComputeMeanRadiance(
            CreateFloatImage(6, 4, (_, row) => row == 1 ? Vector3.One : Vector3.Zero),
            SilkColorSpace.Raw);

        await Assert.That(polarLit.X).IsEqualTo((float)(polar / total)).Within(1e-5f);
        await Assert.That(equatorLit.X).IsEqualTo((float)(equator / total)).Within(1e-5f);
        await Assert.That(equatorLit.X).IsGreaterThan(polarLit.X);
    }

    [Test]
    public async Task EachChannelIsAveragedIndependently()
    {
        Vector3 mean = SilkEnvironmentMeanRadiance.ComputeMeanRadiance(
            CreateFloatImage(4, 2, (_, _) => new Vector3(2f, 4f, 8f)),
            SilkColorSpace.Raw);

        await Assert.That(mean.X).IsEqualTo(2f).Within(1e-5f);
        await Assert.That(mean.Y).IsEqualTo(4f).Within(1e-5f);
        await Assert.That(mean.Z).IsEqualTo(8f).Within(1e-5f);
    }

    [Test]
    public async Task AnEightBitEnvironmentIsLinearizedAndAFloatEnvironmentIsNot()
    {
        // The same authored number means different radiance in the two encodings.
        // An eight-bit environment is an sRGB-encoded LDR image; a float one is
        // already linear radiance, which is why an HDR environment is authored as
        // one at all. Reading the first as if it were the second lights every
        // scene noticeably too brightly.
        const byte encodedByte = 188;
        float encoded = encodedByte / 255f;
        float linear = MathF.Pow((encoded + 0.055f) / 1.055f, 2.4f);

        Vector3 ldr = SilkEnvironmentMeanRadiance.ComputeMeanRadiance(
            CreateByteImage(4, 2, encodedByte),
            SilkEnvironmentMeanRadiance.ResolveColorSpace(
                SilkColorSpace.Auto,
                null,
                SilkTextureFormat.Rgba8Unorm));
        Vector3 hdr = SilkEnvironmentMeanRadiance.ComputeMeanRadiance(
            CreateFloatImage(4, 2, (_, _) => new Vector3(encoded, encoded, encoded)),
            SilkEnvironmentMeanRadiance.ResolveColorSpace(
                SilkColorSpace.Auto,
                null,
                SilkTextureFormat.Rgba32Float));

        await Assert.That(ldr.X).IsEqualTo(linear).Within(1e-4f);
        await Assert.That(hdr.X).IsEqualTo(encoded).Within(1e-5f);
    }

    [Test]
    public async Task AnAuthoredColorSpaceOverridesWhatTheDecodedImageSuggests()
    {
        await Assert.That(SilkEnvironmentMeanRadiance.ResolveColorSpace(
                SilkColorSpace.Raw,
                null,
                SilkTextureFormat.Rgba8Unorm))
            .IsEqualTo(SilkColorSpace.Raw);
        await Assert.That(SilkEnvironmentMeanRadiance.ResolveColorSpace(
                SilkColorSpace.Srgb,
                null,
                SilkTextureFormat.Rgba32Float))
            .IsEqualTo(SilkColorSpace.Srgb);
    }

    [Test]
    public async Task ANonFiniteEnvironmentTexelIsRejected()
    {
        SilkDecodedImage image = CreateFloatImage(
            2,
            2,
            (column, row) => column == 1 && row == 1
                ? new Vector3(float.PositiveInfinity, 0f, 0f)
                : Vector3.One);

        await Assert.That(() => SilkEnvironmentMeanRadiance.ComputeMeanRadiance(
                image,
                SilkColorSpace.Raw))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task AnEnvironmentImageWhoseBufferDoesNotMatchItsExtentIsRejected()
    {
        var image = new SilkDecodedImage(4, 4, new byte[16], SilkTextureFormat.Rgba8Unorm);

        await Assert.That(() => SilkEnvironmentMeanRadiance.ComputeMeanRadiance(
                image,
                SilkColorSpace.Raw))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task ResolvingTheSameEnvironmentDecodesItExactlyOnce()
    {
        var cache = new SilkEnvironmentMeanRadianceCache();
        int decodes = 0;
        SilkDecodedImage decode(string asset, bool convert)
        {
            decodes++;
            return CreateFloatImage(4, 2, (_, _) => new Vector3(0.5f, 0.5f, 0.5f));
        }

        Vector3 first = cache.Resolve(TexturePath, SilkColorSpace.Auto, Stamp, decode);
        Vector3 second = cache.Resolve(TexturePath, SilkColorSpace.Auto, Stamp, decode);

        await Assert.That(decodes).IsEqualTo(1);
        await Assert.That(cache.DecodeCount).IsEqualTo(1);
        await Assert.That(second.X).IsEqualTo(first.X);
    }

    [Test]
    public async Task TheSameFileReadInTwoColourSpacesIsCachedSeparately()
    {
        // The mean radiance of one file differs between raw and sRGB, so a cache
        // keyed by path alone would serve whichever reading happened to arrive
        // first to both callers.
        var cache = new SilkEnvironmentMeanRadianceCache();
        static SilkDecodedImage decode(string asset, bool convert) =>
            CreateFloatImage(4, 2, (_, _) => new Vector3(0.5f, 0.5f, 0.5f));

        Vector3 raw = cache.Resolve(TexturePath, SilkColorSpace.Raw, Stamp, decode);
        Vector3 srgb = cache.Resolve(TexturePath, SilkColorSpace.Srgb, Stamp, decode);

        await Assert.That(cache.DecodeCount).IsEqualTo(2);
        await Assert.That(raw.X).IsEqualTo(0.5f).Within(1e-5f);
        await Assert.That(srgb.X).IsLessThan(raw.X);
    }

    [Test]
    public async Task TheCacheEvictsTheLeastRecentlyUsedEnvironmentAtItsCapacity()
    {
        var cache = new SilkEnvironmentMeanRadianceCache(capacity: 2);
        static SilkDecodedImage decode(string asset, bool convert) =>
            CreateFloatImage(2, 2, (_, _) => Vector3.One);

        _ = cache.Resolve("/a.hdr", SilkColorSpace.Raw, Stamp, decode);
        _ = cache.Resolve("/b.hdr", SilkColorSpace.Raw, Stamp, decode);

        // Touch the older entry so the newer one becomes the eviction candidate.
        _ = cache.Resolve("/a.hdr", SilkColorSpace.Raw, Stamp, decode);
        _ = cache.Resolve("/c.hdr", SilkColorSpace.Raw, Stamp, decode);

        // "/a.hdr" must still be resident, so re-resolving it decodes nothing.
        int before = cache.DecodeCount;
        _ = cache.Resolve("/a.hdr", SilkColorSpace.Raw, Stamp, decode);

        await Assert.That(cache.Count).IsLessThanOrEqualTo(2);
        await Assert.That(cache.EvictionCount).IsGreaterThan(0);
        await Assert.That(cache.DecodeCount).IsEqualTo(before);
    }

    [Test]
    public async Task AnEnvironmentOverTheDecodeBudgetIsRefusedRatherThanTraversed()
    {
        var cache = new SilkEnvironmentMeanRadianceCache(decodeByteBudget: 64);
        static SilkDecodedImage decode(string asset, bool convert) =>
            CreateFloatImage(8, 8, (_, _) => Vector3.One);

        await Assert.That(() => cache.Resolve(TexturePath, SilkColorSpace.Raw, Stamp, decode))
            .Throws<SilkEnvironmentBudgetExceededException>();
        await Assert.That(cache.Count).IsEqualTo(0);
    }

    [Test]
    public async Task AnEnvironmentOverTheDecodeBudgetKeepsTheUntexturedEmission()
    {
        using var device = new EnvironmentGraphicsDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => CreateFloatImage(64, 32, (_, _) => Vector3.One),
            udimResolver: null,
            residencyOptions: null,
            environmentDecodeByteBudget: 1024);
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateEnvironmentUpsert(DomePath, TexturePath), 1, 1);

        Vector3 ambient = ReadAmbient(resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f));

        await Assert.That(ambient.X).IsEqualTo(UnitDomeAmbient).Within(1e-5f);
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.EnvironmentBudgetExceeded);
    }

    [Test]
    public async Task AConstantWhiteEnvironmentReproducesTheUntexturedUnitDomeAmbient()
    {
        // The parity case. hdSilk stops folding a textured dome into the frame
        // ambient term and publishes it here instead, so a dome whose image is
        // constant 1.0 has to arrive at exactly the ambient the untextured dome
        // it replaced produced. Anything else means the change moved the
        // exposure of every scene with an environment in it.
        Vector3 ambient = ResolveAmbient(
            CreateEnvironmentUpsert(DomePath, TexturePath),
            (_, _) => CreateFloatImage(8, 4, (_, _) => Vector3.One));

        await Assert.That(ambient.X).IsEqualTo(UnitDomeAmbient).Within(1e-5f);
        await Assert.That(ambient.Y).IsEqualTo(UnitDomeAmbient).Within(1e-5f);
        await Assert.That(ambient.Z).IsEqualTo(UnitDomeAmbient).Within(1e-5f);
    }

    [Test]
    public async Task TheEnvironmentTermScalesWithMeanRadianceColourIntensityExposureAndDiffuse()
    {
        byte[] page = CreateEnvironmentUpsert(
            DomePath,
            TexturePath,
            color: [1f, 0.5f, 0.25f],
            intensity: 2f,
            exposure: 1f,
            diffuse: 0.5f);

        Vector3 ambient = ResolveAmbient(
            page,
            (_, _) => CreateFloatImage(8, 4, (_, _) => new Vector3(3f, 3f, 3f)));

        float expected = UnitDomeAmbient * 2f * 2f * 0.5f * 3f;
        await Assert.That(ambient.X).IsEqualTo(expected).Within(1e-4f);
        await Assert.That(ambient.Y).IsEqualTo(expected * 0.5f).Within(1e-4f);
        await Assert.That(ambient.Z).IsEqualTo(expected * 0.25f).Within(1e-4f);
    }

    [Test]
    public async Task TwoEnvironmentsAccumulateRatherThanReplacingEachOther()
    {
        byte[] first = CreateEnvironmentUpsert("/World/Lights/A", "/a.hdr");
        byte[] second = CreateEnvironmentUpsert("/World/Lights/B", "/b.hdr");
        byte[] page = [.. first, .. second];

        Vector3 ambient = ResolveAmbient(
            page,
            (_, _) => CreateFloatImage(4, 2, (_, _) => Vector3.One),
            commandCount: 2);

        await Assert.That(ambient.X).IsEqualTo(UnitDomeAmbient * 2f).Within(1e-5f);
    }

    [Test]
    public async Task AnUnsupportedMappingKeepsTheUntexturedEmissionAndNamesTheDomePrim()
    {
        // Sampling an angular or mirrored-ball image as if it were
        // equirectangular lights the scene from directions nobody authored, so
        // the untextured emission is used and the prim is named instead.
        byte[] page = CreateEnvironmentUpsert(
            DomePath,
            TexturePath,
            SilkDomeTextureFormat.Angular);

        (Vector3 ambient, IReadOnlyList<RenderDiagnostic> diagnostics) = ResolveAmbientAndDiagnostics(
            page,
            (_, _) => throw new InvalidOperationException("The image must not be decoded."));

        await Assert.That(ambient.X).IsEqualTo(UnitDomeAmbient).Within(1e-5f);
        await Assert.That(diagnostics.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.EnvironmentMappingUnsupported);
        await Assert.That(diagnostics.Any(entry => entry.Message.Contains(DomePath, StringComparison.Ordinal)))
            .IsTrue();
    }

    [Test]
    public async Task AMissingEnvironmentTextureFallsBackToTheUntexturedEmission()
    {
        (Vector3 ambient, IReadOnlyList<RenderDiagnostic> diagnostics) = ResolveAmbientAndDiagnostics(
            CreateEnvironmentUpsert(DomePath, TexturePath),
            (asset, _) => throw new FileNotFoundException("missing", asset));

        // Falling back to the untextured emission restores exactly the result the
        // scene had before the environment record existed, which is a far better
        // failure than unlighting the stage.
        await Assert.That(ambient.X).IsEqualTo(UnitDomeAmbient).Within(1e-5f);
        await Assert.That(diagnostics.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.EnvironmentAssetNotFound);
    }

    [Test]
    public async Task AnUndecodableEnvironmentTextureFallsBackAndIsDiagnosedSeparately()
    {
        (Vector3 ambient, IReadOnlyList<RenderDiagnostic> diagnostics) = ResolveAmbientAndDiagnostics(
            CreateEnvironmentUpsert(DomePath, TexturePath),
            (_, _) => throw new InvalidDataException("corrupt"));

        await Assert.That(ambient.X).IsEqualTo(UnitDomeAmbient).Within(1e-5f);
        await Assert.That(diagnostics.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.EnvironmentDecodeFailed);
    }

    [Test]
    public async Task AnAuthoredSpecularContributionIsNamedWhenTheDomeFallsBack()
    {
        // A dome the prefiltered environment carries resolves its specular
        // contribution, and is silent. This case is the fallback, where the sky
        // has already been collapsed to one colour: approximating a reflection
        // from that constant would put every mirror-like surface at the average
        // colour of the sky, so it is named instead.
        (Vector3 ambient, IReadOnlyList<RenderDiagnostic> diagnostics) = ResolveAmbientAndDiagnostics(
            CreateEnvironmentUpsert(DomePath, TexturePath, specular: 1f),
            (_, _) => CreateFloatImage(4, 2, (_, _) => Vector3.One));

        await Assert.That(ambient.X).IsEqualTo(UnitDomeAmbient).Within(1e-5f);
        await Assert.That(diagnostics.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.EnvironmentSpecularUnsupported);
    }

    [Test]
    public async Task ADomeWithNoAuthoredSpecularIsNotDiagnosed()
    {
        (_, IReadOnlyList<RenderDiagnostic> diagnostics) = ResolveAmbientAndDiagnostics(
            CreateEnvironmentUpsert(DomePath, TexturePath, specular: 0f),
            (_, _) => CreateFloatImage(4, 2, (_, _) => Vector3.One));

        await Assert.That(diagnostics.Select(entry => entry.Code))
            .DoesNotContain(SilkRenderDiagnosticCodes.EnvironmentSpecularUnsupported);
    }

    [Test]
    public async Task AuthoredDomeBehaviourHdSilkDidNotCarryIsNamedAgainstThePrim()
    {
        (_, IReadOnlyList<RenderDiagnostic> diagnostics) = ResolveAmbientAndDiagnostics(
            CreateEnvironmentUpsert(
                DomePath,
                TexturePath,
                unsupported: SilkEnvironmentUnsupportedFeatures.ColorTemperature),
            (_, _) => CreateFloatImage(4, 2, (_, _) => Vector3.One));

        RenderDiagnostic diagnostic = diagnostics.Single(
            entry => entry.Code == SilkRenderDiagnosticCodes.EnvironmentFeatureUnsupported);
        await Assert.That(diagnostic.Message).Contains(DomePath);
        await Assert.That(diagnostic.Message).Contains("ColorTemperature");
    }

    [Test]
    public async Task RemovingTheEnvironmentReturnsTheFrameAmbientToTheDomeItLeftBehind()
    {
        // The frame's own revision does not move when only an environment
        // changed, so without the environment revision the constants would keep
        // the removed dome's contribution until the camera happened to move.
        using var device = new EnvironmentGraphicsDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => CreateFloatImage(4, 2, (_, _) => Vector3.One));
        var scene = new SilkSceneState();

        _ = scene.Apply(CreateEnvironmentUpsert(DomePath, TexturePath), 1, 1);
        Vector3 lit = ReadAmbient(resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f));

        _ = scene.Apply(CreateEnvironmentRemove(DomePath), 1, 2);
        Vector3 unlit = ReadAmbient(resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f));

        await Assert.That(lit.X).IsEqualTo(UnitDomeAmbient).Within(1e-5f);
        await Assert.That(unlit.X).IsEqualTo(0f);
    }

    [Test]
    public async Task AResolvedEnvironmentIsNotReDecodedWhileNothingChanges()
    {
        using var device = new EnvironmentGraphicsDevice();
        int decodes = 0;
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) =>
            {
                decodes++;
                return CreateFloatImage(4, 2, (_, _) => Vector3.One);
            });
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateEnvironmentUpsert(DomePath, TexturePath), 1, 1);

        for (int frame = 0; frame < 5; frame++)
        {
            _ = resources.RequireFrameBuffer(scene, RenderOutputTransform.Identity, 1f);
        }

        await Assert.That(decodes).IsEqualTo(1);
    }

    [Test]
    public async Task ReauthoringTheSameTextureWithNewEmissionRepacksWithoutReDecoding()
    {
        // The two halves of the cache contract, in the case that actually happens
        // while somebody is working: the artist drags the dome's intensity while
        // the same HDR stays bound. The frame constants have to follow the new
        // emission on the very next frame, and the image must not be decoded
        // again -- re-decoding a 4K environment per slider tick is exactly the
        // stall this cache exists to prevent.
        using var device = new EnvironmentGraphicsDevice();
        List<string> decoded = [];
        using var resources = new SilkSceneGpuResources(
            device,
            (asset, _) =>
            {
                decoded.Add(asset);
                return CreateFloatImage(8, 4, (_, _) => new Vector3(2f, 2f, 2f));
            });
        var scene = new SilkSceneState();

        _ = scene.Apply(CreateEnvironmentUpsert(DomePath, TexturePath), 1, 1);
        ulong firstRevision = scene.EnvironmentRevision;
        Vector3 before = ReadAmbient(resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f));

        _ = scene.Apply(
            CreateEnvironmentUpsert(DomePath, TexturePath, intensity: 4f, exposure: 1f),
            1,
            2);
        ulong secondRevision = scene.EnvironmentRevision;
        Vector3 after = ReadAmbient(resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f));

        // The retained record is replaced and the revision moves, which is what
        // makes the constants re-pack at all.
        await Assert.That(secondRevision).IsGreaterThan(firstRevision);
        await Assert.That(scene.Environments[DomePath].Intensity).IsEqualTo(4f);

        // Mean radiance 2.0, so the ambient is 0.96 * 2 * emission.
        await Assert.That(before.X).IsEqualTo(UnitDomeAmbient * 2f).Within(1e-5f);
        await Assert.That(after.X)
            .IsEqualTo(UnitDomeAmbient * 2f * 4f * 2f)
            .Within(1e-4f);

        // One decode, for one texture path, across both revisions.
        await Assert.That(decoded).Count().IsEqualTo(1);
        await Assert.That(decoded[0]).IsEqualTo(TexturePath);
    }

    [Test]
    public async Task ChangingTheTexturePathDecodesTheNewAssetAndFollowsItsRadiance()
    {
        // The complement of the case above. Cache identity is the asset path, so
        // rebinding the dome to a different file must decode that file: serving
        // the first image's mean for the second path would leave the scene lit by
        // an environment that is no longer bound anywhere.
        const string secondTexture = "/assets/sunset.hdr";
        using var device = new EnvironmentGraphicsDevice();
        List<string> decoded = [];
        using var resources = new SilkSceneGpuResources(
            device,
            (asset, _) =>
            {
                decoded.Add(asset);
                float radiance = asset == secondTexture ? 4f : 1f;
                return CreateFloatImage(
                    8,
                    4,
                    (_, _) => new Vector3(radiance, radiance, radiance));
            });
        var scene = new SilkSceneState();

        _ = scene.Apply(CreateEnvironmentUpsert(DomePath, TexturePath), 1, 1);
        Vector3 first = ReadAmbient(resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f));

        _ = scene.Apply(CreateEnvironmentUpsert(DomePath, secondTexture), 1, 2);
        Vector3 second = ReadAmbient(resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f));

        await Assert.That(first.X).IsEqualTo(UnitDomeAmbient).Within(1e-5f);
        await Assert.That(second.X).IsEqualTo(UnitDomeAmbient * 4f).Within(1e-4f);
        await Assert.That(decoded).IsEquivalentTo(new[] { TexturePath, secondTexture });
    }

    [Test]
    public async Task RebindingAPreviouslyResolvedTextureReusesItsCachedMean()
    {
        // Switching a variant back and forth is the ordinary way a stage cycles
        // environments, and the entry is still resident, so the return trip must
        // cost nothing.
        const string secondTexture = "/assets/sunset.hdr";
        using var device = new EnvironmentGraphicsDevice();
        List<string> decoded = [];
        using var resources = new SilkSceneGpuResources(
            device,
            (asset, _) =>
            {
                decoded.Add(asset);
                return CreateFloatImage(4, 2, (_, _) => Vector3.One);
            });
        var scene = new SilkSceneState();

        _ = scene.Apply(CreateEnvironmentUpsert(DomePath, TexturePath), 1, 1);
        _ = resources.RequireFrameBuffer(scene, RenderOutputTransform.Identity, 1f);
        _ = scene.Apply(CreateEnvironmentUpsert(DomePath, secondTexture), 1, 2);
        _ = resources.RequireFrameBuffer(scene, RenderOutputTransform.Identity, 1f);
        _ = scene.Apply(CreateEnvironmentUpsert(DomePath, TexturePath), 1, 3);
        _ = resources.RequireFrameBuffer(scene, RenderOutputTransform.Identity, 1f);

        await Assert.That(decoded).IsEquivalentTo(new[] { TexturePath, secondTexture });
    }

    [Test]
    public async Task RepublishingAnIdenticalEnvironmentStillResolvesToTheSameAmbient()
    {
        // hdSilk only republishes an environment whose published fields changed,
        // but a consumer must not depend on that: an identical record has to be
        // idempotent rather than accumulating a second contribution.
        using var device = new EnvironmentGraphicsDevice();
        int decodes = 0;
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) =>
            {
                decodes++;
                return CreateFloatImage(4, 2, (_, _) => Vector3.One);
            });
        var scene = new SilkSceneState();

        byte[] page = CreateEnvironmentUpsert(DomePath, TexturePath);
        _ = scene.Apply(page, 1, 1);
        Vector3 first = ReadAmbient(resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f));
        _ = scene.Apply(page, 1, 2);
        Vector3 second = ReadAmbient(resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f));

        await Assert.That(scene.Environments).Count().IsEqualTo(1);
        await Assert.That(second.X).IsEqualTo(first.X);
        await Assert.That(decodes).IsEqualTo(1);
    }

    [Test]
    public async Task AFailedEnvironmentIsRetriedAfterItIsReauthored()
    {
        // A missing asset is diagnosed and falls back, and nothing caches that
        // failure, so pointing the dome at a readable file must recover on the
        // next revision instead of staying dark until the process restarts.
        using var device = new EnvironmentGraphicsDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (asset, _) => asset == TexturePath
                ? throw new FileNotFoundException("missing", asset)
                : CreateFloatImage(8, 4, (_, _) => new Vector3(2f, 2f, 2f)));
        var scene = new SilkSceneState();

        _ = scene.Apply(CreateEnvironmentUpsert(DomePath, TexturePath), 1, 1);
        Vector3 failed = ReadAmbient(resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f));
        bool diagnosed = resources.Diagnostics.Entries.Any(
            entry => entry.Code == SilkRenderDiagnosticCodes.EnvironmentAssetNotFound);

        _ = scene.Apply(CreateEnvironmentUpsert(DomePath, "/assets/present.hdr"), 1, 2);
        Vector3 recovered = ReadAmbient(resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f));

        await Assert.That(diagnosed).IsTrue();
        await Assert.That(failed.X).IsEqualTo(UnitDomeAmbient).Within(1e-5f);
        await Assert.That(recovered.X).IsEqualTo(UnitDomeAmbient * 2f).Within(1e-5f);

        // The stale diagnostic is cleared when the revision moves, so the
        // reported state describes the current scene rather than its history.
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .DoesNotContain(SilkRenderDiagnosticCodes.EnvironmentAssetNotFound);
    }

    [Test]
    public async Task TheFallbackAmbientIsTheSameForEveryOrientationOfTheSameImage()
    {
        // Not a feature: a guard on the claim. The mean-radiance fallback is a
        // single colour, and a single colour is rotation invariant, so a rotated
        // dome must fall back to exactly the same ambient. The directional
        // response that *does* read the orientation is gated separately by
        // SilkEnvironmentLightingTests; if this ever starts failing, the fallback
        // has quietly acquired a directionality it does not have and the wording
        // in docs/rendering.md is no longer true.
        Vector3 upright = ResolveAmbient(
            CreateEnvironmentUpsert(DomePath, TexturePath),
            (_, _) => CreateFloatImage(
                8,
                4,
                (_, row) => row < 2 ? new Vector3(4f, 4f, 4f) : Vector3.Zero));
        Vector3 rotated = ResolveAmbient(
            CreateEnvironmentUpsert(DomePath, TexturePath, translation: 7.5),
            (_, _) => CreateFloatImage(
                8,
                4,
                (_, row) => row < 2 ? new Vector3(4f, 4f, 4f) : Vector3.Zero));

        await Assert.That(rotated.X).IsEqualTo(upright.X);
    }

    private static Vector3 ResolveAmbient(
        byte[] page,
        Func<string, bool, SilkDecodedImage> decoder,
        uint commandCount = 1)
    {
        (Vector3 ambient, _) = ResolveAmbientAndDiagnostics(page, decoder, commandCount);
        return ambient;
    }

    private static (Vector3 Ambient, IReadOnlyList<RenderDiagnostic> Diagnostics)
        ResolveAmbientAndDiagnostics(
            byte[] page,
            Func<string, bool, SilkDecodedImage> decoder,
            uint commandCount = 1)
    {
        using var device = new EnvironmentGraphicsDevice();
        using var resources = new SilkSceneGpuResources(device, decoder);
        var scene = new SilkSceneState();
        _ = scene.Apply(page, commandCount, 1);
        ISilkGraphicsBuffer buffer = resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f);
        return (ReadAmbient(buffer), [.. resources.Diagnostics.Entries]);
    }

    private static Vector3 ReadAmbient(ISilkGraphicsBuffer buffer)
    {
        byte[] constants = new byte[1056];
        buffer.ReadbackForTesting(constants);
        return new Vector3(
            BinaryPrimitives.ReadSingleLittleEndian(constants.AsSpan(208, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(constants.AsSpan(212, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(constants.AsSpan(216, 4)));
    }

    private static void ParseEnvironment(byte[] page)
    {
        using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(
            page,
            1,
            SilkCommandParser.PageAbiVersion);
        _ = commands.MoveNext();
        _ = commands.Current.AsEnvironmentUpsert();
    }

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

    private static SilkDecodedImage CreateByteImage(uint width, uint height, byte value)
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

    private static byte[] CreateEnvironmentUpsert(
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

        // A scene that publishes no frame dome table publishes no dome bits, so
        // its records must claim none: a record whose dome index names an entry
        // the frame does not carry is refused by the page preflight.
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
            double value = index switch
            {
                12 => translation,
                _ => index % 5 == 0 ? 1d : 0d
            };
            payload.AddRange(BitConverter.GetBytes(value));
        }
        payload.AddRange(pathBytes);
        payload.AddRange(textureBytes);
        return CreateCommand(SilkCommandType.EnvironmentUpsert, payload);
    }

    private static byte[] CreateEnvironmentRemove(string path)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        List<byte> payload = [];
        payload.AddRange(BitConverter.GetBytes(ComputeStableHash(path)));
        payload.AddRange(BitConverter.GetBytes((uint)pathBytes.Length));
        payload.AddRange(pathBytes);
        return CreateCommand(SilkCommandType.EnvironmentRemove, payload);
    }

    private static byte[] CreateCommand(SilkCommandType type, List<byte> payload)
    {
        List<byte> command =
        [
            .. BitConverter.GetBytes((uint)type),
            .. BitConverter.GetBytes((uint)(payload.Count + 8)),
            .. payload,
        ];
        return [.. command];
    }

    private static ulong ComputeStableHash(string path)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte value in Encoding.UTF8.GetBytes(path))
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }
        return hash;
    }

    private sealed class EnvironmentGraphicsDevice : ISilkGraphicsDevice
    {
        public SilkGraphicsBackend Backend => SilkGraphicsBackend.D3D12;

        public SilkGraphicsCapabilities Capabilities => new(
            "Dome environment test device",
            "test",
            SupportsCompute: false,
            IsSoftware: true);

        public ISilkGraphicsTexture CreateTexture2D(
            uint width,
            uint height,
            SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm) =>
            throw new NotSupportedException();

        public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage) =>
            new EnvironmentBuffer(size, usage);

        public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsShaderModule CreateShaderModule(
            SilkShaderModuleDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsBindingLayout CreateBindingLayout(
            SilkBindingLayoutDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsShaderProgram CreateShaderProgram(
            SilkShaderProgramDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsPipeline CreateGraphicsPipeline(
            SilkGraphicsPipelineDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputeBindingLayout CreateComputeBindingLayout(
            SilkComputeBindingLayoutDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputeShaderProgram CreateComputeShaderProgram(
            SilkComputeShaderProgramDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputePipeline CreateComputePipeline(
            SilkComputePipelineDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsCommandList CreateCommandList() =>
            throw new NotSupportedException();

        public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList) =>
            throw new NotSupportedException();

        public void WaitIdle()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class EnvironmentBuffer(nuint size, SilkBufferUsage usage)
        : ISilkGraphicsBuffer
    {
        private readonly byte[] _bytes = new byte[checked((int)size)];

        public nuint Size => size;

        public SilkBufferUsage Usage => usage;

        public void Write(ReadOnlySpan<byte> data, nuint offset = 0) =>
            data.CopyTo(_bytes.AsSpan(checked((int)offset)));

        public void ReadbackForTesting(Span<byte> destination) =>
            _bytes.AsSpan(0, destination.Length).CopyTo(destination);

        public void Dispose()
        {
        }
    }
}
