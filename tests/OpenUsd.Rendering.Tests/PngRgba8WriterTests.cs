// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.IO.Compression;

namespace OpenUsd.Rendering.Tests;

public sealed class PngRgba8WriterTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task WritesExactPixelsAndAlphaToANonSeekableCallerOwnedStream(bool bottomUp)
    {
        byte[] pixels = bottomUp
            ? [0, 0, 255, 64, 10, 20, 30, 40, 255, 0, 0, 255, 0, 255, 0, 128]
            : [255, 0, 0, 255, 0, 255, 0, 128, 0, 0, 255, 64, 10, 20, 30, 40];
        using var destination = new WriteOnlyStream();

        long written = PngRgba8Writer.Write(destination, 2, 2, pixels,
            bottomUp ? Rgba8RowOrder.BottomUp : Rgba8RowOrder.TopDown, maximumBytes: 1024);
        byte[] png = destination.ToArray();
        await Assert.That(written).IsEqualTo((long)png.Length);
        await Assert.That(png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            .IsTrue();
        await Assert.That(png.AsSpan(16, 13).SequenceEqual(
            new byte[] { 0, 0, 0, 2, 0, 0, 0, 2, 8, 6, 0, 0, 0 })).IsTrue();
        using var compressed = new MemoryStream();
        int offset = 8;
        bool sawEnd = false;
        while (offset < png.Length)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset)));
            if (png.AsSpan(offset + 4, 4).SequenceEqual("IDAT"u8))
            {
                compressed.Write(png, offset + 8, length);
            }
            if (png.AsSpan(offset + 4, 4).SequenceEqual("IEND"u8))
            {
                sawEnd = true;
            }
            offset = checked(offset + length + 12);
        }
        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        var filtered = new byte[18];
        zlib.ReadExactly(filtered);
        await Assert.That(filtered.SequenceEqual(new byte[]
        {
            0, 255, 0, 0, 255, 0, 255, 0, 128,
            0, 0, 0, 255, 64, 10, 20, 30, 40
        })).IsTrue();
        await Assert.That(zlib.ReadByte()).IsEqualTo(-1);
        await Assert.That(sawEnd).IsTrue();
        await Assert.That(offset).IsEqualTo(png.Length);
        destination.WriteByte(42);
        await Assert.That(destination.ToArray().Length).IsEqualTo(png.Length + 1);
    }

    [Test]
    [Arguments(5L)]
    [Arguments(33L)]
    [Arguments(65L)]
    public async Task RefusesOutputBeyondTheImageBudgetWithoutClosingTheCallerStream(long maximumBytes)
    {
        using var destination = new WriteOnlyStream();
        destination.Write([8, 9, 10]);
        await Assert.That(() => PngRgba8Writer.Write(destination, 1, 1, [1, 2, 3, 4], maximumBytes))
            .Throws<InvalidOperationException>();
        await Assert.That((long)destination.ToArray().Length).IsLessThanOrEqualTo(maximumBytes + 3);
        await Assert.That(destination.ToArray().AsSpan(0, 3).SequenceEqual(new byte[] { 8, 9, 10 })).IsTrue();
        destination.WriteByte(42);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StreamingALargeFrameDoesNotAllocateImageSizedScratchBuffers(bool bottomUp)
    {
        var pixels = new byte[1024 * 256 * 4];
        new Random(482).NextBytes(pixels);

        long before = GC.GetAllocatedBytesForCurrentThread();
        long written = PngRgba8Writer.Write(Stream.Null, 1024, 256, pixels,
            bottomUp ? Rgba8RowOrder.BottomUp : Rgba8RowOrder.TopDown, maximumBytes: 2 * 1024 * 1024);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(written).IsGreaterThan(1024L * 1024);
        await Assert.That(allocated).IsLessThan(128L * 1024);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancellationNeverPublishesACompleteImageOrClosesTheDestination(bool preCanceled)
    {
        using var cancellation = new CancellationTokenSource();
        long canceledAt = 0;
        using var destination = new WriteOnlyStream(count =>
        {
            if (!preCanceled && count > 33 && canceledAt == 0)
            {
                canceledAt = count;
                cancellation.Cancel();
            }
        });
        var pixels = new byte[256 * 256 * 4];
        new Random(923).NextBytes(pixels);
        if (preCanceled)
        {
            cancellation.Cancel();
        }
        await Assert.That(() => PngRgba8Writer.Write(
            destination, 256, 256, pixels, 1024 * 1024, cancellation.Token))
            .Throws<OperationCanceledException>();
        byte[] partial = destination.ToArray();
        await Assert.That(destination.CanWrite).IsTrue();
        if (preCanceled)
        {
            await Assert.That(partial.Length).IsEqualTo(0);
        }
        else
        {
            await Assert.That(canceledAt).IsGreaterThan(33);
            await Assert.That((long)partial.Length).IsLessThanOrEqualTo(canceledAt + (32 * 1024) + 4);
        }
        await Assert.That(partial.AsSpan().EndsWith(
            new byte[] { 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 })).IsFalse();
    }

    [Test]
    public async Task InvalidInputsAreRejectedBeforeWritingAnything()
    {
        using var destination = new WriteOnlyStream();
        await Assert.That(() => PngRgba8Writer.Write(destination, 0, 1, [], 1024))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => PngRgba8Writer.Write(destination, 1, 0, [], 1024))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => PngRgba8Writer.Write(destination, (64 * 1024 * 1024) + 1, 1, [], 1024))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => PngRgba8Writer.Write(destination, int.MaxValue, int.MaxValue, [], 1024))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => PngRgba8Writer.Write(
            destination, 1, 1, [1, 2, 3, 4], (Rgba8RowOrder)99, 1024))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => PngRgba8Writer.Write(destination, 1, 1, [1, 2, 3], 1024))
            .Throws<ArgumentException>();
        await Assert.That(() => PngRgba8Writer.Write(destination, 1, 1, [1, 2, 3, 4], 0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => PngRgba8Writer.Write(null!, 1, 1, [1, 2, 3, 4], 1024))
            .Throws<ArgumentNullException>();
        using var readOnly = new MemoryStream([1, 2, 3, 4], writable: false);
        await Assert.That(() => PngRgba8Writer.Write(readOnly, 1, 1, [1, 2, 3, 4], 1024))
            .Throws<ArgumentException>();
        await Assert.That(destination.ToArray().Length).IsEqualTo(0);
        await Assert.That(readOnly.ToArray().SequenceEqual(new byte[] { 1, 2, 3, 4 })).IsTrue();
    }

    private sealed class WriteOnlyStream(Action<long>? progress = null) : Stream
    {
        private readonly MemoryStream _bytes = new();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => _bytes.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        internal byte[] ToArray() => _bytes.ToArray();
        public override void Flush() => _bytes.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _bytes.Write(buffer);
            progress?.Invoke(_bytes.Length);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _bytes.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
