// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using OpenUsd.Render;

namespace OpenUsd.Rendering;

/// <summary>Identifies the exact captured plane backing an admitted raw render variable.</summary>
public enum RenderProductPlane
{
    /// <summary>Camera-exposed, pre-display packed top-down RGBA binary16 with stored framebuffer alpha.</summary>
    HdrColor,
    /// <summary>Top-down float32 normalized device depth in [0,1], with clear/far value one.</summary>
    DeviceDepth
}

/// <summary>Maps one unchanged requested variable to its explicitly typed output plane.</summary>
public sealed class RenderProductOutputBinding
{
    internal RenderProductOutputBinding(UsdRenderVariableSpecification variable, RenderProductPlane plane)
    {
        Variable = variable;
        Plane = plane;
    }

    /// <summary>Gets the original variable declaration; its source and data type are not substituted.</summary>
    public UsdRenderVariableSpecification Variable { get; }
    /// <summary>Gets the required captured plane.</summary>
    public RenderProductPlane Plane { get; }
}

/// <summary>Admits one sampled raster product for the shared bounded split-plane disk-job engine.</summary>
/// <remarks>
/// The initial profile supports raw half4 color and float normalized depth, one material-binding
/// purpose, standard scene purposes and no active depth of field. The authored camera exposure
/// multiplier scales the HDR color variable and adds its stops to the PNG companion's display
/// exposure, so both outputs carry the same camera exposure. An open shutter with motion blur enabled
/// renders eight equal-weight samples at stratified sub-interval centers and stores their mean;
/// such frames cannot include device depth or exceed 4096 x 4096 pixels.
/// Unsupported variables/settings remain inspectable through RenderProductRequest but cannot execute here.
/// A PNG is an explicitly identified display companion, not a replacement for a requested variable.
/// Adapters must apply the admitted scene filters and all render settings before returning each frame.
/// </remarks>
public sealed partial class RenderProductJobPlan : IUsdDetachedResult
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly IReadOnlyList<StageRenderState> _states;
    private readonly IReadOnlyList<StageRenderState>?[]? _shutterStates;

    /// <summary>
    /// Copies and validates samples from one immutable product request before rendering or file creation.
    /// </summary>
    /// <remarks>
    /// Renderer settings are explicit caller choices. Their display transform/exposure affect the PNG
    /// companion, not the raw HDR variable; scene lighting/material/quality choices affect the render.
    /// Each frame's state adds the sampled camera exposure in stops to the display exposure.
    /// </remarks>
    public RenderProductJobPlan(
        StageIdentity stage, IReadOnlyList<RenderPreparedFrame> frames,
        RenderSettings renderSettings, ulong? sourceStageRevision = null)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(frames);
        int count = frames.Count;
        if (count is < 1 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(frames), "A product job requires 1-4096 prepared samples.");
        }
        RenderPreparedFrame first = frames[0] ??
            throw new ArgumentException("A sample cannot be null.", nameof(frames));
        Request = first.Request;
        (IncludedPurposes, Outputs) = ValidateStaticRequest(Request);
        MaterialBindingPurpose = Request.Specification.MaterialBindingPurposes[0];
        IncludeHdrColor = Outputs.Any(static output => output.Plane == RenderProductPlane.HdrColor);
        IncludeDeviceDepth = Outputs.Any(static output => output.Plane == RenderProductPlane.DeviceDepth);
        SourceStageRevision = sourceStageRevision;
        RenderSettings = renderSettings;
        var samples = new RenderPreparedFrame[count];
        var states = new StageRenderState[count];
        IReadOnlyList<StageRenderState>?[]? shutterStates = null;
        StageRenderState state = StageRenderState.Create(stage)
            .WithDisplay(new SceneDisplayState(
                IncludedPurposes, RenderVisibility.RespectAuthored, RenderDrawMode.SmoothShaded))
            .WithRenderSettings(renderSettings);
        for (int index = 0; index < count; index++)
        {
            RenderPreparedFrame frame = frames[index] ??
                throw new ArgumentException("A prepared sample cannot be null.", nameof(frames));
            if (!ReferenceEquals(frame.Request, Request))
            {
                throw new ArgumentException(
                    "All samples must belong to the same immutable product request.", nameof(frames));
            }
            ValidateCamera(frame, IncludeDeviceDepth);
            samples[index] = frame;
            RenderSettings exposed = ProductCameraExposure.ApplyToDisplay(
                renderSettings, frame.CameraSettings!.LinearExposureScale);
            if (frame.ShutterSamples.Count != 0)
            {
                var shutter = new StageRenderState[frame.ShutterSamples.Count];
                for (int sample = 0; sample < shutter.Length; sample++)
                {
                    RenderPreparedFrame subframe = frame.ShutterSamples[sample];
                    shutter[sample] = state = state.WithCamera(subframe.Camera)
                        .WithViewport(subframe.OutputDimensions).WithTime(new StageTime(subframe.TimeCode))
                        .WithRenderSettings(exposed).AdvanceRevision();
                }
                shutterStates ??= new IReadOnlyList<StageRenderState>?[count];
                shutterStates[index] = new OwnedReadOnlyList<StageRenderState>(shutter);
            }
            states[index] = state = state.WithCamera(frame.Camera).WithViewport(frame.OutputDimensions)
                .WithTime(new StageTime(frame.TimeCode)).WithRenderSettings(exposed).AdvanceRevision();
        }
        Frames = new OwnedReadOnlyList<RenderPreparedFrame>(samples);
        _states = new OwnedReadOnlyList<StageRenderState>(states);
        _shutterStates = shutterStates;
    }

    /// <summary>Gets the per-sample states accumulated for a frame's active shutter, or null.</summary>
    internal IReadOnlyList<StageRenderState>? ShutterStates(int index) => _shutterStates?[index];

    /// <summary>Gets the largest output raster that may be accumulated across a shutter interval.</summary>
    internal const int MaximumAccumulatedPixels = 4096 * 4096;

    /// <summary>Gets the unchanged authored request and explicit raster overrides.</summary>
    public RenderProductRequest Request { get; }
    /// <summary>Gets the immutable ordered camera/raster samples.</summary>
    public IReadOnlyList<RenderPreparedFrame> Frames { get; }
    /// <summary>Gets requested output bindings in their original order.</summary>
    public IReadOnlyList<RenderProductOutputBinding> Outputs { get; }
    /// <summary>Gets the exact scene-purpose mask that the capture adapter must apply.</summary>
    public RenderPurpose IncludedPurposes { get; }
    /// <summary>Gets the single material-binding purpose that the capture adapter must apply.</summary>
    public string MaterialBindingPurpose { get; }
    /// <summary>Gets explicit renderer settings, with display conversion separate from raw product values.</summary>
    public RenderSettings RenderSettings { get; }
    /// <summary>Gets the caller's source revision claim; callers retain stage identity and writer exclusion.</summary>
    public ulong? SourceStageRevision { get; }
    /// <summary>Gets whether the product requires actual pre-display HDR color.</summary>
    public bool IncludeHdrColor { get; }
    /// <summary>Gets whether the product requires actual normalized device depth.</summary>
    public bool IncludeDeviceDepth { get; }

    /// <summary>
    /// Creates a bounded job with caller-authorized output and generated filenames, never product-name authority.
    /// </summary>
    /// <remarks>
    /// Raw split planes preserve crop/overscan and pixel aspect in the manifest. EXR output stores the same
    /// crop/overscan as a data window inside the full display window, plus the pixel aspect.
    /// The PNG display companion is not a separately authored product or a color-space conversion claim.
    /// </remarks>
    public RenderDiskJobRequest CreateJob(
        string outputDirectory, RenderHdrColorFormat hdrColorFormat = RenderHdrColorFormat.RawRgba16Float,
        RenderDiskJobLimits? limits = null)
        => CreateJob(outputDirectory, hdrColorFormat, limits, frameEncoded: null);

    internal RenderDiskJobRequest CreateJob(
        string outputDirectory, RenderHdrColorFormat hdrColorFormat, RenderDiskJobLimits? limits,
        Action<int, int>? frameEncoded)
    {
        if (hdrColorFormat == RenderHdrColorFormat.Exr)
        {
            ViewportDimensions full = Request.Overrides.Resolution ??
                new ViewportDimensions(Request.Product.Width, Request.Product.Height);
            foreach (RenderPreparedFrame frame in Frames)
            {
                // Crop, overscan and pixel aspect become EXR data/display windows; refuse before rendering
                // anything when a frame has no exact encoder representation.
                _ = ExrImageWindows.FromProductFrame(full, frame.DataWindowMinX, frame.DataWindowMinY,
                    frame.OutputDimensions.Height, frame.PixelAspectRatio)
                    .Validate(frame.OutputDimensions.Width, frame.OutputDimensions.Height);
            }
        }
        return new RenderDiskJobRequest(outputDirectory, _states, IncludeDeviceDepth, IncludeHdrColor,
            hdrColorFormat, limits, this)
        {
            FrameEncoded = frameEncoded
        };
    }

    internal static (RenderPurpose IncludedPurposes, IReadOnlyList<RenderProductOutputBinding> Outputs)
        ValidateStaticRequest(RenderProductRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateSpecification(request);
        return (ResolvePurposes(request.Specification.IncludedPurposes), ResolveOutputs(request));
    }

    private static void ValidateSpecification(RenderProductRequest request)
    {
        UsdRenderSpecification specification = request.Specification;
        RequireText(specification.SettingsPath);
        RequireText(request.Product.Path);
        RequireText(request.Product.Name);
        RequireText(request.CameraPath);
        if (request.Product.ProductType != "raster" || specification.NamespacedSettingNames.Count != 0 ||
            request.Product.NamespacedSettingNames.Count != 0)
        {
            throw new NotSupportedException("Product execution requires raster output without unevaluated settings.");
        }
        if (specification.RenderingColorSpace.Length != 0)
        {
            throw new NotSupportedException(
                "The capture adapters cannot certify the requested named rendering color space.");
        }
        if (specification.MaterialBindingPurposes.Count != 1)
        {
            throw new NotSupportedException("Product execution currently requires one exact material-binding purpose.");
        }
        RequireText(specification.MaterialBindingPurposes[0]);
        if (specification.MaterialBindingPurposes[0].Length > 128)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request), "The material-binding purpose exceeds 128 characters.");
        }
    }

    private static RenderPurpose ResolvePurposes(IReadOnlyList<string> purposes)
    {
        if (purposes.Count > 4)
        {
            throw new NotSupportedException("Product execution accepts at most four standard scene purposes.");
        }
        RenderPurpose result = RenderPurpose.None;
        foreach (string purpose in purposes)
        {
            result |= purpose switch
            {
                "default" => RenderPurpose.Default,
                "proxy" => RenderPurpose.Proxy,
                "render" => RenderPurpose.Render,
                "guide" => RenderPurpose.Guide,
                _ => throw new NotSupportedException($"Unknown included purpose '{purpose}' cannot be ignored.")
            };
        }
        return result;
    }

    private static OwnedReadOnlyList<RenderProductOutputBinding> ResolveOutputs(RenderProductRequest request)
    {
        if (request.Outputs.Count is < 1 or > 2)
        {
            throw new NotSupportedException(
                "The current product profile accepts one color variable and/or one depth variable.");
        }
        var outputs = new RenderProductOutputBinding[request.Outputs.Count];
        var seen = new HashSet<RenderProductPlane>();
        for (int index = 0; index < outputs.Length; index++)
        {
            UsdRenderVariableSpecification variable = request.Outputs[index];
            RequireText(variable.Path);
            if (variable.SourceType != "raw" || variable.NamespacedSettingNames.Count != 0)
            {
                throw new NotSupportedException(
                    $"Variable '{variable.Path}' has an unsupported source or unevaluated settings.");
            }
            RenderProductPlane plane = (variable.SourceName, variable.DataType) switch
            {
                ("color", "half4") => RenderProductPlane.HdrColor,
                ("depth", "float") => RenderProductPlane.DeviceDepth,
                _ => throw new NotSupportedException(
                    $"Variable '{variable.Path}' requests unsupported '{variable.SourceName}' / '{variable.DataType}'.")
            };
            if (!seen.Add(plane))
            {
                throw new NotSupportedException("The current product profile requires one unique variable per plane.");
            }
            outputs[index] = new RenderProductOutputBinding(variable, plane);
        }
        return new OwnedReadOnlyList<RenderProductOutputBinding>(outputs);
    }

    private static void ValidateCamera(RenderPreparedFrame frame, bool includeDeviceDepth)
    {
        RenderCameraFrameSettings settings = frame.CameraSettings ??
            throw new NotSupportedException(
                "Executing a product requires sampled camera shutter and exposure, not geometry alone.");
        if (!frame.Request.Product.DisableMotionBlur && (settings.ShutterOpen != 0 || settings.ShutterClose != 0))
        {
            if (settings.ShutterClose <= settings.ShutterOpen)
            {
                throw new NotSupportedException(
                    "A zero-length or inverted camera shutter interval cannot be accumulated.");
            }
            if (frame.ShutterSamples.Count != RenderProductRequest.MotionBlurSampleCount)
            {
                throw new NotSupportedException(
                    "An active camera shutter needs stage-sampled shutter samples from PrepareExecutionFrame.");
            }
            if (includeDeviceDepth)
            {
                throw new NotSupportedException(
                    "Normalized device depth has no defined value when accumulated across a shutter interval.");
            }
            if ((long)frame.OutputDimensions.Width * frame.OutputDimensions.Height > MaximumAccumulatedPixels)
            {
                throw new NotSupportedException(
                    "The output raster exceeds the shutter-accumulation pixel limit of 4096 x 4096.");
            }
        }
        if (!frame.Request.Product.DisableDepthOfField && frame.SampledCamera.FStop != 0)
        {
            throw new NotSupportedException(
                "The current capture profile cannot omit the active depth-of-field aperture.");
        }
    }

    private static void RequireText(string text)
    {
        if (text.Length > 1024 || text.Any(char.IsControl) || Utf8.GetByteCount(text) > 4096)
        {
            throw new ArgumentException("Product metadata exceeds its bounded UTF-8 text domain.");
        }
    }
}
