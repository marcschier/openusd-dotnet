// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;
using OpenUsd.Interop;

namespace OpenUsd.Viewer;

public sealed partial class MainWindow
{
    private async Task RevertReviewAsync()
    {
        bool entered = false;
        try
        {
            await CloseSavedViewsAsync();
            await CloseRenderSequenceAsync();
            await _documentGate.WaitAsync(_viewerLifetime.Token);
            entered = true;
            ViewerAuthoredEditController editor = _documentEditor ??
                throw new InvalidOperationException("Open a saved review before reverting.");
            ViewerFileIdentity expected = editor.SavedReviewIdentity ??
                throw new InvalidOperationException("Save a review document before reverting to it.");
            ViewerDocumentObservation current = await editor.ReadDocumentAsync(_viewerLifetime.Token);
            if (current.State.Source.HasChanges || current.State.HasOtherLayerChanges)
            {
                throw new InvalidOperationException(
                    "Other source or session opinions have changed. " +
                    "Resolve them before replacing this review session.");
            }
            await using ViewerReviewDocumentFile file =
                await ViewerReviewDocumentFile.InspectAsync(expected.FullPath, _viewerLifetime.Token);
            if (!expected.Matches(file.Identity))
            {
                throw new IOException(
                    "The saved review file changed externally. Open it explicitly instead of reverting.");
            }
            string source = _stagePath ?? throw new InvalidOperationException("The source path is unavailable.");
            UsdReviewDocument document = await file.ReadAsync(source, _viewerLifetime.Token);
            await using ViewerPreparedDocument prepared = await ViewerPreparedDocument.OpenReviewAsync(
                document, source, file.Identity, recovery: false, _viewerLifetime.Token);
            await OpenPreparedDocumentAsync(prepared, file.Identity.FullPath, addToRecent: false,
                ViewerDocumentTransition.RevertReview, reload: false, _viewerLifetime.Token);
        }
        catch (OperationCanceledException) when (_viewerLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is OpenUsdNativeException or IOException or
            UnauthorizedAccessException or InvalidOperationException or NotSupportedException or
            InvalidDataException or ArgumentException)
        {
            ShowError($"The review was kept: {exception.Message}");
        }
        finally
        {
            if (entered)
            {
                _documentGate.Release();
            }
        }
    }

    private Task OpenDocumentCoreAsync(string path, bool addToRecent, CancellationToken cancellationToken) =>
        IsReviewDocumentPath(path)
            ? OpenReviewCoreAsync(path, addToRecent, cancellationToken)
            : OpenStageCoreAsync(path, addToRecent, cancellationToken);

    private async Task ChooseReviewDocumentAsync()
    {
        try
        {
            string? path = await _documentFilePicker.OpenReviewAsync(this);
            if (path is not null)
            {
                await OpenStageAndReportAsync(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or NotSupportedException or ArgumentException)
        {
            ShowError($"The review picker failed: {exception.Message}");
        }
    }

    private async Task OpenReviewCoreAsync(string path, bool addToRecent, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Verified portable review opening currently requires Windows.");
        }
        await CloseSavedViewsAsync();
        await CloseRenderSequenceAsync();
        await _documentGate.WaitAsync(cancellationToken);
        try
        {
            if (_shutdownStarted)
            {
                throw new InvalidOperationException("The viewer is already retiring.");
            }
            if (!await ConfirmDocumentTransitionAsync(ViewerDocumentTransition.Replace, cancellationToken))
            {
                return;
            }
            await using ViewerReviewDocumentFile file =
                await ViewerReviewDocumentFile.InspectAsync(path, cancellationToken);
            string source = ViewerWindowsFiles.NormalizePath(file.Info.SourceRootPath);
            ViewerDocumentActionChoice choice = await ShowDocumentActionAsync(
                "Open this review with its recorded source?",
                $"Review: {file.Identity.FullPath}\n\nRecorded source: {source}\n\n" +
                "These are document claims, not verified identities. Continuing explicitly permits " +
                "verification of this source and its bounded dependencies before a new review session is opened.",
                "Verify source and open review");
            if (choice != ViewerDocumentActionChoice.Accept)
            {
                SetReady("Review opening cancelled; the current document is unchanged.");
                return;
            }
            UsdReviewDocument document = await file.ReadAsync(source, cancellationToken);
            ViewerPreparedDocument original = await ViewerPreparedDocument.OpenReviewAsync(
                document, source, file.Identity, recovery: false, cancellationToken);
            await using ViewerPreparedDocument? prepared =
                await SelectRecoveryAsync(original, file.Identity.FullPath, cancellationToken);
            if (prepared is null)
            {
                SetReady("Review opening cancelled; the current document is unchanged.");
                return;
            }
            await OpenPreparedDocumentAsync(prepared, file.Identity.FullPath, addToRecent,
                ViewerDocumentTransition.Replace, reload: false, cancellationToken, decisionAccepted: true);
        }
        finally
        {
            await EndDocumentTransitionAsync();
            _documentGate.Release();
        }
    }

    private async Task<ViewerPreparedDocument?> PrepareSourceDocumentAsync(
        string path, CancellationToken cancellationToken)
    {
        if (_shutdownStarted)
        {
            throw new InvalidOperationException("The viewer is already retiring.");
        }
        ViewerPreparedDocument original;
        try
        {
            original = await ViewerPreparedDocument.OpenSourceAsync(path, null, cancellationToken);
        }
        catch (Exception exception) when (exception is OpenUsdNativeException or NotSupportedException)
        {
            ViewerDocumentActionChoice choice = await ShowDocumentActionAsync(
                "Verified review opening was refused",
                $"{exception.Message}\n\nThe current document has not been discarded. " +
                "You may open the selected source for session-only viewing/editing, without portable saving.",
                "Open session-only");
            if (choice != ViewerDocumentActionChoice.Accept)
            {
                return null;
            }
            cancellationToken.ThrowIfCancellationRequested();
            original = await ViewerPreparedDocument.OpenSourceAsync(
                path, $"Explicit session-only opening: {exception.Message}", cancellationToken);
        }
        return await SelectRecoveryAsync(original, path, cancellationToken);
    }

    private async Task<ViewerDocumentActionChoice> ShowDocumentActionAsync(
        string heading, string details, string accept, string? alternate = null)
    {
        var window = new DocumentActionWindow(heading, details, accept, alternate);
        _documentAction = window;
        try
        {
            await window.ShowDialog(this);
            return window.Choice;
        }
        finally
        {
            _documentAction = null;
        }
    }

    private async Task SaveReviewAsync(bool saveAs)
    {
        bool entered = false;
        try
        {
            await _documentGate.WaitAsync(_viewerLifetime.Token);
            entered = true;
            if (_shutdownStarted)
            {
                throw new InvalidOperationException("The document is already retiring.");
            }
            _documentEditBusy = true;
            UpdateDocumentCommands();
            _ = await SaveReviewWithinDocumentGateAsync(saveAs, _viewerLifetime.Token);
        }
        catch (OperationCanceledException) when (_viewerLifetime.IsCancellationRequested)
        {
            ViewerStatus.Text = "Review save cancelled.";
        }
        catch (Exception exception) when (exception is OpenUsdNativeException or IOException or
            UnauthorizedAccessException or InvalidOperationException or NotSupportedException or
            InvalidDataException or ArgumentException)
        {
            ShowError($"Review was not saved: {exception.Message}");
        }
        finally
        {
            if (entered)
            {
                _documentEditBusy = false;
                UpdateDocumentCommands();
                _documentGate.Release();
                QueueDocumentEditingRefresh();
            }
        }
    }

    private async Task<bool> SaveReviewWithinDocumentGateAsync(bool saveAs, CancellationToken cancellationToken)
    {
        ViewerAuthoredEditController editor = _documentEditor ??
            throw new InvalidOperationException("Open a document before saving a review.");
        if (!editor.CanSaveReview)
        {
            throw new NotSupportedException(_reviewSessionOnlyReason ?? DocumentChangesWindow.SaveRestriction);
        }
        await StopReviewRecoveryAsync();
        string? destination = editor.SavedReviewPath;
        if (saveAs || destination is null)
        {
            string suggestion = destination ?? Path.ChangeExtension(_stagePath ??
                throw new InvalidOperationException("The source path is unavailable."), ".urd");
            destination = await _documentFilePicker.SaveReviewAsync(this, suggestion);
            cancellationToken.ThrowIfCancellationRequested();
            if (destination is null)
            {
                ViewerStatus.Text = "Review save cancelled; the current document is unchanged.";
                return false;
            }
        }
        ViewerReviewSaveCapture capture = await editor.PrepareReviewSaveAsync(destination, cancellationToken);
        bool currentName = string.Equals(capture.Destination.FullPath, editor.SavedReviewPath,
            StringComparison.OrdinalIgnoreCase);
        if (capture.Destination.Exists && !currentName)
        {
            ViewerDocumentActionChoice choice = await ShowDocumentActionAsync(
                "Replace the observed review file?",
                $"{capture.Destination.FullPath}\n\nThe selected file exists. " +
                "Only the observed version will be accepted at the final pre-publication check. " +
                "This is an optimistic check, not filesystem compare-and-swap.",
                "Replace observed file");
            if (choice != ViewerDocumentActionChoice.Accept)
            {
                ViewerStatus.Text = "Review save cancelled; the existing file is unchanged.";
                return false;
            }
        }
        ViewerReviewSaveResult result = await editor.PublishReviewSaveAsync(capture, cancellationToken);
        await ClearCurrentRecoveryAsync(editor, cancellationToken);
        _reviewRecoveryKey = RecoveryKey(result.Destination.FullPath);
        _reviewRecoveryIdentity = null;
        _reviewRecoveryDocument = null;
        _reviewRecoveryRevision = ulong.MaxValue;
        _reviewRecoveryWrittenRevision = ulong.MaxValue;
        _documentObservation = await editor.ReadDocumentAsync(cancellationToken);
        StageStatus.Text = Path.GetFileName(result.Destination.FullPath);
        ViewerStatus.Text = result.Message;
        try
        {
            RefreshRecentMenu(await _recentStageStore.AddAsync(result.Destination.FullPath, cancellationToken));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ViewerStatus.Text = $"{result.Message} Recent-document update failed: {exception.Message}";
            ViewerStartupOptions.WriteStatus(ViewerStatus.Text);
        }
        UpdateDocumentCommands();
        return result.Acknowledged;
    }
}
