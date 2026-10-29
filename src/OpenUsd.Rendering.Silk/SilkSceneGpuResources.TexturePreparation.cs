// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace OpenUsd.Rendering.Silk;

public sealed partial class SilkSceneGpuResources
{
    private PreparedTextureUpdate? _texturePreparation;
    private SilkDepthCaptureRequest? _materialCaptureRequest;
    private const int MaximumStreamedReuseEntries = 256;
    private readonly Dictionary<TextureCacheKey, SilkMaterialTexture> _streamedTextureDefinitions = [];
    private SilkDecodedImage? _streamedDecodedSource;
    private (string Asset, SilkColorSpace ColorSpace, bool Float) _streamedDecodedSourceKey;
    private TextureDependency[]? _streamedDecodedDependencies;

    internal bool StreamsMaterialTextures => _residencyOptions.StreamMaterialTextures;

    private TextureCacheEntry CreateStreamedUdimEntry(
        TextureCacheKey key, SilkMaterialData material, SilkMaterialTexture materialTexture, SilkColorSpace colorSpace)
    {
        const ulong maximumBandBytes = 8 * 1024 * 1024;
        _textures.EnsureCapacity(checked(_textures.Count + 1));
        using UdimAtlasSource source = DecodeUdimTiles(materialTexture, colorSpace, material);
        uint rowBytes = checked((uint)source.Width * SilkTextureFormats.GetBytesPerPixel(source.Format));
        // The assembled band and the command's owned upload copy overlap.
        ulong available = _cpuTextureBudget is null ? maximumBandBytes :
            (_cpuTextureBudget.MaximumBytes - _cpuTextureBudget.Usage.ReservedBytes) / 2;
        uint bandRows = checked((uint)Math.Min((ulong)source.Height,
            Math.Min(maximumBandBytes, available) / rowBytes));
        if (bandRows == 0)
        {
            if (_cpuTextureBudget is null)
            {
                throw new InvalidDataException("One UDIM atlas row exceeds the bounded upload band.");
            }
            throw new SilkCpuTextureBudgetExceededException(
                checked(2ul * rowBytes), _cpuTextureBudget.Usage.ReservedBytes, _cpuTextureBudget.MaximumBytes);
        }
        int bandBytes = checked((int)(bandRows * rowBytes));
        using IDisposable? reservation = _cpuTextureBudget?.Reserve((ulong)bandBytes);
        byte[] pixels = new byte[bandBytes];
        ISilkGraphicsTexture? texture = null;
        try
        {
            TrimStreamedTextureReuse((ulong)source.ByteCount, key.MaterialPath);
            texture = _device.CreateTexture2D(new SilkTextureDescriptor(
                (uint)source.Width, (uint)source.Height, source.Format,
                SilkTextureUsage.Sampled | SilkTextureUsage.CopySource | SilkTextureUsage.CopyDestination));
            for (uint first = 0; first < source.Height;)
            {
                _materialCaptureRequest?.ThrowIfCancellationRequested();
                uint rows = Math.Min(bandRows, (uint)source.Height - first);
                WriteUdimAtlasBand(source, materialTexture, pixels, checked((int)first));
                UploadTextureRowBand(texture, new SilkTextureRowUpload(0, first, rows),
                    pixels.AsSpan(0, checked((int)(rows * rowBytes))));
                first += rows;
                _materialCaptureRequest?.ThrowIfCancellationRequested();
            }
            var entry = new TextureCacheEntry(texture, [], (ulong)source.ByteCount, true,
                CaptureDependencies(source.Dependencies))
            {
                Uploaded = true
            };
            RegisterEntry(entry);
            _textures.Add(key, entry);
            texture = null;
            return entry;
        }
        finally
        {
            texture?.Dispose();
        }
    }

    private TextureCacheEntry CreateStreamedMipEntry(
        TextureCacheKey key, SilkDecodedImage image, Dictionary<TextureCacheKey, TextureCacheEntry> cache,
        IReadOnlyList<string>? dependencies, bool isNormalMap)
    {
        _materialCaptureRequest?.ThrowIfCancellationRequested();
        SilkMipGenerator.ValidateBaseLevel(image);
        ISilkGraphicsTexture? texture = null;
        SilkDecodedImage current = image;
        try
        {
            // Scalar slots sample only .r after scale/bias/channel reduction.
            // UDIM metadata and vector/color slots must retain their RGBA payload.
            if (IsScalarTextureParameter(key.Parameter))
            {
                current = CompactScalarImage(image);
                image.Dispose();
            }
            uint levels = SilkMipChainLayout.GetMaxMipLevelCount(current.Width, current.Height);
            ulong bytes = SilkMipChainLayout.GetLogicalByteSize(current.Width, current.Height, current.Format, levels);
            TrimStreamedTextureReuse(bytes, key.MaterialPath);
            texture = _device.CreateTexture2D(new SilkTextureDescriptor(
                current.Width, current.Height, current.Format,
                SilkTextureUsage.Sampled | SilkTextureUsage.CopySource | SilkTextureUsage.CopyDestination,
                levels));
            for (uint level = 0; level < levels; level++)
            {
                UploadMipRows(texture, current, level);
                if (level + 1 < levels)
                {
                    SilkDecodedImage next = SilkMipGenerator.GenerateNextLevel(current, isNormalMap, _cpuTextureBudget);
                    current.Dispose();
                    current = next;
                }
            }
            var entry = new TextureCacheEntry(texture, [], bytes, false, CaptureDependencies(dependencies))
            {
                Uploaded = true
            };
            RegisterEntry(entry);
            cache.Add(key, entry);
            texture = null;
            return entry;
        }
        finally
        {
            current.Dispose();
            texture?.Dispose();
        }
    }

    private SilkDecodedImage CompactScalarImage(SilkDecodedImage image)
    {
        SilkTextureFormat format = image.Format == SilkTextureFormat.Rgba32Float
            ? SilkTextureFormat.R32Float : SilkTextureFormat.R8Unorm;
        int componentBytes = checked((int)SilkTextureFormats.GetBytesPerPixel(format));
        int bytes = SilkMipChainLayout.GetTotalByteSize(image.Width, image.Height, format, 1);
        IDisposable? reservation = _cpuTextureBudget?.Reserve(checked((ulong)bytes));
        try
        {
            byte[] pixels = new byte[bytes];
            for (int offset = 0; offset < pixels.Length; offset += componentBytes)
            {
                image.Pixels.AsSpan(offset * 4, componentBytes).CopyTo(pixels.AsSpan(offset, componentBytes));
            }
            var result = new SilkDecodedImage(image.Width, image.Height, pixels, format);
            result.OwnReservation(reservation);
            reservation = null;
            return result;
        }
        finally
        {
            reservation?.Dispose();
        }
    }

    private void UploadMipRows(ISilkGraphicsTexture texture, SilkDecodedImage image, uint level)
    {
        const ulong maximumBandBytes = 8 * 1024 * 1024;
        uint rowBytes = checked(image.Width * SilkTextureFormats.GetBytesPerPixel(image.Format));
        for (uint first = 0; first < image.Height;)
        {
            _materialCaptureRequest?.ThrowIfCancellationRequested();
            ulong available = _cpuTextureBudget is null ? maximumBandBytes :
                _cpuTextureBudget.MaximumBytes - _cpuTextureBudget.Usage.ReservedBytes;
            uint rows = checked((uint)Math.Min((ulong)image.Height - first,
                Math.Min(maximumBandBytes, available) / rowBytes));
            if (rows == 0)
            {
                if (_cpuTextureBudget is null)
                {
                    throw new InvalidDataException("One material texture row exceeds the bounded upload band.");
                }
                throw new SilkCpuTextureBudgetExceededException(
                    rowBytes, _cpuTextureBudget.Usage.ReservedBytes, _cpuTextureBudget.MaximumBytes);
            }
            var region = new SilkTextureRowUpload(level, first, rows);
            UploadTextureRowBand(texture, region,
                image.Pixels.AsSpan(checked((int)(first * rowBytes)), checked((int)(rows * rowBytes))));
            first += rows;
            _materialCaptureRequest?.ThrowIfCancellationRequested();
        }
    }

    private void UploadTextureRowBand(
        ISilkGraphicsTexture texture, SilkTextureRowUpload region, ReadOnlySpan<byte> pixels)
    {
        using (ISilkGraphicsCommandList commands = _device.CreateCommandList())
        {
            if (commands is not ISilkTextureRowUploadCommandList upload)
            {
                throw new NotSupportedException("The device no longer provides material row uploads.");
            }
            upload.UploadTextureRows(texture, region, pixels);
            using ISilkGraphicsSubmission submission = _device.Submit(commands);
            submission.Wait();
        }
        _textureUploadBytes += checked((ulong)pixels.Length);
    }

    private void UploadStreamedTexture(TextureCacheEntry entry, bool volume)
    {
        _materialCaptureRequest?.ThrowIfCancellationRequested();
        ulong bytes = entry.DecodedBytes;
        using (ISilkGraphicsCommandList upload = _device.CreateCommandList())
        {
            RecordTextureUpload(upload, entry, volume);
            using ISilkGraphicsSubmission submission = _device.Submit(upload);
            submission.Wait();
        }
        entry.Uploaded = true;
        _textureUploadBytes += bytes;
        _decodedTextureResidentBytes = checked(_decodedTextureResidentBytes - bytes);
        entry.ReleasePixels();
        _materialCaptureRequest?.ThrowIfCancellationRequested();
    }

    internal IDisposable BeginMaterialTextureFrame(SilkDepthCaptureRequest? captureRequest = null)
    {
        captureRequest?.ThrowIfCancellationRequested();
        foreach (RenderDiagnostic diagnostic in _diagnostics.Values)
        {
            if (diagnostic.Code == SilkRenderDiagnosticCodes.TextureCoordinatesUnavailable)
            {
                RemoveDiagnostics(static code => code == SilkRenderDiagnosticCodes.TextureCoordinatesUnavailable);
                break;
            }
        }
        return new MaterialTextureFrame(this, captureRequest);
    }

    private sealed class MaterialTextureFrame : IDisposable
    {
        private readonly SilkSceneGpuResources _owner;
        private readonly SilkDepthCaptureRequest? _previous;
        private bool _disposed;

        internal MaterialTextureFrame(SilkSceneGpuResources owner, SilkDepthCaptureRequest? request)
        {
            _owner = owner;
            _previous = owner._materialCaptureRequest;
            owner._materialCaptureRequest = request;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _owner.ReleaseDecodedSource();
                _owner._materialCaptureRequest = _previous;
            }
        }
    }

    internal void ReportTextureCoordinateFallback(SilkMeshGpuResource mesh)
    {
        SilkMeshGpuGeometryKey key = mesh.Geometry.Key;
        if (key.RequireTextureCoordinates && mesh.Mesh.FindTexCoord(key.UvPrimvar) is null)
        {
            AddDiagnostic(SilkRenderDiagnosticCodes.TextureCoordinatesUnavailable, mesh.Mesh.Path,
                RenderDiagnosticSeverity.Warning,
                $"Mesh '{mesh.Mesh.Path}' has no '{key.UvPrimvar}' texture coordinates; " +
                "material textures sample renderer-default (0, 0) coordinates. " +
                "Custom primvar-reader fallback values are not represented by this projection.");
        }
        if (key.SecondaryUvPrimvar.Length > 0 && mesh.Mesh.FindTexCoord(key.SecondaryUvPrimvar) is null)
        {
            AddDiagnostic(SilkRenderDiagnosticCodes.TextureCoordinatesUnavailable, mesh.Mesh.Path,
                RenderDiagnosticSeverity.Warning,
                $"Mesh '{mesh.Mesh.Path}' has no '{key.SecondaryUvPrimvar}' texture coordinates; " +
                "material textures bound to that set sample renderer-default (0, 0) coordinates.");
        }
    }

    internal void ReleaseStreamedTextureWorkingSet(SilkMaterialData? previous = null, SilkMaterialData? next = null)
    {
        if (!StreamsMaterialTextures)
        {
            throw new InvalidOperationException("Material texture streaming is not enabled.");
        }
        ReleaseDecodedSource();
        List<(TextureCacheKey Key, TextureCacheEntry Entry)>? retained = null;
        if (previous is not null && next is not null &&
            !next.Textures.Any(static texture => texture.Parameter == SilkMaterialParameter.VolumeDensity))
        {
            foreach (SilkMaterialTexture outgoing in previous.Textures)
            {
                if (outgoing.Parameter is SilkMaterialParameter.Displacement or SilkMaterialParameter.VolumeDensity ||
                    outgoing.Asset.Contains("<UDIM>", StringComparison.Ordinal))
                {
                    continue;
                }
                var key = new TextureCacheKey(previous.Path, outgoing.Asset, GetEffectiveColorSpace(outgoing),
                    outgoing.Parameter, outgoing.Channel, outgoing.CompositeOperator,
                    UsesCompactRgb(previous, outgoing.Parameter),
                    UsesIndexedNormal(previous, outgoing.Parameter));
                if (_textures.TryGetValue(key, out TextureCacheEntry? entry) &&
                    entry.Uploaded && !entry.IsUdim && entry.DecodedBytes == 0)
                {
                    _streamedTextureDefinitions[key] = outgoing;
                }
            }
            foreach (TextureCacheKey key in _streamedTextureDefinitions.Keys)
            {
                if (_textures.Remove(key, out TextureCacheEntry? entry))
                {
                    (retained ??= []).Add((key, entry));
                }
            }
        }
        else
        {
            _streamedTextureDefinitions.Clear();
        }
        List<Exception>? failures = null;
        try
        {
            ReleaseWorkingSet(_textures, ref failures);
            ReleaseWorkingSet(_failedTextures, ref failures);
            ReleaseWorkingSet(_volumeTextures, ref failures);
        }
        finally
        {
            if (retained is not null)
            {
                foreach (var item in retained)
                {
                    _textures.Add(item.Key, item.Entry);
                }
            }
        }
        if (failures is not null)
        {
            throw new AggregateException(
                "The streamed material working set could not be completely released.", failures);
        }
        if (next is not null)
        {
            TrimStreamedTextureReuse(0, next.Path);
        }
    }

    private bool TryReuseStreamedTexture(
        TextureCacheKey key, SilkMaterialTexture texture, [NotNullWhen(true)] out TextureCacheEntry? reused)
    {
        foreach ((TextureCacheKey previous, SilkMaterialTexture definition) in _streamedTextureDefinitions)
        {
            if (previous.CompactRgb == key.CompactRgb && previous.IndexedNormal == key.IndexedNormal &&
                EquivalentStreamedTexture(definition, texture) &&
                _textures.TryGetValue(previous, out TextureCacheEntry? entry))
            {
                _streamedTextureDefinitions.Remove(previous);
                _textures.Remove(previous);
                if (DependenciesChanged(entry.Dependencies))
                {
                    ReleaseStreamedEntry(entry);
                    break;
                }
                _textures.Add(key, entry);
                _streamedTextureDefinitions.Add(key, texture);
                TouchEntry(entry);
                reused = entry;
                return true;
            }
        }
        reused = null;
        return false;
    }

    private void TrimStreamedTextureReuse(ulong incomingBytes, string activeMaterialPath)
    {
        if (!StreamsMaterialTextures)
        {
            return;
        }
        while (exceeds(_gpuTextureResidentBytes, incomingBytes, _residencyOptions.MaxGpuBytes) ||
            _streamedTextureDefinitions.Count > MaximumStreamedReuseEntries ||
            (_device is ISilkTextureAdmissionDevice { GpuTextureBudget: { } budget } &&
                exceeds(budget.Usage.ReservedBytes, incomingBytes, budget.MaximumBytes)))
        {
            // Only completed batches are indexed; the material being recorded stays pinned.
            TextureCacheKey candidateKey = default;
            TextureCacheEntry? candidate = null;
            foreach (TextureCacheKey key in _streamedTextureDefinitions.Keys)
            {
                if (key.MaterialPath != activeMaterialPath &&
                    _textures.TryGetValue(key, out TextureCacheEntry? entry) && IsOlder(entry, candidate))
                {
                    candidateKey = key;
                    candidate = entry;
                }
            }
            if (candidate is null)
            {
                return;
            }
            _streamedTextureDefinitions.Remove(candidateKey);
            _textures.Remove(candidateKey);
            ReleaseStreamedEntry(candidate);
            _textureEvictionCount++;
        }

        static bool exceeds(ulong resident, ulong incoming, ulong maximum) =>
            incoming > maximum || resident > maximum - incoming;
    }

    private void ReleaseStreamedEntry(TextureCacheEntry entry)
    {
        if (_texturePreparation is { } preparation && preparation.ReleaseCreated(entry))
        {
            DisposeEntryNow(entry);
        }
        else
        {
            DisposeEntry(entry);
        }
    }

    private void ReleaseDecodedSource()
    {
        SilkDecodedImage? source = _streamedDecodedSource;
        _streamedDecodedSource = null;
        _streamedDecodedSourceKey = default;
        _streamedDecodedDependencies = null;
        source?.Dispose();
    }

    private bool CanReuseDecodedSource(SilkDecodedImage image) =>
        _cpuTextureBudget is { } budget &&
        (ulong)image.Pixels.Length <= Math.Min(64ul * 1024 * 1024, budget.MaximumBytes / 4) &&
        budget.MaximumBytes - budget.Usage.ReservedBytes >=
            2ul * (ulong)image.Pixels.Length + image.Width * SilkTextureFormats.GetBytesPerPixel(image.Format);

    private SilkDecodedImage CopyDecodedSource(SilkDecodedImage source)
    {
        IDisposable? reservation = _cpuTextureBudget?.Reserve((ulong)source.Pixels.Length);
        try
        {
            var result = new SilkDecodedImage(
                source.Width, source.Height, (byte[])source.Pixels.Clone(), source.Format);
            result.OwnReservation(reservation);
            reservation = null;
            return result;
        }
        finally
        {
            reservation?.Dispose();
        }
    }

    private bool HasRepeatedDecodedSource(SilkMaterialData material, SilkMaterialTexture texture)
    {
        if (!StreamsMaterialTextures || _cpuTextureBudget is null)
        {
            return false;
        }
        int count = 0;
        foreach (SilkMaterialTexture candidate in material.Textures)
        {
            if (candidate.Parameter is not (SilkMaterialParameter.Displacement or
                SilkMaterialParameter.VolumeDensity) &&
                candidate.Asset == texture.Asset &&
                GetEffectiveColorSpace(candidate) == GetEffectiveColorSpace(texture) &&
                RequiresFloatTextureAffine(candidate) == RequiresFloatTextureAffine(texture) && ++count > 1)
            {
                return true;
            }
        }
        return false;
    }

    private static bool EquivalentStreamedTexture(SilkMaterialTexture left, SilkMaterialTexture right)
    {
        if (left.Asset != right.Asset || left.Parameter != right.Parameter ||
            left.SourceColorSpace != right.SourceColorSpace || left.ComponentCount != right.ComponentCount ||
            left.Channel != right.Channel || left.CompositeOperator != right.CompositeOperator ||
            left.CompositeFactor != right.CompositeFactor || left.LuminanceMix != right.LuminanceMix ||
            left.WrapS != right.WrapS || left.WrapT != right.WrapT || left.UvPrimvar != right.UvPrimvar)
        {
            return false;
        }
        return sameValues(left.Scale, right.Scale) && sameValues(left.Bias, right.Bias) &&
            sameValues(left.Fallback, right.Fallback);

        static bool sameValues(IReadOnlyList<float> left, IReadOnlyList<float> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }
            for (int index = 0; index < left.Count; index++)
            {
                if (BitConverter.SingleToInt32Bits(left[index]) != BitConverter.SingleToInt32Bits(right[index]))
                {
                    return false;
                }
            }
            return true;
        }
    }

    private void ReleaseWorkingSet<TKey>(
        Dictionary<TKey, TextureCacheEntry> cache, ref List<Exception>? failures) where TKey : notnull
    {
        foreach (TextureCacheEntry entry in cache.Values)
        {
            try
            {
                ReleaseStreamedEntry(entry);
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
        }
        cache.Clear();
    }

    private bool RequiresTexturePreparation =>
        StreamsMaterialTextures ||
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
        internal bool ReleaseCreated(TextureCacheEntry entry)
        {
            _retired.Remove(entry);
            return _created.Remove(entry);
        }

        internal void Prepare(SilkSceneState scene, SilkSceneDelta delta)
        {
            if (delta.MaterialChanges != 0)
            {
                _owner.RemoveMaterialDiagnostics();
            }
            _owner.RemoveChangedMaterialTextureCacheEntries(delta.ChangedMaterialPaths.Span);
            if (_owner.StreamsMaterialTextures)
            {
                return;
            }
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
                    TextureCacheEntry ordinary = _owner.RequireTexture(material, texture);
                    _owner.RequireSampler(material, texture, ordinary.Texture.Format, ordinary,
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
