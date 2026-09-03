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
    /// <summary>
    /// Copies render settings, changing only what a caller asks for.
    /// </summary>
    /// <remarks>
    /// The single place viewport toggles rebuild <see cref="RenderSettings"/>. Every
    /// mutation used to construct one positionally, which silently dropped any property
    /// the positional constructor does not carry: a colour-managed display transform
    /// disappeared the moment a user toggled lighting, shadows, culling, materials, or
    /// the background. Routing every mutation through one copy is what makes adding a
    /// setting safe, and the tests exercise every toggle against it.
    /// </remarks>
    internal static RenderSettings CopyRenderSettings(
        RenderSettings settings,
        bool? enableLighting = null,
        bool? enableShadows = null,
        Vector4? clearColor = null,
        bool? backfaceCulling = null,
        bool? useSceneMaterials = null,
        RenderOutputTransform? outputTransform = null,
        RenderDisplayTransform? displayTransform = null,
        bool clearDisplayTransform = false) =>
        new RenderSettings(
            settings.SamplesPerPixel,
            enableLighting ?? settings.EnableLighting,
            enableShadows ?? settings.EnableShadows,
            clearColor ?? settings.ClearColor,
            backfaceCulling ?? settings.BackfaceCulling,
            useSceneMaterials ?? settings.UseSceneMaterials,
            settings.Complexity,
            outputTransform ?? settings.OutputTransform,
            settings.Exposure)
        {
            DisplayTransform = clearDisplayTransform
                ? null
                : displayTransform ?? settings.DisplayTransform,
        };

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
        return state.WithRenderSettings(
            CopyRenderSettings(state.RenderSettings, enableLighting: enabled));
    }

    internal static StageRenderState WithShadows(
        StageRenderState state,
        bool enabled)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.WithRenderSettings(
            CopyRenderSettings(state.RenderSettings, enableShadows: enabled));
    }

    internal static StageRenderState WithBackground(
        StageRenderState state,
        ViewerBackgroundPreset preset)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.WithRenderSettings(
            CopyRenderSettings(state.RenderSettings, clearColor: ToColor(preset)));
    }

    internal static StageRenderState WithBackfaceCulling(
        StageRenderState state,
        bool enabled)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.WithRenderSettings(
            CopyRenderSettings(state.RenderSettings, backfaceCulling: enabled));
    }

    internal static StageRenderState WithSceneMaterials(
        StageRenderState state,
        bool enabled)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.WithRenderSettings(
            CopyRenderSettings(state.RenderSettings, useSceneMaterials: enabled));
    }

    internal static StageRenderState WithDisplayTransform(
        StageRenderState state,
        RenderDisplayTransform? displayTransform)
    {
        ArgumentNullException.ThrowIfNull(state);
        RenderSettings updated = CopyRenderSettings(
            state.RenderSettings,
            // A colour-managed display transform replaces the built-in output transform
            // rather than composing with it, so selecting one moves the built-in
            // transform to Identity and clearing one restores the presentation default.
            // Applying both would convert the same image twice.
            outputTransform: displayTransform is null
                ? RenderSettings.PresentationDefault.OutputTransform
                : RenderOutputTransform.Identity,
            displayTransform: displayTransform,
            clearDisplayTransform: displayTransform is null);
        updated.ValidateDisplayTransform();
        return state.WithRenderSettings(updated);
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
            SilkMeshRenderOptions.Default.ClearDepth,
            settings.BackfaceCulling,
            settings.UseSceneMaterials)
        {
            OutputTransform = settings.OutputTransform,
            Exposure = settings.Exposure,
            DisplayTransform = settings.DisplayTransform,
        };
    }
}
