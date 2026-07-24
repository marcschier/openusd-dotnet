// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Concurrent;
using Avalonia.Platform;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed class CompositionImportTests
{
    [Test]
    public async Task ImportAndMultiResourceDisposalStayOnDispatcherThread()
    {
        using var dispatcher = new DedicatedThreadDispatcher();
        var imageLeaseReady = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstDisposeReady = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstDisposeStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new FakeImportApi
        {
            ExpectedThreadId = dispatcher.ThreadId,
            FirstDisposeReady = firstDisposeReady.Task,
            FirstDisposeStarted = firstDisposeStarted
        };
        var frame = new ImportFrame(
            [
                new CompositionExternalSemaphore(7, "semaphore"),
                new CompositionExternalSemaphore(8, "semaphore")
            ],
            imageLeaseReady: imageLeaseReady.Task);
        var importer = new AvaloniaCompositionFrameImporter(api);
        AvaloniaImportedCompositionFrame? imported = null;
        Task import = dispatcher.InvokeAsync(async () =>
        {
            imported = await importer.ImportAsync(frame, CancellationToken.None)
                .ConfigureAwait(true);
        }).AsTask();

        imageLeaseReady.SetResult();
        await import;
        Task disposal = dispatcher.InvokeAsync(imported!.DisposeAsync).AsTask();
        await firstDisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        firstDisposeReady.SetResult();
        await disposal;

        await Assert.That(api.WrongThreadOperations).IsEmpty();
        await Assert.That(api.ImageImports).IsEqualTo(1);
        await Assert.That(api.SemaphoreImports).IsEqualTo(2);
        await Assert.That(api.Disposals).IsEqualTo(3);
    }

    [Test]
    public async Task DuplicateTimelineSemaphoreResourceImportsAndDisposesOnce()
    {
        var api = new FakeImportApi();
        var frame = new ImportFrame(
            [
                new CompositionExternalSemaphore(7, "semaphore"),
                new CompositionExternalSemaphore(7, "semaphore")
            ]);
        var importer = new AvaloniaCompositionFrameImporter(api);

        AvaloniaImportedCompositionFrame imported =
            await importer.ImportAsync(frame, CancellationToken.None);
        IImportedGpuSemaphoreResource wait = imported.GetSemaphore(7);
        IImportedGpuSemaphoreResource signal = imported.GetSemaphore(7);
        await imported.DisposeAsync();

        await Assert.That(wait).IsSameReferenceAs(signal);
        await Assert.That(api.SemaphoreImports).IsEqualTo(1);
        await Assert.That(frame.SemaphoreLeaseCount).IsEqualTo(1);
        await Assert.That(api.ImportedSemaphores.Single().DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task MixedImportFailureCommitsOnlySuccessfullyConsumedTransferLease()
    {
        var imageCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var semaphoreCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new FakeImportApi
        {
            ImageImportCompletion = imageCompletion.Task,
            SemaphoreImportCompletion = semaphoreCompletion.Task
        };
        var frame = new ImportFrame(
            [
                new CompositionExternalSemaphore(7, "semaphore"),
                new CompositionExternalSemaphore(7, "semaphore")
            ],
            CompositionExternalHandleOwnership.TransferOnSuccessfulImport);
        var importer = new AvaloniaCompositionFrameImporter(api);
        Task<AvaloniaImportedCompositionFrame> import =
            importer.ImportAsync(frame, CancellationToken.None).AsTask();
        await WaitUntilAsync(() => api.SemaphoreImports == 1);

        imageCompletion.SetResult();
        semaphoreCompletion.SetException(
            new InvalidOperationException("semaphore import failed"));
        Exception? failure = await CaptureFailureAsync(import);

        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        await Assert.That(frame.Leases).Count().IsEqualTo(2);
        await Assert.That(frame.Leases.All(lease => lease.IsDisposed)).IsTrue();
        await Assert.That(frame.Leases[0].IsCommitted).IsTrue();
        await Assert.That(frame.Leases[1].IsCommitted).IsFalse();
        await Assert.That(frame.Leases.All(lease => lease.DisposeCount == 1)).IsTrue();
        await Assert.That(api.ImportedImage!.DisposeCount).IsEqualTo(1);
        await Assert.That(api.ImportedSemaphores.Single().DisposeCount).IsEqualTo(1);
        await Assert.That(frame.SemaphoreLeaseCount).IsEqualTo(1);
    }

    [Test]
    public async Task CancellationWaitsForBoundImportThenCommitsAndReleasesExactlyOnce()
    {
        var semaphoreCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new FakeImportApi
        {
            SemaphoreImportCompletion = semaphoreCompletion.Task
        };
        var frame = new ImportFrame(
            [new CompositionExternalSemaphore(7, "semaphore")],
            CompositionExternalHandleOwnership.TransferOnSuccessfulImport);
        var importer = new AvaloniaCompositionFrameImporter(api);
        using var cancellation = new CancellationTokenSource();
        Task<AvaloniaImportedCompositionFrame> import =
            importer.ImportAsync(frame, cancellation.Token).AsTask();
        await WaitUntilAsync(() => api.SemaphoreImports == 1);

        cancellation.Cancel();
        await Task.Delay(25);

        await Assert.That(import.IsCompleted).IsFalse();
        await Assert.That(frame.Leases[0].IsCommitted).IsTrue();
        await Assert.That(frame.Leases[0].DisposeCount).IsEqualTo(1);
        await Assert.That(frame.Leases[1].IsCommitted).IsFalse();
        await Assert.That(frame.Leases[1].DisposeCount).IsEqualTo(0);
        await Assert.That(api.ImportedSemaphores.Single().DisposeCount).IsEqualTo(0);

        semaphoreCompletion.SetResult();
        Exception? failure = await CaptureFailureAsync(import);

        await Assert.That(failure).IsTypeOf<OperationCanceledException>();
        await Assert.That(frame.Leases[0].IsCommitted).IsTrue();
        await Assert.That(frame.Leases[1].IsCommitted).IsTrue();
        await Assert.That(frame.Leases.All(lease => lease.DisposeCount == 1)).IsTrue();
        await Assert.That(api.ImportedImage!.DisposeCount).IsEqualTo(1);
        await Assert.That(api.ImportedSemaphores.Single().DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task CancellationWaitsForBoundImportFailureBeforeRollingBackHandle()
    {
        var semaphoreCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new FakeImportApi
        {
            SemaphoreImportCompletion = semaphoreCompletion.Task
        };
        var frame = new ImportFrame(
            [new CompositionExternalSemaphore(7, "semaphore")],
            CompositionExternalHandleOwnership.TransferOnSuccessfulImport);
        var importer = new AvaloniaCompositionFrameImporter(api);
        using var cancellation = new CancellationTokenSource();
        Task<AvaloniaImportedCompositionFrame> import =
            importer.ImportAsync(frame, cancellation.Token).AsTask();
        await WaitUntilAsync(() => api.SemaphoreImports == 1);

        cancellation.Cancel();
        await Task.Delay(25);

        await Assert.That(import.IsCompleted).IsFalse();
        await Assert.That(frame.Leases[1].IsCommitted).IsFalse();
        await Assert.That(frame.Leases[1].DisposeCount).IsEqualTo(0);
        await Assert.That(api.ImportedSemaphores.Single().DisposeCount).IsEqualTo(0);

        semaphoreCompletion.SetException(
            new InvalidOperationException("semaphore import failed"));
        Exception? failure = await CaptureFailureAsync(import);

        await Assert.That(failure).IsTypeOf<AggregateException>();
        await Assert.That(
                ((AggregateException)failure!).Flatten().InnerExceptions
                    .Any(exception => exception is OperationCanceledException))
            .IsTrue();
        await Assert.That(
                ((AggregateException)failure).Flatten().InnerExceptions
                    .Any(exception => exception is InvalidOperationException))
            .IsTrue();
        await Assert.That(frame.Leases[0].IsCommitted).IsTrue();
        await Assert.That(frame.Leases[1].IsCommitted).IsFalse();
        await Assert.That(frame.Leases.All(lease => lease.DisposeCount == 1)).IsTrue();
        await Assert.That(api.ImportedImage!.DisposeCount).IsEqualTo(1);
        await Assert.That(api.ImportedSemaphores.Single().DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task CancellationAfterImageImportStopsNewImportsButFinalizesBoundImage()
    {
        var imageCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var api = new FakeImportApi
        {
            ImageImportCompletion = imageCompletion.Task,
            ImageImported = cancellation.Cancel
        };
        var frame = new ImportFrame(
            [new CompositionExternalSemaphore(7, "semaphore")],
            CompositionExternalHandleOwnership.TransferOnSuccessfulImport);
        var importer = new AvaloniaCompositionFrameImporter(api);

        Task<AvaloniaImportedCompositionFrame> import =
            importer.ImportAsync(frame, cancellation.Token).AsTask();
        await Task.Delay(25);

        await Assert.That(import.IsCompleted).IsFalse();
        await Assert.That(api.SemaphoreImports).IsEqualTo(0);
        await Assert.That(frame.Leases).Count().IsEqualTo(1);
        await Assert.That(frame.Leases[0].DisposeCount).IsEqualTo(0);

        imageCompletion.SetResult();
        Exception? failure = await CaptureFailureAsync(import);

        await Assert.That(failure).IsTypeOf<OperationCanceledException>();
        await Assert.That(frame.Leases[0].IsCommitted).IsTrue();
        await Assert.That(frame.Leases[0].DisposeCount).IsEqualTo(1);
        await Assert.That(api.ImportedImage!.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task PosixFileDescriptorZeroImportsSuccessfully()
    {
        var api = new FakeImportApi();
        var frame = new ImportFrame(
            [],
            imageHandleType: "VulkanOpaquePosixFileDescriptor",
            imageHandle: 0);
        var importer = new AvaloniaCompositionFrameImporter(api);

        AvaloniaImportedCompositionFrame imported =
            await importer.ImportAsync(frame, CancellationToken.None);

        await Assert.That(
                ((ICompositionExternalHandleLease)frame.Leases.Single()).IsInvalid)
            .IsFalse();
        await Assert.That(api.ImportedImage).IsNotNull();
        await Assert.That(frame.Leases.Single().DisposeCount).IsEqualTo(1);
        await imported.DisposeAsync();
    }

    [Test]
    public async Task NegativePosixFileDescriptorFailsBeforeImportAndRollsBackLease()
    {
        var api = new FakeImportApi();
        var frame = new ImportFrame(
            [],
            imageHandleType: "VulkanOpaquePosixFileDescriptor",
            imageHandle: -1);
        var importer = new AvaloniaCompositionFrameImporter(api);

        Exception? failure = await CaptureFailureAsync(
            importer.ImportAsync(frame, CancellationToken.None).AsTask());

        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        await Assert.That(
                ((ICompositionExternalHandleLease)frame.Leases.Single()).IsInvalid)
            .IsTrue();
        await Assert.That(frame.Leases.Single().DisposeCount).IsEqualTo(1);
        await Assert.That(api.ImportedImage).IsNull();
    }

    [Test]
    public async Task NullNtHandleFailsBeforeImportAndRollsBackLease()
    {
        var api = new FakeImportApi();
        var frame = new ImportFrame(
            [],
            imageHandleType: "VulkanOpaqueNtHandle",
            imageHandle: 0);
        var importer = new AvaloniaCompositionFrameImporter(api);

        Exception? failure = await CaptureFailureAsync(
            importer.ImportAsync(frame, CancellationToken.None).AsTask());

        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        await Assert.That(
                ((ICompositionExternalHandleLease)frame.Leases.Single()).IsInvalid)
            .IsTrue();
        await Assert.That(frame.Leases.Single().DisposeCount).IsEqualTo(1);
        await Assert.That(api.ImportedImage).IsNull();
    }

    [Test]
    public async Task InvalidSemaphoreRollsBackOnlyItsLeaseAfterImageImportFinalizes()
    {
        var api = new FakeImportApi();
        var frame = new ImportFrame(
            [new CompositionExternalSemaphore(7, "VulkanOpaqueNtHandle")],
            CompositionExternalHandleOwnership.TransferOnSuccessfulImport,
            semaphoreHandleType: "VulkanOpaqueNtHandle",
            semaphoreHandle: 0);
        var importer = new AvaloniaCompositionFrameImporter(api);

        Exception? failure = await CaptureFailureAsync(
            importer.ImportAsync(frame, CancellationToken.None).AsTask());

        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        await Assert.That(frame.Leases).Count().IsEqualTo(2);
        await Assert.That(frame.Leases[0].IsCommitted).IsTrue();
        await Assert.That(frame.Leases[1].IsCommitted).IsFalse();
        await Assert.That(frame.Leases.All(lease => lease.DisposeCount == 1)).IsTrue();
        await Assert.That(api.SemaphoreImports).IsEqualTo(0);
        await Assert.That(api.ImportedImage!.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task FailedLostImportReportsSurfaceLossBeforeRollback()
    {
        bool lost = false;
        var api = new FakeImportApi
        {
            ImageImportCompletion = Task.FromException(
                new InvalidOperationException("image import failed")),
            ImageLost = true
        };
        var frame = new ImportFrame([]);
        var importer = new AvaloniaCompositionFrameImporter(api, () => lost = true);

        _ = await CaptureFailureAsync(
            importer.ImportAsync(frame, CancellationToken.None).AsTask());

        await Assert.That(lost).IsTrue();
        await Assert.That(frame.Leases.Single().DisposeCount).IsEqualTo(1);
        await Assert.That(api.ImportedImage!.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task SuccessfulTransferCommitsOnlyAfterImportCompletion()
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new FakeImportApi { ImageImportCompletion = completion.Task };
        var frame = new ImportFrame([], CompositionExternalHandleOwnership.TransferOnSuccessfulImport);
        var importer = new AvaloniaCompositionFrameImporter(api);

        Task<AvaloniaImportedCompositionFrame> import =
            importer.ImportAsync(frame, CancellationToken.None).AsTask();
        await Task.Delay(25);

        await Assert.That(frame.Leases.Single().IsCommitted).IsFalse();
        completion.SetResult();
        AvaloniaImportedCompositionFrame imported = await import;
        await Assert.That(frame.Leases.Single().IsCommitted).IsTrue();
        await Assert.That(frame.Leases.Single().IsDisposed).IsTrue();
        await imported.DisposeAsync();
    }

    private static async Task<Exception?> CaptureFailureAsync(Task task)
    {
        try
        {
            await task;
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class ImportFrame(
        IReadOnlyList<CompositionExternalSemaphore> semaphores,
        CompositionExternalHandleOwnership ownership =
            CompositionExternalHandleOwnership.BorrowedUntilImportCompleted,
        string imageHandleType = "image",
        nint imageHandle = 1,
        string semaphoreHandleType = "semaphore",
        nint semaphoreHandle = 107,
        Task? imageLeaseReady = null)
        : ICompositionPresentationFrame
    {
        public List<FakeHandleLease> Leases { get; } = [];

        public int SemaphoreLeaseCount { get; private set; }

        public long AllocationId => 1;

        public CompositionExternalImage Image { get; } = new(
            imageHandleType,
            new ViewportDimensions(16, 16),
            CompositionExternalImageFormat.B8G8R8A8UNorm);

        public IReadOnlyList<CompositionExternalSemaphore> Semaphores { get; } = semaphores;

        public async ValueTask<ICompositionExternalHandleLease> LeaseImageHandleAsync(
            CancellationToken cancellationToken = default)
        {
            if (imageLeaseReady is not null)
            {
                await imageLeaseReady.WaitAsync(cancellationToken).ConfigureAwait(true);
            }
            var lease = new FakeHandleLease(imageHandle, imageHandleType, ownership);
            Leases.Add(lease);
            return lease;
        }

        public ValueTask<ICompositionExternalHandleLease> LeaseSemaphoreHandleAsync(
            long resourceId,
            CancellationToken cancellationToken = default)
        {
            SemaphoreLeaseCount++;
            var lease = new FakeHandleLease(
                semaphoreHandle,
                semaphoreHandleType,
                ownership);
            Leases.Add(lease);
            return ValueTask.FromResult<ICompositionExternalHandleLease>(lease);
        }
    }

    private sealed class FakeHandleLease(
        nint handle,
        string handleType,
        CompositionExternalHandleOwnership ownership)
        : ICompositionExternalHandleLease
    {
        public nint Handle { get; } = handle;

        public string HandleType { get; } = handleType;

        public CompositionExternalHandleOwnership Ownership { get; } = ownership;

        public bool IsCommitted { get; private set; }

        public bool IsDisposed { get; private set; }

        public int DisposeCount { get; private set; }

        public void CommitTransfer()
        {
            if (Ownership != CompositionExternalHandleOwnership.TransferOnSuccessfulImport)
            {
                throw new InvalidOperationException("Borrowed lease cannot transfer.");
            }
            IsCommitted = true;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeImportApi : ICompositionImportApi
    {
        private int _nextDispose;

        public bool FailSemaphoreImport { get; set; }

        public int? ExpectedThreadId { get; set; }

        public Task? FirstDisposeReady { get; set; }

        public TaskCompletionSource? FirstDisposeStarted { get; set; }

        public ConcurrentQueue<string> WrongThreadOperations { get; } = new();

        public int Disposals { get; private set; }

        public int ImageImports { get; private set; }

        public Task ImageImportCompletion { get; set; } = Task.CompletedTask;

        public Action? ImageImported { get; set; }

        public bool ImageLost { get; set; }

        public Task SemaphoreImportCompletion { get; set; } = Task.CompletedTask;

        public FakeImportedImage? ImportedImage { get; private set; }

        public List<FakeImportedSemaphore> ImportedSemaphores { get; } = [];

        public int SemaphoreImports { get; private set; }

        public IImportedGpuImageResource ImportImage(
            IPlatformHandle handle,
            PlatformGraphicsExternalImageProperties properties)
        {
            RecordThread("image import");
            ImageImports++;
            ImportedImage = new FakeImportedImage(
                ImageImportCompletion,
                ImageLost,
                DisposeResourceAsync);
            ImageImported?.Invoke();
            return ImportedImage;
        }

        public IImportedGpuSemaphoreResource ImportSemaphore(IPlatformHandle handle)
        {
            RecordThread("semaphore import");
            SemaphoreImports++;
            if (FailSemaphoreImport)
            {
                throw new InvalidOperationException("semaphore import failed");
            }
            var semaphore = new FakeImportedSemaphore(
                SemaphoreImportCompletion,
                DisposeResourceAsync);
            ImportedSemaphores.Add(semaphore);
            return semaphore;
        }

        private async ValueTask DisposeResourceAsync()
        {
            RecordThread("resource disposal");
            Disposals++;
            if (Interlocked.Increment(ref _nextDispose) == 1 &&
                FirstDisposeReady is not null)
            {
                FirstDisposeStarted?.TrySetResult();
                await FirstDisposeReady.ConfigureAwait(false);
            }
        }

        private void RecordThread(string operation)
        {
            if (ExpectedThreadId is int expected &&
                Environment.CurrentManagedThreadId != expected)
            {
                WrongThreadOperations.Enqueue(
                    $"{operation}: expected {expected}, actual " +
                    Environment.CurrentManagedThreadId);
            }
        }
    }

    private sealed class FakeImportedImage(
        Task importCompleted,
        bool isLost,
        Func<ValueTask> dispose)
        : IImportedGpuImageResource
    {
        public Avalonia.Rendering.Composition.ICompositionImportedGpuImage Native =>
            throw new NotSupportedException();

        public Task ImportCompleted { get; } = importCompleted;

        public bool IsLost { get; } = isLost;

        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return dispose();
        }
    }

    private sealed class FakeImportedSemaphore(
        Task importCompleted,
        Func<ValueTask> dispose) : IImportedGpuSemaphoreResource
    {
        public Avalonia.Rendering.Composition.ICompositionImportedGpuSemaphore Native =>
            throw new NotSupportedException();

        public Task ImportCompleted { get; } = importCompleted;

        public bool IsLost => false;

        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return dispose();
        }
    }

    private sealed class DedicatedThreadDispatcher : ICompositionUiDispatcher, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue =
            [];
        private readonly Thread _thread;
        private readonly TaskCompletionSource<int> _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal DedicatedThreadDispatcher()
        {
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "composition-import-test-dispatcher"
            };
            _thread.Start();
            ThreadId = _started.Task.GetAwaiter().GetResult();
        }

        internal int ThreadId { get; }

        public bool CheckAccess() =>
            Environment.CurrentManagedThreadId == ThreadId;

        public async ValueTask InvokeAsync(Func<ValueTask> action)
        {
            if (CheckAccess())
            {
                await action().ConfigureAwait(true);
                return;
            }

            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add((async _ =>
            {
                try
                {
                    await action().ConfigureAwait(true);
                    completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }, null));
            await completion.Task.ConfigureAwait(false);
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            _thread.Join(TimeSpan.FromSeconds(5));
            _queue.Dispose();
        }

        private void Run()
        {
            SynchronizationContext.SetSynchronizationContext(
                new QueueSynchronizationContext(_queue));
            _started.TrySetResult(Environment.CurrentManagedThreadId);
            foreach ((SendOrPostCallback callback, object? state) in _queue.GetConsumingEnumerable())
            {
                callback(state);
            }
        }

        private sealed class QueueSynchronizationContext(
            BlockingCollection<(SendOrPostCallback Callback, object? State)> queue)
            : SynchronizationContext
        {
            public override void Post(SendOrPostCallback callback, object? state) =>
                queue.Add((callback, state));
        }
    }
}
