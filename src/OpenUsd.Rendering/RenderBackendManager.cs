// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Immutable;

namespace OpenUsd.Rendering;

/// <summary>
/// Creates one renderer backend instance for probing and initialization.
/// </summary>
public interface IRenderBackendFactory
{
    /// <summary>Gets the backend kind created by the factory.</summary>
    RenderBackendKind Kind { get; }

    /// <summary>Creates a new uninitialized backend instance.</summary>
    IRenderBackend Create();
}

/// <summary>
/// Identifies a manager-level lifecycle failure.
/// </summary>
public enum RenderBackendManagerFailureKind
{
    /// <summary>The operation succeeded.</summary>
    None,

    /// <summary>No state has been supplied or no backend is active.</summary>
    NotInitialized,

    /// <summary>The selection request could not produce a supported candidate.</summary>
    SelectionFailed,

    /// <summary>Every eligible backend was unavailable or failed initialization.</summary>
    NoBackendAvailable,

    /// <summary>The requested backend kind is retained pending successful cleanup.</summary>
    CleanupPending,

    /// <summary>The active backend reported a frame failure.</summary>
    BackendOperationFailed
}

/// <summary>
/// Reports activation, switching, state-update, or resize behavior.
/// </summary>
public sealed record RenderBackendManagerResult
{
    internal RenderBackendManagerResult(
        bool isSuccess,
        RenderBackendManagerFailureKind failure,
        RenderBackendIdentity? activeBackend,
        RenderBackendDiagnostics diagnostics)
    {
        IsSuccess = isSuccess;
        Failure = failure;
        ActiveBackend = activeBackend;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Gets the manager-level failure category.</summary>
    public RenderBackendManagerFailureKind Failure { get; }

    /// <summary>Gets the active backend after the operation.</summary>
    public RenderBackendIdentity? ActiveBackend { get; }

    /// <summary>Gets aggregated diagnostics and fallback reasons.</summary>
    public RenderBackendDiagnostics Diagnostics { get; }
}

/// <summary>
/// Reports a managed render request, including any device-loss fallback.
/// </summary>
public sealed record ManagedRenderFrameResult
{
    internal ManagedRenderFrameResult(
        bool isSuccess,
        RenderBackendManagerFailureKind failure,
        RenderBackendIdentity? activeBackend,
        RenderFrameResult? frame,
        bool didFailOver,
        RenderBackendDiagnostics diagnostics)
    {
        IsSuccess = isSuccess;
        Failure = failure;
        ActiveBackend = activeBackend;
        Frame = frame;
        DidFailOver = didFailOver;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets a value indicating whether a frame was rendered or skipped successfully.</summary>
    public bool IsSuccess { get; }

    /// <summary>Gets the manager-level failure category.</summary>
    public RenderBackendManagerFailureKind Failure { get; }

    /// <summary>Gets the backend active after rendering and fallback.</summary>
    public RenderBackendIdentity? ActiveBackend { get; }

    /// <summary>Gets the final backend frame result when available.</summary>
    public RenderFrameResult? Frame { get; }

    /// <summary>Gets a value indicating whether device loss activated another backend.</summary>
    public bool DidFailOver { get; }

    /// <summary>Gets aggregated frame, device-loss, and fallback diagnostics.</summary>
    public RenderBackendDiagnostics Diagnostics { get; }
}

/// <summary>
/// Serializes renderer-neutral backend lifecycle operations and failover.
/// </summary>
public sealed class RenderBackendManager : IAsyncDisposable
{
    private readonly object _disposeGate = new();
    private readonly ImmutableDictionary<RenderBackendKind, IRenderBackendFactory> _factories;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly RenderPlatform _platform;
    private readonly List<RetiredBackend> _retiredBackends = [];
    private readonly int[] _candidateSelectionCounts =
        new int[Enum.GetValues<RenderBackendKind>().Max(kind => (int)kind) + 1];
    private readonly int[] _factoryCreationCounts =
        new int[Enum.GetValues<RenderBackendKind>().Max(kind => (int)kind) + 1];
    private RenderBackendIdentity? _activeIdentity;
    private IRenderBackend? _active;
    private Task? _disposeTask;
    private StageRenderState? _currentState;
    private RenderBackendManagerFailureKind _lastActivationFailure =
        RenderBackendManagerFailureKind.NotInitialized;
    private int _disposeState;
    private int _retiredCleanupCount;

    private const int MaxCleanupDiagnosticsPerOperation = 8;

    /// <summary>Initializes a manager over injected backend factories.</summary>
    public RenderBackendManager(
        RenderPlatform platform,
        IEnumerable<IRenderBackendFactory> factories)
    {
        ArgumentNullException.ThrowIfNull(factories);
        _platform = platform;

        var builder = ImmutableDictionary.CreateBuilder<RenderBackendKind, IRenderBackendFactory>();
        foreach (IRenderBackendFactory factory in factories)
        {
            ArgumentNullException.ThrowIfNull(factory);
            if (!builder.TryAdd(factory.Kind, factory))
            {
                throw new ArgumentException(
                    $"A factory for {factory.Kind} was supplied more than once.",
                    nameof(factories));
            }
        }
        _factories = builder.ToImmutable();
    }

    /// <summary>Gets the exact current state reference.</summary>
    public StageRenderState? CurrentState => Volatile.Read(ref _currentState);

    /// <summary>Gets the active backend identity.</summary>
    public RenderBackendIdentity? ActiveBackend => Volatile.Read(ref _activeIdentity);

    /// <summary>Gets the number of backend owners retained for cleanup retry.</summary>
    public int RetiredCleanupCount => Volatile.Read(ref _retiredCleanupCount);

    /// <summary>Gets how often a backend kind reached candidate consideration.</summary>
    public int GetCandidateSelectionCount(RenderBackendKind kind) =>
        Volatile.Read(ref _candidateSelectionCounts[GetKindIndex(kind)]);

    /// <summary>Gets how many backend instances a factory created for one kind.</summary>
    public int GetFactoryCreationCount(RenderBackendKind kind) =>
        Volatile.Read(ref _factoryCreationCounts[GetKindIndex(kind)]);

    /// <summary>
    /// Probes and initializes the first successful automatic or manual candidate.
    /// </summary>
    public async ValueTask<RenderBackendManagerResult> InitializeAsync(
        StageRenderState initialState,
        RenderBackendKind? requestedBackend = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(initialState);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_active is not null)
            {
                throw new InvalidOperationException("The render backend manager is already initialized.");
            }

            _currentState = initialState;
            var diagnostics = new List<RenderBackendDiagnostic>();
            RenderBackendManagerFailureKind failure = await ActivateCoreAsync(
                requestedBackend,
                diagnostics,
                [],
                cancellationToken).ConfigureAwait(false);
            if (_active is null)
            {
                _lastActivationFailure = failure;
            }
            return CreateResult(failure, diagnostics);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Switches to the automatic preferred backend or one explicit backend.
    /// </summary>
    public async ValueTask<RenderBackendManagerResult> SwitchAsync(
        RenderBackendKind? requestedBackend = null,
        CancellationToken cancellationToken = default)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var diagnostics = new List<RenderBackendDiagnostic>();
            await RetryRetiredCleanupAsync(diagnostics).ConfigureAwait(false);
            if (_currentState is null)
            {
                AddDiagnostic(
                    diagnostics,
                    backend: null,
                    RenderDiagnosticSeverity.Error,
                    RenderBackendDiagnosticCategory.Selection,
                    "manager.not_initialized",
                    "No stage state is available for backend initialization.");
                return CreateResult(RenderBackendManagerFailureKind.NotInitialized, diagnostics);
            }

            RenderBackendManagerFailureKind failure = await ActivateCoreAsync(
                requestedBackend,
                diagnostics,
                [],
                cancellationToken).ConfigureAwait(false);
            if (_active is null)
            {
                _lastActivationFailure = failure;
            }
            return CreateResult(failure, diagnostics);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Stores and forwards the exact immutable state reference.</summary>
    public async ValueTask<RenderBackendManagerResult> UpdateStateAsync(
        StageRenderState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var diagnostics = new List<RenderBackendDiagnostic>();
            await RetryRetiredCleanupAsync(diagnostics).ConfigureAwait(false);
            if (_active is null)
            {
                _currentState = state;
                return InactiveResult(
                    "No backend is active to receive stage state.",
                    diagnostics);
            }

            _currentState = state;
            await _active.UpdateStateAsync(state, cancellationToken).ConfigureAwait(false);
            return CreateResult(RenderBackendManagerFailureKind.None, diagnostics);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forwards viewport dimensions to the active backend.</summary>
    public async ValueTask<RenderBackendManagerResult> ResizeAsync(
        ViewportDimensions viewport,
        CancellationToken cancellationToken = default)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var diagnostics = new List<RenderBackendDiagnostic>();
            await RetryRetiredCleanupAsync(diagnostics).ConfigureAwait(false);
            if (_active is null)
            {
                return InactiveResult("No backend is active to resize.", diagnostics);
            }

            await _active.ResizeAsync(viewport, cancellationToken).ConfigureAwait(false);
            return CreateResult(RenderBackendManagerFailureKind.None, diagnostics);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Renders a frame and automatically fails over after a clean device-loss report.
    /// </summary>
    public async ValueTask<ManagedRenderFrameResult> RenderAsync(
        CancellationToken cancellationToken = default)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var diagnostics = new List<RenderBackendDiagnostic>();
            await RetryRetiredCleanupAsync(diagnostics).ConfigureAwait(false);
            HashSet<RenderBackendKind> renderChainExclusions = [];
            bool didFailOver = false;

            while (_active is { } backend)
            {
                RenderFrameResult frame = await backend.RenderAsync(cancellationToken)
                    .ConfigureAwait(false);
                AddDiagnostics(diagnostics, frame.Diagnostics);

                if (frame.Status != RenderFrameStatus.DeviceLost)
                {
                    bool success = frame.Status is
                        RenderFrameStatus.Rendered or RenderFrameStatus.Skipped;
                    return new ManagedRenderFrameResult(
                        success,
                        success
                            ? RenderBackendManagerFailureKind.None
                            : RenderBackendManagerFailureKind.BackendOperationFailed,
                        _activeIdentity,
                        frame,
                        didFailOver,
                        new RenderBackendDiagnostics(diagnostics));
                }

                didFailOver = true;
                RenderBackendKind lostKind = backend.Identity.Kind;
                AddDiagnostic(
                    diagnostics,
                    lostKind,
                    RenderDiagnosticSeverity.Error,
                    RenderBackendDiagnosticCategory.DeviceLoss,
                    "manager.device_lost",
                    $"{backend.Identity.Name} reported {frame.DeviceLoss}.");
                renderChainExclusions.Add(lostKind);
                try
                {
                    await DeactivateAsync(backend, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    AddExceptionDiagnostic(
                        diagnostics,
                        lostKind,
                        RenderBackendDiagnosticCategory.DeviceLoss,
                        "manager.device_lost_deactivation_failed",
                        "Device-lost backend deactivation failed; fallback was aborted.",
                        exception);
                    return new ManagedRenderFrameResult(
                        isSuccess: false,
                        RenderBackendManagerFailureKind.BackendOperationFailed,
                        _activeIdentity,
                        frame,
                        didFailOver,
                        new RenderBackendDiagnostics(diagnostics));
                }
                _active = null;
                _activeIdentity = null;
                _lastActivationFailure = RenderBackendManagerFailureKind.NotInitialized;
                bool disposed = await DisposeWithDiagnosticAsync(
                    backend,
                    lostKind,
                    diagnostics,
                    RenderBackendDiagnosticCategory.DeviceLoss,
                    "manager.device_lost_cleanup_failed",
                    "Device-lost backend cleanup failed.").ConfigureAwait(false);
                if (!disposed)
                {
                    RetireBackend(backend, lostKind);
                }

                RenderBackendManagerFailureKind failure = await ActivateCoreAsync(
                    requestedBackend: null,
                    diagnostics,
                    renderChainExclusions,
                    cancellationToken).ConfigureAwait(false);
                if (failure != RenderBackendManagerFailureKind.None)
                {
                    _lastActivationFailure = failure;
                    return new ManagedRenderFrameResult(
                        isSuccess: false,
                        RenderBackendManagerFailureKind.NoBackendAvailable,
                        activeBackend: null,
                        frame,
                        didFailOver,
                        new RenderBackendDiagnostics(diagnostics));
                }
            }

            AddDiagnostic(
                diagnostics,
                backend: null,
                RenderDiagnosticSeverity.Error,
                RenderBackendDiagnosticCategory.Rendering,
                "manager.no_backend",
                "No backend is active to render a frame.");
            return new ManagedRenderFrameResult(
                isSuccess: false,
                InactiveFailure,
                activeBackend: null,
                frame: null,
                didFailOver,
                new RenderBackendDiagnostics(diagnostics));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            if (Volatile.Read(ref _disposeState) == 2)
            {
                return ValueTask.CompletedTask;
            }
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        await Task.Yield();
        Volatile.Write(ref _disposeState, 1);
        await _gate.WaitAsync().ConfigureAwait(false);
        bool completed = false;
        try
        {
            var failures = new List<Exception>();
            await RetryRetiredCleanupForDisposeAsync(failures).ConfigureAwait(false);

            if (_active is { } active)
            {
                try
                {
                    await DeactivateAsync(active, CancellationToken.None).ConfigureAwait(false);
                    _active = null;
                    _activeIdentity = null;
                    try
                    {
                        await active.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        RetireBackend(active, active.Identity.Kind);
                        failures.Add(exception);
                    }
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            if (_retiredBackends.Count != 0 || _active is not null)
            {
                throw new AggregateException(
                    "One or more render backends remain owned for cleanup retry.",
                    failures);
            }
            Volatile.Write(ref _disposeState, 2);
            completed = true;
        }
        finally
        {
            _gate.Release();
            if (completed)
            {
                _gate.Dispose();
            }
            else
            {
                lock (_disposeGate)
                {
                    _disposeTask = null;
                }
            }
        }
    }

    private async ValueTask EnterAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposeState) != 0,
            this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (Volatile.Read(ref _disposeState) != 0)
        {
            _gate.Release();
            throw new ObjectDisposedException(nameof(RenderBackendManager));
        }
    }

    private async ValueTask<RenderBackendManagerFailureKind> ActivateCoreAsync(
        RenderBackendKind? requestedBackend,
        List<RenderBackendDiagnostic> diagnostics,
        HashSet<RenderBackendKind> exclusions,
        CancellationToken cancellationToken)
    {
        HashSet<RenderBackendKind> quarantinedKinds = GetQuarantinedKinds();
        if (requestedBackend is { } requested && quarantinedKinds.Contains(requested))
        {
            AddDiagnostic(
                diagnostics,
                requested,
                RenderDiagnosticSeverity.Error,
                RenderBackendDiagnosticCategory.Selection,
                "manager.backend_cleanup_pending",
                $"{requested} remains unavailable until all retained owners of that kind " +
                "complete cleanup.");
            return RenderBackendManagerFailureKind.CleanupPending;
        }
        if (!requestedBackend.HasValue)
        {
            foreach (RenderBackendKind quarantined in quarantinedKinds)
            {
                AddDiagnostic(
                    diagnostics,
                    quarantined,
                    RenderDiagnosticSeverity.Warning,
                    RenderBackendDiagnosticCategory.Selection,
                    "manager.backend_cleanup_pending",
                    $"Automatic selection skipped {quarantined} because retained cleanup is pending.");
            }
        }
        var selectionExclusions = new HashSet<RenderBackendKind>(exclusions);
        selectionExclusions.UnionWith(quarantinedKinds);
        RenderBackendSelectionResult selection = RenderBackendSelectionPolicy.Select(
            new RenderBackendSelectionRequest(_platform, requestedBackend),
            _factories.Keys,
            requestedBackend.HasValue ? null : selectionExclusions);
        if (!selection.IsSuccess)
        {
            AddDiagnostic(
                diagnostics,
                requestedBackend,
                RenderDiagnosticSeverity.Error,
                RenderBackendDiagnosticCategory.Selection,
                "manager.selection_failed",
                $"Backend selection failed: {selection.Failure}.");
            return selection.Failure is
                RenderBackendSelectionFailureKind.NoBackendAvailable
                or RenderBackendSelectionFailureKind.RequestedBackendUnavailable
                    ? RenderBackendManagerFailureKind.NoBackendAvailable
                    : RenderBackendManagerFailureKind.SelectionFailed;
        }

        foreach (RenderBackendKind candidateKind in selection.Candidates)
        {
            Interlocked.Increment(ref _candidateSelectionCounts[GetKindIndex(candidateKind)]);
            if (_active is { } current && current.Identity.Kind == candidateKind)
            {
                return RenderBackendManagerFailureKind.None;
            }

            IRenderBackend? candidate;
            try
            {
                Interlocked.Increment(ref _factoryCreationCounts[GetKindIndex(candidateKind)]);
                candidate = _factories[candidateKind].Create();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                exclusions.Add(candidateKind);
                AddExceptionDiagnostic(
                    diagnostics,
                    candidateKind,
                    RenderBackendDiagnosticCategory.Initialization,
                    "manager.factory_exception",
                    "Backend factory creation failed.",
                    exception,
                    initializationFailure: RenderBackendInitializationFailureKind.Unknown);
                AddFallbackDiagnostic(
                    diagnostics,
                    candidateKind,
                    "Factory creation threw an exception.");
                continue;
            }

            if (candidate is null)
            {
                exclusions.Add(candidateKind);
                diagnostics.Add(new RenderBackendDiagnostic(
                    candidateKind,
                    RenderDiagnosticSeverity.Error,
                    RenderBackendDiagnosticCategory.Initialization,
                    "manager.factory_returned_null",
                    "Backend factory returned null.",
                    probeFailure: null,
                    initializationFailure: RenderBackendInitializationFailureKind.Unknown,
                    exceptionType: null,
                    exceptionMessage: null));
                AddFallbackDiagnostic(
                    diagnostics,
                    candidateKind,
                    "Factory creation returned null.");
                continue;
            }

            bool adopted = false;
            try
            {
                if (candidate.Identity.Kind != candidateKind)
                {
                    exclusions.Add(candidateKind);
                    AddDiagnostic(
                        diagnostics,
                        candidateKind,
                        RenderDiagnosticSeverity.Error,
                        RenderBackendDiagnosticCategory.Initialization,
                        "manager.factory_kind_mismatch",
                        $"The {candidateKind} factory created a {candidate.Identity.Kind} backend.");
                    AddFallbackDiagnostic(
                        diagnostics,
                        candidateKind,
                        "Factory created the wrong backend kind.");
                    continue;
                }

                RenderBackendProbeResult probe;
                try
                {
                    probe = await candidate.ProbeAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    exclusions.Add(candidateKind);
                    AddExceptionDiagnostic(
                        diagnostics,
                        candidateKind,
                        RenderBackendDiagnosticCategory.Probe,
                        "manager.probe_exception",
                        "Backend probing failed.",
                        exception,
                        probeFailure: RenderBackendProbeFailureKind.Unknown);
                    AddFallbackDiagnostic(
                        diagnostics,
                        candidateKind,
                        "Probe threw an exception.");
                    continue;
                }

                AddDiagnostics(diagnostics, probe.Diagnostics);
                if (!probe.IsAvailable)
                {
                    exclusions.Add(candidateKind);
                    AddFallbackDiagnostic(
                        diagnostics,
                        candidateKind,
                        $"Probe failed: {probe.Failure}.");
                    continue;
                }

                RenderBackendInitializationResult initialization;
                try
                {
                    initialization = await candidate
                        .InitializeAsync(_currentState!, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    exclusions.Add(candidateKind);
                    AddExceptionDiagnostic(
                        diagnostics,
                        candidateKind,
                        RenderBackendDiagnosticCategory.Initialization,
                        "manager.initialization_exception",
                        "Backend initialization failed.",
                        exception,
                        initializationFailure: RenderBackendInitializationFailureKind.Unknown);
                    AddFallbackDiagnostic(
                        diagnostics,
                        candidateKind,
                        "Initialization threw an exception.");
                    continue;
                }

                AddDiagnostics(diagnostics, initialization.Diagnostics);
                if (!initialization.IsSuccess)
                {
                    exclusions.Add(candidateKind);
                    AddFallbackDiagnostic(
                        diagnostics,
                        candidateKind,
                        $"Initialization failed: {initialization.Failure}.");
                    continue;
                }

                var transaction = new ActivationTransaction(
                    candidate,
                    candidateKind,
                    _active,
                    _activeIdentity?.Kind);
                if (transaction.Previous is not null)
                {
                    try
                    {
                        await DeactivateAsync(transaction.Previous, cancellationToken)
                            .ConfigureAwait(false);
                        transaction.MarkPreviousDeactivated();
                        ClearActiveBackend();
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        AddExceptionDiagnostic(
                            diagnostics,
                            transaction.PreviousKind,
                            RenderBackendDiagnosticCategory.Cleanup,
                            "manager.previous_backend_deactivation_failed",
                            "Previous backend deactivation failed; the switch was aborted.",
                            exception);
                        return RenderBackendManagerFailureKind.BackendOperationFailed;
                    }
                }

                try
                {
                    transaction.MarkCandidateActivationStarted();
                    await ActivateAsync(candidate, cancellationToken).ConfigureAwait(false);
                    transaction.MarkCandidateActivated();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await RollbackActivationAsync(transaction, diagnostics).ConfigureAwait(false);
                    throw;
                }
                catch (Exception exception)
                {
                    AddExceptionDiagnostic(
                        diagnostics,
                        candidateKind,
                        RenderBackendDiagnosticCategory.Initialization,
                        "manager.candidate_activation_failed",
                        "Initialized backend activation failed.",
                        exception,
                        initializationFailure: RenderBackendInitializationFailureKind.Unknown);
                    await RollbackActivationAsync(transaction, diagnostics).ConfigureAwait(false);
                    return RenderBackendManagerFailureKind.BackendOperationFailed;
                }

                SetActiveBackend(candidate);
                _lastActivationFailure = RenderBackendManagerFailureKind.None;
                transaction.Commit();
                adopted = true;

                if (transaction.Previous is not null)
                {
                    bool disposed = await DisposeWithDiagnosticAsync(
                        transaction.Previous,
                        transaction.PreviousKind,
                        diagnostics,
                        RenderBackendDiagnosticCategory.Cleanup,
                        "manager.previous_backend_cleanup_failed",
                        "Previous backend cleanup failed after replacement.").ConfigureAwait(false);
                    if (!disposed)
                    {
                        RetireBackend(transaction.Previous, transaction.PreviousKind);
                    }
                }

                return RenderBackendManagerFailureKind.None;
            }
            finally
            {
                if (!adopted)
                {
                    bool disposed = await DisposeWithDiagnosticAsync(
                        candidate,
                        candidateKind,
                        diagnostics,
                        RenderBackendDiagnosticCategory.Cleanup,
                        "manager.candidate_cleanup_failed",
                        "Failed backend candidate cleanup failed.").ConfigureAwait(false);
                    if (!disposed)
                    {
                        RetireBackend(candidate, candidateKind);
                    }
                }
            }
        }

        AddDiagnostic(
            diagnostics,
            requestedBackend,
            RenderDiagnosticSeverity.Error,
            RenderBackendDiagnosticCategory.Selection,
            "manager.no_backend",
            "Every eligible backend was unavailable or failed initialization.");
        return RenderBackendManagerFailureKind.NoBackendAvailable;
    }

    private RenderBackendManagerFailureKind InactiveFailure =>
        _lastActivationFailure;

    private RenderBackendManagerResult InactiveResult(
        string message,
        IEnumerable<RenderBackendDiagnostic>? existingDiagnostics = null)
    {
        var diagnostics = existingDiagnostics?.ToList() ?? [];
        string code = InactiveFailure switch
        {
            RenderBackendManagerFailureKind.SelectionFailed => "manager.selection_failed",
            RenderBackendManagerFailureKind.NoBackendAvailable => "manager.no_backend",
            RenderBackendManagerFailureKind.CleanupPending => "manager.backend_cleanup_pending",
            RenderBackendManagerFailureKind.BackendOperationFailed =>
                "manager.backend_operation_failed",
            _ => "manager.not_initialized",
        };
        AddDiagnostic(
            diagnostics,
            backend: null,
            RenderDiagnosticSeverity.Error,
            RenderBackendDiagnosticCategory.General,
            code,
            message);
        return CreateResult(InactiveFailure, diagnostics);
    }

    private RenderBackendManagerResult CreateResult(
        RenderBackendManagerFailureKind failure,
        IEnumerable<RenderBackendDiagnostic> diagnostics) =>
        new(
            failure == RenderBackendManagerFailureKind.None,
            failure,
            _activeIdentity,
            new RenderBackendDiagnostics(diagnostics));

    private static void AddDiagnostics(
        List<RenderBackendDiagnostic> destination,
        RenderBackendDiagnostics diagnostics)
    {
        foreach (RenderBackendDiagnostic diagnostic in diagnostics.Entries)
        {
            destination.Add(diagnostic);
        }
    }

    private static void AddFallbackDiagnostic(
        List<RenderBackendDiagnostic> diagnostics,
        RenderBackendKind backend,
        string message) =>
        AddDiagnostic(
            diagnostics,
            backend,
            RenderDiagnosticSeverity.Warning,
            RenderBackendDiagnosticCategory.Fallback,
            "manager.fallback",
            $"{backend}: {message}");

    private static void AddExceptionDiagnostic(
        List<RenderBackendDiagnostic> diagnostics,
        RenderBackendKind? backend,
        RenderBackendDiagnosticCategory category,
        string code,
        string message,
        Exception exception,
        RenderBackendProbeFailureKind? probeFailure = null,
        RenderBackendInitializationFailureKind? initializationFailure = null)
    {
        string exceptionType = exception.GetType().FullName ?? exception.GetType().Name;
        diagnostics.Add(new RenderBackendDiagnostic(
            backend,
            RenderDiagnosticSeverity.Error,
            category,
            code,
            $"{message} {exceptionType}: {exception.Message}",
            probeFailure,
            initializationFailure,
            exceptionType,
            exception.Message));
    }

    private static async ValueTask<bool> DisposeWithDiagnosticAsync(
        IRenderBackend backend,
        RenderBackendKind? kind,
        List<RenderBackendDiagnostic> diagnostics,
        RenderBackendDiagnosticCategory category,
        string code,
        string message)
    {
        try
        {
            await backend.DisposeAsync().ConfigureAwait(false);
            return true;
        }
        catch (Exception exception)
        {
            AddExceptionDiagnostic(
                diagnostics,
                kind,
                category,
                code,
                message,
                exception);
            return false;
        }
    }

    private static ValueTask DeactivateAsync(
        IRenderBackend backend,
        CancellationToken cancellationToken) =>
        backend is IRenderBackendActivationControl activation
            ? activation.DeactivateAsync(cancellationToken)
            : ValueTask.CompletedTask;

    private static ValueTask ActivateAsync(
        IRenderBackend backend,
        CancellationToken cancellationToken) =>
        backend is IRenderBackendActivationControl activation
            ? activation.ActivateAsync(cancellationToken)
            : ValueTask.CompletedTask;

    private async ValueTask RollbackActivationAsync(
        ActivationTransaction transaction,
        List<RenderBackendDiagnostic> diagnostics)
    {
        transaction.BeginRollback();
        bool candidateDeactivated = false;
        try
        {
            await DeactivateAsync(transaction.Candidate, CancellationToken.None)
                .ConfigureAwait(false);
            candidateDeactivated = true;
        }
        catch (Exception exception)
        {
            AddExceptionDiagnostic(
                diagnostics,
                transaction.CandidateKind,
                RenderBackendDiagnosticCategory.Cleanup,
                "manager.candidate_rollback_deactivation_failed",
                "Failed candidate deactivation during switch rollback.",
                exception);
        }

        if (candidateDeactivated && transaction.Previous is not null)
        {
            try
            {
                await ActivateAsync(transaction.Previous, CancellationToken.None)
                    .ConfigureAwait(false);
                SetActiveBackend(transaction.Previous);
                _lastActivationFailure = RenderBackendManagerFailureKind.None;
                transaction.CompleteRollback();
                return;
            }
            catch (Exception exception)
            {
                AddExceptionDiagnostic(
                    diagnostics,
                    transaction.PreviousKind,
                    RenderBackendDiagnosticCategory.Cleanup,
                    "manager.previous_backend_reactivation_failed",
                    "Previous backend reactivation failed after an aborted switch.",
                    exception);
            }
        }
        else if (candidateDeactivated)
        {
            ClearActiveBackend();
            _lastActivationFailure = RenderBackendManagerFailureKind.BackendOperationFailed;
            transaction.CompleteRollback();
            return;
        }

        ClearActiveBackend();
        _lastActivationFailure = RenderBackendManagerFailureKind.BackendOperationFailed;
        if (transaction.Previous is not null)
        {
            bool disposed = await DisposeWithDiagnosticAsync(
                transaction.Previous,
                transaction.PreviousKind,
                diagnostics,
                RenderBackendDiagnosticCategory.Cleanup,
                "manager.previous_backend_rollback_cleanup_failed",
                "Previous backend cleanup failed after rollback could not restore it.")
                .ConfigureAwait(false);
            if (!disposed)
            {
                RetireBackend(transaction.Previous, transaction.PreviousKind);
            }
        }
        transaction.Abandon();
    }

    private void SetActiveBackend(IRenderBackend backend)
    {
        _active = backend;
        _activeIdentity = backend.Identity;
    }

    private void ClearActiveBackend()
    {
        _active = null;
        _activeIdentity = null;
    }

    private void RetireBackend(IRenderBackend backend, RenderBackendKind? kind)
    {
        if (_retiredBackends.Any(entry => ReferenceEquals(entry.Backend, backend)))
        {
            return;
        }
        _retiredBackends.Add(new RetiredBackend(backend, kind));
        Volatile.Write(ref _retiredCleanupCount, _retiredBackends.Count);
    }

    private HashSet<RenderBackendKind> GetQuarantinedKinds() =>
        _retiredBackends
            .Where(entry => entry.Kind.HasValue)
            .Select(entry => entry.Kind!.Value)
            .ToHashSet();

    private static int GetKindIndex(RenderBackendKind kind)
    {
        int index = (int)kind;
        if (!Enum.IsDefined(kind) || index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        return index;
    }

    private async ValueTask RetryRetiredCleanupAsync(
        List<RenderBackendDiagnostic> diagnostics)
    {
        int reported = 0;
        int suppressed = 0;
        for (int index = _retiredBackends.Count - 1; index >= 0; index--)
        {
            RetiredBackend retired = _retiredBackends[index];
            try
            {
                await retired.Backend.DisposeAsync().ConfigureAwait(false);
                _retiredBackends.RemoveAt(index);
                if (reported < MaxCleanupDiagnosticsPerOperation)
                {
                    AddDiagnostic(
                        diagnostics,
                        retired.Kind,
                        RenderDiagnosticSeverity.Information,
                        RenderBackendDiagnosticCategory.Cleanup,
                        "manager.retired_backend_cleanup_recovered",
                        "A retained backend owner was released on cleanup retry.");
                    reported++;
                }
                else
                {
                    suppressed++;
                }
            }
            catch (Exception exception)
            {
                if (reported < MaxCleanupDiagnosticsPerOperation)
                {
                    AddExceptionDiagnostic(
                        diagnostics,
                        retired.Kind,
                        RenderBackendDiagnosticCategory.Cleanup,
                        "manager.retired_backend_cleanup_failed",
                        "Retained backend cleanup retry failed.",
                        exception);
                    reported++;
                }
                else
                {
                    suppressed++;
                }
            }
        }
        Volatile.Write(ref _retiredCleanupCount, _retiredBackends.Count);
        if (suppressed != 0)
        {
            AddDiagnostic(
                diagnostics,
                backend: null,
                RenderDiagnosticSeverity.Warning,
                RenderBackendDiagnosticCategory.Cleanup,
                "manager.retired_backend_cleanup_diagnostics_suppressed",
                $"{suppressed} additional retained cleanup outcomes were suppressed.");
        }
    }

    private async ValueTask RetryRetiredCleanupForDisposeAsync(
        List<Exception> failures)
    {
        for (int index = _retiredBackends.Count - 1; index >= 0; index--)
        {
            RetiredBackend retired = _retiredBackends[index];
            try
            {
                await retired.Backend.DisposeAsync().ConfigureAwait(false);
                _retiredBackends.RemoveAt(index);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
        Volatile.Write(ref _retiredCleanupCount, _retiredBackends.Count);
    }

    private static void AddDiagnostic(
        List<RenderBackendDiagnostic> diagnostics,
        RenderBackendKind? backend,
        RenderDiagnosticSeverity severity,
        RenderBackendDiagnosticCategory category,
        string code,
        string message) =>
        diagnostics.Add(new RenderBackendDiagnostic(
            backend,
            severity,
            category,
            code,
            message));

    private sealed record RetiredBackend(
        IRenderBackend Backend,
        RenderBackendKind? Kind);

    private enum ActivationTransactionState
    {
        CandidatePrepared,
        PreviousDeactivated,
        CandidateActivationStarted,
        CandidateActivated,
        RollingBack,
        RolledBack,
        Committed,
        Abandoned
    }

    private sealed class ActivationTransaction(
        IRenderBackend candidate,
        RenderBackendKind candidateKind,
        IRenderBackend? previous,
        RenderBackendKind? previousKind)
    {
        internal IRenderBackend Candidate { get; } = candidate;

        internal RenderBackendKind CandidateKind { get; } = candidateKind;

        internal IRenderBackend? Previous { get; } = previous;

        internal RenderBackendKind? PreviousKind { get; } = previousKind;

        internal ActivationTransactionState State { get; private set; } =
            ActivationTransactionState.CandidatePrepared;

        internal void MarkPreviousDeactivated() =>
            Transition(
                ActivationTransactionState.CandidatePrepared,
                ActivationTransactionState.PreviousDeactivated);

        internal void MarkCandidateActivationStarted()
        {
            if (State is not (
                ActivationTransactionState.CandidatePrepared or
                ActivationTransactionState.PreviousDeactivated))
            {
                throw new InvalidOperationException(
                    $"Cannot start candidate activation from transaction state {State}.");
            }
            State = ActivationTransactionState.CandidateActivationStarted;
        }

        internal void MarkCandidateActivated() =>
            Transition(
                ActivationTransactionState.CandidateActivationStarted,
                ActivationTransactionState.CandidateActivated);

        internal void BeginRollback() =>
            Transition(
                ActivationTransactionState.CandidateActivationStarted,
                ActivationTransactionState.RollingBack);

        internal void CompleteRollback() =>
            Transition(
                ActivationTransactionState.RollingBack,
                ActivationTransactionState.RolledBack);

        internal void Commit() =>
            Transition(
                ActivationTransactionState.CandidateActivated,
                ActivationTransactionState.Committed);

        internal void Abandon() =>
            Transition(
                ActivationTransactionState.RollingBack,
                ActivationTransactionState.Abandoned);

        private void Transition(
            ActivationTransactionState expected,
            ActivationTransactionState next)
        {
            if (State != expected)
            {
                throw new InvalidOperationException(
                    $"Cannot transition activation transaction from {State} to {next}.");
            }
            State = next;
        }
    }
}
