// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerHierarchySnapshotNativeTests
{
    [Test]
    public async Task TheViewerReceivesInactiveUndefinedAndAbstractRootsWithoutChangingTheStage()
    {
        using NativeHierarchyFixture fixture = await NativeHierarchyFixture.CreateAsync(
            """
            #usda 1.0
            def Xform "Inactive" (active = false)
            {
                def Scope "NotComposed" {}
            }
            over "OnlyOver" {}
            class Xform "Abstract" {}
            def Xform "World" (
                variants = { string look = "blue" }
                prepend variantSets = "look"
            )
            {
                def Scope "Child" {}
                variantSet "look" = {
                    "red" {}
                    "blue" {}
                }
            }
            """);
        ViewerHierarchySnapshot snapshot;
        await using (UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path))
        {
            ulong serial = await scheduler.InvokeAsync(static stage => stage.ChangeSerial);
            snapshot = await scheduler.InvokeAsync(ViewerStageSnapshotBuilder.BuildHierarchy);
            await Assert.That(await scheduler.InvokeAsync(static stage => stage.ChangeSerial)).IsEqualTo(serial);
        }

        await Assert.That(snapshot.Entries.Select(entry => entry.Path))
            .IsEquivalentTo(["/Inactive", "/OnlyOver", "/Abstract", "/World", "/World/Child"]);
        await Assert.That(snapshot.Entries[0].IsActive).IsFalse();
        await Assert.That(snapshot.Entries[0].Depth).IsEqualTo(0);
        await Assert.That(snapshot.Entries[0].ChildCount).IsEqualTo(0);
        await Assert.That(snapshot.Entries[1].IsDefined).IsFalse();
        await Assert.That(snapshot.Entries[2].IsAbstract).IsTrue();
        await Assert.That(snapshot.Entries[3].ChildCount).IsEqualTo(1);
        await Assert.That(snapshot.Entries[4].ParentPath).IsEqualTo("/World");
        await Assert.That(snapshot.Entries[4].Depth).IsEqualTo(1);
        await Assert.That(snapshot.Entries[3].VariantSets.Single().Selection).IsEqualTo("blue");
        await Assert.That(snapshot.Entries[3].VariantSets.Single().VariantNames).IsEquivalentTo(["blue", "red"]);
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo(fixture.Source);
    }

    [Test]
    public async Task SharedPrototypeIdentitySurvivesProjectionAndPrototypeFiltersCoverDescendants()
    {
        using NativeHierarchyFixture fixture = await NativeHierarchyFixture.CreateAsync(
            """
            #usda 1.0
            def Xform "Model" {
                def Scope "Child" {}
            }
            def Xform "InstanceA" (
                instanceable = true
                prepend references = </Model>
            ) {}
            def Xform "InstanceB" (
                instanceable = true
                prepend references = </Model>
            ) {}
            """);
        using UsdStage stage = UsdStage.Open(fixture.Path);
        ViewerHierarchySnapshot snapshot = ViewerStageSnapshotBuilder.BuildHierarchy(stage);
        ViewerHierarchyEntry prototype = snapshot.Entries.Single(entry => entry.IsPrototype);
        ViewerHierarchyEntry instanceA = snapshot.Entries.Single(entry => entry.Path == "/InstanceA");
        ViewerHierarchyEntry instanceB = snapshot.Entries.Single(entry => entry.Path == "/InstanceB");
        await Assert.That(instanceA.IsInstance).IsTrue();
        await Assert.That(instanceA.ChildCount).IsEqualTo(0);
        await Assert.That(instanceA.PrototypePath).IsEqualTo(prototype.Path);
        await Assert.That(instanceB.PrototypePath).IsEqualTo(prototype.Path);
        await Assert.That(prototype.ParentPath).IsNull();
        await Assert.That(prototype.Depth).IsEqualTo(0);
        await Assert.That(snapshot.Entries.Single(entry => entry.Path == prototype.Path + "/Child").IsInPrototype)
            .IsTrue();

        ViewerHierarchySnapshot hidden = snapshot.Filter(new ViewerHierarchyFilter(null, null));
        ViewerHierarchySnapshot shown = snapshot.Filter(new ViewerHierarchyFilter(
            null, null, ShowPrototypes: true));
        await Assert.That(hidden.Entries.Any(entry => entry.IsPrototype || entry.IsInPrototype)).IsFalse();
        await Assert.That(shown.Entries.Count(entry => entry.IsPrototype)).IsEqualTo(1);
        await Assert.That(shown.Contains(prototype.Path + "/Child")).IsTrue();
        await Assert.That(shown.ChangeSerial).IsEqualTo(snapshot.ChangeSerial);
    }

    [Test]
    public async Task DeferredCrateSelectorsRemainExplicitAfterProjectionAndFiltering()
    {
        using NativeHierarchyFixture fixture = await NativeHierarchyFixture.CreateAsync(
            """
            #usda 1.0
            def Xform "World" (
                variants = { string look = "blue" }
                prepend variantSets = "look"
            ) {
                def Scope "Child" {}
                variantSet "look" = {
                    "red" {}
                    "blue" {}
                }
            }
            """);
        string crate = System.IO.Path.ChangeExtension(fixture.Path, ".usdc");
        using (UsdStage text = UsdStage.Open(fixture.Path))
        using (UsdLayer root = text.GetRootLayer())
        {
            root.Export(crate);
        }
        using UsdStage stage = UsdStage.Open(crate);
        ViewerHierarchySnapshot snapshot = ViewerStageSnapshotBuilder.BuildHierarchy(stage);
        await Assert.That(snapshot.Entries.Length).IsEqualTo(2);
        await Assert.That(snapshot.ChangeSerial).IsEqualTo(stage.ChangeSerial);
        await Assert.That(snapshot.Entries[0].VariantMetadataStatus)
            .IsEqualTo(UsdHierarchyVariantMetadataStatus.Deferred);
        await Assert.That(snapshot.Entries[0].VariantSets).IsEmpty();
        ViewerHierarchySnapshot filtered = snapshot.Filter("Child");
        await Assert.That(filtered.Entries[0].VariantMetadataStatus)
            .IsEqualTo(UsdHierarchyVariantMetadataStatus.Deferred);
        await Assert.That(filtered.Entries[1].Path).IsEqualTo("/World/Child");
        await Assert.That(filtered.ChangeSerial).IsEqualTo(snapshot.ChangeSerial);
    }

    private sealed class NativeHierarchyFixture : IDisposable
    {
        private readonly string _root;

        private NativeHierarchyFixture(string root, string path, string source)
        {
            _root = root;
            Path = path;
            Source = source;
        }

        internal string Path { get; }
        internal string Source { get; }

        internal static async Task<NativeHierarchyFixture> CreateAsync(string source)
        {
            string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
            if (string.IsNullOrWhiteSpace(plugins))
            {
                Skip.Test("Provide a matched native hierarchy runtime and OPENUSD_TEST_PLUGIN_PATH.");
            }
            _ = OpenUsdNativeRuntime.RegisterPlugins(plugins!);
            string root = Directory.CreateTempSubdirectory("viewer-bulk-hierarchy-").FullName;
            string path = System.IO.Path.Combine(root, "source.usda");
            await File.WriteAllTextAsync(path, source);
            return new NativeHierarchyFixture(root, path, source);
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
