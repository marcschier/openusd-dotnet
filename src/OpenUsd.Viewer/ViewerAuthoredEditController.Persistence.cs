// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;
using OpenUsd.Interop;

namespace OpenUsd.Viewer;

internal sealed record ViewerReviewSaveCapture(
    Guid DocumentId, string LayerIdentifier, UsdReviewDocument Document, ViewerFileIdentity Destination);

internal sealed record ViewerSourceLayerSaveCapture(
    Guid DocumentId,
    string LayerIdentifier,
    ulong Revision,
    string SourcePath,
    UsdReviewSourceBinding SourceBinding,
    ViewerSourceLayerPublication Publication) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Publication.DisposeAsync();
}

internal sealed record ViewerReviewSaveResult(
    UsdReviewDocument Document, ViewerFileIdentity Destination, bool Acknowledged, string Message);

internal sealed record ViewerSourceLayerSaveResult(string Message, bool ReviewBindingRefreshed);

internal sealed record ViewerSavedReview(UsdReviewDocument Document, ViewerFileIdentity Destination);

internal sealed partial class ViewerAuthoredEditController
{
    internal const int MaximumReviewDocumentBytes = 24 * 1024 * 1024;
    private UsdReviewSourceBinding? _sourceBinding;
    private ViewerSavedReview? _savedReview;
    private bool _publicationUnacknowledged;
    private bool _sourceEditOptIn;

    internal bool CanSaveReview => _sourceBinding is not null && !_disposed && !IsSuspended;

    internal bool CanAdoptReview => _sourceBinding is null && !_disposed && !IsSuspended;

    internal bool CanSaveSource => _sourceBinding is not null && !_disposed && !IsSuspended;

    internal bool SourceEditOptIn => _sourceEditOptIn;

    internal string? SavedReviewPath => _savedReview?.Destination.FullPath;

    internal UsdReviewDocument? SavedReviewDocument => _savedReview?.Document;
    internal ViewerFileIdentity? SavedReviewIdentity => _savedReview?.Destination;

    internal async Task SetSourceEditOptInAsync(
        bool enabled, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        bool changed = false;
        try
        {
            if (IsSuspended)
            {
                throw new InvalidOperationException(
                    "Finish or cancel the document transition before changing save policy.");
            }
            if (_sourceEditOptIn != enabled)
            {
                _sourceEditOptIn = enabled;
                changed = true;
            }
        }
        finally
        {
            _gate.Release();
            if (changed)
            {
                Changed?.Invoke();
            }
        }
    }

    internal async Task<ViewerSourceLayerSaveCapture> PrepareSourceLayerSaveAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Guarded source-layer publication currently requires Windows.");
        }
        UsdReviewSourceBinding sourceBinding = _sourceBinding ??
            throw new NotSupportedException("Source publication requires a verified source origin.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (IsSuspended)
            {
                throw new InvalidOperationException("Finish or cancel the document transition before saving.");
            }

            var source = await _scheduler.InvokeAsync(stage =>
            {
                ViewerDocumentObservation observation = ReadDocument(stage);
                ViewerDocumentState state = observation.State;
                ViewerDocumentSaveDecision decision = ViewerDocumentSavePolicy.Plan(
                    state, ViewerDocumentSaveAction.SaveSource);
                if (!decision.IsAllowed)
                {
                    throw new InvalidOperationException(decision.Message);
                }
                if (state.Source.FilePath is not { } path ||
                    !string.Equals(
                        Path.GetFullPath(path),
                        Path.GetFullPath(sourceBinding.SourceRootPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "The verified source origin does not own the current root layer.");
                }
                if (!string.Equals(
                    stage.RootLayerIdentifier, state.Source.Identifier, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Only the exact current root layer can be published as the source.");
                }
                return (state.Source.Identifier, state.Source.Revision, path);
            }, linked.Token).ConfigureAwait(false);

            ViewerSourceLayerPublication publication = await ViewerSourceLayerPublication.BeginAsync(
                source.path, sourceBinding.SourceFingerprint, linked.Token).ConfigureAwait(false);
            try
            {
                await _scheduler.InvokeAsync(stage =>
                {
                    ViewerDocumentState current = ReadDocument(stage).State;
                    if (current.Source.Identifier != source.Identifier ||
                        current.Source.Revision != source.Revision ||
                        !current.Source.HasChanges ||
                        current.HasOtherLayerChanges ||
                        stage.RootLayerIdentifier != source.Identifier)
                    {
                        throw new IOException("The source layer changed while its publication was being staged.");
                    }
                    using UsdLayer root = stage.GetRootLayer();
                    root.Export(publication.StagingPath);
                    return true;
                }, linked.Token).ConfigureAwait(false);
                await publication.CompleteStagingAsync(linked.Token).ConfigureAwait(false);
                return new ViewerSourceLayerSaveCapture(
                    _documentId, source.Identifier, source.Revision, source.path, sourceBinding, publication);
            }
            catch
            {
                await publication.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<ViewerSourceLayerSaveResult> PublishSourceLayerSaveAsync(
        ViewerSourceLayerSaveCapture capture, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Guarded source-layer publication currently requires Windows.");
        }
        if (capture.DocumentId != _documentId || _sourceBinding is null ||
            !_sourceBinding.HasSamePayload(capture.SourceBinding))
        {
            throw new InvalidOperationException("The source capture belongs to another or unverified document.");
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        bool published = false;
        try
        {
            if (IsSuspended)
            {
                throw new InvalidOperationException("Finish or cancel the document transition before saving.");
            }
            linked.Token.ThrowIfCancellationRequested();
            (UsdReviewSourceBinding? Binding, string? Error, bool HasReview) result =
                await _scheduler.InvokeAsync(stage =>
            {
                ViewerDocumentState current = ReadDocument(stage).State;
                if (current.Source.Identifier != capture.LayerIdentifier ||
                    current.Source.Revision != capture.Revision ||
                    !current.Source.HasChanges ||
                    current.HasOtherLayerChanges ||
                    current.Source.FilePath is not { } currentPath ||
                    !string.Equals(
                        Path.GetFullPath(currentPath),
                        Path.GetFullPath(capture.SourcePath),
                        StringComparison.OrdinalIgnoreCase) ||
                    stage.RootLayerIdentifier != capture.LayerIdentifier)
                {
                    throw new IOException("The source layer changed after capture. No source bytes were published.");
                }

                capture.Publication.Publish();
                using UsdLayer root = stage.GetRootLayer();
                try
                {
                    root.Save();
                }
                catch (Exception publicationFailure)
                {
                    try
                    {
                        capture.Publication.Rollback();
                    }
                    catch (Exception rollbackFailure)
                    {
                        throw new IOException(
                            $"Source publication failed and the prior destination could not be restored. " +
                            $"The retained rollback file is '{capture.Publication.BackupPath}'.",
                            new AggregateException(publicationFailure, rollbackFailure));
                    }
                    try
                    {
                        if (!root.Reload(force: true))
                        {
                            throw new IOException("OpenUSD could not reload the restored source layer.");
                        }
                    }
                    catch (Exception restoreFailure)
                    {
                        throw new IOException(
                            "Source bytes were restored, but OpenUSD could not reload the restored layer.",
                            new AggregateException(publicationFailure, restoreFailure));
                    }
                    throw;
                }

                try
                {
                    stage.AcknowledgeReviewSourcePublication();
                    return (stage.CaptureReviewSourceBinding(), (string?)null, current.Review is not null);
                }
                catch (Exception bindingFailure) when (
                    bindingFailure is OpenUsdNativeException or InvalidDataException or
                        NotSupportedException or IOException)
                {
                    return ((UsdReviewSourceBinding?)null, bindingFailure.Message, current.Review is not null);
                }
            }, CancellationToken.None).ConfigureAwait(false);

            published = true;
            _sourceBinding = result.Binding;
            _savedReview = null;
            _publicationUnacknowledged = result.HasReview;
            string message = result.Binding is null
                ? $"Source layer saved. Review publication is disabled until the document is reopened: {result.Error}"
                : _publicationUnacknowledged
                    ? "Source layer saved. The active review must be saved again against the updated source."
                    : "Source layer saved.";
            return new ViewerSourceLayerSaveResult(message, result.Binding is not null);
        }
        finally
        {
            _gate.Release();
            if (published)
            {
                Changed?.Invoke();
            }
        }
    }

    internal async Task<ViewerDocumentObservation> AdoptIntoReviewAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Verified review adoption currently requires Windows.");
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        bool adopted = false;
        try
        {
            if (IsSuspended)
            {
                throw new InvalidOperationException("Finish or cancel the document transition before adopting.");
            }
            if (_sourceBinding is not null)
            {
                throw new InvalidOperationException("This document is already bound to a verified review source.");
            }

            UsdReviewSourceBinding binding = await _scheduler.InvokeAsync(
                static stage => stage.AdoptIntoReview(), linked.Token).ConfigureAwait(false);
            _sourceBinding = binding;
            _initialSessionIdentity = null;
            _initialSessionRevision = 0;
            _savedReview = null;
            _publicationUnacknowledged = true;
            _history.Clear();
            adopted = true;

            return await _scheduler.InvokeAsync(ReadDocument, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            if (adopted)
            {
                Changed?.Invoke();
            }
        }
    }

    internal async Task<UsdReviewDocument> CaptureRecoveryDocumentAsync(
        string recoveryPath, bool retiring, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        UsdReviewSourceBinding source = _sourceBinding ??
            throw new NotSupportedException("This session has no verified source origin for portable recovery.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (IsSuspended && !retiring)
            {
                throw new InvalidOperationException("Recovery capture was superseded by a document transition.");
            }
            return await _scheduler.InvokeAsync(stage =>
            {
                ViewerDocumentObservation current = ReadDocument(stage);
                string identifier = current.State.Review?.Identifier ??
                    throw new InvalidOperationException("The document has no review opinions to checkpoint.");
                using UsdLayer review = stage.GetLocalLayer(identifier);
                return review.CaptureReviewDocument(source, recoveryPath);
            }, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal void InitializeImportedReview(ViewerPreparedDocument prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (_history.UndoDepth != 0 || _history.RedoDepth != 0 || _sourceBinding is null ||
            prepared.SourceBinding is null || !_sourceBinding.HasSamePayload(prepared.SourceBinding) ||
            !ReferenceEquals(_scheduler, prepared.Scheduler) || prepared.ImportedReview is null)
        {
            throw new InvalidOperationException(
                "Only the matching pristine imported document can initialize review state.");
        }
        if (prepared.ReviewFileIdentity is { } file)
        {
            _savedReview = new ViewerSavedReview(prepared.PublishedReview ??
                throw new InvalidOperationException("The imported named review has no published baseline."), file);
        }
        _publicationUnacknowledged = prepared.IsRecovery;
    }

    internal async Task<ViewerReviewSaveCapture> PrepareReviewSaveAsync(
        string destinationPath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Verified review publication currently requires Windows.");
        }
        UsdReviewSourceBinding source = _sourceBinding ??
            throw new NotSupportedException(
                "This is a session-only document. Verified source opening is required before portable review edits.");
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (!Path.IsPathFullyQualified(destinationPath))
        {
            throw new ArgumentException("Choose an absolute local review destination.", nameof(destinationPath));
        }
        string destination = Path.GetFullPath(destinationPath);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (IsSuspended)
            {
                throw new InvalidOperationException("Finish or cancel the document transition before saving.");
            }
            (string layer, UsdReviewDocument document) = await _scheduler.InvokeAsync(stage =>
            {
                if (_initialSessionIdentity is null)
                {
                    _ = ReadDocument(stage);
                }
                using UsdLayer review = GetReviewForCapture(stage);
                return (review.Identifier, review.CaptureReviewDocument(source, destination));
            }, linked.Token).ConfigureAwait(false);
            if (document.ByteLength is <= 0 or > MaximumReviewDocumentBytes)
            {
                throw new InvalidDataException("The portable review exceeds the Viewer's 24 MiB file bound.");
            }
            ViewerFileIdentity observed = await ViewerReviewDocumentPublication.ObserveAsync(
                document, destination, linked.Token).ConfigureAwait(false);
            if (_savedReview is { } saved &&
                string.Equals(destination, saved.Destination.FullPath, StringComparison.OrdinalIgnoreCase) &&
                !saved.Destination.Matches(observed))
            {
                throw new IOException("The saved review file changed externally. Reopen it or choose a new path.");
            }
            return new ViewerReviewSaveCapture(_documentId, layer, document, observed);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<ViewerReviewSaveResult> PublishReviewSaveAsync(
        ViewerReviewSaveCapture capture, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Verified review publication currently requires Windows.");
        }
        if (capture.DocumentId != _documentId || _sourceBinding is null)
        {
            throw new InvalidOperationException("The review capture belongs to another or unverified document.");
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        bool published = false;
        try
        {
            if (IsSuspended)
            {
                throw new InvalidOperationException("Finish or cancel the document transition before saving.");
            }
            await using ViewerReviewDocumentPublication publication =
                await ViewerReviewDocumentPublication.StageAsync(capture, linked.Token).ConfigureAwait(false);
            ViewerFileIdentity file = await publication.PublishAsync(linked.Token).ConfigureAwait(false);
            published = true;
            _publicationUnacknowledged = true;
            _savedReview = new ViewerSavedReview(capture.Document, file);
            var result = new ViewerReviewSaveResult(
                capture.Document, file, false, "The review file was published; current state is not acknowledged.");
            try
            {
                // Publication has committed; cancellation can no longer retract the file.
                bool acknowledged = await _scheduler.InvokeAsync(stage =>
                {
                    using UsdLayer review = stage.GetLocalLayer(capture.LayerIdentifier);
                    return review.AcknowledgeSaved(capture.Document);
                }, CancellationToken.None).ConfigureAwait(false);
                _publicationUnacknowledged = !acknowledged;
                result = result with
                {
                    Acknowledged = acknowledged,
                    Message = acknowledged ? "Review saved." :
                        "The captured review was published, but later edits remain unsaved. Save again."
                };
            }
            catch (OpenUsdNativeException exception)
            {
                result = result with
                {
                    Message = $"The review was published but could not be acknowledged: {exception.Message}"
                };
            }
            return result;
        }
        finally
        {
            _gate.Release();
            if (published)
            {
                Changed?.Invoke();
            }
        }
    }
}
