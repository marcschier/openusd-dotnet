// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Render;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

internal static class RenderProductRasterConformance
{
    internal static async Task CroppedProductMatchesFullRaster(
        ISilkGraphicsDevice device, string pluginPath, bool crate)
    {
        string root = Directory.CreateTempSubdirectory("openusd-product-raster-").FullName;
        string path = Path.Combine(root, "scene.usda");
        try
        {
            File.WriteAllText(path, """
                #usda 1.0
                (
                    defaultPrim = "World"
                    renderSettingsPrimPath = "/Render/Settings"
                    metersPerUnit = 1
                    upAxis = "Y"
                )
                def Xform "World"
                {
                    def Camera "Camera"
                    {
                        token projection = "orthographic"
                        float horizontalAperture = 20
                        float verticalAperture = 20
                        float2 clippingRange = (1, 11)
                    }
                    def Mesh "Marker"
                    {
                        uniform bool doubleSided = 1
                        uniform token subdivisionScheme = "none"
                        point3f[] points = [(-0.9, -0.8, -5), (0.85, -0.4, -5), (0.2, 0.95, -5)]
                        int[] faceVertexCounts = [3]
                        int[] faceVertexIndices = [0, 1, 2]
                        color3f[] primvars:displayColor = [(0.15, 0.7, 0.4)]
                        uniform token primvars:displayColor:interpolation = "constant"
                    }
                }
                def Scope "Render"
                {
                    def RenderSettings "Settings"
                    {
                        rel camera = </World/Camera>
                        int2 resolution = (96, 96)
                        float4 dataWindowNDC = (0.125, 0.25, 0.75, 0.625)
                        rel products = </Render/Product>
                    }
                    def RenderProduct "Product"
                    {
                        token productName = "not-written.exr"
                        rel orderedVars = </Render/Color>
                    }
                    def RenderVar "Color"
                    {
                        token dataType = "color3f"
                        string sourceName = "color"
                        token sourceType = "raw"
                    }
                }
                """);
            if (crate)
            {
                string binaryPath = Path.Combine(root, "scene.usdc");
                using UsdStage textStage = UsdStage.Open(path);
                using UsdLayer layer = textStage.GetRootLayer();
                layer.Export(binaryPath);
                path = binaryPath;
            }
            byte[] original = File.ReadAllBytes(path);
            await using UsdStageScheduler scheduler = UsdStageScheduler.Open(path);
            (RenderPreparedFrame Full, RenderPreparedFrame Crop) frames = await scheduler.InvokeAsync(static stage =>
            {
                UsdRenderSpecification specification = stage.GetRenderSpecification()
                    ?? throw new InvalidOperationException("The fixture has no default render settings.");
                var full = new RenderProductRequest(specification, 0, new RenderProductOverrides(
                    dataWindowNdc: new UsdVec4f(0, 0, 1, 1)));
                var crop = new RenderProductRequest(specification, 0);
                return (full.PrepareFrame(stage, 0), crop.PrepareFrame(stage, 0));
            });
            await Assert.That(frames.Crop.OutputDimensions).IsEqualTo(new ViewportDimensions(60, 36));
            await Assert.That(frames.Crop.DataWindowMinX).IsEqualTo(12);
            await Assert.That(frames.Crop.DataWindowMinY).IsEqualTo(24);
            using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
            using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(pluginPath, source);
            using var capturer = new SilkFrameCapturer(device);
            SilkFrameCaptureResult fullImage = capturer.Capture(
                session, 96, 96, RenderSettings.Default, frames.Full.TimeCode, frames.Full.Camera);
            SilkFrameCaptureResult croppedImage = capturer.Capture(
                session, 60, 36, RenderSettings.Default, frames.Crop.TimeCode, frames.Crop.Camera);
            SilkFrameCaptureResult resizedInsteadOfCropped = capturer.Capture(
                session, 60, 36, RenderSettings.Default, frames.Full.TimeCode, frames.Full.Camera);

            byte[] expected = new byte[60 * 36 * 4];
            for (int row = 0; row < 36; row++)
            {
                fullImage.Rgba.Span.Slice(((36 + row) * 96 + 12) * 4, 60 * 4)
                    .CopyTo(expected.AsSpan(row * 60 * 4));
            }
            var tolerance = ParityTolerance.Geometry with
            {
                EdgeDilationRadius = 0,
                MinimumCoverageIntersectionOverUnion = 1,
                MaximumCoverageDifferenceFraction = 0,
                CompareColor = true,
                MaximumChannelDifference = 1,
                MaximumMeanChannelDifference = 0.05
            };
            var reference = new ParityImage(60, 36, expected);
            ParityComparisonResult comparison = ParityImageComparer.Compare(
                reference, new ParityImage(60, 36, croppedImage.Rgba), 0x000000FF, tolerance);
            ParityComparisonResult wrong = ParityImageComparer.Compare(
                reference, new ParityImage(60, 36, resizedInsteadOfCropped.Rgba), 0x000000FF, tolerance);
            await Assert.That(comparison.ReferenceCoveragePixels).IsGreaterThan(100);
            await Assert.That(comparison.Passed).IsTrue().Because(comparison.Diagnostics);
            await Assert.That(wrong.Passed).IsFalse()
                .Because("changing target dimensions without changing the product frustum must not satisfy the crop");
            await Assert.That(File.Exists(Path.Combine(root, "not-written.exr"))).IsFalse();
            await Assert.That(File.ReadAllBytes(path).SequenceEqual(original)).IsTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    internal static async Task OverriddenRasterMatchesAuthoredRaster(ISilkGraphicsDevice device, string pluginPath)
    {
        string root = Directory.CreateTempSubdirectory("openusd-product-override-").FullName;
        string path = Path.Combine(root, "scene.usda");
        try
        {
            File.WriteAllText(path, """
                #usda 1.0
                (
                    defaultPrim = "World"
                    renderSettingsPrimPath = "/Render/Settings"
                    metersPerUnit = 1
                    upAxis = "Y"
                )
                def Xform "World"
                {
                    def Camera "Camera"
                    {
                        token projection = "orthographic"
                        float horizontalAperture = 20
                        float verticalAperture = 20
                        float2 clippingRange = (1, 11)
                    }
                    def Camera "Wide"
                    {
                        token projection = "orthographic"
                        float horizontalAperture = 40
                        float verticalAperture = 40
                        float2 clippingRange = (1, 11)
                        double3 xformOp:translate = (0.3, 0.1, 0)
                        uniform token[] xformOpOrder = ["xformOp:translate"]
                    }
                    def Mesh "Marker"
                    {
                        uniform bool doubleSided = 1
                        uniform token subdivisionScheme = "none"
                        point3f[] points = [(-0.9, -0.8, -5), (0.85, -0.4, -5), (0.2, 0.95, -5)]
                        int[] faceVertexCounts = [3]
                        int[] faceVertexIndices = [0, 1, 2]
                        color3f[] primvars:displayColor = [(0.15, 0.7, 0.4)]
                        uniform token primvars:displayColor:interpolation = "constant"
                    }
                }
                def Scope "Render"
                {
                    def RenderSettings "Settings"
                    {
                        rel camera = </World/Camera>
                        int2 resolution = (64, 64)
                        float pixelAspectRatio = 2
                        uniform token aspectRatioConformPolicy = "expandAperture"
                        rel products = </Render/Product>
                    }
                    def RenderSettings "Authored"
                    {
                        rel camera = </World/Wide>
                        int2 resolution = (96, 48)
                        float pixelAspectRatio = 1
                        uniform token aspectRatioConformPolicy = "cropAperture"
                        rel products = </Render/Product>
                    }
                    def RenderProduct "Product"
                    {
                        token productName = "not-written.exr"
                        rel orderedVars = </Render/Color>
                    }
                    def RenderVar "Color"
                    {
                        token dataType = "color3f"
                        string sourceName = "color"
                        token sourceType = "raw"
                    }
                }
                """);
            byte[] original = File.ReadAllBytes(path);
            await using UsdStageScheduler scheduler = UsdStageScheduler.Open(path);
            (RenderPreparedFrame Overridden, RenderPreparedFrame Authored, RenderPreparedFrame Plain) frames =
                await scheduler.InvokeAsync(static stage =>
                {
                    UsdRenderSpecification baseline = stage.GetRenderSpecification()
                        ?? throw new InvalidOperationException("The fixture has no default render settings.");
                    UsdRenderSpecification authored = stage.GetRenderSpecification("/Render/Authored")
                        ?? throw new InvalidOperationException("The fixture has no authored override target.");
                    var overrides = new RenderProductOverrides(
                        "/World/Wide", new ViewportDimensions(96, 48), 1, "cropAperture");
                    return (
                        new RenderProductRequest(baseline, 0, overrides).PrepareFrame(stage, 0),
                        new RenderProductRequest(authored, 0).PrepareFrame(stage, 0),
                        new RenderProductRequest(baseline, 0).PrepareFrame(stage, 0));
                });
            await Assert.That(frames.Overridden.OutputDimensions).IsEqualTo(new ViewportDimensions(96, 48));
            await Assert.That(frames.Authored.OutputDimensions).IsEqualTo(new ViewportDimensions(96, 48));
            await Assert.That(frames.Plain.OutputDimensions).IsEqualTo(new ViewportDimensions(64, 64));
            using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
            using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(pluginPath, source);
            using var capturer = new SilkFrameCapturer(device);
            SilkFrameCaptureResult overridden = capturer.Capture(
                session, 96, 48, RenderSettings.Default, frames.Overridden.TimeCode, frames.Overridden.Camera);
            SilkFrameCaptureResult authoredImage = capturer.Capture(
                session, 96, 48, RenderSettings.Default, frames.Authored.TimeCode, frames.Authored.Camera);
            SilkFrameCaptureResult plain = capturer.Capture(
                session, 96, 48, RenderSettings.Default, frames.Plain.TimeCode, frames.Plain.Camera);

            await Assert.That(overridden.Rgba.Span.SequenceEqual(authoredImage.Rgba.Span)).IsTrue()
                .Because("overrides must produce the raster that authoring the same values would produce");
            await Assert.That(overridden.Rgba.Span.SequenceEqual(plain.Rgba.Span)).IsFalse()
                .Because("the override camera, aspect and conform policy must replace the authored values");
            int covered = 0;
            for (int pixel = 0; pixel < 96 * 48; pixel++)
            {
                covered += overridden.Rgba.Span[pixel * 4 + 1] > 64 ? 1 : 0;
            }
            await Assert.That(covered).IsGreaterThan(50);
            await Assert.That(File.Exists(Path.Combine(root, "not-written.exr"))).IsFalse();
            await Assert.That(File.ReadAllBytes(path).SequenceEqual(original)).IsTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
