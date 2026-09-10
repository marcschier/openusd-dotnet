// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering;

/// <summary>Bounds one disk-backed RGBA frame job independently of in-memory preview limits.</summary>
public sealed class RenderDiskJobLimits
{
    /// <summary>Creates limits up to 4096 frames, 64 MiB per frame and 4 GiB of total output.</summary>
    public RenderDiskJobLimits(
        int maximumFrames = 4096,
        long maximumFrameBytes = 64 * 1024 * 1024,
        long maximumTotalBytes = 4L * 1024 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFrames);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumFrames, 4096);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFrameBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumFrameBytes, 64L * 1024 * 1024);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTotalBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumTotalBytes, 4L * 1024 * 1024 * 1024);
        MaximumFrames = maximumFrames;
        MaximumFrameBytes = maximumFrameBytes;
        MaximumTotalBytes = maximumTotalBytes;
    }

    /// <summary>Gets the default bounded disk-job policy.</summary>
    public static RenderDiskJobLimits Default { get; } = new();
    /// <summary>Gets the maximum number of requested frames.</summary>
    public int MaximumFrames { get; }
    /// <summary>Gets the per-frame raster admission and combined encoded output byte limit.</summary>
    /// <remarks>
    /// Display-only admission charges four bytes per pixel. HDR or device-depth admission conservatively
    /// charges twenty bytes per pixel, including capture staging and selection upload storage.
    /// All encoded planes share this limit separately from raster admission.
    /// Native EXR codec working storage is not covered by either limit.
    /// </remarks>
    public long MaximumFrameBytes { get; }
    /// <summary>Gets the total encoded output limit for all planes, including the manifest.</summary>
    public long MaximumTotalBytes { get; }
}

/// <summary>An immutable, bounded sequence of explicit renderer states and a new output directory.</summary>
/// <remarks>
/// This requests RGBA frames, not arbitrary UsdRender variables or scene-authored commands.
/// A product adapter must honor or refuse its additional semantics before submitting these states.
/// Caller adapters retain stage/revision and renderer-thread ownership.
/// </remarks>
public sealed class RenderDiskJobRequest
{
    internal const int ExpandedCaptureManagedBytesPerPixel = 20;

    internal RenderDiskJobRequest(
        string outputDirectory, IReadOnlyList<StageRenderState> frames,
        bool includeDeviceDepth, bool includeHdrColor, RenderHdrColorFormat hdrColorFormat,
        RenderDiskJobLimits? limits, RenderProductJobPlan productPlan)
        : this(outputDirectory, frames, includeDeviceDepth, includeHdrColor, hdrColorFormat, limits)
    {
        ProductPlan = productPlan;
    }

    /// <summary>Copies a bounded frame list and validates output dimensions before any rendering.</summary>
    public RenderDiskJobRequest(
        string outputDirectory, IReadOnlyList<StageRenderState> frames, RenderDiskJobLimits? limits = null)
        : this(outputDirectory, frames, includeDeviceDepth: false, limits)
    {
    }

    /// <summary>Requests RGBA frames and optionally exact normalized device-depth planes.</summary>
    /// <remarks>Depth frames charge 20 managed bytes per pixel, including HDR staging and selection upload.</remarks>
    public RenderDiskJobRequest(
        string outputDirectory, IReadOnlyList<StageRenderState> frames,
        bool includeDeviceDepth, RenderDiskJobLimits? limits = null)
        : this(outputDirectory, frames, includeDeviceDepth, includeHdrColor: false, limits)
    {
    }

    /// <summary>Requests RGBA8 display frames with optional device-depth and pre-display HDR planes.</summary>
    /// <remarks>Either additional plane uses the conservative 20-byte-per-pixel capture admission policy.</remarks>
    public RenderDiskJobRequest(
        string outputDirectory, IReadOnlyList<StageRenderState> frames,
        bool includeDeviceDepth, bool includeHdrColor, RenderDiskJobLimits? limits = null)
        : this(outputDirectory, frames, includeDeviceDepth, includeHdrColor, RenderHdrColorFormat.RawRgba16Float, limits)
    {
    }

    /// <summary>Requests display/depth planes and chooses raw binary16 or lossless EXR for optional HDR.</summary>
    /// <remarks>
    /// EXR requires HDR capture and the supported Windows x64 encoder profile. It does not change
    /// capture admission or perform a color/alpha conversion. Native codec heap is not quota-certified.
    /// </remarks>
    public RenderDiskJobRequest(
        string outputDirectory, IReadOnlyList<StageRenderState> frames,
        bool includeDeviceDepth, bool includeHdrColor, RenderHdrColorFormat hdrColorFormat,
        RenderDiskJobLimits? limits = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(frames);
        if (hdrColorFormat is not (RenderHdrColorFormat.RawRgba16Float or RenderHdrColorFormat.Exr))
        {
            throw new ArgumentOutOfRangeException(nameof(hdrColorFormat));
        }
        if (hdrColorFormat == RenderHdrColorFormat.Exr)
        {
            if (!includeHdrColor)
            {
                throw new ArgumentException("EXR output requires an HDR color plane.", nameof(includeHdrColor));
            }
            if (!ExrRgba16FloatWriter.IsSupported)
            {
                throw new PlatformNotSupportedException("EXR job output currently requires Windows x64.");
            }
        }
        if (!Path.IsPathFullyQualified(outputDirectory))
        {
            throw new ArgumentException("A render-job directory must be an absolute local path.",
                nameof(outputDirectory));
        }
        OutputDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));
        if (string.IsNullOrEmpty(Path.GetFileName(OutputDirectory)))
        {
            throw new ArgumentException("A render job requires a named child directory.", nameof(outputDirectory));
        }
        Limits = limits ?? RenderDiskJobLimits.Default;
        IncludeDeviceDepth = includeDeviceDepth;
        IncludeHdrColor = includeHdrColor;
        HdrColorFormat = hdrColorFormat;
        int count = frames.Count;
        if (count < 1 || count > Limits.MaximumFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(frames), "The frame list exceeds the render-job limit.");
        }
        var owned = new StageRenderState[count];
        for (int index = 0; index < count; index++)
        {
            StageRenderState state = frames[index] ??
                throw new ArgumentException("A render-job frame cannot be null.", nameof(frames));
            if (string.IsNullOrWhiteSpace(state.Stage.Identifier) || state.Stage.Identifier.Length > 1024 ||
                state.Stage.Identifier.Any(char.IsControl) || !double.IsFinite(state.Time.TimeCode))
            {
                throw new ArgumentException("A frame requires a bounded stage identifier and finite time.",
                    nameof(frames));
            }
            int bytes = PngRgba8Writer.GetPixelByteCount(state.Viewport.Width, state.Viewport.Height);
            long charge = includeDeviceDepth || includeHdrColor
                ? ((long)bytes / 4) * ExpandedCaptureManagedBytesPerPixel : bytes;
            if (charge > Limits.MaximumFrameBytes || state.Viewport.Width > 8192 || state.Viewport.Height > 8192)
            {
                throw new ArgumentOutOfRangeException(nameof(frames), "Frame dimensions exceed the render-job limit.");
            }
            state.RenderSettings.ValidateDisplayTransform();
            if (state.RenderSettings.SamplesPerPixel < 1)
            {
                throw new ArgumentException("Frame render settings are uninitialized.", nameof(frames));
            }
            var clear = state.RenderSettings.ClearColor;
            if (!float.IsFinite(clear.X) || !float.IsFinite(clear.Y) ||
                !float.IsFinite(clear.Z) || !float.IsFinite(clear.W))
            {
                throw new ArgumentException("Frame clear colors must be finite.", nameof(frames));
            }
            if (state.Selection.Items.Count > 256)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(frames), "A job frame admits at most 256 selected items.");
            }
            foreach (SelectionItem item in state.Selection.Items)
            {
                if (item.PrimPath.Length > 1024 || item.InstancerContext.Count > 16 ||
                    item.InstancerContext.Any(static entry => entry.InstancerPath.Length > 1024))
                {
                    throw new ArgumentException("Selection identity exceeds the render-job metadata bounds.",
                        nameof(frames));
                }
            }
            owned[index] = state;
        }
        Frames = new OwnedReadOnlyList<StageRenderState>(owned);
    }

    /// <summary>Gets the absolute, create-only publication directory.</summary>
    public string OutputDirectory { get; }
    /// <summary>Gets the exact requested states in execution order.</summary>
    public IReadOnlyList<StageRenderState> Frames { get; }
    /// <summary>Gets the validated job limits.</summary>
    public RenderDiskJobLimits Limits { get; }
    /// <summary>Gets whether each frame must include normalized device depth; missing depth is an error.</summary>
    public bool IncludeDeviceDepth { get; }
    /// <summary>Gets whether each frame must include actual pre-display HDR color; missing HDR is an error.</summary>
    public bool IncludeHdrColor { get; }
    /// <summary>Gets the HDR container choice; existing overloads retain raw binary16 output.</summary>
    public RenderHdrColorFormat HdrColorFormat { get; }
    /// <summary>Gets admitted authored-product semantics the frame adapter must honor, or null for a viewport job.</summary>
    public RenderProductJobPlan? ProductPlan { get; }
}

/// <summary>Supplies frames on the job's executing thread while retaining renderer and stage ownership.</summary>
public interface IRenderJobFrameSource
{
    /// <summary>Renders the exact requested state, or throws when its semantics cannot be honored.</summary>
    /// <remarks>The image is consumed before the next call. It need not remain valid beyond that call.</remarks>
    RenderJobImage Render(StageRenderState state, CancellationToken cancellationToken);
}

/// <summary>Explicitly admits product semantics before the shared engine creates staging output.</summary>
/// <remarks>Viewport-only sources cannot accidentally execute authored products with their legacy defaults.</remarks>
public interface IRenderProductFrameSource : IRenderJobFrameSource
{
    /// <summary>Requires the adapter to honor every admitted output, scene filter and renderer setting, or throw.</summary>
    void ValidateProduct(RenderProductJobPlan plan, CancellationToken cancellationToken);
}

/// <summary>A renderer-owned, tightly packed RGBA8 image consumed before the next job frame.</summary>
public sealed class RenderJobImage
{
    /// <summary>Validates dimensions, storage extent and row order without copying image data.</summary>
    public RenderJobImage(int width, int height, ReadOnlyMemory<byte> rgba, Rgba8RowOrder rowOrder)
    {
        if (rgba.Length != PngRgba8Writer.GetPixelByteCount(width, height))
        {
            throw new ArgumentException("Render-job RGBA storage does not match its dimensions.", nameof(rgba));
        }
        if (rowOrder is not (Rgba8RowOrder.TopDown or Rgba8RowOrder.BottomUp))
        {
            throw new ArgumentOutOfRangeException(nameof(rowOrder));
        }
        Width = width;
        Height = height;
        Rgba = rgba;
        RowOrder = rowOrder;
    }

    /// <summary>Gets the actual width.</summary>
    public int Width { get; }
    /// <summary>Gets the actual height.</summary>
    public int Height { get; }
    /// <summary>Gets the renderer-owned RGBA bytes.</summary>
    public ReadOnlyMemory<byte> Rgba { get; }
    /// <summary>Gets the input row order.</summary>
    public Rgba8RowOrder RowOrder { get; }
    /// <summary>Gets or initializes degradation diagnostics reported for this frame.</summary>
    public RenderDiagnosticsState Diagnostics { get; init; } = RenderDiagnosticsState.Empty;
    /// <summary>Gets or initializes an actual normalized device-depth plane from the same rendered frame.</summary>
    public RenderJobDeviceDepth? DeviceDepth { get; init; }
    /// <summary>Gets or initializes actual pre-display HDR color from the same rendered frame.</summary>
    public RenderJobHdrColor? HdrColor { get; init; }
}

/// <summary>Top-down little-endian RGBA binary16 framebuffer color before exposure, display and selection.</summary>
/// <remarks>
/// The renderer owns these bytes until the next source render. Alpha is stored framebuffer alpha,
/// not necessarily straight/unassociated. No named primaries, albedo or physical radiance is implied.
/// </remarks>
public sealed class RenderJobHdrColor
{
    /// <summary>Validates dimensions and the exact byte extent without copying or widening pixels.</summary>
    public RenderJobHdrColor(int width, int height, ReadOnlyMemory<byte> rgba16Float)
    {
        if (rgba16Float.Length != checked(PngRgba8Writer.GetPixelByteCount(width, height) * 2))
        {
            throw new ArgumentException("HDR color storage does not match its dimensions.", nameof(rgba16Float));
        }
        Width = width;
        Height = height;
        Rgba16Float = rgba16Float;
    }

    /// <summary>Gets the width.</summary>
    public int Width { get; }
    /// <summary>Gets the height.</summary>
    public int Height { get; }
    /// <summary>Gets packed top-down little-endian RGBA binary16 samples without another pixel copy.</summary>
    public ReadOnlyMemory<byte> Rgba16Float { get; }
}

/// <summary>Top-down, managed-owned normalized device depth in [0, 1], not metric camera distance.</summary>
/// <remarks>
/// Clear is one and cannot be distinguished from a far-plane write. No independent hit mask is implied.
/// </remarks>
public sealed class RenderJobDeviceDepth
{
    /// <summary>Validates dimensions and the exact float extent without copying samples.</summary>
    public RenderJobDeviceDepth(int width, int height, ReadOnlyMemory<float> values)
    {
        if (values.Length != PngRgba8Writer.GetPixelByteCount(width, height) / 4)
        {
            throw new ArgumentException("Device-depth storage does not match its dimensions.", nameof(values));
        }
        Width = width;
        Height = height;
        Values = values;
    }

    /// <summary>Gets the width.</summary>
    public int Width { get; }
    /// <summary>Gets the height.</summary>
    public int Height { get; }
    /// <summary>Gets packed top-down float32 samples, consumed before the next source render.</summary>
    public ReadOnlyMemory<float> Values { get; }
}

/// <summary>Describes one published image without retaining its pixel buffer.</summary>
public sealed class RenderDiskFrameResult
{
    internal RenderDiskFrameResult(
        int index, StageRenderState state, string fileName, long bytes, string sha256,
        string? depthFileName = null, long depthBytes = 0, string? depthSha256 = null,
        string? hdrColorFileName = null, long hdrColorBytes = 0, string? hdrColorSha256 = null,
        RenderHdrColorFormat hdrColorFormat = RenderHdrColorFormat.RawRgba16Float)
    {
        Index = index;
        State = state;
        FileName = fileName;
        Bytes = bytes;
        Sha256 = sha256;
        DepthFileName = depthFileName;
        DepthBytes = depthBytes;
        DepthSha256 = depthSha256;
        HdrColorFileName = hdrColorFileName;
        HdrColorBytes = hdrColorBytes;
        HdrColorSha256 = hdrColorSha256;
        HdrColorFormat = hdrColorFileName is null ? null : hdrColorFormat;
    }

    /// <summary>Gets the zero-based frame index.</summary>
    public int Index { get; }
    /// <summary>Gets the unchanged requested state.</summary>
    public StageRenderState State { get; }
    /// <summary>Gets the generated image name relative to the job directory.</summary>
    public string FileName { get; }
    /// <summary>Gets the encoded file length.</summary>
    public long Bytes { get; }
    /// <summary>Gets the lowercase SHA256 of the encoded file.</summary>
    public string Sha256 { get; }
    /// <summary>Gets the optional float32-little-endian depth filename, relative to the job directory.</summary>
    public string? DepthFileName { get; }
    /// <summary>Gets the depth file length, or zero when no depth was requested.</summary>
    public long DepthBytes { get; }
    /// <summary>Gets the depth file SHA256, or null when no depth was requested.</summary>
    public string? DepthSha256 { get; }
    /// <summary>Gets the optional raw binary16 or EXR filename relative to the job directory.</summary>
    public string? HdrColorFileName { get; }
    /// <summary>Gets the HDR color file length, or zero when no HDR color was requested.</summary>
    public long HdrColorBytes { get; }
    /// <summary>Gets the HDR color SHA256, or null when no HDR color was requested.</summary>
    public string? HdrColorSha256 { get; }
    /// <summary>Gets the encoded HDR format, or null when no HDR plane was requested.</summary>
    public RenderHdrColorFormat? HdrColorFormat { get; }
}

/// <summary>A completed, atomically published disk job.</summary>
public sealed class RenderDiskJobResult
{
    internal RenderDiskJobResult(
        string outputDirectory, RenderDiskFrameResult[] frames, long totalBytes, RenderDiagnostic[] diagnostics)
    {
        OutputDirectory = outputDirectory;
        Frames = new OwnedReadOnlyList<RenderDiskFrameResult>(frames);
        TotalBytes = totalBytes;
        Diagnostics = new OwnedReadOnlyList<RenderDiagnostic>(diagnostics);
    }

    /// <summary>Gets the published directory, including manifest.json.</summary>
    public string OutputDirectory { get; }
    /// <summary>Gets bounded frame descriptors in order, without pixel buffers.</summary>
    public IReadOnlyList<RenderDiskFrameResult> Frames { get; }
    /// <summary>Gets total published bytes including the manifest.</summary>
    public long TotalBytes { get; }
    /// <summary>Gets up to 128 distinct diagnostics observed across all frames, in first-occurrence order.</summary>
    public IReadOnlyList<RenderDiagnostic> Diagnostics { get; }
}
