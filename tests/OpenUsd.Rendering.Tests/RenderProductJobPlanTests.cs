// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Text.Json;
using OpenUsd.Geom;
using OpenUsd.Render;

namespace OpenUsd.Rendering.Tests;

public sealed class RenderProductJobPlanTests
{
    [Test]
    public async Task AdmittedProductPreservesVariableOrderSceneFiltersAndSampledGeometry()
    {
        RenderProductRequest request = Request(depthFirst: true);
        RenderPreparedFrame first = Frame(request, 1);
        RenderPreparedFrame second = Frame(request, 2);
        var input = new[] { first, second };
        var plan = new RenderProductJobPlan(new StageIdentity("scene.usda"), input,
            RenderSettings.PresentationDefault, sourceStageRevision: 42);
        input[0] = second;
        await Assert.That(plan.Frames[0]).IsSameReferenceAs(first);
        await Assert.That(plan.Frames is System.Collections.ICollection).IsFalse();
        await Assert.That(plan.Outputs is System.Collections.ICollection).IsFalse();
        await Assert.That(plan.Outputs.Select(static output => output.Plane).SequenceEqual(
            [RenderProductPlane.DeviceDepth, RenderProductPlane.HdrColor])).IsTrue();
        await Assert.That(plan.Outputs[1].Variable.DataType).IsEqualTo("half4");
        await Assert.That(plan.IncludedPurposes).IsEqualTo(RenderPurpose.Default | RenderPurpose.Render);
        await Assert.That(plan.MaterialBindingPurpose).IsEqualTo("full");
        await Assert.That(plan.SourceStageRevision).IsEqualTo(42UL);
        RenderDiskJobRequest job = plan.CreateJob(Path.Combine(Path.GetTempPath(), $"product-{Guid.NewGuid():N}"));
        await Assert.That(job.ProductPlan).IsSameReferenceAs(plan);
        await Assert.That(job.Frames[0].Camera).IsEqualTo(first.Camera);
        await Assert.That(job.Frames[1].Time.TimeCode).IsEqualTo(2d);
        await Assert.That(job.Frames[0].Display.Purposes).IsEqualTo(plan.IncludedPurposes);
        await Assert.That(job.Frames[0].RenderSettings).IsEqualTo(RenderSettings.PresentationDefault);
        await Assert.That(job.IncludeHdrColor && job.IncludeDeviceDepth).IsTrue();
    }

    [Test]
    [Arguments("binding-fallback", "full")]
    [Arguments("binding-preview-fallback", "preview")]
    public async Task TrailingAllPurposeFallbacksSelectTheSameNamedMaterialBindingPurpose(
        string variant, string expected)
    {
        RenderProductRequest request = Request(variant);
        var plan = new RenderProductJobPlan(new StageIdentity("scene.usda"), [Frame(request)],
            RenderSettings.Default);
        await Assert.That(plan.MaterialBindingPurpose).IsEqualTo(expected);
    }

    [Test]
    [Arguments("variable-type")]
    [Arguments("source-type")]
    [Arguments("source-name")]
    [Arguments("variable-setting")]
    [Arguments("product-setting")]
    [Arguments("settings-setting")]
    [Arguments("color-space")]
    [Arguments("binding-order")]
    [Arguments("binding-empty")]
    [Arguments("binding-all-purpose")]
    [Arguments("binding-fallback-first")]
    [Arguments("binding-named-after-fallback")]
    [Arguments("purpose")]
    [Arguments("outputs-empty")]
    [Arguments("duplicate-color")]
    public async Task UnimplementedSemanticsNeverBecomeApparentlyExecutableProducts(string failure)
    {
        RenderProductRequest request = Request(failure);
        await Assert.That(() => new RenderProductJobPlan(new StageIdentity("scene.usda"), [Frame(request)],
            RenderSettings.Default)).Throws<NotSupportedException>();
        await Assert.That(request.Product.Name).IsEqualTo("../not-authorized.exr");
    }

    [Test]
    [Arguments("shutter")]
    [Arguments("dof")]
    [Arguments("geometry-only")]
    public async Task CameraSemanticsCannotBeDroppedByProductAdmission(string failure)
    {
        RenderProductRequest request = Request(disableOptics: false);
        RenderCameraFrameSettings camera = failure switch
        {
            "shutter" => new(shutterOpen: -0.25, shutterClose: 0.25),
            _ => RenderCameraFrameSettings.Default
        };
        UsdGeomCameraState optics = Optics(failure == "dof" ? 2 : 0);
        RenderPreparedFrame frame = failure == "geometry-only"
            ? request.PrepareFrame(UsdMatrix4d.Identity, optics, 1)
            : request.PrepareFrame(UsdMatrix4d.Identity, optics, 1, camera);
        await Assert.That(() => new RenderProductJobPlan(new StageIdentity("scene.usda"), [frame],
            RenderSettings.Default)).Throws<NotSupportedException>();
    }

    [Test]
    public async Task ExplicitOpticsDisableFlagsPreserveButDoNotActivateTheirInputs()
    {
        RenderProductRequest request = Request();
        var camera = new RenderCameraFrameSettings(-0.25, 0.25);
        RenderPreparedFrame frame = request.PrepareFrame(UsdMatrix4d.Identity, Optics(2), 4, camera);
        var plan = new RenderProductJobPlan(new StageIdentity("scene.usda"), [frame], RenderSettings.Default);
        await Assert.That(plan.Frames[0].CameraSettings).IsSameReferenceAs(camera);
        await Assert.That(plan.Frames[0].SampledCamera.FStop).IsEqualTo(2d);
        await Assert.That(plan.Request.Product.DisableMotionBlur && plan.Request.Product.DisableDepthOfField).IsTrue();
    }

    [Test]
    public async Task CameraExposureUsesIndependentIsoTimeFStopAndResponsivity()
    {
        var settings = new RenderCameraFrameSettings(
            exposure: 3, exposureIso: 200, exposureTime: 0.25, exposureFStop: 2, exposureResponsivity: 0.5);
        await Assert.That(settings.LinearExposureScale).IsEqualTo(0.5d);
        await Assert.That(settings.ShutterOpen).IsEqualTo(0d);
        await Assert.That(settings.ShutterClose).IsEqualTo(0d);
        await Assert.That(() => new RenderCameraFrameSettings(exposureFStop: 0)).Throws<ArgumentException>();
        await Assert.That(() => new RenderCameraFrameSettings(shutterOpen: 1, shutterClose: 0))
            .Throws<ArgumentException>();
        await Assert.That(() => new RenderCameraFrameSettings(exposure: double.NaN)).Throws<ArgumentException>();
        await Assert.That(() => new RenderCameraFrameSettings(exposure: 1024)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(1023, 0, 0, 1, 1021)]
    [Arguments(1024, 0, 0, 1, 1022)]
    [Arguments(1025, 0, 0, 1, 1023)]
    [Arguments(-1075, 0, 1, 0, -1074)]
    [Arguments(0, 600, 600, 600, 0)]
    [Arguments(0, -600, -600, -600, 0)]
    [Arguments(1200, -200, 0, 0, 1000)]
    [Arguments(-1200, 200, 0, 0, -1000)]
    [Arguments(0, 1023, 0, 512, -1)]
    public async Task CameraExposurePreservesRepresentableScaleAcrossIntermediateExponentLimits(
        int exposure, int responsivityExponent, int timeExponent, int fStopExponent, int expectedExponent)
    {
        var settings = new RenderCameraFrameSettings(
            exposure: exposure,
            exposureResponsivity: Math.ScaleB(1d, responsivityExponent),
            exposureTime: Math.ScaleB(1d, timeExponent),
            exposureFStop: Math.ScaleB(1d, fStopExponent));
        await Assert.That(settings.LinearExposureScale).IsEqualTo(Math.ScaleB(1d, expectedExponent));
        await Assert.That(settings.Exposure).IsEqualTo((double)exposure);
        await Assert.That(settings.ExposureResponsivity).IsEqualTo(Math.ScaleB(1d, responsivityExponent));
        await Assert.That(settings.ExposureTime).IsEqualTo(Math.ScaleB(1d, timeExponent));
        await Assert.That(settings.ExposureFStop).IsEqualTo(Math.ScaleB(1d, fStopExponent));
    }

    [Test]
    public async Task CameraExposureStillRefusesAnUnrepresentableCombinedScale()
    {
        await Assert.That(() => new RenderCameraFrameSettings(exposure: 1026, exposureFStop: 2))
            .Throws<ArgumentException>();
        await Assert.That(() => new RenderCameraFrameSettings(exposure: double.MaxValue))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task CameraExposureRetainsSubnormalIsoBeforeCompensatingGain()
    {
        var settings = new RenderCameraFrameSettings(exposure: 100, exposureIso: double.Epsilon);
        await Assert.That(settings.LinearExposureScale).IsEqualTo(Math.ScaleB(1d, -974) / 100);
        await Assert.That(settings.ExposureIso).IsEqualTo(double.Epsilon);
    }

    [Test]
    [Arguments("iso")]
    [Arguments("time")]
    [Arguments("responsivity")]
    public async Task CameraExposureZeroFactorsRemainBlackAtAnExtremeFiniteGain(string zeroFactor)
    {
        var settings = new RenderCameraFrameSettings(
            exposure: double.MaxValue,
            exposureIso: zeroFactor == "iso" ? 0 : 100,
            exposureTime: zeroFactor == "time" ? 0 : 1,
            exposureResponsivity: zeroFactor == "responsivity" ? 0 : 1);
        await Assert.That(settings.LinearExposureScale).IsEqualTo(0d);
    }

    [Test]
    public async Task CameraExposureFiniteAttenuationBelowTheSmallestDoubleStillRoundsToBlack()
    {
        await Assert.That(new RenderCameraFrameSettings(exposure: -double.MaxValue).LinearExposureScale).IsEqualTo(0d);
    }

    [Test]
    public async Task DifferentProductRequestsAndUnboundedSampleCountsAreRefused()
    {
        RenderProductRequest first = Request();
        RenderProductRequest second = Request();
        await Assert.That(() => new RenderProductJobPlan(new StageIdentity("scene.usda"),
            [Frame(first), Frame(second)], RenderSettings.Default)).Throws<ArgumentException>();
        await Assert.That(() => new RenderProductJobPlan(new StageIdentity("scene.usda"), [],
            RenderSettings.Default)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new RenderProductJobPlan(new StageIdentity("scene.usda"),
            Enumerable.Repeat(Frame(first), 4097).ToArray(), RenderSettings.Default))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task ProductAdmissionRetainsTheCombinedRasterAndEncodedByteLimits()
    {
        var plan = new RenderProductJobPlan(
            new StageIdentity("scene.usda"), [Frame(Request())], RenderSettings.Default);
        string output = Path.Combine(Path.GetTempPath(), $"product-quota-{Guid.NewGuid():N}");
        await Assert.That(() => plan.CreateJob(output, limits: new RenderDiskJobLimits(maximumFrameBytes: 79)))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(plan.CreateJob(output, limits: new RenderDiskJobLimits(maximumFrameBytes: 80))
            .Limits.MaximumFrameBytes).IsEqualTo(80L);
        await Assert.That(Directory.Exists(output)).IsFalse();
    }

    [Test]
    public async Task LegacySourcesAndRefusedAdaptersCannotCreateAnyProductOutput()
    {
        string root = Directory.CreateTempSubdirectory("product-source-refusal-").FullName;
        try
        {
            var plan = new RenderProductJobPlan(
                new StageIdentity("scene.usda"), [Frame(Request())], RenderSettings.Default);
            RenderDiskJobRequest job = plan.CreateJob(Path.Combine(root, "output"));
            var legacy = new LegacySource();
            await Assert.That(() => RenderDiskJob.Execute(job, legacy)).Throws<NotSupportedException>();
            await Assert.That(legacy.Calls).IsEqualTo(0);
            var source = new ProductSource { Refuse = true };
            await Assert.That(() => RenderDiskJob.Execute(job, source)).Throws<NotSupportedException>();
            await Assert.That(source.Calls).IsEqualTo(0);
            await Assert.That(source.Validations).IsEqualTo(1);
            await Assert.That(Directory.GetFileSystemEntries(root)).IsEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task ProductManifestCarriesEveryRequestedVariableAndDoesNotUseItsAuthoredFilename(
        bool depthFirst, bool exr)
    {
        if (exr && Environment.GetEnvironmentVariable("OPENUSD_EXR_EXECUTION_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_EXR_EXECUTION_REQUIRED=1 with the matching Windows x64 native runtime.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        string root = Directory.CreateTempSubdirectory("product-manifest-").FullName;
        try
        {
            RenderProductRequest request = Request(depthFirst: depthFirst);
            var plan = new RenderProductJobPlan(new StageIdentity("scene.usda"),
                [Frame(request, 1), Frame(request, 2)], RenderSettings.PresentationDefault, 42);
            var source = new ProductSource
            {
                HdrBytes = Convert.FromHexString(
                    "0054008000B8003C00340040004800380030004C002C0000000000540000003C")
            };
            RenderDiskJobResult result = RenderDiskJob.Execute(plan.CreateJob(Path.Combine(root, "output"),
                exr ? RenderHdrColorFormat.Exr : RenderHdrColorFormat.RawRgba16Float), source);
            await Assert.That(source.Validations).IsEqualTo(1);
            await Assert.That(source.Calls).IsEqualTo(2);
            await Assert.That(source.Admitted).IsSameReferenceAs(plan);
            await Assert.That(File.Exists(Path.Combine(root, "not-authorized.exr"))).IsFalse();
            await Assert.That(Directory.GetFiles(result.OutputDirectory).Length).IsEqualTo(7);
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(result.OutputDirectory, "manifest.json")));
            JsonElement product = manifest.RootElement.GetProperty("authoredProduct");
            await Assert.That(product.GetProperty("requestedProductName").GetString())
                .IsEqualTo("../not-authorized.exr");
            await Assert.That(product.GetProperty("productNameUsedAsWriteAuthority").GetBoolean()).IsFalse();
            await Assert.That(product.GetProperty("materialBindingPurpose").GetString()).IsEqualTo("full");
            await Assert.That(product.GetProperty("callerSourceStageRevision").GetUInt64()).IsEqualTo(42UL);
            await Assert.That(product.GetProperty("pngRole").GetString())
                .IsEqualTo("display-companion-not-authored-render-variable");
            JsonElement frame = manifest.RootElement.GetProperty("frames")[0];
            JsonElement outputs = frame.GetProperty("productOutputs");
            await Assert.That(outputs.GetArrayLength()).IsEqualTo(2);
            foreach (JsonElement metadataFrame in manifest.RootElement.GetProperty("frames").EnumerateArray())
            {
                RenderDiskFrameResult stored = result.Frames[metadataFrame.GetProperty("index").GetInt32()];
                JsonElement bindings = metadataFrame.GetProperty("productOutputs");
                await Assert.That(bindings.GetArrayLength()).IsEqualTo(2);
                for (int index = 0; index < bindings.GetArrayLength(); index++)
                {
                    bool depth = depthFirst == (index == 0);
                    JsonElement binding = bindings[index];
                    JsonElement plane = metadataFrame.GetProperty(depth ? "deviceDepth" : "hdrColor");
                    string fileName = depth ? stored.DepthFileName! : stored.HdrColorFileName!;
                    byte[] published = await File.ReadAllBytesAsync(Path.Combine(result.OutputDirectory, fileName));
                    await Assert.That(binding.GetProperty("variablePath").GetString())
                        .IsEqualTo(depth ? "/Render/Depth" : "/Render/Color");
                    await Assert.That(binding.GetProperty("sourceName").GetString())
                        .IsEqualTo(depth ? "depth" : "color");
                    await Assert.That(binding.GetProperty("sourceType").GetString()).IsEqualTo("raw");
                    await Assert.That(binding.GetProperty("dataType").GetString()).IsEqualTo(depth ? "float" : "half4");
                    await Assert.That(binding.GetProperty("file").GetString()).IsEqualTo(fileName);
                    await Assert.That(binding.GetProperty("format").GetString())
                        .IsEqualTo(depth ? "float32-little-endian" :
                            exr ? "openexr-rgba16float" : "rgba16float-little-endian");
                    await Assert.That(binding.GetProperty("format").GetString())
                        .IsEqualTo(plane.GetProperty("format").GetString());
                    await Assert.That(binding.GetProperty("bytes").GetInt64()).IsEqualTo(published.LongLength);
                    if (!depth && exr)
                    {
                        await Assert.That(Convert.ToHexString(ExrScanlineOracle.Read(published, 2, 2)))
                            .IsEqualTo(Convert.ToHexString(source.HdrBytes!));
                    }
                    else
                    {
                        await Assert.That(published.LongLength).IsEqualTo(depth ? 16L : 32L);
                        if (!depth)
                        {
                            await Assert.That(Convert.ToHexString(published))
                                .IsEqualTo(Convert.ToHexString(source.HdrBytes!));
                        }
                    }
                    await Assert.That(binding.GetProperty("sha256").GetString()).IsEqualTo(
                        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(published))
                            .ToLowerInvariant());
                    await Assert.That(binding.GetProperty("rowOrder").GetString()).IsEqualTo("top-down");
                    await Assert.That(binding.GetProperty("convention").GetString()).IsEqualTo(depth
                        ? "normalized-device-depth-zero-to-one"
                        : "renderer-working-composited-camera-exposed-before-display");
                }
            }
            await Assert.That(frame.GetProperty("productCamera").GetProperty("linearExposureScale").GetDouble())
                .IsEqualTo(1d);
            await Assert.That(frame.GetProperty("productRaster").GetProperty("pixelAspectRatio").GetDouble())
                .IsEqualTo(1d);
            await Assert.That(result.TotalBytes).IsEqualTo(
                Directory.GetFiles(result.OutputDirectory).Sum(static file => new FileInfo(file).Length));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task CroppedRawProductsRetainTheirRasterMetadata()
    {
        string root = Directory.CreateTempSubdirectory("product-crop-").FullName;
        try
        {
            var request = new RenderProductRequest(Request().Specification, 0, new RenderProductOverrides(
                resolution: new ViewportDimensions(4, 4), dataWindowNdc: new UsdVec4f(0.25f, 0, 0.75f, 1)));
            var plan = new RenderProductJobPlan(
                new StageIdentity("scene.usda"), [Frame(request)], RenderSettings.Default);
            RenderDiskJobResult result = RenderDiskJob.Execute(
                plan.CreateJob(Path.Combine(root, "raw")), new ProductSource());
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(result.OutputDirectory, "manifest.json")));
            JsonElement raster = manifest.RootElement.GetProperty("frames")[0].GetProperty("productRaster");
            await Assert.That(raster.GetProperty("fullWidth").GetInt32()).IsEqualTo(4);
            await Assert.That(raster.GetProperty("dataWindowMinX").GetInt32()).IsEqualTo(1);
            await Assert.That(raster.GetProperty("dataWindowMinY").GetInt32()).IsEqualTo(0);
            await Assert.That(result.Frames[0].State.Viewport).IsEqualTo(new ViewportDimensions(2, 4));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(0.25f, 0f, 0.75f, 1f, 1d, 1, 0, 2, 4, 0x3f800000u)]
    [Arguments(-0.25f, 0f, 1.25f, 0.5f, 1d, -1, 2, 6, 2, 0x3f800000u)]
    [Arguments(0f, 0.5f, 1f, 1.25f, 2d, 0, -1, 4, 3, 0x40000000u)]
    public async Task ExrProductsStoreTheirDataWindowDisplayWindowAndPixelAspect(
        float minX, float minY, float maxX, float maxY, double aspect,
        int dataOriginX, int dataOriginY, int dataWidth, int dataHeight, uint aspectBits)
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_EXR_EXECUTION_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_EXR_EXECUTION_REQUIRED=1 with the matching Windows x64 native runtime.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        string root = Directory.CreateTempSubdirectory("product-exr-window-").FullName;
        try
        {
            var request = new RenderProductRequest(Request().Specification, 0, new RenderProductOverrides(
                resolution: new ViewportDimensions(4, 4), pixelAspectRatio: aspect,
                dataWindowNdc: new UsdVec4f(minX, minY, maxX, maxY)));
            var plan = new RenderProductJobPlan(
                new StageIdentity("scene.usda"), [Frame(request)], RenderSettings.Default);
            byte[] hdr = new byte[dataWidth * dataHeight * 8];
            for (int half = 0; half < hdr.Length / 2; half++)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                    hdr.AsSpan(half * 2, 2), (ushort)(0x3C00 + half));
            }
            RenderDiskJobResult result = RenderDiskJob.Execute(
                plan.CreateJob(Path.Combine(root, "exr"), RenderHdrColorFormat.Exr),
                new ProductSource { HdrBytes = hdr });
            await Assert.That(result.Frames[0].State.Viewport)
                .IsEqualTo(new ViewportDimensions(dataWidth, dataHeight));
            byte[] published = await File.ReadAllBytesAsync(
                Path.Combine(result.OutputDirectory, result.Frames[0].HdrColorFileName!));
            byte[] decoded = ExrScanlineOracle.Read(published, dataWidth, dataHeight,
                new ExrOracleWindows(dataOriginX, dataOriginY, 0, 0, 4, 4, aspectBits));
            await Assert.That(Convert.ToHexString(decoded)).IsEqualTo(Convert.ToHexString(hdr));
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(result.OutputDirectory, "manifest.json")));
            JsonElement raster = manifest.RootElement.GetProperty("frames")[0].GetProperty("productRaster");
            await Assert.That(raster.GetProperty("dataWindowMinX").GetInt32()).IsEqualTo(dataOriginX);
            await Assert.That(4 - raster.GetProperty("dataWindowMinY").GetInt32() - dataHeight)
                .IsEqualTo(dataOriginY);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(1e6)]
    [Arguments(1e-6)]
    public async Task ExrProductsRefuseAPixelAspectTheFileCannotRepresent(double aspect)
    {
        string root = Directory.CreateTempSubdirectory("product-exr-aspect-").FullName;
        try
        {
            var request = new RenderProductRequest(Request().Specification, 0, new RenderProductOverrides(
                resolution: new ViewportDimensions(4, 4), pixelAspectRatio: aspect));
            var plan = new RenderProductJobPlan(
                new StageIdentity("scene.usda"), [Frame(request)], RenderSettings.Default);
            await Assert.That(() => plan.CreateJob(Path.Combine(root, "exr"), RenderHdrColorFormat.Exr))
                .Throws<NotSupportedException>();
            await Assert.That(Directory.Exists(Path.Combine(root, "exr"))).IsFalse();
            await Assert.That(plan.CreateJob(Path.Combine(root, "raw"))).IsNotNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CameraExposureScalesProductHdrAndShiftsCompanionDisplayExposure(bool exr)
    {
        if (exr && Environment.GetEnvironmentVariable("OPENUSD_EXR_EXECUTION_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_EXR_EXECUTION_REQUIRED=1 with the matching Windows x64 native runtime.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        string root = Directory.CreateTempSubdirectory("product-exposure-").FullName;
        try
        {
            RenderProductRequest request = Request();
            RenderPreparedFrame exposed = request.PrepareFrame(
                UsdMatrix4d.Identity, Optics(), 1, new RenderCameraFrameSettings(exposure: -2));
            RenderPreparedFrame unit = Frame(request, 2);
            RenderSettings baseline = RenderSettings.PresentationDefault;
            var plan = new RenderProductJobPlan(new StageIdentity("scene.usda"), [exposed, unit], baseline);
            await Assert.That(plan.RenderSettings).IsEqualTo(baseline);
            RenderDiskJobRequest job = plan.CreateJob(
                Path.Combine(root, "out"), exr ? RenderHdrColorFormat.Exr : RenderHdrColorFormat.RawRgba16Float);
            await Assert.That(job.Frames[0].RenderSettings.Exposure).IsEqualTo(baseline.Exposure - 2);
            RenderSettings shifted = job.Frames[0].RenderSettings;
            await Assert.That(shifted.SamplesPerPixel == baseline.SamplesPerPixel &&
                shifted.EnableLighting == baseline.EnableLighting && shifted.EnableShadows == baseline.EnableShadows &&
                shifted.ClearColor == baseline.ClearColor && shifted.BackfaceCulling == baseline.BackfaceCulling &&
                shifted.UseSceneMaterials == baseline.UseSceneMaterials && shifted.Complexity == baseline.Complexity &&
                shifted.OutputTransform == baseline.OutputTransform &&
                Equals(shifted.DisplayTransform, baseline.DisplayTransform)).IsTrue();
            await Assert.That(job.Frames[1].RenderSettings).IsEqualTo(baseline);
            ushort[] input = [0x4400, 0xC000, 0x0000, 0x3800, 0x3C00, 0x7BFF, 0x3400, 0x3C00,
                0x4000, 0x4200, 0x4400, 0x0000, 0x0001, 0x8400, 0x3C00, 0x3555];
            byte[] hdr = Halves(input);
            var source = new ProductSource { HdrBytes = hdr };
            RenderDiskJobResult result = RenderDiskJob.Execute(job, source);
            await Assert.That(source.States[0].RenderSettings.Exposure).IsEqualTo(baseline.Exposure - 2);
            byte[] first = await File.ReadAllBytesAsync(
                Path.Combine(result.OutputDirectory, result.Frames[0].HdrColorFileName!));
            byte[] second = await File.ReadAllBytesAsync(
                Path.Combine(result.OutputDirectory, result.Frames[1].HdrColorFileName!));
            if (exr)
            {
                first = ExrScanlineOracle.Read(first, 2, 2);
                second = ExrScanlineOracle.Read(second, 2, 2);
            }
            ushort[] expected = [0x3C00, 0xB800, 0x0000, 0x3800, 0x3400, 0x73FF, 0x2C00, 0x3C00,
                0x3800, 0x3A00, 0x3C00, 0x0000, 0x0000, 0x8100, 0x3400, 0x3555];
            await Assert.That(Convert.ToHexString(first)).IsEqualTo(Convert.ToHexString(Halves(expected)));
            await Assert.That(Convert.ToHexString(second)).IsEqualTo(Convert.ToHexString(hdr));
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(result.OutputDirectory, "manifest.json")));
            await Assert.That(manifest.RootElement.GetProperty("frames")[0].GetProperty("productCamera")
                .GetProperty("linearExposureScale").GetDouble()).IsEqualTo(0.25d);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task CameraExposureWithoutAFiniteDisplayStopIsRefusedBeforeOutput()
    {
        RenderProductRequest request = Request();
        RenderPreparedFrame frame = request.PrepareFrame(
            UsdMatrix4d.Identity, Optics(), 1, new RenderCameraFrameSettings(exposureResponsivity: 0));
        await Assert.That(frame.CameraSettings!.LinearExposureScale).IsEqualTo(0d);
        await Assert.That(() => new RenderProductJobPlan(new StageIdentity("scene.usda"), [frame],
            RenderSettings.Default)).Throws<NotSupportedException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CameraExposureThatOverflowsBinary16PublishesNothing(bool exr)
    {
        string root = Directory.CreateTempSubdirectory("product-exposure-overflow-").FullName;
        try
        {
            RenderProductRequest request = Request();
            RenderPreparedFrame frame = request.PrepareFrame(
                UsdMatrix4d.Identity, Optics(), 1, new RenderCameraFrameSettings(exposure: 2));
            var plan = new RenderProductJobPlan(new StageIdentity("scene.usda"), [frame], RenderSettings.Default);
            ushort[] input = new ushort[16];
            Array.Fill(input, (ushort)0x3C00);
            input[4] = 0x7BFF;
            string output = Path.Combine(root, "out");
            await Assert.That(() => RenderDiskJob.Execute(
                    plan.CreateJob(output, exr ? RenderHdrColorFormat.Exr : RenderHdrColorFormat.RawRgba16Float),
                    new ProductSource { HdrBytes = Halves(input) }))
                .Throws<InvalidDataException>();
            await Assert.That(Directory.Exists(output)).IsFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ActiveShutterAccumulatesEqualWeightSamplesIntoBothColorPlanes()
    {
        string root = Directory.CreateTempSubdirectory("product-shutter-").FullName;
        try
        {
            RenderProductRequest request = Request(disableOptics: false, colorOnly: true);
            RenderPreparedFrame frame = ShutterFrame(request, 4, -0.25, 0.25);
            var plan = new RenderProductJobPlan(new StageIdentity("scene.usda"), [frame],
                RenderSettings.PresentationDefault);
            IReadOnlyList<StageRenderState> shutter = plan.AccumulationStates(0)!;
            double[] expectedTimes = [.. Enumerable.Range(0, 8).Select(static i => 3.75 + (0.5 * (i + 0.5) / 8))];
            await Assert.That(shutter.Select(static state => state.Time.TimeCode).SequenceEqual(expectedTimes)).IsTrue();
            await Assert.That(shutter.Zip(shutter.Skip(1)).All(static pair => pair.Second.Revision > pair.First.Revision))
                .IsTrue();
            var source = new ProductSource
            {
                IncludeDepth = false,
                Pixels = static (index, count) =>
                {
                    byte[] rgba = new byte[count * 4];
                    Array.Fill(rgba, (byte)index);
                    ushort[] hdr = new ushort[count * 4];
                    Array.Fill(hdr, BitConverter.HalfToUInt16Bits((Half)index));
                    return (rgba, Halves(hdr), index % 2 == 0 ? Rgba8RowOrder.TopDown : Rgba8RowOrder.BottomUp);
                },
            };
            RenderDiskJobResult result = RenderDiskJob.Execute(plan.CreateJob(Path.Combine(root, "out")), source);
            await Assert.That(source.Calls).IsEqualTo(8);
            await Assert.That(source.States.Select(static state => state.Time.TimeCode).SequenceEqual(expectedTimes))
                .IsTrue();
            await Assert.That(result.Frames[0].State.Time.TimeCode).IsEqualTo(4d);
            byte[] hdrFile = await File.ReadAllBytesAsync(
                Path.Combine(result.OutputDirectory, result.Frames[0].HdrColorFileName!));
            ushort[] mean = new ushort[16];
            Array.Fill(mean, BitConverter.HalfToUInt16Bits((Half)3.5));
            await Assert.That(Convert.ToHexString(hdrFile)).IsEqualTo(Convert.ToHexString(Halves(mean)));
            PngRgba8Image png = PngRgba8Reader.Decode(await File.ReadAllBytesAsync(
                Path.Combine(result.OutputDirectory, result.Frames[0].FileName)));
            await Assert.That(png.Pixels.All(static value => value == 4)).IsTrue();
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(result.OutputDirectory, "manifest.json")));
            JsonElement saved = manifest.RootElement.GetProperty("frames")[0];
            await Assert.That(saved.GetProperty("productCamera").GetProperty("accumulationSampleCount").GetInt32())
                .IsEqualTo(8);
            JsonElement accumulation = saved.GetProperty("productAccumulation");
            await Assert.That(accumulation.GetProperty("sampleTimeCodes").EnumerateArray()
                .Select(static value => value.GetDouble()).SequenceEqual(expectedTimes)).IsTrue();
            await Assert.That(accumulation.GetProperty("filter").GetString())
                .IsEqualTo("box-stratified-subinterval-centers");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ActiveShutterRefusesDepthAndPublishesNothingForAnInvalidSample()
    {
        RenderProductRequest depth = Request(disableOptics: false);
        await Assert.That(() => new RenderProductJobPlan(new StageIdentity("scene.usda"),
            [ShutterFrame(depth, 1, -0.25, 0.25)], RenderSettings.Default)).Throws<NotSupportedException>();
        RenderProductRequest color = Request(disableOptics: false, colorOnly: true);
        RenderPreparedFrame instant = color.PrepareFrame(
            UsdMatrix4d.Identity, Optics(), 1, new RenderCameraFrameSettings(0.5, 0.5));
        await Assert.That(() => new RenderProductJobPlan(new StageIdentity("scene.usda"), [instant],
            RenderSettings.Default)).Throws<NotSupportedException>();
        string root = Directory.CreateTempSubdirectory("product-shutter-invalid-").FullName;
        try
        {
            var plan = new RenderProductJobPlan(new StageIdentity("scene.usda"),
                [ShutterFrame(color, 1, 0, 1)], RenderSettings.Default);
            string output = Path.Combine(root, "out");
            var source = new ProductSource
            {
                IncludeDepth = false,
                Pixels = static (index, count) =>
                {
                    ushort[] hdr = new ushort[count * 4];
                    hdr[0] = index == 5 ? (ushort)0x7C00 : (ushort)0x3C00;
                    return (new byte[count * 4], Halves(hdr), Rgba8RowOrder.TopDown);
                },
            };
            await Assert.That(() => RenderDiskJob.Execute(plan.CreateJob(output), source))
                .Throws<InvalidDataException>();
            await Assert.That(Directory.Exists(output)).IsFalse();
            await Assert.That(Directory.GetFileSystemEntries(root)).IsEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task DepthOfFieldSamplesKeepTheFocusPlaneFixedAndAccumulateIntoTheProduct()
    {
        RenderProductRequest request = Request(disableOptics: false, colorOnly: true);
        RenderPreparedFrame frame = LensFrame(request, PerspectiveOptics(fStop: 2, focusDistance: 10));
        double radius = 20 * RenderProductRequest.FocalLengthUnit / (2 * 2);
        await Assert.That(frame.AccumulationSamples.Count).IsEqualTo(RenderProductRequest.DepthOfFieldSampleCount);
        Vector2 focusNdc = Ndc(frame.Camera, new Vector3(0.3f, 0.2f, -10));
        Vector2 nearNdc = Ndc(frame.Camera, new Vector3(0.3f, 0.2f, -2));
        float nearSpread = 0;
        foreach (RenderPreparedFrame sample in frame.AccumulationSamples)
        {
            Vector2 offset = sample.LensOffset!.Value;
            await Assert.That(offset.Length()).IsLessThanOrEqualTo((float)radius * 1.0001f);
            await Assert.That(Vector2.Distance(Ndc(sample.Camera, new Vector3(0.3f, 0.2f, -10)), focusNdc))
                .IsLessThan(1e-4f);
            nearSpread = MathF.Max(nearSpread, Vector2.Distance(Ndc(sample.Camera, new Vector3(0.3f, 0.2f, -2)), nearNdc));
        }
        await Assert.That(nearSpread).IsGreaterThan(1e-2f);
        await Assert.That(frame.AccumulationSamples.Select(static sample => sample.LensOffset!.Value).Distinct().Count())
            .IsEqualTo(RenderProductRequest.DepthOfFieldSampleCount);

        string root = Directory.CreateTempSubdirectory("product-lens-").FullName;
        try
        {
            var plan = new RenderProductJobPlan(new StageIdentity("scene.usda"), [frame], RenderSettings.Default);
            var source = new ProductSource
            {
                IncludeDepth = false,
                Pixels = static (index, count) =>
                {
                    ushort[] hdr = new ushort[count * 4];
                    Array.Fill(hdr, BitConverter.HalfToUInt16Bits((Half)index));
                    return (new byte[count * 4], Halves(hdr), Rgba8RowOrder.TopDown);
                },
            };
            RenderDiskJobResult result = RenderDiskJob.Execute(plan.CreateJob(Path.Combine(root, "out")), source);
            await Assert.That(source.Calls).IsEqualTo(RenderProductRequest.DepthOfFieldSampleCount);
            byte[] hdrFile = await File.ReadAllBytesAsync(
                Path.Combine(result.OutputDirectory, result.Frames[0].HdrColorFileName!));
            ushort[] mean = new ushort[16];
            Array.Fill(mean, BitConverter.HalfToUInt16Bits((Half)7.5));
            await Assert.That(Convert.ToHexString(hdrFile)).IsEqualTo(Convert.ToHexString(Halves(mean)));
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(result.OutputDirectory, "manifest.json")));
            JsonElement accumulation = manifest.RootElement.GetProperty("frames")[0].GetProperty("productAccumulation");
            await Assert.That(accumulation.GetProperty("lensModel").GetString())
                .IsEqualTo("thin-lens-concentric-disk-radius-focalLength-x0.1-over-2fStop");
            await Assert.That(accumulation.GetProperty("sampleLensOffsets").GetArrayLength())
                .IsEqualTo(RenderProductRequest.DepthOfFieldSampleCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments("focus")]
    [Arguments("depth")]
    [Arguments("missing-samples")]
    public async Task DepthOfFieldRefusesUndefinedLensesDepthAndUnsampledFrames(string failure)
    {
        RenderProductRequest request = Request(disableOptics: false, colorOnly: failure != "depth");
        UsdGeomCameraState optics = PerspectiveOptics(fStop: 2, focusDistance: failure == "focus" ? 0 : 10);
        if (failure == "focus")
        {
            RenderPreparedFrame unfocused = request.PrepareFrame(
                UsdMatrix4d.Identity, optics, 1, RenderCameraFrameSettings.Default);
            await Assert.That(() => RenderProductRequest.PrepareLensSample(unfocused, 0))
                .Throws<NotSupportedException>();
            await Assert.That(() => new RenderProductJobPlan(new StageIdentity("scene.usda"), [unfocused],
                RenderSettings.Default)).Throws<NotSupportedException>();
            return;
        }
        RenderPreparedFrame frame = failure == "missing-samples"
            ? request.PrepareFrame(UsdMatrix4d.Identity, optics, 1, RenderCameraFrameSettings.Default)
            : LensFrame(request, optics);
        await Assert.That(() => new RenderProductJobPlan(new StageIdentity("scene.usda"), [frame],
            RenderSettings.Default)).Throws<NotSupportedException>();
    }

    [Test]
    public async Task PrimIdVariableIsAdmittedAsItsOwnIntPlane()
    {
        var plan = new RenderProductJobPlan(new StageIdentity("scene.usda"),
            [Frame(Request(primIdType: "int"))], RenderSettings.Default);
        await Assert.That(plan.Outputs.Select(static output => output.Plane).SequenceEqual(
            [RenderProductPlane.HdrColor, RenderProductPlane.DeviceDepth, RenderProductPlane.PrimId])).IsTrue();
        await Assert.That(plan.Outputs[2].Variable.DataType).IsEqualTo("int");
        await Assert.That(plan.IncludePrimId && plan.IncludeDeviceDepth && plan.IncludeHdrColor).IsTrue();
        RenderDiskJobRequest job = plan.CreateJob(Path.Combine(Path.GetTempPath(), $"product-{Guid.NewGuid():N}"));
        await Assert.That(job.IncludePrimId).IsTrue();
        var colorOnly = new RenderProductJobPlan(new StageIdentity("scene.usda"),
            [Frame(Request(colorOnly: true))], RenderSettings.Default);
        await Assert.That(colorOnly.IncludePrimId).IsFalse();
        await Assert.That(colorOnly.CreateJob(Path.Combine(Path.GetTempPath(), $"product-{Guid.NewGuid():N}"))
            .IncludePrimId).IsFalse();
    }

    [Test]
    [Arguments("float")]
    [Arguments("int2")]
    [Arguments("uint")]
    public async Task PrimIdVariableRequiresTheIntDataType(string dataType)
    {
        RenderProductRequest request = Request(primIdType: dataType);
        await Assert.That(() => new RenderProductJobPlan(new StageIdentity("scene.usda"), [Frame(request)],
            RenderSettings.Default)).Throws<NotSupportedException>();
    }

    [Test]
    public async Task PrimIdRefusesShutterAndLensAccumulation()
    {
        RenderProductRequest request = Request(disableOptics: false, colorOnly: true, primIdType: "int");
        NotSupportedException? shutter = Assert.Throws<NotSupportedException>(() => _ = new RenderProductJobPlan(
            new StageIdentity("scene.usda"), [ShutterFrame(request, 1, -0.25, 0.25)], RenderSettings.Default));
        await Assert.That(shutter!.Message).Contains("Prim identity cannot be averaged");
        NotSupportedException? lens = Assert.Throws<NotSupportedException>(() => _ = new RenderProductJobPlan(
            new StageIdentity("scene.usda"), [LensFrame(request, PerspectiveOptics(fStop: 2, focusDistance: 10))],
            RenderSettings.Default));
        await Assert.That(lens!.Message).Contains("Prim identity cannot be averaged");
    }

    [Test]
    public async Task PrimIdChargesEightMoreManagedBytesPerPixel()
    {
        var plan = new RenderProductJobPlan(
            new StageIdentity("scene.usda"), [Frame(Request(primIdType: "int"))], RenderSettings.Default);
        string output = Path.Combine(Path.GetTempPath(), $"product-quota-{Guid.NewGuid():N}");
        await Assert.That(() => plan.CreateJob(output, limits: new RenderDiskJobLimits(maximumFrameBytes: 111)))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(plan.CreateJob(output, limits: new RenderDiskJobLimits(maximumFrameBytes: 112))
            .Limits.MaximumFrameBytes).IsEqualTo(112L);
        await Assert.That(Directory.Exists(output)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PrimIdProductWritesRawIndicesTableAndManifestEntries(bool exr)
    {
        if (exr && Environment.GetEnvironmentVariable("OPENUSD_EXR_EXECUTION_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_EXR_EXECUTION_REQUIRED=1 with the matching Windows x64 native runtime.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        string root = Directory.CreateTempSubdirectory("product-prim-id-").FullName;
        try
        {
            RenderProductRequest request = Request(primIdType: "int");
            var plan = new RenderProductJobPlan(new StageIdentity("scene.usda"), [Frame(request, 1), Frame(request, 2)],
                RenderSettings.Default);
            var source = new ProductSource { PrimIdPlane = SamplePrimId() };
            RenderDiskJobResult result = RenderDiskJob.Execute(plan.CreateJob(Path.Combine(root, "output"),
                exr ? RenderHdrColorFormat.Exr : RenderHdrColorFormat.RawRgba16Float), source);
            await Assert.That(Directory.GetFiles(result.OutputDirectory).Length).IsEqualTo(11);
            RenderDiskFrameResult stored = result.Frames[1];
            await Assert.That(stored.PrimIdFileName).IsEqualTo("frame-000001.prim-id.i32");
            await Assert.That(stored.PrimIdTableFileName).IsEqualTo("frame-000001.prim-id.json");
            byte[] raw = await File.ReadAllBytesAsync(Path.Combine(result.OutputDirectory, stored.PrimIdFileName!));
            await Assert.That(Convert.ToHexString(raw)).IsEqualTo("01000000FFFFFFFF0000000001000000");
            await Assert.That(stored.PrimIdBytes).IsEqualTo(16L);
            await Assert.That(stored.PrimIdSha256).IsEqualTo(Sha256(raw));
            byte[] table = await File.ReadAllBytesAsync(
                Path.Combine(result.OutputDirectory, stored.PrimIdTableFileName!));
            await Assert.That(stored.PrimIdTableBytes).IsEqualTo(table.LongLength);
            await Assert.That(stored.PrimIdTableSha256).IsEqualTo(Sha256(table));
            using (JsonDocument tableJson = JsonDocument.Parse(table))
            {
                await Assert.That(tableJson.RootElement.GetProperty("background").GetInt32()).IsEqualTo(-1);
                JsonElement identities = tableJson.RootElement.GetProperty("identities");
                await Assert.That(identities.GetArrayLength()).IsEqualTo(2);
                await Assert.That(identities[0].GetProperty("index").GetInt32()).IsEqualTo(0);
                await Assert.That(identities[0].GetProperty("path").GetString()).IsEqualTo("/World/Cube");
                await Assert.That(identities[0].GetProperty("instanceIndex").GetInt32()).IsEqualTo(-1);
                await Assert.That(identities[1].GetProperty("index").GetInt32()).IsEqualTo(1);
                await Assert.That(identities[1].GetProperty("path").GetString()).IsEqualTo("/World/Ünstanced");
                await Assert.That(identities[1].GetProperty("instanceIndex").GetInt32()).IsEqualTo(3);
            }

            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(result.OutputDirectory, "manifest.json")));
            JsonElement frame = manifest.RootElement.GetProperty("frames")[1];
            JsonElement plane = frame.GetProperty("primId");
            await Assert.That(plane.GetProperty("file").GetString()).IsEqualTo(stored.PrimIdFileName);
            await Assert.That(plane.GetProperty("format").GetString()).IsEqualTo("int32-little-endian");
            await Assert.That(plane.GetProperty("background").GetInt32()).IsEqualTo(-1);
            await Assert.That(plane.GetProperty("sha256").GetString()).IsEqualTo(Sha256(raw));
            await Assert.That(plane.GetProperty("tableFile").GetString()).IsEqualTo(stored.PrimIdTableFileName);
            await Assert.That(plane.GetProperty("tableSha256").GetString()).IsEqualTo(Sha256(table));
            JsonElement binding = frame.GetProperty("productOutputs")[2];
            await Assert.That(binding.GetProperty("variablePath").GetString()).IsEqualTo("/Render/PrimId");
            await Assert.That(binding.GetProperty("sourceName").GetString()).IsEqualTo("primId");
            await Assert.That(binding.GetProperty("dataType").GetString()).IsEqualTo("int");
            await Assert.That(binding.GetProperty("file").GetString()).IsEqualTo(stored.PrimIdFileName);
            await Assert.That(binding.GetProperty("bytes").GetInt64()).IsEqualTo(16L);
            await Assert.That(binding.GetProperty("convention").GetString()).IsEqualTo("visible-prim-identity-index");
            await Assert.That(binding.GetProperty("tableBytes").GetInt64()).IsEqualTo(table.LongLength);
            await Assert.That(result.TotalBytes).IsEqualTo(
                Directory.GetFiles(result.OutputDirectory).Sum(static file => new FileInfo(file).Length));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task PrimIdMustBeProvidedExactlyWhenRequested(bool requested)
    {
        string root = Directory.CreateTempSubdirectory("product-prim-id-set-").FullName;
        try
        {
            var plan = new RenderProductJobPlan(new StageIdentity("scene.usda"),
                [Frame(Request(primIdType: requested ? "int" : null))], RenderSettings.Default);
            var source = new ProductSource { PrimIdPlane = requested ? null : SamplePrimId() };
            string output = Path.Combine(root, "output");
            NotSupportedException? failure = Assert.Throws<NotSupportedException>(
                () => RenderDiskJob.Execute(plan.CreateJob(output), source));
            await Assert.That(failure!.Message).Contains("prim identity");
            await Assert.That(Directory.GetFileSystemEntries(root)).IsEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task PrimIdValidatesIndicesDimensionsAndItsBoundedTable()
    {
        RenderJobPrimIdentity[] two = [new("/A", -1), new("/B", 0)];
        await Assert.That(() => new RenderJobPrimId(2, 1, OutOfRange, two)).Throws<ArgumentException>();
        await Assert.That(() => new RenderJobPrimId(2, 1, BelowBackground, two)).Throws<ArgumentException>();
        await Assert.That(() => new RenderJobPrimId(2, 2, Dense, two)).Throws<ArgumentException>();
        await Assert.That(() => new RenderJobPrimId(2, 1, Dense, null!)).Throws<ArgumentNullException>();
        await Assert.That(() => new RenderJobPrimId(1, 1, Single,
            Enumerable.Repeat(new RenderJobPrimIdentity("/A", -1), RenderJobPrimId.MaximumIdentities + 1).ToArray()))
            .Throws<ArgumentException>();
        await Assert.That(() => new RenderJobPrimIdentity("", -1)).Throws<ArgumentException>();
        await Assert.That(() => new RenderJobPrimIdentity("/A\n", -1)).Throws<ArgumentException>();
        await Assert.That(() => new RenderJobPrimIdentity(
            "/" + new string('a', RenderJobPrimIdentity.MaximumPathLength), -1)).Throws<ArgumentException>();
        await Assert.That(() => new RenderJobPrimIdentity("/A", -2)).Throws<ArgumentOutOfRangeException>();
        var background = new RenderJobPrimId(2, 1, new[] { -1, -1 }, []);
        await Assert.That(background.Identities.Count).IsEqualTo(0);
        var accepted = new RenderJobPrimId(2, 1, new[] { 1, -1 }, two);
        two[1] = new("/C", 0);
        await Assert.That(accepted.Identities[1].Path).IsEqualTo("/B");
    }

    [Test]
    public async Task ShutterAccumulationRefusesAnUnrequestedPrimIdPlane()
    {
        string root = Directory.CreateTempSubdirectory("product-prim-id-shutter-").FullName;
        try
        {
            var plan = new RenderProductJobPlan(new StageIdentity("scene.usda"),
                [ShutterFrame(Request(disableOptics: false, colorOnly: true), 1, 0, 1)], RenderSettings.Default);
            var source = new ProductSource
            {
                IncludeDepth = false,
                PrimIdPlane = SamplePrimId(),
                Pixels = static (_, count) => (new byte[count * 4], new byte[count * 8], Rgba8RowOrder.TopDown),
            };
            NotSupportedException? failure = Assert.Throws<NotSupportedException>(
                () => RenderDiskJob.Execute(plan.CreateJob(Path.Combine(root, "out")), source));
            await Assert.That(failure!.Message).Contains("shutter-sample planes");
            await Assert.That(Directory.GetFileSystemEntries(root)).IsEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task InstanceIdVariableIsAdmittedAsItsOwnIntPlane()
    {
        var plan = new RenderProductJobPlan(new StageIdentity("scene.usda"),
            [Frame(Request(instanceIdType: "int"))], RenderSettings.Default);
        await Assert.That(plan.Outputs.Select(static output => output.Plane).SequenceEqual(
            [RenderProductPlane.HdrColor, RenderProductPlane.DeviceDepth, RenderProductPlane.InstanceId])).IsTrue();
        await Assert.That(plan.Outputs[2].Variable.DataType).IsEqualTo("int");
        await Assert.That(plan.IncludeInstanceId && !plan.IncludePrimId).IsTrue();
        await Assert.That(plan.CreateJob(Path.Combine(Path.GetTempPath(), $"product-{Guid.NewGuid():N}"))
            .IncludeInstanceId).IsTrue();
        var both = new RenderProductJobPlan(new StageIdentity("scene.usda"),
            [Frame(Request(primIdType: "int", instanceIdType: "int"))], RenderSettings.Default);
        await Assert.That(both.Outputs.Select(static output => output.Plane).SequenceEqual(
            [RenderProductPlane.HdrColor, RenderProductPlane.DeviceDepth, RenderProductPlane.PrimId,
                RenderProductPlane.InstanceId])).IsTrue();
        await Assert.That(both.IncludeInstanceId && both.IncludePrimId).IsTrue();
        var colorOnly = new RenderProductJobPlan(new StageIdentity("scene.usda"),
            [Frame(Request(colorOnly: true))], RenderSettings.Default);
        await Assert.That(colorOnly.IncludeInstanceId).IsFalse();
        await Assert.That(colorOnly.CreateJob(Path.Combine(Path.GetTempPath(), $"product-{Guid.NewGuid():N}"))
            .IncludeInstanceId).IsFalse();
    }

    [Test]
    [Arguments("float")]
    [Arguments("int2")]
    [Arguments("uint")]
    public async Task InstanceIdVariableRequiresTheIntDataType(string dataType)
    {
        RenderProductRequest request = Request(instanceIdType: dataType);
        await Assert.That(() => new RenderProductJobPlan(new StageIdentity("scene.usda"), [Frame(request)],
            RenderSettings.Default)).Throws<NotSupportedException>();
    }

    [Test]
    public async Task InstanceIdRefusesShutterAndLensAccumulation()
    {
        RenderProductRequest request = Request(disableOptics: false, colorOnly: true, instanceIdType: "int");
        NotSupportedException? shutter = Assert.Throws<NotSupportedException>(() => _ = new RenderProductJobPlan(
            new StageIdentity("scene.usda"), [ShutterFrame(request, 1, -0.25, 0.25)], RenderSettings.Default));
        await Assert.That(shutter!.Message).Contains("Instance identity cannot be averaged");
        NotSupportedException? lens = Assert.Throws<NotSupportedException>(() => _ = new RenderProductJobPlan(
            new StageIdentity("scene.usda"), [LensFrame(request, PerspectiveOptics(fStop: 2, focusDistance: 10))],
            RenderSettings.Default));
        await Assert.That(lens!.Message).Contains("Instance identity cannot be averaged");
    }

    [Test]
    [Arguments(false, 112)]
    [Arguments(true, 128)]
    public async Task InstanceIdChargesOnlyItsOwnRasterWhenSharingThePickReadback(bool withPrimId, int minimum)
    {
        var plan = new RenderProductJobPlan(new StageIdentity("scene.usda"),
            [Frame(Request(primIdType: withPrimId ? "int" : null, instanceIdType: "int"))], RenderSettings.Default);
        string output = Path.Combine(Path.GetTempPath(), $"product-quota-{Guid.NewGuid():N}");
        await Assert.That(() => plan.CreateJob(output, limits: new RenderDiskJobLimits(maximumFrameBytes: minimum - 1)))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(plan.CreateJob(output, limits: new RenderDiskJobLimits(maximumFrameBytes: minimum))
            .Limits.MaximumFrameBytes).IsEqualTo((long)minimum);
        await Assert.That(Directory.Exists(output)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InstanceIdProductWritesRawIndicesAndManifestEntries(bool exr)
    {
        if (exr && Environment.GetEnvironmentVariable("OPENUSD_EXR_EXECUTION_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_EXR_EXECUTION_REQUIRED=1 with the matching Windows x64 native runtime.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        string root = Directory.CreateTempSubdirectory("product-instance-id-").FullName;
        try
        {
            RenderProductRequest request = Request(primIdType: "int", instanceIdType: "int");
            var plan = new RenderProductJobPlan(new StageIdentity("scene.usda"), [Frame(request, 1), Frame(request, 2)],
                RenderSettings.Default);
            var source = new ProductSource { PrimIdPlane = SamplePrimId(), InstanceIdPlane = SampleInstanceId() };
            RenderDiskJobResult result = RenderDiskJob.Execute(plan.CreateJob(Path.Combine(root, "output"),
                exr ? RenderHdrColorFormat.Exr : RenderHdrColorFormat.RawRgba16Float), source);
            await Assert.That(Directory.GetFiles(result.OutputDirectory).Length).IsEqualTo(13);
            RenderDiskFrameResult stored = result.Frames[1];
            await Assert.That(stored.InstanceIdFileName).IsEqualTo("frame-000001.instance-id.i32");
            byte[] raw = await File.ReadAllBytesAsync(
                Path.Combine(result.OutputDirectory, stored.InstanceIdFileName!));
            await Assert.That(Convert.ToHexString(raw)).IsEqualTo("03000000FFFFFFFFFFFFFFFF03000000");
            await Assert.That(stored.InstanceIdBytes).IsEqualTo(16L);
            await Assert.That(stored.InstanceIdSha256).IsEqualTo(Sha256(raw));

            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(result.OutputDirectory, "manifest.json")));
            JsonElement frame = manifest.RootElement.GetProperty("frames")[1];
            JsonElement plane = frame.GetProperty("instanceId");
            await Assert.That(plane.GetProperty("file").GetString()).IsEqualTo(stored.InstanceIdFileName);
            await Assert.That(plane.GetProperty("format").GetString()).IsEqualTo("int32-little-endian");
            await Assert.That(plane.GetProperty("rowOrder").GetString()).IsEqualTo("top-down");
            await Assert.That(plane.GetProperty("convention").GetString())
                .IsEqualTo("innermost-instancer-instance-index");
            await Assert.That(plane.GetProperty("background").GetInt32()).IsEqualTo(-1);
            await Assert.That(plane.GetProperty("bytes").GetInt64()).IsEqualTo(16L);
            await Assert.That(plane.GetProperty("sha256").GetString()).IsEqualTo(Sha256(raw));
            JsonElement binding = frame.GetProperty("productOutputs")[3];
            await Assert.That(binding.GetProperty("variablePath").GetString()).IsEqualTo("/Render/InstanceId");
            await Assert.That(binding.GetProperty("sourceName").GetString()).IsEqualTo("instanceId");
            await Assert.That(binding.GetProperty("dataType").GetString()).IsEqualTo("int");
            await Assert.That(binding.GetProperty("file").GetString()).IsEqualTo(stored.InstanceIdFileName);
            await Assert.That(binding.GetProperty("bytes").GetInt64()).IsEqualTo(16L);
            await Assert.That(binding.GetProperty("convention").GetString())
                .IsEqualTo("innermost-instancer-instance-index");
            await Assert.That(binding.GetProperty("background").GetInt32()).IsEqualTo(-1);
            await Assert.That(result.TotalBytes).IsEqualTo(
                Directory.GetFiles(result.OutputDirectory).Sum(static file => new FileInfo(file).Length));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task InstanceIdMustBeProvidedExactlyWhenRequested(bool requested)
    {
        string root = Directory.CreateTempSubdirectory("product-instance-id-set-").FullName;
        try
        {
            var plan = new RenderProductJobPlan(new StageIdentity("scene.usda"),
                [Frame(Request(instanceIdType: requested ? "int" : null))], RenderSettings.Default);
            var source = new ProductSource { InstanceIdPlane = requested ? null : SampleInstanceId() };
            string output = Path.Combine(root, "output");
            NotSupportedException? failure = Assert.Throws<NotSupportedException>(
                () => RenderDiskJob.Execute(plan.CreateJob(output), source));
            await Assert.That(failure!.Message).Contains("instance identity");
            await Assert.That(Directory.GetFileSystemEntries(root)).IsEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task InstanceIdValidatesIndicesAndDimensions()
    {
        await Assert.That(() => new RenderJobInstanceId(2, 1, BelowBackground)).Throws<ArgumentException>();
        await Assert.That(() => new RenderJobInstanceId(2, 2, Dense)).Throws<ArgumentException>();
        var accepted = new RenderJobInstanceId(2, 1, new[] { 7, -1 });
        await Assert.That(accepted.Values.Span[0]).IsEqualTo(7);
        await Assert.That(new RenderJobInstanceId(2, 1, new[] { int.MaxValue, -1 }).Values.Span[0])
            .IsEqualTo(int.MaxValue);
    }

    [Test]
    public async Task ShutterAccumulationRefusesAnUnrequestedInstanceIdPlane()
    {
        string root = Directory.CreateTempSubdirectory("product-instance-id-shutter-").FullName;
        try
        {
            var plan = new RenderProductJobPlan(new StageIdentity("scene.usda"),
                [ShutterFrame(Request(disableOptics: false, colorOnly: true), 1, 0, 1)], RenderSettings.Default);
            var source = new ProductSource
            {
                IncludeDepth = false,
                InstanceIdPlane = SampleInstanceId(),
                Pixels = static (_, count) => (new byte[count * 4], new byte[count * 8], Rgba8RowOrder.TopDown),
            };
            NotSupportedException? failure = Assert.Throws<NotSupportedException>(
                () => RenderDiskJob.Execute(plan.CreateJob(Path.Combine(root, "out")), source));
            await Assert.That(failure!.Message).Contains("shutter-sample planes");
            await Assert.That(Directory.GetFileSystemEntries(root)).IsEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static RenderJobInstanceId SampleInstanceId() => new(2, 2, new[] { 3, -1, -1, 3 });

    private static readonly int[] OutOfRange = [0, 2];
    private static readonly int[] BelowBackground = [-2, 0];
    private static readonly int[] Dense = [0, 1];
    private static readonly int[] Single = [0];

    private static RenderJobPrimId SamplePrimId() => new(2, 2, new[] { 1, -1, 0, 1 },
        [new RenderJobPrimIdentity("/World/Cube", -1), new RenderJobPrimIdentity("/World/Ünstanced", 3)]);

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private static RenderPreparedFrame LensFrame(RenderProductRequest request, UsdGeomCameraState optics)
    {
        RenderPreparedFrame frame = request.PrepareFrame(
            UsdMatrix4d.Identity, optics, 1, RenderCameraFrameSettings.Default);
        var samples = new RenderPreparedFrame[RenderProductRequest.DepthOfFieldSampleCount];
        for (int index = 0; index < samples.Length; index++)
        {
            samples[index] = RenderProductRequest.PrepareLensSample(frame, index);
        }
        return frame.WithAccumulationSamples(samples);
    }

    private static Vector2 Ndc(CameraState camera, Vector3 point)
    {
        Vector4 clip = Vector4.Transform(new Vector4(point, 1), camera.View * camera.Projection);
        return new Vector2(clip.X / clip.W, clip.Y / clip.W);
    }

    private static UsdGeomCameraState PerspectiveOptics(double fStop, double focusDistance) => new(
        UsdGeomCameraProjection.Perspective, -0.5, 0.5, -0.5, 0.5, 1, 100, 20, 20, 20, 0, 0, focusDistance, fStop);

    private static RenderPreparedFrame ShutterFrame(
        RenderProductRequest request, double time, double open, double close)
    {
        var settings = new RenderCameraFrameSettings(open, close);
        RenderPreparedFrame frame = request.PrepareFrame(UsdMatrix4d.Identity, Optics(), time, settings);
        var samples = new RenderPreparedFrame[RenderProductRequest.MotionBlurSampleCount];
        for (int index = 0; index < samples.Length; index++)
        {
            samples[index] = request.PrepareFrame(UsdMatrix4d.Identity, Optics(),
                time + open + ((close - open) * (index + 0.5) / samples.Length), settings);
        }
        return frame.WithAccumulationSamples(samples);
    }

    private static byte[] Halves(ushort[] values)
    {
        byte[] bytes = new byte[values.Length * 2];
        for (int index = 0; index < values.Length; index++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(index * 2), values[index]);
        }
        return bytes;
    }

    private static RenderPreparedFrame Frame(RenderProductRequest request, double time = 1) =>
        request.PrepareFrame(UsdMatrix4d.Identity, Optics(), time, RenderCameraFrameSettings.Default);

    private static UsdGeomCameraState Optics(double fStop = 0) => new(
        UsdGeomCameraProjection.Orthographic, -1, 1, -1, 1, 1, 11, 0, 20, 20, 0, 0, 10, fStop);

    private static RenderProductRequest Request(
        string? failure = null, bool depthFirst = false, bool disableOptics = true, bool colorOnly = false,
        string? primIdType = null, string? instanceIdType = null)
    {
        var color = new UsdRenderVariableSpecification("/Render/Color",
            failure == "variable-type" ? "color3f" : "half4",
            failure == "source-name" ? "normal" : "color", failure == "source-type" ? "lpe" : "raw",
            failure == "variable-setting" ? ["renderer:filter"] : []);
        var depth = new UsdRenderVariableSpecification("/Render/Depth", "float", "depth", "raw", []);
        int[] indices = failure == "outputs-empty" ? [] : failure == "duplicate-color" ? [0, 0] :
            colorOnly ? [0] : depthFirst ? [1, 0] : [0, 1];
        UsdRenderVariableSpecification[] variables = primIdType is null
            ? [color, depth]
            : [color, depth, new UsdRenderVariableSpecification("/Render/PrimId", primIdType, "primId", "raw", [])];
        if (primIdType is not null)
        {
            indices = [.. indices, 2];
        }
        if (instanceIdType is not null)
        {
            indices = [.. indices, variables.Length];
            variables = [.. variables,
                new UsdRenderVariableSpecification("/Render/InstanceId", instanceIdType, "instanceId", "raw", [])];
        }
        return new RenderProductRequest(new UsdRenderSpecification("/Render/Settings",
            [new UsdRenderProductSpecification("/Render/Product", "../not-authorized.exr", "raster", "/Camera",
                2, 2, 1, "expandAperture", new UsdVec2f(20, 20), new UsdVec4f(0, 0, 1, 1),
                disableOptics, disableOptics, indices, failure == "product-setting" ? ["renderer:samples"] : [])],
            variables, failure == "purpose" ? ["custom"] : ["default", "render"],
            failure switch
            {
                "binding-order" => ["full", "preview"],
                "binding-empty" => [],
                "binding-all-purpose" => [""],
                "binding-fallback-first" => ["", "full"],
                "binding-named-after-fallback" => ["full", "", "preview"],
                "binding-fallback" => ["full", ""],
                "binding-preview-fallback" => ["preview", "", ""],
                _ => ["full"],
            },
            failure == "color-space" ? "lin_rec709_scene" : "",
            failure == "settings-setting" ? ["renderer:samples"] : []), 0);
    }

    private sealed class ProductSource : IRenderProductFrameSource
    {
        internal byte[]? HdrBytes { get; init; }
        internal bool IncludeDepth { get; init; } = true;
        internal Func<int, int, (byte[] Rgba, byte[] Hdr, Rgba8RowOrder RowOrder)>? Pixels { get; init; }
        internal bool Refuse { get; init; }
        internal RenderJobPrimId? PrimIdPlane { get; init; }
        internal RenderJobInstanceId? InstanceIdPlane { get; init; }
        internal int Validations { get; private set; }
        internal int Calls { get; private set; }
        internal RenderProductJobPlan? Admitted { get; private set; }
        internal List<StageRenderState> States { get; } = [];

        public void ValidateProduct(RenderProductJobPlan plan, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Validations++;
            if (Refuse)
            {
                throw new NotSupportedException("Controlled unsupported product filter.");
            }
            Admitted = plan;
        }

        public RenderJobImage Render(StageRenderState state, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            States.Add(state);
            int count = state.Viewport.Width * state.Viewport.Height;
            if (Pixels is { } pixels)
            {
                (byte[] rgba, byte[] hdr, Rgba8RowOrder order) = pixels(Calls - 1, count);
                return new RenderJobImage(state.Viewport.Width, state.Viewport.Height, rgba, order)
                {
                    HdrColor = new RenderJobHdrColor(state.Viewport.Width, state.Viewport.Height, hdr),
                    DeviceDepth = IncludeDepth
                        ? new RenderJobDeviceDepth(state.Viewport.Width, state.Viewport.Height, new float[count])
                        : null,
                    PrimId = PrimIdPlane,
                    InstanceId = InstanceIdPlane,
                };
            }
            return new RenderJobImage(
                state.Viewport.Width, state.Viewport.Height, new byte[count * 4], Rgba8RowOrder.TopDown)
            {
                HdrColor = new RenderJobHdrColor(
                    state.Viewport.Width, state.Viewport.Height, HdrBytes ?? new byte[count * 8]),
                DeviceDepth = new RenderJobDeviceDepth(state.Viewport.Width, state.Viewport.Height, new float[count]),
                PrimId = PrimIdPlane,
                InstanceId = InstanceIdPlane,
            };
        }
    }

    private sealed class LegacySource : IRenderJobFrameSource
    {
        internal int Calls { get; private set; }
        public RenderJobImage Render(StageRenderState state, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("A legacy source must never be asked to render this product.");
        }
    }
}
