// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Viewer;

internal interface IViewerRenderBackendHost
{
    ValueTask<RenderBackendProbeResult> ProbeAsync(
        RenderBackendKind kind,
        CancellationToken cancellationToken);

    ValueTask<IViewerRenderBackendSession> AttachAsync(
        RenderBackendKind kind,
        StageRenderState initialState,
        CancellationToken cancellationToken);
}

internal interface IViewerRenderBackendSession : IAsyncDisposable
{
    RenderBackendDiagnostics Diagnostics { get; }

    StageRenderState CurrentState { get; }

    ValueTask ActivateAsync(CancellationToken cancellationToken);

    ValueTask DeactivateAsync(CancellationToken cancellationToken);

    ValueTask UpdateStateAsync(
        StageRenderState state,
        CancellationToken cancellationToken);

    ValueTask ResizeAsync(
        ViewportDimensions viewport,
        CancellationToken cancellationToken);

    ValueTask<RenderFrameResult> RenderAsync(CancellationToken cancellationToken);
}

internal interface IViewerSelectionOutlineDiagnosticsSource
{
    SilkSelectionOutlineDiagnostics? SelectionOutlineDiagnostics { get; }
}

/// <summary>
/// Reports the live colour-managed display-transform evidence of a backend, so the
/// Viewer never claims a transform is running that the renderer refused.
/// </summary>
internal interface IViewerDisplayTransformDiagnosticsSource
{
    SilkDisplayTransformDiagnostics? DisplayTransformDiagnostics { get; }

    RenderDiagnostic? DisplayTransformDiagnostic { get; }
}

/// <summary>
/// Reports what a backend with no display-transform capability must say about a requested
/// colour-managed transform.
/// </summary>
/// <remarks>
/// Silence is not an option here. A backend that cannot run the fullscreen pass -- Storm,
/// in every one of its hosting shapes -- would otherwise present untransformed colour
/// while the Viewer's menu still claimed the transform was active. Saying
/// <see cref="SilkDisplayTransformStatus.UnsupportedDevice"/> is what lets the Viewer
/// disable the toggle and name the reason, exactly as it does for a missing config.
/// </remarks>
internal static class ViewerUnsupportedDisplayTransform
{
    internal static SilkDisplayTransformDiagnostics Describe(StageRenderState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new SilkDisplayTransformDiagnostics(
            state.RenderSettings.DisplayTransform is null
                ? SilkDisplayTransformStatus.Inactive
                : SilkDisplayTransformStatus.UnsupportedDevice,
            LatticeSize: 0,
            LatticeByteSize: 0,
            Passes: 0,
            LatticeBuilds: 0,
            LatticeCacheHits: 0,
            LatticeUploads: 0,
            PipelineCreations: 0,
            BindingCreations: 0,
            IntermediateCreations: 0,
            ParameterUploads: 0,
            DeviceInvalidations: 0,
            Failures: state.RenderSettings.DisplayTransform is null ? 0UL : 1UL,
            // Correlated with the exact request, so a consumer that ignores reports for
            // superseded transforms still acts on this one.
            RequestKey: state.RenderSettings.DisplayTransform?.CacheKey);
    }

    internal static RenderDiagnostic? Diagnose(StageRenderState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.RenderSettings.DisplayTransform is null
            ? null
            : new RenderDiagnostic(
                RenderDiagnosticSeverity.Warning,
                SilkRenderDiagnosticCodes.DisplayTransformDeviceUnsupported,
                "The active render backend cannot apply a colour-managed display " +
                "transform, so the viewport shows untransformed colour.");
    }
}

internal interface IViewerFrameDiagnosticsSource
{
    ViewerSilkFrameDiagnosticsSnapshot? FrameDiagnostics { get; }
}

internal interface IViewerHydraSceneSnapshotSource
{
    ViewerHydraSceneSnapshot? HydraSceneSnapshot { get; }
}

internal interface IViewerFrameCaptureBackend
{
    ValueTask<SilkFrameCaptureResult> CaptureFrameAsync(
        int width,
        int height,
        CancellationToken cancellationToken);
}

internal sealed class ViewerBackendInitializationException : Exception
{
    internal ViewerBackendInitializationException(
        RenderBackendInitializationFailureKind failure,
        RenderBackendDiagnostics diagnostics,
        Exception? innerException = null,
        IViewerRenderBackendSession? cleanupOwner = null)
        : base(GetMessage(diagnostics), innerException)
    {
        Failure = failure;
        Diagnostics = diagnostics;
        CleanupOwner = cleanupOwner;
    }

    internal RenderBackendInitializationFailureKind Failure { get; }

    internal RenderBackendDiagnostics Diagnostics { get; }

    internal IViewerRenderBackendSession? CleanupOwner { get; }

    private static string GetMessage(RenderBackendDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return (diagnostics.Entries.Count == 0 ? null : diagnostics.Entries[^1])?.Message ??
            "The viewer backend host could not initialize the requested backend.";
    }

}

internal sealed class ViewerBackendAttachmentCanceledException(
    IViewerRenderBackendSession cleanupOwner,
    OperationCanceledException innerException)
    : OperationCanceledException(
        "Viewer backend attachment was canceled.",
        innerException,
        innerException.CancellationToken)
{
    internal IViewerRenderBackendSession CleanupOwner { get; } = cleanupOwner;
}

internal sealed class ViewerRenderBackendRegistry
{
    private readonly object _gate = new();
    private ViewerRenderBackend? _active;
    private long _generation;

    internal void Activate(ViewerRenderBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        lock (_gate)
        {
            if (!ReferenceEquals(_active, backend))
            {
                _active = backend;
                _generation = checked(_generation + 1);
            }
        }
    }

    internal void Deactivate(ViewerRenderBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        lock (_gate)
        {
            if (ReferenceEquals(_active, backend))
            {
                _active = null;
                _generation = checked(_generation + 1);
            }
        }
    }

    internal ViewerPickBackendSnapshot? Capture()
    {
        lock (_gate)
        {
            return _active is null
                ? null
                : new ViewerPickBackendSnapshot(
                    _active,
                    _active.LastRenderedPickState,
                    _generation);
        }
    }

    internal IRenderPickingBackend? CapturePickingBackend()
    {
        lock (_gate)
        {
            return _active is not null &&
                _active.Capabilities.Supports(RenderBackendCapability.Picking)
                    ? _active
                    : null;
        }
    }

    internal SilkDisplayTransformDiagnostics? CaptureDisplayTransform()
    {
        lock (_gate)
        {
            return (_active as IViewerDisplayTransformDiagnosticsSource)?
                .DisplayTransformDiagnostics;
        }
    }

    internal RenderDiagnostic? CaptureDisplayTransformDiagnostic()
    {
        lock (_gate)
        {
            return (_active as IViewerDisplayTransformDiagnosticsSource)?
                .DisplayTransformDiagnostic;
        }
    }

    internal SilkSelectionOutlineDiagnostics? CaptureSelectionOutline()
    {
        lock (_gate)
        {
            return (_active as IViewerSelectionOutlineDiagnosticsSource)?
                .SelectionOutlineDiagnostics;
        }
    }

    internal ViewerSilkFrameDiagnosticsSnapshot? CaptureFrameDiagnostics()
    {
        lock (_gate)
        {
            return (_active as IViewerFrameDiagnosticsSource)?.FrameDiagnostics;
        }
    }

    internal ViewerHydraSceneSnapshot? CaptureHydraSceneSnapshot()
    {
        lock (_gate)
        {
            return (_active as IViewerHydraSceneSnapshotSource)?.HydraSceneSnapshot;
        }
    }

    internal IViewerFrameCaptureBackend? CaptureFrameCaptureBackend()
    {
        lock (_gate)
        {
            return _active as IViewerFrameCaptureBackend;
        }
    }

    /// <summary>
    /// Captures the active backend as a physics override target and the generation that names it.
    /// </summary>
    /// <remarks>
    /// The generation changes whenever the active backend does, which is how the render loop knows
    /// a backend switch or a recovered device needs the latest override batch replayed instead of
    /// waiting for the next simulated frame.
    /// </remarks>
    /// <param name="generation">Receives the identity of the currently active backend.</param>
    /// <returns>The active target, or <see langword="null"/> when none is active.</returns>
    internal IViewerPhysicsOverrideTarget? CapturePhysicsOverrideTarget(out long generation)
    {
        lock (_gate)
        {
            generation = _generation;
            return _active is not null &&
                _active.Capabilities.Supports(
                    RenderBackendCapability.PhysicsTransformOverrides)
                    ? _active
                    : null;
        }
    }
}

internal sealed class ViewerRenderBackendFactory : IRenderBackendFactory
{
    private readonly IViewerRenderBackendHost _host;
    private readonly ViewerRenderBackendRegistry? _registry;

    internal ViewerRenderBackendFactory(
        RenderBackendKind kind,
        IViewerRenderBackendHost host,
        ViewerRenderBackendRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        Kind = kind;
        _host = host;
        _registry = registry;
    }

    public RenderBackendKind Kind { get; }

    public IRenderBackend Create() => new ViewerRenderBackend(Kind, _host, _registry);
}

internal sealed class ViewerRenderBackend :
    IRenderBackend,
    IRenderBackendActivationControl,
    IRenderPickingBackend,
    IViewerRenderedPickStateSource,
    IViewerSelectionOutlineDiagnosticsSource,
    IViewerDisplayTransformDiagnosticsSource,
    IViewerFrameDiagnosticsSource,
    IViewerHydraSceneSnapshotSource,
    IViewerFrameCaptureBackend,
    IViewerPhysicsOverrideTarget
{
    private readonly object _disposeGate = new();
    private readonly IViewerRenderBackendHost _host;
    private readonly ViewerRenderBackendRegistry? _registry;
    private Task? _disposeTask;
    private IViewerRenderBackendSession? _session;
    private ViewerRenderedPickState? _lastRenderedPickState;
    private bool _disposed;

    internal ViewerRenderBackend(
        RenderBackendKind kind,
        IViewerRenderBackendHost host,
        ViewerRenderBackendRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        _registry = registry;
        Identity = CreateIdentity(kind);
        Capabilities = CreateCapabilities(kind);
    }

    public RenderBackendIdentity Identity { get; }

    public RenderBackendCapabilities Capabilities { get; }

    public ViewerRenderedPickState? LastRenderedPickState =>
        (_session as IViewerRenderedPickStateSource)?.LastRenderedPickState ??
        Volatile.Read(ref _lastRenderedPickState);

    public SilkSelectionOutlineDiagnostics? SelectionOutlineDiagnostics =>
        (_session as IViewerSelectionOutlineDiagnosticsSource)?
            .SelectionOutlineDiagnostics;

    public SilkDisplayTransformDiagnostics? DisplayTransformDiagnostics =>
        (_session as IViewerDisplayTransformDiagnosticsSource)?
            .DisplayTransformDiagnostics;

    public RenderDiagnostic? DisplayTransformDiagnostic =>
        (_session as IViewerDisplayTransformDiagnosticsSource)?
            .DisplayTransformDiagnostic;

    public ViewerSilkFrameDiagnosticsSnapshot? FrameDiagnostics =>
        (_session as IViewerFrameDiagnosticsSource)?.FrameDiagnostics;

    public ViewerHydraSceneSnapshot? HydraSceneSnapshot =>
        (_session as IViewerHydraSceneSnapshotSource)?.HydraSceneSnapshot;

    public bool SupportsPhysicsTransformOverrides =>
        (_session as IViewerPhysicsOverrideTarget)?.SupportsPhysicsTransformOverrides ?? false;

    public int ApplyPhysicsOverrides(
        in PhysicsRenderOverrideView overrides,
        PhysicsRenderBindingTable bindings) =>
        _session is IViewerPhysicsOverrideTarget target
            ? target.ApplyPhysicsOverrides(in overrides, bindings)
            : 0;

    // Deformations forward exactly like the rigid batch above. Leaving this out is invisible
    // rather than fatal, because the interface supplies a zero returning default for backends
    // that cannot upload geometry, so the whole deformable path silently reported "no regions
    // applied" while the sessions underneath implemented it.
    public int ApplyPhysicsDeformations(
        in PhysicsRenderDeformationView deformations,
        PhysicsRenderBindingTable bindings) =>
        _session is IViewerPhysicsOverrideTarget target
            ? target.ApplyPhysicsDeformations(in deformations, bindings)
            : 0;

    public bool TryTakeOverrideReport(out ViewerPhysicsOverrideReport report)
    {
        if (_session is IViewerPhysicsOverrideTarget target)
        {
            return target.TryTakeOverrideReport(out report);
        }

        report = default;
        return false;
    }

    public void ClearPhysicsOverrides() =>
        (_session as IViewerPhysicsOverrideTarget)?.ClearPhysicsOverrides();

    public ValueTask<RenderBackendProbeResult> ProbeAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _host.ProbeAsync(Identity.Kind, cancellationToken);
    }

    public async ValueTask<RenderBackendInitializationResult> InitializeAsync(
        StageRenderState initialState,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(initialState);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_session is not null)
        {
            throw new InvalidOperationException("The viewer render backend is already initialized.");
        }

        try
        {
            _session = await _host.AttachAsync(
                Identity.Kind,
                initialState,
                cancellationToken).ConfigureAwait(false);
            return RenderBackendInitializationResult.Success(_session.Diagnostics);
        }
        catch (ViewerBackendAttachmentCanceledException exception)
        {
            _session = exception.CleanupOwner;
            throw;
        }
        catch (ViewerBackendInitializationException exception)
        {
            _session = exception.CleanupOwner;
            return RenderBackendInitializationResult.Failed(
                exception.Failure,
                exception.Diagnostics);
        }
    }

    public ValueTask UpdateStateAsync(
        StageRenderState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return GetSession().UpdateStateAsync(state, cancellationToken);
    }

    public ValueTask ResizeAsync(
        ViewportDimensions viewport,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return GetSession().ResizeAsync(viewport, cancellationToken);
    }

    public ValueTask<RenderFrameResult> RenderAsync(
        CancellationToken cancellationToken = default) =>
        RenderCoreAsync(cancellationToken);

    public ValueTask<RenderPickResult> PickAsync(
        RenderPickRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IViewerRenderBackendSession session = GetSession();
        if (session is IRenderPickingBackend picking)
        {
            return picking.PickAsync(request, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        ViewerRenderedPickState binding = LastRenderedPickState ??
            new ViewerRenderedPickState(
                session.CurrentState,
                request.RequestedSceneRevision,
                Identity.Kind);
        return ValueTask.FromResult(
            request.IsStale(binding.State.Revision, binding.SceneRevision)
                ? RenderPickResult.Stale(
                    request,
                    binding.State.Revision,
                    binding.SceneRevision)
                : RenderPickResult.Unsupported(
                    request,
                    binding.State.Revision,
                    binding.SceneRevision));
    }

    public ValueTask<SilkFrameCaptureResult> CaptureFrameAsync(
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IViewerRenderBackendSession session = GetSession();
        return session is IViewerFrameCaptureBackend capture
            ? capture.CaptureFrameAsync(width, height, cancellationToken)
            : throw new NotSupportedException("The active renderer cannot capture frames.");
    }

    public async ValueTask ActivateAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await GetSession().ActivateAsync(cancellationToken).ConfigureAwait(false);
        _registry?.Activate(this);
    }

    public async ValueTask DeactivateAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await GetSession().DeactivateAsync(cancellationToken).ConfigureAwait(false);
        _registry?.Deactivate(this);
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            if (_disposed)
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
        IViewerRenderBackendSession? session = _session;
        try
        {
            _registry?.Deactivate(this);
            if (session is not null)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            _session = null;
            Volatile.Write(ref _lastRenderedPickState, null);
            _disposed = true;
        }
        catch
        {
            lock (_disposeGate)
            {
                _disposeTask = null;
            }
            throw;
        }
    }

    private IViewerRenderBackendSession GetSession() =>
        _session ?? throw new InvalidOperationException(
            "The viewer render backend has not been initialized.");

    private async ValueTask<RenderFrameResult> RenderCoreAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IViewerRenderBackendSession session = GetSession();
        RenderFrameResult result = await session
            .RenderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (result.Status == RenderFrameStatus.Rendered)
        {
            ViewerRenderedPickState? rendered =
                (session as IViewerRenderedPickStateSource)?.LastRenderedPickState;
            if (rendered is null)
            {
                StageRenderState state = session.CurrentState;
                if (state.Revision == result.StateRevision)
                {
                    rendered = new ViewerRenderedPickState(
                        state,
                        SceneRevision: null,
                        Identity.Kind);
                }
            }
            if (rendered?.State.Revision == result.StateRevision)
            {
                ViewerRenderedPickStateStore.PublishNewest(
                    ref _lastRenderedPickState,
                    rendered);
            }
        }
        return result;
    }

    private static RenderBackendIdentity CreateIdentity(RenderBackendKind kind) =>
        kind switch
        {
            RenderBackendKind.Storm => new(kind, "Storm / OpenGL"),
            RenderBackendKind.D3D12 => new(kind, "hdSilk / Direct3D 12"),
            RenderBackendKind.Vulkan => new(kind, "hdSilk / Vulkan"),
            RenderBackendKind.Metal => new(kind, "hdSilk / Metal"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static RenderBackendCapabilities CreateCapabilities(RenderBackendKind kind)
    {
        RenderBackendCapability features = kind switch
        {
            RenderBackendKind.Storm =>
                RenderBackendCapability.Presentation |
                RenderBackendCapability.Multisampling |
                RenderBackendCapability.Shadows |
                RenderBackendCapability.DeviceLossDetection |
                RenderBackendCapability.Picking |
                RenderBackendCapability.PhysicsTransformOverrides,
            RenderBackendKind.D3D12 or RenderBackendKind.Vulkan or RenderBackendKind.Metal =>
                RenderBackendCapability.Presentation |
                RenderBackendCapability.Offscreen |
                RenderBackendCapability.Compute |
                RenderBackendCapability.DeviceLossDetection |
                RenderBackendCapability.Picking |
                RenderBackendCapability.PhysicsTransformOverrides,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return new RenderBackendCapabilities(
            features,
            maxSamplesPerPixel: kind == RenderBackendKind.Storm ? 8 : 1,
            isSoftware: false);
    }
}
