// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Tests;

public sealed partial class UsdPrimPropertySnapshotNativeTests
{
    [Test]
    public async Task ZeroPreviewsRetainExactCountsAndNeverReturnAnEmptyCompleteValue()
    {
        string path = await WriteStageAsync(
            """
            #usda 1.0
            def "Subject"
            {
                custom int[] numbers = [1, 2, 3]
                custom double sampled.timeSamples = { 1: 2, 3: 6 }
                custom rel target = </Other>
            }
            """);
        try
        {
            using UsdStage stage = UsdStage.Open(path);
            UsdPrimPropertySnapshot snapshot = stage.GetPrimPropertySnapshot("/Subject", 1,
                new UsdPropertyInspectionLimits(previewElements: 0, timeSamplePreview: 0, targetPreview: 0));
            UsdPropertyValuePreview value = Attribute(snapshot, "numbers").Value;
            await Assert.That(value.ElementCount).IsEqualTo(3UL);
            await Assert.That(value.Elements.Count).IsEqualTo(0);
            await Assert.That(value.Status).IsEqualTo(UsdPropertyPreviewStatus.Truncated);
            UsdPropertyTimeSamplePreview times = Attribute(snapshot, "sampled").TimeSamples;
            await Assert.That(times.Count).IsEqualTo(2UL);
            await Assert.That(times.Times.Count).IsEqualTo(0);
            await Assert.That(times.Status).IsEqualTo(UsdPropertyPreviewStatus.Truncated);
            var target = (UsdRelationshipPropertySnapshot)snapshot.Properties
                .Single(property => property.Name == "target");
            await Assert.That(target.Targets.Count).IsEqualTo(1UL);
            await Assert.That(target.Targets.Paths.Count).IsEqualTo(0);
            await Assert.That(target.Targets.Status).IsEqualTo(UsdPropertyPreviewStatus.Truncated);
            await Assert.That(target.Targets.Source!.SpecPath).IsEqualTo("/Subject.target");
            await Assert.That(snapshot.IsComplete).IsFalse();
            await Assert.That(() => stage.GetPrimPropertySnapshot("/Subject",
                limits: new UsdPropertyInspectionLimits(maximumPropertyCount: 1))).Throws<OpenUsdNativeException>();
            await Assert.That(() => stage.GetPrimPropertySnapshot("/Subject",
                limits: new UsdPropertyInspectionLimits(maximumMetadataWork: 0))).Throws<OpenUsdNativeException>();
            await Assert.That(() => stage.GetPrimPropertySnapshot("/Subject",
                limits: new UsdPropertyInspectionLimits(maximumTextBytes: 1))).Throws<OpenUsdNativeException>();
            await Assert.That(() => stage.GetPrimPropertySnapshot("/Missing")).Throws<OpenUsdNativeException>();
            await Assert.That(() => stage.GetPrimPropertySnapshot("/Subject", double.NaN))
                .Throws<ArgumentOutOfRangeException>();
            await Assert.That(stage.GetPrimPropertySnapshot("/Subject").Properties.Count).IsEqualTo(3);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task Utf8PrefixesAndUnicodePropertyOrderRemainCanonicalAndBounded()
    {
        string path = await WriteStageAsync(
            """
            #usda 1.0
            def "Subject"
            {
                custom string text = "é😀é😀é"
                custom int 豈 = 9
                custom int 𐀀 = 10
            }
            """);
        try
        {
            using UsdStage stage = UsdStage.Open(path);
            UsdPrimPropertySnapshot snapshot = stage.GetPrimPropertySnapshot("/Subject",
                limits: new UsdPropertyInspectionLimits(maximumPreviewTextBytes: 7));
            UsdPropertyValuePreview text = Attribute(snapshot, "text").Value;
            await Assert.That(text.Elements.Single()).IsEqualTo("é😀");
            await Assert.That(text.Status).IsEqualTo(UsdPropertyPreviewStatus.Truncated);
            await Assert.That(text.Reason).Contains("UTF-8");
            await Assert.That(snapshot.Properties[1].Name).IsEqualTo("豈");
            await Assert.That(snapshot.Properties[2].Name).IsEqualTo("𐀀");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task VariantWinningSpecProvenanceRetainsItsNativeNamespace()
    {
        string path = await WriteStageAsync(
            """
            #usda 1.0
            def "Subject" (
                prepend variantSets = "look"
                variants = { string look = "warm" }
            )
            {
                variantSet "look" = {
                    "warm" {
                        custom uniform double value = 17
                    }
                    "cool" {
                        custom uniform double value = 9
                    }
                }
            }
            """);
        try
        {
            using UsdStage stage = UsdStage.Open(path);
            UsdAttributePropertySnapshot value = Attribute(stage.GetPrimPropertySnapshot("/Subject"), "value");
            await Assert.That(value.Value.Elements.Single()).IsEqualTo("17");
            await Assert.That(value.Variability).IsEqualTo(UsdPropertyVariability.Uniform);
            await Assert.That(value.ValueSource!.SpecPath).IsEqualTo("/Subject{look=warm}.value");
            await Assert.That(value.ValueSource.LayerIdentifier).IsEqualTo(stage.RootLayerIdentifier);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
