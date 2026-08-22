// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd;

/// <summary>
/// Retains the native stage identity used to create future renderer sessions.
/// </summary>
/// <remarks>
/// Acquire instances from <see cref="UsdStageScheduler.AcquireRenderSourceAsync"/>.
/// A source owns no publicly exposed safe handle. Use <see cref="AcquireLease"/> to obtain an
/// independent retained lifetime for scoped renderer-session creation.
/// </remarks>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public sealed class UsdStageRenderSource : IDisposable, IUsdStageBound
{
    private readonly Registration _registration;
    private Action<UsdStageRenderSource>? _disposeCallback;

    internal UsdStageRenderSource(
        UsdStageScheduler scheduler,
        OpenUsdNativeStage native)
    {
        _registration = new Registration(scheduler, native);
        SharedStageManagedDiagnostics.RenderSourceCreated();
    }

    /// <summary>Releases an abandoned retained stage registration.</summary>
    ~UsdStageRenderSource()
    {
        Release();
    }

    /// <summary>Acquires an independent retained native-stage lease.</summary>
    public UsdStageRenderLease AcquireLease() =>
        _registration.Retain();

    /// <inheritdoc/>
    public void Dispose()
    {
        Release();
        GC.SuppressFinalize(this);
    }

    internal void SetDisposeCallback(Action<UsdStageRenderSource> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (Interlocked.CompareExchange(ref _disposeCallback, callback, null) is not null)
        {
            throw new InvalidOperationException("A render-source disposal callback is already set.");
        }
    }

    private void Release()
    {
        _registration.Release();
        Interlocked.Exchange(ref _disposeCallback, null)?.Invoke(this);
    }

    [ExcludeFromCodeCoverage(
        Justification = "Exercised by clean native and NativeAOT integration probes.")]
    private sealed class Registration
    {
        private readonly object _gate = new();
        private OpenUsdNativeStage? _native;
        private UsdStageScheduler? _scheduler;
        private bool _released;

        internal Registration(
            UsdStageScheduler scheduler,
            OpenUsdNativeStage native)
        {
            _scheduler = scheduler;
            _native = native;
        }

        internal UsdStageRenderLease Retain()
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_released, typeof(UsdStageRenderSource));
                UsdStageScheduler scheduler = _scheduler!;
                scheduler.RetainRenderSourceRegistration();
                try
                {
                    return new UsdStageRenderLease(scheduler, _native!.Retain());
                }
                catch
                {
                    scheduler.ReleaseRenderSourceRegistration();
                    throw;
                }
            }
        }

        internal void Release()
        {
            OpenUsdNativeStage? native;
            UsdStageScheduler? scheduler;
            lock (_gate)
            {
                if (_released)
                {
                    return;
                }

                _released = true;
                native = _native;
                scheduler = _scheduler;
                _native = null;
                _scheduler = null;
            }

            try
            {
                native?.Dispose();
            }
            catch
            {
            }

            try
            {
                scheduler?.ReleaseRenderSourceRegistration();
            }
            catch
            {
            }
            SharedStageManagedDiagnostics.RenderSourceDestroyed();
        }
    }
}

/// <summary>
/// Owns an independent retained stage reference for scoped native renderer-session creation.
/// </summary>
/// <remarks>
/// The native pointer returned by <see cref="DangerousGetHandle"/> is valid only while this lease
/// remains undisposed. Callers must not release the pointer.
/// </remarks>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public sealed class UsdStageRenderLease : IDisposable, IUsdStageBound
{
    private OpenUsdNativeStage? _native;
    private UsdStageScheduler? _scheduler;

    internal UsdStageRenderLease(
        UsdStageScheduler scheduler,
        OpenUsdNativeStage native)
    {
        _scheduler = scheduler;
        _native = native;
        SharedStageManagedDiagnostics.RenderLeaseCreated();
    }

    /// <summary>
    /// Releases the retained native stage and scheduler child registration.
    /// </summary>
    ~UsdStageRenderLease()
    {
        Release();
    }

    internal nint DangerousGetHandle()
    {
        OpenUsdNativeStage native = Volatile.Read(ref _native)
            ?? throw new ObjectDisposedException(nameof(UsdStageRenderLease));
        return native.DangerousGetHandle();
    }

    internal OpenUsdNativeStage Native =>
        Volatile.Read(ref _native)
        ?? throw new ObjectDisposedException(nameof(UsdStageRenderLease));

    /// <inheritdoc/>
    public void Dispose()
    {
        Release();
        GC.SuppressFinalize(this);
    }

    private void Release()
    {
        OpenUsdNativeStage? native = Interlocked.Exchange(ref _native, null);
        UsdStageScheduler? scheduler = Interlocked.Exchange(ref _scheduler, null);
        try
        {
            native?.Dispose();
        }
        finally
        {
            scheduler?.ReleaseRenderSourceRegistration();
            if (native is not null)
            {
                SharedStageManagedDiagnostics.RenderLeaseDestroyed();
            }
        }
    }
}
