// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerCameraPropertyNativeTests
{
    [Test]
    [Arguments("focalLength", "float", UsdLayerEditVariability.Varying)]
    [Arguments("horizontalAperture", "float", UsdLayerEditVariability.Varying)]
    [Arguments("verticalAperture", "float", UsdLayerEditVariability.Varying)]
    [Arguments("clippingRange", "float2", UsdLayerEditVariability.Varying)]
    [Arguments("projection", "token", UsdLayerEditVariability.Varying)]
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
            UsdLayerEditValue value = typeName switch
            {
                "float" => UsdLayerEditValue.FromFloat(60),
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
    public async Task CameraAdmissionDoesNotAuthorizeLookalikesOtherSchemaFieldsOrUniformSamples()
    {
        string root = Directory.CreateTempSubdirectory("openusd-camera-refusal-").FullName;
        string path = Path.Combine(root, "source.usda");
        await File.WriteAllTextAsync(Path.Combine(root, "reference.usda"),
            "#usda 1.0\ndef Xform \"Source\" {\n float focalLength = 7\n}\n");
        await File.WriteAllTextAsync(path,
            "#usda 1.0\ndef Camera \"Camera\" {\n custom uniform token review:uniform = \"locked\"\n}\n" +
            "def Xform \"Pretend\" (prepend references = @reference.usda@</Source>) {}\n");
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            await Assert.That(async () => await editor.CapturePropertyAsync(
                "/Pretend", "focalLength", UsdLayerEditField.Default, 0)).Throws<NotSupportedException>();
            await Assert.That(async () => await editor.CapturePropertyAsync(
                "/Camera", "focusDistance", UsdLayerEditField.Default, 0)).Throws<NotSupportedException>();
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
                new("/Pretend.focalLength", UsdLayerEditField.Default),
                new("/Camera.focusDistance", UsdLayerEditField.Default),
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
}
