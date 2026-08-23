// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Runtime.CompilerServices;

namespace OpenUsd.Physics;

/// <summary>
/// An immutable unit quaternion describing one simulated body's orientation.
/// </summary>
/// <param name="X">The imaginary X component.</param>
/// <param name="Y">The imaginary Y component.</param>
/// <param name="Z">The imaginary Z component.</param>
/// <param name="W">The real component.</param>
public readonly record struct UsdPhysicsOrientation(double X, double Y, double Z, double W)
    : IUsdDetachedResult
{
    /// <summary>Gets the identity orientation.</summary>
    public static UsdPhysicsOrientation Identity { get; } = new(0, 0, 0, 1);

    /// <inheritdoc/>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({X}, {Y}, {Z}, {W})");
}

/// <summary>
/// Reports one simulated body's pose and velocity for a single published frame.
/// </summary>
/// <remarks>
/// The pose never retains a stage, prim, or native handle: bodies are addressed only by their stable
/// <see cref="UsdPhysicsObjectId"/>, so a frame stays valid and thread safe after the world that
/// produced it advances or is rebuilt.
/// </remarks>
/// <param name="Id">The stable identity of the simulated body.</param>
/// <param name="Position">The world-space position, in stage units.</param>
/// <param name="Orientation">The world-space orientation.</param>
/// <param name="LinearVelocity">The world-space linear velocity, in stage units per second.</param>
/// <param name="AngularVelocity">The world-space angular velocity, in radians per second.</param>
/// <param name="IsSleeping">Whether the solver currently considers the body asleep.</param>
/// <param name="IsKinematic">Whether the body is kinematic rather than dynamic.</param>
public readonly record struct UsdPhysicsBodyPose(
    UsdPhysicsObjectId Id,
    UsdVec3d Position,
    UsdPhysicsOrientation Orientation,
    UsdVec3d LinearVelocity,
    UsdVec3d AngularVelocity,
    bool IsSleeping,
    bool IsKinematic) : IUsdDetachedResult;

/// <summary>
/// Identifies which simulated domain produced one <see cref="UsdPhysicsDeformation"/> window.
/// </summary>
public enum UsdPhysicsDeformationKind
{
    /// <summary>Solid or granular particles of one particle body.</summary>
    Particles,

    /// <summary>Fluid particles of one particle body.</summary>
    Fluid,

    /// <summary>Simulated vertices of one surface deformable, which is what a cloth is.</summary>
    Surface,

    /// <summary>Simulated vertices of one volume deformable's tetrahedral simulation mesh.</summary>
    Volume
}

/// <summary>
/// Reports one deformable body's simulated vertex window for a single published frame.
/// </summary>
/// <remarks>
/// A rigid body pose cannot express a per vertex domain, so particle bodies, surface deformables, and
/// volume deformables publish one of these plus a contiguous window of
/// <see cref="UsdPhysicsFrame.DeformationVertices"/>. Windows never overlap and are always complete: a
/// body whose vertices did not fit the frame is dropped whole and reported through
/// <see cref="UsdPhysicsFrame.DeformationsTruncated"/>, so a consumer never reads half a body.
/// </remarks>
/// <param name="Id">The stable identity of the deformable body.</param>
/// <param name="Kind">The domain that produced the window.</param>
/// <param name="VertexOffset">The first vertex of this body inside the frame's vertex buffer.</param>
/// <param name="VertexCount">The number of vertices this body published.</param>
/// <param name="IsSleeping">Whether the solver currently considers the body settled.</param>
public readonly record struct UsdPhysicsDeformation(
    UsdPhysicsObjectId Id,
    UsdPhysicsDeformationKind Kind,
    int VertexOffset,
    int VertexCount,
    bool IsSleeping) : IUsdDetachedResult;

/// <summary>
/// One complete, preallocated simulation frame published by a <see cref="UsdPhysicsTransport"/>.
/// </summary>
/// <remarks>
/// <para>
/// Frames are allocated once, when the transport is built, and are then reused forever: publishing a
/// frame never allocates. A frame is only ever written by the dedicated physics worker while it is
/// not published and not leased, and is only ever read by a consumer holding a
/// <see cref="UsdPhysicsFrameLease"/>. A consumer therefore never observes a partially written frame
/// and never blocks the worker.
/// </para>
/// <para>
/// Every accessor is only valid for the lifetime of the lease that produced the frame. Copy any
/// value that must outlive the lease; do not store the frame itself.
/// </para>
/// </remarks>
public sealed class UsdPhysicsFrame
{
    private readonly UsdPhysicsBodyPose[] _bodies;
    private readonly UsdPhysicsDeformation[] _deformations;
    private readonly UsdVec3d[] _deformationVertices;
    private int _bodyCount;
    private int _deformationCount;
    private int _deformationVertexCount;

    internal UsdPhysicsFrame(int bodyCapacity)
        : this(bodyCapacity, 0, 0)
    {
    }

    internal UsdPhysicsFrame(int bodyCapacity, int deformationCapacity, int deformationVertexCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bodyCapacity);
        ArgumentOutOfRangeException.ThrowIfNegative(deformationCapacity);
        ArgumentOutOfRangeException.ThrowIfNegative(deformationVertexCapacity);
        _bodies = bodyCapacity == 0 ? [] : new UsdPhysicsBodyPose[bodyCapacity];
        _deformations = deformationCapacity == 0 ? [] : new UsdPhysicsDeformation[deformationCapacity];
        _deformationVertices =
            deformationVertexCapacity == 0 ? [] : new UsdVec3d[deformationVertexCapacity];
    }

    /// <summary>Gets the monotonic publication revision this frame was published with.</summary>
    public ulong Revision { get; internal set; }

    /// <summary>Gets the number of fixed sub-steps advanced since the last reset.</summary>
    public ulong StepIndex { get; internal set; }

    /// <summary>Gets the authored time code this frame reflects.</summary>
    public double TimeCode { get; internal set; }

    /// <summary>Gets the simulated seconds advanced since the authored start time code.</summary>
    public double SimulationSeconds { get; internal set; }

    /// <summary>Gets the number of fixed sub-steps the tick that produced this frame advanced.</summary>
    public int SubStepCount { get; internal set; }

    /// <summary>Gets the wall-clock time accepted but not yet simulated when this frame was published.</summary>
    public double BacklogSeconds { get; internal set; }

    /// <summary>Gets the number of events the retained world dropped because a bounded capacity was reached.</summary>
    public int DroppedEventCount { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the retained world reported a bounded overflow for this
    /// frame.
    /// </summary>
    public bool HasOverflow { get; internal set; }

    /// <summary>Gets the number of body poses this frame carries.</summary>
    public int BodyCount => _bodyCount;

    /// <summary>Gets the number of body poses this frame can carry.</summary>
    public int BodyCapacity => _bodies.Length;

    /// <summary>Gets the body poses this frame carries, in stable identity order.</summary>
    /// <remarks>The returned span is only valid while the lease that produced this frame is held.</remarks>
    public ReadOnlySpan<UsdPhysicsBodyPose> Bodies => _bodies.AsSpan(0, _bodyCount);

    /// <summary>Gets a value indicating whether the retained world truncated the body set.</summary>
    public bool BodiesTruncated { get; internal set; }

    /// <summary>Gets the number of deformable bodies this frame carries.</summary>
    public int DeformationCount => _deformationCount;

    /// <summary>Gets the number of deformable bodies this frame can carry.</summary>
    public int DeformationCapacity => _deformations.Length;

    /// <summary>Gets the number of simulated vertices this frame carries.</summary>
    public int DeformationVertexCount => _deformationVertexCount;

    /// <summary>Gets the number of simulated vertices this frame can carry.</summary>
    public int DeformationVertexCapacity => _deformationVertices.Length;

    /// <summary>Gets the deformable bodies this frame carries, in build order.</summary>
    /// <remarks>The returned span is only valid while the lease that produced this frame is held.</remarks>
    public ReadOnlySpan<UsdPhysicsDeformation> Deformations => _deformations.AsSpan(0, _deformationCount);

    /// <summary>Gets the simulated vertices every deformation window addresses.</summary>
    /// <remarks>The returned span is only valid while the lease that produced this frame is held.</remarks>
    public ReadOnlySpan<UsdVec3d> DeformationVertices =>
        _deformationVertices.AsSpan(0, _deformationVertexCount);

    /// <summary>Gets a value indicating whether the retained world dropped a whole deformable body.</summary>
    public bool DeformationsTruncated { get; internal set; }

    /// <summary>Gets the writable body buffer, used only by the owning physics worker.</summary>
    internal Span<UsdPhysicsBodyPose> BodyBuffer => _bodies;

    /// <summary>Gets the writable deformation buffer, used only by the owning physics worker.</summary>
    internal Span<UsdPhysicsDeformation> DeformationBuffer => _deformations;

    /// <summary>Gets the writable deformation vertex buffer, used only by the owning physics worker.</summary>
    internal Span<UsdVec3d> DeformationVertexBuffer => _deformationVertices;

    /// <summary>Gets the reference count guarding this frame's buffers.</summary>
    /// <remarks>
    /// A non-zero count means the frame is published, leased, or claimed for writing; the worker only
    /// ever writes into a frame whose count it atomically claimed from zero.
    /// </remarks>
    internal int References;

    /// <summary>Records how many body poses the worker wrote into <see cref="BodyBuffer"/>.</summary>
    internal void SetBodyCount(int count)
    {
        _bodyCount = Math.Clamp(count, 0, _bodies.Length);
        BodiesTruncated = count > _bodies.Length;
    }

    /// <summary>Records how much deformation the worker wrote into the deformation buffers.</summary>
    /// <param name="bodyCount">The number of complete deformation windows written.</param>
    /// <param name="vertexCount">The number of vertices those windows address.</param>
    /// <param name="truncated">Whether the retained world dropped a whole body.</param>
    internal void SetDeformationCounts(int bodyCount, int vertexCount, bool truncated)
    {
        _deformationCount = Math.Clamp(bodyCount, 0, _deformations.Length);
        _deformationVertexCount = Math.Clamp(vertexCount, 0, _deformationVertices.Length);
        DeformationsTruncated = truncated || bodyCount > _deformations.Length ||
            vertexCount > _deformationVertices.Length;
    }

    /// <summary>Copies every published value except the body buffer from another frame.</summary>
    internal void CopyHeaderFrom(UsdPhysicsFrame other)
    {
        Revision = other.Revision;
        StepIndex = other.StepIndex;
        TimeCode = other.TimeCode;
        SimulationSeconds = other.SimulationSeconds;
        SubStepCount = other.SubStepCount;
        BacklogSeconds = other.BacklogSeconds;
        DroppedEventCount = other.DroppedEventCount;
        HasOverflow = other.HasOverflow;
    }
}

/// <summary>
/// Grants read access to the most recently published <see cref="UsdPhysicsFrame"/>.
/// </summary>
/// <remarks>
/// <para>
/// A lease pins exactly one publication buffer. The physics worker never writes into a leased buffer,
/// so the frame a consumer reads is always complete, and acquiring a lease never blocks the worker
/// or the consumer. Dispose the lease as soon as the frame has been consumed: leases are the only
/// thing that can make the bounded publication ring run out of free buffers, which the transport
/// reports through <see cref="UsdPhysicsTransportStatus.DroppedPublications"/>.
/// </para>
/// <para>
/// A lease may be disposed from any thread, and disposing it twice is a no-op. Because a lease is a
/// value type, it may also be copied freely: every copy names the same pinned frame, and the frame is
/// released by whichever copy is disposed first. Disposing the remaining copies afterwards is a no-op,
/// so a copied lease can never release the same buffer twice or corrupt an unrelated later lease.
/// Copies are therefore aliases of one lease, not additional leases; take a second lease from
/// <see cref="UsdPhysicsTransport.TryAcquireLatestFrame"/> when two independent lifetimes are needed.
/// </para>
/// <para>
/// The default lease pins nothing: it is invalid, disposing it does nothing, and reading
/// <see cref="Frame"/> throws.
/// </para>
/// </remarks>
public struct UsdPhysicsFrameLease : IDisposable, IEquatable<UsdPhysicsFrameLease>
{
    private readonly UsdPhysicsLeaseSlot? _slot;
    private readonly long _token;

    internal UsdPhysicsFrameLease(UsdPhysicsLeaseSlot slot, long token)
    {
        _slot = slot;
        _token = token;
    }

    /// <summary>Gets the leased frame.</summary>
    /// <exception cref="ObjectDisposedException">The lease has already been disposed.</exception>
    public readonly UsdPhysicsFrame Frame =>
        ReadFrame() ?? throw new ObjectDisposedException(nameof(UsdPhysicsFrameLease));

    /// <summary>Gets a value indicating whether this lease still pins a frame.</summary>
    public readonly bool IsValid => ReadFrame() is not null;

    /// <summary>Releases the publication buffer back to the transport.</summary>
    /// <remarks>
    /// The release is a single compare-and-swap on the shared rental generation, so exactly one of the
    /// copies of a lease releases the frame no matter how many copies exist or which thread disposes
    /// them. A copy kept past that point can never release a later lease of the same slot, because the
    /// generation it carries is never handed out again.
    /// </remarks>
    public void Dispose()
    {
        UsdPhysicsLeaseSlot? slot = _slot;
        if (slot is null)
        {
            return;
        }

        // The frame must be read before the generation is advanced: once the slot is free, another
        // consumer may rent it and overwrite the frame, and this copy must not touch that rental.
        UsdPhysicsFrame? frame = Volatile.Read(ref slot.Frame);
        if (Interlocked.CompareExchange(ref slot.Token, _token + 1, _token) != _token)
        {
            return;
        }

        if (frame is not null)
        {
            UsdPhysicsFramePublisher.Release(frame);
        }
    }

    /// <inheritdoc/>
    public readonly bool Equals(UsdPhysicsFrameLease other) =>
        ReferenceEquals(_slot, other._slot) && _token == other._token;

    /// <inheritdoc/>
    public override readonly bool Equals(object? obj) => obj is UsdPhysicsFrameLease other && Equals(other);

    /// <inheritdoc/>
    public override readonly int GetHashCode() =>
        _slot is null ? 0 : HashCode.Combine(RuntimeHelpers.GetHashCode(_slot), _token);

    /// <summary>Determines whether two leases pin the same frame.</summary>
    /// <param name="left">The first lease.</param>
    /// <param name="right">The second lease.</param>
    public static bool operator ==(UsdPhysicsFrameLease left, UsdPhysicsFrameLease right) => left.Equals(right);

    /// <summary>Determines whether two leases pin different frames.</summary>
    /// <param name="left">The first lease.</param>
    /// <param name="right">The second lease.</param>
    public static bool operator !=(UsdPhysicsFrameLease left, UsdPhysicsFrameLease right) => !left.Equals(right);

    private readonly UsdPhysicsFrame? ReadFrame()
    {
        UsdPhysicsLeaseSlot? slot = _slot;
        if (slot is null)
        {
            return null;
        }

        // Reading the frame before the generation is what makes this safe: the generation only ever
        // moves forward, so an unchanged generation proves the frame just read belongs to this lease.
        UsdPhysicsFrame? frame = Volatile.Read(ref slot.Frame);
        return Volatile.Read(ref slot.Token) == _token ? frame : null;
    }
}
