// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

public sealed partial class SilkSceneGpuResources
{
    private PreparedEnvironmentUpdate? _environmentPreparation;

    private void SetEnvironmentPayload(SilkEnvironmentMaps? maps)
    {
        if (ReferenceEquals(maps, _environmentPayload))
        {
            return;
        }
        IDisposable? ownership = maps?.AcquirePixelOwnership();
        IDisposable? previous = _environmentPayloadOwnership;
        _environmentPayload = maps;
        _environmentPayloadOwnership = ownership;
        previous?.Dispose();
    }

    private static SilkEnvironmentData[] PublishedEnvironments(SilkSceneState scene) =>
        [.. scene.Environments.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => pair.Value)];

    private bool RequiresEnvironmentPreparation(SilkSceneState scene) =>
        RequiresTexturePreparation &&
        (scene.Meshes.Count != 0 || scene.Environments.Count != 0 ||
         _environmentLightingRevision != ulong.MaxValue) &&
        (_environmentLightingRevision != scene.EnvironmentRevision ||
         _environmentLightingDeviceGeneration != SilkDeviceGeneration.Read(_device) ||
         _environmentPerDomeGroups != scene.LightLinks.HasDomeLinks ||
         !string.Equals(_environmentAssetRevision,
             ComposeEnvironmentAssetRevision(PublishedEnvironments(scene)), StringComparison.Ordinal));

    private void ReleaseEnvironmentResource(IDisposable? resource)
    {
        if (resource is null)
        {
            return;
        }
        if (_environmentPreparation is { } preparing)
        {
            preparing.Retire(resource);
        }
        else
        {
            resource.Dispose();
        }
    }

    private sealed class PreparedEnvironmentUpdate : IDisposable
    {
        private readonly SilkSceneGpuResources _owner;
        private readonly EnvironmentPublication _previous;
        private readonly HashSet<IDisposable> _retired = new(ReferenceEqualityComparer.Instance);
        private readonly SilkEnvironmentLightingCache.Checkpoint _cache;
        private bool _committed;
        private bool _disposed;

        internal PreparedEnvironmentUpdate(SilkSceneGpuResources owner)
        {
            if (owner._environmentPreparation is not null)
            {
                throw new InvalidOperationException("An environment update is already being prepared.");
            }
            _owner = owner;
            _previous = new(owner);
            _retired.EnsureCapacity(6);
            _cache = owner._environmentLighting.Capture();
            try
            {
                _previous.RetainPayload();
            }
            catch
            {
                _cache.Dispose();
                throw;
            }
            owner._environmentPreparation = this;
        }

        internal void Retire(IDisposable resource) => _retired.Add(resource);

        internal void Prepare(SilkSceneState scene)
        {
            _owner.PrepareEnvironmentLighting(scene);
            if (scene.Meshes.Count != 0)
            {
                if (_owner._environmentIrradianceTexture is null ||
                    _owner._environmentSpecularTexture is null ||
                    _owner._environmentBrdfTexture is null)
                {
                    _owner.RequireEnvironmentStandIn();
                }
                _owner.RequireEnvironmentSampler();
                _owner.RequireEnvironmentBrdfSampler();
            }
            if (_owner._environmentMapsUploaded || _owner._environmentPayload is null)
            {
                return;
            }
            using ISilkGraphicsCommandList commands = _owner._device.CreateCommandList();
            _owner.UploadEnvironment(commands);
            using ISilkGraphicsSubmission submission = _owner._device.Submit(commands);
            submission.Wait();
            _owner.CommitPendingUploads();
        }

        internal void Commit()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _owner._environmentPreparation = null;
            _committed = true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _owner._environmentPreparation = null;
            List<Exception>? failures = null;
            if (_committed)
            {
                foreach (IDisposable resource in _retired)
                {
                    Release(resource, ref failures);
                }
            }
            else
            {
                _previous.ReleaseCandidates(_owner, ref failures);
                _previous.Restore(_owner);
                _cache.Restore();
                if (_previous.DeviceChanged(_owner))
                {
                    _owner.ReleaseEnvironmentDeviceResources();
                    _owner._environmentLightingRevision = ulong.MaxValue;
                }
            }
            _previous.ReleasePayload();
            _cache.Dispose();
            if (failures is not null)
            {
                throw new AggregateException("Prepared environment resources could not all be released.", failures);
            }
        }

        internal static void Release(IDisposable? resource, ref List<Exception>? failures)
        {
            try
            {
                resource?.Dispose();
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
        }
    }

    private sealed class EnvironmentPublication(SilkSceneGpuResources owner)
    {
        private readonly ISilkGraphicsTexture? _irradiance = owner._environmentIrradianceTexture;
        private readonly ISilkGraphicsTexture? _specular = owner._environmentSpecularTexture;
        private readonly ISilkGraphicsTexture? _brdf = owner._environmentBrdfTexture;
        private readonly ISilkGraphicsTexture? _standIn = owner._environmentStandIn;
        private readonly ISilkGraphicsSampler? _sampler = owner._environmentSampler;
        private readonly ISilkGraphicsSampler? _brdfSampler = owner._environmentBrdfSampler;
        private readonly SilkEnvironmentMaps? _payload = owner._environmentPayload;
        private IDisposable? _payloadOwnership;
        private readonly SilkEnvironmentFrameBinding _binding = owner._environmentBinding;
        private readonly ulong _bindingRevision = owner._environmentBindingRevision;
        private readonly string? _uploadedIdentity = owner._environmentUploadedIdentity;
        private readonly string _assetRevision = owner._environmentAssetRevision;
        private readonly bool _mapsUploaded = owner._environmentMapsUploaded;
        private readonly bool _brdfUploaded = owner._environmentBrdfUploaded;
        private readonly bool _mapsPending = owner._environmentMapsUploadPending;
        private readonly bool _brdfPending = owner._environmentBrdfUploadPending;
        private readonly ulong _pendingBytes = owner._environmentPendingUploadBytes;
        private readonly bool _authoredLighting = owner._environmentAuthoredSceneLighting;
        private readonly ulong _lightingRevision = owner._environmentLightingRevision;
        private readonly ulong _deviceGeneration = owner._environmentLightingDeviceGeneration;
        private readonly bool _perDomeGroups = owner._environmentPerDomeGroups;
        private readonly bool _resolved = owner._environmentsResolved;
        private readonly HashSet<string> _lit = new(owner._environmentLitDomes, StringComparer.Ordinal);
        private readonly HashSet<string> _skipped = new(owner._environmentPrefilterSkipped, StringComparer.Ordinal);
        private readonly HashSet<string> _retried = new(owner._environmentPrefilterRetried, StringComparer.Ordinal);
        private readonly Dictionary<(string, SilkEnvironmentAssetStamp), SilkImageDescription?> _descriptions =
            new(owner._environmentDescriptions);

        internal void RetainPayload() => _payloadOwnership = _payload?.AcquirePixelOwnership();
        internal void ReleasePayload() => Interlocked.Exchange(ref _payloadOwnership, null)?.Dispose();

        internal bool DeviceChanged(SilkSceneGpuResources target) =>
            _deviceGeneration != ulong.MaxValue &&
            SilkDeviceGeneration.Read(target._device) != _deviceGeneration;

        internal void ReleaseCandidates(SilkSceneGpuResources target, ref List<Exception>? failures)
        {
            ReleaseCandidate(target._environmentIrradianceTexture, ref failures);
            ReleaseCandidate(target._environmentSpecularTexture, ref failures);
            ReleaseCandidate(target._environmentBrdfTexture, ref failures);
            ReleaseCandidate(target._environmentStandIn, ref failures);
            ReleaseCandidate(target._environmentSampler, ref failures);
            ReleaseCandidate(target._environmentBrdfSampler, ref failures);
        }

        private void ReleaseCandidate(IDisposable? resource, ref List<Exception>? failures)
        {
            if (resource is not null &&
                !ReferenceEquals(resource, _irradiance) && !ReferenceEquals(resource, _specular) &&
                !ReferenceEquals(resource, _brdf) && !ReferenceEquals(resource, _standIn) &&
                !ReferenceEquals(resource, _sampler) && !ReferenceEquals(resource, _brdfSampler))
            {
                PreparedEnvironmentUpdate.Release(resource, ref failures);
            }
        }

        internal void Restore(SilkSceneGpuResources target)
        {
            target._environmentIrradianceTexture = _irradiance;
            target._environmentSpecularTexture = _specular;
            target._environmentBrdfTexture = _brdf;
            target._environmentStandIn = _standIn;
            target._environmentSampler = _sampler;
            target._environmentBrdfSampler = _brdfSampler;
            target.SetEnvironmentPayload(_payload);
            target._environmentBinding = _binding;
            target._environmentBindingRevision = _bindingRevision;
            target._environmentUploadedIdentity = _uploadedIdentity;
            target._environmentAssetRevision = _assetRevision;
            target._environmentMapsUploaded = _mapsUploaded;
            target._environmentBrdfUploaded = _brdfUploaded;
            target._environmentMapsUploadPending = _mapsPending;
            target._environmentBrdfUploadPending = _brdfPending;
            target._environmentPendingUploadBytes = _pendingBytes;
            target._environmentAuthoredSceneLighting = _authoredLighting;
            target._environmentLightingRevision = _lightingRevision;
            target._environmentLightingDeviceGeneration = _deviceGeneration;
            target._environmentPerDomeGroups = _perDomeGroups;
            target._environmentsResolved = _resolved;
            RestoreSet(target._environmentLitDomes, _lit);
            RestoreSet(target._environmentPrefilterSkipped, _skipped);
            RestoreSet(target._environmentPrefilterRetried, _retried);
            target._environmentDescriptions.Clear();
            foreach (var pair in _descriptions)
            {
                target._environmentDescriptions.Add(pair.Key, pair.Value);
            }
        }

        private static void RestoreSet(HashSet<string> target, HashSet<string> source)
        {
            target.Clear();
            target.UnionWith(source);
        }
    }
}
