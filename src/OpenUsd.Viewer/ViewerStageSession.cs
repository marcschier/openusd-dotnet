// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer;

/// <summary>
/// The viewer-owned stage a host may author into while the shell renders it. Obtained
/// from <see cref="ViewerHostOptions.StageReadyAsync"/>.
/// </summary>
/// <remarks>
/// Authoring through <see cref="Scheduler"/> flows into the scheduler's ordered change
/// feed, which the viewer already pumps, so edits invalidate and redraw without any
/// further call from the host.
/// </remarks>
public sealed class ViewerStageSession
{
    internal ViewerStageSession(UsdStageScheduler scheduler, string stagePath)
    {
        Scheduler = scheduler;
        StagePath = stagePath;
    }

    /// <summary>
    /// The scheduler that owns the open stage. All host authoring must go through it.
    /// </summary>
    public UsdStageScheduler Scheduler { get; }

    /// <summary>
    /// Absolute path of the stage the viewer opened.
    /// </summary>
    public string StagePath { get; }
}
