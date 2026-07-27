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
    float ClearDepth)
{
    /// <summary>Gets opaque black with a far depth clear.</summary>
    public static SilkMeshRenderOptions Default { get; } =
        new(new SilkColor(0, 0, 0, 1), 1);
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
    private readonly SilkShaderBinaryFormat _shaderFormat;
    private readonly ISilkPickingGraphicsDevice? _pickingDevice;
    private readonly ISilkSelectionOutlineGraphicsDevice? _selectionOutlineDevice;
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

    internal SilkMeshRenderer(
        ISilkGraphicsDevice device,
        SilkShaderBinaryFormat shaderFormat)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _shaderFormat = shaderFormat;
        _pickingDevice = device as ISilkPickingGraphicsDevice;
        _selectionOutlineDevice = device as ISilkSelectionOutlineGraphicsDevice;
        Scene = new SilkSceneState();
        GpuResources = new SilkSceneGpuResources(device);

        ISilkGraphicsShaderModule? vertexShader = null;
        ISilkGraphicsShaderModule? fragmentShader = null;
        ISilkGraphicsBindingLayout? bindingLayout = null;
        ISilkGraphicsShaderProgram? program = null;
        ISilkGraphicsPipeline? pipeline = null;
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
            _pipeline.Dispose();
            _program.Dispose();
            _bindingLayout.Dispose();
            _fragmentShader.Dispose();
            _vertexShader.Dispose();
        }
    }

    private SilkMeshRenderResult RenderCore(
        ISilkGraphicsTexture colorTarget,
        ISilkGraphicsTexture depthTarget,
        SilkMeshRenderOptions options,
        SilkPickFrameBinding? pickBinding)
    {
        ValidateTargets(colorTarget, depthTarget);
        ValidateOptions(options);
        int uniformUploads = GpuResources.UpdateUniforms(Scene.Frame);
        bool renderSelectionOutline = PrepareSelectionOutline(depthTarget);
        using ISilkGraphicsCommandList commands = _device.CreateCommandList();
        ISilkSelectionOutlineGraphicsCommandList? selectionCommands = null;
        if (renderSelectionOutline)
        {
            selectionCommands = commands as ISilkSelectionOutlineGraphicsCommandList ??
                throw new InvalidOperationException(
                    "A selection-outline-capable device must create " +
                    "selection-outline-capable command lists.");
        }
        commands.ClearColor(colorTarget, options.ClearColor);
        commands.ClearDepth(depthTarget, options.ClearDepth);
        commands.BeginRendering(new SilkRenderingDescriptor(colorTarget, depthTarget));
        commands.SetGraphicsPipeline(_pipeline);
        commands.SetViewport(new SilkViewport(
            0,
            0,
            colorTarget.Width,
            colorTarget.Height,
            0,
            1));
        commands.SetScissor(new SilkScissor(0, 0, colorTarget.Width, colorTarget.Height));

        int drawCount = 0;
        foreach (SilkMeshGpuResource mesh in GpuResources.MeshValues)
        {
            if (mesh.IndexCount == 0)
            {
                continue;
            }
            commands.SetVertexBuffer(mesh.VertexBuffer);
            commands.SetIndexBuffer(mesh.IndexBuffer);
            commands.SetUniformBuffer(0, 0, mesh.UniformBuffer);
            commands.DrawIndexed(mesh.IndexCount);
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
        if (pickBinding is { } binding)
        {
            ProcessPicking(colorTarget, binding);
        }
        return new SilkMeshRenderResult(drawCount, uniformUploads, GpuResources.Statistics);
    }

    private void ApplySceneDelta(SilkSceneDelta delta)
    {
        GpuResources.Apply(Scene, delta);
        if (delta.MeshUpserts != 0 || delta.MeshRemovals != 0)
        {
            _selectionResolutionDirty = true;
        }
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

    private bool PrepareSelectionOutline(ISilkGraphicsTexture depthTarget)
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

        EnsureSelectionOutlineInfrastructure(outlineDevice);
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
                if (resource.IndexCount == 0)
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
        ISilkSelectionOutlineGraphicsDevice outlineDevice)
    {
        ulong generation = outlineDevice.SelectionOutlineDeviceGeneration;
        if (_selectionOutlineInfrastructureInitialized &&
            generation == _selectionOutlineDeviceGeneration)
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
            SilkSelectionOutlinePipelineDescriptor.CreateChecked(_shaderFormat);
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
                if (mesh.IndexCount == 0)
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
        if (colorTarget.Format != SilkTextureFormat.Rgba8Unorm ||
            (colorTarget.Usage & SilkTextureUsage.ColorRenderTarget) == 0)
        {
            throw new ArgumentException(
                "The color target must be an RGBA8 render-target texture.",
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
