// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using global::Silk.NET.Vulkan;

namespace OpenUsd.Rendering.Silk.Vulkan;

public sealed unsafe partial class VulkanSilkGraphicsDevice
{
    /// <inheritdoc/>
    public ISilkComputeBindingLayout CreateComputeBindingLayout(
        SilkComputeBindingLayoutDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        descriptor.Validate();
        DescriptorSetLayout layout = default;
        RegisterDependentObject();
        bool success = false;
        try
        {
            DescriptorSetLayoutBinding* bindings =
                stackalloc DescriptorSetLayoutBinding[2];
            bindings[0] = new DescriptorSetLayoutBinding
            {
                Binding = descriptor.StorageBinding,
                DescriptorType = DescriptorType.StorageBuffer,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.ComputeBit
            };
            bindings[1] = new DescriptorSetLayoutBinding
            {
                Binding = descriptor.UniformBinding,
                DescriptorType = DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.ComputeBit
            };
            var createInfo = new DescriptorSetLayoutCreateInfo
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = 2,
                PBindings = bindings
            };
            ThrowIfFailed(
                _api.CreateDescriptorSetLayout(_device, &createInfo, null, &layout),
                "vkCreateDescriptorSetLayout");
            success = true;
            return new VulkanSilkComputeBindingLayout(
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

    /// <inheritdoc/>
    public ISilkComputeShaderProgram CreateComputeShaderProgram(
        SilkComputeShaderProgramDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (descriptor.ComputeShader is not VulkanSilkGraphicsShaderModule shader ||
            descriptor.BindingLayout is not VulkanSilkComputeBindingLayout layout ||
            !ReferenceEquals(shader.Owner, this) ||
            !ReferenceEquals(layout.Owner, this))
        {
            throw new ArgumentException(
                "Compute program resources must belong to this Vulkan device.",
                nameof(descriptor));
        }
        shader.ThrowIfDisposed();
        layout.ThrowIfDisposed();
        if (shader.Descriptor.Stage != SilkShaderStage.Compute)
        {
            throw new ArgumentException(
                "The compute program requires a compute shader module.",
                nameof(descriptor));
        }
        IDisposable[] leases = [shader.AcquireLease(), layout.AcquireLease()];
        RegisterDependentObject();
        return new VulkanSilkComputeShaderProgram(
            this,
            shader,
            layout,
            leases);
    }

    /// <inheritdoc/>
    public ISilkComputePipeline CreateComputePipeline(
        SilkComputePipelineDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        descriptor.Validate();
        if (descriptor.Program is not VulkanSilkComputeShaderProgram program ||
            !ReferenceEquals(program.Owner, this))
        {
            throw new ArgumentException(
                "The compute program was not created by this Vulkan device.",
                nameof(descriptor));
        }
        program.ThrowIfDisposed();

        PipelineLayout pipelineLayout = default;
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

            byte[] entryPoint = Encoding.UTF8.GetBytes(
                program.Shader.Descriptor.EntryPoint + "\0");
            fixed (byte* entryPointer = entryPoint)
            {
                var pipelineInfo = new ComputePipelineCreateInfo
                {
                    SType = StructureType.ComputePipelineCreateInfo,
                    Stage = new PipelineShaderStageCreateInfo
                    {
                        SType = StructureType.PipelineShaderStageCreateInfo,
                        Stage = ShaderStageFlags.ComputeBit,
                        Module = program.Shader.Module,
                        PName = entryPointer
                    },
                    Layout = pipelineLayout
                };
                ThrowIfFailed(
                    _api.CreateComputePipelines(
                        _device,
                        default,
                        1,
                        &pipelineInfo,
                        null,
                        &pipeline),
                    "vkCreateComputePipelines");
            }
            programLease = program.AcquireLease();
            success = true;
            return new VulkanSilkComputePipeline(
                this,
                _api,
                _device,
                descriptor,
                pipelineLayout,
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
                if (pipelineLayout.Handle != 0)
                {
                    _api.DestroyPipelineLayout(_device, pipelineLayout, null);
                }
                ReleaseDependentObject();
            }
        }
    }

    internal void Readback(
        VulkanSilkGraphicsBuffer buffer,
        Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        buffer.ThrowIfDisposed();
        WaitIdle();

        global::Silk.NET.Vulkan.Buffer readback = default;
        DeviceMemory memory = default;
        CommandPool pool = default;
        CommandBuffer commands = default;
        Fence fence = default;
        try
        {
            CreateHostBuffer(
                checked((ulong)buffer.Size),
                BufferUsageFlags.TransferDstBit,
                out readback,
                out memory);
            CreateCommandBuffer(out pool, out commands);
            var barrier = new BufferMemoryBarrier
            {
                SType = StructureType.BufferMemoryBarrier,
                SrcAccessMask = AccessFlags.ShaderWriteBit,
                DstAccessMask = AccessFlags.TransferReadBit,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Buffer = buffer.Buffer,
                Offset = 0,
                Size = Vk.WholeSize
            };
            _api.CmdPipelineBarrier(
                commands,
                PipelineStageFlags.ComputeShaderBit,
                PipelineStageFlags.TransferBit,
                0,
                0,
                null,
                1,
                &barrier,
                0,
                null);
            var copy = new BufferCopy(0, 0, checked((ulong)buffer.Size));
            _api.CmdCopyBuffer(commands, buffer.Buffer, readback, 1, &copy);
            ThrowIfFailed(_api.EndCommandBuffer(commands), "vkEndCommandBuffer");
            fence = SubmitCommandBuffer(commands);
            WaitForFence(fence);

            void* mapped = null;
            ThrowIfFailed(
                _api.MapMemory(
                    _device,
                    memory,
                    0,
                    checked((ulong)buffer.Size),
                    0,
                    &mapped),
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
            if (readback.Handle != 0)
            {
                _api.DestroyBuffer(_device, readback, null);
            }
            if (memory.Handle != 0)
            {
                _api.FreeMemory(_device, memory, null);
            }
        }
    }
}

internal sealed class VulkanSilkComputeBindingLayout(
    VulkanSilkGraphicsDevice owner,
    Vk api,
    Device device,
    DescriptorSetLayout layout,
    SilkComputeBindingLayoutDescriptor descriptor)
    : SilkGraphicsResourceBase, ISilkComputeBindingLayout
{
    private readonly Vk _api = api;
    private readonly Device _device = device;
    private DescriptorSetLayout _layout = layout;

    internal VulkanSilkGraphicsDevice Owner { get; } = owner;

    internal DescriptorSetLayout Layout => _layout;

    public SilkComputeBindingLayoutDescriptor Descriptor { get; } = descriptor;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfDisposed() => ThrowIfResourceDisposed();

    protected override unsafe void ReleaseNative()
    {
        _api.DestroyDescriptorSetLayout(_device, _layout, null);
        _layout = default;
        Owner.ReleaseDependentObject();
    }
}

internal sealed class VulkanSilkComputeShaderProgram(
    VulkanSilkGraphicsDevice owner,
    VulkanSilkGraphicsShaderModule shader,
    VulkanSilkComputeBindingLayout layout,
    IDisposable[] resourceLeases)
    : SilkGraphicsResourceBase, ISilkComputeShaderProgram
{
    private IDisposable[]? _resourceLeases = resourceLeases;

    internal VulkanSilkGraphicsDevice Owner { get; } = owner;

    internal VulkanSilkGraphicsShaderModule Shader { get; } = shader;

    internal VulkanSilkComputeBindingLayout Layout { get; } = layout;

    public ISilkComputeBindingLayout BindingLayout => Layout;

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

internal sealed class VulkanSilkComputePipeline(
    VulkanSilkGraphicsDevice owner,
    Vk api,
    Device device,
    SilkComputePipelineDescriptor descriptor,
    PipelineLayout layout,
    Pipeline pipeline,
    DescriptorSetLayout descriptorSetLayout,
    IDisposable programLease)
    : SilkGraphicsResourceBase, ISilkComputePipeline
{
    private readonly Vk _api = api;
    private readonly Device _device = device;
    private PipelineLayout _layout = layout;
    private Pipeline _pipeline = pipeline;
    private IDisposable? _programLease = programLease;

    internal VulkanSilkGraphicsDevice Owner { get; } = owner;

    public SilkComputePipelineDescriptor Descriptor { get; } = descriptor;

    internal PipelineLayout Layout => _layout;

    internal Pipeline Pipeline => _pipeline;

    internal DescriptorSetLayout DescriptorSetLayout { get; } = descriptorSetLayout;

    internal IDisposable AcquireLease() => AcquireResourceLease();

    internal void ThrowIfDisposed() => ThrowIfResourceDisposed();

    protected override unsafe void ReleaseNative()
    {
        _api.DestroyPipeline(_device, _pipeline, null);
        _api.DestroyPipelineLayout(_device, _layout, null);
        _pipeline = default;
        _layout = default;
        Interlocked.Exchange(ref _programLease, null)?.Dispose();
        Owner.ReleaseDependentObject();
    }
}
