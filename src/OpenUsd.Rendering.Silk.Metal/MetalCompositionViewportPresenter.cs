// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SharpMetal.Metal;

namespace OpenUsd.Rendering.Silk.Metal;

/// <summary>Renders one retained hdSilk frame into an IOSurface-backed Metal target.</summary>
public delegate MetalCompositionRenderResult MetalCompositionRenderCallback(
    MetalCompositionRenderContext context);

/// <summary>Targets and ring metadata supplied to a Metal composition render callback.</summary>
public sealed class MetalCompositionRenderContext
{
    internal MetalCompositionRenderContext(
        SilkMeshRenderer renderer,
        ISilkGraphicsTexture colorTarget,
        ISilkGraphicsTexture depthTarget,
        long allocationId,
        int frameIndex,
        int useCount,
        CancellationToken cancellationToken)
    {
        Renderer = renderer;
        ColorTarget = colorTarget;
        DepthTarget = depthTarget;
        AllocationId = allocationId;
        FrameIndex = frameIndex;
        UseCount = useCount;
        CancellationToken = cancellationToken;
    }

    /// <summary>Gets the retained mesh renderer sharing the presentation device.</summary>
    public SilkMeshRenderer Renderer { get; }

    /// <summary>Gets the IOSurface-backed compositor color target.</summary>
    public ISilkGraphicsTexture ColorTarget { get; }

    /// <summary>Gets the frame-local depth target.</summary>
    public ISilkGraphicsTexture DepthTarget { get; }

    /// <summary>Gets the stable ring allocation identifier.</summary>
    public long AllocationId { get; }

    /// <summary>Gets the zero-based frame index in the current generation.</summary>
    public int FrameIndex { get; }

    /// <summary>Gets the number of times this ring allocation has been rendered.</summary>
    public int UseCount { get; }

    /// <summary>Gets the cancellation token for this frame.</summary>
    public CancellationToken CancellationToken { get; }
}

/// <summary>Scene revision and retained-mesh evidence returned by a Metal render callback.</summary>
public readonly record struct MetalCompositionRenderResult(
    ulong SceneRevision,
    SilkMeshRenderResult MeshRenderResult);

/// <summary>Metal composition lifecycle, ring, and retained-scene diagnostics.</summary>
public readonly record struct MetalCompositionPresenterDiagnostics(
    bool ProbeSucceeded,
    int ActiveGenerations,
    int ActiveFrames,
    long PresentedFrames,
    long RingReuseFrames,
    long RenderCallbacks,
    ulong LastSceneRevision,
    int LastDrawCount,
    long LastTriangleCount,
    int LastWidth,
    int LastHeight,
    long LastAllocationId,
    string? DeviceLossReason);

/// <summary>
/// Presents IOSurface-backed Metal frames to an Avalonia composition host.
/// </summary>
public sealed class MetalCompositionViewportPresenter : ICompositionViewportPresenter
{
    /// <summary>Avalonia's descriptor for an IOSurface reference.</summary>
    public const string IOSurfaceHandleType = "IOSurfaceRef";

    /// <summary>Avalonia's descriptor for an MTLSharedEvent object pointer.</summary>
    public const string SharedEventHandleType = "MetalSharedEvent";

    private readonly object _sync = new();
    private readonly MetalCompositionRenderCallback? _renderCallback;
    private readonly bool _required;
    private readonly List<MetalCompositionPresentationGeneration> _generations = [];
    private MetalSilkGraphicsDevice? _device;
    private MetalCompositionPipelineResources? _pipeline;
    private SilkMeshRenderer? _renderer;
    private long _nextAllocationId;
    private ulong _nextTimelineValue = 1;
    private long _presentedFrames;
    private long _ringReuseFrames;
    private long _renderCallbacks;
    private ulong _lastSceneRevision;
    private int _lastDrawCount;
    private long _lastTriangleCount;
    private int _lastWidth;
    private int _lastHeight;
    private long _lastAllocationId;
    private int _activeGenerationCount;
    private int _activeFrameCount;
    private string? _deviceLossReason;
    private bool _available;
    private bool _deviceLost;
    private bool _disposed;

    /// <summary>Initializes a Metal composition presenter.</summary>
    /// <param name="required">
    /// When true, probing throws if the checked Metal library or required macOS
    /// external-object support is unavailable.
    /// </param>
    public MetalCompositionViewportPresenter(bool required = false) =>
        _required = required;

    /// <summary>
    /// Initializes a Metal presenter that renders retained hdSilk meshes into each IOSurface.
    /// </summary>
    public MetalCompositionViewportPresenter(
        MetalCompositionRenderCallback renderCallback,
        bool required = false)
    {
        ArgumentNullException.ThrowIfNull(renderCallback);
        _renderCallback = renderCallback;
        _required = required;
    }

    /// <summary>Captures current presenter resources, ring reuse, and scene evidence.</summary>
    public MetalCompositionPresenterDiagnostics GetDiagnostics()
    {
        lock (_sync)
        {
            return new MetalCompositionPresenterDiagnostics(
                _available,
                _activeGenerationCount,
                _activeFrameCount,
                _presentedFrames,
                _ringReuseFrames,
                _renderCallbacks,
                _lastSceneRevision,
                _lastDrawCount,
                _lastTriangleCount,
                _lastWidth,
                _lastHeight,
                _lastAllocationId,
                _deviceLossReason);
        }
    }

    internal bool HasDiagnosticPipeline
    {
        get
        {
            lock (_sync)
            {
                return _pipeline is not null;
            }
        }
    }

    internal bool HasRetainedMeshRenderer
    {
        get
        {
            lock (_sync)
            {
                return _renderer is not null;
            }
        }
    }

    internal static bool HasRequiredResources(
        bool callbackMode,
        bool hasPipeline,
        bool hasRenderer) =>
        callbackMode ? hasRenderer : hasPipeline;

    /// <inheritdoc/>
    public ValueTask<CompositionPresenterProbeResult> ProbeAsync(
        CompositionPresentationTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!OperatingSystem.IsMacOSVersionAtLeast(12))
            {
                return ValueTask.FromResult(ProbeFailure(
                    "Metal composition requires macOS 12 or later.",
                    new PlatformNotSupportedException(
                        "MTLSharedEvent composition requires macOS 12 or later.")));
            }
            if (!target.ImageHandleTypes.Contains(
                    IOSurfaceHandleType,
                    StringComparer.Ordinal) ||
                !target.SemaphoreHandleTypes.Contains(
                    SharedEventHandleType,
                    StringComparer.Ordinal))
            {
                return ValueTask.FromResult(ProbeFailure(
                    "The compositor does not support IOSurfaceRef and MetalSharedEvent.",
                    new NotSupportedException(
                        "Avalonia must advertise IOSurfaceRef and MetalSharedEvent.")));
            }
            if (!SilkCheckedShaderAssets.HasPinnedMetalLibrary)
            {
                return ValueTask.FromResult(ProbeFailure(
                    "The validated ten-entry mesh.metallib and sidecar are unavailable.",
                    new InvalidOperationException(
                        "Metal composition requires a validated ten-entry " +
                        "mesh.metallib and mesh.metallib.manifest.json pair.")));
            }
            if (_deviceLost)
            {
                return ValueTask.FromResult(ProbeFailure(
                    "The Metal presentation device was lost.",
                    new InvalidOperationException(
                        "The Metal presentation device was lost.")));
            }

            try
            {
                if (_device is not null &&
                    HasRequiredResources(
                        _renderCallback is not null,
                        _pipeline is not null,
                        _renderer is not null))
                {
                    ValidateDeviceIdentity(_device, target);
                }
                else
                {
                    (MetalSilkGraphicsDevice device,
                        MetalCompositionPipelineResources? pipeline,
                        SilkMeshRenderer? renderer) =
                        CreateInitializedResources(target, _renderCallback is not null);
                    _device = device;
                    _pipeline = pipeline;
                    _renderer = renderer;
                }
                _available = true;
                _deviceLossReason = null;
                return ValueTask.FromResult(CompositionPresenterProbeResult.Available(
                    _renderCallback is null
                        ? "Metal IOSurface timeline composition available"
                        : "Metal IOSurface timeline composition available with retained hdSilk rendering"));
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException)
            {
                return ValueTask.FromResult(ProbeFailure(
                    $"Metal composition unavailable: {exception.Message}",
                    exception));
            }
        }
    }

    /// <inheritdoc/>
    public ValueTask<ICompositionPresentationGeneration> CreateGenerationAsync(
        ViewportDimensions size,
        int frameCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfLessThan(frameCount, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(frameCount, 3);
        if (size.Width <= 0 || size.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(size),
                "Metal presentation generations require a non-empty size.");
        }

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_available ||
                _device is null ||
                !HasRequiredResources(
                    _renderCallback is not null,
                    _pipeline is not null,
                    _renderer is not null))
            {
                throw new InvalidOperationException(
                    "ProbeAsync must succeed before creating a Metal presentation generation.");
            }
            if (_deviceLost)
            {
                throw new InvalidOperationException(
                    "The Metal presentation device was lost.");
            }
            if (!OperatingSystem.IsMacOSVersionAtLeast(12))
            {
                throw new PlatformNotSupportedException(
                    "Metal composition requires macOS 12 or later.");
            }

            var generation = MetalCompositionPresentationGeneration.Create(
                this,
                _device,
                size,
                frameCount);
            _generations.Add(generation);
            _activeGenerationCount++;
            _activeFrameCount += frameCount;
            return ValueTask.FromResult<ICompositionPresentationGeneration>(generation);
        }
    }

    /// <inheritdoc/>
    public ValueTask<CompositionFrameRenderResult> RenderAsync(
        ICompositionPresentationFrame frame,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(frame);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_deviceLost)
            {
                return ValueTask.FromResult(DeviceLostResult());
            }
            if (!OperatingSystem.IsMacOSVersionAtLeast(12))
            {
                return ValueTask.FromResult(DeviceLostResult());
            }
            if (!_available ||
                _device is null ||
                !HasRequiredResources(
                    _renderCallback is not null,
                    _pipeline is not null,
                    _renderer is not null))
            {
                throw new InvalidOperationException(
                    "ProbeAsync must succeed before rendering Metal presentation frames.");
            }
            if (frame is not MetalCompositionPresentationFrame metalFrame ||
                !ReferenceEquals(metalFrame.Presenter, this))
            {
                throw new ArgumentException(
                    "The frame was not created by this Metal presenter.",
                    nameof(frame));
            }

            try
            {
                if (!metalFrame.PrepareForRender())
                {
                    _deviceLost = true;
                    _available = false;
                    return ValueTask.FromResult(DeviceLostResult());
                }
                (ulong producerValue, ulong consumerValue) =
                    ReserveTimelineValues();
                bool reused = metalFrame.UseCount > 0;
                int useCount = metalFrame.IncrementUseCount();
                ISilkGraphicsSubmission? renderSubmission = null;
                MTLCommandBuffer signalCommand = default;
                try
                {
                    if (_renderCallback is null)
                    {
                        renderSubmission = SubmitTriangle(
                            _device,
                            _pipeline!,
                            metalFrame);
                    }
                    else
                    {
                        MetalCompositionRenderResult rendered = _renderCallback(
                            new MetalCompositionRenderContext(
                                _renderer!,
                                metalFrame.Color,
                                metalFrame.Depth,
                                metalFrame.AllocationId,
                                metalFrame.FrameIndex,
                                useCount,
                                cancellationToken));
                        RecordRenderCallback(rendered);
                    }
                    signalCommand = _device.SubmitEventSignal(
                        metalFrame.SharedEvent,
                        producerValue);
                    metalFrame.SetSubmission(
                        new MetalCompositionSubmission(
                            renderSubmission,
                            signalCommand),
                        consumerValue);
                    renderSubmission = null;
                    signalCommand = default;
                }
                finally
                {
                    if (signalCommand.NativePtr != 0)
                    {
                        signalCommand.Dispose();
                    }
                    renderSubmission?.Dispose();
                }

                _presentedFrames++;
                if (reused)
                {
                    _ringReuseFrames++;
                }
                _lastWidth = metalFrame.Image.Size.Width;
                _lastHeight = metalFrame.Image.Size.Height;
                _lastAllocationId = metalFrame.AllocationId;
                return ValueTask.FromResult(new CompositionFrameRenderResult(
                    CompositionFrameRenderStatus.Presented,
                    ContinueRendering: false,
                    CompositionFrameSynchronization.TimelineSemaphores(
                        metalFrame.SharedEventResourceId,
                        producerValue,
                        metalFrame.SharedEventResourceId,
                        consumerValue)));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _deviceLost = true;
                _available = false;
                _deviceLossReason = exception.Message;
                try
                {
                    metalFrame.DiscardFailedSubmission();
                }
                catch
                {
                }
                return ValueTask.FromResult(DeviceLostResult());
            }
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        MetalCompositionPresentationGeneration[] generations;
        lock (_sync)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }
            _disposed = true;
            _available = false;
            generations = [.. _generations];
        }

        var failures = new List<Exception>();
        if (OperatingSystem.IsMacOS())
        {
            foreach (MetalCompositionPresentationGeneration generation in generations)
            {
                try
                {
                    generation.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
        }
        lock (_sync)
        {
            try
            {
                if (OperatingSystem.IsMacOS())
                {
                    DisposeInitializedResources();
                }
                else
                {
                    _renderer = null;
                    _pipeline = null;
                    _device = null;
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
        if (failures.Count != 0)
        {
            return ValueTask.FromException(
                new AggregateException(
                    "Metal composition presenter disposal failed.",
                    failures));
        }
        return ValueTask.CompletedTask;
    }

    [SupportedOSPlatform("macos")]
    internal void RemoveGeneration(MetalCompositionPresentationGeneration generation)
    {
        lock (_sync)
        {
            if (_generations.Remove(generation))
            {
                _activeGenerationCount--;
                _activeFrameCount -= generation.InitialFrameCount;
            }
        }
    }

    private void RecordRenderCallback(MetalCompositionRenderResult rendered)
    {
        _renderCallbacks++;
        _lastSceneRevision = rendered.SceneRevision;
        _lastDrawCount = rendered.MeshRenderResult.DrawCount;
        _lastTriangleCount = _renderer!.GpuResources.Meshes.Values.Sum(
            mesh => checked((long)mesh.IndexCount / 3));
    }

    internal long GetNextAllocationId()
    {
        long allocationId = checked(++_nextAllocationId);
        if (allocationId <= 0)
        {
            throw new InvalidOperationException(
                "The Metal presentation allocation identifier was exhausted.");
        }
        return allocationId;
    }

    private CompositionPresenterProbeResult ProbeFailure(
        string status,
        Exception exception)
    {
        if (_required)
        {
            throw new InvalidOperationException(status, exception);
        }
        return CompositionPresenterProbeResult.Unavailable(status);
    }

    [SupportedOSPlatform("macos")]
    private static (
        MetalSilkGraphicsDevice Device,
        MetalCompositionPipelineResources? Pipeline,
        SilkMeshRenderer? Renderer) CreateInitializedResources(
            CompositionPresentationTarget target,
            bool createMeshRenderer)
    {
        MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();
        MetalCompositionPipelineResources? pipeline = null;
        SilkMeshRenderer? renderer = null;
        try
        {
            device.ProbeCompositionPresentation();
            ValidateDeviceIdentity(device, target);
            if (createMeshRenderer)
            {
                renderer = new SilkMeshRenderer(device);
            }
            else
            {
                pipeline = MetalCompositionPipelineResources.Create(device);
            }
            return (device, pipeline, renderer);
        }
        catch (Exception creationFailure)
        {
            Exception? cleanupFailure = null;
            try
            {
                renderer?.Dispose();
            }
            catch (Exception exception)
            {
                cleanupFailure = cleanupFailure is null
                    ? exception
                    : new AggregateException(cleanupFailure, exception);
            }
            try
            {
                pipeline?.Dispose();
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
            }
            try
            {
                device.Dispose();
            }
            catch (Exception exception)
            {
                cleanupFailure = cleanupFailure is null
                    ? exception
                    : new AggregateException(cleanupFailure, exception);
            }
            if (cleanupFailure is null)
            {
                throw;
            }
            throw new AggregateException(
                "Metal presentation probe and rollback failed.",
                creationFailure,
                cleanupFailure);
        }
    }

    [SupportedOSPlatform("macos")]
    private static void ValidateDeviceIdentity(
        MetalSilkGraphicsDevice device,
        CompositionPresentationTarget target)
    {
        if (target.DeviceLuid.Count == 0)
        {
            return;
        }
        byte[] deviceLuid = device.GetPresentationDeviceLuid();
        if (!target.DeviceLuid.SequenceEqual(deviceLuid))
        {
            throw new NotSupportedException(
                "The Avalonia compositor and Metal presenter use different devices.");
        }
    }

    [SupportedOSPlatform("macos")]
    private static ISilkGraphicsSubmission SubmitTriangle(
        MetalSilkGraphicsDevice device,
        MetalCompositionPipelineResources pipeline,
        MetalCompositionPresentationFrame frame)
    {
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.ClearColor(frame.Color, new SilkColor(0, 0, 0, 1));
        commands.ClearDepth(frame.Depth, 1);
        commands.BeginRendering(new SilkRenderingDescriptor(frame.Color, frame.Depth));
        commands.SetGraphicsPipeline(pipeline.Pipeline);
        commands.SetViewport(new SilkViewport(
            0,
            0,
            frame.Image.Size.Width,
            frame.Image.Size.Height));
        commands.SetScissor(new SilkScissor(
            0,
            0,
            checked((uint)frame.Image.Size.Width),
            checked((uint)frame.Image.Size.Height)));
        commands.SetVertexBuffer(pipeline.Vertices);
        commands.SetIndexBuffer(pipeline.Indices);
        commands.SetUniformBuffer(0, 0, pipeline.Uniforms);
        commands.DrawIndexed(3);
        commands.EndRendering();
        return device.Submit(commands);
    }

    private (ulong Producer, ulong Consumer) ReserveTimelineValues()
    {
        if (_nextTimelineValue > ulong.MaxValue - 2)
        {
            _deviceLost = true;
            throw new InvalidOperationException(
                "The Metal presentation timeline was exhausted.");
        }
        ulong producer = _nextTimelineValue;
        ulong consumer = producer + 1;
        _nextTimelineValue += 2;
        return (producer, consumer);
    }

    [SupportedOSPlatform("macos")]
    private void DisposeInitializedResources()
    {
        SilkMeshRenderer? renderer = _renderer;
        MetalCompositionPipelineResources? pipeline = _pipeline;
        MetalSilkGraphicsDevice? device = _device;
        _renderer = null;
        _pipeline = null;
        _device = null;
        Exception? failure = null;
        try
        {
            renderer?.Dispose();
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        try
        {
            pipeline?.Dispose();
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        try
        {
            device?.Dispose();
        }
        catch (Exception exception)
        {
            failure = failure is null
                ? exception
                : new AggregateException(failure, exception);
        }
        if (failure is not null)
        {
            throw failure;
        }
    }

    private static CompositionFrameRenderResult DeviceLostResult() =>
        new(
            CompositionFrameRenderStatus.DeviceLost,
            ContinueRendering: false,
            CompositionFrameSynchronization.Automatic);
}

[SupportedOSPlatform("macos")]
internal sealed class MetalCompositionPresentationGeneration
    : ICompositionPresentationGeneration
{
    private readonly object _sync = new();
    private readonly MetalCompositionViewportPresenter _presenter;
    private MetalCompositionPresentationFrame[]? _frames;

    private MetalCompositionPresentationGeneration(
        MetalCompositionViewportPresenter presenter,
        ViewportDimensions size,
        MetalCompositionPresentationFrame[] frames)
    {
        _presenter = presenter;
        Size = size;
        _frames = frames;
        InitialFrameCount = frames.Length;
        Frames = Array.AsReadOnly<ICompositionPresentationFrame>(frames);
    }

    public ViewportDimensions Size { get; }

    public IReadOnlyList<ICompositionPresentationFrame> Frames { get; }

    internal int InitialFrameCount { get; }

    internal static MetalCompositionPresentationGeneration Create(
        MetalCompositionViewportPresenter presenter,
        MetalSilkGraphicsDevice device,
        ViewportDimensions size,
        int frameCount)
    {
        var frames = new MetalCompositionPresentationFrame[frameCount];
        int created = 0;
        try
        {
            for (; created < frames.Length; created++)
            {
                long allocationId = presenter.GetNextAllocationId();
                frames[created] = MetalCompositionPresentationFrame.Create(
                    presenter,
                    device,
                    size,
                    allocationId,
                    created);
            }
            return new MetalCompositionPresentationGeneration(presenter, size, frames);
        }
        catch (Exception creationFailure)
        {
            var failures = new List<Exception> { creationFailure };
            for (int index = 0; index < created; index++)
            {
                try
                {
                    frames[index].Dispose();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
            if (failures.Count == 1)
            {
                throw;
            }
            throw new AggregateException(
                "Metal presentation generation creation and rollback failed.",
                failures);
        }
    }

    public ValueTask DisposeAsync()
    {
        MetalCompositionPresentationFrame[]? frames;
        lock (_sync)
        {
            frames = _frames;
            _frames = null;
        }
        if (frames is null)
        {
            return ValueTask.CompletedTask;
        }

        var failures = new List<Exception>();
        foreach (MetalCompositionPresentationFrame frame in frames)
        {
            try
            {
                frame.Dispose();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
        _presenter.RemoveGeneration(this);
        if (failures.Count != 0)
        {
            return ValueTask.FromException(
                new AggregateException(
                    "Metal presentation generation disposal failed.",
                    failures));
        }
        return ValueTask.CompletedTask;
    }
}

[SupportedOSPlatform("macos")]
internal sealed class MetalCompositionPresentationFrame
    : ICompositionPresentationFrame,
      IDisposable
{
    private readonly object _sync = new();
    private readonly IOSurfaceHandle _surface;
    private MetalCompositionSubmission? _submission;
    private int _useCount;
    private bool _disposed;

    private MetalCompositionPresentationFrame(
        MetalCompositionViewportPresenter presenter,
        long allocationId,
        IOSurfaceHandle surface,
        MetalSilkGraphicsTexture color,
        ISilkGraphicsTexture depth,
        MTLSharedEvent sharedEvent,
        CompositionExternalImage image,
        int frameIndex)
    {
        Presenter = presenter;
        AllocationId = allocationId;
        _surface = surface;
        Color = color;
        Depth = depth;
        SharedEvent = sharedEvent;
        Image = image;
        FrameIndex = frameIndex;
        SharedEventResourceId = allocationId;
        Semaphores = Array.AsReadOnly(
        [
            new CompositionExternalSemaphore(
                SharedEventResourceId,
                MetalCompositionViewportPresenter.SharedEventHandleType)
        ]);
    }

    public long AllocationId { get; }

    public CompositionExternalImage Image { get; }

    public IReadOnlyList<CompositionExternalSemaphore> Semaphores { get; }

    internal MetalCompositionViewportPresenter Presenter { get; }

    internal MetalSilkGraphicsTexture Color { get; }

    internal ISilkGraphicsTexture Depth { get; }

    internal MTLSharedEvent SharedEvent { get; }

    internal long SharedEventResourceId { get; }

    internal int FrameIndex { get; }

    internal int UseCount => _useCount;

    internal ulong LastConsumerSignal { get; private set; }

    internal static MetalCompositionPresentationFrame Create(
        MetalCompositionViewportPresenter presenter,
        MetalSilkGraphicsDevice device,
        ViewportDimensions size,
        long allocationId,
        int frameIndex)
    {
        uint width = checked((uint)size.Width);
        uint height = checked((uint)size.Height);
        IOSurfaceHandle? surface = null;
        MetalSilkGraphicsTexture? color = null;
        ISilkGraphicsTexture? depth = null;
        MTLSharedEvent sharedEvent = default;
        try
        {
            surface = MetalCompositionNativeInterop.CreateIOSurface(width, height);
            var colorDescriptor = new SilkTextureDescriptor(
                width,
                height,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.Sampled);
            color = device.CreateIOSurfaceTexture(surface, colorDescriptor);
            depth = device.CreateTexture2D(
                SilkTextureDescriptor.SampledDepthTarget(width, height));
            sharedEvent = device.CreatePresentationSharedEvent();
            return new MetalCompositionPresentationFrame(
                presenter,
                allocationId,
                surface,
                color,
                depth,
                sharedEvent,
                new CompositionExternalImage(
                    MetalCompositionViewportPresenter.IOSurfaceHandleType,
                    size,
                    CompositionExternalImageFormat.R8G8B8A8UNorm,
                    memoryOffset: 0,
                    memorySize: checked((ulong)surface.AllocationSize),
                    topLeftOrigin: true),
                frameIndex);
        }
        catch (Exception creationFailure)
        {
            Exception? cleanupFailure = null;
            try
            {
                if (sharedEvent.NativePtr != 0)
                {
                    sharedEvent.Dispose();
                }
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
            }
            DisposeOptionalResource(depth, ref cleanupFailure);
            DisposeOptionalResource(color, ref cleanupFailure);
            DisposeOptionalResource(surface, ref cleanupFailure);
            if (cleanupFailure is null)
            {
                throw;
            }
            throw new AggregateException(
                "Metal presentation frame creation and rollback failed.",
                creationFailure,
                cleanupFailure);
        }
    }

    public ValueTask<ICompositionExternalHandleLease> LeaseImageHandleAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return ValueTask.FromResult<ICompositionExternalHandleLease>(
                new MetalCompositionExternalHandleLease(
                    MetalCompositionNativeInterop.RetainIOSurface(_surface),
                    MetalCompositionViewportPresenter.IOSurfaceHandleType));
        }
    }

    public ValueTask<ICompositionExternalHandleLease> LeaseSemaphoreHandleAsync(
        long resourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (resourceId != SharedEventResourceId)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(resourceId),
                    "The frame does not own the requested Metal shared event.");
            }
            return ValueTask.FromResult<ICompositionExternalHandleLease>(
                new MetalCompositionExternalHandleLease(
                    MetalCompositionNativeInterop.RetainObjectiveCObject(
                        SharedEvent.NativePtr),
                    MetalCompositionViewportPresenter.SharedEventHandleType));
        }
    }

    internal bool PrepareForRender()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            MetalCompositionSubmission? submission = _submission;
            _submission = null;
            submission?.Dispose();
            return LastConsumerSignal == 0 ||
                SharedEvent.SignaledValue >= LastConsumerSignal;
        }
    }

    internal int IncrementUseCount()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return ++_useCount;
        }
    }

    internal void SetSubmission(
        MetalCompositionSubmission submission,
        ulong consumerSignal)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_submission is not null)
            {
                throw new InvalidOperationException(
                    "The Metal presentation frame already has a pending submission.");
            }
            _submission = submission;
            LastConsumerSignal = consumerSignal;
        }
    }

    internal void DiscardFailedSubmission()
    {
        lock (_sync)
        {
            MetalCompositionSubmission? submission = _submission;
            _submission = null;
            submission?.Dispose();
        }
    }

    public void Dispose()
    {
        MetalCompositionSubmission? submission;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            submission = _submission;
            _submission = null;
        }

        Exception? failure = null;
        try
        {
            submission?.Dispose();
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        DisposeResource(Depth, ref failure);
        DisposeResource(Color, ref failure);
        DisposeResource(SharedEvent, ref failure);
        DisposeResource(_surface, ref failure);
        if (failure is not null)
        {
            throw failure;
        }
    }

    private static void DisposeResource(IDisposable resource, ref Exception? failure)
    {
        try
        {
            resource.Dispose();
        }
        catch (Exception exception)
        {
            failure = failure is null
                ? exception
                : new AggregateException(failure, exception);
        }
    }

    private static void DisposeOptionalResource(
        IDisposable? resource,
        ref Exception? failure)
    {
        if (resource is not null)
        {
            DisposeResource(resource, ref failure);
        }
    }
}

internal sealed class MetalCompositionExternalHandleLease : ICompositionExternalHandleLease
{
    private readonly object _sync = new();
    private SafeHandle? _handle;

    internal MetalCompositionExternalHandleLease(SafeHandle handle, string handleType)
    {
        _handle = handle;
        HandleType = handleType;
    }

    public nint Handle
    {
        get
        {
            lock (_sync)
            {
                return _handle?.DangerousGetHandle() ?? 0;
            }
        }
    }

    public string HandleType { get; }

    public CompositionExternalHandleOwnership Ownership =>
        CompositionExternalHandleOwnership.BorrowedUntilImportCompleted;

    public void CommitTransfer()
    {
    }

    public ValueTask DisposeAsync()
    {
        SafeHandle? handle;
        lock (_sync)
        {
            handle = _handle;
            _handle = null;
        }
        handle?.Dispose();
        return ValueTask.CompletedTask;
    }
}

[SupportedOSPlatform("macos")]
internal sealed class MetalCompositionSubmission : IDisposable
{
    private ISilkGraphicsSubmission? _renderSubmission;
    private MTLCommandBuffer _signalCommand;
    private bool _disposed;

    internal MetalCompositionSubmission(
        ISilkGraphicsSubmission? renderSubmission,
        MTLCommandBuffer signalCommand)
    {
        _renderSubmission = renderSubmission;
        _signalCommand = signalCommand;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Exception? failure = null;
        try
        {
            _signalCommand.WaitUntilCompleted();
            if (_signalCommand.Status == MTLCommandBufferStatus.Error)
            {
                failure = new InvalidOperationException(
                    "The Metal presentation signal submission failed.");
            }
        }
        finally
        {
            _signalCommand.Dispose();
            try
            {
                _renderSubmission?.Dispose();
            }
            catch (Exception exception)
            {
                failure = failure is null
                    ? exception
                    : new AggregateException(failure, exception);
            }
            _renderSubmission = null;
        }
        if (failure is not null)
        {
            throw failure;
        }
    }
}

[SupportedOSPlatform("macos")]
internal sealed class MetalCompositionPipelineResources : IDisposable
{
    private MetalCompositionPipelineResources(
        ISilkGraphicsShaderModule vertexShader,
        ISilkGraphicsShaderModule fragmentShader,
        ISilkGraphicsBindingLayout bindingLayout,
        ISilkGraphicsShaderProgram program,
        ISilkGraphicsPipeline pipeline,
        ISilkGraphicsBuffer vertices,
        ISilkGraphicsBuffer indices,
        ISilkGraphicsBuffer uniforms)
    {
        VertexShader = vertexShader;
        FragmentShader = fragmentShader;
        BindingLayout = bindingLayout;
        Program = program;
        Pipeline = pipeline;
        Vertices = vertices;
        Indices = indices;
        Uniforms = uniforms;
    }

    internal ISilkGraphicsShaderModule VertexShader { get; }

    internal ISilkGraphicsShaderModule FragmentShader { get; }

    internal ISilkGraphicsBindingLayout BindingLayout { get; }

    internal ISilkGraphicsShaderProgram Program { get; }

    internal ISilkGraphicsPipeline Pipeline { get; }

    internal ISilkGraphicsBuffer Vertices { get; }

    internal ISilkGraphicsBuffer Indices { get; }

    internal ISilkGraphicsBuffer Uniforms { get; }

    internal static MetalCompositionPipelineResources Create(
        MetalSilkGraphicsDevice device)
    {
        ISilkGraphicsShaderModule? vertexShader = null;
        ISilkGraphicsShaderModule? fragmentShader = null;
        ISilkGraphicsBindingLayout? bindingLayout = null;
        ISilkGraphicsShaderProgram? program = null;
        ISilkGraphicsPipeline? pipeline = null;
        ISilkGraphicsBuffer? vertices = null;
        ISilkGraphicsBuffer? indices = null;
        ISilkGraphicsBuffer? uniforms = null;
        try
        {
            vertexShader = device.CreateShaderModule(
                SilkCheckedShaderAssets.LoadMeshVertex(
                    SilkShaderBinaryFormat.MetalLibrary));
            fragmentShader = device.CreateShaderModule(
                SilkCheckedShaderAssets.LoadMeshFragment(
                    SilkShaderBinaryFormat.MetalLibrary));
            bindingLayout = device.CreateBindingLayout(
                SilkBindingLayoutDescriptor.SceneParameters);
            program = device.CreateShaderProgram(new SilkShaderProgramDescriptor(
                vertexShader,
                fragmentShader,
                bindingLayout));
            pipeline = device.CreateGraphicsPipeline(
                new SilkGraphicsPipelineDescriptor(
                    program,
                    SilkVertexLayoutDescriptor.PositionNormal,
                    SilkTextureFormat.Rgba8Unorm,
                    SilkTextureFormat.D32Float));
            vertices = device.CreateBuffer(
                72,
                SilkBufferUsage.Vertex | SilkBufferUsage.Upload);
            indices = device.CreateBuffer(
                6,
                SilkBufferUsage.Index | SilkBufferUsage.Upload);
            uniforms = device.CreateBuffer(
                80,
                SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
            vertices.Write(MemoryMarshal.AsBytes<float>(
            [
                -0.75f, -0.75f, 0, 1, 0, 0,
                 0.00f,  0.75f, 0, 1, 0, 0,
                 0.75f, -0.75f, 0, 1, 0, 0
            ]));
            indices.Write(MemoryMarshal.AsBytes<ushort>([0, 1, 2]));
            uniforms.Write(MemoryMarshal.AsBytes<float>(
            [
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                0, 0, 0, 1,
                1, 1, 1, 1
            ]));
            return new MetalCompositionPipelineResources(
                vertexShader,
                fragmentShader,
                bindingLayout,
                program,
                pipeline,
                vertices,
                indices,
                uniforms);
        }
        catch (Exception creationFailure)
        {
            Exception? cleanupFailure = null;
            DisposeOptionalResource(uniforms, ref cleanupFailure);
            DisposeOptionalResource(indices, ref cleanupFailure);
            DisposeOptionalResource(vertices, ref cleanupFailure);
            DisposeOptionalResource(pipeline, ref cleanupFailure);
            DisposeOptionalResource(program, ref cleanupFailure);
            DisposeOptionalResource(bindingLayout, ref cleanupFailure);
            DisposeOptionalResource(fragmentShader, ref cleanupFailure);
            DisposeOptionalResource(vertexShader, ref cleanupFailure);
            if (cleanupFailure is null)
            {
                throw;
            }
            throw new AggregateException(
                "Metal presentation pipeline creation and rollback failed.",
                creationFailure,
                cleanupFailure);
        }
    }

    public void Dispose()
    {
        Exception? failure = null;
        DisposeResource(Uniforms, ref failure);
        DisposeResource(Indices, ref failure);
        DisposeResource(Vertices, ref failure);
        DisposeResource(Pipeline, ref failure);
        DisposeResource(Program, ref failure);
        DisposeResource(BindingLayout, ref failure);
        DisposeResource(FragmentShader, ref failure);
        DisposeResource(VertexShader, ref failure);
        if (failure is not null)
        {
            throw failure;
        }
    }

    private static void DisposeResource(IDisposable resource, ref Exception? failure)
    {
        try
        {
            resource.Dispose();
        }
        catch (Exception exception)
        {
            failure = failure is null
                ? exception
                : new AggregateException(failure, exception);
        }
    }

    private static void DisposeOptionalResource(
        IDisposable? resource,
        ref Exception? failure)
    {
        if (resource is not null)
        {
            DisposeResource(resource, ref failure);
        }
    }
}
