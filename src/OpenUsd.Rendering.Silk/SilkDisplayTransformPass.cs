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
        _lattices = lattices ?? new SilkDisplayTransformLatticeCache();
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
        sceneTarget = displayTarget;
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

        SilkDisplayTransformLattice lattice;
        try
        {
            lattice = _lattices.Get(transform);
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

        EnsureInfrastructure(_displayDevice, displayTarget.Format);
        EnsureLatticeTexture(lattice);
        EnsureSceneTarget(displayTarget);
        EnsureBinding(_displayDevice);
        UpdateParameters(exposure, lattice);

        _status = SilkDisplayTransformStatus.Applied;
        _diagnostic = null;
        sceneTarget = _sceneTarget ??
            throw new InvalidOperationException(
                "The display transform intermediate target is missing.");
        return true;
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

    private void EnsureSceneTarget(ISilkGraphicsTexture displayTarget)
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
        _pipelineInitialized = false;
        _parametersInitialized = false;
        _deviceGeneration = 0;
        _pipelineColorFormat = default;
    }
}
