// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

public sealed partial class SilkSceneGpuResources
{
    private PreparedMeshUpdate? _preparing;

    internal PreparedMeshUpdate? Prepare(SilkSceneState scene, SilkSceneDelta delta)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        if (_preparing is not null)
        {
            throw new InvalidOperationException("A GPU scene update is already being prepared.");
        }
        if (delta.MeshUpserts == 0 && delta.MeshRemovals == 0 && delta.MaterialChanges == 0)
        {
            return null;
        }
        var prepared = new PreparedMeshUpdate(this);
        _preparing = prepared;
        try
        {
            prepared.Prepare(scene, delta);
            return prepared;
        }
        catch (Exception preparationFailure)
        {
            try
            {
                prepared.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("GPU preparation failed and its staged resources could not be released.",
                    preparationFailure, cleanupFailure);
            }
            throw;
        }
    }

    internal sealed class PreparedMeshUpdate : IDisposable
    {
        private readonly SilkSceneGpuResources _owner;
        private readonly Dictionary<ulong, SilkMeshGpuResource> _replacements = [];
        private readonly List<(SilkMeshGpuResource Resource, SilkMeshData Mesh)> _metadata = [];
        private readonly Dictionary<ulong, SilkMeshGpuResource> _retired = [];
        private readonly Dictionary<DisplacedPrimKey, DisplacementVerdict?> _verdicts = [];
        private readonly Dictionary<SilkMeshGpuDeformation, SilkMeshGpuDeformation.PreparedPose> _poses = [];
        private readonly Dictionary<string, RenderDiagnostic> _diagnostics;
        private readonly Dictionary<ulong, (DisplacementCacheEntry Entry, ulong Stamp)> _images;
        private readonly ulong _imageBytes;
        private readonly ulong _imageClock;
        private readonly bool _deformationDisabled;
        private bool _committed;
        private bool _disposed;

        internal PreparedMeshUpdate(SilkSceneGpuResources owner)
        {
            _owner = owner;
            _diagnostics = new(owner._diagnostics, StringComparer.Ordinal);
            _images = owner._displacementImages.ToDictionary(
                static pair => pair.Key, static pair => (pair.Value, pair.Value.LastUsedStamp));
            _imageBytes = owner._displacementImageBytes;
            _imageClock = owner._displacementUseClock;
            _deformationDisabled = owner._deformationDisabled;
        }

        internal void Prepare(SilkSceneState scene, SilkSceneDelta delta)
        {
            var changedMaterials = new HashSet<string>(delta.ChangedMaterialPaths.ToArray(), StringComparer.Ordinal);
            var removed = new HashSet<ulong>(delta.RemovedMeshIds.ToArray());
            var desired = new Dictionary<ulong, SilkMeshData>();
            foreach (ulong id in delta.UpsertedMeshIds.Span)
            {
                if (scene.Meshes.TryGetValue(id, out SilkMeshData? mesh))
                {
                    desired[id] = mesh;
                }
                else if (!removed.Contains(id))
                {
                    throw new InvalidDataException($"Scene delta references missing mesh {id}.");
                }
            }
            foreach (ulong id in removed)
            {
                if (_owner._meshes.TryGetValue(id, out SilkMeshGpuResource? previous))
                {
                    _retired[id] = previous;
                }
            }
            if (changedMaterials.Count != 0)
            {
                foreach ((ulong id, SilkMeshGpuResource resource) in _owner._meshes)
                {
                    if (changedMaterials.Contains(resource.Mesh.MaterialPath) &&
                        scene.Meshes.TryGetValue(id, out SilkMeshData? mesh))
                    {
                        desired[id] = mesh;
                    }
                }
            }
            _replacements.EnsureCapacity(desired.Count);
            _metadata.EnsureCapacity(desired.Count);
            _retired.EnsureCapacity(checked(_retired.Count + desired.Count));
            int added = 0;
            foreach (ulong id in desired.Keys)
            {
                if (!_owner._meshes.ContainsKey(id))
                {
                    added++;
                }
            }
            _owner._meshes.EnsureCapacity(checked(_owner._meshes.Count + added));
            foreach ((ulong id, SilkMeshData mesh) in desired)
            {
                if (_owner._meshes.TryGetValue(id, out SilkMeshGpuResource? existing) &&
                    !removed.Contains(id) && !changedMaterials.Contains(mesh.MaterialPath) &&
                    existing.HasSameGeometry(mesh))
                {
                    _metadata.Add((existing, mesh));
                    continue;
                }
                SilkMeshGpuResource replacement = _owner.CreateMesh(scene, mesh);
                _replacements.Add(id, replacement);
                if (existing is not null)
                {
                    _retired[id] = existing;
                }
            }
            int addedVerdicts = 0;
            foreach ((DisplacedPrimKey key, DisplacementVerdict? verdict) in _verdicts)
            {
                if (verdict.HasValue && !_owner._displacementVerdicts.ContainsKey(key))
                {
                    addedVerdicts++;
                }
            }
            _owner._displacementVerdicts.EnsureCapacity(
                checked(_owner._displacementVerdicts.Count + addedVerdicts));
        }

        internal void SetVerdict(DisplacedPrimKey key, DisplacementVerdict? verdict) => _verdicts[key] = verdict;

        internal void PreparePose(SilkMeshGpuDeformation deformation, SilkDeformationGpuPayload payload)
        {
            if (_poses.TryGetValue(deformation, out SilkMeshGpuDeformation.PreparedPose? existing))
            {
                if (existing.Identity != payload.Identity)
                {
                    throw new InvalidDataException("Shared GPU geometry cannot carry two poses in one page.");
                }
                return;
            }
            _poses.EnsureCapacity(checked(_poses.Count + 1));
            if (deformation.PreparePose(_owner._device, payload) is { } pose)
            {
                _poses.Add(deformation, pose);
            }
        }

        internal void Commit()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_committed)
            {
                throw new InvalidOperationException("The GPU update was already committed.");
            }
            foreach (SilkMeshGpuDeformation.PreparedPose pose in _poses.Values)
            {
                pose.Commit();
            }
            foreach ((ulong id, SilkMeshGpuResource resource) in _retired)
            {
                _owner._meshes.Remove(id);
                _owner.ForgetDisplacementVerdict(resource.Mesh);
            }
            foreach ((ulong id, SilkMeshGpuResource resource) in _replacements)
            {
                _owner._meshes[id] = resource;
            }
            foreach ((SilkMeshGpuResource resource, SilkMeshData mesh) in _metadata)
            {
                resource.UpdateMesh(mesh);
            }
            foreach ((DisplacedPrimKey key, DisplacementVerdict? verdict) in _verdicts)
            {
                if (verdict is { } value)
                {
                    if (_owner._displacementVerdicts.TryGetValue(key, out DisplacementVerdict previous) &&
                        previous == value)
                    {
                        continue;
                    }
                    _owner._displacementVerdicts[key] = value;
                }
                else if (!_owner._displacementVerdicts.Remove(key))
                {
                    continue;
                }
                _owner._displacementVerdictRevision++;
            }
            _owner.Revision++;
            _owner._preparing = null;
            _committed = true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _owner._preparing = null;
            List<Exception>? failures = null;
            foreach (SilkMeshGpuResource resource in (_committed ? _retired : _replacements).Values)
            {
                try
                {
                    _owner.DisposeMesh(resource);
                }
                catch (Exception error)
                {
                    (failures ??= []).Add(error);
                }
            }
            foreach (SilkMeshGpuDeformation.PreparedPose pose in _poses.Values)
            {
                pose.Dispose();
            }
            if (!_committed)
            {
                _owner._diagnostics.Clear();
                foreach ((string key, RenderDiagnostic diagnostic) in _diagnostics)
                {
                    _owner._diagnostics[key] = diagnostic;
                }
                _owner._displacementImages.Clear();
                foreach ((ulong key, (DisplacementCacheEntry entry, ulong stamp)) in _images)
                {
                    entry.LastUsedStamp = stamp;
                    _owner._displacementImages[key] = entry;
                }
                _owner._displacementImageBytes = _imageBytes;
                _owner._displacementUseClock = _imageClock;
                _owner._deformationDisabled = _deformationDisabled;
            }
            _disposed = true;
            if (failures is not null)
            {
                throw new AggregateException("GPU update resources could not all be released.", failures);
            }
        }
    }
}
