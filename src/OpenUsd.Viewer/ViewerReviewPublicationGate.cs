// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer;

internal readonly record struct ViewerReviewPublicationKey(ViewerPhysicalFileIdentity Parent, string Name);

internal sealed class ViewerReviewPublicationGate : IDisposable
{
    private static readonly object Sync = new();
    private static readonly Dictionary<ViewerReviewPublicationKey, Entry> Entries = [];
    private readonly ViewerReviewPublicationKey _key;
    private readonly Entry _entry;
    private bool _disposed;

    private ViewerReviewPublicationGate(ViewerReviewPublicationKey key, Entry entry)
    {
        _key = key;
        _entry = entry;
    }

    internal static async Task<ViewerReviewPublicationGate> AcquireAsync(
        ViewerReviewPublicationKey key, CancellationToken cancellationToken)
    {
        Entry entry;
        lock (Sync)
        {
            if (Entries.TryGetValue(key, out Entry? existing))
            {
                entry = existing;
            }
            else
            {
                entry = new Entry();
                Entries.Add(key, entry);
            }
            entry.References++;
        }
        try
        {
            await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new ViewerReviewPublicationGate(key, entry);
        }
        catch
        {
            ReleaseReference(key, entry);
            throw;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _entry.Gate.Release();
            ReleaseReference(_key, _entry);
        }
    }

    private static void ReleaseReference(ViewerReviewPublicationKey key, Entry entry)
    {
        lock (Sync)
        {
            if (--entry.References == 0)
            {
                Entries.Remove(key);
                entry.Gate.Dispose();
            }
        }
    }

    private sealed class Entry
    {
        internal SemaphoreSlim Gate { get; } = new(1, 1);
        internal int References { get; set; }
    }
}
