// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal sealed class ViewerCameraNavigationController
{
    internal const float DefaultFrameMargin = 1.2f;
    internal const double PointBoundsRadius = 0.5d;

    private ViewerCameraNavigationState _state;
    private ViewportDimensions _viewport;

    internal ViewerCameraNavigationController(ViewportDimensions viewport)
    {
        _viewport = viewport;
        _state = ViewerCameraNavigationState.CreateLegacy(
            isAutomatic: true,
            ViewerCameraNavigationMath.AspectRatio(viewport));
    }

    internal ViewerCameraNavigationState State => _state;

    internal ViewportDimensions Viewport => _viewport;

    internal CameraState Camera => _state.CreateCameraState();

    internal bool Orbit(float yawDelta, float pitchDelta)
    {
        ThrowIfNotFinite(yawDelta, nameof(yawDelta));
        ThrowIfNotFinite(pitchDelta, nameof(pitchDelta));
        if (yawDelta == 0f && pitchDelta == 0f)
        {
            return false;
        }

        ViewerCameraNavigationState current = MaterializedState();
        var next = new ViewerCameraNavigationState(
            isAutomatic: false,
            current.Target,
            current.Distance,
            current.Yaw + yawDelta,
            current.Pitch + pitchDelta,
            current.ProjectionMode,
            current.VerticalFieldOfView,
            current.OrthographicHeight,
            current.NearPlane,
            current.FarPlane,
            current.AspectRatio);
        return SetState(next);
    }

    internal bool Pan(float rightOffset, float upOffset)
    {
        ThrowIfNotFinite(rightOffset, nameof(rightOffset));
        ThrowIfNotFinite(upOffset, nameof(upOffset));
        if (rightOffset == 0f && upOffset == 0f)
        {
            return false;
        }

        ViewerCameraNavigationState current = MaterializedState();
        Vector3 target = ViewerCameraNavigationMath.AddBasisOffsets(
            current,
            rightOffset,
            upOffset);
        var next = new ViewerCameraNavigationState(
            isAutomatic: false,
            target,
            current.Distance,
            current.Yaw,
            current.Pitch,
            current.ProjectionMode,
            current.VerticalFieldOfView,
            current.OrthographicHeight,
            current.NearPlane,
            current.FarPlane,
            current.AspectRatio);
        return SetState(next);
    }

    internal bool Zoom(float exponent)
    {
        ThrowIfNotFinite(exponent, nameof(exponent));
        if (exponent == 0f)
        {
            return false;
        }

        ViewerCameraNavigationState current = MaterializedState();
        float distance = current.Distance;
        float orthographicHeight = current.OrthographicHeight;
        if (current.ProjectionMode == ViewerCameraProjectionMode.Perspective)
        {
            distance = ViewerCameraNavigationMath.ScaleExponentially(
                distance,
                exponent,
                ViewerCameraNavigationState.MinimumDistance,
                ViewerCameraNavigationState.MaximumDistance);
        }
        else
        {
            orthographicHeight = ViewerCameraNavigationMath.ScaleExponentially(
                orthographicHeight,
                exponent,
                ViewerCameraNavigationState.MinimumOrthographicHeight,
                ViewerCameraNavigationState.MaximumOrthographicHeight);
        }

        var next = new ViewerCameraNavigationState(
            isAutomatic: false,
            current.Target,
            distance,
            current.Yaw,
            current.Pitch,
            current.ProjectionMode,
            current.VerticalFieldOfView,
            orthographicHeight,
            current.NearPlane,
            current.FarPlane,
            current.AspectRatio);
        return SetState(next);
    }

    internal bool ToggleProjection()
    {
        ViewerCameraNavigationState current = MaterializedState();
        ViewerCameraProjectionMode projectionMode;
        float distance = current.Distance;
        float orthographicHeight = current.OrthographicHeight;
        if (current.ProjectionMode == ViewerCameraProjectionMode.Perspective)
        {
            projectionMode = ViewerCameraProjectionMode.Orthographic;
            orthographicHeight = current.GetVisibleVerticalSpan();
        }
        else
        {
            projectionMode = ViewerCameraProjectionMode.Perspective;
            double denominator = 2d * Math.Tan(current.VerticalFieldOfView / 2d);
            distance = ViewerCameraNavigationMath.ClampPositive(
                current.OrthographicHeight / denominator,
                ViewerCameraNavigationState.MinimumDistance,
                ViewerCameraNavigationState.MaximumDistance);
        }

        var next = new ViewerCameraNavigationState(
            isAutomatic: false,
            current.Target,
            distance,
            current.Yaw,
            current.Pitch,
            projectionMode,
            current.VerticalFieldOfView,
            orthographicHeight,
            current.NearPlane,
            current.FarPlane,
            current.AspectRatio);
        return SetState(next);
    }

    internal bool ResetToAutomatic()
    {
        ViewerCameraNavigationState next = ViewerCameraNavigationState.CreateLegacy(
            isAutomatic: true,
            ViewerCameraNavigationMath.AspectRatio(_viewport));
        return SetState(next);
    }

    internal bool ResetToExplicitPose()
    {
        ViewerCameraNavigationState next = ViewerCameraNavigationState.CreateLegacy(
            isAutomatic: false,
            ViewerCameraNavigationMath.AspectRatio(_viewport));
        return SetState(next);
    }

    internal bool Resize(ViewportDimensions viewport)
    {
        bool viewportChanged = viewport != _viewport;
        float aspectRatio = ViewerCameraNavigationMath.AspectRatio(viewport);
        var next = new ViewerCameraNavigationState(
            _state.IsAutomatic,
            _state.Target,
            _state.Distance,
            _state.Yaw,
            _state.Pitch,
            _state.ProjectionMode,
            _state.VerticalFieldOfView,
            _state.OrthographicHeight,
            _state.NearPlane,
            _state.FarPlane,
            aspectRatio);
        _viewport = viewport;
        return SetState(next) || viewportChanged;
    }

    internal bool FrameBounds(
        UsdBounds3d bounds,
        float margin = DefaultFrameMargin)
    {
        ThrowIfNotFinite(margin, nameof(margin));
        if (margin < 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(margin),
                "The framing margin multiplier must be at least one.");
        }
        if (bounds.IsEmpty)
        {
            return false;
        }

        ViewerCameraNavigationState current = MaterializedState();
        UsdVec3d center = bounds.Center;
        UsdVec3d size = bounds.Size;
        double radius = ComputeRobustRadius(size);
        if (radius == 0d)
        {
            radius = PointBoundsRadius;
        }
        double framedRadius = MultiplySaturating(radius, margin);
        double verticalHalfAngle = current.VerticalFieldOfView / 2d;
        double horizontalHalfAngle = Math.Atan(
            MultiplySaturating(Math.Tan(verticalHalfAngle), current.AspectRatio));
        double limitingHalfAngle = Math.Min(verticalHalfAngle, horizontalHalfAngle);
        double distanceValue = DivideSaturating(
            framedRadius,
            Math.Sin(limitingHalfAngle));
        float distance = ViewerCameraNavigationMath.ClampPositive(
            distanceValue,
            ViewerCameraNavigationState.MinimumDistance,
            ViewerCameraNavigationState.MaximumDistance);
        double narrowAspectScale = Math.Min(1d, current.AspectRatio);
        double orthographicHeightValue = DivideSaturating(
            MultiplySaturating(framedRadius, 2d),
            narrowAspectScale);
        float orthographicHeight = ViewerCameraNavigationMath.ClampPositive(
            orthographicHeightValue,
            ViewerCameraNavigationState.MinimumOrthographicHeight,
            ViewerCameraNavigationState.MaximumOrthographicHeight);
        double nearValue = distance - framedRadius;
        double farValue = AddSaturating(distance, framedRadius);
        float nearPlane = ViewerCameraNavigationMath.ClampPositive(
            nearValue,
            ViewerCameraNavigationState.MinimumNearPlane,
            ViewerCameraNavigationState.MaximumNearPlane);
        float farPlane = ViewerCameraNavigationMath.ClampPositive(
            farValue,
            ViewerCameraNavigationState.MinimumNearPlane * 2f,
            ViewerCameraNavigationState.MaximumFarPlane);
        var target = new Vector3(
            ViewerCameraNavigationMath.ClampSigned(
                center.X,
                ViewerCameraNavigationState.MaximumTargetComponent),
            ViewerCameraNavigationMath.ClampSigned(
                center.Y,
                ViewerCameraNavigationState.MaximumTargetComponent),
            ViewerCameraNavigationMath.ClampSigned(
                center.Z,
                ViewerCameraNavigationState.MaximumTargetComponent));
        var next = new ViewerCameraNavigationState(
            isAutomatic: false,
            target,
            distance,
            current.Yaw,
            current.Pitch,
            current.ProjectionMode,
            current.VerticalFieldOfView,
            orthographicHeight,
            nearPlane,
            farPlane,
            current.AspectRatio);
        return SetState(next);
    }

    private ViewerCameraNavigationState MaterializedState() =>
        _state.IsAutomatic
            ? new ViewerCameraNavigationState(
                isAutomatic: false,
                _state.Target,
                _state.Distance,
                _state.Yaw,
                _state.Pitch,
                _state.ProjectionMode,
                _state.VerticalFieldOfView,
                _state.OrthographicHeight,
                _state.NearPlane,
                _state.FarPlane,
                _state.AspectRatio)
            : _state;

    private bool SetState(in ViewerCameraNavigationState next)
    {
        if (_state == next)
        {
            return false;
        }

        _state = next;
        return true;
    }

    private static double ComputeRobustRadius(UsdVec3d size)
    {
        double maximum = Math.Max(size.X, Math.Max(size.Y, size.Z));
        if (maximum == 0d)
        {
            return 0d;
        }

        double x = size.X / maximum;
        double y = size.Y / maximum;
        double z = size.Z / maximum;
        return maximum * (0.5d * Math.Sqrt((x * x) + (y * y) + (z * z)));
    }

    private static double MultiplySaturating(double left, double right)
    {
        if (left == 0d || right == 0d)
        {
            return 0d;
        }
        if (left >= double.MaxValue / right)
        {
            return double.MaxValue;
        }
        return left * right;
    }

    private static double DivideSaturating(double numerator, double denominator)
    {
        if (denominator <= 0d || numerator >= double.MaxValue * denominator)
        {
            return double.MaxValue;
        }
        return numerator / denominator;
    }

    private static double AddSaturating(double left, double right) =>
        left >= double.MaxValue - right
            ? double.MaxValue
            : left + right;

    private static void ThrowIfNotFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Camera navigation input must be finite.");
        }
    }
}
