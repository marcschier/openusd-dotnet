// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;
using OpenUsd.Geom;
using OpenUsd.Interop;
using OpenUsd.Render;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class RenderProductRequestNativeTests
{
    [Test]
    [Arguments(false, UsdGeomCameraProjection.Orthographic, false)]
    [Arguments(true, UsdGeomCameraProjection.Orthographic, false)]
    [Arguments(false, UsdGeomCameraProjection.Perspective, false)]
    [Arguments(true, UsdGeomCameraProjection.Perspective, false)]
    [Arguments(false, UsdGeomCameraProjection.Orthographic, true)]
    [Arguments(true, UsdGeomCameraProjection.Orthographic, true)]
    [Arguments(false, UsdGeomCameraProjection.Perspective, true)]
    [Arguments(true, UsdGeomCameraProjection.Perspective, true)]
    public Task ReviewCameraChangesRenderedCoverageAndRestoresWithoutSourceWrites(
        bool timeSample, UsdGeomCameraProjection projection, bool translate) =>
        AssertReviewCameraChangeAsync(timeSample, projection, translate);

    [Test]
    [Arguments(false, UsdGeomCameraProjection.Orthographic, 2f)]
    [Arguments(true, UsdGeomCameraProjection.Orthographic, 2f)]
    [Arguments(false, UsdGeomCameraProjection.Perspective, 2f)]
    [Arguments(true, UsdGeomCameraProjection.Perspective, 2f)]
    [Arguments(false, UsdGeomCameraProjection.Orthographic, 4f)]
    [Arguments(true, UsdGeomCameraProjection.Orthographic, 4f)]
    [Arguments(false, UsdGeomCameraProjection.Perspective, 4f)]
    [Arguments(true, UsdGeomCameraProjection.Perspective, 4f)]
    [Arguments(false, UsdGeomCameraProjection.Orthographic, 6f)]
    [Arguments(true, UsdGeomCameraProjection.Orthographic, 6f)]
    [Arguments(false, UsdGeomCameraProjection.Perspective, 6f)]
    [Arguments(true, UsdGeomCameraProjection.Perspective, 6f)]
    public Task ReviewCameraNearClipChangesRenderedCoverageAndRestoresWithoutSourceWrites(
        bool timeSample, UsdGeomCameraProjection projection, float nearClip) =>
        AssertReviewCameraChangeAsync(timeSample, projection, translate: false, nearClip);

    private static async Task AssertReviewCameraChangeAsync(
        bool timeSample, UsdGeomCameraProjection projection, bool translate, float? nearClip = null)
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
                float focalLength = 40
                float2 clippingRange = (1, 11)
                double3 xformOp:translate = (0,0,0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
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
            bool perspective = projection == UsdGeomCameraProjection.Perspective;
            File.WriteAllText(path, perspective
                ? scene.Replace("token projection = \"orthographic\"", "token projection = \"perspective\"",
                    StringComparison.Ordinal)
                : scene);
            byte[] originalSource = File.ReadAllBytes(path);
            await using UsdStageScheduler scheduler = UsdStageScheduler.Open(path);
            using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
            using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(plugins, source);
            using var capturer = new SilkFrameCapturer(device);
            string propertyPath = nearClip.HasValue ? "/Camera.clippingRange" :
                translate ? "/Camera.xformOp:translate" :
                perspective ? "/Camera.focalLength" : "/Camera.horizontalAperture";
            var address = new UsdLayerEditAddress(propertyPath,
                timeSample ? UsdLayerEditField.TimeSample : UsdLayerEditField.Default, timeSample ? 24 : 0);
            float editedValue = perspective ? 20 : 40;
            UsdLayerEditValue authoredValue = nearClip is { } near
                ? UsdLayerEditValue.FromVec2f(new UsdVec2f(near, 12))
                : translate
                ? UsdLayerEditValue.FromVec3d(new UsdVec3d(0.25, 0, 0))
                : UsdLayerEditValue.FromFloat(editedValue);
            float expectedDepth = perspective ? 0.825f : 0.3f;
            RenderPreparedFrame original = await prepareAsync();
            SilkFrameCaptureResult baseline = capture(original);
            await Assert.That(original.SampledCamera.Projection).IsEqualTo(projection);
            await assertCoverageAsync(baseline, 16, 48, 16, 48, expectedDepth);
            (UsdLayerAuthoredSnapshot Before, UsdLayerAuthoredSnapshot After) edit =
                await scheduler.InvokeAsync(stage =>
                {
                    using UsdLayer review = stage.GetUserReviewLayer();
                    UsdLayerAuthoredSnapshot before = review.CaptureAuthored([address]);
                    UsdLayerEditResult result = review.CompareAndApply(before,
                        [UsdLayerEdit.Set(address, authoredValue,
                            nearClip.HasValue ? "float2" : translate ? "double3" : "float")]);
                    if (result.Outcome != UsdLayerEditOutcome.Applied || result.AfterSnapshot is null)
                    {
                        throw new InvalidOperationException($"Review camera edit failed: {result.Outcome}.");
                    }
                    return (before, result.AfterSnapshot);
                });
            await Assert.That(edit.Before.Opinions[0].PropertyKind).IsEqualTo(UsdLayerPropertyKind.Absent);
            await Assert.That(edit.After.Opinions[0].Address).IsEqualTo(address);
            RenderPreparedFrame changed = await prepareAsync();
            SilkFrameCaptureResult edited = capture(changed);
            if (nearClip is { } clippingNear)
            {
                await Assert.That(edit.After.Opinions[0].Value.AsVec2f()).IsEqualTo(new UsdVec2f(clippingNear, 12));
                await Assert.That(changed.SampledCamera.ClippingNear).IsEqualTo((double)clippingNear);
                await Assert.That(changed.SampledCamera.ClippingFar).IsEqualTo(12d);
                await Assert.That(changed.Camera.View).IsEqualTo(original.Camera.View);
                await Assert.That(changed.Camera.Projection.M11).IsEqualTo(original.Camera.Projection.M11);
                await Assert.That(changed.Camera.Projection.M22).IsEqualTo(original.Camera.Projection.M22);
                float clippedDepth = clippingNear == 2 ? perspective ? 0.6f : 0.2f : 0f;
                await assertCoverageAsync(edited, clippingNear <= 4 ? 16 : 0, clippingNear <= 4 ? 48 : 0,
                    16, 48, clippedDepth);
                await Assert.That(edited.Depth!.Values.Span.SequenceEqual(baseline.Depth!.Values.Span)).IsFalse()
                    .Because("an unchanged depth plane must not satisfy the edited clipping-range contract");
            }
            else if (translate)
            {
                await Assert.That(edit.After.Opinions[0].Value.AsVec3d()).IsEqualTo(new UsdVec3d(0.25, 0, 0));
                await Assert.That(changed.Camera.View.M41).IsEqualTo(-0.25f);
                await Assert.That(changed.Camera.Projection).IsEqualTo(original.Camera.Projection);
                await assertCoverageAsync(edited, 8, 40, 16, 48, expectedDepth);
            }
            else
            {
                await Assert.That(edit.After.Opinions[0].Value.AsFloat()).IsEqualTo(editedValue);
                await Assert.That(perspective
                        ? changed.SampledCamera.FocalLength : changed.SampledCamera.HorizontalAperture)
                    .IsEqualTo((double)editedValue);
                await Assert.That(changed.Camera.Projection.M11).IsEqualTo(perspective ? 2f : 0.5f);
                await Assert.That(changed.Camera.Projection.M22).IsEqualTo(perspective ? 2f : 0.5f);
                await assertCoverageAsync(edited, 24, 40, 24, 40, expectedDepth);
            }
            await Assert.That(changed.PixelAspectRatio).IsEqualTo(1d);
            if (!nearClip.HasValue)
            {
                await Assert.That(edited.Rgba.Span.SequenceEqual(baseline.Rgba.Span)).IsFalse()
                    .Because("an unchanged camera must not satisfy the edited camera's pixel contract");
            }
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
            await Assert.That(restoredFrame.Camera).IsEqualTo(original.Camera);
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

        static async Task assertCoverageAsync(
            SilkFrameCaptureResult frame, int minimumX, int maximumX, int minimumY, int maximumY, float depth)
        {
            await Assert.That(frame.Width).IsEqualTo(64);
            await Assert.That(frame.Height).IsEqualTo(64);
            byte[] foreground = [0x00, 0x3C, 0x00, 0x3C, 0x00, 0x3C, 0x00, 0x3C];
            byte[] background = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x3C];
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    bool covered = x >= minimumX && x < maximumX && y >= minimumY && y < maximumY;
                    byte[] expected = covered ? foreground : background;
                    await Assert.That(frame.HdrColor!.Rgba16Float.Span.Slice((y * 64 + x) * 8, 8)
                        .SequenceEqual(expected)).IsTrue().Because($"analytic camera coverage at ({x},{y})");
                    await Assert.That(frame.Depth!.Values.Span[y * 64 + x])
                        .IsEqualTo(covered ? depth : 1f).Within(0.00001f);
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
