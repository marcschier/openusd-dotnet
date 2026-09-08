// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerWorkspaceTests
{
    [Test]
    [Arguments("Review", true, false, true, 240, 340, "properties")]
    [Arguments("Inspect", true, true, true, 280, 380, "properties")]
    [Arguments("MaterialsLighting", true, true, false, 240, 400, "appearance")]
    [Arguments("Presentation", false, false, false, 240, 340, "properties")]
    public async Task ExplicitPresetsOnlyChangePanelAndTabLayout(
        string presetName,
        bool stage,
        bool inspector,
        bool timeline,
        double stageWidth,
        double inspectorWidth,
        string tab)
    {
        ViewerSettings original = ViewerSettings.Default with
        {
            WindowWidth = 1280,
            WindowHeight = 720,
            RendererPreference = "Vulkan",
            ThemePreference = ViewerThemePreference.Dark,
            PickTarget = "edge",
            SelectionMode = "xray",
            SnapTimelineToFrames = true,
            SelectedTabId = "physics",
            DiagnosticsVisible = true,
            HydraVisible = true,
            TfDebugVisible = true,
            ColorManagement = new ViewerColorManagement
            {
                Enabled = true,
                ConfigPath = Path.Combine(AppContext.BaseDirectory, "review.ocio"),
                SourceColorSpace = "ACEScg",
                Display = "sRGB",
                View = "Film"
            }
        };

        ViewerSettings applied = ViewerInspectorLayoutPolicy.ApplyPreset(
            original, Enum.Parse<ViewerWorkspacePreset>(presetName));

        await Assert.That(applied.StagePanelVisible).IsEqualTo(stage);
        await Assert.That(applied.InspectorPanelVisible).IsEqualTo(inspector);
        await Assert.That(applied.TimelineVisible).IsEqualTo(timeline);
        await Assert.That(applied.StagePanelWidth).IsEqualTo(stageWidth);
        await Assert.That(applied.InspectorPanelWidth).IsEqualTo(inspectorWidth);
        await Assert.That(applied.SelectedTabId).IsEqualTo(tab);
        await Assert.That(applied.DiagnosticsVisible).IsFalse();
        await Assert.That(applied.HydraVisible).IsFalse();
        await Assert.That(applied.TfDebugVisible).IsFalse();
        await Assert.That(applied.WindowWidth).IsEqualTo(1280);
        await Assert.That(applied.WindowHeight).IsEqualTo(720);
        await Assert.That(applied.RendererPreference).IsEqualTo("Vulkan");
        await Assert.That(applied.ThemePreference).IsEqualTo(ViewerThemePreference.Dark);
        await Assert.That(applied.PickTarget).IsEqualTo("edge");
        await Assert.That(applied.SelectionMode).IsEqualTo("xray");
        await Assert.That(applied.SnapTimelineToFrames).IsTrue();
        await Assert.That(applied.ColorManagement).IsSameReferenceAs(original.ColorManagement);
        await Assert.That(original.SelectedTabId).IsEqualTo("physics");
    }
}
