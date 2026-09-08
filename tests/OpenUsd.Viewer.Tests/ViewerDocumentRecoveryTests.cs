// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerDocumentRecoveryTests
{
    [Test]
    public async Task RecoveryCanOnlyBeOfferedForTheSameSourceReviewLineageAndAssetAnchor()
    {
        var binding = new ViewerRecoveryBinding(
            "source.usda", new string('A', 64), "review-document-1", "source-directory");
        var checkpoint = new ViewerDocumentRecovery(
            1, binding, "native-layer-format-1", ViewerDocumentLayerRole.Review, [1, 2, 3]);

        await Assert.That(checkpoint.Validate(binding, "native-layer-format-1").CanOffer).IsTrue();
        await Assert.That(checkpoint.Validate(
            binding with { SourceFingerprint = new string('B', 64) }, "native-layer-format-1").CanOffer).IsFalse();
        await Assert.That(checkpoint.Validate(
            binding with { ReviewIdentity = "another-review" }, "native-layer-format-1").CanOffer).IsFalse();
        await Assert.That(checkpoint.Validate(
            binding with { AssetAnchorIdentity = "another-directory" }, "native-layer-format-1").CanOffer).IsFalse();
        await Assert.That(checkpoint.Validate(binding, "another-native-format").CanOffer).IsFalse();
    }

    [Test]
    public async Task CheckpointOwnsBoundedOpaqueReviewBytesAndRejectsOtherLayerRoles()
    {
        var binding = new ViewerRecoveryBinding("source", new string('A', 64), "review-1", "anchor-1");
        byte[] bytes = [1, 2, 3];
        var checkpoint = new ViewerDocumentRecovery(1, binding, "native-1", ViewerDocumentLayerRole.Review, bytes);
        bytes[0] = 99;
        byte[] copy = checkpoint.CopyPayload();
        copy[1] = 99;

        await Assert.That(Convert.ToHexString(checkpoint.CopyPayload())).IsEqualTo("010203");
        await Assert.That(checkpoint.PayloadBytes).IsEqualTo(3);
        await Assert.That(() => new ViewerDocumentRecovery(
            1, binding, "native-1", ViewerDocumentLayerRole.Simulation, bytes)).Throws<ArgumentException>();
        byte[] oversized = new byte[8_388_609];
        await Assert.That(() => new ViewerDocumentRecovery(
            1, binding, "native-1", ViewerDocumentLayerRole.Review, oversized)).Throws<ArgumentOutOfRangeException>();
        var future = new ViewerDocumentRecovery(2, binding, "native-1", ViewerDocumentLayerRole.Review, bytes);
        await Assert.That(future.Validate(binding, "native-1").CanOffer).IsFalse();
    }
}
