// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;

namespace OpenUsd.Viewer;

internal sealed class ViewerPreparedDocument : IAsyncDisposable
{
    private UsdStageScheduler? _ownedScheduler;

    private ViewerPreparedDocument(
        UsdStageScheduler scheduler, string sourcePath, UsdReviewSourceBinding? binding, string? sessionOnlyReason)
    {
        _ownedScheduler = scheduler;
        Scheduler = scheduler;
        SourcePath = sourcePath;
        SourceBinding = binding;
        SessionOnlyReason = sessionOnlyReason;
    }

    internal UsdStageScheduler Scheduler { get; }
    internal string SourcePath { get; }
    internal UsdReviewSourceBinding? SourceBinding { get; }
    internal string? SessionOnlyReason { get; }
    internal UsdReviewDocument? ImportedReview { get; private init; }
    internal UsdReviewDocument? PublishedReview { get; private set; }
    internal ViewerFileIdentity? ReviewFileIdentity { get; private init; }
    internal bool IsRecovery { get; private init; }
    internal UsdReviewDocument? RecoveryDocument { get; private set; }
    internal ViewerFileIdentity? RecoveryIdentity { get; private set; }
    internal string? RecoveryKey { get; private set; }

    internal void AttachRecovery(
        string key, UsdReviewDocument document, ViewerFileIdentity identity, UsdReviewDocument? publishedReview)
    {
        RecoveryKey = key;
        RecoveryDocument = document;
        RecoveryIdentity = identity;
        PublishedReview = publishedReview;
    }

    internal static async Task<ViewerPreparedDocument> OpenSourceAsync(
        string path, string? explicitSessionOnlyReason, CancellationToken cancellationToken)
    {
        string source = Path.GetFullPath(path);
        string? reason = explicitSessionOnlyReason ??
            await Task.Run(() => GetSessionOnlyReason(source), cancellationToken).ConfigureAwait(false);
        ViewerStartupOptions.WriteStatus("Viewer stage open: validation scheduler starting");
        UsdStageScheduler scheduler = reason is null
            ? UsdStageScheduler.OpenForReview(source, 1024, 32)
            : UsdStageScheduler.Open(source, 1024, 32);
        try
        {
            UsdReviewSourceBinding? binding = reason is null
                ? await scheduler.InvokeAsync(
                    static stage => stage.CaptureReviewSourceBinding(), cancellationToken).ConfigureAwait(false)
                : null;
            ViewerStartupOptions.WriteStatus("Viewer stage open: validation root layer query starting");
            _ = await scheduler.InvokeAsync(static stage => stage.RootLayerIdentifier, cancellationToken)
                .ConfigureAwait(false);
            ViewerStartupOptions.WriteStatus("Viewer stage open: validation root layer query completed");
            return new ViewerPreparedDocument(scheduler, source, binding, reason);
        }
        catch
        {
            await scheduler.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal void TransferOwnership() => _ownedScheduler = null;

    internal static async Task<ViewerPreparedDocument> OpenReviewAsync(
        UsdReviewDocument document, string expectedSourcePath, ViewerFileIdentity? identity,
        bool recovery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        string sourcePath = Path.GetFullPath(expectedSourcePath);
        UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(sourcePath, 1024, 32);
        try
        {
            UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
                static stage => stage.CaptureReviewSourceBinding(), cancellationToken).ConfigureAwait(false);
            UsdReviewDocumentImportResult imported = await scheduler.InvokeAsync(
                stage => stage.ImportReviewDocument(document, binding), cancellationToken).ConfigureAwait(false);
            if (imported.Outcome != UsdLayerEditOutcome.Applied)
            {
                throw new InvalidDataException(
                    imported.Diagnostic ?? $"The review import was refused: {imported.Outcome}.");
            }
            return new ViewerPreparedDocument(scheduler, sourcePath, binding, null)
            {
                ImportedReview = document,
                PublishedReview = recovery ? null : document,
                ReviewFileIdentity = identity,
                IsRecovery = recovery
            };
        }
        catch
        {
            await scheduler.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal async Task RevalidateSourceAsync(CancellationToken cancellationToken)
    {
        if (SourceBinding is { } source)
        {
            UsdReviewSourceBinding current = await Scheduler.InvokeAsync(
                static stage => stage.CaptureReviewSourceBinding(), cancellationToken).ConfigureAwait(false);
            if (!source.HasSamePayload(current))
            {
                throw new IOException(
                    "The verified source changed while opening the review. The old document was kept.");
            }
        }
    }

    internal static string? GetSessionOnlyReason(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return "Portable review saving currently requires verified Windows filesystem sources.";
        }
        string extension = Path.GetExtension(path);
        if (!extension.Equals(".usda", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".usd", StringComparison.OrdinalIgnoreCase))
        {
            return "This source format is available for viewing, but not for verified portable review saving.";
        }
        try
        {
            using FileStream stream = ViewerWindowsFiles.OpenRead(ViewerWindowsFiles.NormalizePath(path));
            Span<byte> header = stackalloc byte[8];
            int read = stream.Read(header);
            return read == header.Length && header.SequenceEqual("PXR-USDC"u8)
                ? "Crate sources remain available for viewing; portable review saving requires a text-USD source."
                : null;
        }
        catch (NotSupportedException exception)
        {
            return $"Session-only source: {exception.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        UsdStageScheduler? scheduler = _ownedScheduler;
        _ownedScheduler = null;
        if (scheduler is not null)
        {
            await scheduler.DisposeAsync().ConfigureAwait(false);
        }
    }
}
