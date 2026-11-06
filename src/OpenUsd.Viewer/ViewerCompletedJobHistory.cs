// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal sealed class ViewerCompletedJobHistory
{
    internal const int MaximumJobs = 8;
    private readonly List<ViewerCompletedJobHistoryEntry> _jobs = [];

    internal IReadOnlyList<ViewerCompletedJobHistoryEntry> Snapshot() => Array.AsReadOnly(_jobs.ToArray());

    internal void Publish(RenderDiskJobResult job, string description)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(description);
        _jobs.Insert(0, new ViewerCompletedJobHistoryEntry(job, description));
        if (_jobs.Count > MaximumJobs)
        {
            _jobs.RemoveAt(MaximumJobs);
        }
    }
}

internal sealed record ViewerCompletedJobHistoryEntry(RenderDiskJobResult Job, string Description)
{
    public string Label => $"{Description} - {Job.Frames.Count} frames - " +
        Path.GetFileName(Path.TrimEndingDirectorySeparator(Job.OutputDirectory));
}
