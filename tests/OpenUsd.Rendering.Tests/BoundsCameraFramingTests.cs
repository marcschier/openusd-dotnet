// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;

namespace OpenUsd.Rendering.Tests;

public sealed class BoundsCameraFramingTests
{
    [Test]
    public async Task SharedFramingMatchesViewerGeometry()
    {
        var bounds = new UsdBounds3d(
            new UsdVec3d(-1d, -2d, -3d),
            new UsdVec3d(3d, 2d, 1d));
        const float fieldOfView = MathF.PI / 4f;
        const float margin = 1.25f;
        double framedRadius = Math.Sqrt(12d) * margin;

        bool created = BoundsCameraFraming.TryCreate(
            bounds,
            fieldOfView,
            16f / 9f,
            out BoundsCameraFraming framing,
            margin);

        await Assert.That(created).IsTrue();
        await Assert.That(framing.Target).IsEqualTo(new Vector3(1f, 0f, -1f));
        await Assert.That(framing.Distance).IsEqualTo(
            (float)(framedRadius / Math.Sin(fieldOfView / 2d)));
        await Assert.That(framing.NearPlane < framing.Distance - Math.Sqrt(12d))
            .IsTrue();
        await Assert.That(framing.FarPlane > framing.Distance + Math.Sqrt(12d))
            .IsTrue();
    }

    [Test]
    public async Task EmptyBoundsProduceNoFraming()
    {
        bool created = BoundsCameraFraming.TryCreate(
            UsdBounds3d.Empty,
            MathF.PI / 4f,
            1f,
            out BoundsCameraFraming framing);

        await Assert.That(created).IsFalse();
        await Assert.That(framing).IsEqualTo(default(BoundsCameraFraming));
    }

    [Test]
    public async Task NonFiniteBoundsAreRejectedAtTheDetachedBoundsBoundary()
    {
        await Assert.That(() => new UsdBounds3d(
                new UsdVec3d(double.NaN, 0d, 0d),
                new UsdVec3d(1d, 1d, 1d)))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new UsdBounds3d(
                new UsdVec3d(-double.MaxValue, 0d, 0d),
                new UsdVec3d(double.MaxValue, 1d, 1d)))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task ExtremeBoundsSaturateToFiniteFramingLimits()
    {
        var bounds = new UsdBounds3d(
            new UsdVec3d(-8e307, -4e307, -2e307),
            new UsdVec3d(8e307, 4e307, 2e307));

        bool created = BoundsCameraFraming.TryCreate(
            bounds,
            MathF.PI / 4f,
            1e12f,
            out BoundsCameraFraming framing,
            margin: 2f);

        await Assert.That(created).IsTrue();
        await Assert.That(framing.Distance)
            .IsEqualTo(BoundsCameraFraming.MaximumDistance);
        await Assert.That(framing.OrthographicHeight)
            .IsEqualTo(BoundsCameraFraming.MaximumOrthographicHeight);
        await Assert.That(framing.NearPlane)
            .IsEqualTo(BoundsCameraFraming.MinimumNearPlane);
        await Assert.That(framing.FarPlane)
            .IsEqualTo(BoundsCameraFraming.MaximumFarPlane);
        await Assert.That(float.IsFinite(framing.Target.X)).IsTrue();
        await Assert.That(float.IsFinite(framing.Target.Y)).IsTrue();
        await Assert.That(float.IsFinite(framing.Target.Z)).IsTrue();
    }
}
