// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.IO.Compression;

namespace OpenUsd.Rendering;

internal readonly record struct PngRgba8Header(int Width, int Height);

internal sealed record PngRgba8Image(int Width, int Height, byte[] Pixels);

internal static class PngRgba8Reader
{
    internal const int MaximumEncodedByteCount = 512 * 1024 * 1024;
    internal static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    internal static PngRgba8Header ReadHeader(ReadOnlySpan<byte> png)
    {
        if (png.Length < 33 || !png[..8].SequenceEqual(Signature) ||
            BinaryPrimitives.ReadUInt32BigEndian(png[8..]) != 13 ||
            !png.Slice(12, 4).SequenceEqual("IHDR"u8) ||
            PngCrc32.Calculate(png.Slice(12, 4), png.Slice(16, 13)) !=
                BinaryPrimitives.ReadUInt32BigEndian(png[29..]))
        {
            throw new InvalidDataException("The PNG header or its CRC is invalid.");
        }
        uint width = BinaryPrimitives.ReadUInt32BigEndian(png[16..]);
        uint height = BinaryPrimitives.ReadUInt32BigEndian(png[20..]);
        if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue ||
            (ulong)width * height > PngRgba8Writer.MaximumPixelByteCount / 4)
        {
            throw new InvalidDataException("PNG dimensions exceed the RGBA pixel budget.");
        }
        if (png[24] != 8 || png[25] != 6 || png[26] != 0 || png[27] != 0 || png[28] != 0)
        {
            throw new InvalidDataException("Use a non-interlaced, 8-bit RGBA PNG capture.");
        }
        return new PngRgba8Header((int)width, (int)height);
    }

    internal static PngRgba8Image Decode(
        ReadOnlyMemory<byte> png, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (png.Length > MaximumEncodedByteCount)
        {
            throw new InvalidDataException("The encoded PNG exceeds its byte budget.");
        }
        PngRgba8Header header = ReadHeader(png.Span);
        (int firstData, uint expectedAdler) = ValidateChunks(png.Span, cancellationToken);
        byte[] pixels = new byte[PngRgba8Writer.GetPixelByteCount(header.Width, header.Height)];
        int stride = checked(header.Width * 4);
        using var data = new ImageDataStream(png, firstData, cancellationToken);
        using var zlib = new ZLibStream(data, CompressionMode.Decompress);
        uint adler = 1;
        Span<byte> filterByte = stackalloc byte[1];
        for (int row = 0; row < header.Height; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int filter = zlib.ReadByte();
            if (filter is < 0 or > 4)
            {
                throw new InvalidDataException("The PNG scanline filter is invalid or missing.");
            }
            Span<byte> current = pixels.AsSpan(row * stride, stride);
            zlib.ReadExactly(current);
            // ZLibStream can return EOF without a trailer; independently check the filtered-byte checksum.
            filterByte[0] = (byte)filter;
            adler = UpdateAdler(adler, filterByte, cancellationToken);
            adler = UpdateAdler(adler, current, cancellationToken);
            ReadOnlySpan<byte> previous = row == 0 ? [] : pixels.AsSpan((row - 1) * stride, stride);
            Unfilter(current, previous, filter, cancellationToken);
        }
        if (zlib.ReadByte() != -1)
        {
            throw new InvalidDataException("The PNG contains excess decompressed image data.");
        }
        if (adler != expectedAdler)
        {
            throw new InvalidDataException("The PNG zlib trailer is missing or its checksum is invalid.");
        }
        return new PngRgba8Image(header.Width, header.Height, pixels);
    }

    private static uint UpdateAdler(uint adler, ReadOnlySpan<byte> bytes, CancellationToken cancellationToken)
    {
        uint first = adler & 0xffff;
        uint second = adler >> 16;
        while (!bytes.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(bytes.Length, 5552);
            foreach (byte value in bytes[..count])
            {
                first += value;
                second += first;
            }
            first %= 65521;
            second %= 65521;
            bytes = bytes[count..];
        }
        return (second << 16) | first;
    }

    private static void Unfilter(
        Span<byte> row, ReadOnlySpan<byte> previous, int filter, CancellationToken cancellationToken)
    {
        if (filter == 0)
        {
            return;
        }
        for (int index = 0; index < row.Length; index++)
        {
            if ((index & 16383) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            int left = index < 4 ? 0 : row[index - 4];
            int up = previous.IsEmpty ? 0 : previous[index];
            int upperLeft = previous.IsEmpty || index < 4 ? 0 : previous[index - 4];
            int prediction = filter switch
            {
                1 => left,
                2 => up,
                3 => (left + up) / 2,
                4 => Paeth(left, up, upperLeft),
                _ => throw new InvalidDataException("The PNG scanline filter is invalid.")
            };
            row[index] = unchecked((byte)(row[index] + prediction));
        }
    }

    private static int Paeth(int left, int up, int upperLeft)
    {
        int estimate = left + up - upperLeft;
        int leftDistance = Math.Abs(estimate - left);
        int upDistance = Math.Abs(estimate - up);
        int cornerDistance = Math.Abs(estimate - upperLeft);
        return leftDistance <= upDistance && leftDistance <= cornerDistance
            ? left : upDistance <= cornerDistance ? up : upperLeft;
    }

    private static (int FirstData, uint Adler) ValidateChunks(
        ReadOnlySpan<byte> png, CancellationToken cancellationToken)
    {
        int offset = 33;
        int firstData = -1;
        bool endedData = false;
        int dataBytes = 0;
        uint trailer = 0;
        while (offset < png.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (png.Length - offset < 12)
            {
                throw new InvalidDataException("The PNG chunk is truncated.");
            }
            uint size = BinaryPrimitives.ReadUInt32BigEndian(png[offset..]);
            if (size > png.Length - offset - 12)
            {
                throw new InvalidDataException("The PNG chunk data is truncated.");
            }
            int length = (int)size;
            ReadOnlySpan<byte> type = png.Slice(offset + 4, 4);
            foreach (byte character in type)
            {
                if (character is not (>= (byte)'A' and <= (byte)'Z') and not (>= (byte)'a' and <= (byte)'z'))
                {
                    throw new InvalidDataException("The PNG chunk type is invalid.");
                }
            }
            if ((type[2] & 0x20) != 0 ||
                PngCrc32.Calculate(type, png.Slice(offset + 8, length), cancellationToken) !=
                    BinaryPrimitives.ReadUInt32BigEndian(png[(offset + 8 + length)..]))
            {
                throw new InvalidDataException("The PNG chunk type or CRC is invalid.");
            }
            if (type.SequenceEqual("IDAT"u8))
            {
                if (endedData)
                {
                    throw new InvalidDataException("PNG data chunks must be consecutive.");
                }
                if (firstData < 0)
                {
                    firstData = offset;
                }
                ReadOnlySpan<byte> payload = png.Slice(offset + 8, length);
                if (length >= 4)
                {
                    trailer = BinaryPrimitives.ReadUInt32BigEndian(payload[^4..]);
                }
                else
                {
                    foreach (byte value in payload)
                    {
                        trailer = (trailer << 8) | value;
                    }
                }
                dataBytes += length;
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (length != 0 || firstData < 0 || dataBytes < 6 || offset + 12 != png.Length)
                {
                    throw new InvalidDataException("The PNG end chunk or data order is invalid.");
                }
                return (firstData, trailer);
            }
            else
            {
                if ((type[0] & 0x20) == 0)
                {
                    throw new InvalidDataException("The PNG contains an unsupported critical chunk.");
                }
                endedData |= firstData >= 0;
            }
            offset += length + 12;
        }
        throw new InvalidDataException("The PNG stream is incomplete.");
    }

    // IDAT payloads share the original encoded storage; no concatenated compressed or filtered image is allocated.
    private sealed class ImageDataStream(
        ReadOnlyMemory<byte> png, int firstData, CancellationToken cancellationToken) : Stream
    {
        private int _chunk = firstData;
        private int _consumed;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int written = 0;
            while (!buffer.IsEmpty && png.Span.Slice(_chunk + 4, 4).SequenceEqual("IDAT"u8))
            {
                cancellationToken.ThrowIfCancellationRequested();
                int length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.Span[_chunk..]);
                int count = Math.Min(buffer.Length, length - _consumed);
                png.Span.Slice(_chunk + 8 + _consumed, count).CopyTo(buffer);
                written += count;
                buffer = buffer[count..];
                _consumed += count;
                if (_consumed == length)
                {
                    _chunk += length + 12;
                    _consumed = 0;
                }
            }
            return written;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
