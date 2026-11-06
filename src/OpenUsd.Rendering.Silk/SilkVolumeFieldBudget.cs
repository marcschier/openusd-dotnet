// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>Shares a logical decoded-volume field ceiling across hdSilk sessions.</summary>
/// <remarks>
/// Reservations cover private decoded-field bytes until the last session, page, replay or
/// scene lease releases them. Hio's earlier decode allocations, filesystem cache, allocator
/// overhead, host memory and GPU memory are outside this metric. No limit is enabled by default.
/// Disposing this handle does not invalidate configured sessions or their retained fields.
/// </remarks>
public sealed class SilkVolumeFieldBudget : IDisposable
{
    private readonly OpenUsdSilkRuntime.VolumeBudgetHandle _handle;

    /// <summary>Creates a positive immutable ceiling for concurrent decoded-field bytes.</summary>
    public SilkVolumeFieldBudget(ulong maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfZero(maximumBytes);
        MaximumBytes = maximumBytes;
        _handle = OpenUsdSilkRuntime.CreateVolumeBudget(maximumBytes);
    }

    /// <summary>Gets the logical byte ceiling.</summary>
    public ulong MaximumBytes { get; }

    /// <summary>Gets one consistent snapshot, including reservations for fields being stored.</summary>
    public SilkVolumeFieldUsage Usage => OpenUsdSilkRuntime.GetVolumeBudgetUsage(_handle, MaximumBytes);

    internal void Configure(nint session) => OpenUsdSilkRuntime.SetVolumeBudget(session, _handle);

    /// <inheritdoc/>
    public void Dispose() => _handle.Dispose();
}

/// <summary>Immutable logical decoded-field reservation accounting.</summary>
public sealed class SilkVolumeFieldUsage
{
    internal SilkVolumeFieldUsage(ulong maximum, ulong reserved, ulong peak, ulong count)
    {
        MaximumBytes = maximum;
        ReservedBytes = reserved;
        PeakReservedBytes = peak;
        ReservationCount = count;
    }

    /// <summary>Gets the configured logical-byte ceiling.</summary>
    public ulong MaximumBytes { get; }
    /// <summary>Gets the currently reserved decoded-field bytes.</summary>
    public ulong ReservedBytes { get; }
    /// <summary>Gets the highest simultaneous reservation total.</summary>
    public ulong PeakReservedBytes { get; }
    /// <summary>Gets the number of fields with live reservations.</summary>
    public ulong ReservationCount { get; }
}
