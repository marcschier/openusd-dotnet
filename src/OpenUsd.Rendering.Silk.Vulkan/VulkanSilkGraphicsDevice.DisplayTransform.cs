// Copyright (c) marcschier. Licensed under the MIT License.

using global::Silk.NET.Vulkan;

namespace OpenUsd.Rendering.Silk.Vulkan;

public sealed unsafe partial class VulkanSilkGraphicsDevice
    : ISilkDisplayTransformGraphicsDevice
{
    private long _displayTransformPipelineCreations;
    private long _displayTransformBindingCreations;
    private long _activeDisplayTransformPipelines;
    private long _activeDisplayTransformBindings;
    private long _displayTransformSetLayoutCreations;
    private long _displayTransformSetLayoutDestructions;

    /// <inheritdoc/>
    public ulong DisplayTransformDeviceGeneration =>
        SelectionOutlineDeviceGeneration;

    /// <inheritdoc/>
    public ISilkDisplayTransformGraphicsPipeline CreateDisplayTransformGraphicsPipeline(
        SilkDisplayTransformPipelineDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        descriptor.Validate();
        if (descriptor.VertexShader.Format != SilkShaderBinaryFormat.SpirV)
        {
            throw new ArgumentException(
                "Vulkan display-transform pipelines require checked SPIR-V shaders.",
                nameof(descriptor));
        }

        RegisterDependentObject();
        try
        {
            VulkanSilkDisplayTransformGraphicsPipeline pipeline =
                VulkanSilkDisplayTransformGraphicsPipeline.Create(
                    this,
                    _api,
                    _device,
                    descriptor,
                    DisplayTransformDeviceGeneration);
            Interlocked.Increment(ref _displayTransformPipelineCreations);
            Interlocked.Increment(ref _activeDisplayTransformPipelines);
            return pipeline;
        }
        catch
        {
            ReleaseDependentObject();
            throw;
        }
    }

    /// <inheritdoc/>
    public ISilkDisplayTransformBinding CreateDisplayTransformBinding(
        SilkDisplayTransformBindingDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        descriptor.Validate();
        if (descriptor.SceneColorTexture is not VulkanSilkGraphicsTexture sceneColor ||
            descriptor.LatticeTexture is not VulkanSilkGraphicsTexture lattice ||
            descriptor.Sampler is not VulkanSilkGraphicsSampler sampler ||
            descriptor.Parameters is not VulkanSilkGraphicsBuffer parameters ||
            !ReferenceEquals(sceneColor.Device, this) ||
            !ReferenceEquals(lattice.Device, this) ||
            !ReferenceEquals(sampler.Owner, this) ||
            !ReferenceEquals(parameters.Owner, this))
        {
            throw new ArgumentException(
                "Display-transform binding resources must belong to this Vulkan device.",
                nameof(descriptor));
        }

        sceneColor.ThrowIfDisposed();
        lattice.ThrowIfDisposed();
        sampler.ThrowIfDisposed();
        parameters.ThrowIfDisposed();

        RegisterDependentObject();
        try
        {
            VulkanSilkDisplayTransformBinding binding =
                VulkanSilkDisplayTransformBinding.Create(
                    this,
                    _api,
                    _device,
                    descriptor,
                    sceneColor,
                    lattice,
                    sampler,
                    parameters,
                    DisplayTransformDeviceGeneration);
            Interlocked.Increment(ref _displayTransformBindingCreations);
            Interlocked.Increment(ref _activeDisplayTransformBindings);
            return binding;
        }
        catch
        {
            ReleaseDependentObject();
            throw;
        }
    }

    internal VulkanDisplayTransformNativeStatistics
        DisplayTransformNativeStatisticsForTesting => new(
            Interlocked.Read(ref _displayTransformPipelineCreations),
            Interlocked.Read(ref _displayTransformBindingCreations),
            Interlocked.Read(ref _activeDisplayTransformPipelines),
            Interlocked.Read(ref _activeDisplayTransformBindings),
            DisplayTransformDeviceGeneration,
            Interlocked.Read(ref _displayTransformSetLayoutCreations),
            Interlocked.Read(ref _displayTransformSetLayoutDestructions));

    /// <summary>Counts one successful <c>vkCreateDescriptorSetLayout</c>.</summary>
    internal void RecordDisplayTransformSetLayoutCreated() =>
        Interlocked.Increment(ref _displayTransformSetLayoutCreations);

    /// <summary>Counts one issued <c>vkDestroyDescriptorSetLayout</c>.</summary>
    internal void RecordDisplayTransformSetLayoutDestroyed() =>
        Interlocked.Increment(ref _displayTransformSetLayoutDestructions);

    internal void ReleaseDisplayTransformPipeline()
    {
        Interlocked.Decrement(ref _activeDisplayTransformPipelines);
        ReleaseDependentObject();
    }

    internal void ReleaseDisplayTransformBinding()
    {
        Interlocked.Decrement(ref _activeDisplayTransformBindings);
        ReleaseDependentObject();
    }

    private void RecordDisplayTransformDraw(
        CommandBuffer commands,
        VulkanSilkDisplayTransformGraphicsPipeline pipeline,
        VulkanSilkDisplayTransformBinding binding,
        VulkanSilkGraphicsTexture color,
        SilkViewport viewport,
        SilkScissor scissor)
    {
        Framebuffer framebuffer = pipeline.GetFramebuffer(color);
        var beginInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = pipeline.RenderPass,
            Framebuffer = framebuffer,
            RenderArea = new Rect2D(
                new Offset2D(0, 0),
                new Extent2D(color.Width, color.Height))
        };
        _api.CmdBeginRenderPass(commands, &beginInfo, SubpassContents.Inline);
        _api.CmdBindPipeline(
            commands,
            PipelineBindPoint.Graphics,
            pipeline.Pipeline);
        var nativeViewport = new global::Silk.NET.Vulkan.Viewport(
            viewport.X,
            viewport.Y,
            viewport.Width,
            viewport.Height,
            viewport.MinDepth,
            viewport.MaxDepth);
        _api.CmdSetViewport(commands, 0, 1, &nativeViewport);
        var nativeScissor = new Rect2D(
            new Offset2D(scissor.X, scissor.Y),
            new Extent2D(scissor.Width, scissor.Height));
        _api.CmdSetScissor(commands, 0, 1, &nativeScissor);
        DescriptorSet descriptorSet = binding.DescriptorSet;
        _api.CmdBindDescriptorSets(
            commands,
            PipelineBindPoint.Graphics,
            pipeline.Layout,
            0,
            1,
            &descriptorSet,
            0,
            null);
        _api.CmdDraw(commands, 3, 1, 0, 0);
        _api.CmdEndRenderPass(commands);
    }
}

internal readonly record struct VulkanDisplayTransformNativeStatistics(
    long PipelineCreations,
    long BindingCreations,
    long ActivePipelines,
    long ActiveBindings,
    ulong DeviceGeneration,
    long SetLayoutCreations,
    long SetLayoutDestructions)
{
    /// <summary>
    /// Gets the number of <c>VkDescriptorSetLayout</c> handles this device created and
    /// has not destroyed.
    /// </summary>
    /// <remarks>
    /// This counts the native calls, not the managed wrappers. A binding wrapper that is
    /// collected, finalized, or disposed without reaching
    /// <c>vkDestroyDescriptorSetLayout</c> leaves this above zero, which is exactly the
    /// leak a wrapper-count assertion cannot see.
    /// </remarks>
    internal long LiveSetLayouts => SetLayoutCreations - SetLayoutDestructions;
}

internal sealed unsafe class VulkanSilkDisplayTransformGraphicsPipeline :
    SilkGraphicsResourceBase,
    ISilkDisplayTransformGraphicsPipeline
{
    private readonly Vk _api;
    private readonly Device _device;
    private ShaderModule _vertexShader;
    private ShaderModule _fragmentShader;
    private DescriptorSetLayout _descriptorSetLayout;
    private PipelineLayout _layout;
    private RenderPass _renderPass;
    private Pipeline _pipeline;
    private Framebuffer _framebuffer;
    private VulkanSilkGraphicsTexture? _framebufferColor;
    private IDisposable? _framebufferColorLease;

    private VulkanSilkDisplayTransformGraphicsPipeline(
        VulkanSilkGraphicsDevice owner,
        Vk api,
        Device device,
        SilkDisplayTransformPipelineDescriptor descriptor,
        ulong deviceGeneration)
    {
        Owner = owner;
        _api = api;
        _device = device;
        Descriptor = descriptor;
        DeviceGeneration = deviceGeneration;
    }

    internal VulkanSilkGraphicsDevice Owner { get; }

    internal ulong DeviceGeneration { get; }

    public SilkDisplayTransformPipelineDescriptor Descriptor { get; }

    internal DescriptorSetLayout DescriptorSetLayout => _descriptorSetLayout;

    internal PipelineLayout Layout => _layout;

    internal RenderPass RenderPass => _renderPass;

    internal Pipeline Pipeline => _pipeline;

    internal static VulkanSilkDisplayTransformGraphicsPipeline Create(
        VulkanSilkGraphicsDevice owner,
        Vk api,
        Device device,
        SilkDisplayTransformPipelineDescriptor descriptor,
        ulong deviceGeneration)
    {
        var result = new VulkanSilkDisplayTransformGraphicsPipeline(
            owner,
            api,
            device,
            descriptor,
            deviceGeneration);
        try
        {
            result.Initialize();
            return result;
        }
        catch
        {
            result.DestroyNative();
            throw;
        }
    }

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfDisposed() => ThrowIfResourceDisposed();

    internal Framebuffer GetFramebuffer(VulkanSilkGraphicsTexture color)
    {
        ThrowIfDisposed();
        color.ThrowIfDisposed();
        if (_framebuffer.Handle != 0 &&
            ReferenceEquals(_framebufferColor, color))
        {
            return _framebuffer;
        }

        DestroyFramebuffer();
        IDisposable? colorLease = null;
        try
        {
            colorLease = color.AcquireLease();
            ImageView attachment = color.ImageView;
            var framebufferInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = _renderPass,
                AttachmentCount = 1,
                PAttachments = &attachment,
                Width = color.Width,
                Height = color.Height,
                Layers = 1
            };
            Framebuffer framebuffer = default;
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                _api.CreateFramebuffer(
                    _device,
                    &framebufferInfo,
                    null,
                    &framebuffer),
                "vkCreateFramebuffer(display transform)");
            _framebuffer = framebuffer;
            _framebufferColor = color;
            _framebufferColorLease = colorLease;
            return _framebuffer;
        }
        catch
        {
            colorLease?.Dispose();
            throw;
        }
    }

    protected override void ReleaseNative()
    {
        DestroyNative();
        Owner.ReleaseDisplayTransformPipeline();
    }

    private void Initialize()
    {
        _vertexShader = CreateShaderModule(
            Descriptor.VertexShader,
            normalizeVertexId: true);
        _fragmentShader = CreateShaderModule(
            Descriptor.FragmentShader,
            normalizeVertexId: false);
        DescriptorSetLayoutBinding* bindings =
            stackalloc DescriptorSetLayoutBinding[4];
        bindings[0] = CreateBinding(
            0,
            DescriptorType.SampledImage,
            ShaderStageFlags.FragmentBit);
        bindings[1] = CreateBinding(
            1,
            DescriptorType.SampledImage,
            ShaderStageFlags.FragmentBit);
        bindings[2] = CreateBinding(
            2,
            DescriptorType.Sampler,
            ShaderStageFlags.FragmentBit);
        bindings[3] = CreateBinding(
            3,
            DescriptorType.UniformBuffer,
            ShaderStageFlags.FragmentBit);
        var setLayoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 4,
            PBindings = bindings
        };
        DescriptorSetLayout descriptorSetLayout = default;
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.CreateDescriptorSetLayout(
                _device,
                &setLayoutInfo,
                null,
                &descriptorSetLayout),
            "vkCreateDescriptorSetLayout(display transform)");
        _descriptorSetLayout = descriptorSetLayout;
        DescriptorSetLayout setLayout = _descriptorSetLayout;
        var layoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &setLayout
        };
        PipelineLayout pipelineLayout = default;
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.CreatePipelineLayout(_device, &layoutInfo, null, &pipelineLayout),
            "vkCreatePipelineLayout(display transform)");
        _layout = pipelineLayout;
        CreateRenderPass();
        CreatePipeline();
    }

    private static DescriptorSetLayoutBinding CreateBinding(
        uint binding,
        DescriptorType type,
        ShaderStageFlags stages) =>
        new()
        {
            Binding = binding,
            DescriptorType = type,
            DescriptorCount = 1,
            StageFlags = stages
        };

    private ShaderModule CreateShaderModule(
        SilkShaderModuleDescriptor descriptor,
        bool normalizeVertexId)
    {
        byte[] code = normalizeVertexId
            ? VulkanSilkSelectionMaskGraphicsPipeline.NormalizeVertexIdShader(
                descriptor.Code.Span)
            : descriptor.Code.ToArray();
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
                _api.CreateShaderModule(_device, &createInfo, null, &module),
                "vkCreateShaderModule(display transform)");
            return module;
        }
    }

    private void CreateRenderPass()
    {
        var attachment = new AttachmentDescription
        {
            Format = VulkanSilkGraphicsDevice.GetNativeFormat(Descriptor.ColorFormat),
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.DontCare,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.ColorAttachmentOptimal,
            FinalLayout = ImageLayout.ColorAttachmentOptimal
        };
        var colorReference = new AttachmentReference(
            0,
            ImageLayout.ColorAttachmentOptimal);
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorReference
        };
        var dependency = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.FragmentShaderBit |
                PipelineStageFlags.TransferBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = AccessFlags.ShaderReadBit |
                AccessFlags.TransferReadBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit
        };
        var createInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &attachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency
        };
        RenderPass renderPass = default;
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.CreateRenderPass(_device, &createInfo, null, &renderPass),
            "vkCreateRenderPass(display transform)");
        _renderPass = renderPass;
    }

    private void CreatePipeline()
    {
        byte[] entryPoint = "main\0"u8.ToArray();
        fixed (byte* entry = entryPoint)
        {
            PipelineShaderStageCreateInfo* stages =
                stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = _vertexShader,
                PName = entry
            };
            stages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = _fragmentShader,
                PName = entry
            };
            var vertexInput = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo
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
                DepthTestEnable = false,
                DepthWriteEnable = false
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
                Layout = _layout,
                RenderPass = _renderPass,
                Subpass = 0
            };
            Pipeline pipeline = default;
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                _api.CreateGraphicsPipelines(
                    _device,
                    default,
                    1,
                    &pipelineInfo,
                    null,
                    &pipeline),
                "vkCreateGraphicsPipelines(display transform)");
            _pipeline = pipeline;
        }
    }

    private void DestroyFramebuffer()
    {
        if (_framebuffer.Handle != 0)
        {
            _api.DestroyFramebuffer(_device, _framebuffer, null);
            _framebuffer = default;
        }

        _framebufferColor = null;
        Interlocked.Exchange(ref _framebufferColorLease, null)?.Dispose();
    }

    private void DestroyNative()
    {
        DestroyFramebuffer();
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
            _api.DestroyDescriptorSetLayout(_device, _descriptorSetLayout, null);
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
    }
}

internal sealed unsafe class VulkanSilkDisplayTransformBinding :
    SilkGraphicsResourceBase,
    ISilkDisplayTransformBinding
{
    private readonly Vk _api;
    private readonly Device _device;
    private IDisposable[]? _resourceLeases;
    private DescriptorPool _descriptorPool;
    private DescriptorSet _descriptorSet;
    private DescriptorSetLayout _bindingSetLayout;

    private VulkanSilkDisplayTransformBinding(
        VulkanSilkGraphicsDevice owner,
        Vk api,
        Device device,
        SilkDisplayTransformBindingDescriptor descriptor,
        VulkanSilkGraphicsTexture sceneColor,
        VulkanSilkGraphicsTexture lattice,
        VulkanSilkGraphicsSampler sampler,
        VulkanSilkGraphicsBuffer parameters,
        ulong deviceGeneration)
    {
        Owner = owner;
        _api = api;
        _device = device;
        Descriptor = descriptor;
        SceneColor = sceneColor;
        Lattice = lattice;
        Sampler = sampler;
        Parameters = parameters;
        DeviceGeneration = deviceGeneration;
    }

    internal VulkanSilkGraphicsDevice Owner { get; }

    internal VulkanSilkGraphicsTexture SceneColor { get; }

    internal VulkanSilkGraphicsTexture Lattice { get; }

    internal VulkanSilkGraphicsSampler Sampler { get; }

    internal VulkanSilkGraphicsBuffer Parameters { get; }

    internal ulong DeviceGeneration { get; }

    internal DescriptorSet DescriptorSet => _descriptorSet;

    public SilkDisplayTransformBindingDescriptor Descriptor { get; }

    internal static VulkanSilkDisplayTransformBinding Create(
        VulkanSilkGraphicsDevice owner,
        Vk api,
        Device device,
        SilkDisplayTransformBindingDescriptor descriptor,
        VulkanSilkGraphicsTexture sceneColor,
        VulkanSilkGraphicsTexture lattice,
        VulkanSilkGraphicsSampler sampler,
        VulkanSilkGraphicsBuffer parameters,
        ulong deviceGeneration)
    {
        var result = new VulkanSilkDisplayTransformBinding(
            owner,
            api,
            device,
            descriptor,
            sceneColor,
            lattice,
            sampler,
            parameters,
            deviceGeneration);
        try
        {
            result.Initialize();
            return result;
        }
        catch
        {
            result.DestroyNative();
            throw;
        }
    }

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfDisposed() => ThrowIfResourceDisposed();

    protected override void ReleaseNative()
    {
        DestroyNative();
        Owner.ReleaseDisplayTransformBinding();
    }

    private void Initialize()
    {
        SceneColor.ThrowIfDisposed();
        Lattice.ThrowIfDisposed();
        Sampler.ThrowIfDisposed();
        Parameters.ThrowIfDisposed();
        _resourceLeases =
        [
            SceneColor.AcquireLease(),
            Lattice.AcquireLease(),
            Sampler.AcquireLease(),
            Parameters.AcquireLease()
        ];
        DescriptorPoolSize* poolSizes = stackalloc DescriptorPoolSize[3];
        poolSizes[0] = new DescriptorPoolSize(DescriptorType.SampledImage, 2);
        poolSizes[1] = new DescriptorPoolSize(DescriptorType.Sampler, 1);
        poolSizes[2] = new DescriptorPoolSize(DescriptorType.UniformBuffer, 1);
        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = 1,
            PoolSizeCount = 3,
            PPoolSizes = poolSizes
        };
        DescriptorPool descriptorPool = default;
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.CreateDescriptorPool(
                _device,
                &poolInfo,
                null,
                &descriptorPool),
            "vkCreateDescriptorPool(display transform)");
        _descriptorPool = descriptorPool;

        // A temporary layout is used solely to allocate the descriptor set; it
        // is destroyed immediately after allocation because the set is
        // compatible with any identically-structured layout.
        DescriptorSetLayoutBinding* layoutBindings =
            stackalloc DescriptorSetLayoutBinding[4];
        layoutBindings[0] = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.SampledImage,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit
        };
        layoutBindings[1] = new DescriptorSetLayoutBinding
        {
            Binding = 1,
            DescriptorType = DescriptorType.SampledImage,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit
        };
        layoutBindings[2] = new DescriptorSetLayoutBinding
        {
            Binding = 2,
            DescriptorType = DescriptorType.Sampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit
        };
        layoutBindings[3] = new DescriptorSetLayoutBinding
        {
            Binding = 3,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit
        };
        var setLayoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 4,
            PBindings = layoutBindings
        };
        // The layout is retained for the binding's lifetime rather than destroyed right
        // after allocation. The specification permits destroying it early, but a driver
        // is permitted to keep referring to it from the set it allocated -- SwiftShader
        // does exactly that, and dereferenced freed memory inside vkUpdateDescriptorSets.
        // Outliving the set it describes costs one object and removes the hazard.
        DescriptorSetLayout setLayout = default;
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.CreateDescriptorSetLayout(
                _device,
                &setLayoutInfo,
                null,
                &setLayout),
            "vkCreateDescriptorSetLayout(display transform binding)");
        _bindingSetLayout = setLayout;
        Owner.RecordDisplayTransformSetLayoutCreated();
        var allocationInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &setLayout
        };
        DescriptorSet descriptorSet = default;
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.AllocateDescriptorSets(
                _device,
                &allocationInfo,
                &descriptorSet),
            "vkAllocateDescriptorSets(display transform)");
        _descriptorSet = descriptorSet;

        var sceneColorInfo = new DescriptorImageInfo(
            default,
            SceneColor.ImageView,
            ImageLayout.ShaderReadOnlyOptimal);
        var latticeInfo = new DescriptorImageInfo(
            default,
            Lattice.ImageView,
            ImageLayout.ShaderReadOnlyOptimal);
        var samplerInfo = new DescriptorImageInfo(
            Sampler.Sampler,
            default,
            ImageLayout.Undefined);
        var parameterInfo = new DescriptorBufferInfo(
            Parameters.Buffer,
            0,
            SilkDisplayTransformUniformWriter.ByteSize);
        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[4];
        writes[0] = CreateImageWrite(
            _descriptorSet,
            0,
            DescriptorType.SampledImage,
            &sceneColorInfo);
        writes[1] = CreateImageWrite(
            _descriptorSet,
            1,
            DescriptorType.SampledImage,
            &latticeInfo);
        writes[2] = CreateImageWrite(
            _descriptorSet,
            2,
            DescriptorType.Sampler,
            &samplerInfo);
        writes[3] = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _descriptorSet,
            DstBinding = 3,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.UniformBuffer,
            PBufferInfo = &parameterInfo
        };
        _api.UpdateDescriptorSets(_device, 4, writes, 0, null);
    }

    private static WriteDescriptorSet CreateImageWrite(
        DescriptorSet set,
        uint binding,
        DescriptorType type,
        DescriptorImageInfo* imageInfo) =>
        new()
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = binding,
            DescriptorCount = 1,
            DescriptorType = type,
            PImageInfo = imageInfo
        };

    private void DestroyNative()
    {
        // Order matters and is not interchangeable. The pool owns the set that refers to
        // the layout, and SwiftShader really does dereference the layout from the set, so
        // the layout has to outlive the pool. It also has to actually be destroyed --
        // including on a partially initialized binding, where the layout exists but the
        // allocation or the update threw -- or every rebuild of the pass leaks one
        // VkDescriptorSetLayout for the lifetime of the device.
        if (_descriptorPool.Handle != 0)
        {
            _api.DestroyDescriptorPool(_device, _descriptorPool, null);
            _descriptorPool = default;
            _descriptorSet = default;
        }

        if (_bindingSetLayout.Handle != 0)
        {
            _api.DestroyDescriptorSetLayout(_device, _bindingSetLayout, null);
            _bindingSetLayout = default;
            Owner.RecordDisplayTransformSetLayoutDestroyed();
        }

        IDisposable[]? leases = Interlocked.Exchange(ref _resourceLeases, null);
        if (leases is not null)
        {
            for (int index = leases.Length - 1; index >= 0; index--)
            {
                leases[index].Dispose();
            }
        }
    }
}

internal sealed partial class VulkanSilkGraphicsCommandList
    : ISilkDisplayTransformGraphicsCommandList
{
    private VulkanSilkDisplayTransformGraphicsPipeline? _displayTransformPipeline;
    private VulkanSilkDisplayTransformBinding? _displayTransformBinding;
    private bool _displayTransformRendering;

    public void BeginDisplayTransformRendering(
        SilkDisplayTransformRenderingDescriptor descriptor)
    {
        ThrowIfUnavailable();
        if (_rendering)
        {
            throw new InvalidOperationException("A rendering scope is already active.");
        }

        descriptor.Validate();
        VulkanSilkGraphicsTexture color =
            ValidateTexture(descriptor.ColorAttachment);
        VulkanSilkGraphicsTexture sceneColor =
            ValidateTexture(descriptor.SceneColorTexture);
        VulkanSilkGraphicsTexture lattice =
            ValidateTexture(descriptor.LatticeTexture);
        _colorAttachment = color;
        _depthAttachment = null;
        _displayTransformPipeline = null;
        _displayTransformBinding = null;
        _displayTransformRendering = true;
        _rendering = true;
        _commands.Add(VulkanGraphicsCommand.BeginDisplayTransform(
            color,
            sceneColor,
            lattice));
    }

    public void SetDisplayTransformGraphicsPipeline(
        ISilkDisplayTransformGraphicsPipeline pipeline)
    {
        ThrowIfRendering();
        if (!_displayTransformRendering)
        {
            throw new InvalidOperationException(
                "A Vulkan display-transform rendering scope is not active.");
        }

        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not VulkanSilkDisplayTransformGraphicsPipeline vulkanPipeline ||
            !ReferenceEquals(vulkanPipeline.Owner, Device))
        {
            throw new ArgumentException(
                "The display-transform pipeline was not created by this Vulkan device.",
                nameof(pipeline));
        }

        vulkanPipeline.ThrowIfDisposed();
        if (vulkanPipeline.DeviceGeneration !=
            Device.DisplayTransformDeviceGeneration)
        {
            throw new InvalidOperationException(
                "The Vulkan display-transform pipeline belongs to a stale device generation.");
        }

        if (_colorAttachment?.Format != vulkanPipeline.Descriptor.ColorFormat)
        {
            throw new ArgumentException(
                "The display-transform pipeline format does not match the color target.",
                nameof(pipeline));
        }

        _displayTransformPipeline = vulkanPipeline;
        _commands.Add(VulkanGraphicsCommand.SetDisplayTransformPipeline(
            vulkanPipeline));
    }

    public void SetDisplayTransformBinding(ISilkDisplayTransformBinding binding)
    {
        ThrowIfRendering();
        if (!_displayTransformRendering ||
            _displayTransformPipeline is null)
        {
            throw new InvalidOperationException(
                "Set the Vulkan display-transform pipeline before its binding.");
        }

        ArgumentNullException.ThrowIfNull(binding);
        if (binding is not VulkanSilkDisplayTransformBinding vulkanBinding ||
            !ReferenceEquals(vulkanBinding.Owner, Device))
        {
            throw new ArgumentException(
                "The display-transform binding was not created by this Vulkan device.",
                nameof(binding));
        }

        vulkanBinding.ThrowIfDisposed();
        if (vulkanBinding.DeviceGeneration !=
            Device.DisplayTransformDeviceGeneration)
        {
            throw new InvalidOperationException(
                "The Vulkan display-transform binding belongs to a stale device generation.");
        }

        _displayTransformBinding = vulkanBinding;
        _commands.Add(VulkanGraphicsCommand.SetDisplayTransformBinding(
            vulkanBinding));
    }

    public void DrawDisplayTransformFullscreenTriangle()
    {
        ThrowIfRendering();
        if (!_displayTransformRendering ||
            _displayTransformPipeline is null ||
            _displayTransformBinding is null ||
            _viewport is null ||
            _scissor is null)
        {
            throw new InvalidOperationException(
                "The Vulkan fullscreen display transform requires pipeline, binding, " +
                "viewport, and scissor.");
        }

        _commands.Add(VulkanGraphicsCommand.DrawDisplayTransformFullscreenTriangle());
    }

    private void DisposeDisplayTransformState()
    {
        _displayTransformPipeline = null;
        _displayTransformBinding = null;
        _displayTransformRendering = false;
    }
}
