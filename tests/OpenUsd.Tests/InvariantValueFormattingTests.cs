// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using OpenUsd.Geom;

namespace OpenUsd.Tests;

[NotInParallel]
public sealed class InvariantValueFormattingTests
{
    [Test]
    public async Task DetachedNumericValuesUseInvariantFormatting()
    {
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        CultureInfo previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo commaDecimalCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentCulture = commaDecimalCulture;
            CultureInfo.CurrentUICulture = commaDecimalCulture;

            await Assert.That(new UsdVec2f(1.5f, -2.25f).ToString())
                .IsEqualTo("(1.5, -2.25)");
            await Assert.That(new UsdVec3f(1.25f, -2.5f, 3.75f).ToString())
                .IsEqualTo("(1.25, -2.5, 3.75)");
            await Assert.That(new UsdVec3d(1.25, -2.5, 3.75).ToString())
                .IsEqualTo("(1.25, -2.5, 3.75)");
            await Assert.That(new UsdQuatf(0.5f, 0.1f, 0.2f, 0.3f).ToString())
                .IsEqualTo("(0.5; 0.1, 0.2, 0.3)");

            var bounds = new UsdBounds3d(
                new UsdVec3d(-2.25, -4.5, -6.75),
                new UsdVec3d(6.25, 8.5, 10.75));
            await Assert.That(bounds.ToString())
                .IsEqualTo("[(-2.25, -4.5, -6.75) .. (6.25, 8.5, 10.75)]");

            var extent = new UsdExtent3f(
                new UsdVec3f(-1.25f, -2.5f, -3.75f),
                new UsdVec3f(4.25f, 5.5f, 6.75f));
            await Assert.That(extent.ToString()).IsEqualTo(
                "UsdExtent3f { Minimum = (-1.25, -2.5, -3.75), Maximum = (4.25, 5.5, 6.75) }");

            string camera = CreateCameraState().ToString();
            await Assert.That(camera).Contains("WindowLeft = -1.25");
            await Assert.That(camera).Contains("ClippingNear = 0.1");
            await Assert.That(camera).Contains("HorizontalAperture = 20.955");
            await Assert.That(camera).Contains("WindowCenterX = 0.25");
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static UsdGeomCameraState CreateCameraState() =>
        new(
            UsdGeomCameraProjection.Perspective,
            windowLeft: -1.25,
            windowRight: 1.75,
            windowBottom: -0.75,
            windowTop: 1.25,
            clippingNear: 0.1,
            clippingFar: 1000.5,
            focalLength: 50.5,
            horizontalAperture: 20.955,
            verticalAperture: 15.2908,
            horizontalApertureOffset: 0.125,
            verticalApertureOffset: -0.25,
            focusDistance: 5.5,
            fStop: 2.8);
}
