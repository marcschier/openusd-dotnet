// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.IO.Compression;

namespace OpenUsd.Rendering;

/// <summary>Writes bounded RGBA8 PNG output without an image-sized filtering or compression buffer.</summary>
public static class PngRgba8Writer
{
    internal const int BytesPerPixel = 4;
    internal const int MaximumPixelByteCount = 256 * 1024 * 1024;

    /// <summary>Writes top-down, tightly packed RGBA8 pixels to a caller-owned stream.</summary>
    /// <remarks>
    /// The stream need not support seeking and remains open. The byte limit applies to this image,
    /// not any existing stream contents. Failure or cancellation may leave a partial PNG; callers
    /// publishing files must stage and atomically publish successful output themselves.
    /// Pixels are preserved without exposure, color-space, alpha or display transformations.
    /// Compression uses bounded 32-KiB output chunks and no image-sized scratch buffer.
    /// </remarks>
    /// <returns>The number of encoded bytes written.</returns>
    public static long Write(
        Stream destination,
        int width,
        int height,
        ReadOnlySpan<byte> rgba,
        long maximumBytes,
        CancellationToken cancellationToken = default) =>
        Write(destination, width, height, rgba, Rgba8RowOrder.TopDown, maximumBytes, cancellationToken);

    /// <summary>Writes tightly packed RGBA8 rows in either order without a full-image flip buffer.</summary>
    /// <remarks>
    /// Output is always top-down PNG. The stream remains open and need not support seeking.
    /// The byte quota, cancellation, partial-output and unchanged-pixel rules of the top-down overload apply.
    /// </remarks>
    /// <returns>The number of encoded bytes written.</returns>
    public static long Write(
        Stream destination,
        int width,
        int height,
        ReadOnlySpan<byte> rgba,
        Rgba8RowOrder rowOrder,
        long maximumBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        int byteCount = GetPixelByteCount(width, height);
        if (rgba.Length != byteCount)
        {
            throw new ArgumentException($"RGBA8 data must contain exactly {byteCount} bytes.", nameof(rgba));
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        if (rowOrder is not (Rgba8RowOrder.TopDown or Rgba8RowOrder.BottomUp))
        {
            throw new ArgumentOutOfRangeException(nameof(rowOrder));
        }
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The PNG destination must be writable.", nameof(destination));
        }
        cancellationToken.ThrowIfCancellationRequested();

        var output = new PngOutput(destination, maximumBytes, cancellationToken);
        output.WriteSignature();
        Span<byte> header = stackalloc byte[13];
        header.Clear();
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], checked((uint)height));
        header[8] = 8;
        header[9] = 6;
        output.WriteChunk("IHDR"u8, header);

        using var chunks = new DataChunkStream(output);
        using (var zlib = new ZLibStream(chunks, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            ReadOnlySpan<byte> filter = [0];
            int stride = checked(width * BytesPerPixel);
            for (int row = 0; row < height; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                zlib.Write(filter);
                int sourceRow = rowOrder == Rgba8RowOrder.TopDown ? row : height - row - 1;
                zlib.Write(rgba.Slice(sourceRow * stride, stride));
            }
        }
        chunks.Complete();
        output.WriteChunk("IEND"u8, []);
        return output.BytesWritten;
    }

    internal static int GetPixelByteCount(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        long pixels = (long)width * height;
        if (pixels > MaximumPixelByteCount / BytesPerPixel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width), $"RGBA8 images may not exceed {MaximumPixelByteCount} bytes.");
        }
        return (int)(pixels * BytesPerPixel);
    }

    internal static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba)
    {
        using var output = new MemoryStream();
        _ = Write(output, width, height, rgba, int.MaxValue);
        return output.ToArray();
    }

    private sealed class PngOutput(Stream destination, long maximumBytes, CancellationToken cancellationToken)
    {
        internal long BytesWritten { get; private set; }

        internal void WriteSignature()
        {
            Reserve(8);
            destination.Write([137, 80, 78, 71, 13, 10, 26, 10]);
            BytesWritten += 8;
        }

        internal void WriteChunk(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
        {
            long size = (long)data.Length + 12;
            Reserve(size);
            Span<byte> prefix = stackalloc byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(prefix, checked((uint)data.Length));
            type.CopyTo(prefix[4..]);
            destination.Write(prefix);
            destination.Write(data);
            Span<byte> crc = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(crc, PngCrc32.Calculate(type, data));
            destination.Write(crc);
            BytesWritten += size;
        }

        private void Reserve(long count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (count > maximumBytes - BytesWritten)
            {
                throw new RenderOutputQuotaExceededException("Encoded PNG exceeds the output byte budget.");
            }
        }
    }

    private sealed class DataChunkStream(PngOutput output) : Stream
    {
        private readonly byte[] _buffer = new byte[32 * 1024];
        private int _count;
        private bool _completed;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !_completed;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) =>
            Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_completed, this);
            while (!buffer.IsEmpty)
            {
                int length = Math.Min(_buffer.Length - _count, buffer.Length);
                buffer[..length].CopyTo(_buffer.AsSpan(_count));
                _count += length;
                buffer = buffer[length..];
                if (_count == _buffer.Length)
                {
                    Flush();
                }
            }
        }

        public override void Flush()
        {
            ObjectDisposedException.ThrowIf(_completed, this);
            if (_count != 0)
            {
                output.WriteChunk("IDAT"u8, _buffer.AsSpan(0, _count));
                _count = 0;
            }
        }

        internal void Complete()
        {
            Flush();
            _completed = true;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
