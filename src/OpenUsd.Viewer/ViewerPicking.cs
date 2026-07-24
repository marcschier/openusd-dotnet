// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using Avalonia.Input;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Viewer;

internal readonly record struct ViewerPhysicalPixel
{
    internal ViewerPhysicalPixel(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        X = x;
        Y = y;
    }

    internal int X { get; }

    internal int Y { get; }
}

internal readonly record struct ViewerLogicalContentBounds(
    double X,
    double Y,
    double Width,
    double Height);

internal static class ViewerPickPixelMapper
{
    internal static bool TryMap(
        double logicalX,
        double logicalY,
        in ViewerLogicalContentBounds contentBounds,
        double renderScaling,
        ViewportDimensions viewport,
        out ViewerPhysicalPixel pixel)
    {
        if (!double.IsFinite(logicalX) ||
            !double.IsFinite(logicalY) ||
            !double.IsFinite(contentBounds.X) ||
            !double.IsFinite(contentBounds.Y) ||
            !double.IsFinite(contentBounds.Width) ||
            !double.IsFinite(contentBounds.Height) ||
            !double.IsFinite(renderScaling))
        {
            throw new ArgumentOutOfRangeException(nameof(logicalX));
        }
        if (contentBounds.Width <= 0 ||
            contentBounds.Height <= 0 ||
            renderScaling <= 0 ||
            viewport.Width <= 0 ||
            viewport.Height <= 0)
        {
            pixel = default;
            return false;
        }

        double relativeX = logicalX - contentBounds.X;
        double relativeY = logicalY - contentBounds.Y;
        if (relativeX < 0 ||
            relativeY < 0 ||
            relativeX >= contentBounds.Width ||
            relativeY >= contentBounds.Height)
        {
            pixel = default;
            return false;
        }

        double scaledX = Math.Floor(relativeX * renderScaling);
        double scaledY = Math.Floor(relativeY * renderScaling);
        if (scaledX < 0 ||
            scaledY < 0 ||
            scaledX >= viewport.Width ||
            scaledY >= viewport.Height)
        {
            pixel = default;
            return false;
        }

        pixel = new ViewerPhysicalPixel((int)scaledX, (int)scaledY);
        return true;
    }
}

internal static class ViewerPickGestureClassifier
{
    internal const double DragThresholdPhysicalPixels = 4;

    internal static bool CanStart(
        KeyModifiers modifiers,
        ViewerPointerButtons buttons) =>
        modifiers == KeyModifiers.None &&
        buttons == ViewerPointerButtons.Left;

    internal static bool IsDrag(
        double logicalDeltaX,
        double logicalDeltaY,
        double renderScaling)
    {
        if (!double.IsFinite(logicalDeltaX) ||
            !double.IsFinite(logicalDeltaY) ||
            !double.IsFinite(renderScaling) ||
            renderScaling <= 0)
        {
            return true;
        }

        double physicalX = logicalDeltaX * renderScaling;
        double physicalY = logicalDeltaY * renderScaling;
        double thresholdSquared =
            DragThresholdPhysicalPixels * DragThresholdPhysicalPixels;
        return (physicalX * physicalX) + (physicalY * physicalY) >= thresholdSquared;
    }
}

internal sealed class ViewerStormPickInputTracker
{
    private OpenUsdStormNavigationInput _previous;
    private int _originX;
    private int _originY;
    private bool _active;
    private bool _hasBaseline;
    private bool _isDrag;

    internal void Reset()
    {
        _previous = default;
        _originX = 0;
        _originY = 0;
        _active = false;
        _hasBaseline = false;
        _isDrag = false;
    }

    internal bool TryUpdate(
        in OpenUsdStormNavigationInput current,
        out ViewerPhysicalPixel pixel)
    {
        if (!_hasBaseline)
        {
            _previous = current;
            _hasBaseline = true;
            if (current.Buttons == OpenUsdStormPointerButtons.Left &&
            current.Modifiers == OpenUsdStormInputModifiers.None &&
            current.Focused)
            {
                _originX = current.PointerX;
                _originY = current.PointerY;
                _active = true;
                _isDrag = false;
            }
            pixel = default;
            return false;
        }

        OpenUsdStormNavigationInput previous = _previous;
        _previous = current;
        bool previousLeft =
            (previous.Buttons & OpenUsdStormPointerButtons.Left) != 0;
        bool currentLeft =
            (current.Buttons & OpenUsdStormPointerButtons.Left) != 0;
        bool currentPlainLeft =
            current.Buttons == OpenUsdStormPointerButtons.Left &&
            current.Modifiers == OpenUsdStormInputModifiers.None;
        if (!_active && !previousLeft && currentPlainLeft &&
            current.Focused)
        {
            _originX = current.PointerX;
            _originY = current.PointerY;
            _active = true;
            _isDrag = false;
            pixel = default;
            return false;
        }
        if (!_active)
        {
            pixel = default;
            return false;
        }

        if (!current.Focused ||
            current.Modifiers != OpenUsdStormInputModifiers.None ||
            (currentLeft && current.Buttons != OpenUsdStormPointerButtons.Left))
        {
            _active = false;
            _isDrag = false;
            pixel = default;
            return false;
        }
        if (currentLeft)
        {
            _isDrag |= ViewerPickGestureClassifier.IsDrag(
                (double)current.PointerX - _originX,
                (double)current.PointerY - _originY,
                renderScaling: 1);
            pixel = default;
            return false;
        }

        bool isClick = !_isDrag &&
            current.Inside &&
            current.PointerX >= 0 &&
            current.PointerY >= 0;
        _active = false;
        _isDrag = false;
        if (!isClick)
        {
            pixel = default;
            return false;
        }

        pixel = new ViewerPhysicalPixel(current.PointerX, current.PointerY);
        return true;
    }
}

internal static class ViewerPickingPolicy
{
    internal static Vector4 StormSelectionColor { get; } = new(1, 1, 0, 1);
}

internal sealed record ViewerRenderedPickState(
    StageRenderState State,
    ulong? SceneRevision,
    RenderBackendKind BackendKind);

internal interface IViewerRenderedPickStateSource
{
    ViewerRenderedPickState? LastRenderedPickState { get; }
}

internal static class ViewerRenderedPickStateStore
{
    internal static void PublishNewest(
        ref ViewerRenderedPickState? location,
        ViewerRenderedPickState value)
    {
        ArgumentNullException.ThrowIfNull(value);
        while (true)
        {
            ViewerRenderedPickState? current = Volatile.Read(ref location);
            if (current is not null &&
                current.State.Revision >= value.State.Revision)
            {
                return;
            }
            if (ReferenceEquals(
                    Interlocked.CompareExchange(ref location, value, current),
                    current))
            {
                return;
            }
        }
    }
}

internal readonly record struct ViewerPickBackendSnapshot(
    IRenderPickingBackend Backend,
    ViewerRenderedPickState? RenderedState,
    long Generation);

internal readonly record struct ViewerPickingStatistics(
    long Requests,
    long SupersededRequests,
    long BackendCalls,
    long StaleRetries);

internal sealed class ViewerPickOperationQueue : IDisposable
{
    private readonly Func<ViewerPickBackendSnapshot?> _captureBackend;
    private readonly Func<StageRenderState> _captureCurrentState;
    private readonly CancellationToken _lifetime;
    private readonly object _latestGate = new();
    private readonly SemaphoreSlim _serialized = new(1, 1);
    private CancellationTokenSource? _latest;
    private long _backendCalls;
    private long _requests;
    private long _staleRetries;
    private long _supersededRequests;
    private int _disposed;

    internal ViewerPickOperationQueue(
        Func<ViewerPickBackendSnapshot?> captureBackend,
        Func<StageRenderState> captureCurrentState,
        CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(captureBackend);
        ArgumentNullException.ThrowIfNull(captureCurrentState);
        _captureBackend = captureBackend;
        _captureCurrentState = captureCurrentState;
        _lifetime = lifetime;
    }

    internal ViewerPickingStatistics Statistics => new(
        Interlocked.Read(ref _requests),
        Interlocked.Read(ref _supersededRequests),
        Interlocked.Read(ref _backendCalls),
        Interlocked.Read(ref _staleRetries));

    /// <summary>
    /// Serializes picks and coalesces waiting or in-flight work so the latest request wins.
    /// </summary>
    /// <remarks>
    /// Superseding a request cancels its token before backend admission when possible. Once a
    /// native call has been admitted it may finish, but cancellation is checked again after the
    /// backend returns and its result is suppressed.
    /// </remarks>
    internal async ValueTask<RenderPickResult> PickAsync(
        ViewerPhysicalPixel pixel,
        RenderPickTarget target = RenderPickTarget.Primitive,
        RenderPickOptions options = RenderPickOptions.None,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        Interlocked.Increment(ref _requests);
        using var requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetime);
        CancellationTokenSource? superseded;
        lock (_latestGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            superseded = _latest;
            _latest = requestLifetime;
        }
        if (superseded is not null)
        {
            Interlocked.Increment(ref _supersededRequests);
            CancelIfActive(superseded);
        }

        bool entered = false;
        try
        {
            await _serialized.WaitAsync(requestLifetime.Token).ConfigureAwait(false);
            entered = true;
            return await PickCoreAsync(
                pixel,
                target,
                options,
                requestLifetime.Token).ConfigureAwait(false);
        }
        finally
        {
            if (entered)
            {
                _serialized.Release();
            }
            lock (_latestGate)
            {
                if (ReferenceEquals(_latest, requestLifetime))
                {
                    _latest = null;
                }
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        CancellationTokenSource? latest;
        lock (_latestGate)
        {
            latest = _latest;
            _latest = null;
        }
        if (latest is not null)
        {
            CancelIfActive(latest);
        }
    }

    private static void CancelIfActive(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async ValueTask<RenderPickResult> PickCoreAsync(
        ViewerPhysicalPixel pixel,
        RenderPickTarget target,
        RenderPickOptions options,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ViewerPickBackendSnapshot? optionalSnapshot = _captureBackend();
            if (optionalSnapshot is not { } snapshot)
            {
                RenderPickRequest unavailableRequest = CreateRequest(
                    pixel,
                    _captureCurrentState(),
                    sceneRevision: null,
                    target,
                    options);
                return RenderPickResult.Unsupported(
                    unavailableRequest,
                    unavailableRequest.RequestedStateRevision,
                    unavailableRequest.RequestedSceneRevision);
            }
            if (snapshot.RenderedState is not { } rendered)
            {
                StageRenderState current = _captureCurrentState();
                RenderPickRequest pendingRequest = CreateRequest(
                    pixel,
                    current,
                    sceneRevision: null,
                    target,
                    options);
                return RenderPickResult.Stale(
                    pendingRequest,
                    current.Revision,
                    sceneRevision: null,
                    RenderPickStaleReason.BackendState);
            }

            RenderPickRequest request = CreateRequest(
                pixel,
                rendered.State,
                rendered.SceneRevision,
                target,
                options);
            Interlocked.Increment(ref _backendCalls);
            RenderPickResult result;
            try
            {
                result = await snapshot.Backend
                    .PickAsync(request, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception) when (
                !cancellationToken.IsCancellationRequested &&
                BackendChanged(snapshot))
            {
                result = RenderPickResult.Stale(
                    request,
                    rendered.State.Revision,
                    rendered.SceneRevision,
                    RenderPickStaleReason.BackendState);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Request != request)
            {
                throw new InvalidDataException(
                    "The Viewer picking backend returned a result for a different request.");
            }
            if (BackendChanged(snapshot) &&
                result.Status != RenderPickStatus.Stale)
            {
                result = RenderPickResult.Stale(
                    request,
                    rendered.State.Revision,
                    rendered.SceneRevision,
                    RenderPickStaleReason.BackendState);
            }
            if (result.Status != RenderPickStatus.Stale || attempt != 0)
            {
                return result;
            }

            Interlocked.Increment(ref _staleRetries);
            await WaitForNewestRenderedStateAsync(
                snapshot,
                result,
                cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException("The bounded picking retry loop did not return.");
    }

    private bool BackendChanged(in ViewerPickBackendSnapshot admitted)
    {
        ViewerPickBackendSnapshot? current = _captureBackend();
        return current is not { } snapshot ||
            snapshot.Generation != admitted.Generation ||
            !ReferenceEquals(snapshot.Backend, admitted.Backend);
    }

    private async ValueTask WaitForNewestRenderedStateAsync(
        ViewerPickBackendSnapshot admitted,
        RenderPickResult stale,
        CancellationToken cancellationToken)
    {
        ViewerRenderedPickState? admittedState = admitted.RenderedState;
        if (admittedState is null ||
            (stale.StateRevision == admittedState.State.Revision &&
             stale.SceneRevision == admittedState.SceneRevision))
        {
            return;
        }

        int stableObservations = 0;
        for (int attempt = 0; attempt < 128; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ViewerPickBackendSnapshot? current = _captureBackend();
            if (current is not { } snapshot ||
                snapshot.Generation != admitted.Generation ||
                !ReferenceEquals(snapshot.Backend, admitted.Backend))
            {
                return;
            }
            if (snapshot.RenderedState is { } rendered &&
                rendered.State.Revision == stale.StateRevision &&
                rendered.SceneRevision == stale.SceneRevision)
            {
                stableObservations++;
                if (stableObservations == 8)
                {
                    return;
                }
            }
            else
            {
                stableObservations = 0;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(4), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static RenderPickRequest CreateRequest(
        ViewerPhysicalPixel pixel,
        StageRenderState state,
        ulong? sceneRevision,
        RenderPickTarget target,
        RenderPickOptions options)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new RenderPickRequest(
            pixel.X,
            pixel.Y,
            state.Viewport,
            state.Revision,
            sceneRevision,
            target,
            options);
    }
}
