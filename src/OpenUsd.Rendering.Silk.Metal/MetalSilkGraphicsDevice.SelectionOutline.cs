// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using SharpMetal.Foundation;
using SharpMetal.Metal;

namespace OpenUsd.Rendering.Silk.Metal;

[SupportedOSPlatform("macos")]
public sealed partial class MetalSilkGraphicsDevice
{
    private readonly MetalSelectionOutlineDeviceGeneration
        _selectionOutlineDeviceGeneration = new();
    private long _selectionMaskPipelineCreationCount;
    private long _selectionOutlinePipelineCreationCount;
    private long _selectionOutlineBindingCreationCount;

    /// <inheritdoc/>
    public ulong SelectionOutlineDeviceGeneration =>
        _selectionOutlineDeviceGeneration.Current;

    /// <inheritdoc/>
    public SilkSelectionOutlineCapabilities SelectionOutlineCapabilities =>
        SilkSelectionOutlineCapabilities.Full;

    /// <inheritdoc/>
    public ISilkSelectionMaskGraphicsPipeline CreateSelectionMaskGraphicsPipeline(
        SilkSelectionMaskPipelineDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        descriptor.Validate();
        ValidateMetalLibraryFormat(descriptor.VertexShader.Format);

        MTLLibrary library = default;
        MTLFunction vertexFunction = default;
        MTLFunction fragmentFunction = default;
        MTLRenderPipelineState pipeline = default;
        MTLDepthStencilState depthState = default;
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
                    "selection-mask entry point.");
            }

            var vertexDescriptor = new MTLVertexDescriptor();
            var pipelineDescriptor = new MTLRenderPipelineDescriptor();
            var depthDescriptor = new MTLDepthStencilDescriptor();
            try
            {
                ConfigureMeshVertexDescriptor(vertexDescriptor, descriptor.VertexLayout.Stride);
                pipelineDescriptor.VertexFunction = vertexFunction;
                pipelineDescriptor.FragmentFunction = fragmentFunction;
                pipelineDescriptor.VertexDescriptor = vertexDescriptor;
                pipelineDescriptor.InputPrimitiveTopology =
                    descriptor.PrimitiveTopology switch
                    {
                        SilkSelectionMaskPrimitiveTopology.LineList =>
                            MTLPrimitiveTopologyClass.Line,
                        SilkSelectionMaskPrimitiveTopology.PointList =>
                            MTLPrimitiveTopologyClass.Point,
                        _ => MTLPrimitiveTopologyClass.Triangle
                    };
                pipelineDescriptor.RasterSampleCount = descriptor.SampleCount;
                MTLRenderPipelineColorAttachmentDescriptor color =
                    pipelineDescriptor.ColorAttachments.Object(0);
                color.PixelFormat = GetNativeFormat(descriptor.ColorFormat);
                pipelineDescriptor.DepthAttachmentPixelFormat =
                    MTLPixelFormat.Depth32Float;

                NSError pipelineError = default;
                pipeline = _device.NewRenderPipelineState(
                    pipelineDescriptor,
                    ref pipelineError);
                if (pipeline.NativePtr == 0)
                {
                    throw new InvalidOperationException(
                        "Could not create the checked Metal selection-mask pipeline.");
                }

                // The x-ray mask rasterizes the whole selected silhouette,
                // including the part behind an occluder, so its state compares
                // Always; the composite's own depth comparison separates
                // visible from occluded.
                depthDescriptor.DepthCompareFunction = descriptor.DepthTestEnabled
                    ? MTLCompareFunction.LessEqual
                    : MTLCompareFunction.Always;
                depthDescriptor.IsDepthWriteEnabled = false;
                depthState = _device.NewDepthStencilState(depthDescriptor);
                if (depthState.NativePtr == 0)
                {
                    throw new InvalidOperationException(
                        "Could not create the read-only Metal selection depth state.");
                }
            }
            finally
            {
                depthDescriptor.Dispose();
                pipelineDescriptor.Dispose();
                vertexDescriptor.Dispose();
            }

            success = true;
            _ = Interlocked.Increment(ref _selectionMaskPipelineCreationCount);
            return new MetalSilkSelectionMaskGraphicsPipeline(
                this,
                descriptor,
                SelectionOutlineDeviceGeneration,
                library,
                vertexFunction,
                fragmentFunction,
                pipeline,
                depthState);
        }
        finally
        {
            if (!success)
            {
                DisposePipelineObjects(
                    library,
                    vertexFunction,
                    fragmentFunction,
                    pipeline,
                    depthState);
                ReleaseDependentObject();
            }
        }
    }

    /// <inheritdoc/>
    public ISilkSelectionOutlineGraphicsPipeline
        CreateSelectionOutlineGraphicsPipeline(
            SilkSelectionOutlinePipelineDescriptor descriptor)
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
                    "selection-outline entry point.");
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
                color.IsBlendingEnabled = true;
                color.RgbBlendOperation = MTLBlendOperation.Add;
                color.AlphaBlendOperation = MTLBlendOperation.Add;
                color.SourceRGBBlendFactor = MTLBlendFactor.SourceAlpha;
                color.DestinationRGBBlendFactor =
                    MTLBlendFactor.OneMinusSourceAlpha;
                color.SourceAlphaBlendFactor = MTLBlendFactor.One;
                color.DestinationAlphaBlendFactor =
                    MTLBlendFactor.OneMinusSourceAlpha;

                NSError pipelineError = default;
                pipeline = _device.NewRenderPipelineState(
                    pipelineDescriptor,
                    ref pipelineError);
                if (pipeline.NativePtr == 0)
                {
                    throw new InvalidOperationException(
                        "Could not create the checked Metal selection-outline pipeline.");
                }
            }
            finally
            {
                pipelineDescriptor.Dispose();
            }

            success = true;
            _ = Interlocked.Increment(ref _selectionOutlinePipelineCreationCount);
            return new MetalSilkSelectionOutlineGraphicsPipeline(
                this,
                descriptor,
                SelectionOutlineDeviceGeneration,
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
    public ISilkSelectionOutlineBinding CreateSelectionOutlineBinding(
        SilkSelectionOutlineBindingDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        descriptor.Validate();
        if (descriptor.MaskTexture is not MetalSilkGraphicsTexture mask ||
            descriptor.VisibleDepthTexture is not MetalSilkGraphicsTexture depth ||
            descriptor.Sampler is not MetalSilkGraphicsSampler sampler ||
            descriptor.Parameters is not MetalSilkGraphicsBuffer parameters ||
            !ReferenceEquals(mask.Device, this) ||
            !ReferenceEquals(depth.Device, this) ||
            !ReferenceEquals(sampler.Device, this) ||
            !ReferenceEquals(parameters.Device, this))
        {
            throw new ArgumentException(
                "Selection-outline binding resources must belong to this Metal device.",
                nameof(descriptor));
        }
        mask.ThrowIfDisposed();
        depth.ThrowIfDisposed();
        sampler.ThrowIfDisposed();
        parameters.ThrowIfDisposed();

        var leases = new List<IDisposable>(4);
        bool success = false;
        RegisterDependentObject();
        try
        {
            leases.Add(mask.AcquireLease());
            leases.Add(depth.AcquireLease());
            leases.Add(sampler.AcquireLease());
            leases.Add(parameters.AcquireLease());
            success = true;
            _ = Interlocked.Increment(ref _selectionOutlineBindingCreationCount);
            return new MetalSilkSelectionOutlineBinding(
                this,
                descriptor,
                SelectionOutlineDeviceGeneration,
                mask,
                depth,
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

    internal long SelectionMaskPipelineCreationCount =>
        Volatile.Read(ref _selectionMaskPipelineCreationCount);

    internal long SelectionOutlinePipelineCreationCount =>
        Volatile.Read(ref _selectionOutlinePipelineCreationCount);

    internal long SelectionOutlineBindingCreationCount =>
        Volatile.Read(ref _selectionOutlineBindingCreationCount);

    internal void NotifySelectionOutlineCommandBufferFailure() =>
        _ = _selectionOutlineDeviceGeneration.Invalidate();

    private MTLLibrary LoadPinnedMetalLibrary()
    {
        SilkCheckedShaderAssets.ValidatePinnedMetalLibrary();
        NSString path = Path.Combine(AppContext.BaseDirectory, "mesh.metallib");
        NSURL url = NSURL.FileURLWithPath(path);
        try
        {
            NSError error = default;
            MTLLibrary library = _device.NewLibrary(url, ref error);
            if (library.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    "Could not load the pinned Metal shader library.");
            }
            return library;
        }
        finally
        {
            url.Dispose();
            path.Dispose();
        }
    }

    private static void ValidateMetalLibraryFormat(SilkShaderBinaryFormat format)
    {
        if (format != SilkShaderBinaryFormat.MetalLibrary)
        {
            throw new ArgumentException(
                "Metal selection pipelines require the pinned Metal library.");
        }
    }

    private static void ConfigureMeshVertexDescriptor(
        MTLVertexDescriptor vertexDescriptor,
        uint stride)
    {
        MTLVertexAttributeDescriptor position =
            vertexDescriptor.Attributes.Object(0);
        position.Format = MTLVertexFormat.Float3;
        position.Offset = 0;
        position.BufferIndex = 30;
        MTLVertexAttributeDescriptor normal =
            vertexDescriptor.Attributes.Object(1);
        normal.Format = MTLVertexFormat.Float3;
        normal.Offset = 12;
        normal.BufferIndex = 30;
        MTLVertexBufferLayoutDescriptor layout =
            vertexDescriptor.Layouts.Object(30);
        layout.Stride = stride;
        layout.StepFunction = MTLVertexStepFunction.PerVertex;
    }

    private static void DisposePipelineObjects(
        MTLLibrary library,
        MTLFunction vertexFunction,
        MTLFunction fragmentFunction,
        MTLRenderPipelineState pipeline,
        MTLDepthStencilState depthState = default)
    {
        if (depthState.NativePtr != 0)
        {
            depthState.Dispose();
        }
        if (pipeline.NativePtr != 0)
        {
            pipeline.Dispose();
        }
        if (fragmentFunction.NativePtr != 0)
        {
            fragmentFunction.Dispose();
        }
        if (vertexFunction.NativePtr != 0)
        {
            vertexFunction.Dispose();
        }
        if (library.NativePtr != 0)
        {
            library.Dispose();
        }
    }
}

internal sealed class MetalSelectionOutlineDeviceGeneration
{
    private long _value = 1;

    internal ulong Current => checked((ulong)Volatile.Read(ref _value));

    internal ulong Invalidate() =>
        checked((ulong)Interlocked.Increment(ref _value));
}

[SupportedOSPlatform("macos")]
internal sealed class MetalSilkSelectionMaskGraphicsPipeline(
    MetalSilkGraphicsDevice device,
    SilkSelectionMaskPipelineDescriptor descriptor,
    ulong generation,
    MTLLibrary library,
    MTLFunction vertexFunction,
    MTLFunction fragmentFunction,
    MTLRenderPipelineState pipeline,
    MTLDepthStencilState depthState)
    : SilkGraphicsResourceBase, ISilkSelectionMaskGraphicsPipeline
{
    private MTLLibrary _library = library;
    private MTLFunction _vertexFunction = vertexFunction;
    private MTLFunction _fragmentFunction = fragmentFunction;
    private MTLRenderPipelineState _pipeline = pipeline;
    private MTLDepthStencilState _depthState = depthState;

    internal MetalSilkGraphicsDevice Device { get; } = device;

    internal ulong Generation { get; } = generation;

    public SilkSelectionMaskPipelineDescriptor Descriptor { get; } = descriptor;

    internal MTLRenderPipelineState Pipeline => _pipeline;

    internal MTLDepthStencilState DepthState => _depthState;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfUnavailable()
    {
        ThrowIfResourceDisposed();
        if (Generation != Device.SelectionOutlineDeviceGeneration)
        {
            throw new InvalidOperationException(
                "The Metal selection-mask pipeline belongs to an invalid generation.");
        }
    }

    protected override void ReleaseNative()
    {
        _depthState.Dispose();
        _pipeline.Dispose();
        _fragmentFunction.Dispose();
        _vertexFunction.Dispose();
        _library.Dispose();
        Device.ReleaseDependentObject();
    }
}

[SupportedOSPlatform("macos")]
internal sealed class MetalSilkSelectionOutlineGraphicsPipeline(
    MetalSilkGraphicsDevice device,
    SilkSelectionOutlinePipelineDescriptor descriptor,
    ulong generation,
    MTLLibrary library,
    MTLFunction vertexFunction,
    MTLFunction fragmentFunction,
    MTLRenderPipelineState pipeline)
    : SilkGraphicsResourceBase, ISilkSelectionOutlineGraphicsPipeline
{
    private MTLLibrary _library = library;
    private MTLFunction _vertexFunction = vertexFunction;
    private MTLFunction _fragmentFunction = fragmentFunction;
    private MTLRenderPipelineState _pipeline = pipeline;

    internal MetalSilkGraphicsDevice Device { get; } = device;

    internal ulong Generation { get; } = generation;

    public SilkSelectionOutlinePipelineDescriptor Descriptor { get; } = descriptor;

    internal MTLRenderPipelineState Pipeline => _pipeline;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfUnavailable()
    {
        ThrowIfResourceDisposed();
        if (Generation != Device.SelectionOutlineDeviceGeneration)
        {
            throw new InvalidOperationException(
                "The Metal selection-outline pipeline belongs to an invalid generation.");
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
internal sealed class MetalSilkSelectionOutlineBinding(
    MetalSilkGraphicsDevice device,
    SilkSelectionOutlineBindingDescriptor descriptor,
    ulong generation,
    MetalSilkGraphicsTexture mask,
    MetalSilkGraphicsTexture depth,
    MetalSilkGraphicsSampler sampler,
    MetalSilkGraphicsBuffer parameters,
    IDisposable[] leases)
    : SilkGraphicsResourceBase, ISilkSelectionOutlineBinding
{
    private IDisposable[]? _leases = leases;

    internal MetalSilkGraphicsDevice Device { get; } = device;

    internal ulong Generation { get; } = generation;

    public SilkSelectionOutlineBindingDescriptor Descriptor { get; } = descriptor;

    internal MetalSilkGraphicsTexture Mask { get; } = mask;

    internal MetalSilkGraphicsTexture Depth { get; } = depth;

    internal MetalSilkGraphicsSampler Sampler { get; } = sampler;

    internal MetalSilkGraphicsBuffer Parameters { get; } = parameters;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfUnavailable()
    {
        ThrowIfResourceDisposed();
        if (Generation != Device.SelectionOutlineDeviceGeneration)
        {
            throw new InvalidOperationException(
                "The Metal selection-outline binding belongs to an invalid generation.");
        }
    }

    protected override void ReleaseNative()
    {
        IDisposable[]? leases = Interlocked.Exchange(ref _leases, null);
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
