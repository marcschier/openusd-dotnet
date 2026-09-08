// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenUsd.Mcp.Tests;

public sealed class PngRgba8Tests
{
    [Test]
    [Arguments("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAGklEQVR4nGPgEpHT4JdU1mUQ0bAJ" +
        "kDNyiwIAEcQClZcoLZwAAAAASUVORK5CYII=")]
    [Arguments("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAF0lEQVR4nGPkEpHTYAUCRhENmwAu" +
        "IAAADAMBa1PYsO0AAAAASUVORK5CYII=")]
    [Arguments("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAE0lEQVR4nGPiEpHT4JdU1mWCMQAO" +
        "qAG9mRqjWQAAAABJRU5ErkJggg==")]
    [Arguments("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAGklEQVR4nGPmEpHT4OIXkWTml9O1" +
        "4RUSlwEADYMBmebbFy8AAAAASUVORK5CYII=")]
    [Arguments("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFElEQVR4nGPhEpHTYAUCFhCDCwgA" +
        "CfwBDQxoJFYAAAAASUVORK5CYII=")]
    public async Task StandardPngFiltersDecodeIndependentTwoRowFixtures(string fixture)
    {
        ImageRgba8 decoded = PngRgba8Decoder.Decode(Convert.FromBase64String(fixture));

        await Assert.That(decoded.Width).IsEqualTo(2);
        await Assert.That(decoded.Height).IsEqualTo(2);
        await Assert.That(Convert.ToHexString(decoded.Pixels.Span))
            .IsEqualTo("0A141E280F19232D14283C501E32465A");
    }

    [Test]
    public async Task PaethUsesAllNeighborChoicesAndModuloByteArithmetic()
    {
        byte[] png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAGUlEQVR4nGNJAQKjOUZzWERSUs6V" +
            "LHPiAgA42AZFcKu75AAAAABJRU5ErkJggg==");

        ImageRgba8 decoded = PngRgba8Decoder.Decode(png);

        await Assert.That(Convert.ToHexString(decoded.Pixels.Span))
            .IsEqualTo("646464649600960078C8C8320A0A0A0A");
    }

    [Test]
    public async Task DecodingDoesNotAllocateConcatenatedIdatOrFilteredImageBuffers()
    {
        byte[] pixels = new byte[1024 * 1024 * 4];
        new Random(782).NextBytes(pixels);
        byte[] png = PngRgba8Encoder.Encode(new ImageRgba8(1024, 1024, pixels));

        long before = GC.GetAllocatedBytesForCurrentThread();
        ImageRgba8 decoded = PngRgba8Decoder.Decode(png);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(decoded.Pixels.Span.SequenceEqual(pixels)).IsTrue();
        await Assert.That(allocated).IsLessThan(png.Length + (2L * pixels.Length) + (128 * 1024));
    }

    [Test]
    public async Task EncodeRoundTripsRgba8Exactly()
    {
        byte[] pixels =
        [
            255, 0, 0, 255,
            0, 255, 0, 128,
            0, 0, 255, 64,
            10, 20, 30, 40,
        ];
        var image = new ImageRgba8(2, 2, pixels);

        byte[] png = PngRgba8Encoder.Encode(image);
        ImageRgba8 decoded = PngRgba8Decoder.Decode(png);

        await Assert.That(decoded.Width).IsEqualTo(2);
        await Assert.That(decoded.Height).IsEqualTo(2);
        await Assert.That(decoded.Pixels.ToArray()).IsEquivalentTo(pixels);
    }

    [Test]
    public async Task EncodeIsDeterministic()
    {
        var image = new ImageRgba8(
            2,
            1,
            [0, 1, 2, 3, 252, 253, 254, 255]);

        byte[] first = PngRgba8Encoder.Encode(image);
        byte[] second = PngRgba8Encoder.Encode(image);

        await Assert.That(second).IsEquivalentTo(first);
        await Assert.That(Convert.ToHexString(SHA256.HashData(first)).ToLowerInvariant())
            .IsEqualTo("197e4be2670509fdcc06a56da922ebe83ddf514900a9df353f02f1f59c1a07b5");
    }

    [Test]
    public async Task LargeFramesRoundTripThroughBoundedDataChunks()
    {
        var pixels = new byte[512 * 256 * 4];
        new Random(835).NextBytes(pixels);
        byte[] png = PngRgba8Encoder.Encode(new ImageRgba8(512, 256, pixels));
        int dataChunks = 0;
        for (int offset = 8; offset < png.Length;)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset)));
            if (png.AsSpan(offset + 4, 4).SequenceEqual("IDAT"u8))
            {
                dataChunks++;
                await Assert.That(length).IsLessThanOrEqualTo(32 * 1024);
            }
            offset = checked(offset + length + 12);
        }
        ImageRgba8 decoded = PngRgba8Decoder.Decode(png);
        await Assert.That(dataChunks).IsGreaterThan(1);
        await Assert.That(decoded.Width).IsEqualTo(512);
        await Assert.That(decoded.Height).IsEqualTo(256);
        await Assert.That(decoded.Pixels.Span.SequenceEqual(pixels)).IsTrue();
    }

    [Test]
    public async Task DecoderRejectsCorruptChunkCrc()
    {
        byte[] png = PngRgba8Encoder.Encode(
            new ImageRgba8(1, 1, [1, 2, 3, 4]));
        png[20] ^= 0x01;

        await Assert.That(() => PngRgba8Decoder.Decode(png))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task ImageRejectsMismatchedPixelCount()
    {
        await Assert.That(() => new ImageRgba8(2, 2, new byte[15]))
            .Throws<ArgumentException>();
    }
}
