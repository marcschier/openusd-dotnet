// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd;

/// <summary>Owns exclusive scheduler admission during validated document teardown.</summary>
/// <remarks>
/// Obtain this capability from <see cref="UsdStageScheduler.TryPrepareRetirementAsync"/>.
/// Cleanup callbacks may release owner-controlled transient state; they must not introduce
/// unreviewed source edits after the retirement predicate. No ambient bypass is granted to
/// host callbacks. Commit only after releasing render sources and leases. Disposal without
/// a successful commit drains owner cleanup and resumes the still-owned stage.
/// </remarks>
public sealed class UsdStageRetirementLease : IAsyncDisposable, IUsdStageBound
{
    private readonly UsdStageScheduler _scheduler;

    internal UsdStageRetirementLease(UsdStageScheduler scheduler) => _scheduler = scheduler;

    internal bool IsPrepared { get; set; }
    internal bool IsReleased { get; set; }
    internal bool IsCommitted { get; set; }
    internal bool IsCommitting { get; set; }
    internal TaskCompletionSource? CloseCompletion { get; set; }

    /// <summary>Runs explicitly authorized owner cleanup while foreign producer admission is closed.</summary>
    /// <remarks>The ordinary scheduler result and stage-ownership guards also apply to cleanup.</remarks>
    public ValueTask<T> InvokeCleanupAsync<T>(
        Func<UsdStage, T> action,
        CancellationToken cancellationToken = default) =>
        _scheduler.InvokeRetirementCleanupAsync(this, action, cancellationToken);

    /// <summary>Runs explicitly authorized owner cleanup without returning a value.</summary>
    public async ValueTask InvokeCleanupAsync(
        Action<UsdStage> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        await InvokeCleanupAsync(stage =>
        {
            action(stage);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Drains cleanup and disposes the scheduler after all render registrations are released.</summary>
    /// <remarks>
    /// An active registration causes an explicit failure, leaving the fence held for cleanup and retry
    /// or disposal of this lease. Once retirement commits, it cannot be reversed.
    /// </remarks>
    public ValueTask CommitAsync() => _scheduler.CloseRetirementAsync(this, commit: true);

    /// <summary>Drains cleanup and resumes scheduling unless retirement has already committed.</summary>
    public ValueTask DisposeAsync() => _scheduler.CloseRetirementAsync(this, commit: false);
}
