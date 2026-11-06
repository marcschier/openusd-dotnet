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
    [Arguments("RectLight", false, "inputs:width,inputs:height")]
    [Arguments("SphereLight", false, "inputs:radius")]
    [Arguments("DiskLight", false, "inputs:radius")]
    [Arguments("CylinderLight", false, "inputs:radius,inputs:length")]
    [Arguments("DistantLight", false, "inputs:angle")]
    [Arguments("DomeLight", false, "")]
    [Arguments("RectLight", true, "inputs:width,inputs:height")]
    [Arguments("SphereLight", true, "inputs:radius")]
    [Arguments("DiskLight", true, "inputs:radius")]
    [Arguments("CylinderLight", true, "inputs:radius,inputs:length")]
    [Arguments("DistantLight", true, "inputs:angle")]
    [Arguments("DomeLight", true, "")]
    public async Task InspectorLightGroupingUsesNativeSchemaAndAppliedShapingRatherThanAuthoredNames(
        string schema, bool shaping, string expectedShapes)
    {
        string root = Directory.CreateTempSubdirectory("openusd-light-groups-").FullName;
        string path = Path.Combine(root, "source.usda");
        string original = $$"""
            #usda 1.0
            def {{schema}} "Light" {{(shaping ? "(prepend apiSchemas = [\"ShapingAPI\"])" : "")}}
            {
                float inputs:width = 2
                float inputs:height = 2
                float inputs:radius = 2
                float inputs:length = 2
                float inputs:angle = 2
                float inputs:shaping:focus = 2
                float inputs:shaping:cone:angle = 2
                float inputs:shaping:cone:softness = 2
                asset inputs:texture:file = @original.hdr@
                token inputs:texture:format = "latlong"
            }
            """;
        await File.WriteAllTextAsync(path, original);
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            ViewerPrimInspectorSnapshot snapshot = await scheduler.InvokeAsync(
                stage => ViewerStageSnapshotBuilder.BuildInspector(stage, "/Light"));
            string expected = shaping
                ? (expectedShapes.Length == 0 ? "" : expectedShapes + ",") +
                    "inputs:shaping:focus,inputs:shaping:cone:angle,inputs:shaping:cone:softness"
                : expectedShapes;
            string textureProperties = schema switch
            {
                "RectLight" => "inputs:texture:file",
                "DomeLight" => "inputs:texture:file,inputs:texture:format",
                _ => ""
            };
            if (textureProperties.Length != 0)
            {
                expected += (expected.Length == 0 ? "" : ",") + textureProperties;
            }
            await Assert.That(snapshot.IsLight).IsTrue();
            await Assert.That(string.Join(",", snapshot.LightSchemaProperties!)).IsEqualTo(expected);
            ViewerPropertyPage grouped = ViewerPropertyPage.Create(
                snapshot.Attributes, snapshot.Relationships, null, 0, group: ViewerPropertyGroup.Light,
                isLight: snapshot.IsLight, lightSchemaProperties: snapshot.LightSchemaProperties);
            string[] declaredShapes =
            [
                "inputs:width", "inputs:height", "inputs:radius", "inputs:length", "inputs:angle",
                "inputs:shaping:focus", "inputs:shaping:cone:angle", "inputs:shaping:cone:softness",
                "inputs:texture:file", "inputs:texture:format"
            ];
            await Assert.That(grouped.Attributes.Where(attribute => declaredShapes.Contains(attribute.Name))
                .Select(static attribute => attribute.Name))
                .IsEquivalentTo(expected.Split(',', StringSplitOptions.RemoveEmptyEntries));
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(original);
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
    [Arguments("RectLight", "inputs:texture:file", false)]
    [Arguments("DomeLight", "inputs:texture:file", false)]
    [Arguments("DomeLight", "inputs:texture:format", false)]
    [Arguments("RectLight", "inputs:texture:file", true)]
    [Arguments("DomeLight", "inputs:texture:file", true)]
    [Arguments("DomeLight", "inputs:texture:format", true)]
    public async Task LightTextureFieldsPreserveExactReviewHistoryAndRefuseStaleWrites(
        string schema, string name, bool sampled)
    {
        string root = Directory.CreateTempSubdirectory("openusd-light-texture-edit-").FullName;
        string path = Path.Combine(root, "source.usda");
        string original = $"#usda 1.0\ndef {schema} \"Light\" {{}}\n";
        await File.WriteAllTextAsync(path, original);
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            UsdLayerEditField field = sampled ? UsdLayerEditField.TimeSample : UsdLayerEditField.Default;
            double time = sampled ? 5 : 0;
            ViewerPropertyEditCapture property = await editor.CapturePropertyAsync("/Light", name, field, time);
            bool asset = name == "inputs:texture:file";
            await Assert.That(property.TypeName).IsEqualTo(asset ? "asset" : "token");
            await Assert.That(property.Variability).IsEqualTo(UsdLayerEditVariability.Varying);
            await Assert.That(property.Custom).IsFalse();
            await Assert.That(property.Capture.Snapshot.Opinions.Single().PropertyKind)
                .IsEqualTo(UsdLayerPropertyKind.Absent);
            UsdLayerEditAddress address = property.Capture.Snapshot.Addresses.Single();
            UsdLayerEditValue first = asset
                ? UsdLayerEditValue.FromAssetPath("textures/Original Map.hdr")
                : UsdLayerEditValue.FromToken("latlong");
            UsdLayerEditValue second = asset
                ? UsdLayerEditValue.FromAssetPath("textures/Other Map.hdr")
                : UsdLayerEditValue.FromToken("angular");
            ViewerAuthoredEditResult applied = await editor.ApplyAsync(property.Capture,
                [UsdLayerEdit.Set(address, first, property.TypeName, property.Variability, property.Custom)],
                "Set light texture", gestureId: Guid.NewGuid());
            await Assert.That(applied.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(applied.After!.Opinions.Single().Value).IsEqualTo(first);
            await Assert.That(applied.After.Opinions.Single().TypeName).IsEqualTo(property.TypeName);
            await Assert.That(applied.After.Opinions.Single().Variability).IsEqualTo(property.Variability);
            await Assert.That(applied.After.Opinions.Single().Custom).IsFalse();
            await Assert.That(await readValue()).IsEqualTo(asset ? "textures/Original Map.hdr" : "latlong");
            ViewerAuthoredEditResult undone = await editor.UndoAsync();
            await Assert.That(undone.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(undone.After!.Opinions.Single().PropertyKind).IsEqualTo(UsdLayerPropertyKind.Absent);
            ViewerAuthoredEditResult redone = await editor.RedoAsync();
            await Assert.That(redone.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(redone.After!.Opinions.Single().Value).IsEqualTo(first);
            ViewerPropertyEditCapture stale = await editor.CapturePropertyAsync("/Light", name, field, time);
            ViewerAuthoredEditResult newer = await editor.ApplyAsync(stale.Capture,
                [UsdLayerEdit.Set(address, second)], "New light texture", gestureId: Guid.NewGuid());
            await Assert.That(newer.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            int depth = editor.UndoDepth;
            ViewerAuthoredEditResult conflict = await editor.ApplyAsync(stale.Capture,
                [UsdLayerEdit.Set(address, first)], "Stale light texture");
            await Assert.That(conflict.Outcome).IsEqualTo(UsdLayerEditOutcome.Conflict);
            await Assert.That(editor.UndoDepth).IsEqualTo(depth);
            await Assert.That(await readValue()).IsEqualTo(asset ? "textures/Other Map.hdr" : "angular");
            ViewerPropertyEditCapture current = await editor.CapturePropertyAsync("/Light", name, field, time);
            ViewerAuthoredEditResult cleared = await editor.ApplyAsync(current.Capture,
                [UsdLayerEdit.Clear(address)], "Clear light texture", gestureId: Guid.NewGuid());
            await Assert.That(cleared.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(cleared.After!.Opinions.Single().Value.Kind).IsEqualTo(UsdLayerEditValueKind.Absent);
            await Assert.That(await readValue()).IsEqualTo(asset ? "" : "automatic");
            await Assert.That((await editor.UndoAsync()).Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(await readValue()).IsEqualTo(asset ? "textures/Other Map.hdr" : "angular");
            await Assert.That((await editor.RedoAsync()).Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(await readValue()).IsEqualTo(asset ? "" : "automatic");
            ViewerPropertyEditCapture unblocked = await editor.CapturePropertyAsync("/Light", name, field, time);
            ViewerAuthoredEditResult blocked = await editor.ApplyAsync(unblocked.Capture,
                [UsdLayerEdit.Block(address, property.TypeName, property.Variability, property.Custom)],
                "Block light texture", gestureId: Guid.NewGuid());
            await Assert.That(blocked.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(blocked.After!.Opinions.Single().Value.Kind).IsEqualTo(UsdLayerEditValueKind.Block);
            await assertSuppressed();
            ViewerAuthoredEditResult undoBlock = await editor.UndoAsync();
            await Assert.That(undoBlock.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(undoBlock.After!.Opinions.Single().Value.Kind).IsEqualTo(UsdLayerEditValueKind.Absent);
            await Assert.That(await readBlocked()).IsFalse();
            await Assert.That(await readValue()).IsEqualTo(asset ? "" : "automatic");
            ViewerAuthoredEditResult redoBlock = await editor.RedoAsync();
            await Assert.That(redoBlock.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(redoBlock.After!.Opinions.Single().Value.Kind).IsEqualTo(UsdLayerEditValueKind.Block);
            await assertSuppressed();
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(original);

            async Task<string> readValue() => await scheduler.InvokeAsync(stage =>
                stage.GetPrimPropertySnapshot("/Light", sampled ? time : null).Properties
                    .OfType<UsdAttributePropertySnapshot>().Single(attribute => attribute.Name == name)
                    .Value?.Elements.SingleOrDefault() ?? "");
            async Task<bool> readBlocked() => await scheduler.InvokeAsync(stage =>
            {
                UsdAttribute attribute = stage.GetPrim("/Light").GetAttribute(name);
                return (sampled ? attribute.GetValueState(time) : attribute.GetValueState()).IsBlocked;
            });
            async Task assertSuppressed()
            {
                if (sampled)
                {
                    await Assert.That(await readValue()).IsEqualTo("");
                }
                else
                {
                    await Assert.That(await readBlocked()).IsTrue();
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments("SphereLight", "asset inputs:texture:file = @lookalike.hdr@", "inputs:texture:file")]
    [Arguments("RectLight", "token inputs:texture:format = \"latlong\"", "inputs:texture:format")]
    [Arguments("Xform", "asset inputs:texture:file = @lookalike.hdr@", "inputs:texture:file")]
    public async Task LightTextureAdmissionRefusesReferencedPropertiesOnUnrelatedSchemas(
        string schema, string declaration, string name)
    {
        string root = Directory.CreateTempSubdirectory("openusd-light-texture-refusal-").FullName;
        string path = Path.Combine(root, "source.usda");
        string referencePath = Path.Combine(root, "reference.usda");
        string reference = $"#usda 1.0\ndef Xform \"Source\" {{\n {declaration}\n}}\n";
        string original = $"#usda 1.0\ndef {schema} \"Light\" " +
            "(prepend references = @reference.usda@</Source>) {}\n";
        await File.WriteAllTextAsync(referencePath, reference);
        await File.WriteAllTextAsync(path, original);
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            await Assert.That(async () => await editor.CapturePropertyAsync(
                "/Light", name, UsdLayerEditField.Default, 0)).Throws<NotSupportedException>();
            ViewerAuthoredEditCapture untouched = await editor.CaptureAsync(
                [new($"/Light.{name}", UsdLayerEditField.Default)]);
            await Assert.That(untouched.Snapshot.Opinions.Single().PropertyKind)
                .IsEqualTo(UsdLayerPropertyKind.Absent);
            await Assert.That(editor.UndoDepth).IsEqualTo(0);
            await Assert.That(editor.RedoDepth).IsEqualTo(0);
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(original);
            await Assert.That(await File.ReadAllTextAsync(referencePath)).IsEqualTo(reference);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments("float inputs:texture:file = 2")]
    [Arguments("custom asset inputs:texture:file = @lookalike.hdr@")]
    public async Task LightTextureSchemaMetadataRemainsAuthoritativeOverWeakerReferenceDeclarations(string declaration)
    {
        string root = Directory.CreateTempSubdirectory("openusd-light-texture-metadata-").FullName;
        string path = Path.Combine(root, "source.usda");
        string referencePath = Path.Combine(root, "reference.usda");
        string reference = $"#usda 1.0\ndef Xform \"Source\" {{\n {declaration}\n}}\n";
        const string original = "#usda 1.0\ndef DomeLight \"Light\" " +
            "(prepend references = @reference.usda@</Source>) {}\n";
        await File.WriteAllTextAsync(referencePath, reference);
        await File.WriteAllTextAsync(path, original);
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            ViewerPropertyEditCapture property = await editor.CapturePropertyAsync(
                "/Light", "inputs:texture:file", UsdLayerEditField.Default, 0);
            await Assert.That(property.TypeName).IsEqualTo("asset");
            await Assert.That(property.Variability).IsEqualTo(UsdLayerEditVariability.Varying);
            await Assert.That(property.Custom).IsFalse();
            await Assert.That(property.Capture.Snapshot.Opinions.Single().PropertyKind)
                .IsEqualTo(UsdLayerPropertyKind.Absent);
            await Assert.That(editor.UndoDepth).IsEqualTo(0);
            await Assert.That(editor.RedoDepth).IsEqualTo(0);
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(original);
            await Assert.That(await File.ReadAllTextAsync(referencePath)).IsEqualTo(reference);
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
