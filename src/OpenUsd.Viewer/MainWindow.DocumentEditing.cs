// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OpenUsd.Interop;

namespace OpenUsd.Viewer;

public sealed partial class MainWindow
{
    private readonly IViewerDocumentFilePicker _documentFilePicker;
    private readonly IViewerAssetFilePicker _assetFilePicker;
    private PropertyEditWindow? _propertyEditor;
    private DocumentChangesWindow? _documentChanges;
    private DocumentActionWindow? _documentAction;
    private string? _reviewSessionOnlyReason;
    private ViewerDocumentObservation? _documentObservation;
    private ViewerDocumentObservation? _documentRetirementTicket;
    private UsdStageRetirementLease? _documentRetirementLease;
    private ViewerPhysicsController? _retirementPhysics;
    private bool _documentEditBusy;
    private bool _closeDecisionPending;
    private bool _documentRefreshRequested;
    private bool _documentSnapshotRefreshBusy;
    private Task _documentRefreshTask = Task.CompletedTask;

    private void WireDocumentCommands()
    {
        EditPropertyMenuItem.Click += (_, _) => ShowPropertyEditor();
        EditUndoMenuItem.Click += async (_, _) => await ReplayDocumentHistoryAsync(undo: true);
        EditRedoMenuItem.Click += async (_, _) => await ReplayDocumentHistoryAsync(undo: false);
        ExportReviewDeltaMenuItem.Click += async (_, _) => await ExportReviewDeltaAsync();
        SaveReviewMenuItem.Click += async (_, _) => await SaveReviewAsync(saveAs: false);
        SaveReviewAsMenuItem.Click += async (_, _) => await SaveReviewAsync(saveAs: true);
        OpenReviewMenuItem.Click += async (_, _) => await ChooseReviewDocumentAsync();
        RevertReviewMenuItem.Click += async (_, _) => await RevertReviewAsync();
        ToolTip.SetTip(ExportReviewDeltaMenuItem,
            "Exports a bounded review-only USDA delta to an unused path. This is not a portable review document.");
        ToolTip.SetTip(SaveReviewMenuItem, DocumentChangesWindow.SaveRestriction);
        ToolTip.SetTip(SaveReviewAsMenuItem, DocumentChangesWindow.SaveRestriction);
        ToolTip.SetTip(SaveSourceMenuItem,
            "Source layers are capture-only in the bounded native editing API. Source saving is disabled.");
        ToolTip.SetTip(RevertReviewMenuItem,
            "No portable saved review baseline is available. Undo individual review edits instead.");
        UpdateDocumentCommands();
    }

    private async Task InitializeDocumentEditingAsync(CancellationToken cancellationToken)
    {
        ViewerAuthoredEditController editor = _documentEditor ??
            throw new InvalidOperationException("The document editor is unavailable.");
        try
        {
            _documentObservation = await editor.ReadDocumentAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is OpenUsdNativeException or InvalidDataException or
            NotSupportedException)
        {
            _documentObservation = null;
            ViewerStartupOptions.WriteStatus($"Document editing is read-only: {exception.Message}");
            ToolTip.SetTip(DocumentEditStatus, $"Editing is unavailable: {exception.Message}");
        }
        editor.Changed += OnDocumentHistoryChanged;
        UpdateDocumentCommands();
        await RefreshSavedViewsAsync();
    }

    private void UpdateDocumentCommands()
    {
        ViewerAuthoredEditController? editor = _documentEditor;
        bool ready = editor is not null && _documentObservation is not null && !_documentBusy &&
            !_documentEditBusy && !_documentSnapshotRefreshBusy &&
            !_physicsAuthoringBusy && !_physicsPropertyReadBusy &&
            !editor.IsSuspended && !_shutdownStarted;
        OpenReviewMenuItem.IsEnabled = OperatingSystem.IsWindows() &&
            !_documentBusy && !_documentEditBusy && !_shutdownStarted;
        EditUndoMenuItem.IsEnabled = ready && editor is { CanUndo: true };
        EditRedoMenuItem.IsEnabled = ready && editor is { CanRedo: true };
        if (editor is not null)
        {
            PhysicsUndoButton.IsEnabled = ready && _physics is { IsEnabled: true } && editor.CanUndo;
            PhysicsRedoButton.IsEnabled = ready && _physics is { IsEnabled: true } && editor.CanRedo;
        }
        ExportReviewDeltaMenuItem.IsEnabled = ready && _documentObservation?.State.Review is not null;
        SaveReviewMenuItem.IsEnabled = ready && editor is { CanSaveReview: true };
        SaveReviewAsMenuItem.IsEnabled = SaveReviewMenuItem.IsEnabled;
        RevertReviewMenuItem.IsEnabled = ready && editor is { CanSaveReview: true, SavedReviewPath: not null } &&
            _documentObservation?.State is { Source.HasChanges: false, HasOtherLayerChanges: false };
        ToolTip.SetTip(RevertReviewMenuItem,
            "Reopen the unchanged saved review in a fresh verified session. " +
            "Other source/session edits must be resolved.");
        ToolTip.SetTip(SaveReviewMenuItem, editor is { CanSaveReview: true }
            ? "Save the verified source-linked review document without writing source or dependency files."
            : _reviewSessionOnlyReason ?? DocumentChangesWindow.SaveRestriction);
        ToolTip.SetTip(SaveReviewAsMenuItem, ToolTip.GetTip(SaveReviewMenuItem));
        EditUndoMenuItem.Header = editor is { CanUndo: true }
            ? $"_Undo {editor.UndoDescription.Replace("_", "__", StringComparison.Ordinal)}" : "_Undo";
        EditRedoMenuItem.Header = editor is { CanRedo: true }
            ? $"_Redo {editor.RedoDescription.Replace("_", "__", StringComparison.Ordinal)}" : "_Redo";
        EditPropertyMenuItem.IsEnabled = ready && !_inspectorPropertiesLoading && _currentInspector is
        {
            IsPrototype: false, IsInPrototype: false
        } inspector && PropertyEditorAttributes(inspector).Any(
            static attribute => ViewerPropertyEditParser.Supports(attribute.TypeName));
        UpdateInspectorPropertyControls();
        UpdateHierarchyNavigation();
        if (editor is null)
        {
            DocumentEditStatus.Text = "Review: no document";
        }
        else if (_documentObservation is { } document)
        {
            DocumentEditStatus.Text = (document.HasChanges ? "Review: unsaved changes" : "Review: clean") +
                (editor.CanSaveReview ? string.Empty : " (session-only)");
            ToolTip.SetTip(DocumentEditStatus,
                $"{DocumentEditStatus.Text}\nReview: {document.State.Review?.Identifier ?? "<not created>"}\n" +
                $"Source: {document.State.Source.Identifier}\nSource dirty: {document.State.Source.HasChanges}\n" +
                $"Other changed layers: {document.OtherChangedLayers.Count}\n" +
                $"Saved document: {editor.SavedReviewPath ?? "<not saved>"}\n" +
                (_reviewSessionOnlyReason ?? "Verified source origin; source saving remains disabled."));
        }
        else
        {
            DocumentEditStatus.Text = "Review: state unavailable";
        }
        UpdateSavedViewsContext();
    }

    private void ShowPropertyEditor(string? selectedName = null)
    {
        if (_propertyEditor is { } existing)
        {
            existing.Activate();
            return;
        }
        if (_documentEditor is not { } editor || _currentInspector is not { } inspector ||
            !EditPropertyMenuItem.IsEnabled)
        {
            ShowError(
                "Select a prim with supported local typed properties before editing its review layer.");
            return;
        }
        try
        {
            WorkspaceFocus focus = CaptureWorkspaceFocus();
            var window = new PropertyEditWindow(
                editor, inspector.Path, PropertyEditorAttributes(inspector), _currentTimeCode,
                selectedName, _assetFilePicker);
            _propertyEditor = window;
            window.Closed += (_, _) =>
            {
                _propertyEditor = null;
                RestoreWorkspaceFocus(focus);
            };
            window.Show(this);
        }
        catch (Exception exception) when (exception is NotSupportedException or ArgumentException)
        {
            ShowError(exception.Message);
        }
    }

    private async Task ReplayDocumentHistoryAsync(bool undo)
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
            ViewerAuthoredEditController editor = _documentEditor ??
                throw new InvalidOperationException("Open a document before undoing review edits.");
            _documentEditBusy = true;
            UpdateDocumentCommands();
            RenderPhysicsPropertyEditor();
            ViewerAuthoredEditResult result = undo
                ? await editor.UndoAsync(_documentLifetime?.Token ?? default)
                : await editor.RedoAsync(_documentLifetime?.Token ?? default);
            ViewerStatus.Text = result.Message;
            await ReloadPhysicsPropertiesAsync(forceRefresh: true);
            QueueDocumentEditingRefresh();
        }
        catch (OperationCanceledException) when (_viewerLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is OpenUsdNativeException or InvalidOperationException or
            InvalidDataException or NotSupportedException or ArgumentException)
        {
            ShowError($"Review history was not replayed: {exception.Message}");
        }
        finally
        {
            if (entered)
            {
                _documentEditBusy = false;
                RenderPhysicsPropertyEditor();
                UpdateDocumentCommands();
                _documentGate.Release();
            }
        }
    }

    private async Task ExportReviewDeltaAsync()
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
            ViewerAuthoredEditController editor = _documentEditor ??
                throw new InvalidOperationException("Open a document before exporting review opinions.");
            string source = _stagePath ?? throw new InvalidOperationException("The source identity is unavailable.");
            _documentEditBusy = true;
            UpdateDocumentCommands();
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export review delta to a new file (not Save Review)",
                SuggestedFileName = "review-delta.usda",
                DefaultExtension = "usda",
                FileTypeChoices = [new FilePickerFileType("Review delta") { Patterns = ["*.usda"] }]
            });
            if (file is null)
            {
                ViewerStatus.Text = "Review delta export cancelled. The document is unchanged.";
                return;
            }
            string destination = file.TryGetLocalPath() ??
                throw new NotSupportedException("Choose an unused local .usda path for the review delta.");
            OpenUsd.Editing.UsdLayerCheckpoint checkpoint =
                await editor.CaptureReviewCheckpointAsync(_viewerLifetime.Token);
            await ViewerReviewLayerExporter.ExportNewAsync(checkpoint, source, destination, _viewerLifetime.Token);
            ViewerStatus.Text = $"Exported review delta to {destination}. The document remains unsaved; " +
                "this file does not include source composition, bookmarks, or transient simulation.";
        }
        catch (OperationCanceledException) when (_viewerLifetime.IsCancellationRequested)
        {
            ViewerStatus.Text = "Review delta export cancelled.";
        }
        catch (Exception exception) when (exception is OpenUsdNativeException or IOException or
            UnauthorizedAccessException or InvalidDataException or InvalidOperationException or
            NotSupportedException or ArgumentException)
        {
            ShowError($"Review delta was not exported: {exception.Message} Choose an unused local .usda destination.");
        }
        finally
        {
            if (entered)
            {
                _documentEditBusy = false;
                UpdateDocumentCommands();
                _documentGate.Release();
            }
        }
    }

    private void OnDocumentHistoryChanged() => Dispatcher.UIThread.Post(() =>
    {
        QueueDocumentEditingRefresh();
        QueueSavedViewsRefresh();
    });

    private void QueueDocumentEditingRefresh()
    {
        UpdateDocumentCommands();
        if (_documentEditor is { IsSuspended: true })
        {
            return;
        }
        _documentRefreshRequested = true;
        if (_documentRefreshTask.IsCompleted && !_shutdownStarted)
        {
            _documentRefreshTask = RefreshDocumentEditingAsync();
        }
    }

    private async Task RefreshDocumentEditingAsync()
    {
        ViewerAuthoredEditController? editor = _documentEditor;
        ViewerRenderCoordinator? coordinator = _coordinator;
        CancellationToken cancellation = _documentLifetime?.Token ?? _viewerLifetime.Token;
        _documentSnapshotRefreshBusy = true;
        UpdateDocumentCommands();
        RenderPhysicsPropertyEditor();
        try
        {
            while (_documentRefreshRequested && editor is not null && coordinator is not null)
            {
                _documentRefreshRequested = false;
                if (_physics is { IsEnabled: true } && !_physicsAuthoringBusy && !_documentEditBusy &&
                    !_documentBusy && !editor.IsSuspended)
                {
                    await ReloadPhysicsPropertiesAsync(forceRefresh: true);
                }
                ViewerDocumentObservation state = await editor.ReadDocumentAsync(cancellation);
                if (!ReferenceEquals(editor, _documentEditor))
                {
                    return;
                }
                _documentObservation = state;
                ScheduleReviewRecovery(editor, state);
                UpdateDocumentCommands();
                if (_selectionState.PrimPath is { } path && !_documentBusy)
                {
                    double? inspectionTimeCode = _inspectorTimeCode;
                    ViewerPrimInspectorSnapshot inspector = await coordinator.Scheduler.InvokeAsync(
                        stage => ViewerStageSnapshotBuilder.BuildInspector(stage, path, inspectionTimeCode),
                        cancellation);
                    if (ReferenceEquals(editor, _documentEditor) && _selectionState.PrimPath == path &&
                        inspectionTimeCode == _inspectorTimeCode)
                    {
                        ShowInspector(inspector);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is OpenUsdNativeException or InvalidDataException or
            InvalidOperationException or NotSupportedException or ArgumentException)
        {
            if (ReferenceEquals(editor, _documentEditor) && !_shutdownStarted)
            {
                _documentObservation = null;
                ShowError($"Could not refresh review state: {exception.Message}");
            }
        }
        finally
        {
            _documentSnapshotRefreshBusy = false;
            UpdateDocumentCommands();
            RenderPhysicsPropertyEditor();
        }
    }

    private async Task<bool> ConfirmDocumentTransitionAsync(
        ViewerDocumentTransition transition, CancellationToken cancellationToken)
    {
        if (_shutdownStarted)
        {
            throw new InvalidOperationException("The document is already retiring.");
        }
        if (_documentEditor is not { } editor)
        {
            return true;
        }
        ViewerDocumentObservation original = await editor.ReadDocumentAsync(cancellationToken);
        ViewerDocumentObservation observed = original;
        ViewerDocumentObservation? retirementTicket = null;
        _documentEditBusy = true;
        UpdateDocumentCommands();
        ViewerDocumentTransitionResult result = await ViewerDocumentLifecycle.RunAsync(
            transition, () => observed.State,
            async (_, action, token) =>
            {
                var prompt = new DocumentChangesWindow(
                    original, action, editor.CanSaveReview && action != ViewerDocumentTransition.RevertReview,
                    action == ViewerDocumentTransition.RevertReview
                        ? "Revert restores the saved review in a new verified session " +
                            "and resets transient simulation. " +
                            "Cancel keeps the current document and history."
                        : _reviewSessionOnlyReason);
                _documentChanges = prompt;
                try
                {
                    await prompt.ShowDialog(this);
                    if (prompt.Choice != ViewerDocumentUnsavedChoice.Cancel)
                    {
                        observed = await editor.ReadDocumentAsync(token);
                        if (!original.Matches(observed))
                        {
                            throw new InvalidOperationException(
                                "The document changed while awaiting a decision. Review the new state and retry.");
                        }
                    }
                    return prompt.Choice;
                }
                finally
                {
                    _documentChanges = null;
                }
            },
            async (_, token) =>
            {
                Task<bool> saving = await Dispatcher.UIThread.InvokeAsync<Task<bool>>(
                    () => SaveReviewWithinDocumentGateAsync(saveAs: false, token));
                bool saved = await saving.ConfigureAwait(false);
                observed = await editor.ReadDocumentAsync(token).ConfigureAwait(false);
                return saved;
            },
            async (expected, _, token) =>
            {
                if (transition == ViewerDocumentTransition.RevertReview &&
                    (expected.Source.HasChanges || expected.HasOtherLayerChanges))
                {
                    throw new InvalidOperationException(
                        "Other source or session opinions changed during Revert preparation. " +
                        "The current document was kept; resolve those opinions before reverting.");
                }
                if (expected != observed.State || !await editor.TrySuspendAsync(observed, token))
                {
                    throw new InvalidOperationException(
                        "The document changed while awaiting a decision. " +
                        "It was kept; review the new state and retry.");
                }
                retirementTicket = observed;
            }, cancellationToken);
        if (result.Status == ViewerDocumentTransitionStatus.Completed)
        {
            _documentRetirementTicket = retirementTicket ??
                throw new InvalidOperationException("The completed transition has no retained document ticket.");
            return true;
        }
        ViewerStatus.Text = result.Message;
        return false;
    }

    private async Task EndDocumentTransitionAsync()
    {
        if (_shutdownStarted)
        {
            return;
        }
        if (_documentRetirementLease is { } retirement)
        {
            await retirement.DisposeAsync();
            _documentRetirementLease = null;
        }
        _documentRetirementTicket = null;
        _retiringRecoveryDocument = null;
        bool resumedEditor = _documentEditor is { IsSuspended: true };
        _documentEditor?.Resume();
        _retirementPhysics?.ResumeDocumentWrites();
        _retirementPhysics = null;
        if (_hostCallbacks is { IsQuiesced: true } callbacks &&
            _documentLifetime is { IsCancellationRequested: false } lifetime && !_shutdownStarted)
        {
            callbacks.Dispose();
            _hostCallbacks = new ViewerHostCallbackScope(lifetime.Token);
            ViewerStartupOptions.WriteStatus(
                "Document kept after host callbacks were quiesced. " +
                "Stage-ready services are not restarted implicitly.");
        }
        _documentEditBusy = false;
        UpdateDocumentCommands();
        ViewerAuthoredEditController? keptEditor = _documentEditor;
        // Resume posts Changed for suspended editors; cancelled saves need their own refresh.
        if (!resumedEditor && keptEditor is not null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(keptEditor, _documentEditor))
                {
                    QueueDocumentEditingRefresh();
                }
            });
        }
    }

    private async Task PrepareDocumentRetirementAsync(CancellationToken cancellationToken)
    {
        if (_documentEditor is not { } editor)
        {
            return;
        }
        ViewerDocumentObservation ticket = _documentRetirementTicket ??
            throw new InvalidOperationException("A document decision is required before retirement.");
        if (_physics is { } physics)
        {
            await physics.SuspendDocumentWritesAsync(cancellationToken);
            _retirementPhysics = physics;
        }
        if (_hostCallbacks is { } callbacks)
        {
            await callbacks.QuiesceAsync(cancellationToken);
        }
        if (_hostStageReadyTask is { } hostTask)
        {
            await hostTask.WaitAsync(cancellationToken);
        }
        _documentRefreshRequested = false;
        await _documentRefreshTask;
        await PrepareRecoveryRetirementAsync(editor, cancellationToken);
        UsdStageRetirementLease? retirement = await editor.TryPrepareRetirementAsync(ticket, cancellationToken);
        if (retirement is null)
        {
            throw new InvalidOperationException(
                "A writer changed the document during retirement preparation. Review the new state and retry.");
        }
        _documentRetirementLease = retirement;
    }
}
