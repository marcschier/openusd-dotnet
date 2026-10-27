// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenUsd.Rendering;

namespace OpenUsd.Mcp.Tests;

public sealed class OpenUsdMcpServiceTests
{
    [Test]
    [Arguments("still", false)]
    [Arguments("turntable", false)]
    [Arguments("contact_sheet", false)]
    [Arguments("still", true)]
    [Arguments("turntable", true)]
    [Arguments("contact_sheet", true)]
    public async Task PreviewDiagnosticContractReportsProtocolRefusalThenOrderedRetryWithoutChangingScene(
        string kind, bool invalidSeverity)
    {
        using var files = new WorkspaceTestFiles();
        byte[] original = await File.ReadAllBytesAsync(files.SourcePath);
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        using var artifacts = new ArtifactResourceStore();
        bool refuse = true;
        int views = kind == "still" ? 1 : 2;
        var source = new SequenceFrameFactory
        {
            DiagnosticSnapshot = count => new RenderDiagnosticsState(
            [
                new RenderDiagnostic(refuse && count == views && invalidSeverity
                    ? (RenderDiagnosticSeverity)999 : RenderDiagnosticSeverity.Warning,
                    refuse && count == views && !invalidSeverity ? new string('c', 129) : $"view-{count}",
                    "View warning.")
            ])
        };
        using var processor = new PreviewCaptureProcessor(source, artifacts);
        await using var worker = new CaptureWorker(processor);
        using ServiceProvider provider = new ServiceCollection().AddSingleton(worker).BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, CreateOptions(files));
        var tools = new OpenUsdMcpTools(service, new OpenUsdMcpProtocolOptions());
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        var request = new RenderPreviewRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
            Kind = kind,
            Width = kind == "contact_sheet" ? 4 : 2,
            Height = 1,
            Views = Enumerable.Range(0, views)
                .Select(index => new CaptureViewDto { Name = $"view-{index}", TimeCode = index }).ToArray()
        };
        var refused = await tools.RenderPreviewAsync(request, default);
        await Assert.That(refused.IsError).IsTrue();
        await Assert.That(source.Times.Count).IsEqualTo(views);
        await Assert.That(artifacts.Count).IsEqualTo(0);
        await Assert.That(refused.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo(invalidSeverity ? OpenUsdMcpErrorCodes.RenderFailure : OpenUsdMcpErrorCodes.QuotaExceeded);
        await Assert.That((await service.ListSequenceJobsAsync(default)).Jobs).IsEmpty();
        await Assert.That((await workspace.GetStatusAsync()).Session!.StageRevision).IsEqualTo(session.StageRevision);
        refuse = false;
        var retry = await tools.RenderPreviewAsync(request, default);
        await Assert.That(retry.IsError is true).IsFalse();
        JsonElement result = retry.StructuredContent!.Value;
        JsonElement diagnostics = result.GetProperty("diagnostics");
        await Assert.That(diagnostics.GetArrayLength()).IsEqualTo(views);
        for (int index = 0; index < views; index++)
        {
            await Assert.That(diagnostics[index].GetProperty("code").GetString())
                .IsEqualTo($"view-{views + index + 1}");
            await Assert.That(diagnostics[index].GetProperty("severity").GetString()).IsEqualTo("warning");
            await Assert.That(diagnostics[index].GetProperty("message").GetString()).IsEqualTo("View warning.");
        }
        await Assert.That(source.Times.Count).IsEqualTo(views * 2);
        await Assert.That(result.GetProperty("stageRevision").GetUInt64()).IsEqualTo(session.StageRevision);
        foreach (JsonElement artifact in result.GetProperty("artifacts").EnumerateArray())
        {
            ArtifactResourceContent? png = await artifacts.ReadAsync(new Uri(artifact.GetProperty("uri").GetString()!));
            ImageRgba8 image = PngRgba8Decoder.Decode(png!.Content.Span);
            await Assert.That(image.Width).IsEqualTo(request.Width);
            await Assert.That(image.Height).IsEqualTo(request.Height);
            await Assert.That(Convert.ToHexString(image.Pixels.Span)).IsEqualTo(kind == "contact_sheet"
                ? "FF0000FF00FF00FFFF0000FF00FF00FF" : "FF0000FF00FF00FF");
        }
        await Assert.That(artifacts.Count).IsEqualTo(kind == "turntable" ? 2 : 1);
        await Assert.That((await File.ReadAllBytesAsync(files.SourcePath)).SequenceEqual(original)).IsTrue();
    }

    [Test]
    [Arguments(1, 0, -1)]
    [Arguments(1, 0, 1)]
    [Arguments(16, 0, -16)]
    [Arguments(16, 0, -1)]
    [Arguments(16, 0, 1)]
    [Arguments(17, 1, -1)]
    [Arguments(17, 1, 1)]
    public async Task MismatchedSequenceCameraBatchReportsRenderFailureBeforeDiskOutputAndAllowsRetry(
        int frameCount, int failedBatch, int countDelta)
    {
        using var files = new WorkspaceTestFiles();
        byte[] original = await File.ReadAllBytesAsync(files.SourcePath);
        bool mismatch = true;
        var samples = new List<double[]>();
        var backend = new RecordingWorkspaceBackend
        {
            PreparePreviewCameras = times =>
            {
                int batch = samples.Count;
                samples.Add(times.ToArray());
                return Enumerable.Repeat(CameraState.Default,
                    times.Count + (mismatch && batch == failedBatch ? countDelta : 0)).ToArray();
            }
        };
        await using var workspace = files.CreateWorkspace(backend);
        using var artifacts = new ArtifactResourceStore();
        var source = new SequenceFrameFactory();
        using var processor = new PreviewCaptureProcessor(source, artifacts);
        await using var worker = new CaptureWorker(processor);
        using ServiceProvider provider = new ServiceCollection().AddSingleton(worker).BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, CreateOptions(files));
        var tools = new OpenUsdMcpTools(service, new OpenUsdMcpProtocolOptions());
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        var request = new RenderSequenceRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
            Width = 2,
            Height = 1,
            StartTimeCode = 2,
            TimeStep = 0.5,
            FrameCount = frameCount
        };
        double[] expectedTimes = Enumerable.Range(0, frameCount).Select(index => 2 + index * 0.5).ToArray();
        string renderDirectory = Path.Combine(backend.Context!.OutputDirectory, "renders");
        var refused = await tools.RenderSequenceAsync(request, default);
        await Assert.That(refused.IsError).IsTrue();
        await Assert.That(refused.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo(OpenUsdMcpErrorCodes.RenderFailure);
        await Assert.That(samples.Count).IsEqualTo(failedBatch + 1);
        await Assert.That(samples.SelectMany(static batch => batch)
            .SequenceEqual(expectedTimes.Take((failedBatch + 1) * 16))).IsTrue();
        await Assert.That(source.Times).IsEmpty();
        await Assert.That(Directory.Exists(renderDirectory)).IsFalse();
        await Assert.That((await service.ListSequenceJobsAsync(default)).Jobs).IsEmpty();
        await Assert.That(artifacts.Count).IsEqualTo(0);
        await Assert.That((await workspace.GetStatusAsync()).Session!.StageRevision).IsEqualTo(session.StageRevision);

        mismatch = false;
        samples.Clear();
        McpRenderSequenceResultDto retry = await service.RenderSequenceAsync(request, default);
        await Assert.That(Directory.Exists(renderDirectory)).IsTrue();
        await Assert.That(samples.SelectMany(static batch => batch).SequenceEqual(expectedTimes)).IsTrue();
        await Assert.That(samples.All(static batch => batch.Length is > 0 and <= 16)).IsTrue();
        await Assert.That(source.Times.SequenceEqual(expectedTimes)).IsTrue();
        await Assert.That(retry.FrameCount).IsEqualTo(frameCount);
        await Assert.That(retry.StageRevision).IsEqualTo(session.StageRevision);
        string output = Path.Combine(files.OutputRoot, retry.OutputDirectory);
        for (int index = 0; index < frameCount; index++)
        {
            ImageRgba8 image = PngRgba8Decoder.Decode(
                await File.ReadAllBytesAsync(Path.Combine(output, $"frame-{index:D6}.png")));
            await Assert.That(image.Width).IsEqualTo(2);
            await Assert.That(image.Height).IsEqualTo(1);
            await Assert.That(Convert.ToHexString(image.Pixels.Span)).IsEqualTo("FF0000FF00FF00FF");
        }
        await Assert.That((await service.ListSequenceJobsAsync(default)).Jobs)
            .Count().IsEqualTo(1);
        await Assert.That(artifacts.Count).IsEqualTo(0);
        await Assert.That((await File.ReadAllBytesAsync(files.SourcePath)).SequenceEqual(original)).IsTrue();
    }

    [Test]
    [Arguments("still", 0)]
    [Arguments("still", 2)]
    [Arguments("turntable", 0)]
    [Arguments("turntable", 1)]
    [Arguments("turntable", 3)]
    [Arguments("contact_sheet", 0)]
    [Arguments("contact_sheet", 1)]
    [Arguments("contact_sheet", 3)]
    public async Task MismatchedPreviewCameraBatchRefusesBeforeCaptureAndPreservesRetry(string kind, int cameraCount)
    {
        using var files = new WorkspaceTestFiles();
        bool mismatch = true;
        var backend = new RecordingWorkspaceBackend
        {
            PreparePreviewCameras = times => Enumerable.Repeat(
                CameraState.Default, mismatch ? cameraCount : times.Count).ToArray()
        };
        await using var workspace = files.CreateWorkspace(backend);
        using var artifacts = new ArtifactResourceStore();
        var source = new SequenceFrameFactory();
        using var processor = new PreviewCaptureProcessor(source, artifacts);
        await using var worker = new CaptureWorker(processor);
        using ServiceProvider provider = new ServiceCollection().AddSingleton(worker).BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, CreateOptions(files));
        var tools = new OpenUsdMcpTools(service, new OpenUsdMcpProtocolOptions());
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        var request = new RenderPreviewRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
            Kind = kind,
            Width = kind == "contact_sheet" ? 4 : 2,
            Height = 1,
            Views =
            [
                .. Enumerable.Range(0, kind == "still" ? 1 : 2)
                    .Select(index => new CaptureViewDto { Name = $"view-{index}", TimeCode = (index + 1) * 1.25 })
            ]
        };
        var refused = await tools.RenderPreviewAsync(request, default);
        await Assert.That(refused.IsError).IsTrue();
        await Assert.That(refused.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo(OpenUsdMcpErrorCodes.RenderFailure);
        await Assert.That(source.Times).IsEmpty();
        await Assert.That(artifacts.Count).IsEqualTo(0);
        await Assert.That((await workspace.GetStatusAsync()).Session!.StageRevision).IsEqualTo(session.StageRevision);
        mismatch = false;
        var progress = new List<(int, int)>();
        McpCaptureResultDto retry = await service.RenderPreviewAsync(
            request, default, (completed, total) => progress.Add((completed, total)));
        await Assert.That(progress.SequenceEqual(
            Enumerable.Range(1, request.Views.Count).Select(index => (index, request.Views.Count)))).IsTrue();
        int artifactCount = kind == "turntable" ? 2 : 1;
        await Assert.That(retry.Artifacts).Count().IsEqualTo(artifactCount);
        await Assert.That(source.Times.SequenceEqual(request.Views.Select(static view => view.TimeCode))).IsTrue();
        foreach (McpArtifactDto artifact in retry.Artifacts)
        {
            ArtifactResourceContent? png = await artifacts.ReadAsync(new Uri(artifact.Uri));
            ImageRgba8 image = PngRgba8Decoder.Decode(png!.Content.Span);
            await Assert.That(image.Width).IsEqualTo(request.Width);
            await Assert.That(image.Height).IsEqualTo(request.Height);
            await Assert.That(Convert.ToHexString(image.Pixels.Span)).IsEqualTo(kind == "contact_sheet"
                ? "FF0000FF00FF00FFFF0000FF00FF00FF" : "FF0000FF00FF00FF");
        }
        await Assert.That(retry.StageRevision).IsEqualTo(session.StageRevision);
        await Assert.That(artifacts.Count).IsEqualTo(artifactCount);
    }

    [Test]
    [Arguments(1, 0)]
    [Arguments(2, 0)]
    [Arguments(3, 0)]
    [Arguments(5, 1)]
    [Arguments(16, 1)]
    public async Task ContactSheetCamerasUseTheActualTileRasterInsteadOfTheWholeSheet(int count, int padding)
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        using var artifacts = new ArtifactResourceStore();
        var source = new SequenceFrameFactory();
        using var processor = new PreviewCaptureProcessor(source, artifacts);
        await using var worker = new CaptureWorker(processor);
        using ServiceProvider provider = new ServiceCollection().AddSingleton(worker).BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, CreateOptions(files));
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        int columns = (int)Math.Ceiling(Math.Sqrt(count));
        int rows = (count + columns - 1) / columns;
        int width = columns * 2 + padding;
        McpCaptureResultDto result = await service.RenderPreviewAsync(new RenderPreviewRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
            Kind = "contact_sheet",
            CameraPath = "/World/Camera",
            Width = width,
            Height = rows,
            Views = Enumerable.Range(0, count)
                .Select(index => new CaptureViewDto { Name = $"view-{index}", TimeCode = index + 0.25 }).ToArray()
        }, default);
        await Assert.That(backend.LastPreviewWidth).IsEqualTo(2);
        await Assert.That(backend.LastPreviewHeight).IsEqualTo(1);
        await Assert.That(source.Times.SequenceEqual(
            Enumerable.Range(0, count).Select(index => index + 0.25))).IsTrue();
        ArtifactResourceContent? png = await artifacts.ReadAsync(new Uri(result.Artifacts.Single().Uri));
        ImageRgba8 image = PngRgba8Decoder.Decode(png!.Content.Span);
        byte[] expected = new byte[width * rows * 4];
        for (int index = 0; index < count; index++)
        {
            byte[] tile = [255, 0, 0, 255, 0, 255, 0, 255];
            tile.CopyTo(expected, (index / columns * width + index % columns * 2) * 4);
        }
        await Assert.That(image.Pixels.ToArray().SequenceEqual(expected)).IsTrue();
        await Assert.That(result.Width).IsEqualTo(width);
        await Assert.That(result.Height).IsEqualTo(rows);
        await Assert.That(result.StageRevision).IsEqualTo(session.StageRevision);
        await Assert.That(artifacts.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(2, true)]
    [Arguments(4, false)]
    [Arguments(5, true)]
    [Arguments(5, false)]
    [Arguments(16, true)]
    [Arguments(16, false)]
    public async Task InvalidContactSheetGridRefusesBeforeCameraPreparation(int count, bool narrow)
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend
        {
            PreparePreviewCameras = _ => throw new InvalidOperationException("Invalid grid reached scene sampling.")
        };
        await using var workspace = files.CreateWorkspace(backend);
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, CreateOptions(files));
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        int columns = (int)Math.Ceiling(Math.Sqrt(count));
        int rows = (count + columns - 1) / columns;
        var request = new RenderPreviewRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
            Kind = "contact_sheet",
            Width = narrow ? columns - 1 : columns,
            Height = narrow ? rows : rows - 1,
            Views = Enumerable.Range(0, count)
                .Select(index => new CaptureViewDto { Name = $"view-{index}" }).ToArray()
        };
        await Assert.That(async () => await service.RenderPreviewAsync(request, default)).Throws<ArgumentException>();
        var tools = new OpenUsdMcpTools(service, new OpenUsdMcpProtocolOptions());
        var protocol = await tools.RenderPreviewAsync(request, default);
        await Assert.That(protocol.IsError).IsTrue();
        await Assert.That(protocol.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("invalid_argument");
        await Assert.That(backend.Events).DoesNotContain("create-preview-cameras");
        await Assert.That(backend.AcquireRenderSourceCount).IsEqualTo(0);
        await Assert.That((await workspace.GetStatusAsync()).Session!.StageRevision).IsEqualTo(session.StageRevision);
    }

    [Test]
    [Arguments(8)]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(16)]
    public async Task FinalizationReturnsEveryFailureAtAndBeyondThePreviewArtifactLimit(int turntableFrames)
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend { ExportError = new IOException("Export refused.") };
        await using var workspace = files.CreateWorkspace(backend);
        using var artifacts = new ArtifactResourceStore(new ArtifactResourceStoreOptions(
            MaximumResourceCount: turntableFrames + 2,
            MaximumReadResponseBytes: 1,
            FileStorageRoot: Path.Combine(files.OutputRoot, "artifacts")));
        using var processor = new PreviewCaptureProcessor(new SequenceFrameFactory(), artifacts);
        await using var worker = new CaptureWorker(processor);
        using ServiceProvider provider = new ServiceCollection()
            .AddSingleton(worker)
            .AddSingleton(new FinalizationService(workspace, artifacts))
            .BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, CreateOptions(files));
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        byte[] sourceBefore = await File.ReadAllBytesAsync(files.SourcePath);
        foreach (string kind in new[] { "still", "contact_sheet", "turntable" })
        {
            int count = kind == "turntable" ? turntableFrames : 1;
            _ = await service.RenderPreviewAsync(new RenderPreviewRequest
            {
                SessionId = session.SessionId,
                Generation = session.Generation,
                StageRevision = session.StageRevision,
                Kind = kind,
                Width = 2,
                Height = 1,
                Views = Enumerable.Range(0, count)
                    .Select(index => new CaptureViewDto { Name = $"view-{index}" }).ToArray()
            }, default);
        }
        McpFinalizationResultDto result = await service.FinalizeSceneAsync(new SceneRevisionRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision
        }, default);
        string manifestPath = Directory.GetFiles(
            files.OutputRoot, "finalization-manifest.json", SearchOption.AllDirectories).Single();
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(manifestPath));
        JsonElement failures = manifest.RootElement.GetProperty("failures");
        string[] expected = failures.EnumerateArray().Select(item =>
            $"{item.GetProperty("role").GetString()}: {item.GetProperty("message").GetString()}").ToArray();
        await Assert.That(expected.Length).IsEqualTo(turntableFrames + 7);
        await Assert.That(result.Failures.SequenceEqual(expected)).IsTrue();
        await Assert.That(result.Summary).IsEqualTo($"Finalization completed with {expected.Length} failure(s).");
        await Assert.That(result.Partial).IsTrue();
        await Assert.That(result.FinalStageCreated).IsFalse();
        await Assert.That(result.Artifacts).IsEmpty();
        await Assert.That(result.StageRevision).IsEqualTo(session.StageRevision);
        await Assert.That(artifacts.Count).IsEqualTo(turntableFrames + 2);
        var tools = new OpenUsdMcpTools(service, new OpenUsdMcpProtocolOptions());
        var protocol = await tools.FinalizeSceneAsync(new SceneRevisionRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision
        }, default);
        await Assert.That(protocol.IsError).IsFalse();
        await Assert.That(protocol.StructuredContent!.Value.GetProperty("failures").EnumerateArray()
            .Select(item => item.GetString()).SequenceEqual(expected)).IsTrue();
        await Assert.That(Directory.GetFiles(
            files.OutputRoot, "finalization-manifest.json", SearchOption.AllDirectories).Length).IsEqualTo(1);
        await Assert.That(await File.ReadAllBytesAsync(files.SourcePath)).IsEquivalentTo(sourceBefore);
        await Assert.That((await workspace.GetStatusAsync()).IsActive).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(123)]
    [Arguments(124)]
    [Arguments(125)]
    [Arguments(128)]
    public async Task AnalysisPagesExposeEveryFindingAtAndAroundTheProtocolLimitWithoutMutation(int issueCount)
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, CreateOptions(files));
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        string[] issues = Enumerable.Range(0, issueCount).Select(index => $"finding-{index:D3}").ToArray();
        var observations = new AnalysisObservationsDto { ValidationIssues = issues };
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var orderedIds = new List<string>();
        var findings = new List<string>();
        int offset = 0;
        int total = issueCount + 4;
        do
        {
            McpAnalysisResultDto page = await service.AnalyzeSceneAsync(request(offset), default);
            JsonElement json = JsonSerializer.SerializeToElement(page);
            await Assert.That(json.GetProperty("totalProposalCount").GetInt32()).IsEqualTo(total);
            await Assert.That(json.GetProperty("proposalOffset").GetInt32()).IsEqualTo(offset);
            await Assert.That(page.Proposals.Count).IsEqualTo(Math.Min(128, total - offset));
            await Assert.That(page.Generation).IsEqualTo(session.Generation);
            await Assert.That(page.StageRevision).IsEqualTo(session.StageRevision);
            foreach (McpProposalDto proposal in page.Proposals)
            {
                await Assert.That(ids.Add(proposal.Id)).IsTrue();
                orderedIds.Add(proposal.Id);
                if (proposal.Category == "validation")
                {
                    findings.Add(proposal.Explanation);
                }
            }
            offset += page.Proposals.Count;
            JsonElement next = json.GetProperty("nextProposalOffset");
            if (offset == total)
            {
                await Assert.That(next.ValueKind).IsEqualTo(JsonValueKind.Null);
                break;
            }
            await Assert.That(next.GetInt32()).IsEqualTo(offset);
        }
        while (offset < total);
        await Assert.That(ids.Count).IsEqualTo(total);
        await Assert.That(findings.SequenceEqual(issues)).IsTrue();
        McpAnalysisResultDto interior = await service.AnalyzeSceneAsync(request(1), default);
        await Assert.That(interior.Proposals.Select(static proposal => proposal.Id)
            .SequenceEqual(orderedIds.Skip(1).Take(128))).IsTrue();
        McpAnalysisResultDto end = await service.AnalyzeSceneAsync(request(total), default);
        await Assert.That(end.Proposals.Count).IsEqualTo(0);
        await Assert.That(JsonSerializer.SerializeToElement(end).GetProperty("nextProposalOffset").ValueKind)
            .IsEqualTo(JsonValueKind.Null);
        await Assert.That(async () => await service.AnalyzeSceneAsync(request(total + 1), default))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(async () => await service.AnalyzeSceneAsync(request(-1), default))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(async () => await service.AnalyzeSceneAsync(request(int.MaxValue), default))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(backend.Events).DoesNotContain("checkpoint");
        await Assert.That(backend.Events).DoesNotContain("apply");
        await Assert.That(backend.AcquireRenderSourceCount).IsEqualTo(0);

        AnalyzeSceneRequest request(int start) => new()
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
            Observations = observations,
            ProposalOffset = start
        };
    }

    [Test]
    [Arguments("silk")]
    [Arguments("storm")]
    public async Task AnalysisRetainsRecommendationEvidenceAndAdvisoryIdentityWithoutApplyingChanges(string renderer)
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, CreateOptions(files));
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        var request = new AnalyzeSceneRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
            Observations = new AnalysisObservationsDto
            {
                RendererId = renderer,
                SubjectCoverage = 0.1,
                NearClip = 10,
                FarClip = 100,
                NearestGeometryDistance = 1,
                FarthestGeometryDistance = 120,
                SamplesPerPixel = 1,
                ShadowsEnabled = false,
                FrameMilliseconds = 50,
                ValidationIssues = ["review this finding"]
            }
        };
        McpAnalysisResultDto analysis = await service.AnalyzeSceneAsync(request, default);
        var tools = new OpenUsdMcpTools(service, new OpenUsdMcpProtocolOptions());
        var response = await tools.AnalyzeSceneAsync(request, default);
        await Assert.That(response.IsError).IsFalse();
        await Assert.That(response.StructuredContent.HasValue).IsTrue();
        JsonElement result = response.StructuredContent!.Value;
        await Assert.That(result.GetRawText()).IsEqualTo(JsonSerializer.Serialize(analysis));
        JsonElement[] proposals = result.GetProperty("proposals").EnumerateArray().ToArray();
        JsonElement clipping = proposals.Single(item => item.GetProperty("code").GetString() == "camera.clipping");
        await Assert.That(clipping.GetProperty("operation").GetString()).IsEqualTo("inspect-camera-clipping");
        JsonElement[] arguments = clipping.GetProperty("arguments").EnumerateArray().ToArray();
        await Assert.That(arguments.Length).IsEqualTo(2);
        await Assert.That(arguments[0].GetProperty("name").GetString()).IsEqualTo("far");
        await Assert.That(arguments[1].GetProperty("name").GetString()).IsEqualTo("near");
        await Assert.That(arguments[0].GetProperty("value").GetString()).IsEqualTo("132");
        await Assert.That(arguments[1].GetProperty("value").GetString()).IsEqualTo("0.5");
        JsonElement near = clipping.GetProperty("evidence").EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == "nearClip");
        await Assert.That(near.GetProperty("value").GetString()).IsEqualTo("10");
        await Assert.That(clipping.GetProperty("advisory").GetBoolean()).IsFalse();
        await Assert.That(clipping.GetProperty("rendererId").ValueKind).IsEqualTo(JsonValueKind.Null);
        JsonElement framing = proposals.Single(item => item.GetProperty("code").GetString() == "camera.framing");
        JsonElement metric = framing.GetProperty("expectedMetrics")[0];
        await Assert.That(metric.GetProperty("name").GetString()).IsEqualTo("subjectCoverage");
        await Assert.That(metric.GetProperty("current").GetDouble()).IsEqualTo(0.1);
        await Assert.That(metric.GetProperty("expected").GetDouble()).IsEqualTo(0.65);
        await Assert.That(metric.GetProperty("unit").GetString()).IsEqualTo("ratio");
        JsonElement quality = proposals.Single(item =>
            item.GetProperty("code").GetString() == "render.quality-alternative");
        await Assert.That(quality.GetProperty("advisory").GetBoolean()).IsTrue();
        await Assert.That(quality.GetProperty("rendererId").GetString()).IsEqualTo(renderer);
        await Assert.That(quality.GetProperty("arguments").EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == "samplesPerPixel")
            .GetProperty("value").GetString()).IsEqualTo("8");
        JsonElement validation = proposals.Single(item =>
            item.GetProperty("code").GetString() == "validation.issue.0000");
        await Assert.That(validation.GetProperty("expectedMetrics").GetArrayLength()).IsEqualTo(0);
        await Assert.That(validation.GetProperty("evidence")[0].GetProperty("value").GetString())
            .IsEqualTo("review this finding");
        JsonElement composition = proposals.Single(item =>
            item.GetProperty("code").GetString() == "composition.unavailable");
        await Assert.That(composition.GetProperty("operation").GetString()).IsEqualTo("inspect-composition-summary");
        await Assert.That(composition.GetProperty("arguments").GetArrayLength()).IsEqualTo(0);
        await Assert.That(composition.GetProperty("expectedMetrics").GetArrayLength()).IsEqualTo(0);
        await Assert.That(composition.GetProperty("evidence").GetArrayLength()).IsEqualTo(1);
        await Assert.That(composition.GetProperty("evidence")[0].GetProperty("name").GetString())
            .IsEqualTo("availability");
        await Assert.That(composition.GetProperty("evidence")[0].GetProperty("value").GetString())
            .IsEqualTo("not-collected");
        await Assert.That(proposals.Any(item => item.GetProperty("code").GetString() is
            "composition.summary" or "composition.muted-layers" or "composition.deep-graph")).IsFalse();
        await Assert.That(proposals.All(item => item.GetProperty("applicability").GetString() == "diagnostic_only"))
            .IsTrue();
        await Assert.That(JsonSerializer.Serialize(analysis))
            .IsEqualTo(JsonSerializer.Serialize(await service.AnalyzeSceneAsync(request, default)));
        await Assert.That(analysis.Generation).IsEqualTo(session.Generation);
        await Assert.That(analysis.StageRevision).IsEqualTo(session.StageRevision);
        await Assert.That(backend.Events).DoesNotContain("checkpoint");
        await Assert.That(backend.Events).DoesNotContain("apply");
        await Assert.That(backend.AcquireRenderSourceCount).IsEqualTo(0);
    }

    [Test]
    [Arguments("changed", "none")]
    [Arguments("missing", "none")]
    [Arguments("cached-color", "none")]
    [Arguments("changed", "raw")]
    [Arguments("missing", "raw")]
    [Arguments("cached-color", "raw")]
    [Arguments("changed", "exr")]
    [Arguments("missing", "exr")]
    [Arguments("cached-color", "exr")]
    public async Task PlaneIntegrityFailureLeavesExistingResourcesAndRetryCapacityIntact(string failure, string format)
    {
        RequireExrFormatRuntime(format);
        bool hdr = format != "none";
        using var files = new WorkspaceTestFiles();
        await using var workspace = files.CreateWorkspace(new RecordingWorkspaceBackend());
        using var artifacts = new ArtifactResourceStore(new ArtifactResourceStoreOptions(
            MaximumResourceCount: hdr ? 4 : 3, FileStorageRoot: Path.Combine(files.OutputRoot, "artifacts")));
        ArtifactResourceDescriptor original = artifacts.Add("original", "application/octet-stream", new byte[] { 42 });
        using var processor = new PreviewCaptureProcessor(new SequenceFrameFactory(), artifacts);
        await using var worker = new CaptureWorker(processor);
        using ServiceProvider provider = new ServiceCollection().AddSingleton(worker)
            .AddSingleton<IArtifactResourceStore>(artifacts).BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, CreateOptions(files));
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        McpRenderSequenceResultDto job = await service.RenderSequenceAsync(new RenderSequenceRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
            Width = 2,
            Height = 1,
            FrameCount = 1,
            IncludeDeviceDepth = true,
            IncludeHdrColor = hdr,
            HdrColorFormat = format == "exr" ? "exr" : "raw"
        }, default);
        string output = Path.Combine(files.OutputRoot, job.OutputDirectory);
        string depthPath = Path.Combine(output, "frame-000000.device-depth.f32");
        string hdrPath = Path.Combine(output, format == "exr" ? "frame-000000.hdr.exr" : "frame-000000.hdr.rgba16f");
        string changedPath = hdr ? hdrPath : depthPath;
        byte[] originalPlane = await File.ReadAllBytesAsync(changedPath);
        if (failure == "cached-color")
        {
            using JsonDocument manifest = JsonDocument.Parse(
                await File.ReadAllBytesAsync(Path.Combine(output, "manifest.json")));
            JsonElement frame = manifest.RootElement.GetProperty("frames")[0];
            await artifacts.AddVerifiedFileAsync($"sequence-{job.JobId}-000000.png", "image/png",
                Path.Combine(output, "frame-000000.png"),
                frame.GetProperty("bytes").GetInt64(), frame.GetProperty("sha256").GetString()!);
        }
        int beforeCount = artifacts.Count;
        long beforeBytes = artifacts.TotalBytes;
        if (failure == "missing")
        {
            File.Delete(changedPath);
        }
        else
        {
            await File.WriteAllBytesAsync(changedPath, new byte[originalPlane.Length]);
        }
        var read = new ReadSequenceFrameRequest { JobId = job.JobId, FrameIndex = 0 };
        await Assert.That(async () => await service.ReadSequenceFrameAsync(read, default)).Throws<IOException>();
        await Assert.That(artifacts.Count).IsEqualTo(beforeCount);
        await Assert.That(artifacts.TotalBytes).IsEqualTo(beforeBytes);
        await Assert.That((await artifacts.ReadAsync(original.ResourceUri))!.Content.Span[0]).IsEqualTo((byte)42);

        await File.WriteAllBytesAsync(changedPath, originalPlane);
        McpSequenceFrameResultDto complete = await service.ReadSequenceFrameAsync(read, default);
        await Assert.That(artifacts.Count).IsEqualTo(hdr ? 4 : 3);
        await Assert.That(complete.DeviceDepth).IsNotNull();
        ArtifactResourceContent? depth = await artifacts.ReadAsync(new Uri(complete.DeviceDepth!.Uri));
        await Assert.That(Convert.ToHexString(depth!.Content.Span)).IsEqualTo("0000803E0000803F");
        if (hdr)
        {
            await Assert.That(complete.HdrColor).IsNotNull();
            ArtifactResourceContent? raw = await artifacts.ReadAsync(new Uri(complete.HdrColor!.Uri));
            byte[] half = format == "exr"
                ? ExrScanlineOracle.Read(raw!.Content.ToArray(), 2, 1) : raw!.Content.ToArray();
            await Assert.That(Convert.ToHexString(half)).IsEqualTo("005400000000003C000000540000003C");
            await Assert.That(complete.HdrColorFormat).IsEqualTo(format);
            await Assert.That(complete.HdrColor.MimeType)
                .IsEqualTo(format == "exr" ? "image/x-exr" : "application/octet-stream");
            File.Delete(hdrPath);
        }
        long completedBytes = artifacts.TotalBytes;
        File.Delete(depthPath);
        File.Delete(Path.Combine(output, "frame-000000.png"));
        McpSequenceFrameResultDto cached = await service.ReadSequenceFrameAsync(read, default);
        await Assert.That(cached.Artifact.Sha256).IsEqualTo(complete.Artifact.Sha256);
        await Assert.That(cached.DeviceDepth!.Sha256).IsEqualTo(complete.DeviceDepth.Sha256);
        await Assert.That(cached.HdrColor?.Sha256).IsEqualTo(complete.HdrColor?.Sha256);
        await Assert.That(artifacts.TotalBytes).IsEqualTo(completedBytes);
    }

    [Test]
    [Arguments("none")]
    [Arguments("raw")]
    [Arguments("exr")]
    public async Task AdditionalPlaneQuotaFailureDoesNotPublishOrChargeAPartialFrame(string format)
    {
        RequireExrFormatRuntime(format);
        bool hdr = format != "none";
        using var files = new WorkspaceTestFiles();
        await using var workspace = files.CreateWorkspace(new RecordingWorkspaceBackend());
        using var artifacts = new ArtifactResourceStore(new ArtifactResourceStoreOptions(
            FileStorageRoot: Path.Combine(files.OutputRoot, "artifacts")));
        int existingCount = hdr ? 126 : 127;
        for (int index = 0; index < existingCount; index++)
        {
            artifacts.Add($"existing-{index}", "application/octet-stream", new byte[] { 42 });
        }
        using var processor = new PreviewCaptureProcessor(new SequenceFrameFactory(), artifacts);
        await using var worker = new CaptureWorker(processor);
        using ServiceProvider provider = new ServiceCollection().AddSingleton(worker)
            .AddSingleton<IArtifactResourceStore>(artifacts).BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, CreateOptions(files));
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        McpRenderSequenceResultDto job = await service.RenderSequenceAsync(new RenderSequenceRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
            Width = 2,
            Height = 1,
            FrameCount = 1,
            IncludeDeviceDepth = true,
            IncludeHdrColor = hdr,
            HdrColorFormat = format == "exr" ? "exr" : "raw"
        }, default);

        await Assert.That(async () => await service.ReadSequenceFrameAsync(
            new ReadSequenceFrameRequest { JobId = job.JobId, FrameIndex = 0 }, default))
            .Throws<ArtifactResourceStoreCapacityException>();

        await Assert.That(artifacts.Count).IsEqualTo(existingCount);
        await Assert.That(artifacts.TotalBytes).IsEqualTo((long)existingCount);
        await Assert.That(artifacts.TryGetDescriptor(
            ArtifactResourceUri.Create($"sequence-{job.JobId}-000000.png"), out _)).IsFalse();
        ArtifactResourceContent? existing = await artifacts.ReadAsync(ArtifactResourceUri.Create("existing-0"));
        await Assert.That(existing!.Content.Span[0]).IsEqualTo((byte)42);
    }

    [Test]
    [Arguments("", true)]
    [Arguments("png", true)]
    [Arguments("EXR", true)]
    [Arguments("exr", false)]
    public async Task InvalidHdrFormatIsRefusedBeforeAnySceneWork(string format, bool includeHdr)
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, CreateOptions(files));
        await Assert.That(async () => await service.RenderSequenceAsync(new RenderSequenceRequest
        {
            SessionId = "unused",
            IncludeHdrColor = includeHdr,
            HdrColorFormat = format
        }, default)).Throws<ArgumentException>();
        await Assert.That(backend.Events).IsEmpty();
    }

    private static void RequireExrFormatRuntime(string format)
    {
        if (format == "exr" && Environment.GetEnvironmentVariable("OPENUSD_EXR_EXECUTION_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_EXR_EXECUTION_REQUIRED=1 with the matching Windows x64 native runtime.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
    }

    [Test]
    [Arguments(4096, 4096, false)]
    [Arguments(2048, 1639, false)]
    [Arguments(4096, 4096, true)]
    [Arguments(2048, 1639, true)]
    public async Task ExtendedSequenceBudgetIsRefusedBeforeOpeningOrInspectingTheScene(int width, int height, bool hdr)
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, CreateOptions(files));
        await Assert.That(async () => await service.RenderSequenceAsync(new RenderSequenceRequest
        {
            SessionId = "unused",
            Width = width,
            Height = height,
            FrameCount = 1,
            IncludeDeviceDepth = !hdr,
            IncludeHdrColor = hdr
        }, default)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(backend.Events).IsEmpty();
    }

    [Test]
    public async Task ImageSequencesUseBoundedDiskOutputInsteadOfThePreviewArtifactStore()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        var artifacts = new ArtifactResourceStore(new ArtifactResourceStoreOptions(
            FileStorageRoot: Path.Combine(files.OutputRoot, "artifacts")));
        var frameSource = new SequenceFrameFactory();
        using var processor = new PreviewCaptureProcessor(frameSource, artifacts);
        await using var worker = new CaptureWorker(processor);
        using ServiceProvider provider = new ServiceCollection().AddSingleton(worker)
            .AddSingleton<IArtifactResourceStore>(artifacts).BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, CreateOptions(files));
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        var progress = new List<(int, int)>();
        McpRenderSequenceResultDto result = await service.RenderSequenceAsync(new RenderSequenceRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
            Width = 2,
            Height = 1,
            StartTimeCode = 2,
            TimeStep = 0.5,
            FrameCount = 20
        }, default, (completed, total) => progress.Add((completed, total)));

        await Assert.That(progress.SequenceEqual(Enumerable.Range(1, 20).Select(index => (index, 20)))).IsTrue();
        await Assert.That(result.FrameCount).IsEqualTo(20);
        await Assert.That(result.Generation).IsEqualTo(session.Generation);
        await Assert.That(result.StageRevision).IsEqualTo(session.StageRevision);
        await Assert.That(Path.IsPathFullyQualified(result.OutputDirectory)).IsFalse();
        string output = Path.Combine(files.OutputRoot, result.OutputDirectory);
        await Assert.That(Directory.GetFiles(output, "*.png").Length).IsEqualTo(20);
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(
            Path.Combine(output, "manifest.json")));
        await Assert.That(manifest.RootElement.GetProperty("frames")[19].GetProperty("timeCode").GetDouble())
            .IsEqualTo(11.5d);
        await Assert.That(frameSource.Times.Count).IsEqualTo(20);
        await Assert.That(frameSource.Times[0]).IsEqualTo(2d);
        await Assert.That(frameSource.Times[^1]).IsEqualTo(11.5d);
        await Assert.That(frameSource.Threads.Distinct().Count()).IsEqualTo(1);
        await Assert.That(artifacts.Count).IsEqualTo(0);
        await Assert.That(artifacts.TotalBytes).IsEqualTo(0L);
        McpSequenceFrameResultDto selected = await service.ReadSequenceFrameAsync(new ReadSequenceFrameRequest
        {
            JobId = result.JobId,
            FrameIndex = 19
        }, default);
        await Assert.That(selected.FrameIndex).IsEqualTo(19);
        await Assert.That(selected.TimeCode).IsEqualTo(11.5d);
        await Assert.That(selected.Artifact.Sha256).IsEqualTo(
            manifest.RootElement.GetProperty("frames")[19].GetProperty("sha256").GetString());
        ArtifactResourceContent? captured = await artifacts.ReadAsync(new Uri(selected.Artifact.Uri));
        await Assert.That(PngRgba8Decoder.Decode(captured!.Content.Span).Width).IsEqualTo(2);
        await Assert.That(artifacts.Count).IsEqualTo(1);

        await File.WriteAllBytesAsync(Path.Combine(output, "frame-000000.png"), [0, 1, 2, 3]);
        await Assert.That(async () => await service.ReadSequenceFrameAsync(new ReadSequenceFrameRequest
        {
            JobId = result.JobId,
            FrameIndex = 0
        }, default)).Throws<ArtifactResourceIntegrityException>();
        await Assert.That(artifacts.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SequenceCancellationKeepsTheSceneGateUntilActualRenderingDrains()
    {
        using var files = new WorkspaceTestFiles();
        await using var workspace = files.CreateWorkspace(new RecordingWorkspaceBackend());
        var artifacts = new ArtifactResourceStore();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameSource = new SequenceFrameFactory
        {
            OnCapture = count =>
            {
                if (count == 2)
                {
                    entered.TrySetResult();
                    if (!release.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("The test did not release its renderer.");
                    }
                }
            }
        };
        using var processor = new PreviewCaptureProcessor(frameSource, artifacts);
        await using var worker = new CaptureWorker(processor);
        using ServiceProvider provider = new ServiceCollection().AddSingleton(worker).BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, CreateOptions(files));
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        using var cancellation = new CancellationTokenSource();
        Task<McpRenderSequenceResultDto> job = service.RenderSequenceAsync(new RenderSequenceRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
            Width = 2,
            Height = 1,
            FrameCount = 3
        }, cancellation.Token).AsTask();
        Task<McpSessionDto>? read = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            read = service.GetSceneAsync(new SceneRevisionRequest
            {
                SessionId = session.SessionId,
                Generation = session.Generation,
                StageRevision = session.StageRevision
            }, default).AsTask();
            Task deadline = Task.Delay(100);
            await Assert.That(await Task.WhenAny(job, read, deadline)).IsSameReferenceAs(deadline);
        }
        finally
        {
            release.Set();
        }
        await Assert.That(async () => await job).Throws<OperationCanceledException>();
        await Assert.That((await read!).StageRevision).IsEqualTo(session.StageRevision);
        string renders = Path.Combine(files.OutputRoot, session.SessionId, "renders");
        await Assert.That(Directory.GetFileSystemEntries(renders)).IsEmpty();
        await Assert.That(frameSource.Times.Count).IsEqualTo(2);
    }

    [Test]
    public async Task SequenceRequestsRespectRevisionAndProcessJobQuotasBeforeRendering()
    {
        using var files = new WorkspaceTestFiles();
        await using var workspace = files.CreateWorkspace(new RecordingWorkspaceBackend());
        var frameSource = new SequenceFrameFactory();
        using var processor = new PreviewCaptureProcessor(frameSource, new ArtifactResourceStore());
        await using var worker = new CaptureWorker(processor);
        using ServiceProvider provider = new ServiceCollection().AddSingleton(worker).BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, CreateOptions(files));
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        var stale = new RenderSequenceRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation + 1,
            StageRevision = session.StageRevision,
            Width = 2,
            Height = 1,
            FrameCount = 1
        };
        OpenUsdMcpFailureException? rejection = await Assert.That(
            async () => await service.RenderSequenceAsync(stale, default)).Throws<OpenUsdMcpFailureException>();
        await Assert.That(rejection!.Code).IsEqualTo(OpenUsdMcpErrorCodes.StaleRevision);
        await Assert.That(frameSource.Times).IsEmpty();
        var request = new RenderSequenceRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
            Width = 2,
            Height = 1,
            FrameCount = 1
        };
        for (int index = 0; index < 8; index++)
        {
            _ = await service.RenderSequenceAsync(request, default);
        }
        await Assert.That(async () => await service.RenderSequenceAsync(request, default))
            .Throws<WorkspaceQuotaExceededException>();
        await Assert.That(frameSource.Times.Count).IsEqualTo(8);
        await Assert.That(Directory.GetDirectories(
            Path.Combine(files.OutputRoot, session.SessionId, "renders")).Length).IsEqualTo(8);
    }

    private sealed class SequenceFrameFactory
        : IPreviewFrameSourceFactory, IPreviewFrameSource, IPreviewDepthFrameSource, IPreviewHdrFrameSource,
            IPreviewDiagnosticSource
    {
        internal List<double> Times { get; } = [];
        internal List<int> Threads { get; } = [];
        internal Action<int>? OnCapture { get; init; }
        internal Func<int, RenderDiagnosticsState>? DiagnosticSnapshot { get; init; }
        public RenderDiagnosticsState Diagnostics { get; private set; } = RenderDiagnosticsState.Empty;

        public IPreviewFrameSource Create(PreviewCaptureRequest request, CancellationToken cancellationToken = default)
        {
            Threads.Add(Environment.CurrentManagedThreadId);
            return this;
        }

        public ImageRgba8 Capture(CaptureView view, int width, int height)
        {
            Times.Add(view.TimeCode);
            Threads.Add(Environment.CurrentManagedThreadId);
            OnCapture?.Invoke(Times.Count);
            Diagnostics = DiagnosticSnapshot?.Invoke(Times.Count) ?? RenderDiagnosticsState.Empty;
            return new ImageRgba8(2, 1, [255, 0, 0, 255, 0, 255, 0, 255]);
        }

        public RenderJobImage CaptureWithDepth(
            CaptureView view, int width, int height, long maximumReadbackBytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImageRgba8 color = Capture(view, width, height);
            float[] depth = [0.25f, 1f];
            return new RenderJobImage(color.Width, color.Height, color.Pixels, Rgba8RowOrder.TopDown)
            {
                DeviceDepth = new RenderJobDeviceDepth(2, 1, depth)
            };
        }

        public RenderJobImage CaptureWithHdrColor(
            CaptureView view, int width, int height, long maximumReadbackBytes,
            bool includeDepth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImageRgba8 color = Capture(view, width, height);
            float[] depth = [0.25f, 1f];
            return new RenderJobImage(color.Width, color.Height, color.Pixels, Rgba8RowOrder.TopDown)
            {
                HdrColor = new RenderJobHdrColor(2, 1, Convert.FromHexString("005400000000003C000000540000003C")),
                DeviceDepth = includeDepth ? new RenderJobDeviceDepth(2, 1, depth) : null
            };
        }

        public void Dispose()
        {
        }
    }

    [Test]
    public async Task MapsRichTypedEditsIntoOneAtomicWorkspaceBatch()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        using var service = new OpenUsdMcpService(
            workspace,
            provider,
            CreateOptions(files));
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" },
            default);

        _ = await service.ApplyEditsAsync(
            new ApplyEditsRequest
            {
                SessionId = session.SessionId,
                Generation = session.Generation,
                StageRevision = session.StageRevision,
                Edits =
                [
                    new WorkspaceEditDto
                    {
                        Kind = "set_bool",
                        PrimPath = "/World",
                        AttributeName = "custom:enabled",
                        BoolValue = true,
                    },
                    new WorkspaceEditDto
                    {
                        Kind = "set_int64",
                        PrimPath = "/World",
                        AttributeName = "custom:index",
                        Int64Value = 7,
                    },
                    new WorkspaceEditDto
                    {
                        Kind = "set_string",
                        PrimPath = "/World",
                        AttributeName = "custom:label",
                        StringValue = "hero",
                    },
                    new WorkspaceEditDto
                    {
                        Kind = "set_token",
                        PrimPath = "/World",
                        AttributeName = "purpose",
                        StringValue = "render",
                    },
                    new WorkspaceEditDto
                    {
                        Kind = "set_float3",
                        PrimPath = "/World",
                        AttributeName = "xformOp:scale",
                        VectorValue = [1, 2, 3],
                    },
                    new WorkspaceEditDto
                    {
                        Kind = "set_color3f",
                        PrimPath = "/World",
                        AttributeName = "primvars:displayColor",
                        VectorValue = [0.2, 0.4, 0.8],
                    },
                ],
            },
            default);

        await Assert.That(backend.LastBatch).IsNotNull();
        await Assert.That(backend.LastBatch!.Operations.Select(static edit => edit.Kind))
            .IsEquivalentTo(
            [
                WorkspaceEditKind.SetBool,
                WorkspaceEditKind.SetInt64,
                WorkspaceEditKind.SetString,
                WorkspaceEditKind.SetToken,
                WorkspaceEditKind.SetFloat3,
                WorkspaceEditKind.SetColor3f,
            ]);
    }

    [Test]
    public async Task PreviewRejectsMalformedAuthoredCameraPathBeforeCapture()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        using var service = new OpenUsdMcpService(
            workspace,
            provider,
            CreateOptions(files));

        await Assert.That(
                async () => await service.RenderPreviewAsync(
                    new RenderPreviewRequest
                    {
                        SessionId = "unused",
                        Kind = "still",
                        Width = 64,
                        Height = 64,
                        CameraPath = "World/Camera",
                        Views = [new CaptureViewDto { Name = "hero" }],
                    },
                    default))
            .Throws<ArgumentException>();
        await Assert.That(backend.Events).IsEmpty();
    }

    [Test]
    public async Task TraversalRootedAndMissingSourcePathsReturnPathDenied()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        using var service = new OpenUsdMcpService(
            workspace,
            provider,
            CreateOptions(files));
        string[] deniedPaths =
        [
            Path.Combine("..", "outside.usda"),
            files.SourcePath,
            "missing.usda",
        ];

        foreach (string deniedPath in deniedPaths)
        {
            OpenUsdMcpFailureException? failure = null;
            try
            {
                _ = await service.OpenSceneAsync(
                    new OpenSceneRequest { SourcePath = deniedPath },
                    default);
            }
            catch (OpenUsdMcpFailureException exception)
            {
                failure = exception;
            }

            await Assert.That(failure).IsNotNull();
            await Assert.That(failure!.Code).IsEqualTo(OpenUsdMcpErrorCodes.PathDenied);
        }

        await Assert.That(backend.Context).IsNull();
    }

    [Test]
    public async Task ReparseSourcePathReturnsPathDeniedWhenLinksAreSupported()
    {
        using var files = new WorkspaceTestFiles();
        string outside = Path.Combine(
            Path.GetDirectoryName(files.SourceRoot)!,
            "outside.usda");
        string link = Path.Combine(files.SourceRoot, "linked.usda");
        File.WriteAllText(outside, "#usda 1.0");
        try
        {
            try
            {
                _ = File.CreateSymbolicLink(link, outside);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                PlatformNotSupportedException)
            {
                Skip.Test($"Symbolic-link creation is unavailable: {exception.Message}");
                throw;
            }

            var backend = new RecordingWorkspaceBackend();
            await using var workspace = files.CreateWorkspace(backend);
            using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
            using var service = new OpenUsdMcpService(
                workspace,
                provider,
                CreateOptions(files));
            OpenUsdMcpFailureException? failure = null;
            try
            {
                _ = await service.OpenSceneAsync(
                    new OpenSceneRequest { SourcePath = "linked.usda" },
                    default);
            }
            catch (OpenUsdMcpFailureException exception)
            {
                failure = exception;
            }

            await Assert.That(failure).IsNotNull();
            await Assert.That(failure!.Code).IsEqualTo(OpenUsdMcpErrorCodes.PathDenied);
            await Assert.That(backend.Context).IsNull();
        }
        finally
        {
            File.Delete(link);
            File.Delete(outside);
        }
    }

    [Test]
    public async Task AppliedProposalHistoryQuotaRejectsBeforeCheckpointCreation()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        using var service = new OpenUsdMcpService(
            workspace,
            provider,
            CreateOptions(files) with
            {
                MaximumAppliedProposalHistoryCount = 0,
            });
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" },
            default);
        McpAnalysisResultDto analysis = await service.AnalyzeSceneAsync(
            new AnalyzeSceneRequest
            {
                SessionId = session.SessionId,
                Generation = session.Generation,
                StageRevision = session.StageRevision,
                Observations = new AnalysisObservationsDto(),
            },
            default);
        OpenUsdMcpFailureException? failure = null;

        try
        {
            _ = await service.ApplyProposalsAsync(
                new ApplyProposalsRequest
                {
                    SessionId = session.SessionId,
                    Generation = session.Generation,
                    StageRevision = session.StageRevision,
                    ProposalIds = [analysis.Proposals[0].Id],
                },
                default);
        }
        catch (OpenUsdMcpFailureException exception)
        {
            failure = exception;
        }

        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Code).IsEqualTo(OpenUsdMcpErrorCodes.QuotaExceeded);
        await Assert.That(backend.Events).DoesNotContain("checkpoint");
        _ = await service.CloseSceneAsync(
            new SceneRevisionRequest
            {
                SessionId = session.SessionId,
                Generation = session.Generation,
                StageRevision = session.StageRevision,
            },
            default);
    }

    [Test]
    public async Task AppliedProposalHistoryAcceptsExactBoundaryAndRejectsNextId()
    {
        await Assert.That(
                () => OpenUsdMcpService.EnsureAppliedProposalCapacity(
                    currentCount: 1,
                    incomingCount: 2,
                    maximumCount: 3))
            .ThrowsNothing();

        OpenUsdMcpFailureException? failure = null;
        try
        {
            OpenUsdMcpService.EnsureAppliedProposalCapacity(
                currentCount: 2,
                incomingCount: 2,
                maximumCount: 3);
        }
        catch (OpenUsdMcpFailureException exception)
        {
            failure = exception;
        }

        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Code).IsEqualTo(OpenUsdMcpErrorCodes.QuotaExceeded);
    }

    [Test]
    public async Task InspectionCancellationAfterBackendCallbackPropagatesThroughProtocol()
    {
        using var files = new WorkspaceTestFiles();
        using var cancellation = new CancellationTokenSource();
        bool callbackStarted = false;
        var backend = new RecordingWorkspaceBackend
        {
            InspectSceneCallback = token =>
            {
                callbackStarted = true;
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            },
        };
        await using var workspace = files.CreateWorkspace(backend);
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        using var service = new OpenUsdMcpService(
            workspace,
            provider,
            CreateOptions(files));
        var tools = new OpenUsdMcpTools(service, new OpenUsdMcpProtocolOptions());
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" },
            default);
        var request = new SceneRevisionRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
        };

        await Assert.That(
                async () => await tools.InspectSceneAsync(request, cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(callbackStarted).IsTrue();

        _ = await service.CloseSceneAsync(request, default);
    }

    [Test]
    public async Task MutationInvalidatesPreviewAndViewerLaunchRemainsExplicit()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        var artifacts = new ArtifactResourceStore(
            new ArtifactResourceStoreOptions(
                FileStorageRoot: Path.Combine(
                    files.OutputRoot,
                    ".artifact-resources")));
        var captureProcessor = new ServiceCaptureProcessor(artifacts);
        using var captureWorker = new CaptureWorker(captureProcessor);
        string viewerRoot = Path.Combine(files.OutputRoot, "viewer");
        Directory.CreateDirectory(viewerRoot);
        string viewerPath = Path.Combine(viewerRoot, ViewerExecutableName());
        File.WriteAllText(viewerPath, "viewer");
        var processStarter = new RecordingViewerProcessStarter();
        var viewer = new ViewerChildLauncher(
            new ViewerChildLauncherOptions(viewerRoot, viewerPath),
            processStarter);
        var options = new OpenUsdMcpApplicationOptions(
            files.SourceRoot,
            files.OutputRoot,
            files.SourceRoot,
            viewerRoot,
            viewerPath);
        var services = new ServiceCollection();
        services.AddSingleton(captureWorker);
        services.AddSingleton(new FinalizationService(workspace, artifacts));
        services.AddSingleton(viewer);
        await using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
        using var service = new OpenUsdMcpService(workspace, provider, options);

        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" },
            default);
        File.WriteAllText(
            Path.Combine(files.OutputRoot, session.SessionId, "overlay.usda"),
            "#usda 1.0\n");
        McpCaptureResultDto capture = await service.RenderPreviewAsync(
            new RenderPreviewRequest
            {
                SessionId = session.SessionId,
                Generation = session.Generation,
                StageRevision = session.StageRevision,
                Kind = "still",
                Width = 1,
                Height = 1,
                Views = [new CaptureViewDto { Name = "hero" }],
            },
            default);
        await Assert.That(capture.Diagnostics.Count).IsEqualTo(1);
        await Assert.That(capture.Diagnostics[0].Code)
            .IsEqualTo("OPENUSD_SILK_TEXTURE_ASSET_NOT_FOUND");
        McpEditResultDto edit = await service.ApplyEditsAsync(
            new ApplyEditsRequest
            {
                SessionId = session.SessionId,
                Generation = session.Generation,
                StageRevision = session.StageRevision,
                Edits =
                [
                    new WorkspaceEditDto
                    {
                        Kind = "define_prim",
                        PrimPath = "/World",
                        TypeName = "Xform",
                    },
                ],
            },
            default);
        var revision = new SceneRevisionRequest
        {
            SessionId = edit.SessionId,
            Generation = edit.Generation,
            StageRevision = edit.StageRevision,
        };

        McpFinalizationResultDto finalization = await service.FinalizeSceneAsync(
            revision,
            default);

        await Assert.That(finalization.FinalStageCreated).IsTrue();
        await Assert.That(finalization.Partial).IsTrue();
        await Assert.That(finalization.Failures)
            .Contains(failure => failure.StartsWith("hero-still:", StringComparison.Ordinal));
        await Assert.That(File.Exists(Path.Combine(
            files.OutputRoot,
            session.SessionId,
            "presentation",
            "hero-still.png"))).IsFalse();
        await Assert.That(processStarter.StartCount).IsEqualTo(0);

        McpPresentationResultDto presentation = await service.PresentSceneAsync(
            new PresentSceneRequest
            {
                SessionId = edit.SessionId,
                Generation = edit.Generation,
                StageRevision = edit.StageRevision,
                Renderer = "auto",
                CameraPath = "/World/Camera",
            },
            default);

        await Assert.That(presentation.ProcessId).IsEqualTo(1234);
        await Assert.That(processStarter.StartCount).IsEqualTo(1);
        await Assert.That(processStarter.StartInfo!.Arguments).IsEmpty();
        await Assert.That(processStarter.StartInfo.ArgumentList)
            .Contains("--camera");

        _ = await service.CloseSceneAsync(revision, default);

        await Assert.That(captureProcessor.ResetCount).IsEqualTo(1);
        await Assert.That(captureProcessor.ResetThreadId)
            .IsEqualTo(captureProcessor.ProcessThreadId);
        await Assert.That(backend.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task CloseRetryReleasesFailedPreviewSourceBeforeWorkspaceTeardown()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        var frameSource = new RetryingFrameSource(disposeFailuresRemaining: 1);
        var captureProcessor = new PreviewCaptureProcessor(
            new RetryingFrameSourceFactory(frameSource),
            new ArtifactResourceStore());
        using var captureWorker = new CaptureWorker(captureProcessor);
        var services = new ServiceCollection();
        services.AddSingleton(captureWorker);
        await using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
        using var service = new OpenUsdMcpService(
            workspace,
            provider,
            CreateOptions(files));
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" },
            default);
        var revision = new SceneRevisionRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
        };
        _ = await service.RenderPreviewAsync(
            new RenderPreviewRequest
            {
                SessionId = session.SessionId,
                Generation = session.Generation,
                StageRevision = session.StageRevision,
                Kind = "still",
                Width = 1,
                Height = 1,
                Views = [new CaptureViewDto { Name = "hero" }],
            },
            default);

        await Assert.That(async () => await service.CloseSceneAsync(revision, default))
            .ThrowsExactly<IOException>();
        await Assert.That((await workspace.GetStatusAsync()).IsActive).IsTrue();
        await Assert.That(frameSource.DisposeAttemptCount).IsEqualTo(1);
        await Assert.That(backend.DisposeAttemptCount).IsEqualTo(0);

        McpClosedSceneDto closed = await service.CloseSceneAsync(revision, default);
        McpSessionDto replacement = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" },
            default);
        _ = await service.CloseSceneAsync(
            new SceneRevisionRequest
            {
                SessionId = replacement.SessionId,
                Generation = replacement.Generation,
                StageRevision = replacement.StageRevision,
            },
            default);

        await Assert.That(closed.Closed).IsTrue();
        await Assert.That(frameSource.DisposeAttemptCount).IsEqualTo(2);
        await Assert.That(frameSource.DisposeThreadIds.Distinct())
            .IsEquivalentTo([frameSource.CaptureThreadId]);
        await Assert.That(backend.DisposeCount).IsEqualTo(2);
    }

    private static string ViewerExecutableName() =>
        OperatingSystem.IsWindows()
            ? "OpenUsd.Viewer.App.exe"
            : "OpenUsd.Viewer.App";

    private static OpenUsdMcpApplicationOptions CreateOptions(
        WorkspaceTestFiles files) =>
        new(
            files.SourceRoot,
            files.OutputRoot,
            files.SourceRoot,
            files.OutputRoot,
            Path.Combine(files.OutputRoot, ViewerExecutableName()));

    private sealed class ServiceCaptureProcessor(ArtifactResourceStore artifacts)
        : IPreviewCaptureProcessor, IResettablePreviewCaptureProcessor
    {
        internal int ProcessThreadId { get; private set; }

        internal int ResetCount { get; private set; }

        internal int ResetThreadId { get; private set; }

        public PreviewCaptureResult Process(
            PreviewCaptureRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProcessThreadId = Environment.CurrentManagedThreadId;
            ArtifactResourceDescriptor artifact = artifacts.Add(
                string.Concat(request.RequestId, ".png"),
                "image/png",
                new byte[] { 1, 2, 3 });
            return new PreviewCaptureResult(
                request.RequestId,
                request.Kind,
                request.Width,
                request.Height,
                [artifact],
                [
                    new RenderDiagnostic(
                        RenderDiagnosticSeverity.Warning,
                        "OPENUSD_SILK_TEXTURE_ASSET_NOT_FOUND",
                        "A texture asset was not found."),
                ]);
        }

        public void Reset()
        {
            ResetCount++;
            ResetThreadId = Environment.CurrentManagedThreadId;
        }
    }

    private sealed class RetryingFrameSourceFactory(RetryingFrameSource source)
        : IPreviewFrameSourceFactory
    {
        public IPreviewFrameSource Create(
            PreviewCaptureRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return source;
        }
    }

    private sealed class RetryingFrameSource(int disposeFailuresRemaining)
        : IPreviewFrameSource
    {
        internal int CaptureThreadId { get; private set; }

        internal int DisposeAttemptCount { get; private set; }

        internal List<int> DisposeThreadIds { get; } = [];

        public ImageRgba8 Capture(CaptureView view, int width, int height)
        {
            CaptureThreadId = Environment.CurrentManagedThreadId;
            return new ImageRgba8(
                width,
                height,
                new byte[ImageRgba8.GetByteCount(width, height)]);
        }

        public void Dispose()
        {
            DisposeAttemptCount++;
            DisposeThreadIds.Add(Environment.CurrentManagedThreadId);
            if (disposeFailuresRemaining > 0)
            {
                disposeFailuresRemaining--;
                throw new IOException("preview cleanup failed");
            }
        }
    }
}
