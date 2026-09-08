// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;
using OpenUsd.Interop;

namespace OpenUsd.Tests;

public sealed class UsdReviewDocumentInspectionTests
{
    [Test]
    public async Task MetadataOnlyDescriptorIsDetachedAndCannotImportOrAcknowledge()
    {
        byte[] packet = ReviewMetadataPackets.Inspection();
        UsdReviewDocumentInfo info = UsdReviewDocumentCodec.DecodeInspection(
            packet, ReviewMetadataPackets.DocumentBytes.Length);
        packet.AsSpan().Clear();

        await Assert.That(info.Version).IsEqualTo(1u);
        await Assert.That(info.DocumentByteLength).IsEqualTo(8);
        await Assert.That(info.DocumentId).IsEqualTo(ReviewMetadataPackets.DocumentId);
        await Assert.That(info.SourceRootPath).IsEqualTo(ReviewMetadataPackets.Root);
        await Assert.That(info.SourceFingerprint).IsEqualTo(ReviewMetadataPackets.Fingerprint);
        await Assert.That(info.OriginalTargetIdentifier).IsEqualTo("anon:original-review.usda");
        await Assert.That(info.TargetDocumentPath).IsEqualTo(ReviewMetadataPackets.Target);
        await Assert.That(info.AssetAnchor).IsEqualTo(ReviewMetadataPackets.Root);
        await Assert.That(info.Dependencies.Count).IsEqualTo(2);
        await Assert.That(info.Dependencies[1].Path).IsEqualTo(ReviewMetadataPackets.Asset);
        await Assert.That(() => ((IList<UsdReviewDependency>)info.Dependencies).Clear())
            .Throws<NotSupportedException>();
        await Assert.That(typeof(UsdReviewDocumentInfo).GetConstructors().Length).IsEqualTo(0);
        await Assert.That(typeof(UsdReviewDocumentInfo).GetMethod("CopyBytes")).IsNull();
        await Assert.That(typeof(UsdReviewDocument).IsAssignableFrom(typeof(UsdReviewDocumentInfo))).IsFalse();
        UsdStageBoundResultGuard.ThrowIfForbiddenResult(info);
    }

    [Test]
    [Arguments("magic")]
    [Arguments("version")]
    [Arguments("empty-document")]
    [Arguments("document-limit")]
    [Arguments("document-overflow")]
    [Arguments("document-mismatch")]
    [Arguments("guid")]
    [Arguments("guid-upper")]
    [Arguments("guid-braces")]
    [Arguments("empty-root")]
    [Arguments("relative-root")]
    [Arguments("fingerprint-size")]
    [Arguments("fingerprint-hex")]
    [Arguments("empty-identifier")]
    [Arguments("relative-target")]
    [Arguments("relative-anchor")]
    [Arguments("utf8")]
    [Arguments("nul")]
    [Arguments("text-limit")]
    [Arguments("text-overflow")]
    [Arguments("dependency-limit")]
    [Arguments("dependency-overflow")]
    [Arguments("dependency-extent")]
    [Arguments("relative-dependency")]
    [Arguments("duplicate-dependency")]
    [Arguments("dependency-sha-size")]
    [Arguments("dependency-sha-hex")]
    [Arguments("dependency-kind")]
    [Arguments("truncated")]
    [Arguments("trailing")]
    public async Task InvalidInspectionMetadataNeverEscapes(string corruption)
    {
        await Assert.That(() => UsdReviewDocumentCodec.DecodeInspection(
            ReviewMetadataPackets.Inspection(corruption), ReviewMetadataPackets.DocumentBytes.Length))
            .Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task InspectionRejectsInvalidInputBeforeLoadingNative()
    {
        await Assert.That(() => UsdReviewDocument.Inspect([])).Throws<ArgumentException>();
        byte[] oversized = new byte[UsdReviewDocumentCodec.MaximumDocumentBytes + 1];
        await Assert.That(() => UsdReviewDocument.Inspect(oversized)).Throws<ArgumentException>();
    }
}
