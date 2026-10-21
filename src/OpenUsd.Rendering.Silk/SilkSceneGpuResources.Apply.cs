// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

public sealed partial class SilkSceneGpuResources
{
    private PreparedMeshUpdate? _preparing;

    internal SilkMeshGpuResource GetPreparedMesh(ulong id) =>
        _preparing?.GetMesh(id) ?? _meshes[id];

    internal ISilkGraphicsBuffer GetPreparedVertices(SilkMeshGpuGeometryResource geometry) =>
        _preparing?.GetVertices(geometry) ?? geometry.VertexBuffer;

    internal PreparedMeshUpdate? Prepare(
        SilkSceneState scene, SilkSceneDelta delta, bool prepareMaterialTextures = true,
        bool prepareUniformCapacity = false, bool reloadDependencies = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        if (_preparing is not null)
        {
            throw new InvalidOperationException("A GPU scene update is already being prepared.");
        }
        if (delta.MeshUpserts == 0 && delta.MeshRemovals == 0 && delta.MaterialChanges == 0 &&
            !(prepareMaterialTextures && RequiresTexturePreparation && HasChangedMaterialTextureDependencies()) &&
            !(reloadDependencies && RequiresEnvironmentPreparation(scene)) &&
            !(RequiresSurfacePreparation && _surfaceLinkRevision != scene.LightLinks.Revision))
        {
            return null;
        }
        var prepared = new PreparedMeshUpdate(this);
        _preparing = prepared;
        try
        {
            prepared.Prepare(scene, delta, prepareMaterialTextures, prepareUniformCapacity, reloadDependencies);
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
        private readonly List<(SilkMeshGpuResource Resource, SilkMeshData Mesh, SilkMeshData Previous)> _metadata = [];
        private readonly Dictionary<ulong, SilkMeshGpuResource> _retired = [];
        private readonly Dictionary<DisplacedPrimKey, DisplacementVerdict?> _verdicts = [];
        private readonly Dictionary<SilkMeshGpuDeformation, SilkMeshGpuDeformation.PreparedPose> _poses = [];
        private readonly Dictionary<SilkMeshGpuGeometryResource, SilkMeshGpuGeometryResource.PreparedDeformedVertices>
            _vertices = [];
        private ulong _dispatchGeneration;
        private readonly Dictionary<string, RenderDiagnostic> _diagnostics;
        private readonly DisplacementImageCheckpoint _images;
        private readonly bool _deformationDisabled;
        private readonly ulong _revision;
        private bool _activated;
        private PreparedTextureUpdate? _textures;
        private PreparedEnvironmentUpdate? _environment;
        private PreparedSurfaceUpdate? _surfaces;
        private bool _committed;
        private bool _disposed;

        internal PreparedMeshUpdate(SilkSceneGpuResources owner)
        {
            _owner = owner;
            _diagnostics = new(owner._diagnostics, StringComparer.Ordinal);
            _deformationDisabled = owner._deformationDisabled;
            _revision = owner.Revision;
            _images = new(owner);
        }

        internal void Prepare(
            SilkSceneState scene, SilkSceneDelta delta, bool prepareMaterialTextures, bool prepareUniformCapacity,
            bool reloadDependencies)
        {
            if (reloadDependencies && _owner.RequiresEnvironmentPreparation(scene))
            {
                _environment = new PreparedEnvironmentUpdate(_owner);
                _environment.Prepare(scene);
            }
            if (prepareMaterialTextures && _owner.RequiresTexturePreparation)
            {
                _textures = new PreparedTextureUpdate(_owner);
                _textures.Prepare(scene, delta);
            }
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
                    _metadata.Add((existing, mesh, existing.Mesh));
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
            if (_owner.RequiresSurfacePreparation)
            {
                _surfaces = new PreparedSurfaceUpdate(_owner);
                _surfaces.Prepare(scene, delta, prepareUniformCapacity);
            }
            if (prepareUniformCapacity && SilkMeshGpuGeometryResource.UsesInstanceAdmission(_owner._device))
            {
                PrepareDeformationDispatch();
            }
        }

        private void PrepareDeformationDispatch()
        {
            var geometries = new HashSet<SilkMeshGpuGeometryResource>();
            foreach (SilkMeshGpuResource resource in _replacements.Values)
            {
                if (resource.Geometry.Deformation is { } deformation &&
                    (deformation.NeedsDispatch || _poses.ContainsKey(deformation)))
                {
                    geometries.Add(resource.Geometry);
                }
            }
            if (geometries.Count == 0)
            {
                return;
            }
            _dispatchGeneration = SilkDeviceGeneration.Read(_owner._device);
            _vertices.EnsureCapacity(geometries.Count);
            using ISilkGraphicsCommandList commands = _owner._device.CreateCommandList();
            foreach (SilkMeshGpuGeometryResource geometry in geometries)
            {
                var vertices = new SilkMeshGpuGeometryResource.PreparedDeformedVertices(geometry, _owner._device);
                _vertices.Add(geometry, vertices);
                if (_poses.TryGetValue(geometry.Deformation!, out SilkMeshGpuDeformation.PreparedPose? pose))
                {
                    pose.Record(commands, vertices.Buffer);
                }
                else
                {
                    geometry.Deformation!.Record(commands, vertices.Buffer);
                }
            }
            using ISilkGraphicsSubmission submission = _owner._device.Submit(commands);
            submission.Wait();
            _owner._deformationDispatches += checked((ulong)geometries.Count);
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

        internal void Activate()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_activated)
            {
                return;
            }
            foreach (SilkMeshGpuDeformation.PreparedPose pose in _poses.Values)
            {
                pose.Activate();
            }
            foreach (SilkMeshGpuGeometryResource.PreparedDeformedVertices vertices in _vertices.Values)
            {
                vertices.Activate(_dispatchGeneration);
            }
            foreach (ulong id in _retired.Keys)
            {
                _owner._meshes.Remove(id);
            }
            foreach ((ulong id, SilkMeshGpuResource resource) in _replacements)
            {
                _owner._meshes[id] = resource;
            }
            foreach ((SilkMeshGpuResource resource, SilkMeshData mesh, _) in _metadata)
            {
                resource.UpdateMesh(mesh);
            }
            _owner.Revision++;
            _activated = true;
            _surfaces?.Activate();
        }

        internal void Commit()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_committed)
            {
                throw new InvalidOperationException("The GPU update was already committed.");
            }
            Activate();
            foreach (SilkMeshGpuDeformation.PreparedPose pose in _poses.Values)
            {
                pose.Commit();
            }
            foreach (SilkMeshGpuGeometryResource.PreparedDeformedVertices vertices in _vertices.Values)
            {
                vertices.Commit(_dispatchGeneration);
            }
            foreach (SilkMeshGpuResource resource in _retired.Values)
            {
                _owner.ForgetDisplacementVerdict(resource.Mesh);
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
            _textures?.Commit();
            _environment?.Commit();
            _surfaces?.Commit();
            _images.Commit();
            _owner._preparing = null;
            _committed = true;
        }

        internal bool TexturesPrepared => _textures is not null;
        internal bool SurfacesPrepared => _surfaces is not null;
        internal SilkMeshGpuResource GetMesh(ulong id) =>
            _replacements.TryGetValue(id, out SilkMeshGpuResource? mesh) ? mesh : _owner._meshes[id];
        internal ISilkGraphicsBuffer GetVertices(SilkMeshGpuGeometryResource geometry) =>
            _vertices.TryGetValue(geometry, out var vertices) ? vertices.Buffer : geometry.VertexBuffer;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _owner._preparing = null;
            List<Exception>? failures = null;
            if (!_committed && _activated)
            {
                foreach (ulong id in _replacements.Keys)
                {
                    _owner._meshes.Remove(id);
                }
                foreach ((ulong id, SilkMeshGpuResource resource) in _retired)
                {
                    _owner._meshes[id] = resource;
                }
                foreach ((SilkMeshGpuResource resource, _, SilkMeshData previous) in _metadata)
                {
                    resource.UpdateMesh(previous);
                }
                _owner.Revision = _revision;
            }
            foreach (SilkMeshGpuGeometryResource.PreparedDeformedVertices vertices in _vertices.Values)
            {
                try
                {
                    vertices.Dispose();
                }
                catch (Exception error)
                {
                    (failures ??= []).Add(error);
                }
            }
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
            try
            {
                _textures?.Dispose();
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
            try
            {
                _environment?.Dispose();
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
            try
            {
                _surfaces?.Dispose();
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
            if (!_committed)
            {
                _owner._diagnostics.Clear();
                foreach ((string key, RenderDiagnostic diagnostic) in _diagnostics)
                {
                    _owner._diagnostics[key] = diagnostic;
                }
                _owner._deformationDisabled = _deformationDisabled;
            }
            _images.Dispose();
            _disposed = true;
            if (failures is not null)
            {
                throw new AggregateException("GPU update resources could not all be released.", failures);
            }
        }
    }
}
