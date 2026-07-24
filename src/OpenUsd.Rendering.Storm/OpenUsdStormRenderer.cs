// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace OpenUsd.Rendering.Storm;

/// <summary>
/// Explicitly owns a thread-affine Hydra/Storm engine.
/// </summary>
/// <remarks>
/// This type is deliberately non-finalizable. Call <see cref="Dispose"/> while
/// the creation OpenGL context is current, or <see cref="Abandon"/> on the
/// creation thread after context loss or visual detachment.
/// </remarks>
public sealed class OpenUsdStormRenderer : IDisposable
{
    private static int _liveCount;
    private readonly object _gate = new();
    private readonly int _ownerThreadId;
    private readonly Action<nint> _destroyNative;
    private readonly Action<nint> _detachNative;
    private Action? _releaseStageLease;
    private nint _handle;
    private StormFrameBinding _lastFrame;
    private bool _hasRenderedFrame;

    internal OpenUsdStormRenderer(
        nint handle,
        UsdStageRenderLease? stageLease,
        string name)
        : this(
            handle,
            name,
            OpenUsdStormRuntime.Destroy,
            OpenUsdStormRuntime.Detach,
            stageLease is null ? null : stageLease.Dispose)
    {
    }

    internal OpenUsdStormRenderer(
        nint handle,
        string name,
        Action<nint> destroyNative,
        Action<nint> detachNative,
        Action? releaseStageLease)
    {
        _handle = handle;
        _destroyNative = destroyNative;
        _detachNative = detachNative;
        _releaseStageLease = releaseStageLease;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        Name = name;
        Interlocked.Increment(ref _liveCount);
    }

    internal static int LiveCount => Volatile.Read(ref _liveCount);

    /// <summary>Gets the immutable renderer and Hgi backend display name.</summary>
    public string Name { get; }

    /// <summary>Gets the renderer-neutral backend identity.</summary>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Backend identity is part of the renderer instance contract.")]
    public RenderBackendKind BackendKind => RenderBackendKind.Storm;

    /// <summary>Renders into the supplied OpenGL framebuffer.</summary>
    /// <returns><see langword="true"/> when the renderer is converged.</returns>
    public bool Render(
        int width,
        int height,
        uint framebuffer,
        double timeCode = 0,
        CameraState camera = default,
        ulong revision = 0,
        ulong? sceneRevision = null)
    {
        lock (_gate)
        {
            ThrowIfWrongThread();
            ObjectDisposedException.ThrowIf(_handle == 0, this);
            bool converged = OpenUsdStormRuntime.Render(
                _handle,
                width,
                height,
                framebuffer,
                timeCode,
                camera,
                revision,
                sceneRevision);
            _lastFrame = new StormFrameBinding(
                width,
                height,
                timeCode,
                camera,
                revision,
                sceneRevision,
                ContextGeneration: 0);
            _hasRenderedFrame = true;
            return converged;
        }
    }

    /// <summary>Resolves one nearest hit from the exact last rendered frame.</summary>
    public RenderPickResult Pick(RenderPickRequest request)
    {
        lock (_gate)
        {
            ThrowIfWrongThread();
            ObjectDisposedException.ThrowIf(_handle == 0, this);
            if (!_hasRenderedFrame)
            {
                throw new InvalidOperationException(
                    "Storm must render a frame before picking.");
            }
            return OpenUsdStormRuntime.Pick(_handle, request, _lastFrame);
        }
    }

    /// <summary>Applies one packed Storm selection-highlight update.</summary>
    /// <remarks>
    /// OpenUSD scene-index mode currently reduces instance-index highlights to
    /// whole-path selection; legacy scene-delegate mode honors supported indices.
    /// </remarks>
    public void SetSelection(SelectionState selection, System.Numerics.Vector4 color)
    {
        lock (_gate)
        {
            ThrowIfWrongThread();
            ObjectDisposedException.ThrowIf(_handle == 0, this);
            OpenUsdStormRuntime.SetSelection(_handle, selection, color);
        }
    }

    /// <summary>
    /// Destroys the renderer while its creation OpenGL context is current.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            ThrowIfWrongThread();
            if (_handle == 0)
            {
                return;
            }

            _destroyNative(_handle);
            CompleteRelease();
        }
    }

    /// <summary>
    /// Releases stage/session bookkeeping after context loss without invoking
    /// the GL engine destructor.
    /// </summary>
    /// <remarks>
    /// The native GL engine is intentionally orphaned for the remaining process
    /// lifetime because destroying it without its context is unsafe. Stage,
    /// scheduler-child, and renderer-wrapper ownership is still released.
    /// </remarks>
    public void Abandon()
    {
        lock (_gate)
        {
            ThrowIfWrongThread();
            if (_handle == 0)
            {
                return;
            }

            _detachNative(_handle);
            CompleteRelease();
        }
    }

    internal void ReleaseAfterDetach()
    {
        lock (_gate)
        {
            ThrowIfWrongThread();
            if (_handle == 0)
            {
                return;
            }

            _detachNative(_handle);
            CompleteRelease();
        }
    }

    private void CompleteRelease()
    {
        _handle = 0;
        Interlocked.Exchange(ref _releaseStageLease, null)?.Invoke();
        Interlocked.Decrement(ref _liveCount);
    }

    private void ThrowIfWrongThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                "Storm operations must run on the renderer's creation thread.");
        }
    }
}
