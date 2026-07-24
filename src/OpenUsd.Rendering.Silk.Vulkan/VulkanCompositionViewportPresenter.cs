// Copyright (c) marcschier. Licensed under the MIT License.

using System.ComponentModel;
using System.Runtime.InteropServices;
using global::Silk.NET.Vulkan;
using Microsoft.Win32.SafeHandles;
using Silk.NET.Core.Native;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace OpenUsd.Rendering.Silk.Vulkan;

/// <summary>Renders one retained hdSilk frame into an exportable Vulkan image.</summary>
public delegate SilkMeshRenderResult VulkanCompositionRenderCallback(
    VulkanCompositionRenderContext context);

/// <summary>Targets and ring metadata supplied to a Vulkan composition render callback.</summary>
public sealed class VulkanCompositionRenderContext
{
    internal VulkanCompositionRenderContext(
        SilkMeshRenderer renderer,
        ISilkGraphicsTexture colorTarget,
        ISilkGraphicsTexture depthTarget,
        long allocationId,
        int frameIndex,
        int useCount)
    {
        Renderer = renderer;
        ColorTarget = colorTarget;
        DepthTarget = depthTarget;
        AllocationId = allocationId;
        FrameIndex = frameIndex;
        UseCount = useCount;
    }

    /// <summary>Gets the retained mesh renderer sharing the presentation device.</summary>
    public SilkMeshRenderer Renderer { get; }

    /// <summary>Gets the exportable compositor color target.</summary>
    public ISilkGraphicsTexture ColorTarget { get; }

    /// <summary>Gets the frame-local depth target.</summary>
    public ISilkGraphicsTexture DepthTarget { get; }

    /// <summary>Gets the stable ring allocation identifier.</summary>
    public long AllocationId { get; }

    /// <summary>Gets the zero-based frame index in the current generation.</summary>
    public int FrameIndex { get; }

    /// <summary>Gets the number of times this ring allocation has been rendered.</summary>
    public int UseCount { get; }
}

/// <summary>Vulkan composition presenter resource and frame evidence.</summary>
public readonly record struct VulkanCompositionPresenterDiagnostics(
    int ActiveGenerations,
    int ActiveFrames,
    long PresentedFrames,
    long RingReuseFrames,
    long RenderCallbacks,
    SilkMeshRenderResult? LastMeshRenderResult,
    string? ImageHandleType = null,
    string? PresentationPath = null);

/// <summary>
/// Presents exportable Vulkan images to Avalonia composition without a native window.
/// </summary>
public sealed unsafe class VulkanCompositionViewportPresenter
    : ICompositionViewportPresenter
{
    private readonly VulkanCompositionContext _context;
    private readonly VulkanCompositionRenderCallback? _renderCallback;
    private readonly bool _required;
    private SilkMeshRenderer? _renderer;
    private SilkMeshRenderResult? _lastMeshRenderResult;
    private int _generationCount;
    private int _frameCount;
    private long _presentedFrames;
    private long _ringReuseFrames;
    private long _renderCallbacks;
    private bool _compatible;
    private bool _disposed;

    private VulkanCompositionViewportPresenter(
        VulkanCompositionContext context,
        VulkanCompositionRenderCallback? renderCallback,
        bool required)
    {
        _context = context;
        _renderCallback = renderCallback;
        _required = required;
    }

    /// <summary>Creates a headless Vulkan composition presenter.</summary>
    /// <param name="required">
    /// Throws from probing instead of reporting unavailable when composition interop is required.
    /// </param>
    public static VulkanCompositionViewportPresenter Create(bool required = false) =>
        new(VulkanCompositionContext.Create(), null, required);

    /// <summary>
    /// Creates a presenter that renders retained hdSilk meshes into each exported image.
    /// </summary>
    public static VulkanCompositionViewportPresenter Create(
        VulkanCompositionRenderCallback renderCallback,
        bool required = false)
    {
        ArgumentNullException.ThrowIfNull(renderCallback);
        return new VulkanCompositionViewportPresenter(
            VulkanCompositionContext.Create(),
            renderCallback,
            required);
    }

    /// <summary>Captures current presenter resources and render evidence.</summary>
    public VulkanCompositionPresenterDiagnostics GetDiagnostics() =>
        new(
            Volatile.Read(ref _generationCount),
            Volatile.Read(ref _frameCount),
            Interlocked.Read(ref _presentedFrames),
            Interlocked.Read(ref _ringReuseFrames),
            Interlocked.Read(ref _renderCallbacks),
            _lastMeshRenderResult,
            _context.SelectedImageHandleType,
            _context.UsesD3D11Bridge ? "D3D11GpuCopy" : "DirectVulkan");

    internal bool IsDeviceCreatedForTesting => _context.IsDeviceCreated;

    /// <inheritdoc/>
    public ValueTask<CompositionPresenterProbeResult> ProbeAsync(
        CompositionPresentationTarget target,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        CompositionPresenterProbeResult result = _context.ProbeAndInitialize(target);
        result = EnforceRequired(result, _required);
        _compatible = result.IsAvailable;
        if (_compatible && _renderCallback is not null && _renderer is null)
        {
            _renderer = new SilkMeshRenderer(_context.GraphicsDevice);
        }
        return new ValueTask<CompositionPresenterProbeResult>(result);
    }

    /// <inheritdoc/>
    public ValueTask<ICompositionPresentationGeneration> CreateGenerationAsync(
        ViewportDimensions size,
        int frameCount,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_compatible)
        {
            throw new InvalidOperationException(
                "ProbeAsync must report a compatible compositor before creating a generation.");
        }
        if (size.Width <= 0 || size.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }
        ArgumentOutOfRangeException.ThrowIfLessThan(frameCount, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(frameCount, 3);

        var generation = new VulkanCompositionGeneration(
            this,
            _context,
            _renderer,
            _renderCallback,
            size,
            frameCount);
        _generationCount++;
        return new ValueTask<ICompositionPresentationGeneration>(generation);
    }

    /// <inheritdoc/>
    public ValueTask<CompositionFrameRenderResult> RenderAsync(
        ICompositionPresentationFrame frame,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (frame is not VulkanCompositionFrame nativeFrame ||
            !ReferenceEquals(nativeFrame.Owner, this))
        {
            throw new ArgumentException(
                "The presentation frame was not created by this Vulkan presenter.",
                nameof(frame));
        }

        bool reused = nativeFrame.WasPresented;
        CompositionFrameRenderResult result = nativeFrame.Render();
        if (result.Status == CompositionFrameRenderStatus.Presented)
        {
            Interlocked.Increment(ref _presentedFrames);
            if (reused)
            {
                Interlocked.Increment(ref _ringReuseFrames);
            }
        }
        return new ValueTask<CompositionFrameRenderResult>(result);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }
        if (_generationCount != 0)
        {
            throw new InvalidOperationException(
                "Dispose all Vulkan composition generations before disposing the presenter.");
        }

        _renderer?.Dispose();
        _renderer = null;
        _context.Dispose();
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    internal void ReleaseGeneration()
    {
        _generationCount--;
    }

    internal void RegisterFrame() => _frameCount++;

    internal void ReleaseFrame() => _frameCount--;

    internal void RecordMeshRender(SilkMeshRenderResult result)
    {
        _lastMeshRenderResult = result;
        Interlocked.Increment(ref _renderCallbacks);
    }

    internal static CompositionPresenterProbeResult EnforceRequired(
        CompositionPresenterProbeResult result,
        bool required)
    {
        if (required && !result.IsAvailable)
        {
            throw new InvalidOperationException(
                $"Required Vulkan composition presentation is unavailable: {result.Status}");
        }
        return result;
    }
}

internal sealed class VulkanCompositionGeneration : ICompositionPresentationGeneration
{
    private readonly VulkanCompositionViewportPresenter _owner;
    private readonly VulkanCompositionFrame[] _frames;
    private bool _disposed;

    internal VulkanCompositionGeneration(
        VulkanCompositionViewportPresenter owner,
        VulkanCompositionContext context,
        SilkMeshRenderer? renderer,
        VulkanCompositionRenderCallback? renderCallback,
        ViewportDimensions size,
        int frameCount)
    {
        _owner = owner;
        Size = size;
        _frames = new VulkanCompositionFrame[frameCount];
        int created = 0;
        try
        {
            for (; created < frameCount; created++)
            {
                _frames[created] = new VulkanCompositionFrame(
                    owner,
                    context,
                    renderer,
                    renderCallback,
                    size,
                    created);
            }
        }
        catch
        {
            for (int index = created - 1; index >= 0; index--)
            {
                _frames[index].Dispose();
            }
            throw;
        }
    }

    public ViewportDimensions Size { get; }

    public IReadOnlyList<ICompositionPresentationFrame> Frames => _frames;

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        List<Exception>? failures = null;
        for (int index = _frames.Length - 1; index >= 0; index--)
        {
            try
            {
                _frames[index].Dispose();
            }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(exception);
            }
        }
        _disposed = true;
        _owner.ReleaseGeneration();
        if (failures is not null)
        {
            throw new AggregateException(
                "One or more Vulkan presentation frames failed to dispose.",
                failures);
        }
        return ValueTask.CompletedTask;
    }
}

internal sealed unsafe class VulkanCompositionFrame : ICompositionPresentationFrame
{
    private static long _nextAllocationId;

    private readonly VulkanCompositionContext _context;
    private readonly CommandPool _commandPool;
    private readonly CommandBuffer _firstCommands;
    private readonly CommandBuffer _reusedCommands;
    private readonly Image _image;
    private readonly DeviceMemory _memory;
    private readonly ImageView _imageView;
    private readonly Semaphore _renderReady;
    private readonly Semaphore _compositorRelease;
    private readonly SafeHandle _memoryHandle;
    private readonly SafeHandle? _renderReadyHandle;
    private readonly SafeHandle? _compositorReleaseHandle;
    private readonly VulkanD3D11SharedTexture? _d3d11Texture;
    private readonly ISilkGraphicsTexture? _colorTarget;
    private readonly ISilkGraphicsTexture? _depthTarget;
    private readonly SilkMeshRenderer? _renderer;
    private readonly VulkanCompositionRenderCallback? _renderCallback;
    private readonly CompositionExternalSemaphore[] _semaphores;
    private readonly CompositionFrameSynchronization _synchronization;
    private readonly int _frameIndex;
    private int _useCount;
    private bool _presented;
    private bool _disposed;

    internal VulkanCompositionFrame(
        VulkanCompositionViewportPresenter owner,
        VulkanCompositionContext context,
        SilkMeshRenderer? renderer,
        VulkanCompositionRenderCallback? renderCallback,
        ViewportDimensions size,
        int frameIndex)
    {
        Owner = owner;
        _context = context;
        _renderer = renderer;
        _renderCallback = renderCallback;
        _frameIndex = frameIndex;
        AllocationId = Interlocked.Increment(ref _nextAllocationId);

        Image image = default;
        DeviceMemory memory = default;
        ImageView imageView = default;
        Semaphore renderReady = default;
        Semaphore compositorRelease = default;
        CommandPool commandPool = default;
        CommandBuffer firstCommands = default;
        CommandBuffer reusedCommands = default;
        SafeHandle? memoryHandle = null;
        SafeHandle? renderReadyHandle = null;
        SafeHandle? compositorReleaseHandle = null;
        ISilkGraphicsTexture? colorTarget = null;
        ISilkGraphicsTexture? depthTarget = null;
        VulkanD3D11SharedTexture? d3d11Texture = null;
        ulong memorySize = 0;
        bool success = false;
        try
        {
            if (context.UsesD3D11Bridge)
            {
                if (!OperatingSystem.IsWindows())
                {
                    throw new PlatformNotSupportedException(
                        "The D3D11 Vulkan bridge is available only on Windows.");
                }
                d3d11Texture = context.CreateD3D11ImportedImage(
                    size,
                    out image,
                    out memory,
                    out imageView);
                memoryHandle = d3d11Texture.Handle;
                colorTarget = context.GraphicsDevice.CreateTexture2D(
                    SilkTextureDescriptor.ColorTarget(
                        checked((uint)size.Width),
                        checked((uint)size.Height)));
                if (renderCallback is not null)
                {
                    depthTarget = context.GraphicsDevice.CreateTexture2D(
                        SilkTextureDescriptor.SampledDepthTarget(
                            checked((uint)size.Width),
                            checked((uint)size.Height)));
                }
                context.CreateD3D11CopyCommands(
                    ((VulkanSilkGraphicsTexture)colorTarget).Image,
                    image,
                    size,
                    sourceNeedsTransferTransition: renderCallback is null,
                    out commandPool,
                    out firstCommands,
                    out reusedCommands);
            }
            else
            {
                context.CreateExportableImage(
                    size,
                    out image,
                    out memory,
                    out imageView,
                    out memorySize);
                renderReady = context.CreateExportableSemaphore();
                compositorRelease = context.CreateExportableSemaphore();
                context.CreateFrameCommands(
                    image,
                    frameIndex,
                    out commandPool,
                    out firstCommands,
                    out reusedCommands);
                memoryHandle = context.ExportMemoryHandle(memory);
                renderReadyHandle = context.ExportSemaphoreHandle(renderReady);
                compositorReleaseHandle = context.ExportSemaphoreHandle(compositorRelease);
                if (renderCallback is not null)
                {
                    colorTarget = context.GraphicsDevice.WrapBorrowedColorTarget(
                        image,
                        imageView,
                        checked((uint)size.Width),
                        checked((uint)size.Height));
                    depthTarget = context.GraphicsDevice.CreateTexture2D(
                        SilkTextureDescriptor.SampledDepthTarget(
                            checked((uint)size.Width),
                            checked((uint)size.Height)));
                }
            }
            if (renderCallback is not null && depthTarget is null)
            {
                depthTarget = context.GraphicsDevice.CreateTexture2D(
                    SilkTextureDescriptor.SampledDepthTarget(
                        checked((uint)size.Width),
                        checked((uint)size.Height)));
            }
            if (colorTarget is null && renderCallback is not null)
            {
                colorTarget = context.GraphicsDevice.WrapBorrowedColorTarget(
                    image,
                    imageView,
                    checked((uint)size.Width),
                    checked((uint)size.Height));
            }

            _image = image;
            _memory = memory;
            _imageView = imageView;
            _renderReady = renderReady;
            _compositorRelease = compositorRelease;
            _commandPool = commandPool;
            _firstCommands = firstCommands;
            _reusedCommands = reusedCommands;
            _memoryHandle = memoryHandle;
            _renderReadyHandle = renderReadyHandle;
            _compositorReleaseHandle = compositorReleaseHandle;
            _d3d11Texture = d3d11Texture;
            _colorTarget = colorTarget;
            _depthTarget = depthTarget;

            Image = new CompositionExternalImage(
                context.Capabilities.ImageHandleType,
                size,
                CompositionExternalImageFormat.R8G8B8A8UNorm,
                memoryOffset: 0,
                memorySize,
                topLeftOrigin: true);
            if (context.UsesD3D11Bridge)
            {
                _semaphores = [];
                _synchronization = CompositionFrameSynchronization.KeyedMutex(1, 0);
            }
            else
            {
                long readyId = checked((AllocationId * 2) - 1);
                long releaseId = checked(AllocationId * 2);
                _semaphores =
                [
                    new CompositionExternalSemaphore(
                        readyId,
                        context.Capabilities.SemaphoreHandleType),
                    new CompositionExternalSemaphore(
                        releaseId,
                        context.Capabilities.SemaphoreHandleType)
                ];
                _synchronization = CompositionFrameSynchronization.Semaphores(
                    readyId,
                    releaseId);
            }
            owner.RegisterFrame();
            success = true;
        }
        finally
        {
            if (!success)
            {
                compositorReleaseHandle?.Dispose();
                renderReadyHandle?.Dispose();
                memoryHandle?.Dispose();
                depthTarget?.Dispose();
                colorTarget?.Dispose();
                context.DestroyFrameResources(
                    commandPool,
                    compositorRelease,
                    renderReady,
                    imageView,
                    image,
                    memory);
                if (OperatingSystem.IsWindows())
                {
                    d3d11Texture?.Dispose();
                }
            }
        }
    }

    internal VulkanCompositionViewportPresenter Owner { get; }

    internal bool WasPresented => _presented;

    public long AllocationId { get; }

    public CompositionExternalImage Image { get; }

    public IReadOnlyList<CompositionExternalSemaphore> Semaphores => _semaphores;

    public ValueTask<ICompositionExternalHandleLease> LeaseImageHandleAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<ICompositionExternalHandleLease>(
            VulkanCompositionContext.LeaseHandle(_memoryHandle, Image.HandleType));
    }

    public ValueTask<ICompositionExternalHandleLease> LeaseSemaphoreHandleAsync(
        long resourceId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_context.UsesD3D11Bridge)
        {
            throw new NotSupportedException(
                $"D3D11 keyed-mutex frame {AllocationId} has no external semaphores.");
        }
        SafeHandle source = resourceId switch
        {
            var id when id == _semaphores[0].ResourceId => _renderReadyHandle!,
            var id when id == _semaphores[1].ResourceId => _compositorReleaseHandle!,
            _ => throw new ArgumentOutOfRangeException(
                nameof(resourceId),
                resourceId,
                "The semaphore does not belong to this presentation frame.")
        };
        return new ValueTask<ICompositionExternalHandleLease>(
            VulkanCompositionContext.LeaseHandle(
                source,
                _context.Capabilities.SemaphoreHandleType));
    }

    internal CompositionFrameRenderResult Render()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Result result;
        if (_context.UsesD3D11Bridge)
        {
            if (_renderCallback is null)
            {
                using ISilkGraphicsCommandList commands =
                    _context.GraphicsDevice.CreateCommandList();
                commands.ClearColor(
                    _colorTarget!,
                    new SilkColor(
                        0.0625f,
                        0.125f,
                        0.25f + (_frameIndex * 0.125f),
                        1));
                using ISilkGraphicsSubmission submission =
                    _context.GraphicsDevice.Submit(commands);
                submission.Wait();
            }
            else
            {
                int useCount = checked(++_useCount);
                SilkMeshRenderResult meshResult = _renderCallback(
                    new VulkanCompositionRenderContext(
                        _renderer!,
                        _colorTarget!,
                        _depthTarget!,
                        AllocationId,
                        _frameIndex,
                        useCount));
                Owner.RecordMeshRender(meshResult);
            }
            result = _context.SubmitD3D11Frame(
                _presented ? _reusedCommands : _firstCommands,
                _memory);
        }
        else if (_renderCallback is null)
        {
            result = _context.SubmitFrame(
                _presented ? _reusedCommands : _firstCommands,
                _presented ? _compositorRelease : default,
                _renderReady);
        }
        else
        {
            if (_presented)
            {
                _context.WaitForCompositorRelease(_compositorRelease);
            }
            int useCount = checked(++_useCount);
            SilkMeshRenderResult meshResult = _renderCallback(
                new VulkanCompositionRenderContext(
                    _renderer!,
                    _colorTarget!,
                    _depthTarget!,
                    AllocationId,
                    _frameIndex,
                    useCount));
            Owner.RecordMeshRender(meshResult);
            result = _context.SignalRenderReady(_renderReady);
        }
        if (result is Result.ErrorDeviceLost)
        {
            return new CompositionFrameRenderResult(
                CompositionFrameRenderStatus.DeviceLost,
                ContinueRendering: false,
                _synchronization);
        }
        VulkanSilkGraphicsDevice.ThrowIfFailed(result, "vkQueueSubmit");
        _presented = true;
        return new CompositionFrameRenderResult(
            CompositionFrameRenderStatus.Presented,
            ContinueRendering: true,
            _synchronization);
    }

    internal void CompleteCompositorRoundTripForTesting()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_presented)
        {
            throw new InvalidOperationException(
                "A frame must be rendered before simulating compositor consumption.");
        }
        if (_context.UsesD3D11Bridge)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException(
                    "The D3D11 Vulkan bridge is available only on Windows.");
            }
            _d3d11Texture!.CompleteConsumerRoundTrip();
        }
        else
        {
            _context.CompleteCompositorRoundTrip(_renderReady, _compositorRelease);
        }
    }

    internal void ReadbackD3D11SharedTextureForTesting(Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!OperatingSystem.IsWindows() || !_context.UsesD3D11Bridge)
        {
            throw new PlatformNotSupportedException(
                "D3D11 shared-texture readback requires the Windows bridge.");
        }
        _context.ReadbackD3D11SharedTextureForTesting(
            _memoryHandle.DangerousGetHandle(),
            destination);
    }

    internal void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _context.WaitIdle();
        _depthTarget?.Dispose();
        _colorTarget?.Dispose();
        _compositorReleaseHandle?.Dispose();
        _renderReadyHandle?.Dispose();
        _memoryHandle.Dispose();
        _context.DestroyFrameResources(
            _commandPool,
            _compositorRelease,
            _renderReady,
            _imageView,
            _image,
            _memory);
        if (OperatingSystem.IsWindows())
        {
            _d3d11Texture?.Dispose();
        }
        _disposed = true;
        Owner.ReleaseFrame();
    }
}

internal sealed record class VulkanCompositionCapabilitySnapshot
{
    internal required uint ApiVersion { get; init; }

    internal required string ImageHandleType { get; init; }

    internal required string SemaphoreHandleType { get; init; }

    internal required IReadOnlyList<string> MissingExtensions { get; init; }

    internal required bool ImageExportable { get; init; }

    internal required bool SemaphoreExportable { get; init; }

    internal required bool HasGraphicsComputeQueue { get; init; }

    internal required uint QueueFamilyIndex { get; init; }

    internal required bool DeviceLuidValid { get; init; }

    internal required byte[] DeviceLuid { get; init; }

    internal required byte[] DeviceUuid { get; init; }

    internal bool D3D11ImageImportable { get; init; }

    internal bool D3D11DirectRenderSupported { get; init; }

    internal IReadOnlyList<string> D3D11MissingExtensions { get; init; } = [];
}

internal static class VulkanCompositionCompatibility
{
    internal static CompositionPresenterProbeResult Probe(
        CompositionPresentationTarget target,
        VulkanCompositionCapabilitySnapshot capabilities)
    {
        if (Contains(
            target.ImageHandleTypes,
            VulkanCompositionContext.WindowsD3D11ImageHandleType))
        {
            CompositionPresenterProbeResult common = ProbeCommon(target, capabilities);
            if (!common.IsAvailable)
            {
                return common;
            }
            if (target.DeviceLuid.Count != Vk.LuidSize)
            {
                return CompositionPresenterProbeResult.Unavailable(
                    "The D3D11 Vulkan bridge requires an 8-byte compositor adapter LUID.");
            }
            if (capabilities.D3D11MissingExtensions.Count != 0)
            {
                return CompositionPresenterProbeResult.Unavailable(
                    "Vulkan D3D11 texture import extensions are unavailable: " +
                    string.Join(", ", capabilities.D3D11MissingExtensions));
            }
            if (!capabilities.D3D11ImageImportable)
            {
                return CompositionPresenterProbeResult.Unavailable(
                    "The Vulkan adapter cannot import RGBA8 D3D11 shared textures.");
            }
            return CompositionPresenterProbeResult.Available(
                "Vulkan composition available through D3D11TextureNtHandle " +
                (capabilities.D3D11DirectRenderSupported
                    ? "(direct render)"
                    : "(GPU copy fallback)"));
        }

        if (!Contains(target.ImageHandleTypes, capabilities.ImageHandleType))
        {
            return CompositionPresenterProbeResult.Unavailable(
                $"The compositor does not accept {capabilities.ImageHandleType} Vulkan images; " +
                $"supported image handles: {Describe(target.ImageHandleTypes)}.");
        }
        if (!Contains(target.SemaphoreHandleTypes, capabilities.SemaphoreHandleType))
        {
            return CompositionPresenterProbeResult.Unavailable(
                $"The compositor does not accept {capabilities.SemaphoreHandleType} Vulkan semaphores; " +
                $"supported semaphore handles: {Describe(target.SemaphoreHandleTypes)}.");
        }
        CompositionPresenterProbeResult commonResult = ProbeCommon(target, capabilities);
        if (!commonResult.IsAvailable)
        {
            return commonResult;
        }
        if (capabilities.MissingExtensions.Count != 0)
        {
            return CompositionPresenterProbeResult.Unavailable(
                "Vulkan external-object extensions are unavailable: " +
                string.Join(", ", capabilities.MissingExtensions));
        }
        if (!capabilities.ImageExportable)
        {
            return CompositionPresenterProbeResult.Unavailable(
                "The Vulkan adapter cannot export RGBA8 optimal images with the compositor handle type.");
        }
        if (!capabilities.SemaphoreExportable)
        {
            return CompositionPresenterProbeResult.Unavailable(
                "The Vulkan adapter cannot export binary semaphores with the compositor handle type.");
        }

        return CompositionPresenterProbeResult.Available(
            $"Vulkan composition available through {capabilities.ImageHandleType}");
    }

    internal static bool SelectsD3D11Bridge(
        CompositionPresentationTarget target,
        VulkanCompositionCapabilitySnapshot capabilities) =>
        Contains(
            target.ImageHandleTypes,
            VulkanCompositionContext.WindowsD3D11ImageHandleType) &&
        capabilities.D3D11ImageImportable &&
        capabilities.D3D11MissingExtensions.Count == 0;

    private static CompositionPresenterProbeResult ProbeCommon(
        CompositionPresentationTarget target,
        VulkanCompositionCapabilitySnapshot capabilities)
    {
        if (capabilities.ApiVersion < Vk.Version11)
        {
            return CompositionPresenterProbeResult.Unavailable(
                "Vulkan composition presentation requires Vulkan 1.1 or newer.");
        }
        if (!capabilities.HasGraphicsComputeQueue)
        {
            return CompositionPresenterProbeResult.Unavailable(
                "The Vulkan adapter has no queue family supporting both graphics and compute.");
        }
        if (!MatchesAdapter(
            target.DeviceLuid,
            capabilities.DeviceLuidValid,
            capabilities.DeviceLuid))
        {
            return CompositionPresenterProbeResult.Unavailable(
                "The Vulkan renderer and compositor use different adapter LUIDs.");
        }
        if (!MatchesAdapter(
            target.DeviceUuid,
            capabilities.DeviceUuid.Length != 0,
            capabilities.DeviceUuid))
        {
            return CompositionPresenterProbeResult.Unavailable(
                "The Vulkan renderer and compositor use different adapter UUIDs.");
        }

        return CompositionPresenterProbeResult.Available("Vulkan adapter compatible.");
    }

    private static bool Contains(IReadOnlyList<string> values, string expected)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (string.Equals(values[index], expected, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static string Describe(IReadOnlyList<string> values) =>
        values.Count == 0 ? "(none)" : string.Join(", ", values);

    private static bool MatchesAdapter(
        IReadOnlyList<byte> compositorIdentifier,
        bool rendererIdentifierValid,
        byte[] rendererIdentifier)
    {
        if (compositorIdentifier.Count == 0)
        {
            return true;
        }
        if (!rendererIdentifierValid)
        {
            return false;
        }
        if (rendererIdentifier.Length != compositorIdentifier.Count)
        {
            return false;
        }
        for (int index = 0; index < compositorIdentifier.Count; index++)
        {
            if (compositorIdentifier[index] != rendererIdentifier[index])
            {
                return false;
            }
        }
        return true;
    }
}

internal readonly record struct VulkanCompositionSelectionResult(
    int CandidateIndex,
    CompositionPresenterProbeResult ProbeResult);

internal static class VulkanCompositionDeviceSelection
{
    internal static VulkanCompositionSelectionResult Select(
        CompositionPresentationTarget target,
        IReadOnlyList<VulkanCompositionCapabilitySnapshot> candidates)
    {
        if (candidates.Count == 0)
        {
            return new VulkanCompositionSelectionResult(
                -1,
                CompositionPresenterProbeResult.Unavailable(
                    "No Vulkan physical device is available."));
        }

        CompositionPresenterProbeResult firstFailure = default;
        for (int index = 0; index < candidates.Count; index++)
        {
            CompositionPresenterProbeResult result =
                VulkanCompositionCompatibility.Probe(target, candidates[index]);
            if (result.IsAvailable)
            {
                return new VulkanCompositionSelectionResult(index, result);
            }
            if (index == 0)
            {
                firstFailure = result;
            }
        }
        return new VulkanCompositionSelectionResult(-1, firstFailure);
    }
}

internal static class VulkanCompositionBarriers
{
    internal static ImageMemoryBarrier CreateAcquire(
        Image image,
        ImageSubresourceRange range,
        bool firstUse) =>
        new()
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = 0,
            DstAccessMask = AccessFlags.TransferWriteBit,
            OldLayout = firstUse
                ? ImageLayout.Undefined
                : ImageLayout.TransferSrcOptimal,
            NewLayout = ImageLayout.TransferDstOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = range
        };

    internal static ImageMemoryBarrier CreateRelease(
        Image image,
        ImageSubresourceRange range) =>
        new()
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = 0,
            OldLayout = ImageLayout.TransferDstOptimal,
            NewLayout = ImageLayout.TransferSrcOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = range
        };
}

internal static class VulkanCompositionDeviceRules
{
    internal static bool TryFindGraphicsComputeQueue(
        IReadOnlyList<QueueFlags> queues,
        out uint queueFamily)
    {
        QueueFlags required = QueueFlags.GraphicsBit | QueueFlags.ComputeBit;
        for (int index = 0; index < queues.Count; index++)
        {
            if ((queues[index] & required) == required)
            {
                queueFamily = checked((uint)index);
                return true;
            }
        }
        queueFamily = 0;
        return false;
    }
}

internal sealed unsafe class VulkanCompositionContext : IDisposable
{
    internal const string WindowsImageHandleType = "VulkanOpaqueNtHandle";
    internal const string WindowsSemaphoreHandleType = "VulkanOpaqueNtHandle";
    internal const string WindowsD3D11ImageHandleType = "D3D11TextureNtHandle";
    internal const string LinuxImageHandleType = "VulkanOpaquePosixFileDescriptor";
    internal const string LinuxSemaphoreHandleType = "VulkanOpaquePosixFileDescriptor";

    private const string ExternalMemoryWin32Extension = "VK_KHR_external_memory_win32";
    private const string ExternalSemaphoreWin32Extension = "VK_KHR_external_semaphore_win32";
    private const string Win32KeyedMutexExtension = "VK_KHR_win32_keyed_mutex";
    private const string ExternalMemoryFdExtension = "VK_KHR_external_memory_fd";
    private const string ExternalSemaphoreFdExtension = "VK_KHR_external_semaphore_fd";

    private readonly Vk _api;
    private readonly Instance _instance;
    private Device _device;
    private Queue _queue;
    private uint _queueFamily;
    private PhysicalDeviceMemoryProperties _memoryProperties;
    private VulkanSilkGraphicsDevice? _graphicsDevice;
    private nint _getMemoryHandle;
    private nint _getSemaphoreHandle;
    private nint _getMemoryWin32HandleProperties;
    private VulkanD3D11Bridge? _d3d11Bridge;
    private bool _usesD3D11Bridge;
    private VulkanCompositionCapabilitySnapshot? _capabilities;
    private bool _disposed;

    private VulkanCompositionContext(Vk api, Instance instance)
    {
        _api = api;
        _instance = instance;
    }

    internal VulkanCompositionCapabilitySnapshot Capabilities =>
        _capabilities ??
        throw new InvalidOperationException("The Vulkan presentation device is not initialized.");

    internal bool IsDeviceCreated => _device.Handle != 0;

    internal bool UsesD3D11Bridge => _usesD3D11Bridge;

    internal string? SelectedImageHandleType => _capabilities?.ImageHandleType;

    internal VulkanSilkGraphicsDevice GraphicsDevice =>
        _graphicsDevice ??
        throw new InvalidOperationException("The Vulkan presentation device is not initialized.");

    internal static VulkanCompositionContext Create()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                "Vulkan composition presentation is supported on Windows and Linux.");
        }

        Vk api = new(SilkNativeLibraryContext.Load(GetVulkanLibraryNames()));
        Instance instance = default;
        try
        {
            var application = new ApplicationInfo
            {
                SType = StructureType.ApplicationInfo,
                ApiVersion = Vk.Version11
            };
            var instanceInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &application
            };
            Result result = api.CreateInstance(&instanceInfo, null, &instance);
            if (result is Result.ErrorIncompatibleDriver)
            {
                throw new PlatformNotSupportedException(
                    "Vulkan composition presentation requires a Vulkan 1.1 loader.");
            }
            VulkanSilkGraphicsDevice.ThrowIfFailed(result, "vkCreateInstance");
            return new VulkanCompositionContext(api, instance);
        }
        catch
        {
            if (instance.Handle != 0)
            {
                api.DestroyInstance(instance, null);
            }
            api.Dispose();
            throw;
        }
    }

    internal CompositionPresenterProbeResult ProbeAndInitialize(
        CompositionPresentationTarget target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_device.Handle != 0)
        {
            return VulkanCompositionCompatibility.Probe(target, Capabilities);
        }

        PhysicalDevice[] physicalDevices = GetPhysicalDevices(_api, _instance);
        var candidates =
            new VulkanCompositionCapabilitySnapshot[physicalDevices.Length];
        for (int index = 0; index < physicalDevices.Length; index++)
        {
            candidates[index] = GetCapabilities(_api, physicalDevices[index]);
        }
        VulkanCompositionSelectionResult selection =
            VulkanCompositionDeviceSelection.Select(target, candidates);
        if (!selection.ProbeResult.IsAvailable)
        {
            return selection.ProbeResult;
        }

        VulkanCompositionCapabilitySnapshot capabilities =
            candidates[selection.CandidateIndex];
        _usesD3D11Bridge =
            VulkanCompositionCompatibility.SelectsD3D11Bridge(target, capabilities);
        if (_usesD3D11Bridge)
        {
            capabilities = capabilities with
            {
                ImageHandleType = WindowsD3D11ImageHandleType,
                SemaphoreHandleType = string.Empty
            };
        }
        InitializeDevice(physicalDevices[selection.CandidateIndex], capabilities);
        if (_usesD3D11Bridge)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException(
                    "The D3D11 Vulkan bridge is available only on Windows.");
            }
            try
            {
                _d3d11Bridge = VulkanD3D11Bridge.Create(capabilities.DeviceLuid);
            }
            catch (PlatformNotSupportedException exception)
            {
                return CompositionPresenterProbeResult.Unavailable(
                    $"The D3D11 Vulkan bridge could not initialize: {exception.Message}");
            }
        }
        return selection.ProbeResult;
    }

    private void InitializeDevice(
        PhysicalDevice physicalDevice,
        VulkanCompositionCapabilitySnapshot capabilities)
    {
        string[] requiredExtensions = GetRequiredDeviceExtensions(_usesD3D11Bridge);
        using GlobalMemory extensionNames = SilkMarshal.StringArrayToMemory(
            requiredExtensions,
            NativeStringEncoding.UTF8);
        float queuePriority = 1;
        var queueInfo = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = capabilities.QueueFamilyIndex,
            QueueCount = 1,
            PQueuePriorities = &queuePriority
        };
        var deviceInfo = new DeviceCreateInfo
        {
            SType = StructureType.DeviceCreateInfo,
            QueueCreateInfoCount = 1,
            PQueueCreateInfos = &queueInfo,
            EnabledExtensionCount = checked((uint)requiredExtensions.Length),
            PpEnabledExtensionNames = (byte**)extensionNames.Handle
        };
        Device device = default;
        try
        {
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                _api.CreateDevice(physicalDevice, &deviceInfo, null, &device),
                "vkCreateDevice");
            _api.GetDeviceQueue(
                device,
                capabilities.QueueFamilyIndex,
                0,
                out Queue queue);
            _api.GetPhysicalDeviceMemoryProperties(
                physicalDevice,
                out PhysicalDeviceMemoryProperties memoryProperties);
            _api.GetPhysicalDeviceProperties(
                physicalDevice,
                out PhysicalDeviceProperties properties);
            nint getMemoryHandle = 0;
            nint getSemaphoreHandle = 0;
            nint getMemoryWin32HandleProperties = 0;
            if (_usesD3D11Bridge)
            {
                getMemoryWin32HandleProperties = GetRequiredDeviceFunction(
                    _api,
                    device,
                    "vkGetMemoryWin32HandlePropertiesKHR");
            }
            else
            {
                getMemoryHandle = GetRequiredDeviceFunction(
                    _api,
                    device,
                    GetMemoryExportFunctionName());
                getSemaphoreHandle = GetRequiredDeviceFunction(
                    _api,
                    device,
                    GetSemaphoreExportFunctionName());
            }

            _device = device;
            _queue = queue;
            _queueFamily = capabilities.QueueFamilyIndex;
            _memoryProperties = memoryProperties;
            _getMemoryHandle = getMemoryHandle;
            _getSemaphoreHandle = getSemaphoreHandle;
            _getMemoryWin32HandleProperties = getMemoryWin32HandleProperties;
            _capabilities = capabilities;
            byte* namePointer = properties.DeviceName;
            string deviceName = Marshal.PtrToStringUTF8((nint)namePointer) ?? "Vulkan Device";
            uint major = properties.ApiVersion >> 22;
            uint minor = (properties.ApiVersion >> 12) & 0x3ff;
            uint patch = properties.ApiVersion & 0xfff;
            _graphicsDevice = VulkanSilkGraphicsDevice.CreateBorrowed(
                _api,
                _instance,
                physicalDevice,
                device,
                queue,
                capabilities.QueueFamilyIndex,
                memoryProperties,
                new SilkGraphicsCapabilities(
                    deviceName,
                    $"{major}.{minor}.{patch}",
                    SupportsCompute: true,
                    IsSoftware: properties.DeviceType == PhysicalDeviceType.Cpu));
        }
        catch
        {
            if (device.Handle != 0)
            {
                _api.DestroyDevice(device, null);
            }
            throw;
        }
    }

    internal void CreateExportableImage(
        ViewportDimensions size,
        out Image image,
        out DeviceMemory memory,
        out ImageView imageView,
        out ulong memorySize)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ExternalMemoryHandleTypeFlags handleType = GetMemoryHandleType();
        var externalInfo = new ExternalMemoryImageCreateInfo
        {
            SType = StructureType.ExternalMemoryImageCreateInfo,
            HandleTypes = handleType
        };
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            PNext = &externalInfo,
            Flags = (ImageCreateFlags)0x00000008,
            ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            Extent = new Extent3D(
                checked((uint)size.Width),
                checked((uint)size.Height),
                1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = GetExternalImageUsage(),
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        image = default;
        memory = default;
        imageView = default;
        memorySize = 0;
        Image createdImage = default;
        DeviceMemory allocatedMemory = default;
        ImageView createdImageView = default;
        bool success = false;
        try
        {
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                _api.CreateImage(_device, &imageInfo, null, &createdImage),
                "vkCreateImage");
            _api.GetImageMemoryRequirements(
                _device,
                createdImage,
                out MemoryRequirements requirements);
            memorySize = requirements.Size;
            var dedicatedInfo = new MemoryDedicatedAllocateInfo
            {
                SType = StructureType.MemoryDedicatedAllocateInfo,
                Image = createdImage
            };
            var exportInfo = new ExportMemoryAllocateInfo
            {
                SType = StructureType.ExportMemoryAllocateInfo,
                PNext = &dedicatedInfo,
                HandleTypes = handleType
            };
            var allocationInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                PNext = &exportInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = FindMemoryType(
                    requirements.MemoryTypeBits,
                    MemoryPropertyFlags.DeviceLocalBit)
            };
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                _api.AllocateMemory(_device, &allocationInfo, null, &allocatedMemory),
                "vkAllocateMemory");
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                _api.BindImageMemory(_device, createdImage, allocatedMemory, 0),
                "vkBindImageMemory");
            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = createdImage,
                ViewType = ImageViewType.Type2D,
                Format = Format.R8G8B8A8Unorm,
                SubresourceRange = new ImageSubresourceRange(
                    ImageAspectFlags.ColorBit,
                    0,
                    1,
                    0,
                    1)
            };
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                _api.CreateImageView(_device, &viewInfo, null, &createdImageView),
                "vkCreateImageView");
            image = createdImage;
            memory = allocatedMemory;
            imageView = createdImageView;
            success = true;
        }
        finally
        {
            if (!success)
            {
                if (createdImageView.Handle != 0)
                {
                    _api.DestroyImageView(_device, createdImageView, null);
                }
                if (createdImage.Handle != 0)
                {
                    _api.DestroyImage(_device, createdImage, null);
                }
                if (allocatedMemory.Handle != 0)
                {
                    _api.FreeMemory(_device, allocatedMemory, null);
                }
            }
        }
    }

    internal VulkanD3D11SharedTexture CreateD3D11ImportedImage(
            ViewportDimensions size,
            out Image image,
            out DeviceMemory memory,
            out ImageView imageView)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "The D3D11 Vulkan bridge is available only on Windows.");
        }
        VulkanD3D11Bridge bridge = _d3d11Bridge ??
            throw new InvalidOperationException("The D3D11 Vulkan bridge is not initialized.");
        VulkanD3D11SharedTexture sharedTexture = bridge.CreateSharedTexture(size);
        image = default;
        memory = default;
        imageView = default;
        Image createdImage = default;
        DeviceMemory allocatedMemory = default;
        ImageView createdImageView = default;
        bool success = false;
        try
        {
            ExternalMemoryHandleTypeFlags handleType = GetD3D11MemoryHandleType();
            var externalInfo = new ExternalMemoryImageCreateInfo
            {
                SType = StructureType.ExternalMemoryImageCreateInfo,
                HandleTypes = handleType
            };
            var imageInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                PNext = &externalInfo,
                ImageType = ImageType.Type2D,
                Format = Format.R8G8B8A8Unorm,
                Extent = new Extent3D(
                    checked((uint)size.Width),
                    checked((uint)size.Height),
                    1),
                MipLevels = 1,
                ArrayLayers = 1,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = ImageUsageFlags.TransferDstBit |
                    ImageUsageFlags.SampledBit,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined
            };
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                _api.CreateImage(_device, &imageInfo, null, &createdImage),
                "vkCreateImage(D3D11Texture)");
            _api.GetImageMemoryRequirements(
                _device,
                createdImage,
                out MemoryRequirements requirements);

            var handleProperties = new MemoryWin32HandlePropertiesKHR
            {
                SType = StructureType.MemoryWin32HandlePropertiesKhr
            };
            var getHandleProperties =
                (delegate* unmanaged<
                    Device,
                    ExternalMemoryHandleTypeFlags,
                    nint,
                    MemoryWin32HandlePropertiesKHR*,
                    Result>)(void*)_getMemoryWin32HandleProperties;
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                getHandleProperties(
                    _device,
                    handleType,
                    sharedTexture.Handle.DangerousGetHandle(),
                    &handleProperties),
                "vkGetMemoryWin32HandlePropertiesKHR");

            var dedicatedInfo = new MemoryDedicatedAllocateInfo
            {
                SType = StructureType.MemoryDedicatedAllocateInfo,
                Image = createdImage
            };
            var importInfo = new ImportMemoryWin32HandleInfoKHR
            {
                SType = StructureType.ImportMemoryWin32HandleInfoKhr,
                PNext = &dedicatedInfo,
                HandleType = handleType,
                Handle = sharedTexture.Handle.DangerousGetHandle()
            };
            uint memoryTypeBits =
                requirements.MemoryTypeBits & handleProperties.MemoryTypeBits;
            var allocationInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                PNext = &importInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = FindMemoryType(
                    memoryTypeBits,
                    MemoryPropertyFlags.DeviceLocalBit)
            };
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                _api.AllocateMemory(
                    _device,
                    &allocationInfo,
                    null,
                    &allocatedMemory),
                "vkAllocateMemory(D3D11Texture)");
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                _api.BindImageMemory(
                    _device,
                    createdImage,
                    allocatedMemory,
                    0),
                "vkBindImageMemory(D3D11Texture)");
            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = createdImage,
                ViewType = ImageViewType.Type2D,
                Format = Format.R8G8B8A8Unorm,
                SubresourceRange = new ImageSubresourceRange(
                    ImageAspectFlags.ColorBit,
                    0,
                    1,
                    0,
                    1)
            };
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                _api.CreateImageView(
                    _device,
                    &viewInfo,
                    null,
                    &createdImageView),
                "vkCreateImageView(D3D11Texture)");
            image = createdImage;
            memory = allocatedMemory;
            imageView = createdImageView;
            success = true;
            return sharedTexture;
        }
        finally
        {
            if (!success)
            {
                if (createdImageView.Handle != 0)
                {
                    _api.DestroyImageView(_device, createdImageView, null);
                }
                if (createdImage.Handle != 0)
                {
                    _api.DestroyImage(_device, createdImage, null);
                }
                if (allocatedMemory.Handle != 0)
                {
                    _api.FreeMemory(_device, allocatedMemory, null);
                }
                sharedTexture.Dispose();
            }
        }
    }

    internal void CreateD3D11CopyCommands(
        Image source,
        Image destination,
        ViewportDimensions size,
        bool sourceNeedsTransferTransition,
        out CommandPool commandPool,
        out CommandBuffer firstCommands,
        out CommandBuffer reusedCommands)
    {
        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = _queueFamily
        };
        commandPool = default;
        firstCommands = default;
        reusedCommands = default;
        CommandPool createdPool = default;
        bool success = false;
        try
        {
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                _api.CreateCommandPool(_device, &poolInfo, null, &createdPool),
                "vkCreateCommandPool(D3D11Texture)");
            CommandBuffer* commands = stackalloc CommandBuffer[2];
            var allocationInfo = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = createdPool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 2
            };
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                _api.AllocateCommandBuffers(
                    _device,
                    &allocationInfo,
                    commands),
                "vkAllocateCommandBuffers(D3D11Texture)");
            RecordD3D11CopyCommands(
                commands[0],
                source,
                destination,
                size,
                sourceNeedsTransferTransition,
                firstUse: true);
            RecordD3D11CopyCommands(
                commands[1],
                source,
                destination,
                size,
                sourceNeedsTransferTransition,
                firstUse: false);
            commandPool = createdPool;
            firstCommands = commands[0];
            reusedCommands = commands[1];
            success = true;
        }
        finally
        {
            if (!success && createdPool.Handle != 0)
            {
                _api.DestroyCommandPool(_device, createdPool, null);
            }
        }
    }

    internal Semaphore CreateExportableSemaphore()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var exportInfo = new ExportSemaphoreCreateInfo
        {
            SType = StructureType.ExportSemaphoreCreateInfo,
            HandleTypes = GetSemaphoreHandleType()
        };
        var createInfo = new SemaphoreCreateInfo
        {
            SType = StructureType.SemaphoreCreateInfo,
            PNext = &exportInfo
        };
        Semaphore semaphore = default;
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.CreateSemaphore(_device, &createInfo, null, &semaphore),
            "vkCreateSemaphore");
        return semaphore;
    }

    internal void CreateFrameCommands(
        Image image,
        int frameIndex,
        out CommandPool commandPool,
        out CommandBuffer firstCommands,
        out CommandBuffer reusedCommands)
    {
        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = _queueFamily
        };
        commandPool = default;
        firstCommands = default;
        reusedCommands = default;
        CommandPool createdCommandPool = default;
        bool success = false;
        try
        {
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                _api.CreateCommandPool(_device, &poolInfo, null, &createdCommandPool),
                "vkCreateCommandPool");
            CommandBuffer* buffers = stackalloc CommandBuffer[2];
            var allocationInfo = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = createdCommandPool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 2
            };
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                _api.AllocateCommandBuffers(_device, &allocationInfo, buffers),
                "vkAllocateCommandBuffers");
            firstCommands = buffers[0];
            reusedCommands = buffers[1];
            RecordFrameCommands(firstCommands, image, frameIndex, firstUse: true);
            RecordFrameCommands(reusedCommands, image, frameIndex, firstUse: false);
            commandPool = createdCommandPool;
            success = true;
        }
        finally
        {
            if (!success && createdCommandPool.Handle != 0)
            {
                _api.DestroyCommandPool(_device, createdCommandPool, null);
            }
        }
    }

    internal SafeHandle ExportMemoryHandle(DeviceMemory memory)
    {
        if (OperatingSystem.IsWindows())
        {
            var info = new MemoryGetWin32HandleInfoKHR
            {
                SType = StructureType.MemoryGetWin32HandleInfoKhr,
                Memory = memory,
                HandleType = ExternalMemoryHandleTypeFlags.OpaqueWin32Bit
            };
            nint handle = 0;
            var function =
                (delegate* unmanaged<Device, MemoryGetWin32HandleInfoKHR*, nint*, Result>)
                (void*)_getMemoryHandle;
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                function(_device, &info, &handle),
                "vkGetMemoryWin32HandleKHR");
            return ValidateExportedHandle(
                new VulkanWin32SafeHandle(handle),
                "vkGetMemoryWin32HandleKHR");
        }
        else
        {
            var info = new MemoryGetFdInfoKHR
            {
                SType = StructureType.MemoryGetFDInfoKhr,
                Memory = memory,
                HandleType = ExternalMemoryHandleTypeFlags.OpaqueFDBit
            };
            int fileDescriptor = -1;
            var function =
                (delegate* unmanaged<Device, MemoryGetFdInfoKHR*, int*, Result>)
                (void*)_getMemoryHandle;
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                function(_device, &info, &fileDescriptor),
                "vkGetMemoryFdKHR");
            return ValidateExportedHandle(
                new VulkanPosixSafeHandle(fileDescriptor),
                "vkGetMemoryFdKHR");
        }
    }

    internal SafeHandle ExportSemaphoreHandle(Semaphore semaphore)
    {
        if (OperatingSystem.IsWindows())
        {
            var info = new SemaphoreGetWin32HandleInfoKHR
            {
                SType = StructureType.SemaphoreGetWin32HandleInfoKhr,
                Semaphore = semaphore,
                HandleType = ExternalSemaphoreHandleTypeFlags.OpaqueWin32Bit
            };
            nint handle = 0;
            var function =
                (delegate* unmanaged<Device, SemaphoreGetWin32HandleInfoKHR*, nint*, Result>)
                (void*)_getSemaphoreHandle;
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                function(_device, &info, &handle),
                "vkGetSemaphoreWin32HandleKHR");
            return ValidateExportedHandle(
                new VulkanWin32SafeHandle(handle),
                "vkGetSemaphoreWin32HandleKHR");
        }
        else
        {
            var info = new SemaphoreGetFdInfoKHR
            {
                SType = StructureType.SemaphoreGetFDInfoKhr,
                Semaphore = semaphore,
                HandleType = ExternalSemaphoreHandleTypeFlags.OpaqueFDBit
            };
            int fileDescriptor = -1;
            var function =
                (delegate* unmanaged<Device, SemaphoreGetFdInfoKHR*, int*, Result>)
                (void*)_getSemaphoreHandle;
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                function(_device, &info, &fileDescriptor),
                "vkGetSemaphoreFdKHR");
            return ValidateExportedHandle(
                new VulkanPosixSafeHandle(fileDescriptor),
                "vkGetSemaphoreFdKHR");
        }
    }

    internal static ICompositionExternalHandleLease LeaseHandle(
        SafeHandle canonicalHandle,
        string handleType)
    {
        SafeHandle duplicated = DuplicateHandle(canonicalHandle);
        CompositionExternalHandleOwnership ownership = OperatingSystem.IsWindows()
            ? CompositionExternalHandleOwnership.BorrowedUntilImportCompleted
            : CompositionExternalHandleOwnership.TransferOnSuccessfulImport;
        return new VulkanExternalHandleLease(duplicated, handleType, ownership);
    }

    internal Result SubmitFrame(
        CommandBuffer commands,
        Semaphore waitSemaphore,
        Semaphore signalSemaphore)
    {
        var waitStage = PipelineStageFlags.TransferBit;
        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commands,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = &signalSemaphore
        };
        if (waitSemaphore.Handle != 0)
        {
            submit.WaitSemaphoreCount = 1;
            submit.PWaitSemaphores = &waitSemaphore;
            submit.PWaitDstStageMask = &waitStage;
        }
        return _api.QueueSubmit(_queue, 1, &submit, default);
    }

    internal Result SignalRenderReady(Semaphore signalSemaphore)
    {
        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = &signalSemaphore
        };
        return _api.QueueSubmit(_queue, 1, &submit, default);
    }

    internal Result SubmitD3D11Frame(
        CommandBuffer commands,
        DeviceMemory importedMemory)
    {
        ulong acquireKey = 0;
        ulong releaseKey = 1;
        uint acquireTimeout = uint.MaxValue;
        var keyedMutex = new Win32KeyedMutexAcquireReleaseInfoKHR
        {
            SType = StructureType.Win32KeyedMutexAcquireReleaseInfoKhr,
            AcquireCount = 1,
            PAcquireSyncs = &importedMemory,
            PAcquireKeys = &acquireKey,
            PAcquireTimeouts = &acquireTimeout,
            ReleaseCount = 1,
            PReleaseSyncs = &importedMemory,
            PReleaseKeys = &releaseKey
        };
        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            PNext = &keyedMutex,
            CommandBufferCount = 1,
            PCommandBuffers = &commands
        };
        Result result = _api.QueueSubmit(_queue, 1, &submit, default);
        if (result is Result.Success)
        {
            WaitIdle();
        }
        return result;
    }

    internal void WaitForCompositorRelease(Semaphore waitSemaphore)
    {
        var waitStage = PipelineStageFlags.AllCommandsBit;
        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &waitSemaphore,
            PWaitDstStageMask = &waitStage
        };
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.QueueSubmit(_queue, 1, &submit, default),
            "vkQueueSubmit");
    }

    internal void CompleteCompositorRoundTrip(
        Semaphore renderReady,
        Semaphore compositorRelease)
    {
        var waitStage = PipelineStageFlags.AllCommandsBit;
        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &renderReady,
            PWaitDstStageMask = &waitStage,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = &compositorRelease
        };
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.QueueSubmit(_queue, 1, &submit, default),
            "vkQueueSubmit");
        WaitIdle();
    }

    internal void ReadbackD3D11SharedTextureForTesting(
        nint handle,
        Span<byte> destination)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "D3D11 shared-texture readback is available only on Windows.");
        }
        VulkanD3D11Bridge bridge = _d3d11Bridge ??
            throw new InvalidOperationException("The D3D11 Vulkan bridge is not initialized.");
        bridge.ReadbackSharedTexture(handle, 1, 0, destination);
    }

    internal void WaitIdle()
    {
        if (_device.Handle == 0)
        {
            return;
        }
        Result result = _api.DeviceWaitIdle(_device);
        if (result is not Result.ErrorDeviceLost)
        {
            VulkanSilkGraphicsDevice.ThrowIfFailed(result, "vkDeviceWaitIdle");
        }
    }

    internal void DestroyFrameResources(
        CommandPool commandPool,
        Semaphore compositorRelease,
        Semaphore renderReady,
        ImageView imageView,
        Image image,
        DeviceMemory memory)
    {
        if (commandPool.Handle != 0)
        {
            _api.DestroyCommandPool(_device, commandPool, null);
        }
        if (compositorRelease.Handle != 0)
        {
            _api.DestroySemaphore(_device, compositorRelease, null);
        }
        if (renderReady.Handle != 0)
        {
            _api.DestroySemaphore(_device, renderReady, null);
        }
        if (imageView.Handle != 0)
        {
            _api.DestroyImageView(_device, imageView, null);
        }
        if (image.Handle != 0)
        {
            _api.DestroyImage(_device, image, null);
        }
        if (memory.Handle != 0)
        {
            _api.FreeMemory(_device, memory, null);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        WaitIdle();
        _graphicsDevice?.Dispose();
        _graphicsDevice = null;
        if (OperatingSystem.IsWindows())
        {
            _d3d11Bridge?.Dispose();
        }
        _d3d11Bridge = null;
        if (_device.Handle != 0)
        {
            _api.DestroyDevice(_device, null);
        }
        _api.DestroyInstance(_instance, null);
        _api.Dispose();
        _disposed = true;
    }

    private void RecordFrameCommands(
        CommandBuffer commands,
        Image image,
        int frameIndex,
        bool firstUse)
    {
        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.SimultaneousUseBit
        };
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.BeginCommandBuffer(commands, &beginInfo),
            "vkBeginCommandBuffer");
        var range = new ImageSubresourceRange(
            ImageAspectFlags.ColorBit,
            0,
            1,
            0,
            1);
        ImageMemoryBarrier acquire =
            VulkanCompositionBarriers.CreateAcquire(image, range, firstUse);
        _api.CmdPipelineBarrier(
            commands,
            firstUse ? PipelineStageFlags.TopOfPipeBit : PipelineStageFlags.AllCommandsBit,
            PipelineStageFlags.TransferBit,
            0,
            0,
            null,
            0,
            null,
            1,
            &acquire);
        float blue = 0.25f + (frameIndex * 0.125f);
        var color = new ClearColorValue
        {
            Float32_0 = 0.0625f,
            Float32_1 = 0.125f,
            Float32_2 = blue,
            Float32_3 = 1
        };
        _api.CmdClearColorImage(
            commands,
            image,
            ImageLayout.TransferDstOptimal,
            &color,
            1,
            &range);
        ImageMemoryBarrier release =
            VulkanCompositionBarriers.CreateRelease(image, range);
        _api.CmdPipelineBarrier(
            commands,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.BottomOfPipeBit,
            0,
            0,
            null,
            0,
            null,
            1,
            &release);
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.EndCommandBuffer(commands),
            "vkEndCommandBuffer");
    }

    private void RecordD3D11CopyCommands(
        CommandBuffer commands,
        Image source,
        Image destination,
        ViewportDimensions size,
        bool sourceNeedsTransferTransition,
        bool firstUse)
    {
        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.SimultaneousUseBit
        };
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.BeginCommandBuffer(commands, &beginInfo),
            "vkBeginCommandBuffer(D3D11Texture)");
        var range = new ImageSubresourceRange(
            ImageAspectFlags.ColorBit,
            0,
            1,
            0,
            1);
        if (sourceNeedsTransferTransition)
        {
            var sourceBarrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                SrcAccessMask = AccessFlags.TransferWriteBit,
                DstAccessMask = AccessFlags.TransferReadBit,
                OldLayout = ImageLayout.TransferDstOptimal,
                NewLayout = ImageLayout.TransferSrcOptimal,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = source,
                SubresourceRange = range
            };
            _api.CmdPipelineBarrier(
                commands,
                PipelineStageFlags.TransferBit,
                PipelineStageFlags.TransferBit,
                0,
                0,
                null,
                0,
                null,
                1,
                &sourceBarrier);
        }
        var acquire = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = 0,
            DstAccessMask = AccessFlags.TransferWriteBit,
            OldLayout = firstUse ? ImageLayout.Undefined : ImageLayout.General,
            NewLayout = ImageLayout.TransferDstOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyExternal,
            DstQueueFamilyIndex = _queueFamily,
            Image = destination,
            SubresourceRange = range
        };
        _api.CmdPipelineBarrier(
            commands,
            firstUse
                ? PipelineStageFlags.TopOfPipeBit
                : PipelineStageFlags.AllCommandsBit,
            PipelineStageFlags.TransferBit,
            0,
            0,
            null,
            0,
            null,
            1,
            &acquire);
        var copy = new ImageCopy
        {
            SrcSubresource = new ImageSubresourceLayers(
                ImageAspectFlags.ColorBit,
                0,
                0,
                1),
            DstSubresource = new ImageSubresourceLayers(
                ImageAspectFlags.ColorBit,
                0,
                0,
                1),
            Extent = new Extent3D(
                checked((uint)size.Width),
                checked((uint)size.Height),
                1)
        };
        _api.CmdCopyImage(
            commands,
            source,
            ImageLayout.TransferSrcOptimal,
            destination,
            ImageLayout.TransferDstOptimal,
            1,
            &copy);
        var release = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = 0,
            OldLayout = ImageLayout.TransferDstOptimal,
            NewLayout = ImageLayout.General,
            SrcQueueFamilyIndex = _queueFamily,
            DstQueueFamilyIndex = Vk.QueueFamilyExternal,
            Image = destination,
            SubresourceRange = range
        };
        _api.CmdPipelineBarrier(
            commands,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.BottomOfPipeBit,
            0,
            0,
            null,
            0,
            null,
            1,
            &release);
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.EndCommandBuffer(commands),
            "vkEndCommandBuffer(D3D11Texture)");
    }

    private static VulkanCompositionCapabilitySnapshot GetCapabilities(
        Vk api,
        PhysicalDevice physicalDevice)
    {
        api.GetPhysicalDeviceProperties(
            physicalDevice,
            out PhysicalDeviceProperties properties);
        uint apiVersion = properties.ApiVersion;
        string[] requiredExtensions = GetRequiredDeviceExtensions(useD3D11Bridge: false);
        string[] missingExtensions = GetMissingDeviceExtensions(
            api,
            physicalDevice,
            requiredExtensions);
        string[] d3d11MissingExtensions = OperatingSystem.IsWindows()
            ? GetMissingDeviceExtensions(
                api,
                physicalDevice,
                GetRequiredDeviceExtensions(useD3D11Bridge: true))
            : [];
        bool hasQueue = TryFindGraphicsComputeQueue(
            api,
            physicalDevice,
            out uint queueFamily);
        bool isVulkan11 = apiVersion >= Vk.Version11;
        bool deviceReady =
            isVulkan11 && missingExtensions.Length == 0 && hasQueue;
        bool imageExportable = deviceReady &&
            IsImageExportable(api, physicalDevice, GetMemoryHandleType());
        bool semaphoreExportable = deviceReady &&
            IsSemaphoreExportable(api, physicalDevice, GetSemaphoreHandleType());
        bool d3d11Ready = OperatingSystem.IsWindows() &&
            isVulkan11 &&
            d3d11MissingExtensions.Length == 0 &&
            hasQueue;
        bool d3d11DirectRenderSupported = d3d11Ready &&
            IsImageImportable(
                api,
                physicalDevice,
                GetD3D11MemoryHandleType(),
                GetExternalImageUsage());
        bool d3d11CopySupported = d3d11Ready &&
            IsImageImportable(
                api,
                physicalDevice,
                GetD3D11MemoryHandleType(),
                ImageUsageFlags.TransferDstBit |
                ImageUsageFlags.SampledBit);
        bool luidValid = false;
        byte[] luid = [];
        byte[] uuid = [];
        if (isVulkan11)
        {
            GetDeviceIdentifiers(
                api,
                physicalDevice,
                out luidValid,
                out luid,
                out uuid);
        }
        return new VulkanCompositionCapabilitySnapshot
        {
            ApiVersion = apiVersion,
            ImageHandleType = OperatingSystem.IsWindows()
                ? WindowsImageHandleType
                : LinuxImageHandleType,
            SemaphoreHandleType = OperatingSystem.IsWindows()
                ? WindowsSemaphoreHandleType
                : LinuxSemaphoreHandleType,
            MissingExtensions = missingExtensions,
            ImageExportable = imageExportable,
            SemaphoreExportable = semaphoreExportable,
            HasGraphicsComputeQueue = hasQueue,
            QueueFamilyIndex = queueFamily,
            DeviceLuidValid = luidValid,
            DeviceLuid = luid,
            DeviceUuid = uuid,
            D3D11ImageImportable =
                d3d11DirectRenderSupported || d3d11CopySupported,
            D3D11DirectRenderSupported = d3d11DirectRenderSupported,
            D3D11MissingExtensions = d3d11MissingExtensions
        };
    }

    private static bool IsImageImportable(
        Vk api,
        PhysicalDevice physicalDevice,
        ExternalMemoryHandleTypeFlags handleType,
        ImageUsageFlags usage)
    {
        var externalInfo = new PhysicalDeviceExternalImageFormatInfo
        {
            SType = StructureType.PhysicalDeviceExternalImageFormatInfo,
            HandleType = handleType
        };
        var formatInfo = new PhysicalDeviceImageFormatInfo2
        {
            SType = StructureType.PhysicalDeviceImageFormatInfo2,
            PNext = &externalInfo,
            Format = Format.R8G8B8A8Unorm,
            Type = ImageType.Type2D,
            Tiling = ImageTiling.Optimal,
            Usage = usage
        };
        var externalProperties = new ExternalImageFormatProperties
        {
            SType = StructureType.ExternalImageFormatProperties
        };
        var properties = new ImageFormatProperties2
        {
            SType = StructureType.ImageFormatProperties2,
            PNext = &externalProperties
        };
        Result result = api.GetPhysicalDeviceImageFormatProperties2(
            physicalDevice,
            &formatInfo,
            &properties);
        if (result is Result.ErrorFormatNotSupported)
        {
            return false;
        }
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            result,
            "vkGetPhysicalDeviceImageFormatProperties2");
        ExternalMemoryProperties external =
            externalProperties.ExternalMemoryProperties;
        return (external.ExternalMemoryFeatures &
                ExternalMemoryFeatureFlags.ImportableBit) != 0 &&
            (external.CompatibleHandleTypes & handleType) != 0;
    }

    private static bool IsImageExportable(
        Vk api,
        PhysicalDevice physicalDevice,
        ExternalMemoryHandleTypeFlags handleType)
    {
        var externalInfo = new PhysicalDeviceExternalImageFormatInfo
        {
            SType = StructureType.PhysicalDeviceExternalImageFormatInfo,
            HandleType = handleType
        };
        var formatInfo = new PhysicalDeviceImageFormatInfo2
        {
            SType = StructureType.PhysicalDeviceImageFormatInfo2,
            PNext = &externalInfo,
            Format = Format.R8G8B8A8Unorm,
            Type = ImageType.Type2D,
            Tiling = ImageTiling.Optimal,
            Usage = GetExternalImageUsage(),
            Flags = (ImageCreateFlags)0x00000008
        };
        var externalProperties = new ExternalImageFormatProperties
        {
            SType = StructureType.ExternalImageFormatProperties
        };
        var properties = new ImageFormatProperties2
        {
            SType = StructureType.ImageFormatProperties2,
            PNext = &externalProperties
        };
        Result result = api.GetPhysicalDeviceImageFormatProperties2(
            physicalDevice,
            &formatInfo,
            &properties);
        if (result is Result.ErrorFormatNotSupported)
        {
            return false;
        }
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            result,
            "vkGetPhysicalDeviceImageFormatProperties2");
        ExternalMemoryProperties external =
            externalProperties.ExternalMemoryProperties;
        return (external.ExternalMemoryFeatures &
                ExternalMemoryFeatureFlags.ExportableBit) != 0 &&
            (external.CompatibleHandleTypes & handleType) != 0;
    }

    private static bool IsSemaphoreExportable(
        Vk api,
        PhysicalDevice physicalDevice,
        ExternalSemaphoreHandleTypeFlags handleType)
    {
        var info = new PhysicalDeviceExternalSemaphoreInfo
        {
            SType = StructureType.PhysicalDeviceExternalSemaphoreInfo,
            HandleType = handleType
        };
        var properties = new ExternalSemaphoreProperties
        {
            SType = StructureType.ExternalSemaphoreProperties
        };
        api.GetPhysicalDeviceExternalSemaphoreProperties(
            physicalDevice,
            &info,
            &properties);
        return (properties.ExternalSemaphoreFeatures &
                ExternalSemaphoreFeatureFlags.ExportableBit) != 0 &&
            (properties.CompatibleHandleTypes & handleType) != 0;
    }

    private static void GetDeviceIdentifiers(
        Vk api,
        PhysicalDevice physicalDevice,
        out bool luidValid,
        out byte[] luid,
        out byte[] uuid)
    {
        var idProperties = new PhysicalDeviceIDProperties
        {
            SType = StructureType.PhysicalDeviceIDProperties
        };
        var properties = new PhysicalDeviceProperties2
        {
            SType = StructureType.PhysicalDeviceProperties2,
            PNext = &idProperties
        };
        api.GetPhysicalDeviceProperties2(physicalDevice, &properties);
        byte* luidPointer = idProperties.DeviceLuid;
        byte* uuidPointer = idProperties.DeviceUuid;
        luidValid = idProperties.DeviceLuidvalid.Value != 0;
        luid = luidValid
            ? CopyIdentifier(luidPointer, checked((int)Vk.LuidSize))
            : [];
        uuid = CopyNonZeroIdentifier(uuidPointer, checked((int)Vk.UuidSize));
    }

    private static byte[] CopyIdentifier(byte* source, int length) =>
        new ReadOnlySpan<byte>(source, length).ToArray();

    private static byte[] CopyNonZeroIdentifier(byte* source, int length)
    {
        bool hasValue = false;
        for (int index = 0; index < length; index++)
        {
            hasValue |= source[index] != 0;
        }
        return hasValue ? new ReadOnlySpan<byte>(source, length).ToArray() : [];
    }

    private static string[] GetMissingDeviceExtensions(
        Vk api,
        PhysicalDevice physicalDevice,
        string[] required)
    {
        var result = new List<string>();
        foreach (string extension in required)
        {
            if (!api.IsDeviceExtensionPresent(physicalDevice, extension, string.Empty))
            {
                result.Add(extension);
            }
        }
        return [.. result];
    }

    private static string[] GetRequiredDeviceExtensions(bool useD3D11Bridge) =>
        OperatingSystem.IsWindows() && useD3D11Bridge
            ?
            [
                ExternalMemoryWin32Extension,
                Win32KeyedMutexExtension
            ]
            : OperatingSystem.IsWindows()
            ?
            [
                ExternalMemoryWin32Extension,
                ExternalSemaphoreWin32Extension
            ]
            :
            [
                ExternalMemoryFdExtension,
                ExternalSemaphoreFdExtension
            ];

    private static ExternalMemoryHandleTypeFlags GetMemoryHandleType() =>
        OperatingSystem.IsWindows()
            ? ExternalMemoryHandleTypeFlags.OpaqueWin32Bit
            : ExternalMemoryHandleTypeFlags.OpaqueFDBit;

    private static ExternalMemoryHandleTypeFlags GetD3D11MemoryHandleType() =>
        ExternalMemoryHandleTypeFlags.D3D11TextureBit;

    private static ExternalSemaphoreHandleTypeFlags GetSemaphoreHandleType() =>
        OperatingSystem.IsWindows()
            ? ExternalSemaphoreHandleTypeFlags.OpaqueWin32Bit
            : ExternalSemaphoreHandleTypeFlags.OpaqueFDBit;

    private static ImageUsageFlags GetExternalImageUsage() =>
        ImageUsageFlags.ColorAttachmentBit |
        ImageUsageFlags.TransferDstBit |
        ImageUsageFlags.TransferSrcBit |
        ImageUsageFlags.SampledBit;

    private static string GetMemoryExportFunctionName() =>
        OperatingSystem.IsWindows()
            ? "vkGetMemoryWin32HandleKHR"
            : "vkGetMemoryFdKHR";

    private static string GetSemaphoreExportFunctionName() =>
        OperatingSystem.IsWindows()
            ? "vkGetSemaphoreWin32HandleKHR"
            : "vkGetSemaphoreFdKHR";

    private static nint GetRequiredDeviceFunction(Vk api, Device device, string name)
    {
        nint address = (nint)api.GetDeviceProcAddr(device, name).Handle;
        if (address == 0)
        {
            throw new PlatformNotSupportedException(
                $"The Vulkan loader did not expose {name}.");
        }
        return address;
    }

    private static PhysicalDevice[] GetPhysicalDevices(Vk api, Instance instance)
    {
        uint count = 0;
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            api.EnumeratePhysicalDevices(instance, &count, null),
            "vkEnumeratePhysicalDevices");
        if (count == 0)
        {
            throw new PlatformNotSupportedException("No Vulkan physical device is available.");
        }
        var devices = new PhysicalDevice[count];
        fixed (PhysicalDevice* pointer = devices)
        {
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                api.EnumeratePhysicalDevices(instance, &count, pointer),
                "vkEnumeratePhysicalDevices");
        }
        return devices;
    }

    private static bool TryFindGraphicsComputeQueue(
        Vk api,
        PhysicalDevice physicalDevice,
        out uint queueFamily)
    {
        uint count = 0;
        api.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &count, null);
        var properties = new QueueFamilyProperties[count];
        fixed (QueueFamilyProperties* pointer = properties)
        {
            api.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &count, pointer);
        }
        var queues = new QueueFlags[count];
        for (int index = 0; index < queues.Length; index++)
        {
            queues[index] = properties[index].QueueFlags;
        }
        return VulkanCompositionDeviceRules.TryFindGraphicsComputeQueue(
            queues,
            out queueFamily);
    }

    private uint FindMemoryType(uint typeBits, MemoryPropertyFlags desired)
    {
        for (uint index = 0; index < _memoryProperties.MemoryTypeCount; index++)
        {
            bool supported = (typeBits & (1u << checked((int)index))) != 0;
            bool matches =
                (_memoryProperties.MemoryTypes[checked((int)index)].PropertyFlags & desired) ==
                desired;
            if (supported && matches)
            {
                return index;
            }
        }
        throw new PlatformNotSupportedException(
            $"No Vulkan memory type supports {desired}.");
    }

    private static SafeHandle DuplicateHandle(SafeHandle source)
    {
        ObjectDisposedException.ThrowIf(source.IsInvalid || source.IsClosed, source);
        if (OperatingSystem.IsWindows())
        {
            nint process = new(-1);
            int result = VulkanNativeMethods.DuplicateHandle(
                process,
                source.DangerousGetHandle(),
                process,
                out nint duplicate,
                0,
                0,
                2);
            if (result == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
            return new VulkanWin32SafeHandle(duplicate);
        }

        int fileDescriptor = checked((int)source.DangerousGetHandle());
        int duplicated = VulkanNativeMethods.Dup(fileDescriptor);
        if (duplicated < 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        return new VulkanPosixSafeHandle(duplicated);
    }

    private static SafeHandle ValidateExportedHandle(
        SafeHandle handle,
        string operation)
    {
        if (!handle.IsInvalid)
        {
            return handle;
        }
        handle.Dispose();
        throw new InvalidOperationException($"{operation} returned an invalid native handle.");
    }

    private static string[] GetVulkanLibraryNames()
    {
        if (OperatingSystem.IsWindows())
        {
            return ["vulkan-1.dll"];
        }
        return ["libvulkan.so.1", "libvulkan.so"];
    }
}

internal sealed class VulkanExternalHandleLease : ICompositionExternalHandleLease
{
    private SafeHandle? _handle;
    private int _committed;

    internal VulkanExternalHandleLease(
        SafeHandle handle,
        string handleType,
        CompositionExternalHandleOwnership ownership)
    {
        _handle = handle;
        HandleType = handleType;
        Ownership = ownership;
    }

    public nint Handle => _handle?.DangerousGetHandle() ?? 0;

    public bool IsInvalid =>
        _handle is null || _handle.IsClosed || _handle.IsInvalid;

    public string HandleType { get; }

    public CompositionExternalHandleOwnership Ownership { get; }

    public void CommitTransfer()
    {
        if (Ownership == CompositionExternalHandleOwnership.TransferOnSuccessfulImport &&
            Interlocked.CompareExchange(ref _committed, 1, 0) == 0)
        {
            _handle?.SetHandleAsInvalid();
        }
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _handle, null)?.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class VulkanWin32SafeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal VulkanWin32SafeHandle(nint handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle() => VulkanNativeMethods.CloseHandle(handle) != 0;
}

internal sealed class VulkanPosixSafeHandle : SafeHandleMinusOneIsInvalid
{
    internal VulkanPosixSafeHandle(int fileDescriptor)
        : base(ownsHandle: true)
    {
        SetHandle(fileDescriptor);
    }

    protected override bool ReleaseHandle() =>
        VulkanNativeMethods.CloseFileDescriptor(checked((int)handle)) == 0;
}

internal static partial class VulkanNativeMethods
{
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial int DuplicateHandle(
        nint sourceProcess,
        nint sourceHandle,
        nint targetProcess,
        out nint targetHandle,
        uint desiredAccess,
        int inheritHandle,
        uint options);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial int CloseHandle(nint handle);

    [LibraryImport("libc", EntryPoint = "dup", SetLastError = true)]
    internal static partial int Dup(int fileDescriptor);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    internal static partial int CloseFileDescriptor(int fileDescriptor);
}
