// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using System.Security.Cryptography;
using OpenUsd.Editing;

namespace OpenUsd.Viewer;

/// <summary>Stages a review under source/ancestor guards, then performs optimistic atomic publication.</summary>
/// <remarks>
/// The final observed-version check is not filesystem CAS. An uncooperative review-file replacement
/// after that check is outside its guarantee. Retained source/dependency guards remain independent.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class ViewerReviewDocumentPublication : IAsyncDisposable
{
    private readonly ViewerReviewSaveCapture _capture;
    private readonly ViewerReviewFileGuards _guards;
    private readonly ViewerReviewPublicationGate _gate;
    private readonly FileStream _staging;
    private readonly string _hash;
    private readonly ViewerPhysicalFileIdentity _identity;
    private readonly int _length;
    private bool _published;
    private bool _disposed;

    private ViewerReviewDocumentPublication(
        ViewerReviewSaveCapture capture, ViewerReviewFileGuards guards, ViewerReviewPublicationGate gate,
        FileStream staging, string hash, int length)
    {
        _capture = capture;
        _guards = guards;
        _gate = gate;
        _staging = staging;
        _hash = hash;
        _length = length;
        _identity = ViewerWindowsFiles.GetIdentity(staging.SafeFileHandle);
    }

    internal static async Task<ViewerFileIdentity> ObserveAsync(
        UsdReviewDocument document, string destinationPath, CancellationToken cancellationToken)
    {
        using ViewerReviewFileGuards guards =
            await ViewerReviewFileGuards.AcquireAsync(document, destinationPath, cancellationToken)
                .ConfigureAwait(false);
        using ViewerReviewPublicationGate gate =
            await ViewerReviewPublicationGate.AcquireAsync(guards.Key, cancellationToken).ConfigureAwait(false);
        return await guards.ObserveDestinationAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static Task<ViewerReviewDocumentPublication> StageAsync(
        ViewerReviewSaveCapture capture, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.Document.ByteLength is <= 0 or > ViewerAuthoredEditController.MaximumReviewDocumentBytes)
        {
            throw new InvalidDataException("The portable review exceeds the Viewer's publication bound.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return StagePayloadAsync(capture, capture.Document.CopyBytes(), cancellationToken);
    }

    internal static Task<ViewerReviewDocumentPublication> StageRecoveryAsync(
        ViewerDocumentRecovery checkpoint, ViewerFileIdentity destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        UsdReviewDocument document = checkpoint.NativeDocument ??
            throw new InvalidOperationException("Only a newly captured native document can publish recovery.");
        var capture = new ViewerReviewSaveCapture(
            Guid.Empty, document.OriginalTargetIdentifier, document, destination);
        return StagePayloadAsync(capture, ViewerRecoveryStore.Encode(checkpoint), cancellationToken);
    }

    private static async Task<ViewerReviewDocumentPublication> StagePayloadAsync(
        ViewerReviewSaveCapture capture, byte[] bytes, CancellationToken cancellationToken)
    {
        if (bytes.Length is <= 0 or > ViewerAuthoredEditController.MaximumReviewDocumentBytes)
        {
            throw new InvalidDataException("The staged file exceeds the publication byte bound.");
        }
        ViewerReviewFileGuards guards = await ViewerReviewFileGuards.AcquireAsync(
            capture.Document, capture.Destination.FullPath, cancellationToken).ConfigureAwait(false);
        ViewerReviewPublicationGate? gate = null;
        FileStream? staging = null;
        try
        {
            gate = await ViewerReviewPublicationGate.AcquireAsync(guards.Key, cancellationToken).ConfigureAwait(false);
            string hash = Convert.ToHexString(SHA256.HashData(bytes));
            cancellationToken.ThrowIfCancellationRequested();
            staging = guards.CreateStaging();
            await staging.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await staging.FlushAsync(cancellationToken).ConfigureAwait(false);
            staging.Flush(flushToDisk: true);
            return new ViewerReviewDocumentPublication(capture, guards, gate, staging, hash, bytes.Length);
        }
        catch
        {
            try
            {
                if (staging is not null)
                {
                    try
                    {
                        ViewerWindowsFiles.DeleteOwnedStaging(staging.SafeFileHandle);
                    }
                    finally
                    {
                        await staging.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                gate?.Dispose();
                guards.Dispose();
            }
            throw;
        }
    }

    internal async Task<ViewerFileIdentity> PublishAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_published)
        {
            throw new InvalidOperationException("The staged review was already published.");
        }
        ViewerFileIdentity current = await _guards.ObserveDestinationAsync(cancellationToken).ConfigureAwait(false);
        if (!_capture.Destination.Matches(current))
        {
            throw new IOException("The review destination changed after selection. Reopen it or choose Save As.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        ViewerWindowsFiles.Rename(
            _staging.SafeFileHandle, _guards.Parent, _guards.Name, replace: current.Exists);
        _published = true;
        return new ViewerFileIdentity(_guards.Destination, true, _length, _hash,
            _identity, _guards.ParentIdentity);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        try
        {
            try
            {
                if (!_published)
                {
                    ViewerWindowsFiles.DeleteOwnedStaging(_staging.SafeFileHandle);
                }
            }
            finally
            {
                await _staging.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Dispose();
            _guards.Dispose();
        }
    }
}
