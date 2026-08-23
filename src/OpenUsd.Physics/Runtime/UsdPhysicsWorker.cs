// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Extraction;

namespace OpenUsd.Physics;

/// <summary>Identifies the lifecycle operation one queued transport request performs.</summary>
internal enum UsdPhysicsRequestKind
{
    /// <summary>Build or rebuild the retained world.</summary>
    Build,

    /// <summary>Return the world to the authored start state.</summary>
    Reset,

    /// <summary>Move the world to an authored time code.</summary>
    Seek,

    /// <summary>Advance the world by an explicit number of fixed sub-steps.</summary>
    Step,

    /// <summary>Begin advancing the world.</summary>
    Play,

    /// <summary>Stop advancing the world without discarding it.</summary>
    Pause,

    /// <summary>Change whether playback wraps at the authored end.</summary>
    SetLoop,

    /// <summary>Mark the built world stale because authored physics changed.</summary>
    Invalidate,

    /// <summary>Stage runtime commands for the next advance.</summary>
    Commands,

    /// <summary>Attach the extracted stage the next build composes from.</summary>
    AttachExtraction,

    /// <summary>Release the world and stop the worker.</summary>
    Shutdown
}

/// <summary>
/// One queued lifecycle request the physics worker executes on its owning thread.
/// </summary>
internal sealed class UsdPhysicsWorkItem
{
    /// <summary>Initializes a request.</summary>
    internal UsdPhysicsWorkItem(
        UsdPhysicsRequestKind kind,
        double value = 0,
        bool flag = false,
        UsdPhysicsInvalidationReason reason = UsdPhysicsInvalidationReason.External,
        IReadOnlyList<UsdPhysicsCommand>? commands = null,
        UsdPhysicsExtractionPage? extraction = null,
        CancellationToken cancellationToken = default)
    {
        Kind = kind;
        Value = value;
        Flag = flag;
        Reason = reason;
        CancellationToken = cancellationToken;
        Commands = commands;
        Extraction = extraction;
    }

    /// <summary>Gets the operation to perform.</summary>
    internal UsdPhysicsRequestKind Kind { get; }

    /// <summary>Gets the numeric argument, such as the seek target time code.</summary>
    internal double Value { get; }

    /// <summary>Gets the numeric argument reinterpreted as a whole sub-step count.</summary>
    internal int Steps => (int)Value;

    /// <summary>Gets the boolean argument, such as the requested loop mode.</summary>
    internal bool Flag { get; }

    /// <summary>Gets the reason an invalidation was requested.</summary>
    internal UsdPhysicsInvalidationReason Reason { get; }

    /// <summary>Gets the token that cancels this request while it runs.</summary>
    internal CancellationToken CancellationToken { get; }

    /// <summary>Gets the runtime commands a <see cref="UsdPhysicsRequestKind.Commands"/> stages.</summary>
    internal IReadOnlyList<UsdPhysicsCommand>? Commands { get; }

    /// <summary>Gets the extracted stage an attach request carries.</summary>
    internal UsdPhysicsExtractionPage? Extraction { get; }

    /// <summary>Gets or sets what staging the request's commands accepted and refused.</summary>
    /// <remarks>
    /// The staging result is written on the owning thread and read by the caller only after
    /// <see cref="Completion"/> has completed, which is what publishes it safely without a lock.
    /// </remarks>
    internal UsdPhysicsCommandStaging Staging { get; set; }

    /// <summary>Gets the completion the caller awaits.</summary>
    internal TaskCompletionSource Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// Receives worker callbacks on the thread that exclusively owns the physics world.
/// </summary>
internal interface IUsdPhysicsWorkerHost
{
    /// <summary>Executes one dequeued lifecycle request.</summary>
    void Execute(UsdPhysicsWorkItem item);

    /// <summary>Advances the simulation by at most one bounded tick.</summary>
    /// <returns><see langword="true"/> when the worker should tick again without idling.</returns>
    bool Tick();
}

/// <summary>
/// Owns the single thread every physics world operation runs on, and the bounded request queue.
/// </summary>
/// <remarks>
/// <para>
/// Strict thread ownership is the point of this type. The world, the tick scheduler, and the
/// checkpoint cache are touched only from the worker thread, so none of them needs internal locking
/// and none of them can be observed half-updated. Callers never run world code on their own thread:
/// they enqueue a request and await its completion.
/// </para>
/// <para>
/// The queue is bounded and never grows. A caller that floods the transport with lifecycle requests
/// is rejected with <see cref="UsdPhysicsTransportQueueFullException"/> instead of being allowed to
/// consume unbounded memory or push the worker into unbounded latency.
/// </para>
/// </remarks>
internal sealed class UsdPhysicsWorker : IDisposable
{
    private readonly IUsdPhysicsWorkerHost _host;
    private readonly UsdPhysicsWorkItem?[] _ring;
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _signal = new(false);
    private readonly int _tickIntervalMilliseconds;
    private readonly Thread? _thread;

    private int _head;
    private int _tail;
    private int _count;
    private int _ownerThreadId;
    private int _pumping;
    private volatile bool _stopping;
    private bool _disposed;

    /// <summary>Initializes a worker over a bounded queue.</summary>
    /// <param name="host">The host whose callbacks run on the owning thread.</param>
    /// <param name="capacity">The bounded request capacity.</param>
    /// <param name="tickIntervalMilliseconds">How long an idle worker waits before ticking again.</param>
    /// <param name="useDedicatedThread">
    /// Whether to start a dedicated thread. Tests pump the worker manually instead, which makes every
    /// timing rule deterministic without removing the thread-ownership invariant.
    /// </param>
    internal UsdPhysicsWorker(
        IUsdPhysicsWorkerHost host,
        int capacity,
        int tickIntervalMilliseconds,
        bool useDedicatedThread)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegative(tickIntervalMilliseconds);

        _host = host;
        _ring = new UsdPhysicsWorkItem?[capacity];
        _tickIntervalMilliseconds = Math.Max(tickIntervalMilliseconds, 1);
        if (useDedicatedThread)
        {
            _thread = new Thread(RunLoop)
            {
                IsBackground = true,
                Name = "OpenUsd.Physics.Transport"
            };
        }
    }

    /// <summary>Gets a value indicating whether a dedicated worker thread runs the loop.</summary>
    internal bool HasDedicatedThread => _thread is not null;

    /// <summary>Gets the managed identifier of the thread that exclusively owns the world.</summary>
    internal int OwnerThreadId => Volatile.Read(ref _ownerThreadId);

    /// <summary>Gets a value indicating whether the calling thread owns the world.</summary>
    internal bool IsOwnerThread => Environment.CurrentManagedThreadId == OwnerThreadId;

    /// <summary>Gets the number of queued but not yet executed requests.</summary>
    internal int QueueDepth
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    /// <summary>Gets the bounded request capacity.</summary>
    internal int Capacity => _ring.Length;

    /// <summary>Binds ownership and starts the dedicated thread when one is used.</summary>
    internal void Start()
    {
        if (_thread is null)
        {
            Volatile.Write(ref _ownerThreadId, Environment.CurrentManagedThreadId);
            return;
        }

        _thread.Start();
    }

    /// <summary>Queues one lifecycle request and returns the task that completes when it ran.</summary>
    /// <exception cref="UsdPhysicsTransportQueueFullException">The bounded queue is full.</exception>
    internal Task EnqueueAsync(UsdPhysicsWorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_stopping && item.Kind != UsdPhysicsRequestKind.Shutdown)
            {
                throw new UsdPhysicsTransportStateException(UsdPhysicsTransportState.Disposed);
            }
            if (_count == _ring.Length)
            {
                throw new UsdPhysicsTransportQueueFullException(_ring.Length);
            }

            _ring[_tail] = item;
            _tail = (_tail + 1) % _ring.Length;
            _count++;
        }

        _signal.Set();
        return item.Completion.Task;
    }

    /// <summary>
    /// Drains every queued request and performs one tick on the calling thread.
    /// </summary>
    /// <remarks>
    /// Only used when no dedicated thread was started. Pumps are mutually exclusive, so exactly one
    /// thread at a time owns the world exactly as the dedicated thread does; the owner may move
    /// between pumps because a pump never overlaps another pump.
    /// </remarks>
    internal bool Pump()
    {
        if (_thread is not null)
        {
            throw new InvalidOperationException("A worker with a dedicated thread cannot be pumped manually.");
        }
        if (Interlocked.CompareExchange(ref _pumping, 1, 0) != 0)
        {
            throw new UsdPhysicsStepOwnershipException(
                "UsdPhysicsTransport pumps its physics worker from one thread at a time.");
        }

        try
        {
            Volatile.Write(ref _ownerThreadId, Environment.CurrentManagedThreadId);
            DrainRequests();
            return _host.Tick();
        }
        finally
        {
            Volatile.Write(ref _pumping, 0);
        }
    }

    /// <summary>Stops the worker loop and waits for the dedicated thread to exit.</summary>
    internal void Stop()
    {
        _stopping = true;
        _signal.Set();
        if (_thread is { } thread && thread.IsAlive && Environment.CurrentManagedThreadId != thread.ManagedThreadId)
        {
            thread.Join();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
        }

        Stop();
        FailPending();
        _signal.Dispose();
    }

    private void RunLoop()
    {
        Volatile.Write(ref _ownerThreadId, Environment.CurrentManagedThreadId);
        while (!_stopping)
        {
            DrainRequests();
            if (_stopping)
            {
                break;
            }

            bool busy = _host.Tick();
            if (busy || HasQueued())
            {
                continue;
            }

            _signal.Wait(_tickIntervalMilliseconds);
            _signal.Reset();
        }

        DrainRequests();
        FailPending();
    }

    private bool HasQueued()
    {
        lock (_gate)
        {
            return _count > 0;
        }
    }

    private void DrainRequests()
    {
        while (true)
        {
            UsdPhysicsWorkItem? item;
            lock (_gate)
            {
                if (_count == 0)
                {
                    return;
                }

                item = _ring[_head];
                _ring[_head] = null;
                _head = (_head + 1) % _ring.Length;
                _count--;
            }

            if (item is null)
            {
                continue;
            }

            try
            {
                if (item.CancellationToken.IsCancellationRequested)
                {
                    item.Completion.TrySetCanceled(item.CancellationToken);
                    continue;
                }

                _host.Execute(item);
                item.Completion.TrySetResult();
            }
            catch (OperationCanceledException canceled)
            {
                item.Completion.TrySetCanceled(canceled.CancellationToken);
            }
#pragma warning disable CA1031 // The failure is transferred to the awaiting caller verbatim.
            catch (Exception exception)
            {
                item.Completion.TrySetException(exception);
            }
#pragma warning restore CA1031

            if (item.Kind == UsdPhysicsRequestKind.Shutdown)
            {
                _stopping = true;
                return;
            }
        }
    }

    private void FailPending()
    {
        while (true)
        {
            UsdPhysicsWorkItem? item;
            lock (_gate)
            {
                if (_count == 0)
                {
                    return;
                }

                item = _ring[_head];
                _ring[_head] = null;
                _head = (_head + 1) % _ring.Length;
                _count--;
            }

            item?.Completion.TrySetException(
                new UsdPhysicsTransportStateException(UsdPhysicsTransportState.Disposed));
        }
    }
}
