// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;
using OpenUsd.Geom;
using OpenUsd.Interop;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerCompoundPropertyNativeTests
{
    [Test]
    public async Task CameraAndMatrixReviewEditsAffectNativeStateAndUndoWithoutTouchingOtherComponents()
    {
        string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
        if (string.IsNullOrWhiteSpace(plugins))
        {
            Skip.Test("Provide a matching native runtime and OPENUSD_TEST_PLUGIN_PATH.");
        }
        _ = OpenUsdNativeRuntime.RegisterPlugins(plugins!);
        string root = Directory.CreateTempSubdirectory("viewer-compound-property-").FullName;
        string path = Path.Combine(root, "source.usda");
        const string source = """
            #usda 1.0
            def Xform "Body"
            {
                matrix4d xformOp:transform = ((1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 1, 0), (1, 2, 3, 1))
                uniform token[] xformOpOrder = ["xformOp:transform"]
                quatf review:rotation = (1, 0, 0, 0)
                color4f review:tint = (1, 1, 1, 1)
            }
            def Camera "Camera"
            {
                float2 clippingRange = (0.125, 1000)
            }
            """;
        await File.WriteAllTextAsync(path, source);
        try
        {
            await using UsdStageScheduler scheduler = UsdStageScheduler.Open(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            await ApplyAsync(editor, "/Camera", "clippingRange", "(0.5, 250)");
            await ApplyAsync(editor, "/Body", "xformOp:transform",
                "((1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 1, 0), (4.125, 5, -6, 1))");
            await ApplyAsync(editor, "/Body", "review:rotation", "(0.5, 0.25, -0.125, 0)");
            await ApplyAsync(editor, "/Body", "review:tint", "(0.25, 0.5, 0.75, 1)");
            await Assert.That(editor.UndoDepth).IsEqualTo(4);
            await Assert.That(await scheduler.InvokeAsync(static stage =>
                UsdGeomCamera.Wrap(stage.GetPrim("/Camera")).ClippingRange)).IsEqualTo(new UsdVec2f(0.5f, 250));
            await Assert.That(await scheduler.InvokeAsync(static stage =>
                UsdGeomXformable.Wrap(stage.GetPrim("/Body")).GetWorldTransform()))
                .IsEqualTo(UsdMatrix4d.CreateTranslation(4.125, 5, -6));
            ViewerPropertyEditCapture rotation = await editor.CapturePropertyAsync(
                "/Body", "review:rotation", UsdLayerEditField.Default, 0);
            await Assert.That(rotation.Capture.Snapshot.Opinions[0].Value.AsQuatf())
                .IsEqualTo(new UsdQuatf(0.5f, 0.25f, -0.125f, 0));
            ViewerPropertyEditCapture tint = await editor.CapturePropertyAsync(
                "/Body", "review:tint", UsdLayerEditField.Default, 0);
            await Assert.That(tint.Capture.Snapshot.Opinions[0].TypeName).IsEqualTo("color4f");
            await Assert.That(tint.Capture.Snapshot.Opinions[0].Value.AsVec4f())
                .IsEqualTo(new UsdVec4f(0.25f, 0.5f, 0.75f, 1));

            for (int edit = 0; edit < 4; edit++)
            {
                await Assert.That((await editor.UndoAsync()).Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            }
            await Assert.That(await scheduler.InvokeAsync(static stage =>
                UsdGeomCamera.Wrap(stage.GetPrim("/Camera")).ClippingRange)).IsEqualTo(new UsdVec2f(0.125f, 1000));
            await Assert.That(await scheduler.InvokeAsync(static stage =>
                UsdGeomXformable.Wrap(stage.GetPrim("/Body")).GetWorldTransform()))
                .IsEqualTo(UsdMatrix4d.CreateTranslation(1, 2, 3));
            await Assert.That(editor.RedoDepth).IsEqualTo(4);
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(source);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task ApplyAsync(
        ViewerAuthoredEditController editor, string primPath, string propertyName, string text)
    {
        ViewerPropertyEditCapture capture =
            await editor.CapturePropertyAsync(primPath, propertyName, UsdLayerEditField.Default, 0);
        bool parsed = ViewerPropertyEditParser.TryParse(capture.TypeName, text,
            out UsdLayerEditValue? value, out string error);
        await Assert.That(parsed).IsTrue().Because(error);
        ViewerAuthoredEditResult applied = await editor.ApplyAsync(capture.Capture,
            [UsdLayerEdit.Set(capture.Capture.Snapshot.Addresses[0], value!, capture.TypeName,
                capture.Variability, capture.Custom)], propertyName, Guid.NewGuid());
        await Assert.That(applied.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
    }
}
