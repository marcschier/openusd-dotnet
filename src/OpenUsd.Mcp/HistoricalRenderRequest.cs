// Copyright (c) marcschier. Licensed under the MIT License.

using System.ComponentModel;
using OpenUsd.Rendering;

namespace OpenUsd.Mcp;

internal sealed record McpRenderRequestDto(
    [property: Description("Original source-frame requested width, not resized contact-sheet cell width.")] int Width,
    [property: Description("Original source-frame requested height, not resized contact-sheet cell height.")]
    int Height,
    [property: Description("Original requested USD purpose flags, using renderer-neutral enum names.")] string Purposes,
    [property: Description("Original requested authored-visibility policy.")] string Visibility,
    [property: Description("Original requested renderer-neutral draw mode.")] string DrawMode,
    [property: Description("Original requested samples per pixel, not measured backend samples.")] int SamplesPerPixel,
    [property: Description("Original scene-lighting request.")] bool EnableLighting,
    [property: Description("Original shadow request.")] bool EnableShadows,
    [property: Description("Original backface-culling request.")] bool BackfaceCulling,
    [property: Description("Original scene-material request.")] bool UseSceneMaterials,
    [property: Description("Original requested renderer-neutral geometry complexity.")] string Complexity,
    [property: Description("Original requested output-transform enum name.")] string OutputTransform,
    [property: Description("Original requested exposure in stops, before the display/output transform.")]
    float Exposure,
    [property: Description("Original requested linear clear RGBA, in component order.")]
    IReadOnlyList<float> ClearColor,
    [property: Description("Original OCIO request; null means none requested, not missing effective-state evidence.")]
    McpDisplayTransformRequestDto? DisplayTransform)
{
    internal static McpRenderRequestDto From(StageRenderState state)
    {
        RenderSettings settings = state.RenderSettings;
        return new McpRenderRequestDto(
            state.Viewport.Width, state.Viewport.Height,
            state.Display.Purposes.ToString(), state.Display.Visibility.ToString(), state.Display.DrawMode.ToString(),
            settings.SamplesPerPixel, settings.EnableLighting, settings.EnableShadows,
            settings.BackfaceCulling, settings.UseSceneMaterials,
            settings.Complexity.ToString(), settings.OutputTransform.ToString(), settings.Exposure,
            new OwnedReadOnlyList<float>(
                [settings.ClearColor.X, settings.ClearColor.Y, settings.ClearColor.Z, settings.ClearColor.W]),
            settings.DisplayTransform is { } transform
                ? new McpDisplayTransformRequestDto(
                    transform.ConfigPath, transform.SourceColorSpace, transform.Display, transform.View, transform.Look,
                    transform.LatticeSize, transform.ShaperMinimumLog2, transform.ShaperMaximumLog2)
                : null);
    }
}

internal sealed record McpDisplayTransformRequestDto(
    [property: Description("Recorded OCIO config request path; no file is opened or authenticated.")] string ConfigPath,
    [property: Description("Requested source colour space, not verified renderer-working primaries.")]
    string SourceColorSpace,
    [property: Description("Requested display; null requests the config default without recording its resolution.")]
    string? Display,
    [property: Description("Requested view; null requests the display default without recording its resolution.")]
    string? View,
    [property: Description("Requested look expression; null means none requested.")] string? Look,
    [property: Description("Requested lattice edge length.")] int LatticeSize,
    [property: Description("Requested lower log2 shaper bound.")] float ShaperMinimumLog2,
    [property: Description("Requested upper log2 shaper bound.")] float ShaperMaximumLog2);
