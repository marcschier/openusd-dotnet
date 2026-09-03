// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using SharpMetal.Foundation;
using SharpMetal.Metal;

namespace OpenUsd.Rendering.Silk.Metal;

[SupportedOSPlatform("macos")]
public sealed partial class MetalSilkGraphicsDevice
    : ISilkDisplayTransformGraphicsDevice
{
    private long _displayTransformPipelineCreationCount;
    private long _displayTransformBindingCreationCount;

    /// <inheritdoc/>
    public ulong DisplayTransformDeviceGeneration =>
        SelectionOutlineDeviceGeneration;

    /// <inheritdoc/>
    public ISilkDisplayTransformGraphicsPipeline CreateDisplayTransformGraphicsPipeline(
        SilkDisplayTransformPipelineDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        descriptor.Validate();
        ValidateMetalLibraryFormat(descriptor.VertexShader.Format);

        MTLLibrary library = default;
        MTLFunction vertexFunction = default;
        MTLFunction fragmentFunction = default;
        MTLRenderPipelineState pipeline = default;
        bool success = false;
        RegisterDependentObject();
        try
        {
            library = LoadPinnedMetalLibrary();
            vertexFunction = library.NewFunction(descriptor.VertexShader.EntryPoint);
            fragmentFunction = library.NewFunction(descriptor.FragmentShader.EntryPoint);
            if (vertexFunction.NativePtr == 0 || fragmentFunction.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    "The pinned Metal shader library is missing a checked " +
                    "display-transform entry point.");
            }

            var pipelineDescriptor = new MTLRenderPipelineDescriptor();
            try
            {
                pipelineDescriptor.VertexFunction = vertexFunction;
                pipelineDescriptor.FragmentFunction = fragmentFunction;
                pipelineDescriptor.InputPrimitiveTopology =
                    MTLPrimitiveTopologyClass.Triangle;
                pipelineDescriptor.RasterSampleCount = descriptor.SampleCount;
                MTLRenderPipelineColorAttachmentDescriptor color =
                    pipelineDescriptor.ColorAttachments.Object(0);
                color.PixelFormat = GetNativeFormat(descriptor.ColorFormat);
                color.IsBlendingEnabled = false;

                NSError pipelineError = default;
                pipeline = _device.NewRenderPipelineState(
                    pipelineDescriptor,
                    ref pipelineError);
                if (pipeline.NativePtr == 0)
                {
                    throw new InvalidOperationException(
                        "Could not create the checked Metal display-transform pipeline.");
                }
            }
            finally
            {
                pipelineDescriptor.Dispose();
            }

            success = true;
            _ = Interlocked.Increment(ref _displayTransformPipelineCreationCount);
            return new MetalSilkDisplayTransformGraphicsPipeline(
                this,
                descriptor,
                DisplayTransformDeviceGeneration,
                library,
                vertexFunction,
                fragmentFunction,
                pipeline);
        }
        finally
        {
            if (!success)
            {
                DisposePipelineObjects(
                    library,
                    vertexFunction,
                    fragmentFunction,
                    pipeline);
                ReleaseDependentObject();
            }
        }
    }

    /// <inheritdoc/>
    public ISilkDisplayTransformBinding CreateDisplayTransformBinding(
        SilkDisplayTransformBindingDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        descriptor.Validate();
        if (descriptor.SceneColorTexture is not MetalSilkGraphicsTexture sceneColor ||
            descriptor.LatticeTexture is not MetalSilkGraphicsTexture lattice ||
            descriptor.Sampler is not MetalSilkGraphicsSampler sampler ||
            descriptor.Parameters is not MetalSilkGraphicsBuffer parameters ||
            !ReferenceEquals(sceneColor.Device, this) ||
            !ReferenceEquals(lattice.Device, this) ||
            !ReferenceEquals(sampler.Device, this) ||
            !ReferenceEquals(parameters.Device, this))
        {
            throw new ArgumentException(
                "Display-transform binding resources must belong to this Metal device.",
                nameof(descriptor));
        }
        sceneColor.ThrowIfDisposed();
        lattice.ThrowIfDisposed();
        sampler.ThrowIfDisposed();
        parameters.ThrowIfDisposed();

        var leases = new List<IDisposable>(4);
        bool success = false;
        RegisterDependentObject();
        try
        {
            leases.Add(sceneColor.AcquireLease());
            leases.Add(lattice.AcquireLease());
            leases.Add(sampler.AcquireLease());
            leases.Add(parameters.AcquireLease());
            success = true;
            _ = Interlocked.Increment(ref _displayTransformBindingCreationCount);
            return new MetalSilkDisplayTransformBinding(
                this,
                descriptor,
                DisplayTransformDeviceGeneration,
                sceneColor,
                lattice,
                sampler,
                parameters,
                [.. leases]);
        }
        finally
        {
            if (!success)
            {
                foreach (IDisposable lease in leases)
                {
                    lease.Dispose();
                }
                ReleaseDependentObject();
            }
        }
    }

    internal MetalDisplayTransformNativeStatistics
        DisplayTransformNativeStatisticsForTesting =>
        new(
            Volatile.Read(ref _displayTransformPipelineCreationCount),
            Volatile.Read(ref _displayTransformBindingCreationCount),
            DisplayTransformDeviceGeneration);
}

internal readonly record struct MetalDisplayTransformNativeStatistics(
    long PipelineCreations,
    long BindingCreations,
    ulong DeviceGeneration);

[SupportedOSPlatform("macos")]
internal sealed class MetalSilkDisplayTransformGraphicsPipeline(
    MetalSilkGraphicsDevice device,
    SilkDisplayTransformPipelineDescriptor descriptor,
    ulong generation,
    MTLLibrary library,
    MTLFunction vertexFunction,
    MTLFunction fragmentFunction,
    MTLRenderPipelineState pipeline)
    : SilkGraphicsResourceBase, ISilkDisplayTransformGraphicsPipeline
{
    private MTLLibrary _library = library;
    private MTLFunction _vertexFunction = vertexFunction;
    private MTLFunction _fragmentFunction = fragmentFunction;
    private MTLRenderPipelineState _pipeline = pipeline;

    internal MetalSilkGraphicsDevice Device { get; } = device;

    internal ulong Generation { get; } = generation;

    public SilkDisplayTransformPipelineDescriptor Descriptor { get; } = descriptor;

    internal MTLRenderPipelineState Pipeline => _pipeline;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfUnavailable()
    {
        ThrowIfResourceDisposed();
        if (Generation != Device.DisplayTransformDeviceGeneration)
        {
            throw new InvalidOperationException(
                "The Metal display-transform pipeline belongs to an invalid generation.");
        }
    }

    protected override void ReleaseNative()
    {
        _pipeline.Dispose();
        _fragmentFunction.Dispose();
        _vertexFunction.Dispose();
        _library.Dispose();
        Device.ReleaseDependentObject();
    }
}

[SupportedOSPlatform("macos")]
internal sealed class MetalSilkDisplayTransformBinding(
    MetalSilkGraphicsDevice device,
    SilkDisplayTransformBindingDescriptor descriptor,
    ulong generation,
    MetalSilkGraphicsTexture sceneColor,
    MetalSilkGraphicsTexture lattice,
    MetalSilkGraphicsSampler sampler,
    MetalSilkGraphicsBuffer parameters,
    IDisposable[] leases)
    : SilkGraphicsResourceBase, ISilkDisplayTransformBinding
{
    private IDisposable[]? _leases = leases;

    internal MetalSilkGraphicsDevice Device { get; } = device;

    internal ulong Generation { get; } = generation;

    public SilkDisplayTransformBindingDescriptor Descriptor { get; } = descriptor;

    internal MetalSilkGraphicsTexture SceneColor { get; } = sceneColor;

    internal MetalSilkGraphicsTexture Lattice { get; } = lattice;

    internal MetalSilkGraphicsSampler Sampler { get; } = sampler;

    internal MetalSilkGraphicsBuffer Parameters { get; } = parameters;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfUnavailable()
    {
        ThrowIfResourceDisposed();
        if (Generation != Device.DisplayTransformDeviceGeneration)
        {
            throw new InvalidOperationException(
                "The Metal display-transform binding belongs to an invalid generation.");
        }
    }

    protected override void ReleaseNative()
    {
        IDisposable[]? captured = Interlocked.Exchange(ref _leases, null);
        if (captured is not null)
        {
            foreach (IDisposable lease in captured)
            {
                lease.Dispose();
            }
        }
        Device.ReleaseDependentObject();
    }
}
