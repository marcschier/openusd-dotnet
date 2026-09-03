// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace OpenUsd.Rendering.Silk.D3D12;

/// <summary>
/// Minimal Direct3D 12 device, queue, fence, and buffer implementation.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe partial class D3D12SilkGraphicsDevice
    : SilkGraphicsDeviceLifetimeBase,
      ISilkGraphicsDevice,
      ISilkVolumeTextureGraphicsDevice,
      ISilkDeviceLossGraphicsDevice
{
    private readonly global::Silk.NET.Direct3D12.D3D12 _api;
    private readonly DXGI _dxgi;
    private IDXGIFactory4* _factory;
    private IDXGIAdapter1* _adapter;
    private ID3D12Device* _device;
    private ID3D12CommandQueue* _queue;
    private ID3D12Fence* _fence;
    private readonly object _retainedResourcesGate = new();
    private readonly List<D3D12RetainedResources> _retainedResources = [];
    private ulong _fenceValue;
    private ID3D12DeviceTeardownHook? _teardownHookForTesting;
    private Func<Exception>? _fenceWaitFailureForTesting;
    private int _retainedRecordReleaseCount;
    private int _fenceReleaseCount;
    private int _queueReleaseCount;
    private int _deviceReleaseCount;
    private int _adapterReleaseCount;
    private int _factoryReleaseCount;
    private int _apiDisposeCount;
    private int _dxgiDisposeCount;
    private D3D12DescriptorIndexedTextureTables? _materialDescriptorTables;
    private long _deviceLossGeneration = 1;
    private int _nextOffscreenSubmitDeviceLossForTesting;
    private bool _apiDisposed;
    private bool _dxgiDisposed;

    /// <inheritdoc/>
    public ulong DeviceLossGeneration =>
        unchecked((ulong)Interlocked.Read(ref _deviceLossGeneration));

    /// <summary>
    /// Records that this device was observed to be removed, before the failure
    /// that observed it is reported.
    /// </summary>
    /// <remarks>
    /// Unconditional rather than latched: a consumer that reads the generation
    /// either side of a failed call is asking whether *that* call lost the
    /// device, so a second removal has to move it again. The picking latch stays
    /// as it is, because it guards a one-time invalidation rather than a
    /// per-failure signal.
    /// </remarks>
    internal void NotifyDeviceLost() =>
        Interlocked.Increment(ref _deviceLossGeneration);

    /// <summary>
    /// Arms the next offscreen queue submission to report a removed device.
    /// </summary>
    /// <remarks>
    /// The injection substitutes the queue's result, not the handling: the
    /// submission still runs the production path that observes the removal and
    /// raises the failure.
    /// </remarks>
    internal void InjectNextOffscreenSubmitDeviceLossForTesting() =>
        Interlocked.Exchange(ref _nextOffscreenSubmitDeviceLossForTesting, 1);

    internal bool TryConsumeOffscreenSubmitDeviceLossForTesting() =>
        Interlocked.Exchange(ref _nextOffscreenSubmitDeviceLossForTesting, 0) != 0;

    private D3D12SilkGraphicsDevice(
        global::Silk.NET.Direct3D12.D3D12 api,
        DXGI dxgi,
        IDXGIFactory4* factory,
        IDXGIAdapter1* adapter,
        ID3D12Device* device,
        ID3D12CommandQueue* queue,
        ID3D12Fence* fence,
        bool software)
    {
        _api = api;
        _dxgi = dxgi;
        _factory = factory;
        _adapter = adapter;
        _device = device;
        _queue = queue;
        _fence = fence;
        string? descriptorIndexedTextureTablesDiagnostic = null;
        if (SupportsD3D12DescriptorIndexedTextureTables(
            device,
            out descriptorIndexedTextureTablesDiagnostic))
        {
            _materialDescriptorTables =
                D3D12DescriptorIndexedTextureTables.TryCreate(
                    device,
                    out descriptorIndexedTextureTablesDiagnostic);
        }
        Capabilities = new SilkGraphicsCapabilities(
            software ? "D3D12 WARP" : "D3D12 Adapter",
            "Direct3D 12",
            SupportsCompute: true,
            IsSoftware: software)
        {
            SupportsDescriptorIndexedTextureTables =
                _materialDescriptorTables is not null,
            DescriptorIndexedTextureTablesDiagnostic =
                _materialDescriptorTables is null
                    ? descriptorIndexedTextureTablesDiagnostic
                    : null,
            // Direct3D 11 and 12 both require every feature-level 11_0+ device -- including
            // WARP -- to support up to 16x anisotropic filtering (D3D12_REQ_MAXANISOTROPY).
            MaxSamplerAnisotropy = 16f,
            // A pipeline state with zero render targets and OMSetRenderTargets with
            // a depth view alone are core Direct3D 12, so every device this backend
            // creates can record the depth-only shadow pass.
            SupportsRasterShadows = true
        };
    }

    /// <inheritdoc/>
    public SilkGraphicsBackend Backend => SilkGraphicsBackend.D3D12;

    /// <inheritdoc/>
    public SilkGraphicsCapabilities Capabilities { get; }

    internal D3D12DescriptorIndexedTextureTables? MaterialDescriptorTables =>
        _materialDescriptorTables;

    /// <summary>Creates a D3D12 device. WARP is deterministic and suitable for CI.</summary>
    public static D3D12SilkGraphicsDevice Create(bool useWarp = true)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Direct3D 12 is available only on Windows.");
        }

        var api = new global::Silk.NET.Direct3D12.D3D12(
            SilkNativeLibraryContext.Load("d3d12.dll"));
        var dxgi = new DXGI(SilkNativeLibraryContext.Load("dxgi.dll"));
        IDXGIFactory4* factory = null;
        IDXGIAdapter1* adapter = null;
        ID3D12Device* device = null;
        ID3D12CommandQueue* queue = null;
        ID3D12Fence* fence = null;
        try
        {
            Guid factoryId = IDXGIFactory4.Guid;
            SilkMarshal.ThrowHResult(dxgi.CreateDXGIFactory2(0, &factoryId, (void**)&factory));

            Guid adapterId = IDXGIAdapter1.Guid;
            if (useWarp)
            {
                SilkMarshal.ThrowHResult(factory->EnumWarpAdapter(&adapterId, (void**)&adapter));
            }
            else
            {
                SilkMarshal.ThrowHResult(factory->EnumAdapters1(0, &adapter));
            }

            Guid deviceId = ID3D12Device.Guid;
            SilkMarshal.ThrowHResult(api.CreateDevice(
                (IUnknown*)adapter,
                D3DFeatureLevel.Level110,
                &deviceId,
                (void**)&device));

            var queueDescription = new CommandQueueDesc
            {
                Type = CommandListType.Direct,
                Priority = 0,
                Flags = CommandQueueFlags.None,
                NodeMask = 0
            };
            Guid queueId = ID3D12CommandQueue.Guid;
            SilkMarshal.ThrowHResult(device->CreateCommandQueue(
                &queueDescription,
                &queueId,
                (void**)&queue));

            Guid fenceId = ID3D12Fence.Guid;
            SilkMarshal.ThrowHResult(device->CreateFence(
                0,
                FenceFlags.None,
                &fenceId,
                (void**)&fence));

            return new D3D12SilkGraphicsDevice(
                api,
                dxgi,
                factory,
                adapter,
                device,
                queue,
                fence,
                useWarp);
        }
        catch
        {
            if (fence != null)
            {
                _ = fence->Release();
            }
            if (queue != null)
            {
                _ = queue->Release();
            }
            if (device != null)
            {
                _ = device->Release();
            }
            if (adapter != null)
            {
                _ = adapter->Release();
            }
            if (factory != null)
            {
                _ = factory->Release();
            }
            api.Dispose();
            dxgi.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage)
    {
        ObjectDisposedException.ThrowIf(_device == null, this);
        ArgumentOutOfRangeException.ThrowIfZero(size);
        bool storage = usage.HasFlag(SilkBufferUsage.Storage);
        bool upload = usage.HasFlag(SilkBufferUsage.Upload);
        RegisterDependentObject();

        var heapProperties = new HeapProperties(storage && !upload ? HeapType.Default : HeapType.Upload);
        var description = new ResourceDesc(
            ResourceDimension.Buffer,
            0,
            size,
            1,
            1,
            1,
            Format.FormatUnknown,
            new SampleDesc(1, 0),
            TextureLayout.LayoutRowMajor,
            storage && !upload ? ResourceFlags.AllowUnorderedAccess : ResourceFlags.None);
        ResourceStates initialState = storage && !upload
            ? ResourceStates.UnorderedAccess
            : ResourceStates.GenericRead;
        ID3D12Resource* resource = null;
        bool success = false;
        try
        {
            Guid resourceId = ID3D12Resource.Guid;
            SilkMarshal.ThrowHResult(_device->CreateCommittedResource(
                &heapProperties,
                HeapFlags.None,
                &description,
                initialState,
                null,
                &resourceId,
                (void**)&resource));
            success = true;
            return new D3D12SilkGraphicsBuffer(
                this,
                resource,
                size,
                usage,
                initialState);
        }
        finally
        {
            if (!success)
            {
                Release(ref resource);
                ReleaseDependentObject();
            }
        }
    }

    /// <inheritdoc/>
    public void WaitIdle()
    {
        ObjectDisposedException.ThrowIf(_queue == null || _fence == null, this);
        ulong value = ++_fenceValue;
        SilkMarshal.ThrowHResult(_queue->Signal(_fence, value));
        WaitForFence(_fence, value);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!TryBeginDispose())
        {
            return;
        }

        Exception? waitFailure = null;
        int removalReason = 0;
        try
        {
            if (_teardownHookForTesting is null)
            {
                WaitIdle();
            }
            else
            {
                _teardownHookForTesting.WaitIdle();
            }
        }
        catch (Exception exception)
        {
            waitFailure = exception;
            removalReason = GetDeviceRemovedReasonForTeardown();
            if (!IsTerminalDeviceRemovalReason(removalReason))
            {
                CancelLifetimeDispose();
                throw;
            }
        }

        Exception? cleanupFailure = ReleaseAllNativeResources();
        CompleteLifetimeDispose();
        if (waitFailure is not null)
        {
            var removalException = new D3D12DeviceRemovalTeardownException(
                removalReason,
                waitFailure);
            if (cleanupFailure is not null)
            {
                throw new AggregateException(removalException, cleanupFailure);
            }
            throw removalException;
        }
        if (cleanupFailure is not null)
        {
            ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        }
    }

    internal void RegisterDependentObject() => RegisterDependentLifetime();

    internal void ReleaseDependentObject() => ReleaseDependentLifetime();

    private bool TryBeginDispose() => TryBeginLifetimeDispose(
        "Cannot dispose the D3D12 device while buffers, textures, or submissions are alive; " +
        "samplers must also be disposed.");

    internal bool IsFenceCompleted(ID3D12Fence* fence, ulong value)
    {
        ulong completedValue = fence->GetCompletedValue();
        if (completedValue == ulong.MaxValue)
        {
            ThrowDeviceRemoved();
        }
        return completedValue >= value;
    }

    internal void WaitForFence(ID3D12Fence* fence, ulong value)
    {
        if (_fenceWaitFailureForTesting is { } injected)
        {
            throw injected();
        }
        while (!IsFenceCompleted(fence, value))
        {
            Thread.Yield();
        }
    }

    /// <summary>
    /// Makes every fence wait fail, the way a removed device makes them fail,
    /// until the failure is cleared with <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// The window between a submission being handed to the queue and its wait
    /// completing is where every resource that submission owns is still held:
    /// the fence, the command list, the allocator, the upload staging, the
    /// descriptor heaps and the device's own dependent registration. A real
    /// device removal is not reproducible on demand, and once it has happened
    /// every later wait fails too -- including the one disposal makes, which is
    /// the wait that must not replace the failure the caller is already
    /// unwinding. A one-shot failure would let that second wait succeed and hide
    /// exactly the defect this exists to find.
    /// </remarks>
    internal void FailFenceWaitsForTesting(Func<Exception>? failure) =>
        _fenceWaitFailureForTesting = failure;

    internal bool IsDeviceRemoved()
    {
        bool removed = _device != null && _device->GetDeviceRemovedReason() < 0;
        if (removed)
        {
            ObserveNativeDeviceRemoval();
        }
        return removed;
    }

    internal void SetTeardownHookForTesting(ID3D12DeviceTeardownHook? hook) =>
        _teardownHookForTesting = hook;

    internal void AddRetainedRecordForTeardownTesting()
    {
        lock (_retainedResourcesGate)
        {
            _retainedResources.Add(new D3D12RetainedResources([], 0, 0, 0));
        }
    }

    internal D3D12DeviceTeardownReleaseCounts TeardownReleaseCountsForTesting => new(
        _retainedRecordReleaseCount,
        _fenceReleaseCount,
        _queueReleaseCount,
        _deviceReleaseCount,
        _adapterReleaseCount,
        _factoryReleaseCount,
        _apiDisposeCount,
        _dxgiDisposeCount);

    internal bool NativeObjectsReleasedForTesting
    {
        get
        {
            lock (_retainedResourcesGate)
            {
                return _retainedResources.Count == 0 &&
                    _fence == null &&
                    _queue == null &&
                    _device == null &&
                    _adapter == null &&
                    _factory == null &&
                    _apiDisposed &&
                    _dxgiDisposed;
            }
        }
    }

    internal bool TryDrainSubmittedWork()
    {
        if (_queue == null || _fence == null)
        {
            return false;
        }

        ulong value = ++_fenceValue;
        int result = _queue->Signal(_fence, value);
        if (result < 0)
        {
            return IsDeviceRemoved();
        }

        while (true)
        {
            ulong completedValue = _fence->GetCompletedValue();
            if (completedValue == ulong.MaxValue)
            {
                ObserveNativeDeviceRemoval();
                return true;
            }
            if (completedValue >= value)
            {
                return true;
            }
            Thread.Yield();
        }
    }

    internal void RetainSubmittedReadback(
        ID3D12Resource* readback,
        ID3D12CommandAllocator* allocator,
        ID3D12GraphicsCommandList* commands,
        ID3D12Fence* fence)
    {
        lock (_retainedResourcesGate)
        {
            _retainedResources.Add(new D3D12RetainedResources(
                [(nint)readback],
                (nint)allocator,
                (nint)commands,
                (nint)fence));
        }
    }

    internal void RetainSubmittedPresentationCopy(
        ID3D12Resource* source,
        ID3D12Resource* destination,
        ID3D12CommandAllocator* allocator,
        ID3D12GraphicsCommandList* commands,
        ID3D12Fence* fence)
    {
        _ = source->AddRef();
        _ = destination->AddRef();
        try
        {
            lock (_retainedResourcesGate)
            {
                _retainedResources.Add(new D3D12RetainedResources(
                    [(nint)source, (nint)destination],
                    (nint)allocator,
                    (nint)commands,
                    (nint)fence));
            }
        }
        catch
        {
            _ = source->Release();
            _ = destination->Release();
            throw;
        }
    }

    private void ThrowDeviceRemoved()
    {
        ObserveNativeDeviceRemoval();
        int reason = _device->GetDeviceRemovedReason();
        SilkMarshal.ThrowHResult(reason);
        throw new InvalidOperationException(
            "The D3D12 fence reported device removal without a failing removal reason.");
    }

    private int GetDeviceRemovedReasonForTeardown()
    {
        try
        {
            if (_teardownHookForTesting is not null)
            {
                return _teardownHookForTesting.GetDeviceRemovedReason();
            }
            return _device == null ? 0 : _device->GetDeviceRemovedReason();
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsTerminalDeviceRemovalReason(int reason) =>
        reason is
            unchecked((int)0x887A0005) or
            unchecked((int)0x887A0006) or
            unchecked((int)0x887A0007);

    private Exception? ReleaseAllNativeResources()
    {
        List<Exception>? failures = null;
        CaptureCleanupFailure(ReleaseRetainedResources, ref failures);
        CaptureCleanupFailure(ReleaseDescriptorIndexedTextureTables, ref failures);
        CaptureCleanupFailure(ReleaseFence, ref failures);
        CaptureCleanupFailure(ReleaseQueue, ref failures);
        CaptureCleanupFailure(ReleaseDevice, ref failures);
        CaptureCleanupFailure(ReleaseAdapter, ref failures);
        CaptureCleanupFailure(ReleaseFactory, ref failures);
        CaptureCleanupFailure(DisposeApi, ref failures);
        CaptureCleanupFailure(DisposeDxgi, ref failures);
        return failures switch
        {
            null => null,
            [Exception failure] => failure,
            _ => new AggregateException(
                "One or more D3D12 teardown resources failed to release.",
                failures)
        };
    }

    private static void CaptureCleanupFailure(
        Action release,
        ref List<Exception>? failures)
    {
        try
        {
            release();
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }
    }

    private void ReleaseRetainedResources()
    {
        List<Exception>? failures = null;
        lock (_retainedResourcesGate)
        {
            foreach (D3D12RetainedResources resources in _retainedResources)
            {
                try
                {
                    resources.Release();
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);
                }
                finally
                {
                    _retainedRecordReleaseCount++;
                }
            }
            _retainedResources.Clear();
        }
        if (failures is [Exception failure])
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
        if (failures is not null)
        {
            throw new AggregateException(
                "One or more retained D3D12 resources failed to release.",
                failures);
        }
    }

    private void ReleaseFence()
    {
        if (_fence != null)
        {
            Release(ref _fence);
            _fenceReleaseCount++;
        }
    }

    private void ReleaseDescriptorIndexedTextureTables() =>
        Interlocked.Exchange(ref _materialDescriptorTables, null)?.Dispose();

    private static bool SupportsD3D12DescriptorIndexedTextureTables(
        ID3D12Device* device,
        out string? diagnostic)
    {
        diagnostic = null;
        var options = new FeatureDataD3D12Options();
        int result = device->CheckFeatureSupport(
            global::Silk.NET.Direct3D12.Feature.D3D12Options,
            &options,
            (uint)sizeof(FeatureDataD3D12Options));
        if (result < 0)
        {
            diagnostic =
                "D3D12 descriptor-indexed texture tables unavailable: " +
                $"CheckFeatureSupport(D3D12Options) failed with HRESULT 0x{result:X8}.";
            return false;
        }
        bool supported = options.ResourceBindingTier is
            ResourceBindingTier.Tier2 or ResourceBindingTier.Tier3;
        if (!supported)
        {
            diagnostic =
                "D3D12 descriptor-indexed texture tables unavailable: ResourceBindingTier is " +
                $"{options.ResourceBindingTier}; requires Tier2 or Tier3.";
        }
        return supported;
    }

    private void ReleaseQueue()
    {
        if (_queue != null)
        {
            Release(ref _queue);
            _queueReleaseCount++;
        }
    }

    private void ReleaseDevice()
    {
        if (_device != null)
        {
            Release(ref _device);
            _deviceReleaseCount++;
        }
    }

    private void ReleaseAdapter()
    {
        if (_adapter != null)
        {
            Release(ref _adapter);
            _adapterReleaseCount++;
        }
    }

    private void ReleaseFactory()
    {
        if (_factory != null)
        {
            Release(ref _factory);
            _factoryReleaseCount++;
        }
    }

    private void DisposeApi()
    {
        if (!_apiDisposed)
        {
            _api.Dispose();
            _apiDisposed = true;
            _apiDisposeCount++;
        }
    }

    private void DisposeDxgi()
    {
        if (!_dxgiDisposed)
        {
            _dxgi.Dispose();
            _dxgiDisposed = true;
            _dxgiDisposeCount++;
        }
    }

    internal static void Release<T>(ref T* value)
        where T : unmanaged
    {
        if (value != null)
        {
            _ = ((IUnknown*)value)->Release();
            value = null;
        }
    }
}

[SupportedOSPlatform("windows")]
internal interface ID3D12DeviceTeardownHook
{
    void WaitIdle();

    int GetDeviceRemovedReason();
}

internal readonly record struct D3D12DeviceTeardownReleaseCounts(
    int RetainedRecords,
    int Fence,
    int Queue,
    int Device,
    int Adapter,
    int Factory,
    int Api,
    int Dxgi);

internal sealed class D3D12DeviceRemovalTeardownException : InvalidOperationException
{
    internal D3D12DeviceRemovalTeardownException(int removalReason, Exception waitFailure)
        : base(
            $"D3D12 device teardown confirmed terminal removal reason " +
            $"0x{removalReason:X8}; native resources were released.",
            waitFailure)
    {
        RemovalReason = removalReason;
        HResult = removalReason;
    }

    internal int RemovalReason { get; }
}

[SupportedOSPlatform("windows")]
internal sealed unsafe class D3D12RetainedResources(
    nint[] resources,
    nint allocator,
    nint commands,
    nint fence)
{
    private nint[]? _resources = resources;
    private nint _allocator = allocator;
    private nint _commands = commands;
    private nint _fence = fence;

    internal void Release()
    {
        Release(ref _fence);
        Release(ref _commands);
        Release(ref _allocator);
        nint[]? resources = Interlocked.Exchange(ref _resources, null);
        if (resources is null)
        {
            return;
        }
        foreach (nint resource in resources)
        {
            nint value = resource;
            Release(ref value);
        }
    }

    private static void Release(ref nint value)
    {
        if (value != 0)
        {
            _ = ((IUnknown*)value)->Release();
            value = 0;
        }
    }
}

[SupportedOSPlatform("windows")]
internal sealed unsafe class D3D12SilkGraphicsBuffer : SilkGraphicsBufferBase
{
    private readonly D3D12SilkGraphicsDevice _device;
    private ID3D12Resource* _resource;

    internal D3D12SilkGraphicsBuffer(
        D3D12SilkGraphicsDevice device,
        ID3D12Resource* resource,
        nuint size,
        SilkBufferUsage usage,
        ResourceStates state)
        : base(size, usage)
    {
        _device = device;
        _resource = resource;
        State = state;
    }

    public override void Write(ReadOnlySpan<byte> data, nuint offset = 0)
    {
        ThrowIfBufferDisposed();
        nuint length = ValidateWrite(data.Length, offset);
        if (length == 0)
        {
            return;
        }

        var readRange = new global::Silk.NET.Direct3D12.Range(0, 0);
        void* mapped = null;
        SilkMarshal.ThrowHResult(_resource->Map(0, &readRange, &mapped));
        try
        {
            fixed (byte* source = data)
            {
                System.Buffer.MemoryCopy(
                    source,
                    (byte*)mapped + checked((nint)offset),
                    checked((long)(Size - offset)),
                    data.Length);
            }
        }

        finally
        {
            var writtenRange = new global::Silk.NET.Direct3D12.Range(
                offset,
                offset + length);
            _resource->Unmap(0, &writtenRange);
        }
    }

    public override void ReadbackForTesting(Span<byte> destination)
    {
        _ = ValidateReadback(destination.Length);
        _device.Readback(this, destination);
    }

    protected override void ReleaseNative()
    {
        _ = _resource->Release();
        _resource = null;
        _device.ReleaseDependentObject();
    }

    internal ID3D12Resource* Resource => _resource;

    internal D3D12SilkGraphicsDevice Device => _device;

    internal ResourceStates State { get; set; }

    internal IDisposable AcquireLease() => AcquireBufferLease();

    internal void ThrowIfDisposed() => ThrowIfBufferDisposed();
}
