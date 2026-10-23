// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;
using OpenUsd.Lux;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerLightPropertyNativeTests
{
    [Test]
    [Arguments("RectLight", "inputs:intensity", "float")]
    [Arguments("SphereLight", "inputs:intensity", "float")]
    [Arguments("DiskLight", "inputs:intensity", "float")]
    [Arguments("CylinderLight", "inputs:intensity", "float")]
    [Arguments("DistantLight", "inputs:intensity", "float")]
    [Arguments("DomeLight", "inputs:intensity", "float")]
    [Arguments("RectLight", "inputs:exposure", "float")]
    [Arguments("RectLight", "inputs:diffuse", "float")]
    [Arguments("RectLight", "inputs:specular", "float")]
    [Arguments("RectLight", "inputs:colorTemperature", "float")]
    [Arguments("RectLight", "inputs:enableColorTemperature", "bool")]
    [Arguments("RectLight", "inputs:normalize", "bool")]
    [Arguments("RectLight", "inputs:color", "color3f")]
    public async Task CommonLightControlsUseNativeDeclarationsAndExactReviewUndo(
        string schema, string name, string typeName)
    {
        string root = Directory.CreateTempSubdirectory("openusd-light-edit-").FullName;
        string path = Path.Combine(root, "source.usda");
        string original = $"#usda 1.0\ndef {schema} \"Light\" {{}}\n";
        await File.WriteAllTextAsync(path, original);
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            ViewerPropertyEditCapture property = await editor.CapturePropertyAsync(
                "/Light", name, UsdLayerEditField.Default, 0);
            await Assert.That(property.TypeName).IsEqualTo(typeName);
            await Assert.That(property.Variability).IsEqualTo(UsdLayerEditVariability.Varying);
            await Assert.That(property.Custom).IsFalse();
            await Assert.That(property.Capture.Snapshot.Opinions.Single().PropertyKind)
                .IsEqualTo(UsdLayerPropertyKind.Absent);
            UsdLayerEditValue value = typeName switch
            {
                "float" => UsdLayerEditValue.FromFloat(name == "inputs:colorTemperature" ? 4500 : 2),
                "bool" => UsdLayerEditValue.FromBoolean(true),
                "color3f" => UsdLayerEditValue.FromVec3f(new UsdVec3f(0.25f, 0.5f, 0.75f)),
                _ => throw new InvalidOperationException("Unexpected test type.")
            };
            UsdLayerEditAddress address = property.Capture.Snapshot.Addresses.Single();
            ViewerAuthoredEditResult applied = await editor.ApplyAsync(property.Capture,
                [UsdLayerEdit.Set(address, value, property.TypeName, property.Variability, property.Custom)],
                $"Light {name}");
            await Assert.That(applied.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            UsdLayerAuthoredOpinion after = applied.After!.Opinions.Single();
            await Assert.That(after.TypeName).IsEqualTo(typeName);
            await Assert.That(after.Variability).IsEqualTo(UsdLayerEditVariability.Varying);
            await Assert.That(after.Custom).IsFalse();
            await Assert.That(after.Value).IsEqualTo(value);
            await Assert.That(await scheduler.InvokeAsync(stage =>
            {
                UsdPrim prim = stage.GetPrim("/Light");
                UsdLuxLight light = schema switch
                {
                    "RectLight" => UsdLuxRectLight.Wrap(prim).Light,
                    "SphereLight" => UsdLuxSphereLight.Wrap(prim).Light,
                    "DiskLight" => UsdLuxDiskLight.Wrap(prim).Light,
                    "CylinderLight" => UsdLuxCylinderLight.Wrap(prim).Light,
                    "DistantLight" => UsdLuxDistantLight.Wrap(prim).Light,
                    "DomeLight" => UsdLuxDomeLight.Wrap(prim).Light,
                    _ => throw new InvalidOperationException("Unexpected test light.")
                };
                return name switch
                {
                    "inputs:intensity" => light.Intensity == 2,
                    "inputs:exposure" => light.Exposure == 2,
                    "inputs:diffuse" => light.Diffuse == 2,
                    "inputs:specular" => light.Specular == 2,
                    "inputs:colorTemperature" => light.ColorTemperature == 4500,
                    "inputs:enableColorTemperature" => light.EnableColorTemperature,
                    "inputs:normalize" => light.Normalize,
                    "inputs:color" => light.Color.Equals(new UsdVec3f(0.25f, 0.5f, 0.75f)),
                    _ => throw new InvalidOperationException("Unexpected test control.")
                };
            })).IsTrue();
            ViewerAuthoredEditResult undone = await editor.UndoAsync();
            await Assert.That(undone.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(undone.After!.Opinions.Single().PropertyKind).IsEqualTo(UsdLayerPropertyKind.Absent);
            ViewerAuthoredEditResult redone = await editor.RedoAsync();
            await Assert.That(redone.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(redone.After!.Opinions.Single().Value).IsEqualTo(value);
            await Assert.That(editor.UndoDepth).IsEqualTo(1);
            await Assert.That(editor.RedoDepth).IsEqualTo(0);
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(original);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task LightAdmissionRejectsReferencedLookalikesAndUnauthoredShapeFields()
    {
        string root = Directory.CreateTempSubdirectory("openusd-light-refusal-").FullName;
        string path = Path.Combine(root, "source.usda");
        await File.WriteAllTextAsync(Path.Combine(root, "reference.usda"),
            "#usda 1.0\ndef Xform \"Source\" {\n float inputs:intensity = 7\n}\n");
        const string original = "#usda 1.0\ndef RectLight \"Light\" {}\n" +
            "def Xform \"Pretend\" (prepend references = @reference.usda@</Source>) {}\n";
        await File.WriteAllTextAsync(path, original);
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            await Assert.That(async () => await editor.CapturePropertyAsync(
                "/Pretend", "inputs:intensity", UsdLayerEditField.Default, 0)).Throws<NotSupportedException>();
            await Assert.That(async () => await editor.CapturePropertyAsync(
                "/Light", "inputs:width", UsdLayerEditField.Default, 0)).Throws<NotSupportedException>();
            await Assert.That(editor.UndoDepth).IsEqualTo(0);
            await Assert.That(editor.RedoDepth).IsEqualTo(0);
            ViewerAuthoredEditCapture untouched = await editor.CaptureAsync(
            [
                new("/Pretend.inputs:intensity", UsdLayerEditField.Default),
                new("/Light.inputs:width", UsdLayerEditField.Default)
            ]);
            await Assert.That(untouched.Snapshot.Opinions.All(
                static opinion => opinion.PropertyKind == UsdLayerPropertyKind.Absent)).IsTrue();
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(original);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
