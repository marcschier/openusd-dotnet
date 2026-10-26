// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;
using OpenUsd.Geom;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed partial class ViewerCameraPropertyNativeTests
{
    [Test]
    [Arguments("focalLength", "float", UsdLayerEditVariability.Varying)]
    [Arguments("horizontalAperture", "float", UsdLayerEditVariability.Varying)]
    [Arguments("verticalAperture", "float", UsdLayerEditVariability.Varying)]
    [Arguments("clippingRange", "float2", UsdLayerEditVariability.Varying)]
    [Arguments("projection", "token", UsdLayerEditVariability.Varying)]
    [Arguments("horizontalApertureOffset", "float", UsdLayerEditVariability.Varying)]
    [Arguments("verticalApertureOffset", "float", UsdLayerEditVariability.Varying)]
    [Arguments("focusDistance", "float", UsdLayerEditVariability.Varying)]
    [Arguments("fStop", "float", UsdLayerEditVariability.Varying)]
    [Arguments("shutter:open", "double", UsdLayerEditVariability.Varying)]
    [Arguments("shutter:close", "double", UsdLayerEditVariability.Varying)]
    public async Task SchemaCameraOpticsCaptureNativeDeclarationsButUndoOnlyReviewOpinions(
        string name, string typeName, UsdLayerEditVariability variability)
    {
        string root = Directory.CreateTempSubdirectory("openusd-camera-edit-").FullName;
        string path = Path.Combine(root, "source.usda");
        const string original = "#usda 1.0\ndef Camera \"Camera\" {}\n";
        await File.WriteAllTextAsync(path, original);
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            ViewerPropertyEditCapture property = await editor.CapturePropertyAsync(
                "/Camera", name, UsdLayerEditField.Default, 0);
            await Assert.That(property.TypeName).IsEqualTo(typeName);
            await Assert.That(property.Variability).IsEqualTo(variability);
            await Assert.That(property.Custom).IsFalse();
            await Assert.That(property.Capture.Snapshot.Opinions.Single().PropertyKind)
                .IsEqualTo(UsdLayerPropertyKind.Absent);
            double scalar = name switch
            {
                "horizontalApertureOffset" => -2.5,
                "shutter:open" => -0.125,
                "shutter:close" => 0.375,
                _ => 60
            };
            UsdLayerEditValue value = typeName switch
            {
                "float" => UsdLayerEditValue.FromFloat((float)scalar),
                "double" => UsdLayerEditValue.FromDouble(scalar),
                "float2" => UsdLayerEditValue.FromVec2f(new UsdVec2f(1, 2000)),
                "token" => UsdLayerEditValue.FromToken("orthographic"),
                _ => throw new InvalidOperationException("Unexpected test declaration.")
            };
            UsdLayerEditAddress address = property.Capture.Snapshot.Addresses.Single();
            ViewerAuthoredEditResult applied = await editor.ApplyAsync(property.Capture,
                [UsdLayerEdit.Set(address, value, property.TypeName, property.Variability, property.Custom)],
                $"Camera {name}");
            await Assert.That(applied.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            UsdLayerAuthoredOpinion after = applied.After!.Opinions.Single();
            await Assert.That(after.TypeName).IsEqualTo(typeName);
            await Assert.That(after.Variability).IsEqualTo(variability);
            await Assert.That(after.Custom).IsFalse();
            await Assert.That(after.Value).IsEqualTo(value);
            if (name is "horizontalApertureOffset" or "verticalApertureOffset" or "focusDistance" or "fStop" or
                "shutter:open" or "shutter:close")
            {
                await Assert.That(await scheduler.InvokeAsync(stage => ReadAdditionalOptic(stage, name, 0)))
                    .IsEqualTo(scalar);
            }
            await Assert.That(editor.UndoDepth).IsEqualTo(1);
            ViewerAuthoredEditResult undo = await editor.UndoAsync();
            await Assert.That(undo.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(undo.After!.Opinions.Single().PropertyKind).IsEqualTo(UsdLayerPropertyKind.Absent);
            await Assert.That(editor.UndoDepth).IsEqualTo(0);
            await Assert.That(editor.RedoDepth).IsEqualTo(1);
            ViewerAuthoredEditResult redo = await editor.RedoAsync();
            await Assert.That(redo.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(redo.After!.Opinions.Single().Value).IsEqualTo(value);
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(original);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments("focalLength")]
    [Arguments("horizontalApertureOffset")]
    [Arguments("verticalApertureOffset")]
    [Arguments("focusDistance")]
    [Arguments("fStop")]
    [Arguments("shutter:open")]
    [Arguments("shutter:close")]
    public async Task CameraAdmissionDoesNotAuthorizeLookalikesOtherSchemaFieldsOrUniformSamples(string name)
    {
        string root = Directory.CreateTempSubdirectory("openusd-camera-refusal-").FullName;
        string path = Path.Combine(root, "source.usda");
        string typeName = name.StartsWith("shutter:", StringComparison.Ordinal) ? "double" : "float";
        await File.WriteAllTextAsync(Path.Combine(root, "reference.usda"),
            $"#usda 1.0\ndef Xform \"Source\" {{\n {typeName} {name} = 7\n}}\n");
        await File.WriteAllTextAsync(path,
            "#usda 1.0\ndef Camera \"Camera\" {\n custom uniform token review:uniform = \"locked\"\n}\n" +
            "def Xform \"Pretend\" (prepend references = @reference.usda@</Source>) {}\n");
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            await Assert.That(async () => await editor.CapturePropertyAsync(
                "/Pretend", name, UsdLayerEditField.Default, 0)).Throws<NotSupportedException>();
            await Assert.That(async () => await editor.CapturePropertyAsync(
                "/Camera", "stereoRole", UsdLayerEditField.Default, 0)).Throws<NotSupportedException>();
            await Assert.That(async () => await editor.CapturePropertyAsync(
                "/Camera", "review:uniform", UsdLayerEditField.TimeSample, 5)).Throws<NotSupportedException>();
            ViewerPropertyEditCapture varyingProjection = await editor.CapturePropertyAsync(
                "/Camera", "projection", UsdLayerEditField.TimeSample, 5);
            await Assert.That(varyingProjection.Variability).IsEqualTo(UsdLayerEditVariability.Varying);
            await Assert.That(varyingProjection.Capture.Snapshot.Opinions.Single().PropertyKind)
                .IsEqualTo(UsdLayerPropertyKind.Absent);
            await Assert.That(editor.UndoDepth).IsEqualTo(0);
            await Assert.That(editor.RedoDepth).IsEqualTo(0);
            ViewerAuthoredEditCapture untouched = await editor.CaptureAsync(
            [
                new($"/Pretend.{name}", UsdLayerEditField.Default),
                new("/Camera.stereoRole", UsdLayerEditField.Default),
                new("/Camera.review:uniform", UsdLayerEditField.TimeSample, 5)
            ]);
            await Assert.That(untouched.Snapshot.Opinions.All(
                static opinion => opinion.PropertyKind == UsdLayerPropertyKind.Absent)).IsTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments("horizontalApertureOffset")]
    [Arguments("verticalApertureOffset")]
    [Arguments("focusDistance")]
    [Arguments("fStop")]
    [Arguments("shutter:open")]
    [Arguments("shutter:close")]
    public async Task AdditionalOpticsSamplesClearExactlyAndRefuseStaleReviewCaptures(string name)
    {
        string root = Directory.CreateTempSubdirectory("openusd-camera-samples-").FullName;
        string path = Path.Combine(root, "source.usda");
        const string original = "#usda 1.0\ndef Camera \"Camera\" {}\n";
        await File.WriteAllTextAsync(path, original);
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            double fallback = await scheduler.InvokeAsync(stage => ReadAdditionalOptic(stage, name, 5));
            ViewerPropertyEditCapture absent = await editor.CapturePropertyAsync(
                "/Camera", name, UsdLayerEditField.TimeSample, 5);
            UsdLayerEditAddress address = absent.Capture.Snapshot.Addresses.Single();
            ViewerAuthoredEditResult first = await editor.ApplyAsync(absent.Capture,
                [UsdLayerEdit.Set(address, AdditionalOpticValue(name, 30),
                    absent.TypeName, absent.Variability, absent.Custom)], "Set camera sample",
                gestureId: Guid.NewGuid());
            await Assert.That(first.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(await scheduler.InvokeAsync(stage => ReadAdditionalOptic(stage, name, 5)))
                .IsEqualTo(30);
            ViewerPropertyEditCapture stale = await editor.CapturePropertyAsync(
                "/Camera", name, UsdLayerEditField.TimeSample, 5);
            ViewerAuthoredEditResult newer = await editor.ApplyAsync(stale.Capture,
                [UsdLayerEdit.Set(address, AdditionalOpticValue(name, 45))], "Newer camera sample",
                gestureId: Guid.NewGuid());
            await Assert.That(newer.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            int depth = editor.UndoDepth;
            ViewerAuthoredEditResult refused = await editor.ApplyAsync(stale.Capture,
                [UsdLayerEdit.Set(address, AdditionalOpticValue(name, 60))], "Stale camera sample");
            await Assert.That(refused.Outcome).IsEqualTo(UsdLayerEditOutcome.Conflict);
            await Assert.That(editor.UndoDepth).IsEqualTo(depth);
            await Assert.That(await scheduler.InvokeAsync(stage => ReadAdditionalOptic(stage, name, 5)))
                .IsEqualTo(45);
            ViewerPropertyEditCapture current = await editor.CapturePropertyAsync(
                "/Camera", name, UsdLayerEditField.TimeSample, 5);
            ViewerAuthoredEditResult cleared = await editor.ApplyAsync(
                current.Capture, [UsdLayerEdit.Clear(address)], "Clear camera sample", gestureId: Guid.NewGuid());
            await Assert.That(cleared.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(await scheduler.InvokeAsync(stage => ReadAdditionalOptic(stage, name, 5)))
                .IsEqualTo(fallback);
            await Assert.That((await editor.UndoAsync()).Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(await scheduler.InvokeAsync(stage => ReadAdditionalOptic(stage, name, 5)))
                .IsEqualTo(45);
            await Assert.That((await editor.RedoAsync()).Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(await scheduler.InvokeAsync(stage => ReadAdditionalOptic(stage, name, 5)))
                .IsEqualTo(fallback);
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(original);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static double ReadAdditionalOptic(UsdStage stage, string name, double timeCode)
    {
        if (name is "shutter:open" or "shutter:close")
        {
            return stage.GetPrim("/Camera").GetDouble(name, timeCode);
        }
        UsdGeomCameraState state = UsdGeomCamera.Wrap(stage.GetPrim("/Camera")).GetState(timeCode);
        return name switch
        {
            "horizontalApertureOffset" => state.HorizontalApertureOffset,
            "verticalApertureOffset" => state.VerticalApertureOffset,
            "focusDistance" => state.FocusDistance,
            "fStop" => state.FStop,
            _ => throw new InvalidOperationException("Unexpected optics field.")
        };
    }

    private static UsdLayerEditValue AdditionalOpticValue(string name, float value) =>
        name is "shutter:open" or "shutter:close"
            ? UsdLayerEditValue.FromDouble(value)
            : UsdLayerEditValue.FromFloat(value);
}
