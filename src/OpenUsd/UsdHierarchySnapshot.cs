// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd;

/// <summary>Indicates whether bounded native variant metadata is available for a hierarchy entry.</summary>
public enum UsdHierarchyVariantMetadataStatus
{
    /// <summary>The variant-set list, choices and applied selections are complete.</summary>
    Complete = 0,

    /// <summary>
    /// The backing store cannot admit variant reads before allocation. No partial selectors are returned.
    /// This includes deferred crate string list operations and unrecognized data stores.
    /// </summary>
    Deferred = 1
}

/// <summary>An immutable, stage-detached inline variant selector.</summary>
public sealed class UsdHierarchyVariantSet : IUsdDetachedResult
{
    internal UsdHierarchyVariantSet(OpenUsdNativeHierarchyVariantSet value)
    {
        Name = value.Name;
        Selection = value.Selection;
        VariantNames = Array.AsReadOnly(value.VariantNames);
    }

    /// <summary>Gets the variant-set name.</summary>
    public string Name { get; }

    /// <summary>Gets available choices in native OpenUSD order.</summary>
    public IReadOnlyList<string> VariantNames { get; }

    /// <summary>Gets the actually applied selection, including fallbacks; empty means none.</summary>
    public string Selection { get; }
}

/// <summary>An immutable, stage-detached composed prim hierarchy entry.</summary>
public sealed class UsdHierarchyEntry : IUsdDetachedResult
{
    private static readonly IReadOnlyList<UsdHierarchyVariantSet> EmptyVariants =
        Array.AsReadOnly(Array.Empty<UsdHierarchyVariantSet>());

    private readonly uint _flags;

    internal UsdHierarchyEntry(OpenUsdNativeHierarchyEntry value)
    {
        Path = value.Path;
        Name = value.Name;
        TypeName = value.TypeName;
        PrototypePath = value.PrototypePath;
        ParentIndex = value.Values.ParentIndex;
        Depth = (int)value.Values.Depth;
        ChildCount = (int)value.Values.ChildCount;
        _flags = value.Values.Flags;
        VariantMetadataStatus = (UsdHierarchyVariantMetadataStatus)value.Values.VariantStatus;
        if (value.VariantSets.Length == 0)
        {
            VariantSets = EmptyVariants;
        }
        else
        {
            var variants = new UsdHierarchyVariantSet[value.VariantSets.Length];
            for (int index = 0; index < variants.Length; index++)
            {
                variants[index] = new UsdHierarchyVariantSet(value.VariantSets[index]);
            }
            VariantSets = Array.AsReadOnly(variants);
        }
    }

    /// <summary>Gets the absolute prim path, never the pseudo-root path.</summary>
    public string Path { get; }

    /// <summary>Gets the prim's terminal name.</summary>
    public string Name { get; }

    /// <summary>Gets the composed type token, which may be empty.</summary>
    public string TypeName { get; }

    /// <summary>Gets the parent entry index, or minus one for a scene or prototype root.</summary>
    public int ParentIndex { get; }

    /// <summary>Gets the depth, starting at one for scene and prototype roots.</summary>
    public int Depth { get; }

    /// <summary>Gets the number of immediate represented children, excluding instance proxies.</summary>
    public int ChildCount { get; }

    /// <summary>Gets whether this prim is active.</summary>
    public bool IsActive => (_flags & 1) != 0;

    /// <summary>Gets whether this prim is loaded.</summary>
    public bool IsLoaded => (_flags & 2) != 0;

    /// <summary>Gets whether this prim is defined.</summary>
    public bool IsDefined => (_flags & 4) != 0;

    /// <summary>Gets whether this prim is abstract.</summary>
    public bool IsAbstract => (_flags & 8) != 0;

    /// <summary>Gets whether this prim is a prototype root.</summary>
    public bool IsPrototype => (_flags & 16) != 0;

    /// <summary>Gets whether this prim is in a prototype, including its root.</summary>
    public bool IsInPrototype => (_flags & 32) != 0;

    /// <summary>Gets whether this prim is an instance.</summary>
    public bool IsInstance => (_flags & 64) != 0;

    /// <summary>Gets whether this prim is an instance proxy; this snapshot never expands proxies.</summary>
    public bool IsInstanceProxy => (_flags & 128) != 0;

    /// <summary>Gets the effective native has-payload flag, without materializing payload arcs.</summary>
    public bool HasPayload => (_flags & 256) != 0;

    /// <summary>Gets whether this prim is marked instanceable.</summary>
    public bool IsInstanceable => (_flags & 512) != 0;

    /// <summary>Gets the referenced prototype root path for an instance, otherwise an empty string.</summary>
    public string PrototypePath { get; }

    /// <summary>Gets the availability of this entry's inline variant selectors.</summary>
    public UsdHierarchyVariantMetadataStatus VariantMetadataStatus { get; }

    /// <summary>Gets complete selectors in native set order, or an empty list when explicitly deferred.</summary>
    public IReadOnlyList<UsdHierarchyVariantSet> VariantSets { get; }
}

/// <summary>An immutable hierarchy snapshot that remains usable after its stage or scheduler is disposed.</summary>
/// <remarks>
/// Entries always contain the entire admitted hierarchy. Quotas fail, never truncate.
/// Native all-prim preorder includes inactive, undefined and abstract prims. Prototypes follow in
/// first-reference order and appear once; no instance proxies or pseudo-root row are expanded.
/// Inactive prims have no composed descendants. Unloaded payload contents are not loaded by this query.
/// </remarks>
public sealed class UsdHierarchySnapshot : IUsdDetachedResult
{
    internal UsdHierarchySnapshot(OpenUsdNativeHierarchySnapshot value, UsdHierarchyLimits limits)
    {
        ChangeSerial = value.ChangeSerial;
        IsComplete = value.IsComplete;
        Limits = limits;
        TextByteCount = value.TextByteCount;
        MetadataWorkCount = value.MetadataWorkCount;
        var entries = new UsdHierarchyEntry[value.Entries.Length];
        for (int index = 0; index < entries.Length; index++)
        {
            entries[index] = new UsdHierarchyEntry(value.Entries[index]);
        }
        Entries = Array.AsReadOnly(entries);
    }

    /// <summary>Gets the stage change serial observed under the native query's stage-access lock.</summary>
    public ulong ChangeSerial { get; }

    /// <summary>Gets whether all variant metadata is complete; hierarchy rows are always complete.</summary>
    public bool IsComplete { get; }

    /// <summary>Gets the immutable limits used for this query.</summary>
    public UsdHierarchyLimits Limits { get; }

    /// <summary>Gets all scene and prototype entries in native traversal order.</summary>
    public IReadOnlyList<UsdHierarchyEntry> Entries { get; }

    /// <summary>Gets the packed output UTF-8 byte count, excluding source-admission text.</summary>
    public int TextByteCount { get; }

    /// <summary>Gets the charged composition and variant metadata admission work units.</summary>
    public int MetadataWorkCount { get; }
}
