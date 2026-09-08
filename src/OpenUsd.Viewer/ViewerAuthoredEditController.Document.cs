// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;

namespace OpenUsd.Viewer;

internal sealed partial class ViewerAuthoredEditController
{
    private UsdLayerIdentity? _initialSessionIdentity;
    private ulong _initialSessionRevision;
    private int _suspended;

    internal bool IsSuspended => Volatile.Read(ref _suspended) != 0;

    internal async Task<UsdLayerCheckpoint> CaptureReviewCheckpointAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (IsSuspended)
            {
                throw new InvalidOperationException("Finish or cancel the document transition before exporting.");
            }
            return await _scheduler.InvokeAsync(stage =>
            {
                ViewerDocumentObservation document = ReadDocument(stage);
                if (document.State.Review is not { } review)
                {
                    throw new InvalidOperationException("This document has no review layer to export.");
                }
                using UsdLayer layer = stage.GetLocalLayer(review.Identifier);
                return layer.CaptureCheckpoint();
            }, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<bool> TrySuspendAsync(
        ViewerDocumentObservation expected, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        bool suspended = false;
        try
        {
            if (IsSuspended)
            {
                throw new InvalidOperationException("A document transition is already pending.");
            }
            suspended = await _scheduler.InvokeAsync(stage =>
            {
                if (!expected.Matches(ReadDocument(stage)))
                {
                    return false;
                }
                Interlocked.Exchange(ref _suspended, 1);
                return true;
            }, linked.Token).ConfigureAwait(false);
            return suspended;
        }
        finally
        {
            _gate.Release();
            if (suspended)
            {
                Changed?.Invoke();
            }
        }
    }

    internal void Resume()
    {
        if (Interlocked.Exchange(ref _suspended, 0) != 0 && !_disposed)
        {
            Changed?.Invoke();
        }
    }

    internal async Task<UsdStageRetirementLease?> TryPrepareRetirementAsync(
        ViewerDocumentObservation expected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (!IsSuspended)
            {
                throw new InvalidOperationException("The document writer must be suspended before retirement.");
            }
            return await _scheduler.TryPrepareRetirementAsync(
                stage => expected.Matches(ReadDocument(stage)), linked.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<ViewerDocumentObservation> ReadDocumentAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            return await _scheduler.InvokeAsync(ReadDocument, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private ViewerDocumentObservation ReadDocument(UsdStage stage)
    {
        string[] identifiers = stage.GetLayerStackIdentifiers();
        if (identifiers.Length > 256)
        {
            throw new NotSupportedException("Document editing supports at most 256 local layers.");
        }
        var layers = new UsdLayerEditingState[identifiers.Length];
        UsdLayerEditingState? source = null;
        UsdLayerEditingState? review = null;
        List<string> otherChanged = [];
        for (int index = 0; index < identifiers.Length; index++)
        {
            using UsdLayer layer = stage.GetLocalLayer(identifiers[index]);
            UsdLayerEditingState state = layer.GetEditingState();
            layers[index] = state;
            switch (state.Role)
            {
                case UsdLayerRole.Root:
                    source = state;
                    break;
                case UsdLayerRole.UserReview:
                    if (review is not null)
                    {
                        throw new InvalidDataException("The stage reports multiple active user-review layers.");
                    }
                    review = state;
                    break;
                case UsdLayerRole.SessionContainer:
                    if (_initialSessionIdentity is null)
                    {
                        _initialSessionIdentity = state.Identity;
                        _initialSessionRevision = state.Revision;
                    }
                    else if (state.Identity != _initialSessionIdentity || state.Revision != _initialSessionRevision)
                    {
                        otherChanged.Add(state.Identifier);
                    }
                    break;
                case UsdLayerRole.Local when state.IsDirty:
                    otherChanged.Add(state.Identifier);
                    break;
            }
        }
        if (source is null)
        {
            throw new InvalidDataException("The exact source layer is unavailable.");
        }
        var document = new ViewerDocumentState(
            _documentId, ToLayerState(source, ViewerDocumentLayerRole.Source),
            review is null ? null : ToLayerState(review, ViewerDocumentLayerRole.Review) with
            {
                FilePath = SavedReviewPath,
                HasChanges = review.IsDirty || _publicationUnacknowledged
            },
            stage.EditTargetLayerIdentifier, ReviewAbsenceConfirmed: review is null,
            HasOtherLayerChanges: otherChanged.Count != 0 || (_publicationUnacknowledged && review is null));
        return new ViewerDocumentObservation(document, layers, otherChanged);
    }

    private ViewerAuthoredEditCapture CaptureReview(UsdStage stage, UsdLayerEditAddress[] addresses)
    {
        using UsdLayer review = GetReviewForCapture(stage);
        UsdLayerAuthoredSnapshot snapshot = review.CaptureAuthored(addresses);
        ValidateBookmarkCapture(stage, review, snapshot);
        return new ViewerAuthoredEditCapture(_documentId, review.Identifier, snapshot);
    }

    private UsdLayer GetReviewForCapture(UsdStage stage)
    {
        using UsdLayer session = stage.GetSessionLayer();
        UsdLayerEditingState before = session.GetEditingState();
        UsdLayer review = stage.GetUserReviewLayer();
        try
        {
            if (before.Identity == _initialSessionIdentity && before.Revision == _initialSessionRevision)
            {
                // Only the facade's own empty review-layer insertion occurred in this scheduler callback.
                // Do not acknowledge a session revision that already contained an external change.
                UsdLayerEditingState after = session.GetEditingState();
                _initialSessionIdentity = after.Identity;
                _initialSessionRevision = after.Revision;
            }
            return review;
        }
        catch
        {
            review.Dispose();
            throw;
        }
    }

    private static ViewerDocumentLayerState ToLayerState(
        UsdLayerEditingState layer, ViewerDocumentLayerRole role) => new(
            layer.Identifier, role, layer.IsAnonymous ? null : layer.RealPath,
            layer.Revision, layer.IsDirty, layer.CanAttemptAuthoredEdits);
}
