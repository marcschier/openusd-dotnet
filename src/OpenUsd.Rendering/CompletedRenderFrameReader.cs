// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography;

namespace OpenUsd.Rendering;

internal static class CompletedRenderFrameReader
{
    internal static PngRgba8Image ReadThumbnail(
        Stream input, RenderDiskFrameResult frame, int width, int height, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, 1024);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, 1024);
        cancellationToken.ThrowIfCancellationRequested();
        if (!input.CanRead || !input.CanSeek)
        {
            throw new ArgumentException(
                "A completed PNG requires a readable stream with an exact extent.", nameof(input));
        }
        int sourceWidth = frame.State.Viewport.Width;
        int sourceHeight = frame.State.Viewport.Height;
        if (sourceWidth is < 1 or > 8192 || sourceHeight is < 1 or > 8192 ||
            (long)sourceWidth * sourceHeight * 4 > RenderDiskJobLimits.Default.MaximumFrameBytes)
        {
            throw new InvalidDataException("The completed PNG dimensions exceed the recorded frame limit.");
        }
        long length = input.Length;
        if (length != frame.Bytes || length < 1 || length > RenderDiskJobLimits.Default.MaximumFrameBytes)
        {
            throw new InvalidDataException("The completed PNG extent has changed or exceeds its frame limit.");
        }
        input.Position = 0;
        byte[] bytes = new byte[checked((int)length)];
        for (int offset = 0; offset < bytes.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(64 * 1024, bytes.Length - offset);
            input.ReadExactly(bytes.AsSpan(offset, count));
            offset += count;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (input.ReadByte() != -1 || input.Length != frame.Bytes ||
            !Convert.ToHexString(SHA256.HashData(bytes)).Equals(frame.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The completed PNG no longer matches its recorded SHA256.");
        }
        PngRgba8Header header;
        try
        {
            header = PngRgba8Reader.ReadHeader(bytes);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException($"The completed PNG cannot be decoded: {exception.Message}", exception);
        }
        if (header.Width != sourceWidth || header.Height != sourceHeight)
        {
            throw new InvalidDataException("The completed PNG dimensions do not match the recorded frame.");
        }
        PngRgba8Image image;
        try
        {
            image = PngRgba8Reader.Decode(bytes, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException($"The completed PNG cannot be decoded: {exception.Message}", exception);
        }
        double scale = Math.Min((double)width / image.Width, (double)height / image.Height);
        int fittedWidth = Math.Clamp((int)Math.Round(image.Width * scale), 1, width);
        int fittedHeight = Math.Clamp((int)Math.Round(image.Height * scale), 1, height);
        int left = (width - fittedWidth) / 2;
        int top = (height - fittedHeight) / 2;
        var pixels = new byte[checked(width * height * 4)];
        for (int y = 0; y < fittedHeight; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int sourceY = y * image.Height / fittedHeight;
            for (int x = 0; x < fittedWidth; x++)
            {
                int sourceX = x * image.Width / fittedWidth;
                image.Pixels.AsSpan((sourceY * image.Width + sourceX) * 4, 4)
                    .CopyTo(pixels.AsSpan(((y + top) * width + x + left) * 4, 4));
            }
        }
        return new PngRgba8Image(width, height, pixels);
    }
}
