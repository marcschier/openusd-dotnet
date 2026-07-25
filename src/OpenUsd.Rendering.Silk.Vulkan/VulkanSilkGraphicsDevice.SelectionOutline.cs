// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using global::Silk.NET.Vulkan;

namespace OpenUsd.Rendering.Silk.Vulkan;

public sealed unsafe partial class VulkanSilkGraphicsDevice
{
    private long _selectionOutlineDeviceGeneration = 1;
    private int _nextSelectionSubmissionFailureForTesting;
    private int _nextSelectionFenceFailureForTesting;
    private long _selectionMaskPipelineCreations;
    private long _selectionOutlinePipelineCreations;
    private long _selectionBindingCreations;
    private long _selectionFramebufferCreations;
    private long _selectionDescriptorSetCreations;
    private long _selectionSubmissions;
    private long _selectionDeviceLosses;
    private long _selectionLiveDependentObjects;
    private VulkanSilkSelectionMaskGraphicsPipeline? _currentSelectionMaskPipeline;
    private VulkanSilkSelectionOutlineGraphicsPipeline? _currentSelectionOutlinePipeline;

    /// <inheritdoc/>
    public ulong SelectionOutlineDeviceGeneration =>
        unchecked((ulong)Interlocked.Read(ref _selectionOutlineDeviceGeneration));

    /// <inheritdoc/>
    public SilkSelectionOutlineCapabilities SelectionOutlineCapabilities =>
        SilkSelectionOutlineCapabilities.VisibleOnly;

    /// <inheritdoc/>
    public ISilkSelectionMaskGraphicsPipeline CreateSelectionMaskGraphicsPipeline(
        SilkSelectionMaskPipelineDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        descriptor.Validate();
        if (descriptor.VertexShader.Format != SilkShaderBinaryFormat.SpirV)
        {
            throw new ArgumentException(
                "Vulkan selection-mask pipelines require checked SPIR-V shaders.",
                nameof(descriptor));
        }

        RegisterDependentObject();
        try
        {
            VulkanSilkSelectionMaskGraphicsPipeline pipeline =
                VulkanSilkSelectionMaskGraphicsPipeline.Create(
                    this,
                    _api,
                    _device,
                    descriptor,
                    SelectionOutlineDeviceGeneration);
            _currentSelectionMaskPipeline = pipeline;
            Interlocked.Increment(ref _selectionMaskPipelineCreations);
            Interlocked.Increment(ref _selectionLiveDependentObjects);
            return pipeline;
        }
        catch
        {
            ReleaseDependentObject();
            throw;
        }
    }

    /// <inheritdoc/>
    public ISilkSelectionOutlineGraphicsPipeline CreateSelectionOutlineGraphicsPipeline(
        SilkSelectionOutlinePipelineDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        descriptor.Validate();
        if (descriptor.VertexShader.Format != SilkShaderBinaryFormat.SpirV)
        {
            throw new ArgumentException(
                "Vulkan selection-outline pipelines require checked SPIR-V shaders.",
                nameof(descriptor));
        }

        RegisterDependentObject();
        try
        {
            VulkanSilkSelectionOutlineGraphicsPipeline pipeline =
                VulkanSilkSelectionOutlineGraphicsPipeline.Create(
                    this,
                    _api,
                    _device,
                    descriptor,
                    SelectionOutlineDeviceGeneration);
            _currentSelectionOutlinePipeline = pipeline;
            Interlocked.Increment(ref _selectionOutlinePipelineCreations);
            Interlocked.Increment(ref _selectionLiveDependentObjects);
            return pipeline;
        }
        catch
        {
            ReleaseDependentObject();
            throw;
        }
    }

    /// <inheritdoc/>
    public ISilkSelectionOutlineBinding CreateSelectionOutlineBinding(
        SilkSelectionOutlineBindingDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        descriptor.Validate();
        if (descriptor.MaskTexture is not VulkanSilkGraphicsTexture mask ||
            descriptor.VisibleDepthTexture is not VulkanSilkGraphicsTexture depth ||
            descriptor.Sampler is not VulkanSilkGraphicsSampler sampler ||
            descriptor.Parameters is not VulkanSilkGraphicsBuffer parameters ||
            !ReferenceEquals(mask.Device, this) ||
            !ReferenceEquals(depth.Device, this) ||
            !ReferenceEquals(sampler.Owner, this) ||
            !ReferenceEquals(parameters.Owner, this))
        {
            throw new ArgumentException(
                "Selection-outline binding resources must belong to this Vulkan device.",
                nameof(descriptor));
        }
        VulkanSilkSelectionMaskGraphicsPipeline maskPipeline =
            _currentSelectionMaskPipeline ??
            throw new InvalidOperationException(
                "Create the Vulkan selection-mask pipeline before its binding.");
        VulkanSilkSelectionOutlineGraphicsPipeline outlinePipeline =
            _currentSelectionOutlinePipeline ??
            throw new InvalidOperationException(
                "Create the Vulkan selection-outline pipeline before its binding.");
        maskPipeline.ThrowIfDisposed();
        outlinePipeline.ThrowIfDisposed();
        if (maskPipeline.DeviceGeneration != SelectionOutlineDeviceGeneration ||
            outlinePipeline.DeviceGeneration != SelectionOutlineDeviceGeneration)
        {
            throw new InvalidOperationException(
                "The Vulkan selection pipelines belong to a stale device generation.");
        }

        RegisterDependentObject();
        VulkanSilkSelectionOutlineBinding binding;
        try
        {
            binding = VulkanSilkSelectionOutlineBinding.Create(
                this,
                _api,
                _device,
                descriptor,
                mask,
                depth,
                sampler,
                parameters,
                outlinePipeline.DescriptorSetLayout,
                SelectionOutlineDeviceGeneration);
        }
        catch
        {
            ReleaseDependentObject();
            throw;
        }

        Interlocked.Increment(ref _selectionLiveDependentObjects);
        try
        {
            _ = maskPipeline.GetFramebuffer(mask, depth);
            Interlocked.Increment(ref _selectionBindingCreations);
            return binding;
        }
        catch
        {
            binding.Dispose();
            throw;
        }
    }

    internal VulkanSilkSelectionOutlineDiagnostics SelectionOutlineDiagnostics => new(
        SelectionOutlineDeviceGeneration,
        Interlocked.Read(ref _selectionMaskPipelineCreations),
        Interlocked.Read(ref _selectionOutlinePipelineCreations),
        Interlocked.Read(ref _selectionBindingCreations),
        Interlocked.Read(ref _selectionFramebufferCreations),
        Interlocked.Read(ref _selectionDescriptorSetCreations),
        Interlocked.Read(ref _selectionSubmissions),
        Interlocked.Read(ref _selectionDeviceLosses),
        Interlocked.Read(ref _selectionLiveDependentObjects));

    internal void AdvanceSelectionOutlineDeviceGenerationForTesting() =>
        AdvanceSelectionOutlineDeviceGeneration(deviceLost: false);

    internal void FailNextSelectionOutlineSubmissionForTesting(bool deviceLost) =>
        Interlocked.Exchange(
            ref _nextSelectionSubmissionFailureForTesting,
            deviceLost ? 2 : 1);

    internal void FailNextSelectionOutlineFenceForTesting(bool deviceLost) =>
        Interlocked.Exchange(
            ref _nextSelectionFenceFailureForTesting,
            deviceLost ? 2 : 1);

    internal void ThrowIfSelectionOutlineSubmissionFailureForTesting()
    {
        int failure = Interlocked.Exchange(
            ref _nextSelectionSubmissionFailureForTesting,
            0);
        if (failure == 0)
        {
            return;
        }
        if (failure == 2)
        {
            NotifySelectionOutlineDeviceLost();
        }
        throw new InvalidOperationException(
            failure == 2
                ? "Injected Vulkan selection-outline device loss."
                : "Injected Vulkan selection-outline submission failure.");
    }

    internal bool TryConsumeSelectionOutlineFenceFailureForTesting(
        out Result result)
    {
        int failure = Interlocked.Exchange(
            ref _nextSelectionFenceFailureForTesting,
            0);
        result = failure switch
        {
            2 => Result.ErrorDeviceLost,
            1 => Result.ErrorOutOfHostMemory,
            _ => Result.Success
        };
        return failure != 0;
    }

    internal void NotifySelectionOutlineDeviceLost() =>
        AdvanceSelectionOutlineDeviceGeneration(deviceLost: true);

    internal void CountSelectionFramebufferCreation() =>
        Interlocked.Increment(ref _selectionFramebufferCreations);

    internal void CountSelectionDescriptorSetCreation() =>
        Interlocked.Increment(ref _selectionDescriptorSetCreations);

    internal void CountSelectionSubmission() =>
        Interlocked.Increment(ref _selectionSubmissions);

    internal void ReleaseSelectionDependent(object resource)
    {
        if (ReferenceEquals(_currentSelectionMaskPipeline, resource))
        {
            _currentSelectionMaskPipeline = null;
        }
        if (ReferenceEquals(_currentSelectionOutlinePipeline, resource))
        {
            _currentSelectionOutlinePipeline = null;
        }
        Interlocked.Decrement(ref _selectionLiveDependentObjects);
        ReleaseDependentObject();
    }

    private void RecordSelectionMaskDraw(
        CommandBuffer commands,
        VulkanSilkSelectionMaskGraphicsPipeline pipeline,
        VulkanSilkGraphicsTexture mask,
        VulkanSilkGraphicsTexture depth,
        VulkanSilkGraphicsBuffer vertexBuffer,
        VulkanSilkGraphicsBuffer indexBuffer,
        VulkanSilkGraphicsBuffer uniformBuffer,
        SilkViewport viewport,
        SilkScissor scissor,
        uint indexCount)
    {
        Framebuffer framebuffer = pipeline.GetFramebuffer(mask, depth);
        var beginInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = pipeline.RenderPass,
            Framebuffer = framebuffer,
            RenderArea = new Rect2D(
                new Offset2D(0, 0),
                new Extent2D(mask.Width, mask.Height))
        };
        _api.CmdBeginRenderPass(commands, &beginInfo, SubpassContents.Inline);
        _api.CmdBindPipeline(
            commands,
            PipelineBindPoint.Graphics,
            pipeline.Pipeline);
        SetSelectionViewportAndScissor(commands, viewport, scissor);
        global::Silk.NET.Vulkan.Buffer nativeVertexBuffer = vertexBuffer.Buffer;
        ulong vertexOffset = 0;
        _api.CmdBindVertexBuffers(
            commands,
            0,
            1,
            &nativeVertexBuffer,
            &vertexOffset);
        _api.CmdBindIndexBuffer(
            commands,
            indexBuffer.Buffer,
            0,
            IndexType.Uint32);
        DescriptorSet descriptorSet = pipeline.GetDescriptorSet(uniformBuffer);
        _api.CmdBindDescriptorSets(
            commands,
            PipelineBindPoint.Graphics,
            pipeline.Layout,
            0,
            1,
            &descriptorSet,
            0,
            null);
        _api.CmdDrawIndexed(commands, indexCount, 1, 0, 0, 0);
        _api.CmdEndRenderPass(commands);
    }

    private void RecordSelectionOutlineDraw(
        CommandBuffer commands,
        VulkanSilkSelectionOutlineGraphicsPipeline pipeline,
        VulkanSilkSelectionOutlineBinding binding,
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
        SetSelectionViewportAndScissor(commands, viewport, scissor);
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

    private void SetSelectionViewportAndScissor(
        CommandBuffer commands,
        SilkViewport viewport,
        SilkScissor scissor)
    {
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
    }

    private void AdvanceSelectionOutlineDeviceGeneration(bool deviceLost)
    {
        long generation = Interlocked.Increment(
            ref _selectionOutlineDeviceGeneration);
        if (generation == 0)
        {
            _ = Interlocked.Increment(ref _selectionOutlineDeviceGeneration);
        }
        if (deviceLost)
        {
            Interlocked.Increment(ref _selectionDeviceLosses);
        }
    }
}

internal readonly record struct VulkanSilkSelectionOutlineDiagnostics(
    ulong DeviceGeneration,
    long MaskPipelineCreations,
    long OutlinePipelineCreations,
    long BindingCreations,
    long FramebufferCreations,
    long DescriptorSetCreations,
    long Submissions,
    long DeviceLosses,
    long LiveDependentObjects);

internal sealed unsafe class VulkanSilkSelectionMaskGraphicsPipeline :
    SilkGraphicsResourceBase,
    ISilkSelectionMaskGraphicsPipeline
{
    private const uint MaximumCachedSceneBindings = 4096;
    private readonly Vk _api;
    private readonly Device _device;
    private readonly Dictionary<VulkanSilkGraphicsBuffer, CachedDescriptorSet>
        _descriptorSets = [];
    private ShaderModule _vertexShader;
    private ShaderModule _fragmentShader;
    private DescriptorSetLayout _descriptorSetLayout;
    private PipelineLayout _layout;
    private RenderPass _renderPass;
    private Pipeline _pipeline;
    private DescriptorPool _descriptorPool;
    private Framebuffer _framebuffer;
    private VulkanSilkGraphicsTexture? _framebufferMask;
    private VulkanSilkGraphicsTexture? _framebufferDepth;
    private IDisposable? _framebufferMaskLease;
    private IDisposable? _framebufferDepthLease;

    private VulkanSilkSelectionMaskGraphicsPipeline(
        VulkanSilkGraphicsDevice owner,
        Vk api,
        Device device,
        SilkSelectionMaskPipelineDescriptor descriptor,
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

    public SilkSelectionMaskPipelineDescriptor Descriptor { get; }

    internal PipelineLayout Layout => _layout;

    internal RenderPass RenderPass => _renderPass;

    internal Pipeline Pipeline => _pipeline;

    internal static VulkanSilkSelectionMaskGraphicsPipeline Create(
        VulkanSilkGraphicsDevice owner,
        Vk api,
        Device device,
        SilkSelectionMaskPipelineDescriptor descriptor,
        ulong deviceGeneration)
    {
        var result = new VulkanSilkSelectionMaskGraphicsPipeline(
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

    internal DescriptorSet GetDescriptorSet(VulkanSilkGraphicsBuffer uniformBuffer)
    {
        ThrowIfDisposed();
        uniformBuffer.ThrowIfDisposed();
        if (_descriptorSets.TryGetValue(
                uniformBuffer,
                out CachedDescriptorSet cached))
        {
            return cached.Set;
        }
        if (_descriptorSets.Count >= MaximumCachedSceneBindings)
        {
            throw new InvalidOperationException(
                "The Vulkan selection-mask descriptor cache is full.");
        }

        DescriptorSetLayout layout = _descriptorSetLayout;
        var allocationInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &layout
        };
        DescriptorSet set = default;
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.AllocateDescriptorSets(_device, &allocationInfo, &set),
            "vkAllocateDescriptorSets(selection mask)");
        var bufferInfo = new DescriptorBufferInfo(
            uniformBuffer.Buffer,
            0,
            80);
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.UniformBuffer,
            PBufferInfo = &bufferInfo
        };
        _api.UpdateDescriptorSets(_device, 1, &write, 0, null);
        _descriptorSets.Add(
            uniformBuffer,
            new CachedDescriptorSet(set, uniformBuffer.AcquireLease()));
        Owner.CountSelectionDescriptorSetCreation();
        return set;
    }

    internal Framebuffer GetFramebuffer(
        VulkanSilkGraphicsTexture mask,
        VulkanSilkGraphicsTexture depth)
    {
        ThrowIfDisposed();
        mask.ThrowIfDisposed();
        depth.ThrowIfDisposed();
        if (_framebuffer.Handle != 0 &&
            ReferenceEquals(_framebufferMask, mask) &&
            ReferenceEquals(_framebufferDepth, depth))
        {
            return _framebuffer;
        }

        DestroyFramebuffer();
        IDisposable? maskLease = null;
        IDisposable? depthLease = null;
        try
        {
            maskLease = mask.AcquireLease();
            depthLease = depth.AcquireLease();
            ImageView* attachments = stackalloc ImageView[2]
            {
                mask.ImageView,
                depth.ImageView
            };
            var framebufferInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = _renderPass,
                AttachmentCount = 2,
                PAttachments = attachments,
                Width = mask.Width,
                Height = mask.Height,
                Layers = 1
            };
            Framebuffer framebuffer = default;
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                _api.CreateFramebuffer(
                    _device,
                    &framebufferInfo,
                    null,
                    &framebuffer),
                "vkCreateFramebuffer(selection mask)");
            _framebuffer = framebuffer;
            _framebufferMask = mask;
            _framebufferDepth = depth;
            _framebufferMaskLease = maskLease;
            _framebufferDepthLease = depthLease;
            Owner.CountSelectionFramebufferCreation();
            return _framebuffer;
        }
        catch
        {
            maskLease?.Dispose();
            depthLease?.Dispose();
            throw;
        }
    }

    protected override void ReleaseNative()
    {
        DestroyNative();
        Owner.ReleaseSelectionDependent(this);
    }

    private void Initialize()
    {
        _vertexShader = CreateShaderModule(Descriptor.VertexShader);
        _fragmentShader = CreateShaderModule(Descriptor.FragmentShader);
        var binding = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.VertexBit
        };
        var setLayoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &binding
        };
        DescriptorSetLayout descriptorSetLayout = default;
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.CreateDescriptorSetLayout(
                _device,
                &setLayoutInfo,
                null,
                &descriptorSetLayout),
            "vkCreateDescriptorSetLayout(selection mask)");
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
            "vkCreatePipelineLayout(selection mask)");
        _layout = pipelineLayout;
        CreateRenderPass();
        CreatePipeline();
        var poolSize = new DescriptorPoolSize(
            DescriptorType.UniformBuffer,
            MaximumCachedSceneBindings);
        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = MaximumCachedSceneBindings,
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
            "vkCreateDescriptorPool(selection mask)");
        _descriptorPool = descriptorPool;
    }

    private ShaderModule CreateShaderModule(SilkShaderModuleDescriptor descriptor)
    {
        byte[] code = descriptor.Code.ToArray();
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
                "vkCreateShaderModule(selection mask)");
            return module;
        }
    }

    internal static byte[] NormalizeVertexIdShader(ReadOnlySpan<byte> code)
    {
        if (code.Length % sizeof(uint) != 0)
        {
            throw new InvalidDataException(
                "The checked fullscreen vertex SPIR-V is not word aligned.");
        }
        var words = new uint[code.Length / sizeof(uint)];
        code.CopyTo(MemoryMarshal.AsBytes(words.AsSpan()));
        if (words.Length < 5 || words[0] != 0x07230203)
        {
            throw new InvalidDataException(
                "The checked fullscreen vertex shader is not SPIR-V.");
        }

        const ushort opCapability = 17;
        const ushort opEntryPoint = 15;
        const ushort opDecorate = 71;
        const ushort opVariable = 59;
        const ushort opLoad = 61;
        const ushort opISub = 130;
        const ushort opCopyObject = 83;
        const uint capabilityDrawParameters = 4427;
        const uint decorationBuiltIn = 11;
        const uint builtInBaseVertex = 4424;
        uint baseVertexVariable = 0;
        uint baseVertexValue = 0;

        for (int index = 5; index < words.Length;)
        {
            int wordCount = checked((int)(words[index] >> 16));
            ushort opcode = checked((ushort)(words[index] & 0xffff));
            if (wordCount <= 0 || index + wordCount > words.Length)
            {
                throw new InvalidDataException(
                    "The checked fullscreen vertex SPIR-V is malformed.");
            }
            if (opcode == opDecorate &&
                wordCount == 4 &&
                words[index + 2] == decorationBuiltIn &&
                words[index + 3] == builtInBaseVertex)
            {
                baseVertexVariable = words[index + 1];
            }
            index += wordCount;
        }
        if (baseVertexVariable == 0)
        {
            return code.ToArray();
        }

        for (int index = 5; index < words.Length;)
        {
            int wordCount = checked((int)(words[index] >> 16));
            ushort opcode = checked((ushort)(words[index] & 0xffff));
            if (opcode == opLoad &&
                wordCount == 4 &&
                words[index + 3] == baseVertexVariable)
            {
                baseVertexValue = words[index + 2];
                break;
            }
            index += wordCount;
        }
        if (baseVertexValue == 0)
        {
            throw new InvalidDataException(
                "The checked fullscreen vertex SPIR-V has no BaseVertex load.");
        }

        var normalized = new List<uint>(words.Length);
        normalized.AddRange(words.AsSpan(0, 5));
        for (int index = 5; index < words.Length;)
        {
            int wordCount = checked((int)(words[index] >> 16));
            ushort opcode = checked((ushort)(words[index] & 0xffff));
            ReadOnlySpan<uint> instruction = words.AsSpan(index, wordCount);
            bool remove =
                opcode == opCapability &&
                wordCount == 2 &&
                instruction[1] == capabilityDrawParameters;
            remove |=
                opcode == opDecorate &&
                wordCount == 4 &&
                instruction[1] == baseVertexVariable &&
                instruction[2] == decorationBuiltIn &&
                instruction[3] == builtInBaseVertex;
            remove |=
                opcode == opVariable &&
                wordCount == 4 &&
                instruction[2] == baseVertexVariable;
            remove |=
                opcode == opLoad &&
                wordCount == 4 &&
                instruction[2] == baseVertexValue;
            if (remove)
            {
                index += wordCount;
                continue;
            }

            if (opcode == opEntryPoint)
            {
                int interfaceStart = 3;
                while (interfaceStart < wordCount)
                {
                    uint nameWord = instruction[interfaceStart++];
                    if ((nameWord & 0xff000000) == 0 ||
                        (nameWord & 0x00ff0000) == 0 ||
                        (nameWord & 0x0000ff00) == 0 ||
                        (nameWord & 0x000000ff) == 0)
                    {
                        break;
                    }
                }
                normalized.Add(
                    checked((uint)((wordCount - 1) << 16)) | opEntryPoint);
                normalized.Add(instruction[1]);
                normalized.Add(instruction[2]);
                normalized.AddRange(instruction.Slice(3, interfaceStart - 3));
                for (int operand = interfaceStart;
                     operand < wordCount;
                     operand++)
                {
                    if (instruction[operand] != baseVertexVariable)
                    {
                        normalized.Add(instruction[operand]);
                    }
                }
            }
            else if (opcode == opISub &&
                     wordCount == 5 &&
                     instruction[4] == baseVertexValue)
            {
                normalized.Add((4u << 16) | opCopyObject);
                normalized.Add(instruction[1]);
                normalized.Add(instruction[2]);
                normalized.Add(instruction[3]);
            }
            else
            {
                normalized.AddRange(instruction);
            }
            index += wordCount;
        }

        var bytes = new byte[normalized.Count * sizeof(uint)];
        MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(normalized)).CopyTo(bytes);
        return bytes;
    }

    private void CreateRenderPass()
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
            InitialLayout = ImageLayout.DepthStencilReadOnlyOptimal,
            FinalLayout = ImageLayout.DepthStencilReadOnlyOptimal
        };
        var colorReference = new AttachmentReference(
            0,
            ImageLayout.ColorAttachmentOptimal);
        var depthReference = new AttachmentReference(
            1,
            ImageLayout.DepthStencilReadOnlyOptimal);
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
            SrcStageMask = PipelineStageFlags.TransferBit |
                PipelineStageFlags.LateFragmentTestsBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit |
                PipelineStageFlags.EarlyFragmentTestsBit,
            SrcAccessMask = AccessFlags.TransferWriteBit |
                AccessFlags.DepthStencilAttachmentWriteBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit |
                AccessFlags.DepthStencilAttachmentReadBit
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
            _api.CreateRenderPass(_device, &createInfo, null, &renderPass),
            "vkCreateRenderPass(selection mask)");
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
            var vertexBinding = new VertexInputBindingDescription(
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
                PVertexBindingDescriptions = &vertexBinding,
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
                DepthWriteEnable = false,
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
                "vkCreateGraphicsPipelines(selection mask)");
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
        _framebufferMask = null;
        _framebufferDepth = null;
        Interlocked.Exchange(ref _framebufferDepthLease, null)?.Dispose();
        Interlocked.Exchange(ref _framebufferMaskLease, null)?.Dispose();
    }

    private void DestroyNative()
    {
        DestroyFramebuffer();
        foreach (CachedDescriptorSet descriptor in _descriptorSets.Values)
        {
            descriptor.BufferLease.Dispose();
        }
        _descriptorSets.Clear();
        if (_descriptorPool.Handle != 0)
        {
            _api.DestroyDescriptorPool(_device, _descriptorPool, null);
            _descriptorPool = default;
        }
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

    private readonly record struct CachedDescriptorSet(
        DescriptorSet Set,
        IDisposable BufferLease);
}

internal sealed unsafe class VulkanSilkSelectionOutlineGraphicsPipeline :
    SilkGraphicsResourceBase,
    ISilkSelectionOutlineGraphicsPipeline
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

    private VulkanSilkSelectionOutlineGraphicsPipeline(
        VulkanSilkGraphicsDevice owner,
        Vk api,
        Device device,
        SilkSelectionOutlinePipelineDescriptor descriptor,
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

    public SilkSelectionOutlinePipelineDescriptor Descriptor { get; }

    internal DescriptorSetLayout DescriptorSetLayout => _descriptorSetLayout;

    internal PipelineLayout Layout => _layout;

    internal RenderPass RenderPass => _renderPass;

    internal Pipeline Pipeline => _pipeline;

    internal static VulkanSilkSelectionOutlineGraphicsPipeline Create(
        VulkanSilkGraphicsDevice owner,
        Vk api,
        Device device,
        SilkSelectionOutlinePipelineDescriptor descriptor,
        ulong deviceGeneration)
    {
        var result = new VulkanSilkSelectionOutlineGraphicsPipeline(
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
                "vkCreateFramebuffer(selection outline)");
            _framebuffer = framebuffer;
            _framebufferColor = color;
            _framebufferColorLease = colorLease;
            Owner.CountSelectionFramebufferCreation();
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
        Owner.ReleaseSelectionDependent(this);
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
            "vkCreateDescriptorSetLayout(selection outline)");
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
            "vkCreatePipelineLayout(selection outline)");
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
                "vkCreateShaderModule(selection outline)");
            return module;
        }
    }

    private void CreateRenderPass()
    {
        var attachment = new AttachmentDescription
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
            DstAccessMask = AccessFlags.ColorAttachmentReadBit |
                AccessFlags.ColorAttachmentWriteBit
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
            "vkCreateRenderPass(selection outline)");
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
                BlendEnable = true,
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
                "vkCreateGraphicsPipelines(selection outline)");
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

internal sealed unsafe class VulkanSilkSelectionOutlineBinding :
    SilkGraphicsResourceBase,
    ISilkSelectionOutlineBinding
{
    private readonly Vk _api;
    private readonly Device _device;
    private IDisposable[]? _resourceLeases;
    private DescriptorPool _descriptorPool;
    private DescriptorSet _descriptorSet;

    private VulkanSilkSelectionOutlineBinding(
        VulkanSilkGraphicsDevice owner,
        Vk api,
        Device device,
        SilkSelectionOutlineBindingDescriptor descriptor,
        VulkanSilkGraphicsTexture mask,
        VulkanSilkGraphicsTexture depth,
        VulkanSilkGraphicsSampler sampler,
        VulkanSilkGraphicsBuffer parameters,
        ulong deviceGeneration)
    {
        Owner = owner;
        _api = api;
        _device = device;
        Descriptor = descriptor;
        Mask = mask;
        Depth = depth;
        Sampler = sampler;
        Parameters = parameters;
        DeviceGeneration = deviceGeneration;
    }

    internal VulkanSilkGraphicsDevice Owner { get; }

    internal VulkanSilkGraphicsTexture Mask { get; }

    internal VulkanSilkGraphicsTexture Depth { get; }

    internal VulkanSilkGraphicsSampler Sampler { get; }

    internal VulkanSilkGraphicsBuffer Parameters { get; }

    internal ulong DeviceGeneration { get; }

    internal DescriptorSet DescriptorSet => _descriptorSet;

    public SilkSelectionOutlineBindingDescriptor Descriptor { get; }

    internal static VulkanSilkSelectionOutlineBinding Create(
        VulkanSilkGraphicsDevice owner,
        Vk api,
        Device device,
        SilkSelectionOutlineBindingDescriptor descriptor,
        VulkanSilkGraphicsTexture mask,
        VulkanSilkGraphicsTexture depth,
        VulkanSilkGraphicsSampler sampler,
        VulkanSilkGraphicsBuffer parameters,
        DescriptorSetLayout layout,
        ulong deviceGeneration)
    {
        var result = new VulkanSilkSelectionOutlineBinding(
            owner,
            api,
            device,
            descriptor,
            mask,
            depth,
            sampler,
            parameters,
            deviceGeneration);
        try
        {
            result.Initialize(layout);
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
        Owner.ReleaseSelectionDependent(this);
    }

    private void Initialize(DescriptorSetLayout layout)
    {
        Mask.ThrowIfDisposed();
        Depth.ThrowIfDisposed();
        Sampler.ThrowIfDisposed();
        Parameters.ThrowIfDisposed();
        _resourceLeases =
        [
            Mask.AcquireLease(),
            Depth.AcquireLease(),
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
            "vkCreateDescriptorPool(selection outline)");
        _descriptorPool = descriptorPool;
        var allocationInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &layout
        };
        DescriptorSet descriptorSet = default;
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.AllocateDescriptorSets(
                _device,
                &allocationInfo,
                &descriptorSet),
            "vkAllocateDescriptorSets(selection outline)");
        _descriptorSet = descriptorSet;
        var maskInfo = new DescriptorImageInfo(
            default,
            Mask.ImageView,
            ImageLayout.ShaderReadOnlyOptimal);
        var depthInfo = new DescriptorImageInfo(
            default,
            Depth.ImageView,
            ImageLayout.DepthStencilReadOnlyOptimal);
        var samplerInfo = new DescriptorImageInfo(
            Sampler.Sampler,
            default,
            ImageLayout.Undefined);
        var parameterInfo = new DescriptorBufferInfo(
            Parameters.Buffer,
            0,
            SilkSelectionOutlineUniformWriter.ByteSize);
        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[4];
        writes[0] = CreateImageWrite(
            _descriptorSet,
            0,
            DescriptorType.SampledImage,
            &maskInfo);
        writes[1] = CreateImageWrite(
            _descriptorSet,
            1,
            DescriptorType.SampledImage,
            &depthInfo);
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
        Owner.CountSelectionDescriptorSetCreation();
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
        if (_descriptorPool.Handle != 0)
        {
            _api.DestroyDescriptorPool(_device, _descriptorPool, null);
            _descriptorPool = default;
            _descriptorSet = default;
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
{
    private VulkanSelectionRenderingScope _selectionScope;
    private VulkanSilkSelectionMaskGraphicsPipeline? _selectionMaskPipeline;
    private VulkanSilkSelectionOutlineGraphicsPipeline? _selectionOutlinePipeline;
    private VulkanSilkSelectionOutlineBinding? _selectionOutlineBinding;
    private bool _hasSelectionOutlineSubmission;

    internal bool HasSelectionOutlineSubmission => _hasSelectionOutlineSubmission;

    public void BeginSelectionMaskRendering(
        SilkSelectionMaskRenderingDescriptor descriptor)
    {
        ThrowIfUnavailable();
        if (_rendering)
        {
            throw new InvalidOperationException("A rendering scope is already active.");
        }
        descriptor.Validate();
        VulkanSilkGraphicsTexture mask = ValidateTexture(descriptor.MaskAttachment);
        VulkanSilkGraphicsTexture depth =
            ValidateTexture(descriptor.VisibleDepthAttachment);
        _colorAttachment = mask;
        _depthAttachment = depth;
        _pipeline = null;
        _pickPipeline = null;
        _selectionMaskPipeline = null;
        _selectionScope = VulkanSelectionRenderingScope.Mask;
        _rendering = true;
        _hasSelectionOutlineSubmission = true;
        _commands.Add(VulkanGraphicsCommand.BeginSelectionMask(mask, depth));
    }

    public void SetSelectionMaskGraphicsPipeline(
        ISilkSelectionMaskGraphicsPipeline pipeline)
    {
        ThrowIfRendering();
        if (_selectionScope != VulkanSelectionRenderingScope.Mask)
        {
            throw new InvalidOperationException(
                "A Vulkan selection-mask rendering scope is not active.");
        }
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not VulkanSilkSelectionMaskGraphicsPipeline
                vulkanPipeline ||
            !ReferenceEquals(vulkanPipeline.Owner, Device))
        {
            throw new ArgumentException(
                "The selection-mask pipeline was not created by this Vulkan device.",
                nameof(pipeline));
        }
        vulkanPipeline.ThrowIfDisposed();
        if (vulkanPipeline.DeviceGeneration !=
            Device.SelectionOutlineDeviceGeneration)
        {
            throw new InvalidOperationException(
                "The Vulkan selection-mask pipeline belongs to a stale device generation.");
        }
        _selectionMaskPipeline = vulkanPipeline;
        _commands.Add(VulkanGraphicsCommand.SetSelectionMaskPipeline(
            vulkanPipeline));
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
        VulkanSilkGraphicsTexture color =
            ValidateTexture(descriptor.VisibleColorAttachment);
        _colorAttachment = color;
        _depthAttachment = null;
        _selectionOutlinePipeline = null;
        _selectionOutlineBinding = null;
        _selectionScope = VulkanSelectionRenderingScope.Outline;
        _rendering = true;
        _hasSelectionOutlineSubmission = true;
        _commands.Add(VulkanGraphicsCommand.BeginSelectionOutline(color));
    }

    public void SetSelectionOutlineGraphicsPipeline(
        ISilkSelectionOutlineGraphicsPipeline pipeline)
    {
        ThrowIfRendering();
        if (_selectionScope != VulkanSelectionRenderingScope.Outline)
        {
            throw new InvalidOperationException(
                "A Vulkan selection-outline rendering scope is not active.");
        }
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not VulkanSilkSelectionOutlineGraphicsPipeline
                vulkanPipeline ||
            !ReferenceEquals(vulkanPipeline.Owner, Device))
        {
            throw new ArgumentException(
                "The selection-outline pipeline was not created by this Vulkan device.",
                nameof(pipeline));
        }
        vulkanPipeline.ThrowIfDisposed();
        if (vulkanPipeline.DeviceGeneration !=
            Device.SelectionOutlineDeviceGeneration)
        {
            throw new InvalidOperationException(
                "The Vulkan selection-outline pipeline belongs to a stale device generation.");
        }
        _selectionOutlinePipeline = vulkanPipeline;
        _commands.Add(VulkanGraphicsCommand.SetSelectionOutlinePipeline(
            vulkanPipeline));
    }

    public void SetSelectionOutlineBinding(ISilkSelectionOutlineBinding binding)
    {
        ThrowIfRendering();
        if (_selectionScope != VulkanSelectionRenderingScope.Outline ||
            _selectionOutlinePipeline is null)
        {
            throw new InvalidOperationException(
                "Set the Vulkan selection-outline pipeline before its binding.");
        }
        ArgumentNullException.ThrowIfNull(binding);
        if (binding is not VulkanSilkSelectionOutlineBinding vulkanBinding ||
            !ReferenceEquals(vulkanBinding.Owner, Device))
        {
            throw new ArgumentException(
                "The selection-outline binding was not created by this Vulkan device.",
                nameof(binding));
        }
        vulkanBinding.ThrowIfDisposed();
        if (vulkanBinding.DeviceGeneration !=
            Device.SelectionOutlineDeviceGeneration)
        {
            throw new InvalidOperationException(
                "The Vulkan selection-outline binding belongs to a stale device generation.");
        }
        _selectionOutlineBinding = vulkanBinding;
        _commands.Add(VulkanGraphicsCommand.SetSelectionOutlineBinding(
            vulkanBinding));
    }

    public void DrawSelectionOutlineFullscreenTriangle()
    {
        ThrowIfRendering();
        if (_selectionScope != VulkanSelectionRenderingScope.Outline ||
            _selectionOutlinePipeline is null ||
            _selectionOutlineBinding is null ||
            _viewport is null ||
            _scissor is null)
        {
            throw new InvalidOperationException(
                "The Vulkan fullscreen outline requires pipeline, binding, viewport, and scissor.");
        }
        _commands.Add(VulkanGraphicsCommand.DrawSelectionOutline());
    }

    private void EndSelectionRenderingScope()
    {
        _selectionScope = VulkanSelectionRenderingScope.None;
        _selectionMaskPipeline = null;
        _selectionOutlinePipeline = null;
        _selectionOutlineBinding = null;
    }

    private void DisposeSelectionOutlineState()
    {
        EndSelectionRenderingScope();
        _hasSelectionOutlineSubmission = false;
    }
}

internal enum VulkanSelectionRenderingScope
{
    None,
    Mask,
    Outline
}
