// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk.Vulkan;

internal static class VulkanLoaderLibrary
{
    internal const string PathEnvironmentVariable = "OPENUSD_VULKAN_LOADER_PATH";

    internal static string[] GetCandidateNames()
    {
        string? configured = Environment.GetEnvironmentVariable(PathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!Path.IsPathFullyQualified(configured))
            {
                throw new InvalidOperationException(
                    $"{PathEnvironmentVariable} must contain an absolute path.");
            }

            string fullPath = Path.GetFullPath(configured);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    $"The configured Vulkan loader does not exist: {fullPath}",
                    fullPath);
            }
            return [fullPath];
        }

        if (OperatingSystem.IsWindows())
        {
            return ["vulkan-1.dll"];
        }
        if (OperatingSystem.IsMacOS())
        {
            return ["libvulkan.1.dylib", "libvulkan.dylib"];
        }
        return ["libvulkan.so.1", "libvulkan.so"];
    }
}
