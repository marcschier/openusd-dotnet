// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using SharpMetal.Foundation;
using SharpMetal.Metal;

namespace OpenUsd.Rendering.Silk.Metal;

[SupportedOSPlatform("macos")]
public sealed partial class MetalSilkGraphicsDevice
{
    /// <inheritdoc/>
    public ISilkGraphicsShaderModule CreateShaderModule(
        SilkShaderModuleDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        descriptor.Validate();
        if (descriptor.Format != SilkShaderBinaryFormat.MetalLibrary)
        {
            throw new ArgumentException(
                "Metal shader modules require a pinned Metal library.",
                nameof(descriptor));
        }
        RegisterDependentObject();
        return new MetalSilkGraphicsShaderModule(
            this,
            descriptor with { Code = descriptor.Code.ToArray() });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Metal has no descriptor set layout object: resources bind by argument index at
    /// encode time. The layout therefore only carries and validates the descriptor,
    /// including its material slots, which the encoder consumes when binding.
    /// </remarks>
    public ISilkGraphicsBindingLayout CreateBindingLayout(
        SilkBindingLayoutDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        descriptor.Validate();
        RegisterDependentObject();
        return new MetalSilkGraphicsBindingLayout(this, descriptor);
    }

    /// <inheritdoc/>
    public ISilkGraphicsShaderProgram CreateShaderProgram(
        SilkShaderProgramDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (descriptor.VertexShader is not MetalSilkGraphicsShaderModule vertex ||
            descriptor.FragmentShader is not MetalSilkGraphicsShaderModule fragment ||
            descriptor.BindingLayout is not MetalSilkGraphicsBindingLayout layout ||
            !ReferenceEquals(vertex.Device, this) ||
            !ReferenceEquals(fragment.Device, this) ||
            !ReferenceEquals(layout.Device, this))
        {
            throw new ArgumentException(
                "Shader program resources must belong to this Metal device.",
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
        return new MetalSilkGraphicsShaderProgram(
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
        ObjectDisposedException.ThrowIf(_disposed, this);
        descriptor.Validate();
        if (descriptor.Program is not MetalSilkGraphicsShaderProgram program ||
            !ReferenceEquals(program.Device, this))
        {
            throw new ArgumentException(
                "The shader program was not created by this Metal device.",
                nameof(descriptor));
        }
        program.ThrowIfDisposed();
        SilkCheckedShaderAssets.ValidatePinnedMetalLibrary();

        MTLLibrary library = default;
        MTLFunction vertexFunction = default;
        MTLFunction fragmentFunction = default;
        MTLRenderPipelineState pipeline = default;
        MTLDepthStencilState depthState = default;
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

            vertexFunction = library.NewFunction(program.Vertex.Descriptor.EntryPoint);
            fragmentFunction = library.NewFunction(program.Fragment.Descriptor.EntryPoint);
            if (vertexFunction.NativePtr == 0 || fragmentFunction.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    "The pinned Metal shader library is missing a checked mesh entry point.");
            }

            var vertexDescriptor = new MTLVertexDescriptor();
            var pipelineDescriptor = new MTLRenderPipelineDescriptor();
            var depthDescriptor = new MTLDepthStencilDescriptor();
            try
            {
                foreach (SilkVertexAttributeDescriptor attribute in
                    descriptor.VertexLayout.Attributes)
                {
                    MTLVertexAttributeDescriptor nativeAttribute =
                        vertexDescriptor.Attributes.Object(attribute.Location);
                    nativeAttribute.Format = GetFormat(attribute.Format);
                    nativeAttribute.Offset = attribute.Offset;
                    nativeAttribute.BufferIndex = 30;
                }
                MTLVertexBufferLayoutDescriptor layout =
                    vertexDescriptor.Layouts.Object(30);
                layout.Stride = descriptor.VertexLayout.Stride;
                layout.StepFunction = MTLVertexStepFunction.PerVertex;

                pipelineDescriptor.VertexFunction = vertexFunction;
                pipelineDescriptor.FragmentFunction = fragmentFunction;
                pipelineDescriptor.VertexDescriptor = vertexDescriptor;
                pipelineDescriptor.InputPrimitiveTopology =
                    descriptor.TopologyKind == SilkTopologyKind.LineList
                        ? MTLPrimitiveTopologyClass.Line
                        : MTLPrimitiveTopologyClass.Triangle;
                MTLRenderPipelineColorAttachmentDescriptor colorAttachment =
                    pipelineDescriptor.ColorAttachments.Object(0);
                colorAttachment.PixelFormat = MTLPixelFormat.RGBA8Unorm;
                pipelineDescriptor.DepthAttachmentPixelFormat =
                    MTLPixelFormat.Depth32Float;

                NSError pipelineError = default;
                pipeline = _device.NewRenderPipelineState(
                    pipelineDescriptor,
                    ref pipelineError);
                if (pipeline.NativePtr == 0)
                {
                    throw new InvalidOperationException(
                        "Could not create the checked Metal graphics pipeline.");
                }

                depthDescriptor.DepthCompareFunction = MTLCompareFunction.LessEqual;
                depthDescriptor.IsDepthWriteEnabled = true;
                depthState = _device.NewDepthStencilState(depthDescriptor);
                if (depthState.NativePtr == 0)
                {
                    throw new InvalidOperationException(
                        "Could not create the Metal depth-stencil state.");
                }
            }
            finally
            {
                depthDescriptor.Dispose();
                pipelineDescriptor.Dispose();
                vertexDescriptor.Dispose();
            }

            programLease = program.AcquireLease();
            success = true;
            return new MetalSilkGraphicsPipeline(
                this,
                descriptor,
                library,
                vertexFunction,
                fragmentFunction,
                pipeline,
                depthState,
                programLease);
        }
        finally
        {
            if (!success)
            {
                programLease?.Dispose();
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
                ReleaseDependentObject();
            }
        }
    }

    private static MTLVertexFormat GetFormat(SilkVertexFormat format) =>
        format switch
        {
            SilkVertexFormat.Float2 => MTLVertexFormat.Float2,
            SilkVertexFormat.Float3 => MTLVertexFormat.Float3,
            SilkVertexFormat.Float4 => MTLVertexFormat.Float4,
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
}

[SupportedOSPlatform("macos")]
internal sealed class MetalSilkGraphicsShaderModule(
    MetalSilkGraphicsDevice device,
    SilkShaderModuleDescriptor descriptor)
    : SilkGraphicsResourceBase, ISilkGraphicsShaderModule
{
    internal MetalSilkGraphicsDevice Device { get; } = device;

    public SilkShaderModuleDescriptor Descriptor { get; } = descriptor;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfDisposed() => ThrowIfResourceDisposed();

    protected override void ReleaseNative() => Device.ReleaseDependentObject();
}

[SupportedOSPlatform("macos")]
internal sealed class MetalSilkGraphicsBindingLayout(
    MetalSilkGraphicsDevice device,
    SilkBindingLayoutDescriptor descriptor)
    : SilkGraphicsResourceBase, ISilkGraphicsBindingLayout
{
    internal MetalSilkGraphicsDevice Device { get; } = device;

    public SilkBindingLayoutDescriptor Descriptor { get; } = descriptor;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfDisposed() => ThrowIfResourceDisposed();

    protected override void ReleaseNative() => Device.ReleaseDependentObject();
}

[SupportedOSPlatform("macos")]
internal sealed class MetalSilkGraphicsShaderProgram(
    MetalSilkGraphicsDevice device,
    MetalSilkGraphicsShaderModule vertex,
    MetalSilkGraphicsShaderModule fragment,
    MetalSilkGraphicsBindingLayout layout,
    IDisposable[] resourceLeases)
    : SilkGraphicsResourceBase, ISilkGraphicsShaderProgram
{
    private IDisposable[]? _resourceLeases = resourceLeases;

    internal MetalSilkGraphicsDevice Device { get; } = device;

    internal MetalSilkGraphicsShaderModule Vertex { get; } = vertex;

    internal MetalSilkGraphicsShaderModule Fragment { get; } = fragment;

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

[SupportedOSPlatform("macos")]
internal sealed class MetalSilkGraphicsPipeline(
    MetalSilkGraphicsDevice device,
    SilkGraphicsPipelineDescriptor descriptor,
    MTLLibrary library,
    MTLFunction vertexFunction,
    MTLFunction fragmentFunction,
    MTLRenderPipelineState pipeline,
    MTLDepthStencilState depthState,
    IDisposable programLease)
    : SilkGraphicsResourceBase, ISilkGraphicsPipeline
{
    private MTLLibrary _library = library;
    private MTLFunction _vertexFunction = vertexFunction;
    private MTLFunction _fragmentFunction = fragmentFunction;
    private MTLRenderPipelineState _pipeline = pipeline;
    private MTLDepthStencilState _depthState = depthState;
    private IDisposable? _programLease = programLease;

    internal MetalSilkGraphicsDevice Device { get; } = device;

    public SilkGraphicsPipelineDescriptor Descriptor { get; } = descriptor;

    internal MTLRenderPipelineState Pipeline => _pipeline;

    internal MTLDepthStencilState DepthState => _depthState;

    /// <summary>
    /// Gets the binding layout this pipeline was created from, so a submission can
    /// map a material slot to its Metal argument index.
    /// </summary>
    internal SilkBindingLayoutDescriptor BindingLayout { get; } =
        descriptor.Program.BindingLayout.Descriptor;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfDisposed() => ThrowIfResourceDisposed();

    protected override void ReleaseNative()
    {
        _depthState.Dispose();
        _pipeline.Dispose();
        _fragmentFunction.Dispose();
        _vertexFunction.Dispose();
        _library.Dispose();
        Interlocked.Exchange(ref _programLease, null)?.Dispose();
        Device.ReleaseDependentObject();
    }
}
