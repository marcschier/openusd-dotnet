// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Tests;

public sealed class UsdMatrix4dInversionTests
{
    [Test]
    public async Task IdentityInvertsExactly()
    {
        bool succeeded = UsdMatrix4d.Identity.TryInvert(out UsdMatrix4d inverse);

        await Assert.That(succeeded).IsTrue();
        await Assert.That(inverse).IsEqualTo(UsdMatrix4d.Identity);
        await Assert.That(UsdMatrix4d.Identity.GetInverse()).IsEqualTo(UsdMatrix4d.Identity);
    }

    [Test]
    public async Task TranslationInvertsExactlyUsingRowVectorSlots()
    {
        UsdMatrix4d matrix = UsdMatrix4d.CreateTranslation(10, -20, 30);

        bool succeeded = matrix.TryInvert(out UsdMatrix4d inverse);

        await Assert.That(succeeded).IsTrue();
        await Assert.That(inverse).IsEqualTo(UsdMatrix4d.CreateTranslation(-10, 20, -30));
        await Assert.That(
            inverse.TransformPoint(matrix.TransformPoint(new UsdVec3d(2, 3, 5))))
            .IsEqualTo(new UsdVec3d(2, 3, 5));
    }

    [Test]
    public async Task ScaleRotationAndTranslationRoundTripInBothOrders()
    {
        const double angle = 0.7;
        double cosine = Math.Cos(angle);
        double sine = Math.Sin(angle);
        var matrix = new UsdMatrix4d(
            2 * cosine, 2 * sine, 0, 0,
            -3 * sine, 3 * cosine, 0, 0,
            0, 0, 4, 0,
            5, -6, 7, 1);

        bool succeeded = matrix.TryInvert(out UsdMatrix4d inverse);

        await Assert.That(succeeded).IsTrue();
        await AssertApproximatelyIdentityAsync(Multiply(matrix, inverse), 1e-13);
        await AssertApproximatelyIdentityAsync(Multiply(inverse, matrix), 1e-13);
    }

    [Test]
    public async Task GeneralMatrixRoundTripsInBothOrders()
    {
        var matrix = new UsdMatrix4d(
            4, 1, 2, 0.5,
            0, 3, -1, 2,
            2, 0, 5, -1,
            1, -2, 0.25, 4);

        bool succeeded = matrix.TryInvert(out UsdMatrix4d inverse);

        await Assert.That(succeeded).IsTrue();
        await Assert.That(matrix.GetInverse()).IsEqualTo(inverse);
        await AssertApproximatelyIdentityAsync(Multiply(matrix, inverse), 1e-13);
        await AssertApproximatelyIdentityAsync(Multiply(inverse, matrix), 1e-13);
    }

    [Test]
    public async Task SingularAndNonFiniteMatricesFailWithZeroOutput()
    {
        var singular = new UsdMatrix4d(
            1, 2, 3, 4,
            1, 2, 3, 4,
            0, 1, 0, 0,
            0, 0, 0, 1);
        var nonFinite = new UsdMatrix4d(
            1, 0, 0, 0,
            0, double.NaN, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1);
        var overflowingInverse = new UsdMatrix4d(
            double.Epsilon, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1);

        await AssertFailureAsync(singular);
        await AssertFailureAsync(nonFinite);
        await AssertFailureAsync(overflowingInverse);
    }

    [Test]
    public async Task ExtremeFiniteMatricesRetainFiniteInverses()
    {
        var extremeScale = new UsdMatrix4d(
            1e-300, 0, 0, 0,
            0, -1e300, 0, 0,
            0, 0, 1e-150, 0,
            0, 0, 0, -1e150);
        UsdMatrix4d extremeTranslation =
            UsdMatrix4d.CreateTranslation(1e300, -1e300, 1e-300);

        await AssertFiniteRoundTripAsync(extremeScale, 1e-15);
        await AssertFiniteRoundTripAsync(extremeTranslation, 0);
    }

    [Test]
    public async Task TryInvertAllocatesNoManagedMemory()
    {
        var matrix = new UsdMatrix4d(
            4, 1, 2, 0.5,
            0, 3, -1, 2,
            2, 0, 5, -1,
            1, -2, 0.25, 4);
        for (int index = 0; index < 256; index++)
        {
            _ = matrix.TryInvert(out _);
        }

        bool succeeded = true;
        double checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 4096; index++)
        {
            succeeded &= matrix.TryInvert(out UsdMatrix4d inverse);
            checksum += inverse.M00;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(succeeded).IsTrue();
        await Assert.That(checksum).IsNotEqualTo(0);
        await Assert.That(allocated).IsEqualTo(0);
    }

    private static async Task AssertFailureAsync(UsdMatrix4d matrix)
    {
        bool succeeded = matrix.TryInvert(out UsdMatrix4d inverse);
        InvalidOperationException exception = CaptureInvalidOperation(matrix.GetInverse);

        await Assert.That(succeeded).IsFalse();
        await Assert.That(inverse).IsEqualTo(default(UsdMatrix4d));
        await Assert.That(exception.Message).Contains("singular");
    }

    private static async Task AssertFiniteRoundTripAsync(
        UsdMatrix4d matrix,
        double tolerance)
    {
        bool succeeded = matrix.TryInvert(out UsdMatrix4d inverse);

        await Assert.That(succeeded).IsTrue();
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                await Assert.That(double.IsFinite(inverse[row, column])).IsTrue();
            }
        }
        await AssertApproximatelyIdentityAsync(Multiply(matrix, inverse), tolerance);
        await AssertApproximatelyIdentityAsync(Multiply(inverse, matrix), tolerance);
    }

    private static async Task AssertApproximatelyIdentityAsync(
        UsdMatrix4d matrix,
        double tolerance)
    {
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                double expected = row == column ? 1 : 0;
                await Assert.That(Math.Abs(matrix[row, column] - expected))
                    .IsLessThanOrEqualTo(tolerance)
                    .Because($"Element ({row}, {column}) was not identity.");
            }
        }
    }

    private static UsdMatrix4d Multiply(UsdMatrix4d left, UsdMatrix4d right)
    {
        Span<double> values = stackalloc double[16];
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                double value = 0;
                for (int index = 0; index < 4; index++)
                {
                    value = Math.FusedMultiplyAdd(
                        left[row, index],
                        right[index, column],
                        value);
                }
                values[(row * 4) + column] = value;
            }
        }
        return new UsdMatrix4d(
            values[0], values[1], values[2], values[3],
            values[4], values[5], values[6], values[7],
            values[8], values[9], values[10], values[11],
            values[12], values[13], values[14], values[15]);
    }

    private static InvalidOperationException CaptureInvalidOperation(Func<UsdMatrix4d> action)
    {
        try
        {
            _ = action();
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("Expected matrix inversion to fail.");
    }
}
