// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Immutable;
using System.Globalization;

namespace OpenUsd.Physics.Interop;

/// <summary>
/// Translates immutable public scene query requests into native records and detaches the hits.
/// </summary>
/// <remarks>
/// The whole batch crosses the ABI exactly once: every request is staged into one pinned array and
/// every hit is written back into one pinned array, so a batch of a thousand raycasts still costs a
/// single interop transition and no per-hit marshalling. Hits carry stable object and collider
/// identities only; no stage handle, prim path, or native pointer is ever exposed.
/// </remarks>
internal static class PhysxQueryAdapter
{
    /// <summary>Translates one public request into its native record.</summary>
    /// <param name="request">The immutable public request.</param>
    /// <param name="maxHitsPerRequest">
    /// The per request hit budget of the session, which bounds every request and supplies the budget
    /// of a request that declares none.
    /// </param>
    /// <param name="native">The translated native record; undefined when translation fails.</param>
    /// <param name="rejection">The rejection reason; <see langword="null"/> on success.</param>
    /// <returns><see langword="true"/> when the request was accepted.</returns>
    internal static bool TryTranslate(
        UsdPhysicsQueryRequest request,
        uint maxHitsPerRequest,
        out PhysxQueryRequest native,
        out string? rejection)
    {
        ArgumentNullException.ThrowIfNull(request);
        native = default;

        if (maxHitsPerRequest == 0)
        {
            rejection = "The session declares a per request hit budget of zero, so no query can report a hit.";
            return false;
        }

        PhysxQueryType type = request.Kind switch
        {
            UsdPhysicsQueryKind.Raycast => PhysxQueryType.Raycast,
            UsdPhysicsQueryKind.Sweep => PhysxQueryType.Sweep,
            UsdPhysicsQueryKind.Overlap => PhysxQueryType.Overlap,
            _ => PhysxQueryType.Count
        };
        if (type == PhysxQueryType.Count)
        {
            rejection = string.Create(
                CultureInfo.InvariantCulture,
                $"The query kind {request.Kind} is not carried by the retained world query ABI.");
            return false;
        }

        if (!IsFinite(request.Origin))
        {
            rejection = "The request declares a non finite origin.";
            return false;
        }

        bool needsDirection = type != PhysxQueryType.Overlap;
        if (needsDirection)
        {
            if (!IsFinite(request.Direction) || IsZero(request.Direction))
            {
                rejection = "The request declares a zero length or non finite direction.";
                return false;
            }
            if (!(request.MaxDistance > 0))
            {
                rejection = "The request declares a non positive maximum distance.";
                return false;
            }
        }
        else if (!IsZero(request.Direction))
        {
            rejection = "An overlap request does not read a direction.";
            return false;
        }

        bool needsRadius = type != PhysxQueryType.Raycast;
        if (needsRadius && !(request.Radius > 0))
        {
            rejection = "The request declares a non positive radius.";
            return false;
        }
        if (!needsRadius && request.Radius != 0)
        {
            rejection = "A raycast request does not read a radius.";
            return false;
        }

        if (request.ExcludeStatic && request.ExcludeDynamic)
        {
            rejection = "The request excludes both static and dynamic colliders and can never hit.";
            return false;
        }

        if (!TryResolveFilterMask(request.Filter, out uint filterMask, out rejection))
        {
            return false;
        }

        // The runtime reports one hit for an any hit query without promising which one, so a filter
        // that could discard that hit would silently turn a hit into a miss.
        if (request.AnyHit && (filterMask != 0 || request.ExcludeTriggers))
        {
            rejection = "An any hit request cannot also declare a collision group or trigger filter, "
                + "because the single hit it reports is not guaranteed to survive the filter.";
            return false;
        }

        UsdVec3d direction = needsDirection ? Normalize(request.Direction) : default;
        native = new PhysxQueryRequest
        {
            UserId = request.UserId,
            Type = (uint)type,
            Flags = (uint)MapFlags(request),
            Origin = Vector(request.Origin),
            Direction = Vector(direction),
            MaxDistance = needsDirection ? (float)request.MaxDistance : 0,
            ShapeType = (uint)PhysxShapeType.Sphere,

            // The ABI reads the rotation of the swept or overlapped shape and rejects a quaternion it
            // cannot normalize, so a shaped request always carries the identity rotation of a sphere.
            // A raycast carries no geometry at all and must leave the quaternion zeroed.
            Rotation = needsRadius ? new PhysxQuatf(0, 0, 0, 1) : default,
            Radius = (float)request.Radius,
            FilterMask = filterMask,
            MaxHits = request.MaxHits == 0
                ? maxHitsPerRequest
                : Math.Min(request.MaxHits, maxHitsPerRequest),
            SceneIndex = request.SceneIndex
        };
        rejection = null;
        return true;
    }

    /// <summary>Resolves the public include and exclude masks onto the single native filter mask.</summary>
    /// <remarks>
    /// The ABI carries one mask whose zero value means every collision group is accepted, so a public
    /// filter that accepts every group is folded onto zero and a public filter whose exclusions cancel
    /// every inclusion is rejected instead of being handed over as an accidental accept-all.
    /// </remarks>
    internal static bool TryResolveFilterMask(
        UsdPhysicsQueryFilter filter,
        out uint filterMask,
        out string? rejection)
    {
        uint effective = filter.IncludeMask & ~filter.ExcludeMask;
        if (effective == 0)
        {
            filterMask = 0;
            rejection = "The request excludes every collision group it includes and can never hit.";
            return false;
        }

        filterMask = effective == uint.MaxValue ? 0 : effective;
        rejection = null;
        return true;
    }

    /// <summary>Stages a whole request batch, reporting the first request that was rejected.</summary>
    internal static bool TryTranslateBatch(
        IReadOnlyList<UsdPhysicsQueryRequest> requests,
        uint maxHitsPerRequest,
        Span<PhysxQueryRequest> destination,
        out int acceptedCount,
        out int rejectedIndex,
        out string? rejection)
    {
        ArgumentNullException.ThrowIfNull(requests);
        acceptedCount = 0;
        rejectedIndex = -1;
        rejection = null;

        if (requests.Count > destination.Length)
        {
            rejectedIndex = destination.Length;
            rejection = string.Create(
                CultureInfo.InvariantCulture,
                $"The query batch of {requests.Count} exceeds the staged capacity of {destination.Length}.");
            return false;
        }

        for (int index = 0; index < requests.Count; index++)
        {
            if (!TryTranslate(requests[index], maxHitsPerRequest, out PhysxQueryRequest native, out rejection))
            {
                rejectedIndex = index;
                return false;
            }

            destination[index] = native;
            acceptedCount++;
        }
        return true;
    }

    /// <summary>Detaches one filled hit buffer onto one public result per staged request.</summary>
    /// <remarks>
    /// The runtime writes the hits of one request as one contiguous run, in request order, nearest
    /// first. Grouping therefore walks both arrays once, which keeps detaching linear even when a
    /// batch shares one user identifier across several requests.
    /// </remarks>
    internal static ImmutableArray<UsdPhysicsQueryResult> Detach(
        ReadOnlySpan<PhysxQueryRequest> requests,
        ReadOnlySpan<PhysxQueryHit> hits,
        uint droppedHits,
        bool droppedCountIsLowerBound = false)
    {
        if (requests.IsEmpty)
        {
            return [];
        }

        var results = ImmutableArray.CreateBuilder<UsdPhysicsQueryResult>(requests.Length);
        int cursor = 0;
        for (int index = 0; index < requests.Length; index++)
        {
            ulong userId = requests[index].UserId;
            int start = cursor;
            while (cursor < hits.Length && hits[cursor].UserId == userId)
            {
                cursor++;
            }

            // Only a single-request batch can attribute the batch level dropped count without
            // guessing, so a larger batch reports overflow on the batch instead of on one request.
            bool attributable = requests.Length == 1;
            int dropped = attributable ? (int)Math.Min(droppedHits, int.MaxValue) : 0;
            results.Add(DetachRun(hits[start..cursor], dropped, attributable && droppedCountIsLowerBound));
        }
        return results.ToImmutable();
    }

    /// <summary>Detaches one contiguous hit run onto the public result contract.</summary>
    internal static UsdPhysicsQueryResult DetachRun(
        ReadOnlySpan<PhysxQueryHit> hits,
        int droppedCount,
        bool droppedCountIsLowerBound = false)
    {
        if (hits.IsEmpty && droppedCount == 0 && !droppedCountIsLowerBound)
        {
            return UsdPhysicsQueryResult.Empty;
        }

        var entries = ImmutableArray.CreateBuilder<UsdPhysicsQueryHit>(hits.Length);
        foreach (PhysxQueryHit hit in hits)
        {
            entries.Add(Detach(hit));
        }
        return new UsdPhysicsQueryResult(entries.ToImmutable(), droppedCount, droppedCountIsLowerBound);
    }

    /// <summary>Detaches one native hit onto the public hit contract.</summary>
    internal static UsdPhysicsQueryHit Detach(in PhysxQueryHit hit)
    {
        var flags = (PhysxQueryHitFlags)hit.Flags;
        bool hasDistance = (flags & PhysxQueryHitFlags.HasDistance) != 0;
        return new UsdPhysicsQueryHit(
            new UsdPhysicsObjectId(hit.ActorId),
            (flags & PhysxQueryHitFlags.HasPosition) != 0 ? Vector(hit.Position) : default,
            (flags & PhysxQueryHitFlags.HasNormal) != 0 ? Vector(hit.Normal) : default,
            hasDistance && float.IsFinite(hit.Distance) && hit.Distance >= 0 ? hit.Distance : 0)
        {
            ColliderId = hit.ShapeId == PhysxAbi.InvalidId
                ? null
                : new UsdPhysicsObjectId(hit.ShapeId, UsdPhysicsObjectKind.Collider),
            Fields = MapHitFields(flags),
            FaceIndex = hit.FaceIndex
        };
    }

    /// <summary>Maps native hit flags onto the public hit field set.</summary>
    internal static UsdPhysicsQueryHitFields MapHitFields(PhysxQueryHitFlags flags)
    {
        UsdPhysicsQueryHitFields fields = UsdPhysicsQueryHitFields.None;
        if ((flags & PhysxQueryHitFlags.HasPosition) != 0)
        {
            fields |= UsdPhysicsQueryHitFields.Position;
        }
        if ((flags & PhysxQueryHitFlags.HasNormal) != 0)
        {
            fields |= UsdPhysicsQueryHitFields.Normal;
        }
        if ((flags & PhysxQueryHitFlags.HasDistance) != 0)
        {
            fields |= UsdPhysicsQueryHitFields.Distance;
        }
        if ((flags & PhysxQueryHitFlags.HasFace) != 0)
        {
            fields |= UsdPhysicsQueryHitFields.FaceIndex;
        }
        if ((flags & PhysxQueryHitFlags.InitialOverlap) != 0)
        {
            fields |= UsdPhysicsQueryHitFields.InitialOverlap;
        }
        if ((flags & PhysxQueryHitFlags.Trigger) != 0)
        {
            fields |= UsdPhysicsQueryHitFields.Trigger;
        }
        return fields;
    }

    private static PhysxQueryFlags MapFlags(UsdPhysicsQueryRequest request)
    {
        PhysxQueryFlags flags = PhysxQueryFlags.None;
        if (request.AnyHit)
        {
            flags |= PhysxQueryFlags.AnyHit;
        }
        if (request.ExcludeStatic)
        {
            flags |= PhysxQueryFlags.ExcludeStatic;
        }
        if (request.ExcludeDynamic)
        {
            flags |= PhysxQueryFlags.ExcludeDynamic;
        }
        if (request.ExcludeTriggers)
        {
            flags |= PhysxQueryFlags.ExcludeTriggers;
        }
        if (request.ReportInitialOverlap && request.Kind == UsdPhysicsQueryKind.Sweep)
        {
            flags |= PhysxQueryFlags.SweepInitialOverlap;
        }
        return flags;
    }

    private static UsdVec3d Normalize(UsdVec3d value)
    {
        double length = Math.Sqrt((value.X * value.X) + (value.Y * value.Y) + (value.Z * value.Z));
        return length > 0 ? new UsdVec3d(value.X / length, value.Y / length, value.Z / length) : default;
    }

    private static bool IsFinite(UsdVec3d value) =>
        double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);

    private static bool IsZero(UsdVec3d value) => value.X == 0 && value.Y == 0 && value.Z == 0;

    private static PhysxVec3f Vector(UsdVec3d value) => new((float)value.X, (float)value.Y, (float)value.Z);

    private static UsdVec3d Vector(in PhysxVec3f value) => new(value.X, value.Y, value.Z);
}
