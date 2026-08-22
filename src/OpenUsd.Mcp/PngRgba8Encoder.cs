// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.IO.Compression;

namespace OpenUsd.Mcp;

public static class PngRgba8Encoder
{
    private static ReadOnlySpan<byte> Signature =>
        [137, 80, 78, 71, 13, 10, 26, 10];

    private static ReadOnlySpan<byte> HeaderChunk => "IHDR"u8;

    private static ReadOnlySpan<byte> DataChunk => "IDAT"u8;

    private static ReadOnlySpan<byte> EndChunk => "IEND"u8;

    public static byte[] Encode(ImageRgba8 image)
    {
        ArgumentNullException.ThrowIfNull(image);

        using var output = new MemoryStream();
        output.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)image.Width));
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], checked((uint)image.Height));
        header[8] = 8;
        header[9] = 6;
        WriteChunk(output, HeaderChunk, header);

        byte[] filtered = AddNoneFilters(image);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(
            compressed,
            CompressionLevel.SmallestSize,
            leaveOpen: true))
        {
            zlib.Write(filtered);
        }

        WriteChunk(output, DataChunk, compressed.GetBuffer().AsSpan(0, checked((int)compressed.Length)));
        WriteChunk(output, EndChunk, []);
        return output.ToArray();
    }

    private static byte[] AddNoneFilters(ImageRgba8 image)
    {
        int stride = checked(image.Width * ImageRgba8.BytesPerPixel);
        byte[] filtered = new byte[checked((stride + 1) * image.Height)];
        ReadOnlySpan<byte> pixels = image.Pixels.Span;
        for (int row = 0; row < image.Height; row++)
        {
            pixels.Slice(row * stride, stride)
                .CopyTo(filtered.AsSpan((row * (stride + 1)) + 1, stride));
        }

        return filtered;
    }

    private static void WriteChunk(
        Stream output,
        ReadOnlySpan<byte> type,
        ReadOnlySpan<byte> data)
    {
        Span<byte> integer = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(integer, checked((uint)data.Length));
        output.Write(integer);
        output.Write(type);
        output.Write(data);

        uint crc = PngCrc32.Calculate(type, data);
        BinaryPrimitives.WriteUInt32BigEndian(integer, crc);
        output.Write(integer);
    }
}

internal static class PngCrc32
{
    internal static uint Calculate(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        uint crc = uint.MaxValue;
        crc = Update(crc, first);
        crc = Update(crc, second);
        return ~crc;
    }

    private static uint Update(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                uint mask = (uint)-(int)(crc & 1);
                crc = (crc >> 1) ^ (0xEDB88320U & mask);
            }
        }

        return crc;
    }
}
