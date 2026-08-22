// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp;

internal sealed record CompositionLayerSnapshot
{
    public CompositionLayerSnapshot(
        string identifier,
        bool isMuted,
        bool isAnonymous,
        int primSpecCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentOutOfRangeException.ThrowIfNegative(primSpecCount);
        Identifier = identifier;
        IsMuted = isMuted;
        IsAnonymous = isAnonymous;
        PrimSpecCount = primSpecCount;
    }

    public string Identifier { get; }

    public bool IsMuted { get; }

    public bool IsAnonymous { get; }

    public int PrimSpecCount { get; }
}

internal sealed record CompositionPcpNodeSnapshot
{
    public CompositionPcpNodeSnapshot(
        string path,
        string arcType,
        int depth,
        bool hasSpecs,
        bool isDueToAncestor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(arcType);
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        Path = path;
        ArcType = arcType;
        Depth = depth;
        HasSpecs = hasSpecs;
        IsDueToAncestor = isDueToAncestor;
    }

    public string Path { get; }

    public string ArcType { get; }

    public int Depth { get; }

    public bool HasSpecs { get; }

    public bool IsDueToAncestor { get; }
}

internal sealed record CompositionSnapshot
{
    public CompositionSnapshot(
        IEnumerable<CompositionLayerSnapshot>? layers = null,
        IEnumerable<CompositionPcpNodeSnapshot>? pcpNodes = null)
    {
        CompositionLayerSnapshot[] detachedLayers = (layers ?? []).ToArray();
        CompositionPcpNodeSnapshot[] detachedNodes = (pcpNodes ?? []).ToArray();
        if (detachedLayers.Any(static layer => layer is null))
        {
            throw new ArgumentException(
                "Composition layers cannot contain null values.",
                nameof(layers));
        }

        if (detachedNodes.Any(static node => node is null))
        {
            throw new ArgumentException(
                "Pcp nodes cannot contain null values.",
                nameof(pcpNodes));
        }

        Layers = Array.AsReadOnly(
            detachedLayers
                .OrderBy(static layer => layer.Identifier, StringComparer.Ordinal)
                .ToArray());
        PcpNodes = Array.AsReadOnly(
            detachedNodes
                .OrderBy(static node => node.Path, StringComparer.Ordinal)
                .ThenBy(static node => node.ArcType, StringComparer.Ordinal)
                .ThenBy(static node => node.Depth)
                .ToArray());
    }

    public IReadOnlyList<CompositionLayerSnapshot> Layers { get; }

    public IReadOnlyList<CompositionPcpNodeSnapshot> PcpNodes { get; }
}
