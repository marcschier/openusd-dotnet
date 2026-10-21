// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd.Rendering.Silk;

/// <summary>Immutable coarse-mesh reservation and command-page ceilings for one native session.</summary>
/// <remarks>
/// Every legacy or explicit sync is bounded without changing its purposes or material binding.
/// Per-request limits may tighten these ceilings but cannot relax them. The mesh metric counts
/// conservative logical numeric-buffer reservations, not actual allocations. Page limits include
/// serialized bytes, not other live pages or overlapping buffers during reallocation and copying.
/// Source/SDK/material caches, metadata, textures and GPU resources are not covered.
/// With CommandStorage enabled, the page ceiling additionally bounds each stored command;
/// the separate storage policy bounds whole updates and live files.
/// The profile requires Low complexity and SmoothShaded meshes without Skel, computed geometry or volumes.
/// </remarks>
public sealed class SilkPreparationLimits
{
    /// <summary>Creates positive session ceilings; there is no implicit process-memory budget.</summary>
    public SilkPreparationLimits(ulong maximumMeshPreparationReservationBytes, int maximumCommandPageBytes)
        : this(maximumMeshPreparationReservationBytes, maximumCommandPageBytes, null)
    {
    }

    /// <summary>Creates preparation ceilings with optional bounded file-backed command transport.</summary>
    public SilkPreparationLimits(
        ulong maximumMeshPreparationReservationBytes, int maximumCommandPageBytes,
        SilkCommandStorageLimits? commandStorage)
    {
        ArgumentOutOfRangeException.ThrowIfZero(maximumMeshPreparationReservationBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCommandPageBytes);
        if (commandStorage is not null && commandStorage.MaximumCommandBytes > maximumCommandPageBytes)
        {
            throw new ArgumentException("Stored commands cannot exceed the preparation page ceiling.", nameof(commandStorage));
        }
        MaximumMeshPreparationReservationBytes = maximumMeshPreparationReservationBytes;
        MaximumCommandPageBytes = maximumCommandPageBytes;
        CommandStorage = commandStorage;
    }

    /// <summary>Gets the maximum concurrent logical mesh preparation reservation bytes.</summary>
    public ulong MaximumMeshPreparationReservationBytes { get; }

    /// <summary>Gets the maximum serialized bytes in each native page and its managed copy.</summary>
    public int MaximumCommandPageBytes { get; }

    /// <summary>Gets opt-in file-backed transport limits, or null for contiguous managed page copies.</summary>
    public SilkCommandStorageLimits? CommandStorage { get; }

    /// <summary>Parses two positive invariant decimal limits, or returns null when neither is configured.</summary>
    /// <remarks>Partial, blank, signed, fractional and out-of-range configurations are errors.</remarks>
    public static SilkPreparationLimits? FromConfiguration(
        string? maximumMeshPreparationReservationBytes, string? maximumCommandPageBytes)
        => FromConfiguration(maximumMeshPreparationReservationBytes, maximumCommandPageBytes, null);

    /// <summary>Parses preparation ceilings and requires them when file-backed command storage is configured.</summary>
    public static SilkPreparationLimits? FromConfiguration(
        string? maximumMeshPreparationReservationBytes, string? maximumCommandPageBytes,
        SilkCommandStorageLimits? commandStorage)
    {
        if (maximumMeshPreparationReservationBytes is null && maximumCommandPageBytes is null)
        {
            if (commandStorage is not null)
            {
                throw new ArgumentException("Command storage requires explicit mesh and command-page preparation ceilings.");
            }
            return null;
        }
        if (!ulong.TryParse(maximumMeshPreparationReservationBytes, NumberStyles.None, CultureInfo.InvariantCulture,
            out ulong meshBytes) || meshBytes == 0 ||
            !int.TryParse(maximumCommandPageBytes, NumberStyles.None, CultureInfo.InvariantCulture,
                out int pageBytes) || pageBytes <= 0)
        {
            throw new ArgumentException(
                "Both mesh reservation and command-page ceilings must be positive base-10 integers; " +
                "the page ceiling must fit Int32.");
        }
        return new SilkPreparationLimits(meshBytes, pageBytes, commandStorage);
    }

    internal SilkSceneIngestionOptions Apply(SilkSceneIngestionOptions options)
    {
        ulong meshBytes = Math.Min(
            options.MaximumMeshPreparationReservationBytes ?? ulong.MaxValue, MaximumMeshPreparationReservationBytes);
        int pageBytes = Math.Min(options.MaximumCommandPageBytes ?? int.MaxValue, MaximumCommandPageBytes);
        return options.MaximumMeshPreparationReservationBytes == meshBytes &&
            options.MaximumCommandPageBytes == pageBytes
            ? options
            : new SilkSceneIngestionOptions(
                options.IncludedPurposes, options.MaterialBindingPurpose, meshBytes, pageBytes);
    }

    internal static void ValidateProfile(RenderComplexity complexity, RenderDrawMode drawMode)
    {
        if (complexity != RenderComplexity.Low || drawMode != RenderDrawMode.SmoothShaded)
        {
            throw new NotSupportedException(
                "Bounded mesh preparation currently requires Low complexity and SmoothShaded draw mode.");
        }
    }
}
