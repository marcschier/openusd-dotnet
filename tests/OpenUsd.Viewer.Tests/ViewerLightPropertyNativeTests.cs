// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;
using OpenUsd.Lux;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerLightPropertyNativeTests
{
    [Test]
    public async Task InspectorSnapshotMarksOnlyValidatedLightSchemasAsLight()
    {
        string root = Directory.CreateTempSubdirectory("openusd-light-inspector-").FullName;
        string path = Path.Combine(root, "source.usda");
        await File.WriteAllTextAsync(path,
            """
            #usda 1.0
            def RectLight "Light" {}
            def Xform "Other" {}
            """);
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            (bool isLight, bool isOtherLight) = await scheduler.InvokeAsync(stage =>
            {
                bool light = ViewerStageSnapshotBuilder.BuildInspector(stage, "/Light").IsLight;
                bool other = ViewerStageSnapshotBuilder.BuildInspector(stage, "/Other").IsLight;
                return (light, other);
            });
            await Assert.That(isLight).IsTrue();
            await Assert.That(isOtherLight).IsFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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
    [Arguments("RectLight", "inputs:width")]
    [Arguments("RectLight", "inputs:height")]
    [Arguments("SphereLight", "inputs:radius")]
    [Arguments("DiskLight", "inputs:radius")]
    [Arguments("CylinderLight", "inputs:radius")]
    [Arguments("CylinderLight", "inputs:length")]
    [Arguments("DistantLight", "inputs:angle")]
    public async Task LightShapeControlsUseExactNativeDeclarationsAndReviewUndo(string schema, string name)
    {
        string root = Directory.CreateTempSubdirectory("openusd-light-shape-edit-").FullName;
        string path = Path.Combine(root, "source.usda");
        string original = $"#usda 1.0\ndef {schema} \"Light\" {{}}\n";
        await File.WriteAllTextAsync(path, original);
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            ViewerPropertyEditCapture property = await editor.CapturePropertyAsync(
                "/Light", name, UsdLayerEditField.Default, 0);
            await Assert.That(property.TypeName).IsEqualTo("float");
            await Assert.That(property.Custom).IsFalse();
            await Assert.That(property.Capture.Snapshot.Opinions.Single().PropertyKind)
                .IsEqualTo(UsdLayerPropertyKind.Absent);
            const float value = 2.5f;
            UsdLayerEditAddress address = property.Capture.Snapshot.Addresses.Single();
            ViewerAuthoredEditResult applied = await editor.ApplyAsync(property.Capture,
                [UsdLayerEdit.Set(address, UsdLayerEditValue.FromFloat(value),
                    property.TypeName, property.Variability, property.Custom)],
                $"Light {name}");
            await Assert.That(applied.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(applied.After!.Opinions.Single().Value)
                .IsEqualTo(UsdLayerEditValue.FromFloat(value));
            await Assert.That(await scheduler.InvokeAsync(stage =>
            {
                UsdPrim prim = stage.GetPrim("/Light");
                return name switch
                {
                    "inputs:width" => UsdLuxRectLight.Wrap(prim).Width == value,
                    "inputs:height" => UsdLuxRectLight.Wrap(prim).Height == value,
                    "inputs:radius" when schema == "SphereLight" => UsdLuxSphereLight.Wrap(prim).Radius == value,
                    "inputs:radius" when schema == "DiskLight" => UsdLuxDiskLight.Wrap(prim).Radius == value,
                    "inputs:radius" => UsdLuxCylinderLight.Wrap(prim).Radius == value,
                    "inputs:length" => UsdLuxCylinderLight.Wrap(prim).Length == value,
                    "inputs:angle" => UsdLuxDistantLight.Wrap(prim).Angle == value,
                    _ => throw new InvalidOperationException("Unexpected light shape.")
                };
            })).IsTrue();
            ViewerAuthoredEditResult undone = await editor.UndoAsync();
            await Assert.That(undone.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(undone.After!.Opinions.Single().PropertyKind).IsEqualTo(UsdLayerPropertyKind.Absent);
            ViewerAuthoredEditResult redone = await editor.RedoAsync();
            await Assert.That(redone.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(redone.After!.Opinions.Single().Value)
                .IsEqualTo(UsdLayerEditValue.FromFloat(value));
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(original);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments("inputs:shaping:focus", 2.5f)]
    [Arguments("inputs:shaping:cone:angle", 45f)]
    [Arguments("inputs:shaping:cone:softness", 0.25f)]
    public async Task AppliedShapingControlsUseNativeDeclarationsAndReviewUndo(string name, float value)
    {
        string root = Directory.CreateTempSubdirectory("openusd-light-shaping-edit-").FullName;
        string path = Path.Combine(root, "source.usda");
        const string original =
            "#usda 1.0\ndef SphereLight \"Light\" (prepend apiSchemas = [\"ShapingAPI\"]) {}\n";
        await File.WriteAllTextAsync(path, original);
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            ViewerPropertyEditCapture property = await editor.CapturePropertyAsync(
                "/Light", name, UsdLayerEditField.Default, 0);
            await Assert.That(property.TypeName).IsEqualTo("float");
            await Assert.That(property.Custom).IsFalse();
            await Assert.That(property.Capture.Snapshot.Opinions.Single().PropertyKind)
                .IsEqualTo(UsdLayerPropertyKind.Absent);
            UsdLayerEditAddress address = property.Capture.Snapshot.Addresses.Single();
            ViewerAuthoredEditResult applied = await editor.ApplyAsync(property.Capture,
                [UsdLayerEdit.Set(address, UsdLayerEditValue.FromFloat(value),
                    property.TypeName, property.Variability, property.Custom)],
                $"Light {name}");
            await Assert.That(applied.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(await scheduler.InvokeAsync(stage =>
            {
                UsdLuxShaping shaping = UsdLuxSphereLight.Wrap(stage.GetPrim("/Light")).Light.GetShaping();
                return name switch
                {
                    "inputs:shaping:focus" => shaping.Focus == value,
                    "inputs:shaping:cone:angle" => shaping.ConeAngle == value,
                    "inputs:shaping:cone:softness" => shaping.ConeSoftness == value,
                    _ => throw new InvalidOperationException("Unexpected shaping input.")
                };
            })).IsTrue();
            ViewerAuthoredEditResult undone = await editor.UndoAsync();
            await Assert.That(undone.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(undone.After!.Opinions.Single().PropertyKind).IsEqualTo(UsdLayerPropertyKind.Absent);
            ViewerAuthoredEditResult redone = await editor.RedoAsync();
            await Assert.That(redone.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(redone.After!.Opinions.Single().Value)
                .IsEqualTo(UsdLayerEditValue.FromFloat(value));
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
                "/Light", "width", UsdLayerEditField.Default, 0)).Throws<NotSupportedException>();
            await Assert.That(async () => await editor.CapturePropertyAsync(
                "/Light", "radius", UsdLayerEditField.Default, 0)).Throws<NotSupportedException>();
            await Assert.That(async () => await editor.CapturePropertyAsync(
                "/Light", "inputs:shaping:focus", UsdLayerEditField.Default, 0))
                .Throws<NotSupportedException>();
            await Assert.That(editor.UndoDepth).IsEqualTo(0);
            await Assert.That(editor.RedoDepth).IsEqualTo(0);
            ViewerAuthoredEditCapture untouched = await editor.CaptureAsync(
            [
                new("/Pretend.inputs:intensity", UsdLayerEditField.Default),
                new("/Light.width", UsdLayerEditField.Default),
                new("/Light.radius", UsdLayerEditField.Default),
                new("/Light.inputs:shaping:focus", UsdLayerEditField.Default)
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
