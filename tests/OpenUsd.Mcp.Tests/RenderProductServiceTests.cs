// Copyright (c) marcschier. Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using OpenUsd.Geom;
using OpenUsd.Render;
using OpenUsd.Rendering;

namespace OpenUsd.Mcp.Tests;

public sealed class RenderProductServiceTests
{
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
        McpRenderSequenceResultDto job = await service.RenderProductAsync(Request(session, 3), default);
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
        ArtifactResourceContent? hdr = await artifacts.ReadAsync(new Uri(frame.HdrColor!.Uri));
        await Assert.That(hdr!.Content.Length).IsEqualTo(32);
        await Assert.That(backend.Events).DoesNotContain("apply");
        await Assert.That(backend.Events).DoesNotContain("checkpoint");
        await Assert.That(File.Exists(Path.Combine(files.SourceRoot, "not-authorized.exr"))).IsFalse();
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

    private static RenderProductJobPlan Plan(string sessionId, ulong revision, IReadOnlyList<double> times)
    {
        var specification = new UsdRenderSpecification("/Render/Settings",
            [new UsdRenderProductSpecification("/Render/Product", "../not-authorized.exr", "raster", "/Camera",
                2, 2, 1, "expandAperture", new UsdVec2f(20, 20), new UsdVec4f(0, 0, 1, 1),
                true, true, [1, 0], [])],
            [new UsdRenderVariableSpecification("/Render/Color", "half4", "color", "raw", []),
                new UsdRenderVariableSpecification("/Render/Depth", "float", "depth", "raw", [])],
            ["default", "render"], ["full"], "", []);
        var request = new RenderProductRequest(specification, 0);
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
