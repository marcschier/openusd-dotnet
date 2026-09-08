// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using OpenUsd.Editing;
using OpenUsd.Interop;

namespace OpenUsd.Tests;

public sealed class UsdReviewDocumentCodecTests
{
    [Test]
    public async Task SourceBindingDecodesExactLittleEndianMetadataAndOwnsItsStorage()
    {
        byte[] bytes = ReviewMetadataPackets.Binding();
        UsdReviewSourceBinding binding = DecodeBinding(bytes);
        UsdReviewSourceBinding identical = DecodeBinding((byte[])bytes.Clone());
        int length = bytes.Length;
        await Assert.That(Convert.ToHexString(bytes.AsSpan(0, 16)))
            .IsEqualTo("52534231010000002A00000000000000");
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), 43);
        UsdReviewSourceBinding otherStage = DecodeBinding(bytes);
        bytes.AsSpan().Clear();

        await Assert.That(binding.StageId).IsEqualTo(42ul);
        await Assert.That(binding.SourceRootPath).IsEqualTo(ReviewMetadataPackets.Root);
        await Assert.That(binding.SourceFingerprint).IsEqualTo(ReviewMetadataPackets.Fingerprint);
        await Assert.That(binding.AssetAnchor).IsEqualTo(ReviewMetadataPackets.Root);
        await Assert.That(binding.ByteLength).IsEqualTo(length);
        await Assert.That(binding.HasSamePayload(identical)).IsTrue();
        await Assert.That(binding.HasSamePayload(otherStage)).IsFalse();
        await Assert.That(binding.HasSamePayload(null)).IsFalse();
        await Assert.That(binding.Dependencies.Count).IsEqualTo(2);
        await Assert.That(binding.Dependencies[0].Path).IsEqualTo(ReviewMetadataPackets.Root);
        await Assert.That(binding.Dependencies[0].Sha256).IsEqualTo(ReviewMetadataPackets.Fingerprint);
        await Assert.That(binding.Dependencies[0].ByteLength).IsEqualTo(321ul);
        await Assert.That(binding.Dependencies[0].Kind).IsEqualTo(UsdReviewDependencyKind.Layer);
        await Assert.That(binding.Dependencies[1].Path).IsEqualTo(ReviewMetadataPackets.Asset);
        await Assert.That(binding.Dependencies[1].ByteLength).IsEqualTo(765ul);
        await Assert.That(binding.Dependencies[1].Kind).IsEqualTo(UsdReviewDependencyKind.Asset);
        await Assert.That(() => ((IList<UsdReviewDependency>)binding.Dependencies)[0] = null!)
            .Throws<NotSupportedException>();
        UsdStageBoundResultGuard.ThrowIfForbiddenResult(binding);
        UsdStageBoundResultGuard.ThrowIfForbiddenResult(binding.Dependencies[0]);
    }

    [Test]
    public async Task CaptureAndReadKeepPortableBytesDetachedAndNeverSerializeSaveReceipts()
    {
        byte[] envelope = ReviewMetadataPackets.Envelope();
        UsdReviewDocument capture = DecodeCapture(envelope);
        UsdReviewDocument read = DecodeRead(ReviewMetadataPackets.Envelope(captured: false));
        await Assert.That(Convert.ToHexString(envelope.AsSpan(0, 8))).IsEqualTo("5244453101000000");
        envelope.AsSpan().Clear();
        capture.CopyBytes().AsSpan().Clear();

        await Assert.That(capture.Version).IsEqualTo(1u);
        await Assert.That(capture.DocumentId).IsEqualTo(ReviewMetadataPackets.DocumentId);
        await Assert.That(capture.SourceRootPath).IsEqualTo(ReviewMetadataPackets.Root);
        await Assert.That(capture.SourceFingerprint).IsEqualTo(ReviewMetadataPackets.Fingerprint);
        await Assert.That(capture.OriginalTargetIdentifier).IsEqualTo("anon:original-review.usda");
        await Assert.That(capture.TargetDocumentPath).IsEqualTo(ReviewMetadataPackets.Target);
        await Assert.That(capture.AssetAnchor).IsEqualTo(ReviewMetadataPackets.Root);
        await Assert.That(capture.ByteLength).IsEqualTo(ReviewMetadataPackets.DocumentBytes.Length);
        await Assert.That(capture.CopyBytes().SequenceEqual(ReviewMetadataPackets.DocumentBytes)).IsTrue();
        await Assert.That(capture.HasSamePayload(read)).IsTrue();
        await Assert.That(capture.HasSamePayload(null)).IsFalse();
        using var layer = new UsdLayer(new OpenUsdNativeLayer(0));
        await Assert.That(layer.AcknowledgeSaved(read)).IsFalse();
        await Assert.That(() => ((IList<UsdReviewDependency>)capture.Dependencies).Clear())
            .Throws<NotSupportedException>();
        UsdStageBoundResultGuard.ThrowIfForbiddenResult(capture);
        UsdStageBoundResultGuard.ThrowIfForbiddenResult(read);
    }

    [Test]
    public async Task ConstructorsCopyDependencyCollectionsAndAllByteInputs()
    {
        var dependency = new UsdReviewDependency(ReviewMetadataPackets.Root,
            ReviewMetadataPackets.Fingerprint, 3, UsdReviewDependencyKind.Layer);
        UsdReviewDependency[] dependencies = [dependency];
        byte[] bytes = [1, 2, 3];
        byte[] receipt = [4, 5, 6];
        var binding = new UsdReviewSourceBinding(42, ReviewMetadataPackets.Root,
            ReviewMetadataPackets.Fingerprint, ReviewMetadataPackets.Root, dependencies, bytes);
        var document = new UsdReviewDocument(ReviewMetadataPackets.DocumentId, ReviewMetadataPackets.Root,
            ReviewMetadataPackets.Fingerprint, "original", ReviewMetadataPackets.Target,
            ReviewMetadataPackets.Root, dependencies, bytes, receipt);
        dependencies[0] = null!;
        bytes[0] = 99;
        receipt[0] = 99;

        await Assert.That(binding.Dependencies[0]).IsSameReferenceAs(dependency);
        await Assert.That(document.Dependencies[0]).IsSameReferenceAs(dependency);
        await Assert.That(binding.Payload[0]).IsEqualTo((byte)1);
        await Assert.That(document.CopyBytes()).IsEquivalentTo(new byte[] { 1, 2, 3 });
        await Assert.That(typeof(UsdReviewDocument).GetConstructors().Length).IsEqualTo(0);
        await Assert.That(typeof(UsdReviewSourceBinding).GetConstructors().Length).IsEqualTo(0);
        await Assert.That(typeof(UsdReviewDependency).GetConstructors().Length).IsEqualTo(0);
        await Assert.That(typeof(UsdReviewDocument).GetMethod("FromBytes")).IsNull();
    }

    [Test]
    public async Task ByteLengthAndPayloadComparisonAllocateNoCopies()
    {
        UsdReviewSourceBinding binding = DecodeBinding(ReviewMetadataPackets.Binding());
        UsdReviewSourceBinding identicalBinding = DecodeBinding(ReviewMetadataPackets.Binding());
        UsdReviewDocument document = DecodeCapture(ReviewMetadataPackets.Envelope());
        UsdReviewDocument identicalDocument = DecodeRead(ReviewMetadataPackets.Envelope(captured: false));
        byte[] changed = ReviewMetadataPackets.Envelope();
        changed[12] ^= 1;
        UsdReviewDocument differentDocument = DecodeCapture(changed);
        _ = CompareRepeatedly(binding, identicalBinding, document, identicalDocument);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int matches = CompareRepeatedly(binding, identicalBinding, document, identicalDocument);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(matches).IsEqualTo(2000);
        await Assert.That(allocated).IsEqualTo(0);
        await Assert.That(document.HasSamePayload(differentDocument)).IsFalse();
    }

    [Test]
    [Arguments("magic")]
    [Arguments("version")]
    [Arguments("stage-id")]
    [Arguments("empty-root")]
    [Arguments("relative-root")]
    [Arguments("root-mismatch")]
    [Arguments("relative-anchor")]
    [Arguments("fingerprint-size")]
    [Arguments("fingerprint-hex")]
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
    public async Task MalformedSourceBindingsFailClosed(string corruption)
    {
        await Assert.That(() => DecodeBinding(ReviewMetadataPackets.Binding(corruption)))
            .Throws<OpenUsdNativeException>();
    }

    [Test]
    [Arguments("magic")]
    [Arguments("version")]
    [Arguments("empty-document")]
    [Arguments("document-limit")]
    [Arguments("document-overflow")]
    [Arguments("receipt-overflow")]
    [Arguments("guid")]
    [Arguments("guid-upper")]
    [Arguments("guid-braces")]
    [Arguments("empty-root")]
    [Arguments("relative-root")]
    [Arguments("root-mismatch")]
    [Arguments("fingerprint-size")]
    [Arguments("fingerprint-hex")]
    [Arguments("fingerprint-mismatch")]
    [Arguments("empty-identifier")]
    [Arguments("relative-target")]
    [Arguments("target-mismatch")]
    [Arguments("relative-anchor")]
    [Arguments("anchor-mismatch")]
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
    public async Task MalformedCaptureEnvelopesFailClosed(string corruption)
    {
        await Assert.That(() => DecodeCapture(ReviewMetadataPackets.Envelope(corruption)))
            .Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task ReceiptPresenceAndReadByteIdentityAreMandatory()
    {
        await Assert.That(() => DecodeCapture(ReviewMetadataPackets.Envelope(captured: false)))
            .Throws<OpenUsdNativeException>();
        await Assert.That(() => DecodeRead(ReviewMetadataPackets.Envelope()))
            .Throws<OpenUsdNativeException>();
        byte[] changed = ReviewMetadataPackets.Envelope(captured: false);
        changed[12] ^= 1;
        await Assert.That(() => DecodeRead(changed)).Throws<OpenUsdNativeException>();
        await Assert.That(() => UsdReviewDocumentCodec.DecodeReadDocument(
            ReviewMetadataPackets.Envelope(captured: false), ReviewMetadataPackets.DocumentBytes,
            ReviewMetadataPackets.Target)).Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task EveryTruncatedEnvelopeFailsAndLargerBudgetsDoNotWeakenEditingLimits()
    {
        byte[] binding = ReviewMetadataPackets.Binding();
        byte[] envelope = ReviewMetadataPackets.Envelope();
        for (int length = 0; length < binding.Length; length++)
        {
            int extent = length;
            await Assert.That(() => DecodeBinding(binding.AsSpan(0, extent))).Throws<OpenUsdNativeException>();
        }
        for (int length = 0; length < envelope.Length; length++)
        {
            int extent = length;
            await Assert.That(() => DecodeCapture(envelope.AsSpan(0, extent))).Throws<OpenUsdNativeException>();
        }
        var tooLarge = new byte[UsdReviewDocumentCodec.MaximumEnvelopeBytes + 1];
        await Assert.That(() => DecodeBinding(tooLarge)).Throws<OpenUsdNativeException>();
        await Assert.That(() => DecodeCapture(tooLarge)).Throws<OpenUsdNativeException>();
        await Assert.That(() => UsdReviewDocument.Read(tooLarge, ReviewMetadataPackets.Root))
            .Throws<ArgumentException>();
        await Assert.That(() => UsdLayerEditCodec.DecodeState(new byte[(4 * 1024 * 1024) + 1]))
            .Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task ExactDocumentTextAndDependencyBoundariesAreAdmittedWithoutTruncation()
    {
        var bytes = new byte[16 * 1024 * 1024];
        bytes[0] = 0x71;
        bytes[^1] = 0x93;
        string identifier = new('\u00e9', 2048);
        byte[] envelope = ReviewMetadataPackets.Envelope(
            portableBytes: bytes, originalIdentifier: identifier, dependencyCount: 1024);
        UsdReviewDocument document = DecodeCapture(envelope);
        UsdReviewSourceBinding binding = DecodeBinding(ReviewMetadataPackets.Binding(dependencyCount: 1024));
        byte[] copied = document.CopyBytes();
        bytes[^1] = 0;
        envelope.AsSpan().Clear();

        await Assert.That(document.ByteLength).IsEqualTo(16 * 1024 * 1024);
        await Assert.That(copied[0]).IsEqualTo((byte)0x71);
        await Assert.That(copied[^1]).IsEqualTo((byte)0x93);
        await Assert.That(document.OriginalTargetIdentifier).IsEqualTo(identifier);
        await Assert.That(document.Dependencies.Count).IsEqualTo(1024);
        await Assert.That(binding.Dependencies.Count).IsEqualTo(1024);
        await Assert.That(document.Dependencies[^1].Path)
            .IsEqualTo(Path.ChangeExtension(ReviewMetadataPackets.Root, ".1023.bin"));
        await Assert.That(binding.Dependencies[^1].Kind).IsEqualTo(UsdReviewDependencyKind.Asset);
    }

    [Test]
    public async Task ImportUsesExistingStrictStatePacketAndNewLocalReviewHistory()
    {
        UsdReviewSourceBinding binding = DecodeBinding(ReviewMetadataPackets.Binding());
        UsdReviewDocumentImportResult result = UsdReviewDocumentCodec.DecodeImportResult(
            new OpenUsdNativeLayerEditResult(0, ReviewMetadataPackets.State(), null), binding);
        await Assert.That(result.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        await Assert.That(result.AfterState!.Identity).IsEqualTo(new UsdLayerIdentity(42, 101, 3));
        await Assert.That(result.AfterState.Revision).IsEqualTo(9ul);
        await Assert.That(result.AfterState.Role).IsEqualTo(UsdLayerRole.UserReview);
        await Assert.That(result.AfterState.CurrentlyLocal).IsTrue();
        await Assert.That(result.AfterState.PermissionToEdit).IsTrue();
        await Assert.That(result.Diagnostic).IsNull();
        UsdStageBoundResultGuard.ThrowIfForbiddenResult(result);
        UsdStageBoundResultGuard.ThrowIfForbiddenResult(result.AfterState);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task ImportConflictResultsHaveNoAfterStateAndKeepDiagnostics(int outcome)
    {
        UsdReviewDocumentImportResult result = UsdReviewDocumentCodec.DecodeImportResult(
            new OpenUsdNativeLayerEditResult(outcome, null, "explicit reconciliation required"),
            DecodeBinding(ReviewMetadataPackets.Binding()));
        await Assert.That(result.Outcome).IsEqualTo((UsdLayerEditOutcome)outcome);
        await Assert.That(result.AfterState).IsNull();
        await Assert.That(result.Diagnostic).IsEqualTo("explicit reconciliation required");
    }

    [Test]
    [Arguments("negative-outcome")]
    [Arguments("unknown-outcome")]
    [Arguments("missing-state")]
    [Arguments("conflict-state")]
    [Arguments("wrong-stage")]
    [Arguments("wrong-role")]
    [Arguments("not-local")]
    [Arguments("not-editable")]
    [Arguments("unknown-flags")]
    [Arguments("trailing-state")]
    public async Task InvalidImportStateCannotEscapeAsADetachedResult(string corruption)
    {
        int outcome = corruption switch
        {
            "negative-outcome" => -1,
            "unknown-outcome" => 4,
            "conflict-state" => 1,
            _ => 0
        };
        byte[]? state = corruption switch
        {
            "missing-state" => null,
            "wrong-stage" => ReviewMetadataPackets.State(stageId: 99),
            "wrong-role" => ReviewMetadataPackets.State(role: 3),
            "not-local" => ReviewMetadataPackets.State(flags: 8),
            "not-editable" => ReviewMetadataPackets.State(flags: 32),
            "unknown-flags" => ReviewMetadataPackets.State(flags: 106),
            "trailing-state" => [.. ReviewMetadataPackets.State(), 0],
            _ => ReviewMetadataPackets.State()
        };
        await Assert.That(() => UsdReviewDocumentCodec.DecodeImportResult(
            new OpenUsdNativeLayerEditResult(outcome, state, null), DecodeBinding(ReviewMetadataPackets.Binding())))
            .Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task PublicArgumentValidationNeverLoadsNativeForInvalidInput()
    {
        UsdReviewSourceBinding binding = DecodeBinding(ReviewMetadataPackets.Binding());
        UsdReviewDocument document = DecodeRead(ReviewMetadataPackets.Envelope(captured: false));
        using var layer = new UsdLayer(new OpenUsdNativeLayer(0));
        await Assert.That(() => UsdStage.OpenForReview(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => UsdStage.OpenForReview(" ")).Throws<ArgumentException>();
        await Assert.That(() => UsdStage.OpenForReview("bad\0path")).Throws<ArgumentException>();
        await Assert.That(() => UsdReviewDocument.Read([], ReviewMetadataPackets.Root)).Throws<ArgumentException>();
        await Assert.That(() => UsdReviewDocument.Read([1], null!)).Throws<ArgumentNullException>();
        await Assert.That(() => UsdReviewDocument.Read([1], "bad\0path")).Throws<ArgumentException>();
        await Assert.That(() => ((UsdStage)null!).CaptureReviewSourceBinding()).Throws<ArgumentNullException>();
        await Assert.That(() => layer.CaptureReviewDocument(binding, "relative.review")).Throws<ArgumentException>();
        await Assert.That(() => layer.CaptureReviewDocument(null!, ReviewMetadataPackets.Target))
            .Throws<ArgumentNullException>();
        await Assert.That(() => ((UsdLayer)null!).CaptureReviewDocument(binding, ReviewMetadataPackets.Target))
            .Throws<ArgumentNullException>();
        await Assert.That(() => ((UsdStage)null!).ImportReviewDocument(document, binding))
            .Throws<ArgumentNullException>();
        await Assert.That(() => layer.AcknowledgeSaved((UsdReviewDocument)null!)).Throws<ArgumentNullException>();
        await Assert.That(() => ((UsdLayer)null!).AcknowledgeSaved(document)).Throws<ArgumentNullException>();
    }

    private static UsdReviewSourceBinding DecodeBinding(ReadOnlySpan<byte> bytes) =>
        UsdReviewDocumentCodec.DecodeSourceBinding(bytes, ReviewMetadataPackets.Root);

    private static UsdReviewDocument DecodeCapture(ReadOnlySpan<byte> bytes) =>
        UsdReviewDocumentCodec.DecodeCapturedDocument(
            bytes, DecodeBinding(ReviewMetadataPackets.Binding()), ReviewMetadataPackets.Target);

    private static UsdReviewDocument DecodeRead(ReadOnlySpan<byte> bytes) =>
        UsdReviewDocumentCodec.DecodeReadDocument(
            bytes, ReviewMetadataPackets.DocumentBytes, ReviewMetadataPackets.Root);

    private static int CompareRepeatedly(
        UsdReviewSourceBinding binding, UsdReviewSourceBinding otherBinding,
        UsdReviewDocument document, UsdReviewDocument otherDocument)
    {
        int matches = 0;
        for (int index = 0; index < 1000; index++)
        {
            matches += binding.ByteLength == otherBinding.ByteLength && binding.HasSamePayload(otherBinding) ? 1 : 0;
            matches += document.ByteLength == otherDocument.ByteLength && document.HasSamePayload(otherDocument) ? 1 : 0;
        }
        return matches;
    }
}
