// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using OpenUsd.Rendering;

namespace OpenUsd.Mcp.Tests;

public sealed class CaptureWorkerTests
{
    [Test]
    public async Task ShutdownCancelsQueuedDiskWithAnActuallyCancelledTokenAndStillDrainsActiveWork()
    {
        using var files = new WorkspaceTestFiles();
        var source = new DrainFrameSource();
        using var artifacts = new ArtifactResourceStore();
        using var processor = new PreviewCaptureProcessor(new ThrowingFrameSourceFactory(source), artifacts);
        await using var worker = new CaptureWorker(processor);
        string output = Path.Combine(files.OutputRoot, "shutdown-disk");
        var request = new RenderDiskJobRequest(output,
        [
            StageRenderState.Create(new StageIdentity("shutdown-disk"))
                .WithViewport(new ViewportDimensions(1, 1))
                .WithRenderSettings(RenderSettings.PresentationDefault)
        ]);
        Task<PreviewCaptureResult> active = worker.CaptureAsync(Request("active-at-shutdown")).AsTask();
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task<RenderDiskJobResult> queued = worker.CaptureDiskAsync(request).AsTask();
            Task shutdown = worker.DisposeAsync().AsTask();
            Exception failure = await CaptureExceptionAsync(() => queued.WaitAsync(TimeSpan.FromSeconds(1)));
            await Assert.That(failure).IsTypeOf<TaskCanceledException>();
            var cancellation = (OperationCanceledException)failure;
            await Assert.That(cancellation.CancellationToken.CanBeCanceled).IsTrue();
            await Assert.That(cancellation.CancellationToken.IsCancellationRequested).IsTrue();
            await Assert.That(shutdown.IsCompleted).IsFalse();
            await Assert.That(active.IsCompleted).IsFalse();
            await Assert.That(source.Calls).IsEqualTo(1);
            await Assert.That(Directory.Exists(output)).IsFalse();
            source.Release.Set();
            await active.WaitAsync(TimeSpan.FromSeconds(5));
            await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(source.Exited).IsTrue();
            await Assert.That(source.Calls).IsEqualTo(1);
            await Assert.That(worker.ShutdownResult!.Succeeded).IsTrue();
            await Assert.That(worker.IsWorkerThreadAlive).IsFalse();
        }
        finally
        {
            source.Release.Set();
            await worker.DisposeAsync();
            source.Release.Dispose();
        }
    }

    [Test]
    public async Task QueuedDiskCancellationCompletesWithoutWaitingForUnrelatedActiveCaptureAndPreservesRetry()
    {
        using var files = new WorkspaceTestFiles();
        var source = new DrainFrameSource();
        using var artifacts = new ArtifactResourceStore();
        using var processor = new PreviewCaptureProcessor(new ThrowingFrameSourceFactory(source), artifacts);
        await using var worker = new CaptureWorker(processor, capacity: 1);
        using var cancellation = new CancellationTokenSource();
        string output = Path.Combine(files.OutputRoot, "cancelled-disk");
        var request = new RenderDiskJobRequest(output,
        [
            StageRenderState.Create(new StageIdentity("queued-disk"))
                .WithViewport(new ViewportDimensions(1, 1))
                .WithRenderSettings(RenderSettings.PresentationDefault)
        ]);
        Task<PreviewCaptureResult> active = worker.CaptureAsync(Request("active-preview")).AsTask();
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task<RenderDiskJobResult> queued = worker.CaptureDiskAsync(request, cancellation.Token).AsTask();
            await Assert.That(worker.PendingCount).IsEqualTo(1);
            cancellation.Cancel();
            Task barrier = Task.Delay(1000);
            await Assert.That(ReferenceEquals(await Task.WhenAny(queued, barrier), queued)).IsTrue();
            Exception queuedFailure = await CaptureExceptionAsync(() => queued);
            await Assert.That(queuedFailure).IsTypeOf<TaskCanceledException>();
            await Assert.That(((OperationCanceledException)queuedFailure).CancellationToken == cancellation.Token)
                .IsTrue();
            await Assert.That(active.IsCompleted).IsFalse();
            await Assert.That(source.Exited).IsFalse();
            await Assert.That(source.Calls).IsEqualTo(1);
            await Assert.That(Directory.Exists(output)).IsFalse();
            await Assert.That(artifacts.Count).IsEqualTo(0);
            await Assert.That(async () => await worker.CaptureDiskAsync(request))
                .Throws<CaptureQueueFullException>();
            source.Release.Set();
            await active.WaitAsync(TimeSpan.FromSeconds(5));
            await worker.ResetAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            RenderDiskJobResult retry = await worker.CaptureDiskAsync(request).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(source.Calls).IsEqualTo(2);
            await Assert.That(retry.Frames.Count).IsEqualTo(1);
            ImageRgba8 image = PngRgba8Decoder.Decode(
                await File.ReadAllBytesAsync(Path.Combine(output, retry.Frames[0].FileName)));
            await Assert.That(Convert.ToHexString(image.Pixels.Span)).IsEqualTo("010203FF");
            await Assert.That(artifacts.Count).IsEqualTo(1);
        }
        finally
        {
            source.Release.Set();
            await worker.DisposeAsync();
            source.Release.Dispose();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CanceledCaptureKeepsSceneEditsBlockedUntilActualCaptureDrain(bool disk)
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        var source = new DrainFrameSource();
        using var artifacts = new ArtifactResourceStore();
        using var processor = new PreviewCaptureProcessor(new ThrowingFrameSourceFactory(source), artifacts);
        await using var worker = new CaptureWorker(processor);
        using ServiceProvider provider = new ServiceCollection().AddSingleton(worker).BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, new OpenUsdMcpApplicationOptions(
            files.SourceRoot, files.OutputRoot, files.SourceRoot, files.OutputRoot,
            Path.Combine(files.OutputRoot, "viewer")));
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        using var cancellation = new CancellationTokenSource();
        Task capture = disk
            ? service.RenderSequenceAsync(new RenderSequenceRequest
            {
                SessionId = session.SessionId,
                Generation = session.Generation,
                StageRevision = session.StageRevision,
                Width = 1,
                Height = 1,
                FrameCount = 1
            }, cancellation.Token).AsTask()
            : service.RenderPreviewAsync(new RenderPreviewRequest
            {
                SessionId = session.SessionId,
                Generation = session.Generation,
                StageRevision = session.StageRevision,
                Kind = "still",
                Width = 1,
                Height = 1,
                Views = [new CaptureViewDto { Name = "hero" }]
            }, cancellation.Token).AsTask();
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            Task<McpEditResultDto> edit = service.ApplyEditsAsync(new ApplyEditsRequest
            {
                SessionId = session.SessionId,
                Generation = session.Generation,
                StageRevision = session.StageRevision,
                Edits = [new WorkspaceEditDto { Kind = "define_prim", PrimPath = "/World", TypeName = "Xform" }]
            }, default).AsTask();
            Task barrier = Task.Delay(100);
            await Assert.That(await Task.WhenAny(edit, barrier)).IsEqualTo(barrier);
            await Assert.That(backend.Events).DoesNotContain("apply");
            await Assert.That(capture.IsCompleted).IsFalse();
            source.Release.Set();
            await Assert.That(async () => await capture.WaitAsync(TimeSpan.FromSeconds(5)))
                .Throws<OperationCanceledException>();
            McpEditResultDto result = await edit.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(source.Exited).IsTrue();
            await Assert.That(result.StageRevision).IsEqualTo(session.StageRevision + 1);
            await Assert.That(backend.Events.Count(item => item == "apply")).IsEqualTo(1);
            await Assert.That(artifacts.Count).IsEqualTo(0);
            if (disk)
            {
                await Assert.That(Directory.GetFileSystemEntries(
                    Path.Combine(backend.Context!.OutputDirectory, "renders"))).IsEmpty();
                await Assert.That((await service.ListSequenceJobsAsync(default)).Jobs).IsEmpty();
            }
        }
        finally
        {
            source.Release.Set();
            await worker.DisposeAsync();
            source.Release.Dispose();
        }
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task ActiveCancellationWaitsForActualCaptureDrainAndPreservesRetry(
        bool failCapture, bool disk)
    {
        using var files = new WorkspaceTestFiles();
        var source = new DrainFrameSource(failCapture);
        using var artifacts = new ArtifactResourceStore();
        using var processor = new PreviewCaptureProcessor(new ThrowingFrameSourceFactory(source), artifacts);
        await using var worker = new CaptureWorker(processor);
        using var cancellation = new CancellationTokenSource();
        string output = Path.Combine(files.OutputRoot, "active-disk");
        var diskRequest = new RenderDiskJobRequest(output,
        [
            StageRenderState.Create(new StageIdentity("active-disk"))
                .WithViewport(new ViewportDimensions(1, 1))
                .WithRenderSettings(RenderSettings.PresentationDefault)
        ]);
        Task capture = disk
            ? worker.CaptureDiskAsync(diskRequest, cancellation.Token).AsTask()
            : worker.CaptureAsync(Request("cancel-active"), cancellation.Token).AsTask();
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            Task barrier = Task.Delay(100);
            await Assert.That(await Task.WhenAny(capture, barrier)).IsEqualTo(barrier);
            await Assert.That(artifacts.Count).IsEqualTo(0);
            source.Release.Set();
            Exception failure = await CaptureExceptionAsync(() => capture.WaitAsync(TimeSpan.FromSeconds(5)));
            if (failCapture)
            {
                await Assert.That(failure).IsTypeOf<InvalidOperationException>();
                await Assert.That(failure.Message).IsEqualTo("Capture failed after cancellation.");
            }
            else
            {
                await Assert.That(failure).IsTypeOf<TaskCanceledException>();
                await Assert.That(((OperationCanceledException)failure).CancellationToken == cancellation.Token)
                    .IsTrue();
            }
            await Assert.That(source.Exited).IsTrue();
            await Assert.That(artifacts.Count).IsEqualTo(0);
            if (disk)
            {
                await Assert.That(Directory.Exists(output)).IsFalse();
                RenderDiskJobResult retry = await worker.CaptureDiskAsync(diskRequest);
                await Assert.That(retry.Frames.Count).IsEqualTo(1);
                ImageRgba8 image = PngRgba8Decoder.Decode(
                    await File.ReadAllBytesAsync(Path.Combine(output, retry.Frames[0].FileName)));
                await Assert.That(Convert.ToHexString(image.Pixels.Span)).IsEqualTo("010203FF");
                await Assert.That(artifacts.Count).IsEqualTo(0);
            }
            else
            {
                PreviewCaptureResult retry = await worker.CaptureAsync(Request("retry"));
                await Assert.That(retry.Artifacts.Count).IsEqualTo(1);
                ArtifactResourceContent? content = await artifacts.ReadAsync(retry.Artifacts[0].ResourceUri);
                await Assert.That(Convert.ToHexString(PngRgba8Decoder.Decode(content!.Content.Span).Pixels.Span))
                    .IsEqualTo("010203FF");
            }
        }
        finally
        {
            source.Release.Set();
            await worker.DisposeAsync();
            source.Release.Dispose();
        }
    }

    private sealed class DrainFrameSource(bool failFirstCapture = false) : IPreviewFrameSource
    {
        private bool _failFirstCapture = failFirstCapture;

        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Release { get; } = new();
        internal bool Exited { get; private set; }
        internal int Calls { get; private set; }

        public ImageRgba8 Capture(CaptureView view, int width, int height)
        {
            Calls++;
            Entered.TrySetResult();
            if (!Release.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("The test did not release its capture barrier.");
            }
            Exited = true;
            if (_failFirstCapture)
            {
                _failFirstCapture = false;
                throw new InvalidOperationException("Capture failed after cancellation.");
            }
            return new ImageRgba8(width, height, new byte[] { 1, 2, 3, 255 });
        }

        public void Dispose() { }
    }

    [Test]
    public async Task ExecutesCapturesSeriallyOnOneDedicatedThread()
    {
        var processor = new RecordingProcessor();
        using var worker = new CaptureWorker(processor, capacity: 4);

        PreviewCaptureResult[] results = await Task.WhenAll(
            worker.CaptureAsync(Request("one")).AsTask(),
            worker.CaptureAsync(Request("two")).AsTask(),
            worker.CaptureAsync(Request("three")).AsTask());

        await Assert.That(results.Select(static result => result.RequestId))
            .IsEquivalentTo(["one", "two", "three"]);
        await Assert.That(processor.RequestIds).IsEquivalentTo(["one", "two", "three"]);
        await Assert.That(string.Join(",", processor.RequestIds))
            .IsEqualTo("one,two,three");
        await Assert.That(processor.ThreadIds.Distinct().Count()).IsEqualTo(1);
        await Assert.That(processor.ThreadIds.First())
            .IsNotEqualTo(Environment.CurrentManagedThreadId);
        await Assert.That(processor.PeakConcurrency).IsEqualTo(1);
    }

    [Test]
    public async Task RejectsRequestsBeyondBoundedCapacity()
    {
        var processor = new RecordingProcessor(blockFirst: true);
        using var worker = new CaptureWorker(processor, capacity: 1);
        Task<PreviewCaptureResult> first =
            worker.CaptureAsync(Request("one")).AsTask();
        await processor.FirstStarted.Task;
        Task<PreviewCaptureResult> second =
            worker.CaptureAsync(Request("two")).AsTask();
        ValueTask<PreviewCaptureResult> rejected =
            worker.CaptureAsync(Request("three"));

        await Assert.That(async () => await rejected)
            .Throws<CaptureQueueFullException>();
        processor.Release();
        await Task.WhenAll(first, second);
    }

    [Test]
    public async Task DisposalDrainsAcceptedRequestsAndDisposesProcessorOnWorker()
    {
        var processor = new RecordingProcessor(blockFirst: true);
        var worker = new CaptureWorker(processor, capacity: 1);
        Task<PreviewCaptureResult> capture =
            worker.CaptureAsync(Request("one")).AsTask();
        await processor.FirstStarted.Task;

        Task dispose = worker.DisposeAsync().AsTask();
        await Assert.That(async () => await worker.CaptureAsync(Request("late")))
            .Throws<ObjectDisposedException>();
        processor.Release();
        await Task.WhenAll(capture, dispose);

        await Assert.That(processor.Disposed).IsTrue();
        await Assert.That(processor.DisposeThreadId)
            .IsEqualTo(processor.ThreadIds.First());
        await Assert.That(worker.ShutdownResult!.Succeeded).IsTrue();
    }

    [Test]
    public async Task AdmissionTimeoutAndCancellationDoNotRunRejectedWork()
    {
        var processor = new RecordingProcessor(blockFirst: true);
        using var worker = new CaptureWorker(processor, capacity: 2);
        Task<PreviewCaptureResult> first = worker.CaptureAsync(Request("one")).AsTask();
        await processor.FirstStarted.Task;
        Task<PreviewCaptureResult> queued = worker.CaptureAsync(Request("two")).AsTask();
        using var cancellation = new CancellationTokenSource();
        ValueTask<PreviewCaptureResult> canceled = worker.CaptureAsync(
            Request("canceled"),
            cancellation.Token);

        await Assert.That(
                async () => await worker.CaptureAsync(
                    Request("timeout"),
                    TimeSpan.FromMilliseconds(25)))
            .Throws<CaptureAdmissionTimeoutException>();

        cancellation.Cancel();
        await Assert.That(async () => await canceled)
            .Throws<OperationCanceledException>();

        processor.Release();
        await Task.WhenAll(first, queued);
        await Assert.That(processor.RequestIds)
            .IsEquivalentTo(["one", "two"]);
    }

    [Test]
    public async Task ResetRunsAfterAcceptedCapturesOnTheWorkerThread()
    {
        var processor = new RecordingProcessor();
        using var worker = new CaptureWorker(processor, capacity: 1);

        _ = await worker.CaptureAsync(Request("one"));
        await worker.ResetAsync();
        _ = await worker.CaptureAsync(Request("two"));

        await Assert.That(processor.ResetCount).IsEqualTo(1);
        await Assert.That(processor.ResetThreadId)
            .IsEqualTo(processor.ThreadIds.First());
        await Assert.That(string.Join(",", processor.Events))
            .IsEqualTo("capture:one,reset,capture:two");
    }

    [Test]
    public async Task ShutdownRetriesDisposePendingProcessorOnWorkerThread()
    {
        var source = new ThrowingFrameSource(disposeFailuresRemaining: 1);
        var processor = new PreviewCaptureProcessor(
            new ThrowingFrameSourceFactory(source),
            new ArtifactResourceStore());
        var worker = new CaptureWorker(processor);

        _ = await worker.CaptureAsync(Request("one"));
        await worker.DisposeAsync();

        await Assert.That(source.DisposeAttemptCount).IsEqualTo(2);
        await Assert.That(source.DisposeThreadIds.Distinct())
            .IsEquivalentTo([source.CaptureThreadId]);
        await Assert.That(processor.IsDisposePending).IsFalse();
        await Assert.That(worker.ShutdownResult!.Succeeded).IsTrue();
        await Assert.That(worker.ShutdownResult.ProcessorDisposeAttemptCount).IsEqualTo(2);
        await Assert.That(worker.IsWorkerThreadAlive).IsFalse();
    }

    [Test]
    public async Task PermanentShutdownFailureIsRetainedAndObservedByEveryCaller()
    {
        var source = new ThrowingFrameSource(disposeFailuresRemaining: int.MaxValue);
        var processor = new PreviewCaptureProcessor(
            new ThrowingFrameSourceFactory(source),
            new ArtifactResourceStore());
        var worker = new CaptureWorker(processor);
        _ = await worker.CaptureAsync(Request("one"));

        Exception asyncFailure = await CaptureExceptionAsync(
            () => worker.DisposeAsync().AsTask());
        Exception stopFailure = CaptureException(() => _ = worker.Stop());

        await Assert.That(asyncFailure).IsTypeOf<AggregateException>();
        await Assert.That(stopFailure).IsSameReferenceAs(asyncFailure);
        await Assert.That(worker.ShutdownResult!.Failure)
            .IsSameReferenceAs(asyncFailure);
        await Assert.That(worker.ShutdownResult.Succeeded).IsFalse();
        await Assert.That(worker.ShutdownResult.ProcessorDisposeAttemptCount).IsEqualTo(2);
        await Assert.That(source.DisposeAttemptCount).IsEqualTo(2);
        await Assert.That(source.DisposeThreadIds.Distinct())
            .IsEquivalentTo([source.CaptureThreadId]);
        await Assert.That(processor.IsDisposePending).IsTrue();
        await Assert.That(worker.IsWorkerThreadAlive).IsFalse();
    }

    [Test]
    public async Task ShutdownAggregatesEveryFailedResourceAttemptWithoutEscapingThread()
    {
        var order = new ConcurrentQueue<string>();
        var capturer = new RecordingDisposable("capturer", order, int.MaxValue);
        var session = new RecordingDisposable("session", order, int.MaxValue);
        var device = new RecordingDisposable("device", order);
        var sourceRegistration = new RecordingDisposable("source", order);
        int captureThreadId = 0;
        var frameSource = new PreviewSilkFrameSource(
            (_, width, height) =>
            {
                captureThreadId = Environment.CurrentManagedThreadId;
                return new ImageRgba8(
                    width,
                    height,
                    new byte[ImageRgba8.GetByteCount(width, height)]);
            },
            capturer,
            session,
            device,
            sourceRegistration);
        var processor = new PreviewCaptureProcessor(
            new ThrowingFrameSourceFactory(frameSource),
            new ArtifactResourceStore());
        var worker = new CaptureWorker(processor);
        _ = await worker.CaptureAsync(Request("one"));

        Exception failure = await CaptureExceptionAsync(
            () => worker.DisposeAsync().AsTask());
        var aggregate = (AggregateException)failure;

        await Assert.That(aggregate.Flatten().InnerExceptions.Select(
                static exception => exception.Message))
            .IsEquivalentTo(
            [
                "capturer failed",
                "session failed",
                "capturer failed",
                "session failed",
            ]);
        await Assert.That(string.Join(",", order))
            .IsEqualTo("capturer,session,device,source,capturer,session");
        await Assert.That(capturer.ThreadIds.Distinct())
            .IsEquivalentTo([captureThreadId]);
        await Assert.That(capturer.ThreadIds)
            .IsEquivalentTo(session.ThreadIds);
        await Assert.That(device.Disposed).IsTrue();
        await Assert.That(sourceRegistration.Disposed).IsTrue();
        await Assert.That(processor.IsDisposePending).IsTrue();
        await Assert.That(worker.IsWorkerThreadAlive).IsFalse();
    }

    [Test]
    public async Task ConcurrentStopAndDisposeAsyncShareOneProcessorDisposal()
    {
        var processor = new RecordingProcessor();
        var worker = new CaptureWorker(processor);
        _ = await worker.CaptureAsync(Request("one"));

        Task[] shutdowns =
        [
            Task.Run(() => worker.Stop()),
            worker.DisposeAsync().AsTask(),
            Task.Run(worker.Dispose),
            worker.DisposeAsync().AsTask(),
        ];
        await Task.WhenAll(shutdowns);

        await Assert.That(processor.DisposeAttemptCount).IsEqualTo(1);
        await Assert.That(worker.ShutdownResult!.Succeeded).IsTrue();
        await Assert.That(worker.IsWorkerThreadAlive).IsFalse();
    }

    private static PreviewCaptureRequest Request(string requestId) =>
        new(requestId, 1, 1);

    private static Exception CaptureException(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            return exception;
        }

        throw new InvalidOperationException("The operation did not throw.");
    }

    private static async Task<Exception> CaptureExceptionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            return exception;
        }

        throw new InvalidOperationException("The operation did not throw.");
    }

    private sealed class RecordingProcessor(bool blockFirst = false)
        : IPreviewCaptureProcessor, IResettablePreviewCaptureProcessor, IDisposable
    {
        private readonly ManualResetEventSlim _release = new(initialState: !blockFirst);
        private int _active;
        private int _peakConcurrency;

        internal bool Disposed { get; private set; }

        internal int DisposeAttemptCount { get; private set; }

        internal int DisposeThreadId { get; private set; }

        internal TaskCompletionSource FirstStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal ConcurrentQueue<string> RequestIds { get; } = new();

        internal ConcurrentQueue<int> ThreadIds { get; } = new();

        internal int PeakConcurrency => Volatile.Read(ref _peakConcurrency);

        internal ConcurrentQueue<string> Events { get; } = new();

        internal int ResetCount { get; private set; }

        internal int ResetThreadId { get; private set; }

        public PreviewCaptureResult Process(
            PreviewCaptureRequest request,
            CancellationToken cancellationToken = default)
        {
            int active = Interlocked.Increment(ref _active);
            SetMaximum(ref _peakConcurrency, active);
            try
            {
                RequestIds.Enqueue(request.RequestId);
                Events.Enqueue($"capture:{request.RequestId}");
                ThreadIds.Enqueue(Environment.CurrentManagedThreadId);
                FirstStarted.TrySetResult();
                _release.Wait(cancellationToken);
                return new PreviewCaptureResult(
                    request.RequestId,
                    request.Kind,
                    request.Width,
                    request.Height,
                    []);
            }
            finally
            {
                _ = Interlocked.Decrement(ref _active);
            }
        }

        public void Dispose()
        {
            DisposeAttemptCount++;
            Disposed = true;
            DisposeThreadId = Environment.CurrentManagedThreadId;
            _release.Dispose();
        }

        public void Reset()
        {
            ResetCount++;
            ResetThreadId = Environment.CurrentManagedThreadId;
            Events.Enqueue("reset");
        }

        internal void Release() => _release.Set();

        private static void SetMaximum(ref int target, int value)
        {
            int current;
            do
            {
                current = Volatile.Read(ref target);
                if (current >= value)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref target, value, current) != current);
        }
    }

    private sealed class ThrowingFrameSourceFactory(IPreviewFrameSource source)
        : IPreviewFrameSourceFactory
    {
        public IPreviewFrameSource Create(
            PreviewCaptureRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return source;
        }
    }

    private sealed class ThrowingFrameSource(int disposeFailuresRemaining)
        : IPreviewFrameSource
    {
        internal int CaptureThreadId { get; private set; }

        internal int DisposeAttemptCount { get; private set; }

        internal ConcurrentQueue<int> DisposeThreadIds { get; } = new();

        public ImageRgba8 Capture(CaptureView view, int width, int height)
        {
            CaptureThreadId = Environment.CurrentManagedThreadId;
            return new ImageRgba8(
                width,
                height,
                new byte[ImageRgba8.GetByteCount(width, height)]);
        }

        public void Dispose()
        {
            DisposeAttemptCount++;
            DisposeThreadIds.Enqueue(Environment.CurrentManagedThreadId);
            if (disposeFailuresRemaining > 0)
            {
                disposeFailuresRemaining--;
                throw new IOException("frame source failed");
            }
        }
    }

    private sealed class RecordingDisposable(
        string name,
        ConcurrentQueue<string> order,
        int disposeFailuresRemaining = 0) : IDisposable
    {
        internal bool Disposed { get; private set; }

        internal ConcurrentQueue<int> ThreadIds { get; } = new();

        public void Dispose()
        {
            order.Enqueue(name);
            ThreadIds.Enqueue(Environment.CurrentManagedThreadId);
            if (disposeFailuresRemaining > 0)
            {
                disposeFailuresRemaining--;
                throw new IOException($"{name} failed");
            }

            Disposed = true;
        }
    }
}
