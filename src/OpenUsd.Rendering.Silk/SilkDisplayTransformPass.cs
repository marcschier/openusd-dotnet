// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Owns the GPU resources of the fullscreen colour-managed display transform for one
/// renderer: the linear intermediate the scene is drawn into, the baked lattice
/// texture, the sampler, the constant buffer, the checked pipeline, and the binding.
/// </summary>
/// <remarks>
/// <para>
/// The scene is never drawn straight into the display target when a display transform
/// is active. It is drawn into an owned RGBA16Float intermediate at the target's
/// dimensions, and this pass reads that intermediate and writes the display target,
/// so the transform is applied exactly once, to finished linear colour, with explicit
/// resource transitions supplied by the backend.
/// </para>
/// <para>
/// Nothing here fails silently. Every reason a transform did not run is recorded as a
/// <see cref="SilkDisplayTransformStatus"/> plus one bounded
/// <see cref="RenderDiagnostic"/>, and the renderer falls back to writing untransformed
/// linear colour rather than pretending an identity result was the requested transform.
/// </para>
/// </remarks>
internal sealed class SilkDisplayTransformPass : IDisposable
{
    private readonly ISilkGraphicsDevice _device;
    private readonly ISilkDisplayTransformGraphicsDevice? _displayDevice;
    private readonly SilkShaderBinaryFormat _shaderFormat;
    private readonly SilkDisplayTransformLatticeCache _lattices;
    private readonly byte[] _parameterBytes =
        new byte[SilkDisplayTransformUniformWriter.ByteSize];

    private ISilkDisplayTransformGraphicsPipeline? _pipeline;
    private ISilkDisplayTransformBinding? _binding;
    private ISilkGraphicsTexture? _sceneTarget;
    private ISilkGraphicsTexture? _latticeTexture;
    private ISilkGraphicsSampler? _sampler;
    private ISilkGraphicsBuffer? _parameters;
    private SilkDisplayTransformLattice? _lattice;
    private IDisposable? _latticeOwnership;
    private PreparedPage? _pagePreparation;
    private bool _publishedPage;
    private SilkTextureFormat _pipelineColorFormat;
    private ulong _deviceGeneration;
    private bool _pipelineInitialized;
    private bool _parametersInitialized;

    private SilkDisplayTransformStatus _status = SilkDisplayTransformStatus.Inactive;
    private string? _requestKey;
    private RenderDiagnostic? _diagnostic;
    private ulong _passes;
    private ulong _latticeUploads;
    private ulong _pipelineCreations;
    private ulong _bindingCreations;
    private ulong _intermediateCreations;
    private ulong _parameterUploads;
    private ulong _deviceInvalidations;
    private ulong _failures;
    private bool _disposed;

    internal SilkDisplayTransformPass(
        ISilkGraphicsDevice device,
        SilkShaderBinaryFormat shaderFormat,
        SilkDisplayTransformLatticeCache? lattices = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _displayDevice = device as ISilkDisplayTransformGraphicsDevice;
        _shaderFormat = shaderFormat;
        SilkCpuTextureBudget? budget = (device as ISilkCpuTextureAdmissionDevice)?.CpuTextureBudget;
        if (lattices is not null && !ReferenceEquals(lattices.CpuBudget, budget))
        {
            throw new ArgumentException("The display lattice cache must use the device's CPU pixel budget.",
                nameof(lattices));
        }
        _lattices = lattices ?? (budget is null
            ? new SilkDisplayTransformLatticeCache() : new SilkDisplayTransformLatticeCache(budget, provider: null));
    }

    /// <summary>Gets the latest bounded diagnostic, or null when there is none.</summary>
    internal RenderDiagnostic? Diagnostic => _diagnostic;

    /// <summary>Gets cumulative display-transform evidence.</summary>
    internal SilkDisplayTransformDiagnostics Diagnostics => new(
        _status,
        _lattice?.Size ?? 0,
        _lattice?.ByteCount ?? 0,
        _passes,
        _lattices.Builds,
        _lattices.Hits,
        _latticeUploads,
        _pipelineCreations,
        _bindingCreations,
        _intermediateCreations,
        _parameterUploads,
        _deviceInvalidations,
        _failures,
        _requestKey);

    /// <summary>
    /// Prepares every resource the pass needs and reports the linear intermediate the
    /// scene must be drawn into.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the pass will run and <paramref name="sceneTarget"/>
    /// is the intermediate to render into; <see langword="false"/> when the caller must
    /// render straight into the display target with no transform.
    /// </returns>
    internal bool TryPrepare(
        RenderDisplayTransform transform,
        ISilkGraphicsTexture displayTarget,
        float exposure,
        out ISilkGraphicsTexture sceneTarget)
    {
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentNullException.ThrowIfNull(displayTarget);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((_publishedPage || _pagePreparation is not null) && UsesAdmission &&
            MatchesPublishedRequest(transform, displayTarget, exposure))
        {
            sceneTarget = _sceneTarget!;
            return true;
        }
        bool prepared = TryPrepareCore(transform, new SilkTextureDescriptor(
            displayTarget.Width, displayTarget.Height, displayTarget.Format, displayTarget.Usage), exposure);
        sceneTarget = prepared ? _sceneTarget! : displayTarget;
        return prepared;
    }

    private bool MatchesPublishedRequest(
        RenderDisplayTransform transform, ISilkGraphicsTexture target, float exposure)
    {
        if (_status != SilkDisplayTransformStatus.Applied || !_pipelineInitialized || !_parametersInitialized ||
            _sceneTarget is null || _lattice is null || _sceneTarget.Width != target.Width ||
            _sceneTarget.Height != target.Height || _pipelineColorFormat != target.Format ||
            _displayDevice!.DisplayTransformDeviceGeneration != _deviceGeneration ||
            !string.Equals(_requestKey, transform.CacheKey, StringComparison.Ordinal))
        {
            return false;
        }
        Span<byte> parameters = stackalloc byte[SilkDisplayTransformUniformWriter.ByteSize];
        SilkDisplayTransformUniformWriter.Write(exposure, _lattice.ShaperMinimumLog2, _lattice.ShaperRangeLog2,
            _lattice.Size, _device.ClipSpaceYPointsDown, parameters);
        return parameters.SequenceEqual(_parameterBytes);
    }

    private bool TryPrepareCore(RenderDisplayTransform transform, SilkTextureDescriptor displayTarget, float exposure)
    {
        ArgumentNullException.ThrowIfNull(transform);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Recorded before anything can fail, so a published failure always names the
        // request it belongs to. A consumer that reconciles a menu against these
        // diagnostics has to be able to tell "the transform you asked for was refused"
        // from "a transform you have already replaced was refused", and a bare status
        // cannot carry that.
        _requestKey = transform.CacheKey;

        if (_displayDevice is null)
        {
            Fail(
                SilkDisplayTransformStatus.UnsupportedDevice,
                SilkRenderDiagnosticCodes.DisplayTransformDeviceUnsupported,
                "This graphics device cannot record a colour-managed display transform, " +
                "so untransformed linear colour was written instead.");
            return false;
        }

        IDisposable? ownership = null;
        try
        {
            SilkDisplayTransformLattice lattice;
            try
            {
                lattice = _lattices.GetOwned(transform, out ownership);
            }
            catch (SilkDisplayTransformException exception)
            {
                Fail(
                    exception.Status,
                    exception.Status == SilkDisplayTransformStatus.ConfigUnavailable
                        ? SilkRenderDiagnosticCodes.DisplayTransformConfigUnavailable
                        : SilkRenderDiagnosticCodes.DisplayTransformUnsupported,
                    exception.Message);
                return false;
            }

            if (UsesAdmission)
            {
                PrepareAdmitted(_displayDevice, displayTarget, exposure, lattice, ref ownership);
            }
            else
            {
                EnsureInfrastructure(_displayDevice, displayTarget.Format);
                EnsureLatticeTexture(lattice);
                EnsureSceneTarget(displayTarget);
                EnsureBinding(_displayDevice);
                UpdateParameters(exposure, lattice);
            }
        }
        finally
        {
            ownership?.Dispose();
        }

        _status = SilkDisplayTransformStatus.Applied;
        _diagnostic = null;
        return true;
    }

    internal PreparedPage? PreparePage(
        RenderDisplayTransform? transform, SilkTextureDescriptor target, float exposure)
    {
        if (!UsesAdmission)
        {
            return null;
        }
        if (_pagePreparation is not null)
        {
            throw new InvalidOperationException("Display resources are already being prepared for a page.");
        }
        SynchronizeGeneration();
        var prepared = new PreparedPage(this);
        _pagePreparation = prepared;
        try
        {
            if (transform is not null)
            {
                _ = TryPrepareCore(transform, target, exposure);
            }
            else
            {
                MarkInactive();
            }
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
                throw new AggregateException("Display page preparation and cleanup failed.", failure, cleanup);
            }
            throw;
        }
    }

    internal PreparedPage? PrepareFrame()
    {
        if (!UsesAdmission)
        {
            return null;
        }
        if (_pagePreparation is not null)
        {
            throw new InvalidOperationException("Display resources are already being prepared.");
        }
        SynchronizeGeneration();
        return _pagePreparation = new PreparedPage(this, publishPage: false);
    }

    internal sealed class PreparedPage : IDisposable
    {
        private readonly SilkDisplayTransformPass _owner;
        private readonly ISilkDisplayTransformGraphicsPipeline? _pipeline;
        private readonly ISilkDisplayTransformBinding? _binding;
        private readonly ISilkGraphicsTexture? _scene;
        private readonly ISilkGraphicsTexture? _texture;
        private readonly ISilkGraphicsSampler? _sampler;
        private readonly ISilkGraphicsBuffer? _parameters;
        private readonly SilkDisplayTransformLattice? _lattice;
        private readonly IDisposable? _ownership;
        private readonly byte[] _bytes;
        private readonly ulong _generation;
        private readonly SilkTextureFormat _format;
        private readonly bool _pipelineReady;
        private readonly bool _parametersReady;
        private readonly SilkDisplayTransformStatus _status;
        private readonly string? _request;
        private readonly RenderDiagnostic? _diagnostic;
        private readonly bool _publishPage;
        private bool _committed;
        private bool _disposed;

        internal PreparedPage(SilkDisplayTransformPass owner, bool publishPage = true)
        {
            _owner = owner;
            _publishPage = publishPage;
            _pipeline = owner._pipeline;
            _binding = owner._binding;
            _scene = owner._sceneTarget;
            _texture = owner._latticeTexture;
            _sampler = owner._sampler;
            _parameters = owner._parameters;
            _lattice = owner._lattice;
            _ownership = owner._latticeOwnership;
            _bytes = [.. owner._parameterBytes];
            _generation = owner._deviceGeneration;
            _format = owner._pipelineColorFormat;
            _pipelineReady = owner._pipelineInitialized;
            _parametersReady = owner._parametersInitialized;
            _status = owner._status;
            _request = owner._requestKey;
            _diagnostic = owner._diagnostic;
        }

        internal void Commit()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _owner._publishedPage |= _publishPage;
            _committed = true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _owner._pagePreparation = null;
            bool lost = _pipelineReady && _owner._displayDevice!.DisplayTransformDeviceGeneration != _generation;
            List<Exception>? errors = null;
            if (_committed || lost)
            {
                DisposeDifferent(_binding, _owner._binding, ref errors);
                DisposeDifferent(_scene, _owner._sceneTarget, ref errors);
                DisposeDifferent(_texture, _owner._latticeTexture, ref errors);
                DisposeDifferent(_parameters, _owner._parameters, ref errors);
                DisposeDifferent(_sampler, _owner._sampler, ref errors);
                DisposeDifferent(_pipeline, _owner._pipeline, ref errors);
                DisposeDifferent(_ownership, _owner._latticeOwnership, ref errors);
            }
            else
            {
                DisposeDifferent(_owner._binding, _binding, ref errors);
                DisposeDifferent(_owner._sceneTarget, _scene, ref errors);
                DisposeDifferent(_owner._latticeTexture, _texture, ref errors);
                DisposeDifferent(_owner._parameters, _parameters, ref errors);
                DisposeDifferent(_owner._sampler, _sampler, ref errors);
                DisposeDifferent(_owner._pipeline, _pipeline, ref errors);
                DisposeDifferent(_owner._latticeOwnership, _ownership, ref errors);
                _owner._binding = _binding;
                _owner._sceneTarget = _scene;
                _owner._latticeTexture = _texture;
                _owner._parameters = _parameters;
                _owner._sampler = _sampler;
                _owner._pipeline = _pipeline;
                _owner._lattice = _lattice;
                _owner._latticeOwnership = _ownership;
                _bytes.CopyTo(_owner._parameterBytes, 0);
                _owner._deviceGeneration = _generation;
                _owner._pipelineColorFormat = _format;
                _owner._pipelineInitialized = _pipelineReady;
                _owner._parametersInitialized = _parametersReady;
                _owner._status = _status;
                _owner._requestKey = _request;
                _owner._diagnostic = _diagnostic;
            }
            if (lost)
            {
                _owner.SynchronizeGeneration();
            }
            if (errors is not null)
            {
                throw new AggregateException("Display page resources could not all be released.", errors);
            }
        }
    }

    private bool UsesAdmission =>
        _device is ISilkBufferAdmissionDevice { GpuBufferBudget: not null } ||
        _device is ISilkTextureAdmissionDevice { GpuTextureBudget: not null } ||
        _device is ISilkStagingAdmissionDevice { GpuStagingBudget: not null } ||
        _device is ISilkCpuTextureAdmissionDevice { CpuTextureBudget: not null };

    private void PrepareAdmitted(
        ISilkDisplayTransformGraphicsDevice device, SilkTextureDescriptor target,
        float exposure, SilkDisplayTransformLattice lattice, ref IDisposable? ownership)
    {
        ulong generation = device.DisplayTransformDeviceGeneration;
        SynchronizeGeneration();
        ISilkDisplayTransformGraphicsPipeline? pipeline = _pipeline;
        ISilkDisplayTransformBinding? binding = _binding;
        ISilkGraphicsTexture? scene = _sceneTarget;
        ISilkGraphicsTexture? texture = _latticeTexture;
        ISilkGraphicsSampler? sampler = _sampler;
        ISilkGraphicsBuffer? parameters = _parameters;
        Span<byte> bytes = stackalloc byte[SilkDisplayTransformUniformWriter.ByteSize];
        SilkDisplayTransformUniformWriter.Write(exposure, lattice.ShaperMinimumLog2, lattice.ShaperRangeLog2,
            lattice.Size, _device.ClipSpaceYPointsDown, bytes);
        bool updateParameters = !_parametersInitialized || !bytes.SequenceEqual(_parameterBytes);
        bool updateLattice = texture is null || !ReferenceEquals(_lattice, lattice);
        try
        {
            if (pipeline is null || _pipelineColorFormat != target.Format)
            {
                SilkDisplayTransformPipelineDescriptor descriptor =
                    SilkDisplayTransformPipelineDescriptor.CreateChecked(_shaderFormat) with
                    {
                        ColorFormat = target.Format
                    };
                descriptor.Validate();
                pipeline = device.CreateDisplayTransformGraphicsPipeline(descriptor);
                _pipelineCreations++;
            }
            sampler ??= _device.CreateSampler(SilkSamplerDescriptor.LinearClamp);
            if (updateParameters)
            {
                parameters = _device.CreateBuffer(SilkDisplayTransformUniformWriter.ByteSize,
                    SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
                parameters.Write(bytes);
                _parameterUploads++;
            }
            if (updateLattice)
            {
                texture = _device.CreateTexture2D(SilkTextureDescriptor.SampledRgba8(
                    lattice.StripWidth, lattice.StripHeight));
                using ISilkGraphicsCommandList upload = _device.CreateCommandList();
                upload.UploadTexture(texture, lattice.Rgba8.Span);
                using ISilkGraphicsSubmission submission = _device.Submit(upload);
                submission.Wait();
                _latticeUploads++;
            }
            if (scene is null || scene.Width != target.Width || scene.Height != target.Height)
            {
                scene = _device.CreateTexture2D(SilkTextureDescriptor.HdrColorTarget(target.Width, target.Height));
                _intermediateCreations++;
            }
            if (binding is null || !ReferenceEquals(scene, _sceneTarget) ||
                !ReferenceEquals(texture, _latticeTexture) || !ReferenceEquals(parameters, _parameters))
            {
                var descriptor = new SilkDisplayTransformBindingDescriptor(scene, texture!, sampler, parameters!);
                descriptor.Validate();
                binding = device.CreateDisplayTransformBinding(descriptor);
                _bindingCreations++;
            }
        }
        catch (Exception preparationFailure)
        {
            List<Exception>? errors = null;
            DisposeDifferent(binding, _binding, ref errors);
            DisposeDifferent(scene, _sceneTarget, ref errors);
            DisposeDifferent(texture, _latticeTexture, ref errors);
            DisposeDifferent(parameters, _parameters, ref errors);
            DisposeDifferent(sampler, _sampler, ref errors);
            DisposeDifferent(pipeline, _pipeline, ref errors);
            if (errors is not null)
            {
                errors.Insert(0, preparationFailure);
                throw new AggregateException("Display preparation failed and candidate cleanup failed.", errors);
            }
            throw;
        }
        ISilkDisplayTransformBinding? previousBinding = _binding;
        ISilkGraphicsTexture? previousScene = _sceneTarget;
        ISilkGraphicsTexture? previousTexture = _latticeTexture;
        ISilkGraphicsBuffer? previousParameters = _parameters;
        ISilkGraphicsSampler? previousSampler = _sampler;
        ISilkDisplayTransformGraphicsPipeline? previousPipeline = _pipeline;
        _binding = binding;
        _sceneTarget = scene;
        _latticeTexture = texture;
        _parameters = parameters;
        _sampler = sampler;
        _pipeline = pipeline;
        _lattice = lattice;
        IDisposable? previousOwnership = _latticeOwnership;
        _latticeOwnership = ownership;
        ownership = null;
        _deviceGeneration = generation;
        _pipelineColorFormat = target.Format;
        _pipelineInitialized = true;
        _parametersInitialized = true;
        bytes.CopyTo(_parameterBytes);
        if (_pagePreparation is not null)
        {
            return;
        }
        previousOwnership?.Dispose();
        List<Exception>? retirementErrors = null;
        DisposeDifferent(previousBinding, binding, ref retirementErrors);
        DisposeDifferent(previousScene, scene, ref retirementErrors);
        DisposeDifferent(previousTexture, texture, ref retirementErrors);
        DisposeDifferent(previousParameters, parameters, ref retirementErrors);
        DisposeDifferent(previousSampler, sampler, ref retirementErrors);
        DisposeDifferent(previousPipeline, pipeline, ref retirementErrors);
        if (retirementErrors is not null)
        {
            throw new AggregateException("Previous display resources could not all be released.", retirementErrors);
        }
    }

    private void SynchronizeGeneration()
    {
        if (_pipelineInitialized && _displayDevice!.DisplayTransformDeviceGeneration != _deviceGeneration)
        {
            DisposeInfrastructure();
            _deviceInvalidations++;
        }
    }

    private static void DisposeDifferent(IDisposable? resource, IDisposable? retained, ref List<Exception>? errors)
    {
        if (ReferenceEquals(resource, retained))
        {
            return;
        }
        try
        {
            resource?.Dispose();
        }
        catch (Exception error)
        {
            (errors ??= []).Add(error);
        }
    }

    /// <summary>Records the fullscreen display-transform pass.</summary>
    internal void Record(
        ISilkGraphicsCommandList commands,
        ISilkDisplayTransformGraphicsCommandList displayCommands,
        ISilkGraphicsTexture displayTarget)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(displayCommands);
        ArgumentNullException.ThrowIfNull(displayTarget);

        ISilkGraphicsTexture sceneTarget = _sceneTarget ??
            throw new InvalidOperationException(
                "The display transform intermediate target is missing.");
        ISilkGraphicsTexture latticeTexture = _latticeTexture ??
            throw new InvalidOperationException(
                "The display transform lattice texture is missing.");
        ISilkDisplayTransformGraphicsPipeline pipeline = _pipeline ??
            throw new InvalidOperationException(
                "The display transform pipeline is missing.");
        ISilkDisplayTransformBinding binding = _binding ??
            throw new InvalidOperationException(
                "The display transform binding is missing.");

        var rendering = new SilkDisplayTransformRenderingDescriptor(
            displayTarget,
            sceneTarget,
            latticeTexture);
        rendering.Validate();
        displayCommands.BeginDisplayTransformRendering(rendering);
        displayCommands.SetDisplayTransformGraphicsPipeline(pipeline);
        displayCommands.SetDisplayTransformBinding(binding);
        commands.SetViewport(new SilkViewport(
            0,
            0,
            displayTarget.Width,
            displayTarget.Height));
        commands.SetScissor(new SilkScissor(
            0,
            0,
            displayTarget.Width,
            displayTarget.Height));
        displayCommands.DrawDisplayTransformFullscreenTriangle();
        commands.EndRendering();
        _passes++;
    }

    /// <summary>Records that no display transform was requested for this frame.</summary>
    internal void MarkInactive()
    {
        _status = SilkDisplayTransformStatus.Inactive;
        _requestKey = null;
        _diagnostic = null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        DisposeInfrastructure();
        _lattices.Clear();
    }

    private void Fail(SilkDisplayTransformStatus status, string code, string message)
    {
        _status = status;
        _diagnostic = new RenderDiagnostic(RenderDiagnosticSeverity.Warning, code, message);
        _failures++;
    }

    private void EnsureInfrastructure(
        ISilkDisplayTransformGraphicsDevice displayDevice,
        SilkTextureFormat colorFormat)
    {
        ulong generation = displayDevice.DisplayTransformDeviceGeneration;
        if (_pipelineInitialized &&
            generation == _deviceGeneration &&
            colorFormat == _pipelineColorFormat)
        {
            return;
        }

        if (_pipelineInitialized)
        {
            DisposeInfrastructure();
            _deviceInvalidations++;
        }

        SilkDisplayTransformPipelineDescriptor descriptor =
            SilkDisplayTransformPipelineDescriptor.CreateChecked(_shaderFormat) with
            {
                ColorFormat = colorFormat
            };
        descriptor.Validate();

        ISilkDisplayTransformGraphicsPipeline? pipeline = null;
        ISilkGraphicsSampler? sampler = null;
        ISilkGraphicsBuffer? parameters = null;
        try
        {
            pipeline = displayDevice.CreateDisplayTransformGraphicsPipeline(descriptor);
            sampler = _device.CreateSampler(SilkSamplerDescriptor.LinearClamp);
            parameters = _device.CreateBuffer(
                SilkDisplayTransformUniformWriter.ByteSize,
                SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
        }
        catch
        {
            parameters?.Dispose();
            sampler?.Dispose();
            pipeline?.Dispose();
            throw;
        }

        _pipeline = pipeline;
        _sampler = sampler;
        _parameters = parameters;
        _deviceGeneration = generation;
        _pipelineColorFormat = colorFormat;
        _pipelineInitialized = true;
        _parametersInitialized = false;
        _pipelineCreations++;
    }

    private void EnsureLatticeTexture(SilkDisplayTransformLattice lattice)
    {
        // The uploaded bytes are identified by the lattice instance the cache returned,
        // not by the transform's cache key. A key names *what was asked for*, and that is
        // unchanged when a config is edited underneath it; the cache answers *what was
        // baked*, and returns a new instance whenever the configuration identity moved.
        // Keying the GPU texture on the request meant an edited config rebaked on the CPU
        // and kept the stale lattice on the GPU, which is the one place the whole
        // invalidation chain could still show the wrong image.
        if (_latticeTexture is not null &&
            ReferenceEquals(_lattice, lattice) &&
            _latticeTexture.Width == lattice.StripWidth &&
            _latticeTexture.Height == lattice.StripHeight)
        {
            return;
        }

        ISilkGraphicsTexture? replacement = null;
        if (_latticeTexture is null ||
            _latticeTexture.Width != lattice.StripWidth ||
            _latticeTexture.Height != lattice.StripHeight)
        {
            replacement = _device.CreateTexture2D(
                SilkTextureDescriptor.SampledRgba8(
                    lattice.StripWidth,
                    lattice.StripHeight));
        }

        ISilkGraphicsTexture target = replacement ?? _latticeTexture!;
        try
        {
            // The lattice changes when the config, display, view, look, size, or shaper
            // interval changes -- not per frame -- so its upload is submitted on its own
            // command list rather than threaded through the frame's recording.
            using ISilkGraphicsCommandList upload = _device.CreateCommandList();
            upload.UploadTexture(target, lattice.Rgba8.Span);
            using ISilkGraphicsSubmission submission = _device.Submit(upload);
            submission.Wait();
        }
        catch
        {
            replacement?.Dispose();
            throw;
        }

        if (replacement is not null)
        {
            _binding?.Dispose();
            _binding = null;
            _latticeTexture?.Dispose();
            _latticeTexture = replacement;
        }
        _lattice = lattice;
        _latticeUploads++;
    }

    private void EnsureSceneTarget(SilkTextureDescriptor displayTarget)
    {
        if (_sceneTarget is not null &&
            _sceneTarget.Width == displayTarget.Width &&
            _sceneTarget.Height == displayTarget.Height)
        {
            return;
        }

        ISilkGraphicsTexture replacement = _device.CreateTexture2D(
            SilkTextureDescriptor.HdrColorTarget(
                displayTarget.Width,
                displayTarget.Height));
        _binding?.Dispose();
        _binding = null;
        _sceneTarget?.Dispose();
        _sceneTarget = replacement;
        _intermediateCreations++;
    }

    private void EnsureBinding(ISilkDisplayTransformGraphicsDevice displayDevice)
    {
        if (_binding is not null)
        {
            return;
        }

        var descriptor = new SilkDisplayTransformBindingDescriptor(
            _sceneTarget ??
                throw new InvalidOperationException(
                    "The display transform intermediate target is missing."),
            _latticeTexture ??
                throw new InvalidOperationException(
                    "The display transform lattice texture is missing."),
            _sampler ??
                throw new InvalidOperationException(
                    "The display transform sampler is missing."),
            _parameters ??
                throw new InvalidOperationException(
                    "The display transform parameter buffer is missing."));
        descriptor.Validate();
        _binding = displayDevice.CreateDisplayTransformBinding(descriptor);
        _bindingCreations++;
    }

    private void UpdateParameters(float exposure, SilkDisplayTransformLattice lattice)
    {
        ISilkGraphicsBuffer parameters = _parameters ??
            throw new InvalidOperationException(
                "The display transform parameter buffer is missing.");
        Span<byte> bytes = stackalloc byte[SilkDisplayTransformUniformWriter.ByteSize];
        SilkDisplayTransformUniformWriter.Write(
            exposure,
            lattice.ShaperMinimumLog2,
            lattice.ShaperRangeLog2,
            lattice.Size,
            _device.ClipSpaceYPointsDown,
            bytes);
        if (_parametersInitialized && bytes.SequenceEqual(_parameterBytes))
        {
            return;
        }

        parameters.Write(bytes);
        bytes.CopyTo(_parameterBytes);
        _parametersInitialized = true;
        _parameterUploads++;
    }

    private void DisposeInfrastructure()
    {
        _binding?.Dispose();
        _binding = null;
        _sceneTarget?.Dispose();
        _sceneTarget = null;
        _latticeTexture?.Dispose();
        _latticeTexture = null;
        _parameters?.Dispose();
        _parameters = null;
        _sampler?.Dispose();
        _sampler = null;
        _pipeline?.Dispose();
        _pipeline = null;
        _lattice = null;
        _latticeOwnership?.Dispose();
        _latticeOwnership = null;
        _pipelineInitialized = false;
        _parametersInitialized = false;
        _deviceGeneration = 0;
        _pipelineColorFormat = default;
    }
}
