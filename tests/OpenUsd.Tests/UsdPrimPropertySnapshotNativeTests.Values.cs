// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Tests;

public sealed partial class UsdPrimPropertySnapshotNativeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OrdinaryUsdaAndCrateExposeTypedValuesAndExplicitDeferredStorage(bool crate)
    {
        string path = await WriteStageAsync(
            """
            #usda 1.0
            def Xform "Subject"
            {
                custom double3 precise = (1.25, 2.5, 3.75)
                custom string text = "héllo"
                custom int[] numbers = [1, 2, 3, 4, 5]
                custom double sampled.timeSamples = { 1: 2, 3: 6 }
                custom int blocked = None
                custom int unset
                custom rel targets = [</A>, </B>, </C>]
            }
            """);
        string binary = Path.ChangeExtension(path, ".usdc");
        try
        {
            if (crate)
            {
                using UsdStage text = UsdStage.Open(path);
                using UsdLayer layer = text.GetRootLayer();
                layer.Export(binary);
            }
            using UsdStage stage = UsdStage.Open(crate ? binary : path);
            UsdPrimPropertySnapshot snapshot = stage.GetPrimPropertySnapshot("/Subject", 2,
                new UsdPropertyInspectionLimits(previewElements: 2, timeSamplePreview: 1, targetPreview: 2));
            UsdAttributePropertySnapshot precise = Attribute(snapshot, "precise");
            await Assert.That(precise.TypeName).IsEqualTo("double3");
            await Assert.That(precise.Value.Elements.Single()).IsEqualTo("(1.25, 2.5, 3.75)");
            await Assert.That(Attribute(snapshot, "text").Value.Elements.Single()).IsEqualTo("héllo");
            await Assert.That(Attribute(snapshot, "visibility").ResolveSource)
                .IsEqualTo(UsdAttributeResolveSource.Fallback);
            await Assert.That(Attribute(snapshot, "visibility").ValueSource).IsNull();
            UsdAttributePropertySnapshot sampled = Attribute(snapshot, "sampled");
            await Assert.That(sampled.Value.Elements.Single()).IsEqualTo("4");
            await Assert.That(sampled.ResolveSource).IsEqualTo(UsdAttributeResolveSource.TimeSamples);
            await Assert.That(sampled.TimeSamples.Count).IsEqualTo(2UL);
            await Assert.That(sampled.TimeSamples.Times.Single()).IsEqualTo(1d);
            await Assert.That(sampled.TimeSamples.Status).IsEqualTo(UsdPropertyPreviewStatus.Truncated);
            await Assert.That(Attribute(snapshot, "blocked").ValueState).IsEqualTo(UsdPropertyValueState.Blocked);
            await Assert.That(Attribute(snapshot, "blocked").HasAuthoredValueOpinion).IsTrue();
            await Assert.That(Attribute(snapshot, "unset").IsAuthored).IsTrue();
            await Assert.That(Attribute(snapshot, "unset").HasAuthoredValueOpinion).IsFalse();
            await Assert.That(Attribute(snapshot, "unset").ValueState).IsEqualTo(UsdPropertyValueState.Unset);
            UsdAttributePropertySnapshot numbers = Attribute(snapshot, "numbers");
            var targets = (UsdRelationshipPropertySnapshot)snapshot.Properties
                .Single(property => property.Name == "targets");
            await Assert.That(numbers.Value.IsArray).IsTrue();
            if (crate)
            {
                await Assert.That(numbers.Value.Status).IsEqualTo(UsdPropertyPreviewStatus.Deferred);
                await Assert.That(numbers.Value.ElementCount).IsNull();
                await Assert.That(numbers.Value.Reason).Contains("before deserialization");
                await Assert.That(targets.Targets.Status).IsEqualTo(UsdPropertyPreviewStatus.Deferred);
                await Assert.That(targets.Targets.Count).IsNull();
            }
            else
            {
                await Assert.That(numbers.Value.ElementCount).IsEqualTo(5UL);
                await Assert.That(numbers.Value.Elements.Count).IsEqualTo(2);
                await Assert.That(numbers.Value.Elements[1]).IsEqualTo("2");
                await Assert.That(numbers.Value.Status).IsEqualTo(UsdPropertyPreviewStatus.Truncated);
                await Assert.That(targets.Targets.Count).IsEqualTo(3UL);
                await Assert.That(targets.Targets.Paths.Count).IsEqualTo(2);
                await Assert.That(targets.Targets.Paths[1]).IsEqualTo("/B");
            }
            await Assert.That(snapshot.IsComplete).IsFalse();
        }
        finally
        {
            File.Delete(binary);
            File.Delete(path);
        }
    }

    [Test]
    public async Task DefaultTimeAndNumericBlocksKeepAuthoredFallbackAndSampleStatesSeparate()
    {
        string path = await WriteStageAsync(
            """
            #usda 1.0
            def Xform "Subject"
            {
                token visibility = None
                custom int sampleBlock.timeSamples = { 1: None, 3: 7 }
                custom float graphOnly.connect = </Graph.outputs:value>
            }
            """);
        try
        {
            using UsdStage stage = UsdStage.Open(path);
            UsdPrimPropertySnapshot defaults = stage.GetPrimPropertySnapshot("/Subject");
            UsdAttributePropertySnapshot sampleDefault = Attribute(defaults, "sampleBlock");
            await Assert.That(sampleDefault.ValueState).IsEqualTo(UsdPropertyValueState.Unset);
            await Assert.That(sampleDefault.TimeSamples.Count).IsEqualTo(2UL);
            await Assert.That(sampleDefault.HasAuthoredValueOpinion).IsTrue();
            UsdAttributePropertySnapshot visibility = Attribute(defaults, "visibility");
            await Assert.That(visibility.ValueState).IsEqualTo(UsdPropertyValueState.Blocked);
            await Assert.That(visibility.ResolveSource).IsEqualTo(UsdAttributeResolveSource.Fallback);
            await Assert.That(visibility.Value.Elements.Single()).IsEqualTo("inherited");
            UsdAttributePropertySnapshot graph = Attribute(defaults, "graphOnly");
            await Assert.That(graph.ValueState).IsEqualTo(UsdPropertyValueState.Unset);
            await Assert.That(graph.Connections.Paths.Single()).IsEqualTo("/Graph.outputs:value");
            await Assert.That(graph.HasAuthoredValueOpinion).IsFalse();
            UsdAttributePropertySnapshot blocked =
                Attribute(stage.GetPrimPropertySnapshot("/Subject", 1), "sampleBlock");
            await Assert.That(blocked.ValueState).IsEqualTo(UsdPropertyValueState.Blocked);
            await Assert.That(blocked.Value.Elements.Count).IsEqualTo(0);
            await Assert.That(blocked.TimeSamples.Times.Count).IsEqualTo(2);
            UsdAttributePropertySnapshot unblocked =
                Attribute(stage.GetPrimPropertySnapshot("/Subject", 3), "sampleBlock");
            await Assert.That(unblocked.Value.Elements.Single()).IsEqualTo("7");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task NativeReferenceOffsetsTransformTimecodeValuesAndTheSamplePreview()
    {
        string reference = await WriteStageAsync(
            """
            #usda 1.0
            def "Model"
            {
                custom timecode clock = 3
                custom timecode[] clocks = [1, 3, 5]
                custom double sampled.timeSamples = { 1: 2, 3: 6 }
            }
            """);
        string path = await WriteStageAsync(
            $$"""
            #usda 1.0
            def "Subject" (
                references = @{{Path.GetFileName(reference)}}@</Model> (offset = 10; scale = 2)
            ) {}
            """);
        try
        {
            using UsdStage stage = UsdStage.Open(path);
            UsdPrimPropertySnapshot snapshot = stage.GetPrimPropertySnapshot("/Subject", 12);
            await Assert.That(Attribute(snapshot, "clock").Value.Elements.Single()).IsEqualTo("16");
            UsdAttributePropertySnapshot clocks = Attribute(snapshot, "clocks");
            await Assert.That(clocks.Value.Elements.Count).IsEqualTo(3);
            await Assert.That(clocks.Value.Elements[0]).IsEqualTo("12");
            await Assert.That(clocks.Value.Elements[1]).IsEqualTo("16");
            await Assert.That(clocks.Value.Elements[2]).IsEqualTo("20");
            UsdAttributePropertySnapshot sampled = Attribute(snapshot, "sampled");
            await Assert.That(sampled.Value.Elements.Single()).IsEqualTo("2");
            await Assert.That(sampled.TimeSamples.Times[0]).IsEqualTo(12d);
            await Assert.That(sampled.TimeSamples.Times[1]).IsEqualTo(16d);
            await Assert.That(sampled.ValueSource!.SpecPath).IsEqualTo("/Model.sampled");
            await Assert.That(sampled.ValueSource.LayerIdentifier).Contains(Path.GetFileName(reference));
        }
        finally
        {
            File.Delete(path);
            File.Delete(reference);
        }
    }

    [Test]
    public async Task AssetsAreNativeAnchoredAndExpressionExpansionIsExplicitlyDeferred()
    {
        string path = await WriteStageAsync(
            """
            #usda 1.0
            def "Subject"
            {
                custom asset missing = @missing-inspection-asset.bin@
                custom asset expression = @`"${NAME}"`@
                custom asset[] files = [@missing-inspection-asset.bin@, @other.bin@, @third.bin@]
            }
            """);
        try
        {
            using UsdStage stage = UsdStage.Open(path);
            UsdPrimPropertySnapshot snapshot = stage.GetPrimPropertySnapshot("/Subject",
                limits: new UsdPropertyInspectionLimits(previewElements: 2));
            UsdPropertyAssetPath missing = Attribute(snapshot, "missing").Value.Assets.Single();
            await Assert.That(missing.AuthoredPath).IsEqualTo("missing-inspection-asset.bin");
            await Assert.That(missing.EvaluatedPath).IsEmpty();
            await Assert.That(missing.ResolvedPath).IsEmpty();
            await Assert.That(missing.IsMissing).IsTrue();
            await Assert.That(missing.AnchorLayerIdentifier).IsEqualTo(stage.RootLayerIdentifier);
            UsdPropertyValuePreview expression = Attribute(snapshot, "expression").Value;
            await Assert.That(expression.Status).IsEqualTo(UsdPropertyPreviewStatus.Deferred);
            await Assert.That(expression.ElementCount).IsEqualTo(1UL);
            await Assert.That(expression.Reason).Contains("before expansion");
            await Assert.That(expression.Assets.Count).IsEqualTo(0);
            UsdPropertyValuePreview files = Attribute(snapshot, "files").Value;
            await Assert.That(files.ElementCount).IsEqualTo(3UL);
            await Assert.That(files.Assets.Count).IsEqualTo(2);
            await Assert.That(files.Assets[1].AuthoredPath).IsEqualTo("other.bin");
            await Assert.That(files.Status).IsEqualTo(UsdPropertyPreviewStatus.Truncated);
            await Assert.That(() => ((IList<UsdPropertyAssetPath>)files.Assets).Clear())
                .Throws<NotSupportedException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ClipDomainsNeverBecomeSuccessfulEmptyNumericPreviews()
    {
        string path = await WriteStageAsync(
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
            using UsdStage stage = UsdStage.Open(path);
            UsdAttributePropertySnapshot numeric = Attribute(stage.GetPrimPropertySnapshot("/Subject", 1), "value");
            await Assert.That(numeric.ResolveSource).IsEqualTo(UsdAttributeResolveSource.Deferred);
            await Assert.That(numeric.Value.Status).IsEqualTo(UsdPropertyPreviewStatus.Deferred);
            await Assert.That(numeric.Value.ElementCount).IsNull();
            await Assert.That(numeric.Value.Reason).Contains("Value-clip");
            await Assert.That(numeric.HasAuthoredValueOpinion).IsNull();
            UsdAttributePropertySnapshot defaults = Attribute(stage.GetPrimPropertySnapshot("/Subject"), "value");
            await Assert.That(defaults.Value.Elements.Single()).IsEqualTo("7");
            await Assert.That(defaults.TimeSamples.Status).IsEqualTo(UsdPropertyPreviewStatus.Deferred);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static UsdAttributePropertySnapshot Attribute(UsdPrimPropertySnapshot snapshot, string name) =>
        (UsdAttributePropertySnapshot)snapshot.Properties.Single(property => property.Name == name);

    [Test]
    public async Task UnmappableReferenceTargetsAreDeferredRatherThanReportedAsCompleteEmpty()
    {
        string reference = await WriteStageAsync(
            """
            #usda 1.0
            def "Model"
            {
                custom rel target = </Outside>
            }
            def "Outside" {}
            """);
        string path = await WriteStageAsync(
            $$"""
            #usda 1.0
            def "Subject" (references = @{{Path.GetFileName(reference)}}@</Model>) {}
            """);
        try
        {
            using UsdStage stage = UsdStage.Open(path);
            UsdPrimPropertySnapshot snapshot = stage.GetPrimPropertySnapshot("/Subject");
            var target = (UsdRelationshipPropertySnapshot)snapshot.Properties
                .Single(property => property.Name == "target");
            await Assert.That(target.Targets.Status).IsEqualTo(UsdPropertyPreviewStatus.Deferred);
            await Assert.That(target.Targets.Count).IsNull();
            await Assert.That(snapshot.IsComplete).IsFalse();
        }
        finally
        {
            File.Delete(path);
            File.Delete(reference);
        }
    }
}
