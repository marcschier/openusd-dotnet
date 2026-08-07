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

    internal OpenUsdSilkSession(
        nint handle,
        UsdStageRenderLease? stageLease)
    {
        _handle = new SilkSessionSafeHandle(handle, stageLease);
        SilkManagedDiagnostics.SessionCreated();
    }

    /// <summary>
    /// Synchronizes Hydra and returns a managed-owned immutable dirty page.
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
            return OpenUsdSilkRuntime.Sync(
                _handle.DangerousGetHandle(),
                width,
                height,
                timeCode,
                camera,
                complexity,
                drawMode);
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
