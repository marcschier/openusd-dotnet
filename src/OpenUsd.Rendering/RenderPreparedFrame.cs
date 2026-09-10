// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Geom;

namespace OpenUsd.Rendering;

/// <summary>Immutable raster geometry prepared from a product and a sampled camera.</summary>
public sealed class RenderPreparedFrame : IUsdDetachedResult
{
    internal RenderPreparedFrame(
        RenderProductRequest request,
        UsdGeomCameraState sampledCamera,
        double timeCode,
        CameraState camera,
        RenderProductFraming framing,
        RenderCameraFrameSettings? cameraSettings = null)
    {
        Request = request;
        SampledCamera = sampledCamera;
        TimeCode = timeCode;
        Camera = camera;
        OutputDimensions = framing.OutputDimensions;
        DataWindowMinX = framing.DataWindowMinX;
        DataWindowMinY = framing.DataWindowMinY;
        PixelAspectRatio = framing.PixelAspectRatio;
        CameraSettings = cameraSettings;
    }

    /// <summary>Gets the request, including output variables and settings still to be honored by an adapter.</summary>
    public RenderProductRequest Request { get; }

    /// <summary>Gets the unmodified sampled camera optics, including focus and aperture information.</summary>
    public UsdGeomCameraState SampledCamera { get; }

    /// <summary>Gets sampled shutter/exposure inputs, or null for geometry-only preparation.</summary>
    public RenderCameraFrameSettings? CameraSettings { get; }

    /// <summary>Gets the numeric USD time code of this frame.</summary>
    public double TimeCode { get; }

    /// <summary>Gets the conformed, data-window-adjusted camera.</summary>
    public CameraState Camera { get; }

    /// <summary>Gets the pixel dimensions of the cropped or overscanned output.</summary>
    public ViewportDimensions OutputDimensions { get; }

    /// <summary>Gets the data window's inclusive X origin in the full-resolution raster.</summary>
    public int DataWindowMinX { get; }

    /// <summary>Gets the data window's inclusive Y origin, measured from the bottom of the full raster.</summary>
    public int DataWindowMinY { get; }

    /// <summary>Gets the pixel width-to-height ratio after conformance.</summary>
    public double PixelAspectRatio { get; }
}
