// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Mcp.Tests;

public sealed class PreviewCaptureProcessorTests
{
    [Test]
    [Arguments(2, true)]
    [Arguments(3, true)]
    [Arguments(4, false)]
    [Arguments(5, true)]
    [Arguments(5, false)]
    [Arguments(16, true)]
    [Arguments(16, false)]
    public async Task UndersizedContactSheetIsRefusedBeforeCreatingItsFrameSource(int count, bool narrow)
    {
        int columns = (int)Math.Ceiling(Math.Sqrt(count));
        int rows = (count + columns - 1) / columns;
        var source = new RecordingFrameSource();
        var factory = new RecordingFrameSourceFactory(source);
        using var artifacts = new ArtifactResourceStore();
        using var processor = new PreviewCaptureProcessor(factory, artifacts);
        CaptureView[] views = Enumerable.Range(0, count)
            .Select(index => new CaptureView($"view-{index}", default)).ToArray();
        var request = new PreviewCaptureRequest("small-sheet", CaptureKind.ContactSheet,
            narrow ? columns - 1 : columns, narrow ? rows : rows - 1, views);
        await Assert.That(() => processor.Process(request)).Throws<ArgumentException>();
        await Assert.That(factory.CreateCount).IsEqualTo(0);
        await Assert.That(source.CaptureCount).IsEqualTo(0);
        await Assert.That(artifacts.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(1, 0)]
    [Arguments(2, 0)]
    [Arguments(3, 0)]
    [Arguments(4, 0)]
    [Arguments(5, 0)]
    [Arguments(9, 0)]
    [Arguments(16, 0)]
    [Arguments(5, 1)]
    public async Task ContactSheetAtMinimumGridExtentRetainsEveryTileAndUnusedPixel(int count, int padding)
    {
        int columns = (int)Math.Ceiling(Math.Sqrt(count));
        int rows = (count + columns - 1) / columns;
        int width = columns + padding;
        int height = rows + padding;
        var source = new RecordingFrameSource();
        var factory = new RecordingFrameSourceFactory(source);
        using var artifacts = new ArtifactResourceStore();
        using var processor = new PreviewCaptureProcessor(factory, artifacts);
        PreviewCaptureResult result = processor.Process(new PreviewCaptureRequest(
            "minimum-sheet", CaptureKind.ContactSheet, width, height,
            Enumerable.Range(0, count).Select(index => new CaptureView($"view-{index}", default)).ToArray()));
        ArtifactResourceContent? png = await artifacts.ReadAsync(result.Artifacts.Single().ResourceUri);
        ImageRgba8 image = PngRgba8Decoder.Decode(png!.Content.Span);
        byte[] expected = new byte[width * height * 4];
        for (int index = 0; index < count; index++)
        {
            int offset = (index / columns * width + index % columns) * 4;
            expected[offset] = (byte)(index + 1);
            expected[offset + 1] = (byte)(index + 2);
            expected[offset + 2] = (byte)(index + 3);
            expected[offset + 3] = 255;
        }
        await Assert.That(image.Pixels.ToArray().SequenceEqual(expected)).IsTrue();
        await Assert.That(image.Width).IsEqualTo(width);
        await Assert.That(image.Height).IsEqualTo(height);
        await Assert.That(factory.CreateCount).IsEqualTo(1);
        await Assert.That(source.CaptureCount).IsEqualTo(count);
        await Assert.That(artifacts.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(CaptureKind.Still)]
    [Arguments(CaptureKind.ContactSheet)]
    [Arguments(CaptureKind.CandidateSweep)]
    [Arguments(CaptureKind.Turntable)]
    public async Task MaximumLengthPreviewArtifactRemainsReadableWithoutChangingGeneratedNames(CaptureKind kind)
    {
        string suffix = kind switch
        {
            CaptureKind.Still => ".png",
            CaptureKind.ContactSheet => "-contact-sheet.png",
            _ => "-00-front_left.png"
        };
        string requestId = new('a', 1024 - suffix.Length);
        using var artifacts = new ArtifactResourceStore();
        using var processor = new PreviewCaptureProcessor(
            new RecordingFrameSourceFactory(new RecordingFrameSource()), artifacts);
        var request = new PreviewCaptureRequest(requestId, kind, 1, 1,
            [new CaptureView("front left", default)]);
        ArtifactResourceDescriptor descriptor = processor.Process(request).Artifacts.Single();
        await Assert.That(descriptor.Id).IsEqualTo(requestId + suffix);
        var resources = new OpenUsdMcpResources(artifacts);
        ModelContextProtocol.Protocol.ResourceContents content =
            await resources.ReadArtifactAsync(descriptor.Id, default);
        await Assert.That(content.Uri).IsEqualTo(descriptor.ResourceUri.AbsoluteUri);
        var blob = (ModelContextProtocol.Protocol.BlobResourceContents)content;
        await Assert.That(Convert.ToHexString(PngRgba8Decoder.Decode(blob.DecodedData.Span).Pixels.Span))
            .IsEqualTo("010203FF");
    }

    [Test]
    [Arguments(CaptureKind.Still, "separator")]
    [Arguments(CaptureKind.Still, "long")]
    [Arguments(CaptureKind.ContactSheet, "separator")]
    [Arguments(CaptureKind.ContactSheet, "long")]
    [Arguments(CaptureKind.CandidateSweep, "separator")]
    [Arguments(CaptureKind.CandidateSweep, "long")]
    [Arguments(CaptureKind.CandidateSweep, "name")]
    [Arguments(CaptureKind.CandidateSweep, "later")]
    [Arguments(CaptureKind.Turntable, "separator")]
    [Arguments(CaptureKind.Turntable, "long")]
    [Arguments(CaptureKind.Turntable, "name")]
    [Arguments(CaptureKind.Turntable, "later")]
    public async Task UnreadableArtifactIdentitiesAreRejectedBeforeCreatingOrCapturingFrames(
        CaptureKind kind, string invalid)
    {
        var source = new RecordingFrameSource();
        var factory = new RecordingFrameSourceFactory(source);
        using var artifacts = new ArtifactResourceStore();
        using var processor = new PreviewCaptureProcessor(factory, artifacts);
        string id = invalid switch
        {
            "separator" => "unreadable/name",
            "long" => new string('a', 1024),
            _ => "valid"
        };
        string name = invalid == "name" ? new string('a', 1024) : "front";
        CaptureView[] views = invalid == "later"
            ? [new CaptureView("valid-first", default), new CaptureView(new string('a', 1024), default)]
            : [new CaptureView(name, default)];
        var request = new PreviewCaptureRequest(id, kind, 1, 1, views);
        await Assert.That(() => processor.Process(request)).Throws<ArgumentException>();
        await Assert.That(factory.CreateCount).IsEqualTo(0);
        await Assert.That(source.CaptureCount).IsEqualTo(0);
        await Assert.That(artifacts.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ExhaustedCumulativePreviewBudgetDoesNotPublishFirstFrame()
    {
        var image = new ImageRgba8(1, 1, [17, 34, 51, 255]);
        int firstBytes = PngRgba8Encoder.Encode(image).Length;
        using var artifacts = new ArtifactResourceStore();
        using var source = new FixedFrameSource(image);
        using var processor = new PreviewCaptureProcessor(source, artifacts,
            new PreviewCaptureLimits(1, 1, 2, firstBytes));
        var request = new PreviewCaptureRequest("exhausted", CaptureKind.Turntable, 1, 1,
            [new CaptureView("first", default), new CaptureView("second", default)]);
        await Assert.That(() => processor.Process(request)).Throws<RenderOutputQuotaExceededException>();
        await Assert.That(artifacts.Count).IsEqualTo(0);
        await Assert.That(artifacts.TotalBytes).IsEqualTo(0L);
        await Assert.That(source.CaptureCount).IsEqualTo(2);
    }

    [Test]
    [Arguments(CaptureKind.Still, -1)]
    [Arguments(CaptureKind.Still, 0)]
    [Arguments(CaptureKind.Still, 1)]
    [Arguments(CaptureKind.ContactSheet, -1)]
    [Arguments(CaptureKind.ContactSheet, 0)]
    [Arguments(CaptureKind.ContactSheet, 1)]
    [Arguments(CaptureKind.CandidateSweep, -1)]
    [Arguments(CaptureKind.CandidateSweep, 0)]
    [Arguments(CaptureKind.CandidateSweep, 1)]
    [Arguments(CaptureKind.Turntable, -1)]
    [Arguments(CaptureKind.Turntable, 0)]
    [Arguments(CaptureKind.Turntable, 1)]
    public async Task PreviewEncodingHonorsExactRemainingBudgetAcrossAllModes(CaptureKind kind, int margin)
    {
        var image = new ImageRgba8(1, 1, [17, 34, 51, 255]);
        byte[] png = PngRgba8Encoder.Encode(image);
        bool separate = kind is CaptureKind.CandidateSweep or CaptureKind.Turntable;
        CaptureView[] views = separate
            ? [new CaptureView("first", default), new CaptureView("second", default)]
            : [new CaptureView("first", default)];
        long exact = png.Length * (separate ? 2L : 1L);
        using var artifacts = new ArtifactResourceStore();
        using var source = new FixedFrameSource(image);
        using var processor = new PreviewCaptureProcessor(source, artifacts,
            new PreviewCaptureLimits(1, 1, 2, exact + margin));
        var request = new PreviewCaptureRequest("boundary", kind, 1, 1, views);
        if (margin < 0)
        {
            await Assert.That(() => processor.Process(request)).Throws<RenderOutputQuotaExceededException>();
            await Assert.That(artifacts.Count).IsEqualTo(0);
            await Assert.That(artifacts.TotalBytes).IsEqualTo(0L);
        }
        else
        {
            PreviewCaptureResult result = processor.Process(request);
            await Assert.That(result.Artifacts.Count).IsEqualTo(separate ? 2 : 1);
            await Assert.That(artifacts.TotalBytes).IsEqualTo(exact);
            foreach (ArtifactResourceDescriptor descriptor in result.Artifacts)
            {
                ArtifactResourceContent content = (await artifacts.ReadAsync(descriptor.ResourceUri))!;
                await Assert.That(Convert.ToHexString(content.Content.Span)).IsEqualTo(Convert.ToHexString(png));
                await Assert.That(Convert.ToHexString(PngRgba8Decoder.Decode(content.Content.Span).Pixels.Span))
                    .IsEqualTo("112233FF");
            }
        }
        await Assert.That(source.CaptureCount).IsEqualTo(views.Length);
    }

    [Test]
    [Arguments(CaptureKind.Still)]
    [Arguments(CaptureKind.ContactSheet)]
    public async Task CancellationAfterCaptureSkipsEncodingAndRetainsSourceForExactRetry(CaptureKind kind)
    {
        byte[] pixels = new byte[1024 * 1024 * 4];
        new Random(419).NextBytes(pixels);
        var image = new ImageRgba8(1024, 1024, pixels);
        using var cancellation = new CancellationTokenSource();
        using var artifacts = new ArtifactResourceStore();
        using var source = new FixedFrameSource(image, () => cancellation.Cancel());
        using var processor = new PreviewCaptureProcessor(source, artifacts,
            new PreviewCaptureLimits(1024, 1024, 1, 16 * 1024 * 1024));
        var request = new PreviewCaptureRequest("cancel-encoding", kind, 1024, 1024,
            [new CaptureView("original", default)]);
        OperationCanceledException? observed = null;
        long before = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            processor.Process(request, cancellation.Token);
        }
        catch (OperationCanceledException exception)
        {
            observed = exception;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(observed).IsNotNull();
        await Assert.That(observed!.CancellationToken).IsEqualTo(cancellation.Token);
        long sheetBytes = kind == CaptureKind.ContactSheet ? pixels.Length : 0;
        await Assert.That(allocated - sheetBytes).IsLessThan(1024 * 1024L);
        await Assert.That(artifacts.Count).IsEqualTo(0);
        await Assert.That(source.CaptureCount).IsEqualTo(1);
        PreviewCaptureResult retry = processor.Process(request);
        ArtifactResourceContent content = (await artifacts.ReadAsync(retry.Artifacts.Single().ResourceUri))!;
        await Assert.That(Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(PngRgba8Decoder.Decode(content.Content.Span).Pixels.Span)))
            .IsEqualTo(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels)));
        await Assert.That(artifacts.Count).IsEqualTo(1);
        await Assert.That(source.CaptureCount).IsEqualTo(2);
    }

    [Test]
    [Arguments(CaptureKind.Still)]
    [Arguments(CaptureKind.ContactSheet)]
    public async Task PreviewByteQuotaRejectsBeforeAllocatingTheCompleteEncodedImage(CaptureKind kind)
    {
        byte[] pixels = new byte[1024 * 1024 * 4];
        new Random(173).NextBytes(pixels);
        var image = new ImageRgba8(1024, 1024, pixels);
        using var artifacts = new ArtifactResourceStore();
        using var source = new FixedFrameSource(image);
        using var processor = new PreviewCaptureProcessor(source, artifacts,
            new PreviewCaptureLimits(1024, 1024, 1, 1024));
        var request = new PreviewCaptureRequest("bounded", kind, 1024, 1024,
            [new CaptureView("original", default)]);
        InvalidOperationException? observed = null;
        long before = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            processor.Process(request);
        }
        catch (InvalidOperationException exception)
        {
            observed = exception;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(observed).IsNotNull();
        long sheetBytes = kind == CaptureKind.ContactSheet ? pixels.Length : 0;
        await Assert.That(allocated - sheetBytes).IsLessThan(1024 * 1024L);
        await Assert.That(artifacts.Count).IsEqualTo(0);
        await Assert.That(artifacts.TotalBytes).IsEqualTo(0L);
        await Assert.That(source.CaptureCount).IsEqualTo(1);
    }

    private sealed class FixedFrameSource(ImageRgba8 image, Action? captured = null)
        : IPreviewFrameSourceFactory, IPreviewFrameSource
    {
        internal int CaptureCount { get; private set; }

        public IPreviewFrameSource Create(PreviewCaptureRequest request, CancellationToken cancellationToken) => this;

        public ImageRgba8 Capture(CaptureView view, int width, int height)
        {
            CaptureCount++;
            captured?.Invoke();
            return image;
        }

        public void Dispose()
        {
        }
    }

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
    public async Task CapturePublishesBoundedRendererDiagnostics()
    {
        var diagnostic = new RenderDiagnostic(
            RenderDiagnosticSeverity.Warning,
            "OPENUSD_SILK_TEXTURE_ASSET_NOT_FOUND",
            "A texture asset was not found.");
        var source = new RecordingFrameSource(
            diagnostics: new RenderDiagnosticsState([diagnostic]));
        using var processor = new PreviewCaptureProcessor(
            new RecordingFrameSourceFactory(source),
            new ArtifactResourceStore());

        PreviewCaptureResult result = processor.Process(
            new PreviewCaptureRequest("diagnostics", 1, 1));

        await Assert.That(result.Diagnostics).IsEquivalentTo([diagnostic]);
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
        int disposeFailuresRemaining = 0,
        RenderDiagnosticsState? diagnostics = null)
        : IPreviewFrameSource, IPreviewDiagnosticSource
    {
        internal int CaptureCount { get; private set; }

        internal int DisposeAttemptCount { get; private set; }

        internal bool Disposed { get; private set; }

        public RenderDiagnosticsState Diagnostics { get; } =
            diagnostics ?? RenderDiagnosticsState.Empty;

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
