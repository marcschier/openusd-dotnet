// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Captures successive hdSilk frames from one session, retaining the scene between captures.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OpenUsdSilkSession.Sync"/> reports only what changed since the previous
/// synchronization, so the first page carries the whole scene and later pages carry deltas.
/// A capturer therefore has to keep its renderer - and with it the retained scene - alive
/// across captures. The one-shot <see cref="SilkFrameCapture"/> helper
/// builds a renderer per call and so can only serve a session that has never been
/// synchronized; use this type for a render loop, a camera sweep, or anything else that
/// captures more than once.
/// </para>
/// </remarks>
public sealed class SilkFrameCapturer : IDisposable
{
    private readonly ISilkGraphicsDevice _device;
    private readonly object _gate = new();
    private readonly SilkMeshRenderer _renderer;
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
            _renderer.Dispose();
        }
    }
}
