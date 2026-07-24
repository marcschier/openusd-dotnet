// Copyright (c) marcschier. Licensed under the MIT License.

using System.Reflection;
using OpenUsd.Geom;

namespace OpenUsd.Tests;

public sealed class UsdGeomCameraStateTests
{
    [Test]
    public async Task DetachedStateUsesComponentValueSemantics()
    {
        var state = CreateState();
        var same = CreateState();
        IUsdDetachedResult detached = state;

        await Assert.That(state).IsEqualTo(same);
        await Assert.That(state.GetHashCode()).IsEqualTo(same.GetHashCode());
        await Assert.That(detached.GetType()).IsEqualTo(typeof(UsdGeomCameraState));
        await Assert.That(state.Projection)
            .IsEqualTo(UsdGeomCameraProjection.Perspective);
        await Assert.That(state.WindowWidth).IsEqualTo(0.5d);
        await Assert.That(state.WindowHeight).IsEqualTo(0.25d);
        await Assert.That(Math.Abs(state.WindowCenterX - 0.05d)).IsLessThan(1e-15d);
        await Assert.That(Math.Abs(state.WindowCenterY - 0.025d)).IsLessThan(1e-15d);
        await Assert.That(state.HorizontalApertureOffset).IsEqualTo(2d);
        await Assert.That(state.VerticalApertureOffset).IsEqualTo(-1d);
        await Assert.That(state.FocusDistance).IsEqualTo(10d);
        await Assert.That(state.FStop).IsEqualTo(2.8d);
    }

    [Test]
    public async Task OrthographicStateAcceptsZeroFocalAndNegativeNearPlane()
    {
        UsdGeomCameraState state = CreateState(
            projection: UsdGeomCameraProjection.Orthographic,
            clippingNear: -10d,
            focalLength: 0d);

        await Assert.That(state.ClippingNear).IsEqualTo(-10d);
        await Assert.That(state.ClippingFar).IsEqualTo(1000d);
        await Assert.That(state.FocalLength).IsEqualTo(0d);
    }

    [Test]
    public async Task StateRejectsMalformedWindowsOpticsAndPerspectiveClipping()
    {
        await Assert.That(() => CreateState(windowRight: -0.2d))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => CreateState(windowTop: double.NaN))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => CreateState(clippingNear: 0d))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => CreateState(clippingFar: 0.1d))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => CreateState(focalLength: 0d))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => CreateState(
            projection: UsdGeomCameraProjection.Orthographic,
            focalLength: -1d)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => CreateState(focusDistance: -1d))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => CreateState(fStop: double.PositiveInfinity))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task CameraPublishesDefaultAndNumericStateOverloads()
    {
        MethodInfo[] methods = typeof(UsdGeomCamera)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(static method => method.Name == nameof(UsdGeomCamera.GetState))
            .OrderBy(static method => method.GetParameters().Length)
            .ToArray();

        await Assert.That(methods).Count().IsEqualTo(2);
        await Assert.That(methods[0].ReturnType).IsEqualTo(typeof(UsdGeomCameraState));
        await Assert.That(methods[0].GetParameters()).IsEmpty();
        await Assert.That(methods[1].ReturnType).IsEqualTo(typeof(UsdGeomCameraState));
        await Assert.That(methods[1].GetParameters()).Count().IsEqualTo(1);
        await Assert.That(methods[1].GetParameters()[0].ParameterType)
            .IsEqualTo(typeof(double));
    }

    [Test]
    public async Task NumericStateRejectsNonFiniteTimeBeforeStageAccess()
    {
        double[] invalidTimes =
        [
            double.NaN,
            double.PositiveInfinity,
            double.NegativeInfinity
        ];

        foreach (double timeCode in invalidTimes)
        {
            ArgumentOutOfRangeException exception = Capture(
                () => default(UsdGeomCamera).GetState(timeCode));
            await Assert.That(exception.ParamName).IsEqualTo("timeCode");
        }
    }

    private static UsdGeomCameraState CreateState(
        UsdGeomCameraProjection projection = UsdGeomCameraProjection.Perspective,
        double windowRight = 0.3d,
        double windowTop = 0.15d,
        double clippingNear = 0.1d,
        double clippingFar = 1000d,
        double focalLength = 50d,
        double focusDistance = 10d,
        double fStop = 2.8d) =>
        new(
            projection,
            windowLeft: -0.2d,
            windowRight,
            windowBottom: -0.1d,
            windowTop,
            clippingNear,
            clippingFar,
            focalLength,
            horizontalAperture: 24d,
            verticalAperture: 18d,
            horizontalApertureOffset: 2d,
            verticalApertureOffset: -1d,
            focusDistance,
            fStop);

    private static ArgumentOutOfRangeException Capture(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("Expected a non-finite time-code rejection.");
    }
}
