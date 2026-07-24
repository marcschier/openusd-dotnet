// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace OpenUsd.RhiProbe;

internal static partial class VulkanRuntimeLoader
{
    internal const string ApiVersionVariable = "OPENUSD_VULKAN_API_VERSION";
    internal const string DriverHashVariable = "OPENUSD_VULKAN_DRIVER_SHA256";
    internal const string DriverPathVariable = "OPENUSD_VULKAN_DRIVER_PATH";
    internal const string LoaderHashVariable = "OPENUSD_VULKAN_LOADER_SHA256";
    internal const string LoaderPathVariable = "OPENUSD_VULKAN_LOADER_PATH";
    internal const string ManifestPathVariable = "OPENUSD_VULKAN_MANIFEST_PATH";
    internal const string RequireSwiftShaderVariable =
        "OPENUSD_REQUIRE_SWIFTSHADER";

    private static nint _loader;

    internal static string? LoadedPath { get; private set; }

    internal static void EnsureLoaded()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(RequireSwiftShaderVariable),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        string loaderPath = RequireAbsoluteFile(LoaderPathVariable);
        AssertSha256(
            loaderPath,
            RequireEnvironmentVariable(LoaderHashVariable),
            "Vulkan loader");
        bool loaded = false;
        if (_loader == 0)
        {
            _loader = NativeLibrary.Load(loaderPath);
            loaded = true;
        }

        LoadedPath = OperatingSystem.IsWindows()
            ? GetWindowsModulePath(_loader)
            : loaderPath;
        if (!PathsEqual(LoadedPath, loaderPath))
        {
            throw new InvalidOperationException(
                $"Loaded Vulkan loader '{LoadedPath}' does not match " +
                $"the locked loader '{loaderPath}'.");
        }
        if (loaded)
        {
            Console.WriteLine($"VULKAN_LOADER path={LoadedPath}");
        }
    }

    internal static void AssertSha256(
        string path,
        string expected,
        string description)
    {
        using FileStream stream = File.OpenRead(path);
        string actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{description} hash mismatch for '{path}'. " +
                $"Expected {expected}, got {actual}.");
        }
    }

    internal static string RequireAbsoluteFile(string variable)
    {
        string path = RequireEnvironmentVariable(variable);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException(
                $"{variable} must be an absolute path, but was '{path}'.");
        }
        path = Path.GetFullPath(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"{variable} does not identify a file.",
                path);
        }
        return path;
    }

    internal static string RequireEnvironmentVariable(string variable) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"{variable} must be set for the locked Vulkan runtime.");

    internal static bool PathsEqual(string left, string right)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            comparison);
    }

    private static unsafe string GetWindowsModulePath(nint module)
    {
        Span<char> path = stackalloc char[32768];
        fixed (char* pointer = path)
        {
            uint length = GetModuleFileName(module, pointer, (uint)path.Length);
            if (length == 0 || length == path.Length)
            {
                throw new InvalidOperationException(
                    $"GetModuleFileNameW failed with error " +
                    $"{Marshal.GetLastPInvokeError()}.");
            }
            return new string(path[..checked((int)length)]);
        }
    }

    [LibraryImport(
        "kernel32.dll",
        EntryPoint = "GetModuleFileNameW",
        SetLastError = true)]
    private static unsafe partial uint GetModuleFileName(
        nint module,
        char* path,
        uint size);
}
