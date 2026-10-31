// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// One caster draw the depth-only shadow pass records.
/// </summary>
internal readonly record struct SilkShadowCaster(
    SilkMeshGpuResource Mesh,
    Matrix4x4 ObjectToLightClip);

internal enum SilkShadowOpacityCoverage
{
    Solid,
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
/// produced from changes -- the published descriptor table, the caster geometry,
/// the caster restriction, or the device generation -- and reused byte for byte
/// otherwise.
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
    private ISilkGraphicsTexture? _atlas;
    private ISilkGraphicsTexture? _spareAtlas;
    private ISilkGraphicsTexture? _standIn;
    private ISilkGraphicsSampler? _sampler;
    private SilkShadowAtlasLayout? _layout;
    private SilkShadowFrameBinding _binding = SilkShadowFrameBinding.None;
    private readonly List<string> _unsupportedCasters = [];
    private ulong _casterReportRevision;
    private RenderKey? _renderedKey;
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

    internal PreparedCapacity? PrepareCapacity(SilkSceneState scene, bool prepareRendering = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!UsesAdmission)
        {
            return null;
        }
        ulong generation = ReadDeviceGeneration();
        SynchronizeGeneration(generation);
        IReadOnlyList<SilkShadowDescriptor> descriptors = scene.Shadows.Descriptors;
        bool enabled = _device.Capabilities.SupportsRasterShadows && descriptors.Count > 0;
        bool clearAtlas = !enabled &&
            (_atlas is not null || _spareAtlas is not null || _layout is not null || _unsupportedCasters.Count > 0);
        if (!enabled && scene.Meshes.Count == 0 && !clearAtlas)
        {
            return null;
        }
        uint edge = enabled ? SilkShadowAtlasLayout.Create(descriptors)!.Edge : 0;
        bool renderNeeded = enabled &&
            (_atlas is null || _layout?.Edge != edge || _renderedKey != CreateKey(scene, generation));
        bool atlasNeeded = renderNeeded &&
            (_spareAtlas is null || _spareAtlas.Width != edge || _spareAtlas.Height != edge);
        bool standInNeeded = !enabled && scene.Meshes.Count > 0 && _standIn is null;
        bool samplerNeeded = scene.Meshes.Count > 0 && _sampler is null;
        if (!atlasNeeded && !standInNeeded && !samplerNeeded && !(prepareRendering && renderNeeded) && !clearAtlas)
        {
            return null;
        }
        var prepared = new PreparedCapacity(this, prepareRendering && renderNeeded, clearAtlas);
        try
        {
            prepared.Allocate(atlasNeeded ? edge : 0, standInNeeded, samplerNeeded);
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

    internal sealed class PreparedCapacity(SilkShadowMapCache owner, bool renderNeeded, bool clearAtlas) : IDisposable
    {
        private readonly ISilkGraphicsTexture? _previousAtlas = owner._atlas;
        private readonly ISilkGraphicsTexture? _previousSpare = owner._spareAtlas;
        private readonly ISilkGraphicsTexture? _previousStandIn = owner._standIn;
        private readonly ISilkGraphicsSampler? _previousSampler = owner._sampler;
        private readonly SilkShadowAtlasLayout? _previousLayout = owner._layout;
        private readonly SilkShadowFrameBinding _previousBinding = owner._binding;
        private readonly RenderKey? _previousKey = owner._renderedKey;
        private readonly ulong _previousBindingRevision = owner._bindingRevision;
        private readonly ulong _previousRenderCount = owner.RenderCount;
        private readonly string[] _previousUnsupported = [.. owner._unsupportedCasters];
        private readonly ulong _previousReportRevision = owner._casterReportRevision;
        private readonly ulong? _previousGeneration = owner._resourceGeneration;
        private ISilkGraphicsTexture? _atlas;
        private ISilkGraphicsTexture? _standIn;
        private ISilkGraphicsSampler? _sampler;
        private ISilkGraphicsTexture? _retiredSpare;
        private ISilkGraphicsTexture? _retiredAtlas;
        private bool _borrowedAtlas;
        private SilkShadowAtlasLayout? _layout;
        private SilkShadowFrameBinding? _binding;
        private RenderKey? _key;
        private readonly List<string> _unsupported = [];
        private bool _committed;
        private bool _activated;
        private bool _disposed;

        internal void Allocate(uint edge, bool standIn, bool sampler)
        {
            if (edge != 0)
            {
                _atlas = owner._device.CreateTexture2D(SilkTextureDescriptor.SampledDepthTarget(edge, edge));
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
            Func<SilkMeshGpuGeometryResource, IReadOnlyList<SilkShadowCaster>, ISilkGraphicsBuffer> writeInstances)
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
            SilkShadowAtlasLayout layout = SilkShadowAtlasLayout.Create(scene.Shadows.Descriptors)!;
            SilkShadowFrameBinding binding = SilkShadowFrameBinding.Create(scene.Shadows.Descriptors, layout);
            _ = owner.Render(scene, resources, scene.Shadows.Descriptors, layout, _atlas, _unsupported, writeInstances);
            owner._unsupportedCasters.EnsureCapacity(_unsupported.Count);
            _layout = layout;
            _binding = binding;
            _key = CreateKey(scene, owner.ReadDeviceGeneration());
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
                owner._binding = SilkShadowFrameBinding.None;
                owner._bindingRevision++;
                owner._unsupportedCasters.Clear();
                owner._casterReportRevision++;
            }
            if (_layout is not null)
            {
                ISilkGraphicsTexture? previous = owner._atlas;
                if (!ReferenceEquals(owner._spareAtlas, _atlas))
                {
                    _retiredSpare = owner._spareAtlas;
                }
                owner._atlas = _atlas;
                _atlas = null;
                owner._spareAtlas = previous is not null &&
                    previous.Width == _layout.Edge && previous.Height == _layout.Edge ? previous : null;
                if (!ReferenceEquals(previous, owner._spareAtlas))
                {
                    _retiredAtlas = previous;
                }
                owner._layout = _layout;
                owner._binding = _binding!;
                owner._renderedKey = _key;
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
            if (failures is not null)
            {
                throw new AggregateException("Prepared shadow capacity could not all be released.", failures);
            }
        }

        private void ReleaseCandidate(ISilkGraphicsTexture? texture, ref List<Exception>? failures)
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

    private static RenderKey CreateKey(SilkSceneState scene, ulong generation) => new(
        scene.Shadows.Revision, scene.GeometryRevision, scene.MaterialRevision,
        scene.LightLinks.Revision, scene.DeformationRevision, generation);

    /// <summary>
    /// Renders every shadow map the scene describes, reusing the retained atlas
    /// when nothing it was produced from has changed.
    /// </summary>
    /// <returns>The number of caster draws recorded, which is zero on a reuse.</returns>
    internal int Prepare(SilkSceneState scene, SilkSceneGpuResources resources)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(resources);

        IReadOnlyList<SilkShadowDescriptor> descriptors = scene.Shadows.Descriptors;

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
        RenderKey key = CreateKey(scene, generation);
        if (_atlas is not null &&
            _layout is not null &&
            _layout.Edge == layout.Edge &&
            _renderedKey == key)
        {
            resources.ReportUnsupportedShadowCasters(_unsupportedCasters, _casterReportRevision);
            return 0;
        }

        if (UsesAdmission)
        {
            return PrepareCandidate(scene, resources, descriptors, layout, key);
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
            _atlas = _device.CreateTexture2D(
                SilkTextureDescriptor.SampledDepthTarget(layout.Edge, layout.Edge));
        }

        _layout = layout;
        _binding = SilkShadowFrameBinding.Create(descriptors, layout);
        _bindingRevision++;
        _unsupportedCasters.Clear();
        int draws = Render(scene, resources, descriptors, layout);
        resources.ReportUnsupportedShadowCasters(_unsupportedCasters, ++_casterReportRevision);
        _renderedKey = key;
        RenderCount++;
        return draws;
    }

    private int PrepareCandidate(
        SilkSceneState scene,
        SilkSceneGpuResources resources,
        IReadOnlyList<SilkShadowDescriptor> descriptors,
        SilkShadowAtlasLayout layout,
        RenderKey key)
    {
        ISilkGraphicsTexture? previous = _atlas;
        SilkShadowAtlasLayout? previousLayout = _layout;
        SilkShadowFrameBinding previousBinding = _binding;
        RenderKey? previousKey = _renderedKey;
        ulong previousBindingRevision = _bindingRevision;
        ulong previousCasterRevision = _casterReportRevision;
        ISilkGraphicsSampler? previousSampler = _sampler;
        string[] previousUnsupported = [.. _unsupportedCasters];
        if (_spareAtlas is not null &&
            (_spareAtlas.Width != layout.Edge || _spareAtlas.Height != layout.Edge))
        {
            ReleaseSpareAtlas();
        }
        bool reused = _spareAtlas is not null;
        ISilkGraphicsTexture? candidate = null;
        int draws;
        try
        {
            candidate = _spareAtlas ??
                _device.CreateTexture2D(SilkTextureDescriptor.SampledDepthTarget(layout.Edge, layout.Edge));
            _spareAtlas = null;
            RequireSampler();
            _atlas = candidate;
            _layout = layout;
            _binding = SilkShadowFrameBinding.Create(descriptors, layout);
            _unsupportedCasters.Clear();
            draws = Render(scene, resources, descriptors, layout);
            resources.ReportUnsupportedShadowCasters(_unsupportedCasters, ++_casterReportRevision);
            _renderedKey = key;
            _bindingRevision++;
            RenderCount++;
        }
        catch (Exception failure)
        {
            _atlas = previous;
            _layout = previousLayout;
            _binding = previousBinding;
            _renderedKey = previousKey;
            _bindingRevision = previousBindingRevision;
            _casterReportRevision = previousCasterRevision;
            _unsupportedCasters.Clear();
            _unsupportedCasters.AddRange(previousUnsupported);
            ISilkGraphicsSampler? pendingSampler = ReferenceEquals(_sampler, previousSampler) ? null : _sampler;
            _sampler = previousSampler;
            if (reused)
            {
                _spareAtlas = candidate;
            }
            List<Exception>? cleanupFailures = null;
            if (!reused)
            {
                DisposeResource(candidate, ref cleanupFailures);
            }
            DisposeResource(pendingSampler, ref cleanupFailures);
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
        if (previous is not null && previous.Width == layout.Edge && previous.Height == layout.Edge)
        {
            _spareAtlas = previous;
        }
        else
        {
            previous?.Dispose();
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
            _atlas ?? EnsureStandIn());
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
        ISilkGraphicsTexture? candidate = null,
        List<string>? unsupported = null,
        Func<SilkMeshGpuGeometryResource, IReadOnlyList<SilkShadowCaster>, ISilkGraphicsBuffer>? writeInstances = null)
    {
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
                unsupported ?? _unsupportedCasters);

            // Each slot is its own submission, because a caster's light-space
            // transform is uploaded into the geometry's shadow instance buffer and
            // a second slot would overwrite it before the first slot's draws ran.
            // Four submissions is the whole ABI budget, and a frame that reuses its
            // maps records none of them.
            using ISilkGraphicsCommandList commands = _device.CreateCommandList();
            var shadowCommands = commands as ISilkShadowGraphicsCommandList ??
                throw new InvalidOperationException(
                    "A raster-shadow-capable device must create shadow-capable command lists.");
            if (slot == 0)
            {
                // One clear for the whole atlas, not one per tile: a tile whose
                // light casts nothing must still read as fully lit rather than as
                // whatever the previous frame left behind.
                commands.ClearDepth(candidate ?? _atlas!, 1f);
            }
            shadowCommands.BeginShadowRendering(new SilkShadowRenderingDescriptor(candidate ?? _atlas!));
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
            draws += RecordCasters(commands, casters, resources, writeInstances);
            commands.EndRendering();
            using ISilkGraphicsSubmission submission = _device.Submit(commands);
            submission.Wait();
        }
        return draws;
    }

    private int RecordCasters(
        ISilkGraphicsCommandList commands,
        List<SilkShadowCaster> casters,
        SilkSceneGpuResources resources,
        Func<SilkMeshGpuGeometryResource, IReadOnlyList<SilkShadowCaster>, ISilkGraphicsBuffer>? writeInstances)
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
            SilkMeshGpuResource first = batch[0].Mesh;
            ISilkGraphicsPipeline pipeline = _pipelineCache.GetOrCreateShadowPipeline(
                first.VertexLayout,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureFormat.D32Float);
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
        List<string> unsupportedCasters)
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

            SilkShadowOpacityCoverage coverage = ClassifyOpacity(scene, data);
            if (coverage != SilkShadowOpacityCoverage.Solid)
            {
                if (coverage == SilkShadowOpacityCoverage.Unsupported && !unsupportedCasters.Contains(data.Path))
                {
                    unsupportedCasters.Add(data.Path);
                }
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
                    flipClipSpaceY)));
        }
    }

    internal static bool CanCastShadow(
        SilkSceneState scene, SilkMeshGpuResource resource, SilkMeshData mesh, int lightIndex) =>
        IsPotentialCaster(scene, resource, mesh, lightIndex) &&
        ClassifyOpacity(scene, mesh) == SilkShadowOpacityCoverage.Solid;

    private static bool IsPotentialCaster(
        SilkSceneState scene, SilkMeshGpuResource resource, SilkMeshData mesh, int lightIndex) =>
        resource.IndexCount != 0 && mesh.TopologyKind == SilkTopologyKind.TriangleList &&
        scene.LightLinks.Resolve(mesh.Path, mesh.InstanceIndex).CastsShadow(lightIndex);

    /// <summary>
    /// Classifies constant PreviewSurface cutoffs separately from unsupported variable coverage.
    /// </summary>
    internal static SilkShadowOpacityCoverage ClassifyOpacity(SilkSceneState scene, SilkMeshData mesh)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(mesh);
        if (string.IsNullOrEmpty(mesh.MaterialPath) ||
            !scene.Materials.TryGetValue(mesh.MaterialPath, out SilkMaterialData? material))
        {
            return SilkShadowOpacityCoverage.Solid;
        }

        if (material.SurfaceKind == SilkSurfaceKind.PreviewSurface)
        {
            float constantOpacity = SilkSurfaceUniformWriter.GetOpacity(material);
            float constantThreshold = SilkSurfaceUniformWriter.GetOpacityThreshold(material);
            if (material.GetTexture(SilkMaterialParameter.Opacity) is not null)
            {
                return SilkShadowOpacityCoverage.Unsupported;
            }
            if (constantThreshold > 0)
            {
                return constantOpacity < constantThreshold
                    ? SilkShadowOpacityCoverage.Invisible
                    : SilkShadowOpacityCoverage.Solid;
            }
            return constantOpacity < 1 ? SilkShadowOpacityCoverage.Unsupported : SilkShadowOpacityCoverage.Solid;
        }

        if (material.GetTexture(SilkMaterialParameter.Opacity) is not null)
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

        ISilkGraphicsTexture? atlas = _atlas;
        _atlas = null;
        _layout = null;
        _renderedKey = null;
        _binding = SilkShadowFrameBinding.None;
        _bindingRevision++;
        atlas?.Dispose();
    }

    private void ReleaseSpareAtlas()
    {
        ISilkGraphicsTexture? spare = _spareAtlas;
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
    /// </remarks>
    private readonly record struct RenderKey(
        ulong ShadowRevision,
        ulong GeometryRevision,
        ulong MaterialRevision,
        ulong LinkRevision,
        ulong DeformationRevision,
        ulong DeviceGeneration);
}
