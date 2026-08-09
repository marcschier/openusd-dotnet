// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Viewer;

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

    internal IRenderPickingBackend? PickingBackend =>
        _backendRegistry.CapturePickingBackend();

    internal ViewerSilkFrameDiagnosticsSnapshot? FrameDiagnostics =>
        _backendRegistry.CaptureFrameDiagnostics();

    internal ViewerHydraSceneSnapshot? HydraSceneSnapshot =>
        _backendRegistry.CaptureHydraSceneSnapshot();

    internal int GetCandidateSelectionCount(RenderBackendKind kind) =>
        _manager.GetCandidateSelectionCount(kind);

    internal int GetFactoryCreationCount(RenderBackendKind kind) =>
        _manager.GetFactoryCreationCount(kind);

    internal static async ValueTask<ViewerRenderCoordinator> OpenAsync(
        string stagePath,
        Func<UsdStageScheduler, UsdStageRenderSource, IViewerRenderBackendHost> hostFactory,
        RenderBackendKind? requestedBackend,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagePath);
        ArgumentNullException.ThrowIfNull(hostFactory);

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
            StageRenderState state = StageRenderState.Create(new StageIdentity(identifier));
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
            ViewerStartupOptions.WriteStatus("Renderer coordinator: backend initialization completed");
            coordinator.PublishResult("Renderer initialization", initialization);
            ViewerStartupOptions.WriteStatus("Renderer coordinator: initialized backend returning");
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

    internal async ValueTask<RenderBackendManagerResult> UpdateStateAsync(
        StageRenderState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ThrowIfDisposed();
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Volatile.Write(ref _currentState, state);
            StateChanged?.Invoke(state);
            RenderBackendManagerResult result = await _manager
                .UpdateStateAsync(state, cancellationToken)
                .ConfigureAwait(false);
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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ThrowIfDisposed();
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StageRenderState state = update(CurrentState);
            ArgumentNullException.ThrowIfNull(state);
            if (ReferenceEquals(state, CurrentState))
            {
                return false;
            }
            Volatile.Write(ref _currentState, state);
            StateChanged?.Invoke(state);
            RenderBackendManagerResult result = await _manager
                .UpdateStateAsync(state, cancellationToken)
                .ConfigureAwait(false);
            PublishResult("Renderer state", result);
            return true;
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
                StageRenderState revised = CurrentState.AdvanceRevision();
                Volatile.Write(ref _currentState, revised);
                StateChanged?.Invoke(revised);
                RenderBackendManagerResult result = await _manager
                    .UpdateStateAsync(revised, cancellationToken)
                    .ConfigureAwait(false);
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
        Publish(
            $"{operation}: {backend}; {fallback}; " +
            $"retiredCleanup={_manager.RetiredCleanupCount}");
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
