// Copyright (c) marcschier. Licensed under the MIT License.

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using OpenUsd.Rendering;

namespace OpenUsd.Mcp;

internal sealed class RenderProductCaptureRequest : SceneRevisionRequestBase
{
    [JsonPropertyName("settingsPath"), MaxLength(OpenUsdMcpLimits.MaximumPathLength)]
    [Description("Optional absolute RenderSettings path; omission uses the stage's authored default.")]
    public string? SettingsPath { get; init; }

    [JsonPropertyName("productPath"), MaxLength(OpenUsdMcpLimits.MaximumPathLength)]
    [Description("Exact RenderProduct path in the selected settings; omission is valid only for a sole product.")]
    public string? ProductPath { get; init; }

    [JsonPropertyName("cameraPath"), MaxLength(OpenUsdMcpLimits.MaximumPathLength)]
    [Description("Optional explicit camera override; the unchanged authored camera is retained in the manifest.")]
    public string? CameraPath { get; init; }

    [JsonPropertyName("width"), Range(1, 4096)]
    [Description(
        "Optional full-raster width before product cropping, 1-4096 pixels. " +
        "Supply width and height together; omit both to preserve the authored resolution.")]
    public int? Width { get; init; }

    [JsonPropertyName("height"), Range(1, 4096)]
    [Description(
        "Optional full-raster height before product cropping, 1-4096 pixels. " +
        "Supply width and height together; omit both to preserve the authored resolution.")]
    public int? Height { get; init; }

    [JsonPropertyName("pixelAspectRatio")]
    [Description(
        "Optional finite positive pixel width-to-height ratio before conformance. " +
        "Omission preserves the authored ratio; adjustPixelAspectRatio computes the effective ratio from the camera.")]
    public double? PixelAspectRatio { get; init; }

    [JsonPropertyName("aspectRatioConformPolicy"), MaxLength(32)]
    [Description(
        "Optional OpenUSD policy: expandAperture, cropAperture, adjustApertureWidth, " +
        "adjustApertureHeight or adjustPixelAspectRatio. Omission preserves the authored policy.")]
    public string? AspectRatioConformPolicy { get; init; }

    [JsonPropertyName("dataWindowNdc"), MinLength(4), MaxLength(4)]
    [Description(
        "Optional normalized [minX, minY, maxX, maxY] data window; four finite values with increasing bounds. " +
        "Coordinates outside 0-1 retain overscan. Omission preserves the authored window; output quotas still apply.")]
    public float[]? DataWindowNdc { get; init; }

    [JsonPropertyName("frameCount"), Range(1, 4096), DefaultValue(1)]
    [Description(
        "Number of explicit time samples, 1-4096. Product variables, not viewport output toggles, choose data planes.")]
    public int FrameCount { get; init; } = 1;

    [JsonPropertyName("startTimeCode")]
    [Description("Finite USD time code of the first frame.")]
    public double StartTimeCode { get; init; }

    [JsonPropertyName("timeStep"), DefaultValue(1d)]
    [Description("Finite nonzero time step; negative steps are permitted and the last sample must remain finite.")]
    public double TimeStep { get; init; } = 1;

    [JsonPropertyName("hdrColorFormat"), MaxLength(3), DefaultValue("raw")]
    [Description(
        "Color-plane container: 'raw' or Windows x64 'exr'. EXR currently requires an uncropped, " +
        "origin-zero square-pixel half4 color plane. Files use generated names, not the authored product name.")]
    public string HdrColorFormat { get; init; } = "raw";
}

internal sealed record McpAuthoredProductDto(
    [property: Description("Selected authored RenderSettings path.")] string SettingsPath,
    [property: Description("Selected authored RenderProduct path.")] string ProductPath,
    [property: Description("Effective camera path after any explicit override.")] string CameraPath,
    [property: Description("Explicit bounded execution profile, not a claim of arbitrary UsdRender support.")]
    string Profile,
    [property: Description("Exact material-binding purpose applied to ingestion.")] string MaterialBindingPurpose,
    [property: Description("Requested render variables in authored order, at most two in the initial profile.")]
    IReadOnlyList<McpProductVariableDto> Outputs);

internal sealed record McpProductVariableDto(
    [property: Description("Requested RenderVar prim path.")] string Path,
    [property: Description("Exact requested source name, not a substituted beauty output.")] string SourceName,
    [property: Description("Exact requested data type.")] string DataType,
    [property: Description("Returned plane name: hdrColor or deviceDepth.")] string Plane);

internal sealed record McpProductRasterDto(
    [property: Description("Original prepared full-raster width before crop or overscan, not sheet-cell width.")]
    int FullWidth,
    [property: Description("Original prepared full-raster height before crop or overscan.")]
    int FullHeight,
    [property: Description("Inclusive output X origin in the original full raster; negative values retain overscan.")]
    int DataWindowMinX,
    [property: Description("Inclusive output Y origin measured from the bottom of the original full raster.")]
    int DataWindowMinY,
    [property: Description("Original prepared pixel width-to-height ratio after camera conformance.")]
    double PixelAspectRatio,
    [property: Description("Original requested OpenUSD conform-policy token after explicit overrides.")]
    string AspectRatioConformPolicy)
{
    [Description("Raster-origin convention; independent of the top-down PNG and sheet-cell coordinates.")]
    public string DataWindowOriginConvention { get; } = "bottom-left";

    internal static McpProductRasterDto From(RenderPreparedFrame frame)
    {
        RenderProductRequest request = frame.Request;
        ViewportDimensions full = request.Overrides.Resolution ??
            new ViewportDimensions(request.Product.Width, request.Product.Height);
        return new(full.Width, full.Height, frame.DataWindowMinX, frame.DataWindowMinY, frame.PixelAspectRatio,
            request.Overrides.AspectRatioConformPolicy ?? request.Product.AspectRatioConformPolicy);
    }
}

internal sealed record McpProductCameraDto(
    [property: Description("Sampled shutter open offset in USD time codes; not evidence of applied motion blur.")]
    double ShutterOpen,
    [property: Description("Original sampled shutter close offset in USD time codes.")]
    double ShutterClose,
    [property: Description("Original sampled camera compensation in stops, separate from display exposure.")]
    double Exposure,
    [property: Description("Original sampled camera exposure ISO.")]
    double ExposureIso,
    [property: Description("Original sampled exposure time in seconds, separate from the shutter interval.")]
    double ExposureTime,
    [property: Description("Original sampled exposure f-stop, separate from the optics' depth-of-field aperture.")]
    double ExposureFStop,
    [property: Description("Original sampled camera exposure responsivity.")]
    double ExposureResponsivity,
    [property: Description("Sampled camera-model exposure multiplier; this reader does not apply it again.")]
    double LinearExposureScale,
    [property: Description("Original sampled focus distance; not a verified effective backend focus setting.")]
    double FocusDistance,
    [property: Description("Original sampled optics f-stop, separate from exposureFStop.")]
    double FStop,
    [property: Description("Whether the original product disables motion blur despite its sampled shutter interval.")]
    bool DisableMotionBlur,
    [property: Description("Whether the product disables depth of field despite its sampled focus and aperture.")]
    bool DisableDepthOfField)
{
    internal static McpProductCameraDto From(RenderPreparedFrame frame)
    {
        RenderCameraFrameSettings camera = frame.CameraSettings ??
            throw new InvalidOperationException("The admitted product frame has no sampled camera settings.");
        return new(camera.ShutterOpen, camera.ShutterClose, camera.Exposure, camera.ExposureIso,
            camera.ExposureTime, camera.ExposureFStop, camera.ExposureResponsivity, camera.LinearExposureScale,
            frame.SampledCamera.FocusDistance, frame.SampledCamera.FStop,
            frame.Request.Product.DisableMotionBlur, frame.Request.Product.DisableDepthOfField);
    }
}
