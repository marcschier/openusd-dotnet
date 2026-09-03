// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using Avalonia.Threading;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Viewer;

/// <summary>The outcome of one authoritative state mutation.</summary>
/// <param name="Applied">Whether the coordinator accepted the mutation.</param>
/// <param name="Changed">Whether the state actually differed.</param>
/// <param name="PublishedState">The state the coordinator now holds.</param>
internal readonly record struct ViewerStateMutationResult(
    bool Applied,
    bool Changed,
    StageRenderState PublishedState);

internal sealed class ViewerRenderCoordinator : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ViewerRenderBackendRegistry _backendRegistry;
    private readonly RenderBackendManager _manager;
    private readonly ViewerPickOperationQueue _pickQueue;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly Task _changePump;
    private StageRenderState _currentState;
    private RenderBackendKind? _lastReportedFrameBackend;
    private ulong _lastReportedFrameRevision = ulong.MaxValue;
    private RenderBackendDiagnostics _latestDiagnostics = RenderBackendDiagnostics.Empty;
    private string _latestRecoveryReason = "None";
    private bool _disposed;

    private ViewerRenderCoordinator(
        UsdStageScheduler scheduler,
        UsdStageRenderSource source,
        RenderBackendManager manager,
        ViewerRenderBackendRegistry backendRegistry,
        StageRenderState initialState)
    {
        Scheduler = scheduler;
        RenderSource = source;
        _manager = manager;
        _backendRegistry = backendRegistry;
        _currentState = initialState;
        _pickQueue = new ViewerPickOperationQueue(
            _backendRegistry.Capture,
            () => CurrentState,
            _lifetime.Token);
        _changePump = PumpStageChangesAsync(_lifetime.Token);
    }

    internal event EventHandler<string>? StatusChanged;

    internal event Action<UsdStageChange>? StageChanged;

    internal event Action<StageRenderState>? StateChanged;

    internal UsdStageScheduler Scheduler { get; }

    internal UsdStageRenderSource RenderSource { get; }

    internal int SchedulerEvidenceIdentity => RuntimeHelpers.GetHashCode(Scheduler);

    internal int RenderSourceEvidenceIdentity => RuntimeHelpers.GetHashCode(RenderSource);

    internal StageRenderState CurrentState => Volatile.Read(ref _currentState);

    internal RenderBackendIdentity? ActiveBackend => _manager.ActiveBackend;

    internal int RetiredCleanupCount => _manager.RetiredCleanupCount;

    internal RenderBackendDiagnostics LatestDiagnostics =>
        Volatile.Read(ref _latestDiagnostics);

    internal string LatestRecoveryReason => Volatile.Read(ref _latestRecoveryReason);

    internal ViewerPickingStatistics PickingStatistics => _pickQueue.Statistics;

    internal SilkSelectionOutlineDiagnostics? SelectionOutlineDiagnostics =>
        _backendRegistry.CaptureSelectionOutline();

    /// <summary>
    /// Gets the active backend's live colour-managed display-transform evidence, or
    /// <see langword="null"/> when the active backend has none.
    /// </summary>
    internal SilkDisplayTransformDiagnostics? DisplayTransformDiagnostics =>
        _backendRegistry.CaptureDisplayTransform();

    /// <summary>
    /// Gets the active backend's latest bounded display-transform diagnostic, or
    /// <see langword="null"/> when the transform ran or was never requested.
    /// </summary>
    internal RenderDiagnostic? DisplayTransformDiagnostic =>
        _backendRegistry.CaptureDisplayTransformDiagnostic();

    internal IRenderPickingBackend? PickingBackend =>
        _backendRegistry.CapturePickingBackend();

    internal ViewerSilkFrameDiagnosticsSnapshot? FrameDiagnostics =>
        _backendRegistry.CaptureFrameDiagnostics();

    internal ViewerHydraSceneSnapshot? HydraSceneSnapshot =>
        _backendRegistry.CaptureHydraSceneSnapshot();

    /// <summary>Captures the active backend as a physics override target.</summary>
    /// <param name="generation">Receives the identity of the currently active backend.</param>
    /// <returns>The active target, or <see langword="null"/> when none is active.</returns>
    internal IViewerPhysicsOverrideTarget? CapturePhysicsOverrideTarget(out long generation) =>
        _backendRegistry.CapturePhysicsOverrideTarget(out generation);

    internal int GetCandidateSelectionCount(RenderBackendKind kind) =>
        _manager.GetCandidateSelectionCount(kind);

    internal int GetFactoryCreationCount(RenderBackendKind kind) =>
        _manager.GetFactoryCreationCount(kind);

    internal static async ValueTask<ViewerRenderCoordinator> OpenAsync(
        string stagePath,
        Func<UsdStageScheduler, UsdStageRenderSource, IViewerRenderBackendHost> hostFactory,
        RenderBackendKind? requestedBackend,
        CancellationToken cancellationToken = default) =>
        await OpenAsync(
            stagePath,
            hostFactory,
            requestedBackend,
            RenderSettings.PresentationDefault,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Opens a coordinator with explicit initial render settings.</summary>
    /// <remarks>
    /// The settings a stage opens with are a caller decision, not a constant. A viewer
    /// that restored a colour-managed display transform at start-up used to lose it the
    /// moment a stage was opened, because every coordinator reset to the presentation
    /// default; the restored choice now travels into the very first state the backend is
    /// initialized with.
    /// </remarks>
    internal static async ValueTask<ViewerRenderCoordinator> OpenAsync(
        string stagePath,
        Func<UsdStageScheduler, UsdStageRenderSource, IViewerRenderBackendHost> hostFactory,
        RenderBackendKind? requestedBackend,
        RenderSettings initialRenderSettings,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagePath);
        ArgumentNullException.ThrowIfNull(hostFactory);
        initialRenderSettings.ValidateDisplayTransform();

        ViewerStartupOptions.WriteStatus(
            "Renderer coordinator: open entered " +
            FormatThreadStatus());
        ViewerStartupOptions.WriteStatus("Renderer coordinator: stage scheduler starting");
        UsdStageScheduler scheduler = UsdStageScheduler.Open(
            stagePath,
            capacity: 1024,
            notificationCapacity: 32);
        ViewerStartupOptions.WriteStatus("Renderer coordinator: stage scheduler created");
        UsdStageRenderSource? source = null;
        RenderBackendManager? manager = null;
        ViewerRenderCoordinator? coordinator = null;
        try
        {
            ViewerStartupOptions.WriteStatus("Renderer coordinator: render source acquiring");
            source = await scheduler.AcquireRenderSourceAsync(cancellationToken)
                .ConfigureAwait(false);
            ViewerStartupOptions.WriteStatus("Renderer coordinator: render source acquired");
            ViewerStartupOptions.WriteStatus("Renderer coordinator: root layer query starting");
            string identifier = await scheduler.InvokeAsync(
                static stage => stage.RootLayerIdentifier,
                cancellationToken).ConfigureAwait(false);
            ViewerStartupOptions.WriteStatus("Renderer coordinator: root layer query completed");
            StageRenderState state = StageRenderState
                .Create(new StageIdentity(identifier))
                .WithRenderSettings(initialRenderSettings);
            IViewerRenderBackendHost host = hostFactory(scheduler, source);
            RenderPlatform platform = GetPlatform();
            var backendRegistry = new ViewerRenderBackendRegistry();
            manager = new RenderBackendManager(
                platform,
                Enum.GetValues<RenderBackendKind>()
                    .Select(kind => new ViewerRenderBackendFactory(
                        kind,
                        host,
                        backendRegistry)));
            coordinator = new ViewerRenderCoordinator(
                scheduler,
                source,
                manager,
                backendRegistry,
                state);
            manager = null;
            source = null;
            ViewerStartupOptions.WriteStatus("Renderer coordinator: backend initialization starting");
            RenderBackendManagerResult initialization = await coordinator._manager
                .InitializeAsync(state, requestedBackend, cancellationToken)
                .ConfigureAwait(false);
            ViewerStartupOptions.WriteStatus(
                "Renderer coordinator: backend initialization completed " +
                FormatThreadStatus());
            coordinator.PublishResult("Renderer initialization", initialization);
            ViewerStartupOptions.WriteStatus(
                "Renderer coordinator: initialized backend returning " +
                FormatThreadStatus());
            ViewerStartupOptions.WriteStatus(
                "Renderer coordinator: dispatcher return probe armed " +
                FormatThreadStatus());
            Dispatcher.UIThread.Post(static () =>
                ViewerStartupOptions.WriteStatus(
                    "Renderer coordinator: dispatcher return probe processed " +
                    FormatThreadStatus()));
            ViewerStartupOptions.WriteStatus(
                "Renderer coordinator: dispatcher return probe posted " +
                FormatThreadStatus());
            PostDispatcherReturnPriorityProbe(
                "Send",
                DispatcherPriority.Send);
            PostDispatcherReturnPriorityProbe(
                "Background",
                DispatcherPriority.Background);
            return coordinator;
        }
        catch
        {
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                if (manager is not null)
                {
                    await manager.DisposeAsync().ConfigureAwait(false);
                }
                source?.Dispose();
                await scheduler.DisposeAsync().ConfigureAwait(false);
            }
            throw;
        }
    }

    private static string FormatThreadStatus() =>
        AvaloniaDispatcherShutdownDiagnostics.FormatThreadStatus();

    private static void PostDispatcherReturnPriorityProbe(
        string name,
        DispatcherPriority priority)
    {
        ViewerStartupOptions.WriteStatus(
            "Renderer coordinator: dispatcher return priority probe armed " +
            $"priority={name} {FormatThreadStatus()}");
        Dispatcher.UIThread.Post(
            static state =>
            {
                string priorityName = (string)state!;
                ViewerStartupOptions.WriteStatus(
                    "Renderer coordinator: dispatcher return priority probe processed " +
                    $"priority={priorityName} {FormatThreadStatus()}");
            },
            name,
            priority);
        ViewerStartupOptions.WriteStatus(
            "Renderer coordinator: dispatcher return priority probe posted " +
            $"priority={name} {FormatThreadStatus()}");
    }

    internal async ValueTask<RenderBackendManagerResult> SwitchAsync(
        RenderBackendKind? requestedBackend,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RenderBackendManagerResult result = await _manager
                .SwitchAsync(requestedBackend, cancellationToken)
                .ConfigureAwait(false);
            result = await ReapplyCurrentStateAfterSwitchAsync(
                _manager,
                CurrentState,
                result,
                cancellationToken).ConfigureAwait(false);
            PublishResult("Renderer switch", result);
            return result;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <summary>Replaces the authoritative state transactionally.</summary>
    /// <remarks>
    /// Backend first, publish second, exactly as <see cref="TryMutateStateAsync"/> does.
    /// A direct replacement is no more trustworthy than a computed one: publishing before
    /// the manager accepted left the coordinator, its subscribers, and every mirrored view
    /// describing a state the renderer had refused.
    /// </remarks>
    internal async ValueTask<RenderBackendManagerResult> UpdateStateAsync(
        StageRenderState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ThrowIfDisposed();
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RenderBackendManagerResult result = await _manager
                .UpdateStateAsync(state, cancellationToken)
                .ConfigureAwait(false);
            Volatile.Write(ref _currentState, state);
            StateChanged?.Invoke(state);
            PublishResult("Renderer state", result);
            return result;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    internal async ValueTask<bool> MutateStateAsync(
        Func<StageRenderState, StageRenderState> update,
        CancellationToken cancellationToken = default) =>
        (await TryMutateStateAsync(update, cancellationToken).ConfigureAwait(false)).Changed;

    /// <summary>
    /// Mutates the authoritative state transactionally and reports what was published.
    /// </summary>
    /// <remarks>
    /// The backend is told first and the coordinator publishes only once that succeeded.
    /// Publishing before the update meant a backend that threw or was cancelled left the
    /// coordinator claiming a state no renderer had ever been given -- and a caller that
    /// mirrors that state into a menu, a persisted setting, or a cache key mirrored the
    /// claim rather than the truth. The published state is returned so such a caller
    /// commits what the coordinator actually holds instead of what it asked for.
    /// </remarks>
    internal async ValueTask<ViewerStateMutationResult> TryMutateStateAsync(
        Func<StageRenderState, StageRenderState> update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ThrowIfDisposed();
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StageRenderState previous = CurrentState;
            StageRenderState state = update(previous);
            ArgumentNullException.ThrowIfNull(state);
            if (ReferenceEquals(state, previous))
            {
                return new ViewerStateMutationResult(true, false, previous);
            }

            RenderBackendManagerResult result = await _manager
                .UpdateStateAsync(state, cancellationToken)
                .ConfigureAwait(false);
            Volatile.Write(ref _currentState, state);
            StateChanged?.Invoke(state);
            PublishResult("Renderer state", result);
            return new ViewerStateMutationResult(true, true, state);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    internal async ValueTask<ManagedRenderFrameResult> RenderAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        ManagedRenderFrameResult result;
        try
        {
            result = await _manager
                .RenderAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _stateGate.Release();
        }
        if (result.IsSuccess &&
            result.ActiveBackend is { } active &&
            result.Frame is { Status: RenderFrameStatus.Rendered } frame &&
            (_lastReportedFrameBackend != active.Kind ||
                _lastReportedFrameRevision != frame.StateRevision))
        {
            _lastReportedFrameBackend = active.Kind;
            _lastReportedFrameRevision = frame.StateRevision;
            Publish(
                $"Renderer frame rendered: {active.Name}; " +
                $"revision={frame.StateRevision}; draws={frame.Statistics.DrawCalls}; " +
                $"retiredCleanup={_manager.RetiredCleanupCount}");
        }
        if (result.DidFailOver || !result.IsSuccess)
        {
            PublishFrame(result);
        }
        return result;
    }

    internal ValueTask<RenderPickResult> PickAsync(
        ViewerPhysicalPixel pixel,
        RenderPickTarget target = RenderPickTarget.Primitive,
        RenderPickOptions options = RenderPickOptions.None,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _pickQueue.PickAsync(pixel, target, options, cancellationToken);
    }

    internal ValueTask<SilkFrameCaptureResult> CaptureFrameAsync(
        int width,
        int height,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        IViewerFrameCaptureBackend backend = _backendRegistry.CaptureFrameCaptureBackend() ??
            throw new NotSupportedException("The active renderer cannot capture frames.");
        return backend.CaptureFrameAsync(width, height, cancellationToken);
    }

    internal static async ValueTask<RenderBackendManagerResult>
        ReapplyCurrentStateAfterSwitchAsync(
            RenderBackendManager manager,
            StageRenderState state,
            RenderBackendManagerResult switchResult,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(switchResult);
        if (!switchResult.IsSuccess)
        {
            return switchResult;
        }

        RenderBackendManagerResult reapplied = await manager
            .UpdateStateAsync(state, cancellationToken)
            .ConfigureAwait(false);
        return reapplied.IsSuccess ? switchResult : reapplied;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _pickQueue.Dispose();
        var failures = new List<Exception>();
        try
        {
            await _changePump.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        try
        {
            await _manager.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        try
        {
            RenderSource.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        try
        {
            await Scheduler.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        _stateGate.Dispose();
        _lifetime.Dispose();
        if (failures.Count != 0)
        {
            throw new AggregateException(
                "One or more viewer renderer resources failed to dispose.",
                failures);
        }
    }

    private async Task PumpStageChangesAsync(CancellationToken cancellationToken)
    {
        await foreach (UsdStageChange change in Scheduler.ReadChangesAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Backend first, publish second. A live stage edit the renderer refuses
                // must not advance the coordinator's revision or announce a stage change
                // that no backend ever saw.
                StageRenderState revised = CurrentState.AdvanceRevision();
                RenderBackendManagerResult result = await _manager
                    .UpdateStateAsync(revised, cancellationToken)
                    .ConfigureAwait(false);
                Volatile.Write(ref _currentState, revised);
                StateChanged?.Invoke(revised);
                PublishResult("Live stage update", result);
                StageChanged?.Invoke(change);
            }
            finally
            {
                _stateGate.Release();
            }
        }
    }

    private void PublishResult(string operation, RenderBackendManagerResult result)
    {
        bool reportInitializationBoundary =
            string.Equals(operation, "Renderer initialization", StringComparison.Ordinal);
        if (reportInitializationBoundary)
        {
            ViewerStartupOptions.WriteStatus(
                "Renderer coordinator: initialization result publish starting");
        }
        Volatile.Write(ref _latestDiagnostics, result.Diagnostics);
        Volatile.Write(ref _latestRecoveryReason, GetRecoveryReason(result.Diagnostics));
        string backend = result.ActiveBackend?.Name ?? "unavailable";
        string fallback = FormatDiagnostics(result.Diagnostics);
        foreach (RenderBackendDiagnostic diagnostic in result.Diagnostics.Entries)
        {
            ViewerStartupOptions.WriteStatus(
                $"{operation} diagnostic: {diagnostic.Backend?.ToString() ?? "none"}; " +
                $"{diagnostic.Code}; {diagnostic.Message}");
        }
        if (reportInitializationBoundary)
        {
            int subscribers = StatusChanged?.GetInvocationList().Length ?? 0;
            ViewerStartupOptions.WriteStatus(
                "Renderer coordinator: initialization diagnostics published; " +
                $"subscribers={subscribers}");
            ViewerStartupOptions.WriteStatus(
                "Renderer coordinator: initialization summary publishing");
        }
        Publish(
            $"{operation}: {backend}; {fallback}; " +
            $"retiredCleanup={_manager.RetiredCleanupCount}");
        if (reportInitializationBoundary)
        {
            ViewerStartupOptions.WriteStatus(
                "Renderer coordinator: initialization summary published");
        }
    }

    private void PublishFrame(ManagedRenderFrameResult result)
    {
        Volatile.Write(ref _latestDiagnostics, result.Diagnostics);
        Volatile.Write(ref _latestRecoveryReason, GetRecoveryReason(result.Diagnostics));
        string backend = result.ActiveBackend?.Name ?? "unavailable";
        string outcome = result.DidFailOver ? "device-loss fallback" : result.Failure.ToString();
        Publish(
            $"Renderer frame: {backend}; {outcome}; " +
            $"{FormatDiagnostics(result.Diagnostics)}; " +
            $"retiredCleanup={_manager.RetiredCleanupCount}");
    }

    private void Publish(string status)
    {
        ViewerStartupOptions.WriteStatus(status);
        StatusChanged?.Invoke(this, status);
    }

    private static string FormatDiagnostics(RenderBackendDiagnostics diagnostics)
    {
        RenderBackendDiagnostic? diagnostic = diagnostics.Entries.Count == 0
            ? null
            : diagnostics.Entries[^1];
        return diagnostic is null
            ? "ready"
            : $"{diagnostic.Code}: {diagnostic.Message}";
    }

    private static string GetRecoveryReason(RenderBackendDiagnostics diagnostics)
    {
        for (int index = diagnostics.Entries.Count - 1; index >= 0; index--)
        {
            RenderBackendDiagnostic diagnostic = diagnostics.Entries[index];
            if (diagnostic.Category is RenderBackendDiagnosticCategory.Fallback or
                RenderBackendDiagnosticCategory.DeviceLoss ||
                diagnostic.Severity is RenderDiagnosticSeverity.Warning or
                    RenderDiagnosticSeverity.Error)
            {
                return $"{diagnostic.Code}: {diagnostic.Message}";
            }
        }
        return "None";
    }

    private static RenderPlatform GetPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return RenderPlatform.Windows;
        }
        if (OperatingSystem.IsLinux())
        {
            return RenderPlatform.Linux;
        }
        if (OperatingSystem.IsMacOS())
        {
            return RenderPlatform.MacOS;
        }
        return RenderPlatform.Unknown;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
