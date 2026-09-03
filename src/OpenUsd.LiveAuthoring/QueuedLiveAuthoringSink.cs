// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.LiveAuthoring;

/// <summary>
/// Provides bounded admission and ordered execution for one logical producer. Admission is
/// acknowledged separately from the eventual applied result: <see cref="ApplyAsync"/> completes as soon
/// as a batch is enqueued or coalesced, and callers observe execution through the returned receipt's
/// <see cref="LiveAuthoringAdmissionReceipt.Applied"/> task.
/// </summary>
/// <remarks>
/// The producer may have multiple outstanding calls, but must invoke them in strictly increasing
/// sequence order. Concurrent independent producers are unsupported and must serialize externally.
/// </remarks>
public sealed class QueuedLiveAuthoringSink : ILiveAuthoringSink, IAsyncDisposable
{
    private const int MaxHealthDetailLength = 256;

    private readonly CancellationTokenSource _acceptingCancellation = new();
    private readonly SemaphoreSlim _admissionGate = new(1, 1);
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ILiveAuthoringBatchExecutor _executor;
    private readonly object _gate = new();
    private readonly IProgress<LiveAuthoringHealthEvent>? _healthObserver;
    private readonly SemaphoreSlim _items = new(0);
    private readonly Queue<PendingBatch> _pending = [];
    private readonly SemaphoreSlim _slots;
    private readonly Task _worker;
    private bool _accepting = true;
    private long _coalescedBatchCount;
    private int _disposeState;
    private long _healthObserverFailureCount;
    private long _lastAcceptedSequence;
    private long? _lastAppliedSequence;
    private string? _lastFailureDetail;
    private long? _lastFailedSequence;
    private string? _lastHealthObserverFailureDetail;
    private int _peakPendingBatchCount;

    /// <summary>Initializes a single-producer bounded queue that owns the supplied executor.</summary>
    public QueuedLiveAuthoringSink(
        ILiveAuthoringBatchExecutor executor,
        int capacity,
        IProgress<LiveAuthoringHealthEvent>? healthObserver = null)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _executor = executor;
        _healthObserver = healthObserver;
        _slots = new SemaphoreSlim(capacity, capacity);
        Capacity = capacity;
        _worker = Task.Run(ProcessAsync);
    }

    /// <summary>Gets the maximum number of pending batches.</summary>
    public int Capacity { get; }

    /// <summary>Gets the current number of pending batches.</summary>
    public int PendingBatchCount
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count;
            }
        }
    }

    /// <summary>Gets the largest observed pending-batch count.</summary>
    public int PeakPendingBatchCount => Volatile.Read(ref _peakPendingBatchCount);

    /// <summary>Gets the number of pending snapshot batches superseded by newer snapshots.</summary>
    public long CoalescedBatchCount => Volatile.Read(ref _coalescedBatchCount);

    /// <summary>
    /// Gets the number of times the constructor-supplied health observer threw while reporting an
    /// event. Every such exception is isolated and never affects admission or applied-result semantics.
    /// </summary>
    public long HealthObserverFailureCount => Volatile.Read(ref _healthObserverFailureCount);

    /// <summary>Gets a task that completes when disposal finishes draining the queue.</summary>
    public Task Completion => _completion.Task;

    /// <summary>Returns a bounded, point-in-time snapshot of queue admission and execution health.</summary>
    public LiveAuthoringHealthSnapshot GetHealthSnapshot()
    {
        lock (_gate)
        {
            return new LiveAuthoringHealthSnapshot(
                Capacity,
                _pending.Count,
                PeakPendingBatchCount,
                CoalescedBatchCount,
                _accepting,
                _lastAcceptedSequence,
                _lastAppliedSequence,
                _lastFailedSequence,
                _lastFailureDetail,
                DateTimeOffset.UtcNow,
                Volatile.Read(ref _healthObserverFailureCount),
                _lastHealthObserverFailureDetail);
        }
    }

    /// <inheritdoc/>
    public async ValueTask<LiveAuthoringAdmissionReceipt> ApplyAsync(
        LiveAuthoringBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        cancellationToken.ThrowIfCancellationRequested();
        var waiter = new TaskCompletionSource<LiveAuthoringBatchResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource admissionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _acceptingCancellation.Token);
        bool slotReserved = false;
        bool coalesced = false;
        try
        {
            await _admissionGate.WaitAsync(admissionCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested &&
            _acceptingCancellation.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(QueuedLiveAuthoringSink));
        }

        try
        {
            try
            {
                lock (_gate)
                {
                    ThrowIfNotAccepting();
                    ValidateSequence(batch.Sequence);
                    coalesced = _pending.Count == Capacity &&
                        _pending.Last().TrySupersede(batch, waiter);
                    if (coalesced)
                    {
                        _lastAcceptedSequence = batch.Sequence;
                        Interlocked.Increment(ref _coalescedBatchCount);
                    }
                }
            }
            catch (Exception exception) when (
                exception is ArgumentOutOfRangeException or ObjectDisposedException)
            {
                ReportHealth(
                    LiveAuthoringHealthEventKind.Rejected,
                    batch.Sequence,
                    batch.CorrelationId,
                    batch.OriginId,
                    exception.Message);
                throw;
            }

            if (!coalesced)
            {
                await _slots.WaitAsync(admissionCancellation.Token).ConfigureAwait(false);
                slotReserved = true;
                admissionCancellation.Token.ThrowIfCancellationRequested();
                try
                {
                    lock (_gate)
                    {
                        ThrowIfNotAccepting();
                        ValidateSequence(batch.Sequence);
                        _lastAcceptedSequence = batch.Sequence;
                        _pending.Enqueue(new PendingBatch(batch, waiter));
                        UpdatePeakPendingCount(_pending.Count);
                        slotReserved = false;
                    }
                }
                catch (Exception exception) when (
                    exception is ArgumentOutOfRangeException or ObjectDisposedException)
                {
                    ReportHealth(
                        LiveAuthoringHealthEventKind.Rejected,
                        batch.Sequence,
                        batch.CorrelationId,
                        batch.OriginId,
                        exception.Message);
                    throw;
                }
                _items.Release();
            }
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested &&
            _acceptingCancellation.IsCancellationRequested)
        {
            if (slotReserved)
            {
                _slots.Release();
                slotReserved = false;
            }
            throw new ObjectDisposedException(nameof(QueuedLiveAuthoringSink));
        }
        catch
        {
            if (slotReserved)
            {
                _slots.Release();
            }
            throw;
        }
        finally
        {
            _admissionGate.Release();
        }

        ReportHealth(
            coalesced ? LiveAuthoringHealthEventKind.Coalesced : LiveAuthoringHealthEventKind.Admitted,
            batch.Sequence,
            batch.CorrelationId,
            batch.OriginId,
            detail: null);
        return new LiveAuthoringAdmissionReceipt(
            batch.Sequence,
            batch.CorrelationId,
            batch.OriginId,
            coalesced,
            waiter.Task);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
        {
            await _disposed.Task.ConfigureAwait(false);
            return;
        }

        try
        {
            lock (_gate)
            {
                _accepting = false;
            }
            _acceptingCancellation.Cancel();
            _items.Release();
            await _worker.ConfigureAwait(false);
            await _executor.DisposeAsync().ConfigureAwait(false);
            _disposed.TrySetResult();
        }
        catch (Exception exception)
        {
            _disposed.TrySetException(exception);
            throw;
        }
        finally
        {
            _items.Dispose();
            _slots.Dispose();
            _admissionGate.Dispose();
            _acceptingCancellation.Dispose();
            Volatile.Write(ref _disposeState, 2);
            ReportHealth(LiveAuthoringHealthEventKind.Disposed, _lastAcceptedSequence, null, null, null);
        }
    }

    private async Task ProcessAsync()
    {
        try
        {
            while (true)
            {
                await _items.WaitAsync().ConfigureAwait(false);
                PendingBatch? pending = null;
                lock (_gate)
                {
                    if (_pending.Count > 0)
                    {
                        pending = _pending.Dequeue();
                        _slots.Release();
                    }
                    else if (!_accepting)
                    {
                        break;
                    }
                }

                if (pending is null)
                {
                    continue;
                }

                try
                {
                    LiveAuthoringBatchResult result = await _executor.ExecuteAsync(
                        pending.Batch,
                        CancellationToken.None).ConfigureAwait(false);
                    pending.Complete(result);
                    lock (_gate)
                    {
                        _lastAppliedSequence = pending.Batch.Sequence;
                    }
                    ReportHealth(
                        LiveAuthoringHealthEventKind.Applied,
                        pending.Batch.Sequence,
                        pending.Batch.CorrelationId,
                        pending.Batch.OriginId,
                        detail: null);
                }
                catch (Exception exception)
                {
                    pending.Fail(exception);
                    lock (_gate)
                    {
                        _lastFailedSequence = pending.Batch.Sequence;
                        _lastFailureDetail = Truncate(exception.Message);
                    }
                    ReportHealth(
                        LiveAuthoringHealthEventKind.Failed,
                        pending.Batch.Sequence,
                        pending.Batch.CorrelationId,
                        pending.Batch.OriginId,
                        exception.Message);
                }
            }

            _completion.TrySetResult();
        }
        catch (Exception exception)
        {
            FailPending(exception);
            _completion.TrySetException(exception);
        }
    }

    private void FailPending(Exception exception)
    {
        lock (_gate)
        {
            while (_pending.TryDequeue(out PendingBatch? pending))
            {
                pending.Fail(exception);
                _lastFailedSequence = pending.Batch.Sequence;
                _lastFailureDetail = Truncate(exception.Message);
            }
        }
    }

    private void ValidateSequence(long sequence)
    {
        if (sequence <= _lastAcceptedSequence)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequence),
                sequence,
                "Live-authoring batch sequences must be strictly increasing.");
        }
    }

    private void ThrowIfNotAccepting()
    {
        ObjectDisposedException.ThrowIf(!_accepting, this);
    }

    private void UpdatePeakPendingCount(int count)
    {
        int observed;
        do
        {
            observed = Volatile.Read(ref _peakPendingBatchCount);
            if (observed >= count)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _peakPendingBatchCount, count, observed) != observed);
    }

    private void ReportHealth(
        LiveAuthoringHealthEventKind kind,
        long sequence,
        string? correlationId,
        string? originId,
        string? detail)
    {
        if (_healthObserver is null)
        {
            return;
        }

        // A caller-supplied IProgress<T> is untrusted code running inline on the admission or worker
        // path. It must never be able to change admission or applied-result semantics for a
        // well-behaved caller, so every exception it throws is isolated here: counted and recorded in
        // the health snapshot, never rethrown, and never allowed to fail ApplyAsync or lose a batch.
        try
        {
            _healthObserver.Report(new LiveAuthoringHealthEvent(
                kind,
                sequence,
                correlationId,
                originId,
                DateTimeOffset.UtcNow,
                PendingBatchCount,
                Truncate(detail)));
        }
        catch (Exception exception)
        {
            Interlocked.Increment(ref _healthObserverFailureCount);
            lock (_gate)
            {
                _lastHealthObserverFailureDetail = Truncate(exception.Message);
            }
        }
    }

    private static string? Truncate(string? message)
    {
        if (message is null || message.Length <= MaxHealthDetailLength)
        {
            return message;
        }
        return string.Concat(message.AsSpan(0, MaxHealthDetailLength), "\u2026");
    }

    private sealed class PendingBatch
    {
        private readonly List<Waiter> _waiters;
        private int _batchCount = 1;
        private readonly long _firstSequence;

        internal PendingBatch(
            LiveAuthoringBatch batch,
            TaskCompletionSource<LiveAuthoringBatchResult> waiter)
        {
            Batch = batch;
            _firstSequence = batch.Sequence;
            _waiters = [new Waiter(batch.CorrelationId, batch.OriginId, waiter)];
        }

        internal LiveAuthoringBatch Batch { get; private set; }

        internal bool TrySupersede(
            LiveAuthoringBatch batch,
            TaskCompletionSource<LiveAuthoringBatchResult> waiter)
        {
            if (Batch.CoalescingKey is null ||
                !string.Equals(
                    Batch.CoalescingKey,
                    batch.CoalescingKey,
                    StringComparison.Ordinal))
            {
                return false;
            }

            Batch = batch;
            _batchCount++;
            _waiters.Add(new Waiter(batch.CorrelationId, batch.OriginId, waiter));
            return true;
        }

        internal void Complete(LiveAuthoringBatchResult result)
        {
            LiveAuthoringBatchResult shared = result with
            {
                FirstSequence = _firstSequence,
                LastSequence = Batch.Sequence,
                BatchCount = _batchCount
            };
            foreach (Waiter waiter in _waiters)
            {
                // Every waiter observes the same coalesced sequence range, batch count, and actual
                // edit outcome, but each keeps its own opaque correlation/origin identifiers: a caller
                // that was superseded must not see another caller's correlation value in its result.
                waiter.CompletionSource.TrySetResult(shared with
                {
                    CorrelationId = waiter.CorrelationId,
                    OriginId = waiter.OriginId
                });
            }
        }

        internal void Fail(Exception exception)
        {
            foreach (Waiter waiter in _waiters)
            {
                waiter.CompletionSource.TrySetException(exception);
            }
        }

        private readonly record struct Waiter(
            string? CorrelationId,
            string? OriginId,
            TaskCompletionSource<LiveAuthoringBatchResult> CompletionSource);
    }
}
