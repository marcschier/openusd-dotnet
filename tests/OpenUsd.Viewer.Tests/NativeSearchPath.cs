// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Points the native loader at the project-owned shim before any test loads it.
/// </summary>
/// <remarks>
/// The colour-management library and the project-owned C ABI live in the native install
/// tree, not in the test output, so without this the OpenColorIO tests in this assembly
/// skipped every time and proved nothing. A module initializer runs before any test body,
/// which is the only point at which changing the loader's search path still has an
/// effect.
/// </remarks>
internal static class NativeSearchPath
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string? root = FindRepositoryRoot();
        if (root is null)
        {
            return;
        }

        string[] directories =
        [
            Path.Combine(root, "native", "install", "shim", "win-x64", "bin"),
            Path.Combine(root, "native", "install", "win-x64", "bin"),
            Path.Combine(root, "native", "install", "win-x64", "lib"),
        ];
        string prefix = string.Join(
            Path.PathSeparator,
            directories.Where(Directory.Exists).Select(Path.GetFullPath));
        if (prefix.Length == 0)
        {
            return;
        }

        string current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (current.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        Environment.SetEnvironmentVariable("PATH", prefix + Path.PathSeparator + current);
    }

    private static string? FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        return null;
    }
}
