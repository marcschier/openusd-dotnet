// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.ObjectModel;

namespace OpenUsd.Rendering.Silk;

internal readonly record struct SilkVolumeAtlasInput(
    SilkVolumeTextureExtent Extent,
    ReadOnlyMemory<byte> Pixels);

internal readonly record struct SilkVolumeAtlasRegion(
    uint X,
    uint Y,
    uint Z,
    SilkVolumeTextureExtent Extent);

internal readonly record struct SilkVolumeAtlasLayout(
    uint Width,
    uint Height,
    uint Depth,
    int ByteCount);

internal sealed class SilkVolumeAtlas
{
    internal const int MaximumFieldCount = 8;

    private const uint GutterWidth = 1;
    private const uint MaximumAtlasAxis = 2048;
    private const uint BytesPerVoxel = sizeof(float);
    private readonly byte[] _pixels;

    private SilkVolumeAtlas(
        uint width,
        uint height,
        uint depth,
        byte[] pixels,
        IReadOnlyList<SilkVolumeAtlasRegion> regions)
    {
        Width = width;
        Height = height;
        Depth = depth;
        _pixels = pixels;
        Regions = regions;
    }

    internal uint Width { get; }
    internal uint Height { get; }
    internal uint Depth { get; }
    internal ReadOnlyMemory<byte> Pixels => _pixels;
    internal IReadOnlyList<SilkVolumeAtlasRegion> Regions { get; }

    internal static SilkVolumeAtlasLayout Plan(IReadOnlyList<SilkVolumeAtlasInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count is < 1 or > MaximumFieldCount)
        {
            throw new InvalidDataException(
                $"A volume atlas requires between 1 and {MaximumFieldCount} fields; received {inputs.Count}.");
        }

        uint width = 0;
        uint height = 0;
        uint maximumDepth = 0;
        ulong depth = 0;
        for (int i = 0; i < inputs.Count; i++)
        {
            SilkVolumeTextureExtent extent = inputs[i].Extent;
            ValidateExtent(extent, i);
            width = Math.Max(width, checked(extent.Width + (2 * GutterWidth)));
            height = Math.Max(height, checked(extent.Height + (2 * GutterWidth)));
            maximumDepth = Math.Max(maximumDepth, checked(extent.Depth + (2 * GutterWidth)));
            depth = checked(depth + extent.Depth + (2 * GutterWidth));
        }

        if (depth > MaximumAtlasAxis)
        {
            uint cellWidth = width;
            uint cellHeight = height;
            ulong bestVolume = ulong.MaxValue;
            uint bestLongestAxis = uint.MaxValue;
            for (uint columns = 1; columns <= inputs.Count; columns++)
            {
                for (uint rows = 1; rows <= inputs.Count; rows++)
                {
                    uint layers = checked(((uint)inputs.Count + columns * rows - 1) / (columns * rows));
                    uint candidateWidth = checked(cellWidth * columns);
                    uint candidateHeight = checked(cellHeight * rows);
                    uint candidateDepth = checked(maximumDepth * layers);
                    if (candidateWidth > MaximumAtlasAxis || candidateHeight > MaximumAtlasAxis ||
                        candidateDepth > MaximumAtlasAxis)
                    {
                        continue;
                    }
                    ulong volume = checked((ulong)candidateWidth * candidateHeight * candidateDepth);
                    uint longestAxis = Math.Max(candidateWidth, Math.Max(candidateHeight, candidateDepth));
                    if (volume < bestVolume || (volume == bestVolume && longestAxis < bestLongestAxis))
                    {
                        bestVolume = volume;
                        bestLongestAxis = longestAxis;
                        width = candidateWidth;
                        height = candidateHeight;
                        depth = candidateDepth;
                    }
                }
            }
            if (bestVolume == ulong.MaxValue)
            {
                throw new InvalidDataException(
                    $"The volume atlas exceeds the portable 3D texture axis limit of {MaximumAtlasAxis} voxels.");
            }
        }

        ulong byteCount = checked((ulong)width * height * depth * BytesPerVoxel);
        if (byteCount > int.MaxValue)
        {
            throw new InvalidDataException(
                $"The volume atlas requires {byteCount} bytes, exceeding the managed array limit of {int.MaxValue}.");
        }

        return new SilkVolumeAtlasLayout(width, height, checked((uint)depth), checked((int)byteCount));
    }

    internal static SilkVolumeAtlas Pack(IReadOnlyList<SilkVolumeAtlasInput> inputs)
    {
        SilkVolumeAtlasLayout layout = Plan(inputs);
        var regions = new SilkVolumeAtlasRegion[inputs.Count];
        uint cellWidth = 0;
        uint cellHeight = 0;
        uint cellDepth = 0;
        ulong stackedDepth = 0;
        foreach (SilkVolumeAtlasInput input in inputs)
        {
            cellWidth = Math.Max(cellWidth, input.Extent.Width + 2 * GutterWidth);
            cellHeight = Math.Max(cellHeight, input.Extent.Height + 2 * GutterWidth);
            cellDepth = Math.Max(cellDepth, input.Extent.Depth + 2 * GutterWidth);
            stackedDepth += input.Extent.Depth + 2 * GutterWidth;
        }
        bool stacked = stackedDepth <= MaximumAtlasAxis;
        uint columns = layout.Width / cellWidth;
        uint rows = layout.Height / cellHeight;
        uint zStart = 0;
        for (int i = 0; i < inputs.Count; i++)
        {
            SilkVolumeAtlasInput input = inputs[i];
            ulong expectedByteCount =
                checked((ulong)input.Extent.Width * input.Extent.Height * input.Extent.Depth * BytesPerVoxel);
            if ((ulong)input.Pixels.Length != expectedByteCount)
            {
                throw new InvalidDataException(
                    $"Volume field {i} ({input.Extent.Width}x{input.Extent.Height}x{input.Extent.Depth}) " +
                    $"requires exactly {expectedByteCount} R32Float bytes; received {input.Pixels.Length}.");
            }

            regions[i] = new SilkVolumeAtlasRegion(
                checked((stacked ? 0 : (uint)i % columns * cellWidth) + GutterWidth),
                checked((stacked ? 0 : (uint)i / columns % rows * cellHeight) + GutterWidth),
                checked((stacked ? zStart : (uint)i / (columns * rows) * cellDepth) + GutterWidth),
                input.Extent);
            zStart = checked(zStart + input.Extent.Depth + (2 * GutterWidth));
        }

        var pixels = new byte[layout.ByteCount];
        for (int i = 0; i < inputs.Count; i++)
        {
            SilkVolumeAtlasInput input = inputs[i];
            SilkVolumeAtlasRegion region = regions[i];
            ReadOnlySpan<byte> source = input.Pixels.Span;
            SilkVolumeTextureExtent extent = input.Extent;
            uint paddedWidth = checked(extent.Width + (2 * GutterWidth));
            uint paddedHeight = checked(extent.Height + (2 * GutterWidth));
            uint paddedDepth = checked(extent.Depth + (2 * GutterWidth));

            for (uint z = 0; z < paddedDepth; z++)
            {
                uint sourceZ = ClampToInterior(z, extent.Depth);
                for (uint y = 0; y < paddedHeight; y++)
                {
                    uint sourceY = ClampToInterior(y, extent.Height);
                    for (uint x = 0; x < paddedWidth; x++)
                    {
                        uint sourceX = ClampToInterior(x, extent.Width);
                        int sourceOffset = checked((int)(
                            (((ulong)sourceZ * extent.Height + sourceY) * extent.Width + sourceX) *
                            BytesPerVoxel));
                        int atlasOffset = checked((int)(
                            ((((ulong)region.Z - GutterWidth + z) * layout.Height +
                                region.Y - GutterWidth + y) * layout.Width + region.X - GutterWidth + x) *
                            BytesPerVoxel));
                        source.Slice(sourceOffset, checked((int)BytesPerVoxel))
                            .CopyTo(pixels.AsSpan(atlasOffset, checked((int)BytesPerVoxel)));
                    }
                }
            }
        }

        return new SilkVolumeAtlas(
            layout.Width,
            layout.Height,
            layout.Depth,
            pixels,
            new ReadOnlyCollection<SilkVolumeAtlasRegion>(regions));
    }

    private static void ValidateExtent(SilkVolumeTextureExtent extent, int fieldIndex)
    {
        if (extent.Width is 0 or > SilkVolumeTextureExtent.MaximumAxisCount ||
            extent.Height is 0 or > SilkVolumeTextureExtent.MaximumAxisCount ||
            extent.Depth is 0 or > SilkVolumeTextureExtent.MaximumAxisCount)
        {
            throw new InvalidDataException(
                $"Volume field {fieldIndex} dimensions must each be between 1 and " +
                $"{SilkVolumeTextureExtent.MaximumAxisCount}; received " +
                $"{extent.Width}x{extent.Height}x{extent.Depth}.");
        }
    }

    private static uint ClampToInterior(uint paddedCoordinate, uint extent) =>
        paddedCoordinate == 0 ? 0 : Math.Min(paddedCoordinate - 1, extent - 1);
}
