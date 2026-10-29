// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;

namespace OpenUsd.Rendering;

public static partial class RenderDiskJob
{
    /// <summary>
    /// Renders every shutter sample and box-filters them into one frame image with equal weights.
    /// </summary>
    /// <remarks>
    /// HDR color is averaged in float from binary16 samples. The PNG companion averages the display-encoded
    /// RGBA8 samples with round-half-up, because the managed path cannot re-run the renderer's display transform.
    /// Each renderer-owned sample is consumed before the next render.
    /// </remarks>
    private static RenderJobImage AccumulateShutter(
        StageRenderState frameState, IReadOnlyList<StageRenderState> samples, IRenderJobFrameSource source,
        bool includeHdrColor, CancellationToken cancellationToken)
    {
        int width = frameState.Viewport.Width;
        int height = frameState.Viewport.Height;
        if (samples.Count is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(samples), "A shutter accumulates 1-256 samples.");
        }
        int pixels = checked(width * height);
        var display = new ushort[checked(pixels * 4)];
        float[]? hdr = includeHdrColor ? new float[checked(pixels * 4)] : null;
        var diagnostics = new List<RenderDiagnostic>();
        foreach (StageRenderState sample in samples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RenderJobImage image = source.Render(sample, cancellationToken) ??
                throw new InvalidOperationException("The render source returned no image.");
            if (image.Width != width || image.Height != height)
            {
                throw new InvalidDataException("A shutter sample does not match the requested dimensions.");
            }
            if (image.Simulation is not null)
            {
                throw new NotSupportedException(
                    "Simulation publications cannot be accumulated across a camera shutter interval.");
            }
            if (image.DeviceDepth is not null || image.PrimId is not null || image.InstanceId is not null ||
                image.Normal is not null || image.ElementId is not null ||
                includeHdrColor != (image.HdrColor is not null))
            {
                throw new NotSupportedException(
                    "The render source did not provide exactly the requested shutter-sample planes.");
            }
            ArgumentNullException.ThrowIfNull(image.Diagnostics);
            foreach (RenderDiagnostic diagnostic in image.Diagnostics.Entries)
            {
                if (!diagnostics.Contains(diagnostic))
                {
                    if (diagnostics.Count == 128)
                    {
                        throw new RenderOutputQuotaExceededException(
                            "A shutter-accumulated frame exceeds the diagnostic count limit.");
                    }
                    diagnostics.Add(diagnostic);
                }
            }
            AddDisplay(display, image, width, height);
            if (hdr is not null)
            {
                RenderJobHdrColor color = image.HdrColor!;
                if (color.Width != width || color.Height != height)
                {
                    throw new InvalidDataException("A shutter-sample HDR plane does not match the display frame.");
                }
                AddHdr(hdr, color.Rgba16Float.Span);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        int count = samples.Count;
        byte[] rgba = GC.AllocateUninitializedArray<byte>(display.Length);
        for (int index = 0; index < rgba.Length; index++)
        {
            rgba[index] = (byte)(((2 * display[index]) + count) / (2 * count));
        }
        RenderJobHdrColor? averaged = null;
        if (hdr is not null)
        {
            byte[] halves = GC.AllocateUninitializedArray<byte>(hdr.Length * sizeof(ushort));
            for (int index = 0; index < hdr.Length; index++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    halves.AsSpan(index * sizeof(ushort)), BitConverter.HalfToUInt16Bits((Half)(hdr[index] / count)));
            }
            averaged = new RenderJobHdrColor(width, height, halves);
        }
        return new RenderJobImage(width, height, rgba, Rgba8RowOrder.TopDown)
        {
            Diagnostics = diagnostics.Count == 0
                ? RenderDiagnosticsState.Empty
                : new RenderDiagnosticsState(diagnostics),
            HdrColor = averaged,
        };
    }

    private static void AddDisplay(ushort[] sums, RenderJobImage image, int width, int height)
    {
        ReadOnlySpan<byte> rgba = image.Rgba.Span;
        int stride = width * 4;
        for (int row = 0; row < height; row++)
        {
            int sourceRow = image.RowOrder == Rgba8RowOrder.TopDown ? row : height - 1 - row;
            ReadOnlySpan<byte> source = rgba.Slice(sourceRow * stride, stride);
            Span<ushort> target = sums.AsSpan(row * stride, stride);
            for (int index = 0; index < stride; index++)
            {
                target[index] += source[index];
            }
        }
    }

    private static void AddHdr(float[] sums, ReadOnlySpan<byte> rgba16Float)
    {
        for (int index = 0; index < sums.Length; index++)
        {
            ushort bits = BinaryPrimitives.ReadUInt16LittleEndian(rgba16Float[(index * sizeof(ushort))..]);
            if ((bits & 0x7C00) == 0x7C00)
            {
                throw new InvalidDataException("All stored HDR color channels must be finite.");
            }
            sums[index] += (float)BitConverter.UInt16BitsToHalf(bits);
        }
    }
}
