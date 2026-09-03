// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

/// <summary>
/// The decided result of one colour-management request, after validation.
/// </summary>
/// <param name="Requested">The choice the request carried.</param>
/// <param name="Transform">The transform to apply, or <see langword="null"/>.</param>
/// <param name="Diagnostic">The bounded reason no transform is applied, if any.</param>
internal readonly record struct ViewerColorManagementOutcome(
    ViewerColorManagement Requested,
    RenderDisplayTransform? Transform,
    string? Diagnostic,
    long Version)
{
    /// <summary>Gets whether a usable transform was produced.</summary>
    internal bool Resolved => Transform is not null;
}

/// <summary>
/// A colour-management request that could not reach the authoritative state, tagged with
/// the pipeline generation that produced it.
/// </summary>
/// <param name="Request">The choice waiting to be applied.</param>
/// <param name="Generation">
/// The pipeline generation of the request. A deferred request is replayed only while it
/// is still the newest generation; a later request replaces it outright, because
/// replaying a superseded choice would re-apply a decision the user already changed.
/// </param>
internal readonly record struct ViewerDeferredColorManagement(
    ViewerColorManagement Request,
    long Generation)
{
    /// <summary>
    /// Chooses the colour-management choice a document open should use, and whether a
    /// deferred request must be discarded because a later one replaced it.
    /// </summary>
    /// <param name="committed">The currently committed choice.</param>
    /// <param name="deferred">The deferred request, if any.</param>
    /// <param name="newestGeneration">The newest request generation.</param>
    /// <param name="committedGeneration">The newest generation already committed.</param>
    /// <remarks>
    /// The generation an open commits is the generation its opening choice actually
    /// represents, never simply the newest one. A request whose validation is still in
    /// flight has a newer generation than anything the open can see, and marking that
    /// generation committed would declare a decision the open never made -- unpending a
    /// request that has not been applied and silencing the reconciliation about it.
    /// Without a matching deferred result the open therefore carries the prior committed
    /// generation forward, leaving the in-flight request pending until it lands.
    /// </remarks>
    internal static ViewerOpeningColorManagement SelectOpeningChoice(
        ViewerColorManagement committed,
        ViewerDeferredColorManagement? deferred,
        long newestGeneration,
        long committedGeneration)
    {
        ArgumentNullException.ThrowIfNull(committed);
        if (deferred is not { } entry)
        {
            return new ViewerOpeningColorManagement(
                committed,
                committedGeneration,
                DiscardDeferred: false,
                newestGeneration);
        }

        if (entry.Generation == newestGeneration)
        {
            return new ViewerOpeningColorManagement(
                entry.Request,
                entry.Generation,
                DiscardDeferred: false,
                newestGeneration);
        }

        // A later request superseded it. Replaying it would re-apply a decision the user
        // has already changed, so it is dropped rather than folded into the open -- and
        // the open speaks only for the generation already committed.
        return new ViewerOpeningColorManagement(
            committed,
            committedGeneration,
            DiscardDeferred: true,
            newestGeneration);
    }
}

/// <summary>The choice a document open resolves, and the generations it was resolved against.</summary>
/// <param name="Choice">The colour-management choice to open with.</param>
/// <param name="Generation">The generation this open commits when it succeeds.</param>
/// <param name="DiscardDeferred">Whether a superseded deferred request must be dropped.</param>
/// <param name="NewestGeneration">
/// The newest request generation at the instant the choice was captured.
/// </param>
internal readonly record struct ViewerOpeningColorManagement(
    ViewerColorManagement Choice,
    long Generation,
    bool DiscardDeferred,
    long NewestGeneration)
{
    /// <summary>
    /// Gets whether a request started after this choice was captured, which makes the
    /// captured choice stale.
    /// </summary>
    /// <remarks>
    /// An open captures its choice, then suspends -- to bake the restored transform, and
    /// again to create the coordinator. A View &gt; Reset Layout clear started across
    /// either suspension takes a newer pipeline generation, and that generation is the
    /// only trace of it the open can see: the clear commits nothing while the document is
    /// busy, so the committed model, the cached key, and the deferral the open already
    /// consumed all still describe the world before the reset. Comparing generations is
    /// therefore what stops a captured enable from being opened with, committed, or
    /// rendered after the reset has reported the viewport clean.
    /// <para>
    /// The comparison is against the newest generation observed at capture, not against
    /// the generation the open would commit. Those differ whenever a request's validation
    /// is still in flight, and using the commit generation would report every such open
    /// as stale and rebuild it forever.
    /// </para>
    /// </remarks>
    internal bool IsSupersededBy(long newestGeneration) =>
        IsSuperseded(NewestGeneration, newestGeneration);

    /// <summary>
    /// Gets whether a capture made against <paramref name="capturedNewestGeneration"/> has
    /// been replaced by a request that took <paramref name="newestGeneration"/>.
    /// </summary>
    internal static bool IsSuperseded(
        long capturedNewestGeneration,
        long newestGeneration) =>
        newestGeneration > capturedNewestGeneration;
}

/// <summary>
/// The committed view of colour management after one request: what the menu shows, what
/// is persisted, which transform key the authoritative state carries, and what is left
/// waiting to be replayed.
/// </summary>
/// <param name="Committed">The choice the menu and the settings file must show.</param>
/// <param name="CommittedTransformKey">
/// The cache key of the display transform in the authoritative state.
/// </param>
/// <param name="Deferred">
/// A request that could not reach the state and must be replayed, or
/// <see langword="null"/>.
/// </param>
/// <param name="StateTransform">The transform the authoritative state now carries.</param>
internal readonly record struct ViewerColorManagementCommit(
    ViewerColorManagement Committed,
    string? CommittedTransformKey,
    ViewerColorManagement? Deferred,
    RenderDisplayTransform? StateTransform)
{
    /// <summary>
    /// Decides what may be committed, given whether the authoritative state actually
    /// accepted the mutation.
    /// </summary>
    /// <remarks>
    /// Committing before the mutation is confirmed is what let the menu, the persisted
    /// settings, and the cached key drift away from the image. A request made while
    /// there is no coordinator, or while a document change is in flight, changes none of
    /// them: it becomes a deferred request that the next document open replays, so the
    /// four views agree at every instant rather than only eventually.
    /// </remarks>
    internal static ViewerColorManagementCommit Decide(
        ViewerColorManagement committed,
        string? committedTransformKey,
        RenderDisplayTransform? committedStateTransform,
        ViewerColorManagement requested,
        RenderDisplayTransform? validated,
        string? diagnostic,
        bool applied)
    {
        ArgumentNullException.ThrowIfNull(committed);
        ArgumentNullException.ThrowIfNull(requested);
        ViewerColorManagement effective = diagnostic is null
            ? requested
            : requested with { Enabled = false };
        if (!applied)
        {
            return new ViewerColorManagementCommit(
                committed,
                committedTransformKey,
                effective,
                committedStateTransform);
        }

        return new ViewerColorManagementCommit(
            effective,
            validated?.CacheKey,
            Deferred: null,
            validated);
    }

    /// <summary>
    /// Gets whether the menu, the persisted choice, the committed key, and the state's
    /// transform describe the same thing.
    /// </summary>
    internal bool IsConsistent =>
        (StateTransform?.CacheKey ?? null) == CommittedTransformKey &&
        (Committed.Enabled
            ? StateTransform is not null
            : StateTransform is null);
}

/// <summary>
/// Serializes colour-management requests so only the newest one can ever decide what the
/// Viewer displays.
/// </summary>
/// <remarks>
/// <para>
/// Validation bakes a lattice, which takes long enough that a user can toggle the setting
/// or choose a different config while an earlier request is still running. Without
/// versioning, whichever validation happened to finish last would win: enabling config A
/// and then disabling would leave A applied if A's bake completed second, and the menu
/// would disagree with the image. Every request therefore takes a version, cancels the
/// request before it, and discards its own result after every suspension point if it is
/// no longer the current one.
/// </para>
/// <para>
/// Cancellation is cooperative and advisory: a bake already inside OpenColorIO cannot be
/// interrupted, so correctness rests on the discard, not on the token.
/// </para>
/// </remarks>
internal sealed class ViewerColorManagementRequestPipeline : IDisposable
{
    private readonly Func<RenderDisplayTransform, CancellationToken, Task<string?>> _validate;
    private readonly object _gate = new();
    private long _version;
    private long _committed;
    private long _superseded;
    private CancellationTokenSource? _current;
    private bool _disposed;

    /// <summary>Creates a pipeline over a validation callback.</summary>
    /// <param name="validate">
    /// Bakes the transform, returning <see langword="null"/> when it can be honoured or
    /// the bounded reason it cannot.
    /// </param>
    internal ViewerColorManagementRequestPipeline(
        Func<RenderDisplayTransform, CancellationToken, Task<string?>> validate)
    {
        ArgumentNullException.ThrowIfNull(validate);
        _validate = validate;
    }

    /// <summary>Gets the version of the most recently started request.</summary>
    internal long Version
    {
        get
        {
            lock (_gate)
            {
                return _version;
            }
        }
    }

    /// <summary>Gets how many results were discarded because a newer request arrived.</summary>
    internal long SupersededResults
    {
        get
        {
            lock (_gate)
            {
                return _superseded;
            }
        }
    }

    /// <summary>
    /// Gets whether the newest request has not yet been committed to the authoritative
    /// state.
    /// </summary>
    /// <remarks>
    /// This is a generation comparison, not a count of running operations. A bake already
    /// inside OpenColorIO cannot be cancelled, so a superseded request can still be
    /// running long after the request that replaced it has committed. Counting operations
    /// would keep reporting "pending" for as long as that abandoned work took, which
    /// suppresses every diagnostic about the request that is actually live. Only the
    /// newest generation matters, and once it commits nothing older can make the state
    /// pending again.
    /// </remarks>
    internal bool HasPendingRequest
    {
        get
        {
            lock (_gate)
            {
                return _committed < _version;
            }
        }
    }

    /// <summary>Gets the newest committed generation.</summary>
    internal long CommittedVersion
    {
        get
        {
            lock (_gate)
            {
                return _committed;
            }
        }
    }

    /// <summary>
    /// Gets whether <paramref name="version"/> is still the newest request, so its
    /// caller may go on to commit it.
    /// </summary>
    /// <remarks>
    /// A request stops being current the instant a newer one is started, which is well
    /// before that newer one produces a result. A caller that has already left the
    /// pipeline -- one awaiting the transactional mutation, for instance -- has no other
    /// way to notice, and committing there would apply a decision a newer request has
    /// already replaced. Unlike the pipeline's internal check this only asks the
    /// question; it does not count the caller as a superseded result, because the caller
    /// decides for itself what to do about the answer.
    /// </remarks>
    internal bool IsCurrent(long version)
    {
        lock (_gate)
        {
            return !_disposed && _version == version;
        }
    }

    /// <summary>
    /// Records that a generation reached the authoritative state. Older generations never
    /// move the mark backwards.
    /// </summary>
    internal void MarkCommitted(long version)
    {
        lock (_gate)
        {
            if (version > _committed)
            {
                _committed = version;
            }
        }
    }

    /// <summary>
    /// Records that the newest generation will never commit, which is what a disposed
    /// pipeline means. A deferred request deliberately does not call this: it stays
    /// pending until it is replayed or superseded.
    /// </summary>
    internal void AbandonNewestRequest()
    {
        lock (_gate)
        {
            _committed = _version;
        }
    }

    /// <summary>
    /// Resolves and validates a request, returning <see langword="null"/> when a newer
    /// request superseded it and its result must not be applied.
    /// </summary>
    internal async Task<ViewerColorManagementOutcome?> RunAsync(
        ViewerColorManagement request)
    {
        ArgumentNullException.ThrowIfNull(request);
        long version;
        CancellationToken token;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _current?.Cancel();
            _current = new CancellationTokenSource();
            version = ++_version;
            token = _current.Token;
        }

        _ = request.TryResolve(
            out RenderDisplayTransform? transform,
            out string? diagnostic);
        if (transform is not null)
        {
            string? failure;
            try
            {
                failure = await _validate(transform, token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return Discard(version);
            }

            if (!TryStayCurrent(version))
            {
                return null;
            }

            if (failure is not null)
            {
                transform = null;
                diagnostic = failure;
            }
        }

        if (!TryStayCurrent(version))
        {
            return null;
        }

        Complete(version);
        return new ViewerColorManagementOutcome(request, transform, diagnostic, version);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        CancellationTokenSource? current;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            current = _current;
            _current = null;
        }

        current?.Cancel();
        current?.Dispose();
    }

    private ViewerColorManagementOutcome? Discard(long version)
    {
        lock (_gate)
        {
            _superseded++;
            if (_version == version)
            {
                // Cancelled while still current only happens on disposal.
                _current = null;
            }
        }

        return null;
    }

    private bool TryStayCurrent(long version)
    {
        lock (_gate)
        {
            if (!_disposed && _version == version)
            {
                return true;
            }

            _superseded++;
            return false;
        }
    }

    private void Complete(long version)
    {
        CancellationTokenSource? finished = null;
        lock (_gate)
        {
            if (_version == version)
            {
                finished = _current;
                _current = null;
            }
        }

        finished?.Dispose();
    }
}

/// <summary>
/// A tracked, cancellable polling loop tied to an owner's lifetime.
/// </summary>
/// <remarks>
/// <para>
/// This replaces an <c>async void</c> dispatcher-timer tick. An <c>async void</c> tick
/// cannot be awaited, so shutdown could never know whether a tick was still running
/// against the state it was about to dispose, and an exception inside one would reach the
/// dispatcher unobserved. The loop here is a single <see cref="Task"/> the owner holds,
/// cancelled at the start of closing and drained before anything it touches is disposed.
/// </para>
/// <para>
/// The loop runs for the whole window lifetime and gates on
/// <see cref="IsEnabled"/> rather than being started and stopped, so toggling the setting
/// cannot race a half-torn-down loop.
/// </para>
/// </remarks>
internal sealed class ViewerColorManagementPoller : IDisposable
{
    private readonly Func<CancellationToken, Task> _tick;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private Task? _loop;
    private int _enabled;
    private long _ticks;
    private bool _stopped;
    private bool _lifetimeDisposed;

    /// <summary>Creates a poller.</summary>
    /// <param name="interval">The poll interval.</param>
    /// <param name="tick">The work performed on each poll.</param>
    internal ViewerColorManagementPoller(TimeSpan interval, Func<CancellationToken, Task> tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        _interval = interval;
        _tick = tick;
    }

    /// <summary>Gets or sets whether ticks are performed.</summary>
    internal bool IsEnabled
    {
        get => Volatile.Read(ref _enabled) != 0;
        set => Volatile.Write(ref _enabled, value ? 1 : 0);
    }

    /// <summary>Gets whether the loop task is still running.</summary>
    internal bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _loop is { IsCompleted: false };
            }
        }
    }

    /// <summary>Gets the number of completed ticks.</summary>
    internal long Ticks => Interlocked.Read(ref _ticks);

    /// <summary>Gets whether the loop has been cancelled.</summary>
    internal bool IsStopped
    {
        get
        {
            lock (_gate)
            {
                return _stopped;
            }
        }
    }

    /// <summary>Starts the loop once.</summary>
    internal void Start()
    {
        lock (_gate)
        {
            if (_stopped || _loop is not null)
            {
                return;
            }

            _loop = RunAsync(_lifetime.Token);
        }
    }

    /// <summary>
    /// Cancels the loop without waiting, for synchronous disposal paths that must not
    /// block the thread a tick may be trying to reach.
    /// </summary>
    internal void Cancel()
    {
        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
        }

        IsEnabled = false;
        _lifetime.Cancel();
    }

    /// <summary>Cancels the loop and awaits the in-flight tick before returning.</summary>
    internal async Task StopAsync()
    {
        Cancel();
        Task? loop;
        lock (_gate)
        {
            loop = _loop;
        }

        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }
        }

        lock (_gate)
        {
            _loop = null;
        }

        Dispose();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Cancel();
        lock (_gate)
        {
            if (_lifetimeDisposed)
            {
                return;
            }

            _lifetimeDisposed = true;
        }

        _lifetime.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(_interval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!IsEnabled)
                {
                    continue;
                }

                await _tick(cancellationToken).ConfigureAwait(false);
                _ = Interlocked.Increment(ref _ticks);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
