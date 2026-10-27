// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

public sealed partial class MainWindow
{
    private readonly ViewerCompletedJobHistory _completedJobs = new();
    private readonly ViewerRecentRenderManifestStore _recentRenderManifests;
    private IReadOnlyList<string> _rememberedRenderManifests = [];
    private Task _rememberRenderManifestTask = Task.CompletedTask;
    private string? _rememberedRenderManifestDiagnostic;
    private CompletedJobHistoryWindow? _completedJobHistoryWindow;

    private void PublishCompletedJob(RenderDiskJobResult job, string description)
    {
        _completedJobs.Publish(job, description);
        _completedJobHistoryWindow?.Refresh();
        RememberRenderManifest(Path.Combine(job.OutputDirectory, "manifest.json"));
    }

    private void RememberRenderManifest(string path)
    {
        if (!IsAutomatedViewerRun())
        {
            _rememberRenderManifestTask = RememberRenderManifestAsync(_rememberRenderManifestTask, path);
        }
    }

    private async Task RememberRenderManifestAsync(Task preceding, string? path)
    {
        await preceding;
        try
        {
            _rememberedRenderManifests = path is null
                ? await _recentRenderManifests.LoadAsync()
                : await _recentRenderManifests.AddAsync(path);
            _rememberedRenderManifestDiagnostic = null;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
            UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            _rememberedRenderManifestDiagnostic =
                $"Remembered render paths could not be {(path is null ? "loaded" : "saved")}: " +
                $"{ViewerPackageErrorFormatter.Format(exception)} Original render files were not changed.";
            ViewerStatus.Text = _rememberedRenderManifestDiagnostic;
            ViewerStartupOptions.WriteStatus(_rememberedRenderManifestDiagnostic);
        }
        _completedJobHistoryWindow?.SetRememberedPaths(
            _rememberedRenderManifests, _rememberedRenderManifestDiagnostic);
    }

    private void ShowCompletedJobs()
    {
        if (_shutdownStarted)
        {
            return;
        }
        if (_completedJobHistoryWindow is { } existing)
        {
            existing.Activate();
            return;
        }
        WorkspaceFocus focus = CaptureWorkspaceFocus();
        var window = new CompletedJobHistoryWindow(_completedJobs,
            rememberedPaths: _rememberedRenderManifests, rememberManifest: RememberRenderManifest);
        window.SetRememberedPaths(_rememberedRenderManifests, _rememberedRenderManifestDiagnostic);
        _completedJobHistoryWindow = window;
        window.Closed += (_, _) =>
        {
            _completedJobHistoryWindow = null;
        };
        RestoreWorkspaceFocusWhenClosed(window, focus);
        window.Show(this);
    }
}
