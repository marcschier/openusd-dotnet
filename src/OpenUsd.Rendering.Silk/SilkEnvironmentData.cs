// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Runtime.InteropServices;

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// One retained textured <c>UsdLuxDomeLight</c> published by hdSilk.
/// </summary>
/// <remarks>
/// <para>
/// A dome light with no authored texture stays part of the frame ambient term
/// exactly as it always has been. A dome light that carries an image cannot be
/// described by that single colour, so hdSilk publishes it here instead and the
/// renderer prefilters the image into a directional environment response.
/// </para>
/// <para>
/// The authored emission controls arrive unmultiplied. The renderer applies
/// colour, intensity, exposure and both the diffuse and specular contribution
/// scales, and diagnoses the rest against this prim path rather than
/// approximating them. A dome the prefilter cannot accept -- an unsupported
/// mapping, an unreadable asset, a budget, or a device that cannot carry the
/// environment resources -- falls back to the mean-radiance ambient term below,
/// which is exactly the result this renderer produced before the directional
/// response existed.
/// </para>
/// </remarks>
public sealed class SilkEnvironmentData
{
    private readonly double[] _transform;

    private SilkEnvironmentData(
        string path,
        ulong stableHash,
        string texturePath,
        SilkDomeTextureFormat textureFormat,
        SilkColorSpace sourceColorSpace,
        SilkEnvironmentUnsupportedFeatures unsupportedFeatures,
        uint domeIndex,
        Vector3 color,
        float intensity,
        float exposure,
        float diffuse,
        float specular,
        double[] transform)
    {
        Path = path;
        StableHash = stableHash;
        TexturePath = texturePath;
        TextureFormat = textureFormat;
        SourceColorSpace = sourceColorSpace;
        UnsupportedFeatures = unsupportedFeatures;
        DomeIndex = domeIndex;
        Color = color;
        Intensity = intensity;
        Exposure = exposure;
        Diffuse = diffuse;
        Specular = specular;
        _transform = transform;
    }

    /// <summary>Gets the dome light's authoritative USD prim path.</summary>
    public string Path { get; }

    /// <summary>Gets the FNV-1a path hash used only as an identity index.</summary>
    public ulong StableHash { get; }

    /// <summary>Gets the resolved <c>texture:file</c> asset path.</summary>
    public string TexturePath { get; }

    /// <summary>Gets the declared image mapping.</summary>
    public SilkDomeTextureFormat TextureFormat { get; }

    /// <summary>Gets the declared source colour space of the image.</summary>
    public SilkColorSpace SourceColorSpace { get; }

    /// <summary>Gets the authored dome behaviour hdSilk did not put on the wire.</summary>
    public SilkEnvironmentUnsupportedFeatures UnsupportedFeatures { get; }

    /// <summary>
    /// Gets this dome's entry in the frame dome table, which is the bit a
    /// per-prim dome link mask sets for it.
    /// </summary>
    /// <remarks>
    /// <see cref="SilkEnvironmentUpsertCommand.NoDomeIndex"/> when the page
    /// publishes no dome table, in which case the dome lights every prim and no
    /// per-draw mask can address it.
    /// </remarks>
    public uint DomeIndex { get; }

    /// <summary>
    /// Gets whether a per-draw dome mask can address this dome at all.
    /// </summary>
    public bool HasDomeIndex => DomeIndex != SilkEnvironmentUpsertCommand.NoDomeIndex;

    /// <summary>Gets the authored <c>inputs:color</c>.</summary>
    public Vector3 Color { get; }

    /// <summary>Gets the authored <c>inputs:intensity</c>.</summary>
    public float Intensity { get; }

    /// <summary>Gets the authored <c>inputs:exposure</c> in stops.</summary>
    public float Exposure { get; }

    /// <summary>Gets the authored <c>inputs:diffuse</c> contribution scale.</summary>
    public float Diffuse { get; }

    /// <summary>Gets the authored <c>inputs:specular</c> contribution scale.</summary>
    public float Specular { get; }

    /// <summary>Gets the row-major light-to-world transform that orients the image.</summary>
    /// <remarks>
    /// Read by the prefilter that bakes this dome into the shared world-space
    /// environment maps: every source texel's direction is taken through this
    /// transform's rotation before it is accumulated, so a rotated dome produces
    /// different prefiltered bytes rather than the same bytes read differently.
    /// The mean-radiance ambient fallback beside it is rotation invariant by
    /// construction and does not read it, which is what makes the fallback's
    /// orientation independence a property rather than an oversight.
    /// </remarks>
    public ReadOnlyMemory<double> Transform => _transform;

    /// <summary>
    /// Gets whether the declared image mapping is one this renderer resolves at
    /// all, given what can be observed about the image.
    /// </summary>
    /// <param name="description">
    /// The shape the image describer reported, or <see langword="null"/> when the
    /// image could not be described.
    /// </param>
    /// <remarks>
    /// <para>
    /// An explicitly authored <see cref="SilkDomeTextureFormat.Latlong"/> is a
    /// statement by the author and is taken at its word. Every other named
    /// mapping -- <see cref="SilkDomeTextureFormat.MirroredBall"/>,
    /// <see cref="SilkDomeTextureFormat.Angular"/> and
    /// <see cref="SilkDomeTextureFormat.CubeMapVerticalCross"/> -- is refused by
    /// name: each parameterizes the sphere differently, and integrating one as if
    /// it were equirectangular weights the wrong parts of the image.
    /// </para>
    /// <para>
    /// <see cref="SilkDomeTextureFormat.Automatic"/> says the mapping is to be
    /// derived from the image, and this renderer derives it from the one property
    /// the image actually carries: an equirectangular map covers 360 degrees of
    /// longitude by 180 of latitude, so it is exactly twice as wide as it is
    /// tall. An automatic dome whose image is not 2:1 is refused rather than
    /// guessed at, because a square automatic image is far more likely to be a
    /// mirrored ball or an angular map than an equirectangular one with the wrong
    /// aspect, and guessing produces a lit scene whose sky is smeared.
    /// </para>
    /// </remarks>
    internal bool IsMappingSupported(SilkImageDescription? description)
    {
        if (TextureFormat == SilkDomeTextureFormat.Latlong)
        {
            return true;
        }
        if (TextureFormat != SilkDomeTextureFormat.Automatic)
        {
            return false;
        }
        return description is { Width: > 0, Height: > 0 } shape &&
            shape.Width == shape.Height * 2;
    }

    /// <summary>
    /// Gets the authored dome behaviour that invalidates the exact emission and
    /// orientation semantics the prefiltered environment claims.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>enableColorTemperature</c> scales the authored colour by a black-body
    /// tint hdSilk did not put on the wire, so the emission this renderer would
    /// bake is not the emission the dome describes. A non-<c>scene</c>
    /// <c>poleAxis</c> re-parameterizes the sphere, so the light-to-world rotation
    /// the bake applies is not the orientation the dome describes. Both are
    /// claims the directional response makes and cannot honour, so a dome that
    /// carries either falls back to the mean-radiance ambient term, which claims
    /// neither.
    /// </para>
    /// <para>
    /// An authored link collection is <em>not</em> here. A dome resolves to one
    /// scene-wide term under both paths, so the collection is equally
    /// inapplicable to the fallback; falling back would lose the directional
    /// response without making the linking any more correct. It stays a
    /// diagnostic on a dome that is otherwise fully resolved.
    /// </para>
    /// </remarks>
    internal SilkEnvironmentUnsupportedFeatures SemanticsInvalidatingFeatures =>
        UnsupportedFeatures &
        (SilkEnvironmentUnsupportedFeatures.ColorTemperature |
            SilkEnvironmentUnsupportedFeatures.PoleAxis);

    /// <summary>
    /// Gets the authored emission scale a unit-white environment is multiplied by.
    /// </summary>
    /// <remarks>
    /// The 0.96 factor is the ambient a unit white dome already resolves to in
    /// this renderer and in Storm, and it is the same normalization hdSilk
    /// applies to an untextured dome. Keeping it here is what makes a dome whose
    /// image is constant 1.0 render identically to an untextured unit dome.
    /// </remarks>
    internal Vector3 AmbientEmissionScale =>
        Color * (SilkEnvironmentMeanRadiance.StormUnitDomeAmbientScale *
            Intensity * MathF.Pow(2f, Exposure) * Diffuse);

    /// <summary>
    /// Gets the radiance scale the prefiltered specular chain is built from.
    /// </summary>
    /// <remarks>
    /// The same product as <see cref="AmbientEmissionScale"/> with
    /// <c>inputs:specular</c> in place of <c>inputs:diffuse</c>. It carries the
    /// same unit-white-dome normalization on purpose: a dome authored with equal
    /// diffuse and specular contributions must reflect the same sky it lights
    /// with, and normalizing one term and not the other would make a mirror and a
    /// matte surface disagree about how bright the sky is.
    /// </remarks>
    internal Vector3 SpecularEmissionScale =>
        Color * (SilkEnvironmentMeanRadiance.StormUnitDomeAmbientScale *
            Intensity * MathF.Pow(2f, Exposure) * Specular);

    /// <summary>
    /// Gets the authored light-to-world transform as a matrix, for the bake that
    /// orients the image.
    /// </summary>
    /// <remarks>
    /// The wire carries the transform row-major, and <see cref="Matrix4x4"/>'s
    /// <c>Mrc</c> naming is row-major too, so the copy is element for element.
    /// Only the upper 3x3 is ever read: a dome light is infinitely distant, so its
    /// translation cannot move the sky and its scale cannot resize it.
    /// </remarks>
    internal Matrix4x4 LightToWorld => new(
        (float)_transform[0], (float)_transform[1], (float)_transform[2], (float)_transform[3],
        (float)_transform[4], (float)_transform[5], (float)_transform[6], (float)_transform[7],
        (float)_transform[8], (float)_transform[9], (float)_transform[10], (float)_transform[11],
        (float)_transform[12], (float)_transform[13], (float)_transform[14], (float)_transform[15]);

    internal static SilkEnvironmentData CopyFrom(SilkEnvironmentUpsertCommand command)
    {
        double[] transform = new double[16];
        for (int index = 0; index < transform.Length; index++)
        {
            transform[index] = command.GetTransformElement(index);
        }

        return new SilkEnvironmentData(
            command.Path,
            command.StableHash,
            command.TexturePath,
            command.TextureFormat,
            command.SourceColorSpace,
            command.UnsupportedFeatures,
            command.DomeIndex,
            new Vector3(command.GetColor(0), command.GetColor(1), command.GetColor(2)),
            command.Intensity,
            command.Exposure,
            command.Diffuse,
            command.Specular,
            transform);
    }
}

/// <summary>
/// Reduces a textured dome light's image to a single ambient radiance value.
/// </summary>
/// <remarks>
/// <para>
/// This is the *fallback* term, and calling it image-based lighting would
/// overstate it. It is a mean-radiance ambient approximation: the image is
/// collapsed to its solid-angle-weighted mean radiance and that single value
/// stands in for the whole sky, so every surface normal receives the same value
/// and none of the sky's directionality survives. It is exact only for an
/// environment that really is uniform.
/// </para>
/// <para>
/// It is reached only when the directional prefiltered environment cannot be:
/// an unsupported image mapping, an asset that could not be read or decoded, a
/// budget, a device that cannot carry the environment resources, or a dome beyond
/// the composed-dome bound. Every one of those is named against the dome's own
/// prim path, so a scene that silently lost its directionality is not a state
/// this renderer can be in. Falling back here restores precisely the result the
/// scene had before the directional response existed rather than unlighting it.
/// </para>
/// </remarks>
internal static class SilkEnvironmentMeanRadiance
{
    /// <summary>
    /// The ambient a unit white dome light resolves to, matching Storm and the
    /// untextured dome term hdSilk already publishes.
    /// </summary>
    internal const float StormUnitDomeAmbientScale = 0.96f;

    /// <summary>
    /// Computes the solid-angle-weighted mean radiance of an equirectangular image.
    /// </summary>
    /// <param name="image">The decoded latitude/longitude environment image.</param>
    /// <param name="colorSpace">
    /// The effective colour space of the decoded texels. <see cref="SilkColorSpace.Srgb"/>
    /// linearizes each channel before it is accumulated.
    /// </param>
    /// <returns>The mean radiance, which is 1.0 for a constant-white image.</returns>
    /// <remarks>
    /// Rows are weighted by <c>sin(theta)</c> because an equirectangular row near
    /// a pole covers far less of the sphere than a row at the equator. Weighting
    /// every texel equally instead would let a bright pixel column at the pole
    /// dominate a term it barely contributes to. The weighting makes the mean
    /// correct as a mean; it does not make the result directional.
    /// </remarks>
    internal static Vector3 ComputeMeanRadiance(
        SilkDecodedImage image,
        SilkColorSpace colorSpace)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Width == 0 || image.Height == 0)
        {
            throw new InvalidDataException(
                "An environment image must have a non-zero width and height.");
        }

        int width = checked((int)image.Width);
        int height = checked((int)image.Height);
        bool linearize = colorSpace == SilkColorSpace.Srgb;

        // Accumulated in double precision. A 4K latlong is over eight million
        // texels, and a single-precision sum of that many HDR values loses the
        // dim majority of the sphere to the few bright ones.
        double red = 0;
        double green = 0;
        double blue = 0;
        double weightSum = 0;

        ReadOnlySpan<byte> bytes = image.Pixels;
        ValidateLength(image, width, height, bytes.Length);
        ReadOnlySpan<float> floats = image.Format == SilkTextureFormat.Rgba32Float
            ? MemoryMarshal.Cast<byte, float>(bytes)
            : default;

        for (int row = 0; row < height; row++)
        {
            double theta = Math.PI * (row + 0.5) / height;
            double weight = Math.Sin(theta);
            if (weight <= 0)
            {
                continue;
            }

            double rowRed = 0;
            double rowGreen = 0;
            double rowBlue = 0;
            int rowOffset = row * width * 4;
            for (int column = 0; column < width; column++)
            {
                int texel = rowOffset + (column * 4);
                float r;
                float g;
                float b;
                if (floats.IsEmpty)
                {
                    r = bytes[texel] / 255f;
                    g = bytes[texel + 1] / 255f;
                    b = bytes[texel + 2] / 255f;
                }
                else
                {
                    r = floats[texel];
                    g = floats[texel + 1];
                    b = floats[texel + 2];
                }

                if (linearize)
                {
                    r = SrgbToLinear(r);
                    g = SrgbToLinear(g);
                    b = SrgbToLinear(b);
                }

                if (!float.IsFinite(r) || !float.IsFinite(g) || !float.IsFinite(b))
                {
                    throw new InvalidDataException(
                        "The environment image contains a non-finite texel.");
                }

                rowRed += r;
                rowGreen += g;
                rowBlue += b;
            }

            red += rowRed * weight;
            green += rowGreen * weight;
            blue += rowBlue * weight;
            weightSum += weight * width;
        }

        if (weightSum <= 0)
        {
            throw new InvalidDataException(
                "The environment image has no sampled solid angle.");
        }

        return new Vector3(
            (float)(red / weightSum),
            (float)(green / weightSum),
            (float)(blue / weightSum));
    }

    /// <summary>
    /// Resolves the colour space the decoded texels are actually in.
    /// </summary>
    /// <param name="declared">The colour space hdSilk published.</param>
    /// <param name="description">
    /// The shape and observations the image describer reported, or
    /// <see langword="null"/> when the image could not be described.
    /// </param>
    /// <param name="decodedFormat">The format the decode actually produced.</param>
    /// <remarks>
    /// <para>
    /// UsdLux carries a dome texture's colour space as asset-path metadata, which
    /// Hydra's light parameters do not expose at all, so hdSilk publishes
    /// <see cref="SilkColorSpace.Auto"/> and states that it does not know. There
    /// is no authored value to forward: the producer cannot read one, and a
    /// consumer that invented one would be asserting an encoding nobody wrote.
    /// </para>
    /// <para>
    /// What does exist is the image library's own effective colour space, which
    /// the ABI v2 image-info seam reports as an explicit observation. That is
    /// preferred over any inference, because it is the space the library will
    /// actually decode in. Only when the library made no observation is the
    /// decoded format used as the fallback signal -- an eight-bit environment is
    /// an sRGB-encoded LDR image, while a float image is already linear radiance,
    /// which is the whole reason an HDR environment is authored as one.
    /// </para>
    /// </remarks>
    internal static SilkColorSpace ResolveColorSpace(
        SilkColorSpace declared,
        SilkImageDescription? description,
        SilkTextureFormat decodedFormat)
    {
        switch (declared)
        {
            case SilkColorSpace.Raw:
                return SilkColorSpace.Raw;
            case SilkColorSpace.Srgb:
                return SilkColorSpace.Srgb;
            case SilkColorSpace.Auto:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(declared));
        }

        if (description is { } shape &&
            shape.Observed.HasFlag(SilkImageObservation.ColorSpace))
        {
            return shape.ColorSpace == SilkImageColorSpaceObservation.Srgb
                ? SilkColorSpace.Srgb
                : SilkColorSpace.Raw;
        }

        return decodedFormat == SilkTextureFormat.Rgba32Float
            ? SilkColorSpace.Raw
            : SilkColorSpace.Srgb;
    }

    private static void ValidateLength(
        SilkDecodedImage image,
        int width,
        int height,
        int byteLength)
    {
        int bytesPerTexel = image.Format == SilkTextureFormat.Rgba32Float ? 16 : 4;
        long expected = (long)width * height * bytesPerTexel;
        if (byteLength != expected)
        {
            throw new InvalidDataException(
                $"The decoded environment image carries {byteLength} bytes; " +
                $"expected {expected}.");
        }
    }

    private static float SrgbToLinear(float value) =>
        value <= 0.04045f
            ? value / 12.92f
            : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
}

/// <summary>
/// A bounded cache of resolved environment mean radiance, keyed by texture identity.
/// </summary>
/// <remarks>
/// <para>
/// Decoding an environment image is the expensive part and its result is a
/// single colour, so the decoded pixels are never retained: the retained cost is
/// three floats per distinct texture, and the decoded buffer is released as soon
/// as the mean has been accumulated.
/// </para>
/// <para>
/// The byte budget bounds the image this renderer will accept. It is checked
/// against the decoded buffer the image decoder returned, so it does not bound
/// that decoder's own transient allocation; what it does bound is the
/// solid-angle traversal and everything downstream of it, and an environment
/// over the budget is diagnosed against its dome prim instead of being resolved.
/// </para>
/// <para>
/// Entries are keyed by asset path, declared colour space and the asset's own
/// file stamp together: the same file read as raw and as sRGB has two different
/// mean radiances, and a file rewritten in place under a running session has a
/// third. Without the stamp a repaired or re-exported HDR would keep resolving
/// to the mean of the bytes that are no longer there.
/// </para>
/// </remarks>
internal sealed class SilkEnvironmentMeanRadianceCache
{
    /// <summary>
    /// The default decoded-image ceiling, in bytes, one resolve accepts.
    /// </summary>
    /// <remarks>
    /// 256 MiB admits a 4096x2048 float RGBA environment, which is the largest
    /// size the accepted corpus authors, and refuses an 8K one with a
    /// diagnostic instead of traversing half a gigabyte for a colour.
    /// </remarks>
    internal const ulong DefaultDecodeByteBudget = 256UL * 1024 * 1024;

    /// <summary>The default number of distinct environment textures retained.</summary>
    /// <remarks>
    /// A stage has one active dome light in almost every case and a handful
    /// while a variant is being switched. The bound exists so that a stage that
    /// cycles environments cannot grow the table without limit, not because a
    /// realistic scene approaches it.
    /// </remarks>
    internal const int DefaultCapacity = 8;

    private readonly Dictionary<CacheKey, Entry> _entries = [];
    private readonly ulong _decodeByteBudget;
    private readonly int _capacity;
    private ulong _clock;

    internal SilkEnvironmentMeanRadianceCache(
        ulong decodeByteBudget = DefaultDecodeByteBudget,
        int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfZero(decodeByteBudget);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _decodeByteBudget = decodeByteBudget;
        _capacity = capacity;
    }

    /// <summary>Gets the number of retained entries.</summary>
    internal int Count => _entries.Count;

    /// <summary>Gets the decoded-image ceiling one resolve accepts, in bytes.</summary>
    /// <remarks>
    /// Read by the prefiltered environment path as well, so the two share one
    /// statement of how large a dome texture this renderer decodes. Two budgets
    /// would let a dome be small enough to prefilter and too large to fall back
    /// to, which is a state with no correct behaviour.
    /// </remarks>
    internal ulong DecodeByteBudget => _decodeByteBudget;

    /// <summary>Gets the number of decodes performed since construction.</summary>
    internal int DecodeCount { get; private set; }

    /// <summary>Gets the number of entries evicted since construction.</summary>
    internal int EvictionCount { get; private set; }

    /// <summary>Drops every retained mean.</summary>
    internal void Clear() => _entries.Clear();

    /// <summary>
    /// Returns the mean radiance of one environment texture, decoding it once.
    /// </summary>
    /// <param name="asset">The resolved texture asset path.</param>
    /// <param name="declaredColorSpace">The colour space hdSilk published.</param>
    /// <param name="stamp">
    /// The asset's observed file stamp, which is what distinguishes a file
    /// rewritten in place from the one whose mean is already retained.
    /// </param>
    /// <param name="decoder">The image decoder, called at most once per entry.</param>
    /// <param name="describer">
    /// The image describer, used to resolve an <see cref="SilkColorSpace.Auto"/>
    /// declaration from the library's own observation rather than from the
    /// decoded format.
    /// </param>
    internal Vector3 Resolve(
        string asset,
        SilkColorSpace declaredColorSpace,
        SilkEnvironmentAssetStamp stamp,
        Func<string, bool, SilkDecodedImage> decoder,
        Func<string, SilkImageDescription?>? describer = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(asset);
        ArgumentNullException.ThrowIfNull(decoder);

        var key = new CacheKey(asset, declaredColorSpace, stamp);
        if (_entries.TryGetValue(key, out Entry? cached))
        {
            cached.LastUsed = ++_clock;
            return cached.MeanRadiance;
        }

        SilkImageDescription? description = describer?.Invoke(asset);

        // Preflighted from the description, so an oversized environment costs a
        // describe rather than a decode. The fallback is the path a refused dome
        // lands on, so decoding half a gigabyte here to produce one colour --
        // after the prefilter refused the very same image on the very same
        // budget -- would defeat the budget rather than enforce it.
        if (description is { } shape)
        {
            ulong preflight = SilkEnvironmentPrefilter.EstimateDecodedBytes(shape);
            if (preflight > _decodeByteBudget)
            {
                throw new SilkEnvironmentBudgetExceededException(
                    asset,
                    preflight,
                    _decodeByteBudget);
            }
        }

        // Decoded without a transfer function so that the effective colour space
        // is decided from what the image library observed, which is the only
        // place a dome texture's encoding is actually recorded.
        SilkDecodedImage image;
        ulong decodedBytes;
        Vector3 mean;
        try
        {
            image = decoder(asset, false);
            DecodeCount++;
            decodedBytes = checked((ulong)image.Pixels.LongLength);

            // Re-checked after the decode, because the describer and the decoder
            // can disagree: a description that preflighted inside the budget does
            // not make the bytes that were actually produced fit. The traversed
            // bytes are the ones the budget has to hold.
            if (decodedBytes > _decodeByteBudget)
            {
                throw new SilkEnvironmentBudgetExceededException(
                    asset,
                    decodedBytes,
                    _decodeByteBudget);
            }

            mean = SilkEnvironmentMeanRadiance.ComputeMeanRadiance(
                image,
                SilkEnvironmentMeanRadiance.ResolveColorSpace(
                    declaredColorSpace,
                    description,
                    image.Format));
        }
        catch (OverflowException exception)
        {
            // An image whose byte count does not fit the accumulator is over any
            // budget this renderer states, so it resolves to the budget
            // diagnostic rather than escaping a frame as an arithmetic fault.
            throw new SilkEnvironmentBudgetExceededException(
                asset,
                ulong.MaxValue,
                _decodeByteBudget,
                exception);
        }
        catch (OutOfMemoryException exception)
        {
            // Reached when the decode fits the stated budget but not the process.
            // Naming it as a budget refusal keeps the dome on the untextured
            // emission it would otherwise have had, instead of failing the frame.
            throw new SilkEnvironmentBudgetExceededException(
                asset,
                ulong.MaxValue,
                _decodeByteBudget,
                exception);
        }

        Evict();
        _entries[key] = new Entry(mean) { LastUsed = ++_clock };
        return mean;
    }

    /// <summary>Drops every retained entry.</summary>
    private void Evict()
    {
        while (_entries.Count >= _capacity)
        {
            CacheKey? oldestKey = null;
            ulong oldest = ulong.MaxValue;
            foreach (KeyValuePair<CacheKey, Entry> pair in _entries)
            {
                if (pair.Value.LastUsed < oldest)
                {
                    oldest = pair.Value.LastUsed;
                    oldestKey = pair.Key;
                }
            }
            if (oldestKey is null)
            {
                return;
            }
            _entries.Remove(oldestKey.Value);
            EvictionCount++;
        }
    }

    /// <summary>
    /// The identity one retained mean is a function of: which file, read in which
    /// colour space, in the state that file was in when it was read.
    /// </summary>
    private readonly record struct CacheKey(
        string Asset,
        SilkColorSpace ColorSpace,
        SilkEnvironmentAssetStamp Stamp);

    private sealed class Entry(Vector3 meanRadiance)
    {
        internal Vector3 MeanRadiance { get; } = meanRadiance;

        internal ulong LastUsed { get; set; }
    }
}

/// <summary>
/// Thrown when one environment resource would exceed a configured byte budget.
/// </summary>
/// <remarks>
/// Deliberately not an <see cref="InvalidDataException"/>: the image is perfectly
/// valid, and the caller distinguishes "this environment is larger than this
/// renderer accepts" from "this environment is corrupt" because they produce
/// different diagnostics against the same dome prim. The same type covers the
/// decoded source, the prefiltered payload and the retained cache, because all
/// three are the same statement about the same dome and a caller that has to
/// re-report one has to re-report all of them.
/// </remarks>
internal sealed class SilkEnvironmentBudgetExceededException(
    string asset,
    ulong decodedBytes,
    ulong budgetBytes,
    Exception? innerException = null)
    : InvalidOperationException(
        $"The environment resource '{asset}' requires {decodedBytes} bytes, " +
        $"which exceeds its {budgetBytes} byte budget.",
        innerException)
{
    internal string Asset { get; } = asset;

    internal ulong DecodedBytes { get; } = decodedBytes;

    internal ulong BudgetBytes { get; } = budgetBytes;
}
