// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using global::Silk.NET.Vulkan;

namespace OpenUsd.Rendering.Silk.Vulkan;

public sealed unsafe partial class VulkanSilkGraphicsDevice
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

        Format nativeFormat = descriptor.Format == SilkTextureFormat.Rgba8Unorm
            ? Format.R8G8B8A8Unorm
            : Format.D32Sfloat;
        ImageUsageFlags nativeUsage = ImageUsageFlags.TransferSrcBit;
        if (descriptor.Usage.HasFlag(SilkTextureUsage.ColorRenderTarget))
        {
            nativeUsage |= ImageUsageFlags.ColorAttachmentBit |
                ImageUsageFlags.TransferDstBit;
        }
        if (descriptor.Usage.HasFlag(SilkTextureUsage.DepthRenderTarget))
        {
            nativeUsage |= ImageUsageFlags.DepthStencilAttachmentBit |
                ImageUsageFlags.TransferDstBit;
        }
        if (descriptor.Usage.HasFlag(SilkTextureUsage.Sampled))
        {
            nativeUsage |= ImageUsageFlags.SampledBit;
        }
        if (descriptor.Usage.HasFlag(SilkTextureUsage.CopyDestination))
        {
            nativeUsage |= ImageUsageFlags.TransferDstBit;
        }
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = nativeFormat,
            Extent = new Extent3D(descriptor.Width, descriptor.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = nativeUsage,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        Image image = default;
        DeviceMemory memory = default;
        ImageView imageView = default;
        bool success = false;
        try
        {
            ThrowIfFailed(_api.CreateImage(_device, &imageInfo, null, &image), "vkCreateImage");
            _api.GetImageMemoryRequirements(
                _device,
                image,
                out MemoryRequirements requirements);
            var allocationInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = FindMemoryType(
                    requirements.MemoryTypeBits,
                    MemoryPropertyFlags.DeviceLocalBit)
            };
            ThrowIfFailed(
                _api.AllocateMemory(_device, &allocationInfo, null, &memory),
                "vkAllocateMemory");
            ThrowIfFailed(
                _api.BindImageMemory(_device, image, memory, 0),
                "vkBindImageMemory");
            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = image,
                ViewType = ImageViewType.Type2D,
                Format = nativeFormat,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = descriptor.Format == SilkTextureFormat.Rgba8Unorm
                        ? ImageAspectFlags.ColorBit
                        : ImageAspectFlags.DepthBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };
            ThrowIfFailed(
                _api.CreateImageView(_device, &viewInfo, null, &imageView),
                "vkCreateImageView");
            success = true;
            return new VulkanSilkGraphicsTexture(
                this,
                image,
                memory,
                imageView,
                descriptor,
                ownsNativeObjects: true);
        }
        finally
        {
            if (!success && imageView.Handle != 0)
            {
                _api.DestroyImageView(_device, imageView, null);
            }
            if (!success && image.Handle != 0)
            {
                _api.DestroyImage(_device, image, null);
            }
            if (!success && memory.Handle != 0)
            {
                _api.FreeMemory(_device, memory, null);
            }
            if (!success)
            {
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
        Sampler sampler = default;
        bool success = false;
        try
        {
            var createInfo = new SamplerCreateInfo
            {
                SType = StructureType.SamplerCreateInfo,
                MagFilter = GetFilter(descriptor.MagFilter),
                MinFilter = GetFilter(descriptor.MinFilter),
                MipmapMode = SamplerMipmapMode.Nearest,
                AddressModeU = GetAddressMode(descriptor.AddressU),
                AddressModeV = GetAddressMode(descriptor.AddressV),
                AddressModeW = GetAddressMode(descriptor.AddressW),
                MinLod = 0,
                MaxLod = 0,
                MaxAnisotropy = 1,
                BorderColor = BorderColor.FloatTransparentBlack
            };
            ThrowIfFailed(
                _api.CreateSampler(_device, &createInfo, null, &sampler),
                "vkCreateSampler");
            success = true;
            return new VulkanSilkGraphicsSampler(
                this,
                _api,
                _device,
                sampler,
                descriptor);
        }
        finally
        {
            if (!success)
            {
                if (sampler.Handle != 0)
                {
                    _api.DestroySampler(_device, sampler, null);
                }
                ReleaseDependentObject();
            }
        }
    }

    /// <inheritdoc/>
    public ISilkGraphicsCommandList CreateCommandList()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new VulkanSilkGraphicsCommandList(this);
    }

    /// <inheritdoc/>
    public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(commandList);
        if (commandList is not VulkanSilkGraphicsCommandList commands ||
            !ReferenceEquals(commands.Device, this))
        {
            throw new ArgumentException(
                "The command list was not created by this Vulkan device.",
                nameof(commandList));
        }
        commands.MarkSubmitted();
        if (commands.HasPickSubmission)
        {
            return SubmitPick(commands);
        }
        if (commands.HasSelectionOutlineSubmission)
        {
            ThrowIfSelectionOutlineSubmissionFailureForTesting();
        }

        var leases = new List<IDisposable>();
        var uploadResources = new List<VulkanUploadResource>();
        var drawResources = new List<VulkanDrawSubmissionResource>();
        var computeResources = new List<VulkanComputeSubmissionResource>();
        CommandPool pool = default;
        CommandBuffer nativeCommands = default;
        Fence fence = default;
        bool dependentRegistered = false;
        bool success = false;
        try
        {
            var leasedTextures = new HashSet<VulkanSilkGraphicsTexture>();
            var leasedPipelines = new HashSet<VulkanSilkGraphicsPipeline>();
            var leasedComputePipelines = new HashSet<VulkanSilkComputePipeline>();
            var leasedSelectionMaskPipelines =
                new HashSet<VulkanSilkSelectionMaskGraphicsPipeline>();
            var leasedSelectionOutlinePipelines =
                new HashSet<VulkanSilkSelectionOutlineGraphicsPipeline>();
            var leasedSelectionBindings =
                new HashSet<VulkanSilkSelectionOutlineBinding>();
            var leasedBuffers = new HashSet<VulkanSilkGraphicsBuffer>();
            foreach (VulkanGraphicsCommand command in commands.Commands)
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
                if (command.ComputePipeline is { } leasedComputePipeline &&
                    leasedComputePipelines.Add(leasedComputePipeline))
                {
                    leases.Add(leasedComputePipeline.AcquireLease());
                }
                if (command.SelectionMaskPipeline is { } leasedSelectionMaskPipeline &&
                    leasedSelectionMaskPipelines.Add(leasedSelectionMaskPipeline))
                {
                    leases.Add(leasedSelectionMaskPipeline.AcquireLease());
                }
                if (command.SelectionOutlinePipeline is { } leasedSelectionOutlinePipeline &&
                    leasedSelectionOutlinePipelines.Add(leasedSelectionOutlinePipeline))
                {
                    leases.Add(leasedSelectionOutlinePipeline.AcquireLease());
                }
                if (command.SelectionOutlineBinding is { } selectionBinding &&
                    leasedSelectionBindings.Add(selectionBinding))
                {
                    leases.Add(selectionBinding.AcquireLease());
                }
                if (command.Buffer is { } buffer && leasedBuffers.Add(buffer))
                {
                    leases.Add(buffer.AcquireLease());
                }
            }
            RegisterDependentObject();
            dependentRegistered = true;

            CreateCommandBuffer(out pool, out nativeCommands);
            var finalLayouts = new Dictionary<VulkanSilkGraphicsTexture, ImageLayout>();
            VulkanSilkGraphicsTexture? colorAttachment = null;
            VulkanSilkGraphicsTexture? depthAttachment = null;
            VulkanSilkGraphicsPipeline? currentPipeline = null;
            VulkanSilkGraphicsBuffer? vertexBuffer = null;
            VulkanSilkGraphicsBuffer? indexBuffer = null;
            VulkanSilkGraphicsBuffer? uniformBuffer = null;
            VulkanSilkComputePipeline? computePipeline = null;
            VulkanSilkSelectionMaskGraphicsPipeline? selectionMaskPipeline = null;
            VulkanSilkSelectionOutlineGraphicsPipeline? selectionOutlinePipeline = null;
            VulkanSilkSelectionOutlineBinding? selectionOutlineBinding = null;
            VulkanSelectionRenderingScope selectionScope =
                VulkanSelectionRenderingScope.None;
            VulkanSilkGraphicsBuffer? storageBuffer = null;
            VulkanSilkGraphicsBuffer? computeUniformBuffer = null;
            SilkViewport? currentViewport = null;
            SilkScissor? currentScissor = null;
            bool rendering = false;
            foreach (VulkanGraphicsCommand command in commands.Commands)
            {
                switch (command.Kind)
                {
                    case SilkGraphicsCommandKind.UploadTexture:
                        VulkanSilkGraphicsTexture uploadTexture = command.Texture!;
                        uploadTexture.ThrowIfDisposed();
                        VulkanUploadResource upload = CreateTextureUpload(command.Data!);
                        uploadResources.Add(upload);
                        Transition(
                            nativeCommands,
                            uploadTexture.Image,
                            uploadTexture.AspectMask,
                            GetCurrentLayout(finalLayouts, uploadTexture),
                            ImageLayout.TransferDstOptimal);
                        var copyRegion = new BufferImageCopy
                        {
                            ImageSubresource = new ImageSubresourceLayers
                            {
                                AspectMask = ImageAspectFlags.ColorBit,
                                LayerCount = 1
                            },
                            ImageExtent = new Extent3D(
                                uploadTexture.Width,
                                uploadTexture.Height,
                                1)
                        };
                        _api.CmdCopyBufferToImage(
                            nativeCommands,
                            upload.Buffer,
                            uploadTexture.Image,
                            ImageLayout.TransferDstOptimal,
                            1,
                            &copyRegion);
                        Transition(
                            nativeCommands,
                            uploadTexture.Image,
                            uploadTexture.AspectMask,
                            ImageLayout.TransferDstOptimal,
                            ImageLayout.TransferSrcOptimal);
                        finalLayouts[uploadTexture] = ImageLayout.TransferSrcOptimal;
                        break;
                    case SilkGraphicsCommandKind.ClearColor:
                        VulkanSilkGraphicsTexture colorTexture = command.Texture!;
                        colorTexture.ThrowIfDisposed();
                        Transition(
                            nativeCommands,
                            colorTexture.Image,
                            colorTexture.AspectMask,
                            GetCurrentLayout(finalLayouts, colorTexture),
                            ImageLayout.TransferDstOptimal);
                        var clearColor = new ClearColorValue
                        {
                            Float32_0 = command.Color.Red,
                            Float32_1 = command.Color.Green,
                            Float32_2 = command.Color.Blue,
                            Float32_3 = command.Color.Alpha
                        };
                        ImageSubresourceRange colorRange =
                            colorTexture.SubresourceRange;
                        _api.CmdClearColorImage(
                            nativeCommands,
                            colorTexture.Image,
                            ImageLayout.TransferDstOptimal,
                            &clearColor,
                            1,
                            &colorRange);
                        Transition(
                            nativeCommands,
                            colorTexture.Image,
                            colorTexture.AspectMask,
                            ImageLayout.TransferDstOptimal,
                            ImageLayout.TransferSrcOptimal);
                        finalLayouts[colorTexture] = ImageLayout.TransferSrcOptimal;
                        break;
                    case SilkGraphicsCommandKind.ClearDepth:
                        VulkanSilkGraphicsTexture depthTextureToClear = command.Texture!;
                        depthTextureToClear.ThrowIfDisposed();
                        Transition(
                            nativeCommands,
                            depthTextureToClear.Image,
                            depthTextureToClear.AspectMask,
                            GetCurrentLayout(finalLayouts, depthTextureToClear),
                            ImageLayout.TransferDstOptimal);
                        var clearDepth = new ClearDepthStencilValue(command.Depth, 0);
                        ImageSubresourceRange depthRange =
                            depthTextureToClear.SubresourceRange;
                        _api.CmdClearDepthStencilImage(
                            nativeCommands,
                            depthTextureToClear.Image,
                            ImageLayout.TransferDstOptimal,
                            &clearDepth,
                            1,
                            &depthRange);
                        Transition(
                            nativeCommands,
                            depthTextureToClear.Image,
                            depthTextureToClear.AspectMask,
                            ImageLayout.TransferDstOptimal,
                            ImageLayout.TransferSrcOptimal);
                        finalLayouts[depthTextureToClear] = ImageLayout.TransferSrcOptimal;
                        break;
                    case SilkGraphicsCommandKind.BeginRendering:
                        colorAttachment = command.Texture!;
                        depthAttachment = command.DepthTexture!;
                        colorAttachment.ThrowIfDisposed();
                        depthAttachment.ThrowIfDisposed();
                        Transition(
                            nativeCommands,
                            colorAttachment.Image,
                            ImageAspectFlags.ColorBit,
                            GetCurrentLayout(finalLayouts, colorAttachment),
                            ImageLayout.ColorAttachmentOptimal);
                        Transition(
                            nativeCommands,
                            depthAttachment.Image,
                            ImageAspectFlags.DepthBit,
                            GetCurrentLayout(finalLayouts, depthAttachment),
                            ImageLayout.DepthStencilAttachmentOptimal);
                        finalLayouts[colorAttachment] =
                            ImageLayout.ColorAttachmentOptimal;
                        finalLayouts[depthAttachment] =
                            ImageLayout.DepthStencilAttachmentOptimal;
                        rendering = true;
                        selectionScope = VulkanSelectionRenderingScope.None;
                        break;
                    case SilkGraphicsCommandKind.BeginSelectionMaskRendering:
                        colorAttachment = command.Texture!;
                        depthAttachment = command.DepthTexture!;
                        colorAttachment.ThrowIfDisposed();
                        depthAttachment.ThrowIfDisposed();
                        Transition(
                            nativeCommands,
                            colorAttachment.Image,
                            ImageAspectFlags.ColorBit,
                            GetCurrentLayout(finalLayouts, colorAttachment),
                            ImageLayout.ColorAttachmentOptimal);
                        Transition(
                            nativeCommands,
                            depthAttachment.Image,
                            ImageAspectFlags.DepthBit,
                            GetCurrentLayout(finalLayouts, depthAttachment),
                            ImageLayout.DepthStencilReadOnlyOptimal);
                        finalLayouts[colorAttachment] =
                            ImageLayout.ColorAttachmentOptimal;
                        finalLayouts[depthAttachment] =
                            ImageLayout.DepthStencilReadOnlyOptimal;
                        selectionMaskPipeline = null;
                        rendering = true;
                        selectionScope = VulkanSelectionRenderingScope.Mask;
                        break;
                    case SilkGraphicsCommandKind.BeginSelectionOutlineRendering:
                        colorAttachment = command.Texture!;
                        depthAttachment = null;
                        colorAttachment.ThrowIfDisposed();
                        Transition(
                            nativeCommands,
                            colorAttachment.Image,
                            ImageAspectFlags.ColorBit,
                            GetCurrentLayout(finalLayouts, colorAttachment),
                            ImageLayout.ColorAttachmentOptimal);
                        finalLayouts[colorAttachment] =
                            ImageLayout.ColorAttachmentOptimal;
                        selectionOutlinePipeline = null;
                        selectionOutlineBinding = null;
                        rendering = true;
                        selectionScope = VulkanSelectionRenderingScope.Outline;
                        break;
                    case SilkGraphicsCommandKind.SetGraphicsPipeline:
                        currentPipeline = command.Pipeline!;
                        currentPipeline.ThrowIfDisposed();
                        break;
                    case SilkGraphicsCommandKind.SetSelectionMaskPipeline:
                        selectionMaskPipeline = command.SelectionMaskPipeline!;
                        selectionMaskPipeline.ThrowIfDisposed();
                        break;
                    case SilkGraphicsCommandKind.SetSelectionOutlinePipeline:
                        selectionOutlinePipeline =
                            command.SelectionOutlinePipeline!;
                        selectionOutlinePipeline.ThrowIfDisposed();
                        break;
                    case SilkGraphicsCommandKind.SetSelectionOutlineBinding:
                        selectionOutlineBinding =
                            command.SelectionOutlineBinding!;
                        selectionOutlineBinding.ThrowIfDisposed();
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
                        if (selectionScope == VulkanSelectionRenderingScope.Mask)
                        {
                            if (!rendering ||
                                colorAttachment is null ||
                                depthAttachment is null ||
                                selectionMaskPipeline is null ||
                                vertexBuffer is null ||
                                indexBuffer is null ||
                                uniformBuffer is null ||
                                currentViewport is null ||
                                currentScissor is null)
                            {
                                throw new InvalidOperationException(
                                    "The ordered Vulkan selection-mask stream has incomplete draw state.");
                            }
                            RecordSelectionMaskDraw(
                                nativeCommands,
                                selectionMaskPipeline,
                                colorAttachment,
                                depthAttachment,
                                vertexBuffer,
                                indexBuffer,
                                uniformBuffer,
                                currentViewport.Value,
                                currentScissor.Value,
                                command.IndexCount);
                            break;
                        }
                        if (!rendering || colorAttachment is null ||
                            depthAttachment is null || currentPipeline is null ||
                            vertexBuffer is null || indexBuffer is null ||
                            uniformBuffer is null || currentViewport is null ||
                            currentScissor is null)
                        {
                            throw new InvalidOperationException(
                                "The ordered Vulkan command stream has incomplete draw state.");
                        }
                        var drawState = new VulkanDrawState(
                            colorAttachment,
                            depthAttachment,
                            currentPipeline,
                            vertexBuffer,
                            indexBuffer,
                            uniformBuffer,
                            currentViewport.Value,
                            currentScissor.Value,
                            command.IndexCount);
                        VulkanDrawSubmissionResource drawResource =
                            CreateDrawSubmissionResource(drawState);
                        drawResources.Add(drawResource);
                        var beginInfo = new RenderPassBeginInfo
                        {
                            SType = StructureType.RenderPassBeginInfo,
                            RenderPass = currentPipeline.RenderPass,
                            Framebuffer = drawResource.Framebuffer,
                            RenderArea = new Rect2D(
                                new Offset2D(0, 0),
                                new Extent2D(
                                    colorAttachment.Width,
                                    colorAttachment.Height))
                        };
                        _api.CmdBeginRenderPass(
                            nativeCommands,
                            &beginInfo,
                            SubpassContents.Inline);
                        _api.CmdBindPipeline(
                            nativeCommands,
                            PipelineBindPoint.Graphics,
                            currentPipeline.Pipeline);
                        var viewport = new global::Silk.NET.Vulkan.Viewport(
                            currentViewport.Value.X,
                            currentViewport.Value.Y,
                            currentViewport.Value.Width,
                            currentViewport.Value.Height,
                            currentViewport.Value.MinDepth,
                            currentViewport.Value.MaxDepth);
                        _api.CmdSetViewport(nativeCommands, 0, 1, &viewport);
                        var scissor = new Rect2D(
                            new Offset2D(
                                currentScissor.Value.X,
                                currentScissor.Value.Y),
                            new Extent2D(
                                currentScissor.Value.Width,
                                currentScissor.Value.Height));
                        _api.CmdSetScissor(nativeCommands, 0, 1, &scissor);
                        global::Silk.NET.Vulkan.Buffer nativeVertexBuffer =
                            vertexBuffer.Buffer;
                        ulong vertexOffset = 0;
                        _api.CmdBindVertexBuffers(
                            nativeCommands,
                            0,
                            1,
                            &nativeVertexBuffer,
                            &vertexOffset);
                        _api.CmdBindIndexBuffer(
                            nativeCommands,
                            indexBuffer.Buffer,
                            0,
                            IndexType.Uint16);
                        DescriptorSet descriptorSet = drawResource.DescriptorSet;
                        _api.CmdBindDescriptorSets(
                            nativeCommands,
                            PipelineBindPoint.Graphics,
                            currentPipeline.Layout,
                            0,
                            1,
                            &descriptorSet,
                            0,
                            null);
                        _api.CmdDrawIndexed(
                            nativeCommands,
                            command.IndexCount,
                            1,
                            0,
                            0,
                            0);
                        _api.CmdEndRenderPass(nativeCommands);
                        break;
                    case SilkGraphicsCommandKind.DrawSelectionOutlineFullscreenTriangle:
                        if (!rendering ||
                            selectionScope != VulkanSelectionRenderingScope.Outline ||
                            colorAttachment is null ||
                            selectionOutlinePipeline is null ||
                            selectionOutlineBinding is null ||
                            currentViewport is null ||
                            currentScissor is null)
                        {
                            throw new InvalidOperationException(
                                "The ordered Vulkan selection-outline stream has incomplete draw state.");
                        }
                        Transition(
                            nativeCommands,
                            selectionOutlineBinding.Mask.Image,
                            ImageAspectFlags.ColorBit,
                            GetCurrentLayout(
                                finalLayouts,
                                selectionOutlineBinding.Mask),
                            ImageLayout.ShaderReadOnlyOptimal);
                        Transition(
                            nativeCommands,
                            selectionOutlineBinding.Depth.Image,
                            ImageAspectFlags.DepthBit,
                            GetCurrentLayout(
                                finalLayouts,
                                selectionOutlineBinding.Depth),
                            ImageLayout.DepthStencilReadOnlyOptimal);
                        finalLayouts[selectionOutlineBinding.Mask] =
                            ImageLayout.ShaderReadOnlyOptimal;
                        finalLayouts[selectionOutlineBinding.Depth] =
                            ImageLayout.DepthStencilReadOnlyOptimal;
                        RecordSelectionOutlineDraw(
                            nativeCommands,
                            selectionOutlinePipeline,
                            selectionOutlineBinding,
                            colorAttachment,
                            currentViewport.Value,
                            currentScissor.Value);
                        break;
                    case SilkGraphicsCommandKind.EndRendering:
                        if (!rendering || colorAttachment is null)
                        {
                            throw new InvalidOperationException(
                                "The ordered Vulkan command stream ended no rendering scope.");
                        }
                        if (selectionScope == VulkanSelectionRenderingScope.Mask)
                        {
                            if (depthAttachment is null)
                            {
                                throw new InvalidOperationException(
                                    "The Vulkan selection-mask scope lost its depth attachment.");
                            }
                            Transition(
                                nativeCommands,
                                colorAttachment.Image,
                                ImageAspectFlags.ColorBit,
                                ImageLayout.ColorAttachmentOptimal,
                                ImageLayout.ShaderReadOnlyOptimal);
                            finalLayouts[colorAttachment] =
                                ImageLayout.ShaderReadOnlyOptimal;
                            finalLayouts[depthAttachment] =
                                ImageLayout.DepthStencilReadOnlyOptimal;
                        }
                        else if (selectionScope ==
                                 VulkanSelectionRenderingScope.Outline)
                        {
                            Transition(
                                nativeCommands,
                                colorAttachment.Image,
                                ImageAspectFlags.ColorBit,
                                ImageLayout.ColorAttachmentOptimal,
                                ImageLayout.TransferSrcOptimal);
                            finalLayouts[colorAttachment] =
                                ImageLayout.TransferSrcOptimal;
                        }
                        else
                        {
                            if (depthAttachment is null)
                            {
                                throw new InvalidOperationException(
                                    "The Vulkan rendering scope lost its depth attachment.");
                            }
                            Transition(
                                nativeCommands,
                                colorAttachment.Image,
                                ImageAspectFlags.ColorBit,
                                ImageLayout.ColorAttachmentOptimal,
                                ImageLayout.TransferSrcOptimal);
                            Transition(
                                nativeCommands,
                                depthAttachment.Image,
                                ImageAspectFlags.DepthBit,
                                ImageLayout.DepthStencilAttachmentOptimal,
                                ImageLayout.TransferSrcOptimal);
                            finalLayouts[colorAttachment] =
                                ImageLayout.TransferSrcOptimal;
                            finalLayouts[depthAttachment] =
                                ImageLayout.TransferSrcOptimal;
                        }
                        colorAttachment = null;
                        depthAttachment = null;
                        rendering = false;
                        selectionScope = VulkanSelectionRenderingScope.None;
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
                                "The ordered Vulkan command stream has incomplete compute state.");
                        }
                        VulkanComputeSubmissionResource computeResource =
                            CreateComputeSubmissionResource(
                                computePipeline,
                                storageBuffer,
                                computeUniformBuffer);
                        computeResources.Add(computeResource);
                        _api.CmdBindPipeline(
                            nativeCommands,
                            PipelineBindPoint.Compute,
                            computePipeline.Pipeline);
                        DescriptorSet computeSet = computeResource.DescriptorSet;
                        _api.CmdBindDescriptorSets(
                            nativeCommands,
                            PipelineBindPoint.Compute,
                            computePipeline.Layout,
                            0,
                            1,
                            &computeSet,
                            0,
                            null);
                        _api.CmdDispatch(
                            nativeCommands,
                            checked((command.ElementCount + 63) / 64),
                            1,
                            1);
                        break;
                    case SilkGraphicsCommandKind.BufferBarrier:
                        VulkanSilkGraphicsBuffer barrierBuffer = command.Buffer!;
                        barrierBuffer.ThrowIfDisposed();
                        PipelineStageFlags destinationStages =
                            PipelineStageFlags.ComputeShaderBit |
                            PipelineStageFlags.TransferBit;
                        AccessFlags destinationAccess =
                            AccessFlags.ShaderReadBit |
                            AccessFlags.ShaderWriteBit |
                            AccessFlags.TransferReadBit;
                        if (barrierBuffer.Usage.HasFlag(SilkBufferUsage.Vertex))
                        {
                            destinationStages |= PipelineStageFlags.VertexInputBit;
                            destinationAccess |= AccessFlags.VertexAttributeReadBit;
                        }
                        if (barrierBuffer.Usage.HasFlag(SilkBufferUsage.Index))
                        {
                            destinationStages |= PipelineStageFlags.VertexInputBit;
                            destinationAccess |= AccessFlags.IndexReadBit;
                        }
                        if (barrierBuffer.Usage.HasFlag(SilkBufferUsage.Uniform))
                        {
                            destinationStages |= PipelineStageFlags.VertexShaderBit |
                                PipelineStageFlags.FragmentShaderBit;
                            destinationAccess |= AccessFlags.UniformReadBit;
                        }
                        var bufferBarrier = new BufferMemoryBarrier
                        {
                            SType = StructureType.BufferMemoryBarrier,
                            SrcAccessMask = AccessFlags.ShaderWriteBit,
                            DstAccessMask = destinationAccess,
                            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                            Buffer = barrierBuffer.Buffer,
                            Offset = 0,
                            Size = Vk.WholeSize
                        };
                        _api.CmdPipelineBarrier(
                            nativeCommands,
                            PipelineStageFlags.ComputeShaderBit,
                            destinationStages,
                            0,
                            0,
                            null,
                            1,
                            &bufferBarrier,
                            0,
                            null);
                        break;
                    default:
                        throw new InvalidOperationException("Unknown Vulkan graphics command.");
                }
            }
            ThrowIfFailed(_api.EndCommandBuffer(nativeCommands), "vkEndCommandBuffer");
            fence = SubmitCommandBuffer(nativeCommands);
            if (commands.HasSelectionOutlineSubmission)
            {
                CountSelectionSubmission();
            }
            foreach (KeyValuePair<VulkanSilkGraphicsTexture, ImageLayout> layout in finalLayouts)
            {
                layout.Key.Layout = layout.Value;
            }
            success = true;
            return new VulkanSilkGraphicsSubmission(
                this,
                _api,
                _device,
                pool,
                fence,
                [.. leases],
                [.. uploadResources],
                [.. drawResources],
                [.. computeResources],
                commands.HasSelectionOutlineSubmission);
        }
        finally
        {
            if (!success && fence.Handle != 0)
            {
                _api.DestroyFence(_device, fence, null);
            }
            if (!success && pool.Handle != 0)
            {
                _api.DestroyCommandPool(_device, pool, null);
            }
            if (!success)
            {
                foreach (VulkanUploadResource upload in uploadResources)
                {
                    DestroyUploadResource(upload);
                }
                foreach (VulkanDrawSubmissionResource resource in drawResources)
                {
                    DestroyDrawSubmissionResource(resource);
                }
                foreach (VulkanComputeSubmissionResource resource in computeResources)
                {
                    DestroyComputeSubmissionResource(resource);
                }
                foreach (IDisposable lease in leases)
                {
                    lease.Dispose();
                }
                if (dependentRegistered)
                {
                    ReleaseDependentObject();
                }
            }
        }
    }

    private VulkanComputeSubmissionResource CreateComputeSubmissionResource(
        VulkanSilkComputePipeline pipeline,
        VulkanSilkGraphicsBuffer storageBuffer,
        VulkanSilkGraphicsBuffer uniformBuffer)
    {
        DescriptorPool descriptorPool = default;
        try
        {
            DescriptorPoolSize* poolSizes = stackalloc DescriptorPoolSize[2];
            poolSizes[0] = new DescriptorPoolSize(DescriptorType.StorageBuffer, 1);
            poolSizes[1] = new DescriptorPoolSize(DescriptorType.UniformBuffer, 1);
            var poolInfo = new DescriptorPoolCreateInfo
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                MaxSets = 1,
                PoolSizeCount = 2,
                PPoolSizes = poolSizes
            };
            ThrowIfFailed(
                _api.CreateDescriptorPool(
                    _device,
                    &poolInfo,
                    null,
                    &descriptorPool),
                "vkCreateDescriptorPool");
            DescriptorSetLayout setLayout = pipeline.DescriptorSetLayout;
            var allocationInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = descriptorPool,
                DescriptorSetCount = 1,
                PSetLayouts = &setLayout
            };
            DescriptorSet descriptorSet = default;
            ThrowIfFailed(
                _api.AllocateDescriptorSets(
                    _device,
                    &allocationInfo,
                    &descriptorSet),
                "vkAllocateDescriptorSets");
            DescriptorBufferInfo* bufferInfos = stackalloc DescriptorBufferInfo[2];
            bufferInfos[0] = new DescriptorBufferInfo(
                storageBuffer.Buffer,
                0,
                checked((ulong)storageBuffer.Size));
            bufferInfos[1] = new DescriptorBufferInfo(
                uniformBuffer.Buffer,
                0,
                SilkCheckedShaderAssets.Compute.VulkanUniformByteSize);
            WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[2];
            writes[0] = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = descriptorSet,
                DstBinding = 0,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.StorageBuffer,
                PBufferInfo = &bufferInfos[0]
            };
            writes[1] = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = descriptorSet,
                DstBinding = 1,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.UniformBuffer,
                PBufferInfo = &bufferInfos[1]
            };
            _api.UpdateDescriptorSets(_device, 2, writes, 0, null);
            return new VulkanComputeSubmissionResource(
                descriptorPool,
                descriptorSet);
        }
        catch
        {
            if (descriptorPool.Handle != 0)
            {
                _api.DestroyDescriptorPool(_device, descriptorPool, null);
            }
            throw;
        }
    }

    private void DestroyComputeSubmissionResource(
        VulkanComputeSubmissionResource resource)
    {
        if (resource.DescriptorPool.Handle != 0)
        {
            _api.DestroyDescriptorPool(_device, resource.DescriptorPool, null);
        }
    }

    private VulkanDrawSubmissionResource CreateDrawSubmissionResource(
        VulkanDrawState command)
    {
        DescriptorPool descriptorPool = default;
        Framebuffer framebuffer = default;
        try
        {
            var poolSize = new DescriptorPoolSize(DescriptorType.UniformBuffer, 1);
            var poolInfo = new DescriptorPoolCreateInfo
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                MaxSets = 1,
                PoolSizeCount = 1,
                PPoolSizes = &poolSize
            };
            ThrowIfFailed(
                _api.CreateDescriptorPool(
                    _device,
                    &poolInfo,
                    null,
                    &descriptorPool),
                "vkCreateDescriptorPool");
            DescriptorSetLayout setLayout = command.Pipeline.DescriptorSetLayout;
            var allocationInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = descriptorPool,
                DescriptorSetCount = 1,
                PSetLayouts = &setLayout
            };
            DescriptorSet descriptorSet = default;
            ThrowIfFailed(
                _api.AllocateDescriptorSets(
                    _device,
                    &allocationInfo,
                    &descriptorSet),
                "vkAllocateDescriptorSets");
            var bufferInfo = new DescriptorBufferInfo(
                command.UniformBuffer.Buffer,
                0,
                80);
            var write = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = descriptorSet,
                DstBinding = 0,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.UniformBuffer,
                PBufferInfo = &bufferInfo
            };
            _api.UpdateDescriptorSets(_device, 1, &write, 0, null);

            ImageView* attachments = stackalloc ImageView[2]
            {
                command.ColorAttachment.ImageView,
                command.DepthAttachment.ImageView
            };
            var framebufferInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = command.Pipeline.RenderPass,
                AttachmentCount = 2,
                PAttachments = attachments,
                Width = command.ColorAttachment.Width,
                Height = command.ColorAttachment.Height,
                Layers = 1
            };
            ThrowIfFailed(
                _api.CreateFramebuffer(
                    _device,
                    &framebufferInfo,
                    null,
                    &framebuffer),
                "vkCreateFramebuffer");
            return new VulkanDrawSubmissionResource(
                framebuffer,
                descriptorPool,
                descriptorSet);
        }

        catch
        {
            if (framebuffer.Handle != 0)
            {
                _api.DestroyFramebuffer(_device, framebuffer, null);
            }
            if (descriptorPool.Handle != 0)
            {
                _api.DestroyDescriptorPool(_device, descriptorPool, null);
            }
            throw;
        }
    }

    private static ImageLayout GetCurrentLayout(
        Dictionary<VulkanSilkGraphicsTexture, ImageLayout> layouts,
        VulkanSilkGraphicsTexture texture) =>
        layouts.TryGetValue(texture, out ImageLayout layout)
            ? layout
            : texture.Layout;

    private void DestroyDrawSubmissionResource(
        VulkanDrawSubmissionResource resource)
    {
        if (resource.Framebuffer.Handle != 0)
        {
            _api.DestroyFramebuffer(_device, resource.Framebuffer, null);
        }
        if (resource.DescriptorPool.Handle != 0)
        {
            _api.DestroyDescriptorPool(_device, resource.DescriptorPool, null);
        }
    }

    internal void Readback(
        VulkanSilkGraphicsTexture texture,
        Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        texture.ThrowIfDisposed();
        ulong size = checked((ulong)destination.Length);
        CreateReadbackBuffer(
            size,
            out global::Silk.NET.Vulkan.Buffer buffer,
            out DeviceMemory memory);
        CommandPool pool = default;
        Fence fence = default;
        try
        {
            CreateCommandBuffer(out pool, out CommandBuffer commands);
            Transition(
                commands,
                texture.Image,
                texture.AspectMask,
                texture.Layout,
                ImageLayout.TransferSrcOptimal);
            var region = new BufferImageCopy
            {
                BufferOffset = 0,
                BufferRowLength = 0,
                BufferImageHeight = 0,
                ImageSubresource = new ImageSubresourceLayers
                {
                    AspectMask = texture.AspectMask,
                    MipLevel = 0,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                },
                ImageOffset = new Offset3D(0, 0, 0),
                ImageExtent = new Extent3D(texture.Width, texture.Height, 1)
            };
            _api.CmdCopyImageToBuffer(
                commands,
                texture.Image,
                ImageLayout.TransferSrcOptimal,
                buffer,
                1,
                &region);
            ThrowIfFailed(_api.EndCommandBuffer(commands), "vkEndCommandBuffer");
            fence = SubmitCommandBuffer(commands);
            WaitForFence(fence);
            texture.Layout = ImageLayout.TransferSrcOptimal;

            void* mapped = null;
            ThrowIfFailed(
                _api.MapMemory(_device, memory, 0, size, 0, &mapped),
                "vkMapMemory");
            try
            {
                new ReadOnlySpan<byte>(mapped, destination.Length).CopyTo(destination);
            }
            finally
            {
                _api.UnmapMemory(_device, memory);
            }
        }
        finally
        {
            if (fence.Handle != 0)
            {
                _api.DestroyFence(_device, fence, null);
            }
            if (pool.Handle != 0)
            {
                _api.DestroyCommandPool(_device, pool, null);
            }
            _api.DestroyBuffer(_device, buffer, null);
            _api.FreeMemory(_device, memory, null);
        }
    }

    internal void DestroyTexture(
        ref Image image,
        ref DeviceMemory memory,
        ref ImageView imageView)
    {
        if (imageView.Handle != 0)
        {
            _api.DestroyImageView(_device, imageView, null);
            imageView = default;
        }
        if (image.Handle != 0)
        {
            _api.DestroyImage(_device, image, null);
            image = default;
        }
        if (memory.Handle != 0)
        {
            _api.FreeMemory(_device, memory, null);
            memory = default;
        }
    }

    private VulkanUploadResource CreateTextureUpload(ReadOnlySpan<byte> source)
    {
        CreateHostBuffer(
            checked((ulong)source.Length),
            BufferUsageFlags.TransferSrcBit,
            out global::Silk.NET.Vulkan.Buffer buffer,
            out DeviceMemory memory);
        try
        {
            void* mapped = null;
            ThrowIfFailed(
                _api.MapMemory(
                    _device,
                    memory,
                    0,
                    checked((ulong)source.Length),
                    0,
                    &mapped),
                "vkMapMemory");
            try
            {
                source.CopyTo(new Span<byte>(mapped, source.Length));
            }
            finally
            {
                _api.UnmapMemory(_device, memory);
            }
            return new VulkanUploadResource(buffer, memory);
        }
        catch
        {
            _api.DestroyBuffer(_device, buffer, null);
            _api.FreeMemory(_device, memory, null);
            throw;
        }
    }

    private void DestroyUploadResource(VulkanUploadResource resource)
    {
        if (resource.Buffer.Handle != 0)
        {
            _api.DestroyBuffer(_device, resource.Buffer, null);
        }
        if (resource.Memory.Handle != 0)
        {
            _api.FreeMemory(_device, resource.Memory, null);
        }
    }

    private void CreateReadbackBuffer(
        ulong size,
        out global::Silk.NET.Vulkan.Buffer buffer,
        out DeviceMemory memory) =>
        CreateHostBuffer(
            size,
            BufferUsageFlags.TransferDstBit,
            out buffer,
            out memory);

    private void CreateHostBuffer(
        ulong size,
        BufferUsageFlags usage,
        out global::Silk.NET.Vulkan.Buffer buffer,
        out DeviceMemory memory)
    {
        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive
        };
        global::Silk.NET.Vulkan.Buffer createdBuffer = default;
        DeviceMemory allocatedMemory = default;
        bool success = false;
        ThrowIfFailed(
            _api.CreateBuffer(_device, &bufferInfo, null, &createdBuffer),
            "vkCreateBuffer");
        try
        {
            _api.GetBufferMemoryRequirements(
                _device,
                createdBuffer,
                out MemoryRequirements requirements);
            var allocationInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = FindMemoryType(
                    requirements.MemoryTypeBits,
                    MemoryPropertyFlags.HostVisibleBit |
                    MemoryPropertyFlags.HostCoherentBit)
            };
            ThrowIfFailed(
                _api.AllocateMemory(_device, &allocationInfo, null, &allocatedMemory),
                "vkAllocateMemory");
            ThrowIfFailed(
                _api.BindBufferMemory(_device, createdBuffer, allocatedMemory, 0),
                "vkBindBufferMemory");
            buffer = createdBuffer;
            memory = allocatedMemory;
            success = true;
        }
        finally
        {
            if (!success && allocatedMemory.Handle != 0)
            {
                _api.FreeMemory(_device, allocatedMemory, null);
            }
            if (!success)
            {
                _api.DestroyBuffer(_device, createdBuffer, null);
            }
        }
    }

    private void CreateCommandBuffer(
        out CommandPool pool,
        out CommandBuffer commands)
    {
        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.TransientBit,
            QueueFamilyIndex = _queueFamily
        };
        CommandPool createdPool = default;
        CommandBuffer allocatedCommands = default;
        bool success = false;
        ThrowIfFailed(
            _api.CreateCommandPool(_device, &poolInfo, null, &createdPool),
            "vkCreateCommandPool");
        try
        {
            var allocationInfo = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = createdPool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1
            };
            ThrowIfFailed(
                _api.AllocateCommandBuffers(_device, &allocationInfo, &allocatedCommands),
                "vkAllocateCommandBuffers");
            var beginInfo = new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit
            };
            ThrowIfFailed(
                _api.BeginCommandBuffer(allocatedCommands, &beginInfo),
                "vkBeginCommandBuffer");
            pool = createdPool;
            commands = allocatedCommands;
            success = true;
        }
        finally
        {
            if (!success)
            {
                _api.DestroyCommandPool(_device, createdPool, null);
            }
        }
    }

    private Fence SubmitCommandBuffer(CommandBuffer commands)
    {
        var fenceInfo = new FenceCreateInfo
        {
            SType = StructureType.FenceCreateInfo
        };
        Fence fence = default;
        bool success = false;
        ThrowIfFailed(
            _api.CreateFence(_device, &fenceInfo, null, &fence),
            "vkCreateFence");
        try
        {
            var submitInfo = new SubmitInfo
            {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = &commands
            };
            ThrowIfFailed(
                _api.QueueSubmit(_queue, 1, &submitInfo, fence),
                "vkQueueSubmit");
            success = true;
            return fence;
        }
        finally
        {
            if (!success)
            {
                _api.DestroyFence(_device, fence, null);
            }
        }
    }

    private void WaitForFence(Fence fence)
    {
        ThrowIfFailed(
            _api.WaitForFences(_device, 1, &fence, true, ulong.MaxValue),
            "vkWaitForFences");
    }

    internal void Transition(
        CommandBuffer commands,
        Image image,
        ImageAspectFlags aspectMask,
        ImageLayout before,
        ImageLayout after)
    {
        if (before == after)
        {
            return;
        }

        GetTransitionMasks(
            before,
            after,
            out AccessFlags sourceAccess,
            out AccessFlags destinationAccess,
            out PipelineStageFlags sourceStage,
            out PipelineStageFlags destinationStage);
        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = sourceAccess,
            DstAccessMask = destinationAccess,
            OldLayout = before,
            NewLayout = after,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = aspectMask,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };
        _api.CmdPipelineBarrier(
            commands,
            sourceStage,
            destinationStage,
            DependencyFlags.None,
            0,
            null,
            0,
            null,
            1,
            &barrier);
    }

    private static void GetTransitionMasks(
        ImageLayout before,
        ImageLayout after,
        out AccessFlags sourceAccess,
        out AccessFlags destinationAccess,
        out PipelineStageFlags sourceStage,
        out PipelineStageFlags destinationStage)
    {
        sourceAccess = before switch
        {
            ImageLayout.TransferDstOptimal => AccessFlags.TransferWriteBit,
            ImageLayout.TransferSrcOptimal => AccessFlags.TransferReadBit,
            ImageLayout.ColorAttachmentOptimal => AccessFlags.ColorAttachmentWriteBit,
            ImageLayout.DepthStencilAttachmentOptimal =>
                AccessFlags.DepthStencilAttachmentWriteBit,
            ImageLayout.DepthStencilReadOnlyOptimal =>
                AccessFlags.DepthStencilAttachmentReadBit |
                AccessFlags.ShaderReadBit,
            ImageLayout.ShaderReadOnlyOptimal => AccessFlags.ShaderReadBit,
            _ => 0
        };
        destinationAccess = after switch
        {
            ImageLayout.TransferDstOptimal => AccessFlags.TransferWriteBit,
            ImageLayout.TransferSrcOptimal => AccessFlags.TransferReadBit,
            ImageLayout.ColorAttachmentOptimal => AccessFlags.ColorAttachmentWriteBit,
            ImageLayout.DepthStencilAttachmentOptimal =>
                AccessFlags.DepthStencilAttachmentWriteBit,
            ImageLayout.DepthStencilReadOnlyOptimal =>
                AccessFlags.DepthStencilAttachmentReadBit |
                AccessFlags.ShaderReadBit,
            ImageLayout.ShaderReadOnlyOptimal => AccessFlags.ShaderReadBit,
            _ => 0
        };
        sourceStage = before switch
        {
            ImageLayout.Undefined => PipelineStageFlags.TopOfPipeBit,
            ImageLayout.ColorAttachmentOptimal =>
                PipelineStageFlags.ColorAttachmentOutputBit,
            ImageLayout.DepthStencilAttachmentOptimal =>
                PipelineStageFlags.EarlyFragmentTestsBit,
            ImageLayout.DepthStencilReadOnlyOptimal =>
                PipelineStageFlags.EarlyFragmentTestsBit |
                PipelineStageFlags.FragmentShaderBit,
            ImageLayout.ShaderReadOnlyOptimal =>
                PipelineStageFlags.FragmentShaderBit,
            _ => PipelineStageFlags.TransferBit
        };
        destinationStage = after switch
        {
            ImageLayout.ColorAttachmentOptimal =>
                PipelineStageFlags.ColorAttachmentOutputBit,
            ImageLayout.DepthStencilAttachmentOptimal =>
                PipelineStageFlags.EarlyFragmentTestsBit,
            ImageLayout.DepthStencilReadOnlyOptimal =>
                PipelineStageFlags.EarlyFragmentTestsBit |
                PipelineStageFlags.FragmentShaderBit,
            ImageLayout.ShaderReadOnlyOptimal =>
                PipelineStageFlags.FragmentShaderBit,
            _ => PipelineStageFlags.TransferBit
        };
    }

    private static Filter GetFilter(SilkSamplerFilter filter) =>
        filter switch
        {
            SilkSamplerFilter.Nearest => Filter.Nearest,
            SilkSamplerFilter.Linear => Filter.Linear,
            _ => throw new ArgumentOutOfRangeException(nameof(filter))
        };

    private static SamplerAddressMode GetAddressMode(SilkSamplerAddressMode mode) =>
        mode switch
        {
            SilkSamplerAddressMode.ClampToEdge => SamplerAddressMode.ClampToEdge,
            SilkSamplerAddressMode.Repeat => SamplerAddressMode.Repeat,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
}

internal sealed class VulkanSilkGraphicsTexture : SilkGraphicsTextureBase
{
    private readonly VulkanSilkGraphicsDevice _device;
    private Image _image;
    private DeviceMemory _memory;
    private ImageView _imageView;
    private readonly bool _ownsNativeObjects;

    internal VulkanSilkGraphicsTexture(
        VulkanSilkGraphicsDevice device,
        Image image,
        DeviceMemory memory,
        ImageView imageView,
        SilkTextureDescriptor descriptor,
        bool ownsNativeObjects)
        : base(descriptor)
    {
        _device = device;
        _image = image;
        _memory = memory;
        _imageView = imageView;
        _ownsNativeObjects = ownsNativeObjects;
    }

    internal Image Image => _image;

    internal ImageView ImageView => _imageView;

    internal VulkanSilkGraphicsDevice Device => _device;

    internal ImageAspectFlags AspectMask =>
        Format == SilkTextureFormat.Rgba8Unorm
            ? ImageAspectFlags.ColorBit
            : ImageAspectFlags.DepthBit;

    internal ImageSubresourceRange SubresourceRange => new()
    {
        AspectMask = AspectMask,
        BaseMipLevel = 0,
        LevelCount = 1,
        BaseArrayLayer = 0,
        LayerCount = 1
    };

    internal ImageLayout Layout { get; set; } = ImageLayout.Undefined;

    public override void ReadbackForTesting(Span<byte> destination)
    {
        ThrowIfDisposed();
        ValidateReadback(destination.Length);
        _device.Readback(this, destination);
    }

    public override void ReadbackForTesting(Span<float> destination)
    {
        ThrowIfDisposed();
        ValidateDepthReadback(destination.Length);
        _device.Readback(this, MemoryMarshal.AsBytes(destination));
    }

    protected override void ReleaseNative()
    {
        if (_ownsNativeObjects)
        {
            _device.DestroyTexture(ref _image, ref _memory, ref _imageView);
        }
        else
        {
            _image = default;
            _imageView = default;
        }
        _device.ReleaseDependentObject();
    }

    internal IDisposable AcquireLease() => AcquireSubmissionLease();

    internal void ThrowIfDisposed() => ThrowIfTextureDisposed();
}

internal sealed class VulkanSilkGraphicsSampler(
    VulkanSilkGraphicsDevice owner,
    Vk api,
    Device device,
    Sampler sampler,
    SilkSamplerDescriptor descriptor)
    : SilkGraphicsResourceBase, ISilkGraphicsSampler
{
    private readonly Vk _api = api;
    private readonly Device _device = device;
    private Sampler _sampler = sampler;

    internal VulkanSilkGraphicsDevice Owner { get; } = owner;

    internal Sampler Sampler => _sampler;

    public SilkSamplerDescriptor Descriptor { get; } = descriptor;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfDisposed() => ThrowIfResourceDisposed();

    protected override unsafe void ReleaseNative()
    {
        if (_sampler.Handle != 0)
        {
            _api.DestroySampler(_device, _sampler, null);
            _sampler = default;
        }
        Owner.ReleaseDependentObject();
    }
}

internal sealed partial class VulkanSilkGraphicsCommandList(VulkanSilkGraphicsDevice device)
    : ISilkGraphicsCommandList,
      ISilkPickGraphicsCommandList,
      ISilkSelectionOutlineGraphicsCommandList
{
    private readonly List<VulkanGraphicsCommand> _commands = [];
    private VulkanSilkGraphicsTexture? _colorAttachment;
    private VulkanSilkGraphicsTexture? _depthAttachment;
    private VulkanSilkGraphicsPipeline? _pipeline;
    private VulkanSilkGraphicsBuffer? _vertexBuffer;
    private VulkanSilkGraphicsBuffer? _indexBuffer;
    private VulkanSilkGraphicsBuffer? _uniformBuffer;
    private VulkanSilkComputePipeline? _computePipeline;
    private VulkanSilkGraphicsBuffer? _storageBuffer;
    private VulkanSilkGraphicsBuffer? _computeUniformBuffer;
    private SilkViewport? _viewport;
    private SilkScissor? _scissor;
    private bool _rendering;
    private bool _submitted;
    private bool _disposed;

    internal VulkanSilkGraphicsDevice Device { get; } = device;

    internal IReadOnlyList<VulkanGraphicsCommand> Commands => _commands;

    public void UploadTexture(ISilkGraphicsTexture texture, ReadOnlySpan<byte> source)
    {
        ThrowIfOutsideRendering();
        VulkanSilkGraphicsTexture vulkanTexture = ValidateTexture(texture);
        if (vulkanTexture.Format != SilkTextureFormat.Rgba8Unorm ||
            !vulkanTexture.Usage.HasFlag(SilkTextureUsage.CopyDestination))
        {
            throw new InvalidOperationException(
                "UploadTexture requires an RGBA8 texture with CopyDestination usage.");
        }
        int requiredLength = checked((int)(vulkanTexture.Width * vulkanTexture.Height * 4));
        if (source.Length != requiredLength)
        {
            throw new ArgumentException(
                $"The source must contain exactly {requiredLength} bytes.",
                nameof(source));
        }
        _commands.Add(VulkanGraphicsCommand.Upload(vulkanTexture, source.ToArray()));
    }

    public void ClearColor(ISilkGraphicsTexture texture, SilkColor color)
    {
        ThrowIfOutsideRendering();
        VulkanSilkGraphicsTexture vulkanTexture = ValidateTexture(texture);
        if (vulkanTexture.Format != SilkTextureFormat.Rgba8Unorm ||
            !vulkanTexture.Usage.HasFlag(SilkTextureUsage.ColorRenderTarget))
        {
            throw new InvalidOperationException("ClearColor requires an RGBA8 color render target.");
        }
        color.Validate();
        _commands.Add(VulkanGraphicsCommand.ClearColor(vulkanTexture, color));
    }

    public void ClearDepth(ISilkGraphicsTexture texture, float depth)
    {
        ThrowIfOutsideRendering();
        VulkanSilkGraphicsTexture vulkanTexture = ValidateTexture(texture);
        if (vulkanTexture.Format != SilkTextureFormat.D32Float ||
            !vulkanTexture.Usage.HasFlag(SilkTextureUsage.DepthRenderTarget))
        {
            throw new InvalidOperationException(
                "ClearDepth requires a D32Float depth render target.");
        }
        ValidateDepth(depth);
        _commands.Add(VulkanGraphicsCommand.ClearDepth(vulkanTexture, depth));
    }

    public void BeginRendering(SilkRenderingDescriptor descriptor)
    {
        ThrowIfUnavailable();
        if (_rendering)
        {
            throw new InvalidOperationException("A rendering scope is already active.");
        }
        VulkanSilkGraphicsTexture color = ValidateTexture(descriptor.ColorAttachment);
        VulkanSilkGraphicsTexture depth = ValidateTexture(descriptor.DepthAttachment);
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
        BeginPickRenderingScope();
        EndSelectionRenderingScope();
        _rendering = true;
        _commands.Add(VulkanGraphicsCommand.BeginRendering(color, depth));
    }

    public void SetGraphicsPipeline(ISilkGraphicsPipeline pipeline)
    {
        ThrowIfRendering();
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not VulkanSilkGraphicsPipeline vulkanPipeline ||
            !ReferenceEquals(vulkanPipeline.Owner, Device))
        {
            throw new ArgumentException(
                "The pipeline was not created by this Vulkan device.",
                nameof(pipeline));
        }
        vulkanPipeline.ThrowIfDisposed();
        _pipeline = vulkanPipeline;
        _pickPipeline = null;
        _commands.Add(VulkanGraphicsCommand.SetPipeline(vulkanPipeline));
    }

    public void SetViewport(SilkViewport viewport)
    {
        ThrowIfRendering();
        viewport.Validate();
        _viewport = viewport;
        _commands.Add(VulkanGraphicsCommand.SetViewport(viewport));
    }

    public void SetScissor(SilkScissor scissor)
    {
        ThrowIfRendering();
        scissor.Validate();
        _scissor = scissor;
        _commands.Add(VulkanGraphicsCommand.SetScissor(scissor));
    }

    public void SetVertexBuffer(ISilkGraphicsBuffer buffer)
    {
        ThrowIfRendering();
        VulkanSilkGraphicsBuffer vulkanBuffer = ValidateBuffer(buffer);
        if (!vulkanBuffer.Usage.HasFlag(SilkBufferUsage.Vertex))
        {
            throw new ArgumentException("The buffer is not a vertex buffer.", nameof(buffer));
        }
        _vertexBuffer = vulkanBuffer;
        _commands.Add(VulkanGraphicsCommand.SetVertexBuffer(vulkanBuffer));
    }

    public void SetIndexBuffer(ISilkGraphicsBuffer buffer)
    {
        ThrowIfRendering();
        VulkanSilkGraphicsBuffer vulkanBuffer = ValidateBuffer(buffer);
        if (!vulkanBuffer.Usage.HasFlag(SilkBufferUsage.Index))
        {
            throw new ArgumentException("The buffer is not an index buffer.", nameof(buffer));
        }
        _indexBuffer = vulkanBuffer;
        _commands.Add(VulkanGraphicsCommand.SetIndexBuffer(vulkanBuffer));
    }

    public void SetUniformBuffer(
        uint setIndex,
        uint binding,
        ISilkGraphicsBuffer buffer)
    {
        ThrowIfRendering();
        VulkanSilkGraphicsBuffer vulkanBuffer = ValidateBuffer(buffer);
        if (setIndex != 0 || binding != 0 ||
            !vulkanBuffer.Usage.HasFlag(SilkBufferUsage.Uniform) ||
            vulkanBuffer.Size < 80)
        {
            throw new ArgumentException(
                "SceneParameters requires an 80-byte uniform buffer at set 0, binding 0.",
                nameof(buffer));
        }
        _uniformBuffer = vulkanBuffer;
        _commands.Add(VulkanGraphicsCommand.SetUniformBuffer(
            setIndex,
            binding,
            vulkanBuffer));
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
        if (checked((nuint)indexCount * 2) > _indexBuffer.Size)
        {
            throw new ArgumentOutOfRangeException(nameof(indexCount));
        }
        if (_pickPipeline is not null)
        {
            RecordPickDraw(
                _vertexBuffer,
                _indexBuffer,
                _uniformBuffer,
                indexCount);
        }
        _commands.Add(VulkanGraphicsCommand.DrawIndexed(indexCount));
    }

    public void EndRendering()
    {
        ThrowIfRendering();
        _commands.Add(VulkanGraphicsCommand.EndRendering());
        _rendering = false;
        _colorAttachment = null;
        _depthAttachment = null;
        EndSelectionRenderingScope();
    }

    public void SetComputePipeline(ISilkComputePipeline pipeline)
    {
        ThrowIfOutsideRendering();
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not VulkanSilkComputePipeline vulkanPipeline ||
            !ReferenceEquals(vulkanPipeline.Owner, Device))
        {
            throw new ArgumentException(
                "The compute pipeline was not created by this Vulkan device.",
                nameof(pipeline));
        }
        vulkanPipeline.ThrowIfDisposed();
        _computePipeline = vulkanPipeline;
        _commands.Add(VulkanGraphicsCommand.SetComputePipeline(vulkanPipeline));
    }

    public void SetStorageBuffer(
        uint setIndex,
        uint binding,
        ISilkGraphicsBuffer buffer)
    {
        ThrowIfOutsideRendering();
        VulkanSilkGraphicsBuffer vulkanBuffer = ValidateBuffer(buffer);
        if (setIndex != 0 || binding != 0 ||
            !vulkanBuffer.Usage.HasFlag(SilkBufferUsage.Storage))
        {
            throw new ArgumentException(
                "outputValues requires a storage buffer at set 0, binding 0.",
                nameof(buffer));
        }
        _storageBuffer = vulkanBuffer;
        _commands.Add(VulkanGraphicsCommand.SetStorageBuffer(
            setIndex,
            binding,
            vulkanBuffer));
    }

    public void SetComputeUniformBuffer(
        uint setIndex,
        uint binding,
        ISilkGraphicsBuffer buffer)
    {
        ThrowIfOutsideRendering();
        VulkanSilkGraphicsBuffer vulkanBuffer = ValidateBuffer(buffer);
        if (setIndex != 0 || binding != 1 ||
            !vulkanBuffer.Usage.HasFlag(SilkBufferUsage.Uniform) ||
            vulkanBuffer.Size < SilkCheckedShaderAssets.Compute.VulkanUniformByteSize)
        {
            throw new ArgumentException(
                "ComputeParameters requires a 16-byte uniform buffer at set 0, binding 1.",
                nameof(buffer));
        }
        _computeUniformBuffer = vulkanBuffer;
        _commands.Add(VulkanGraphicsCommand.SetComputeUniformBuffer(
            setIndex,
            binding,
            vulkanBuffer));
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
        _commands.Add(VulkanGraphicsCommand.Dispatch(elementCount));
    }

    public void BufferBarrier(ISilkGraphicsBuffer buffer)
    {
        ThrowIfOutsideRendering();
        VulkanSilkGraphicsBuffer vulkanBuffer = ValidateBuffer(buffer);
        if (!vulkanBuffer.Usage.HasFlag(SilkBufferUsage.Storage))
        {
            throw new ArgumentException(
                "BufferBarrier requires a storage buffer.",
                nameof(buffer));
        }
        _commands.Add(VulkanGraphicsCommand.BufferBarrier(vulkanBuffer));
    }

    public void Dispose()
    {
        DisposePickState();
        DisposeSelectionOutlineState();
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
        ValidatePickSubmission();
        _submitted = true;
    }

    private static void ValidateDepth(float depth)
    {
        if (!float.IsFinite(depth) || depth < 0 || depth > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(depth));
        }
    }

    private VulkanSilkGraphicsTexture ValidateTexture(ISilkGraphicsTexture texture)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_submitted)
        {
            throw new InvalidOperationException("The command list was already submitted.");
        }
        ArgumentNullException.ThrowIfNull(texture);
        if (texture is not VulkanSilkGraphicsTexture vulkanTexture)
        {
            throw new ArgumentException("The texture is not a Vulkan texture.", nameof(texture));
        }
        if (!ReferenceEquals(vulkanTexture.Device, Device))
        {
            throw new ArgumentException(
                "The texture was not created by this Vulkan device.",
                nameof(texture));
        }
        vulkanTexture.ThrowIfDisposed();
        return vulkanTexture;
    }

    private VulkanSilkGraphicsBuffer ValidateBuffer(ISilkGraphicsBuffer buffer)
    {
        ThrowIfUnavailable();
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer is not VulkanSilkGraphicsBuffer vulkanBuffer ||
            !ReferenceEquals(vulkanBuffer.Owner, Device))
        {
            throw new ArgumentException("The buffer is not a Vulkan buffer.", nameof(buffer));
        }
        vulkanBuffer.ThrowIfDisposed();
        return vulkanBuffer;
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

internal readonly record struct VulkanGraphicsCommand(
    SilkGraphicsCommandKind Kind,
    VulkanSilkGraphicsTexture? Texture,
    VulkanSilkGraphicsTexture? DepthTexture,
    VulkanSilkGraphicsPipeline? Pipeline,
    VulkanSilkComputePipeline? ComputePipeline,
    VulkanSilkSelectionMaskGraphicsPipeline? SelectionMaskPipeline,
    VulkanSilkSelectionOutlineGraphicsPipeline? SelectionOutlinePipeline,
    VulkanSilkSelectionOutlineBinding? SelectionOutlineBinding,
    VulkanSilkGraphicsBuffer? Buffer,
    SilkColor Color,
    float Depth,
    byte[]? Data,
    SilkViewport Viewport,
    SilkScissor Scissor,
    uint SetIndex,
    uint Binding,
    uint IndexCount,
    uint ElementCount)
{
    internal static VulkanGraphicsCommand Upload(
        VulkanSilkGraphicsTexture texture,
        byte[] data) =>
        Create(
            SilkGraphicsCommandKind.UploadTexture,
            texture: texture,
            data: data);

    internal static VulkanGraphicsCommand ClearColor(
        VulkanSilkGraphicsTexture texture,
        SilkColor color) =>
        Create(
            SilkGraphicsCommandKind.ClearColor,
            texture: texture,
            color: color);

    internal static VulkanGraphicsCommand ClearDepth(
        VulkanSilkGraphicsTexture texture,
        float depth) =>
        Create(
            SilkGraphicsCommandKind.ClearDepth,
            texture: texture,
            depth: depth);

    internal static VulkanGraphicsCommand BeginRendering(
        VulkanSilkGraphicsTexture color,
        VulkanSilkGraphicsTexture depth) =>
        Create(
            SilkGraphicsCommandKind.BeginRendering,
            texture: color,
            depthTexture: depth);

    internal static VulkanGraphicsCommand BeginSelectionMask(
        VulkanSilkGraphicsTexture mask,
        VulkanSilkGraphicsTexture depth) =>
        Create(
            SilkGraphicsCommandKind.BeginSelectionMaskRendering,
            texture: mask,
            depthTexture: depth);

    internal static VulkanGraphicsCommand BeginSelectionOutline(
        VulkanSilkGraphicsTexture color) =>
        Create(
            SilkGraphicsCommandKind.BeginSelectionOutlineRendering,
            texture: color);

    internal static VulkanGraphicsCommand SetPipeline(
        VulkanSilkGraphicsPipeline pipeline) =>
        Create(
            SilkGraphicsCommandKind.SetGraphicsPipeline,
            pipeline: pipeline);

    internal static VulkanGraphicsCommand SetSelectionMaskPipeline(
        VulkanSilkSelectionMaskGraphicsPipeline pipeline) =>
        Create(
            SilkGraphicsCommandKind.SetSelectionMaskPipeline,
            selectionMaskPipeline: pipeline);

    internal static VulkanGraphicsCommand SetSelectionOutlinePipeline(
        VulkanSilkSelectionOutlineGraphicsPipeline pipeline) =>
        Create(
            SilkGraphicsCommandKind.SetSelectionOutlinePipeline,
            selectionOutlinePipeline: pipeline);

    internal static VulkanGraphicsCommand SetSelectionOutlineBinding(
        VulkanSilkSelectionOutlineBinding binding) =>
        Create(
            SilkGraphicsCommandKind.SetSelectionOutlineBinding,
            selectionOutlineBinding: binding);

    internal static VulkanGraphicsCommand SetViewport(SilkViewport viewport) =>
        Create(SilkGraphicsCommandKind.SetViewport, viewport: viewport);

    internal static VulkanGraphicsCommand SetScissor(SilkScissor scissor) =>
        Create(SilkGraphicsCommandKind.SetScissor, scissor: scissor);

    internal static VulkanGraphicsCommand SetVertexBuffer(
        VulkanSilkGraphicsBuffer buffer) =>
        Create(SilkGraphicsCommandKind.SetVertexBuffer, buffer: buffer);

    internal static VulkanGraphicsCommand SetIndexBuffer(
        VulkanSilkGraphicsBuffer buffer) =>
        Create(SilkGraphicsCommandKind.SetIndexBuffer, buffer: buffer);

    internal static VulkanGraphicsCommand SetUniformBuffer(
        uint setIndex,
        uint binding,
        VulkanSilkGraphicsBuffer buffer) =>
        Create(
            SilkGraphicsCommandKind.SetUniformBuffer,
            buffer: buffer,
            setIndex: setIndex,
            binding: binding);

    internal static VulkanGraphicsCommand DrawIndexed(uint indexCount) =>
        Create(SilkGraphicsCommandKind.DrawIndexed, indexCount: indexCount);

    internal static VulkanGraphicsCommand DrawSelectionOutline() =>
        Create(SilkGraphicsCommandKind.DrawSelectionOutlineFullscreenTriangle);

    internal static VulkanGraphicsCommand EndRendering() =>
        Create(SilkGraphicsCommandKind.EndRendering);

    internal static VulkanGraphicsCommand SetComputePipeline(
        VulkanSilkComputePipeline pipeline) =>
        Create(
            SilkGraphicsCommandKind.SetComputePipeline,
            computePipeline: pipeline);

    internal static VulkanGraphicsCommand SetStorageBuffer(
        uint setIndex,
        uint binding,
        VulkanSilkGraphicsBuffer buffer) =>
        Create(
            SilkGraphicsCommandKind.SetStorageBuffer,
            buffer: buffer,
            setIndex: setIndex,
            binding: binding);

    internal static VulkanGraphicsCommand SetComputeUniformBuffer(
        uint setIndex,
        uint binding,
        VulkanSilkGraphicsBuffer buffer) =>
        Create(
            SilkGraphicsCommandKind.SetComputeUniformBuffer,
            buffer: buffer,
            setIndex: setIndex,
            binding: binding);

    internal static VulkanGraphicsCommand Dispatch(uint elementCount) =>
        Create(SilkGraphicsCommandKind.Dispatch, elementCount: elementCount);

    internal static VulkanGraphicsCommand BufferBarrier(
        VulkanSilkGraphicsBuffer buffer) =>
        Create(SilkGraphicsCommandKind.BufferBarrier, buffer: buffer);

    private static VulkanGraphicsCommand Create(
        SilkGraphicsCommandKind kind,
        VulkanSilkGraphicsTexture? texture = null,
        VulkanSilkGraphicsTexture? depthTexture = null,
        VulkanSilkGraphicsPipeline? pipeline = null,
        VulkanSilkComputePipeline? computePipeline = null,
        VulkanSilkSelectionMaskGraphicsPipeline? selectionMaskPipeline = null,
        VulkanSilkSelectionOutlineGraphicsPipeline? selectionOutlinePipeline = null,
        VulkanSilkSelectionOutlineBinding? selectionOutlineBinding = null,
        VulkanSilkGraphicsBuffer? buffer = null,
        SilkColor color = default,
        float depth = 0,
        byte[]? data = null,
        SilkViewport viewport = default,
        SilkScissor scissor = default,
        uint setIndex = 0,
        uint binding = 0,
        uint indexCount = 0,
        uint elementCount = 0) =>
        new(
            kind,
            texture,
            depthTexture,
            pipeline,
            computePipeline,
            selectionMaskPipeline,
            selectionOutlinePipeline,
            selectionOutlineBinding,
            buffer,
            color,
            depth,
            data,
            viewport,
            scissor,
            setIndex,
            binding,
            indexCount,
            elementCount);
}

internal readonly record struct VulkanDrawState(
    VulkanSilkGraphicsTexture ColorAttachment,
    VulkanSilkGraphicsTexture DepthAttachment,
    VulkanSilkGraphicsPipeline Pipeline,
    VulkanSilkGraphicsBuffer VertexBuffer,
    VulkanSilkGraphicsBuffer IndexBuffer,
    VulkanSilkGraphicsBuffer UniformBuffer,
    SilkViewport Viewport,
    SilkScissor Scissor,
    uint IndexCount);

internal readonly record struct VulkanUploadResource(
    global::Silk.NET.Vulkan.Buffer Buffer,
    DeviceMemory Memory);

internal readonly record struct VulkanDrawSubmissionResource(
    Framebuffer Framebuffer,
    DescriptorPool DescriptorPool,
    DescriptorSet DescriptorSet);

internal readonly record struct VulkanComputeSubmissionResource(
    DescriptorPool DescriptorPool,
    DescriptorSet DescriptorSet);

internal sealed unsafe class VulkanSilkGraphicsSubmission(
    VulkanSilkGraphicsDevice owner,
    Vk api,
    Device device,
    CommandPool pool,
    Fence fence,
    IDisposable[] leases,
    VulkanUploadResource[] uploadResources,
    VulkanDrawSubmissionResource[] drawResources,
    VulkanComputeSubmissionResource[] computeResources,
    bool selectionOutlineSubmission)
    : ISilkGraphicsSubmission
{
    private readonly VulkanSilkGraphicsDevice _owner = owner;
    private readonly Vk _api = api;
    private readonly Device _device = device;
    private CommandPool _pool = pool;
    private Fence _fence = fence;
    private IDisposable[]? _leases = leases;
    private VulkanUploadResource[]? _uploadResources = uploadResources;
    private VulkanDrawSubmissionResource[]? _drawResources = drawResources;
    private VulkanComputeSubmissionResource[]? _computeResources = computeResources;
    private readonly bool _selectionOutlineSubmission = selectionOutlineSubmission;

    public bool IsCompleted
    {
        get
        {
            ObjectDisposedException.ThrowIf(_fence.Handle == 0, this);
            Result result =
                _selectionOutlineSubmission &&
                _owner.TryConsumeSelectionOutlineFenceFailureForTesting(
                    out Result injected)
                    ? injected
                    : _api.GetFenceStatus(_device, _fence);
            if (result == Result.Success)
            {
                ReleaseCompletedResources();
                return true;
            }
            if (result == Result.ErrorDeviceLost)
            {
                _owner.NotifyPickDeviceLost();
                if (_selectionOutlineSubmission)
                {
                    _owner.NotifySelectionOutlineDeviceLost();
                }
                ReleaseCompletedResources();
            }
            if (result == Result.NotReady)
            {
                return false;
            }
            VulkanSilkGraphicsDevice.ThrowIfFailed(result, "vkGetFenceStatus");
            return false;
        }
    }

    public void Wait()
    {
        ObjectDisposedException.ThrowIf(_fence.Handle == 0, this);
        Result result = WaitForCompletion();
        if (IsTerminal(result))
        {
            ReleaseCompletedResources();
        }
        VulkanSilkGraphicsDevice.ThrowIfFailed(result, "vkWaitForFences");
    }

    public void Dispose()
    {
        if (_fence.Handle == 0)
        {
            return;
        }
        Result result = WaitForCompletion();
        bool terminal = IsTerminal(result);
        try
        {
            VulkanSilkGraphicsDevice.ThrowIfFailed(result, "vkWaitForFences");
        }
        finally
        {
            if (terminal)
            {
                ReleaseCompletedResources();
                _api.DestroyFence(_device, _fence, null);
                _api.DestroyCommandPool(_device, _pool, null);
                _fence = default;
                _pool = default;
                _owner.ReleaseDependentObject();
            }
        }
    }

    private void ReleaseCompletedResources()
    {
        IDisposable[]? leases = Interlocked.Exchange(ref _leases, null);
        if (leases is not null)
        {
            foreach (IDisposable lease in leases)
            {
                lease.Dispose();
            }
        }
        VulkanUploadResource[]? uploads =
            Interlocked.Exchange(ref _uploadResources, null);
        if (uploads is not null)
        {
            foreach (VulkanUploadResource upload in uploads)
            {
                if (upload.Buffer.Handle != 0)
                {
                    _api.DestroyBuffer(_device, upload.Buffer, null);
                }
                if (upload.Memory.Handle != 0)
                {
                    _api.FreeMemory(_device, upload.Memory, null);
                }
            }
        }
        VulkanDrawSubmissionResource[]? draws =
            Interlocked.Exchange(ref _drawResources, null);
        if (draws is not null)
        {
            foreach (VulkanDrawSubmissionResource draw in draws)
            {
                if (draw.Framebuffer.Handle != 0)
                {
                    _api.DestroyFramebuffer(_device, draw.Framebuffer, null);
                }
                if (draw.DescriptorPool.Handle != 0)
                {
                    _api.DestroyDescriptorPool(_device, draw.DescriptorPool, null);
                }
            }
        }
        VulkanComputeSubmissionResource[]? computes =
            Interlocked.Exchange(ref _computeResources, null);
        if (computes is not null)
        {
            foreach (VulkanComputeSubmissionResource compute in computes)
            {
                if (compute.DescriptorPool.Handle != 0)
                {
                    _api.DestroyDescriptorPool(
                        _device,
                        compute.DescriptorPool,
                        null);
                }
            }
        }
    }

    private Result WaitForCompletion()
    {
        Result result = _api.WaitForFences(
            _device,
            1,
            in _fence,
            true,
            ulong.MaxValue);
        if (_selectionOutlineSubmission &&
            _owner.TryConsumeSelectionOutlineFenceFailureForTesting(
                out Result injected))
        {
            result = injected;
        }
        if (result == Result.ErrorDeviceLost)
        {
            _owner.NotifyPickDeviceLost();
            if (_selectionOutlineSubmission)
            {
                _owner.NotifySelectionOutlineDeviceLost();
            }
        }
        return result;
    }

    private bool IsTerminal(Result result) =>
        result == Result.Success ||
        result == Result.ErrorDeviceLost ||
        (_selectionOutlineSubmission &&
         result != Result.NotReady &&
         result != Result.Timeout);
}
