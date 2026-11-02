// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

internal static class SilkConformanceBackends
{
    internal static IReadOnlyList<SilkGraphicsBackend> Available() =>
        OperatingSystem.IsWindows()
            ? [SilkGraphicsBackend.D3D12, SilkGraphicsBackend.Vulkan]
            : OperatingSystem.IsLinux()
                ? [SilkGraphicsBackend.Vulkan]
                : OperatingSystem.IsMacOS()
                    ? [SilkGraphicsBackend.Metal]
                    : throw new PlatformNotSupportedException("Conformance requires D3D12, Vulkan, or Metal.");
}
