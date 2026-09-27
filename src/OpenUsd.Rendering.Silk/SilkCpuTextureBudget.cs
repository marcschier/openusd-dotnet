// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd.Rendering.Silk;

/// <summary>Supports shared managed pixel and command-copy admission.</summary>
public interface ISilkCpuTextureAdmissionDevice
{
    /// <summary>Gets the configured managed pixel budget.</summary>
    SilkCpuTextureBudget? CpuTextureBudget { get; }
    /// <summary>Configures the budget before scene-resource construction or command-data allocation.</summary>
    void ConfigureCpuTextureBudget(SilkCpuTextureBudget budget);
}

/// <summary>A shared logical ceiling for owned managed material-pixel and transfer-copy buffers.</summary>
/// <remarks>
/// Admission precedes allocation and tracks ownership, not garbage-collector residency.
/// Native codec scratch, environment/displacement caches and source geometry are separate domains.
/// </remarks>
public sealed class SilkCpuTextureBudget
{
    private readonly SilkByteReservationPool _pool;

    /// <summary>Creates a positive immutable managed-pixel byte ceiling.</summary>
    public SilkCpuTextureBudget(ulong maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfZero(maximumBytes);
        MaximumBytes = maximumBytes;
        _pool = new(maximumBytes, static (requested, reserved, maximum) =>
            new SilkCpuTextureBudgetExceededException(requested, reserved, maximum));
    }

    /// <summary>Gets the maximum simultaneously owned logical pixel bytes.</summary>
    public ulong MaximumBytes { get; }

    /// <summary>Gets a consistent immutable snapshot of logical pixel-buffer ownership.</summary>
    public SilkGpuBufferUsage Usage
    {
        get
        {
            (ulong reserved, ulong peak, ulong count) = _pool.Snapshot;
            return new(MaximumBytes, reserved, peak, count);
        }
    }

    /// <summary>Configures a device that enforces managed pixel-copy admission.</summary>
    public void ConfigureDevice(ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (device is not SilkGraphicsDeviceLifetimeBase ||
            device is not ISilkCpuTextureAdmissionDevice admission)
        {
            throw new NotSupportedException("The device cannot enforce shared managed pixel-copy admission.");
        }
        admission.ConfigureCpuTextureBudget(this);
    }

    internal IDisposable Reserve(ulong bytes) => _pool.Reserve(bytes);

    internal byte[] Copy(ReadOnlySpan<byte> source, ref List<IDisposable>? owners)
    {
        if (source.IsEmpty)
        {
            return [];
        }
        owners ??= [];
        owners.EnsureCapacity(checked(owners.Count + 1));
        IDisposable reservation = Reserve((ulong)source.Length);
        try
        {
            byte[] copy = source.ToArray();
            owners.Add(reservation);
            return copy;
        }
        catch
        {
            reservation.Dispose();
            throw;
        }
    }

    internal static void ReleaseCopies(ref List<IDisposable>? owners)
    {
        List<IDisposable>? released = Interlocked.Exchange(ref owners, null);
        if (released is not null)
        {
            foreach (IDisposable owner in released)
            {
                owner.Dispose();
            }
        }
    }
}

/// <summary>Reports managed texture preparation refused before a pixel-buffer allocation.</summary>
public sealed class SilkCpuTextureBudgetExceededException : InvalidOperationException
{
    internal SilkCpuTextureBudgetExceededException(ulong requested, ulong reserved, ulong maximum)
        : base(string.Create(CultureInfo.InvariantCulture,
            $"CPU texture preparation refused {requested} bytes with {reserved} reserved (limit {maximum})."))
    {
        RequestedBytes = requested;
        ReservedBytes = reserved;
        MaximumBytes = maximum;
    }

    /// <summary>Gets the refused managed pixel-buffer payload size.</summary>
    public ulong RequestedBytes { get; }
    /// <summary>Gets the reservations held when allocation was refused.</summary>
    public ulong ReservedBytes { get; }
    /// <summary>Gets the configured logical-byte ceiling.</summary>
    public ulong MaximumBytes { get; }
}
