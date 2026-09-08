// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Tests;

public sealed partial class UsdHierarchySnapshotNativeTests
{
    private const string VariantStage = """
        #usda 1.0
        def Xform "World" (
            prepend variantSets = ["look", "lod"]
            variants = { string look = "zebra" }
        ) {
            variantSet "look" = {
                "zebra" {}
                "amber" {}
            }
            variantSet "lod" = {
                "low" {}
                "high" {}
            }
            def Scope "Child" {}
        }
        """;

    [Test]
    public async Task EffectivePayloadFlagsDoNotExpandUnloadedContents()
    {
        string path = await WriteStageAsync(
            """
            #usda 1.0
            def Xform "PayloadSource" {
                def Mesh "Geometry" {}
            }
            def Xform "Payloaded" (prepend payload = </PayloadSource>) {}
            def Scope "Ordinary" {}
            """);
        try
        {
            using UsdStage stage = UsdStage.Open(path);
            UsdHierarchySnapshot loaded = stage.GetHierarchySnapshot();
            UsdHierarchyEntry payload = loaded.Entries.Single(entry => entry.Path == "/Payloaded");
            await Assert.That(payload.HasPayload).IsTrue();
            await Assert.That(payload.IsLoaded).IsTrue();
            await Assert.That(payload.ChildCount).IsEqualTo(1);
            await Assert.That(loaded.Entries.Single(entry => entry.Path == "/Ordinary").HasPayload).IsFalse();
            stage.GetPrim("/Payloaded").Unload();
            ulong serial = stage.ChangeSerial;
            UsdHierarchySnapshot unloaded = stage.GetHierarchySnapshot();
            UsdHierarchyEntry unloadedPayload = unloaded.Entries.Single(entry => entry.Path == "/Payloaded");
            await Assert.That(unloadedPayload.HasPayload).IsTrue();
            await Assert.That(unloadedPayload.IsLoaded).IsFalse();
            await Assert.That(unloadedPayload.ChildCount).IsEqualTo(0);
            await Assert.That(unloaded.Entries.Any(entry => entry.Path == "/Payloaded/Geometry")).IsFalse();
            await Assert.That(stage.ChangeSerial).IsEqualTo(serial);
            await Assert.That(payload.IsLoaded).IsTrue();
            await Assert.That(payload.ChildCount).IsEqualTo(1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task VariantOrderAndAppliedSelectionsComposeAcrossLayersAndReferences()
    {
        string weak = await WriteStageAsync(VariantStage);
        string strong = await WriteStageAsync(
            """
            #usda 1.0
            over "World" (
                reorder variantSets = ["lod", "look"]
                variants = { string look = "amber" }
            ) {}
            def Xform "Referenced" (prepend references = </World>) {}
            """);
        try
        {
            using UsdStage stage = UsdStage.Open(strong);
            using UsdLayer root = stage.GetRootLayer();
            root.AddSublayer(weak);
            ulong serial = stage.ChangeSerial;
            UsdHierarchySnapshot snapshot = stage.GetHierarchySnapshot();
            UsdHierarchyEntry referenced = snapshot.Entries.Single(entry => entry.Path == "/Referenced");
            await Assert.That(referenced.VariantMetadataStatus).IsEqualTo(UsdHierarchyVariantMetadataStatus.Complete);
            await Assert.That(referenced.VariantSets.Count).IsEqualTo(2);
            await Assert.That(referenced.VariantSets[0].Name).IsEqualTo("lod");
            await Assert.That(referenced.VariantSets[0].VariantNames[0]).IsEqualTo("high");
            await Assert.That(referenced.VariantSets[0].VariantNames[1]).IsEqualTo("low");
            await Assert.That(referenced.VariantSets[0].Selection).IsEqualTo(string.Empty);
            await Assert.That(referenced.VariantSets[1].Name).IsEqualTo("look");
            await Assert.That(referenced.VariantSets[1].VariantNames[0]).IsEqualTo("amber");
            await Assert.That(referenced.VariantSets[1].VariantNames[1]).IsEqualTo("zebra");
            await Assert.That(referenced.VariantSets[1].Selection).IsEqualTo("amber");
            await Assert.That(snapshot.Entries.Any(entry => entry.Path == "/Referenced/Child")).IsTrue();
            await Assert.That(stage.ChangeSerial).IsEqualTo(serial);
        }
        finally
        {
            File.Delete(strong);
            File.Delete(weak);
        }
    }

    [Test]
    public async Task EverySnapshotCollectionAndNestedDtoSurvivesSchedulerDisposalUnchanged()
    {
        string path = await WriteStageAsync(VariantStage);
        try
        {
            UsdHierarchySnapshot snapshot;
            UsdHierarchyEntry entry;
            UsdHierarchyVariantSet variant;
            await using (UsdStageScheduler scheduler = UsdStageScheduler.Open(path))
            {
                snapshot = await scheduler.InvokeAsync(stage => stage.GetHierarchySnapshot());
                entry = await scheduler.InvokeAsync(stage => stage.GetHierarchySnapshot().Entries[0]);
                variant = await scheduler.InvokeAsync(stage => stage.GetHierarchySnapshot().Entries[0].VariantSets[0]);
                await scheduler.InvokeAsync(stage => stage.GetPrim("/World").SetVariantSelection("look", "amber"));
                UsdHierarchySnapshot changed = await scheduler.InvokeAsync(stage => stage.GetHierarchySnapshot());
                await Assert.That(changed.ChangeSerial > snapshot.ChangeSerial).IsTrue();
                await Assert.That(changed.Entries[0].VariantSets[0].Selection).IsEqualTo("amber");
            }
            await Assert.That(snapshot.Entries[0].VariantSets[0].Selection).IsEqualTo("zebra");
            await Assert.That(entry.Name).IsEqualTo("World");
            await Assert.That(variant.Selection).IsEqualTo("zebra");
            await Assert.That(snapshot.Entries is UsdHierarchyEntry[]).IsFalse();
            await Assert.That(entry.VariantSets is UsdHierarchyVariantSet[]).IsFalse();
            await Assert.That(variant.VariantNames is string[]).IsFalse();
            await Assert.That(() => ((IList<UsdHierarchyEntry>)snapshot.Entries)[0] = entry)
                .Throws<NotSupportedException>();
            await Assert.That(() => ((IList<UsdHierarchyVariantSet>)entry.VariantSets)[0] = variant)
                .Throws<NotSupportedException>();
            await Assert.That(() => ((IList<string>)variant.VariantNames)[0] = "mutated")
                .Throws<NotSupportedException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OrdinaryCrateHierarchyWorksAndDeferredVariantMetadataIsExplicit(bool withVariants)
    {
        string path = await WriteStageAsync(withVariants ? VariantStage :
            """
            #usda 1.0
            def Xform "World" {
                def Scope "Child" {}
            }
            """);
        string crate = Path.ChangeExtension(path, ".usdc");
        try
        {
            using (UsdStage textStage = UsdStage.Open(path))
            using (UsdLayer layer = textStage.GetRootLayer())
            {
                layer.Export(crate);
            }
            using UsdStage stage = UsdStage.Open(crate);
            ulong serial = stage.ChangeSerial;
            UsdHierarchySnapshot snapshot = stage.GetHierarchySnapshot();
            await Assert.That(snapshot.Entries.Count).IsEqualTo(2);
            await Assert.That(snapshot.Entries[0].Path).IsEqualTo("/World");
            await Assert.That(snapshot.Entries[1].Path).IsEqualTo("/World/Child");
            await Assert.That(snapshot.IsComplete).IsEqualTo(!withVariants);
            await Assert.That(snapshot.Entries[0].VariantMetadataStatus).IsEqualTo(withVariants
                ? UsdHierarchyVariantMetadataStatus.Deferred
                : UsdHierarchyVariantMetadataStatus.Complete);
            await Assert.That(snapshot.Entries[0].VariantSets.Count).IsEqualTo(0);
            await Assert.That(stage.ChangeSerial).IsEqualTo(serial);
        }
        finally
        {
            File.Delete(crate);
            File.Delete(path);
        }
    }
}
