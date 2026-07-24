// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using SharpMetal.Foundation;
using SharpMetal.Metal;

namespace OpenUsd.Rendering.Silk.Metal;

[SupportedOSPlatform("macos")]
public sealed partial class MetalSilkGraphicsDevice
{
    private readonly MetalPickDeviceGeneration _pickDeviceGeneration = new();
    private long _pickPipelineCreationCount;
    private long _pickReadbackBufferCreationCount;

    /// <inheritdoc/>
    public ulong PickDeviceGeneration => _pickDeviceGeneration.Current;

    /// <inheritdoc/>
    public ISilkPickGraphicsPipeline CreatePickGraphicsPipeline(
        SilkPickPipelineDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        descriptor.Validate();
        if (descriptor.VertexShader.Format != SilkShaderBinaryFormat.MetalLibrary)
        {
            throw new ArgumentException(
                "Metal pick pipelines require the pinned Metal library.",
                nameof(descriptor));
        }
        SilkCheckedShaderAssets.ValidatePinnedMetalLibrary();

        MTLLibrary library = default;
        MTLFunction vertexFunction = default;
        MTLFunction fragmentFunction = default;
        MTLRenderPipelineState pipeline = default;
        MTLDepthStencilState depthState = default;
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

            vertexFunction = library.NewFunction(
                descriptor.VertexShader.EntryPoint);
            fragmentFunction = library.NewFunction(
                descriptor.FragmentShader.EntryPoint);
            if (vertexFunction.NativePtr == 0 || fragmentFunction.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    "The pinned Metal shader library is missing a checked pick entry point.");
            }

            var vertexDescriptor = new MTLVertexDescriptor();
            var pipelineDescriptor = new MTLRenderPipelineDescriptor();
            var depthDescriptor = new MTLDepthStencilDescriptor();
            try
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
                layout.Stride = 24;
                layout.StepFunction = MTLVertexStepFunction.PerVertex;

                pipelineDescriptor.VertexFunction = vertexFunction;
                pipelineDescriptor.FragmentFunction = fragmentFunction;
                pipelineDescriptor.VertexDescriptor = vertexDescriptor;
                pipelineDescriptor.InputPrimitiveTopology =
                    MTLPrimitiveTopologyClass.Triangle;
                pipelineDescriptor.RasterSampleCount = descriptor.SampleCount;
                MTLRenderPipelineColorAttachmentDescriptor colorAttachment =
                    pipelineDescriptor.ColorAttachments.Object(0);
                colorAttachment.PixelFormat = MTLPixelFormat.RGBA8Unorm;
                colorAttachment.IsBlendingEnabled = false;
                pipelineDescriptor.DepthAttachmentPixelFormat =
                    MTLPixelFormat.Depth32Float;

                NSError pipelineError = default;
                pipeline = _device.NewRenderPipelineState(
                    pipelineDescriptor,
                    ref pipelineError);
                if (pipeline.NativePtr == 0)
                {
                    throw new InvalidOperationException(
                        "Could not create the checked Metal pick pipeline.");
                }

                depthDescriptor.DepthCompareFunction = MTLCompareFunction.LessEqual;
                depthDescriptor.IsDepthWriteEnabled = true;
                depthState = _device.NewDepthStencilState(depthDescriptor);
                if (depthState.NativePtr == 0)
                {
                    throw new InvalidOperationException(
                        "Could not create the Metal pick depth-stencil state.");
                }
            }
            finally
            {
                depthDescriptor.Dispose();
                pipelineDescriptor.Dispose();
                vertexDescriptor.Dispose();
            }

            success = true;
            _ = Interlocked.Increment(ref _pickPipelineCreationCount);
            return new MetalSilkPickGraphicsPipeline(
                this,
                descriptor,
                PickDeviceGeneration,
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

    /// <inheritdoc/>
    public ISilkPickReadbackBuffer CreatePickReadbackBuffer()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RegisterDependentObject();
        MTLBuffer buffer = default;
        bool success = false;
        try
        {
            ulong rowPitch = Math.Max(
                checked((ulong)SilkPickTokenEncoding.ByteSize),
                _device.MinimumLinearTextureAlignmentForPixelFormat(
                    MTLPixelFormat.RGBA8Unorm));
            buffer = _device.NewBuffer(
                rowPitch,
                MTLResourceOptions.ResourceStorageModeShared);
            if (buffer.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    "Could not create a shared Metal pick readback buffer.");
            }
            success = true;
            _ = Interlocked.Increment(ref _pickReadbackBufferCreationCount);
            return new MetalSilkPickReadbackBuffer(
                this,
                buffer,
                PickDeviceGeneration,
                rowPitch);
        }
        finally
        {
            if (!success)
            {
                if (buffer.NativePtr != 0)
                {
                    buffer.Dispose();
                }
                ReleaseDependentObject();
            }
        }
    }

    internal long PickPipelineCreationCount =>
        Volatile.Read(ref _pickPipelineCreationCount);

    internal long PickReadbackBufferCreationCount =>
        Volatile.Read(ref _pickReadbackBufferCreationCount);

    internal void NotifyCommandBufferFailure()
    {
        _ = _pickDeviceGeneration.Invalidate();
    }
}

internal sealed class MetalPickDeviceGeneration
{
    private long _value = 1;

    internal ulong Current => checked((ulong)Volatile.Read(ref _value));

    internal ulong Invalidate() =>
        checked((ulong)Interlocked.Increment(ref _value));
}

internal static class MetalPickParameters
{
    internal const int UInt32Count = 4;

    internal static void Write(uint baseToken, Span<uint> destination)
    {
        ArgumentOutOfRangeException.ThrowIfZero(baseToken);
        if (destination.Length != UInt32Count)
        {
            throw new ArgumentException(
                "Metal PickParameters requires exactly one uint4.",
                nameof(destination));
        }
        destination.Clear();
        destination[0] = baseToken;
    }
}

internal readonly record struct MetalPickCopyPlan(
    ulong X,
    ulong Y,
    ulong Width,
    ulong Height,
    ulong Depth,
    ulong BytesPerRow,
    ulong BytesPerImage)
{
    internal static MetalPickCopyPlan Create(
        SilkTexturePixelCoordinate coordinate,
        ulong bytesPerRow)
    {
        if (bytesPerRow < SilkPickTokenEncoding.ByteSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bytesPerRow),
                "A Metal pick copy row must retain at least one RGBA8 pixel.");
        }
        return new(
            coordinate.X,
            coordinate.Y,
            1,
            1,
            1,
            bytesPerRow,
            bytesPerRow);
    }
}

[SupportedOSPlatform("macos")]
internal sealed class MetalSilkPickGraphicsPipeline(
    MetalSilkGraphicsDevice device,
    SilkPickPipelineDescriptor descriptor,
    ulong generation,
    MTLLibrary library,
    MTLFunction vertexFunction,
    MTLFunction fragmentFunction,
    MTLRenderPipelineState pipeline,
    MTLDepthStencilState depthState)
    : SilkGraphicsResourceBase, ISilkPickGraphicsPipeline
{
    private MTLLibrary _library = library;
    private MTLFunction _vertexFunction = vertexFunction;
    private MTLFunction _fragmentFunction = fragmentFunction;
    private MTLRenderPipelineState _pipeline = pipeline;
    private MTLDepthStencilState _depthState = depthState;

    internal MetalSilkGraphicsDevice Device { get; } = device;

    internal ulong Generation { get; } = generation;

    public SilkPickPipelineDescriptor Descriptor { get; } = descriptor;

    internal MTLRenderPipelineState Pipeline => _pipeline;

    internal MTLDepthStencilState DepthState => _depthState;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfUnavailable()
    {
        ThrowIfResourceDisposed();
        if (Generation != Device.PickDeviceGeneration)
        {
            throw new InvalidOperationException(
                "The Metal pick pipeline belongs to an invalid device generation.");
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
internal sealed unsafe class MetalSilkPickReadbackBuffer(
    MetalSilkGraphicsDevice device,
    MTLBuffer buffer,
    ulong generation,
    ulong rowPitch)
    : SilkGraphicsResourceBase, ISilkPickReadbackBuffer
{
    private MTLBuffer _buffer = buffer;

    internal MetalSilkGraphicsDevice Device { get; } = device;

    internal ulong Generation { get; } = generation;

    internal MTLBuffer Buffer => _buffer;

    internal ulong RowPitch { get; } = rowPitch;

    public int ByteSize => SilkPickTokenEncoding.ByteSize;

    public void ReadRgba8Pixel(Span<byte> destination)
    {
        ThrowIfResourceDisposed();
        if (destination.Length != ByteSize)
        {
            throw new ArgumentException(
                "A Metal pick readback destination must contain exactly four bytes.",
                nameof(destination));
        }
        if (Generation != Device.PickDeviceGeneration)
        {
            throw new InvalidOperationException(
                "The Metal pick readback belongs to an invalid device generation.");
        }
        nint contents = _buffer.Contents;
        if (contents == 0)
        {
            throw new InvalidOperationException(
                "The shared Metal pick readback buffer is not CPU-visible.");
        }
        new ReadOnlySpan<byte>((void*)contents, ByteSize).CopyTo(destination);
    }

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfUnavailable()
    {
        ThrowIfResourceDisposed();
        if (Generation != Device.PickDeviceGeneration)
        {
            throw new InvalidOperationException(
                "The Metal pick readback belongs to an invalid device generation.");
        }
    }

    protected override void ReleaseNative()
    {
        _buffer.Dispose();
        Device.ReleaseDependentObject();
    }
}
