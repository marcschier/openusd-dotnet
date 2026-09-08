// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Tests;

public sealed partial class UsdHierarchySnapshotNativeTests
{
    [Test]
    public async Task AllPrimClassificationAndSharedNestedPrototypesRemainNative()
    {
        string path = await WriteStageAsync(
            """
            #usda 1.0
            (
                startTimeCode = 7
                endTimeCode = 11
                customLayerData = { string sentinel = "unchanged" }
            )
            def Xform "Inactive" (active = false) {
                def Scope "NotComposed" {}
            }
            over "OnlyOver" {
                over "ChildOver" {}
            }
            class Xform "Abstract" {
                def Scope "AbstractChild" {}
            }
            def Xform "LeafModel" {
                def Mesh "Leaf" {}
            }
            def Xform "Model" {
                def Xform "Nested" (
                    instanceable = true
                    prepend references = </LeafModel>
                ) {}
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
        try
        {
            byte[] source = await File.ReadAllBytesAsync(path);
            using UsdStage stage = UsdStage.Open(path);
            ulong serial = stage.ChangeSerial;
            UsdHierarchySnapshot snapshot = stage.GetHierarchySnapshot();
            await Assert.That(snapshot.IsComplete).IsTrue();
            var entries = snapshot.Entries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
            await Assert.That(entries["/Inactive"].IsActive).IsFalse();
            await Assert.That(entries["/Inactive"].ChildCount).IsEqualTo(0);
            await Assert.That(entries.ContainsKey("/Inactive/NotComposed")).IsFalse();
            await Assert.That(entries["/OnlyOver"].IsDefined).IsFalse();
            await Assert.That(entries["/OnlyOver/ChildOver"].IsDefined).IsFalse();
            await Assert.That(entries["/Abstract"].IsAbstract).IsTrue();
            await Assert.That(entries["/Abstract/AbstractChild"].IsAbstract).IsTrue();
            await Assert.That(entries["/InstanceA"].IsInstance).IsTrue();
            await Assert.That(entries["/InstanceA"].IsInstanceable).IsTrue();
            await Assert.That(entries["/InstanceA"].ChildCount).IsEqualTo(0);
            await Assert.That(entries["/InstanceA"].PrototypePath).IsEqualTo(entries["/InstanceB"].PrototypePath);
            await Assert.That(entries.Values.Count(entry => entry.IsPrototype)).IsEqualTo(2);
            UsdHierarchyEntry modelPrototype = entries[entries["/InstanceA"].PrototypePath];
            await Assert.That(modelPrototype.ParentIndex).IsEqualTo(-1);
            await Assert.That(modelPrototype.IsInPrototype).IsTrue();
            UsdHierarchyEntry nestedPrototypeInstance = entries[modelPrototype.Path + "/Nested"];
            await Assert.That(nestedPrototypeInstance.IsInstance).IsTrue();
            await Assert.That(nestedPrototypeInstance.PrototypePath).IsEqualTo(entries["/Model/Nested"].PrototypePath);
            await Assert.That(entries.Values.Any(entry => entry.IsInstanceProxy)).IsFalse();
            await Assert.That(entries.Values.Any(entry => entry.Path.StartsWith("/InstanceA/", StringComparison.Ordinal)))
                .IsFalse();
            await Assert.That(snapshot.Entries[0].Path).IsEqualTo("/Inactive");
            await Assert.That(snapshot.Entries[1].Path).IsEqualTo("/OnlyOver");
            await Assert.That(snapshot.Entries[2].Path).IsEqualTo("/OnlyOver/ChildOver");
            await Assert.That(stage.ChangeSerial).IsEqualTo(serial);
            await Assert.That(stage.StartTimeCode).IsEqualTo(7d);
            await Assert.That(stage.EndTimeCode).IsEqualTo(11d);
            using UsdLayer root = stage.GetRootLayer();
            await Assert.That(root.GetMetadataString("sentinel")).IsEqualTo("unchanged");
            await Assert.That((await File.ReadAllBytesAsync(path)).SequenceEqual(source)).IsTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task SnapshotPreservesNativeOrderAndOutlivesItsStage()
    {
        string path = await WriteStageAsync(
            """
            #usda 1.0
            def Xform "World" {
                def Scope "Zulu" {}
                def Mesh "Alpha" {}
            }
            def Scope "Other" {}
            """);
        try
        {
            UsdHierarchySnapshot snapshot;
            using (UsdStage stage = UsdStage.Open(path))
            {
                ulong serial = stage.ChangeSerial;
                snapshot = stage.GetHierarchySnapshot();
                await Assert.That(snapshot.ChangeSerial).IsEqualTo(serial);
                await Assert.That(stage.ChangeSerial).IsEqualTo(serial);
            }

            await Assert.That(snapshot.IsComplete).IsTrue();
            await Assert.That(snapshot.Entries.Count).IsEqualTo(4);
            await Assert.That(snapshot.Entries[0].Path).IsEqualTo("/World");
            await Assert.That(snapshot.Entries[1].Path).IsEqualTo("/World/Zulu");
            await Assert.That(snapshot.Entries[2].Path).IsEqualTo("/World/Alpha");
            await Assert.That(snapshot.Entries[3].Path).IsEqualTo("/Other");
            await Assert.That(snapshot.Entries[0].ParentIndex).IsEqualTo(-1);
            await Assert.That(snapshot.Entries[0].Depth).IsEqualTo(1);
            await Assert.That(snapshot.Entries[0].ChildCount).IsEqualTo(2);
            await Assert.That(snapshot.Entries[2].ParentIndex).IsEqualTo(0);
            await Assert.That(snapshot.Entries[2].Depth).IsEqualTo(2);
            await Assert.That(snapshot.Entries[2].Name).IsEqualTo("Alpha");
            await Assert.That(snapshot.Entries[2].TypeName).IsEqualTo("Mesh");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<string> WriteStageAsync(string contents)
    {
        string? pluginPath = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
        if (string.IsNullOrWhiteSpace(pluginPath) || !Directory.Exists(pluginPath))
        {
            Skip.Test("Set OPENUSD_TEST_PLUGIN_PATH and stage a matching native hierarchy runtime.");
        }
        _ = OpenUsdNativeRuntime.RegisterPlugins(pluginPath!);
        string root = Environment.GetEnvironmentVariable("OPENUSD_TEST_WORK_ROOT")
            ?? Path.Combine(AppContext.BaseDirectory, "native-work");
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, $"hierarchy-{Guid.NewGuid():N}.usda");
        await File.WriteAllTextAsync(path, contents);
        return path;
    }
}
