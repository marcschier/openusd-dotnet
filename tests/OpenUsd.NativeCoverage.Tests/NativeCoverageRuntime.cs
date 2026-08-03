// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.NativeCoverage.Tests;

internal static class NativeCoverageRuntime
{
    private static readonly Lock Sync = new();
    private static bool _registered;

    public static string CreateTempDirectory(string testName)
    {
        EnsureNativeLoaded();
        string? configuredRoot = Environment.GetEnvironmentVariable("OPENUSD_TEST_WORK_ROOT");
        string root = configuredRoot ?? string.Empty;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(AppContext.BaseDirectory, "native-work");
        }

        string directory = Path.Combine(
            root,
            testName,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static void EnsureNativeLoaded()
    {
        lock (Sync)
        {
            if (_registered)
            {
                return;
            }

            string? pluginPath = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
            if (string.IsNullOrWhiteSpace(pluginPath))
            {
                throw new InvalidOperationException(
                    "OpenUSD native runtime is not staged for OpenUsd.NativeCoverage.Tests. " +
                    "Run eng/run-native-managed-tests.ps1 so OPENUSD_TEST_PLUGIN_PATH and the native " +
                    "library search path are configured.");
            }

            try
            {
                _ = OpenUsdNativeRuntime.AbiVersion;
            }
            catch (DllNotFoundException exception)
            {
                throw new InvalidOperationException(
                    "OpenUSD native runtime could not be loaded for OpenUsd.NativeCoverage.Tests. " +
                    "Run eng/run-native-managed-tests.ps1 or restore/build native/install/<rid> " +
                    "and native/install/shim/<rid> first.",
                    exception);
            }

            nuint pluginCount = OpenUsdNativeRuntime.RegisterPlugins(pluginPath);
            if (pluginCount == 0)
            {
                throw new InvalidOperationException(
                    $"OpenUSD native runtime loaded, but no plugins were registered from '{pluginPath}'.");
            }

            _registered = true;
        }
    }
}
