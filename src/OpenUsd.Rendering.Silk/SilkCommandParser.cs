// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Creates zero-allocation enumerators over command-page bytes.
/// </summary>
public static class SilkCommandParser
{
    /// <summary>Gets the only command-page ABI understood by this parser.</summary>
    public const uint PageAbiVersion = 12;

    /// <summary>Creates an enumerator for a validated native page or test buffer.</summary>
    public static SilkCommandEnumerator Enumerate(ReadOnlySpan<byte> data, uint commandCount) =>
        new(data, commandCount);

    /// <summary>Creates an enumerator after validating the page ABI.</summary>
    public static SilkCommandEnumerator Enumerate(
        ReadOnlySpan<byte> data,
        uint commandCount,
        uint abiVersion)
    {
        ValidatePageAbi(abiVersion);
        return new SilkCommandEnumerator(data, commandCount);
    }

    internal static void ValidatePageAbi(uint abiVersion)
    {
        if (abiVersion != PageAbiVersion)
        {
            throw new InvalidDataException(
                $"Unsupported hdSilk page ABI {abiVersion}; expected {PageAbiVersion}.");
        }
    }
}
