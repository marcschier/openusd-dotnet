// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;

namespace OpenUsd.Viewer;

/// <summary>
/// Turns a pointer position over the viewport into a stage-space ray.
/// </summary>
/// <remarks>
/// <para>
/// The ray is what every interactive manipulation follows: a gizmo drag projects it onto an axis or
/// a plane, and a body drag pulls the grabbed point to a depth along it. Deriving it from the same
/// navigation state the viewport draws with is what keeps the manipulation under the pointer
/// instead of near it.
/// </para>
/// <para>
/// The construction is pure so the mapping can be asserted exactly - a pointer in the middle of the
/// viewport must look straight down the camera axis, and the edges must open by the field of view
/// the camera declares - without a window, a renderer, or a pointer device.
/// </para>
/// </remarks>
internal static class ViewerPhysicsPointerRay
{
    /// <summary>Builds the stage-space ray under one pointer position.</summary>
    /// <param name="state">The camera the viewport is drawing with.</param>
    /// <param name="viewportWidth">The viewport width, in logical pixels.</param>
    /// <param name="viewportHeight">The viewport height, in logical pixels.</param>
    /// <param name="pointerX">The pointer position from the viewport's left edge.</param>
    /// <param name="pointerY">The pointer position from the viewport's top edge.</param>
    /// <param name="ray">Receives the ray.</param>
    /// <returns><see langword="true"/> when the viewport and pointer describe a usable ray.</returns>
    internal static bool TryBuild(
        in ViewerCameraNavigationState state,
        double viewportWidth,
        double viewportHeight,
        double pointerX,
        double pointerY,
        out ViewerGizmoRay ray)
    {
        ray = default;
        if (!double.IsFinite(viewportWidth) || !double.IsFinite(viewportHeight) ||
            viewportWidth <= 0d || viewportHeight <= 0d ||
            !double.IsFinite(pointerX) || !double.IsFinite(pointerY))
        {
            return false;
        }

        ViewerCameraNavigationMath.GetCameraBasis(
            state,
            out Vector3 right,
            out Vector3 up,
            out Vector3 backward);
        Vector3 eye = state.Eye;
        if (!IsFinite(right) || !IsFinite(up) || !IsFinite(backward) || !IsFinite(eye))
        {
            return false;
        }

        // Normalized device coordinates, with the pointer's origin moved to the centre and its Y
        // axis flipped so up on screen is up in the scene.
        double aspect = viewportWidth / viewportHeight;
        double ndcX = ((pointerX / viewportWidth) * 2d) - 1d;
        double ndcY = 1d - ((pointerY / viewportHeight) * 2d);
        double tangent = Math.Tan(state.VerticalFieldOfView / 2d);
        if (!double.IsFinite(tangent) || tangent <= 0d || !double.IsFinite(aspect))
        {
            return false;
        }

        ViewerPhysicsVector3 forward = Negate(ToVector(backward));
        ViewerPhysicsVector3 origin = ToVector(eye);
        if (state.ProjectionMode == ViewerCameraProjectionMode.Orthographic)
        {
            // An orthographic camera has one direction for the whole viewport; the pointer moves
            // the ray's origin across the film back instead of tilting it.
            double halfHeight = state.GetVisibleVerticalSpan() / 2d;
            double halfWidth = halfHeight * aspect;
            origin = ViewerPhysicsVector3.Add(
                origin,
                ViewerPhysicsVector3.Add(
                    ViewerPhysicsVector3.Scale(ToVector(right), ndcX * halfWidth),
                    ViewerPhysicsVector3.Scale(ToVector(up), ndcY * halfHeight)));
            ray = new ViewerGizmoRay(origin, forward);
            return ray.IsValid;
        }

        ViewerPhysicsVector3 direction = ViewerPhysicsVector3.Add(
            forward,
            ViewerPhysicsVector3.Add(
                ViewerPhysicsVector3.Scale(ToVector(right), ndcX * tangent * aspect),
                ViewerPhysicsVector3.Scale(ToVector(up), ndcY * tangent)));
        ray = new ViewerGizmoRay(origin, direction.Normalized());
        return ray.IsValid;
    }

    /// <summary>Returns the camera basis as the physics vectors the drive models expect.</summary>
    /// <param name="state">The camera the viewport is drawing with.</param>
    /// <param name="forward">Receives the direction the camera looks along.</param>
    /// <param name="right">Receives the camera right direction.</param>
    /// <param name="up">Receives the camera up direction.</param>
    internal static void ReadBasis(
        in ViewerCameraNavigationState state,
        out ViewerPhysicsVector3 forward,
        out ViewerPhysicsVector3 right,
        out ViewerPhysicsVector3 up)
    {
        ViewerCameraNavigationMath.GetCameraBasis(
            state,
            out Vector3 cameraRight,
            out Vector3 cameraUp,
            out Vector3 backward);
        forward = Negate(ToVector(backward));
        right = ToVector(cameraRight);
        up = ToVector(cameraUp);
    }

    private static ViewerPhysicsVector3 ToVector(Vector3 value) =>
        new(value.X, value.Y, value.Z);

    private static ViewerPhysicsVector3 Negate(ViewerPhysicsVector3 value) =>
        new(-value.X, -value.Y, -value.Z);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
