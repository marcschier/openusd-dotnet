// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OpenUsd.Physics.Interop;

/// <summary>
/// Carries one fully detached copy of a query batch result.
/// </summary>
internal sealed record PhysxQueryCapture(
    ImmutableArray<PhysxQueryHit> Hits,
    uint DroppedHits,
    uint RejectedRequests,
    PhysxOverflowFlags Flags)
{
    /// <summary>Gets an empty capture.</summary>
    internal static PhysxQueryCapture Empty { get; } = new([], 0, 0, PhysxOverflowFlags.None);

    /// <summary>Gets a value indicating whether hits were dropped.</summary>
    internal bool IsOverflowed =>
        (Flags & (PhysxOverflowFlags.QueryHits | PhysxOverflowFlags.QueryTruncated)) != 0 ||
        DroppedHits != 0;

    /// <summary>
    /// Gets a value indicating whether <see cref="DroppedHits"/> is only a lower bound because the
    /// simulation backend discarded hits before the runtime could count them.
    /// </summary>
    internal bool DroppedCountIsLowerBound => (Flags & PhysxOverflowFlags.QueryTruncated) != 0;

    /// <summary>Projects the hits of one request onto the public query result contract.</summary>
    /// <remarks>
    /// This scans the whole capture for one request. Prefer <see cref="ToResults"/> when the results
    /// of a whole batch are needed, because that walks requests and hits exactly once.
    /// </remarks>
    internal UsdPhysicsQueryResult ToResult(ulong userId)
    {
        int start = -1;
        int end = 0;
        for (int index = 0; index < Hits.Length; index++)
        {
            if (Hits[index].UserId != userId)
            {
                if (start >= 0)
                {
                    break;
                }
                continue;
            }

            if (start < 0)
            {
                start = index;
            }
            end = index + 1;
        }

        return start < 0
            ? (IsOverflowed
                ? new UsdPhysicsQueryResult(
                    [],
                    (int)Math.Min(DroppedHits, int.MaxValue),
                    DroppedCountIsLowerBound)
                : UsdPhysicsQueryResult.Empty)
            : PhysxQueryAdapter.DetachRun(
                Hits.AsSpan()[start..end],
                (int)Math.Min(DroppedHits, int.MaxValue),
                DroppedCountIsLowerBound);
    }

    /// <summary>Projects a whole staged batch onto one public result per request, in request order.</summary>
    internal ImmutableArray<UsdPhysicsQueryResult> ToResults(ReadOnlySpan<PhysxQueryRequest> requests) =>
        PhysxQueryAdapter.Detach(requests, Hits.AsSpan(), DroppedHits, DroppedCountIsLowerBound);
}

/// <summary>
/// Owns the caller-allocated, fixed-capacity request and hit buffers of one query batch.
/// </summary>
/// <remarks>
/// Requests are staged in a pinned buffer and submitted as one batch, so a scene query never costs
/// one interop transition per request. Hits are written into a pinned buffer whose capacity is fixed
/// at construction; anything beyond it is counted, never allocated, and reported as bounded overflow.
/// </remarks>
internal sealed unsafe class PhysxQueryBuffers : IDisposable
{
    private readonly PhysxQueryRequest[] _requests;
    private readonly PhysxQueryHit[] _hits;
    private readonly PhysxQueryRequest* _requestPointer;
    private readonly PhysxQueryHit* _hitPointer;
    private int _requestCount;
    private bool _disposed;

    /// <summary>Allocates query buffers with fixed capacities.</summary>
    internal PhysxQueryBuffers(uint requestCapacity, uint hitCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(requestCapacity, PhysxAbi.MaxResultCapacity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hitCapacity, PhysxAbi.MaxResultCapacity);
        _requests = Allocate<PhysxQueryRequest>(requestCapacity, out _requestPointer);
        _hits = Allocate<PhysxQueryHit>(hitCapacity, out _hitPointer);
    }

    /// <summary>Gets the number of request slots.</summary>
    internal int RequestCapacity => _requests.Length;

    /// <summary>Gets the number of hit slots.</summary>
    internal int HitCapacity => _hits.Length;

    /// <summary>Gets the number of staged requests.</summary>
    internal int RequestCount => _requestCount;

    /// <summary>Gets the staged requests in submission order.</summary>
    /// <remarks>The span is only valid while this instance is alive and not disposed.</remarks>
    internal ReadOnlySpan<PhysxQueryRequest> StagedRequests => _requests.AsSpan(0, _requestCount);

    /// <summary>Removes every staged request.</summary>
    internal void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _requests.AsSpan(0, _requestCount).Clear();
        _requestCount = 0;
    }

    /// <summary>Stages one request; returns <see langword="false"/> when the batch is full.</summary>
    internal bool TryAddRequest(in PhysxQueryRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_requestCount >= _requests.Length)
        {
            return false;
        }

        _requests[_requestCount++] = request;
        return true;
    }

    /// <summary>Creates the query description the runtime reads.</summary>
    /// <remarks>The returned description is only valid while this instance is alive and not disposed.</remarks>
    internal PhysxQueryDesc CreateDesc()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new PhysxQueryDesc
        {
            StructSize = (uint)Unsafe.SizeOf<PhysxQueryDesc>(),
            AbiVersion = PhysxAbi.Version,
            Requests = _requestCount == 0 ? null : _requestPointer,
            RequestCount = (nuint)_requestCount,
            Hits = _hitPointer,
            HitCapacity = (nuint)_hits.Length
        };
    }

    /// <summary>Copies the filled hit buffer into immutable managed memory.</summary>
    internal PhysxQueryCapture Capture(in PhysxQueryResultInfo info)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int count = (int)Math.Min((ulong)info.HitCount, (ulong)_hits.Length);
        return new PhysxQueryCapture(
            ImmutableArray.Create(_hits, 0, count),
            (uint)Math.Min((ulong)info.DroppedHitCount, uint.MaxValue),
            (uint)Math.Min((ulong)info.RejectedRequestCount, uint.MaxValue),
            (PhysxOverflowFlags)info.OverflowFlags);
    }

    /// <inheritdoc/>
    public void Dispose() => _disposed = true;

    private static T[] Allocate<T>(uint capacity, out T* pointer)
        where T : unmanaged
    {
        if (capacity == 0)
        {
            pointer = null;
            return [];
        }

        T[] array = GC.AllocateArray<T>((int)capacity, pinned: true);
        pointer = (T*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(array));
        return array;
    }
}
