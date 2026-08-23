// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics;

/// <summary>
/// Identifies the lifecycle state of a <see cref="UsdPhysicsTransport"/>.
/// </summary>
public enum UsdPhysicsTransportState
{
    /// <summary>The transport has not completed its initial build.</summary>
    Unbuilt,

    /// <summary>A build, reset, or seek is currently reconstructing or replaying the world.</summary>
    Building,

    /// <summary>The world is built and holding its current time; no fixed step is advancing.</summary>
    Paused,

    /// <summary>The fixed simulation step is advancing on the dedicated physics worker.</summary>
    Playing,

    /// <summary>Playback reached the authored end time code with looping disabled.</summary>
    Ended,

    /// <summary>
    /// A physics-relevant edit invalidated the built world; <see cref="UsdPhysicsTransport.ResetAsync"/>
    /// is required before playback resumes.
    /// </summary>
    Invalidated,

    /// <summary>An operation failed and left the world unusable; a reset is required.</summary>
    Faulted,

    /// <summary>The transport has been disposed and every retained resource has been released.</summary>
    Disposed
}

/// <summary>
/// Identifies why a <see cref="UsdPhysicsTransport"/> world was invalidated.
/// </summary>
/// <remarks>
/// Invalidation is a contract-level hook today: retained stage extraction does not exist yet, so
/// nothing classifies authored edits automatically. A host that already knows an edit is
/// physics-relevant calls <see cref="UsdPhysicsTransport.InvalidateAsync"/> with the matching reason,
/// and the transport reacts exactly as an automatic classifier later will: it pauses, marks the world
/// <see cref="UsdPhysicsTransportState.Invalidated"/>, and drops every retained checkpoint.
/// </remarks>
public enum UsdPhysicsInvalidationReason
{
    /// <summary>An authored physics attribute, relationship, or schema application changed.</summary>
    PhysicsEdit,

    /// <summary>The authored playback range or time-code rate changed.</summary>
    TimelineEdit,

    /// <summary>Stage composition changed in a way that invalidates every extracted identity.</summary>
    StageComposition,

    /// <summary>The host invalidated the world for a reason outside these categories.</summary>
    External
}

/// <summary>
/// Reports one immutable, atomically published snapshot of transport progress.
/// </summary>
/// <remarks>
/// Every field is captured together on the physics worker thread, so a reader never observes a
/// step index from one tick alongside a backlog from another.
/// </remarks>
/// <param name="State">The lifecycle state observed when this status was published.</param>
/// <param name="Revision">The monotonic publication revision.</param>
/// <param name="StepIndex">The number of fixed sub-steps advanced since the last reset.</param>
/// <param name="TimeCode">The authored time code the world currently holds.</param>
/// <param name="SimulationSeconds">Simulated seconds advanced since the authored start.</param>
/// <param name="BacklogSeconds">
/// Wall-clock time accepted but not yet simulated. A non-zero backlog means playback is running
/// slower than real time; no accepted time is ever discarded, so physics is never skipped.
/// </param>
/// <param name="CatchUpLimitedTicks">
/// The number of ticks whose catch-up was limited by <see cref="UsdPhysicsTransportOptions.MaxCatchUpSubSteps"/>.
/// </param>
/// <param name="DroppedPublications">
/// The number of completed frames that were simulated but not published because every publication
/// buffer was still leased by a consumer. Publication is bounded and latest-wins; dropping a
/// publication never drops simulated time.
/// </param>
/// <param name="LoopCount">
/// The number of times playback wrapped from the authored end back to the authored start.
/// </param>
/// <param name="QueueDepth">The number of pending requests in the bounded worker queue.</param>
public readonly record struct UsdPhysicsTransportStatus(
    UsdPhysicsTransportState State,
    ulong Revision,
    ulong StepIndex,
    double TimeCode,
    double SimulationSeconds,
    double BacklogSeconds,
    long CatchUpLimitedTicks,
    long DroppedPublications,
    long LoopCount,
    int QueueDepth);

/// <summary>
/// Configures the fixed-step transport, its bounded worker queue, and its looping behavior.
/// </summary>
/// <remarks>
/// Simulation capacities, the fixed-frequency override, the per-tick sub-step limit, and the
/// checkpoint policy are all carried by <see cref="Session"/> so a transport and a
/// <see cref="UsdPhysicsSession"/> built from the same stage agree on every bound.
/// </remarks>
public sealed record UsdPhysicsTransportOptions
{
    /// <summary>The hard upper bound on catch-up sub-steps advanced by one tick.</summary>
    /// <remarks>
    /// A tick never advances more than this many fixed sub-steps regardless of how far behind
    /// playback is. Exceeding the bound slows playback down; it never skips simulated time.
    /// </remarks>
    public const int MaxCatchUpSubStepLimit = 8;

    /// <summary>The number of publication buffers a transport retains.</summary>
    public const int PublicationBufferCount = 3;

    /// <summary>The default number of frame leases that may be live at the same time.</summary>
    public const int DefaultMaxConcurrentFrameLeases = 64;

    /// <summary>The hard upper bound on <see cref="MaxConcurrentFrameLeases"/>.</summary>
    public const int MaxConcurrentFrameLeaseLimit = 4096;

    /// <summary>Gets the default transport options.</summary>
    public static UsdPhysicsTransportOptions Default { get; } = new();

    /// <summary>Initializes validated transport options.</summary>
    /// <param name="session">
    /// Simulation capacities and limits; defaults to
    /// <see cref="UsdPhysicsSessionOptions.Default"/>.
    /// </param>
    /// <param name="loop">Whether playback wraps from the authored end back to the authored start.</param>
    /// <param name="requestQueueCapacity">The bounded worker request queue capacity; must be positive.</param>
    /// <param name="tickIntervalMilliseconds">
    /// The interval the dedicated worker waits between ticks when no request arrives. It bounds
    /// scheduling latency only; the simulated step size is always <see cref="UsdPhysicsFixedStep"/>.
    /// </param>
    /// <param name="maxConcurrentFrameLeases">
    /// The number of <see cref="UsdPhysicsFrameLease"/> instances that may be live at the same time;
    /// must be positive and at most <see cref="MaxConcurrentFrameLeaseLimit"/>.
    /// </param>
    public UsdPhysicsTransportOptions(
        UsdPhysicsSessionOptions? session = null,
        bool loop = false,
        int requestQueueCapacity = 64,
        int tickIntervalMilliseconds = 1,
        int maxConcurrentFrameLeases = DefaultMaxConcurrentFrameLeases)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestQueueCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tickIntervalMilliseconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tickIntervalMilliseconds, 1000);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrentFrameLeases);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            maxConcurrentFrameLeases,
            MaxConcurrentFrameLeaseLimit);

        Session = session ?? UsdPhysicsSessionOptions.Default;
        Loop = loop;
        RequestQueueCapacity = requestQueueCapacity;
        TickIntervalMilliseconds = tickIntervalMilliseconds;
        MaxConcurrentFrameLeases = maxConcurrentFrameLeases;
    }

    /// <summary>Gets the session options carrying every simulation capacity and limit.</summary>
    public UsdPhysicsSessionOptions Session { get; }

    /// <summary>Gets a value indicating whether playback wraps at the authored end time code.</summary>
    public bool Loop { get; }

    /// <summary>Gets the bounded worker request queue capacity.</summary>
    public int RequestQueueCapacity { get; }

    /// <summary>Gets the dedicated worker's idle tick interval, in milliseconds.</summary>
    public int TickIntervalMilliseconds { get; }

    /// <summary>Gets the number of frame leases that may be live at the same time.</summary>
    /// <remarks>
    /// Lease state is preallocated so that acquiring and releasing a frame never allocates. The pool
    /// is therefore bounded: while every slot is held by an undisposed lease,
    /// <see cref="UsdPhysicsTransport.TryAcquireLatestFrame"/> refuses instead of allocating, which
    /// surfaces a leaked lease immediately rather than as unbounded growth. Copies of one lease share
    /// a single slot, so only distinct acquisitions count against this bound.
    /// </remarks>
    public int MaxConcurrentFrameLeases { get; }

    /// <summary>Gets the effective per-tick catch-up sub-step bound.</summary>
    /// <remarks>
    /// This is the smaller of <see cref="UsdPhysicsSessionOptions.MaxSubStepsPerTick"/> and
    /// <see cref="MaxCatchUpSubStepLimit"/>, so configuring a larger session bound never lets one
    /// tick monopolize the worker.
    /// </remarks>
    public int MaxCatchUpSubSteps => Math.Min(Session.MaxSubStepsPerTick, MaxCatchUpSubStepLimit);
}

/// <summary>
/// Reports a <see cref="UsdPhysicsTransport"/> request rejected because its bounded worker queue is full.
/// </summary>
/// <remarks>
/// The queue is a hard bound, exactly like every simulation capacity: a saturated transport rejects
/// new requests immediately instead of growing without limit or blocking the caller.
/// </remarks>
public sealed class UsdPhysicsTransportQueueFullException : InvalidOperationException
{
    /// <summary>Identifies bounded transport queue overflow.</summary>
    public const string ErrorCode = "OPENUSD_PHYSICS_TRANSPORT_QUEUE_FULL";

    internal UsdPhysicsTransportQueueFullException(int capacity)
        : base(
            $"The UsdPhysicsTransport request queue is full at its bounded capacity of {capacity} " +
            "requests; retry after the physics worker drains pending work.")
    {
        Capacity = capacity;
    }

    /// <summary>Gets the stable queue-overflow error code.</summary>
    public string Code { get; } = ErrorCode;

    /// <summary>Gets the bounded queue capacity that was reached.</summary>
    public int Capacity { get; }
}

/// <summary>
/// Reports a <see cref="UsdPhysicsTransport"/> operation that is invalid for its current state.
/// </summary>
public sealed class UsdPhysicsTransportStateException : InvalidOperationException
{
    /// <summary>Identifies transport lifecycle-state contract violations.</summary>
    public const string ErrorCode = "OPENUSD_PHYSICS_TRANSPORT_INVALID_STATE";

    internal UsdPhysicsTransportStateException(UsdPhysicsTransportState state)
        : base($"The UsdPhysicsTransport operation is not valid while the transport state is '{state}'.")
    {
        State = state;
    }

    /// <summary>Gets the stable transport lifecycle-state error code.</summary>
    public string Code { get; } = ErrorCode;

    /// <summary>Gets the transport state observed when the operation was rejected.</summary>
    public UsdPhysicsTransportState State { get; }
}
