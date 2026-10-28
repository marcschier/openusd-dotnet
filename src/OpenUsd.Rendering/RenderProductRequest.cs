// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using OpenUsd.Geom;
using OpenUsd.Render;

namespace OpenUsd.Rendering;

/// <summary>A detached product request that retains its complete authored output specification.</summary>
/// <remarks>
/// Preparing a frame resolves raster geometry only. It does not evaluate render variables,
/// namespaced settings, motion blur or depth of field, or claim that a backend supports them.
/// An executing adapter must honor or explicitly refuse those remaining requested semantics.
/// </remarks>
public sealed class RenderProductRequest : IUsdDetachedResult
{
    /// <summary>Selects one product in native relationship order without changing the specification.</summary>
    public RenderProductRequest(
        UsdRenderSpecification specification,
        int productIndex,
        RenderProductOverrides? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(specification);
        if ((uint)productIndex >= (uint)specification.Products.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(productIndex));
        }
        Specification = specification;
        Product = specification.Products[productIndex];
        Overrides = overrides ?? RenderProductOverrides.None;
        var outputs = new UsdRenderVariableSpecification[Product.RenderVariableIndices.Count];
        for (int index = 0; index < outputs.Length; index++)
        {
            outputs[index] = specification.RenderVariables[Product.RenderVariableIndices[index]];
        }
        Outputs = new OwnedReadOnlyList<UsdRenderVariableSpecification>(outputs);
    }

    /// <summary>Gets the original immutable specification, including unapplied settings.</summary>
    public UsdRenderSpecification Specification { get; }

    /// <summary>Gets the selected product.</summary>
    public UsdRenderProductSpecification Product { get; }

    /// <summary>Gets explicit overrides, independently of the unchanged authored product.</summary>
    public RenderProductOverrides Overrides { get; }

    /// <summary>Gets the effective camera path after applying the explicit override.</summary>
    public string CameraPath => Overrides.CameraPath ?? Product.CameraPath;

    /// <summary>Gets requested variables in product order, without replacing unknown outputs with beauty.</summary>
    public IReadOnlyList<UsdRenderVariableSpecification> Outputs { get; }

    /// <summary>Samples the effective stage camera and prepares raster geometry at a numeric USD time code.</summary>
    /// <remarks>
    /// Call on the owning stage thread, normally within a scheduler callback. This does not
    /// mutate the stage or execute rendering. Authored extra clipping planes are refused until
    /// the stage-camera data interface can transfer them instead of silently dropping them.
    /// </remarks>
    public RenderPreparedFrame PrepareFrame(UsdStage stage, double timeCode) =>
        PrepareStageFrame(stage, timeCode, includeExecutionSettings: false);

    /// <summary>Samples raster geometry plus shutter/exposure inputs for product-execution admission.</summary>
    /// <remarks>Call on the owning stage thread. No stage or renderer is mutated.</remarks>
    public RenderPreparedFrame PrepareExecutionFrame(UsdStage stage, double timeCode) =>
        PrepareStageFrame(stage, timeCode, includeExecutionSettings: true);

    private RenderPreparedFrame PrepareStageFrame(UsdStage stage, double timeCode, bool includeExecutionSettings)
    {
        ArgumentNullException.ThrowIfNull(stage);
        if (!double.IsFinite(timeCode))
        {
            throw new ArgumentOutOfRangeException(nameof(timeCode));
        }
        if (!stage.HasPrim(CameraPath))
        {
            throw new ArgumentException($"Camera '{CameraPath}' does not exist in the stage.", nameof(stage));
        }
        UsdPrim prim = stage.GetPrim(CameraPath);
        if (includeExecutionSettings && (!prim.IsActive() || !prim.IsDefined()))
        {
            throw new NotSupportedException($"Camera '{CameraPath}' must be active and defined.");
        }
        UsdGeomCamera camera = UsdGeomCamera.Wrap(prim);
        (UsdMatrix4d worldToView, UsdGeomCameraState sampled, Vector4[] planes) = SampleCamera(camera, timeCode);
        if (!includeExecutionSettings)
        {
            return PrepareFrameCore(worldToView, sampled, timeCode, null, planes);
        }
        RenderCameraFrameSettings settings = RenderCameraFrameSettings.Read(stage, prim, timeCode);
        RenderPreparedFrame frame = PrepareFrameCore(worldToView, sampled, timeCode, settings, planes);
        bool blur = settings.ShutterClose > settings.ShutterOpen && !Product.DisableMotionBlur;
        bool lens = IsDepthOfFieldActive(sampled);
        if (!blur && !lens)
        {
            return frame;
        }
        int count = lens ? DepthOfFieldSampleCount : MotionBlurSampleCount;
        var samples = new RenderPreparedFrame[count];
        double interval = settings.ShutterClose - settings.ShutterOpen;
        for (int index = 0; index < samples.Length; index++)
        {
            RenderPreparedFrame sample = frame;
            if (blur)
            {
                // Stratified box-filter samples at subinterval centers; the shutter offsets are in time codes.
                // With depth of field, 5 is coprime with 16 and decorrelates time strata from lens strata.
                int stratum = lens ? index * 5 % count : index;
                double time = timeCode + settings.ShutterOpen + (interval * (stratum + 0.5) / count);
                if (!double.IsFinite(time))
                {
                    throw new NotSupportedException("The camera shutter interval has no finite sample times.");
                }
                (UsdMatrix4d sampleView, UsdGeomCameraState sampleOptics, Vector4[] samplePlanes) =
                    SampleCamera(camera, time);
                sample = PrepareFrameCore(sampleView, sampleOptics, time, settings, samplePlanes);
            }
            if (lens)
            {
                sample = PrepareLensSample(sample, index);
            }
            if (sample.OutputDimensions != frame.OutputDimensions ||
                sample.DataWindowMinX != frame.DataWindowMinX || sample.DataWindowMinY != frame.DataWindowMinY)
            {
                throw new NotSupportedException(
                    "The product raster changes within the shutter interval and cannot be accumulated.");
            }
            samples[index] = sample;
        }
        return frame.WithAccumulationSamples(samples);
    }

    /// <summary>Gets the stratified sample count used when a product renders an active shutter interval.</summary>
    internal const int MotionBlurSampleCount = 8;

    /// <summary>Gets the stratified lens sample count used when a product renders depth of field.</summary>
    internal const int DepthOfFieldSampleCount = 16;

    /// <summary>Gets the scene-unit size of one USD focal-length unit, matching Hydra's lens conversion.</summary>
    internal const double FocalLengthUnit = 0.1;

    internal bool IsDepthOfFieldActive(UsdGeomCameraState optics) =>
        !Product.DisableDepthOfField && optics.FStop != 0;

    /// <summary>Moves a frame's eye to one stratified point on the thin-lens aperture disk.</summary>
    /// <remarks>
    /// The eye moves by <c>(dx, dy)</c> in camera space and the frustum window moves by
    /// <c>-(dx, dy) / focusDistance</c>, so the focus plane projects to the same pixels for every sample.
    /// </remarks>
    internal static RenderPreparedFrame PrepareLensSample(RenderPreparedFrame frame, int index)
    {
        UsdGeomCameraState optics = frame.SampledCamera;
        if (optics.Projection != UsdGeomCameraProjection.Perspective)
        {
            throw new NotSupportedException("Depth of field requires a perspective camera.");
        }
        if (optics.FocusDistance <= 0)
        {
            throw new NotSupportedException("Depth of field requires a positive camera focusDistance.");
        }
        double radius = optics.FocalLength * FocalLengthUnit / (2 * optics.FStop);
        (double u, double v) = ConcentricDisk((index % 4 + 0.5) / 4, (index / 4 + 0.5) / 4);
        double dx = radius * u;
        double dy = radius * v;
        double shiftX = dx / optics.FocusDistance;
        double shiftY = dy / optics.FocusDistance;
        if (!double.IsFinite(radius) || !double.IsFinite(shiftX) || !double.IsFinite(shiftY))
        {
            throw new NotSupportedException("The camera lens aperture has no finite sample positions.");
        }
        StageCameraApertureWindow window = frame.FramingWindow;
        StageCameraApertureWindow shifted = StageCameraProjectionMath.CreateWindow(
            window.Left - shiftX, window.Right - shiftX, window.Bottom - shiftY, window.Top - shiftY);
        Matrix4x4 view = frame.Camera.View * Matrix4x4.CreateTranslation((float)-dx, (float)-dy, 0);
        var camera = new CameraState(
            view,
            StageCameraProjectionMath.CreateProjectionMatrix(optics, shifted),
            ShiftClipPlanes(frame.Camera.ClipPlanes, dx, dy));
        return frame.WithLensSample(camera, new Vector2((float)dx, (float)dy));
    }

    /// <summary>Re-expresses camera-space clip planes after the camera origin moves by (dx, dy, 0).</summary>
    /// <remarks>A point p in the moved space is p + (dx, dy, 0) in the original space, so only d changes.</remarks>
    internal static Vector4[] ShiftClipPlanes(IReadOnlyList<Vector4> planes, double dx, double dy)
    {
        var shifted = new Vector4[planes.Count];
        for (int index = 0; index < shifted.Length; index++)
        {
            Vector4 plane = planes[index];
            shifted[index] = plane with { W = (float)(plane.W + (plane.X * dx) + (plane.Y * dy)) };
        }
        return shifted;
    }

    // Shirley-Chiu concentric map from the unit square to the unit disk, preserving stratification.
    private static (double U, double V) ConcentricDisk(double s, double t)
    {
        double a = (2 * s) - 1;
        double b = (2 * t) - 1;
        if (a == 0 && b == 0)
        {
            return (0, 0);
        }
        (double r, double phi) = Math.Abs(a) > Math.Abs(b)
            ? (a, Math.PI / 4 * (b / a))
            : (b, (Math.PI / 2) - (Math.PI / 4 * (a / b)));
        return (r * Math.Cos(phi), r * Math.Sin(phi));
    }

    private (UsdMatrix4d WorldToView, UsdGeomCameraState Optics, Vector4[] ClipPlanes) SampleCamera(
        UsdGeomCamera camera, double timeCode)
    {
        UsdMatrix4d localToWorld = camera.Xformable.GetWorldTransform(timeCode);
        if (!localToWorld.TryInvert(out UsdMatrix4d worldToView))
        {
            throw new InvalidOperationException($"Camera '{CameraPath}' has a non-invertible transform.");
        }
        return (worldToView, camera.GetState(timeCode), SampleClipPlanes(camera.Prim, timeCode));
    }

    /// <summary>Reads authored camera-space clippingPlanes; each keeps points where ax + by + cz + d is non-negative.</summary>
    private Vector4[] SampleClipPlanes(UsdPrim prim, double timeCode)
    {
        if (!prim.GetAttribute("clippingPlanes").GetValueState(timeCode).HasAuthoredValueOpinion)
        {
            return [];
        }
        UsdVec4f[] authored = prim.GetVec4fArray("clippingPlanes", timeCode);
        if (authored.Length > CameraState.MaxClipPlanes)
        {
            throw new NotSupportedException(
                $"Camera '{CameraPath}' authors {authored.Length} clippingPlanes; at most {CameraState.MaxClipPlanes} are supported.");
        }
        var planes = new Vector4[authored.Length];
        for (int index = 0; index < planes.Length; index++)
        {
            UsdVec4f plane = authored[index];
            if (!float.IsFinite(plane.X) || !float.IsFinite(plane.Y) || !float.IsFinite(plane.Z) ||
                !float.IsFinite(plane.W) || (plane.X == 0 && plane.Y == 0 && plane.Z == 0))
            {
                throw new NotSupportedException(
                    $"Camera '{CameraPath}' clippingPlanes[{index}] is not a finite plane with a nonzero normal.");
            }
            planes[index] = new Vector4(plane.X, plane.Y, plane.Z, plane.W);
        }
        return planes;
    }

    /// <summary>Prepares a raster frame from a caller-sampled camera and world-to-view transform.</summary>
    /// <remarks>
    /// The caller supplies the selected camera's optics at <paramref name="timeCode"/>.
    /// Native specification aperture metadata is default-time data and is not reused as an animated sample.
    /// No stage, file, or renderer is mutated.
    /// </remarks>
    public RenderPreparedFrame PrepareFrame(
        UsdMatrix4d worldToView,
        UsdGeomCameraState sampledCamera,
        double timeCode) => PrepareFrameCore(worldToView, sampledCamera, timeCode, null, []);

    /// <summary>Prepares a raster frame with caller-sampled shutter and exposure inputs.</summary>
    /// <remarks>The caller supplies every camera input from the same camera/time sample.</remarks>
    public RenderPreparedFrame PrepareFrame(
        UsdMatrix4d worldToView, UsdGeomCameraState sampledCamera, double timeCode,
        RenderCameraFrameSettings cameraSettings)
    {
        ArgumentNullException.ThrowIfNull(cameraSettings);
        return PrepareFrameCore(worldToView, sampledCamera, timeCode, cameraSettings, []);
    }

    private RenderPreparedFrame PrepareFrameCore(
        UsdMatrix4d worldToView, UsdGeomCameraState sampledCamera, double timeCode,
        RenderCameraFrameSettings? cameraSettings, Vector4[] clipPlanes)
    {
        if (!double.IsFinite(timeCode))
        {
            throw new ArgumentOutOfRangeException(nameof(timeCode));
        }
        if (Product.ProductType != "raster")
        {
            throw new NotSupportedException($"Product '{Product.Path}' is not a raster product.");
        }
        if (!worldToView.TryInvert(out _))
        {
            throw new ArgumentException("The camera world-to-view transform must be invertible.", nameof(worldToView));
        }
        var window = new StageCameraApertureWindow(
            sampledCamera.WindowLeft,
            sampledCamera.WindowRight,
            sampledCamera.WindowBottom,
            sampledCamera.WindowTop);
        UsdVec4f data = Overrides.DataWindowNdc ?? Product.DataWindowNdc;
        RenderProductFraming framing = RenderProductFraming.Resolve(
            window,
            Overrides.Resolution ?? new ViewportDimensions(Product.Width, Product.Height),
            Overrides.PixelAspectRatio ?? Product.PixelAspectRatio,
            Overrides.AspectRatioConformPolicy ?? Product.AspectRatioConformPolicy,
            new Vector4(data.X, data.Y, data.Z, data.W));
        var camera = new CameraState(
            StageCameraMatrixConversion.ToMatrix4x4(worldToView),
            StageCameraProjectionMath.CreateProjectionMatrix(sampledCamera, framing.Window),
            clipPlanes);
        return new RenderPreparedFrame(this, sampledCamera, timeCode, camera, framing, cameraSettings);
    }
}
