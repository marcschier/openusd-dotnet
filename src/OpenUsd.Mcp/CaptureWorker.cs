// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.ExceptionServices;

namespace OpenUsd.Mcp;

public sealed class CaptureQueueFullException(int capacity)
    : InvalidOperationException($"The preview capture queue is full (capacity {capacity}).");

public sealed class CaptureAdmissionTimeoutException(TimeSpan timeout)
    : TimeoutException($"Preview capture admission timed out after {timeout}.");

public sealed class CaptureWorkerShutdownResult
{
    internal CaptureWorkerShutdownResult(
        int processorDisposeAttemptCount,
        AggregateException? failure)
    {
        ProcessorDisposeAttemptCount = processorDisposeAttemptCount;
        Failure = failure;
    }

    public AggregateException? Failure { get; }

    public int ProcessorDisposeAttemptCount { get; }

    public bool Succeeded => Failure is null;

    internal void ThrowIfFailed()
    {
        if (Failure is not null)
        {
            ExceptionDispatchInfo.Capture(Failure).Throw();
        }
    }
}

public sealed class CaptureWorker : IDisposable, IAsyncDisposable
{
    private const int ProcessorDisposeAttemptLimit = 2;

    private readonly int _capacity;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly SemaphoreSlim _itemsAvailable = new(0);
    private readonly TaskCompletionSource _operationsDrained =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IPreviewCaptureProcessor _processor;
    private readonly Queue<IWorkItem> _requests = [];
    private readonly TaskCompletionSource<CaptureWorkerShutdownResult> _shutdownCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _slots;
    private readonly object _stateGate = new();
    private readonly Thread _thread;
    private int _activeOperations;
    private int _ownedResourcesDisposed;
    private CaptureWorkerShutdownResult? _shutdownResult;
    private List<Exception>? _shutdownRequestFailures;
    private CaptureWorkerState _state;

    public CaptureWorker(IPreviewCaptureProcessor processor, int capacity = 8)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _processor = processor;
        _capacity = capacity;
        _slots = new SemaphoreSlim(capacity, capacity);
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "OpenUsd.Mcp.PreviewCapture",
        };
        _thread.Start();
    }

    public int Capacity => _capacity;

    public int PendingCount
    {
        get
        {
            lock (_stateGate)
            {
                return _requests.Count;
            }
        }
    }

    public CaptureWorkerShutdownResult? ShutdownResult
    {
        get
        {
            lock (_stateGate)
            {
                return _shutdownResult;
            }
        }
    }

    internal bool IsWorkerThreadAlive => _thread.IsAlive;

    public ValueTask<PreviewCaptureResult> CaptureAsync(
        PreviewCaptureRequest request,
        CancellationToken cancellationToken = default) =>
        CaptureAsync(request, TimeSpan.Zero, cancellationToken);

    public async ValueTask<PreviewCaptureResult> CaptureAsync(
        PreviewCaptureRequest request,
        TimeSpan admissionTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (admissionTimeout < TimeSpan.Zero &&
            admissionTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(admissionTimeout));
        }

        cancellationToken.ThrowIfCancellationRequested();
        EnterOperation();
        try
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _disposeCancellation.Token);
            bool admitted;
            try
            {
                admitted = admissionTimeout == TimeSpan.Zero
                    ? _slots.Wait(0, CancellationToken.None)
                    : await _slots.WaitAsync(admissionTimeout, linkedCancellation.Token)
                        .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ObjectDisposedException(nameof(CaptureWorker));
            }

            if (!admitted)
            {
                if (admissionTimeout == TimeSpan.Zero)
                {
                    throw new CaptureQueueFullException(_capacity);
                }

                throw new CaptureAdmissionTimeoutException(admissionTimeout);
            }

            var item = new WorkItem(request, cancellationToken);
            lock (_stateGate)
            {
                if (_state != CaptureWorkerState.Running)
                {
                    _slots.Release();
                    throw new ObjectDisposedException(nameof(CaptureWorker));
                }

                _requests.Enqueue(item);
            }

            _itemsAvailable.Release();
            return cancellationToken.CanBeCanceled
                ? await item.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false)
                : await item.Completion.Task.ConfigureAwait(false);
        }
        finally
        {
            ExitOperation();
        }
    }

    internal async ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnterOperation();
        try
        {
            var item = new ResetWorkItem(cancellationToken);
            lock (_stateGate)
            {
                ObjectDisposedException.ThrowIf(
                    _state != CaptureWorkerState.Running,
                    this);
                _requests.Enqueue(item);
            }

            _itemsAvailable.Release();
            await item.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ExitOperation();
        }
    }

    public CaptureWorkerShutdownResult Stop()
    {
        ThrowIfWorkerThread();
        return StopCoreAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _ = Stop();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        ThrowIfWorkerThread();
        _ = await StopCoreAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private Task<CaptureWorkerShutdownResult> BeginShutdown()
    {
        bool signalWorker = false;
        lock (_stateGate)
        {
            if (_state == CaptureWorkerState.Running)
            {
                _state = CaptureWorkerState.Stopping;
                signalWorker = true;
                if (_activeOperations == 0)
                {
                    _operationsDrained.TrySetResult();
                }
            }
        }

        if (signalWorker)
        {
            try
            {
                _disposeCancellation.Cancel();
            }
            catch (Exception exception)
            {
                lock (_stateGate)
                {
                    (_shutdownRequestFailures ??= []).Add(exception);
                }
            }

            _itemsAvailable.Release();
        }

        return _shutdownCompletion.Task;
    }

    private void DisposeOwnedResources()
    {
        if (Interlocked.Exchange(ref _ownedResourcesDisposed, 1) != 0)
        {
            return;
        }

        _disposeCancellation.Dispose();
        _itemsAvailable.Dispose();
        _slots.Dispose();
    }

    private int DisposeProcessor(List<Exception> failures)
    {
        if (_processor is not IDisposable disposable)
        {
            return 0;
        }

        var attemptFailures = new List<Exception>(ProcessorDisposeAttemptLimit);
        for (int attempt = 1; attempt <= ProcessorDisposeAttemptLimit; attempt++)
        {
            try
            {
                disposable.Dispose();
                return attempt;
            }
            catch (Exception exception)
            {
                attemptFailures.Add(exception);
            }
        }

        failures.Add(
            new AggregateException(
                $"The preview capture processor remained undisposed after " +
                $"{ProcessorDisposeAttemptLimit} worker-thread attempts.",
                attemptFailures));
        return ProcessorDisposeAttemptLimit;
    }

    private void EnterOperation()
    {
        lock (_stateGate)
        {
            ObjectDisposedException.ThrowIf(
                _state != CaptureWorkerState.Running,
                this);
            _activeOperations++;
        }
    }

    private void ExitOperation()
    {
        lock (_stateGate)
        {
            _activeOperations--;
            if (_state != CaptureWorkerState.Running &&
                _activeOperations == 0)
            {
                _operationsDrained.TrySetResult();
            }
        }
    }

    private void FailPendingRequests(Exception failure)
    {
        IWorkItem[] pending;
        lock (_stateGate)
        {
            pending = _requests.ToArray();
            _requests.Clear();
        }

        foreach (IWorkItem item in pending)
        {
            item.Fail(failure);
        }
    }

    private void Run()
    {
        var failures = new List<Exception>();
        try
        {
            RunRequests();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
            StopAdmissionAfterWorkerFailure(failures);
            FailPendingRequests(exception);
        }

        int processorDisposeAttemptCount = DisposeProcessor(failures);
        CaptureWorkerShutdownResult result;
        lock (_stateGate)
        {
            if (_shutdownRequestFailures is not null)
            {
                failures.AddRange(_shutdownRequestFailures);
            }

            AggregateException? failure = failures.Count == 0
                ? null
                : new AggregateException(
                    "The preview capture worker failed during shutdown.",
                    failures);
            result = new CaptureWorkerShutdownResult(
                processorDisposeAttemptCount,
                failure);
            _shutdownResult = result;
            _state = CaptureWorkerState.Stopped;
            if (_activeOperations == 0)
            {
                _operationsDrained.TrySetResult();
            }
        }

        _shutdownCompletion.TrySetResult(result);
    }

    private void RunRequests()
    {
        while (true)
        {
            _itemsAvailable.Wait();
            IWorkItem? item;
            lock (_stateGate)
            {
                if (_requests.Count == 0)
                {
                    if (_state != CaptureWorkerState.Running)
                    {
                        break;
                    }

                    continue;
                }

                item = _requests.Dequeue();
            }

            try
            {
                if (item.UsesCaptureSlot)
                {
                    _slots.Release();
                }

                item.Execute(_processor);
            }
            catch (Exception exception)
            {
                item.Fail(exception);
                throw;
            }
        }
    }

    private async Task<CaptureWorkerShutdownResult> StopCoreAsync()
    {
        CaptureWorkerShutdownResult result =
            await BeginShutdown().ConfigureAwait(false);
        _thread.Join();
        await _operationsDrained.Task.ConfigureAwait(false);
        DisposeOwnedResources();
        result.ThrowIfFailed();
        return result;
    }

    private void StopAdmissionAfterWorkerFailure(List<Exception> failures)
    {
        bool cancelAdmission = false;
        lock (_stateGate)
        {
            if (_state == CaptureWorkerState.Running)
            {
                _state = CaptureWorkerState.Stopping;
                cancelAdmission = true;
                if (_activeOperations == 0)
                {
                    _operationsDrained.TrySetResult();
                }
            }
        }

        if (!cancelAdmission)
        {
            return;
        }

        try
        {
            _disposeCancellation.Cancel();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private void ThrowIfWorkerThread()
    {
        if (Thread.CurrentThread == _thread)
        {
            throw new InvalidOperationException(
                "The capture worker cannot be stopped from its worker thread.");
        }
    }

    private interface IWorkItem
    {
        bool UsesCaptureSlot { get; }

        void Execute(IPreviewCaptureProcessor processor);

        void Fail(Exception failure);
    }

    private sealed class WorkItem(
        PreviewCaptureRequest request,
        CancellationToken cancellationToken) : IWorkItem
    {
        internal CancellationToken CancellationToken { get; } = cancellationToken;

        internal PreviewCaptureRequest Request { get; } = request;

        internal TaskCompletionSource<PreviewCaptureResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool UsesCaptureSlot => true;

        public void Execute(IPreviewCaptureProcessor processor)
        {
            try
            {
                CancellationToken.ThrowIfCancellationRequested();
                Completion.TrySetResult(
                    processor.Process(Request, CancellationToken));
            }
            catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
            {
                Completion.TrySetCanceled(CancellationToken);
            }
            catch (Exception exception)
            {
                Completion.TrySetException(exception);
            }
        }

        public void Fail(Exception failure) => Completion.TrySetException(failure);
    }

    private sealed class ResetWorkItem(CancellationToken cancellationToken) : IWorkItem
    {
        internal TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool UsesCaptureSlot => false;

        public void Execute(IPreviewCaptureProcessor processor)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (processor is IResettablePreviewCaptureProcessor resettable)
                {
                    resettable.Reset();
                }

                Completion.TrySetResult();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception exception)
            {
                Completion.TrySetException(exception);
            }
        }

        public void Fail(Exception failure) => Completion.TrySetException(failure);
    }

    private enum CaptureWorkerState
    {
        Running,
        Stopping,
        Stopped,
    }
}
