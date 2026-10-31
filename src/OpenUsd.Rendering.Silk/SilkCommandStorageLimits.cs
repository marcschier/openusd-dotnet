// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd.Rendering.Silk;

/// <summary>Opt-in ceilings for private file-backed command updates and their bounded readers.</summary>
/// <remarks>
/// Live serialized file bytes share one pool per native session, including replay owners.
/// Filesystem cache, allocator overhead, retained geometry and GPU memory are separate costs.
/// </remarks>
public sealed class SilkCommandStorageLimits
{
    /// <summary>Creates live-file, update, command and bulk-transfer ceilings.</summary>
    public SilkCommandStorageLimits(
        ulong maximumLiveBytes, ulong maximumUpdateBytes, int maximumCommandBytes, int transferBufferBytes)
    {
        ArgumentOutOfRangeException.ThrowIfZero(maximumLiveBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumLiveBytes, (ulong)long.MaxValue);
        ArgumentOutOfRangeException.ThrowIfZero(maximumUpdateBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumUpdateBytes, maximumLiveBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCommandBytes, 8);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((ulong)maximumCommandBytes, maximumUpdateBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(transferBufferBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(transferBufferBytes, maximumCommandBytes);
        MaximumLiveBytes = maximumLiveBytes;
        MaximumUpdateBytes = maximumUpdateBytes;
        MaximumCommandBytes = maximumCommandBytes;
        TransferBufferBytes = transferBufferBytes;
    }

    /// <summary>Gets the aggregate serialized-byte ceiling across live files from one session.</summary>
    public ulong MaximumLiveBytes { get; }

    /// <summary>Gets the serialized-byte ceiling for one complete update.</summary>
    public ulong MaximumUpdateBytes { get; }

    /// <summary>Gets the ceiling for one command and the renderer's reusable command buffer.</summary>
    public int MaximumCommandBytes { get; }

    /// <summary>Gets the bulk native/file transfer-buffer ceiling.</summary>
    public int TransferBufferBytes { get; }

    /// <summary>Parses four positive invariant decimal limits, or returns null when all are absent.</summary>
    public static SilkCommandStorageLimits? FromConfiguration(
        string? maximumLiveBytes, string? maximumUpdateBytes, string? maximumCommandBytes, string? transferBufferBytes)
    {
        if (maximumLiveBytes is null && maximumUpdateBytes is null &&
            maximumCommandBytes is null && transferBufferBytes is null)
        {
            return null;
        }
        if (!ulong.TryParse(maximumLiveBytes, NumberStyles.None, CultureInfo.InvariantCulture, out ulong live) ||
            !ulong.TryParse(maximumUpdateBytes, NumberStyles.None, CultureInfo.InvariantCulture, out ulong update) ||
            !int.TryParse(maximumCommandBytes, NumberStyles.None, CultureInfo.InvariantCulture, out int command) ||
            !int.TryParse(transferBufferBytes, NumberStyles.None, CultureInfo.InvariantCulture, out int transfer))
        {
            throw new ArgumentException("All four command-storage ceilings must be positive base-10 integers.");
        }
        return new(live, update, command, transfer);
    }
}
