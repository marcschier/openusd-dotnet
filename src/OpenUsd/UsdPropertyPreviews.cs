// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd;

/// <summary>Describes availability of one bounded inspection preview.</summary>
public enum UsdPropertyPreviewStatus
{
    /// <summary>The preview and its count are complete.</summary>
    Complete = 0,
    /// <summary>A known value or list exceeds the requested preview length.</summary>
    Truncated = 1,
    /// <summary>The native store or composition cannot prove a bounded read.</summary>
    Deferred = 2,
    /// <summary>The native value type has no supported invariant preview representation.</summary>
    Unsupported = 3
}

/// <summary>A structured native inspection admission or preview reason, independent of display diagnostics.</summary>
/// <remarks>
/// Reasons describe why a preview is incomplete, not which opinion wins value resolution.
/// In particular, Spline identifies contributing spline metadata; it does not establish a winning spline source.
/// </remarks>
public enum UsdPropertyPreviewReasonKind
{
    /// <summary>No preview restriction or deferred domain was encountered.</summary>
    None = 0,
    /// <summary>The value or list exceeds its preview element limit.</summary>
    PreviewLimit = 1,
    /// <summary>A value exceeds its UTF-8 preview text limit.</summary>
    TextLimit = 2,
    /// <summary>The backing store cannot admit the value before deserialization.</summary>
    DeferredStore = 3,
    /// <summary>Value-clip opinions may contribute and bounded resolution is unproven.</summary>
    ValueClips = 4,
    /// <summary>Array-edit composition prevents a bounded value or sample-domain preview.</summary>
    ArrayEdit = 5,
    /// <summary>The native value type or declared storage representation is unsupported.</summary>
    UnsupportedType = 6,
    /// <summary>Contributing spline metadata was detected; this is not a winning-source or HasSpline claim.</summary>
    Spline = 7,
    /// <summary>Asset expression evaluation is deferred before expansion.</summary>
    AssetExpression = 8,
    /// <summary>Native composition cannot prove a bounded preview.</summary>
    Composition = 9
}

/// <summary>A native-proven winning authored source, never merely a local declaration.</summary>
public sealed class UsdPropertySource : IUsdDetachedResult
{
    internal UsdPropertySource(OpenUsdNativePropertySource value)
    {
        LayerIdentifier = value.LayerIdentifier;
        SpecPath = value.SpecPath;
    }

    /// <summary>Gets the native source layer identifier; it may identify an anonymous layer.</summary>
    public string LayerIdentifier { get; }

    /// <summary>Gets the source property spec path in that layer's namespace.</summary>
    public string SpecPath { get; }
}

/// <summary>Complete, unshortened native asset fields for one admitted value element.</summary>
public sealed class UsdPropertyAssetPath : IUsdDetachedResult
{
    internal UsdPropertyAssetPath(OpenUsdNativePropertyAsset value)
    {
        AuthoredPath = value.AuthoredPath;
        EvaluatedPath = value.EvaluatedPath;
        ResolvedPath = value.ResolvedPath;
        AnchorLayerIdentifier = value.AnchorLayerIdentifier;
        IsMissing = value.IsMissing;
    }

    /// <summary>Gets the raw path exactly as authored.</summary>
    public string AuthoredPath { get; }

    /// <summary>Gets the evaluated expression path, or empty when no expression was evaluated.</summary>
    public string EvaluatedPath { get; }

    /// <summary>Gets the native resolver result, or empty when unresolved.</summary>
    public string ResolvedPath { get; }

    /// <summary>Gets the native winning source layer used as the relative asset anchor, or empty if none.</summary>
    public string AnchorLayerIdentifier { get; }

    /// <summary>Gets whether a nonempty anchored asset was unresolved; null means unknown/not applicable.</summary>
    public bool? IsMissing { get; }
}

/// <summary>An immutable invariant-text scalar or array prefix, explicitly typed by its attribute.</summary>
/// <remarks>
/// Elements contain raw string/token text, invariant numbers or numeric tuples, never a whole-value
/// serializer. Truncated string elements are UTF-8-safe prefixes. Asset fields are never shortened.
/// An unavailable preview does not mean the underlying composed value is empty.
/// </remarks>
public sealed class UsdPropertyValuePreview : IUsdDetachedResult
{
    internal UsdPropertyValuePreview(OpenUsdNativePropertyEntry value)
    {
        Status = (UsdPropertyPreviewStatus)value.Values.Value.Status;
        ReasonKind = (UsdPropertyPreviewReasonKind)value.Values.Value.Reason;
        Reason = UsdPropertyPreviewReason.Describe(value.Values.Value.Reason);
        IsArray = (value.Values.Flags & 4) != 0;
        ElementCount = UsdPropertyPreviewReason.Count(value.Values.Value.TotalCount);
        Elements = Array.AsReadOnly(value.Elements);
        var assets = new UsdPropertyAssetPath[value.Assets.Length];
        for (int index = 0; index < assets.Length; index++)
        {
            assets[index] = new(value.Assets[index]);
        }
        Assets = Array.AsReadOnly(assets);
    }

    /// <summary>Gets this value preview's completeness.</summary>
    public UsdPropertyPreviewStatus Status { get; }

    /// <summary>Gets an actionable reason for an incomplete preview, otherwise empty.</summary>
    public string Reason { get; }

    /// <summary>Gets the structured native preview reason without parsing display diagnostics.</summary>
    public UsdPropertyPreviewReasonKind ReasonKind { get; }

    /// <summary>Gets whether the declared native type is an array.</summary>
    public bool IsArray { get; }

    /// <summary>Gets the exact native element count when known; null explicitly means unproven.</summary>
    public ulong? ElementCount { get; }

    /// <summary>Gets at most sixteen invariant scalar/array element previews.</summary>
    public IReadOnlyList<string> Elements { get; }

    /// <summary>Gets complete native asset fields corresponding to the admitted asset elements.</summary>
    public IReadOnlyList<UsdPropertyAssetPath> Assets { get; }
}

/// <summary>An immutable bounded preview of composed sample times in stage time.</summary>
public sealed class UsdPropertyTimeSamplePreview : IUsdDetachedResult
{
    internal UsdPropertyTimeSamplePreview(OpenUsdNativePropertyEntry value)
    {
        Status = (UsdPropertyPreviewStatus)value.Values.TimeSamples.Status;
        ReasonKind = (UsdPropertyPreviewReasonKind)value.Values.TimeSamples.Reason;
        Reason = UsdPropertyPreviewReason.Describe(value.Values.TimeSamples.Reason);
        Count = UsdPropertyPreviewReason.Count(value.Values.TimeSamples.TotalCount);
        Times = Array.AsReadOnly(value.Times);
    }

    /// <summary>Gets this sample preview's completeness.</summary>
    public UsdPropertyPreviewStatus Status { get; }

    /// <summary>Gets an actionable incomplete-preview reason, otherwise empty.</summary>
    public string Reason { get; }

    /// <summary>Gets the structured native sample-preview reason without claiming a winning value source.</summary>
    public UsdPropertyPreviewReasonKind ReasonKind { get; }

    /// <summary>Gets the exact composed sample count when proven, otherwise null.</summary>
    public ulong? Count { get; }

    /// <summary>Gets the ascending, finite prefix of sample times in stage coordinates.</summary>
    public IReadOnlyList<double> Times { get; }
}

/// <summary>An immutable relationship or connection target prefix; forwarding is not flattened.</summary>
public sealed class UsdPropertyTargetPreview : IUsdDetachedResult
{
    internal UsdPropertyTargetPreview(OpenUsdNativePropertyEntry value)
    {
        Status = (UsdPropertyPreviewStatus)value.Values.Targets.Status;
        ReasonKind = (UsdPropertyPreviewReasonKind)value.Values.Targets.Reason;
        Reason = UsdPropertyPreviewReason.Describe(value.Values.Targets.Reason);
        Count = UsdPropertyPreviewReason.Count(value.Values.Targets.TotalCount);
        Paths = Array.AsReadOnly(value.Targets);
        Source = value.TargetSource is null ? null : new(value.TargetSource);
    }

    /// <summary>Gets this target preview's completeness.</summary>
    public UsdPropertyPreviewStatus Status { get; }

    /// <summary>Gets an actionable incomplete-preview reason, otherwise empty.</summary>
    public string Reason { get; }

    /// <summary>Gets the structured native target-preview reason without parsing display diagnostics.</summary>
    public UsdPropertyPreviewReasonKind ReasonKind { get; }

    /// <summary>Gets the exact composed target count when proven, otherwise null.</summary>
    public ulong? Count { get; }

    /// <summary>Gets native targets in native order, without following shader or relationship graphs.</summary>
    public IReadOnlyList<string> Paths { get; }

    /// <summary>Gets a singular native-proven source when available; composed list operations may have none.</summary>
    public UsdPropertySource? Source { get; }
}

internal static class UsdPropertyPreviewReason
{
    internal static ulong? Count(ulong value) => value == ulong.MaxValue ? null : value;

    internal static string Describe(uint reason) => reason switch
    {
        0 => "",
        1 => "The value or list exceeds the requested preview element limit.",
        2 => "The value exceeds the per-element UTF-8 preview limit.",
        3 => "The native backing store cannot admit this value before deserialization.",
        4 => "Value-clip opinions may contribute; bounded composed resolution is not proven.",
        5 => "Array-edit composition requires an unbounded materialization and is deferred.",
        6 => "This native type has no supported invariant inspection preview.",
        7 => "Spline evaluation and knot metadata are deferred.",
        8 => "Asset expression evaluation is deferred before expansion.",
        9 => "This native composition cannot prove a bounded preview.",
        _ => throw new InvalidOperationException("The native property reason was not validated.")
    };
}
