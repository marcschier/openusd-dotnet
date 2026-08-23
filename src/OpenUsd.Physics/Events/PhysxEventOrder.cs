// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics.Interop;

/// <summary>
/// Mirrors the deterministic total order the native runtime imposes on events and query hits.
/// </summary>
/// <remarks>
/// The native runtime already emits both sections in this order; the managed mirror exists so the
/// contract can be asserted without a native runtime, and so a detached snapshot can be re-sorted
/// after it has been merged with another snapshot. The order is total: no two distinct records of
/// one step compare equal, so it is stable regardless of worker thread count, solver iteration
/// order, or how many records were dropped by a bounded capacity.
/// </remarks>
internal static class PhysxEventOrder
{
    /// <summary>Compares two event records by step index, type, and then every identity.</summary>
    internal static int Compare(in PhysxEventRecord left, in PhysxEventRecord right)
    {
        int order = left.StepIndex.CompareTo(right.StepIndex);
        if (order != 0)
        {
            return order;
        }

        order = left.Type.CompareTo(right.Type);
        if (order != 0)
        {
            return order;
        }

        order = left.Id0.CompareTo(right.Id0);
        if (order != 0)
        {
            return order;
        }

        order = left.Id1.CompareTo(right.Id1);
        if (order != 0)
        {
            return order;
        }

        order = left.Detail0.CompareTo(right.Detail0);
        return order != 0 ? order : left.Detail1.CompareTo(right.Detail1);
    }

    /// <summary>Compares two query hits nearest first, then by every identity.</summary>
    /// <remarks>
    /// A non-finite distance orders last so a degenerate hit can never displace a real nearer hit
    /// from the retained prefix.
    /// </remarks>
    internal static int CompareHits(in PhysxQueryHit left, in PhysxQueryHit right)
    {
        int order = OrderableDistance(left.Distance).CompareTo(OrderableDistance(right.Distance));
        if (order != 0)
        {
            return order;
        }

        order = left.ActorId.CompareTo(right.ActorId);
        if (order != 0)
        {
            return order;
        }

        order = left.ShapeId.CompareTo(right.ShapeId);
        return order != 0 ? order : left.FaceIndex.CompareTo(right.FaceIndex);
    }

    /// <summary>Determines whether a retained event prefix is in deterministic order.</summary>
    internal static bool IsOrdered(ReadOnlySpan<PhysxEventRecord> records)
    {
        for (int index = 1; index < records.Length; index++)
        {
            if (Compare(records[index - 1], records[index]) >= 0)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Determines whether a retained hit run is in deterministic order.</summary>
    internal static bool AreHitsOrdered(ReadOnlySpan<PhysxQueryHit> hits)
    {
        for (int index = 1; index < hits.Length; index++)
        {
            if (CompareHits(hits[index - 1], hits[index]) > 0)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Restores the deterministic order of an event buffer in place.</summary>
    internal static void Sort(Span<PhysxEventRecord> records) =>
        records.Sort(static (left, right) => Compare(left, right));

    private static float OrderableDistance(float distance) =>
        float.IsNaN(distance) ? float.PositiveInfinity : distance;
}
