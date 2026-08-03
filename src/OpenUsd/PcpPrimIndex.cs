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
    IReadOnlyList<string> LayerIdentifiers)
{
    /// <inheritdoc />
    public bool Equals(PcpPrimIndexNode? other) =>
        other is not null &&
        ParentIndex == other.ParentIndex &&
        ArcType == other.ArcType &&
        IsCulled == other.IsCulled &&
        IsInert == other.IsInert &&
        IsDueToAncestor == other.IsDueToAncestor &&
        HasSpecs == other.HasSpecs &&
        CanContributeSpecs == other.CanContributeSpecs &&
        NamespaceDepth == other.NamespaceDepth &&
        DepthBelowIntroduction == other.DepthBelowIntroduction &&
        SiblingIndexAtOrigin == other.SiblingIndexAtOrigin &&
        SitePath == other.SitePath &&
        IntroPath == other.IntroPath &&
        PathAtIntroduction == other.PathAtIntroduction &&
        PathAtOriginRootIntroduction == other.PathAtOriginRootIntroduction &&
        LayerStackIdentifier == other.LayerStackIdentifier &&
        LayerIdentifiers.SequenceEqual(other.LayerIdentifiers);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(ParentIndex);
        hash.Add(ArcType);
        hash.Add(IsCulled);
        hash.Add(IsInert);
        hash.Add(IsDueToAncestor);
        hash.Add(HasSpecs);
        hash.Add(CanContributeSpecs);
        hash.Add(NamespaceDepth);
        hash.Add(DepthBelowIntroduction);
        hash.Add(SiblingIndexAtOrigin);
        hash.Add(SitePath);
        hash.Add(IntroPath);
        hash.Add(PathAtIntroduction);
        hash.Add(PathAtOriginRootIntroduction);
        hash.Add(LayerStackIdentifier);
        hash.Add(RecordCollectionFormatting.SequenceHashCode(LayerIdentifiers));
        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"{nameof(PcpPrimIndexNode)} {{ {nameof(ParentIndex)} = {ParentIndex}, " +
        $"{nameof(ArcType)} = {ArcType}, {nameof(IsCulled)} = {IsCulled}, " +
        $"{nameof(IsInert)} = {IsInert}, {nameof(IsDueToAncestor)} = {IsDueToAncestor}, " +
        $"{nameof(HasSpecs)} = {HasSpecs}, {nameof(CanContributeSpecs)} = {CanContributeSpecs}, " +
        $"{nameof(NamespaceDepth)} = {NamespaceDepth}, " +
        $"{nameof(DepthBelowIntroduction)} = {DepthBelowIntroduction}, " +
        $"{nameof(SiblingIndexAtOrigin)} = {SiblingIndexAtOrigin}, {nameof(SitePath)} = {SitePath}, " +
        $"{nameof(IntroPath)} = {IntroPath}, {nameof(PathAtIntroduction)} = {PathAtIntroduction}, " +
        $"{nameof(PathAtOriginRootIntroduction)} = {PathAtOriginRootIntroduction}, " +
        $"{nameof(LayerStackIdentifier)} = {LayerStackIdentifier}, " +
        $"{nameof(LayerIdentifiers)} = {RecordCollectionFormatting.FormatSequence(LayerIdentifiers)} }}";
}

/// <summary>A detached Pcp prim-index snapshot for UI inspection.</summary>
public sealed record PcpPrimIndex(
    IReadOnlyList<PcpPrimIndexNode> Nodes,
    IReadOnlyList<string> Errors)
{
    /// <inheritdoc />
    public bool Equals(PcpPrimIndex? other) =>
        other is not null &&
        Nodes.SequenceEqual(other.Nodes) &&
        Errors.SequenceEqual(other.Errors);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(
            RecordCollectionFormatting.SequenceHashCode(Nodes),
            RecordCollectionFormatting.SequenceHashCode(Errors));

    /// <inheritdoc />
    public override string ToString() =>
        $"{nameof(PcpPrimIndex)} {{ {nameof(Nodes)} = " +
        $"{RecordCollectionFormatting.FormatSequence(Nodes)}, {nameof(Errors)} = " +
        $"{RecordCollectionFormatting.FormatSequence(Errors)} }}";
}
