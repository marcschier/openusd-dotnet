// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd;

/// <summary>
/// An immutable row-major 4x4 double-precision matrix using OpenUSD/Gf row-vector semantics.
/// </summary>
public readonly struct UsdMatrix4d : IEquatable<UsdMatrix4d>, IUsdDetachedResult
{
    /// <summary>Initializes a matrix from values in row-major order.</summary>
    public UsdMatrix4d(
        double m00, double m01, double m02, double m03,
        double m10, double m11, double m12, double m13,
        double m20, double m21, double m22, double m23,
        double m30, double m31, double m32, double m33)
    {
        M00 = m00;
        M01 = m01;
        M02 = m02;
        M03 = m03;
        M10 = m10;
        M11 = m11;
        M12 = m12;
        M13 = m13;
        M20 = m20;
        M21 = m21;
        M22 = m22;
        M23 = m23;
        M30 = m30;
        M31 = m31;
        M32 = m32;
        M33 = m33;
    }

    /// <summary>Gets the identity matrix.</summary>
    public static UsdMatrix4d Identity { get; } = new(
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1);

    /// <summary>
    /// Creates an affine translation matrix. OpenUSD stores translation in M30, M31, and M32.
    /// </summary>
    public static UsdMatrix4d CreateTranslation(double x, double y, double z) => new(
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        x, y, z, 1);

    /// <summary>Creates an affine translation matrix.</summary>
    public static UsdMatrix4d CreateTranslation(UsdVec3d translation) =>
        CreateTranslation(translation.X, translation.Y, translation.Z);

    /// <summary>Gets row 0, column 0.</summary>
    public double M00 { get; }
    /// <summary>Gets row 0, column 1.</summary>
    public double M01 { get; }
    /// <summary>Gets row 0, column 2.</summary>
    public double M02 { get; }
    /// <summary>Gets row 0, column 3.</summary>
    public double M03 { get; }
    /// <summary>Gets row 1, column 0.</summary>
    public double M10 { get; }
    /// <summary>Gets row 1, column 1.</summary>
    public double M11 { get; }
    /// <summary>Gets row 1, column 2.</summary>
    public double M12 { get; }
    /// <summary>Gets row 1, column 3.</summary>
    public double M13 { get; }
    /// <summary>Gets row 2, column 0.</summary>
    public double M20 { get; }
    /// <summary>Gets row 2, column 1.</summary>
    public double M21 { get; }
    /// <summary>Gets row 2, column 2.</summary>
    public double M22 { get; }
    /// <summary>Gets row 2, column 3.</summary>
    public double M23 { get; }
    /// <summary>Gets row 3, column 0.</summary>
    public double M30 { get; }
    /// <summary>Gets row 3, column 1.</summary>
    public double M31 { get; }
    /// <summary>Gets row 3, column 2.</summary>
    public double M32 { get; }
    /// <summary>Gets row 3, column 3.</summary>
    public double M33 { get; }

    /// <summary>Gets a value by zero-based row and column.</summary>
    public double this[int row, int column] => (row, column) switch
    {
        (0, 0) => M00,
        (0, 1) => M01,
        (0, 2) => M02,
        (0, 3) => M03,
        (1, 0) => M10,
        (1, 1) => M11,
        (1, 2) => M12,
        (1, 3) => M13,
        (2, 0) => M20,
        (2, 1) => M21,
        (2, 2) => M22,
        (2, 3) => M23,
        (3, 0) => M30,
        (3, 1) => M31,
        (3, 2) => M32,
        (3, 3) => M33,
        _ => throw new ArgumentOutOfRangeException(
            row is < 0 or > 3 ? nameof(row) : nameof(column))
    };

    /// <summary>Returns a new row-major array containing all 16 values.</summary>
    public double[] ToArray() =>
    [
        M00, M01, M02, M03,
        M10, M11, M12, M13,
        M20, M21, M22, M23,
        M30, M31, M32, M33
    ];

    /// <summary>Extracts the OpenUSD/Gf row-vector translation components.</summary>
    public UsdVec3d ExtractTranslation() => new(M30, M31, M32);

    /// <summary>Transforms a point using OpenUSD/Gf row-vector semantics.</summary>
    public UsdVec3d TransformPoint(UsdVec3d point)
    {
        double x = (point.X * M00) + (point.Y * M10) + (point.Z * M20) + M30;
        double y = (point.X * M01) + (point.Y * M11) + (point.Z * M21) + M31;
        double z = (point.X * M02) + (point.Y * M12) + (point.Z * M22) + M32;
        double w = (point.X * M03) + (point.Y * M13) + (point.Z * M23) + M33;
        if (w != 0 && w != 1)
        {
            x /= w;
            y /= w;
            z /= w;
        }
        return new UsdVec3d(x, y, z);
    }

    /// <summary>Tries to compute a finite double-precision inverse without allocating.</summary>
    /// <param name="inverse">
    /// Receives the inverse, or the all-zero matrix when this matrix is singular, contains
    /// non-finite values, or cannot produce a finite inverse.
    /// </param>
    public bool TryInvert(out UsdMatrix4d inverse)
    {
        inverse = default;
        Span<double> augmented = stackalloc double[32];
        augmented.Clear();
        augmented[0] = M00;
        augmented[1] = M01;
        augmented[2] = M02;
        augmented[3] = M03;
        augmented[4] = 1;
        augmented[8] = M10;
        augmented[9] = M11;
        augmented[10] = M12;
        augmented[11] = M13;
        augmented[13] = 1;
        augmented[16] = M20;
        augmented[17] = M21;
        augmented[18] = M22;
        augmented[19] = M23;
        augmented[22] = 1;
        augmented[24] = M30;
        augmented[25] = M31;
        augmented[26] = M32;
        augmented[27] = M33;
        augmented[31] = 1;

        Span<double> rowScales = stackalloc double[4];
        for (int row = 0; row < 4; row++)
        {
            int rowOffset = row * 8;
            double scale = 0;
            for (int column = 0; column < 4; column++)
            {
                double component = augmented[rowOffset + column];
                if (!double.IsFinite(component))
                {
                    return false;
                }
                scale = Math.Max(scale, Math.Abs(component));
            }
            if (scale == 0)
            {
                return false;
            }
            rowScales[row] = scale;
        }

        for (int pivotColumn = 0; pivotColumn < 4; pivotColumn++)
        {
            int pivotRow = pivotColumn;
            double bestRatio = -1;
            for (int row = pivotColumn; row < 4; row++)
            {
                double ratio =
                    Math.Abs(augmented[(row * 8) + pivotColumn]) / rowScales[row];
                if (ratio > bestRatio)
                {
                    bestRatio = ratio;
                    pivotRow = row;
                }
            }

            int pivotOffset = pivotRow * 8;
            double pivot = augmented[pivotOffset + pivotColumn];
            if (pivot == 0 || !double.IsFinite(pivot))
            {
                return false;
            }

            if (pivotRow != pivotColumn)
            {
                int targetOffset = pivotColumn * 8;
                for (int column = 0; column < 8; column++)
                {
                    (augmented[targetOffset + column], augmented[pivotOffset + column]) =
                        (augmented[pivotOffset + column], augmented[targetOffset + column]);
                }
                (rowScales[pivotColumn], rowScales[pivotRow]) =
                    (rowScales[pivotRow], rowScales[pivotColumn]);
                pivotOffset = targetOffset;
            }

            pivot = augmented[pivotOffset + pivotColumn];
            for (int column = 0; column < 8; column++)
            {
                double normalized = augmented[pivotOffset + column] / pivot;
                if (!double.IsFinite(normalized))
                {
                    return false;
                }
                augmented[pivotOffset + column] = normalized;
            }
            augmented[pivotOffset + pivotColumn] = 1;

            for (int row = 0; row < 4; row++)
            {
                if (row == pivotColumn)
                {
                    continue;
                }

                int rowOffset = row * 8;
                double factor = augmented[rowOffset + pivotColumn];
                if (factor == 0)
                {
                    continue;
                }
                for (int column = 0; column < 8; column++)
                {
                    if (column == pivotColumn)
                    {
                        continue;
                    }
                    double updated = Math.FusedMultiplyAdd(
                        -factor,
                        augmented[pivotOffset + column],
                        augmented[rowOffset + column]);
                    if (!double.IsFinite(updated))
                    {
                        return false;
                    }
                    augmented[rowOffset + column] = updated;
                }
                augmented[rowOffset + pivotColumn] = 0;
            }
        }

        inverse = new UsdMatrix4d(
            augmented[4], augmented[5], augmented[6], augmented[7],
            augmented[12], augmented[13], augmented[14], augmented[15],
            augmented[20], augmented[21], augmented[22], augmented[23],
            augmented[28], augmented[29], augmented[30], augmented[31]);
        return true;
    }

    /// <summary>Returns the finite inverse of this matrix.</summary>
    /// <exception cref="InvalidOperationException">
    /// The matrix is singular, contains non-finite values, or cannot produce a finite inverse.
    /// </exception>
    public UsdMatrix4d GetInverse() =>
        TryInvert(out UsdMatrix4d inverse)
            ? inverse
            : throw new InvalidOperationException(
                "The matrix is singular or cannot be inverted to a finite result.");

    /// <inheritdoc/>
    public bool Equals(UsdMatrix4d other) =>
        M00.Equals(other.M00) && M01.Equals(other.M01) &&
        M02.Equals(other.M02) && M03.Equals(other.M03) &&
        M10.Equals(other.M10) && M11.Equals(other.M11) &&
        M12.Equals(other.M12) && M13.Equals(other.M13) &&
        M20.Equals(other.M20) && M21.Equals(other.M21) &&
        M22.Equals(other.M22) && M23.Equals(other.M23) &&
        M30.Equals(other.M30) && M31.Equals(other.M31) &&
        M32.Equals(other.M32) && M33.Equals(other.M33);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is UsdMatrix4d other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (double value in ToArray())
        {
            hash.Add(value);
        }
        return hash.ToHashCode();
    }

    /// <summary>Returns whether two matrices are equal.</summary>
    public static bool operator ==(UsdMatrix4d left, UsdMatrix4d right) => left.Equals(right);

    /// <summary>Returns whether two matrices are not equal.</summary>
    public static bool operator !=(UsdMatrix4d left, UsdMatrix4d right) => !left.Equals(right);

    internal static UsdMatrix4d FromNative(OpenUsdNativeMatrix4d value) => new(
        value.M00, value.M01, value.M02, value.M03,
        value.M10, value.M11, value.M12, value.M13,
        value.M20, value.M21, value.M22, value.M23,
        value.M30, value.M31, value.M32, value.M33);

    internal OpenUsdNativeMatrix4d ToNative() => new()
    {
        M00 = M00,
        M01 = M01,
        M02 = M02,
        M03 = M03,
        M10 = M10,
        M11 = M11,
        M12 = M12,
        M13 = M13,
        M20 = M20,
        M21 = M21,
        M22 = M22,
        M23 = M23,
        M30 = M30,
        M31 = M31,
        M32 = M32,
        M33 = M33
    };
}
