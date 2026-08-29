// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Common resource and submission contract implemented by every fallback backend.
/// </summary>
public interface ISilkGraphicsDevice : IDisposable
{
    /// <summary>Gets the backend.</summary>
    SilkGraphicsBackend Backend { get; }

    /// <summary>Gets immutable device capabilities.</summary>
    SilkGraphicsCapabilities Capabilities { get; }

    /// <summary>
    /// Gets whether the backend's clip space has +Y pointing down the render target.
    /// </summary>
    /// <remarks>
    /// Direct3D and Metal clip space has +Y up, Vulkan has +Y down. Geometry is projected
    /// once in renderer-neutral code, so the backend declares its convention and the scene
    /// constants are mirrored for it. Without this the same stage rendered vertically
    /// flipped on Vulkan relative to the other backends.
    /// </remarks>
    bool ClipSpaceYPointsDown => false;

    /// <summary>Creates a GPU buffer.</summary>
    ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage);

    /// <summary>Creates a two-dimensional color texture.</summary>
    ISilkGraphicsTexture CreateTexture2D(
        uint width,
        uint height,
        SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm);

    /// <summary>Creates a two-dimensional texture from an explicit descriptor.</summary>
    ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor);

    /// <summary>Creates an immutable texture sampler.</summary>
    ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor);

    /// <summary>Creates a backend shader module.</summary>
    ISilkGraphicsShaderModule CreateShaderModule(SilkShaderModuleDescriptor descriptor);

    /// <summary>Creates the reflected resource binding layout.</summary>
    ISilkGraphicsBindingLayout CreateBindingLayout(SilkBindingLayoutDescriptor descriptor);

    /// <summary>Links vertex and fragment modules with their binding layout.</summary>
    ISilkGraphicsShaderProgram CreateShaderProgram(SilkShaderProgramDescriptor descriptor);

    /// <summary>Creates an indexed graphics pipeline.</summary>
    ISilkGraphicsPipeline CreateGraphicsPipeline(SilkGraphicsPipelineDescriptor descriptor);

    /// <summary>Creates the checked compute resource binding layout.</summary>
    ISilkComputeBindingLayout CreateComputeBindingLayout(
        SilkComputeBindingLayoutDescriptor descriptor);

    /// <summary>Links one checked compute shader with its binding layout.</summary>
    ISilkComputeShaderProgram CreateComputeShaderProgram(
        SilkComputeShaderProgramDescriptor descriptor);

    /// <summary>Creates a checked compute pipeline.</summary>
    ISilkComputePipeline CreateComputePipeline(SilkComputePipelineDescriptor descriptor);

    /// <summary>Creates a command list for offscreen work.</summary>
    ISilkGraphicsCommandList CreateCommandList();

    /// <summary>Submits a recorded command list.</summary>
    ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList);

    /// <summary>Waits until previously submitted work is idle.</summary>
    void WaitIdle();
}

internal interface ISilkVolumeTextureGraphicsDevice
{
    ISilkGraphicsTexture CreateTexture3D(
        uint width,
        uint height,
        uint depth,
        SilkTextureFormat format);
}

internal interface ISilkVolumeTextureCommandList
{
    void UploadTexture3D(ISilkGraphicsTexture texture, ReadOnlySpan<byte> source);
}

/// <summary>
/// Common GPU buffer contract.
/// </summary>
public interface ISilkGraphicsBuffer : IDisposable
{
    /// <summary>Gets the allocation size.</summary>
    nuint Size { get; }

    /// <summary>Gets the intended usage.</summary>
    SilkBufferUsage Usage { get; }

    /// <summary>Writes bytes into a CPU-visible upload buffer.</summary>
    void Write(ReadOnlySpan<byte> data, nuint offset = 0);

    /// <summary>Copies the complete buffer to host memory for conformance testing.</summary>
    void ReadbackForTesting(Span<byte> destination);
}

/// <summary>
/// Shared GPU buffer state and upload validation.
/// </summary>
public abstract class SilkGraphicsBufferBase : SilkGraphicsResourceBase, ISilkGraphicsBuffer
{
    /// <summary>Initializes a buffer wrapper.</summary>
    protected SilkGraphicsBufferBase(nuint size, SilkBufferUsage usage)
    {
        Size = size;
        Usage = usage;
    }

    /// <inheritdoc/>
    public nuint Size { get; }

    /// <inheritdoc/>
    public SilkBufferUsage Usage { get; }

    /// <inheritdoc/>
    public abstract void Write(ReadOnlySpan<byte> data, nuint offset = 0);

    /// <inheritdoc/>
    public abstract void ReadbackForTesting(Span<byte> destination);

    /// <summary>Validates a host upload and returns its native-sized length.</summary>
    protected nuint ValidateWrite(int dataLength, nuint offset)
    {
        ThrowIfResourceDisposed();
        // Bitwise testing avoids the boxing Enum.HasFlag performs; this runs on the per-frame
        // uniform upload path where physics-driven meshes write every frame.
        if ((Usage & SilkBufferUsage.Upload) != SilkBufferUsage.Upload)
        {
            throw new InvalidOperationException(
                "The buffer was not created with SilkBufferUsage.Upload.");
        }

        nuint length = checked((nuint)dataLength);
        if (offset > Size || length > Size - offset)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                "The write exceeds the buffer allocation.");
        }
        return length;
    }

    /// <summary>Validates a complete storage-buffer test readback.</summary>
    protected int ValidateReadback(int destinationLength)
    {
        ThrowIfResourceDisposed();
        if (!Usage.HasFlag(SilkBufferUsage.Storage))
        {
            throw new InvalidOperationException(
                "Buffer readback is supported only for storage buffers.");
        }
        int requiredLength = checked((int)Size);
        if (destinationLength != requiredLength)
        {
            throw new ArgumentException(
                $"The destination must contain exactly {requiredLength} bytes.",
                nameof(destinationLength));
        }
        return requiredLength;
    }

    /// <summary>Acquires ownership that keeps the native buffer alive.</summary>
    protected IDisposable AcquireBufferLease() => AcquireResourceLease();

    /// <summary>Rejects use after buffer disposal.</summary>
    protected void ThrowIfBufferDisposed() => ThrowIfResourceDisposed();
}

/// <summary>
/// Common two-dimensional color texture contract.
/// </summary>
public interface ISilkGraphicsTexture : IDisposable
{
    /// <summary>Gets the texture width in pixels.</summary>
    uint Width { get; }

    /// <summary>Gets the texture height in pixels.</summary>
    uint Height { get; }

    /// <summary>Gets the texture format.</summary>
    SilkTextureFormat Format { get; }

    /// <summary>Gets the intended texture usage.</summary>
    SilkTextureUsage Usage { get; }

    /// <summary>Gets the number of allocated mip levels. Explicit chains are always the full valid chain.</summary>
    uint MipLevelCount { get; }

    /// <summary>Copies tightly packed raw texture bytes to host memory for conformance testing.</summary>
    void ReadbackForTesting(Span<byte> destination);

    /// <summary>Copies tightly packed 32-bit floating-point values to host memory for testing.</summary>
    void ReadbackForTesting(Span<float> destination);
}

/// <summary>
/// Immutable texture-sampling state.
/// </summary>
public interface ISilkGraphicsSampler : IDisposable
{
    /// <summary>Gets the backend-neutral sampler descriptor.</summary>
    SilkSamplerDescriptor Descriptor { get; }
}

/// <summary>
/// Shared texture state and readback validation.
/// </summary>
public abstract class SilkGraphicsTextureBase : ISilkGraphicsTexture
{
    private readonly object _lifetimeGate = new();
    private int _submissionLeaseCount;
    private bool _disposeRequested;
    private bool _nativeReleased;

    /// <summary>Initializes a texture wrapper.</summary>
    protected SilkGraphicsTextureBase(uint width, uint height, SilkTextureFormat format)
        : this(new SilkTextureDescriptor(
            width,
            height,
            format,
            SilkTextureDescriptor.GetDefaultUsage(format)))
    {
    }

    /// <summary>Initializes a texture wrapper from an explicit descriptor.</summary>
    protected SilkGraphicsTextureBase(SilkTextureDescriptor descriptor)
    {
        descriptor.Validate();
        Width = descriptor.Width;
        Height = descriptor.Height;
        Format = descriptor.Format;
        Usage = descriptor.Usage;
        MipLevelCount = descriptor.MipLevelCount;
    }

    /// <inheritdoc/>
    public uint Width { get; }

    /// <inheritdoc/>
    public uint Height { get; }

    /// <inheritdoc/>
    public SilkTextureFormat Format { get; }

    /// <inheritdoc/>
    public SilkTextureUsage Usage { get; }

    /// <inheritdoc/>
    public uint MipLevelCount { get; }

    /// <inheritdoc/>
    public abstract void ReadbackForTesting(Span<byte> destination);

    /// <inheritdoc/>
    public abstract void ReadbackForTesting(Span<float> destination);

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        bool releaseNative;
        lock (_lifetimeGate)
        {
            if (_disposeRequested)
            {
                return;
            }

            _disposeRequested = true;
            releaseNative = _submissionLeaseCount == 0;
            if (releaseNative)
            {
                _nativeReleased = true;
            }
        }

        if (releaseNative)
        {
            ReleaseNative();
        }
    }

    /// <summary>Acquires ownership that keeps the native texture alive for submitted work.</summary>
    protected IDisposable AcquireSubmissionLease()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposeRequested, this);
            _submissionLeaseCount++;
        }
        return new SubmissionLease(this);
    }

    /// <summary>Throws when disposal has been requested, even if a submission retains the native texture.</summary>
    protected void ThrowIfTextureDisposed()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposeRequested, this);
        }
    }

    /// <summary>Releases the backend texture after disposal and all submission leases complete.</summary>
    protected abstract void ReleaseNative();

    /// <summary>Validates a tightly packed raw readback destination.</summary>
    protected int ValidateReadback(int destinationLength)
    {
        if (Format == SilkTextureFormat.D32Float)
        {
            throw new InvalidOperationException(
                "Depth textures require floating-point readback.");
        }
        int requiredLength = checked(
            (int)(Width * Height * SilkTextureFormats.GetBytesPerPixel(Format)));
        if (destinationLength != requiredLength)
        {
            throw new ArgumentException(
                $"The destination must contain exactly {requiredLength} bytes.",
                nameof(destinationLength));
        }
        return requiredLength;
    }

    /// <summary>Validates a tightly packed 32-bit floating-point readback destination.</summary>
    protected int ValidateDepthReadback(int destinationLength)
    {
        int componentCount = Format switch
        {
            SilkTextureFormat.D32Float or SilkTextureFormat.R32Float => 1,
            SilkTextureFormat.Rgba32Float => 4,
            _ => throw new InvalidOperationException(
                "Float readback requires D32Float, R32Float, or Rgba32Float.")
        };
        int requiredLength = checked((int)(Width * Height * componentCount));
        if (destinationLength != requiredLength)
        {
            throw new ArgumentException(
                $"The destination must contain exactly {requiredLength} values.",
                nameof(destinationLength));
        }
        return requiredLength;
    }

    private void ReleaseSubmissionLease()
    {
        bool releaseNative;
        lock (_lifetimeGate)
        {
            _submissionLeaseCount--;
            releaseNative =
                _disposeRequested &&
                _submissionLeaseCount == 0 &&
                !_nativeReleased;
            if (releaseNative)
            {
                _nativeReleased = true;
            }
        }

        if (releaseNative)
        {
            ReleaseNative();
        }
    }

    private sealed class SubmissionLease(SilkGraphicsTextureBase texture) : IDisposable
    {
        private SilkGraphicsTextureBase? _texture = texture;

        public void Dispose()
        {
            SilkGraphicsTextureBase? texture =
                Interlocked.Exchange(ref _texture, null);
            texture?.ReleaseSubmissionLease();
        }
    }
}

/// <summary>
/// Coordinates deterministic disposal between a graphics device and its native dependents.
/// </summary>
public abstract class SilkGraphicsDeviceLifetimeBase
{
    private readonly object _lifetimeGate = new();
    private int _dependentObjectCount;
    private bool _disposeStarted;
    private bool _disposeCompleted;

    /// <summary>Registers a native object that must be released before device teardown.</summary>
    protected void RegisterDependentLifetime()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposeStarted || _disposeCompleted, this);
            _dependentObjectCount++;
        }
    }

    /// <summary>Releases a previously registered native dependent.</summary>
    protected void ReleaseDependentLifetime()
    {
        lock (_lifetimeGate)
        {
            _dependentObjectCount--;
        }
    }

    /// <summary>Begins device disposal after verifying that no native dependents remain.</summary>
    protected bool TryBeginLifetimeDispose(string liveDependentsMessage)
    {
        lock (_lifetimeGate)
        {
            if (_disposeStarted || _disposeCompleted)
            {
                return false;
            }
            if (_dependentObjectCount != 0)
            {
                throw new InvalidOperationException(liveDependentsMessage);
            }
            _disposeStarted = true;
            return true;
        }
    }

    /// <summary>Allows a failed pre-teardown disposal attempt to be retried.</summary>
    protected void CancelLifetimeDispose()
    {
        lock (_lifetimeGate)
        {
            if (!_disposeCompleted)
            {
                _disposeStarted = false;
            }
        }
    }

    /// <summary>Marks native device teardown as complete.</summary>
    protected void CompleteLifetimeDispose()
    {
        lock (_lifetimeGate)
        {
            _disposeCompleted = true;
        }
    }
}

/// <summary>Ordered command kinds shared by every backend command stream.</summary>
public enum SilkGraphicsCommandKind
{
    /// <summary>Uploads one texture image.</summary>
    UploadTexture,

    /// <summary>Clears a color attachment.</summary>
    ClearColor,

    /// <summary>Clears a depth attachment.</summary>
    ClearDepth,

    /// <summary>Begins a rendering scope.</summary>
    BeginRendering,

    /// <summary>Binds a graphics pipeline.</summary>
    SetGraphicsPipeline,

    /// <summary>Sets a viewport.</summary>
    SetViewport,

    /// <summary>Sets a scissor rectangle.</summary>
    SetScissor,

    /// <summary>Binds a vertex buffer.</summary>
    SetVertexBuffer,

    /// <summary>Binds an index buffer.</summary>
    SetIndexBuffer,

    /// <summary>Binds a uniform buffer.</summary>
    SetUniformBuffer,

    /// <summary>Issues an indexed draw.</summary>
    DrawIndexed,

    /// <summary>Ends a rendering scope.</summary>
    EndRendering,

    /// <summary>Binds a compute pipeline.</summary>
    SetComputePipeline,

    /// <summary>Binds a read-write storage buffer.</summary>
    SetStorageBuffer,

    /// <summary>Binds the checked compute constant buffer.</summary>
    SetComputeUniformBuffer,

    /// <summary>Dispatches the checked one-dimensional compute kernel.</summary>
    Dispatch,

    /// <summary>Makes prior storage writes visible to later commands.</summary>
    BufferBarrier,

    /// <summary>Begins a selected-mesh mask scope with read-only visible depth.</summary>
    BeginSelectionMaskRendering,

    /// <summary>Binds the checked selected-mesh mask pipeline.</summary>
    SetSelectionMaskPipeline,

    /// <summary>Begins a loaded visible-color fullscreen composite scope.</summary>
    BeginSelectionOutlineRendering,

    /// <summary>Binds the checked fullscreen selection-outline pipeline.</summary>
    SetSelectionOutlinePipeline,

    /// <summary>Binds cached mask, depth, sampler, and outline parameters.</summary>
    SetSelectionOutlineBinding,

    /// <summary>Draws the generated three-vertex fullscreen triangle.</summary>
    DrawSelectionOutlineFullscreenTriangle,

    // Appended so no existing member's value shifts, which the public API baseline
    // records explicitly.

    /// <summary>Binds a sampled texture to a material slot.</summary>
    SetTexture,

    /// <summary>Binds a sampler to a material slot.</summary>
    SetSampler,

    /// <summary>Issues an indexed instanced draw.</summary>
    DrawIndexedInstanced,

    /// <summary>Uploads one tightly packed 3D texture image.</summary>
    UploadTexture3D
}

/// <summary>
/// Records shader-independent commands in submission order.
/// </summary>
public interface ISilkGraphicsCommandList : IDisposable
{
    /// <summary>
    /// Uploads one texture's full packed chain: mip 0 first, then ascending levels, each
    /// tightly packed with dimensions <c>max(1, base &gt;&gt; level)</c>. A single-level texture
    /// still uploads one tightly packed image. The source length must equal the exact packed
    /// chain size for the destination's <see cref="ISilkGraphicsTexture.MipLevelCount"/>; see
    /// <see cref="SilkMipChainLayout"/>.
    /// </summary>
    void UploadTexture(ISilkGraphicsTexture texture, ReadOnlySpan<byte> source);

    /// <summary>Clears an RGBA8 color texture.</summary>
    void ClearColor(ISilkGraphicsTexture texture, SilkColor color);

    /// <summary>Clears a D32Float depth texture.</summary>
    void ClearDepth(ISilkGraphicsTexture texture, float depth);

    /// <summary>Begins an offscreen color/depth rendering scope.</summary>
    void BeginRendering(SilkRenderingDescriptor descriptor);

    /// <summary>Sets the graphics pipeline.</summary>
    void SetGraphicsPipeline(ISilkGraphicsPipeline pipeline);

    /// <summary>Sets the viewport.</summary>
    void SetViewport(SilkViewport viewport);

    /// <summary>Sets the pixel scissor rectangle.</summary>
    void SetScissor(SilkScissor scissor);

    /// <summary>Binds a vertex buffer.</summary>
    void SetVertexBuffer(ISilkGraphicsBuffer buffer);

    /// <summary>Binds a 32-bit index buffer.</summary>
    void SetIndexBuffer(ISilkGraphicsBuffer buffer);

    /// <summary>Binds SceneParameters at set zero, binding zero.</summary>
    void SetUniformBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer);

    /// <summary>
    /// Binds a sampled texture to the material slot at the given set and binding.
    /// </summary>
    /// <remarks>
    /// The slot must be declared by the bound pipeline's
    /// <see cref="SilkBindingLayoutDescriptor.MaterialSlots"/> as a
    /// <see cref="SilkBindingKind.SampledTexture"/>, and the texture must carry
    /// <see cref="SilkTextureUsage.Sampled"/>.
    /// </remarks>
    void SetTexture(uint setIndex, uint binding, ISilkGraphicsTexture texture);

    /// <summary>
    /// Binds a sampler to the material slot at the given set and binding.
    /// </summary>
    /// <remarks>
    /// The slot must be declared by the bound pipeline's
    /// <see cref="SilkBindingLayoutDescriptor.MaterialSlots"/> as a
    /// <see cref="SilkBindingKind.Sampler"/>.
    /// </remarks>
    void SetSampler(uint setIndex, uint binding, ISilkGraphicsSampler sampler);

    /// <summary>Draws indexed triangle-list geometry.</summary>
    void DrawIndexed(uint indexCount);

    /// <summary>Draws indexed triangle-list geometry once per bound instance.</summary>
    void DrawIndexedInstanced(uint indexCount, uint instanceCount);

    /// <summary>Ends the current rendering scope.</summary>
    void EndRendering();

    /// <summary>Sets the compute pipeline.</summary>
    void SetComputePipeline(ISilkComputePipeline pipeline);

    /// <summary>Binds outputValues at set zero, binding zero.</summary>
    void SetStorageBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer);

    /// <summary>Binds ComputeParameters at set zero, binding one.</summary>
    void SetComputeUniformBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer);

    /// <summary>Dispatches enough 64-wide groups for the requested element count.</summary>
    void Dispatch(uint elementCount);

    /// <summary>Makes storage writes visible to later graphics, compute, or copy work.</summary>
    void BufferBarrier(ISilkGraphicsBuffer buffer);
}

/// <summary>
/// Represents submitted GPU work.
/// </summary>
public interface ISilkGraphicsSubmission : IDisposable
{
    /// <summary>Gets whether the submitted work has completed.</summary>
    bool IsCompleted { get; }

    /// <summary>Waits for the submitted work to complete.</summary>
    void Wait();
}

/// <summary>
/// Describes a backend device.
/// </summary>
public readonly record struct SilkGraphicsCapabilities(
    string DeviceName,
    string ApiVersion,
    bool SupportsCompute,
    bool IsSoftware)
{
    /// <summary>
    /// Gets whether material textures can be bound through a descriptor-indexed table.
    /// </summary>
    public bool SupportsDescriptorIndexedTextureTables { get; init; }

    /// <summary>
    /// Gets why descriptor-indexed material texture tables were unavailable, when they declined.
    /// </summary>
    public string? DescriptorIndexedTextureTablesDiagnostic { get; init; }
}

/// <summary>
/// Describes intended GPU buffer use.
/// </summary>
[Flags]
public enum SilkBufferUsage
{
    /// <summary>Vertex data.</summary>
    Vertex = 1,

    /// <summary>Index data.</summary>
    Index = 2,

    /// <summary>Uniform or constant data.</summary>
    Uniform = 4,

    /// <summary>Storage/structured data.</summary>
    Storage = 8,

    /// <summary>CPU-visible upload data.</summary>
    Upload = 16
}

/// <summary>
/// Texture formats supported by the backend-neutral RHI.
/// </summary>
public enum SilkTextureFormat
{
    /// <summary>Four normalized eight-bit red, green, blue, and alpha channels.</summary>
    Rgba8Unorm,

    /// <summary>One 32-bit floating-point depth channel.</summary>
    D32Float,

    /// <summary>One 32-bit floating-point sampled channel.</summary>
    R32Float,

    /// <summary>Four 16-bit floating-point color channels.</summary>
    Rgba16Float,

    /// <summary>Four 32-bit floating-point color channels.</summary>
    Rgba32Float
}

/// <summary>
/// Describes intended texture attachment use.
/// </summary>
[Flags]
public enum SilkTextureUsage
{
    /// <summary>Texture can be read by shaders.</summary>
    Sampled = 1,

    /// <summary>Color render-target attachment.</summary>
    ColorRenderTarget = 2,

    /// <summary>Depth render-target attachment.</summary>
    DepthRenderTarget = 4,

    /// <summary>Texture can be copied from.</summary>
    CopySource = 8,

    /// <summary>Texture can be copied to.</summary>
    CopyDestination = 16
}

/// <summary>
/// Describes a two-dimensional texture allocation.
/// </summary>
/// <param name="Width">The texture width in texels.</param>
/// <param name="Height">The texture height in texels.</param>
/// <param name="Format">The texture format.</param>
/// <param name="Usage">The intended texture usage.</param>
/// <param name="MipLevelCount">
/// The number of allocated mip levels. Must be <c>1</c> (default) or the full mathematically
/// valid chain length for <paramref name="Width"/> and <paramref name="Height"/>; partial chains
/// are rejected so every backend can allocate, view, and transition the same well-defined level set.
/// </param>
public readonly record struct SilkTextureDescriptor(
    uint Width,
    uint Height,
    SilkTextureFormat Format,
    SilkTextureUsage Usage,
    uint MipLevelCount = 1)
{
    /// <summary>Creates an RGBA8 color render-target descriptor.</summary>
    public static SilkTextureDescriptor ColorTarget(uint width, uint height) =>
        new(width, height, SilkTextureFormat.Rgba8Unorm, SilkTextureUsage.ColorRenderTarget);

    /// <summary>Creates a D32Float depth render-target descriptor.</summary>
    public static SilkTextureDescriptor DepthTarget(uint width, uint height) =>
        new(width, height, SilkTextureFormat.D32Float, SilkTextureUsage.DepthRenderTarget);

    /// <summary>Creates a shader-readable D32Float visible-depth descriptor.</summary>
    public static SilkTextureDescriptor SampledDepthTarget(uint width, uint height) =>
        new(
            width,
            height,
            SilkTextureFormat.D32Float,
            SilkTextureUsage.DepthRenderTarget | SilkTextureUsage.Sampled);

    /// <summary>Creates an RGBA16Float linear HDR color render-target descriptor.</summary>
    public static SilkTextureDescriptor HdrColorTarget(uint width, uint height) =>
        new(
            width,
            height,
            SilkTextureFormat.Rgba16Float,
            SilkTextureUsage.ColorRenderTarget |
                SilkTextureUsage.Sampled |
                SilkTextureUsage.CopySource);

    /// <summary>Creates a reusable sampled RGBA8 selection-mask descriptor.</summary>
    public static SilkTextureDescriptor SelectionMask(uint width, uint height) =>
        new(
            width,
            height,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.Sampled);

    /// <summary>Creates an uploadable and shader-readable RGBA8 descriptor.</summary>
    public static SilkTextureDescriptor SampledRgba8(uint width, uint height) =>
        new(
            width,
            height,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.Sampled |
                SilkTextureUsage.CopySource |
                SilkTextureUsage.CopyDestination);

    /// <summary>Gets the default attachment usage for a format.</summary>
    public static SilkTextureUsage GetDefaultUsage(SilkTextureFormat format) =>
        format switch
        {
            SilkTextureFormat.Rgba8Unorm or
            SilkTextureFormat.Rgba16Float or
            SilkTextureFormat.Rgba32Float => SilkTextureUsage.ColorRenderTarget,
            SilkTextureFormat.D32Float => SilkTextureUsage.DepthRenderTarget,
            SilkTextureFormat.R32Float => SilkTextureUsage.Sampled,
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

    /// <summary>Validates the descriptor's dimensions and format/usage pairing.</summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfZero(Width);
        ArgumentOutOfRangeException.ThrowIfZero(Height);
        if (!Enum.IsDefined(Format))
        {
            throw new ArgumentOutOfRangeException(nameof(Format));
        }
        const SilkTextureUsage knownUsage =
            SilkTextureUsage.Sampled |
            SilkTextureUsage.ColorRenderTarget |
            SilkTextureUsage.DepthRenderTarget |
            SilkTextureUsage.CopySource |
            SilkTextureUsage.CopyDestination;
        if (Usage == 0 || (Usage & ~knownUsage) != 0)
        {
            throw new ArgumentException("Texture usage must contain only defined values.", nameof(Usage));
        }
        if (Format != SilkTextureFormat.D32Float &&
            Usage.HasFlag(SilkTextureUsage.DepthRenderTarget))
        {
            throw new ArgumentException(
                "Color and sampled textures cannot use DepthRenderTarget.",
                nameof(Usage));
        }
        if (Format == SilkTextureFormat.D32Float &&
            !Usage.HasFlag(SilkTextureUsage.DepthRenderTarget))
        {
            throw new ArgumentException(
                "D32Float textures require DepthRenderTarget usage.",
                nameof(Usage));
        }
        if (Format == SilkTextureFormat.D32Float &&
            (Usage.HasFlag(SilkTextureUsage.ColorRenderTarget) ||
             Usage.HasFlag(SilkTextureUsage.CopyDestination)))
        {
            throw new ArgumentException(
                "D32Float textures cannot use ColorRenderTarget or CopyDestination.",
                nameof(Usage));
        }
        if (!SilkTextureFormats.IsColorRenderTarget(Format) &&
            Usage.HasFlag(SilkTextureUsage.ColorRenderTarget))
        {
            throw new ArgumentException(
                "Only RGBA color formats can use ColorRenderTarget.",
                nameof(Usage));
        }
        ArgumentOutOfRangeException.ThrowIfZero(MipLevelCount);
        uint maxMipLevelCount = SilkMipChainLayout.GetMaxMipLevelCount(Width, Height);
        if (MipLevelCount != 1 && MipLevelCount != maxMipLevelCount)
        {
            throw new ArgumentException(
                $"MipLevelCount must be 1 or the full {maxMipLevelCount}-level chain for a " +
                $"{Width}x{Height} texture; partial chains are not supported.",
                nameof(MipLevelCount));
        }
        if (MipLevelCount > 1 &&
            (Usage.HasFlag(SilkTextureUsage.ColorRenderTarget) ||
             Usage.HasFlag(SilkTextureUsage.DepthRenderTarget)))
        {
            throw new ArgumentException(
                "Render-target textures cannot allocate more than one mip level.",
                nameof(MipLevelCount));
        }
    }
}

internal static class SilkTextureFormats
{
    internal static bool IsColorRenderTarget(SilkTextureFormat format) =>
        format is SilkTextureFormat.Rgba8Unorm or
            SilkTextureFormat.Rgba16Float or
            SilkTextureFormat.Rgba32Float;

    internal static bool IsFloatingPointColor(SilkTextureFormat format) =>
        format is SilkTextureFormat.Rgba16Float or SilkTextureFormat.Rgba32Float;

    internal static uint GetBytesPerPixel(SilkTextureFormat format) =>
        format switch
        {
            SilkTextureFormat.Rgba8Unorm or
            SilkTextureFormat.D32Float or
            SilkTextureFormat.R32Float => 4,
            SilkTextureFormat.Rgba16Float => 8,
            SilkTextureFormat.Rgba32Float => 16,
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
}

/// <summary>
/// Texture minification and magnification filtering.
/// </summary>
public enum SilkSamplerFilter
{
    /// <summary>Selects the nearest texel.</summary>
    Nearest,

    /// <summary>Linearly blends adjacent texels.</summary>
    Linear
}

/// <summary>
/// Texture coordinate addressing outside the normalized range.
/// </summary>
public enum SilkSamplerAddressMode
{
    /// <summary>Clamps coordinates to the edge texel.</summary>
    ClampToEdge,

    /// <summary>Repeats texture coordinates.</summary>
    Repeat,

    /// <summary>Mirrors texture coordinates across integer boundaries.</summary>
    MirrorRepeat
}

/// <summary>
/// Describes immutable texture sampling state.
/// </summary>
public readonly record struct SilkSamplerDescriptor(
    SilkSamplerFilter MinFilter,
    SilkSamplerFilter MagFilter,
    SilkSamplerAddressMode AddressU,
    SilkSamplerAddressMode AddressV,
    SilkSamplerAddressMode AddressW)
{
    /// <summary>Gets a linear-filtered clamp-to-edge sampler.</summary>
    public static SilkSamplerDescriptor LinearClamp => new(
        SilkSamplerFilter.Linear,
        SilkSamplerFilter.Linear,
        SilkSamplerAddressMode.ClampToEdge,
        SilkSamplerAddressMode.ClampToEdge,
        SilkSamplerAddressMode.ClampToEdge);

    /// <summary>Gets a nearest-filtered clamp-to-edge sampler.</summary>
    public static SilkSamplerDescriptor NearestClamp => new(
        SilkSamplerFilter.Nearest,
        SilkSamplerFilter.Nearest,
        SilkSamplerAddressMode.ClampToEdge,
        SilkSamplerAddressMode.ClampToEdge,
        SilkSamplerAddressMode.ClampToEdge);

    /// <summary>Gets a nearest-filtered repeating sampler.</summary>
    public static SilkSamplerDescriptor NearestRepeat => new(
        SilkSamplerFilter.Nearest,
        SilkSamplerFilter.Nearest,
        SilkSamplerAddressMode.Repeat,
        SilkSamplerAddressMode.Repeat,
        SilkSamplerAddressMode.Repeat);

    /// <summary>Validates all descriptor enum values.</summary>
    public void Validate()
    {
        if (!Enum.IsDefined(MinFilter))
        {
            throw new ArgumentOutOfRangeException(nameof(MinFilter));
        }
        if (!Enum.IsDefined(MagFilter))
        {
            throw new ArgumentOutOfRangeException(nameof(MagFilter));
        }
        if (!Enum.IsDefined(AddressU))
        {
            throw new ArgumentOutOfRangeException(nameof(AddressU));
        }
        if (!Enum.IsDefined(AddressV))
        {
            throw new ArgumentOutOfRangeException(nameof(AddressV));
        }
        if (!Enum.IsDefined(AddressW))
        {
            throw new ArgumentOutOfRangeException(nameof(AddressW));
        }
    }
}

/// <summary>
/// Linear floating-point clear color.
/// </summary>
public readonly record struct SilkColor(float Red, float Green, float Blue, float Alpha)
{
    /// <summary>Validates that every channel is finite and normalized.</summary>
    public void Validate()
    {
        ValidateChannel(Red, nameof(Red));
        ValidateChannel(Green, nameof(Green));
        ValidateChannel(Blue, nameof(Blue));
        ValidateChannel(Alpha, nameof(Alpha));
    }

    internal void ValidateFinite()
    {
        ValidateFiniteChannel(Red, nameof(Red));
        ValidateFiniteChannel(Green, nameof(Green));
        ValidateFiniteChannel(Blue, nameof(Blue));
        ValidateFiniteChannel(Alpha, nameof(Alpha));
    }

    private static void ValidateChannel(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0 || value > 1)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private static void ValidateFiniteChannel(float value, string name)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }
}
