// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Tests;

public sealed class ParityImageComparisonTests
{
    private const uint Background = 0x0E0E0EFFU;

    [Test]
    public async Task BottomUpReadbackConvertsToATopDownCapture()
    {
        // A shape in the top half of a top-down capture occupies the last rows of the
        // bottom-up readback Storm produces, so the conversion must move it back.
        ParityImage topDown = FilledRectangle(8, 8, 2, 1, 6, 3);
        byte[] bottomUp = ReverseRows(topDown);

        ParityImage converted = ParityImage.FromBottomUpRgba(8, 8, bottomUp);

        ParityComparisonResult result = ParityImageComparer.Compare(
            topDown,
            converted,
            Background,
            ParityTolerance.Geometry);

        await Assert.That(result.Passed).IsTrue();
        await Assert.That(result.UnforgivenCoverageDifferencePixels).IsEqualTo(0);
    }

    [Test]
    public async Task UnconvertedBottomUpReadbackLooksLikeAFlipRegression()
    {
        // Guards the conversion itself: comparing a vertically asymmetric capture against
        // its raw bottom-up bytes must fail, otherwise a missing conversion would be
        // silently indistinguishable from agreement.
        ParityImage topDown = FilledRectangle(8, 8, 2, 1, 6, 3);
        var unconverted = new ParityImage(8, 8, ReverseRows(topDown));

        ParityComparisonResult result = ParityImageComparer.Compare(
            topDown,
            unconverted,
            Background,
            ParityTolerance.Geometry);

        await Assert.That(result.Passed).IsFalse();
    }

    [Test]
    public async Task BottomUpConversionRejectsAMismatchedByteCount()
    {
        await Assert.That(() => ParityImage.FromBottomUpRgba(4, 4, new byte[4 * 4 * 4 - 1]))
            .Throws<ArgumentException>();
    }

    private static byte[] ReverseRows(ParityImage image)
    {
        int stride = image.Width * ParityImage.BytesPerPixel;
        ReadOnlySpan<byte> source = image.Rgba.Span;
        byte[] reversed = new byte[source.Length];
        for (int row = 0; row < image.Height; row++)
        {
            source.Slice(row * stride, stride)
                .CopyTo(reversed.AsSpan((image.Height - 1 - row) * stride, stride));
        }

        return reversed;
    }

    [Test]
    public async Task IdenticalCapturesAgreeExactly()
    {
        ParityImage image = FilledRectangle(16, 16, 4, 4, 12, 12);

        ParityComparisonResult result = ParityImageComparer.Compare(
            image,
            image,
            Background,
            ParityTolerance.Geometry);

        await Assert.That(result.Passed).IsTrue();
        await Assert.That(result.CoverageIntersectionOverUnion).IsEqualTo(1.0);
        await Assert.That(result.AdjustedCoverageIntersectionOverUnion).IsEqualTo(1.0);
        await Assert.That(result.UnforgivenCoverageDifferencePixels).IsEqualTo(0);
        await Assert.That(result.ReferenceCoveragePixels).IsEqualTo(64);
        await Assert.That(result.CandidateCoveragePixels).IsEqualTo(64);
    }

    [Test]
    public async Task EmptyCapturesAgreeInsteadOfDividingByZero()
    {
        ParityImage empty = FilledRectangle(8, 8, 0, 0, 0, 0);

        ParityComparisonResult result = ParityImageComparer.Compare(
            empty,
            empty,
            Background,
            ParityTolerance.Geometry);

        await Assert.That(result.CoverageUnionPixels).IsEqualTo(0);
        await Assert.That(result.CoverageIntersectionOverUnion).IsEqualTo(1.0);
        await Assert.That(result.Passed).IsTrue();
    }

    [Test]
    public async Task SinglePixelSilhouetteShiftIsForgiven()
    {
        ParityImage reference = FilledRectangle(32, 32, 8, 8, 24, 24);
        ParityImage candidate = FilledRectangle(32, 32, 9, 8, 25, 24);

        ParityComparisonResult result = ParityImageComparer.Compare(
            reference,
            candidate,
            Background,
            ParityTolerance.Geometry);

        await Assert.That(result.UnforgivenCoverageDifferencePixels).IsEqualTo(0);
        await Assert.That(result.AdjustedCoverageIntersectionOverUnion).IsEqualTo(1.0);
        await Assert.That(result.CoverageIntersectionOverUnion).IsLessThan(1.0);
        await Assert.That(result.Passed).IsTrue();
    }

    [Test]
    public async Task SinglePixelShiftIsNotForgivenWithoutDilation()
    {
        ParityImage reference = FilledRectangle(32, 32, 8, 8, 24, 24);
        ParityImage candidate = FilledRectangle(32, 32, 9, 8, 25, 24);
        ParityTolerance strict = ParityTolerance.Geometry with
        {
            EdgeDilationRadius = 0,
            MaximumCoverageDifferenceFraction = 0.0,
        };

        ParityComparisonResult result = ParityImageComparer.Compare(
            reference,
            candidate,
            Background,
            strict);

        await Assert.That(result.UnforgivenCoverageDifferencePixels).IsEqualTo(32);
        await Assert.That(result.Passed).IsFalse();
    }

    [Test]
    public async Task MissingGeometryFailsEvenWithDilation()
    {
        ParityImage reference = FilledRectangle(32, 32, 4, 4, 28, 28);
        ParityImage candidate = FilledRectangle(32, 32, 0, 0, 0, 0);

        ParityComparisonResult result = ParityImageComparer.Compare(
            reference,
            candidate,
            Background,
            ParityTolerance.Geometry);

        await Assert.That(result.CoverageIntersectionOverUnion).IsEqualTo(0.0);
        await Assert.That(result.AdjustedCoverageIntersectionOverUnion).IsEqualTo(0.0);
        await Assert.That(result.Passed).IsFalse();
        await Assert.That(result.UnforgivenCoverageDifferencePixels).IsEqualTo(576);
    }

    [Test]
    public async Task ColourDifferencesAreIgnoredUntilColourComparisonIsEnabled()
    {
        ParityImage reference = FilledRectangle(16, 16, 4, 4, 12, 12, 0xFF0000FFU);
        ParityImage candidate = FilledRectangle(16, 16, 4, 4, 12, 12, 0x00FF00FFU);

        ParityComparisonResult ignored = ParityImageComparer.Compare(
            reference,
            candidate,
            Background,
            ParityTolerance.Geometry);
        ParityComparisonResult compared = ParityImageComparer.Compare(
            reference,
            candidate,
            Background,
            ParityTolerance.Geometry with
            {
                CompareColor = true,
                MaximumChannelDifference = 8,
                MaximumMeanChannelDifference = 8,
            });

        await Assert.That(ignored.Passed).IsTrue();
        await Assert.That(ignored.MaximumChannelDifference).IsEqualTo(0);
        await Assert.That(compared.Passed).IsFalse();
        await Assert.That(compared.MaximumChannelDifference).IsEqualTo(255);
    }

    [Test]
    public async Task DiffImageMarksReferenceOnlyBlueAndCandidateOnlyRed()
    {
        ParityImage reference = FilledRectangle(4, 1, 0, 0, 2, 1);
        ParityImage candidate = FilledRectangle(4, 1, 2, 0, 4, 1);

        ParityComparisonResult result = ParityImageComparer.Compare(
            reference,
            candidate,
            Background,
            ParityTolerance.Geometry);

        ReadOnlyMemory<byte> diff = result.DiffRgba;
        await Assert.That(diff.Length).IsEqualTo(16);
        await Assert.That(diff.Span[2]).IsEqualTo((byte)255);
        await Assert.That(diff.Span[0]).IsEqualTo((byte)0);
        await Assert.That(diff.Span[8]).IsEqualTo((byte)255);
        await Assert.That(diff.Span[10]).IsEqualTo((byte)0);
    }

    [Test]
    public async Task MismatchedDimensionsAreRejected()
    {
        ParityImage reference = FilledRectangle(8, 8, 0, 0, 4, 4);
        ParityImage candidate = FilledRectangle(8, 4, 0, 0, 4, 4);

        await Assert.That(() => ParityImageComparer.Compare(
                reference,
                candidate,
                Background,
                ParityTolerance.Geometry))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task TruncatedCaptureIsRejected()
    {
        var truncated = new ParityImage(4, 4, new byte[16]);

        await Assert.That(() => truncated.Validate("candidate"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ImpossibleToleranceIsRejected()
    {
        ParityTolerance tolerance = ParityTolerance.Geometry with
        {
            MinimumCoverageIntersectionOverUnion = 1.5,
        };

        await Assert.That(() => tolerance.Validate()).Throws<InvalidOperationException>();
    }

    private static ParityImage FilledRectangle(
        int width,
        int height,
        int left,
        int top,
        int right,
        int bottom,
        uint fillRgba = 0xC0C0C0FFU)
    {
        byte[] pixels = new byte[width * height * ParityImage.BytesPerPixel];
        for (int index = 0; index < width * height; index++)
        {
            WritePixel(pixels, index * ParityImage.BytesPerPixel, Background);
        }

        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                WritePixel(pixels, ((y * width) + x) * ParityImage.BytesPerPixel, fillRgba);
            }
        }

        return new ParityImage(width, height, pixels);
    }

    private static void WritePixel(byte[] pixels, int offset, uint rgba)
    {
        pixels[offset] = (byte)((rgba >> 24) & 0xFF);
        pixels[offset + 1] = (byte)((rgba >> 16) & 0xFF);
        pixels[offset + 2] = (byte)((rgba >> 8) & 0xFF);
        pixels[offset + 3] = (byte)(rgba & 0xFF);
    }
}
