// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

internal static class ViewerNativeTestStages
{
    internal static string RequirePluginPathOrSkip()
    {
        string? plugins = Environment.GetEnvironmentVariable("OPENUSD_PLUGIN_PATH") ??
            Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
        if (plugins is null)
        {
            if (Environment.GetEnvironmentVariable("OPENUSD_VIEWER_TEST_NATIVE_ROOT") is not null)
            {
                throw new InvalidOperationException(
                    "The configured Viewer native runtime requires OPENUSD_PLUGIN_PATH or OPENUSD_TEST_PLUGIN_PATH.");
            }
            Skip.Test("Set OPENUSD_PLUGIN_PATH or OPENUSD_TEST_PLUGIN_PATH for the native portable-review tests.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(plugins);
        if (!Path.IsPathFullyQualified(plugins))
        {
            throw new ArgumentException("The configured native plugin path must be absolute.", nameof(plugins));
        }
        if (!Directory.Exists(plugins))
        {
            throw new DirectoryNotFoundException("The configured native plugin directory does not exist.");
        }
        return plugins;
    }

    internal static UsdStageScheduler OpenSchedulerOrSkip(string path)
    {
        try
        {
            using UsdStage probe = UsdStage.Open(path);
        }
        catch (DllNotFoundException exception) when (
            Environment.GetEnvironmentVariable("OPENUSD_VIEWER_TEST_NATIVE_ROOT") is null)
        {
            Skip.Test($"openusd_dotnet native runtime is unavailable: {exception.Message}");
            throw;
        }
        return UsdStageScheduler.Open(path);
    }
}
