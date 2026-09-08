// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography;
using OpenUsd.Editing;

namespace OpenUsd.Viewer;

internal static class ViewerReviewRecoveryCandidate
{
    internal static async Task<ViewerPreparedDocument?> PrepareAsync(
        ViewerRecoveryStore store, ViewerPreparedDocument original, string key, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || original.SourceBinding is not { } source)
        {
            return null;
        }
        ViewerDocumentRecovery? checkpoint = await store.LoadAsync(key, cancellationToken).ConfigureAwait(false);
        if (checkpoint is null)
        {
            return null;
        }
        var expected = new ViewerRecoveryBinding(
            Path.GetFullPath(source.SourceRootPath), source.SourceFingerprint, key, source.AssetAnchor);
        ViewerRecoveryAdmission admission = checkpoint.Validate(expected, "URD1");
        if (!admission.CanOffer)
        {
            throw new InvalidDataException(admission.Message);
        }
        UsdReviewDocument document = await Task.Run(
            () => UsdReviewDocument.Read(checkpoint.CopyPayload(), original.SourcePath), cancellationToken)
            .ConfigureAwait(false);
        if (original.ImportedReview is { } published && document.DocumentId != published.DocumentId)
        {
            throw new InvalidDataException(
                "Recovery belongs to another review lineage; explicit reconciliation is required.");
        }
        ViewerPreparedDocument prepared = await ViewerPreparedDocument.OpenReviewAsync(
            document, original.SourcePath, original.ReviewFileIdentity, recovery: true, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            ViewerFileIdentity identity = await ViewerReviewDocumentPublication.ObserveAsync(
                document, store.GetCheckpointPath(key), cancellationToken).ConfigureAwait(false);
            byte[] encoded = ViewerRecoveryStore.Encode(checkpoint);
            if (!identity.Exists || identity.Length != encoded.LongLength ||
                identity.Sha256 != Convert.ToHexString(SHA256.HashData(encoded)))
            {
                throw new IOException("The recovery checkpoint changed during validation.");
            }
            prepared.AttachRecovery(key, document, identity, original.PublishedReview);
            return prepared;
        }
        catch
        {
            await prepared.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
