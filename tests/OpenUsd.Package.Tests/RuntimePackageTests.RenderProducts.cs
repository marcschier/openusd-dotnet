// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Package.Tests;

public sealed partial class RuntimePackageTests
{
    private static string CreateRenderProductConsumerProgram() =>
        """"
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using OpenUsd;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;

internal static class PackageAuthoredProductExecution
{
    internal static void Run(ISilkGraphicsDevice device, string pluginPath)
    {
        var depthFirst = RunOrder(device, pluginPath, depthFirst: true);
        var colorFirst = RunOrder(device, pluginPath, depthFirst: false);
        AssertMatches(depthFirst.Full, colorFirst.Full, depthFirst: false);
        AssertMatches(depthFirst.Preview, colorFirst.Preview, depthFirst: false);
        Console.WriteLine("AUTHORED_PRODUCT_VARIABLE_ORDERS=true");
    }

    private static (RenderDiskJobResult Full, RenderDiskJobResult Preview) RunOrder(
        ISilkGraphicsDevice device, string pluginPath, bool depthFirst)
    {
        string root = Path.Combine(AppContext.BaseDirectory,
            depthFirst ? "authored-product-depth-first" : "authored-product-color-first");
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "authored-product.usda");
        string scene = depthFirst ? Scene : Scene.Replace(
            "rel orderedVars = [</Depth>, </Color>]",
            "rel orderedVars = [</Color>, </Depth>]", StringComparison.Ordinal);
        File.WriteAllText(path, scene);
        UsdStageScheduler scheduler = UsdStageScheduler.Open(path);
        try
        {
            using UsdStageRenderSource source = scheduler.AcquireRenderSourceAsync().AsTask()
                .GetAwaiter().GetResult();
            using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(pluginPath, source);
            using var capturer = new SilkFrameCapturer(device);
            ulong revision = GetRevision(scheduler);
            var adapter = new FrameSource(session, capturer, scheduler, revision);
            RenderProductJobPlan fullPlan = Prepare("/Full");
            RenderDiskJobResult full = RenderDiskJob.Execute(fullPlan.CreateJob(
                Path.Combine(root, "authored-product-full")), adapter);
            AssertBindings(full, depthFirst);
            byte[] first = ReadHdr(full, 0);
            AssertPixel(first, 4, 4, "005400000000003C");
            AssertPixel(first, 12, 4, "005400000000003C");
            AssertPixel(first, 4, 12, "000000000000003C");
            AssertPixel(first, 12, 12, "000000000000003C");
            byte[] second = ReadHdr(full, 1);
            AssertPixel(second, 4, 4, "000000000000003C");
            AssertPixel(second, 12, 4, "005400000000003C");
            if (full.Frames.Count != 2 || full.Frames[1].State.Time.TimeCode != 2 ||
                fullPlan.Outputs.Count != 2 ||
                fullPlan.Outputs[depthFirst ? 0 : 1].Plane != RenderProductPlane.DeviceDepth ||
                fullPlan.Outputs[depthFirst ? 1 : 0].Plane != RenderProductPlane.HdrColor ||
                full.Frames[0].DepthBytes != 16 * 16 * 4)
            {
                throw new InvalidOperationException("Product time, variable order or depth output changed.");
            }
            using (JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(
                Path.Combine(full.OutputDirectory, "manifest.json"))))
            {
                JsonElement product = manifest.RootElement.GetProperty("authoredProduct");
                JsonElement frame = manifest.RootElement.GetProperty("frames")[0];
                if (product.GetProperty("productNameUsedAsWriteAuthority").GetBoolean() ||
                    product.GetProperty("materialBindingPurpose").GetString() != "full" ||
                    product.GetProperty("callerSourceStageRevision").GetUInt64() != revision ||
                    frame.GetProperty("productRaster").GetProperty("fullWidth").GetInt32() != 16)
                {
                    throw new InvalidOperationException("The authored product manifest lost its requested semantics.");
                }
            }
            Console.WriteLine("AUTHORED_PRODUCT_JOB=true");

            RenderDiskJobResult preview = RenderDiskJob.Execute(Prepare("/Preview").CreateJob(
                Path.Combine(root, "authored-product-preview")), adapter);
            AssertBindings(preview, depthFirst);
            byte[] previewPixels = ReadHdr(preview, 0);
            AssertPixel(previewPixels, 4, 4, "000000540000003C");
            AssertPixel(previewPixels, 12, 4, "000000000000003C");
            AssertPixel(previewPixels, 4, 12, "000000540000003C");
            AssertPixel(previewPixels, 12, 12, "000000000000003C");
            Console.WriteLine("AUTHORED_PRODUCT_FILTER_PIXELS=true");

            SilkFrameCaptureResult legacy = capturer.CaptureWithHdrColor(session, 16, 16,
                RenderSettings.PresentationDefault, timeCode: 0, camera: fullPlan.Frames[0].Camera);
            byte[] legacyPixels = (legacy.HdrColor ??
                throw new InvalidOperationException("Legacy capture omitted HDR.")).Rgba16Float.ToArray();
            AssertPixel(legacyPixels, 4, 4, "000000000054003C");
            AssertPixel(legacyPixels, 12, 4, "000000000054003C");
            AssertPixel(legacyPixels, 4, 12, "000000000054003C");
            AssertPixel(legacyPixels, 12, 12, "000000000000003C");
            RenderDiskJobResult restored = RenderDiskJob.Execute(fullPlan.CreateJob(
                Path.Combine(root, "authored-product-restored")), adapter);
            AssertMatches(full, restored, depthFirst);
            Console.WriteLine("AUTHORED_PRODUCT_LEGACY_RESET=true");
            if (OperatingSystem.IsWindows())
            {
                RenderDiskJobResult exrFull = RenderDiskJob.Execute(fullPlan.CreateJob(
                    Path.Combine(root, "authored-product-full-exr"),
                    RenderHdrColorFormat.Exr), adapter);
                AssertMatches(full, exrFull, depthFirst, RenderHdrColorFormat.Exr);
                RenderDiskJobResult exrPreview = RenderDiskJob.Execute(Prepare("/Preview").CreateJob(
                    Path.Combine(root, "authored-product-preview-exr"),
                    RenderHdrColorFormat.Exr), adapter);
                AssertMatches(preview, exrPreview, depthFirst, RenderHdrColorFormat.Exr);
                SilkFrameCaptureResult legacyAgain = capturer.CaptureWithHdrColor(session, 16, 16,
                    RenderSettings.PresentationDefault, timeCode: 0, camera: fullPlan.Frames[0].Camera);
                if (!(legacyAgain.HdrColor ??
                    throw new InvalidOperationException("Legacy reset omitted HDR."))
                    .Rgba16Float.Span.SequenceEqual(legacyPixels))
                {
                    throw new InvalidOperationException("EXR preview did not restore the legacy capture.");
                }
                RenderDiskJobResult exrRestored = RenderDiskJob.Execute(fullPlan.CreateJob(
                    Path.Combine(root, "authored-product-restored-exr"),
                    RenderHdrColorFormat.Exr), adapter);
                AssertMatches(full, exrRestored, depthFirst, RenderHdrColorFormat.Exr);
                Console.WriteLine("AUTHORED_PRODUCT_EXR=true");
            }
            if (GetRevision(scheduler) != revision || File.ReadAllText(path) != scene ||
                File.Exists(Path.Combine(root, "authored-name-must-not-write.exr")) ||
                File.Exists(Path.Combine(AppContext.BaseDirectory, "authored-name-must-not-write.exr")))
            {
                throw new InvalidOperationException("Rendering changed the source or used the authored output name.");
            }
            Console.WriteLine("AUTHORED_PRODUCT_SOURCE_UNCHANGED=true");
            return (full, preview);

            RenderProductJobPlan Prepare(string settings) => RenderProductJobPlan.PrepareAsync(
                scheduler, new StageIdentity(path), new double[] { 0, 2 },
                RenderSettings.PresentationDefault, settingsPath: settings,
                expectedStageRevision: revision).AsTask().GetAwaiter().GetResult();
        }
        finally
        {
            scheduler.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static ulong GetRevision(UsdStageScheduler scheduler) =>
        scheduler.InvokeAsync(static stage => stage.ChangeSerial).AsTask().GetAwaiter().GetResult();

    private static byte[] ReadHdr(RenderDiskJobResult result, int index)
    {
        RenderDiskFrameResult frame = result.Frames[index];
        byte[] bytes = File.ReadAllBytes(Path.Combine(result.OutputDirectory,
            frame.HdrColorFileName ?? throw new InvalidOperationException("Product HDR file is absent.")));
        if (bytes.LongLength != frame.HdrColorBytes || !string.Equals(
            Convert.ToHexString(SHA256.HashData(bytes)), frame.HdrColorSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Product HDR bytes or hash changed after publication.");
        }
        byte[] pixels = frame.HdrColorFormat switch
        {
            RenderHdrColorFormat.RawRgba16Float => bytes,
            RenderHdrColorFormat.Exr => ExrScanlineOracle.Read(bytes, 16, 16),
            _ => throw new InvalidOperationException("Product HDR container is unsupported.")
        };
        if (pixels.Length != 16 * 16 * 8)
        {
            throw new InvalidOperationException("Product HDR pixel extent changed.");
        }
        return pixels;
    }

    private static void AssertMatches(
        RenderDiskJobResult raw, RenderDiskJobResult actual, bool depthFirst,
        RenderHdrColorFormat expectedFormat = RenderHdrColorFormat.RawRgba16Float)
    {
        if (actual.Frames.Count != 2 || actual.Frames.Count != raw.Frames.Count)
        {
            throw new InvalidOperationException("Product frame count changed.");
        }
        AssertBindings(actual, depthFirst);
        for (int index = 0; index < actual.Frames.Count; index++)
        {
            RenderDiskFrameResult frame = actual.Frames[index];
            if (frame.HdrColorFormat != expectedFormat ||
                frame.State.Time.TimeCode != raw.Frames[index].State.Time.TimeCode ||
                !ReadHdr(actual, index).AsSpan().SequenceEqual(ReadHdr(raw, index)))
            {
                throw new InvalidOperationException("Product time or decoded half bits changed.");
            }
            byte[] depth = File.ReadAllBytes(Path.Combine(actual.OutputDirectory,
                frame.DepthFileName ?? throw new InvalidOperationException("Product depth is absent.")));
            byte[] rawDepth = File.ReadAllBytes(Path.Combine(raw.OutputDirectory,
                raw.Frames[index].DepthFileName ??
                    throw new InvalidOperationException("Raw product depth is absent.")));
            if (!depth.AsSpan().SequenceEqual(rawDepth))
            {
                throw new InvalidOperationException("Product replay changed the raw depth plane.");
            }
        }
    }

    private static void AssertBindings(RenderDiskJobResult result, bool depthFirst)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(result.OutputDirectory, "manifest.json")));
        JsonElement frames = manifest.RootElement.GetProperty("frames");
        if (frames.GetArrayLength() != 2 || result.Frames.Count != 2)
        {
            throw new InvalidOperationException("Product manifest frame count changed.");
        }
        for (int index = 0; index < result.Frames.Count; index++)
        {
            RenderDiskFrameResult frame = result.Frames[index];
            if (frame.State.Time.TimeCode != index * 2)
            {
                throw new InvalidOperationException("Product frame time changed.");
            }
            _ = ReadHdr(result, index);
            byte[] depth = File.ReadAllBytes(Path.Combine(result.OutputDirectory,
                frame.DepthFileName ?? throw new InvalidOperationException("Product depth is absent.")));
            if (depth.Length != 16 * 16 * 4 || depth.LongLength != frame.DepthBytes ||
                !string.Equals(
                    Convert.ToHexString(SHA256.HashData(depth)), frame.DepthSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Product depth extent or accounting changed.");
            }
            JsonElement bindings = frames[index].GetProperty("productOutputs");
            if (bindings.GetArrayLength() != 2)
            {
                throw new InvalidOperationException("Product variable count changed.");
            }
            for (int bindingIndex = 0; bindingIndex < 2; bindingIndex++)
            {
                bool isDepth = depthFirst == (bindingIndex == 0);
                JsonElement binding = bindings[bindingIndex];
                JsonElement plane = frames[index].GetProperty(isDepth ? "deviceDepth" : "hdrColor");
                string format = isDepth ? "float32-little-endian" :
                    frame.HdrColorFormat == RenderHdrColorFormat.Exr
                        ? "openexr-rgba16float" : "rgba16float-little-endian";
                if (binding.GetProperty("variablePath").GetString() != (isDepth ? "/Depth" : "/Color") ||
                    binding.GetProperty("sourceName").GetString() != (isDepth ? "depth" : "color") ||
                    binding.GetProperty("sourceType").GetString() != "raw" ||
                    binding.GetProperty("dataType").GetString() != (isDepth ? "float" : "half4") ||
                    binding.GetProperty("file").GetString() !=
                        (isDepth ? frame.DepthFileName : frame.HdrColorFileName) ||
                    binding.GetProperty("format").GetString() != format ||
                    plane.GetProperty("format").GetString() != format ||
                    plane.GetProperty("file").GetString() != binding.GetProperty("file").GetString() ||
                    plane.GetProperty("bytes").GetInt64() != binding.GetProperty("bytes").GetInt64() ||
                    plane.GetProperty("sha256").GetString() != binding.GetProperty("sha256").GetString() ||
                    plane.GetProperty("rowOrder").GetString() != binding.GetProperty("rowOrder").GetString() ||
                    plane.GetProperty("convention").GetString() != binding.GetProperty("convention").GetString() ||
                    binding.GetProperty("bytes").GetInt64() !=
                        (isDepth ? frame.DepthBytes : frame.HdrColorBytes) ||
                    binding.GetProperty("sha256").GetString() !=
                        (isDepth ? frame.DepthSha256 : frame.HdrColorSha256) ||
                    binding.GetProperty("rowOrder").GetString() != "top-down" ||
                    binding.GetProperty("convention").GetString() != (isDepth
                        ? "normalized-device-depth-zero-to-one"
                        : "renderer-working-composited-before-exposure-and-display"))
                {
                    throw new InvalidOperationException("Product bindings lost their stored-plane identity.");
                }
            }
        }
    }

    private static void AssertPixel(byte[] pixels, int x, int y, string expected)
    {
        string actual = Convert.ToHexString(pixels.AsSpan((y * 16 + x) * 8, 8));
        if (actual != expected)
        {
            throw new InvalidOperationException($"Product pixel ({x},{y}) is {actual}, expected {expected}.");
        }
    }

    private sealed class FrameSource(
        OpenUsdSilkSession session, SilkFrameCapturer capturer,
        UsdStageScheduler scheduler, ulong revision) : IRenderProductFrameSource
    {
        private SilkSceneIngestionOptions? _options;

        public void ValidateProduct(RenderProductJobPlan plan, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (plan.SourceStageRevision != revision)
            {
                throw new InvalidOperationException("The adapter was offered a different source revision.");
            }
            _options = new SilkSceneIngestionOptions(plan.IncludedPurposes, plan.MaterialBindingPurpose);
        }

        public RenderJobImage Render(StageRenderState state, CancellationToken cancellationToken)
        {
            if (GetRevision(scheduler) != revision)
            {
                throw new InvalidOperationException("The product source changed before capture.");
            }
            SilkFrameCaptureResult frame = capturer.CaptureWithHdrColor(
                session, state.Viewport.Width, state.Viewport.Height, state.RenderSettings,
                _options ?? throw new InvalidOperationException("The product was not admitted."),
                new SilkHdrColorCaptureOptions(includeDeviceDepth: true),
                state.Time.TimeCode, state.Camera, cancellationToken);
            if (GetRevision(scheduler) != revision)
            {
                throw new InvalidOperationException("The product source changed during capture.");
            }
            SilkHdrColorCaptureResult hdr = frame.HdrColor ??
                throw new InvalidOperationException("The requested HDR plane is absent.");
            SilkDepthCaptureResult depth = frame.Depth ??
                throw new InvalidOperationException("The requested depth plane is absent.");
            return new RenderJobImage(frame.Width, frame.Height, frame.Rgba, Rgba8RowOrder.TopDown)
            {
                HdrColor = new RenderJobHdrColor(hdr.Width, hdr.Height, hdr.Rgba16Float),
                DeviceDepth = new RenderJobDeviceDepth(depth.Width, depth.Height, depth.Values)
            };
        }
    }

    private const string Scene = """
        #usda 1.0
        (renderSettingsPrimPath = "/Full")
        def Camera "Camera" {
            token projection = "orthographic"
            float horizontalAperture = 40
            float verticalAperture = 40
            float2 clippingRange = (1, 11)
        }
        def RenderSettings "Full" {
            rel camera = </Camera>
            rel products = </Product>
            int2 resolution = (16, 16)
            uniform token[] includedPurposes = ["default", "render"]
            uniform token[] materialBindingPurposes = ["full"]
        }
        def RenderSettings "Preview" {
            rel camera = </Camera>
            rel products = </Product>
            int2 resolution = (16, 16)
            uniform token[] includedPurposes = ["default", "proxy"]
            uniform token[] materialBindingPurposes = ["preview"]
        }
        def RenderProduct "Product" {
            token productName = "authored-name-must-not-write.exr"
            rel orderedVars = [</Depth>, </Color>]
        }
        def RenderVar "Depth" {
            token dataType = "float"
            string sourceName = "depth"
            token sourceType = "raw"
        }
        def RenderVar "Color" {
            token dataType = "half4"
            string sourceName = "color"
            token sourceType = "raw"
        }
        def Scope "Looks" {
            def Material "Full" {
                token outputs:surface.connect = </Looks/Full/Shader.outputs:surface>
                def Shader "Shader" {
                    uniform token info:id = "UsdPreviewSurface"
                    color3f inputs:diffuseColor = (0, 0, 0)
                    color3f inputs:emissiveColor = (64, 0, 0)
                    int inputs:useSpecularWorkflow = 1
                    color3f inputs:specularColor = (0, 0, 0)
                    token outputs:surface
                }
            }
            def Material "Preview" {
                token outputs:surface.connect = </Looks/Preview/Shader.outputs:surface>
                def Shader "Shader" {
                    uniform token info:id = "UsdPreviewSurface"
                    color3f inputs:diffuseColor = (0, 0, 0)
                    color3f inputs:emissiveColor = (0, 64, 0)
                    int inputs:useSpecularWorkflow = 1
                    color3f inputs:specularColor = (0, 0, 0)
                    token outputs:surface
                }
            }
            def Material "Legacy" {
                token outputs:surface.connect = </Looks/Legacy/Shader.outputs:surface>
                def Shader "Shader" {
                    uniform token info:id = "UsdPreviewSurface"
                    color3f inputs:diffuseColor = (0, 0, 0)
                    color3f inputs:emissiveColor = (0, 0, 64)
                    int inputs:useSpecularWorkflow = 1
                    color3f inputs:specularColor = (0, 0, 0)
                    token outputs:surface
                }
            }
        }
        def Xform "World" (prepend apiSchemas = ["MaterialBindingAPI"]) {
            rel material:binding = </Looks/Legacy>
            rel material:binding:full = </Looks/Full>
            rel material:binding:preview = </Looks/Preview>
            def Cube "Default" {
                double size = 0.75
                double3 xformOp:translate.timeSamples = { 0: (-1, 1, -4), 2: (1, 1, -4) }
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            def Cube "Render" {
                uniform token purpose = "render"
                double size = 0.75
                double3 xformOp:translate = (1, 1, -4)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            def Xform "ProxyParent" {
                uniform token purpose = "proxy"
                def Cube "InheritedProxy" {
                    double size = 0.75
                    double3 xformOp:translate = (-1, -1, -4)
                    uniform token[] xformOpOrder = ["xformOp:translate"]
                }
            }
            def Cube "Guide" {
                uniform token purpose = "guide"
                double size = 0.75
                double3 xformOp:translate = (1, -1, -4)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
        }
        """;
}
"""";
}
