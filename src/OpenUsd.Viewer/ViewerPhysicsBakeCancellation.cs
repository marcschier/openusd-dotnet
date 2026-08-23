// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer;

/// <summary>
/// Owns the cancellation source of the physics bake that is currently running.
/// </summary>
/// <remarks>
/// A bake holds the transport command gate for its whole duration, so the cancel affordance can
/// never route through that gate: it has to signal the running bake directly. This type keeps that
/// hand-off race free. The bake takes a lease, the cancel button and document teardown signal
/// whatever lease is current, and only the lease that is still installed disposes its own source, so
/// a cancel that arrives while the bake is completing observes either a live source or nothing at
/// all - never a disposed one.
/// </remarks>
internal sealed class ViewerPhysicsBakeCancellation : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _current;
    private bool _disposed;

    /// <summary>
    /// Gets a value indicating whether a bake currently holds a lease.
    /// </summary>
    internal bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _current is not null;
            }
        }
    }

    /// <summary>
    /// Starts a bake lease linked to the lifetime of the owning document.
    /// </summary>
    /// <param name="documentLifetime">The token that closes with the document.</param>
    /// <returns>The lease whose token the bake must observe.</returns>
    internal ViewerPhysicsBakeLease Begin(CancellationToken documentLifetime)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(documentLifetime);
        CancellationTokenSource? superseded = null;
        bool refused;
        lock (_gate)
        {
            refused = _disposed;
            if (!refused)
            {
                superseded = _current;
                _current = source;
            }
        }

        // A bake started after teardown, or one that somehow overlapped an earlier bake, is asked to
        // stop immediately instead of being handed a token that nobody will ever signal.
        superseded?.Cancel();
        if (refused)
        {
            source.Cancel();
        }

        return new ViewerPhysicsBakeLease(this, source);
    }

    /// <summary>
    /// Signals the bake that currently holds a lease, if any.
    /// </summary>
    /// <returns><see langword="true"/> when a running bake was signaled.</returns>
    internal bool Cancel()
    {
        CancellationTokenSource? current;
        lock (_gate)
        {
            current = _current;
        }

        if (current is null)
        {
            return false;
        }

        try
        {
            current.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            // The bake completed between the read and the signal, which is what the caller wanted.
            return false;
        }
    }

    /// <summary>
    /// Cancels any running bake and refuses new leases.
    /// </summary>
    public void Dispose()
    {
        CancellationTokenSource? current;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            current = _current;
        }

        try
        {
            current?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// Releases a lease, disposing its source only when it is still the installed one.
    /// </summary>
    /// <param name="source">The source the lease was created with.</param>
    internal void Release(CancellationTokenSource source)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_current, source))
            {
                _current = null;
            }
        }

        source.Dispose();
    }
}

/// <summary>
/// The cancellation lease held by a running physics bake.
/// </summary>
internal readonly struct ViewerPhysicsBakeLease : IDisposable, IEquatable<ViewerPhysicsBakeLease>
{
    private readonly ViewerPhysicsBakeCancellation? _owner;
    private readonly CancellationTokenSource? _source;

    internal ViewerPhysicsBakeLease(
        ViewerPhysicsBakeCancellation owner,
        CancellationTokenSource source)
    {
        _owner = owner;
        _source = source;
    }

    /// <summary>
    /// Gets the token the bake must observe.
    /// </summary>
    internal CancellationToken Token => _source?.Token ?? new CancellationToken(true);

    /// <summary>
    /// Gets a value indicating whether the bake has been asked to stop.
    /// </summary>
    internal bool IsCancellationRequested => _source?.IsCancellationRequested ?? true;

    /// <summary>
    /// Compares two leases.
    /// </summary>
    /// <param name="left">The first lease.</param>
    /// <param name="right">The second lease.</param>
    /// <returns><see langword="true"/> when both leases wrap the same source.</returns>
    public static bool operator ==(ViewerPhysicsBakeLease left, ViewerPhysicsBakeLease right) =>
        left.Equals(right);

    /// <summary>
    /// Compares two leases.
    /// </summary>
    /// <param name="left">The first lease.</param>
    /// <param name="right">The second lease.</param>
    /// <returns><see langword="true"/> when the leases wrap different sources.</returns>
    public static bool operator !=(ViewerPhysicsBakeLease left, ViewerPhysicsBakeLease right) =>
        !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(ViewerPhysicsBakeLease other) => ReferenceEquals(_source, other._source);

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is ViewerPhysicsBakeLease other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _source?.GetHashCode() ?? 0;

    /// <summary>
    /// Ends the lease.
    /// </summary>
    public void Dispose()
    {
        if (_owner is null || _source is null)
        {
            return;
        }

        _owner.Release(_source);
    }
}
