// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// One caster draw the depth-only shadow pass records.
/// </summary>
internal readonly record struct SilkShadowCaster(
    SilkMeshGpuResource Mesh,
    Matrix4x4 ObjectToLightClip);

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
            ReleaseAtlas();
            EnsureStandIn();
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
        var key = new RenderKey(
            scene.Shadows.Revision,
            scene.GeometryRevision,
            scene.MaterialRevision,
            scene.LightLinks.Revision,
            scene.DeformationRevision,
            generation);
        if (_atlas is not null &&
            _layout is not null &&
            _layout.Edge == layout.Edge &&
            _renderedKey == key)
        {
            return 0;
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
        SilkShadowAtlasLayout layout)
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
                _unsupportedCasters);

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
                commands.ClearDepth(_atlas!, 1f);
            }
            shadowCommands.BeginShadowRendering(new SilkShadowRenderingDescriptor(_atlas!));
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
            draws += RecordCasters(commands, casters);
            commands.EndRendering();
            using ISilkGraphicsSubmission submission = _device.Submit(commands);
            submission.Wait();
        }
        return draws;
    }

    private int RecordCasters(
        ISilkGraphicsCommandList commands,
        List<SilkShadowCaster> casters)
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
                commands.SetVertexBuffer(first.VertexBuffer);
                commands.SetIndexBuffer(first.IndexBuffer);
                boundGeometry = geometry;
            }
            commands.SetUniformBuffer(0, 0, first.UniformBuffer);
            ISilkGraphicsBuffer instances =
                geometry.RequireShadowInstanceBuffer(_device, batch);
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
        foreach (SilkMeshGpuResource mesh in resources.MeshValues)
        {
            if (mesh.IndexCount == 0 ||
                mesh.Mesh.TopologyKind != SilkTopologyKind.TriangleList)
            {
                // A screen-space line or point rasterizes at one pixel and has no
                // surface to occlude with, so admitting one into a shadow map would
                // publish a single-texel artefact rather than an occluder.
                continue;
            }

            // UsdLux collection:shadowLink is a caster restriction, and this is
            // where it is applied: a prim the light's shadow collection excludes is
            // simply not drawn into that light's map. It is resolved independently
            // of the light mask, so a prim the light does not illuminate still casts
            // its shadow when the caster collection includes it.
            if (!scene.LightLinks.Resolve(mesh.Mesh.Path, mesh.Mesh.InstanceIndex)
                .CastsShadow(lightIndex))
            {
                continue;
            }

            // An opacity-masked caster's depth coverage is not its geometric
            // coverage, and the depth-only program binds no material and cannot
            // discard, so drawing it would cast the solid shadow of a cutout card.
            // Dropping and naming it is the one honest option available here.
            if (IsOpacityMasked(scene, mesh.Mesh))
            {
                if (!unsupportedCasters.Contains(mesh.Mesh.Path))
                {
                    unsupportedCasters.Add(mesh.Mesh.Path);
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
                    mesh.Mesh.Transform.Span,
                    flipClipSpaceY)));
        }
    }

    /// <summary>
    /// Reports whether a caster's material discards or blends fragments, so that its
    /// depth coverage is not the coverage of its geometry.
    /// </summary>
    internal static bool IsOpacityMasked(SilkSceneState scene, SilkMeshData mesh)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(mesh);
        if (string.IsNullOrEmpty(mesh.MaterialPath) ||
            !scene.Materials.TryGetValue(mesh.MaterialPath, out SilkMaterialData? material))
        {
            return false;
        }

        if (material.GetTexture(SilkMaterialParameter.Opacity) is not null)
        {
            return true;
        }
        ReadOnlySpan<float> threshold =
            material.GetScalar(SilkMaterialParameter.OpacityThreshold);
        if (!threshold.IsEmpty && threshold[0] > 0)
        {
            return true;
        }
        ReadOnlySpan<float> opacity = material.GetScalar(SilkMaterialParameter.Opacity);
        return !opacity.IsEmpty && opacity[0] < 1;
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
