// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
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
        FramingWindow = framing.Window;
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
    public CameraState Camera { get; private init; }

    /// <summary>Gets the pixel dimensions of the cropped or overscanned output.</summary>
    public ViewportDimensions OutputDimensions { get; }

    /// <summary>Gets the data window's inclusive X origin in the full-resolution raster.</summary>
    public int DataWindowMinX { get; }

    /// <summary>Gets the data window's inclusive Y origin, measured from the bottom of the full raster.</summary>
    public int DataWindowMinY { get; }

    /// <summary>Gets the pixel width-to-height ratio after conformance.</summary>
    public double PixelAspectRatio { get; }

    /// <summary>Gets equal-weight shutter and lens samples averaged into this frame, or an empty list.</summary>
    /// <remarks>
    /// Only stage-sampled execution frames with an open shutter and motion blur enabled, or an active
    /// depth-of-field aperture, carry samples. Each sample shares this frame's raster and holds its own
    /// absolute time code and, for depth of field, a camera moved across the lens aperture.
    /// </remarks>
    public IReadOnlyList<RenderPreparedFrame> AccumulationSamples { get; private init; } = [];

    /// <summary>Gets the camera-space lens offset in scene units of a depth-of-field sample, or null.</summary>
    internal Vector2? LensOffset { get; init; }

    internal StageCameraApertureWindow FramingWindow { get; private init; }

    internal RenderPreparedFrame WithLensSample(CameraState camera, Vector2 lensOffset) => new(this)
    {
        Camera = camera,
        LensOffset = lensOffset,
    };

    internal RenderPreparedFrame WithAccumulationSamples(RenderPreparedFrame[] samples) => new(this)
    {
        AccumulationSamples = Array.AsReadOnly(samples),
    };

    private RenderPreparedFrame(RenderPreparedFrame source)
    {
        Request = source.Request;
        SampledCamera = source.SampledCamera;
        TimeCode = source.TimeCode;
        Camera = source.Camera;
        OutputDimensions = source.OutputDimensions;
        DataWindowMinX = source.DataWindowMinX;
        DataWindowMinY = source.DataWindowMinY;
        PixelAspectRatio = source.PixelAspectRatio;
        FramingWindow = source.FramingWindow;
        CameraSettings = source.CameraSettings;
        LensOffset = source.LensOffset;
    }
}
