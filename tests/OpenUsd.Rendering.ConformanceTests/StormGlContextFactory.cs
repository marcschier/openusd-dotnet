// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.ConformanceTests;

internal static class StormGlContextFactory
{
    internal static bool IsCurrentPlatformSupported =>
        OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

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

        if (OperatingSystem.IsMacOS())
        {
            return new MacosCglStormContextFactory();
        }

        throw new PlatformNotSupportedException(
            "Storm parity capture requires the Windows WGL, Linux GLX, or macOS CGL shim.");
    }
}
