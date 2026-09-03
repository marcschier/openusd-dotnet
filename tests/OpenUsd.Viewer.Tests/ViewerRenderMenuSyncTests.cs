// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Exercises the pure Render menu state computation directly, so a regression in the
/// index/flag-to-menu-item mapping is caught here instead of only by a source-string
/// assertion that a call site exists somewhere.
/// </summary>
public sealed class ViewerRenderMenuSyncTests
{
    private static ViewerRenderMenuInput DefaultInput(
        int rendererIndex = 0,
        bool rendererEnabled = true,
        int drawModeIndex = 0,
        bool drawModeEnabled = true,
        bool purposeDefault = true,
        bool purposeProxy = true,
        bool purposeRender = true,
        bool purposeGuide = false,
        bool purposesEnabled = true,
        bool sceneLighting = true,
        bool sceneShadows = true,
        bool backfaceCulling = true,
        bool sceneMaterials = true,
        bool sceneTogglesEnabled = true,
        int backgroundColorIndex = 0,
        bool backgroundColorEnabled = true) => new(
            rendererIndex,
            rendererEnabled,
            drawModeIndex,
            drawModeEnabled,
            purposeDefault,
            purposeProxy,
            purposeRender,
            purposeGuide,
            purposesEnabled,
            sceneLighting,
            sceneShadows,
            backfaceCulling,
            sceneMaterials,
            sceneTogglesEnabled,
            backgroundColorIndex,
            backgroundColorEnabled);

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task ExactlyOneRendererRadioIsCheckedForEachSelectedIndex(int selectedIndex)
    {
        ViewerRenderMenuState state = ViewerRenderMenuSync.Compute(
            DefaultInput(rendererIndex: selectedIndex));

        await Assert.That(state.Renderer.Count).IsEqualTo(ViewerRenderMenuSync.RendererCount);
        for (int index = 0; index < state.Renderer.Count; index++)
        {
            await Assert.That(state.Renderer[index].IsChecked).IsEqualTo(index == selectedIndex);
            await Assert.That(state.Renderer[index].IsEnabled).IsTrue();
        }
    }

    [Test]
    public async Task RendererRadiosAreAllDisabledWhenTheSelectorIsDisabled()
    {
        ViewerRenderMenuState state = ViewerRenderMenuSync.Compute(
            DefaultInput(rendererIndex: 1, rendererEnabled: false));

        foreach (ViewerRenderMenuItemState item in state.Renderer)
        {
            await Assert.That(item.IsEnabled).IsFalse();
        }
        // Disabling the selector must not also blank the checked state: the preference itself
        // is still whatever it was, only activation is unavailable.
        await Assert.That(state.Renderer[1].IsChecked).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(4)]
    [Arguments(8)]
    public async Task ExactlyOneDrawModeRadioIsCheckedForEachSelectedIndex(int selectedIndex)
    {
        ViewerRenderMenuState state = ViewerRenderMenuSync.Compute(
            DefaultInput(drawModeIndex: selectedIndex));

        await Assert.That(state.DrawMode.Count).IsEqualTo(ViewerRenderMenuSync.DrawModeCount);
        int checkedCount = 0;
        for (int index = 0; index < state.DrawMode.Count; index++)
        {
            if (state.DrawMode[index].IsChecked)
            {
                checkedCount++;
                await Assert.That(index).IsEqualTo(selectedIndex);
            }
        }
        await Assert.That(checkedCount).IsEqualTo(1);
    }

    [Test]
    public async Task DrawModeRadiosAreDisabledWhenTheViewportIsUnavailable()
    {
        ViewerRenderMenuState state = ViewerRenderMenuSync.Compute(
            DefaultInput(drawModeEnabled: false));

        foreach (ViewerRenderMenuItemState item in state.DrawMode)
        {
            await Assert.That(item.IsEnabled).IsFalse();
        }
    }

    [Test]
    public async Task PurposeChecksReflectEachIndependentFlag()
    {
        ViewerRenderMenuState state = ViewerRenderMenuSync.Compute(DefaultInput(
            purposeDefault: true,
            purposeProxy: false,
            purposeRender: true,
            purposeGuide: false));

        await Assert.That(state.PurposeDefault.IsChecked).IsTrue();
        await Assert.That(state.PurposeProxy.IsChecked).IsFalse();
        await Assert.That(state.PurposeRender.IsChecked).IsTrue();
        await Assert.That(state.PurposeGuide.IsChecked).IsFalse();
    }

    [Test]
    public async Task PurposeChecksAreDisabledTogetherWhenPurposesAreUnavailable()
    {
        ViewerRenderMenuState state = ViewerRenderMenuSync.Compute(
            DefaultInput(purposesEnabled: false));

        await Assert.That(state.PurposeDefault.IsEnabled).IsFalse();
        await Assert.That(state.PurposeProxy.IsEnabled).IsFalse();
        await Assert.That(state.PurposeRender.IsEnabled).IsFalse();
        await Assert.That(state.PurposeGuide.IsEnabled).IsFalse();
    }

    [Test]
    public async Task SceneToggleChecksReflectEachIndependentFlag()
    {
        ViewerRenderMenuState state = ViewerRenderMenuSync.Compute(DefaultInput(
            sceneLighting: false,
            sceneShadows: true,
            backfaceCulling: false,
            sceneMaterials: true));

        await Assert.That(state.SceneLighting.IsChecked).IsFalse();
        await Assert.That(state.SceneShadows.IsChecked).IsTrue();
        await Assert.That(state.BackfaceCulling.IsChecked).IsFalse();
        await Assert.That(state.SceneMaterials.IsChecked).IsTrue();
    }

    [Test]
    public async Task SceneToggleChecksAreDisabledTogetherWhenUnavailable()
    {
        ViewerRenderMenuState state = ViewerRenderMenuSync.Compute(
            DefaultInput(sceneTogglesEnabled: false));

        await Assert.That(state.SceneLighting.IsEnabled).IsFalse();
        await Assert.That(state.SceneShadows.IsEnabled).IsFalse();
        await Assert.That(state.BackfaceCulling.IsEnabled).IsFalse();
        await Assert.That(state.SceneMaterials.IsEnabled).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task ExactlyOneBackgroundColorRadioIsCheckedForEachSelectedIndex(int selectedIndex)
    {
        ViewerRenderMenuState state = ViewerRenderMenuSync.Compute(
            DefaultInput(backgroundColorIndex: selectedIndex));

        await Assert.That(state.BackgroundColor.Count)
            .IsEqualTo(ViewerRenderMenuSync.BackgroundColorCount);
        for (int index = 0; index < state.BackgroundColor.Count; index++)
        {
            await Assert.That(state.BackgroundColor[index].IsChecked)
                .IsEqualTo(index == selectedIndex);
        }
    }

    [Test]
    public async Task BackgroundColorRadiosAreDisabledWhenUnavailable()
    {
        ViewerRenderMenuState state = ViewerRenderMenuSync.Compute(
            DefaultInput(backgroundColorEnabled: false));

        foreach (ViewerRenderMenuItemState item in state.BackgroundColor)
        {
            await Assert.That(item.IsEnabled).IsFalse();
        }
    }

    [Test]
    public async Task NoStageLoadedStateDisablesEveryGroupButNotTheRendererSelector()
    {
        // Mirrors the real "no coordinator yet" state: the renderer preference itself always
        // stays changeable, but every viewport-display group is unavailable until a stage
        // opens.
        ViewerRenderMenuState state = ViewerRenderMenuSync.Compute(new ViewerRenderMenuInput(
            RendererIndex: 0,
            RendererEnabled: true,
            DrawModeIndex: 0,
            DrawModeEnabled: false,
            PurposeDefault: true,
            PurposeProxy: true,
            PurposeRender: true,
            PurposeGuide: false,
            PurposesEnabled: false,
            SceneLighting: true,
            SceneShadows: true,
            BackfaceCulling: true,
            SceneMaterials: true,
            SceneTogglesEnabled: false,
            BackgroundColorIndex: 0,
            BackgroundColorEnabled: false));

        foreach (ViewerRenderMenuItemState item in state.Renderer)
        {
            await Assert.That(item.IsEnabled).IsTrue();
        }
        foreach (ViewerRenderMenuItemState item in state.DrawMode)
        {
            await Assert.That(item.IsEnabled).IsFalse();
        }
        await Assert.That(state.PurposeDefault.IsEnabled).IsFalse();
        await Assert.That(state.SceneLighting.IsEnabled).IsFalse();
        foreach (ViewerRenderMenuItemState item in state.BackgroundColor)
        {
            await Assert.That(item.IsEnabled).IsFalse();
        }
    }
}
