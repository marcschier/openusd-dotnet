// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace OpenUsd.Rendering.Silk.D3D12;

[SupportedOSPlatform("windows")]
public sealed unsafe partial class D3D12SilkGraphicsDevice : ISilkPickingGraphicsDevice
{
    internal const uint PickReadbackRowPitch = 256;

    private long _pickDeviceGeneration = 1;
    private int _pickNativeDeviceRemovalObserved;
    private int _pickFailureForTesting;
    private int _holdPickCompletionsForTesting;
    private long _pickPipelineCreateCount;
    private long _pickReadbackCreateCount;
    private long _pickCommandAllocatorCreateCount;
    private long _pickCommandListCreateCount;
    private long _pickFenceCreateCount;
    private long _pickSubmissionCount;
    private long _pickCopyCount;
    private long _pickReadCount;
    private long _lastPickCoordinate;

    /// <inheritdoc/>
    public ulong PickDeviceGeneration =>
        checked((ulong)Interlocked.Read(ref _pickDeviceGeneration));

    /// <inheritdoc/>
    public ISilkPickGraphicsPipeline CreatePickGraphicsPipeline(
        SilkPickPipelineDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_device == null, this);
        descriptor.Validate();
        if (descriptor.VertexShader.Format != SilkShaderBinaryFormat.Dxil)
        {
            throw new ArgumentException(
                "D3D12 pick pipelines require checked DXIL shaders.",
                nameof(descriptor));
        }

        SilkPickPipelineDescriptor retainedDescriptor = descriptor with
        {
            VertexShader = descriptor.VertexShader with
            {
                Code = descriptor.VertexShader.Code.ToArray()
            },
            FragmentShader = descriptor.FragmentShader with
            {
                Code = descriptor.FragmentShader.Code.ToArray()
            }
        };
        ID3D12RootSignature* rootSignature = null;
        ID3D12PipelineState* pipeline = null;
        bool success = false;
        RegisterDependentObject();
        try
        {
            CreatePickRootSignature(out rootSignature);
            CreatePickPipelineState(
                retainedDescriptor,
                rootSignature,
                out pipeline);
            var result = new D3D12SilkPickGraphicsPipeline(
                this,
                retainedDescriptor,
                PickDeviceGeneration,
                rootSignature,
                pipeline);
            Interlocked.Increment(ref _pickPipelineCreateCount);
            success = true;
            return result;
        }
        finally
        {
            if (!success)
            {
                Release(ref pipeline);
                Release(ref rootSignature);
                ReleaseDependentObject();
            }
        }
    }

    /// <inheritdoc/>
    public ISilkPickReadbackBuffer CreatePickReadbackBuffer()
    {
        ObjectDisposedException.ThrowIf(_device == null, this);
        RegisterDependentObject();
        ID3D12Resource* resource = null;
        ID3D12CommandAllocator* allocator = null;
        ID3D12GraphicsCommandList* commands = null;
        ID3D12Fence* fence = null;
        void* mapped = null;
        bool success = false;
        try
        {
            var heapProperties = new HeapProperties(HeapType.Readback);
            var description = new ResourceDesc(
                ResourceDimension.Buffer,
                0,
                PickReadbackRowPitch,
                1,
                1,
                1,
                Format.FormatUnknown,
                new SampleDesc(1, 0),
                TextureLayout.LayoutRowMajor,
                ResourceFlags.None);
            Guid resourceId = ID3D12Resource.Guid;
            SilkMarshal.ThrowHResult(_device->CreateCommittedResource(
                &heapProperties,
                HeapFlags.None,
                &description,
                ResourceStates.CopyDest,
                null,
                &resourceId,
                (void**)&resource));

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
            SilkMarshal.ThrowHResult(commands->Close());
            Guid fenceId = ID3D12Fence.Guid;
            SilkMarshal.ThrowHResult(_device->CreateFence(
                0,
                FenceFlags.None,
                &fenceId,
                (void**)&fence));

            var readRange = new global::Silk.NET.Direct3D12.Range(
                0,
                SilkPickTokenEncoding.ByteSize);
            SilkMarshal.ThrowHResult(resource->Map(0, &readRange, &mapped));
            var result = new D3D12SilkPickReadbackBuffer(
                this,
                resource,
                (byte*)mapped,
                allocator,
                commands,
                fence,
                PickDeviceGeneration);
            Interlocked.Increment(ref _pickReadbackCreateCount);
            Interlocked.Increment(ref _pickCommandAllocatorCreateCount);
            Interlocked.Increment(ref _pickCommandListCreateCount);
            Interlocked.Increment(ref _pickFenceCreateCount);
            success = true;
            return result;
        }
        finally
        {
            if (!success)
            {
                if (mapped != null)
                {
                    var writtenRange = new global::Silk.NET.Direct3D12.Range(0, 0);
                    resource->Unmap(0, &writtenRange);
                }
                Release(ref fence);
                Release(ref commands);
                Release(ref allocator);
                Release(ref resource);
                ReleaseDependentObject();
            }
        }
    }

    internal D3D12PickNativeStatistics PickNativeStatisticsForTesting => new(
        Interlocked.Read(ref _pickPipelineCreateCount),
        Interlocked.Read(ref _pickReadbackCreateCount),
        Interlocked.Read(ref _pickCommandAllocatorCreateCount),
        Interlocked.Read(ref _pickCommandListCreateCount),
        Interlocked.Read(ref _pickFenceCreateCount),
        Interlocked.Read(ref _pickSubmissionCount),
        Interlocked.Read(ref _pickCopyCount),
        Interlocked.Read(ref _pickReadCount),
        UnpackPickCoordinate(Interlocked.Read(ref _lastPickCoordinate)),
        PickDeviceGeneration);

    internal bool ArePickCompletionsHeldForTesting =>
        Volatile.Read(ref _holdPickCompletionsForTesting) != 0;

    internal void SetPickCompletionsHeldForTesting(bool held) =>
        Volatile.Write(ref _holdPickCompletionsForTesting, held ? 1 : 0);

    internal void InjectPickFailureForTesting(D3D12PickFailure failure)
    {
        if (!Enum.IsDefined(failure))
        {
            throw new ArgumentOutOfRangeException(nameof(failure));
        }
        Interlocked.Exchange(ref _pickFailureForTesting, (int)failure);
    }

    internal void ThrowIfInjectedPickCopyFailure()
    {
        D3D12PickFailure failure = (D3D12PickFailure)Interlocked.Exchange(
            ref _pickFailureForTesting,
            (int)D3D12PickFailure.None);
        switch (failure)
        {
            case D3D12PickFailure.None:
                return;
            case D3D12PickFailure.CopyFailure:
                throw new InvalidOperationException(
                    "Injected D3D12 pick CopyTextureRegion failure.");
            case D3D12PickFailure.DeviceRemoved:
                ThrowInjectedPickDeviceLoss(unchecked((int)0x887A0005));
                break;
            case D3D12PickFailure.DeviceReset:
                ThrowInjectedPickDeviceLoss(unchecked((int)0x887A0007));
                break;
            default:
                throw new InvalidOperationException("Unknown D3D12 pick failure injection.");
        }
    }

    internal void RecordPickCopy(SilkTexturePixelCoordinate coordinate)
    {
        Interlocked.Increment(ref _pickCopyCount);
        Interlocked.Exchange(ref _lastPickCoordinate, PackPickCoordinate(coordinate));
    }

    internal void RecordPickRead() => Interlocked.Increment(ref _pickReadCount);

    internal void RecordPickSubmission() =>
        Interlocked.Increment(ref _pickSubmissionCount);

    internal void ObserveNativeDeviceRemoval()
    {
        if (Interlocked.Exchange(ref _pickNativeDeviceRemovalObserved, 1) == 0)
        {
            AdvancePickDeviceGeneration();
        }
    }

    private void ThrowInjectedPickDeviceLoss(int reason)
    {
        AdvancePickDeviceGeneration();
        throw new D3D12PickDeviceLostException(reason);
    }

    private void AdvancePickDeviceGeneration() =>
        Interlocked.Increment(ref _pickDeviceGeneration);

    private static long PackPickCoordinate(SilkTexturePixelCoordinate coordinate) =>
        unchecked((long)(((ulong)coordinate.X << 32) | coordinate.Y));

    private static SilkTexturePixelCoordinate UnpackPickCoordinate(long packed) =>
        new(
            unchecked((uint)((ulong)packed >> 32)),
            unchecked((uint)packed));

    private void CreatePickRootSignature(out ID3D12RootSignature* rootSignature)
    {
        RootParameter* parameters = stackalloc RootParameter[2];
        parameters[0] = new RootParameter(
            RootParameterType.TypeCbv,
            shaderVisibility: ShaderVisibility.Vertex,
            descriptor: new RootDescriptor(0, 0));
        parameters[1] = new RootParameter(
            RootParameterType.Type32BitConstants,
            shaderVisibility: ShaderVisibility.Pixel,
            constants: new RootConstants(1, 0, 4));
        var description = new RootSignatureDesc(
            2,
            parameters,
            flags: RootSignatureFlags.AllowInputAssemblerInputLayout);
        ID3D10Blob* serialized = null;
        ID3D10Blob* errors = null;
        ID3D12RootSignature* nativeRootSignature = null;
        try
        {
            SilkMarshal.ThrowHResult(_api.SerializeRootSignature(
                &description,
                D3DRootSignatureVersion.Version1,
                &serialized,
                &errors));
            Guid rootSignatureId = ID3D12RootSignature.Guid;
            SilkMarshal.ThrowHResult(_device->CreateRootSignature(
                0,
                serialized->GetBufferPointer(),
                serialized->GetBufferSize(),
                &rootSignatureId,
                (void**)&nativeRootSignature));
            rootSignature = nativeRootSignature;
        }
        finally
        {
            if (errors != null)
            {
                _ = errors->Release();
            }
            if (serialized != null)
            {
                _ = serialized->Release();
            }
        }
    }

    private void CreatePickPipelineState(
        SilkPickPipelineDescriptor descriptor,
        ID3D12RootSignature* rootSignature,
        out ID3D12PipelineState* pipeline)
    {
        byte[] vertexCode = descriptor.VertexShader.Code.ToArray();
        byte[] fragmentCode = descriptor.FragmentShader.Code.ToArray();
        byte[] position = "POSITION\0"u8.ToArray();
        byte[] normal = "NORMAL\0"u8.ToArray();
        ID3D12PipelineState* nativePipeline = null;
        fixed (byte* vertexPointer = vertexCode)
        fixed (byte* fragmentPointer = fragmentCode)
        fixed (byte* positionPointer = position)
        fixed (byte* normalPointer = normal)
        {
            InputElementDesc* elements = stackalloc InputElementDesc[2];
            elements[0] = new InputElementDesc(
                positionPointer,
                0,
                Format.FormatR32G32B32Float,
                0,
                0,
                InputClassification.PerVertexData,
                0);
            elements[1] = new InputElementDesc(
                normalPointer,
                0,
                Format.FormatR32G32B32Float,
                0,
                12,
                InputClassification.PerVertexData,
                0);
            var pipelineDescription = new GraphicsPipelineStateDesc
            {
                PRootSignature = rootSignature,
                VS = new ShaderBytecode(vertexPointer, (nuint)vertexCode.Length),
                PS = new ShaderBytecode(fragmentPointer, (nuint)fragmentCode.Length),
                BlendState = CreateBlendState(),
                SampleMask = uint.MaxValue,
                RasterizerState = new RasterizerDesc
                {
                    FillMode = FillMode.Solid,
                    CullMode = CullMode.None,
                    DepthClipEnable = true
                },
                DepthStencilState = new DepthStencilDesc
                {
                    DepthEnable = true,
                    DepthWriteMask = DepthWriteMask.All,
                    DepthFunc = ComparisonFunc.LessEqual,
                    StencilEnable = false
                },
                InputLayout = new InputLayoutDesc(elements, 2),
                PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
                NumRenderTargets = 1,
                DSVFormat = Format.FormatD32Float,
                SampleDesc = new SampleDesc(1, 0)
            };
            pipelineDescription.RTVFormats[0] = Format.FormatR8G8B8A8Unorm;
            Guid pipelineId = ID3D12PipelineState.Guid;
            SilkMarshal.ThrowHResult(_device->CreateGraphicsPipelineState(
                &pipelineDescription,
                &pipelineId,
                (void**)&nativePipeline));
        }
        pipeline = nativePipeline;
    }
}

[SupportedOSPlatform("windows")]
internal sealed unsafe class D3D12SilkPickGraphicsPipeline(
    D3D12SilkGraphicsDevice device,
    SilkPickPipelineDescriptor descriptor,
    ulong deviceGeneration,
    ID3D12RootSignature* rootSignature,
    ID3D12PipelineState* pipeline)
    : SilkGraphicsResourceBase, ISilkPickGraphicsPipeline
{
    private ID3D12RootSignature* _rootSignature = rootSignature;
    private ID3D12PipelineState* _pipeline = pipeline;

    internal D3D12SilkGraphicsDevice Device { get; } = device;

    internal ulong DeviceGeneration { get; } = deviceGeneration;

    internal ID3D12RootSignature* RootSignature => _rootSignature;

    internal ID3D12PipelineState* Pipeline => _pipeline;

    public SilkPickPipelineDescriptor Descriptor { get; } = descriptor;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfDisposed() => ThrowIfResourceDisposed();

    protected override void ReleaseNative()
    {
        D3D12SilkGraphicsDevice.Release(ref _pipeline);
        D3D12SilkGraphicsDevice.Release(ref _rootSignature);
        Device.ReleaseDependentObject();
    }
}

[SupportedOSPlatform("windows")]
internal sealed unsafe class D3D12SilkPickReadbackBuffer(
    D3D12SilkGraphicsDevice device,
    ID3D12Resource* resource,
    byte* mapped,
    ID3D12CommandAllocator* allocator,
    ID3D12GraphicsCommandList* commands,
    ID3D12Fence* fence,
    ulong deviceGeneration)
    : SilkGraphicsResourceBase, ISilkPickReadbackBuffer
{
    private readonly object _executionGate = new();
    private ID3D12Resource* _resource = resource;
    private byte* _mapped = mapped;
    private ID3D12CommandAllocator* _allocator = allocator;
    private ID3D12GraphicsCommandList* _commands = commands;
    private ID3D12Fence* _fence = fence;
    private ulong _nextFenceValue;
    private ulong _activeFenceValue;
    private bool _inUse;

    internal D3D12SilkGraphicsDevice Device { get; } = device;

    internal ulong DeviceGeneration { get; } = deviceGeneration;

    internal ID3D12Resource* Resource => _resource;

    internal uint NativeByteSize => checked((uint)_resource->GetDesc().Width);

    public int ByteSize => SilkPickTokenEncoding.ByteSize;

    public void ReadRgba8Pixel(Span<byte> destination)
    {
        ThrowIfResourceDisposed();
        if (destination.Length != ByteSize)
        {
            throw new ArgumentException(
                "A D3D12 pick readback requires exactly four RGBA8 bytes.",
                nameof(destination));
        }
        lock (_executionGate)
        {
            if (_inUse)
            {
                throw new InvalidOperationException(
                    "The D3D12 pick readback is still in flight.");
            }
            new ReadOnlySpan<byte>(_mapped, ByteSize).CopyTo(destination);
        }
        Device.RecordPickRead();
    }

    internal D3D12PickExecution BeginSubmission()
    {
        ThrowIfResourceDisposed();
        lock (_executionGate)
        {
            if (_inUse)
            {
                throw new InvalidOperationException(
                    "The persistent D3D12 pick slot is already in flight.");
            }
            SilkMarshal.ThrowHResult(_allocator->Reset());
            SilkMarshal.ThrowHResult(_commands->Reset(_allocator, null));
            _activeFenceValue = checked(++_nextFenceValue);
            _inUse = true;
            return new D3D12PickExecution(
                _allocator,
                _commands,
                _fence,
                _activeFenceValue);
        }
    }

    internal bool IsSubmissionCompleted(ulong fenceValue)
    {
        lock (_executionGate)
        {
            ValidateActiveSubmission(fenceValue);
            return Device.IsFenceCompleted(_fence, fenceValue);
        }
    }

    internal void WaitForSubmission(ulong fenceValue)
    {
        lock (_executionGate)
        {
            ValidateActiveSubmission(fenceValue);
        }
        Device.WaitForFence(_fence, fenceValue);
    }

    internal void CompleteSubmission(ulong fenceValue)
    {
        lock (_executionGate)
        {
            ValidateActiveSubmission(fenceValue);
            _activeFenceValue = 0;
            _inUse = false;
        }
    }

    internal void AbandonSubmission(ulong fenceValue)
    {
        lock (_executionGate)
        {
            ValidateActiveSubmission(fenceValue);
            _activeFenceValue = 0;
            _inUse = false;
        }
    }

    internal void CancelSubmission(ulong fenceValue)
    {
        lock (_executionGate)
        {
            if (!_inUse || _activeFenceValue != fenceValue)
            {
                return;
            }
            _ = _commands->Close();
            _activeFenceValue = 0;
            _inUse = false;
        }
    }

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfDisposed() => ThrowIfResourceDisposed();

    protected override void ReleaseNative()
    {
        D3D12SilkGraphicsDevice.Release(ref _fence);
        D3D12SilkGraphicsDevice.Release(ref _commands);
        D3D12SilkGraphicsDevice.Release(ref _allocator);
        if (_resource != null && _mapped != null)
        {
            var writtenRange = new global::Silk.NET.Direct3D12.Range(0, 0);
            _resource->Unmap(0, &writtenRange);
            _mapped = null;
        }
        D3D12SilkGraphicsDevice.Release(ref _resource);
        Device.ReleaseDependentObject();
    }

    private void ValidateActiveSubmission(ulong fenceValue)
    {
        if (!_inUse || _activeFenceValue != fenceValue)
        {
            throw new InvalidOperationException(
                "The D3D12 pick submission no longer owns this persistent slot.");
        }
    }
}

[SupportedOSPlatform("windows")]
internal sealed partial class D3D12SilkGraphicsCommandList : ISilkPickGraphicsCommandList
{
    private D3D12SilkPickGraphicsPipeline? _pickPipeline;
    private D3D12SilkPickReadbackBuffer? _pickReadbackDestination;
    private uint _pickBaseToken;

    internal D3D12SilkPickReadbackBuffer? PickReadbackDestination =>
        _pickReadbackDestination;

    public void SetPickGraphicsPipeline(ISilkPickGraphicsPipeline pipeline)
    {
        ThrowIfRendering();
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not D3D12SilkPickGraphicsPipeline d3d12Pipeline ||
            !ReferenceEquals(d3d12Pipeline.Device, Device))
        {
            throw new ArgumentException(
                "The pick pipeline was not created by this D3D12 device.",
                nameof(pipeline));
        }
        d3d12Pipeline.ThrowIfDisposed();
        if (d3d12Pipeline.DeviceGeneration != Device.PickDeviceGeneration)
        {
            throw new InvalidOperationException(
                "The D3D12 pick pipeline belongs to an invalid device generation.");
        }
        _pipeline = null;
        _pickPipeline = d3d12Pipeline;
        _pickBaseToken = 0;
        _commands.Add(D3D12GraphicsCommand.SetPickPipeline(d3d12Pipeline));
    }

    public void SetPickBaseToken(uint baseToken)
    {
        ThrowIfRendering();
        ArgumentOutOfRangeException.ThrowIfZero(baseToken);
        if (_pickPipeline is null)
        {
            throw new InvalidOperationException(
                "A D3D12 pick pipeline must be set before its base token.");
        }
        _pickBaseToken = baseToken;
        _commands.Add(D3D12GraphicsCommand.SetPickBaseToken(baseToken));
    }

    public void CopyRgba8Pixel(
        ISilkGraphicsTexture source,
        SilkTexturePixelCoordinate coordinate,
        ISilkPickReadbackBuffer destination)
    {
        ThrowIfOutsideRendering();
        coordinate.Validate(source);
        D3D12SilkGraphicsTexture d3d12Source = ValidateTexture(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (destination is not D3D12SilkPickReadbackBuffer d3d12Destination ||
            !ReferenceEquals(d3d12Destination.Device, Device))
        {
            throw new ArgumentException(
                "The readback buffer was not created by this D3D12 device.",
                nameof(destination));
        }
        d3d12Destination.ThrowIfDisposed();
        if (d3d12Destination.DeviceGeneration != Device.PickDeviceGeneration)
        {
            throw new InvalidOperationException(
                "The D3D12 pick readback belongs to an invalid device generation.");
        }
        if (_pickReadbackDestination is not null)
        {
            throw new InvalidOperationException(
                "A D3D12 command list can copy only one pick pixel.");
        }
        _pickReadbackDestination = d3d12Destination;
        _commands.Add(D3D12GraphicsCommand.CopyPickPixel(
            d3d12Source,
            coordinate,
            d3d12Destination));
    }
}

internal enum D3D12PickCommandKind
{
    None,
    SetPipeline,
    SetBaseToken,
    CopyPixel
}

internal enum D3D12PickFailure
{
    None,
    CopyFailure,
    DeviceRemoved,
    DeviceReset
}

internal readonly record struct D3D12PickNativeStatistics(
    long PipelineCreations,
    long ReadbackCreations,
    long CommandAllocatorCreations,
    long CommandListCreations,
    long FenceCreations,
    long Submissions,
    long Copies,
    long Reads,
    SilkTexturePixelCoordinate LastCoordinate,
    ulong DeviceGeneration);

[SupportedOSPlatform("windows")]
internal readonly unsafe struct D3D12PickExecution(
    ID3D12CommandAllocator* allocator,
    ID3D12GraphicsCommandList* commands,
    ID3D12Fence* fence,
    ulong fenceValue)
{
    internal ID3D12CommandAllocator* Allocator { get; } = allocator;

    internal ID3D12GraphicsCommandList* Commands { get; } = commands;

    internal ID3D12Fence* Fence { get; } = fence;

    internal ulong FenceValue { get; } = fenceValue;
}

[SupportedOSPlatform("windows")]
internal sealed unsafe class D3D12SilkPickSubmission(
    D3D12SilkGraphicsDevice device,
    D3D12SilkPickReadbackBuffer readback,
    ulong fenceValue,
    IDisposable[] leases,
    nint[] uploadResources)
    : ISilkGraphicsSubmission
{
    private readonly object _gate = new();
    private IDisposable[]? _leases = leases;
    private nint[]? _uploadResources = uploadResources;
    private bool _completed;
    private bool _disposed;

    public bool IsCompleted
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_completed)
                {
                    return true;
                }
                if (device.ArePickCompletionsHeldForTesting ||
                    !readback.IsSubmissionCompleted(fenceValue))
                {
                    return false;
                }
                Complete();
                return true;
            }
        }
    }

    public void Wait()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_completed)
            {
                return;
            }
            readback.WaitForSubmission(fenceValue);
            Complete();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            if (!_completed)
            {
                try
                {
                    readback.WaitForSubmission(fenceValue);
                    Complete();
                }
                catch when (device.IsDeviceRemoved())
                {
                    Abandon();
                }
            }
            _disposed = true;
        }
        device.ReleaseDependentObject();
    }

    private void Complete()
    {
        readback.CompleteSubmission(fenceValue);
        ReleaseRetainedResources();
    }

    private void Abandon()
    {
        readback.AbandonSubmission(fenceValue);
        ReleaseRetainedResources();
    }

    private void ReleaseRetainedResources()
    {
        _completed = true;
        IDisposable[]? activeLeases = Interlocked.Exchange(ref _leases, null);
        if (activeLeases is not null)
        {
            foreach (IDisposable lease in activeLeases)
            {
                lease.Dispose();
            }
        }
        nint[]? resources = Interlocked.Exchange(ref _uploadResources, null);
        if (resources is null)
        {
            return;
        }
        foreach (nint resource in resources)
        {
            ID3D12Resource* pointer = (ID3D12Resource*)resource;
            D3D12SilkGraphicsDevice.Release(ref pointer);
        }
    }
}

internal sealed class D3D12PickDeviceLostException : InvalidOperationException
{
    internal D3D12PickDeviceLostException(int reason)
        : base($"Injected D3D12 pick device loss 0x{reason:X8}.")
    {
        Reason = reason;
        HResult = reason;
    }

    internal int Reason { get; }
}
