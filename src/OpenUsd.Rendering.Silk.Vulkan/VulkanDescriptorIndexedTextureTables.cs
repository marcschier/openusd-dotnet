// Copyright (c) marcschier. Licensed under the MIT License.

using global::Silk.NET.Vulkan;

namespace OpenUsd.Rendering.Silk.Vulkan;

internal unsafe delegate DescriptorPool VulkanDescriptorPoolFactory(
    DescriptorPoolCreateInfo poolInfo);

internal sealed unsafe class VulkanDescriptorIndexedTextureTables : IDisposable
{
    private const uint MaxDescriptorSets = 4096;
    private const uint UniformDescriptorCapacity = 4096;
    private const uint SampledImageDescriptorCapacity = 4096;
    private const uint SamplerDescriptorCapacity = 2048;

    private readonly Vk _api;
    private readonly Device _device;
    private readonly object _gate = new();
    private DescriptorPool _pool;

    private VulkanDescriptorIndexedTextureTables(
        Vk api,
        Device device,
        DescriptorPool pool)
    {
        _api = api;
        _device = device;
        _pool = pool;
    }

    internal static VulkanDescriptorIndexedTextureTables? TryCreate(
        Vk api,
        Device device,
        out string? diagnostic) =>
        TryCreate(
            api,
            device,
            poolInfo =>
            {
                DescriptorPool pool = default;
                VulkanSilkGraphicsDevice.ThrowIfFailed(
                    api.CreateDescriptorPool(device, &poolInfo, null, &pool),
                    "vkCreateDescriptorPool(descriptor indexed materials)");
                return pool;
            },
            out diagnostic);

    internal static VulkanDescriptorIndexedTextureTables? TryCreate(
        Vk api,
        Device device,
        VulkanDescriptorPoolFactory createPool,
        out string? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(createPool);
        diagnostic = null;
        DescriptorPool pool = default;
        try
        {
            DescriptorPoolSize* poolSizes = stackalloc DescriptorPoolSize[3];
            poolSizes[0] = new DescriptorPoolSize(
                DescriptorType.UniformBuffer,
                UniformDescriptorCapacity);
            poolSizes[1] = new DescriptorPoolSize(
                DescriptorType.SampledImage,
                SampledImageDescriptorCapacity);
            poolSizes[2] = new DescriptorPoolSize(
                DescriptorType.Sampler,
                SamplerDescriptorCapacity);
            var poolInfo = new DescriptorPoolCreateInfo
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit,
                MaxSets = MaxDescriptorSets,
                PoolSizeCount = 3,
                PPoolSizes = poolSizes
            };
            pool = createPool(poolInfo);
            return new VulkanDescriptorIndexedTextureTables(api, device, pool);
        }
        catch (Exception exception)
        {
            if (pool.Handle != 0)
            {
                api.DestroyDescriptorPool(device, pool, null);
            }
            diagnostic = SilkCapabilityDiagnostics.DescriptorIndexedTextureTablesSetupFailed(
                "Vulkan",
                exception);
            return null;
        }
    }

    internal bool TryAllocate(
        DescriptorSetLayout layout,
        out DescriptorSet descriptorSet)
    {
        lock (_gate)
        {
            if (_pool.Handle == 0)
            {
                descriptorSet = default;
                return false;
            }
            var allocateInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = _pool,
                DescriptorSetCount = 1,
                PSetLayouts = &layout
            };
            DescriptorSet allocated = default;
            Result result = _api.AllocateDescriptorSets(
                _device,
                &allocateInfo,
                &allocated);
            if (result is Result.ErrorOutOfPoolMemory or Result.ErrorFragmentedPool)
            {
                descriptorSet = default;
                return false;
            }
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                result,
                "vkAllocateDescriptorSets(descriptor indexed materials)");
            descriptorSet = allocated;
            return true;
        }
    }

    internal void Free(DescriptorSet descriptorSet)
    {
        if (descriptorSet.Handle == 0)
        {
            return;
        }
        lock (_gate)
        {
            if (_pool.Handle != 0)
            {
                _api.FreeDescriptorSets(_device, _pool, 1, &descriptorSet);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_pool.Handle != 0)
            {
                _api.DestroyDescriptorPool(_device, _pool, null);
                _pool = default;
            }
        }
    }
}

internal readonly record struct VulkanDescriptorIndexingFeatures(
    bool RuntimeDescriptorArray,
    bool DescriptorBindingPartiallyBound,
    bool ShaderSampledImageArrayNonUniformIndexing,
    bool DescriptorBindingVariableDescriptorCount)
{
    internal bool SupportsDescriptorIndexedTextureTables =>
        RuntimeDescriptorArray &&
        DescriptorBindingPartiallyBound &&
        ShaderSampledImageArrayNonUniformIndexing &&
        DescriptorBindingVariableDescriptorCount;

    internal string DescribeDescriptorIndexedTextureTablesUnavailable(
        bool descriptorIndexingExtension,
        bool descriptorIndexingIsCore)
    {
        if (!descriptorIndexingExtension && !descriptorIndexingIsCore)
        {
            return "Vulkan descriptor-indexed texture tables unavailable: neither Vulkan 1.2 " +
                "nor VK_EXT_descriptor_indexing is available.";
        }

        List<string> missing = [];
        if (!RuntimeDescriptorArray)
        {
            missing.Add(nameof(RuntimeDescriptorArray));
        }
        if (!DescriptorBindingPartiallyBound)
        {
            missing.Add(nameof(DescriptorBindingPartiallyBound));
        }
        if (!ShaderSampledImageArrayNonUniformIndexing)
        {
            missing.Add(nameof(ShaderSampledImageArrayNonUniformIndexing));
        }
        if (!DescriptorBindingVariableDescriptorCount)
        {
            missing.Add(nameof(DescriptorBindingVariableDescriptorCount));
        }
        return "Vulkan descriptor-indexed texture tables unavailable: missing descriptor " +
            $"indexing features {string.Join(", ", missing)}.";
    }
}
