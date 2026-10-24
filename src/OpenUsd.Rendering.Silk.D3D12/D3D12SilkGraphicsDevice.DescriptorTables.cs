// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using Silk.NET.Direct3D12;

namespace OpenUsd.Rendering.Silk.D3D12;

[SupportedOSPlatform("windows")]
public sealed unsafe partial class D3D12SilkGraphicsDevice
{
    internal readonly record struct SubmissionDescriptorTable(nint Heap, GpuDescriptorHandle Start);

    internal sealed class SubmissionDescriptorArena(
        D3D12SilkGraphicsDevice owner, DescriptorHeapType type, List<nint> retainedHeaps)
    {
        private readonly Dictionary<ulong, List<Entry>> _tables = [];
        private ID3D12DescriptorHeap* _heap;
        private uint _used;
        private uint _capacity;

        internal SubmissionDescriptorTable Resolve(ReadOnlySpan<CpuDescriptorHandle> sources)
        {
            if (sources.IsEmpty)
            {
                return default;
            }
            uint count = checked((uint)sources.Length);
            uint maximum = type == DescriptorHeapType.Sampler ? 2048u : 1_000_000u;
            if (count > maximum)
            {
                throw new NotSupportedException("A material descriptor table exceeds the D3D12 heap limit.");
            }
            ulong hash = 14695981039346656037ul;
            foreach (CpuDescriptorHandle source in sources)
            {
                hash = unchecked((hash ^ source.Ptr) * 1099511628211ul);
            }
            if (_tables.TryGetValue(hash, out List<Entry>? matches))
            {
                foreach (Entry entry in matches)
                {
                    if (entry.Matches(sources))
                    {
                        return entry.Table;
                    }
                }
            }
            var identities = new nuint[sources.Length];
            for (int index = 0; index < identities.Length; index++)
            {
                identities[index] = sources[index].Ptr;
            }
            if (_heap == null || count > _capacity - _used)
            {
                _capacity = Math.Max(count, type == DescriptorHeapType.Sampler ? 2048u : 4096u);
                _heap = owner.CreateShaderVisibleHeap(type, _capacity, retainedHeaps);
                _used = 0;
            }
            uint increment = owner._device->GetDescriptorHandleIncrementSize(type);
            uint first = _used;
            _used += count;
            for (int index = 0; index < sources.Length; index++)
            {
                // Layout supersets may contain slots unused by the active shader.
                if (sources[index].Ptr == 0)
                {
                    continue;
                }
                var destination = new CpuDescriptorHandle(
                    _heap->GetCPUDescriptorHandleForHeapStart().Ptr + ((first + (uint)index) * increment));
                owner._device->CopyDescriptorsSimple(1, destination, sources[index], type);
            }
            var table = new SubmissionDescriptorTable((nint)_heap,
                new GpuDescriptorHandle(_heap->GetGPUDescriptorHandleForHeapStart().Ptr + (first * increment)));
            if (matches is null)
            {
                matches = [];
                _tables.Add(hash, matches);
            }
            matches.Add(new Entry(identities, table));
            return table;
        }

        private sealed record Entry(nuint[] Sources, SubmissionDescriptorTable Table)
        {
            internal bool Matches(ReadOnlySpan<CpuDescriptorHandle> sources)
            {
                if (Sources.Length != sources.Length)
                {
                    return false;
                }
                for (int index = 0; index < Sources.Length; index++)
                {
                    if (Sources[index] != sources[index].Ptr)
                    {
                        return false;
                    }
                }
                return true;
            }
        }
    }
}
