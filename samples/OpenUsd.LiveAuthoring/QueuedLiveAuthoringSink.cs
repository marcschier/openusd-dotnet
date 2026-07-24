// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.LiveAuthoring;

/// <summary>
/// Provides bounded admission and ordered execution for one logical producer.
/// </summary>
/// <remarks>
/// The producer may have multiple outstanding calls, but must invoke them in strictly increasing
/// sequence order. Concurrent independent producers are unsupported and must serialize externally.
/// </remarks>
public sealed class QueuedLiveAuthoringSink : ILiveAuthoringSink, IAsyncDisposable
{
    private readonly CancellationTokenSource _acceptingCancellation = new();
    private readonly SemaphoreSlim _admissionGate = new(1, 1);
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ILiveAuthoringBatchExecutor _executor;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _items = new(0);
    private readonly Queue<PendingBatch> _pending = [];
    private readonly SemaphoreSlim _slots;
    private readonly Task _worker;
    private bool _accepting = true;
    private long _coalescedBatchCount;
    private int _disposeState;
    private long _lastAcceptedSequence;
    private int _peakPendingBatchCount;

    /// <summary>Initializes a single-producer bounded queue that owns the supplied executor.</summary>
    public QueuedLiveAuthoringSink(ILiveAuthoringBatchExecutor executor, int capacity)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _executor = executor;
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

    /// <summary>Gets a task that completes when disposal finishes draining the queue.</summary>
    public Task Completion => _completion.Task;

    /// <inheritdoc/>
    public async ValueTask<LiveAuthoringBatchResult> ApplyAsync(
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
        try
        {
            await _admissionGate.WaitAsync(admissionCancellation.Token).ConfigureAwait(false);
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

        try
        {
            bool coalesced;
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

            if (!coalesced)
            {
                await _slots.WaitAsync(admissionCancellation.Token).ConfigureAwait(false);
                slotReserved = true;
                admissionCancellation.Token.ThrowIfCancellationRequested();
                lock (_gate)
                {
                    ThrowIfNotAccepting();
                    ValidateSequence(batch.Sequence);
                    _lastAcceptedSequence = batch.Sequence;
                    _pending.Enqueue(new PendingBatch(batch, waiter));
                    UpdatePeakPendingCount(_pending.Count);
                    slotReserved = false;
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

        return await waiter.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
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
                }
                catch (Exception exception)
                {
                    pending.Fail(exception);
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

    private sealed class PendingBatch
    {
        private readonly List<TaskCompletionSource<LiveAuthoringBatchResult>> _waiters;
        private int _batchCount = 1;
        private readonly long _firstSequence;

        internal PendingBatch(
            LiveAuthoringBatch batch,
            TaskCompletionSource<LiveAuthoringBatchResult> waiter)
        {
            Batch = batch;
            _firstSequence = batch.Sequence;
            _waiters = [waiter];
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
            _waiters.Add(waiter);
            return true;
        }

        internal void Complete(LiveAuthoringBatchResult result)
        {
            LiveAuthoringBatchResult coalesced = result with
            {
                FirstSequence = _firstSequence,
                LastSequence = Batch.Sequence,
                BatchCount = _batchCount
            };
            foreach (TaskCompletionSource<LiveAuthoringBatchResult> waiter in _waiters)
            {
                waiter.TrySetResult(coalesced);
            }
        }

        internal void Fail(Exception exception)
        {
            foreach (TaskCompletionSource<LiveAuthoringBatchResult> waiter in _waiters)
            {
                waiter.TrySetException(exception);
            }
        }
    }
}
