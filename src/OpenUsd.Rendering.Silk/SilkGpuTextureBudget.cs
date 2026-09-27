// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd.Rendering.Silk;

/// <summary>Supports immutable admission for textures created through the Silk RHI.</summary>
public interface ISilkTextureAdmissionDevice
{
    /// <summary>Gets the shared texture budget, or null when texture admission is disabled.</summary>
    SilkGpuTextureBudget? GpuTextureBudget { get; }

    /// <summary>Configures the budget before the first owned texture allocation attempt.</summary>
    void ConfigureTextureBudget(SilkGpuTextureBudget budget);
}

/// <summary>A shared, thread-safe ceiling on logical owned RHI texture payload bytes.</summary>
/// <remarks>
/// Includes all declared mip levels and volume slices, with one charge per native texture owner.
/// Charges survive public disposal while submission leases retain native ownership.
/// Imported images, decoded pixels, upload/readback buffers, descriptors, alignment and driver
/// overhead are excluded. This is neither physical VRAM nor total process memory.
/// </remarks>
public sealed class SilkGpuTextureBudget
{
    private readonly SilkByteReservationPool _pool;

    /// <summary>Creates a positive immutable logical texture-byte ceiling.</summary>
    public SilkGpuTextureBudget(ulong maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfZero(maximumBytes);
        MaximumBytes = maximumBytes;
        _pool = new(maximumBytes, static (requested, reserved, maximum) =>
            new SilkGpuTextureBudgetExceededException(requested, reserved, maximum));
    }

    /// <summary>Gets the maximum concurrent texture-payload reservations.</summary>
    public ulong MaximumBytes { get; }

    /// <summary>Gets one consistent snapshot including pending creations and submission-held textures.</summary>
    public SilkGpuTextureUsage Usage
    {
        get
        {
            (ulong reserved, ulong peak, ulong count) = _pool.Snapshot;
            return new(MaximumBytes, reserved, peak, count);
        }
    }

    /// <summary>Configures a supporting device; unsupported devices fail explicitly.</summary>
    public void ConfigureDevice(ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (device is not ISilkTextureAdmissionDevice admission)
        {
            throw new NotSupportedException("The graphics device cannot enforce shared GPU texture admission.");
        }
        admission.ConfigureTextureBudget(this);
    }

    internal IDisposable Reserve(SilkTextureDescriptor descriptor, uint depth)
    {
        descriptor.Validate();
        ArgumentOutOfRangeException.ThrowIfZero(depth);
        ulong bytes = checked(SilkMipChainLayout.GetLogicalByteSize(
            descriptor.Width, descriptor.Height, descriptor.Format, descriptor.MipLevelCount) * depth);
        return _pool.Reserve(bytes);
    }
}

/// <summary>Immutable texture-payload accounting, not physical VRAM measurements.</summary>
public sealed class SilkGpuTextureUsage
{
    internal SilkGpuTextureUsage(ulong maximum, ulong reserved, ulong peak, ulong count)
    {
        MaximumBytes = maximum;
        ReservedBytes = reserved;
        PeakReservedBytes = peak;
        ReservationCount = count;
    }

    /// <summary>Gets the configured logical-byte ceiling.</summary>
    public ulong MaximumBytes { get; }
    /// <summary>Gets current pending and owned texture reservations.</summary>
    public ulong ReservedBytes { get; }
    /// <summary>Gets the highest simultaneous reservation total.</summary>
    public ulong PeakReservedBytes { get; }
    /// <summary>Gets the number of current texture reservations.</summary>
    public ulong ReservationCount { get; }
}

/// <summary>Reports a texture-payload request refused before native allocation.</summary>
public sealed class SilkGpuTextureBudgetExceededException : InvalidOperationException
{
    internal SilkGpuTextureBudgetExceededException(ulong requested, ulong reserved, ulong maximum)
        : base(string.Create(CultureInfo.InvariantCulture,
            $"GPU texture payload admission refused {requested} bytes with {reserved} reserved (limit {maximum})."))
    {
        RequestedBytes = requested;
        ReservedBytes = reserved;
        MaximumBytes = maximum;
    }

    /// <summary>Gets the refused texture's logical size including mips and slices.</summary>
    public ulong RequestedBytes { get; }
    /// <summary>Gets reservations already held when the request was refused.</summary>
    public ulong ReservedBytes { get; }
    /// <summary>Gets the configured logical-byte ceiling.</summary>
    public ulong MaximumBytes { get; }
}
