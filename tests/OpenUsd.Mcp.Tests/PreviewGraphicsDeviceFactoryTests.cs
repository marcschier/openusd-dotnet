// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp.Tests;

public sealed class PreviewGraphicsDeviceFactoryTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task LegacyInjectedFactoriesCannotSilentlyIgnoreGpuUploadRequirements(bool useWarp)
    {
        int calls = 0;
        var factory = new PreviewGraphicsDeviceFactory(
            _ => { calls++; return null!; },
            () => { calls++; return null!; },
            () => { calls++; return null!; });
        await Assert.That(() => factory.Create(
            new PreviewGraphicsDeviceOptions(useWarp) { D3D12GpuUploadHeaps = true }))
            .Throws<NotSupportedException>();
        await Assert.That(calls).IsEqualTo(0);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task RoutesToPlatformBackendWithoutCreatingNativeTestDevice(bool useWarp)
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

        _ = factory.Create(new PreviewGraphicsDeviceOptions(UseWarpOnWindows: useWarp));

        string expected = OperatingSystem.IsWindows()
            ? "d3d12"
            : OperatingSystem.IsLinux()
                ? "vulkan"
                : "metal";
        await Assert.That(selected).IsEqualTo(expected);
        if (OperatingSystem.IsWindows())
        {
            await Assert.That(warp).IsEqualTo(useWarp);
        }
    }
}
