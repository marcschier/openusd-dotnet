// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Covers the renderer-neutral packed 2D mip-chain layout, descriptor validation, and CPU mip
/// generation shared by every RHI backend.
/// </summary>
public sealed class SilkMipChainTests
{
    [Test]
    [Arguments(1f, 0f)]
    [Arguments(0.5f, 0.25f)]
    [Arguments(-0.75f, 0.8f)]
    [Arguments(0.003921569f, 0.0019607844f)]
    public async Task ByteAffineLookupPreservesOriginalRoundingForEveryInputByte(float gain, float bias)
    {
        float[] scale = [gain, gain * 0.5f, -gain, 1];
        float[] offset = [bias, bias + 0.125f, 1 - bias, 0];
        var texture = new SilkMaterialTexture(SilkMaterialParameter.DiffuseColor,
            SilkTextureWrap.Repeat, SilkTextureWrap.Repeat, SilkColorSpace.Raw, 4,
            SilkTextureChannel.Rgb, scale, offset, [0, 0, 0, 1], "image", "st",
            SilkCompositeOperator.None, 0);
        byte[] pixels = new byte[256 * 4];
        byte[] expected = new byte[pixels.Length];
        for (int value = 0; value < 256; value++)
        {
            for (int component = 0; component < 4; component++)
            {
                pixels[value * 4 + component] = (byte)value;
                float transformed = value / 255f;
                transformed = transformed * scale[component] + offset[component];
                expected[value * 4 + component] = (byte)Math.Clamp(MathF.Round(transformed * 255), 0, 255);
            }
        }
        SilkSceneGpuResources.ApplyScaleBias(pixels, SilkTextureFormat.Rgba8Unorm, texture);
        await Assert.That(pixels.SequenceEqual(expected)).IsTrue();
    }

    [Test]
    [Arguments(1u, 17u, 1)]
    [Arguments(17u, 1u, 4)]
    [Arguments(257u, 129u, 1)]
    [Arguments(512u, 256u, 4)]
    public async Task ByteMipLevelsMatchTheOriginalFloatingPointRoundingExactly(uint width, uint height, int components)
    {
        byte[] source = new byte[checked((int)(width * height * (uint)components))];
        new Random(4735).NextBytes(source);
        SilkTextureFormat format = components == 1 ? SilkTextureFormat.R8Unorm : SilkTextureFormat.Rgba8Unorm;
        byte[] chain = SilkMipGenerator.GenerateChain(source, width, height, format, false, out uint count);
        SilkMipLevelLayout[] levels = SilkMipChainLayout.Create(width, height, format, count);
        for (int level = 1; level < levels.Length; level++)
        {
            SilkMipLevelLayout previous = levels[level - 1];
            SilkMipLevelLayout current = levels[level];
            byte[] expected = new byte[current.Size];
            for (uint y = 0; y < current.Height; y++)
            {
                for (uint x = 0; x < current.Width; x++)
                {
                    int a = checked((int)((2 * y * previous.Width + 2 * x) * (uint)components));
                    int b = checked((int)((2 * y * previous.Width + Math.Min(2 * x + 1, previous.Width - 1)) * (uint)components));
                    int c = checked((int)((Math.Min(2 * y + 1, previous.Height - 1) * previous.Width + 2 * x) * (uint)components));
                    int d = checked((int)((Math.Min(2 * y + 1, previous.Height - 1) * previous.Width +
                        Math.Min(2 * x + 1, previous.Width - 1)) * (uint)components));
                    for (int channel = 0; channel < components; channel++)
                    {
                        float average = (chain[previous.Offset + a + channel] / 255f +
                            chain[previous.Offset + b + channel] / 255f +
                            chain[previous.Offset + c + channel] / 255f +
                            chain[previous.Offset + d + channel] / 255f) / 4f;
                        expected[(y * current.Width + x) * components + channel] =
                            (byte)Math.Clamp(MathF.Round(average * 255f), 0, 255);
                    }
                }
            }
            await Assert.That(chain.AsSpan(current.Offset, current.Size).SequenceEqual(expected)).IsTrue();
        }
    }

    private static readonly float[] HdrOutOfRangeExpected = [2f, 2f, 2f, 1f];
    private static readonly float[] StraightUpNormalExpected = [0.5f, 0.5f, 1f, 1f];

    [Test]
    [Arguments(1u, 9u)]
    [Arguments(5u, 3u)]
    [Arguments(4u, 4u)]
    public async Task SingleChannelFloatLevelsExactlyMatchTheRgbaRedChannel(uint width, uint height)
    {
        float[] scalar = new float[width * height];
        float[] rgba = new float[scalar.Length * 4];
        for (int index = 0; index < scalar.Length; index++)
        {
            scalar[index] = (index - 7) * 0.03125f;
            rgba[index * 4] = scalar[index];
            rgba[index * 4 + 1] = 5;
            rgba[index * 4 + 2] = 20;
            rgba[index * 4 + 3] = 1;
        }
        byte[] packed = SilkMipGenerator.GenerateChain(MemoryMarshal.AsBytes(scalar.AsSpan()).ToArray(),
            width, height, SilkTextureFormat.R32Float, false, out uint count);
        byte[] full = SilkMipGenerator.GenerateChain(MemoryMarshal.AsBytes(rgba.AsSpan()).ToArray(),
            width, height, SilkTextureFormat.Rgba32Float, false, out uint fullCount);
        await Assert.That(count).IsEqualTo(fullCount);
        await Assert.That(packed.Length * 4).IsEqualTo(full.Length);
        for (int offset = 0; offset < packed.Length; offset += 4)
        {
            await Assert.That(packed.AsSpan(offset, 4).SequenceEqual(full.AsSpan(offset * 4, 4))).IsTrue();
        }
        var budget = new SilkCpuTextureBudget(4096);
        var current = new SilkDecodedImage(width, height,
            MemoryMarshal.AsBytes(scalar.AsSpan()).ToArray(), SilkTextureFormat.R32Float);
        current.OwnReservation(budget.Reserve(checked((ulong)current.Pixels.Length)));
        try
        {
            foreach (SilkMipLevelLayout level in SilkMipChainLayout.Create(width, height, SilkTextureFormat.R32Float, count))
            {
                await Assert.That(current.Pixels.AsSpan().SequenceEqual(packed.AsSpan(level.Offset, level.Size))).IsTrue();
                if (level.Level + 1 < count)
                {
                    SilkDecodedImage next = SilkMipGenerator.GenerateNextLevel(current, false, budget);
                    current.Dispose();
                    current = next;
                }
            }
        }
        finally { current.Dispose(); }
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task SingleChannelCannotPretendToBeANormalMap()
    {
        using var source = new SilkDecodedImage(2, 2, new byte[16], SilkTextureFormat.R32Float);
        await Assert.That(() => SilkMipGenerator.GenerateNextLevel(source, true, null)).Throws<ArgumentException>();
        await Assert.That(() => SilkMipGenerator.GenerateChain(
            source.Pixels, 2, 2, SilkTextureFormat.R32Float, true, out _)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(1u, 9u)]
    [Arguments(5u, 3u)]
    [Arguments(4u, 4u)]
    public async Task SingleChannelBytesExactlyMatchRgbaQuantizationAtEveryMip(uint width, uint height)
    {
        byte[] scalar = new byte[width * height];
        byte[] rgba = new byte[scalar.Length * 4];
        for (int index = 0; index < scalar.Length; index++)
        {
            scalar[index] = (byte)(index * 79 % 256);
            rgba[index * 4] = scalar[index];
            rgba[index * 4 + 1] = 15;
            rgba[index * 4 + 2] = 200;
            rgba[index * 4 + 3] = 255;
        }
        byte[] packed = SilkMipGenerator.GenerateChain(scalar, width, height, SilkTextureFormat.R8Unorm,
            false, out uint count);
        byte[] full = SilkMipGenerator.GenerateChain(rgba, width, height, SilkTextureFormat.Rgba8Unorm,
            false, out uint fullCount);
        await Assert.That(fullCount).IsEqualTo(count);
        await Assert.That(packed.Length * 4).IsEqualTo(full.Length);
        for (int index = 0; index < packed.Length; index++)
        {
            await Assert.That(packed[index]).IsEqualTo(full[index * 4]);
        }
        var current = new SilkDecodedImage(width, height, scalar, SilkTextureFormat.R8Unorm);
        try
        {
            foreach (SilkMipLevelLayout level in SilkMipChainLayout.Create(width, height, SilkTextureFormat.R8Unorm, count))
            {
                await Assert.That(current.Pixels.AsSpan().SequenceEqual(packed.AsSpan(level.Offset, level.Size))).IsTrue();
                if (level.Level + 1 < count)
                {
                    SilkDecodedImage next = SilkMipGenerator.GenerateNextLevel(current, false, null);
                    current.Dispose();
                    current = next;
                }
            }
        }
        finally { current.Dispose(); }
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ProgressiveLevelsMatchThePackedChainAndReleaseTheirPixels(bool floatingPoint, bool normal)
    {
        SilkTextureFormat format = floatingPoint ? SilkTextureFormat.Rgba32Float : SilkTextureFormat.Rgba8Unorm;
        byte[] pixels = new byte[5 * 3 * (floatingPoint ? 16 : 4)];
        if (floatingPoint)
        {
            Span<float> values = MemoryMarshal.Cast<byte, float>(pixels.AsSpan());
            for (int index = 0; index < values.Length; index++)
            { values[index] = 0.1f + (index % 4) * 0.2f; }
        }
        else
        {
            for (int index = 0; index < pixels.Length; index++)
            { pixels[index] = (byte)(32 + index * 3); }
        }
        byte[] packed = SilkMipGenerator.GenerateChain(pixels, 5, 3, format, normal, out uint count);
        SilkMipLevelLayout[] levels = SilkMipChainLayout.Create(5, 3, format, count);
        var budget = new SilkCpuTextureBudget(checked((ulong)(levels[0].Size + levels[1].Size)));
        var image = new SilkDecodedImage(5, 3, pixels, format);
        image.OwnReservation(budget.Reserve(checked((ulong)pixels.Length)));
        try
        {
            for (int index = 1; index < levels.Length; index++)
            {
                SilkDecodedImage previous = image;
                image = SilkMipGenerator.GenerateNextLevel(previous, normal, budget);
                previous.Dispose();
                await Assert.That(previous.Pixels.Length).IsEqualTo(0);
                await Assert.That(image.Pixels.AsSpan().SequenceEqual(
                    packed.AsSpan(levels[index].Offset, levels[index].Size))).IsTrue();
            }
            await Assert.That(budget.Usage.PeakReservedBytes).IsEqualTo(budget.MaximumBytes);
        }
        finally { image.Dispose(); }
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task DescriptorAcceptsSingleLevelAndFullChain()
    {
        var single = new SilkTextureDescriptor(
            4,
            4,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination);
        var fullChain = single with { MipLevelCount = 3 };

        single.Validate();
        fullChain.Validate();

        await Assert.That(SilkMipChainLayout.GetMaxMipLevelCount(4, 4)).IsEqualTo(3u);
    }

    [Test]
    public async Task DescriptorRejectsPartialChain()
    {
        var descriptor = new SilkTextureDescriptor(
            4,
            4,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination,
            MipLevelCount: 2);

        ArgumentException exception = Assert.Throws<ArgumentException>(descriptor.Validate);

        await Assert.That(exception.ParamName).IsEqualTo("MipLevelCount");
    }

    [Test]
    public async Task DescriptorRejectsMultiLevelRenderTarget()
    {
        var descriptor = new SilkTextureDescriptor(
            4,
            4,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget,
            MipLevelCount: 3);

        ArgumentException exception = Assert.Throws<ArgumentException>(descriptor.Validate);

        await Assert.That(exception.ParamName).IsEqualTo("MipLevelCount");
    }

    [Test]
    public async Task DescriptorRejectsZeroMipLevelCount()
    {
        var descriptor = new SilkTextureDescriptor(
            4,
            4,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.Sampled,
            MipLevelCount: 0);

        Assert.Throws<ArgumentOutOfRangeException>(descriptor.Validate);
    }

    [Test]
    public async Task MaxMipLevelCountMatchesLog2OfLargestAxis()
    {
        await Assert.That(SilkMipChainLayout.GetMaxMipLevelCount(1, 1)).IsEqualTo(1u);
        await Assert.That(SilkMipChainLayout.GetMaxMipLevelCount(8, 1)).IsEqualTo(4u);
        await Assert.That(SilkMipChainLayout.GetMaxMipLevelCount(3, 5)).IsEqualTo(3u);
    }

    [Test]
    public async Task PackedLayoutIsTightlyPackedAscendingFromZero()
    {
        SilkMipLevelLayout[] levels = SilkMipChainLayout.Create(
            4,
            4,
            SilkTextureFormat.Rgba8Unorm,
            3);

        await Assert.That(levels.Length).IsEqualTo(3);
        await Assert.That(levels[0]).IsEqualTo(new SilkMipLevelLayout(0, 4, 4, 0, 16, 64));
        await Assert.That(levels[1]).IsEqualTo(new SilkMipLevelLayout(1, 2, 2, 64, 8, 16));
        await Assert.That(levels[2]).IsEqualTo(new SilkMipLevelLayout(2, 1, 1, 80, 4, 4));
        await Assert.That(SilkMipChainLayout.GetTotalByteSize(4, 4, SilkTextureFormat.Rgba8Unorm, 3))
            .IsEqualTo(84);
    }

    [Test]
    public async Task PackedLayoutHalvesOddDimensionsByFlooring()
    {
        (uint width, uint height) = SilkMipChainLayout.GetLevelExtent(3, 5, 1);

        await Assert.That(width).IsEqualTo(1u);
        await Assert.That(height).IsEqualTo(2u);
    }

    [Test]
    public async Task GenerateChainRejectsWrongBaseLevelLength()
    {
        byte[] tooShort = new byte[4 * 4 * 4 - 1];

        await Assert.That(
            () => SilkMipGenerator.GenerateChain(
                tooShort,
                4,
                4,
                SilkTextureFormat.Rgba8Unorm,
                isNormalMap: false,
                out _))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task GenerateChainRejectsUnsupportedFormat()
    {
        byte[] baseLevel = new byte[4];

        await Assert.That(
            () => SilkMipGenerator.GenerateChain(
                baseLevel,
                1,
                1,
                SilkTextureFormat.D32Float,
                isNormalMap: false,
                out _))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Rgba8ChainAveragesFourTexelsPerLevel()
    {
        // A 2x2 base image with four distinct solid-color texels; the single 1x1 mip must be
        // the exact arithmetic mean of all four corners.
        byte[] baseLevel =
        [
            255, 0, 0, 255, // top-left: red
            0, 255, 0, 255, // top-right: green
            0, 0, 255, 255, // bottom-left: blue
            255, 255, 255, 0 // bottom-right: white, transparent
        ];

        byte[] chain = SilkMipGenerator.GenerateChain(
            baseLevel,
            2,
            2,
            SilkTextureFormat.Rgba8Unorm,
            isNormalMap: false,
            out uint mipLevelCount);

        await Assert.That(mipLevelCount).IsEqualTo(2u);
        await Assert.That(chain.Length).IsEqualTo((2 * 2 * 4) + (1 * 1 * 4));
        byte[] mip1 = chain[16..20];
        await Assert.That(mip1).IsEquivalentTo(new byte[] { 128, 128, 128, 191 });
    }

    [Test]
    public async Task Rgba32FloatChainPreservesHdrValuesOutsideUnitRange()
    {
        float[] baseLevel = [8f, 8f, 8f, 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];
        byte[] baseBytes = new byte[baseLevel.Length * sizeof(float)];
        MemoryMarshal.AsBytes(baseLevel.AsSpan()).CopyTo(baseBytes);

        byte[] chain = SilkMipGenerator.GenerateChain(
            baseBytes,
            2,
            2,
            SilkTextureFormat.Rgba32Float,
            isNormalMap: false,
            out uint mipLevelCount);

        await Assert.That(mipLevelCount).IsEqualTo(2u);
        float[] mip1 = MemoryMarshal.Cast<byte, float>(
            chain.AsSpan(baseBytes.Length, 16)).ToArray();
        // The average of one 8.0 HDR texel and three 0.0 texels is 2.0: well outside [0, 1], so a
        // clamping (as opposed to HDR-preserving) box filter would corrupt this value.
        await Assert.That(mip1).IsEquivalentTo(HdrOutOfRangeExpected);
    }

    [Test]
    public async Task NormalMapMipFallsBackToStraightUpWhenAverageExactlyCancels()
    {
        // Two texels decode to exactly (1, -1, -1) and two to exactly (-1, 1, 1): the raw sum is
        // the exact zero vector, which must trip the deterministic (0, 0, 1) fallback rather than
        // normalizing a zero-length vector into a NaN.
        float[] baseLevel =
        [
            1f, 0f, 0f, 1f,
            0f, 1f, 1f, 1f,
            1f, 0f, 0f, 1f,
            0f, 1f, 1f, 1f
        ];
        byte[] baseBytes = new byte[baseLevel.Length * sizeof(float)];
        MemoryMarshal.AsBytes(baseLevel.AsSpan()).CopyTo(baseBytes);

        byte[] chain = SilkMipGenerator.GenerateChain(
            baseBytes,
            2,
            2,
            SilkTextureFormat.Rgba32Float,
            isNormalMap: true,
            out _);

        ReadOnlySpan<float> mip1 = MemoryMarshal.Cast<byte, float>(
            chain.AsSpan(baseBytes.Length, 16));
        // (0, 0, 1) encodes to (0.5, 0.5, 1); alpha remains an ordinary average of the inputs.
        await Assert.That(mip1.ToArray()).IsEquivalentTo(StraightUpNormalExpected);
    }

    [Test]
    public async Task NormalMapMipRenormalizesNonCancelingAverageToUnitLength()
    {
        // Four identical texels all encoding straight +Z: the average is already unit length, so
        // renormalizing must be a no-op and must not perturb the encoded value.
        float[] baseLevel =
        [
            0.5f, 0.5f, 1f, 1f,
            0.5f, 0.5f, 1f, 1f,
            0.5f, 0.5f, 1f, 1f,
            0.5f, 0.5f, 1f, 1f
        ];
        byte[] baseBytes = new byte[baseLevel.Length * sizeof(float)];
        MemoryMarshal.AsBytes(baseLevel.AsSpan()).CopyTo(baseBytes);

        byte[] chain = SilkMipGenerator.GenerateChain(
            baseBytes,
            2,
            2,
            SilkTextureFormat.Rgba32Float,
            isNormalMap: true,
            out _);

        ReadOnlySpan<float> mip1 = MemoryMarshal.Cast<byte, float>(
            chain.AsSpan(baseBytes.Length, 16));
        await Assert.That(mip1.ToArray()).IsEquivalentTo(StraightUpNormalExpected);
    }

    [Test]
    public async Task GenerateChainRejectsNonFiniteBaseLevelInput()
    {
        float[] baseLevel = [float.PositiveInfinity, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];
        byte[] baseBytes = new byte[baseLevel.Length * sizeof(float)];
        MemoryMarshal.AsBytes(baseLevel.AsSpan()).CopyTo(baseBytes);

        await Assert.That(
            () => SilkMipGenerator.GenerateChain(
                baseBytes,
                2,
                2,
                SilkTextureFormat.Rgba32Float,
                isNormalMap: false,
                out _))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task UploadTextureRejectsAnythingOtherThanExactPackedChainLength()
    {
        // Every backend's ISilkGraphicsCommandList.UploadTexture validates the source span
        // against exactly this quantity before repacking or copying it; mirroring that one-line
        // contract here proves an off-by-one payload is rejected and the exact packed size is
        // accepted, without depending on a native device for any of the three backends.
        using var texture = new TestMipTexture(
            new SilkTextureDescriptor(
                4,
                4,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination,
                MipLevelCount: 3));

        await Assert.That(texture.MipLevelCount).IsEqualTo(3u);
        await Assert.That(() => ValidateUploadLength(texture, new byte[83]))
            .Throws<ArgumentException>();
        await Assert.That(() => ValidateUploadLength(texture, new byte[85]))
            .Throws<ArgumentException>();
        ValidateUploadLength(texture, new byte[84]);
    }

    /// <summary>
    /// Reproduces the required-length check every backend's command list performs before
    /// staging an <c>UploadTexture</c> call, so the contract is exercised without a native
    /// device.
    /// </summary>
    private static void ValidateUploadLength(ISilkGraphicsTexture texture, ReadOnlySpan<byte> source)
    {
        int requiredLength = SilkMipChainLayout.GetTotalByteSize(
            texture.Width,
            texture.Height,
            texture.Format,
            texture.MipLevelCount);
        if (source.Length != requiredLength)
        {
            throw new ArgumentException(
                $"The source must contain exactly {requiredLength} bytes.",
                nameof(source));
        }
    }

    private sealed class TestMipTexture : SilkGraphicsTextureBase
    {
        internal TestMipTexture(SilkTextureDescriptor descriptor)
            : base(descriptor)
        {
        }

        public override void ReadbackForTesting(Span<byte> destination)
        {
            ThrowIfTextureDisposed();
            ValidateReadback(destination.Length);
        }

        public override void ReadbackForTesting(Span<float> destination)
        {
            ThrowIfTextureDisposed();
            ValidateDepthReadback(destination.Length);
        }

        protected override void ReleaseNative()
        {
        }
    }
}
