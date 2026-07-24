// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using OpenUsd.Rendering.Silk.D3D12;
using OpenUsd.Viewer;

namespace OpenUsd.D3D12CompositionSmoke;

[SupportedOSPlatform("windows")]
internal sealed class SmokeWindow : Window, IAsyncDisposable
{
    private static readonly TimeSpan MilestoneTimeout = TimeSpan.FromSeconds(45);
    private readonly SmokeContext _context;
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly CompositionViewportControl _viewport;
    private int _presentedStatusCount;
    private int _teardownStarted;
    private int _workflowStarted;

    internal SmokeWindow(
        SmokeContext context,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        _context = context;
        _desktop = desktop;
        Title = "OpenUSD D3D12 Composition Smoke";
        Width = 640;
        Height = 480;
        ShowInTaskbar = false;
        _viewport = new CompositionViewportControl
        {
            PresenterFactory = () => _context.Presenter
        };
        _viewport.StatusChanged += OnViewportStatusChanged;
        Content = _viewport;
        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _workflowStarted, 1) != 0)
        {
            return;
        }

        try
        {
            await RunWorkflowAsync();
            SmokeApp.Complete(0);
            _desktop.Shutdown(0);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError(exception.ToString());
            string reason = exception.Message;
            try
            {
                await TeardownAsync();
            }
            catch (Exception cleanupException)
            {
                reason += $" Cleanup failed: {cleanupException.Message}";
                System.Diagnostics.Trace.TraceError(cleanupException.ToString());
            }
            SmokeStatus.Write($"D3D12_SMOKE_FAIL reason={SmokeStatus.Value(reason)}");
            SmokeApp.Complete(1);
            _desktop.Shutdown(1);
        }
    }

    private async Task RunWorkflowAsync()
    {
        await WaitForAsync(
            statistics =>
                statistics.ProbeSucceeded &&
                statistics.SilkRenderedFrameCount >= 6 &&
                statistics.KeyedMutexReuseCount >= 2 &&
                statistics.LastDrawCount > 0 &&
                Volatile.Read(ref _presentedStatusCount) >= 6,
            "initial hdSilk composition frames");
        _context.PresentationRenderer.Pause();
        await WaitForPresentationIdleAsync();
        D3D12CompositionPresenterStatistics initial = _context.Presenter.GetStatistics();
        WindowsClientCaptureResult initialCapture = await CaptureViewportAsync("initial");
        string rendererLuid = Convert.ToHexString([.. _context.Presenter.RendererAdapterLuid]);
        SmokeStatus.Write(
            $"D3D12_SMOKE_INTEROP probe=pass luidMatch=true rendererLuid={rendererLuid} " +
            $"avaloniaPresented={Volatile.Read(ref _presentedStatusCount)} " +
            $"frames={initial.SilkRenderedFrameCount} draws={initial.LastDrawCount} " +
            $"revision={initial.LastSceneRevision} reuses={initial.KeyedMutexReuseCount}");

        ulong initialRevision = initial.LastSceneRevision;
        long editStartFrame = initial.SilkRenderedFrameCount;
        long editStartRendererFrame = _context.PresentationRenderer.FrameCount;
        int editStartPresented = Volatile.Read(ref _presentedStatusCount);
        _context.PresentationRenderer.Resume();
        _viewport.RequestPresentationForDiagnostics();
        await _context.Scheduler.EditAsync(
            static stage => SharedStageNativeDiagnostics.SetDisplayColor(
                stage,
                "/World/Cube",
                0.95f,
                0.05f,
                0.15f),
            UsdStageInvalidationKind.Property);
        await WaitForAsync(
            statistics =>
                statistics.LastSceneRevision > initialRevision &&
                statistics.SilkRenderedFrameCount >= editStartFrame + 2 &&
                _context.PresentationRenderer.LastMeshUpsertFrame > editStartRendererFrame &&
                Volatile.Read(ref _presentedStatusCount) >= editStartPresented + 2,
            "shared-stage display-color edit");
        _context.PresentationRenderer.Pause();
        await WaitForPresentationIdleAsync();
        D3D12CompositionPresenterStatistics edited = _context.Presenter.GetStatistics();
        WindowsClientCaptureResult editedCapture = await CaptureViewportAsync("edited");
        (long changedPixels, double meanDelta) =
            WindowsClientCaptureResult.Compare(initialCapture, editedCapture);
        SmokeStatus.Write(
            $"D3D12_SMOKE_EDIT edits=1 revision={initialRevision}->{edited.LastSceneRevision} " +
            $"frames={editStartFrame}->{edited.SilkRenderedFrameCount} " +
            $"meshUpsertFrame={_context.PresentationRenderer.LastMeshUpsertFrame} " +
            $"draws={edited.LastDrawCount}");
        SmokeStatus.Write(
            $"D3D12_SMOKE_PIXELS api=PrintWindow initialHash={initialCapture.Evidence.Sha256} " +
            $"editedHash={editedCapture.Evidence.Sha256} " +
            $"initialScenePixels={initialCapture.Evidence.NonBackgroundPixels} " +
            $"editedScenePixels={editedCapture.Evidence.NonBackgroundPixels} " +
            $"changedPixels={changedPixels} meanDelta={meanDelta:F4} " +
            $"initialSamples={string.Join(',', initialCapture.Evidence.Samples)} " +
            $"editedSamples={string.Join(',', editedCapture.Evidence.Samples)}");

        CompositionViewportSessionStatistics lifecycleBefore =
            _viewport.GetSessionStatistics();
        if (lifecycleBefore.SurfaceUpdateStartedCount !=
                lifecycleBefore.SurfaceUpdateCompletedCount ||
            lifecycleBefore.LastPresentedGenerationId != lifecycleBefore.CurrentGenerationId)
        {
            throw new InvalidOperationException(
                "The old generation did not reach a completed composed update before resize.");
        }
        long resizeStartGeneration = edited.GenerationCount;
        long resizeStartFrames = edited.SilkRenderedFrameCount;
        long resizeStartReuse = edited.KeyedMutexReuseCount;
        long resizeStartAllocation = edited.LastAllocationId;
        int resizeStartPresented = Volatile.Read(ref _presentedStatusCount);
        int initialWidth = edited.LastWidth;
        int initialHeight = edited.LastHeight;
        _context.PresentationRenderer.Resume();
        _viewport.RequestPresentationForDiagnostics();
        Width = 820;
        Height = 540;
        await Task.Delay(50);
        await WaitForAsync(
            statistics =>
                statistics.GenerationCount > resizeStartGeneration &&
                statistics.ActiveGenerations == 1 &&
                (statistics.LastWidth != initialWidth ||
                    statistics.LastHeight != initialHeight) &&
                statistics.SilkRenderedFrameCount >= resizeStartFrames + 6 &&
                statistics.KeyedMutexReuseCount >= resizeStartReuse + 2 &&
                statistics.LastAllocationId > resizeStartAllocation &&
                Volatile.Read(ref _presentedStatusCount) >= resizeStartPresented + 6,
            "resize generation replacement");
        _context.PresentationRenderer.Pause();
        await WaitForPresentationIdleAsync();
        D3D12CompositionPresenterStatistics resized = _context.Presenter.GetStatistics();
        CompositionViewportSessionStatistics lifecycleAfter =
            _viewport.GetSessionStatistics();
        var lifecycleEvidence = new SmokeLifecycleEvidence(
            lifecycleBefore.CurrentGenerationId,
            lifecycleAfter.CurrentGenerationId,
            lifecycleBefore.SurfaceUpdateStartedCount,
            lifecycleAfter.SurfaceUpdateStartedCount,
            lifecycleBefore.SurfaceUpdateCompletedCount,
            lifecycleAfter.SurfaceUpdateCompletedCount,
            lifecycleBefore.GenerationRetirementStartedCount,
            lifecycleAfter.GenerationRetirementStartedCount,
            lifecycleBefore.GenerationRetirementCompletedCount,
            lifecycleAfter.GenerationRetirementCompletedCount,
            lifecycleAfter.LastRetiredGenerationId,
            lifecycleBefore.ImportedFrameDisposalCount,
            lifecycleAfter.ImportedFrameDisposalCount,
            lifecycleAfter.StaleImportedFrameReuseCount);
        SmokeStatus.Write(
            $"D3D12_SMOKE_RESIZE resizes={resized.GenerationCount - 1} " +
            $"generations={resized.GenerationCount} active={resized.ActiveGenerations} " +
            $"size={initialWidth}x{initialHeight}->{resized.LastWidth}x{resized.LastHeight} " +
            $"frames={resized.SilkRenderedFrameCount} reuses={resized.KeyedMutexReuseCount} " +
            $"updates={resizeStartPresented}->{Volatile.Read(ref _presentedStatusCount)} " +
            $"allocations={resizeStartAllocation}->{resized.LastAllocationId} " +
            $"updateStarted={lifecycleEvidence.UpdateStartedBefore}->" +
            $"{lifecycleEvidence.UpdateStartedAfter} " +
            $"updateCompleted={lifecycleEvidence.UpdateCompletedBefore}->" +
            $"{lifecycleEvidence.UpdateCompletedAfter} " +
            $"retirementStarted={lifecycleEvidence.RetirementStartedBefore}->" +
            $"{lifecycleEvidence.RetirementStartedAfter} " +
            $"retirementCompleted={lifecycleEvidence.RetirementCompletedBefore}->" +
            $"{lifecycleEvidence.RetirementCompletedAfter} " +
            $"lastRetiredGeneration={lifecycleEvidence.LastRetiredGenerationId} " +
            $"importDisposals={lifecycleEvidence.ImportedDisposalsBefore}->" +
            $"{lifecycleEvidence.ImportedDisposalsAfter} " +
            $"staleImportReuse={lifecycleEvidence.StaleImportReuseCount}");

        await TeardownAsync();
        D3D12CompositionPresenterStatistics teardown = _context.Presenter.GetStatistics();
        if (teardown.ActiveGenerations != 0 ||
            teardown.ActiveFrames != 0 ||
            teardown.RetainedPresentationCopies != 0)
        {
            throw new InvalidOperationException(
                $"Teardown left reclaimable resources: generations={teardown.ActiveGenerations}, " +
                $"frames={teardown.ActiveFrames}, retained={teardown.RetainedPresentationCopies}.");
        }

        SmokeStatus.Write(
            $"D3D12_SMOKE_TEARDOWN activeGenerations={teardown.ActiveGenerations} " +
            $"activeFrames={teardown.ActiveFrames} " +
            $"retainedCopies={teardown.RetainedPresentationCopies}");
        var artifact = new SmokePixelEvidenceArtifact(
            1,
            SmokePixelEvidenceArtifact.RequiredCaptureApi,
            initialCapture.Evidence,
            editedCapture.Evidence,
            changedPixels,
            meanDelta,
            lifecycleEvidence,
            new SmokeTeardownEvidence(
                teardown.ActiveGenerations,
                teardown.ActiveFrames,
                teardown.RetainedPresentationCopies));
        string evidencePath = Path.Combine(GetArtifactDirectory(), "pixel-evidence.json");
        File.WriteAllText(evidencePath, artifact.ToJson());
        SmokeStatus.Write(
            $"D3D12_SMOKE_PASS frames={teardown.SilkRenderedFrameCount} edits=1 " +
            $"resizes={teardown.GenerationCount - 1} reuses={teardown.KeyedMutexReuseCount} " +
            $"revision={initialRevision}->{teardown.LastSceneRevision} " +
            $"pixelEvidence={SmokeStatus.Value(evidencePath)}");
    }

    private async Task TeardownAsync()
    {
        if (Interlocked.Exchange(ref _teardownStarted, 1) != 0)
        {
            return;
        }
        _viewport.StatusChanged -= OnViewportStatusChanged;
        _context.PresentationRenderer.Pause();
        await WaitForPresentationIdleAsync();
        await _viewport.DisposeAsync();
        Content = null;
        await _context.DisposeAsync();
    }

    private async Task WaitForPresentationIdleAsync()
    {
        long previous = _context.PresentationRenderer.FrameCount;
        for (int attempt = 0; attempt < 50; attempt++)
        {
            await Task.Delay(100);
            long current = _context.PresentationRenderer.FrameCount;
            CompositionViewportSessionStatistics statistics =
                _viewport.GetSessionStatistics();
            if (current == previous &&
                statistics.SurfaceUpdateStartedCount ==
                    statistics.SurfaceUpdateCompletedCount)
            {
                await Task.Delay(250);
                CompositionViewportSessionStatistics confirmed =
                    _viewport.GetSessionStatistics();
                if (_context.PresentationRenderer.FrameCount == current &&
                    confirmed.SurfaceUpdateStartedCount ==
                        confirmed.SurfaceUpdateCompletedCount)
                {
                    return;
                }
            }
            previous = current;
        }
        throw new TimeoutException("The D3D12 presentation pump did not become idle.");
    }

    private async Task<WindowsClientCaptureResult> CaptureViewportAsync(string phase)
    {
        IPlatformHandle handle = TryGetPlatformHandle() ??
            throw new InvalidOperationException("Avalonia did not expose a platform window handle.");
        if (!string.Equals(handle.HandleDescriptor, "HWND", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Expected an HWND capture target, got '{handle.HandleDescriptor}'.");
        }
        Point origin = _viewport.TranslatePoint(default, this) ??
            throw new InvalidOperationException("Could not locate the viewport in the client area.");
        double scale = RenderScaling;
        var crop = new PixelCaptureRectangle(
            checked((int)Math.Round(origin.X * scale)),
            checked((int)Math.Round(origin.Y * scale)),
            checked((int)Math.Round(_viewport.Bounds.Width * scale)),
            checked((int)Math.Round(_viewport.Bounds.Height * scale)));
        string path = Path.Combine(GetArtifactDirectory(), $"composed-{phase}.bmp");
        return await Task.Run(
            () => WindowsClientCapture.Capture(handle.Handle, crop, phase, path));
    }

    private static string GetArtifactDirectory()
    {
        string? value = Environment.GetEnvironmentVariable("OPENUSD_ARTIFACT_DIR");
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                "OPENUSD_ARTIFACT_DIR is required for composed pixel evidence.");
        }
        string path = Path.GetFullPath(value);
        Directory.CreateDirectory(path);
        return path;
    }

    private async Task WaitForAsync(
        Func<D3D12CompositionPresenterStatistics, bool> predicate,
        string milestone)
    {
        DateTime deadline = DateTime.UtcNow + MilestoneTimeout;
        while (!predicate(_context.Presenter.GetStatistics()))
        {
            if (DateTime.UtcNow >= deadline)
            {
                D3D12CompositionPresenterStatistics statistics =
                    _context.Presenter.GetStatistics();
                throw new TimeoutException(
                    $"Timed out waiting for {milestone}; status={_viewport.Status}, " +
                    $"frames={statistics.SilkRenderedFrameCount}, " +
                    $"generations={statistics.GenerationCount}, " +
                    $"active={statistics.ActiveGenerations}, " +
                    $"reuses={statistics.KeyedMutexReuseCount}, " +
                    $"revision={statistics.LastSceneRevision}, " +
                    $"draws={statistics.LastDrawCount}.");
            }
            await Task.Delay(25);
        }
    }

    private void OnViewportStatusChanged(object? sender, string status)
    {
        if (status.Contains("frame", StringComparison.OrdinalIgnoreCase))
        {
            _ = Interlocked.Increment(ref _presentedStatusCount);
        }
        else
        {
            SmokeStatus.Write($"D3D12_SMOKE_STATUS viewport={SmokeStatus.Value(status)}");
        }
    }

    public ValueTask DisposeAsync() => _viewport.DisposeAsync();
}
