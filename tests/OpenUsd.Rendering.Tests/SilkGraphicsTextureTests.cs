// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkGraphicsTextureTests
{
    [Test]
    public async Task ReadbackRequiresTightlyPackedRgba8Destination()
    {
        using var texture = new TestTexture(2, 3);
        byte[] destination = new byte[23];

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => texture.ReadbackForTesting(destination));

        await Assert.That(exception.ParamName).IsEqualTo("destinationLength");
    }

    [Test]
    public async Task ClearColorRejectsNonNormalizedChannels()
    {
        var color = new SilkColor(0, 0, 2, 1);

        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(color.Validate);

        await Assert.That(exception.ParamName).IsEqualTo("Blue");
    }

    [Test]
    public async Task SubmissionLeaseDefersNativeReleaseAfterDispose()
    {
        using var texture = new TestTexture(2, 3);
        IDisposable lease = texture.AcquireLeaseForTesting();

        texture.Dispose();

        await Assert.That(texture.ReleaseCount).IsEqualTo(0);
        await Assert.That(
            () => texture.ReadbackForTesting(new byte[24]))
            .Throws<ObjectDisposedException>();

        lease.Dispose();

        await Assert.That(texture.ReleaseCount).IsEqualTo(1);
    }

    [Test]
    public async Task DepthDescriptorRequiresDepthUsage()
    {
        var descriptor = new SilkTextureDescriptor(
            2,
            3,
            SilkTextureFormat.D32Float,
            SilkTextureUsage.ColorRenderTarget);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => _ = new TestTexture(descriptor));

        await Assert.That(exception.Message).Contains("DepthRenderTarget");
    }

    [Test]
    public async Task DepthReadbackUsesOneFloatPerPixel()
    {
        using var texture = new TestTexture(
            SilkTextureDescriptor.DepthTarget(2, 3));
        float[] destination = new float[6];

        texture.ReadbackForTesting(destination);

        await Assert.That(texture.Format).IsEqualTo(SilkTextureFormat.D32Float);
        await Assert.That(texture.Usage).IsEqualTo(SilkTextureUsage.DepthRenderTarget);
        await Assert.That(
            () => texture.ReadbackForTesting(new byte[24]))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task FloatingPointColorReadbackUsesFormatComponentWidth()
    {
        using var rgba16 = new TestTexture(
            SilkTextureDescriptor.HdrColorTarget(2, 3));
        using var rgba32 = new TestTexture(
            new SilkTextureDescriptor(
                2,
                3,
                SilkTextureFormat.Rgba32Float,
                SilkTextureUsage.ColorRenderTarget));

        rgba16.ReadbackForTesting(new byte[48]);
        rgba32.ReadbackForTesting(new float[24]);

        await Assert.That(
            () => rgba16.ReadbackForTesting(new float[24]))
            .Throws<InvalidOperationException>();
        await Assert.That(
            () => rgba32.ReadbackForTesting(new byte[48]))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task HdrDescriptorIsSampledAndReadable()
    {
        SilkTextureDescriptor descriptor =
            SilkTextureDescriptor.HdrColorTarget(2, 3);

        descriptor.Validate();

        await Assert.That(descriptor.Format)
            .IsEqualTo(SilkTextureFormat.Rgba16Float);
        await Assert.That(descriptor.Usage).IsEqualTo(
            SilkTextureUsage.ColorRenderTarget |
            SilkTextureUsage.Sampled |
            SilkTextureUsage.CopySource);
    }

    [Test]
    public async Task SampledDescriptorIncludesUploadAndReadbackUsage()
    {
        SilkTextureDescriptor descriptor = SilkTextureDescriptor.SampledRgba8(2, 3);

        descriptor.Validate();

        await Assert.That(descriptor.Format).IsEqualTo(SilkTextureFormat.Rgba8Unorm);
        await Assert.That(descriptor.Usage.HasFlag(SilkTextureUsage.Sampled)).IsTrue();
        await Assert.That(descriptor.Usage.HasFlag(SilkTextureUsage.CopySource)).IsTrue();
        await Assert.That(descriptor.Usage.HasFlag(SilkTextureUsage.CopyDestination)).IsTrue();
    }

    [Test]
    public async Task SelectionDescriptorsAreSampledRenderTargets()
    {
        SilkTextureDescriptor mask = SilkTextureDescriptor.SelectionMask(2, 3);
        SilkTextureDescriptor depth = SilkTextureDescriptor.SampledDepthTarget(2, 3);

        mask.Validate();
        depth.Validate();

        await Assert.That(mask.Usage).IsEqualTo(
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.Sampled);
        await Assert.That(depth.Usage).IsEqualTo(
            SilkTextureUsage.DepthRenderTarget | SilkTextureUsage.Sampled);
    }

    [Test]
    public async Task Rgba8DescriptorRejectsDepthUsage()
    {
        var descriptor = new SilkTextureDescriptor(
            2,
            3,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.DepthRenderTarget);

        ArgumentException exception = Assert.Throws<ArgumentException>(descriptor.Validate);

        await Assert.That(exception.ParamName).IsEqualTo("Usage");
    }

    [Test]
    public async Task SingleChannelFloatDescriptorRejectsColorTargetUsage()
    {
        var descriptor = new SilkTextureDescriptor(
            2,
            3,
            SilkTextureFormat.R32Float,
            SilkTextureUsage.ColorRenderTarget);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            descriptor.Validate);

        await Assert.That(exception.ParamName).IsEqualTo("Usage");
    }

    [Test]
    public async Task DisplayConversionAppliesExposureAndReinhardAfterHdrRendering()
    {
        Half[] linear =
        [
            (Half)64,
            (Half)32,
            (Half)0,
            (Half)0.5f
        ];
        byte[] source = new byte[linear.Length * sizeof(ushort)];
        MemoryMarshal.AsBytes(linear.AsSpan()).CopyTo(source);
        byte[] destination = new byte[4];

        SilkDisplayConverter.ConvertRgba16FloatToRgba8(
            source,
            destination,
            RenderOutputTransform.Reinhard,
            exposure: -6);
        SilkColor clear = SilkDisplayConverter.TransformColor(
            new SilkColor(64, 32, 0, 0.5f),
            RenderOutputTransform.Reinhard,
            exposure: -6);

        await Assert.That(destination).IsEquivalentTo(new byte[] { 128, 85, 0, 128 });
        await Assert.That(clear).IsEqualTo(new SilkColor(0.5f, 1f / 3f, 0, 0.5f));
    }

    [Test]
    public async Task DisplayConversionRejectsNonFiniteHdrChannels()
    {
        Half[] linear =
        [
            Half.PositiveInfinity,
            (Half)0,
            (Half)0,
            (Half)1
        ];
        byte[] source = new byte[linear.Length * sizeof(ushort)];
        MemoryMarshal.AsBytes(linear.AsSpan()).CopyTo(source);

        await Assert.That(
            () => SilkDisplayConverter.ConvertRgba16FloatToRgba8(
                source,
                new byte[4],
                RenderOutputTransform.Identity,
                exposure: 0))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task SamplerDescriptorValidatesEveryEnum()
    {
        SilkSamplerDescriptor.LinearClamp.Validate();
        SilkSamplerDescriptor.NearestClamp.Validate();
        SilkSamplerDescriptor.NearestRepeat.Validate();
        var invalid = SilkSamplerDescriptor.LinearClamp with
        {
            AddressW = (SilkSamplerAddressMode)42
        };

        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(invalid.Validate);

        await Assert.That(exception.ParamName).IsEqualTo("AddressW");
    }

    private sealed class TestTexture : SilkGraphicsTextureBase
    {
        internal TestTexture(uint width, uint height)
            : base(width, height, SilkTextureFormat.Rgba8Unorm)
        {
        }

        internal TestTexture(SilkTextureDescriptor descriptor)
            : base(descriptor)
        {
        }

        internal int ReleaseCount { get; private set; }

        internal IDisposable AcquireLeaseForTesting() => AcquireSubmissionLease();

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
            ReleaseCount++;
        }
    }
}
