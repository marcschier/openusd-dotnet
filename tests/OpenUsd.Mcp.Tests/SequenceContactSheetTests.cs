// Copyright (c) marcschier. Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;

namespace OpenUsd.Mcp.Tests;

public sealed class SequenceContactSheetTests
{
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
        private readonly McpSessionWorkspace _workspace;
        private readonly CaptureWorker _worker;
        private readonly PreviewCaptureProcessor _processor;
        private readonly ServiceProvider _provider;

        internal Fixture(int maximumResources = 128)
        {
            _workspace = Files.CreateWorkspace(new RecordingWorkspaceBackend());
            Artifacts = new ArtifactResourceStore(new ArtifactResourceStoreOptions(maximumResources));
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
