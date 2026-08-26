// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text;

namespace OpenUsd.Mcp.Tests;

public sealed class FinalizationServiceTests
{
    [Test]
    public async Task FinalizationPersistsPresentationAndKeepsSessionActive()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        string? exportPath = null;
        backend.ExportCallback = (path, _) => exportPath = path;
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        File.WriteAllText(session.OverlayPath, "#usda 1.0\n");
        ArtifactResourceStore store = CreateArtifactStore(files);
        PreviewCaptureResult still = AddPreview(
            store,
            "hero",
            CaptureKind.Still,
            ["hero.png"]);
        PreviewCaptureResult sheet = AddPreview(
            store,
            "sheet",
            CaptureKind.ContactSheet,
            ["sheet.png"]);
        PreviewCaptureResult turntable = AddPreview(
            store,
            "turntable",
            CaptureKind.Turntable,
            ["turntable-0.png", "turntable-1.png"]);
        var service = new FinalizationService(workspace, store);

        var request = new FinalizationRequest(
            Revision(session),
            new FinalizationAnalysis(
                [new FinalizationValidationFinding("valid", "info", "Stage is valid.")],
                [new FinalizationStatistic("prims", "1")],
                ["proposal-1"]),
            new FinalizationPreviewOutputs(still, sheet, turntable));
        FinalizationResult result = await service.FinalizeAsync(request);
        byte[] firstManifest = File.ReadAllBytes(result.ManifestPath);
        byte[] firstJsonReport = File.ReadAllBytes(result.JsonReportPath);
        FinalizationResult repeated = await service.FinalizeAsync(request);
        byte[] repeatedManifest = File.ReadAllBytes(repeated.ManifestPath);
        byte[] repeatedJsonReport = File.ReadAllBytes(repeated.JsonReportPath);
        FinalizationResult changed = await service.FinalizeAsync(
            new FinalizationRequest(
                Revision(session),
                new FinalizationAnalysis(
                    [new FinalizationValidationFinding("changed", "info", "Changed.")],
                    [new FinalizationStatistic("prims", "2")],
                    ["proposal-2"]),
                new FinalizationPreviewOutputs(still, sheet, turntable)));
        WorkspaceSessionStatus status = await workspace.GetStatusAsync();

        await Assert.That(result.IsPartial).IsFalse();
        await Assert.That(repeated.IsPartial).IsFalse();
        await Assert.That(changed.IsPartial).IsFalse();
        await Assert.That(repeatedManifest)
            .IsEquivalentTo(firstManifest);
        await Assert.That(repeatedJsonReport)
            .IsEquivalentTo(firstJsonReport);
        await Assert.That(repeated.OutputDirectory)
            .IsEqualTo(result.OutputDirectory);
        await Assert.That(repeated.ManifestResource!.Id)
            .IsEqualTo(result.ManifestResource!.Id);
        await Assert.That(repeated.JsonReportResource!.Id)
            .IsEqualTo(result.JsonReportResource!.Id);
        await Assert.That(repeated.MarkdownReportResource!.Id)
            .IsEqualTo(result.MarkdownReportResource!.Id);
        await Assert.That(File.Exists(result.FinalStagePath!)).IsTrue();
        await Assert.That(File.Exists(Path.Combine(
            result.OutputDirectory,
            "presentation",
            "hero-still.png"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(
            result.OutputDirectory,
            "presentation",
            "contact-sheet.png"))).IsTrue();
        await Assert.That(Directory.GetFiles(Path.Combine(
            result.OutputDirectory,
            "presentation",
            "turntable"))).Count().IsEqualTo(2);
        await Assert.That(result.ManifestResource).IsNotNull();
        await Assert.That(result.JsonReportResource).IsNotNull();
        await Assert.That(result.MarkdownReportResource).IsNotNull();
        await Assert.That(result.ManifestResource!.IsInline).IsFalse();
        await Assert.That(result.JsonReportResource!.IsInline).IsFalse();
        await Assert.That(result.MarkdownReportResource!.IsInline).IsFalse();
        await Assert.That(Directory.EnumerateFiles(
                Path.Combine(files.OutputRoot, ".artifact-resources"),
                "*",
                SearchOption.AllDirectories))
            .Count().IsGreaterThanOrEqualTo(3);
        await Assert.That(changed.ManifestResource).IsNotNull();
        await Assert.That(changed.JsonReportResource).IsNotNull();
        await Assert.That(changed.MarkdownReportResource).IsNotNull();
        await Assert.That(changed.ManifestResource!.Id)
            .IsNotEqualTo(result.ManifestResource!.Id);
        await Assert.That(changed.JsonReportResource!.Id)
            .IsNotEqualTo(result.JsonReportResource!.Id);
        await Assert.That(changed.MarkdownReportResource!.Id)
            .IsNotEqualTo(result.MarkdownReportResource!.Id);
        await Assert.That(changed.OutputDirectory)
            .IsNotEqualTo(result.OutputDirectory);
        await Assert.That(result.OutputDirectory)
            .Contains("generation-00000000000000000000");
        await Assert.That(result.OutputDirectory)
            .Contains("revision-00000000000000000000");
        await Assert.That(status.IsActive).IsTrue();
        await Assert.That(status.Session!.Generation).IsEqualTo(session.Generation);
        await Assert.That(backend.Events).Contains("export");
        await Assert.That(exportPath).IsNotNull();
        await Assert.That(Path.GetDirectoryName(Path.GetDirectoryName(exportPath!)))
            .IsEqualTo(session.OutputDirectory);
    }

    [Test]
    public async Task FinalizationNeverOverwritesSourceAndRejectsEscapingExportPaths()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        File.WriteAllText(session.OverlayPath, "#usda 1.0\n");
        string sourceBefore = File.ReadAllText(files.SourcePath);

        WorkspaceFinalStageResult exported = await workspace.ExportFinalStageAsync(
            Revision(session),
            "final.usda");
        await Assert.That(
                async () => await workspace.ExportFinalStageAsync(
                    Revision(session),
                    Path.Combine("..", "..", "scene.usda")))
            .Throws<WorkspacePathContainmentException>();

        await Assert.That(File.ReadAllText(files.SourcePath)).IsEqualTo(sourceBefore);
        await Assert.That(exported.FinalStagePath).IsNotEqualTo(files.SourcePath);
        await Assert.That(File.Exists(exported.FinalStagePath)).IsTrue();
        await Assert.That(backend.Events.Count(static item => item == "export"))
            .IsEqualTo(1);
    }

    [Test]
    public async Task MissingPreviewAndExportFailuresArePersistedExplicitly()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend
        {
            ExportError = new IOException("flatten unavailable"),
        };
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        File.WriteAllText(session.OverlayPath, "#usda 1.0\n");
        ArtifactResourceStore store = CreateArtifactStore(files);
        ArtifactResourceDescriptor missing = store.Add(
            "missing-source",
            "image/png",
            new byte[] { 1, 2, 3 });
        ArtifactResourceStore missingStore = CreateArtifactStore(
            files,
            "missing-artifacts");
        var unavailableStill = new PreviewCaptureResult(
            "missing",
            CaptureKind.Still,
            1,
            1,
            [missing]);
        var service = new FinalizationService(workspace, missingStore);

        FinalizationResult result = await service.FinalizeAsync(
            new FinalizationRequest(
                Revision(session),
                new FinalizationAnalysis(),
                new FinalizationPreviewOutputs(HeroStill: unavailableStill)));
        string manifest = File.ReadAllText(result.ManifestPath);

        await Assert.That(result.IsPartial).IsTrue();
        await Assert.That(result.FinalStagePath).IsNull();
        await Assert.That(result.Failures.Select(static item => item.Role))
            .Contains("final-stage");
        await Assert.That(result.Failures.Select(static item => item.Role))
            .Contains("hero-still");
        await Assert.That(result.Failures.Select(static item => item.Role))
            .Contains("contact-sheet");
        await Assert.That(result.Failures.Select(static item => item.Role))
            .Contains("turntable");
        await Assert.That(manifest).Contains("\"status\": \"failed\"");
        await Assert.That(manifest).Contains("flatten unavailable");
        await Assert.That(manifest).Contains("Preview resource");
    }

    [Test]
    public async Task TurntableFrameCountIsBoundedBeforeAnyExportOrLaunch()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        File.WriteAllText(session.OverlayPath, "#usda 1.0\n");
        ArtifactResourceStore store = CreateArtifactStore(files);
        PreviewCaptureResult turntable = AddPreview(
            store,
            "turntable",
            CaptureKind.Turntable,
            ["0.png", "1.png"]);
        var processStarter = new RecordingViewerProcessStarter();
        string viewerRoot = CreateViewerRoot(session.OutputDirectory);
        _ = new ViewerChildLauncher(
            new ViewerChildLauncherOptions(
                viewerRoot,
                Path.Combine(viewerRoot, ViewerExecutableName())),
            processStarter);
        var service = new FinalizationService(
            workspace,
            store,
            new FinalizationOptions(MaximumTurntableFrames: 1));

        await Assert.That(
                async () => await service.FinalizeAsync(
                    new FinalizationRequest(
                        Revision(session),
                        new FinalizationAnalysis(),
                        new FinalizationPreviewOutputs(Turntable: turntable))))
            .Throws<ArgumentException>();

        await Assert.That(backend.Events).DoesNotContain("export");
        await Assert.That(processStarter.StartCount).IsEqualTo(0);
    }

    [Test]
    public async Task RerunAfterPreviewInvalidationDoesNotExposePriorPresentation()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        File.WriteAllText(session.OverlayPath, "#usda 1.0\n");
        ArtifactResourceStore store = CreateArtifactStore(files);
        FinalizationPreviewOutputs previews = CreateCompletePreviews(store);
        var service = new FinalizationService(workspace, store);

        FinalizationResult complete = await service.FinalizeAsync(
            CreateRequest(session, "complete", previews));
        FinalizationResult invalidated = await service.FinalizeAsync(
            CreateRequest(session, "invalidated", new FinalizationPreviewOutputs()));

        await Assert.That(complete.IsPartial).IsFalse();
        await Assert.That(invalidated.IsPartial).IsTrue();
        await Assert.That(invalidated.OutputDirectory)
            .IsNotEqualTo(complete.OutputDirectory);
        await Assert.That(Directory.Exists(Path.Combine(
            complete.OutputDirectory,
            "presentation"))).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(
            invalidated.OutputDirectory,
            "presentation"))).IsFalse();
        await Assert.That(invalidated.Artifacts
                .Where(static item => item.Status == FinalizationArtifactStatus.Created)
                .Select(static item => item.RelativePath))
            .DoesNotContain("presentation/hero-still.png");
    }

    [Test]
    public async Task ShorterTurntablePublishesOnlyCurrentFrames()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        File.WriteAllText(session.OverlayPath, "#usda 1.0\n");
        ArtifactResourceStore store = CreateArtifactStore(files);
        PreviewCaptureResult still = AddPreview(
            store,
            "hero",
            CaptureKind.Still,
            ["hero.png"]);
        PreviewCaptureResult sheet = AddPreview(
            store,
            "sheet",
            CaptureKind.ContactSheet,
            ["sheet.png"]);
        PreviewCaptureResult longTurntable = AddPreview(
            store,
            "long-turntable",
            CaptureKind.Turntable,
            ["frame-0.png", "frame-1.png", "frame-2.png"]);
        PreviewCaptureResult shortTurntable = AddPreview(
            store,
            "short-turntable",
            CaptureKind.Turntable,
            ["replacement-frame.png"]);
        var service = new FinalizationService(workspace, store);

        FinalizationResult first = await service.FinalizeAsync(
            CreateRequest(
                session,
                "long",
                new FinalizationPreviewOutputs(still, sheet, longTurntable)));
        FinalizationResult second = await service.FinalizeAsync(
            CreateRequest(
                session,
                "short",
                new FinalizationPreviewOutputs(still, sheet, shortTurntable)));
        string secondTurntable = Path.Combine(
            second.OutputDirectory,
            "presentation",
            "turntable");

        await Assert.That(first.IsPartial).IsFalse();
        await Assert.That(second.IsPartial).IsFalse();
        await Assert.That(second.OutputDirectory).IsNotEqualTo(first.OutputDirectory);
        await Assert.That(Directory.GetFiles(secondTurntable))
            .Count().IsEqualTo(1);
        await Assert.That(File.Exists(Path.Combine(
            secondTurntable,
            "frame-00.png"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(
            secondTurntable,
            "frame-01.png"))).IsFalse();
        await Assert.That(Directory.GetFiles(Path.Combine(
            first.OutputDirectory,
            "presentation",
            "turntable"))).Count().IsEqualTo(3);
    }

    [Test]
    public async Task FailedExportPublishesNoStaleStageAndPreservesPriorOutput()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        File.WriteAllText(session.OverlayPath, "#usda 1.0\n");
        ArtifactResourceStore store = CreateArtifactStore(files);
        FinalizationPreviewOutputs previews = CreateCompletePreviews(store);
        var service = new FinalizationService(workspace, store);
        FinalizationRequest request = CreateRequest(session, "export", previews);

        FinalizationResult complete = await service.FinalizeAsync(request);
        byte[] completeManifest = File.ReadAllBytes(complete.ManifestPath);
        backend.ExportError = new IOException("later flatten failed");
        FinalizationResult failed = await service.FinalizeAsync(request);

        await Assert.That(complete.IsPartial).IsFalse();
        await Assert.That(failed.IsPartial).IsTrue();
        await Assert.That(failed.OutputDirectory).IsNotEqualTo(complete.OutputDirectory);
        await Assert.That(failed.FinalStagePath).IsNull();
        await Assert.That(File.Exists(Path.Combine(
            failed.OutputDirectory,
            "final-stage.usda"))).IsFalse();
        await Assert.That(File.Exists(complete.FinalStagePath!)).IsTrue();
        await Assert.That(File.ReadAllBytes(complete.ManifestPath))
            .IsEquivalentTo(completeManifest);
        await Assert.That(File.ReadAllText(failed.ManifestPath))
            .Contains("later flatten failed");
    }

    [Test]
    public async Task FullResourceStoreRecordsEveryPublicationFailure()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        File.WriteAllText(session.OverlayPath, "#usda 1.0\n");
        var store = new ArtifactResourceStore(
            new ArtifactResourceStoreOptions(
                MaximumResourceCount: 1,
                MaximumTotalBytes: 1024 * 1024,
                FileStorageRoot: Path.Combine(
                    files.OutputRoot,
                    ".artifact-resources")));
        _ = store.Add("occupied", "text/plain", new byte[] { 1 });
        var service = new FinalizationService(workspace, store);

        FinalizationResult result = await service.FinalizeAsync(
            CreateRequest(session, "full-store", new FinalizationPreviewOutputs()));
        string manifest = File.ReadAllText(result.ManifestPath);

        await Assert.That(result.IsPartial).IsTrue();
        await Assert.That(result.ManifestResource).IsNull();
        await Assert.That(result.JsonReportResource).IsNull();
        await Assert.That(result.MarkdownReportResource).IsNull();
        await Assert.That(result.Failures.Select(static item => item.Role))
            .Contains("analysis-json");
        await Assert.That(result.Failures.Select(static item => item.Role))
            .Contains("analysis-markdown");
        await Assert.That(result.Failures.Select(static item => item.Role))
            .Contains("manifest");
        await Assert.That(manifest).Contains("artifact store is full");
        await Assert.That(manifest).Contains("\"role\": \"manifest\"");
    }

    [Test]
    public async Task ResourceCollisionIsExplicitAndDoesNotClaimLinks()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        File.WriteAllText(session.OverlayPath, "#usda 1.0\n");
        var service = new FinalizationService(workspace, new CollidingArtifactStore());

        FinalizationResult result = await service.FinalizeAsync(
            CreateRequest(session, "collision", new FinalizationPreviewOutputs()));
        string manifest = File.ReadAllText(result.ManifestPath);

        await Assert.That(result.ManifestResource).IsNull();
        await Assert.That(result.JsonReportResource).IsNull();
        await Assert.That(result.MarkdownReportResource).IsNull();
        await Assert.That(result.Failures
                .Where(static item => item.Role is
                    "analysis-json" or "analysis-markdown" or "manifest")
                .Select(static item => item.Message))
            .All(static message => message.Contains(
                "already contains different content",
                StringComparison.Ordinal));
        await Assert.That(manifest).Contains("already contains different content");
        await Assert.That(manifest).DoesNotContain("\"resourceUri\": \"openusd://artifact/");
    }

    [Test]
    public async Task ManifestPublicationFailureIsReflectedOnDiskAndInResult()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        File.WriteAllText(session.OverlayPath, "#usda 1.0\n");
        var store = new ManifestRejectingArtifactStore(
            Path.Combine(files.OutputRoot, ".artifact-resources"));
        var service = new FinalizationService(workspace, store);

        FinalizationResult result = await service.FinalizeAsync(
            CreateRequest(
                session,
                "manifest-failure",
                new FinalizationPreviewOutputs()));
        byte[] manifestBytes = File.ReadAllBytes(result.ManifestPath);
        string manifest = Encoding.UTF8.GetString(manifestBytes);
        FinalizationFailure failure = result.Failures.Single(
            static item => item.Role == "manifest");

        await Assert.That(result.ManifestResource).IsNull();
        await Assert.That(result.JsonReportResource).IsNotNull();
        await Assert.That(result.MarkdownReportResource).IsNotNull();
        await Assert.That(failure.Message)
            .Contains(ManifestRejectingArtifactStore.ErrorMessage);
        await Assert.That(manifest)
            .Contains(ManifestRejectingArtifactStore.ErrorMessage);
        await Assert.That(manifest).Contains("\"role\": \"manifest\"");
        await Assert.That(Path.GetFileName(result.OutputDirectory))
            .IsEqualTo(string.Concat(
                "finalization-",
                Hash(manifestBytes)));
    }

    [Test]
    public async Task RejectsReparseAncestorBeforeFinalizationMutation()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        File.WriteAllText(session.OverlayPath, "#usda 1.0\n");
        string outside = Path.Combine(
            Path.GetDirectoryName(session.OutputDirectory)!,
            "finalization-outside");
        string link = Path.Combine(session.OutputDirectory, "finalizations");
        Directory.CreateDirectory(outside);
        try
        {
            try
            {
                _ = Directory.CreateSymbolicLink(link, outside);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                PlatformNotSupportedException)
            {
                Skip.Test($"Symbolic-link creation is unavailable: {exception.Message}");
                throw;
            }

            var service = new FinalizationService(
                workspace,
                CreateArtifactStore(files));
            await Assert.That(
                    async () => await service.FinalizeAsync(
                        CreateRequest(
                            session,
                            "reparse",
                            new FinalizationPreviewOutputs())))
                .Throws<WorkspacePathContainmentException>();

            await Assert.That(backend.Events).DoesNotContain("export");
            await Assert.That(Directory.EnumerateFileSystemEntries(outside))
                .IsEmpty();
        }
        finally
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }

            if (Directory.Exists(outside))
            {
                Directory.Delete(outside);
            }
        }
    }

    [Test]
    public async Task AtomicPublicationFailurePreservesPriorCompleteFinalization()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        File.WriteAllText(session.OverlayPath, "#usda 1.0\n");
        ArtifactResourceStore store = CreateArtifactStore(files);
        FinalizationPreviewOutputs previews = CreateCompletePreviews(store);
        var service = new FinalizationService(workspace, store);
        FinalizationResult prior = await service.FinalizeAsync(
            CreateRequest(session, "prior", previews));
        byte[] priorManifest = File.ReadAllBytes(prior.ManifestPath);
        FinalizationRequest blockedRequest = CreateRequest(
            session,
            "blocked",
            previews);
        FinalizationResult toBlock = await service.FinalizeAsync(blockedRequest);
        Directory.Delete(toBlock.OutputDirectory, recursive: true);
        File.WriteAllText(toBlock.OutputDirectory, "blocks atomic directory publication");
        try
        {
            await Assert.That(
                    async () => await service.FinalizeAsync(blockedRequest))
                .Throws<IOException>();

            await Assert.That(File.Exists(prior.FinalStagePath!)).IsTrue();
            await Assert.That(File.ReadAllBytes(prior.ManifestPath))
                .IsEquivalentTo(priorManifest);
            await Assert.That(Directory.GetDirectories(
                Path.GetDirectoryName(toBlock.OutputDirectory)!,
                ".staging-*")).IsEmpty();
        }
        finally
        {
            File.Delete(toBlock.OutputDirectory);
        }
    }

    [Test]
    public async Task SparseOverlayUsesStreamingLengthAndDeterministicHash()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        const long overlayLength = (32L * 1024 * 1024) + 17;
        await using (var overlay = new FileStream(
                         session.OverlayPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None))
        {
            await overlay.WriteAsync("#usda 1.0\n"u8.ToArray());
            overlay.SetLength(overlayLength);
        }

        ArtifactResourceStore store = CreateArtifactStore(files);
        var service = new FinalizationService(workspace, store);
        FinalizationResult result = await service.FinalizeAsync(
            CreateRequest(session, "sparse", new FinalizationPreviewOutputs()));
        FinalizationArtifactRecord overlayRecord = result.Artifacts.Single(
            static artifact => artifact.Role == "overlay");
        string persistedOverlay = Path.Combine(
            result.OutputDirectory,
            overlayRecord.RelativePath);

        await Assert.That(overlayRecord.ByteLength).IsEqualTo(overlayLength);
        await Assert.That(new FileInfo(persistedOverlay).Length)
            .IsEqualTo(overlayLength);
        await Assert.That(overlayRecord.Sha256)
            .IsEqualTo(await HashFileAsync(persistedOverlay));
    }

    [Test]
    public async Task ExistingPublicationMutationIsDetectedAndStagingIsCleaned()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        File.WriteAllText(session.OverlayPath, "#usda 1.0\n");
        ArtifactResourceStore store = CreateArtifactStore(files);
        FinalizationPreviewOutputs previews = CreateCompletePreviews(store);
        var service = new FinalizationService(workspace, store);
        FinalizationRequest request = CreateRequest(session, "mutation", previews);
        FinalizationResult first = await service.FinalizeAsync(request);
        File.AppendAllText(first.FinalStagePath!, "# mutation\n");

        await Assert.That(
                async () => await service.FinalizeAsync(request))
            .Throws<IOException>();
        await Assert.That(Directory.EnumerateDirectories(
            Path.GetDirectoryName(first.OutputDirectory)!,
            ".staging-*")).IsEmpty();
    }

    [Test]
    public async Task CancellationAfterExportRemovesPartialStagingDirectory()
    {
        using var files = new WorkspaceTestFiles();
        using var cancellation = new CancellationTokenSource();
        var backend = new RecordingWorkspaceBackend
        {
            ExportCallback = (path, _) =>
            {
                using var stage = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Write,
                    FileShare.None);
                stage.SetLength((long)int.MaxValue + 1);
                cancellation.Cancel();
            },
        };
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        File.WriteAllText(session.OverlayPath, "#usda 1.0\n");
        var service = new FinalizationService(
            workspace,
            CreateArtifactStore(files));

        await Assert.That(
                async () => await service.FinalizeAsync(
                    CreateRequest(
                        session,
                        "cancelled",
                        new FinalizationPreviewOutputs()),
                    cancellation.Token))
            .Throws<OperationCanceledException>();
        string revisionDirectory = Path.Combine(
            session.OutputDirectory,
            "finalizations",
            "generation-00000000000000000000",
            "revision-00000000000000000000");
        await Assert.That(
                Directory.Exists(revisionDirectory)
                    ? Directory.EnumerateDirectories(revisionDirectory, ".staging-*")
                    : [])
            .IsEmpty();
    }

    private static ArtifactResourceStore CreateArtifactStore(
        WorkspaceTestFiles files,
        string directoryName = ".artifact-resources") =>
        new(
            new ArtifactResourceStoreOptions(
                FileStorageRoot: Path.Combine(files.OutputRoot, directoryName)));

    private static PreviewCaptureResult AddPreview(
        ArtifactResourceStore store,
        string requestId,
        CaptureKind kind,
        IEnumerable<string> artifactIds)
    {
        ArtifactResourceDescriptor[] artifacts = artifactIds
            .Select(
                (id, index) => store.Add(
                    id,
                    "image/png",
                    Encoding.UTF8.GetBytes($"png-{index}")))
            .ToArray();
        return new PreviewCaptureResult(requestId, kind, 1, 1, artifacts);
    }

    private static FinalizationRequest CreateRequest(
        WorkspaceSessionInfo session,
        string findingCode,
        FinalizationPreviewOutputs previews) =>
        new(
            Revision(session),
            new FinalizationAnalysis(
                [
                    new FinalizationValidationFinding(
                        findingCode,
                        "info",
                        string.Concat(findingCode, " finding.")),
                ]),
            previews);

    private static FinalizationPreviewOutputs CreateCompletePreviews(
        ArtifactResourceStore store) =>
        new(
            AddPreview(store, "hero", CaptureKind.Still, ["hero.png"]),
            AddPreview(store, "sheet", CaptureKind.ContactSheet, ["sheet.png"]),
            AddPreview(
                store,
                "turntable",
                CaptureKind.Turntable,
                ["frame-0.png", "frame-1.png"]));

    private static string CreateViewerRoot(string outputDirectory)
    {
        string root = Path.Combine(outputDirectory, "viewer");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ViewerExecutableName()), "viewer");
        return root;
    }

    private static WorkspaceSessionRevision Revision(WorkspaceSessionInfo session) =>
        new(session.SessionId, session.Generation, session.StageRevision);

    private static string Hash(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static async ValueTask<string> HashFileAsync(string path)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ViewerExecutableName() =>
        OperatingSystem.IsWindows()
            ? "OpenUsd.Viewer.App.exe"
            : "OpenUsd.Viewer.App";

    private sealed class CollidingArtifactStore : IArtifactResourceStore
    {
        public ArtifactResourceDescriptor Add(
            string artifactId,
            string mediaType,
            ReadOnlyMemory<byte> content) =>
            throw new InvalidOperationException("The collision should be detected before Add.");

        public IReadOnlyList<ArtifactResourceDescriptor> AddRange(
            IReadOnlyList<ArtifactResourceWrite> artifacts) =>
            throw new InvalidOperationException("The collision should be detected before AddRange.");

        public ValueTask<ArtifactResourceDescriptor> AddFileAsync(
            string artifactId,
            string mediaType,
            string sourcePath,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<ArtifactResourceDescriptor>(
                new InvalidOperationException(
                    "The collision should be detected before AddFileAsync."));

        public bool TryGetDescriptor(
            Uri resourceUri,
            out ArtifactResourceDescriptor? descriptor)
        {
            descriptor = new ArtifactResourceDescriptor(
                "collision",
                resourceUri,
                "application/octet-stream",
                1,
                Hash(new byte[] { 0xff }));
            return true;
        }

        public ValueTask<ArtifactResourceContent?> ReadAsync(
            Uri resourceUri,
            CancellationToken cancellationToken = default)
        {
            _ = TryGetDescriptor(resourceUri, out ArtifactResourceDescriptor? descriptor);
            return ValueTask.FromResult<ArtifactResourceContent?>(
                new ArtifactResourceContent(
                    descriptor!,
                    new byte[] { 0xff }));
        }
    }

    private sealed class ManifestRejectingArtifactStore : IArtifactResourceStore
    {
        internal const string ErrorMessage = "simulated manifest publication failure";
        private readonly ArtifactResourceStore _inner;

        internal ManifestRejectingArtifactStore(string storageRoot)
        {
            _inner = new ArtifactResourceStore(
                new ArtifactResourceStoreOptions(
                    FileStorageRoot: storageRoot));
        }

        public ArtifactResourceDescriptor Add(
            string artifactId,
            string mediaType,
            ReadOnlyMemory<byte> content)
        {
            if (IsManifest(artifactId))
            {
                throw new InvalidOperationException(ErrorMessage);
            }

            return _inner.Add(artifactId, mediaType, content);
        }

        public IReadOnlyList<ArtifactResourceDescriptor> AddRange(
            IReadOnlyList<ArtifactResourceWrite> artifacts)
        {
            if (artifacts.Any(static artifact => IsManifest(artifact.Id)))
            {
                throw new InvalidOperationException(ErrorMessage);
            }

            return _inner.AddRange(artifacts);
        }

        public ValueTask<ArtifactResourceDescriptor> AddFileAsync(
            string artifactId,
            string mediaType,
            string sourcePath,
            CancellationToken cancellationToken = default)
        {
            if (IsManifest(artifactId))
            {
                return ValueTask.FromException<ArtifactResourceDescriptor>(
                    new InvalidOperationException(ErrorMessage));
            }

            return _inner.AddFileAsync(
                artifactId,
                mediaType,
                sourcePath,
                cancellationToken);
        }

        public bool TryGetDescriptor(
            Uri resourceUri,
            out ArtifactResourceDescriptor? descriptor) =>
            _inner.TryGetDescriptor(resourceUri, out descriptor);

        public ValueTask<ArtifactResourceContent?> ReadAsync(
            Uri resourceUri,
            CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(resourceUri, cancellationToken);

        private static bool IsManifest(string artifactId) =>
            artifactId.Contains(
                "-finalization-manifest-",
                StringComparison.Ordinal);
    }
}
