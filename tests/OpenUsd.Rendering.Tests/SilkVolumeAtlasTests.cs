// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkVolumeAtlasTests
{
    [Test]
    public async Task Pack_TwoNonuniformFields_UsesDeterministicGutteredZStackAndPreservesEverySample()
    {
        SilkVolumeTextureExtent firstExtent = new(2, 1, 2);
        SilkVolumeTextureExtent secondExtent = new(1, 2, 1);
        byte[] firstPixels = Encode(1, 2, 3, 4);
        byte[] secondPixels = Encode(101, 102);
        SilkVolumeAtlasInput[] inputs =
        [
            new(firstExtent, firstPixels),
            new(secondExtent, secondPixels)
        ];

        SilkVolumeAtlas atlas = SilkVolumeAtlas.Pack(inputs);

        await Assert.That(atlas.Width).IsEqualTo(4u);
        await Assert.That(atlas.Height).IsEqualTo(4u);
        await Assert.That(atlas.Depth).IsEqualTo(7u);
        await Assert.That(atlas.Regions).IsEquivalentTo(new[]
        {
            new SilkVolumeAtlasRegion(1, 1, 1, firstExtent),
            new SilkVolumeAtlasRegion(1, 1, 5, secondExtent)
        });
        AssertPaddedField(atlas, firstExtent, firstPixels, 0);
        AssertPaddedField(atlas, secondExtent, secondPixels, 4);
    }

    [Test]
    public async Task Pack_EnforcesFieldCountBounds()
    {
        await Assert.That(() => SilkVolumeAtlas.Pack(Array.Empty<SilkVolumeAtlasInput>()))
            .Throws<InvalidDataException>()
            .WithMessageContaining("between 1 and 8");

        SilkVolumeAtlasInput[] maximumInputs = Enumerable
            .Repeat(new SilkVolumeAtlasInput(new SilkVolumeTextureExtent(1, 1, 1), Encode(1)), 8)
            .ToArray();
        SilkVolumeAtlas atlas = SilkVolumeAtlas.Pack(maximumInputs);
        await Assert.That(atlas.Regions.Count).IsEqualTo(SilkVolumeAtlas.MaximumFieldCount);

        SilkVolumeAtlasInput[] overLimitInputs = Enumerable
            .Repeat(new SilkVolumeAtlasInput(new SilkVolumeTextureExtent(1, 1, 1), Encode(1)), 9)
            .ToArray();
        await Assert.That(() => SilkVolumeAtlas.Pack(overLimitInputs))
            .Throws<InvalidDataException>()
            .WithMessageContaining("received 9");
    }

    [Test]
    [Arguments(0u, 1u, 1u)]
    [Arguments(1u, 0u, 1u)]
    [Arguments(1u, 1u, 0u)]
    [Arguments(513u, 1u, 1u)]
    [Arguments(1u, 513u, 1u)]
    [Arguments(1u, 1u, 513u)]
    public async Task Plan_RejectsDimensionsOutsideThePerAxisLimits(uint width, uint height, uint depth)
    {
        SilkVolumeAtlasInput[] input =
        [
            new(new SilkVolumeTextureExtent(width, height, depth), ReadOnlyMemory<byte>.Empty)
        ];

        await Assert.That(() => SilkVolumeAtlas.Plan(input))
            .Throws<InvalidDataException>()
            .WithMessageContaining("dimensions must each be between 1 and 512");
    }

    [Test]
    public async Task Plan_Accepts512VoxelFieldAxesAndComputesReservationSizeWithoutAllocatingAtlas()
    {
        SilkVolumeAtlasInput[] inputs =
        [
            new(new SilkVolumeTextureExtent(512, 512, 512), ReadOnlyMemory<byte>.Empty)
        ];

        SilkVolumeAtlasLayout layout = SilkVolumeAtlas.Plan(inputs);

        await Assert.That(layout.Width).IsEqualTo(514u);
        await Assert.That(layout.Height).IsEqualTo(514u);
        await Assert.That(layout.Depth).IsEqualTo(514u);
        await Assert.That(layout.ByteCount).IsEqualTo(514 * 514 * 514 * sizeof(float));
    }

    [Test]
    [Arguments(3)]
    [Arguments(5)]
    public async Task Pack_RejectsInputByteLengthsThatDoNotExactlyMatchR32FloatExtent(int length)
    {
        SilkVolumeAtlasInput[] inputs =
        [
            new(new SilkVolumeTextureExtent(1, 1, 1), new byte[length])
        ];

        await Assert.That(() => SilkVolumeAtlas.Pack(inputs))
            .Throws<InvalidDataException>()
            .WithMessageContaining("requires exactly 4 R32Float bytes");
    }

    [Test]
    public async Task Plan_RejectsTheFirstFourFieldLayoutThatExceedsManagedArrayByteLimit()
    {
        SilkVolumeAtlasInput[] fits =
        [
            EmptyInput(512, 512, 512),
            EmptyInput(512, 512, 512),
            EmptyInput(512, 512, 512),
            EmptyInput(512, 512, 488)
        ];
        SilkVolumeAtlasInput[] exceeds =
        [
            EmptyInput(512, 512, 512),
            EmptyInput(512, 512, 512),
            EmptyInput(512, 512, 512),
            EmptyInput(512, 512, 489)
        ];

        SilkVolumeAtlasLayout layout = SilkVolumeAtlas.Plan(fits);
        await Assert.That(layout.Depth).IsEqualTo(2032u);
        await Assert.That(layout.ByteCount).IsEqualTo(514 * 514 * 2032 * sizeof(float));
        await Assert.That(layout.ByteCount).IsLessThanOrEqualTo(int.MaxValue);
        await Assert.That(() => SilkVolumeAtlas.Plan(exceeds))
            .Throws<InvalidDataException>()
            .WithMessageContaining("managed array limit");
    }

    [Test]
    [Arguments(4)]
    [Arguments(8)]
    public async Task Pack_DeepFieldsUsePortableMultiaxisPlacementWithCompleteGutters(int count)
    {
        SilkVolumeAtlasInput[] fits =
        [
            EmptyInput(1, 1, 512),
            EmptyInput(1, 1, 512),
            EmptyInput(1, 1, 512),
            EmptyInput(1, 1, 504)
        ];
        SilkVolumeAtlasLayout layout = SilkVolumeAtlas.Plan(fits);
        await Assert.That(layout.Width).IsEqualTo(3u);
        await Assert.That(layout.Height).IsEqualTo(3u);
        await Assert.That(layout.Depth).IsEqualTo(2048u);
        await Assert.That(layout.ByteCount).IsEqualTo(3 * 3 * 2048 * sizeof(float));

        SilkVolumeAtlasInput[] inputs = Enumerable.Range(1, count)
            .Select(index => new SilkVolumeAtlasInput(
                new SilkVolumeTextureExtent(1, 1, 512),
                Encode(Enumerable.Range(0, 512).Select(z => index * 1000f + z).ToArray())))
            .ToArray();
        SilkVolumeAtlas atlas = SilkVolumeAtlas.Pack(inputs);
        await Assert.That(atlas.Width).IsLessThanOrEqualTo(2048u);
        await Assert.That(atlas.Height).IsLessThanOrEqualTo(2048u);
        await Assert.That(atlas.Depth).IsLessThanOrEqualTo(2048u);
        for (int i = 0; i < inputs.Length; i++)
        {
            SilkVolumeAtlasRegion region = atlas.Regions[i];
            AssertPaddedField(atlas, inputs[i].Extent, inputs[i].Pixels.ToArray(),
                region.Z - 1, region.X - 1, region.Y - 1);
        }
    }

    [Test]
    public async Task Pack_IsDeterministicAndDoesNotMutateOrAliasInputPixelBuffers()
    {
        byte[] inputPixels = Encode(1.25f, 4.5f);
        byte[] originalPixels = (byte[])inputPixels.Clone();
        SilkVolumeAtlasInput[] inputs =
        [
            new(new SilkVolumeTextureExtent(2, 1, 1), inputPixels)
        ];

        SilkVolumeAtlas first = SilkVolumeAtlas.Pack(inputs);
        SilkVolumeAtlas second = SilkVolumeAtlas.Pack(inputs);

        await Assert.That(inputPixels.AsSpan().SequenceEqual(originalPixels)).IsTrue();
        await Assert.That(first.Pixels.Span.SequenceEqual(second.Pixels.Span)).IsTrue();
        await Assert.That(first.Regions.SequenceEqual(second.Regions)).IsTrue();
        byte[] packedBeforeInputMutation = Pixel(first, 1, 1, 1);
        inputPixels[4] ^= 0xff;
        await Assert.That(Pixel(first, 2, 1, 1)).IsEquivalentTo(Encode(4.5f));
        await Assert.That(Pixel(first, 1, 1, 1)).IsEquivalentTo(packedBeforeInputMutation);
    }

    private static SilkVolumeAtlasInput EmptyInput(uint width, uint height, uint depth) =>
        new(new SilkVolumeTextureExtent(width, height, depth), ReadOnlyMemory<byte>.Empty);

    private static byte[] Encode(params float[] values)
    {
        byte[] bytes = new byte[values.Length * sizeof(float)];
        for (int i = 0; i < values.Length; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * sizeof(float), sizeof(float)), values[i]);
        }
        return bytes;
    }

    private static byte[] Pixel(SilkVolumeAtlas atlas, uint x, uint y, uint z)
    {
        int offset = checked((int)(((ulong)z * atlas.Height + y) * atlas.Width + x) * sizeof(float));
        return atlas.Pixels.Span.Slice(offset, sizeof(float)).ToArray();
    }

    private static void AssertPaddedField(
        SilkVolumeAtlas atlas,
        SilkVolumeTextureExtent extent,
        byte[] source,
        uint zStart,
        uint xStart = 0,
        uint yStart = 0)
    {
        for (uint z = 0; z < extent.Depth + 2; z++)
        {
            uint sourceZ = Math.Min(z == 0 ? 0 : z - 1, extent.Depth - 1);
            for (uint y = 0; y < extent.Height + 2; y++)
            {
                uint sourceY = Math.Min(y == 0 ? 0 : y - 1, extent.Height - 1);
                for (uint x = 0; x < extent.Width + 2; x++)
                {
                    uint sourceX = Math.Min(x == 0 ? 0 : x - 1, extent.Width - 1);
                    int sourceOffset = checked((int)(
                        (((ulong)sourceZ * extent.Height + sourceY) * extent.Width + sourceX) *
                        sizeof(float)));
                    byte[] expected = source.AsSpan(sourceOffset, sizeof(float)).ToArray();
                    byte[] actual = Pixel(atlas, xStart + x, yStart + y, zStart + z);
                    if (!actual.AsSpan().SequenceEqual(expected))
                    {
                        throw new InvalidOperationException(
                            $"Unexpected padded sample at atlas coordinate ({x},{y},{zStart + z}).");
                    }
                }
            }
        }
    }
}
