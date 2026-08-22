// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;

namespace OpenUsd.Rendering;

internal readonly record struct BoundsCameraFraming(
    Vector3 Target,
    float Distance,
    float OrthographicHeight,
    float NearPlane,
    float FarPlane)
{
    internal const float DefaultMargin = 1.2f;
    internal const double PointBoundsRadius = 0.5d;
    internal const float MinimumDistance = 1e-4f;
    internal const float MaximumDistance = 1e30f;
    internal const float MinimumOrthographicHeight = 1e-4f;
    internal const float MaximumOrthographicHeight = 1e30f;
    internal const float MinimumNearPlane = 1e-5f;
    internal const float MaximumNearPlane = 1e29f;
    internal const float MaximumFarPlane = 1e31f;
    internal const float MaximumTargetComponent = 1e30f;

    internal static bool TryCreate(
        UsdBounds3d bounds,
        float verticalFieldOfView,
        float aspectRatio,
        out BoundsCameraFraming framing,
        float margin = DefaultMargin)
    {
        ThrowIfNotFinite(verticalFieldOfView, nameof(verticalFieldOfView));
        ThrowIfNotFinite(aspectRatio, nameof(aspectRatio));
        ThrowIfNotFinite(margin, nameof(margin));
        if (verticalFieldOfView <= 0f || verticalFieldOfView >= MathF.PI)
        {
            throw new ArgumentOutOfRangeException(
                nameof(verticalFieldOfView),
                "The vertical field of view must be between zero and pi.");
        }
        if (aspectRatio <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(aspectRatio),
                "The camera aspect ratio must be positive.");
        }
        if (margin < 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(margin),
                "The framing margin multiplier must be at least one.");
        }
        if (bounds.IsEmpty)
        {
            framing = default;
            return false;
        }

        UsdVec3d center = bounds.Center;
        UsdVec3d size = bounds.Size;
        if (!IsFinite(center) || !IsFinite(size))
        {
            framing = default;
            return false;
        }

        double radius = ComputeRobustRadius(size);
        if (radius == 0d)
        {
            radius = PointBoundsRadius;
        }
        double framedRadius = MultiplySaturating(radius, margin);
        double verticalHalfAngle = verticalFieldOfView / 2d;
        double horizontalHalfAngle = Math.Atan(
            MultiplySaturating(Math.Tan(verticalHalfAngle), aspectRatio));
        double limitingHalfAngle = Math.Min(verticalHalfAngle, horizontalHalfAngle);
        float distance = ClampPositive(
            DivideSaturating(framedRadius, Math.Sin(limitingHalfAngle)),
            MinimumDistance,
            MaximumDistance);
        float orthographicHeight = ClampPositive(
            DivideSaturating(
                MultiplySaturating(framedRadius, 2d),
                Math.Min(1d, aspectRatio)),
            MinimumOrthographicHeight,
            MaximumOrthographicHeight);
        float nearPlane = ClampPositive(
            distance - framedRadius,
            MinimumNearPlane,
            MaximumNearPlane);
        float farPlane = ClampPositive(
            AddSaturating(distance, framedRadius),
            MinimumNearPlane * 2f,
            MaximumFarPlane);
        framing = new BoundsCameraFraming(
            new Vector3(
                ClampSigned(center.X, MaximumTargetComponent),
                ClampSigned(center.Y, MaximumTargetComponent),
                ClampSigned(center.Z, MaximumTargetComponent)),
            distance,
            orthographicHeight,
            nearPlane,
            farPlane);
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

    private static float ClampPositive(double value, float minimum, float maximum)
    {
        if (!double.IsFinite(value) || value >= maximum)
        {
            return maximum;
        }
        if (value <= minimum)
        {
            return minimum;
        }
        return (float)value;
    }

    private static float ClampSigned(double value, float maximumMagnitude)
    {
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

    private static void ThrowIfNotFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Camera framing input must be finite.");
        }
    }

    private static bool IsFinite(UsdVec3d value) =>
        double.IsFinite(value.X) &&
        double.IsFinite(value.Y) &&
        double.IsFinite(value.Z);
}
