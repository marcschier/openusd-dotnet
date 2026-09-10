// Copyright (c) marcschier. Licensed under the MIT License.

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
    [Arguments("variable-type")]
    [Arguments("source-type")]
    [Arguments("source-name")]
    [Arguments("variable-setting")]
    [Arguments("product-setting")]
    [Arguments("settings-setting")]
    [Arguments("color-space")]
    [Arguments("binding-order")]
    [Arguments("binding-empty")]
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
    [Arguments("exposure")]
    [Arguments("shutter")]
    [Arguments("dof")]
    [Arguments("geometry-only")]
    public async Task CameraSemanticsCannotBeDroppedByProductAdmission(string failure)
    {
        RenderProductRequest request = Request(disableOptics: false);
        RenderCameraFrameSettings camera = failure switch
        {
            "exposure" => new(exposure: 1),
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
        await Assert.That(() => new RenderCameraFrameSettings(shutterOpen: 1, shutterClose: 0)).Throws<ArgumentException>();
        await Assert.That(() => new RenderCameraFrameSettings(exposure: double.NaN)).Throws<ArgumentException>();
        await Assert.That(() => new RenderCameraFrameSettings(exposure: 1024)).Throws<ArgumentException>();
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
        var plan = new RenderProductJobPlan(new StageIdentity("scene.usda"), [Frame(Request())], RenderSettings.Default);
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
            var plan = new RenderProductJobPlan(new StageIdentity("scene.usda"), [Frame(Request())], RenderSettings.Default);
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
    public async Task ProductManifestCarriesEveryRequestedVariableAndDoesNotUseItsAuthoredFilename()
    {
        string root = Directory.CreateTempSubdirectory("product-manifest-").FullName;
        try
        {
            RenderProductRequest request = Request(depthFirst: true);
            var plan = new RenderProductJobPlan(new StageIdentity("scene.usda"),
                [Frame(request, 1), Frame(request, 2)], RenderSettings.PresentationDefault, 42);
            var source = new ProductSource();
            RenderDiskJobResult result = RenderDiskJob.Execute(plan.CreateJob(Path.Combine(root, "output")), source);
            await Assert.That(source.Validations).IsEqualTo(1);
            await Assert.That(source.Calls).IsEqualTo(2);
            await Assert.That(source.Admitted).IsSameReferenceAs(plan);
            await Assert.That(File.Exists(Path.Combine(root, "not-authorized.exr"))).IsFalse();
            await Assert.That(Directory.GetFiles(result.OutputDirectory).Length).IsEqualTo(7);
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(result.OutputDirectory, "manifest.json")));
            JsonElement product = manifest.RootElement.GetProperty("authoredProduct");
            await Assert.That(product.GetProperty("requestedProductName").GetString()).IsEqualTo("../not-authorized.exr");
            await Assert.That(product.GetProperty("productNameUsedAsWriteAuthority").GetBoolean()).IsFalse();
            await Assert.That(product.GetProperty("materialBindingPurpose").GetString()).IsEqualTo("full");
            await Assert.That(product.GetProperty("callerSourceStageRevision").GetUInt64()).IsEqualTo(42UL);
            await Assert.That(product.GetProperty("pngRole").GetString()).IsEqualTo("display-companion-not-authored-render-variable");
            JsonElement frame = manifest.RootElement.GetProperty("frames")[0];
            JsonElement outputs = frame.GetProperty("productOutputs");
            await Assert.That(outputs.GetArrayLength()).IsEqualTo(2);
            await Assert.That(outputs[0].GetProperty("sourceName").GetString()).IsEqualTo("depth");
            await Assert.That(outputs[0].GetProperty("file").GetString()).IsEqualTo(result.Frames[0].DepthFileName);
            await Assert.That(outputs[1].GetProperty("dataType").GetString()).IsEqualTo("half4");
            await Assert.That(outputs[1].GetProperty("file").GetString()).IsEqualTo(result.Frames[0].HdrColorFileName);
            await Assert.That(outputs[1].GetProperty("sha256").GetString()).IsEqualTo(result.Frames[0].HdrColorSha256);
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
    public async Task CroppedRawProductsRetainTheirRasterMetadataAndCannotMasqueradeAsFullWindowExr()
    {
        string root = Directory.CreateTempSubdirectory("product-crop-").FullName;
        try
        {
            var request = new RenderProductRequest(Request().Specification, 0, new RenderProductOverrides(
                resolution: new ViewportDimensions(4, 4), dataWindowNdc: new UsdVec4f(0.25f, 0, 0.75f, 1)));
            var plan = new RenderProductJobPlan(new StageIdentity("scene.usda"), [Frame(request)], RenderSettings.Default);
            await Assert.That(() => plan.CreateJob(Path.Combine(root, "exr"), RenderHdrColorFormat.Exr))
                .Throws<NotSupportedException>();
            RenderDiskJobResult result = RenderDiskJob.Execute(plan.CreateJob(Path.Combine(root, "raw")), new ProductSource());
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(result.OutputDirectory, "manifest.json")));
            JsonElement raster = manifest.RootElement.GetProperty("frames")[0].GetProperty("productRaster");
            await Assert.That(raster.GetProperty("fullWidth").GetInt32()).IsEqualTo(4);
            await Assert.That(raster.GetProperty("dataWindowMinX").GetInt32()).IsEqualTo(1);
            await Assert.That(raster.GetProperty("dataWindowMinY").GetInt32()).IsEqualTo(0);
            await Assert.That(result.Frames[0].State.Viewport).IsEqualTo(new ViewportDimensions(2, 4));
            await Assert.That(Directory.Exists(Path.Combine(root, "exr"))).IsFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static RenderPreparedFrame Frame(RenderProductRequest request, double time = 1) =>
        request.PrepareFrame(UsdMatrix4d.Identity, Optics(), time, RenderCameraFrameSettings.Default);

    private static UsdGeomCameraState Optics(double fStop = 0) => new(
        UsdGeomCameraProjection.Orthographic, -1, 1, -1, 1, 1, 11, 0, 20, 20, 0, 0, 10, fStop);

    private static RenderProductRequest Request(string? failure = null, bool depthFirst = false, bool disableOptics = true)
    {
        var color = new UsdRenderVariableSpecification("/Render/Color",
            failure == "variable-type" ? "color3f" : "half4",
            failure == "source-name" ? "normal" : "color", failure == "source-type" ? "lpe" : "raw",
            failure == "variable-setting" ? ["renderer:filter"] : []);
        var depth = new UsdRenderVariableSpecification("/Render/Depth", "float", "depth", "raw", []);
        int[] indices = failure == "outputs-empty" ? [] : failure == "duplicate-color" ? [0, 0] :
            depthFirst ? [1, 0] : [0, 1];
        return new RenderProductRequest(new UsdRenderSpecification("/Render/Settings",
            [new UsdRenderProductSpecification("/Render/Product", "../not-authorized.exr", "raster", "/Camera",
                2, 2, 1, "expandAperture", new UsdVec2f(20, 20), new UsdVec4f(0, 0, 1, 1),
                disableOptics, disableOptics, indices, failure == "product-setting" ? ["renderer:samples"] : [])],
            [color, depth], failure == "purpose" ? ["custom"] : ["default", "render"],
            failure == "binding-order" ? ["full", "preview"] : failure == "binding-empty" ? [] : ["full"],
            failure == "color-space" ? "lin_rec709_scene" : "",
            failure == "settings-setting" ? ["renderer:samples"] : []), 0);
    }

    private sealed class ProductSource : IRenderProductFrameSource
    {
        internal bool Refuse { get; init; }
        internal int Validations { get; private set; }
        internal int Calls { get; private set; }
        internal RenderProductJobPlan? Admitted { get; private set; }

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
            int count = state.Viewport.Width * state.Viewport.Height;
            return new RenderJobImage(state.Viewport.Width, state.Viewport.Height, new byte[count * 4], Rgba8RowOrder.TopDown)
            {
                HdrColor = new RenderJobHdrColor(state.Viewport.Width, state.Viewport.Height, new byte[count * 8]),
                DeviceDepth = new RenderJobDeviceDepth(state.Viewport.Width, state.Viewport.Height, new float[count])
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
