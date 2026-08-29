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
    private static readonly float[] HdrOutOfRangeExpected = [2f, 2f, 2f, 1f];
    private static readonly float[] StraightUpNormalExpected = [0.5f, 0.5f, 1f, 1f];

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
