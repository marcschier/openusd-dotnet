// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;

namespace OpenUsd.Rendering.Tests;

public sealed class RenderProductFramingTests
{
    [Test]
    [Arguments("expandAperture", -1d, 3d, -2d, 2d, 1d)]
    [Arguments("cropAperture", 0d, 2d, -1d, 1d, 1d)]
    [Arguments("adjustApertureWidth", 0d, 2d, -1d, 1d, 1d)]
    [Arguments("adjustApertureHeight", -1d, 3d, -2d, 2d, 1d)]
    [Arguments("adjustPixelAspectRatio", -1d, 3d, -1d, 1d, 2d)]
    public async Task ProductConformPolicyPreservesTheAuthoredOffAxisCenter(
        string policy,
        double left,
        double right,
        double bottom,
        double top,
        double pixelAspectRatio)
    {
        RenderProductFraming framing = RenderProductFraming.Resolve(
            new StageCameraApertureWindow(-1, 3, -1, 1),
            new ViewportDimensions(100, 100),
            1,
            policy,
            new Vector4(0, 0, 1, 1));

        await Assert.That(framing.Window).IsEqualTo(new StageCameraApertureWindow(left, right, bottom, top));
        await Assert.That(framing.PixelAspectRatio).IsEqualTo(pixelAspectRatio);
        await Assert.That(framing.OutputDimensions).IsEqualTo(new ViewportDimensions(100, 100));
        await Assert.That(framing.DataWindowMinX).IsEqualTo(0);
        await Assert.That(framing.DataWindowMinY).IsEqualTo(0);
    }

    [Test]
    public async Task DataWindowIncludesMinimumPixelCentersAndExcludesMaximumCenters()
    {
        RenderProductFraming framing = RenderProductFraming.Resolve(
            new StageCameraApertureWindow(-1, 1, -1, 1),
            new ViewportDimensions(4, 4),
            1,
            "expandAperture",
            new Vector4(0.375f, 0.125f, 0.625f, 0.625f));

        await Assert.That(framing.DataWindowMinX).IsEqualTo(1);
        await Assert.That(framing.DataWindowMinY).IsEqualTo(0);
        await Assert.That(framing.OutputDimensions).IsEqualTo(new ViewportDimensions(1, 2));
        await Assert.That(framing.Window).IsEqualTo(new StageCameraApertureWindow(-0.5, 0, -1, 0));
    }

    [Test]
    public async Task OverscanExtendsTheImageInsteadOfClampingItsAuthoredWindow()
    {
        RenderProductFraming framing = RenderProductFraming.Resolve(
            new StageCameraApertureWindow(-1, 1, -1, 1),
            new ViewportDimensions(4, 4),
            1,
            "expandAperture",
            new Vector4(-0.25f, -0.25f, 1.25f, 1.25f));

        await Assert.That(framing.DataWindowMinX).IsEqualTo(-1);
        await Assert.That(framing.DataWindowMinY).IsEqualTo(-1);
        await Assert.That(framing.OutputDimensions).IsEqualTo(new ViewportDimensions(6, 6));
        await Assert.That(framing.Window).IsEqualTo(new StageCameraApertureWindow(-1.5, 1.5, -1.5, 1.5));
    }

    [Test]
    public async Task AdjacentTilesPartitionAnOddResolutionWithoutRepeatingBoundaryPixels()
    {
        var aperture = new StageCameraApertureWindow(-1, 1, -1, 1);
        var resolution = new ViewportDimensions(5, 4);
        RenderProductFraming left = RenderProductFraming.Resolve(
            aperture, resolution, 1, "expandAperture", new Vector4(0, 0, 0.5f, 1));
        RenderProductFraming right = RenderProductFraming.Resolve(
            aperture, resolution, 1, "expandAperture", new Vector4(0.5f, 0, 1, 1));

        await Assert.That(left.DataWindowMinX).IsEqualTo(0);
        await Assert.That(left.OutputDimensions.Width).IsEqualTo(2);
        await Assert.That(right.DataWindowMinX).IsEqualTo(2);
        await Assert.That(right.OutputDimensions.Width).IsEqualTo(3);
        await Assert.That(left.Window).IsEqualTo(new StageCameraApertureWindow(-1.25, -0.25, -1, 1));
        await Assert.That(right.Window).IsEqualTo(new StageCameraApertureWindow(-0.25, 1.25, -1, 1));
    }

    [Test]
    public async Task NonSquarePixelsAffectConformanceUnlessThePolicyAdjustsThePixelAspect()
    {
        var aperture = new StageCameraApertureWindow(-1, 3, -1, 1);
        var resolution = new ViewportDimensions(100, 50);
        RenderProductFraming expanded = RenderProductFraming.Resolve(
            aperture, resolution, 2, "expandAperture", new Vector4(0, 0, 1, 1));
        RenderProductFraming adjusted = RenderProductFraming.Resolve(
            aperture, resolution, 2, "adjustPixelAspectRatio", new Vector4(0, 0, 1, 1));

        await Assert.That(expanded.Window).IsEqualTo(new StageCameraApertureWindow(-3, 5, -1, 1));
        await Assert.That(expanded.PixelAspectRatio).IsEqualTo(2d);
        await Assert.That(adjusted.Window).IsEqualTo(aperture);
        await Assert.That(adjusted.PixelAspectRatio).IsEqualTo(1d);
    }

    [Test]
    [Arguments(float.NaN, 0f, 1f, 1f)]
    [Arguments(0f, float.PositiveInfinity, 1f, 1f)]
    [Arguments(1f, 0f, 0f, 1f)]
    [Arguments(0f, 0.5f, 1f, 0.5f)]
    public async Task InvalidDataWindowsAreRejectedRatherThanClamped(float x, float y, float z, float w)
    {
        await Assert.That(() => RenderProductFraming.Resolve(
            new StageCameraApertureWindow(-1, 1, -1, 1),
            new ViewportDimensions(4, 4),
            1, "expandAperture", new Vector4(x, y, z, w))).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0.13f, 0.15f)]
    [Arguments(-float.MaxValue, float.MaxValue)]
    public async Task UnsampleablePixelExtentsAreRejectedExplicitly(float minimum, float maximum)
    {
        await Assert.That(() => RenderProductFraming.Resolve(
            new StageCameraApertureWindow(-1, 1, -1, 1),
            new ViewportDimensions(4, 4),
            1, "expandAperture", new Vector4(minimum, minimum, maximum, maximum)))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(0d)]
    [Arguments(-1d)]
    [Arguments(double.NaN)]
    [Arguments(double.PositiveInfinity)]
    public async Task PixelAspectCannotBeInvalid(double aspect)
    {
        await Assert.That(() => RenderProductFraming.Resolve(
            new StageCameraApertureWindow(-1, 1, -1, 1),
            new ViewportDimensions(4, 4), aspect, "expandAperture", new Vector4(0, 0, 1, 1)))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task UnknownPolicyCannotSilentlyBecomeExpandAperture()
    {
        await Assert.That(() => RenderProductFraming.Resolve(
            new StageCameraApertureWindow(-1, 1, -1, 1),
            new ViewportDimensions(4, 4), 1, "unknownPolicy", new Vector4(0, 0, 1, 1)))
            .Throws<ArgumentException>();
    }
}
