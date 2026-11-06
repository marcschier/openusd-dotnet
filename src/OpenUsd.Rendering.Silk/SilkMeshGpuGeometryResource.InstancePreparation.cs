// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

internal sealed partial class SilkMeshGpuGeometryResource
{
    private PreparedInstanceWrites? _instancePreparation;

    internal PreparedInstanceWrites PrepareInstanceWrites(ISilkGraphicsDevice device)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_instancePreparation is not null)
        {
            throw new InvalidOperationException("Instance writes are already being prepared.");
        }
        return _instancePreparation = new PreparedInstanceWrites(this, device);
    }

    internal sealed class PreparedInstanceWrites : IDisposable
    {
        private readonly SilkMeshGpuGeometryResource _owner;
        private readonly ISilkGraphicsDevice _device;
        private readonly ulong _generation;
        private readonly int _count;
        private readonly Dictionary<int, InstanceSlot?> _slots = [];
        private readonly List<ISilkGraphicsBuffer> _created = [];
        private readonly List<ISilkGraphicsBuffer> _retired = [];
        private bool _committed;
        private bool _disposed;

        internal PreparedInstanceWrites(SilkMeshGpuGeometryResource owner, ISilkGraphicsDevice device)
        {
            _owner = owner;
            _device = device;
            _generation = SilkDeviceGeneration.Read(device);
            _count = owner._instanceSlots.Count;
        }

        internal void Track(int index)
        {
            if (_slots.ContainsKey(index))
            {
                return;
            }
            InstanceSlot? previous = index < _owner._instanceSlots.Count ? _owner._instanceSlots[index] : null;
            _slots.Add(index, previous is null ? null : new InstanceSlot
            {
                Buffer = previous.Buffer,
                SpareBuffer = previous.SpareBuffer,
                Bytes = previous.Bytes,
                Staging = previous.Staging,
                Meshes = previous.Meshes,
                MeshesStaging = previous.MeshesStaging,
                UploadedCount = previous.UploadedCount,
                FrameRevision = previous.FrameRevision,
                Capacity = previous.Capacity
            });
        }

        internal bool ProtectsBuffer(int slot, ISilkGraphicsBuffer? buffer) =>
            buffer is not null && ReferenceEquals(_slots[slot]?.Buffer, buffer);

        internal bool ProtectsBytes(int slot, byte[] bytes) =>
            ReferenceEquals(_slots[slot]?.Bytes, bytes);

        internal bool ProtectsMeshes(int slot, SilkMeshData?[] meshes) =>
            ReferenceEquals(_slots[slot]?.Meshes, meshes);

        internal void ReservePublication()
        {
            _created.EnsureCapacity(checked(_created.Count + 1));
            _retired.EnsureCapacity(checked(_retired.Count + 1));
        }

        internal void Created(ISilkGraphicsBuffer buffer) => _created.Add(buffer);

        internal void Retire(ISilkGraphicsBuffer? buffer)
        {
            if (buffer is not null)
            {
                _retired.Add(buffer);
            }
        }

        internal void Commit()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _committed = true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _owner._instancePreparation = null;
            bool rollback = !_committed && SilkDeviceGeneration.Read(_device) == _generation;
            if (rollback)
            {
                foreach ((int index, InstanceSlot? slot) in _slots)
                {
                    if (slot is not null)
                    {
                        _owner._instanceSlots[index] = slot;
                    }
                }
                _owner._instanceSlots.RemoveRange(_count, _owner._instanceSlots.Count - _count);
            }
            List<Exception>? errors = null;
            foreach (ISilkGraphicsBuffer buffer in rollback ? _created : _retired)
            {
                SilkSceneGpuResources.ReleaseUnpublishedUniform(buffer, null, null, ref errors);
            }
            if (errors is not null)
            {
                throw new AggregateException("Prepared instance writes could not all release their buffers.", errors);
            }
        }
    }
}
