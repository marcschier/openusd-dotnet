// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer;

/// <summary>Whether a Render menu item is checked and whether it can be activated.</summary>
/// <param name="IsChecked">Whether the item currently reflects the authoritative state.</param>
/// <param name="IsEnabled">Whether the item can currently be activated.</param>
internal readonly record struct ViewerRenderMenuItemState(bool IsChecked, bool IsEnabled);

/// <summary>
/// The authoritative viewport-display and renderer-selection state the Render menu must
/// reflect, expressed as plain values rather than live Avalonia controls so the mapping to
/// menu item state can be computed and tested without constructing any control.
/// </summary>
/// <param name="RendererIndex">
/// The selected renderer index: 0 Automatic, 1 Storm, 2 D3D12, 3 Vulkan, 4 Metal.
/// </param>
/// <param name="RendererEnabled">Whether the renderer selection can be changed.</param>
/// <param name="DrawModeIndex">
/// The selected draw mode index, in the order Wireframe, Wireframe on surface, Smooth shaded,
/// Flat shaded, Points, Geom only, Geom flat, Geom smooth, Hidden surface wireframe.
/// </param>
/// <param name="DrawModeEnabled">Whether the draw mode selection can be changed.</param>
/// <param name="PurposeDefault">Whether the default render purpose is shown.</param>
/// <param name="PurposeProxy">Whether the proxy render purpose is shown.</param>
/// <param name="PurposeRender">Whether the render render-purpose is shown.</param>
/// <param name="PurposeGuide">Whether the guide render purpose is shown.</param>
/// <param name="PurposesEnabled">Whether the render-purpose toggles can be changed.</param>
/// <param name="SceneLighting">Whether scene lighting is enabled.</param>
/// <param name="SceneShadows">Whether scene shadows are enabled.</param>
/// <param name="BackfaceCulling">Whether backface culling is enabled.</param>
/// <param name="SceneMaterials">Whether authored scene materials are used.</param>
/// <param name="SceneTogglesEnabled">
/// Whether the lighting/shadows/culling/materials toggles can be changed.
/// </param>
/// <param name="BackgroundColorIndex">
/// The selected background colour index: 0 Black, 1 Dark gray, 2 Light gray, 3 White.
/// </param>
/// <param name="BackgroundColorEnabled">Whether the background colour selection can be changed.</param>
internal readonly record struct ViewerRenderMenuInput(
    int RendererIndex,
    bool RendererEnabled,
    int DrawModeIndex,
    bool DrawModeEnabled,
    bool PurposeDefault,
    bool PurposeProxy,
    bool PurposeRender,
    bool PurposeGuide,
    bool PurposesEnabled,
    bool SceneLighting,
    bool SceneShadows,
    bool BackfaceCulling,
    bool SceneMaterials,
    bool SceneTogglesEnabled,
    int BackgroundColorIndex,
    bool BackgroundColorEnabled);

/// <summary>The computed Render menu state: one entry per menu item, in authored order.</summary>
internal sealed record ViewerRenderMenuState(
    IReadOnlyList<ViewerRenderMenuItemState> Renderer,
    IReadOnlyList<ViewerRenderMenuItemState> DrawMode,
    ViewerRenderMenuItemState PurposeDefault,
    ViewerRenderMenuItemState PurposeProxy,
    ViewerRenderMenuItemState PurposeRender,
    ViewerRenderMenuItemState PurposeGuide,
    ViewerRenderMenuItemState SceneLighting,
    ViewerRenderMenuItemState SceneShadows,
    ViewerRenderMenuItemState BackfaceCulling,
    ViewerRenderMenuItemState SceneMaterials,
    IReadOnlyList<ViewerRenderMenuItemState> BackgroundColor);

/// <summary>
/// Pure computation of Render menu radio/check/enabled state from authoritative viewport
/// display and renderer state. This is the single source the Render menu is derived from:
/// <c>MainWindow</c> calls <see cref="Compute"/> with the current state whenever it changes
/// (stage open/close, backend switch or failover, a rejected mutation, or the controls'
/// own availability) and applies the result to the live menu items, so the menu can never
/// go stale or blank independently of this mapping.
/// </summary>
internal static class ViewerRenderMenuSync
{
    internal const int RendererCount = 5;
    internal const int DrawModeCount = 9;
    internal const int BackgroundColorCount = 4;

    internal static ViewerRenderMenuState Compute(ViewerRenderMenuInput input) => new(
        Renderer: ComputeRadioStates(input.RendererIndex, input.RendererEnabled, RendererCount),
        DrawMode: ComputeRadioStates(input.DrawModeIndex, input.DrawModeEnabled, DrawModeCount),
        PurposeDefault: new ViewerRenderMenuItemState(input.PurposeDefault, input.PurposesEnabled),
        PurposeProxy: new ViewerRenderMenuItemState(input.PurposeProxy, input.PurposesEnabled),
        PurposeRender: new ViewerRenderMenuItemState(input.PurposeRender, input.PurposesEnabled),
        PurposeGuide: new ViewerRenderMenuItemState(input.PurposeGuide, input.PurposesEnabled),
        SceneLighting: new ViewerRenderMenuItemState(
            input.SceneLighting, input.SceneTogglesEnabled),
        SceneShadows: new ViewerRenderMenuItemState(
            input.SceneShadows, input.SceneTogglesEnabled),
        BackfaceCulling: new ViewerRenderMenuItemState(
            input.BackfaceCulling, input.SceneTogglesEnabled),
        SceneMaterials: new ViewerRenderMenuItemState(
            input.SceneMaterials, input.SceneTogglesEnabled),
        BackgroundColor: ComputeRadioStates(
            input.BackgroundColorIndex, input.BackgroundColorEnabled, BackgroundColorCount));

    private static ViewerRenderMenuItemState[] ComputeRadioStates(
        int selectedIndex,
        bool enabled,
        int count)
    {
        var states = new ViewerRenderMenuItemState[count];
        for (int index = 0; index < count; index++)
        {
            states[index] = new ViewerRenderMenuItemState(selectedIndex == index, enabled);
        }
        return states;
    }
}
