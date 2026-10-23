// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;
using OpenUsd.Interop;
using OpenUsd.Shade;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerMaterialBindingNativeTests
{
    private static readonly string[] OriginalTarget = ["/Looks/Original"];
    private static readonly string[] AlternateTarget = ["/Looks/Alternate"];

    [Test]
    [Arguments("material:binding", UsdShadeMaterialPurpose.All)]
    [Arguments("material:binding:preview", UsdShadeMaterialPurpose.Preview)]
    [Arguments("material:binding:full", UsdShadeMaterialPurpose.Full)]
    public async Task DirectBindingsDistinguishReplaceExplicitUnbindAndClearAndShareExactUndo(
        string name, UsdShadeMaterialPurpose purpose)
    {
        string root = Directory.CreateTempSubdirectory("openusd-binding-edit-").FullName;
        string path = Path.Combine(root, "source.usda");
        string source = Source(name);
        await File.WriteAllTextAsync(path, source);
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            ViewerAuthoredEditCapture capture = await editor.CaptureMaterialBindingAsync("/Body", name);
            await Assert.That(capture.Snapshot.Opinions.Single().PropertyKind).IsEqualTo(UsdLayerPropertyKind.Absent);
            ViewerAuthoredEditResult replaced = await editor.ApplyAsync(capture,
                [Set(capture, "/Looks/Alternate")], "Replace direct binding", Guid.NewGuid());
            await Assert.That(replaced.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(replaced.After!.Opinions.Single().Custom).IsFalse();
            await Assert.That(await BoundPathAsync(scheduler, purpose)).IsEqualTo("/Looks/Alternate");
            await Assert.That(await scheduler.InvokeAsync(stage =>
                stage.GetBoundMaterial(stage.GetPrim("/Body/Child"), purpose).Path)).IsEqualTo("/Looks/Alternate");
            capture = capture with { Snapshot = replaced.After };
            ViewerAuthoredEditResult unbound = await editor.ApplyAsync(capture,
                [Set(capture)], "Explicitly unbind", Guid.NewGuid());
            await Assert.That(unbound.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            UsdPathListEdit empty = unbound.After!.Opinions.Single().Value.AsPathList();
            await Assert.That(empty.IsExplicit).IsTrue();
            await Assert.That(empty.ExplicitItems).IsEmpty();
            await Assert.That(await scheduler.InvokeAsync(
                stage => stage.GetPrim("/Body").GetRelationshipTargets(name).Length)).IsEqualTo(0);
            await Assert.That(async () => await BoundPathAsync(scheduler, purpose)).Throws<OpenUsdNativeException>();
            capture = capture with { Snapshot = unbound.After };
            ViewerAuthoredEditResult cleared = await editor.ApplyAsync(capture,
                [UsdLayerEdit.Clear(capture.Snapshot.Addresses.Single())], "Reveal weaker binding", Guid.NewGuid());
            await Assert.That(cleared.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(cleared.After!.Opinions.Single().Value.Kind).IsEqualTo(UsdLayerEditValueKind.Absent);
            await Assert.That(await BoundPathAsync(scheduler, purpose)).IsEqualTo("/Looks/Original");
            await Assert.That(editor.UndoDepth).IsEqualTo(3);
            await Assert.That((await editor.UndoAsync()).Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(async () => await BoundPathAsync(scheduler, purpose)).Throws<OpenUsdNativeException>();
            await Assert.That((await editor.UndoAsync()).Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(await BoundPathAsync(scheduler, purpose)).IsEqualTo("/Looks/Alternate");
            ViewerAuthoredEditResult original = await editor.UndoAsync();
            await Assert.That(original.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(original.After!.Opinions.Single().PropertyKind).IsEqualTo(UsdLayerPropertyKind.Absent);
            await Assert.That(await BoundPathAsync(scheduler, purpose)).IsEqualTo("/Looks/Original");
            await Assert.That((await editor.RedoAsync()).Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(await BoundPathAsync(scheduler, purpose)).IsEqualTo("/Looks/Alternate");
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(source);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task BindingUndoRestoresExactNonexplicitBucketsAndConflictsNeverOverwriteExternalTargets()
    {
        string root = Directory.CreateTempSubdirectory("openusd-binding-history-").FullName;
        string path = Path.Combine(root, "source.usda");
        await File.WriteAllTextAsync(path, Source("material:binding"));
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            ViewerAuthoredEditCapture capture = await editor.CaptureMaterialBindingAsync("/Body", "material:binding");
            var list = new UsdPathListEdit(false, prependedItems: AlternateTarget, deletedItems: OriginalTarget);
            UsdLayerEditOutcome seeded = await scheduler.EditAsync(stage =>
            {
                using UsdLayer layer = stage.GetLocalLayer(capture.LayerIdentifier);
                return layer.CompareAndApply(capture.Snapshot,
                    [UsdLayerEdit.Set(capture.Snapshot.Addresses[0], UsdLayerEditValue.FromPathList(list))]).Outcome;
            }, UsdStageInvalidationKind.Property);
            await Assert.That(seeded).IsEqualTo(UsdLayerEditOutcome.Applied);
            capture = await editor.CaptureMaterialBindingAsync("/Body", "material:binding");
            ViewerAuthoredEditResult set = await editor.ApplyAsync(capture, [Set(capture, "/Looks/Original")], "Set");
            await Assert.That(set.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            ViewerAuthoredEditResult undo = await editor.UndoAsync();
            await Assert.That(undo.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(undo.After!.Identity).IsEqualTo(capture.Snapshot.Identity);
            UsdLayerAuthoredOpinion restored = undo.After.Opinions.Single();
            UsdLayerAuthoredOpinion previous = capture.Snapshot.Opinions.Single();
            await Assert.That(restored.Value).IsEqualTo(previous.Value);
            await Assert.That(restored.PropertyKind).IsEqualTo(previous.PropertyKind);
            await Assert.That(restored.Custom).IsEqualTo(previous.Custom);
            await Assert.That(restored.Value.AsPathList().IsExplicit).IsFalse();
            await Assert.That(await BoundPathAsync(scheduler, UsdShadeMaterialPurpose.All))
                .IsEqualTo("/Looks/Alternate");
            ViewerAuthoredEditCapture stale = await editor.CaptureMaterialBindingAsync("/Body", "material:binding");
            UsdLayerEditOutcome external = await scheduler.EditAsync(stage =>
            {
                using UsdLayer layer = stage.GetLocalLayer(stale.LayerIdentifier);
                return layer.CompareAndApply(stale.Snapshot, [Set(stale, "/Looks/Original")]).Outcome;
            }, UsdStageInvalidationKind.Property);
            await Assert.That(external).IsEqualTo(UsdLayerEditOutcome.Applied);
            ViewerAuthoredEditResult conflict = await editor.ApplyAsync(stale,
                [Set(stale, "/Looks/Alternate")], "Stale binding");
            await Assert.That(conflict.Outcome).IsEqualTo(UsdLayerEditOutcome.Conflict);
            await Assert.That(await BoundPathAsync(scheduler, UsdShadeMaterialPurpose.All))
                .IsEqualTo("/Looks/Original");
            await Assert.That(editor.UndoDepth).IsEqualTo(0);
            await Assert.That(editor.RedoDepth).IsEqualTo(1);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task BindingValidationRejectsMissingApiCollectionTargetsAndWrongNativeSchemasWithoutMutation()
    {
        string root = Directory.CreateTempSubdirectory("openusd-binding-refusal-").FullName;
        string path = Path.Combine(root, "source.usda");
        string source = Source("material:binding") +
            "\ndef Xform \"NoApi\" {\n rel material:binding = </Looks/Original>\n}\n";
        await File.WriteAllTextAsync(path, source);
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            await Assert.That(async () => await editor.CaptureMaterialBindingAsync(
                "/NoApi", "material:binding")).Throws<NotSupportedException>();
            await Assert.That(async () => await editor.CaptureMaterialBindingAsync(
                "/Body", "material:binding:collection:test")).Throws<NotSupportedException>();
            ViewerAuthoredEditCapture capture = await editor.CaptureMaterialBindingAsync("/Body", "material:binding");
            await Assert.That(async () => await editor.ApplyAsync(
                capture, [Set(capture, "/Body")], "Wrong schema")).Throws<NotSupportedException>();
            await Assert.That(async () => await editor.ApplyAsync(
                capture, [Set(capture, "/Looks/Original", "/Looks/Alternate")], "Too many"))
                .Throws<NotSupportedException>();
            ViewerAuthoredEditCapture unchanged = await editor.CaptureMaterialBindingAsync(
                "/Body", "material:binding");
            await Assert.That(unchanged.Snapshot.HasSamePayload(capture.Snapshot)).IsTrue();
            await Assert.That(editor.UndoDepth).IsEqualTo(0);
            await Assert.That(await BoundPathAsync(scheduler, UsdShadeMaterialPurpose.All))
                .IsEqualTo("/Looks/Original");
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(source);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task BindingMaterialSchemaIsRevalidatedAtMutationRatherThanAuthorizedByTheDisplaySnapshot()
    {
        string root = Directory.CreateTempSubdirectory("openusd-binding-race-").FullName;
        string path = Path.Combine(root, "source.usda");
        string source = Source("material:binding");
        await File.WriteAllTextAsync(path, source);
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            ViewerAuthoredEditCapture capture = await editor.CaptureMaterialBindingAsync("/Body", "material:binding");
            await scheduler.EditAsync(stage =>
            {
                stage.DefinePrim("/Looks/Alternate", "Xform");
            }, UsdStageInvalidationKind.Composition);
            await Assert.That(async () => await editor.ApplyAsync(capture,
                [Set(capture, "/Looks/Alternate")], "Changed material schema")).Throws<NotSupportedException>();
            ViewerAuthoredEditCapture unchanged = await editor.CaptureMaterialBindingAsync(
                "/Body", "material:binding");
            await Assert.That(unchanged.Snapshot.Opinions.Single().PropertyKind).IsEqualTo(UsdLayerPropertyKind.Absent);
            await Assert.That(editor.UndoDepth).IsEqualTo(0);
            await Assert.That(await BoundPathAsync(scheduler, UsdShadeMaterialPurpose.All))
                .IsEqualTo("/Looks/Original");
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(source);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static UsdLayerEdit Set(ViewerAuthoredEditCapture capture, params string[] paths) =>
        UsdLayerEdit.Set(capture.Snapshot.Addresses.Single(),
            UsdLayerEditValue.FromPathList(new UsdPathListEdit(true, explicitItems: paths)));

    private static ValueTask<string> BoundPathAsync(UsdStageScheduler scheduler, UsdShadeMaterialPurpose purpose) =>
        scheduler.InvokeAsync(stage => stage.GetBoundMaterial(stage.GetPrim("/Body"), purpose).Path);

    private static string Source(string relationship) =>
        "#usda 1.0\ndef Xform \"Body\" (prepend apiSchemas = [\"MaterialBindingAPI\"]) {\n" +
        $" rel {relationship} = </Looks/Original> (\n bindMaterialAs = \"strongerThanDescendants\"\n )\n" +
        " def Xform \"Child\" (prepend apiSchemas = [\"MaterialBindingAPI\"]) {\n" +
        $"  rel {relationship} = </Looks/Original>\n }}\n}}\n" +
        "def Scope \"Looks\" {\n def Material \"Original\" {}\n def Material \"Alternate\" {}\n}\n";
}
