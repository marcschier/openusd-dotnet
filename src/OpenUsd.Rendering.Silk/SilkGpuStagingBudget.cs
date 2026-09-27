// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd.Rendering.Silk;

/// <summary>Supports immutable admission for backend-owned GPU transfer buffers.</summary>
public interface ISilkStagingAdmissionDevice
{
    /// <summary>Gets the shared staging budget, or null when staging admission is disabled.</summary>
    SilkGpuStagingBudget? GpuStagingBudget { get; }

    /// <summary>Configures admission before the first internal transfer-buffer allocation attempt.</summary>
    void ConfigureStagingBudget(SilkGpuStagingBudget budget);
}

/// <summary>A shared ceiling on backend-owned upload and readback buffer payload bytes.</summary>
/// <remarks>
/// Charges include required transfer row and slice padding, but not native allocation alignment,
/// descriptors or driver overhead. CPU command copies and decoded pixels are excluded.
/// This pool is independent of the public RHI buffer and owned-texture pools.
/// </remarks>
public sealed class SilkGpuStagingBudget
{
    private readonly SilkByteReservationPool _pool;

    /// <summary>Creates a positive immutable staging-payload ceiling.</summary>
    public SilkGpuStagingBudget(ulong maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfZero(maximumBytes);
        MaximumBytes = maximumBytes;
        _pool = new(maximumBytes, static (requested, reserved, maximum) =>
            new SilkGpuStagingBudgetExceededException(requested, reserved, maximum));
    }

    /// <summary>Gets the maximum simultaneous staging-payload reservations.</summary>
    public ulong MaximumBytes { get; }

    /// <summary>Gets one immutable snapshot of pending and native-owned staging reservations.</summary>
    public SilkGpuBufferUsage Usage
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
        if (device is not ISilkStagingAdmissionDevice admission)
        {
            throw new NotSupportedException("The graphics device cannot enforce shared GPU staging admission.");
        }
        admission.ConfigureStagingBudget(this);
    }

    internal IDisposable Reserve(ulong bytes) => _pool.Reserve(bytes);
}

/// <summary>Reports a backend transfer-buffer request refused before native allocation.</summary>
public sealed class SilkGpuStagingBudgetExceededException : InvalidOperationException
{
    internal SilkGpuStagingBudgetExceededException(ulong requested, ulong reserved, ulong maximum)
        : base(string.Create(CultureInfo.InvariantCulture,
            $"GPU staging payload admission refused {requested} bytes with {reserved} reserved (limit {maximum})."))
    {
        RequestedBytes = requested;
        ReservedBytes = reserved;
        MaximumBytes = maximum;
    }

    /// <summary>Gets the refused transfer-buffer size including its required row/slice padding.</summary>
    public ulong RequestedBytes { get; }
    /// <summary>Gets reservations already held when the request was refused.</summary>
    public ulong ReservedBytes { get; }
    /// <summary>Gets the configured staging-byte ceiling.</summary>
    public ulong MaximumBytes { get; }
}
