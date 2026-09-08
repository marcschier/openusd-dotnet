// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd;

public sealed partial class UsdStageScheduler
{
    private int _pendingSubmissions;
    private TaskCompletionSource? _submissionsDrained;
    private UsdStageRetirementLease? _retirement;

    /// <summary>Fences producers and conditionally prepares retirement on the stage owner thread.</summary>
    /// <remarks>
    /// Operations admitted before the fence, including writers awaiting queue capacity, drain before
    /// the predicate. New Invoke, Edit, render-source and render-lease acquisitions are refused.
    /// A false predicate returns null; predicate failure or cancellation releases the fence.
    /// On success, only the returned lease can schedule cleanup or commit disposal. Disposing that
    /// lease instead resumes ordinary scheduling without disposing the stage.
    /// </remarks>
    public async ValueTask<UsdStageRetirementLease?> TryPrepareRetirementAsync(
        Func<UsdStage, bool> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ThrowIfOwnerThreadReentrancy();
        cancellationToken.ThrowIfCancellationRequested();
        var retirement = new UsdStageRetirementLease(this);
        Task submissions;
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposeState != 0, this);
            if (_retirement is not null)
            {
                throw new InvalidOperationException("A scheduler retirement fence is already held.");
            }
            _retirement = retirement;
            submissions = PendingSubmissions();
        }

        try
        {
            await submissions.WaitAsync(cancellationToken).ConfigureAwait(false);
            bool accepted = await EnqueueRetirementControlAsync(
                retirement, predicate, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!accepted)
            {
                ReleasePreparation(retirement);
                return null;
            }
            lock (_lifetimeGate)
            {
                retirement.IsPrepared = true;
            }
            return retirement;
        }
        catch
        {
            ReleasePreparation(retirement);
            throw;
        }
    }

    internal ValueTask<T> InvokeRetirementCleanupAsync<T>(
        UsdStageRetirementLease retirement,
        Func<UsdStage, T> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        ThrowIfOwnerThreadReentrancy();
        UsdStageBoundResultGuard.ThrowIfForbiddenType(typeof(T));
        var item = CreateRetirementWorkItem(action, cancellationToken);
        lock (_lifetimeGate)
        {
            RequireRetirement(retirement);
            if (!retirement.IsPrepared || retirement.CloseCompletion is not null)
            {
                throw new InvalidOperationException("Retirement cleanup is not currently available.");
            }
            _pendingSubmissions++;
        }
        return EnqueueAdmittedAsync(item, cancellationToken);
    }

    internal ValueTask CloseRetirementAsync(UsdStageRetirementLease retirement, bool commit)
    {
        ThrowIfOwnerThreadReentrancy();
        Task submissions;
        TaskCompletionSource completion;
        lock (_lifetimeGate)
        {
            if (retirement.IsCommitted)
            {
                return new ValueTask(_completion.Task);
            }
            if (retirement.IsReleased)
            {
                ObjectDisposedException.ThrowIf(commit, retirement);
                return ValueTask.CompletedTask;
            }
            RequireRetirement(retirement);
            if (retirement.CloseCompletion is not null)
            {
                if (commit && !retirement.IsCommitting)
                {
                    throw new InvalidOperationException("The retirement fence is already being released.");
                }
                return new ValueTask(retirement.CloseCompletion.Task);
            }
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            retirement.CloseCompletion = completion;
            retirement.IsCommitting = commit;
            submissions = PendingSubmissions();
        }

        _ = CompleteRetirementCloseAsync(retirement, commit, submissions, completion);
        return new ValueTask(completion.Task);
    }

    private async Task CompleteRetirementCloseAsync(
        UsdStageRetirementLease retirement,
        bool commit,
        Task submissions,
        TaskCompletionSource completion)
    {
        try
        {
            await submissions.ConfigureAwait(false);
            await EnqueueRetirementControlAsync(retirement, _ =>
            {
                lock (_lifetimeGate)
                {
                    RequireRetirement(retirement);
                    if (commit && _activeRenderSources != 0)
                    {
                        throw new InvalidOperationException(
                            "Dispose all active render sources and leases before committing retirement.");
                    }
                    if (commit)
                    {
                        _disposeState = 1;
                        retirement.IsCommitted = true;
                        _queue.Writer.TryComplete();
                    }
                    retirement.IsReleased = true;
                    _retirement = null;
                }
                return true;
            }, CancellationToken.None).ConfigureAwait(false);
            if (commit)
            {
                await _completion.Task.ConfigureAwait(false);
            }
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            lock (_lifetimeGate)
            {
                if (ReferenceEquals(_retirement, retirement) && !retirement.IsReleased)
                {
                    retirement.CloseCompletion = null;
                    retirement.IsCommitting = false;
                }
            }
            completion.TrySetException(exception);
        }
    }

    private ValueTask<T> EnqueueRetirementControlAsync<T>(
        UsdStageRetirementLease retirement,
        Func<UsdStage, T> action,
        CancellationToken cancellationToken)
    {
        var item = CreateRetirementWorkItem(action, cancellationToken);
        lock (_lifetimeGate)
        {
            RequireRetirement(retirement);
            _pendingSubmissions++;
        }
        return EnqueueAdmittedAsync(item, cancellationToken);
    }

    private StageWorkItem<T> CreateRetirementWorkItem<T>(
        Func<UsdStage, T> action,
        CancellationToken cancellationToken) =>
        new(action, UsdStageInvalidationKind.Full, _changes, cancellationToken,
            recordInvalidation: RecordInvalidation);

    private void AdmitSubmission()
    {
        ObjectDisposedException.ThrowIf(_disposeState != 0, this);
        if (_retirement is not null)
        {
            throw new InvalidOperationException("Scheduler admission is closed by a retirement fence.");
        }
        _pendingSubmissions++;
    }

    private Task PendingSubmissions() =>
        _pendingSubmissions == 0
            ? Task.CompletedTask
            : (_submissionsDrained ??=
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

    private void CompleteSubmission()
    {
        lock (_lifetimeGate)
        {
            if (_pendingSubmissions <= 0)
            {
                throw new InvalidOperationException("The scheduler has no pending submission to complete.");
            }
            if (--_pendingSubmissions == 0)
            {
                _submissionsDrained?.TrySetResult();
                _submissionsDrained = null;
            }
        }
    }

    private void RequireRetirement(UsdStageRetirementLease retirement)
    {
        ObjectDisposedException.ThrowIf(_disposeState != 0, this);
        ObjectDisposedException.ThrowIf(
            !ReferenceEquals(_retirement, retirement) || retirement.IsReleased, retirement);
    }

    private void ReleasePreparation(UsdStageRetirementLease retirement)
    {
        lock (_lifetimeGate)
        {
            if (ReferenceEquals(_retirement, retirement))
            {
                retirement.IsReleased = true;
                _retirement = null;
            }
        }
    }
}
