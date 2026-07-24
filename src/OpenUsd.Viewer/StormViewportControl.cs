// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Viewer;

internal readonly record struct StormHostedFrameOutcome(
    bool DeviceLost,
    ulong StateRevision,
    string? Message = null);

public sealed class StormViewportControl : OpenGlControlBase
{
    public static readonly StyledProperty<string?> PluginPathProperty =
        AvaloniaProperty.Register<StormViewportControl, string?>(nameof(PluginPath));

    public static readonly StyledProperty<string?> StagePathProperty =
        AvaloniaProperty.Register<StormViewportControl, string?>(nameof(StagePath));

    private readonly object _bindingGate = new();
    private readonly TaskCompletionSource<string> _hostedInitialization =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private OpenUsdStormRenderer? _renderer;
    private StageRenderState _renderState = StageRenderState.Default;
    private StageBinding? _configuredBinding;
    private StageBinding? _activeBinding;
    private string? _activePluginPath;
    private string? _activeStagePath;
    private string _status = "Renderer: waiting for a stage";
    private TaskCompletionSource? _soakContextLossCompletion;
    private TaskCompletionSource? _soakShutdownCompletion;
    private TaskCompletionSource<StormHostedFrameOutcome>? _hostedFrame;
    private HostedPickRequest? _hostedPick;
    private SelectionState? _appliedSelection;
    private long _soakFrameCount;
    private long _soakPostLossFrameCount;
    private long _soakPreLossFrameCount;
    private long _soakShutdownCompletions;
    private int _soakRendererFaultCount;
    private int _soakContextLossRequested;
    private int _soakAwaitingPostLossFrame;
    private int _soakShutdownRequested;
    private bool _hasRendered;
    private bool _soakRenderingStopped;

    public StormViewportControl()
    {
        ViewerStartupOptions.WriteStatus("Renderer: control created");
    }

    public event EventHandler<string>? StatusChanged;

    public string? PluginPath
    {
        get => GetValue(PluginPathProperty);
        set => SetValue(PluginPathProperty, value);
    }

    public string? StagePath
    {
        get => GetValue(StagePathProperty);
        set => SetValue(StagePathProperty, value);
    }

    /// <summary>
    /// Uses an existing scheduler/source so viewer rendering observes the
    /// application's unsaved and session-layer edits.
    /// </summary>
    public void SetRenderSource(
        UsdStageScheduler scheduler,
        UsdStageRenderSource source,
        StormStageOwnership ownership = StormStageOwnership.FullyBorrowed)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(source);
        if (!Enum.IsDefined(ownership))
        {
            throw new ArgumentOutOfRangeException(nameof(ownership));
        }
        var replacement = new StageBinding(
            scheduler,
            source,
            ownership,
            null);
        StageBinding? releaseNow = null;
        lock (_bindingGate)
        {
            if (_configuredBinding is not null &&
                !ReferenceEquals(_configuredBinding, _activeBinding))
            {
                releaseNow = _configuredBinding;
            }
            _configuredBinding = replacement;
        }
        releaseNow?.ReleaseOwned();
        RequestRenderThreadSafe();
    }

    /// <summary>Returns to path-owned compatibility mode.</summary>
    public void ClearRenderSource()
    {
        StageBinding? releaseNow = null;
        lock (_bindingGate)
        {
            if (_configuredBinding is not null &&
                !ReferenceEquals(_configuredBinding, _activeBinding))
            {
                releaseNow = _configuredBinding;
            }
            _configuredBinding = null;
        }
        releaseNow?.ReleaseOwned();
        RequestRenderThreadSafe();
    }

    internal long SoakFrameCount => Interlocked.Read(ref _soakFrameCount);

    internal StageRenderState CurrentRenderState => Volatile.Read(ref _renderState);

    internal void UpdateRenderState(StageRenderState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Volatile.Write(ref _renderState, state);
        RequestRenderThreadSafe();
    }

    internal Task<string> WaitForHostedInitializationAsync(
        CancellationToken cancellationToken) =>
        _hostedInitialization.Task.WaitAsync(cancellationToken);

    internal async Task<StormHostedFrameOutcome> RenderHostedFrameAsync(
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<StormHostedFrameOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (Interlocked.CompareExchange(ref _hostedFrame, completion, null) is not null)
        {
            throw new InvalidOperationException("A Storm frame request is already pending.");
        }
        RequestRenderThreadSafe();
        try
        {
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.CompareExchange(ref _hostedFrame, null, completion);
        }
    }

    internal async ValueTask<RenderPickResult> PickHostedAsync(
        RenderPickRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pending = new HostedPickRequest(request, cancellationToken);
        if (Interlocked.CompareExchange(ref _hostedPick, pending, null) is not null)
        {
            throw new InvalidOperationException("A Storm pick request is already pending.");
        }
        RequestRenderThreadSafe();
        try
        {
            return await pending.Completion.Task
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            Interlocked.CompareExchange(ref _hostedPick, null, pending);
        }
    }

    internal SharedStageRendererDiagnostics GetSoakDiagnostics()
    {
        (long managed, long native, long nativePeak, long abandoned) =
            OpenUsdStormRuntime.GetDiagnostics();
        return new SharedStageRendererDiagnostics(
            Interlocked.Read(ref _soakPreLossFrameCount),
            Interlocked.Read(ref _soakPostLossFrameCount),
            Volatile.Read(ref _soakRendererFaultCount),
            managed,
            native,
            nativePeak,
            abandoned,
            Interlocked.Read(ref _soakShutdownCompletions));
    }

    internal void RequestSoakFrame() => RequestRenderThreadSafe();

    internal Task SimulateContextLossForSoakAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (Interlocked.CompareExchange(
            ref _soakContextLossCompletion,
            completion,
            null) is not null)
        {
            throw new InvalidOperationException(
                "A shared-stage context-loss request is already pending.");
        }
        Volatile.Write(ref _soakContextLossRequested, 1);
        RequestRenderThreadSafe();
        return completion.Task.WaitAsync(cancellationToken);
    }

    internal Task ShutdownSoakRendererAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (Interlocked.CompareExchange(
            ref _soakShutdownCompletion,
            completion,
            null) is not null)
        {
            throw new InvalidOperationException(
                "A shared-stage renderer shutdown is already pending.");
        }
        Volatile.Write(ref _soakShutdownRequested, 1);
        RequestRenderThreadSafe();
        return completion.Task.WaitAsync(cancellationToken);
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        SetStatus($"OpenGL context: {GlVersion}");
        EnsureRenderer();
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (Interlocked.Exchange(ref _soakShutdownRequested, 0) != 0)
        {
            CompleteSoakShutdown();
            return;
        }

        if (Interlocked.Exchange(ref _soakContextLossRequested, 0) != 0)
        {
            CompleteSoakContextLoss();
        }

        EnsureRenderer();
        if (_renderer is null)
        {
            return;
        }

        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        int width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * scaling));
        int height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * scaling));
        StageRenderState state = CurrentRenderState;
        try
        {
            if (_appliedSelection != state.Selection)
            {
                _renderer.SetSelection(
                    state.Selection,
                    ViewerPickingPolicy.StormSelectionColor);
                _appliedSelection = state.Selection;
            }
            bool converged = _renderer.Render(
                width,
                height,
                (uint)fb,
                state.Time.TimeCode,
                state.Camera,
                state.Revision,
                sceneRevision: null);
            CompleteHostedPick(_renderer);
            Interlocked.Increment(ref _soakFrameCount);
            Interlocked.Exchange(ref _hostedFrame, null)
                ?.TrySetResult(new StormHostedFrameOutcome(
                    DeviceLost: false,
                    state.Revision));
            if (Interlocked.Exchange(ref _soakAwaitingPostLossFrame, 0) != 0)
            {
                Interlocked.Increment(ref _soakPostLossFrameCount);
                Interlocked.Exchange(ref _soakContextLossCompletion, null)?.TrySetResult();
            }
            if (!_hasRendered)
            {
                _hasRendered = true;
                SetStatus(
                    $"Renderer: {ViewerStartupOptions.FormatStormRendererName(_renderer.Name)}; " +
                    "frame rendered");
            }
            if (!converged)
            {
                RequestNextFrameRendering();
            }
        }
        catch (Exception exception)
        {
            Interlocked.Exchange(ref _hostedPick, null)
                ?.Completion.TrySetException(exception);
            Interlocked.Increment(ref _soakRendererFaultCount);
            Interlocked.Exchange(ref _soakContextLossCompletion, null)
                ?.TrySetException(exception);
            Interlocked.Exchange(ref _hostedFrame, null)?.TrySetResult(
                new StormHostedFrameOutcome(
                    DeviceLost: true,
                    state.Revision,
                    exception.Message));
            DisposeRendererWithCurrentContext();
            SetStatus($"Renderer failed: {exception.Message}");
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        DisposeRendererWithCurrentContext();
        ReleasePathBinding();
    }

    protected override void OnOpenGlLost()
    {
        Exception? failure = null;
        try
        {
            AbandonRendererAfterContextLoss();
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        try
        {
            ReleaseActiveBinding();
        }
        catch (Exception ex)
        {
            failure ??= ex;
        }

        SetStatus("Renderer lost its OpenGL context");
        Interlocked.Exchange(ref _hostedPick, null)?.Completion.TrySetException(
            new InvalidOperationException("Storm lost its OpenGL context during picking."));
        StageRenderState state = CurrentRenderState;
        Interlocked.Exchange(ref _hostedFrame, null)?.TrySetResult(
            new StormHostedFrameOutcome(
                DeviceLost: true,
                state.Revision,
                "The OpenGL context was lost."));
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // The native compatibility release destroys only when the original
        // context is safely current; otherwise it intentionally leaks the GL
        // engine. Managed stage/session bookkeeping is always released first.
        StageBinding? active = _activeBinding;
        StageBinding? configured;
        lock (_bindingGate)
        {
            configured = _configuredBinding;
        }

        Exception? failure = null;
        try
        {
            ReleaseRendererAfterDetach();
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        bool activeReleased = false;
        try
        {
            ReleaseActiveBinding();
            activeReleased = true;
        }
        catch (Exception ex)
        {
            failure ??= ex;
        }

        if (ReferenceEquals(configured, active))
        {
            if (activeReleased)
            {
                ClearConfiguredBinding(configured);
            }
        }
        else
        {
            try
            {
                configured?.ReleaseOwned();
                ClearConfiguredBinding(configured);
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }

        try
        {
            base.OnDetachedFromVisualTree(e);
        }
        finally
        {
            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PluginPathProperty || change.Property == StagePathProperty)
        {
            RequestNextFrameRendering();
        }
    }

    private void EnsureRenderer()
    {
        if (_soakRenderingStopped)
        {
            return;
        }

        string? pluginPath = PluginPath;
        StageBinding? configured;
        lock (_bindingGate)
        {
            configured = _configuredBinding;
        }
        string? stagePath = configured is null ? StagePath : null;
        if (string.IsNullOrWhiteSpace(pluginPath) ||
            (configured is null && string.IsNullOrWhiteSpace(stagePath)))
        {
            DisposeRendererWithCurrentContext();
            ReleaseActiveBinding();
            SetStatus("Renderer: supply --plugins and --stage");
            return;
        }

        if (_renderer is not null &&
            string.Equals(_activePluginPath, pluginPath, StringComparison.Ordinal) &&
            ReferenceEquals(_activeBinding, configured) &&
            (configured is not null ||
                string.Equals(_activeStagePath, stagePath, StringComparison.Ordinal)))
        {
            return;
        }

        DisposeRendererWithCurrentContext();
        if (!ReferenceEquals(_activeBinding, configured))
        {
            ReleaseActiveBinding();
        }

        try
        {
            if (configured is not null)
            {
                _activeBinding = configured;
            }
            else
            {
                UsdStageScheduler scheduler = UsdStageScheduler.Open(stagePath!);
                UsdStageRenderSource source;
                try
                {
                    source = scheduler
                        .AcquireRenderSourceAsync()
                        .AsTask()
                        .GetAwaiter()
                        .GetResult();
                }
                catch
                {
                    scheduler.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    throw;
                }
                _activeBinding = new StageBinding(
                    scheduler,
                    source,
                    StormStageOwnership.OwnedSchedulerAndSource,
                    stagePath);
            }

            _renderer = OpenUsdStormRuntime.Create(pluginPath, _activeBinding.Source);
            _hasRendered = false;
            _activePluginPath = pluginPath;
            _activeStagePath = stagePath;
            SetStatus(
                $"Renderer: {ViewerStartupOptions.FormatStormRendererName(_renderer.Name)}");
            _hostedInitialization.TrySetResult(_renderer.Name);
            RequestNextFrameRendering();
        }
        catch (Exception exception)
        {
            DisposeRendererWithCurrentContext();
            if (configured is null)
            {
                ReleaseActiveBinding();
            }
            SetStatus($"Renderer unavailable: {exception.Message}");
            _hostedInitialization.TrySetException(exception);
        }
    }

    private void DisposeRendererWithCurrentContext()
    {
        OpenUsdStormRenderer? renderer = _renderer;
        renderer?.Dispose();
        _renderer = null;
        ResetRendererIdentity();
    }

    private void AbandonRendererAfterContextLoss()
    {
        OpenUsdStormRenderer? renderer = _renderer;
        renderer?.Abandon();
        _renderer = null;
        ResetRendererIdentity();
    }

    private void ReleaseRendererAfterDetach()
    {
        OpenUsdStormRenderer? renderer = _renderer;
        renderer?.ReleaseAfterDetach();
        _renderer = null;
        ResetRendererIdentity();
    }

    private void ReleasePathBinding()
    {
        if (_activeBinding?.StagePath is not null)
        {
            ReleaseActiveBinding();
        }
    }

    private void ReleaseActiveBinding()
    {
        StageBinding? binding = _activeBinding;
        binding?.ReleaseOwned();
        _activeBinding = null;
        if (binding?.OwnsSource == true)
        {
            lock (_bindingGate)
            {
                if (ReferenceEquals(_configuredBinding, binding))
                {
                    _configuredBinding = null;
                }
            }
        }
        ResetRendererIdentity();
    }

    private void ResetRendererIdentity()
    {
        _activePluginPath = null;
        _activeStagePath = null;
        _hasRendered = false;
        _appliedSelection = null;
    }

    private void ClearConfiguredBinding(StageBinding? binding)
    {
        lock (_bindingGate)
        {
            if (ReferenceEquals(_configuredBinding, binding))
            {
                _configuredBinding = null;
            }
        }
    }

    private void RequestRenderThreadSafe()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RequestNextFrameRendering();
        }
        else
        {
            Dispatcher.UIThread.Post(RequestNextFrameRendering);
        }
    }

    private void CompleteSoakContextLoss()
    {
        try
        {
            Interlocked.Exchange(
                ref _soakPreLossFrameCount,
                Interlocked.Read(ref _soakFrameCount));
            AbandonRendererAfterContextLoss();
            EnsureRenderer();
            if (_renderer is null)
            {
                throw new InvalidOperationException(
                    "Storm renderer recreation failed after simulated context loss.");
            }
            Volatile.Write(ref _soakAwaitingPostLossFrame, 1);
            RequestNextFrameRendering();
        }
        catch (Exception exception)
        {
            Interlocked.Increment(ref _soakRendererFaultCount);
            Interlocked.Exchange(ref _soakContextLossCompletion, null)
                ?.TrySetException(exception);
            throw;
        }
    }

    private void CompleteSoakShutdown()
    {
        TaskCompletionSource? completion =
            Interlocked.Exchange(ref _soakShutdownCompletion, null);
        try
        {
            _soakRenderingStopped = true;
            Interlocked.Exchange(ref _hostedFrame, null)?.TrySetException(
                new ObjectDisposedException(nameof(StormViewportControl)));
            Interlocked.Exchange(ref _hostedPick, null)?.Completion.TrySetException(
                new ObjectDisposedException(nameof(StormViewportControl)));
            DisposeRendererWithCurrentContext();
            ReleaseActiveBinding();
            lock (_bindingGate)
            {
                _configuredBinding = null;
            }
            Dispatcher.UIThread.Post(() =>
            {
                Interlocked.Increment(ref _soakShutdownCompletions);
                completion?.TrySetResult();
            });
        }
        catch (Exception exception)
        {
            completion?.TrySetException(exception);
            throw;
        }
    }

    private void SetStatus(string value)
    {
        if (string.Equals(_status, value, StringComparison.Ordinal))
        {
            return;
        }

        _status = value;
        ViewerStartupOptions.WriteStatus(value);
        StatusChanged?.Invoke(this, value);
    }

    private void CompleteHostedPick(OpenUsdStormRenderer renderer)
    {
        HostedPickRequest? pending = Interlocked.Exchange(ref _hostedPick, null);
        if (pending is null)
        {
            return;
        }
        if (pending.CancellationToken.IsCancellationRequested)
        {
            pending.Completion.TrySetCanceled(pending.CancellationToken);
            return;
        }

        try
        {
            pending.Completion.TrySetResult(renderer.Pick(pending.Request));
        }
        catch (Exception exception)
        {
            pending.Completion.TrySetException(exception);
        }
    }

    private sealed class HostedPickRequest(
        RenderPickRequest request,
        CancellationToken cancellationToken)
    {
        internal RenderPickRequest Request { get; } = request;

        internal CancellationToken CancellationToken { get; } = cancellationToken;

        internal TaskCompletionSource<RenderPickResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal sealed class StageBinding
    {
        private readonly StageBindingLifetime _lifetime;

        internal StageBinding(
            UsdStageScheduler scheduler,
            UsdStageRenderSource source,
            StormStageOwnership ownership,
            string? stagePath)
        {
            Scheduler = scheduler;
            Source = source;
            Ownership = ownership;
            StagePath = stagePath;
            _lifetime = new StageBindingLifetime(
                ownership,
                source.Dispose,
                () => scheduler.DisposeAsync().AsTask().GetAwaiter().GetResult());
        }

        internal UsdStageScheduler Scheduler { get; }

        internal UsdStageRenderSource Source { get; }

        internal StormStageOwnership Ownership { get; }

        internal bool OwnsSource =>
            Ownership != StormStageOwnership.FullyBorrowed;

        internal string? StagePath { get; }

        internal void ReleaseOwned() => _lifetime.ReleaseOwned();
    }
}

/// <summary>Specifies aggregate scheduler/source ownership for the viewport.</summary>
public enum StormStageOwnership
{
    /// <summary>The caller retains disposal responsibility for both resources.</summary>
    FullyBorrowed = 0,

    /// <summary>The caller owns the scheduler; the viewport owns the source.</summary>
    BorrowedSchedulerOwnedSource = 1,

    /// <summary>The viewport owns both scheduler and source.</summary>
    OwnedSchedulerAndSource = 2
}

internal sealed class StageBindingLifetime
{
    private readonly object _gate = new();
    private readonly StormStageOwnership _ownership;
    private readonly Action _releaseSource;
    private readonly Action _releaseScheduler;
    private bool _sourceReleased;
    private bool _schedulerReleased;

    internal StageBindingLifetime(
        StormStageOwnership ownership,
        Action releaseSource,
        Action releaseScheduler)
    {
        if (!Enum.IsDefined(ownership))
        {
            throw new ArgumentOutOfRangeException(nameof(ownership));
        }
        _ownership = ownership;
        _releaseSource = releaseSource;
        _releaseScheduler = releaseScheduler;
    }

    internal void ReleaseOwned()
    {
        lock (_gate)
        {
            if (_ownership != StormStageOwnership.FullyBorrowed &&
                !_sourceReleased)
            {
                _releaseSource();
                _sourceReleased = true;
            }
            if (_ownership == StormStageOwnership.OwnedSchedulerAndSource &&
                !_schedulerReleased)
            {
                _releaseScheduler();
                _schedulerReleased = true;
            }
        }
    }
}
