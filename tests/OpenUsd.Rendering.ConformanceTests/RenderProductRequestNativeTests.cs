// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;
using OpenUsd.Interop;
using OpenUsd.Render;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class RenderProductRequestNativeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReviewCameraApertureChangesRenderedCoverageAndRestoresWithoutSourceWrites(bool timeSample)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(SilkGraphicsBackend.D3D12);
        string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
        if (string.IsNullOrWhiteSpace(plugins))
        {
            Skip.Test("Set OPENUSD_TEST_PLUGIN_PATH to the complete matched native runtime plugin directory.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        _ = OpenUsdNativeRuntime.RegisterPlugins(plugins);
        string root = Directory.CreateTempSubdirectory("openusd-review-camera-pixels-").FullName;
        string path = Path.Combine(root, "scene.usda");
        const string scene = """
            #usda 1.0
            (renderSettingsPrimPath = "/Settings")
            def Camera "Camera" {
                token projection = "orthographic"
                float horizontalAperture = 20
                float verticalAperture = 20
                float2 clippingRange = (1, 11)
            }
            def Mesh "Marker" (prepend apiSchemas = ["MaterialBindingAPI"]) {
                uniform token subdivisionScheme = "none"
                uniform bool doubleSided = 1
                point3f[] points = [(-0.5,-0.5,-4),(0.5,-0.5,-4),(0.5,0.5,-4),(-0.5,0.5,-4)]
                int[] faceVertexCounts = [4]
                int[] faceVertexIndices = [0,1,2,3]
                rel material:binding = </Material>
            }
            def Material "Material" {
                token outputs:surface.connect = </Material/Surface.outputs:surface>
                def Shader "Surface" {
                    uniform token info:id = "UsdPreviewSurface"
                    color3f inputs:diffuseColor = (0,0,0)
                    color3f inputs:emissiveColor = (1,1,1)
                    int inputs:useSpecularWorkflow = 1
                    color3f inputs:specularColor = (0,0,0)
                    token outputs:surface
                }
            }
            def RenderSettings "Settings" {
                rel camera = </Camera>
                rel products = </Product>
                int2 resolution = (64,64)
                token aspectRatioConformPolicy = "adjustApertureHeight"
            }
            def RenderProduct "Product" {
                token productName = "not-written.exr"
                rel orderedVars = </Color>
            }
            def RenderVar "Color" {
                token dataType = "half4"
                string sourceName = "color"
                token sourceType = "raw"
            }
            """;
        try
        {
            File.WriteAllText(path, scene);
            byte[] originalSource = File.ReadAllBytes(path);
            await using UsdStageScheduler scheduler = UsdStageScheduler.Open(path);
            using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
            using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(plugins, source);
            using var capturer = new SilkFrameCapturer(device);
            var address = new UsdLayerEditAddress("/Camera.horizontalAperture",
                timeSample ? UsdLayerEditField.TimeSample : UsdLayerEditField.Default, timeSample ? 24 : 0);
            RenderPreparedFrame original = await prepareAsync();
            SilkFrameCaptureResult baseline = capture(original);
            await assertCoverageAsync(baseline, 16, 48);
            (UsdLayerAuthoredSnapshot Before, UsdLayerAuthoredSnapshot After) edit =
                await scheduler.InvokeAsync(stage =>
                {
                    using UsdLayer review = stage.GetUserReviewLayer();
                    UsdLayerAuthoredSnapshot before = review.CaptureAuthored([address]);
                    UsdLayerEditResult result = review.CompareAndApply(before,
                        [UsdLayerEdit.Set(address, UsdLayerEditValue.FromFloat(40), "float")]);
                    if (result.Outcome != UsdLayerEditOutcome.Applied || result.AfterSnapshot is null)
                    {
                        throw new InvalidOperationException($"Review aperture edit failed: {result.Outcome}.");
                    }
                    return (before, result.AfterSnapshot);
                });
            await Assert.That(edit.Before.Opinions[0].PropertyKind).IsEqualTo(UsdLayerPropertyKind.Absent);
            await Assert.That(edit.After.Opinions[0].Address).IsEqualTo(address);
            await Assert.That(edit.After.Opinions[0].Value.AsFloat()).IsEqualTo(40f);
            RenderPreparedFrame changed = await prepareAsync();
            SilkFrameCaptureResult edited = capture(changed);
            await Assert.That(changed.SampledCamera.HorizontalAperture).IsEqualTo(40d);
            await Assert.That(changed.Camera.Projection.M11).IsEqualTo(0.5f);
            await Assert.That(changed.Camera.Projection.M22).IsEqualTo(0.5f);
            await Assert.That(changed.PixelAspectRatio).IsEqualTo(1d);
            await assertCoverageAsync(edited, 24, 40);
            await Assert.That(edited.Rgba.Span.SequenceEqual(baseline.Rgba.Span)).IsFalse()
                .Because("an unchanged camera must not satisfy the edited aperture's pixel contract");
            UsdLayerEditResult restored = await scheduler.InvokeAsync(stage =>
            {
                using UsdLayer review = stage.GetUserReviewLayer();
                return review.CompareAndRestore(edit.After, edit.Before);
            });
            await Assert.That(restored.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(restored.AfterSnapshot!.Opinions[0].PropertyKind)
                .IsEqualTo(UsdLayerPropertyKind.Absent);
            await Assert.That(restored.AfterSnapshot.Opinions[0].Value.Kind).IsEqualTo(UsdLayerEditValueKind.Absent);
            RenderPreparedFrame restoredFrame = await prepareAsync();
            SilkFrameCaptureResult replay = capture(restoredFrame);
            await Assert.That(restoredFrame.SampledCamera.HorizontalAperture).IsEqualTo(20d);
            await Assert.That(replay.Rgba.Span.SequenceEqual(baseline.Rgba.Span)).IsTrue();
            await Assert.That(replay.HdrColor!.Rgba16Float.Span.SequenceEqual(baseline.HdrColor!.Rgba16Float.Span))
                .IsTrue();
            await Assert.That(replay.Depth!.Values.Span.SequenceEqual(baseline.Depth!.Values.Span)).IsTrue();
            await Assert.That(File.ReadAllBytes(path).AsSpan().SequenceEqual(originalSource)).IsTrue();
            await Assert.That(File.Exists(Path.Combine(root, "not-written.exr"))).IsFalse();
            await Assert.That(File.Exists(Path.Combine(AppContext.BaseDirectory, "not-written.exr"))).IsFalse();

            async Task<RenderPreparedFrame> prepareAsync() => await scheduler.InvokeAsync(stage =>
            {
                UsdRenderSpecification specification = stage.GetRenderSpecification() ??
                    throw new InvalidOperationException("The camera fixture has no render settings.");
                return new RenderProductRequest(specification, 0).PrepareFrame(stage, address.TimeCode);
            });

            SilkFrameCaptureResult capture(RenderPreparedFrame frame) => capturer.CaptureWithHdrColor(
                session, frame.OutputDimensions.Width, frame.OutputDimensions.Height,
                RenderSettings.Default, new SilkHdrColorCaptureOptions(includeDeviceDepth: true),
                frame.TimeCode, frame.Camera);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        static async Task assertCoverageAsync(SilkFrameCaptureResult frame, int minimumX, int maximumX)
        {
            await Assert.That(frame.Width).IsEqualTo(64);
            await Assert.That(frame.Height).IsEqualTo(64);
            byte[] foreground = [0x00, 0x3C, 0x00, 0x3C, 0x00, 0x3C, 0x00, 0x3C];
            byte[] background = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x3C];
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    bool covered = x >= minimumX && x < maximumX && y >= minimumX && y < maximumX;
                    byte[] expected = covered ? foreground : background;
                    await Assert.That(frame.HdrColor!.Rgba16Float.Span.Slice((y * 64 + x) * 8, 8)
                        .SequenceEqual(expected)).IsTrue().Because($"analytic aperture coverage at ({x},{y})");
                    await Assert.That(frame.Depth!.Values.Span[y * 64 + x])
                        .IsEqualTo(covered ? 0.3f : 1f).Within(0.00001f);
                }
            }
        }
    }

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
