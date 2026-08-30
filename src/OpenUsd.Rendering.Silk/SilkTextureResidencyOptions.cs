// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Immutable, validated retention budgets for the decoded CPU and estimated GPU-resident bytes
/// that <see cref="SilkSceneGpuResources"/> keeps for ordinary, UDIM, volume-density, and
/// fallback material textures.
/// </summary>
/// <remarks>
/// hdSilk retains a texture cache entry until a scene edit, local dependency hot-reload, or the
/// least-recently-used trim performed after a completed graphics submission reclaims it. These two
/// independent budgets bound that retention so a long-running session that visits many distinct
/// textures cannot grow its resident footprint without bound. The defaults are behavior-preserving
/// in the sense that a session whose working set stays within them never evicts; they are chosen
/// as a reasonable production ceiling rather than a hard device limit, and a memory-constrained or
/// headless host should supply tighter values.
/// </remarks>
public sealed class SilkTextureResidencyOptions
{
    /// <summary>
    /// The default maximum decoded CPU pixel bytes retained across every texture cache entry
    /// (512 MiB).
    /// </summary>
    public const ulong DefaultMaxDecodedCpuBytes = 512UL * 1024 * 1024;

    /// <summary>
    /// The default maximum estimated logical GPU-resident texture bytes retained across every
    /// texture cache entry (512 MiB).
    /// </summary>
    public const ulong DefaultMaxGpuBytes = 512UL * 1024 * 1024;

    /// <summary>Gets the behavior-preserving default budgets (512 MiB CPU, 512 MiB GPU).</summary>
    public static SilkTextureResidencyOptions Default { get; } = new();

    /// <summary>Initializes validated texture cache residency budgets.</summary>
    /// <param name="maxDecodedCpuBytes">
    /// The maximum decoded CPU pixel bytes retained across every texture cache entry. Must be
    /// nonzero.
    /// </param>
    /// <param name="maxGpuBytes">
    /// The maximum estimated logical GPU-resident texture bytes retained across every texture
    /// cache entry. Must be nonzero.
    /// </param>
    public SilkTextureResidencyOptions(
        ulong maxDecodedCpuBytes = DefaultMaxDecodedCpuBytes,
        ulong maxGpuBytes = DefaultMaxGpuBytes)
    {
        if (maxDecodedCpuBytes == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDecodedCpuBytes),
                maxDecodedCpuBytes,
                "The maximum decoded CPU texture byte budget must be nonzero.");
        }
        if (maxGpuBytes == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxGpuBytes),
                maxGpuBytes,
                "The maximum GPU texture byte budget must be nonzero.");
        }
        MaxDecodedCpuBytes = maxDecodedCpuBytes;
        MaxGpuBytes = maxGpuBytes;
    }

    /// <summary>Gets the maximum decoded CPU pixel bytes retained across every texture cache entry.</summary>
    public ulong MaxDecodedCpuBytes { get; }

    /// <summary>
    /// Gets the maximum estimated logical GPU-resident texture bytes retained across every
    /// texture cache entry.
    /// </summary>
    public ulong MaxGpuBytes { get; }
}
