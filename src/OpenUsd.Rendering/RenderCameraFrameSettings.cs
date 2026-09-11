// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd.Rendering;

/// <summary>Detached shutter and exposure inputs sampled alongside a product camera.</summary>
public sealed class RenderCameraFrameSettings : IUsdDetachedResult
{
    private static readonly UsdPropertyInspectionLimits InspectionLimits = new(
        maximumPropertyCount: 128, maximumTextBytes: 65_536, previewElements: 1,
        timeSamplePreview: 0, targetPreview: 0, maximumPreviewTextBytes: 128);

    /// <summary>Gets the standard zero-width shutter and unit-exposure settings.</summary>
    public static RenderCameraFrameSettings Default { get; } = new();

    /// <summary>Initializes finite camera inputs without applying exposure or motion blur.</summary>
    public RenderCameraFrameSettings(
        double shutterOpen = 0, double shutterClose = 0, double exposure = 0,
        double exposureIso = 100, double exposureTime = 1, double exposureFStop = 1,
        double exposureResponsivity = 1)
    {
        if (!double.IsFinite(shutterOpen) || !double.IsFinite(shutterClose) ||
            !double.IsFinite(exposure) || !double.IsFinite(exposureIso) ||
            !double.IsFinite(exposureTime) || !double.IsFinite(exposureFStop) ||
            !double.IsFinite(exposureResponsivity) || shutterOpen > shutterClose ||
            exposureIso < 0 || exposureTime < 0 || exposureFStop <= 0 || exposureResponsivity < 0)
        {
            throw new ArgumentException("Camera shutter and exposure inputs must be finite and physically valid.");
        }
        double scale = exposureResponsivity * exposureTime * (exposureIso / 100) *
            Math.Pow(2, exposure) / (exposureFStop * exposureFStop);
        if (!double.IsFinite(scale) || scale < 0)
        {
            throw new ArgumentException("The camera exposure scale is not representable.");
        }
        ShutterOpen = shutterOpen;
        ShutterClose = shutterClose;
        Exposure = exposure;
        ExposureIso = exposureIso;
        ExposureTime = exposureTime;
        ExposureFStop = exposureFStop;
        ExposureResponsivity = exposureResponsivity;
        LinearExposureScale = scale;
    }

    /// <summary>Gets the frame-relative shutter open time in USD time codes.</summary>
    public double ShutterOpen { get; }
    /// <summary>Gets the frame-relative shutter close time in USD time codes.</summary>
    public double ShutterClose { get; }
    /// <summary>Gets camera exposure compensation in stops, separate from display exposure.</summary>
    public double Exposure { get; }
    /// <summary>Gets the exposure ISO.</summary>
    public double ExposureIso { get; }
    /// <summary>Gets exposure time in seconds, separate from the motion-blur shutter interval.</summary>
    public double ExposureTime { get; }
    /// <summary>Gets the exposure f-stop, separate from the optics' depth-of-field aperture.</summary>
    public double ExposureFStop { get; }
    /// <summary>Gets the camera exposure responsivity.</summary>
    public double ExposureResponsivity { get; }
    /// <summary>Gets the OpenUSD camera-model exposure multiplier, without applying it to pixels.</summary>
    public double LinearExposureScale { get; }

    internal static RenderCameraFrameSettings Read(UsdStage stage, UsdPrim camera, double timeCode)
    {
        UsdPrimPropertySnapshot snapshot = stage.GetPrimPropertySnapshot(camera.Path, timeCode, InspectionLimits);
        if (snapshot.PrimPath != camera.Path || snapshot.TimeCode != timeCode)
        {
            throw new InvalidDataException("The native camera metadata does not match the requested prim and time.");
        }
        return new(
            ReadScalar(snapshot, "shutter:open", "double"),
            ReadScalar(snapshot, "shutter:close", "double"),
            ReadScalar(snapshot, "exposure", "float"),
            ReadScalar(snapshot, "exposure:iso", "float"),
            ReadScalar(snapshot, "exposure:time", "float"),
            ReadScalar(snapshot, "exposure:fStop", "float"),
            ReadScalar(snapshot, "exposure:responsivity", "float"));
    }

    private static double ReadScalar(UsdPrimPropertySnapshot snapshot, string name, string type)
    {
        foreach (UsdPropertySnapshot property in snapshot.Properties)
        {
            if (property.Name != name)
            {
                continue;
            }
            if (property is not UsdAttributePropertySnapshot attribute || attribute.TypeName != type ||
                attribute.ValueState != UsdPropertyValueState.Value ||
                attribute.Value is not
                { IsArray: false, Status: UsdPropertyPreviewStatus.Complete, ElementCount: 1 } value ||
                value.Elements is not [string text])
            {
                throw new NotSupportedException(
                    $"Camera attribute '{name}' requires a complete native '{type}' sample.");
            }
            // These are full invariant numeric values, not shortened Viewer labels or diagnostic text.
            if (type == "float" &&
                float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float single))
            {
                return single;
            }
            if (type == "double" &&
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            {
                return number;
            }
            throw new InvalidDataException($"Camera attribute '{name}' has an invalid invariant numeric sample.");
        }
        throw new NotSupportedException($"Camera attribute '{name}' is absent from the bounded native snapshot.");
    }
}
