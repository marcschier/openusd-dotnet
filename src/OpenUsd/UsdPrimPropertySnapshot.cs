// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd;

/// <summary>The native variability of an inspected attribute.</summary>
public enum UsdPropertyVariability
{
    /// <summary>The attribute may vary over time.</summary>
    Varying = 0,
    /// <summary>The attribute is declared uniform.</summary>
    Uniform = 1
}

/// <summary>The native composed value source, separate from declarations and connections.</summary>
public enum UsdAttributeResolveSource
{
    /// <summary>No composed value source.</summary>
    None = 0,
    /// <summary>A built-in schema fallback, not an authored layer value.</summary>
    Fallback = 1,
    /// <summary>A winning authored default.</summary>
    Default = 2,
    /// <summary>Winning authored time samples.</summary>
    TimeSamples = 3,
    /// <summary>A native-proven value clip source.</summary>
    ValueClips = 4,
    /// <summary>A native-proven spline source.</summary>
    Spline = 5,
    /// <summary>Resolution is explicitly deferred rather than guessed.</summary>
    Deferred = 6
}

/// <summary>Composed value availability independent of preview truncation and graph connections.</summary>
public enum UsdPropertyValueState
{
    /// <summary>No composed value is present.</summary>
    Unset = 0,
    /// <summary>A composed value exists; inspect the preview status before displaying it as complete.</summary>
    Value = 1,
    /// <summary>A winning block is present; a schema fallback can still supply the preview.</summary>
    Blocked = 2,
    /// <summary>Bounded value evaluation could not be proven.</summary>
    Deferred = 3
}

/// <summary>The closed base of immutable attribute and relationship inspection records.</summary>
public abstract class UsdPropertySnapshot : IUsdDetachedResult
{
    internal UsdPropertySnapshot(OpenUsdNativePropertyEntry value)
    {
        Name = value.Name;
        IsCustom = (value.Values.Flags & 1) != 0;
        IsAuthored = (value.Values.Flags & 2) != 0;
    }

    /// <summary>Gets the canonical, namespace-qualified property name.</summary>
    public string Name { get; }

    /// <summary>Gets whether the property is custom.</summary>
    public bool IsCustom { get; }

    /// <summary>Gets whether a declaration is authored; this does not establish a winning value source.</summary>
    public bool IsAuthored { get; }
}

/// <summary>A detached composed attribute value, provenance, sample and connection inspection record.</summary>
public sealed class UsdAttributePropertySnapshot : UsdPropertySnapshot
{
    internal UsdAttributePropertySnapshot(OpenUsdNativePropertyEntry value) : base(value)
    {
        TypeName = value.TypeName;
        Variability = (UsdPropertyVariability)value.Values.Variability;
        ResolveSource = (UsdAttributeResolveSource)value.Values.ResolveSource;
        ValueState = (UsdPropertyValueState)value.Values.ValueState;
        HasAuthoredValueOpinion = (value.Values.Flags & 8) == 0 ? null : (value.Values.Flags & 16) != 0;
        ValueSource = value.ValueSource is null ? null : new(value.ValueSource);
        Value = new(value);
        TimeSamples = new(value);
        Connections = new(value);
    }

    /// <summary>Gets the exact native Sdf type token, including roles and array suffix.</summary>
    public string TypeName { get; }

    /// <summary>Gets the native uniform/varying declaration.</summary>
    public UsdPropertyVariability Variability { get; }

    /// <summary>Gets the native value resolve source, not the source of the strongest declaration.</summary>
    public UsdAttributeResolveSource ResolveSource { get; }

    /// <summary>Gets whether the composed value is present, unset, blocked or explicitly deferred.</summary>
    public UsdPropertyValueState ValueState { get; }

    /// <summary>Gets native authored-value-opinion state, including blocks; null means unproven.</summary>
    public bool? HasAuthoredValueOpinion { get; }

    /// <summary>Gets native winning authored layer/spec provenance only when proven.</summary>
    public UsdPropertySource? ValueSource { get; }

    /// <summary>Gets the bounded invariant typed value preview, independent of shader connections.</summary>
    public UsdPropertyValuePreview Value { get; }

    /// <summary>Gets the bounded composed time sample count and prefix.</summary>
    public UsdPropertyTimeSamplePreview TimeSamples { get; }

    /// <summary>Gets composed connection graph identities; connections may coexist with a USD default value.</summary>
    public UsdPropertyTargetPreview Connections { get; }
}

/// <summary>A detached composed relationship inspection record.</summary>
public sealed class UsdRelationshipPropertySnapshot : UsdPropertySnapshot
{
    internal UsdRelationshipPropertySnapshot(OpenUsdNativePropertyEntry value) : base(value)
    {
        Targets = new(value);
    }

    /// <summary>Gets the composed target count and bounded direct target prefix.</summary>
    public UsdPropertyTargetPreview Targets { get; }
}

/// <summary>An immutable selected-prim property snapshot that outlives its stage or scheduler.</summary>
/// <remarks>
/// Property rows are complete or quota-refused. Preview incompleteness is explicit per field.
/// This composed inspection DTO is not an authored-layer checkpoint or undo authority.
/// </remarks>
public sealed class UsdPrimPropertySnapshot : IUsdDetachedResult
{
    internal UsdPrimPropertySnapshot(OpenUsdNativePropertySnapshot value, UsdPropertyInspectionLimits limits)
    {
        PrimPath = value.PrimPath;
        TimeCode = value.TimeCode;
        ChangeSerial = value.ChangeSerial;
        IsComplete = value.IsComplete;
        Limits = limits;
        TextByteCount = value.TextByteCount;
        MetadataWorkCount = value.MetadataWorkCount;
        var properties = new UsdPropertySnapshot[value.Entries.Length];
        for (int index = 0; index < properties.Length; index++)
        {
            OpenUsdNativePropertyEntry entry = value.Entries[index];
            properties[index] = entry.Values.Kind == 0
                ? new UsdAttributePropertySnapshot(entry)
                : new UsdRelationshipPropertySnapshot(entry);
        }
        Properties = Array.AsReadOnly(properties);
    }

    /// <summary>Gets the selected absolute prim path.</summary>
    public string PrimPath { get; }

    /// <summary>Gets the queried numeric time, or null for USD default time.</summary>
    public double? TimeCode { get; }

    /// <summary>Gets the change serial captured under the native stage access lock.</summary>
    public ulong ChangeSerial { get; }

    /// <summary>Gets whether every value, sample and target preview is complete.</summary>
    public bool IsComplete { get; }

    /// <summary>Gets the immutable limits used by the query.</summary>
    public UsdPropertyInspectionLimits Limits { get; }

    /// <summary>Gets every property in ordinal UTF-8 name order, independent of propertyOrder metadata.</summary>
    public IReadOnlyList<UsdPropertySnapshot> Properties { get; }

    /// <summary>Gets the packed output UTF-8 byte count, excluding admitted source text.</summary>
    public int TextByteCount { get; }

    /// <summary>Gets charged native metadata and composition admission work units.</summary>
    public int MetadataWorkCount { get; }
}
