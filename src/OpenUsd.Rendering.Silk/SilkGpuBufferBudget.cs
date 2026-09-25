// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd.Rendering.Silk;

/// <summary>Supports immutable admission for buffers created through the Silk RHI.</summary>
public interface ISilkBufferAdmissionDevice
{
    /// <summary>Gets the configured shared budget, or null when buffer admission is not enabled.</summary>
    SilkGpuBufferBudget? GpuBufferBudget { get; }

    /// <summary>Configures the budget before the first buffer allocation attempt.</summary>
    /// <remarks>Repeating the same object is idempotent; replacing or configuring a budget later is refused.</remarks>
    void ConfigureBufferBudget(SilkGpuBufferBudget budget);
}

/// <summary>A shared, thread-safe ceiling on logical Silk RHI buffer payload bytes.</summary>
/// <remarks>
/// Admission precedes native creation. Charges remain until native resources and all submission leases release.
/// Sharing this object across devices or renderers aggregates their buffers within one process.
/// This is not measured VRAM: textures, internal backend staging/readback buffers, descriptors, alignment,
/// driver overhead and managed/source memory are outside the metric. No implicit limit is selected.
/// </remarks>
public sealed class SilkGpuBufferBudget
{
    private readonly object _gate = new();
    private ulong _reservedBytes;
    private ulong _peakBytes;
    private ulong _reservationCount;

    /// <summary>Creates a positive immutable logical-byte ceiling.</summary>
    public SilkGpuBufferBudget(ulong maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfZero(maximumBytes);
        MaximumBytes = maximumBytes;
    }

    /// <summary>Gets the maximum concurrent buffer payload reservations.</summary>
    public ulong MaximumBytes { get; }

    /// <summary>Gets one consistent immutable accounting snapshot, including pending creations.</summary>
    public SilkGpuBufferUsage Usage
    {
        get
        {
            lock (_gate)
            {
                return new(MaximumBytes, _reservedBytes, _peakBytes, _reservationCount);
            }
        }
    }

    /// <summary>Configures a supporting device; unsupported devices are refused rather than left unbounded.</summary>
    public void ConfigureDevice(ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (device is not ISilkBufferAdmissionDevice admission)
        {
            throw new NotSupportedException("The graphics device cannot enforce shared GPU buffer admission.");
        }
        admission.ConfigureBufferBudget(this);
    }

    /// <summary>Parses a positive invariant decimal byte ceiling, or returns null when unconfigured.</summary>
    public static SilkGpuBufferBudget? FromConfiguration(string? maximumBytes)
    {
        if (maximumBytes is null)
        {
            return null;
        }
        if (!ulong.TryParse(maximumBytes, NumberStyles.None, CultureInfo.InvariantCulture, out ulong value) ||
            value == 0)
        {
            throw new ArgumentException(
                "The GPU buffer ceiling must be a positive base-10 integer.", nameof(maximumBytes));
        }
        return new SilkGpuBufferBudget(value);
    }

    internal IDisposable Reserve(ulong bytes)
    {
        ArgumentOutOfRangeException.ThrowIfZero(bytes);
        var reservation = new Reservation(this, bytes);
        lock (_gate)
        {
            if (bytes > MaximumBytes - _reservedBytes)
            {
                throw new SilkGpuBufferBudgetExceededException(bytes, _reservedBytes, MaximumBytes);
            }
            _reservedBytes = checked(_reservedBytes + bytes);
            _peakBytes = Math.Max(_peakBytes, _reservedBytes);
            _reservationCount++;
        }
        return reservation;
    }

    private void Release(ulong bytes)
    {
        lock (_gate)
        {
            _reservedBytes -= bytes;
            _reservationCount--;
        }
    }

    private sealed class Reservation(SilkGpuBufferBudget owner, ulong bytes) : IDisposable
    {
        private SilkGpuBufferBudget? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(bytes);
    }
}

/// <summary>Immutable shared buffer-payload reservation accounting, not physical VRAM measurements.</summary>
public sealed class SilkGpuBufferUsage
{
    internal SilkGpuBufferUsage(ulong maximum, ulong reserved, ulong peak, ulong count)
    {
        MaximumBytes = maximum;
        ReservedBytes = reserved;
        PeakReservedBytes = peak;
        ReservationCount = count;
    }

    /// <summary>Gets the configured logical-byte ceiling.</summary>
    public ulong MaximumBytes { get; }
    /// <summary>Gets current reservations, including pending creation and submission-held buffers.</summary>
    public ulong ReservedBytes { get; }
    /// <summary>Gets the highest simultaneous reservation total.</summary>
    public ulong PeakReservedBytes { get; }
    /// <summary>Gets the number of currently owned buffer reservations.</summary>
    public ulong ReservationCount { get; }
}

/// <summary>Reports a buffer payload request refused before native allocation.</summary>
public sealed class SilkGpuBufferBudgetExceededException : InvalidOperationException
{
    internal SilkGpuBufferBudgetExceededException(ulong requested, ulong reserved, ulong maximum)
        : base(string.Create(CultureInfo.InvariantCulture,
            $"GPU buffer payload admission refused {requested} bytes with {reserved} reserved (limit {maximum})."))
    {
        RequestedBytes = requested;
        ReservedBytes = reserved;
        MaximumBytes = maximum;
    }

    /// <summary>Gets the refused buffer's logical size.</summary>
    public ulong RequestedBytes { get; }
    /// <summary>Gets reservations already held when the request was refused.</summary>
    public ulong ReservedBytes { get; }
    /// <summary>Gets the configured logical-byte ceiling.</summary>
    public ulong MaximumBytes { get; }
}
