// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;

namespace OpenUsd.Rendering.Silk.D3D12;

[SupportedOSPlatform("windows")]
public sealed unsafe partial class D3D12SilkGraphicsDevice
{
    /// <inheritdoc/>
    public ISilkComputeBindingLayout CreateComputeBindingLayout(
        SilkComputeBindingLayoutDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_device == null, this);
        descriptor.Validate();
        RegisterDependentObject();
        return new D3D12SilkComputeBindingLayout(this, descriptor);
    }

    /// <inheritdoc/>
    public ISilkComputeShaderProgram CreateComputeShaderProgram(
        SilkComputeShaderProgramDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_device == null, this);
        if (descriptor.ComputeShader is not D3D12SilkGraphicsShaderModule shader ||
            descriptor.BindingLayout is not D3D12SilkComputeBindingLayout layout ||
            !ReferenceEquals(shader.Device, this) ||
            !ReferenceEquals(layout.Device, this))
        {
            throw new ArgumentException(
                "Compute program resources must belong to this D3D12 device.",
                nameof(descriptor));
        }
        shader.ThrowIfDisposed();
        layout.ThrowIfDisposed();
        if (shader.Descriptor.Stage != SilkShaderStage.Compute)
        {
            throw new ArgumentException(
                "The compute program requires a compute shader module.",
                nameof(descriptor));
        }

        IDisposable[] leases = [shader.AcquireLease(), layout.AcquireLease()];
        RegisterDependentObject();
        return new D3D12SilkComputeShaderProgram(
            this,
            shader,
            layout,
            leases);
    }

    /// <inheritdoc/>
    public ISilkComputePipeline CreateComputePipeline(
        SilkComputePipelineDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_device == null, this);
        descriptor.Validate();
        if (descriptor.Program is not D3D12SilkComputeShaderProgram program ||
            !ReferenceEquals(program.Device, this))
        {
            throw new ArgumentException(
                "The compute program was not created by this D3D12 device.",
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
            CreateComputeRootSignature(out rootSignature);
            byte[] code = program.Shader.Descriptor.Code.ToArray();
            fixed (byte* codePointer = code)
            {
                var pipelineDescription = new ComputePipelineStateDesc
                {
                    PRootSignature = rootSignature,
                    CS = new ShaderBytecode(codePointer, checked((nuint)code.Length))
                };
                Guid pipelineId = ID3D12PipelineState.Guid;
                SilkMarshal.ThrowHResult(_device->CreateComputePipelineState(
                    &pipelineDescription,
                    &pipelineId,
                    (void**)&pipeline));
            }
            programLease = program.AcquireLease();
            success = true;
            return new D3D12SilkComputePipeline(
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

    private void CreateComputeRootSignature(out ID3D12RootSignature* rootSignature)
    {
        RootParameter* parameters = stackalloc RootParameter[2];
        parameters[0] = new RootParameter(
            RootParameterType.TypeUav,
            shaderVisibility: ShaderVisibility.All,
            descriptor: new RootDescriptor(0, 0));
        parameters[1] = new RootParameter(
            RootParameterType.TypeCbv,
            shaderVisibility: ShaderVisibility.All,
            descriptor: new RootDescriptor(1, 0));
        var description = new RootSignatureDesc(2, parameters);
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

    internal void Readback(
        D3D12SilkGraphicsBuffer buffer,
        Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_device == null || _queue == null, this);
        buffer.ThrowIfDisposed();
        WaitIdle();

        ID3D12Resource* readback = null;
        ID3D12CommandAllocator* allocator = null;
        ID3D12GraphicsCommandList* commands = null;
        ID3D12Fence* fence = null;
        bool submitted = false;
        try
        {
            var heap = new HeapProperties(HeapType.Readback);
            var description = new ResourceDesc(
                ResourceDimension.Buffer,
                0,
                buffer.Size,
                1,
                1,
                1,
                global::Silk.NET.DXGI.Format.FormatUnknown,
                new global::Silk.NET.DXGI.SampleDesc(1, 0),
                TextureLayout.LayoutRowMajor,
                ResourceFlags.None);
            Guid resourceId = ID3D12Resource.Guid;
            SilkMarshal.ThrowHResult(_device->CreateCommittedResource(
                &heap,
                HeapFlags.None,
                &description,
                ResourceStates.CopyDest,
                null,
                &resourceId,
                (void**)&readback));

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
            Transition(
                commands,
                buffer.Resource,
                buffer.State,
                ResourceStates.CopySource);
            commands->CopyBufferRegion(
                readback,
                0,
                buffer.Resource,
                0,
                buffer.Size);
            Transition(
                commands,
                buffer.Resource,
                ResourceStates.CopySource,
                buffer.State);
            SilkMarshal.ThrowHResult(commands->Close());

            Guid fenceId = ID3D12Fence.Guid;
            SilkMarshal.ThrowHResult(_device->CreateFence(
                0,
                FenceFlags.None,
                &fenceId,
                (void**)&fence));
            ID3D12CommandList* commandList = (ID3D12CommandList*)commands;
            _queue->ExecuteCommandLists(1, &commandList);
            submitted = true;
            SilkMarshal.ThrowHResult(_queue->Signal(fence, 1));
            WaitForFence(fence, 1);

            var readRange = new global::Silk.NET.Direct3D12.Range(0, buffer.Size);
            void* mapped = null;
            SilkMarshal.ThrowHResult(readback->Map(0, &readRange, &mapped));
            try
            {
                new ReadOnlySpan<byte>(mapped, destination.Length).CopyTo(destination);
            }
            finally
            {
                readback->Unmap(0, null);
            }
        }
        finally
        {
            if (submitted && fence != null && !IsFenceCompleted(fence, 1))
            {
                WaitForFence(fence, 1);
            }
            Release(ref fence);
            Release(ref commands);
            Release(ref allocator);
            Release(ref readback);
        }
    }
}

[SupportedOSPlatform("windows")]
internal sealed class D3D12SilkComputeBindingLayout(
    D3D12SilkGraphicsDevice device,
    SilkComputeBindingLayoutDescriptor descriptor)
    : SilkGraphicsResourceBase, ISilkComputeBindingLayout
{
    internal D3D12SilkGraphicsDevice Device { get; } = device;

    public SilkComputeBindingLayoutDescriptor Descriptor { get; } = descriptor;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfDisposed() => ThrowIfResourceDisposed();

    protected override void ReleaseNative() => Device.ReleaseDependentObject();
}

[SupportedOSPlatform("windows")]
internal sealed class D3D12SilkComputeShaderProgram(
    D3D12SilkGraphicsDevice device,
    D3D12SilkGraphicsShaderModule shader,
    D3D12SilkComputeBindingLayout layout,
    IDisposable[] resourceLeases)
    : SilkGraphicsResourceBase, ISilkComputeShaderProgram
{
    private IDisposable[]? _resourceLeases = resourceLeases;

    internal D3D12SilkGraphicsDevice Device { get; } = device;

    internal D3D12SilkGraphicsShaderModule Shader { get; } = shader;

    public ISilkComputeBindingLayout BindingLayout => layout;

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
internal sealed unsafe class D3D12SilkComputePipeline(
    D3D12SilkGraphicsDevice device,
    SilkComputePipelineDescriptor descriptor,
    ID3D12RootSignature* rootSignature,
    ID3D12PipelineState* pipeline,
    IDisposable programLease)
    : SilkGraphicsResourceBase, ISilkComputePipeline
{
    private ID3D12RootSignature* _rootSignature = rootSignature;
    private ID3D12PipelineState* _pipeline = pipeline;
    private IDisposable? _programLease = programLease;

    internal D3D12SilkGraphicsDevice Device { get; } = device;

    public SilkComputePipelineDescriptor Descriptor { get; } = descriptor;

    internal ID3D12RootSignature* RootSignature => _rootSignature;

    internal ID3D12PipelineState* Pipeline => _pipeline;

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
