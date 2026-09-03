// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// The bounded, backend-neutral shape of the prefiltered environment a textured
/// <c>UsdLuxDomeLight</c> is reduced to, and the budgets that shape is checked
/// against before anything is decoded or allocated.
/// </summary>
/// <remarks>
/// <para>
/// Every output dimension here is a fixed, small constant rather than a function
/// of the authored image. The precomputation is a CPU convolution whose cost is
/// <c>output texels * source bins</c>; letting the output follow a 4K source
/// would make a dome swap cost seconds. The bounded output also makes the
/// retained GPU footprint a constant a budget can be checked against *before*
/// anything is allocated, which is the only order in which a budget is a budget.
/// </para>
/// <para>
/// The specular output is a stack of roughness slices that all share one angular
/// resolution, not a mip chain. A chain's trailing levels collapse to 2x1 and
/// 1x1, so the roughest reflection would be a single texel rather than the
/// hemisphere-wide integration a roughness of 1 actually is.
/// </para>
/// </remarks>
internal sealed record SilkEnvironmentPrefilterOptions
{
    /// <summary>The default equirectangular radiance base width, in texels.</summary>
    /// <remarks>
    /// 64x32 is 2048 bins over the sphere, about 5.6 degrees a bin. That is the
    /// resolution every specular slice reflects at, including the sharpest, so a
    /// mirror shows a blurred sky rather than a sharp one -- which is stated in
    /// the docs rather than implied by a larger number that would still not be a
    /// mirror. It is also the lattice the diffuse convolution integrates over,
    /// where 2048 bins is far more than a cosine lobe needs.
    /// </remarks>
    internal const uint DefaultRadianceWidth = 64;

    /// <summary>The default equirectangular irradiance width, in texels.</summary>
    /// <remarks>
    /// A cosine-convolved irradiance field has no detail finer than its lobe, so
    /// 32x16 reconstructs it to well under a percent while keeping the
    /// convolution at 512 x 2048 operations.
    /// </remarks>
    internal const uint DefaultIrradianceWidth = 32;

    /// <summary>The default number of prefiltered roughness slices.</summary>
    /// <remarks>
    /// Six slices put samples at roughness 0, 0.2, 0.4, 0.6, 0.8 and 1.0. The
    /// prefiltered radiance varies smoothly and slowly in roughness -- it is an
    /// integral of one environment against a widening kernel -- so linear
    /// interpolation between neighbours reconstructs the axis far more accurately
    /// than the shading it feeds can resolve.
    /// </remarks>
    internal const uint DefaultSpecularSliceCount = 6;

    /// <summary>
    /// The default number of textured dome lights one prefiltered environment
    /// composes.
    /// </summary>
    /// <remarks>
    /// The bake is a sum in world space, so composing several domes is exact
    /// rather than approximate; the bound exists because each dome costs a full
    /// traversal of its own decoded image.
    /// </remarks>
    internal const int DefaultMaximumDomeLights = 4;

    /// <summary>The default ceiling on the prefiltered GPU footprint, in bytes.</summary>
    internal const ulong DefaultMaximumPrefilteredBytes = 64UL * 1024 * 1024;

    /// <summary>The default ceiling on one decoded source image, in bytes.</summary>
    /// <remarks>
    /// 256 MiB admits a 4096x2048 float RGBA environment and refuses an 8K one.
    /// It is checked against the shape the image describer reports, so an image
    /// over it is never decoded at all.
    /// </remarks>
    internal const ulong DefaultMaximumSourceBytes = 256UL * 1024 * 1024;

    /// <summary>
    /// The default ceiling on the decoded bytes one composed environment reads in
    /// total, in bytes.
    /// </summary>
    /// <remarks>
    /// Only one source is ever resident at a time -- each dome is decoded,
    /// accumulated into the shared world lattice, and released before the next is
    /// opened -- so this bounds the transient work a composed environment
    /// performs rather than a peak footprint. It exists because four domes at the
    /// per-image ceiling would otherwise be a gigabyte of decoding for one frame.
    /// </remarks>
    internal const ulong DefaultMaximumAggregateSourceBytes = 512UL * 1024 * 1024;

    /// <summary>Gets the shared default options.</summary>
    internal static SilkEnvironmentPrefilterOptions Default { get; } = new();

    /// <summary>Gets the equirectangular radiance base and specular slice width.</summary>
    internal uint RadianceWidth { get; init; } = DefaultRadianceWidth;

    /// <summary>Gets the equirectangular irradiance width.</summary>
    internal uint IrradianceWidth { get; init; } = DefaultIrradianceWidth;

    /// <summary>Gets the number of prefiltered roughness slices.</summary>
    internal uint SpecularSliceCount { get; init; } = DefaultSpecularSliceCount;

    /// <summary>Gets the number of textured domes one environment composes.</summary>
    internal int MaximumDomeLights { get; init; } = DefaultMaximumDomeLights;

    /// <summary>Gets the ceiling on the prefiltered CPU payload and GPU footprint.</summary>
    internal ulong MaximumPrefilteredBytes { get; init; } = DefaultMaximumPrefilteredBytes;

    /// <summary>Gets the ceiling on one decoded source image.</summary>
    internal ulong MaximumSourceBytes { get; init; } = DefaultMaximumSourceBytes;

    /// <summary>Gets the ceiling on the decoded bytes one environment reads in total.</summary>
    internal ulong MaximumAggregateSourceBytes { get; init; } =
        DefaultMaximumAggregateSourceBytes;

    /// <summary>Gets the radiance base height, which is always half its width.</summary>
    internal uint RadianceHeight => RadianceWidth / 2;

    /// <summary>Gets the irradiance height, which is always half its width.</summary>
    internal uint IrradianceHeight => IrradianceWidth / 2;

    /// <summary>Gets the height of the whole vertically stacked specular atlas.</summary>
    internal uint SpecularAtlasHeight => RadianceHeight * SpecularSliceCount;

    /// <summary>
    /// Gets the exact prefiltered byte count these options produce for one
    /// environment group, computed from the dimensions alone so it can be checked
    /// before an allocation.
    /// </summary>
    internal ulong PrefilteredByteSize => GetPrefilteredByteSize(1);

    /// <summary>
    /// Gets the exact prefiltered byte count <paramref name="groupCount"/>
    /// environment groups produce.
    /// </summary>
    internal ulong GetPrefilteredByteSize(int groupCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(groupCount, 1);
        ulong bytesPerTexel =
            SilkTextureFormats.GetBytesPerPixel(SilkEnvironmentMaps.Format);
        ulong irradiance = (ulong)IrradianceWidth * IrradianceHeight * bytesPerTexel;
        ulong specular = (ulong)RadianceWidth * SpecularAtlasHeight * bytesPerTexel;
        return checked((irradiance + specular) * (ulong)groupCount);
    }

    /// <summary>
    /// Checks the footprint of a whole grouped bake against the byte budget.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Validate"/> because the group count is a property
    /// of the scene rather than of the options, and it is still checked before a
    /// single output array exists -- which is the only order in which a byte
    /// budget bounds anything.
    /// </remarks>
    /// <exception cref="SilkEnvironmentBudgetExceededException">
    /// The grouped bake would exceed <see cref="MaximumPrefilteredBytes"/>.
    /// </exception>
    internal void ValidateGroupBudget(int groupCount)
    {
        ulong required = GetPrefilteredByteSize(groupCount);
        if (required > MaximumPrefilteredBytes)
        {
            throw new SilkEnvironmentBudgetExceededException(
                "prefiltered environment groups",
                required,
                MaximumPrefilteredBytes);
        }
    }

    /// <summary>Validates the option set and the budget it implies.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is out of range.</exception>
    /// <exception cref="SilkEnvironmentBudgetExceededException">
    /// The dimensions would exceed <see cref="MaximumPrefilteredBytes"/>.
    /// </exception>
    internal void Validate()
    {
        ValidateEquirectangularWidth(RadianceWidth, 16, 512, nameof(RadianceWidth));
        ValidateEquirectangularWidth(IrradianceWidth, 8, 256, nameof(IrradianceWidth));
        if (SpecularSliceCount < 2 || SpecularSliceCount > 16)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SpecularSliceCount),
                SpecularSliceCount,
                "A roughness axis needs at least two slices to interpolate across.");
        }
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumDomeLights, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumDomeLights, 16);
        ArgumentOutOfRangeException.ThrowIfZero(MaximumPrefilteredBytes);
        ArgumentOutOfRangeException.ThrowIfZero(MaximumSourceBytes);
        ArgumentOutOfRangeException.ThrowIfZero(MaximumAggregateSourceBytes);
        ulong required = PrefilteredByteSize;
        if (required > MaximumPrefilteredBytes)
        {
            throw new SilkEnvironmentBudgetExceededException(
                "prefiltered environment",
                required,
                MaximumPrefilteredBytes);
        }
    }

    private static void ValidateEquirectangularWidth(
        uint width,
        uint minimum,
        uint maximum,
        string name)
    {
        if (width < minimum || width > maximum || (width & (width - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(
                name,
                width,
                $"An equirectangular width must be a power of two in [{minimum}, {maximum}].");
        }
    }
}

/// <summary>
/// One textured dome light reduced to the inputs the prefilter integrates.
/// </summary>
/// <param name="Image">The decoded equirectangular source image.</param>
/// <param name="ColorSpace">
/// The resolved colour space of <paramref name="Image"/>'s texels;
/// <see cref="SilkColorSpace.Srgb"/> is linearized before integration.
/// </param>
/// <param name="LightToWorld">
/// The dome's authored light-to-world transform. Only its upper 3x3 is read, and
/// it is what makes the bake orientation dependent: the transform's inverse maps
/// a world direction back into the image.
/// </param>
/// <param name="DiffuseScale">
/// <c>color * intensity * 2^exposure * diffuse</c> times the unit-white-dome
/// normalization, which is the radiance scale the irradiance map is built from.
/// </param>
/// <param name="SpecularScale">
/// The same product with <c>specular</c> in place of <c>diffuse</c>, which is the
/// radiance scale the prefiltered specular slices are built from.
/// </param>
/// <remarks>
/// The two scales are separate maps rather than one map and two multipliers
/// because <c>inputs:diffuse</c> and <c>inputs:specular</c> are per dome: a stage
/// with a bright specular-only dome and a dim diffuse-only one cannot be
/// described by a single environment and a pair of scalars, and collapsing them
/// would light the scene from the wrong sky.
/// </remarks>
internal readonly record struct SilkEnvironmentSource(
    SilkDecodedImage Image,
    SilkColorSpace ColorSpace,
    Matrix4x4 LightToWorld,
    Vector3 DiffuseScale,
    Vector3 SpecularScale);

/// <summary>
/// The prefiltered environment the checked mesh fragment samples: one cosine
/// irradiance map and one stack of roughness-indexed prefiltered radiance
/// slices, both equirectangular and both already in world orientation.
/// </summary>
/// <remarks>
/// <para>
/// Both are baked in *world* space. That is the whole reason the shader needs no
/// orientation matrix and no per-dome loop: each dome's authored light-to-world
/// orientation is applied while its image is resampled into the shared
/// world-space bins, and several domes sum exactly. A rotation-dependent term
/// therefore exists without any per-frame rotation state, and a scene whose dome
/// is rotated produces different bytes here rather than the same bytes read
/// differently.
/// </para>
/// <para>
/// The pixel format is RGBA16F. It is the widest format every backend is
/// *required* to filter linearly -- Vulkan makes
/// <c>SAMPLED_IMAGE_FILTER_LINEAR</c> optional for 32-bit float formats, so a
/// 32-bit environment would sample correctly on the two backends that happen to
/// support it and produce blocky nearest-filtered reflections on a conformant
/// implementation that does not. Values are clamped to the largest finite half so
/// a sun brighter than 65504 saturates rather than becoming an infinity that
/// poisons every filtered neighbourhood.
/// </para>
/// </remarks>
internal sealed class SilkEnvironmentMaps
{
    /// <summary>The pixel format every environment resource uses.</summary>
    internal const SilkTextureFormat Format = SilkTextureFormat.Rgba16Float;

    /// <summary>The largest finite value a half can carry.</summary>
    internal const float MaximumHalf = 65504f;

    internal SilkEnvironmentMaps(
        uint irradianceWidth,
        uint irradianceHeight,
        byte[] irradiancePixels,
        uint specularWidth,
        uint specularSliceHeight,
        uint specularSliceCount,
        byte[] specularPixels,
        int domeCount,
        uint groupCount = 1,
        uint composedGroup = 0)
    {
        IrradianceWidth = irradianceWidth;
        IrradianceHeight = irradianceHeight;
        IrradiancePixels = irradiancePixels;
        SpecularWidth = specularWidth;
        SpecularSliceHeight = specularSliceHeight;
        SpecularSliceCount = specularSliceCount;
        SpecularPixels = specularPixels;
        DomeCount = domeCount;
        GroupCount = groupCount;
        ComposedGroup = composedGroup;
    }

    /// <summary>Gets the irradiance map width in texels.</summary>
    internal uint IrradianceWidth { get; }

    /// <summary>Gets the height of one irradiance group in texels.</summary>
    internal uint IrradianceHeight { get; }

    /// <summary>Gets the height of the whole vertically stacked irradiance atlas.</summary>
    internal uint IrradianceAtlasHeight => IrradianceHeight * GroupCount;

    /// <summary>Gets the tightly packed irradiance texels, group 0 first.</summary>
    internal byte[] IrradiancePixels { get; }

    /// <summary>Gets the specular atlas width in texels.</summary>
    internal uint SpecularWidth { get; }

    /// <summary>Gets the height of one roughness slice in texels.</summary>
    internal uint SpecularSliceHeight { get; }

    /// <summary>Gets the number of prefiltered roughness slices in one group.</summary>
    internal uint SpecularSliceCount { get; }

    /// <summary>Gets the height of the whole vertically stacked atlas in texels.</summary>
    internal uint SpecularAtlasHeight => SpecularSliceHeight * SpecularSliceCount * GroupCount;

    /// <summary>Gets the tightly packed specular atlas, group 0 slice 0 first.</summary>
    internal byte[] SpecularPixels { get; }

    /// <summary>Gets the number of dome lights composed into these maps.</summary>
    internal int DomeCount { get; }

    /// <summary>
    /// Gets the number of independently selectable environment groups the atlases
    /// carry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One for a scene with no dome linking. That is not merely an optimization:
    /// it is the layout and the byte content that existed before dome linking
    /// did, addressed through the same texture coordinates, so an unlinked scene
    /// renders the bytes it always rendered rather than bytes that are
    /// arithmetically equivalent.
    /// </para>
    /// <para>
    /// A scene that links domes carries one group per composed dome plus the
    /// composed group itself. The composed group is not redundant: a prim linked
    /// to every dome reads a single bake of their sum, while summing the per-dome
    /// groups would add values that were each rounded to half separately.
    /// </para>
    /// </remarks>
    internal uint GroupCount { get; }

    /// <summary>Gets the group index of the all-domes bake.</summary>
    internal uint ComposedGroup { get; }

    /// <summary>Gets the retained CPU and uploaded GPU byte count of both maps.</summary>
    internal ulong ByteSize =>
        checked((ulong)IrradiancePixels.LongLength + (ulong)SpecularPixels.LongLength);

    /// <summary>Gets the roughness slice <paramref name="slice"/> was prefiltered for.</summary>
    internal float GetSliceRoughness(uint slice) =>
        SpecularSliceCount <= 1 ? 0f : (float)slice / (SpecularSliceCount - 1);

    /// <summary>
    /// Reads the irradiance in one world direction with the same bilinear,
    /// U-wrapping, V-clamping reconstruction the shader uses.
    /// </summary>
    internal Vector3 SampleIrradiance(Vector3 direction) =>
        SampleIrradiance(direction, ComposedGroup);

    /// <summary>Reads the irradiance one environment group carries.</summary>
    internal Vector3 SampleIrradiance(Vector3 direction, uint group)
    {
        int origin = checked((int)(group * IrradianceWidth * IrradianceHeight * 4));
        return Sample(IrradiancePixels, origin, IrradianceWidth, IrradianceHeight, direction);
    }

    /// <summary>
    /// Reads the prefiltered radiance in one world direction at one roughness,
    /// mirroring the shader's slice selection and blend exactly.
    /// </summary>
    internal Vector3 SampleSpecular(Vector3 direction, float roughness) =>
        SampleSpecular(direction, roughness, ComposedGroup);

    /// <summary>Reads one environment group's prefiltered radiance.</summary>
    internal Vector3 SampleSpecular(Vector3 direction, float roughness, uint group)
    {
        float slice = Math.Clamp(roughness, 0f, 1f) * (SpecularSliceCount - 1);
        uint lower = (uint)Math.Floor(slice);
        uint upper = Math.Min(lower + 1, SpecularSliceCount - 1);
        float blend = slice - lower;
        return Vector3.Lerp(
            SampleSpecularSlice(direction, lower, group),
            SampleSpecularSlice(direction, upper, group),
            blend);
    }

    /// <summary>Reads the prefiltered radiance from one explicit roughness slice.</summary>
    internal Vector3 SampleSpecularSlice(Vector3 direction, uint slice) =>
        SampleSpecularSlice(direction, slice, ComposedGroup);

    /// <summary>Reads one group's radiance from one explicit roughness slice.</summary>
    internal Vector3 SampleSpecularSlice(Vector3 direction, uint slice, uint group)
    {
        uint atlasSlice = (group * SpecularSliceCount) + slice;
        int origin = checked((int)(atlasSlice * SpecularWidth * SpecularSliceHeight * 4));
        return Sample(
            SpecularPixels,
            origin,
            SpecularWidth,
            SpecularSliceHeight,
            direction);
    }

    private static Vector3 Sample(
        byte[] pixels,
        int origin,
        uint width,
        uint height,
        Vector3 direction)
    {
        Vector2 uv = SilkEnvironmentLatLong.Project(direction);
        float x = (uv.X * width) - 0.5f;
        float y = (uv.Y * height) - 0.5f;
        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);
        float fx = x - x0;
        float fy = y - y0;
        Vector3 c00 = Fetch(pixels, origin, width, height, x0, y0);
        Vector3 c10 = Fetch(pixels, origin, width, height, x0 + 1, y0);
        Vector3 c01 = Fetch(pixels, origin, width, height, x0, y0 + 1);
        Vector3 c11 = Fetch(pixels, origin, width, height, x0 + 1, y0 + 1);
        return Vector3.Lerp(
            Vector3.Lerp(c00, c10, fx),
            Vector3.Lerp(c01, c11, fx),
            fy);
    }

    private static Vector3 Fetch(
        byte[] pixels,
        int origin,
        uint width,
        uint height,
        int column,
        int row)
    {
        int wrapped = ((column % (int)width) + (int)width) % (int)width;
        int clamped = Math.Clamp(row, 0, (int)height - 1);
        return ReadTexel(pixels, origin + (((clamped * (int)width) + wrapped) * 4));
    }

    private static Vector3 ReadTexel(byte[] pixels, int halfIndex)
    {
        ReadOnlySpan<Half> halves = MemoryMarshal.Cast<byte, Half>(pixels);
        return new Vector3(
            (float)halves[halfIndex],
            (float)halves[halfIndex + 1],
            (float)halves[halfIndex + 2]);
    }
}

/// <summary>
/// The one equirectangular mapping the bake and the checked shader share.
/// </summary>
/// <remarks>
/// It is Hydra's own <c>ProjectToLatLong</c> from
/// <c>hdSt/shaders/domeLight.glslfx</c>, restated term for term:
/// <c>u = (atan2(z, x) + pi/2) / 2pi</c> and <c>v = acos(y) / pi</c>. The
/// reference points that pin it are external rather than internal --
/// <c>u = 0</c> is <c>-Z</c>, <c>u = 0.25</c> is <c>+X</c>, <c>u = 0.5</c> is
/// <c>+Z</c>, <c>u = 0.75</c> is <c>-X</c>, and <c>v = 0</c> is the <c>+Y</c>
/// pole -- because a projection and an inverse that agree with each other but not
/// with USD would light a scene plausibly with its sun in the wrong place, and a
/// round-trip test cannot see that at all.
/// </remarks>
internal static class SilkEnvironmentLatLong
{
    /// <summary>Projects a unit world direction onto equirectangular coordinates.</summary>
    /// <remarks>
    /// The longitude is wrapped into <c>[0, 1)</c> before it is returned.
    /// <c>atan2</c> answers in <c>(-pi, pi]</c>, so a direction at or just past
    /// <c>-X</c> -- and, because IEEE distinguishes <c>-0</c> from <c>+0</c>, the
    /// exact <c>-X</c> axis itself -- comes back negative. A negative longitude is
    /// the same direction, but it clamps to bin zero rather than wrapping to the
    /// far side of the image, which puts a whole hemisphere of radiance in the
    /// wrong place.
    /// </remarks>
    internal static Vector2 Project(Vector3 direction)
    {
        Vector3 unit = Normalize(direction);
        float u = (MathF.Atan2(unit.Z, unit.X) + (0.5f * MathF.PI)) / (2f * MathF.PI);
        u -= MathF.Floor(u);
        if (u >= 1f)
        {
            u = 0f;
        }
        float v = MathF.Acos(Math.Clamp(unit.Y, -1f, 1f)) / MathF.PI;
        return new Vector2(u, v);
    }

    /// <summary>Returns the unit direction at the centre of one image texel.</summary>
    internal static Vector3 Unproject(int column, int row, uint width, uint height) =>
        Unproject((column + 0.5) / width, (row + 0.5) / height);

    /// <summary>Returns the unit direction at one equirectangular coordinate.</summary>
    internal static Vector3 Unproject(double u, double v)
    {
        double theta = Math.PI * v;
        double phi = (2.0 * Math.PI * u) - (0.5 * Math.PI);
        double sinTheta = Math.Sin(theta);
        return new Vector3(
            (float)(sinTheta * Math.Cos(phi)),
            (float)Math.Cos(theta),
            (float)(sinTheta * Math.Sin(phi)));
    }

    /// <summary>Returns the solid angle one texel of an equirectangular image covers.</summary>
    internal static double GetSolidAngle(int row, uint width, uint height)
    {
        double theta = Math.PI * (row + 0.5) / height;
        return Math.Sin(theta) * (Math.PI / height) * (2.0 * Math.PI / width);
    }

    private static Vector3 Normalize(Vector3 value)
    {
        float lengthSquared = value.LengthSquared();
        return float.IsFinite(lengthSquared) && lengthSquared > 1e-20f
            ? value / MathF.Sqrt(lengthSquared)
            : Vector3.UnitY;
    }
}

/// <summary>
/// The split-sum environment BRDF, numerically integrated from the same GGX
/// distribution and Smith geometry term the direct specular lobe evaluates.
/// </summary>
/// <remarks>
/// <para>
/// The table holds the scale and bias applied to a material's F0 and F90, so the
/// environment specular weight is <c>F0 * A + F90 * B</c>. That is the second
/// half of Karis' split-sum approximation.
/// </para>
/// <para>
/// It replaced an analytic curve fit. A fit is accurate in the middle of the
/// domain and visibly wrong at its edges -- at grazing incidence and at both
/// roughness extremes -- and it is a *different* BRDF from the one the direct
/// lobe uses, so a surface lit by a light and by the sky reflected two different
/// materials. Integrating the shader's own geometry term makes them the same
/// function by construction.
/// </para>
/// <para>
/// The integration is GGX importance sampling over the deterministic Hammersley
/// point set: fixed, non-random, and therefore byte-identical on every machine
/// and every run, with a convergence that can be gated by recomputing entries at
/// a higher sample count. Every entry is non-negative by construction, because
/// the geometry term, the cosine and both Fresnel weights are, and the two
/// entries sum to at most one because they partition a single integral whose
/// value is the directional albedo.
/// </para>
/// </remarks>
internal static class SilkEnvironmentBrdf
{
    /// <summary>The table edge, in texels, along both the incidence and roughness axes.</summary>
    internal const uint Size = 32;

    /// <summary>The number of Hammersley samples one table entry integrates.</summary>
    internal const int SampleCount = 1024;

    /// <summary>The narrowest lobe the table integrates, matching the fragment.</summary>
    internal const double MinimumRoughness = 0.001;

    /// <summary>The floor a BRDF denominator is clamped to, matching the fragment.</summary>
    /// <remarks>
    /// It guards a division by exactly zero and nothing else. At the minimum
    /// roughness the GGX denominator is pi * alpha^4, about 3e-24, so a floor
    /// anywhere near it clamps the peak of every smooth lobe and unnormalizes it:
    /// at roughness 0.05 a 1e-9 floor cost the distribution 42% of its energy.
    /// </remarks>
    internal const double DenominatorEpsilon = 1.0e-30;

    private static readonly Lazy<byte[]> Table = new(() => Build(Size, SampleCount));

    /// <summary>Gets the tightly packed RGBA16F table, A in red and B in green.</summary>
    internal static byte[] Pixels => Table.Value;

    /// <summary>Gets the table's byte count.</summary>
    internal static ulong ByteSize => checked((ulong)Table.Value.LongLength);

    /// <summary>Integrates one entry of the table.</summary>
    /// <param name="normalDotEye">The cosine of the view angle, in (0, 1].</param>
    /// <param name="roughness">The authored roughness, in [0, 1].</param>
    /// <param name="sampleCount">The number of Hammersley samples.</param>
    internal static Vector2 Integrate(double normalDotEye, double roughness, int sampleCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleCount, 1);
        double cosine = Math.Clamp(normalDotEye, 1.0 / 512.0, 1.0);
        // The same clamp the fragment applies before it evaluates the lobe, so the
        // table and the direct lobe are the same function of authored roughness at
        // the mirror end of the axis.
        double clampedRoughness = Math.Max(MinimumRoughness, Math.Clamp(roughness, 0.0, 1.0));
        double alpha = clampedRoughness * clampedRoughness;
        double alphaSquared = alpha * alpha;

        // The view vector in the tangent frame of a normal that is +Z.
        double viewX = Math.Sqrt(Math.Max(0.0, 1.0 - (cosine * cosine)));
        double viewZ = cosine;

        double scale = 0;
        double bias = 0;
        for (int index = 0; index < sampleCount; index++)
        {
            (double u, double v) = Hammersley(index, sampleCount);

            // The standard GGX half-vector mapping. Sampling the distribution
            // rather than the hemisphere is what lets a narrow lobe be resolved
            // by a bounded, fixed number of samples. Grouped the same way the
            // distribution is -- (1 - v) + alphaSquared * v rather than
            // 1 + (alphaSquared - 1) * v -- so the inverse CDF stays the inverse
            // of the lobe that is actually evaluated. This runs in double
            // precision, where the difference is far below the sampling error,
            // but the two forms must not be allowed to drift apart.
            double phi = 2.0 * Math.PI * u;
            double cosTheta = Math.Sqrt(
                (1.0 - v) / ((1.0 - v) + (alphaSquared * v)));
            double sinTheta = Math.Sqrt(Math.Max(0.0, 1.0 - (cosTheta * cosTheta)));
            double halfX = sinTheta * Math.Cos(phi);
            double halfY = sinTheta * Math.Sin(phi);
            double halfZ = cosTheta;

            double viewDotHalf = (viewX * halfX) + (viewZ * halfZ);
            double lightZ = (2.0 * viewDotHalf * halfZ) - viewZ;
            if (lightZ <= 0 || viewDotHalf <= 0)
            {
                continue;
            }

            double normalDotLight = Math.Min(1.0, lightZ);
            double normalDotHalf = Math.Clamp(halfZ, 0.0, 1.0);
            double clampedViewDotHalf = Math.Clamp(viewDotHalf, 0.0, 1.0);
            if (normalDotHalf <= 0)
            {
                continue;
            }

            // The importance-sampled estimator: the GGX probability density
            // cancels the distribution term, leaving the geometry term, the
            // Jacobian of the reflection, and the Fresnel weight.
            double geometry = Geometry(clampedRoughness, normalDotLight, cosine);
            double visibility = geometry * clampedViewDotHalf / (normalDotHalf * cosine);
            double fresnel = Math.Pow(1.0 - clampedViewDotHalf, 5.0);
            scale += (1.0 - fresnel) * visibility;
            bias += fresnel * visibility;
        }

        return new Vector2(
            (float)Math.Max(0.0, scale / sampleCount),
            (float)Math.Max(0.0, bias / sampleCount));
    }

    /// <summary>
    /// The checked fragment's <c>NormalDistribution</c>: normalized GGX, with the
    /// same roughness clamp, the same guarded denominator, and the same grouping.
    /// </summary>
    /// <remarks>
    /// It carries no additive numerator epsilon, because the fragment no longer
    /// does either. The table and the direct lobe have to be one BRDF or the
    /// split-sum identity does not hold: a surface would reflect the environment
    /// through one distribution and a light through another, and no amount of
    /// resolution in the table would reconcile them.
    /// <para>
    /// The denominator is written as <c>n2 * alphaSquared + (1 - n2)</c> rather
    /// than Storm's <c>n2 * (alphaSquared - 1) + 1</c>. They are the same in exact
    /// arithmetic and nothing alike in single precision -- at roughness 0.01,
    /// <c>alphaSquared - 1</c> rounds to exactly -1 and the expression cancels to
    /// zero at <c>n.h = 1</c> -- and the table has to evaluate what the fragment
    /// evaluates, not what it means.
    /// </para>
    /// </remarks>
    internal static double Distribution(double roughness, double normalDotHalf)
    {
        double clampedRoughness = Math.Max(roughness, MinimumRoughness);
        double alpha = clampedRoughness * clampedRoughness;
        double alphaSquared = alpha * alpha;
        double normalDotHalfSquared =
            Math.Clamp(normalDotHalf * normalDotHalf, 0.0, 1.0);
        double denominator =
            (normalDotHalfSquared * alphaSquared) + (1.0 - normalDotHalfSquared);
        denominator = Math.PI * denominator * denominator;
        return alphaSquared / Math.Max(denominator, DenominatorEpsilon);
    }

    /// <summary>
    /// The checked fragment's <c>Geometric</c>: Schlick-GGX with
    /// <c>k = alpha / 2</c>, under the same roughness clamp.
    /// </summary>
    internal static double Geometry(
        double roughness,
        double normalDotLight,
        double normalDotEye)
    {
        double clampedRoughness = Math.Max(roughness, MinimumRoughness);
        double alpha = clampedRoughness * clampedRoughness;
        double k = alpha * 0.5;
        double geometry = normalDotEye / ((normalDotEye * (1.0 - k)) + k);
        geometry *= normalDotLight / ((normalDotLight * (1.0 - k)) + k);
        return geometry;
    }

    /// <summary>
    /// The checked fragment's specular lobe, without its Fresnel factor.
    /// </summary>
    /// <remarks>
    /// Exists so that the table's estimator and the direct lobe can be gated
    /// against the same expression rather than against two transcriptions of it.
    /// </remarks>
    internal static double SpecularLobe(
        double roughness,
        double normalDotLight,
        double normalDotEye,
        double normalDotHalf)
    {
        double distribution = Distribution(roughness, normalDotHalf);
        double geometry = Geometry(roughness, normalDotLight, normalDotEye);
        return distribution * geometry /
            Math.Max(4.0 * normalDotLight * normalDotEye, DenominatorEpsilon);
    }

    /// <summary>Builds the packed table at an explicit size and sample count.</summary>
    internal static byte[] Build(uint size, int sampleCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 2u);
        byte[] pixels = new byte[checked((int)(size * size * 8))];
        Span<Half> halves = MemoryMarshal.Cast<byte, Half>(pixels.AsSpan());
        for (int row = 0; row < size; row++)
        {
            double roughness = (row + 0.5) / size;
            for (int column = 0; column < size; column++)
            {
                double normalDotEye = (column + 0.5) / size;
                Vector2 term = Integrate(normalDotEye, roughness, sampleCount);
                int offset = ((row * (int)size) + column) * 4;
                halves[offset] = (Half)term.X;
                halves[offset + 1] = (Half)term.Y;
                halves[offset + 2] = (Half)0f;
                halves[offset + 3] = (Half)1f;
            }
        }
        return pixels;
    }

    /// <summary>
    /// Reads the packed table the way the shader's clamped bilinear sampler does.
    /// </summary>
    internal static Vector2 Sample(
        byte[] pixels,
        uint size,
        float normalDotEye,
        float roughness)
    {
        ReadOnlySpan<Half> halves = MemoryMarshal.Cast<byte, Half>(pixels);
        float x = (Math.Clamp(normalDotEye, 0f, 1f) * size) - 0.5f;
        float y = (Math.Clamp(roughness, 0f, 1f) * size) - 0.5f;
        int x0 = Math.Clamp((int)MathF.Floor(x), 0, (int)size - 1);
        int y0 = Math.Clamp((int)MathF.Floor(y), 0, (int)size - 1);
        int x1 = Math.Min(x0 + 1, (int)size - 1);
        int y1 = Math.Min(y0 + 1, (int)size - 1);
        float fx = Math.Clamp(x - x0, 0f, 1f);
        float fy = Math.Clamp(y - y0, 0f, 1f);
        Vector2 c00 = Read(halves, size, x0, y0);
        Vector2 c10 = Read(halves, size, x1, y0);
        Vector2 c01 = Read(halves, size, x0, y1);
        Vector2 c11 = Read(halves, size, x1, y1);
        return Vector2.Lerp(Vector2.Lerp(c00, c10, fx), Vector2.Lerp(c01, c11, fx), fy);
    }

    private static Vector2 Read(ReadOnlySpan<Half> halves, uint size, int column, int row)
    {
        int offset = ((row * (int)size) + column) * 4;
        return new Vector2((float)halves[offset], (float)halves[offset + 1]);
    }

    /// <summary>The deterministic point set the integration uses.</summary>
    private static (double U, double V) Hammersley(int index, int count)
    {
        uint bits = (uint)index;
        bits = (bits << 16) | (bits >> 16);
        bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
        bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
        bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
        bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);
        return ((double)index / count, bits * 2.3283064365386963e-10);
    }
}

/// <summary>
/// Builds the deterministic prefiltered environment a set of textured dome lights
/// reduces to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Prefilter policy.</b> Every step is a fixed-order double-precision
/// quadrature over a fixed lattice -- there is no importance sampling, no random
/// or quasi-random sequence, and no iteration count that depends on the input --
/// so the same authored scene produces the same bytes on every machine and every
/// run.
/// </para>
/// <para>
/// <b>Step one: resample into world space.</b> Each dome's decoded image is
/// traversed once, and the enumeration is lazy so a caller can release one image
/// before opening the next. Every source texel's direction is taken through the
/// dome's authored light-to-world rotation and accumulated into the world-space
/// radiance bin it lands in, weighted by the texel's own solid angle. Scattering
/// rather than gathering is what preserves energy when the source is far finer
/// than the bin lattice: a one-texel sun contributes its full radiance times its
/// full solid angle to exactly one bin instead of being missed by a point sample.
/// A bin no source texel landed in -- possible only when the source is coarser
/// than the lattice -- is filled by sampling the source at the bin's own centre
/// direction, so coverage is total either way.
/// </para>
/// <para>
/// <b>Step two: diffuse irradiance.</b> <c>E(n) = sum L(l) max(0, n.l) dw</c>
/// over every radiance bin. For a constant environment this returns exactly
/// <c>pi * L</c>, which is the identity the shader's Lambert normalization
/// expects, and it is asserted directly.
/// </para>
/// <para>
/// <b>Step three: specular roughness slices.</b> Slice 0 is the radiance lattice
/// itself, which is the mirror direction at roughness 0. Slice <c>i</c> of
/// <c>n</c> is prefiltered for <c>roughness = i / (n - 1)</c> with the GGX normal
/// distribution evaluated at the half vector between the slice's reflection
/// direction and each radiance bin, under the standard split-sum assumption
/// <c>N = V = R</c>. Every slice keeps the same angular resolution, so the
/// roughest one is a genuine hemisphere-wide integration rather than the single
/// texel a collapsing mip chain would leave. The weights are normalized by their
/// own sum, so a constant environment prefilters to itself at every roughness and
/// no energy is created or lost by the lattice.
/// </para>
/// </remarks>
internal static class SilkEnvironmentPrefilter
{
    /// <summary>
    /// Returns the bytes a described image decodes to.
    /// </summary>
    /// <remarks>
    /// The one statement of decoded cost, shared by the prefiltered path and the
    /// mean-radiance fallback. Two estimates would let a dome be refused by one
    /// path on a size the other accepted, which is a state with no correct
    /// behaviour: the fallback is exactly where a refused dome lands.
    /// </remarks>
    /// <exception cref="InvalidDataException">
    /// The description has a zero dimension.
    /// </exception>
    /// <exception cref="OverflowException">
    /// The product does not fit a <see cref="ulong"/>.
    /// </exception>
    internal static ulong EstimateDecodedBytes(SilkImageDescription description)
    {
        if (description.Width == 0 || description.Height == 0)
        {
            throw new InvalidDataException(
                "An environment image must have a non-zero width and height.");
        }
        return checked(
            (ulong)description.Width *
            description.Height *
            SilkTextureFormats.GetBytesPerPixel(description.Format));
    }

    /// <summary>
    /// Validates one decoded source before it is accumulated, while the caller
    /// still knows which dome it came from.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="Build"/>. A malformed or non-finite
    /// image discovered halfway through the accumulation could only fail the whole
    /// composed environment, which would let one broken dome take the directional
    /// response away from every valid one. Validating here lets the caller drop
    /// exactly the dome that is broken.
    /// </remarks>
    /// <exception cref="InvalidDataException">
    /// The image is empty, mis-sized or carries a non-finite texel.
    /// </exception>
    internal static void ValidateSource(SilkDecodedImage image, SilkColorSpace colorSpace)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Width == 0 || image.Height == 0)
        {
            throw new InvalidDataException(
                "An environment image must have a non-zero width and height.");
        }

        int width = checked((int)image.Width);
        int height = checked((int)image.Height);
        ReadOnlySpan<byte> bytes = image.Pixels;
        ValidateLength(image, width, height, bytes.Length);
        if (image.Format != SilkTextureFormat.Rgba32Float)
        {
            // Eight-bit texels cannot be non-finite, so the length check above is
            // the whole of their validity.
            return;
        }

        ReadOnlySpan<float> floats = MemoryMarshal.Cast<byte, float>(bytes);
        bool linearize = colorSpace == SilkColorSpace.Srgb;
        for (int texel = 0; texel < floats.Length; texel += 4)
        {
            _ = ReadSourceTexel(bytes, floats, texel, linearize);
        }
    }

    /// <summary>Builds the prefiltered environment for one set of dome lights.</summary>
    /// <param name="sources">
    /// The decoded, oriented and scaled dome lights, enumerated lazily so that a
    /// caller can open and release one decoded image at a time.
    /// </param>
    /// <param name="options">The bounded output shape and its budget.</param>
    /// <param name="perDomeGroups">
    /// Whether to bake one independently selectable group per dome beside the
    /// composed one, which is what a per-draw dome link mask selects between.
    /// </param>
    /// <remarks>
    /// <paramref name="perDomeGroups"/> is deliberately a decision of the caller
    /// rather than something inferred from the source count. A scene that authors
    /// no dome collection must keep the single-group layout: the composed bake is
    /// then the whole atlas, addressed by exactly the texture coordinates it was
    /// addressed by before dome linking existed, so its pixels are the pixels it
    /// always produced rather than ones that merely round to the same place.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="sources"/> is empty.</exception>
    /// <exception cref="SilkEnvironmentBudgetExceededException">
    /// The requested output shape exceeds the prefiltered byte budget.
    /// </exception>
    internal static SilkEnvironmentMaps Build(
        IEnumerable<SilkEnvironmentSource> sources,
        SilkEnvironmentPrefilterOptions options,
        bool perDomeGroups = false)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(options);

        // Checked before a single output array exists, which is the only order in
        // which a byte budget bounds anything. The grouped footprint is checked
        // again below, once the number of domes -- and therefore of groups -- is
        // known, and still before any output array is allocated.
        options.Validate();

        uint width = options.RadianceWidth;
        uint height = options.RadianceHeight;
        int binCount = checked((int)(width * height));
        double[] diffuseBase = new double[binCount * 3];
        double[] specularBase = new double[binCount * 3];
        double[] domeBins = new double[binCount * 3];
        double[] domeWeights = new double[binCount];
        var domeDiffuse = new List<double[]>();
        var domeSpecular = new List<double[]>();

        int domeCount = 0;
        foreach (SilkEnvironmentSource source in sources)
        {
            if (domeCount == options.MaximumDomeLights)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sources),
                    domeCount + 1,
                    "More dome lights were composed than the configured bound admits.");
            }
            domeCount++;
            Array.Clear(domeBins);
            Array.Clear(domeWeights);
            Resample(source, width, height, domeBins, domeWeights);
            double[]? ownDiffuse = perDomeGroups ? new double[binCount * 3] : null;
            double[]? ownSpecular = perDomeGroups ? new double[binCount * 3] : null;
            for (int bin = 0; bin < binCount; bin++)
            {
                int offset = bin * 3;
                double red = domeBins[offset];
                double green = domeBins[offset + 1];
                double blue = domeBins[offset + 2];
                double diffuseRed = red * source.DiffuseScale.X;
                double diffuseGreen = green * source.DiffuseScale.Y;
                double diffuseBlue = blue * source.DiffuseScale.Z;
                double specularRed = red * source.SpecularScale.X;
                double specularGreen = green * source.SpecularScale.Y;
                double specularBlue = blue * source.SpecularScale.Z;
                diffuseBase[offset] += diffuseRed;
                diffuseBase[offset + 1] += diffuseGreen;
                diffuseBase[offset + 2] += diffuseBlue;
                specularBase[offset] += specularRed;
                specularBase[offset + 1] += specularGreen;
                specularBase[offset + 2] += specularBlue;
                if (ownDiffuse is null || ownSpecular is null)
                {
                    continue;
                }
                ownDiffuse[offset] = diffuseRed;
                ownDiffuse[offset + 1] = diffuseGreen;
                ownDiffuse[offset + 2] = diffuseBlue;
                ownSpecular[offset] = specularRed;
                ownSpecular[offset + 1] = specularGreen;
                ownSpecular[offset + 2] = specularBlue;
            }
            if (ownDiffuse is not null && ownSpecular is not null)
            {
                domeDiffuse.Add(ownDiffuse);
                domeSpecular.Add(ownSpecular);
            }
        }

        if (domeCount == 0)
        {
            throw new ArgumentException(
                "A prefiltered environment requires at least one dome light.",
                nameof(sources));
        }

        // The composed bake is always the last group, so a scene with no dome
        // linking has exactly one group and it is that one. Group g < domeCount is
        // the dome at index g of the composed set.
        int groupCount = perDomeGroups ? domeCount + 1 : 1;
        options.ValidateGroupBudget(groupCount);
        domeDiffuse.Add(diffuseBase);
        domeSpecular.Add(specularBase);

        byte[] irradiance = BuildIrradianceGroups(
            perDomeGroups ? domeDiffuse : [diffuseBase],
            width,
            height,
            options.IrradianceWidth,
            options.IrradianceHeight);
        byte[] specular = BuildSpecularGroups(
            perDomeGroups ? domeSpecular : [specularBase],
            width,
            height,
            options.SpecularSliceCount);
        return new SilkEnvironmentMaps(
            options.IrradianceWidth,
            options.IrradianceHeight,
            irradiance,
            width,
            height,
            options.SpecularSliceCount,
            specular,
            domeCount,
            (uint)groupCount,
            (uint)(groupCount - 1));
    }

    /// <summary>Bakes one irradiance image per group into one stacked atlas.</summary>
    private static byte[] BuildIrradianceGroups(
        List<double[]> groups,
        uint radianceWidth,
        uint radianceHeight,
        uint width,
        uint height)
    {
        if (groups.Count == 1)
        {
            return BuildIrradiance(groups[0], radianceWidth, radianceHeight, width, height);
        }

        int groupBytes = checked((int)(width * height * 8));
        byte[] pixels = new byte[checked(groupBytes * groups.Count)];
        for (int group = 0; group < groups.Count; group++)
        {
            byte[] baked = BuildIrradiance(
                groups[group],
                radianceWidth,
                radianceHeight,
                width,
                height);
            baked.CopyTo(pixels, group * groupBytes);
        }
        return pixels;
    }

    /// <summary>Bakes one roughness stack per group into one stacked atlas.</summary>
    private static byte[] BuildSpecularGroups(
        List<double[]> groups,
        uint width,
        uint height,
        uint sliceCount)
    {
        if (groups.Count == 1)
        {
            return BuildSpecularSlices(groups[0], width, height, sliceCount);
        }

        int groupBytes = checked((int)(width * height * sliceCount * 8));
        byte[] pixels = new byte[checked(groupBytes * groups.Count)];
        for (int group = 0; group < groups.Count; group++)
        {
            byte[] baked = BuildSpecularSlices(groups[group], width, height, sliceCount);
            baked.CopyTo(pixels, group * groupBytes);
        }
        return pixels;
    }

    /// <summary>
    /// Accumulates one dome's decoded image into world-space radiance bins,
    /// honouring its authored orientation.
    /// </summary>
    private static void Resample(
        SilkEnvironmentSource source,
        uint width,
        uint height,
        double[] bins,
        double[] weights)
    {
        SilkDecodedImage image = source.Image;
        ArgumentNullException.ThrowIfNull(image);
        int sourceWidth = checked((int)image.Width);
        int sourceHeight = checked((int)image.Height);
        bool linearize = source.ColorSpace == SilkColorSpace.Srgb;
        ReadOnlySpan<byte> bytes = image.Pixels;
        ValidateLength(image, sourceWidth, sourceHeight, bytes.Length);
        ReadOnlySpan<float> floats = image.Format == SilkTextureFormat.Rgba32Float
            ? MemoryMarshal.Cast<byte, float>(bytes)
            : default;

        Matrix4x4 rotation = ExtractRotation(source.LightToWorld);
        for (int row = 0; row < sourceHeight; row++)
        {
            double weight = SilkEnvironmentLatLong.GetSolidAngle(
                row,
                (uint)sourceWidth,
                (uint)sourceHeight);
            if (weight <= 0)
            {
                continue;
            }
            for (int column = 0; column < sourceWidth; column++)
            {
                Vector3 radiance = ReadSourceTexel(
                    bytes,
                    floats,
                    ((row * sourceWidth) + column) * 4,
                    linearize);
                Vector3 local = SilkEnvironmentLatLong.Unproject(
                    column,
                    row,
                    (uint)sourceWidth,
                    (uint)sourceHeight);
                Vector3 world = Vector3.TransformNormal(local, rotation);
                Vector2 uv = SilkEnvironmentLatLong.Project(world);
                int binColumn = Math.Clamp((int)(uv.X * width), 0, (int)width - 1);
                int binRow = Math.Clamp((int)(uv.Y * height), 0, (int)height - 1);
                int bin = (binRow * (int)width) + binColumn;
                int offset = bin * 3;
                bins[offset] += radiance.X * weight;
                bins[offset + 1] += radiance.Y * weight;
                bins[offset + 2] += radiance.Z * weight;
                weights[bin] += weight;
            }
        }

        // Normalize, and gather-fill every bin the scatter missed. A bin can only
        // be empty when the source is coarser than the lattice, and leaving it at
        // zero would punch a black hole into an otherwise uniform sky.
        Matrix4x4 inverse = Matrix4x4.Transpose(rotation);
        for (int bin = 0; bin < weights.Length; bin++)
        {
            int offset = bin * 3;
            if (weights[bin] > 0)
            {
                double scale = 1.0 / weights[bin];
                bins[offset] *= scale;
                bins[offset + 1] *= scale;
                bins[offset + 2] *= scale;
                continue;
            }

            Vector3 world = SilkEnvironmentLatLong.Unproject(
                bin % (int)width,
                bin / (int)width,
                width,
                height);
            Vector3 local = Vector3.TransformNormal(world, inverse);
            Vector2 uv = SilkEnvironmentLatLong.Project(local);
            int column = Math.Clamp((int)(uv.X * sourceWidth), 0, sourceWidth - 1);
            int row = Math.Clamp((int)(uv.Y * sourceHeight), 0, sourceHeight - 1);
            Vector3 radiance = ReadSourceTexel(
                bytes,
                floats,
                ((row * sourceWidth) + column) * 4,
                linearize);
            bins[offset] = radiance.X;
            bins[offset + 1] = radiance.Y;
            bins[offset + 2] = radiance.Z;
        }
    }

    private static byte[] BuildIrradiance(
        double[] radiance,
        uint radianceWidth,
        uint radianceHeight,
        uint width,
        uint height)
    {
        int binCount = checked((int)(radianceWidth * radianceHeight));
        (Vector3[] directions, double[] solidAngles) =
            BuildLattice(radianceWidth, radianceHeight);

        byte[] pixels = new byte[checked((int)(width * height * 8))];
        Span<Half> halves = MemoryMarshal.Cast<byte, Half>(pixels.AsSpan());
        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                Vector3 normal = SilkEnvironmentLatLong.Unproject(column, row, width, height);
                double red = 0;
                double green = 0;
                double blue = 0;
                for (int bin = 0; bin < binCount; bin++)
                {
                    double cosine = Vector3.Dot(normal, directions[bin]);
                    if (cosine <= 0)
                    {
                        continue;
                    }
                    double weight = cosine * solidAngles[bin];
                    int offset = bin * 3;
                    red += radiance[offset] * weight;
                    green += radiance[offset + 1] * weight;
                    blue += radiance[offset + 2] * weight;
                }
                Write(halves, ((row * (int)width) + column) * 4, red, green, blue);
            }
        }
        return pixels;
    }

    private static byte[] BuildSpecularSlices(
        double[] radiance,
        uint width,
        uint height,
        uint sliceCount)
    {
        int sliceTexels = checked((int)(width * height));
        byte[] pixels = new byte[checked(sliceTexels * (int)sliceCount * 8)];
        Span<Half> halves = MemoryMarshal.Cast<byte, Half>(pixels.AsSpan());

        // Slice 0 is the mirror direction, so it is the radiance lattice itself
        // rather than a convolution of it with a delta kernel that no finite
        // lattice can represent.
        for (int bin = 0; bin < sliceTexels; bin++)
        {
            int offset = bin * 3;
            Write(halves, bin * 4, radiance[offset], radiance[offset + 1], radiance[offset + 2]);
        }

        (Vector3[] directions, double[] solidAngles) = BuildLattice(width, height);
        for (uint slice = 1; slice < sliceCount; slice++)
        {
            double roughness = (double)slice / (sliceCount - 1);
            int sliceOffset = checked((int)slice * sliceTexels * 4);
            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    Vector3 reflection =
                        SilkEnvironmentLatLong.Unproject(column, row, width, height);
                    double red = 0;
                    double green = 0;
                    double blue = 0;
                    double total = 0;
                    for (int bin = 0; bin < sliceTexels; bin++)
                    {
                        double cosine = Vector3.Dot(reflection, directions[bin]);
                        if (cosine <= 0)
                        {
                            continue;
                        }
                        // With N = V = R the half vector between R and the sampled
                        // direction has cos(theta_h) = sqrt((1 + R.l) / 2).
                        //
                        // Evaluated through the shared lobe rather than restated
                        // here, so the prefilter, the split-sum table and the
                        // direct lobe are one distribution written once. It had
                        // been restated, which is how it kept Storm's
                        // catastrophically cancelling grouping after the other two
                        // were corrected.
                        double normalDotHalf = Math.Sqrt((1.0 + cosine) * 0.5);
                        double distribution =
                            SilkEnvironmentBrdf.Distribution(roughness, normalDotHalf);
                        double weight = distribution * cosine * solidAngles[bin];
                        if (weight <= 0)
                        {
                            continue;
                        }
                        int offset = bin * 3;
                        red += radiance[offset] * weight;
                        green += radiance[offset + 1] * weight;
                        blue += radiance[offset + 2] * weight;
                        total += weight;
                    }

                    int destination = sliceOffset + (((row * (int)width) + column) * 4);
                    if (total > 0)
                    {
                        Write(halves, destination, red / total, green / total, blue / total);
                        continue;
                    }

                    // Only reachable if every weight underflowed, which a very
                    // narrow lobe on a coarse lattice can do. The mirror sample is
                    // the correct limit of the integral in exactly that case.
                    int nearest = NearestBin(reflection, width, height) * 3;
                    Write(
                        halves,
                        destination,
                        radiance[nearest],
                        radiance[nearest + 1],
                        radiance[nearest + 2]);
                }
            }
        }
        return pixels;
    }

    private static (Vector3[] Directions, double[] SolidAngles) BuildLattice(
        uint width,
        uint height)
    {
        int count = checked((int)(width * height));
        var directions = new Vector3[count];
        var solidAngles = new double[count];
        for (int bin = 0; bin < count; bin++)
        {
            directions[bin] = SilkEnvironmentLatLong.Unproject(
                bin % (int)width,
                bin / (int)width,
                width,
                height);
            solidAngles[bin] = SilkEnvironmentLatLong.GetSolidAngle(
                bin / (int)width,
                width,
                height);
        }
        return (directions, solidAngles);
    }

    private static int NearestBin(Vector3 direction, uint width, uint height)
    {
        Vector2 uv = SilkEnvironmentLatLong.Project(direction);
        int column = Math.Clamp((int)(uv.X * width), 0, (int)width - 1);
        int row = Math.Clamp((int)(uv.Y * height), 0, (int)height - 1);
        return (row * (int)width) + column;
    }

    private static void Write(Span<Half> halves, int index, double red, double green, double blue)
    {
        halves[index] = ToHalf(red);
        halves[index + 1] = ToHalf(green);
        halves[index + 2] = ToHalf(blue);
        halves[index + 3] = (Half)1f;
    }

    private static Half ToHalf(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new InvalidDataException(
                "The prefiltered environment produced a non-finite value.");
        }
        return (Half)(float)Math.Clamp(value, 0, SilkEnvironmentMaps.MaximumHalf);
    }

    private static Vector3 ReadSourceTexel(
        ReadOnlySpan<byte> bytes,
        ReadOnlySpan<float> floats,
        int texel,
        bool linearize)
    {
        float red;
        float green;
        float blue;
        if (floats.IsEmpty)
        {
            red = bytes[texel] / 255f;
            green = bytes[texel + 1] / 255f;
            blue = bytes[texel + 2] / 255f;
        }
        else
        {
            red = floats[texel];
            green = floats[texel + 1];
            blue = floats[texel + 2];
        }

        if (linearize)
        {
            red = SrgbToLinear(red);
            green = SrgbToLinear(green);
            blue = SrgbToLinear(blue);
        }

        if (!float.IsFinite(red) || !float.IsFinite(green) || !float.IsFinite(blue))
        {
            throw new InvalidDataException(
                "The environment image contains a non-finite texel.");
        }
        return new Vector3(red, green, blue);
    }

    private static float SrgbToLinear(float value) =>
        value <= 0.04045f
            ? value / 12.92f
            : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

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

    /// <summary>
    /// Extracts an orthonormal rotation from a dome's light-to-world transform.
    /// </summary>
    /// <remarks>
    /// A dome light's transform may carry translation and scale that mean nothing
    /// to an infinitely distant sky, and a non-uniform scale would otherwise skew
    /// the sampled directions and rotate the sky by a different amount at
    /// different latitudes. The basis is normalized and re-orthogonalized so that
    /// only the orientation survives; a degenerate basis falls back to identity
    /// rather than producing directions that are not unit length.
    /// </remarks>
    private static Matrix4x4 ExtractRotation(Matrix4x4 transform)
    {
        Vector3 x = new(transform.M11, transform.M12, transform.M13);
        Vector3 y = new(transform.M21, transform.M22, transform.M23);
        Vector3 z = new(transform.M31, transform.M32, transform.M33);
        if (!IsUsable(x) || !IsUsable(y) || !IsUsable(z))
        {
            return Matrix4x4.Identity;
        }

        x = Vector3.Normalize(x);
        z = Vector3.Normalize(Vector3.Cross(x, y));
        y = Vector3.Cross(z, x);
        if (!IsUsable(x) || !IsUsable(y) || !IsUsable(z))
        {
            return Matrix4x4.Identity;
        }
        return new Matrix4x4(
            x.X, x.Y, x.Z, 0,
            y.X, y.Y, y.Z, 0,
            z.X, z.Y, z.Z, 0,
            0, 0, 0, 1);
    }

    private static bool IsUsable(Vector3 value)
    {
        float lengthSquared = value.LengthSquared();
        return float.IsFinite(lengthSquared) && lengthSquared > 1e-12f;
    }
}

/// <summary>
/// The identity one prefiltered environment is a function of.
/// </summary>
/// <remarks>
/// <para>
/// The prefilter is a pure function of the decoded images, the authored dome
/// controls and orientations, and the output shape, so the identity has to name
/// all three or a cache hit can return an environment the scene no longer
/// describes. It names four things, in the order they can change:
/// </para>
/// <list type="bullet">
/// <item><b>Asset.</b> The resolved <c>texture:file</c> path of every dome, and
/// the colour space each is read in, because the same file read raw and as sRGB
/// is two different environments.</item>
/// <item><b>File.</b> The length and last-write time of every asset that exists
/// on disk. This is what makes an edited HDR invalidate: the path is unchanged,
/// so nothing else in the identity moves, and a cache keyed on the path alone
/// would serve the previous sky forever. The stamps are re-read on every resolve,
/// not only when a scene command arrives, so a file repaired or re-exported under
/// a running session is observed without any authoring at all.</item>
/// <item><b>Context.</b> A caller-supplied token naming the backend and the
/// resource set the maps were produced for, so a rebuilt device or a substituted
/// decoder cannot inherit another one's entries.</item>
/// <item><b>Revision.</b> The authored dome payload -- prim path, orientation and
/// every emission control -- and the output shape. This is the revision in the
/// sense that matters here: it changes on exactly the edits that change the
/// bytes, rather than on every scene revision, which is what lets re-authoring an
/// unrelated prim reuse the entry.</item>
/// </list>
/// </remarks>
internal static class SilkEnvironmentIdentity
{
    /// <summary>Composes the cache identity of one prefiltered environment.</summary>
    /// <param name="context">The backend and resource-set context token.</param>
    /// <param name="domes">The dome lights composed, in their published order.</param>
    /// <param name="stamps">The file stamp of each dome's texture asset.</param>
    /// <param name="options">The bounded output shape.</param>
    /// <param name="perDomeGroups">
    /// Whether the bake carries one selectable group per dome. It is part of the
    /// identity because two bakes of the same domes with and without groups are
    /// two different payloads with two different atlas shapes, and serving one for
    /// the other would sample a group index that does not exist.
    /// </param>
    internal static string Compose(
        string context,
        IReadOnlyList<SilkEnvironmentData> domes,
        IReadOnlyList<SilkEnvironmentAssetStamp> stamps,
        SilkEnvironmentPrefilterOptions options,
        bool perDomeGroups = false)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(domes);
        ArgumentNullException.ThrowIfNull(stamps);
        ArgumentNullException.ThrowIfNull(options);
        if (domes.Count != stamps.Count)
        {
            throw new ArgumentException(
                "Every dome light must carry exactly one asset stamp.",
                nameof(stamps));
        }

        var builder = new StringBuilder(256);
        builder.Append(context).Append('\u001f');
        builder.Append(options.RadianceWidth.ToString(CultureInfo.InvariantCulture)).Append('x');
        builder.Append(options.IrradianceWidth.ToString(CultureInfo.InvariantCulture)).Append('x');
        builder.Append(options.SpecularSliceCount.ToString(CultureInfo.InvariantCulture));
        builder.Append(perDomeGroups ? "xg" : "xc");
        for (int index = 0; index < domes.Count; index++)
        {
            SilkEnvironmentData dome = domes[index];
            SilkEnvironmentAssetStamp stamp = stamps[index];
            builder.Append('\u001e');
            builder.Append(dome.Path).Append('\u001f');
            builder.Append(dome.TexturePath).Append('\u001f');
            builder.Append(((int)dome.SourceColorSpace).ToString(CultureInfo.InvariantCulture));
            builder.Append('\u001f');
            builder.Append(stamp.Length.ToString(CultureInfo.InvariantCulture)).Append('\u001f');
            builder.Append(stamp.LastWriteUtcTicks.ToString(CultureInfo.InvariantCulture));
            AppendFloat(builder, dome.Color.X);
            AppendFloat(builder, dome.Color.Y);
            AppendFloat(builder, dome.Color.Z);
            AppendFloat(builder, dome.Intensity);
            AppendFloat(builder, dome.Exposure);
            AppendFloat(builder, dome.Diffuse);
            AppendFloat(builder, dome.Specular);
            ReadOnlySpan<double> transform = dome.Transform.Span;
            for (int element = 0; element < transform.Length; element++)
            {
                builder.Append('\u001f');
                builder.Append(BitConverter.DoubleToInt64Bits(transform[element])
                    .ToString(CultureInfo.InvariantCulture));
            }
        }
        return builder.ToString();
    }

    private static void AppendFloat(StringBuilder builder, float value)
    {
        builder.Append('\u001f');
        builder.Append(BitConverter.SingleToInt32Bits(value).ToString(CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// The length and last-write time of one environment asset, or the absent stamp
/// when the file cannot be observed.
/// </summary>
/// <remarks>
/// An asset a resolver serves from something other than the local file system has
/// no stamp, and <see cref="Unavailable"/> is the honest answer: the identity then
/// depends on the path alone, so such an asset is re-read only when its path or
/// its controls change. That is stated here rather than hidden behind a zero that
/// would read as "an empty file".
/// </remarks>
internal readonly record struct SilkEnvironmentAssetStamp(long Length, long LastWriteUtcTicks)
{
    /// <summary>Gets the stamp used when a file cannot be observed.</summary>
    internal static SilkEnvironmentAssetStamp Unavailable => new(-1, -1);

    /// <summary>Reads one asset's stamp from the local file system.</summary>
    internal static SilkEnvironmentAssetStamp Read(string asset)
    {
        try
        {
            var info = new FileInfo(asset);
            return info.Exists
                ? new SilkEnvironmentAssetStamp(info.Length, info.LastWriteTimeUtc.Ticks)
                : Unavailable;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or
                NotSupportedException)
        {
            return Unavailable;
        }
    }
}

/// <summary>
/// A bounded least-recently-used cache of prefiltered environments.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the mean-radiance cache beside it, the retained value here is not three
/// floats: it is the full prefiltered payload, so the cache carries a byte budget
/// of its own and evicts on either bound. The entry count bound exists because a
/// stage that cycles environment variants would otherwise grow the table forever;
/// the byte bound exists because the entry count alone says nothing about
/// footprint once the output shape is configurable.
/// </para>
/// <para>
/// A build that throws is never recorded, so an environment whose asset was
/// missing or malformed is retried on the next resolve rather than cached as a
/// failure that outlives the repair.
/// </para>
/// </remarks>
internal sealed class SilkEnvironmentLightingCache
{
    /// <summary>The default number of retained prefiltered environments.</summary>
    internal const int DefaultCapacity = 4;

    /// <summary>The default retained payload ceiling, in bytes.</summary>
    internal const ulong DefaultByteBudget = 64UL * 1024 * 1024;

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly int _capacity;
    private readonly ulong _byteBudget;
    private ulong _clock;
    private ulong _bytes;

    internal SilkEnvironmentLightingCache(
        int capacity = DefaultCapacity,
        ulong byteBudget = DefaultByteBudget)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfZero(byteBudget);
        _capacity = capacity;
        _byteBudget = byteBudget;
    }

    /// <summary>Gets the number of retained environments.</summary>
    internal int Count => _entries.Count;

    /// <summary>Gets the retained payload byte count.</summary>
    internal ulong Bytes => _bytes;

    /// <summary>Gets the number of prefilter builds performed since construction.</summary>
    internal int BuildCount { get; private set; }

    /// <summary>Gets the number of entries evicted since construction.</summary>
    internal int EvictionCount { get; private set; }

    /// <summary>Returns the prefiltered environment for one identity, building it once.</summary>
    /// <param name="identity">The composed cache identity.</param>
    /// <param name="build">The prefilter, invoked at most once per identity.</param>
    /// <exception cref="SilkEnvironmentBudgetExceededException">
    /// One built environment alone exceeds the cache byte budget.
    /// </exception>
    internal SilkEnvironmentMaps GetOrBuild(string identity, Func<SilkEnvironmentMaps> build)
    {
        ArgumentException.ThrowIfNullOrEmpty(identity);
        ArgumentNullException.ThrowIfNull(build);
        if (TryGet(identity) is { } cached)
        {
            return cached;
        }

        SilkEnvironmentMaps maps = build();
        BuildCount++;
        Add(identity, maps);
        return maps;
    }

    /// <summary>Returns a retained environment, or <c>null</c>.</summary>
    /// <remarks>
    /// Separated from <see cref="GetOrBuild"/> because a caller that streams its
    /// sources cannot know the final identity until the stream has been consumed:
    /// a dome that failed to decode is not in the environment that was built, and
    /// keying it under an identity that names that dome would return a payload
    /// missing a dome the next lookup believes is present. Such a caller looks up
    /// the preflighted identity, builds, and then adds under the identity of the
    /// domes actually consumed -- one pass, no source decoded twice.
    /// </remarks>
    internal SilkEnvironmentMaps? TryGet(string identity)
    {
        ArgumentException.ThrowIfNullOrEmpty(identity);
        if (_entries.TryGetValue(identity, out Entry? cached))
        {
            cached.LastUsed = ++_clock;
            return cached.Maps;
        }
        return null;
    }

    /// <summary>Retains one built environment under its identity.</summary>
    /// <exception cref="SilkEnvironmentBudgetExceededException">
    /// The environment alone exceeds the cache byte budget.
    /// </exception>
    internal void Add(string identity, SilkEnvironmentMaps maps)
    {
        ArgumentException.ThrowIfNullOrEmpty(identity);
        ArgumentNullException.ThrowIfNull(maps);
        if (_entries.TryGetValue(identity, out Entry? existing))
        {
            existing.LastUsed = ++_clock;
            return;
        }

        ulong size = maps.ByteSize;
        if (size > _byteBudget)
        {
            throw new SilkEnvironmentBudgetExceededException(identity, size, _byteBudget);
        }

        Evict(size);
        _entries[identity] = new Entry(maps) { LastUsed = ++_clock };
        _bytes += size;
    }

    /// <summary>Records that one prefilter build was performed by the caller.</summary>
    internal void CountBuild() => BuildCount++;

    /// <summary>Drops every retained environment.</summary>
    internal void Clear()
    {
        _entries.Clear();
        _bytes = 0;
    }

    private void Evict(ulong incoming)
    {
        while (_entries.Count > 0 &&
            (_entries.Count >= _capacity || _bytes + incoming > _byteBudget))
        {
            string? oldestKey = null;
            ulong oldest = ulong.MaxValue;
            foreach (KeyValuePair<string, Entry> pair in _entries)
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
            _bytes -= _entries[oldestKey].Maps.ByteSize;
            _entries.Remove(oldestKey);
            EvictionCount++;
        }
    }

    private sealed class Entry(SilkEnvironmentMaps maps)
    {
        internal SilkEnvironmentMaps Maps { get; } = maps;

        internal ulong LastUsed { get; set; }
    }
}

/// <summary>
/// The resolved environment block the frame constants carry.
/// </summary>
/// <param name="Enabled">
/// Whether the prefiltered environment resources are live for this frame.
/// </param>
/// <param name="SpecularSliceCount">
/// The number of prefiltered roughness slices, which is the axis the shader
/// interpolates across.
/// </param>
/// <param name="SpecularSliceHeight">
/// The height of one slice in texels, which fixes the half-texel inset that keeps
/// a slice's bilinear taps out of its neighbours.
/// </param>
/// <param name="AuthoredSceneLighting">
/// Whether the scene authors at least one dome light, whether or not this
/// renderer resolved any of them into a prefiltered environment.
/// </param>
/// <param name="GroupCount">
/// The number of independently selectable environment groups the atlases carry.
/// One when the scene links no dome, in which case the composed bake is the whole
/// atlas.
/// </param>
/// <param name="ComposedGroup">
/// The group index of the all-domes bake, which is what a prim linked to every
/// dome reads.
/// </param>
/// <param name="IrradianceSliceHeight">
/// The height of one irradiance group in texels, which fixes the half-texel inset
/// that keeps a group's bilinear taps out of its neighbours.
/// </param>
/// <param name="DomeGroups">
/// The environment group each dome bit resolves to, or <c>-1</c> for a dome that
/// has no prefiltered contribution. Indexed by dome bit, not by group.
/// </param>
/// <remarks>
/// <see cref="AuthoredSceneLighting"/> is deliberately not <see cref="Enabled"/>,
/// and deliberately not inferred from a non-zero ambient term. A dome authored
/// black, a dome authored specular-only, and a dome the prefilter refused all
/// resolve to a zero ambient and a disabled environment -- and a headlight keyed
/// on either of those would switch itself on for a stage that is lit exactly as
/// its author asked, replacing the author's dome with a camera light.
/// </remarks>
internal readonly record struct SilkEnvironmentFrameBinding(
    bool Enabled,
    uint SpecularSliceCount,
    uint SpecularSliceHeight,
    bool AuthoredSceneLighting = false,
    uint GroupCount = 1,
    uint ComposedGroup = 0,
    uint IrradianceSliceHeight = 0,
    SilkDomeGroupTable DomeGroups = default)
{
    /// <summary>Gets the block a frame with no dome light at all writes.</summary>
    internal static SilkEnvironmentFrameBinding None => new(false, 0, 0);

    /// <summary>
    /// Gets the block a frame writes when every authored dome fell back.
    /// </summary>
    internal static SilkEnvironmentFrameBinding FallbackOnly => new(false, 0, 0, true);
}

/// <summary>
/// The environment group each dome bit resolves to, as a fixed inline table.
/// </summary>
/// <remarks>
/// A value type with one slot per addressable dome bit rather than an array, so
/// that <see cref="SilkEnvironmentFrameBinding"/> stays comparable by value: the
/// frame constants are re-packed exactly when the binding changes, and an array
/// reference would compare by identity and re-pack on every rebuild that produced
/// the same table.
/// </remarks>
internal readonly struct SilkDomeGroupTable : IEquatable<SilkDomeGroupTable>
{
    /// <summary>The group index a dome with no prefiltered contribution carries.</summary>
    internal const int NoGroup = -1;

    private readonly long _packed;

    private SilkDomeGroupTable(long packed) => _packed = packed;

    /// <summary>Gets the empty table, in which no dome resolves to a group.</summary>
    internal static SilkDomeGroupTable Empty { get; } = new(0);

    /// <summary>Gets the group dome <paramref name="dome"/> resolves to.</summary>
    internal int GetGroup(int dome)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dome);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            dome,
            (int)SilkFrameCommand.MaximumDomes);

        // Stored biased by one so that the default-constructed value -- all zero
        // bits -- is the table in which every dome resolves to no group, which is
        // exactly what a frame with no prefiltered environment publishes.
        int stored = (int)((_packed >> (dome * 8)) & 0xFF);
        return stored == 0 ? NoGroup : stored - 1;
    }

    /// <summary>Returns this table with dome <paramref name="dome"/> set.</summary>
    internal SilkDomeGroupTable WithGroup(int dome, uint group)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dome);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            dome,
            (int)SilkFrameCommand.MaximumDomes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(group, 254u);
        long cleared = _packed & ~(0xFFL << (dome * 8));
        return new SilkDomeGroupTable(cleared | ((long)(group + 1) << (dome * 8)));
    }

    /// <inheritdoc/>
    public bool Equals(SilkDomeGroupTable other) => _packed == other._packed;

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is SilkDomeGroupTable other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _packed.GetHashCode();

    /// <summary>Compares two tables by value.</summary>
    public static bool operator ==(SilkDomeGroupTable left, SilkDomeGroupTable right) =>
        left.Equals(right);

    /// <summary>Compares two tables by value.</summary>
    public static bool operator !=(SilkDomeGroupTable left, SilkDomeGroupTable right) =>
        !left.Equals(right);
}

/// <summary>
/// The mean-radiance ambient term each dome bit contributes on its own.
/// </summary>
/// <remarks>
/// Only a dome the prefiltered environment did <em>not</em> carry has a non-zero
/// entry here: a prefiltered dome contributes an image, and adding a collapsed
/// approximation of it as well would light the scene twice from one dome.
/// </remarks>
internal struct SilkDomeAmbientTable
{
    private DomeAmbientSlots _slots;

    /// <summary>
    /// Gets the fallback ambient of every dome the page could not give a bit to.
    /// </summary>
    /// <remarks>
    /// Zero for every page that publishes a dome table, because a published
    /// environment record must name a present dome for the page to be applied at
    /// all. It exists so the frame writer stays total-preserving on the page that
    /// publishes no dome table, where every dome is unaddressable by construction.
    /// </remarks>
    internal Vector3 Unattributed { get; private set; }

    /// <summary>Gets the fallback ambient dome <paramref name="dome"/> contributes.</summary>
    internal readonly Vector3 GetAmbient(int dome) => _slots[dome];

    /// <summary>Adds one dome's fallback ambient contribution.</summary>
    internal void AddAmbient(int dome, Vector3 value) => _slots[dome] += value;

    /// <summary>Adds the fallback ambient of a dome that holds no bit.</summary>
    internal void AddUnattributed(Vector3 value) => Unattributed += value;

    [System.Runtime.CompilerServices.InlineArray((int)SilkFrameCommand.MaximumDomes)]
    private struct DomeAmbientSlots
    {
        private Vector3 _element;
    }
}
