// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp.Tests;

public sealed class PreviewGraphicsDeviceFactoryTests
{
    [Test]
    public async Task RoutesToPlatformBackendWithoutCreatingNativeTestDevice()
    {
        string? selected = null;
        bool? warp = null;
        var factory = new PreviewGraphicsDeviceFactory(
            useWarp =>
            {
                selected = "d3d12";
                warp = useWarp;
                return null!;
            },
            () =>
            {
                selected = "vulkan";
                return null!;
            },
            () =>
            {
                selected = "metal";
                return null!;
            });

        _ = factory.Create(new PreviewGraphicsDeviceOptions(UseWarpOnWindows: true));

        string expected = OperatingSystem.IsWindows()
            ? "d3d12"
            : OperatingSystem.IsLinux()
                ? "vulkan"
                : "metal";
        await Assert.That(selected).IsEqualTo(expected);
        if (OperatingSystem.IsWindows())
        {
            await Assert.That(warp).IsTrue();
        }
    }
}
