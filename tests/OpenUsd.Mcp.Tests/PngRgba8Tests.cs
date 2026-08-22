// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography;

namespace OpenUsd.Mcp.Tests;

public sealed class PngRgba8Tests
{
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
