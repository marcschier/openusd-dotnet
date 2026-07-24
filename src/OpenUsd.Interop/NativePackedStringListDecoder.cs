// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;

namespace OpenUsd.Interop;

internal static class NativePackedStringListDecoder
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal static string[] Decode(
        ReadOnlySpan<byte> data,
        ReadOnlySpan<nuint> offsets,
        string description)
    {
        if (offsets.IsEmpty)
        {
            if (!data.IsEmpty)
            {
                throw InvalidBuffer(description, "an empty offset table has trailing data");
            }
            return [];
        }
        if (data.IsEmpty)
        {
            throw InvalidBuffer(description, "a non-empty offset table has no data");
        }

        var values = new string[offsets.Length];
        nuint expectedOffset = 0;
        for (int index = 0; index < offsets.Length; index++)
        {
            nuint offset = offsets[index];
            if (offset != expectedOffset || offset > (nuint)int.MaxValue)
            {
                throw InvalidBuffer(description, "the offset table is not canonical and contiguous");
            }

            ReadOnlySpan<byte> remaining = data[(int)offset..];
            int terminator = remaining.IndexOf((byte)0);
            if (terminator < 0)
            {
                throw InvalidBuffer(description, "an entry is not NUL-terminated");
            }

            try
            {
                values[index] = StrictUtf8.GetString(remaining[..terminator]);
            }
            catch (DecoderFallbackException)
            {
                throw InvalidBuffer(description, "an entry is not valid UTF-8");
            }

            expectedOffset = checked(offset + (nuint)terminator + 1);
        }

        if (expectedOffset != (nuint)data.Length)
        {
            throw InvalidBuffer(description, "the packed data has trailing or embedded bytes");
        }
        return values;
    }

    private static OpenUsdNativeException InvalidBuffer(string description, string detail) =>
        new(
            OpenUsdNativeStatus.NativeError,
            $"The native runtime returned an invalid {description}: {detail}.");
}
