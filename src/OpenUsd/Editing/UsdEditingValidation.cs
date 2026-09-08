// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using OpenUsd.Interop;

namespace OpenUsd.Editing;

internal static class UsdEditingValidation
{
    internal const int MaximumBytes = 4 * 1024 * 1024;
    internal const int MaximumTextBytes = 4096;
    internal const int MaximumAddresses = 256;
    internal const int MaximumItems = 4096;
    internal const int MaximumDepth = 16;
    internal static readonly UTF8Encoding Utf8 = new(false, true);

    internal static int Text(string value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
        if (value.Length > MaximumTextBytes || value.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("Editing text must be NUL-free and at most 4096 UTF-8 bytes.", paramName);
        }
        int size;
        try
        {
            size = Utf8.GetByteCount(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("Editing text must contain valid Unicode.", paramName, exception);
        }
        if (size > MaximumTextBytes)
        {
            throw new ArgumentException("Editing text exceeds 4096 UTF-8 bytes.", paramName);
        }
        return size;
    }

    internal static void PropertyPath(string path, string paramName)
    {
        _ = Text(path, paramName);
        int dot = path.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 1 || !UsdPath.IsAbsolutePrimPath(path[..dot]) || dot == path.Length - 1)
        {
            throw new ArgumentException("An absolute, non-variant property path is required.", paramName);
        }
        foreach (string part in path[(dot + 1)..].Split(':'))
        {
            if (!UsdPath.IsAbsolutePrimPath("/" + part) || part.Contains('/', StringComparison.Ordinal))
            {
                throw new ArgumentException("The property name must be a namespaced USD identifier.", paramName);
            }
        }
        PathDepth(path, paramName, property: true);
    }

    internal static void PrimPath(string path, string paramName)
    {
        _ = Text(path, paramName);
        if (!UsdPath.IsAbsolutePrimPath(path))
        {
            throw new ArgumentException("An absolute, non-variant prim path is required.", paramName);
        }
        PathDepth(path, paramName, property: false);
    }

    internal static void MutationPath(string path, bool connection)
    {
        if (connection || path.Contains('.', StringComparison.Ordinal))
        {
            PropertyPath(path, nameof(path));
        }
        else
        {
            PrimPath(path, nameof(path));
        }
    }

    internal static void ItemCount(int count)
    {
        if (count is < 0 or > MaximumItems)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Editing collections are limited to 4096 items.");
        }
    }

    internal static OpenUsdNativeException InvalidPacket(string detail) =>
        new(OpenUsdNativeStatus.NativeError, $"Invalid authored-layer editing packet: {detail}.");

    private static void PathDepth(string path, string paramName, bool property)
    {
        int elements = property ? 1 : 0;
        foreach (char character in path)
        {
            if (character == '/')
            {
                elements++;
            }
        }
        if (elements > 32)
        {
            throw new ArgumentException("Editing paths are limited to 32 elements.", paramName);
        }
    }
}
