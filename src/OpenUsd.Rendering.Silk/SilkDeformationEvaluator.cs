// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Evaluates a bounded deformation rig into deformed points and normals.
/// </summary>
/// <remarks>
/// <para>
/// This is the renderer-neutral contract of the ABI v20 deformation block. It
/// is deliberately written as one bounded pass over bulk arrays, in single
/// precision, in the exact order the ABI documents, because that is the order a
/// backend kernel has to reproduce: bind pose, then weighted sparse blend
/// deltas, then the geom bind transform, then the weighted joint palette.
/// </para>
/// <para>
/// It is not the renderer's deformation path. hdSilk resolves the supported
/// UsdSkel subset on the CPU and publishes the result, and that result is what
/// the renderer draws. This evaluator exists so the published rig can be held
/// to that result analytically -- if the two disagree, the rig is wrong, and no
/// backend may evaluate it.
/// </para>
/// </remarks>
public static class SilkDeformationEvaluator
{
    private const float DegenerateLengthSquared = 1e-30f;

    /// <summary>
    /// Evaluates the rig's deformed points into <paramref name="points"/>,
    /// which must hold three components per bind point.
    /// </summary>
    public static void EvaluatePoints(
        SilkMeshDeformationData deformation,
        Span<float> points)
    {
        ArgumentNullException.ThrowIfNull(deformation);
        int pointCount = deformation.BindPointCount;
        if (points.Length != pointCount * 3)
        {
            throw new ArgumentException(
                "The point destination must hold three components per bind point.",
                nameof(points));
        }

        float[] offsets = AccumulateBlendOffsets(
            deformation,
            deformation.BlendDeltaPositionOffsets.Span);
        ReadOnlySpan<float> bindPoints = deformation.BindPoints.Span;
        ReadOnlySpan<float> weights = deformation.JointWeights.Span;
        ReadOnlySpan<uint> indices = deformation.JointIndices.Span;
        ReadOnlySpan<float> palette = deformation.JointMatrices.Span;
        ReadOnlySpan<float> geomBind = deformation.GeomBindTransform.Span;
        int influences = deformation.InfluencesPerPoint;

        for (int point = 0; point < pointCount; point++)
        {
            int source = point * 3;
            TransformPoint(
                geomBind,
                bindPoints[source] + offsets[source],
                bindPoints[source + 1] + offsets[source + 1],
                bindPoints[source + 2] + offsets[source + 2],
                out float boundX,
                out float boundY,
                out float boundZ);

            float x = 0;
            float y = 0;
            float z = 0;
            for (int influence = 0; influence < influences; influence++)
            {
                int slot = (point * influences) + influence;
                float weight = weights[slot];
                if (weight == 0)
                {
                    continue;
                }
                TransformPoint(
                    palette.Slice((int)indices[slot] * 16, 16),
                    boundX,
                    boundY,
                    boundZ,
                    out float movedX,
                    out float movedY,
                    out float movedZ);
                x += movedX * weight;
                y += movedY * weight;
                z += movedZ * weight;
            }
            points[source] = x;
            points[source + 1] = y;
            points[source + 2] = z;
        }
    }

    /// <summary>
    /// Evaluates the rig's deformed normals into <paramref name="normals"/>,
    /// which must hold three components per bind point. Each normal is
    /// transformed by the inverse transpose of the matrices that move the
    /// points and renormalized once at the end.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the rig carries no bind normals, in which
    /// case <paramref name="normals"/> is left untouched and a consumer derives
    /// normals from the deformed points exactly as it does for a CPU-resolved
    /// record that omitted them.
    /// </returns>
    public static bool TryEvaluateNormals(
        SilkMeshDeformationData deformation,
        Span<float> normals) =>
        TryEvaluateNormals(deformation, normals, []);

    /// <summary>
    /// Evaluates the rig's deformed normals and reports which of them carried
    /// no direction.
    /// </summary>
    /// <param name="deformation">The bounded rig to evaluate.</param>
    /// <param name="normals">
    /// Three components per bind point. A point whose accumulated normal is
    /// non-finite or shorter than the degenerate threshold resolves to the
    /// canonical <c>(0, 0, 1)</c> fallback, which every consumer of this ABI
    /// must produce so that one rig cannot verify against one consumer and not
    /// against another.
    /// </param>
    /// <param name="degenerate">
    /// One flag per bind point, or empty to skip the report. The fallback is
    /// indistinguishable from a genuinely computed <c>+Z</c>, so a caller that
    /// compares these normals against a separately resolved array needs the
    /// flag to tell a collapsed direction from a computed one: a degeneracy on
    /// exactly one side is a disagreement about the surface, and a degeneracy
    /// on both sides is simply nothing to compare.
    /// </param>
    /// <returns>
    /// <see langword="false"/> when the rig carries no bind normals.
    /// </returns>
    public static bool TryEvaluateNormals(
        SilkMeshDeformationData deformation,
        Span<float> normals,
        Span<bool> degenerate)
    {
        ArgumentNullException.ThrowIfNull(deformation);
        if (!deformation.HasBindNormals)
        {
            return false;
        }
        int pointCount = deformation.BindPointCount;
        if (normals.Length != pointCount * 3)
        {
            throw new ArgumentException(
                "The normal destination must hold three components per bind point.",
                nameof(normals));
        }
        if (!degenerate.IsEmpty && degenerate.Length != pointCount)
        {
            throw new ArgumentException(
                "The degeneracy report must hold one flag per bind point.",
                nameof(degenerate));
        }

        float[] offsets = AccumulateBlendOffsets(
            deformation,
            deformation.BlendDeltaNormalOffsets.Span);
        ReadOnlySpan<float> bindNormals = deformation.BindNormals.Span;
        ReadOnlySpan<float> weights = deformation.JointWeights.Span;
        ReadOnlySpan<uint> indices = deformation.JointIndices.Span;
        ReadOnlySpan<float> palette = deformation.JointMatrices.Span;
        int influences = deformation.InfluencesPerPoint;

        float[] normalPalette = new float[deformation.JointCount * 9];
        for (int joint = 0; joint < deformation.JointCount; joint++)
        {
            InverseTranspose(palette.Slice(joint * 16, 16), normalPalette.AsSpan(joint * 9, 9));
        }
        float[] geomBindNormal = new float[9];
        InverseTranspose(deformation.GeomBindTransform.Span, geomBindNormal);

        for (int point = 0; point < pointCount; point++)
        {
            int source = point * 3;
            TransformDirection(
                geomBindNormal,
                bindNormals[source] + offsets[source],
                bindNormals[source + 1] + offsets[source + 1],
                bindNormals[source + 2] + offsets[source + 2],
                out float boundX,
                out float boundY,
                out float boundZ);

            float x = 0;
            float y = 0;
            float z = 0;
            for (int influence = 0; influence < influences; influence++)
            {
                int slot = (point * influences) + influence;
                float weight = weights[slot];
                if (weight == 0)
                {
                    continue;
                }
                TransformDirection(
                    normalPalette.AsSpan((int)indices[slot] * 9, 9),
                    boundX,
                    boundY,
                    boundZ,
                    out float movedX,
                    out float movedY,
                    out float movedZ);
                x += movedX * weight;
                y += movedY * weight;
                z += movedZ * weight;
            }

            double lengthSquared = ((double)x * x) + ((double)y * y) + ((double)z * z);
            if (!double.IsFinite(lengthSquared) || lengthSquared <= DegenerateLengthSquared)
            {
                normals[source] = 0;
                normals[source + 1] = 0;
                normals[source + 2] = 1;
                if (!degenerate.IsEmpty)
                {
                    degenerate[point] = true;
                }
                continue;
            }
            double inverseLength = 1.0 / Math.Sqrt(lengthSquared);
            normals[source] = (float)(x * inverseLength);
            normals[source + 1] = (float)(y * inverseLength);
            normals[source + 2] = (float)(z * inverseLength);
        }
        return true;
    }

    /// <summary>
    /// Reports whether a resolved normal carries a direction, on the same terms
    /// the evaluator uses for its own output.
    /// </summary>
    /// <remarks>
    /// A caller comparing evaluated normals against separately resolved ones
    /// needs both sides classified by one rule; classifying only the evaluated
    /// side would make a collapsed resolved normal look like a mismatch and a
    /// collapsed evaluated normal look like agreement.
    /// </remarks>
    public static bool IsDegenerateNormal(float x, float y, float z)
    {
        double lengthSquared = ((double)x * x) + ((double)y * y) + ((double)z * z);
        return !double.IsFinite(lengthSquared) ||
            lengthSquared <= DegenerateLengthSquared;
    }

    /// <summary>
    /// Compares one evaluated component against a resolved one on the ABI's
    /// terms: the difference is scaled by the larger of one and the resolved
    /// magnitude, because the two evaluations run the same arithmetic in a
    /// different order and precision.
    /// </summary>
    public static bool ComponentAgrees(float evaluated, float resolved)
    {
        if (!float.IsFinite(evaluated) || !float.IsFinite(resolved))
        {
            return false;
        }
        float scale = Math.Max(1.0f, Math.Abs(resolved));
        return Math.Abs(evaluated - resolved) <=
            SilkDeformationLimits.VerifyTolerance * scale;
    }

    /// <summary>
    /// Accumulates every resolved sub-shape's weighted sparse deltas into one
    /// dense offset per point. Ranges overlap and deltas are sparse, so this is
    /// one pass over the delta table rather than a search per point.
    /// </summary>
    private static float[] AccumulateBlendOffsets(
        SilkMeshDeformationData deformation,
        ReadOnlySpan<float> deltaOffsets)
    {
        float[] offsets = new float[deformation.BindPointCount * 3];
        if (deltaOffsets.IsEmpty)
        {
            return offsets;
        }
        ReadOnlySpan<uint> deltaPoints = deformation.BlendDeltaPoints.Span;
        foreach (SilkDeformationBlendRange range in deformation.BlendRanges)
        {
            for (int entry = 0; entry < range.DeltaCount; entry++)
            {
                int delta = range.FirstDelta + entry;
                int target = (int)deltaPoints[delta] * 3;
                offsets[target] += range.Weight * deltaOffsets[delta * 3];
                offsets[target + 1] += range.Weight * deltaOffsets[(delta * 3) + 1];
                offsets[target + 2] += range.Weight * deltaOffsets[(delta * 3) + 2];
            }
        }
        return offsets;
    }

    private static void TransformPoint(
        ReadOnlySpan<float> matrix,
        float x,
        float y,
        float z,
        out float outX,
        out float outY,
        out float outZ)
    {
        // USD composes row vectors, so a point multiplies the matrix from the
        // left; that is the convention UsdSkel blends influences in.
        outX = (x * matrix[0]) + (y * matrix[4]) + (z * matrix[8]) + matrix[12];
        outY = (x * matrix[1]) + (y * matrix[5]) + (z * matrix[9]) + matrix[13];
        outZ = (x * matrix[2]) + (y * matrix[6]) + (z * matrix[10]) + matrix[14];
    }

    private static void TransformDirection(
        ReadOnlySpan<float> matrix,
        float x,
        float y,
        float z,
        out float outX,
        out float outY,
        out float outZ)
    {
        outX = (x * matrix[0]) + (y * matrix[3]) + (z * matrix[6]);
        outY = (x * matrix[1]) + (y * matrix[4]) + (z * matrix[7]);
        outZ = (x * matrix[2]) + (y * matrix[5]) + (z * matrix[8]);
    }

    /// <summary>
    /// Writes the inverse transpose of the upper-left three-by-three of a
    /// row-major four-by-four into nine row-major floats.
    /// </summary>
    /// <remarks>
    /// This is the same arithmetic the evaluator applies to a joint, exposed so
    /// a GPU uploader can precompute the normal matrices instead of inverting
    /// them again in a kernel. Doing it once, here, is what guarantees a
    /// near-singular joint is inverted identically on both paths rather than in
    /// double precision on one and single precision on the other.
    /// </remarks>
    public static void WriteInverseTranspose(
        ReadOnlySpan<float> matrix,
        Span<float> result) =>
        InverseTranspose(matrix, result);

    /// <summary>
    /// The inverse transpose of the upper-left three-by-three of a row-major
    /// four-by-four. Computed in double so a near-singular joint keeps the
    /// precision float would lose; a singular joint yields the identity, which
    /// leaves the authored normal direction unchanged rather than collapsing it.
    /// </summary>
    private static void InverseTranspose(ReadOnlySpan<float> matrix, Span<float> result)
    {
        double m00 = matrix[0];
        double m01 = matrix[1];
        double m02 = matrix[2];
        double m10 = matrix[4];
        double m11 = matrix[5];
        double m12 = matrix[6];
        double m20 = matrix[8];
        double m21 = matrix[9];
        double m22 = matrix[10];
        double c00 = (m11 * m22) - (m12 * m21);
        double c01 = (m12 * m20) - (m10 * m22);
        double c02 = (m10 * m21) - (m11 * m20);
        double determinant = (m00 * c00) + (m01 * c01) + (m02 * c02);
        if (!double.IsFinite(determinant) || Math.Abs(determinant) < 1e-12)
        {
            result[0] = 1;
            result[1] = 0;
            result[2] = 0;
            result[3] = 0;
            result[4] = 1;
            result[5] = 0;
            result[6] = 0;
            result[7] = 0;
            result[8] = 1;
            return;
        }
        double inverse = 1.0 / determinant;
        // Transposing the inverse is the same as reading the adjugate by rows.
        result[0] = (float)(c00 * inverse);
        result[1] = (float)(c01 * inverse);
        result[2] = (float)(c02 * inverse);
        result[3] = (float)(((m02 * m21) - (m01 * m22)) * inverse);
        result[4] = (float)(((m00 * m22) - (m02 * m20)) * inverse);
        result[5] = (float)(((m01 * m20) - (m00 * m21)) * inverse);
        result[6] = (float)(((m01 * m12) - (m02 * m11)) * inverse);
        result[7] = (float)(((m02 * m10) - (m00 * m12)) * inverse);
        result[8] = (float)(((m00 * m11) - (m01 * m10)) * inverse);
    }
}
