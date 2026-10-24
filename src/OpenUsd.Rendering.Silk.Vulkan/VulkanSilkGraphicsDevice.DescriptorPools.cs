// Copyright (c) marcschier. Licensed under the MIT License.

using global::Silk.NET.Vulkan;

namespace OpenUsd.Rendering.Silk.Vulkan;

public sealed unsafe partial class VulkanSilkGraphicsDevice
{
    internal sealed class SubmissionDescriptorPools(VulkanSilkGraphicsDevice owner) : IDisposable
    {
        private const uint SetsPerPool = 256;
        private readonly Dictionary<ulong, Page> _current = [];
        private readonly List<DescriptorPool> _pools = [];
        private bool _disposed;

        internal DescriptorSet Allocate(VulkanSilkGraphicsPipeline pipeline)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            DescriptorSetLayout layout = pipeline.DescriptorSetLayout;
            if (!_current.TryGetValue(layout.Handle, out Page? page) || page.Count == SetsPerPool)
            {
                DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[4];
                uint sizeCount = DescribeMaterialPoolSizes(pipeline.BindingLayout, sizes);
                if (sizeCount == 0)
                {
                    sizes[0] = new DescriptorPoolSize(DescriptorType.UniformBuffer, 1);
                    sizeCount = 1;
                }
                for (int index = 0; index < sizeCount; index++)
                {
                    sizes[index].DescriptorCount = checked(sizes[index].DescriptorCount * SetsPerPool);
                }
                var info = new DescriptorPoolCreateInfo
                {
                    SType = StructureType.DescriptorPoolCreateInfo,
                    MaxSets = SetsPerPool,
                    PoolSizeCount = sizeCount,
                    PPoolSizes = sizes
                };
                DescriptorPool pool = default;
                ThrowIfFailed(owner._api.CreateDescriptorPool(owner._device, &info, null, &pool),
                    "vkCreateDescriptorPool");
                Interlocked.Increment(ref owner._drawDescriptorPoolCreations);
                try
                {
                    _pools.Add(pool);
                }
                catch
                {
                    owner._api.DestroyDescriptorPool(owner._device, pool, null);
                    throw;
                }
                page = new Page(pool);
                _current[layout.Handle] = page;
            }
            var allocation = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = page.Pool,
                DescriptorSetCount = 1,
                PSetLayouts = &layout
            };
            DescriptorSet set = default;
            ThrowIfFailed(owner._api.AllocateDescriptorSets(owner._device, &allocation, &set),
                "vkAllocateDescriptorSets");
            page.Count++;
            return set;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            foreach (DescriptorPool pool in _pools)
            {
                owner._api.DestroyDescriptorPool(owner._device, pool, null);
            }
            _pools.Clear();
            _current.Clear();
            _disposed = true;
        }

        private sealed class Page(DescriptorPool pool)
        {
            internal DescriptorPool Pool { get; } = pool;
            internal uint Count { get; set; }
        }
    }
}
