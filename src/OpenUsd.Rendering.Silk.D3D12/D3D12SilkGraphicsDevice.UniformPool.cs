// Copyright (c) marcschier. Licensed under the MIT License.

using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace OpenUsd.Rendering.Silk.D3D12;

public sealed unsafe partial class D3D12SilkGraphicsDevice
{
    private const int UniformPageBytes = 65_536;
    private const int UniformAlignment = 256;
    private readonly object _uniformPagesGate = new();
    private readonly List<UniformPage> _uniformPages = [];

    internal int UniformPageCountForTesting
    {
        get { lock (_uniformPagesGate) { return _uniformPages.Count; } }
    }

    private UniformAllocation AllocateUniform(nuint bytes)
    {
        int count = checked(((int)bytes + UniformAlignment - 1) / UniformAlignment);
        lock (_uniformPagesGate)
        {
            foreach (UniformPage page in _uniformPages)
            {
                int start = page.Find(count);
                if (start >= 0)
                {
                    ClearUniformRange(page.Resource, start, count);
                    var allocation = new UniformAllocation(this, page, start, count);
                    page.Set(start, count, occupied: true);
                    return allocation;
                }
            }
            _uniformPages.EnsureCapacity(checked(_uniformPages.Count + 1));
            var created = new UniformPage();
            var heap = new HeapProperties(UsesGpuUploadHeaps ? HeapType.GpuUpload : HeapType.Upload);
            var description = new ResourceDesc(ResourceDimension.Buffer, 0, UniformPageBytes, 1, 1, 1,
                Format.FormatUnknown, new SampleDesc(1, 0), TextureLayout.LayoutRowMajor, ResourceFlags.None);
            Guid resourceId = ID3D12Resource.Guid;
            ID3D12Resource* resource = null;
            try
            {
                SilkMarshal.ThrowHResult(_device->CreateCommittedResource(
                    &heap, HeapFlags.None, &description, ResourceStates.GenericRead,
                    null, &resourceId, (void**)&resource));
                created.Resource = resource;
                ClearUniformRange(resource, 0, count);
                var allocation = new UniformAllocation(this, created, 0, count);
                created.Set(0, count, occupied: true);
                _uniformPages.Add(created);
                resource = null;
                return allocation;
            }
            finally { Release(ref resource); }
        }
    }

    private static void ClearUniformRange(ID3D12Resource* resource, int start, int count)
    {
        void* mapped = null;
        var read = new global::Silk.NET.Direct3D12.Range(0, 0);
        SilkMarshal.ThrowHResult(resource->Map(0, &read, &mapped));
        try
        {
            new Span<byte>((byte*)mapped + start * UniformAlignment, count * UniformAlignment).Clear();
        }
        finally
        {
            var written = new global::Silk.NET.Direct3D12.Range(
                (nuint)(start * UniformAlignment), (nuint)((start + count) * UniformAlignment));
            resource->Unmap(0, &written);
        }
    }

    private void ReleaseUniform(UniformPage page, int start, int count)
    {
        lock (_uniformPagesGate)
        {
            page.Set(start, count, occupied: false);
            if (page.Used == 0)
            {
                _uniformPages.Remove(page);
                ID3D12Resource* resource = page.Resource;
                page.Resource = null;
                Release(ref resource);
            }
        }
    }

    internal sealed class UniformPage
    {
        private readonly bool[] _occupied = new bool[UniformPageBytes / UniformAlignment];
        internal ID3D12Resource* Resource { get; set; }
        internal int Used { get; private set; }

        internal int Find(int count)
        {
            if (_occupied.Length - Used < count)
            { return -1; }
            int free = 0;
            for (int index = 0; index < _occupied.Length; index++)
            {
                free = _occupied[index] ? 0 : free + 1;
                if (free == count)
                { return index - count + 1; }
            }
            return -1;
        }

        internal void Set(int start, int count, bool occupied)
        {
            for (int index = start; index < start + count; index++)
            {
                if (_occupied[index] == occupied)
                {
                    throw new InvalidOperationException("Uniform allocation ownership is inconsistent.");
                }
            }
            Array.Fill(_occupied, occupied, start, count);
            Used += occupied ? count : -count;
        }
    }

    internal sealed class UniformAllocation(
        D3D12SilkGraphicsDevice owner, UniformPage page, int start, int count) : IDisposable
    {
        private D3D12SilkGraphicsDevice? _owner = owner;
        internal ID3D12Resource* Resource => page.Resource;
        internal nuint Offset => (nuint)(start * UniformAlignment);

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ReleaseUniform(page, start, count);
    }
}
