// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Geom;

namespace OpenUsd.Tests;

public sealed class UsdBounds3dTests
{
    [Test]
    public async Task EmptyBoundsHaveFiniteZeroSemantics()
    {
        UsdBounds3d bounds = default;

        await Assert.That(bounds.IsEmpty).IsTrue();
        await Assert.That(bounds).IsEqualTo(UsdBounds3d.Empty);
        await Assert.That(bounds.Min).IsEqualTo(default(UsdVec3d));
        await Assert.That(bounds.Max).IsEqualTo(default(UsdVec3d));
        await Assert.That(bounds.Center).IsEqualTo(default(UsdVec3d));
        await Assert.That(bounds.Size).IsEqualTo(default(UsdVec3d));
        await Assert.That(bounds.ToString()).IsEqualTo("Empty");
    }

    [Test]
    public async Task NonEmptyBoundsExposeCenterSizeAndValueSemantics()
    {
        var bounds = new UsdBounds3d(
            new UsdVec3d(-2, -4, -6),
            new UsdVec3d(6, 8, 10));
        var same = new UsdBounds3d(
            new UsdVec3d(-2, -4, -6),
            new UsdVec3d(6, 8, 10));

        await Assert.That(bounds.IsEmpty).IsFalse();
        await Assert.That(bounds.Min).IsEqualTo(new UsdVec3d(-2, -4, -6));
        await Assert.That(bounds.Max).IsEqualTo(new UsdVec3d(6, 8, 10));
        await Assert.That(bounds.Center).IsEqualTo(new UsdVec3d(2, 2, 2));
        await Assert.That(bounds.Size).IsEqualTo(new UsdVec3d(8, 12, 16));
        await Assert.That(bounds).IsEqualTo(same);
        await Assert.That(bounds == same).IsTrue();
        await Assert.That(bounds != UsdBounds3d.Empty).IsTrue();
        await Assert.That(bounds.GetHashCode()).IsEqualTo(same.GetHashCode());
        await Assert.That(bounds.ToString())
            .IsEqualTo("[(-2, -4, -6) .. (6, 8, 10)]");
    }

    [Test]
    public async Task BoundsRejectNonFiniteInvertedAndOverflowingRanges()
    {
        await Assert.That(() => new UsdBounds3d(
            new UsdVec3d(double.NaN, 0, 0),
            new UsdVec3d(1, 1, 1))).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new UsdBounds3d(
            new UsdVec3d(0, 0, 0),
            new UsdVec3d(double.PositiveInfinity, 1, 1)))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new UsdBounds3d(
            new UsdVec3d(2, 0, 0),
            new UsdVec3d(1, 1, 1))).Throws<ArgumentException>();
        await Assert.That(() => new UsdBounds3d(
            new UsdVec3d(-double.MaxValue, 0, 0),
            new UsdVec3d(double.MaxValue, 1, 1)))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task PurposeMaskDefinesEverySupportedPurpose()
    {
        uint[] values =
        [
            (uint)UsdGeomPurposeMask.Default,
            (uint)UsdGeomPurposeMask.Proxy,
            (uint)UsdGeomPurposeMask.Render,
            (uint)UsdGeomPurposeMask.Guide,
            (uint)UsdGeomPurposeMask.All
        ];

        await Assert.That(values).IsEquivalentTo(new uint[] { 1, 2, 4, 8, 15 });
    }
}
