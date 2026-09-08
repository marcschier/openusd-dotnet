// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Interop.Tests;

public sealed class ReviewDocumentBufferTests
{
    [Test]
    public async Task PortableCopiesHaveAnIndependentBoundWithoutWeakeningEditingPackets()
    {
        var bytes = new byte[OpenUsdNativeRuntime.LayerEditingMaximumBytes + 1];
        bytes[0] = 0x52;
        bytes[^1] = 0x31;
        byte[] copy = Copy(bytes);
        bytes[0] = 0;
        await Assert.That(copy.Length).IsEqualTo((4 * 1024 * 1024) + 1);
        await Assert.That(copy[0]).IsEqualTo((byte)0x52);
        await Assert.That(copy[^1]).IsEqualTo((byte)0x31);
        await Assert.That(() => CopyEditing(bytes)).Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task ExactPortableEnvelopeLimitIsCopiedWithoutTruncation()
    {
        var bytes = new byte[OpenUsdNativeRuntime.ReviewMaximumEnvelopeBytes];
        bytes[0] = 1;
        bytes[^1] = 9;
        byte[] copy = Copy(bytes);
        copy[0] = 2;
        await Assert.That(copy.Length).IsEqualTo(24 * 1024 * 1024);
        await Assert.That(bytes[0]).IsEqualTo((byte)1);
        await Assert.That(copy[^1]).IsEqualTo((byte)9);
    }

    [Test]
    [Arguments("missing-owner")]
    [Arguments("invalid-owner")]
    [Arguments("misaligned-owner")]
    [Arguments("missing-data")]
    [Arguments("empty-size")]
    [Arguments("over-budget")]
    [Arguments("size-overflow")]
    [Arguments("pointer-overflow")]
    public async Task InvalidPortableOwnershipAndExtentsFailBeforeDereference(string corruption)
    {
        await Assert.That(() => Copy([1, 2, 3, 4], corruption)).Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task InvalidInputsAreRejectedBeforeNativeLoadingOrHandleLeasing()
    {
        using var layer = new OpenUsdNativeLayer(0);
        using var stage = new OpenUsdNativeStage(0);
        await Assert.That(() => OpenUsdNativeRuntime.CaptureReviewSourceBinding(null!))
            .Throws<ArgumentNullException>();
        await Assert.That(() => OpenUsdNativeRuntime.OpenStageForReview(new string('a', 4097)))
            .Throws<ArgumentException>();
        await Assert.That(() => OpenUsdNativeRuntime.OpenStageForReview(new string('\u00e9', 2049)))
            .Throws<ArgumentException>();
        await Assert.That(() => OpenUsdNativeRuntime.OpenStageForReview("\ud800"))
            .Throws<System.Text.EncoderFallbackException>();
        await Assert.That(() => OpenUsdNativeRuntime.ReadReviewDocument([], "source.usda"))
            .Throws<ArgumentException>();
        await Assert.That(() => OpenUsdNativeRuntime.ReadReviewDocument(
            new byte[OpenUsdNativeRuntime.ReviewMaximumDocumentBytes + 1], "source.usda"))
            .Throws<ArgumentException>();
        await Assert.That(() => OpenUsdNativeRuntime.CaptureReviewDocument(layer, [], "review.urd"))
            .Throws<ArgumentException>();
        await Assert.That(() => OpenUsdNativeRuntime.CaptureReviewDocument(layer, [1], "relative.review"))
            .Throws<ArgumentException>();
        await Assert.That(() => OpenUsdNativeRuntime.ImportReviewDocument(stage, [], [1]))
            .Throws<ArgumentException>();
        await Assert.That(() => OpenUsdNativeRuntime.ImportReviewDocument(stage, [1], []))
            .Throws<ArgumentException>();
        await Assert.That(() => OpenUsdNativeRuntime.AcknowledgeReviewSaved(layer, []))
            .Throws<ArgumentException>();
    }

    private static unsafe byte[] CopyEditing(byte[] bytes)
    {
        fixed (byte* pointer = bytes)
        {
            return OpenUsdNativeRuntime.CopyLayerEditingBuffer(8,
                new OpenUsdNativeRuntime.NativeEditBufferView { Data = pointer, Size = (nuint)bytes.Length }, 0)!;
        }
    }

    private static unsafe byte[] Copy(byte[] bytes, string corruption = "")
    {
        fixed (byte* pointer = bytes)
        {
            nint owner = 8;
            var view = new OpenUsdNativeRuntime.NativeEditBufferView { Data = pointer, Size = (nuint)bytes.Length };
            switch (corruption)
            {
                case "missing-owner":
                    owner = 0;
                    break;
                case "invalid-owner":
                    owner = -1;
                    break;
                case "misaligned-owner":
                    owner = 1;
                    break;
                case "missing-data":
                    view.Data = null;
                    break;
                case "empty-size":
                    view.Size = 0;
                    break;
                case "over-budget":
                    view.Size = OpenUsdNativeRuntime.ReviewMaximumEnvelopeBytes + 1u;
                    break;
                case "size-overflow":
                    view.Size = nuint.MaxValue;
                    break;
                case "pointer-overflow":
                    view.Data = (byte*)(nuint.MaxValue - 1);
                    break;
            }
            return OpenUsdNativeRuntime.CopyReviewBuffer(owner, view);
        }
    }
}
