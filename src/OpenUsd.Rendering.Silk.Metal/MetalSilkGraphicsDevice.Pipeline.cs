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

        MTLLibrary vertexLibrary = default;
        MTLLibrary fragmentLibrary = default;
        MTLFunction vertexFunction = default;
        MTLFunction fragmentFunction = default;
        MTLRenderPipelineState pipeline = default;
        MTLDepthStencilState depthState = default;
        IDisposable? programLease = null;
        bool success = false;
        RegisterDependentObject();
        try
        {
            vertexLibrary = LoadGraphicsLibrary(program.Vertex.Descriptor);
            vertexFunction = vertexLibrary.NewFunction(program.Vertex.Descriptor.EntryPoint);
            if (vertexFunction.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    $"The Metal vertex shader library is missing entry point '{program.Vertex.Descriptor.EntryPoint}'.");
            }
            fragmentLibrary = LoadGraphicsLibrary(program.Fragment.Descriptor);
            fragmentFunction = fragmentLibrary.NewFunction(program.Fragment.Descriptor.EntryPoint);
            if (fragmentFunction.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    $"The Metal fragment shader library is missing entry point '{program.Fragment.Descriptor.EntryPoint}'.");
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
                    descriptor.TopologyKind switch
                    {
                        SilkTopologyKind.LineList => MTLPrimitiveTopologyClass.Line,
                        SilkTopologyKind.PointList => MTLPrimitiveTopologyClass.Point,
                        _ => MTLPrimitiveTopologyClass.Triangle
                    };
                MTLRenderPipelineColorAttachmentDescriptor colorAttachment =
                    pipelineDescriptor.ColorAttachments.Object(0);
                colorAttachment.PixelFormat =
                    GetNativeFormat(descriptor.ColorFormat);
                colorAttachment.IsBlendingEnabled =
                    descriptor.BlendMode == SilkBlendMode.StraightAlphaOver;
                colorAttachment.RgbBlendOperation = MTLBlendOperation.Add;
                colorAttachment.AlphaBlendOperation = MTLBlendOperation.Add;
                colorAttachment.SourceRGBBlendFactor = MTLBlendFactor.SourceAlpha;
                colorAttachment.DestinationRGBBlendFactor =
                    MTLBlendFactor.OneMinusSourceAlpha;
                colorAttachment.SourceAlphaBlendFactor = MTLBlendFactor.One;
                colorAttachment.DestinationAlphaBlendFactor =
                    MTLBlendFactor.OneMinusSourceAlpha;
                pipelineDescriptor.DepthAttachmentPixelFormat =
                    MTLPixelFormat.Depth32Float;

                NSError pipelineError = default;
                pipeline = _device.NewRenderPipelineState(
                    pipelineDescriptor,
                    ref pipelineError);
                if (pipeline.NativePtr == 0)
                {
                    throw CreateMetalFailure(
                        "Could not create the checked Metal graphics pipeline.", pipelineError);
                }

                depthDescriptor.DepthCompareFunction = MTLCompareFunction.LessEqual;
                depthDescriptor.IsDepthWriteEnabled = descriptor.DepthWriteEnabled;
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
                vertexLibrary,
                fragmentLibrary,
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
                if (fragmentLibrary.NativePtr != 0)
                {
                    fragmentLibrary.Dispose();
                }
                if (vertexLibrary.NativePtr != 0)
                {
                    vertexLibrary.Dispose();
                }
                ReleaseDependentObject();
            }
        }
    }

    private MTLLibrary LoadGraphicsLibrary(SilkShaderModuleDescriptor descriptor) =>
        MetalGraphicsShaderLibrary.Load(
            descriptor, LoadPinnedGraphicsLibrary, CompileGraphicsShaderSource);

    private MTLLibrary CompileGraphicsShaderSource(SilkShaderModuleDescriptor descriptor)
    {
        NSString source = MetalObjectOwnership.Retain(NSString.String(
            System.Text.Encoding.UTF8.GetString(descriptor.Code.Span)));
        try
        {
            NSError error = default;
            MTLCompileOptions options = default;
            MTLLibrary library = _device.NewLibrary(source, options, ref error);
            if (library.NativePtr == 0)
            {
                string stage = descriptor.Stage == SilkShaderStage.Vertex ? "vertex" : "fragment";
                throw CreateMetalFailure(
                    $"Could not compile the generated Metal {stage} shader.", error);
            }
            return library;
        }
        finally
        {
            source.Dispose();
        }
    }

    private MTLLibrary LoadPinnedGraphicsLibrary()
    {
        NSString path = MetalObjectOwnership.Retain(NSString.String(
            Path.Combine(AppContext.BaseDirectory, "mesh.metallib")));
        NSURL url = MetalObjectOwnership.Retain(NSURL.FileURLWithPath(path));
        try
        {
            NSError error = default;
            MTLLibrary library = _device.NewLibrary(url, ref error);
            if (library.NativePtr == 0)
            {
                throw CreateMetalFailure(
                    "Could not load the pinned Metal shader library.", error);
            }
            return library;
        }
        finally
        {
            url.Dispose();
            path.Dispose();
        }
    }

    private static InvalidOperationException CreateMetalFailure(string message, NSError error)
    {
        if (error.NativePtr == 0)
        {
            return new InvalidOperationException($"{message} Metal did not supply an NSError.");
        }
        string? description = MetalObjectOwnership.GetString(error.LocalizedDescription);
        if (string.IsNullOrEmpty(description))
        {
            description = "Metal supplied no localized description.";
        }
        return new InvalidOperationException(FormattableString.Invariant(
            $"{message} Metal error {error.Code}: {description}"));
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
    private IDisposable?[]? _resourceLeases = resourceLeases;
    private bool _releasingLeases;

    internal MetalSilkGraphicsDevice Device { get; } = device;

    internal MetalSilkGraphicsShaderModule Vertex { get; } = vertex;

    internal MetalSilkGraphicsShaderModule Fragment { get; } = fragment;

    public ISilkGraphicsBindingLayout BindingLayout => layout;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfDisposed() => ThrowIfResourceDisposed();

    protected override void ReleaseNative()
    {
        RetireOwnedLeases(ref _resourceLeases, ref _releasingLeases);
        Device.ReleaseDependentObject();
    }

    internal static void RetireOwnedLeases(ref IDisposable?[]? leases, ref bool releasing) =>
        SilkOwnedResourceLeases.Dispose(ref leases, ref releasing);
}

[SupportedOSPlatform("macos")]
internal sealed class MetalSilkGraphicsPipeline(
    MetalSilkGraphicsDevice device,
    SilkGraphicsPipelineDescriptor descriptor,
    MTLLibrary vertexLibrary,
    MTLLibrary fragmentLibrary,
    MTLFunction vertexFunction,
    MTLFunction fragmentFunction,
    MTLRenderPipelineState pipeline,
    MTLDepthStencilState depthState,
    IDisposable programLease)
    : SilkGraphicsResourceBase, ISilkGraphicsPipeline
{
    private MTLLibrary _vertexLibrary = vertexLibrary;
    private MTLLibrary _fragmentLibrary = fragmentLibrary;
    private MTLFunction _vertexFunction = vertexFunction;
    private MTLFunction _fragmentFunction = fragmentFunction;
    private MTLRenderPipelineState _pipeline = pipeline;
    private MTLDepthStencilState _depthState = depthState;
    private IDisposable? _programLease = programLease;
    private SilkResourceReleaseSteps _releaseSteps;
    private Action<int>? _releaseStep;

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
        RetireOwnedNativeSteps(ref _releaseSteps, _releaseStep ??= ReleaseStep);
    }

    internal static void RetireOwnedNativeSteps(ref SilkResourceReleaseSteps steps, Action<int> release) =>
        steps.Release(8, release);

    private void ReleaseStep(int step)
    {
        switch (step)
        {
            case 0:
                _depthState.Dispose();
                _depthState = default;
                break;
            case 1:
                _pipeline.Dispose();
                _pipeline = default;
                break;
            case 2:
                _fragmentFunction.Dispose();
                _fragmentFunction = default;
                break;
            case 3:
                _vertexFunction.Dispose();
                _vertexFunction = default;
                break;
            case 4:
                _fragmentLibrary.Dispose();
                _fragmentLibrary = default;
                break;
            case 5:
                _vertexLibrary.Dispose();
                _vertexLibrary = default;
                break;
            case 6:
                SilkOwnedResourceLeases.Dispose(ref _programLease);
                break;
            case 7:
                Device.ReleaseDependentObject();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(step));
        }
    }
}
