// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Tests;

public sealed partial class UsdPrimPropertySnapshotNativeTests
{
    [Test]
    public async Task DefaultTimeSplineCandidateHasTypedReasonsWithoutClaimingAWinner()
    {
        string path = await WriteStageAsync(
            """
            #usda 1.0
            def "Subject"
            {
                custom double curve
            }
            """);
        try
        {
            UsdAttributePropertySnapshot property;
            using (UsdStage stage = UsdStage.Open(path))
            {
                using var spline = new TsSpline();
                spline.SetData(
                [
                    new TsKnot(1, 10, null, 0, 0, 0, 0, TsInterpMode.Linear,
                        TsTangentAlgorithm.None, TsTangentAlgorithm.None),
                    new TsKnot(3, 20, null, 0, 0, 0, 0, TsInterpMode.Held,
                        TsTangentAlgorithm.None, TsTangentAlgorithm.None)
                ]);
                stage.GetPrim("/Subject").GetAttribute("curve").SetSpline(spline);
                ulong serial = stage.ChangeSerial;
                UsdPrimPropertySnapshot snapshot = stage.GetPrimPropertySnapshot("/Subject");
                await Assert.That(snapshot.TimeCode).IsNull();
                await Assert.That(snapshot.ChangeSerial).IsEqualTo(serial);
                await Assert.That(stage.ChangeSerial).IsEqualTo(serial);
                property = Attribute(snapshot, "curve");
            }

            await Assert.That(property.ResolveSource).IsEqualTo(UsdAttributeResolveSource.Deferred);
            await Assert.That(property.ValueState).IsEqualTo(UsdPropertyValueState.Deferred);
            await Assert.That(property.ValueSource).IsNull();
            await Assert.That(property.Value.Status).IsEqualTo(UsdPropertyPreviewStatus.Deferred);
            await Assert.That(property.Value.ReasonKind).IsEqualTo(UsdPropertyPreviewReasonKind.Spline);
            await Assert.That(property.TimeSamples.ReasonKind).IsEqualTo(UsdPropertyPreviewReasonKind.Spline);
            await Assert.That(property.Connections.ReasonKind).IsEqualTo(UsdPropertyPreviewReasonKind.None);
            await Assert.That(property.Value.Reason).IsEqualTo("Spline evaluation and knot metadata are deferred.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StructuredReasonsDistinguishIndependentPreviewDomains(bool crate)
    {
        string path = await WriteStageAsync(
            """
            #usda 1.0
            def "Subject"
            {
                custom int scalar = 7
                custom int[] numbers = [1, 2, 3]
                custom string text = "éééé"
                custom double sampled.timeSamples = { 1: 2, 3: 6 }
                custom asset expression = @`"${NAME}"`@
                custom rel targets = [</A>, </B>]
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
            UsdPrimPropertySnapshot snapshot = stage.GetPrimPropertySnapshot("/Subject", 1,
                new UsdPropertyInspectionLimits(previewElements: 1, timeSamplePreview: 1,
                    targetPreview: 1, maximumPreviewTextBytes: 5));
            UsdAttributePropertySnapshot scalar = Attribute(snapshot, "scalar");
            await Assert.That(scalar.Value.ReasonKind).IsEqualTo(UsdPropertyPreviewReasonKind.None);
            await Assert.That(scalar.Value.Reason).IsEmpty();
            await Assert.That(scalar.TimeSamples.ReasonKind).IsEqualTo(UsdPropertyPreviewReasonKind.None);
            await Assert.That(scalar.Connections.ReasonKind).IsEqualTo(UsdPropertyPreviewReasonKind.None);
            UsdPropertyPreviewReasonKind listReason = crate
                ? UsdPropertyPreviewReasonKind.DeferredStore
                : UsdPropertyPreviewReasonKind.PreviewLimit;
            await Assert.That(Attribute(snapshot, "numbers").Value.ReasonKind).IsEqualTo(listReason);
            await Assert.That(Attribute(snapshot, "sampled").TimeSamples.ReasonKind)
                .IsEqualTo(UsdPropertyPreviewReasonKind.PreviewLimit);
            await Assert.That(Attribute(snapshot, "text").Value.ReasonKind)
                .IsEqualTo(UsdPropertyPreviewReasonKind.TextLimit);
            await Assert.That(Attribute(snapshot, "expression").Value.ReasonKind)
                .IsEqualTo(UsdPropertyPreviewReasonKind.AssetExpression);
            var targets = (UsdRelationshipPropertySnapshot)snapshot.Properties
                .Single(property => property.Name == "targets");
            await Assert.That(targets.Targets.ReasonKind).IsEqualTo(listReason);
        }
        finally
        {
            File.Delete(binary);
            File.Delete(path);
        }
    }

    [Test]
    public async Task ClipReasonIsStructuredAndDoesNotBecomeASplineCandidate()
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
            UsdAttributePropertySnapshot value = Attribute(stage.GetPrimPropertySnapshot("/Subject", 1), "value");
            await Assert.That(value.Value.ReasonKind).IsEqualTo(UsdPropertyPreviewReasonKind.ValueClips);
            await Assert.That(value.TimeSamples.ReasonKind).IsEqualTo(UsdPropertyPreviewReasonKind.ValueClips);
            await Assert.That(value.ResolveSource).IsEqualTo(UsdAttributeResolveSource.Deferred);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task UnmappableNativeTargetsExposeStructuredCompositionReason()
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
            var target = (UsdRelationshipPropertySnapshot)snapshot.Properties.Single();
            await Assert.That(target.Targets.ReasonKind).IsEqualTo(UsdPropertyPreviewReasonKind.Composition);
            await Assert.That(target.Targets.Status).IsEqualTo(UsdPropertyPreviewStatus.Deferred);
            await Assert.That(target.Targets.Count).IsNull();
        }
        finally
        {
            File.Delete(path);
            File.Delete(reference);
        }
    }
}
