// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using global::Silk.NET.Vulkan;
using VkBuffer = global::Silk.NET.Vulkan.Buffer;

namespace OpenUsd.Rendering.Silk.Vulkan;

public sealed unsafe partial class VulkanSilkGraphicsDevice
{
    private long _pickDeviceGeneration = 1;
    private ulong _pickNonCoherentAtomSize = 1;
    private ulong _pickCopyOffsetAlignment = 4;
    private ulong _pickUniformOffsetAlignment = 16;
    private int _suppressPickCompletionsForTesting;
    private int _nextPickSubmissionFailureForTesting;
    private int _nextPickFenceFailureForTesting;
    private int _pickDeviceLostNotified;
    private long _pickPipelineCreations;
    private long _pickShaderModuleCreations;
    private long _pickReadbackBufferCreations;
    private long _pickCommandPoolCreations;
    private long _pickFenceCreations;
    private long _pickFramebufferCreations;
    private long _pickDescriptorPoolCreations;
    private long _pickUniformBufferCreations;
    private long _pickSecondaryCommandRecordings;
    private long _pickSubmissions;
    private long _pickCompletionPolls;
    private long _pickFenceWaitCalls;
    private long _pickReadbacks;
    private long _pickCopies;
    private long _pickDeviceLosses;
    private long _pickLiveBuffers;
    private long _pickLiveCommandPools;
    private long _pickLiveFences;
    private long _pickLiveMappings;
    private long _pickLiveDependentObjects;

    /// <inheritdoc/>
    public ulong PickDeviceGeneration =>
        unchecked((ulong)Interlocked.Read(ref _pickDeviceGeneration));

    /// <inheritdoc/>
    public ISilkPickGraphicsPipeline CreatePickGraphicsPipeline(
        SilkPickPipelineDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        descriptor.Validate();
        if (descriptor.VertexShader.Format != SilkShaderBinaryFormat.SpirV)
        {
            throw new ArgumentException(
                "Vulkan pick pipelines require checked SPIR-V shaders.",
                nameof(descriptor));
        }

        RegisterDependentObject();
        try
        {
            VulkanSilkPickGraphicsPipeline pipeline =
                VulkanSilkPickGraphicsPipeline.Create(
                    this,
                    _api,
                    _device,
                    descriptor,
                    PickDeviceGeneration);
            Interlocked.Increment(ref _pickPipelineCreations);
            Interlocked.Add(ref _pickShaderModuleCreations, 2);
            Interlocked.Increment(ref _pickLiveDependentObjects);
            return pipeline;
        }
        catch
        {
            ReleaseDependentObject();
            throw;
        }
    }

    /// <inheritdoc/>
    public ISilkPickReadbackBuffer CreatePickReadbackBuffer()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RegisterDependentObject();
        try
        {
            var buffer = new VulkanSilkPickReadbackBuffer(
                this,
                _api,
                _device,
                _queue,
                _queueFamily,
                PickDeviceGeneration,
                _pickNonCoherentAtomSize,
                _pickCopyOffsetAlignment,
                _pickUniformOffsetAlignment);
            buffer.RegisterDiagnostics();
            Interlocked.Increment(ref _pickReadbackBufferCreations);
            Interlocked.Increment(ref _pickCommandPoolCreations);
            Interlocked.Increment(ref _pickFenceCreations);
            return buffer;
        }
        catch
        {
            ReleaseDependentObject();
            throw;
        }
    }

    internal VulkanSilkPickingDiagnostics PickDiagnostics => new(
        PickDeviceGeneration,
        Interlocked.Read(ref _pickPipelineCreations),
        Interlocked.Read(ref _pickShaderModuleCreations),
        Interlocked.Read(ref _pickReadbackBufferCreations),
        Interlocked.Read(ref _pickCommandPoolCreations),
        Interlocked.Read(ref _pickFenceCreations),
        Interlocked.Read(ref _pickFramebufferCreations),
        Interlocked.Read(ref _pickDescriptorPoolCreations),
        Interlocked.Read(ref _pickUniformBufferCreations),
        Interlocked.Read(ref _pickSecondaryCommandRecordings),
        Interlocked.Read(ref _pickSubmissions),
        Interlocked.Read(ref _pickCompletionPolls),
        Interlocked.Read(ref _pickFenceWaitCalls),
        Interlocked.Read(ref _pickReadbacks),
        Interlocked.Read(ref _pickCopies),
        Interlocked.Read(ref _pickDeviceLosses),
        Interlocked.Read(ref _pickLiveBuffers),
        Interlocked.Read(ref _pickLiveCommandPools),
        Interlocked.Read(ref _pickLiveFences),
        Interlocked.Read(ref _pickLiveMappings),
        Interlocked.Read(ref _pickLiveDependentObjects));

    internal bool SuppressPickCompletionsForTesting =>
        Volatile.Read(ref _suppressPickCompletionsForTesting) != 0;

    internal void SetPickCompletionsSuppressedForTesting(bool suppressed) =>
        Volatile.Write(
            ref _suppressPickCompletionsForTesting,
            suppressed ? 1 : 0);

    internal void AdvancePickDeviceGenerationForTesting() =>
        AdvancePickDeviceGeneration(deviceLost: false);

    internal void FailNextPickSubmissionForTesting(bool deviceLost) =>
        Interlocked.Exchange(
            ref _nextPickSubmissionFailureForTesting,
            deviceLost ? 2 : 1);

    internal void FailNextPickFenceForTesting(bool deviceLost) =>
        Interlocked.Exchange(
            ref _nextPickFenceFailureForTesting,
            deviceLost ? 2 : 1);

    internal bool IsPickDeviceLost =>
        Volatile.Read(ref _pickDeviceLostNotified) != 0;

    internal void NotifyPickDeviceLost()
    {
        if (Interlocked.Exchange(ref _pickDeviceLostNotified, 1) == 0)
        {
            AdvancePickDeviceGeneration(deviceLost: true);
        }
    }

    internal void CountPickCompletionPoll() =>
        Interlocked.Increment(ref _pickCompletionPolls);

    internal void CountPickFenceWait() =>
        Interlocked.Increment(ref _pickFenceWaitCalls);

    internal void CountPickReadback() =>
        Interlocked.Increment(ref _pickReadbacks);

    internal void CountPickFramebufferCreation() =>
        Interlocked.Increment(ref _pickFramebufferCreations);

    internal void CountPickDescriptorPoolCreation() =>
        Interlocked.Increment(ref _pickDescriptorPoolCreations);

    internal void CountPickUniformBufferCreation() =>
        Interlocked.Increment(ref _pickUniformBufferCreations);

    internal void CountPickSecondaryCommandRecording() =>
        Interlocked.Increment(ref _pickSecondaryCommandRecordings);

    internal void RegisterPickReadbackResources()
    {
        Interlocked.Increment(ref _pickLiveBuffers);
        Interlocked.Increment(ref _pickLiveCommandPools);
        Interlocked.Increment(ref _pickLiveFences);
        Interlocked.Increment(ref _pickLiveMappings);
        Interlocked.Increment(ref _pickLiveDependentObjects);
    }

    internal void ReleasePickReadbackResources()
    {
        Interlocked.Decrement(ref _pickLiveBuffers);
        Interlocked.Decrement(ref _pickLiveCommandPools);
        Interlocked.Decrement(ref _pickLiveFences);
        Interlocked.Decrement(ref _pickLiveMappings);
    }

    internal void RegisterPickBufferMapping()
    {
        Interlocked.Increment(ref _pickLiveBuffers);
        Interlocked.Increment(ref _pickLiveMappings);
    }

    internal void ReleasePickBufferMapping()
    {
        Interlocked.Decrement(ref _pickLiveBuffers);
        Interlocked.Decrement(ref _pickLiveMappings);
    }

    internal void ReleasePickPipelineDependent()
    {
        Interlocked.Decrement(ref _pickLiveDependentObjects);
        ReleaseDependentObject();
    }

    internal void ReleasePickReadbackDependent()
    {
        Interlocked.Decrement(ref _pickLiveDependentObjects);
        ReleaseDependentObject();
    }

    internal bool TryConsumePickFenceFailureForTesting(out Result result)
    {
        int failure = Interlocked.Exchange(
            ref _nextPickFenceFailureForTesting,
            0);
        result = failure switch
        {
            2 => Result.ErrorDeviceLost,
            1 => Result.ErrorOutOfHostMemory,
            _ => Result.Success
        };
        return failure != 0;
    }

    internal uint FindPickHostVisibleMemoryType(
        uint typeBits,
        out MemoryPropertyFlags properties)
    {
        const MemoryPropertyFlags required = MemoryPropertyFlags.HostVisibleBit;
        const MemoryPropertyFlags preferred =
            MemoryPropertyFlags.HostVisibleBit |
            MemoryPropertyFlags.HostCoherentBit;
        for (uint index = 0; index < _memoryProperties.MemoryTypeCount; index++)
        {
            MemoryPropertyFlags flags =
                _memoryProperties.MemoryTypes[(int)index].PropertyFlags;
            if ((typeBits & (1u << (int)index)) != 0 &&
                (flags & preferred) == preferred)
            {
                properties = flags;
                return index;
            }
        }
        for (uint index = 0; index < _memoryProperties.MemoryTypeCount; index++)
        {
            MemoryPropertyFlags flags =
                _memoryProperties.MemoryTypes[(int)index].PropertyFlags;
            if ((typeBits & (1u << (int)index)) != 0 &&
                (flags & required) == required)
            {
                properties = flags;
                return index;
            }
        }
        throw new PlatformNotSupportedException(
            "No Vulkan host-visible memory type is available for pick readback.");
    }

    private void InitializePicking()
    {
        _api.GetPhysicalDeviceProperties(
            _physicalDevice,
            out PhysicalDeviceProperties properties);
        _pickNonCoherentAtomSize = Math.Max(
            1,
            properties.Limits.NonCoherentAtomSize);
        _pickCopyOffsetAlignment = Math.Max(
            4,
            properties.Limits.OptimalBufferCopyOffsetAlignment);
        _pickUniformOffsetAlignment = Math.Max(
            16,
            properties.Limits.MinUniformBufferOffsetAlignment);
    }

    private VulkanSilkPickSubmission SubmitPick(
        VulkanSilkGraphicsCommandList commands)
    {
        int failure = Interlocked.Exchange(
            ref _nextPickSubmissionFailureForTesting,
            0);
        if (failure != 0)
        {
            if (failure == 2)
            {
                NotifyPickDeviceLost();
            }
            throw new InvalidOperationException(
                failure == 2
                    ? "Injected Vulkan pick device loss."
                    : "Injected Vulkan pick submission failure.");
        }

        VulkanSilkPickGraphicsPipeline pipeline = commands.PickPipeline ??
            throw new InvalidOperationException(
                "The Vulkan pick command list has no pick pipeline.");
        VulkanSilkGraphicsTexture color = commands.PickColorAttachment ??
            throw new InvalidOperationException(
                "The Vulkan pick command list has no color attachment.");
        VulkanSilkGraphicsTexture depth = commands.PickDepthAttachment ??
            throw new InvalidOperationException(
                "The Vulkan pick command list has no depth attachment.");
        VulkanSilkPickReadbackBuffer readback = commands.PickReadback ??
            throw new InvalidOperationException(
                "The Vulkan pick command list has no readback slot.");
        if (!commands.TryGetPickClearValues(
                out SilkColor clearColor,
                out float clearDepth))
        {
            throw new InvalidOperationException(
                "A Vulkan pick pass requires explicit ID and depth clears.");
        }

        IReadOnlyList<VulkanPickDrawCommand> draws = commands.PickDraws;
        var leases = new IDisposable[checked(3 + (draws.Count * 3))];
        int acquired = 0;
        try
        {
            leases[acquired++] = pipeline.AcquireLease();
            leases[acquired++] = color.AcquireLease();
            leases[acquired++] = depth.AcquireLease();
            foreach (VulkanPickDrawCommand draw in draws)
            {
                leases[acquired++] = draw.VertexBuffer.AcquireLease();
                leases[acquired++] = draw.IndexBuffer.AcquireLease();
                leases[acquired++] = draw.UniformBuffer.AcquireLease();
            }

            ulong serial = readback.RecordAndSubmit(
                pipeline,
                color,
                depth,
                clearColor,
                clearDepth,
                draws,
                commands.PickCoordinate);
            Interlocked.Increment(ref _pickSubmissions);
            Interlocked.Increment(ref _pickCopies);
            return new VulkanSilkPickSubmission(
                readback,
                serial,
                leases);
        }
        catch
        {
            for (int index = acquired - 1; index >= 0; index--)
            {
                leases[index].Dispose();
            }
            throw;
        }
    }

    private void AdvancePickDeviceGeneration(bool deviceLost)
    {
        long generation = Interlocked.Increment(ref _pickDeviceGeneration);
        if (generation == 0)
        {
            _ = Interlocked.Increment(ref _pickDeviceGeneration);
        }
        if (deviceLost)
        {
            Interlocked.Increment(ref _pickDeviceLosses);
        }
    }
}

internal readonly record struct VulkanSilkPickingDiagnostics(
    ulong DeviceGeneration,
    long PipelineCreations,
    long ShaderModuleCreations,
    long ReadbackBufferCreations,
    long CommandPoolCreations,
    long FenceCreations,
    long FramebufferCreations,
    long DescriptorPoolCreations,
    long UniformBufferCreations,
    long SecondaryCommandRecordings,
    long Submissions,
    long CompletionPolls,
    long FenceWaitCalls,
    long Readbacks,
    long Copies,
    long DeviceLosses,
    long LiveBuffers,
    long LiveCommandPools,
    long LiveFences,
    long LiveMappings,
    long LiveDependentObjects);

internal sealed unsafe class VulkanSilkPickGraphicsPipeline :
    SilkGraphicsResourceBase,
    ISilkPickGraphicsPipeline
{
    private readonly Vk _api;
    private readonly Device _device;
    private ShaderModule _vertexShader;
    private ShaderModule _fragmentShader;
    private DescriptorSetLayout _descriptorSetLayout;
    private PipelineLayout _layout;
    private RenderPass _renderPass;
    private Pipeline _pipeline;

    private VulkanSilkPickGraphicsPipeline(
        VulkanSilkGraphicsDevice owner,
        Vk api,
        Device device,
        SilkPickPipelineDescriptor descriptor,
        ulong deviceGeneration,
        ShaderModule vertexShader,
        ShaderModule fragmentShader,
        DescriptorSetLayout descriptorSetLayout,
        PipelineLayout layout,
        RenderPass renderPass,
        Pipeline pipeline)
    {
        Owner = owner;
        _api = api;
        _device = device;
        Descriptor = descriptor;
        DeviceGeneration = deviceGeneration;
        _vertexShader = vertexShader;
        _fragmentShader = fragmentShader;
        _descriptorSetLayout = descriptorSetLayout;
        _layout = layout;
        _renderPass = renderPass;
        _pipeline = pipeline;
    }

    public SilkPickPipelineDescriptor Descriptor { get; }

    internal VulkanSilkGraphicsDevice Owner { get; }

    internal ulong DeviceGeneration { get; }

    internal DescriptorSetLayout DescriptorSetLayout => _descriptorSetLayout;

    internal PipelineLayout Layout => _layout;

    internal RenderPass RenderPass => _renderPass;

    internal Pipeline Pipeline => _pipeline;

    internal static VulkanSilkPickGraphicsPipeline Create(
        VulkanSilkGraphicsDevice owner,
        Vk api,
        Device device,
        SilkPickPipelineDescriptor descriptor,
        ulong deviceGeneration)
    {
        ShaderModule vertexShader = default;
        ShaderModule fragmentShader = default;
        DescriptorSetLayout descriptorSetLayout = default;
        PipelineLayout layout = default;
        RenderPass renderPass = default;
        Pipeline pipeline = default;
        try
        {
            vertexShader = CreateShaderModule(
                api,
                device,
                descriptor.VertexShader.Code.ToArray());
            fragmentShader = CreateShaderModule(
                api,
                device,
                LoadShader("pick.replay.fragment.spv"));
            descriptorSetLayout = CreateDescriptorSetLayout(api, device);
            layout = CreatePipelineLayout(api, device, descriptorSetLayout);
            renderPass = CreateRenderPass(api, device);
            pipeline = CreatePipeline(
                api,
                device,
                descriptor.VertexShader.EntryPoint,
                vertexShader,
                fragmentShader,
                layout,
                renderPass);
            return new VulkanSilkPickGraphicsPipeline(
                owner,
                api,
                device,
                descriptor,
                deviceGeneration,
                vertexShader,
                fragmentShader,
                descriptorSetLayout,
                layout,
                renderPass,
                pipeline);
        }
        catch
        {
            if (pipeline.Handle != 0)
            {
                api.DestroyPipeline(device, pipeline, null);
            }
            if (renderPass.Handle != 0)
            {
                api.DestroyRenderPass(device, renderPass, null);
            }
            if (layout.Handle != 0)
            {
                api.DestroyPipelineLayout(device, layout, null);
            }
            if (descriptorSetLayout.Handle != 0)
            {
                api.DestroyDescriptorSetLayout(
                    device,
                    descriptorSetLayout,
                    null);
            }
            if (fragmentShader.Handle != 0)
            {
                api.DestroyShaderModule(device, fragmentShader, null);
            }
            if (vertexShader.Handle != 0)
            {
                api.DestroyShaderModule(device, vertexShader, null);
            }
            throw;
        }
    }

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfDisposed() => ThrowIfResourceDisposed();

    protected override void ReleaseNative()
    {
        if (_pipeline.Handle != 0)
        {
            _api.DestroyPipeline(_device, _pipeline, null);
            _pipeline = default;
        }
        if (_renderPass.Handle != 0)
        {
            _api.DestroyRenderPass(_device, _renderPass, null);
            _renderPass = default;
        }
        if (_layout.Handle != 0)
        {
            _api.DestroyPipelineLayout(_device, _layout, null);
            _layout = default;
        }
        if (_descriptorSetLayout.Handle != 0)
        {
            _api.DestroyDescriptorSetLayout(
                _device,
                _descriptorSetLayout,
                null);
            _descriptorSetLayout = default;
        }
        if (_fragmentShader.Handle != 0)
        {
            _api.DestroyShaderModule(_device, _fragmentShader, null);
            _fragmentShader = default;
        }
        if (_vertexShader.Handle != 0)
        {
            _api.DestroyShaderModule(_device, _vertexShader, null);
            _vertexShader = default;
        }
        Owner.ReleasePickPipelineDependent();
    }

    private static ShaderModule CreateShaderModule(
        Vk api,
        Device device,
        byte[] code)
    {
        fixed (byte* codePointer = code)
        {
            var createInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = checked((nuint)code.Length),
                PCode = (uint*)codePointer
            };
            ShaderModule module = default;
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                api.CreateShaderModule(device, &createInfo, null, &module),
                "vkCreateShaderModule(pick)");
            return module;
        }
    }

    private static byte[] LoadShader(string fileName)
    {
        string resourceName =
            $"OpenUsd.Rendering.Silk.Vulkan.Shaders.{fileName}";
        using Stream stream =
            typeof(VulkanSilkPickGraphicsPipeline).Assembly
                .GetManifestResourceStream(resourceName) ??
            throw new InvalidDataException(
                $"Embedded Vulkan pick shader '{resourceName}' is missing.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static DescriptorSetLayout CreateDescriptorSetLayout(
        Vk api,
        Device device)
    {
        DescriptorSetLayoutBinding* bindings =
            stackalloc DescriptorSetLayoutBinding[2];
        bindings[0] = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            StageFlags =
                ShaderStageFlags.VertexBit |
                ShaderStageFlags.FragmentBit
        };
        bindings[1] = new DescriptorSetLayoutBinding
        {
            Binding = 1,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            StageFlags =
                ShaderStageFlags.VertexBit |
                ShaderStageFlags.FragmentBit
        };
        var createInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 2,
            PBindings = bindings
        };
        DescriptorSetLayout layout = default;
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            api.CreateDescriptorSetLayout(device, &createInfo, null, &layout),
            "vkCreateDescriptorSetLayout(pick)");
        return layout;
    }

    private static PipelineLayout CreatePipelineLayout(
        Vk api,
        Device device,
        DescriptorSetLayout descriptorSetLayout)
    {
        var createInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &descriptorSetLayout
        };
        PipelineLayout layout = default;
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            api.CreatePipelineLayout(device, &createInfo, null, &layout),
            "vkCreatePipelineLayout(pick)");
        return layout;
    }

    private static RenderPass CreateRenderPass(Vk api, Device device)
    {
        AttachmentDescription* attachments =
            stackalloc AttachmentDescription[2];
        attachments[0] = new AttachmentDescription
        {
            Format = Format.R8G8B8A8Unorm,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.ColorAttachmentOptimal,
            FinalLayout = ImageLayout.ColorAttachmentOptimal
        };
        attachments[1] = new AttachmentDescription
        {
            Format = Format.D32Sfloat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.DepthStencilAttachmentOptimal,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
        };
        var colorReference = new AttachmentReference(
            0,
            ImageLayout.ColorAttachmentOptimal);
        var depthReference = new AttachmentReference(
            1,
            ImageLayout.DepthStencilAttachmentOptimal);
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorReference,
            PDepthStencilAttachment = &depthReference
        };
        var dependency = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask =
                PipelineStageFlags.ColorAttachmentOutputBit |
                PipelineStageFlags.EarlyFragmentTestsBit,
            DstStageMask =
                PipelineStageFlags.ColorAttachmentOutputBit |
                PipelineStageFlags.EarlyFragmentTestsBit,
            SrcAccessMask =
                AccessFlags.ColorAttachmentWriteBit |
                AccessFlags.DepthStencilAttachmentWriteBit,
            DstAccessMask =
                AccessFlags.ColorAttachmentWriteBit |
                AccessFlags.DepthStencilAttachmentWriteBit
        };
        var createInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 2,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency
        };
        RenderPass renderPass = default;
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            api.CreateRenderPass(device, &createInfo, null, &renderPass),
            "vkCreateRenderPass(pick)");
        return renderPass;
    }

    private static Pipeline CreatePipeline(
        Vk api,
        Device device,
        string vertexEntryPoint,
        ShaderModule vertexShader,
        ShaderModule fragmentShader,
        PipelineLayout layout,
        RenderPass renderPass)
    {
        byte[] vertexEntry = System.Text.Encoding.UTF8.GetBytes(
            vertexEntryPoint + "\0");
        byte[] fragmentEntry = "main\0"u8.ToArray();
        fixed (byte* vertexEntryPointer = vertexEntry)
        fixed (byte* fragmentEntryPointer = fragmentEntry)
        {
            PipelineShaderStageCreateInfo* stages =
                stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = vertexShader,
                PName = vertexEntryPointer
            };
            stages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = fragmentShader,
                PName = fragmentEntryPointer
            };
            var binding = new VertexInputBindingDescription(
                0,
                24,
                VertexInputRate.Vertex);
            VertexInputAttributeDescription* attributes =
                stackalloc VertexInputAttributeDescription[2];
            attributes[0] = new VertexInputAttributeDescription(
                0,
                0,
                Format.R32G32B32Sfloat,
                0);
            attributes[1] = new VertexInputAttributeDescription(
                1,
                0,
                Format.R32G32B32Sfloat,
                12);
            var vertexInput = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 1,
                PVertexBindingDescriptions = &binding,
                VertexAttributeDescriptionCount = 2,
                PVertexAttributeDescriptions = attributes
            };
            var inputAssembly = new PipelineInputAssemblyStateCreateInfo
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = PrimitiveTopology.TriangleList
            };
            var viewportState = new PipelineViewportStateCreateInfo
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1,
                ScissorCount = 1
            };
            var rasterization = new PipelineRasterizationStateCreateInfo
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                PolygonMode = PolygonMode.Fill,
                CullMode = CullModeFlags.None,
                FrontFace = FrontFace.CounterClockwise,
                LineWidth = 1
            };
            var multisample = new PipelineMultisampleStateCreateInfo
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                RasterizationSamples = SampleCountFlags.Count1Bit
            };
            var depthStencil = new PipelineDepthStencilStateCreateInfo
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = true,
                DepthWriteEnable = true,
                DepthCompareOp = CompareOp.LessOrEqual
            };
            var colorAttachment = new PipelineColorBlendAttachmentState
            {
                BlendEnable = false,
                ColorWriteMask =
                    ColorComponentFlags.RBit |
                    ColorComponentFlags.GBit |
                    ColorComponentFlags.BBit |
                    ColorComponentFlags.ABit
            };
            var colorBlend = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = 1,
                PAttachments = &colorAttachment
            };
            DynamicState* dynamicStates = stackalloc DynamicState[2]
            {
                DynamicState.Viewport,
                DynamicState.Scissor
            };
            var dynamicState = new PipelineDynamicStateCreateInfo
            {
                SType = StructureType.PipelineDynamicStateCreateInfo,
                DynamicStateCount = 2,
                PDynamicStates = dynamicStates
            };
            var pipelineInfo = new GraphicsPipelineCreateInfo
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                StageCount = 2,
                PStages = stages,
                PVertexInputState = &vertexInput,
                PInputAssemblyState = &inputAssembly,
                PViewportState = &viewportState,
                PRasterizationState = &rasterization,
                PMultisampleState = &multisample,
                PDepthStencilState = &depthStencil,
                PColorBlendState = &colorBlend,
                PDynamicState = &dynamicState,
                Layout = layout,
                RenderPass = renderPass,
                Subpass = 0
            };
            Pipeline pipeline = default;
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                api.CreateGraphicsPipelines(
                    device,
                    default,
                    1,
                    &pipelineInfo,
                    null,
                    &pipeline),
                "vkCreateGraphicsPipelines(pick)");
            return pipeline;
        }
    }
}

internal sealed partial class VulkanSilkGraphicsCommandList
{
    private readonly List<VulkanPickDrawCommand> _pickDraws = [];
    private VulkanSilkPickGraphicsPipeline? _pickPipeline;
    private VulkanSilkGraphicsTexture? _pickColorAttachment;
    private VulkanSilkGraphicsTexture? _pickDepthAttachment;
    private VulkanSilkPickReadbackBuffer? _pickReadback;
    private SilkTexturePixelCoordinate _pickCoordinate;
    private uint _pickBaseToken;

    internal bool HasPickSubmission => _pickReadback is not null;

    internal VulkanSilkPickGraphicsPipeline? PickPipeline => _pickPipeline;

    internal VulkanSilkGraphicsTexture? PickColorAttachment =>
        _pickColorAttachment;

    internal VulkanSilkGraphicsTexture? PickDepthAttachment =>
        _pickDepthAttachment;

    internal VulkanSilkPickReadbackBuffer? PickReadback => _pickReadback;

    internal SilkTexturePixelCoordinate PickCoordinate => _pickCoordinate;

    internal IReadOnlyList<VulkanPickDrawCommand> PickDraws => _pickDraws;

    public void SetPickGraphicsPipeline(ISilkPickGraphicsPipeline pipeline)
    {
        ThrowIfRendering();
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not VulkanSilkPickGraphicsPipeline vulkanPipeline ||
            !ReferenceEquals(vulkanPipeline.Owner, Device))
        {
            throw new ArgumentException(
                "The pick pipeline was not created by this Vulkan device.",
                nameof(pipeline));
        }
        vulkanPipeline.ThrowIfDisposed();
        if (vulkanPipeline.DeviceGeneration != Device.PickDeviceGeneration)
        {
            throw new InvalidOperationException(
                "The Vulkan pick pipeline belongs to a stale device generation.");
        }

        _pickPipeline = vulkanPipeline;
        _pipeline = null;
        _pickColorAttachment = _colorAttachment;
        _pickDepthAttachment = _depthAttachment;
        _pickBaseToken = 0;
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
    }

    public void CopyRgba8Pixel(
        ISilkGraphicsTexture source,
        SilkTexturePixelCoordinate coordinate,
        ISilkPickReadbackBuffer destination)
    {
        ThrowIfOutsideRendering();
        VulkanSilkGraphicsTexture texture = ValidateTexture(source);
        coordinate.Validate(texture);
        ArgumentNullException.ThrowIfNull(destination);
        if (destination is not VulkanSilkPickReadbackBuffer readback ||
            !ReferenceEquals(readback.Owner, Device))
        {
            throw new ArgumentException(
                "The pick readback buffer was not created by this Vulkan device.",
                nameof(destination));
        }
        readback.ThrowIfDisposed();
        if (_pickPipeline is null ||
            _pickColorAttachment is null ||
            _pickDepthAttachment is null ||
            !ReferenceEquals(texture, _pickColorAttachment))
        {
            throw new InvalidOperationException(
                "CopyRgba8Pixel must follow one completed Vulkan pick rendering scope.");
        }
        if (_pickReadback is not null)
        {
            throw new InvalidOperationException(
                "A Vulkan pick command list can copy only one ID pixel.");
        }

        _pickReadback = readback;
        _pickCoordinate = coordinate;
    }

    internal bool TryGetPickClearValues(
        out SilkColor clearColor,
        out float clearDepth)
    {
        bool foundColor = false;
        bool foundDepth = false;
        clearColor = default;
        clearDepth = default;
        foreach (VulkanGraphicsCommand command in _commands)
        {
            if (command.Kind == SilkGraphicsCommandKind.ClearColor &&
                ReferenceEquals(command.Texture, _pickColorAttachment))
            {
                clearColor = command.Color;
                foundColor = true;
            }
            else if (command.Kind == SilkGraphicsCommandKind.ClearDepth &&
                     ReferenceEquals(command.Texture, _pickDepthAttachment))
            {
                clearDepth = command.Depth;
                foundDepth = true;
            }
        }
        return foundColor && foundDepth;
    }

    private void BeginPickRenderingScope()
    {
        _pickPipeline = null;
        _pickColorAttachment = null;
        _pickDepthAttachment = null;
        _pickReadback = null;
        _pickCoordinate = default;
        _pickBaseToken = 0;
        _pickDraws.Clear();
    }

    private void RecordPickDraw(
        VulkanSilkGraphicsBuffer vertexBuffer,
        VulkanSilkGraphicsBuffer indexBuffer,
        VulkanSilkGraphicsBuffer uniformBuffer,
        uint indexCount)
    {
        if (_pickBaseToken == 0 ||
            _viewport is null ||
            _scissor is null ||
            indexCount % 3 != 0)
        {
            throw new InvalidOperationException(
                "A Vulkan pick draw requires a nonzero base token, viewport, and scissor.");
        }
        _pickDraws.Add(new VulkanPickDrawCommand(
            vertexBuffer,
            indexBuffer,
            uniformBuffer,
            _viewport.Value,
            _scissor.Value,
            _pickBaseToken,
            indexCount));
    }

    private void ValidatePickSubmission()
    {
        if (_pickPipeline is null && _pickReadback is null)
        {
            return;
        }
        if (_pickPipeline is null ||
            _pickColorAttachment is null ||
            _pickDepthAttachment is null ||
            _pickReadback is null)
        {
            throw new InvalidOperationException(
                "The Vulkan pick pass must end with one exact pixel copy.");
        }
        if (!TryGetPickClearValues(
                out SilkColor clearColor,
                out float clearDepth) ||
            clearColor != new SilkColor(0, 0, 0, 0) ||
            clearDepth != 1)
        {
            throw new InvalidOperationException(
                "The Vulkan pick pass must clear token zero and depth one.");
        }
    }

    private void DisposePickState()
    {
        _pickDraws.Clear();
        _pickPipeline = null;
        _pickColorAttachment = null;
        _pickDepthAttachment = null;
        _pickReadback = null;
    }
}

internal readonly record struct VulkanPickDrawCommand(
    VulkanSilkGraphicsBuffer VertexBuffer,
    VulkanSilkGraphicsBuffer IndexBuffer,
    VulkanSilkGraphicsBuffer UniformBuffer,
    SilkViewport Viewport,
    SilkScissor Scissor,
    uint BaseToken,
    uint IndexCount);

internal readonly record struct VulkanPickDrawCacheEntry(
    VulkanSilkGraphicsBuffer VertexBuffer,
    VulkanSilkGraphicsBuffer IndexBuffer,
    SilkViewport Viewport,
    SilkScissor Scissor,
    uint IndexCount);

internal sealed unsafe class VulkanSilkPickReadbackBuffer :
    ISilkPickReadbackBuffer
{
    private readonly Vk _api;
    private readonly Device _device;
    private readonly Queue _queue;
    private readonly ulong _deviceGeneration;
    private readonly ulong _nonCoherentAtomSize;
    private readonly ulong _uniformOffsetAlignment;
    private readonly ulong _copyOffset;
    private readonly ulong _copyRangeSize;
    private VkBuffer _buffer;
    private DeviceMemory _memory;
    private ulong _allocationSize;
    private nint _mapped;
    private CommandPool _commandPool;
    private CommandBuffer _commands;
    private CommandBuffer _secondaryCommands;
    private Fence _fence;
    private Framebuffer _framebuffer;
    private VulkanSilkPickGraphicsPipeline? _framebufferPipeline;
    private VulkanSilkGraphicsTexture? _framebufferColor;
    private VulkanSilkGraphicsTexture? _framebufferDepth;
    private IDisposable? _framebufferPipelineLease;
    private IDisposable? _framebufferColorLease;
    private IDisposable? _framebufferDepthLease;
    private DescriptorPool _descriptorPool;
    private DescriptorSet[]? _descriptorSets;
    private VulkanSilkPickGraphicsPipeline? _descriptorPipeline;
    private VkBuffer _pickUniformBuffer;
    private DeviceMemory _pickUniformMemory;
    private ulong _pickUniformAllocationSize;
    private nint _pickUniformMapped;
    private int _descriptorCapacity;
    private VulkanPickDrawCacheEntry[]? _secondaryDraws;
    private int _secondaryDrawCount;
    private VulkanSilkPickGraphicsPipeline? _secondaryPipeline;
    private uint _secondaryWidth;
    private uint _secondaryHeight;
    private ulong _nextSerial = 1;
    private ulong _activeSerial;
    private PickSlotState _state;
    private bool _disposed;
    private bool _diagnosticsRegistered;
    private bool _pickUniformDiagnosticsRegistered;
    private bool _dependentReleased;

    internal VulkanSilkPickReadbackBuffer(
        VulkanSilkGraphicsDevice owner,
        Vk api,
        Device device,
        Queue queue,
        uint queueFamily,
        ulong deviceGeneration,
        ulong nonCoherentAtomSize,
        ulong copyOffsetAlignment,
        ulong uniformOffsetAlignment)
    {
        Owner = owner;
        _api = api;
        _device = device;
        _queue = queue;
        _deviceGeneration = deviceGeneration;
        _nonCoherentAtomSize = Math.Max(1, nonCoherentAtomSize);
        _uniformOffsetAlignment = Math.Max(16, uniformOffsetAlignment);
        ulong requiredCopyAlignment = Math.Max(
            _nonCoherentAtomSize,
            Math.Max(4, copyOffsetAlignment));
        _copyOffset = requiredCopyAlignment;
        _copyRangeSize = AlignUp(
            SilkPickTokenEncoding.ByteSize,
            _nonCoherentAtomSize);
        try
        {
            CreateReadbackBuffer();
            CreateExecutionResources(queueFamily);
        }
        catch
        {
            DestroyNativeResources();
            throw;
        }
    }

    public int ByteSize => SilkPickTokenEncoding.ByteSize;

    internal VulkanSilkGraphicsDevice Owner { get; }

    internal void RegisterDiagnostics()
    {
        if (_diagnosticsRegistered)
        {
            throw new InvalidOperationException(
                "Vulkan pick readback diagnostics were already registered.");
        }
        _diagnosticsRegistered = true;
        Owner.RegisterPickReadbackResources();
    }

    public void ReadRgba8Pixel(Span<byte> destination)
    {
        ThrowIfDisposed();
        if (destination.Length != ByteSize)
        {
            throw new ArgumentException(
                "A Vulkan pick readback requires exactly four RGBA bytes.",
                nameof(destination));
        }
        if (_state == PickSlotState.Failed)
        {
            throw new InvalidOperationException(
                "The Vulkan pick readback failed before completion.");
        }
        if (_state != PickSlotState.Completed)
        {
            throw new InvalidOperationException(
                "The Vulkan pick readback is not complete.");
        }

        var range = new MappedMemoryRange
        {
            SType = StructureType.MappedMemoryRange,
            Memory = _memory,
            Offset = _copyOffset,
            Size = _copyRangeSize
        };
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.InvalidateMappedMemoryRanges(_device, 1, &range),
            "vkInvalidateMappedMemoryRanges(pick)");
        new ReadOnlySpan<byte>(
                (byte*)_mapped + checked((int)_copyOffset),
                ByteSize)
            .CopyTo(destination);
        _state = PickSlotState.Read;
        Owner.CountPickReadback();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        try
        {
            if (_state == PickSlotState.Submitted)
            {
                CompleteForTeardown();
            }
        }
        finally
        {
            try
            {
                DestroyNativeResources();
            }
            finally
            {
                _activeSerial = 0;
                _state = PickSlotState.Disposed;
                ReleaseDependent();
            }
        }
    }

    internal void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    internal ulong RecordAndSubmit(
        VulkanSilkPickGraphicsPipeline pipeline,
        VulkanSilkGraphicsTexture color,
        VulkanSilkGraphicsTexture depth,
        SilkColor clearColor,
        float clearDepth,
        IReadOnlyList<VulkanPickDrawCommand> draws,
        SilkTexturePixelCoordinate coordinate)
    {
        ThrowIfDisposed();
        if (_state != PickSlotState.Idle)
        {
            throw new InvalidOperationException(
                "The persistent Vulkan pick readback slot is still in use.");
        }
        if (_deviceGeneration != Owner.PickDeviceGeneration ||
            pipeline.DeviceGeneration != _deviceGeneration)
        {
            throw new InvalidOperationException(
                "The Vulkan pick resources belong to a stale device generation.");
        }
        if (coordinate.X >= color.Width || coordinate.Y >= color.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(coordinate));
        }

        EnsureFramebuffer(pipeline, color, depth);
        int primitiveCount = CountPrimitives(draws);
        EnsureDescriptorCapacity(pipeline, primitiveCount);
        UpdateDescriptorsAndTokens(draws);
        EnsureSecondaryCommands(pipeline, color, draws);

        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.ResetFences(_device, 1, in _fence),
            "vkResetFences(pick)");
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.ResetCommandBuffer(
                _commands,
                CommandBufferResetFlags.None),
            "vkResetCommandBuffer(pick)");
        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.BeginCommandBuffer(_commands, &beginInfo),
            "vkBeginCommandBuffer(pick)");
        Owner.Transition(
            _commands,
            color.Image,
            ImageAspectFlags.ColorBit,
            color.Layout,
            ImageLayout.ColorAttachmentOptimal);
        Owner.Transition(
            _commands,
            depth.Image,
            ImageAspectFlags.DepthBit,
            depth.Layout,
            ImageLayout.DepthStencilAttachmentOptimal);

        ClearValue* clearValues = stackalloc ClearValue[2];
        clearValues[0] = new ClearValue
        {
            Color = new ClearColorValue
            {
                Float32_0 = clearColor.Red,
                Float32_1 = clearColor.Green,
                Float32_2 = clearColor.Blue,
                Float32_3 = clearColor.Alpha
            }
        };
        clearValues[1] = new ClearValue
        {
            DepthStencil = new ClearDepthStencilValue(clearDepth, 0)
        };
        var renderPassBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = pipeline.RenderPass,
            Framebuffer = _framebuffer,
            RenderArea = new Rect2D(
                new Offset2D(
                    checked((int)coordinate.X),
                    checked((int)coordinate.Y)),
                new Extent2D(1, 1)),
            ClearValueCount = 2,
            PClearValues = clearValues
        };
        _api.CmdBeginRenderPass(
            _commands,
            &renderPassBegin,
            SubpassContents.SecondaryCommandBuffers);
        if (primitiveCount > 0)
        {
            CommandBuffer secondary = _secondaryCommands;
            _api.CmdExecuteCommands(_commands, 1, &secondary);
        }
        _api.CmdEndRenderPass(_commands);

        Owner.Transition(
            _commands,
            color.Image,
            ImageAspectFlags.ColorBit,
            ImageLayout.ColorAttachmentOptimal,
            ImageLayout.TransferSrcOptimal);
        var copy = new BufferImageCopy
        {
            BufferOffset = _copyOffset,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1
            },
            ImageOffset = new Offset3D(
                checked((int)coordinate.X),
                checked((int)coordinate.Y),
                0),
            ImageExtent = new Extent3D(1, 1, 1)
        };
        _api.CmdCopyImageToBuffer(
            _commands,
            color.Image,
            ImageLayout.TransferSrcOptimal,
            _buffer,
            1,
            &copy);
        var hostBarrier = new BufferMemoryBarrier
        {
            SType = StructureType.BufferMemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.HostReadBit,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Buffer = _buffer,
            Offset = _copyOffset,
            Size = SilkPickTokenEncoding.ByteSize
        };
        _api.CmdPipelineBarrier(
            _commands,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.HostBit,
            DependencyFlags.None,
            0,
            null,
            1,
            &hostBarrier,
            0,
            null);
        Owner.Transition(
            _commands,
            color.Image,
            ImageAspectFlags.ColorBit,
            ImageLayout.TransferSrcOptimal,
            ImageLayout.ColorAttachmentOptimal);
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.EndCommandBuffer(_commands),
            "vkEndCommandBuffer(pick)");

        CommandBuffer submittedCommands = _commands;
        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &submittedCommands
        };
        Result result = _api.QueueSubmit(_queue, 1, &submitInfo, _fence);
        if (result == Result.ErrorDeviceLost)
        {
            Owner.NotifyPickDeviceLost();
        }
        VulkanSilkGraphicsDevice.ThrowIfFailed(result, "vkQueueSubmit(pick)");

        color.Layout = ImageLayout.ColorAttachmentOptimal;
        depth.Layout = ImageLayout.DepthStencilAttachmentOptimal;
        ulong serial = _nextSerial++;
        if (_nextSerial == 0)
        {
            _nextSerial = 1;
        }
        _activeSerial = serial;
        _state = PickSlotState.Submitted;
        return serial;
    }

    internal bool TryComplete(ulong serial)
    {
        ValidateActiveSerial(serial);
        if (_state is PickSlotState.Completed or PickSlotState.Read)
        {
            return true;
        }
        if (_state == PickSlotState.Failed)
        {
            throw new InvalidOperationException(
                "The Vulkan pick submission failed.");
        }
        if (Owner.SuppressPickCompletionsForTesting)
        {
            return false;
        }

        Owner.CountPickCompletionPoll();
        Result result = GetFenceResult(wait: false);
        if (result == Result.Success)
        {
            ObserveFenceResult(result);
            return true;
        }
        if (result == Result.NotReady)
        {
            return false;
        }
        ObserveFenceResult(result);
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            result,
            "vkGetFenceStatus(pick)");
        return false;
    }

    internal void Wait(ulong serial)
    {
        ValidateActiveSerial(serial);
        if (_state is PickSlotState.Completed or PickSlotState.Read)
        {
            return;
        }
        Result result = GetFenceResult(wait: true);
        ObserveFenceResult(result);
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            result,
            "vkWaitForFences(pick)");
    }

    internal void ReleaseSubmission(ulong serial)
    {
        ValidateActiveSerial(serial);
        try
        {
            if (_state == PickSlotState.Submitted)
            {
                CompleteForTeardown();
            }
        }
        finally
        {
            _activeSerial = 0;
            _state = PickSlotState.Idle;
        }
    }

    private static ulong AlignUp(ulong value, ulong alignment)
    {
        ulong normalized = Math.Max(1, alignment);
        ulong remainder = value % normalized;
        return remainder == 0
            ? value
            : checked(value + normalized - remainder);
    }

    private void CreateReadbackBuffer()
    {
        ulong size = checked(_copyOffset + _copyRangeSize);
        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = BufferUsageFlags.TransferDstBit,
            SharingMode = SharingMode.Exclusive
        };
        VkBuffer buffer = default;
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.CreateBuffer(_device, &bufferInfo, null, &buffer),
            "vkCreateBuffer(pick readback)");
        _buffer = buffer;
        _api.GetBufferMemoryRequirements(
            _device,
            _buffer,
            out MemoryRequirements requirements);
        uint memoryType = Owner.FindPickHostVisibleMemoryType(
            requirements.MemoryTypeBits,
            out _);
        var allocationInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = memoryType
        };
        DeviceMemory memory = default;
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.AllocateMemory(
                _device,
                &allocationInfo,
                null,
                &memory),
            "vkAllocateMemory(pick readback)");
        _memory = memory;
        _allocationSize = requirements.Size;
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.BindBufferMemory(_device, _buffer, _memory, 0),
            "vkBindBufferMemory(pick readback)");
        void* mapped = null;
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.MapMemory(
                _device,
                _memory,
                0,
                _allocationSize,
                0,
                &mapped),
            "vkMapMemory(pick readback)");
        _mapped = (nint)mapped;
    }

    private void CreateExecutionResources(uint queueFamily)
    {
        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = queueFamily
        };
        CommandPool commandPool = default;
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.CreateCommandPool(
                _device,
                &poolInfo,
                null,
                &commandPool),
            "vkCreateCommandPool(pick)");
        _commandPool = commandPool;
        var allocationInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1
        };
        CommandBuffer commands = default;
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.AllocateCommandBuffers(
                _device,
                &allocationInfo,
                &commands),
            "vkAllocateCommandBuffers(pick)");
        _commands = commands;
        allocationInfo.Level = CommandBufferLevel.Secondary;
        CommandBuffer secondaryCommands = default;
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.AllocateCommandBuffers(
                _device,
                &allocationInfo,
                &secondaryCommands),
            "vkAllocateCommandBuffers(pick secondary)");
        _secondaryCommands = secondaryCommands;
        var fenceInfo = new FenceCreateInfo
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.SignaledBit
        };
        Fence fence = default;
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.CreateFence(_device, &fenceInfo, null, &fence),
            "vkCreateFence(pick)");
        _fence = fence;
    }

    private void EnsureFramebuffer(
        VulkanSilkPickGraphicsPipeline pipeline,
        VulkanSilkGraphicsTexture color,
        VulkanSilkGraphicsTexture depth)
    {
        if (_framebuffer.Handle != 0 &&
            ReferenceEquals(_framebufferPipeline, pipeline) &&
            ReferenceEquals(_framebufferColor, color) &&
            ReferenceEquals(_framebufferDepth, depth))
        {
            return;
        }

        DestroyFramebuffer();
        IDisposable? pipelineLease = null;
        IDisposable? colorLease = null;
        IDisposable? depthLease = null;
        try
        {
            pipelineLease = pipeline.AcquireLease();
            colorLease = color.AcquireLease();
            depthLease = depth.AcquireLease();
            ImageView* attachments = stackalloc ImageView[2]
            {
                color.ImageView,
                depth.ImageView
            };
            var createInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = pipeline.RenderPass,
                AttachmentCount = 2,
                PAttachments = attachments,
                Width = color.Width,
                Height = color.Height,
                Layers = 1
            };
            Framebuffer framebuffer = default;
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                _api.CreateFramebuffer(
                    _device,
                    &createInfo,
                    null,
                    &framebuffer),
                "vkCreateFramebuffer(pick)");
            _framebuffer = framebuffer;
            _framebufferPipeline = pipeline;
            _framebufferColor = color;
            _framebufferDepth = depth;
            _framebufferPipelineLease = pipelineLease;
            _framebufferColorLease = colorLease;
            _framebufferDepthLease = depthLease;
            Owner.CountPickFramebufferCreation();
        }
        catch
        {
            depthLease?.Dispose();
            colorLease?.Dispose();
            pipelineLease?.Dispose();
            throw;
        }
    }

    private void EnsureDescriptorCapacity(
        VulkanSilkPickGraphicsPipeline pipeline,
        int drawCount)
    {
        if (drawCount == 0)
        {
            return;
        }
        if (ReferenceEquals(_descriptorPipeline, pipeline) &&
            drawCount <= _descriptorCapacity)
        {
            return;
        }

        DestroyDescriptorResources();
        int capacity = 1;
        while (capacity < drawCount)
        {
            capacity = checked(capacity * 2);
        }

        var poolSize = new DescriptorPoolSize(
            DescriptorType.UniformBuffer,
            checked((uint)(capacity * 2)));
        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = checked((uint)capacity),
            PoolSizeCount = 1,
            PPoolSizes = &poolSize
        };
        DescriptorPool descriptorPool = default;
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.CreateDescriptorPool(
                _device,
                &poolInfo,
                null,
                &descriptorPool),
            "vkCreateDescriptorPool(pick)");
        _descriptorPool = descriptorPool;
        Owner.CountPickDescriptorPoolCreation();
        try
        {
            _descriptorSets = new DescriptorSet[capacity];
            var layouts = new DescriptorSetLayout[capacity];
            Array.Fill(layouts, pipeline.DescriptorSetLayout);
            fixed (DescriptorSetLayout* layoutsPointer = layouts)
            fixed (DescriptorSet* setsPointer = _descriptorSets)
            {
                var allocationInfo = new DescriptorSetAllocateInfo
                {
                    SType = StructureType.DescriptorSetAllocateInfo,
                    DescriptorPool = _descriptorPool,
                    DescriptorSetCount = checked((uint)capacity),
                    PSetLayouts = layoutsPointer
                };
                VulkanSilkGraphicsDevice.ThrowIfFailed(
                    _api.AllocateDescriptorSets(
                        _device,
                        &allocationInfo,
                        setsPointer),
                    "vkAllocateDescriptorSets(pick)");
            }

            CreatePickUniformBuffer(capacity);
            _descriptorCapacity = capacity;
            _descriptorPipeline = pipeline;
        }
        catch
        {
            DestroyDescriptorResources();
            throw;
        }
    }

    private void CreatePickUniformBuffer(int capacity)
    {
        ulong size = checked(
            AlignUp(16, _uniformOffsetAlignment) *
            checked((ulong)capacity));
        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = BufferUsageFlags.UniformBufferBit,
            SharingMode = SharingMode.Exclusive
        };
        VkBuffer uniformBuffer = default;
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.CreateBuffer(
                _device,
                &bufferInfo,
                null,
                &uniformBuffer),
            "vkCreateBuffer(pick uniforms)");
        _pickUniformBuffer = uniformBuffer;
        _api.GetBufferMemoryRequirements(
            _device,
            _pickUniformBuffer,
            out MemoryRequirements requirements);
        uint memoryType = Owner.FindPickHostVisibleMemoryType(
            requirements.MemoryTypeBits,
            out _);
        var allocationInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = memoryType
        };
        DeviceMemory uniformMemory = default;
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.AllocateMemory(
                _device,
                &allocationInfo,
                null,
                &uniformMemory),
            "vkAllocateMemory(pick uniforms)");
        _pickUniformMemory = uniformMemory;
        _pickUniformAllocationSize = requirements.Size;
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.BindBufferMemory(
                _device,
                _pickUniformBuffer,
                _pickUniformMemory,
                0),
            "vkBindBufferMemory(pick uniforms)");
        void* mapped = null;
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.MapMemory(
                _device,
                _pickUniformMemory,
                0,
                _pickUniformAllocationSize,
                0,
                &mapped),
            "vkMapMemory(pick uniforms)");
        _pickUniformMapped = (nint)mapped;
        Owner.RegisterPickBufferMapping();
        _pickUniformDiagnosticsRegistered = true;
        Owner.CountPickUniformBufferCreation();
    }

    private static int CountPrimitives(
        IReadOnlyList<VulkanPickDrawCommand> draws)
    {
        int count = 0;
        foreach (VulkanPickDrawCommand draw in draws)
        {
            if (draw.IndexCount % 3 != 0)
            {
                throw new InvalidOperationException(
                    "A Vulkan pick draw must contain complete triangles.");
            }
            count = checked(count + (int)(draw.IndexCount / 3));
        }
        return count;
    }

    private void UpdateDescriptorsAndTokens(
        IReadOnlyList<VulkanPickDrawCommand> draws)
    {
        if (draws.Count == 0)
        {
            return;
        }
        int descriptorIndex = 0;
        ulong stride = AlignUp(16, _uniformOffsetAlignment);
        DescriptorBufferInfo* bufferInfos =
            stackalloc DescriptorBufferInfo[2];
        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[2];
        foreach (VulkanPickDrawCommand draw in draws)
        {
            uint drawPrimitiveCount = draw.IndexCount / 3;
            for (uint primitive = 0;
                 primitive < drawPrimitiveCount;
                 primitive++)
            {
                ulong tokenOffset = checked(
                    stride * (ulong)descriptorIndex);
                Span<byte> tokenBytes = new(
                    (byte*)_pickUniformMapped +
                        checked((int)tokenOffset),
                    16);
                tokenBytes.Clear();
                BinaryPrimitives.WriteUInt32LittleEndian(
                    tokenBytes,
                    checked(draw.BaseToken + primitive));
                bufferInfos[0] = new DescriptorBufferInfo(
                    draw.UniformBuffer.Buffer,
                    0,
                    SilkCheckedShaderAssets.SceneParameters.ByteSize);
                bufferInfos[1] = new DescriptorBufferInfo(
                    _pickUniformBuffer,
                    tokenOffset,
                    16);
                writes[0] = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = _descriptorSets![descriptorIndex],
                    DstBinding = 0,
                    DescriptorCount = 1,
                    DescriptorType = DescriptorType.UniformBuffer,
                    PBufferInfo = &bufferInfos[0]
                };
                writes[1] = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = _descriptorSets[descriptorIndex],
                    DstBinding = 1,
                    DescriptorCount = 1,
                    DescriptorType = DescriptorType.UniformBuffer,
                    PBufferInfo = &bufferInfos[1]
                };
                _api.UpdateDescriptorSets(
                    _device,
                    2,
                    writes,
                    0,
                    null);
                descriptorIndex++;
            }
        }

        var range = new MappedMemoryRange
        {
            SType = StructureType.MappedMemoryRange,
            Memory = _pickUniformMemory,
            Offset = 0,
            Size = Vk.WholeSize
        };
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.FlushMappedMemoryRanges(_device, 1, &range),
            "vkFlushMappedMemoryRanges(pick uniforms)");
    }

    private void EnsureSecondaryCommands(
        VulkanSilkPickGraphicsPipeline pipeline,
        VulkanSilkGraphicsTexture color,
        IReadOnlyList<VulkanPickDrawCommand> draws)
    {
        if (MatchesSecondaryCommands(pipeline, color, draws))
        {
            return;
        }

        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.ResetCommandBuffer(
                _secondaryCommands,
                CommandBufferResetFlags.None),
            "vkResetCommandBuffer(pick secondary)");
        var inheritance = new CommandBufferInheritanceInfo
        {
            SType = StructureType.CommandBufferInheritanceInfo,
            RenderPass = pipeline.RenderPass,
            Subpass = 0,
            Framebuffer = _framebuffer
        };
        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.RenderPassContinueBit,
            PInheritanceInfo = &inheritance
        };
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.BeginCommandBuffer(_secondaryCommands, &beginInfo),
            "vkBeginCommandBuffer(pick secondary)");
        _api.CmdBindPipeline(
            _secondaryCommands,
            PipelineBindPoint.Graphics,
            pipeline.Pipeline);

        int descriptorIndex = 0;
        foreach (VulkanPickDrawCommand draw in draws)
        {
            var viewport = new global::Silk.NET.Vulkan.Viewport(
                draw.Viewport.X,
                draw.Viewport.Y,
                draw.Viewport.Width,
                draw.Viewport.Height,
                draw.Viewport.MinDepth,
                draw.Viewport.MaxDepth);
            _api.CmdSetViewport(
                _secondaryCommands,
                0,
                1,
                &viewport);
            var scissor = new Rect2D(
                new Offset2D(draw.Scissor.X, draw.Scissor.Y),
                new Extent2D(
                    draw.Scissor.Width,
                    draw.Scissor.Height));
            _api.CmdSetScissor(
                _secondaryCommands,
                0,
                1,
                &scissor);
            VkBuffer vertexBuffer = draw.VertexBuffer.Buffer;
            ulong vertexOffset = 0;
            _api.CmdBindVertexBuffers(
                _secondaryCommands,
                0,
                1,
                &vertexBuffer,
                &vertexOffset);
            _api.CmdBindIndexBuffer(
                _secondaryCommands,
                draw.IndexBuffer.Buffer,
                0,
                IndexType.Uint32);
            uint primitiveCount = draw.IndexCount / 3;
            for (uint primitive = 0;
                 primitive < primitiveCount;
                 primitive++)
            {
                DescriptorSet descriptorSet =
                    _descriptorSets![descriptorIndex++];
                _api.CmdBindDescriptorSets(
                    _secondaryCommands,
                    PipelineBindPoint.Graphics,
                    pipeline.Layout,
                    0,
                    1,
                    &descriptorSet,
                    0,
                    null);
                _api.CmdDrawIndexed(
                    _secondaryCommands,
                    3,
                    1,
                    primitive * 3,
                    0,
                    0);
            }
        }
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.EndCommandBuffer(_secondaryCommands),
            "vkEndCommandBuffer(pick secondary)");
        CacheSecondaryCommands(pipeline, color, draws);
        Owner.CountPickSecondaryCommandRecording();
    }

    private bool MatchesSecondaryCommands(
        VulkanSilkPickGraphicsPipeline pipeline,
        VulkanSilkGraphicsTexture color,
        IReadOnlyList<VulkanPickDrawCommand> draws)
    {
        if (!ReferenceEquals(_secondaryPipeline, pipeline) ||
            _secondaryWidth != color.Width ||
            _secondaryHeight != color.Height ||
            _secondaryDrawCount != draws.Count ||
            _secondaryDraws is null)
        {
            return false;
        }
        for (int index = 0; index < draws.Count; index++)
        {
            VulkanPickDrawCommand draw = draws[index];
            VulkanPickDrawCacheEntry cached = _secondaryDraws[index];
            if (!ReferenceEquals(cached.VertexBuffer, draw.VertexBuffer) ||
                !ReferenceEquals(cached.IndexBuffer, draw.IndexBuffer) ||
                cached.IndexCount != draw.IndexCount ||
                cached.Viewport != draw.Viewport ||
                cached.Scissor != draw.Scissor)
            {
                return false;
            }
        }
        return true;
    }

    private void CacheSecondaryCommands(
        VulkanSilkPickGraphicsPipeline pipeline,
        VulkanSilkGraphicsTexture color,
        IReadOnlyList<VulkanPickDrawCommand> draws)
    {
        if (_secondaryDraws is null ||
            _secondaryDraws.Length < draws.Count)
        {
            _secondaryDraws =
                new VulkanPickDrawCacheEntry[Math.Max(1, draws.Count)];
        }
        for (int index = 0; index < draws.Count; index++)
        {
            VulkanPickDrawCommand draw = draws[index];
            _secondaryDraws[index] = new VulkanPickDrawCacheEntry(
                draw.VertexBuffer,
                draw.IndexBuffer,
                draw.Viewport,
                draw.Scissor,
                draw.IndexCount);
        }
        _secondaryPipeline = pipeline;
        _secondaryWidth = color.Width;
        _secondaryHeight = color.Height;
        _secondaryDrawCount = draws.Count;
    }

    private Result GetFenceResult(bool wait)
    {
        if (Owner.TryConsumePickFenceFailureForTesting(out Result failure))
        {
            return failure;
        }
        if (Owner.IsPickDeviceLost)
        {
            return Result.ErrorDeviceLost;
        }
        if (wait)
        {
            Owner.CountPickFenceWait();
            return _api.WaitForFences(
                _device,
                1,
                in _fence,
                true,
                ulong.MaxValue);
        }
        return _api.GetFenceStatus(_device, _fence);
    }

    private void ObserveFenceResult(Result result)
    {
        if (result == Result.Success)
        {
            _state = PickSlotState.Completed;
            return;
        }
        _state = PickSlotState.Failed;
        if (result == Result.ErrorDeviceLost)
        {
            Owner.NotifyPickDeviceLost();
        }
    }

    private void CompleteForTeardown()
    {
        Result result = GetFenceResult(wait: true);
        ObserveFenceResult(result);
    }

    private void ReleaseDependent()
    {
        if (_dependentReleased)
        {
            return;
        }
        _dependentReleased = true;
        Owner.ReleasePickReadbackDependent();
    }

    private void ValidateActiveSerial(ulong serial)
    {
        ThrowIfDisposed();
        if (serial == 0 ||
            serial != _activeSerial ||
            _state == PickSlotState.Idle)
        {
            throw new InvalidOperationException(
                "The Vulkan pick submission tag is no longer active.");
        }
    }

    private void DestroyFramebuffer()
    {
        InvalidateSecondaryCommands();
        if (_framebuffer.Handle != 0)
        {
            _api.DestroyFramebuffer(_device, _framebuffer, null);
            _framebuffer = default;
        }
        _framebufferDepthLease?.Dispose();
        _framebufferColorLease?.Dispose();
        _framebufferPipelineLease?.Dispose();
        _framebufferDepthLease = null;
        _framebufferColorLease = null;
        _framebufferPipelineLease = null;
        _framebufferPipeline = null;
        _framebufferColor = null;
        _framebufferDepth = null;
    }

    private void DestroyDescriptorResources()
    {
        InvalidateSecondaryCommands();
        if (_pickUniformMapped != 0)
        {
            _api.UnmapMemory(_device, _pickUniformMemory);
            _pickUniformMapped = 0;
        }
        if (_pickUniformBuffer.Handle != 0)
        {
            _api.DestroyBuffer(_device, _pickUniformBuffer, null);
            _pickUniformBuffer = default;
        }
        if (_pickUniformMemory.Handle != 0)
        {
            _api.FreeMemory(_device, _pickUniformMemory, null);
            _pickUniformMemory = default;
        }
        if (_pickUniformDiagnosticsRegistered)
        {
            _pickUniformDiagnosticsRegistered = false;
            Owner.ReleasePickBufferMapping();
        }
        if (_descriptorPool.Handle != 0)
        {
            _api.DestroyDescriptorPool(_device, _descriptorPool, null);
            _descriptorPool = default;
        }
        _pickUniformAllocationSize = 0;
        _descriptorSets = null;
        _descriptorPipeline = null;
        _descriptorCapacity = 0;
    }

    private void InvalidateSecondaryCommands()
    {
        _secondaryPipeline = null;
        _secondaryWidth = 0;
        _secondaryHeight = 0;
        _secondaryDrawCount = 0;
    }

    private void DestroyNativeResources()
    {
        try
        {
            DestroyDescriptorResources();
            DestroyFramebuffer();
            if (_fence.Handle != 0)
            {
                _api.DestroyFence(_device, _fence, null);
                _fence = default;
            }
            if (_commandPool.Handle != 0)
            {
                _api.DestroyCommandPool(_device, _commandPool, null);
                _commandPool = default;
                _commands = default;
                _secondaryCommands = default;
            }
            if (_mapped != 0)
            {
                _api.UnmapMemory(_device, _memory);
                _mapped = 0;
            }
            if (_buffer.Handle != 0)
            {
                _api.DestroyBuffer(_device, _buffer, null);
                _buffer = default;
            }
            if (_memory.Handle != 0)
            {
                _api.FreeMemory(_device, _memory, null);
                _memory = default;
            }
            _allocationSize = 0;
        }
        finally
        {
            if (_diagnosticsRegistered)
            {
                _diagnosticsRegistered = false;
                Owner.ReleasePickReadbackResources();
            }
        }
    }

    private enum PickSlotState
    {
        Idle,
        Submitted,
        Completed,
        Read,
        Failed,
        Disposed
    }
}

internal sealed class VulkanSilkPickSubmission(
    VulkanSilkPickReadbackBuffer readback,
    ulong serial,
    IDisposable[] leases)
    : ISilkGraphicsSubmission
{
    private VulkanSilkPickReadbackBuffer? _readback = readback;
    private IDisposable[]? _leases = leases;

    public bool IsCompleted
    {
        get
        {
            VulkanSilkPickReadbackBuffer active = _readback ??
                throw new ObjectDisposedException(
                    nameof(VulkanSilkPickSubmission));
            return active.TryComplete(serial);
        }
    }

    public void Wait()
    {
        VulkanSilkPickReadbackBuffer active = _readback ??
            throw new ObjectDisposedException(
                nameof(VulkanSilkPickSubmission));
        active.Wait(serial);
    }

    public void Dispose()
    {
        VulkanSilkPickReadbackBuffer? active =
            Interlocked.Exchange(ref _readback, null);
        if (active is null)
        {
            return;
        }
        try
        {
            active.ReleaseSubmission(serial);
        }
        finally
        {
            IDisposable[]? retained =
                Interlocked.Exchange(ref _leases, null);
            if (retained is not null)
            {
                for (int index = retained.Length - 1; index >= 0; index--)
                {
                    retained[index].Dispose();
                }
            }
        }
    }
}
