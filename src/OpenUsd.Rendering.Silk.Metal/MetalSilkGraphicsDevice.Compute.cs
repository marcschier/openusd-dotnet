// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using SharpMetal.Foundation;
using SharpMetal.Metal;

namespace OpenUsd.Rendering.Silk.Metal;

[SupportedOSPlatform("macos")]
public sealed partial class MetalSilkGraphicsDevice
{
    /// <inheritdoc/>
    public ISilkComputeBindingLayout CreateComputeBindingLayout(
        SilkComputeBindingLayoutDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        descriptor.Validate();
        RegisterDependentObject();
        return new MetalSilkComputeBindingLayout(this, descriptor);
    }

    /// <inheritdoc/>
    public ISilkComputeShaderProgram CreateComputeShaderProgram(
        SilkComputeShaderProgramDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (descriptor.ComputeShader is not MetalSilkGraphicsShaderModule shader ||
            descriptor.BindingLayout is not MetalSilkComputeBindingLayout layout ||
            !ReferenceEquals(shader.Device, this) ||
            !ReferenceEquals(layout.Device, this))
        {
            throw new ArgumentException(
                "Compute program resources must belong to this Metal device.",
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
        return new MetalSilkComputeShaderProgram(
            this,
            shader,
            layout,
            leases);
    }

    /// <inheritdoc/>
    public ISilkComputePipeline CreateComputePipeline(
        SilkComputePipelineDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        descriptor.Validate();
        if (descriptor.Program is not MetalSilkComputeShaderProgram program ||
            !ReferenceEquals(program.Device, this))
        {
            throw new ArgumentException(
                "The compute program was not created by this Metal device.",
                nameof(descriptor));
        }
        program.ThrowIfDisposed();
        SilkCheckedShaderAssets.ValidatePinnedMetalLibrary();

        MTLLibrary library = default;
        MTLFunction function = default;
        MTLComputePipelineState pipeline = default;
        IDisposable? programLease = null;
        bool success = false;
        RegisterDependentObject();
        try
        {
            NSString path = Path.Combine(AppContext.BaseDirectory, "mesh.metallib");
            NSURL url = NSURL.FileURLWithPath(path);
            try
            {
                NSError error = default;
                library = _device.NewLibrary(url, ref error);
                if (library.NativePtr == 0)
                {
                    throw new InvalidOperationException(
                        "Could not load the pinned Metal shader library.");
                }
            }
            finally
            {
                url.Dispose();
                path.Dispose();
            }

            function = library.NewFunction(program.Shader.Descriptor.EntryPoint);
            if (function.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    "The pinned Metal shader library is missing the checked compute entry point.");
            }
            NSError pipelineError = default;
            pipeline = _device.NewComputePipelineState(function, ref pipelineError);
            if (pipeline.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    "Could not create the checked Metal compute pipeline.");
            }
            programLease = program.AcquireLease();
            success = true;
            return new MetalSilkComputePipeline(
                this,
                descriptor,
                library,
                function,
                pipeline,
                programLease);
        }
        finally
        {
            if (!success)
            {
                programLease?.Dispose();
                if (pipeline.NativePtr != 0)
                {
                    pipeline.Dispose();
                }
                if (function.NativePtr != 0)
                {
                    function.Dispose();
                }
                if (library.NativePtr != 0)
                {
                    library.Dispose();
                }
                ReleaseDependentObject();
            }
        }
    }

    internal unsafe void Readback(
        MetalSilkGraphicsBuffer buffer,
        Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        buffer.ThrowIfDisposed();
        WaitIdle();

        MTLBuffer readback = _device.NewBuffer(
            checked((ulong)buffer.Size),
            MTLResourceOptions.ResourceStorageModeShared);
        if (readback.NativePtr == 0)
        {
            throw new InvalidOperationException("Could not create a Metal readback buffer.");
        }
        MTLCommandBuffer commandBuffer = default;
        try
        {
            commandBuffer = _queue.CommandBuffer();
            if (commandBuffer.NativePtr == 0)
            {
                throw new InvalidOperationException("Could not create a Metal command buffer.");
            }
            MTLBlitCommandEncoder encoder = commandBuffer.BlitCommandEncoder();
            if (encoder.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    "Could not create a Metal blit command encoder.");
            }
            try
            {
                encoder.CopyFromBuffer(
                    buffer.Buffer,
                    0,
                    readback,
                    0,
                    checked((ulong)buffer.Size));
                encoder.EndEncoding();
            }
            finally
            {
                encoder.Dispose();
            }
            commandBuffer.Commit();
            commandBuffer.WaitUntilCompleted();
            new ReadOnlySpan<byte>(
                (void*)readback.Contents,
                destination.Length).CopyTo(destination);
        }
        finally
        {
            if (commandBuffer.NativePtr != 0)
            {
                commandBuffer.Dispose();
            }
            readback.Dispose();
        }
    }
}

[SupportedOSPlatform("macos")]
internal sealed class MetalSilkComputeBindingLayout(
    MetalSilkGraphicsDevice device,
    SilkComputeBindingLayoutDescriptor descriptor)
    : SilkGraphicsResourceBase, ISilkComputeBindingLayout
{
    internal MetalSilkGraphicsDevice Device { get; } = device;

    public SilkComputeBindingLayoutDescriptor Descriptor { get; } = descriptor;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfDisposed() => ThrowIfResourceDisposed();

    protected override void ReleaseNative() => Device.ReleaseDependentObject();
}

[SupportedOSPlatform("macos")]
internal sealed class MetalSilkComputeShaderProgram(
    MetalSilkGraphicsDevice device,
    MetalSilkGraphicsShaderModule shader,
    MetalSilkComputeBindingLayout layout,
    IDisposable[] resourceLeases)
    : SilkGraphicsResourceBase, ISilkComputeShaderProgram
{
    private IDisposable[]? _resourceLeases = resourceLeases;

    internal MetalSilkGraphicsDevice Device { get; } = device;

    internal MetalSilkGraphicsShaderModule Shader { get; } = shader;

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

[SupportedOSPlatform("macos")]
internal sealed class MetalSilkComputePipeline(
    MetalSilkGraphicsDevice device,
    SilkComputePipelineDescriptor descriptor,
    MTLLibrary library,
    MTLFunction function,
    MTLComputePipelineState pipeline,
    IDisposable programLease)
    : SilkGraphicsResourceBase, ISilkComputePipeline
{
    private MTLLibrary _library = library;
    private MTLFunction _function = function;
    private MTLComputePipelineState _pipeline = pipeline;
    private IDisposable? _programLease = programLease;

    internal MetalSilkGraphicsDevice Device { get; } = device;

    public SilkComputePipelineDescriptor Descriptor { get; } = descriptor;

    internal MTLComputePipelineState Pipeline => _pipeline;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfDisposed() => ThrowIfResourceDisposed();

    protected override void ReleaseNative()
    {
        _pipeline.Dispose();
        _function.Dispose();
        _library.Dispose();
        Interlocked.Exchange(ref _programLease, null)?.Dispose();
        Device.ReleaseDependentObject();
    }
}
