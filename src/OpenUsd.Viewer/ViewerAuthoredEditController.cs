// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using OpenUsd.Editing;

namespace OpenUsd.Viewer;

internal sealed record ViewerAuthoredEditCapture(
    Guid DocumentId, string LayerIdentifier, UsdLayerAuthoredSnapshot Snapshot) : IUsdDetachedResult;

internal sealed record ViewerAuthoredEditResult(
    UsdLayerEditOutcome Outcome, UsdLayerAuthoredSnapshot? After, string Message,
    ulong BeforeSerial = 0, ulong AfterSerial = 0);

internal sealed partial class ViewerAuthoredEditController : IAsyncDisposable, IViewerEditHistoryView
{
    private const int MaximumAddresses = 256;
    private readonly UsdStageScheduler _scheduler;
    private readonly Guid _documentId = Guid.NewGuid();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime;
    private readonly ViewerEditHistory<ViewerAuthoredEditStep> _history = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private bool _disposed;

    internal ViewerAuthoredEditController(
        UsdStageScheduler scheduler, CancellationToken lifetime = default) : this(scheduler, null, lifetime)
    {
    }

    internal ViewerAuthoredEditController(
        UsdStageScheduler scheduler, UsdReviewSourceBinding? sourceBinding, CancellationToken lifetime = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        _scheduler = scheduler;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        _sourceBinding = sourceBinding;
    }

    public bool CanUndo => !_disposed && !IsSuspended && _history.CanUndo;

    public bool CanRedo => !_disposed && !IsSuspended && _history.CanRedo;

    public int UndoDepth => _history.UndoDepth;

    public int RedoDepth => _history.RedoDepth;

    internal long RetainedBytes => _history.RetainedBytes;

    public string UndoDescription => _history.UndoDescription;

    public string RedoDescription => _history.RedoDescription;

    internal event Action? Changed;

    internal event Action<ViewerAuthoredEditResult>? Applied;

    internal async Task<ViewerAuthoredEditCapture> CaptureAsync(
        IReadOnlyList<UsdLayerEditAddress> addresses, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(addresses.Count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(addresses.Count, MaximumAddresses);
        ObjectDisposedException.ThrowIf(_disposed, this);
        UsdLayerEditAddress[] copy = [.. addresses];
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (IsSuspended)
            {
                throw new InvalidOperationException("Finish or cancel the document transition before editing.");
            }
            return await _scheduler.InvokeAsync(stage =>
            {
                if (_initialSessionIdentity is null)
                {
                    _ = ReadDocument(stage);
                }
                return CaptureReview(stage, copy);
            }, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<ViewerAuthoredEditResult> ApplyAsync(
        ViewerAuthoredEditCapture capture,
        IReadOnlyList<UsdLayerEdit> edits,
        string description,
        Guid? gestureId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(description.Length, 1024);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(edits.Count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(edits.Count, MaximumAddresses);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (capture.DocumentId != _documentId)
        {
            return new ViewerAuthoredEditResult(
                UsdLayerEditOutcome.StaleTarget, null, "The captured edit belongs to a different document.");
        }
        if (gestureId == Guid.Empty)
        {
            throw new ArgumentException("A gesture identity cannot be empty.", nameof(gestureId));
        }
        UsdLayerEdit[] copy = [.. edits];
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        bool changed = false;
        ViewerAuthoredEditResult? outcome = null;
        try
        {
            if (IsSuspended)
            {
                return TransitionBlocked();
            }
            ApplicationResult result = await _scheduler.EditAsync(stage =>
            {
                ulong before = stage.ChangeSerial;
                using UsdLayer layer = stage.GetLocalLayer(capture.LayerIdentifier);
                if (ViewerCameraBookmarkCatalog.UsesReservedNamespace(capture.Snapshot.Addresses))
                {
                    ValidateBookmarkCapture(stage, layer, layer.CaptureAuthored([.. capture.Snapshot.Addresses]));
                    ViewerCameraBookmarkCatalog.ValidateChange(
                        copy, capture.Snapshot,
                        ViewerCameraBookmarkCatalog.CreateSourceStamp(CameraBookmarkSourceBinding));
                }
                UsdLayerEditResult applied = layer.CompareAndApply(capture.Snapshot, copy);
                return new ApplicationResult(applied, before, stage.ChangeSerial);
            }, UsdStageInvalidationKind.Property, linked.Token).ConfigureAwait(false);
            if (result.Native.Outcome == UsdLayerEditOutcome.Applied)
            {
                UsdLayerAuthoredSnapshot after = RequireAfter(result.Native);
                if (!ViewerAuthoredEditStep.HasSameAffectedState(capture.Snapshot, after))
                {
                    _history.Record(new ViewerAuthoredEditStep(
                        description, capture.LayerIdentifier, capture.Snapshot, after),
                        _clock.Elapsed.TotalSeconds, gestureId);
                    changed = true;
                }
            }
            outcome = ToResult(
                result, changed ? "Review edit applied." : "The review opinion was already in this state.");
            return outcome;
        }
        finally
        {
            _gate.Release();
            if (changed)
            {
                Applied?.Invoke(outcome!);
                Changed?.Invoke();
            }
        }
    }

    internal Task<ViewerAuthoredEditResult> UndoAsync(CancellationToken cancellationToken = default) =>
        ReplayAsync(undo: true, cancellationToken);

    internal Task<ViewerAuthoredEditResult> RedoAsync(CancellationToken cancellationToken = default) =>
        ReplayAsync(undo: false, cancellationToken);

    private async Task<ViewerAuthoredEditResult> ReplayAsync(bool undo, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        ViewerEditHistory<ViewerAuthoredEditStep>.PendingChange? pending = null;
        bool changed = false;
        ViewerAuthoredEditResult? outcome = null;
        try
        {
            if (IsSuspended)
            {
                return TransitionBlocked();
            }
            bool available = undo ? _history.TryBeginUndo(out pending) : _history.TryBeginRedo(out pending);
            if (!available)
            {
                return new ViewerAuthoredEditResult(
                    UsdLayerEditOutcome.NotEditable, null,
                    undo ? "There is nothing to undo." : "There is nothing to redo.");
            }
            ViewerAuthoredEditStep step = pending!.Step;
            ApplicationResult result = await _scheduler.EditAsync(stage =>
            {
                ulong before = stage.ChangeSerial;
                using UsdLayer layer = stage.GetLocalLayer(step.LayerIdentifier);
                if (ViewerCameraBookmarkCatalog.UsesReservedNamespace(step.Before.Addresses))
                {
                    ValidateBookmarkCapture(stage, layer, layer.CaptureAuthored([.. step.Before.Addresses]));
                    _ = ViewerCameraBookmarkCatalog.Decode(undo ? step.Before : step.After,
                        ViewerCameraBookmarkCatalog.CreateSourceStamp(CameraBookmarkSourceBinding));
                }
                UsdLayerEditResult restored = layer.CompareAndRestore(
                    undo ? step.After : step.Before, undo ? step.Before : step.After);
                return new ApplicationResult(restored, before, stage.ChangeSerial);
            }, UsdStageInvalidationKind.Property, linked.Token).ConfigureAwait(false);
            if (result.Native.Outcome == UsdLayerEditOutcome.Applied)
            {
                UsdLayerAuthoredSnapshot after = RequireAfter(result.Native);
                var updated = new ViewerAuthoredEditStep(
                    step.Description, step.LayerIdentifier, undo ? after : step.Before, undo ? step.After : after);
                _history.Complete(pending, applied: true, updated);
            }
            else
            {
                _history.Complete(pending, applied: false);
            }
            pending = null;
            changed = true;
            outcome = ToResult(result, undo ? "Review edit undone." : "Review edit redone.");
            return outcome;
        }
        finally
        {
            if (pending is not null)
            {
                _history.Complete(pending, applied: false);
            }
            _gate.Release();
            if (changed)
            {
                if (outcome is { Outcome: UsdLayerEditOutcome.Applied })
                {
                    Applied?.Invoke(outcome);
                }
                Changed?.Invoke();
            }
        }
    }

    private static UsdLayerAuthoredSnapshot RequireAfter(UsdLayerEditResult result) =>
        result.AfterSnapshot ?? throw new InvalidDataException("Applied editing result has no authored after-state.");

    private static ViewerAuthoredEditResult TransitionBlocked() => new(
        UsdLayerEditOutcome.NotEditable, null, "Finish or cancel the document transition before editing.");

    private static ViewerAuthoredEditResult ToResult(ApplicationResult result, string appliedMessage) => new(
        result.Native.Outcome, result.Native.AfterSnapshot,
        result.Native.Outcome == UsdLayerEditOutcome.Applied ? appliedMessage :
            result.Native.Diagnostic ?? $"The edit was refused: {result.Native.Outcome}.",
        result.BeforeSerial, result.AfterSerial);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _lifetime.Cancel();
        await _gate.WaitAsync().ConfigureAwait(false);
        _history.Clear();
        _gate.Release();
        _gate.Dispose();
        _lifetime.Dispose();
    }

    private sealed record ApplicationResult(
        UsdLayerEditResult Native, ulong BeforeSerial, ulong AfterSerial) : IUsdDetachedResult;
}
