// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering;

/// <summary>
/// A renderer-neutral RGBA8 capture used by the Storm-to-hdSilk parity harness.
/// Rows are top-down and tightly packed with four bytes per pixel.
/// </summary>
internal readonly record struct ParityImage(int Width, int Height, ReadOnlyMemory<byte> Rgba)
{
    internal const int BytesPerPixel = 4;

    internal long ExpectedByteCount => (long)Width * Height * BytesPerPixel;

    /// <summary>
    /// Creates a top-down capture from a bottom-up RGBA readback.
    /// </summary>
    /// <remarks>
    /// OpenGL puts the framebuffer origin at the bottom-left, so Storm's <c>glReadPixels</c>
    /// evidence arrives last row first, while every hdSilk backend reads back top-down.
    /// Comparing the two without this conversion reports an exact vertical mirror, which is
    /// indistinguishable from a genuine flip regression in the renderer under test.
    /// </remarks>
    internal static ParityImage FromBottomUpRgba(
        int width,
        int height,
        ReadOnlySpan<byte> bottomUpRgba)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException(
                $"A capture must have a positive width and height; got {width}x{height}.",
                nameof(width));
        }

        long expected = (long)width * height * BytesPerPixel;
        if (bottomUpRgba.Length != expected)
        {
            throw new ArgumentException(
                $"A bottom-up capture must contain exactly {expected} bytes for " +
                $"{width}x{height}; got {bottomUpRgba.Length}.",
                nameof(bottomUpRgba));
        }

        int stride = width * BytesPerPixel;
        byte[] topDown = new byte[bottomUpRgba.Length];
        for (int row = 0; row < height; row++)
        {
            bottomUpRgba.Slice(row * stride, stride)
                .CopyTo(topDown.AsSpan((height - 1 - row) * stride, stride));
        }

        return new ParityImage(width, height, topDown);
    }

    internal void Validate(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (Width <= 0 || Height <= 0)
        {
            throw new ArgumentException(
                $"{name} must have a positive width and height; got {Width}x{Height}.",
                name);
        }

        if (Rgba.Length != ExpectedByteCount)
        {
            throw new ArgumentException(
                $"{name} must contain exactly {ExpectedByteCount} bytes for {Width}x{Height}; " +
                $"got {Rgba.Length}.",
                name);
        }
    }
}

/// <summary>
/// The accepted difference between a Storm reference capture and an hdSilk candidate capture.
/// Colour comparison stays disabled until hdSilk implements materials and lighting, because
/// its surface shading is still an absolute-normal debug visualization.
/// </summary>
internal readonly record struct ParityTolerance
{
    /// <summary>Smallest accepted Jaccard index between the two coverage masks.</summary>
    internal double MinimumCoverageIntersectionOverUnion { get; init; }

    /// <summary>Largest accepted fraction of pixels whose coverage disagrees after dilation.</summary>
    internal double MaximumCoverageDifferenceFraction { get; init; }

    /// <summary>Per-channel slack when deciding whether a pixel is background.</summary>
    internal byte BackgroundChannelTolerance { get; init; }

    /// <summary>
    /// Chebyshev radius used to forgive silhouette disagreement. Rasterization and
    /// anti-aliasing legitimately differ by a pixel between backends and drivers.
    /// </summary>
    internal int EdgeDilationRadius { get; init; }

    /// <summary>Whether colour is compared over the agreed coverage.</summary>
    internal bool CompareColor { get; init; }

    /// <summary>Largest accepted single-channel difference when colour is compared.</summary>
    internal byte MaximumChannelDifference { get; init; }

    /// <summary>Largest accepted mean channel difference when colour is compared.</summary>
    internal double MaximumMeanChannelDifference { get; init; }

    /// <summary>
    /// Geometry-only tolerance for the current hdSilk feature set: silhouette and coverage
    /// must agree, colour is not compared.
    /// </summary>
    internal static ParityTolerance Geometry => new()
    {
        MinimumCoverageIntersectionOverUnion = 0.98,
        MaximumCoverageDifferenceFraction = 0.005,
        BackgroundChannelTolerance = 2,
        EdgeDilationRadius = 1,
        CompareColor = false,
        MaximumChannelDifference = byte.MaxValue,
        MaximumMeanChannelDifference = byte.MaxValue,
    };

    internal void Validate()
    {
        if (!double.IsFinite(MinimumCoverageIntersectionOverUnion) ||
            MinimumCoverageIntersectionOverUnion < 0 ||
            MinimumCoverageIntersectionOverUnion > 1)
        {
            throw new InvalidOperationException(
                "MinimumCoverageIntersectionOverUnion must be a finite value between 0 and 1.");
        }

        if (!double.IsFinite(MaximumCoverageDifferenceFraction) ||
            MaximumCoverageDifferenceFraction < 0 ||
            MaximumCoverageDifferenceFraction > 1)
        {
            throw new InvalidOperationException(
                "MaximumCoverageDifferenceFraction must be a finite value between 0 and 1.");
        }

        if (EdgeDilationRadius < 0 || EdgeDilationRadius > 8)
        {
            throw new InvalidOperationException("EdgeDilationRadius must be between 0 and 8.");
        }

        if (CompareColor &&
            (!double.IsFinite(MaximumMeanChannelDifference) || MaximumMeanChannelDifference < 0))
        {
            throw new InvalidOperationException(
                "MaximumMeanChannelDifference must be a finite non-negative value.");
        }
    }
}

/// <summary>Measured difference between a reference capture and a candidate capture.</summary>
internal sealed record ParityComparisonResult
{
    internal required int Width { get; init; }

    internal required int Height { get; init; }

    internal required int ReferenceCoveragePixels { get; init; }

    internal required int CandidateCoveragePixels { get; init; }

    internal required int CoverageIntersectionPixels { get; init; }

    internal required int CoverageUnionPixels { get; init; }

    /// <summary>Coverage disagreements that dilation did not forgive.</summary>
    internal required int UnforgivenCoverageDifferencePixels { get; init; }

    internal required double CoverageIntersectionOverUnion { get; init; }

    /// <summary>
    /// Intersection over union after dilation forgiveness, so a silhouette that merely
    /// shifted within the accepted radius counts as agreement. This is the gated value.
    /// </summary>
    internal required double AdjustedCoverageIntersectionOverUnion { get; init; }

    internal required double CoverageDifferenceFraction { get; init; }

    /// <summary>Largest single-channel difference over the agreed coverage, or 0 when empty.</summary>
    internal required int MaximumChannelDifference { get; init; }

    internal required double MeanChannelDifference { get; init; }

    internal required bool Passed { get; init; }

    internal required string Diagnostics { get; init; }

    /// <summary>
    /// RGBA diff image: reference-only coverage is blue, candidate-only coverage is red,
    /// agreed coverage is grey, and agreed background is black.
    /// </summary>
    internal required ReadOnlyMemory<byte> DiffRgba { get; init; }
}

/// <summary>
/// Compares a Storm reference capture with an hdSilk candidate capture. The comparison is
/// deliberately geometry-first: until hdSilk implements materials and lighting, only coverage
/// and silhouette carry meaning, and colour comparison must be opted into explicitly.
/// </summary>
internal static class ParityImageComparer
{
    internal static ParityComparisonResult Compare(
        ParityImage reference,
        ParityImage candidate,
        uint backgroundRgba,
        ParityTolerance tolerance)
    {
        reference.Validate(nameof(reference));
        candidate.Validate(nameof(candidate));
        tolerance.Validate();
        if (reference.Width != candidate.Width || reference.Height != candidate.Height)
        {
            throw new ArgumentException(
                $"Reference {reference.Width}x{reference.Height} and candidate " +
                $"{candidate.Width}x{candidate.Height} captures must have identical dimensions.",
                nameof(candidate));
        }

        int width = reference.Width;
        int height = reference.Height;
        int pixelCount = width * height;
        ReadOnlySpan<byte> referencePixels = reference.Rgba.Span;
        ReadOnlySpan<byte> candidatePixels = candidate.Rgba.Span;

        bool[] referenceMask = new bool[pixelCount];
        bool[] candidateMask = new bool[pixelCount];
        BuildCoverageMask(referencePixels, backgroundRgba, tolerance.BackgroundChannelTolerance, referenceMask);
        BuildCoverageMask(candidatePixels, backgroundRgba, tolerance.BackgroundChannelTolerance, candidateMask);

        int referenceCoverage = 0;
        int candidateCoverage = 0;
        int intersection = 0;
        int union = 0;
        int unforgiven = 0;
        int forgivenCount = 0;
        long channelDifferenceSum = 0;
        int maximumChannelDifference = 0;
        byte[] diff = new byte[pixelCount * ParityImage.BytesPerPixel];

        for (int index = 0; index < pixelCount; index++)
        {
            bool inReference = referenceMask[index];
            bool inCandidate = candidateMask[index];
            if (inReference)
            {
                referenceCoverage++;
            }

            if (inCandidate)
            {
                candidateCoverage++;
            }

            if (inReference || inCandidate)
            {
                union++;
            }

            int diffOffset = index * ParityImage.BytesPerPixel;
            diff[diffOffset + 3] = byte.MaxValue;
            if (inReference && inCandidate)
            {
                intersection++;
                diff[diffOffset] = 0x60;
                diff[diffOffset + 1] = 0x60;
                diff[diffOffset + 2] = 0x60;
                if (tolerance.CompareColor)
                {
                    int difference = MaximumChannelDelta(referencePixels, candidatePixels, diffOffset);
                    channelDifferenceSum += difference;
                    if (difference > maximumChannelDifference)
                    {
                        maximumChannelDifference = difference;
                    }
                }

                continue;
            }

            if (!inReference && !inCandidate)
            {
                continue;
            }

            int x = index % width;
            int y = index / width;
            bool forgiven = IsForgiven(
                inCandidate ? referenceMask : candidateMask,
                width,
                height,
                x,
                y,
                tolerance.EdgeDilationRadius);
            if (!forgiven)
            {
                unforgiven++;
            }
            else
            {
                forgivenCount++;
            }

            if (inCandidate)
            {
                diff[diffOffset] = byte.MaxValue;
            }
            else
            {
                diff[diffOffset + 2] = byte.MaxValue;
            }
        }

        double intersectionOverUnion = union == 0 ? 1.0 : (double)intersection / union;
        double adjustedIntersectionOverUnion = union == 0
            ? 1.0
            : (double)(intersection + forgivenCount) / union;
        double differenceFraction = pixelCount == 0 ? 0.0 : (double)unforgiven / pixelCount;
        double meanChannelDifference = intersection == 0
            ? 0.0
            : (double)channelDifferenceSum / intersection;

        bool passed =
            adjustedIntersectionOverUnion >= tolerance.MinimumCoverageIntersectionOverUnion &&
            differenceFraction <= tolerance.MaximumCoverageDifferenceFraction;
        if (passed && tolerance.CompareColor)
        {
            passed =
                maximumChannelDifference <= tolerance.MaximumChannelDifference &&
                meanChannelDifference <= tolerance.MaximumMeanChannelDifference;
        }

        string diagnostics =
            FormattableString.Invariant($"{width}x{height}; ") +
            FormattableString.Invariant($"referenceCoverage={referenceCoverage}; ") +
            FormattableString.Invariant($"candidateCoverage={candidateCoverage}; ") +
            FormattableString.Invariant($"iou={intersectionOverUnion:F6}; ") +
            FormattableString.Invariant($"adjustedIou={adjustedIntersectionOverUnion:F6}; ") +
            FormattableString.Invariant($"unforgiven={unforgiven}; ") +
            FormattableString.Invariant($"differenceFraction={differenceFraction:F6}; ") +
            FormattableString.Invariant($"maxChannelDelta={maximumChannelDifference}; ") +
            FormattableString.Invariant($"meanChannelDelta={meanChannelDifference:F6}; ") +
            FormattableString.Invariant($"compareColor={tolerance.CompareColor}");

        return new ParityComparisonResult
        {
            Width = width,
            Height = height,
            ReferenceCoveragePixels = referenceCoverage,
            CandidateCoveragePixels = candidateCoverage,
            CoverageIntersectionPixels = intersection,
            CoverageUnionPixels = union,
            UnforgivenCoverageDifferencePixels = unforgiven,
            CoverageIntersectionOverUnion = intersectionOverUnion,
            AdjustedCoverageIntersectionOverUnion = adjustedIntersectionOverUnion,
            CoverageDifferenceFraction = differenceFraction,
            MaximumChannelDifference = maximumChannelDifference,
            MeanChannelDifference = meanChannelDifference,
            Passed = passed,
            DiffRgba = diff,
            Diagnostics = diagnostics,
        };
    }

    private static void BuildCoverageMask(
        ReadOnlySpan<byte> pixels,
        uint backgroundRgba,
        byte channelTolerance,
        Span<bool> mask)
    {
        byte backgroundRed = (byte)((backgroundRgba >> 24) & 0xFF);
        byte backgroundGreen = (byte)((backgroundRgba >> 16) & 0xFF);
        byte backgroundBlue = (byte)((backgroundRgba >> 8) & 0xFF);
        for (int index = 0; index < mask.Length; index++)
        {
            int offset = index * ParityImage.BytesPerPixel;
            mask[index] =
                Math.Abs(pixels[offset] - backgroundRed) > channelTolerance ||
                Math.Abs(pixels[offset + 1] - backgroundGreen) > channelTolerance ||
                Math.Abs(pixels[offset + 2] - backgroundBlue) > channelTolerance;
        }
    }

    private static int MaximumChannelDelta(
        ReadOnlySpan<byte> reference,
        ReadOnlySpan<byte> candidate,
        int offset)
    {
        int red = Math.Abs(reference[offset] - candidate[offset]);
        int green = Math.Abs(reference[offset + 1] - candidate[offset + 1]);
        int blue = Math.Abs(reference[offset + 2] - candidate[offset + 2]);
        return Math.Max(red, Math.Max(green, blue));
    }

    /// <summary>
    /// A coverage disagreement is forgiven when the other capture has coverage within
    /// <paramref name="radius"/>, which means the silhouette merely shifted rather than
    /// geometry appearing or disappearing.
    /// </summary>
    private static bool IsForgiven(
        ReadOnlySpan<bool> otherMask,
        int width,
        int height,
        int x,
        int y,
        int radius)
    {
        if (radius <= 0)
        {
            return false;
        }

        int minimumY = Math.Max(0, y - radius);
        int maximumY = Math.Min(height - 1, y + radius);
        int minimumX = Math.Max(0, x - radius);
        int maximumX = Math.Min(width - 1, x + radius);
        for (int row = minimumY; row <= maximumY; row++)
        {
            int rowOffset = row * width;
            for (int column = minimumX; column <= maximumX; column++)
            {
                if (otherMask[rowOffset + column])
                {
                    return true;
                }
            }
        }

        return false;
    }
}
