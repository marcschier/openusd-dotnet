// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace OpenUsd.Rendering.Silk.D3D12;

[SupportedOSPlatform("windows")]
public sealed unsafe partial class D3D12SilkGraphicsDevice
{
    /// <inheritdoc/>
    public ISilkGraphicsShaderModule CreateShaderModule(
        SilkShaderModuleDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_device == null, this);
        descriptor.Validate();
        if (descriptor.Format != SilkShaderBinaryFormat.Dxil)
        {
            throw new ArgumentException("D3D12 shader modules require DXIL.", nameof(descriptor));
        }
        RegisterDependentObject();
        return new D3D12SilkGraphicsShaderModule(
            this,
            descriptor with { Code = descriptor.Code.ToArray() });
    }

    /// <inheritdoc/>
    public ISilkGraphicsBindingLayout CreateBindingLayout(
        SilkBindingLayoutDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_device == null, this);
        descriptor.Validate();
        RegisterDependentObject();
        return new D3D12SilkGraphicsBindingLayout(this, descriptor);
    }

    /// <inheritdoc/>
    public ISilkGraphicsShaderProgram CreateShaderProgram(
        SilkShaderProgramDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_device == null, this);
        if (descriptor.VertexShader is not D3D12SilkGraphicsShaderModule vertex ||
            descriptor.FragmentShader is not D3D12SilkGraphicsShaderModule fragment ||
            descriptor.BindingLayout is not D3D12SilkGraphicsBindingLayout layout ||
            !ReferenceEquals(vertex.Device, this) ||
            !ReferenceEquals(fragment.Device, this) ||
            !ReferenceEquals(layout.Device, this))
        {
            throw new ArgumentException(
                "Shader program resources must belong to this D3D12 device.",
                nameof(descriptor));
        }
        vertex.ThrowIfDisposed();
        fragment.ThrowIfDisposed();
        layout.ThrowIfDisposed();
        if (vertex.Descriptor.Stage != SilkShaderStage.Vertex ||
            fragment.Descriptor.Stage != SilkShaderStage.Fragment)
        {
            throw new ArgumentException(
                "Shader program stages must be vertex then fragment.",
                nameof(descriptor));
        }

        var leases = new IDisposable[]
        {
            vertex.AcquireLease(),
            fragment.AcquireLease(),
            layout.AcquireLease()
        };
        RegisterDependentObject();
        return new D3D12SilkGraphicsShaderProgram(
            this,
            vertex,
            fragment,
            layout,
            leases);
    }

    /// <inheritdoc/>
    public ISilkGraphicsPipeline CreateGraphicsPipeline(
        SilkGraphicsPipelineDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_device == null, this);
        descriptor.Validate();
        if (descriptor.Program is not D3D12SilkGraphicsShaderProgram program ||
            !ReferenceEquals(program.Device, this))
        {
            throw new ArgumentException(
                "The shader program was not created by this D3D12 device.",
                nameof(descriptor));
        }
        program.ThrowIfDisposed();

        ID3D12RootSignature* rootSignature = null;
        ID3D12PipelineState* pipeline = null;
        IDisposable? programLease = null;
        bool success = false;
        RegisterDependentObject();
        try
        {
            CreateRootSignature(program.BindingLayout.Descriptor, out rootSignature);
            CreatePipelineState(
                descriptor,
                program,
                rootSignature,
                out pipeline);
            programLease = program.AcquireLease();
            success = true;
            return new D3D12SilkGraphicsPipeline(
                this,
                descriptor,
                rootSignature,
                pipeline,
                programLease);
        }
        finally
        {
            if (!success)
            {
                programLease?.Dispose();
                Release(ref pipeline);
                Release(ref rootSignature);
                ReleaseDependentObject();
            }
        }
    }

    private static ShaderVisibility ToD3D12Visibility(SilkShaderStageVisibility visibility)
    {
        bool vertex = visibility.HasFlag(SilkShaderStageVisibility.Vertex);
        bool fragment = visibility.HasFlag(SilkShaderStageVisibility.Fragment);
        if (vertex && !fragment)
        {
            return ShaderVisibility.Vertex;
        }
        return !vertex && fragment ? ShaderVisibility.Pixel : ShaderVisibility.All;
    }

    private void CreateRootSignature(
        SilkBindingLayoutDescriptor layout,
        out ID3D12RootSignature* rootSignature)
    {
        IReadOnlyList<SilkBindingSlot> slots = layout.MaterialSlots ?? [];
        // Root parameter 0 stays the SceneParameters CBV at b0 so every existing
        // pipeline keeps its exact root signature. Material slots become descriptor
        // ranges in one table each, which is what a descriptor heap binds.
        int parameterCount = 1 + slots.Count;
        RootParameter* parameters = stackalloc RootParameter[parameterCount];
        DescriptorRange* ranges = stackalloc DescriptorRange[Math.Max(slots.Count, 1)];
        parameters[0] = new RootParameter(
            RootParameterType.TypeCbv,
            shaderVisibility: ShaderVisibility.All,
            descriptor: new RootDescriptor(0, 0));
        for (int index = 0; index < slots.Count; index++)
        {
            SilkBindingSlot slot = slots[index];
            if (slot.Kind is SilkBindingKind.UniformBuffer or SilkBindingKind.StorageBuffer)
            {
                parameters[index + 1] = new RootParameter(
                    slot.Kind == SilkBindingKind.UniformBuffer
                        ? RootParameterType.TypeCbv
                        : RootParameterType.TypeSrv,
                    shaderVisibility: ShaderVisibility.All,
                    descriptor: new RootDescriptor(slot.Binding, slot.Set));
                continue;
            }
            ranges[index] = new DescriptorRange(
                slot.Kind == SilkBindingKind.SampledTexture
                    ? DescriptorRangeType.Srv
                    : DescriptorRangeType.Sampler,
                1,
                slot.Binding,
                slot.Set);
            parameters[index + 1] = new RootParameter(
                RootParameterType.TypeDescriptorTable,
                shaderVisibility: ToD3D12Visibility(slot.Visibility),
                descriptorTable: new RootDescriptorTable(1, &ranges[index]));
        }
        var description = new RootSignatureDesc(
            (uint)parameterCount,
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

    private void CreatePipelineState(
        SilkGraphicsPipelineDescriptor descriptor,
        D3D12SilkGraphicsShaderProgram program,
        ID3D12RootSignature* rootSignature,
        out ID3D12PipelineState* pipeline)
    {
        byte[] vertexCode = program.Vertex.Descriptor.Code.ToArray();
        byte[] fragmentCode = program.Fragment.Descriptor.Code.ToArray();
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

    private static BlendDesc CreateBlendState()
    {
        var blend = new BlendDesc
        {
            AlphaToCoverageEnable = false,
            IndependentBlendEnable = false
        };
        blend.RenderTarget[0] = new RenderTargetBlendDesc
        {
            BlendEnable = false,
            LogicOpEnable = false,
            RenderTargetWriteMask = (byte)ColorWriteEnable.All
        };
        return blend;
    }
}

[SupportedOSPlatform("windows")]
internal sealed class D3D12SilkGraphicsShaderModule(
    D3D12SilkGraphicsDevice device,
    SilkShaderModuleDescriptor descriptor)
    : SilkGraphicsResourceBase, ISilkGraphicsShaderModule
{
    internal D3D12SilkGraphicsDevice Device { get; } = device;

    public SilkShaderModuleDescriptor Descriptor { get; } = descriptor;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfDisposed() => ThrowIfResourceDisposed();

    protected override void ReleaseNative() => Device.ReleaseDependentObject();
}

[SupportedOSPlatform("windows")]
internal sealed class D3D12SilkGraphicsBindingLayout(
    D3D12SilkGraphicsDevice device,
    SilkBindingLayoutDescriptor descriptor)
    : SilkGraphicsResourceBase, ISilkGraphicsBindingLayout
{
    internal D3D12SilkGraphicsDevice Device { get; } = device;

    public SilkBindingLayoutDescriptor Descriptor { get; } = descriptor;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfDisposed() => ThrowIfResourceDisposed();

    protected override void ReleaseNative() => Device.ReleaseDependentObject();
}

[SupportedOSPlatform("windows")]
internal sealed class D3D12SilkGraphicsShaderProgram(
    D3D12SilkGraphicsDevice device,
    D3D12SilkGraphicsShaderModule vertex,
    D3D12SilkGraphicsShaderModule fragment,
    D3D12SilkGraphicsBindingLayout layout,
    IDisposable[] resourceLeases)
    : SilkGraphicsResourceBase, ISilkGraphicsShaderProgram
{
    private IDisposable[]? _resourceLeases = resourceLeases;

    internal D3D12SilkGraphicsDevice Device { get; } = device;

    internal D3D12SilkGraphicsShaderModule Vertex { get; } = vertex;

    internal D3D12SilkGraphicsShaderModule Fragment { get; } = fragment;

    public ISilkGraphicsBindingLayout BindingLayout => layout;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfDisposed() => ThrowIfResourceDisposed();

    protected override void ReleaseNative()
    {
        IDisposable[]? leases = Interlocked.Exchange(ref _resourceLeases, null);
        if (leases is not null)
        {
            foreach (IDisposable lease in leases)
            {
                lease.Dispose();
            }
        }
        Device.ReleaseDependentObject();
    }
}

[SupportedOSPlatform("windows")]
internal sealed unsafe class D3D12SilkGraphicsPipeline(
    D3D12SilkGraphicsDevice device,
    SilkGraphicsPipelineDescriptor descriptor,
    ID3D12RootSignature* rootSignature,
    ID3D12PipelineState* pipeline,
    IDisposable programLease)
    : SilkGraphicsResourceBase, ISilkGraphicsPipeline
{
    private ID3D12RootSignature* _rootSignature = rootSignature;
    private ID3D12PipelineState* _pipeline = pipeline;
    private IDisposable? _programLease = programLease;

    internal D3D12SilkGraphicsDevice Device { get; } = device;

    public SilkGraphicsPipelineDescriptor Descriptor { get; } = descriptor;

    internal ID3D12RootSignature* RootSignature => _rootSignature;

    internal ID3D12PipelineState* Pipeline => _pipeline;

    /// <summary>
    /// Gets the binding layout this pipeline was created from, so a submission can
    /// map a material slot to its root parameter index.
    /// </summary>
    internal SilkBindingLayoutDescriptor BindingLayout { get; } =
        descriptor.Program.BindingLayout.Descriptor;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfDisposed() => ThrowIfResourceDisposed();

    protected override void ReleaseNative()
    {
        D3D12SilkGraphicsDevice.Release(ref _pipeline);
        D3D12SilkGraphicsDevice.Release(ref _rootSignature);
        Interlocked.Exchange(ref _programLease, null)?.Dispose();
        Device.ReleaseDependentObject();
    }
}
