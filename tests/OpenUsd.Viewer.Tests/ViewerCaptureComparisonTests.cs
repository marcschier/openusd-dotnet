// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.IO.Compression;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerCaptureComparisonTests
{
    [Test]
    public async Task PngDataAndChecksumMaySpanSingleByteIdatChunks()
    {
        string path = Path.Combine(Path.GetTempPath(), $"viewer-png-chunks-{Guid.NewGuid():N}.png");
        byte[] original = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAGUlEQVR4nGNJAQKjOUZzWERSUs6V" +
            "LHPiAgA42AZFcKu75AAAAABJRU5ErkJggg==");
        try
        {
            using (FileStream png = File.Create(path))
            {
                png.Write(original.AsSpan(0, 33));
                int length = (int)BinaryPrimitives.ReadUInt32BigEndian(original.AsSpan(33));
                for (int index = 0; index < length; index++)
                {
                    WriteChunk(png, "IDAT"u8, []);
                    WriteChunk(png, "IDAT"u8, original.AsSpan(41 + index, 1));
                }
                WriteChunk(png, "IDAT"u8, []);
                WriteChunk(png, "IEND"u8, []);
            }

            ViewerCapturePair pair = await ViewerCaptureComparison.LoadAsync(path, path, CancellationToken.None);

            await Assert.That(Convert.ToHexString(pair.Before.Rgba))
                .IsEqualTo("646464649600960078C8C8320A0A0A0A");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    [Arguments(8193, 1, "8192 pixels")]
    [Arguments(4097, 4096, "16 megapixels")]
    [Arguments(int.MaxValue, int.MaxValue, "pixel budget")]
    public async Task PngDimensionsAreAdmittedBeforeReadingCompressedData(int width, int height, string diagnostic)
    {
        string path = Path.Combine(Path.GetTempPath(), $"viewer-png-budget-{Guid.NewGuid():N}.png");
        byte[] header = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0k");
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(20), (uint)height);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(29),
            PngCrc32.Calculate(header.AsSpan(12, 4), header.AsSpan(16, 13)));
        try
        {
            await File.WriteAllBytesAsync(path, header);
            InvalidDataException? failure = null;
            try
            {
                _ = await ViewerCaptureComparison.LoadAsync(path, path, CancellationToken.None);
            }
            catch (InvalidDataException exception)
            {
                failure = exception;
            }
            await Assert.That(failure).IsNotNull();
            await Assert.That(failure!.Message).Contains(diagnostic);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    [Arguments("crc")]
    [Arguments("length")]
    [Arguments("split-data")]
    [Arguments("missing-data")]
    [Arguments("trailing")]
    [Arguments("excess-inflated")]
    [Arguments("truncated-zlib")]
    [Arguments("unfinished-deflate")]
    [Arguments("filter")]
    public async Task MalformedPngsNeverBecomePartialSuccessfulComparisons(string defect)
    {
        string root = Directory.CreateTempSubdirectory("viewer-png-invalid-").FullName;
        string valid = Path.Combine(root, "valid.png");
        string invalid = Path.Combine(root, "invalid.png");
        byte[] png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNlZGJmAQAAMgAQvw7tDQAAAABJRU5ErkJggg==");
        try
        {
            using (FileStream image = File.Create(valid))
            {
                _ = PngRgba8Writer.Write(image, 1, 1, [1, 2, 3, 4], 1024);
            }
            if (defect != "filter")
            {
                png = await File.ReadAllBytesAsync(valid);
            }
            if (defect == "crc")
            {
                png[^1] ^= 1;
            }
            else if (defect == "length")
            {
                BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(33), uint.MaxValue);
            }
            else if (defect == "trailing")
            {
                png = [.. png, 0];
            }
            else if (defect is "split-data" or "missing-data" or "excess-inflated" or
                "truncated-zlib" or "unfinished-deflate")
            {
                using var rebuilt = new MemoryStream();
                rebuilt.Write(png.AsSpan(0, 33));
                if (defect == "split-data")
                {
                    int length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(33));
                    WriteChunk(rebuilt, "IDAT"u8, png.AsSpan(41, 1));
                    WriteChunk(rebuilt, "tEXt"u8, "note\0split"u8);
                    WriteChunk(rebuilt, "IDAT"u8, png.AsSpan(42, length - 1));
                }
                else if (defect == "excess-inflated")
                {
                    using var compressed = new MemoryStream();
                    using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
                    {
                        zlib.Write(new byte[1_000_000]);
                    }
                    WriteChunk(rebuilt, "IDAT"u8, compressed.ToArray());
                }
                else if (defect == "truncated-zlib")
                {
                    int length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(33));
                    WriteChunk(rebuilt, "IDAT"u8, png.AsSpan(41, length - 4));
                }
                else if (defect == "unfinished-deflate")
                {
                    int length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(33));
                    await Assert.That(png[43] & 1).IsEqualTo(1);
                    png[43] &= 0xfe;
                    WriteChunk(rebuilt, "IDAT"u8, png.AsSpan(41, length));
                }
                WriteChunk(rebuilt, "IEND"u8, []);
                png = rebuilt.ToArray();
            }
            await File.WriteAllBytesAsync(invalid, png);

            await Assert.That(async () => await ViewerCaptureComparison.LoadAsync(
                valid, invalid, CancellationToken.None))
                .Throws<InvalidDataException>();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(size, (uint)data.Length);
        stream.Write(size);
        stream.Write(type);
        stream.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(size, PngCrc32.Calculate(type, data));
        stream.Write(size);
    }

    [Test]
    public async Task PngAndBmpCapturesCompareInImageOrderWithoutChangingRgbaValues()
    {
        string root = Directory.CreateTempSubdirectory("viewer-png-comparison-").FullName;
        try
        {
            string before = Path.Combine(root, "before.png");
            string after = Path.Combine(root, "after.bmp");
            using (FileStream png = File.Create(before))
            {
                _ = PngRgba8Writer.Write(png, 2, 2,
                    [0, 0, 255, 64, 10, 20, 30, 40, 255, 0, 0, 255, 0, 255, 0, 128],
                    Rgba8RowOrder.BottomUp, 1024);
            }
            ViewerFrameBitmapWriter.WriteBmp(after, 1, 1, [20, 40, 80, 255]);

            ViewerCapturePair pair = await ViewerCaptureComparison.LoadAsync(before, after, CancellationToken.None);

            await Assert.That(pair.Before.Width).IsEqualTo(2);
            await Assert.That(pair.Before.Height).IsEqualTo(2);
            await Assert.That(Convert.ToHexString(pair.Before.Rgba))
                .IsEqualTo("FF0000FF00FF00800000FF400A141E28");
            await Assert.That(Convert.ToHexString(pair.After.Rgba)).IsEqualTo("142850FF");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task TopDown32BitBmpTreatsTheReservedByteAsOpaque()
    {
        string path = Path.Combine(AppContext.BaseDirectory, $"comparison-{Guid.NewGuid():N}.bmp");
        try
        {
            byte[] bitmap = Convert.FromHexString(
                "424D4600000000000000360000002800" +
                "000002000000FEFFFFFF010020000000" +
                "00001000000000000000000000000000" +
                "000000000000" +
                "0000FF0000FF0040FF0000FFFFFFFF00");
            await File.WriteAllBytesAsync(path, bitmap);

            ViewerCapturePair pair = await ViewerCaptureComparison.LoadAsync(path, path, CancellationToken.None);

            await Assert.That(Convert.ToHexString(pair.Before.Rgba))
                .IsEqualTo("FF0000FF" + "00FF00FF" + "0000FFFF" + "FFFFFFFF");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    [Arguments(8193, 1, 24634L)]
    [Arguments(4097, 4096, 50348086L)]
    [Arguments(1, 1, 67108865L)]
    public async Task DimensionPixelAndFileBudgetsRejectOtherwiseCompleteBmpFiles(
        int width, int height, long fileBytes)
    {
        string path = Path.Combine(AppContext.BaseDirectory, $"comparison-limit-{Guid.NewGuid():N}.bmp");
        try
        {
            byte[] header = Convert.FromHexString(
                "424D4600000000000000360000002800" +
                "00000200000002000000010018000000" +
                "00000000000000000000000000000000" +
                "000000000000");
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(2), checked((uint)fileBytes));
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(18), width);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(22), height);
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(header);
                stream.SetLength(fileBytes);
            }

            await Assert.That(async () => await ViewerCaptureComparison.LoadAsync(path, path, CancellationToken.None))
                .Throws<InvalidDataException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ExistingCapturesDecodeInImageOrderWithExplicitSizeAndCancellationLimits()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "comparison-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string before = Path.Combine(root, "before.bmp");
            string after = Path.Combine(root, "after.bmp");
            byte[] bitmap = Convert.FromHexString(
                "424D4600000000000000360000002800" +
                "00000200000002000000010018000000" +
                "00001000000000000000000000000000" +
                "000000000000" +
                "FF0000FFFFFF0000" +
                "0000FF00FF000000");
            await File.WriteAllBytesAsync(before, bitmap);
            bitmap[54] = 0;
            await File.WriteAllBytesAsync(after, bitmap);

            ViewerCapturePair pair = await ViewerCaptureComparison.LoadAsync(before, after, CancellationToken.None);

            await Assert.That(pair.Before.Width).IsEqualTo(2);
            await Assert.That(pair.Before.Height).IsEqualTo(2);
            await Assert.That(Convert.ToHexString(pair.Before.Rgba))
                .IsEqualTo("FF0000FF" + "00FF00FF" + "0000FFFF" + "FFFFFFFF");
            await Assert.That(Convert.ToHexString(pair.After.Rgba))
                .IsEqualTo("FF0000FF" + "00FF00FF" + "000000FF" + "FFFFFFFF");

            BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(18), 8193);
            await File.WriteAllBytesAsync(after, bitmap);
            await Assert.That(async () => await ViewerCaptureComparison.LoadAsync(
                before, after, CancellationToken.None)).Throws<InvalidDataException>();

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.That(async () => await ViewerCaptureComparison.LoadAsync(
                before, before, cancellation.Token)).Throws<OperationCanceledException>();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
