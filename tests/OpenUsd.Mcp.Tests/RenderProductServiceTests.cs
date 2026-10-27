// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenUsd.Geom;
using OpenUsd.Render;
using OpenUsd.Rendering;

namespace OpenUsd.Mcp.Tests;

public sealed class RenderProductServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    [Arguments(null, null, 1.5d, 1f, 2.25f)]
    [Arguments(0.5d, null, 0.5d, 1f / 0.75f, 1f)]
    [Arguments(1d, null, 1d, 1f, 1.5f)]
    [Arguments(2d, null, 2d, 1f, 3f)]
    [Arguments(null, "expandAperture", 1.5d, 1f / 2.25f, 1f)]
    [Arguments(2d, "expandAperture", 2d, 1f / 3f, 1f)]
    [Arguments(2d, "cropAperture", 2d, 1f, 3f)]
    [Arguments(2d, "adjustApertureWidth", 2d, 1f / 3f, 1f)]
    [Arguments(2d, "adjustApertureHeight", 2d, 1f, 3f)]
    [Arguments(2d, "adjustPixelAspectRatio", 2d / 3d, 1f, 1f)]
    [Arguments(null, "adjustPixelAspectRatio", 2d / 3d, 1f, 1f)]
    public async Task ProductFramingOverridesReachProjectionAndHistoricalManifestWithoutChangingAuthoredValues(
        double? pixelAspectRatio, string? conformPolicy, double effectiveAspect, float projectionX, float projectionY)
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        RenderProductJobPlan? prepared = null;
        backend.PrepareProduct = times => prepared = Plan(
            backend.Context!.SessionId, backend.StageRevision, times, backend.LastProductOverrides,
            authoredPixelAspectRatio: 1.5f, authoredConformPolicy: "cropAperture");
        await using var workspace = files.CreateWorkspace(backend);
        using var artifacts = new ArtifactResourceStore(new ArtifactResourceStoreOptions(
            FileStorageRoot: Path.Combine(files.OutputRoot, "artifacts")));
        var factory = new ProductFactory();
        using var processor = new PreviewCaptureProcessor(factory, artifacts);
        await using var worker = new CaptureWorker(processor);
        using ServiceProvider provider = new ServiceCollection().AddSingleton(worker)
            .AddSingleton<IArtifactResourceStore>(artifacts).BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, Options(files));
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        McpRenderSequenceResultDto job = await service.RenderProductAsync(new RenderProductCaptureRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
            Width = 6,
            Height = 4,
            PixelAspectRatio = pixelAspectRatio,
            AspectRatioConformPolicy = conformPolicy
        }, default);
        await Assert.That(prepared!.Request.Product.PixelAspectRatio).IsEqualTo(1.5d);
        await Assert.That(prepared.Request.Product.AspectRatioConformPolicy).IsEqualTo("cropAperture");
        await Assert.That(prepared.Request.Overrides.PixelAspectRatio).IsEqualTo(pixelAspectRatio);
        await Assert.That(prepared.Request.Overrides.AspectRatioConformPolicy).IsEqualTo(conformPolicy);
        await Assert.That(prepared.Frames[0].PixelAspectRatio).IsEqualTo(effectiveAspect);
        await Assert.That(prepared.Frames[0].Camera.Projection.M11).IsEqualTo(projectionX);
        await Assert.That(prepared.Frames[0].Camera.Projection.M22).IsEqualTo(projectionY);
        string manifestPath = Path.Combine(backend.Context!.OutputDirectory, "renders", job.JobId, "manifest.json");
        await service.CloseSceneAsync(new SceneRevisionRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision
        }, default);
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        JsonElement raster = manifest.RootElement.GetProperty("frames")[0].GetProperty("productRaster");
        await Assert.That(raster.GetProperty("pixelAspectRatio").GetDouble()).IsEqualTo(effectiveAspect);
        await Assert.That(raster.GetProperty("aspectRatioConformPolicy").GetString())
            .IsEqualTo(conformPolicy ?? "cropAperture");
        McpSequenceFrameResultDto frame = await service.ReadSequenceFrameAsync(
            new ReadSequenceFrameRequest { JobId = job.JobId, FrameIndex = 0 }, default);
        await Assert.That(frame.Width).IsEqualTo(6);
        await Assert.That(frame.Height).IsEqualTo(4);
        ArtifactResourceContent? png = await artifacts.ReadAsync(new Uri(frame.Artifact.Uri));
        ImageRgba8 image = PngRgba8Decoder.Decode(png!.Content.Span);
        await Assert.That(image.Width).IsEqualTo(6);
        await Assert.That(image.Height).IsEqualTo(4);
        await Assert.That(backend.Events).DoesNotContain("apply");
        await Assert.That(backend.Events).DoesNotContain("checkpoint");
        await Assert.That(await File.ReadAllTextAsync(files.SourcePath)).IsEqualTo("#usda 1.0");
    }

    [Test]
    [Arguments(0d)]
    [Arguments(-1d)]
    [Arguments(double.NaN)]
    [Arguments(double.PositiveInfinity)]
    [Arguments(double.NegativeInfinity)]
    public async Task InvalidProductPixelAspectIsRejectedBeforeWorkspaceWork(double pixelAspectRatio)
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, Options(files));
        await Assert.That(async () => await service.RenderProductAsync(new RenderProductCaptureRequest
        {
            SessionId = "unused",
            PixelAspectRatio = pixelAspectRatio
        }, default)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(backend.Events).IsEmpty();
        await Assert.That((await service.ListSequenceJobsAsync(default)).Jobs).IsEmpty();
        await Assert.That(Directory.EnumerateDirectories(files.OutputRoot)).IsEmpty();
    }

    [Test]
    [Arguments("")]
    [Arguments("ExpandAperture")]
    [Arguments("unknown")]
    [Arguments(" cropAperture")]
    [Arguments("cropAperture ")]
    public async Task InvalidProductConformPolicyIsRejectedBeforeWorkspaceWork(string conformPolicy)
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, Options(files));
        await Assert.That(async () => await service.RenderProductAsync(new RenderProductCaptureRequest
        {
            SessionId = "unused",
            AspectRatioConformPolicy = conformPolicy
        }, default)).Throws<ArgumentException>();
        await Assert.That(backend.Events).IsEmpty();
        await Assert.That((await service.ListSequenceJobsAsync(default)).Jobs).IsEmpty();
        await Assert.That(Directory.EnumerateDirectories(files.OutputRoot)).IsEmpty();
    }

    [Test]
    [Arguments(null, null, false)]
    [Arguments(1, 3, false)]
    [Arguments(3, 1, false)]
    [Arguments(7, 5, false)]
    [Arguments(4096, 1, false)]
    [Arguments(1, 4096, false)]
    [Arguments(8, 6, true)]
    public async Task ProductRasterOverridesReachPreparedFramesAndHistoricalOutputsWithoutChangingAuthoredSize(
        int? width, int? height, bool cropped)
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        RenderProductJobPlan? prepared = null;
        var window = cropped ? new UsdVec4f(0.25f, 0, 0.75f, 1) : new UsdVec4f(0, 0, 1, 1);
        backend.PrepareProduct = times => prepared = Plan(
            backend.Context!.SessionId, backend.StageRevision, times, backend.LastProductOverrides, window);
        await using var workspace = files.CreateWorkspace(backend);
        using var artifacts = new ArtifactResourceStore(new ArtifactResourceStoreOptions(
            FileStorageRoot: Path.Combine(files.OutputRoot, "artifacts")));
        var factory = new ProductFactory();
        using var processor = new PreviewCaptureProcessor(factory, artifacts);
        await using var worker = new CaptureWorker(processor);
        using ServiceProvider provider = new ServiceCollection().AddSingleton(worker)
            .AddSingleton<IArtifactResourceStore>(artifacts).BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, Options(files));
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        McpRenderSequenceResultDto job = await service.RenderProductAsync(new RenderProductCaptureRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
            CameraPath = "/AlternateCamera",
            Width = width,
            Height = height
        }, default);
        await Assert.That(prepared!.Request.Product.Width).IsEqualTo(2);
        await Assert.That(prepared.Request.Product.Height).IsEqualTo(2);
        await Assert.That(prepared.Request.Product.DataWindowNdc).IsEqualTo(window);
        await Assert.That(prepared.Request.CameraPath).IsEqualTo("/AlternateCamera");
        await Assert.That(prepared.Request.Product.CameraPath).IsEqualTo("/Camera");
        await Assert.That(prepared.Request.Overrides.Resolution is null).IsEqualTo(width is null);
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(backend.Context!.OutputDirectory, "renders", job.JobId, "manifest.json")));
        JsonElement raster = manifest.RootElement.GetProperty("frames")[0].GetProperty("productRaster");
        await Assert.That(raster.GetProperty("fullWidth").GetInt32()).IsEqualTo(width ?? 2);
        await Assert.That(raster.GetProperty("fullHeight").GetInt32()).IsEqualTo(height ?? 2);
        await Assert.That(raster.GetProperty("dataWindowMinX").GetInt32()).IsEqualTo(cropped ? 2 : 0);
        await Assert.That(raster.GetProperty("dataWindowMinY").GetInt32()).IsEqualTo(0);
        await Assert.That(raster.GetProperty("pixelAspectRatio").GetDouble()).IsEqualTo(1d);
        await Assert.That(raster.GetProperty("aspectRatioConformPolicy").GetString()).IsEqualTo("expandAperture");
        await service.CloseSceneAsync(new SceneRevisionRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision
        }, default);
        McpSequenceFrameResultDto frame = await service.ReadSequenceFrameAsync(
            new ReadSequenceFrameRequest { JobId = job.JobId, FrameIndex = 0 }, default);
        int expectedWidth = cropped ? 4 : width ?? 2;
        int expectedHeight = height ?? 2;
        await Assert.That(frame.Width).IsEqualTo(expectedWidth);
        await Assert.That(frame.Height).IsEqualTo(expectedHeight);
        await Assert.That(frame.RenderRequest!.Width).IsEqualTo(expectedWidth);
        await Assert.That(frame.RenderRequest.Height).IsEqualTo(expectedHeight);
        ArtifactResourceContent? png = await artifacts.ReadAsync(new Uri(frame.Artifact.Uri));
        ImageRgba8 image = PngRgba8Decoder.Decode(png!.Content.Span);
        await Assert.That(image.Width).IsEqualTo(expectedWidth);
        await Assert.That(image.Height).IsEqualTo(expectedHeight);
        ArtifactResourceContent? hdr = await artifacts.ReadAsync(new Uri(frame.HdrColor!.Uri));
        await Assert.That(hdr!.Content.Length).IsEqualTo(expectedWidth * expectedHeight * 8);
        await Assert.That(factory.Source.Times.SequenceEqual([0d])).IsTrue();
        await Assert.That(backend.Events).DoesNotContain("apply");
        await Assert.That(backend.Events).DoesNotContain("checkpoint");
        await Assert.That(await File.ReadAllTextAsync(files.SourcePath)).IsEqualTo("#usda 1.0");
    }

    [Test]
    [Arguments(1, null)]
    [Arguments(null, 1)]
    [Arguments(0, 1)]
    [Arguments(1, 0)]
    [Arguments(-1, 1)]
    [Arguments(1, -1)]
    [Arguments(4097, 1)]
    [Arguments(1, 4097)]
    public async Task InvalidProductRasterPairsAreRejectedBeforeWorkspaceWork(int? width, int? height)
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, Options(files));
        await Assert.That(async () => await service.RenderProductAsync(new RenderProductCaptureRequest
        {
            SessionId = "unused",
            Width = width,
            Height = height
        }, default)).Throws<ArgumentException>();
        await Assert.That(backend.Events).IsEmpty();
        await Assert.That((await service.ListSequenceJobsAsync(default)).Jobs).IsEmpty();
        await Assert.That(Directory.EnumerateDirectories(files.OutputRoot)).IsEmpty();
    }

    [Test]
    public async Task ProductJobsCarryAuthoredBindingsAndShareTheExistingCompletedFrameStore()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        backend.PrepareProduct = times => Plan(backend.Context!.SessionId, backend.StageRevision, times);
        await using var workspace = files.CreateWorkspace(backend);
        using var artifacts = new ArtifactResourceStore(new ArtifactResourceStoreOptions(
            FileStorageRoot: Path.Combine(files.OutputRoot, "artifacts")));
        var factory = new ProductFactory();
        using var processor = new PreviewCaptureProcessor(factory, artifacts);
        await using var worker = new CaptureWorker(processor);
        using ServiceProvider provider = new ServiceCollection().AddSingleton(worker)
            .AddSingleton<IArtifactResourceStore>(artifacts).BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, Options(files));
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        var progress = new List<(int, int)>();
        McpRenderSequenceResultDto job = await service.RenderProductAsync(
            Request(session, 3), default, (completed, total) => progress.Add((completed, total)));
        await Assert.That(progress.SequenceEqual([(1, 3), (2, 3), (3, 3)])).IsTrue();
        await Assert.That(job.AuthoredProduct!.ProductPath).IsEqualTo("/Render/Product");
        await Assert.That(job.AuthoredProduct.MaterialBindingPurpose).IsEqualTo("full");
        await Assert.That(job.AuthoredProduct.Outputs.Select(static item => item.SourceName)
            .SequenceEqual(["depth", "color"])).IsTrue();
        await Assert.That(backend.LastSettingsPath).IsEqualTo("/Render/Settings");
        await Assert.That(backend.LastProductPath).IsEqualTo("/Render/Product");
        await Assert.That(factory.Source.Validations).IsEqualTo(1);
        await Assert.That(factory.Source.Times.SequenceEqual([2d, 2.5, 3d])).IsTrue();
        await Assert.That(factory.Source.Purposes).IsEqualTo(RenderPurpose.Default | RenderPurpose.Render);
        McpSequenceFrameResultDto frame = await service.ReadSequenceFrameAsync(
            new ReadSequenceFrameRequest { JobId = job.JobId, FrameIndex = 1 }, default);
        await Assert.That(frame.AuthoredProduct).IsSameReferenceAs(job.AuthoredProduct);
        await Assert.That(frame.TimeCode).IsEqualTo(2.5);
        await Assert.That(frame.ArtifactResources.Count).IsEqualTo(3);
        await Assert.That(frame.RenderRequest!.Purposes).IsEqualTo("Default, Render");
        await Assert.That(frame.RenderRequest.Width).IsEqualTo(2);
        await Assert.That(frame.RenderRequest.Height).IsEqualTo(2);
        await Assert.That(frame.RenderRequest.OutputTransform).IsEqualTo("Reinhard");
        await Assert.That(frame.RenderRequest.Exposure).IsEqualTo(-6);
        ArtifactResourceContent? hdr = await artifacts.ReadAsync(new Uri(frame.HdrColor!.Uri));
        await Assert.That(hdr!.Content.Length).IsEqualTo(32);
        await Assert.That(backend.Events).DoesNotContain("apply");
        await Assert.That(backend.Events).DoesNotContain("checkpoint");
        await Assert.That(File.Exists(Path.Combine(files.SourceRoot, "not-authorized.exr"))).IsFalse();
        await service.CloseSceneAsync(new SceneRevisionRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision
        }, default);
        McpSequenceJobsResultDto history = await service.ListSequenceJobsAsync(default);
        await Assert.That(history.Jobs.Count).IsEqualTo(1);
        await Assert.That(history.Jobs[0]).IsSameReferenceAs(job);
        await Assert.That(history.Jobs[0].AuthoredProduct).IsSameReferenceAs(job.AuthoredProduct);
        McpSequenceSheetResultDto sheet = await service.ReadSequenceSheetAsync(new ReadSequenceSheetRequest
        {
            JobId = job.JobId,
            FrameIndices = [2, 0],
            Width = 8,
            Height = 4
        }, default);
        JsonElement json = JsonSerializer.SerializeToElement(sheet, JsonOptions);
        await Assert.That(json.TryGetProperty("authoredProduct", out JsonElement product)).IsTrue();
        await Assert.That(product.GetProperty("productPath").GetString()).IsEqualTo("/Render/Product");
        await Assert.That(product.GetProperty("materialBindingPurpose").GetString()).IsEqualTo("full");
        await Assert.That(sheet.AuthoredProduct).IsSameReferenceAs(job.AuthoredProduct);
        await Assert.That(sheet.AuthoredProduct!.Outputs.Select(static item => item.SourceName)
            .SequenceEqual(["depth", "color"])).IsTrue();
        await Assert.That(sheet.SessionId).IsEqualTo(job.SessionId);
        await Assert.That(sheet.StageRevision).IsEqualTo(job.StageRevision);
        await Assert.That(sheet.Tiles.Select(static tile => tile.TimeCode).SequenceEqual([3d, 2d])).IsTrue();
        foreach (McpSequenceSheetTileDto tile in sheet.Tiles)
        {
            await Assert.That(tile.RenderRequest!.Purposes).IsEqualTo("Default, Render");
            await Assert.That(tile.RenderRequest.Width).IsEqualTo(2);
            await Assert.That(tile.RenderRequest.Height).IsEqualTo(2);
            await Assert.That(tile.RenderRequest.Exposure).IsEqualTo(-6);
        }
        await Assert.That(factory.Source.Times.SequenceEqual([2d, 2.5, 3d])).IsTrue();
    }

    [Test]
    public async Task RefusedProductsConsumeNoJobSlotAndProductAndViewportJobsShareTheQuota()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        backend.PrepareProduct = times => Plan(backend.Context!.SessionId, backend.StageRevision, times);
        await using var workspace = files.CreateWorkspace(backend);
        using var artifacts = new ArtifactResourceStore();
        var factory = new ProductFactory();
        factory.Source.Refuse = true;
        using var processor = new PreviewCaptureProcessor(factory, artifacts);
        await using var worker = new CaptureWorker(processor);
        using ServiceProvider provider = new ServiceCollection().AddSingleton(worker)
            .AddSingleton<IArtifactResourceStore>(artifacts).BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, Options(files));
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        await Assert.That(async () => await service.RenderProductAsync(Request(session, 1), default))
            .Throws<OpenUsdMcpFailureException>();
        await Assert.That(factory.Source.Times).IsEmpty();
        await Assert.That((await service.ListSequenceJobsAsync(default)).Jobs).IsEmpty();
        factory.Source.Refuse = false;
        _ = await service.RenderProductAsync(Request(session, 1), default);
        for (int index = 0; index < 7; index++)
        {
            _ = await service.RenderSequenceAsync(new RenderSequenceRequest
            {
                SessionId = session.SessionId,
                Generation = session.Generation,
                StageRevision = session.StageRevision,
                FrameCount = 1,
                Width = 2,
                Height = 2
            }, default);
        }
        int preparations = backend.Events.Count(static item => item == "prepare-product");
        await Assert.That(async () => await service.RenderProductAsync(Request(session, 1), default))
            .Throws<WorkspaceQuotaExceededException>();
        await Assert.That(backend.Events.Count(static item => item == "prepare-product")).IsEqualTo(preparations);
        await Assert.That((await service.ListSequenceJobsAsync(default)).Jobs.Count).IsEqualTo(8);
    }

    [Test]
    [Arguments("format")]
    [Arguments("settings")]
    [Arguments("product")]
    [Arguments("camera")]
    [Arguments("time")]
    [Arguments("count")]
    public async Task MalformedProductRequestsAreRejectedBeforeWorkspaceWork(string failure)
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, Options(files));
        var request = new RenderProductCaptureRequest
        {
            SessionId = "unused",
            SettingsPath = failure == "settings" ? "relative" : null,
            ProductPath = failure == "product" ? "relative" : null,
            CameraPath = failure == "camera" ? "relative" : null,
            HdrColorFormat = failure == "format" ? "png" : "raw",
            TimeStep = failure == "time" ? 0 : 1,
            FrameCount = failure == "count" ? 4097 : 1
        };
        await Assert.That(async () => await service.RenderProductAsync(request, default)).Throws<ArgumentException>();
        await Assert.That(backend.Events).IsEmpty();
    }

    private static RenderProductCaptureRequest Request(McpSessionDto session, int count) => new()
    {
        SessionId = session.SessionId,
        Generation = session.Generation,
        StageRevision = session.StageRevision,
        SettingsPath = "/Render/Settings",
        ProductPath = "/Render/Product",
        FrameCount = count,
        StartTimeCode = 2,
        TimeStep = 0.5
    };

    private static RenderProductJobPlan Plan(
        string sessionId, ulong revision, IReadOnlyList<double> times, RenderProductOverrides? overrides = null,
        UsdVec4f? dataWindow = null, float authoredPixelAspectRatio = 1,
        string authoredConformPolicy = "expandAperture")
    {
        var specification = new UsdRenderSpecification("/Render/Settings",
            [new UsdRenderProductSpecification("/Render/Product", "../not-authorized.exr", "raster", "/Camera",
                2, 2, authoredPixelAspectRatio, authoredConformPolicy, new UsdVec2f(20, 20),
                dataWindow ?? new UsdVec4f(0, 0, 1, 1),
                true, true, [1, 0], [])],
            [new UsdRenderVariableSpecification("/Render/Color", "half4", "color", "raw", []),
                new UsdRenderVariableSpecification("/Render/Depth", "float", "depth", "raw", [])],
            ["default", "render"], ["full"], "", []);
        var request = new RenderProductRequest(specification, 0, overrides);
        var optics = new UsdGeomCameraState(
            UsdGeomCameraProjection.Orthographic, -1, 1, -1, 1, 1, 11, 0, 20, 20, 0, 0, 0, 0);
        RenderPreparedFrame[] frames = [.. times.Select(time =>
            request.PrepareFrame(UsdMatrix4d.Identity, optics, time, RenderCameraFrameSettings.Default))];
        return new RenderProductJobPlan(new StageIdentity($"session:{sessionId}"), frames,
            RenderSettings.PresentationDefault, revision);
    }

    private static OpenUsdMcpApplicationOptions Options(WorkspaceTestFiles files) => new(
        files.SourceRoot, files.OutputRoot, files.SourceRoot, files.OutputRoot,
        Path.Combine(files.OutputRoot, "viewer-not-launched.exe"));

    private sealed class ProductFactory : IPreviewFrameSourceFactory
    {
        internal ProductSource Source { get; } = new();
        public IPreviewFrameSource Create(
            PreviewCaptureRequest request, CancellationToken cancellationToken = default) => Source;
    }

    private sealed class ProductSource : IPreviewFrameSource, IPreviewProductFrameSource
    {
        internal bool Refuse { get; set; }
        internal int Validations { get; private set; }
        internal List<double> Times { get; } = [];
        internal RenderPurpose Purposes { get; private set; }

        public void ValidateProduct(RenderProductJobPlan plan, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Validations++;
            if (Refuse)
            {
                throw new NotSupportedException("Controlled unsupported material binding.");
            }
            Purposes = plan.IncludedPurposes;
        }

        public RenderJobImage CaptureProduct(
            RenderProductJobPlan plan, StageRenderState state, long maximumReadbackBytes,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Times.Add(state.Time.TimeCode);
            int width = state.Viewport.Width;
            int height = state.Viewport.Height;
            return new RenderJobImage(width, height, new byte[width * height * 4], Rgba8RowOrder.TopDown)
            {
                HdrColor = new RenderJobHdrColor(width, height, new byte[width * height * 8]),
                DeviceDepth = new RenderJobDeviceDepth(width, height, new float[width * height])
            };
        }

        public ImageRgba8 Capture(CaptureView view, int width, int height) =>
            new(width, height, new byte[width * height * 4]);
        public void Dispose() { }
    }
}
