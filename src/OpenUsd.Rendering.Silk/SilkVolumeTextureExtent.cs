// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd.Rendering.Silk;

internal readonly record struct SilkVolumeTextureExtent(uint Width, uint Height, uint Depth)
{
    internal const uint MaximumExactLayerCount = 512;

    internal static SilkVolumeTextureExtent Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string[] parts = value.Split(',');
        if (parts.Length != 3 ||
            !uint.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out uint width) ||
            !uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out uint height) ||
            !uint.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out uint depth) ||
            width == 0 ||
            height == 0 ||
            depth == 0)
        {
            throw new InvalidDataException($"Invalid volume texture dimensions '{value}'.");
        }
        if (depth > MaximumExactLayerCount)
        {
            throw new InvalidDataException(
                $"Volume texture depth {depth} exceeds the exact integration limit " +
                $"{MaximumExactLayerCount}.");
        }
        return new SilkVolumeTextureExtent(width, height, depth);
    }
}
