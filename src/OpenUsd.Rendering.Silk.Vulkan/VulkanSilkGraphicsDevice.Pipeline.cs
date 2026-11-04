// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
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
        byte[] code = ShouldPatchSlangDrawIndexLowering()
            ? PatchSlangDrawIndexLowering(descriptor.Code.Span)
            : descriptor.Code.ToArray();
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

    internal static byte[] PatchSlangDrawIndexLowering(ReadOnlySpan<byte> source)
    {
        byte[] code = PatchSlangIndexLowering(source, 42, 4424, false, out var vertex);
        code = PatchSlangIndexLowering(code, 43, 4425, true, out var instance);
        if (vertex.Variable != 0 && instance.Variable != 0)
        {
            RemoveUnusedDrawParameters(
                ref code, vertex.Variable, vertex.LoadedValue, 4424, instance.Variable, 4425);
            RemoveUnusedDrawParameters(ref code, instance.Variable, instance.LoadedValue, 4425);
        }
        return code;
    }

    internal static byte[] PatchSlangInstanceIndexLowering(ReadOnlySpan<byte> source) =>
        PatchSlangIndexLowering(source, 43, 4425, true, out _);

    private static byte[] PatchSlangIndexLowering(
        ReadOnlySpan<byte> source,
        uint indexBuiltin,
        uint baseBuiltin,
        bool requireExpectedPattern,
        out PatchedDrawParameter patched)
    {
        patched = default;
        if (source.Length < 20 || source.Length % sizeof(uint) != 0)
        {
            throw new InvalidDataException("The SPIR-V module is truncated.");
        }

        byte[] code = source.ToArray();
        const uint magic = 0x07230203;
        const ushort opDecorate = 71;
        const ushort opLoad = 61;
        const ushort opISub = 130;
        const ushort opCopyObject = 83;
        const uint decorationBuiltIn = 11;
        // Slang subtracts the corresponding draw base from both index builtins.
        // This backend always draws with zero firstInstance and vertex offset;
        // SwiftShader cannot reliably execute the DrawParameters capability.
        if (readWord(source, 0) != magic)
        {
            throw new InvalidDataException("The SPIR-V module has an invalid magic number.");
        }

        uint indexVariable = 0;
        uint baseVariable = 0;
        bool ambiguousBuiltin = false;
        var loadedVariablesByResult = new Dictionary<uint, uint>();
        var patchOffsets = new List<int>();
        int wordOffset = 5;
        int wordCount = source.Length / sizeof(uint);
        while (wordOffset < wordCount)
        {
            uint firstWord = readWord(source, wordOffset);
            ushort instructionWords = (ushort)(firstWord >> 16);
            ushort opcode = (ushort)firstWord;
            if (instructionWords == 0 || wordOffset + instructionWords > wordCount)
            {
                throw new InvalidDataException("The SPIR-V module contains a truncated instruction.");
            }

            if (opcode == opDecorate && instructionWords == 4 &&
                readWord(source, wordOffset + 2) == decorationBuiltIn)
            {
                uint target = readWord(source, wordOffset + 1);
                uint builtin = readWord(source, wordOffset + 3);
                if (builtin == indexBuiltin)
                {
                    assignUniqueBuiltin(ref indexVariable, target, "index");
                }
                else if (builtin == baseBuiltin)
                {
                    assignUniqueBuiltin(ref baseVariable, target, "base");
                }
            }
            else if (opcode == opLoad && instructionWords >= 4)
            {
                loadedVariablesByResult[readWord(source, wordOffset + 2)] =
                    readWord(source, wordOffset + 3);
            }
            else if (opcode == opISub && instructionWords == 5)
            {
                uint leftOperand = readWord(source, wordOffset + 3);
                uint rightOperand = readWord(source, wordOffset + 4);
                if (loadedVariablesByResult.TryGetValue(leftOperand, out uint leftVariable) &&
                    loadedVariablesByResult.TryGetValue(rightOperand, out uint rightVariable) &&
                    leftVariable == indexVariable &&
                    rightVariable == baseVariable)
                {
                    patchOffsets.Add(wordOffset);
                }
            }

            wordOffset += instructionWords;
        }
        if (baseVariable == 0 || ambiguousBuiltin)
        {
            return code;
        }
        if (indexVariable == 0 || patchOffsets.Count != 1)
        {
            if (requireExpectedPattern)
            {
                throw new InvalidDataException(
                    "The SPIR-V module references BaseInstance but does not match " +
                    "Slang's expected InstanceIndex - BaseInstance lowering.");
            }
            return code;
        }
        int patchOffset = patchOffsets[0];
        uint indexLoad = readWord(code, patchOffset + 3);
        uint baseLoad = readWord(code, patchOffset + 4);
        writeWord(code, patchOffset, ((uint)4 << 16) | opCopyObject);
        writeWord(code, patchOffset + 3, indexLoad);
        writeWord(code, patchOffset + 4, 1u << 16);
        patched = new PatchedDrawParameter(baseVariable, baseLoad);
        RemoveUnusedDrawParameters(ref code, baseVariable, baseLoad, baseBuiltin);
        return code;

        void assignUniqueBuiltin(ref uint current, uint target, string name)
        {
            if (current != 0 && current != target)
            {
                if (requireExpectedPattern)
                {
                    throw new InvalidDataException(
                        $"The SPIR-V module decorates multiple variables as the {name} builtin.");
                }
                ambiguousBuiltin = true;
                return;
            }
            current = target;
        }

        static uint readWord(ReadOnlySpan<byte> bytes, int offset) =>
            BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.Slice(offset * sizeof(uint), sizeof(uint)));

        static void writeWord(Span<byte> bytes, int offset, uint value) =>
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.Slice(offset * sizeof(uint), sizeof(uint)),
                value);
    }

    private readonly record struct PatchedDrawParameter(uint Variable, uint LoadedValue);

    private bool ShouldPatchSlangDrawIndexLowering() =>
        Capabilities.DeviceName.Contains("SwiftShader", StringComparison.OrdinalIgnoreCase);

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
                StageFlags = ToVulkanStages(descriptor.Visibility)
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
            renderPass = CreateTriangleRenderPass(
                descriptor.ColorFormat,
                descriptor.DepthOnly);
            pipeline = CreateTrianglePipeline(
                descriptor,
                program,
                pipelineLayout,
                renderPass,
                descriptor.TopologyKind);
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

    private RenderPass CreateTriangleRenderPass(SilkTextureFormat colorFormat) =>
        CreateTriangleRenderPass(colorFormat, depthOnly: false);

    /// <summary>
    /// Creates the render pass a mesh or shadow pipeline is compatible with.
    /// </summary>
    /// <remarks>
    /// A depth-only pass declares one attachment and no colour reference, which is
    /// what a shadow map is rendered into. Everything else -- load and store ops,
    /// the depth layout, and the external dependency -- is identical, so the two
    /// passes cannot drift apart.
    /// </remarks>
    private RenderPass CreateTriangleRenderPass(
        SilkTextureFormat colorFormat,
        bool depthOnly)
    {
        AttachmentDescription* attachments = stackalloc AttachmentDescription[2];
        attachments[0] = depthOnly
            ? default
            : new AttachmentDescription
            {
                Format = GetNativeFormat(colorFormat),
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Load,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.ColorAttachmentOptimal,
                FinalLayout = ImageLayout.ColorAttachmentOptimal
            };
        var depthAttachment = new AttachmentDescription
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
        if (depthOnly)
        {
            attachments[0] = depthAttachment;
        }
        else
        {
            attachments[1] = depthAttachment;
        }
        var colorReference = new AttachmentReference(
            0,
            ImageLayout.ColorAttachmentOptimal);
        var depthReference = new AttachmentReference(
            depthOnly ? 0u : 1u,
            ImageLayout.DepthStencilAttachmentOptimal);
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = depthOnly ? 0u : 1u,
            PColorAttachments = depthOnly ? null : &colorReference,
            PDepthStencilAttachment = &depthReference
        };
        SubpassDependency dependency = GetTriangleRenderPassDependency();
        var createInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = depthOnly ? 1u : 2u,
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

    internal static SubpassDependency GetTriangleRenderPassDependency() => new()
    {
        SrcSubpass = Vk.SubpassExternal,
        DstSubpass = 0,
        // Each indexed draw opens a render pass whose LOAD operations consume
        // attachment writes from earlier draws, not only transfer operations.
        SrcStageMask = PipelineStageFlags.TransferBit |
            PipelineStageFlags.ColorAttachmentOutputBit |
            PipelineStageFlags.EarlyFragmentTestsBit |
            PipelineStageFlags.LateFragmentTestsBit,
        DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit |
            PipelineStageFlags.EarlyFragmentTestsBit |
            PipelineStageFlags.LateFragmentTestsBit,
        SrcAccessMask = AccessFlags.TransferWriteBit |
            AccessFlags.TransferReadBit |
            AccessFlags.ColorAttachmentWriteBit |
            AccessFlags.DepthStencilAttachmentWriteBit,
        DstAccessMask = AccessFlags.ColorAttachmentReadBit |
            AccessFlags.ColorAttachmentWriteBit |
            AccessFlags.DepthStencilAttachmentReadBit |
            AccessFlags.DepthStencilAttachmentWriteBit
    };

    private Pipeline CreateTrianglePipeline(
        SilkGraphicsPipelineDescriptor descriptor,
        VulkanSilkGraphicsShaderProgram program,
        PipelineLayout layout,
        RenderPass renderPass,
        SilkTopologyKind topologyKind)
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
                descriptor.VertexLayout.Stride,
                VertexInputRate.Vertex);
            VertexInputAttributeDescription* attributes =
                stackalloc VertexInputAttributeDescription[
                    descriptor.VertexLayout.Attributes.Count];
            for (int index = 0; index < descriptor.VertexLayout.Attributes.Count; index++)
            {
                SilkVertexAttributeDescriptor attribute =
                    descriptor.VertexLayout.Attributes[index];
                attributes[index] = new VertexInputAttributeDescription(
                    attribute.Location,
                    0,
                    GetFormat(attribute.Format),
                    attribute.Offset);
            }
            var vertexInput = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 1,
                PVertexBindingDescriptions = &binding,
                VertexAttributeDescriptionCount =
                    checked((uint)descriptor.VertexLayout.Attributes.Count),
                PVertexAttributeDescriptions = attributes
            };
            var inputAssembly = new PipelineInputAssemblyStateCreateInfo
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = topologyKind switch
                {
                    SilkTopologyKind.LineList => PrimitiveTopology.LineList,
                    SilkTopologyKind.PointList => PrimitiveTopology.PointList,
                    _ => PrimitiveTopology.TriangleList
                }
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
                DepthWriteEnable = descriptor.DepthWriteEnabled,
                DepthCompareOp = CompareOp.LessOrEqual
            };
            var colorAttachment = new PipelineColorBlendAttachmentState
            {
                BlendEnable = descriptor.BlendMode == SilkBlendMode.StraightAlphaOver,
                SrcColorBlendFactor = BlendFactor.SrcAlpha,
                DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = BlendFactor.One,
                DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
                AlphaBlendOp = BlendOp.Add,
                ColorWriteMask = ColorComponentFlags.RBit |
                    ColorComponentFlags.GBit |
                    ColorComponentFlags.BBit |
                    ColorComponentFlags.ABit
            };
            var colorBlend = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = descriptor.DepthOnly ? 0u : 1u,
                PAttachments = descriptor.DepthOnly ? null : &colorAttachment
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
            SilkCullMode.Front => CullModeFlags.FrontBit,
            _ => throw new ArgumentOutOfRangeException(nameof(cullMode))
        };

    private static Format GetFormat(SilkVertexFormat format) =>
        format switch
        {
            SilkVertexFormat.Float2 => Format.R32G32Sfloat,
            SilkVertexFormat.Float3 => Format.R32G32B32Sfloat,
            SilkVertexFormat.Float4 => Format.R32G32B32A32Sfloat,
            _ => throw new ArgumentOutOfRangeException(nameof(format))
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
    private IDisposable?[]? _resourceLeases = resourceLeases;
    private bool _releasingLeases;

    internal VulkanSilkGraphicsDevice Owner { get; } = owner;

    internal VulkanSilkGraphicsShaderModule Vertex { get; } = vertex;

    internal VulkanSilkGraphicsShaderModule Fragment { get; } = fragment;

    internal VulkanSilkGraphicsBindingLayout Layout { get; } = layout;

    public ISilkGraphicsBindingLayout BindingLayout => Layout;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfDisposed() => ThrowIfResourceDisposed();

    protected override void ReleaseNative()
    {
        RetireOwnedLeases(ref _resourceLeases, ref _releasingLeases);
        Owner.ReleaseDependentObject();
    }

    internal static void RetireOwnedLeases(ref IDisposable?[]? leases, ref bool releasing) =>
        SilkOwnedResourceLeases.Dispose(ref leases, ref releasing);
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
        SilkOwnedResourceLeases.Dispose(ref _programLease);
        Owner.ReleaseDependentObject();
    }
}
