// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;
using OpenUsd.Interop;

namespace OpenUsd.Viewer;

public sealed partial class MainWindow
{
    private readonly ViewerRecoveryStore _reviewRecoveryStore;
    private string? _reviewRecoveryKey;
    private UsdReviewDocument? _reviewRecoveryDocument;
    private ViewerFileIdentity? _reviewRecoveryIdentity;
    private UsdReviewDocument? _retiringRecoveryDocument;
    private CancellationTokenSource? _reviewRecoveryStop;
    private Task _reviewRecoveryTask = Task.CompletedTask;
    private bool _reviewRecoveryRequested;
    private ulong _reviewRecoveryRevision = ulong.MaxValue;
    private ulong _reviewRecoveryWrittenRevision = ulong.MaxValue;

    private static string RecoveryKey(string path) => Path.GetFullPath(path).ToUpperInvariant();

    private static async Task ValidateRecoverySelectionAsync(
        ViewerPreparedDocument prepared, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows() && prepared.RecoveryDocument is { } document &&
            prepared.RecoveryIdentity is { } expected)
        {
            ViewerFileIdentity current = await ViewerReviewDocumentPublication.ObserveAsync(
                document, expected.FullPath, cancellationToken).ConfigureAwait(false);
            if (!expected.Matches(current))
            {
                throw new IOException(
                    "The recovery checkpoint changed during the decision. The current document was kept.");
            }
        }
    }

    private async Task<ViewerPreparedDocument?> SelectRecoveryAsync(
        ViewerPreparedDocument original, string path, CancellationToken cancellationToken)
    {
        try
        {
            return await OfferRecoveryAsync(original, path, cancellationToken);
        }
        catch
        {
            await original.DisposeAsync();
            throw;
        }
    }

    private void ScheduleReviewRecovery(ViewerAuthoredEditController editor, ViewerDocumentObservation state)
    {
        if (Volatile.Read(ref _disposed) != 0 || !editor.CanSaveReview ||
            state.State.Review is not { HasChanges: true } review ||
            _reviewRecoveryKey is null || _documentBusy || _documentEditBusy || _shutdownStarted ||
            _reviewRecoveryRevision == review.Revision)
        {
            return;
        }
        _reviewRecoveryRevision = review.Revision;
        RequestReviewRecovery(editor);
    }

    private void RequestReviewRecovery(ViewerAuthoredEditController editor)
    {
        if (Volatile.Read(ref _disposed) != 0 || !ReferenceEquals(editor, _documentEditor) ||
            !editor.CanSaveReview || _reviewRecoveryKey is null ||
            _documentBusy || _documentEditBusy || _shutdownStarted)
        {
            return;
        }
        _reviewRecoveryRequested = true;
        if (_reviewRecoveryTask.IsCompleted)
        {
            var stop = CancellationTokenSource.CreateLinkedTokenSource(
                _documentLifetime?.Token ?? _viewerLifetime.Token);
            _reviewRecoveryStop = stop;
            _reviewRecoveryTask = RunReviewRecoveryAsync(editor, _reviewRecoveryKey, stop);
        }
    }

    private async Task RunReviewRecoveryAsync(
        ViewerAuthoredEditController editor, string key, CancellationTokenSource stop)
    {
        try
        {
            while (_reviewRecoveryRequested)
            {
                await Task.Delay(750, stop.Token);
                _reviewRecoveryRequested = false;
                if (!ReferenceEquals(editor, _documentEditor) || key != _reviewRecoveryKey || !editor.CanSaveReview)
                {
                    return;
                }
                ViewerDocumentObservation state = await editor.ReadDocumentAsync(stop.Token);
                if (state.State.Review is not { HasChanges: true } review ||
                    _reviewRecoveryWrittenRevision == review.Revision)
                {
                    continue;
                }
                _documentObservation = state;
                UpdateDocumentCommands();
                Directory.CreateDirectory(_reviewRecoveryStore.RootPath);
                UsdReviewDocument document = await editor.CaptureRecoveryDocumentAsync(
                    _reviewRecoveryStore.GetCheckpointPath(key), retiring: false, stop.Token);
                ViewerDocumentRecovery checkpoint = ViewerDocumentRecovery.FromNative(document, key);
                ViewerFileIdentity identity = await _reviewRecoveryStore.SaveVerifiedAsync(checkpoint, stop.Token);
                if (Volatile.Read(ref _disposed) == 0 &&
                    ReferenceEquals(editor, _documentEditor) && key == _reviewRecoveryKey)
                {
                    _reviewRecoveryDocument = document;
                    _reviewRecoveryIdentity = identity;
                    _reviewRecoveryWrittenRevision = review.Revision;
                    ViewerStartupOptions.WriteStatus("A bounded native review recovery checkpoint was updated.");
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is OpenUsdNativeException or IOException or
            UnauthorizedAccessException or InvalidOperationException or NotSupportedException or
            InvalidDataException or ArgumentException)
        {
            if (Volatile.Read(ref _disposed) == 0 &&
                ReferenceEquals(editor, _documentEditor) && key == _reviewRecoveryKey)
            {
                string message =
                    $"Recovery checkpoint unavailable; review edits remain in memory: {exception.Message}";
                if (!_documentBusy && !_documentEditBusy && !_shutdownStarted)
                {
                    ViewerStatus.Text = message;
                }
                ViewerStartupOptions.WriteStatus(message);
            }
        }
        finally
        {
            stop.Dispose();
            if (ReferenceEquals(_reviewRecoveryStop, stop))
            {
                _reviewRecoveryStop = null;
            }
        }
    }

    private async Task StopReviewRecoveryAsync()
    {
        _reviewRecoveryRequested = false;
        _reviewRecoveryStop?.Cancel();
        await _reviewRecoveryTask;
        _reviewRecoveryRevision = ulong.MaxValue;
        _reviewRecoveryWrittenRevision = ulong.MaxValue;
    }

    private void DisposeReviewRecovery()
    {
        _reviewRecoveryRequested = false;
        _reviewRecoveryStop?.Cancel();
        if (_reviewRecoveryTask.IsCompleted)
        {
            _reviewRecoveryStore.Dispose();
            return;
        }
        _ = _reviewRecoveryTask.ContinueWith(static (completed, state) =>
        {
            if (completed.Exception is { } failure)
            {
                ViewerStartupOptions.WriteStatus($"Recovery shutdown failed: {failure.GetBaseException().Message}");
            }
            ((ViewerRecoveryStore)state!).Dispose();
        }, _reviewRecoveryStore, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task ClearCurrentRecoveryAsync(
        ViewerAuthoredEditController editor, CancellationToken cancellationToken)
    {
        if (_reviewRecoveryIdentity is not { } identity || _reviewRecoveryKey is not { } key)
        {
            return;
        }
        try
        {
            UsdReviewDocument document = await editor.CaptureRecoveryDocumentAsync(
                _reviewRecoveryStore.GetCheckpointPath(key), retiring: false, cancellationToken);
            await _reviewRecoveryStore.DeleteVerifiedAsync(document, identity, cancellationToken);
            _reviewRecoveryIdentity = null;
            _reviewRecoveryDocument = null;
        }
        catch (Exception exception) when (exception is OpenUsdNativeException or IOException or
            UnauthorizedAccessException or InvalidOperationException or NotSupportedException or
            InvalidDataException or ArgumentException)
        {
            ViewerStatus.Text = $"Review published; recovery cleanup was refused: {exception.Message}";
            ViewerStartupOptions.WriteStatus(ViewerStatus.Text);
        }
    }

    private async Task PrepareRecoveryRetirementAsync(
        ViewerAuthoredEditController editor, CancellationToken cancellationToken)
    {
        await StopReviewRecoveryAsync();
        _retiringRecoveryDocument = null;
        if (_reviewRecoveryIdentity is null || _reviewRecoveryKey is not { } key)
        {
            return;
        }
        try
        {
            _retiringRecoveryDocument = await editor.CaptureRecoveryDocumentAsync(
                _reviewRecoveryStore.GetCheckpointPath(key), retiring: true, cancellationToken);
        }
        catch (Exception exception) when (exception is OpenUsdNativeException or IOException or
            InvalidOperationException or NotSupportedException or InvalidDataException or ArgumentException)
        {
            ViewerStartupOptions.WriteStatus($"Recovery was retained during retirement: {exception.Message}");
        }
    }

    private async Task RemoveRetiringRecoveryAsync()
    {
        if (_retiringRecoveryDocument is not { } document || _reviewRecoveryIdentity is not { } identity)
        {
            return;
        }
        _retiringRecoveryDocument = null;
        try
        {
            await _reviewRecoveryStore.DeleteVerifiedAsync(document, identity, CancellationToken.None);
        }
        catch (Exception exception) when (exception is OpenUsdNativeException or IOException or
            UnauthorizedAccessException or InvalidOperationException or NotSupportedException or
            InvalidDataException or ArgumentException)
        {
            ViewerStartupOptions.WriteStatus(
                $"Recovery cleanup was refused; the checkpoint was retained: {exception.Message}");
        }
    }

    private async Task<ViewerPreparedDocument?> OfferRecoveryAsync(
        ViewerPreparedDocument original, string openedPath, CancellationToken cancellationToken)
    {
        ViewerPreparedDocument? recovery;
        string key = RecoveryKey(openedPath);
        try
        {
            recovery = await ViewerReviewRecoveryCandidate.PrepareAsync(
                _reviewRecoveryStore, original, key, cancellationToken);
        }
        catch (Exception exception) when (exception is OpenUsdNativeException or IOException or
            UnauthorizedAccessException or InvalidOperationException or NotSupportedException or
            InvalidDataException or ArgumentException)
        {
            ViewerDocumentActionChoice refused = await ShowDocumentActionAsync(
                "Recovery requires reconciliation",
                $"{exception.Message}\n\nNo checkpoint opinions were applied. The checkpoint is retained. " +
                "You may open the selected document without recovery or cancel.",
                "Open without recovery");
            if (refused == ViewerDocumentActionChoice.Accept)
            {
                return original;
            }
            await original.DisposeAsync();
            return null;
        }
        if (recovery is null)
        {
            return original;
        }
        try
        {
            ViewerDocumentActionChoice choice = await ShowDocumentActionAsync(
                "Recover validated review changes?",
                $"Source: {original.SourcePath}\n\nThe bounded checkpoint's native source, dependency and review " +
                "identities have been validated in a fresh session. Recovery restores review opinions as unsaved " +
                "work with fresh history; it does not restore transient simulation or save any source.",
                "Recover review", "Discard checkpoint");
            if (choice == ViewerDocumentActionChoice.Accept)
            {
                await original.DisposeAsync();
                ViewerPreparedDocument accepted = recovery;
                recovery = null;
                return accepted;
            }
            if (choice == ViewerDocumentActionChoice.Alternate)
            {
                await _reviewRecoveryStore.DeleteVerifiedAsync(
                    recovery.RecoveryDocument ??
                        throw new InvalidDataException("The validated recovery has no native document."),
                    recovery.RecoveryIdentity ??
                        throw new InvalidDataException("The validated recovery has no file identity."),
                    cancellationToken);
                return original;
            }
            await original.DisposeAsync();
            return null;
        }
        finally
        {
            if (recovery is not null)
            {
                await recovery.DisposeAsync();
            }
        }
    }
}
