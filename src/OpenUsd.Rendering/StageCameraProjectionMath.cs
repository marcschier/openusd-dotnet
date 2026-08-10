// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using OpenUsd.Geom;

namespace OpenUsd.Rendering;

internal readonly record struct StageCameraApertureWindow(
    double Left,
    double Right,
    double Bottom,
    double Top)
{
    internal double Width => Right - Left;

    internal double Height => Top - Bottom;

    internal double CenterX => (Left / 2d) + (Right / 2d);

    internal double CenterY => (Bottom / 2d) + (Top / 2d);
}

internal static class StageCameraProjectionMath
{
    internal static CameraState CreateCameraState(
        in UsdMatrix4d worldToView,
        in UsdGeomCameraState optics,
        ViewportDimensions viewport) =>
        new(
            StageCameraMatrixConversion.ToMatrix4x4(worldToView),
            CreateProjectionMatrix(optics, viewport));

    internal static Matrix4x4 CreateProjectionMatrix(
        in UsdGeomCameraState optics,
        ViewportDimensions viewport)
    {
        StageCameraApertureWindow window = ConformWindow(
            optics.WindowLeft,
            optics.WindowRight,
            optics.WindowBottom,
            optics.WindowTop,
            viewport);
        double windowWidth = window.Width;
        double windowHeight = window.Height;
        double near = optics.ClippingNear;
        double far = optics.ClippingFar;
        double depthRange = far - near;
        if (optics.Projection == UsdGeomCameraProjection.Perspective)
        {
            return new Matrix4x4(
                ToFiniteFloat(2d / windowWidth), 0f, 0f, 0f,
                0f, ToFiniteFloat(2d / windowHeight), 0f, 0f,
                ToFiniteFloat((window.Right + window.Left) / windowWidth),
                ToFiniteFloat((window.Top + window.Bottom) / windowHeight),
                ToFiniteFloat(-((far + near) / depthRange)),
                -1f,
                0f, 0f, ToFiniteFloat(-(2d * near * far / depthRange)), 0f);
        }

        return new Matrix4x4(
            ToFiniteFloat(2d / windowWidth), 0f, 0f, 0f,
            0f, ToFiniteFloat(2d / windowHeight), 0f, 0f,
            0f, 0f, ToFiniteFloat(-2d / depthRange), 0f,
            ToFiniteFloat(-((window.Right + window.Left) / windowWidth)),
            ToFiniteFloat(-((window.Top + window.Bottom) / windowHeight)),
            ToFiniteFloat(-((far + near) / depthRange)),
            1f);
    }

    internal static StageCameraApertureWindow ConformWindow(
        double left,
        double right,
        double bottom,
        double top,
        ViewportDimensions viewport)
    {
        StageCameraApertureWindow authored = CreateWindow(
            left,
            right,
            bottom,
            top);
        double authoredAspect = authored.Width / authored.Height;
        double viewportAspect = viewport.Width == 0 || viewport.Height == 0
            ? authoredAspect
            : (double)viewport.Width / viewport.Height;
        if (!double.IsFinite(viewportAspect) || viewportAspect <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(viewport),
                "The stage-camera viewport aspect must be finite and positive.");
        }

        if (viewportAspect >= authoredAspect)
        {
            double targetWidth = authored.Height * viewportAspect;
            double expansion = (targetWidth - authored.Width) / 2d;
            return CreateWindow(
                authored.Left - expansion,
                authored.Right + expansion,
                authored.Bottom,
                authored.Top);
        }

        double targetHeight = authored.Width / viewportAspect;
        double verticalExpansion = (targetHeight - authored.Height) / 2d;
        return CreateWindow(
            authored.Left,
            authored.Right,
            authored.Bottom - verticalExpansion,
            authored.Top + verticalExpansion);
    }

    private static float ToFiniteFloat(double value)
    {
        if (!double.IsFinite(value) ||
            value < -float.MaxValue ||
            value > float.MaxValue)
        {
            throw new InvalidOperationException(
                "The stage-camera projection cannot be represented by finite float matrices.");
        }

        float converted = (float)value;
        if (!float.IsFinite(converted) || (converted == 0f && value != 0d))
        {
            throw new InvalidOperationException(
                "The stage-camera projection is outside the finite float range.");
        }
        return converted;
    }

    private static StageCameraApertureWindow CreateWindow(
        double left,
        double right,
        double bottom,
        double top)
    {
        var window = new StageCameraApertureWindow(
            left,
            right,
            bottom,
            top);
        if (!double.IsFinite(left) ||
            !double.IsFinite(right) ||
            !double.IsFinite(bottom) ||
            !double.IsFinite(top) ||
            left >= right ||
            bottom >= top ||
            !double.IsFinite(window.Width) ||
            !double.IsFinite(window.Height) ||
            !double.IsFinite(window.CenterX) ||
            !double.IsFinite(window.CenterY))
        {
            throw new ArgumentOutOfRangeException(
                nameof(right),
                "The stage-camera frustum window must be finite and ordered.");
        }
        return window;
    }
}

internal static class StageCameraMatrixConversion
{
    internal static bool IsFinite(in UsdMatrix4d value) =>
        double.IsFinite(value.M00) &&
        double.IsFinite(value.M01) &&
        double.IsFinite(value.M02) &&
        double.IsFinite(value.M03) &&
        double.IsFinite(value.M10) &&
        double.IsFinite(value.M11) &&
        double.IsFinite(value.M12) &&
        double.IsFinite(value.M13) &&
        double.IsFinite(value.M20) &&
        double.IsFinite(value.M21) &&
        double.IsFinite(value.M22) &&
        double.IsFinite(value.M23) &&
        double.IsFinite(value.M30) &&
        double.IsFinite(value.M31) &&
        double.IsFinite(value.M32) &&
        double.IsFinite(value.M33);

    internal static Matrix4x4 ToMatrix4x4(in UsdMatrix4d value)
    {
        if (!IsFinite(value))
        {
            throw new InvalidOperationException(
                "The stage-camera matrix must contain only finite values.");
        }

        return new Matrix4x4(
            ToFiniteFloat(value.M00), ToFiniteFloat(value.M01),
            ToFiniteFloat(value.M02), ToFiniteFloat(value.M03),
            ToFiniteFloat(value.M10), ToFiniteFloat(value.M11),
            ToFiniteFloat(value.M12), ToFiniteFloat(value.M13),
            ToFiniteFloat(value.M20), ToFiniteFloat(value.M21),
            ToFiniteFloat(value.M22), ToFiniteFloat(value.M23),
            ToFiniteFloat(value.M30), ToFiniteFloat(value.M31),
            ToFiniteFloat(value.M32), ToFiniteFloat(value.M33));
    }

    private static float ToFiniteFloat(double value)
    {
        if (value < -float.MaxValue || value > float.MaxValue)
        {
            throw new InvalidOperationException(
                "The stage-camera view matrix is outside the finite float range.");
        }

        float converted = (float)value;
        if (!float.IsFinite(converted) || (converted == 0f && value != 0d))
        {
            throw new InvalidOperationException(
                "The stage-camera view matrix cannot be represented as finite floats.");
        }
        return converted;
    }
}
