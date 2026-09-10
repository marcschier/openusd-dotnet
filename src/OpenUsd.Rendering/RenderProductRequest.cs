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
        if (camera.Prim.GetAttribute("clippingPlanes").GetValueState(timeCode).HasAuthoredValueOpinion)
        {
            throw new NotSupportedException(
                $"Camera '{CameraPath}' has authored clippingPlanes that cannot be omitted from a product request.");
        }
        UsdMatrix4d localToWorld = camera.Xformable.GetWorldTransform(timeCode);
        if (!localToWorld.TryInvert(out UsdMatrix4d worldToView))
        {
            throw new InvalidOperationException($"Camera '{CameraPath}' has a non-invertible transform.");
        }
        UsdGeomCameraState sampled = camera.GetState(timeCode);
        return includeExecutionSettings
            ? PrepareFrame(worldToView, sampled, timeCode, RenderCameraFrameSettings.Read(stage, prim, timeCode))
            : PrepareFrame(worldToView, sampled, timeCode);
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
        double timeCode) => PrepareFrameCore(worldToView, sampledCamera, timeCode, null);

    /// <summary>Prepares a raster frame with caller-sampled shutter and exposure inputs.</summary>
    /// <remarks>The caller supplies every camera input from the same camera/time sample.</remarks>
    public RenderPreparedFrame PrepareFrame(
        UsdMatrix4d worldToView, UsdGeomCameraState sampledCamera, double timeCode,
        RenderCameraFrameSettings cameraSettings)
    {
        ArgumentNullException.ThrowIfNull(cameraSettings);
        return PrepareFrameCore(worldToView, sampledCamera, timeCode, cameraSettings);
    }

    private RenderPreparedFrame PrepareFrameCore(
        UsdMatrix4d worldToView, UsdGeomCameraState sampledCamera, double timeCode,
        RenderCameraFrameSettings? cameraSettings)
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
            StageCameraProjectionMath.CreateProjectionMatrix(sampledCamera, framing.Window));
        return new RenderPreparedFrame(this, sampledCamera, timeCode, camera, framing, cameraSettings);
    }
}
