// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Asserts that the ray a pointer position produces really is the ray the viewport is drawing
/// through, because every gizmo drag and every interactive body drag follows it.
/// </summary>
public sealed class ViewerPhysicsPointerRayTests
{
    [Test]
    public async Task ThePointerAtTheCentreLooksStraightDownTheCameraAxis()
    {
        ViewerCameraNavigationState state = NewState();

        await Assert.That(ViewerPhysicsPointerRay.TryBuild(
            state, 800d, 600d, 400d, 300d, out ViewerGizmoRay ray)).IsTrue();

        ViewerPhysicsPointerRay.ReadBasis(
            state,
            out ViewerPhysicsVector3 forward,
            out _,
            out _);
        double alignment = ViewerPhysicsVector3.Dot(ray.Direction.Normalized(), forward);
        await Assert.That(Math.Abs(alignment - 1d)).IsLessThan(1e-6d);
    }

    [Test]
    public async Task TheRayStartsAtTheCameraEye()
    {
        ViewerCameraNavigationState state = NewState();

        ViewerPhysicsPointerRay.TryBuild(state, 800d, 600d, 400d, 300d, out ViewerGizmoRay ray);

        Vector3 eye = state.Eye;
        await Assert.That(Math.Abs(ray.Origin.X - eye.X)).IsLessThan(1e-4d);
        await Assert.That(Math.Abs(ray.Origin.Y - eye.Y)).IsLessThan(1e-4d);
        await Assert.That(Math.Abs(ray.Origin.Z - eye.Z)).IsLessThan(1e-4d);
    }

    [Test]
    public async Task TheTopEdgeOpensByHalfTheDeclaredVerticalFieldOfView()
    {
        ViewerCameraNavigationState state = NewState();
        ViewerPhysicsPointerRay.ReadBasis(
            state,
            out ViewerPhysicsVector3 forward,
            out _,
            out _);

        ViewerPhysicsPointerRay.TryBuild(state, 800d, 600d, 400d, 0d, out ViewerGizmoRay ray);

        double angle = Math.Acos(Math.Clamp(
            ViewerPhysicsVector3.Dot(ray.Direction.Normalized(), forward),
            -1d,
            1d));
        await Assert.That(Math.Abs(angle - (state.VerticalFieldOfView / 2d))).IsLessThan(1e-5d);
    }

    [Test]
    public async Task ThePointerToTheRightTiltsTheRayTowardTheCameraRight()
    {
        ViewerCameraNavigationState state = NewState();
        ViewerPhysicsPointerRay.ReadBasis(
            state,
            out _,
            out ViewerPhysicsVector3 right,
            out _);

        ViewerPhysicsPointerRay.TryBuild(state, 800d, 600d, 700d, 300d, out ViewerGizmoRay ray);

        await Assert.That(ViewerPhysicsVector3.Dot(ray.Direction, right)).IsGreaterThan(0d);
    }

    [Test]
    public async Task ThePointerBelowTheCentreTiltsTheRayDown()
    {
        ViewerCameraNavigationState state = NewState();
        ViewerPhysicsPointerRay.ReadBasis(state, out _, out _, out ViewerPhysicsVector3 up);

        ViewerPhysicsPointerRay.TryBuild(state, 800d, 600d, 400d, 500d, out ViewerGizmoRay ray);

        await Assert.That(ViewerPhysicsVector3.Dot(ray.Direction, up)).IsLessThan(0d);
    }

    [Test]
    public async Task AnOrthographicCameraMovesTheOriginAndKeepsOneDirection()
    {
        ViewerCameraNavigationState state = NewState(
            projection: ViewerCameraProjectionMode.Orthographic);

        ViewerPhysicsPointerRay.TryBuild(state, 800d, 600d, 400d, 300d, out ViewerGizmoRay centre);
        ViewerPhysicsPointerRay.TryBuild(state, 800d, 600d, 700d, 300d, out ViewerGizmoRay right);

        double alignment = ViewerPhysicsVector3.Dot(
            centre.Direction.Normalized(),
            right.Direction.Normalized());
        await Assert.That(Math.Abs(alignment - 1d)).IsLessThan(1e-6d);
        await Assert.That(
                ViewerPhysicsVector3.Subtract(right.Origin, centre.Origin).Length)
            .IsGreaterThan(0d);
    }

    [Test]
    public async Task ADegenerateViewportOrPointerProducesNoRay()
    {
        ViewerCameraNavigationState state = NewState();

        await Assert.That(ViewerPhysicsPointerRay.TryBuild(
            state, 0d, 600d, 10d, 10d, out _)).IsFalse();
        await Assert.That(ViewerPhysicsPointerRay.TryBuild(
            state, 800d, 0d, 10d, 10d, out _)).IsFalse();
        await Assert.That(ViewerPhysicsPointerRay.TryBuild(
            state, 800d, 600d, double.NaN, 10d, out _)).IsFalse();
        await Assert.That(ViewerPhysicsPointerRay.TryBuild(
            state, double.PositiveInfinity, 600d, 10d, 10d, out _)).IsFalse();
    }

    [Test]
    public async Task EveryPointerInsideTheViewportProducesAUsableRay()
    {
        ViewerCameraNavigationState state = NewState();

        var violations = new List<string>();
        for (double x = 0d; x <= 800d; x += 40d)
        {
            for (double y = 0d; y <= 600d; y += 40d)
            {
                if (!ViewerPhysicsPointerRay.TryBuild(
                        state, 800d, 600d, x, y, out ViewerGizmoRay ray) ||
                    !ray.IsValid ||
                    !ray.Direction.IsFinite)
                {
                    violations.Add($"({x}, {y})");
                }
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    private static ViewerCameraNavigationState NewState(
        ViewerCameraProjectionMode projection = ViewerCameraProjectionMode.Perspective) =>
        new(
            isAutomatic: false,
            new Vector3(0f, 0f, 0f),
            10f,
            0.3f,
            0.2f,
            projection,
            MathF.PI / 4f,
            8f,
            0.1f,
            1000f,
            800f / 600f);
}
