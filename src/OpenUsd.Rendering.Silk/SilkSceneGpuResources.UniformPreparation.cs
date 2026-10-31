// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

public sealed partial class SilkSceneGpuResources
{
    private PreparedFrameUniforms? _uniformPreparation;

    internal PreparedFrameUniforms? PrepareFrameUniforms()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!SilkMeshGpuGeometryResource.UsesInstanceAdmission(_device))
        {
            return null;
        }
        if (_uniformPreparation is not null)
        {
            throw new InvalidOperationException("Frame uniforms are already being prepared.");
        }
        return _uniformPreparation = new PreparedFrameUniforms(this);
    }

    internal sealed class PreparedFrameUniforms : IDisposable
    {
        private readonly SilkSceneGpuResources _owner;
        private readonly ulong _generation;
        private readonly Dictionary<SilkMeshGpuResource, SilkMeshGpuResource.UniformCheckpoint> _meshes = [];
        private readonly Dictionary<SilkMeshGpuGeometryResource, SilkMeshGpuGeometryResource.PreparedInstanceWrites>
            _instances = [];
        private FrameUniformCheckpoint? _frame;
        private bool _committed;
        private bool _disposed;

        internal PreparedFrameUniforms(SilkSceneGpuResources owner)
        {
            _owner = owner;
            _generation = SilkDeviceGeneration.Read(owner._device);
            BeforeMeshUpdate = TrackMesh;
        }

        internal Func<SilkMeshGpuResource, ISilkGraphicsBuffer> BeforeMeshUpdate { get; }

        private ISilkGraphicsBuffer TrackMesh(SilkMeshGpuResource mesh)
        {
            if (!_meshes.TryGetValue(mesh, out SilkMeshGpuResource.UniformCheckpoint? checkpoint))
            {
                checkpoint = new SilkMeshGpuResource.UniformCheckpoint(mesh);
                _meshes.Add(mesh, checkpoint);
            }
            return checkpoint.PublishedBuffer;
        }

        internal void TrackFrame() => _frame ??= new FrameUniformCheckpoint(_owner);

        internal bool ProtectsFrameBuffer(ISilkGraphicsBuffer? buffer) =>
            buffer is not null && ReferenceEquals(_frame?.PublishedBuffer, buffer);

        internal void TrackInstances(SilkMeshGpuGeometryResource geometry)
        {
            if (!_instances.ContainsKey(geometry))
            {
                _instances.EnsureCapacity(checked(_instances.Count + 1));
                _instances.Add(geometry, geometry.PrepareInstanceWrites(_owner._device));
            }
        }

        internal void Commit()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (SilkMeshGpuGeometryResource.PreparedInstanceWrites instances in _instances.Values)
            {
                instances.Commit();
            }
            _committed = true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _owner._uniformPreparation = null;
            List<Exception>? errors = null;
            foreach (SilkMeshGpuGeometryResource.PreparedInstanceWrites instances in _instances.Values)
            {
                try
                {
                    instances.Dispose();
                }
                catch (Exception error)
                {
                    (errors ??= []).Add(error);
                }
            }
            if (!_committed && SilkDeviceGeneration.Read(_owner._device) == _generation)
            {
                foreach (SilkMeshGpuResource.UniformCheckpoint checkpoint in _meshes.Values)
                {
                    checkpoint.Restore(ref errors);
                }
                _frame?.Restore(ref errors);
            }
            else
            {
                foreach (SilkMeshGpuResource.UniformCheckpoint checkpoint in _meshes.Values)
                {
                    checkpoint.ReleaseRetired(ref errors);
                }
                _frame?.ReleaseRetired(ref errors);
            }
            if (errors is not null)
            {
                throw new AggregateException("Unpublished frame uniform buffers could not all be released.", errors);
            }
        }
    }

    private sealed class FrameUniformCheckpoint
    {
        private readonly SilkSceneGpuResources _owner;
        private readonly ISilkGraphicsBuffer? _buffer;
        private readonly ISilkGraphicsBuffer? _spare;
        private readonly bool _initialized;
        private readonly byte[] _bytes;
        private readonly ulong _frameRevision;
        private readonly ulong _environmentRevision;
        private readonly ulong _ambientRevision;
        private readonly ulong _bindingRevision;
        private readonly ulong _shadowRevision;
        private readonly RenderOutputTransform _outputTransform;
        private readonly float _exposure;

        internal FrameUniformCheckpoint(SilkSceneGpuResources owner)
        {
            _owner = owner;
            _buffer = owner._frameBuffer;
            _spare = owner._frameSpare;
            _initialized = owner._frameBufferInitialized;
            _bytes = [.. owner._frameBytes];
            _frameRevision = owner._frameRevision;
            _environmentRevision = owner._frameEnvironmentRevision;
            _ambientRevision = owner._frameEnvironmentAmbientRevision;
            _bindingRevision = owner._frameEnvironmentBindingRevision;
            _shadowRevision = owner._frameShadowRevision;
            _outputTransform = owner._frameOutputTransform;
            _exposure = owner._frameExposure;
        }

        internal ISilkGraphicsBuffer? PublishedBuffer => _buffer;

        internal void ReleaseRetired(ref List<Exception>? errors)
        {
            ReleaseUnpublishedUniform(_buffer, _owner._frameBuffer, _owner._frameSpare, ref errors);
            ReleaseUnpublishedUniform(_spare, _owner._frameBuffer, _owner._frameSpare, ref errors);
        }

        internal void Restore(ref List<Exception>? errors)
        {
            ISilkGraphicsBuffer? current = _owner._frameBuffer;
            ISilkGraphicsBuffer? spare = _owner._frameSpare;
            _owner._frameBuffer = _buffer;
            _owner._frameSpare = _spare;
            _owner._frameBufferInitialized = _initialized;
            _bytes.CopyTo(_owner._frameBytes, 0);
            _owner._frameRevision = _frameRevision;
            _owner._frameEnvironmentRevision = _environmentRevision;
            _owner._frameEnvironmentAmbientRevision = _ambientRevision;
            _owner._frameEnvironmentBindingRevision = _bindingRevision;
            _owner._frameShadowRevision = _shadowRevision;
            _owner._frameOutputTransform = _outputTransform;
            _owner._frameExposure = _exposure;
            ReleaseUnpublishedUniform(current, _buffer, _spare, ref errors);
            if (!ReferenceEquals(current, spare))
            {
                ReleaseUnpublishedUniform(spare, _buffer, _spare, ref errors);
            }
        }
    }

    internal static void ReleaseUnpublishedUniform(
        ISilkGraphicsBuffer? buffer, ISilkGraphicsBuffer? previous, ISilkGraphicsBuffer? previousSpare,
        ref List<Exception>? errors)
    {
        if (ReferenceEquals(buffer, previous) || ReferenceEquals(buffer, previousSpare))
        {
            return;
        }
        try
        {
            buffer?.Dispose();
        }
        catch (Exception error)
        {
            (errors ??= []).Add(error);
        }
    }
}
