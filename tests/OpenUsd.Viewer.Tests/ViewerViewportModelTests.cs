// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerViewportModelTests
{
    [Test]
    public async Task ViewportStateMutationsDriveRendererState()
    {
        StageRenderState initial = StageRenderState.Create(new StageIdentity("stage.usda"));

        StageRenderState revised = ViewerViewportStateMutation.WithDrawMode(
            initial,
            RenderDrawMode.Wireframe);
        revised = ViewerViewportStateMutation.WithPurpose(
            revised,
            RenderPurpose.Guide,
            enabled: true);
        revised = ViewerViewportStateMutation.WithPurpose(
            revised,
            RenderPurpose.Proxy,
            enabled: false);
        revised = ViewerViewportStateMutation.WithLighting(revised, enabled: false);
        revised = ViewerViewportStateMutation.WithShadows(revised, enabled: false);
        revised = ViewerViewportStateMutation.WithBackground(
            revised,
            ViewerBackgroundPreset.LightGray);

        await Assert.That(revised.Display.DrawMode).IsEqualTo(RenderDrawMode.Wireframe);
        await Assert.That((revised.Display.Purposes & RenderPurpose.Guide) != 0).IsTrue();
        await Assert.That((revised.Display.Purposes & RenderPurpose.Proxy) != 0).IsFalse();
        await Assert.That((revised.Display.Purposes & RenderPurpose.Render) != 0).IsTrue();
        await Assert.That(revised.RenderSettings.EnableLighting).IsFalse();
        await Assert.That(revised.RenderSettings.EnableShadows).IsFalse();
        await Assert.That(revised.RenderSettings.ClearColor)
            .IsEqualTo(ViewerViewportStateMutation.ToColor(ViewerBackgroundPreset.LightGray));
        await Assert.That(revised.Revision).IsGreaterThan(initial.Revision);
    }

    [Test]
    public async Task SilkOptionsUseViewportBackgroundColour()
    {
        StageRenderState state = ViewerViewportStateMutation.WithBackground(
            StageRenderState.Create(new StageIdentity("stage.usda")),
            ViewerBackgroundPreset.DarkGray);

        SilkMeshRenderOptions options =
            ViewerViewportStateMutation.ToSilkOptions(state.RenderSettings);

        await Assert.That(options.ClearColor.Red).IsEqualTo(0.18f);
        await Assert.That(options.ClearColor.Green).IsEqualTo(0.18f);
        await Assert.That(options.ClearColor.Blue).IsEqualTo(0.18f);
        await Assert.That(options.ClearColor.Alpha).IsEqualTo(1f);
        await Assert.That(options.ClearDepth).IsEqualTo(SilkMeshRenderOptions.Default.ClearDepth);
    }
}
