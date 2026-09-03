// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Text;

namespace OpenUsd.Rendering;

/// <summary>
/// Describes a renderer-neutral colour-managed display transform: an OpenColorIO
/// config plus the display, view, and optional look that convert linear
/// scene-referred colour to display-referred colour.
/// </summary>
/// <remarks>
/// <para>
/// This descriptor is renderer-neutral and immutable. It names *what* transform is
/// wanted; it does not describe how a backend realizes it. A renderer realizes the
/// transform by baking it once into a bounded lattice and sampling that lattice on
/// the GPU, so the transform can be evaluated live at presentation rates without a
/// per-pixel transition to native code.
/// </para>
/// <para>
/// The lattice is indexed through a base-2 logarithmic shaper, which is what lets a
/// bounded table cover an unbounded scene-referred range.
/// <see cref="ShaperMinimumLog2"/> and <see cref="ShaperMaximumLog2"/> are the
/// closed interval of stops the table spans; a channel outside that interval is
/// clamped to the nearest lattice edge rather than extrapolated.
/// </para>
/// <para>
/// Ordering is fixed and documented: <see cref="RenderSettings.Exposure"/> is
/// applied to linear RGB first, then the display transform. It is the same order
/// the CPU export path uses, so a GPU-transformed frame and an exported frame agree
/// on what exposure means.
/// </para>
/// </remarks>
public sealed record RenderDisplayTransform
{
    /// <summary>Gets the smallest supported lattice edge length.</summary>
    public const int MinimumLatticeSize = 8;

    /// <summary>Gets the largest supported lattice edge length.</summary>
    public const int MaximumLatticeSize = 64;

    /// <summary>Gets the default lattice edge length.</summary>
    /// <remarks>
    /// A lattice interpolates between baked samples, so its edge length and its shaper
    /// interval together decide how far an interpolated display code value can sit from
    /// the directly evaluated one. The default pair -- a 64-entry edge over 20 stops,
    /// about a third of a stop between neighbours -- keeps that difference inside a
    /// couple of 8-bit code values for ordinary display transforms, which the
    /// cross-checked GPU-against-CPU conformance gate asserts rather than assumes.
    /// </remarks>
    public const int DefaultLatticeSize = 64;

    /// <summary>Gets the default lower shaper bound in stops.</summary>
    /// <remarks>
    /// Chosen to coincide with the smallest normal half-precision value, because
    /// the lattice is fed to the colour-management library as half-precision data.
    /// </remarks>
    public const float DefaultShaperMinimumLog2 = -14;

    /// <summary>Gets the default upper shaper bound in stops.</summary>
    /// <remarks>
    /// Scene colour brighter than this clamps to the lattice edge rather than
    /// extrapolating. Every display transform this feature targets has already reached
    /// its own maximum long before 64 times diffuse white, so the clamp costs nothing a
    /// display could show, while a wider interval would spend lattice resolution where
    /// no display code value changes.
    /// </remarks>
    public const float DefaultShaperMaximumLog2 = 6;

    /// <summary>Gets the lowest accepted shaper bound in stops.</summary>
    public const float MinimumShaperLog2 = -32;

    /// <summary>Gets the highest accepted shaper bound in stops.</summary>
    public const float MaximumShaperLog2 = 32;

    /// <summary>Gets the narrowest accepted shaper interval in stops.</summary>
    public const float MinimumShaperRangeLog2 = 1;

    /// <summary>Gets the longest accepted colour-space, display, view, or look name.</summary>
    public const int MaximumNameLength = 256;

    /// <summary>Gets the longest accepted configuration path.</summary>
    public const int MaximumConfigPathLength = 1024;

    private readonly string _configPath;
    private readonly string _sourceColorSpace;

    /// <summary>Initializes an immutable display transform descriptor.</summary>
    /// <param name="configPath">
    /// The OpenColorIO configuration path. It must be absolute and fully qualified; a
    /// relative path is rejected rather than resolved.
    /// </param>
    /// <param name="sourceColorSpace">
    /// The colour space the renderer's linear scene colour is in.
    /// </param>
    /// <param name="display">The display name, or <see langword="null"/> for the config default.</param>
    /// <param name="view">The view name, or <see langword="null"/> for the display's default view.</param>
    /// <param name="look">
    /// The look name or comma-separated look expression, or <see langword="null"/> for none.
    /// </param>
    /// <param name="latticeSize">The lattice edge length.</param>
    /// <param name="shaperMinimumLog2">The lower shaper bound in stops.</param>
    /// <param name="shaperMaximumLog2">The upper shaper bound in stops.</param>
    /// <exception cref="ArgumentException">A name or the path is empty, too long, or not contained.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A size or shaper bound is out of range.</exception>
    public RenderDisplayTransform(
        string configPath,
        string sourceColorSpace,
        string? display = null,
        string? view = null,
        string? look = null,
        int latticeSize = DefaultLatticeSize,
        float shaperMinimumLog2 = DefaultShaperMinimumLog2,
        float shaperMaximumLog2 = DefaultShaperMaximumLog2)
    {
        _configPath = NormalizeConfigPath(configPath);
        _sourceColorSpace = RequireName(sourceColorSpace, nameof(sourceColorSpace));
        Display = NormalizeOptionalName(display, nameof(display));
        View = NormalizeOptionalName(view, nameof(view));
        Look = NormalizeOptionalName(look, nameof(look));
        if (latticeSize is < MinimumLatticeSize or > MaximumLatticeSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(latticeSize),
                latticeSize,
                $"The display transform lattice edge must be between {MinimumLatticeSize} " +
                $"and {MaximumLatticeSize}.");
        }
        if (!float.IsFinite(shaperMinimumLog2) ||
            shaperMinimumLog2 < MinimumShaperLog2 ||
            shaperMinimumLog2 > MaximumShaperLog2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(shaperMinimumLog2),
                shaperMinimumLog2,
                $"The lower shaper bound must be finite and between {MinimumShaperLog2} " +
                $"and {MaximumShaperLog2} stops.");
        }
        if (!float.IsFinite(shaperMaximumLog2) ||
            shaperMaximumLog2 < MinimumShaperLog2 ||
            shaperMaximumLog2 > MaximumShaperLog2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(shaperMaximumLog2),
                shaperMaximumLog2,
                $"The upper shaper bound must be finite and between {MinimumShaperLog2} " +
                $"and {MaximumShaperLog2} stops.");
        }
        if (shaperMaximumLog2 - shaperMinimumLog2 < MinimumShaperRangeLog2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(shaperMaximumLog2),
                shaperMaximumLog2,
                $"The shaper interval must span at least {MinimumShaperRangeLog2} stop.");
        }

        LatticeSize = latticeSize;
        ShaperMinimumLog2 = shaperMinimumLog2;
        ShaperMaximumLog2 = shaperMaximumLog2;
    }

    /// <summary>Gets the resolved absolute OpenColorIO configuration path.</summary>
    public string ConfigPath => _configPath;

    /// <summary>Gets the linear scene-referred source colour space.</summary>
    public string SourceColorSpace => _sourceColorSpace;

    /// <summary>Gets the display name, or <see langword="null"/> for the config default.</summary>
    public string? Display { get; }

    /// <summary>Gets the view name, or <see langword="null"/> for the display default.</summary>
    public string? View { get; }

    /// <summary>Gets the look expression, or <see langword="null"/> for none.</summary>
    public string? Look { get; }

    /// <summary>Gets the lattice edge length.</summary>
    public int LatticeSize { get; }

    /// <summary>Gets the lower shaper bound in stops.</summary>
    public float ShaperMinimumLog2 { get; }

    /// <summary>Gets the upper shaper bound in stops.</summary>
    public float ShaperMaximumLog2 { get; }

    /// <summary>Gets the shaper interval width in stops.</summary>
    public float ShaperRangeLog2 => ShaperMaximumLog2 - ShaperMinimumLog2;

    /// <summary>Gets the number of lattice entries.</summary>
    public int LatticeEntryCount => LatticeSize * LatticeSize * LatticeSize;

    /// <summary>
    /// Gets a stable, secret-free identity for caching. Only paths and names
    /// participate; no credential or user content is ever part of this value.
    /// </summary>
    /// <remarks>
    /// The encoding is length-prefixed, so it is injective. Joining fields with a
    /// separator is not: a name that itself contains the separator -- and colour-space,
    /// display, view, and look names are free-form strings that may -- lets two different
    /// transforms produce one key. Two transforms sharing a cache key share a baked
    /// lattice and share a cached failure, so a collision is a wrong image, not merely a
    /// wasted rebake. Prefixing every field with its length removes the possibility
    /// entirely: no field content can be mistaken for a boundary.
    /// </remarks>
    public string CacheKey => BuildCacheKey();

    private string BuildCacheKey()
    {
        var builder = new StringBuilder(
            ConfigPath.Length + SourceColorSpace.Length + 96);
        AppendField(builder, ConfigPath);
        AppendField(builder, SourceColorSpace);
        AppendOptionalField(builder, Display);
        AppendOptionalField(builder, View);
        AppendOptionalField(builder, Look);
        AppendField(builder, LatticeSize.ToString(CultureInfo.InvariantCulture));
        AppendField(builder, ShaperMinimumLog2.ToString("R", CultureInfo.InvariantCulture));
        AppendField(builder, ShaperMaximumLog2.ToString("R", CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    private static void AppendField(StringBuilder builder, string value)
    {
        _ = builder.Append(CultureInfo.InvariantCulture, $"{value.Length}:{value};");
    }

    // An absent optional name is distinct from an empty one, so it is encoded as a
    // separate token rather than as a zero-length string.
    private static void AppendOptionalField(StringBuilder builder, string? value)
    {
        if (value is null)
        {
            _ = builder.Append("-;");
            return;
        }

        AppendField(builder, value);
    }

    private static string NormalizeConfigPath(string configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        if (configPath.Length > MaximumConfigPathLength)
        {
            throw new ArgumentException(
                $"The OpenColorIO config path must be at most {MaximumConfigPathLength} characters.",
                nameof(configPath));
        }
        if (configPath.AsSpan().IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new ArgumentException(
                "The OpenColorIO config path contains characters that are not valid in a path.",
                nameof(configPath));
        }

        // Absolute only, and deliberately so. A relative path was previously accepted and
        // "contained" by comparing normalized strings against the working directory, which
        // is not containment: it is defeated by a symbolic link, a junction, a hard link,
        // and a working directory that changes between validation and use. Rather than
        // claim a guarantee that string comparison cannot provide, the contract is simply
        // that the caller names the config absolutely. Every real caller already does: a
        // file chooser returns an absolute path, and OCIO's own environment variable is
        // specified as one.
        if (!Path.IsPathFullyQualified(configPath))
        {
            throw new ArgumentException(
                "The OpenColorIO config path must be absolute and fully qualified. A " +
                "relative path is rejected rather than resolved, because resolving it " +
                "against a working directory that can change, through links that can be " +
                "retargeted, is not a containment guarantee.",
                nameof(configPath));
        }

        string resolved;
        try
        {
            resolved = Path.GetFullPath(configPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException(
                "The OpenColorIO config path could not be resolved to a full path.",
                nameof(configPath),
                exception);
        }

        return resolved;
    }

    private static string RequireName(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > MaximumNameLength)
        {
            throw new ArgumentException(
                $"'{parameterName}' must be at most {MaximumNameLength} characters.",
                parameterName);
        }
        return value;
    }

    private static string? NormalizeOptionalName(string? value, string parameterName)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }
        return RequireName(value, parameterName);
    }
}
