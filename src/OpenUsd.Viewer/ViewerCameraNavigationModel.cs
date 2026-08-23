// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal enum ViewerCameraProjectionMode
{
    Perspective,
    Orthographic,
}

internal readonly record struct ViewerCameraNavigationState
{
    internal const float MinimumDistance = 1e-4f;
    internal const float MaximumDistance = 1e30f;
    internal const float MinimumOrthographicHeight = 1e-4f;
    internal const float MaximumOrthographicHeight = 1e30f;
    internal const float MinimumNearPlane = 1e-5f;
    internal const float MaximumNearPlane = 1e29f;
    internal const float MaximumFarPlane = 1e31f;
    internal const float MinimumVerticalFieldOfView = MathF.PI / 180f;
    internal const float MaximumVerticalFieldOfView = 175f * MathF.PI / 180f;
    internal const float MaximumPitch = (MathF.PI / 2f) - 1e-3f;
    internal const float MaximumTargetComponent = 1e30f;
    internal const float LegacyYaw = MathF.PI / 4f;
    internal const float LegacyVerticalFieldOfView = MathF.PI / 4f;
    internal const float LegacyNearPlane = 0.1f;
    internal const float LegacyFarPlane = 1000f;

    private const float MinimumAspectRatio = 1e-12f;
    private const float MaximumAspectRatio = 1e12f;
    private static readonly float LegacyDistanceValue = MathF.Sqrt(41f);
    private static readonly float LegacyPitchValue = MathF.Asin(3f / LegacyDistanceValue);
    private static readonly float LegacyOrthographicHeightValue =
        2f * LegacyDistanceValue * MathF.Tan(LegacyVerticalFieldOfView / 2f);

    internal ViewerCameraNavigationState(
        bool isAutomatic,
        Vector3 target,
        float distance,
        float yaw,
        float pitch,
        ViewerCameraProjectionMode projectionMode,
        float verticalFieldOfView,
        float orthographicHeight,
        float nearPlane,
        float farPlane,
        float aspectRatio)
    {
        ThrowIfNotFinite(target, nameof(target));
        ThrowIfNotFinite(distance, nameof(distance));
        ThrowIfNotFinite(yaw, nameof(yaw));
        ThrowIfNotFinite(pitch, nameof(pitch));
        ThrowIfNotFinite(verticalFieldOfView, nameof(verticalFieldOfView));
        ThrowIfNotFinite(orthographicHeight, nameof(orthographicHeight));
        ThrowIfNotFinite(nearPlane, nameof(nearPlane));
        ThrowIfNotFinite(farPlane, nameof(farPlane));
        ThrowIfNotFinite(aspectRatio, nameof(aspectRatio));
        if (projectionMode is not ViewerCameraProjectionMode.Perspective and
            not ViewerCameraProjectionMode.Orthographic)
        {
            throw new ArgumentOutOfRangeException(
                nameof(projectionMode),
                "The camera projection mode is not supported.");
        }

        IsAutomatic = isAutomatic;
        Target = new Vector3(
            Math.Clamp(target.X, -MaximumTargetComponent, MaximumTargetComponent),
            Math.Clamp(target.Y, -MaximumTargetComponent, MaximumTargetComponent),
            Math.Clamp(target.Z, -MaximumTargetComponent, MaximumTargetComponent));
        Distance = Math.Clamp(distance, MinimumDistance, MaximumDistance);
        Yaw = NormalizeYaw(yaw);
        Pitch = Math.Clamp(pitch, -MaximumPitch, MaximumPitch);
        ProjectionMode = projectionMode;
        VerticalFieldOfView = Math.Clamp(
            verticalFieldOfView,
            MinimumVerticalFieldOfView,
            MaximumVerticalFieldOfView);
        OrthographicHeight = Math.Clamp(
            orthographicHeight,
            MinimumOrthographicHeight,
            MaximumOrthographicHeight);
        NearPlane = Math.Clamp(nearPlane, MinimumNearPlane, MaximumNearPlane);
        FarPlane = NormalizeFarPlane(farPlane, NearPlane);
        AspectRatio = Math.Clamp(aspectRatio, MinimumAspectRatio, MaximumAspectRatio);
    }

    internal static float LegacyDistance => LegacyDistanceValue;

    internal static float LegacyPitch => LegacyPitchValue;

    internal static float LegacyOrthographicHeight => LegacyOrthographicHeightValue;

    internal bool IsAutomatic { get; }

    internal Vector3 Target { get; }

    internal float Distance { get; }

    internal float Yaw { get; }

    internal float Pitch { get; }

    internal ViewerCameraProjectionMode ProjectionMode { get; }

    internal float VerticalFieldOfView { get; }

    internal float OrthographicHeight { get; }

    internal float NearPlane { get; }

    internal float FarPlane { get; }

    internal float AspectRatio { get; }

    internal Vector3 Eye => ViewerCameraNavigationMath.CreateEye(this);

    internal CameraState CreateCameraState()
    {
        if (IsAutomatic)
        {
            return CameraState.Default;
        }

        return new CameraState(
            ViewerCameraNavigationMath.CreateViewMatrix(this),
            ViewerCameraNavigationMath.CreateProjectionMatrix(this));
    }

    internal float GetVisibleVerticalSpan()
    {
        if (ProjectionMode == ViewerCameraProjectionMode.Orthographic)
        {
            return OrthographicHeight;
        }

        double span = 2d * Distance * Math.Tan(VerticalFieldOfView / 2d);
        return ViewerCameraNavigationMath.ClampPositive(
            span,
            MinimumOrthographicHeight,
            MaximumOrthographicHeight);
    }

    internal static ViewerCameraNavigationState CreateLegacy(
        bool isAutomatic,
        float aspectRatio) =>
        new(
            isAutomatic,
            Vector3.Zero,
            LegacyDistanceValue,
            LegacyYaw,
            LegacyPitchValue,
            ViewerCameraProjectionMode.Perspective,
            LegacyVerticalFieldOfView,
            LegacyOrthographicHeightValue,
            LegacyNearPlane,
            LegacyFarPlane,
            aspectRatio);

    private static float NormalizeYaw(float yaw)
    {
        double normalized = Math.IEEERemainder(yaw, 2d * Math.PI);
        return (float)normalized;
    }

    private static float NormalizeFarPlane(float farPlane, float nearPlane)
    {
        float normalized = Math.Clamp(
            farPlane,
            MinimumNearPlane * 2f,
            MaximumFarPlane);
        float separation = MathF.Max(MinimumNearPlane, nearPlane * 1e-4f);
        float minimum = nearPlane + separation;
        return MathF.Max(normalized, minimum);
    }

    private static void ThrowIfNotFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Camera values must be finite.");
        }
    }

    private static void ThrowIfNotFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) ||
            !float.IsFinite(value.Y) ||
            !float.IsFinite(value.Z))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Camera vectors must contain only finite values.");
        }
    }
}

internal static class ViewerCameraInputDeltas
{
    internal const float OrbitRadiansPerPixel = MathF.PI / 360f;
    internal const float ZoomExponentPerWheelUnit = -0.12f;

    internal static Vector2 CreateOrbitDelta(Vector2 pointerDelta)
    {
        ThrowIfNotFinite(pointerDelta, nameof(pointerDelta));
        return new Vector2(
            MultiplyToFinite(pointerDelta.X, OrbitRadiansPerPixel),
            MultiplyToFinite(-pointerDelta.Y, OrbitRadiansPerPixel));
    }

    internal static Vector2 CreatePanDelta(
        Vector2 pointerDelta,
        ViewportDimensions viewport,
        in ViewerCameraNavigationState state)
    {
        ThrowIfNotFinite(pointerDelta, nameof(pointerDelta));
        if (viewport.Width == 0 || viewport.Height == 0)
        {
            return Vector2.Zero;
        }

        double visibleHeight = state.GetVisibleVerticalSpan();
        double visibleWidth = visibleHeight * state.AspectRatio;
        double right = -pointerDelta.X * visibleWidth / viewport.Width;
        double up = pointerDelta.Y * visibleHeight / viewport.Height;
        return new Vector2(
            ViewerCameraNavigationMath.ClampSigned(
                right,
                ViewerCameraNavigationState.MaximumTargetComponent),
            ViewerCameraNavigationMath.ClampSigned(
                up,
                ViewerCameraNavigationState.MaximumTargetComponent));
    }

    internal static float CreateZoomExponent(float wheelDelta)
    {
        if (!float.IsFinite(wheelDelta))
        {
            throw new ArgumentOutOfRangeException(
                nameof(wheelDelta),
                "The wheel delta must be finite.");
        }

        return MultiplyToFinite(wheelDelta, ZoomExponentPerWheelUnit);
    }

    private static float MultiplyToFinite(float left, float right) =>
        ViewerCameraNavigationMath.ClampSigned(
            (double)left * right,
            float.MaxValue);

    private static void ThrowIfNotFinite(Vector2 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Pointer deltas must contain only finite values.");
        }
    }
}

internal static class ViewerCameraNavigationMath
{
    private static readonly Vector3 LegacyEye = new(4f, 3f, 4f);

    internal static Vector3 CreateEye(in ViewerCameraNavigationState state)
    {
        if (HasExactLegacyView(state))
        {
            return LegacyEye;
        }

        GetBasis(state, out _, out _, out Vector3 backward);
        return new Vector3(
            ClampSigned(
                (double)state.Target.X + ((double)backward.X * state.Distance),
                ViewerCameraNavigationState.MaximumTargetComponent * 2f),
            ClampSigned(
                (double)state.Target.Y + ((double)backward.Y * state.Distance),
                ViewerCameraNavigationState.MaximumTargetComponent * 2f),
            ClampSigned(
                (double)state.Target.Z + ((double)backward.Z * state.Distance),
                ViewerCameraNavigationState.MaximumTargetComponent * 2f));
    }

    internal static Matrix4x4 CreateViewMatrix(in ViewerCameraNavigationState state)
    {
        if (HasExactLegacyView(state))
        {
            return Matrix4x4.CreateLookAt(LegacyEye, Vector3.Zero, Vector3.UnitY);
        }

        GetBasis(state, out Vector3 right, out Vector3 up, out Vector3 backward);
        float translationX = ClampSigned(
            -Dot(right, state.Target),
            ViewerCameraNavigationState.MaximumTargetComponent * 4f);
        float translationY = ClampSigned(
            -Dot(up, state.Target),
            ViewerCameraNavigationState.MaximumTargetComponent * 4f);
        float translationZ = ClampSigned(
            -Dot(backward, state.Target) - state.Distance,
            ViewerCameraNavigationState.MaximumTargetComponent * 4f);
        return new Matrix4x4(
            right.X, up.X, backward.X, 0f,
            right.Y, up.Y, backward.Y, 0f,
            right.Z, up.Z, backward.Z, 0f,
            translationX, translationY, translationZ, 1f);
    }

    internal static Matrix4x4 CreateProjectionMatrix(
        in ViewerCameraNavigationState state)
    {
        double nearPlane = state.NearPlane;
        double farPlane = state.FarPlane;
        double depthRange = farPlane - nearPlane;
        if (state.ProjectionMode == ViewerCameraProjectionMode.Perspective)
        {
            double tangent = Math.Tan(state.VerticalFieldOfView / 2d);
            double horizontalDenominator = tangent * state.AspectRatio;
            float horizontalScale =
                double.IsFinite(horizontalDenominator) && horizontalDenominator > 0d
                    ? (float)(1d / horizontalDenominator)
                    : (float)((1d / tangent) / state.AspectRatio);
            float verticalScale = (float)(1d / tangent);
            float depthScale = (float)(-((farPlane + nearPlane) / depthRange));
            float depthTranslation =
                (float)(-(2d * nearPlane * farPlane / depthRange));
            return new Matrix4x4(
                horizontalScale, 0f, 0f, 0f,
                0f, verticalScale, 0f, 0f,
                0f, 0f, depthScale, -1f,
                0f, 0f, depthTranslation, 0f);
        }

        double width = (double)state.OrthographicHeight * state.AspectRatio;
        float horizontalOrthographicScale =
            double.IsFinite(width) && width > 0d
                ? (float)(2d / width)
                : (2f / state.OrthographicHeight) / state.AspectRatio;
        float verticalOrthographicScale =
            (float)(2d / state.OrthographicHeight);
        float orthographicDepthScale = (float)(-2d / depthRange);
        float orthographicDepthTranslation =
            (float)(-((farPlane + nearPlane) / depthRange));
        return new Matrix4x4(
            horizontalOrthographicScale, 0f, 0f, 0f,
            0f, verticalOrthographicScale, 0f, 0f,
            0f, 0f, orthographicDepthScale, 0f,
            0f, 0f, orthographicDepthTranslation, 1f);
    }

    internal static Vector3 AddBasisOffsets(
        in ViewerCameraNavigationState state,
        float rightOffset,
        float upOffset)
    {
        GetBasis(state, out Vector3 right, out Vector3 up, out _);
        return new Vector3(
            ClampSigned(
                state.Target.X +
                    ((double)right.X * rightOffset) +
                    ((double)up.X * upOffset),
                ViewerCameraNavigationState.MaximumTargetComponent),
            ClampSigned(
                state.Target.Y +
                    ((double)right.Y * rightOffset) +
                    ((double)up.Y * upOffset),
                ViewerCameraNavigationState.MaximumTargetComponent),
            ClampSigned(
                state.Target.Z +
                    ((double)right.Z * rightOffset) +
                    ((double)up.Z * upOffset),
                ViewerCameraNavigationState.MaximumTargetComponent));
    }

    internal static float ScaleExponentially(
        float value,
        float exponent,
        float minimum,
        float maximum)
    {
        double logarithm = Math.Log(value) + exponent;
        double minimumLogarithm = Math.Log(minimum);
        double maximumLogarithm = Math.Log(maximum);
        if (logarithm <= minimumLogarithm)
        {
            return minimum;
        }
        if (logarithm >= maximumLogarithm)
        {
            return maximum;
        }
        return (float)Math.Exp(logarithm);
    }

    internal static float ClampPositive(double value, float minimum, float maximum)
    {
        if (double.IsNaN(value) || value <= minimum)
        {
            return minimum;
        }
        if (double.IsPositiveInfinity(value) || value >= maximum)
        {
            return maximum;
        }
        return (float)value;
    }

    internal static float ClampSigned(double value, float maximumMagnitude)
    {
        if (double.IsNaN(value))
        {
            return 0f;
        }
        if (value <= -maximumMagnitude)
        {
            return -maximumMagnitude;
        }
        if (value >= maximumMagnitude)
        {
            return maximumMagnitude;
        }
        return (float)value;
    }

    internal static float AspectRatio(ViewportDimensions viewport) =>
        viewport.Width == 0 || viewport.Height == 0
            ? 1f
            : (float)((double)viewport.Width / viewport.Height);

    private static bool HasExactLegacyView(
        in ViewerCameraNavigationState state) =>
        state.Target == Vector3.Zero &&
        state.Distance == ViewerCameraNavigationState.LegacyDistance &&
        state.Yaw == ViewerCameraNavigationState.LegacyYaw &&
        state.Pitch == ViewerCameraNavigationState.LegacyPitch;

    /// <summary>Returns the camera's orthonormal basis in stage space.</summary>
    /// <param name="state">The navigation state to read.</param>
    /// <param name="right">Receives the camera right direction.</param>
    /// <param name="up">Receives the camera up direction.</param>
    /// <param name="backward">Receives the direction the camera looks away from.</param>
    /// <remarks>
    /// The basis is what turns a pointer position into a stage-space ray, which is what a gizmo
    /// drag and an interactive body drag both follow. Exposing the existing derivation keeps those
    /// features on exactly the same camera the viewport is drawing with, instead of on a second
    /// reconstruction that could drift from it.
    /// </remarks>
    internal static void GetCameraBasis(
        in ViewerCameraNavigationState state,
        out Vector3 right,
        out Vector3 up,
        out Vector3 backward) => GetBasis(state, out right, out up, out backward);

    private static void GetBasis(
        in ViewerCameraNavigationState state,
        out Vector3 right,
        out Vector3 up,
        out Vector3 backward)
    {
        float sinYaw = MathF.Sin(state.Yaw);
        float cosYaw = MathF.Cos(state.Yaw);
        float sinPitch = MathF.Sin(state.Pitch);
        float cosPitch = MathF.Cos(state.Pitch);
        backward = Vector3.Normalize(new Vector3(
            cosPitch * sinYaw,
            sinPitch,
            cosPitch * cosYaw));
        right = Vector3.Normalize(new Vector3(cosYaw, 0f, -sinYaw));
        up = Vector3.Cross(backward, right);
    }

    private static double Dot(Vector3 left, Vector3 right) =>
        ((double)left.X * right.X) +
        ((double)left.Y * right.Y) +
        ((double)left.Z * right.Z);
}
