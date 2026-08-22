// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal sealed class ViewerCameraNavigationController
{
    internal const float DefaultFrameMargin = BoundsCameraFraming.DefaultMargin;
    internal const double PointBoundsRadius = BoundsCameraFraming.PointBoundsRadius;

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
        ViewerCameraNavigationState current = MaterializedState();
        if (!BoundsCameraFraming.TryCreate(
            bounds,
            current.VerticalFieldOfView,
            current.AspectRatio,
            out BoundsCameraFraming framing,
            margin))
        {
            return false;
        }
        var next = new ViewerCameraNavigationState(
            isAutomatic: false,
            framing.Target,
            framing.Distance,
            current.Yaw,
            current.Pitch,
            current.ProjectionMode,
            current.VerticalFieldOfView,
            framing.OrthographicHeight,
            framing.NearPlane,
            framing.FarPlane,
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
