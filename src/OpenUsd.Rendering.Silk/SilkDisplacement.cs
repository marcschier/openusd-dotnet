// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Why an authored UsdPreviewSurface <c>displacement</c> input did not move the
/// geometry hdSilk drew.
/// </summary>
/// <remarks>
/// Every value other than <see cref="None"/> and <see cref="NotAuthored"/> means
/// the same thing: the undisplaced surface was drawn, and the reason is named in
/// a <see cref="SilkRenderDiagnosticCodes.DisplacementUnsupported"/> or
/// <see cref="SilkRenderDiagnosticCodes.DisplacementBudgetExceeded"/> diagnostic.
/// None of them may be swallowed -- an unmoved surface that reported no reason is
/// indistinguishable from one the author never asked to move, which is exactly
/// the plausible-but-wrong result this enum exists to prevent.
/// </remarks>
public enum SilkDisplacementFallback
{
    /// <summary>The authored displacement moved the drawn geometry.</summary>
    None = 0,

    /// <summary>The material authors no displacement input at all.</summary>
    NotAuthored = 1,

    /// <summary>
    /// The material authors a constant displacement of exactly zero, which is
    /// the schema default and moves nothing.
    /// </summary>
    AuthoredZero = 2,

    /// <summary>The emitted topology is not an indexed triangle list.</summary>
    /// <remarks>
    /// Displacement is defined along the surface normal, and a line or point
    /// list has no surface. hdSilk resolves normals for those topologies to the
    /// canonical (0, 0, 1) fallback, so displacing them would translate the prim
    /// along an arbitrary axis rather than displace it.
    /// </remarks>
    UnsupportedTopology = 3,

    /// <summary>
    /// The displacement input is driven by the material's two-image composite
    /// operand.
    /// </summary>
    /// <remarks>
    /// The product, sum, difference or blend of two images is resolved in the
    /// fragment stage from two bound samplers. Reproducing it per vertex would
    /// need a second decoded image and a second sampling rule that no analytic
    /// gate covers yet, so the composite is reported rather than half-applied.
    /// </remarks>
    UnsupportedComposite = 4,

    /// <summary>The displacement texture is a UDIM set.</summary>
    /// <remarks>
    /// A UDIM entry is resolved into a padded atlas with per-tile metadata in its
    /// first row and a gutter around every tile, and the fragment stage addresses
    /// it with a bespoke rule. Per-vertex sampling of that atlas is not the same
    /// function, so the tile set is reported rather than sampled as if it were
    /// one image.
    /// </remarks>
    UnsupportedUdim = 5,

    /// <summary>
    /// The displacement texture names a texture-coordinate primvar the mesh does
    /// not carry, or names none at all.
    /// </summary>
    UnsupportedUvSet = 6,

    /// <summary>The authored displacement is not a finite value.</summary>
    NonFiniteAmount = 7,

    /// <summary>The prim carries more points than the displacement vertex budget.</summary>
    VertexBudget = 8,

    /// <summary>
    /// The displacement image's declared dimensions exceed the texel budget, or
    /// the byte count they imply overflowed.
    /// </summary>
    /// <remarks>
    /// Decided from the image's declared dimensions and format before a single
    /// pixel is decoded, so an image whose header alone would exhaust memory is
    /// refused rather than allocated and then measured.
    /// </remarks>
    TextureBudget = 9,

    /// <summary>
    /// The displacement image could not be found or decoded, so the authored
    /// <c>fallback</c> was used as a constant displacement.
    /// </summary>
    /// <remarks>
    /// UsdUVTexture defines <c>fallback</c> as the value the reader produces when
    /// the file cannot be read, so hdSilk displaces by that authored value, read
    /// through the same output channel and the same <c>scale</c> and <c>bias</c>
    /// the texel would have been. The substitution is still reported, because
    /// rendering an authored fallback is not the same picture as rendering the
    /// file, and a fallback that is not finite leaves the surface undisplaced.
    /// </remarks>
    TextureUnavailable = 10,

    /// <summary>
    /// The authored input defers to image metadata this renderer could not
    /// observe, so there is nothing to resolve it from.
    /// </summary>
    /// <remarks>
    /// `sourceColorSpace = auto` and `wrap = useMetadata` both defer to the image
    /// file. hdSilk asks the image library for its effective colour space and for
    /// per-axis sampler metadata; when the library was not consulted at all -- a
    /// consumer that supplied a decoder but no describer -- there is no
    /// observation to resolve from, and guessing a default would be a picture
    /// nobody authored. The exact deferred case is refused by name instead.
    /// </remarks>
    MetadataUnavailable = 11,

    /// <summary>
    /// The image's own sampler metadata names an addressing mode the wire cannot
    /// carry.
    /// </summary>
    /// <remarks>
    /// Hio can report mirror-clamp-to-edge, which is neither of the two mirroring
    /// or clamping modes UsdUVTexture defines, so a `useMetadata` that resolves to
    /// it is reported rather than rounded to the nearest mode this renderer does
    /// implement.
    /// </remarks>
    MetadataUnsupported = 12
}

/// <summary>Where one material's resolved displacement amount comes from.</summary>
internal enum SilkDisplacementSource
{
    /// <summary>Nothing moves.</summary>
    None = 0,

    /// <summary>One constant amount for every point.</summary>
    Constant = 1,

    /// <summary>A per-vertex sample of a decoded height field.</summary>
    Texture = 2
}

/// <summary>
/// Everything about one material's displacement that can be decided without
/// reading a pixel.
/// </summary>
/// <remarks>
/// The plan exists so the retained-geometry cache can be consulted before any
/// work is done. Its <see cref="Identity"/> is derived from the authored inputs
/// and the image file's own stamp, so two evaluations that would produce the same
/// vertices produce the same identity, and a cache hit costs no image decode, no
/// per-vertex sampling and no geometry construction.
/// </remarks>
internal readonly record struct SilkDisplacementPlan(
    SilkDisplacementFallback Fallback,
    SilkDisplacementSource Source,
    ulong Identity,
    float ConstantAmount,
    SilkMaterialTexture? Texture,
    SilkColorSpace AuthoredColorSpace,
    IReadOnlyList<float>? UvTransform,
    string RequestedUvPrimvar = "")
{
    /// <summary>A plan for a material that authors no displacement.</summary>
    internal static SilkDisplacementPlan NotAuthored { get; } = new(
        SilkDisplacementFallback.NotAuthored,
        SilkDisplacementSource.None,
        0,
        0,
        null,
        SilkColorSpace.Raw,
        null);

    /// <summary>Creates a plan that names why nothing will move.</summary>
    /// <param name="fallback">The reason nothing moves.</param>
    /// <param name="requestedUvPrimvar">
    /// The coordinate set the refused input asked for, when the refusal is about
    /// that coordinate set being absent.
    /// </param>
    /// <remarks>
    /// A refusal that names a primvar keeps the name, because the refusal is a
    /// statement about a *missing* attribute and the fast path has to notice when
    /// it stops being missing. Without the name the retained geometry records no
    /// displacement coordinate set at all, so a mesh republished with the primvar
    /// added compares equal to the refused one and keeps the flat vertices.
    /// </remarks>
    internal static SilkDisplacementPlan Refused(
        SilkDisplacementFallback fallback,
        string requestedUvPrimvar = "") =>
        new(
            fallback,
            SilkDisplacementSource.None,
            0,
            0,
            null,
            SilkColorSpace.Raw,
            null,
            requestedUvPrimvar);

    /// <summary>
    /// Gets the coordinate set the retained geometry must fingerprint for this
    /// plan, which is the sampled one when it moves and the requested one when it
    /// was refused for being absent.
    /// </summary>
    internal string DisplacementUvPrimvar =>
        Texture?.UvPrimvar is { Length: > 0 } sampled ? sampled : RequestedUvPrimvar;

    /// <summary>Gets whether this plan asks for per-point amounts.</summary>
    internal bool MovesGeometry => Source != SilkDisplacementSource.None;
}

/// <summary>
/// One material's resolved UsdPreviewSurface <c>displacement</c> input, as the
/// scalar amount every point of a bound prim is moved along its shading normal.
/// </summary>
/// <remarks>
/// <para>
/// This is the renderer-neutral half of hdSilk's displacement: it turns an
/// authored constant, or an authored <c>UsdUVTexture</c>, into one exact scalar
/// per emitted point. Nothing here touches a device, so the same amounts drive
/// the colour pass, the raster shadow depth pass, the pick pass and the selection
/// outline -- they all draw the one retained vertex buffer these amounts moved.
/// </para>
/// <para>
/// The height field is retained as single-precision floats, not as the eight-bit
/// texels an image may have been decoded from. A height is not a colour: the
/// authored <c>scale</c> and <c>bias</c> of a <c>UsdUVTexture</c> may legitimately
/// carry a height field into negative values or past one, and quantizing the
/// product back into an unsigned-normalized byte -- which is what the fragment
/// stage's upload path must do -- would clamp exactly those values away. The
/// affine is therefore applied in float, after the texel is converted, and is
/// never clamped.
/// </para>
/// <para>
/// Addressing is the authored wrap mode, evaluated exactly. <c>black</c> and
/// <c>useMetadata</c> are a transparent-black border: a texel outside the image
/// contributes zero <em>sampled</em> value, including its share of a bilinear
/// blend near an edge, which is what a border-addressed sampler computes. The
/// fragment stage resolves both to clamp-to-edge because no backend is handed a
/// border colour; the vertex stage owns its own addressing and has no such
/// constraint.
/// </para>
/// <para>
/// The authored <c>scale</c> and <c>bias</c> are applied to the <em>filtered</em>
/// value rather than to the stored texels, because that is where UsdUVTexture
/// puts them: the reader samples, then scales and biases. It matters at a border,
/// where the sampled value is zero and the authored result is therefore the bias
/// rather than zero, and inside a bilinear blend that straddles an edge, where
/// the border's share must carry the bias exactly once rather than once per
/// texel.
/// </para>
/// <para>
/// Base level only: a vertex has no screen derivative, so there is no defensible
/// level of detail to select, and reading a mip would silently low-pass the
/// height field.
/// </para>
/// </remarks>
internal sealed class SilkDisplacementField
{
    private readonly float[] _texels;
    private readonly int _width;
    private readonly int _height;
    private readonly SilkTextureWrap _wrapS;
    private readonly SilkTextureWrap _wrapT;
    private readonly float[] _uvTransform;
    private readonly float _scale;
    private readonly float _bias;

    private SilkDisplacementField(
        float constantAmount,
        float[] texels,
        int width,
        int height,
        SilkTextureWrap wrapS,
        SilkTextureWrap wrapT,
        float[] uvTransform,
        float scale,
        float bias,
        string uvPrimvar,
        ulong identity)
    {
        ConstantAmount = constantAmount;
        _texels = texels;
        _width = width;
        _height = height;
        _wrapS = wrapS;
        _wrapT = wrapT;
        _uvTransform = uvTransform;
        _scale = scale;
        _bias = bias;
        UvPrimvar = uvPrimvar;
        Identity = identity;
    }

    /// <summary>
    /// The largest number of points one prim may displace.
    /// </summary>
    /// <remarks>
    /// Checked from the emitted point count before a single amount is allocated,
    /// so a prim that refined past this bound falls back with a named reason
    /// rather than allocating an amount per point first and discovering the size
    /// afterwards.
    /// </remarks>
    internal const int MaximumDisplacedPoints = 4 * 1024 * 1024;

    /// <summary>
    /// The largest number of texels one retained displacement image may hold.
    /// </summary>
    /// <remarks>
    /// One texel is retained as one single-precision amount, so this bound is
    /// also the sixty-four mebibyte decoded byte budget of the image cache. It is
    /// checked against the image's declared dimensions before it is decoded, so an
    /// image whose header alone claims more than this never reaches a decoder.
    /// </remarks>
    internal const int MaximumImageTexels = 16 * 1024 * 1024;

    /// <summary>Gets the constant amount, zero for a texture-driven field.</summary>
    internal float ConstantAmount { get; }

    /// <summary>Gets whether this field reads a decoded image.</summary>
    internal bool IsTextured => _texels.Length != 0;

    /// <summary>
    /// Gets the texture-coordinate primvar the image is sampled through, empty
    /// for a constant field.
    /// </summary>
    internal string UvPrimvar { get; }

    /// <summary>
    /// Gets the identity of everything that can change the amounts: the authored
    /// constant, the image's asset, addressing, channel, affine and file stamp.
    /// </summary>
    internal ulong Identity { get; }

    /// <summary>Gets the retained texel count, zero for a constant field.</summary>
    internal int TexelCount => _texels.Length;

    /// <summary>Creates a constant displacement field.</summary>
    internal static SilkDisplacementField Constant(float amount, ulong identity) =>
        new(
            amount,
            [],
            0,
            0,
            SilkTextureWrap.Clamp,
            SilkTextureWrap.Clamp,
            [],
            1,
            0,
            string.Empty,
            identity);

    /// <summary>Creates a texture-driven displacement field over decoded amounts.</summary>
    /// <param name="texels">The raw sampled value of each texel, one per texel.</param>
    /// <param name="width">The image width in texels.</param>
    /// <param name="height">The image height in texels.</param>
    /// <param name="wrapS">The authored horizontal addressing.</param>
    /// <param name="wrapT">The authored vertical addressing.</param>
    /// <param name="uvTransform">The material's folded coordinate affine.</param>
    /// <param name="scale">The authored multiply, applied after filtering.</param>
    /// <param name="bias">The authored offset, applied after filtering.</param>
    /// <param name="uvPrimvar">The coordinate set this field is sampled through.</param>
    /// <param name="identity">The identity of everything that can change the amounts.</param>
    internal static SilkDisplacementField Textured(
        float[] texels,
        int width,
        int height,
        SilkTextureWrap wrapS,
        SilkTextureWrap wrapT,
        IReadOnlyList<float> uvTransform,
        float scale,
        float bias,
        string uvPrimvar,
        ulong identity)
    {
        ArgumentNullException.ThrowIfNull(texels);
        ArgumentNullException.ThrowIfNull(uvTransform);
        if (width <= 0 || height <= 0 || texels.Length != checked(width * height))
        {
            throw new ArgumentException(
                "A textured displacement field must carry one amount per texel.",
                nameof(texels));
        }
        if (uvTransform.Count != 6)
        {
            throw new ArgumentException(
                "A material texture-coordinate transform must carry six elements.",
                nameof(uvTransform));
        }
        if (!float.IsFinite(scale) || !float.IsFinite(bias))
        {
            throw new ArgumentException(
                "A displacement affine must be finite.",
                nameof(scale));
        }
        return new SilkDisplacementField(
            0,
            texels,
            width,
            height,
            wrapS,
            wrapT,
            [.. uvTransform],
            scale,
            bias,
            uvPrimvar,
            identity);
    }

    /// <summary>
    /// Resolves one amount per emitted point, or false when the amounts would all
    /// be zero and the geometry is therefore identical to the undisplaced one.
    /// </summary>
    /// <param name="pointCount">The emitted point count.</param>
    /// <param name="uv">
    /// The authored texture coordinates, required for a textured field and
    /// ignored for a constant one.
    /// </param>
    /// <param name="amounts">The per-point amounts, or an empty array.</param>
    /// <returns>Whether any point moves.</returns>
    internal bool TryResolveAmounts(
        int pointCount,
        SilkVertexAttributeData? uv,
        out float[] amounts)
    {
        amounts = [];
        if (pointCount <= 0)
        {
            return false;
        }
        if (!IsTextured)
        {
            if (ConstantAmount == 0)
            {
                return false;
            }
            amounts = new float[pointCount];
            Array.Fill(amounts, ConstantAmount);
            return true;
        }

        ArgumentNullException.ThrowIfNull(uv);
        float[] resolved = new float[pointCount];
        bool moved = false;
        for (int point = 0; point < pointCount; point++)
        {
            float u = uv.GetComponent(point, 0);
            float v = uv.ComponentCount > 1 ? uv.GetComponent(point, 1) : 0;
            float amount = Sample(u, v);
            resolved[point] = amount;
            moved |= amount != 0;
        }
        if (!moved)
        {
            return false;
        }
        amounts = resolved;
        return true;
    }

    /// <summary>
    /// Samples the amount at one authored texture coordinate, applying the
    /// material affine, the authored wrap modes and bilinear filtering.
    /// </summary>
    internal float Sample(float u, float v)
    {
        if (!IsTextured)
        {
            return ConstantAmount;
        }
        float s = (_uvTransform[0] * u) + (_uvTransform[1] * v) + _uvTransform[4];
        float t = (_uvTransform[2] * u) + (_uvTransform[3] * v) + _uvTransform[5];
        if (!float.IsFinite(s) || !float.IsFinite(t))
        {
            return 0;
        }

        // The half-texel shift, the two integer neighbours and the fractional
        // blend are exactly what a linear-filtered sampler computes, which is
        // what lets one image drive a vertex amount here and a fragment sample in
        // the checked shader without the two disagreeing about anything except
        // the border rule this stage implements and that one cannot.
        double x = ((double)s * _width) - 0.5;
        double y = ((double)t * _height) - 0.5;
        double floorX = Math.Floor(x);
        double floorY = Math.Floor(y);
        float fractionX = (float)(x - floorX);
        float fractionY = (float)(y - floorY);
        if (floorX <= int.MinValue || floorX >= int.MaxValue - 1 ||
            floorY <= int.MinValue || floorY >= int.MaxValue - 1)
        {
            // A coordinate this far outside the image cannot be reduced without
            // overflowing. Under a border mode the sample is the border; under a
            // periodic or clamped mode the reduction is unrepresentable, and the
            // honest answer is the same zero sample. The authored affine still
            // applies to it, exactly as it applies to any other border sample.
            return Affine(0);
        }
        int x0 = (int)floorX;
        int y0 = (int)floorY;
        float c00 = Texel(x0, y0);
        float c10 = Texel(x0 + 1, y0);
        float c01 = Texel(x0, y0 + 1);
        float c11 = Texel(x0 + 1, y0 + 1);
        float top = c00 + ((c10 - c00) * fractionX);
        float bottom = c01 + ((c11 - c01) * fractionX);
        return Affine(top + ((bottom - top) * fractionY));
    }

    /// <summary>Applies the authored multiply and offset to one sampled value.</summary>
    private float Affine(float sampled)
    {
        float value = (sampled * _scale) + _bias;
        return float.IsFinite(value) ? value : 0;
    }

    /// <summary>
    /// Whether a wrap mode addresses outside the image with a border rather than
    /// with a texel of the image.
    /// </summary>
    /// <remarks>
    /// <see cref="SilkTextureWrap.UseMetadata"/> is a border here for the reason
    /// USD gives: it defers addressing to wrap metadata inside the image file,
    /// hdSilk reads no such metadata, and the documented fallback when none is
    /// present is <c>black</c>. It stays a distinct wire value, and a distinct
    /// part of this field's identity, because a consumer that does read metadata
    /// must be able to tell the two apart.
    /// </remarks>
    internal static bool IsBorder(SilkTextureWrap wrap) =>
        wrap is SilkTextureWrap.Black or SilkTextureWrap.UseMetadata;

    /// <summary>
    /// Reads one texel under the authored addressing, returning the transparent
    /// black border for a coordinate a border mode places outside the image.
    /// </summary>
    private float Texel(int x, int y)
    {
        if (!TryAddress(x, _width, _wrapS, out int column) ||
            !TryAddress(y, _height, _wrapT, out int row))
        {
            return 0;
        }
        return _texels[(row * _width) + column];
    }

    /// <summary>
    /// Resolves one texel coordinate under an authored wrap mode, or false when
    /// the coordinate falls on the border.
    /// </summary>
    private static bool TryAddress(
        int coordinate,
        int size,
        SilkTextureWrap wrap,
        out int resolved)
    {
        switch (wrap)
        {
            case SilkTextureWrap.Repeat:
                int repeated = coordinate % size;
                resolved = repeated < 0 ? repeated + size : repeated;
                return true;
            case SilkTextureWrap.Mirror:
                int period = size * 2;
                int mirrored = coordinate % period;
                if (mirrored < 0)
                {
                    mirrored += period;
                }
                resolved = mirrored < size ? mirrored : period - mirrored - 1;
                return true;
            case SilkTextureWrap.Clamp:
                resolved = Math.Clamp(coordinate, 0, size - 1);
                return true;
            default:
                resolved = coordinate;
                return coordinate >= 0 && coordinate < size;
        }
    }
}
