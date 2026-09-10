// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Captures successive hdSilk frames from one session, retaining the scene between captures.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OpenUsdSilkSession.Sync(int, int, double, CameraState, RenderComplexity, RenderDrawMode)"/> reports only what changed since the previous
/// synchronization, so the first page carries the whole scene and later pages carry deltas.
/// A capturer therefore has to keep its renderer - and with it the retained scene - alive
/// across captures. The one-shot <see cref="SilkFrameCapture"/> helper
/// builds a renderer per call and so can only serve a session that has never been
/// synchronized; use this type for a render loop, a camera sweep, or anything else that
/// captures more than once.
/// </para>
/// </remarks>
public sealed partial class SilkFrameCapturer : IDisposable
{
    private readonly ISilkGraphicsDevice _device;
    private readonly object _gate = new();
    private readonly SilkMeshRenderer _renderer;
    private WeakReference<OpenUsdSilkSession>? _capturedSession;
    private bool _depthSceneComplete;
    private bool _disposed;

    /// <summary>
    /// Creates a capturer that renders through <paramref name="device"/>.
    /// </summary>
    /// <param name="device">The graphics device that renders and reads back frames.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <c>null</c>.</exception>
    public SilkFrameCapturer(ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _renderer = new SilkMeshRenderer(device);
    }

    /// <summary>
    /// Synchronizes, renders, and captures one RGBA8 frame with default render settings.
    /// </summary>
    public SilkFrameCaptureResult Capture(
        OpenUsdSilkSession session,
        int width,
        int height,
        double timeCode = 0,
        CameraState camera = default) =>
        Capture(session, width, height, RenderSettings.Default, timeCode, camera);

    /// <summary>
    /// Synchronizes, renders, and captures one RGBA8 frame.
    /// </summary>
    /// <param name="session">The session whose retained scene is rendered.</param>
    /// <param name="width">The capture width in pixels.</param>
    /// <param name="height">The capture height in pixels.</param>
    /// <param name="renderSettings">The render settings applied to the capture.</param>
    /// <param name="timeCode">The stage time code to synchronize.</param>
    /// <param name="camera">The camera the frame is rendered from.</param>
    /// <returns>The captured frame.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="session"/> or <paramref name="renderSettings"/> is <c>null</c>.
    /// </exception>
    public SilkFrameCaptureResult Capture(
        OpenUsdSilkSession session,
        int width,
        int height,
        RenderSettings renderSettings,
        double timeCode = 0,
        CameraState camera = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ObserveCaptureSession(session);
            return SilkFrameCapture.CaptureCore(
                session,
                _device,
                _renderer,
                width,
                height,
                renderSettings,
                timeCode,
                camera);
        }
    }

    /// <summary>
    /// Synchronizes and captures RGBA8 color together with the actual normalized
    /// device-depth attachment, retaining geometry across captures.
    /// </summary>
    /// <remarks>
    /// Existing color-only captures on this capturer and captures with depth share
    /// one renderer. A quota failure or pre-cancellation consumes no session delta.
    /// Once synchronization starts, its delta is retained and submitted work completes
    /// before cancellation can be reported. Use
    /// <see cref="SilkFrameCapture"/>'s retained-depth API instead when another
    /// renderer already owns the session's synchronized geometry.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The dimensions or capture quotas are exceeded.</exception>
    /// <exception cref="InvalidOperationException">
    /// The session was already synchronized before this capturer first used it, or
    /// the capturer has consumed more than one session. Its retained scene cannot be
    /// established as complete; capture from the original renderer instead.
    /// </exception>
    public SilkFrameCaptureResult CaptureWithDepth(
        OpenUsdSilkSession session,
        int width,
        int height,
        RenderSettings renderSettings,
        SilkDepthCaptureOptions? depthOptions = null,
        double timeCode = 0,
        CameraState camera = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var request = new SilkDepthCaptureRequest(width, height, depthOptions, cancellationToken);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ValidateDepthCaptureSession(session);
            ObserveCaptureSession(session);
            return SilkFrameCapture.CaptureCore(
                session,
                _device,
                _renderer,
                width,
                height,
                renderSettings,
                timeCode,
                camera,
                request);
        }
    }

    /// <summary>
    /// Synchronizes and captures OCIO-transformed RGBA8 color together with the
    /// actual normalized device-depth attachment, retaining geometry between calls.
    /// </summary>
    /// <remarks>
    /// The processor transforms color only. Limits, cancellation, and session
    /// ownership requirements match capture with built-in color output.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The complete session scene is not retained by this capturer, or a built-in
    /// output transform or GPU display transform is also requested.
    /// </exception>
    public SilkFrameCaptureResult CaptureWithDepth(
        OpenUsdSilkSession session,
        int width,
        int height,
        RenderSettings renderSettings,
        SilkOpenColorIoProcessor ocioProcessor,
        SilkDepthCaptureOptions? depthOptions = null,
        double timeCode = 0,
        CameraState camera = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(ocioProcessor);
        var request = new SilkDepthCaptureRequest(width, height, depthOptions, cancellationToken);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ValidateDepthCaptureSession(session);
            ObserveCaptureSession(session);
            return SilkFrameCapture.CaptureCoreOcio(
                session,
                _device,
                _renderer,
                width,
                height,
                renderSettings,
                ocioProcessor,
                timeCode,
                camera,
                request);
        }
    }

    /// <summary>
    /// Synchronizes, renders, and captures one RGBA8 frame using an OpenColorIO processor
    /// for display-referred output.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="renderSettings"/> specifies a non-Identity
    /// <see cref="RenderOutputTransform"/> alongside an OCIO processor.
    /// </exception>
    public SilkFrameCaptureResult Capture(
        OpenUsdSilkSession session,
        int width,
        int height,
        RenderSettings renderSettings,
        SilkOpenColorIoProcessor ocioProcessor,
        double timeCode = 0,
        CameraState camera = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(ocioProcessor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ObserveCaptureSession(session);
            return SilkFrameCapture.CaptureCoreOcio(
                session,
                _device,
                _renderer,
                width,
                height,
                renderSettings,
                ocioProcessor,
                timeCode,
                camera);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _capturedSession = null;
            _renderer.Dispose();
        }
    }

    private void ObserveCaptureSession(OpenUsdSilkSession session)
    {
        if (_capturedSession is null)
        {
            _depthSceneComplete = !session.HasSynchronized;
            // Continuity tracking must not extend the caller's native session lifetime.
            _capturedSession = new WeakReference<OpenUsdSilkSession>(session);
        }
        else if (!MatchesCapturedSession(session))
        {
            _depthSceneComplete = false;
        }
    }

    private void ValidateDepthCaptureSession(OpenUsdSilkSession session)
    {
        if (_capturedSession is null
            ? session.HasSynchronized
            : !_depthSceneComplete || !MatchesCapturedSession(session))
        {
            throw new InvalidOperationException(
                "Depth capture requires the renderer that retained the session's complete scene. " +
                "Use SilkFrameCapture.CaptureRetainedWithDepth with that renderer, or start " +
                "a new session with a new SilkFrameCapturer.");
        }
    }

    private bool MatchesCapturedSession(OpenUsdSilkSession session) =>
        _capturedSession is not null &&
        _capturedSession.TryGetTarget(out OpenUsdSilkSession? captured) &&
        ReferenceEquals(captured, session);
}
