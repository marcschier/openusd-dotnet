// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;

namespace OpenUsd.Rendering.Silk;

internal sealed class SilkShadowProjectionException(InvalidDataException innerException)
    : Exception(innerException.Message, innerException)
{
}

/// <summary>
/// One caster draw the depth-only shadow pass records.
/// </summary>
internal readonly record struct SilkShadowCaster(
    SilkMeshGpuResource Mesh,
    Matrix4x4 ObjectToLightClip,
    SilkMaterialData? MaskMaterial = null,
    SilkMeshData? Data = null);

internal enum SilkShadowOpacityCoverage
{
    Solid,
    Masked,
    Transmissive,
    Invisible,
    Unsupported,
}

/// <summary>
/// Owns the retained shadow atlas, decides when it has to be rendered again, and
/// releases it when a scene stops casting.
/// </summary>
/// <remarks>
/// <para>
/// The atlas is retained across frames on purpose: re-rendering every caster from
/// every light on a frame where nothing moved is the single largest avoidable
/// cost of a shadow slice. It is re-rendered exactly when one of the inputs it was
/// produced from changes -- the published descriptor table, caster geometry or
/// materials, caster restriction, device generation, captured opacity-image
/// dependencies, or explicit texture retry -- and reused byte for byte otherwise.
/// </para>
/// <para>
/// A scene that publishes no descriptor allocates no atlas and submits no pass.
/// The one texture such a scene still owns is a one-texel stand-in, allocated
/// only because the checked mesh fragment references the atlas slot in every
/// permutation and a backend pipeline layout requires every declared descriptor
/// to be populated; it is never sampled, because every light resolves to slot
/// <c>-1</c>.
/// </para>
/// <para>
/// Every GPU object the cache owns -- the atlas, the stand-in and the sampler --
/// belongs to the device generation that created it, and that generation is
/// tracked as one value. A reset destroys all of them together whatever their
/// dimensions were, so a changed generation releases all of them before any is
/// reused and re-creates what the frame actually needs, rather than reusing an
/// image because it happens to be the size the next frame wants.
/// </para>
/// </remarks>
internal sealed class SilkShadowMapCache : IDisposable
{
    private readonly ISilkGraphicsDevice _device;
    private readonly SilkGraphicsPipelineCache _pipelineCache;
    private SilkShadowAtlasResource? _atlas;
    private SilkShadowAtlasResource? _spareAtlas;
    private ISilkGraphicsTexture? _standIn;
    private ISilkGraphicsSampler? _sampler;
    private SilkShadowAtlasLayout? _layout;
    private SilkShadowFrameBinding _binding = SilkShadowFrameBinding.None;
    private readonly List<string> _unsupportedCasters = [];
    private ulong _casterReportRevision;
    private RenderKey? _renderedKey;
    private SilkSceneGpuResources.TextureDependency[] _renderedDependencies = [];
    private ulong _bindingRevision;

    /// <summary>
    /// The device generation the retained atlas, stand-in and sampler were
    /// created against, or <see langword="null"/> when the cache holds none of
    /// them. Every GPU object this cache owns belongs to exactly one generation,
    /// so this is the one value that decides whether any of them may be reused.
    /// </summary>
    private ulong? _resourceGeneration;
    private bool _disposed;

    private bool UsesAdmission =>
        _device is ISilkTextureAdmissionDevice { GpuTextureBudget: not null } ||
        _device is ISilkStagingAdmissionDevice { GpuStagingBudget: not null } ||
        _device is ISilkCpuTextureAdmissionDevice { CpuTextureBudget: not null } ||
        _device is ISilkBufferAdmissionDevice { GpuBufferBudget: not null };

    internal SilkShadowMapCache(
        ISilkGraphicsDevice device,
        SilkGraphicsPipelineCache pipelineCache)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(pipelineCache);
        _device = device;
        _pipelineCache = pipelineCache;
    }

    /// <summary>Gets the resolved shadow block the frame constants carry.</summary>
    internal SilkShadowFrameBinding Binding => _binding;

    /// <summary>
    /// Gets a revision that changes only when the resolved frame block changes, so
    /// the frame constants re-pack exactly when they must.
    /// </summary>
    internal ulong BindingRevision => _bindingRevision;

    /// <summary>Gets the number of retained shadow maps.</summary>
    internal int MapCount => _layout?.Tiles.Count ?? 0;

    /// <summary>Gets the square atlas edge length in texels, or zero when unused.</summary>
    internal uint AtlasEdge => _layout is null ? 0 : _layout.Edge;

    /// <summary>Gets the number of times the retained atlas has been rendered.</summary>
    internal ulong RenderCount { get; private set; }

    internal void ReportPreparedCasters(SilkSceneGpuResources resources) =>
        resources.ReportUnsupportedShadowCasters(_unsupportedCasters, _casterReportRevision);

    internal PreparedCapacity? PrepareCapacity(
        SilkSceneState scene,
        SilkSceneGpuResources resources,
        bool prepareRendering = false,
        bool useSceneMaterials = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(resources);
        IReadOnlyList<SilkShadowDescriptor> descriptors =
            ExpandShadowDescriptorsForPreparation(
                scene, resources, out double displacementPadding);
        if (!UsesAdmission && !RequiresTransmission(scene, useSceneMaterials) &&
            _atlas?.HasTransmission != true && _spareAtlas?.HasTransmission != true)
        {
            return null;
        }
        ulong generation = ReadDeviceGeneration();
        SynchronizeGeneration(generation);
        bool enabled = _device.Capabilities.SupportsRasterShadows && descriptors.Count > 0;
        bool clearAtlas = !enabled &&
            (_atlas is not null || _spareAtlas is not null || _layout is not null || _unsupportedCasters.Count > 0);
        if (!enabled && scene.Meshes.Count == 0 && !clearAtlas)
        {
            return null;
        }
        uint edge = enabled ? SilkShadowAtlasLayout.Create(descriptors)!.Edge : 0;
        bool renderNeeded = enabled &&
            (_atlas is null || _layout?.Edge != edge ||
                _renderedKey != CreateKey(scene, resources, generation, useSceneMaterials, displacementPadding) ||
                SilkSceneGpuResources.DependenciesChanged(_renderedDependencies));
        bool transmission = RequiresTransmission(scene, useSceneMaterials);
        bool atlasNeeded = renderNeeded &&
            (_spareAtlas is null || _spareAtlas.Width != edge || _spareAtlas.Height != edge ||
                _spareAtlas.HasTransmission != transmission);
        bool standInNeeded = !enabled && scene.Meshes.Count > 0 && _standIn is null;
        bool samplerNeeded = scene.Meshes.Count > 0 && _sampler is null;
        if (!atlasNeeded && !standInNeeded && !samplerNeeded && !(prepareRendering && renderNeeded) && !clearAtlas)
        {
            return null;
        }
        if (renderNeeded)
        {
            ValidateTransmissionWorkspace(edge, transmission, atlasNeeded);
        }
        var prepared = new PreparedCapacity(
            this, prepareRendering && renderNeeded, clearAtlas, descriptors, useSceneMaterials,
            displacementPadding);
        try
        {
            prepared.Allocate(atlasNeeded ? edge : 0, standInNeeded, samplerNeeded, transmission);
            return prepared;
        }
        catch (Exception failure)
        {
            try
            {
                prepared.Dispose();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException("Shadow capacity preparation and cleanup failed.", failure, cleanup);
            }
            throw;
        }
    }

    internal sealed class PreparedCapacity(
        SilkShadowMapCache owner,
        bool renderNeeded,
        bool clearAtlas,
        IReadOnlyList<SilkShadowDescriptor> descriptors,
        bool useSceneMaterials,
        double displacementPadding) : IDisposable
    {
        private readonly SilkShadowDescriptor[] _descriptors = [.. descriptors];
        private readonly SilkShadowAtlasResource? _previousAtlas = owner._atlas;
        private readonly SilkShadowAtlasResource? _previousSpare = owner._spareAtlas;
        private readonly ISilkGraphicsTexture? _previousStandIn = owner._standIn;
        private readonly ISilkGraphicsSampler? _previousSampler = owner._sampler;
        private readonly SilkShadowAtlasLayout? _previousLayout = owner._layout;
        private readonly SilkShadowFrameBinding _previousBinding = owner._binding;
        private readonly RenderKey? _previousKey = owner._renderedKey;
        private readonly SilkSceneGpuResources.TextureDependency[] _previousDependencies = owner._renderedDependencies;
        private readonly ulong _previousBindingRevision = owner._bindingRevision;
        private readonly ulong _previousRenderCount = owner.RenderCount;
        private readonly string[] _previousUnsupported = [.. owner._unsupportedCasters];
        private readonly ulong _previousReportRevision = owner._casterReportRevision;
        private readonly ulong? _previousGeneration = owner._resourceGeneration;
        private SilkShadowAtlasResource? _atlas;
        private ISilkGraphicsTexture? _standIn;
        private ISilkGraphicsSampler? _sampler;
        private SilkShadowAtlasResource? _retiredSpare;
        private SilkShadowAtlasResource? _retiredAtlas;
        private bool _borrowedAtlas;
        private SilkShadowAtlasLayout? _layout;
        private SilkShadowFrameBinding? _binding;
        private RenderKey? _key;
        private readonly HashSet<SilkSceneGpuResources.TextureDependency> _dependencies = [];
        private SilkSceneGpuResources.PreparedShadowTextures? _textures;
        private readonly List<string> _unsupported = [];
        private bool _committed;
        private bool _activated;
        private bool _disposed;

        internal void Allocate(uint edge, bool standIn, bool sampler, bool transmission = false)
        {
            if (edge != 0)
            {
                _atlas = new SilkShadowAtlasResource(owner._device, edge, transmission);
            }
            if (standIn)
            {
                _standIn = owner._device.CreateTexture2D(SilkTextureDescriptor.SampledDepthTarget(1, 1));
                using ISilkGraphicsCommandList commands = owner._device.CreateCommandList();
                commands.ClearDepth(_standIn, 1);
                using ISilkGraphicsSubmission submission = owner._device.Submit(commands);
                submission.Wait();
            }
            if (sampler)
            {
                _sampler = owner._device.CreateSampler(SilkSamplerDescriptor.NearestClamp);
            }
        }

        internal void Render(
            SilkSceneState scene, SilkSceneGpuResources resources,
            Func<SilkMeshGpuGeometryResource, IReadOnlyList<SilkShadowCaster>, ISilkGraphicsBuffer>? writeInstances)
        {
            if (!renderNeeded)
            {
                return;
            }
            if (_atlas is null)
            {
                _atlas = owner._spareAtlas ??
                    throw new InvalidOperationException("Prepared shadow rendering requires an admitted atlas.");
                _borrowedAtlas = true;
            }
            SilkShadowAtlasLayout layout = SilkShadowAtlasLayout.Create(_descriptors)!;
            SilkShadowFrameBinding binding = SilkShadowFrameBinding.Create(
                _descriptors, layout, _atlas.HasTransmission);
            _textures = resources.PrepareShadowTextureUpdate();
            _ = owner.Render(
                scene, resources, _descriptors, layout, _dependencies, _atlas, _unsupported, writeInstances,
                useSceneMaterials);
            owner._unsupportedCasters.EnsureCapacity(_unsupported.Count);
            _layout = layout;
            _binding = binding;
            _key = CreateKey(
                scene, resources, owner.ReadDeviceGeneration(), useSceneMaterials, displacementPadding);
        }

        internal void Activate()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_activated)
            {
                return;
            }
            if (clearAtlas)
            {
                _retiredAtlas = owner._atlas;
                _retiredSpare = owner._spareAtlas;
                owner._atlas = null;
                owner._spareAtlas = null;
                owner._layout = null;
                owner._renderedKey = null;
                owner._renderedDependencies = [];
                owner._binding = SilkShadowFrameBinding.None;
                owner._bindingRevision++;
                owner._unsupportedCasters.Clear();
                owner._casterReportRevision++;
            }
            if (_layout is not null)
            {
                SilkShadowAtlasResource? previous = owner._atlas;
                if (!ReferenceEquals(owner._spareAtlas, _atlas))
                {
                    _retiredSpare = owner._spareAtlas;
                }
                owner._atlas = _atlas;
                _atlas = null;
                owner._spareAtlas = previous is not null &&
                    previous.Width == _layout.Edge && previous.Height == _layout.Edge &&
                    previous.HasTransmission == owner._atlas!.HasTransmission ? previous : null;
                if (!ReferenceEquals(previous, owner._spareAtlas))
                {
                    _retiredAtlas = previous;
                }
                owner._layout = _layout;
                owner._binding = _binding!;
                owner._renderedKey = _key;
                owner._renderedDependencies = [.. _dependencies];
                owner._bindingRevision++;
                owner.RenderCount++;
                owner._unsupportedCasters.Clear();
                owner._unsupportedCasters.AddRange(_unsupported);
                owner._casterReportRevision++;
            }
            else if (_atlas is not null && !_borrowedAtlas)
            {
                _retiredSpare = owner._spareAtlas;
                owner._spareAtlas = _atlas;
                _atlas = null;
            }
            if (_standIn is not null)
            {
                owner._standIn = _standIn;
                _standIn = null;
            }
            if (_sampler is not null)
            {
                owner._sampler = _sampler;
                _sampler = null;
            }
            _activated = true;
        }

        internal void Commit()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_committed)
            {
                throw new InvalidOperationException("Shadow capacity was already committed.");
            }
            Activate();
            _textures?.Commit();
            _committed = true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            List<Exception>? failures = null;
            if (!_committed && _activated)
            {
                ReleaseCandidate(owner._atlas, ref failures);
                ReleaseCandidate(owner._spareAtlas, ref failures);
                ReleaseCandidate(owner._standIn, ref failures);
                if (!ReferenceEquals(owner._sampler, _previousSampler))
                {
                    DisposeResource(owner._sampler, ref failures);
                }
                owner._atlas = _previousAtlas;
                owner._spareAtlas = _previousSpare;
                owner._standIn = _previousStandIn;
                owner._sampler = _previousSampler;
                owner._layout = _previousLayout;
                owner._binding = _previousBinding;
                owner._renderedKey = _previousKey;
                owner._renderedDependencies = _previousDependencies;
                owner._bindingRevision = _previousBindingRevision;
                owner.RenderCount = _previousRenderCount;
                owner._unsupportedCasters.Clear();
                owner._unsupportedCasters.AddRange(_previousUnsupported);
                owner._casterReportRevision = _previousReportRevision;
                owner._resourceGeneration = _previousGeneration;
                _retiredSpare = null;
                _retiredAtlas = null;
            }
            if (!_borrowedAtlas)
            {
                DisposeResource(_atlas, ref failures);
            }
            DisposeResource(_standIn, ref failures);
            DisposeResource(_sampler, ref failures);
            DisposeResource(_retiredSpare, ref failures);
            DisposeResource(_retiredAtlas, ref failures);
            DisposeResource(_textures, ref failures);
            if (failures is not null)
            {
                throw new AggregateException("Prepared shadow capacity could not all be released.", failures);
            }
        }

        private void ReleaseCandidate(IDisposable? texture, ref List<Exception>? failures)
        {
            if (!ReferenceEquals(texture, _previousAtlas) && !ReferenceEquals(texture, _previousSpare) &&
                !ReferenceEquals(texture, _previousStandIn))
            {
                DisposeResource(texture, ref failures);
            }
        }
    }

    private static void DisposeResource(IDisposable? resource, ref List<Exception>? failures)
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

    private static RenderKey CreateKey(
        SilkSceneState scene, SilkSceneGpuResources resources, ulong generation,
        bool useSceneMaterials, double displacementPadding) => new(
        scene.Shadows.Revision, scene.GeometryRevision, scene.MaterialRevision,
        scene.LightLinks.Revision, scene.DeformationRevision, generation,
        resources.TextureRetryRevision, useSceneMaterials, displacementPadding);

    private static IReadOnlyList<SilkShadowDescriptor> ExpandShadowDescriptorsForPreparation(
        SilkSceneState scene,
        SilkSceneGpuResources resources,
        out double displacementPadding)
    {
        try
        {
            return ExpandShadowDescriptors(scene, resources, out displacementPadding);
        }
        catch (InvalidDataException exception)
        {
            throw new SilkShadowProjectionException(exception);
        }
    }

    private static IReadOnlyList<SilkShadowDescriptor> ExpandShadowDescriptors(
        SilkSceneState scene,
        SilkSceneGpuResources resources,
        out double displacementPadding)
    {
        if (scene.Shadows.Descriptors.Count == 0)
        {
            displacementPadding = 0;
            return scene.Shadows.Descriptors;
        }

        double maximumDisplacement = 0;
        foreach ((ulong id, SilkMeshData mesh) in scene.Meshes)
        {
            SilkMeshGpuGeometryResource geometry = resources.GetPreparedMesh(id).Geometry;
            if (geometry.MaximumDisplacement <= 0)
            {
                continue;
            }

            ReadOnlySpan<double> transform = mesh.Transform.Span;
            double scale = 0;
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    scale = Math.Max(scale, Math.Abs(transform[(row * 4) + column]));
                }
            }
            if (scale == 0)
            {
                continue;
            }

            double squaredNorm = 0;
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    double component = transform[(row * 4) + column] / scale;
                    squaredNorm += component * component;
                }
            }
            double worldDisplacement =
                geometry.MaximumDisplacement * scale * Math.Sqrt(squaredNorm);
            if (!double.IsFinite(worldDisplacement))
            {
                throw new InvalidDataException(
                    $"Displacement bounds for mesh '{mesh.Path}' exceed the supported numeric range.");
            }
            maximumDisplacement = Math.Max(maximumDisplacement, worldDisplacement);
        }

        displacementPadding = maximumDisplacement == 0
            ? 0
            : maximumDisplacement + Math.Max(1e-7, maximumDisplacement * 1e-6);
        if (displacementPadding == 0)
        {
            return scene.Shadows.Descriptors;
        }

        var expanded = new SilkShadowDescriptor[scene.Shadows.Descriptors.Count];
        for (int index = 0; index < expanded.Length; index++)
        {
            SilkShadowDescriptor descriptor = scene.Shadows.Descriptors[index];
            if ((descriptor.Flags & SilkShadowDescriptorOptions.Orthographic) == 0)
            {
                expanded[index] = ExpandPerspectiveDescriptor(descriptor, displacementPadding);
                continue;
            }

            double radius = 1.0 / descriptor.Projection[0];
            double expandedRadius = radius + displacementPadding;
            if (!double.IsFinite(radius) || radius <= 0 || !double.IsFinite(expandedRadius))
            {
                throw new InvalidDataException(
                    "The displaced caster bounds exceed the shadow projection's numeric range.");
            }

            double[] view = (double[])descriptor.View.Clone();
            view[14] -= 2.0 * displacementPadding;
            if (!double.IsFinite(view[14]))
            {
                throw new InvalidDataException(
                    "The displaced caster bounds exceed the shadow view numeric range.");
            }
            double[] projection = (double[])descriptor.Projection.Clone();
            projection[0] = 1.0 / expandedRadius;
            projection[5] = 1.0 / expandedRadius;
            projection[10] = -1.0 / expandedRadius;
            projection[14] = -2.0 - (1.0 / expandedRadius);
            float normalBias = (float)((3.0 * expandedRadius) / descriptor.Resolution);
            if (!float.IsFinite(normalBias))
            {
                throw new InvalidDataException(
                    "The displaced caster bounds exceed the shadow normal-bias numeric range.");
            }
            expanded[index] = descriptor with
            {
                NormalBias = normalBias,
                View = view,
                Projection = projection,
            };
        }
        return expanded;
    }

    internal static SilkShadowDescriptor ExpandPerspectiveDescriptor(
        SilkShadowDescriptor descriptor,
        double displacementPadding)
    {
        double[] source = descriptor.Projection;
        if (source is null || descriptor.View is null ||
            source.Length != 16 || descriptor.View.Length != 16 ||
            descriptor.Resolution == 0 ||
            !double.IsFinite(displacementPadding) || displacementPadding <= 0 ||
            !double.IsFinite(source[0]) || source[0] <= 0 ||
            !double.IsFinite(source[5]) || source[5] <= 0 ||
            source[11] != -1 || source[15] != 0 ||
            !double.IsFinite(source[10]) || !double.IsFinite(source[14]) ||
            source[10] >= -1 || source[14] >= 0)
        {
            throw new InvalidDataException(
                "The displaced caster bounds require a finite perspective shadow projection.");
        }

        double nearPlane = source[14] / (source[10] - 1);
        double farPlane = source[14] / (source[10] + 1);
        bool cube = (descriptor.Flags & SilkShadowDescriptorOptions.CubeFace) != 0;
        double expandedNear = cube ? nearPlane : nearPlane - displacementPadding;
        double expandedFar = farPlane + displacementPadding;
        if (!double.IsFinite(nearPlane) || !double.IsFinite(farPlane) ||
            nearPlane <= 0 || farPlane <= nearPlane || expandedNear <= 0 ||
            !double.IsFinite(expandedFar) || expandedFar <= expandedNear)
        {
            throw new InvalidDataException(
                "The displaced caster bounds exceed the perspective shadow depth range.");
        }

        double expandedHorizontalTangent =
            cube ? 1 / source[0] : ((nearPlane / source[0]) + displacementPadding) / expandedNear;
        double expandedVerticalTangent =
            cube ? 1 / source[5] : ((nearPlane / source[5]) + displacementPadding) / expandedNear;
        if (!double.IsFinite(expandedHorizontalTangent) || expandedHorizontalTangent <= 0 ||
            !double.IsFinite(expandedVerticalTangent) || expandedVerticalTangent <= 0)
        {
            throw new InvalidDataException(
                "The displaced caster bounds exceed the perspective shadow frustum range.");
        }

        double[] projection = (double[])source.Clone();
        projection[0] = 1 / expandedHorizontalTangent;
        projection[5] = 1 / expandedVerticalTangent;
        projection[10] = -(expandedFar + expandedNear) / (expandedFar - expandedNear);
        projection[14] =
            -(2 * expandedFar * expandedNear) / (expandedFar - expandedNear);
        double normalBiasValue =
            (3 * expandedFar * Math.Max(expandedHorizontalTangent, expandedVerticalTangent)) /
            descriptor.Resolution;
        float normalBias = (float)normalBiasValue;
        if (!double.IsFinite(projection[0]) || !double.IsFinite(projection[5]) ||
            !double.IsFinite(projection[10]) || !double.IsFinite(projection[14]) ||
            !float.IsFinite(normalBias))
        {
            throw new InvalidDataException(
                "The displaced caster bounds exceed the perspective shadow numeric range.");
        }

        return descriptor with
        {
            DepthBias = cube ? (float)(descriptor.DepthBias * farPlane / expandedFar) : descriptor.DepthBias,
            NormalBias = normalBias,
            Projection = projection,
        };
    }

    /// <summary>
    /// Renders every shadow map the scene describes, reusing the retained atlas
    /// when nothing it was produced from has changed.
    /// </summary>
    /// <returns>The number of caster draws recorded, which is zero on a reuse.</returns>
    internal int Prepare(
        SilkSceneState scene, SilkSceneGpuResources resources, bool useSceneMaterials = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(resources);

        IReadOnlyList<SilkShadowDescriptor> descriptors =
            ExpandShadowDescriptorsForPreparation(
                scene, resources, out double displacementPadding);

        // Ownership first, and before anything is read or reused. Every GPU
        // object this cache holds was created by one device generation, and a
        // reset destroys all of them together -- the atlas, the one-texel
        // stand-in the disabled path binds, and the sampler that is otherwise
        // created once and never touched again. Releasing them here is what
        // makes the reuse decisions below safe to take at face value: after this
        // call every surviving field belongs to the generation that is current
        // now.
        ulong generation = ReadDeviceGeneration();
        SynchronizeGeneration(generation);

        bool enabled = _device.Capabilities.SupportsRasterShadows && descriptors.Count > 0;
        if (!enabled)
        {
            if (UsesAdmission)
            {
                EnsureStandIn();
                RequireSampler();
                ReleaseAtlas();
                ReleaseSpareAtlas();
            }
            else
            {
                ReleaseAtlas();
                ReleaseSpareAtlas();
                EnsureStandIn();
            }
            if (_unsupportedCasters.Count > 0)
            {
                _unsupportedCasters.Clear();
                resources.ReportUnsupportedShadowCasters(
                    _unsupportedCasters,
                    ++_casterReportRevision);
            }
            return 0;
        }

        SilkShadowAtlasLayout layout = SilkShadowAtlasLayout.Create(descriptors)!;
        RenderKey key = CreateKey(scene, resources, generation, useSceneMaterials, displacementPadding);
        if (_atlas is not null &&
            _layout is not null &&
            _layout.Edge == layout.Edge &&
            _renderedKey == key &&
            !SilkSceneGpuResources.DependenciesChanged(_renderedDependencies))
        {
            resources.ReportUnsupportedShadowCasters(_unsupportedCasters, _casterReportRevision);
            return 0;
        }

        if (UsesAdmission || RequiresTransmission(scene, useSceneMaterials) ||
            _atlas?.HasTransmission == true || _spareAtlas?.HasTransmission == true)
        {
            return PrepareCandidate(scene, resources, descriptors, layout, key, useSceneMaterials);
        }

        if (_atlas is null || _layout is null || _layout.Edge != layout.Edge)
        {
            // A resolution change is the only reason a live atlas is replaced;
            // a generation change already released it above. Everything else
            // re-renders into the atlas already allocated, which is what keeps
            // a moving scene from reallocating every frame.
            //
            // The retained atlas is unpublished before the old image is released
            // and the replacement is asked for, so a refused allocation leaves the
            // cache holding nothing rather than a field still pointing at a
            // disposed texture. That distinction is the whole point: the colour
            // pass binds this field on every frame, and binding a disposed image
            // is a use-after-free the moment the next frame runs, whereas binding
            // nothing falls through to the one-texel stand-in that every light
            // already resolves to slot -1 against.
            ReleaseAtlas();
            _atlas = new SilkShadowAtlasResource(_device, layout.Edge, transmission: false);
        }

        _layout = layout;
        _binding = SilkShadowFrameBinding.Create(descriptors, layout);
        _bindingRevision++;
        _unsupportedCasters.Clear();
        var dependencies = new HashSet<SilkSceneGpuResources.TextureDependency>();
        int draws = Render(scene, resources, descriptors, layout, dependencies, useSceneMaterials: useSceneMaterials);
        resources.ReportUnsupportedShadowCasters(_unsupportedCasters, ++_casterReportRevision);
        _renderedKey = key;
        _renderedDependencies = [.. dependencies];
        RenderCount++;
        return draws;
    }

    private int PrepareCandidate(
        SilkSceneState scene,
        SilkSceneGpuResources resources,
        IReadOnlyList<SilkShadowDescriptor> descriptors,
        SilkShadowAtlasLayout layout,
        RenderKey key,
        bool useSceneMaterials)
    {
        SilkShadowAtlasResource? previous = _atlas;
        SilkShadowAtlasLayout? previousLayout = _layout;
        SilkShadowFrameBinding previousBinding = _binding;
        RenderKey? previousKey = _renderedKey;
        SilkSceneGpuResources.TextureDependency[] previousDependencies = _renderedDependencies;
        ulong previousBindingRevision = _bindingRevision;
        ulong previousCasterRevision = _casterReportRevision;
        ulong previousRenderCount = RenderCount;
        ISilkGraphicsSampler? previousSampler = _sampler;
        string[] previousUnsupported = [.. _unsupportedCasters];
        bool transmission = RequiresTransmission(scene, useSceneMaterials);
        bool allocateCandidate = _spareAtlas is null || _spareAtlas.Width != layout.Edge ||
            _spareAtlas.Height != layout.Edge || _spareAtlas.HasTransmission != transmission;
        ValidateTransmissionWorkspace(layout.Edge, transmission, allocateCandidate);
        SilkShadowAtlasResource? previousSpare = _spareAtlas;
        bool reused = !allocateCandidate;
        SilkShadowAtlasResource? candidate = null;
        SilkSceneGpuResources.PreparedShadowTextures? textures = resources.PrepareShadowTextureUpdate();
        int draws;
        try
        {
            candidate = reused ? previousSpare! :
                new SilkShadowAtlasResource(_device, layout.Edge, transmission);
            _spareAtlas = null;
            RequireSampler();
            _atlas = candidate;
            _layout = layout;
            _binding = SilkShadowFrameBinding.Create(descriptors, layout, candidate.HasTransmission);
            _unsupportedCasters.Clear();
            var dependencies = new HashSet<SilkSceneGpuResources.TextureDependency>();
            draws = Render(
                scene, resources, descriptors, layout, dependencies, useSceneMaterials: useSceneMaterials);
            resources.ReportUnsupportedShadowCasters(_unsupportedCasters, ++_casterReportRevision);
            _renderedKey = key;
            _renderedDependencies = [.. dependencies];
            _bindingRevision++;
            RenderCount++;
            textures?.Commit();
        }
        catch (Exception failure)
        {
            _atlas = previous;
            _layout = previousLayout;
            _binding = previousBinding;
            _renderedKey = previousKey;
            _renderedDependencies = previousDependencies;
            _bindingRevision = previousBindingRevision;
            _casterReportRevision = previousCasterRevision;
            RenderCount = previousRenderCount;
            _unsupportedCasters.Clear();
            _unsupportedCasters.AddRange(previousUnsupported);
            ISilkGraphicsSampler? pendingSampler = ReferenceEquals(_sampler, previousSampler) ? null : _sampler;
            _sampler = previousSampler;
            _spareAtlas = previousSpare;
            List<Exception>? cleanupFailures = null;
            if (!reused)
            {
                DisposeResource(candidate, ref cleanupFailures);
            }
            DisposeResource(pendingSampler, ref cleanupFailures);
            DisposeResource(textures, ref cleanupFailures);
            try
            {
                SynchronizeGeneration(ReadDeviceGeneration());
            }
            catch (Exception cleanup)
            {
                (cleanupFailures ??= []).Add(cleanup);
            }
            if (cleanupFailures is not null)
            {
                cleanupFailures.Insert(0, failure);
                throw new AggregateException(
                    "Shadow preparation failed and its candidate could not be released.", cleanupFailures);
            }
            throw;
        }
        List<Exception>? retirementFailures = null;
        DisposeResource(textures, ref retirementFailures);
        if (!reused)
        {
            DisposeResource(previousSpare, ref retirementFailures);
        }
        if (previous is not null && previous.Width == layout.Edge && previous.Height == layout.Edge &&
            previous.HasTransmission == transmission)
        {
            _spareAtlas = previous;
        }
        else
        {
            DisposeResource(previous, ref retirementFailures);
        }
        if (retirementFailures is not null)
        {
            throw new AggregateException("Published shadow atlas retirement failed.", retirementFailures);
        }
        return draws;
    }

    /// <summary>Binds the shadow atlas and its sampler for one colour-pass draw.</summary>
    /// <remarks>
    /// Always bound, because the checked mesh fragment references the slot in every
    /// permutation and a backend pipeline layout requires every declared descriptor
    /// to be populated. A frame with no shadow map binds the one-texel stand-in and
    /// never samples it.
    /// </remarks>
    internal void Bind(ISilkGraphicsCommandList commands)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(commands);

        // The colour pass can reach this before the next frame's Prepare -- a
        // reset detected by the submission that ended the previous frame is
        // already visible here -- so ownership is re-checked rather than
        // assumed. Binding is the one place a stale handle would actually reach
        // the device, which is why the check is repeated at the point of use.
        SynchronizeGeneration(ReadDeviceGeneration());
        commands.SetSampler(
            0,
            SilkBindingLayoutDescriptor.ShadowSamplerBinding,
            RequireSampler());
        commands.SetTexture(
            0,
            SilkBindingLayoutDescriptor.ShadowAtlasTextureBinding,
            _atlas?.Depth ?? EnsureStandIn());
        for (int layer = 0; layer < SilkShadowAtlasResource.MaximumTransmissionLayers; layer++)
        {
            commands.SetTexture(
                0,
                SilkBindingLayoutDescriptor.ShadowTransmissionTextureBinding + (uint)layer,
                _atlas?.HasTransmission == true ? _atlas.Layers[layer] : _atlas?.Depth ?? EnsureStandIn());
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseGenerationOwnedResources();
        _resourceGeneration = null;
    }

    /// <summary>
    /// Releases every GPU object the cache owns when the generation that
    /// created them is no longer the generation the device reports.
    /// </summary>
    /// <remarks>
    /// A reset drops what the device held whatever its dimensions were, so this
    /// releases and re-creates at an identical edge length as readily as at a
    /// changed one: an image that a reset destroyed is not reusable because it
    /// happens to be the size the next frame wants.
    ///
    /// The generation is recorded before any replacement is allocated, so a
    /// refused allocation leaves the cache owning nothing at the current
    /// generation rather than nothing at the previous one. That is what makes
    /// the retry a plain allocation on the next frame instead of a second
    /// release of fields that are already null.
    /// </remarks>
    private void SynchronizeGeneration(ulong generation)
    {
        if (_resourceGeneration == generation)
        {
            return;
        }

        ReleaseGenerationOwnedResources();
        _resourceGeneration = generation;
    }

    private void ReleaseGenerationOwnedResources()
    {
        ReleaseAtlas();
        ReleaseSpareAtlas();

        // Unpublished before disposed, in that order and for every object, so
        // that a Dispose that throws cannot leave a field pointing at something
        // half-destroyed for the next frame to bind.
        ISilkGraphicsTexture? standIn = _standIn;
        ISilkGraphicsSampler? sampler = _sampler;
        _standIn = null;
        _sampler = null;
        standIn?.Dispose();
        sampler?.Dispose();
    }

    private int Render(
        SilkSceneState scene,
        SilkSceneGpuResources resources,
        IReadOnlyList<SilkShadowDescriptor> descriptors,
        SilkShadowAtlasLayout layout,
        HashSet<SilkSceneGpuResources.TextureDependency> dependencies,
        SilkShadowAtlasResource? candidate = null,
        List<string>? unsupported = null,
        Func<SilkMeshGpuGeometryResource, IReadOnlyList<SilkShadowCaster>, ISilkGraphicsBuffer>? writeInstances = null,
        bool useSceneMaterials = true)
    {
        SilkShadowAtlasResource atlas = candidate ?? _atlas ??
            throw new InvalidOperationException("Shadow rendering requires an atlas.");
        int draws = 0;
        var casters = new List<SilkShadowCaster>();
        for (int slot = 0; slot < descriptors.Count; slot++)
        {
            SilkShadowDescriptor descriptor = descriptors[slot];
            casters.Clear();
            CollectCasters(
                scene,
                resources,
                descriptor,
                _device.ClipSpaceYPointsDown,
                casters,
                unsupported ?? _unsupportedCasters,
                useSceneMaterials);

            var groups = new Dictionary<string, List<SilkShadowCaster>>(StringComparer.Ordinal);
            var order = new List<List<SilkShadowCaster>>();
            foreach (SilkShadowCaster caster in casters)
            {
                string path = caster.MaskMaterial?.Path ?? string.Empty;
                if (!groups.TryGetValue(path, out List<SilkShadowCaster>? group))
                {
                    group = [];
                    groups.Add(path, group);
                    order.Add(group);
                }
                group.Add(caster);
            }
            if (order.Count == 0)
            {
                order.Add([]);
            }

            for (int groupIndex = 0; groupIndex < order.Count; groupIndex++)
            {
                List<SilkShadowCaster> group = order[groupIndex];
                SilkMaterialData? material = group.Count == 0 ? null : group[0].MaskMaterial;
                try
                {
                    if (material is not null)
                    {
                        resources.PrepareShadowOpacityTextures(material, dependencies);
                    }
                    using ISilkGraphicsCommandList commands = _device.CreateCommandList();
                    var shadowCommands = commands as ISilkShadowGraphicsCommandList ??
                        throw new InvalidOperationException(
                            "A raster-shadow-capable device must create shadow-capable command lists.");
                    if (slot == 0 && groupIndex == 0)
                    {
                        commands.ClearDepth(atlas.Depth, 1f);
                    }
                    shadowCommands.BeginShadowRendering(new SilkShadowRenderingDescriptor(atlas.Depth));
                    SilkShadowTilePlacement tile = layout.Tiles[slot];
                    commands.SetViewport(new SilkViewport(
                        tile.PixelX,
                        tile.PixelY,
                        tile.Resolution,
                        tile.Resolution,
                        0,
                        1));
                    commands.SetScissor(new SilkScissor(
                        (int)tile.PixelX,
                        (int)tile.PixelY,
                        tile.Resolution,
                        tile.Resolution));
                    draws += RecordCasters(commands, group, scene, resources, writeInstances);
                    commands.EndRendering();
                    using ISilkGraphicsSubmission submission = _device.Submit(commands);
                    submission.Wait();
                }
                catch (Exception error)
                {
                    if (material is not null && resources.StreamsMaterialTextures)
                    {
                        try
                        {
                            resources.ReleaseStreamedTextureWorkingSet();
                        }
                        catch (Exception cleanup)
                        {
                            throw new AggregateException(
                                "Shadow rendering and streamed texture cleanup failed.", error, cleanup);
                        }
                    }
                    throw;
                }
                if (material is not null && resources.StreamsMaterialTextures)
                {
                    resources.ReleaseStreamedTextureWorkingSet();
                }
            }
        }
        if (atlas.HasTransmission)
        {
            draws += RenderTransmission(
                scene, resources, descriptors, layout, dependencies, atlas, writeInstances, useSceneMaterials);
        }
        return draws;
    }

    private int RenderTransmission(
        SilkSceneState scene,
        SilkSceneGpuResources resources,
        IReadOnlyList<SilkShadowDescriptor> descriptors,
        SilkShadowAtlasLayout layout,
        HashSet<SilkSceneGpuResources.TextureDependency> dependencies,
        SilkShadowAtlasResource atlas,
        Func<SilkMeshGpuGeometryResource, IReadOnlyList<SilkShadowCaster>, ISilkGraphicsBuffer>? writeInstances,
        bool useSceneMaterials)
    {
        ISilkGraphicsTexture probe = atlas.Probe!;
        ISilkGraphicsTexture depth = atlas.PeelDepth!;
        int byteCount = checked((int)(atlas.Width * atlas.Height * 4));
        using IDisposable? pixelsReservation =
            (_device as ISilkCpuTextureAdmissionDevice)?.CpuTextureBudget?.Reserve((ulong)byteCount);
        byte[] pixels = new byte[byteCount];
        using (ISilkGraphicsCommandList clear = _device.CreateCommandList())
        {
            foreach (ISilkGraphicsTexture layer in atlas.Layers)
            {
                clear.ClearColor(layer, new SilkColor(-1, 1, 0, 0));
            }
            using ISilkGraphicsSubmission submission = _device.Submit(clear);
            submission.Wait();
        }

        int draws = 0;
        var casters = new List<SilkShadowCaster>();
        for (int slot = 0; slot < descriptors.Count; slot++)
        {
            SilkShadowDescriptor descriptor = descriptors[slot];
            casters.Clear();
            CollectCasters(scene, resources, descriptor, _device.ClipSpaceYPointsDown,
                casters, [], useSceneMaterials, transmissionOnly: true);
            if (casters.Count == 0)
            {
                continue;
            }
            SilkShadowTilePlacement tile = layout.Tiles[slot];
            for (int layer = 0; layer <= atlas.Layers.Length; layer++)
            {
                bool overflow = layer == atlas.Layers.Length;
                ISilkGraphicsTexture previous = layer == 0 ? atlas.Layers[^1] : atlas.Layers[layer - 1];
                ISilkGraphicsTexture target = overflow ? probe : atlas.Layers[layer];
                using (ISilkGraphicsCommandList clear = _device.CreateCommandList())
                {
                    clear.ClearDepth(depth, 1);
                    if (overflow)
                    {
                        clear.ClearColor(probe, new SilkColor(0, 0, 0, 0));
                    }
                    using ISilkGraphicsSubmission submission = _device.Submit(clear);
                    submission.Wait();
                }
                draws += renderTransmissionPass(
                    layer == 0 ? SilkShadowTransmissionPass.FirstPeel : SilkShadowTransmissionPass.Peel,
                    target, previous, depth, tile, casters);
                if (!overflow)
                {
                    using ISilkGraphicsCommandList clear = _device.CreateCommandList();
                    clear.ClearColor(probe, new SilkColor(0, 0, 0, 0));
                    using ISilkGraphicsSubmission submission = _device.Submit(clear);
                    submission.Wait();
                    draws += renderTransmissionPass(
                        SilkShadowTransmissionPass.EqualDepthProbe, probe, target, depth, tile, casters);
                }
                probe.ReadbackForTesting(pixels);
                try
                {
                    SilkShadowTransmissionValidation.ValidateProbe(pixels, atlas.Width, tile, descriptor.LightIndex,
                        layer, overflow);
                }
                catch (InvalidDataException failure)
                {
                    throw new SilkShadowTransmissionException(failure);
                }
            }
        }
        return draws;

        int renderTransmissionPass(
            SilkShadowTransmissionPass pass,
            ISilkGraphicsTexture target,
            ISilkGraphicsTexture source,
            ISilkGraphicsTexture depthTarget,
            SilkShadowTilePlacement tile,
            List<SilkShadowCaster> casters)
        {
            int recorded = 0;
            foreach (IGrouping<string, SilkShadowCaster> group in casters.GroupBy(
                static caster => caster.MaskMaterial!.Path, StringComparer.Ordinal))
            {
                SilkMaterialData material = group.First().MaskMaterial!;
                bool texturedOpacity = material.GetRenderableTexture(SilkMaterialParameter.Opacity) is not null;
                try
                {
                    if (texturedOpacity)
                    {
                        resources.PrepareShadowOpacityTextures(material, dependencies);
                    }
                    using ISilkGraphicsCommandList commands = _device.CreateCommandList();
                    commands.BeginRendering(new SilkRenderingDescriptor(target, depthTarget));
                    commands.SetViewport(new SilkViewport(
                        tile.PixelX, tile.PixelY, tile.Resolution, tile.Resolution));
                    commands.SetScissor(new SilkScissor(
                        (int)tile.PixelX, (int)tile.PixelY, tile.Resolution, tile.Resolution));
                    recorded += RecordCasters(
                        commands, [.. group], scene, resources, writeInstances, pass, source, target.Format);
                    commands.EndRendering();
                    using ISilkGraphicsSubmission submission = _device.Submit(commands);
                    submission.Wait();
                }
                catch (Exception failure)
                {
                    if (texturedOpacity && resources.StreamsMaterialTextures)
                    {
                        try
                        {
                            resources.ReleaseStreamedTextureWorkingSet();
                        }
                        catch (Exception cleanup)
                        {
                            throw new AggregateException(
                                "Transmittance rendering and streamed texture cleanup failed.", failure, cleanup);
                        }
                    }
                    throw;
                }
                if (texturedOpacity && resources.StreamsMaterialTextures)
                {
                    resources.ReleaseStreamedTextureWorkingSet();
                }
            }
            return recorded;
        }
    }

    private int RecordCasters(
        ISilkGraphicsCommandList commands,
        List<SilkShadowCaster> casters,
        SilkSceneState scene,
        SilkSceneGpuResources resources,
        Func<SilkMeshGpuGeometryResource, IReadOnlyList<SilkShadowCaster>, ISilkGraphicsBuffer>? writeInstances,
        SilkShadowTransmissionPass? transmissionPass = null,
        ISilkGraphicsTexture? previousLayer = null,
        SilkTextureFormat transmissionColorFormat = SilkTextureFormat.Rgba32Float)
    {
        int draws = 0;
        SilkMeshGpuGeometryResource? boundGeometry = null;
        var batches = new Dictionary<SilkMeshGpuGeometryResource, List<SilkShadowCaster>>();
        var order = new List<SilkMeshGpuGeometryResource>();
        foreach (SilkShadowCaster caster in casters)
        {
            if (!batches.TryGetValue(caster.Mesh.Geometry, out List<SilkShadowCaster>? batch))
            {
                batch = [];
                batches.Add(caster.Mesh.Geometry, batch);
                order.Add(caster.Mesh.Geometry);
            }
            batch.Add(caster);
        }

        foreach (SilkMeshGpuGeometryResource geometry in order)
        {
            List<SilkShadowCaster> batch = batches[geometry];
            SilkShadowCaster caster = batch[0];
            SilkMeshGpuResource first = caster.Mesh;
            bool constantOpacity = transmissionPass.HasValue &&
                (caster.MaskMaterial ?? throw new InvalidOperationException(
                    "A transmissive shadow caster requires a material."))
                    .GetRenderableTexture(SilkMaterialParameter.Opacity) is null;
            ISilkGraphicsPipeline pipeline = _pipelineCache.GetOrCreateShadowPipeline(
                first.VertexLayout,
                transmissionPass.HasValue ? transmissionColorFormat : SilkTextureFormat.Rgba8Unorm,
                SilkTextureFormat.D32Float,
                masked: caster.MaskMaterial is not null,
                transmissionPass: transmissionPass,
                constantOpacity: constantOpacity);
            commands.SetGraphicsPipeline(pipeline);
            if (pipeline is ISilkGraphicsPipelineLease lease)
            {
                lease.Dispose();
            }
            if (!ReferenceEquals(boundGeometry, geometry))
            {
                commands.SetVertexBuffer(resources.GetPreparedVertices(geometry));
                commands.SetIndexBuffer(first.IndexBuffer);
                boundGeometry = geometry;
            }
            commands.SetUniformBuffer(0, 0, first.UniformBuffer);
            ISilkGraphicsBuffer instances =
                writeInstances is null
                    ? geometry.RequireShadowInstanceBuffer(_device, batch) : writeInstances(geometry, batch);
            commands.SetStorageBuffer(0, 6, instances);
            if (previousLayer is not null)
            {
                commands.SetTexture(0, SilkBindingLayoutDescriptor.ShadowTransmissionTextureBinding, previousLayer);
            }
            if (caster.MaskMaterial is { } material)
            {
                commands.SetStorageBuffer(
                    0,
                    SilkBindingLayoutDescriptor.SurfaceParametersBinding,
                    resources.RequireSurfaceBuffer(
                        scene,
                        caster.Data ?? throw new InvalidOperationException("The masked caster has no mesh data."),
                        RenderHeadlight.Deterministic));
                if (!constantOpacity)
                {
                    resources.BindShadowOpacityTextures(commands, material);
                }
            }
            if (batch.Count == 1)
            {
                commands.DrawIndexed(first.IndexCount);
            }
            else
            {
                commands.DrawIndexedInstanced(first.IndexCount, checked((uint)batch.Count));
            }
            draws++;
        }
        return draws;
    }

    private static void CollectCasters(
        SilkSceneState scene,
        SilkSceneGpuResources resources,
        SilkShadowDescriptor descriptor,
        bool flipClipSpaceY,
        List<SilkShadowCaster> casters,
        List<string> unsupportedCasters,
        bool useSceneMaterials,
        bool transmissionOnly = false)
    {
        int lightIndex = (int)descriptor.LightIndex;
        foreach ((ulong id, SilkMeshData data) in scene.Meshes)
        {
            SilkMeshGpuResource mesh = resources.GetPreparedMesh(id);
            // Screen-space primitives cannot occlude; shadow links independently restrict casters.
            if (!IsPotentialCaster(scene, mesh, data, lightIndex))
            {
                continue;
            }
            SilkShadowOpacityCoverage coverage = ClassifyOpacity(scene, data, useSceneMaterials);
            if (coverage is SilkShadowOpacityCoverage.Invisible or SilkShadowOpacityCoverage.Unsupported)
            {
                if (coverage == SilkShadowOpacityCoverage.Unsupported && !unsupportedCasters.Contains(data.Path))
                {
                    unsupportedCasters.Add(data.Path);
                }
                continue;
            }
            if ((coverage == SilkShadowOpacityCoverage.Transmissive) != transmissionOnly)
            {
                continue;
            }

            casters.Add(new SilkShadowCaster(
                mesh,
                // The caster is rasterized with the device's own clip-space
                // convention, exactly as the colour pass is: Vulkan's clip Y
                // points down, so an unmirrored caster matrix would store the map
                // upside down relative to Direct3D and Metal while the colour pass
                // reconstructs the atlas coordinate with one convention on every
                // backend. Mirroring here is what makes the stored map identical
                // on all three, and it is invisible to a Y-symmetric scene, which
                // is why it needs asymmetric evidence.
                SilkShadowMatrix.CreateObjectToLightClip(
                    descriptor,
                    data.Transform.Span,
                    flipClipSpaceY),
                coverage is SilkShadowOpacityCoverage.Masked or SilkShadowOpacityCoverage.Transmissive
                    ? scene.Materials[data.MaterialPath] : null,
                data));
        }
    }

    internal static bool CanCastShadow(
        SilkSceneState scene, SilkMeshGpuResource resource, SilkMeshData mesh, int lightIndex,
        bool useSceneMaterials = true) =>
        IsPotentialCaster(scene, resource, mesh, lightIndex) &&
        ClassifyOpacity(scene, mesh, useSceneMaterials) is
            SilkShadowOpacityCoverage.Solid or SilkShadowOpacityCoverage.Masked or
                SilkShadowOpacityCoverage.Transmissive;

    private static bool RequiresTransmission(SilkSceneState scene, bool useSceneMaterials)
    {
        IReadOnlyList<SilkShadowDescriptor> descriptors = scene.Shadows.Descriptors;
        foreach (SilkMeshData mesh in scene.MeshValues)
        {
            if (mesh.TopologyKind != SilkTopologyKind.TriangleList ||
                ClassifyOpacity(scene, mesh, useSceneMaterials) != SilkShadowOpacityCoverage.Transmissive)
            {
                continue;
            }
            for (int index = 0; index < descriptors.Count; index++)
            {
                if (scene.LightLinks.Resolve(mesh.Path, mesh.InstanceIndex)
                    .CastsShadow((int)descriptors[index].LightIndex))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool IsPotentialCaster(
        SilkSceneState scene, SilkMeshGpuResource resource, SilkMeshData mesh, int lightIndex) =>
        resource.IndexCount != 0 && mesh.TopologyKind == SilkTopologyKind.TriangleList &&
        scene.LightLinks.Resolve(mesh.Path, mesh.InstanceIndex).CastsShadow(lightIndex);

    /// <summary>
    /// Classifies material opacity for solid, cutout and bounded transmission shadows.
    /// </summary>
    internal static SilkShadowOpacityCoverage ClassifyOpacity(
        SilkSceneState scene, SilkMeshData mesh, bool useSceneMaterials = true)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(mesh);
        if (!useSceneMaterials)
        {
            return SilkShadowOpacityCoverage.Solid;
        }
        if (string.IsNullOrEmpty(mesh.MaterialPath) ||
            !scene.Materials.TryGetValue(mesh.MaterialPath, out SilkMaterialData? material))
        {
            return SilkShadowOpacityCoverage.Solid;
        }

        if (material.SurfaceKind == SilkSurfaceKind.VolumeDensity)
        {
            return SilkShadowOpacityCoverage.Unsupported;
        }

        if (material.Textures.Any(static texture =>
            texture.Parameter == SilkMaterialParameter.Opacity &&
            texture.SourceKind != SilkTextureSourceKind.Standard))
        {
            return SilkShadowOpacityCoverage.Unsupported;
        }

        if (material.SurfaceKind == SilkSurfaceKind.PreviewSurface)
        {
            float constantOpacity = SilkSurfaceUniformWriter.GetOpacity(material);
            float constantThreshold = SilkSurfaceUniformWriter.GetOpacityThreshold(material);
            if (material.GetRenderableTexture(SilkMaterialParameter.Opacity) is not null)
            {
                return constantThreshold > 0
                    ? SilkShadowOpacityCoverage.Masked
                    : SilkShadowOpacityCoverage.Transmissive;
            }
            if (constantThreshold > 0)
            {
                return constantOpacity < constantThreshold
                    ? SilkShadowOpacityCoverage.Invisible
                    : SilkShadowOpacityCoverage.Solid;
            }
            return constantOpacity <= 0 ? SilkShadowOpacityCoverage.Invisible :
                constantOpacity < 1 ? SilkShadowOpacityCoverage.Transmissive : SilkShadowOpacityCoverage.Solid;
        }

        if (material.GetRenderableTexture(SilkMaterialParameter.Opacity) is not null)
        {
            return SilkShadowOpacityCoverage.Unsupported;
        }
        ReadOnlySpan<float> threshold =
            material.GetScalar(SilkMaterialParameter.OpacityThreshold);
        if (!threshold.IsEmpty && threshold[0] > 0)
        {
            return SilkShadowOpacityCoverage.Unsupported;
        }
        ReadOnlySpan<float> opacity = material.GetScalar(SilkMaterialParameter.Opacity);
        return !opacity.IsEmpty && opacity[0] < 1
            ? SilkShadowOpacityCoverage.Unsupported
            : SilkShadowOpacityCoverage.Solid;
    }

    private ISilkGraphicsSampler RequireSampler() =>
        _sampler ??= _device.CreateSampler(SilkSamplerDescriptor.NearestClamp);

    private void ValidateTransmissionWorkspace(uint edge, bool transmission, bool allocateCandidate)
    {
        if (!transmission && _atlas?.HasTransmission != true && _spareAtlas?.HasTransmission != true)
        {
            return;
        }
        ulong retainedBytes = checked((_atlas?.PayloadBytes ?? 0) + (_spareAtlas?.PayloadBytes ?? 0));
        try
        {
            SilkShadowTransmissionWorkspace.Validate(_device, retainedBytes, edge, transmission, allocateCandidate);
        }
        catch (InvalidOperationException failure)
        {
            throw new SilkShadowTransmissionException(failure);
        }
    }

    private ISilkGraphicsTexture EnsureStandIn()
    {
        if (_standIn is not null)
        {
            return _standIn;
        }

        // Cleared once, in a submission of its own, because the colour pass binds
        // it inside a rendering scope and an uninitialized depth image is not
        // something a backend should be asked to sample even when the shader never
        // does.
        ISilkGraphicsTexture standIn =
            _device.CreateTexture2D(SilkTextureDescriptor.SampledDepthTarget(1, 1));
        try
        {
            using ISilkGraphicsCommandList commands = _device.CreateCommandList();
            commands.ClearDepth(standIn, 1f);
            using ISilkGraphicsSubmission submission = _device.Submit(commands);
            submission.Wait();
        }
        catch
        {
            standIn.Dispose();
            throw;
        }

        _standIn = standIn;
        return standIn;
    }

    private void ReleaseAtlas()
    {
        if (_atlas is null && _layout is null)
        {
            return;
        }

        SilkShadowAtlasResource? atlas = _atlas;
        _atlas = null;
        _layout = null;
        _renderedKey = null;
        _renderedDependencies = [];
        _binding = SilkShadowFrameBinding.None;
        _bindingRevision++;
        atlas?.Dispose();
    }

    private void ReleaseSpareAtlas()
    {
        SilkShadowAtlasResource? spare = _spareAtlas;
        _spareAtlas = null;
        spare?.Dispose();
    }

    private ulong ReadDeviceGeneration() => SilkDeviceGeneration.Read(_device);

    /// <summary>
    /// The inputs a rendered atlas is a function of. A change in any of them
    /// re-renders it; none of them changing reuses it byte for byte.
    /// </summary>
    /// <remarks>
    /// The material revision is here because caster selection reads the material:
    /// an opacity-masked prim is excluded from the map, and a material can turn
    /// masked or opaque without any mesh command, so a key without it would reuse
    /// a map rendered from the opposite caster set and keep a diagnostic that no
    /// longer applies.
    ///
    /// The deformation revision is here for the same class of reason. A consumer
    /// that evaluates the published bounded rig instead of the CPU-resolved
    /// points has its pose in the rig, and a rig can change while every other
    /// input this key holds is unchanged; without it a shadow map would keep the
    /// previous pose while the colour pass drew the new one.
    ///
    /// The device generation is the combined one every reset a backend reports
    /// advances -- device loss detected on any submission, not only on one that
    /// belonged to the picking or selection-outline subsystem. An ordinary
    /// colour or shadow submission that loses the device drops every image the
    /// device held, including this atlas, without either subsystem generation
    /// moving; keying on one of them alone would reuse a map whose texture the
    /// reset destroyed.
    ///
    /// Material mode and the conservative world-space displacement padding also
    /// affect the caster set and projection without changing the published scene.
    /// </remarks>
    private readonly record struct RenderKey(
        ulong ShadowRevision,
        ulong GeometryRevision,
        ulong MaterialRevision,
        ulong LinkRevision,
        ulong DeformationRevision,
        ulong DeviceGeneration,
        ulong TextureRetryRevision,
        bool UseSceneMaterials,
        double DisplacementPadding);
}
