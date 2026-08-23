// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics.Interop;

/// <summary>
/// Mirrors the UTF-8 rule the native page validator enforces.
/// </summary>
/// <remarks>
/// The native rule is stricter than <see cref="System.Text.Unicode.Utf8.IsValid"/>: an embedded null
/// byte is rejected because the string section is addressed by offset and length and must never be
/// mistaken for a C string. Overlong encodings, surrogate code points, and code points above
/// <c>U+10FFFF</c> are rejected exactly as the native validator rejects them.
/// </remarks>
internal static class PhysxUtf8
{
    /// <summary>Determines whether a byte span is UTF-8 without embedded null bytes.</summary>
    internal static bool IsValid(ReadOnlySpan<byte> data)
    {
        int index = 0;
        while (index < data.Length)
        {
            byte lead = data[index];
            int continuationCount;
            uint codePoint;
            if (lead < 0x80)
            {
                if (lead == 0)
                {
                    return false;
                }
                index++;
                continue;
            }

            if ((lead & 0xE0) == 0xC0)
            {
                continuationCount = 1;
                codePoint = (uint)(lead & 0x1F);
            }
            else if ((lead & 0xF0) == 0xE0)
            {
                continuationCount = 2;
                codePoint = (uint)(lead & 0x0F);
            }
            else if ((lead & 0xF8) == 0xF0)
            {
                continuationCount = 3;
                codePoint = (uint)(lead & 0x07);
            }
            else
            {
                return false;
            }

            if (continuationCount > data.Length - index - 1)
            {
                return false;
            }

            for (int offset = 1; offset <= continuationCount; offset++)
            {
                byte continuation = data[index + offset];
                if ((continuation & 0xC0) != 0x80)
                {
                    return false;
                }
                codePoint = (codePoint << 6) | (uint)(continuation & 0x3F);
            }

            if ((continuationCount == 1 && codePoint < 0x80) ||
                (continuationCount == 2 && codePoint < 0x800) ||
                (continuationCount == 3 && codePoint < 0x10000) ||
                codePoint > 0x10FFFF ||
                codePoint is >= 0xD800 and <= 0xDFFF)
            {
                return false;
            }

            index += continuationCount + 1;
        }

        return true;
    }
}
