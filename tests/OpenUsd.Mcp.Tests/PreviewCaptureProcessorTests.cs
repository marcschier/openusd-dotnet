// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp.Tests;

public sealed class PreviewCaptureProcessorTests
{
    [Test]
    public async Task StillCaptureEncodesPngAndPublishesArtifact()
    {
        var source = new RecordingFrameSource();
        var sourceFactory = new RecordingFrameSourceFactory(source);
        var artifacts = new ArtifactResourceStore();
        using var processor = new PreviewCaptureProcessor(sourceFactory, artifacts);
        var request = new PreviewCaptureRequest("still-1", 1, 1);

        PreviewCaptureResult result = processor.Process(request);
        ArtifactResourceDescriptor descriptor = result.Artifacts[0];
        ArtifactResourceContent? content = await artifacts.ReadAsync(
            descriptor.ResourceUri);
        ImageRgba8 decoded = PngRgba8Decoder.Decode(content!.Content.Span);

        await Assert.That(content).IsNotNull();
        await Assert.That(result.Artifacts.Count).IsEqualTo(1);
        await Assert.That(descriptor.MediaType).IsEqualTo("image/png");
        await Assert.That(decoded.Pixels.ToArray())
            .IsEquivalentTo(new byte[] { 1, 2, 3, 255 });
        await Assert.That(source.Disposed).IsFalse();
    }

    [Test]
    public async Task RetainsOneLazilyCreatedFrameSourceAcrossRequests()
    {
        var source = new RecordingFrameSource();
        var factory = new RecordingFrameSourceFactory(source);
        var processor = new PreviewCaptureProcessor(factory, new ArtifactResourceStore());

        _ = processor.Process(new PreviewCaptureRequest("one", 1, 1));
        _ = processor.Process(new PreviewCaptureRequest("two", 1, 1));

        await Assert.That(factory.CreateCount).IsEqualTo(1);
        await Assert.That(source.CaptureCount).IsEqualTo(2);
        await Assert.That(source.Disposed).IsFalse();
        processor.Dispose();
        await Assert.That(source.Disposed).IsTrue();
    }

    [Test]
    public async Task ResetDisposesRetainedSourceBeforeCreatingAReplacement()
    {
        var first = new RecordingFrameSource();
        var second = new RecordingFrameSource();
        var factory = new SequenceFrameSourceFactory(first, second);
        using var processor = new PreviewCaptureProcessor(
            factory,
            new ArtifactResourceStore());

        _ = processor.Process(new PreviewCaptureRequest("one", 1, 1));
        processor.Reset();
        _ = processor.Process(new PreviewCaptureRequest("two", 1, 1));

        await Assert.That(first.Disposed).IsTrue();
        await Assert.That(first.CaptureCount).IsEqualTo(1);
        await Assert.That(second.Disposed).IsFalse();
        await Assert.That(second.CaptureCount).IsEqualTo(1);
        await Assert.That(factory.CreateCount).IsEqualTo(2);
    }

    [Test]
    public async Task FailedResetRetainsSourceUntilRetrySucceeds()
    {
        var first = new RecordingFrameSource(disposeFailuresRemaining: 1);
        var second = new RecordingFrameSource();
        var factory = new SequenceFrameSourceFactory(first, second);
        using var processor = new PreviewCaptureProcessor(
            factory,
            new ArtifactResourceStore());

        _ = processor.Process(new PreviewCaptureRequest("one", 1, 1));

        await Assert.That(processor.Reset).ThrowsExactly<IOException>();
        await Assert.That(
                () => processor.Process(new PreviewCaptureRequest("blocked", 1, 1)))
            .Throws<InvalidOperationException>();
        await Assert.That(factory.CreateCount).IsEqualTo(1);
        await Assert.That(first.DisposeAttemptCount).IsEqualTo(1);

        processor.Reset();
        _ = processor.Process(new PreviewCaptureRequest("two", 1, 1));

        await Assert.That(first.DisposeAttemptCount).IsEqualTo(2);
        await Assert.That(first.Disposed).IsTrue();
        await Assert.That(second.CaptureCount).IsEqualTo(1);
        await Assert.That(factory.CreateCount).IsEqualTo(2);
    }

    [Test]
    public async Task FailedDisposeRetainsSourceUntilDisposeRetrySucceeds()
    {
        var source = new RecordingFrameSource(disposeFailuresRemaining: 1);
        var processor = new PreviewCaptureProcessor(
            new RecordingFrameSourceFactory(source),
            new ArtifactResourceStore());
        _ = processor.Process(new PreviewCaptureRequest("one", 1, 1));

        await Assert.That(processor.Dispose).ThrowsExactly<IOException>();
        await Assert.That(
                () => processor.Process(new PreviewCaptureRequest("blocked", 1, 1)))
            .Throws<ObjectDisposedException>();
        await Assert.That(processor.Reset).Throws<ObjectDisposedException>();
        await Assert.That(processor.IsDisposePending).IsTrue();
        await Assert.That(source.DisposeAttemptCount).IsEqualTo(1);

        processor.Dispose();
        processor.Dispose();

        await Assert.That(source.DisposeAttemptCount).IsEqualTo(2);
        await Assert.That(source.Disposed).IsTrue();
        await Assert.That(processor.IsDisposePending).IsFalse();
    }

    [Test]
    [Arguments(CaptureKind.CandidateSweep)]
    [Arguments(CaptureKind.Turntable)]
    public async Task MultiViewModesPublishOneArtifactPerSuppliedView(CaptureKind kind)
    {
        var artifacts = new ArtifactResourceStore();
        using var processor = new PreviewCaptureProcessor(
            new RecordingFrameSourceFactory(new RecordingFrameSource()),
            artifacts);
        var request = new PreviewCaptureRequest(
            "views",
            kind,
            1,
            1,
            [
                new CaptureView("front left", default),
                new CaptureView("back", default, 1),
            ]);

        PreviewCaptureResult result = processor.Process(request);

        await Assert.That(result.Artifacts.Count).IsEqualTo(2);
        await Assert.That(result.Artifacts[0].Id).IsEqualTo("views-00-front_left.png");
        await Assert.That(result.Artifacts[1].Id).IsEqualTo("views-01-back.png");
    }

    [Test]
    public async Task ContactSheetUsesSuppliedViewsInDeterministicGrid()
    {
        var source = new RecordingFrameSource();
        var artifacts = new ArtifactResourceStore();
        using var processor = new PreviewCaptureProcessor(
            new RecordingFrameSourceFactory(source),
            artifacts);
        var request = new PreviewCaptureRequest(
            "sheet",
            CaptureKind.ContactSheet,
            2,
            2,
            [
                new CaptureView("red", default),
                new CaptureView("green", default),
                new CaptureView("blue", default),
            ]);

        PreviewCaptureResult result = processor.Process(request);
        ArtifactResourceContent? png = await artifacts.ReadAsync(
            result.Artifacts[0].ResourceUri);
        ImageRgba8 image = PngRgba8Decoder.Decode(png!.Content.Span);

        await Assert.That(result.Artifacts.Count).IsEqualTo(1);
        await Assert.That(result.Artifacts[0].Id)
            .IsEqualTo("sheet-contact-sheet.png");
        await Assert.That(image.Pixels.ToArray()).IsEquivalentTo(
        new byte[]
        {
            1, 2, 3, 255,
            2, 3, 4, 255,
            3, 4, 5, 255,
            0, 0, 0, 0,
        });
    }

    [Test]
    public async Task RejectsDimensionViewAndEncodedByteQuotaViolations()
    {
        var limits = new PreviewCaptureLimits(
            MaximumWidth: 2,
            MaximumHeight: 2,
            MaximumViews: 2,
            MaximumTotalArtifactBytes: 1);
        var artifacts = new ArtifactResourceStore();
        using var processor = new PreviewCaptureProcessor(
            new RecordingFrameSourceFactory(new RecordingFrameSource()),
            artifacts,
            limits);

        await Assert.That(
                () => processor.Process(new PreviewCaptureRequest("wide", 3, 1)))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(
                () => processor.Process(
                    new PreviewCaptureRequest(
                        "views",
                        CaptureKind.Turntable,
                        1,
                        1,
                        [
                            new CaptureView("one", default),
                            new CaptureView("two", default),
                            new CaptureView("three", default),
                        ])))
            .Throws<ArgumentException>();
        await Assert.That(
                () => processor.Process(new PreviewCaptureRequest("bytes", 1, 1)))
            .Throws<InvalidOperationException>();
        await Assert.That(artifacts.Count).IsEqualTo(0);
    }

    [Test]
    public async Task CancellationBetweenViewsDoesNotPublishPartialArtifacts()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new RecordingFrameSource(
            captureCallback: count =>
            {
                if (count == 1)
                {
                    cancellation.Cancel();
                }
            });
        var artifacts = new ArtifactResourceStore();
        using var processor = new PreviewCaptureProcessor(
            new RecordingFrameSourceFactory(source),
            artifacts);
        var request = new PreviewCaptureRequest(
            "cancel",
            CaptureKind.CandidateSweep,
            1,
            1,
            [
                new CaptureView("one", default),
                new CaptureView("two", default),
            ]);

        await Assert.That(() => processor.Process(request, cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(source.CaptureCount).IsEqualTo(1);
        await Assert.That(artifacts.Count).IsEqualTo(0);
    }

    [Test]
    public async Task StoreQuotaFailureDoesNotPublishPartialMultiViewArtifacts()
    {
        var artifacts = new ArtifactResourceStore(
            new ArtifactResourceStoreOptions(
                MaximumResourceCount: 1,
                MaximumTotalBytes: 1024));
        using var processor = new PreviewCaptureProcessor(
            new RecordingFrameSourceFactory(new RecordingFrameSource()),
            artifacts);
        var request = new PreviewCaptureRequest(
            "atomic",
            CaptureKind.Turntable,
            1,
            1,
            [
                new CaptureView("one", default),
                new CaptureView("two", default),
            ]);

        await Assert.That(() => processor.Process(request))
            .Throws<ArtifactResourceStoreCapacityException>();
        await Assert.That(artifacts.Count).IsEqualTo(0);
        await Assert.That(artifacts.TotalBytes).IsEqualTo(0);
    }

    private sealed class RecordingFrameSourceFactory(RecordingFrameSource source)
        : IPreviewFrameSourceFactory
    {
        internal int CreateCount { get; private set; }

        public IPreviewFrameSource Create(
            PreviewCaptureRequest request,
            CancellationToken cancellationToken = default)
        {
            CreateCount++;
            return source;
        }
    }

    private sealed class SequenceFrameSourceFactory(
        params RecordingFrameSource[] sources) : IPreviewFrameSourceFactory
    {
        internal int CreateCount { get; private set; }

        public IPreviewFrameSource Create(
            PreviewCaptureRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return sources[CreateCount++];
        }
    }

    private sealed class RecordingFrameSource(
        Action<int>? captureCallback = null,
        int disposeFailuresRemaining = 0)
        : IPreviewFrameSource
    {
        internal int CaptureCount { get; private set; }

        internal int DisposeAttemptCount { get; private set; }

        internal bool Disposed { get; private set; }

        public ImageRgba8 Capture(CaptureView view, int width, int height)
        {
            CaptureCount++;
            captureCallback?.Invoke(CaptureCount);
            byte first = checked((byte)CaptureCount);
            byte[] pixels = new byte[ImageRgba8.GetByteCount(width, height)];
            for (int offset = 0; offset < pixels.Length; offset += 4)
            {
                pixels[offset] = first;
                pixels[offset + 1] = checked((byte)(first + 1));
                pixels[offset + 2] = checked((byte)(first + 2));
                pixels[offset + 3] = 255;
            }

            return new ImageRgba8(width, height, pixels);
        }

        public void Dispose()
        {
            DisposeAttemptCount++;
            if (disposeFailuresRemaining > 0)
            {
                disposeFailuresRemaining--;
                throw new IOException("dispose failed");
            }

            Disposed = true;
        }
    }
}
