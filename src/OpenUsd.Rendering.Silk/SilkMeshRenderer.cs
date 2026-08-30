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
    int InFlightReadbacks);

/// <summary>
/// Owns retained mesh buffers and the checked mesh graphics pipeline for one RHI device.
/// </summary>
public sealed class SilkMeshRenderer :
    ISilkRenderTargetRenderer,
    IRenderPickingBackend
{
    private readonly object _gate = new();
    private readonly ISilkGraphicsDevice _device;
    private readonly ISilkGraphicsShaderModule _vertexShader;
    private readonly ISilkGraphicsShaderModule _fragmentShader;
    private readonly ISilkGraphicsBindingLayout _bindingLayout;
    private readonly ISilkGraphicsShaderProgram _program;
    private readonly ISilkGraphicsPipeline _pipeline;
    private readonly ISilkGraphicsPipeline _backCullPipeline;
    private readonly ISilkGraphicsPipeline _linePipeline;
    private readonly ISilkGraphicsPipeline _pointPipeline;
    private readonly SilkGraphicsPipelineCache _pipelineCache;
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
    private ISilkPickGraphicsPipeline? _pickPipeline;
    private SilkPickReadbackRing? _pickReadbacks;
    private PendingPick?[]? _inFlightPicks;
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
    private SelectionState _selection = SelectionState.Empty;
    private SilkSelectionOutlineSettings _selectionOutlineSettings =
        SilkSelectionOutlineSettings.Default;
    private int _selectionItemCount;
    private SilkMeshGpuResource?[] _selectedMeshes = [];
    private int _selectedMeshCount;
    private int _missingSelectionPathCount;
    private bool _selectionResolutionDirty = true;
    private ulong _selectionResolvedGpuRevision = ulong.MaxValue;
    private ulong _selectionRevision;
    private SilkSelectionOutlineStatus _selectionOutlineStatus =
        SilkSelectionOutlineStatus.EmptySelection;
    private ISilkSelectionMaskGraphicsPipeline? _selectionMaskPipeline;
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

        ISilkGraphicsShaderModule? vertexShader = null;
        ISilkGraphicsShaderModule? fragmentShader = null;
        ISilkGraphicsBindingLayout? bindingLayout = null;
        ISilkGraphicsShaderProgram? program = null;
        ISilkGraphicsPipeline? pipeline = null;
        ISilkGraphicsPipeline? backCullPipeline = null;
        ISilkGraphicsPipeline? linePipeline = null;
        ISilkGraphicsPipeline? pointPipeline = null;
        ISilkPickGraphicsPipeline? pickPipeline = null;
        SilkPickReadbackRing? pickReadbacks = null;
        try
        {
            vertexShader = device.CreateShaderModule(
                SilkCheckedShaderAssets.LoadMeshVertex(shaderFormat));
            fragmentShader = device.CreateShaderModule(
                SilkCheckedShaderAssets.LoadMeshFragment(shaderFormat));
            bindingLayout = device.CreateBindingLayout(SilkBindingLayoutDescriptor.SceneParameters);
            program = device.CreateShaderProgram(new SilkShaderProgramDescriptor(
                vertexShader,
                fragmentShader,
                bindingLayout));
            pipeline = device.CreateGraphicsPipeline(new SilkGraphicsPipelineDescriptor(
                program,
                SilkVertexLayoutDescriptor.PositionNormal,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureFormat.D32Float));
            backCullPipeline = device.CreateGraphicsPipeline(new SilkGraphicsPipelineDescriptor(
                program,
                SilkVertexLayoutDescriptor.PositionNormal,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureFormat.D32Float,
                SilkCullMode.Back));
            // Lines are never culled: Storm rasterizes curve segments as
            // screen-space lines, which have no facing to cull against.
            linePipeline = device.CreateGraphicsPipeline(
                new SilkGraphicsPipelineDescriptor(
                    program,
                    SilkVertexLayoutDescriptor.PositionNormal,
                    SilkTextureFormat.Rgba8Unorm,
                    SilkTextureFormat.D32Float,
                    SilkCullMode.None,
                    SilkTopologyKind.LineList));
            pointPipeline = device.CreateGraphicsPipeline(
                new SilkGraphicsPipelineDescriptor(
                    program,
                    SilkVertexLayoutDescriptor.PositionNormal,
                    SilkTextureFormat.Rgba8Unorm,
                    SilkTextureFormat.D32Float,
                    SilkCullMode.None,
                    SilkTopologyKind.PointList));
            if (_pickingDevice is not null)
            {
                SilkPickPipelineDescriptor pickDescriptor =
                    SilkPickPipelineDescriptor.CreateChecked(shaderFormat);
                pickDescriptor.Validate();
                pickPipeline = _pickingDevice.CreatePickGraphicsPipeline(
                    pickDescriptor);
                pickReadbacks = new SilkPickReadbackRing(_pickingDevice);
            }
        }
        catch
        {
            pickReadbacks?.Dispose();
            pickPipeline?.Dispose();
            backCullPipeline?.Dispose();
            linePipeline?.Dispose();
            pointPipeline?.Dispose();
            pipeline?.Dispose();
            program?.Dispose();
            bindingLayout?.Dispose();
            fragmentShader?.Dispose();
            vertexShader?.Dispose();
            GpuResources.Dispose();
            throw;
        }

        _vertexShader = vertexShader;
        _fragmentShader = fragmentShader;
        _bindingLayout = bindingLayout;
        _program = program;
        _pipeline = pipeline;
        _backCullPipeline = backCullPipeline;
        _linePipeline = linePipeline;
        _pointPipeline = pointPipeline;
        _pipelineCache = new SilkGraphicsPipelineCache(device, shaderFormat);
        _materialShaderGenerator = new SilkProjectedMaterialShaderGenerator();
        _materialShaderCompiler = new SilkMaterialShaderCompilerService(_materialShaderGenerator);
        _pickPipeline = pickPipeline;
        _pickReadbacks = pickReadbacks;
        if (pickReadbacks is not null)
        {
            _inFlightPicks = new PendingPick?[pickReadbacks.Capacity];
            _pickDeviceGeneration = pickReadbacks.DeviceGeneration;
            _pickPipelineCreations = 1;
        }
    }

    /// <summary>Gets the retained CPU scene.</summary>
    public SilkSceneState Scene { get; }

    /// <summary>Gets the retained GPU resources and upload diagnostics.</summary>
    public SilkSceneGpuResources GpuResources { get; }

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
                    _pickReadbacks?.InFlightCount ?? 0);
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
                }
            }
            _pickDepthTarget?.Dispose();
            _pickColorTarget?.Dispose();
            _pickReadbacks?.Dispose();
            _pickPipeline?.Dispose();
            DisposeSelectionOutlineInfrastructure();
            GpuResources.Dispose();
            _materialShaderCompiler.Dispose();
            _pipelineCache.Dispose();
            _backCullPipeline.Dispose();
            _pipeline.Dispose();
            _linePipeline.Dispose();
            _pointPipeline.Dispose();
            _program.Dispose();
            _bindingLayout.Dispose();
            _fragmentShader.Dispose();
            _vertexShader.Dispose();

            // Batch keys reference geometry resources the GPU scene has just disposed, so the table
            // is emptied here rather than left holding them for the lifetime of the renderer.
            _batches.Clear();
            _batchPool.Clear();
            _batchOrder.Clear();
        }
    }

    private SilkMeshRenderResult RenderCore(
        ISilkGraphicsTexture colorTarget,
        ISilkGraphicsTexture depthTarget,
        SilkMeshRenderOptions options,
        SilkPickFrameBinding? pickBinding,
        bool renderSelectionOutline = true)
    {
        ValidateTargets(colorTarget, depthTarget);
        ValidateOptions(options);
        SyncPhysicsDeformations();
        int uniformUploads = GpuResources.UpdateUniforms(Scene.Frame, PhysicsOverrides);
        ISilkGraphicsBuffer frameBuffer = GpuResources.RequireFrameBuffer(
            Scene.Frame,
            options.OutputTransform,
            options.Exposure);
        bool shouldRenderSelectionOutline =
            renderSelectionOutline &&
            PrepareSelectionOutline(colorTarget, depthTarget);
        using ISilkGraphicsCommandList commands = _device.CreateCommandList();
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

        SilkCullMode resolveCullMode(SilkMeshData mesh) =>
            options.BackfaceCulling ? GetCullMode(mesh) : SilkCullMode.None;

        bool resolveSampledVolume(SilkMeshData mesh) =>
            options.UseSceneMaterials && IsSampledVolumeMesh(mesh);

        if (singleMesh is not null)
        {
            PrepareMaterialTextures(commands, singleMesh, resolveMaterialFeatures(singleMesh.Mesh));
        }

        if (singleMesh is null)
        {
            // Dictionary.Clear above kept its capacity, so refilling it allocates nothing once the
            // scene has been drawn once.
            foreach (SilkMeshGpuResource mesh in GpuResources.MeshValues)
            {
                if (mesh.IndexCount == 0)
                {
                    continue;
                }
                BatchKey key = new(
                    mesh.Geometry,
                    mesh.Mesh.MaterialPath,
                    resolveMaterialFeatures(mesh.Mesh),
                    resolveMaterialShaderIdentity(mesh.Mesh),
                    resolveSampledVolume(mesh.Mesh),
                    resolveCullMode(mesh.Mesh),
                    mesh.Mesh.TopologyKind);
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
                options.OutputTransform,
                options.Exposure));
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
        string? boundSurfaceMaterialPath = null;
        if (singleMesh is not null)
        {
            SilkShaderFeatures features = resolveMaterialFeatures(singleMesh.Mesh);
            SilkMaterialShaderRequest? materialShader = options.UseSceneMaterials
                ? GetMaterialShaderRequest(singleMesh.Mesh, features)
                : null;
            SilkCullMode cullMode = resolveCullMode(singleMesh.Mesh);
            ISilkGraphicsPipeline pipeline = GetPipeline(
                singleMesh,
                features,
                IsSampledVolumeMesh(singleMesh.Mesh),
                cullMode,
                singleMesh.Mesh.TopologyKind,
                colorTarget.Format,
                materialShader);
            commands.SetGraphicsPipeline(pipeline);
            DisposePipelineLease(pipeline);
            boundPipeline = new PipelineKey(
                features,
                IsSampledVolumeMesh(singleMesh.Mesh),
                cullMode,
                singleMesh.Mesh.TopologyKind,
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
            BindSurfaceBufferIfChanged(commands, singleMesh, ref boundSurfaceMaterialPath);
            BindMaterialResources(commands, singleMesh, features);
            commands.DrawIndexed(singleMesh.IndexCount);
            drawCount++;
            commands.EndRendering();
            if (selectionCommands is not null)
            {
                RecordSelectionOutline(
                    commands,
                    selectionCommands,
                    colorTarget,
                    depthTarget);
            }

            using ISilkGraphicsSubmission singleSubmission = _device.Submit(commands);
            singleSubmission.Wait();
            // Safe: Wait() returning means no unsubmitted or in-flight execution referencing
            // these textures remains, so completing this submission's lease makes disposing them
            // safe even though `commands` itself is still alive in this `using` scope. See
            // SilkSceneGpuResources.TrimTextureResidency.
            GpuResources.TrimTextureResidency();
            if (pickBinding is { } singleBinding)
            {
                ProcessPicking(colorTarget, singleBinding);
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
                    BindSurfaceBufferIfChanged(commands, mesh, ref boundSurfaceMaterialPath);
                    BindMaterialResources(commands, mesh, key.Features);
                    commands.DrawIndexed(mesh.IndexCount);
                    drawCount++;
                }
                continue;
            }
            key.Geometry.UpdateInstanceBuffer(
                _device,
                Scene.Frame,
                meshes,
                _device.ClipSpaceYPointsDown);
            BindPipelineIfChanged(
                commands,
                first,
                key,
                colorTarget.Format,
                ref boundPipeline);
            commands.SetVertexBuffer(first.VertexBuffer);
            commands.SetIndexBuffer(first.IndexBuffer);
            commands.SetUniformBuffer(0, 0, first.UniformBuffer);
            commands.SetStorageBuffer(0, 6, key.Geometry.RequireInstanceBuffer());
            commands.SetStorageBuffer(
                0,
                SilkBindingLayoutDescriptor.FrameParametersBinding,
                frameBuffer);
            BindSurfaceBufferIfChanged(commands, first, ref boundSurfaceMaterialPath);
            BindMaterialResources(commands, first, key.Features);
            commands.DrawIndexedInstanced(first.IndexCount, checked((uint)meshes.Count));
            drawCount++;
        }
        commands.EndRendering();
        if (selectionCommands is not null)
        {
            RecordSelectionOutline(
                commands,
                selectionCommands,
                colorTarget,
                depthTarget);
        }

        using ISilkGraphicsSubmission submission = _device.Submit(commands);
        submission.Wait();
        // Safe: Wait() returning means no unsubmitted or in-flight execution referencing these
        // textures remains, so completing this submission's lease makes disposing them safe even
        // though `commands` itself is still alive in this `using` scope. See
        // SilkSceneGpuResources.TrimTextureResidency.
        GpuResources.TrimTextureResidency();
        if (pickBinding is { } binding)
        {
            ProcessPicking(colorTarget, binding);
        }
        return new SilkMeshRenderResult(drawCount, uniformUploads, GpuResources.Statistics);
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
                : GetMaterialShaderRequest(mesh.Mesh, key.Features));
        commands.SetGraphicsPipeline(pipeline);
        DisposePipelineLease(pipeline);
        boundPipeline = next;
    }

    private void BindSurfaceBufferIfChanged(
        ISilkGraphicsCommandList commands,
        SilkMeshGpuResource mesh,
        ref string? boundSurfaceMaterialPath)
    {
        string materialPath = mesh.Mesh.MaterialPath;
        if (string.Equals(boundSurfaceMaterialPath, materialPath, StringComparison.Ordinal))
        {
            return;
        }

        commands.SetStorageBuffer(
            0,
            SilkBindingLayoutDescriptor.SurfaceParametersBinding,
            GpuResources.RequireSurfaceBuffer(Scene, mesh.Mesh, RenderHeadlight.Deterministic));
        boundSurfaceMaterialPath = materialPath;
    }

    private static int CompareBatchKeys(BatchKey left, BatchKey right)
    {
        int result = left.TopologyKind.CompareTo(right.TopologyKind);
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
        return left.Geometry.Key.TopologyFingerprint.CompareTo(
            right.Geometry.Key.TopologyFingerprint);
    }

    private static SilkCullMode GetCullMode(SilkMeshData mesh) =>
        mesh.CullStyle switch
        {
            SilkMeshCullStyle.Nothing => SilkCullMode.None,
            SilkMeshCullStyle.Back => SilkCullMode.Back,
            SilkMeshCullStyle.BackUnlessDoubleSided => mesh.DoubleSided ? SilkCullMode.None : SilkCullMode.Back,
            _ => mesh.DoubleSided ? SilkCullMode.None : SilkCullMode.Back,
        };

    // Lines and points carry no facing, so those batches always use the
    // unculled pipeline for their topology regardless of authored cull style.
    private ISilkGraphicsPipeline GetPipeline(
        SilkMeshGpuResource mesh,
        SilkShaderFeatures features,
        bool sampledVolume,
        SilkCullMode cullMode,
        SilkTopologyKind topologyKind,
        SilkTextureFormat colorFormat,
        SilkMaterialShaderRequest? materialShader = null)
    {
        if (materialShader?.Status == SilkMaterialShaderStatus.Ready)
        {
            return _pipelineCache.GetOrCreateMaterialPipeline(
                materialShader.Program,
                mesh.VertexLayout,
                colorFormat,
                SilkTextureFormat.D32Float,
                cullMode,
                topologyKind);
        }
        if (features == SilkShaderFeatures.None &&
            !sampledVolume &&
            mesh.VertexLayout.Equals(SilkVertexLayoutDescriptor.PositionNormal) &&
            colorFormat == SilkTextureFormat.Rgba8Unorm)
        {
            return topologyKind switch
            {
                SilkTopologyKind.LineList => _linePipeline,
                SilkTopologyKind.PointList => _pointPipeline,
                SilkTopologyKind.TriangleList =>
                    cullMode == SilkCullMode.Back ? _backCullPipeline : _pipeline,
                _ => throw new InvalidDataException(
                    $"Unsupported Silk topology kind '{topologyKind}'.")
            };
        }
        if (features == SilkShaderFeatures.None && sampledVolume)
        {
            return _pipelineCache.GetOrCreateSampledVolumePipeline(
                mesh.VertexLayout,
                colorFormat,
                SilkTextureFormat.D32Float,
                cullMode,
                topologyKind);
        }
        return _pipelineCache.GetOrCreateMeshPipeline(
            new SilkShaderPermutationId(features),
            mesh.VertexLayout,
            colorFormat,
            SilkTextureFormat.D32Float,
            cullMode,
            topologyKind);
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
            material.SurfaceKind == SilkSurfaceKind.MaterialXGenerated
                ? "MaterialXGeneratedBackendFragment.v2"
                : "MaterialXProjectedPreviewSurface.v1");

    private static string GetPipelineShaderIdentity(SilkMaterialShaderRequest? materialShader) =>
        materialShader?.Status == SilkMaterialShaderStatus.Ready
            ? materialShader.Program.CacheHash
            : string.Empty;

    private void BindMaterialResources(
        ISilkGraphicsCommandList commands,
        SilkMeshGpuResource mesh,
        SilkShaderFeatures features)
    {
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
                SilkMaterialParameter.SpecularColor)
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
        SilkCullMode CullMode,
        SilkTopologyKind TopologyKind);

    private readonly record struct PipelineKey(
        SilkShaderFeatures Features,
        bool SampledVolume,
        SilkCullMode CullMode,
        SilkTopologyKind TopologyKind,
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
        else if (!_selectionOutlineSettings.VisibleOnly)
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
        if (!_selectionOutlineSettings.VisibleOnly)
        {
            _selectionOutlineStatus = SilkSelectionOutlineStatus.XRayUnsupported;
            _unsupportedXRayRequests++;
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

        IReadOnlyList<SelectionItem> items = _selection.Items;
        var resolved = new List<SilkMeshGpuResource>(items.Count);
        int missing = 0;
        for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            string path = items[itemIndex].PrimPath;

            // A point-instanced prototype contributes one retained mesh per
            // instance, and selecting the prototype path highlights all of them.
            IReadOnlyList<SilkMeshData> instances = Scene.GetInstances(path);
            if (instances.Count == 0)
            {
                missing++;
                continue;
            }

            bool resolvedAnyInstance = false;
            for (int instance = 0; instance < instances.Count; instance++)
            {
                if (!GpuResources.Meshes.TryGetValue(
                        instances[instance].Id,
                        out SilkMeshGpuResource? resource))
                {
                    continue;
                }
                resolvedAnyInstance = true;
                if (resource.IndexCount == 0 ||
                    instances[instance].TopologyKind is SilkTopologyKind.LineList or
                        SilkTopologyKind.PointList)
                {
                    continue;
                }

                bool duplicate = false;
                for (int resolvedIndex = 0; resolvedIndex < resolved.Count; resolvedIndex++)
                {
                    if (ReferenceEquals(resolved[resolvedIndex], resource))
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate)
                {
                    resolved.Add(resource);
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

        _selectionMaskPipeline = maskPipeline;
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

            var bindingDescriptor = new SilkSelectionOutlineBindingDescriptor(
                bindingMask,
                depthTarget,
                _selectionOutlineSampler ??
                    throw new InvalidOperationException(
                        "The selection outline sampler is missing."),
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
        if (_selectionOutlineParametersInitialized &&
            bytes.SequenceEqual(_selectionOutlineParameterBytes))
        {
            return;
        }

        parameters.Write(bytes);
        bytes.CopyTo(_selectionOutlineParameterBytes);
        _selectionOutlineParametersInitialized = true;
        _selectionParameterUploads++;
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
        ISilkSelectionMaskGraphicsPipeline maskPipeline =
            _selectionMaskPipeline ??
            throw new InvalidOperationException(
                "The selection mask pipeline is missing.");
        ISilkSelectionOutlineGraphicsPipeline outlinePipeline =
            _selectionOutlinePipeline ??
            throw new InvalidOperationException(
                "The selection outline pipeline is missing.");
        ISilkSelectionOutlineBinding binding = _selectionOutlineBinding ??
            throw new InvalidOperationException(
                "The selection outline binding is missing.");

        commands.ClearColor(maskTarget, new SilkColor(0, 0, 0, 0));
        var maskRendering = new SilkSelectionMaskRenderingDescriptor(
            maskTarget,
            depthTarget);
        maskRendering.Validate();
        selectionCommands.BeginSelectionMaskRendering(maskRendering);
        selectionCommands.SetSelectionMaskGraphicsPipeline(maskPipeline);
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
        for (int index = 0; index < _selectedMeshCount; index++)
        {
            SilkMeshGpuResource mesh = _selectedMeshes[index] ??
                throw new InvalidOperationException(
                    "A resolved selected mesh entry is missing.");
            commands.SetVertexBuffer(mesh.VertexBuffer);
            commands.SetIndexBuffer(mesh.IndexBuffer);
            commands.SetUniformBuffer(0, 0, mesh.UniformBuffer);
            commands.DrawIndexed(mesh.IndexCount);
            selectedDraws++;
        }
        commands.EndRendering();

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

        _selectionMaskPasses++;
        _selectionOutlinePasses++;
        _selectionDraws += checked((ulong)selectedDraws);
        _selectionOutlineStatus = SilkSelectionOutlineStatus.Rendered;
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
        _selectionMaskPipeline?.Dispose();
        _selectionMaskPipeline = null;
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
        ISilkPickGraphicsPipeline pickPipeline = _pickPipeline ??
            throw new InvalidOperationException(
                "The pick-capable device has no pick pipeline.");
        PendingPick?[] inFlight = _inFlightPicks ??
            throw new InvalidOperationException(
                "The pick-capable device has no in-flight slot table.");
        if (!readbacks.TryAcquire(out SilkPickReadbackReservation reservation))
        {
            _pickRingSaturations++;
            return;
        }

        ISilkGraphicsSubmission? pickSubmission = null;
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
            commands.ClearColor(pickColor, new SilkColor(0, 0, 0, 0));
            commands.ClearDepth(pickDepth, 1);
            commands.BeginRendering(new SilkRenderingDescriptor(pickColor, pickDepth));
            pickCommands.SetPickGraphicsPipeline(pickPipeline);
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

            foreach (SilkMeshGpuResource mesh in GpuResources.MeshValues)
            {
                if (mesh.IndexCount == 0 ||
                    mesh.Mesh.TopologyKind is SilkTopologyKind.LineList or SilkTopologyKind.PointList)
                {
                    continue;
                }
                if (!Scene.PickIdentities.TryGetRange(
                    mesh.Mesh.Path,
                    mesh.Mesh.InstanceIndex,
                    out SilkPickTokenRange tokenRange))
                {
                    throw new InvalidDataException(
                        $"Mesh '{mesh.Mesh.Path}' has no active Silk pick token range.");
                }
                if (tokenRange.FirstToken == 0 ||
                    tokenRange.TokenCount != mesh.IndexCount / 3)
                {
                    throw new InvalidDataException(
                        $"Mesh '{mesh.Mesh.Path}' has an inconsistent Silk pick token range.");
                }

                commands.SetVertexBuffer(mesh.VertexBuffer);
                commands.SetIndexBuffer(mesh.IndexBuffer);
                commands.SetUniformBuffer(0, 0, mesh.UniformBuffer);
                pickCommands.SetPickBaseToken(tokenRange.FirstToken);
                commands.DrawIndexed(mesh.IndexCount);
            }
            commands.EndRendering();

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
            _pickPassesRecorded++;
            AdvancePickQueue();
        }
        catch (Exception exception)
        {
            pickSubmission?.Dispose();
            readbacks.Cancel(reservation);
            active.Fail(exception);
            AdvancePickQueue();
            throw;
        }

        ResolveCompletedReadbacks(binding, viewport);
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

            SelectionItem item = pending.Request.Target == RenderPickTarget.Face
                ? new SelectionItem(
                    identity.Path,
                    elementIndex: identity.SubprimIndex)
                : new SelectionItem(identity.Path);
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
        _pickPipeline?.Dispose();

        SilkPickPipelineDescriptor descriptor =
            SilkPickPipelineDescriptor.CreateChecked(_shaderFormat);
        descriptor.Validate();
        ISilkPickGraphicsPipeline pipeline =
            _pickingDevice.CreatePickGraphicsPipeline(descriptor);
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

        _pickPipeline = pipeline;
        _pickReadbacks = readbacks;
        _inFlightPicks = new PendingPick?[readbacks.Capacity];
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
        request.Target is RenderPickTarget.Primitive or RenderPickTarget.Face &&
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
