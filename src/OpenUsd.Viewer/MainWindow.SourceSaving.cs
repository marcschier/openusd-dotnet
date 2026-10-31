// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Viewer;

public sealed partial class MainWindow
{
    private async Task SaveSourceLayerAsync()
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

            ViewerAuthoredEditController editor = _documentEditor ??
                throw new InvalidOperationException("Open a document before publishing its source layer.");
            if (!editor.CanSaveSource)
            {
                throw new NotSupportedException("Source publication requires a verified source origin.");
            }
            ViewerDocumentObservation observation = await editor.ReadDocumentAsync(_viewerLifetime.Token);
            ViewerDocumentSaveDecision decision = ViewerDocumentSavePolicy.Plan(
                observation.State with { SourceEditOptIn = true }, ViewerDocumentSaveAction.SaveSource);
            if (!decision.IsAllowed)
            {
                throw new InvalidOperationException(decision.Message);
            }
            string sourcePath = observation.State.Source.FilePath ??
                throw new InvalidOperationException("The root layer has no source file.");
            string details = $"{sourcePath}\n\n" +
                "Only the verified root layer will be published. Other changed layers are refused. " +
                "The source file is replaced atomically. If OpenUSD cannot reload it, " +
                "the prior file is restored. " +
                "The active review remains in memory but its saved baseline is invalidated and must be saved again.";
            ViewerDocumentActionChoice choice = await ShowDocumentActionAsync(
                editor.SourceEditOptIn
                    ? "Publish changes to the source layer?"
                    : "Enable source saving for this document?",
                editor.SourceEditOptIn
                    ? details
                    : $"{details}\n\nAccepting also opts this document into source-layer saving until it is closed.",
                editor.SourceEditOptIn ? "Publish source layer" : "Enable and publish",
                "Cancel");
            if (choice != ViewerDocumentActionChoice.Accept)
            {
                ViewerStatus.Text = "Source publication cancelled; the document and source file are unchanged.";
                return;
            }
            if (!editor.SourceEditOptIn)
            {
                await editor.SetSourceEditOptInAsync(true, _viewerLifetime.Token);
            }

            await using ViewerSourceLayerSaveCapture capture =
                await editor.PrepareSourceLayerSaveAsync(_viewerLifetime.Token);
            ViewerSourceLayerSaveResult result =
                await editor.PublishSourceLayerSaveAsync(capture, _viewerLifetime.Token);
            _documentObservation = await editor.ReadDocumentAsync(_viewerLifetime.Token);
            ViewerStatus.Text = result.Message;
            UpdateDocumentCommands();
        }
        catch (OperationCanceledException) when (_viewerLifetime.IsCancellationRequested)
        {
            ViewerStatus.Text = "Source publication cancelled.";
        }
        catch (Exception exception) when (exception is OpenUsdNativeException or IOException or
            UnauthorizedAccessException or InvalidOperationException or NotSupportedException or
            InvalidDataException or ArgumentException)
        {
            ShowError($"Source layer was not published: {exception.Message}");
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
}
