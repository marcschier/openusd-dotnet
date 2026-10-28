// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;

namespace OpenUsd.Rendering;

public static partial class RenderDiskJob
{
    private static void WriteProductRequest(Utf8JsonWriter json, RenderProductJobPlan plan)
    {
        RenderProductRequest request = plan.Request;
        json.WriteStartObject("authoredProduct");
        json.WriteString("profile", "raw-raster-split-planes-v1");
        json.WriteString("settingsPath", request.Specification.SettingsPath);
        json.WriteString("productPath", request.Product.Path);
        json.WriteString("requestedProductName", request.Product.Name);
        json.WriteBoolean("productNameUsedAsWriteAuthority", false);
        json.WriteString("productType", request.Product.ProductType);
        json.WriteString("cameraPath", request.CameraPath);
        json.WriteString("authoredCameraPath", request.Product.CameraPath);
        json.WriteString("renderingColorSpace", request.Specification.RenderingColorSpace);
        json.WriteString("materialBindingPurpose", plan.MaterialBindingPurpose);
        json.WriteStartArray("includedPurposes");
        foreach (string purpose in request.Specification.IncludedPurposes)
        {
            json.WriteStringValue(purpose);
        }
        json.WriteEndArray();
        json.WriteBoolean("disableMotionBlur", request.Product.DisableMotionBlur);
        json.WriteBoolean("disableDepthOfField", request.Product.DisableDepthOfField);
        if (plan.SourceStageRevision is { } revision)
        {
            json.WriteNumber("callerSourceStageRevision", revision);
        }
        else
        {
            json.WriteNull("callerSourceStageRevision");
        }
        json.WriteString("pngRole", "display-companion-not-authored-render-variable");
        json.WriteEndObject();
    }

    private static void WriteProductFrame(
        Utf8JsonWriter json, RenderProductJobPlan plan, RenderDiskFrameResult result)
    {
        RenderPreparedFrame frame = plan.Frames[result.Index];
        RenderProductOverrides overrides = plan.Request.Overrides;
        ViewportDimensions full = overrides.Resolution ??
            new ViewportDimensions(plan.Request.Product.Width, plan.Request.Product.Height);
        json.WriteStartObject("productRaster");
        json.WriteNumber("fullWidth", full.Width);
        json.WriteNumber("fullHeight", full.Height);
        json.WriteNumber("dataWindowMinX", frame.DataWindowMinX);
        json.WriteNumber("dataWindowMinY", frame.DataWindowMinY);
        json.WriteString("dataWindowOriginConvention", "bottom-left");
        json.WriteNumber("pixelAspectRatio", frame.PixelAspectRatio);
        json.WriteString("aspectRatioConformPolicy",
            overrides.AspectRatioConformPolicy ?? plan.Request.Product.AspectRatioConformPolicy);
        json.WriteEndObject();
        RenderCameraFrameSettings camera = frame.CameraSettings!;
        json.WriteStartObject("productCamera");
        json.WriteNumber("shutterOpen", camera.ShutterOpen);
        json.WriteNumber("shutterClose", camera.ShutterClose);
        json.WriteNumber("exposure", camera.Exposure);
        json.WriteNumber("exposureIso", camera.ExposureIso);
        json.WriteNumber("exposureTime", camera.ExposureTime);
        json.WriteNumber("exposureFStop", camera.ExposureFStop);
        json.WriteNumber("exposureResponsivity", camera.ExposureResponsivity);
        json.WriteNumber("linearExposureScale", camera.LinearExposureScale);
        json.WriteNumber("focusDistance", frame.SampledCamera.FocusDistance);
        json.WriteNumber("fStop", frame.SampledCamera.FStop);
        json.WriteNumber("shutterSampleCount", frame.ShutterSamples.Count);
        json.WriteEndObject();
        if (frame.ShutterSamples.Count != 0)
        {
            json.WriteStartObject("productShutterAccumulation");
            json.WriteString("filter", "box-stratified-subinterval-centers");
            json.WriteStartArray("sampleTimeCodes");
            foreach (RenderPreparedFrame sample in frame.ShutterSamples)
            {
                json.WriteNumberValue(sample.TimeCode);
            }
            json.WriteEndArray();
            json.WriteString("hdrAccumulation", "float-mean-of-binary16-samples-before-camera-exposure");
            json.WriteString("pngAccumulation", "rounded-mean-of-display-encoded-rgba8-samples");
            json.WriteEndObject();
        }
        else
        {
            json.WriteNull("productShutterAccumulation");
        }
        json.WriteStartArray("productOutputs");
        foreach (RenderProductOutputBinding output in plan.Outputs)
        {
            bool hdr = output.Plane == RenderProductPlane.HdrColor;
            json.WriteStartObject();
            json.WriteString("variablePath", output.Variable.Path);
            json.WriteString("sourceType", output.Variable.SourceType);
            json.WriteString("sourceName", output.Variable.SourceName);
            json.WriteString("dataType", output.Variable.DataType);
            json.WriteString("file", hdr ? result.HdrColorFileName : result.DepthFileName);
            json.WriteString("format", hdr ? HdrColorFormatName(result.HdrColorFormat) : "float32-little-endian");
            json.WriteNumber("bytes", hdr ? result.HdrColorBytes : result.DepthBytes);
            json.WriteString("sha256", hdr ? result.HdrColorSha256 : result.DepthSha256);
            json.WriteString("rowOrder", "top-down");
            json.WriteString("convention", hdr ? "renderer-working-composited-camera-exposed-before-display" :
                "normalized-device-depth-zero-to-one");
            json.WriteEndObject();
        }
        json.WriteEndArray();
    }
}
