// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;
using OpenUsd.Rendering.Silk.Metal;
using OpenUsd.Rendering.Silk.Vulkan;

namespace OpenUsd.Mcp;

public sealed record PreviewGraphicsDeviceOptions(bool UseWarpOnWindows = true);

public interface IPreviewGraphicsDeviceFactory
{
    ISilkGraphicsDevice Create(PreviewGraphicsDeviceOptions options);
}

public sealed class PreviewGraphicsDeviceFactory : IPreviewGraphicsDeviceFactory
{
    private readonly Func<bool, ISilkGraphicsDevice> _createD3D12;
    private readonly Func<ISilkGraphicsDevice> _createVulkan;
    private readonly Func<ISilkGraphicsDevice> _createMetal;

    public PreviewGraphicsDeviceFactory()
        : this(CreateD3D12, CreateVulkan, CreateMetal)
    {
    }

    public PreviewGraphicsDeviceFactory(
        Func<bool, ISilkGraphicsDevice> createD3D12,
        Func<ISilkGraphicsDevice> createVulkan,
        Func<ISilkGraphicsDevice> createMetal)
    {
        _createD3D12 = createD3D12 ?? throw new ArgumentNullException(nameof(createD3D12));
        _createVulkan = createVulkan ?? throw new ArgumentNullException(nameof(createVulkan));
        _createMetal = createMetal ?? throw new ArgumentNullException(nameof(createMetal));
    }

    public ISilkGraphicsDevice Create(PreviewGraphicsDeviceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (OperatingSystem.IsWindows())
        {
            return _createD3D12(options.UseWarpOnWindows);
        }

        if (OperatingSystem.IsLinux())
        {
            return _createVulkan();
        }

        if (OperatingSystem.IsMacOS())
        {
            return _createMetal();
        }

        throw new PlatformNotSupportedException(
            "Preview capture supports D3D12 on Windows, Vulkan on Linux, and Metal on macOS.");
    }

    private static ISilkGraphicsDevice CreateD3D12(bool useWarp)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException();
        }

        return D3D12SilkGraphicsDevice.Create(useWarp);
    }

    private static ISilkGraphicsDevice CreateVulkan()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException();
        }

        return VulkanSilkGraphicsDevice.Create();
    }

    private static ISilkGraphicsDevice CreateMetal()
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException();
        }

        return MetalSilkGraphicsDevice.Create();
    }
}
