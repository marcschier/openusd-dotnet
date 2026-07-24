// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace OpenUsd.Rendering.Silk.D3D12;

[SupportedOSPlatform("windows")]
public sealed unsafe partial class D3D12SilkGraphicsDevice
    : ISilkSelectionOutlineGraphicsDevice
{
    private long _selectionMaskPipelineCreateCount;
    private long _selectionOutlinePipelineCreateCount;
    private long _selectionBindingCreateCount;
    private long _activeSelectionMaskPipelines;
    private long _activeSelectionOutlinePipelines;
    private long _activeSelectionBindings;

    /// <inheritdoc/>
    public ulong SelectionOutlineDeviceGeneration => PickDeviceGeneration;

    /// <inheritdoc/>
    public SilkSelectionOutlineCapabilities SelectionOutlineCapabilities =>
        SilkSelectionOutlineCapabilities.VisibleOnly;

    /// <inheritdoc/>
    public ISilkSelectionMaskGraphicsPipeline CreateSelectionMaskGraphicsPipeline(
        SilkSelectionMaskPipelineDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_device == null, this);
        descriptor.Validate();
        ValidateSelectionShaderFormat(
            descriptor.VertexShader.Format,
            nameof(descriptor));
        SilkSelectionMaskPipelineDescriptor retainedDescriptor = descriptor with
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
            CreateSelectionMaskRootSignature(out rootSignature);
            CreateSelectionMaskPipelineState(
                retainedDescriptor,
                rootSignature,
                out pipeline);
            var result = new D3D12SilkSelectionMaskGraphicsPipeline(
                this,
                retainedDescriptor,
                SelectionOutlineDeviceGeneration,
                rootSignature,
                pipeline);
            Interlocked.Increment(ref _selectionMaskPipelineCreateCount);
            Interlocked.Increment(ref _activeSelectionMaskPipelines);
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
    public ISilkSelectionOutlineGraphicsPipeline CreateSelectionOutlineGraphicsPipeline(
        SilkSelectionOutlinePipelineDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_device == null, this);
        descriptor.Validate();
        ValidateSelectionShaderFormat(
            descriptor.VertexShader.Format,
            nameof(descriptor));
        SilkSelectionOutlinePipelineDescriptor retainedDescriptor = descriptor with
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
            CreateSelectionOutlineRootSignature(out rootSignature);
            CreateSelectionOutlinePipelineState(
                retainedDescriptor,
                rootSignature,
                out pipeline);
            var result = new D3D12SilkSelectionOutlineGraphicsPipeline(
                this,
                retainedDescriptor,
                SelectionOutlineDeviceGeneration,
                rootSignature,
                pipeline);
            Interlocked.Increment(ref _selectionOutlinePipelineCreateCount);
            Interlocked.Increment(ref _activeSelectionOutlinePipelines);
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
    public ISilkSelectionOutlineBinding CreateSelectionOutlineBinding(
        SilkSelectionOutlineBindingDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_device == null, this);
        descriptor.Validate();
        if (descriptor.MaskTexture is not D3D12SilkGraphicsTexture mask ||
            descriptor.VisibleDepthTexture is not D3D12SilkGraphicsTexture depth ||
            descriptor.Sampler is not D3D12SilkGraphicsSampler sampler ||
            descriptor.Parameters is not D3D12SilkGraphicsBuffer parameters ||
            !ReferenceEquals(mask.Device, this) ||
            !ReferenceEquals(depth.Device, this) ||
            !ReferenceEquals(sampler.Device, this) ||
            !ReferenceEquals(parameters.Device, this))
        {
            throw new ArgumentException(
                "Selection-outline resources must belong to this D3D12 device.",
                nameof(descriptor));
        }
        mask.ThrowIfDisposed();
        depth.ThrowIfDisposed();
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
                mask.ShaderResourceView,
                DescriptorHeapType.CbvSrvUav);
            destination = new CpuDescriptorHandle(destination.Ptr + increment);
            _device->CopyDescriptorsSimple(
                1,
                destination,
                depth.ShaderResourceView,
                DescriptorHeapType.CbvSrvUav);

            retainedSamplerHeap = sampler.Heap;
            _ = ((IUnknown*)retainedSamplerHeap)->AddRef();
            leases =
            [
                mask.AcquireLease(),
                depth.AcquireLease(),
                parameters.AcquireLease()
            ];
            var result = new D3D12SilkSelectionOutlineBinding(
                this,
                descriptor,
                SelectionOutlineDeviceGeneration,
                resources,
                retainedSamplerHeap,
                parameters,
                leases);
            Interlocked.Increment(ref _selectionBindingCreateCount);
            Interlocked.Increment(ref _activeSelectionBindings);
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

    internal D3D12SelectionOutlineNativeStatistics
        SelectionOutlineNativeStatisticsForTesting => new(
            Interlocked.Read(ref _selectionMaskPipelineCreateCount),
            Interlocked.Read(ref _selectionOutlinePipelineCreateCount),
            Interlocked.Read(ref _selectionBindingCreateCount),
            Interlocked.Read(ref _activeSelectionMaskPipelines),
            Interlocked.Read(ref _activeSelectionOutlinePipelines),
            Interlocked.Read(ref _activeSelectionBindings),
            SelectionOutlineDeviceGeneration);

    internal void InvalidateSelectionOutlineDeviceGenerationForTesting() =>
        AdvancePickDeviceGeneration();

    internal void ReleaseSelectionMaskPipeline() =>
        Interlocked.Decrement(ref _activeSelectionMaskPipelines);

    internal void ReleaseSelectionOutlinePipeline() =>
        Interlocked.Decrement(ref _activeSelectionOutlinePipelines);

    internal void ReleaseSelectionOutlineBinding() =>
        Interlocked.Decrement(ref _activeSelectionBindings);

    private static void ValidateSelectionShaderFormat(
        SilkShaderBinaryFormat format,
        string parameterName)
    {
        if (format != SilkShaderBinaryFormat.Dxil)
        {
            throw new ArgumentException(
                "D3D12 selection-outline pipelines require checked DXIL shaders.",
                parameterName);
        }
    }

    private void CreateSelectionMaskRootSignature(
        out ID3D12RootSignature* rootSignature)
    {
        var parameter = new RootParameter(
            RootParameterType.TypeCbv,
            shaderVisibility: ShaderVisibility.Vertex,
            descriptor: new RootDescriptor(0, 0));
        var description = new RootSignatureDesc(
            1,
            &parameter,
            flags: RootSignatureFlags.AllowInputAssemblerInputLayout);
        CreateSelectionRootSignature(description, out rootSignature);
    }

    private void CreateSelectionOutlineRootSignature(
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

    private void CreateSelectionRootSignature(
        RootSignatureDesc description,
        out ID3D12RootSignature* rootSignature)
    {
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

    private void CreateSelectionMaskPipelineState(
        SilkSelectionMaskPipelineDescriptor descriptor,
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
                    DepthWriteMask = DepthWriteMask.Zero,
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

    private void CreateSelectionOutlinePipelineState(
        SilkSelectionOutlinePipelineDescriptor descriptor,
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
                BlendState = CreateSelectionOutlineBlendState(),
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
            pipelineDescription.RTVFormats[0] = Format.FormatR8G8B8A8Unorm;
            Guid pipelineId = ID3D12PipelineState.Guid;
            SilkMarshal.ThrowHResult(_device->CreateGraphicsPipelineState(
                &pipelineDescription,
                &pipelineId,
                (void**)&nativePipeline));
        }
        pipeline = nativePipeline;
    }

    private static BlendDesc CreateSelectionOutlineBlendState()
    {
        var blend = new BlendDesc
        {
            AlphaToCoverageEnable = false,
            IndependentBlendEnable = false
        };
        blend.RenderTarget[0] = new RenderTargetBlendDesc
        {
            BlendEnable = true,
            LogicOpEnable = false,
            SrcBlend = Blend.SrcAlpha,
            DestBlend = Blend.InvSrcAlpha,
            BlendOp = BlendOp.Add,
            SrcBlendAlpha = Blend.One,
            DestBlendAlpha = Blend.InvSrcAlpha,
            BlendOpAlpha = BlendOp.Add,
            RenderTargetWriteMask = (byte)ColorWriteEnable.All
        };
        return blend;
    }
}

[SupportedOSPlatform("windows")]
internal sealed unsafe class D3D12SilkSelectionMaskGraphicsPipeline(
    D3D12SilkGraphicsDevice device,
    SilkSelectionMaskPipelineDescriptor descriptor,
    ulong deviceGeneration,
    ID3D12RootSignature* rootSignature,
    ID3D12PipelineState* pipeline)
    : SilkGraphicsResourceBase, ISilkSelectionMaskGraphicsPipeline
{
    private ID3D12RootSignature* _rootSignature = rootSignature;
    private ID3D12PipelineState* _pipeline = pipeline;

    internal D3D12SilkGraphicsDevice Device { get; } = device;

    internal ulong DeviceGeneration { get; } = deviceGeneration;

    internal ID3D12RootSignature* RootSignature => _rootSignature;

    internal ID3D12PipelineState* Pipeline => _pipeline;

    public SilkSelectionMaskPipelineDescriptor Descriptor { get; } = descriptor;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfDisposed() => ThrowIfResourceDisposed();

    protected override void ReleaseNative()
    {
        D3D12SilkGraphicsDevice.Release(ref _pipeline);
        D3D12SilkGraphicsDevice.Release(ref _rootSignature);
        Device.ReleaseSelectionMaskPipeline();
        Device.ReleaseDependentObject();
    }
}

[SupportedOSPlatform("windows")]
internal sealed unsafe class D3D12SilkSelectionOutlineGraphicsPipeline(
    D3D12SilkGraphicsDevice device,
    SilkSelectionOutlinePipelineDescriptor descriptor,
    ulong deviceGeneration,
    ID3D12RootSignature* rootSignature,
    ID3D12PipelineState* pipeline)
    : SilkGraphicsResourceBase, ISilkSelectionOutlineGraphicsPipeline
{
    private ID3D12RootSignature* _rootSignature = rootSignature;
    private ID3D12PipelineState* _pipeline = pipeline;

    internal D3D12SilkGraphicsDevice Device { get; } = device;

    internal ulong DeviceGeneration { get; } = deviceGeneration;

    internal ID3D12RootSignature* RootSignature => _rootSignature;

    internal ID3D12PipelineState* Pipeline => _pipeline;

    public SilkSelectionOutlinePipelineDescriptor Descriptor { get; } = descriptor;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfDisposed() => ThrowIfResourceDisposed();

    protected override void ReleaseNative()
    {
        D3D12SilkGraphicsDevice.Release(ref _pipeline);
        D3D12SilkGraphicsDevice.Release(ref _rootSignature);
        Device.ReleaseSelectionOutlinePipeline();
        Device.ReleaseDependentObject();
    }
}

[SupportedOSPlatform("windows")]
internal sealed unsafe class D3D12SilkSelectionOutlineBinding(
    D3D12SilkGraphicsDevice device,
    SilkSelectionOutlineBindingDescriptor descriptor,
    ulong deviceGeneration,
    ID3D12DescriptorHeap* resourceHeap,
    ID3D12DescriptorHeap* samplerHeap,
    D3D12SilkGraphicsBuffer parameters,
    IDisposable[] leases)
    : SilkGraphicsResourceBase, ISilkSelectionOutlineBinding
{
    private ID3D12DescriptorHeap* _resourceHeap = resourceHeap;
    private ID3D12DescriptorHeap* _samplerHeap = samplerHeap;
    private IDisposable[]? _leases = leases;

    internal D3D12SilkGraphicsDevice Device { get; } = device;

    internal ulong DeviceGeneration { get; } = deviceGeneration;

    internal ID3D12DescriptorHeap* ResourceHeap => _resourceHeap;

    internal ID3D12DescriptorHeap* SamplerHeap => _samplerHeap;

    internal D3D12SilkGraphicsBuffer Parameters { get; } = parameters;

    public SilkSelectionOutlineBindingDescriptor Descriptor { get; } = descriptor;

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
        Device.ReleaseSelectionOutlineBinding();
        Device.ReleaseDependentObject();
    }
}

internal readonly record struct D3D12SelectionOutlineNativeStatistics(
    long MaskPipelineCreations,
    long OutlinePipelineCreations,
    long BindingCreations,
    long ActiveMaskPipelines,
    long ActiveOutlinePipelines,
    long ActiveBindings,
    ulong DeviceGeneration);

[SupportedOSPlatform("windows")]
internal sealed partial class D3D12SilkGraphicsCommandList
    : ISilkSelectionOutlineGraphicsCommandList
{
    private D3D12SilkSelectionMaskGraphicsPipeline? _selectionMaskPipeline;
    private D3D12SilkSelectionOutlineGraphicsPipeline? _selectionOutlinePipeline;
    private D3D12SilkSelectionOutlineBinding? _selectionOutlineBinding;
    private D3D12SelectionRenderingKind _selectionRenderingKind;

    public void BeginSelectionMaskRendering(
        SilkSelectionMaskRenderingDescriptor descriptor)
    {
        ThrowIfUnavailable();
        if (_rendering)
        {
            throw new InvalidOperationException("A rendering scope is already active.");
        }
        descriptor.Validate();
        D3D12SilkGraphicsTexture mask = ValidateTexture(descriptor.MaskAttachment);
        D3D12SilkGraphicsTexture depth =
            ValidateTexture(descriptor.VisibleDepthAttachment);
        _colorAttachment = mask;
        _depthAttachment = depth;
        _selectionRenderingKind = D3D12SelectionRenderingKind.Mask;
        _rendering = true;
        _commands.Add(D3D12GraphicsCommand.BeginSelectionMask(mask, depth));
    }

    public void SetSelectionMaskGraphicsPipeline(
        ISilkSelectionMaskGraphicsPipeline pipeline)
    {
        ThrowIfRendering();
        ArgumentNullException.ThrowIfNull(pipeline);
        if (_selectionRenderingKind != D3D12SelectionRenderingKind.Mask ||
            pipeline is not D3D12SilkSelectionMaskGraphicsPipeline d3d12Pipeline ||
            !ReferenceEquals(d3d12Pipeline.Device, Device))
        {
            throw new ArgumentException(
                "The selection-mask pipeline is not valid for this D3D12 pass.",
                nameof(pipeline));
        }
        d3d12Pipeline.ThrowIfDisposed();
        if (d3d12Pipeline.DeviceGeneration !=
            Device.SelectionOutlineDeviceGeneration)
        {
            throw new InvalidOperationException(
                "The D3D12 selection-mask pipeline belongs to an invalid device generation.");
        }
        _pipeline = null;
        _pickPipeline = null;
        _pickBaseToken = 0;
        _selectionMaskPipeline = d3d12Pipeline;
        _selectionOutlinePipeline = null;
        _commands.Add(D3D12GraphicsCommand.SetSelectionMaskPipeline(d3d12Pipeline));
    }

    public void BeginSelectionOutlineRendering(
        SilkSelectionOutlineRenderingDescriptor descriptor)
    {
        ThrowIfUnavailable();
        if (_rendering)
        {
            throw new InvalidOperationException("A rendering scope is already active.");
        }
        descriptor.Validate();
        D3D12SilkGraphicsTexture color =
            ValidateTexture(descriptor.VisibleColorAttachment);
        _colorAttachment = color;
        _depthAttachment = null;
        _selectionRenderingKind = D3D12SelectionRenderingKind.Outline;
        _rendering = true;
        _commands.Add(D3D12GraphicsCommand.BeginSelectionOutline(color));
    }

    public void SetSelectionOutlineGraphicsPipeline(
        ISilkSelectionOutlineGraphicsPipeline pipeline)
    {
        ThrowIfRendering();
        ArgumentNullException.ThrowIfNull(pipeline);
        if (_selectionRenderingKind != D3D12SelectionRenderingKind.Outline ||
            pipeline is not D3D12SilkSelectionOutlineGraphicsPipeline d3d12Pipeline ||
            !ReferenceEquals(d3d12Pipeline.Device, Device))
        {
            throw new ArgumentException(
                "The selection-outline pipeline is not valid for this D3D12 pass.",
                nameof(pipeline));
        }
        d3d12Pipeline.ThrowIfDisposed();
        if (d3d12Pipeline.DeviceGeneration !=
            Device.SelectionOutlineDeviceGeneration)
        {
            throw new InvalidOperationException(
                "The D3D12 selection-outline pipeline belongs to an invalid device generation.");
        }
        _pipeline = null;
        _pickPipeline = null;
        _pickBaseToken = 0;
        _selectionMaskPipeline = null;
        _selectionOutlinePipeline = d3d12Pipeline;
        _commands.Add(D3D12GraphicsCommand.SetSelectionOutlinePipeline(d3d12Pipeline));
    }

    public void SetSelectionOutlineBinding(ISilkSelectionOutlineBinding binding)
    {
        ThrowIfRendering();
        ArgumentNullException.ThrowIfNull(binding);
        if (_selectionRenderingKind != D3D12SelectionRenderingKind.Outline ||
            binding is not D3D12SilkSelectionOutlineBinding d3d12Binding ||
            !ReferenceEquals(d3d12Binding.Device, Device))
        {
            throw new ArgumentException(
                "The selection-outline binding is not valid for this D3D12 pass.",
                nameof(binding));
        }
        d3d12Binding.ThrowIfDisposed();
        if (d3d12Binding.DeviceGeneration !=
            Device.SelectionOutlineDeviceGeneration)
        {
            throw new InvalidOperationException(
                "The D3D12 selection-outline binding belongs to an invalid device generation.");
        }
        _selectionOutlineBinding = d3d12Binding;
        _commands.Add(D3D12GraphicsCommand.SetSelectionOutlineBinding(d3d12Binding));
    }

    public void DrawSelectionOutlineFullscreenTriangle()
    {
        ThrowIfRendering();
        if (_selectionRenderingKind != D3D12SelectionRenderingKind.Outline ||
            _colorAttachment is null ||
            _selectionOutlinePipeline is null ||
            _selectionOutlineBinding is null ||
            _viewport is null ||
            _scissor is null)
        {
            throw new InvalidOperationException(
                "Fullscreen selection outlining requires color, pipeline, binding, viewport, and scissor.");
        }
        _commands.Add(D3D12GraphicsCommand.DrawSelectionOutlineFullscreenTriangle());
    }
}

internal enum D3D12SelectionRenderingKind
{
    None,
    Mask,
    Outline
}

internal enum D3D12SelectionOutlineCommandKind
{
    None,
    BeginMask,
    SetMaskPipeline,
    BeginOutline,
    SetOutlinePipeline,
    SetBinding,
    DrawFullscreenTriangle
}
