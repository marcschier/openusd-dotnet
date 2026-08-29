// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Describes an OpenColorIO display/view transform applied to CPU capture output.
/// </summary>
/// <remarks>
/// <para>
/// This type parameterizes a <see cref="SilkOpenColorIoProcessor"/>: a reusable,
/// immutable native OCIO CPU processor that converts linear scene-referred RGBA16Float
/// readback data to display-referred RGBA8 through an OCIO config's display/view
/// pipeline. It is used for offline / export capture only; live GPU presentation
/// OCIO is not yet supported.
/// </para>
/// <para>
/// When <see cref="Display"/> or <see cref="View"/> is <see langword="null"/>, the
/// OCIO config's default display or default view for that display is used.
/// </para>
/// </remarks>
public sealed class SilkOpenColorIoDisplayTransform
{
    /// <summary>Initializes a new OCIO display transform descriptor.</summary>
    /// <param name="configPath">
    /// Path to the OCIO config file. Must not be <see langword="null"/> or empty.
    /// </param>
    /// <param name="sourceColorSpace">
    /// The OCIO color space of the source data (typically the rendering working space).
    /// Must not be <see langword="null"/> or empty.
    /// </param>
    /// <param name="display">
    /// The OCIO display name, or <see langword="null"/> for the config default.
    /// </param>
    /// <param name="view">
    /// The OCIO view name, or <see langword="null"/> for the default view of the display.
    /// </param>
    /// <param name="looks">
    /// Optional OCIO look names (comma-separated), or <see langword="null"/> for none.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="configPath"/> or <paramref name="sourceColorSpace"/> is
    /// <see langword="null"/> or empty.
    /// </exception>
    public SilkOpenColorIoDisplayTransform(
        string configPath,
        string sourceColorSpace,
        string? display = null,
        string? view = null,
        string? looks = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(configPath);
        ArgumentException.ThrowIfNullOrEmpty(sourceColorSpace);

        ConfigPath = configPath;
        SourceColorSpace = sourceColorSpace;
        Display = string.IsNullOrEmpty(display) ? null : display;
        View = string.IsNullOrEmpty(view) ? null : view;
        Looks = string.IsNullOrEmpty(looks) ? null : looks;
    }

    /// <summary>Gets the path to the OCIO config file.</summary>
    public string ConfigPath { get; }

    /// <summary>Gets the source color space name.</summary>
    public string SourceColorSpace { get; }

    /// <summary>
    /// Gets the OCIO display name, or <see langword="null"/> for the config default.
    /// </summary>
    public string? Display { get; }

    /// <summary>
    /// Gets the OCIO view name, or <see langword="null"/> for the default view.
    /// </summary>
    public string? View { get; }

    /// <summary>
    /// Gets the optional OCIO look names (comma-separated), or <see langword="null"/>
    /// for no look override.
    /// </summary>
    public string? Looks { get; }

    /// <summary>
    /// Creates a reusable <see cref="SilkOpenColorIoProcessor"/> from this transform.
    /// </summary>
    /// <returns>An immutable processor that can be reused across frames.</returns>
    /// <exception cref="OpenUsd.Interop.OpenUsdNativeException">
    /// The OCIO config could not be loaded or the display/view/color-space names are
    /// invalid.
    /// </exception>
    public SilkOpenColorIoProcessor CreateProcessor() =>
        new(this);
}
