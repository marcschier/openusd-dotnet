// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

public sealed partial class SilkSceneGpuResources
{
    private PreparedTextureUpdate? _texturePreparation;

    private bool RequiresTexturePreparation =>
        _cpuTextureBudget is not null ||
        _device is ISilkTextureAdmissionDevice { GpuTextureBudget: not null } ||
        _device is ISilkStagingAdmissionDevice { GpuStagingBudget: not null };

    private bool HasChangedMaterialTextureDependencies() =>
        _textures.Values.Any(static entry => DependenciesChanged(entry.Dependencies));

    private sealed class PreparedTextureUpdate : IDisposable
    {
        private readonly SilkSceneGpuResources _owner;
        private readonly Dictionary<TextureCacheKey, TextureCacheEntry> _textures;
        private readonly Dictionary<TextureCacheKey, TextureCacheEntry> _failed;
        private readonly Dictionary<string, TextureCacheEntry> _volumes;
        private readonly Dictionary<TextureCacheEntry, (ulong Stamp, bool Uploaded)> _state;
        private readonly Dictionary<string, RenderDiagnostic> _diagnostics;
        private readonly Dictionary<SilkSamplerDescriptor, ISilkGraphicsSampler> _samplers;
        private readonly HashSet<TextureCacheEntry> _created = [];
        private readonly HashSet<TextureCacheEntry> _retired = [];
        private readonly ulong _clock;
        private bool _committed;
        private bool _disposed;

        internal PreparedTextureUpdate(SilkSceneGpuResources owner)
        {
            if (owner._texturePreparation is not null)
            {
                throw new InvalidOperationException("A texture update is already being prepared.");
            }
            _owner = owner;
            _textures = new(owner._textures);
            _failed = new(owner._failedTextures);
            _volumes = new(owner._volumeTextures, StringComparer.Ordinal);
            _diagnostics = new(owner._diagnostics, StringComparer.Ordinal);
            _samplers = new(owner._samplers);
            _clock = owner._textureUseClock;
            _state = new();
            foreach (TextureCacheEntry entry in _textures.Values.Concat(_failed.Values).Concat(_volumes.Values))
            {
                _state.TryAdd(entry, (entry.LastUsedStamp, entry.Uploaded));
            }
            owner._texturePreparation = this;
        }

        internal void Track(TextureCacheEntry entry) => _created.Add(entry);
        internal void Retire(TextureCacheEntry entry) => _retired.Add(entry);

        internal void Prepare(SilkSceneState scene, SilkSceneDelta delta)
        {
            if (delta.MaterialChanges != 0)
            {
                _owner.RemoveMaterialDiagnostics();
            }
            _owner.RemoveChangedMaterialTextureCacheEntries(delta.ChangedMaterialPaths.Span);
            var materials = new HashSet<string>(StringComparer.Ordinal);
            var uploads = new HashSet<TextureCacheEntry>();
            var volumes = new HashSet<TextureCacheEntry>();
            foreach (SilkMeshData mesh in scene.Meshes.Values)
            {
                if (!materials.Add(mesh.MaterialPath) ||
                    !scene.Materials.TryGetValue(mesh.MaterialPath, out SilkMaterialData? material) ||
                    !material.IsSupported)
                {
                    continue;
                }
                foreach (SilkMaterialTexture texture in material.Textures)
                {
                    if (texture.Parameter == SilkMaterialParameter.Displacement)
                    {
                        continue;
                    }
                    if (texture.Parameter == SilkMaterialParameter.VolumeDensity)
                    {
                        if (_owner._device is ISilkVolumeTextureGraphicsDevice)
                        {
                            TextureCacheEntry entry = _owner.RequireVolumeTexture(texture);
                            if (!entry.Uploaded)
                            {
                                volumes.Add(entry);
                            }
                        }
                        continue;
                    }
                    TextureCacheEntry ordinary = _owner.RequireTexture(material.Path, texture);
                    _owner.RequireSampler(texture, ordinary.Texture.Format,
                        ordinary.IsUdim, ordinary.Texture.MipLevelCount);
                    if (!ordinary.Uploaded)
                    {
                        uploads.Add(ordinary);
                    }
                }
            }
            if (uploads.Count == 0 && volumes.Count == 0)
            {
                return;
            }
            using ISilkGraphicsCommandList commands = _owner._device.CreateCommandList();
            foreach (TextureCacheEntry entry in uploads)
            {
                commands.UploadTexture(entry.Texture, entry.Pixels);
                _owner._textureUploadBytes += checked((ulong)entry.Pixels.Length);
            }
            if (volumes.Count != 0)
            {
                if (commands is not ISilkVolumeTextureCommandList volumeCommands)
                {
                    throw new InvalidOperationException(
                        "The device cannot record its prepared volume texture uploads.");
                }
                foreach (TextureCacheEntry entry in volumes)
                {
                    volumeCommands.UploadTexture3D(entry.Texture, entry.Pixels);
                    _owner._textureUploadBytes += checked((ulong)entry.Pixels.Length);
                }
            }
            using ISilkGraphicsSubmission submission = _owner._device.Submit(commands);
            submission.Wait();
            foreach (TextureCacheEntry entry in uploads.Concat(volumes))
            {
                entry.Uploaded = true;
            }
        }

        internal void Commit()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _committed = true;
            _owner._texturePreparation = null;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _owner._texturePreparation = null;
            List<Exception>? failures = null;
            foreach (TextureCacheEntry entry in _committed ? _retired : _created)
            {
                try
                {
                    _owner.DisposeEntry(entry);
                }
                catch (Exception error)
                {
                    (failures ??= []).Add(error);
                }
            }
            if (!_committed)
            {
                foreach ((SilkSamplerDescriptor descriptor, ISilkGraphicsSampler sampler) in _owner._samplers)
                {
                    if (!_samplers.ContainsKey(descriptor))
                    {
                        try
                        {
                            sampler.Dispose();
                        }
                        catch (Exception error)
                        {
                            (failures ??= []).Add(error);
                        }
                    }
                }
                Restore(_owner._samplers, _samplers);
                Restore(_owner._textures, _textures);
                Restore(_owner._failedTextures, _failed);
                Restore(_owner._volumeTextures, _volumes);
                Restore(_owner._diagnostics, _diagnostics);
                foreach ((TextureCacheEntry entry, (ulong stamp, bool uploaded)) in _state)
                {
                    entry.LastUsedStamp = stamp;
                    entry.Uploaded = uploaded;
                }
                _owner._textureUseClock = _clock;
            }
            if (failures is not null)
            {
                throw new AggregateException("Prepared texture ownership could not be completely released.", failures);
            }
        }

        private static void Restore<TKey, TValue>(
            Dictionary<TKey, TValue> target, Dictionary<TKey, TValue> source) where TKey : notnull
        {
            target.Clear();
            foreach ((TKey key, TValue value) in source)
            {
                target.Add(key, value);
            }
        }
    }
}
