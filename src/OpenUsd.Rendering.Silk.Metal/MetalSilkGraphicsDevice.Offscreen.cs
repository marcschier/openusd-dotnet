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
                GetNativeFormat(descriptor.Format),
                descriptor.Width,
                descriptor.Height,
                descriptor.MipLevelCount > 1);
            nativeDescriptor.MipmapLevelCount = descriptor.MipLevelCount;
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

    private static MTLPixelFormat GetNativeFormat(SilkTextureFormat format) =>
        format switch
        {
            SilkTextureFormat.Rgba8Unorm => MTLPixelFormat.RGBA8Unorm,
            SilkTextureFormat.D32Float => MTLPixelFormat.Depth32Float,
            SilkTextureFormat.R32Float => MTLPixelFormat.R32Float,
            SilkTextureFormat.Rgba16Float => MTLPixelFormat.RGBA16Float,
            SilkTextureFormat.Rgba32Float => MTLPixelFormat.RGBA32Float,
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

    /// <summary>Allocates the single R32Float 3D texture a sampled density volume samples.</summary>
    /// <remarks>
    /// Explicit, and restricted to <see cref="SilkTextureFormat.R32Float"/> with exactly one
    /// mip level, because the only consumer is the sampled-density-volume path: the native
    /// shim publishes one bulk R32 density cache per <c>UsdVolOpenVDBAsset</c>, and the
    /// checked <c>mesh.volume.fragment</c> program is the only mesh fragment binary that
    /// declares a <c>texture3d&lt;float&gt;</c>. Accepting any other format here would
    /// allocate a texture no checked program can sample. <see cref="MTLStorageMode.Shared"/>
    /// matches <see cref="CreateTexture2D(SilkTextureDescriptor)"/>, so the density grid
    /// obeys the same storage rules as every other texture this device hands out.
    /// </remarks>
    ISilkGraphicsTexture ISilkVolumeTextureGraphicsDevice.CreateTexture3D(
        uint width,
        uint height,
        uint depth,
        SilkTextureFormat format)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);
        ArgumentOutOfRangeException.ThrowIfZero(depth);
        if (format != SilkTextureFormat.R32Float)
        {
            throw new ArgumentException("Volume textures currently require R32Float.", nameof(format));
        }
        RegisterDependentObject();

        var descriptor = new SilkTextureDescriptor(
            width,
            height,
            format,
            SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination);
        MTLTextureDescriptor nativeDescriptor = default;
        MTLTexture texture = default;
        bool success = false;
        try
        {
            nativeDescriptor = new MTLTextureDescriptor
            {
                TextureType = MTLTextureType.Type3D,
                PixelFormat = MTLPixelFormat.R32Float,
                Width = width,
                Height = height,
                Depth = depth,
                MipmapLevelCount = 1,
                SampleCount = 1,
                ArrayLength = 1,
                StorageMode = MTLStorageMode.Shared,
                Usage = MTLTextureUsage.ShaderRead
            };
            texture = _device.NewTexture(nativeDescriptor);
            if (texture.NativePtr == 0)
            {
                throw new InvalidOperationException("Could not create a Metal 3D texture.");
            }
            success = true;
            return new MetalSilkGraphicsTexture(this, texture, descriptor, depth, isVolume: true);
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
        descriptor.Validate(Capabilities);
        RegisterDependentObject();
        MTLSamplerDescriptor nativeDescriptor = default;
        MTLSamplerState sampler = default;
        bool success = false;
        try
        {
            bool useAnisotropy = descriptor.MaxAnisotropy > 1f;
            nativeDescriptor = new MTLSamplerDescriptor
            {
                MinFilter = GetFilter(descriptor.MinFilter),
                MagFilter = GetFilter(descriptor.MagFilter),
                // Metal defaults to NotMipmapped, so mirror the minification filter
                // explicitly while preserving nearest-only float texture sampling.
                MipFilter = descriptor.MinFilter == SilkSamplerFilter.Linear
                    ? MTLSamplerMipFilter.Linear
                    : MTLSamplerMipFilter.Nearest,
                SAddressMode = GetAddressMode(descriptor.AddressU),
                TAddressMode = GetAddressMode(descriptor.AddressV),
                RAddressMode = GetAddressMode(descriptor.AddressW),
                MaxAnisotropy = useAnisotropy
                    ? (ulong)MathF.Round(descriptor.MaxAnisotropy)
                    : 1UL
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
        HashSet<MetalSilkDisplayTransformGraphicsPipeline>?
            leasedDisplayTransformPipelines =
                commands.ContainsDisplayTransformCommands ? [] : null;
        HashSet<MetalSilkDisplayTransformBinding>? leasedDisplayTransformBindings =
            commands.ContainsDisplayTransformCommands ? [] : null;
        var leasedComputePipelines = new HashSet<MetalSilkComputePipeline>();
        var leasedBuffers = new HashSet<MetalSilkGraphicsBuffer>();
        var leasedSamplers = new HashSet<MetalSilkGraphicsSampler>();
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
                if (command.DisplayTransformPipeline is { } dtPipeline &&
                    leasedDisplayTransformPipelines!.Add(dtPipeline))
                {
                    leases.Add(dtPipeline.AcquireLease());
                }
                if (command.DisplayTransformBinding is { } dtBinding &&
                    leasedDisplayTransformBindings!.Add(dtBinding))
                {
                    leases.Add(dtBinding.AcquireLease());
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
                if (command.Sampler is { } materialSampler &&
                    leasedSamplers.Add(materialSampler))
                {
                    leases.Add(materialSampler.AcquireLease());
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
            MetalSilkDisplayTransformGraphicsPipeline?
                currentDisplayTransformPipeline = null;
            MetalSilkDisplayTransformBinding? currentDisplayTransformBinding = null;
            MetalSilkGraphicsBuffer? vertexBuffer = null;
            MetalSilkGraphicsBuffer? indexBuffer = null;
            MetalSilkGraphicsBuffer? uniformBuffer = null;
            uint? pickBaseToken = null;
            MetalSilkComputePipeline? computePipeline = null;
            // Compute bindings are keyed by their declared coordinate rather
            // than held in one field, because a generalized layout binds
            // several buffers and a dispatch resolves each by the slot its own
            // layout declares.
            Dictionary<(uint Set, uint Binding), MetalSilkGraphicsBuffer> computeBuffers = [];
            MetalSilkGraphicsBuffer? storageBuffer = null;
            MetalSilkGraphicsBuffer? computeUniformBuffer = null;
            List<MetalMaterialBinding> materialBindings = [];
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
                if (command.DisplayTransformKind != MetalDisplayTransformCommandKind.None)
                {
                    switch (command.DisplayTransformKind)
                    {
                        case MetalDisplayTransformCommandKind.BeginRendering:
                            colorAttachment = command.Texture!;
                            colorAttachment.ThrowIfDisposed();
                            currentDisplayTransformPipeline = null;
                            currentDisplayTransformBinding = null;
                            rendering = true;
                            break;
                        case MetalDisplayTransformCommandKind.SetPipeline:
                            currentDisplayTransformPipeline =
                                command.DisplayTransformPipeline!;
                            currentDisplayTransformPipeline.ThrowIfUnavailable();
                            currentPipeline = null;
                            currentPickPipeline = null;
                            currentSelectionMaskPipeline = null;
                            currentSelectionOutlinePipeline = null;
                            currentSelectionOutlineBinding = null;
                            break;
                        case MetalDisplayTransformCommandKind.SetBinding:
                            currentDisplayTransformBinding =
                                command.DisplayTransformBinding!;
                            currentDisplayTransformBinding.ThrowIfUnavailable();
                            break;
                        case MetalDisplayTransformCommandKind.DrawFullscreenTriangle:
                            if (!rendering || colorAttachment is null ||
                                currentDisplayTransformPipeline is null ||
                                currentDisplayTransformBinding is null ||
                                currentViewport is null ||
                                currentScissor is null)
                            {
                                throw new InvalidOperationException(
                                    "The ordered Metal display-transform stream has " +
                                    "incomplete fullscreen state.");
                            }
                            EncodeDisplayTransformDraw(
                                commandBuffer,
                                colorAttachment,
                                currentDisplayTransformPipeline,
                                currentDisplayTransformBinding,
                                currentViewport.Value,
                                currentScissor.Value);
                            break;
                        default:
                            throw new InvalidOperationException(
                                "Unknown Metal display-transform command.");
                    }
                    continue;
                }
                switch (command.Kind)
                {
                    case SilkGraphicsCommandKind.UploadTexture:
                        MetalSilkGraphicsTexture uploadTexture = command.Texture!;
                        uploadTexture.ThrowIfDisposed();
                        MetalMipCopyPlan[] uploadPlans = MetalMipCopyPlan.Create(
                            uploadTexture.Width,
                            uploadTexture.Height,
                            uploadTexture.Format,
                            uploadTexture.MipLevelCount);
                        // Staged rather than uploaded directly: Metal requires the
                        // source offset and row pitch of a buffer-to-texture blit
                        // to be 256-byte aligned, and a tightly packed chain
                        // satisfies neither past the base level.
                        byte[] staged = new byte[
                            checked((int)MetalMipCopyPlan.GetStagingByteSize(uploadPlans))];
                        MetalMipCopyPlan.Stage(
                            uploadPlans,
                            uploadTexture.Width,
                            uploadTexture.Height,
                            uploadTexture.Format,
                            command.Data!,
                            staged);
                        MTLBuffer upload = CreateTextureUpload(staged);
                        uploadBuffers.Add(upload);
                        MTLBlitCommandEncoder blitEncoder =
                            commandBuffer.BlitCommandEncoder();
                        if (blitEncoder.NativePtr == 0)
                        {
                            throw new InvalidOperationException(
                                "Could not create a Metal blit command encoder.");
                        }
                        foreach (MetalMipCopyPlan uploadPlan in uploadPlans)
                        {
                            blitEncoder.CopyFromBuffer(
                                upload,
                                uploadPlan.SourceOffset,
                                uploadPlan.SourceBytesPerRow,
                                uploadPlan.SourceBytesPerImage,
                                new MTLSize
                                {
                                    width = uploadPlan.Width,
                                    height = uploadPlan.Height,
                                    depth = 1
                                },
                                uploadTexture.Texture,
                                0,
                                uploadPlan.DestinationLevel,
                                new MTLOrigin());
                        }
                        blitEncoder.EndEncoding();
                        blitEncoder.Dispose();
                        break;
                    case SilkGraphicsCommandKind.UploadTexture3D:
                        MetalSilkGraphicsTexture uploadVolume = command.Texture!;
                        uploadVolume.ThrowIfDisposed();
                        MTLBuffer volumeUpload = CreateTextureUpload(command.Data!);
                        uploadBuffers.Add(volumeUpload);
                        MTLBlitCommandEncoder volumeEncoder =
                            commandBuffer.BlitCommandEncoder();
                        if (volumeEncoder.NativePtr == 0)
                        {
                            throw new InvalidOperationException(
                                "Could not create a Metal blit command encoder.");
                        }
                        // One copy for the whole grid: the density cache the native shim
                        // publishes is a single tightly packed R32 block with no mip chain,
                        // so bytes-per-image is the full slice stride and the destination
                        // extent is the entire volume.
                        ulong volumeBytesPerRow = checked(
                            (ulong)uploadVolume.Width * sizeof(float));
                        volumeEncoder.CopyFromBuffer(
                            volumeUpload,
                            0,
                            volumeBytesPerRow,
                            checked(volumeBytesPerRow * uploadVolume.Height),
                            new MTLSize
                            {
                                width = uploadVolume.Width,
                                height = uploadVolume.Height,
                                depth = uploadVolume.Depth
                            },
                            uploadVolume.Texture,
                            0,
                            0,
                            new MTLOrigin());
                        volumeEncoder.EndEncoding();
                        volumeEncoder.Dispose();
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
                    case SilkGraphicsCommandKind.SetTexture:
                        command.Texture!.ThrowIfDisposed();
                        RecordMaterialBinding(
                            materialBindings,
                            new MetalMaterialBinding(
                                command.Binding,
                                SilkBindingKind.SampledTexture,
                                command.Texture,
                                null,
                                null));
                        break;
                    case SilkGraphicsCommandKind.SetSampler:
                        command.Sampler!.ThrowIfDisposed();
                        RecordMaterialBinding(
                            materialBindings,
                            new MetalMaterialBinding(
                                command.Binding,
                                SilkBindingKind.Sampler,
                                null,
                                command.Sampler,
                                null));
                        break;
                    case SilkGraphicsCommandKind.DrawIndexed:
                    case SilkGraphicsCommandKind.DrawIndexedInstanced:
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
                                encoder.SetFrontFacingWinding(MTLWinding.CounterClockwise);
                                encoder.SetCullMode(currentPipeline is null
                                    ? MTLCullMode.None
                                    : ToMetalCullMode(currentPipeline.Descriptor.CullMode));
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
                                if (materialBindings.Count != 0 &&
                                    currentPipeline is not null)
                                {
                                    BindMaterialArguments(
                                        encoder,
                                        currentPipeline.BindingLayout,
                                        materialBindings);
                                }

                                // No rasterizer depth bias is set for any pick
                                // pass. Metal's depth bias, like Direct3D's and
                                // Vulkan's, is defined for triangle primitives,
                                // so the edge and point separation comes from
                                // the checked subprim vertex stage's clip-space
                                // offset instead -- the one place every backend
                                // applies it identically.
                                encoder.DrawIndexedPrimitives(
                                    currentPickPipeline is not null
                                        ? currentPickPipeline.Descriptor.PrimitiveTopology switch
                                        {
                                            SilkPickPrimitiveTopology.LineList =>
                                                MTLPrimitiveType.Line,
                                            SilkPickPrimitiveTopology.PointList =>
                                                MTLPrimitiveType.Point,
                                            _ => MTLPrimitiveType.Triangle
                                        }
                                        : currentSelectionMaskPipeline is not null
                                            ? currentSelectionMaskPipeline
                                                .Descriptor.PrimitiveTopology switch
                                            {
                                                SilkSelectionMaskPrimitiveTopology.LineList =>
                                                    MTLPrimitiveType.Line,
                                                SilkSelectionMaskPrimitiveTopology.PointList =>
                                                    MTLPrimitiveType.Point,
                                                _ => MTLPrimitiveType.Triangle
                                            }
                                            : currentPipeline?.Descriptor.TopologyKind switch
                                            {
                                                SilkTopologyKind.LineList => MTLPrimitiveType.Line,
                                                SilkTopologyKind.PointList => MTLPrimitiveType.Point,
                                                _ => MTLPrimitiveType.Triangle
                                            },
                                    command.IndexCount,
                                    MTLIndexType.UInt32,
                                    indexBuffer.Buffer,
                                    0,
                                    command.Kind == SilkGraphicsCommandKind.DrawIndexedInstanced
                                        ? command.ElementCount
                                        : 1);
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
                        currentDisplayTransformPipeline = null;
                        currentDisplayTransformBinding = null;
                        rendering = false;
                        break;
                    case SilkGraphicsCommandKind.SetComputePipeline:
                        computePipeline = command.ComputePipeline!;
                        computePipeline.ThrowIfDisposed();
                        break;
                    case SilkGraphicsCommandKind.SetStorageBuffer:
                        storageBuffer = command.Buffer!;
                        storageBuffer.ThrowIfDisposed();
                        if (rendering && currentPipeline is not null)
                        {
                            RecordMaterialBinding(
                                materialBindings,
                                new MetalMaterialBinding(
                                    command.Binding,
                                    SilkBindingKind.StorageBuffer,
                                    null,
                                    null,
                                    storageBuffer));
                        }
                        else
                        {
                            computeBuffers[(command.SetIndex, command.Binding)] =
                                storageBuffer;
                        }
                        break;
                    case SilkGraphicsCommandKind.SetComputeUniformBuffer:
                        computeUniformBuffer = command.Buffer!;
                        computeUniformBuffer.ThrowIfDisposed();
                        computeBuffers[(command.SetIndex, command.Binding)] =
                            computeUniformBuffer;
                        break;
                    case SilkGraphicsCommandKind.Dispatch:
                        if (computePipeline is null)
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
                        SilkComputeBindingLayoutDescriptor metalLayout =
                            computePipeline.Descriptor.Program.BindingLayout.Descriptor;
                        // Metal has no register classes, so a slot's ordinal is
                        // its MSL buffer index. Slang assigns those indices in
                        // declaration order for a kernel whose Direct3D
                        // registers are declared in the same order, which is
                        // what the checked reflection contract requires of every
                        // compute source in this repository.
                        for (int slot = 0; slot < metalLayout.Slots.Count; slot++)
                        {
                            SilkComputeSlot metalSlot = metalLayout.Slots[slot];
                            if (!computeBuffers.TryGetValue(
                                    (metalSlot.Set, metalSlot.Binding),
                                    out MetalSilkGraphicsBuffer? slotBuffer))
                            {
                                throw new InvalidOperationException(
                                    "The ordered Metal command stream has incomplete " +
                                    "compute state.");
                            }
                            slotBuffer.ThrowIfDisposed();
                            computeEncoder.SetBuffer(
                                slotBuffer.Buffer,
                                0,
                                checked((nuint)slot));
                        }
                        computeEncoder.DispatchThreadgroups(
                            new MTLSize
                            {
                                width = command.GroupCount,
                                height = 1,
                                depth = 1
                            },
                            new MTLSize
                            {
                                width = computePipeline.Descriptor.ThreadGroupSizeX,
                                height = computePipeline.Descriptor.ThreadGroupSizeY,
                                depth = computePipeline.Descriptor.ThreadGroupSizeZ
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
                commands.ContainsSelectionOutlineCommands ||
                commands.ContainsDisplayTransformCommands);
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
                    (commands.ContainsSelectionOutlineCommands ||
                     commands.ContainsDisplayTransformCommands))
                {
                    NotifySelectionOutlineCommandBufferFailure();
                }
            }
        }
    }

    /// <summary>
    /// Replaces any prior binding at the same slot so the last write before a draw
    /// wins, matching how the pipeline and buffer bindings already behave.
    /// </summary>
    private static void RecordMaterialBinding(
        List<MetalMaterialBinding> bindings,
        MetalMaterialBinding binding)
    {
        for (int index = 0; index < bindings.Count; index++)
        {
            if (bindings[index].Binding == binding.Binding)
            {
                bindings[index] = binding;
                return;
            }
        }
        bindings.Add(binding);
    }

    /// <summary>
    /// Binds material slots to Metal fragment argument indices.
    /// </summary>
    /// <remarks>
    /// Tier 2 devices try the persistent argument-buffer table first. The fallback keeps
    /// Metal's separate texture, sampler, and buffer argument tables, so the slot binding
    /// is used directly as the index within its own table. Both paths validate against
    /// the layout so a slot can never be bound to a table it does not belong to.
    /// </remarks>
    private void BindMaterialArguments(
        MTLRenderCommandEncoder encoder,
        SilkBindingLayoutDescriptor layout,
        List<MetalMaterialBinding> bindings)
    {
        // The argument-buffer table encodes textures and samplers only, and it is a
        // fragment-side table: it writes a fragment argument buffer and calls only
        // SetFragmentBuffer. Buffers are therefore bound directly, which is both what
        // the encoder can actually represent and what the vertex stage needs -- the
        // instance table at slot 6 is read by the vertex function as [[buffer(6)]] and
        // would never reach it through a fragment argument buffer.
        List<MetalMaterialBinding> tableBindings = [];
        foreach (MetalMaterialBinding binding in bindings)
        {
            _ = layout.RequireMaterialSlot(0, binding.Binding, binding.Kind);
            if (binding.Kind is SilkBindingKind.StorageBuffer or
                SilkBindingKind.UniformBuffer)
            {
                BindDirectly(encoder, binding);
                continue;
            }

            tableBindings.Add(binding);
        }

        if (tableBindings.Count == 0)
        {
            return;
        }

        if (MaterialDescriptorTables is { } descriptorTables &&
            descriptorTables.TryBind(encoder, layout, tableBindings))
        {
            return;
        }

        foreach (MetalMaterialBinding binding in tableBindings)
        {
            BindDirectly(encoder, binding);
        }
    }

    private static void BindDirectly(
        MTLRenderCommandEncoder encoder,
        MetalMaterialBinding binding)
    {
        if (binding.Kind is SilkBindingKind.StorageBuffer or
            SilkBindingKind.UniformBuffer)
        {
            encoder.SetVertexBuffer(binding.Buffer!.Buffer, 0, binding.Binding);
            encoder.SetFragmentBuffer(binding.Buffer.Buffer, 0, binding.Binding);
            return;
        }
        if (binding.Kind == SilkBindingKind.SampledTexture)
        {
            encoder.SetFragmentTexture(
                binding.Texture!.Texture,
                ToMetalShaderResourceIndex(binding));
            return;
        }
        encoder.SetFragmentSamplerState(
            binding.Sampler!.Sampler,
            ToMetalShaderResourceIndex(binding));
    }

    /// <summary>
    /// Maps an abstract binding onto the Metal argument index the checked shaders use.
    /// </summary>
    /// <remarks>
    /// See <see cref="MetalShaderResourceIndices"/> for the table and its rationale.
    /// </remarks>
    private static uint ToMetalShaderResourceIndex(MetalMaterialBinding binding) =>
        MetalShaderResourceIndices.Map(binding.Kind, binding.Binding);

    private static MTLCullMode ToMetalCullMode(SilkCullMode cullMode) =>
        cullMode switch
        {
            SilkCullMode.None => MTLCullMode.None,
            SilkCullMode.Back => MTLCullMode.Back,
            SilkCullMode.Front => MTLCullMode.Front,
            _ => throw new ArgumentOutOfRangeException(nameof(cullMode))
        };

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

    private static void EncodeDisplayTransformDraw(
        MTLCommandBuffer commandBuffer,
        MetalSilkGraphicsTexture colorAttachment,
        MetalSilkDisplayTransformGraphicsPipeline pipeline,
        MetalSilkDisplayTransformBinding binding,
        SilkViewport viewport,
        SilkScissor scissor)
    {
        var renderDescriptor = new MTLRenderPassDescriptor();
        try
        {
            MTLRenderPassColorAttachmentDescriptor color =
                renderDescriptor.ColorAttachments.Object(0);
            color.Texture = colorAttachment.Texture;
            color.LoadAction = MTLLoadAction.DontCare;
            color.StoreAction = MTLStoreAction.Store;

            MTLRenderCommandEncoder encoder =
                commandBuffer.RenderCommandEncoder(renderDescriptor);
            if (encoder.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    "Could not create a Metal display-transform encoder.");
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
                // Display transform fragment shader ABI (display.transform.fragment.metal):
                // sceneColor [[texture(0)]], displayLut [[texture(1)]],
                // displaySampler [[sampler(0)]], DisplayTransformParameters [[buffer(0)]]
                encoder.SetFragmentTexture(binding.SceneColor.Texture, 0);
                encoder.SetFragmentTexture(binding.Lattice.Texture, 1);
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
            SilkSamplerAddressMode.MirrorRepeat => MTLSamplerAddressMode.MirrorRepeat,
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
        : this(device, texture, descriptor, depth: 1, isVolume: false)
    {
    }

    internal MetalSilkGraphicsTexture(
        MetalSilkGraphicsDevice device,
        MTLTexture texture,
        SilkTextureDescriptor descriptor,
        uint depth,
        bool isVolume)
        : base(descriptor)
    {
        _device = device;
        _texture = texture;
        Depth = depth;
        IsVolume = isVolume;
    }

    internal MetalSilkGraphicsDevice Device => _device;

    /// <summary>Gets the slice count; only a sampled density volume has more than one.</summary>
    internal uint Depth { get; }

    /// <summary>
    /// Gets whether the native texture was created as <see cref="MTLTextureType.Type3D"/>.
    /// </summary>
    /// <remarks>
    /// Tracked separately from <see cref="Depth"/> because a density grid may legitimately
    /// be one slice deep, and a one-slice 3D texture is still not interchangeable with a 2D
    /// one: <c>texture3d&lt;float&gt;</c> and <c>texture2d&lt;float&gt;</c> are distinct
    /// Metal argument types, so binding one where the other is declared is undefined.
    /// </remarks>
    internal bool IsVolume { get; }

    internal MTLTexture Texture => _texture;

    public override void ReadbackForTesting(Span<byte> destination)
    {
        ThrowIfDisposed();
        ThrowIfVolume();
        ValidateReadback(destination.Length);
        Readback(MemoryMarshal.AsBytes(destination));
    }

    public override void ReadbackForTesting(Span<float> destination)
    {
        ThrowIfDisposed();
        ThrowIfVolume();
        ValidateDepthReadback(destination.Length);
        Readback(MemoryMarshal.AsBytes(destination));
    }

    /// <summary>Rejects a readback of a sampled density volume.</summary>
    /// <remarks>
    /// The shared readback validation sizes the destination as <c>Width * Height</c>, and the
    /// copy below reads a single slice, so a 3D texture would quietly hand back its first
    /// slice as though it were the whole grid. Rejecting it before the length check keeps the
    /// diagnostic on the real reason rather than on a byte count.
    /// </remarks>
    private void ThrowIfVolume()
    {
        if (IsVolume)
        {
            throw new InvalidOperationException(
                "Readback of a 3D texture is not supported; the destination covers one slice only.");
        }
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
                checked((ulong)Width * SilkTextureFormats.GetBytesPerPixel(Format)),
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

/// <summary>One resolved material slot binding recorded before a Metal draw.</summary>
internal readonly record struct MetalMaterialBinding(
    uint Binding,
    SilkBindingKind Kind,
    MetalSilkGraphicsTexture? Texture,
    MetalSilkGraphicsSampler? Sampler,
    MetalSilkGraphicsBuffer? Buffer);

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
      ISilkSelectionOutlineGraphicsCommandList,
      ISilkDisplayTransformGraphicsCommandList,
      ISilkVolumeTextureCommandList
{
    private readonly List<MetalGraphicsCommand> _commands = [];
    private MetalSilkGraphicsTexture? _colorAttachment;
    private MetalSilkGraphicsTexture? _depthAttachment;
    private MetalSilkGraphicsPipeline? _pipeline;
    private MetalSilkPickGraphicsPipeline? _pickPipeline;
    private MetalSilkSelectionMaskGraphicsPipeline? _selectionMaskPipeline;
    private MetalSilkSelectionOutlineGraphicsPipeline? _selectionOutlinePipeline;
    private MetalSilkSelectionOutlineBinding? _selectionOutlineBinding;
    private MetalSilkDisplayTransformGraphicsPipeline? _displayTransformPipeline;
    private MetalSilkDisplayTransformBinding? _displayTransformBinding;
    private MetalSilkGraphicsBuffer? _vertexBuffer;
    private MetalSilkGraphicsBuffer? _indexBuffer;
    private MetalSilkGraphicsBuffer? _uniformBuffer;
    private MetalSilkComputePipeline? _computePipeline;
    private MetalSilkGraphicsBuffer? _storageBuffer;
    private MetalSilkGraphicsBuffer? _computeUniformBuffer;
    // One entry per declared compute slot ordinal, so a dispatch can require
    // every slot its layout declares to have been bound.
    private readonly MetalSilkGraphicsBuffer?[] _computeBuffers =
        new MetalSilkGraphicsBuffer?[SilkComputeBindingLayoutDescriptor.MaximumSlots];
    private SilkViewport? _viewport;
    private SilkScissor? _scissor;
    private uint? _pickBaseToken;
    private bool _containsPickCommands;
    private bool _containsSelectionOutlineCommands;
    private bool _containsDisplayTransformCommands;
    private bool _displayTransformRendering;
    private bool _rendering;
    private bool _submitted;
    private bool _disposed;

    internal MetalSilkGraphicsDevice Device { get; } = device;

    internal IReadOnlyList<MetalGraphicsCommand> Commands => _commands;

    internal bool ContainsPickCommands => _containsPickCommands;

    internal bool ContainsSelectionOutlineCommands =>
        _containsSelectionOutlineCommands;

    internal bool ContainsDisplayTransformCommands =>
        _containsDisplayTransformCommands;

    public void UploadTexture(ISilkGraphicsTexture texture, ReadOnlySpan<byte> source)
    {
        ThrowIfOutsideRendering();
        MetalSilkGraphicsTexture metalTexture = ValidateTexture(texture);
        if (metalTexture.Format == SilkTextureFormat.D32Float ||
            !metalTexture.Usage.HasFlag(SilkTextureUsage.CopyDestination))
        {
            throw new InvalidOperationException(
                "UploadTexture requires a color or sampled texture with CopyDestination usage.");
        }
        int requiredLength = SilkMipChainLayout.GetTotalByteSize(
            metalTexture.Width,
            metalTexture.Height,
            metalTexture.Format,
            metalTexture.MipLevelCount);
        if (source.Length != requiredLength)
        {
            throw new ArgumentException(
                $"The source must contain exactly {requiredLength} bytes.",
                nameof(source));
        }
        _commands.Add(MetalGraphicsCommand.Upload(metalTexture, source.ToArray()));
    }

    public void UploadTexture3D(ISilkGraphicsTexture texture, ReadOnlySpan<byte> source)
    {
        ThrowIfOutsideRendering();
        MetalSilkGraphicsTexture metalTexture = ValidateTexture(texture);
        if (!metalTexture.IsVolume ||
            metalTexture.Format != SilkTextureFormat.R32Float ||
            !metalTexture.Usage.HasFlag(SilkTextureUsage.CopyDestination))
        {
            throw new InvalidOperationException(
                "UploadTexture3D requires an R32Float 3D texture with CopyDestination usage.");
        }
        int requiredLength = checked(
            (int)(metalTexture.Width *
            metalTexture.Height *
            metalTexture.Depth *
            sizeof(float)));
        if (source.Length != requiredLength)
        {
            throw new ArgumentException(
                $"The source must contain exactly {requiredLength} bytes.",
                nameof(source));
        }
        _commands.Add(MetalGraphicsCommand.Upload3D(metalTexture, source.ToArray()));
    }

    public void ClearColor(ISilkGraphicsTexture texture, SilkColor color)
    {
        ThrowIfOutsideRendering();
        MetalSilkGraphicsTexture metalTexture = ValidateTexture(texture);
        if (!SilkTextureFormats.IsColorRenderTarget(metalTexture.Format) ||
            !metalTexture.Usage.HasFlag(SilkTextureUsage.ColorRenderTarget))
        {
            throw new InvalidOperationException("ClearColor requires a color render target.");
        }
        if (SilkTextureFormats.IsFloatingPointColor(metalTexture.Format))
        {
            color.ValidateFinite();
        }
        else
        {
            color.Validate();
        }
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
        if (!SilkTextureFormats.IsColorRenderTarget(color.Format) ||
            !color.Usage.HasFlag(SilkTextureUsage.ColorRenderTarget) ||
            depth.Format != SilkTextureFormat.D32Float ||
            !depth.Usage.HasFlag(SilkTextureUsage.DepthRenderTarget) ||
            color.Width != depth.Width ||
            color.Height != depth.Height)
        {
            throw new ArgumentException(
                "Rendering requires matching supported color and D32Float depth attachments.",
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
        pipeline = pipeline is ISilkGraphicsPipelineLease lease
            ? lease.Pipeline
            : pipeline;
        if (pipeline is not MetalSilkGraphicsPipeline metalPipeline ||
            !ReferenceEquals(metalPipeline.Device, Device))
        {
            throw new ArgumentException(
                "The pipeline was not created by this Metal device.",
                nameof(pipeline));
        }
        metalPipeline.ThrowIfDisposed();
        if (_colorAttachment?.Format != metalPipeline.Descriptor.ColorFormat)
        {
            throw new ArgumentException(
                "The pipeline color format does not match the active color attachment.",
                nameof(pipeline));
        }
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

    public void SetTexture(uint setIndex, uint binding, ISilkGraphicsTexture texture)
    {
        ThrowIfRendering();
        MetalSilkGraphicsTexture metalTexture = ValidateTexture(texture);
        RequireMaterialSlot(setIndex, binding, SilkBindingKind.SampledTexture);
        if (!metalTexture.Usage.HasFlag(SilkTextureUsage.Sampled))
        {
            throw new ArgumentException(
                "A sampled-texture slot requires a texture with Sampled usage.",
                nameof(texture));
        }
        _commands.Add(MetalGraphicsCommand.SetTexture(
            setIndex,
            binding,
            metalTexture));
    }

    public void SetSampler(uint setIndex, uint binding, ISilkGraphicsSampler sampler)
    {
        ThrowIfRendering();
        ArgumentNullException.ThrowIfNull(sampler);
        if (sampler is not MetalSilkGraphicsSampler metalSampler ||
            !ReferenceEquals(metalSampler.Device, Device))
        {
            throw new ArgumentException(
                "The sampler must belong to this Metal device.",
                nameof(sampler));
        }
        metalSampler.ThrowIfDisposed();
        RequireMaterialSlot(setIndex, binding, SilkBindingKind.Sampler);
        _commands.Add(MetalGraphicsCommand.SetSampler(
            setIndex,
            binding,
            metalSampler));
    }

    /// <summary>
    /// Requires that the bound pipeline declares a matching material slot, so a
    /// binding that no pipeline can consume is rejected while recording rather than
    /// silently dropped at submission.
    /// </summary>
    private void RequireMaterialSlot(uint setIndex, uint binding, SilkBindingKind kind)
    {
        if (_pipeline is null)
        {
            throw new InvalidOperationException(
                "A material resource can only be bound after a graphics pipeline.");
        }
        _ = _pipeline.BindingLayout.RequireMaterialSlot(setIndex, binding, kind);
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

    public void DrawIndexedInstanced(uint indexCount, uint instanceCount)
    {
        ArgumentOutOfRangeException.ThrowIfZero(instanceCount);
        DrawIndexed(indexCount);
        _commands[^1] = MetalGraphicsCommand.DrawIndexedInstanced(indexCount, instanceCount);
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
        _displayTransformPipeline = null;
        _displayTransformBinding = null;
        _displayTransformRendering = false;
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
        if (_colorAttachment?.Format != metalPipeline.Descriptor.ColorFormat)
        {
            throw new ArgumentException(
                "The selection-outline pipeline format does not match the visible target.",
                nameof(pipeline));
        }
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

    public void BeginDisplayTransformRendering(
        SilkDisplayTransformRenderingDescriptor descriptor)
    {
        ThrowIfUnavailable();
        if (_rendering)
        {
            throw new InvalidOperationException("A rendering scope is already active.");
        }
        descriptor.Validate();
        MetalSilkGraphicsTexture color = ValidateTexture(descriptor.ColorAttachment);
        _colorAttachment = color;
        _depthAttachment = null;
        _selectionMaskPipeline = null;
        _displayTransformRendering = true;
        _rendering = true;
        _containsDisplayTransformCommands = true;
        _commands.Add(MetalGraphicsCommand.BeginDisplayTransformRendering(color));
    }

    public void SetDisplayTransformGraphicsPipeline(
        ISilkDisplayTransformGraphicsPipeline pipeline)
    {
        ThrowIfRendering();
        ArgumentNullException.ThrowIfNull(pipeline);
        if (!_displayTransformRendering ||
            pipeline is not MetalSilkDisplayTransformGraphicsPipeline metalPipeline ||
            !ReferenceEquals(metalPipeline.Device, Device))
        {
            throw new ArgumentException(
                "The display-transform pipeline is not valid for this Metal pass.",
                nameof(pipeline));
        }
        metalPipeline.ThrowIfUnavailable();
        if (_colorAttachment?.Format != metalPipeline.Descriptor.ColorFormat)
        {
            throw new ArgumentException(
                "The display-transform pipeline format does not match the color target.",
                nameof(pipeline));
        }
        _pipeline = null;
        _pickPipeline = null;
        _selectionMaskPipeline = null;
        _selectionOutlinePipeline = null;
        _selectionOutlineBinding = null;
        _displayTransformPipeline = metalPipeline;
        _containsDisplayTransformCommands = true;
        _commands.Add(MetalGraphicsCommand.SetDisplayTransformPipeline(metalPipeline));
    }

    public void SetDisplayTransformBinding(ISilkDisplayTransformBinding binding)
    {
        ThrowIfRendering();
        ArgumentNullException.ThrowIfNull(binding);
        if (!_displayTransformRendering ||
            binding is not MetalSilkDisplayTransformBinding metalBinding ||
            !ReferenceEquals(metalBinding.Device, Device))
        {
            throw new ArgumentException(
                "The display-transform binding is not valid for this Metal pass.",
                nameof(binding));
        }
        metalBinding.ThrowIfUnavailable();
        _displayTransformBinding = metalBinding;
        _containsDisplayTransformCommands = true;
        _commands.Add(MetalGraphicsCommand.SetDisplayTransformBinding(metalBinding));
    }

    public void DrawDisplayTransformFullscreenTriangle()
    {
        ThrowIfRendering();
        if (!_displayTransformRendering ||
            _colorAttachment is null ||
            _displayTransformPipeline is null ||
            _displayTransformBinding is null ||
            _viewport is null ||
            _scissor is null)
        {
            throw new InvalidOperationException(
                "Fullscreen display transform requires color, pipeline, binding, " +
                "viewport, and scissor.");
        }
        _containsDisplayTransformCommands = true;
        _commands.Add(MetalGraphicsCommand.DrawDisplayTransformFullscreenTriangle());
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
        // A new pipeline may declare a different layout, so bindings made for
        // the previous one are dropped rather than reinterpreted against slot
        // ordinals that no longer mean the same thing.
        Array.Clear(_computeBuffers);
        _commands.Add(MetalGraphicsCommand.SetComputePipeline(metalPipeline));
    }

    /// <summary>
    /// The layout a compute binding is validated against: the bound pipeline's
    /// own layout, or the checked two-slot layout when none is bound yet.
    /// </summary>
    private SilkComputeBindingLayoutDescriptor ActiveComputeLayout =>
        _computePipeline?.Descriptor.Program.BindingLayout.Descriptor ??
        SilkComputeBindingLayoutDescriptor.Checked;

    public void SetStorageBuffer(
        uint setIndex,
        uint binding,
        ISilkGraphicsBuffer buffer)
    {
        ThrowIfUnavailable();
        MetalSilkGraphicsBuffer metalBuffer = ValidateBuffer(buffer);
        if (_rendering)
        {
            if (!metalBuffer.Usage.HasFlag(SilkBufferUsage.Storage))
            {
                throw new ArgumentException(
                    "A storage binding requires a storage buffer.",
                    nameof(buffer));
            }
            RequireMaterialSlot(setIndex, binding, SilkBindingKind.StorageBuffer);
            _commands.Add(MetalGraphicsCommand.SetStorageBuffer(
                setIndex,
                binding,
                metalBuffer));
            return;
        }
        SilkComputeBindingLayoutDescriptor layout = ActiveComputeLayout;
        int ordinal = SilkComputeRecording.ResolveStructuredSlot(
            layout,
            setIndex,
            binding,
            metalBuffer.Usage,
            nameof(buffer));
        _computeBuffers[ordinal] = metalBuffer;
        if (layout.Slots[ordinal].Kind == SilkComputeSlotKind.ReadWriteStructured)
        {
            _storageBuffer = metalBuffer;
        }
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
        int ordinal = SilkComputeRecording.ResolveUniformSlot(
            ActiveComputeLayout,
            setIndex,
            binding,
            metalBuffer.Usage,
            metalBuffer.Size,
            SilkCheckedShaderAssets.Compute.D3DUniformByteSize,
            nameof(buffer));
        _computeBuffers[ordinal] = metalBuffer;
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
        uint groupCount = SilkComputeRecording.ValidateDispatch(
            ActiveComputeLayout,
            elementCount,
            _computePipeline.Descriptor.ThreadGroupSizeX,
            ordinal => _computeBuffers[ordinal] is not null,
            _storageBuffer.Size);
        _commands.Add(MetalGraphicsCommand.Dispatch(elementCount, groupCount));
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
    MetalSilkGraphicsSampler? Sampler,
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
    uint ElementCount,
    uint GroupCount,
    MetalDisplayTransformCommandKind DisplayTransformKind,
    MetalSilkDisplayTransformGraphicsPipeline? DisplayTransformPipeline,
    MetalSilkDisplayTransformBinding? DisplayTransformBinding)
{
    internal static MetalGraphicsCommand Upload(
        MetalSilkGraphicsTexture texture,
        byte[] data) =>
        Create(
            SilkGraphicsCommandKind.UploadTexture,
            texture: texture,
            data: data);

    internal static MetalGraphicsCommand Upload3D(
        MetalSilkGraphicsTexture texture,
        byte[] data) =>
        Create(
            SilkGraphicsCommandKind.UploadTexture3D,
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

    internal static MetalGraphicsCommand SetTexture(
        uint setIndex,
        uint binding,
        MetalSilkGraphicsTexture texture) =>
        Create(
            SilkGraphicsCommandKind.SetTexture,
            texture: texture,
            setIndex: setIndex,
            binding: binding);

    internal static MetalGraphicsCommand SetSampler(
        uint setIndex,
        uint binding,
        MetalSilkGraphicsSampler sampler) =>
        Create(
            SilkGraphicsCommandKind.SetSampler,
            sampler: sampler,
            setIndex: setIndex,
            binding: binding);

    internal static MetalGraphicsCommand DrawIndexed(uint indexCount) =>
        Create(SilkGraphicsCommandKind.DrawIndexed, indexCount: indexCount);

    internal static MetalGraphicsCommand DrawIndexedInstanced(uint indexCount, uint instanceCount) =>
        Create(
            SilkGraphicsCommandKind.DrawIndexedInstanced,
            indexCount: indexCount,
            elementCount: instanceCount);

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

    internal static MetalGraphicsCommand BeginDisplayTransformRendering(
        MetalSilkGraphicsTexture color) =>
        Create(
            SilkGraphicsCommandKind.BeginRendering,
            displayTransformKind: MetalDisplayTransformCommandKind.BeginRendering,
            texture: color);

    internal static MetalGraphicsCommand SetDisplayTransformPipeline(
        MetalSilkDisplayTransformGraphicsPipeline pipeline) =>
        Create(
            SilkGraphicsCommandKind.SetGraphicsPipeline,
            displayTransformKind: MetalDisplayTransformCommandKind.SetPipeline,
            displayTransformPipeline: pipeline);

    internal static MetalGraphicsCommand SetDisplayTransformBinding(
        MetalSilkDisplayTransformBinding binding) =>
        Create(
            SilkGraphicsCommandKind.SetSelectionOutlineBinding,
            displayTransformKind: MetalDisplayTransformCommandKind.SetBinding,
            displayTransformBinding: binding);

    internal static MetalGraphicsCommand DrawDisplayTransformFullscreenTriangle() =>
        Create(
            SilkGraphicsCommandKind.DrawSelectionOutlineFullscreenTriangle,
            displayTransformKind: MetalDisplayTransformCommandKind.DrawFullscreenTriangle);

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

    internal static MetalGraphicsCommand Dispatch(uint elementCount, uint groupCount) =>
        Create(
            SilkGraphicsCommandKind.Dispatch,
            elementCount: elementCount,
            groupCount: groupCount);

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
        MetalSilkGraphicsSampler? sampler = null,
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
        uint elementCount = 0,
        uint groupCount = 0,
        MetalDisplayTransformCommandKind displayTransformKind =
            MetalDisplayTransformCommandKind.None,
        MetalSilkDisplayTransformGraphicsPipeline? displayTransformPipeline = null,
        MetalSilkDisplayTransformBinding? displayTransformBinding = null) =>
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
            sampler,
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
            elementCount,
            groupCount,
            displayTransformKind,
            displayTransformPipeline,
            displayTransformBinding);
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

internal enum MetalDisplayTransformCommandKind
{
    None,
    BeginRendering,
    SetPipeline,
    SetBinding,
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

/// <summary>
/// Describes one <c>MTLBlitCommandEncoder.CopyFromBuffer</c> call needed to upload a single mip
/// level from a renderer-neutral packed chain (see <see cref="SilkMipChainLayout"/>). This type
/// holds no native handles and is not <see cref="SupportedOSPlatformAttribute"/>-gated, so it is
/// fully testable without macOS or a Metal device, mirroring the existing
/// <see cref="MetalPickCopyPlan"/> portable-contract pattern for pick copies.
/// </summary>
/// <remarks>
/// <para>
/// The plan describes a *staged* footprint, not the packed source directly. Metal
/// requires the source offset and the source bytes-per-row of a buffer-to-texture
/// blit to be aligned -- 256 bytes on macOS -- and a tightly packed chain
/// satisfies neither past the base level: a 2x2 RGBA8 mip has an eight-byte row
/// pitch and starts at whatever offset the previous level ended at. Encoding the
/// packed layout directly is rejected outright by a validating Metal runtime and
/// is undefined on one that is not validating, which is a class of failure no
/// Windows or Linux test can see.
/// </para>
/// <para>
/// So each level is given a row pitch rounded up to the alignment and an offset
/// that is a multiple of it, and <see cref="Stage"/> re-packs the tightly packed
/// source into that footprint. The staging buffer is larger than the source --
/// for a small mip, much larger -- which is the cost of a correct copy.
/// </para>
/// </remarks>
internal readonly record struct MetalMipCopyPlan(
    ulong SourceOffset,
    ulong SourceBytesPerRow,
    ulong SourceBytesPerImage,
    ulong Width,
    ulong Height,
    ulong DestinationLevel)
{
    /// <summary>
    /// The alignment Metal requires of a buffer-to-texture blit's source offset
    /// and row pitch on macOS.
    /// </summary>
    internal const ulong Alignment = 256;

    /// <summary>
    /// Builds one plan per mip level of <paramref name="mipLevelCount"/>, in ascending level
    /// order, describing the aligned staging footprint the packed source is copied into.
    /// </summary>
    internal static MetalMipCopyPlan[] Create(
        uint baseWidth,
        uint baseHeight,
        SilkTextureFormat format,
        uint mipLevelCount)
    {
        SilkMipLevelLayout[] levels = SilkMipChainLayout.Create(
            baseWidth,
            baseHeight,
            format,
            mipLevelCount);
        var plans = new MetalMipCopyPlan[levels.Length];
        ulong offset = 0;
        for (int index = 0; index < levels.Length; index++)
        {
            SilkMipLevelLayout level = levels[index];
            ulong rowPitch = AlignUp(checked((ulong)level.RowPitch));
            ulong imageSize = checked(rowPitch * level.Height);
            plans[index] = new MetalMipCopyPlan(
                offset,
                rowPitch,
                imageSize,
                level.Width,
                level.Height,
                level.Level);
            offset = checked(AlignUp(offset + imageSize));
        }
        return plans;
    }

    /// <summary>Gets the total staging buffer size one plan set requires.</summary>
    internal static ulong GetStagingByteSize(IReadOnlyList<MetalMipCopyPlan> plans)
    {
        ArgumentNullException.ThrowIfNull(plans);
        if (plans.Count == 0)
        {
            return 0;
        }
        MetalMipCopyPlan last = plans[^1];
        return checked(last.SourceOffset + last.SourceBytesPerImage);
    }

    /// <summary>
    /// Copies a tightly packed mip chain into the aligned staging layout the plans
    /// describe, row by row.
    /// </summary>
    /// <param name="plans">The plans, in ascending level order.</param>
    /// <param name="baseWidth">The base level width in texels.</param>
    /// <param name="baseHeight">The base level height in texels.</param>
    /// <param name="format">The texture format.</param>
    /// <param name="source">The tightly packed chain.</param>
    /// <param name="destination">The staging buffer, at least the size the plans need.</param>
    internal static void Stage(
        IReadOnlyList<MetalMipCopyPlan> plans,
        uint baseWidth,
        uint baseHeight,
        SilkTextureFormat format,
        ReadOnlySpan<byte> source,
        Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(plans);
        SilkMipLevelLayout[] levels = SilkMipChainLayout.Create(
            baseWidth,
            baseHeight,
            format,
            checked((uint)plans.Count));
        for (int index = 0; index < levels.Length; index++)
        {
            SilkMipLevelLayout level = levels[index];
            MetalMipCopyPlan plan = plans[index];
            for (uint row = 0; row < level.Height; row++)
            {
                source
                    .Slice(level.Offset + checked((int)(row * (uint)level.RowPitch)), level.RowPitch)
                    .CopyTo(destination.Slice(
                        checked((int)(plan.SourceOffset + (row * plan.SourceBytesPerRow))),
                        level.RowPitch));
            }
        }
    }

    private static ulong AlignUp(ulong value) =>
        checked((value + Alignment - 1) / Alignment * Alignment);
}
