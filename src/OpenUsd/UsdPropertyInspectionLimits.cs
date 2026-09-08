// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd;

/// <summary>Immutable admission and preview limits for a selected-prim inspection query.</summary>
/// <remarks>
/// Text bounds output and admitted source text. Work bounds composition, metadata and source list
/// visits before their materialization. Limits exclude the already-open stage and do not sandbox
/// arbitrary resolver plugins. Property rows are complete or the query fails; previews are explicit.
/// </remarks>
public sealed class UsdPropertyInspectionLimits : IUsdDetachedResult
{
    /// <summary>Creates bounded limits; every array, time and target preview is at most sixteen elements.</summary>
    public UsdPropertyInspectionLimits(
        int maximumPropertyCount = 4096,
        int maximumTextBytes = 1024 * 1024,
        int previewElements = 16,
        int timeSamplePreview = 16,
        int targetPreview = 16,
        int maximumMetadataWork = 262_144,
        int maximumPreviewTextBytes = 4096)
    {
        Validate(maximumPropertyCount, 65_536, nameof(maximumPropertyCount));
        Validate(maximumTextBytes, 16 * 1024 * 1024, nameof(maximumTextBytes));
        Validate(previewElements, 16, nameof(previewElements));
        Validate(timeSamplePreview, 16, nameof(timeSamplePreview));
        Validate(targetPreview, 16, nameof(targetPreview));
        Validate(maximumMetadataWork, 1_048_576, nameof(maximumMetadataWork));
        Validate(maximumPreviewTextBytes, 65_536, nameof(maximumPreviewTextBytes));
        MaximumPropertyCount = maximumPropertyCount;
        MaximumTextBytes = maximumTextBytes;
        PreviewElements = previewElements;
        TimeSamplePreview = timeSamplePreview;
        TargetPreview = targetPreview;
        MaximumMetadataWork = maximumMetadataWork;
        MaximumPreviewTextBytes = maximumPreviewTextBytes;
    }

    /// <summary>Gets the conservative selected-prim inspection defaults.</summary>
    public static UsdPropertyInspectionLimits Default { get; } = new();

    /// <summary>Gets larger row/text/work limits while retaining sixteen-element previews.</summary>
    public static UsdPropertyInspectionLimits Viewer { get; } = new(65_536, 16 * 1024 * 1024,
        maximumMetadataWork: 1_048_576);

    /// <summary>Gets the maximum complete property row count.</summary>
    public int MaximumPropertyCount { get; }

    /// <summary>Gets the maximum total output and admitted source UTF-8 bytes, including terminators.</summary>
    public int MaximumTextBytes { get; }

    /// <summary>Gets the maximum scalar/array value preview elements, from zero through sixteen.</summary>
    public int PreviewElements { get; }

    /// <summary>Gets the maximum time sample preview count, from zero through sixteen.</summary>
    public int TimeSamplePreview { get; }

    /// <summary>Gets the maximum relationship/connection target preview count, from zero through sixteen.</summary>
    public int TargetPreview { get; }

    /// <summary>Gets the maximum charged composition, metadata and source-list work units.</summary>
    public int MaximumMetadataWork { get; }

    /// <summary>Gets the maximum UTF-8 bytes per value element or complete asset path field.</summary>
    public int MaximumPreviewTextBytes { get; }

    internal OpenUsdNativePropertyLimits ToNative() => new(40, 1,
        (uint)MaximumPropertyCount, (uint)MaximumTextBytes, (uint)PreviewElements, (uint)TimeSamplePreview,
        (uint)TargetPreview, (uint)MaximumMetadataWork, (uint)MaximumPreviewTextBytes, 0);

    private static void Validate(int value, int maximum, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, name);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, maximum, name);
    }
}
