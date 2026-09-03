// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Pins the ABI v20 bounded deformation block: its wire contract, its bounds,
/// the retained rig it produces, and the evaluation order a backend kernel has
/// to reproduce.
/// </summary>
/// <remarks>
/// <para>
/// The block is the renderer-neutral seam a GPU deformation path consumes. It
/// never replaces the CPU-resolved points travelling in the same record, so the
/// only property worth gating is agreement: evaluating the rig must land on the
/// same surface hdSilk already published. These tests hold the managed
/// evaluator to hand-computed answers, and the native probe holds hdSilk's own
/// published rigs to hdSilk's own CPU deformation.
/// </para>
/// <para>
/// Every bound is checked at decode rather than at evaluation, because a
/// consumer sizes GPU allocations from these counts before it evaluates
/// anything. A block that declared more joints than the palette bound, or an
/// influence that indexed past the palette it declared, would otherwise be
/// found only by a shader reading out of bounds.
/// </para>
/// </remarks>
public sealed class SilkDeformationWireTests
{
    private const int MeshFixedSize = 268;

    [Test]
    public async Task DeformationRoundTripsEveryStreamAtItsOwnOffset()
    {
        Rig rig = TranslationRig();
        byte[] page = CreateMesh("/World/Skinned", rig);

        Decoded decoded = Read(page);
        await Assert.That(decoded.HasDeformation).IsTrue();
        await Assert.That(decoded.Deformation!.JointCount).IsEqualTo(2);
        await Assert.That(decoded.Deformation.InfluencesPerPoint).IsEqualTo(2);
        await Assert.That(decoded.Deformation.BindPointCount).IsEqualTo(3);
        await Assert.That(decoded.Deformation.HasBindNormals).IsTrue();
        await Assert.That(decoded.Deformation.UnsupportedFeatures)
            .IsEqualTo(SilkDeformationUnsupportedFeatures.None);

        // The bind points are authored as ascending distinct values so a
        // transposed, swapped, or off-by-one read is visible rather than
        // plausible.
        for (int component = 0; component < rig.BindPoints.Length; component++)
        {
            await Assert.That(decoded.Deformation.BindPoints.Span[component])
                .IsEqualTo(rig.BindPoints[component]);
        }
        for (int slot = 0; slot < rig.JointIndices.Length; slot++)
        {
            await Assert.That(decoded.Deformation.JointIndices.Span[slot])
                .IsEqualTo(rig.JointIndices[slot]);
            await Assert.That(decoded.Deformation.JointWeights.Span[slot])
                .IsEqualTo(rig.JointWeights[slot]);
        }
        for (int element = 0; element < rig.JointMatrices.Length; element++)
        {
            await Assert.That(decoded.Deformation.JointMatrices.Span[element])
                .IsEqualTo(rig.JointMatrices[element]);
        }
        await Assert.That(decoded.Deformation.BlendRanges.Count).IsEqualTo(0);
    }

    [Test]
    public async Task IdentityIsTheHashOfThePublishedRigBytes()
    {
        Rig rig = TranslationRig();
        Decoded decoded = Read(CreateMesh("/World/Skinned", rig));

        // The producer hashes the bytes it published; recomputing the same hash
        // from the decoded arrays proves the consumer decoded those bytes and
        // not a shifted view of them.
        await Assert.That(decoded.Deformation!.Identity)
            .IsEqualTo(decoded.Deformation.ComputeIdentity());
        await Assert.That(decoded.Deformation.Identity).IsNotEqualTo(0UL);
    }

    [Test]
    public async Task IdentityChangesWithThePoseAndNotWithTheRecord()
    {
        Rig pose = TranslationRig();
        ulong first = Read(CreateMesh("/World/Skinned", pose)).Deformation!.Identity;
        ulong same = Read(CreateMesh("/World/Other", pose)).Deformation!.Identity;

        Rig moved = TranslationRig();
        moved.JointMatrices[28] = 9.0f;
        ulong changed = Read(CreateMesh("/World/Skinned", moved)).Deformation!.Identity;

        // The identity indexes the rig, not the prim, so two prims at the same
        // pose share it and one prim at two poses does not.
        await Assert.That(same).IsEqualTo(first);
        await Assert.That(changed).IsNotEqualTo(first);
    }

    [Test]
    public async Task EvaluatingTheRigReproducesLinearBlendSkinning()
    {
        // Point 0 is fully weighted to joint 0, which translates by +10 in x.
        // Point 1 is fully weighted to joint 1, which translates by +20 in y.
        // Point 2 is split evenly, so it lands on the average of both.
        Rig rig = TranslationRig();
        Decoded decoded = Read(CreateMesh("/World/Skinned", rig));
        float[] points = new float[9];
        SilkDeformationEvaluator.EvaluatePoints(decoded.Deformation!, points);

        await Assert.That(points[0]).IsEqualTo(11.0f).Within(1e-5f);
        await Assert.That(points[1]).IsEqualTo(2.0f).Within(1e-5f);
        await Assert.That(points[2]).IsEqualTo(3.0f).Within(1e-5f);

        await Assert.That(points[3]).IsEqualTo(4.0f).Within(1e-5f);
        await Assert.That(points[4]).IsEqualTo(25.0f).Within(1e-5f);
        await Assert.That(points[5]).IsEqualTo(6.0f).Within(1e-5f);

        await Assert.That(points[6]).IsEqualTo(12.0f).Within(1e-5f);
        await Assert.That(points[7]).IsEqualTo(18.0f).Within(1e-5f);
        await Assert.That(points[8]).IsEqualTo(9.0f).Within(1e-5f);
    }

    [Test]
    public async Task EvaluatingTheRigAppliesTheGeomBindTransformBeforeTheJoints()
    {
        // The geom bind transform scales x by two. Point 0 is weighted entirely
        // to a joint that translates by +10, so the bind point 1 becomes 2 and
        // then 12. An implementation that skinned first and bound afterwards
        // would land on 22 instead, so the order is observable rather than
        // conventional.
        Rig rig = TranslationRig();
        rig.GeomBindTransform[0] = 2.0f;
        Decoded decoded = Read(CreateMesh("/World/Skinned", rig));
        float[] points = new float[9];
        SilkDeformationEvaluator.EvaluatePoints(decoded.Deformation!, points);

        await Assert.That(points[0]).IsEqualTo(12.0f).Within(1e-5f);
        await Assert.That(points[3]).IsEqualTo(8.0f).Within(1e-5f);
    }

    [Test]
    public async Task EvaluatingTheRigAppliesWeightedSparseBlendDeltasBeforeSkinning()
    {
        // Two ranges address the same point, which is what a resolved
        // in-between and its primary shape do: both contribute, scaled by the
        // weights UsdSkel resolved for them, before anything is skinned.
        Rig rig = TranslationRig();
        rig.BlendRanges = [(0, 1, 0.5f), (1, 1, 0.25f)];
        rig.BlendDeltaPoints = [0, 0];
        rig.BlendDeltaPositionOffsets = [4, 0, 0, 8, 0, 0];
        rig.BlendDeltaNormalOffsets = new float[6];
        Decoded decoded = Read(CreateMesh("/World/Skinned", rig));
        float[] points = new float[9];
        SilkDeformationEvaluator.EvaluatePoints(decoded.Deformation!, points);

        // 1 + (0.5 * 4) + (0.25 * 8) = 5, then joint 0 translates by +10.
        await Assert.That(points[0]).IsEqualTo(15.0f).Within(1e-5f);
        // Every other point is untouched by the ranges.
        await Assert.That(points[4]).IsEqualTo(25.0f).Within(1e-5f);
    }

    [Test]
    public async Task EvaluatingNormalsUsesTheInverseTransposeAndRenormalizes()
    {
        // Joint 0 scales x by four and y by one, so a normal along the diagonal
        // must tip towards y, not towards x: the inverse transpose is what turns
        // an anisotropic scale of the surface into the opposite tilt of its
        // normal. Every point is bound to joint 0 for this case.
        Rig rig = TranslationRig();
        rig.JointIndices = [0, 0, 0, 0, 0, 0];
        rig.JointWeights = [1, 0, 1, 0, 1, 0];
        rig.JointMatrices[0] = 4.0f;
        rig.BindNormals = [
            0.70710678f, 0.70710678f, 0,
            0, 0, 1,
            0, 0, 1];
        Decoded decoded = Read(CreateMesh("/World/Skinned", rig));
        float[] normals = new float[9];
        bool evaluated = SilkDeformationEvaluator.TryEvaluateNormals(
            decoded.Deformation!,
            normals);

        await Assert.That(evaluated).IsTrue();
        // (1/4, 1) normalized.
        await Assert.That(normals[0]).IsEqualTo(0.2425356f).Within(1e-5f);
        await Assert.That(normals[1]).IsEqualTo(0.9701425f).Within(1e-5f);
        await Assert.That(normals[2]).IsEqualTo(0f).Within(1e-5f);

        double length = Math.Sqrt(
            (normals[0] * normals[0]) +
            (normals[1] * normals[1]) +
            (normals[2] * normals[2]));
        await Assert.That(length).IsEqualTo(1.0).Within(1e-5);
    }

    [Test]
    public async Task ARigWithoutBindNormalsEvaluatesNoNormals()
    {
        Rig rig = TranslationRig();
        rig.BindNormals = null;
        Decoded decoded = Read(CreateMesh("/World/Skinned", rig));

        await Assert.That(decoded.Deformation!.HasBindNormals).IsFalse();
        await Assert.That(decoded.Deformation.BindNormals.Length).IsEqualTo(0);
        // The record already omits authored normals a deformation cannot carry,
        // so a consumer derives them from the deformed points exactly as it does
        // for the CPU-resolved record.
        await Assert.That(SilkDeformationEvaluator.TryEvaluateNormals(
                decoded.Deformation,
                new float[9]))
            .IsFalse();
    }

    [Test]
    public async Task AMeshWithoutARigCarriesNoBlockAndNoFlags()
    {
        Decoded decoded = Read(CreateMesh("/World/Static", rig: null));

        await Assert.That(decoded.HasDeformation).IsFalse();
        await Assert.That(decoded.Deformation).IsNull();
        await Assert.That(decoded.Flags).IsEqualTo(SilkDeformationOptions.None);
        await Assert.That(decoded.Unsupported)
            .IsEqualTo(SilkDeformationUnsupportedFeatures.None);
    }

    [Test]
    public async Task AnUnsupportedRigIsDiagnosedWithoutABlock()
    {
        // A refused rig still publishes its CPU-resolved points, so the record
        // renders; what it must not do is stay silent about the refusal.
        byte[] page = CreateMesh(
            "/World/Skinned",
            rig: null,
            unsupported: SilkDeformationUnsupportedFeatures.JointBudget |
                SilkDeformationUnsupportedFeatures.Normals);
        Decoded decoded = Read(page);

        await Assert.That(decoded.HasDeformation).IsFalse();
        await Assert.That(decoded.Unsupported).IsEqualTo(
            SilkDeformationUnsupportedFeatures.JointBudget |
            SilkDeformationUnsupportedFeatures.Normals);
    }

    [Test]
    [Arguments(0)]
    [Arguments(SilkDeformationLimits.MaximumJoints + 1)]
    public async Task AJointCountOutsideTheBudgetIsRejected(int jointCount)
    {
        Rig rig = TranslationRig();
        rig.OverrideJointCount = jointCount;

        await Assert.That(() => Read(CreateMesh("/World/Skinned", rig)))
            .Throws<InvalidDataException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(SilkDeformationLimits.MaximumInfluences + 1)]
    public async Task AnInfluenceWidthOutsideTheBudgetIsRejected(int influences)
    {
        Rig rig = TranslationRig();
        rig.OverrideInfluencesPerPoint = influences;

        await Assert.That(() => Read(CreateMesh("/World/Skinned", rig)))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task AJointIndexOutsideThePaletteIsRejected()
    {
        Rig rig = TranslationRig();
        rig.JointIndices[0] = 2;

        await Assert.That(() => Read(CreateMesh("/World/Skinned", rig)))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task ABindPointCountThatDisagreesWithTheRecordIsRejected()
    {
        Rig rig = TranslationRig();
        rig.OverrideBindPointCount = 2;

        await Assert.That(() => Read(CreateMesh("/World/Skinned", rig)))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task ABlendRangeOutsideTheDeltaTableIsRejected()
    {
        Rig rig = TranslationRig();
        rig.BlendRanges = [(0, 2, 1.0f)];
        rig.BlendDeltaPoints = [0];
        rig.BlendDeltaPositionOffsets = [1, 0, 0];
        rig.BlendDeltaNormalOffsets = new float[3];

        await Assert.That(() => Read(CreateMesh("/World/Skinned", rig)))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task ABlendDeltaOutsideThePointArrayIsRejected()
    {
        Rig rig = TranslationRig();
        rig.BlendRanges = [(0, 1, 1.0f)];
        rig.BlendDeltaPoints = [7];
        rig.BlendDeltaPositionOffsets = [1, 0, 0];
        rig.BlendDeltaNormalOffsets = new float[3];

        await Assert.That(() => Read(CreateMesh("/World/Skinned", rig)))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task ANonZeroReservedFieldIsRejected()
    {
        Rig rig = TranslationRig();
        rig.CorruptReserved = true;

        await Assert.That(() => Read(CreateMesh("/World/Skinned", rig)))
            .Throws<InvalidDataException>();
    }

    /// <summary>
    /// Every floating stream must be rejected at decode when it is not finite.
    /// </summary>
    /// <remarks>
    /// A non-finite element does not fail loudly downstream. It propagates
    /// through the whole evaluation and arrives as a NaN vertex, which every
    /// rasterizer silently discards, so the only symptom is a surface that
    /// quietly loses triangles with nothing naming the cause. The stream is
    /// named per case so a regression says which one stopped being checked.
    /// </remarks>
    [Test]
    [Arguments(DeformationFloatingTable.GeomBindTransform, float.NaN)]
    [Arguments(DeformationFloatingTable.GeomBindTransform, float.PositiveInfinity)]
    [Arguments(DeformationFloatingTable.BindPoints, float.NaN)]
    [Arguments(DeformationFloatingTable.BindPoints, float.NegativeInfinity)]
    [Arguments(DeformationFloatingTable.BindNormals, float.NaN)]
    [Arguments(DeformationFloatingTable.JointWeights, float.NaN)]
    [Arguments(DeformationFloatingTable.JointWeights, float.PositiveInfinity)]
    [Arguments(DeformationFloatingTable.JointMatrices, float.NaN)]
    [Arguments(DeformationFloatingTable.JointMatrices, float.NegativeInfinity)]
    [Arguments(DeformationFloatingTable.BlendRangeWeight, float.NaN)]
    [Arguments(DeformationFloatingTable.BlendDeltaPosition, float.NaN)]
    [Arguments(DeformationFloatingTable.BlendDeltaNormal, float.PositiveInfinity)]
    public async Task ANonFiniteStreamElementIsRejected(
        DeformationFloatingTable stream,
        float value)
    {
        Rig rig = BlendedRig();
        Poison(rig, stream, value);

        await Assert.That(() => Read(CreateMesh("/World/Skinned", rig)))
            .Throws<InvalidDataException>()
            .WithMessageContaining("not finite");
    }

    [Test]
    public async Task TheSameRigWithoutPoisonIsAccepted()
    {
        // Non-vacuity for the case above: the poisoned rig differs from an
        // accepted one only by the poisoned element, so the rejections cannot
        // be coming from the fixture's shape.
        Decoded decoded = Read(CreateMesh("/World/Skinned", BlendedRig()));

        await Assert.That(decoded.HasDeformation).IsTrue();
        await Assert.That(decoded.Deformation!.BlendRanges.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ADeclaredIdentityThatDoesNotCoverTheBlockIsRejected()
    {
        Rig rig = TranslationRig();
        byte[] page = CreateMesh("/World/Skinned", rig);
        int identityOffset = DeformationIdentityOffset(page);
        ulong declared = BinaryPrimitives.ReadUInt64LittleEndian(
            page.AsSpan(identityOffset, 8));
        BinaryPrimitives.WriteUInt64LittleEndian(
            page.AsSpan(identityOffset, 8),
            declared ^ 1UL);

        await Assert.That(() => Read(page))
            .Throws<InvalidDataException>()
            .WithMessageContaining("identity");
    }

    [Test]
    public async Task AlteredRigContentUnderAStaleIdentityIsRejected()
    {
        // This is the case the identity check exists for. The block still
        // decodes, every bound still holds, and every stream is still finite --
        // only the pose changed while the index a retained geometry resource
        // and a retained shadow map are keyed on stayed put. Without the
        // recomputation the altered pose would be drawn through resources
        // cached for the previous one.
        Rig rig = TranslationRig();
        byte[] page = CreateMesh("/World/Skinned", rig);
        int identityOffset = DeformationIdentityOffset(page);
        ulong declared = BinaryPrimitives.ReadUInt64LittleEndian(
            page.AsSpan(identityOffset, 8));

        // The first joint matrix translation, which is inside the identity's
        // coverage and outside every other check.
        int matricesOffset = identityOffset + 8 + (16 * sizeof(float)) +
            (rig.BindPoints.Length * sizeof(float)) +
            (rig.BindNormals!.Length * sizeof(float)) +
            (rig.JointIndices.Length * sizeof(uint)) +
            (rig.JointWeights.Length * sizeof(float));
        BinaryPrimitives.WriteSingleLittleEndian(
            page.AsSpan(matricesOffset + (12 * sizeof(float)), sizeof(float)),
            99.0f);

        // The declared identity is deliberately left untouched, so the only
        // thing that can reject this page is recomputing it.
        await Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(
                page.AsSpan(identityOffset, 8)))
            .IsEqualTo(declared);
        await Assert.That(() => Read(page))
            .Throws<InvalidDataException>()
            .WithMessageContaining("identity");
    }

    [Test]
    public async Task ARigThatFailsTheIdentityCheckNeverReachesTheRetainedScene()
    {
        var scene = new SilkSceneState();
        byte[] page = CreateMesh("/World/Skinned", TranslationRig());
        int identityOffset = DeformationIdentityOffset(page);
        BinaryPrimitives.WriteUInt64LittleEndian(page.AsSpan(identityOffset, 8), 0);

        await Assert.That(() => scene.Apply(page, 1, 1))
            .Throws<InvalidDataException>();
        // The page is refused whole, so nothing is retained and no cache key is
        // built from a rig the parser could not vouch for.
        await Assert.That(scene.MeshesByPath.Count).IsEqualTo(0);
        await Assert.That(scene.DeformationRevision).IsEqualTo(0UL);
    }

    /// <summary>The floating streams a deformation block carries.</summary>
    public enum DeformationFloatingTable
    {
        /// <summary>The row-major transform into skeleton space.</summary>
        GeomBindTransform,

        /// <summary>The bind-pose points.</summary>
        BindPoints,

        /// <summary>The bind-pose normals.</summary>
        BindNormals,

        /// <summary>The fixed-width joint weight stream.</summary>
        JointWeights,

        /// <summary>The joint palette.</summary>
        JointMatrices,

        /// <summary>A resolved sub-shape weight.</summary>
        BlendRangeWeight,

        /// <summary>A sparse blend position offset.</summary>
        BlendDeltaPosition,

        /// <summary>A sparse blend normal offset.</summary>
        BlendDeltaNormal
    }

    private static void Poison(Rig rig, DeformationFloatingTable stream, float value)
    {
        switch (stream)
        {
            case DeformationFloatingTable.GeomBindTransform:
                rig.GeomBindTransform[5] = value;
                break;
            case DeformationFloatingTable.BindPoints:
                rig.BindPoints[4] = value;
                break;
            case DeformationFloatingTable.BindNormals:
                rig.BindNormals![2] = value;
                break;
            case DeformationFloatingTable.JointWeights:
                rig.JointWeights[3] = value;
                break;
            case DeformationFloatingTable.JointMatrices:
                rig.JointMatrices[17] = value;
                break;
            case DeformationFloatingTable.BlendRangeWeight:
                rig.BlendRanges[0] = (
                    rig.BlendRanges[0].First,
                    rig.BlendRanges[0].Count,
                    value);
                break;
            case DeformationFloatingTable.BlendDeltaPosition:
                rig.BlendDeltaPositionOffsets[1] = value;
                break;
            case DeformationFloatingTable.BlendDeltaNormal:
                rig.BlendDeltaNormalOffsets[2] = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(stream));
        }
    }

    /// <summary>
    /// The offset of the deformation identity field inside an encoded page.
    /// </summary>
    private static int DeformationIdentityOffset(byte[] page)
    {
        int pathLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(page.AsSpan(48, 4));
        int pointCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(page.AsSpan(52, 4));
        int indexCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(page.AsSpan(56, 4));
        int triangleCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(page.AsSpan(60, 4));
        int blockStart = MeshFixedSize +
            pathLength +
            (pointCount * 3 * sizeof(float)) +
            (indexCount * sizeof(uint)) +
            (triangleCount * sizeof(uint));
        return blockStart + 24;
    }

    [Test]
    public async Task AnUnknownFlagIsRejected()
    {
        Rig rig = TranslationRig();
        rig.OverrideFlags = (SilkDeformationOptions)0x8;

        await Assert.That(() => Read(CreateMesh("/World/Skinned", rig)))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task AnUnknownUnsupportedReasonIsRejected()
    {
        await Assert.That(() => Read(CreateMesh(
                "/World/Skinned",
                rig: null,
                unsupported: (SilkDeformationUnsupportedFeatures)0x100)))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task ADeclaredBlockLongerThanTheRecordIsRejected()
    {
        Rig rig = TranslationRig();
        byte[] page = CreateMesh("/World/Skinned", rig);
        // The declared block length is the only field that could make a
        // consumer read past the command, so it is corrupted directly.
        uint declared = BinaryPrimitives.ReadUInt32LittleEndian(page.AsSpan(232, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(232, 4), declared + 4);

        await Assert.That(() => Read(page)).Throws<InvalidDataException>();
    }

    [Test]
    public async Task TheRetainedSceneCarriesTheRigAndItsIdentity()
    {
        var scene = new SilkSceneState();
        Rig rig = TranslationRig();
        byte[] page = CreateMesh("/World/Skinned", rig);
        scene.Apply(page, 1, 1);

        SilkMeshData mesh = scene.MeshesByPath[("/World/Skinned", 0)];
        await Assert.That(mesh.Deformation).IsNotNull();
        await Assert.That(mesh.DeformationIdentity).IsEqualTo(mesh.Deformation!.Identity);
        await Assert.That(mesh.DeformationUnsupportedFeatures)
            .IsEqualTo(SilkDeformationUnsupportedFeatures.None);
    }

    [Test]
    public async Task OnlyAChangedPoseAdvancesTheDeformationRevision()
    {
        var scene = new SilkSceneState();
        Rig rig = TranslationRig();
        scene.Apply(CreateMesh("/World/Skinned", rig), 1, 1);
        ulong afterFirst = scene.DeformationRevision;

        // Republishing the same pose is what a material or transform edit does
        // to a skinned prim; re-rendering every retained shadow map for it would
        // be a per-frame cost with no visible cause.
        scene.Apply(CreateMesh("/World/Skinned", TranslationRig()), 1, 2);
        ulong afterUnchanged = scene.DeformationRevision;

        Rig moved = TranslationRig();
        moved.JointMatrices[12] = 42.0f;
        scene.Apply(CreateMesh("/World/Skinned", moved), 1, 3);
        ulong afterMoved = scene.DeformationRevision;

        await Assert.That(afterFirst).IsGreaterThan(0UL);
        await Assert.That(afterUnchanged).IsEqualTo(afterFirst);
        await Assert.That(afterMoved).IsGreaterThan(afterUnchanged);
    }

    [Test]
    public async Task AStaticSceneNeverAdvancesTheDeformationRevision()
    {
        var scene = new SilkSceneState();
        scene.Apply(CreateMesh("/World/Static", rig: null), 1, 1);
        scene.Apply(CreateMesh("/World/Static", rig: null), 1, 2);

        await Assert.That(scene.DeformationRevision).IsEqualTo(0UL);
    }

    private static Decoded Read(byte[] page)
    {
        SilkCommandEnumerator commands = SilkCommandParser.Enumerate(
            page,
            1,
            SilkCommandParser.PageAbiVersion);
        if (!commands.MoveNext())
        {
            throw new InvalidDataException("The page carried no command.");
        }
        SilkMeshUpsertCommand mesh = commands.Current.AsMeshUpsert();
        return new Decoded(
            mesh.HasDeformation,
            mesh.DeformationFlags,
            mesh.DeformationUnsupportedFeatures,
            mesh.CopyDeformation());
    }

    private sealed record Decoded(
        bool HasDeformation,
        SilkDeformationOptions Flags,
        SilkDeformationUnsupportedFeatures Unsupported,
        SilkMeshDeformationData? Deformation);

    /// <summary>
    /// A three-point mesh bound to two translating joints, which is the
    /// smallest rig that exercises a split influence: one point per joint plus
    /// one point blended evenly between them.
    /// </summary>
    private static Rig TranslationRig()
    {
        float[] joint0 = Identity();
        joint0[12] = 10.0f;
        float[] joint1 = Identity();
        joint1[13] = 20.0f;
        return new Rig
        {
            GeomBindTransform = Identity(),
            BindPoints = [1, 2, 3, 4, 5, 6, 7, 8, 9],
            BindNormals = [0, 0, 1, 0, 1, 0, 1, 0, 0],
            JointIndices = [0, 0, 1, 1, 0, 1],
            JointWeights = [1, 0, 1, 0, 0.5f, 0.5f],
            JointMatrices = [.. joint0, .. joint1],
            BlendRanges = [],
            BlendDeltaPoints = [],
            BlendDeltaPositionOffsets = [],
            BlendDeltaNormalOffsets = []
        };
    }

    private static float[] Identity() =>
    [
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1
    ];

    /// <summary>
    /// The translation rig with one resolved sub-shape, so every floating
    /// stream the block can carry -- including the blend range weight and both
    /// delta channels -- is present in one fixture.
    /// </summary>
    private static Rig BlendedRig()
    {
        Rig rig = TranslationRig();
        rig.BlendRanges = [(0, 1, 0.5f)];
        rig.BlendDeltaPoints = [0];
        rig.BlendDeltaPositionOffsets = [1, 2, 3];
        rig.BlendDeltaNormalOffsets = [0.25f, 0.5f, 0.75f];
        return rig;
    }

    private sealed class Rig
    {
        public required float[] GeomBindTransform { get; set; }

        public required float[] BindPoints { get; set; }

        public float[]? BindNormals { get; set; }

        public required uint[] JointIndices { get; set; }

        public required float[] JointWeights { get; set; }

        public required float[] JointMatrices { get; set; }

        public required (uint First, uint Count, float Weight)[] BlendRanges { get; set; }

        public required uint[] BlendDeltaPoints { get; set; }

        public required float[] BlendDeltaPositionOffsets { get; set; }

        public required float[] BlendDeltaNormalOffsets { get; set; }

        public int? OverrideJointCount { get; set; }

        public int? OverrideInfluencesPerPoint { get; set; }

        public int? OverrideBindPointCount { get; set; }

        public SilkDeformationOptions? OverrideFlags { get; set; }

        public bool CorruptReserved { get; set; }

        public int JointCount => OverrideJointCount ?? (JointMatrices.Length / 16);

        public int InfluencesPerPoint =>
            OverrideInfluencesPerPoint ?? (JointIndices.Length / (BindPoints.Length / 3));

        public int BindPointCount => OverrideBindPointCount ?? (BindPoints.Length / 3);

        public SilkDeformationOptions Flags
        {
            get
            {
                if (OverrideFlags is { } flags)
                {
                    return flags;
                }
                SilkDeformationOptions resolved = BindNormals is null
                    ? SilkDeformationOptions.None
                    : SilkDeformationOptions.BindNormals;
                foreach (float offset in BlendDeltaNormalOffsets)
                {
                    if (offset != 0)
                    {
                        resolved |= SilkDeformationOptions.BlendNormalOffsets;
                        break;
                    }
                }
                return resolved;
            }
        }

        public int ByteCount =>
            96 +
            (BindPoints.Length * sizeof(float)) +
            ((BindNormals?.Length ?? 0) * sizeof(float)) +
            (JointIndices.Length * sizeof(uint)) +
            (JointWeights.Length * sizeof(float)) +
            (JointMatrices.Length * sizeof(float)) +
            (BlendRanges.Length * 16) +
            (BlendDeltaPoints.Length * 28);
    }

    /// <summary>
    /// Encodes one MESH_UPSERT carrying a triangle and, optionally, the bounded
    /// rig for it. The encoder is hand-written on purpose: the point of a wire
    /// test is that the bytes are produced independently of the parser under
    /// test.
    /// </summary>
    private static byte[] CreateMesh(
        string pathValue,
        Rig? rig,
        SilkDeformationUnsupportedFeatures unsupported =
            SilkDeformationUnsupportedFeatures.None)
    {
        byte[] path = Encoding.UTF8.GetBytes(pathValue);
        float[] points = [1, 2, 3, 4, 5, 6, 7, 8, 9];
        uint[] indices = [0, 1, 2];
        int deformationBytes = rig?.ByteCount ?? 0;
        int size = MeshFixedSize +
            path.Length +
            (points.Length * sizeof(float)) +
            (indices.Length * sizeof(uint)) +
            sizeof(uint) +
            deformationBytes;
        byte[] bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkWireFormat.ComputeStableHash(pathValue));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 7);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(28),
            (uint)SilkTopologyKind.TriangleList);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(44),
            (uint)SilkMeshCullStyle.BackUnlessDoubleSided);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), (uint)path.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), 1);
        for (int component = 0; component < 4; component++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(64 + (component * sizeof(float))),
                1.0f);
        }
        float[] identity = Identity();
        for (int element = 0; element < 16; element++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(80 + (element * sizeof(double))),
                identity[element]);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(224), (uint)(rig?.Flags ?? 0));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(228), (uint)unsupported);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(232), (uint)deformationBytes);

        path.CopyTo(bytes, MeshFixedSize);
        int cursor = MeshFixedSize + path.Length;
        foreach (float value in points)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(cursor), value);
            cursor += sizeof(float);
        }
        foreach (uint index in indices)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), index);
            cursor += sizeof(uint);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), 0);
        cursor += sizeof(uint);

        if (rig is null)
        {
            return bytes;
        }

        int blockStart = cursor;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), (uint)rig.JointCount);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(cursor + 4),
            (uint)rig.InfluencesPerPoint);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(cursor + 8),
            (uint)rig.BindPointCount);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(cursor + 12),
            (uint)rig.BlendRanges.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(cursor + 16),
            (uint)rig.BlendDeltaPoints.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(cursor + 20),
            rig.CorruptReserved ? 1u : 0u);
        cursor += 32;
        cursor = WriteFloats(bytes, cursor, rig.GeomBindTransform);
        cursor = WriteFloats(bytes, cursor, rig.BindPoints);
        if (rig.BindNormals is { } normals)
        {
            cursor = WriteFloats(bytes, cursor, normals);
        }
        foreach (uint joint in rig.JointIndices)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), joint);
            cursor += sizeof(uint);
        }
        cursor = WriteFloats(bytes, cursor, rig.JointWeights);
        cursor = WriteFloats(bytes, cursor, rig.JointMatrices);
        foreach ((uint first, uint count, float weight) in rig.BlendRanges)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), first);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor + 4), count);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(cursor + 8), weight);
            cursor += 16;
        }
        for (int delta = 0; delta < rig.BlendDeltaPoints.Length; delta++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(cursor),
                rig.BlendDeltaPoints[delta]);
            for (int component = 0; component < 3; component++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(
                    bytes.AsSpan(cursor + 4 + (component * sizeof(float))),
                    rig.BlendDeltaPositionOffsets[(delta * 3) + component]);
                BinaryPrimitives.WriteSingleLittleEndian(
                    bytes.AsSpan(cursor + 16 + (component * sizeof(float))),
                    rig.BlendDeltaNormalOffsets[(delta * 3) + component]);
            }
            cursor += 28;
        }

        // The identity is FNV-1a over the block's own bytes after the identity
        // field, exactly as the producer computes it.
        ulong identityHash = 14695981039346656037UL;
        for (int offset = blockStart + 32; offset < cursor; offset++)
        {
            identityHash ^= bytes[offset];
            identityHash *= 1099511628211UL;
        }
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(blockStart + 24),
            identityHash);
        return bytes;
    }

    private static int WriteFloats(byte[] bytes, int cursor, float[] values)
    {
        foreach (float value in values)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(cursor), value);
            cursor += sizeof(float);
        }
        return cursor;
    }
}
