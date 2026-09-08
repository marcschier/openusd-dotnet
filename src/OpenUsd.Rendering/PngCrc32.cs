// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering;

internal static class PngCrc32
{
    internal static uint Calculate(
        ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, CancellationToken cancellationToken = default)
    {
        uint crc = uint.MaxValue;
        crc = Update(crc, first);
        while (!second.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int length = Math.Min(second.Length, 16 * 1024);
            crc = Update(crc, second[..length]);
            second = second[length..];
        }
        return ~crc;
    }

    private static uint Update(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                uint mask = (uint)-(int)(crc & 1);
                crc = (crc >> 1) ^ (0xEDB88320U & mask);
            }
        }
        return crc;
    }
}
