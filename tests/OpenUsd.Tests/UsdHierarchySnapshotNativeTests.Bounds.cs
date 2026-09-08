// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Text;
using OpenUsd.Interop;

namespace OpenUsd.Tests;

public sealed partial class UsdHierarchySnapshotNativeTests
{
    [Test]
    [Arguments("prim count")]
    [Arguments("text bytes")]
    [Arguments("depth")]
    [Arguments("variant sets")]
    [Arguments("variant names")]
    [Arguments("metadata work")]
    public async Task QuotasRefuseExplicitlyWithoutChangingTheStage(string kind)
    {
        string path = await WriteStageAsync(VariantStage);
        try
        {
            using UsdStage stage = UsdStage.Open(path);
            ulong serial = stage.ChangeSerial;
            UsdHierarchyLimits limits = kind switch
            {
                "prim count" => new(maximumPrimCount: 1),
                "text bytes" => new(maximumTextBytes: 4),
                "depth" => new(maximumDepth: 1),
                "variant sets" => new(maximumVariantSets: 0),
                "variant names" => new(maximumVariantNames: 1),
                _ => new(maximumMetadataWork: 0)
            };
            OpenUsdNativeException failure = Refusal(() => stage.GetHierarchySnapshot(limits));
            await Assert.That(failure.Message).Contains(kind);
            await Assert.That(failure.Message).Contains("quota exceeded");
            await Assert.That(stage.ChangeSerial).IsEqualTo(serial);
            await Assert.That(stage.GetHierarchySnapshot().Entries.Count).IsEqualTo(2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ExactTextAndDepthBoundariesAreInclusiveAndZeroLimitsAdmitAnEmptyStage()
    {
        string path = await WriteStageAsync(
            """
            #usda 1.0
            def Scope "A" {
                def "B" {}
            }
            """);
        string empty = await WriteStageAsync("#usda 1.0\n");
        try
        {
            using UsdStage stage = UsdStage.Open(path);
            UsdHierarchySnapshot snapshot = stage.GetHierarchySnapshot(
                new UsdHierarchyLimits(maximumPrimCount: 2, maximumTextBytes: 21, maximumDepth: 2));
            await Assert.That(snapshot.TextByteCount).IsEqualTo(21);
            await Assert.That(snapshot.Entries.Count).IsEqualTo(2);
            await Assert.That(Refusal(() => stage.GetHierarchySnapshot(
                new UsdHierarchyLimits(maximumTextBytes: 20))).Message).Contains("text bytes");
            using UsdStage emptyStage = UsdStage.Open(empty);
            var limits = new UsdHierarchyLimits(0, 0, 0, 0, 0, 0);
            UsdHierarchySnapshot emptySnapshot = emptyStage.GetHierarchySnapshot(limits);
            await Assert.That(emptySnapshot.IsComplete).IsTrue();
            await Assert.That(emptySnapshot.Entries.Count).IsEqualTo(0);
            await Assert.That(emptySnapshot.TextByteCount).IsEqualTo(0);
            await Assert.That(emptySnapshot.MetadataWorkCount).IsEqualTo(0);
        }
        finally
        {
            File.Delete(path);
            File.Delete(empty);
        }
    }

    [Test]
    public async Task LongResidentNamesRefuseBeforeFullPathMaterialization()
    {
        string name = new('N', 65_536);
        string path = await WriteStageAsync($"#usda 1.0\ndef Scope \"{name}\" {{}}\n");
        try
        {
            using UsdStage stage = UsdStage.Open(path);
            OpenUsdNativeException failure = Refusal(() => stage.GetHierarchySnapshot(
                new UsdHierarchyLimits(maximumTextBytes: 32)));
            await Assert.That(failure.Message).Contains("text bytes");
            await Assert.That(failure.Message.Length < 256).IsTrue();
            await Assert.That(stage.GetHierarchySnapshot().Entries[0].Name.Length).IsEqualTo(65_536);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task DeepNativeTraversalIsIterativeAndExplicitlyDepthLimited()
    {
        string path = await WriteStageAsync("#usda 1.0\n");
        try
        {
            using UsdStage stage = UsdStage.Open(path);
            string primPath = string.Concat(Enumerable.Repeat("/N", 1024));
            _ = stage.DefinePrim(primPath, "Scope");
            await Assert.That(Refusal(() => stage.GetHierarchySnapshot()).Message).Contains("depth");
            UsdHierarchySnapshot snapshot = stage.GetHierarchySnapshot(UsdHierarchyLimits.Viewer);
            await Assert.That(snapshot.Entries.Count).IsEqualTo(1024);
            await Assert.That(snapshot.Entries[^1].Depth).IsEqualTo(1024);
            await Assert.That(snapshot.Entries[^1].Path).IsEqualTo(primPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task WideNativeHierarchyProducesOneCompleteBoundedSnapshot()
    {
        var contents = new StringBuilder("#usda 1.0\n");
        for (int index = 0; index < 50_000; index++)
        {
            contents.Append("def Scope \"P").Append(index.ToString(CultureInfo.InvariantCulture)).Append("\" {}\n");
        }
        string path = await WriteStageAsync(contents.ToString());
        try
        {
            using UsdStage stage = UsdStage.Open(path);
            await Assert.That(Refusal(() => stage.GetHierarchySnapshot(
                new UsdHierarchyLimits(maximumPrimCount: 32))).Message).Contains("prim count");
            UsdHierarchySnapshot snapshot = stage.GetHierarchySnapshot();
            await Assert.That(snapshot.Entries.Count).IsEqualTo(50_000);
            await Assert.That(snapshot.Entries[0].Path).IsEqualTo("/P0");
            await Assert.That(snapshot.Entries[^1].Path).IsEqualTo("/P49999");
            await Assert.That(snapshot.TextByteCount <= snapshot.Limits.MaximumTextBytes).IsTrue();
            await Assert.That(snapshot.MetadataWorkCount <= snapshot.Limits.MaximumMetadataWork).IsTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static OpenUsdNativeException Refusal(Func<UsdHierarchySnapshot> query)
    {
        try
        {
            _ = query();
        }
        catch (OpenUsdNativeException exception)
        {
            return exception;
        }
        throw new InvalidOperationException("The bounded hierarchy query should have refused this input.");
    }
}
