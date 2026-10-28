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
        json.WriteNumber("accumulationSampleCount", frame.AccumulationSamples.Count);
        json.WriteEndObject();
        if (frame.AccumulationSamples.Count != 0)
        {
            json.WriteStartObject("productAccumulation");
            json.WriteString("filter", "box-stratified-subinterval-centers");
            json.WriteStartArray("sampleTimeCodes");
            foreach (RenderPreparedFrame sample in frame.AccumulationSamples)
            {
                json.WriteNumberValue(sample.TimeCode);
            }
            json.WriteEndArray();
            bool lens = frame.AccumulationSamples[0].LensOffset is not null;
            if (lens)
            {
                json.WriteString("lensModel", "thin-lens-concentric-disk-radius-focalLength-x0.1-over-2fStop");
                json.WriteStartArray("sampleLensOffsets");
                foreach (RenderPreparedFrame sample in frame.AccumulationSamples)
                {
                    json.WriteStartArray();
                    json.WriteNumberValue(sample.LensOffset!.Value.X);
                    json.WriteNumberValue(sample.LensOffset!.Value.Y);
                    json.WriteEndArray();
                }
                json.WriteEndArray();
            }
            else
            {
                json.WriteNull("lensModel");
                json.WriteNull("sampleLensOffsets");
            }
            json.WriteString("hdrAccumulation", "float-mean-of-binary16-samples-before-camera-exposure");
            json.WriteString("pngAccumulation", "rounded-mean-of-display-encoded-rgba8-samples");
            json.WriteEndObject();
        }
        else
        {
            json.WriteNull("productAccumulation");
        }
        json.WriteStartArray("productOutputs");
        foreach (RenderProductOutputBinding output in plan.Outputs)
        {
            (string? file, string format, long bytes, string? sha256, string convention) = output.Plane switch
            {
                RenderProductPlane.HdrColor => (result.HdrColorFileName, HdrColorFormatName(result.HdrColorFormat),
                    result.HdrColorBytes, result.HdrColorSha256,
                    "renderer-working-composited-camera-exposed-before-display"),
                RenderProductPlane.DeviceDepth => (result.DepthFileName, "float32-little-endian",
                    result.DepthBytes, result.DepthSha256, "normalized-device-depth-zero-to-one"),
                RenderProductPlane.PrimId => (result.PrimIdFileName, "int32-little-endian",
                    result.PrimIdBytes, result.PrimIdSha256, "visible-prim-identity-index"),
                RenderProductPlane.InstanceId => (result.InstanceIdFileName, "int32-little-endian",
                    result.InstanceIdBytes, result.InstanceIdSha256, InstanceIdConvention),
                _ => throw new InvalidDataException("A product output names an unknown plane.")
            };
            json.WriteStartObject();
            json.WriteString("variablePath", output.Variable.Path);
            json.WriteString("sourceType", output.Variable.SourceType);
            json.WriteString("sourceName", output.Variable.SourceName);
            json.WriteString("dataType", output.Variable.DataType);
            json.WriteString("file", file);
            json.WriteString("format", format);
            json.WriteNumber("bytes", bytes);
            json.WriteString("sha256", sha256);
            json.WriteString("rowOrder", "top-down");
            json.WriteString("convention", convention);
            if (output.Plane == RenderProductPlane.PrimId)
            {
                json.WriteNumber("background", -1);
                json.WriteString("tableFile", result.PrimIdTableFileName);
                json.WriteNumber("tableBytes", result.PrimIdTableBytes);
                json.WriteString("tableSha256", result.PrimIdTableSha256);
            }
            else if (output.Plane == RenderProductPlane.InstanceId)
            {
                json.WriteNumber("background", -1);
            }
            json.WriteEndObject();
        }
        json.WriteEndArray();
    }
}
