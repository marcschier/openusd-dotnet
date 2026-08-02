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

internal interface IViewerFrameDiagnosticsSource
{
    ViewerSilkFrameDiagnosticsSnapshot? FrameDiagnostics { get; }
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
    IViewerFrameDiagnosticsSource
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

    public ViewerSilkFrameDiagnosticsSnapshot? FrameDiagnostics =>
        (_session as IViewerFrameDiagnosticsSource)?.FrameDiagnostics;

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
                RenderBackendCapability.Picking,
            RenderBackendKind.D3D12 or RenderBackendKind.Vulkan or RenderBackendKind.Metal =>
                RenderBackendCapability.Presentation |
                RenderBackendCapability.Offscreen |
                RenderBackendCapability.Compute |
                RenderBackendCapability.DeviceLossDetection |
                RenderBackendCapability.Picking,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return new RenderBackendCapabilities(
            features,
            maxSamplesPerPixel: kind == RenderBackendKind.Storm ? 8 : 1,
            isSoftware: false);
    }
}
