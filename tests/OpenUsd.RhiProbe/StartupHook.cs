// Copyright (c) marcschier. Licensed under the MIT License.

#pragma warning disable CA1050

/// <summary>
/// Loads the locked Vulkan loader before a framework-dependent test assembly starts.
/// </summary>
public static class StartupHook
{
    /// <summary>Loads and verifies the configured Vulkan loader.</summary>
    public static void Initialize()
    {
        foreach (string argument in Environment.GetCommandLineArgs())
        {
            string name = Path.GetFileName(argument);
            if (string.Equals(
                    name,
                    "OpenUsd.RhiProbe.dll",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    name,
                    "OpenUsd.Rendering.ConformanceTests.dll",
                    StringComparison.OrdinalIgnoreCase))
            {
                OpenUsd.RhiProbe.VulkanRuntimeLoader.EnsureLoaded();
                return;
            }
        }
    }
}

#pragma warning restore CA1050
