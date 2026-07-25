// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SharpMetal.Metal;

namespace OpenUsd.Rendering.Silk.Metal;

[SupportedOSPlatform("macos")]
public sealed partial class MetalSilkGraphicsDevice
{
    /// <inheritdoc/>
    public ISilkGraphicsTexture CreateTexture2D(
        uint width,
        uint height,
        SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm) =>
        CreateTexture2D(new SilkTextureDescriptor(
            width,
            height,
            format,
            SilkTextureDescriptor.GetDefaultUsage(format)));

    /// <inheritdoc/>
    public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        descriptor.Validate();
        RegisterDependentObject();

        MTLTextureDescriptor nativeDescriptor = default;
        MTLTexture texture = default;
        bool success = false;
        try
        {
            nativeDescriptor = MTLTextureDescriptor.Texture2DDescriptor(
                descriptor.Format == SilkTextureFormat.Rgba8Unorm
                    ? MTLPixelFormat.RGBA8Unorm
                    : MTLPixelFormat.Depth32Float,
                descriptor.Width,
                descriptor.Height,
                false);
            nativeDescriptor.SampleCount = 1;
            nativeDescriptor.StorageMode = MTLStorageMode.Shared;
            MTLTextureUsage nativeUsage = MTLTextureUsage.Unknown;
            if (descriptor.Usage.HasFlag(SilkTextureUsage.ColorRenderTarget) ||
                descriptor.Usage.HasFlag(SilkTextureUsage.DepthRenderTarget))
            {
                nativeUsage |= MTLTextureUsage.RenderTarget;
            }
            if (descriptor.Usage.HasFlag(SilkTextureUsage.Sampled))
            {
                nativeUsage |= MTLTextureUsage.ShaderRead;
            }
            nativeDescriptor.Usage = nativeUsage;
            texture = _device.NewTexture(nativeDescriptor);
            if (texture.NativePtr == 0)
            {
                throw new InvalidOperationException("Could not create a Metal texture.");
            }
            success = true;
            return new MetalSilkGraphicsTexture(this, texture, descriptor);
        }
        finally
        {
            if (nativeDescriptor.NativePtr != 0)
            {
                nativeDescriptor.Dispose();
            }
            if (!success)
            {
                if (texture.NativePtr != 0)
                {
                    texture.Dispose();
                }
                ReleaseDependentObject();
            }
        }
    }

    /// <inheritdoc/>
    public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        descriptor.Validate();
        RegisterDependentObject();
        MTLSamplerDescriptor nativeDescriptor = default;
        MTLSamplerState sampler = default;
        bool success = false;
        try
        {
            nativeDescriptor = new MTLSamplerDescriptor
            {
                MinFilter = GetFilter(descriptor.MinFilter),
                MagFilter = GetFilter(descriptor.MagFilter),
                SAddressMode = GetAddressMode(descriptor.AddressU),
                TAddressMode = GetAddressMode(descriptor.AddressV),
                RAddressMode = GetAddressMode(descriptor.AddressW)
            };
            sampler = _device.NewSamplerState(nativeDescriptor);
            if (sampler.NativePtr == 0)
            {
                throw new InvalidOperationException("Could not create a Metal sampler.");
            }
            success = true;
            return new MetalSilkGraphicsSampler(this, sampler, descriptor);
        }
        finally
        {
            if (nativeDescriptor.NativePtr != 0)
            {
                nativeDescriptor.Dispose();
            }
            if (!success)
            {
                if (sampler.NativePtr != 0)
                {
                    sampler.Dispose();
                }
                ReleaseDependentObject();
            }
        }
    }

    /// <inheritdoc/>
    public ISilkGraphicsCommandList CreateCommandList()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new MetalSilkGraphicsCommandList(this);
    }

    /// <inheritdoc/>
    public unsafe ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(commandList);
        if (commandList is not MetalSilkGraphicsCommandList commands ||
            !ReferenceEquals(commands.Device, this))
        {
            throw new ArgumentException(
                "The command list was not created by this Metal device.",
                nameof(commandList));
        }
        commands.MarkSubmitted();

        var leases = new List<IDisposable>();
        var leasedTextures = new HashSet<MetalSilkGraphicsTexture>();
        var leasedPipelines = new HashSet<MetalSilkGraphicsPipeline>();
        HashSet<MetalSilkPickGraphicsPipeline>? leasedPickPipelines =
            commands.ContainsPickCommands ? [] : null;
        HashSet<MetalSilkSelectionMaskGraphicsPipeline>? leasedSelectionMaskPipelines =
            commands.ContainsSelectionOutlineCommands ? [] : null;
        HashSet<MetalSilkSelectionOutlineGraphicsPipeline>?
            leasedSelectionOutlinePipelines =
                commands.ContainsSelectionOutlineCommands ? [] : null;
        HashSet<MetalSilkSelectionOutlineBinding>? leasedSelectionOutlineBindings =
            commands.ContainsSelectionOutlineCommands ? [] : null;
        var leasedComputePipelines = new HashSet<MetalSilkComputePipeline>();
        var leasedBuffers = new HashSet<MetalSilkGraphicsBuffer>();
        HashSet<MetalSilkPickReadbackBuffer>? leasedPickReadbacks =
            commands.ContainsPickCommands ? [] : null;
        var uploadBuffers = new List<MTLBuffer>();
        MTLCommandBuffer commandBuffer = default;
        MTLComputeCommandEncoder computeEncoder = default;
        bool dependentRegistered = false;
        bool nativeSubmissionAttempted = false;
        bool success = false;
        try
        {
            foreach (MetalGraphicsCommand command in commands.Commands)
            {
                if (command.Texture is { } texture && leasedTextures.Add(texture))
                {
                    leases.Add(texture.AcquireLease());
                }
                if (command.DepthTexture is { } depthTexture &&
                    leasedTextures.Add(depthTexture))
                {
                    leases.Add(depthTexture.AcquireLease());
                }
                if (command.Pipeline is { } pipeline &&
                    leasedPipelines.Add(pipeline))
                {
                    leases.Add(pipeline.AcquireLease());
                }
                if (command.PickPipeline is { } pickPipeline &&
                    leasedPickPipelines!.Add(pickPipeline))
                {
                    leases.Add(pickPipeline.AcquireLease());
                }
                if (command.SelectionMaskPipeline is { } selectionMaskPipeline &&
                    leasedSelectionMaskPipelines!.Add(selectionMaskPipeline))
                {
                    leases.Add(selectionMaskPipeline.AcquireLease());
                }
                if (command.SelectionOutlinePipeline is { } selectionOutlinePipeline &&
                    leasedSelectionOutlinePipelines!.Add(selectionOutlinePipeline))
                {
                    leases.Add(selectionOutlinePipeline.AcquireLease());
                }
                if (command.SelectionOutlineBinding is { } selectionOutlineBinding &&
                    leasedSelectionOutlineBindings!.Add(selectionOutlineBinding))
                {
                    leases.Add(selectionOutlineBinding.AcquireLease());
                }
                if (command.ComputePipeline is { } leasedComputePipeline &&
                    leasedComputePipelines.Add(leasedComputePipeline))
                {
                    leases.Add(leasedComputePipeline.AcquireLease());
                }
                if (command.Buffer is { } buffer && leasedBuffers.Add(buffer))
                {
                    leases.Add(buffer.AcquireLease());
                }
                if (command.PickReadback is { } pickReadback &&
                    leasedPickReadbacks!.Add(pickReadback))
                {
                    leases.Add(pickReadback.AcquireLease());
                }
            }
            RegisterDependentObject();
            dependentRegistered = true;

            nativeSubmissionAttempted = true;
            commandBuffer = _queue.CommandBuffer();
            if (commandBuffer.NativePtr == 0)
            {
                throw new InvalidOperationException("Could not create a Metal command buffer.");
            }
            MetalSilkGraphicsTexture? colorAttachment = null;
            MetalSilkGraphicsTexture? depthAttachment = null;
            MetalSilkGraphicsPipeline? currentPipeline = null;
            MetalSilkPickGraphicsPipeline? currentPickPipeline = null;
            MetalSilkSelectionMaskGraphicsPipeline? currentSelectionMaskPipeline = null;
            MetalSilkSelectionOutlineGraphicsPipeline?
                currentSelectionOutlinePipeline = null;
            MetalSilkSelectionOutlineBinding? currentSelectionOutlineBinding = null;
            MetalSilkGraphicsBuffer? vertexBuffer = null;
            MetalSilkGraphicsBuffer? indexBuffer = null;
            MetalSilkGraphicsBuffer? uniformBuffer = null;
            uint? pickBaseToken = null;
            MetalSilkComputePipeline? computePipeline = null;
            MetalSilkGraphicsBuffer? storageBuffer = null;
            MetalSilkGraphicsBuffer? computeUniformBuffer = null;
            SilkViewport? currentViewport = null;
            SilkScissor? currentScissor = null;
            bool rendering = false;
            Span<uint> pickParameters = commands.ContainsPickCommands
                ? stackalloc uint[MetalPickParameters.UInt32Count]
                : Span<uint>.Empty;
            foreach (MetalGraphicsCommand command in commands.Commands)
            {
                bool computeCommand =
                    command.PickKind == MetalPickCommandKind.None &&
                    (command.Kind == SilkGraphicsCommandKind.SetComputePipeline ||
                     command.Kind == SilkGraphicsCommandKind.SetStorageBuffer ||
                     command.Kind == SilkGraphicsCommandKind.SetComputeUniformBuffer ||
                     command.Kind == SilkGraphicsCommandKind.Dispatch ||
                     command.Kind == SilkGraphicsCommandKind.BufferBarrier);
                if (!computeCommand && computeEncoder.NativePtr != 0)
                {
                    try
                    {
                        computeEncoder.EndEncoding();
                    }
                    finally
                    {
                        computeEncoder.Dispose();
                        computeEncoder = default;
                    }
                }
                if (command.PickKind != MetalPickCommandKind.None)
                {
                    switch (command.PickKind)
                    {
                        case MetalPickCommandKind.SetGraphicsPipeline:
                            currentPickPipeline = command.PickPipeline!;
                            currentPickPipeline.ThrowIfUnavailable();
                            currentPipeline = null;
                            currentSelectionMaskPipeline = null;
                            currentSelectionOutlinePipeline = null;
                            currentSelectionOutlineBinding = null;
                            pickBaseToken = null;
                            break;
                        case MetalPickCommandKind.SetBaseToken:
                            pickBaseToken = command.PickBaseToken;
                            break;
                        case MetalPickCommandKind.CopyRgba8Pixel:
                            MetalSilkGraphicsTexture pickSource = command.Texture!;
                            MetalSilkPickReadbackBuffer pickReadback =
                                command.PickReadback!;
                            pickSource.ThrowIfDisposed();
                            pickReadback.ThrowIfUnavailable();
                            MetalPickCopyPlan copy = MetalPickCopyPlan.Create(
                                command.PickCoordinate,
                                pickReadback.RowPitch);
                            MTLBlitCommandEncoder pickBlitEncoder =
                                commandBuffer.BlitCommandEncoder();
                            if (pickBlitEncoder.NativePtr == 0)
                            {
                                throw new InvalidOperationException(
                                    "Could not create a Metal pick blit command encoder.");
                            }
                            try
                            {
                                pickBlitEncoder.CopyFromTexture(
                                    pickSource.Texture,
                                    0,
                                    0,
                                    new MTLOrigin
                                    {
                                        x = copy.X,
                                        y = copy.Y,
                                        z = 0
                                    },
                                    new MTLSize
                                    {
                                        width = copy.Width,
                                        height = copy.Height,
                                        depth = copy.Depth
                                    },
                                    pickReadback.Buffer,
                                    0,
                                    copy.BytesPerRow,
                                    copy.BytesPerImage);
                                pickBlitEncoder.EndEncoding();
                            }
                            finally
                            {
                                pickBlitEncoder.Dispose();
                            }
                            break;
                        default:
                            throw new InvalidOperationException(
                                "Unknown Metal pick command.");
                    }
                    continue;
                }
                if (command.SelectionKind != MetalSelectionCommandKind.None)
                {
                    switch (command.SelectionKind)
                    {
                        case MetalSelectionCommandKind.BeginMaskRendering:
                            colorAttachment = command.Texture!;
                            depthAttachment = command.DepthTexture!;
                            colorAttachment.ThrowIfDisposed();
                            depthAttachment.ThrowIfDisposed();
                            currentSelectionOutlinePipeline = null;
                            currentSelectionOutlineBinding = null;
                            rendering = true;
                            break;
                        case MetalSelectionCommandKind.SetMaskPipeline:
                            currentSelectionMaskPipeline =
                                command.SelectionMaskPipeline!;
                            currentSelectionMaskPipeline.ThrowIfUnavailable();
                            currentPipeline = null;
                            currentPickPipeline = null;
                            currentSelectionOutlinePipeline = null;
                            currentSelectionOutlineBinding = null;
                            break;
                        case MetalSelectionCommandKind.BeginOutlineRendering:
                            colorAttachment = command.Texture!;
                            colorAttachment.ThrowIfDisposed();
                            depthAttachment = null;
                            currentSelectionMaskPipeline = null;
                            rendering = true;
                            break;
                        case MetalSelectionCommandKind.SetOutlinePipeline:
                            currentSelectionOutlinePipeline =
                                command.SelectionOutlinePipeline!;
                            currentSelectionOutlinePipeline.ThrowIfUnavailable();
                            currentPipeline = null;
                            currentPickPipeline = null;
                            break;
                        case MetalSelectionCommandKind.SetOutlineBinding:
                            currentSelectionOutlineBinding =
                                command.SelectionOutlineBinding!;
                            currentSelectionOutlineBinding.ThrowIfUnavailable();
                            break;
                        case MetalSelectionCommandKind.DrawFullscreenTriangle:
                            if (!rendering || colorAttachment is null ||
                                depthAttachment is not null ||
                                currentSelectionOutlinePipeline is null ||
                                currentSelectionOutlineBinding is null ||
                                currentViewport is null ||
                                currentScissor is null)
                            {
                                throw new InvalidOperationException(
                                    "The ordered Metal selection-outline stream has " +
                                    "incomplete fullscreen state.");
                            }
                            EncodeSelectionOutlineDraw(
                                commandBuffer,
                                colorAttachment,
                                currentSelectionOutlinePipeline,
                                currentSelectionOutlineBinding,
                                currentViewport.Value,
                                currentScissor.Value);
                            break;
                        default:
                            throw new InvalidOperationException(
                                "Unknown Metal selection-outline command.");
                    }
                    continue;
                }
                switch (command.Kind)
                {
                    case SilkGraphicsCommandKind.UploadTexture:
                        MetalSilkGraphicsTexture uploadTexture = command.Texture!;
                        uploadTexture.ThrowIfDisposed();
                        MTLBuffer upload = CreateTextureUpload(command.Data!);
                        uploadBuffers.Add(upload);
                        MTLBlitCommandEncoder blitEncoder =
                            commandBuffer.BlitCommandEncoder();
                        if (blitEncoder.NativePtr == 0)
                        {
                            throw new InvalidOperationException(
                                "Could not create a Metal blit command encoder.");
                        }
                        blitEncoder.CopyFromBuffer(
                            upload,
                            0,
                            checked((ulong)uploadTexture.Width * 4),
                            checked((ulong)command.Data!.Length),
                            new MTLSize
                            {
                                width = uploadTexture.Width,
                                height = uploadTexture.Height,
                                depth = 1
                            },
                            uploadTexture.Texture,
                            0,
                            0,
                            new MTLOrigin());
                        blitEncoder.EndEncoding();
                        blitEncoder.Dispose();
                        break;
                    case SilkGraphicsCommandKind.ClearColor:
                        MetalSilkGraphicsTexture colorTexture = command.Texture!;
                        colorTexture.ThrowIfDisposed();
                        var colorDescriptor = new MTLRenderPassDescriptor();
                        try
                        {
                            MTLRenderPassColorAttachmentDescriptor attachment =
                                colorDescriptor.ColorAttachments.Object(0);
                            attachment.Texture = colorTexture.Texture;
                            attachment.LoadAction = MTLLoadAction.Clear;
                            attachment.StoreAction = MTLStoreAction.Store;
                            attachment.ClearColor = new MTLClearColor
                            {
                                red = command.Color.Red,
                                green = command.Color.Green,
                                blue = command.Color.Blue,
                                alpha = command.Color.Alpha
                            };
                            MTLRenderCommandEncoder colorEncoder =
                                commandBuffer.RenderCommandEncoder(colorDescriptor);
                            if (colorEncoder.NativePtr == 0)
                            {
                                throw new InvalidOperationException(
                                    "Could not create a Metal render command encoder.");
                            }
                            colorEncoder.EndEncoding();
                            colorEncoder.Dispose();
                        }
                        finally
                        {
                            colorDescriptor.Dispose();
                        }
                        break;
                    case SilkGraphicsCommandKind.ClearDepth:
                        MetalSilkGraphicsTexture depthTextureToClear = command.Texture!;
                        depthTextureToClear.ThrowIfDisposed();
                        var depthDescriptor = new MTLRenderPassDescriptor();
                        try
                        {
                            MTLRenderPassDepthAttachmentDescriptor attachment =
                                depthDescriptor.DepthAttachment;
                            attachment.Texture = depthTextureToClear.Texture;
                            attachment.LoadAction = MTLLoadAction.Clear;
                            attachment.StoreAction = MTLStoreAction.Store;
                            attachment.ClearDepth = command.Depth;
                            MTLRenderCommandEncoder depthEncoder =
                                commandBuffer.RenderCommandEncoder(depthDescriptor);
                            if (depthEncoder.NativePtr == 0)
                            {
                                throw new InvalidOperationException(
                                    "Could not create a Metal render command encoder.");
                            }
                            depthEncoder.EndEncoding();
                            depthEncoder.Dispose();
                        }
                        finally
                        {
                            depthDescriptor.Dispose();
                        }
                        break;
                    case SilkGraphicsCommandKind.BeginRendering:
                        colorAttachment = command.Texture!;
                        depthAttachment = command.DepthTexture!;
                        colorAttachment.ThrowIfDisposed();
                        depthAttachment.ThrowIfDisposed();
                        rendering = true;
                        break;
                    case SilkGraphicsCommandKind.SetGraphicsPipeline:
                        currentPipeline = command.Pipeline!;
                        currentPipeline.ThrowIfDisposed();
                        currentPickPipeline = null;
                        currentSelectionMaskPipeline = null;
                        currentSelectionOutlinePipeline = null;
                        currentSelectionOutlineBinding = null;
                        break;
                    case SilkGraphicsCommandKind.SetViewport:
                        currentViewport = command.Viewport;
                        break;
                    case SilkGraphicsCommandKind.SetScissor:
                        currentScissor = command.Scissor;
                        break;
                    case SilkGraphicsCommandKind.SetVertexBuffer:
                        vertexBuffer = command.Buffer!;
                        vertexBuffer.ThrowIfDisposed();
                        break;
                    case SilkGraphicsCommandKind.SetIndexBuffer:
                        indexBuffer = command.Buffer!;
                        indexBuffer.ThrowIfDisposed();
                        break;
                    case SilkGraphicsCommandKind.SetUniformBuffer:
                        uniformBuffer = command.Buffer!;
                        uniformBuffer.ThrowIfDisposed();
                        break;
                    case SilkGraphicsCommandKind.DrawIndexed:
                        if (!rendering || colorAttachment is null ||
                            depthAttachment is null ||
                            (currentPipeline is null &&
                             currentPickPipeline is null &&
                             currentSelectionMaskPipeline is null) ||
                            vertexBuffer is null || indexBuffer is null ||
                            uniformBuffer is null || currentViewport is null ||
                            currentScissor is null)
                        {
                            throw new InvalidOperationException(
                                "The ordered Metal command stream has incomplete draw state.");
                        }
                        var renderDescriptor = new MTLRenderPassDescriptor();
                        try
                        {
                            MTLRenderPassColorAttachmentDescriptor color =
                                renderDescriptor.ColorAttachments.Object(0);
                            color.Texture = colorAttachment.Texture;
                            color.LoadAction = MTLLoadAction.Load;
                            color.StoreAction = MTLStoreAction.Store;
                            MTLRenderPassDepthAttachmentDescriptor depth =
                                renderDescriptor.DepthAttachment;
                            depth.Texture = depthAttachment.Texture;
                            depth.LoadAction = MTLLoadAction.Load;
                            depth.StoreAction = MTLStoreAction.Store;

                            MTLRenderCommandEncoder encoder =
                                commandBuffer.RenderCommandEncoder(renderDescriptor);
                            if (encoder.NativePtr == 0)
                            {
                                throw new InvalidOperationException(
                                    "Could not create a Metal render command encoder.");
                            }
                            try
                            {
                                if (currentSelectionMaskPipeline is not null)
                                {
                                    currentSelectionMaskPipeline.ThrowIfUnavailable();
                                    encoder.SetRenderPipelineState(
                                        currentSelectionMaskPipeline.Pipeline);
                                    encoder.SetDepthStencilState(
                                        currentSelectionMaskPipeline.DepthState);
                                }
                                else if (currentPickPipeline is not null)
                                {
                                    if (pickBaseToken is null)
                                    {
                                        throw new InvalidOperationException(
                                            "A Metal pick draw requires a nonzero base token.");
                                    }
                                    currentPickPipeline.ThrowIfUnavailable();
                                    encoder.SetRenderPipelineState(
                                        currentPickPipeline.Pipeline);
                                    encoder.SetDepthStencilState(
                                        currentPickPipeline.DepthState);
                                }
                                else
                                {
                                    encoder.SetRenderPipelineState(
                                        currentPipeline!.Pipeline);
                                    encoder.SetDepthStencilState(
                                        currentPipeline.DepthState);
                                }
                                encoder.SetCullMode(MTLCullMode.None);
                                encoder.SetViewport(new MTLViewport
                                {
                                    originX = currentViewport.Value.X,
                                    originY = currentViewport.Value.Y,
                                    width = currentViewport.Value.Width,
                                    height = currentViewport.Value.Height,
                                    znear = currentViewport.Value.MinDepth,
                                    zfar = currentViewport.Value.MaxDepth
                                });
                                encoder.SetScissorRect(new MTLScissorRect
                                {
                                    x = checked((ulong)currentScissor.Value.X),
                                    y = checked((ulong)currentScissor.Value.Y),
                                    width = currentScissor.Value.Width,
                                    height = currentScissor.Value.Height
                                });
                                encoder.SetVertexBuffer(vertexBuffer.Buffer, 0, 30);
                                encoder.SetVertexBuffer(uniformBuffer.Buffer, 0, 0);
                                if (currentPickPipeline is not null)
                                {
                                    MetalPickParameters.Write(
                                        pickBaseToken!.Value,
                                        pickParameters);
                                    fixed (uint* pickParametersPointer = pickParameters)
                                    {
                                        encoder.SetFragmentBytes(
                                            (nint)pickParametersPointer,
                                            checked((ulong)(
                                                MetalPickParameters.UInt32Count *
                                                sizeof(uint))),
                                            1);
                                    }
                                }
                                else if (currentSelectionMaskPipeline is null)
                                {
                                    encoder.SetFragmentBuffer(
                                        uniformBuffer.Buffer,
                                        0,
                                        0);
                                }
                                encoder.DrawIndexedPrimitives(
                                    MTLPrimitiveType.Triangle,
                                    command.IndexCount,
                                    MTLIndexType.UInt32,
                                    indexBuffer.Buffer,
                                    0);
                                encoder.EndEncoding();
                            }
                            finally
                            {
                                encoder.Dispose();
                            }
                        }
                        finally
                        {
                            renderDescriptor.Dispose();
                        }
                        break;
                    case SilkGraphicsCommandKind.EndRendering:
                        if (!rendering)
                        {
                            throw new InvalidOperationException(
                                "The ordered Metal command stream ended no rendering scope.");
                        }
                        colorAttachment = null;
                        depthAttachment = null;
                        rendering = false;
                        break;
                    case SilkGraphicsCommandKind.SetComputePipeline:
                        computePipeline = command.ComputePipeline!;
                        computePipeline.ThrowIfDisposed();
                        break;
                    case SilkGraphicsCommandKind.SetStorageBuffer:
                        storageBuffer = command.Buffer!;
                        storageBuffer.ThrowIfDisposed();
                        break;
                    case SilkGraphicsCommandKind.SetComputeUniformBuffer:
                        computeUniformBuffer = command.Buffer!;
                        computeUniformBuffer.ThrowIfDisposed();
                        break;
                    case SilkGraphicsCommandKind.Dispatch:
                        if (computePipeline is null ||
                            storageBuffer is null ||
                            computeUniformBuffer is null)
                        {
                            throw new InvalidOperationException(
                                "The ordered Metal command stream has incomplete compute state.");
                        }
                        if (computeEncoder.NativePtr == 0)
                        {
                            computeEncoder = commandBuffer.ComputeCommandEncoder();
                            if (computeEncoder.NativePtr == 0)
                            {
                                throw new InvalidOperationException(
                                    "Could not create a Metal compute command encoder.");
                            }
                        }
                        computeEncoder.SetComputePipelineState(
                            computePipeline.Pipeline);
                        computeEncoder.SetBuffer(storageBuffer.Buffer, 0, 0);
                        computeEncoder.SetBuffer(computeUniformBuffer.Buffer, 0, 1);
                        computeEncoder.DispatchThreadgroups(
                            new MTLSize
                            {
                                width = checked((command.ElementCount + 63) / 64),
                                height = 1,
                                depth = 1
                            },
                            new MTLSize
                            {
                                width = 64,
                                height = 1,
                                depth = 1
                            });
                        break;
                    case SilkGraphicsCommandKind.BufferBarrier:
                        MetalSilkGraphicsBuffer barrierBuffer = command.Buffer!;
                        if (computeEncoder.NativePtr == 0)
                        {
                            throw new InvalidOperationException(
                                "A Metal buffer barrier requires prior compute work.");
                        }
                        computeEncoder.MemoryBarrier(MTLBarrierScope.Buffers);
                        if (barrierBuffer.Usage.HasFlag(SilkBufferUsage.Vertex) ||
                            barrierBuffer.Usage.HasFlag(SilkBufferUsage.Index) ||
                            barrierBuffer.Usage.HasFlag(SilkBufferUsage.Uniform))
                        {
                            try
                            {
                                computeEncoder.EndEncoding();
                            }
                            finally
                            {
                                computeEncoder.Dispose();
                                computeEncoder = default;
                            }
                        }
                        break;
                    default:
                        throw new InvalidOperationException("Unknown Metal graphics command.");
                }
            }
            if (computeEncoder.NativePtr != 0)
            {
                try
                {
                    computeEncoder.EndEncoding();
                }
                finally
                {
                    computeEncoder.Dispose();
                    computeEncoder = default;
                }
            }
            commandBuffer.Commit();
            var completion = new MetalSubmissionCompletion(
                this,
                commandBuffer,
                commands.ContainsPickCommands,
                commands.ContainsSelectionOutlineCommands);
            foreach (MetalSilkGraphicsTexture texture in leasedTextures)
            {
                texture.SetPendingSubmission(completion);
            }
            success = true;
            return new MetalSilkGraphicsSubmission(
                this,
                commandBuffer,
                completion,
                [.. leases],
                [.. uploadBuffers]);
        }
        finally
        {
            if (!success)
            {
                if (computeEncoder.NativePtr != 0)
                {
                    try
                    {
                        computeEncoder.EndEncoding();
                    }
                    finally
                    {
                        computeEncoder.Dispose();
                    }
                }
                if (commandBuffer.NativePtr != 0)
                {
                    commandBuffer.Dispose();
                }
                foreach (IDisposable lease in leases)
                {
                    lease.Dispose();
                }
                foreach (MTLBuffer upload in uploadBuffers)
                {
                    upload.Dispose();
                }
                if (dependentRegistered)
                {
                    ReleaseDependentObject();
                }
                if (nativeSubmissionAttempted && commands.ContainsPickCommands)
                {
                    NotifyCommandBufferFailure();
                }
                if (nativeSubmissionAttempted &&
                    commands.ContainsSelectionOutlineCommands)
                {
                    NotifySelectionOutlineCommandBufferFailure();
                }
            }
        }
    }

    private static void EncodeSelectionOutlineDraw(
        MTLCommandBuffer commandBuffer,
        MetalSilkGraphicsTexture colorAttachment,
        MetalSilkSelectionOutlineGraphicsPipeline pipeline,
        MetalSilkSelectionOutlineBinding binding,
        SilkViewport viewport,
        SilkScissor scissor)
    {
        var renderDescriptor = new MTLRenderPassDescriptor();
        try
        {
            MTLRenderPassColorAttachmentDescriptor color =
                renderDescriptor.ColorAttachments.Object(0);
            color.Texture = colorAttachment.Texture;
            color.LoadAction = MTLLoadAction.Load;
            color.StoreAction = MTLStoreAction.Store;

            MTLRenderCommandEncoder encoder =
                commandBuffer.RenderCommandEncoder(renderDescriptor);
            if (encoder.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    "Could not create a Metal selection-outline encoder.");
            }
            try
            {
                pipeline.ThrowIfUnavailable();
                binding.ThrowIfUnavailable();
                encoder.SetRenderPipelineState(pipeline.Pipeline);
                encoder.SetCullMode(MTLCullMode.None);
                encoder.SetViewport(new MTLViewport
                {
                    originX = viewport.X,
                    originY = viewport.Y,
                    width = viewport.Width,
                    height = viewport.Height,
                    znear = viewport.MinDepth,
                    zfar = viewport.MaxDepth
                });
                encoder.SetScissorRect(new MTLScissorRect
                {
                    x = checked((ulong)scissor.X),
                    y = checked((ulong)scissor.Y),
                    width = scissor.Width,
                    height = scissor.Height
                });
                encoder.SetFragmentTexture(binding.Mask.Texture, 0);
                encoder.SetFragmentTexture(binding.Depth.Texture, 1);
                encoder.SetFragmentSamplerState(binding.Sampler.Sampler, 0);
                encoder.SetFragmentBuffer(binding.Parameters.Buffer, 0, 0);
                encoder.DrawPrimitives(MTLPrimitiveType.Triangle, 0, 3);
                encoder.EndEncoding();
            }
            finally
            {
                encoder.Dispose();
            }
        }
        finally
        {
            renderDescriptor.Dispose();
        }
    }

    private unsafe MTLBuffer CreateTextureUpload(ReadOnlySpan<byte> source)
    {
        MTLBuffer upload = _device.NewBuffer(
            checked((ulong)source.Length),
            MTLResourceOptions.ResourceStorageModeShared);
        if (upload.NativePtr == 0)
        {
            throw new InvalidOperationException("Could not create a Metal upload buffer.");
        }
        try
        {
            source.CopyTo(new Span<byte>((void*)upload.Contents, source.Length));
            return upload;
        }
        catch
        {
            upload.Dispose();
            throw;
        }
    }

    private static MTLSamplerMinMagFilter GetFilter(SilkSamplerFilter filter) =>
        filter switch
        {
            SilkSamplerFilter.Nearest => MTLSamplerMinMagFilter.Nearest,
            SilkSamplerFilter.Linear => MTLSamplerMinMagFilter.Linear,
            _ => throw new ArgumentOutOfRangeException(nameof(filter))
        };

    private static MTLSamplerAddressMode GetAddressMode(SilkSamplerAddressMode mode) =>
        mode switch
        {
            SilkSamplerAddressMode.ClampToEdge => MTLSamplerAddressMode.ClampToEdge,
            SilkSamplerAddressMode.Repeat => MTLSamplerAddressMode.Repeat,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
}

[SupportedOSPlatform("macos")]
internal sealed unsafe class MetalSilkGraphicsTexture : SilkGraphicsTextureBase
{
    private readonly MetalSilkGraphicsDevice _device;
    private MTLTexture _texture;
    private MetalSubmissionCompletion? _pendingSubmission;

    internal MetalSilkGraphicsTexture(
        MetalSilkGraphicsDevice device,
        MTLTexture texture,
        SilkTextureDescriptor descriptor)
        : base(descriptor)
    {
        _device = device;
        _texture = texture;
    }

    internal MetalSilkGraphicsDevice Device => _device;

    internal MTLTexture Texture => _texture;

    public override void ReadbackForTesting(Span<byte> destination)
    {
        ThrowIfDisposed();
        ValidateReadback(destination.Length);
        Readback(MemoryMarshal.AsBytes(destination));
    }

    public override void ReadbackForTesting(Span<float> destination)
    {
        ThrowIfDisposed();
        ValidateDepthReadback(destination.Length);
        Readback(MemoryMarshal.AsBytes(destination));
    }

    private void Readback(Span<byte> destination)
    {
        MetalSubmissionCompletion? pendingSubmission =
            Volatile.Read(ref _pendingSubmission);
        if (pendingSubmission is not null &&
            pendingSubmission.WaitAndGetFailure())
        {
            throw new InvalidOperationException("Metal command buffer execution failed.");
        }
        var region = new MTLRegion
        {
            origin = new MTLOrigin(),
            size = new MTLSize
            {
                width = Width,
                height = Height,
                depth = 1
            }
        };
        fixed (byte* destinationPointer = destination)
        {
            _texture.GetBytes(
                (nint)destinationPointer,
                checked((ulong)Width * 4),
                region,
                0);
        }
    }

    protected override void ReleaseNative()
    {
        _texture.Dispose();
        _device.ReleaseDependentObject();
    }

    internal IDisposable AcquireLease() => AcquireSubmissionLease();

    internal void SetPendingSubmission(MetalSubmissionCompletion submission) =>
        Volatile.Write(ref _pendingSubmission, submission);

    internal void ThrowIfDisposed() => ThrowIfTextureDisposed();
}

[SupportedOSPlatform("macos")]
internal sealed class MetalSilkGraphicsSampler(
    MetalSilkGraphicsDevice device,
    MTLSamplerState sampler,
    SilkSamplerDescriptor descriptor)
    : SilkGraphicsResourceBase, ISilkGraphicsSampler
{
    private MTLSamplerState _sampler = sampler;

    internal MetalSilkGraphicsDevice Device { get; } = device;

    public SilkSamplerDescriptor Descriptor { get; } = descriptor;

    internal MTLSamplerState Sampler => _sampler;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfDisposed() => ThrowIfResourceDisposed();

    protected override void ReleaseNative()
    {
        _sampler.Dispose();
        Device.ReleaseDependentObject();
    }
}

[SupportedOSPlatform("macos")]
internal sealed class MetalSilkGraphicsCommandList(MetalSilkGraphicsDevice device)
    : ISilkGraphicsCommandList,
      ISilkPickGraphicsCommandList,
      ISilkSelectionOutlineGraphicsCommandList
{
    private readonly List<MetalGraphicsCommand> _commands = [];
    private MetalSilkGraphicsTexture? _colorAttachment;
    private MetalSilkGraphicsTexture? _depthAttachment;
    private MetalSilkGraphicsPipeline? _pipeline;
    private MetalSilkPickGraphicsPipeline? _pickPipeline;
    private MetalSilkSelectionMaskGraphicsPipeline? _selectionMaskPipeline;
    private MetalSilkSelectionOutlineGraphicsPipeline? _selectionOutlinePipeline;
    private MetalSilkSelectionOutlineBinding? _selectionOutlineBinding;
    private MetalSilkGraphicsBuffer? _vertexBuffer;
    private MetalSilkGraphicsBuffer? _indexBuffer;
    private MetalSilkGraphicsBuffer? _uniformBuffer;
    private MetalSilkComputePipeline? _computePipeline;
    private MetalSilkGraphicsBuffer? _storageBuffer;
    private MetalSilkGraphicsBuffer? _computeUniformBuffer;
    private SilkViewport? _viewport;
    private SilkScissor? _scissor;
    private uint? _pickBaseToken;
    private bool _containsPickCommands;
    private bool _containsSelectionOutlineCommands;
    private bool _rendering;
    private bool _submitted;
    private bool _disposed;

    internal MetalSilkGraphicsDevice Device { get; } = device;

    internal IReadOnlyList<MetalGraphicsCommand> Commands => _commands;

    internal bool ContainsPickCommands => _containsPickCommands;

    internal bool ContainsSelectionOutlineCommands =>
        _containsSelectionOutlineCommands;

    public void UploadTexture(ISilkGraphicsTexture texture, ReadOnlySpan<byte> source)
    {
        ThrowIfOutsideRendering();
        MetalSilkGraphicsTexture metalTexture = ValidateTexture(texture);
        if (metalTexture.Format != SilkTextureFormat.Rgba8Unorm ||
            !metalTexture.Usage.HasFlag(SilkTextureUsage.CopyDestination))
        {
            throw new InvalidOperationException(
                "UploadTexture requires an RGBA8 texture with CopyDestination usage.");
        }
        int requiredLength = checked((int)(metalTexture.Width * metalTexture.Height * 4));
        if (source.Length != requiredLength)
        {
            throw new ArgumentException(
                $"The source must contain exactly {requiredLength} bytes.",
                nameof(source));
        }
        _commands.Add(MetalGraphicsCommand.Upload(metalTexture, source.ToArray()));
    }

    public void ClearColor(ISilkGraphicsTexture texture, SilkColor color)
    {
        ThrowIfOutsideRendering();
        MetalSilkGraphicsTexture metalTexture = ValidateTexture(texture);
        if (metalTexture.Format != SilkTextureFormat.Rgba8Unorm ||
            !metalTexture.Usage.HasFlag(SilkTextureUsage.ColorRenderTarget))
        {
            throw new InvalidOperationException("ClearColor requires an RGBA8 color render target.");
        }
        color.Validate();
        _commands.Add(MetalGraphicsCommand.ClearColor(metalTexture, color));
    }

    public void ClearDepth(ISilkGraphicsTexture texture, float depth)
    {
        ThrowIfOutsideRendering();
        MetalSilkGraphicsTexture metalTexture = ValidateTexture(texture);
        if (metalTexture.Format != SilkTextureFormat.D32Float ||
            !metalTexture.Usage.HasFlag(SilkTextureUsage.DepthRenderTarget))
        {
            throw new InvalidOperationException(
                "ClearDepth requires a D32Float depth render target.");
        }
        ValidateDepth(depth);
        _commands.Add(MetalGraphicsCommand.ClearDepth(metalTexture, depth));
    }

    public void BeginRendering(SilkRenderingDescriptor descriptor)
    {
        ThrowIfUnavailable();
        if (_rendering)
        {
            throw new InvalidOperationException("A rendering scope is already active.");
        }
        MetalSilkGraphicsTexture color = ValidateTexture(descriptor.ColorAttachment);
        MetalSilkGraphicsTexture depth = ValidateTexture(descriptor.DepthAttachment);
        if (color.Format != SilkTextureFormat.Rgba8Unorm ||
            !color.Usage.HasFlag(SilkTextureUsage.ColorRenderTarget) ||
            depth.Format != SilkTextureFormat.D32Float ||
            !depth.Usage.HasFlag(SilkTextureUsage.DepthRenderTarget) ||
            color.Width != depth.Width ||
            color.Height != depth.Height)
        {
            throw new ArgumentException(
                "Rendering requires matching RGBA8 color and D32Float depth attachments.",
                nameof(descriptor));
        }
        _colorAttachment = color;
        _depthAttachment = depth;
        _rendering = true;
        _commands.Add(MetalGraphicsCommand.BeginRendering(color, depth));
    }

    public void SetGraphicsPipeline(ISilkGraphicsPipeline pipeline)
    {
        ThrowIfRendering();
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not MetalSilkGraphicsPipeline metalPipeline ||
            !ReferenceEquals(metalPipeline.Device, Device))
        {
            throw new ArgumentException(
                "The pipeline was not created by this Metal device.",
                nameof(pipeline));
        }
        metalPipeline.ThrowIfDisposed();
        _pipeline = metalPipeline;
        _pickPipeline = null;
        _selectionMaskPipeline = null;
        _selectionOutlinePipeline = null;
        _commands.Add(MetalGraphicsCommand.SetPipeline(metalPipeline));
    }

    public void SetPickGraphicsPipeline(ISilkPickGraphicsPipeline pipeline)
    {
        ThrowIfRendering();
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not MetalSilkPickGraphicsPipeline metalPipeline ||
            !ReferenceEquals(metalPipeline.Device, Device))
        {
            throw new ArgumentException(
                "The pick pipeline was not created by this Metal device.",
                nameof(pipeline));
        }
        metalPipeline.ThrowIfUnavailable();
        _pipeline = null;
        _pickPipeline = metalPipeline;
        _selectionMaskPipeline = null;
        _selectionOutlinePipeline = null;
        _pickBaseToken = null;
        _containsPickCommands = true;
        _commands.Add(MetalGraphicsCommand.SetPickPipeline(metalPipeline));
    }

    public void SetPickBaseToken(uint baseToken)
    {
        ThrowIfRendering();
        ArgumentOutOfRangeException.ThrowIfZero(baseToken);
        if (_pickPipeline is null)
        {
            throw new InvalidOperationException(
                "SetPickGraphicsPipeline must precede SetPickBaseToken.");
        }
        _pickBaseToken = baseToken;
        _containsPickCommands = true;
        _commands.Add(MetalGraphicsCommand.SetPickBaseToken(baseToken));
    }

    public void SetViewport(SilkViewport viewport)
    {
        ThrowIfRendering();
        viewport.Validate();
        _viewport = viewport;
        _commands.Add(MetalGraphicsCommand.SetViewport(viewport));
    }

    public void SetScissor(SilkScissor scissor)
    {
        ThrowIfRendering();
        scissor.Validate();
        _scissor = scissor;
        _commands.Add(MetalGraphicsCommand.SetScissor(scissor));
    }

    public void SetVertexBuffer(ISilkGraphicsBuffer buffer)
    {
        ThrowIfRendering();
        MetalSilkGraphicsBuffer metalBuffer = ValidateBuffer(buffer);
        if (!metalBuffer.Usage.HasFlag(SilkBufferUsage.Vertex))
        {
            throw new ArgumentException("The buffer is not a vertex buffer.", nameof(buffer));
        }
        _vertexBuffer = metalBuffer;
        _commands.Add(MetalGraphicsCommand.SetVertexBuffer(metalBuffer));
    }

    public void SetIndexBuffer(ISilkGraphicsBuffer buffer)
    {
        ThrowIfRendering();
        MetalSilkGraphicsBuffer metalBuffer = ValidateBuffer(buffer);
        if (!metalBuffer.Usage.HasFlag(SilkBufferUsage.Index))
        {
            throw new ArgumentException("The buffer is not an index buffer.", nameof(buffer));
        }
        _indexBuffer = metalBuffer;
        _commands.Add(MetalGraphicsCommand.SetIndexBuffer(metalBuffer));
    }

    public void SetUniformBuffer(
        uint setIndex,
        uint binding,
        ISilkGraphicsBuffer buffer)
    {
        ThrowIfRendering();
        MetalSilkGraphicsBuffer metalBuffer = ValidateBuffer(buffer);
        if (setIndex != 0 || binding != 0 ||
            !metalBuffer.Usage.HasFlag(SilkBufferUsage.Uniform) ||
            metalBuffer.Size < 80)
        {
            throw new ArgumentException(
                "SceneParameters requires an 80-byte uniform buffer at set 0, binding 0.",
                nameof(buffer));
        }
        _uniformBuffer = metalBuffer;
        _commands.Add(MetalGraphicsCommand.SetUniformBuffer(
            setIndex,
            binding,
            metalBuffer));
    }

    public void DrawIndexed(uint indexCount)
    {
        ThrowIfRendering();
        ArgumentOutOfRangeException.ThrowIfZero(indexCount);
        if (_colorAttachment is null || _depthAttachment is null ||
            (_pipeline is null &&
             _pickPipeline is null &&
             _selectionMaskPipeline is null) ||
            _vertexBuffer is null ||
            _indexBuffer is null || _uniformBuffer is null ||
            _viewport is null || _scissor is null)
        {
            throw new InvalidOperationException(
                "Indexed drawing requires attachments, pipeline, viewport, scissor, and all buffers.");
        }
        if (_pickPipeline is not null && _pickBaseToken is null)
        {
            throw new InvalidOperationException(
                "A pick draw requires a nonzero base token.");
        }
        if (checked((nuint)indexCount * 2) > _indexBuffer.Size)
        {
            throw new ArgumentOutOfRangeException(nameof(indexCount));
        }
        _commands.Add(MetalGraphicsCommand.DrawIndexed(indexCount));
    }

    public void EndRendering()
    {
        ThrowIfRendering();
        _commands.Add(MetalGraphicsCommand.EndRendering());
        _rendering = false;
        _colorAttachment = null;
        _depthAttachment = null;
        _selectionMaskPipeline = null;
        _selectionOutlinePipeline = null;
        _selectionOutlineBinding = null;
    }

    public void BeginSelectionMaskRendering(
        SilkSelectionMaskRenderingDescriptor descriptor)
    {
        ThrowIfUnavailable();
        if (_rendering)
        {
            throw new InvalidOperationException("A rendering scope is already active.");
        }
        descriptor.Validate();
        MetalSilkGraphicsTexture mask = ValidateTexture(descriptor.MaskAttachment);
        MetalSilkGraphicsTexture depth =
            ValidateTexture(descriptor.VisibleDepthAttachment);
        _colorAttachment = mask;
        _depthAttachment = depth;
        _selectionOutlinePipeline = null;
        _selectionOutlineBinding = null;
        _rendering = true;
        _containsSelectionOutlineCommands = true;
        _commands.Add(MetalGraphicsCommand.BeginSelectionMaskRendering(
            mask,
            depth));
    }

    public void SetSelectionMaskGraphicsPipeline(
        ISilkSelectionMaskGraphicsPipeline pipeline)
    {
        ThrowIfRendering();
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not MetalSilkSelectionMaskGraphicsPipeline metalPipeline ||
            !ReferenceEquals(metalPipeline.Device, Device))
        {
            throw new ArgumentException(
                "The selection-mask pipeline was not created by this Metal device.",
                nameof(pipeline));
        }
        metalPipeline.ThrowIfUnavailable();
        _pipeline = null;
        _pickPipeline = null;
        _selectionMaskPipeline = metalPipeline;
        _containsSelectionOutlineCommands = true;
        _commands.Add(MetalGraphicsCommand.SetSelectionMaskPipeline(
            metalPipeline));
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
        MetalSilkGraphicsTexture color =
            ValidateTexture(descriptor.VisibleColorAttachment);
        _colorAttachment = color;
        _depthAttachment = null;
        _selectionMaskPipeline = null;
        _rendering = true;
        _containsSelectionOutlineCommands = true;
        _commands.Add(MetalGraphicsCommand.BeginSelectionOutlineRendering(color));
    }

    public void SetSelectionOutlineGraphicsPipeline(
        ISilkSelectionOutlineGraphicsPipeline pipeline)
    {
        ThrowIfRendering();
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not MetalSilkSelectionOutlineGraphicsPipeline metalPipeline ||
            !ReferenceEquals(metalPipeline.Device, Device))
        {
            throw new ArgumentException(
                "The selection-outline pipeline was not created by this Metal device.",
                nameof(pipeline));
        }
        metalPipeline.ThrowIfUnavailable();
        _pipeline = null;
        _pickPipeline = null;
        _selectionOutlinePipeline = metalPipeline;
        _containsSelectionOutlineCommands = true;
        _commands.Add(MetalGraphicsCommand.SetSelectionOutlinePipeline(
            metalPipeline));
    }

    public void SetSelectionOutlineBinding(ISilkSelectionOutlineBinding binding)
    {
        ThrowIfRendering();
        ArgumentNullException.ThrowIfNull(binding);
        if (binding is not MetalSilkSelectionOutlineBinding metalBinding ||
            !ReferenceEquals(metalBinding.Device, Device))
        {
            throw new ArgumentException(
                "The selection-outline binding was not created by this Metal device.",
                nameof(binding));
        }
        metalBinding.ThrowIfUnavailable();
        if (_colorAttachment is null ||
            metalBinding.Mask.Width != _colorAttachment.Width ||
            metalBinding.Mask.Height != _colorAttachment.Height)
        {
            throw new ArgumentException(
                "The selection-outline binding dimensions must match the visible target.",
                nameof(binding));
        }
        _selectionOutlineBinding = metalBinding;
        _containsSelectionOutlineCommands = true;
        _commands.Add(MetalGraphicsCommand.SetSelectionOutlineBinding(
            metalBinding));
    }

    public void DrawSelectionOutlineFullscreenTriangle()
    {
        ThrowIfRendering();
        if (_colorAttachment is null ||
            _depthAttachment is not null ||
            _selectionOutlinePipeline is null ||
            _selectionOutlineBinding is null ||
            _viewport is null ||
            _scissor is null)
        {
            throw new InvalidOperationException(
                "Selection-outline drawing requires a color target, pipeline, " +
                "binding, viewport, and scissor.");
        }
        _containsSelectionOutlineCommands = true;
        _commands.Add(MetalGraphicsCommand.DrawSelectionOutlineFullscreenTriangle());
    }

    public void CopyRgba8Pixel(
        ISilkGraphicsTexture source,
        SilkTexturePixelCoordinate coordinate,
        ISilkPickReadbackBuffer destination)
    {
        ThrowIfOutsideRendering();
        coordinate.Validate(source);
        MetalSilkGraphicsTexture metalSource = ValidateTexture(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (destination is not MetalSilkPickReadbackBuffer metalDestination ||
            !ReferenceEquals(metalDestination.Device, Device))
        {
            throw new ArgumentException(
                "The pick readback was not created by this Metal device.",
                nameof(destination));
        }
        metalDestination.ThrowIfUnavailable();
        _containsPickCommands = true;
        _commands.Add(MetalGraphicsCommand.CopyRgba8Pixel(
            metalSource,
            coordinate,
            metalDestination));
    }

    public void SetComputePipeline(ISilkComputePipeline pipeline)
    {
        ThrowIfOutsideRendering();
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not MetalSilkComputePipeline metalPipeline ||
            !ReferenceEquals(metalPipeline.Device, Device))
        {
            throw new ArgumentException(
                "The compute pipeline was not created by this Metal device.",
                nameof(pipeline));
        }
        metalPipeline.ThrowIfDisposed();
        _computePipeline = metalPipeline;
        _commands.Add(MetalGraphicsCommand.SetComputePipeline(metalPipeline));
    }

    public void SetStorageBuffer(
        uint setIndex,
        uint binding,
        ISilkGraphicsBuffer buffer)
    {
        ThrowIfOutsideRendering();
        MetalSilkGraphicsBuffer metalBuffer = ValidateBuffer(buffer);
        if (setIndex != 0 || binding != 0 ||
            !metalBuffer.Usage.HasFlag(SilkBufferUsage.Storage))
        {
            throw new ArgumentException(
                "outputValues requires a storage buffer at set 0, binding 0.",
                nameof(buffer));
        }
        _storageBuffer = metalBuffer;
        _commands.Add(MetalGraphicsCommand.SetStorageBuffer(
            setIndex,
            binding,
            metalBuffer));
    }

    public void SetComputeUniformBuffer(
        uint setIndex,
        uint binding,
        ISilkGraphicsBuffer buffer)
    {
        ThrowIfOutsideRendering();
        MetalSilkGraphicsBuffer metalBuffer = ValidateBuffer(buffer);
        if (setIndex != 0 || binding != 1 ||
            !metalBuffer.Usage.HasFlag(SilkBufferUsage.Uniform) ||
            metalBuffer.Size < SilkCheckedShaderAssets.Compute.D3DUniformByteSize)
        {
            throw new ArgumentException(
                "ComputeParameters requires an 8-byte uniform buffer at set 0, binding 1.",
                nameof(buffer));
        }
        _computeUniformBuffer = metalBuffer;
        _commands.Add(MetalGraphicsCommand.SetComputeUniformBuffer(
            setIndex,
            binding,
            metalBuffer));
    }

    public void Dispatch(uint elementCount)
    {
        ThrowIfOutsideRendering();
        ArgumentOutOfRangeException.ThrowIfZero(elementCount);
        if (_computePipeline is null ||
            _storageBuffer is null ||
            _computeUniformBuffer is null)
        {
            throw new InvalidOperationException(
                "Dispatch requires a compute pipeline, storage buffer, and uniform buffer.");
        }
        if (checked((nuint)elementCount * 16) > _storageBuffer.Size)
        {
            throw new ArgumentOutOfRangeException(
                nameof(elementCount),
                "The storage buffer is too small for the dispatch.");
        }
        _commands.Add(MetalGraphicsCommand.Dispatch(elementCount));
    }

    public void BufferBarrier(ISilkGraphicsBuffer buffer)
    {
        ThrowIfOutsideRendering();
        MetalSilkGraphicsBuffer metalBuffer = ValidateBuffer(buffer);
        if (!metalBuffer.Usage.HasFlag(SilkBufferUsage.Storage))
        {
            throw new ArgumentException(
                "BufferBarrier requires a storage buffer.",
                nameof(buffer));
        }
        _commands.Add(MetalGraphicsCommand.BufferBarrier(metalBuffer));
    }

    public void Dispose()
    {
        _commands.Clear();
        _disposed = true;
    }

    internal void MarkSubmitted()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_submitted)
        {
            throw new InvalidOperationException("The command list was already submitted.");
        }
        if (_rendering)
        {
            throw new InvalidOperationException("EndRendering must be called before submission.");
        }
        _submitted = true;
    }

    private static void ValidateDepth(float depth)
    {
        if (!float.IsFinite(depth) || depth < 0 || depth > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(depth));
        }
    }

    private MetalSilkGraphicsTexture ValidateTexture(ISilkGraphicsTexture texture)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_submitted)
        {
            throw new InvalidOperationException("The command list was already submitted.");
        }
        ArgumentNullException.ThrowIfNull(texture);
        if (texture is not MetalSilkGraphicsTexture metalTexture)
        {
            throw new ArgumentException("The texture is not a Metal texture.", nameof(texture));
        }
        if (!ReferenceEquals(metalTexture.Device, Device))
        {
            throw new ArgumentException(
                "The texture was not created by this Metal device.",
                nameof(texture));
        }
        metalTexture.ThrowIfDisposed();
        return metalTexture;
    }

    private MetalSilkGraphicsBuffer ValidateBuffer(ISilkGraphicsBuffer buffer)
    {
        ThrowIfUnavailable();
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer is not MetalSilkGraphicsBuffer metalBuffer ||
            !ReferenceEquals(metalBuffer.Device, Device))
        {
            throw new ArgumentException("The buffer is not a Metal buffer.", nameof(buffer));
        }
        metalBuffer.ThrowIfDisposed();
        return metalBuffer;
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_submitted)
        {
            throw new InvalidOperationException("The command list was already submitted.");
        }
    }

    private void ThrowIfRendering()
    {
        ThrowIfUnavailable();
        if (!_rendering)
        {
            throw new InvalidOperationException("No rendering scope is active.");
        }
    }

    private void ThrowIfOutsideRendering()
    {
        ThrowIfUnavailable();
        if (_rendering)
        {
            throw new InvalidOperationException(
                "Upload and clear commands cannot be recorded inside a rendering scope.");
        }
    }
}

internal readonly record struct MetalGraphicsCommand(
    SilkGraphicsCommandKind Kind,
    MetalPickCommandKind PickKind,
    MetalSelectionCommandKind SelectionKind,
    MetalSilkGraphicsTexture? Texture,
    MetalSilkGraphicsTexture? DepthTexture,
    MetalSilkGraphicsPipeline? Pipeline,
    MetalSilkPickGraphicsPipeline? PickPipeline,
    MetalSilkSelectionMaskGraphicsPipeline? SelectionMaskPipeline,
    MetalSilkSelectionOutlineGraphicsPipeline? SelectionOutlinePipeline,
    MetalSilkSelectionOutlineBinding? SelectionOutlineBinding,
    MetalSilkComputePipeline? ComputePipeline,
    MetalSilkGraphicsBuffer? Buffer,
    MetalSilkPickReadbackBuffer? PickReadback,
    SilkColor Color,
    float Depth,
    byte[]? Data,
    SilkViewport Viewport,
    SilkScissor Scissor,
    SilkTexturePixelCoordinate PickCoordinate,
    uint SetIndex,
    uint Binding,
    uint PickBaseToken,
    uint IndexCount,
    uint ElementCount)
{
    internal static MetalGraphicsCommand Upload(
        MetalSilkGraphicsTexture texture,
        byte[] data) =>
        Create(
            SilkGraphicsCommandKind.UploadTexture,
            texture: texture,
            data: data);

    internal static MetalGraphicsCommand ClearColor(
        MetalSilkGraphicsTexture texture,
        SilkColor color) =>
        Create(
            SilkGraphicsCommandKind.ClearColor,
            texture: texture,
            color: color);

    internal static MetalGraphicsCommand ClearDepth(
        MetalSilkGraphicsTexture texture,
        float depth) =>
        Create(
            SilkGraphicsCommandKind.ClearDepth,
            texture: texture,
            depth: depth);

    internal static MetalGraphicsCommand BeginRendering(
        MetalSilkGraphicsTexture color,
        MetalSilkGraphicsTexture depth) =>
        Create(
            SilkGraphicsCommandKind.BeginRendering,
            texture: color,
            depthTexture: depth);

    internal static MetalGraphicsCommand SetPipeline(
        MetalSilkGraphicsPipeline pipeline) =>
        Create(
            SilkGraphicsCommandKind.SetGraphicsPipeline,
            pipeline: pipeline);

    internal static MetalGraphicsCommand SetPickPipeline(
        MetalSilkPickGraphicsPipeline pipeline) =>
        Create(
            SilkGraphicsCommandKind.SetGraphicsPipeline,
            pickKind: MetalPickCommandKind.SetGraphicsPipeline,
            pickPipeline: pipeline);

    internal static MetalGraphicsCommand SetPickBaseToken(uint baseToken) =>
        Create(
            SilkGraphicsCommandKind.SetUniformBuffer,
            pickKind: MetalPickCommandKind.SetBaseToken,
            pickBaseToken: baseToken);

    internal static MetalGraphicsCommand SetViewport(SilkViewport viewport) =>
        Create(SilkGraphicsCommandKind.SetViewport, viewport: viewport);

    internal static MetalGraphicsCommand SetScissor(SilkScissor scissor) =>
        Create(SilkGraphicsCommandKind.SetScissor, scissor: scissor);

    internal static MetalGraphicsCommand SetVertexBuffer(
        MetalSilkGraphicsBuffer buffer) =>
        Create(SilkGraphicsCommandKind.SetVertexBuffer, buffer: buffer);

    internal static MetalGraphicsCommand SetIndexBuffer(
        MetalSilkGraphicsBuffer buffer) =>
        Create(SilkGraphicsCommandKind.SetIndexBuffer, buffer: buffer);

    internal static MetalGraphicsCommand SetUniformBuffer(
        uint setIndex,
        uint binding,
        MetalSilkGraphicsBuffer buffer) =>
        Create(
            SilkGraphicsCommandKind.SetUniformBuffer,
            buffer: buffer,
            setIndex: setIndex,
            binding: binding);

    internal static MetalGraphicsCommand DrawIndexed(uint indexCount) =>
        Create(SilkGraphicsCommandKind.DrawIndexed, indexCount: indexCount);

    internal static MetalGraphicsCommand EndRendering() =>
        Create(SilkGraphicsCommandKind.EndRendering);

    internal static MetalGraphicsCommand BeginSelectionMaskRendering(
        MetalSilkGraphicsTexture mask,
        MetalSilkGraphicsTexture depth) =>
        Create(
            SilkGraphicsCommandKind.BeginSelectionMaskRendering,
            selectionKind: MetalSelectionCommandKind.BeginMaskRendering,
            texture: mask,
            depthTexture: depth);

    internal static MetalGraphicsCommand SetSelectionMaskPipeline(
        MetalSilkSelectionMaskGraphicsPipeline pipeline) =>
        Create(
            SilkGraphicsCommandKind.SetSelectionMaskPipeline,
            selectionKind: MetalSelectionCommandKind.SetMaskPipeline,
            selectionMaskPipeline: pipeline);

    internal static MetalGraphicsCommand BeginSelectionOutlineRendering(
        MetalSilkGraphicsTexture color) =>
        Create(
            SilkGraphicsCommandKind.BeginSelectionOutlineRendering,
            selectionKind: MetalSelectionCommandKind.BeginOutlineRendering,
            texture: color);

    internal static MetalGraphicsCommand SetSelectionOutlinePipeline(
        MetalSilkSelectionOutlineGraphicsPipeline pipeline) =>
        Create(
            SilkGraphicsCommandKind.SetSelectionOutlinePipeline,
            selectionKind: MetalSelectionCommandKind.SetOutlinePipeline,
            selectionOutlinePipeline: pipeline);

    internal static MetalGraphicsCommand SetSelectionOutlineBinding(
        MetalSilkSelectionOutlineBinding binding) =>
        Create(
            SilkGraphicsCommandKind.SetSelectionOutlineBinding,
            selectionKind: MetalSelectionCommandKind.SetOutlineBinding,
            selectionOutlineBinding: binding);

    internal static MetalGraphicsCommand DrawSelectionOutlineFullscreenTriangle() =>
        Create(
            SilkGraphicsCommandKind.DrawSelectionOutlineFullscreenTriangle,
            selectionKind: MetalSelectionCommandKind.DrawFullscreenTriangle);

    internal static MetalGraphicsCommand CopyRgba8Pixel(
        MetalSilkGraphicsTexture source,
        SilkTexturePixelCoordinate coordinate,
        MetalSilkPickReadbackBuffer destination) =>
        Create(
            SilkGraphicsCommandKind.UploadTexture,
            pickKind: MetalPickCommandKind.CopyRgba8Pixel,
            texture: source,
            pickReadback: destination,
            pickCoordinate: coordinate);

    internal static MetalGraphicsCommand SetComputePipeline(
        MetalSilkComputePipeline pipeline) =>
        Create(
            SilkGraphicsCommandKind.SetComputePipeline,
            computePipeline: pipeline);

    internal static MetalGraphicsCommand SetStorageBuffer(
        uint setIndex,
        uint binding,
        MetalSilkGraphicsBuffer buffer) =>
        Create(
            SilkGraphicsCommandKind.SetStorageBuffer,
            buffer: buffer,
            setIndex: setIndex,
            binding: binding);

    internal static MetalGraphicsCommand SetComputeUniformBuffer(
        uint setIndex,
        uint binding,
        MetalSilkGraphicsBuffer buffer) =>
        Create(
            SilkGraphicsCommandKind.SetComputeUniformBuffer,
            buffer: buffer,
            setIndex: setIndex,
            binding: binding);

    internal static MetalGraphicsCommand Dispatch(uint elementCount) =>
        Create(SilkGraphicsCommandKind.Dispatch, elementCount: elementCount);

    internal static MetalGraphicsCommand BufferBarrier(
        MetalSilkGraphicsBuffer buffer) =>
        Create(SilkGraphicsCommandKind.BufferBarrier, buffer: buffer);

    private static MetalGraphicsCommand Create(
        SilkGraphicsCommandKind kind,
        MetalPickCommandKind pickKind = MetalPickCommandKind.None,
        MetalSelectionCommandKind selectionKind = MetalSelectionCommandKind.None,
        MetalSilkGraphicsTexture? texture = null,
        MetalSilkGraphicsTexture? depthTexture = null,
        MetalSilkGraphicsPipeline? pipeline = null,
        MetalSilkPickGraphicsPipeline? pickPipeline = null,
        MetalSilkSelectionMaskGraphicsPipeline? selectionMaskPipeline = null,
        MetalSilkSelectionOutlineGraphicsPipeline? selectionOutlinePipeline = null,
        MetalSilkSelectionOutlineBinding? selectionOutlineBinding = null,
        MetalSilkComputePipeline? computePipeline = null,
        MetalSilkGraphicsBuffer? buffer = null,
        MetalSilkPickReadbackBuffer? pickReadback = null,
        SilkColor color = default,
        float depth = 0,
        byte[]? data = null,
        SilkViewport viewport = default,
        SilkScissor scissor = default,
        SilkTexturePixelCoordinate pickCoordinate = default,
        uint setIndex = 0,
        uint binding = 0,
        uint pickBaseToken = 0,
        uint indexCount = 0,
        uint elementCount = 0) =>
        new(
            kind,
            pickKind,
            selectionKind,
            texture,
            depthTexture,
            pipeline,
            pickPipeline,
            selectionMaskPipeline,
            selectionOutlinePipeline,
            selectionOutlineBinding,
            computePipeline,
            buffer,
            pickReadback,
            color,
            depth,
            data,
            viewport,
            scissor,
            pickCoordinate,
            setIndex,
            binding,
            pickBaseToken,
            indexCount,
            elementCount);
}

internal enum MetalPickCommandKind
{
    None,
    SetGraphicsPipeline,
    SetBaseToken,
    CopyRgba8Pixel
}

internal enum MetalSelectionCommandKind
{
    None,
    BeginMaskRendering,
    SetMaskPipeline,
    BeginOutlineRendering,
    SetOutlinePipeline,
    SetOutlineBinding,
    DrawFullscreenTriangle
}

[SupportedOSPlatform("macos")]
internal sealed class MetalSilkGraphicsSubmission(
    MetalSilkGraphicsDevice device,
    MTLCommandBuffer commandBuffer,
    MetalSubmissionCompletion completion,
    IDisposable[] leases,
    MTLBuffer[] uploadBuffers)
    : ISilkGraphicsSubmission
{
    private readonly MetalSilkGraphicsDevice _device = device;
    private readonly MetalSubmissionCompletion _completion = completion;
    private MTLCommandBuffer _commandBuffer = commandBuffer;
    private IDisposable[]? _leases = leases;
    private MTLBuffer[]? _uploadBuffers = uploadBuffers;
    private bool _disposed;

    public bool IsCompleted
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_completion.TryGetCompletion(out bool failed))
            {
                return false;
            }
            ReleaseLeases();
            if (failed)
            {
                throw new InvalidOperationException("Metal command buffer execution failed.");
            }
            return true;
        }
    }

    public void Wait()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        bool failed = _completion.WaitAndGetFailure();
        ReleaseLeases();
        if (failed)
        {
            throw new InvalidOperationException("Metal command buffer execution failed.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _ = _completion.WaitAndGetFailure();
        ReleaseLeases();
        _commandBuffer.Dispose();
        _disposed = true;
        _device.ReleaseDependentObject();
    }

    private void ReleaseLeases()
    {
        IDisposable[]? leases = Interlocked.Exchange(ref _leases, null);
        if (leases is null)
        {
            return;
        }
        foreach (IDisposable lease in leases)
        {
            lease.Dispose();
        }
        MTLBuffer[]? uploads = Interlocked.Exchange(ref _uploadBuffers, null);
        if (uploads is null)
        {
            return;
        }
        foreach (MTLBuffer upload in uploads)
        {
            upload.Dispose();
        }
    }
}

[SupportedOSPlatform("macos")]
internal sealed class MetalSubmissionCompletion(
    MetalSilkGraphicsDevice device,
    MTLCommandBuffer commandBuffer,
    bool invalidatesPickGeneration,
    bool invalidatesSelectionOutlineGeneration)
{
    private readonly MetalSilkGraphicsDevice _device = device;
    private readonly object _gate = new();
    private readonly MTLCommandBuffer _commandBuffer = commandBuffer;
    private readonly bool _invalidatesPickGeneration = invalidatesPickGeneration;
    private readonly bool _invalidatesSelectionOutlineGeneration =
        invalidatesSelectionOutlineGeneration;
    private bool _completed;
    private bool _failed;
    private bool _failureNotified;

    internal bool TryGetCompletion(out bool failed)
    {
        lock (_gate)
        {
            if (!_completed)
            {
                MTLCommandBufferStatus status = _commandBuffer.Status;
                if (status != MTLCommandBufferStatus.Completed &&
                    status != MTLCommandBufferStatus.Error)
                {
                    failed = false;
                    return false;
                }
                _completed = true;
                _failed = status == MTLCommandBufferStatus.Error;
                NotifyFailure();
            }
            failed = _failed;
            return true;
        }
    }

    internal bool WaitAndGetFailure()
    {
        lock (_gate)
        {
            if (!_completed)
            {
                _commandBuffer.WaitUntilCompleted();
                _failed = _commandBuffer.Status == MTLCommandBufferStatus.Error;
                _completed = true;
                NotifyFailure();
            }
            return _failed;
        }
    }

    private void NotifyFailure()
    {
        if (_failed && _invalidatesPickGeneration && !_failureNotified)
        {
            _device.NotifyCommandBufferFailure();
        }
        if (_failed &&
            _invalidatesSelectionOutlineGeneration &&
            !_failureNotified)
        {
            _device.NotifySelectionOutlineCommandBufferFailure();
        }
        if (_failed && !_failureNotified)
        {
            _failureNotified = true;
        }
    }
}
