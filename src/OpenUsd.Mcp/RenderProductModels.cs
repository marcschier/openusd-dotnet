// Copyright (c) marcschier. Licensed under the MIT License.

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

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

    [JsonPropertyName("frameCount"), Range(1, 4096), DefaultValue(1)]
    [Description("Number of explicit time samples, 1-4096. Product variables, not viewport output toggles, choose data planes.")]
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
    [property: Description("Explicit bounded execution profile, not a claim of arbitrary UsdRender support.")] string Profile,
    [property: Description("Exact material-binding purpose applied to ingestion.")] string MaterialBindingPurpose,
    [property: Description("Requested render variables in authored order, at most two in the initial profile.")]
    IReadOnlyList<McpProductVariableDto> Outputs);

internal sealed record McpProductVariableDto(
    [property: Description("Requested RenderVar prim path.")] string Path,
    [property: Description("Exact requested source name, not a substituted beauty output.")] string SourceName,
    [property: Description("Exact requested data type.")] string DataType,
    [property: Description("Returned plane name: hdrColor or deviceDepth.")] string Plane);
