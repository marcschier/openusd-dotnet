// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerPropertyInspectionNativeTests
{
    [Test]
    public async Task InspectorShowsWinningReferenceProvenanceSampledValuesAndBoundedArrayPrefixes()
    {
        RequireRuntime();
        string root = Directory.CreateTempSubdirectory("viewer-property-inspection-").FullName;
        string reference = Path.Combine(root, "reference.usda");
        string path = Path.Combine(root, "source.usda");
        const string source = """
            #usda 1.0
            def Xform "Subject" (
                prepend references = @reference.usda@</Source>
            )
            {
                custom double fromReference
                custom double animated.timeSamples = { 0: 0, 10: 20 }
                custom asset texture = @texture.bin@
                custom asset missing = @missing.bin@
                custom rel links = [</One>, </Two>]
            }
            """;
        await File.WriteAllTextAsync(reference,
            "#usda 1.0\ndef Xform \"Source\"\n{\n custom double fromReference = 42\n custom int[] values = [" +
            string.Join(", ", Enumerable.Range(1, 1000)) + "]\n}\n");
        await File.WriteAllTextAsync(path, source);
        await File.WriteAllBytesAsync(Path.Combine(root, "texture.bin"), [1, 2, 3, 4]);
        try
        {
            ViewerPrimInspectorSnapshot inspector;
            await using (UsdStageScheduler scheduler = UsdStageScheduler.Open(path))
            {
                inspector = await scheduler.InvokeAsync(stage =>
                    ViewerStageSnapshotBuilder.BuildInspector(stage, "/Subject", timeCode: 5));
            }
            ViewerAttributeSnapshot value = inspector.Attributes.Single(item => item.Name == "fromReference");
            await Assert.That(value.Value).IsEqualTo("42");
            await Assert.That(value.Inspection!.ResolveSource).IsEqualTo(UsdAttributeResolveSource.Default);
            await Assert.That(Path.GetFullPath(value.Inspection.ValueSource!.LayerIdentifier))
                .IsEqualTo(reference);
            await Assert.That(value.Inspection.ValueSource.SpecPath).IsEqualTo("/Source.fromReference");
            await Assert.That(inspector.PropertySnapshot!.TimeCode).IsEqualTo(5d);
            await Assert.That(inspector.Attributes.Single(item => item.Name == "animated").Value).IsEqualTo("10");
            ViewerAttributeSnapshot array = inspector.Attributes.Single(item => item.Name == "values");
            await Assert.That(array.Inspection!.Value.ElementCount).IsEqualTo(1000ul);
            await Assert.That(array.Inspection.Value.Elements.Count).IsEqualTo(16);
            await Assert.That(array.Inspection.Value.Status).IsEqualTo(UsdPropertyPreviewStatus.Truncated);
            await Assert.That(array.Value).Contains("1000");
            await Assert.That(array.Value).Contains("1, 2, 3");
            await Assert.That(array.Value.Length).IsLessThanOrEqualTo(512);
            ViewerAttributeSnapshot visibility = inspector.Attributes.Single(item => item.Name == "visibility");
            await Assert.That(visibility.Inspection!.ResolveSource).IsEqualTo(UsdAttributeResolveSource.Fallback);
            await Assert.That(visibility.Inspection.ValueSource).IsNull();
            ViewerAttributeSnapshot asset = inspector.Attributes.Single(item => item.Name == "texture");
            await Assert.That(asset.Inspection!.Value.Assets.Single().IsMissing).IsFalse();
            await Assert.That(Path.GetFullPath(asset.Inspection.Value.Assets.Single().ResolvedPath))
                .IsEqualTo(Path.Combine(root, "texture.bin"));
            await Assert.That(asset.Value).Contains("texture.bin");
            await Assert.That(inspector.Attributes.Single(item => item.Name == "missing")
                .Inspection!.Value.Assets.Single().IsMissing).IsTrue();
            await Assert.That(inspector.Relationships.Single(item => item.Name == "links")
                .Inspection!.Targets.Paths).IsEquivalentTo(["/One", "/Two"]);
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(source);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BlockedFallbackRemainsVisibleAndUnavailableArraysAndTargetsDoNotAppearEmpty(bool crate)
    {
        RequireRuntime();
        string root = Directory.CreateTempSubdirectory("viewer-property-state-").FullName;
        string path = Path.Combine(root, "state.usda");
        string binary = Path.Combine(root, "state.usdc");
        await File.WriteAllTextAsync(path,
            """
            #usda 1.0
            def Xform "Subject"
            {
                token visibility = None
                custom int[] array = [1, 2, 3]
                custom rel targets = [</One>, </Two>]
            }
            """);
        try
        {
            if (crate)
            {
                using UsdStage text = UsdStage.Open(path);
                using UsdLayer layer = text.GetRootLayer();
                layer.Export(binary);
            }
            ViewerPrimInspectorSnapshot inspector;
            await using (UsdStageScheduler scheduler = UsdStageScheduler.Open(crate ? binary : path))
            {
                inspector = await scheduler.InvokeAsync(stage =>
                    ViewerStageSnapshotBuilder.BuildInspector(stage, "/Subject"));
            }
            ViewerAttributeSnapshot visibility = inspector.Attributes.Single(item => item.Name == "visibility");
            await Assert.That(visibility.IsBlocked).IsTrue();
            await Assert.That(visibility.HasAuthoredValue).IsTrue();
            await Assert.That(visibility.Value).IsEqualTo("<blocked; fallback: inherited>");
            await Assert.That(visibility.Inspection!.ResolveSource).IsEqualTo(UsdAttributeResolveSource.Fallback);
            ViewerAttributeSnapshot array = inspector.Attributes.Single(item => item.Name == "array");
            ViewerRelationshipSnapshot targets = inspector.Relationships.Single(item => item.Name == "targets");
            if (crate)
            {
                await Assert.That(array.Value).Contains("Deferred");
                await Assert.That(array.Value).DoesNotContain("0 elements");
                await Assert.That(array.Inspection!.Value.ElementCount).IsNull();
                await Assert.That(targets.Targets).Contains("Deferred");
                await Assert.That(targets.Inspection!.Targets.Count).IsNull();
            }
            else
            {
                await Assert.That(array.Value).IsEqualTo("[1, 2, 3] (3 elements)");
                await Assert.That(targets.Targets).IsEqualTo("/One, /Two");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ClipInspectionKeepsUnprovenAuthorshipAndSampleCountsExplicit()
    {
        RequireRuntime();
        string root = Directory.CreateTempSubdirectory("viewer-property-clips-").FullName;
        string path = Path.Combine(root, "clips.usda");
        await File.WriteAllTextAsync(path,
            """
            #usda 1.0
            def "Subject" (
                clips = { dictionary default = { string templateAssetPath = "unopened.###.usda" } }
            )
            {
                custom double value = 7
            }
            """);
        try
        {
            ViewerPrimInspectorSnapshot inspector;
            await using (UsdStageScheduler scheduler = UsdStageScheduler.Open(path))
            {
                inspector = await scheduler.InvokeAsync(stage =>
                    ViewerStageSnapshotBuilder.BuildInspector(stage, "/Subject", 1));
            }
            ViewerAttributeSnapshot value = inspector.Attributes.Single();
            await Assert.That(value.HasAuthoredValue).IsNull();
            await Assert.That(value.TimeSampleCount).IsNull();
            await Assert.That(value.Value).Contains("Deferred");
            await Assert.That(value.Value).Contains("Value-clip");
            await Assert.That(value.TimeSamples).Contains("Deferred");
            await Assert.That(value.Value).DoesNotContain("<unset>");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void RequireRuntime()
    {
        string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
        if (string.IsNullOrWhiteSpace(plugins))
        {
            Skip.Test("Provide the matched property snapshot runtime and OPENUSD_TEST_PLUGIN_PATH.");
        }
        _ = OpenUsdNativeRuntime.RegisterPlugins(plugins!);
    }

    [Test]
    public async Task LongUnicodeValuePreviewsNeverEndWithHalfASurrogatePair()
    {
        RequireRuntime();
        string root = Directory.CreateTempSubdirectory("viewer-property-unicode-").FullName;
        string path = Path.Combine(root, "unicode.usda");
        string prefix = new('a', 508);
        await File.WriteAllTextAsync(path,
            "#usda 1.0\ndef \"Subject\" {\n custom string text = \"" + prefix + "\U0001F600suffix\"\n}\n");
        try
        {
            ViewerPrimInspectorSnapshot inspector;
            await using (UsdStageScheduler scheduler = UsdStageScheduler.Open(path))
            {
                inspector = await scheduler.InvokeAsync(stage =>
                    ViewerStageSnapshotBuilder.BuildInspector(stage, "/Subject"));
            }
            await Assert.That(inspector.Attributes.Single().Value).IsEqualTo(prefix + "...");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(false, false, false)]
    [Arguments(false, true, false)]
    [Arguments(true, false, false)]
    [Arguments(true, true, false)]
    [Arguments(false, false, true)]
    [Arguments(false, true, true)]
    [Arguments(true, false, true)]
    [Arguments(true, true, true)]
    public async Task UnresolvedReferencesCannotProduceASuccessfulInspector(
        bool external, bool localProperty, bool descendant)
    {
        RequireRuntime();
        string root = Directory.CreateTempSubdirectory("viewer-property-unresolved-").FullName;
        string path = Path.Combine(root, "unresolved.usda");
        string reference = external ? "@missing.usda@</Missing>" : "</Missing>";
        string properties = localProperty ? " custom int local = 42\n" : string.Empty;
        await File.WriteAllTextAsync(path,
            "#usda 1.0\ndef \"Subject\" (references = " + reference + ")\n{\n" +
            (descendant ? " def \"Child\" {\n" + properties + " }\n" : properties) + "}\n");
        try
        {
            using UsdStage stage = UsdStage.Open(path);
            await Assert.That(() => ViewerStageSnapshotBuilder.BuildInspector(
                stage, descendant ? "/Subject/Child" : "/Subject"))
                .Throws<OpenUsdNativeException>();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
