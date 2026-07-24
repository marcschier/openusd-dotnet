// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;
using D3D11Api = Silk.NET.Direct3D11.D3D11;
using D3D12Resource = Silk.NET.Direct3D12.ID3D12Resource;

namespace OpenUsd.Rendering.Silk.D3D12;

/// <summary>
/// Renders backend-neutral Silk color and depth targets for one composition frame.
/// </summary>
public interface ISilkPresentationRenderer
{
    /// <summary>Renders one frame and returns scene evidence for diagnostics.</summary>
    SilkPresentationRenderResult Render(
        ISilkGraphicsTexture colorTarget,
        ISilkGraphicsTexture depthTarget,
        CancellationToken cancellationToken);
}

/// <summary>Describes the retained scene rendered into one presentation frame.</summary>
public readonly record struct SilkPresentationRenderResult(
    ulong SceneRevision,
    int DrawCount,
    bool ContinueRendering = true);

/// <summary>Reports D3D12 composition lifecycle and frame evidence.</summary>
public readonly record struct D3D12CompositionPresenterStatistics(
    bool ProbeSucceeded,
    int ActiveGenerations,
    int ActiveFrames,
    long GenerationCount,
    long RenderedFrameCount,
    long SilkRenderedFrameCount,
    long KeyedMutexReuseCount,
    ulong LastSceneRevision,
    int LastDrawCount,
    int LastWidth,
    int LastHeight,
    int RetainedPresentationCopies,
    long LastAllocationId);

/// <summary>
/// Presents D3D12-rendered frames through Avalonia-compatible D3D11 NT shared textures.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class D3D12CompositionViewportPresenter : ICompositionViewportPresenter
{
    /// <summary>The Avalonia descriptor for an NT handle created by IDXGIResource1.</summary>
    public const string D3D11TextureNtHandle = "D3D11TextureNtHandle";

    private readonly object _gate = new();
    private readonly D3D12SilkGraphicsDevice _device;
    private readonly D3D11Api _d3d11;
    private readonly ISilkPresentationRenderer? _renderer;
    private readonly HashSet<D3D12CompositionPresentationGeneration> _generations = [];
    private ID3D11Device* _d3d11Device;
    private string? _selectedHandleType;
    private string? _deviceLossReason;
    private long _generationCount;
    private long _renderedFrameCount;
    private long _silkRenderedFrameCount;
    private long _keyedMutexReuseCount;
    private ulong _lastSceneRevision;
    private int _lastDrawCount;
    private int _lastWidth;
    private int _lastHeight;
    private int _activeFrameCount;
    private long _lastAllocationId;
    private bool _probeSucceeded;
    private bool _disposed;

    /// <summary>Initializes a presenter that renders with the supplied D3D12 device.</summary>
    public D3D12CompositionViewportPresenter(D3D12SilkGraphicsDevice device)
        : this(device, null)
    {
    }

    /// <summary>
    /// Initializes a presenter with an optional retained-scene renderer.
    /// </summary>
    public D3D12CompositionViewportPresenter(
        D3D12SilkGraphicsDevice device,
        ISilkPresentationRenderer? renderer)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _renderer = renderer;
        _device.RegisterDependentObject();
        try
        {
            _d3d11 = new D3D11Api(SilkNativeLibraryContext.Load("d3d11.dll"));
        }
        catch
        {
            _device.ReleaseDependentObject();
            throw;
        }
    }

    /// <summary>Gets the renderer adapter LUID used for same-adapter composition checks.</summary>
    public IReadOnlyList<byte> RendererAdapterLuid =>
        Array.AsReadOnly(_device.GetAdapterLuid());

    /// <summary>Gets the most recent D3D12 device-loss reason reported while rendering.</summary>
    public string? DeviceLossReason
    {
        get
        {
            lock (_gate)
            {
                return _deviceLossReason;
            }
        }
    }

    /// <summary>Captures presenter lifecycle and rendered-scene evidence.</summary>
    public D3D12CompositionPresenterStatistics GetStatistics()
    {
        lock (_gate)
        {
            return new D3D12CompositionPresenterStatistics(
                _probeSucceeded,
                _generations.Count,
                _activeFrameCount,
                _generationCount,
                _renderedFrameCount,
                _silkRenderedFrameCount,
                _keyedMutexReuseCount,
                _lastSceneRevision,
                _lastDrawCount,
                _lastWidth,
                _lastHeight,
                _device.RetainedResourceCountForTesting,
                _lastAllocationId);
        }
    }

    /// <inheritdoc/>
    public ValueTask<CompositionPresenterProbeResult> ProbeAsync(
        CompositionPresentationTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!target.ImageHandleTypes.Contains(
                D3D11TextureNtHandle,
                StringComparer.Ordinal))
            {
                _probeSucceeded = false;
                _selectedHandleType = null;
                return ValueTask.FromResult(CompositionPresenterProbeResult.Unavailable(
                    "D3D12 composition unavailable: Avalonia does not advertise " +
                    $"{D3D11TextureNtHandle}."));
            }

            byte[] adapterLuid = _device.GetAdapterLuid();
            if (target.DeviceLuid.Count != adapterLuid.Length)
            {
                _probeSucceeded = false;
                _selectedHandleType = null;
                return ValueTask.FromResult(CompositionPresenterProbeResult.Unavailable(
                    "D3D12 composition unavailable: Avalonia did not expose an 8-byte " +
                    "adapter LUID, so same-adapter D3D11 sharing cannot be verified."));
            }
            if (!Matches(target.DeviceLuid, adapterLuid))
            {
                _probeSucceeded = false;
                _selectedHandleType = null;
                return ValueTask.FromResult(CompositionPresenterProbeResult.Unavailable(
                    "D3D12 composition unavailable: compositor adapter LUID " +
                    $"{Convert.ToHexString([.. target.DeviceLuid])} does not match renderer " +
                    $"{Convert.ToHexString(adapterLuid)}."));
            }

            try
            {
                EnsureD3D11Device();
            }
            catch (Exception exception) when (
                exception is COMException or InvalidOperationException)
            {
                _probeSucceeded = false;
                _selectedHandleType = null;
                return ValueTask.FromResult(CompositionPresenterProbeResult.Unavailable(
                    $"D3D12 composition unavailable: the same-adapter D3D11 bridge " +
                    $"could not be created ({exception.Message})."));
            }

            _selectedHandleType = D3D11TextureNtHandle;
            _probeSucceeded = true;
            return ValueTask.FromResult(CompositionPresenterProbeResult.Available(
                "D3D12 composition available through a same-adapter D3D11 NT shared " +
                "texture and keyed mutex."));
        }
    }

    /// <inheritdoc/>
    public ValueTask<ICompositionPresentationGeneration> CreateGenerationAsync(
        ViewportDimensions size,
        int frameCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (size.Width == 0 || size.Height == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }
        if (frameCount is < 2 or > 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameCount),
                "A composition presentation ring must contain two or three frames.");
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_selectedHandleType is null || _d3d11Device == null)
            {
                throw new InvalidOperationException(
                    "ProbeAsync must report a compatible composition target before " +
                    "a D3D12 presentation generation is created.");
            }

            var generation = new D3D12CompositionPresentationGeneration(
                this,
                _device,
                _d3d11Device,
                size,
                frameCount,
                _selectedHandleType);
            _generations.Add(generation);
            _generationCount++;
            return ValueTask.FromResult<ICompositionPresentationGeneration>(generation);
        }
    }

    /// <inheritdoc/>
    public ValueTask<CompositionFrameRenderResult> RenderAsync(
        ICompositionPresentationFrame frame,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (frame is not D3D12CompositionPresentationFrame d3d12Frame ||
            !ReferenceEquals(d3d12Frame.Presenter, this))
        {
            throw new ArgumentException(
                "The frame was not created by this D3D12 presenter.",
                nameof(frame));
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        try
        {
            D3D12CompositionFrameRender render =
                d3d12Frame.Render(_device, _renderer, cancellationToken);
            RecordRenderedFrame(render);
            return ValueTask.FromResult(new CompositionFrameRenderResult(
                CompositionFrameRenderStatus.Presented,
                ContinueRendering: render.SilkResult?.ContinueRendering ?? true,
                render.Synchronization));
        }
        catch (Exception exception) when (_device.IsPresentationDeviceLoss(exception))
        {
            lock (_gate)
            {
                _deviceLossReason = exception.Message;
            }
            d3d12Frame.AbortProducerAccess();
            return ValueTask.FromResult(new CompositionFrameRenderResult(
                CompositionFrameRenderStatus.DeviceLost,
                ContinueRendering: false,
                CompositionFrameSynchronization.Automatic));
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        D3D12CompositionPresentationGeneration[] generations;
        lock (_gate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }
            _disposed = true;
            generations = [.. _generations];
            _generations.Clear();
        }

        foreach (D3D12CompositionPresentationGeneration generation in generations)
        {
            generation.DisposeFromPresenter();
        }
        D3D12SilkGraphicsDevice.Release(ref _d3d11Device);
        _d3d11.Dispose();
        _device.ReleaseDependentObject();
        return ValueTask.CompletedTask;
    }

    internal void RemoveGeneration(D3D12CompositionPresentationGeneration generation)
    {
        lock (_gate)
        {
            _generations.Remove(generation);
        }
    }

    internal void AddFrame()
    {
        lock (_gate)
        {
            _activeFrameCount++;
        }
    }

    internal void RemoveFrame()
    {
        lock (_gate)
        {
            _activeFrameCount--;
        }
    }

    private void RecordRenderedFrame(D3D12CompositionFrameRender render)
    {
        lock (_gate)
        {
            _renderedFrameCount++;
            if (render.SilkResult is SilkPresentationRenderResult result)
            {
                _silkRenderedFrameCount++;
                _lastSceneRevision = result.SceneRevision;
                _lastDrawCount = result.DrawCount;
            }
            _lastWidth = render.Size.Width;
            _lastHeight = render.Size.Height;
            _lastAllocationId = render.AllocationId;
            if (render.ReusedKeyedMutex)
            {
                _keyedMutexReuseCount++;
            }
        }
    }

    private void EnsureD3D11Device()
    {
        if (_d3d11Device != null)
        {
            return;
        }

        ID3D11Device* device = null;
        ID3D11DeviceContext* context = null;
        D3DFeatureLevel selectedFeatureLevel;
        SilkMarshal.ThrowHResult(_d3d11.CreateDevice(
            (IDXGIAdapter*)_device.Adapter,
            D3DDriverType.Unknown,
            0,
            (uint)CreateDeviceFlag.BgraSupport,
            null,
            0,
            D3D11Api.SdkVersion,
            &device,
            &selectedFeatureLevel,
            &context));
        _ = selectedFeatureLevel;
        D3D12SilkGraphicsDevice.Release(ref context);
        _d3d11Device = device;
    }

    private static bool Matches(IReadOnlyList<byte> left, ReadOnlySpan<byte> right)
    {
        if (left.Count != right.Length)
        {
            return false;
        }
        for (int index = 0; index < right.Length; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }
        return true;
    }
}

[SupportedOSPlatform("windows")]
internal sealed class D3D12CompositionPresentationGeneration
    : ICompositionPresentationGeneration
{
    private readonly D3D12CompositionViewportPresenter _presenter;
    private readonly D3D12CompositionPresentationFrame[] _frames;
    private int _disposed;

    internal unsafe D3D12CompositionPresentationGeneration(
        D3D12CompositionViewportPresenter presenter,
        D3D12SilkGraphicsDevice device,
        ID3D11Device* d3d11Device,
        ViewportDimensions size,
        int frameCount,
        string handleType)
    {
        _presenter = presenter;
        Size = size;
        var frames = new D3D12CompositionPresentationFrame[frameCount];
        int created = 0;
        try
        {
            for (; created < frames.Length; created++)
            {
                frames[created] = D3D12CompositionPresentationFrame.Create(
                    presenter,
                    device,
                    d3d11Device,
                    size,
                    handleType);
            }
            _frames = frames;
            Frames = Array.AsReadOnly<ICompositionPresentationFrame>(frames);
        }
        catch
        {
            for (int index = 0; index < created; index++)
            {
                frames[index].Dispose();
            }
            throw;
        }
    }

    public ViewportDimensions Size { get; }

    public IReadOnlyList<ICompositionPresentationFrame> Frames { get; }

    public ValueTask DisposeAsync()
    {
        DisposeCore(removeFromPresenter: true);
        return ValueTask.CompletedTask;
    }

    internal void DisposeFromPresenter() => DisposeCore(removeFromPresenter: false);

    private void DisposeCore(bool removeFromPresenter)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        foreach (D3D12CompositionPresentationFrame frame in _frames)
        {
            frame.Dispose();
        }
        if (removeFromPresenter)
        {
            _presenter.RemoveGeneration(this);
        }
    }
}

[SupportedOSPlatform("windows")]
internal sealed unsafe class D3D12CompositionPresentationFrame
    : ICompositionPresentationFrame
{
    private const uint SharedResourceReadWrite = 0x80000001;
    private const int WaitTimeout = 0x102;
    private static long _nextAllocationId;

    private readonly object _gate = new();
    private readonly D3D12SilkGraphicsDevice _device;
    private D3D12SilkGraphicsTexture? _renderTexture;
    private ID3D11Texture2D* _d3d11Texture;
    private IDXGIKeyedMutex* _keyedMutex;
    private D3D12Resource* _sharedD3D12Resource;
    private SafeCompositionHandle? _canonicalHandle;
    private ResourceStates _sharedState = ResourceStates.Common;
    private int _producerAcquireWaitCountForTesting;
    private ISilkGraphicsTexture? _depthTexture;
    private long _renderCount;
    private bool _producerOwnsMutex;
    private bool _disposed;

    private D3D12CompositionPresentationFrame(
        D3D12CompositionViewportPresenter presenter,
        D3D12SilkGraphicsDevice device,
        ViewportDimensions size,
        string handleType)
    {
        Presenter = presenter;
        _device = device;
        AllocationId = Interlocked.Increment(ref _nextAllocationId);
        Presenter.AddFrame();
        Image = new CompositionExternalImage(
            handleType,
            size,
            CompositionExternalImageFormat.R8G8B8A8UNorm);
        Semaphores = Array.Empty<CompositionExternalSemaphore>();
    }

    public long AllocationId { get; }

    public CompositionExternalImage Image { get; }

    public IReadOnlyList<CompositionExternalSemaphore> Semaphores { get; }

    internal D3D12CompositionViewportPresenter Presenter { get; }

    internal static D3D12CompositionPresentationFrame Create(
        D3D12CompositionViewportPresenter presenter,
        D3D12SilkGraphicsDevice device,
        ID3D11Device* d3d11Device,
        ViewportDimensions size,
        string handleType)
    {
        var frame = new D3D12CompositionPresentationFrame(
            presenter,
            device,
            size,
            handleType);
        try
        {
            frame.CreateResources(d3d11Device, size);
            return frame;
        }
        catch
        {
            frame.Dispose();
            throw;
        }
    }

    public ValueTask<ICompositionExternalHandleLease> LeaseImageHandleAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            SafeCompositionHandle canonical = _canonicalHandle ??
                throw new ObjectDisposedException(nameof(D3D12CompositionPresentationFrame));
            SafeCompositionHandle duplicate = WindowsHandleApi.Duplicate(canonical);
            return ValueTask.FromResult<ICompositionExternalHandleLease>(
                new D3D12CompositionHandleLease(duplicate, Image.HandleType));
        }
    }

    public ValueTask<ICompositionExternalHandleLease> LeaseSemaphoreHandleAsync(
        long resourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException(
            $"D3D11 keyed-mutex frame {AllocationId} has no external semaphore {resourceId}.");
    }

    internal D3D12CompositionFrameRender Render(
        D3D12SilkGraphicsDevice device,
        ISilkPresentationRenderer? renderer,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            AcquireProducerMutex(cancellationToken);
            try
            {
                SilkPresentationRenderResult? silkResult = null;
                if (renderer is null)
                {
                    using ISilkGraphicsCommandList commands = device.CreateCommandList();
                    commands.ClearColor(
                        _renderTexture!,
                        new SilkColor(0.125f, 0.25f, 0.75f, 1));
                    using ISilkGraphicsSubmission submission = device.Submit(commands);
                    submission.Wait();
                }
                else
                {
                    _depthTexture ??= device.CreateTexture2D(
                        SilkTextureDescriptor.SampledDepthTarget(
                            checked((uint)Image.Size.Width),
                            checked((uint)Image.Size.Height)));
                    silkResult = renderer.Render(
                        _renderTexture!,
                        _depthTexture!,
                        cancellationToken);
                }

                device.CopyToSharedPresentationResource(
                    _renderTexture!,
                    _sharedD3D12Resource,
                    ref _sharedState);
                SilkMarshal.ThrowHResult(_keyedMutex->ReleaseSync(1));
                _producerOwnsMutex = false;
                long renderCount = ++_renderCount;
                return new D3D12CompositionFrameRender(
                    CompositionFrameSynchronization.KeyedMutex(1, 0),
                    silkResult,
                    Image.Size,
                    ReusedKeyedMutex: renderCount > 1,
                    AllocationId);
            }
            catch
            {
                AbortProducerAccess();
                throw;
            }
        }
    }

    internal void ReadbackSourceForTesting(Span<byte> destination)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _renderTexture!.ReadbackForTesting(destination);
        }
    }

    internal bool CanOpenLeaseHandleForTesting(nint handle) =>
        _device.CanOpenSharedResourceForTesting(handle);

    internal int ProducerAcquireWaitCountForTesting =>
        Volatile.Read(ref _producerAcquireWaitCountForTesting);

    internal void ReadbackSharedDestinationForTesting(
        nint handle,
        uint acquireKey,
        uint releaseKey,
        Span<byte> destination) =>
        _device.ReadbackD3D11SharedTextureForTesting(
            handle,
            acquireKey,
            releaseKey,
            destination);

    internal void SimulateConsumerReleaseForTesting(uint acquireKey, uint releaseKey)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            int result = _keyedMutex->AcquireSync(acquireKey, uint.MaxValue);
            if (result != 0)
            {
                throw new InvalidOperationException(
                    $"The keyed mutex consumer acquire failed with 0x{result:X8}.");
            }
            SilkMarshal.ThrowHResult(_keyedMutex->ReleaseSync(releaseKey));
        }
    }

    internal void AbortProducerAccess()
    {
        lock (_gate)
        {
            if (!_producerOwnsMutex || _keyedMutex == null)
            {
                return;
            }
            _ = _keyedMutex->ReleaseSync(0);
            _producerOwnsMutex = false;
        }
    }

    internal void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            AbortProducerAccess();
            _disposed = true;
            _depthTexture?.Dispose();
            _depthTexture = null;
            _renderTexture?.Dispose();
            _renderTexture = null;
            D3D12SilkGraphicsDevice.Release(ref _sharedD3D12Resource);
            D3D12SilkGraphicsDevice.Release(ref _keyedMutex);
            D3D12SilkGraphicsDevice.Release(ref _d3d11Texture);
            _canonicalHandle?.Dispose();
            _canonicalHandle = null;
            Presenter.RemoveFrame();
        }
    }

    private void CreateResources(ID3D11Device* d3d11Device, ViewportDimensions size)
    {
        _renderTexture = (D3D12SilkGraphicsTexture)_device.CreateTexture2D(
            checked((uint)size.Width),
            checked((uint)size.Height),
            SilkTextureFormat.Rgba8Unorm);

        var description = new Texture2DDesc
        {
            Width = checked((uint)size.Width),
            Height = checked((uint)size.Height),
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.FormatR8G8B8A8Unorm,
            SampleDesc = new SampleDesc(1, 0),
            Usage = Usage.Default,
            BindFlags = (uint)(BindFlag.RenderTarget | BindFlag.ShaderResource),
            CPUAccessFlags = 0,
            MiscFlags = (uint)(ResourceMiscFlag.SharedKeyedmutex |
                ResourceMiscFlag.SharedNthandle)
        };
        ID3D11Texture2D* d3d11Texture = null;
        SilkMarshal.ThrowHResult(d3d11Device->CreateTexture2D(
            &description,
            null,
            &d3d11Texture));
        _d3d11Texture = d3d11Texture;

        IDXGIResource1* dxgiResource = null;
        try
        {
            Guid resourceId = IDXGIResource1.Guid;
            SilkMarshal.ThrowHResult(((IUnknown*)_d3d11Texture)->QueryInterface(
                &resourceId,
                (void**)&dxgiResource));
            void* handle = null;
            SilkMarshal.ThrowHResult(dxgiResource->CreateSharedHandle(
                null,
                SharedResourceReadWrite,
                (char*)null,
                &handle));
            _canonicalHandle = SafeCompositionHandle.FromOwned((nint)handle);
        }
        finally
        {
            D3D12SilkGraphicsDevice.Release(ref dxgiResource);
        }

        Guid keyedMutexId = IDXGIKeyedMutex.Guid;
        IDXGIKeyedMutex* keyedMutex = null;
        SilkMarshal.ThrowHResult(((IUnknown*)_d3d11Texture)->QueryInterface(
            &keyedMutexId,
            (void**)&keyedMutex));
        _keyedMutex = keyedMutex;

        Guid d3d12ResourceId = D3D12Resource.Guid;
        D3D12Resource* sharedD3D12Resource = null;
        SilkMarshal.ThrowHResult(_device.NativeDevice->OpenSharedHandle(
            (void*)_canonicalHandle.DangerousGetHandle(),
            &d3d12ResourceId,
            (void**)&sharedD3D12Resource));
        _sharedD3D12Resource = sharedD3D12Resource;
    }

    private void AcquireProducerMutex(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int result = _keyedMutex->AcquireSync(0, 16);
            if (result == 0)
            {
                _producerOwnsMutex = true;
                return;
            }
            if (result == WaitTimeout)
            {
                _ = Interlocked.Increment(ref _producerAcquireWaitCountForTesting);
                continue;
            }
            throw new InvalidOperationException(
                $"The D3D11 keyed mutex acquire for producer key 0 failed with " +
                $"0x{result:X8}.");
        }
    }
}

[SupportedOSPlatform("windows")]
internal readonly record struct D3D12CompositionFrameRender(
    CompositionFrameSynchronization Synchronization,
    SilkPresentationRenderResult? SilkResult,
    ViewportDimensions Size,
    bool ReusedKeyedMutex,
    long AllocationId);

[SupportedOSPlatform("windows")]
internal sealed class D3D12CompositionHandleLease(
    SafeCompositionHandle handle,
    string handleType)
    : ICompositionExternalHandleLease
{
    private SafeCompositionHandle? _handle = handle;

    public nint Handle => _handle?.DangerousGetHandle() ?? 0;

    public string HandleType { get; } = handleType;

    public CompositionExternalHandleValidityPolicy ValidityPolicy =>
        CompositionExternalHandleValidityPolicy.NonZero;

    public bool IsInvalid => _handle is null || _handle.IsInvalid || _handle.IsClosed;

    public CompositionExternalHandleOwnership Ownership =>
        CompositionExternalHandleOwnership.BorrowedUntilImportCompleted;

    public void CommitTransfer()
    {
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _handle, null)?.Dispose();
        return ValueTask.CompletedTask;
    }
}

[SupportedOSPlatform("windows")]
internal sealed class SafeCompositionHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeCompositionHandle()
        : base(ownsHandle: true)
    {
    }

    internal static SafeCompositionHandle FromOwned(nint handle)
    {
        if (handle == 0 || handle == -1)
        {
            throw new ArgumentOutOfRangeException(nameof(handle));
        }
        var result = new SafeCompositionHandle();
        result.SetHandle(handle);
        return result;
    }

    protected override bool ReleaseHandle() => WindowsHandleApi.Close(handle) != 0;
}

[SupportedOSPlatform("windows")]
internal static partial class WindowsHandleApi
{
    private const uint DuplicateSameAccess = 0x2;

    internal static SafeCompositionHandle Duplicate(SafeCompositionHandle source)
    {
        nint process = GetCurrentProcess();
        if (DuplicateHandle(
            process,
            source,
            process,
            out nint duplicate,
            0,
            inheritHandle: 0,
            DuplicateSameAccess) == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        return SafeCompositionHandle.FromOwned(duplicate);
    }

    internal static int Close(nint handle) => CloseHandle(handle);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int DuplicateHandle(
        nint sourceProcess,
        SafeCompositionHandle sourceHandle,
        nint targetProcess,
        out nint targetHandle,
        uint desiredAccess,
        int inheritHandle,
        uint options);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int CloseHandle(nint handle);
}

public sealed unsafe partial class D3D12SilkGraphicsDevice
{
    private int _presentationCopyFailureForTesting;

    internal IDXGIAdapter1* Adapter => _adapter;

    internal ID3D12Device* NativeDevice => _device;

    internal byte[] GetAdapterLuid()
    {
        ObjectDisposedException.ThrowIf(_adapter == null, this);
        AdapterDesc1 description;
        SilkMarshal.ThrowHResult(_adapter->GetDesc1(&description));
        byte[] result = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(result, description.AdapterLuid.Low);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), description.AdapterLuid.High);
        return result;
    }

    internal void CopyToSharedPresentationResource(
        D3D12SilkGraphicsTexture source,
        D3D12Resource* destination,
        ref ResourceStates destinationState)
    {
        ObjectDisposedException.ThrowIf(_device == null || _queue == null, this);
        source.ThrowIfDisposed();
        ID3D12CommandAllocator* allocator = null;
        ID3D12GraphicsCommandList* commands = null;
        ID3D12Fence* fence = null;
        bool commandExecuted = false;
        bool queueSignalSucceeded = false;
        bool copyCompleted = false;
        D3D12PresentationCopyFailure failureInjection = D3D12PresentationCopyFailure.None;
        try
        {
            Guid fenceId = ID3D12Fence.Guid;
            SilkMarshal.ThrowHResult(_device->CreateFence(
                0,
                FenceFlags.None,
                &fenceId,
                (void**)&fence));
            Guid allocatorId = ID3D12CommandAllocator.Guid;
            SilkMarshal.ThrowHResult(_device->CreateCommandAllocator(
                CommandListType.Direct,
                &allocatorId,
                (void**)&allocator));
            Guid commandListId = ID3D12GraphicsCommandList.Guid;
            SilkMarshal.ThrowHResult(_device->CreateCommandList(
                0,
                CommandListType.Direct,
                allocator,
                null,
                &commandListId,
                (void**)&commands));
            Transition(commands, source.Resource, source.State, ResourceStates.CopySource);
            Transition(commands, destination, destinationState, ResourceStates.CopyDest);
            commands->CopyResource(destination, source.Resource);
            Transition(commands, destination, ResourceStates.CopyDest, ResourceStates.Common);
            SilkMarshal.ThrowHResult(commands->Close());

            ID3D12CommandList* commandList = (ID3D12CommandList*)commands;
            _queue->ExecuteCommandLists(1, &commandList);
            commandExecuted = true;
            failureInjection = (D3D12PresentationCopyFailure)Interlocked.Exchange(
                ref _presentationCopyFailureForTesting,
                (int)D3D12PresentationCopyFailure.None);
            ThrowInjectedPresentationCopyFailure(failureInjection);

            int signalResult = _queue->Signal(fence, 1);
            if (signalResult < 0)
            {
                if (IsDeviceRemoved())
                {
                    ThrowDeviceRemoved();
                }
                SilkMarshal.ThrowHResult(signalResult);
            }
            queueSignalSucceeded = true;
            WaitForFence(fence, 1);
            copyCompleted = true;
            source.State = ResourceStates.CopySource;
            destinationState = ResourceStates.Common;
        }
        catch
        {
            if (commandExecuted && (!queueSignalSucceeded || !copyCompleted))
            {
                bool recovered = failureInjection !=
                    D3D12PresentationCopyFailure.DeferRecoveryForTesting &&
                    TryDrainSubmittedWork();
                if (!recovered)
                {
                    RetainSubmittedPresentationCopy(
                        source.Resource,
                        destination,
                        allocator,
                        commands,
                        fence);
                    allocator = null;
                    commands = null;
                    fence = null;
                }
            }
            throw;
        }
        finally
        {
            Release(ref fence);
            Release(ref commands);
            Release(ref allocator);
        }
    }

    internal void InjectPresentationCopyFailureForTesting(
        D3D12PresentationCopyFailure failure) =>
        Interlocked.Exchange(ref _presentationCopyFailureForTesting, (int)failure);

    internal int RetainedResourceCountForTesting
    {
        get
        {
            lock (_retainedResourcesGate)
            {
                return _retainedResources.Count;
            }
        }
    }

    internal bool IsPresentationDeviceLoss(Exception exception) =>
        exception is D3D12PresentationDeviceLostException || IsDeviceRemoved();

    private static void ThrowInjectedPresentationCopyFailure(
        D3D12PresentationCopyFailure failure)
    {
        switch (failure)
        {
            case D3D12PresentationCopyFailure.None:
                return;
            case D3D12PresentationCopyFailure.SignalFailure:
            case D3D12PresentationCopyFailure.DeferRecoveryForTesting:
                throw new D3D12PresentationSignalException();
            case D3D12PresentationCopyFailure.DeviceRemoved:
                throw new D3D12PresentationDeviceLostException();
            default:
                throw new ArgumentOutOfRangeException(nameof(failure));
        }
    }

    internal bool CanOpenSharedResourceForTesting(nint handle)
    {
        ObjectDisposedException.ThrowIf(_device == null, this);
        D3D12Resource* resource = null;
        Guid resourceId = D3D12Resource.Guid;
        int result = _device->OpenSharedHandle(
            (void*)handle,
            &resourceId,
            (void**)&resource);
        Release(ref resource);
        return result >= 0;
    }

    internal void ReadbackD3D11SharedTextureForTesting(
        nint handle,
        uint acquireKey,
        uint releaseKey,
        Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_adapter == null, this);
        using var d3d11 = new D3D11Api(SilkNativeLibraryContext.Load("d3d11.dll"));
        ID3D11Device* device = null;
        ID3D11Device1* device1 = null;
        ID3D11DeviceContext* context = null;
        ID3D11Texture2D* sharedTexture = null;
        ID3D11Texture2D* stagingTexture = null;
        IDXGIKeyedMutex* keyedMutex = null;
        bool mutexAcquired = false;
        bool mapped = false;
        try
        {
            D3DFeatureLevel selectedFeatureLevel;
            SilkMarshal.ThrowHResult(d3d11.CreateDevice(
                (IDXGIAdapter*)_adapter,
                D3DDriverType.Unknown,
                0,
                (uint)CreateDeviceFlag.BgraSupport,
                null,
                0,
                D3D11Api.SdkVersion,
                &device,
                &selectedFeatureLevel,
                &context));
            _ = selectedFeatureLevel;

            Guid device1Id = ID3D11Device1.Guid;
            SilkMarshal.ThrowHResult(((IUnknown*)device)->QueryInterface(
                &device1Id,
                (void**)&device1));
            Guid textureId = ID3D11Texture2D.Guid;
            SilkMarshal.ThrowHResult(device1->OpenSharedResource1(
                (void*)handle,
                &textureId,
                (void**)&sharedTexture));
            Guid keyedMutexId = IDXGIKeyedMutex.Guid;
            SilkMarshal.ThrowHResult(((IUnknown*)sharedTexture)->QueryInterface(
                &keyedMutexId,
                (void**)&keyedMutex));
            int acquireResult = keyedMutex->AcquireSync(acquireKey, uint.MaxValue);
            if (acquireResult != 0)
            {
                throw new InvalidOperationException(
                    $"The second D3D11 device could not acquire consumer key " +
                    $"{acquireKey}: 0x{acquireResult:X8}.");
            }
            mutexAcquired = true;

            Texture2DDesc description;
            sharedTexture->GetDesc(&description);
            int rowBytes = checked((int)description.Width * 4);
            int requiredLength = checked(rowBytes * (int)description.Height);
            if (destination.Length != requiredLength)
            {
                throw new ArgumentException(
                    $"The readback destination must contain exactly {requiredLength} bytes.",
                    nameof(destination));
            }
            description.Usage = Usage.Staging;
            description.BindFlags = 0;
            description.CPUAccessFlags = (uint)CpuAccessFlag.Read;
            description.MiscFlags = 0;
            SilkMarshal.ThrowHResult(device->CreateTexture2D(
                &description,
                null,
                &stagingTexture));
            context->CopyResource(
                (ID3D11Resource*)stagingTexture,
                (ID3D11Resource*)sharedTexture);

            MappedSubresource mapping;
            SilkMarshal.ThrowHResult(context->Map(
                (ID3D11Resource*)stagingTexture,
                0,
                Map.Read,
                0,
                &mapping));
            mapped = true;
            fixed (byte* destinationPointer = destination)
            {
                for (uint row = 0; row < description.Height; row++)
                {
                    byte* sourceRow = (byte*)mapping.PData + (row * mapping.RowPitch);
                    byte* destinationRow = destinationPointer + (row * rowBytes);
                    Buffer.MemoryCopy(sourceRow, destinationRow, rowBytes, rowBytes);
                }
            }
        }
        finally
        {
            if (mapped)
            {
                context->Unmap((ID3D11Resource*)stagingTexture, 0);
            }
            if (mutexAcquired)
            {
                _ = keyedMutex->ReleaseSync(releaseKey);
            }
            Release(ref keyedMutex);
            Release(ref stagingTexture);
            Release(ref sharedTexture);
            Release(ref device1);
            Release(ref context);
            Release(ref device);
        }
    }
}

internal enum D3D12PresentationCopyFailure
{
    None,
    SignalFailure,
    DeviceRemoved,
    DeferRecoveryForTesting
}

internal sealed class D3D12PresentationSignalException : InvalidOperationException
{
    internal D3D12PresentationSignalException()
        : base("Injected D3D12 presentation queue signal failure after command execution.")
    {
        HResult = unchecked((int)0x80004005);
    }
}

internal sealed class D3D12PresentationDeviceLostException : InvalidOperationException
{
    internal D3D12PresentationDeviceLostException()
        : base(
            "Injected D3D12 presentation device removal after command execution " +
            "(DXGI_ERROR_DEVICE_REMOVED).")
    {
        HResult = unchecked((int)0x887A0005);
    }
}
