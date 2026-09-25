// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>Native logical buffer reservations for one successfully published coarse-mesh request.</summary>
/// <remarks>
/// Reservations include worst-case preparation transients and live old/new mesh owners. A producer
/// and its shared record hold one lease. These are conservative charges, not actual allocated or
/// process bytes; SDK/source storage, allocator overhead, materials, instance metadata, page copies
/// and GPU resources are outside the metric.
/// </remarks>
public sealed class SilkMeshPreparationUsage
{
    internal SilkMeshPreparationUsage(ulong maximumReservedBytes, ulong reservedBytes, ulong peakReservedBytes)
    {
        MaximumReservedBytes = maximumReservedBytes;
        ReservedBytes = reservedBytes;
        PeakReservedBytes = peakReservedBytes;
    }

    /// <summary>Gets the caller's reservation ceiling.</summary>
    public ulong MaximumReservedBytes { get; }

    /// <summary>Gets reservations retained by mesh producers and records after this sync.</summary>
    public ulong ReservedBytes { get; }

    /// <summary>Gets the greatest concurrent reservation charge during this sync.</summary>
    public ulong PeakReservedBytes { get; }
}
