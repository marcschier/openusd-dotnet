// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.ConformanceTests;

/// <summary>
/// The per-channel difference between two volume captures.
/// </summary>
/// <remarks>
/// Shared by every volume gate rather than duplicated per class, because the numbers it
/// produces are the evidence those gates upload and the docs quote. Two comparers that
/// drifted apart would still each look self-consistent while making their recorded deltas
/// incomparable, which is exactly the kind of quiet divergence the volume gates exist to
/// catch in the renderer.
///
/// Alpha is excluded deliberately: the volume proxy composites over an opaque clear, so
/// the alpha channel carries no density information and including it would dilute the
/// mean that the thresholds are tuned against.
/// </remarks>
internal readonly record struct ImageDelta(int MaximumChannelDelta, double MeanChannelDelta)
{
    internal static ImageDelta Compare(ParityImage reference, ParityImage candidate)
    {
        reference.Validate(nameof(reference));
        candidate.Validate(nameof(candidate));
        if (reference.Width != candidate.Width || reference.Height != candidate.Height)
        {
            throw new ArgumentException("Images must have matching dimensions.", nameof(candidate));
        }

        ReadOnlySpan<byte> referencePixels = reference.Rgba.Span;
        ReadOnlySpan<byte> candidatePixels = candidate.Rgba.Span;
        int maximum = 0;
        long sum = 0;
        int count = 0;
        for (int offset = 0; offset < referencePixels.Length; offset += ParityImage.BytesPerPixel)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                int delta = Math.Abs(referencePixels[offset + channel] - candidatePixels[offset + channel]);
                maximum = Math.Max(maximum, delta);
                sum += delta;
                count++;
            }
        }

        return new ImageDelta(maximum, count == 0 ? 0 : (double)sum / count);
    }
}
