// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd;

/// <summary>
/// Pure, native-independent validation for absolute USD prim paths, used to fail fast with a
/// clear managed exception before crossing the native boundary.
/// </summary>
public static class UsdPath
{
    /// <summary>
    /// Returns whether <paramref name="path"/> is a well-formed absolute prim path, such as
    /// <c>/World/Sensor</c>. The pseudo-root path <c>/</c> is not a prim path.
    /// </summary>
    public static bool IsAbsolutePrimPath(string? path) =>
        OpenUsdIdentifierValidation.IsAbsolutePrimPath(path);

    /// <summary>Throws an <see cref="ArgumentException"/> unless <paramref name="path"/> is valid.</summary>
    public static void ValidateAbsolutePrimPath(string? path, string paramName = "path")
    {
        if (!IsAbsolutePrimPath(path))
        {
            throw new ArgumentException($"'{path}' is not a valid absolute prim path.", paramName);
        }
    }
}
