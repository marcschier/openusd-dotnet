// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd.Viewer;

/// <summary>Identifies what a viewport gizmo manipulates.</summary>
internal enum ViewerGizmoMode
{
    /// <summary>No gizmo is active and the pointer navigates the camera.</summary>
    None,

    /// <summary>The gizmo moves the selection.</summary>
    Translate,

    /// <summary>The gizmo rotates the selection.</summary>
    Rotate,

    /// <summary>The gizmo scales the selection.</summary>
    Scale,

    /// <summary>The gizmo drags a simulated body through the solver.</summary>
    Drag,
}

/// <summary>Identifies the axis a gizmo drag is constrained to.</summary>
internal enum ViewerGizmoAxis
{
    /// <summary>The drag is unconstrained and follows the view plane.</summary>
    None,

    /// <summary>The drag is constrained to the X axis.</summary>
    X,

    /// <summary>The drag is constrained to the Y axis.</summary>
    Y,

    /// <summary>The drag is constrained to the Z axis.</summary>
    Z,
}

/// <summary>Identifies the frame a gizmo's axes are expressed in.</summary>
internal enum ViewerGizmoSpace
{
    /// <summary>The gizmo axes are the stage axes.</summary>
    World,

    /// <summary>The gizmo axes are the manipulated object's axes.</summary>
    Local,
}

/// <summary>The snapping increments a gizmo drag quantizes to.</summary>
/// <param name="IsEnabled">Whether snapping is applied at all.</param>
/// <param name="TranslateStep">The translation increment, in stage linear units.</param>
/// <param name="RotateStepDegrees">The rotation increment, in degrees.</param>
/// <param name="ScaleStep">The scale increment.</param>
/// <remarks>
/// A zero increment disables snapping for that channel rather than dividing by zero. Snapping a
/// value to a zero step has no meaning, and treating it as "snap to nothing" is the only behaviour
/// that cannot produce an infinity in a transform the user is about to author.
/// </remarks>
internal readonly record struct ViewerGizmoSnapSettings(
    bool IsEnabled,
    double TranslateStep,
    double RotateStepDegrees,
    double ScaleStep)
{
    /// <summary>Gets the increments the viewer offers by default.</summary>
    internal static ViewerGizmoSnapSettings Default =>
        new(IsEnabled: false, 0.1d, 15d, 0.1d);

    /// <summary>Quantizes one scalar to an increment.</summary>
    /// <param name="value">The value to quantize.</param>
    /// <param name="step">The increment, or a non-positive value to leave the value alone.</param>
    /// <returns>The quantized value.</returns>
    internal static double Quantize(double value, double step)
    {
        if (!double.IsFinite(value))
        {
            return 0d;
        }

        if (!double.IsFinite(step) || step <= 0d)
        {
            return value;
        }

        return Math.Round(value / step, MidpointRounding.AwayFromZero) * step;
    }

    /// <summary>Quantizes a translation delta.</summary>
    /// <param name="delta">The unsnapped delta.</param>
    /// <returns>The snapped delta.</returns>
    internal ViewerPhysicsVector3 SnapTranslation(ViewerPhysicsVector3 delta) => IsEnabled
        ? new ViewerPhysicsVector3(
            Quantize(delta.X, TranslateStep),
            Quantize(delta.Y, TranslateStep),
            Quantize(delta.Z, TranslateStep))
        : delta;

    /// <summary>Quantizes a rotation delta, in degrees.</summary>
    /// <param name="degrees">The unsnapped rotation.</param>
    /// <returns>The snapped rotation.</returns>
    internal double SnapRotation(double degrees) =>
        IsEnabled ? Quantize(degrees, RotateStepDegrees) : degrees;

    /// <summary>Quantizes a scale factor.</summary>
    /// <param name="scale">The unsnapped scale.</param>
    /// <returns>The snapped scale, never below a thousandth.</returns>
    internal double SnapScale(double scale)
    {
        double snapped = IsEnabled ? Quantize(scale, ScaleStep) : scale;

        // A snapped scale of zero would collapse the object into a degenerate transform that no
        // later drag could recover, so the smallest offered scale is kept instead.
        return snapped <= 0.001d ? 0.001d : snapped;
    }
}

/// <summary>One ray through the scene, in stage space.</summary>
/// <param name="Origin">The ray origin.</param>
/// <param name="Direction">The ray direction, which does not have to be normalized.</param>
internal readonly record struct ViewerGizmoRay(
    ViewerPhysicsVector3 Origin,
    ViewerPhysicsVector3 Direction)
{
    /// <summary>Gets a value indicating whether the ray is usable.</summary>
    internal bool IsValid =>
        Origin.IsFinite && Direction.IsFinite && Direction.Length > 1e-9d;

    /// <summary>Returns the point at a parametric distance along the ray.</summary>
    /// <param name="distance">The parametric distance.</param>
    /// <returns>The point.</returns>
    internal ViewerPhysicsVector3 At(double distance) =>
        ViewerPhysicsVector3.Add(Origin, ViewerPhysicsVector3.Scale(Direction, distance));
}

/// <summary>
/// The pure geometry a transform or physics gizmo drag needs.
/// </summary>
/// <remarks>
/// <para>
/// Every routine here is a pure function of a ray, a plane, and an axis, so the drag behaviour the
/// viewport shows can be asserted exactly without a window, a renderer, or a pointer device. That
/// matters more for a gizmo than for most controls: a drag that is off by a factor or that flips
/// sign at a grazing angle is very hard to see in a screenshot and trivial to catch in a test.
/// </para>
/// <para>
/// Degenerate configurations - a ray parallel to the drag plane, a zero-length axis, a camera
/// looking straight down an axis - return "no movement" rather than an infinity. A gizmo that
/// teleported the selection to the far edge of the scene the moment the camera lined up with the
/// axis being dragged would be worse than one that simply stops responding.
/// </para>
/// </remarks>
internal static class ViewerGizmoGeometry
{
    /// <summary>The smallest usable denominator before a configuration counts as degenerate.</summary>
    private const double Epsilon = 1e-9d;

    /// <summary>Returns the unit axis of one gizmo axis in one frame.</summary>
    /// <param name="axis">The axis being dragged.</param>
    /// <param name="space">The frame the axes are expressed in.</param>
    /// <param name="localX">The object's local X axis, in stage space.</param>
    /// <param name="localY">The object's local Y axis, in stage space.</param>
    /// <param name="localZ">The object's local Z axis, in stage space.</param>
    /// <returns>The unit axis, or zero when the axis is unusable.</returns>
    internal static ViewerPhysicsVector3 ResolveAxis(
        ViewerGizmoAxis axis,
        ViewerGizmoSpace space,
        ViewerPhysicsVector3 localX,
        ViewerPhysicsVector3 localY,
        ViewerPhysicsVector3 localZ)
    {
        if (space == ViewerGizmoSpace.World)
        {
            return axis switch
            {
                ViewerGizmoAxis.X => new ViewerPhysicsVector3(1d, 0d, 0d),
                ViewerGizmoAxis.Y => new ViewerPhysicsVector3(0d, 1d, 0d),
                ViewerGizmoAxis.Z => new ViewerPhysicsVector3(0d, 0d, 1d),
                _ => ViewerPhysicsVector3.Zero,
            };
        }

        return axis switch
        {
            ViewerGizmoAxis.X => localX.Normalized(),
            ViewerGizmoAxis.Y => localY.Normalized(),
            ViewerGizmoAxis.Z => localZ.Normalized(),
            _ => ViewerPhysicsVector3.Zero,
        };
    }

    /// <summary>Intersects a ray with a plane.</summary>
    /// <param name="ray">The ray to intersect.</param>
    /// <param name="planeOrigin">A point on the plane.</param>
    /// <param name="planeNormal">The plane normal, which does not have to be normalized.</param>
    /// <param name="hit">Receives the intersection point.</param>
    /// <returns><see langword="true"/> when the ray meets the plane in front of its origin.</returns>
    internal static bool TryIntersectPlane(
        ViewerGizmoRay ray,
        ViewerPhysicsVector3 planeOrigin,
        ViewerPhysicsVector3 planeNormal,
        out ViewerPhysicsVector3 hit)
    {
        hit = ViewerPhysicsVector3.Zero;
        if (!ray.IsValid || !planeOrigin.IsFinite)
        {
            return false;
        }

        ViewerPhysicsVector3 normal = planeNormal.Normalized();
        if (normal.Length <= Epsilon)
        {
            return false;
        }

        double denominator = ViewerPhysicsVector3.Dot(ray.Direction, normal);
        if (Math.Abs(denominator) <= Epsilon)
        {
            return false;
        }

        double distance = ViewerPhysicsVector3.Dot(
            ViewerPhysicsVector3.Subtract(planeOrigin, ray.Origin),
            normal) / denominator;
        if (!double.IsFinite(distance) || distance <= 0d)
        {
            return false;
        }

        hit = ray.At(distance);
        return hit.IsFinite;
    }

    /// <summary>
    /// Projects a ray onto an axis line, which is what an axis-constrained drag follows.
    /// </summary>
    /// <param name="ray">The pointer ray.</param>
    /// <param name="axisOrigin">A point on the axis line.</param>
    /// <param name="axisDirection">The axis direction, which does not have to be normalized.</param>
    /// <param name="point">Receives the point on the axis closest to the ray.</param>
    /// <returns><see langword="true"/> when the configuration is not degenerate.</returns>
    internal static bool TryProjectOntoAxis(
        ViewerGizmoRay ray,
        ViewerPhysicsVector3 axisOrigin,
        ViewerPhysicsVector3 axisDirection,
        out ViewerPhysicsVector3 point)
    {
        point = axisOrigin;
        if (!ray.IsValid || !axisOrigin.IsFinite)
        {
            return false;
        }

        ViewerPhysicsVector3 axis = axisDirection.Normalized();
        if (axis.Length <= Epsilon)
        {
            return false;
        }

        ViewerPhysicsVector3 direction = ray.Direction.Normalized();
        double axisDotRay = ViewerPhysicsVector3.Dot(axis, direction);
        double denominator = 1d - (axisDotRay * axisDotRay);
        if (Math.Abs(denominator) <= 1e-6d)
        {
            // The camera is looking straight down the axis, so every pointer position maps onto the
            // same axis point and the drag has no meaningful direction to follow.
            return false;
        }

        ViewerPhysicsVector3 offset = ViewerPhysicsVector3.Subtract(axisOrigin, ray.Origin);
        double offsetDotAxis = ViewerPhysicsVector3.Dot(offset, axis);
        double offsetDotRay = ViewerPhysicsVector3.Dot(offset, direction);
        double distance = (offsetDotRay * axisDotRay) - offsetDotAxis;
        distance /= denominator;
        if (!double.IsFinite(distance))
        {
            return false;
        }

        point = ViewerPhysicsVector3.Add(axisOrigin, ViewerPhysicsVector3.Scale(axis, distance));
        return point.IsFinite;
    }

    /// <summary>Computes the translation one drag step produced.</summary>
    /// <param name="axis">The axis the drag is constrained to.</param>
    /// <param name="space">The frame the axes are expressed in.</param>
    /// <param name="pivot">The point the gizmo is drawn at.</param>
    /// <param name="viewDirection">The camera's forward direction, used for a free drag plane.</param>
    /// <param name="localX">The object's local X axis, in stage space.</param>
    /// <param name="localY">The object's local Y axis, in stage space.</param>
    /// <param name="localZ">The object's local Z axis, in stage space.</param>
    /// <param name="start">The ray under the pointer when the drag began.</param>
    /// <param name="current">The ray under the pointer now.</param>
    /// <param name="snap">The increments the drag quantizes to.</param>
    /// <param name="delta">Receives the translation, in stage space.</param>
    /// <returns><see langword="true"/> when the drag produced a usable translation.</returns>
    internal static bool TryTranslate(
        ViewerGizmoAxis axis,
        ViewerGizmoSpace space,
        ViewerPhysicsVector3 pivot,
        ViewerPhysicsVector3 viewDirection,
        ViewerPhysicsVector3 localX,
        ViewerPhysicsVector3 localY,
        ViewerPhysicsVector3 localZ,
        ViewerGizmoRay start,
        ViewerGizmoRay current,
        ViewerGizmoSnapSettings snap,
        out ViewerPhysicsVector3 delta)
    {
        delta = ViewerPhysicsVector3.Zero;
        if (axis == ViewerGizmoAxis.None)
        {
            if (!TryIntersectPlane(start, pivot, viewDirection, out ViewerPhysicsVector3 from) ||
                !TryIntersectPlane(current, pivot, viewDirection, out ViewerPhysicsVector3 to))
            {
                return false;
            }

            delta = snap.SnapTranslation(ViewerPhysicsVector3.Subtract(to, from));
            return true;
        }

        ViewerPhysicsVector3 direction = ResolveAxis(axis, space, localX, localY, localZ);
        if (direction.Length <= Epsilon ||
            !TryProjectOntoAxis(start, pivot, direction, out ViewerPhysicsVector3 startPoint) ||
            !TryProjectOntoAxis(current, pivot, direction, out ViewerPhysicsVector3 currentPoint))
        {
            return false;
        }

        double distance = ViewerPhysicsVector3.Dot(
            ViewerPhysicsVector3.Subtract(currentPoint, startPoint),
            direction);
        distance = snap.IsEnabled
            ? ViewerGizmoSnapSettings.Quantize(distance, snap.TranslateStep)
            : distance;
        delta = ViewerPhysicsVector3.Scale(direction, distance);
        return delta.IsFinite;
    }

    /// <summary>Computes the rotation one drag step produced, in degrees about the axis.</summary>
    /// <param name="axis">The axis the drag rotates about.</param>
    /// <param name="space">The frame the axes are expressed in.</param>
    /// <param name="pivot">The point the gizmo is drawn at.</param>
    /// <param name="viewDirection">The camera's forward direction, used for a free rotation.</param>
    /// <param name="localX">The object's local X axis, in stage space.</param>
    /// <param name="localY">The object's local Y axis, in stage space.</param>
    /// <param name="localZ">The object's local Z axis, in stage space.</param>
    /// <param name="start">The ray under the pointer when the drag began.</param>
    /// <param name="current">The ray under the pointer now.</param>
    /// <param name="snap">The increments the drag quantizes to.</param>
    /// <param name="degrees">Receives the signed rotation, in degrees.</param>
    /// <returns><see langword="true"/> when the drag produced a usable rotation.</returns>
    internal static bool TryRotate(
        ViewerGizmoAxis axis,
        ViewerGizmoSpace space,
        ViewerPhysicsVector3 pivot,
        ViewerPhysicsVector3 viewDirection,
        ViewerPhysicsVector3 localX,
        ViewerPhysicsVector3 localY,
        ViewerPhysicsVector3 localZ,
        ViewerGizmoRay start,
        ViewerGizmoRay current,
        ViewerGizmoSnapSettings snap,
        out double degrees)
    {
        degrees = 0d;
        ViewerPhysicsVector3 normal = axis == ViewerGizmoAxis.None
            ? viewDirection.Normalized()
            : ResolveAxis(axis, space, localX, localY, localZ);
        if (normal.Length <= Epsilon ||
            !TryIntersectPlane(start, pivot, normal, out ViewerPhysicsVector3 from) ||
            !TryIntersectPlane(current, pivot, normal, out ViewerPhysicsVector3 to))
        {
            return false;
        }

        ViewerPhysicsVector3 first = ViewerPhysicsVector3.Subtract(from, pivot);
        ViewerPhysicsVector3 second = ViewerPhysicsVector3.Subtract(to, pivot);
        if (first.Length <= Epsilon || second.Length <= Epsilon)
        {
            return false;
        }

        ViewerPhysicsVector3 cross = ViewerPhysicsVector3.Cross(first, second);
        double sine = ViewerPhysicsVector3.Dot(cross, normal);
        double cosine = ViewerPhysicsVector3.Dot(first, second);
        double radians = Math.Atan2(sine, cosine);
        if (!double.IsFinite(radians))
        {
            return false;
        }

        degrees = snap.SnapRotation(radians * (180d / Math.PI));
        return true;
    }

    /// <summary>Computes the uniform scale factor one drag step produced.</summary>
    /// <param name="pivot">The point the gizmo is drawn at.</param>
    /// <param name="viewDirection">The camera's forward direction.</param>
    /// <param name="start">The ray under the pointer when the drag began.</param>
    /// <param name="current">The ray under the pointer now.</param>
    /// <param name="snap">The increments the drag quantizes to.</param>
    /// <param name="scale">Receives the scale factor, which is one for no change.</param>
    /// <returns><see langword="true"/> when the drag produced a usable scale.</returns>
    internal static bool TryScale(
        ViewerPhysicsVector3 pivot,
        ViewerPhysicsVector3 viewDirection,
        ViewerGizmoRay start,
        ViewerGizmoRay current,
        ViewerGizmoSnapSettings snap,
        out double scale)
    {
        scale = 1d;
        if (!TryIntersectPlane(start, pivot, viewDirection, out ViewerPhysicsVector3 from) ||
            !TryIntersectPlane(current, pivot, viewDirection, out ViewerPhysicsVector3 to))
        {
            return false;
        }

        double reference = ViewerPhysicsVector3.Subtract(from, pivot).Length;
        if (reference <= Epsilon)
        {
            return false;
        }

        double factor = ViewerPhysicsVector3.Subtract(to, pivot).Length / reference;
        if (!double.IsFinite(factor))
        {
            return false;
        }

        scale = snap.SnapScale(factor);
        return true;
    }

    /// <summary>Formats a drag for the status line.</summary>
    /// <param name="mode">The gizmo the drag belongs to.</param>
    /// <param name="axis">The axis the drag is constrained to.</param>
    /// <param name="space">The frame the axes are expressed in.</param>
    /// <param name="magnitude">The translation distance, rotation degrees, or scale factor.</param>
    /// <returns>The status line.</returns>
    internal static string Describe(
        ViewerGizmoMode mode,
        ViewerGizmoAxis axis,
        ViewerGizmoSpace space,
        double magnitude)
    {
        string axisText = axis == ViewerGizmoAxis.None ? "view" : axis.ToString();
        return mode switch
        {
            ViewerGizmoMode.Translate => string.Create(
                CultureInfo.InvariantCulture,
                $"Move {axisText} ({space}) {magnitude:0.###}"),
            ViewerGizmoMode.Rotate => string.Create(
                CultureInfo.InvariantCulture,
                $"Rotate {axisText} ({space}) {magnitude:0.###}\u00b0"),
            ViewerGizmoMode.Scale => string.Create(
                CultureInfo.InvariantCulture,
                $"Scale {magnitude:0.###}x"),
            ViewerGizmoMode.Drag => string.Create(
                CultureInfo.InvariantCulture,
                $"Drag {magnitude:0.###}"),
            _ => "No gizmo",
        };
    }
}
