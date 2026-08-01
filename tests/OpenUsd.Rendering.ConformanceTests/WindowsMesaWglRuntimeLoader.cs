// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace OpenUsd.Rendering.ConformanceTests;

internal static class WindowsMesaWglRuntimeLoader
{
    private static nint _openGl32;

    [ModuleInitializer]
    internal static void Initialize()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string? path = Environment.GetEnvironmentVariable("OPENUSD_MESA_WGL_OPENGL32_PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string fullPath = Path.GetFullPath(path);
        string? expectedSha256 = Environment.GetEnvironmentVariable("OPENUSD_MESA_WGL_OPENGL32_SHA256");
        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            string actualSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullPath)));
            if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Mesa WGL opengl32.dll hash mismatch for '{fullPath}'. " +
                    $"Expected {expectedSha256}, got {actualSha256}.");
            }
        }

        _openGl32 = NativeLibrary.Load(fullPath);
    }

    [SupportedOSPlatform("windows")]
    internal static void EnsureLoaded()
    {
        _ = _openGl32;
    }
}
