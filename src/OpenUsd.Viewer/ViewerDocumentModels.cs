// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Globalization;
using OpenUsd.Geom;
using OpenUsd.Interop;

namespace OpenUsd.Viewer;

internal sealed record ViewerHierarchyEntry(
    string Path,
    string Name,
    string TypeName,
    string? ParentPath,
    int Depth,
    int ChildCount) : IUsdDetachedResult;

internal sealed record ViewerHierarchySourceEntry(
    string Path,
    string TypeName);

internal sealed record ViewerHierarchyFilter(
    string? NameQuery,
    string? TypeQuery);

internal sealed record ViewerHierarchySnapshot : IUsdDetachedResult
{
    private readonly Dictionary<string, ViewerHierarchyEntry> _byPath;
    private readonly Dictionary<string, ViewerHierarchyEntry[]> _children;

    private ViewerHierarchySnapshot(ViewerHierarchyEntry[] entries)
    {
        Entries = entries;
        _byPath = entries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        _children = entries
            .GroupBy(entry => entry.ParentPath ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray(),
                StringComparer.Ordinal);
    }

    internal ViewerHierarchyEntry[] Entries { get; }

    internal static ViewerHierarchySnapshot Empty { get; } = new([]);

    internal static ViewerHierarchySnapshot Build(IEnumerable<string> traversalPaths)
    {
        ArgumentNullException.ThrowIfNull(traversalPaths);
        return Build(traversalPaths.Select(path => new ViewerHierarchySourceEntry(path, string.Empty)));
    }

    internal static ViewerHierarchySnapshot Build(IEnumerable<ViewerHierarchySourceEntry> traversalEntries)
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
                childCounts.GetValueOrDefault(source.Path));
        }
        return new ViewerHierarchySnapshot(entries);
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
            string.IsNullOrWhiteSpace(filter.TypeQuery))
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
            .Select(entry => new ViewerHierarchySourceEntry(entry.Path, entry.TypeName)));
    }

    private static bool Matches(ViewerHierarchyEntry entry, ViewerHierarchyFilter filter)
    {
        bool nameMatches = string.IsNullOrWhiteSpace(filter.NameQuery) ||
            entry.Name.Contains(filter.NameQuery, StringComparison.OrdinalIgnoreCase) ||
            entry.Path.Contains(filter.NameQuery, StringComparison.OrdinalIgnoreCase);
        bool typeMatches = string.IsNullOrWhiteSpace(filter.TypeQuery) ||
            entry.TypeName.Contains(filter.TypeQuery, StringComparison.OrdinalIgnoreCase);
        return nameMatches && typeMatches;
    }

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
        Roots = snapshot.GetChildren(null)
            .Select(entry => new ViewerHierarchyTreeNode(snapshot, entry))
            .ToArray();
    }

    internal ViewerHierarchySnapshot Snapshot { get; }

    internal IReadOnlyList<ViewerHierarchyTreeNode> Roots { get; }
}

internal sealed class ViewerHierarchyTreeNode
{
    private readonly Lazy<IReadOnlyList<ViewerHierarchyTreeNode>> _children;

    internal ViewerHierarchyTreeNode(
        ViewerHierarchySnapshot snapshot,
        ViewerHierarchyEntry entry)
    {
        Entry = entry;
        _children = new Lazy<IReadOnlyList<ViewerHierarchyTreeNode>>(
            () => snapshot.GetChildren(entry.Path)
                .Select(child => new ViewerHierarchyTreeNode(snapshot, child))
                .ToArray());
    }

    internal ViewerHierarchyEntry Entry { get; }

    internal bool IsChildrenMaterialized => _children.IsValueCreated;

    internal IReadOnlyList<ViewerHierarchyTreeNode> Children => _children.Value;
}

internal sealed record ViewerAttributeSnapshot(
    string Name,
    string TypeName,
    bool HasAuthoredValue,
    bool IsBlocked,
    int TimeSampleCount,
    string Value) : IUsdDetachedResult;

internal sealed record ViewerRelationshipSnapshot(
    string Name,
    string Targets) : IUsdDetachedResult;

internal sealed class ViewerVariantSetSnapshot : IUsdDetachedResult
{
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
    ViewerUnsupportedFeature[] UnsupportedFeatures) : IUsdDetachedResult;

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
        TimeSpan.Zero);
}

internal sealed record ViewerDocumentSnapshot(
    ViewerHierarchySnapshot Hierarchy,
    ViewerStageTimingSnapshot Timing,
    ViewerLayerStackSnapshot Layers,
    ViewerStageStatisticsSnapshot Statistics,
    ViewerPrimInspectorSnapshot? SelectedPrim) : IUsdDetachedResult;

internal static class ViewerStageSnapshotBuilder
{
    internal static ViewerDocumentSnapshot BuildDocument(UsdStage stage)
        => BuildDocument(stage, previousLayers: null, selectedPrimPath: null);

    internal static ViewerDocumentSnapshot BuildDocument(
        UsdStage stage,
        ViewerLayerStackSnapshot? previousLayers)
        => BuildDocument(stage, previousLayers, selectedPrimPath: null);

    internal static ViewerDocumentSnapshot BuildDocument(
        UsdStage stage,
        ViewerLayerStackSnapshot? previousLayers,
        string? selectedPrimPath)
    {
        ArgumentNullException.ThrowIfNull(stage);
        IReadOnlyList<UsdPrim> prims = stage.Traverse();
        ViewerHierarchySnapshot hierarchy = BuildHierarchy(prims);
        ViewerPrimInspectorSnapshot? selectedPrim =
            !string.IsNullOrWhiteSpace(selectedPrimPath) &&
            hierarchy.Contains(selectedPrimPath) &&
            stage.HasPrim(selectedPrimPath)
                ? BuildInspector(stage, selectedPrimPath)
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
            selectedPrim);
    }

    internal static ViewerHierarchySnapshot BuildHierarchy(UsdStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        return BuildHierarchy(stage.Traverse());
    }

    private static ViewerHierarchySnapshot BuildHierarchy(IReadOnlyList<UsdPrim> prims)
    {
        var entries = new ViewerHierarchySourceEntry[prims.Count];
        for (int index = 0; index < prims.Count; index++)
        {
            entries[index] = new ViewerHierarchySourceEntry(prims[index].Path, prims[index].TypeName);
        }
        return ViewerHierarchySnapshot.Build(entries);
    }

    internal static ViewerPrimInspectorSnapshot BuildInspector(UsdStage stage, string primPath)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        if (!stage.HasPrim(primPath))
        {
            throw new InvalidOperationException($"Prim '{primPath}' no longer exists.");
        }

        UsdPrim prim = stage.GetPrim(primPath);
        IReadOnlyList<UsdAttribute> attributes = prim.GetAttributes();
        var attributeSnapshots = new ViewerAttributeSnapshot[attributes.Count];
        for (int index = 0; index < attributes.Count; index++)
        {
            UsdAttribute attribute = attributes[index];
            string typeName = attribute.TypeName;
            UsdAttributeValueState state = attribute.GetValueState();
            int sampleCount = attribute.GetTimeSamples().Length;
            attributeSnapshots[index] = new ViewerAttributeSnapshot(
                attribute.Name,
                typeName,
                state.HasAuthoredValueOpinion,
                state.IsBlocked,
                sampleCount,
                GetDisplayValue(attribute, typeName, state));
        }

        IReadOnlyList<UsdRelationship> relationships = prim.GetRelationships();
        var relationshipSnapshots = new ViewerRelationshipSnapshot[relationships.Count];
        for (int index = 0; index < relationships.Count; index++)
        {
            UsdRelationship relationship = relationships[index];
            relationshipSnapshots[index] = new ViewerRelationshipSnapshot(
                relationship.Name,
                ViewerScalarFormatter.Bound(
                    string.Join(", ", relationship.GetTargets()),
                    ViewerScalarFormatter.DefaultTextLimit));
        }

        ViewerVariantSetSnapshot[] variantSets = BuildVariantSets(prim);
        ViewerPayloadArcSnapshot[] payloadArcs =
            ViewerPayloadArcSnapshot.Create(prim.GetPayloadArcs());
        PcpPrimIndex composition = prim.GetPrimIndex();
        bool isInstance = prim.IsInstance();
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
            unsupported.ToArray());
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

    private static ViewerStageStatisticsSnapshot BuildStatistics(
        UsdStage stage,
        ViewerHierarchySnapshot hierarchy)
    {
        int meshCount = 0;
        long curveVertexCount = 0;
        long meshVertexCount = 0;
        long faceCount = 0;
        foreach (ViewerHierarchyEntry entry in hierarchy.Entries)
        {
            UsdPrim prim = stage.GetPrim(entry.Path);
            if (UsdGeomMesh.TryWrap(prim, out UsdGeomMesh mesh))
            {
                meshCount++;
                meshVertexCount += mesh.GetPoints().LongLength;
                faceCount += mesh.GetFaceVertexCounts().LongLength;
            }
            else if (UsdGeomBasisCurves.TryWrap(prim, out UsdGeomBasisCurves basisCurves))
            {
                curveVertexCount += basisCurves.GetPoints().LongLength;
            }
            else if (UsdGeomHermiteCurves.TryWrap(prim, out UsdGeomHermiteCurves hermiteCurves))
            {
                curveVertexCount += hermiteCurves.GetPoints().LongLength;
            }
            else if (UsdGeomNurbsCurves.TryWrap(prim, out UsdGeomNurbsCurves nurbsCurves))
            {
                curveVertexCount += nurbsCurves.GetPoints().LongLength;
            }
        }

        Stopwatch boundsTimer = Stopwatch.StartNew();
        UsdBounds3d worldBounds = stage.GetWorldBounds(
            timeCode: stage.StartTimeCode,
            purposeMask: UsdGeomPurposeMask.All);
        boundsTimer.Stop();
        string defaultPrimPath;
        try
        {
            defaultPrimPath = stage.GetDefaultPrim().Path;
        }
        catch (OpenUsdNativeException exception)
            when (exception.Status == OpenUsdNativeStatus.NotFound)
        {
            defaultPrimPath = string.Empty;
        }
        return new ViewerStageStatisticsSnapshot(
            stage.RootLayerIdentifier,
            stage.SessionLayerIdentifier,
            defaultPrimPath,
            hierarchy.Entries.Length,
            meshCount,
            curveVertexCount,
            meshVertexCount,
            faceCount,
            hierarchy.Entries.Count(entry => entry.ParentPath is null),
            hierarchy.Entries.Count(entry => entry.ChildCount == 0),
            hierarchy.Entries.Length == 0
                ? 0
                : hierarchy.Entries.Max(entry => entry.Depth),
            worldBounds,
            boundsTimer.Elapsed);
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

    private static string GetDisplayValue(
        UsdAttribute attribute,
        string typeName,
        UsdAttributeValueState state)
    {
        if (state.IsBlocked)
        {
            return "<blocked>";
        }
        if (typeName.EndsWith("[]", StringComparison.Ordinal))
        {
            return $"<{typeName} array>";
        }
        try
        {
            return ViewerScalarFormatter.Format(attribute.GetValue());
        }
        catch (OpenUsdNativeException exception)
            when ((exception.Status == OpenUsdNativeStatus.InvalidArgument &&
                   exception.Message.StartsWith(
                       "The attribute type is not a supported scalar:",
                       StringComparison.Ordinal)) ||
                  (exception.Status == OpenUsdNativeStatus.NotFound &&
                   string.Equals(
                       exception.Message,
                       "The attribute has no readable scalar value.",
                       StringComparison.Ordinal)))
        {
            return "<unsupported value>";
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
        return value.Length <= maximumLength
            ? value
            : string.Concat(value.AsSpan(0, maximumLength - 3), "...");
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
