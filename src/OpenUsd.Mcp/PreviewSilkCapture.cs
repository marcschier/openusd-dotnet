// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.ExceptionServices;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Mcp;

public interface IPreviewRenderSourceProvider
{
    ValueTask<UsdStageRenderSource> AcquireRenderSourceAsync(
        CancellationToken cancellationToken = default);
}

public sealed class PreviewSilkFrameSourceFactory(
    string pluginPath,
    IPreviewRenderSourceProvider renderSourceProvider,
    IPreviewGraphicsDeviceFactory graphicsDeviceFactory,
    PreviewGraphicsDeviceOptions graphicsOptions) : IPreviewFrameSourceFactory
{
    private readonly string _pluginPath = ValidatePluginPath(pluginPath);
    private readonly IPreviewRenderSourceProvider _renderSourceProvider =
        renderSourceProvider ?? throw new ArgumentNullException(nameof(renderSourceProvider));
    private readonly IPreviewGraphicsDeviceFactory _graphicsDeviceFactory =
        graphicsDeviceFactory ?? throw new ArgumentNullException(nameof(graphicsDeviceFactory));
    private readonly PreviewGraphicsDeviceOptions _graphicsOptions =
        graphicsOptions ?? throw new ArgumentNullException(nameof(graphicsOptions));

    public IPreviewFrameSource Create(
        PreviewCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CreateCore(
            () => _renderSourceProvider
                .AcquireRenderSourceAsync(cancellationToken)
                .AsTask()
                .GetAwaiter()
                .GetResult()
                ?? throw new InvalidOperationException("The stage source factory returned null."),
            () => _graphicsDeviceFactory.Create(_graphicsOptions),
            source => OpenUsdSilkRuntime.Create(_pluginPath, source),
            device => new SilkFrameCapturer(device),
            static (source, device, session, capturer) =>
                new PreviewSilkFrameSource(source, device, session, capturer));
    }

    internal static PreviewSilkFrameSource CreateCore<
        TSource,
        TDevice,
        TSession,
        TCapturer>(
        Func<TSource> acquireSource,
        Func<TDevice> createDevice,
        Func<TSource, TSession> createSession,
        Func<TDevice, TCapturer> createCapturer,
        Func<TSource, TDevice, TSession, TCapturer, PreviewSilkFrameSource> createFrameSource)
        where TSource : class, IDisposable
        where TDevice : class, IDisposable
        where TSession : class, IDisposable
        where TCapturer : class, IDisposable
    {
        ArgumentNullException.ThrowIfNull(acquireSource);
        ArgumentNullException.ThrowIfNull(createDevice);
        ArgumentNullException.ThrowIfNull(createSession);
        ArgumentNullException.ThrowIfNull(createCapturer);
        ArgumentNullException.ThrowIfNull(createFrameSource);

        TSource? source = null;
        TDevice? device = null;
        TSession? session = null;
        TCapturer? capturer = null;
        try
        {
            source = acquireSource()
                ?? throw new InvalidOperationException("The stage source factory returned null.");
            device = createDevice()
                ?? throw new InvalidOperationException("The graphics device factory returned null.");
            session = createSession(source)
                ?? throw new InvalidOperationException("The Silk session factory returned null.");
            capturer = createCapturer(device)
                ?? throw new InvalidOperationException("The frame capturer factory returned null.");
            return createFrameSource(source, device, session, capturer)
                ?? throw new InvalidOperationException("The frame source factory returned null.");
        }
        catch (Exception constructionFailure)
        {
            var cleanupFailures = new List<Exception>();
            PreviewResourceCleanup.TryDispose(ref capturer, cleanupFailures);
            PreviewResourceCleanup.TryDispose(ref session, cleanupFailures);
            PreviewResourceCleanup.TryDispose(ref device, cleanupFailures);
            PreviewResourceCleanup.TryDispose(ref source, cleanupFailures);
            if (cleanupFailures.Count == 0)
            {
                ExceptionDispatchInfo.Capture(constructionFailure).Throw();
            }

            cleanupFailures.Insert(0, constructionFailure);
            throw new AggregateException(
                "Silk preview construction failed and one or more resources could not be released.",
                cleanupFailures);
        }
    }

    private static string ValidatePluginPath(string pluginPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginPath);
        return pluginPath;
    }
}

internal sealed class PreviewSilkFrameSource : IPreviewFrameSource
{
    private readonly Func<CaptureView, int, int, ImageRgba8> _capture;
    private IDisposable? _capturer;
    private IDisposable? _device;
    private IDisposable? _session;
    private IDisposable? _source;
    private bool _teardownStarted;

    internal PreviewSilkFrameSource(
        UsdStageRenderSource source,
        ISilkGraphicsDevice device,
        OpenUsdSilkSession session,
        SilkFrameCapturer capturer)
        : this(
            (view, width, height) =>
            {
                SilkFrameCaptureResult result = capturer.Capture(
                    session,
                    width,
                    height,
                    view.TimeCode,
                    view.Camera);
                return new ImageRgba8(result.Width, result.Height, result.Rgba.Span);
            },
            capturer,
            session,
            device,
            source)
    {
    }

    internal PreviewSilkFrameSource(
        Func<CaptureView, int, int, ImageRgba8> capture,
        IDisposable capturer,
        IDisposable session,
        IDisposable device,
        IDisposable source)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _capturer = capturer ?? throw new ArgumentNullException(nameof(capturer));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public ImageRgba8 Capture(CaptureView view, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(view);
        ObjectDisposedException.ThrowIf(_teardownStarted, this);
        return _capture(view, width, height);
    }

    public void Dispose()
    {
        _teardownStarted = true;
        var failures = new List<Exception>();
        PreviewResourceCleanup.TryDispose(ref _capturer, failures);

        // The session owns an independent stage lease. Release it before the source registration.
        PreviewResourceCleanup.TryDispose(ref _session, failures);
        PreviewResourceCleanup.TryDispose(ref _device, failures);
        PreviewResourceCleanup.TryDispose(ref _source, failures);
        if (failures.Count != 0)
        {
            throw new AggregateException(
                "One or more Silk preview resources remain owned for cleanup retry.",
                failures);
        }
    }
}

internal static class PreviewResourceCleanup
{
    internal static void TryDispose<T>(ref T? resource, List<Exception> failures)
        where T : class, IDisposable
    {
        T? current = resource;
        if (current is null)
        {
            return;
        }

        try
        {
            current.Dispose();
            resource = null;
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }
}
