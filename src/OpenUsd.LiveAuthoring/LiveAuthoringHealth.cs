// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.LiveAuthoring;

/// <summary>Identifies the lifecycle stage reported by a <see cref="LiveAuthoringHealthEvent"/>.</summary>
public enum LiveAuthoringHealthEventKind
{
    /// <summary>A batch was admitted into the bounded queue.</summary>
    Admitted,

    /// <summary>A pending batch was superseded by tail coalescing.</summary>
    Coalesced,

    /// <summary>A batch was rejected before admission, for example a non-increasing sequence.</summary>
    Rejected,

    /// <summary>An admitted batch finished applying successfully.</summary>
    Applied,

    /// <summary>An admitted batch failed while applying.</summary>
    Failed,

    /// <summary>The sink finished disposal and drained its queue.</summary>
    Disposed
}

/// <summary>
/// A bounded, structured health notification. Every field is a fixed-size value or a length-capped
/// string, so implementations may forward instances to an external health endpoint without additional
/// bounding.
/// </summary>
public readonly record struct LiveAuthoringHealthEvent(
    LiveAuthoringHealthEventKind Kind,
    long Sequence,
    string? CorrelationId,
    string? OriginId,
    DateTimeOffset TimestampUtc,
    int PendingBatchCount,
    string? Detail);

/// <summary>
/// A bounded, point-in-time snapshot of queue admission and execution health, suitable for polling from
/// an external health endpoint. <see cref="HealthObserverFailureCount"/> and
/// <see cref="LastHealthObserverFailureDetail"/> report exceptions thrown by a caller-supplied
/// <see cref="IProgress{T}"/> health observer while reporting a <see cref="LiveAuthoringHealthEvent"/>.
/// Every such exception is caught and isolated here rather than propagated to
/// <see cref="ILiveAuthoringSink.ApplyAsync"/> or batch execution: a broken observer must never change
/// admission or applied-result semantics for a well-behaved caller.
/// </summary>
public readonly record struct LiveAuthoringHealthSnapshot(
    int Capacity,
    int PendingBatchCount,
    int PeakPendingBatchCount,
    long CoalescedBatchCount,
    bool IsAccepting,
    long LastAdmittedSequence,
    long? LastAppliedSequence,
    long? LastFailedSequence,
    string? LastFailureDetail,
    DateTimeOffset TimestampUtc,
    long HealthObserverFailureCount = 0,
    string? LastHealthObserverFailureDetail = null);
