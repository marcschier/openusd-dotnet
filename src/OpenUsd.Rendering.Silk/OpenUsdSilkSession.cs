// Copyright (c) marcschier. Licensed under the MIT License.

using Microsoft.Win32.SafeHandles;

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Owns a serialized native Hydra session using the hdSilk renderer plugin.
/// </summary>
public sealed class OpenUsdSilkSession : IDisposable
{
    private readonly object _gate = new();
    private readonly SilkSessionSafeHandle _handle;
    private bool _hasSynchronized;

    internal OpenUsdSilkSession(
        nint handle,
        UsdStageRenderLease? stageLease)
    {
        _handle = new SilkSessionSafeHandle(handle, stageLease);
        SilkManagedDiagnostics.SessionCreated();
    }

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
        RenderDrawMode drawMode = RenderDrawMode.SmoothShaded)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_handle.IsClosed || _handle.IsInvalid, this);
            OpenUsdSilkPage page = OpenUsdSilkRuntime.Sync(
                _handle.DangerousGetHandle(),
                width,
                height,
                timeCode,
                camera,
                complexity,
                drawMode);
            _hasSynchronized = true;
            return page;
        }
    }

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

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_handle.IsClosed || _handle.IsInvalid, this);
            OpenUsdSilkPage page = OpenUsdSilkRuntime.Sync(
                _handle.DangerousGetHandle(),
                width,
                height,
                timeCode,
                camera,
                complexity,
                drawMode,
                ingestionOptions);
            _hasSynchronized = true;
            return page;
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
