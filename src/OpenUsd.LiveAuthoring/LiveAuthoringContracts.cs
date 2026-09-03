// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.LiveAuthoring;

/// <summary>Receives ordered, renderer-neutral live-authoring batches.</summary>
public interface ILiveAuthoringSink
{
    /// <summary>
    /// Queues a batch and completes as soon as it is admitted (enqueued or coalesced into a pending
    /// tail). The returned receipt separately exposes the eventual applied result; it does not wait for
    /// execution.
    /// </summary>
    ValueTask<LiveAuthoringAdmissionReceipt> ApplyAsync(
        LiveAuthoringBatch batch,
        CancellationToken cancellationToken = default);
}

/// <summary>Applies one admitted batch to its destination.</summary>
public interface ILiveAuthoringBatchExecutor : IAsyncDisposable
{
    /// <summary>Applies a batch after queue ordering and backpressure have been enforced.</summary>
    ValueTask<LiveAuthoringBatchResult> ExecuteAsync(
        LiveAuthoringBatch batch,
        CancellationToken cancellationToken);
}

/// <summary>Consumes the exact scheduler-owned stage identity used for rendering.</summary>
public interface IUsdStageRenderSourceConsumer : IAsyncDisposable
{
    /// <summary>
    /// Attaches to the scheduler and retained render source. The consumer must not reopen a stage path.
    /// </summary>
    ValueTask AttachAsync(
        UsdStageScheduler scheduler,
        UsdStageRenderSource renderSource,
        CancellationToken cancellationToken);
}

/// <summary>Selects the layer used for live edits.</summary>
public enum LiveAuthoringEditLayer
{
    /// <summary>Authors transient edits into the stage session layer.</summary>
    Session,

    /// <summary>Authors persistent edits into the root layer.</summary>
    Root
}

/// <summary>Selects whether a host creates or opens its scheduler-owned stage.</summary>
public enum LiveAuthoringStageMode
{
    /// <summary>Create a new file-backed stage.</summary>
    Create,

    /// <summary>Open an existing stage.</summary>
    Open
}

/// <summary>Configures bounded live-authoring and scheduler queues.</summary>
public sealed class UsdLiveAuthoringOptions
{
    /// <summary>Gets or sets the maximum number of batches waiting behind the active edit.</summary>
    public int QueueCapacity { get; set; } = 64;

    /// <summary>Gets or sets the scheduler operation queue capacity.</summary>
    public int SchedulerCapacity { get; set; } = 1024;

    /// <summary>Gets or sets the scheduler change-notification queue capacity.</summary>
    public int NotificationCapacity { get; set; } = 64;

    /// <summary>Gets or sets the default edit layer.</summary>
    public LiveAuthoringEditLayer EditLayer { get; set; } = LiveAuthoringEditLayer.Session;

    /// <summary>
    /// Gets or sets an optional observer notified of bounded, structured admission and execution health
    /// events. A <see langword="null"/> observer disables event reporting; queue metrics remain
    /// available through <see cref="QueuedLiveAuthoringSink.GetHealthSnapshot"/> either way.
    /// </summary>
    public IProgress<LiveAuthoringHealthEvent>? HealthObserver { get; set; }
}
