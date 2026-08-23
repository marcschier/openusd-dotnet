// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics;

/// <summary>
/// A retained UsdPhysics simulation session bound to a stage's owner thread.
/// </summary>
/// <remarks>
/// <para>
/// A session is built once through <see cref="BuildAsync"/>, which extracts an immutable build
/// snapshot from the authored stage through the supplied <see cref="UsdStageScheduler"/> and hands
/// it to a dedicated backend that owns every native simulation object. Only that backend, never the
/// stage scheduler thread, retains native simulation state.
/// </para>
/// <para>
/// Stepping is intentionally synchronous and constrained by an explicit ownership model: exactly
/// one <see cref="UsdPhysicsStepOwnership"/> may be active for a session at a time, obtained through
/// <see cref="AcquireStepOwnership"/> and required by every call to <see cref="Step"/>. The token
/// captures the managed thread that acquired it, and <see cref="Step"/> validates that every call
/// arrives on that exact thread; this models the single dedicated physics worker that owns every
/// native object without allowing accidental cross-thread re-entrancy into the backend.
/// </para>
/// <para>
/// <see cref="ResetAsync"/>, <see cref="SeekAsync"/>, <see cref="BakeAsync"/>, <see cref="Step"/>,
/// and <see cref="DisposeAsync"/> all share one exclusive world gate: at most one of them ever
/// executes against the backend at a time for a given session, and none of them ever observes a
/// partial check-then-act window. The async operations await their turn; <see cref="Step"/> never
/// blocks and instead fails fast with <see cref="UsdPhysicsStepOwnershipException"/> if the gate is
/// currently held by another operation. <see cref="Capabilities"/>, <see cref="Diagnostics"/>, and
/// <see cref="LatestSnapshot"/> are published together from a single immutable snapshot so a reader
/// never observes one refreshed while the others still reflect a prior operation.
/// </para>
/// <para>
/// Until the retained native world ABI and PhysX translator are implemented, every session reports
/// <see cref="UsdPhysicsCapabilities.None"/> and stepping, seeking, and baking succeed structurally
/// but never fabricate simulated results; unsupported capabilities are always reported through
/// <see cref="Diagnostics"/> rather than silently producing incorrect data.
/// </para>
/// </remarks>
public sealed class UsdPhysicsSession : IAsyncDisposable
{
    private readonly IUsdPhysicsBackend _backend;
    private readonly UsdStageScheduler? _scheduler;

    /// <summary>
    /// The exclusive world gate serializing every backend-touching operation: <see cref="ResetAsync"/>,
    /// <see cref="SeekAsync"/>, <see cref="BakeAsync"/>, <see cref="Step"/>, and
    /// <see cref="DisposeAsync"/>. Async operations acquire it with <c>WaitAsync</c> and therefore
    /// queue deterministically behind one another; <see cref="Step"/> acquires it with a
    /// non-blocking <c>Wait(0)</c> so a synchronous stepping call never awaits an in-flight async
    /// lifecycle operation and instead reports contention immediately.
    /// </summary>
    /// <remarks>
    /// This gate is intentionally never disposed and is retained for the lifetime of the owning
    /// <see cref="UsdPhysicsSession"/> object, including after disposal. See the disposal-semantics
    /// remarks on <see cref="DisposeAsync"/> for why: disposing it would let a concurrent caller that
    /// already passed the disposed-state precheck observe an <see cref="ObjectDisposedException"/>
    /// thrown by the semaphore itself instead of this session's own controlled state check. A
    /// <see cref="SemaphoreSlim"/> holds no unmanaged resource unless its <c>AvailableWaitHandle</c>
    /// is accessed, which this type never does, so leaving it undisposed has no real resource cost.
    /// </remarks>
    private readonly SemaphoreSlim _worldGate = new(1, 1);

    private object? _activeOwner;
    private int _state;
    private PublishedState _published;

    private UsdPhysicsSession(
        UsdStageScheduler? scheduler,
        UsdPhysicsSessionOptions options,
        IUsdPhysicsBackend backend,
        UsdPhysicsBuildOutcome outcome)
    {
        _scheduler = scheduler;
        Options = options;
        _backend = backend;
        _published = new PublishedState(outcome.Capabilities, outcome.Diagnostics, UsdPhysicsSnapshot.Empty);
        _state = (int)UsdPhysicsSessionState.Ready;
    }

    /// <summary>Gets the options this session was built or last reset with.</summary>
    public UsdPhysicsSessionOptions Options { get; }

    /// <summary>Gets the capabilities the active backend reported after the last build or reset.</summary>
    /// <remarks>
    /// Read together with <see cref="Diagnostics"/> and <see cref="LatestSnapshot"/>, this always
    /// reflects one consistent, atomically published operation outcome; it is never possible to
    /// observe capabilities from a newer operation alongside diagnostics from an older one.
    /// </remarks>
    public UsdPhysicsCapabilities Capabilities => Volatile.Read(ref _published).Capabilities;

    /// <summary>Gets the diagnostics reported by the last build, reset, or bake operation.</summary>
    public UsdPhysicsDiagnostics Diagnostics => Volatile.Read(ref _published).Diagnostics;

    /// <summary>Gets the current lifecycle state.</summary>
    public UsdPhysicsSessionState State => (UsdPhysicsSessionState)Volatile.Read(ref _state);

    /// <summary>Gets the most recently published simulation snapshot.</summary>
    /// <remarks>
    /// This is <see cref="UsdPhysicsSnapshot.Empty"/> until the first successful
    /// <see cref="Step"/> or <see cref="SeekAsync"/> call. Reading it never blocks on stepping.
    /// </remarks>
    public UsdPhysicsSnapshot LatestSnapshot => Volatile.Read(ref _published).Snapshot;

    /// <summary>
    /// Builds a new session by extracting a physics-relevant build snapshot from <paramref name="scheduler"/>.
    /// </summary>
    /// <param name="scheduler">
    /// The stage-owner scheduler used for retained extraction and, later, for <see cref="BakeAsync"/>
    /// transactions. Only the scheduler's owner thread ever accesses the underlying stage.
    /// </param>
    /// <param name="options">
    /// Bounded capacities and requested capabilities. Defaults to
    /// <see cref="UsdPhysicsSessionOptions.Default"/>.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the build before it completes.</param>
    public static Task<UsdPhysicsSession> BuildAsync(
        UsdStageScheduler scheduler,
        UsdPhysicsSessionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        return BuildCoreAsync(scheduler, options, backend: null, cancellationToken);
    }

    internal static Task<UsdPhysicsSession> BuildForTestingAsync(
        UsdStageScheduler? scheduler,
        UsdPhysicsSessionOptions? options,
        IUsdPhysicsBackend backend,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(backend);
        return BuildCoreAsync(scheduler, options, backend, cancellationToken);
    }

    private static async Task<UsdPhysicsSession> BuildCoreAsync(
        UsdStageScheduler? scheduler,
        UsdPhysicsSessionOptions? options,
        IUsdPhysicsBackend? backend,
        CancellationToken cancellationToken)
    {
        options ??= UsdPhysicsSessionOptions.Default;
        backend ??= UsdPhysicsBackendFactory.CreateDefault();
        cancellationToken.ThrowIfCancellationRequested();

        UsdPhysicsBuildOutcome outcome = await backend
            .BuildAsync(scheduler, options, cancellationToken)
            .ConfigureAwait(false);
        return new UsdPhysicsSession(scheduler, options, backend, outcome);
    }

    /// <summary>
    /// Rebuilds the simulated world from the current authored stage content, discarding all
    /// simulation state accumulated since the last build or reset.
    /// </summary>
    /// <remarks>
    /// Reset is required after a physics-relevant edit invalidates the session (<see cref="State"/>
    /// becomes <see cref="UsdPhysicsSessionState.Invalidated"/>) and may also be called proactively.
    /// All checkpoints are invalidated by a reset. Reset shares the session's exclusive world gate
    /// with <see cref="SeekAsync"/>, <see cref="BakeAsync"/>, <see cref="Step"/>, and
    /// <see cref="DisposeAsync"/>: concurrent calls to any of those queue deterministically behind
    /// one another rather than racing, and reset is rejected while a <see cref="UsdPhysicsStepOwnership"/>
    /// is active, since an owner could call <see cref="Step"/> on its own thread at any moment.
    /// </remarks>
    /// <exception cref="UsdPhysicsStepOwnershipException">
    /// A <see cref="UsdPhysicsStepOwnership"/> is currently active for this session.
    /// </exception>
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _worldGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ThrowIfStepOwnershipActive("UsdPhysicsSession.ResetAsync");
            BeginStateTransition();
            try
            {
                UsdPhysicsBuildOutcome outcome = await _backend
                    .ResetAsync(_scheduler, Options, cancellationToken)
                    .ConfigureAwait(false);
                Volatile.Write(
                    ref _published,
                    new PublishedState(outcome.Capabilities, outcome.Diagnostics, UsdPhysicsSnapshot.Empty));
                Volatile.Write(ref _state, (int)UsdPhysicsSessionState.Ready);
            }
            catch
            {
                Volatile.Write(ref _state, (int)UsdPhysicsSessionState.Invalidated);
                throw;
            }
        }
        finally
        {
            _worldGate.Release();
        }
    }

    /// <summary>
    /// Seeks the simulated world to <paramref name="timeCode"/> without advancing the fixed
    /// simulation frequency's normal stepping.
    /// </summary>
    /// <remarks>
    /// An exact cached frame or checkpoint is used when available; otherwise the world replays from
    /// the authored start time code. CUDA-domain results after a seek are approximate because GPU
    /// solvers are not deterministic, and that is reported as a diagnostic rather than silently
    /// promising bitwise-equivalent results. Seek shares the session's exclusive world gate with
    /// <see cref="ResetAsync"/>, <see cref="BakeAsync"/>, <see cref="Step"/>, and
    /// <see cref="DisposeAsync"/>, and is rejected while a <see cref="UsdPhysicsStepOwnership"/> is
    /// active.
    /// </remarks>
    /// <exception cref="UsdPhysicsStepOwnershipException">
    /// A <see cref="UsdPhysicsStepOwnership"/> is currently active for this session.
    /// </exception>
    public async Task SeekAsync(double timeCode, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(timeCode))
        {
            throw new ArgumentOutOfRangeException(nameof(timeCode), "The time code must be finite.");
        }
        ThrowIfDisposed();

        await _worldGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ThrowIfStepOwnershipActive("UsdPhysicsSession.SeekAsync");
            ThrowIfNotReady();

            UsdPhysicsSnapshot snapshot = await _backend
                .SeekAsync(timeCode, cancellationToken)
                .ConfigureAwait(false);
            PublishSnapshot(snapshot);
        }
        finally
        {
            _worldGate.Release();
        }
    }

    /// <summary>
    /// Acquires the exclusive right to call <see cref="Step"/> for this session from the calling
    /// managed thread.
    /// </summary>
    /// <remarks>
    /// The returned token permanently binds to <see cref="Environment.CurrentManagedThreadId"/> as
    /// observed at the moment of this call; only that exact thread may subsequently call
    /// <see cref="Step"/> with the token. While the token is active, <see cref="ResetAsync"/>,
    /// <see cref="SeekAsync"/>, <see cref="BakeAsync"/>, and <see cref="DisposeAsync"/> are all
    /// rejected: dispose the token first to release stepping rights and allow those operations to
    /// proceed. The token's own <see cref="UsdPhysicsStepOwnership.Dispose"/> may be called from any
    /// thread.
    /// </remarks>
    /// <exception cref="UsdPhysicsStepOwnershipException">Another owner is already active.</exception>
    public UsdPhysicsStepOwnership AcquireStepOwnership()
    {
        ThrowIfDisposed();
        var owner = new UsdPhysicsStepOwnership(this);
        if (Interlocked.CompareExchange(ref _activeOwner, owner, null) is not null)
        {
            throw new UsdPhysicsStepOwnershipException();
        }
        return owner;
    }

    internal void ReleaseStepOwnership(UsdPhysicsStepOwnership owner) =>
        Interlocked.CompareExchange(ref _activeOwner, null, owner);

    /// <summary>
    /// Synchronously advances the simulated world by <paramref name="request"/> and publishes a new
    /// <see cref="LatestSnapshot"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Step is bounded and batched: it accepts every command and scene query for the tick in one
    /// call and returns one complete result. After warmup a supported backend performs no managed
    /// allocation here; the placeholder backend used before native simulation is implemented may
    /// still allocate.
    /// </para>
    /// <para>
    /// Step must be called from the exact managed thread that called <see cref="AcquireStepOwnership"/>
    /// for <paramref name="owner"/>; a call from any other thread is rejected even while
    /// <paramref name="owner"/> is otherwise still valid. Step never blocks waiting for a concurrent
    /// <see cref="ResetAsync"/>, <see cref="SeekAsync"/>, <see cref="BakeAsync"/>, or
    /// <see cref="DisposeAsync"/> call to finish; it instead fails fast with
    /// <see cref="UsdPhysicsStepOwnershipException"/> so the fixed simulation tick is never delayed
    /// by an unrelated async operation.
    /// </para>
    /// <para>
    /// <paramref name="cancellationToken"/> is only observed before the backend is entered: once the
    /// synchronous native step call begins, it always runs to completion and is not interrupted by
    /// cancellation. Pass a token to avoid starting a step that is already known to be unnecessary,
    /// not to abort one that already started.
    /// </para>
    /// </remarks>
    /// <exception cref="UsdPhysicsStepOwnershipException">
    /// <paramref name="owner"/> is not the exclusive owner currently registered for this session, the
    /// call did not arrive on the exact thread that acquired <paramref name="owner"/>, or the world
    /// gate is currently held by another <see cref="Step"/> call or lifecycle operation.
    /// </exception>
    /// <exception cref="UsdPhysicsSessionStateException">
    /// The session is not <see cref="UsdPhysicsSessionState.Ready"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled before the backend was entered.
    /// </exception>
    public UsdPhysicsStepResult Step(
        UsdPhysicsStepOwnership owner,
        UsdPhysicsStepRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        if (!owner.Owns(this) ||
            !ReferenceEquals(Volatile.Read(ref _activeOwner), owner) ||
            owner.OwnerThreadId != Environment.CurrentManagedThreadId)
        {
            throw new UsdPhysicsStepOwnershipException();
        }

        if (!_worldGate.Wait(0, CancellationToken.None))
        {
            throw new UsdPhysicsStepOwnershipException(
                "UsdPhysicsSession.Step could not acquire the exclusive world gate because another " +
                "Step call or lifecycle operation (Reset, Seek, Bake, or Dispose) is already in progress.");
        }
        try
        {
            ThrowIfDisposed();
            ThrowIfNotReady();
            cancellationToken.ThrowIfCancellationRequested();

            // From here on the step is bounded and non-interruptible: the backend call always runs
            // to completion, and cancellationToken is not consulted again.
            UsdPhysicsStepResult result = _backend.Step(request);
            PublishSnapshot(result.Snapshot);
            return result;
        }
        finally
        {
            _worldGate.Release();
        }
    }

    /// <summary>
    /// Bakes a selected authored range into a file-backed animation layer.
    /// </summary>
    /// <remarks>
    /// Simulation results never modify a persistent layer until this method is called. A bake never
    /// flattens hierarchy or replaces unrelated xform ops, and reports
    /// <see cref="UsdPhysicsBakeStatus.NotSupported"/> rather than writing fabricated data when no
    /// backend can produce simulated results. Bake shares the session's exclusive world gate with
    /// <see cref="ResetAsync"/>, <see cref="SeekAsync"/>, <see cref="Step"/>, and
    /// <see cref="DisposeAsync"/>, and is rejected while a <see cref="UsdPhysicsStepOwnership"/> is
    /// active.
    /// </remarks>
    /// <exception cref="UsdPhysicsStepOwnershipException">
    /// A <see cref="UsdPhysicsStepOwnership"/> is currently active for this session.
    /// </exception>
    public async Task<UsdPhysicsBakeResult> BakeAsync(
        UsdPhysicsBakeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();

        await _worldGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ThrowIfStepOwnershipActive("UsdPhysicsSession.BakeAsync");
            ThrowIfNotReady();

            UsdPhysicsBakeResult result = await _backend
                .BakeAsync(_scheduler, request, cancellationToken)
                .ConfigureAwait(false);
            PublishDiagnostics(result.Diagnostics);
            return result;
        }
        finally
        {
            _worldGate.Release();
        }
    }

    /// <summary>
    /// Disposes the session and its backend, releasing every retained native simulation object.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Disposal shares the session's exclusive world gate with <see cref="ResetAsync"/>,
    /// <see cref="SeekAsync"/>, <see cref="BakeAsync"/>, and <see cref="Step"/>: a concurrent dispose
    /// call queues deterministically behind an in-flight lifecycle operation rather than racing with
    /// it, and never observes a partially transitioned state.
    /// </para>
    /// <para>
    /// Dispose is rejected while a <see cref="UsdPhysicsStepOwnership"/> is active; dispose that
    /// token first. Dispose never blocks waiting for the token to be released on its own, since the
    /// token may legitimately be held for the session's entire lifetime by a transport loop.
    /// </para>
    /// <para>
    /// Dispose is idempotent: calling it again after it has already completed successfully is a
    /// no-op, and concurrent calls that all observe the session as not-yet-disposed queue behind one
    /// another, with exactly one performing the underlying backend disposal.
    /// </para>
    /// <para>
    /// The world gate itself is deliberately never disposed: it is retained for the lifetime of the
    /// <see cref="UsdPhysicsSession"/> object. A <see cref="SemaphoreSlim"/> only owns an unmanaged
    /// OS wait handle if <c>AvailableWaitHandle</c> is accessed, which this type never does, so there
    /// is no resource to release, and disposing the gate would otherwise create a race: any caller
    /// that passed the disposed-state precheck and is already queued in <c>WaitAsync</c>/<c>Wait</c>
    /// for the gate — or about to call <c>Release</c> after acquiring it — could observe an
    /// <see cref="ObjectDisposedException"/> thrown by the semaphore instead of the session's own,
    /// intentional <see cref="ObjectDisposedException"/> from <c>ThrowIfDisposed</c>. Keeping the gate
    /// alive guarantees every rejection concurrent callers observe after disposal is reported through
    /// this session's own state check, never through the underlying synchronization primitive.
    /// </para>
    /// </remarks>
    /// <exception cref="UsdPhysicsStepOwnershipException">
    /// A <see cref="UsdPhysicsStepOwnership"/> is currently active for this session.
    /// </exception>
    public async ValueTask DisposeAsync()
    {
        if (State == UsdPhysicsSessionState.Disposed)
        {
            return;
        }

        await _worldGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (State == UsdPhysicsSessionState.Disposed)
            {
                return;
            }
            ThrowIfStepOwnershipActive("UsdPhysicsSession.DisposeAsync");

            bool disposedNow = Interlocked.Exchange(ref _state, (int)UsdPhysicsSessionState.Disposed) !=
                (int)UsdPhysicsSessionState.Disposed;
            if (disposedNow)
            {
                await _backend.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            // The world gate is intentionally never disposed here; see the disposal-semantics
            // remarks above. Every queued or future caller still finds a valid, usable gate and is
            // rejected solely through this session's own disposed-state check.
            _worldGate.Release();
        }
    }

    private void PublishSnapshot(UsdPhysicsSnapshot snapshot)
    {
        PublishedState current = Volatile.Read(ref _published);
        Volatile.Write(ref _published, new PublishedState(current.Capabilities, current.Diagnostics, snapshot));
    }

    private void PublishDiagnostics(UsdPhysicsDiagnostics diagnostics)
    {
        PublishedState current = Volatile.Read(ref _published);
        Volatile.Write(ref _published, new PublishedState(current.Capabilities, diagnostics, current.Snapshot));
    }

    private void BeginStateTransition()
    {
        int previous = Interlocked.CompareExchange(
            ref _state,
            (int)UsdPhysicsSessionState.Building,
            (int)UsdPhysicsSessionState.Ready);
        if (previous == (int)UsdPhysicsSessionState.Ready)
        {
            return;
        }

        previous = Interlocked.CompareExchange(
            ref _state,
            (int)UsdPhysicsSessionState.Building,
            (int)UsdPhysicsSessionState.Invalidated);
        if (previous != (int)UsdPhysicsSessionState.Invalidated)
        {
            throw new UsdPhysicsSessionStateException((UsdPhysicsSessionState)previous);
        }
    }

    private void ThrowIfNotReady()
    {
        UsdPhysicsSessionState state = State;
        if (state != UsdPhysicsSessionState.Ready)
        {
            throw new UsdPhysicsSessionStateException(state);
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(State == UsdPhysicsSessionState.Disposed, this);

    private void ThrowIfStepOwnershipActive(string operationName)
    {
        if (Volatile.Read(ref _activeOwner) is not null)
        {
            throw new UsdPhysicsStepOwnershipException(
                $"{operationName} was rejected because a UsdPhysicsStepOwnership is currently active " +
                "for this session. Dispose the active ownership token before calling this operation.");
        }
    }

    /// <summary>
    /// An immutable snapshot of every value <see cref="Capabilities"/>, <see cref="Diagnostics"/>,
    /// and <see cref="LatestSnapshot"/> expose, swapped in one atomic reference write so a reader
    /// never observes a mix of values from two different operations.
    /// </summary>
    private sealed class PublishedState
    {
        internal PublishedState(
            UsdPhysicsCapabilities capabilities,
            UsdPhysicsDiagnostics diagnostics,
            UsdPhysicsSnapshot snapshot)
        {
            Capabilities = capabilities;
            Diagnostics = diagnostics;
            Snapshot = snapshot;
        }

        internal UsdPhysicsCapabilities Capabilities { get; }

        internal UsdPhysicsDiagnostics Diagnostics { get; }

        internal UsdPhysicsSnapshot Snapshot { get; }
    }
}
