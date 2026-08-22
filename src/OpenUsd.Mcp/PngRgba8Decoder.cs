// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.IO.Compression;

namespace OpenUsd.Mcp;

public static class PngRgba8Decoder
{
    private static ReadOnlySpan<byte> Signature =>
        [137, 80, 78, 71, 13, 10, 26, 10];

    public static ImageRgba8 Decode(ReadOnlySpan<byte> png)
    {
        if (png.Length < Signature.Length || !png[..Signature.Length].SequenceEqual(Signature))
        {
            throw new InvalidDataException("The PNG signature is invalid.");
        }

        int offset = Signature.Length;
        int width = 0;
        int height = 0;
        bool sawHeader = false;
        bool sawEnd = false;
        using var compressed = new MemoryStream();
        while (offset < png.Length)
        {
            if (png.Length - offset < 12)
            {
                throw new InvalidDataException("The PNG chunk is truncated.");
            }

            uint lengthValue = BinaryPrimitives.ReadUInt32BigEndian(png[offset..]);
            if (lengthValue > int.MaxValue)
            {
                throw new InvalidDataException("The PNG chunk is too large.");
            }

            int length = checked((int)lengthValue);
            int chunkLength = checked(length + 12);
            if (chunkLength > png.Length - offset)
            {
                throw new InvalidDataException("The PNG chunk data is truncated.");
            }

            ReadOnlySpan<byte> type = png.Slice(offset + 4, 4);
            ReadOnlySpan<byte> data = png.Slice(offset + 8, length);
            uint expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(
                png.Slice(offset + 8 + length, 4));
            if (PngCrc32.Calculate(type, data) != expectedCrc)
            {
                throw new InvalidDataException("The PNG chunk CRC is invalid.");
            }

            if (type.SequenceEqual("IHDR"u8))
            {
                if (sawHeader || offset != Signature.Length || length != 13)
                {
                    throw new InvalidDataException("The PNG header is invalid.");
                }

                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]));
                if (width <= 0 ||
                    height <= 0 ||
                    data[8] != 8 ||
                    data[9] != 6 ||
                    data[10] != 0 ||
                    data[11] != 0 ||
                    data[12] != 0)
                {
                    throw new InvalidDataException(
                        "Only non-interlaced, 8-bit RGBA PNG images are supported.");
                }

                _ = ImageRgba8.GetByteCount(width, height);
                sawHeader = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (!sawHeader || sawEnd)
                {
                    throw new InvalidDataException("The PNG data chunks are out of order.");
                }

                compressed.Write(data);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (!sawHeader || length != 0)
                {
                    throw new InvalidDataException("The PNG end chunk is invalid.");
                }

                sawEnd = true;
                offset += chunkLength;
                break;
            }
            else if ((type[0] & 0x20) == 0)
            {
                throw new InvalidDataException("The PNG contains an unsupported critical chunk.");
            }

            offset += chunkLength;
        }

        if (!sawHeader || !sawEnd || offset != png.Length)
        {
            throw new InvalidDataException("The PNG stream is incomplete.");
        }

        int stride = checked(width * ImageRgba8.BytesPerPixel);
        int filteredLength = checked((stride + 1) * height);
        byte[] filtered = new byte[filteredLength];
        compressed.Position = 0;
        using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress))
        {
            zlib.ReadExactly(filtered);
            if (zlib.ReadByte() != -1)
            {
                throw new InvalidDataException("The PNG contains excess decompressed image data.");
            }
        }

        byte[] pixels = new byte[ImageRgba8.GetByteCount(width, height)];
        for (int row = 0; row < height; row++)
        {
            int filteredOffset = row * (stride + 1);
            if (filtered[filteredOffset] != 0)
            {
                throw new InvalidDataException("Only PNG filter type None is supported.");
            }

            filtered.AsSpan(filteredOffset + 1, stride)
                .CopyTo(pixels.AsSpan(row * stride, stride));
        }

        return new ImageRgba8(width, height, pixels);
    }
}
