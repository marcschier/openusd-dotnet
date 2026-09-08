// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd;

/// <summary>Immutable admission limits for a complete native hierarchy query.</summary>
/// <remarks>
/// Text includes NUL-terminated output and borrowed source variant text admitted before SDK copies.
/// Variant limits apply independently to output counts and aggregate source list elements, including
/// list-op duplicates/deletions. Metadata work bounds composition-node, layer and variant-item visits.
/// Limits cover this query, not the preceding stage open or composition.
/// </remarks>
public sealed class UsdHierarchyLimits : IUsdDetachedResult
{
    /// <summary>Creates limits no larger than the supported native ceilings.</summary>
    public UsdHierarchyLimits(
        int maximumPrimCount = 100_000,
        int maximumTextBytes = 16 * 1024 * 1024,
        int maximumDepth = 256,
        int maximumVariantSets = 4096,
        int maximumVariantNames = 65_536,
        int maximumMetadataWork = 2_000_000)
    {
        Validate(maximumPrimCount, 1_000_000, nameof(maximumPrimCount));
        Validate(maximumTextBytes, 128 * 1024 * 1024, nameof(maximumTextBytes));
        Validate(maximumDepth, 1024, nameof(maximumDepth));
        Validate(maximumVariantSets, 65_536, nameof(maximumVariantSets));
        Validate(maximumVariantNames, 262_144, nameof(maximumVariantNames));
        Validate(maximumMetadataWork, 16_000_000, nameof(maximumMetadataWork));
        MaximumPrimCount = maximumPrimCount;
        MaximumTextBytes = maximumTextBytes;
        MaximumDepth = maximumDepth;
        MaximumVariantSets = maximumVariantSets;
        MaximumVariantNames = maximumVariantNames;
        MaximumMetadataWork = maximumMetadataWork;
    }

    /// <summary>Gets conservative inspection defaults: 100,000 prims and 16 MiB of text.</summary>
    public static UsdHierarchyLimits Default { get; } = new();

    /// <summary>Gets viewer ceilings: 1,000,000 prims, 128 MiB of text and depth 1,024.</summary>
    public static UsdHierarchyLimits Viewer { get; } = new(
        1_000_000, 128 * 1024 * 1024, 1024, 65_536, 262_144, 16_000_000);

    /// <summary>Gets the maximum total scene and prototype prim count.</summary>
    public int MaximumPrimCount { get; }

    /// <summary>Gets the maximum aggregate output and admitted source UTF-8 bytes, including terminators.</summary>
    public int MaximumTextBytes { get; }

    /// <summary>Gets the maximum hierarchy depth; stage and prototype roots have depth one.</summary>
    public int MaximumDepth { get; }

    /// <summary>Gets the maximum emitted set count and aggregate admitted source set-list elements.</summary>
    public int MaximumVariantSets { get; }

    /// <summary>Gets the maximum emitted choice count and aggregate admitted source choice-list elements.</summary>
    public int MaximumVariantNames { get; }

    /// <summary>Gets the maximum composition and variant metadata admission work units.</summary>
    public int MaximumMetadataWork { get; }

    internal OpenUsdNativeHierarchyLimits ToNative() => new(
        32, 1, (uint)MaximumPrimCount, (uint)MaximumTextBytes, (uint)MaximumDepth,
        (uint)MaximumVariantSets, (uint)MaximumVariantNames, (uint)MaximumMetadataWork);

    private static void Validate(int value, int maximum, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, maximum, parameterName);
    }
}
