// Copyright (c) marcschier. Licensed under the MIT License.

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OpenUsd.Rendering.ConformanceTests;

internal sealed partial class NativeTemporaryDirectoryScope : IDisposable
{
    private readonly string? _previous;
    private bool _disposed;

    internal NativeTemporaryDirectoryScope(string? value)
    {
        _previous = ReadCurrent();
        SetCurrent(value);
    }

    internal static string? ReadCurrent()
    {
        if (OperatingSystem.IsWindows())
        {
            return Environment.GetEnvironmentVariable("TMP");
        }

        EnsureUnixPlatform();
        nint value = GetEnv("TMPDIR");
        return value == 0 ? null : Marshal.PtrToStringUTF8(value);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            SetCurrent(_previous);
            _disposed = true;
        }
    }

    private static void SetCurrent(string? value)
    {
        if (OperatingSystem.IsWindows())
        {
            Environment.SetEnvironmentVariable("TMP", value);
            return;
        }

        EnsureUnixPlatform();
        int result = value is null ? UnsetEnv("TMPDIR") : SetEnv("TMPDIR", value, 1);
        if (result != 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to update native TMPDIR.");
        }
    }

    private static void EnsureUnixPlatform()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("Native temporary-directory overrides require Windows, Linux, or macOS.");
        }
    }

    [LibraryImport("libc", EntryPoint = "getenv", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetEnv(string name);

    [LibraryImport("libc", EntryPoint = "setenv", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int SetEnv(string name, string value, int overwrite);

    [LibraryImport("libc", EntryPoint = "unsetenv", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int UnsetEnv(string name);
}
