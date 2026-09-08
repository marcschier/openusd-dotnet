// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Storm;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerTextureRestorationOracleTests
{
    [Test]
    [Arguments("black")]
    [Arguments("red")]
    [Arguments("shifted-white")]
    [Arguments("transparent-white")]
    public async Task MissingGreenDoesNotProveTheWhiteQuadWasRestored(string alternative)
    {
        OpenUsdStormFramebufferCapture baseline = Frame("white");
        var oracle = new ViewerTextureRestorationOracle(baseline);

        await Assert.That(oracle.IsRestored(Frame(alternative))).IsFalse();
        await Assert.That(oracle.IsRestored(Frame("white"))).IsTrue();
    }

    [Test]
    public async Task ABlankBaselineOrIncompleteFrameCannotEstablishRestoration()
    {
        await Assert.That(() => new ViewerTextureRestorationOracle(Frame("black")))
            .Throws<InvalidDataException>();
        var oracle = new ViewerTextureRestorationOracle(Frame("white"));
        await Assert.That(oracle.IsRestored(Frame("white") with { RgbaPixels = ReadOnlyMemory<byte>.Empty }))
            .IsFalse();
        await Assert.That(oracle.IsRestored(Frame("white") with { Width = 32 })).IsFalse();
    }

    private static OpenUsdStormFramebufferCapture Frame(string kind)
    {
        byte[] pixels = new byte[16 * 16 * 4];
        for (int row = 0; row < 16; row++)
        {
            for (int column = 0; column < 16; column++)
            {
                int offset = ((row * 16) + column) * 4;
                pixels[offset + 3] = kind == "transparent-white" ? (byte)0 : (byte)255;
                bool quad = kind == "shifted-white"
                    ? row >= 8 && column >= 8
                    : row < 8 && column < 8;
                if (quad)
                {
                    pixels[offset] = kind == "black" ? (byte)0 : (byte)220;
                    pixels[offset + 1] = kind is "black" or "red" ? (byte)0 : (byte)220;
                    pixels[offset + 2] = kind is "black" or "red" ? (byte)0 : (byte)220;
                }
            }
        }
        return new OpenUsdStormFramebufferCapture(1, 0, 256, 64, 16, 16, 96, 0, 0, 0, 0, 0, pixels);
    }
}
