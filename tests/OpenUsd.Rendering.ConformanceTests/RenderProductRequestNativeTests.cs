// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;
using OpenUsd.Interop;
using OpenUsd.Render;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class RenderProductRequestNativeTests
{
    [Test]
    public async Task AuthoredProductsObserveReviewCameraOverridesWithoutChangingSource()
    {
        string root = Directory.CreateTempSubdirectory("openusd-review-render-request-").FullName;
        string path = Path.Combine(root, "scene.usda");
        const string scene = """
            #usda 1.0
            (
                renderSettingsPrimPath = "/Settings"
                metersPerUnit = 1
                upAxis = "Y"
            )
            def Camera "Camera"
            {
                token projection = "orthographic"
                float horizontalAperture = 20
                float verticalAperture = 20
                float2 clippingRange = (1, 11)
            }
            def RenderSettings "Settings"
            {
                rel camera = </Camera>
                rel products = </Product>
                int2 resolution = (8, 4)
                token aspectRatioConformPolicy = "adjustPixelAspectRatio"
            }
            def RenderProduct "Product"
            {
                token productName = "never-created.exr"
                rel orderedVars = </Color>
            }
            def RenderVar "Color"
            {
                token dataType = "color3f"
                string sourceName = "color"
                token sourceType = "raw"
            }
            """;
        try
        {
            File.WriteAllText(path, scene);
            string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
            if (!string.IsNullOrWhiteSpace(plugins))
            {
                _ = OpenUsdNativeRuntime.RegisterPlugins(plugins);
            }
            RenderPreparedFrame frame;
            await using (UsdStageScheduler scheduler = UsdStageScheduler.Open(path))
            {
                frame = await scheduler.InvokeAsync(static stage =>
                {
                    using UsdLayer review = stage.GetUserReviewLayer();
                    var address = new UsdLayerEditAddress(
                        "/Camera.horizontalAperture", UsdLayerEditField.Default);
                    UsdLayerAuthoredSnapshot before = review.CaptureAuthored([address]);
                    UsdLayerEditResult result = review.CompareAndApply(
                        before, [UsdLayerEdit.Set(address, UsdLayerEditValue.FromFloat(40), "float")]);
                    if (result.Outcome != UsdLayerEditOutcome.Applied)
                    {
                        throw new InvalidOperationException($"Review camera edit failed: {result.Outcome}.");
                    }
                    UsdRenderSpecification specification = stage.GetRenderSpecification()
                        ?? throw new InvalidOperationException("The authored default settings were not found.");
                    return new RenderProductRequest(specification, 0).PrepareFrame(stage, 0);
                });
            }

            await Assert.That(frame.Request.Product.ApertureSize.X).IsEqualTo(40f);
            await Assert.That(frame.SampledCamera.HorizontalAperture).IsEqualTo(40d);
            await Assert.That(frame.OutputDimensions).IsEqualTo(new ViewportDimensions(8, 4));
            await Assert.That(frame.Camera.Projection.M11).IsEqualTo(0.5f);
            await Assert.That(frame.Camera.Projection.M22).IsEqualTo(1f);
            await Assert.That(frame.PixelAspectRatio).IsEqualTo(1d);
            await Assert.That(File.ReadAllText(path)).IsEqualTo(scene);
            await Assert.That(File.Exists(Path.Combine(root, "never-created.exr"))).IsFalse();
        }
        catch (DllNotFoundException exception)
        {
            Skip.Test($"A matched native data runtime is required: {exception.Message}");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.", exception);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AuthoredProductsUseTheFrameTimeCameraAndLeaveTheSourceUntouched(bool crate)
    {
        string root = Directory.CreateTempSubdirectory("openusd-render-request-").FullName;
        string path = Path.Combine(root, "scene.usda");
        string binaryPath = Path.Combine(root, "scene.usdc");
        const string scene = """
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
                    float horizontalAperture.timeSamples = { 0: 20, 24: 40 }
                    float verticalAperture = 20
                    float2 clippingRange = (1, 11)
                    double3 xformOp:translate.timeSamples = { 0: (0, 0, 0), 24: (5, 0, 0) }
                    uniform token[] xformOpOrder = ["xformOp:translate"]
                }
            }
            def Scope "Render"
            {
                def RenderSettings "Settings"
                {
                    rel camera = </World/Camera>
                    int2 resolution = (4, 4)
                    float4 dataWindowNDC = (0.375, 0.125, 0.625, 0.625)
                    rel products = </Render/Product>
                }
                def RenderProduct "Product"
                {
                    token productName = "never-created.exr"
                    rel orderedVars = </Render/Color>
                }
                def RenderVar "Color"
                {
                    token dataType = "color3f"
                    string sourceName = "color"
                    token sourceType = "raw"
                }
            }
            """;
        try
        {
            File.WriteAllText(path, scene);
            string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
            if (!string.IsNullOrWhiteSpace(plugins))
            {
                _ = OpenUsdNativeRuntime.RegisterPlugins(plugins);
            }
            if (crate)
            {
                using UsdStage source = UsdStage.Open(path);
                using UsdLayer layer = source.GetRootLayer();
                layer.Export(binaryPath);
            }
            string stagePath = crate ? binaryPath : path;
            byte[] original = File.ReadAllBytes(stagePath);
            RenderPreparedFrame frame;
            await using (UsdStageScheduler scheduler = UsdStageScheduler.Open(stagePath))
            {
                frame = await scheduler.InvokeAsync(static stage =>
                {
                    UsdRenderSpecification specification = stage.GetRenderSpecification()
                        ?? throw new InvalidOperationException("The authored default settings were not found.");
                    var request = new RenderProductRequest(specification, 0);
                    return request.PrepareFrame(stage, 24);
                });
            }

            await Assert.That(frame.TimeCode).IsEqualTo(24d);
            await Assert.That(frame.Request.Product.Width).IsEqualTo(4);
            await Assert.That(frame.OutputDimensions).IsEqualTo(new ViewportDimensions(1, 2));
            await Assert.That(frame.Camera.View.M41).IsEqualTo(-5f);
            await Assert.That(frame.Camera.Projection.M11).IsEqualTo(2f);
            await Assert.That(frame.Camera.Projection.M22).IsEqualTo(1f);
            await Assert.That(frame.SampledCamera.HorizontalAperture).IsEqualTo(40d);
            await Assert.That(frame.Request.Product.ApertureSize.X).IsEqualTo(20f);
            await Assert.That(File.ReadAllText(path)).IsEqualTo(scene);
            await Assert.That(File.ReadAllBytes(stagePath).SequenceEqual(original)).IsTrue();
            await Assert.That(File.Exists(Path.Combine(root, "never-created.exr"))).IsFalse();
        }
        catch (DllNotFoundException exception)
        {
            Skip.Test($"A matched native data runtime is required: {exception.Message}");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.", exception);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
