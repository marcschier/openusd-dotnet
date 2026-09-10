// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.ExceptionServices;
using OpenUsd.Rendering;
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

internal sealed class PreviewSilkFrameSource
    : IPreviewFrameSource, IPreviewDiagnosticSource, IPreviewDepthFrameSource, IPreviewHdrFrameSource,
        IPreviewProductFrameSource
{
    private readonly Func<CaptureView, int, int, CapturedFrame> _capture;
    private readonly Func<CaptureView, int, int, long, CancellationToken, RenderJobImage>? _captureDepth;
    private readonly Func<CaptureView, int, int, long, bool, CancellationToken, RenderJobImage>? _captureHdr;
    private readonly Action<RenderProductJobPlan, CancellationToken>? _validateProduct;
    private readonly Func<RenderProductJobPlan, StageRenderState, long, CancellationToken, RenderJobImage>? _captureProduct;
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
                    RenderSettings.PresentationDefault,
                    view.TimeCode,
                    view.Camera);
                return new CapturedFrame(
                    new ImageRgba8(result.Width, result.Height, result.Rgba.Span),
                    result.Diagnostics);
            },
            capturer,
            session,
            device,
            source)
    {
        _captureDepth = (view, width, height, maximumBytes, cancellationToken) =>
        {
            SilkFrameCaptureResult result = capturer.CaptureWithDepth(
                session, width, height, RenderSettings.PresentationDefault,
                new SilkDepthCaptureOptions(maximumReadbackBytes: maximumBytes),
                view.TimeCode, view.Camera, cancellationToken);
            SilkDepthCaptureResult depth = result.Depth ??
                throw new InvalidDataException("The depth capture returned no depth plane.");
            if (depth.Convention != SilkDepthConvention.NormalizedDeviceDepthZeroToOne)
            {
                throw new NotSupportedException("The depth convention cannot be represented by this disk job.");
            }
            return new RenderJobImage(result.Width, result.Height, result.Rgba, Rgba8RowOrder.TopDown)
            {
                DeviceDepth = new RenderJobDeviceDepth(depth.Width, depth.Height, depth.Values),
                Diagnostics = result.Diagnostics
            };
        };
        _captureHdr = (view, width, height, maximumBytes, includeDepth, cancellationToken) =>
        {
            SilkFrameCaptureResult result = capturer.CaptureWithHdrColor(
                session, width, height, RenderSettings.PresentationDefault,
                new SilkHdrColorCaptureOptions(includeDepth, maximumReadbackBytes: maximumBytes),
                view.TimeCode, view.Camera, cancellationToken);
            SilkHdrColorCaptureResult hdr = result.HdrColor ??
                throw new InvalidDataException("The HDR capture returned no HDR plane.");
            if (hdr.Convention != SilkHdrColorConvention.RendererWorkingCompositedBeforeExposureAndDisplay)
            {
                throw new NotSupportedException("The HDR convention cannot be represented by this disk job.");
            }
            if (result.Depth is { } depth &&
                depth.Convention != SilkDepthConvention.NormalizedDeviceDepthZeroToOne)
            {
                throw new NotSupportedException("The depth convention cannot be represented by this disk job.");
            }
            return new RenderJobImage(result.Width, result.Height, result.Rgba, Rgba8RowOrder.TopDown)
            {
                HdrColor = new RenderJobHdrColor(hdr.Width, hdr.Height, hdr.Rgba16Float),
                DeviceDepth = result.Depth is { } values
                    ? new RenderJobDeviceDepth(values.Width, values.Height, values.Values) : null,
                Diagnostics = result.Diagnostics
            };
        };
        SilkSceneIngestionOptions? productOptions = null;
        RenderProductJobPlan? admittedProduct = null;
        _validateProduct = (plan, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (plan.RenderSettings != RenderSettings.PresentationDefault)
            {
                throw new NotSupportedException("The MCP product adapter requires its explicit presentation settings.");
            }
            var ingestion = new SilkSceneIngestionOptions(plan.IncludedPurposes, plan.MaterialBindingPurpose);
            RequireProductRevision(source, plan);
            productOptions = ingestion;
            admittedProduct = plan;
        };
        _captureProduct = (plan, state, maximumBytes, cancellationToken) =>
        {
            if (!ReferenceEquals(plan, admittedProduct) || productOptions is null)
            {
                throw new InvalidOperationException("The product must be admitted before capture.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            RequireProductRevision(source, plan);
            int width = state.Viewport.Width;
            int height = state.Viewport.Height;
            SilkFrameCaptureResult result = plan.IncludeHdrColor
                ? capturer.CaptureWithHdrColor(session, width, height, state.RenderSettings, productOptions,
                    new SilkHdrColorCaptureOptions(plan.IncludeDeviceDepth, maximumReadbackBytes: maximumBytes),
                    state.Time.TimeCode, state.Camera, cancellationToken)
                : capturer.CaptureWithDepth(session, width, height, state.RenderSettings, productOptions,
                    new SilkDepthCaptureOptions(maximumReadbackBytes: maximumBytes),
                    state.Time.TimeCode, state.Camera, cancellationToken);
            RequireProductRevision(source, plan);
            return ProductImage(result, plan);
        };
    }

    internal PreviewSilkFrameSource(
        Func<CaptureView, int, int, ImageRgba8> capture,
        IDisposable capturer,
        IDisposable session,
        IDisposable device,
        IDisposable source)
        : this(CreateCapture(capture), capturer, session, device, source)
    {
    }

    private PreviewSilkFrameSource(
        Func<CaptureView, int, int, CapturedFrame> capture,
        IDisposable capturer,
        IDisposable session,
        IDisposable device,
        IDisposable source)
    {
        _capture = capture;
        _capturer = capturer ?? throw new ArgumentNullException(nameof(capturer));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public RenderDiagnosticsState Diagnostics { get; private set; } =
        RenderDiagnosticsState.Empty;

    public ImageRgba8 Capture(CaptureView view, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(view);
        ObjectDisposedException.ThrowIf(_teardownStarted, this);
        CapturedFrame frame = _capture(view, width, height);
        Diagnostics = frame.Diagnostics;
        return frame.Image;
    }

    public RenderJobImage CaptureWithDepth(
        CaptureView view, int width, int height, long maximumReadbackBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(view);
        ObjectDisposedException.ThrowIf(_teardownStarted, this);
        RenderJobImage result = (_captureDepth ??
            throw new NotSupportedException("The configured preview source does not expose device depth."))(
                view, width, height, maximumReadbackBytes, cancellationToken);
        Diagnostics = result.Diagnostics;
        return result;
    }

    public RenderJobImage CaptureWithHdrColor(
        CaptureView view, int width, int height, long maximumReadbackBytes,
        bool includeDepth, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(view);
        ObjectDisposedException.ThrowIf(_teardownStarted, this);
        RenderJobImage result = (_captureHdr ??
            throw new NotSupportedException("The configured preview source does not expose HDR color."))(
                view, width, height, maximumReadbackBytes, includeDepth, cancellationToken);
        Diagnostics = result.Diagnostics;
        return result;
    }

    public void ValidateProduct(RenderProductJobPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ObjectDisposedException.ThrowIf(_teardownStarted, this);
        (_validateProduct ?? throw new NotSupportedException("The capture source cannot execute authored products."))(
            plan, cancellationToken);
    }

    public RenderJobImage CaptureProduct(
        RenderProductJobPlan plan, StageRenderState state, long maximumReadbackBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(state);
        ObjectDisposedException.ThrowIf(_teardownStarted, this);
        RenderJobImage image = (_captureProduct ??
            throw new NotSupportedException("The capture source cannot execute authored products."))(
                plan, state, maximumReadbackBytes, cancellationToken);
        Diagnostics = image.Diagnostics;
        return image;
    }

    private static void RequireProductRevision(UsdStageRenderSource source, RenderProductJobPlan plan)
    {
        if (plan.SourceStageRevision is not { } expected)
        {
            throw new NotSupportedException("The MCP product adapter requires a sampled source revision.");
        }
        using UsdStageRenderLease lease = source.AcquireLease();
        if (lease.ChangeSerial != expected)
        {
            throw new InvalidOperationException("The stage changed after authored-product preparation.");
        }
    }

    private static RenderJobImage ProductImage(SilkFrameCaptureResult result, RenderProductJobPlan plan)
    {
        if (plan.IncludeHdrColor != (result.HdrColor is not null) ||
            plan.IncludeDeviceDepth != (result.Depth is not null))
        {
            throw new InvalidDataException("The product capture did not return exactly its admitted planes.");
        }
        if (result.HdrColor is { Convention: not SilkHdrColorConvention.RendererWorkingCompositedBeforeExposureAndDisplay } ||
            result.Depth is { Convention: not SilkDepthConvention.NormalizedDeviceDepthZeroToOne })
        {
            throw new NotSupportedException("The product capture conventions do not match the admitted variables.");
        }
        return new RenderJobImage(result.Width, result.Height, result.Rgba, Rgba8RowOrder.TopDown)
        {
            HdrColor = result.HdrColor is { } hdr
                ? new RenderJobHdrColor(hdr.Width, hdr.Height, hdr.Rgba16Float) : null,
            DeviceDepth = result.Depth is { } depth
                ? new RenderJobDeviceDepth(depth.Width, depth.Height, depth.Values) : null,
            Diagnostics = result.Diagnostics
        };
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

    private static Func<CaptureView, int, int, CapturedFrame> CreateCapture(
        Func<CaptureView, int, int, ImageRgba8> capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        return (view, width, height) =>
            new CapturedFrame(capture(view, width, height), RenderDiagnosticsState.Empty);
    }

    private sealed record CapturedFrame(
        ImageRgba8 Image,
        RenderDiagnosticsState Diagnostics);
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
