// Copyright (c) marcschier. Licensed under the MIT License.

using global::Silk.NET.Vulkan;

namespace OpenUsd.Rendering.Silk.Vulkan;

public sealed unsafe partial class VulkanSilkGraphicsDevice
{
    /// <inheritdoc/>
    public ISilkGraphicsShaderModule CreateShaderModule(
        SilkShaderModuleDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        descriptor.Validate();
        if (descriptor.Format != SilkShaderBinaryFormat.SpirV)
        {
            throw new ArgumentException("Vulkan shader modules require SPIR-V.", nameof(descriptor));
        }
        byte[] code = descriptor.Code.ToArray();
        ShaderModule module = default;
        RegisterDependentObject();
        bool success = false;
        try
        {
            fixed (byte* codePointer = code)
            {
                var createInfo = new ShaderModuleCreateInfo
                {
                    SType = StructureType.ShaderModuleCreateInfo,
                    CodeSize = checked((nuint)code.Length),
                    PCode = (uint*)codePointer
                };
                ThrowIfFailed(
                    _api.CreateShaderModule(_device, &createInfo, null, &module),
                    "vkCreateShaderModule");
            }
            success = true;
            return new VulkanSilkGraphicsShaderModule(
                this,
                _api,
                _device,
                module,
                descriptor with { Code = code });
        }
        finally
        {
            if (!success)
            {
                if (module.Handle != 0)
                {
                    _api.DestroyShaderModule(_device, module, null);
                }
                ReleaseDependentObject();
            }
        }
    }

    /// <inheritdoc/>
    public ISilkGraphicsBindingLayout CreateBindingLayout(
        SilkBindingLayoutDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        descriptor.Validate();
        DescriptorSetLayout layout = default;
        RegisterDependentObject();
        bool success = false;
        try
        {
            IReadOnlyList<SilkBindingSlot> slots = descriptor.MaterialSlots ?? [];
            // Set 0 binding 0 is always SceneParameters; material slots follow it in
            // the same set, because a second set would need a second layout object
            // and the pipeline contract binds exactly one.
            int count = 1 + slots.Count;
            DescriptorSetLayoutBinding* bindings =
                stackalloc DescriptorSetLayoutBinding[count];
            DescriptorBindingFlags* bindingFlags =
                stackalloc DescriptorBindingFlags[count];
            for (int index = 0; index < count; index++)
            {
                bindingFlags[index] = 0;
            }
            bindings[0] = new DescriptorSetLayoutBinding
            {
                Binding = descriptor.Binding,
                DescriptorType = DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit
            };
            for (int index = 0; index < slots.Count; index++)
            {
                SilkBindingSlot slot = slots[index];
                if (slot.Set != 0)
                {
                    throw new ArgumentException(
                        "The Vulkan backend binds one descriptor set, so material slots must use set 0.",
                        nameof(descriptor));
                }
                bindings[index + 1] = new DescriptorSetLayoutBinding
                {
                    Binding = slot.Binding,
                    DescriptorType = slot.Kind switch
                    {
                        SilkBindingKind.UniformBuffer => DescriptorType.UniformBuffer,
                        SilkBindingKind.SampledTexture => DescriptorType.SampledImage,
                        SilkBindingKind.StorageBuffer => DescriptorType.StorageBuffer,
                        _ => DescriptorType.Sampler
                    },
                    DescriptorCount = 1,
                    StageFlags = ToVulkanStages(slot.Visibility)
                };
                if (Capabilities.SupportsDescriptorIndexedTextureTables)
                {
                    bindingFlags[index + 1] =
                        DescriptorBindingFlags.PartiallyBoundBit;
                }
            }
            var bindingFlagsInfo = new DescriptorSetLayoutBindingFlagsCreateInfo
            {
                SType = StructureType.DescriptorSetLayoutBindingFlagsCreateInfo,
                BindingCount = (uint)count,
                PBindingFlags = bindingFlags
            };
            var createInfo = new DescriptorSetLayoutCreateInfo
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = (uint)count,
                PBindings = bindings,
                PNext = Capabilities.SupportsDescriptorIndexedTextureTables
                    ? &bindingFlagsInfo
                    : null
            };
            ThrowIfFailed(
                _api.CreateDescriptorSetLayout(_device, &createInfo, null, &layout),
                "vkCreateDescriptorSetLayout");
            success = true;
            return new VulkanSilkGraphicsBindingLayout(
                this,
                _api,
                _device,
                layout,
                descriptor);
        }
        finally
        {
            if (!success)
            {
                if (layout.Handle != 0)
                {
                    _api.DestroyDescriptorSetLayout(_device, layout, null);
                }
                ReleaseDependentObject();
            }
        }
    }

    private static ShaderStageFlags ToVulkanStages(SilkShaderStageVisibility visibility)
    {
        ShaderStageFlags flags = 0;
        if (visibility.HasFlag(SilkShaderStageVisibility.Vertex))
        {
            flags |= ShaderStageFlags.VertexBit;
        }
        if (visibility.HasFlag(SilkShaderStageVisibility.Fragment))
        {
            flags |= ShaderStageFlags.FragmentBit;
        }
        if (visibility.HasFlag(SilkShaderStageVisibility.Compute))
        {
            flags |= ShaderStageFlags.ComputeBit;
        }
        return flags;
    }

    /// <inheritdoc/>
    public ISilkGraphicsShaderProgram CreateShaderProgram(
        SilkShaderProgramDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (descriptor.VertexShader is not VulkanSilkGraphicsShaderModule vertex ||
            descriptor.FragmentShader is not VulkanSilkGraphicsShaderModule fragment ||
            descriptor.BindingLayout is not VulkanSilkGraphicsBindingLayout layout ||
            !ReferenceEquals(vertex.Owner, this) ||
            !ReferenceEquals(fragment.Owner, this) ||
            !ReferenceEquals(layout.Owner, this))
        {
            throw new ArgumentException(
                "Shader program resources must belong to this Vulkan device.",
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
        IDisposable[] leases =
        [
            vertex.AcquireLease(),
            fragment.AcquireLease(),
            layout.AcquireLease()
        ];
        RegisterDependentObject();
        return new VulkanSilkGraphicsShaderProgram(
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
        if (descriptor.Program is not VulkanSilkGraphicsShaderProgram program ||
            !ReferenceEquals(program.Owner, this))
        {
            throw new ArgumentException(
                "The shader program was not created by this Vulkan device.",
                nameof(descriptor));
        }
        program.ThrowIfDisposed();

        PipelineLayout pipelineLayout = default;
        RenderPass renderPass = default;
        Pipeline pipeline = default;
        IDisposable? programLease = null;
        RegisterDependentObject();
        bool success = false;
        try
        {
            DescriptorSetLayout setLayout = program.Layout.Layout;
            var layoutInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1,
                PSetLayouts = &setLayout
            };
            ThrowIfFailed(
                _api.CreatePipelineLayout(
                    _device,
                    &layoutInfo,
                    null,
                    &pipelineLayout),
                "vkCreatePipelineLayout");
            renderPass = CreateTriangleRenderPass();
            pipeline = CreateTrianglePipeline(descriptor, program, pipelineLayout, renderPass);
            programLease = program.AcquireLease();
            success = true;
            return new VulkanSilkGraphicsPipeline(
                this,
                _api,
                _device,
                descriptor,
                pipelineLayout,
                renderPass,
                pipeline,
                program.Layout.Layout,
                programLease);
        }
        finally
        {
            if (!success)
            {
                programLease?.Dispose();
                if (pipeline.Handle != 0)
                {
                    _api.DestroyPipeline(_device, pipeline, null);
                }
                if (renderPass.Handle != 0)
                {
                    _api.DestroyRenderPass(_device, renderPass, null);
                }
                if (pipelineLayout.Handle != 0)
                {
                    _api.DestroyPipelineLayout(_device, pipelineLayout, null);
                }
                ReleaseDependentObject();
            }
        }
    }

    private RenderPass CreateTriangleRenderPass()
    {
        AttachmentDescription* attachments = stackalloc AttachmentDescription[2];
        attachments[0] = new AttachmentDescription
        {
            Format = Format.R8G8B8A8Unorm,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Load,
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
            LoadOp = AttachmentLoadOp.Load,
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
            SrcStageMask = PipelineStageFlags.TransferBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit |
                PipelineStageFlags.EarlyFragmentTestsBit,
            SrcAccessMask = AccessFlags.TransferWriteBit |
                AccessFlags.TransferReadBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit |
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
        ThrowIfFailed(
            _api.CreateRenderPass(_device, &createInfo, null, &renderPass),
            "vkCreateRenderPass");
        return renderPass;
    }

    private Pipeline CreateTrianglePipeline(
        SilkGraphicsPipelineDescriptor descriptor,
        VulkanSilkGraphicsShaderProgram program,
        PipelineLayout layout,
        RenderPass renderPass)
    {
        byte[] vertexEntry = System.Text.Encoding.UTF8.GetBytes(
            program.Vertex.Descriptor.EntryPoint + "\0");
        byte[] fragmentEntry = System.Text.Encoding.UTF8.GetBytes(
            program.Fragment.Descriptor.EntryPoint + "\0");
        fixed (byte* vertexEntryPointer = vertexEntry)
        fixed (byte* fragmentEntryPointer = fragmentEntry)
        {
            PipelineShaderStageCreateInfo* stages =
                stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = program.Vertex.Module,
                PName = vertexEntryPointer
            };
            stages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = program.Fragment.Module,
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
                CullMode = ToVulkanCullMode(descriptor.CullMode),
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
                ColorWriteMask = ColorComponentFlags.RBit |
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
            ThrowIfFailed(
                _api.CreateGraphicsPipelines(
                    _device,
                    default,
                    1,
                    &pipelineInfo,
                    null,
                    &pipeline),
                "vkCreateGraphicsPipelines");
            return pipeline;
        }
    }

    private static CullModeFlags ToVulkanCullMode(SilkCullMode cullMode) =>
        cullMode switch
        {
            SilkCullMode.None => CullModeFlags.None,
            SilkCullMode.Back => CullModeFlags.BackBit,
            _ => throw new ArgumentOutOfRangeException(nameof(cullMode))
        };
}

internal sealed class VulkanSilkGraphicsShaderModule(
    VulkanSilkGraphicsDevice owner,
    Vk api,
    Device device,
    ShaderModule module,
    SilkShaderModuleDescriptor descriptor)
    : SilkGraphicsResourceBase, ISilkGraphicsShaderModule
{
    private readonly Vk _api = api;
    private readonly Device _device = device;
    private ShaderModule _module = module;

    internal VulkanSilkGraphicsDevice Owner { get; } = owner;

    internal ShaderModule Module => _module;

    public SilkShaderModuleDescriptor Descriptor { get; } = descriptor;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfDisposed() => ThrowIfResourceDisposed();

    protected override unsafe void ReleaseNative()
    {
        _api.DestroyShaderModule(_device, _module, null);
        _module = default;
        Owner.ReleaseDependentObject();
    }
}

internal sealed class VulkanSilkGraphicsBindingLayout(
    VulkanSilkGraphicsDevice owner,
    Vk api,
    Device device,
    DescriptorSetLayout layout,
    SilkBindingLayoutDescriptor descriptor)
    : SilkGraphicsResourceBase, ISilkGraphicsBindingLayout
{
    private readonly Vk _api = api;
    private readonly Device _device = device;
    private DescriptorSetLayout _layout = layout;

    internal VulkanSilkGraphicsDevice Owner { get; } = owner;

    internal DescriptorSetLayout Layout => _layout;

    public SilkBindingLayoutDescriptor Descriptor { get; } = descriptor;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfDisposed() => ThrowIfResourceDisposed();

    protected override unsafe void ReleaseNative()
    {
        _api.DestroyDescriptorSetLayout(_device, _layout, null);
        _layout = default;
        Owner.ReleaseDependentObject();
    }
}

internal sealed class VulkanSilkGraphicsShaderProgram(
    VulkanSilkGraphicsDevice owner,
    VulkanSilkGraphicsShaderModule vertex,
    VulkanSilkGraphicsShaderModule fragment,
    VulkanSilkGraphicsBindingLayout layout,
    IDisposable[] resourceLeases)
    : SilkGraphicsResourceBase, ISilkGraphicsShaderProgram
{
    private IDisposable[]? _resourceLeases = resourceLeases;

    internal VulkanSilkGraphicsDevice Owner { get; } = owner;

    internal VulkanSilkGraphicsShaderModule Vertex { get; } = vertex;

    internal VulkanSilkGraphicsShaderModule Fragment { get; } = fragment;

    internal VulkanSilkGraphicsBindingLayout Layout { get; } = layout;

    public ISilkGraphicsBindingLayout BindingLayout => Layout;

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
        Owner.ReleaseDependentObject();
    }
}

internal sealed class VulkanSilkGraphicsPipeline(
    VulkanSilkGraphicsDevice owner,
    Vk api,
    Device device,
    SilkGraphicsPipelineDescriptor descriptor,
    PipelineLayout layout,
    RenderPass renderPass,
    Pipeline pipeline,
    DescriptorSetLayout descriptorSetLayout,
    IDisposable programLease)
    : SilkGraphicsResourceBase, ISilkGraphicsPipeline
{
    private readonly Vk _api = api;
    private readonly Device _device = device;
    private PipelineLayout _layout = layout;
    private RenderPass _renderPass = renderPass;
    private Pipeline _pipeline = pipeline;
    private IDisposable? _programLease = programLease;

    internal VulkanSilkGraphicsDevice Owner { get; } = owner;

    public SilkGraphicsPipelineDescriptor Descriptor { get; } = descriptor;

    internal PipelineLayout Layout => _layout;

    internal RenderPass RenderPass => _renderPass;

    internal Pipeline Pipeline => _pipeline;

    internal DescriptorSetLayout DescriptorSetLayout { get; } = descriptorSetLayout;

    /// <summary>
    /// Gets the binding layout this pipeline was created from, so a submission can
    /// size its descriptor pool for the slots the layout actually declares.
    /// </summary>
    internal SilkBindingLayoutDescriptor BindingLayout { get; } =
        descriptor.Program.BindingLayout.Descriptor;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfDisposed() => ThrowIfResourceDisposed();

    protected override unsafe void ReleaseNative()
    {
        _api.DestroyPipeline(_device, _pipeline, null);
        _api.DestroyRenderPass(_device, _renderPass, null);
        _api.DestroyPipelineLayout(_device, _layout, null);
        _pipeline = default;
        _renderPass = default;
        _layout = default;
        Interlocked.Exchange(ref _programLease, null)?.Dispose();
        Owner.ReleaseDependentObject();
    }
}
