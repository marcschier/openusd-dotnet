// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

public sealed partial class MainWindow
{
    private readonly ViewerCompletedJobHistory _completedJobs = new();
    private CompletedJobHistoryWindow? _completedJobHistoryWindow;

    private void PublishCompletedJob(RenderDiskJobResult job, string description)
    {
        _completedJobs.Publish(job, description);
        _completedJobHistoryWindow?.Refresh();
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
        var window = new CompletedJobHistoryWindow(_completedJobs);
        _completedJobHistoryWindow = window;
        window.Closed += (_, _) =>
        {
            _completedJobHistoryWindow = null;
            RestoreWorkspaceFocus(focus);
        };
        window.Show(this);
    }
}
