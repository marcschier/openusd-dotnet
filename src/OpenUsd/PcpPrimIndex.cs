// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd;

/// <summary>Identifies the Pcp composition arc that introduced a prim-index node.</summary>
public enum PcpArcType
{
    /// <summary>The root node for the inspected prim.</summary>
    Root = 0,
    /// <summary>An inherit arc.</summary>
    Inherit = 1,
    /// <summary>A variant-selection arc.</summary>
    Variant = 2,
    /// <summary>A relocate arc.</summary>
    Relocate = 3,
    /// <summary>A reference arc.</summary>
    Reference = 4,
    /// <summary>A payload arc.</summary>
    Payload = 5,
    /// <summary>A specialize arc.</summary>
    Specialize = 6
}

/// <summary>A detached record for one node in a Pcp prim index.</summary>
public sealed record PcpPrimIndexNode(
    int ParentIndex,
    PcpArcType ArcType,
    bool IsCulled,
    bool IsInert,
    bool IsDueToAncestor,
    bool HasSpecs,
    bool CanContributeSpecs,
    int NamespaceDepth,
    int DepthBelowIntroduction,
    int SiblingIndexAtOrigin,
    string SitePath,
    string IntroPath,
    string PathAtIntroduction,
    string PathAtOriginRootIntroduction,
    string LayerStackIdentifier,
    IReadOnlyList<string> LayerIdentifiers);

/// <summary>A detached Pcp prim-index snapshot for UI inspection.</summary>
public sealed record PcpPrimIndex(
    IReadOnlyList<PcpPrimIndexNode> Nodes,
    IReadOnlyList<string> Errors);
