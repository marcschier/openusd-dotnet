// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Viewer;

internal enum ViewerBackgroundPreset
{
    Black,
    DarkGray,
    LightGray,
    White
}

internal static class ViewerViewportStateMutation
{
    internal static StageRenderState WithDrawMode(
        StageRenderState state,
        RenderDrawMode drawMode)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.WithDisplay(state.Display with { DrawMode = drawMode });
    }

    internal static StageRenderState WithPurpose(
        StageRenderState state,
        RenderPurpose purpose,
        bool enabled)
    {
        ArgumentNullException.ThrowIfNull(state);
        RenderPurpose purposes = enabled
            ? state.Display.Purposes | purpose
            : state.Display.Purposes & ~purpose;
        return state.WithDisplay(state.Display with { Purposes = purposes });
    }

    internal static StageRenderState WithLighting(
        StageRenderState state,
        bool enabled)
    {
        ArgumentNullException.ThrowIfNull(state);
        RenderSettings settings = state.RenderSettings;
        return state.WithRenderSettings(new RenderSettings(
            settings.SamplesPerPixel,
            enabled,
            settings.EnableShadows,
            settings.ClearColor));
    }

    internal static StageRenderState WithShadows(
        StageRenderState state,
        bool enabled)
    {
        ArgumentNullException.ThrowIfNull(state);
        RenderSettings settings = state.RenderSettings;
        return state.WithRenderSettings(new RenderSettings(
            settings.SamplesPerPixel,
            settings.EnableLighting,
            enabled,
            settings.ClearColor));
    }

    internal static StageRenderState WithBackground(
        StageRenderState state,
        ViewerBackgroundPreset preset)
    {
        ArgumentNullException.ThrowIfNull(state);
        RenderSettings settings = state.RenderSettings;
        return state.WithRenderSettings(new RenderSettings(
            settings.SamplesPerPixel,
            settings.EnableLighting,
            settings.EnableShadows,
            ToColor(preset)));
    }

    internal static Vector4 ToColor(ViewerBackgroundPreset preset) =>
        preset switch
        {
            ViewerBackgroundPreset.Black => new Vector4(0, 0, 0, 1),
            ViewerBackgroundPreset.DarkGray => new Vector4(0.18f, 0.18f, 0.18f, 1),
            ViewerBackgroundPreset.LightGray => new Vector4(0.72f, 0.72f, 0.72f, 1),
            ViewerBackgroundPreset.White => new Vector4(1, 1, 1, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(preset))
        };

    internal static SilkMeshRenderOptions ToSilkOptions(RenderSettings settings)
    {
        Vector4 color = settings.ClearColor;
        return new SilkMeshRenderOptions(
            new SilkColor(color.X, color.Y, color.Z, color.W),
            SilkMeshRenderOptions.Default.ClearDepth);
    }
}
