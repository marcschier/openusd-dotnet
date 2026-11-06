// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenUsd.Rendering;

namespace OpenUsd.Mcp.Tests;

public sealed class SequenceContactSheetTests
{
    [Test]
    [Arguments("automatic")]
    [Arguments("matrices")]
    [Arguments("clip-planes")]
    public async Task HistoricalFrameAndSheetRetainRequestedCamerasAfterSceneRetirement(string mode)
    {
        await using var fixture = new Fixture();
        int planeCount = mode == "clip-planes" ? CameraState.MaxClipPlanes : 0;
        fixture.Backend.PreparePreviewCameras = times => times.Select(time => mode == "automatic"
            ? CameraState.Default
            : new CameraState(
                Matrix4x4.CreateTranslation((float)time + 0.125f, -0.25f, 3.5f),
                Matrix4x4.CreateScale(1.25f, 2.5f, 3.75f),
                Enumerable.Range(0, planeCount).Select(index => new Vector4(index, 0.5f, -1.25f, 2.75f))))
            .ToArray();
        McpRenderSequenceResultDto job = await fixture.RenderAsync(3);
        await fixture.Service.CloseSceneAsync(new SceneRevisionRequest
        {
            SessionId = job.SessionId,
            Generation = job.Generation,
            StageRevision = job.StageRevision
        }, default);
        fixture.Backend.PreparePreviewCameras = _ =>
            throw new InvalidOperationException("Historical reads must not sample the current scene.");
        McpSessionDto replacement = await fixture.Service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        await Assert.That(replacement.SessionId).IsNotEqualTo(job.SessionId);
        McpSequenceFrameResultDto frame = await fixture.Service.ReadSequenceFrameAsync(
            new ReadSequenceFrameRequest { JobId = job.JobId, FrameIndex = 2 }, default);
        McpSequenceSheetResultDto sheet = await fixture.Service.ReadSequenceSheetAsync(
            new ReadSequenceSheetRequest { JobId = job.JobId, FrameIndices = [2, 0], Width = 8, Height = 4 }, default);
        JsonElement frameJson = JsonSerializer.SerializeToElement(frame, JsonSerializerOptions.Web);
        JsonElement sheetJson = JsonSerializer.SerializeToElement(sheet, JsonSerializerOptions.Web);
        JsonElement renderRequest = frameJson.GetProperty("renderRequest");
        await Assert.That(renderRequest.GetProperty("width").GetInt32()).IsEqualTo(2);
        await Assert.That(renderRequest.GetProperty("height").GetInt32()).IsEqualTo(1);
        await Assert.That(renderRequest.GetProperty("purposes").GetString()).IsEqualTo("Default, Proxy, Render");
        await Assert.That(renderRequest.GetProperty("visibility").GetString()).IsEqualTo("RespectAuthored");
        await Assert.That(renderRequest.GetProperty("drawMode").GetString()).IsEqualTo("SmoothShaded");
        await Assert.That(renderRequest.GetProperty("samplesPerPixel").GetInt32()).IsEqualTo(1);
        await Assert.That(renderRequest.GetProperty("enableLighting").GetBoolean()).IsTrue();
        await Assert.That(renderRequest.GetProperty("enableShadows").GetBoolean()).IsTrue();
        await Assert.That(renderRequest.GetProperty("backfaceCulling").GetBoolean()).IsTrue();
        await Assert.That(renderRequest.GetProperty("useSceneMaterials").GetBoolean()).IsTrue();
        await Assert.That(renderRequest.GetProperty("complexity").GetString()).IsEqualTo("Low");
        await Assert.That(renderRequest.GetProperty("outputTransform").GetString()).IsEqualTo("Reinhard");
        await Assert.That(renderRequest.GetProperty("exposure").GetSingle()).IsEqualTo(-6);
        await Assert.That(renderRequest.GetProperty("clearColor").EnumerateArray()
            .Select(static value => value.GetSingle()).SequenceEqual([0, 0, 0, 1])).IsTrue();
        await Assert.That(renderRequest.GetProperty("displayTransform").ValueKind).IsEqualTo(JsonValueKind.Null);
        JsonElement camera = frameJson.GetProperty("cameraRequest");
        await Assert.That(camera.GetProperty("mode").GetString())
            .IsEqualTo(mode == "automatic" ? "automatic" : "matrices");
        await Assert.That(camera.GetProperty("clipPlanes").GetArrayLength()).IsEqualTo(planeCount);
        JsonElement tiles = sheetJson.GetProperty("tiles");
        await Assert.That(tiles[0].GetProperty("renderRequest").GetRawText()).IsEqualTo(renderRequest.GetRawText());
        await Assert.That(tiles[1].GetProperty("renderRequest").GetRawText()).IsEqualTo(renderRequest.GetRawText());
        await Assert.That(tiles[0].GetProperty("frameIndex").GetInt32()).IsEqualTo(2);
        await Assert.That(tiles[1].GetProperty("frameIndex").GetInt32()).IsEqualTo(0);
        if (mode == "automatic")
        {
            await Assert.That(camera.GetProperty("view").ValueKind).IsEqualTo(JsonValueKind.Null);
            await Assert.That(camera.GetProperty("projection").ValueKind).IsEqualTo(JsonValueKind.Null);
            await Assert.That(tiles[1].GetProperty("cameraRequest").GetProperty("view").ValueKind)
                .IsEqualTo(JsonValueKind.Null);
        }
        else
        {
            float[] expectedView = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 2.125f, -0.25f, 3.5f, 1];
            float[] expectedProjection = [1.25f, 0, 0, 0, 0, 2.5f, 0, 0, 0, 0, 3.75f, 0, 0, 0, 0, 1];
            await Assert.That(camera.GetProperty("view").EnumerateArray().Select(static item => item.GetSingle())
                .SequenceEqual(expectedView)).IsTrue();
            await Assert.That(camera.GetProperty("projection").EnumerateArray()
                .Select(static item => item.GetSingle()).SequenceEqual(expectedProjection)).IsTrue();
            await Assert.That(tiles[0].GetProperty("cameraRequest").GetRawText()).IsEqualTo(camera.GetRawText());
            await Assert.That(tiles[1].GetProperty("cameraRequest").GetProperty("view")[12].GetSingle())
                .IsEqualTo(0.125f);
            for (int index = 0; index < planeCount; index++)
            {
                await Assert.That(camera.GetProperty("clipPlanes")[index].EnumerateArray()
                    .Select(static value => value.GetSingle()).SequenceEqual([index, 0.5f, -1.25f, 2.75f])).IsTrue();
            }
        }
        ArtifactResourceContent png = await fixture.Artifacts.ReadAsync(new Uri(frame.Artifact.Uri)) ??
            throw new InvalidOperationException("The original PNG resource is absent.");
        await Assert.That(PngRgba8Decoder.Decode(png.Content.Span).Pixels.Span[0]).IsEqualTo((byte)3);
        await Assert.That(fixture.Source.Calls).IsEqualTo(3);
        await Assert.That(fixture.Artifacts.Count).IsEqualTo(2);
        await Assert.That(frame.SessionId).IsEqualTo(job.SessionId);
        await Assert.That(sheet.Generation).IsEqualTo(job.Generation);
        await Assert.That(sheet.StageRevision).IsEqualTo(job.StageRevision);
    }

    [Test]
    public async Task HistoricalJobsAreDiscoverableAcrossClosedAndNewScenesWithoutReadingOrRendering()
    {
        await using var fixture = new Fixture();
        McpSequenceJobsResultDto empty = await fixture.Service.ListSequenceJobsAsync(default);
        await Assert.That(empty.Jobs).IsEmpty();
        await Assert.That(fixture.Source.Calls).IsEqualTo(0);
        McpRenderSequenceResultDto first = await fixture.RenderAsync(3);
        McpSequenceJobsResultDto original = await fixture.Service.ListSequenceJobsAsync(default);
        await Assert.That(original.Jobs.Count).IsEqualTo(1);
        await Assert.That(original.Jobs[0]).IsSameReferenceAs(first);
        await fixture.Service.CloseSceneAsync(new SceneRevisionRequest
        {
            SessionId = first.SessionId,
            Generation = first.Generation,
            StageRevision = first.StageRevision
        }, default);
        File.Delete(Path.Combine(fixture.Files.OutputRoot, first.OutputDirectory, "frame-000000.png"));
        McpSequenceJobsResultDto retired = await fixture.Service.ListSequenceJobsAsync(default);
        await Assert.That(retired.Jobs[0]).IsSameReferenceAs(first);
        McpRenderSequenceResultDto second = await fixture.RenderAsync(1);
        await Assert.That(second.SessionId).IsNotEqualTo(first.SessionId);
        McpSequenceJobsResultDto current = await fixture.Service.ListSequenceJobsAsync(default);
        await Assert.That(current.Jobs.Select(static job => job.JobId)
            .SequenceEqual(new[] { first.JobId, second.JobId }.Order(StringComparer.Ordinal))).IsTrue();
        await Assert.That(current.Jobs.Single(job => job.JobId == first.JobId)).IsSameReferenceAs(first);
        await Assert.That(current.Jobs.Single(job => job.JobId == second.JobId)).IsSameReferenceAs(second);
        await Assert.That(original.Jobs.Count).IsEqualTo(1);
        await Assert.That(empty.Jobs).IsEmpty();
        await Assert.That(first.AuthoredProduct).IsNull();
        await Assert.That(first.Simulation).IsNull();
        await Assert.That(Path.IsPathRooted(first.OutputDirectory)).IsFalse();
        await Assert.That(Path.IsPathRooted(first.ManifestPath)).IsFalse();
        await Assert.That(async () => await fixture.Service.ReadSequenceSheetAsync(
            new ReadSequenceSheetRequest { JobId = first.JobId, Width = 8, Height = 4 }, default))
            .Throws<FileNotFoundException>();
        await Assert.That(fixture.Source.Calls).IsEqualTo(4);
        await Assert.That(fixture.Artifacts.Count).IsEqualTo(0);
    }

    [Test]
    public async Task CancelledJobListingLeavesTheHistoricalSnapshotAndQuotasUntouched()
    {
        await using var fixture = new Fixture();
        McpRenderSequenceResultDto job = await fixture.RenderAsync(1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.That(async () => await fixture.Service.ListSequenceJobsAsync(cancellation.Token))
            .Throws<OperationCanceledException>();
        McpSequenceJobsResultDto result = await fixture.Service.ListSequenceJobsAsync(default);
        await Assert.That(result.Jobs.Count).IsEqualTo(1);
        await Assert.That(result.Jobs[0]).IsSameReferenceAs(job);
        await Assert.That(fixture.Source.Calls).IsEqualTo(1);
        await Assert.That(fixture.Artifacts.Count).IsEqualTo(0);
    }

    [Test]
    public async Task CompletedFramesKeepOrderAspectAndCapturedRevisionWithoutRerendering()
    {
        await using var fixture = new Fixture();
        McpRenderSequenceResultDto job = await fixture.RenderAsync(20);
        int calls = fixture.Source.Calls;
        await fixture.Service.CloseSceneAsync(new SceneRevisionRequest
        {
            SessionId = job.SessionId,
            Generation = job.Generation,
            StageRevision = job.StageRevision
        }, default);
        McpSequenceSheetResultDto sheet = await fixture.Service.ReadSequenceSheetAsync(new ReadSequenceSheetRequest
        {
            JobId = job.JobId,
            FrameIndices = [19, 0],
            Width = 8,
            Height = 4
        }, default);
        await Assert.That(sheet.SessionId).IsEqualTo(job.SessionId);
        await Assert.That(sheet.StageRevision).IsEqualTo(job.StageRevision);
        await Assert.That(sheet.AuthoredProduct).IsNull();
        await Assert.That(sheet.Tiles.All(static tile => tile.Simulation is null)).IsTrue();
        await Assert.That(sheet.Tiles.Select(static tile => tile.FrameIndex).SequenceEqual([19, 0])).IsTrue();
        await Assert.That(sheet.Tiles[0].TimeCode).IsEqualTo(19d);
        await Assert.That(sheet.Tiles[1].X).IsEqualTo(4);
        ArtifactResourceContent resource = await fixture.Artifacts.ReadAsync(new Uri(sheet.Artifact.Uri)) ??
            throw new InvalidOperationException("The sheet resource is absent.");
        ImageRgba8 image = PngRgba8Decoder.Decode(resource.Content.Span);
        await Assert.That(image.Pixels.Span[(2 * 8 + 1) * 4]).IsEqualTo((byte)20);
        await Assert.That(image.Pixels.Span[(2 * 8 + 5) * 4]).IsEqualTo((byte)1);
        await Assert.That(image.Pixels.Span[3]).IsEqualTo((byte)0);
        await Assert.That(image.Pixels.Span[(2 * 8 + 1) * 4 + 3]).IsEqualTo(byte.MaxValue);
        McpSequenceSheetResultDto sampled = await fixture.Service.ReadSequenceSheetAsync(
            new ReadSequenceSheetRequest { JobId = job.JobId, Width = 16, Height = 16 }, default);
        await Assert.That(sampled.Tiles.Count).IsEqualTo(16);
        await Assert.That(sampled.Tiles[0].FrameIndex).IsEqualTo(0);
        await Assert.That(sampled.Tiles[^1].FrameIndex).IsEqualTo(19);
        await Assert.That(sampled.Tiles.Select(static tile => tile.FrameIndex).Distinct().Count()).IsEqualTo(16);
        await Assert.That(fixture.Source.Calls).IsEqualTo(calls);
        await Assert.That(fixture.Artifacts.Count).IsEqualTo(2);
    }

    [Test]
    public async Task TamperedFrameRejectsTheWholeSheetWithoutRegisteringAnyResource()
    {
        await using var fixture = new Fixture();
        McpRenderSequenceResultDto job = await fixture.RenderAsync(3);
        string path = Path.Combine(fixture.Files.OutputRoot, job.OutputDirectory, "frame-000002.png");
        byte[] original = await File.ReadAllBytesAsync(path);
        byte[] changed = [.. original];
        changed[^1] ^= 1;
        await File.WriteAllBytesAsync(path, changed);
        var request = new ReadSequenceSheetRequest { JobId = job.JobId, Width = 12, Height = 8 };
        await Assert.That(async () => await fixture.Service.ReadSequenceSheetAsync(request, default))
            .Throws<ArtifactResourceIntegrityException>();
        await Assert.That(fixture.Artifacts.Count).IsEqualTo(0);
        await Assert.That(fixture.Source.Calls).IsEqualTo(3);
        await File.WriteAllBytesAsync(path, original);
        _ = await fixture.Service.ReadSequenceSheetAsync(request, default);
        await Assert.That(fixture.Artifacts.Count).IsEqualTo(1);
    }

    [Test]
    public async Task CapacityFailurePreservesExistingResourcesAndDoesNotRender()
    {
        await using var fixture = new Fixture(maximumResources: 1);
        McpRenderSequenceResultDto job = await fixture.RenderAsync(2);
        ArtifactResourceDescriptor existing = fixture.Artifacts.Add("existing", "image/png", new byte[] { 1, 2, 3 });
        await Assert.That(async () => await fixture.Service.ReadSequenceSheetAsync(
            new ReadSequenceSheetRequest { JobId = job.JobId, Width = 8, Height = 4 }, default))
            .Throws<ArtifactResourceStoreCapacityException>();
        await Assert.That(fixture.Artifacts.Count).IsEqualTo(1);
        await Assert.That(fixture.Source.Calls).IsEqualTo(2);
        ArtifactResourceContent resource = await fixture.Artifacts.ReadAsync(existing.ResourceUri) ??
            throw new InvalidOperationException("Existing resource was lost.");
        await Assert.That(resource.Content.ToArray()).IsEquivalentTo(new byte[] { 1, 2, 3 });
    }

    [Test]
    [Arguments("empty")]
    [Arguments("duplicate")]
    [Arguments("negative")]
    [Arguments("count")]
    [Arguments("outside")]
    [Arguments("width")]
    public async Task InvalidSelectionCannotPublishOrRerender(string failure)
    {
        await using var fixture = new Fixture();
        McpRenderSequenceResultDto job = await fixture.RenderAsync(2);
        int[]? indices = failure switch
        {
            "empty" => [],
            "duplicate" => [0, 0],
            "negative" => [-1],
            "count" => Enumerable.Range(0, 17).ToArray(),
            "outside" => [2],
            _ => null
        };
        await Assert.That(async () => await fixture.Service.ReadSequenceSheetAsync(new ReadSequenceSheetRequest
        {
            JobId = job.JobId,
            FrameIndices = indices,
            Width = failure == "width" ? 1025 : 8,
            Height = 8
        }, default)).Throws<ArgumentException>();
        await Assert.That(fixture.Source.Calls).IsEqualTo(2);
        await Assert.That(fixture.Artifacts.Count).IsEqualTo(0);
    }

    [Test]
    public async Task CancelledOrUnknownJobDoesNotTouchCaptureOrResources()
    {
        await using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.That(async () => await fixture.Service.ReadSequenceSheetAsync(
            new ReadSequenceSheetRequest { JobId = "unknown" }, cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(async () => await fixture.Service.ReadSequenceSheetAsync(
            new ReadSequenceSheetRequest { JobId = "unknown" }, default)).Throws<OpenUsdMcpFailureException>();
        await Assert.That(fixture.Source.Calls).IsEqualTo(0);
        await Assert.That(fixture.Artifacts.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SheetsDoNotConsumeTheCompletedRenderJobQuota()
    {
        await using var fixture = new Fixture();
        McpRenderSequenceResultDto first = await fixture.RenderAsync(1);
        for (int index = 0; index < 8; index++)
        {
            McpSequenceJobsResultDto jobs = await fixture.Service.ListSequenceJobsAsync(default);
            await Assert.That(jobs.Jobs.Count).IsEqualTo(1);
            _ = await fixture.Service.ReadSequenceSheetAsync(
                new ReadSequenceSheetRequest { JobId = first.JobId, Width = 4, Height = 4 }, default);
        }
        var request = new RenderSequenceRequest
        {
            SessionId = first.SessionId,
            Generation = first.Generation,
            StageRevision = first.StageRevision,
            Width = 2,
            Height = 1,
            FrameCount = 1
        };
        for (int index = 0; index < 7; index++)
        {
            _ = await fixture.Service.RenderSequenceAsync(request, default);
        }
        await Assert.That(fixture.Source.Calls).IsEqualTo(8);
        await Assert.That(async () => await fixture.Service.RenderSequenceAsync(request, default))
            .Throws<WorkspaceQuotaExceededException>();
        McpSequenceJobsResultDto full = await fixture.Service.ListSequenceJobsAsync(default);
        await Assert.That(full.Jobs.Count).IsEqualTo(8);
        await Assert.That(full.Jobs.Select(static job => job.JobId).Distinct().Count()).IsEqualTo(8);
        await Assert.That(full.Jobs.Single(job => job.JobId == first.JobId)).IsSameReferenceAs(first);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal WorkspaceTestFiles Files { get; } = new();
        internal SourceFactory Source { get; } = new();
        internal ArtifactResourceStore Artifacts { get; }
        internal OpenUsdMcpService Service { get; }
        internal RecordingWorkspaceBackend Backend { get; } = new();
        private readonly McpSessionWorkspace _workspace;
        private readonly CaptureWorker _worker;
        private readonly PreviewCaptureProcessor _processor;
        private readonly ServiceProvider _provider;

        internal Fixture(int maximumResources = 128)
        {
            _workspace = Files.CreateWorkspace(Backend);
            Artifacts = new ArtifactResourceStore(new ArtifactResourceStoreOptions(
                maximumResources, FileStorageRoot: Path.Combine(Files.OutputRoot, "artifacts")));
            _processor = new PreviewCaptureProcessor(Source, Artifacts);
            _worker = new CaptureWorker(_processor);
            _provider = new ServiceCollection().AddSingleton(_worker)
                .AddSingleton<IArtifactResourceStore>(Artifacts).BuildServiceProvider();
            Service = new OpenUsdMcpService(_workspace, _provider,
                new OpenUsdMcpApplicationOptions(Files.SourceRoot, Files.OutputRoot, Files.SourceRoot,
                    Files.OutputRoot, Path.Combine(Files.OutputRoot, "viewer-not-launched.exe")));
        }

        internal async Task<McpRenderSequenceResultDto> RenderAsync(int frames)
        {
            McpSessionDto session = await Service.OpenSceneAsync(
                new OpenSceneRequest { SourcePath = "scene.usda" }, default);
            return await Service.RenderSequenceAsync(new RenderSequenceRequest
            {
                SessionId = session.SessionId,
                Generation = session.Generation,
                StageRevision = session.StageRevision,
                Width = 2,
                Height = 1,
                FrameCount = frames
            }, default);
        }

        public async ValueTask DisposeAsync()
        {
            Service.Dispose();
            await _worker.DisposeAsync();
            _processor.Dispose();
            _provider.Dispose();
            Artifacts.Dispose();
            await _workspace.DisposeAsync();
            Files.Dispose();
        }
    }

    private sealed class SourceFactory : IPreviewFrameSourceFactory, IPreviewFrameSource
    {
        internal int Calls { get; private set; }
        public IPreviewFrameSource Create(
            PreviewCaptureRequest request, CancellationToken cancellationToken = default) => this;
        public ImageRgba8 Capture(CaptureView view, int width, int height)
        {
            Calls++;
            byte[] pixels = new byte[width * height * 4];
            for (int index = 0; index < pixels.Length; index += 4)
            {
                pixels[index] = checked((byte)(view.TimeCode + 1));
                pixels[index + 3] = byte.MaxValue;
            }
            return new ImageRgba8(width, height, pixels);
        }
        public void Dispose()
        {
        }
    }
}
