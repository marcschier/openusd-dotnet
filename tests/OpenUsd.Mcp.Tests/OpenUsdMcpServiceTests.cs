// Copyright (c) marcschier. Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;

namespace OpenUsd.Mcp.Tests;

public sealed class OpenUsdMcpServiceTests
{
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
                [artifact]);
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
