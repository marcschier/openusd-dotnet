// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Asserts the gizmo geometry a viewport drag depends on: the distances it produces, the snapping
/// it applies, and the degenerate configurations where it must refuse to move anything at all.
/// </summary>
public sealed class ViewerPhysicsGizmoTests
{
    private static readonly ViewerPhysicsVector3 X = new(1d, 0d, 0d);
    private static readonly ViewerPhysicsVector3 Y = new(0d, 1d, 0d);
    private static readonly ViewerPhysicsVector3 Z = new(0d, 0d, 1d);

    [Test]
    public async Task AWorldAxisResolvesToTheStageAxisRegardlessOfTheObjectFrame()
    {
        ViewerPhysicsVector3 axis = ViewerGizmoGeometry.ResolveAxis(
            ViewerGizmoAxis.X,
            ViewerGizmoSpace.World,
            new ViewerPhysicsVector3(0d, 0d, 5d),
            Y,
            Z);

        await Assert.That(axis.X).IsEqualTo(1d);
        await Assert.That(axis.Y).IsEqualTo(0d);
        await Assert.That(axis.Z).IsEqualTo(0d);
    }

    [Test]
    public async Task ALocalAxisResolvesToTheObjectFrameAndIsNormalized()
    {
        ViewerPhysicsVector3 axis = ViewerGizmoGeometry.ResolveAxis(
            ViewerGizmoAxis.X,
            ViewerGizmoSpace.Local,
            new ViewerPhysicsVector3(0d, 0d, 5d),
            Y,
            Z);

        await Assert.That(axis.Z).IsEqualTo(1d);
        await Assert.That(Math.Abs(axis.Length - 1d)).IsLessThan(1e-9d);
    }

    [Test]
    public async Task ARayMeetsThePlaneItFacesAndMissesTheOneItIsParallelTo()
    {
        var ray = new ViewerGizmoRay(new ViewerPhysicsVector3(0d, 5d, 0d), new(0d, -1d, 0d));

        await Assert.That(ViewerGizmoGeometry.TryIntersectPlane(
            ray, ViewerPhysicsVector3.Zero, Y, out ViewerPhysicsVector3 hit)).IsTrue();
        await Assert.That(Math.Abs(hit.Y)).IsLessThan(1e-9d);

        var parallel = new ViewerGizmoRay(new ViewerPhysicsVector3(0d, 5d, 0d), X);
        await Assert.That(ViewerGizmoGeometry.TryIntersectPlane(
            parallel, ViewerPhysicsVector3.Zero, Y, out _)).IsFalse();
    }

    [Test]
    public async Task APlaneBehindTheRayOriginIsNeverHit()
    {
        var ray = new ViewerGizmoRay(new ViewerPhysicsVector3(0d, 5d, 0d), Y);

        await Assert.That(ViewerGizmoGeometry.TryIntersectPlane(
            ray, ViewerPhysicsVector3.Zero, Y, out _)).IsFalse();
    }

    [Test]
    public async Task AnAxisDragMovesExactlyAlongTheAxisAndByTheProjectedDistance()
    {
        var start = new ViewerGizmoRay(new ViewerPhysicsVector3(0d, 0d, 10d), new(0d, 0d, -1d));
        var current = new ViewerGizmoRay(new ViewerPhysicsVector3(3d, 0d, 10d), new(0d, 0d, -1d));

        await Assert.That(ViewerGizmoGeometry.TryTranslate(
            ViewerGizmoAxis.X,
            ViewerGizmoSpace.World,
            ViewerPhysicsVector3.Zero,
            new ViewerPhysicsVector3(0d, 0d, -1d),
            X,
            Y,
            Z,
            start,
            current,
            ViewerGizmoSnapSettings.Default,
            out ViewerPhysicsVector3 delta)).IsTrue();

        await Assert.That(Math.Abs(delta.X - 3d)).IsLessThan(1e-6d);
        await Assert.That(Math.Abs(delta.Y)).IsLessThan(1e-9d);
        await Assert.That(Math.Abs(delta.Z)).IsLessThan(1e-9d);
    }

    [Test]
    public async Task ACameraLookingStraightDownTheDraggedAxisRefusesToMoveAnything()
    {
        var start = new ViewerGizmoRay(new ViewerPhysicsVector3(-10d, 0d, 0d), X);
        var current = new ViewerGizmoRay(new ViewerPhysicsVector3(-10d, 1d, 0d), X);

        await Assert.That(ViewerGizmoGeometry.TryTranslate(
            ViewerGizmoAxis.X,
            ViewerGizmoSpace.World,
            ViewerPhysicsVector3.Zero,
            X,
            X,
            Y,
            Z,
            start,
            current,
            ViewerGizmoSnapSettings.Default,
            out _)).IsFalse();
    }

    [Test]
    public async Task SnappingQuantizesAnAxisDragToTheConfiguredIncrement()
    {
        var snap = new ViewerGizmoSnapSettings(IsEnabled: true, 0.5d, 15d, 0.25d);
        var start = new ViewerGizmoRay(new ViewerPhysicsVector3(0d, 0d, 10d), new(0d, 0d, -1d));
        var current = new ViewerGizmoRay(new ViewerPhysicsVector3(1.2d, 0d, 10d), new(0d, 0d, -1d));

        await Assert.That(ViewerGizmoGeometry.TryTranslate(
            ViewerGizmoAxis.X,
            ViewerGizmoSpace.World,
            ViewerPhysicsVector3.Zero,
            new ViewerPhysicsVector3(0d, 0d, -1d),
            X,
            Y,
            Z,
            start,
            current,
            snap,
            out ViewerPhysicsVector3 delta)).IsTrue();

        await Assert.That(Math.Abs(delta.X - 1d)).IsLessThan(1e-6d);
    }

    [Test]
    public async Task AFreeDragFollowsTheViewPlaneThroughTheGizmoPivot()
    {
        var start = new ViewerGizmoRay(new ViewerPhysicsVector3(0d, 0d, 10d), new(0d, 0d, -1d));
        var current = new ViewerGizmoRay(new ViewerPhysicsVector3(2d, 3d, 10d), new(0d, 0d, -1d));

        await Assert.That(ViewerGizmoGeometry.TryTranslate(
            ViewerGizmoAxis.None,
            ViewerGizmoSpace.World,
            ViewerPhysicsVector3.Zero,
            new ViewerPhysicsVector3(0d, 0d, -1d),
            X,
            Y,
            Z,
            start,
            current,
            ViewerGizmoSnapSettings.Default,
            out ViewerPhysicsVector3 delta)).IsTrue();

        await Assert.That(Math.Abs(delta.X - 2d)).IsLessThan(1e-6d);
        await Assert.That(Math.Abs(delta.Y - 3d)).IsLessThan(1e-6d);
    }

    [Test]
    public async Task ARotationDragProducesTheSignedAngleAboutTheAxis()
    {
        var start = new ViewerGizmoRay(new ViewerPhysicsVector3(1d, 5d, 0d), new(0d, -1d, 0d));
        var current = new ViewerGizmoRay(new ViewerPhysicsVector3(0d, 5d, 1d), new(0d, -1d, 0d));

        await Assert.That(ViewerGizmoGeometry.TryRotate(
            ViewerGizmoAxis.Y,
            ViewerGizmoSpace.World,
            ViewerPhysicsVector3.Zero,
            new ViewerPhysicsVector3(0d, -1d, 0d),
            X,
            Y,
            Z,
            start,
            current,
            ViewerGizmoSnapSettings.Default,
            out double degrees)).IsTrue();

        await Assert.That(Math.Abs(Math.Abs(degrees) - 90d)).IsLessThan(1e-6d);
    }

    [Test]
    public async Task SnappingQuantizesARotationToTheConfiguredIncrement()
    {
        var snap = new ViewerGizmoSnapSettings(IsEnabled: true, 0.1d, 45d, 0.1d);

        await Assert.That(snap.SnapRotation(50d)).IsEqualTo(45d);
        await Assert.That(snap.SnapRotation(-70d)).IsEqualTo(-90d);
        await Assert.That(new ViewerGizmoSnapSettings(false, 0.1d, 45d, 0.1d).SnapRotation(50d))
            .IsEqualTo(50d);
    }

    [Test]
    public async Task AScaleDragProducesTheRatioOfTheTwoPointerDistances()
    {
        var start = new ViewerGizmoRay(new ViewerPhysicsVector3(1d, 0d, 10d), new(0d, 0d, -1d));
        var current = new ViewerGizmoRay(new ViewerPhysicsVector3(2d, 0d, 10d), new(0d, 0d, -1d));

        await Assert.That(ViewerGizmoGeometry.TryScale(
            ViewerPhysicsVector3.Zero,
            new ViewerPhysicsVector3(0d, 0d, -1d),
            start,
            current,
            ViewerGizmoSnapSettings.Default,
            out double scale)).IsTrue();

        await Assert.That(Math.Abs(scale - 2d)).IsLessThan(1e-6d);
    }

    [Test]
    public async Task ASnappedScaleNeverCollapsesTheObjectToZero()
    {
        var snap = new ViewerGizmoSnapSettings(IsEnabled: true, 0.1d, 15d, 0.5d);

        await Assert.That(snap.SnapScale(0.1d)).IsGreaterThan(0d);
        await Assert.That(snap.SnapScale(0d)).IsGreaterThan(0d);
        await Assert.That(snap.SnapScale(1.2d)).IsEqualTo(1d);
    }

    [Test]
    public async Task AZeroSnapIncrementLeavesTheValueAloneRatherThanDividingByZero()
    {
        await Assert.That(ViewerGizmoSnapSettings.Quantize(1.234d, 0d)).IsEqualTo(1.234d);
        await Assert.That(ViewerGizmoSnapSettings.Quantize(1.234d, double.NaN)).IsEqualTo(1.234d);
        await Assert.That(ViewerGizmoSnapSettings.Quantize(double.NaN, 0.5d)).IsEqualTo(0d);
    }

    [Test]
    public async Task ADegenerateRayNeverProducesAMovement()
    {
        var degenerate = new ViewerGizmoRay(
            ViewerPhysicsVector3.Zero,
            ViewerPhysicsVector3.Zero);

        await Assert.That(degenerate.IsValid).IsFalse();
        await Assert.That(ViewerGizmoGeometry.TryTranslate(
            ViewerGizmoAxis.X,
            ViewerGizmoSpace.World,
            ViewerPhysicsVector3.Zero,
            Z,
            X,
            Y,
            Z,
            degenerate,
            degenerate,
            ViewerGizmoSnapSettings.Default,
            out _)).IsFalse();
    }

    [Test]
    public async Task EveryDragIsDescribedForTheStatusLine()
    {
        await Assert.That(ViewerGizmoGeometry.Describe(
            ViewerGizmoMode.Translate, ViewerGizmoAxis.X, ViewerGizmoSpace.World, 1.5d))
            .Contains("Move X");
        await Assert.That(ViewerGizmoGeometry.Describe(
            ViewerGizmoMode.Rotate, ViewerGizmoAxis.None, ViewerGizmoSpace.Local, 30d))
            .Contains("view");
        await Assert.That(ViewerGizmoGeometry.Describe(
            ViewerGizmoMode.Scale, ViewerGizmoAxis.None, ViewerGizmoSpace.World, 2d))
            .Contains("Scale");
        await Assert.That(ViewerGizmoGeometry.Describe(
            ViewerGizmoMode.None, ViewerGizmoAxis.None, ViewerGizmoSpace.World, 0d))
            .IsEqualTo("No gizmo");
    }
}
