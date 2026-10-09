// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

public sealed partial class SilkSceneGpuResources
{
    private PreparedSurfaceUpdate? _surfacePreparation;

    private bool RequiresSurfacePreparation =>
        RequiresTexturePreparation ||
        _device is ISilkBufferAdmissionDevice { GpuBufferBudget: not null };

    private void ReleaseSurfaceBuffer(ISilkGraphicsBuffer? buffer)
    {
        if (buffer is null)
        {
            return;
        }
        if (_surfacePreparation is { } preparing)
        {
            preparing.Retire(buffer);
        }
        else
        {
            buffer.Dispose();
        }
    }

    private sealed class PreparedSurfaceUpdate
    {
        private readonly SilkSceneGpuResources _owner;
        private readonly Dictionary<SurfaceBufferKey, SurfaceBuffer> _previous;
        private readonly HashSet<SilkLightLinkMasks> _masks;
        private readonly ulong _linkRevision;
        private readonly HashSet<ISilkGraphicsBuffer> _created = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<ISilkGraphicsBuffer> _retired = new(ReferenceEqualityComparer.Instance);
        private ISilkGraphicsBuffer? _createdFrame;
        private bool _committed;
        private bool _disposed;

        internal PreparedSurfaceUpdate(SilkSceneGpuResources owner)
        {
            if (owner._surfacePreparation is not null)
            {
                throw new InvalidOperationException("Surface constants are already being prepared.");
            }
            _owner = owner;
            _previous = new(owner._surfaceBuffers);
            _masks = new(owner._liveLinkMasks);
            _linkRevision = owner._surfaceLinkRevision;
            _retired.EnsureCapacity(_previous.Count);
            owner._surfacePreparation = this;
        }

        internal void Retire(ISilkGraphicsBuffer buffer) => _retired.Add(buffer);
        internal void Track(ISilkGraphicsBuffer buffer) => _created.Add(buffer);

        internal void Prepare(SilkSceneState scene, SilkSceneDelta delta)
        {
            foreach (string path in delta.ChangedMaterialPaths.Span)
            {
                _owner.RemoveSurfaceBuffers(path);
            }
            _owner.ObserveLightLinkRevision(scene);
            if (scene.Meshes.Count == 0)
            {
                return;
            }
            if (_owner._frameBuffer is null)
            {
                _createdFrame = _owner.CreateTrackedBuffer(
                    SilkFrameUniformWriter.ByteSize, SilkBufferUsage.Storage | SilkBufferUsage.Upload);
            }
            foreach (SilkMeshData mesh in scene.Meshes.Values)
            {
                _owner.RequireSurfaceBuffer(scene, mesh, RenderHeadlight.Deterministic);
            }
        }

        internal void Commit()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_committed)
            {
                throw new InvalidOperationException("Surface constants were already committed.");
            }
            if (_createdFrame is not null)
            {
                _owner._frameBuffer = _createdFrame;
                _createdFrame = null;
            }
            _owner._surfacePreparation = null;
            _committed = true;
        }

        internal void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _owner._surfacePreparation = null;
            List<Exception>? errors = null;
            foreach (ISilkGraphicsBuffer buffer in _committed ? _retired : _created)
            {
                try
                {
                    buffer.Dispose();
                }
                catch (Exception error)
                {
                    (errors ??= []).Add(error);
                }
            }
            try
            {
                _createdFrame?.Dispose();
            }
            catch (Exception error)
            {
                (errors ??= []).Add(error);
            }
            if (!_committed)
            {
                _owner._surfaceBuffers.Clear();
                foreach (var pair in _previous)
                {
                    _owner._surfaceBuffers.Add(pair.Key, pair.Value);
                }
                _owner._liveLinkMasks.Clear();
                _owner._liveLinkMasks.UnionWith(_masks);
                _owner._surfaceLinkRevision = _linkRevision;
            }
            if (errors is not null)
            {
                throw new AggregateException("Prepared surface buffers could not all be released.", errors);
            }
        }
    }
}
