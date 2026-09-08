// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;
using OpenUsd.Interop;

namespace OpenUsd.Viewer;

internal sealed record ViewerReviewSaveCapture(
    Guid DocumentId, string LayerIdentifier, UsdReviewDocument Document, ViewerFileIdentity Destination);

internal sealed record ViewerReviewSaveResult(
    UsdReviewDocument Document, ViewerFileIdentity Destination, bool Acknowledged, string Message);

internal sealed record ViewerSavedReview(UsdReviewDocument Document, ViewerFileIdentity Destination);

internal sealed partial class ViewerAuthoredEditController
{
    internal const int MaximumReviewDocumentBytes = 24 * 1024 * 1024;
    private readonly UsdReviewSourceBinding? _sourceBinding;
    private ViewerSavedReview? _savedReview;
    private bool _publicationUnacknowledged;

    internal bool CanSaveReview => _sourceBinding is not null && !_disposed && !IsSuspended;

    internal string? SavedReviewPath => _savedReview?.Destination.FullPath;

    internal UsdReviewDocument? SavedReviewDocument => _savedReview?.Document;
    internal ViewerFileIdentity? SavedReviewIdentity => _savedReview?.Destination;

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
