// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering;

/// <summary>Explicit raster overrides; a null property preserves the authored product value.</summary>
public sealed class RenderProductOverrides : IUsdDetachedResult
{
    /// <summary>Gets an override set that preserves every authored value.</summary>
    public static RenderProductOverrides None { get; } = new();

    /// <summary>Creates an immutable override set without changing a stage or specification.</summary>
    public RenderProductOverrides(
        string? cameraPath = null,
        ViewportDimensions? resolution = null,
        double? pixelAspectRatio = null,
        string? aspectRatioConformPolicy = null,
        UsdVec4f? dataWindowNdc = null)
    {
        if (cameraPath is not null)
        {
            UsdPath.ValidateAbsolutePrimPath(cameraPath, nameof(cameraPath));
        }
        if (resolution is { } size && (size.Width == 0 || size.Height == 0))
        {
            throw new ArgumentOutOfRangeException(nameof(resolution));
        }
        if (pixelAspectRatio is { } aspect && (!double.IsFinite(aspect) || aspect <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(pixelAspectRatio));
        }
        if (aspectRatioConformPolicy is not null &&
            !RenderProductFraming.IsKnownConformPolicy(aspectRatioConformPolicy))
        {
            throw new ArgumentException("Unknown aspect-ratio conform policy.", nameof(aspectRatioConformPolicy));
        }
        if (dataWindowNdc is { } window &&
            (!float.IsFinite(window.X) || !float.IsFinite(window.Y) ||
             !float.IsFinite(window.Z) || !float.IsFinite(window.W) ||
             window.X >= window.Z || window.Y >= window.W))
        {
            throw new ArgumentOutOfRangeException(nameof(dataWindowNdc));
        }
        CameraPath = cameraPath;
        Resolution = resolution;
        PixelAspectRatio = pixelAspectRatio;
        AspectRatioConformPolicy = aspectRatioConformPolicy;
        DataWindowNdc = dataWindowNdc;
    }

    /// <summary>Gets the override camera prim path, or null to use the product camera.</summary>
    public string? CameraPath { get; }

    /// <summary>Gets the override full-raster dimensions before data-window cropping.</summary>
    public ViewportDimensions? Resolution { get; }

    /// <summary>Gets the override pixel width-to-height ratio before conformance.</summary>
    public double? PixelAspectRatio { get; }

    /// <summary>Gets the override standard OpenUSD conform-policy token.</summary>
    public string? AspectRatioConformPolicy { get; }

    /// <summary>Gets override bottom-left/top-right NDC bounds, retaining crop and overscan.</summary>
    public UsdVec4f? DataWindowNdc { get; }
}
