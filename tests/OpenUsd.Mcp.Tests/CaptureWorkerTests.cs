// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Concurrent;

namespace OpenUsd.Mcp.Tests;

public sealed class CaptureWorkerTests
{
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
