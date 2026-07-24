// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Tests;

public sealed class UsdMatrix4dCoverageTests
{
    private static readonly double[] SequentialValues =
    [
        1, 2, 3, 4,
        5, 6, 7, 8,
        9, 10, 11, 12,
        13, 14, 15, 16
    ];

    [Test]
    [Arguments(0, 0, 1)]
    [Arguments(0, 1, 2)]
    [Arguments(0, 2, 3)]
    [Arguments(0, 3, 4)]
    [Arguments(1, 0, 5)]
    [Arguments(1, 1, 6)]
    [Arguments(1, 2, 7)]
    [Arguments(1, 3, 8)]
    [Arguments(2, 0, 9)]
    [Arguments(2, 1, 10)]
    [Arguments(2, 2, 11)]
    [Arguments(2, 3, 12)]
    [Arguments(3, 0, 13)]
    [Arguments(3, 1, 14)]
    [Arguments(3, 2, 15)]
    [Arguments(3, 3, 16)]
    public async Task IndexerReturnsEveryRowMajorElement(int row, int column, double expected)
    {
        UsdMatrix4d matrix = CreateMatrix(SequentialValues);

        await Assert.That(matrix[row, column]).IsEqualTo(expected);
    }

    [Test]
    [Arguments(-1, 0, "row")]
    [Arguments(4, 0, "row")]
    [Arguments(0, -1, "column")]
    [Arguments(0, 4, "column")]
    public async Task IndexerReportsTheInvalidCoordinate(
        int row,
        int column,
        string expectedParameterName)
    {
        UsdMatrix4d matrix = UsdMatrix4d.Identity;

        ArgumentOutOfRangeException exception = CaptureOutOfRange(
            () => _ = matrix[row, column]);

        await Assert.That(exception.ParamName).IsEqualTo(expectedParameterName);
    }

    [Test]
    public async Task ToArrayReturnsIndependentValuesInExactRowMajorOrder()
    {
        UsdMatrix4d matrix = CreateMatrix(SequentialValues);

        double[] first = matrix.ToArray();
        double[] second = matrix.ToArray();

        await Assert.That(first).IsNotSameReferenceAs(second);
        await Assert.That(first.Length).IsEqualTo(16);
        for (int index = 0; index < SequentialValues.Length; index++)
        {
            await Assert.That(first[index]).IsEqualTo(SequentialValues[index])
                .Because($"Element {index} was not in row-major order.");
        }
    }

    [Test]
    public async Task IdentityContainsOnlyUnitDiagonalElements()
    {
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                double expected = row == column ? 1 : 0;
                await Assert.That(UsdMatrix4d.Identity[row, column]).IsEqualTo(expected);
            }
        }

        await Assert.That(
            UsdMatrix4d.Identity.TransformPoint(new UsdVec3d(2, -3, 4)))
            .IsEqualTo(new UsdVec3d(2, -3, 4));
    }

    [Test]
    public async Task TranslationFactoriesUseTheOpenUsdRowVectorSlots()
    {
        var translation = new UsdVec3d(10, -20, 30);

        UsdMatrix4d fromComponents = UsdMatrix4d.CreateTranslation(
            translation.X,
            translation.Y,
            translation.Z);
        UsdMatrix4d fromVector = UsdMatrix4d.CreateTranslation(translation);

        await Assert.That(fromComponents).IsEqualTo(fromVector);
        await Assert.That(fromComponents.ExtractTranslation()).IsEqualTo(translation);
        await Assert.That(fromComponents.M03).IsEqualTo(0);
        await Assert.That(fromComponents.M13).IsEqualTo(0);
        await Assert.That(fromComponents.M23).IsEqualTo(0);
        await Assert.That(fromComponents.M30).IsEqualTo(10);
        await Assert.That(fromComponents.M31).IsEqualTo(-20);
        await Assert.That(fromComponents.M32).IsEqualTo(30);
        await Assert.That(fromComponents.M33).IsEqualTo(1);
    }

    [Test]
    public async Task TransformPointUsesRowVectorAffineArithmeticWhenWIsOne()
    {
        var matrix = new UsdMatrix4d(
            2, 3, 5, 0,
            7, 11, 13, 0,
            17, 19, 23, 0,
            29, 31, 37, 1);

        UsdVec3d transformed = matrix.TransformPoint(new UsdVec3d(1, 2, 3));

        await Assert.That(transformed.X).IsEqualTo(96);
        await Assert.That(transformed.Y).IsEqualTo(113);
        await Assert.That(transformed.Z).IsEqualTo(137);
    }

    [Test]
    public async Task TransformPointDoesNotDivideWhenPerspectiveWIsZero()
    {
        var matrix = new UsdMatrix4d(
            2, 0, 0, 0,
            0, 3, 0, 0,
            0, 0, 4, 0,
            5, 6, 7, 0);

        UsdVec3d transformed = matrix.TransformPoint(new UsdVec3d(1, 2, 3));

        await Assert.That(transformed).IsEqualTo(new UsdVec3d(7, 12, 19));
    }

    [Test]
    public async Task TransformPointDividesByNonUnitPerspectiveW()
    {
        var matrix = new UsdMatrix4d(
            2, 0, 0, 1,
            0, 4, 0, 0,
            0, 0, 6, 0,
            2, 4, 6, 1);

        UsdVec3d transformed = matrix.TransformPoint(new UsdVec3d(1, 2, 3));

        await Assert.That(transformed).IsEqualTo(new UsdVec3d(2, 6, 12));
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(11)]
    [Arguments(12)]
    [Arguments(13)]
    [Arguments(14)]
    [Arguments(15)]
    public async Task EqualityDetectsDifferenceAtEveryElement(int changedIndex)
    {
        double[] changedValues = (double[])SequentialValues.Clone();
        changedValues[changedIndex] = -changedValues[changedIndex];
        UsdMatrix4d baseline = CreateMatrix(SequentialValues);
        UsdMatrix4d changed = CreateMatrix(changedValues);

        await Assert.That(baseline.Equals(changed)).IsFalse();
        await Assert.That(baseline == changed).IsFalse();
        await Assert.That(baseline != changed).IsTrue();
    }

    [Test]
    public async Task EqualityOperatorsObjectEqualityAndHashCodeAgree()
    {
        UsdMatrix4d value = CreateMatrix(SequentialValues);
        UsdMatrix4d same = CreateMatrix((double[])SequentialValues.Clone());

        await Assert.That(value.Equals(same)).IsTrue();
        await Assert.That(value.Equals((object)same)).IsTrue();
        await Assert.That(value.Equals(null)).IsFalse();
        await Assert.That(value.Equals(SequentialValues)).IsFalse();
        await Assert.That(value == same).IsTrue();
        await Assert.That(value != same).IsFalse();
        await Assert.That(value.GetHashCode()).IsEqualTo(same.GetHashCode());
    }

    [Test]
    public async Task NativeConversionRoundTripsEveryElementWithoutDispatch()
    {
        var native = new OpenUsdNativeMatrix4d
        {
            M00 = 1,
            M01 = 2,
            M02 = 3,
            M03 = 4,
            M10 = 5,
            M11 = 6,
            M12 = 7,
            M13 = 8,
            M20 = 9,
            M21 = 10,
            M22 = 11,
            M23 = 12,
            M30 = 13,
            M31 = 14,
            M32 = 15,
            M33 = 16
        };

        UsdMatrix4d managed = UsdMatrix4d.FromNative(native);
        OpenUsdNativeMatrix4d roundTrip = managed.ToNative();
        double[] actual =
        [
            roundTrip.M00, roundTrip.M01, roundTrip.M02, roundTrip.M03,
            roundTrip.M10, roundTrip.M11, roundTrip.M12, roundTrip.M13,
            roundTrip.M20, roundTrip.M21, roundTrip.M22, roundTrip.M23,
            roundTrip.M30, roundTrip.M31, roundTrip.M32, roundTrip.M33
        ];

        for (int index = 0; index < SequentialValues.Length; index++)
        {
            await Assert.That(actual[index]).IsEqualTo(SequentialValues[index]);
        }
    }

    private static UsdMatrix4d CreateMatrix(double[] values) => new(
        values[0], values[1], values[2], values[3],
        values[4], values[5], values[6], values[7],
        values[8], values[9], values[10], values[11],
        values[12], values[13], values[14], values[15]);

    private static ArgumentOutOfRangeException CaptureOutOfRange(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("Expected an ArgumentOutOfRangeException.");
    }
}
