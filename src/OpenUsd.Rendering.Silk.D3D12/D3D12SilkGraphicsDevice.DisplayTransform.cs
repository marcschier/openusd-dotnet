// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace OpenUsd.Rendering.Silk.D3D12;

[SupportedOSPlatform("windows")]
public sealed unsafe partial class D3D12SilkGraphicsDevice
    : ISilkDisplayTransformGraphicsDevice
{
    private long _displayTransformPipelineCreateCount;
    private long _displayTransformBindingCreateCount;
    private long _activeDisplayTransformPipelines;
    private long _activeDisplayTransformBindings;

    /// <inheritdoc/>
    public ulong DisplayTransformDeviceGeneration => PickDeviceGeneration;

    /// <inheritdoc/>
    public ISilkDisplayTransformGraphicsPipeline CreateDisplayTransformGraphicsPipeline(
        SilkDisplayTransformPipelineDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_device == null, this);
        descriptor.Validate();
        ValidateSelectionShaderFormat(
            descriptor.VertexShader.Format,
            nameof(descriptor));
        SilkDisplayTransformPipelineDescriptor retainedDescriptor = descriptor with
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
            CreateDisplayTransformRootSignature(out rootSignature);
            CreateDisplayTransformPipelineState(
                retainedDescriptor,
                rootSignature,
                out pipeline);
            var result = new D3D12SilkDisplayTransformGraphicsPipeline(
                this,
                retainedDescriptor,
                DisplayTransformDeviceGeneration,
                rootSignature,
                pipeline);
            Interlocked.Increment(ref _displayTransformPipelineCreateCount);
            Interlocked.Increment(ref _activeDisplayTransformPipelines);
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
    public ISilkDisplayTransformBinding CreateDisplayTransformBinding(
        SilkDisplayTransformBindingDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_device == null, this);
        descriptor.Validate();
        if (descriptor.SceneColorTexture is not D3D12SilkGraphicsTexture sceneColor ||
            descriptor.LatticeTexture is not D3D12SilkGraphicsTexture lattice ||
            descriptor.Sampler is not D3D12SilkGraphicsSampler sampler ||
            descriptor.Parameters is not D3D12SilkGraphicsBuffer parameters ||
            !ReferenceEquals(sceneColor.Device, this) ||
            !ReferenceEquals(lattice.Device, this) ||
            !ReferenceEquals(sampler.Device, this) ||
            !ReferenceEquals(parameters.Device, this))
        {
            throw new ArgumentException(
                "Display-transform resources must belong to this D3D12 device.",
                nameof(descriptor));
        }
        sceneColor.ThrowIfDisposed();
        lattice.ThrowIfDisposed();
        sampler.ThrowIfDisposed();
        parameters.ThrowIfDisposed();

        ID3D12DescriptorHeap* resources = null;
        ID3D12DescriptorHeap* retainedSamplerHeap = null;
        IDisposable[]? leases = null;
        bool success = false;
        RegisterDependentObject();
        try
        {
            var heapDescription = new DescriptorHeapDesc(
                DescriptorHeapType.CbvSrvUav,
                2,
                DescriptorHeapFlags.ShaderVisible,
                0);
            Guid heapId = ID3D12DescriptorHeap.Guid;
            SilkMarshal.ThrowHResult(_device->CreateDescriptorHeap(
                &heapDescription,
                &heapId,
                (void**)&resources));
            uint increment = _device->GetDescriptorHandleIncrementSize(
                DescriptorHeapType.CbvSrvUav);
            CpuDescriptorHandle destination =
                resources->GetCPUDescriptorHandleForHeapStart();
            _device->CopyDescriptorsSimple(
                1,
                destination,
                sceneColor.ShaderResourceView,
                DescriptorHeapType.CbvSrvUav);
            destination = new CpuDescriptorHandle(destination.Ptr + increment);
            _device->CopyDescriptorsSimple(
                1,
                destination,
                lattice.ShaderResourceView,
                DescriptorHeapType.CbvSrvUav);

            retainedSamplerHeap = sampler.Heap;
            _ = ((IUnknown*)retainedSamplerHeap)->AddRef();
            leases =
            [
                sceneColor.AcquireLease(),
                lattice.AcquireLease(),
                parameters.AcquireLease()
            ];
            var result = new D3D12SilkDisplayTransformBinding(
                this,
                descriptor,
                DisplayTransformDeviceGeneration,
                resources,
                retainedSamplerHeap,
                parameters,
                leases);
            Interlocked.Increment(ref _displayTransformBindingCreateCount);
            Interlocked.Increment(ref _activeDisplayTransformBindings);
            success = true;
            return result;
        }
        finally
        {
            if (!success)
            {
                if (leases is not null)
                {
                    foreach (IDisposable lease in leases)
                    {
                        lease.Dispose();
                    }
                }
                Release(ref retainedSamplerHeap);
                Release(ref resources);
                ReleaseDependentObject();
            }
        }
    }

    internal D3D12DisplayTransformNativeStatistics
        DisplayTransformNativeStatisticsForTesting => new(
            Interlocked.Read(ref _displayTransformPipelineCreateCount),
            Interlocked.Read(ref _displayTransformBindingCreateCount),
            Interlocked.Read(ref _activeDisplayTransformPipelines),
            Interlocked.Read(ref _activeDisplayTransformBindings),
            DisplayTransformDeviceGeneration);

    internal void ReleaseDisplayTransformPipeline() =>
        Interlocked.Decrement(ref _activeDisplayTransformPipelines);

    internal void ReleaseDisplayTransformBinding() =>
        Interlocked.Decrement(ref _activeDisplayTransformBindings);

    private void CreateDisplayTransformRootSignature(
        out ID3D12RootSignature* rootSignature)
    {
        var textureRange = new DescriptorRange(
            DescriptorRangeType.Srv,
            2,
            0,
            0,
            0);
        var samplerRange = new DescriptorRange(
            DescriptorRangeType.Sampler,
            1,
            0,
            0,
            0);
        RootParameter* parameters = stackalloc RootParameter[3];
        parameters[0] = new RootParameter(
            RootParameterType.TypeDescriptorTable,
            shaderVisibility: ShaderVisibility.Pixel,
            descriptorTable: new RootDescriptorTable(1, &textureRange));
        parameters[1] = new RootParameter(
            RootParameterType.TypeDescriptorTable,
            shaderVisibility: ShaderVisibility.Pixel,
            descriptorTable: new RootDescriptorTable(1, &samplerRange));
        parameters[2] = new RootParameter(
            RootParameterType.TypeCbv,
            shaderVisibility: ShaderVisibility.Pixel,
            descriptor: new RootDescriptor(0, 0));
        var description = new RootSignatureDesc(
            3,
            parameters,
            flags: RootSignatureFlags.None);
        CreateSelectionRootSignature(description, out rootSignature);
    }

    private void CreateDisplayTransformPipelineState(
        SilkDisplayTransformPipelineDescriptor descriptor,
        ID3D12RootSignature* rootSignature,
        out ID3D12PipelineState* pipeline)
    {
        byte[] vertexCode = descriptor.VertexShader.Code.ToArray();
        byte[] fragmentCode = descriptor.FragmentShader.Code.ToArray();
        ID3D12PipelineState* nativePipeline = null;
        fixed (byte* vertexPointer = vertexCode)
        fixed (byte* fragmentPointer = fragmentCode)
        {
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
                    DepthEnable = false,
                    DepthWriteMask = DepthWriteMask.Zero,
                    DepthFunc = ComparisonFunc.Always,
                    StencilEnable = false
                },
                InputLayout = new InputLayoutDesc(null, 0),
                PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
                NumRenderTargets = 1,
                DSVFormat = Format.FormatUnknown,
                SampleDesc = new SampleDesc(1, 0)
            };
            pipelineDescription.RTVFormats[0] =
                GetNativeFormat(descriptor.ColorFormat);
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
internal sealed unsafe class D3D12SilkDisplayTransformGraphicsPipeline(
    D3D12SilkGraphicsDevice device,
    SilkDisplayTransformPipelineDescriptor descriptor,
    ulong deviceGeneration,
    ID3D12RootSignature* rootSignature,
    ID3D12PipelineState* pipeline)
    : SilkGraphicsResourceBase, ISilkDisplayTransformGraphicsPipeline
{
    private ID3D12RootSignature* _rootSignature = rootSignature;
    private ID3D12PipelineState* _pipeline = pipeline;

    internal D3D12SilkGraphicsDevice Device { get; } = device;

    internal ulong DeviceGeneration { get; } = deviceGeneration;

    internal ID3D12RootSignature* RootSignature => _rootSignature;

    internal ID3D12PipelineState* Pipeline => _pipeline;

    public SilkDisplayTransformPipelineDescriptor Descriptor { get; } = descriptor;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfDisposed() => ThrowIfResourceDisposed();

    protected override void ReleaseNative()
    {
        D3D12SilkGraphicsDevice.Release(ref _pipeline);
        D3D12SilkGraphicsDevice.Release(ref _rootSignature);
        Device.ReleaseDisplayTransformPipeline();
        Device.ReleaseDependentObject();
    }
}

[SupportedOSPlatform("windows")]
internal sealed unsafe class D3D12SilkDisplayTransformBinding(
    D3D12SilkGraphicsDevice device,
    SilkDisplayTransformBindingDescriptor descriptor,
    ulong deviceGeneration,
    ID3D12DescriptorHeap* resourceHeap,
    ID3D12DescriptorHeap* samplerHeap,
    D3D12SilkGraphicsBuffer parameters,
    IDisposable[] leases)
    : SilkGraphicsResourceBase, ISilkDisplayTransformBinding
{
    private ID3D12DescriptorHeap* _resourceHeap = resourceHeap;
    private ID3D12DescriptorHeap* _samplerHeap = samplerHeap;
    private IDisposable[]? _leases = leases;

    internal D3D12SilkGraphicsDevice Device { get; } = device;

    internal ulong DeviceGeneration { get; } = deviceGeneration;

    internal ID3D12DescriptorHeap* ResourceHeap => _resourceHeap;

    internal ID3D12DescriptorHeap* SamplerHeap => _samplerHeap;

    internal D3D12SilkGraphicsBuffer Parameters { get; } = parameters;

    public SilkDisplayTransformBindingDescriptor Descriptor { get; } = descriptor;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfDisposed() => ThrowIfResourceDisposed();

    protected override void ReleaseNative()
    {
        D3D12SilkGraphicsDevice.Release(ref _samplerHeap);
        D3D12SilkGraphicsDevice.Release(ref _resourceHeap);
        IDisposable[]? leases = Interlocked.Exchange(ref _leases, null);
        if (leases is not null)
        {
            foreach (IDisposable lease in leases)
            {
                lease.Dispose();
            }
        }
        Device.ReleaseDisplayTransformBinding();
        Device.ReleaseDependentObject();
    }
}

internal readonly record struct D3D12DisplayTransformNativeStatistics(
    long PipelineCreations,
    long BindingCreations,
    long ActivePipelines,
    long ActiveBindings,
    ulong DeviceGeneration);

[SupportedOSPlatform("windows")]
internal sealed partial class D3D12SilkGraphicsCommandList
    : ISilkDisplayTransformGraphicsCommandList
{
    private D3D12SilkDisplayTransformGraphicsPipeline? _displayTransformPipeline;
    private D3D12SilkDisplayTransformBinding? _displayTransformBinding;
    private bool _displayTransformRendering;

    public void BeginDisplayTransformRendering(
        SilkDisplayTransformRenderingDescriptor descriptor)
    {
        ThrowIfUnavailable();
        if (_rendering)
        {
            throw new InvalidOperationException("A rendering scope is already active.");
        }
        descriptor.Validate();
        D3D12SilkGraphicsTexture color =
            ValidateTexture(descriptor.ColorAttachment);
        D3D12SilkGraphicsTexture sceneColor =
            ValidateTexture(descriptor.SceneColorTexture);
        D3D12SilkGraphicsTexture lattice =
            ValidateTexture(descriptor.LatticeTexture);
        _colorAttachment = color;
        _depthAttachment = null;
        _displayTransformRendering = true;
        _selectionRenderingKind = D3D12SelectionRenderingKind.None;
        _rendering = true;
        _commands.Add(D3D12GraphicsCommand.BeginDisplayTransform(
            color, sceneColor, lattice));
    }

    public void SetDisplayTransformGraphicsPipeline(
        ISilkDisplayTransformGraphicsPipeline pipeline)
    {
        ThrowIfRendering();
        ArgumentNullException.ThrowIfNull(pipeline);
        if (!_displayTransformRendering ||
            pipeline is not D3D12SilkDisplayTransformGraphicsPipeline d3d12Pipeline ||
            !ReferenceEquals(d3d12Pipeline.Device, Device))
        {
            throw new ArgumentException(
                "The display-transform pipeline is not valid for this D3D12 pass.",
                nameof(pipeline));
        }
        d3d12Pipeline.ThrowIfDisposed();
        if (d3d12Pipeline.DeviceGeneration !=
            Device.DisplayTransformDeviceGeneration)
        {
            throw new InvalidOperationException(
                "The D3D12 display-transform pipeline belongs to an invalid device generation.");
        }
        if (_colorAttachment?.Format != d3d12Pipeline.Descriptor.ColorFormat)
        {
            throw new ArgumentException(
                "The display-transform pipeline format does not match the color target.",
                nameof(pipeline));
        }
        _pipeline = null;
        _pickPipeline = null;
        _pickBaseToken = 0;
        _selectionMaskPipeline = null;
        _selectionOutlinePipeline = null;
        _displayTransformPipeline = d3d12Pipeline;
        _commands.Add(
            D3D12GraphicsCommand.SetDisplayTransformPipeline(d3d12Pipeline));
    }

    public void SetDisplayTransformBinding(ISilkDisplayTransformBinding binding)
    {
        ThrowIfRendering();
        ArgumentNullException.ThrowIfNull(binding);
        if (!_displayTransformRendering ||
            binding is not D3D12SilkDisplayTransformBinding d3d12Binding ||
            !ReferenceEquals(d3d12Binding.Device, Device))
        {
            throw new ArgumentException(
                "The display-transform binding is not valid for this D3D12 pass.",
                nameof(binding));
        }
        d3d12Binding.ThrowIfDisposed();
        if (d3d12Binding.DeviceGeneration !=
            Device.DisplayTransformDeviceGeneration)
        {
            throw new InvalidOperationException(
                "The D3D12 display-transform binding belongs to an invalid device generation.");
        }
        _displayTransformBinding = d3d12Binding;
        _commands.Add(D3D12GraphicsCommand.SetDisplayTransformBinding(d3d12Binding));
    }

    public void DrawDisplayTransformFullscreenTriangle()
    {
        ThrowIfRendering();
        if (!_displayTransformRendering ||
            _colorAttachment is null ||
            _displayTransformPipeline is null ||
            _displayTransformBinding is null ||
            _viewport is null ||
            _scissor is null)
        {
            throw new InvalidOperationException(
                "Fullscreen display transform requires color, pipeline, binding, " +
                "viewport, and scissor.");
        }
        _commands.Add(D3D12GraphicsCommand.DrawDisplayTransformFullscreenTriangle());
    }
}

internal enum D3D12DisplayTransformCommandKind
{
    None,
    Begin,
    SetPipeline,
    SetBinding,
    DrawFullscreenTriangle
}
