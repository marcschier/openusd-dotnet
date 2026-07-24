// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

public sealed class CompositionViewportControl : Control, IAsyncDisposable
{
    private readonly AvaloniaCompositionDispatcher _dispatcher = new();
    private readonly Action _compositionUpdate;
    private readonly Lock _disposeLock = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly ObservedTaskRunner _tasks;
    private readonly CompositionPresentationPump _pump;
    private readonly BoundedCompositionRecovery _recovery = new();
    private readonly ViewerCompositionObservation _runtimeObservation = new();
    private readonly ConcurrentQueue<CancellationTokenSource> _retiredAttachments = new();
    private readonly TaskCompletionSource<bool> _initialization =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _attachment;
    private TopLevel? _topLevel;
    private CompositionViewportSession? _session;
    private CompositionDrawingSurface? _surface;
    private CompositionSurfaceVisual? _surfaceVisual;
    private Compositor? _compositor;
    private Task _detachment = Task.CompletedTask;
    private Task? _disposeTask;
    private TaskCompletionSource<CompositionPresentOutcome>? _nextPresentation;
    private ViewportDimensions _pixelSize;
    private bool _updateQueued;
    private bool _disposed;

    public CompositionViewportControl()
    {
        _compositionUpdate = OnCompositionUpdate;
        _tasks = new ObservedTaskRunner(OnBackgroundFailure);
        _pump = new CompositionPresentationPump(PumpIterationAsync, _tasks);
    }

    public event EventHandler<string>? StatusChanged;

    public Func<ICompositionViewportPresenter>? PresenterFactory { get; set; }

    public string Status { get; private set; } = "GPU composition: not attached";

    internal bool ManagerControlsDeviceLoss { get; set; }

    internal RenderBackendKind BackendKind { get; set; }

    internal ViewerCompositionEvidence GetRuntimeEvidence() =>
        _runtimeObservation.Snapshot(IsVisible && VisualRoot is not null);

    internal Task<bool> WaitForInitializationAsync(CancellationToken cancellationToken) =>
        _initialization.Task.WaitAsync(cancellationToken);

    internal async Task<CompositionPresentOutcome> PresentNextFrameAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var completion = new TaskCompletionSource<CompositionPresentOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (Interlocked.CompareExchange(ref _nextPresentation, completion, null) is not null)
        {
            throw new InvalidOperationException(
                "A composition frame request is already pending.");
        }

        _pump.Request();
        try
        {
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.CompareExchange(ref _nextPresentation, null, completion);
        }
    }

    internal CompositionViewportSessionStatistics GetSessionStatistics() =>
        _session?.GetStatistics() ?? default;

    internal void RequestPresentationForDiagnostics() => _pump.Request();

    public Task ReinitializePresentationAsync()
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }
        return _tasks.Run("composition host reinitialization", ReinitializeCoreAsync);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_disposed)
        {
            return;
        }
        var attachment = new CancellationTokenSource();
        _attachment = attachment;
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.ScalingChanged += OnScalingChanged;
        Task previousDetachment = _detachment;
        _tasks.Run(
            "composition host attachment",
            async () =>
            {
                try
                {
                    await previousDetachment.ConfigureAwait(false);
                    if (ReferenceEquals(_attachment, attachment))
                    {
                        bool initialized = await InitializeAsync(attachment.Token)
                            .ConfigureAwait(false);
                        _initialization.TrySetResult(initialized);
                    }
                }
                catch (OperationCanceledException) when (attachment.IsCancellationRequested)
                {
                    _initialization.TrySetCanceled(attachment.Token);
                    throw;
                }
                catch (Exception exception)
                {
                    _initialization.TrySetException(exception);
                    throw;
                }
            });
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_disposed)
        {
            base.OnDetachedFromVisualTree(e);
            return;
        }
        CancelAttachment();
        ElementComposition.SetElementChildVisual(this, null);
        _detachment = _tasks.Run(
            "composition host detachment",
            TeardownDetachedAsync);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
        {
            QueueCompositionUpdate();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            if (_disposeTask is null)
            {
                _disposed = true;
                _initialization.TrySetException(new ObjectDisposedException(
                    nameof(CompositionViewportControl)));
                Interlocked.Exchange(ref _nextPresentation, null)?.TrySetException(
                    new ObjectDisposedException(nameof(CompositionViewportControl)));
                _disposeTask = DisposeOnceAsync();
            }
            return new ValueTask(_disposeTask);
        }
    }

    private async Task<bool> InitializeAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            try
            {
                await TeardownCoreAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(
                    "Composition initialization and cleanup failed.",
                    exception,
                    cleanupException);
            }
            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task<bool> InitializeCoreAsync(CancellationToken cancellationToken)
    {
        Func<ICompositionViewportPresenter>? factory = PresenterFactory;
        if (factory is null)
        {
            await SetStatusAsync(
                "GPU composition unavailable: no backend external-image adapter; Storm OpenGL remains active")
                .ConfigureAwait(false);
            return false;
        }

        Compositor? compositor = null;
        ICompositionGpuInterop? interop = null;
        CompositionDrawingSurface? surface = null;
        CompositionSurfaceVisual? surfaceVisual = null;
        await _dispatcher.InvokeAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompositionVisual elementVisual = ElementComposition.GetElementVisual(this) ??
                throw new InvalidOperationException("Avalonia did not create an element visual.");
            compositor = elementVisual.Compositor;
            interop = await compositor.TryGetCompositionGpuInterop().ConfigureAwait(true);
            if (interop is null)
            {
                return;
            }

            surface = compositor.CreateDrawingSurface();
            surfaceVisual = compositor.CreateSurfaceVisual();
            surfaceVisual.Size = new Vector(Bounds.Width, Bounds.Height);
            surfaceVisual.Surface = surface;
            ElementComposition.SetElementChildVisual(this, surfaceVisual);
            _compositor = compositor;
            _surface = surface;
            _surfaceVisual = surfaceVisual;
        }).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (interop is null || compositor is null || surface is null || surfaceVisual is null)
        {
            await SetStatusAsync(
                "GPU composition unavailable: compositor interop is unsupported")
                .ConfigureAwait(false);
            return false;
        }

        ICompositionViewportPresenter presenter = factory() ??
            throw new InvalidOperationException("The presenter factory returned no presenter.");
        var bridge = new AvaloniaCompositionSurfaceBridge(
            surface,
            interop,
            _dispatcher,
            _runtimeObservation.ObserveImport,
            _runtimeObservation.ObservePresent);
        var session = new CompositionViewportSession(presenter, bridge, _dispatcher);
        session.StateChanged += OnSessionStateChanged;
        _session = session;
        var target = new CompositionPresentationTarget(
            interop.SupportedImageHandleTypes,
            interop.SupportedSemaphoreTypes,
            interop.DeviceLuid,
            interop.DeviceUuid);
        _runtimeObservation.ObserveTarget(BackendKind, target);
        _pixelSize = await GetPixelSizeAsync().ConfigureAwait(false);
        bool attached = await session.AttachAsync(
            target,
            _pixelSize,
            cancellationToken).ConfigureAwait(false);
        if (attached)
        {
            QueueCompositionUpdate();
        }
        else
        {
            await TeardownCoreAsync().ConfigureAwait(false);
        }
        return attached;
    }

    private async Task TeardownAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await TeardownCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task TeardownDetachedAsync()
    {
        await _pump.WaitForIdleAsync().ConfigureAwait(false);
        await TeardownAsync().ConfigureAwait(false);
        DisposeRetiredAttachments();
    }

    private async Task TeardownCoreAsync()
    {
        CompositionViewportSession? session = _session;
        CompositionDrawingSurface? surface = _surface;
        _session = null;
        _surface = null;
        _surfaceVisual = null;
        _compositor = null;
        _pixelSize = ViewportDimensions.Empty;
        _updateQueued = false;

        var failures = new List<Exception>();
        try
        {
            await _dispatcher.InvokeAsync(() =>
            {
                ElementComposition.SetElementChildVisual(this, null);
                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        if (session is not null)
        {
            session.StateChanged -= OnSessionStateChanged;
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
        if (surface is not null)
        {
            try
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    surface.Dispose();
                    return ValueTask.CompletedTask;
                }).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
        if (failures.Count != 0)
        {
            throw new AggregateException(
                "One or more composition host resources failed to tear down.",
                failures);
        }
    }

    private void OnCompositionUpdate()
    {
        _updateQueued = false;
        _surfaceVisual?.Size = new Vector(Bounds.Width, Bounds.Height);
        _pump.Request();
    }

    private async ValueTask<CompositionPresentOutcome> PumpIterationAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            CompositionPresentOutcome outcome = await PumpIterationCoreAsync(cancellationToken)
                .ConfigureAwait(false);
            Interlocked.Exchange(ref _nextPresentation, null)?.TrySetResult(outcome);
            return outcome;
        }
        catch (Exception exception)
        {
            Interlocked.Exchange(ref _nextPresentation, null)?.TrySetException(exception);
            throw;
        }
    }

    private async ValueTask<CompositionPresentOutcome> PumpIterationCoreAsync(
        CancellationToken cancellationToken)
    {
        CompositionViewportSession? session = _session;
        CancellationTokenSource? attachment = _attachment;
        if (session is null || attachment is null)
        {
            return new CompositionPresentOutcome(CompositionPresentResult.Detached, false);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            attachment.Token);
        ViewportDimensions size = await GetPixelSizeAsync().ConfigureAwait(false);
        if (size != _pixelSize)
        {
            await session.ResizeAsync(size, linked.Token).ConfigureAwait(false);
            _pixelSize = size;
        }

        CompositionPresentOutcome outcome =
            await session.PresentNextFrameAsync(linked.Token).ConfigureAwait(false);
        if (outcome.Result == CompositionPresentResult.Lost)
        {
            if (!ManagerControlsDeviceLoss)
            {
                outcome = await RecoverFromLossAsync(linked.Token).ConfigureAwait(false);
            }
        }
        if (outcome.Result == CompositionPresentResult.Presented)
        {
            _recovery.Reset();
        }
        return outcome;
    }

    private async ValueTask<CompositionPresentOutcome> RecoverFromLossAsync(
        CancellationToken cancellationToken)
    {
        bool recovered;
        try
        {
            recovered = await _recovery.TryRecoverAsync(
                async token =>
                {
                    await SetStatusAsync(
                        $"GPU composition: recovery attempt {_recovery.Attempts}")
                        .ConfigureAwait(false);
                    await TeardownAsync().ConfigureAwait(false);
                    return await InitializeAsync(token).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _tasks.Report("composition loss recovery", exception);
            recovered = false;
        }

        if (!recovered && _recovery.IsExhausted)
        {
            await SetStatusAsync(
                "GPU composition unavailable: persistent device/compositor loss")
                .ConfigureAwait(false);
            return new CompositionPresentOutcome(CompositionPresentResult.Lost, false);
        }
        return new CompositionPresentOutcome(
            recovered ? CompositionPresentResult.Idle : CompositionPresentResult.Lost,
            ContinueRendering: recovered || !_recovery.IsExhausted);
    }

    private async Task ReinitializeCoreAsync()
    {
        CancellationToken token = _attachment?.Token ?? CancellationToken.None;
        await CompositionControlDisposal.DisposeAttachedAsync(
            _dispatcher,
            () => ElementComposition.SetElementChildVisual(this, null),
            async () =>
            {
                await TeardownAsync().ConfigureAwait(false);
                if (_attachment is not null && !token.IsCancellationRequested)
                {
                    _recovery.Reset();
                    await InitializeAsync(token).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();
        try
        {
            CancelAttachment();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        try
        {
            await CompositionControlDisposal.DisposeAttachedAsync(
                _dispatcher,
                () => ElementComposition.SetElementChildVisual(this, null),
                DisposeHostResourcesAsync).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        if (failures.Count != 0)
        {
            throw new AggregateException(
                "One or more composition host disposal operations failed.",
                failures);
        }
    }

    private async ValueTask DisposeHostResourcesAsync()
    {
        var failures = new List<Exception>();
        try
        {
            await _pump.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        try
        {
            await TeardownAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        try
        {
            DisposeRetiredAttachments();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        if (failures.Count != 0)
        {
            throw new AggregateException(
                "One or more composition host resources failed to dispose.",
                failures);
        }
    }

    private async Task DisposeOnceAsync()
    {
        Exception? failure = null;
        try
        {
            await DisposeCoreAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
            _tasks.Report("composition host disposal", exception);
        }

        await _tasks.DrainAsync().ConfigureAwait(false);
        _lifecycleGate.Dispose();
        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private void QueueCompositionUpdate()
    {
        if (_disposed)
        {
            return;
        }
        if (!_dispatcher.CheckAccess())
        {
            Dispatcher.UIThread.Post(QueueCompositionUpdate);
            return;
        }
        if (_updateQueued || _compositor is null || _attachment is null)
        {
            return;
        }
        _updateQueued = true;
        _compositor.RequestCompositionUpdate(_compositionUpdate);
    }

    private async ValueTask<ViewportDimensions> GetPixelSizeAsync()
    {
        ViewportDimensions result = ViewportDimensions.Empty;
        await _dispatcher.InvokeAsync(() =>
        {
            double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            result = ViewportPixelMath.ToPixels(Bounds.Width, Bounds.Height, scaling);
            return ValueTask.CompletedTask;
        }).ConfigureAwait(false);
        return result;
    }

    private async ValueTask SetStatusAsync(string status)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            SetStatus(status);
            return ValueTask.CompletedTask;
        }).ConfigureAwait(false);
    }

    private void OnSessionStateChanged(object? sender, EventArgs e)
    {
        if (sender is CompositionViewportSession session)
        {
            SetStatus(session.Status);
        }
    }

    private void OnBackgroundFailure(string operation, Exception exception)
    {
        string status = $"GPU composition {operation} failed: {exception.Message}";
        ViewerStartupOptions.WriteStatus(status);
        Trace.TraceError("{0}{1}{2}", status, Environment.NewLine, exception);
        Dispatcher.UIThread.Post(() => SetStatus(status));
    }

    private void SetStatus(string status)
    {
        if (string.Equals(Status, status, StringComparison.Ordinal))
        {
            return;
        }
        Status = status;
        ViewerStartupOptions.WriteStatus(status);
        StatusChanged?.Invoke(this, status);
    }

    private void CancelAttachment()
    {
        CancellationTokenSource? attachment = _attachment;
        _attachment = null;
        if (attachment is not null)
        {
            attachment.Cancel();
            _retiredAttachments.Enqueue(attachment);
        }
        _topLevel?.ScalingChanged -= OnScalingChanged;
        _topLevel = null;
    }

    private void DisposeRetiredAttachments()
    {
        while (_retiredAttachments.TryDequeue(out CancellationTokenSource? attachment))
        {
            attachment.Dispose();
        }
    }

    private void OnScalingChanged(object? sender, EventArgs e)
    {
        QueueCompositionUpdate();
    }
}
