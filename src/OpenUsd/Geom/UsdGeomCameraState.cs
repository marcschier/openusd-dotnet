// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Text;
using OpenUsd.Interop;

namespace OpenUsd.Geom;

/// <summary>
/// Contains one detached, time-sampled UsdGeomCamera optics and frustum state.
/// </summary>
public readonly record struct UsdGeomCameraState : IUsdDetachedResult
{
    /// <summary>Initializes a finite camera state using the exact Gf frustum window.</summary>
    public UsdGeomCameraState(
        UsdGeomCameraProjection projection,
        double windowLeft,
        double windowRight,
        double windowBottom,
        double windowTop,
        double clippingNear,
        double clippingFar,
        double focalLength,
        double horizontalAperture,
        double verticalAperture,
        double horizontalApertureOffset,
        double verticalApertureOffset,
        double focusDistance,
        double fStop)
    {
        if (projection is not UsdGeomCameraProjection.Perspective and
            not UsdGeomCameraProjection.Orthographic)
        {
            throw new ArgumentOutOfRangeException(
                nameof(projection),
                "The camera projection is not supported.");
        }

        ValidateFinite(windowLeft, nameof(windowLeft));
        ValidateFinite(windowRight, nameof(windowRight));
        ValidateFinite(windowBottom, nameof(windowBottom));
        ValidateFinite(windowTop, nameof(windowTop));
        ValidateFinite(clippingNear, nameof(clippingNear));
        ValidateFinite(clippingFar, nameof(clippingFar));
        ValidateFocalLength(focalLength, projection, nameof(focalLength));
        ValidatePositive(horizontalAperture, nameof(horizontalAperture));
        ValidatePositive(verticalAperture, nameof(verticalAperture));
        ValidateFinite(horizontalApertureOffset, nameof(horizontalApertureOffset));
        ValidateFinite(verticalApertureOffset, nameof(verticalApertureOffset));
        ValidateNonNegative(focusDistance, nameof(focusDistance));
        ValidateNonNegative(fStop, nameof(fStop));

        double windowWidth = windowRight - windowLeft;
        double windowHeight = windowTop - windowBottom;
        double clippingDepth = clippingFar - clippingNear;
        double windowCenterX = (windowLeft / 2d) + (windowRight / 2d);
        double windowCenterY = (windowBottom / 2d) + (windowTop / 2d);
        if (windowLeft >= windowRight ||
            windowBottom >= windowTop ||
            !double.IsFinite(windowWidth) ||
            !double.IsFinite(windowHeight) ||
            !double.IsFinite(windowCenterX) ||
            !double.IsFinite(windowCenterY))
        {
            throw new ArgumentOutOfRangeException(
                nameof(windowRight),
                "The camera window must be finite, ordered, and have finite extents.");
        }
        if (clippingNear >= clippingFar || !double.IsFinite(clippingDepth))
        {
            throw new ArgumentOutOfRangeException(
                nameof(clippingFar),
                "The far clipping plane must be greater than near with finite depth.");
        }
        if (projection == UsdGeomCameraProjection.Perspective && clippingNear <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(clippingNear),
                "A perspective camera requires a positive near clipping plane.");
        }

        Projection = projection;
        WindowLeft = windowLeft;
        WindowRight = windowRight;
        WindowBottom = windowBottom;
        WindowTop = windowTop;
        ClippingNear = clippingNear;
        ClippingFar = clippingFar;
        FocalLength = focalLength;
        HorizontalAperture = horizontalAperture;
        VerticalAperture = verticalAperture;
        HorizontalApertureOffset = horizontalApertureOffset;
        VerticalApertureOffset = verticalApertureOffset;
        FocusDistance = focusDistance;
        FStop = fStop;
    }

    /// <summary>Gets the projection type.</summary>
    public UsdGeomCameraProjection Projection { get; }

    /// <summary>Gets the left edge of the Gf frustum window at reference depth one.</summary>
    public double WindowLeft { get; }

    /// <summary>Gets the right edge of the Gf frustum window at reference depth one.</summary>
    public double WindowRight { get; }

    /// <summary>Gets the bottom edge of the Gf frustum window at reference depth one.</summary>
    public double WindowBottom { get; }

    /// <summary>Gets the top edge of the Gf frustum window at reference depth one.</summary>
    public double WindowTop { get; }

    /// <summary>Gets the near clipping plane in world units.</summary>
    public double ClippingNear { get; }

    /// <summary>Gets the far clipping plane in world units.</summary>
    public double ClippingFar { get; }

    /// <summary>
    /// Gets focal length in tenths of a world unit. Perspective requires a positive value;
    /// orthographic permits zero.
    /// </summary>
    public double FocalLength { get; }

    /// <summary>Gets horizontal aperture in tenths of a world unit.</summary>
    public double HorizontalAperture { get; }

    /// <summary>Gets vertical aperture in tenths of a world unit.</summary>
    public double VerticalAperture { get; }

    /// <summary>Gets horizontal aperture offset in tenths of a world unit.</summary>
    public double HorizontalApertureOffset { get; }

    /// <summary>Gets vertical aperture offset in tenths of a world unit.</summary>
    public double VerticalApertureOffset { get; }

    /// <summary>Gets focus distance in world units.</summary>
    public double FocusDistance { get; }

    /// <summary>Gets the unitless f-stop value; zero means no depth-of-field aperture.</summary>
    public double FStop { get; }

    /// <summary>Gets the exact authored-window width.</summary>
    public double WindowWidth => WindowRight - WindowLeft;

    /// <summary>Gets the exact authored-window height.</summary>
    public double WindowHeight => WindowTop - WindowBottom;

    /// <summary>Gets the horizontal authored-window center.</summary>
    public double WindowCenterX => (WindowLeft / 2d) + (WindowRight / 2d);

    /// <summary>Gets the vertical authored-window center.</summary>
    public double WindowCenterY => (WindowBottom / 2d) + (WindowTop / 2d);

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append(CultureInfo.InvariantCulture, $"Projection = {Projection}");
        builder.Append(CultureInfo.InvariantCulture, $", WindowLeft = {WindowLeft}");
        builder.Append(CultureInfo.InvariantCulture, $", WindowRight = {WindowRight}");
        builder.Append(CultureInfo.InvariantCulture, $", WindowBottom = {WindowBottom}");
        builder.Append(CultureInfo.InvariantCulture, $", WindowTop = {WindowTop}");
        builder.Append(CultureInfo.InvariantCulture, $", ClippingNear = {ClippingNear}");
        builder.Append(CultureInfo.InvariantCulture, $", ClippingFar = {ClippingFar}");
        builder.Append(CultureInfo.InvariantCulture, $", FocalLength = {FocalLength}");
        builder.Append(CultureInfo.InvariantCulture, $", HorizontalAperture = {HorizontalAperture}");
        builder.Append(CultureInfo.InvariantCulture, $", VerticalAperture = {VerticalAperture}");
        builder.Append(
            CultureInfo.InvariantCulture,
            $", HorizontalApertureOffset = {HorizontalApertureOffset}");
        builder.Append(
            CultureInfo.InvariantCulture,
            $", VerticalApertureOffset = {VerticalApertureOffset}");
        builder.Append(CultureInfo.InvariantCulture, $", FocusDistance = {FocusDistance}");
        builder.Append(CultureInfo.InvariantCulture, $", FStop = {FStop}");
        builder.Append(CultureInfo.InvariantCulture, $", WindowWidth = {WindowWidth}");
        builder.Append(CultureInfo.InvariantCulture, $", WindowHeight = {WindowHeight}");
        builder.Append(CultureInfo.InvariantCulture, $", WindowCenterX = {WindowCenterX}");
        builder.Append(CultureInfo.InvariantCulture, $", WindowCenterY = {WindowCenterY}");
        return true;
    }

    internal static UsdGeomCameraState FromNative(OpenUsdNativeCameraState state) =>
        new(
            (UsdGeomCameraProjection)state.Projection,
            state.WindowLeft,
            state.WindowRight,
            state.WindowBottom,
            state.WindowTop,
            state.ClippingNear,
            state.ClippingFar,
            state.FocalLength,
            state.HorizontalAperture,
            state.VerticalAperture,
            state.HorizontalApertureOffset,
            state.VerticalApertureOffset,
            state.FocusDistance,
            state.FStop);

    internal static double ValidateTimeCode(double timeCode)
    {
        if (!double.IsFinite(timeCode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeCode),
                "The time code must be finite.");
        }
        return timeCode;
    }

    private static void ValidateFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "The camera state value must be finite.");
        }
    }

    private static void ValidatePositive(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "The camera state value must be finite and positive.");
        }
    }

    private static void ValidateFocalLength(
        double value,
        UsdGeomCameraProjection projection,
        string parameterName)
    {
        if (!double.IsFinite(value) ||
            value < 0d ||
            (projection == UsdGeomCameraProjection.Perspective && value == 0d))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Focal length must be finite and non-negative, and positive for perspective.");
        }
    }

    private static void ValidateNonNegative(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0d)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "The camera state value must be finite and non-negative.");
        }
    }
}
