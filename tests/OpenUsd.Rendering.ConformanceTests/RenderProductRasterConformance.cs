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
}
