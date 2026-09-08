// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using OpenUsd.Geom;
using OpenUsd.Interop;

namespace OpenUsd.Viewer;

internal sealed class ViewerHierarchyEntry : IUsdDetachedResult, IEquatable<ViewerHierarchyEntry>
{
    private readonly ViewerVariantSetSnapshot[] _variantSets;

    internal ViewerHierarchyEntry(
        string path,
        string name,
        string typeName,
        string? parentPath,
        int depth,
        int childCount,
        bool isActive = true,
        bool isLoaded = true,
        bool isDefined = true,
        bool isAbstract = false,
        bool isPrototype = false,
        bool hasPayloads = false,
        IReadOnlyList<ViewerVariantSetSnapshot>? variantSets = null,
        UsdHierarchyVariantMetadataStatus variantMetadataStatus = UsdHierarchyVariantMetadataStatus.Complete,
        bool isInPrototype = false,
        bool isInstance = false,
        string? prototypePath = null)
    {
        Path = path;
        Name = name;
        TypeName = typeName;
        ParentPath = parentPath;
        Depth = depth;
        ChildCount = childCount;
        IsActive = isActive;
        IsLoaded = isLoaded;
        IsDefined = isDefined;
        IsAbstract = isAbstract;
        IsPrototype = isPrototype;
        HasPayloads = hasPayloads;
        VariantMetadataStatus = variantMetadataStatus;
        IsInPrototype = isInPrototype;
        IsInstance = isInstance;
        PrototypePath = prototypePath;
        _variantSets = variantSets?.ToArray() ?? [];
        VariantSets = Array.AsReadOnly(_variantSets);
    }

    internal string Path { get; }

    internal string Name { get; }

    internal string TypeName { get; }

    internal string? ParentPath { get; }

    internal int Depth { get; }

    internal int ChildCount { get; }

    internal bool IsActive { get; }

    internal bool IsLoaded { get; }

    internal bool IsDefined { get; }

    internal bool IsAbstract { get; }

    internal bool IsPrototype { get; }

    internal bool HasPayloads { get; }

    internal UsdHierarchyVariantMetadataStatus VariantMetadataStatus { get; }

    internal bool IsInPrototype { get; }

    internal bool IsInstance { get; }

    internal string? PrototypePath { get; }

    internal IReadOnlyList<ViewerVariantSetSnapshot> VariantSets { get; }

    public bool Equals(ViewerHierarchyEntry? other) =>
        other is not null &&
        string.Equals(Path, other.Path, StringComparison.Ordinal) &&
        string.Equals(Name, other.Name, StringComparison.Ordinal) &&
        string.Equals(TypeName, other.TypeName, StringComparison.Ordinal) &&
        string.Equals(ParentPath, other.ParentPath, StringComparison.Ordinal) &&
        Depth == other.Depth &&
        ChildCount == other.ChildCount &&
        IsActive == other.IsActive &&
        IsLoaded == other.IsLoaded &&
        IsDefined == other.IsDefined &&
        IsAbstract == other.IsAbstract &&
        IsPrototype == other.IsPrototype &&
        HasPayloads == other.HasPayloads &&
        VariantMetadataStatus == other.VariantMetadataStatus &&
        IsInPrototype == other.IsInPrototype &&
        IsInstance == other.IsInstance &&
        string.Equals(PrototypePath, other.PrototypePath, StringComparison.Ordinal) &&
        _variantSets.SequenceEqual(other._variantSets, ViewerVariantSetSnapshot.ValueComparer);

    public override bool Equals(object? obj) => Equals(obj as ViewerHierarchyEntry);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Path, StringComparer.Ordinal);
        hash.Add(Name, StringComparer.Ordinal);
        hash.Add(TypeName, StringComparer.Ordinal);
        hash.Add(ParentPath, StringComparer.Ordinal);
        hash.Add(Depth);
        hash.Add(ChildCount);
        hash.Add(IsActive);
        hash.Add(IsLoaded);
        hash.Add(IsDefined);
        hash.Add(IsAbstract);
        hash.Add(IsPrototype);
        hash.Add(HasPayloads);
        hash.Add(VariantMetadataStatus);
        hash.Add(IsInPrototype);
        hash.Add(IsInstance);
        hash.Add(PrototypePath, StringComparer.Ordinal);
        foreach (ViewerVariantSetSnapshot variantSet in _variantSets)
        {
            hash.Add(variantSet, ViewerVariantSetSnapshot.ValueComparer);
        }
        return hash.ToHashCode();
    }

    public override string ToString() =>
        $"{Path} ({TypeName}; active={IsActive}; loaded={IsLoaded}; " +
        $"defined={IsDefined}; abstract={IsAbstract}; prototype={IsPrototype})";
}

internal sealed record ViewerHierarchySourceEntry(
    string Path,
    string TypeName,
    bool IsActive = true,
    bool IsLoaded = true,
    bool IsDefined = true,
    bool IsAbstract = false,
    bool IsPrototype = false,
    bool HasPayloads = false,
    IReadOnlyList<ViewerVariantSetSnapshot>? VariantSets = null,
    UsdHierarchyVariantMetadataStatus VariantMetadataStatus = UsdHierarchyVariantMetadataStatus.Complete,
    bool IsInPrototype = false,
    bool IsInstance = false,
    string? PrototypePath = null);

internal sealed record ViewerHierarchyFilter(
    string? NameQuery,
    string? TypeQuery,
    bool ShowInactive = false,
    bool ShowUndefined = false,
    bool ShowAbstract = false,
    bool ShowPrototypes = false);

internal sealed record ViewerHierarchySnapshot : IUsdDetachedResult
{
    private readonly Dictionary<string, ViewerHierarchyEntry> _byPath;
    private readonly Dictionary<string, ViewerHierarchyEntry[]> _children;

    private ViewerHierarchySnapshot(ViewerHierarchyEntry[] entries, ulong? changeSerial = null)
    {
        Entries = entries;
        ChangeSerial = changeSerial;
        _byPath = entries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        _children = entries
            .GroupBy(entry => entry.ParentPath ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray(),
                StringComparer.Ordinal);
    }

    internal ViewerHierarchyEntry[] Entries { get; }

    internal ulong? ChangeSerial { get; }

    internal static ViewerHierarchySnapshot Empty { get; } = new([]);

    internal static ViewerHierarchySnapshot Build(IEnumerable<string> traversalPaths)
    {
        ArgumentNullException.ThrowIfNull(traversalPaths);
        return Build(traversalPaths.Select(path => new ViewerHierarchySourceEntry(path, string.Empty)));
    }

    internal static ViewerHierarchySnapshot Build(IEnumerable<ViewerHierarchySourceEntry> traversalEntries) =>
        Build(traversalEntries, null);

    private static ViewerHierarchySnapshot Build(
        IEnumerable<ViewerHierarchySourceEntry> traversalEntries, ulong? changeSerial)
    {
        ArgumentNullException.ThrowIfNull(traversalEntries);
        ViewerHierarchySourceEntry[] sourceEntries = traversalEntries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path) && entry.Path[0] == '/')
            .GroupBy(entry => entry.Path, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        var childCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (ViewerHierarchySourceEntry entry in sourceEntries)
        {
            string? parent = GetParentPath(entry.Path);
            if (parent is not null)
            {
                childCounts[parent] = childCounts.GetValueOrDefault(parent) + 1;
            }
        }

        var entries = new ViewerHierarchyEntry[sourceEntries.Length];
        for (int index = 0; index < sourceEntries.Length; index++)
        {
            ViewerHierarchySourceEntry source = sourceEntries[index];
            entries[index] = new ViewerHierarchyEntry(
                source.Path,
                GetName(source.Path),
                source.TypeName,
                GetParentPath(source.Path),
                GetDepth(source.Path),
                childCounts.GetValueOrDefault(source.Path),
                source.IsActive,
                source.IsLoaded,
                source.IsDefined,
                source.IsAbstract,
                source.IsPrototype,
                source.HasPayloads,
                source.VariantSets,
                source.VariantMetadataStatus,
                source.IsInPrototype,
                source.IsInstance,
                source.PrototypePath);
        }
        return new ViewerHierarchySnapshot(entries, changeSerial);
    }

    internal static ViewerHierarchySnapshot FromNative(UsdHierarchySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var entries = new ViewerHierarchyEntry[snapshot.Entries.Count];
        for (int index = 0; index < entries.Length; index++)
        {
            UsdHierarchyEntry source = snapshot.Entries[index];
            var variants = new ViewerVariantSetSnapshot[source.VariantSets.Count];
            for (int variantIndex = 0; variantIndex < variants.Length; variantIndex++)
            {
                UsdHierarchyVariantSet variant = source.VariantSets[variantIndex];
                variants[variantIndex] = ViewerVariantSetSnapshot.Create(
                    variant.Name, variant.VariantNames,
                    string.IsNullOrEmpty(variant.Selection) ? null : variant.Selection);
            }
            entries[index] = new ViewerHierarchyEntry(
                source.Path, source.Name, source.TypeName,
                source.ParentIndex < 0 ? null : snapshot.Entries[source.ParentIndex].Path,
                source.Depth - 1, source.ChildCount, source.IsActive, source.IsLoaded,
                source.IsDefined, source.IsAbstract, source.IsPrototype, source.HasPayload, variants,
                source.VariantMetadataStatus, source.IsInPrototype, source.IsInstance,
                string.IsNullOrEmpty(source.PrototypePath) ? null : source.PrototypePath);
        }
        return new ViewerHierarchySnapshot(entries, snapshot.ChangeSerial);
    }

    internal bool Contains(string path) => _byPath.ContainsKey(path);

    internal ViewerHierarchyEntry[] GetChildren(string? parentPath) =>
        _children.TryGetValue(parentPath ?? string.Empty, out ViewerHierarchyEntry[]? children)
            ? children
            : [];

    internal ViewerHierarchySnapshot Filter(string? query) => Filter(new ViewerHierarchyFilter(query, null));

    internal ViewerHierarchySnapshot Filter(ViewerHierarchyFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (string.IsNullOrWhiteSpace(filter.NameQuery) &&
            string.IsNullOrWhiteSpace(filter.TypeQuery) &&
            filter.ShowInactive &&
            filter.ShowUndefined &&
            filter.ShowAbstract &&
            filter.ShowPrototypes)
        {
            return this;
        }

        var included = new HashSet<string>(StringComparer.Ordinal);
        foreach (ViewerHierarchyEntry entry in Entries)
        {
            if (!Matches(entry, filter))
            {
                continue;
            }

            string? path = entry.Path;
            while (path is not null && included.Add(path))
            {
                path = GetParentPath(path);
            }
        }
        return Build(Entries
            .Where(entry => included.Contains(entry.Path))
            .Select(ToSourceEntry), ChangeSerial);
    }

    private static bool Matches(ViewerHierarchyEntry entry, ViewerHierarchyFilter filter)
    {
        if ((!filter.ShowInactive && !entry.IsActive) ||
            (!filter.ShowUndefined && !entry.IsDefined) ||
            (!filter.ShowAbstract && entry.IsAbstract) ||
            (!filter.ShowPrototypes && (entry.IsPrototype || entry.IsInPrototype)))
        {
            return false;
        }

        bool nameMatches = string.IsNullOrWhiteSpace(filter.NameQuery) ||
            entry.Name.Contains(filter.NameQuery, StringComparison.OrdinalIgnoreCase) ||
            entry.Path.Contains(filter.NameQuery, StringComparison.OrdinalIgnoreCase);
        bool typeMatches = string.IsNullOrWhiteSpace(filter.TypeQuery) ||
            entry.TypeName.Contains(filter.TypeQuery, StringComparison.OrdinalIgnoreCase);
        return nameMatches && typeMatches;
    }

    private static ViewerHierarchySourceEntry ToSourceEntry(ViewerHierarchyEntry entry) =>
        new(
            entry.Path,
            entry.TypeName,
            entry.IsActive,
            entry.IsLoaded,
            entry.IsDefined,
            entry.IsAbstract,
            entry.IsPrototype,
            entry.HasPayloads,
            entry.VariantSets,
            entry.VariantMetadataStatus,
            entry.IsInPrototype,
            entry.IsInstance,
            entry.PrototypePath);

    private static string GetName(string path)
    {
        int separator = path.LastIndexOf('/');
        return separator < 0 || separator == path.Length - 1
            ? path
            : path[(separator + 1)..];
    }

    private static string? GetParentPath(string path)
    {
        int separator = path.LastIndexOf('/');
        return separator <= 0 ? null : path[..separator];
    }

    private static int GetDepth(string path)
    {
        int depth = 0;
        foreach (char character in path)
        {
            if (character == '/')
            {
                depth++;
            }
        }
        return Math.Max(0, depth - 1);
    }
}

internal sealed class ViewerHierarchyTreeSource
{
    internal ViewerHierarchyTreeSource(ViewerHierarchySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Snapshot = snapshot;
        Roots = GetRootPage(0).Nodes;
    }

    internal ViewerHierarchySnapshot Snapshot { get; }

    internal IReadOnlyList<ViewerHierarchyTreeNode> Roots { get; }

    internal ViewerHierarchyPage GetRootPage(int pageIndex, string? revealPath = null) =>
        ViewerHierarchyPage.Create(Snapshot, null, pageIndex, revealPath);
}

internal sealed class ViewerHierarchyTreeNode
{
    private readonly ViewerHierarchySnapshot _snapshot;
    private readonly Lazy<IReadOnlyList<ViewerHierarchyTreeNode>> _children;

    internal ViewerHierarchyTreeNode(
        ViewerHierarchySnapshot snapshot,
        ViewerHierarchyEntry entry)
    {
        _snapshot = snapshot;
        Entry = entry;
        _children = new Lazy<IReadOnlyList<ViewerHierarchyTreeNode>>(
            () => GetChildrenPage(0).Nodes);
    }

    internal ViewerHierarchyEntry Entry { get; }

    internal bool IsChildrenMaterialized => _children.IsValueCreated;

    internal IReadOnlyList<ViewerHierarchyTreeNode> Children => _children.Value;

    internal ViewerHierarchyPage GetChildrenPage(int pageIndex, string? revealPath = null) =>
        ViewerHierarchyPage.Create(_snapshot, Entry.Path, pageIndex, revealPath);
}

internal sealed record ViewerAttributeSnapshot(
    string Name,
    string TypeName,
    bool? HasAuthoredValue,
    bool IsBlocked,
    ulong? TimeSampleCount,
    string TimeSamples,
    string Value,
    ViewerSplineSnapshot? Spline = null,
    UsdAttributePropertySnapshot? Inspection = null) : IUsdDetachedResult;

internal sealed record ViewerRelationshipSnapshot(
    string Name,
    string Targets,
    UsdRelationshipPropertySnapshot? Inspection = null) : IUsdDetachedResult;

internal sealed class ViewerVariantSetSnapshot : IUsdDetachedResult
{
    internal static IEqualityComparer<ViewerVariantSetSnapshot> ValueComparer { get; } =
        new Comparer();

    private readonly string[] _variantNames;

    private ViewerVariantSetSnapshot(
        string name,
        string[] variantNames,
        string? selection)
    {
        Name = name;
        Selection = selection;
        _variantNames = variantNames;
        VariantNames = Array.AsReadOnly(_variantNames);
    }

    internal string Name { get; }

    internal IReadOnlyList<string> VariantNames { get; }

    internal string? Selection { get; }

    internal static ViewerVariantSetSnapshot Create(
        string name,
        IEnumerable<string> variantNames,
        string? selection)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(variantNames);
        string[] detachedNames = variantNames.ToArray();
        for (int index = 0; index < detachedNames.Length; index++)
        {
            ArgumentNullException.ThrowIfNull(detachedNames[index], nameof(variantNames));
        }
        return new ViewerVariantSetSnapshot(name, detachedNames, selection);
    }

    public override string ToString() =>
        $"{Name}: selection={Selection ?? "<none>"}; variants={string.Join(", ", _variantNames)}";

    private sealed class Comparer : IEqualityComparer<ViewerVariantSetSnapshot>
    {
        public bool Equals(ViewerVariantSetSnapshot? x, ViewerVariantSetSnapshot? y) =>
            ReferenceEquals(x, y) ||
            (x is not null &&
             y is not null &&
             string.Equals(x.Name, y.Name, StringComparison.Ordinal) &&
             string.Equals(x.Selection, y.Selection, StringComparison.Ordinal) &&
             x._variantNames.SequenceEqual(y._variantNames, StringComparer.Ordinal));

        public int GetHashCode(ViewerVariantSetSnapshot obj)
        {
            ArgumentNullException.ThrowIfNull(obj);
            var hash = new HashCode();
            hash.Add(obj.Name, StringComparer.Ordinal);
            hash.Add(obj.Selection, StringComparer.Ordinal);
            foreach (string variantName in obj._variantNames)
            {
                hash.Add(variantName, StringComparer.Ordinal);
            }
            return hash.ToHashCode();
        }
    }
}

internal sealed record ViewerVariantSelectionOption(
    string DisplayName,
    string? Selection)
{
    internal static ViewerVariantSelectionOption[] Create(ViewerVariantSetSnapshot variantSet)
    {
        ArgumentNullException.ThrowIfNull(variantSet);
        var options = new ViewerVariantSelectionOption[variantSet.VariantNames.Count + 1];
        options[0] = new ViewerVariantSelectionOption("<no selection>", Selection: null);
        for (int index = 0; index < variantSet.VariantNames.Count; index++)
        {
            string name = variantSet.VariantNames[index];
            options[index + 1] = new ViewerVariantSelectionOption(
                string.IsNullOrEmpty(name) ? "<empty variant name>" : name,
                name);
        }
        return options;
    }

    public override string ToString() => DisplayName;
}

internal sealed record ViewerPayloadArcSnapshot(
    string AssetPath,
    string TargetPrimPath,
    string SourceLayerIdentifier) : IUsdDetachedResult
{
    internal static ViewerPayloadArcSnapshot[] Create(IReadOnlyList<UsdPayloadArc> payloadArcs)
    {
        ArgumentNullException.ThrowIfNull(payloadArcs);
        var snapshots = new ViewerPayloadArcSnapshot[payloadArcs.Count];
        for (int index = 0; index < snapshots.Length; index++)
        {
            UsdPayloadArc payloadArc = payloadArcs[index];
            snapshots[index] = new ViewerPayloadArcSnapshot(
                payloadArc.AssetPath,
                payloadArc.TargetPrimPath,
                payloadArc.SourceLayerIdentifier);
        }
        return snapshots;
    }
}

internal static class ViewerPayloadArcFormatter
{
    internal const int DefaultPathLimit = ViewerScalarFormatter.DefaultTextLimit;

    internal static string FormatAssetPath(
        string assetPath,
        int maximumLength = DefaultPathLimit)
    {
        ArgumentNullException.ThrowIfNull(assetPath);
        string display = string.IsNullOrEmpty(assetPath)
            ? "<internal payload; authored asset path is empty>"
            : IsRelativeIdentifier(assetPath)
                ? $"[relative authored asset path] {assetPath}"
                : $"[authored asset path] {assetPath}";
        return ViewerScalarFormatter.Bound(display, maximumLength);
    }

    internal static string FormatTargetPrimPath(
        string targetPrimPath,
        int maximumLength = DefaultPathLimit)
    {
        ArgumentNullException.ThrowIfNull(targetPrimPath);
        string display = string.IsNullOrEmpty(targetPrimPath)
            ? "<target layer default prim>"
            : targetPrimPath;
        return ViewerScalarFormatter.Bound(display, maximumLength);
    }

    internal static string FormatSourceLayerIdentifier(
        string sourceLayerIdentifier,
        int maximumLength = DefaultPathLimit)
    {
        ArgumentNullException.ThrowIfNull(sourceLayerIdentifier);
        string display = string.IsNullOrEmpty(sourceLayerIdentifier)
            ? "<missing source-layer identifier>"
            : sourceLayerIdentifier.StartsWith("anon:", StringComparison.OrdinalIgnoreCase)
                ? $"[anonymous source layer; process-local] {sourceLayerIdentifier}"
                : IsRelativeIdentifier(sourceLayerIdentifier)
                    ? $"[relative source-layer identifier] {sourceLayerIdentifier}"
                    : $"[source-layer identifier] {sourceLayerIdentifier}";
        return ViewerScalarFormatter.Bound(display, maximumLength);
    }

    private static bool IsRelativeIdentifier(string value)
    {
        if (value.StartsWith('/') ||
            value.StartsWith("\\\\", StringComparison.Ordinal) ||
            (value.Length >= 3 &&
             char.IsAsciiLetter(value[0]) &&
             value[1] == ':' &&
             value[2] is '\\' or '/'))
        {
            return false;
        }
        return !Uri.TryCreate(value, UriKind.Absolute, out _);
    }
}

internal sealed record ViewerPrimInspectorSnapshot(
    string Path,
    string TypeName,
    bool IsActive,
    bool IsLoaded,
    bool IsDefined,
    bool IsAbstract,
    bool IsInPrototype,
    UsdPrimSpecifier Specifier,
    bool IsInstance,
    bool IsInstanceable,
    bool IsPrototype,
    string? PrototypePath,
    bool IsImageable,
    bool IsCamera,
    UsdGeomVisibility? Visibility,
    UsdGeomPurpose? Purpose,
    string[] AppliedSchemas,
    ViewerVariantSetSnapshot[] VariantSets,
    ViewerPayloadArcSnapshot[] PayloadArcs,
    PcpPrimIndex Composition,
    ViewerAttributeSnapshot[] Attributes,
    ViewerRelationshipSnapshot[] Relationships,
    ViewerUnsupportedFeature[] UnsupportedFeatures,
    UsdPrimPropertySnapshot? PropertySnapshot = null) : IUsdDetachedResult;

internal static class ViewerHierarchyExpansionPolicy
{
    internal static bool ShouldMaterializeChildren(
        ViewerHierarchyEntry entry,
        int expandDepth,
        bool containsSelection)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.ChildCount == 0)
        {
            return false;
        }
        return containsSelection || entry.Depth < Math.Max(0, expandDepth);
    }
}

internal static class ViewerCompositionFormatter
{
    internal static string FormatSummary(PcpPrimIndex composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{composition.Nodes.Count} nodes; {composition.Errors.Count} errors");
    }

    internal static string FormatNode(PcpPrimIndexNode node, int index)
    {
        ArgumentNullException.ThrowIfNull(node);
        return ViewerScalarFormatter.Bound(
            string.Create(
                CultureInfo.InvariantCulture,
                $"#{index}: {node.ArcType}; parent={node.ParentIndex}; site={node.SitePath}; " +
                $"intro={node.IntroPath}; specs={node.HasSpecs}; contributes={node.CanContributeSpecs}; " +
                $"layers={node.LayerIdentifiers.Count}"),
            ViewerScalarFormatter.DefaultTextLimit);
    }
}

[Flags]
internal enum ViewerLayerRole
{
    Local = 1,
    Root = 2,
    Session = 4
}

internal sealed record ViewerLayerSnapshot(
    string Identifier,
    int StrengthIndex,
    ViewerLayerRole Role,
    bool IsEditTarget,
    bool IsMuted) : IUsdDetachedResult
{
    internal bool IsRoot => (Role & ViewerLayerRole.Root) != 0;

    internal bool IsSession => (Role & ViewerLayerRole.Session) != 0;

    internal bool CanChangeMuted => Role == ViewerLayerRole.Local;
}

internal sealed class ViewerLayerStackSnapshot : IUsdDetachedResult
{
    private readonly ViewerLayerSnapshot[] _layers;
    private readonly string[] _localLayerIdentifiers;

    private ViewerLayerStackSnapshot(
        string rootLayerIdentifier,
        string sessionLayerIdentifier,
        string editTargetIdentifier,
        ViewerLayerSnapshot[] layers)
    {
        RootLayerIdentifier = rootLayerIdentifier;
        SessionLayerIdentifier = sessionLayerIdentifier;
        EditTargetIdentifier = editTargetIdentifier;
        _layers = (ViewerLayerSnapshot[])layers.Clone();
        _localLayerIdentifiers = _layers.Select(layer => layer.Identifier).ToArray();
        Layers = Array.AsReadOnly(_layers);
        LocalLayerIdentifiers = Array.AsReadOnly(_localLayerIdentifiers);
    }

    internal string RootLayerIdentifier { get; }

    internal string SessionLayerIdentifier { get; }

    internal string EditTargetIdentifier { get; }

    internal IReadOnlyList<string> LocalLayerIdentifiers { get; }

    internal IReadOnlyList<ViewerLayerSnapshot> Layers { get; }

    internal static ViewerLayerStackSnapshot Empty { get; } = Create(
        string.Empty,
        string.Empty,
        string.Empty,
        [],
        []);

    internal static ViewerLayerStackSnapshot Create(
        string rootLayerIdentifier,
        string sessionLayerIdentifier,
        string editTargetIdentifier,
        IEnumerable<string> localLayerIdentifiers,
        IEnumerable<string> mutedLayerIdentifiers)
    {
        ArgumentNullException.ThrowIfNull(rootLayerIdentifier);
        ArgumentNullException.ThrowIfNull(sessionLayerIdentifier);
        ArgumentNullException.ThrowIfNull(editTargetIdentifier);
        ArgumentNullException.ThrowIfNull(localLayerIdentifiers);
        ArgumentNullException.ThrowIfNull(mutedLayerIdentifiers);

        var muted = new HashSet<string>(mutedLayerIdentifiers, StringComparer.Ordinal);
        string[] identifiers = localLayerIdentifiers
            .Where(identifier => !string.IsNullOrWhiteSpace(identifier))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var layers = new ViewerLayerSnapshot[identifiers.Length];
        for (int index = 0; index < identifiers.Length; index++)
        {
            string identifier = identifiers[index];
            ViewerLayerRole role = ViewerLayerRole.Local;
            if (string.Equals(identifier, rootLayerIdentifier, StringComparison.Ordinal))
            {
                role |= ViewerLayerRole.Root;
            }
            if (string.Equals(identifier, sessionLayerIdentifier, StringComparison.Ordinal))
            {
                role |= ViewerLayerRole.Session;
            }
            layers[index] = new ViewerLayerSnapshot(
                identifier,
                index,
                role,
                string.Equals(identifier, editTargetIdentifier, StringComparison.Ordinal),
                muted.Contains(identifier));
        }
        return new ViewerLayerStackSnapshot(
            rootLayerIdentifier,
            sessionLayerIdentifier,
            editTargetIdentifier,
            layers);
    }

    internal static string[] PreserveMutedOrder(
        IEnumerable<string> currentIdentifiers,
        ViewerLayerStackSnapshot? previous,
        ISet<string> mutedIdentifiers)
    {
        ArgumentNullException.ThrowIfNull(currentIdentifiers);
        ArgumentNullException.ThrowIfNull(mutedIdentifiers);
        var result = currentIdentifiers
            .Where(identifier => !string.IsNullOrWhiteSpace(identifier))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (previous is null)
        {
            return result.ToArray();
        }

        var present = new HashSet<string>(result, StringComparer.Ordinal);
        foreach (ViewerLayerSnapshot layer in previous.Layers)
        {
            if (present.Contains(layer.Identifier) || !mutedIdentifiers.Contains(layer.Identifier))
            {
                continue;
            }
            result.Insert(Math.Min(layer.StrengthIndex, result.Count), layer.Identifier);
            present.Add(layer.Identifier);
        }
        return result.ToArray();
    }
}

internal sealed record ViewerStageStatisticsSnapshot(
    string RootLayerIdentifier,
    string SessionLayerIdentifier,
    string DefaultPrimPath,
    int PrimCount,
    int MeshCount,
    long CurveVertexCount,
    long MeshVertexCount,
    long FaceCount,
    int RootPrimCount,
    int LeafPrimCount,
    int MaximumDepth,
    UsdBounds3d WorldBounds,
    UsdOrientedBounds3d OrientedWorldBounds,
    TimeSpan BoundsQueryDuration) : IUsdDetachedResult
{
    internal static ViewerStageStatisticsSnapshot Empty { get; } = new(
        string.Empty,
        string.Empty,
        string.Empty,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        UsdBounds3d.Empty,
        UsdOrientedBounds3d.Empty,
        TimeSpan.Zero);
}

internal sealed record ViewerDocumentSnapshot(
    ViewerHierarchySnapshot Hierarchy,
    ViewerStageTimingSnapshot Timing,
    ViewerLayerStackSnapshot Layers,
    ViewerStageStatisticsSnapshot Statistics,
    ViewerPrimInspectorSnapshot? SelectedPrim,
    ViewerStageCameraMenuEntry[] StageCameras = null!,
    string? PrimaryCameraPath = null) : IUsdDetachedResult;

internal static partial class ViewerStageSnapshotBuilder
{
    /// <summary>
    /// The most authored Ts splines one inspector snapshot reads and evaluates.
    /// </summary>
    /// <remarks>
    /// This bounds native work inside the scheduler callback and is distinct
    /// from the Value tab's knot-line budget, which bounds the visual tree
    /// afterwards. One is about what the stage thread does; the other is about
    /// what the UI thread builds.
    /// </remarks>
    internal const int MaxReadSplinesPerInspector = 16;

    internal static ViewerDocumentSnapshot BuildDocument(UsdStage stage)
        => BuildDocument(stage, previousLayers: null, selectedPrimPath: null);

    internal static ViewerDocumentSnapshot BuildDocument(
        UsdStage stage,
        ViewerLayerStackSnapshot? previousLayers)
        => BuildDocument(stage, previousLayers, selectedPrimPath: null);

    internal static ViewerDocumentSnapshot BuildDocument(
        UsdStage stage,
        ViewerLayerStackSnapshot? previousLayers,
        string? selectedPrimPath,
        double? selectedPrimTimeCode = null)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ViewerHierarchySnapshot hierarchy = BuildHierarchy(stage);
        ViewerPrimInspectorSnapshot? selectedPrim =
            !string.IsNullOrWhiteSpace(selectedPrimPath) &&
            hierarchy.Contains(selectedPrimPath) &&
            stage.HasPrim(selectedPrimPath)
                ? BuildInspector(stage, selectedPrimPath, selectedPrimTimeCode)
                : null;
        return new ViewerDocumentSnapshot(
            hierarchy,
            ViewerStageTimingSnapshot.Create(
                stage.StartTimeCode,
                stage.EndTimeCode,
                stage.FramesPerSecond,
                stage.TimeCodesPerSecond),
            BuildLayerStack(stage, previousLayers),
            BuildStatistics(stage, hierarchy),
            selectedPrim,
            ViewerStageCameraDiscovery.ListCameras(stage),
            ViewerStageCameraDiscovery.GetPrimaryCameraPath(stage));
    }

    internal static ViewerHierarchySnapshot BuildHierarchy(UsdStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        return ViewerHierarchySnapshot.FromNative(stage.GetHierarchySnapshot(UsdHierarchyLimits.Viewer));
    }

    internal static ViewerPrimInspectorSnapshot BuildInspector(
        UsdStage stage, string primPath, double? timeCode = null)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        if (!stage.HasPrim(primPath))
        {
            throw new InvalidOperationException($"Prim '{primPath}' no longer exists.");
        }

        UsdPrim prim = stage.GetPrim(primPath);
        UsdPrimPropertySnapshot properties = stage.GetPrimPropertySnapshot(
            primPath, timeCode, UsdPropertyInspectionLimits.Viewer);
        (ViewerAttributeSnapshot[] attributeSnapshots, ViewerRelationshipSnapshot[] relationshipSnapshots) =
            BuildProperties(prim, properties);

        ViewerVariantSetSnapshot[] variantSets = BuildVariantSets(prim);
        ViewerPayloadArcSnapshot[] payloadArcs =
            ViewerPayloadArcSnapshot.Create(prim.GetPayloadArcs());
        PcpPrimIndex composition = prim.GetPrimIndex();
        bool isInstance = prim.IsInstance();
        UsdPrimClassification classification = prim.GetClassification();
        bool isPrototype = prim.IsPrototype();
        bool isImageable = UsdGeomImageable.TryWrap(prim, out UsdGeomImageable imageable);
        bool isCamera = UsdGeomCamera.TryWrap(prim, out _);
        var unsupported = new List<ViewerUnsupportedFeature>();
        UsdGeomVisibility? visibility = null;
        UsdGeomPurpose? purpose = null;
        if (isImageable)
        {
            visibility = imageable.GetVisibility();
            purpose = imageable.GetPurpose();
        }
        else
        {
            unsupported.Add(ViewerUnsupportedFeatureCatalog.PurposeVisibilityNotImageable);
        }

        return new ViewerPrimInspectorSnapshot(
            prim.Path,
            prim.TypeName,
            prim.IsActive(),
            prim.IsLoaded(),
            classification.IsDefined,
            classification.IsAbstract,
            classification.IsInPrototype,
            classification.Specifier,
            isInstance,
            prim.IsInstanceable(),
            isPrototype,
            isInstance ? prim.GetPrototypePath() : null,
            isImageable,
            isCamera,
            visibility,
            purpose,
            prim.GetAppliedSchemas(),
            variantSets,
            payloadArcs,
            composition,
            attributeSnapshots,
            relationshipSnapshots,
            unsupported.ToArray(),
            properties);
    }

    private static ViewerVariantSetSnapshot[] BuildVariantSets(UsdPrim prim)
    {
        string[] variantSetNames = prim.GetVariantSetNames();
        var variantSets = new ViewerVariantSetSnapshot[variantSetNames.Length];
        for (int index = 0; index < variantSetNames.Length; index++)
        {
            string variantSetName = variantSetNames[index];
            string selection = prim.GetVariantSelection(variantSetName);
            variantSets[index] = ViewerVariantSetSnapshot.Create(
                variantSetName,
                prim.GetVariantNames(variantSetName),
                string.IsNullOrEmpty(selection) ? null : selection);
        }
        return variantSets;
    }

    internal static string FormatTimeSamples(IReadOnlyList<double> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            return "<none>";
        }
        if (samples.Count <= 4)
        {
            return string.Join(
                ", ",
                samples.Select(sample => sample.ToString("G17", CultureInfo.InvariantCulture)));
        }

        int middle = samples.Count / 2;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{samples[0]:G17}, {samples[middle]:G17}, {samples[^1]:G17} ({samples.Count} samples)");
    }

    private static ViewerStageStatisticsSnapshot BuildStatistics(
        UsdStage stage,
        ViewerHierarchySnapshot hierarchy)
    {
        StageStatisticsAnalysis analysis = StageStatisticsAnalyzer.Analyze(
            stage,
            hierarchy.Entries.Select(static entry => entry.Path),
            StageStatisticsAnalysisLimits.Viewer,
            CancellationToken.None);
        return new ViewerStageStatisticsSnapshot(
            analysis.RootLayerIdentifier,
            analysis.SessionLayerIdentifier,
            analysis.DefaultPrimPath,
            analysis.PrimCount,
            analysis.MeshCount,
            analysis.CurveVertexCount,
            analysis.MeshVertexCount,
            analysis.FaceCount,
            analysis.RootPrimCount,
            analysis.LeafPrimCount,
            analysis.MaximumDepth,
            analysis.WorldBounds,
            analysis.OrientedWorldBounds,
            analysis.BoundsQueryDuration);
    }

    private static ViewerLayerStackSnapshot BuildLayerStack(
        UsdStage stage,
        ViewerLayerStackSnapshot? previousLayers)
    {
        string rootIdentifier = stage.RootLayerIdentifier;
        string sessionIdentifier = stage.SessionLayerIdentifier;
        string editTargetIdentifier = stage.EditTargetLayerIdentifier;
        string[] currentIdentifiers = stage.GetLayerStackIdentifiers();
        var mutedIdentifiers = new HashSet<string>(StringComparer.Ordinal);
        if (previousLayers is not null)
        {
            foreach (ViewerLayerSnapshot layer in previousLayers.Layers)
            {
                if (!currentIdentifiers.Contains(layer.Identifier, StringComparer.Ordinal) &&
                    layer.IsMuted)
                {
                    mutedIdentifiers.Add(layer.Identifier);
                }
            }
        }
        string[] identifiers = ViewerLayerStackSnapshot.PreserveMutedOrder(
            currentIdentifiers,
            previousLayers,
            mutedIdentifiers);
        return ViewerLayerStackSnapshot.Create(
            rootIdentifier,
            sessionIdentifier,
            editTargetIdentifier,
            identifiers,
            mutedIdentifiers);
    }

    /// <summary>
    /// Copies an attribute's authored Ts spline, plus a bounded evaluated
    /// preview, while the builder still holds stage access.
    /// </summary>
    /// <remarks>
    /// Every failure the native runtime can report here belongs to one
    /// attribute - an unsupported value type, a spline the runtime refuses to
    /// read, a time it refuses to evaluate. Letting it escape would fail the
    /// whole inspector snapshot and leave the prim uninspectable, so it is
    /// projected as an unreadable spline instead.
    ///
    /// Reading one spline costs two native calls plus one evaluation per
    /// preview sample, and nothing bounds how many splined attributes a prim
    /// may carry, so the scheduler callback would grow with the prim. The
    /// budget bounds that native work per inspector; the attributes past it
    /// are projected as deliberately unread rather than dropped, so the Value
    /// tab can say which ones were skipped and why.
    /// </remarks>
    private static ViewerSplineSnapshot? BuildSpline(UsdPrim prim, string attributeName, ref int budget)
    {
        if (budget <= 0)
        {
            return ViewerSplineSnapshot.CreateNotRead(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"this prim's budget of {MaxReadSplinesPerInspector} read spline(s) " +
                    $"was already spent"));
        }
        budget--;
        try
        {
            UsdAttribute attribute = prim.GetAttribute(attributeName);
            if (!attribute.HasSpline())
            {
                return null;
            }
            using TsSpline spline = attribute.GetSpline();
            TsSplineData data = spline.GetData();
            double[] times = ViewerSplineSnapshot.GetSampleTimes(data);
            var samples = new ViewerSplineSampleSnapshot[times.Length];
            for (int index = 0; index < times.Length; index++)
            {
                samples[index] = new ViewerSplineSampleSnapshot(
                    times[index],
                    spline.Evaluate(times[index]));
            }
            return ViewerSplineSnapshot.Create(data, samples);
        }
        catch (OpenUsdNativeException exception)
        {
            return ViewerSplineSnapshot.CreateUnreadable(exception.Message);
        }
    }
}

internal static class ViewerScalarFormatter
{
    internal const int DefaultTextLimit = 256;

    internal static string Format(UsdScalarValue value) =>
        Bound(
            value.Kind switch
            {
                UsdScalarKind.Invalid => "<invalid>",
                UsdScalarKind.Boolean => value.BoolValue ? "true" : "false",
                UsdScalarKind.Signed64 => value.Int64Value.ToString(CultureInfo.InvariantCulture),
                UsdScalarKind.Number => value.DoubleValue.ToString("G17", CultureInfo.InvariantCulture),
                UsdScalarKind.Text => value.StringValue,
                UsdScalarKind.Token => value.TokenValue,
                UsdScalarKind.Vector3 => FormatVector(value.Vec3fValue),
                UsdScalarKind.Color3 => FormatVector(value.Color3fValue),
                UsdScalarKind.Matrix4d => FormatMatrix(value.Matrix4dValue),
                UsdScalarKind.Int32Array => "<int[] array>",
                UsdScalarKind.FloatArray => "<float[] array>",
                UsdScalarKind.DoubleArray => "<double[] array>",
                UsdScalarKind.Vec2fArray => "<float2[] array>",
                UsdScalarKind.Vec3fArray => "<float3[] array>",
                _ => "<unsupported value>"
            },
            DefaultTextLimit);

    internal static string Bound(string value, int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumLength, 4);
        if (value.Length <= maximumLength)
        {
            return value;
        }
        int prefixLength = maximumLength - 3;
        if (char.IsHighSurrogate(value[prefixLength - 1]) && char.IsLowSurrogate(value[prefixLength]))
        {
            prefixLength--;
        }
        return string.Concat(value.AsSpan(0, prefixLength), "...");
    }

    private static string FormatVector(UsdVec3f value) =>
        FormattableString.Invariant($"({value.X:G9}, {value.Y:G9}, {value.Z:G9})");

    private static string FormatMatrix(UsdMatrix4d value)
    {
        var text = new System.Text.StringBuilder();
        text.Append('[');
        for (int row = 0; row < 4; row++)
        {
            if (row != 0)
            {
                text.Append("; ");
            }
            for (int column = 0; column < 4; column++)
            {
                if (column != 0)
                {
                    text.Append(", ");
                }
                text.Append(value[row, column].ToString("G17", CultureInfo.InvariantCulture));
            }
        }
        return text.Append(']').ToString();
    }
}
