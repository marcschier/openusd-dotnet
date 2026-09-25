// Copyright (c) marcschier. Licensed under the MIT License.

using Microsoft.Win32.SafeHandles;

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Owns a serialized native Hydra session using the hdSilk renderer plugin.
/// </summary>
/// <remarks>
/// A renderer consumes this delta stream in order. After GPU page preparation fails,
/// the next sync first retries that renderer's retained page on the calling thread.
/// Repeated refusal produces no newer native delta. A rejected older page or conflicting
/// consumers invalidate automatic recovery; recreate the session rather than dropping changes.
/// </remarks>
public sealed class OpenUsdSilkSession : IDisposable
{
    private readonly object _gate = new();
    private readonly SilkSessionSafeHandle _handle;
    private readonly WeakReference<OpenUsdSilkSession> _pageRecoverySource;
    private bool _hasSynchronized;
    private ulong _lastPageRevision;
    private Action? _replayRejectedPage;
    private ulong _replaySequence;
    private bool _replayConflict;

    internal OpenUsdSilkSession(
        nint handle,
        UsdStageRenderLease? stageLease,
        SilkPreparationLimits? preparationLimits = null)
    {
        _pageRecoverySource = new(this);
        _handle = new SilkSessionSafeHandle(handle, stageLease);
        PreparationLimits = preparationLimits;
        SilkManagedDiagnostics.SessionCreated();
    }

    /// <summary>Gets immutable session ceilings, or null for historical per-request-only admission.</summary>
    public SilkPreparationLimits? PreparationLimits { get; }

    internal bool HasSynchronized
    {
        get
        {
            lock (_gate)
            {
                return _hasSynchronized;
            }
        }
    }

    /// <summary>
    /// Synchronizes Hydra with the historical viewport ingestion defaults and
    /// returns a managed-owned immutable dirty page.
    /// Legacy sync intentionally restores the original purpose mask
    /// (default|proxy|render) and the original material-binding token
    /// (empty/allPurpose), even after an earlier explicit product-style sync.
    /// </summary>
    public OpenUsdSilkPage Sync(
        int width,
        int height,
        double timeCode = 0,
        CameraState camera = default,
        RenderComplexity complexity = RenderComplexity.Low,
        RenderDrawMode drawMode = RenderDrawMode.SmoothShaded) =>
        SyncCore(width, height, timeCode, camera, complexity, drawMode, null);

    /// <summary>
    /// Synchronizes Hydra using explicit scene-ingestion choices and returns a
    /// managed-owned immutable dirty page.
    /// </summary>
    public OpenUsdSilkPage Sync(
        int width,
        int height,
        SilkSceneIngestionOptions ingestionOptions,
        double timeCode = 0,
        CameraState camera = default,
        RenderComplexity complexity = RenderComplexity.Low,
        RenderDrawMode drawMode = RenderDrawMode.SmoothShaded)
    {
        ArgumentNullException.ThrowIfNull(ingestionOptions);
        return SyncCore(width, height, timeCode, camera, complexity, drawMode,
            PreparationLimits?.Apply(ingestionOptions) ?? ingestionOptions);
    }

    private OpenUsdSilkPage SyncCore(
        int width, int height, double timeCode, CameraState camera, RenderComplexity complexity,
        RenderDrawMode drawMode, SilkSceneIngestionOptions? ingestionOptions)
    {
        while (true)
        {
            Action replay;
            ulong replaySequence;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_handle.IsClosed || _handle.IsInvalid, this);
                if (_replayConflict)
                {
                    throw new InvalidOperationException(
                        "A rejected page no longer has a live, single, ordered renderer consumer. " +
                        "Recreate the session instead of continuing an incomplete stream.");
                }
                if (_replayRejectedPage is not { } pending)
                {
                    OpenUsdSilkPage page = ingestionOptions is null
                        ? OpenUsdSilkRuntime.Sync(_handle.DangerousGetHandle(), width, height, timeCode,
                            camera, complexity, drawMode, PreparationLimits)
                        : OpenUsdSilkRuntime.Sync(_handle.DangerousGetHandle(), width, height, timeCode,
                            camera, complexity, drawMode, ingestionOptions);
                    _hasSynchronized = true;
                    _lastPageRevision = page.Revision;
                    page.SetRecoverySource(_pageRecoverySource);
                    return page;
                }
                replay = pending;
                replaySequence = _replaySequence;
            }
            // Do not take the renderer's lock while holding the native-session lock.
            replay();
            lock (_gate)
            {
                if (ReferenceEquals(_replayRejectedPage, replay) && _replaySequence == replaySequence)
                {
                    _replayRejectedPage = null;
                }
            }
        }
    }

    internal void RegisterRejectedPage(ulong revision, Action replay)
    {
        lock (_gate)
        {
            if (_handle.IsClosed || _handle.IsInvalid)
            {
                return;
            }
            if (revision != _lastPageRevision ||
                (_replayRejectedPage is not null && !ReferenceEquals(_replayRejectedPage, replay)))
            {
                _replayConflict = true;
                throw new InvalidOperationException("Only the latest page's single consumer can register replay.");
            }
            _replaySequence = checked(_replaySequence + 1);
            _replayRejectedPage = replay;
        }
    }

    internal void RevokePageReplay(Action replay)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_replayRejectedPage, replay))
            {
                _replayRejectedPage = null;
                _replayConflict = true;
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_handle.IsClosed || _handle.IsInvalid)
            {
                return;
            }

            OpenUsdSilkRuntime.Destroy(_handle.DangerousGetHandle());
            _replayRejectedPage = null;
            _handle.CompleteCheckedDestroy();
        }
    }

    private sealed class SilkSessionSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private int _diagnosticOwned = 1;
        private UsdStageRenderLease? _stageLease;

        internal SilkSessionSafeHandle(
            nint handle,
            UsdStageRenderLease? stageLease)
            : base(ownsHandle: true)
        {
            _stageLease = stageLease;
            SetHandle(handle);
        }

        internal void CompleteCheckedDestroy()
        {
            SetHandleAsInvalid();
            Interlocked.Exchange(ref _stageLease, null)?.Dispose();
            ReleaseDiagnosticOwnership();
            Dispose();
        }

        protected override bool ReleaseHandle()
        {
            try
            {
                OpenUsdSilkRuntime.ReleaseSession(handle);
            }
            finally
            {
                Interlocked.Exchange(ref _stageLease, null)?.Dispose();
                ReleaseDiagnosticOwnership();
            }
            return true;
        }

        private void ReleaseDiagnosticOwnership()
        {
            if (Interlocked.Exchange(ref _diagnosticOwned, 0) != 0)
            {
                SilkManagedDiagnostics.SessionDestroyed();
            }
        }
    }
}
