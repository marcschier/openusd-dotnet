// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering;

/// <summary>Explicit EXR data/display windows in top-down pixel coordinates plus the stored pixel aspect.</summary>
internal readonly record struct ExrImageWindows(
    int DataOriginX,
    int DataOriginY,
    int DisplayOriginX,
    int DisplayOriginY,
    int DisplayWidth,
    int DisplayHeight,
    double PixelAspectRatio)
{
    private const long MaximumCoordinate = 1_073_741_823;
    private const double MaximumAspectScale = 65536;

    /// <summary>Maps a bottom-left product data window into EXR's downward-Y data/display windows.</summary>
    internal static ExrImageWindows FromProductFrame(
        ViewportDimensions full, int dataMinX, int dataMinYFromBottom, int dataHeight, double pixelAspectRatio) =>
        new(dataMinX, checked(full.Height - dataMinYFromBottom - dataHeight), 0, 0,
            full.Width, full.Height, pixelAspectRatio);

    /// <summary>Validates the windows and returns the pixel aspect as a bounded rational.</summary>
    internal (uint Numerator, uint Denominator) Validate(int width, int height)
    {
        if (!Corner(DataOriginX, width) || !Corner(DataOriginY, height) ||
            !Corner(DisplayOriginX, DisplayWidth) || !Corner(DisplayOriginY, DisplayHeight))
        {
            throw new NotSupportedException("EXR windows exceed the encoder's coordinate range.");
        }
        if (!double.IsFinite(PixelAspectRatio) || PixelAspectRatio < 1 / MaximumAspectScale ||
            PixelAspectRatio > MaximumAspectScale)
        {
            throw new NotSupportedException("EXR pixel aspect is outside 1/65536..65536.");
        }
        (uint numerator, uint denominator) = Rational(PixelAspectRatio);
        if ((float)((double)numerator / denominator) != (float)PixelAspectRatio)
        {
            throw new NotSupportedException("EXR pixel aspect has no exact single-precision rational form.");
        }
        return (numerator, denominator);
    }

    private static bool Corner(long origin, long extent) =>
        origin >= -MaximumCoordinate && origin <= MaximumCoordinate && extent >= 1 &&
        extent <= MaximumCoordinate && origin + extent - 1 <= MaximumCoordinate;

    // Best continued-fraction convergent with both terms in UInt32; EXR stores the ratio as float.
    private static (uint Numerator, uint Denominator) Rational(double value)
    {
        ulong previousNumerator = 0, numerator = 1, previousDenominator = 1, denominator = 0;
        double remainder = value;
        for (int step = 0; step < 64; step++)
        {
            double whole = Math.Floor(remainder);
            if (whole > uint.MaxValue)
            {
                break;
            }
            ulong term = (ulong)whole;
            ulong nextNumerator = term * numerator + previousNumerator;
            ulong nextDenominator = term * denominator + previousDenominator;
            if (nextNumerator > uint.MaxValue || nextDenominator > uint.MaxValue)
            {
                break;
            }
            (previousNumerator, numerator) = (numerator, nextNumerator);
            (previousDenominator, denominator) = (denominator, nextDenominator);
            double fraction = remainder - whole;
            if ((double)numerator / denominator == value || fraction == 0)
            {
                break;
            }
            remainder = 1 / fraction;
        }
        return ((uint)numerator, (uint)denominator);
    }
}
