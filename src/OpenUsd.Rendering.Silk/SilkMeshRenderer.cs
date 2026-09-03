// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Renders retained hdSilk scene pages into backend-neutral color and depth targets.
/// </summary>
public interface ISilkRenderTargetRenderer : IDisposable
{
    /// <summary>Gets the immutable retained renderer-neutral selection.</summary>
    SelectionState Selection { get; }

    /// <summary>Gets the current shared selection-outline settings.</summary>
    SilkSelectionOutlineSettings SelectionOutlineSettings { get; }

    /// <summary>Gets the device's visible-only and x-ray capability.</summary>
    SilkSelectionOutlineCapabilities SelectionOutlineCapabilities { get; }

    /// <summary>Gets cumulative selection resolution, pass, and resource evidence.</summary>
    SilkSelectionOutlineDiagnostics SelectionOutlineDiagnostics { get; }

    /// <summary>
    /// Replaces immutable selected identities without synchronizing hdSilk or
    /// uploading scene geometry.
    /// </summary>
    void UpdateSelection(
        SelectionState selection,
        SilkSelectionOutlineSettings? settings = null);

    /// <summary>Applies a page and renders the retained scene.</summary>
    SilkMeshRenderResult ApplyAndRender(
        OpenUsdSilkPage page,
        ISilkGraphicsTexture colorTarget,
        ISilkGraphicsTexture depthTarget,
        SilkMeshRenderOptions? options = null);

    /// <summary>
    /// Applies a page, renders, and services queued picks with exact revision binding.
    /// </summary>
    SilkMeshRenderResult ApplyAndRender(
        OpenUsdSilkPage page,
        ISilkGraphicsTexture colorTarget,
        ISilkGraphicsTexture depthTarget,
        SilkPickFrameBinding pickBinding,
        SilkMeshRenderOptions? options = null);

    /// <summary>Renders the currently retained scene without synchronizing it.</summary>
    SilkMeshRenderResult Render(
        ISilkGraphicsTexture colorTarget,
        ISilkGraphicsTexture depthTarget,
        SilkMeshRenderOptions? options = null);

    /// <summary>
    /// Renders retained data and services queued picks with exact revision binding.
    /// </summary>
    SilkMeshRenderResult Render(
        ISilkGraphicsTexture colorTarget,
        ISilkGraphicsTexture depthTarget,
        SilkPickFrameBinding pickBinding,
        SilkMeshRenderOptions? options = null);
}

/// <summary>Clear values used by <see cref="SilkMeshRenderer"/>.</summary>
public sealed record SilkMeshRenderOptions(
    SilkColor ClearColor,
    float ClearDepth,
    bool BackfaceCulling = true,
    bool UseSceneMaterials = true)
{
    /// <summary>Gets opaque black with a far depth clear.</summary>
    public static SilkMeshRenderOptions Default { get; } =
        new(new SilkColor(0, 0, 0, 1), 1);

    /// <summary>Gets or initializes the output transform applied before writing display pixels.</summary>
    public RenderOutputTransform OutputTransform { get; init; }

    /// <summary>Gets or initializes the exposure adjustment in stops.</summary>
    public float Exposure { get; init; }

    /// <summary>
    /// Gets or initializes an optional colour-managed display transform applied by a
    /// fullscreen pass, or <see langword="null"/> to use <see cref="OutputTransform"/>.
    /// </summary>
    /// <remarks>
    /// When set, the scene is drawn into a renderer-owned linear RGBA16Float
    /// intermediate with no output transform and no exposure, and one fullscreen pass
    /// applies exposure and then the colour-managed transform into the caller's target.
    /// <see cref="OutputTransform"/> must stay
    /// <see cref="RenderOutputTransform.Identity"/>, because a built-in transform
    /// alongside this one would convert the image twice.
    /// </remarks>
    public RenderDisplayTransform? DisplayTransform { get; init; }
}

/// <summary>Per-frame retained-scene rendering evidence.</summary>
public readonly record struct SilkMeshRenderResult(
    int DrawCount,
    int UniformUploads,
    SilkSceneGpuStatistics Statistics);

/// <summary>Cumulative bounded Silk pick queue, pass, and readback evidence.</summary>
public readonly record struct SilkPickRendererStatistics(
    ulong Requests,
    ulong SupersededRequests,
    ulong PassesRecorded,
    ulong RingSaturations,
    ulong Hits,
    ulong Misses,
    ulong StaleResults,
    ulong UnsupportedResults,
    ulong PipelineCreations,
    ulong TargetCreations,
    int QueuedRequests,
    int InFlightReadbacks,
    ulong RefusedSubprimTargets = 0,
    SilkSubprimUnsupportedReason LastRefusedSubprimReason =
        SilkSubprimUnsupportedReason.None);

/// <summary>
/// Owns retained mesh buffers and the checked mesh graphics pipeline for one RHI device.
/// </summary>
public sealed class SilkMeshRenderer :
    ISilkRenderTargetRenderer,
    IRenderPickingBackend
{
    private readonly object _gate = new();
    private readonly ISilkGraphicsDevice _device;
    private readonly SilkGraphicsPipelineCache _pipelineCache;
    private readonly SilkShadowMapCache _shadowMaps;
    private readonly SilkDisplayTransformPass _displayTransform;
    private int _deformationDispatches;
    private readonly SilkProjectedMaterialShaderGenerator _materialShaderGenerator;
    private readonly SilkMaterialShaderCompilerService _materialShaderCompiler;
    private readonly SilkShaderBinaryFormat _shaderFormat;
    private readonly ISilkPickingGraphicsDevice? _pickingDevice;
    private readonly ISilkSelectionOutlineGraphicsDevice? _selectionOutlineDevice;
    // The batch table is rebuilt from scratch every frame rather than accumulated, because a
    // BatchKey holds the geometry resource by reference: deformable geometry produces a new
    // resource whenever its points change, so a table that only cleared its lists would keep one
    // dead key per deformed frame, hold the disposed resource alive, and lengthen the per-frame
    // sweep forever. The lists themselves are pooled, so rebuilding costs no steady-state
    // allocation.
    private readonly Dictionary<BatchKey, List<SilkMeshGpuResource>> _batches = [];
    private readonly List<List<SilkMeshGpuResource>> _batchPool = [];
    private readonly List<BatchKey> _batchOrder = [];

    // How many batches of the frame being recorded have already drawn each
    // geometry. It is the ordinal of the retained instance-transform slot the
    // next such batch writes, which is what keeps two batches of one geometry
    // from sharing a mutable table across a single submission.
    private readonly Dictionary<SilkMeshGpuGeometryResource, int> _instanceSlots = [];
    // Pick pipelines are keyed by (topology, vertex stride, depth bias, colour
    // write). The subprim overlay pass rasterizes the same vertices through the
    // same checked fragment stage as the surface pass; only the assembled
    // primitive and the coincident depth bias differ. The bias is part of the
    // key because both stages draw line and point lists: a whole basis-curve or
    // point resource is drawn unbiased by the surface pass, and an authored mesh
    // edge or point is drawn biased by the overlay pass, so a key without it
    // would hand one of them the other's pipeline. The colour-write flag is part
    // of the key because a face request draws curves and point clouds as pure
    // occluders. The stride is part of the key because a textured or
    // normal-mapped mesh has a 32- or 48-byte vertex, and a pipeline pinned to
    // the 24-byte layout would read every vertex after the first from the wrong
    // offset and pick a different surface from the one on screen. All of them
    // are created on first use and retired together with the device generation.
    private readonly Dictionary<
        (SilkPickPrimitiveTopology Topology,
            uint Stride,
            SilkPickDepthBias DepthBias,
            bool ColorWrite),
        ISilkPickGraphicsPipeline> _pickPipelines = [];
    private SilkPickReadbackRing? _pickReadbacks;
    private PendingPick?[]? _inFlightPicks;
    // The index buffers one in-flight subprim pass draws from. They are owned by
    // the ring slot rather than cached per mesh, so the extra resources a
    // subprim pick costs are bounded by the ring capacity and are released the
    // moment the readback completes, is discarded, or the device is lost.
    private List<ISilkGraphicsBuffer>?[]? _inFlightPickBuffers;
    private ISilkGraphicsTexture? _pickColorTarget;
    private ISilkGraphicsTexture? _pickDepthTarget;
    private PendingPick? _activePick;
    private PendingPick? _pendingPick;
    private ulong _pickDeviceGeneration;
    private uint _pickTargetWidth;
    private uint _pickTargetHeight;
    private ulong _pickRequests;
    private ulong _pickSupersededRequests;
    private ulong _pickPassesRecorded;
    private ulong _pickRingSaturations;
    private ulong _pickHits;
    private ulong _pickMisses;
    private ulong _pickStaleResults;
    private ulong _pickUnsupportedResults;
    private ulong _pickPipelineCreations;
    private ulong _pickTargetCreations;
    private ulong _pickRefusedSubprimTargets;
    private SilkSubprimUnsupportedReason _pickLastRefusedReason;
    private SelectionState _selection = SelectionState.Empty;
    private SilkSelectionOutlineSettings _selectionOutlineSettings =
        SilkSelectionOutlineSettings.Default;
    private int _selectionItemCount;
    private SelectedMeshDraw[] _selectedMeshes = [];
    private readonly List<ISilkGraphicsBuffer> _scopedSelectionBuffers = [];
    private int _selectedMeshCount;
    private int _missingSelectionPathCount;
    private bool _selectionResolutionDirty = true;
    private ulong _selectionResolvedGpuRevision = ulong.MaxValue;
    private ulong _selectionRevision;
    private SilkSelectionOutlineStatus _selectionOutlineStatus =
        SilkSelectionOutlineStatus.EmptySelection;
    // Mask pipelines are keyed by (depth-tested, topology, stride, stage): the
    // visible-only composite needs a depth-tested mask, the x-ray composite an
    // untested one, and a selection scoped to a face, an edge or a point needs
    // the matching topology. The stage is part of the key because both stages
    // rasterize line and point lists: a whole basis-curve or point resource is
    // masked unbiased, and a selected authored mesh edge or point is masked
    // through the coincident separation. Each is created on first use and all
    // are retired together with the device generation, so a session that selects
    // only whole prims creates exactly the one pipeline it always did.
    private readonly Dictionary<(bool DepthTested,
        SilkSelectionMaskPrimitiveTopology Topology,
        uint Stride,
        SilkSelectionMaskStage Stage),
        ISilkSelectionMaskGraphicsPipeline> _selectionMaskPipelines = [];
    private ISilkSelectionOutlineGraphicsPipeline? _selectionOutlinePipeline;
    private ISilkGraphicsSampler? _selectionOutlineSampler;
    private ISilkGraphicsBuffer? _selectionOutlineParameters;
    private readonly byte[] _selectionOutlineParameterBytes =
        new byte[SilkSelectionOutlineUniformWriter.ByteSize];
    private bool _selectionOutlineParametersInitialized;
    private ISilkGraphicsTexture? _selectionMaskTarget;
    private ISilkGraphicsTexture? _selectionBoundDepthTarget;
    private ISilkSelectionOutlineBinding? _selectionOutlineBinding;
    private ulong _selectionOutlineDeviceGeneration;
    private SilkTextureFormat _selectionOutlineColorFormat;
    private bool _selectionOutlineInfrastructureInitialized;
    private ulong _selectionMaskPasses;
    private ulong _selectionOutlinePasses;
    private ulong _selectionDraws;
    private ulong _selectionPipelineCreations;
    private ulong _selectionTargetCreations;
    private ulong _selectionBindingCreations;
    private ulong _selectionParameterUploads;
    private ulong _selectionDeviceInvalidations;
    private ulong _unsupportedXRayRequests;
    private bool _disposed;

    /// <summary>Initializes a retained renderer using checked shaders for the device backend.</summary>
    public SilkMeshRenderer(ISilkGraphicsDevice device)
        : this(device, GetShaderFormat(device))
    {
    }

    /// <summary>
    /// Initializes a retained renderer using checked shaders for the device backend, with
    /// explicit decoded CPU and estimated GPU texture cache residency budgets.
    /// </summary>
    /// <param name="device">The backend graphics device.</param>
    /// <param name="textureResidencyOptions">
    /// The decoded CPU and estimated GPU texture cache residency budgets to enforce after each
    /// completed submission.
    /// </param>
    public SilkMeshRenderer(ISilkGraphicsDevice device, SilkTextureResidencyOptions textureResidencyOptions)
        : this(
            device,
            GetShaderFormat(device),
            imageDecoder: null,
            udimResolver: null,
            RequireResidencyOptions(textureResidencyOptions))
    {
    }

    private static SilkTextureResidencyOptions RequireResidencyOptions(
        SilkTextureResidencyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options;
    }

    internal SilkMeshRenderer(
        ISilkGraphicsDevice device,
        SilkShaderBinaryFormat shaderFormat,
        Func<string, bool, SilkDecodedImage>? imageDecoder = null,
        Func<string, IReadOnlyList<SilkUdimTile>>? udimResolver = null,
        SilkTextureResidencyOptions? textureResidencyOptions = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _shaderFormat = shaderFormat;
        _pickingDevice = device as ISilkPickingGraphicsDevice;
        _selectionOutlineDevice = device as ISilkSelectionOutlineGraphicsDevice;
        Scene = new SilkSceneState();
        GpuResources = imageDecoder is null
            ? textureResidencyOptions is null
                ? new SilkSceneGpuResources(device)
                : new SilkSceneGpuResources(device, textureResidencyOptions)
            : new SilkSceneGpuResources(device, imageDecoder, udimResolver, textureResidencyOptions);

        ISilkPickGraphicsPipeline? pickPipeline = null;
        SilkPickReadbackRing? pickReadbacks = null;
        try
        {
            if (_pickingDevice is not null)
            {
                // Only the surface pipeline for the default 24-byte vertex is
                // created up front. Every other topology and stride is created
                // the first time a pick actually asks for one, so a session that
                // picks only untextured prims pays exactly what it always paid.
                pickPipeline = CreatePickPipeline(
                    _pickingDevice,
                    shaderFormat,
                    SilkPickPrimitiveTopology.TriangleList,
                    SilkVertexLayoutDescriptor.PositionNormal,
                    SilkPickDepthBias.None);
                pickReadbacks = new SilkPickReadbackRing(_pickingDevice);
            }
        }
        catch
        {
            pickReadbacks?.Dispose();
            pickPipeline?.Dispose();
            GpuResources.Dispose();
            throw;
        }

        _pipelineCache = new SilkGraphicsPipelineCache(device, shaderFormat);
        _shadowMaps = new SilkShadowMapCache(device, _pipelineCache);
        _displayTransform = new SilkDisplayTransformPass(device, shaderFormat);
        _materialShaderGenerator = new SilkProjectedMaterialShaderGenerator();
        _materialShaderCompiler = new SilkMaterialShaderCompilerService(_materialShaderGenerator);
        if (pickPipeline is not null)
        {
            _pickPipelines[(
                SilkPickPrimitiveTopology.TriangleList,
                SilkVertexLayoutDescriptor.PositionNormal.Stride,
                SilkPickDepthBias.None,
                true)] = pickPipeline;
        }
        _pickReadbacks = pickReadbacks;
        if (pickReadbacks is not null)
        {
            _inFlightPicks = new PendingPick?[pickReadbacks.Capacity];
            _inFlightPickBuffers =
                new List<ISilkGraphicsBuffer>?[pickReadbacks.Capacity];
            _pickDeviceGeneration = pickReadbacks.DeviceGeneration;
            _pickPipelineCreations = 1;
        }
    }

    private static ISilkPickGraphicsPipeline CreatePickPipeline(
        ISilkPickingGraphicsDevice device,
        SilkShaderBinaryFormat shaderFormat,
        SilkPickPrimitiveTopology topology,
        SilkVertexLayoutDescriptor vertexLayout,
        SilkPickDepthBias depthBias,
        bool colorWriteEnabled = true)
    {
        SilkPickPipelineDescriptor descriptor =
            SilkPickPipelineDescriptor.CreateChecked(
                shaderFormat,
                topology,
                vertexLayout,
                depthBias,
                colorWriteEnabled);
        descriptor.Validate();
        return device.CreatePickGraphicsPipeline(descriptor);
    }

    /// <summary>
    /// Gets the pick pipeline for one topology, mesh vertex layout, pass stage,
    /// and colour-write policy, creating it on first use.
    /// </summary>
    private ISilkPickGraphicsPipeline EnsurePickPipeline(
        SilkPickPrimitiveTopology topology,
        SilkVertexLayoutDescriptor vertexLayout,
        SilkPickDepthBias depthBias,
        bool colorWriteEnabled = true)
    {
        if (_pickPipelines.TryGetValue(
                (topology, vertexLayout.Stride, depthBias, colorWriteEnabled),
                out ISilkPickGraphicsPipeline? existing))
        {
            return existing;
        }

        ISilkPickingGraphicsDevice device = _pickingDevice ??
            throw new InvalidOperationException(
                "The renderer has no pick-capable device.");
        ISilkPickGraphicsPipeline created = CreatePickPipeline(
            device,
            _shaderFormat,
            topology,
            vertexLayout,
            depthBias,
            colorWriteEnabled);
        _pickPipelines[(
            topology,
            vertexLayout.Stride,
            depthBias,
            colorWriteEnabled)] = created;
        _pickPipelineCreations++;
        return created;
    }

    /// <summary>Gets the retained CPU scene.</summary>
    public SilkSceneState Scene { get; }

    /// <summary>Gets the retained GPU resources and upload diagnostics.</summary>
    public SilkSceneGpuResources GpuResources { get; }

    /// <summary>
    /// Gets the number of times the retained shadow atlas has been rendered.
    /// </summary>
    /// <remarks>
    /// A frame that reuses its maps leaves this value where it was, which is what
    /// makes retention measurable rather than assumed.
    /// </remarks>
    internal ulong ShadowMapRenderCount => _shadowMaps.RenderCount;

    /// <summary>Gets the number of retained shadow maps.</summary>
    internal int ShadowMapCount => _shadowMaps.MapCount;

    /// <summary>
    /// Gets or sets the physics transform overrides applied to retained meshes for every rendered
    /// frame, or <see langword="null"/> to draw every mesh from its authored transform.
    /// </summary>
    /// <remarks>
    /// The renderer only reads the resolved override table; it never authors USD and never sees a
    /// simulation handle. Clearing the table, or setting this to <see langword="null"/>, restores
    /// the authored render state on the next rendered frame.
    /// </remarks>
    public SilkPhysicsTransformOverrides? PhysicsOverrides { get; set; }

    /// <summary>Gets or sets the deformable geometry currently driving retained meshes.</summary>
    /// <remarks>
    /// The batch is re-applied on every render, immediately after any authored scene page has been
    /// applied and before anything is drawn. That ordering is the whole point: the delegate
    /// republishes authored geometry on every page, so a deformation applied before the page would
    /// be overwritten by it and the frame would draw the rest pose. Re-applying also invalidates
    /// exactly the meshes whose points changed, which is what reaches the vertex buffers.
    /// </remarks>
    public SilkPhysicsDeformations? PhysicsDeformations { get; set; }

    /// <inheritdoc/>
    public SelectionState Selection
    {
        get
        {
            lock (_gate)
            {
                return _selection;
            }
        }
    }

    /// <inheritdoc/>
    public SilkSelectionOutlineSettings SelectionOutlineSettings
    {
        get
        {
            lock (_gate)
            {
                return _selectionOutlineSettings;
            }
        }
    }

    /// <inheritdoc/>
    public SilkSelectionOutlineCapabilities SelectionOutlineCapabilities
    {
        get
        {
            lock (_gate)
            {
                return _selectionOutlineDevice?.SelectionOutlineCapabilities ??
                    SilkSelectionOutlineCapabilities.Unsupported;
            }
        }
    }

    /// <summary>
    /// Gets cumulative colour-managed display-transform state and resource evidence.
    /// </summary>
    public SilkDisplayTransformDiagnostics DisplayTransformDiagnostics
    {
        get
        {
            lock (_gate)
            {
                return _displayTransform.Diagnostics;
            }
        }
    }

    /// <summary>
    /// Gets the latest bounded display-transform diagnostic, or <see langword="null"/>
    /// when the most recent frame either applied the requested transform or was not
    /// asked for one.
    /// </summary>
    public RenderDiagnostic? DisplayTransformDiagnostic
    {
        get
        {
            lock (_gate)
            {
                return _displayTransform.Diagnostic;
            }
        }
    }

    /// <inheritdoc/>
    public SilkSelectionOutlineDiagnostics SelectionOutlineDiagnostics
    {
        get
        {
            lock (_gate)
            {
                return new SilkSelectionOutlineDiagnostics(
                    _selectionOutlineStatus,
                    _selectionRevision,
                    _selectionItemCount,
                    _selectedMeshCount,
                    _missingSelectionPathCount,
                    _selectionMaskPasses,
                    _selectionOutlinePasses,
                    _selectionDraws,
                    _selectionPipelineCreations,
                    _selectionTargetCreations,
                    _selectionBindingCreations,
                    _selectionParameterUploads,
                    _selectionDeviceInvalidations,
                    _unsupportedXRayRequests);
            }
        }
    }

    /// <inheritdoc/>
    public void UpdateSelection(
        SelectionState selection,
        SilkSelectionOutlineSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(selection);
        lock (_gate)
        {
            ThrowIfDisposed();
            SilkSelectionOutlineSettings nextSettings =
                settings ?? _selectionOutlineSettings;
            if (_selection.Equals(selection) &&
                _selectionOutlineSettings == nextSettings)
            {
                return;
            }

            if (!_selection.Equals(selection))
            {
                _selection = selection;
                _selectionItemCount = selection.Items.Count;
                _selectionResolutionDirty = true;
                _selectedMeshCount = 0;
                _missingSelectionPathCount = 0;
            }
            _selectionOutlineSettings = nextSettings;
            _selectionRevision++;
            UpdateSelectionStatusBeforeRender();
        }
    }

    /// <summary>Gets cumulative bounded picking evidence.</summary>
    public SilkPickRendererStatistics PickingStatistics
    {
        get
        {
            lock (_gate)
            {
                int queued = (_activePick is null ? 0 : 1) +
                    (_pendingPick is null ? 0 : 1);
                return new SilkPickRendererStatistics(
                    _pickRequests,
                    _pickSupersededRequests,
                    _pickPassesRecorded,
                    _pickRingSaturations,
                    _pickHits,
                    _pickMisses,
                    _pickStaleResults,
                    _pickUnsupportedResults,
                    _pickPipelineCreations,
                    _pickTargetCreations,
                    queued,
                    _pickReadbacks?.InFlightCount ?? 0,
                    _pickRefusedSubprimTargets,
                    _pickLastRefusedReason);
            }
        }
    }

    /// <inheritdoc/>
    public ValueTask<RenderPickResult> PickAsync(
        RenderPickRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidatePickRequest(request);
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<RenderPickResult>(cancellationToken);
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            var pending = new PendingPick(request, cancellationToken);
            _pickRequests++;
            if (_activePick is null)
            {
                _activePick = pending;
            }
            else
            {
                PendingPick? superseded = _pendingPick;
                _pendingPick = pending;
                if (superseded is not null)
                {
                    superseded.CancelAsSuperseded();
                    _pickSupersededRequests++;
                }
            }
            return pending.AsValueTask();
        }
    }

    /// <summary>
    /// Synchronizes an hdSilk session for the target dimensions and renders its retained scene.
    /// </summary>
    public SilkMeshRenderResult SyncAndRender(
        OpenUsdSilkSession session,
        ISilkGraphicsTexture colorTarget,
        ISilkGraphicsTexture depthTarget,
        double timeCode,
        SilkMeshRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (_gate)
        {
            ThrowIfDisposed();
            ValidateTargets(colorTarget, depthTarget);
            using OpenUsdSilkPage page = session.Sync(
                checked((int)colorTarget.Width),
                checked((int)colorTarget.Height),
                timeCode,
                CameraState.Default);
            SilkSceneDelta delta = Scene.Apply(page);
            ApplySceneDelta(delta);
            return RenderCore(
                colorTarget,
                depthTarget,
                options ?? SilkMeshRenderOptions.Default,
                pickBinding: null);
        }
    }

    /// <summary>
    /// Synchronizes and renders while binding queued picks to exact renderer-neutral revisions.
    /// </summary>
    public SilkMeshRenderResult SyncAndRender(
        OpenUsdSilkSession session,
        ISilkGraphicsTexture colorTarget,
        ISilkGraphicsTexture depthTarget,
        double timeCode,
        SilkPickFrameBinding pickBinding,
        SilkMeshRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (_gate)
        {
            ThrowIfDisposed();
            ValidateTargets(colorTarget, depthTarget);
            using OpenUsdSilkPage page = session.Sync(
                checked((int)colorTarget.Width),
                checked((int)colorTarget.Height),
                timeCode,
                CameraState.Default);
            SilkSceneDelta delta = Scene.Apply(page);
            ApplySceneDelta(delta);
            return RenderCore(
                colorTarget,
                depthTarget,
                options ?? SilkMeshRenderOptions.Default,
                pickBinding);
        }
    }

    /// <inheritdoc/>
    public SilkMeshRenderResult ApplyAndRender(
        OpenUsdSilkPage page,
        ISilkGraphicsTexture colorTarget,
        ISilkGraphicsTexture depthTarget,
        SilkMeshRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        lock (_gate)
        {
            ThrowIfDisposed();
            SilkSceneDelta delta = Scene.Apply(page);
            ApplySceneDelta(delta);
            return RenderCore(
                colorTarget,
                depthTarget,
                options ?? SilkMeshRenderOptions.Default,
                pickBinding: null);
        }
    }

    /// <summary>
    /// Applies a page and renders while binding queued picks to exact revisions.
    /// </summary>
    public SilkMeshRenderResult ApplyAndRender(
        OpenUsdSilkPage page,
        ISilkGraphicsTexture colorTarget,
        ISilkGraphicsTexture depthTarget,
        SilkPickFrameBinding pickBinding,
        SilkMeshRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        lock (_gate)
        {
            ThrowIfDisposed();
            SilkSceneDelta delta = Scene.Apply(page);
            ApplySceneDelta(delta);
            return RenderCore(
                colorTarget,
                depthTarget,
                options ?? SilkMeshRenderOptions.Default,
                pickBinding);
        }
    }

    /// <inheritdoc/>
    public SilkMeshRenderResult Render(
        ISilkGraphicsTexture colorTarget,
        ISilkGraphicsTexture depthTarget,
        SilkMeshRenderOptions? options = null)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            return RenderCore(
                colorTarget,
                depthTarget,
                options ?? SilkMeshRenderOptions.Default,
                pickBinding: null);
        }
    }

    internal IDisposable AcquireDisplayCaptureLease()
    {
        Monitor.Enter(_gate);
        try
        {
            ThrowIfDisposed();
            return new DisplayCaptureLease(_gate);
        }
        catch
        {
            Monitor.Exit(_gate);
            throw;
        }
    }

    /// <summary>Renders retained data and services at most one queued pick pass.</summary>
    public SilkMeshRenderResult Render(
        ISilkGraphicsTexture colorTarget,
        ISilkGraphicsTexture depthTarget,
        SilkPickFrameBinding pickBinding,
        SilkMeshRenderOptions? options = null)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            return RenderCore(
                colorTarget,
                depthTarget,
                options ?? SilkMeshRenderOptions.Default,
                pickBinding);
        }
    }

    internal SilkMeshRenderResult ApplyAndRenderForDisplayCapture(
        OpenUsdSilkPage page,
        ISilkGraphicsTexture colorTarget,
        ISilkGraphicsTexture depthTarget,
        SilkMeshRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(page);
        lock (_gate)
        {
            ThrowIfDisposed();
            SilkSceneDelta delta = Scene.Apply(page);
            ApplySceneDelta(delta);
            return RenderCore(
                colorTarget,
                depthTarget,
                options,
                pickBinding: null,
                renderSelectionOutline: false);
        }
    }

    internal SilkMeshRenderResult RenderForDisplayCapture(
        ISilkGraphicsTexture colorTarget,
        ISilkGraphicsTexture depthTarget,
        SilkMeshRenderOptions options)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            return RenderCore(
                colorTarget,
                depthTarget,
                options,
                pickBinding: null,
                renderSelectionOutline: false);
        }
    }

    internal bool TryRenderDisplaySelectionOutline(
        ISilkGraphicsTexture colorTarget,
        ISilkGraphicsTexture depthTarget)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            ValidateTargets(colorTarget, depthTarget);
            if (!PrepareSelectionOutline(colorTarget, depthTarget))
            {
                return false;
            }

            using ISilkGraphicsCommandList commands = _device.CreateCommandList();
            ISilkSelectionOutlineGraphicsCommandList selectionCommands =
                commands as ISilkSelectionOutlineGraphicsCommandList ??
                throw new InvalidOperationException(
                    "A selection-outline-capable device must create " +
                    "selection-outline-capable command lists.");
            RecordSelectionOutline(
                commands,
                selectionCommands,
                colorTarget,
                depthTarget);
            using ISilkGraphicsSubmission submission = _device.Submit(commands);
            submission.Wait();
            return true;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            var disposed = new ObjectDisposedException(nameof(SilkMeshRenderer));
            _activePick?.Fail(disposed);
            _pendingPick?.Fail(disposed);
            _activePick = null;
            _pendingPick = null;
            if (_pickReadbacks is not null && _inFlightPicks is not null)
            {
                while (_pickReadbacks.TryDiscard(
                    out int slotIndex,
                    out _))
                {
                    _inFlightPicks[slotIndex]?.Fail(disposed);
                    _inFlightPicks[slotIndex] = null;
                    ReleaseInFlightPickBuffers(slotIndex);
                }
            }
            if (_inFlightPickBuffers is not null)
            {
                for (int slot = 0; slot < _inFlightPickBuffers.Length; slot++)
                {
                    ReleaseInFlightPickBuffers(slot);
                }
            }
            _pickDepthTarget?.Dispose();
            _pickColorTarget?.Dispose();
            _pickReadbacks?.Dispose();
            foreach (ISilkPickGraphicsPipeline retired in _pickPipelines.Values)
            {
                retired.Dispose();
            }
            _pickPipelines.Clear();
            DisposeSelectionOutlineInfrastructure();
            _displayTransform.Dispose();
            _shadowMaps.Dispose();
            GpuResources.Dispose();
            _materialShaderCompiler.Dispose();
            _pipelineCache.Dispose();

            // Batch keys reference geometry resources the GPU scene has just disposed, so the table
            // is emptied here rather than left holding them for the lifetime of the renderer.
            _batches.Clear();
            _batchPool.Clear();
            _batchOrder.Clear();
        }
    }

    private SilkMeshRenderResult RenderCore(
        ISilkGraphicsTexture displayTarget,
        ISilkGraphicsTexture depthTarget,
        SilkMeshRenderOptions options,
        SilkPickFrameBinding? pickBinding,
        bool renderSelectionOutline = true)
    {
        ValidateTargets(displayTarget, depthTarget);
        ValidateOptions(options);
        SyncPhysicsDeformations();
        int uniformUploads = GpuResources.UpdateUniforms(Scene.Frame, PhysicsOverrides);
        // A colour-managed display transform moves the scene off the caller's target and
        // into a renderer-owned linear intermediate of the same size. Everything below
        // this point draws the scene into `colorTarget`, which is that intermediate when
        // the transform is active and the caller's target when it is not; the fullscreen
        // transform, the selection composite, and picking all keep using `displayTarget`,
        // because those act on the finished display image.
        ISilkGraphicsTexture colorTarget = displayTarget;
        bool displayTransformActive = false;
        RenderOutputTransform sceneOutputTransform = options.OutputTransform;
        float sceneExposure = options.Exposure;
        if (options.DisplayTransform is { } requestedDisplayTransform)
        {
            displayTransformActive = _displayTransform.TryPrepare(
                requestedDisplayTransform,
                displayTarget,
                options.Exposure,
                out ISilkGraphicsTexture sceneIntermediate);
            if (displayTransformActive)
            {
                colorTarget = sceneIntermediate;
                sceneOutputTransform = RenderOutputTransform.Identity;
                sceneExposure = 0;
            }
        }
        else
        {
            _displayTransform.MarkInactive();
        }
        // Every deformed geometry whose pose has not reached its vertex buffer
        // is dispatched here, on its own submitted command list, before the
        // shadow maps are prepared. Both the shadow depth pass and the colour
        // pass fetch the same vertex buffers, and the shadow cache submits its
        // own command list that this renderer does not compose, so ordering by
        // submission is what makes one dispatch serve both.
        _deformationDispatches = GpuResources.DispatchDeformations(ReadDeviceGeneration());
        // Once per frame, before anything branches on whether there is a drawable
        // mesh. The link table's revision retires the diagnostics and the per-mask
        // surface blocks a previous table produced, and a frame that draws nothing
        // has to do that too -- otherwise a stage whose prims were all removed
        // keeps warning about a table it no longer retains.
        GpuResources.ObserveLightLinks(Scene);
        // The shadow maps are rendered from light space before anything reads
        // them, and before the frame constants are packed, because those constants
        // carry the atlas tiles and light-space matrices this pass just resolved.
        // A scene that publishes no descriptor records nothing here at all.
        _shadowMaps.Prepare(Scene, GpuResources);
        // The prefiltered environment is resolved next and for the same reason:
        // the frame constants carry whether it is live and how many prefiltered
        // roughness levels it has, and the mean-radiance ambient term they also
        // carry is only the domes this step did *not* take.
        GpuResources.PrepareEnvironmentLighting(Scene);
        ISilkGraphicsBuffer frameBuffer = GpuResources.RequireFrameBuffer(
            Scene,
            sceneOutputTransform,
            sceneExposure,
            _shadowMaps.Binding,
            _shadowMaps.BindingRevision);
        bool shouldRenderSelectionOutline =
            renderSelectionOutline &&
            PrepareSelectionOutline(displayTarget, depthTarget);
        using ISilkGraphicsCommandList commands = _device.CreateCommandList();

        // Any upload still marked pending here was recorded into a command list
        // that never completed -- a submission that failed, or a frame that threw
        // between recording and submitting. Its copies are gone with that list, so
        // the marks are dropped before this frame records anything, and the upload
        // is recorded again rather than skipped against a texture nothing wrote.
        GpuResources.AbandonPendingUploads();
        ISilkDisplayTransformGraphicsCommandList? displayTransformCommands = null;
        if (displayTransformActive)
        {
            displayTransformCommands =
                commands as ISilkDisplayTransformGraphicsCommandList ??
                throw new InvalidOperationException(
                    "A display-transform-capable device must create " +
                    "display-transform-capable command lists.");
        }
        ISilkSelectionOutlineGraphicsCommandList? selectionCommands = null;
        if (shouldRenderSelectionOutline)
        {
            selectionCommands = commands as ISilkSelectionOutlineGraphicsCommandList ??
                throw new InvalidOperationException(
                    "A selection-outline-capable device must create " +
                    "selection-outline-capable command lists.");
        }
        // The batch table is released before anything branches on the frame's shape, so a frame
        // that takes the single-mesh fast path - or that has no drawable mesh at all - still drops
        // the keys the previous frame produced. Leaving that inside the grouped branch meant a
        // scene that shrank to one mesh kept the whole multi-mesh table, and every geometry
        // resource it named, for the life of the renderer.
        foreach (List<SilkMeshGpuResource> batch in _batches.Values)
        {
            batch.Clear();
            _batchPool.Add(batch);
        }
        _batches.Clear();
        _batchOrder.Clear();
        _instanceSlots.Clear();

        SilkMeshGpuResource? singleMesh = null;
        if (GpuResources.MeshValues.Count == 1)
        {
            foreach (SilkMeshGpuResource mesh in GpuResources.MeshValues)
            {
                singleMesh = mesh.IndexCount == 0 ? null : mesh;
            }
        }

        SilkShaderFeatures resolveMaterialFeatures(SilkMeshData mesh) =>
            options.UseSceneMaterials ? GetMaterialFeatures(mesh) : SilkShaderFeatures.None;

        string resolveMaterialShaderIdentity(SilkMeshData mesh) =>
            options.UseSceneMaterials ? GetMaterialShaderIdentity(mesh) : string.Empty;

        // A screen-space line or point has no facing, so no cull style can apply
        // to one. Resolving it to None here rather than letting the mesh's style
        // through keeps that invariant where the topology is known -- the
        // rasterizer would ignore the mode for these topologies anyway, but a
        // cull mode that varies with the authored style would fragment the
        // pipeline cache into states that draw identically.
        SilkCullMode resolveCullMode(SilkMeshData mesh) =>
            !options.BackfaceCulling ||
                mesh.TopologyKind != SilkTopologyKind.TriangleList
                ? SilkCullMode.None
                : GetCullMode(mesh);

        bool resolveSampledVolume(SilkMeshData mesh) =>
            options.UseSceneMaterials && IsSampledVolumeMesh(mesh);

        bool resolveTransparent(SilkMeshData mesh) =>
            options.UseSceneMaterials && IsTransparent(mesh);

        uint resolveLinkMasks(SilkMeshData mesh) =>
            SilkSceneGpuResources.PackLinkMasks(
                Scene.LightLinks.Resolve(mesh.Path, mesh.InstanceIndex));

        if (singleMesh is not null)
        {
            // The prefiltered environment is uploaded here, with the material
            // textures and outside any rendering scope, because a copy cannot be
            // recorded inside one on any backend. It is a no-op on every frame
            // that did not rebuild the maps.
            GpuResources.UploadEnvironment(commands);
            PrepareMaterialTextures(commands, singleMesh, resolveMaterialFeatures(singleMesh.Mesh));
        }

        if (singleMesh is null)
        {
            // Uploaded here for the same reason and at the same point as the
            // single-mesh path above: outside any rendering scope, and once per
            // rebuild rather than once per batch.
            GpuResources.UploadEnvironment(commands);
            // Dictionary.Clear above kept its capacity, so refilling it allocates nothing once the
            // scene has been drawn once.
            foreach (SilkMeshGpuResource mesh in GpuResources.MeshValues)
            {
                if (mesh.IndexCount == 0)
                {
                    continue;
                }
                bool transparent = resolveTransparent(mesh.Mesh);
                BatchKey key = new(
                    mesh.Geometry,
                    mesh.Mesh.MaterialPath,
                    resolveMaterialFeatures(mesh.Mesh),
                    resolveMaterialShaderIdentity(mesh.Mesh),
                    resolveSampledVolume(mesh.Mesh),
                    transparent,
                    transparent ? GetEyeDepth(mesh.Mesh, Scene.Frame) : 0,
                    transparent ? mesh.Mesh.StableHash : 0,
                    resolveCullMode(mesh.Mesh),
                    mesh.Mesh.TopologyKind,
                    resolveLinkMasks(mesh.Mesh));
                if (!_batches.TryGetValue(key, out List<SilkMeshGpuResource>? batch))
                {
                    batch = RentBatch();
                    _batches.Add(key, batch);
                    _batchOrder.Add(key);
                }
                batch.Add(mesh);
            }
            _batchOrder.Sort(CompareBatchKeys);
            foreach (BatchKey key in _batchOrder)
            {
                List<SilkMeshGpuResource> batch = _batches[key];
                PrepareMaterialTextures(commands, batch[0], key.Features);
            }
        }

        commands.ClearColor(
            colorTarget,
            SilkDisplayConverter.TransformColor(
                options.ClearColor,
                sceneOutputTransform,
                sceneExposure));
        commands.ClearDepth(depthTarget, options.ClearDepth);
        commands.BeginRendering(new SilkRenderingDescriptor(colorTarget, depthTarget));
        commands.SetViewport(new SilkViewport(
            0,
            0,
            colorTarget.Width,
            colorTarget.Height,
            0,
            1));
        commands.SetScissor(new SilkScissor(0, 0, colorTarget.Width, colorTarget.Height));

        int drawCount = 0;
        PipelineKey? boundPipeline = null;
        (string MaterialPath, uint LinkMasks)? boundSurface = null;
        if (singleMesh is not null)
        {
            SilkShaderFeatures features = resolveMaterialFeatures(singleMesh.Mesh);
            SilkMaterialShaderRequest? materialShader = options.UseSceneMaterials
                ? GetMaterialShaderRequest(singleMesh.Mesh, features)
                : null;
            SilkCullMode cullMode = resolveCullMode(singleMesh.Mesh);
            bool transparent = resolveTransparent(singleMesh.Mesh);
            ISilkGraphicsPipeline pipeline = GetPipeline(
                singleMesh,
                features,
                IsSampledVolumeMesh(singleMesh.Mesh),
                cullMode,
                singleMesh.Mesh.TopologyKind,
                colorTarget.Format,
                materialShader,
                transparent);
            commands.SetGraphicsPipeline(pipeline);
            DisposePipelineLease(pipeline);
            boundPipeline = new PipelineKey(
                features,
                IsSampledVolumeMesh(singleMesh.Mesh),
                cullMode,
                singleMesh.Mesh.TopologyKind,
                transparent,
                singleMesh.VertexLayout.Stride,
                GetPipelineShaderIdentity(materialShader));
            commands.SetVertexBuffer(singleMesh.VertexBuffer);
            commands.SetIndexBuffer(singleMesh.IndexBuffer);
            commands.SetUniformBuffer(0, 0, singleMesh.UniformBuffer);
            commands.SetStorageBuffer(0, 6, singleMesh.UniformBuffer);
            commands.SetStorageBuffer(
                0,
                SilkBindingLayoutDescriptor.FrameParametersBinding,
                frameBuffer);
            BindSurfaceBufferIfChanged(commands, singleMesh, ref boundSurface);
            BindMaterialResources(commands, singleMesh, features);
            commands.DrawIndexed(singleMesh.IndexCount);
            drawCount++;
            commands.EndRendering();
            if (displayTransformCommands is not null)
            {
                _displayTransform.Record(
                    commands,
                    displayTransformCommands,
                    displayTarget);
            }
            if (selectionCommands is not null)
            {
                RecordSelectionOutline(
                    commands,
                    selectionCommands,
                    displayTarget,
                    depthTarget);
            }

            using ISilkGraphicsSubmission singleSubmission = SubmitAndCommitUploads(commands);
            // Safe: Wait() returning means no unsubmitted or in-flight execution referencing
            // these textures remains, so completing this submission's lease makes disposing them
            // safe even though `commands` itself is still alive in this `using` scope. See
            // SilkSceneGpuResources.TrimTextureResidency.
            GpuResources.TrimTextureResidency();
            if (pickBinding is { } singleBinding)
            {
                ProcessPicking(displayTarget, singleBinding);
            }
            return new SilkMeshRenderResult(drawCount, uniformUploads, GpuResources.Statistics);
        }
        foreach (BatchKey key in _batchOrder)
        {
            List<SilkMeshGpuResource> meshes = _batches[key];
            SilkMeshGpuResource first = meshes[0];
            // A batch of one gains nothing from instancing and would cost an
            // instance storage buffer per unique geometry, which for a scene of
            // mostly distinct meshes is a pure allocation regression. The
            // per-mesh uniform path already carries the single transform.
            if (meshes.Count < 2)
            {
                BindPipelineIfChanged(
                    commands,
                    first,
                    key,
                    colorTarget.Format,
                    ref boundPipeline);
                commands.SetVertexBuffer(first.VertexBuffer);
                commands.SetIndexBuffer(first.IndexBuffer);
                foreach (SilkMeshGpuResource mesh in meshes)
                {
                    commands.SetUniformBuffer(0, 0, mesh.UniformBuffer);
                    // The vertex shader always reads its transform from the
                    // instance table, so bind this mesh's 80-byte uniform buffer
                    // there as a one-element table. Leaving it unbound worked on
                    // D3D12 and Vulkan by accident and rendered nothing on Metal.
                    commands.SetStorageBuffer(0, 6, mesh.UniformBuffer);
                    commands.SetStorageBuffer(
                        0,
                        SilkBindingLayoutDescriptor.FrameParametersBinding,
                        frameBuffer);
                    BindSurfaceBufferIfChanged(commands, mesh, ref boundSurface);
                    BindMaterialResources(commands, mesh, key.Features);
                    commands.DrawIndexed(mesh.IndexCount);
                    drawCount++;
                }
                continue;
            }

            // Every batch of this frame is recorded before any of them is
            // submitted, so a geometry split across several batches -- which is
            // exactly what a differing material, cull mode or UsdLux light,
            // shadow or dome mask produces -- must not share one mutable
            // transform table. The second batch would rewrite it while the first
            // batch's draw still referenced it, and both draws would read the
            // last batch's transforms: some instances drawn twice and others not
            // at all. Each batch is given its own retained slot instead, and the
            // slot ordinal is assigned in batch order so an unchanged scene keeps
            // writing the same slot and keeps the delta upload.
            if (!_instanceSlots.TryGetValue(key.Geometry, out int slot))
            {
                slot = 0;
            }
            _instanceSlots[key.Geometry] = slot + 1;
            key.Geometry.UpdateInstanceBuffer(
                _device,
                Scene.Frame,
                meshes,
                _device.ClipSpaceYPointsDown,
                slot);
            BindPipelineIfChanged(
                commands,
                first,
                key,
                colorTarget.Format,
                ref boundPipeline);
            commands.SetVertexBuffer(first.VertexBuffer);
            commands.SetIndexBuffer(first.IndexBuffer);
            commands.SetUniformBuffer(0, 0, first.UniformBuffer);
            commands.SetStorageBuffer(0, 6, key.Geometry.RequireInstanceBuffer(slot));
            commands.SetStorageBuffer(
                0,
                SilkBindingLayoutDescriptor.FrameParametersBinding,
                frameBuffer);
            BindSurfaceBufferIfChanged(commands, first, ref boundSurface);
            BindMaterialResources(commands, first, key.Features);
            commands.DrawIndexedInstanced(first.IndexCount, checked((uint)meshes.Count));
            drawCount++;
        }
        commands.EndRendering();
        if (displayTransformCommands is not null)
        {
            _displayTransform.Record(
                commands,
                displayTransformCommands,
                displayTarget);
        }
        if (selectionCommands is not null)
        {
            RecordSelectionOutline(
                commands,
                selectionCommands,
                displayTarget,
                depthTarget);
        }

        using ISilkGraphicsSubmission submission = SubmitAndCommitUploads(commands);
        // Safe: Wait() returning means no unsubmitted or in-flight execution referencing these
        // textures remains, so completing this submission's lease makes disposing them safe even
        // though `commands` itself is still alive in this `using` scope. See
        // SilkSceneGpuResources.TrimTextureResidency.
        GpuResources.TrimTextureResidency();
        if (pickBinding is { } binding)
        {
            ProcessPicking(displayTarget, binding);
        }
        return new SilkMeshRenderResult(drawCount, uniformUploads, GpuResources.Statistics);
    }

    /// <summary>
    /// Submits the recorded frame, waits for it, and only then marks every
    /// recorded upload as performed.
    /// </summary>
    /// <remarks>
    /// Recording a copy is not performing one. A submission that throws, or one
    /// whose wait throws, leaves the target textures holding whatever they held
    /// before -- so the recorded uploads are abandoned rather than committed, and
    /// the next frame records them again instead of binding memory nothing ever
    /// wrote.
    /// </remarks>
    private ISilkGraphicsSubmission SubmitAndCommitUploads(ISilkGraphicsCommandList commands)
    {
        ISilkGraphicsSubmission submission;
        try
        {
            submission = _device.Submit(commands);
        }
        catch
        {
            GpuResources.AbandonPendingUploads();
            throw;
        }

        try
        {
            submission.Wait();
        }
        catch
        {
            GpuResources.AbandonPendingUploads();
            submission.Dispose();
            throw;
        }

        GpuResources.CommitPendingUploads();
        return submission;
    }

    private void BindPipelineIfChanged(
        ISilkGraphicsCommandList commands,
        SilkMeshGpuResource mesh,
        BatchKey key,
        SilkTextureFormat colorFormat,
        ref PipelineKey? boundPipeline)
    {
        PipelineKey next = new(
            key.Features,
            key.SampledVolume,
            key.CullMode,
            key.TopologyKind,
            key.Transparent,
            mesh.VertexLayout.Stride,
            key.MaterialShaderIdentity);
        if (boundPipeline == next)
        {
            return;
        }

        ISilkGraphicsPipeline pipeline = GetPipeline(
            mesh,
            key.Features,
            key.SampledVolume,
            key.CullMode,
            key.TopologyKind,
            colorFormat,
            string.IsNullOrEmpty(key.MaterialShaderIdentity)
                ? null
                : GetMaterialShaderRequest(mesh.Mesh, key.Features),
            key.Transparent);
        commands.SetGraphicsPipeline(pipeline);
        DisposePipelineLease(pipeline);
        boundPipeline = next;
    }

    private void BindSurfaceBufferIfChanged(
        ISilkGraphicsCommandList commands,
        SilkMeshGpuResource mesh,
        ref (string MaterialPath, uint LinkMasks)? boundSurface)
    {
        (string MaterialPath, uint LinkMasks) next = (
            mesh.Mesh.MaterialPath,
            SilkSceneGpuResources.PackLinkMasks(
                Scene.LightLinks.Resolve(mesh.Mesh.Path, mesh.Mesh.InstanceIndex)));
        if (boundSurface is { } bound &&
            bound.LinkMasks == next.LinkMasks &&
            string.Equals(bound.MaterialPath, next.MaterialPath, StringComparison.Ordinal))
        {
            return;
        }

        commands.SetStorageBuffer(
            0,
            SilkBindingLayoutDescriptor.SurfaceParametersBinding,
            GpuResources.RequireSurfaceBuffer(Scene, mesh.Mesh, RenderHeadlight.Deterministic));
        boundSurface = next;
    }

    private static int CompareBatchKeys(BatchKey left, BatchKey right)
    {
        int result = left.Transparent.CompareTo(right.Transparent);
        if (result != 0)
        {
            return result;
        }
        if (left.Transparent)
        {
            result = left.EyeDepth.CompareTo(right.EyeDepth);
            if (result != 0)
            {
                return result;
            }
            result = left.SortIdentity.CompareTo(right.SortIdentity);
            if (result != 0)
            {
                return result;
            }
        }
        result = left.TopologyKind.CompareTo(right.TopologyKind);
        if (result != 0)
        {
            return result;
        }
        result = left.CullMode.CompareTo(right.CullMode);
        if (result != 0)
        {
            return result;
        }
        result = left.Features.CompareTo(right.Features);
        if (result != 0)
        {
            return result;
        }
        result = left.SampledVolume.CompareTo(right.SampledVolume);
        if (result != 0)
        {
            return result;
        }
        result = left.Geometry.VertexLayout.Stride.CompareTo(right.Geometry.VertexLayout.Stride);
        if (result != 0)
        {
            return result;
        }
        result = string.CompareOrdinal(left.MaterialPath, right.MaterialPath);
        if (result != 0)
        {
            return result;
        }
        result = string.CompareOrdinal(left.MaterialShaderIdentity, right.MaterialShaderIdentity);
        if (result != 0)
        {
            return result;
        }
        result = string.CompareOrdinal(left.Geometry.Key.Path, right.Geometry.Key.Path);
        if (result != 0)
        {
            return result;
        }
        result = left.Geometry.Key.TopologyFingerprint.CompareTo(
            right.Geometry.Key.TopologyFingerprint);
        if (result != 0)
        {
            return result;
        }
        return left.LinkMasks.CompareTo(right.LinkMasks);
    }

    /// <summary>
    /// Resolves Hydra's cull style onto a rasterizer cull mode.
    /// </summary>
    /// <remarks>
    /// The two "front" styles used to fall into a catch-all that culled *back*
    /// faces, so authoring <c>front</c> culled exactly the set of faces it asks
    /// to keep. The mapping is now total, and an unknown wire value falls back
    /// to Hydra's default rather than to a silently inverted one.
    /// </remarks>
    private static SilkCullMode GetCullMode(SilkMeshData mesh) =>
        mesh.CullStyle switch
        {
            SilkMeshCullStyle.Nothing => SilkCullMode.None,
            SilkMeshCullStyle.Back => SilkCullMode.Back,
            SilkMeshCullStyle.Front => SilkCullMode.Front,
            SilkMeshCullStyle.BackUnlessDoubleSided => mesh.DoubleSided ? SilkCullMode.None : SilkCullMode.Back,
            SilkMeshCullStyle.FrontUnlessDoubleSided => mesh.DoubleSided ? SilkCullMode.None : SilkCullMode.Front,
            _ => mesh.DoubleSided ? SilkCullMode.None : SilkCullMode.Back,
        };

    private bool IsTransparent(SilkMeshData mesh)
    {
        SilkMaterialData? material = ResolveMaterial(mesh);
        if (material is null)
        {
            return false;
        }
        ReadOnlySpan<float> threshold =
            material.GetScalar(SilkMaterialParameter.OpacityThreshold);
        if (!threshold.IsEmpty && threshold[0] > 0)
        {
            return false;
        }
        if (material.GetTexture(SilkMaterialParameter.Opacity) is not null)
        {
            return true;
        }
        ReadOnlySpan<float> opacity = material.GetScalar(SilkMaterialParameter.Opacity);
        return !opacity.IsEmpty && opacity[0] < 1;
    }

    private static float GetEyeDepth(SilkMeshData mesh, SilkFrameState frame)
    {
        ReadOnlySpan<double> transform = mesh.Transform.Span;
        ReadOnlySpan<double> view = frame.View.Span;
        double x = transform[12];
        double y = transform[13];
        double z = transform[14];
        return (float)((x * view[2]) + (y * view[6]) + (z * view[10]) + view[14]);
    }

    // Lines and points carry no facing, so those batches always use the
    // unculled pipeline for their topology regardless of authored cull style.
    private ISilkGraphicsPipeline GetPipeline(
        SilkMeshGpuResource mesh,
        SilkShaderFeatures features,
        bool sampledVolume,
        SilkCullMode cullMode,
        SilkTopologyKind topologyKind,
        SilkTextureFormat colorFormat,
        SilkMaterialShaderRequest? materialShader = null,
        bool transparent = false)
    {
        if (sampledVolume)
        {
            // The sampled density volume has exactly one checked fragment program, and
            // it is the only mesh fragment binary that declares the 3D density texture.
            // There is no permutation of it that also samples 2D material maps, and no
            // runtime material shader is generated for a volume surface. Falling through
            // to an ordinary mesh pipeline would raymarch nothing and shade the authored
            // uniform density instead of the grid: a plausible image that silently
            // ignores the volume, which is the exact failure the dedicated program
            // exists to prevent. Name the impossible combination instead.
            if (materialShader?.Status == SilkMaterialShaderStatus.Ready)
            {
                throw new InvalidDataException(
                    $"Mesh '{mesh.Mesh.Path}' binds a sampled density volume and a runtime " +
                    "material shader. hdSilk has no checked fragment program that samples " +
                    "both, and rendering it as an ordinary surface would silently drop the " +
                    "volume grid.");
            }
            if (features != SilkShaderFeatures.None)
            {
                throw new InvalidDataException(
                    $"Mesh '{mesh.Mesh.Path}' binds a sampled density volume together with " +
                    $"material texture features '{features}'. hdSilk has no checked fragment " +
                    "program that samples both, and rendering it as an ordinary surface " +
                    "would silently drop the volume grid.");
            }
            return _pipelineCache.GetOrCreateSampledVolumePipeline(
                mesh.VertexLayout,
                colorFormat,
                SilkTextureFormat.D32Float,
                cullMode,
                topologyKind,
                transparent ? SilkBlendMode.StraightAlphaOver : SilkBlendMode.None,
                depthWriteEnabled: !transparent);
        }
        if (materialShader?.Status == SilkMaterialShaderStatus.Ready)
        {
            return _pipelineCache.GetOrCreateMaterialPipeline(
                materialShader.Program,
                mesh.VertexLayout,
                colorFormat,
                SilkTextureFormat.D32Float,
                cullMode,
                topologyKind,
                transparent ? SilkBlendMode.StraightAlphaOver : SilkBlendMode.None,
                depthWriteEnabled: !transparent);
        }
        return _pipelineCache.GetOrCreateMeshPipelineWithState(
            new SilkShaderPermutationId(features),
            mesh.VertexLayout,
            colorFormat,
            SilkTextureFormat.D32Float,
            cullMode,
            topologyKind,
            transparent ? SilkBlendMode.StraightAlphaOver : SilkBlendMode.None,
            depthWriteEnabled: !transparent);
    }

    private static void DisposePipelineLease(ISilkGraphicsPipeline pipeline)
    {
        if (pipeline is ISilkGraphicsPipelineLease)
        {
            pipeline.Dispose();
        }
    }

    private SilkMaterialData? ResolveMaterial(SilkMeshData mesh)
    {
        if (string.IsNullOrEmpty(mesh.MaterialPath))
        {
            return null;
        }
        return Scene.Materials.TryGetValue(mesh.MaterialPath, out SilkMaterialData? material) &&
            material.IsSupported
            ? material
            : null;
    }

    private SilkShaderFeatures GetMaterialFeatures(SilkMeshData mesh) =>
        ResolveMaterial(mesh)?.GetTextureFeatures() ?? SilkShaderFeatures.None;

    private string GetMaterialShaderIdentity(SilkMeshData mesh)
    {
        SilkMaterialData? material = ResolveMaterial(mesh);
        if (material is null || !material.UsesRuntimeMaterialShader)
        {
            return string.Empty;
        }

        SilkMaterialShaderKey key = CreateMaterialShaderKey(material);
        return key.CacheHash;
    }

    private bool IsSampledVolumeMesh(SilkMeshData mesh) =>
        ResolveMaterial(mesh) is { SurfaceKind: SilkSurfaceKind.VolumeDensity } volumeMaterial &&
        volumeMaterial.GetTexture(SilkMaterialParameter.VolumeDensity) is not null &&
        _device is ISilkVolumeTextureGraphicsDevice;

    private SilkMaterialShaderRequest? GetMaterialShaderRequest(
        SilkMeshData mesh,
        SilkShaderFeatures features)
    {
        SilkMaterialData? material = ResolveMaterial(mesh);
        if (material is null || !material.UsesRuntimeMaterialShader)
        {
            return null;
        }

        SilkMaterialShaderKey key = CreateMaterialShaderKey(material);
        if (material.SurfaceKind == SilkSurfaceKind.MaterialXGenerated)
        {
            ReadOnlyMemory<byte> generatedFragment = _shaderFormat switch
            {
                SilkShaderBinaryFormat.Dxil => material.GeneratedFragmentSpirV,
                SilkShaderBinaryFormat.SpirV => material.GeneratedFragmentSpirV,
                SilkShaderBinaryFormat.MetalLibrary => material.GeneratedFragmentMslSource,
                _ => ReadOnlyMemory<byte>.Empty
            };
            if (generatedFragment.IsEmpty)
            {
                return null;
            }
            _materialShaderGenerator.RegisterGenerated(key, generatedFragment);
        }
        else
        {
            _materialShaderGenerator.Register(key, features);
        }
        return _materialShaderCompiler.GetOrQueue(key);
    }

    private SilkMaterialShaderKey CreateMaterialShaderKey(SilkMaterialData material) =>
        SilkMaterialShaderKey.Create(
            material.CreateRuntimeShaderKeyBytes(),
            _shaderFormat,
            // The salt versions the persisted program *and its binding layout*.
            // Both were bumped when the prefiltered environment added three slots
            // to every mesh layout: a cache entry written before that carries the
            // narrower layout, and rebinding through it fails at the first
            // environment slot rather than silently drawing without one.
            material.SurfaceKind == SilkSurfaceKind.MaterialXGenerated
                ? "MaterialXGeneratedBackendFragment.v4"
                : "MaterialXProjectedPreviewSurface.v3");

    private static string GetPipelineShaderIdentity(SilkMaterialShaderRequest? materialShader) =>
        materialShader?.Status == SilkMaterialShaderStatus.Ready
            ? materialShader.Program.CacheHash
            : string.Empty;

    private void BindMaterialResources(
        ISilkGraphicsCommandList commands,
        SilkMeshGpuResource mesh,
        SilkShaderFeatures features)
    {
        // Bound for every draw of every permutation, because the checked mesh
        // fragment references the shadow atlas slot in all of them and a backend
        // pipeline layout requires every declared descriptor to be populated. A
        // frame with no shadow map binds a one-texel stand-in the shader never
        // samples, exactly as an unused material slot does.
        _shadowMaps.Bind(commands);
        // Bound for the same reason and in the same place: the checked mesh
        // fragment references both environment maps in every permutation. A frame
        // with no live environment binds a one-texel stand-in twice and never
        // samples it.
        GpuResources.BindEnvironment(commands);
        SilkMaterialData? material = ResolveMaterial(mesh.Mesh);
        SilkMaterialParameter alias = FindFirstMaterialTextureParameter(material, features);
        bindTexture(SilkMaterialParameter.DiffuseColor, SilkShaderFeatures.BaseColorMap);
        if ((features & SilkShaderFeatures.NormalMap) != 0)
        {
            GpuResources.BindMaterialTexture(
                commands,
                material!,
                SilkMaterialParameter.Normal,
                SilkMaterialParameter.Normal);
        }
        bindTexture(SilkMaterialParameter.Roughness, SilkShaderFeatures.RoughnessMetallicMap);
        bindTexture(SilkMaterialParameter.Metallic, SilkShaderFeatures.MetallicMap);
        bindTexture(SilkMaterialParameter.EmissiveColor, SilkShaderFeatures.EmissiveMap);
        bindTexture(SilkMaterialParameter.Opacity, SilkShaderFeatures.OpacityMap);
        bindTexture(SilkMaterialParameter.Occlusion, SilkShaderFeatures.OcclusionMap);
        bindTexture(SilkMaterialParameter.SpecularColor, SilkShaderFeatures.SpecularColorMap);
        bindTexture(SilkMaterialParameter.Clearcoat, SilkShaderFeatures.ClearcoatMap);
        bindTexture(
            SilkMaterialParameter.ClearcoatRoughness,
            SilkShaderFeatures.ClearcoatRoughnessMap);
        bindTexture(SilkMaterialParameter.Ior, SilkShaderFeatures.IorMap);
        if ((features & ~SilkShaderFeatures.Uv) != 0)
        {
            // Always bound, because the checked binary references the slot in every
            // MAP_MATERIAL permutation. A material with no composite binds the same
            // stand-in the unused material slots bind and the shader never samples
            // it, because its composite target matches no slot bit.
            GpuResources.BindCompositeTexture(commands, material!, alias);
        }
        if (material is { SurfaceKind: SilkSurfaceKind.VolumeDensity } volumeMaterial &&
            volumeMaterial.GetTexture(SilkMaterialParameter.VolumeDensity) is not null)
        {
            GpuResources.BindVolumeDensityTexture(commands, volumeMaterial);
        }

        void bindTexture(SilkMaterialParameter bindingParameter, SilkShaderFeatures feature)
        {
            if ((features & ~SilkShaderFeatures.Uv) == 0)
            {
                return;
            }
            SilkMaterialParameter sourceParameter = (features & feature) != 0
                ? bindingParameter
                : alias;
            GpuResources.BindMaterialTexture(
                commands,
                material!,
                sourceParameter,
                bindingParameter);
        }
    }

    private static SilkMaterialParameter FindFirstMaterialTextureParameter(
        SilkMaterialData? material,
        SilkShaderFeatures features)
    {
        if (material is null || (features & ~SilkShaderFeatures.Uv) == 0)
        {
            return SilkMaterialParameter.DiffuseColor;
        }
        foreach (SilkMaterialTexture texture in material.Textures)
        {
            if (texture.Parameter is SilkMaterialParameter.DiffuseColor or
                SilkMaterialParameter.Normal or
                SilkMaterialParameter.Roughness or
                SilkMaterialParameter.Metallic or
                SilkMaterialParameter.EmissiveColor or
                SilkMaterialParameter.Opacity or
                SilkMaterialParameter.Occlusion or
                SilkMaterialParameter.SpecularColor or
                SilkMaterialParameter.Clearcoat or
                SilkMaterialParameter.ClearcoatRoughness or
                SilkMaterialParameter.Ior)
            {
                return texture.Parameter;
            }
        }
        throw new InvalidDataException(
            $"Material '{material.Path}' advertises texture features without a supported texture.");
    }

    private void PrepareMaterialTextures(
        ISilkGraphicsCommandList commands,
        SilkMeshGpuResource mesh,
        SilkShaderFeatures features)
    {
        if ((features & SilkShaderFeatures.BaseColorMap) != 0)
        {
            GpuResources.UploadMaterialTexture(
                commands,
                ResolveMaterial(mesh.Mesh)!,
                SilkMaterialParameter.DiffuseColor);
        }
        if ((features & SilkShaderFeatures.NormalMap) != 0)
        {
            GpuResources.UploadMaterialTexture(commands, ResolveMaterial(mesh.Mesh)!, SilkMaterialParameter.Normal);
        }
        if ((features & SilkShaderFeatures.RoughnessMetallicMap) != 0)
        {
            GpuResources.UploadMaterialTexture(
                commands,
                ResolveMaterial(mesh.Mesh)!,
                SilkMaterialParameter.Roughness);
        }
        if ((features & SilkShaderFeatures.MetallicMap) != 0)
        {
            GpuResources.UploadMaterialTexture(
                commands,
                ResolveMaterial(mesh.Mesh)!,
                SilkMaterialParameter.Metallic);
        }
        if ((features & SilkShaderFeatures.EmissiveMap) != 0)
        {
            GpuResources.UploadMaterialTexture(
                commands,
                ResolveMaterial(mesh.Mesh)!,
                SilkMaterialParameter.EmissiveColor);
        }
        if ((features & SilkShaderFeatures.OpacityMap) != 0)
        {
            GpuResources.UploadMaterialTexture(
                commands,
                ResolveMaterial(mesh.Mesh)!,
                SilkMaterialParameter.Opacity);
        }
        if ((features & SilkShaderFeatures.OcclusionMap) != 0)
        {
            GpuResources.UploadMaterialTexture(
                commands,
                ResolveMaterial(mesh.Mesh)!,
                SilkMaterialParameter.Occlusion);
        }
        if ((features & SilkShaderFeatures.SpecularColorMap) != 0)
        {
            GpuResources.UploadMaterialTexture(
                commands,
                ResolveMaterial(mesh.Mesh)!,
                SilkMaterialParameter.SpecularColor);
        }
        if ((features & SilkShaderFeatures.ClearcoatMap) != 0)
        {
            GpuResources.UploadMaterialTexture(
                commands,
                ResolveMaterial(mesh.Mesh)!,
                SilkMaterialParameter.Clearcoat);
        }
        if ((features & SilkShaderFeatures.ClearcoatRoughnessMap) != 0)
        {
            GpuResources.UploadMaterialTexture(
                commands,
                ResolveMaterial(mesh.Mesh)!,
                SilkMaterialParameter.ClearcoatRoughness);
        }
        if ((features & SilkShaderFeatures.IorMap) != 0)
        {
            GpuResources.UploadMaterialTexture(
                commands,
                ResolveMaterial(mesh.Mesh)!,
                SilkMaterialParameter.Ior);
        }
        if ((features & ~SilkShaderFeatures.Uv) != 0)
        {
            GpuResources.UploadCompositeTexture(commands, ResolveMaterial(mesh.Mesh)!);
        }
        if (ResolveMaterial(mesh.Mesh) is { SurfaceKind: SilkSurfaceKind.VolumeDensity } volumeMaterial &&
            volumeMaterial.GetTexture(SilkMaterialParameter.VolumeDensity) is not null)
        {
            GpuResources.UploadVolumeDensityTexture(commands, volumeMaterial);
        }
    }

    private readonly record struct BatchKey(
        SilkMeshGpuGeometryResource Geometry,
        string MaterialPath,
        SilkShaderFeatures Features,
        string MaterialShaderIdentity,
        bool SampledVolume,
        bool Transparent,
        float EyeDepth,
        ulong SortIdentity,
        SilkCullMode CullMode,
        SilkTopologyKind TopologyKind,
        // The packed UsdLux light and shadow link masks of every prim in the
        // batch. Instances of one prototype are drawn from a single instance
        // table with one bound surface block, so two instances that are linked to
        // different lights cannot share a draw and must not share a batch. A
        // scene with no authored linking resolves every prim to the same value,
        // so batching is unchanged there.
        uint LinkMasks);

    private readonly record struct PipelineKey(
        SilkShaderFeatures Features,
        bool SampledVolume,
        SilkCullMode CullMode,
        SilkTopologyKind TopologyKind,
        bool Transparent,
        uint VertexStride,
        string MaterialShaderIdentity);

    private void ApplySceneDelta(SilkSceneDelta delta)
    {
        GpuResources.Apply(Scene, delta);
        if (delta.MeshUpserts != 0 || delta.MeshRemovals != 0)
        {
            _selectionResolutionDirty = true;
        }
    }

    /// <summary>Takes a pooled batch list, or a new one when the pool is empty.</summary>
    /// <remarks>
    /// The pool can never exceed the number of distinct batches one frame produced, which is
    /// bounded by the live geometry in the scene: lists are returned at the start of the frame and
    /// taken again while grouping the same frame's meshes.
    /// </remarks>
    private List<SilkMeshGpuResource> RentBatch()
    {
        int last = _batchPool.Count - 1;
        if (last < 0)
        {
            return [];
        }

        List<SilkMeshGpuResource> batch = _batchPool[last];
        _batchPool.RemoveAt(last);
        return batch;
    }

    /// <summary>Gets the number of batch keys the most recent frame grouped meshes into.</summary>
    /// <remarks>
    /// A diagnostic for the retention gates only. It is the count that grew without bound while a
    /// batch key held a disposed geometry resource for every deformed frame ever drawn.
    /// </remarks>
    internal int BatchKeyCount => _batches.Count;

    /// <summary>Gets the number of pooled batch lists that are not in use.</summary>
    internal int PooledBatchCount => _batchPool.Count;

    /// <summary>
    /// Applies simulated geometry over the authored scene and uploads what changed.
    /// </summary>
    /// <remarks>
    /// This runs after any authored page has been applied and before the frame is drawn, so the
    /// simulated points win over authored geometry for the frame and the geometry delta the apply
    /// produces is uploaded here, exactly once. A body that has settled applies as unchanged,
    /// produces an empty delta, and costs nothing.
    /// </remarks>
    private void SyncPhysicsDeformations()
    {
        SilkPhysicsDeformations? deformations = PhysicsDeformations;
        if (deformations is null || !deformations.HasBatch)
        {
            return;
        }

        _ = deformations.Reapply(Scene);
        if (!deformations.HasPendingGeometry)
        {
            return;
        }

        GpuResources.Apply(Scene, deformations.Delta);
    }

    private void UpdateSelectionStatusBeforeRender()
    {
        if (_selectionItemCount == 0)
        {
            _selectedMeshCount = 0;
            _missingSelectionPathCount = 0;
            _selectionOutlineStatus = SilkSelectionOutlineStatus.EmptySelection;
        }
        else if (!_selectionOutlineSettings.Enabled)
        {
            _selectionOutlineStatus = SilkSelectionOutlineStatus.Disabled;
        }
        else if (!_selectionOutlineSettings.VisibleOnly &&
            _selectionOutlineDevice?.SelectionOutlineCapabilities.SupportsXRay != true)
        {
            _selectionOutlineStatus = SilkSelectionOutlineStatus.XRayUnsupported;
        }
        else if (_selectionOutlineDevice is null)
        {
            _selectionOutlineStatus = SilkSelectionOutlineStatus.UnsupportedDevice;
        }
        else
        {
            _selectionOutlineStatus = SilkSelectionOutlineStatus.Pending;
        }
    }

    private bool PrepareSelectionOutline(
        ISilkGraphicsTexture colorTarget,
        ISilkGraphicsTexture depthTarget)
    {
        int itemCount = _selectionItemCount;
        if (itemCount == 0)
        {
            _selectedMeshCount = 0;
            _missingSelectionPathCount = 0;
            _selectionOutlineStatus = SilkSelectionOutlineStatus.EmptySelection;
            return false;
        }
        if (!_selectionOutlineSettings.Enabled)
        {
            _selectionOutlineStatus = SilkSelectionOutlineStatus.Disabled;
            return false;
        }

        ISilkSelectionOutlineGraphicsDevice? outlineDevice = _selectionOutlineDevice;
        if (outlineDevice is null)
        {
            _selectionOutlineStatus = SilkSelectionOutlineStatus.UnsupportedDevice;
            return false;
        }
        SilkSelectionOutlineCapabilities capabilities =
            outlineDevice.SelectionOutlineCapabilities;
        capabilities.Validate();
        if (!capabilities.SupportsVisibleOnly)
        {
            _selectionOutlineStatus = SilkSelectionOutlineStatus.UnsupportedDevice;
            return false;
        }
        if (!_selectionOutlineSettings.VisibleOnly && !capabilities.SupportsXRay)
        {
            _selectionOutlineStatus = SilkSelectionOutlineStatus.XRayUnsupported;
            _unsupportedXRayRequests++;
            return false;
        }
        if ((depthTarget.Usage & SilkTextureUsage.Sampled) == 0)
        {
            _selectionOutlineStatus =
                SilkSelectionOutlineStatus.DepthSamplingUnsupported;
            return false;
        }

        ResolveSelectedMeshes();
        if (_selectedMeshCount == 0)
        {
            _selectionOutlineStatus = SilkSelectionOutlineStatus.NoMatchingMeshes;
            return false;
        }

        EnsureSelectionOutlineInfrastructure(outlineDevice, colorTarget.Format);
        EnsureSelectionOutlineTarget(outlineDevice, depthTarget);
        UpdateSelectionOutlineParameters(depthTarget.Width, depthTarget.Height);
        return true;
    }

    private void ResolveSelectedMeshes()
    {
        if (!_selectionResolutionDirty &&
            _selectionResolvedGpuRevision == GpuResources.Revision)
        {
            return;
        }

        DisposeScopedSelectionBuffers();
        IReadOnlyList<SelectionItem> items = _selection.Items;

        // A prim or instance selected whole already contains every component of
        // itself, so a component item for the same prim would add a second mask
        // draw that changes nothing the user can see.
        //
        // The key is the whole ordered chain rather than an innermost pair,
        // because two different nested instances can share an innermost
        // instancer and index and differ only in an outer level.
        HashSet<string>? wholeSelections = null;
        for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            SelectionItem candidate = items[itemIndex];
            if (candidate.ElementKind != SelectionElementKind.None)
            {
                continue;
            }
            wholeSelections ??= [];
            _ = wholeSelections.Add(DescribeSelectionScope(candidate));
        }

        var resolved = new List<SelectedMeshDraw>(items.Count);
        int missing = 0;
        for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            SelectionItem item = items[itemIndex];

            // A point-instanced prototype contributes one retained mesh per
            // instance. Selecting the prototype path alone highlights all of
            // them; naming an instancing chain highlights exactly that one,
            // because that is the identity a pick reports and a mask that
            // outlined every sibling would not be showing what the user
            // selected.
            IReadOnlyList<SilkMeshData> instances = Scene.GetInstances(item.PrimPath);
            if (instances.Count == 0)
            {
                missing++;
                continue;
            }

            bool resolvedAnyInstance = false;
            for (int instance = 0; instance < instances.Count; instance++)
            {
                SilkMeshData mesh = instances[instance];
                if (!MatchesSelectedInstance(item, mesh))
                {
                    continue;
                }
                if (!GpuResources.Meshes.TryGetValue(
                        mesh.Id,
                        out SilkMeshGpuResource? resource))
                {
                    continue;
                }
                resolvedAnyInstance = true;
                if (resource.IndexCount == 0)
                {
                    continue;
                }
                if (item.ElementKind != SelectionElementKind.None &&
                    wholeSelections is not null &&
                    (wholeSelections.Contains(item.PrimPath) ||
                        wholeSelections.Contains(DescribeMeshScope(mesh))))
                {
                    continue;
                }
                if (!TryCreateSelectedMeshDraw(
                        item,
                        mesh,
                        resource,
                        out SelectedMeshDraw draw))
                {
                    continue;
                }

                bool duplicate = false;
                for (int resolvedIndex = 0; resolvedIndex < resolved.Count; resolvedIndex++)
                {
                    if (resolved[resolvedIndex].Equals(draw))
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate)
                {
                    resolved.Add(draw);
                }
            }

            if (!resolvedAnyInstance)
            {
                missing++;
            }
        }

        _selectedMeshes = [.. resolved];
        _selectedMeshCount = resolved.Count;
        _missingSelectionPathCount = missing;
        _selectionResolvedGpuRevision = GpuResources.Revision;
        _selectionResolutionDirty = false;
    }

    /// <summary>
    /// Whether one selection item names the instance a retained record is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An item with no instancing chain selects the prototype itself, so every
    /// instance of it matches. An item with a chain selects exactly one
    /// instance, and matching it needs the whole chain: two nested instances can
    /// share an innermost instancer and an innermost index and differ only in an
    /// outer level, so comparing the innermost pair alone would outline a
    /// sibling the user did not select.
    /// </para>
    /// <para>
    /// A retained record that published no chain -- a producer older than ABI
    /// v23, or a record built directly by a host or a test -- is matched on the
    /// flattened pair it does publish. That is exact for the single-level scenes
    /// such a record can describe, and it is the only comparison available.
    /// </para>
    /// </remarks>
    private static bool MatchesSelectedInstance(in SelectionItem item, SilkMeshData mesh)
    {
        IReadOnlyList<SelectionInstancerEntry> selected = item.InstancerContext;
        if (selected.Count == 0)
        {
            return true;
        }

        IReadOnlyList<SilkInstancerContextEntry> published = mesh.InstancerContext;
        if (published.Count == 0)
        {
            return string.Equals(
                    mesh.InstancerPath,
                    item.InstancerPath,
                    StringComparison.Ordinal) &&
                mesh.InstanceIndex == item.InstanceIndex;
        }
        if (published.Count != selected.Count)
        {
            return false;
        }
        for (int level = 0; level < selected.Count; level++)
        {
            if (!string.Equals(
                    published[level].InstancerPath,
                    selected[level].InstancerPath,
                    StringComparison.Ordinal) ||
                published[level].InstanceIndex != selected[level].InstanceIndex)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Describes the exact scope one whole selection covers, so a component item
    /// for the same scope can be recognized as redundant.
    /// </summary>
    /// <remarks>
    /// A prim selected with no chain is described by its path alone, which is
    /// what makes it cover every instance of itself. An instance is described by
    /// its path and its whole ordered chain, because an innermost pair does not
    /// identify a nested instance.
    /// </remarks>
    private static string DescribeSelectionScope(in SelectionItem item)
    {
        IReadOnlyList<SelectionInstancerEntry> chain = item.InstancerContext;
        if (chain.Count == 0)
        {
            return item.PrimPath;
        }
        var builder = new System.Text.StringBuilder(item.PrimPath);
        for (int level = 0; level < chain.Count; level++)
        {
            _ = builder.Append('\u0000')
                .Append(chain[level].InstancerPath)
                .Append('\u0000')
                .Append(chain[level].InstanceIndex);
        }
        return builder.ToString();
    }

    /// <summary>Describes the scope of one retained record on the same terms.</summary>
    private static string DescribeMeshScope(SilkMeshData mesh)
    {
        IReadOnlyList<SilkInstancerContextEntry> chain = mesh.InstancerContext;
        if (chain.Count == 0)
        {
            return mesh.InstancerPath.Length == 0
                ? mesh.Path
                : string.Concat(
                    mesh.Path,
                    "\u0000",
                    mesh.InstancerPath,
                    "\u0000",
                    mesh.InstanceIndex.ToString(
                        System.Globalization.CultureInfo.InvariantCulture));
        }
        var builder = new System.Text.StringBuilder(mesh.Path);
        for (int level = 0; level < chain.Count; level++)
        {
            _ = builder.Append('\u0000')
                .Append(chain[level].InstancerPath)
                .Append('\u0000')
                .Append(chain[level].InstanceIndex);
        }
        return builder.ToString();
    }

    /// <summary>
    /// Builds the exact mask draw one selection item asks for: the whole mesh
    /// for a prim or instance item, and only the named component for a face,
    /// edge, or point item.
    /// </summary>
    /// <remarks>
    /// Scoping matters because the mask is what the outline is drawn around. An
    /// item that named face seven and produced the prototype's whole silhouette
    /// would outline something the user did not select, and would be
    /// indistinguishable from selecting the prim. A component the retained mesh
    /// cannot resolve exactly produces no draw at all rather than a broader one.
    /// </remarks>
    private bool TryCreateSelectedMeshDraw(
        in SelectionItem item,
        SilkMeshData mesh,
        SilkMeshGpuResource resource,
        out SelectedMeshDraw draw)
    {
        SilkSelectionMaskPrimitiveTopology wholeTopology =
            MaskTopologyOf(mesh.TopologyKind);
        if (item.ElementKind == SelectionElementKind.None)
        {
            // The whole-primitive mask is drawn in the mesh's own topology. A
            // selected basis curve is a line list and a selected point cloud is
            // a point list; masking either as a triangle list would reinterpret
            // its indices and outline a shape the scene does not contain.
            //
            // It is drawn by the whole-resource stage, never the subprim overlay
            // one. The overlay stage separates a component from the surface it
            // lies on; a whole curve or point cloud has no surface behind it, so
            // the same separation would pull the entire prim toward the viewer
            // and outline it through an occluder in the visible-only mode.
            draw = new SelectedMeshDraw(
                resource,
                null,
                resource.IndexCount,
                wholeTopology,
                SilkSelectionMaskStage.WholeResource);
            return true;
        }

        int element = item.ElementIndex!.Value;
        if (item.ElementKind is SelectionElementKind.Face or
            SelectionElementKind.Unspecified)
        {
            // An unspecified kind is masked as a face. The mask needs a concrete
            // component to scope to, and the only shape that produces an
            // unstated kind is the legacy four-parameter item, whose index could
            // never have meant anything but a face. This is a rendering fallback
            // and nothing more: the item's own identity keeps saying the kind is
            // unstated, so no consumer is told the index is a face.
            //
            // Only a triangle list has authored faces; a curve or point cloud
            // publishes one authored subprim per primitive but no face a mask
            // could scope to, so a face item over one resolves to nothing.
            if (mesh.TopologyKind != SilkTopologyKind.TriangleList)
            {
                draw = default;
                return false;
            }
            ReadOnlySpan<int> subprims = mesh.TriangleSubprims.Span;
            ReadOnlySpan<uint> indices = mesh.Indices.Span;
            var faceIndices = new List<uint>(12);
            for (int triangle = 0; triangle < subprims.Length; triangle++)
            {
                if (subprims[triangle] != element)
                {
                    continue;
                }
                int baseIndex = triangle * 3;
                faceIndices.Add(indices[baseIndex]);
                faceIndices.Add(indices[baseIndex + 1]);
                faceIndices.Add(indices[baseIndex + 2]);
            }
            return TryCreateScopedDraw(
                resource,
                faceIndices,
                SilkSelectionMaskPrimitiveTopology.TriangleList,
                SilkSelectionMaskStage.WholeResource,
                out draw);
        }

        if (item.ElementKind == SelectionElementKind.Edge)
        {
            if (!SilkSubprimPickGeometry.TryResolveEdges(
                    mesh,
                    out int[] authoredEdges,
                    out uint[] lineIndices))
            {
                draw = default;
                return false;
            }
            var edgeIndices = new List<uint>(4);
            for (int line = 0; line < authoredEdges.Length; line++)
            {
                if (authoredEdges[line] != element)
                {
                    continue;
                }
                edgeIndices.Add(lineIndices[line * 2]);
                edgeIndices.Add(lineIndices[(line * 2) + 1]);
            }
            return TryCreateScopedDraw(
                resource,
                edgeIndices,
                SilkSelectionMaskPrimitiveTopology.LineList,
                SubprimMaskStageOf(mesh),
                out draw);
        }

        if (item.ElementKind == SelectionElementKind.Point)
        {
            if (!SilkSubprimPickGeometry.TryResolvePoints(
                    mesh,
                    out int[] authoredPoints,
                    out uint[] pointIndices))
            {
                draw = default;
                return false;
            }
            var points = new List<uint>(4);
            for (int point = 0; point < authoredPoints.Length; point++)
            {
                if (authoredPoints[point] == element)
                {
                    points.Add(pointIndices[point]);
                }
            }
            return TryCreateScopedDraw(
                resource,
                points,
                SilkSelectionMaskPrimitiveTopology.PointList,
                SubprimMaskStageOf(mesh),
                out draw);
        }

        draw = default;
        return false;
    }

    /// <summary>
    /// The mask stage one selected authored edge or point is rasterized with.
    /// </summary>
    /// <remarks>
    /// The overlay stage exists to separate a component from the surface it lies
    /// on, which is what an edge or a point of a triangulated mesh is. An
    /// authored point of a UsdGeomPoints resource, or an authored edge of a line
    /// list, is the resource's own primitive rather than an overlay on one: the
    /// colour pass drew that exact primitive from that exact vertex, so the
    /// unbiased stage keeps its depth identical, and offsetting it would outline
    /// a point that stands behind an occluder in the very mode that exists to
    /// hide it.
    /// </remarks>
    private static SilkSelectionMaskStage SubprimMaskStageOf(SilkMeshData mesh) =>
        mesh.TopologyKind == SilkTopologyKind.TriangleList
            ? SilkSelectionMaskStage.SubprimOverlay
            : SilkSelectionMaskStage.WholeResource;

    private bool TryCreateScopedDraw(
        SilkMeshGpuResource resource,
        List<uint> indices,
        SilkSelectionMaskPrimitiveTopology topology,
        SilkSelectionMaskStage stage,
        out SelectedMeshDraw draw)
    {
        if (indices.Count == 0)
        {
            draw = default;
            return false;
        }

        uint[] data = [.. indices];
        ISilkGraphicsBuffer buffer = _device.CreateBuffer(
            checked((nuint)data.Length * sizeof(uint)),
            SilkBufferUsage.Index | SilkBufferUsage.Upload);
        _scopedSelectionBuffers.Add(buffer);
        buffer.Write(
            System.Runtime.InteropServices.MemoryMarshal.AsBytes<uint>(data));
        draw = new SelectedMeshDraw(
            resource,
            buffer,
            checked((uint)data.Length),
            topology,
            stage);
        return true;
    }

    private void DisposeScopedSelectionBuffers()
    {
        for (int index = 0; index < _scopedSelectionBuffers.Count; index++)
        {
            _scopedSelectionBuffers[index].Dispose();
        }
        _scopedSelectionBuffers.Clear();
    }

    /// <summary>
    /// One mask draw: a retained mesh, the index buffer to draw it with, and
    /// the topology that buffer describes.
    /// </summary>
    /// <remarks>
    /// A null index buffer means the mesh's own retained one, which is the whole
    /// prim. A non-null one is a scoped buffer this renderer owns and retires
    /// whenever the selection or the GPU scene changes.
    /// </remarks>
    private readonly record struct SelectedMeshDraw(
        SilkMeshGpuResource Resource,
        ISilkGraphicsBuffer? ScopedIndices,
        uint IndexCount,
        SilkSelectionMaskPrimitiveTopology Topology,
        SilkSelectionMaskStage Stage = SilkSelectionMaskStage.WholeResource);

    /// <summary>
    /// The device's own resource generation, which advances when the device
    /// invalidates what it is holding for this renderer.
    /// </summary>
    /// <remarks>
    /// The deformation resources key their "already dispatched" claim on it for
    /// the same reason the shadow atlas does: the host's uploads survive a
    /// generation change, but nothing the device wrote can be assumed to.
    /// </remarks>
    /// <summary>
    /// The generation every retained deformation resource is keyed on.
    /// </summary>
    /// <remarks>
    /// Every reset a device reports counts, not only a selection-outline one: a
    /// device lost on an ordinary submission invalidates what the kernel wrote
    /// just as completely, and the deformation resources belong to no subsystem
    /// that would otherwise hear about it.
    /// </remarks>
    private ulong ReadDeviceGeneration() => SilkDeviceGeneration.Read(_device);

    private void EnsureSelectionOutlineInfrastructure(
        ISilkSelectionOutlineGraphicsDevice outlineDevice,
        SilkTextureFormat colorFormat)
    {
        ulong generation = outlineDevice.SelectionOutlineDeviceGeneration;
        if (_selectionOutlineInfrastructureInitialized &&
            generation == _selectionOutlineDeviceGeneration &&
            colorFormat == _selectionOutlineColorFormat)
        {
            return;
        }

        if (_selectionOutlineInfrastructureInitialized)
        {
            DisposeSelectionOutlineInfrastructure();
            _selectionDeviceInvalidations++;
        }

        SilkSelectionMaskPipelineDescriptor maskDescriptor =
            SilkSelectionMaskPipelineDescriptor.CreateChecked(_shaderFormat);
        SilkSelectionOutlinePipelineDescriptor outlineDescriptor =
            SilkSelectionOutlinePipelineDescriptor.CreateChecked(_shaderFormat) with
            {
                ColorFormat = colorFormat
            };
        maskDescriptor.Validate();
        outlineDescriptor.Validate();

        ISilkSelectionMaskGraphicsPipeline? maskPipeline = null;
        ISilkSelectionOutlineGraphicsPipeline? outlinePipeline = null;
        ISilkGraphicsSampler? sampler = null;
        ISilkGraphicsBuffer? parameters = null;
        try
        {
            maskPipeline = outlineDevice.CreateSelectionMaskGraphicsPipeline(
                maskDescriptor);
            outlinePipeline = outlineDevice.CreateSelectionOutlineGraphicsPipeline(
                outlineDescriptor);
            sampler = _device.CreateSampler(SilkSamplerDescriptor.NearestClamp);
            parameters = _device.CreateBuffer(
                SilkSelectionOutlineUniformWriter.ByteSize,
                SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
        }
        catch
        {
            parameters?.Dispose();
            sampler?.Dispose();
            outlinePipeline?.Dispose();
            maskPipeline?.Dispose();
            throw;
        }

        _selectionMaskPipelines[(
            true,
            SilkSelectionMaskPrimitiveTopology.TriangleList,
            SilkVertexLayoutDescriptor.PositionNormal.Stride,
            SilkSelectionMaskStage.WholeResource)] = maskPipeline;
        _selectionOutlinePipeline = outlinePipeline;
        _selectionOutlineSampler = sampler;
        _selectionOutlineParameters = parameters;
        _selectionOutlineDeviceGeneration = generation;
        _selectionOutlineColorFormat = colorFormat;
        _selectionOutlineInfrastructureInitialized = true;
        _selectionOutlineParametersInitialized = false;
        _selectionPipelineCreations += 2;
    }

    private void EnsureSelectionOutlineTarget(
        ISilkSelectionOutlineGraphicsDevice outlineDevice,
        ISilkGraphicsTexture depthTarget)
    {
        ISilkGraphicsTexture? currentMask = _selectionMaskTarget;
        bool replaceMask =
            currentMask is null ||
            currentMask.Width != depthTarget.Width ||
            currentMask.Height != depthTarget.Height;
        if (!replaceMask &&
            ReferenceEquals(_selectionBoundDepthTarget, depthTarget) &&
            _selectionOutlineBinding is not null)
        {
            return;
        }

        ISilkGraphicsTexture? newMask = null;
        ISilkSelectionOutlineBinding? newBinding = null;
        ISilkGraphicsTexture bindingMask = currentMask!;
        try
        {
            if (replaceMask)
            {
                newMask = _device.CreateTexture2D(
                    SilkTextureDescriptor.SelectionMask(
                        depthTarget.Width,
                        depthTarget.Height));
                bindingMask = newMask;
            }

            ISilkGraphicsSampler sampler = _selectionOutlineSampler ??
                throw new InvalidOperationException(
                    "The selection outline sampler is missing.");
            var bindingDescriptor = new SilkSelectionOutlineBindingDescriptor(
                bindingMask,
                depthTarget,
                sampler,
                _selectionOutlineParameters ??
                    throw new InvalidOperationException(
                        "The selection outline parameter buffer is missing."));
            bindingDescriptor.Validate();
            newBinding = outlineDevice.CreateSelectionOutlineBinding(
                bindingDescriptor);
        }
        catch
        {
            newBinding?.Dispose();
            newMask?.Dispose();
            throw;
        }

        _selectionOutlineBinding?.Dispose();
        _selectionOutlineBinding = newBinding;
        _selectionBoundDepthTarget = depthTarget;
        _selectionBindingCreations++;
        if (replaceMask)
        {
            _selectionMaskTarget?.Dispose();
            _selectionMaskTarget = newMask;
            _selectionTargetCreations++;
        }
    }

    private void UpdateSelectionOutlineParameters(uint width, uint height)
    {
        ISilkGraphicsBuffer parameters = _selectionOutlineParameters ??
            throw new InvalidOperationException(
                "The selection outline parameter buffer is missing.");
        Span<byte> bytes =
            stackalloc byte[SilkSelectionOutlineUniformWriter.ByteSize];
        SilkSelectionOutlineUniformWriter.Write(
            _selectionOutlineSettings,
            width,
            height,
            bytes);
        if (!_selectionOutlineParametersInitialized ||
            !bytes.SequenceEqual(_selectionOutlineParameterBytes))
        {
            parameters.Write(bytes);
            bytes.CopyTo(_selectionOutlineParameterBytes);
            _selectionOutlineParametersInitialized = true;
            _selectionParameterUploads++;
        }

    }


    /// <summary>
    /// Gets the mask pipeline for one depth policy, topology, mesh vertex
    /// layout, and mask stage, creating it on first use.
    /// </summary>
    private ISilkSelectionMaskGraphicsPipeline EnsureSelectionMaskPipeline(
        bool depthTested,
        SilkSelectionMaskPrimitiveTopology topology,
        SilkVertexLayoutDescriptor vertexLayout,
        SilkSelectionMaskStage stage)
    {
        if (_selectionMaskPipelines.TryGetValue(
                (depthTested, topology, vertexLayout.Stride, stage),
                out ISilkSelectionMaskGraphicsPipeline? existing))
        {
            return existing;
        }

        ISilkSelectionOutlineGraphicsDevice outlineDevice = _selectionOutlineDevice ??
            throw new InvalidOperationException(
                "The renderer has no selection-outline capable device.");
        SilkSelectionMaskPipelineDescriptor descriptor =
            SilkSelectionMaskPipelineDescriptor.CreateChecked(
                _shaderFormat,
                depthTested,
                topology,
                vertexLayout,
                stage);
        descriptor.Validate();
        ISilkSelectionMaskGraphicsPipeline created =
            outlineDevice.CreateSelectionMaskGraphicsPipeline(descriptor);
        _selectionMaskPipelines[(
            depthTested,
            topology,
            vertexLayout.Stride,
            stage)] = created;
        _selectionPipelineCreations++;
        return created;
    }

    private void RecordSelectionOutline(
        ISilkGraphicsCommandList commands,
        ISilkSelectionOutlineGraphicsCommandList selectionCommands,
        ISilkGraphicsTexture colorTarget,
        ISilkGraphicsTexture depthTarget)
    {
        ISilkGraphicsTexture maskTarget = _selectionMaskTarget ??
            throw new InvalidOperationException(
                "The reusable selection mask target is missing.");
        ISilkSelectionOutlineGraphicsPipeline outlinePipeline =
            _selectionOutlinePipeline ??
            throw new InvalidOperationException(
                "The selection outline pipeline is missing.");
        ISilkSelectionOutlineBinding binding = _selectionOutlineBinding ??
            throw new InvalidOperationException(
                "The selection outline binding is missing.");

        int selectedDraws = 0;

        // Both silhouettes go into the one reusable mask texture, in different
        // channels, and are composited once. The occluded pass runs first and
        // writes only green; the depth-tested visible pass runs second and writes
        // every channel, which is correct because the visible silhouette is a
        // subset of the whole one. The mask is cleared once, before the first
        // pass, so the second pass adds to the first rather than replacing it.
        //
        // The two must be composited together and not one over the other. The
        // visible-only composite's occlusion suppression works precisely because
        // its mask contains only the unoccluded selected fragments, so a second
        // mask is genuinely needed; but compositing the whole silhouette and then
        // the visible one over it blends the two styles wherever both cover a
        // pixel, and the default outline colour is not opaque. A visible edge in
        // x-ray then did not match the visible-only image it is required to
        // reproduce exactly. The shader now chooses per pixel instead.
        if (_selectionOutlineSettings.Mode == SilkSelectionOutlineMode.XRay)
        {
            selectedDraws += RecordSelectionMaskPass(
                commands,
                selectionCommands,
                maskTarget,
                depthTarget,
                depthTested: false,
                clearMask: true);
            selectedDraws += RecordSelectionMaskPass(
                commands,
                selectionCommands,
                maskTarget,
                depthTarget,
                depthTested: true,
                clearMask: false);
        }
        else
        {
            selectedDraws += RecordSelectionMaskPass(
                commands,
                selectionCommands,
                maskTarget,
                depthTarget,
                depthTested: true,
                clearMask: true);
        }

        RecordSelectionCompositePass(
            commands,
            selectionCommands,
            colorTarget,
            outlinePipeline,
            binding);

        _selectionDraws += checked((ulong)selectedDraws);
        _selectionOutlineStatus = SilkSelectionOutlineStatus.Rendered;
    }

    /// <summary>
    /// Renders the selected components into the reusable mask texture.
    /// </summary>
    /// <remarks>
    /// One mask pass can contain draws of several topologies, because a
    /// selection may name a prim, a face, an edge, and a point at once. The
    /// pipeline is switched per topology group inside the one rendering scope,
    /// so the mask still costs one pass however many kinds the selection mixes.
    /// </remarks>
    private int RecordSelectionMaskPass(
        ISilkGraphicsCommandList commands,
        ISilkSelectionOutlineGraphicsCommandList selectionCommands,
        ISilkGraphicsTexture maskTarget,
        ISilkGraphicsTexture depthTarget,
        bool depthTested,
        bool clearMask)
    {
        if (clearMask)
        {
            commands.ClearColor(maskTarget, new SilkColor(0, 0, 0, 0));
        }
        var maskRendering = new SilkSelectionMaskRenderingDescriptor(
            maskTarget,
            depthTarget);
        maskRendering.Validate();
        selectionCommands.BeginSelectionMaskRendering(maskRendering);
        commands.SetViewport(new SilkViewport(
            0,
            0,
            maskTarget.Width,
            maskTarget.Height));
        commands.SetScissor(new SilkScissor(
            0,
            0,
            maskTarget.Width,
            maskTarget.Height));
        int selectedDraws = 0;
        (SilkSelectionMaskPrimitiveTopology Topology,
            uint Stride,
            SilkSelectionMaskStage Stage)? bound = null;
        foreach (SilkSelectionMaskPrimitiveTopology topology in MaskTopologyOrder)
        {
            for (int index = 0; index < _selectedMeshCount; index++)
            {
                SelectedMeshDraw draw = _selectedMeshes[index];
                if (draw.Topology != topology)
                {
                    continue;
                }
                SilkVertexLayoutDescriptor layout = draw.Resource.VertexLayout;
                if (bound != (topology, layout.Stride, draw.Stage))
                {
                    selectionCommands.SetSelectionMaskGraphicsPipeline(
                        EnsureSelectionMaskPipeline(
                            depthTested,
                            topology,
                            layout,
                            draw.Stage));
                    bound = (topology, layout.Stride, draw.Stage);
                }
                commands.SetVertexBuffer(draw.Resource.VertexBuffer);
                commands.SetIndexBuffer(
                    draw.ScopedIndices ?? draw.Resource.IndexBuffer);
                commands.SetUniformBuffer(0, 0, draw.Resource.UniformBuffer);
                commands.DrawIndexed(draw.IndexCount);
                selectedDraws++;
            }
        }
        if (bound is null)
        {
            selectionCommands.SetSelectionMaskGraphicsPipeline(
                EnsureSelectionMaskPipeline(
                    depthTested,
                    SilkSelectionMaskPrimitiveTopology.TriangleList,
                    SilkVertexLayoutDescriptor.PositionNormal,
                    SilkSelectionMaskStage.WholeResource));
        }
        commands.EndRendering();
        _selectionMaskPasses++;
        return selectedDraws;
    }

    private static readonly SilkSelectionMaskPrimitiveTopology[] MaskTopologyOrder =
    [
        SilkSelectionMaskPrimitiveTopology.TriangleList,
        SilkSelectionMaskPrimitiveTopology.LineList,
        SilkSelectionMaskPrimitiveTopology.PointList
    ];

    /// <summary>Composites one outline over the visible color target.</summary>
    private void RecordSelectionCompositePass(
        ISilkGraphicsCommandList commands,
        ISilkSelectionOutlineGraphicsCommandList selectionCommands,
        ISilkGraphicsTexture colorTarget,
        ISilkSelectionOutlineGraphicsPipeline outlinePipeline,
        ISilkSelectionOutlineBinding binding)
    {
        var outlineRendering = new SilkSelectionOutlineRenderingDescriptor(
            colorTarget);
        outlineRendering.Validate();
        selectionCommands.BeginSelectionOutlineRendering(outlineRendering);
        selectionCommands.SetSelectionOutlineGraphicsPipeline(outlinePipeline);
        selectionCommands.SetSelectionOutlineBinding(binding);
        commands.SetViewport(new SilkViewport(
            0,
            0,
            colorTarget.Width,
            colorTarget.Height));
        commands.SetScissor(new SilkScissor(
            0,
            0,
            colorTarget.Width,
            colorTarget.Height));
        selectionCommands.DrawSelectionOutlineFullscreenTriangle();
        commands.EndRendering();
        _selectionOutlinePasses++;
    }

    private void DisposeSelectionOutlineInfrastructure()
    {
        _selectionOutlineBinding?.Dispose();
        _selectionOutlineBinding = null;
        _selectionBoundDepthTarget = null;
        _selectionMaskTarget?.Dispose();
        _selectionMaskTarget = null;
        _selectionOutlineParameters?.Dispose();
        _selectionOutlineParameters = null;
        _selectionOutlineSampler?.Dispose();
        _selectionOutlineSampler = null;
        _selectionOutlinePipeline?.Dispose();
        _selectionOutlinePipeline = null;
        foreach (ISilkSelectionMaskGraphicsPipeline maskPipeline in
            _selectionMaskPipelines.Values)
        {
            maskPipeline.Dispose();
        }
        _selectionMaskPipelines.Clear();
        DisposeScopedSelectionBuffers();
        _selectedMeshes = [];
        _selectedMeshCount = 0;
        _selectionResolutionDirty = true;
        _selectionOutlineParametersInitialized = false;
        _selectionOutlineInfrastructureInitialized = false;
        _selectionOutlineDeviceGeneration = 0;
        _selectionOutlineColorFormat = default;
    }

    private sealed class DisplayCaptureLease(object gate) : IDisposable
    {
        private object? _gate = gate;

        public void Dispose()
        {
            object? gate = Interlocked.Exchange(ref _gate, null);
            if (gate is not null)
            {
                Monitor.Exit(gate);
            }
        }
    }

    private void ProcessPicking(
        ISilkGraphicsTexture visibleColorTarget,
        SilkPickFrameBinding binding)
    {
        var viewport = new ViewportDimensions(
            checked((int)visibleColorTarget.Width),
            checked((int)visibleColorTarget.Height));
        EnsurePickDeviceGeneration(binding);
        ResolveCompletedReadbacks(binding, viewport);
        PromoteQueuedPick();

        while (_activePick is { } queued)
        {
            if (queued.Request.IsStale(
                binding.StateRevision,
                binding.SceneRevision))
            {
                CompleteStale(queued, binding);
                AdvancePickQueue();
                PromoteQueuedPick();
                continue;
            }
            if (queued.Request.Viewport != viewport)
            {
                CompleteInfrastructureStale(
                    queued,
                    binding,
                    RenderPickStaleReason.Viewport);
                AdvancePickQueue();
                PromoteQueuedPick();
                continue;
            }
            if (!SupportsPickRequest(queued.Request) ||
                _pickingDevice is null)
            {
                CompleteUnsupported(queued, binding);
                AdvancePickQueue();
                PromoteQueuedPick();
                continue;
            }
            break;
        }

        PendingPick? active = _activePick;
        if (active is null)
        {
            return;
        }

        EnsurePickTargets(visibleColorTarget.Width, visibleColorTarget.Height);
        SilkPickReadbackRing readbacks = _pickReadbacks ??
            throw new InvalidOperationException(
                "The pick-capable device has no readback ring.");
        PendingPick?[] inFlight = _inFlightPicks ??
            throw new InvalidOperationException(
                "The pick-capable device has no in-flight slot table.");
        if (!readbacks.TryAcquire(out SilkPickReadbackReservation reservation))
        {
            _pickRingSaturations++;
            return;
        }

        ISilkGraphicsSubmission? pickSubmission = null;
        List<ISilkGraphicsBuffer>? subprimBuffers = null;
        try
        {
            using ISilkGraphicsCommandList commands = _device.CreateCommandList();
            if (commands is not ISilkPickGraphicsCommandList pickCommands)
            {
                throw new InvalidOperationException(
                    "A pick-capable device must create pick-capable command lists.");
            }

            ISilkGraphicsTexture pickColor = _pickColorTarget ??
                throw new InvalidOperationException("The pick color target is missing.");
            ISilkGraphicsTexture pickDepth = _pickDepthTarget ??
                throw new InvalidOperationException("The pick depth target is missing.");
            RenderPickRequest request = active.Request;
            SilkPickSubprimKind? subprimKind = request.Target switch
            {
                RenderPickTarget.Edge => SilkPickSubprimKind.Edge,
                RenderPickTarget.Point => SilkPickSubprimKind.Point,
                _ => null
            };
            if (subprimKind is { } kind && !HasResolvableSubprims(kind))
            {
                readbacks.Cancel(reservation);
                CompleteUnsupported(active, binding);
                AdvancePickQueue();
                return;
            }

            commands.ClearColor(pickColor, new SilkColor(0, 0, 0, 0));
            commands.ClearDepth(pickDepth, 1);
            commands.BeginRendering(new SilkRenderingDescriptor(pickColor, pickDepth));

            // One pipeline is bound before any state or draw so the recorded
            // scope is always complete, even when no mesh is eligible; meshes
            // whose vertex stride differs rebind below.
            pickCommands.SetPickGraphicsPipeline(EnsurePickPipeline(
                SilkPickPrimitiveTopology.TriangleList,
                SilkVertexLayoutDescriptor.PositionNormal,
                SilkPickDepthBias.None));
            (SilkPickPrimitiveTopology Topology, uint Stride, bool ColorWrite)? bound =
                (SilkPickPrimitiveTopology.TriangleList,
                    SilkVertexLayoutDescriptor.PositionNormal.Stride,
                    true);
            commands.SetViewport(new SilkViewport(
                0,
                0,
                pickColor.Width,
                pickColor.Height));
            commands.SetScissor(new SilkScissor(
                request.X,
                request.Y,
                1,
                1));

            // Every rendered topology takes part, not only triangles. A basis
            // curve and a point cloud are drawn, depth-tested, and visible, so
            // they must both answer a prim pick and write the depth that hides a
            // face, edge or point behind them. Skipping them made a curve in
            // front of a surface invisible to picking, which is the one thing a
            // "visible only" pick must never be.
            foreach (SilkMeshGpuResource mesh in GpuResources.MeshValues)
            {
                if (mesh.IndexCount == 0)
                {
                    continue;
                }
                SilkPickPrimitiveTopology topology =
                    PickTopologyOf(mesh.Mesh.TopologyKind);
                uint indicesPerPrimitive = IndicesPerPickPrimitive(topology);
                if (!Scene.PickIdentities.TryGetRange(
                    mesh.Mesh.Path,
                    mesh.Mesh.InstanceIndex,
                    out SilkPickTokenRange tokenRange))
                {
                    throw new InvalidDataException(
                        $"Mesh '{mesh.Mesh.Path}' has no active Silk pick token range.");
                }
                if (tokenRange.FirstToken == 0 ||
                    tokenRange.TokenCount != mesh.IndexCount / indicesPerPrimitive)
                {
                    throw new InvalidDataException(
                        $"Mesh '{mesh.Mesh.Path}' has an inconsistent Silk pick token range.");
                }

                // A face request draws a curve or a point cloud as a pure
                // occluder. Its depth still hides the faces behind it -- that is
                // exactly what a visible-surface face pick has to honour -- but
                // it writes no token, so the pixel keeps the background value and
                // the request answers a miss instead of a face index the scene
                // never authored.
                bool colorWrite = request.Target != RenderPickTarget.Face ||
                    Scene.PickIdentities.AnswersFacePicks(
                        mesh.Mesh.Path,
                        mesh.Mesh.InstanceIndex);

                // Each mesh is drawn through the pipeline that matches its own
                // topology and vertex stride, so a textured or normal-mapped
                // mesh is picked from the same vertices the colour pass
                // rasterized, and a curve is picked as a line rather than being
                // reinterpreted as triangles.
                if (bound != (topology, mesh.VertexLayout.Stride, colorWrite))
                {
                    pickCommands.SetPickGraphicsPipeline(EnsurePickPipeline(
                        topology,
                        mesh.VertexLayout,
                        SilkPickDepthBias.None,
                        colorWrite));
                    bound = (topology, mesh.VertexLayout.Stride, colorWrite);
                }
                commands.SetVertexBuffer(mesh.VertexBuffer);
                commands.SetIndexBuffer(mesh.IndexBuffer);
                commands.SetUniformBuffer(0, 0, mesh.UniformBuffer);
                pickCommands.SetPickBaseToken(tokenRange.FirstToken);
                commands.DrawIndexed(mesh.IndexCount);
            }
            commands.EndRendering();

            if (subprimKind is { } resolvedKind)
            {
                // The surface pass above wrote the depth every visible fragment
                // has, from the same deformed and displaced vertices the color
                // pass uses. Its colour is discarded here so the pixel can only
                // carry an authored edge or point token, while the depth it
                // wrote stays and occludes the components behind it.
                subprimBuffers = RecordSubprimPickPass(
                    commands,
                    pickCommands,
                    pickColor,
                    pickDepth,
                    request,
                    resolvedKind);
            }

            var coordinate = new SilkTexturePixelCoordinate(
                checked((uint)request.X),
                checked((uint)request.Y));
            coordinate.Validate(pickColor);
            pickCommands.CopyRgba8Pixel(
                pickColor,
                coordinate,
                reservation.Buffer);

            pickSubmission = _device.Submit(commands);
            readbacks.Commit(
                reservation,
                pickSubmission,
                new SilkPickReadbackContext(
                    request,
                    binding.StateRevision,
                    binding.SceneRevision,
                    Scene.PickIdentities.Revision,
                    _pickDeviceGeneration,
                    viewport));
            pickSubmission = null;
            inFlight[reservation.SlotIndex] = active;
            if (_inFlightPickBuffers is not null)
            {
                _inFlightPickBuffers[reservation.SlotIndex] = subprimBuffers;
            }
            subprimBuffers = null;
            _pickPassesRecorded++;
            AdvancePickQueue();
        }
        catch (Exception exception)
        {
            pickSubmission?.Dispose();
            DisposeBuffers(subprimBuffers);
            readbacks.Cancel(reservation);
            active.Fail(exception);
            AdvancePickQueue();
            throw;
        }

        ResolveCompletedReadbacks(binding, viewport);
    }

    /// <summary>
    /// Records the edge or point pass over the depth the surface pass wrote.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One primitive is drawn per authored component, in ascending authored
    /// order, so the rasterized primitive index plus the mesh's base token is
    /// the token that resolves back to that authored component. Triangulation
    /// diagonals and authored components no emitted primitive covers are simply
    /// not drawn, so an edge pick can never answer with a diagonal.
    /// </para>
    /// <para>
    /// The coincident offset is applied per mesh, from the mesh's own topology,
    /// and not to the whole pass. A component of a triangulated mesh is genuinely
    /// coplanar with the triangles the surface pass already rasterized, and its
    /// depth differs from theirs only by rounding, so it needs the offset to win
    /// its own less-equal test. A point of a UsdGeomPoints resource -- or an
    /// authored edge of a line list -- is not a derived overlay at all: the
    /// surface pass drew that very primitive, from the same vertex, so its depth
    /// is bit-identical and less-equal already passes. Offsetting it would pull
    /// it in front of a genuine occluder, and a point standing behind a wall
    /// would answer a point pick the user cannot even see.
    /// </para>
    /// </remarks>
    private List<ISilkGraphicsBuffer> RecordSubprimPickPass(
        ISilkGraphicsCommandList commands,
        ISilkPickGraphicsCommandList pickCommands,
        ISilkGraphicsTexture pickColor,
        ISilkGraphicsTexture pickDepth,
        RenderPickRequest request,
        SilkPickSubprimKind kind)
    {
        SilkPickPrimitiveTopology topology = kind == SilkPickSubprimKind.Edge
            ? SilkPickPrimitiveTopology.LineList
            : SilkPickPrimitiveTopology.PointList;

        var buffers = new List<ISilkGraphicsBuffer>();
        try
        {
            commands.ClearColor(pickColor, new SilkColor(0, 0, 0, 0));
            commands.BeginRendering(new SilkRenderingDescriptor(pickColor, pickDepth));

            // As in the surface pass, one pipeline is bound before any state or
            // draw so the recorded scope is complete even when no mesh is
            // eligible.
            pickCommands.SetPickGraphicsPipeline(
                EnsurePickPipeline(
                    topology,
                    SilkVertexLayoutDescriptor.PositionNormal,
                    SilkPickDepthBias.Coincident));
            (uint Stride, SilkPickDepthBias Bias)? bound =
                (SilkVertexLayoutDescriptor.PositionNormal.Stride,
                    SilkPickDepthBias.Coincident);
            commands.SetViewport(new SilkViewport(
                0,
                0,
                pickColor.Width,
                pickColor.Height));
            commands.SetScissor(new SilkScissor(request.X, request.Y, 1, 1));
            foreach (SilkMeshGpuResource mesh in GpuResources.MeshValues)
            {
                if (mesh.IndexCount == 0 ||
                    !TryResolveSubprimIndices(
                        mesh.Mesh,
                        kind,
                        out uint[] indices,
                        out uint primitiveCount))
                {
                    continue;
                }
                if (!Scene.PickIdentities.TryGetRange(
                        mesh.Mesh.Path,
                        mesh.Mesh.InstanceIndex,
                        kind,
                        out SilkPickTokenRange tokenRange) ||
                    tokenRange.FirstToken == 0 ||
                    tokenRange.TokenCount != primitiveCount)
                {
                    throw new InvalidDataException(
                        $"Mesh '{mesh.Mesh.Path}' has an inconsistent Silk " +
                        $"{kind} pick token range.");
                }

                ISilkGraphicsBuffer indexBuffer = _device.CreateBuffer(
                    checked((nuint)indices.Length * sizeof(uint)),
                    SilkBufferUsage.Index | SilkBufferUsage.Upload);
                buffers.Add(indexBuffer);
                indexBuffer.Write(
                    System.Runtime.InteropServices.MemoryMarshal.AsBytes<uint>(indices));

                SilkPickDepthBias bias = SubprimPickDepthBiasOf(mesh.Mesh);
                if (bound != (mesh.VertexLayout.Stride, bias))
                {
                    pickCommands.SetPickGraphicsPipeline(
                        EnsurePickPipeline(
                            topology,
                            mesh.VertexLayout,
                            bias));
                    bound = (mesh.VertexLayout.Stride, bias);
                }
                commands.SetVertexBuffer(mesh.VertexBuffer);
                commands.SetIndexBuffer(indexBuffer);
                commands.SetUniformBuffer(0, 0, mesh.UniformBuffer);
                pickCommands.SetPickBaseToken(tokenRange.FirstToken);
                commands.DrawIndexed(checked((uint)indices.Length));
            }
            commands.EndRendering();
        }
        catch
        {
            DisposeBuffers(buffers);
            throw;
        }
        return buffers;
    }

    /// <summary>
    /// The pass stage one retained mesh's authored edge or point pass is drawn
    /// with.
    /// </summary>
    /// <remarks>
    /// A triangulated mesh's edges and points are overlays derived from the
    /// triangles the surface pass rasterized, so they are coplanar-by-rounding
    /// and need the coincident offset. A line list's authored edges and a point
    /// list's authored points are the whole resource's own primitives: the
    /// surface pass drew exactly those, from exactly those vertices, so their
    /// depth is identical rather than merely equal in exact arithmetic, and the
    /// unbiased stage is both sufficient and required.
    /// </remarks>
    private static SilkPickDepthBias SubprimPickDepthBiasOf(SilkMeshData mesh) =>
        mesh.TopologyKind == SilkTopologyKind.TriangleList
            ? SilkPickDepthBias.Coincident
            : SilkPickDepthBias.None;

    private static bool TryResolveSubprimIndices(
        SilkMeshData mesh,
        SilkPickSubprimKind kind,
        out uint[] indices,
        out uint primitiveCount)
    {
        if (kind == SilkPickSubprimKind.Edge)
        {
            if (SilkSubprimPickGeometry.TryResolveEdges(
                mesh,
                out int[] authoredEdges,
                out indices))
            {
                primitiveCount = checked((uint)authoredEdges.Length);
                return true;
            }
        }
        else if (SilkSubprimPickGeometry.TryResolvePoints(
            mesh,
            out int[] authoredPoints,
            out indices))
        {
            primitiveCount = checked((uint)authoredPoints.Length);
            return true;
        }

        indices = [];
        primitiveCount = 0;
        return false;
    }

    /// <summary>Maps one retained topology to the pick pipeline topology.</summary>
    private static SilkPickPrimitiveTopology PickTopologyOf(
        SilkTopologyKind topologyKind) =>
        topologyKind switch
        {
            SilkTopologyKind.LineList => SilkPickPrimitiveTopology.LineList,
            SilkTopologyKind.PointList => SilkPickPrimitiveTopology.PointList,
            _ => SilkPickPrimitiveTopology.TriangleList
        };

    /// <summary>Maps one retained topology to the mask pipeline topology.</summary>
    private static SilkSelectionMaskPrimitiveTopology MaskTopologyOf(
        SilkTopologyKind topologyKind) =>
        topologyKind switch
        {
            SilkTopologyKind.LineList => SilkSelectionMaskPrimitiveTopology.LineList,
            SilkTopologyKind.PointList => SilkSelectionMaskPrimitiveTopology.PointList,
            _ => SilkSelectionMaskPrimitiveTopology.TriangleList
        };

    /// <summary>The index count one primitive of a pick topology consumes.</summary>
    private static uint IndicesPerPickPrimitive(
        SilkPickPrimitiveTopology topology) =>
        topology switch
        {
            SilkPickPrimitiveTopology.LineList => 2u,
            SilkPickPrimitiveTopology.PointList => 1u,
            _ => 3u
        };

    /// <summary>
    /// Whether any retained mesh answers this target with authored identity.
    /// </summary>
    /// <remarks>
    /// A scene in which nothing does completes the request as unsupported and
    /// records the named reason the delegate published, rather than rendering an
    /// empty pass and reporting an indistinguishable miss.
    /// </remarks>
    private bool HasResolvableSubprims(SilkPickSubprimKind kind)
    {
        bool resolvable = false;
        SilkSubprimUnsupportedReason refused = SilkSubprimUnsupportedReason.None;
        foreach (SilkMeshGpuResource mesh in GpuResources.MeshValues)
        {
            if (mesh.IndexCount == 0)
            {
                continue;
            }
            if (TryResolveSubprimIndices(mesh.Mesh, kind, out _, out uint count) &&
                count != 0)
            {
                resolvable = true;
                continue;
            }
            refused |= mesh.Mesh.SubprimUnsupported;
        }

        if (!resolvable)
        {
            _pickRefusedSubprimTargets++;
            _pickLastRefusedReason = refused;
        }
        return resolvable;
    }

    private static void DisposeBuffers(List<ISilkGraphicsBuffer>? buffers)
    {
        if (buffers is null)
        {
            return;
        }
        for (int index = 0; index < buffers.Count; index++)
        {
            buffers[index].Dispose();
        }
        buffers.Clear();
    }

    private void ReleaseInFlightPickBuffers(int slotIndex)
    {
        if (_inFlightPickBuffers is null)
        {
            return;
        }
        DisposeBuffers(_inFlightPickBuffers[slotIndex]);
        _inFlightPickBuffers[slotIndex] = null;
    }

    private void ResolveCompletedReadbacks(
        SilkPickFrameBinding binding,
        ViewportDimensions viewport)
    {
        if (_pickReadbacks is null || _inFlightPicks is null)
        {
            return;
        }

        while (_pickReadbacks.TryReadCompleted(
            out SilkPickReadbackResult readback))
        {
            PendingPick pending = _inFlightPicks[readback.SlotIndex] ??
                throw new InvalidOperationException(
                    "A completed Silk pick slot has no retained request.");
            _inFlightPicks[readback.SlotIndex] = null;
            ReleaseInFlightPickBuffers(readback.SlotIndex);
            if (pending.IsCompleted)
            {
                pending.ReleaseCancellationRegistration();
                continue;
            }

            SilkPickReadbackContext context = readback.Context;
            RenderPickStaleReason staleReasons =
                pending.Request.InferStaleReasons(
                    binding.StateRevision,
                    binding.SceneRevision);
            if (context.StateRevision != binding.StateRevision)
            {
                staleReasons |= RenderPickStaleReason.StateRevision;
            }
            if (context.SceneRevision != binding.SceneRevision)
            {
                staleReasons |= RenderPickStaleReason.SceneRevision;
            }
            if (context.IdentityRevision != Scene.PickIdentities.Revision)
            {
                staleReasons |= RenderPickStaleReason.BackendState;
            }
            if (context.DeviceGeneration != _pickDeviceGeneration)
            {
                staleReasons |= RenderPickStaleReason.ContextGeneration;
            }
            if (context.Viewport != viewport)
            {
                staleReasons |= RenderPickStaleReason.Viewport;
            }
            if (staleReasons != RenderPickStaleReason.None)
            {
                CompleteInfrastructureStale(
                    pending,
                    binding,
                    staleReasons);
                continue;
            }

            if (readback.Token == 0)
            {
                pending.Complete(RenderPickResult.Miss(
                    pending.Request,
                    context.StateRevision,
                    context.SceneRevision));
                _pickMisses++;
                continue;
            }
            if (!Scene.PickIdentities.TryResolve(
                readback.Token,
                out SilkPickIdentity identity))
            {
                pending.Fail(new InvalidDataException(
                    $"Silk pick token {readback.Token} has no active authoritative identity."));
                continue;
            }

            // A prim request accepts either whole-resource kind: a triangulated
            // mesh resolves its surface tokens to the authored face the triangle
            // came from, and a curve or point resource resolves them to the
            // resource itself. Both name the same prim and the same instance,
            // which is all a prim pick reports.
            bool acceptable = pending.Request.Target switch
            {
                RenderPickTarget.Edge =>
                    identity.SubprimKind == SilkPickSubprimKind.Edge,
                RenderPickTarget.Point =>
                    identity.SubprimKind == SilkPickSubprimKind.Point,
                RenderPickTarget.Face =>
                    identity.SubprimKind == SilkPickSubprimKind.Face,
                _ => identity.SubprimKind is SilkPickSubprimKind.Face or
                    SilkPickSubprimKind.Primitive
            };
            if (!acceptable)
            {
                pending.Fail(new InvalidDataException(
                    $"Silk pick token {readback.Token} resolved a " +
                    $"{identity.SubprimKind} identity for a " +
                    $"{pending.Request.Target} request."));
                continue;
            }

            // The whole resolved identity reaches the caller: the prim path, the
            // complete ordered instancing chain when the hit is one instance of
            // a prototype, and the authored subprim index together with the kind
            // that says what the index names.
            //
            // The chain is preferred over the flattened pair whenever the record
            // published one. For a nested instance the record's own instance
            // ordinal is an hdSilk composite that no consumer can decode back to
            // a scene instance, so reporting it beside the innermost instancer
            // path would name an instance that does not exist.
            SelectionElementKind elementKind = pending.Request.Target switch
            {
                RenderPickTarget.Face => SelectionElementKind.Face,
                RenderPickTarget.Edge => SelectionElementKind.Edge,
                RenderPickTarget.Point => SelectionElementKind.Point,
                _ => SelectionElementKind.None
            };
            int? elementIndex = elementKind == SelectionElementKind.None
                ? null
                : identity.SubprimIndex;
            SelectionItem item;
            if (identity.InstancerContext.Length != 0)
            {
                ReadOnlySpan<SilkInstancerContextEntry> chain =
                    identity.InstancerContext;
                var levels = new SelectionInstancerEntry[chain.Length];
                for (int level = 0; level < chain.Length; level++)
                {
                    levels[level] = new SelectionInstancerEntry(
                        chain[level].InstancerPath,
                        chain[level].InstanceIndex);
                }
                item = SelectionItem.FromInstancerContext(
                    identity.Path,
                    levels,
                    elementIndex,
                    elementKind);
            }
            else
            {
                item = new SelectionItem(
                    identity.Path,
                    identity.InstancerPath,
                    identity.InstancerPath is null ? null : identity.InstanceIndex,
                    elementIndex,
                    elementKind);
            }
            pending.Complete(RenderPickResult.Hit(
                pending.Request,
                context.StateRevision,
                context.SceneRevision,
                item,
                backendKind: GetRenderBackendKind(),
                backendToken: readback.Token));
            _pickHits++;
        }
    }

    private void EnsurePickDeviceGeneration(SilkPickFrameBinding binding)
    {
        if (_pickingDevice is null ||
            _pickingDevice.PickDeviceGeneration == _pickDeviceGeneration)
        {
            return;
        }

        if (_pickReadbacks is not null && _inFlightPicks is not null)
        {
            while (_pickReadbacks.TryDiscard(
                out int slotIndex,
                out _))
            {
                PendingPick? pending = _inFlightPicks[slotIndex];
                _inFlightPicks[slotIndex] = null;
                ReleaseInFlightPickBuffers(slotIndex);
                if (pending is not null && !pending.IsCompleted)
                {
                    CompleteInfrastructureStale(
                        pending,
                        binding,
                        RenderPickStaleReason.ContextGeneration);
                }
            }
        }

        _pickDepthTarget?.Dispose();
        _pickColorTarget?.Dispose();
        _pickDepthTarget = null;
        _pickColorTarget = null;
        _pickTargetWidth = 0;
        _pickTargetHeight = 0;
        _pickReadbacks?.Dispose();
        foreach (ISilkPickGraphicsPipeline retired in _pickPipelines.Values)
        {
            retired.Dispose();
        }
        _pickPipelines.Clear();

        ISilkPickGraphicsPipeline pipeline = CreatePickPipeline(
            _pickingDevice,
            _shaderFormat,
            SilkPickPrimitiveTopology.TriangleList,
            SilkVertexLayoutDescriptor.PositionNormal,
            SilkPickDepthBias.None);
        SilkPickReadbackRing? readbacks = null;
        try
        {
            readbacks = new SilkPickReadbackRing(_pickingDevice);
        }
        catch
        {
            pipeline.Dispose();
            throw;
        }

        _pickPipelines[(
            SilkPickPrimitiveTopology.TriangleList,
            SilkVertexLayoutDescriptor.PositionNormal.Stride,
            SilkPickDepthBias.None,
            true)] = pipeline;
        _pickReadbacks = readbacks;
        _inFlightPicks = new PendingPick?[readbacks.Capacity];
        _inFlightPickBuffers = new List<ISilkGraphicsBuffer>?[readbacks.Capacity];
        _pickDeviceGeneration = readbacks.DeviceGeneration;
        _pickPipelineCreations++;
    }

    private void EnsurePickTargets(uint width, uint height)
    {
        if (_pickColorTarget is not null &&
            _pickDepthTarget is not null &&
            _pickTargetWidth == width &&
            _pickTargetHeight == height)
        {
            return;
        }

        ISilkGraphicsTexture? color = null;
        ISilkGraphicsTexture? depth = null;
        try
        {
            color = _device.CreateTexture2D(new SilkTextureDescriptor(
                width,
                height,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.ColorRenderTarget |
                    SilkTextureUsage.CopySource));
            depth = _device.CreateTexture2D(
                SilkTextureDescriptor.DepthTarget(width, height));
        }
        catch
        {
            depth?.Dispose();
            color?.Dispose();
            throw;
        }

        _pickDepthTarget?.Dispose();
        _pickColorTarget?.Dispose();
        _pickColorTarget = color;
        _pickDepthTarget = depth;
        _pickTargetWidth = width;
        _pickTargetHeight = height;
        _pickTargetCreations++;
    }

    private void PromoteQueuedPick()
    {
        while (_activePick?.IsCompleted == true)
        {
            _activePick.ReleaseCancellationRegistration();
            AdvancePickQueue();
        }
    }

    private void AdvancePickQueue()
    {
        _activePick = _pendingPick;
        _pendingPick = null;
    }

    private void CompleteStale(
        PendingPick pending,
        SilkPickFrameBinding binding)
    {
        pending.Complete(RenderPickResult.Stale(
            pending.Request,
            binding.StateRevision,
            binding.SceneRevision));
        _pickStaleResults++;
    }

    private void CompleteInfrastructureStale(
        PendingPick pending,
        SilkPickFrameBinding binding,
        RenderPickStaleReason staleReasons)
    {
        pending.Complete(RenderPickResult.Stale(
            pending.Request,
            binding.StateRevision,
            binding.SceneRevision,
            staleReasons));
        _pickStaleResults++;
    }

    private void CompleteUnsupported(
        PendingPick pending,
        SilkPickFrameBinding binding)
    {
        pending.Complete(RenderPickResult.Unsupported(
            pending.Request,
            binding.StateRevision,
            binding.SceneRevision));
        _pickUnsupportedResults++;
    }

    private static bool SupportsPickRequest(RenderPickRequest request) =>
        request.Target is RenderPickTarget.Primitive or
            RenderPickTarget.Face or
            RenderPickTarget.Edge or
            RenderPickTarget.Point &&
        request.Flags == RenderPickOptions.None;

    private static void ValidatePickRequest(RenderPickRequest request)
    {
        if (request.Width != 1 ||
            request.Height != 1 ||
            request.Viewport.Width <= 0 ||
            request.Viewport.Height <= 0 ||
            request.X < 0 ||
            request.Y < 0 ||
            request.X >= request.Viewport.Width ||
            request.Y >= request.Viewport.Height ||
            request.Target is not (
                RenderPickTarget.Primitive or
                RenderPickTarget.Face or
                RenderPickTarget.Edge or
                RenderPickTarget.Point) ||
            (request.Flags & ~RenderPickOptions.CullBackFaces) != 0)
        {
            throw new ArgumentException(
                "The Silk pick request is not a valid renderer-neutral one-pixel request.",
                nameof(request));
        }
    }

    private RenderBackendKind GetRenderBackendKind() =>
        _device.Backend switch
        {
            SilkGraphicsBackend.D3D12 => RenderBackendKind.D3D12,
            SilkGraphicsBackend.Vulkan => RenderBackendKind.Vulkan,
            SilkGraphicsBackend.Metal => RenderBackendKind.Metal,
            _ => throw new NotSupportedException(
                $"Unsupported Silk graphics backend '{_device.Backend}'.")
        };

    private static void ValidateTargets(
        ISilkGraphicsTexture colorTarget,
        ISilkGraphicsTexture depthTarget)
    {
        ArgumentNullException.ThrowIfNull(colorTarget);
        ArgumentNullException.ThrowIfNull(depthTarget);
        if (!SilkTextureFormats.IsColorRenderTarget(colorTarget.Format) ||
            (colorTarget.Usage & SilkTextureUsage.ColorRenderTarget) == 0)
        {
            throw new ArgumentException(
                "The color target must use a supported color render-target format.",
                nameof(colorTarget));
        }
        if (depthTarget.Format != SilkTextureFormat.D32Float ||
            (depthTarget.Usage & SilkTextureUsage.DepthRenderTarget) == 0)
        {
            throw new ArgumentException(
                "The depth target must be a D32Float depth-stencil texture.",
                nameof(depthTarget));
        }
        if (colorTarget.Width != depthTarget.Width || colorTarget.Height != depthTarget.Height)
        {
            throw new ArgumentException("Color and depth target dimensions must match.", nameof(depthTarget));
        }
    }

    private static void ValidateOptions(SilkMeshRenderOptions options)
    {
        options.ClearColor.Validate();
        if (!float.IsFinite(options.ClearDepth) ||
            options.ClearDepth < 0 ||
            options.ClearDepth > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Clear depth must be between zero and one.");
        }
        if (options.OutputTransform is < RenderOutputTransform.Identity or > RenderOutputTransform.Reinhard)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The output transform is unknown.");
        }
        if (!float.IsFinite(options.Exposure))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Exposure must be finite.");
        }
        if (options.DisplayTransform is not null &&
            options.OutputTransform != RenderOutputTransform.Identity)
        {
            throw new InvalidOperationException(
                "SilkMeshRenderOptions.OutputTransform must be Identity when a display " +
                "transform is set. A built-in output transform alongside a colour-managed " +
                "display transform would convert the image twice.");
        }
    }

    private static SilkShaderBinaryFormat GetShaderFormat(ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return device.Backend switch
        {
            SilkGraphicsBackend.D3D12 => SilkShaderBinaryFormat.Dxil,
            SilkGraphicsBackend.Vulkan => SilkShaderBinaryFormat.SpirV,
            SilkGraphicsBackend.Metal => SilkShaderBinaryFormat.MetalLibrary,
            _ => throw new NotSupportedException($"Unsupported Silk graphics backend '{device.Backend}'."),
        };
    }

    private sealed class PendingPick
    {
        private readonly TaskCompletionSource<RenderPickResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationToken _cancellationToken;
        private readonly CancellationTokenRegistration _cancellationRegistration;

        internal PendingPick(
            RenderPickRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            _cancellationToken = cancellationToken;
            if (cancellationToken.CanBeCanceled)
            {
                _cancellationRegistration = cancellationToken.Register(
                    static state => ((PendingPick)state!).CancelFromToken(),
                    this);
            }
        }

        internal RenderPickRequest Request { get; }

        internal bool IsCompleted => _completion.Task.IsCompleted;

        internal ValueTask<RenderPickResult> AsValueTask() =>
            new(_completion.Task);

        internal void Complete(RenderPickResult result)
        {
            _ = _completion.TrySetResult(result);
            ReleaseCancellationRegistration();
        }

        internal void Fail(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            _ = _completion.TrySetException(exception);
            ReleaseCancellationRegistration();
        }

        internal void CancelAsSuperseded()
        {
            _ = _completion.TrySetCanceled();
            ReleaseCancellationRegistration();
        }

        internal void ReleaseCancellationRegistration() =>
            _cancellationRegistration.Dispose();

        private void CancelFromToken() =>
            _ = _completion.TrySetCanceled(_cancellationToken);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
