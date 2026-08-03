// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Skel;

/// <summary>Specifies how joint-influence tuples are interpolated.</summary>
public enum UsdSkelInterpolation
{
    /// <summary>One influence tuple applies to the whole primitive.</summary>
    Constant = 0,
    /// <summary>One influence tuple applies to each point.</summary>
    Vertex = 1
}

/// <summary>Identifies the UsdSkel skinning method.</summary>
public enum UsdSkelSkinningMethod
{
    /// <summary>Classic linear blend skinning.</summary>
    ClassicLinear = 0,
    /// <summary>Dual-quaternion skinning.</summary>
    DualQuaternion = 1
}

/// <summary>Contains joint indices, matching weights, and their primvar shape metadata.</summary>
public sealed record UsdSkelJointInfluences(
    int[] JointIndices,
    float[] JointWeights,
    int ElementSize,
    UsdSkelInterpolation Interpolation) : IUsdDetachedResult
{
    /// <inheritdoc />
    public bool Equals(UsdSkelJointInfluences? other) =>
        other is not null &&
        JointIndices.SequenceEqual(other.JointIndices) &&
        JointWeights.SequenceEqual(other.JointWeights) &&
        ElementSize == other.ElementSize &&
        Interpolation == other.Interpolation;

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(
            RecordCollectionFormatting.SequenceHashCode(JointIndices),
            RecordCollectionFormatting.SequenceHashCode(JointWeights),
            ElementSize,
            Interpolation);

    /// <inheritdoc />
    public override string ToString() =>
        $"{nameof(UsdSkelJointInfluences)} {{ {nameof(JointIndices)} = " +
        $"{RecordCollectionFormatting.FormatSequence(JointIndices)}, {nameof(JointWeights)} = " +
        $"{RecordCollectionFormatting.FormatSequence(JointWeights)}, {nameof(ElementSize)} = {ElementSize}, " +
        $"{nameof(Interpolation)} = {Interpolation} }}";
}

/// <summary>Contains one blend-shape inbetween payload.</summary>
public sealed record UsdSkelBlendShapeInbetween(
    string Name,
    float Weight,
    UsdVec3f[] Offsets,
    UsdVec3f[] NormalOffsets) : IUsdDetachedResult
{
    /// <inheritdoc />
    public bool Equals(UsdSkelBlendShapeInbetween? other) =>
        other is not null &&
        Name == other.Name &&
        Weight.Equals(other.Weight) &&
        Offsets.SequenceEqual(other.Offsets) &&
        NormalOffsets.SequenceEqual(other.NormalOffsets);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(
            Name,
            Weight,
            RecordCollectionFormatting.SequenceHashCode(Offsets),
            RecordCollectionFormatting.SequenceHashCode(NormalOffsets));

    /// <inheritdoc />
    public override string ToString() =>
        $"{nameof(UsdSkelBlendShapeInbetween)} {{ {nameof(Name)} = {Name}, {nameof(Weight)} = {Weight}, " +
        $"{nameof(Offsets)} = {RecordCollectionFormatting.FormatSequence(Offsets)}, " +
        $"{nameof(NormalOffsets)} = {RecordCollectionFormatting.FormatSequence(NormalOffsets)} }}";
}
