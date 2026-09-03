// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// The bounds the ABI v20 deformation block declares. They are stated here as
/// well as in the native header because a consumer allocates against them
/// before it evaluates anything, and an allocation sized from an unchecked wire
/// count is the failure the bounds exist to prevent.
/// </summary>
public static class SilkDeformationLimits
{
    /// <summary>Gets the largest joint palette one rig may carry.</summary>
    public const int MaximumJoints = 256;

    /// <summary>Gets the fixed influence stream width one rig may carry.</summary>
    public const int MaximumInfluences = 8;

    /// <summary>Gets the largest number of sub-shapes active at one time.</summary>
    public const int MaximumBlendRanges = 64;

    /// <summary>Gets the largest sparse blend delta table one rig may carry.</summary>
    public const int MaximumBlendDeltas = 1048576;

    /// <summary>Gets the byte ceiling of one deformation block.</summary>
    public const int MaximumBytes = 67108864;

    /// <summary>
    /// Gets the largest relative component difference between an evaluated rig
    /// and the CPU-resolved deformation published beside it. The difference is
    /// scaled by the larger of one and the resolved magnitude, because the two
    /// evaluations run the same arithmetic in a different order.
    /// </summary>
    public const float VerifyTolerance = 1.0e-4f;

    internal const SilkDeformationOptions KnownOptions =
        SilkDeformationOptions.BindNormals |
        SilkDeformationOptions.BlendNormalOffsets;

    internal const SilkDeformationUnsupportedFeatures KnownUnsupportedFeatures =
        SilkDeformationUnsupportedFeatures.JointBudget |
        SilkDeformationUnsupportedFeatures.InfluenceBudget |
        SilkDeformationUnsupportedFeatures.BlendBudget |
        SilkDeformationUnsupportedFeatures.ByteBudget |
        SilkDeformationUnsupportedFeatures.SkinningMethod |
        SilkDeformationUnsupportedFeatures.Geometry |
        SilkDeformationUnsupportedFeatures.Normals |
        SilkDeformationUnsupportedFeatures.Unverified;
}

/// <summary>Optional sections of a published deformation block.</summary>
[Flags]
public enum SilkDeformationOptions : uint
{
    /// <summary>The rig carries positions only.</summary>
    None = 0,

    /// <summary>The rig carries authored per-point bind normals.</summary>
    BindNormals = 1,

    /// <summary>At least one blend delta carries an authored normal offset.</summary>
    BlendNormalOffsets = 2
}

/// <summary>
/// Why a deformed prim published no bounded rig. The record still carries the
/// CPU-resolved points, so this names what a consumer that wanted to evaluate
/// the deformation itself did not receive.
/// </summary>
[Flags]
public enum SilkDeformationUnsupportedFeatures : uint
{
    /// <summary>Nothing about the deformation was refused.</summary>
    None = 0,

    /// <summary>The skeleton has more joints than the palette bound allows.</summary>
    JointBudget = 1,

    /// <summary>The rig authors more influences per point than the bound allows.</summary>
    InfluenceBudget = 2,

    /// <summary>The active sub-shapes exceed the sparse blend table bounds.</summary>
    BlendBudget = 4,

    /// <summary>The rig would exceed the deformation block byte ceiling.</summary>
    ByteBudget = 8,

    /// <summary>The rig uses a skinning method this block cannot express.</summary>
    SkinningMethod = 16,

    /// <summary>The emitted points are not the points the influences address.</summary>
    Geometry = 32,

    /// <summary>Authored normals a point-indexed deformation cannot carry.</summary>
    Normals = 64,

    /// <summary>The rig did not reproduce the CPU deformation, so it was dropped.</summary>
    Unverified = 128
}

/// <summary>
/// One resolved sub-shape of a bounded rig: a contiguous run of the sparse
/// delta table scaled by the weight UsdSkel resolved for it. In-between shapes
/// need no range kind of their own, because an authored blend-shape weight is
/// expanded into the weights of the primary shape and of every in-between it
/// interpolates through before the ranges are published.
/// </summary>
/// <param name="FirstDelta">The first delta this range covers.</param>
/// <param name="DeltaCount">The number of deltas this range covers.</param>
/// <param name="Weight">The resolved sub-shape weight.</param>
public readonly record struct SilkDeformationBlendRange(
    int FirstDelta,
    int DeltaCount,
    float Weight);

/// <summary>
/// The bounded, renderer-neutral rig of one deformed prototype, decoded from
/// the ABI v20 deformation block.
/// </summary>
/// <remarks>
/// Everything here is bulk: one array per stream, sized by the counts the block
/// declares and bounded by <see cref="SilkDeformationLimits"/>. No consumer
/// reads a point, a joint, or a blend shape one element at a time across the
/// native boundary, and no backend sees a USD path or token.
/// </remarks>
public sealed class SilkMeshDeformationData
{
    private const int BlendRangeSize = 16;
    private const int BlendDeltaSize = 28;

    private readonly float[] _geomBindTransform;
    private readonly float[] _bindPoints;
    private readonly float[] _bindNormals;
    private readonly uint[] _jointIndices;
    private readonly float[] _jointWeights;
    private readonly float[] _jointMatrices;
    private readonly SilkDeformationBlendRange[] _blendRanges;
    private readonly uint[] _blendDeltaPoints;
    private readonly float[] _blendDeltaPositionOffsets;
    private readonly float[] _blendDeltaNormalOffsets;
    private ulong? _bindIdentity;

    private SilkMeshDeformationData(
        SilkDeformationOptions options,
        SilkDeformationUnsupportedFeatures unsupportedFeatures,
        ulong identity,
        int jointCount,
        int influencesPerPoint,
        int bindPointCount,
        float[] geomBindTransform,
        float[] bindPoints,
        float[] bindNormals,
        uint[] jointIndices,
        float[] jointWeights,
        float[] jointMatrices,
        SilkDeformationBlendRange[] blendRanges,
        uint[] blendDeltaPoints,
        float[] blendDeltaPositionOffsets,
        float[] blendDeltaNormalOffsets)
    {
        Options = options;
        UnsupportedFeatures = unsupportedFeatures;
        Identity = identity;
        JointCount = jointCount;
        InfluencesPerPoint = influencesPerPoint;
        BindPointCount = bindPointCount;
        _geomBindTransform = geomBindTransform;
        _bindPoints = bindPoints;
        _bindNormals = bindNormals;
        _jointIndices = jointIndices;
        _jointWeights = jointWeights;
        _jointMatrices = jointMatrices;
        _blendRanges = blendRanges;
        _blendDeltaPoints = blendDeltaPoints;
        _blendDeltaPositionOffsets = blendDeltaPositionOffsets;
        _blendDeltaNormalOffsets = blendDeltaNormalOffsets;
    }

    /// <summary>Gets the optional sections this rig carries.</summary>
    public SilkDeformationOptions Options { get; }

    /// <summary>Gets what the producer refused to describe about this rig.</summary>
    public SilkDeformationUnsupportedFeatures UnsupportedFeatures { get; }

    /// <summary>
    /// Gets the FNV-1a index over the rig's own published bytes. It changes
    /// whenever the pose changes, which is what lets a retained deformation
    /// resource and a retained shadow map be keyed without comparing arrays.
    /// </summary>
    public ulong Identity { get; }

    /// <summary>Gets the number of joint palette rows.</summary>
    public int JointCount { get; }

    /// <summary>Gets the fixed influence stream width.</summary>
    public int InfluencesPerPoint { get; }

    /// <summary>Gets the number of bind-pose points, equal to the point count.</summary>
    public int BindPointCount { get; }

    /// <summary>Gets whether the rig carries authored per-point bind normals.</summary>
    public bool HasBindNormals => Options.HasFlag(SilkDeformationOptions.BindNormals);

    /// <summary>Gets the row-major transform into skeleton space.</summary>
    public ReadOnlyMemory<float> GeomBindTransform => _geomBindTransform;

    /// <summary>Gets the bind-pose points, three components per point.</summary>
    public ReadOnlyMemory<float> BindPoints => _bindPoints;

    /// <summary>Gets the bind-pose normals, empty without them.</summary>
    public ReadOnlyMemory<float> BindNormals => _bindNormals;

    /// <summary>Gets the fixed-width joint index stream.</summary>
    public ReadOnlyMemory<uint> JointIndices => _jointIndices;

    /// <summary>Gets the fixed-width joint weight stream.</summary>
    public ReadOnlyMemory<float> JointWeights => _jointWeights;

    /// <summary>Gets the joint palette, sixteen row-major floats per joint.</summary>
    public ReadOnlyMemory<float> JointMatrices => _jointMatrices;

    /// <summary>Gets the resolved sub-shape ranges.</summary>
    public IReadOnlyList<SilkDeformationBlendRange> BlendRanges => _blendRanges;

    /// <summary>Gets the point each sparse blend delta addresses.</summary>
    public ReadOnlyMemory<uint> BlendDeltaPoints => _blendDeltaPoints;

    /// <summary>Gets the sparse blend position offsets, three per delta.</summary>
    public ReadOnlyMemory<float> BlendDeltaPositionOffsets =>
        _blendDeltaPositionOffsets;

    /// <summary>Gets the sparse blend normal offsets, three per delta.</summary>
    public ReadOnlyMemory<float> BlendDeltaNormalOffsets => _blendDeltaNormalOffsets;

    /// <summary>
    /// Decodes one validated deformation block into a retained rig. The block
    /// has already been bounds-checked by the mesh command, so decoding copies
    /// rather than re-validates: the copy is what survives the page.
    /// </summary>
    internal static SilkMeshDeformationData Decode(
        ReadOnlySpan<byte> block,
        SilkDeformationOptions options,
        SilkDeformationUnsupportedFeatures unsupportedFeatures)
    {
        int jointCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(block[..4]);
        int influences = (int)BinaryPrimitives.ReadUInt32LittleEndian(block[4..8]);
        int bindPointCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(block[8..12]);
        int blendRangeCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(block[12..16]);
        int blendDeltaCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(block[16..20]);
        ulong identity = BinaryPrimitives.ReadUInt64LittleEndian(block[24..32]);

        int cursor = 32;
        float[] geomBindTransform = ReadFloats(block, ref cursor, 16);
        float[] bindPoints = ReadFloats(block, ref cursor, bindPointCount * 3);
        float[] bindNormals = options.HasFlag(SilkDeformationOptions.BindNormals)
            ? ReadFloats(block, ref cursor, bindPointCount * 3)
            : [];
        int influenceCount = bindPointCount * influences;
        uint[] jointIndices = new uint[influenceCount];
        for (int slot = 0; slot < influenceCount; slot++)
        {
            jointIndices[slot] = BinaryPrimitives.ReadUInt32LittleEndian(
                block.Slice(cursor + (slot * sizeof(uint)), sizeof(uint)));
        }
        cursor += influenceCount * sizeof(uint);
        float[] jointWeights = ReadFloats(block, ref cursor, influenceCount);
        float[] jointMatrices = ReadFloats(block, ref cursor, jointCount * 16);

        SilkDeformationBlendRange[] blendRanges = new SilkDeformationBlendRange[blendRangeCount];
        for (int range = 0; range < blendRangeCount; range++)
        {
            int entry = cursor + (range * BlendRangeSize);
            blendRanges[range] = new SilkDeformationBlendRange(
                (int)BinaryPrimitives.ReadUInt32LittleEndian(
                    block.Slice(entry, sizeof(uint))),
                (int)BinaryPrimitives.ReadUInt32LittleEndian(
                    block.Slice(entry + 4, sizeof(uint))),
                BinaryPrimitives.ReadSingleLittleEndian(
                    block.Slice(entry + 8, sizeof(float))));
        }
        cursor += blendRangeCount * BlendRangeSize;

        uint[] blendDeltaPoints = new uint[blendDeltaCount];
        float[] positionOffsets = new float[blendDeltaCount * 3];
        float[] normalOffsets = new float[blendDeltaCount * 3];
        for (int delta = 0; delta < blendDeltaCount; delta++)
        {
            int entry = cursor + (delta * BlendDeltaSize);
            blendDeltaPoints[delta] = BinaryPrimitives.ReadUInt32LittleEndian(
                block.Slice(entry, sizeof(uint)));
            for (int component = 0; component < 3; component++)
            {
                positionOffsets[(delta * 3) + component] =
                    BinaryPrimitives.ReadSingleLittleEndian(
                        block.Slice(entry + 4 + (component * sizeof(float)), sizeof(float)));
                normalOffsets[(delta * 3) + component] =
                    BinaryPrimitives.ReadSingleLittleEndian(
                        block.Slice(entry + 16 + (component * sizeof(float)), sizeof(float)));
            }
        }

        return new SilkMeshDeformationData(
            options,
            unsupportedFeatures,
            identity,
            jointCount,
            influences,
            bindPointCount,
            geomBindTransform,
            bindPoints,
            bindNormals,
            jointIndices,
            jointWeights,
            jointMatrices,
            blendRanges,
            blendDeltaPoints,
            positionOffsets,
            normalOffsets);
    }

    private static float[] ReadFloats(
        ReadOnlySpan<byte> block,
        ref int cursor,
        int count)
    {
        float[] values = new float[count];
        for (int element = 0; element < count; element++)
        {
            values[element] = BinaryPrimitives.ReadSingleLittleEndian(
                block.Slice(cursor + (element * sizeof(float)), sizeof(float)));
        }
        cursor += count * sizeof(float);
        return values;
    }

    /// <summary>
    /// Gets the identity of the rig's time-independent inputs alone.
    /// </summary>
    /// <remarks>
    /// <see cref="Identity"/> changes with every pose, which is what makes it a
    /// correct invalidation key, and exactly what makes it useless as a
    /// <em>retention</em> key: a GPU deformation resource holds the bind pose,
    /// the influence streams and the emitted topology, none of which move when
    /// the skeleton does. Keying that resource on the whole identity would
    /// rebuild it every frame and make dispatching the kernel strictly more
    /// expensive than deforming on the CPU. This covers the streams a resource
    /// uploads once and never touches again, so two poses of one rig share it
    /// and two different rigs never can.
    /// </remarks>
    public ulong BindIdentity => _bindIdentity ??= ComputeBindIdentity();

    private ulong ComputeBindIdentity()
    {
        ulong hash = 14695981039346656037UL;
        MixUInt32(ref hash, (uint)BindPointCount);
        MixUInt32(ref hash, (uint)InfluencesPerPoint);
        MixUInt32(ref hash, (uint)JointCount);
        MixUInt32(ref hash, (uint)Options);
        MixFloats(ref hash, _bindPoints);
        MixFloats(ref hash, _bindNormals);
        foreach (uint index in _jointIndices)
        {
            MixUInt32(ref hash, index);
        }
        MixFloats(ref hash, _jointWeights);
        return hash;
    }

    /// <summary>
    /// Recomputes the identity index over the rig's decoded bytes. The producer
    /// hashes the bytes it published, so a consumer that reaches a different
    /// value decoded something other than what was sent.
    /// </summary>
    internal ulong ComputeIdentity()
    {
        ulong hash = 14695981039346656037UL;
        MixFloats(ref hash, _geomBindTransform);
        MixFloats(ref hash, _bindPoints);
        MixFloats(ref hash, _bindNormals);
        foreach (uint index in _jointIndices)
        {
            MixUInt32(ref hash, index);
        }
        MixFloats(ref hash, _jointWeights);
        MixFloats(ref hash, _jointMatrices);
        foreach (SilkDeformationBlendRange range in _blendRanges)
        {
            MixUInt32(ref hash, (uint)range.FirstDelta);
            MixUInt32(ref hash, (uint)range.DeltaCount);
            MixSingle(ref hash, range.Weight);
            MixUInt32(ref hash, 0);
        }
        for (int delta = 0; delta < _blendDeltaPoints.Length; delta++)
        {
            MixUInt32(ref hash, _blendDeltaPoints[delta]);
            MixFloats(ref hash, _blendDeltaPositionOffsets.AsSpan(delta * 3, 3));
            MixFloats(ref hash, _blendDeltaNormalOffsets.AsSpan(delta * 3, 3));
        }
        return hash;
    }

    private static void MixFloats(ref ulong hash, ReadOnlySpan<float> values)
    {
        foreach (float value in values)
        {
            MixSingle(ref hash, value);
        }
    }

    private static void MixSingle(ref ulong hash, float value) =>
        MixUInt32(ref hash, (uint)BitConverter.SingleToInt32Bits(value));

    private static void MixUInt32(ref ulong hash, uint value)
    {
        for (int shift = 0; shift < 32; shift += 8)
        {
            hash ^= (value >> shift) & 0xFFu;
            hash *= 1099511628211UL;
        }
    }
}
