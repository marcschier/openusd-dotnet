// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;
using OpenUsd.Render;

namespace OpenUsd.Rendering.Tests;

public sealed class RenderProductCameraNativeTests
{
    [Test]
    public async Task SharedPreparationSamplesBatchesAndRejectsStaleOrAmbiguousSelection()
    {
        RequireNativeExecution();
        string root = Directory.CreateTempSubdirectory("render-product-preparation-").FullName;
        string path = Path.Combine(root, "scene.usda");
        const string scene = """
            #usda 1.0
            (renderSettingsPrimPath = "/Settings")
            def Camera "Camera" {
                token projection = "orthographic"
                float horizontalAperture = 20
                float verticalAperture = 20
                float2 clippingRange = (1, 11)
                double3 xformOp:translate.timeSamples = { 0: (0, 0, 0), 32: (8, 0, 0) }
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            def RenderSettings "Settings" {
                rel camera = </Camera>
                rel products = </Product>
                int2 resolution = (4, 4)
                uniform token[] includedPurposes = ["default", "render"]
                uniform token[] materialBindingPurposes = ["full"]
            }
            def RenderProduct "Product" {
                token productName = "never-written.exr"
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
            await File.WriteAllTextAsync(path, scene);
            await using UsdStageScheduler scheduler = UsdStageScheduler.Open(path);
            ulong revision = await scheduler.InvokeAsync(static stage => stage.ChangeSerial);
            double[] times = [.. Enumerable.Range(0, 33).Select(static value => (double)value)];
            RenderProductJobPlan plan = await RenderProductJobPlan.PrepareAsync(
                scheduler, new StageIdentity("scene.usda"), times, RenderSettings.PresentationDefault,
                expectedStageRevision: revision);
            await Assert.That(plan.Frames.Count).IsEqualTo(33);
            await Assert.That(plan.Frames[0].TimeCode).IsEqualTo(0d);
            await Assert.That(plan.Frames[^1].TimeCode).IsEqualTo(32d);
            await Assert.That(plan.Frames[^1].Camera.View.M41).IsEqualTo(-8f);
            await Assert.That(plan.Frames.All(frame => ReferenceEquals(frame.Request, plan.Request))).IsTrue();
            await Assert.That(plan.SourceStageRevision).IsEqualTo(revision);
            await Assert.That(await scheduler.InvokeAsync(static stage => stage.ChangeSerial)).IsEqualTo(revision);
            await Assert.That(async () => await RenderProductJobPlan.PrepareAsync(
                scheduler, new StageIdentity("scene.usda"), [0], RenderSettings.Default,
                expectedStageRevision: revision + 1)).Throws<InvalidOperationException>();
            await Assert.That(async () => await RenderProductJobPlan.PrepareAsync(
                scheduler, new StageIdentity("scene.usda"), [0], RenderSettings.Default,
                productPath: "/OtherProduct")).Throws<ArgumentException>();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.That(async () => await RenderProductJobPlan.PrepareAsync(
                scheduler, new StageIdentity("scene.usda"), [0], RenderSettings.Default,
                cancellationToken: cancellation.Token)).Throws<OperationCanceledException>();
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(scene);
            await Assert.That(Directory.GetFiles(root)).IsEquivalentTo([path]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ExecutionPreparationSamplesEveryCameraInputWithoutChangingTheSource()
    {
        RequireNativeExecution();
        string root = Directory.CreateTempSubdirectory("render-product-camera-").FullName;
        string path = Path.Combine(root, "scene.usda");
        const string scene = """
            #usda 1.0
            (renderSettingsPrimPath = "/Settings")
            def Camera "Camera" {
                token projection = "orthographic"
                float horizontalAperture = 20
                float verticalAperture = 20
                float2 clippingRange = (1, 11)
                double shutter:open = -0.25
                double shutter:close = 0.25
                float exposure.timeSamples = { 0: 0, 2: 2 }
                float exposure:iso = 200
                float exposure:time = 0.25
                float exposure:fStop = 1
                float exposure:responsivity = 0.5
                float fStop = 2
                float focusDistance = 10
            }
            def RenderSettings "Settings" {
                rel camera = </Camera>
                rel products = </Product>
                int2 resolution = (4, 4)
                bool disableMotionBlur = true
                bool disableDepthOfField = true
                uniform token[] includedPurposes = ["default", "render"]
                uniform token[] materialBindingPurposes = ["full"]
            }
            def RenderProduct "Product" {
                token productName = "never-written.exr"
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
            await File.WriteAllTextAsync(path, scene);
            await using UsdStageScheduler scheduler = UsdStageScheduler.Open(path);
            RenderProductJobPlan plan = await scheduler.InvokeAsync(static stage =>
            {
                ulong before = stage.ChangeSerial;
                var request = new RenderProductRequest(stage.GetRenderSpecification()!, 0);
                RenderPreparedFrame geometry = request.PrepareFrame(stage, 2);
                if (geometry.CameraSettings is not null)
                {
                    throw new InvalidDataException("The old geometry-only surface changed its contract.");
                }
                RenderPreparedFrame first = request.PrepareExecutionFrame(stage, 0);
                if (first.CameraSettings!.LinearExposureScale != 0.25)
                {
                    throw new InvalidDataException("Exposure did not use the requested first time sample.");
                }
                RenderPreparedFrame second = request.PrepareExecutionFrame(stage, 2);
                if (stage.ChangeSerial != before)
                {
                    throw new InvalidDataException("Execution preparation mutated the stage.");
                }
                return new RenderProductJobPlan(
                    new StageIdentity("scene.usda"), [second], RenderSettings.PresentationDefault, before);
            });
            RenderCameraFrameSettings camera = plan.Frames[0].CameraSettings!;
            await Assert.That(camera.ShutterOpen).IsEqualTo(-0.25);
            await Assert.That(camera.ShutterClose).IsEqualTo(0.25);
            await Assert.That(camera.Exposure).IsEqualTo(2d);
            await Assert.That(camera.ExposureIso).IsEqualTo(200d);
            await Assert.That(camera.ExposureTime).IsEqualTo(0.25);
            await Assert.That(camera.ExposureFStop).IsEqualTo(1d);
            await Assert.That(camera.ExposureResponsivity).IsEqualTo(0.5);
            await Assert.That(camera.LinearExposureScale).IsEqualTo(1d);
            await Assert.That(plan.Frames[0].SampledCamera.FStop).IsEqualTo(2d);
            await Assert.That(plan.IncludedPurposes).IsEqualTo(RenderPurpose.Default | RenderPurpose.Render);
            await Assert.That(plan.MaterialBindingPurpose).IsEqualTo("full");
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(scene);
            await Assert.That(Directory.GetFiles(root)).IsEquivalentTo([path]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ActiveAccumulationSamplesTheMovingCameraAtStratifiedSubintervalCenters()
    {
        RequireNativeExecution();
        string root = Directory.CreateTempSubdirectory("render-product-shutter-").FullName;
        string path = Path.Combine(root, "scene.usda");
        const string scene = """
            #usda 1.0
            (renderSettingsPrimPath = "/Settings")
            def Camera "Camera" {
                token projection = "orthographic"
                float horizontalAperture = 20
                float verticalAperture = 20
                float2 clippingRange = (1, 11)
                double shutter:open = -0.5
                double shutter:close = 0.5
                double3 xformOp:translate.timeSamples = { 0: (0, 0, 0), 8: (16, 0, 0) }
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            def RenderSettings "Settings" {
                rel camera = </Camera>
                rel products = </Product>
                int2 resolution = (4, 4)
                bool disableMotionBlur = false
                uniform token[] includedPurposes = ["default", "render"]
                uniform token[] materialBindingPurposes = ["full"]
            }
            def RenderProduct "Product" {
                token productName = "never-written.exr"
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
            await File.WriteAllTextAsync(path, scene);
            await using UsdStageScheduler scheduler = UsdStageScheduler.Open(path);
            RenderProductJobPlan plan = await RenderProductJobPlan.PrepareAsync(
                scheduler, new StageIdentity("scene.usda"), [4], RenderSettings.PresentationDefault);
            RenderPreparedFrame frame = plan.Frames[0];
            await Assert.That(frame.AccumulationSamples.Count).IsEqualTo(8);
            for (int index = 0; index < 8; index++)
            {
                double time = 3.5 + ((index + 0.5) / 8);
                RenderPreparedFrame sample = frame.AccumulationSamples[index];
                await Assert.That(sample.TimeCode).IsEqualTo(time);
                await Assert.That(sample.Camera.View.M41).IsEqualTo((float)(-2 * time));
                await Assert.That(sample.OutputDimensions).IsEqualTo(frame.OutputDimensions);
            }
            await Assert.That(frame.Camera.View.M41).IsEqualTo(-8f);
            await Assert.That(plan.AccumulationStates(0)!.Count).IsEqualTo(8);
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(scene);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ActiveDepthOfFieldSamplesTheLensDiskWithDecorrelatedShutterTimes()
    {
        RequireNativeExecution();
        string root = Directory.CreateTempSubdirectory("render-product-lens-").FullName;
        string path = Path.Combine(root, "scene.usda");
        const string scene = """
            #usda 1.0
            (renderSettingsPrimPath = "/Settings")
            def Camera "Camera" {
                token projection = "perspective"
                float focalLength = 50
                float horizontalAperture = 20
                float verticalAperture = 20
                float2 clippingRange = (1, 100)
                float fStop = 2
                float focusDistance = 10
                double shutter:open = -0.5
                double shutter:close = 0.5
                double3 xformOp:translate.timeSamples = { 0: (0, 0, 0), 8: (16, 0, 0) }
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            def RenderSettings "Settings" {
                rel camera = </Camera>
                rel products = </Product>
                int2 resolution = (4, 4)
                bool disableMotionBlur = false
                bool disableDepthOfField = false
                uniform token[] includedPurposes = ["default", "render"]
                uniform token[] materialBindingPurposes = ["full"]
            }
            def RenderProduct "Product" {
                token productName = "never-written.exr"
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
            await File.WriteAllTextAsync(path, scene);
            await using UsdStageScheduler scheduler = UsdStageScheduler.Open(path);
            RenderProductJobPlan plan = await RenderProductJobPlan.PrepareAsync(
                scheduler, new StageIdentity("scene.usda"), [4], RenderSettings.PresentationDefault);
            RenderPreparedFrame frame = plan.Frames[0];
            int count = RenderProductRequest.DepthOfFieldSampleCount;
            await Assert.That(frame.AccumulationSamples.Count).IsEqualTo(count);
            const float radius = 50 * 0.1f / (2 * 2);
            for (int index = 0; index < count; index++)
            {
                double time = 3.5 + (((index * 5 % count) + 0.5) / count);
                RenderPreparedFrame sample = frame.AccumulationSamples[index];
                await Assert.That(sample.TimeCode).IsEqualTo(time);
                System.Numerics.Vector2 offset = sample.LensOffset!.Value;
                await Assert.That(offset.Length()).IsLessThanOrEqualTo(radius * 1.0001f);
                await Assert.That(sample.Camera.View.M41).IsEqualTo((float)(-2 * time) - offset.X).Within(1e-4f);
                await Assert.That(sample.Camera.View.M42).IsEqualTo(-offset.Y).Within(1e-4f);
            }
            await Assert.That(plan.AccumulationStates(0)!.Count).IsEqualTo(count);
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(scene);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task AuthoredClippingPlanesReachEveryCameraSampleAndFollowTheLens()
    {
        RequireNativeExecution();
        string root = Directory.CreateTempSubdirectory("render-product-planes-").FullName;
        string path = Path.Combine(root, "scene.usda");
        const string scene = """
            #usda 1.0
            (renderSettingsPrimPath = "/Settings")
            def Camera "Camera" {
                token projection = "perspective"
                float focalLength = 50
                float horizontalAperture = 20
                float verticalAperture = 20
                float2 clippingRange = (1, 100)
                float4[] clippingPlanes.timeSamples = {
                    0: [(1, 0, 0, 2), (0, 0.6, -0.8, 5)],
                    8: [(1, 0, 0, 4), (0, 0.6, -0.8, 5)],
                }
                float fStop = 2
                float focusDistance = 10
            }
            def RenderSettings "Settings" {
                rel camera = </Camera>
                rel products = </Product>
                int2 resolution = (4, 4)
                bool disableDepthOfField = false
                uniform token[] includedPurposes = ["default", "render"]
                uniform token[] materialBindingPurposes = ["full"]
            }
            def RenderProduct "Product" {
                token productName = "never-written.exr"
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
            await File.WriteAllTextAsync(path, scene);
            await using UsdStageScheduler scheduler = UsdStageScheduler.Open(path);
            RenderProductJobPlan plan = await RenderProductJobPlan.PrepareAsync(
                scheduler, new StageIdentity("scene.usda"), [4], RenderSettings.PresentationDefault);
            RenderPreparedFrame frame = plan.Frames[0];
            System.Numerics.Vector4[] expected = [new(1, 0, 0, 3), new(0, 0.6f, -0.8f, 5)];
            await Assert.That(frame.Camera.ClipPlanes).IsEquivalentTo(expected);
            await Assert.That(frame.AccumulationSamples.Count).IsEqualTo(RenderProductRequest.DepthOfFieldSampleCount);
            foreach (RenderPreparedFrame sample in frame.AccumulationSamples)
            {
                System.Numerics.Vector2 offset = sample.LensOffset!.Value;
                IReadOnlyList<System.Numerics.Vector4> planes = sample.Camera.ClipPlanes;
                await Assert.That(planes.Count).IsEqualTo(2);
                await Assert.That(planes[0].W).IsEqualTo(3 + offset.X).Within(1e-5f);
                await Assert.That(planes[1].W).IsEqualTo(5 + (0.6f * offset.Y)).Within(1e-5f);
                await Assert.That(planes[1].Z).IsEqualTo(-0.8f);
            }
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(scene);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments("[(1, 0, 0, 0), (1, 0, 0, 0), (1, 0, 0, 0), (1, 0, 0, 0), (1, 0, 0, 0), (1, 0, 0, 0), (1, 0, 0, 0), (1, 0, 0, 0), (1, 0, 0, 0)]", "at most 8")]
    [Arguments("[(0, 0, 0, 1)]", "nonzero normal")]
    [Arguments("[(1, 0, 0, inf)]", "finite plane")]
    public async Task UnrepresentableClippingPlanesAreRefused(string planes, string reason)
    {
        RequireNativeExecution();
        string root = Directory.CreateTempSubdirectory("render-product-bad-planes-").FullName;
        string path = Path.Combine(root, "scene.usda");
        string scene = $$"""
            #usda 1.0
            (renderSettingsPrimPath = "/Settings")
            def Camera "Camera" {
                float horizontalAperture = 20
                float verticalAperture = 20
                float4[] clippingPlanes = {{planes}}
            }
            def RenderSettings "Settings" {
                rel camera = </Camera>
                rel products = </Product>
                int2 resolution = (4, 4)
                uniform token[] includedPurposes = ["default", "render"]
                uniform token[] materialBindingPurposes = ["full"]
            }
            def RenderProduct "Product" {
                token productName = "never-written.exr"
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
            await File.WriteAllTextAsync(path, scene);
            await using UsdStageScheduler scheduler = UsdStageScheduler.Open(path);
            NotSupportedException? refusal = await Assert.That(async () => await RenderProductJobPlan.PrepareAsync(
                scheduler, new StageIdentity("scene.usda"), [0], RenderSettings.PresentationDefault))
                .Throws<NotSupportedException>();
            await Assert.That(refusal!.Message).Contains(reason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void RequireNativeExecution()
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_RENDER_PRODUCT_EXECUTION_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_RENDER_PRODUCT_EXECUTION_REQUIRED=1 with the matching native runtime.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
        if (!string.IsNullOrWhiteSpace(plugins))
        {
            _ = OpenUsdNativeRuntime.RegisterPlugins(plugins);
        }
    }
}
