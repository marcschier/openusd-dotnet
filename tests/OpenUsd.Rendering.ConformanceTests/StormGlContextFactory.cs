// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.ConformanceTests;

internal static class StormGlContextFactory
{
    internal static bool IsCurrentPlatformSupported =>
        OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    internal static IStormGlContextFactory CreateForCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsWglStormContextFactory();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxGlxStormContextFactory();
        }

        throw new PlatformNotSupportedException(
            "Storm parity capture requires either the Windows WGL shim or the Linux GLX shim.");
    }
}
