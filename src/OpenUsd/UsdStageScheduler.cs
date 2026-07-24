// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

namespace OpenUsd;

/// <summary>
/// Serializes all access to an owned <see cref="UsdStage"/> on one dedicated thread.
/// </summary>
/// <remarks>
/// Callbacks receive a borrowed stage facade that cannot dispose the scheduler-owned native stage.
/// Results are default-deny: primitive, enum, string, project detached values, concrete
/// <see cref="IUsdDetachedResult"/> implementations, and trusted arrays, lists, dictionaries, and
/// tuples composed solely from those values are accepted. Interfaces, abstract types, arbitrary
/// classes and structs, lazy sequences, custom collections, and asynchronous wrappers are rejected.
/// Cancellation is checked before native access. Cancellation requested while waiting for a native
/// stage-access lock cannot interrupt that wait and is observed after the lock is acquired.
/// </remarks>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public sealed class UsdStageScheduler : IAsyncDisposable
{
    private const int DefaultNotificationCapacity = 64;

    private readonly UsdStageChangeFeed _changes;
    private readonly Channel<IStageWorkItem> _queue;
    private readonly Func<UsdStage> _stageFactory;
    private readonly object _lifetimeGate = new();
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private int _activeRenderSources;
    private int _disposeState;
    private long _fullInvalidations;
    private long _compositionInvalidations;
    private long _propertyInvalidations;
    private long _topologyInvalidations;

    private UsdStageScheduler(
        Func<UsdStage> stageFactory,
        int capacity,
        int notificationCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(notificationCapacity);
        _stageFactory = stageFactory;
        _changes = new UsdStageChangeFeed(notificationCapacity);
        _queue = Channel.CreateBounded<IStageWorkItem>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "OpenUsd Stage"
        };
        SharedStageManagedDiagnostics.SchedulerCreated();
        _thread.Start();
    }

    /// <summary>Creates a scheduler that opens an existing stage on its owner thread.</summary>
    public static UsdStageScheduler Open(string path, int capacity = 1024) =>
        Open(path, capacity, DefaultNotificationCapacity);

    /// <summary>
    /// Creates a scheduler with bounded operation and change-notification queues.
    /// </summary>
    public static UsdStageScheduler Open(
        string path,
        int capacity,
        int notificationCapacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new UsdStageScheduler(
            () => UsdStage.Open(path),
            capacity,
            notificationCapacity);
    }

    /// <summary>Creates a scheduler that creates a new stage on its owner thread.</summary>
    public static UsdStageScheduler Create(string path, int capacity = 1024) =>
        Create(path, capacity, DefaultNotificationCapacity);

    /// <summary>
    /// Creates a scheduler with bounded operation and change-notification queues.
    /// </summary>
    public static UsdStageScheduler Create(
        string path,
        int capacity,
        int notificationCapacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new UsdStageScheduler(
            () => UsdStage.Create(path),
            capacity,
            notificationCapacity);
    }

    /// <summary>
    /// Reads bounded, ordered stage changes for the single active renderer.
    /// </summary>
    /// <remarks>
    /// When the renderer falls behind, the newest queued notification absorbs later edits.
    /// Enumeration completes after the scheduler drains and disposes, and propagates owner-thread
    /// failures. Only one enumeration may be active at a time.
    /// </remarks>
    public IAsyncEnumerable<UsdStageChange> ReadChangesAsync(
        CancellationToken cancellationToken = default) =>
        _changes.ReadAllAsync(cancellationToken);

    /// <summary>
    /// Acquires a retained stage identity for a future renderer session.
    /// </summary>
    public ValueTask<UsdStageRenderSource> AcquireRenderSourceAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfOwnerThreadReentrancy();
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposeState != 0, this);
            _activeRenderSources++;
        }

        return AcquireRenderSourceCoreAsync(cancellationToken);
    }

    private async ValueTask<UsdStageRenderSource> AcquireRenderSourceCoreAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await EnqueueAsync(
                new StageWorkItem<UsdStageRenderSource>(
                    stage => new UsdStageRenderSource(this, stage.Native.Retain()),
                    UsdStageInvalidationKind.Full,
                    _changes,
                    cancellationToken,
                    allowStageBoundResult: true,
                    recordInvalidation: RecordInvalidation),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ReleaseRenderSourceRegistration();
            throw;
        }
    }

    /// <summary>Queues a synchronous operation on the stage owner thread.</summary>
    public ValueTask<T> InvokeAsync<T>(
        Func<UsdStage, T> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ThrowIfOwnerThreadReentrancy();
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposeState) != 0,
            this);
        UsdStageBoundResultGuard.ThrowIfForbiddenType(typeof(T));
        return EnqueueAsync(
            new StageWorkItem<T>(
                action,
                UsdStageInvalidationKind.Full,
                _changes,
                cancellationToken,
                recordInvalidation: RecordInvalidation),
            cancellationToken);
    }

    /// <summary>Queues a synchronous operation that does not return a value.</summary>
    public ValueTask InvokeAsync(
        Action<UsdStage> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ThrowIfOwnerThreadReentrancy();
        return InvokeActionCoreAsync(action, cancellationToken);
    }

    private async ValueTask InvokeActionCoreAsync(
        Action<UsdStage> action,
        CancellationToken cancellationToken)
    {
        await InvokeAsync(
            stage =>
            {
                action(stage);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Queues a synchronous edit and publishes its native change-serial range.
    /// </summary>
    /// <remarks>
    /// Use this API, rather than <see cref="InvokeAsync(Action{UsdStage}, CancellationToken)"/>,
    /// for operations that may mutate the stage.
    /// </remarks>
    public ValueTask<T> EditAsync<T>(
        Func<UsdStage, T> edit,
        UsdStageInvalidationKind invalidation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ThrowIfOwnerThreadReentrancy();
        if ((uint)invalidation > (uint)UsdStageInvalidationKind.Full)
        {
            throw new ArgumentOutOfRangeException(nameof(invalidation));
        }
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposeState) != 0,
            this);
        UsdStageBoundResultGuard.ThrowIfForbiddenType(typeof(T));
        return EnqueueAsync(
            new StageWorkItem<T>(
                edit,
                invalidation,
                _changes,
                cancellationToken,
                recordInvalidation: RecordInvalidation),
            cancellationToken);
    }

    /// <summary>
    /// Queues a synchronous edit and publishes its native change-serial range.
    /// </summary>
    public ValueTask EditAsync(
        Action<UsdStage> edit,
        UsdStageInvalidationKind invalidation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ThrowIfOwnerThreadReentrancy();
        return EditActionCoreAsync(edit, invalidation, cancellationToken);
    }

    private async ValueTask EditActionCoreAsync(
        Action<UsdStage> edit,
        UsdStageInvalidationKind invalidation,
        CancellationToken cancellationToken)
    {
        await EditAsync(
            stage =>
            {
                edit(stage);
                return true;
            },
            invalidation,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        ThrowIfOwnerThreadReentrancy();
        lock (_lifetimeGate)
        {
            if (_disposeState == 0)
            {
                if (_activeRenderSources != 0)
                {
                    throw new InvalidOperationException(
                        "Dispose all active UsdStageRenderSource instances before disposing the scheduler.");
                }
                _disposeState = 1;
                _queue.Writer.TryComplete();
            }
        }
        return new ValueTask(_completion.Task);
    }

    internal void ReleaseRenderSourceRegistration()
    {
        lock (_lifetimeGate)
        {
            if (_activeRenderSources > 0)
            {
                _activeRenderSources--;
            }
        }
    }

    internal void RetainRenderSourceRegistration()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposeState != 0, this);
            _activeRenderSources++;
        }
    }

    internal UsdStageSchedulerDiagnosticSnapshot GetDiagnosticSnapshot()
    {
        lock (_lifetimeGate)
        {
            return new UsdStageSchedulerDiagnosticSnapshot(
                _activeRenderSources,
                Volatile.Read(ref _propertyInvalidations),
                Volatile.Read(ref _topologyInvalidations),
                Volatile.Read(ref _compositionInvalidations),
                Volatile.Read(ref _fullInvalidations));
        }
    }

    private void RecordInvalidation(UsdStageInvalidationKind invalidation)
    {
        switch (invalidation)
        {
            case UsdStageInvalidationKind.Property:
                Interlocked.Increment(ref _propertyInvalidations);
                break;
            case UsdStageInvalidationKind.Topology:
                Interlocked.Increment(ref _topologyInvalidations);
                break;
            case UsdStageInvalidationKind.Composition:
                Interlocked.Increment(ref _compositionInvalidations);
                break;
            default:
                Interlocked.Increment(ref _fullInvalidations);
                break;
        }
    }

    private void ThrowIfOwnerThreadReentrancy()
    {
        if (ReferenceEquals(Thread.CurrentThread, _thread))
        {
            throw new UsdStageSchedulerReentrancyException();
        }
    }

    private async ValueTask<T> EnqueueAsync<T>(
        StageWorkItem<T> item,
        CancellationToken cancellationToken)
    {
        try
        {
            await _queue.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException exception)
        {
            await _completion.Task.ConfigureAwait(false);
            throw new ObjectDisposedException(nameof(UsdStageScheduler), exception);
        }
        return await item.Task.ConfigureAwait(false);
    }

    private void Run()
    {
        try
        {
            using (UsdStage owningStage = _stageFactory())
            {
                UsdStage borrowedStage = owningStage.Borrow();
                while (_queue.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
                {
                    while (_queue.Reader.TryRead(out IStageWorkItem? item))
                    {
                        if (item.TryCancelBeforeAccess())
                        {
                            item = null;
                            continue;
                        }

                        try
                        {
                            owningStage.Native.WithAccess(
                                () => item.ExecuteWithinAccess(borrowedStage));
                            item.CompleteAfterAccess();
                        }
                        catch (Exception exception)
                        {
                            item.Fail(exception);
                            throw;
                        }

                        item = null;
                    }
                }
            }
            _changes.Complete();
            _completion.TrySetResult();
        }
        catch (Exception exception)
        {
            _queue.Writer.TryComplete(exception);
            while (_queue.Reader.TryRead(out IStageWorkItem? item))
            {
                item.Fail(exception);
            }
            _changes.Complete(exception);
            _completion.TrySetException(exception);
        }
        finally
        {
            SharedStageManagedDiagnostics.SchedulerDestroyed();
        }
    }

    private interface IStageWorkItem
    {
        bool TryCancelBeforeAccess();

        void ExecuteWithinAccess(UsdStage stage);

        void CompleteAfterAccess();

        void Fail(Exception exception);
    }

    private sealed class StageWorkItem<T> : IStageWorkItem
    {
        private readonly Func<UsdStage, T> _action;
        private readonly CancellationToken _cancellationToken;
        private readonly UsdStageChangeFeed _changes;
        private readonly TaskCompletionSource<T> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly UsdStageInvalidationKind _invalidation;
        private readonly bool _allowStageBoundResult;
        private readonly Action<UsdStageInvalidationKind> _recordInvalidation;
        private ulong _afterChangeSerial;
        private Exception? _actionFailure;
        private ulong _beforeChangeSerial;
        private bool _canceledAfterAccessBegin;
        private T _result = default!;

        internal StageWorkItem(
            Func<UsdStage, T> action,
            UsdStageInvalidationKind invalidation,
            UsdStageChangeFeed changes,
            CancellationToken cancellationToken,
            bool allowStageBoundResult = false,
            Action<UsdStageInvalidationKind>? recordInvalidation = null)
        {
            _action = action;
            _invalidation = invalidation;
            _changes = changes;
            _cancellationToken = cancellationToken;
            _allowStageBoundResult = allowStageBoundResult;
            _recordInvalidation = recordInvalidation ?? NoopInvalidation;
        }

        private static void NoopInvalidation(UsdStageInvalidationKind invalidation) =>
            _ = invalidation;

        internal Task<T> Task => _completion.Task;

        public bool TryCancelBeforeAccess()
        {
            if (!_cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            _completion.TrySetCanceled(_cancellationToken);
            return true;
        }

        public void ExecuteWithinAccess(UsdStage stage)
        {
            if (_cancellationToken.IsCancellationRequested)
            {
                _canceledAfterAccessBegin = true;
                return;
            }

            _beforeChangeSerial = stage.ChangeSerial;
            try
            {
                _result = _action(stage);
            }
            catch (Exception exception)
            {
                _actionFailure = exception;
            }

            _afterChangeSerial = stage.ChangeSerial;
        }

        public void CompleteAfterAccess()
        {
            if (_canceledAfterAccessBegin)
            {
                _completion.TrySetCanceled(_cancellationToken);
                return;
            }

            if (_afterChangeSerial != _beforeChangeSerial)
            {
                _recordInvalidation(_invalidation);
                _changes.Publish(new UsdStageChange(
                    _beforeChangeSerial,
                    _afterChangeSerial,
                    _invalidation));
            }

            if (_actionFailure is OperationCanceledException cancellation &&
                _cancellationToken.CanBeCanceled &&
                _cancellationToken.IsCancellationRequested &&
                cancellation.CancellationToken == _cancellationToken)
            {
                _completion.TrySetCanceled(_cancellationToken);
            }
            else if (_actionFailure is not null)
            {
                _completion.TrySetException(_actionFailure);
            }
            else
            {
                try
                {
                    if (!_allowStageBoundResult)
                    {
                        UsdStageBoundResultGuard.ThrowIfForbiddenResult(_result);
                    }
                    _completion.TrySetResult(_result);
                }
                catch (Exception exception)
                {
                    _completion.TrySetException(exception);
                }
            }
        }

        public void Fail(Exception exception)
        {
            _completion.TrySetException(exception);
        }
    }
}
