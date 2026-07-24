// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;

namespace OpenUsd.Interop;

internal static class NativeStringValidation
{
    internal static void ThrowIfContainsNull(string value, string? parameterName = null)
    {
        if (value.AsSpan().Contains('\0'))
        {
            throw new ArgumentException(
                "Native UTF-8 strings must not contain embedded null characters.",
                parameterName);
        }
    }

    internal static void ThrowIfInvalidOptionalAbsolutePrimPath(
        string? value,
        string parameterName)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }
        if (!IsValidAbsolutePrimPath(value))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid absolute prim path.",
                parameterName);
        }
    }

    internal static bool IsValidAbsolutePrimPath(string? value)
        => OpenUsdIdentifierValidation.IsAbsolutePrimPath(value);
}

[CustomMarshaller(
    typeof(string),
    MarshalMode.ManagedToUnmanagedIn,
    typeof(NativeUtf8StringMarshaller))]
internal static unsafe class NativeUtf8StringMarshaller
{
    public static byte* ConvertToUnmanaged(string? managed)
    {
        if (managed is null)
        {
            return null;
        }

        NativeStringValidation.ThrowIfContainsNull(managed);
        int byteCount = Encoding.UTF8.GetByteCount(managed);
        byte* result = (byte*)NativeMemory.Alloc((nuint)byteCount + 1);
        try
        {
            int written = Encoding.UTF8.GetBytes(managed, new Span<byte>(result, byteCount));
            result[written] = 0;
            return result;
        }
        catch
        {
            NativeMemory.Free(result);
            throw;
        }
    }

    public static void Free(byte* unmanaged) => NativeMemory.Free(unmanaged);
}
